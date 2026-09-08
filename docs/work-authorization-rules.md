# Work-authorization parsing rules

The production detector loads `rules/work-authorization-v1.json` from `AppContext.BaseDirectory`. The JSON contains all 21 existing domain regexes: 19 named detection/context/heading patterns, one sponsorship exclusion and one abbreviation normalization. The ruleset is independent of research assets and SQLite concept-rule authority. Edit the JSON and restart to load changes without recompiling; there is no mutable database or live Admin editing workflow.

## Identity, schema and failure behavior

Initial schema version: **1**. Ruleset version: **1.0.0**. `work-authorization-v1.schema.json` describes the structure. `WorkAuthorizationRules` validates required fields, unknown properties, enum mappings, unique IDs/priorities, nonnegative priorities, references, scopes and regex syntax before host startup. Missing or invalid rules fail explicitly; there is no embedded fallback. Version and SHA-256 of exact loaded bytes are exposed internally and logged at startup.

Each pattern declares its original options. Most use IgnoreCase and CultureInvariant; the original exclusion and abbreviation expressions use IgnoreCase alone. The existing 250 ms matching timeout is retained, now also explicit for whitespace normalization. Configuration bounds are 1–1000 ms. Timeout exceptions include rules identity and never produce a silent no-requirement result. No input truncation was added.

The existing `rules/**/*.json` publish configuration and `rules/** text eol=lf` attributes cover both new files on Windows and Linux. They are production content, not research assets. Analysis version **4** and the persisted/API result schema are unchanged.

## Execution semantics

C# retains HTML stripping, sentence segmentation, evidence snippets/deduplication/limit, typed output and ordered application. JSON owns vocabulary, abbreviation replacement, section mappings, eligibility and sponsorship mappings, pattern dependencies/exclusions, priority and applicability metadata.

Section rules execute first; recognized headings update context and consume the segment. Sponsorship rules run independently of eligibility. Eligibility rules execute by ascending priority and consume the first matching branch, even when its application guard declines to replace an earlier result. `firstSpecific` only replaces noneSpecified/exportControlled; `replace` overwrites; `fillUnset` keeps an existing eligibility while still applying strength/evidence. A null country mapping preserves the current country. These small application modes preserve the original parser's behavior across multiple segments.

Existing quirks are intentionally frozen: positive sponsorship offers and visa names alone are not recognized; standalone green-card/resident wording is not universally supported; negative citizenship statements are not newly interpreted; branch priority and country retention can produce surprising mixed-statement results. No semantic fixes or support expansion belong to this migration. Browser profile comparison and rendering remain untouched.

## Tests and parity

`Tests/LegacyWorkAuthorizationBaseline.cs` freezes the pre-migration detector. `Tests/work-authorization-fixtures.json` contains 77 outputs generated from that implementation before production changes. The full .NET suite compares all fields (including evidence contents/order), validates malformed configurations, checks content identity, demonstrates vocabulary changes without rebuilding, and tests a pathological regex timeout.

Additional copied corpus input should stay outside Git:

```text
dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release -- --work-authorization-parity input.json report.json
```

Input is an array of `{ "id": "fixture-id", "html": "posting HTML or text" }`. The command reports exact matches and changes in eligibility, country, sponsorship, both strengths, evidence, parse status and analysis version; any difference fails. Test-only baseline code/fixtures are excluded from normal compile/publish by existing project exclusions.
