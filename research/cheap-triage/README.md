# Cheap Triage: explicit research and historical evidence

Cheap Triage is retired from normal JSM. This project is never registered in web DI, hosted services, endpoints, Admin, startup or normal deployment. Its executable supports offline replay, frozen-reference evaluation and explicit historical SQLite backups. The normal Job Fit path remains deterministic concept detection and the existing score.

Historical root paths `CheapRejectRules.cs`, `CheapTriageShadow.cs`, `CheapTriageHumanReview.cs`, `CheapTriageMaintenance.cs`, `CheapTriageMaintenanceDetector.cs`, `RuleMaintenance.cs` and `TriageEvaluation.cs` are linked only into this research project. `JobCatalog.CheapTriage.cs` remains historical, excluded from all current executable projects. Normal production retains only passive serialized observation contracts in `LegacyTriageCache.cs`.

## Reproduce evidence (from repository root)

```powershell
dotnet restore research/cheap-triage/Tests/Jsm.CheapTriage.Research.Tests.csproj --locked-mode
dotnet run --project research/cheap-triage/Tests/Jsm.CheapTriage.Research.Tests.csproj -c Release --no-restore
python scripts/evaluate-cheap-rules.py --candidate CheapTriage/rulesets/1.0.1.json --require-frozen-parity --output <new-output-directory>
dotnet run --project research/cheap-triage/Jsm.CheapTriage.Research.csproj -- --cheap-triage evaluate CheapTriage/rulesets/1.0.1.json <frozen-corpus.jsonl>
dotnet run --project research/cheap-triage/Jsm.CheapTriage.Research.csproj -- --human-review-backup <historical-source.db> <new-backup.db>
dotnet run --project research/cheap-triage/Jsm.CheapTriage.Research.csproj -- --triage-holdout evaluate <explicit-evaluation-directory>
```

`--triage-holdout freeze <directory>` explicitly freezes references. Work against copies and new output directories. No research command is called by normal CI/deploy. The preserved discovery implementation is exercised only through explicit research tests; it is not started by this CLI.

## Preserved paths

- `CheapTriage/`: unchanged 1.0.0/1.0.1 rules, review queue, prompt/release templates, context and automatic-maintenance policy.
- `Tests/cheap_reject_evaluation/`, `Tests/cheap_triage_rules/`, `docs/evaluations/`: unchanged corpora, 29-case human-review evidence, shadow audit, candidate/no-update receipts, snapshots, hashes, manifests and historical outputs.
- `Tests/CheapRejectRuleTests.cs`, `Tests/HumanReviewTests.cs`, `Tests/MaintenanceDetectionTests.cs`: original research regression suites linked into the separate test project. The former runtime Shadow integration test remains at `Tests/CheapTriageShadowTests.cs` as historical coverage of the retired integration, excluded from normal tests.
- `archive/`: exact pre-removal web composition, catalog, HTML/CSS/JS, tests and deployment script. The four triage JS assets were removed from live `wwwroot` only after verifying byte-identical archive copies.
- Historical candidate/review sync and package scripts keep their original paths. The sync scripts describe the retired application inbox/UI and must not be used as a normal deployment workflow. Use copies of archived data for inspection; the current web app exposes no import, review or release endpoints.

## Cache compatibility and rollback window

No cache migration or forced provider refresh occurs. Known legacy `CheapTriage` observations retain the exact previous DTO shape and are carried unchanged through ordinary refresh/detail/cache reserialization. The normal application ignores them for visibility, workflow, analysis and scoring, and suppresses them in detail responses. The observation's serialized computed `decision` retains its original derivation; no matching engine or scheduler is compiled into JSM.

Compatibility and the pre-retirement image/data/config archives must remain available **at least through 2026-10-07 (30 days)**. This date is a minimum recovery window, not a deletion schedule. No automatic archive deletion is introduced. Unknown fields outside the supported legacy DTO shape retain the reader's existing behavior; this task does not broaden schema handling.

Before smoke replacement, stop only JSM briefly; create and verify a read-only archival snapshot of the human-review DB, maintenance/discovery journals, candidate/release receipts, queues and rule-update artifacts. Preserve the original files in place. Record per-file SHA-256 hashes and SQLite integrity/decision counts in the host archive manifest. Full application/data-protection backup also protects non-triage state.

Normal deployment continues the active RegEx backup and all app health/version/security/rollback gates, but no longer opens or depends on the Human Review database. Recovery restores the prior image and archived runtime environment with the existing mounts; restore data only as an explicit recovery action, never automatically overwrite newer workspace data. The smoke result report identifies the exact host archive, backup and rollback script.
