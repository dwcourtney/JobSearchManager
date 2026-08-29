$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$guard = Join-Path $PSScriptRoot "verify-github-account.ps1"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("jsm-gh-guard-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
    $positive = Join-Path $temporaryRoot "gh-positive.cmd"
    $negative = Join-Path $temporaryRoot "gh-negative.cmd"
    @'
@echo off
if "%1"=="auth" (
  1>&2 echo Logged in to github.com account dwc5703 ^(keyring^)
  exit /b 0
)
if "%1"=="api" (
  echo dwc5703
  exit /b 0
)
exit /b 2
'@ | Set-Content -LiteralPath $positive -Encoding Ascii
    @'
@echo off
if "%1"=="auth" (
  1>&2 echo Logged in to github.com account dwcourtney ^(keyring^)
  exit /b 0
)
if "%1"=="api" (
  echo dwcourtney
  exit /b 0
)
exit /b 2
'@ | Set-Content -LiteralPath $negative -Encoding Ascii

    & $guard -GitHubCliPath $positive | Out-Null

    $rejected = $false
    try {
        & $guard -GitHubCliPath $negative | Out-Null
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "GitHub account guard accepted dwcourtney."
    }

    Write-Output "GitHub account guard positive and negative tests passed."
}
finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
