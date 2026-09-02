# RegEx migration baseline

Captured read-only on 2026-09-01 through the canonical `ssh curiosity-codex` alias before any
production mutation.

## Deployed baseline

- JSM commit/image revision: `09a019deddd00f06e51225ef80f3b064db9ea914`
- JSM health: healthy
- Compose: `2.40.3+ds1-0ubuntu1~24.04.1`
- Running project services: JSM, `job-classifier`, Ollama, and Mailpit; all healthy
- `ai801`: separately managed and running; outside the JSM Compose replacement scope
- Persistent application, Data Protection, and backup directories: present
- DeBERTa model directory: approximately 715 MiB
- Ollama model directory: approximately 2.4 GiB; pinned Qwen model approximately 2.5 GB

The production file inventory and SHA-256 hashes were captured in the migration work log without
copying account contents into the repository. Representative immutable hashes included accounts
`56cca…`, primary workspace history `372c…`, and primary workspace settings `192ec…`.

## Existing state

Job Fit was enabled in two of three inspected workspaces. The populated primary workspace contained
77 configured signals. Existing cached semantic state was mixed: one large source cache had 259/259
completed DeBERTa classifications and six Qwen results; another had 59 completed LLM-era results,
177 pending, and one unavailable. Other caches were predominantly pending. These observations define
the cache-invalidation/backfill baseline; the migration does not fabricate current RegEx results.

## Recovered implementation and benchmark

The last complete RegEx detector and catalog were recovered from immediately before removal commit
`a9e6a5b`. Recovery preserved title evidence/exclusions, posting evidence, local negation windows,
required-context groups, remote designation/signal categories, extended-location signals, and
concept aggregation.

The recovered fixed corpus has 148 fixtures, 1,740 labels, and 85 concepts. Independent reproduction
of the historical eight-concept benchmark produced:

| Aggregate | Precision | Recall | F1 |
| --- | ---: | ---: | ---: |
| Macro | 0.995370370370370 | 1.000000000000000 | 0.997641509433962 |
| Micro | 0.993865030674847 | 1.000000000000000 | 0.996923076923077 |

Full recovered-corpus results were macro F1 `0.894597869692249` and micro F1
`0.915770609318996`. The versioned validation-corpus fingerprint is
`9e39bf1bf01b65f55a149f4f164fd0c226c5f2c033fc8e8da9872e02f41a04af`; the initial migrated
ruleset fingerprint is `9d491a52ed1046fa319f2b6b9f4d81048b77f9d4a6774d803a14afee5ae06d2a` with 288 runtime rules.

A clean Release maintenance run initialized SQLite, compiled the rules, evaluated all 148 fixtures,
persisted the report, and emitted JSON in 3.078 seconds on the Windows development host. This is an
end-to-end cold maintenance measurement, not a per-posting latency claim.

## Cutover boundary

This document records the pre-change state only. Production cutover is permitted only from a merged,
exact-SHA CI candidate. Verification must cover JSM health/version, SQLite overview/evaluation,
cache reclassification, optional Qwen lifecycle, Mailpit continuity, and `ai801` non-interference.
