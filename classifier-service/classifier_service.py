#!/usr/bin/env python3
"""Private experimental classifier service. Production scoring remains regex-only."""
from __future__ import annotations

import argparse, hashlib, json, os, subprocess, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

SERVICE_VERSION, PROTOCOL_VERSION = "0.3.0", "3"
MODEL_TYPE = "embedding"
MODEL_ID = "BAAI/bge-base-en-v1.5"
MODEL_REVISION = "a5beb1e3e68b9ab74eb54cfd186867f64f240e1a"
MODEL_SHA256 = "c7c1988aae201f80cf91a5dbbd5866409503b89dcaba877ca6dba7dd0a5167d7"
CONFIG_SHA256 = "bc00af31a4a31b74040d73370aa83b62da34c90b75eb77bfa7db039d90abd591"
MODEL_ROOT = Path(os.environ.get("CLASSIFIER_MODEL_ROOT", "/models/bge-base-en-v1.5"))
MAX_BODY_BYTES, MAX_TEXT_CHARACTERS = 2_000_000, 500_000
CHUNK_TOKENS, CHUNK_OVERLAP = 384, 64
EMBEDDING_DIMENSION, DEFAULT_THRESHOLD = 768, .80
AGGREGATION = "max"
QUERY_INSTRUCTION = "Represent this sentence for searching relevant passages: "
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
CONCEPT_CACHE_KEY = hashlib.sha256(json.dumps({"version": 1, "instruction": QUERY_INSTRUCTION,
    "concepts": CONCEPTS}, separators=(",", ":")).encode()).hexdigest()

def gpu_diagnostic() -> dict[str, Any]:
    unavailable = {"gpuAvailable": False, "deviceCount": 0, "deviceName": None,
                   "vramTotalMiB": None, "vramUsedMiB": None, "driverVersion": None}
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=name,memory.total,memory.used,driver_version",
             "--format=csv,noheader,nounits"], capture_output=True, text=True, timeout=3, check=True)
        rows = [row.strip() for row in result.stdout.splitlines() if row.strip()]
        values = [value.strip() for value in rows[0].split(",", 3)]
        if len(values) != 4: return unavailable
        return {"gpuAvailable": True, "deviceCount": len(rows), "deviceName": values[0],
                "vramTotalMiB": int(values[1]), "vramUsedMiB": int(values[2]),
                "driverVersion": values[3]}
    except (OSError, ValueError, subprocess.SubprocessError, IndexError):
        return unavailable

def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""): digest.update(block)
    return digest.hexdigest()

def model_cache_valid(full: bool = False) -> bool:
    try:
        identity = json.loads((MODEL_ROOT / ".classifier-model.json").read_text(encoding="utf-8"))
        valid = (identity == {"modelId": MODEL_ID, "revision": MODEL_REVISION} and
                 (MODEL_ROOT / "config.json").is_file() and (MODEL_ROOT / "model.safetensors").is_file())
        return valid and (not full or (file_sha256(MODEL_ROOT / "model.safetensors") == MODEL_SHA256 and
            file_sha256(MODEL_ROOT / "config.json") == CONFIG_SHA256))
    except (OSError, ValueError): return False

def chunk_tokens(tokens: list[int], size: int, overlap: int) -> list[list[int]]:
    if size <= 0 or overlap < 0 or overlap >= size: raise ValueError("Invalid chunk configuration.")
    if not tokens: return [[]]
    chunks, start = [], 0
    while start < len(tokens):
        chunks.append(tokens[start:start + size])
        if start + size >= len(tokens): break
        start += size - overlap
    return chunks

def aggregate_similarities(rows: list[list[float]]) -> list[float]:
    if not rows or any(len(row) != len(CONCEPTS) for row in rows):
        raise ValueError("One complete similarity row per posting chunk is required.")
    return [max(row[index] for row in rows) for index in range(len(CONCEPTS))]

def similarity_matches(similarity: float, threshold: float = DEFAULT_THRESHOLD) -> bool:
    if not -1 <= similarity <= 1 or not -1 <= threshold <= 1:
        raise ValueError("Cosine similarity and threshold must be bounded to [-1, 1].")
    return similarity >= threshold

class ModelRuntime:
    def __init__(self) -> None:
        self.tokenizer: Any = None; self.model: Any = None; self.torch: Any = None
        self.concept_embeddings: Any = None; self.device = "unloaded"
        self.model_load_milliseconds = 0.0; self.concept_initialization_milliseconds = 0.0
        self.concept_norm_min = 0.0; self.concept_norm_max = 0.0
    @property
    def loaded(self) -> bool: return self.model is not None
    def load(self) -> None:
        if self.loaded: return
        if not model_cache_valid(full=True): raise RuntimeError("Pinned model cache is unavailable.")
        os.environ["HF_HUB_OFFLINE"] = "1"; os.environ["TRANSFORMERS_OFFLINE"] = "1"
        import torch
        from transformers import AutoModel, AutoTokenizer
        if not torch.cuda.is_available() or torch.cuda.device_count() != 1:
            raise RuntimeError("Exactly one CUDA device is required for model inference.")
        if torch.cuda.get_device_name(0) != "NVIDIA GeForce GTX 1070":
            raise RuntimeError("The required NVIDIA GeForce GTX 1070 is unavailable.")
        started = time.perf_counter()
        self.tokenizer = AutoTokenizer.from_pretrained(str(MODEL_ROOT), local_files_only=True)
        self.model = AutoModel.from_pretrained(
            str(MODEL_ROOT), local_files_only=True, use_safetensors=True).to("cuda").eval()
        self.torch, self.device = torch, "cuda:0"
        self.model_load_milliseconds = (time.perf_counter() - started) * 1000
        started = time.perf_counter()
        with torch.inference_mode():
            self.concept_embeddings = self.embed(
                [QUERY_INSTRUCTION + description for _, description in CONCEPTS])
        if self.concept_embeddings.shape != (len(CONCEPTS), EMBEDDING_DIMENSION):
            raise RuntimeError("Embedding model returned an unexpected concept-vector shape.")
        norms = self.concept_embeddings.norm(p=2, dim=1)
        self.concept_norm_min, self.concept_norm_max = float(norms.min()), float(norms.max())
        if not torch.allclose(norms, torch.ones_like(norms), atol=1e-5):
            raise RuntimeError("Concept embeddings are not L2 normalized.")
        self.concept_initialization_milliseconds = (time.perf_counter() - started) * 1000
    def embed(self, texts: list[str]) -> Any:
        encoded = self.tokenizer(texts, padding=True, truncation=True, max_length=512,
            return_tensors="pt")
        encoded = {key: value.to("cuda") for key, value in encoded.items()}
        vectors = self.model(**encoded).last_hidden_state[:, 0]
        return self.torch.nn.functional.normalize(vectors, p=2, dim=1)
    def classify(self, title: str, description: str) -> dict[str, Any]:
        self.load()
        title = title.strip(); description = description.strip()
        title_ids = self.tokenizer.encode(title, add_special_tokens=False)
        description_ids = self.tokenizer.encode(description, add_special_tokens=False)
        chunks = chunk_tokens(description_ids, CHUNK_TOKENS, CHUNK_OVERLAP)
        passages = [f"{title}\n\n{self.tokenizer.decode(chunk, skip_special_tokens=True)}".strip()
                    for chunk in chunks]
        started = time.perf_counter()
        with INFERENCE_LOCK, self.torch.inference_mode():
            posting_embeddings = self.embed(passages)
            rows = (posting_embeddings @ self.concept_embeddings.T).clamp(-1, 1).cpu().tolist()
            similarities = aggregate_similarities(rows)
        return {"modelType": MODEL_TYPE, "modelId": MODEL_ID, "modelRevision": MODEL_REVISION,
                "device": self.device, "embeddingDimension": EMBEDDING_DIMENSION,
                "conceptEmbeddingCacheKey": CONCEPT_CACHE_KEY,
                "conceptEmbeddingMemoryBytes": self.concept_embeddings.numel() * self.concept_embeddings.element_size(),
                "conceptEmbeddingNormMin": self.concept_norm_min,
                "conceptEmbeddingNormMax": self.concept_norm_max,
                "modelLoadMilliseconds": self.model_load_milliseconds,
                "conceptEmbeddingInitializationMilliseconds": self.concept_initialization_milliseconds,
                "aggregation": AGGREGATION, "threshold": DEFAULT_THRESHOLD,
                "tokenCount": len(title_ids) + len(description_ids), "chunkCount": len(chunks),
                "inferenceMilliseconds": (time.perf_counter() - started) * 1000,
                "predictions": [{"conceptId": concept_id, "similarity": similarity,
                    "matched": similarity_matches(similarity)}
                    for (concept_id, _), similarity in zip(CONCEPTS, similarities, strict=True)]}

RUNTIME = ModelRuntime()
def identity() -> dict[str, Any]:
    return {"serviceVersion": SERVICE_VERSION, "protocolVersion": PROTOCOL_VERSION,
            "revision": os.environ.get("CLASSIFIER_GIT_SHA", "unknown"), **gpu_diagnostic()}

class Handler(BaseHTTPRequestHandler):
    server_version = "JsmClassifier/0.2"
    def log_message(self, fmt: str, *args: Any) -> None: print(f"{self.address_string()} {fmt % args}", flush=True)
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
        self.send_json(200, {"status": "healthy", **identity(), "modelId": MODEL_ID,
            "modelRevision": MODEL_REVISION, "modelAvailable": model_cache_valid(),
            "modelType": MODEL_TYPE, "modelLoaded": RUNTIME.loaded, "modelDevice": RUNTIME.device,
            "embeddingDimension": EMBEDDING_DIMENSION, "aggregation": AGGREGATION})
    def do_POST(self) -> None:  # noqa: N802
        if self.path not in ("/classify", "/classify-embedding"):
            self.send_json(404, {"error": "not found"}); return
        try:
            request = self.read_json(); job_id, title, description = (request.get(k) for k in ("jobId", "title", "description"))
            invalid = [n for n, v in (("jobId", job_id), ("title", title)) if not isinstance(v, str) or not v.strip()]
            if not isinstance(description, str): invalid.append("description")
            if invalid: self.send_json(400, {"error": "invalid request", "fields": invalid}); return
            if len(title) + len(description) > MAX_TEXT_CHARACTERS:
                self.send_json(413, {"error": "posting text is too large"}); return
            if self.path == "/classify":
                self.send_json(200, {"received": True, "jobId": job_id, "title": title,
                                     "descriptionLength": len(description), **identity()}); return
            if not model_cache_valid():
                self.send_json(503, {"error": "pinned model is unavailable", "modelAvailable": False,
                                     "modelId": MODEL_ID, "modelRevision": MODEL_REVISION}); return
            result = RUNTIME.classify(title, description)
            self.send_json(200, {"received": True, "jobId": job_id, "title": title,
                                 "descriptionLength": len(description), **identity(), **result})
        except (ValueError, json.JSONDecodeError): self.send_json(400, {"error": "invalid request"})
        except Exception as error:
            print(f"embedding failure type={type(error).__name__}", flush=True)
            self.send_json(503, {"error": "model inference is unavailable"})

def download_model() -> None:
    from huggingface_hub import snapshot_download
    MODEL_ROOT.mkdir(parents=True, exist_ok=True)
    snapshot_download(repo_id=MODEL_ID, revision=MODEL_REVISION, local_dir=MODEL_ROOT,
        allow_patterns=["config.json", "model.safetensors", "tokenizer.json", "tokenizer_config.json",
                        "special_tokens_map.json", "vocab.txt"])
    (MODEL_ROOT / ".classifier-model.json").write_text(
        json.dumps({"modelId": MODEL_ID, "revision": MODEL_REVISION}) + "\n")
    if not model_cache_valid(full=True): raise RuntimeError("Downloaded model cache failed validation.")

def self_test() -> None:
    assert chunk_tokens(list(range(10)), 4, 1) == [[0,1,2,3],[3,4,5,6],[6,7,8,9]]
    assert len(CONCEPTS) == len({c for c, _ in CONCEPTS}) == 8
    assert aggregate_similarities([[.1] * 8, [.2] * 8]) == [.2] * 8
    assert [similarity_matches(v) for v in [.6,.7,.8,.9]] == [False,False,True,True]
    assert len(CONCEPT_CACHE_KEY) == 64 and EMBEDDING_DIMENSION == 768
    assert set(gpu_diagnostic()) == {"gpuAvailable","deviceCount","deviceName","vramTotalMiB","vramUsedMiB","driverVersion"}
    print("Embedding schema, chunking, normalization contract, cache, and threshold self-test: PASS")

def main() -> None:
    parser = argparse.ArgumentParser()
    for flag in ("healthcheck", "gpu-diagnostic", "model-diagnostic", "download-model", "self-test"):
        parser.add_argument(f"--{flag}", action="store_true")
    options = parser.parse_args()
    if options.self_test: self_test()
    elif options.download_model: download_model()
    elif options.gpu_diagnostic:
        result=gpu_diagnostic(); print(json.dumps(result)); raise SystemExit(0 if result["deviceCount"]==1 and result["deviceName"]=="NVIDIA GeForce GTX 1070" else 1)
    elif options.model_diagnostic:
        print(json.dumps({**identity(), **RUNTIME.classify("Backend API Engineer", "Build Python APIs in Docker on AWS.")}))
    elif options.healthcheck:
        import urllib.request
        try:
            with urllib.request.urlopen("http://127.0.0.1:8081/healthz", timeout=3) as response: raise SystemExit(0 if response.status==200 else 1)
        except OSError: raise SystemExit(1)
    else: ThreadingHTTPServer(("0.0.0.0", 8081), Handler).serve_forever()

if __name__ == "__main__": main()
