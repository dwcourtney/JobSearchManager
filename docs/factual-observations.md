# Factual observations, version 1

This substrate is not Taxonomy v10 and does not score jobs. All eight factual domains expose retained observations/candidates alongside unchanged legacy summaries. Authorization, compensation, education, credentials, remote work and extended location have explicit extraction records followed by legacy reduction; clearance and geography preserve their lazy ordered matching while exposing candidates. Public parser contracts, analysis versions, packaged legacy rules, preferences and cache identities are unchanged.

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

## Audit and completed observation coverage

| Domain | Actual loss in current output | Consequence for future v10 | Status here |
|---|---|---|---|
| Clearance | Ordered level selection, one requirement, first evidence snippet; negation is stripped before summary | Multiple levels, timing and preferred/required alternatives cannot be reconstructed from the scalar alone | Legacy summary unchanged; observations now retained |
| Education | Paths retain substantial structure, but grouping drops repeated evidence and section applicability; global experience-substitution boolean and eight-evidence cap | A substitution allowed for one path cannot safely be applied to every degree path | Legacy summary unchanged; observations now retained |
| Credentials | Merge by credential ID chooses strongest requirement/evidence and ORs equivalence, in-progress and post-hire flags; unknown mentions capped at ten | Flags from different clauses can be combined without their original conditions | Legacy summary unchanged; observations now retained |
| Remote work | Four-signal cap; numeric travel becomes a band/category and evidence; metadata/body designation is a single boolean | Complete simultaneous duties, numeric bounds and provider/body disagreement need pre-summary observations | Legacy summary unchanged; observations now retained |
| Extended location | Five-signal cap, one selected destination, duration threshold folded into a category | Multiple destination/duration/rotation observations require their own retained associations | Legacy summary unchanged; observations now retained |
| Geographic restriction | First matching category/first sentence only; only remote-designated inputs considered; evidence snippet | Multiple jurisdictions, preference versus obligation and exclusions cannot be recovered from that one result | Legacy summary unchanged; observations now retained |
| Salary | First range selection or regional aggregation loses alternatives/applicability | Addressed by retained candidates and separate scoped monetary observations | Implemented |
| Work authorization | First-specific/replace semantics, scalar sponsorship and evidence cap lose later alternatives and disagreements | Addressed by retained candidates and supplemental observations | Implemented |

## Six-domain completion

`FactualDomainObservations` reuses `FactualObservation`, `FactValue`, `FactScope` and `FactRelation`; there is no second fact contract. Its bounded supplemental vocabulary lives in `rules/factual-observation-completion-v1.json`. Existing native rule patterns are retained as explicitly **unfiltered candidates**, with their native rule hash/ID, not presented as resolved requirements. Header/role-level/employment context uses the existing common scope helper. Original v9 rule bytes, analysis versions and authority identities are unchanged.

Statements contain paths; paths contain atomic observations. Explicit alternatives link paths, while conjunctions remain within a path. Field/jurisdiction alternatives stay attached to their predicate. Repeated evidence locations receive distinct IDs. Timing cues, acquisition/possession, intervals, exclusions and source declarations remain separate observations; no resolver chooses a single truth. A potential provider/body disagreement receives a source-comparison review link, not an invented contradiction across different times or scopes.

Clearance retains level, acquisition/current status, negation and timing. Education retains degree/field/experience components and paths, repeated headings and level applicability. Credentials retain individual catalog mentions, unknown assertions and obtain-by intervals. Remote observations retain arrangements, cadence, travel bounds/frequency and separate provider declarations. Extended-location observations retain individual destinations, durations, rotations and relocation-assistance wording. Geographic observations retain inclusion/exclusion lists, radius, time zones and raw applicability without the v9 remote gate.

Inspection has a `domains` object with all eight domains, each containing `observations`, `legacyCandidates` and `legacySummary`. Existing authorization/compensation output fields remain available. Optional input fields `title`, `primaryLocation` and `additionalLocations` are passed unchanged to the native legacy parsers. Provider declarations stay in their own coordinate space.

### Boundary and normalization

Observation presence is not automatically a current-role requirement. `unfiltered-legacy-pattern` records deliberately retain matches before context/exclusion decisions; source/path nodes and unknown qualifiers must be respected. Unknown names and ambiguous syntax remain reviewable rather than being promoted to canonical facts. This is not a complete natural-language understanding claim.

Normal v9 evaluation performs its existing extraction once and uses the existing reducer. Rich inspection also reads the unmodified source: that separate coordinate space is necessary because v9 clearance strips negation, remote work truncates before matching, and other native parsers use different segmentation/evidence policies. Clearance retains the original early-exit/lazy behavior, including timeout order. Inspection may enumerate additional candidates that v9 never consumes; these do not enter legacy reduction. No observer writes to caches or calls providers.

The known lossy-summary prerequisite is addressed for the eight existing domains. This permits resuming v10 Phase 1/2 implementation, not activation: final typed-fact projection, the newly proposed fact-family extractors and all original admission/migration gates remain separate work. No v10 implementation or preference migration is included here.
