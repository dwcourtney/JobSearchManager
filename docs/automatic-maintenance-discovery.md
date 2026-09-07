# Automatic Cheap Triage maintenance discovery

This detector requests human review; it never classifies, hides, downloads, scores, or changes jobs or rules. Shadow must already be enabled. It reads persisted current Shadow observations from `shared/job-caches` under the existing application data root every five minutes, including per-workspace caches. It does not instantiate a job source or a workspace runtime. Admin polls the saved workflow every minute without remounting an open review draft.

## Trigger policy v1

`CheapTriage/maintenance-trigger-policy-v1.json` is pinned by its file SHA-256 in each discovery ledger and selection manifest. It is separate from the unchanged Cheap Triage ruleset. Occupational risk references are declarative rule/predicate IDs, not C# occupation patterns.

A new queue requires all of:

- At least 50 distinct new normalized title/duty fingerprints since discovery began for the completed cycle.
- At least 12 eligible, distinct representatives, across at least three employers.
- Each representative scores at least 5. Maximum batch: 24; maximum per employer or exact rule combination: six.

Scores: novel pattern +2; REJECT +1; under-reviewed rule (fewer than three reviewed examples) +3; rule previously associated with a human KEEP disagreement +3; title similarity to a prior human KEEP +4; unseen rule combination +2; configured risk rule/predicate +3; UNDETERMINED/fail-open +4. These are review-selection signals, not predicted human labels or a safety claim. Straightforward new rejects from well-reviewed patterns do not automatically qualify.

Normalize Unicode letters/case, ignore numbers, configured seniority markers and stopwords. Exact normalized title/duty fingerprints collapse across employers/IDs. Prior reviewed patterns and within-batch near duplicates are excluded at title token Jaccard >=0.80 and duty token Jaccard >=0.65; title-only duplicates need only the title match. Prior KEEP title similarity >=0.45 is a ranking signal. Employer round-robin selection, score ordering, ordinal fingerprint tie breaks, and fixed caps make replay deterministic for saved inputs. Changes to duties can qualify even for a previously seen job ID.

These conservative operational defaults are not statistically calibrated. A one-employer or one-rule flood may intentionally stay idle. Distinct fingerprints count toward the 50-observation threshold, but near-duplicate candidates cannot inflate the minimum 12 representatives. Employer/product names are not classification signals.

## Lifecycle and persistence

Only a completed `NO_UPDATE_NEEDED` or verified `RELEASED` workflow permits discovery. Initial startup seeds all currently available/current cached fingerprints as baseline: existing data is not retroactively treated as fresh evidence. This deliberately forgoes evidence arriving before the detector was installed. Only observations analyzed after that persisted initialization watermark qualify; discovering an older cached analysis later does not count as fresh evidence. Subsequent scans accumulate new evidence durably even if a posting later disappears. Missing/stale observations are not evaluated or reconciled by this detector.

The application data mount contains:

- `cheap-triage-discovery/<completed-workflow-key>/observations.json`: completed key/revision, previous queue fingerprint, ruleset identity, policy hash, initialization time, baseline and new input fingerprints, and eligible observation snapshots.
- `trigger-policy.json`: exact policy bytes used for that ledger.
- `<hash>.manifest.json`: immutable selection provenance, considered fingerprints, selected cases, scores/reasons, ruleset/policy identity, and the ledger hash.
- `cheap-triage-review-queues/<queue-hash>.json`: immutable queue, version `auto-v1-<manifest-hash>`, with source-manifest hash and input/rule evidence.
- `cheap-triage-review-queues/active.json`: atomically replaced pointer to the next queue. Queue content hash is checked on load.

Human decisions remain in the existing SQLite revision store, scoped to queue hash. Creating a queue writes no human decisions and does not rewrite the historical 29-item source or workflow journal. Stale-browser saves to an old queue are rejected. Queue activation rechecks the completed cycle under the review-store lock. There is only one open review cycle. All prior queues/decisions remain retained and inform later duplicate suppression. The normal whole-data backup covers these new directories alongside the separately backed-up SQLite review database.

No new review/scan button is required. Below threshold the completion card says “No action required” and can show the count collected. At threshold the existing Human Review Required state automatically loads the new blank queue and offers Start Human Review. Technical detection provenance remains collapsed.

## Failure and resource behavior

Errors leave jobs/rules/reviews untouched and retry on the next interval; Admin diagnostics report unavailability. A malformed cache stops that scan rather than publishing a partial queue. Limits: 64 MB cache file, two million decompressed description characters, 50,000 ledger fingerprints. Selection uses at most 8,000 body characters (deterministic head/tail); the review shows concise head/tail excerpts, with the captured text available in technical JSON. No generative or learned inference runs.

A policy or completion-revision mismatch pauses detection rather than silently reinterpreting its ledger. Future policy migration requires an explicit audited migration; no old policy or ledger is overwritten. Multiple independently writing JSM replicas are not supported by the file-backed detector/queue publication, consistent with the current single curiosity instance.

## Validation

`MaintenanceDetectionTests` covers bootstrap/no evidence, insufficient evidence, duplicate/stale observations, restart accumulation, sufficient diverse cases, deterministic selection, near duplicates/prior review exclusion, anomalies, atomic versioned queue reload, stale saves, separate new decisions, unchanged old DB/queue/journal during discovery, and Off mode. Workflow UI tests cover automatic idle-to-review transitions and draft preservation. Source validation asserts no provider/runtime/model integration in the detector. Existing Shadow and Job Fit regressions remain applicable.
