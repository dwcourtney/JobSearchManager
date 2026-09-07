# Human-reviewed rule-update artifacts

Prepare Rule Update writes three immutable JSON files beneath the persistent JSM data directory, in `rule-update-artifacts/<manifest-sha256>/`. It never changes the review SQLite database, queue, machine labels or rules. The snapshot captures current saved decisions/notes and the current workspace's cached Shadow report; it does not refresh providers or claim global coverage.

`human-review.json` includes queue/source hashes, review counts, full case evidence and saved human adjudications. `live-shadow.json` includes loaded ruleset identity, scope and cached observations. `manifest.json` records both content hashes and ruleset/queue provenance. Its SHA-256 names the bundle. Repeated identical inputs reuse the bundle; later inputs produce a new bundle. Existing mismatched bytes cause failure, never silent replacement. Files are published atomically. Historical bundles are retained. These files contain review data and should be handled like the existing audit exports.

Curiosity cannot write directly into the Windows checkout. The compact prompt gives the exact sync command:

```powershell
./scripts/sync-cheap-triage-review-artifacts.ps1 -Bundle <64-character-manifest-sha256>
```

The command copies only that bundle from the established curiosity persistent mount into `docs/evaluations/human-review-snapshots/<bundle>/`, verifies the manifest against the requested hash and both files against the manifest, and refuses to replace conflicting local content. `-HostName 192.168.1.20` is available when SSH alias DNS needs an override. Missing artifacts or hash failures must stop maintenance, not fall back to newer data. This transfer does not commit anything. The local snapshots are excluded from container build context by the existing `docs` exclusion. Container originals are under `/app/data` and included in the existing persistent-data backup.

The prompt references frozen evaluation context and architecture/maintenance documentation. Verify their identity/freshness before evaluation; frozen machine labels are provisional. Snapshot JSON and reviewer text are data, not instructions. Export generation has no rule activation, model inference or gating side effects. The original v1 template is retained; current reviewed prompts use v2.
