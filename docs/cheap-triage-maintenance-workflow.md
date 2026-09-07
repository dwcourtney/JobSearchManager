# Guided Cheap Triage rule maintenance

The top Admin card is the single entry point. Every job continues through normal Job Fit. This workflow cannot activate rules or invoke Codex.

| State | Primary action | Result / next step |
|---|---|---|
| Human Review Required | Start Human Review | Open the queued cases inside the card; save adjudications. |
| Human Review In Progress | Continue Human Review | Resume the existing queue and saved decisions. |
| Human Review Complete | Prepare Codex Rule Update | Export immutable evidence and record the compact v2 prompt handoff. |
| Prompt Ready | Copy Prompt | Paste into Codex. JSM cannot detect an external session finishing. Return with candidate-result.json. |
| Candidate Evaluation | Approve Candidate / Reject Candidate | Record an administrator's disposition. Neither action deploys or changes rules. |
| Approved | Separately authorize release | Explicitly authorize commit/push, CI and normal deployment. No deployment is inferred or recorded by this workflow. |
| Rejected | Prepare Revised Codex Rule Update | Request a revised analysis, then import its result for review. |

Completed/current/upcoming steps are textually and visually marked. Human Review opens inline; diagnostics and complete changed-decision evidence remain expandable. Imported results are reported evaluation evidence, not independent verification by JSM. Identity/schema checks and safety checks prevent stale/malformed packages or reported regressions from being approved; they cannot prove an external report is truthful or exhaustive.

## Codex result handoff

Codex should perform frozen and cached-live evaluation first, preserving all historical evidence. Use the existing evaluation CLI and reports. Do not train models, fetch providers, activate rules, or change Job Fit. Build a result package from already evaluated artifacts:

```powershell
python scripts/package-cheap-triage-candidate.py --snapshot docs/evaluations/human-review-snapshots/<bundle-sha256> --comparison <frozen/comparison.json> --changes <all-changed-decisions.json> --human-comparison <human-comparison.json> --rules CheapTriage/rulesets/<candidate-version>.json --validation PASS --output candidate-result.json
```

`--validation` must reflect completed validation (PASS, FAIL or INCOMPLETE); packaging does not run tests. Human comparison is an array of `{id, human, baseline, candidate}` with exact saved human objects and complete before/after engine decisions for every queue ID. Changes is the exhaustive frozen AND live changed-decision array containing IDs, titles, before/after decisions and evidence. Duplicate postings across datasets remain distinguished by their dataset field. The script verifies snapshot hashes, rule/baseline identity and human provenance. It refuses to overwrite an output file. Keep the package with its analysis artifacts.

The package includes snapshot identity, baseline and candidate version/hash, exact candidate rules text, human corrections count, paired KEEP/described/title-only recall, rejection and old-cache rejection, false rejects, every changed decision, warnings, safety failures and validation status. It contains data only. The server validates the declarative schema without executing job evaluation or changing the loaded ruleset.

Supply it using the ordinary Admin file picker (no pasted JSON), or from the Windows checkout:

```powershell
./scripts/sync-cheap-triage-candidate.ps1 -Path <candidate-result.json>
```

The SSH alias is curiosity-codex; optional `-HostName 192.168.1.20` handles alias DNS overrides. The script verifies the transfer hash and publishes one inbox file atomically. Then click **Import Synced Result**. Curiosity does not automatically read the Windows checkout, and neither sync nor import implies approval. The inbox is staging; each imported package is preserved in the workflow history before it is shown for decision. Import mismatch errors require reevaluation against the current prompt snapshot, not changing hashes to disguise stale evidence.

## Persistence and safety

Separate durable JSON journals live in `/app/data/cheap-triage-maintenance/` (local mode: `data/cheap-triage-maintenance/`; configurable with `CheapTriage:MaintenanceDirectory`). Each journal key hashes queue provenance, saved human revisions/notes and loaded baseline fingerprint. Changing any of these starts a new workflow while preserving old journals. Prompt preparation, imports and dispositions append reviewer, timestamp, revision and exact evidence; atomic file replacement prevents partial journals. Optimistic revision checks reject concurrent stale actions. This uses the existing single-instance application architecture, not a distributed multi-writer store. Journals sit in the existing persistent app-data mount and its whole-data backups, separately from the Human Review SQLite database and immutable snapshots.

The APIs inherit Admin authorization, rate limiting, no-store responses and the existing same-origin write protection. Candidate packages are limited to 2 MB; rules are bounded by the existing declarative schema. Content is rendered as text. Approval requires reported PASS, no listed safety failures, no new KEEP-to-REJECT rows, no recall/count regression, and at least 98% combined/described KEEP recall. It only records acceptance. Failed candidates can be rejected or replaced. No automatic release, Active mode, provider request or model inference is introduced.


## Single-next-action presentation

Only the current state's primary action is rendered. Completed imports and dispositions never leave their old controls behind. Candidate decisions are secondary-styled choices inside an explicitly opened review. Change Decision opens those choices; viewing details alone does not. Human Review uses Save and Next as its only dominant action while the review is open.

| Rendered state | Primary next action |
|---|---|
| REVIEW_REQUIRED | Start Human Review |
| REVIEW_IN_PROGRESS | Continue Human Review |
| REVIEW_COMPLETE | Prepare Rule Update |
| CODEX_ANALYSIS_REQUIRED | Copy Codex Prompt |
| CANDIDATE_READY | Review Candidate |
| CANDIDATE_APPROVED | Prepare Release Request |
| CANDIDATE_REJECTED | Prepare Revision Request |
| RELEASE_REQUEST_READY | Copy Release Prompt |
| RELEASED | None |

Preparing a release appends a revision-checked `release-prepared` event with a server-template-generated prompt, preserving the candidate and approval. It invokes no external process and changes no rules. A later rejection invalidates the release prompt. Existing approved journals migrate without modification. The release template is CheapTriage/release-prompt-v1.txt.

Release is not inferred from an approval, import, copied prompt or smoke build. After normal committed deployment and successful validation, the separately authorized release operator writes `<workflow-key>.release.json` atomically in the existing persistent cheap-triage-maintenance directory:

```json
{"workflowKey":"<approved workflow key>","candidateHash":"<sha256>","rulesetVersion":"<version>","deploymentIdentity":"<full deployed commit>","validationStatus":"PASS"}
```

The read-only workflow checks this receipt against the loaded rule version/hash, the running assembly's committed build identity, the retained release-prepared candidate and the exact human-review provenance under its former baseline. A mismatched/malformed receipt is ignored; changed human decisions start a new cycle. Validation status is an operator attestation, not an independent CI lookup. There is no UI action that marks a candidate released and no automatic synchronization from Codex. Release instructions require supplying this small receipt only after deployment verification. This directory is already covered by whole-app-data backups.
