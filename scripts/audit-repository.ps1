$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$safeRepo = $repo.Replace('\', '/')
$gitArguments = @('-c', "safe.directory=$safeRepo")
Push-Location $repo
try {
    $prohibitedPath = [regex]::new(
        '(^|/)(data|backups|mailpit-data|authentication|dataprotection|sessions|actions-runner|_work)(/|$)|' +
        '(^|/)\.env($|\.)|(^|/)(accounts\.json|deploy\.env|deployed-sha|\.runner|\.credentials|\.credentials_rsaparams)$|' +
        '\.(bundle|zip|pem|key|pfx|p12)$',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $tracked = @(& git @gitArguments ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate tracked repository paths."
    }

    $blocked = @($tracked | Where-Object { $prohibitedPath.IsMatch($_) })
    if ($blocked.Count -gt 0) {
        throw "Prohibited runtime, credential, archive, or runner paths are tracked: $($blocked -join ', ')"
    }

    $highConfidenceSecrets = @(
        '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
        'github_pat_[A-Za-z0-9_]{20,}',
        'gh[pousr]_[A-Za-z0-9_]{30,}',
        'cfat_[A-Za-z0-9_-]{20,}',
        'AccountKey=[A-Za-z0-9+/]{20,}={0,2}',
        'AKIA[0-9A-Z]{16}',
        'AIza[0-9A-Za-z_-]{35}'
    )

    $revisions = @(& git @gitArguments rev-list --all)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate Git history."
    }

    foreach ($pattern in $highConfidenceSecrets) {
        & git @gitArguments grep --cached -I -E -q -- $pattern -- .
        if ($LASTEXITCODE -eq 0) {
            throw "A high-confidence credential signature exists in the current Git index."
        }
        if ($LASTEXITCODE -gt 1) {
            throw "Credential scan failed for the current Git index."
        }

        foreach ($revision in $revisions) {
            & git @gitArguments grep -I -E -q -- $pattern $revision -- .
            if ($LASTEXITCODE -eq 0) {
                throw "A high-confidence credential signature exists in Git revision $revision."
            }
            if ($LASTEXITCODE -gt 1) {
                throw "Credential scan failed for Git revision $revision."
            }
        }
    }

    Write-Host "Repository path and high-confidence history credential audit: PASS"
}
finally {
    Pop-Location
}

exit 0
