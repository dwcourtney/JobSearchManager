#!/usr/bin/env python3
"""Private 85-concept DeBERTa NLI classifier with opt-in Qwen deep analysis."""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import subprocess
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

SERVICE_VERSION, PROTOCOL_VERSION = "2.0.0", "6"
MODEL_TYPE = "nli-sequence-classifier"
MODEL_ID = "cross-encoder/nli-deberta-v3-base"
MODEL_REVISION = "6c749ce3425cd33b46d187e45b92bbf96ee12ec7"
MODEL_SHA256 = "d8148c6d49e0a7925134294c56326c71fe0ab1dc390e37355e00c7efbb488afa"
CONFIG_SHA256 = "897e756eb59d3183adb505952e7910e7cbc7750a43f3b3747a96b688d2b02a47"
MODEL_DIGEST = f"sha256:{MODEL_SHA256}"
MODEL_ROOT = Path(os.environ.get("CLASSIFIER_MODEL_ROOT", "/models/nli-deberta-v3-base"))
DEFAULT_CATALOG_PATH = (Path("/app/JobConceptCatalog.json")
    if Path("/app/JobConceptCatalog.json").exists()
    else Path(__file__).resolve().parents[1] / "JobConceptCatalog.json")
CATALOG_PATH = Path(os.environ.get("CLASSIFIER_CATALOG_PATH", str(DEFAULT_CATALOG_PATH)))
EXPECTED_DEVICE_NAME = os.environ.get("CLASSIFIER_EXPECTED_DEVICE_NAME", "NVIDIA GeForce GTX 1070")
MAX_BODY_BYTES, MAX_TEXT_CHARACTERS = 2_000_000, 500_000
CHUNK_TOKENS, CHUNK_OVERLAP, MAX_LENGTH, CONCEPT_BATCH_SIZE, THRESHOLD = 384, 64, 512, 8, 0.5
CONFIGURATION_VERSION = "deberta-85-nli-v1"
HYPOTHESIS_TEMPLATE = "This job assigns the candidate work or conditions matching this concept: {definition}"
INFERENCE_LOCK = threading.Lock()

QWEN_MODEL_ID = "Qwen/Qwen3-4B-Instruct-2507"
QWEN_MODEL_TAG = "qwen3:4b-instruct-2507-q4_K_M"
QWEN_MODEL_DIGEST = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://ollama:11434").rstrip("/")
QWEN_PROMPT_VERSION = "job-fit-85-deep-analysis-v1"
QWEN_CONTEXT_LENGTH, QWEN_MAX_OUTPUT_TOKENS, QWEN_SEED, QWEN_TEMPERATURE = 8192, 3072, 42, 0
QWEN_INFERENCE_LOCK = threading.Lock()
QWEN_SYSTEM_PROMPT = """You are a careful job-posting responsibility classifier.
Classify the role itself, not technologies merely mentioned as products, customer environments,
desired awareness, qualifications without assigned duties, team context, or work managed by someone else.
A label is true only when the posting assigns the candidate responsibility or a work condition matching
its definition. Multiple overlapping labels may be true. Return exactly the requested JSON object with
one boolean for every canonical concept plus a concise analysis. Do not add prose outside the JSON."""


def sha256(value: bytes | str) -> str:
    return hashlib.sha256(value.encode() if isinstance(value, str) else value).hexdigest()


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_catalog() -> tuple[int, str, tuple[tuple[str, str, str, str], ...]]:
    raw = CATALOG_PATH.read_bytes()
    document = json.loads(raw)
    values = document.get("concepts")
    concepts = tuple((item["id"], item["displayName"], item["category"], item["definition"])
                     for item in values)
    if not isinstance(document.get("version"), int) or len(concepts) != 85 or len(set(x[0] for x in concepts)) != 85:
        raise RuntimeError("The production classifier requires exactly 85 canonical concepts.")
    if any(not all(item) for item in concepts):
        raise RuntimeError("Every canonical concept requires an ID and definition.")
    return document["version"], sha256(raw.replace(b"\r\n", b"\n")), concepts


TAXONOMY_VERSION, TAXONOMY_FINGERPRINT, CONCEPTS = load_catalog()
CONCEPT_IDS = tuple(item[0] for item in CONCEPTS)
QWEN_OUTPUT_SCHEMA = {"type": "object", "properties": {
    "concepts": {"type": "object",
                 "properties": {key: {"type": "boolean"} for key in CONCEPT_IDS},
                 "required": list(CONCEPT_IDS), "additionalProperties": False},
    "analysis": {"type": "string"}},
    "required": ["concepts", "analysis"], "additionalProperties": False}
QWEN_PROMPT_HASH = sha256(f"{QWEN_PROMPT_VERSION}\n{QWEN_SYSTEM_PROMPT}\n{TAXONOMY_FINGERPRINT}")
CONFIGURATION_FINGERPRINT = sha256("\n".join((
    CONFIGURATION_VERSION, MODEL_ID, MODEL_REVISION, MODEL_DIGEST,
    str(CHUNK_TOKENS), str(CHUNK_OVERLAP), str(MAX_LENGTH), str(CONCEPT_BATCH_SIZE), str(THRESHOLD),
    HYPOTHESIS_TEMPLATE, TAXONOMY_FINGERPRINT,
)))


def posting_content_hash(title: str, description: str) -> str:
    return sha256(f"{title}\n{description}")


def classification_fingerprint(content_hash: str) -> str:
    return sha256("\n".join((
        content_hash, str(TAXONOMY_VERSION), TAXONOMY_FINGERPRINT,
        MODEL_ID, MODEL_REVISION, MODEL_DIGEST,
        CONFIGURATION_VERSION, CONFIGURATION_FINGERPRINT,
    )))


def qwen_classification_fingerprint(content_hash: str) -> str:
    return sha256("\n".join((
        content_hash, str(TAXONOMY_VERSION), TAXONOMY_FINGERPRINT,
        QWEN_MODEL_ID, QWEN_MODEL_TAG, QWEN_MODEL_DIGEST,
        QWEN_PROMPT_VERSION, QWEN_PROMPT_HASH, str(QWEN_TEMPERATURE),
        str(QWEN_SEED), str(QWEN_CONTEXT_LENGTH), str(QWEN_MAX_OUTPUT_TOKENS),
    )))


def qwen_user_prompt(title: str, description: str) -> str:
    definitions = "\n".join(
        f"- {concept_id} [{category}] {name}: {definition}"
        for concept_id, name, category, definition in CONCEPTS)
    return (f"Canonical concept definitions:\n{definitions}\n\nJob title:\n{title}\n\n"
            f"Full job posting:\n{description}\n\nClassify all 85 concepts according to actual "
            "candidate responsibilities and work conditions. In analysis, briefly summarize the role, "
            "responsibility shape, work arrangement, technical domains, and material fit risks.")


def model_cache_valid(full: bool = False) -> bool:
    try:
        identity = json.loads((MODEL_ROOT / ".phase2-model.json").read_text())
        valid = identity == {"modelId": MODEL_ID, "revision": MODEL_REVISION}
        valid = valid and (MODEL_ROOT / "config.json").is_file() and (MODEL_ROOT / "model.safetensors").is_file()
        return valid and (not full or (
            file_sha256(MODEL_ROOT / "model.safetensors") == MODEL_SHA256 and
            file_sha256(MODEL_ROOT / "config.json") == CONFIG_SHA256))
    except (OSError, ValueError):
        return False


def gpu_diagnostic() -> dict[str, Any]:
    unavailable = {"gpuAvailable": False, "deviceCount": 0, "deviceName": None,
                   "vramTotalMiB": None, "vramUsedMiB": None, "driverVersion": None}
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=name,memory.total,memory.used,driver_version",
             "--format=csv,noheader,nounits"], capture_output=True, text=True, timeout=3, check=True)
        rows = [row.strip() for row in result.stdout.splitlines() if row.strip()]
        values = [value.strip() for value in rows[0].split(",", 3)]
        return {"gpuAvailable": True, "deviceCount": len(rows), "deviceName": values[0],
                "vramTotalMiB": int(values[1]), "vramUsedMiB": int(values[2]),
                "driverVersion": values[3]}
    except (OSError, ValueError, subprocess.SubprocessError, IndexError):
        return unavailable


def chunk_tokens(tokens: list[int]) -> list[list[int]]:
    if not tokens:
        return [[]]
    chunks, start = [], 0
    while start < len(tokens):
        chunks.append(tokens[start:start + CHUNK_TOKENS])
        if start + CHUNK_TOKENS >= len(tokens):
            break
        start += CHUNK_TOKENS - CHUNK_OVERLAP
    return chunks


class ModelRuntime:
    def __init__(self) -> None:
        self.tokenizer: Any = None
        self.model: Any = None
        self.torch: Any = None
        self.device = "unloaded"

    @property
    def loaded(self) -> bool:
        return self.model is not None

    def load(self) -> None:
        if self.loaded:
            return
        if not model_cache_valid(full=True):
            raise RuntimeError("Pinned DeBERTa model cache is unavailable.")
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
        import torch
        from transformers import AutoModelForSequenceClassification, AutoTokenizer
        if not torch.cuda.is_available() or torch.cuda.device_count() != 1:
            raise RuntimeError("Exactly one CUDA device is required.")
        if torch.cuda.get_device_name(0) != EXPECTED_DEVICE_NAME:
            raise RuntimeError("The expected production GPU is unavailable.")
        self.tokenizer = AutoTokenizer.from_pretrained(str(MODEL_ROOT), local_files_only=True)
        self.model = AutoModelForSequenceClassification.from_pretrained(
            str(MODEL_ROOT), local_files_only=True, use_safetensors=True).to("cuda").eval()
        self.torch, self.device = torch, "cuda:0"

    def classify(self, title: str, description: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
        self.load()
        ids = self.tokenizer.encode(f"{title.strip()}\n\n{description.strip()}".strip(), add_special_tokens=False)
        chunks = chunk_tokens(ids)
        maximum = dict.fromkeys(CONCEPT_IDS, 0.0)
        hypotheses = [HYPOTHESIS_TEMPLATE.format(definition=definition)
                      for _, _, _, definition in CONCEPTS]
        started = time.perf_counter()
        with INFERENCE_LOCK, self.torch.inference_mode():
            for chunk in chunks:
                premise = self.tokenizer.decode(chunk, skip_special_tokens=True)
                for start in range(0, len(CONCEPTS), CONCEPT_BATCH_SIZE):
                    batch_ids = CONCEPT_IDS[start:start + CONCEPT_BATCH_SIZE]
                    batch_hypotheses = hypotheses[start:start + CONCEPT_BATCH_SIZE]
                    encoded = self.tokenizer(
                        [premise] * len(batch_ids), batch_hypotheses, padding=True,
                        truncation="only_first", max_length=MAX_LENGTH, return_tensors="pt")
                    encoded = {key: value.to("cuda") for key, value in encoded.items()}
                    logits = self.model(**encoded).logits
                    labels = {int(key): value.lower() for key, value in self.model.config.id2label.items()}
                    entail = next(index for index, value in labels.items() if "entail" in value)
                    contradict = next(index for index, value in labels.items() if "contrad" in value)
                    scores = self.torch.softmax(
                        logits[:, [contradict, entail]], dim=1)[:, 1].cpu().tolist()
                    for concept_id, score in zip(batch_ids, scores, strict=True):
                        maximum[concept_id] = max(maximum[concept_id], float(score))
        predictions = [{"conceptId": concept_id, "matched": score >= THRESHOLD, "score": score}
                       for concept_id, score in maximum.items()]
        return predictions, {"tokenCount": len(ids), "chunkCount": len(chunks),
                             "inferenceMilliseconds": (time.perf_counter() - started) * 1000}


RUNTIME = ModelRuntime()


def identity() -> dict[str, Any]:
    return {"serviceVersion": SERVICE_VERSION, "protocolVersion": PROTOCOL_VERSION,
            "revision": os.environ.get("CLASSIFIER_GIT_SHA", "unknown"),
            "modelType": MODEL_TYPE, "modelId": MODEL_ID, "modelRevision": MODEL_REVISION,
            "modelDigest": MODEL_DIGEST, "taxonomyVersion": TAXONOMY_VERSION,
            "taxonomyFingerprint": TAXONOMY_FINGERPRINT, "conceptCount": len(CONCEPTS),
            "classifierConfigurationVersion": CONFIGURATION_VERSION,
            "classifierConfigurationFingerprint": CONFIGURATION_FINGERPRINT,
            "threshold": THRESHOLD, "chunkTokens": CHUNK_TOKENS,
            "chunkOverlap": CHUNK_OVERLAP, "conceptBatchSize": CONCEPT_BATCH_SIZE,
            **gpu_diagnostic()}


def classify_payload(job_id: str, title: str, description: str) -> dict[str, Any]:
    content_hash = posting_content_hash(title, description)
    predictions, metrics = RUNTIME.classify(title, description)
    return {"received": True, "jobId": job_id, "title": title,
            "descriptionLength": len(description), **identity(),
            "postingContentHash": content_hash,
            "classificationFingerprint": classification_fingerprint(content_hash),
            "classifiedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "device": RUNTIME.device, "predictions": predictions, **metrics}


def qwen_deep_analysis(job_id: str, title: str, description: str) -> dict[str, Any]:
    request = urllib.request.Request(f"{OLLAMA_URL}/api/chat",
        data=json.dumps({"model": QWEN_MODEL_TAG,
                         "messages": [{"role": "system", "content": QWEN_SYSTEM_PROMPT},
                                      {"role": "user", "content": qwen_user_prompt(title, description)}],
                         "format": QWEN_OUTPUT_SCHEMA, "stream": False, "keep_alive": -1,
                         "options": {"temperature": QWEN_TEMPERATURE, "seed": QWEN_SEED,
                                     "num_ctx": QWEN_CONTEXT_LENGTH,
                                     "num_predict": QWEN_MAX_OUTPUT_TOKENS}}).encode(),
        headers={"Content-Type": "application/json"}, method="POST")
    with QWEN_INFERENCE_LOCK, urllib.request.urlopen(request, timeout=300) as response:
        response_value = json.load(response)
    content = response_value.get("message", {}).get("content")
    if response_value.get("done") is not True or not isinstance(content, str):
        raise RuntimeError("LLM deep-analysis response was incomplete.")
    try:
        result = json.loads(content)
    except json.JSONDecodeError as error:
        raise RuntimeError("LLM deep-analysis response was not valid JSON.") from error
    predictions = result.get("concepts") if isinstance(result, dict) else None
    analysis = result.get("analysis") if isinstance(result, dict) else None
    if (not isinstance(predictions, dict) or set(predictions) != set(CONCEPT_IDS) or
            any(type(predictions[key]) is not bool for key in CONCEPT_IDS) or
            not isinstance(analysis, str) or not analysis.strip()):
        raise RuntimeError("LLM deep-analysis response failed strict validation.")
    content_hash = posting_content_hash(title, description)
    return {"received": True, "jobId": job_id, "title": title,
            "modelId": QWEN_MODEL_ID, "modelTag": QWEN_MODEL_TAG,
            "modelDigest": QWEN_MODEL_DIGEST,
            "taxonomyVersion": TAXONOMY_VERSION, "taxonomyFingerprint": TAXONOMY_FINGERPRINT,
            "promptVersion": QWEN_PROMPT_VERSION, "promptHash": QWEN_PROMPT_HASH,
            "analyzedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "postingContentHash": content_hash,
            "classificationFingerprint": qwen_classification_fingerprint(content_hash),
            "predictions": [{"conceptId": key, "matched": predictions[key]}
                            for key in CONCEPT_IDS],
            "analysis": analysis.strip()}


class Handler(BaseHTTPRequestHandler):
    server_version = "JsmClassifier/2.0"
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
        job_id, title, description = (value.get(key) for key in ("jobId", "title", "description"))
        if not all(isinstance(item, str) for item in (job_id, title, description)) or not job_id.strip() or not title.strip():
            raise ValueError("Invalid request.")
        if len(title) + len(description) > MAX_TEXT_CHARACTERS:
            raise ValueError("Posting is too large.")
        return job_id, title, description
    def do_GET(self) -> None:
        if self.path != "/healthz":
            self.send_json(404, {"error": "not found"})
            return
        self.send_json(200, {"status": "healthy", **identity(),
                            "modelAvailable": model_cache_valid(), "modelLoaded": RUNTIME.loaded,
                            "modelDevice": RUNTIME.device})
    def do_POST(self) -> None:
        if self.path not in ("/classify", "/deep-analyze"):
            self.send_json(404, {"error": "not found"})
            return
        try:
            job_id, title, description = self.read_request()
            self.send_json(200, classify_payload(job_id, title, description)
                           if self.path == "/classify"
                           else qwen_deep_analysis(job_id, title, description))
        except (ValueError, json.JSONDecodeError):
            self.send_json(400, {"error": "invalid request"})
        except Exception as error:
            print(f"classifier failure type={type(error).__name__}", flush=True)
            self.send_json(503, {"error": "classifier unavailable"})


def download_model() -> None:
    from huggingface_hub import snapshot_download
    MODEL_ROOT.mkdir(parents=True, exist_ok=True)
    snapshot_download(repo_id=MODEL_ID, revision=MODEL_REVISION, local_dir=MODEL_ROOT,
        allow_patterns=["config.json", "model.safetensors", "tokenizer.json", "tokenizer_config.json",
                        "special_tokens_map.json", "spm.model", "added_tokens.json"])
    (MODEL_ROOT / ".phase2-model.json").write_text(
        json.dumps({"modelId": MODEL_ID, "revision": MODEL_REVISION}) + "\n")
    if not model_cache_valid(full=True):
        raise RuntimeError("Downloaded model cache failed validation.")


def self_test() -> None:
    assert len(CONCEPTS) == len(set(CONCEPT_IDS)) == 85
    assert chunk_tokens(list(range(10))) == [list(range(10))]
    assert [value >= THRESHOLD for value in (.2, .5, .9)] == [False, True, True]
    assert len(CONFIGURATION_FINGERPRINT) == len(classification_fingerprint("a" * 64)) == 64
    assert QWEN_OUTPUT_SCHEMA["properties"]["concepts"]["required"] == list(CONCEPT_IDS)
    assert len(QWEN_PROMPT_HASH) == len(qwen_classification_fingerprint("a" * 64)) == 64
    assert "all 85 concepts" in qwen_user_prompt("title", "description")
    print("DeBERTa 85-concept schema, configuration, and threshold self-test: PASS")


def main() -> None:
    parser = argparse.ArgumentParser()
    for flag in ("healthcheck", "gpu-diagnostic", "model-diagnostic", "download-model", "self-test"):
        parser.add_argument(f"--{flag}", action="store_true")
    options = parser.parse_args()
    if options.self_test:
        self_test()
    elif options.download_model:
        download_model()
    elif options.gpu_diagnostic:
        print(json.dumps(gpu_diagnostic()))
    elif options.model_diagnostic:
        print(json.dumps(classify_payload("diagnostic", "Backend API Engineer",
            "Build Python APIs in Docker on AWS.")))
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
