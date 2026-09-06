# Declarative cheap-triage rules

Cheap triage = a declarative RegEx/rule engine. **Temporary implementation pending future learned-model replacement.** The current `electrical-safe` release is **shadow/evaluation only**, with no automatic job discard. Production Job Fit scoring has not changed.

## Inventory and retirement decision

| Component | Classification | Action and reason |
|---|---|---|
| `RegexSemanticClassifier.cs`, `SemanticRules.cs`, `SqliteSemanticRuleStore.cs`, `LegacySemanticRuleMigrator.cs`, `ClassifierClient.cs`, `JobConceptCatalog.json`, `LegacyJobConceptRules.json` | Active Job Fit behavior | Retained. SQLite-backed semantic concept rules, cache identity and score freshness are a separate subsystem. |
| `classifier-service/classifier_service.py`, its Dockerfile, `ollama-runtime/`, `DeepAnalyzeAsync`, existing LLM evaluation/Admin controls | Still-used Job Fit/Qwen functionality | Retained. Opt-in 85-concept Job Fit deep analysis and evaluation need these components. The retired `/classify` route stays absent; `/deep-analyze` stays available. No DeBERTa/BERT-Tiny runtime dependency exists here. |
| `TriageEvaluation.cs`, `evaluate-triage` maintenance CLI, `/api/admin/evaluations/triage`, historical Admin Triage card | Historical evaluation-only infrastructure | Retained and explicitly distinguished in the UI. This earlier two-stage prototype is not electrical-safe, does not discard jobs, and is parsed by historical research code. Its DTOs and saved reports remain readable. |
| `Tests/cheap_reject_evaluation/evaluate.py`, `leakage.py` | Preserved research / frozen baseline | Retained byte-for-byte. These are the original electrical-safe implementation, not a production inference service. |
| `Tests/cheap_reject_evaluation/deberta*`, `supervised*`, `input_v3`, machine corpus and all other files under that directory; related investigation docs | Preserved research | All datasets, labels, splits, predictions, thresholds, reports, logs, scripts, dependency locks, checkpoint manifests/references remain unchanged. Training and inference scripts are historical reproduction tools, not application hooks. |
| Active cheap-only DeBERTa/BERT-Tiny configuration, model selection UI, endpoints, containers or runtime dependencies | Not found | Nothing is deleted merely for containing “classifier” or “LLM.” There is no safe/necessary runtime removal in this checkout. |
| `CheapRejectRules.cs`, `CheapTriage/`, `RuleMaintenance.cs`, evaluation CLI | Current cheap-triage subsystem | New externalized electrical-safe engine and read-only maintenance workflow. No connection to production job filtering or Job Fit scoring. |

Validation exposed one supporting build blocker: Alpine no longer serves the exact `libuuid=2.41.6-r0` package pinned by the deep-analysis Dockerfile. Its inherited Python `_uuid` runtime dependency is now pinned to available `2.41.6-r1`; the exact-version regression assertion was updated. No package was added, no base digest was loosened, and no security gate or Job Fit behavior changed.

The model experiments are **retired from active cheap triage, experimental, and preserved for future research/revival**. DeBERTa/BERT and improved input representations did not establish the required safety/calibration versus rejection tradeoff. Runtime was not the decisive blocker. Revisit learned models when labels, model capability and practical consumer hardware support better results; start from the frozen protocols and manifests, never silently relabel or reuse blinded holdouts.

## Rules and execution

`CheapTriage/rulesets/1.0.0.json` contains the frozen electrical-safe occupation knowledge: 68 named predicates and 23 ordered rules, comprising seven KEEP guards and sixteen rejection conditions. `CheapTriage/ruleset-schema-v1.json` describes schema 1. The engine additionally validates duplicate JSON properties, unique IDs/orders, regex syntax, references, cycles, depth and KEEP-before-REJECT ordering.

Match predicates specify `id`, `scope` (`title`, `body`, or the title before the first ` - `), `pattern`, and optional `ignorePattern`. The latter excludes individual matched duty phrases, preserving other legitimate duty evidence. Boolean predicates combine named conditions through `all`, `any`, and `none`. Rule entries contain stable `id`, numeric `order`, `decision`, `category`, `reason`, and predicate `when`. This bounded Boolean structure expresses positive title evidence, corroborating duties, exclusions and vetoes without embedding occupation families in C#.

Matching strips HTML tags, decodes entities, collapses whitespace and uses invariant case handling. KEEP guards short-circuit in explicit order; otherwise all corroborated rejection rules are collected. No corroboration means KEEP. Regex operations have 25 ms timeouts. Oversized inputs or matching timeouts fail open to KEEP, explicitly flagged. The engine never writes or discards postings.

Each result records ruleset version, raw-file SHA-256 fingerprint, posting-input fingerprint, decision, categories/reasons, matched rule IDs, observed positive evidence (predicate/scope/text), KEEP guards and fail-open status. Evidence is diagnostic matching evidence, not a new label or inferred truth. Engine fallback IDs identify unmatched/failed-open orchestration, separately from declarative rule IDs.

Rules are strictly loaded at startup into an immutable singleton snapshot. Configure `CheapTriage:RulesetPath` only through the normal reviewed deployment process, then restart. Editing a file cannot change an already loaded engine. Existing Job Fit RegEx reload/import controls operate on the separate SQLite subsystem. Retain every released version file; create `1.0.1.json` (or an appropriate new semantic version), preserve existing IDs where semantics are continuous, and evaluate both versions before activation. Formatting changes also change the content fingerprint. `.gitattributes` fixes `CheapTriage/**` to LF so Windows and Linux use the same pinned bytes. No persistence migration is required.

## Admin maintenance workflow

Open Admin and select **Update RegEx Rules** in the RegEx panel. The modal retrieves a complete server-generated prompt, displays the loaded rule/template versions, and offers **Copy Prompt** and **Close**. Copy includes the entire prompt. A selection/copy fallback is available when the browser clipboard API is unavailable; errors remain visible and the modal can always be closed. Closing aborts pending fetches and restores focus. Text is rendered as textarea content, never HTML.

The authenticated Admin-only, rate-limited, no-store GET endpoint is `/api/admin/cheap-triage/maintenance-prompt`. `RuleMaintenance.Generate()` is independently callable and returns template version, loaded ruleset version/fingerprint, path, execution mode and prompt. `CheapTriage/maintenance-prompt-v1.txt` is template version 1.0.0; `CheapTriage/evaluation-context.json` packages current available metrics and corpus/review provenance. Metrics are explicitly marked stale when their ruleset hash differs from the loaded rules. No private posting bodies are fetched into the prompt.

The template covers Windows source location (override `CheapTriage:RepositoryPath` if necessary), `ssh curiosity-codex`, exact active version/path/hash, frozen datasets/reviews, before/after metrics, high KEEP recall, described/title-only cohorts, no holdout tuning, conservative candidate rejection, all validations, preservation of research and Job Fit/Qwen, and no commit/push/deploy without explicit authorization. The prompt can be copied into Codex; this action itself performs no execution or rule mutation.

## Reproducible comparison

Build Release, then run:

```text
python -B scripts/evaluate-cheap-rules.py --require-frozen-parity --output /path/to/new/baseline-report
python -B scripts/evaluate-cheap-rules.py --baseline CheapTriage/rulesets/1.0.0.json --candidate CheapTriage/rulesets/1.0.1.json --output /path/to/new/candidate-report
```

The script invokes the actual C# application through `dotnet bin/Release/net10.0/JobSearchManager.dll --cheap-triage evaluate <rules.json> <postings.jsonl>`. This early CLI branch initializes neither the application server, database, nor model runtime. The frozen 3,198 corpus and 1,503 old-cache inputs are SHA-256 checked against supervised_v2 splits. AMBIGUOUS remains separate; only KEEP/REJECT labels enter binary accuracy. Cache rejection volume is over the whole cache; accuracy/precision there applies only to its existing audited labeled subset. Do not interpret that subset as an unbiased cache safety estimate.

Outputs include every decision and its provenance, combined/described/title-only metrics, false-reject IDs, ambiguity outcomes, cache volume, fail-open count and every changed decision. Candidate increases in false-reject counts fail comparison; human review must additionally inspect replacements/new families and ambiguous decisions even when counts are unchanged. Wall timing includes process startup, JSON serialization and full diagnostics, not just regex latency. The script never discovers or reads the blinded production holdout and refuses output under the preserved research directory.

The refactor must retain exact frozen decisions. Current baseline: combined KEEP recall 99.5757% (9/2,121 false rejects), described 99.0153% (9/914), title-only 100% (0/1,207), binary rejection 42/2,669, old-cache rejection 208/1,503 (13.839%). These are provisional machine labels, not certification for production discard. The earlier balanced 166-row development set is diagnostic and not representative cache prevalence.

## V2 execution boundary

Keep `RuleMaintenancePrompt` as a request artifact, independent of clipboard/UI. A future separately authorized executor can accept that artifact plus its expected loaded fingerprint, create an isolated `codex/` worktree/branch, run a bounded rule-update task, evaluate the frozen corpus, and return a proposed diff and validation artifact. Require Admin review, normal PR/CI and explicit deployment approval. Reject stale fingerprints and retain request/output provenance. Authentication, sandboxing, cancellation, budgets, concurrency and approval handling belong to that executor, not this GET endpoint. V1 intentionally has no process launcher, SDK call, credentials, scheduler, automatic edits or deployment action.
