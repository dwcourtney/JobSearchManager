#!/usr/bin/env python3
"""Private Phase 3 LLM adapter. Production regex scoring remains authoritative."""
from __future__ import annotations

import argparse, hashlib, json, os, threading, time, urllib.error, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

SERVICE_VERSION, PROTOCOL_VERSION = "0.5.0", "4"
MODEL_TYPE, MODEL_ID = "generative-llm", "google/gemma-3-4b-it"
MODEL_TAG = "gemma3:4b-it-q4_K_M"
MODEL_DIGEST = "sha256:a2af6cc3eb7fa8be8504abaf9b04e88f17a119ec3f04a3addf55f92841195f5a"
MODEL_QUANTIZATION, OLLAMA_VERSION = "Q4_K_M", "0.33.2"
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://ollama:11434").rstrip("/")
EXPECTED_DEVICE_NAME = os.environ.get("CLASSIFIER_EXPECTED_DEVICE_NAME", "NVIDIA GeForce GTX 1070")
MAX_BODY_BYTES, MAX_TEXT_CHARACTERS = 2_000_000, 500_000
CONTEXT_LENGTH, MAX_OUTPUT_TOKENS, SEED, TEMPERATURE = 8192, 384, 42, 0
PROMPT_VERSION = "phase3-zero-shot-v1"
INFERENCE_LOCK = threading.Lock()
CONCEPTS = (
    ("role.ai-ml-engineering", "Hands-on engineering that builds, integrates, or operationalizes machine-learning models, AI systems, pipelines, or production AI applications."),
    ("role.software-engineering", "Direct design, implementation, testing, and maintenance of software systems as an engineering responsibility."),
    ("technical.software-development", "Hands-on implementation, testing, debugging, or maintenance of software applications, services, or systems."),
    ("technical.backend-development", "Server-side software development involving services, business logic, databases, microservices, APIs, and backend systems."),
    ("technical.api-development", "Direct design, implementation, integration, operation, or maintenance of programmatic service interfaces and APIs."),
    ("technical.automation-scripting", "Automating technical workflows, deployments, operations, testing, or repetitive tasks with scripts or software tooling."),
    ("role.cloud-engineering", "Hands-on design, implementation, operation, or reliability engineering of cloud infrastructure, services, and platforms."),
    ("technical.containers", "Direct implementation or operation of containerization and orchestration using Docker, Kubernetes, or related platforms."),
)
CONCEPT_IDS = tuple(item[0] for item in CONCEPTS)
OUTPUT_SCHEMA = {"type": "object", "properties": {key: {"type": "boolean"} for key in CONCEPT_IDS},
                 "required": list(CONCEPT_IDS), "additionalProperties": False}
SYSTEM_PROMPT = """You are a careful job-posting responsibility classifier.
Classify the role itself, not technologies merely mentioned as products, customer environments,
desired awareness, team context, or work managed by someone else. A label is true only when the
posting assigns the candidate direct, hands-on responsibility matching its definition.
Return exactly the requested JSON object with one boolean for every concept. Do not add prose."""
PROMPT_HASH = hashlib.sha256(json.dumps({"version": PROMPT_VERSION, "system": SYSTEM_PROMPT,
    "concepts": CONCEPTS, "schema": OUTPUT_SCHEMA}, sort_keys=True, separators=(",", ":")).encode()).hexdigest()

class InferenceOutputError(Exception): pass

def ollama_json(path: str, payload: dict[str, Any] | None = None, timeout: int = 5) -> dict[str, Any]:
    body = None if payload is None else json.dumps(payload, separators=(",", ":")).encode()
    request = urllib.request.Request(f"{OLLAMA_URL}{path}", data=body,
        headers={"Content-Type": "application/json"} if body is not None else {},
        method="POST" if body is not None else "GET")
    with urllib.request.urlopen(request, timeout=timeout) as response:
        value = json.load(response)
    if not isinstance(value, dict): raise RuntimeError("Ollama returned a non-object response.")
    return value

def model_digest_matches(value: Any) -> bool:
    return isinstance(value, str) and f"sha256:{value.removeprefix('sha256:')}" == MODEL_DIGEST

def model_available() -> bool:
    try:
        return any(item.get("name") == MODEL_TAG and model_digest_matches(item.get("digest"))
                   for item in ollama_json("/api/tags").get("models", []))
    except (OSError, ValueError, urllib.error.URLError): return False

def runtime_diagnostic() -> dict[str, Any]:
    unavailable = {"gpuAvailable": False, "deviceCount": 0, "deviceName": None,
                   "vramTotalMiB": None, "vramUsedMiB": None, "driverVersion": None}
    try:
        active = next((item for item in ollama_json("/api/ps").get("models", [])
                       if item.get("name") == MODEL_TAG), None)
        if not active or not isinstance(active.get("size_vram"), int) or active["size_vram"] <= 0:
            return unavailable
        return {"gpuAvailable": True, "deviceCount": 1, "deviceName": EXPECTED_DEVICE_NAME,
                "vramTotalMiB": None, "vramUsedMiB": round(active["size_vram"] / 1048576),
                "driverVersion": None}
    except (OSError, ValueError, urllib.error.URLError): return unavailable

def identity() -> dict[str, Any]:
    return {"serviceVersion": SERVICE_VERSION, "protocolVersion": PROTOCOL_VERSION,
            "revision": os.environ.get("CLASSIFIER_GIT_SHA", "unknown"), **runtime_diagnostic()}

def user_prompt(title: str, description: str) -> str:
    definitions = "\n".join(f"- {key}: {definition}" for key, definition in CONCEPTS)
    return f"Concept definitions:\n{definitions}\n\nJob title:\n{title}\n\nFull job posting:\n{description}\n\nClassify all eight concepts according to actual candidate responsibilities."

def validate_output(value: Any) -> dict[str, bool]:
    if not isinstance(value, dict) or set(value) != set(CONCEPT_IDS):
        raise ValueError("Model output did not contain exactly the canonical concept keys.")
    if any(type(value[key]) is not bool for key in CONCEPT_IDS):
        raise ValueError("Every model output value must be a boolean.")
    return {key: value[key] for key in CONCEPT_IDS}

def classify(title: str, description: str) -> dict[str, Any]:
    request = {"model": MODEL_TAG,
        "messages": [{"role": "system", "content": SYSTEM_PROMPT},
                     {"role": "user", "content": user_prompt(title, description)}],
        "format": OUTPUT_SCHEMA, "stream": False, "keep_alive": -1,
        "options": {"temperature": TEMPERATURE, "seed": SEED,
                    "num_ctx": CONTEXT_LENGTH, "num_predict": MAX_OUTPUT_TOKENS}}
    started = time.perf_counter()
    with INFERENCE_LOCK: result = ollama_json("/api/chat", request, timeout=300)
    content = result.get("message", {}).get("content")
    if result.get("done") is not True or not isinstance(content, str):
        raise InferenceOutputError("Ollama response was incomplete.")
    try: predictions = validate_output(json.loads(content))
    except (ValueError, json.JSONDecodeError) as error:
        raise InferenceOutputError("Model output failed strict JSON validation.") from error
    eval_count, eval_duration = result.get("eval_count"), result.get("eval_duration")
    tokens_per_second = (eval_count * 1_000_000_000 / eval_duration
        if isinstance(eval_count, int) and isinstance(eval_duration, int) and eval_duration > 0 else None)
    return {"modelType": MODEL_TYPE, "modelId": MODEL_ID, "modelTag": MODEL_TAG,
        "modelDigest": MODEL_DIGEST, "quantization": MODEL_QUANTIZATION,
        "ollamaVersion": OLLAMA_VERSION, "device": "cuda:0",
        "promptVersion": PROMPT_VERSION, "promptHash": PROMPT_HASH,
        "temperature": TEMPERATURE, "seed": SEED, "contextLength": CONTEXT_LENGTH,
        "maxOutputTokens": MAX_OUTPUT_TOKENS, "totalDurationNanoseconds": result.get("total_duration"),
        "loadDurationNanoseconds": result.get("load_duration"),
        "promptTokenCount": result.get("prompt_eval_count"), "outputTokenCount": eval_count,
        "tokensPerSecond": tokens_per_second,
        "inferenceMilliseconds": (time.perf_counter() - started) * 1000,
        "malformedOutputCount": 0,
        "predictions": [{"conceptId": key, "matched": predictions[key]} for key in CONCEPT_IDS]}

class Handler(BaseHTTPRequestHandler):
    server_version = "JsmClassifier/0.4"
    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"{self.address_string()} {fmt % args}", flush=True)
    def send_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode()
        self.send_response(status); self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body))); self.send_header("Cache-Control", "no-store")
        self.end_headers(); self.wfile.write(body)
    def read_json(self) -> dict[str, Any]:
        try: length = int(self.headers.get("Content-Length", "-1"))
        except ValueError as error: raise ValueError("Invalid Content-Length.") from error
        if length < 0 or length > MAX_BODY_BYTES: raise ValueError("Request body size is invalid.")
        value = json.loads(self.rfile.read(length))
        if not isinstance(value, dict): raise ValueError("JSON object required.")
        return value
    def do_GET(self) -> None:  # noqa: N802
        if self.path != "/healthz": self.send_json(404, {"error": "not found"}); return
        self.send_json(200, {"status": "healthy", **identity(), "modelType": MODEL_TYPE,
            "modelId": MODEL_ID, "modelTag": MODEL_TAG, "modelDigest": MODEL_DIGEST,
            "modelAvailable": model_available(), "promptVersion": PROMPT_VERSION, "promptHash": PROMPT_HASH})
    def do_POST(self) -> None:  # noqa: N802
        if self.path not in ("/classify", "/classify-llm"):
            self.send_json(404, {"error": "not found"}); return
        try:
            request = self.read_json()
            job_id, title, description = (request.get(key) for key in ("jobId", "title", "description"))
            invalid = [name for name, value in (("jobId", job_id), ("title", title))
                       if not isinstance(value, str) or not value.strip()]
            if not isinstance(description, str): invalid.append("description")
            if invalid: self.send_json(400, {"error": "invalid request", "fields": invalid}); return
            if len(title) + len(description) > MAX_TEXT_CHARACTERS:
                self.send_json(413, {"error": "posting text is too large"}); return
            envelope = {"received": True, "jobId": job_id, "title": title,
                        "descriptionLength": len(description)}
            if self.path == "/classify": self.send_json(200, {**envelope, **identity()}); return
            if not model_available():
                self.send_json(503, {"error": "pinned model is unavailable", "modelAvailable": False,
                    "modelTag": MODEL_TAG, "modelDigest": MODEL_DIGEST}); return
            result = classify(title, description)
            self.send_json(200, {**envelope, **identity(), **result})
        except InferenceOutputError:
            self.send_json(503, {"error": "model output is invalid", "malformedOutputCount": 1})
        except (ValueError, json.JSONDecodeError): self.send_json(400, {"error": "invalid request"})
        except Exception as error:
            print(f"llm failure type={type(error).__name__}", flush=True)
            self.send_json(503, {"error": "model inference is unavailable"})

def self_test() -> None:
    assert len(CONCEPTS) == len(set(CONCEPT_IDS)) == 8
    assert len(PROMPT_HASH) == 64 and OUTPUT_SCHEMA["required"] == list(CONCEPT_IDS)
    assert model_digest_matches(MODEL_DIGEST) and model_digest_matches(MODEL_DIGEST.removeprefix("sha256:"))
    assert not model_digest_matches("0" * 64)
    all_false = validate_output({key: False for key in CONCEPT_IDS})
    assert list(all_false) == list(CONCEPT_IDS) and not any(all_false.values())
    try: validate_output({CONCEPT_IDS[0]: True}); raise AssertionError("Incomplete output passed validation.")
    except ValueError: pass
    assert "actual candidate responsibilities" in user_prompt("title", "description")
    print("LLM schema, prompt, identity, and strict-output self-test: PASS")

def main() -> None:
    parser = argparse.ArgumentParser()
    for flag in ("healthcheck", "model-diagnostic", "self-test"):
        parser.add_argument(f"--{flag}", action="store_true")
    options = parser.parse_args()
    if options.self_test: self_test()
    elif options.model_diagnostic:
        result = classify("Backend API Engineer", "Build Python APIs in Docker on AWS.")
        print(json.dumps({**identity(), **result}))
    elif options.healthcheck:
        try:
            with urllib.request.urlopen("http://127.0.0.1:8081/healthz", timeout=3) as response:
                raise SystemExit(0 if response.status == 200 else 1)
        except OSError: raise SystemExit(1)
    else: ThreadingHTTPServer(("0.0.0.0", 8081), Handler).serve_forever()

if __name__ == "__main__": main()
