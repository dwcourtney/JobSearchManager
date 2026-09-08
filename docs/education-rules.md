# Education parsing rules

`rules/education-v1.json` is the production education vocabulary; `rules/education-v1.schema.json` documents schema version 1. Initial ruleset version: 1.0.0. The loader fingerprints the exact file bytes with SHA-256 and validates them before the web host starts. `AcademicQualificationDetector.RulesetVersion` and `.RulesetFingerprint` expose that identity; startup logs include both.

The rules contain degree phrases, contextual abbreviations and level mappings, field wording and exclusions, experience substitution, OR/AND connectors, requirement cues and section precedence, and accreditation wording. Named capture groups (`degree`, `min`, `max`, `fields`) are execution contracts. Patterns have stable IDs, regex type, scope and explicit options. Ordered degree, qualifier, section and accreditation groups have stable IDs and unique nonnegative priorities. Section rules may override their result using one conditional pattern. This is a bounded parser configuration, not a scripting language.

C# retains segmentation/HTML decoding, bounded context windows, overlap removal, requirement and degree result ordering, field normalization mechanics, alternative grouping, typed result construction, deduplication and evidence ordering/limits. Typed degree and requirement values remain in C#. Browser education comparison and At a Glance rendering are unchanged; they consume parsed results rather than parse posting text.

The runtime rejects missing files, malformed JSON, unknown members/versions/enums/mappings, duplicate IDs, invalid priorities, incorrect scopes, missing references/capture groups, unreferenced patterns, and malformed regexes. Invalid configuration prevents startup without a fallback. Each regex has a finite timeout (250 ms in v1; permitted range 1–1000 ms); a timeout raises an explicit exception with the version and fingerprint. It never produces a partial successful analysis.

Edit the packaged JSON and restart to apply vocabulary changes without recompilation. JSON/schema files are already included by the normal rules/**/*.json publish configuration and forced to LF by .gitattributes. They are production assets, separate from research. No database or Admin editor is introduced.

Analysis version remains 4 because results are unchanged. This migration deliberately preserves existing quirks, including unsupported/ambiguous wording and overlap/field behavior. No semantic fixes are included.

Tests/LegacyEducationBaseline.cs freezes the pre-migration detector from 6ad1c00e6b068825b12cd3da5676f500ed1f90f3. Tests/education-fixtures.json contains 87 results frozen before changing the production detector. Full-object comparisons include every path, field, accreditation and evidence position. The baseline and fixtures are test-only and excluded from app publish.

Run the full suite with `dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release`. For an external corpus of `{id, html}` records, use `dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release -- --education-parity input.json report.json`. The report records input/rules hashes and individual changed-field counts and fails on any difference. Current cache exports and smoke artifacts belong outside Git.
