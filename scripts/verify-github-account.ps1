param(
    [string]$GitHubCliPath = "gh"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$expectedLogin = "dwc5703"
$gh = Get-Command $GitHubCliPath -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI is required for GitHub administration."
}

$status = & $gh.Source auth status --active --hostname github.com 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI has no usable active github.com account."
}
$statusText = $status -join "`n"
if ($statusText -notmatch "(?m)account\s+$([regex]::Escape($expectedLogin))\b") {
    throw "The active GitHub CLI account is not $expectedLogin; STOP before any GitHub administration or deployment dispatch."
}

$effectiveLogin = (& $gh.Source api user --jq .login).Trim()
if ($LASTEXITCODE -ne 0 -or $effectiveLogin -cne $expectedLogin) {
    throw "The effective GitHub API account is not exactly $expectedLogin; STOP before any GitHub administration or deployment dispatch."
}

Write-Output "Verified active and effective GitHub account: $expectedLogin."
