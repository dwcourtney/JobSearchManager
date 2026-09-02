#!/usr/bin/env python3
"""Private opt-in Qwen deep-analysis adapter. Default Job Fit runs in-process RegEx."""
from __future__ import annotations
import argparse, datetime, hashlib, json, os, threading, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
try:
    import resource
except ImportError:  # pragma: no cover - Windows-only local self-test path
    resource = None

SERVICE_VERSION, PROTOCOL_VERSION = "3.2.0", "9"
MAX_BODY_BYTES, MAX_TEXT_CHARACTERS = 2_000_000, 500_000
QWEN_MODEL_ID = "Qwen/Qwen3-4B-Instruct-2507"
QWEN_MODEL_TAG = "qwen3:4b-instruct-2507-q4_K_M"
QWEN_MODEL_DIGEST = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://ollama:11434").rstrip("/")
QWEN_PROMPT_VERSION = "job-fit-85-compact-json-v2"
QWEN_OUTPUT_CONTRACT_VERSION = "compact-85-boolean-map-v2"
QWEN_CONTEXT_LENGTH, QWEN_MAX_OUTPUT_TOKENS, QWEN_SEED, QWEN_TEMPERATURE = 8192, 3072, 42, 0
QWEN_INFERENCE_LOCK = threading.Lock()
DEFAULT_CATALOG_PATH = Path("/app/JobConceptCatalog.json") if Path("/app/JobConceptCatalog.json").exists() else Path(__file__).resolve().parents[1] / "JobConceptCatalog.json"
CATALOG_PATH = Path(os.environ.get("CLASSIFIER_CATALOG_PATH", str(DEFAULT_CATALOG_PATH)))
QWEN_SYSTEM_PROMPT = """You are a careful job-posting responsibility classifier.
Classify the role itself, not technologies merely mentioned as products, customer environments,
desired awareness, qualifications without assigned duties, team context, or work managed by someone else.
A label is true only when the posting assigns the candidate responsibility or a work condition matching
its definition. Multiple overlapping labels may be true. Return exactly the requested compact JSON object
with one boolean for every canonical concept. Do not provide analysis, explanations, or prose."""

def sha256(value: bytes | str) -> str:
    return hashlib.sha256(value.encode() if isinstance(value, str) else value).hexdigest()

def load_catalog() -> tuple[int, str, tuple[tuple[str, str, str, str], ...]]:
    raw = CATALOG_PATH.read_bytes()
    document = json.loads(raw)
    concepts = tuple((item["id"], item["displayName"], item["category"], item["definition"]) for item in document.get("concepts"))
    if not isinstance(document.get("version"), int) or len(concepts) != 85 or len(set(x[0] for x in concepts)) != 85 or any(not all(x) for x in concepts):
        raise RuntimeError("The deep-analysis adapter requires exactly 85 canonical concepts.")
    return document["version"], sha256(raw.replace(b"\r\n", b"\n")), concepts

TAXONOMY_VERSION, TAXONOMY_FINGERPRINT, CONCEPTS = load_catalog()
CONCEPT_IDS = tuple(item[0] for item in CONCEPTS)
QWEN_OUTPUT_SCHEMA = {"type": "object", "properties": {
    "concepts": {"type": "object", "properties": {key: {"type": "boolean"} for key in CONCEPT_IDS},
                 "required": list(CONCEPT_IDS), "additionalProperties": False}},
    "required": ["concepts"], "additionalProperties": False}
QWEN_OUTPUT_SCHEMA_HASH = sha256(json.dumps(QWEN_OUTPUT_SCHEMA, sort_keys=True, separators=(",", ":")))
QWEN_PROMPT_HASH = sha256("\n".join((QWEN_PROMPT_VERSION, QWEN_SYSTEM_PROMPT,
    TAXONOMY_FINGERPRINT, QWEN_OUTPUT_CONTRACT_VERSION, QWEN_OUTPUT_SCHEMA_HASH)))

def posting_content_hash(title: str, description: str) -> str:
    return sha256(f"{title}\n{description}")

def qwen_classification_fingerprint(content_hash: str) -> str:
    return sha256("\n".join((content_hash, str(TAXONOMY_VERSION), TAXONOMY_FINGERPRINT,
        QWEN_MODEL_ID, QWEN_MODEL_TAG, QWEN_MODEL_DIGEST, QWEN_PROMPT_VERSION, QWEN_PROMPT_HASH,
        QWEN_OUTPUT_CONTRACT_VERSION, QWEN_OUTPUT_SCHEMA_HASH,
        str(QWEN_TEMPERATURE), str(QWEN_SEED), str(QWEN_CONTEXT_LENGTH), str(QWEN_MAX_OUTPUT_TOKENS))))

def qwen_user_prompt(title: str, description: str) -> str:
    definitions = "\n".join(f"- {cid} [{category}] {name}: {definition}" for cid, name, category, definition in CONCEPTS)
    return (f"Canonical concept definitions:\n{definitions}\n\nJob title:\n{title}\n\nFull job posting:\n{description}\n\n"
            "Classify all 85 concepts according to actual candidate responsibilities and work conditions. "
            "Return only the compact structured boolean map required by the schema.")

def identity() -> dict[str, Any]:
    return {"serviceVersion": SERVICE_VERSION, "protocolVersion": PROTOCOL_VERSION,
        "revision": os.environ.get("CLASSIFIER_GIT_SHA", "unknown"), "purpose": "opt-in-llm-deep-analysis",
        "modelId": QWEN_MODEL_ID, "modelTag": QWEN_MODEL_TAG, "modelDigest": QWEN_MODEL_DIGEST,
        "taxonomyVersion": TAXONOMY_VERSION, "taxonomyFingerprint": TAXONOMY_FINGERPRINT,
        "conceptCount": len(CONCEPTS), "promptVersion": QWEN_PROMPT_VERSION, "promptHash": QWEN_PROMPT_HASH,
        "outputContractVersion": QWEN_OUTPUT_CONTRACT_VERSION, "outputSchemaHash": QWEN_OUTPUT_SCHEMA_HASH}

def ollama_residency() -> tuple[int | None, int | None]:
    try:
        with urllib.request.urlopen(f"{OLLAMA_URL}/api/ps", timeout=10) as response:
            models = json.load(response).get("models", [])
        model = next((item for item in models if item.get("name") == QWEN_MODEL_TAG), None)
        return ((model or {}).get("size"), (model or {}).get("size_vram"))
    except (OSError, ValueError, json.JSONDecodeError):
        return None, None

def qwen_deep_analysis(job_id: str, title: str, description: str) -> dict[str, Any]:
    request = urllib.request.Request(f"{OLLAMA_URL}/api/chat", data=json.dumps({
        "model": QWEN_MODEL_TAG,
        "messages": [{"role": "system", "content": QWEN_SYSTEM_PROMPT}, {"role": "user", "content": qwen_user_prompt(title, description)}],
        "format": QWEN_OUTPUT_SCHEMA, "stream": False, "keep_alive": -1,
        "options": {"temperature": QWEN_TEMPERATURE, "seed": QWEN_SEED, "num_ctx": QWEN_CONTEXT_LENGTH, "num_predict": QWEN_MAX_OUTPUT_TOKENS}
    }).encode(), headers={"Content-Type": "application/json"}, method="POST")
    with QWEN_INFERENCE_LOCK, urllib.request.urlopen(request, timeout=300) as response:
        response_value = json.load(response)
    content = response_value.get("message", {}).get("content")
    if response_value.get("done") is not True or response_value.get("done_reason") != "stop" or not isinstance(content, str):
        raise RuntimeError("LLM deep-analysis response was incomplete.")
    try:
        result = json.loads(content)
    except json.JSONDecodeError as error:
        raise RuntimeError("LLM deep-analysis response was not valid JSON.") from error
    predictions = result.get("concepts") if isinstance(result, dict) else None
    if not isinstance(predictions, dict) or set(predictions) != set(CONCEPT_IDS) or any(type(predictions[key]) is not bool for key in CONCEPT_IDS):
        raise RuntimeError("LLM deep-analysis response failed strict validation.")
    content_hash = posting_content_hash(title, description)
    model_size, model_vram = ollama_residency()
    eval_count, eval_duration = response_value.get("eval_count"), response_value.get("eval_duration")
    tokens_per_second = (eval_count * 1_000_000_000 / eval_duration
        if isinstance(eval_count, int) and isinstance(eval_duration, int) and eval_duration > 0 else None)
    return {"received": True, "jobId": job_id, "title": title, "modelId": QWEN_MODEL_ID,
        "modelTag": QWEN_MODEL_TAG, "modelDigest": QWEN_MODEL_DIGEST, "taxonomyVersion": TAXONOMY_VERSION,
        "taxonomyFingerprint": TAXONOMY_FINGERPRINT, "promptVersion": QWEN_PROMPT_VERSION,
        "promptHash": QWEN_PROMPT_HASH, "outputContractVersion": QWEN_OUTPUT_CONTRACT_VERSION,
        "outputSchemaHash": QWEN_OUTPUT_SCHEMA_HASH,
        "analyzedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "postingContentHash": content_hash, "classificationFingerprint": qwen_classification_fingerprint(content_hash),
        "predictions": [{"conceptId": key, "matched": predictions[key]} for key in CONCEPT_IDS],
        "analysis": "Compact deterministic 85-concept structured classification.",
        "inference": {"totalDurationNanoseconds": response_value.get("total_duration"),
            "loadDurationNanoseconds": response_value.get("load_duration"),
            "promptTokenCount": response_value.get("prompt_eval_count"),
            "promptDurationNanoseconds": response_value.get("prompt_eval_duration"),
            "outputTokenCount": eval_count, "outputDurationNanoseconds": eval_duration,
            "tokensPerSecond": tokens_per_second, "modelResidentBytes": model_size,
            "modelVramBytes": model_vram,
            "adapterPeakResidentBytes": (resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * 1024
                if resource is not None else None)}}

class Handler(BaseHTTPRequestHandler):
    server_version = "JsmDeepAnalysis/3.2"
    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"{self.address_string()} {fmt % args}", flush=True)
    def send_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)
    def read_request(self) -> tuple[str, str, str]:
        length = int(self.headers.get("Content-Length", "-1"))
        if length < 0 or length > MAX_BODY_BYTES:
            raise ValueError("Invalid request length.")
        value = json.loads(self.rfile.read(length))
        values = tuple(value.get(key) for key in ("jobId", "title", "description"))
        if not all(isinstance(item, str) for item in values) or not values[0].strip() or not values[1].strip() or len(values[1]) + len(values[2]) > MAX_TEXT_CHARACTERS:
            raise ValueError("Invalid request.")
        return values
    def do_GET(self) -> None:
        if self.path == "/healthz":
            self.send_json(200, {"status": "healthy", **identity()})
        else:
            self.send_json(404, {"error": "not found"})
    def do_POST(self) -> None:
        if self.path != "/deep-analyze":
            self.send_json(404, {"error": "not found"})
            return
        try:
            self.send_json(200, qwen_deep_analysis(*self.read_request()))
        except (ValueError, json.JSONDecodeError):
            self.send_json(400, {"error": "invalid request"})
        except Exception as error:
            print(f"deep-analysis failure type={type(error).__name__}", flush=True)
            self.send_json(503, {"error": "deep analysis unavailable"})

def self_test() -> None:
    assert len(CONCEPTS) == len(set(CONCEPT_IDS)) == 85
    assert QWEN_OUTPUT_SCHEMA["properties"]["concepts"]["required"] == list(CONCEPT_IDS)
    assert "analysis" not in QWEN_OUTPUT_SCHEMA["properties"]
    assert len(QWEN_PROMPT_HASH) == len(qwen_classification_fingerprint("a" * 64)) == 64
    assert "all 85 concepts" in qwen_user_prompt("title", "description")
    print("Opt-in Qwen deep-analysis adapter self-test: PASS")

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--healthcheck", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    options = parser.parse_args()
    if options.self_test:
        self_test()
    elif options.healthcheck:
        try:
            with urllib.request.urlopen("http://127.0.0.1:8081/healthz", timeout=3) as response:
                raise SystemExit(0 if response.status == 200 else 1)
        except OSError:
            raise SystemExit(1)
    else:
        ThreadingHTTPServer(("0.0.0.0", 8081), Handler).serve_forever()

if __name__ == "__main__":
    main()
