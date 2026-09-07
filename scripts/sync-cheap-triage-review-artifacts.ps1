param(
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$Bundle,
    [string]$SshHost = 'curiosity-codex',
    [string]$HostName
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'docs/evaluations/human-review-snapshots'
$target = Join-Path $root $Bundle
$remote = '/home/codex/jsm-lab/data/app/rule-update-artifacts/' + $Bundle
$sshOptions = @()
if ($HostName) { $sshOptions += @('-o', "HostName=$HostName") }
New-Item -ItemType Directory -Force -Path $root | Out-Null
$staging = Join-Path $root ('.sync-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
function Assert-Hash([string]$Path, [string]$Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Expected) {
        throw "Integrity failure: $Path"
    }
}
try {
    & scp @sshOptions "${SshHost}:$remote/manifest.json" (Join-Path $staging 'manifest.json')
    if ($LASTEXITCODE -ne 0) { throw 'Manifest download failed; do not substitute a newer snapshot.' }
    Assert-Hash (Join-Path $staging 'manifest.json') $Bundle
    $manifest = Get-Content -LiteralPath (Join-Path $staging 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) { throw 'Unsupported manifest schema.' }
    foreach ($name in @('human-review.json', 'live-shadow.json')) {
        $expected = $manifest.artifacts.$name
        if ($expected -cnotmatch '^[a-f0-9]{64}$') { throw "Missing/invalid hash: $name" }
        & scp @sshOptions "${SshHost}:$remote/$name" (Join-Path $staging $name)
        if ($LASTEXITCODE -ne 0) { throw "Snapshot download failed: $name" }
        Assert-Hash (Join-Path $staging $name) $expected
    }
    if (Test-Path -LiteralPath $target) {
        Assert-Hash (Join-Path $target 'manifest.json') $Bundle
        foreach ($name in @('human-review.json', 'live-shadow.json')) {
            Assert-Hash (Join-Path $target $name) $manifest.artifacts.$name
        }
    } else {
        Move-Item -LiteralPath $staging -Destination $target
    }
    Write-Output "Verified immutable snapshot: $target"
} finally {
    # Only the newly created staging directory may be removed; never delete an existing snapshot.
    $resolvedRoot = [IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar
    if (![IO.Path]::GetFullPath($staging).StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
