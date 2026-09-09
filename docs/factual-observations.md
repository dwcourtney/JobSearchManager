# Factual observations, version 1

This substrate is not Taxonomy v10 and does not score jobs. Authorization and compensation now have extraction records followed by the existing legacy selection/aggregation. Public parser contracts, analysis versions, packaged legacy rules, preferences and cache identities are unchanged.

## Contract

`FactualObservation` retains domain/type, a normalized `FactValue`, matched surface text, obligation, qualifier/polarity, scope, evidence coordinates, parser/rule identity, source/provider, logical connective, exclusions and relationships. `FactValue` supports categorical values, OR alternatives, numeric bounds, currency (including a distinct upper-end currency), units and compensation basis. Unknown fields are null or explicitly `unknown`; absence does not mean false. No model confidence is supplied.

`FactScope` retains section, geographic, temporal, level and employment applicability and the unabridged applicability text. `FactObservationDocument` retains original HTML and normalized segments. Offsets are explicitly in a normalized segment or salary line/flat-text coordinate space; they are **not** claimed to be original HTML offsets. Evidence contains its full normalized segment so coordinates are independently checkable. Raw values are matched surface text in that space; original spelling/markup remains in `OriginalHtml`.

IDs are SHA-256 over observation contents, source, scope, rule identity and evidence location. Repeated identical evidence at different locations is retained. Overlapping rules remain separate; rule duplicates are not silently interpreted as independent confidence. Different geographic scopes, alternatives, providers and evidence offsets cannot collapse into one ID. Relationships reference these IDs.

Lossless means retaining recognized assertions, alternatives, source context and unprojected candidates rather than irreversibly reducing them to the legacy summary. It does **not** mean every possible natural-language requirement has a complete semantic parse. Unrecognized scope remains raw, not guessed. Future consumers must respect unknown/context-unresolved fields and the retained original text.

## Authorization

`Extract` collects context-qualified rule candidates before first-specific/only-when-unset selection. `Summarize` applies the existing ordered policy and six-evidence legacy cap to those candidates. It does not parse the posting again. Later eligibility alternatives remain in the extraction record even when v9 selects only the first one.

`Observe` exposes candidates and repeated matches, plus bounded supplemental recognition of sponsorship availability/unavailability and standalone permanent residency. This new recognition never enters `Summarize`. Conditionality, section context, AND/OR and negated requirements remain visible. Sponsorship disagreements create `potential-contradiction-review-scope` relationships; no resolver decides whether differing conditions make them compatible. Eligibility alternatives preserve their members and surface clause.

Supplemental rules live in `rules/factual-observations-v1.json`, are validated when inspection requests them, and use a 250 ms maximum regex timeout. They are not a new runtime service and do not alter the v9 concept pipeline identity.

## Compensation

`ExtractSalary` retains legacy range candidates, rule/coordinate provenance, numeric interpretation and priority. `SummarizeSalary` selects the same priority and aggregates only the same summary-range candidates as before. Deferred extraction errors are rethrown only if legacy selection would have evaluated that candidate; a lower-priority overflow must not replace a valid role-specific range.

`CompensationObservations.Observe` additionally retains separate amounts/ranges, local headings, geography, level, employment terms, explicit period/currency and base/bonus/total wording. Three regional ranges remain three observations, even when the legacy projection deliberately returns their outer envelope. `$` alone does not imply USD, and a large number alone does not imply annual pay in the observations. Mixed currencies retain a separate endpoint and a review qualifier rather than asserting one comparable interval. Partial/reversed ranges remain reviewable. Money without explicit compensation context is labeled `monetary-context-unresolved`.

## Inspection

Build the test project, then run:

```text
dotnet Tests/bin/Release/net10.0/JobSearchManager.Tests.dll --factual-observations INPUT.json OUTPUT.json
```

Input is an array of `{ "id": "posting-id", "html": "...", "provider": "optional", "metadata": { "sponsorship": "available" } }`. Output includes both observation documents, legacy candidates and summaries, and separate provider declarations. Provider metadata never overwrites body observations. The command reads explicit files only; it has no provider client, cache writer, settings mutation or workflow operation. Input/output reports belong outside Git when they contain copied postings.

## Focused audit of remaining domains

| Domain | Actual loss in current output | Consequence for future v10 | Status here |
|---|---|---|---|
| Clearance | Ordered level selection, one requirement, first evidence snippet; negation is stripped before summary | Multiple levels, timing and preferred/required alternatives cannot be reconstructed from the scalar alone | Audited; unchanged |
| Education | Paths retain substantial structure, but grouping drops repeated evidence and section applicability; global experience-substitution boolean and eight-evidence cap | A substitution allowed for one path cannot safely be applied to every degree path | Audited; unchanged |
| Credentials | Merge by credential ID chooses strongest requirement/evidence and ORs equivalence, in-progress and post-hire flags; unknown mentions capped at ten | Flags from different clauses can be combined without their original conditions | Audited; unchanged |
| Remote work | Four-signal cap; numeric travel becomes a band/category and evidence; metadata/body designation is a single boolean | Complete simultaneous duties, numeric bounds and provider/body disagreement need pre-summary observations | Audited; unchanged |
| Extended location | Five-signal cap, one selected destination, duration threshold folded into a category | Multiple destination/duration/rotation observations require their own retained associations | Audited; unchanged |
| Geographic restriction | First matching category/first sentence only; only remote-designated inputs considered; evidence snippet | Multiple jurisdictions, preference versus obligation and exclusions cannot be recovered from that one result | Audited; unchanged |
| Salary | First range selection or regional aggregation loses alternatives/applicability | Addressed by retained candidates and separate scoped monetary observations | Implemented |
| Work authorization | First-specific/replace semantics, scalar sponsorship and evidence cap lose later alternatives and disagreements | Addressed by retained candidates and supplemental observations | Implemented |

The six audited domains have not been rewritten. Accordingly, this release does **not** prove that all 14 proposed v10 fact families are unblocked. It supplies the two requested domain implementations and a reusable contract; further domain-specific traces and extraction coverage are still prerequisites. No v10 implementation or preference migration resumes here.
