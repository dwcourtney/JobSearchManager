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
$companySelectorPath = Join-Path $repo "wwwroot\company-selector.js"
$companySelectorTestsPath = Join-Path $repo "Tests\company-selector.tests.js"
$postingTextPath = Join-Path $repo "wwwroot\job-posting-text.js"
$postingTextTestsPath = Join-Path $repo "Tests\job-posting-text.tests.js"
$clipboardTextPath = Join-Path $repo "wwwroot\clipboard-text.js"
$clipboardTextTestsPath = Join-Path $repo "Tests\clipboard-text.tests.js"
$workflowStatePath = Join-Path $repo "wwwroot\job-workflow-state.js"
$workflowStateTestsPath = Join-Path $repo "Tests\job-workflow-state.tests.js"
$unseenStatePath = Join-Path $repo "wwwroot\job-unseen-state.js"
$unseenStateTestsPath = Join-Path $repo "Tests\job-unseen-state.tests.js"
$jobCardBadgesTestsPath = Join-Path $repo "Tests\job-card-badges.tests.js"
$closedStateTestsPath = Join-Path $repo "Tests\job-closed-state.tests.js"
$sourceStatePath = Join-Path $repo "wwwroot\job-source-state.js"
$sourceStateTestsPath = Join-Path $repo "Tests\job-source-state.tests.js"
$credentialFitPath = Join-Path $repo "wwwroot\credential-fit.js"
$credentialFitTestsPath = Join-Path $repo "Tests\credential-fit.tests.js"
$clearanceFitPath = Join-Path $repo "wwwroot\clearance-fit.js"
$clearanceFitTestsPath = Join-Path $repo "Tests\clearance-fit.tests.js"
$qualificationSettingsTestsPath = Join-Path $repo "Tests\qualification-settings.tests.js"
$themeSettingsTestsPath = Join-Path $repo "Tests\theme-settings.tests.js"
$cacheStatusTestsPath = Join-Path $repo "Tests\cache-status.tests.js"
$jobFitPath = Join-Path $repo "wwwroot\job-fit.js"
$jobFitTestsPath = Join-Path $repo "Tests\job-fit.tests.js"
$jobFitCalibrationReportTestsPath = Join-Path $repo "Tests\job-fit-calibration-report.tests.js"
$jobFitUiTestsPath = Join-Path $repo "Tests\job-fit-ui.tests.js"
$jobFitDetailUiTestsPath = Join-Path $repo "Tests\job-fit-detail-ui.tests.js"
$accountUiTestsPath = Join-Path $repo "Tests\account-ui.tests.js"
$adminUiTestsPath = Join-Path $repo "Tests\admin-ui.tests.js"

foreach ($scriptPath in @(
    $appPath,
    $countryOrderingPath,
    $countryOrderingTestsPath,
    $companySelectorPath,
    $companySelectorTestsPath,
    $postingTextPath,
    $postingTextTestsPath,
    $clipboardTextPath,
    $clipboardTextTestsPath,
    $workflowStatePath,
    $workflowStateTestsPath,
    $unseenStatePath,
    $unseenStateTestsPath,
    $jobCardBadgesTestsPath,
    $closedStateTestsPath,
    $sourceStatePath,
    $sourceStateTestsPath,
    $credentialFitPath,
    $credentialFitTestsPath,
    $clearanceFitPath,
    $clearanceFitTestsPath,
    $qualificationSettingsTestsPath,
    $themeSettingsTestsPath,
    $cacheStatusTestsPath,
    $jobFitPath,
    $jobFitTestsPath,
    $jobFitCalibrationReportTestsPath,
    $jobFitUiTestsPath,
    $jobFitDetailUiTestsPath,
    $accountUiTestsPath,
    $adminUiTestsPath)) {
    & $NodePath --check $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "JavaScript syntax validation failed for $scriptPath."
    }
}

& $NodePath $countryOrderingTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Country-ordering runtime tests failed."
}

& $NodePath $companySelectorTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Company-selector category tests failed."
}

& $NodePath $postingTextTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-posting text normalization tests failed."
}

& $NodePath $clipboardTextTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Clipboard transport and Copy Posting UI tests failed."
}

& $NodePath $workflowStateTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-workflow state tests failed."
}

& $NodePath $unseenStateTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Unseen-job state tests failed."
}

& $NodePath $jobCardBadgesTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-card badge-path tests failed."
}

& $NodePath $closedStateTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Closed-state UI integration tests failed."
}

& $NodePath $sourceStateTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job-source state tests failed."
}

& $NodePath $credentialFitTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Credential-fit tests failed."
}

& $NodePath $clearanceFitTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Clearance-fit tests failed."
}

& $NodePath $qualificationSettingsTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Qualification-settings UI tests failed."
}

& $NodePath $themeSettingsTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Theme settings tests failed."
}

& $NodePath $cacheStatusTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Compact cache-status tests failed."
}

& $NodePath $jobFitTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job Fit scoring tests failed."
}

& $NodePath $jobFitCalibrationReportTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job Fit calibration report tests failed."
}

& $NodePath $jobFitUiTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job Fit UI integration tests failed."
}

& $NodePath $jobFitDetailUiTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Job Fit detail-tab UI integration tests failed."
}

& $NodePath $accountUiTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Optional Account UI integration tests failed."
}

& $NodePath $adminUiTestsPath
if ($LASTEXITCODE -ne 0) {
    throw "Admin role/bootstrap UI integration tests failed."
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
    $app -notmatch 'refreshStatusRequestInFlight' -or
    $app -notmatch 'beginRefreshProgressPolling\(true\)' -or
    $app -match '/api/automatic-check/' -or
    $index -match 'automatic-check-(?:enabled|interval|status)') {
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
$companySelectorScript = $index.IndexOf('src="/company-selector.js?v=1"')
$sourceStateScript = $index.IndexOf('src="/job-source-state.js"')
$unseenStateScript = $index.IndexOf('src="/job-unseen-state.js?v=1"')
$credentialFitScript = $index.IndexOf('src="/credential-fit.js?v=2"')
$clearanceFitScript = $index.IndexOf('src="/clearance-fit.js?v=1"')
$jobFitScript = $index.IndexOf('src="/job-fit.js?v=4"')
$clipboardTextScript = $index.IndexOf('src="/clipboard-text.js?v=1"')
$appScript = $index.IndexOf('src="/app.js?v=27"')
if ($countryOrderingScript -lt 0 -or $appScript -le $countryOrderingScript) {
    throw "The versioned country-ordering.js asset must load before app.js."
}
if ($companySelectorScript -lt 0 -or $appScript -le $companySelectorScript -or
    $app -notmatch '\bCompanySelector\.groupCompanies\b') {
    throw "The metadata-driven company selector must load before app.js."
}
if ($sourceStateScript -lt 0 -or $appScript -le $sourceStateScript) {
    throw "job-source-state.js must load before app.js."
}
if ($unseenStateScript -lt 0 -or $appScript -le $unseenStateScript) {
    throw "job-unseen-state.js must load before app.js."
}
if ($credentialFitScript -lt 0 -or $appScript -le $credentialFitScript) {
    throw "credential-fit.js must load before app.js."
}
if ($clearanceFitScript -lt 0 -or $appScript -le $clearanceFitScript) {
    throw "clearance-fit.js must load before app.js."
}
if ($jobFitScript -lt 0 -or $appScript -le $jobFitScript) {
    throw "job-fit.js must load before app.js."
}
if ($clipboardTextScript -lt 0 -or $appScript -le $clipboardTextScript -or
    $app -notmatch '\bClipboardText\.copyText\(postingText\)') {
    throw "The clipboard fallback module must load before app.js and handle Copy Posting transport."
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
    "preferences-settings",
    "job-fit-settings",
    "account-settings"
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
    "job-fit-settings" = "Job Fit"
    "account-settings" = "Account"
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
$jobFitStart = $index.IndexOf('id="job-fit-settings-panel"')
$accountStart = $index.IndexOf('id="account-settings-panel"')
$settingsEnd = $index.IndexOf('id="loading-overlay"')
if ($jobSearchStart -lt 0 -or $qualificationsStart -le $jobSearchStart -or
    $preferencesStart -le $qualificationsStart -or $jobFitStart -le $preferencesStart -or
    $accountStart -le $jobFitStart -or $settingsEnd -le $accountStart) {
    throw "Settings tab panels are missing or out of order."
}
$settingsPanelMarkup = @{
    "Job Search" = $index.Substring($jobSearchStart, $qualificationsStart - $jobSearchStart)
    "Qualifications" = $index.Substring($qualificationsStart, $preferencesStart - $qualificationsStart)
    "Preferences" = $index.Substring($preferencesStart, $jobFitStart - $preferencesStart)
    "Job Fit" = $index.Substring($jobFitStart, $accountStart - $jobFitStart)
    "Account" = $index.Substring($accountStart, $settingsEnd - $accountStart)
}
$requiredSettingsControls = @{
    "Job Search" = @(
        "company-select", "country-select", "include-all-locations", "include-remote",
        "location-search", "selected-location-summary", "location-groups", "apply-location"
    )
    "Qualifications" = @(
        "education-level", "clearance-profile-level", "public-trust-profile",
        "us-work-authorization-status", "sponsorship-profile", "hide-strict-education-mismatch",
        "hide-strict-clearance-mismatch", "hide-strict-work-authorization-mismatch",
        "credential-inventory-status", "credential-search", "held-credentials"
    )
    "Preferences" = @(
        "minimum-pay", "theme-mode"
    )
    "Job Fit" = @(
        "job-fit-enabled", "job-fit-configuration", "job-fit-concept-search",
        "job-fit-survey-status", "job-fit-survey"
    )
    "Account" = @(
        "import-workspace-button", "export-workspace-button", "import-workspace-file",
        "reset-workspace-button", "workspace-id", "copy-workspace-id-button"
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
    $settingsPanelMarkup["Preferences"] -notmatch 'Changes on this tab are saved automatically\.' -or
    $settingsPanelMarkup["Job Fit"] -notmatch 'Changes on this tab are saved automatically\.') {
    throw "The auto-save note is missing from a settings panel."
}
if ($settingsPanelMarkup["Qualifications"] -match 'id="screening-heading"' -or
    $settingsPanelMarkup["Qualifications"] -match '>\s*Screening Rules\s*<') {
    throw "The obsolete standalone Screening Rules section is still present."
}
if ($settingsPanelMarkup["Qualifications"] -notmatch 'id="qualification-basics-tab"' -or
    $settingsPanelMarkup["Qualifications"] -notmatch 'id="qualification-credentials-tab"' -or
    $settingsPanelMarkup["Qualifications"] -match 'id="minimum-pay"' -or
    $settingsPanelMarkup["Preferences"] -notmatch 'id="compensation-heading"') {
    throw "Qualification subtabs or Compensation placement is incomplete."
}
if ($settingsPanelMarkup["Preferences"] -notmatch
    'locations such as Antarctica, Guam, or Ramstein AFB in Germany') {
    throw "Deployment Filtering help is missing verified corpus examples."
}
if ($settingsPanelMarkup["Preferences"] -match 'id="import-export-heading"' -or
    $settingsPanelMarkup["Preferences"] -match 'id="reset-workspace-heading"') {
    throw "Workspace import/export or reset controls are still assigned to My Preferences."
}
if ($settingsPanelMarkup["Account"] -notmatch
    '(?s)id="account-privacy-heading".*id="import-export-heading".*id="reset-workspace-heading"' -or
    $settingsPanelMarkup["Account"] -notmatch 'without deleting the authenticated account') {
    throw "Account workspace controls are missing, out of order, or unclear about account preservation."
}

if ($settingsPanelMarkup["Job Search"] -notmatch '>\s*Apply Job Source\s*<' -or
    $settingsPanelMarkup["Account"] -notmatch '>\s*Import Workspace\s*<' -or
    $settingsPanelMarkup["Account"] -notmatch '>\s*Export Workspace\s*<' -or
    $settingsPanelMarkup["Account"] -notmatch '>\s*Reset Current Workspace\s*<') {
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
