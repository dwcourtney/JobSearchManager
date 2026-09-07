# Live Shadow audit v1 — 2026-09-06

Observation and design evidence only. This directory is not a classifier, training dataset, production rule bundle or deployment input. Original historical labels/research are unchanged.

Read [the audit report](../../../docs/live-cheap-triage-shadow-audit.md) and [the pipeline design](../../../docs/classification-pipeline-design.md).

## Files

- `observations.jsonl`: 2,284 cache-instance observations, stable IDs/titles, input/rule identities, exact rule output/evidence, coverage, cached source availability and provenance. Deduplication yields 2,205 jobs. Full posting bodies are not copied here.
- `review.jsonl`: 157 independent audit notes (100 rejects, 55 keeps, two undetermined), including matched rules, judgment, sample type and already-captured Job Fit scores where available. These are Codex interpretations, not human ground truth or historical relabels.
- `summary.json`: recomputable aggregate/employer/provider/input-coverage/sample metrics.
- `manifest.json`: original 14 query definitions, exact cache paths/input-output hashes, release/image identity, raw archive and reporting hashes, sampling rules and limitations.
- `adjudications.tsv`: manually authored audit-note input, keyed to the frozen sorted unique posting list; joins are materialized by stable ID in `review.jsonl`.
- `scores.json`: nine scores already rendered in the signed-in Parsons browser list. They depend on that user's profile. No score was calculated to populate missing entries; they are not occupational truth.
- `build-report.py`: Python standard-library-only reporter/verifier. No model/rules evaluation, provider access or production writes.
- `production-integrity.json`: final read-only health/version/Shadow/cache checks, with deployment container identities and start times.
- `validation.json`: checks performed and results/limitations. No new CI/deployment is claimed.

## Reproduce the portable metrics

From repository root, on Windows or Linux with Python 3.9+:

```text
python -B Tests/cheap_reject_evaluation/live_shadow_v1/build-report.py --validate-only Tests/cheap_reject_evaluation/live_shadow_v1
```

This recomputes summaries from every cache instance, verifies the deterministic sample and annotation joins, checks original-observation preservation and version/hash/adapter identities, and checks portable artifact hashes. It never opens a holdout or contacts a server.

## Reproduce the report from frozen raw inputs

Full cache copies and runtime logs remain on curiosity:

```text
/home/codex/jsm-lab/experiments/live-shadow-audit-20260906/
  manifest.json
  records.json
  collect.py
  analyze.py
  adjudications.tsv
  scores.json
  build-report.py
  production-integrity.json
  <employer>-<full-query-fingerprint>/
    input.json
    output.json
    runtime.log
    data/
```

Raw `records.json`, original manifest and original collector hashes are in the portable manifest; each cache input/output has its own hash. `data/` is isolated audit state, not production data. Full cache bodies are retained there rather than duplicated in Git. No model/checkpoint artifact was created by this audit.

On curiosity, using those frozen inputs:

```text
python3 -B /home/codex/jsm-lab/experiments/live-shadow-audit-20260906/build-report.py --raw-dir /home/codex/jsm-lab/experiments/live-shadow-audit-20260906 --output /tmp/jsm-shadow-report-reproduction-NEW
```

The output directory **must not exist**. Compare the three generated portable artifact hashes to the manifest. This is reporting only, not another inference run. The collector is preserved as historical execution evidence; **do not rerun its hard-coded output path over this archive**. Any future collection must use a new archive, an explicit cache inventory and no-network isolated containers, preserving original observations and recording timeout variability separately.

## Collection boundary

The exact deployed image was already present; no rebuild/pull or provider download occurred. Each copied cache was loaded through normal `JobCatalog` initialization and the live Shadow worker in a Local-mode audit container with `--network none`, `--read-only`, temporary `/tmp`, no capabilities and only its own audit-data mount. Synthetic settings select the copied query and disable startup refresh/browser launch. No user authentication/profile/history state was copied into these containers. The normal startup may reconcile other derived fields **inside the isolated copy**; no such derived values are used as audit ground truth or copied to production.

Parsons' 268 observations predated collection and remained identical. Other employers' results come from the deployed runtime in isolated reconciliation, not ordinary user traffic. All original production cache bytes and workflow state were verified unchanged. This distinction must accompany any reuse of these artifacts.

Sampling is category-stratified and includes targeted risk cases, so sample proportions are not unbiased precision/recall estimates. Cache overlap with earlier research, weak machine judgments, missing descriptions and repeated requisitions prevent safety certification. Do not silently train on this review file or treat KEEP as Technology labels.
