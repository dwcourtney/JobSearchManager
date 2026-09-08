# Extended-location rules

`rules/extended-location-v1.json` externalizes 35 domain patterns, destination priority, signal mappings, duration vocabulary and summary text. Version 1.0.0 preserves analysis version 4. Three generic HTML/sentence/whitespace expressions remain compiled with the existing one-second bound.

The loader validates required fields, references, mappings, unique IDs/priorities, capture groups and finite regex timeouts before host startup. Missing or malformed rules fail before listening; there is no compiled fallback. Startup diagnostics include the exact ruleset version and SHA-256 of file bytes. JSON is copied to build/publish output through the existing rules content configuration.

C# retains numeric parsing/multiplication, context windows, evidence construction, destination fallback order, stable aggregation and typed results. JSON is configuration, not a programming language. SQLite concept rules and Job Fit are unchanged.

`Tests/LegacyExtendedLocationBaseline.cs` freezes the old implementation. Frozen fixtures and corpus parity compare every serialized output field and downstream deterministic concept output. The shared actual Jobs-caller downstream test compares all preference/travel combinations. Run `dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release`; corpus comparison uses `--extended-location-parity input.json output.json`.
