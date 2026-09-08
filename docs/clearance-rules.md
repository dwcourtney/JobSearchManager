# Clearance parsing rules

The normal deterministic parser loads `rules/clearance-v1.json` from the application base directory. It is production content, independent of research tooling and the SQLite concept-rule store. Edit the JSON and restart the application to load changes; recompilation is unnecessary. This migration changes no clearance meanings or browser comparison behavior.

## Identity and validation

Schema version: **1**. Initial ruleset version: **1.0.0**. `clearance-v1.schema.json` describes the document structure. `ClearanceRules` enforces that structure, required fields, scope/type compatibility, unique IDs and priorities, valid references and known application result mappings, then compiles every regex before host startup. Missing/invalid files fail with an explicit error; there is no fallback rule table. Startup logs and internal diagnostics expose the version and SHA-256 of the exact loaded JSON bytes.

Patterns use the original `IgnoreCase | CultureInvariant` options. Matching has a 250 ms timeout per regex operation (configurable within 1–1000 ms). A timeout raises an explicit exception with rules identity, never a silent no-clearance result. The previous generated expressions had no explicit finite timeout. There is no new input truncation.

The project publishes both JSON files to `rules/`; `.gitattributes` fixes their line endings to LF so Windows and Linux load identical fingerprint bytes. No absolute checkout path or working-directory dependency is used.

## Execution and precedence

JSON owns all 18 clearance expressions and four section phrases. Pattern IDs identify vocabulary; type and scope constrain where it can run. Level and requirement rules execute by ascending priority. Overrides execute in listed order. Literal preferred-section markers execute in fallback order; reset markers use the latest occurrence within the configured prefix window.

C# retains HTML normalization, negative-clause removal, sentence/context selection, section execution, fallback behavior, typed result construction and evidence snippets. Result identifiers are the existing cache/API vocabulary. Neither persisted schemas nor browser clearance comparison are changed.

Preserved edge cases include standalone SCI being unrecognized unless it matches the existing wording, polygraph mention/preference alone not creating a requirement, the existing ordering of multiple levels and active/obtain language, and full-text first-match evidence even if preferred-section exclusion chose a later match. These are parity constraints, not endorsements or semantic fixes.

## Regression and parity checks

`Tests/LegacyClearanceBaseline.cs` is a test-only frozen copy from commit `41eda623187db07bfd5550823325e1ecf72aa3fd`. `Tests/clearance-fixtures.json` freezes 65 baseline outputs before production refactoring. The full .NET suite checks exact record equality, configuration failures, content identity, vocabulary changes without rebuilding, and a pathological regex timeout.

For additional copied posting descriptions (keep runtime exports outside Git):

```text
dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release -- --clearance-parity input.json report.json
```

Input is an array of `{ "id": "stable-fixture-id", "html": "posting text or HTML" }`. The report counts exact matches and changes to level/requirement, evidence, parse/ambiguity status and polygraph status; any difference fails the command. The old parser is excluded from normal compilation and publication by the existing Tests exclusions.
