#!/usr/bin/env python3
"""Private production semantic classifier backed by the pinned local Qwen model."""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

SERVICE_VERSION, PROTOCOL_VERSION = "1.0.0", "5"
MODEL_TYPE, MODEL_ID = "generative-llm", "Qwen/Qwen3-4B-Instruct-2507"
MODEL_TAG = "qwen3:4b-instruct-2507-q4_K_M"
MODEL_DIGEST = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"
MODEL_QUANTIZATION, OLLAMA_VERSION = "Q4_K_M", "0.33.2"
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://ollama:11434").rstrip("/")
EXPECTED_DEVICE_NAME = os.environ.get("CLASSIFIER_EXPECTED_DEVICE_NAME", "NVIDIA GeForce GTX 1070")
DEFAULT_CATALOG_PATH = (Path("/app/JobConceptCatalog.json")
    if Path("/app/JobConceptCatalog.json").exists()
    else Path(__file__).resolve().parents[1] / "JobConceptCatalog.json")
CATALOG_PATH = Path(os.environ.get("CLASSIFIER_CATALOG_PATH", str(DEFAULT_CATALOG_PATH)))
MAX_BODY_BYTES, MAX_TEXT_CHARACTERS = 2_000_000, 500_000
CONTEXT_LENGTH, MAX_OUTPUT_TOKENS, SEED, TEMPERATURE = 8192, 2048, 42, 0
PROMPT_VERSION = "job-fit-85-zero-shot-v1"
INFERENCE_LOCK = threading.Lock()
SYSTEM_PROMPT = """You are a careful job-posting responsibility classifier.
Classify the role itself, not technologies merely mentioned as products, customer environments,
desired awareness, qualifications without assigned duties, team context, or work managed by someone else.
A label is true only when the posting assigns the candidate responsibility or a work condition matching
its definition. Multiple overlapping labels may be true. Return exactly the requested JSON object with
one boolean for every canonical concept. Do not add prose."""


def sha256(value: bytes | str) -> str:
    return hashlib.sha256(value.encode("utf-8") if isinstance(value, str) else value).hexdigest()


def load_catalog() -> tuple[int, str, tuple[tuple[str, str, str, str], ...]]:
    raw = CATALOG_PATH.read_bytes()
    document = json.loads(raw)
    version, values = document.get("version"), document.get("concepts")
    if not isinstance(version, int) or version < 1 or not isinstance(values, list):
        raise RuntimeError("The canonical Job Fit catalog is invalid.")
    concepts: list[tuple[str, str, str, str]] = []
    for item in values:
        fields = tuple(item.get(key) for key in ("id", "displayName", "category", "definition"))
        if any(not isinstance(value, str) or not value.strip() for value in fields):
            raise RuntimeError("A canonical Job Fit concept is missing semantic definition metadata.")
        concepts.append(fields)  # type: ignore[arg-type]
    if len(concepts) != 85 or len({item[0] for item in concepts}) != len(concepts):
        raise RuntimeError("The production classifier requires exactly 85 unique canonical concepts.")
    return version, sha256(raw.replace(b"\r\n", b"\n")), tuple(concepts)


TAXONOMY_VERSION, TAXONOMY_FINGERPRINT, CONCEPTS = load_catalog()
CONCEPT_IDS = tuple(item[0] for item in CONCEPTS)
OUTPUT_SCHEMA = {
    "type": "object",
    "properties": {key: {"type": "boolean"} for key in CONCEPT_IDS},
    "required": list(CONCEPT_IDS),
    "additionalProperties": False,
}
PROMPT_HASH = sha256(f"{PROMPT_VERSION}\n{SYSTEM_PROMPT}\n{TAXONOMY_FINGERPRINT}")


class InferenceOutputError(Exception):
    pass


def posting_content_hash(title: str, description: str) -> str:
    return sha256(f"{title}\n{description}")


def classification_fingerprint(content_hash: str) -> str:
    material = "\n".join((
        content_hash, str(TAXONOMY_VERSION), TAXONOMY_FINGERPRINT,
        MODEL_ID, MODEL_TAG, MODEL_DIGEST, PROMPT_VERSION, PROMPT_HASH,
        str(TEMPERATURE), str(SEED), str(CONTEXT_LENGTH), str(MAX_OUTPUT_TOKENS),
    ))
    return sha256(material)


def ollama_json(path: str, payload: dict[str, Any] | None = None, timeout: int = 5) -> dict[str, Any]:
    body = None if payload is None else json.dumps(payload, separators=(",", ":")).encode()
    request = urllib.request.Request(f"{OLLAMA_URL}{path}", data=body,
        headers={"Content-Type": "application/json"} if body is not None else {},
        method="POST" if body is not None else "GET")
    with urllib.request.urlopen(request, timeout=timeout) as response:
        value = json.load(response)
    if not isinstance(value, dict):
        raise RuntimeError("Ollama returned a non-object response.")
    return value


def model_digest_matches(value: Any) -> bool:
    return isinstance(value, str) and f"sha256:{value.removeprefix('sha256:')}" == MODEL_DIGEST


def model_available() -> bool:
    try:
        return any(item.get("name") == MODEL_TAG and model_digest_matches(item.get("digest"))
                   for item in ollama_json("/api/tags").get("models", []))
    except (OSError, ValueError, urllib.error.URLError):
        return False


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
    except (OSError, ValueError, urllib.error.URLError):
        return unavailable


def identity() -> dict[str, Any]:
    return {"serviceVersion": SERVICE_VERSION, "protocolVersion": PROTOCOL_VERSION,
            "revision": os.environ.get("CLASSIFIER_GIT_SHA", "unknown"),
            "modelType": MODEL_TYPE, "modelId": MODEL_ID, "modelTag": MODEL_TAG,
            "modelDigest": MODEL_DIGEST, "quantization": MODEL_QUANTIZATION,
            "ollamaVersion": OLLAMA_VERSION, "taxonomyVersion": TAXONOMY_VERSION,
            "taxonomyFingerprint": TAXONOMY_FINGERPRINT, "conceptCount": len(CONCEPTS),
            "promptVersion": PROMPT_VERSION, "promptHash": PROMPT_HASH,
            "temperature": TEMPERATURE, "seed": SEED, "contextLength": CONTEXT_LENGTH,
            "maxOutputTokens": MAX_OUTPUT_TOKENS, **runtime_diagnostic()}


def user_prompt(title: str, description: str) -> str:
    definitions = "\n".join(
        f"- {concept_id} [{category}] {name}: {definition}"
        for concept_id, name, category, definition in CONCEPTS)
    return (f"Canonical concept definitions:\n{definitions}\n\nJob title:\n{title}\n\n"
            f"Full job posting:\n{description}\n\n"
            "Classify all 85 concepts according to actual candidate responsibilities and work conditions.")


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
    with INFERENCE_LOCK:
        result = ollama_json("/api/chat", request, timeout=300)
    content = result.get("message", {}).get("content")
    if result.get("done") is not True or not isinstance(content, str):
        raise InferenceOutputError("Ollama response was incomplete.")
    try:
        predictions = validate_output(json.loads(content))
    except (ValueError, json.JSONDecodeError) as error:
        raise InferenceOutputError("Model output failed strict JSON validation.") from error
    eval_count, eval_duration = result.get("eval_count"), result.get("eval_duration")
    tokens_per_second = (eval_count * 1_000_000_000 / eval_duration
        if isinstance(eval_count, int) and isinstance(eval_duration, int) and eval_duration > 0 else None)
    content_hash = posting_content_hash(title, description)
    return {**identity(), "postingContentHash": content_hash,
        "classificationFingerprint": classification_fingerprint(content_hash),
        "classifiedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "device": "cuda:0",
        "totalDurationNanoseconds": result.get("total_duration"),
        "loadDurationNanoseconds": result.get("load_duration"),
        "promptTokenCount": result.get("prompt_eval_count"), "outputTokenCount": eval_count,
        "tokensPerSecond": tokens_per_second,
        "inferenceMilliseconds": (time.perf_counter() - started) * 1000,
        "malformedOutputCount": 0,
        "predictions": [{"conceptId": key, "matched": predictions[key]} for key in CONCEPT_IDS]}


class Handler(BaseHTTPRequestHandler):
    server_version = "JsmClassifier/1.0"
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
        self.send_json(200, {"status": "healthy", "modelAvailable": model_available(), **identity()})
    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/classify": self.send_json(404, {"error": "not found"}); return
        try:
            request = self.read_json()
            job_id, title, description = (request.get(key) for key in ("jobId", "title", "description"))
            invalid = [name for name, value in (("jobId", job_id), ("title", title))
                       if not isinstance(value, str) or not value.strip()]
            if not isinstance(description, str): invalid.append("description")
            if invalid: self.send_json(400, {"error": "invalid request", "fields": invalid}); return
            if len(title) + len(description) > MAX_TEXT_CHARACTERS:
                self.send_json(413, {"error": "posting text is too large"}); return
            if not model_available():
                self.send_json(503, {"error": "pinned model is unavailable", "modelAvailable": False,
                    "modelTag": MODEL_TAG, "modelDigest": MODEL_DIGEST}); return
            result = classify(title, description)
            self.send_json(200, {"received": True, "jobId": job_id, "title": title,
                "descriptionLength": len(description), **result})
        except InferenceOutputError:
            self.send_json(503, {"error": "model output is invalid", "malformedOutputCount": 1})
        except (ValueError, json.JSONDecodeError): self.send_json(400, {"error": "invalid request"})
        except Exception as error:
            print(f"classifier failure type={type(error).__name__}", flush=True)
            self.send_json(503, {"error": "model inference is unavailable"})


def self_test() -> None:
    assert len(CONCEPTS) == len(set(CONCEPT_IDS)) == 85
    assert len(TAXONOMY_FINGERPRINT) == len(PROMPT_HASH) == 64
    assert OUTPUT_SCHEMA["required"] == list(CONCEPT_IDS)
    assert model_digest_matches(MODEL_DIGEST) and model_digest_matches(MODEL_DIGEST.removeprefix("sha256:"))
    assert not model_digest_matches("0" * 64)
    all_false = validate_output({key: False for key in CONCEPT_IDS})
    assert list(all_false) == list(CONCEPT_IDS) and not any(all_false.values())
    try: validate_output({CONCEPT_IDS[0]: True}); raise AssertionError("Incomplete output passed validation.")
    except ValueError: pass
    content_hash = posting_content_hash("title", "description")
    assert len(content_hash) == len(classification_fingerprint(content_hash)) == 64
    assert "all 85 concepts" in user_prompt("title", "description")
    print("85-concept schema, catalog, prompt, identity, and strict-output self-test: PASS")


def main() -> None:
    parser = argparse.ArgumentParser()
    for flag in ("healthcheck", "model-diagnostic", "self-test"):
        parser.add_argument(f"--{flag}", action="store_true")
    options = parser.parse_args()
    if options.self_test: self_test()
    elif options.model_diagnostic: print(json.dumps(classify(
        "Backend API Engineer", "Build Python APIs in Docker on AWS.")))
    elif options.healthcheck:
        try:
            with urllib.request.urlopen("http://127.0.0.1:8081/healthz", timeout=3) as response:
                raise SystemExit(0 if response.status == 200 else 1)
        except OSError: raise SystemExit(1)
    else: ThreadingHTTPServer(("0.0.0.0", 8081), Handler).serve_forever()


if __name__ == "__main__":
    main()
