# Job Fit calibration report

The calibration harness evaluates persisted job-cache data without calling provider APIs or
writing workspace state. Keep copied cache and settings documents outside the repository and
remove them when the review is complete.

First, run the production C# detectors against a cache document:

```powershell
dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release --no-restore -- `
  --job-fit-calibration-detect <cache.json> <detections.json>
```

Then invoke the authoritative browser scoring module against those detections and a read-only
settings snapshot:

```powershell
node scripts/job-fit-calibration-report.mjs `
  <cache.json> <settings.json> <report.json> <detections.json>
```

The JSON report includes requisition/title, score distribution, detected and Neutral concepts,
configured preferences, bounded category contributions, evidence, Hard Conflict state, sparse
long postings, and conservative audit misses. It does not include account data, credentials,
tokens, qualifications, workflow history, or full posting bodies.
