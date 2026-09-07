#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/../.."
dotnet restore research/cheap-triage/Tests/Jsm.CheapTriage.Research.Tests.csproj --locked-mode
dotnet run --project research/cheap-triage/Tests/Jsm.CheapTriage.Research.Tests.csproj -c Release --no-restore
node Tests/admin-architecture.tests.js
node Tests/retired-triage.tests.js
echo 'Offline research suites and preserved 241-file inventory verified; no runtime service or model started.'
