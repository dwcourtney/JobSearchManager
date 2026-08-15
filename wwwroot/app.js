"use strict";

const HEADROOM_WARNING_THRESHOLD = 0.75;
const SETTINGS_SAVE_DEBOUNCE_MS = 400;
const OVERLAY_TRANSITION_MS = 180;
const AUTOMATIC_STATUS_POLL_MS = 2000;
const ALL_COUNTRIES_LABEL = "All countries";
const ALL_LOCATIONS_LABEL = "All locations";
const AGE_GROUPS = [
  { id: "today", label: "Posted Today", minimumDays: 0, maximumDays: 0 },
  { id: "yesterday", label: "Posted Yesterday", minimumDays: 1, maximumDays: 1 },
  { id: "days-2-7", label: "Posted 2–7 Days Ago", minimumDays: 2, maximumDays: 7 },
  { id: "days-8-14", label: "Posted 8–14 Days Ago", minimumDays: 8, maximumDays: 14 },
  { id: "days-15-30", label: "Posted 15–30 Days Ago", minimumDays: 15, maximumDays: 30 },
  { id: "days-31-plus", label: "Posted 31+ Days Ago", minimumDays: 31, maximumDays: Infinity },
  { id: "unknown", label: "Posting Date Unavailable", minimumDays: null, maximumDays: null }
];

const state = {
  activeView: "jobs",
  jobs: [],
  inclusions: [],
  exclusions: [],
  scope: "metadata",
  minimumSalary: null,
  locationMode: "all",
  highlightInclusions: true,
  collapsedAgeGroups: {},
  searchFiltersCollapsed: false,
  automaticCheckEnabled: true,
  automaticCheckIntervalMinutes: 60,
  themeMode: "light",
  educationLevel: "notSpecified",
  doctorateType: null,
  hideStrictEducationMismatch: false,
  clearanceProfileLevel: "notSpecified",
  publicTrustProfile: "unknown",
  hideStrictClearanceMismatch: false,
  detailTab: "glance",
  renderedDetailJobId: null,
  lastObservedAutomaticRefreshUtc: null,
  automaticCheckRequestInFlight: false,
  newJobIds: new Set(),
  dismissedJobIds: new Set(),
  showDismissedJobs: false,
  selectedJobId: null,
  lastRefreshedUtc: null,
  isCached: false,
  isRefreshing: false,
  isInitializing: true,
  catalogIsRefreshing: false,
  facetsLoaded: false,
  companies: [],
  companyId: "",
  companyName: "Workday",
  country: { id: null, label: ALL_COUNTRIES_LABEL },
  includeAllLocations: false,
  includeRemote: true,
  physicalLocations: [],
  locationGroups: [],
  remoteLocations: [],
  facetMatchingJobs: 0,
  pollTimer: null,
  refreshProgressTimer: null,
  overlayHideTimer: null,
  focusBeforeLoading: null,
  sourceConfirmationOpen: false,
  sourceConfirmationHideTimer: null,
  focusBeforeSourceConfirmation: null,
  loadingTitle: "Loading Workday jobs",
  settingsSaveTimer: null
};

const elements = {
  jobsTab: document.querySelector("#jobs-tab"),
  settingsTab: document.querySelector("#settings-tab"),
  jobsView: document.querySelector("#jobs-view"),
  settingsView: document.querySelector("#settings-view"),
  sourceSettingsLink: document.querySelector("#source-settings-link"),
  sourceSummary: document.querySelector("#source-summary"),
  refreshButton: document.querySelector("#refresh-button"),
  lastRefreshed: document.querySelector("#last-refreshed"),
  filterToggle: document.querySelector("#filter-toggle"),
  filterContent: document.querySelector("#filter-content"),
  filterChevron: document.querySelector("#filter-chevron"),
  filterSummary: document.querySelector("#filter-summary"),
  filterToggleAction: document.querySelector("#filter-toggle-action"),
  includeInput: document.querySelector("#include-input"),
  addInclusion: document.querySelector("#add-inclusion"),
  inclusionChips: document.querySelector("#inclusion-chips"),
  excludeInput: document.querySelector("#exclude-input"),
  addExclusion: document.querySelector("#add-exclusion"),
  exclusionChips: document.querySelector("#exclusion-chips"),
  minimumPay: document.querySelector("#minimum-pay"),
  companySelect: document.querySelector("#company-select"),
  countrySelect: document.querySelector("#country-select"),
  includeAllLocations: document.querySelector("#include-all-locations"),
  includeRemoteOption: document.querySelector("#include-remote-option"),
  includeRemote: document.querySelector("#include-remote"),
  locationSearch: document.querySelector("#location-search"),
  selectedLocationSummary: document.querySelector("#selected-location-summary"),
  locationGroups: document.querySelector("#location-groups"),
  applyLocation: document.querySelector("#apply-location"),
  facetStatus: document.querySelector("#facet-status"),
  highlightInclusions: document.querySelector("#highlight-inclusions"),
  automaticCheckEnabled: document.querySelector("#automatic-check-enabled"),
  automaticCheckInterval: document.querySelector("#automatic-check-interval"),
  automaticCheckStatus: document.querySelector("#automatic-check-status"),
  themeMode: document.querySelector("#theme-mode"),
  educationLevel: document.querySelector("#education-level"),
  doctorateTypeField: document.querySelector("#doctorate-type-field"),
  doctorateType: document.querySelector("#doctorate-type"),
  hideStrictEducationMismatch: document.querySelector("#hide-strict-education-mismatch"),
  clearanceProfileLevel: document.querySelector("#clearance-profile-level"),
  publicTrustProfile: document.querySelector("#public-trust-profile"),
  hideStrictClearanceMismatch: document.querySelector("#hide-strict-clearance-mismatch"),
  resultCount: document.querySelector("#result-count"),
  appShell: document.querySelector("#app-shell"),
  errorBanner: document.querySelector("#error-banner"),
  cacheBanner: document.querySelector("#cache-banner"),
  loadingOverlay: document.querySelector("#loading-overlay"),
  loadingTitle: document.querySelector("#loading-title"),
  loadingPhase: document.querySelector("#loading-phase"),
  loadingNote: document.querySelector("#loading-note"),
  sourceConfirmationOverlay: document.querySelector("#source-confirmation-overlay"),
  sourceConfirmationCurrent: document.querySelector("#source-confirmation-current"),
  sourceConfirmationPending: document.querySelector("#source-confirmation-pending"),
  sourceConfirmationStay: document.querySelector("#source-confirmation-stay"),
  sourceConfirmationApply: document.querySelector("#source-confirmation-apply"),
  showHiddenJobs: document.querySelector("#show-hidden-jobs"),
  hiddenJobCount: document.querySelector("#hidden-job-count"),
  jobList: document.querySelector("#job-list"),
  emptyDetail: document.querySelector("#empty-detail"),
  jobDetail: document.querySelector("#job-detail"),
  detailTitle: document.querySelector("#detail-title"),
  detailNewBadge: document.querySelector("#detail-new-badge"),
  detailHiddenBadge: document.querySelector("#detail-hidden-badge"),
  detailRequisition: document.querySelector("#detail-requisition"),
  detailDate: document.querySelector("#detail-date"),
  detailPay: document.querySelector("#detail-pay"),
  detailTimeType: document.querySelector("#detail-time-type"),
  detailLocation: document.querySelector("#detail-location"),
  detailAdditional: document.querySelector("#detail-additional"),
  detailFitSummary: document.querySelector("#detail-fit-summary"),
  detailFitTitle: document.querySelector("#detail-fit-title"),
  detailFitText: document.querySelector("#detail-fit-text"),
  atAGlanceTab: document.querySelector("#at-a-glance-tab"),
  fullPostingTab: document.querySelector("#full-posting-tab"),
  atAGlancePanel: document.querySelector("#at-a-glance-panel"),
  fullPostingPanel: document.querySelector("#full-posting-panel"),
  detailFlags: document.querySelector("#detail-flags"),
  detailEducationMismatch: document.querySelector("#detail-education-mismatch"),
  detailEducationMismatchText: document.querySelector("#detail-education-mismatch-text"),
  detailClearanceMismatch: document.querySelector("#detail-clearance-mismatch"),
  detailClearanceMismatchText: document.querySelector("#detail-clearance-mismatch-text"),
  detailClearanceRow: document.querySelector("#detail-clearance-row"),
  detailClearance: document.querySelector("#detail-clearance"),
  detailClearanceStatusRow: document.querySelector("#detail-clearance-status-row"),
  detailClearanceStatus: document.querySelector("#detail-clearance-status"),
  detailUserClearance: document.querySelector("#detail-user-clearance"),
  detailClearanceComparison: document.querySelector("#detail-clearance-comparison"),
  detailPolygraphRow: document.querySelector("#detail-polygraph-row"),
  detailPolygraph: document.querySelector("#detail-polygraph"),
  detailLocationNote: document.querySelector("#detail-location-note"),
  detailLocationNoteText: document.querySelector("#detail-location-note-text"),
  detailHeadroomNote: document.querySelector("#detail-headroom-note"),
  detailHeadroomNoteText: document.querySelector("#detail-headroom-note-text"),
  detailClearanceNote: document.querySelector("#detail-clearance-note"),
  detailClearanceEvidence: document.querySelector("#detail-clearance-evidence"),
  detailClearanceNoteText: document.querySelector("#detail-clearance-note-text"),
  detailAcademic: document.querySelector("#detail-academic"),
  detailAcademicContent: document.querySelector("#detail-academic-content"),
  detailCredentials: document.querySelector("#detail-credentials"),
  detailCredentialsList: document.querySelector("#detail-credentials-list"),
  detailWarning: document.querySelector("#detail-warning"),
  detailDescription: document.querySelector("#detail-description"),
  workdayLink: document.querySelector("#workday-link")
};

document.addEventListener("DOMContentLoaded", initialize);

async function initialize() {
  setLoading(true, {
    title: "Loading Workday jobs",
    phaseText: "Loading saved settings and job data…"
  });
  elements.loadingOverlay.addEventListener("keydown", constrainLoadingFocus);
  elements.sourceConfirmationOverlay.addEventListener("keydown", constrainSourceConfirmationFocus);
  elements.sourceConfirmationStay.addEventListener("click", () => closeSourceConfirmation(true));
  elements.sourceConfirmationApply.addEventListener("click", applyPendingSourceAndGoToJobs);
  wireKeywordInput("inclusions", elements.includeInput, elements.addInclusion);
  wireKeywordInput("exclusions", elements.excludeInput, elements.addExclusion);
  elements.jobsTab.addEventListener("click", () => showView("jobs"));
  elements.settingsTab.addEventListener("click", () => showView("settings"));
  elements.sourceSettingsLink.addEventListener("click", () => showView("settings", true));
  document.querySelector(".app-tabs").addEventListener("keydown", handleTabKeydown);
  elements.refreshButton.addEventListener("click", refreshJobs);
  elements.filterToggle.addEventListener("click", toggleSearchFilters);
  elements.companySelect.addEventListener("change", companySelectionChanged);
  elements.countrySelect.addEventListener("change", countrySelectionChanged);
  elements.includeAllLocations.addEventListener("change", sourceCoverageChanged);
  elements.includeRemote.addEventListener("change", sourceCoverageChanged);
  elements.locationSearch.addEventListener("input", filterLocationChoices);
  elements.applyLocation.addEventListener("click", applyWorkdayLocation);
  elements.minimumPay.addEventListener("input", () => {
    state.minimumSalary = parseCurrencyInput(elements.minimumPay.value);
    renderResults();
    queueSettingsSave();
  });
  elements.minimumPay.addEventListener("blur", () => {
    if (state.minimumSalary !== null) {
      elements.minimumPay.value = new Intl.NumberFormat(undefined, {
        maximumFractionDigits: 0
      }).format(state.minimumSalary);
    }
  });
  document.querySelectorAll('input[name="scope"]').forEach(radio => {
    radio.addEventListener("change", event => {
      state.scope = event.target.value;
      renderResults();
      queueSettingsSave();
    });
  });
  document.querySelectorAll('input[name="location-mode"]').forEach(radio => {
    radio.addEventListener("change", event => {
      state.locationMode = event.target.value;
      renderResults();
      queueSettingsSave();
    });
  });
  elements.highlightInclusions.addEventListener("change", () => {
    state.highlightInclusions = elements.highlightInclusions.checked;
    renderResults();
    queueSettingsSave();
  });
  elements.showHiddenJobs.addEventListener("change", () => {
    state.showDismissedJobs = elements.showHiddenJobs.checked;
    renderResults();
  });
  elements.automaticCheckEnabled.addEventListener("change", () => {
    state.automaticCheckEnabled = elements.automaticCheckEnabled.checked;
    elements.automaticCheckInterval.disabled = !state.automaticCheckEnabled;
    queueSettingsSave();
  });
  elements.automaticCheckInterval.addEventListener("change", () => {
    state.automaticCheckIntervalMinutes = Number(elements.automaticCheckInterval.value);
    queueSettingsSave();
  });
  elements.themeMode.addEventListener("change", () => {
    state.themeMode = elements.themeMode.value === "dark" ? "dark" : "light";
    applyTheme();
    queueSettingsSave();
  });
  elements.educationLevel.addEventListener("change", () => {
    state.educationLevel = normalizeEducationLevel(elements.educationLevel.value);
    if (state.educationLevel !== "doctorate") {
      state.doctorateType = null;
    }
    updateEducationSettingsUi();
    renderResults();
    queueSettingsSave();
  });
  elements.doctorateType.addEventListener("change", () => {
    state.doctorateType = elements.doctorateType.value === "phD" ? "phD" : null;
    renderResults();
    queueSettingsSave();
  });
  elements.hideStrictEducationMismatch.addEventListener("change", () => {
    state.hideStrictEducationMismatch = elements.hideStrictEducationMismatch.checked;
    renderResults();
    queueSettingsSave();
  });
  elements.clearanceProfileLevel.addEventListener("change", () => {
    state.clearanceProfileLevel = normalizeClearanceProfileLevel(elements.clearanceProfileLevel.value);
    renderResults();
    queueSettingsSave();
  });
  elements.publicTrustProfile.addEventListener("change", () => {
    state.publicTrustProfile = normalizePublicTrustProfile(elements.publicTrustProfile.value);
    renderResults();
    queueSettingsSave();
  });
  elements.hideStrictClearanceMismatch.addEventListener("change", () => {
    state.hideStrictClearanceMismatch = elements.hideStrictClearanceMismatch.checked;
    renderResults();
    queueSettingsSave();
  });
  elements.atAGlanceTab.addEventListener("click", () => showDetailTab("glance", true));
  elements.fullPostingTab.addEventListener("click", () => showDetailTab("posting", true));
  document.querySelector(".detail-tabs").addEventListener("keydown", handleDetailTabKeydown);
  elements.workdayLink.addEventListener("click", () => {
    const job = state.jobs.find(item => item.stableId === state.selectedJobId);
    if (job) {
      markJobViewed(job);
    }
  });

  await loadInitialState();
}

function showView(view, focusFirstControl = false, options = {}) {
  const nextView = view === "settings" ? "settings" : "jobs";
  if (nextView === "jobs" &&
      state.activeView === "settings" &&
      options.bypassSourceGuard !== true &&
      querySelectionIsPending()) {
    showSourceConfirmation();
    return false;
  }
  state.activeView = nextView;
  const jobsSelected = nextView === "jobs";
  elements.jobsView.hidden = !jobsSelected;
  elements.settingsView.hidden = jobsSelected;
  elements.jobsTab.classList.toggle("active", jobsSelected);
  elements.settingsTab.classList.toggle("active", !jobsSelected);
  elements.jobsTab.setAttribute("aria-selected", String(jobsSelected));
  elements.settingsTab.setAttribute("aria-selected", String(!jobsSelected));
  elements.jobsTab.tabIndex = jobsSelected ? 0 : -1;
  elements.settingsTab.tabIndex = jobsSelected ? -1 : 0;
  if (focusFirstControl) {
    (jobsSelected ? elements.filterToggle : elements.companySelect).focus();
  }
  return true;
}

function handleTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
    return;
  }
  event.preventDefault();
  const targetView = event.key === "ArrowLeft" || event.key === "Home"
    ? "jobs"
    : "settings";
  const changed = showView(targetView);
  if (changed) {
    (targetView === "jobs" ? elements.jobsTab : elements.settingsTab).focus();
  }
}

function showDetailTab(tab, moveFocus = false) {
  const nextTab = tab === "posting" ? "posting" : "glance";
  const changed = state.detailTab !== nextTab;
  state.detailTab = nextTab;
  const glanceSelected = state.detailTab === "glance";
  elements.atAGlancePanel.hidden = !glanceSelected;
  elements.fullPostingPanel.hidden = glanceSelected;
  elements.atAGlanceTab.classList.toggle("active", glanceSelected);
  elements.fullPostingTab.classList.toggle("active", !glanceSelected);
  elements.atAGlanceTab.setAttribute("aria-selected", String(glanceSelected));
  elements.fullPostingTab.setAttribute("aria-selected", String(!glanceSelected));
  elements.atAGlanceTab.tabIndex = glanceSelected ? 0 : -1;
  elements.fullPostingTab.tabIndex = glanceSelected ? -1 : 0;
  if (changed) {
    document.querySelector(".detail-pane").scrollTop = 0;
  }
  if (moveFocus) {
    (glanceSelected ? elements.atAGlanceTab : elements.fullPostingTab).focus();
  }
}

function handleDetailTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
    return;
  }
  event.preventDefault();
  const tab = event.key === "ArrowLeft" || event.key === "Home" ? "glance" : "posting";
  showDetailTab(tab, true);
}

async function loadInitialState() {
  try {
    const [companiesResponse, settingsResponse, jobsResponse] = await Promise.all([
      fetch("/api/companies", { cache: "no-store" }),
      fetch("/api/settings", { cache: "no-store" }),
      fetch("/api/jobs", { cache: "no-store" })
    ]);
    if (!companiesResponse.ok || !settingsResponse.ok || !jobsResponse.ok) {
      const failed = [companiesResponse, settingsResponse, jobsResponse].find(response => !response.ok);
      throw new Error(await apiErrorMessage(failed, "Application data could not be loaded."));
    }
    state.companies = await companiesResponse.json();
    populateCompanySelect();
    applySettings(await settingsResponse.json());
    const initialSnapshot = await jobsResponse.json();
    const needsInitialRefresh = !initialSnapshot.isRefreshing &&
      !initialSnapshot.lastRefreshedUtc &&
      (!initialSnapshot.jobs || initialSnapshot.jobs.length === 0);
    applySnapshot(initialSnapshot);
    await loadLocationFacets(
      state.companyId,
      state.country.id,
      state.physicalLocations,
      state.includeAllLocations,
      state.includeRemote);
    await loadAutomaticCheckStatus();
    state.isInitializing = false;
    if (needsInitialRefresh) {
      await refreshJobs();
    } else if (!state.catalogIsRefreshing) {
      setLoading(false);
    }
    window.setInterval(loadAutomaticCheckStatus, AUTOMATIC_STATUS_POLL_MS);
  } catch (error) {
    state.isInitializing = false;
    showClientError(error);
  }
}

function applySettings(settings) {
  state.inclusions = Array.isArray(settings.includeKeywords) ? settings.includeKeywords : [];
  state.exclusions = Array.isArray(settings.excludeKeywords) ? settings.excludeKeywords : [];
  state.minimumSalary = Number.isFinite(settings.minimumSalary) ? settings.minimumSalary : null;
  state.scope = settings.keywordScope === "description" ? "description" : "metadata";
  state.locationMode = ["all", "hide-restricted", "only-restricted"].includes(settings.locationMode)
    ? settings.locationMode
    : "all";
  state.highlightInclusions = settings.highlightIncludeKeywords !== false;
  state.collapsedAgeGroups = settings.collapsedAgeGroups || {};
  state.searchFiltersCollapsed = settings.searchFiltersCollapsed === true;
  state.automaticCheckEnabled = settings.automaticCheckEnabled !== false;
  state.automaticCheckIntervalMinutes = [30, 60, 120, 240, 480]
      .includes(settings.automaticCheckIntervalMinutes)
    ? settings.automaticCheckIntervalMinutes
    : 60;
  state.themeMode = settings.themeMode === "dark" ? "dark" : "light";
  state.educationLevel = normalizeEducationLevel(settings.userProfile?.education?.level);
  state.doctorateType = state.educationLevel === "doctorate" &&
    settings.userProfile?.education?.doctorateType === "phD"
    ? "phD"
    : null;
  state.hideStrictEducationMismatch = settings.hideStrictEducationMismatch === true;
  state.clearanceProfileLevel = normalizeClearanceProfileLevel(
    settings.userProfile?.security?.clearanceLevel);
  state.publicTrustProfile = normalizePublicTrustProfile(settings.userProfile?.security?.publicTrust);
  state.hideStrictClearanceMismatch = settings.hideStrictClearanceMismatch === true;
  state.companyId = settings.companyId || state.companies[0]?.id || "";
  state.companyName = companyById(state.companyId)?.displayName || state.companyId;
  elements.companySelect.value = state.companyId;
  state.loadingTitle = `Loading ${state.companyName} jobs`;
  elements.loadingTitle.textContent = state.loadingTitle;
  state.country = normalizeFacetSelection(settings.country, ALL_COUNTRIES_LABEL);
  state.includeAllLocations = settings.includeAllLocations === true;
  state.includeRemote = settings.includeRemote === true;
  state.physicalLocations = normalizeFacetSelections(settings.selectedPhysicalLocations);

  elements.minimumPay.value = state.minimumSalary === null
    ? ""
    : new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(state.minimumSalary);
  elements.highlightInclusions.checked = state.highlightInclusions;
  elements.automaticCheckEnabled.checked = state.automaticCheckEnabled;
  elements.automaticCheckInterval.value = String(state.automaticCheckIntervalMinutes);
  elements.automaticCheckInterval.disabled = !state.automaticCheckEnabled;
  elements.themeMode.value = state.themeMode;
  elements.educationLevel.value = state.educationLevel;
  elements.doctorateType.value = state.doctorateType || "";
  elements.hideStrictEducationMismatch.checked = state.hideStrictEducationMismatch;
  elements.clearanceProfileLevel.value = state.clearanceProfileLevel;
  elements.publicTrustProfile.value = state.publicTrustProfile;
  elements.hideStrictClearanceMismatch.checked = state.hideStrictClearanceMismatch;
  updateEducationSettingsUi();
  applyTheme();
  const scopeRadio = document.querySelector(`input[name="scope"][value="${state.scope}"]`);
  const locationRadio = document.querySelector(`input[name="location-mode"][value="${state.locationMode}"]`);
  if (scopeRadio) scopeRadio.checked = true;
  if (locationRadio) locationRadio.checked = true;
  renderChips("inclusions");
  renderChips("exclusions");
  updateSourceSummary();
  updateSearchFilterUi();
}

function applyTheme() {
  document.documentElement.dataset.theme = state.themeMode;
  try {
    localStorage.setItem("workday-job-manager-theme-hint", state.themeMode);
  } catch {
    // The persisted backend setting is authoritative; this cache is optional.
  }
}

function updateEducationSettingsUi() {
  const isDoctorate = state.educationLevel === "doctorate";
  elements.doctorateTypeField.hidden = !isDoctorate;
  elements.doctorateType.disabled = !isDoctorate;
}

function normalizeEducationLevel(value) {
  return ["notSpecified", "noCredential", "ged", "highSchool", "associate", "bachelor", "master", "doctorate"]
    .includes(value)
    ? value
    : "notSpecified";
}

function normalizeClearanceProfileLevel(value) {
  return ["notSpecified", "none", "secret", "topSecret", "topSecretSCI", "otherUnknown"]
    .includes(value)
    ? value
    : "notSpecified";
}

function normalizePublicTrustProfile(value) {
  if (value === "notSpecified") return "unknown";
  return ["unknown", "none", "current"].includes(value) ? value : "unknown";
}

async function loadAutomaticCheckStatus() {
  try {
    const response = await fetch("/api/automatic-check/status", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Automatic-check status returned HTTP ${response.status}.`);
    }
    const status = await response.json();
    renderAutomaticCheckStatus(status);

    if (status.isChecking && !state.isRefreshing) {
      await detectAutomaticFullRefresh();
    }

    const automaticRefreshUtc = status.lastAutomaticRefreshUtc || null;
    if (state.lastObservedAutomaticRefreshUtc &&
        automaticRefreshUtc &&
        automaticRefreshUtc !== state.lastObservedAutomaticRefreshUtc) {
      await loadAutomaticSnapshotPreservingUi();
    }
    state.lastObservedAutomaticRefreshUtc = automaticRefreshUtc;

    const nextCheckUtc = status.nextCheckUtc ? new Date(status.nextCheckUtc).getTime() : null;
    if (status.enabled && !status.isChecking && nextCheckUtc !== null &&
        nextCheckUtc <= Date.now() && !state.automaticCheckRequestInFlight) {
      runDueAutomaticCheck();
    }
  } catch (error) {
    console.warn("Automatic-check status is temporarily unavailable.", error);
  }
}

async function runDueAutomaticCheck() {
  state.automaticCheckRequestInFlight = true;
  try {
    const response = await fetch("/api/automatic-check/run", {
      method: "POST",
      cache: "no-store"
    });
    if (!response.ok) {
      throw new Error(`Automatic check returned HTTP ${response.status}.`);
    }
  } catch (error) {
    console.warn("The due automatic check could not be completed.", error);
  } finally {
    state.automaticCheckRequestInFlight = false;
    window.setTimeout(loadAutomaticCheckStatus, 250);
  }
}

async function detectAutomaticFullRefresh() {
  try {
    const response = await fetch("/api/jobs", { cache: "no-store" });
    if (!response.ok) return;
    const snapshot = await response.json();
    if (snapshot.isRefreshing) {
      setLoading(true, { title: `Refreshing ${state.companyName} jobs` });
      applySnapshot(snapshot);
    }
  } catch (error) {
    console.warn("Could not inspect automatic refresh progress.", error);
  }
}

function renderAutomaticCheckStatus(status) {
  if (!status.enabled) {
    elements.automaticCheckStatus.textContent = "Automatic checks disabled";
    return;
  }
  const lastChecked = status.lastCheckedUtc
    ? new Date(status.lastCheckedUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })
    : "Never";
  const nextCheck = status.nextCheckUtc
    ? new Date(status.nextCheckUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })
    : "Scheduling…";
  elements.automaticCheckStatus.textContent = status.isChecking
    ? `Checking now · Last checked: ${lastChecked}`
    : `Last checked: ${lastChecked} · Next check: ${nextCheck}`;
}

async function loadAutomaticSnapshotPreservingUi() {
  const listScrollTop = elements.jobList.scrollTop;
  const detailPane = document.querySelector(".detail-pane");
  const detailScrollTop = detailPane.scrollTop;
  try {
    const response = await fetch("/api/jobs", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Local API returned HTTP ${response.status}.`);
    }
    applySnapshot(await response.json());
    requestAnimationFrame(() => {
      elements.jobList.scrollTop = listScrollTop;
      detailPane.scrollTop = detailScrollTop;
    });
  } catch (error) {
    console.warn("The automatic-refresh snapshot could not be loaded.", error);
  }
}

function toggleSearchFilters() {
  state.searchFiltersCollapsed = !state.searchFiltersCollapsed;
  updateSearchFilterUi();
  queueSettingsSave();
}

function updateSearchFilterUi() {
  const collapsed = state.searchFiltersCollapsed;
  elements.filterContent.hidden = collapsed;
  elements.filterToggle.setAttribute("aria-expanded", String(!collapsed));
  elements.filterChevron.textContent = collapsed ? "▶" : "▼";
  elements.filterToggleAction.textContent = collapsed ? "Expand" : "Collapse";
  elements.filterSummary.textContent = buildFilterSummary();
}

function buildFilterSummary() {
  const summary = [];
  if (state.inclusions.length) summary.push(`Include: ${state.inclusions.length}`);
  if (state.exclusions.length) summary.push(`Exclude: ${state.exclusions.length}`);
  if (state.minimumSalary !== null) {
    summary.push(`Min pay: ${formatCurrency(state.minimumSalary)}`);
  }

  if (state.locationMode !== "all") {
    summary.push(state.locationMode === "hide-restricted"
      ? "Restricted remote hidden"
      : "Restricted remote only");
  }
  if (state.hideStrictEducationMismatch) {
    summary.push(`Education: ${educationLevelLabel(state.educationLevel)}`);
  }
  if (state.hideStrictClearanceMismatch) {
    summary.push("Strict clearance filter");
  }
  return summary.length
    ? summary.join(" · ")
    : "No active keyword, salary, remote-location, education, or clearance filters";
}

function updateSourceSummary() {
  const country = state.country.label || ALL_COUNTRIES_LABEL;
  elements.sourceSummary.textContent = `${state.companyName} · ${country} · ${describeSourceLocations(
    state.includeAllLocations,
    state.includeRemote,
    state.physicalLocations)}`;
}

function companyById(companyId) {
  return state.companies.find(company => company.id === companyId) || null;
}

function populateCompanySelect() {
  elements.companySelect.replaceChildren();
  for (const company of state.companies) {
    const option = document.createElement("option");
    option.value = company.id;
    option.textContent = company.displayName;
    elements.companySelect.append(option);
  }
}

async function companySelectionChanged() {
  const companyId = elements.companySelect.value;
  state.facetsLoaded = false;
  updateQueryControls();
  try {
    const response = await fetch(`/api/source/${encodeURIComponent(companyId)}`, {
      cache: "no-store"
    });
    if (!response.ok) {
      throw new Error(`Company source returned HTTP ${response.status}.`);
    }
    const result = await response.json();
    const source = result.source || {};
    const country = normalizeFacetSelection(source.country, ALL_COUNTRIES_LABEL);
    await loadLocationFacets(
      companyId,
      country.id,
      source.selectedPhysicalLocations || [],
      source.includeAllLocations === true,
      source.includeRemote === true);
  } catch (error) {
    elements.errorBanner.textContent = `Company source could not be loaded: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  }
}

function normalizeFacetSelection(selection, allLabel) {
  return selection?.id
    ? { id: selection.id, label: selection.label || selection.id }
    : { id: null, label: allLabel };
}

function normalizeFacetSelections(selections) {
  const unique = new Map();
  for (const selection of Array.isArray(selections) ? selections : []) {
    if (!selection?.id || unique.has(selection.id)) continue;
    unique.set(selection.id, {
      id: selection.id,
      label: selection.label || selection.id
    });
  }
  return [...unique.values()].sort((left, right) => left.id.localeCompare(right.id));
}

function describeSourceLocations(includeAll, includeRemote, physicalLocations) {
  if (includeAll) return ALL_LOCATIONS_LABEL;
  const labels = normalizeFacetSelections(physicalLocations).map(location => location.label);
  if (includeRemote && labels.length === 0) return "Remote/Teleworker";
  if (includeRemote && labels.length === 1) return `Remote + ${labels[0]}`;
  if (includeRemote && labels.length > 1) return `Remote + ${labels.length} locations`;
  if (labels.length <= 2) return labels.join(" + ") || "No locations";
  return `${labels.length} locations`;
}

async function loadLocationFacets(
  companyId,
  countryId,
  selectedLocations = [],
  includeAll = false,
  includeRemote = false) {
  state.facetsLoaded = false;
  let loaded = false;
  updateQueryControls();
  elements.facetStatus.textContent = "Loading Workday location choices…";
  try {
    const parameters = new URLSearchParams();
    parameters.set("companyId", companyId);
    if (countryId) parameters.set("countryId", countryId);
    const response = await fetch(`/api/location-facets?${parameters}`, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "Location choices could not be loaded."));
    }
    const facets = await response.json();
    populateCountrySelect(
      elements.countrySelect,
      facets.countries || [],
      ALL_COUNTRIES_LABEL,
      countryId);
    state.locationGroups = Array.isArray(facets.groups) ? facets.groups : [];
    state.remoteLocations = Array.isArray(facets.remoteLocations) ? facets.remoteLocations : [];
    state.facetMatchingJobs = Number(facets.matchingJobs) || 0;
    renderLocationGroups(state.locationGroups, selectedLocations);
    elements.includeAllLocations.checked = includeAll;
    elements.includeRemote.checked = includeAll && state.remoteLocations.length > 0
      ? true
      : includeRemote && state.remoteLocations.length > 0;
    elements.includeRemoteOption.hidden = state.remoteLocations.length === 0;
    elements.locationSearch.value = "";
    updateSelectedLocationSummary();
    loaded = true;
  } catch (error) {
    elements.facetStatus.textContent = `Location choices unavailable: ${error.message || error}`;
    elements.errorBanner.textContent = elements.facetStatus.textContent;
    elements.errorBanner.hidden = false;
  } finally {
    state.facetsLoaded = loaded;
    updateQueryControls();
  }
}

function populateCountrySelect(select, options, allLabel, selectedId) {
  select.replaceChildren();
  const all = document.createElement("option");
  all.value = "";
  all.dataset.label = allLabel;
  all.textContent = allLabel;
  select.append(all);

  const orderedOptions = WorkdayCountryOrdering.orderCountryFacets(options);
  for (const item of orderedOptions) {
    const option = document.createElement("option");
    option.value = item.id;
    option.dataset.label = item.label;
    option.textContent = `${item.label} (${new Intl.NumberFormat().format(item.count)})`;
    select.append(option);
  }

  const exists = Boolean(selectedId) && orderedOptions.some(option => option.id === selectedId);
  select.value = exists ? selectedId : "";
  return exists;
}

async function countrySelectionChanged() {
  const countryId = elements.countrySelect.value || null;
  await loadLocationFacets(
    elements.companySelect.value,
    countryId,
    [],
    true,
    true);
  updateQueryControls();
}

function selectedFacet(select, allLabel) {
  const option = select.selectedOptions[0];
  return {
    id: option?.value || null,
    label: option?.dataset.label || allLabel
  };
}

function renderLocationGroups(groups, selectedLocations) {
  const selected = new Map(normalizeFacetSelections(selectedLocations)
    .map(location => [location.id, location]));
  const knownIds = new Set((groups || []).flatMap(group =>
    (group.locations || []).map(location => location.id)));
  const missingSelected = [...selected.values()].filter(location => !knownIds.has(location.id));
  const renderedGroups = [...(groups || [])];
  if (missingSelected.length > 0) {
    renderedGroups.push({
      id: "selected-unavailable",
      label: "Selected locations",
      locations: missingSelected.map(location => ({
        ...location,
        displayLabel: location.label,
        count: 0
      }))
    });
  }

  elements.locationGroups.replaceChildren();
  if (renderedGroups.length === 0) {
    const empty = document.createElement("p");
    empty.className = "location-selector-empty";
    empty.textContent = "No physical location facets are available for this country.";
    elements.locationGroups.append(empty);
    updateSelectedLocationSummary();
    return;
  }

  for (const group of renderedGroups) {
    const details = document.createElement("details");
    details.className = "location-group";
    details.dataset.groupSearch = group.label.toLocaleLowerCase();
    details.open = (group.locations || []).some(location => selected.has(location.id));
    const summary = document.createElement("summary");
    summary.textContent = `${group.label} (${group.locations.length} ${group.locations.length === 1 ? "location" : "locations"})`;
    summary.setAttribute("aria-expanded", String(details.open));
    details.addEventListener("toggle", () => {
      summary.setAttribute("aria-expanded", String(details.open));
    });
    const choices = document.createElement("div");
    choices.className = "location-choice-list";
    for (const location of group.locations || []) {
      const label = document.createElement("label");
      label.className = "location-choice";
      label.dataset.locationSearch = `${group.label} ${location.label} ${location.displayLabel || ""}`
        .toLocaleLowerCase();
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = selected.has(location.id);
      checkbox.dataset.locationId = location.id;
      checkbox.dataset.locationLabel = location.label;
      checkbox.addEventListener("change", () => {
        updateSelectedLocationSummary();
        updateQueryControls();
      });
      const name = document.createElement("span");
      name.textContent = location.displayLabel || location.label;
      const count = document.createElement("small");
      count.textContent = `(${new Intl.NumberFormat().format(location.count || 0)})`;
      label.append(checkbox, name, count);
      choices.append(label);
    }
    details.append(summary, choices);
    elements.locationGroups.append(details);
  }
  updateSelectedLocationSummary();
}

function filterLocationChoices() {
  const query = elements.locationSearch.value.trim().toLocaleLowerCase();
  for (const group of elements.locationGroups.querySelectorAll(".location-group")) {
    let visibleCount = 0;
    const groupMatch = query && group.dataset.groupSearch.includes(query);
    for (const choice of group.querySelectorAll(".location-choice")) {
      const visible = !query || groupMatch || choice.dataset.locationSearch.includes(query);
      choice.hidden = !visible;
      if (visible) visibleCount++;
    }
    group.hidden = visibleCount === 0;
    if (query && visibleCount > 0) group.open = true;
  }
}

function selectedPendingLocations() {
  return normalizeFacetSelections([...elements.locationGroups
    .querySelectorAll('input[type="checkbox"][data-location-id]:checked')]
    .map(checkbox => ({
      id: checkbox.dataset.locationId,
      label: checkbox.dataset.locationLabel
    })));
}

function updateSelectedLocationSummary() {
  const selected = selectedPendingLocations();
  elements.selectedLocationSummary.replaceChildren();
  if (elements.includeAllLocations.checked) {
    elements.selectedLocationSummary.textContent = "Country-wide source; individual locations are disabled.";
    return;
  }
  if (selected.length === 0) {
    elements.selectedLocationSummary.textContent = elements.includeRemote.checked
      ? "No physical locations selected — Remote/Teleworker only."
      : "No physical locations selected.";
    return;
  }
  const prefix = document.createElement("span");
  prefix.textContent = `Selected (${selected.length}):`;
  elements.selectedLocationSummary.append(prefix);
  for (const location of selected) {
    const chip = document.createElement("span");
    chip.className = "selected-location-chip";
    chip.textContent = location.label;
    elements.selectedLocationSummary.append(chip);
  }
}

function sourceCoverageChanged() {
  if (elements.includeAllLocations.checked && state.remoteLocations.length > 0) {
    elements.includeRemote.checked = true;
  }
  updateSelectedLocationSummary();
  updateQueryControls();
}

function querySelectionIsPending() {
  const physicalIds = selectedPendingLocations().map(location => location.id);
  const activePhysicalIds = normalizeFacetSelections(state.physicalLocations)
    .map(location => location.id);
  const pendingRemote = elements.includeAllLocations.checked && state.remoteLocations.length > 0
    ? true
    : elements.includeRemote.checked;
  return elements.companySelect.value !== state.companyId ||
    (elements.countrySelect.value || null) !== state.country.id ||
    elements.includeAllLocations.checked !== state.includeAllLocations ||
    pendingRemote !== state.includeRemote ||
    physicalIds.length !== activePhysicalIds.length ||
    physicalIds.some((id, index) => id !== activePhysicalIds[index]);
}

function formatSourceDescription(companyName, country, includeAll, includeRemote, physicalLocations) {
  return `${companyName || "Workday"} · ${country?.label || ALL_COUNTRIES_LABEL} · ${describeSourceLocations(
    includeAll,
    includeRemote,
    physicalLocations)}`;
}

function pendingSourceDescription() {
  const includeAllLocations = elements.includeAllLocations.checked;
  const includeRemote = includeAllLocations && state.remoteLocations.length > 0
    ? true
    : elements.includeRemote.checked;
  return formatSourceDescription(
    companyById(elements.companySelect.value)?.displayName || elements.companySelect.value,
    selectedFacet(elements.countrySelect, ALL_COUNTRIES_LABEL),
    includeAllLocations,
    includeRemote,
    includeAllLocations ? [] : selectedPendingLocations());
}

function showSourceConfirmation() {
  clearTimeout(state.sourceConfirmationHideTimer);
  state.sourceConfirmationOpen = true;
  state.focusBeforeSourceConfirmation = document.activeElement;
  elements.sourceConfirmationCurrent.textContent = formatSourceDescription(
    state.companyName,
    state.country,
    state.includeAllLocations,
    state.includeRemote,
    state.physicalLocations);
  elements.sourceConfirmationPending.textContent = pendingSourceDescription();
  elements.appShell.inert = true;
  elements.sourceConfirmationOverlay.hidden = false;
  elements.sourceConfirmationOverlay.setAttribute("aria-hidden", "false");
  requestAnimationFrame(() => {
    elements.sourceConfirmationOverlay.classList.add("visible");
    elements.sourceConfirmationApply.focus({ preventScroll: true });
  });
}

function closeSourceConfirmation(restoreFocus) {
  if (!state.sourceConfirmationOpen) return;
  state.sourceConfirmationOpen = false;
  elements.sourceConfirmationOverlay.classList.remove("visible");
  elements.sourceConfirmationOverlay.setAttribute("aria-hidden", "true");
  elements.appShell.inert = state.isRefreshing;
  state.sourceConfirmationHideTimer = setTimeout(() => {
    if (!state.sourceConfirmationOpen) elements.sourceConfirmationOverlay.hidden = true;
  }, OVERLAY_TRANSITION_MS);
  if (restoreFocus && state.focusBeforeSourceConfirmation?.isConnected) {
    state.focusBeforeSourceConfirmation.focus({ preventScroll: true });
  }
  state.focusBeforeSourceConfirmation = null;
}

async function applyPendingSourceAndGoToJobs() {
  closeSourceConfirmation(false);
  elements.companySelect.focus({ preventScroll: true });
  await applyWorkdayLocation({ navigateToJobs: true });
}

function updateQueryControls() {
  const disabled = !state.facetsLoaded || state.isRefreshing;
  elements.companySelect.disabled = state.isRefreshing;
  elements.countrySelect.disabled = disabled;
  elements.includeAllLocations.disabled = disabled;
  const allLocations = elements.includeAllLocations.checked;
  elements.includeRemote.disabled = disabled || allLocations;
  elements.locationSearch.disabled = disabled || allLocations;
  for (const checkbox of elements.locationGroups.querySelectorAll('input[type="checkbox"]')) {
    checkbox.disabled = disabled || allLocations;
  }

  const hasExplicitSource = allLocations || elements.includeRemote.checked ||
    selectedPendingLocations().length > 0;
  const pending = !disabled && querySelectionIsPending();
  elements.applyLocation.disabled = disabled || !hasExplicitSource || !pending;
  if (!state.facetsLoaded) return;
  const context = `${new Intl.NumberFormat().format(state.facetMatchingJobs)} jobs in this country context.`;
  elements.facetStatus.textContent = !hasExplicitSource
    ? "Choose at least one location source before applying."
    : pending
      ? `Unsaved source changes · ${context}`
      : `Source matches currently loaded jobs · ${context}`;
}

async function applyWorkdayLocation(options = {}) {
  const companyId = elements.companySelect.value;
  const company = companyById(companyId);
  const country = selectedFacet(elements.countrySelect, ALL_COUNTRIES_LABEL);
  const includeAllLocations = elements.includeAllLocations.checked;
  const includeRemote = includeAllLocations && state.remoteLocations.length > 0
    ? true
    : elements.includeRemote.checked;
  const physicalLocations = includeAllLocations ? [] : selectedPendingLocations();
  clearTimeout(state.pollTimer);
  setLoading(true, { title: `Loading ${company?.displayName || "Workday"} jobs` });
  beginRefreshProgressPolling();
  elements.errorBanner.hidden = true;
  try {
    const response = await fetch("/api/query", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        companyId,
        countryId: country.id,
        countryLabel: country.label,
        includeAllLocations,
        includeRemote,
        physicalLocations,
        sourceModelVersion: 2
      })
    });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "The job source could not be refreshed."));
    }
    const snapshot = await response.json();
    if (snapshot.error) {
      showSnapshotError(snapshot.error, snapshot.detailFailureCount || 0);
      setLoading(false);
      updateQueryControls();
      return false;
    }
    applySnapshot(snapshot);
    await loadAutomaticCheckStatus();
    if (options.navigateToJobs === true) {
      showView("jobs", true, { bypassSourceGuard: true });
    }
    return true;
  } catch (error) {
    showClientError(error);
    return false;
  }
}

async function loadSnapshot() {
  try {
    const response = await fetch("/api/jobs", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Local API returned HTTP ${response.status}.`);
    }
    applySnapshot(await response.json());
    await loadAutomaticCheckStatus();
  } catch (error) {
    showClientError(error);
  }
}

async function refreshJobs() {
  clearTimeout(state.pollTimer);
  setLoading(true, { title: `Refreshing ${state.companyName} jobs` });
  beginRefreshProgressPolling();
  elements.errorBanner.hidden = true;
  try {
    const response = await fetch("/api/refresh", { method: "POST", cache: "no-store" });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "Jobs could not be refreshed."));
    }
    applySnapshot(await response.json());
    await loadAutomaticCheckStatus();
  } catch (error) {
    showClientError(error);
    await loadSnapshot();
  }
}

function applySnapshot(snapshot) {
  state.jobs = (snapshot.jobs || []).map(job => ({
    ...job,
    descriptionText: descriptionToText(job.descriptionHtml || "")
  }));
  state.lastRefreshedUtc = snapshot.lastRefreshedUtc;
  state.isCached = Boolean(snapshot.isCached);
  state.newJobIds = new Set(snapshot.newJobIds || []);
  state.dismissedJobIds = new Set(snapshot.dismissedJobIds || []);
  if (snapshot.query) {
    state.companyId = snapshot.query.companyId || state.companies[0]?.id || "";
    state.companyName = companyById(state.companyId)?.displayName || state.companyId;
    elements.companySelect.value = state.companyId;
    state.country = {
      id: snapshot.query.countryId || null,
      label: snapshot.query.countryLabel || ALL_COUNTRIES_LABEL
    };
    state.includeAllLocations = snapshot.query.includeAllLocations === true;
    state.includeRemote = snapshot.query.includeRemote === true;
    state.physicalLocations = normalizeFacetSelections(snapshot.query.physicalLocations);
  }
  updateSourceSummary();

  state.catalogIsRefreshing = Boolean(snapshot.isRefreshing);
  setLoading(state.catalogIsRefreshing || state.isInitializing, {
    progress: snapshot.refreshProgress
  });
  showSnapshotError(snapshot.error, snapshot.detailFailureCount || 0);
  renderResults();
  updateLastRefreshed();
  updateCacheBanner(snapshot);

  clearTimeout(state.pollTimer);
  if (snapshot.isRefreshing) {
    state.pollTimer = setTimeout(loadSnapshot, 750);
  }
  updateQueryControls();
}

function updateCacheBanner(snapshot) {
  const usingCache = Boolean(snapshot.isCached);
  if (!usingCache || snapshot.isRefreshing) {
    elements.cacheBanner.hidden = true;
    elements.cacheBanner.textContent = "";
    return;
  }

  const refreshed = snapshot.lastRefreshedUtc
    ? new Date(snapshot.lastRefreshedUtc).toLocaleString()
    : "an earlier run";
  elements.cacheBanner.textContent = snapshot.error
    ? `Showing cached jobs from ${refreshed}; the live refresh failed.`
    : `Showing cached jobs from ${refreshed}.`;
  elements.cacheBanner.hidden = false;
}

function wireKeywordInput(kind, input, button) {
  button.addEventListener("click", () => addKeywordsFromInput(kind, input));
  input.addEventListener("keydown", event => {
    if (event.key === "Enter") {
      event.preventDefault();
      addKeywordsFromInput(kind, input);
    }
  });
}

function addKeywordsFromInput(kind, input) {
  const values = input.value
    .split(/[,\n]/)
    .map(value => value.trim())
    .filter(Boolean);

  const terms = state[kind];
  for (const value of values) {
    if (!terms.some(existing => existing.toLocaleLowerCase() === value.toLocaleLowerCase())) {
      terms.push(value);
    }
  }
  input.value = "";
  renderChips(kind);
  renderResults();
  queueSettingsSave();
  input.focus();
}

function removeKeyword(kind, index) {
  state[kind].splice(index, 1);
  renderChips(kind);
  renderResults();
  queueSettingsSave();
}

function queueSettingsSave() {
  clearTimeout(state.settingsSaveTimer);
  state.settingsSaveTimer = setTimeout(saveSettings, SETTINGS_SAVE_DEBOUNCE_MS);
}

async function saveSettings() {
  clearTimeout(state.settingsSaveTimer);
  state.settingsSaveTimer = null;
  try {
    const response = await fetch("/api/settings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        includeKeywords: state.inclusions,
        excludeKeywords: state.exclusions,
        minimumSalary: state.minimumSalary,
        keywordScope: state.scope,
        locationMode: state.locationMode,
        highlightIncludeKeywords: state.highlightInclusions,
        collapsedAgeGroups: state.collapsedAgeGroups,
        companyId: state.companyId,
        country: state.country,
        includeAllLocations: state.includeAllLocations,
        includeRemote: state.includeRemote,
        selectedPhysicalLocations: state.physicalLocations,
        searchFiltersCollapsed: state.searchFiltersCollapsed,
        automaticCheckEnabled: state.automaticCheckEnabled,
        automaticCheckIntervalMinutes: state.automaticCheckIntervalMinutes,
        themeMode: state.themeMode,
        userProfile: {
          education: {
            level: state.educationLevel,
            doctorateType: state.doctorateType
          },
          security: {
            clearanceLevel: state.clearanceProfileLevel,
            publicTrust: state.publicTrustProfile
          }
        },
        hideStrictEducationMismatch: state.hideStrictEducationMismatch,
        hideStrictClearanceMismatch: state.hideStrictClearanceMismatch
      })
    });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "Settings could not be saved."));
    }
    await loadAutomaticCheckStatus();
  } catch (error) {
    elements.errorBanner.textContent = `Settings could not be saved: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  }
}

function renderChips(kind) {
  const container = kind === "inclusions" ? elements.inclusionChips : elements.exclusionChips;
  const singularLabel = kind === "inclusions" ? "inclusion" : "exclusion";
  container.replaceChildren();
  state[kind].forEach((term, index) => {
    const chip = document.createElement("span");
    chip.className = `chip ${singularLabel}-chip`;
    chip.append(document.createTextNode(term));

    const remove = document.createElement("button");
    remove.type = "button";
    remove.setAttribute("aria-label", `Remove ${singularLabel} ${term}`);
    remove.textContent = "×";
    remove.addEventListener("click", () => removeKeyword(kind, index));
    chip.append(remove);
    container.append(chip);
  });
}

function jobsPassingGeneralFilters() {
  const inclusionTerms = state.inclusions.map(term => term.toLocaleLowerCase());
  const exclusionTerms = state.exclusions.map(term => term.toLocaleLowerCase());

  return state.jobs.filter(job => {
    const metadata = [
      job.title,
      job.requisitionId,
      job.primaryLocation,
      ...(job.additionalLocations || [])
    ].join("\n").toLocaleLowerCase();
    const haystack = state.scope === "description"
      ? `${metadata}\n${job.descriptionText.toLocaleLowerCase()}`
      : metadata;

    const passesInclusion = inclusionTerms.length === 0 ||
      inclusionTerms.some(term => haystack.includes(term));
    const passesExclusion = !exclusionTerms.some(term => haystack.includes(term));
    const passesSalary = state.minimumSalary === null ||
      job.payPeriod !== "annual" ||
      job.payMaximum === null ||
      job.payMaximum >= state.minimumSalary;
    const passesLocation = state.locationMode === "all" ||
      (state.locationMode === "hide-restricted" && !job.isRemoteLocationRestricted) ||
      (state.locationMode === "only-restricted" && job.isRemoteLocationRestricted);
    const educationStatus = evaluateEducationMatch(job.academicQualification, currentEducationProfile());
    const passesEducation = !state.hideStrictEducationMismatch || !educationStatus.hide;
    const clearanceStatus = evaluateClearanceMatch(job, currentSecurityProfile());
    const passesClearance = !state.hideStrictClearanceMismatch || !clearanceStatus.hide;

    return passesInclusion && passesExclusion && passesSalary && passesLocation &&
      passesEducation && passesClearance;
  });
}

const CLEARANCE_LEVEL_RANK = Object.freeze({
  none: 0,
  secret: 1,
  topSecret: 2,
  topSecretSCI: 3
});

function currentSecurityProfile() {
  return {
    clearanceLevel: state.clearanceProfileLevel,
    publicTrust: state.publicTrustProfile
  };
}

function evaluateClearanceMatch(job, profile) {
  const level = job?.clearanceLevel || "noneMentioned";
  const requirement = job?.clearanceRequirement || "none";
  const parseStatus = job?.clearanceParseStatus || "not-mentioned";
  const strict = requirement === "activeRequired" || requirement === "mustPossess";
  const user = {
    clearanceLevel: normalizeClearanceProfileLevel(profile?.clearanceLevel),
    publicTrust: normalizePublicTrustProfile(profile?.publicTrust)
  };
  const publicTrustJob = level === "publicTrust";
  const userLabel = publicTrustJob
    ? publicTrustProfileLabel(user.publicTrust)
    : clearanceProfileLevelLabel(user.clearanceLevel);

  if (level === "noneMentioned" || requirement === "none") {
    return {
      kind: "noneSpecified",
      hide: false,
      strict: false,
      userLabel,
      summary: "No clearance requirement identified",
      explanation: "The posting does not state a recognized clearance requirement."
    };
  }

  if (parseStatus !== "parsed" || level === "other" || requirement === "ambiguous") {
    return {
      kind: "uncertain",
      hide: false,
      strict: false,
      userLabel,
      summary: "Clearance wording requires review",
      explanation: "The clearance language is uncertain, so this job remains visible."
    };
  }

  if (!strict) {
    const obtainable = requirement === "obtain" || requirement === "obtainAndMaintain" ||
      requirement === "eligible" || requirement === "publicTrustSuitability";
    return {
      kind: requirement === "preferred" ? "preferredOnly" : "notStrict",
      hide: false,
      strict: false,
      userLabel,
      summary: requirement === "preferred"
        ? "Clearance is preferred, not required"
        : obtainable
          ? "Obtainable after hire / not automatically disqualifying"
          : "Not a strict day-one hiring blocker",
      explanation: obtainable
        ? "The posting allows the clearance or suitability status to be obtained or established; it is not treated as an already-held requirement."
        : "Only explicit active/current/day-one requirements can hide a job."
    };
  }

  if (publicTrustJob) {
    if (user.publicTrust === "unknown") {
      return {
        kind: "profileNotConfigured",
        hide: false,
        strict: true,
        userLabel,
        summary: "Strict Public Trust requirement; status unknown",
        explanation: "The posting explicitly requires current Public Trust status, but your separate Public Trust status is unknown. The job remains visible."
      };
    }
    if (user.publicTrust !== "current") {
      return {
        kind: "strictMismatch",
        hide: true,
        strict: true,
        userLabel,
        summary: "Does not meet strict current Public Trust requirement",
        explanation: "The posting explicitly requires current Public Trust status, which your profile says you do not hold."
      };
    }
    return {
      kind: job.polygraphRequired ? "meetsLevelPolygraphReview" : "meets",
      hide: false,
      strict: true,
      userLabel,
      summary: job.polygraphRequired
        ? "Public Trust status meets; polygraph requires separate review"
        : "Meets strict current Public Trust requirement",
      explanation: job.polygraphRequired
        ? "Your Public Trust status matches, but this posting also requires a polygraph that the profile does not track."
        : "Your separately reported Public Trust status meets this strict requirement."
    };
  }

  if (!(level in CLEARANCE_LEVEL_RANK)) {
    return {
      kind: "uncertain",
      hide: false,
      strict: true,
      userLabel,
      summary: "Strict clearance language requires review",
      explanation: "The required clearance level could not be compared confidently, so this job remains visible."
    };
  }

  if (user.clearanceLevel === "notSpecified" || user.clearanceLevel === "otherUnknown") {
    return {
      kind: "profileNotConfigured",
      hide: false,
      strict: true,
      userLabel,
      summary: "Strict clearance requirement; profile not comparable",
      explanation: "Choose a specific current clearance level in Settings to enable strict comparison. The job remains visible."
    };
  }

  if ((CLEARANCE_LEVEL_RANK[user.clearanceLevel] ?? -1) < CLEARANCE_LEVEL_RANK[level]) {
    return {
      kind: "strictMismatch",
      hide: true,
      strict: true,
      userLabel,
      summary: "Does not meet strict current-clearance requirement",
      explanation: `The posting requires an active/current ${clearanceLevelLabel(level)}, while your profile reports ${userLabel}.`
    };
  }

  return {
    kind: job.polygraphRequired ? "meetsLevelPolygraphReview" : "meets",
    hide: false,
    strict: true,
    userLabel,
    summary: job.polygraphRequired
      ? "Clearance level meets; polygraph requires separate review"
      : "Meets strict current-clearance requirement",
    explanation: job.polygraphRequired
      ? `Your ${userLabel} meets the clearance level, but this posting also requires a polygraph that the profile does not track.`
      : `Your ${userLabel} meets or exceeds the strict ${clearanceLevelLabel(level)} requirement.`
  };
}

const EDUCATION_LEVEL_RANK = Object.freeze({
  noCredential: 0,
  ged: 1,
  highSchool: 1,
  associate: 2,
  bachelor: 3,
  master: 4,
  doctorate: 5
});

function currentEducationProfile() {
  return { level: state.educationLevel, doctorateType: state.doctorateType };
}

function evaluateEducationMatch(academic, profile) {
  const user = {
    level: normalizeEducationLevel(profile?.level),
    doctorateType: profile?.level === "doctorate" && profile?.doctorateType === "phD"
      ? "phD"
      : null
  };
  const userLabel = educationProfileLabel(user);
  if (user.level === "notSpecified") {
    return {
      kind: "profileNotConfigured",
      hide: false,
      userLabel,
      summary: "Education profile not configured",
      explanation: "Choose your highest completed education in Settings to enable personal comparison."
    };
  }
  if (!academic || academic.requirementType === "noDegreeSpecified") {
    return {
      kind: "noneSpecified",
      hide: false,
      userLabel,
      summary: "No academic requirement specified",
      explanation: "The posting does not state a recognized academic requirement."
    };
  }

  const requiredLabel = academicLevelLabel(academic.minimumLevel, academic.specificDegree);
  if (academic.parseStatus !== "parsed" || academic.requirementType === "mentionedUnclear") {
    return {
      kind: "uncertain",
      hide: false,
      userLabel,
      requiredLabel,
      summary: "Academic wording is uncertain",
      explanation: "The parser found academic language but will not use it to exclude this job."
    };
  }

  if (academic.requirementType === "preferredOnly") {
    return {
      kind: "preferredOnly",
      hide: false,
      userLabel,
      requiredLabel,
      summary: `${requiredLabel} preferred`,
      explanation: "This is a preference, not a strict minimum requirement."
    };
  }

  if (academic.experienceSubstitutionAccepted ||
      academic.requirementType === "degreeOrExperience" ||
      academic.requirementType === "degreeWithExperienceSubstitution") {
    return {
      kind: "flexible",
      hide: false,
      userLabel,
      requiredLabel,
      summary: academic.requirementType === "degreeWithExperienceSubstitution"
        ? "Alternative degree/experience paths"
        : `${requiredLabel} or experience alternative`,
      explanation: "The posting provides an experience or alternate education path, so no automatic mismatch is applied."
    };
  }

  if (academic.requirementType !== "strictDegree") {
    return {
      kind: "uncertain",
      hide: false,
      userLabel,
      requiredLabel,
      summary: "Academic requirement is not strict",
      explanation: "This academic language is informational and will not exclude the job."
    };
  }

  const requiredRank = EDUCATION_LEVEL_RANK[academic.minimumLevel] ?? 0;
  const userRank = EDUCATION_LEVEL_RANK[user.level] ?? 0;
  if (academic.minimumLevel === "doctorate" && academic.specificDegree === "phD" &&
      user.level === "doctorate" && user.doctorateType !== "phD") {
    return {
      kind: "specificDegreeUncertain",
      hide: false,
      userLabel,
      requiredLabel,
      summary: "Specific Ph.D. requirement is uncertain",
      explanation: "You reported a doctorate without specifying Ph.D.; the application will not assume equivalence or hide this job."
    };
  }

  if (requiredRank > userRank) {
    return {
      kind: "strictMismatch",
      hide: true,
      userLabel,
      requiredLabel,
      summary: `${requiredLabel} required`,
      explanation: `The posting's strict ${requiredLabel} requirement is above your completed ${userLabel}.`
    };
  }

  const preferredLevels = Array.isArray(academic.preferredLevels) ? academic.preferredLevels : [];
  const unmetPreferred = preferredLevels
    .filter(level => (EDUCATION_LEVEL_RANK[level] ?? 0) > userRank)
    .sort((left, right) => (EDUCATION_LEVEL_RANK[left] ?? 0) - (EDUCATION_LEVEL_RANK[right] ?? 0))[0];
  return {
    kind: unmetPreferred ? "meetsMinimumPreferredNotMet" : "meets",
    hide: false,
    userLabel,
    requiredLabel,
    preferredLabel: unmetPreferred ? academicLevelLabel(unmetPreferred) : null,
    summary: unmetPreferred
      ? `Meets minimum; ${academicLevelLabel(unmetPreferred)} preferred`
      : "Meets strict education requirement",
    explanation: unmetPreferred
      ? `Your ${userLabel} meets the strict ${requiredLabel} minimum; ${academicLevelLabel(unmetPreferred)} is preferred.`
      : `Your ${userLabel} meets or exceeds the strict ${requiredLabel} requirement.`
  };
}

function educationLevelLabel(level) {
  return ({
    notSpecified: "Not configured",
    noCredential: "No high school credential",
    ged: "GED",
    highSchool: "High school diploma",
    associate: "Associate degree",
    bachelor: "Bachelor's degree",
    master: "Master's degree",
    doctorate: "Doctorate"
  })[level] || "No high school credential";
}

function educationProfileLabel(profile) {
  return profile?.level === "doctorate" && profile?.doctorateType === "phD"
    ? "Ph.D."
    : educationLevelLabel(profile?.level);
}

function renderResults() {
  const matchingJobs = jobsPassingGeneralFilters();
  const dismissedMatchingJobs = matchingJobs.filter(job => state.dismissedJobIds.has(job.stableId));
  const jobs = state.showDismissedJobs
    ? matchingJobs
    : matchingJobs.filter(job => !state.dismissedJobIds.has(job.stableId));
  elements.showHiddenJobs.checked = state.showDismissedJobs;
  elements.hiddenJobCount.textContent = `(${dismissedMatchingJobs.length})`;
  elements.resultCount.textContent = `Showing ${jobs.length} of ${state.jobs.length} jobs` +
    (!state.showDismissedJobs && dismissedMatchingJobs.length
      ? ` · ${dismissedMatchingJobs.length} hidden`
      : "");
  elements.filterSummary.textContent = buildFilterSummary();

  if (!jobs.some(job => job.stableId === state.selectedJobId)) {
    // Automatic selection is presentation only and deliberately does not mark NEW as viewed.
    const nextSelectedJobId = jobs[0]?.stableId ?? null;
    if (nextSelectedJobId !== state.selectedJobId) {
      state.detailTab = "glance";
    }
    state.selectedJobId = nextSelectedJobId;
  }

  elements.jobList.replaceChildren();
  if (jobs.length === 0) {
    const empty = document.createElement("p");
    empty.className = "list-empty";
    empty.textContent = state.jobs.length === 0
      ? "No jobs are available."
      : "No jobs remain after applying the exclusions.";
    elements.jobList.append(empty);
  } else {
    const fragment = document.createDocumentFragment();
    const grouped = groupJobsByAge(jobs);
    for (const group of AGE_GROUPS) {
      const groupJobs = grouped.get(group.id) || [];
      if (groupJobs.length > 0) {
        fragment.append(createAgeGroup(group, groupJobs));
      }
    }
    elements.jobList.append(fragment);
  }

  const selected = jobs.find(job => job.stableId === state.selectedJobId);
  renderDetail(selected || null);
}

function groupJobsByAge(jobs) {
  const grouped = new Map(AGE_GROUPS.map(group => [group.id, []]));
  for (const job of jobs) {
    const age = getPostingAgeInDays(job.startDate);
    const group = age === null
      ? AGE_GROUPS[AGE_GROUPS.length - 1]
      : AGE_GROUPS.find(candidate => candidate.minimumDays !== null &&
          age >= candidate.minimumDays && age <= candidate.maximumDays);
    grouped.get(group?.id || "unknown").push(job);
  }
  return grouped;
}

function getPostingAgeInDays(isoDate) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(isoDate || "")) {
    return null;
  }
  const [year, month, day] = isoDate.split("-").map(Number);
  const now = new Date();
  const todayUtc = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  const postedUtc = Date.UTC(year, month - 1, day);
  return Math.max(0, Math.floor((todayUtc - postedUtc) / 86400000));
}

function createAgeGroup(group, jobs) {
  const section = document.createElement("section");
  section.className = "age-group";
  const collapsed = Boolean(state.collapsedAgeGroups[group.id]);

  const heading = document.createElement("button");
  heading.type = "button";
  heading.className = "age-group-heading";
  heading.setAttribute("aria-expanded", String(!collapsed));
  heading.innerHTML = `<span class="age-group-arrow" aria-hidden="true">${collapsed ? "▶" : "▼"}</span>`;
  const label = document.createElement("span");
  label.textContent = `${group.label} (${jobs.length})`;
  heading.append(label);

  const contents = document.createElement("div");
  contents.className = "age-group-jobs";
  contents.hidden = collapsed;
  for (const job of jobs) {
    contents.append(createJobListItem(job));
  }

  heading.addEventListener("click", () => {
    state.collapsedAgeGroups[group.id] = !collapsed;
    if (!state.collapsedAgeGroups[group.id]) {
      delete state.collapsedAgeGroups[group.id];
    }
    renderResults();
    queueSettingsSave();
  });
  section.append(heading, contents);
  return section;
}

function createJobListItem(job) {
  const card = document.createElement("div");
  card.className = "job-card";
  const isDismissed = state.dismissedJobIds.has(job.stableId);
  if (isDismissed) {
    card.classList.add("dismissed");
  }

  const button = document.createElement("button");
  button.type = "button";
  button.className = "job-card-main";
  button.setAttribute("aria-pressed", String(job.stableId === state.selectedJobId));
  if (job.stableId === state.selectedJobId) {
    card.classList.add("selected");
  }

  const date = document.createElement("time");
  date.dateTime = job.startDate || "";
  date.textContent = formatShortDate(job.startDate) || job.postedOn || "Date unavailable";

  const title = document.createElement("strong");
  appendHighlightedText(title, job.title || "Untitled job");

  const location = document.createElement("span");
  appendHighlightedText(location, job.primaryLocation || "Location unavailable");

  const requisition = document.createElement("span");
  appendHighlightedText(requisition, job.requisitionId);

  const pay = document.createElement("span");
  pay.className = "job-pay";
  pay.textContent = `Pay: ${formatPay(job)}`;

  button.append(date, title, location, requisition, pay);
  const badges = document.createElement("span");
  badges.className = "job-badges";
  if (state.newJobIds.has(job.stableId)) {
    const newBadge = document.createElement("span");
    newBadge.className = "new-badge";
    newBadge.textContent = "NEW";
    badges.append(newBadge);
  }
  if (isDismissed) {
    const hiddenBadge = document.createElement("span");
    hiddenBadge.className = "hidden-badge";
    hiddenBadge.textContent = "Hidden";
    badges.append(hiddenBadge);
  }
  if (job.clearanceLevel && job.clearanceLevel !== "noneMentioned") {
    const clearanceBadge = document.createElement("span");
    clearanceBadge.className = "clearance-badge";
    clearanceBadge.textContent = clearanceBadgeLabel(job);
    badges.append(clearanceBadge);
  }
  const clearanceStatus = evaluateClearanceMatch(job, currentSecurityProfile());
  if (clearanceStatus.kind === "strictMismatch") {
    const clearanceMismatchBadge = document.createElement("span");
    clearanceMismatchBadge.className = "clearance-mismatch-badge";
    clearanceMismatchBadge.textContent = `${clearanceLevelLabel(job.clearanceLevel)} required`;
    clearanceMismatchBadge.title = clearanceStatus.explanation;
    badges.append(clearanceMismatchBadge);
  }
  const academicQualification = job.academicQualification;
  if (academicQualification && academicQualification.requirementType !== "noDegreeSpecified") {
    const academicBadge = document.createElement("span");
    academicBadge.className = "academic-badge";
    academicBadge.textContent = academicBadgeLabel(academicQualification);
    badges.append(academicBadge);
  }
  const educationStatus = evaluateEducationMatch(academicQualification, currentEducationProfile());
  if (educationStatus.kind === "strictMismatch") {
    const mismatchBadge = document.createElement("span");
    mismatchBadge.className = "education-mismatch-badge";
    mismatchBadge.textContent = "Education mismatch";
    mismatchBadge.title = educationStatus.explanation;
    badges.append(mismatchBadge);
  }
  const credentials = Array.isArray(job.credentials) ? job.credentials : [];
  credentials.slice(0, 2).forEach(credential => {
    const credentialBadge = document.createElement("span");
    credentialBadge.className = `credential-badge${credential.requirement === "required" ? " required" : ""}`;
    credentialBadge.textContent = credentialBadgeLabel(credential);
    credentialBadge.title = credential.fullName || credential.name;
    badges.append(credentialBadge);
  });
  if (credentials.length > 2) {
    const moreCredentials = document.createElement("span");
    moreCredentials.className = "credential-badge credential-count-badge";
    moreCredentials.textContent = `+${credentials.length - 2} credentials`;
    badges.append(moreCredentials);
  }
  if (job.isRemoteLocationRestricted) {
    const restriction = document.createElement("span");
    restriction.className = "restriction-badge";
    restriction.textContent = "⚠ Location restricted";
    badges.append(restriction);
  }
  const headroom = calculateSalaryHeadroom(job);
  if (headroom?.isLimited) {
    const salaryWarning = document.createElement("span");
    salaryWarning.className = "headroom-badge";
    salaryWarning.textContent = "⚠ Limited salary headroom";
    badges.append(salaryWarning);
  }
  if (badges.childElementCount > 0) {
    button.append(badges);
  }
  button.addEventListener("click", () => {
    if (state.selectedJobId !== job.stableId) {
      state.detailTab = "glance";
    }
    state.selectedJobId = job.stableId;
    markJobViewed(job);
    renderResults();
  });

  const dismissButton = document.createElement("button");
  dismissButton.type = "button";
  dismissButton.className = "job-dismiss-button";
  dismissButton.textContent = isDismissed ? "Restore" : "×";
  dismissButton.setAttribute(
    "aria-label",
    isDismissed
      ? `Restore job ${job.requisitionId || job.stableId}`
      : `Dismiss job ${job.requisitionId || job.stableId}`);
  dismissButton.title = isDismissed ? "Restore this job" : "Hide this job";
  dismissButton.addEventListener("click", () => setJobDismissed(job, !isDismissed));

  card.append(button, dismissButton);
  return card;
}

async function setJobDismissed(job, dismissed) {
  if (dismissed) {
    state.dismissedJobIds.add(job.stableId);
  } else {
    state.dismissedJobIds.delete(job.stableId);
  }
  renderResults();

  try {
    const response = await fetch("/api/history/dismissed", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stableId: job.stableId, dismissed }),
      keepalive: true
    });
    if (!response.ok) {
      throw new Error(`Dismissed-state save returned HTTP ${response.status}.`);
    }
  } catch (error) {
    if (dismissed) {
      state.dismissedJobIds.delete(job.stableId);
    } else {
      state.dismissedJobIds.add(job.stableId);
    }
    renderResults();
    elements.errorBanner.textContent = `Dismissed state could not be saved: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  }
}

async function markJobViewed(job) {
  if (!state.newJobIds.has(job.stableId)) {
    return;
  }

  state.newJobIds.delete(job.stableId);
  renderResults();
  try {
    const response = await fetch("/api/history/viewed", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stableId: job.stableId }),
      keepalive: true
    });
    if (!response.ok) {
      throw new Error(`Viewed-state save returned HTTP ${response.status}.`);
    }
  } catch (error) {
    state.newJobIds.add(job.stableId);
    renderResults();
    elements.errorBanner.textContent = `Viewed state could not be saved: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  }
}

function renderDetail(job) {
  elements.emptyDetail.hidden = Boolean(job);
  elements.jobDetail.hidden = !job;
  if (!job) {
    state.renderedDetailJobId = null;
    elements.detailDescription.replaceChildren();
    return;
  }

  if (state.renderedDetailJobId !== job.stableId) {
    state.renderedDetailJobId = job.stableId;
    state.detailTab = "glance";
    document.querySelector(".detail-pane").scrollTop = 0;
  }
  showDetailTab(state.detailTab);

  // All metadata uses textContent. Only the description fragment is inserted as HTML,
  // and only after a strict DOMPurify allowlist has removed executable content.
  replaceWithHighlightedText(elements.detailTitle, job.title);
  replaceWithHighlightedText(elements.detailRequisition, job.requisitionId);
  elements.detailNewBadge.hidden = !state.newJobIds.has(job.stableId);
  elements.detailHiddenBadge.hidden = !state.dismissedJobIds.has(job.stableId);
  elements.detailDate.textContent = formatLongDate(job.startDate) || job.postedOn || "Unavailable";
  elements.detailPay.textContent = formatPay(job);
  elements.detailTimeType.textContent = job.timeType || "Not specified";
  replaceWithHighlightedText(elements.detailLocation, job.primaryLocation || "Not specified");
  replaceWithHighlightedText(
    elements.detailAdditional,
    job.additionalLocations?.length ? job.additionalLocations.join("; ") : "None listed");

  const hasClearance = Boolean(job.clearanceLevel && job.clearanceLevel !== "noneMentioned");
  const clearanceStatus = evaluateClearanceMatch(job, currentSecurityProfile());
  elements.detailClearanceNote.hidden = !hasClearance;
  elements.detailClearance.textContent = hasClearance
    ? clearanceLevelLabel(job.clearanceLevel)
    : "";
  elements.detailClearanceStatus.textContent = hasClearance
    ? clearanceRequirementLabel(job.clearanceRequirement)
    : "";
  elements.detailUserClearance.textContent = hasClearance ? clearanceStatus.userLabel : "";
  elements.detailClearanceComparison.textContent = hasClearance ? clearanceStatus.summary : "";
  elements.detailClearanceComparison.className = hasClearance
    ? `clearance-profile-status ${clearanceStatus.kind}`
    : "";
  elements.detailPolygraphRow.hidden = !job.polygraphRequired;
  elements.detailPolygraph.textContent = job.polygraphRequired
    ? "Required; verify separately (not inferred from clearance level)"
    : "";
  const hasClearanceEvidence = hasClearance && Boolean(job.clearanceEvidence);
  elements.detailClearanceEvidence.hidden = !hasClearanceEvidence;
  elements.detailClearanceEvidence.open = false;
  elements.detailClearanceNoteText.textContent = hasClearanceEvidence
    ? `“${job.clearanceEvidence}”`
    : "";

  const educationStatus = evaluateEducationMatch(job.academicQualification, currentEducationProfile());
  renderAcademicQualification(job, educationStatus);
  renderCredentials(job);

  elements.detailEducationMismatch.hidden = educationStatus.kind !== "strictMismatch";
  elements.detailEducationMismatchText.textContent = educationStatus.kind === "strictMismatch"
    ? educationStatus.explanation
    : "";
  elements.detailClearanceMismatch.hidden = clearanceStatus.kind !== "strictMismatch";
  elements.detailClearanceMismatchText.textContent = clearanceStatus.kind === "strictMismatch"
    ? clearanceStatus.explanation
    : "";

  elements.detailLocationNote.hidden = !job.isRemoteLocationRestricted;
  elements.detailLocationNoteText.textContent = job.isRemoteLocationRestricted
    ? job.remoteLocationRestrictionSnippet || "The description contains a geographic restriction for remote work."
    : "";

  const headroom = calculateSalaryHeadroom(job);
  elements.detailHeadroomNote.hidden = !headroom?.isLimited;
  elements.detailHeadroomNoteText.textContent = headroom?.isLimited
    ? `Your ${formatCurrency(state.minimumSalary)} minimum is near the top of this job's ` +
      `${formatCurrency(job.payMinimum)} – ${formatCurrency(job.payMaximum)} advertised hiring range. ` +
      `Your minimum is approximately ${Math.round(headroom.position * 100)}% through the posted range; ` +
      `negotiating room may be limited.`
    : "";

  renderQualificationFit(job, educationStatus, clearanceStatus, headroom);

  elements.workdayLink.href = safeHttpUrl(job.workdayUrl) || "#";
  elements.workdayLink.hidden = !safeHttpUrl(job.workdayUrl);

  elements.detailWarning.hidden = !job.detailError;
  elements.detailWarning.textContent = job.detailError
    ? `Full details could not be retrieved for this job: ${job.detailError}`
    : "";
  elements.detailFlags.hidden = !(
    educationStatus.kind === "strictMismatch" ||
    clearanceStatus.kind === "strictMismatch" ||
    job.isRemoteLocationRestricted ||
    headroom?.isLimited ||
    job.detailError);

  if (!job.descriptionHtml) {
    elements.detailDescription.textContent = "The formatted description is unavailable for this job.";
    return;
  }

  const cleanHtml = DOMPurify.sanitize(job.descriptionHtml, {
    ALLOWED_TAGS: ["a", "b", "br", "div", "em", "h1", "h2", "h3", "h4", "i", "li", "ol", "p", "span", "strong", "u", "ul"],
    ALLOWED_ATTR: ["href", "title"],
    ALLOW_DATA_ATTR: false,
    FORBID_TAGS: ["script", "style", "iframe", "object", "embed", "form", "input", "button", "svg", "math"],
    FORBID_ATTR: ["style", "class", "id"]
  });
  elements.detailDescription.innerHTML = cleanHtml;
  secureDescriptionLinks(elements.detailDescription);
  highlightDescriptionText(elements.detailDescription);
}

function renderQualificationFit(job, educationStatus, clearanceStatus, headroom) {
  const blockers = [];
  if (clearanceStatus.kind === "strictMismatch") blockers.push("Clearance mismatch");
  if (educationStatus.kind === "strictMismatch") blockers.push("Education mismatch");

  const notes = [];
  if (job.isRemoteLocationRestricted) notes.push("remote-location condition");
  if (headroom?.isLimited) notes.push("limited salary headroom");
  if (job.detailError) notes.push("incomplete job details");
  if (clearanceStatus.kind === "meetsLevelPolygraphReview") notes.push("polygraph review");
  if (["uncertain", "profileNotConfigured"].includes(clearanceStatus.kind)) {
    notes.push("clearance review");
  }
  if (["uncertain", "specificDegreeUncertain", "profileNotConfigured"].includes(educationStatus.kind)) {
    notes.push("education review");
  }

  elements.detailFitSummary.className = `fit-summary ${blockers.length ? "blocker" : notes.length ? "review" : "compatible"}`;
  elements.detailFitTitle.textContent = blockers.length
    ? `${blockers.length} potential blocker${blockers.length === 1 ? "" : "s"}`
    : "No confirmed strict blockers";

  const parts = [];
  if (blockers.length) parts.push(blockers.join(" · "));
  if (notes.length) parts.push(`${notes.length} item${notes.length === 1 ? "" : "s"} to review`);
  if (!parts.length) parts.push("Based on the configured profile and confidently parsed requirements.");
  elements.detailFitText.textContent = parts.join(" · ");
}

function secureDescriptionLinks(container) {
  container.querySelectorAll("a").forEach(anchor => {
    const companyBaseUrl = companyById(state.companyId)?.publicSiteUrl;
    const safeUrl = safeHttpUrl(anchor.getAttribute("href"), companyBaseUrl);
    if (!safeUrl) {
      anchor.replaceWith(document.createTextNode(anchor.textContent || ""));
      return;
    }
    anchor.href = safeUrl;
    anchor.target = "_blank";
    anchor.rel = "noopener noreferrer";
  });
}

function activeHighlightTerms() {
  if (!state.highlightInclusions) {
    return [];
  }
  return state.inclusions
    .map(term => term.trim())
    .filter(Boolean)
    .sort((left, right) => right.length - left.length);
}

function replaceWithHighlightedText(element, value) {
  element.replaceChildren();
  appendHighlightedText(element, value || "");
}

function appendHighlightedText(element, value) {
  const terms = activeHighlightTerms();
  if (terms.length === 0) {
    element.append(document.createTextNode(value));
    return;
  }

  const matcher = new RegExp(`(${terms.map(escapeRegularExpression).join("|")})`, "giu");
  let cursor = 0;
  for (const match of value.matchAll(matcher)) {
    if (match.index > cursor) {
      element.append(document.createTextNode(value.slice(cursor, match.index)));
    }
    const mark = document.createElement("mark");
    mark.textContent = match[0];
    element.append(mark);
    cursor = match.index + match[0].length;
  }
  if (cursor < value.length) {
    element.append(document.createTextNode(value.slice(cursor)));
  }
}

function highlightDescriptionText(container) {
  const terms = activeHighlightTerms();
  if (terms.length === 0) {
    return;
  }

  const matcher = new RegExp(`(${terms.map(escapeRegularExpression).join("|")})`, "giu");
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  const textNodes = [];
  while (walker.nextNode()) {
    if (walker.currentNode.nodeValue && walker.currentNode.nodeValue.trim()) {
      textNodes.push(walker.currentNode);
    }
  }

  for (const textNode of textNodes) {
    const text = textNode.nodeValue;
    matcher.lastIndex = 0;
    if (!matcher.test(text)) {
      continue;
    }
    matcher.lastIndex = 0;
    const fragment = document.createDocumentFragment();
    let cursor = 0;
    for (const match of text.matchAll(matcher)) {
      if (match.index > cursor) {
        fragment.append(document.createTextNode(text.slice(cursor, match.index)));
      }
      const mark = document.createElement("mark");
      mark.textContent = match[0];
      fragment.append(mark);
      cursor = match.index + match[0].length;
    }
    if (cursor < text.length) {
      fragment.append(document.createTextNode(text.slice(cursor)));
    }
    textNode.replaceWith(fragment);
  }
}

function escapeRegularExpression(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function safeHttpUrl(value, base) {
  if (!value) {
    return null;
  }
  try {
    const url = base ? new URL(value, base) : new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? url.href : null;
  } catch {
    return null;
  }
}

function descriptionToText(html) {
  const documentFragment = new DOMParser().parseFromString(html, "text/html");
  return documentFragment.body.textContent || "";
}

function parseCurrencyInput(value) {
  const normalized = value.replace(/[$,\s]/g, "");
  if (!normalized) {
    return null;
  }
  const amount = Number(normalized);
  return Number.isFinite(amount) && amount >= 0 ? amount : null;
}

function calculateSalaryHeadroom(job) {
  const desired = state.minimumSalary;
  const minimum = job.payMinimum;
  const maximum = job.payMaximum;
  if (desired === null || job.payPeriod !== "annual" ||
      !Number.isFinite(minimum) || !Number.isFinite(maximum)) {
    return null;
  }

  // Jobs above the desired minimum have ample headroom by definition. Jobs below
  // it are removed by the existing salary filter before this informational warning.
  if (desired < minimum || desired > maximum) {
    return null;
  }

  if (maximum <= minimum) {
    const position = desired >= maximum ? 1 : 0;
    return { position, isLimited: position >= HEADROOM_WARNING_THRESHOLD };
  }

  const position = Math.max(0, Math.min(1, (desired - minimum) / (maximum - minimum)));
  return { position, isLimited: position >= HEADROOM_WARNING_THRESHOLD };
}

function clearanceLevelLabel(level) {
  return ({
    publicTrust: "Public Trust",
    secret: "Secret",
    topSecret: "Top Secret",
    topSecretSCI: "TS/SCI",
    other: "Other / unclear level"
  })[level] || "Clearance mentioned";
}

function clearanceProfileLevelLabel(level) {
  return ({
    notSpecified: "Not configured",
    none: "None",
    secret: "Secret",
    topSecret: "Top Secret",
    topSecretSCI: "TS/SCI",
    otherUnknown: "Other / Unknown"
  })[level] || "Not configured";
}

function publicTrustProfileLabel(status) {
  return ({
    unknown: "Public Trust status unknown",
    none: "Not currently held",
    current: "Currently held / active"
  })[status] || "Public Trust status unknown";
}

function clearanceRequirementLabel(requirement) {
  return ({
    activeRequired: "Active/current required",
    mustPossess: "Must already possess",
    obtainAndMaintain: "Must be able to obtain and maintain",
    obtain: "Must be able to obtain",
    maintain: "Must be able to maintain",
    eligible: "Must be eligible / able to qualify",
    publicTrustSuitability: "Public Trust / suitability investigation",
    preferred: "Preferred",
    ambiguous: "Mentioned; requirement unclear"
  })[requirement] || "Mentioned; requirement unclear";
}

function clearanceBadgeLabel(job) {
  const level = clearanceLevelLabel(job.clearanceLevel);
  const suffix = ({
    activeRequired: " — Active",
    mustPossess: " — Possess",
    obtainAndMaintain: " — Obtainable",
    obtain: " — Obtainable",
    maintain: " — Maintain",
    eligible: " — Eligible",
    publicTrustSuitability: " — Suitability",
    preferred: " — Preferred"
  })[job.clearanceRequirement] || "";
  return `${level}${suffix}${job.polygraphRequired ? " + Poly" : ""}`;
}

function academicLevelLabel(level, specificDegree = null) {
  if (level === "doctorate" && specificDegree === "phD") {
    return "Ph.D.";
  }
  return ({
    highSchool: "High School/GED",
    associate: "Associate",
    bachelor: "Bachelor's",
    master: "Master's",
    doctorate: "Doctorate",
    noneSpecified: "None specified"
  })[level] || "Academic qualification";
}

function academicBadgeLabel(academic) {
  const level = academicLevelLabel(academic.minimumLevel, academic.specificDegree);
  const degreeOrExperience = academic.minimumLevel === "noneSpecified"
    ? "Degree/Experience"
    : `${level}/Experience`;
  return ({
    degreeOrExperience,
    degreeWithExperienceSubstitution: "Degree/experience options",
    preferredOnly: `${level} \u2014 Preferred`,
    strictDegree: `${level} \u2014 Required`,
    mentionedUnclear: `${level} \u2014 Mentioned`
  })[academic.requirementType] || level;
}

function renderAcademicQualification(job, educationStatus) {
  const academic = job.academicQualification;
  const hasAcademic = academic && academic.requirementType !== "noDegreeSpecified";
  elements.detailAcademicContent.replaceChildren();
  const rows = document.createElement("dl");
  rows.className = "summary-rows";

  const paths = hasAcademic && Array.isArray(academic.paths) ? academic.paths : [];
  const showPathFields = new Set(paths.map(path =>
    Array.isArray(path.fields) ? path.fields.join("\u001f") : "")).size > 1;
  const acceptedPaths = document.createElement("div");
  const pathsLabel = document.createElement("dt");
  pathsLabel.textContent = hasAcademic ? "Accepted paths" : "Requirement";
  const pathsValue = document.createElement("dd");
  if (paths.length) {
    const list = document.createElement("ul");
    list.className = "qualification-paths";
    paths.forEach(path => {
      const item = document.createElement("li");
      const name = document.createElement("span");
      name.textContent = `${academicLevelLabel(path.level, path.specificDegree)}${academicExperienceLabel(path)}`;
      const status = document.createElement("span");
      status.className = "summary-status requirement";
      status.textContent = academicRequirementLabel(path.requirement);
      item.append(name, status);
      if (showPathFields && Array.isArray(path.fields) && path.fields.length) {
        const fields = document.createElement("small");
        fields.textContent = path.fields.join(", ");
        item.append(fields);
      }
      list.append(item);
    });
    pathsValue.append(list);
  } else {
    pathsValue.textContent = hasAcademic
      ? academicLevelLabel(academic.minimumLevel, academic.specificDegree)
      : "No academic requirement specified";
  }
  acceptedPaths.append(pathsLabel, pathsValue);
  rows.append(acceptedPaths);

  if (hasAcademic && Array.isArray(academic.fields) && academic.fields.length) {
    rows.append(createSummaryRow("Fields", academic.fields.join(", ")));
  }
  rows.append(createSummaryRow("Your education", educationStatus.userLabel));

  const assessment = document.createElement("div");
  const assessmentLabel = document.createElement("dt");
  assessmentLabel.textContent = "Assessment";
  const assessmentValue = document.createElement("dd");
  const assessmentStatus = document.createElement("span");
  assessmentStatus.className = `summary-status education ${educationStatus.kind}`;
  assessmentStatus.textContent = educationStatus.summary;
  const explanation = document.createElement("span");
  explanation.className = "summary-explanation";
  explanation.textContent = educationStatus.explanation;
  assessmentValue.append(assessmentStatus, explanation);
  assessment.append(assessmentLabel, assessmentValue);
  rows.append(assessment);
  elements.detailAcademicContent.append(rows);

  if (hasAcademic) {
    const evidence = [...new Set([
      ...paths.map(path => path.evidence),
      ...(Array.isArray(academic.evidence) ? academic.evidence : [])
    ].filter(Boolean))];
    if (evidence.length) {
      elements.detailAcademicContent.append(createEvidenceDisclosure(evidence));
    }
  }
}

function createSummaryRow(labelText, valueText) {
  const row = document.createElement("div");
  const label = document.createElement("dt");
  label.textContent = labelText;
  const value = document.createElement("dd");
  value.textContent = valueText;
  row.append(label, value);
  return row;
}

function createEvidenceDisclosure(excerpts) {
  const disclosure = document.createElement("details");
  disclosure.className = "evidence-disclosure";
  const summary = document.createElement("summary");
  summary.textContent = excerpts.length === 1 ? "Show evidence" : `Show evidence (${excerpts.length})`;
  disclosure.append(summary);
  excerpts.forEach(excerpt => {
    const evidence = document.createElement("p");
    evidence.textContent = `\u201c${excerpt}\u201d`;
    disclosure.append(evidence);
  });
  return disclosure;
}

function academicExperienceLabel(path) {
  if (!Number.isFinite(path.minimumExperienceYears)) {
    return "";
  }
  const range = Number.isFinite(path.maximumExperienceYears) &&
    path.maximumExperienceYears !== path.minimumExperienceYears
    ? `${path.minimumExperienceYears}\u2013${path.maximumExperienceYears}`
    : `${path.minimumExperienceYears}+`;
  return ` + ${range} yrs`;
}

function academicRequirementLabel(requirement) {
  return ({
    required: "Required",
    minimum: "Minimum",
    preferred: "Preferred",
    desired: "Desired",
    mentioned: "Mentioned; status unclear"
  })[requirement] || "Mentioned; status unclear";
}

function renderCredentials(job) {
  const credentials = Array.isArray(job.credentials) ? job.credentials : [];
  elements.detailCredentials.hidden = credentials.length === 0;
  elements.detailCredentialsList.replaceChildren();

  credentials.forEach((credential, index) => {
    const item = document.createElement("div");
    item.className = "credential-row";
    const name = document.createElement("strong");
    name.textContent = credential.name;
    const status = document.createElement("span");
    status.className = `summary-status credential${credential.requirement === "required" ? " required" : ""}`;
    status.textContent = credentialRequirementLabel(credential);

    const identity = document.createElement("p");
    identity.className = "disclosure-metadata";
    identity.textContent = [
      credential.fullName,
      credential.issuer,
      credentialTypeLabel(credential.type),
      credential.category
    ].filter(Boolean).join(" · ");

    const details = document.createElement("details");
    details.className = "inline-details";
    details.id = `credential-details-${index}`;
    const summary = document.createElement("summary");
    summary.textContent = "Details";
    summary.setAttribute("aria-controls", details.id);
    summary.setAttribute("aria-expanded", "false");
    details.addEventListener("toggle", () => {
      summary.setAttribute("aria-expanded", String(details.open));
    });
    details.append(summary, identity);
    if (credential.evidence) {
      const evidence = document.createElement("p");
      evidence.className = "disclosure-evidence";
      evidence.textContent = `“${credential.evidence}”`;
      details.append(evidence);
    }
    item.append(name, status, details);
    elements.detailCredentialsList.append(item);
  });
}

function credentialRequirementLabel(credential) {
  const labels = [];
  if (credential.postHireAcquisitionAllowed) {
    labels.push("Required; post-hire acquisition allowed");
  } else {
    labels.push(({
      required: "Required",
      preferred: "Preferred",
      desired: "Desired",
      mentioned: "Mentioned; status unclear"
    })[credential.requirement] || "Mentioned; status unclear");
  }
  if (credential.isAlternative) {
    labels.push("alternative accepted");
  }
  if (credential.equivalentAccepted) {
    labels.push("equivalent accepted");
  }
  if (credential.inProgressAccepted) {
    labels.push("in progress accepted");
  }
  return labels.join(" · ");
}

function credentialBadgeLabel(credential) {
  let status = ({
    required: "Required",
    preferred: "Preferred",
    desired: "Desired"
  })[credential.requirement] || "";
  if (credential.postHireAcquisitionAllowed) {
    status = "Required after hire";
  } else if (credential.isAlternative && status) {
    status += " alternative";
  } else if (credential.inProgressAccepted) {
    status = status ? `${status} / in progress` : "In progress accepted";
  }
  return status ? `${credential.name} — ${status}` : credential.name;
}

function credentialTypeLabel(type) {
  return ({
    ProfessionalLicense: "Professional license",
    OtherProfessionalCredential: "Professional credential",
    ProjectManagementCertification: "Project/program-management certification",
    SecurityCertification: "Security certification",
    NetworkingCertification: "Networking certification",
    CloudCertification: "Cloud certification",
    VendorCertification: "Vendor certification",
    Certification: "Certification"
  })[type] || "Professional credential";
}

function formatCurrency(value) {
  if (!Number.isFinite(value)) {
    return "Unknown";
  }
  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: 0
  }).format(value);
}

function formatPay(job) {
  if (job.payMinimum === null || job.payMaximum === null) {
    return "Unknown";
  }
  const period = job.payPeriod === "hourly" ? " per hour" :
    job.payPeriod === "annual" ? " annually" : " (period unknown)";
  return `${formatCurrency(job.payMinimum)} – ${formatCurrency(job.payMaximum)}${period}`;
}

function formatShortDate(isoDate) {
  const date = parseIsoDate(isoDate);
  return date ? new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" }).format(date) : "";
}

function formatLongDate(isoDate) {
  const date = parseIsoDate(isoDate);
  return date
    ? new Intl.DateTimeFormat(undefined, { year: "numeric", month: "long", day: "numeric" }).format(date)
    : "";
}

function parseIsoDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value || "")) {
    return null;
  }
  const [year, month, day] = value.split("-").map(Number);
  return new Date(year, month - 1, day);
}

function updateLastRefreshed() {
  if (!state.lastRefreshedUtc) {
    elements.lastRefreshed.textContent = "No successful refresh yet";
    return;
  }
  const refreshed = new Date(state.lastRefreshedUtc);
  elements.lastRefreshed.textContent = `Last refreshed ${refreshed.toLocaleString()}`;
}

function setLoading(isLoading, options = {}) {
  const wasLoading = state.isRefreshing;
  state.isRefreshing = isLoading;
  elements.refreshButton.disabled = isLoading;
  elements.refreshButton.textContent = isLoading ? "Refreshing…" : "Refresh";
  elements.appShell.setAttribute("aria-busy", String(isLoading));

  if (isLoading) {
    clearTimeout(state.overlayHideTimer);
    if (options.title) state.loadingTitle = options.title;
    elements.loadingTitle.textContent = state.loadingTitle;
    updateLoadingProgress(options.progress, options.phaseText);
    elements.loadingNote.textContent = state.jobs.length > 0
      ? "Existing results are visible in the background, but refresh is still in progress."
      : "This may take a moment.";
    if (!wasLoading && document.activeElement !== document.body) {
      state.focusBeforeLoading = document.activeElement;
    }
    elements.appShell.inert = true;
    elements.loadingOverlay.hidden = false;
    elements.loadingOverlay.setAttribute("aria-hidden", "false");
    requestAnimationFrame(() => {
      elements.loadingOverlay.classList.add("visible");
      elements.loadingOverlay.focus({ preventScroll: true });
    });
  } else {
    clearTimeout(state.refreshProgressTimer);
    elements.appShell.inert = state.sourceConfirmationOpen;
    elements.loadingOverlay.classList.remove("visible");
    elements.loadingOverlay.setAttribute("aria-hidden", "true");
    state.overlayHideTimer = setTimeout(() => {
      if (!state.isRefreshing) elements.loadingOverlay.hidden = true;
    }, OVERLAY_TRANSITION_MS);
    if (wasLoading && state.focusBeforeLoading?.isConnected) {
      state.focusBeforeLoading.focus({ preventScroll: true });
    }
    state.focusBeforeLoading = null;
    state.loadingTitle = `Loading ${state.companyName} jobs`;
  }
  updateQueryControls();
}

function updateLoadingProgress(progress, explicitText) {
  if (explicitText) {
    elements.loadingPhase.textContent = explicitText;
    return;
  }

  const completed = Number(progress?.completed || 0);
  const total = Number(progress?.total || 0);
  switch (progress?.phase) {
    case "listings":
      elements.loadingPhase.textContent = total > 0
        ? `Retrieving Workday listings (${completed} of ${total} found)…`
        : "Retrieving Workday listing pages…";
      break;
    case "details":
      elements.loadingPhase.textContent = total > 0
        ? `Fetching job details (${completed} of ${total})…`
        : "Fetching job details…";
      break;
    case "finalizing":
      elements.loadingPhase.textContent = "Analyzing and sorting jobs…";
      break;
    case "saving":
      elements.loadingPhase.textContent = "Saving refreshed results…";
      break;
    default:
      elements.loadingPhase.textContent = "Retrieving Workday listings and exact posting dates…";
      break;
  }
}

function beginRefreshProgressPolling() {
  clearTimeout(state.refreshProgressTimer);
  const poll = async () => {
    if (!state.isRefreshing) return;
    try {
      const response = await fetch("/api/jobs", { cache: "no-store" });
      if (response.ok) {
        const snapshot = await response.json();
        if (snapshot.isRefreshing) updateLoadingProgress(snapshot.refreshProgress);
      }
    } catch {
      // The foreground request owns error reporting; progress polling is best effort.
    }
    if (state.isRefreshing) {
      state.refreshProgressTimer = setTimeout(poll, 400);
    }
  };
  state.refreshProgressTimer = setTimeout(poll, 100);
}

function constrainLoadingFocus(event) {
  if (event.key === "Tab") {
    event.preventDefault();
    elements.loadingOverlay.focus({ preventScroll: true });
  }
}

function constrainSourceConfirmationFocus(event) {
  if (event.key === "Escape") {
    event.preventDefault();
    closeSourceConfirmation(true);
    return;
  }
  if (event.key !== "Tab") return;
  const focusable = [elements.sourceConfirmationStay, elements.sourceConfirmationApply];
  const currentIndex = focusable.indexOf(document.activeElement);
  const nextIndex = event.shiftKey
    ? (currentIndex <= 0 ? focusable.length - 1 : currentIndex - 1)
    : (currentIndex < 0 || currentIndex === focusable.length - 1 ? 0 : currentIndex + 1);
  event.preventDefault();
  focusable[nextIndex].focus({ preventScroll: true });
}

function showSnapshotError(error, detailFailureCount) {
  const messages = [];
  if (error) {
    messages.push(`Refresh failed: ${error}`);
  }
  if (detailFailureCount > 0) {
    messages.push(`${detailFailureCount} job detail request${detailFailureCount === 1 ? "" : "s"} failed; listing metadata was retained.`);
  }
  elements.errorBanner.textContent = messages.join(" ");
  elements.errorBanner.hidden = messages.length === 0;
}

function showClientError(error) {
  console.error(error);
  elements.errorBanner.textContent = `The application request failed: ${error.message || error}`;
  elements.errorBanner.hidden = false;
  setLoading(false);
}

async function apiErrorMessage(response, fallback) {
  if (!response) return fallback;
  try {
    const payload = await response.clone().json();
    if (typeof payload?.error === "string" && payload.error.trim()) {
      return payload.error.trim();
    }
  } catch {
    // A non-JSON server/proxy response still receives a useful status fallback.
  }
  return `${fallback} (HTTP ${response.status})`;
}
