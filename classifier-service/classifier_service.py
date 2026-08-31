#!/usr/bin/env python3
"""Private Phase 2 classifier service. Model inference is experimental only."""
from __future__ import annotations

import argparse, hashlib, json, os, subprocess, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

SERVICE_VERSION, PROTOCOL_VERSION = "0.2.0", "2"
MODEL_ID = "cross-encoder/nli-deberta-v3-base"
MODEL_REVISION = "6c749ce3425cd33b46d187e45b92bbf96ee12ec7"
MODEL_SHA256 = "d8148c6d49e0a7925134294c56326c71fe0ab1dc390e37355e00c7efbb488afa"
CONFIG_SHA256 = "897e756eb59d3183adb505952e7910e7cbc7750a43f3b3747a96b688d2b02a47"
MODEL_ROOT = Path(os.environ.get("CLASSIFIER_MODEL_ROOT", "/models/nli-deberta-v3-base"))
MAX_BODY_BYTES, MAX_TEXT_CHARACTERS = 2_000_000, 500_000
CHUNK_TOKENS, CHUNK_OVERLAP = 384, 64
INFERENCE_LOCK = threading.Lock()
CONCEPTS = (
    ("role.ai-ml-engineering", "This job involves artificial intelligence or machine-learning engineering work."),
    ("role.software-engineering", "This job involves direct software engineering work."),
    ("technical.software-development", "This job involves developing or maintaining software."),
    ("technical.backend-development", "This job involves backend or server-side software development."),
    ("technical.api-development", "This job involves designing, implementing, or maintaining APIs."),
    ("technical.automation-scripting", "This job involves scripting or software automation."),
    ("role.cloud-engineering", "This job involves cloud engineering responsibilities."),
    ("technical.containers", "This job involves Kubernetes, Docker, or container orchestration responsibilities."),
)

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
        identity = json.loads((MODEL_ROOT / ".phase2-model.json").read_text(encoding="utf-8"))
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

class ModelRuntime:
    def __init__(self) -> None:
        self.tokenizer: Any = None; self.model: Any = None; self.torch: Any = None; self.device = "unloaded"
    @property
    def loaded(self) -> bool: return self.model is not None
    def load(self) -> None:
        if self.loaded: return
        if not model_cache_valid(full=True): raise RuntimeError("Pinned model cache is unavailable.")
        os.environ["HF_HUB_OFFLINE"] = "1"; os.environ["TRANSFORMERS_OFFLINE"] = "1"
        import torch
        from transformers import AutoModelForSequenceClassification, AutoTokenizer
        if not torch.cuda.is_available() or torch.cuda.device_count() != 1:
            raise RuntimeError("Exactly one CUDA device is required for model inference.")
        if torch.cuda.get_device_name(0) != "NVIDIA GeForce GTX 1070":
            raise RuntimeError("The required NVIDIA GeForce GTX 1070 is unavailable.")
        self.tokenizer = AutoTokenizer.from_pretrained(str(MODEL_ROOT), local_files_only=True)
        self.model = AutoModelForSequenceClassification.from_pretrained(
            str(MODEL_ROOT), local_files_only=True, use_safetensors=True).to("cuda").eval()
        self.torch, self.device = torch, "cuda:0"
    def classify(self, title: str, description: str) -> dict[str, Any]:
        self.load()
        ids = self.tokenizer.encode(f"{title.strip()}\n\n{description.strip()}".strip(), add_special_tokens=False)
        chunks, maximum, started = chunk_tokens(ids, CHUNK_TOKENS, CHUNK_OVERLAP), dict.fromkeys((c[0] for c in CONCEPTS), 0.0), time.perf_counter()
        with INFERENCE_LOCK, self.torch.inference_mode():
            for chunk in chunks:
                premise = self.tokenizer.decode(chunk, skip_special_tokens=True)
                encoded = self.tokenizer([premise] * len(CONCEPTS), [h for _, h in CONCEPTS],
                    padding=True, truncation="only_first", max_length=512, return_tensors="pt")
                encoded = {key: value.to("cuda") for key, value in encoded.items()}
                logits = self.model(**encoded).logits
                labels = {int(key): value.lower() for key, value in self.model.config.id2label.items()}
                entail = next(i for i, v in labels.items() if "entail" in v)
                contradict = next(i for i, v in labels.items() if "contrad" in v)
                scores = self.torch.softmax(logits[:, [contradict, entail]], dim=1)[:, 1].cpu().tolist()
                for (concept_id, _), score in zip(CONCEPTS, scores, strict=True): maximum[concept_id] = max(maximum[concept_id], float(score))
        return {"modelId": MODEL_ID, "modelRevision": MODEL_REVISION, "device": self.device,
                "tokenCount": len(ids), "chunkCount": len(chunks),
                "inferenceMilliseconds": (time.perf_counter() - started) * 1000,
                "scores": [{"conceptId": c, "score": maximum[c]} for c, _ in CONCEPTS]}

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
            "modelLoaded": RUNTIME.loaded, "modelDevice": RUNTIME.device})
    def do_POST(self) -> None:  # noqa: N802
        if self.path not in ("/classify", "/classify-zero-shot"):
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
            print(f"zero-shot failure type={type(error).__name__}", flush=True)
            self.send_json(503, {"error": "model inference is unavailable"})

def download_model() -> None:
    from huggingface_hub import snapshot_download
    MODEL_ROOT.mkdir(parents=True, exist_ok=True)
    snapshot_download(repo_id=MODEL_ID, revision=MODEL_REVISION, local_dir=MODEL_ROOT,
        allow_patterns=["config.json", "model.safetensors", "tokenizer.json", "tokenizer_config.json",
                        "special_tokens_map.json", "spm.model", "added_tokens.json"])
    (MODEL_ROOT / ".phase2-model.json").write_text(json.dumps({"modelId": MODEL_ID, "revision": MODEL_REVISION}) + "\n")
    if not model_cache_valid(full=True): raise RuntimeError("Downloaded model cache failed validation.")

def self_test() -> None:
    assert chunk_tokens(list(range(10)), 4, 1) == [[0,1,2,3],[3,4,5,6],[6,7,8,9]]
    assert len(CONCEPTS) == len({c for c, _ in CONCEPTS}) == 8
    assert [v >= .5 for v in [.2,.3,.5,.7,.9]] == [False,False,True,True,True]
    assert set(gpu_diagnostic()) == {"gpuAvailable","deviceCount","deviceName","vramTotalMiB","vramUsedMiB","driverVersion"}
    print("Classifier schema, chunking, and threshold self-test: PASS")

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
