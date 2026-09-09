# Refreshable concept evaluation

This is an explicit offline maintenance workflow. It does not add a model, scheduler,
database, mutable Admin endpoint, or inference dependency to normal JSM. The web app
reads only hash-verified compact reports and `index-v2.json`. Historical v1 reports
are unchanged. `tools/` is excluded from application compile, publish and Docker input.

## Meaning and limitations

These are real precision/recall/F1 calculations **against frozen AI-reference labels**,
not independent human ground truth or an estimate of population production accuracy.
Two prediction-blinded labeling passes use fresh contexts. Disagreements and nulls
receive a third blinded decision without seeing JSM predictions or A/B answers.
Any remaining null is excluded from all scored denominators and counted explicitly.
Using the same model in fresh sessions does not remove correlated model errors.

Labels follow the exact frozen `JobConceptCatalog` definitions. Every posting has an
explicit boolean/null decision for every concept; absent responses are never assumed
negative. Positive labels require a verbatim evidence quote. Posting text is untrusted
data and cannot instruct the labeler. The labeler receives title, company, locations,
plain posting text, definitions, and ordered IDs only. It never receives production
predictions, classifier evidence, rules, workflow state, account data, or another pass.

Sampling includes only available cached descriptions of at least 200 plain-text
characters. It deduplicates stable job IDs across workspaces and normalized identical
descriptions, keeping the most recent copy. Near duplicates may remain. Seeded
proportional joint strata use company, title role family, remote/hybrid wording,
technical/management title cues, travel wording and lexical breadth. These are
documented proxies; lexical breadth is **not** measured concept density. True positive
and negative support is reported only after labeling. Cached jobs may reflect the
user's search preferences, so coverage is not representative of the whole labor market.

## Monthly process (no scheduler)

Use Python 3.11+ and .NET 10. Work in an external run/archive directory, not inside
the application repository. Replace the paths below with the current checkout,
allowlisted cache export and a **new** run ID. Never reuse a run directory.

1. Read-only export public cached fields from curiosity: `stableId`, `requisitionId`,
   `title`, `companyId`, `primaryLocation`, `additionalLocations`, `descriptionHtml`
   or `compressedDescriptionHtml`, `sourceUrl`, `isSourceAvailable`, `detailCachedAtUtc`.
   Preserve the `job-caches/*.json` layout with `{ "jobs": [...] }` documents. Do not
   export credentials, accounts, workflow state, model fields or cached predictions.
   The supplied exporter performs that allowlist operation:

   ```text
   python tools/concept-evaluation/export_public_cache.py --cache-root <read-only-cache-root> --destination <new-export-directory>
   ```
2. Freeze 500 postings (or the entire eligible population if smaller):

   ```text
   python tools/concept-evaluation/evaluate.py refresh --run <archive>/<new-id> --cache-root <export> --taxonomy JobConceptCatalog.json --count 500 --seed <YYYY-MM-DD>
   python tools/concept-evaluation/evaluate.py export --run <run> --phase a --batch-size 5
   python tools/concept-evaluation/evaluate.py export --run <run> --phase b --batch-size 5
   ```

3. Generate independent A/B labels with an authenticated, supported Codex CLI:

   ```text
   python tools/concept-evaluation/label_codex.py --run <run> --phase a --codex <codex-executable> --model gpt-6-astra
   python tools/concept-evaluation/label_codex.py --run <run> --phase b --codex <codex-executable> --model gpt-6-astra
   ```

   The adapter uses `codex exec`, stdin prompts, `--ignore-user-config`, `--ephemeral`,
   `--skip-git-repo-check`, `--sandbox read-only`, no shell/web, and a temporary empty
   working directory. It rejects any session event other than reasoning and final
   assistant text. This is an audited no-tool protocol, not an OS-enforced guarantee
   that all MCP tools are unavailable. Any tool use fails import. Preserve raw prompts,
   event streams, responses, stderr, requested model, CLI version, session ID and UTC
   times. Backend model revision is not exposed; do not invent one. See the official
   [Codex non-interactive documentation](https://learn.chatgpt.com/docs/non-interactive-mode).

   A failed invocation retains its raw attempt and imports nothing. Re-run the command
   to retry missing batches in fresh sessions. `--limit`, `--shard`, and `--shards`
   support bounded operations; do not run overlapping shards for the same phase.

4. Export only disagreements/nulls and obtain a third independent decision:

   ```text
   python tools/concept-evaluation/evaluate.py export --run <run> --phase adjudication --batch-size 5
   python tools/concept-evaluation/label_codex.py --run <run> --phase adjudication --codex <codex-executable> --model gpt-6-astra
   python tools/concept-evaluation/evaluate.py freeze-reference --run <run>
   ```

5. **Only after reference freeze**, run the unchanged detector offline:

   ```text
   dotnet run --project tools/ConceptEvaluation/ConceptEvaluation.csproj -c Release -- <run>/sample.json <run>/predictions.json
   python tools/concept-evaluation/evaluate.py evaluate --run <run> --policy evaluation/concept-detection/score-policy-v1.json --metric-policy evaluation/concept-detection/metric-policy-v2.json
   python tools/concept-evaluation/evaluate.py publish --run <run> --destination evaluation/concept-detection
   ```

6. Inspect metrics, low-support concepts, PR points and disagreement examples. Retain
   the **entire** external run directory in the research/evaluation archive. Publish
   only the compact `run-<id>.json` and updated index in the app. Test and deploy through
   the existing application workflow. There is no live Admin "relabel" button.

The same commands serve external/manual labeling: give a new isolated labeler exactly
one exported batch JSON. Import a JSON envelope with `identity` (nonempty `model`,
`tool`, `sessionId`, `startedUtc`, `completedUtc`) and `response`:

```json
{
  "identity": {"model":"actual model", "tool":"actual tool/version", "sessionId":"unique session", "startedUtc":"UTC ISO date", "completedUtc":"UTC ISO date"},
  "response": {
    "batchId":"exact exported batch ID", "inputHash":"exact exported hash",
    "decisions":[{"id":"exact posting ID", "labels":[true,false,null], "evidence":{"concept.id":"Exact quote"}}]
  }
}
```

The illustrated vector is abbreviated: actual imports require the exact number and
order of requested concepts for each posting. Import with:

```text
python tools/concept-evaluation/evaluate.py import --run <run> --phase a --batch <batch.json> --response <envelope.json>
```

## Diagnostic evidence score and PR

`concept-evaluation-score-v1` uses `1 - 2^(-units)`. A distinct accepted body-evidence
rule signature contributes one unit (.5 alone); title-specific evidence, a typed
parsed-fact selector or a complete context group contributes two (.75 alone). Additional
distinct signatures increase the rank with diminishing increments. This is a simple
ordinal evidence policy, not fitted weights, calibrated probability, or proof of
statistical independence. Duplicate signatures and repeated occurrences add nothing;
a context group contributes once. Only actual accepted matcher IDs contribute, so
exclusions, local negation and failed groups never add score. Production binary semantics
and Job Fit are unchanged. Missing rule evidence generally scores zero, including false
negatives; the ranking cannot recover concepts absent from the rules. Policy bytes and
hash are frozen independently from detector identity, without tuning to this reference.

Micro sums TP/FP/FN/TN over resolved posting × concept decisions. Macro averages all
taxonomy concepts; undefined precision/recall/F1 use zero. PR thresholds are the distinct
observed diagnostic scores, descending, with all ties entering together and a starting
point above all scores. Each point stores its confusion counts. AP is the sum of
`delta recall * precision` at those thresholds, **not trapezoidal area**. No PR AUC is
claimed. Per-concept curves require at least 5 positives and 5 negatives. AP is null if
there are no positives. Raw points, not an interpolated binary curve, drive the chart.

The compact report contains latest/previous comparison, per-concept results, PR points
and FP/FN/unresolved examples. Different samples/references make score changes descriptive;
they cannot be attributed solely to rule changes. CURRENT requires age at most the
index's `freshnessDays` (default 45, supported 1–365), and identical pipeline, taxonomy,
rules, score policy and metric policy hashes. Otherwise it is STALE. Invalid checksums
are visibly excluded and the latest valid run is selected. No valid run retains the
honest production-accuracy-not-measured fallback.

## Verification

```text
python -m unittest discover -s tools/concept-evaluation -p test_evaluate.py -v
dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release
pwsh -NoProfile -File scripts/validate-source.ps1
```

All new run files use exclusive creation. Sample/taxonomy hashes are checked before
label steps, labels are frozen with input hashes, predictions carry sample/authority
identity, and the manifest hashes raw artifacts. Only the published index is mutable.
Do not manually edit a frozen artifact to repair a run; create a new run when inputs or
policy must change. Raw artifacts can contain public posting text and should remain
outside normal app output and source history unless deliberately archived.
