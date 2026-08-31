# Classifier service Phase 0/1 design

This phase proves only an internal HTTP round trip and NVIDIA device visibility. It does not
load, download, select, or train a model, and no normal ingestion or Job Fit path calls the
service.

## Inventory and decisions

- The Compose project is `jsm-lab`. Production JSM publishes only `192.168.1.20:8080` and
  persists application data and Data Protection keys under `/home/codex/jsm-lab/data`.
- The exact-SHA deployment script builds and scans the candidate locally on `curiosity`, then
  replaces only named Compose services. Mailpit and unrelated Docker resources are not managed
  by the deployment. The classifier follows that same exact-SHA label/scan policy.
- JSM keeps its existing default network (including its established Mailpit route) and joins a
  new internal `classifier` network. `job-classifier` joins only that internal network. It has no
  published port, volumes, Docker socket, JSM data, account/workspace data, Data Protection
  keys, Mailpit route, or ai801 route.
- Both containers remain non-root, read-only, capability-free, and protected by
  `no-new-privileges`. The classifier receives only the JSON supplied to the explicit diagnostic.
- The service uses Python's standard-library HTTP server. No Python package, CUDA SDK, ML
  runtime, or model weight is installed. NVIDIA Container Toolkit injects only the driver's
  `utility` capability and `nvidia-smi`; the probe therefore consumes no model/runtime VRAM.

## Contract

`GET /healthz` and `POST /classify` are internal on port 8081. Protocol version `1` and service
version `0.1.0` are explicit. `/classify` accepts `jobId`, `title`, and `description`, returning
the exact ID/title, Unicode-scalar description length, version identity, and a deterministic GPU
schema. Full descriptions are never logged or echoed.

JSM exposes `POST /api/admin/classifier-diagnostic`, protected by the existing server-side
`JsmAdmin` authorization policy and state-changing-request controls. The typed client validates
the echo contract and returns an isolated HTTP 503 diagnostic result when unavailable. JSM
startup, browsing, ingestion, and Job Fit scoring remain independent.

## GPU compatibility and host preflight

The GTX 1070 is Pascal, compute capability 6.1. NVIDIA documents CUDA 12.9 as the last toolkit
that can compile new offline code for pre-7.5 GPUs; CUDA 13 removes that compilation target.
Phase 1 compiles no CUDA code and uses only the driver-management API through `nvidia-smi`, so
it avoids prematurely choosing a CUDA/PyTorch stack. Phase 2 should remain on a CUDA 12.x/12.9
build that explicitly includes `sm_61` support and validate it against the installed driver.

Before replacing any container, the trusted deployment records Docker/Compose versions, host
GPU name/driver/VRAM, `nvidia-ctk` presence, configured Docker runtimes, and proves
`docker run --gpus all` against the exact classifier image. If Toolkit support is absent, the
deployment stops without changing JSM. It deliberately does not install packages or edit
`/etc/docker/daemon.json` automatically. The minimum operator change, if required, is the
NVIDIA-supported `nvidia-container-toolkit` package, `nvidia-ctk runtime configure
--runtime=docker`, and a controlled Docker restart after confirming unrelated containers'
restart policies.

## Verification strategy

Hosted CI runs unit/contract tests with GPU unavailable, builds and scans both exact-SHA images,
and runs the classifier container without a GPU. Architecture tests enforce private networking,
no sensitive mounts/socket, hardening, admin authorization, and absence from Job Fit scoring.
On `curiosity`, deployment requires exactly one visible `NVIDIA GeForce GTX 1070`, runs a JSM
process inside the deployed JSM container to POST job `R180395`, and validates the echoed ID,
title, length, versions, and GPU result. It also records classifier idle CPU/RAM and GPU memory
before and after startup.
