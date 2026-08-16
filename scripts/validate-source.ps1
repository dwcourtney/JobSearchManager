param(
    [string]$NodePath = "node"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$themePath = Join-Path $repo "wwwroot\theme.css"
$stylesPath = Join-Path $repo "wwwroot\styles.css"
$indexPath = Join-Path $repo "wwwroot\index.html"
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

$styles = Get-Content -LiteralPath $stylesPath -Raw
$theme = Get-Content -LiteralPath $themePath -Raw
$index = Get-Content -LiteralPath $indexPath -Raw
$app = Get-Content -LiteralPath $appPath -Raw
$countryOrdering = Get-Content -LiteralPath $countryOrderingPath -Raw

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
