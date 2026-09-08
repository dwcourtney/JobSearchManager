# Salary recognition rules

`rules/salary-v1.json` version 1.0.0 owns ten exact recognition expressions and two literal unparseable-range phrases. Expression flags preserve the original generated-regex options. C# retains decimal/scale handling, explicit parsing precedence, summary range aggregation, mixed-period rejection and typed result construction. No arithmetic language or new salary behavior is introduced.

Current `SalaryAnalysis` has four fields: minimum, maximum, period and parse status. Hourly ranges remain hourly and unconverted; there is no new annualization, currency conversion, evidence field, provider metadata override, or min/max reordering. Existing provider callers continue using the same four outputs. Generic HTML normalization and geographic restrictions in `JobAnalysis` are unchanged.

The loader validates schema/version, required references, capture groups, regex compilation and timeout bounds before host startup. Startup logs include version and the SHA-256 of exact JSON bytes. Missing or malformed files fail before listening with no compiled fallback. Migrated recognition now has a finite 1,000ms maximum per-match timeout and contextual timeout diagnostics; the previous generated expressions had no explicit timeout. Generic expressions remain unchanged.

The test-only old implementation and 78 frozen cases cover all current result fields, numeric edge cases and precedence. Corpus comparison uses `--salary-parity input.json output.json`; full tests also check exact expression/options, three cultures, malformed configurations and bounded execution. The existing Jobs UI functions are used for pay formatting, salary headroom and Job Fit regression comparison.
