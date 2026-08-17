param(
    [string]$NodePath = "node"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$themePath = Join-Path $repo "wwwroot\theme.css"
$stylesPath = Join-Path $repo "wwwroot\styles.css"
$indexPath = Join-Path $repo "wwwroot\index.html"
$manifestPath = Join-Path $repo "wwwroot\site.webmanifest"
$sourceIconPath = Join-Path $repo "assets\JobSearchManager.ico"
$faviconPath = Join-Path $repo "wwwroot\favicon.ico"
$appPath = Join-Path $repo "wwwroot\app.js"
$countryOrderingPath = Join-Path $repo "wwwroot\country-ordering.js"
$countryOrderingTestsPath = Join-Path $repo "Tests\country-ordering.tests.js"
$postingTextPath = Join-Path $repo "wwwroot\job-posting-text.js"
$postingTextTestsPath = Join-Path $repo "Tests\job-posting-text.tests.js"
$workflowStatePath = Join-Path $repo "wwwroot\job-workflow-state.js"
$workflowStateTestsPath = Join-Path $repo "Tests\job-workflow-state.tests.js"
$sourceStatePath = Join-Path $repo "wwwroot\job-source-state.js"
$sourceStateTestsPath = Join-Path $repo "Tests\job-source-state.tests.js"

foreach ($scriptPath in @(
    $appPath,
    $countryOrderingPath,
    $countryOrderingTestsPath,
    $postingTextPath,
    $postingTextTestsPath,
    $workflowStatePath,
    $workflowStateTestsPath,
    $sourceStatePath,
    $sourceStateTestsPath)) {
    & $NodePath --check $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "JavaScript syntax validation failed for $scriptPath."
    }
}

& $NodePath $countryOrderingTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Country-ordering runtime tests failed."
}

& $NodePath $postingTextTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-posting text normalization tests failed."
}

& $NodePath $workflowStateTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-workflow state tests failed."
}

& $NodePath $sourceStateTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-source state tests failed."
}

$styles = Get-Content -LiteralPath $stylesPath -Raw -Encoding UTF8
$theme = Get-Content -LiteralPath $themePath -Raw -Encoding UTF8
$index = Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8
$app = Get-Content -LiteralPath $appPath -Raw -Encoding UTF8
$countryOrdering = Get-Content -LiteralPath $countryOrderingPath -Raw -Encoding UTF8

$mojibakeMarkers = @(
    [char]0x00e2,
    [char]0x00c3,
    [char]0x00c2,
    [char]0xfffd
)
foreach ($webSource in @($index, $app, $styles)) {
    if ($mojibakeMarkers | Where-Object { $webSource.Contains([string]$_) }) {
        throw "A browser source file contains mojibake or a Unicode replacement character."
    }
}

if ($app -match 'setInterval\s*\(' -or
    $app -match 'setTimeout\s*\(\s*loadSnapshot' -or
    $app -notmatch 'document\.visibilityState\s*===\s*"hidden"' -or
    $app -notmatch 'automaticStatusRequestInFlight' -or
    $app -notmatch 'refreshStatusRequestInFlight' -or
    $app -notmatch '/api/automatic-check/status' -or
    $app -notmatch 'beginRefreshProgressPolling\(true\)') {
    throw "Refresh polling must be visibility-aware, non-overlapping, and status-only until completion."
}

$requiredIconLinks = @(
    'rel="icon" href="/favicon.ico?v=1" sizes="any"',
    'rel="icon" type="image/png" href="/icons/favicon-32.png?v=1" sizes="32x32"',
    'rel="apple-touch-icon" href="/icons/apple-touch-icon.png?v=1" sizes="180x180"',
    'rel="manifest" href="/site.webmanifest?v=1"'
)
foreach ($link in $requiredIconLinks) {
    if (-not $index.Contains($link)) {
        throw "index.html is missing required browser-icon metadata: $link"
    }
}
if (-not (Test-Path -LiteralPath $manifestPath) -or
    -not (Test-Path -LiteralPath $faviconPath) -or
    (Get-FileHash -LiteralPath $sourceIconPath -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $faviconPath -Algorithm SHA256).Hash) {
    throw "The web favicon is missing or no longer matches the executable icon."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.name -ne "Job Search Manager" -or
    @($manifest.icons).Count -ne 2 -or
    @($manifest.icons.src) -notcontains "/icons/icon-192.png?v=1" -or
    @($manifest.icons.src) -notcontains "/icons/icon-512.png?v=1") {
    throw "site.webmanifest is missing the canonical application name or icon assets."
}
foreach ($icon in $manifest.icons) {
    $iconFile = ($icon.src -replace '^/', '') -replace '\?v=.*$', ''
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $repo "wwwroot") $iconFile))) {
        throw "site.webmanifest references a missing icon: $($icon.src)"
    }
}

$countryOrderingScript = $index.IndexOf('src="/country-ordering.js?v=2"')
$sourceStateScript = $index.IndexOf('src="/job-source-state.js"')
$appScript = $index.IndexOf('src="/app.js?v=2"')
if ($countryOrderingScript -lt 0 -or $appScript -le $countryOrderingScript) {
    throw "The versioned country-ordering.js asset must load before app.js."
}
if ($sourceStateScript -lt 0 -or $appScript -le $sourceStateScript) {
    throw "job-source-state.js must load before app.js."
}
if ($countryOrdering -notmatch 'globalThis\.CountryOrdering\s*=' -or
    $app -notmatch '\bCountryOrdering\.orderCountryFacets\b' -or
    $countryOrdering -match '\b(?:Workday|JobSource)CountryOrdering\b' -or
    $app -match '\b(?:Workday|JobSource)CountryOrdering\b') {
    throw "The country-ordering declaration and application consumer are inconsistent."
}

if ([regex]::IsMatch($index, '(?is)<h2[^>]*>\s*Settings\s*</h2>')) {
    throw "The redundant Settings page heading is still present."
}

$settingsTabIds = @(
    "job-search-settings",
    "qualifications-settings",
    "preferences-settings"
)
foreach ($tabId in $settingsTabIds) {
    if (-not [regex]::IsMatch(
        $index,
        "(?is)<button[^>]*id=`"$tabId-tab`"[^>]*role=`"tab`"[^>]*aria-controls=`"$tabId-panel`"")) {
        throw "Settings tab $tabId is missing its tab role or panel relationship."
    }
    if (-not [regex]::IsMatch(
        $index,
        "(?is)<div[^>]*id=`"$tabId-panel`"[^>]*role=`"tabpanel`"[^>]*aria-labelledby=`"$tabId-tab`"")) {
        throw "Settings panel $tabId is missing its tabpanel role or tab relationship."
    }
}

$settingsTabLabels = @{
    "job-search-settings" = "Job Source"
    "qualifications-settings" = "My Qualifications"
    "preferences-settings" = "My Preferences"
}
foreach ($tabId in $settingsTabLabels.Keys) {
    $label = [regex]::Escape($settingsTabLabels[$tabId])
    if (-not [regex]::IsMatch(
        $index,
        "(?is)<button[^>]*id=`"$tabId-tab`"[^>]*>\s*$label\s*</button>")) {
        throw "Settings tab $tabId does not use the required visible label."
    }
}

if ($index -match 'id="job-search-heading"' -or $index -match 'id="qualifications-heading"') {
    throw "A redundant top-level Settings panel heading is still present."
}

foreach ($requiredId in @(
    "source-confirmation-stay",
    "source-confirmation-discard",
    "source-confirmation-apply",
    "source-loading-overlay",
    "source-loading-title",
    "source-loading-phase")) {
    if ($index -notmatch "id=`"$requiredId`"") {
        throw "Source confirmation/loading UX is missing $requiredId."
    }
}
$discardHandler = [regex]::Match(
    $app,
    '(?s)async function discardPendingSourceAndGoToJobs\(\).*?\n}')
if (-not $discardHandler.Success -or
    $discardHandler.Value -match '/api/(?:query|refresh)' -or
    $app -notmatch 'sourceConfirmationDiscard\.hidden\s*=\s*!state\.hasConfiguredSource' -or
    $app -notmatch 'sourceRequestGeneration' -or
    $app -notmatch 'sourceAbortController' -or
    $app -notmatch 'isCurrentSourceRequest') {
    throw "Source discard or stale-response protection is incomplete."
}
if ($app.IndexOf('JobPostingText.normalizeHtml(job.descriptionHtml)') -lt 0 -or
    $app.IndexOf('DOMPurify.sanitize(normalizedHtml') -lt 0 -or
    $app.IndexOf('DOMPurify.sanitize(normalizedHtml') -lt
        $app.IndexOf('JobPostingText.normalizeHtml(job.descriptionHtml)') -or
    $app -notmatch 'FORBID_TAGS:\s*\["script"') {
    throw "Posting normalization must remain ahead of the strict HTML sanitizer."
}

$jobSearchStart = $index.IndexOf('id="job-search-settings-panel"')
$qualificationsStart = $index.IndexOf('id="qualifications-settings-panel"')
$preferencesStart = $index.IndexOf('id="preferences-settings-panel"')
$settingsEnd = $index.IndexOf('id="loading-overlay"')
if ($jobSearchStart -lt 0 -or $qualificationsStart -le $jobSearchStart -or
    $preferencesStart -le $qualificationsStart -or $settingsEnd -le $preferencesStart) {
    throw "Settings tab panels are missing or out of order."
}
$settingsPanelMarkup = @{
    "Job Search" = $index.Substring($jobSearchStart, $qualificationsStart - $jobSearchStart)
    "Qualifications" = $index.Substring($qualificationsStart, $preferencesStart - $qualificationsStart)
    "Preferences" = $index.Substring($preferencesStart, $settingsEnd - $preferencesStart)
}
$requiredSettingsControls = @{
    "Job Search" = @(
        "company-select", "country-select", "include-all-locations", "include-remote",
        "location-search", "selected-location-summary", "location-groups", "apply-location"
    )
    "Qualifications" = @(
        "minimum-pay", "education-level", "clearance-profile-level", "public-trust-profile",
        "us-work-authorization-status", "sponsorship-profile", "hide-strict-education-mismatch",
        "hide-strict-clearance-mismatch", "hide-strict-work-authorization-mismatch"
    )
    "Preferences" = @(
        "automatic-check-enabled", "automatic-check-interval", "automatic-check-status",
        "theme-mode", "import-workspace-button", "export-workspace-button",
        "import-workspace-file", "reset-workspace-button"
    )
}
foreach ($panelName in $requiredSettingsControls.Keys) {
    foreach ($controlId in $requiredSettingsControls[$panelName]) {
        if ($settingsPanelMarkup[$panelName] -notmatch "id=`"$controlId`"") {
            throw "$controlId is not assigned to the $panelName Settings tab."
        }
    }
}

if ($settingsPanelMarkup["Job Search"] -notmatch 'class="settings-section apply-source-section"' -or
    $settingsPanelMarkup["Job Search"] -notmatch 'class="apply-source-content"') {
    throw "Apply Job Source is not in its dedicated Settings section."
}
if ($settingsPanelMarkup["Job Search"] -match 'saved automatically') {
    throw "Job Source incorrectly claims that source changes are saved automatically."
}
if ($settingsPanelMarkup["Qualifications"] -notmatch 'Changes on this tab are saved automatically\.' -or
    $settingsPanelMarkup["Preferences"] -notmatch 'Changes on this tab are saved automatically\.') {
    throw "The auto-save note is missing from My Qualifications or My Preferences."
}
if ($settingsPanelMarkup["Qualifications"] -notmatch 'id="screening-heading"' -or
    $settingsPanelMarkup["Preferences"] -match 'id="screening-heading"') {
    throw "Screening Rules is not contained exclusively in My Qualifications."
}

if ($settingsPanelMarkup["Job Search"] -notmatch '>\s*Apply Job Source\s*<' -or
    $settingsPanelMarkup["Preferences"] -notmatch '>\s*Import Workspace\s*<' -or
    $settingsPanelMarkup["Preferences"] -notmatch '>\s*Export Workspace\s*<' -or
    $settingsPanelMarkup["Preferences"] -notmatch '>\s*Reset Current Workspace\s*<') {
    throw "A required action button is missing or does not use title case."
}
if ($app -notmatch '"Apply Job Source"' -or
    $app -cmatch '"Apply job source"' -or
    $app -notmatch '"Reset Current Workspace"') {
    throw "Dynamic Apply/Reset action labels do not use the required title case."
}

$duplicateIds = [regex]::Matches($index, '(?i)\sid="([^"]+)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Group-Object |
    Where-Object Count -gt 1
if ($duplicateIds) {
    throw "index.html contains duplicate IDs: $($duplicateIds.Name -join ', ')"
}

$rawPaint = [regex]::Matches(
    $styles,
    '(?im)(?<![\w-])(?:#[0-9a-f]{3,8}\b|(?:rgb|hsl)a?\s*\()')
if ($rawPaint.Count -gt 0) {
    throw "styles.css contains raw color values; define them in theme.css instead."
}

if ([regex]::IsMatch($index, '(?i)\sstyle\s*=')) {
    throw "index.html contains an inline style attribute."
}

$usedTokens = [regex]::Matches($styles, 'var\((--[a-z0-9-]+)') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique
$definedTokens = [regex]::Matches($theme, '(?m)^\s*(--[a-z0-9-]+)\s*:') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique
$missingTokens = @($usedTokens | Where-Object { $_ -notin $definedTokens })
if ($missingTokens.Count -gt 0) {
    throw "styles.css references undefined theme tokens: $($missingTokens -join ', ')"
}

Write-Output "JavaScript syntax: PASS"
Write-Output "Theme raw-color audit: PASS"
Write-Output "Inline-style audit: PASS"
Write-Output "Theme token-reference audit: PASS ($($usedTokens.Count) tokens used)"
Write-Output "Settings tab structure audit: PASS"
Write-Output "HTML ID uniqueness audit: PASS"
Write-Output "Browser icon/manifest audit: PASS"
Write-Output "Bandwidth-efficient polling audit: PASS"
Write-Output "Source modal/loading race audit: PASS"
Write-Output "Posting normalization/sanitization order audit: PASS"
Write-Output "Browser text encoding audit: PASS"
