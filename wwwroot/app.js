"use strict";

const HEADROOM_WARNING_THRESHOLD = 0.75;
const SETTINGS_SAVE_DEBOUNCE_MS = 400;
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
  lastObservedAutomaticRefreshUtc: null,
  newJobIds: new Set(),
  dismissedJobIds: new Set(),
  showDismissedJobs: false,
  selectedJobId: null,
  lastRefreshedUtc: null,
  isCached: false,
  isRefreshing: false,
  facetsLoaded: false,
  country: { id: null, label: ALL_COUNTRIES_LABEL },
  location: { id: null, label: ALL_LOCATIONS_LABEL },
  pollTimer: null,
  settingsSaveTimer: null
};

const elements = {
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
  countrySelect: document.querySelector("#country-select"),
  locationSelect: document.querySelector("#location-select"),
  applyLocation: document.querySelector("#apply-location"),
  facetStatus: document.querySelector("#facet-status"),
  highlightInclusions: document.querySelector("#highlight-inclusions"),
  automaticCheckEnabled: document.querySelector("#automatic-check-enabled"),
  automaticCheckInterval: document.querySelector("#automatic-check-interval"),
  automaticCheckStatus: document.querySelector("#automatic-check-status"),
  themeMode: document.querySelector("#theme-mode"),
  resultCount: document.querySelector("#result-count"),
  errorBanner: document.querySelector("#error-banner"),
  loadingBanner: document.querySelector("#loading-banner"),
  cacheBanner: document.querySelector("#cache-banner"),
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
  detailClearanceRow: document.querySelector("#detail-clearance-row"),
  detailClearance: document.querySelector("#detail-clearance"),
  detailClearanceStatusRow: document.querySelector("#detail-clearance-status-row"),
  detailClearanceStatus: document.querySelector("#detail-clearance-status"),
  detailPolygraphRow: document.querySelector("#detail-polygraph-row"),
  detailPolygraph: document.querySelector("#detail-polygraph"),
  detailLocationNote: document.querySelector("#detail-location-note"),
  detailLocationNoteText: document.querySelector("#detail-location-note-text"),
  detailHeadroomNote: document.querySelector("#detail-headroom-note"),
  detailHeadroomNoteText: document.querySelector("#detail-headroom-note-text"),
  detailClearanceNote: document.querySelector("#detail-clearance-note"),
  detailClearanceNoteText: document.querySelector("#detail-clearance-note-text"),
  detailCredentials: document.querySelector("#detail-credentials"),
  detailCredentialsList: document.querySelector("#detail-credentials-list"),
  detailWarning: document.querySelector("#detail-warning"),
  detailDescription: document.querySelector("#detail-description"),
  workdayLink: document.querySelector("#workday-link")
};

document.addEventListener("DOMContentLoaded", initialize);

async function initialize() {
  wireKeywordInput("inclusions", elements.includeInput, elements.addInclusion);
  wireKeywordInput("exclusions", elements.excludeInput, elements.addExclusion);
  elements.refreshButton.addEventListener("click", refreshJobs);
  elements.filterToggle.addEventListener("click", toggleSearchFilters);
  elements.countrySelect.addEventListener("change", countrySelectionChanged);
  elements.locationSelect.addEventListener("change", updateQueryControls);
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
  elements.workdayLink.addEventListener("click", () => {
    const job = state.jobs.find(item => item.stableId === state.selectedJobId);
    if (job) {
      markJobViewed(job);
    }
  });

  await loadInitialState();
}

async function loadInitialState() {
  try {
    const [settingsResponse, jobsResponse] = await Promise.all([
      fetch("/api/settings", { cache: "no-store" }),
      fetch("/api/jobs", { cache: "no-store" })
    ]);
    if (!settingsResponse.ok || !jobsResponse.ok) {
      throw new Error(`Local API returned HTTP ${settingsResponse.status}/${jobsResponse.status}.`);
    }
    applySettings(await settingsResponse.json());
    applySnapshot(await jobsResponse.json());
    await loadLocationFacets(state.country.id, state.location.id);
    await loadAutomaticCheckStatus();
    window.setInterval(loadAutomaticCheckStatus, 30000);
  } catch (error) {
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
  state.country = normalizeFacetSelection(settings.country, ALL_COUNTRIES_LABEL);
  state.location = normalizeFacetSelection(settings.location, ALL_LOCATIONS_LABEL);

  elements.minimumPay.value = state.minimumSalary === null
    ? ""
    : new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(state.minimumSalary);
  elements.highlightInclusions.checked = state.highlightInclusions;
  elements.automaticCheckEnabled.checked = state.automaticCheckEnabled;
  elements.automaticCheckInterval.value = String(state.automaticCheckIntervalMinutes);
  elements.automaticCheckInterval.disabled = !state.automaticCheckEnabled;
  elements.themeMode.value = state.themeMode;
  applyTheme();
  const scopeRadio = document.querySelector(`input[name="scope"][value="${state.scope}"]`);
  const locationRadio = document.querySelector(`input[name="location-mode"][value="${state.locationMode}"]`);
  if (scopeRadio) scopeRadio.checked = true;
  if (locationRadio) locationRadio.checked = true;
  renderChips("inclusions");
  renderChips("exclusions");
  updateSearchFilterUi();
}

function applyTheme() {
  document.documentElement.dataset.theme = state.themeMode;
  try {
    localStorage.setItem("leidos-jobs-theme-hint", state.themeMode);
  } catch {
    // The persisted backend setting is authoritative; this cache is optional.
  }
}

async function loadAutomaticCheckStatus() {
  try {
    const response = await fetch("/api/automatic-check/status", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Automatic-check status returned HTTP ${response.status}.`);
    }
    const status = await response.json();
    renderAutomaticCheckStatus(status);

    const automaticRefreshUtc = status.lastAutomaticRefreshUtc || null;
    if (state.lastObservedAutomaticRefreshUtc &&
        automaticRefreshUtc &&
        automaticRefreshUtc !== state.lastObservedAutomaticRefreshUtc) {
      await loadAutomaticSnapshotPreservingUi();
    }
    state.lastObservedAutomaticRefreshUtc = automaticRefreshUtc;
  } catch (error) {
    console.warn("Automatic-check status is temporarily unavailable.", error);
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

  const country = state.country.label || ALL_COUNTRIES_LABEL;
  const location = state.location.label || ALL_LOCATIONS_LABEL;
  summary.push(`${country} / ${location}`);
  if (state.locationMode !== "all") {
    summary.push(state.locationMode === "hide-restricted"
      ? "Restricted remote hidden"
      : "Restricted remote only");
  }
  return summary.join(" · ");
}

function normalizeFacetSelection(selection, allLabel) {
  return selection?.id
    ? { id: selection.id, label: selection.label || selection.id }
    : { id: null, label: allLabel };
}

async function loadLocationFacets(countryId, locationId = null) {
  state.facetsLoaded = false;
  let loaded = false;
  updateQueryControls();
  elements.facetStatus.textContent = "Loading Workday location choices…";
  try {
    const parameters = new URLSearchParams();
    if (countryId) parameters.set("countryId", countryId);
    const response = await fetch(`/api/location-facets?${parameters}`, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Location facets returned HTTP ${response.status}.`);
    }
    const facets = await response.json();
    populateFacetSelect(
      elements.countrySelect,
      facets.countries || [],
      ALL_COUNTRIES_LABEL,
      countryId);
    const selectedLocation = populateFacetSelect(
      elements.locationSelect,
      facets.locations || [],
      ALL_LOCATIONS_LABEL,
      locationId);
    if (!selectedLocation) {
      elements.locationSelect.value = "";
    }
    elements.facetStatus.textContent =
      `${new Intl.NumberFormat().format(facets.matchingJobs || 0)} jobs match this facet context.`;
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

function populateFacetSelect(select, options, allLabel, selectedId) {
  select.replaceChildren();
  const all = document.createElement("option");
  all.value = "";
  all.dataset.label = allLabel;
  all.textContent = allLabel;
  select.append(all);

  for (const item of options) {
    const option = document.createElement("option");
    option.value = item.id;
    option.dataset.label = item.label;
    option.textContent = `${item.label} (${new Intl.NumberFormat().format(item.count)})`;
    select.append(option);
  }

  const exists = Boolean(selectedId) && options.some(option => option.id === selectedId);
  select.value = exists ? selectedId : "";
  return exists;
}

async function countrySelectionChanged() {
  const countryId = elements.countrySelect.value || null;
  await loadLocationFacets(countryId, null);
  updateQueryControls();
}

function selectedFacet(select, allLabel) {
  const option = select.selectedOptions[0];
  return {
    id: option?.value || null,
    label: option?.dataset.label || allLabel
  };
}

function querySelectionIsPending() {
  return (elements.countrySelect.value || null) !== state.country.id ||
    (elements.locationSelect.value || null) !== state.location.id;
}

function updateQueryControls() {
  const disabled = !state.facetsLoaded || state.isRefreshing;
  elements.countrySelect.disabled = disabled;
  elements.locationSelect.disabled = disabled;
  elements.applyLocation.disabled = disabled || !querySelectionIsPending();
}

async function applyWorkdayLocation() {
  const country = selectedFacet(elements.countrySelect, ALL_COUNTRIES_LABEL);
  const location = selectedFacet(elements.locationSelect, ALL_LOCATIONS_LABEL);
  clearTimeout(state.pollTimer);
  setLoading(true);
  elements.errorBanner.hidden = true;
  state.jobs = [];
  state.newJobIds = new Set();
  state.selectedJobId = null;
  renderResults();
  try {
    const response = await fetch("/api/query", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        countryId: country.id,
        countryLabel: country.label,
        locationId: location.id,
        locationLabel: location.label
      })
    });
    if (!response.ok) {
      throw new Error(`Location refresh returned HTTP ${response.status}.`);
    }
    state.country = country;
    state.location = location;
    applySnapshot(await response.json());
    await loadAutomaticCheckStatus();
  } catch (error) {
    showClientError(error);
    await loadSnapshot();
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
  setLoading(true);
  elements.errorBanner.hidden = true;
  try {
    const response = await fetch("/api/refresh", { method: "POST", cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Refresh returned HTTP ${response.status}.`);
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
    state.country = {
      id: snapshot.query.countryId || null,
      label: snapshot.query.countryLabel || ALL_COUNTRIES_LABEL
    };
    state.location = {
      id: snapshot.query.locationId || null,
      label: snapshot.query.locationLabel || ALL_LOCATIONS_LABEL
    };
  }

  setLoading(Boolean(snapshot.isRefreshing));
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
  if (!usingCache) {
    elements.cacheBanner.hidden = true;
    elements.cacheBanner.textContent = "";
    return;
  }

  const refreshed = snapshot.lastRefreshedUtc
    ? new Date(snapshot.lastRefreshedUtc).toLocaleString()
    : "an earlier run";
  elements.cacheBanner.textContent = snapshot.error
    ? `Showing cached jobs from ${refreshed}; the live refresh failed.`
    : `Showing cached jobs from ${refreshed} while the live refresh runs.`;
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
        country: state.country,
        location: state.location,
        searchFiltersCollapsed: state.searchFiltersCollapsed,
        automaticCheckEnabled: state.automaticCheckEnabled,
        automaticCheckIntervalMinutes: state.automaticCheckIntervalMinutes,
        themeMode: state.themeMode
      })
    });
    if (!response.ok) {
      throw new Error(`Settings save returned HTTP ${response.status}.`);
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

    return passesInclusion && passesExclusion && passesSalary && passesLocation;
  });
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

  if (!jobs.some(job => job.stableId === state.selectedJobId)) {
    // Automatic selection is presentation only and deliberately does not mark NEW as viewed.
    state.selectedJobId = jobs[0]?.stableId ?? null;
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
    elements.detailDescription.replaceChildren();
    return;
  }

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
  elements.detailClearanceRow.hidden = !hasClearance;
  elements.detailClearanceStatusRow.hidden = !hasClearance;
  elements.detailClearance.textContent = hasClearance
    ? clearanceLevelLabel(job.clearanceLevel)
    : "";
  elements.detailClearanceStatus.textContent = hasClearance
    ? clearanceRequirementLabel(job.clearanceRequirement)
    : "";
  elements.detailPolygraphRow.hidden = !job.polygraphRequired;
  elements.detailPolygraph.textContent = job.polygraphRequired ? "Required" : "";
  elements.detailClearanceNote.hidden = !hasClearance || !job.clearanceEvidence;
  elements.detailClearanceNoteText.textContent = hasClearance && job.clearanceEvidence
    ? `“${job.clearanceEvidence}”`
    : "";

  renderCredentials(job);

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

  elements.workdayLink.href = safeHttpUrl(job.workdayUrl) || "#";
  elements.workdayLink.hidden = !safeHttpUrl(job.workdayUrl);

  elements.detailWarning.hidden = !job.detailError;
  elements.detailWarning.textContent = job.detailError
    ? `Full details could not be retrieved for this job: ${job.detailError}`
    : "";

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

function secureDescriptionLinks(container) {
  container.querySelectorAll("a").forEach(anchor => {
    const safeUrl = safeHttpUrl(anchor.getAttribute("href"), "https://leidos.wd5.myworkdayjobs.com/");
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

function renderCredentials(job) {
  const credentials = Array.isArray(job.credentials) ? job.credentials : [];
  elements.detailCredentials.hidden = credentials.length === 0;
  elements.detailCredentialsList.replaceChildren();

  credentials.forEach(credential => {
    const item = document.createElement("article");
    item.className = "credential-item";

    const heading = document.createElement("div");
    heading.className = "credential-item-heading";
    const name = document.createElement("strong");
    name.textContent = credential.name;
    const status = document.createElement("span");
    status.className = `credential-status${credential.requirement === "required" ? " required" : ""}`;
    status.textContent = credentialRequirementLabel(credential);
    heading.append(name, status);

    const identity = document.createElement("p");
    identity.className = "credential-identity";
    identity.textContent = [
      credential.fullName,
      credential.issuer,
      credentialTypeLabel(credential.type),
      credential.category
    ].filter(Boolean).join(" · ");

    item.append(heading, identity);
    if (credential.evidence) {
      const evidence = document.createElement("p");
      evidence.className = "credential-evidence";
      evidence.textContent = `“${credential.evidence}”`;
      item.append(evidence);
    }
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

function setLoading(isLoading) {
  state.isRefreshing = isLoading;
  elements.loadingBanner.hidden = !isLoading;
  elements.refreshButton.disabled = isLoading;
  elements.refreshButton.textContent = isLoading ? "Refreshing…" : "Refresh";
  updateQueryControls();
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
  elements.errorBanner.textContent = `The local application could not be reached: ${error.message || error}`;
  elements.errorBanner.hidden = false;
  setLoading(false);
}
