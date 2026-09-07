# Explicit Qwen research tooling

Normal JSM does not build, load, start or contact this project. Its web executable has no Qwen client or LLM commands. Persisted Qwen DTOs and historical evaluation ledger contracts remain in production only for compatibility; no cache schema changed.

The separate console project `Jsm.Qwen.Research.csproj` links `../../ClassifierClient.cs`, `../../LlmHoldoutEvaluation.cs`, `../../LlmHardwareBenchmark.cs` and `../../LlmTechnicalPreflight.cs`. These historical source paths are retained for existing research references and explicitly excluded from the web project and normal Docker build context. Shared deterministic metrics now live in `HoldoutMetrics.cs`; deterministic inference lives in `SemanticClassificationService.cs`.

From the repository root:

```sh
dotnet restore research/qwen/Jsm.Qwen.Research.csproj --locked-mode
dotnet run --project research/qwen/Jsm.Qwen.Research.csproj -- --llm-benchmark preflight /path/to/benchmark
dotnet run --project research/qwen/Jsm.Qwen.Research.csproj -- --llm-benchmark predict /path/to/benchmark
dotnet run --project research/qwen/Jsm.Qwen.Research.csproj -- --llm-benchmark score /path/to/benchmark /path/to/evaluation
dotnet run --project research/qwen/Jsm.Qwen.Research.csproj -- --regex-maintenance evaluate-llm-holdout /path/to/research-rules.db /path/to/evaluation
dotnet run --project research/qwen/Jsm.Qwen.Research.csproj -- --regex-maintenance preflight-llm-holdout /path/to/research-rules.db /path/to/evaluation
```

Set `DEEP_ANALYSIS_URL` explicitly when running outside the research network. Hardware prediction/preflight dispatch still occurs before any rule-store initialization; it does not read scoring labels or initialize SQLite. Full holdout evaluation intentionally opens its explicitly supplied research database and references.

Use `compose.yaml` in this directory for the separate `jsm-qwen-research` project. Supply `OLLAMA_IMAGE_REFERENCE`, `DEEP_ANALYSIS_IMAGE_REFERENCE`, `BENCHMARK_IMAGE_REFERENCE` and `BENCHMARK_ROOT`; run `docker compose -f research/qwen/compose.yaml up -d ollama deep-analysis` and explicitly run the `runner` profile when needed. No ports are published. GPU/model settings exist only here. The model directory is mounted read-only in the experiment stack.

`provision-model.sh` explicitly downloads and verifies the unchanged pinned model into an existing, research-owned `RESEARCH_MODEL_ROOT`. It requires the pinned `OLLAMA_IMAGE_REFERENCE`, GPU access and permissions for UID 65532. It does not chown, delete or prune existing model directories; its cleanup removes only the unique temporary provisioning container it creates.

`validate.sh <full-git-sha>` is an independent validation entry point: mocked client/holdout tests, adapter self-test, research architecture tests, image builds and existing Trivy image gates. It is never called by normal CI/deployment. It does not download a model or run inference. Model execution is a separate explicit experiment.

## Preserved paths and artifacts

- `classifier-service/Dockerfile` and `classifier_service.py`: unchanged pinned Qwen adapter; research-only.
- `ollama-runtime/Dockerfile`: unchanged pinned Ollama build; research-only.
- `Dockerfile.hardware-benchmark`: now builds the separate research executable, removes the same scoring/seed files and uses the research entry point.
- `deploy/compose.tinker-benchmark.yaml`: retained historical benchmark definition; its runner image must be the research image now.
- `scripts/monitor-llm-hardware-benchmark.py`, all model research documentation, corpora/reports/manifests/checkpoint inventories and frozen outputs retain their paths and contents.
- `archive/JobCatalog-deep-analysis.cs.txt`: dormant web cache-write branch preserved as historical source, not compiled.
- `archive/ci-model-validation.sh.txt`: previous model-specific CI block preserved as historical reference.

Existing curiosity model directories and containers are not deleted or repurposed by normal deployment. Existing model containers may remain as detached legacy research resources; normal JSM is no longer attached to their classifier network. A researcher can explicitly start the separate project against a chosen preserved model copy. Do not run inference from two containers against a writable provisioning directory concurrently.
