"use strict";

const HEADROOM_WARNING_THRESHOLD = 0.75;
const SETTINGS_SAVE_DEBOUNCE_MS = 400;
const OVERLAY_TRANSITION_MS = 180;
const COPY_FEEDBACK_MS = 2000;
const REFRESH_STATUS_POLL_MS = 1000;
const ALL_COUNTRIES_LABEL = "All countries";
const ALL_LOCATIONS_LABEL = "All locations";
const SUPPORTED_THEME_MODES = new Set([
  "light",
  "dark",
  "nord-polar-night",
  "nord-snow-storm",
  "dracula"
]);

function normalizeThemeMode(value) {
  return SUPPORTED_THEME_MODES.has(value) ? value : "light";
}
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
  activeSettingsTab: "job-search",
  account: null,
  workspaceIdentity: null,
  accountLinkToken: null,
  adminStatusLoaded: false,
  activeQualificationTab: "basics",
  jobs: [],
  inclusions: [],
  exclusions: [],
  scope: "metadata",
  minimumSalary: null,
  locationMode: "all",
  highlightInclusions: true,
  collapsedAgeGroups: {},
  searchFiltersCollapsed: false,
  themeMode: "light",
  educationLevel: "notSpecified",
  doctorateType: null,
  hideStrictEducationMismatch: false,
  clearanceProfileLevel: "notSpecified",
  publicTrustProfile: "unknown",
  hideStrictClearanceMismatch: false,
  usWorkAuthorizationStatus: "notSpecified",
  sponsorshipProfile: "unknown",
  hideStrictWorkAuthorizationMismatch: false,
  excludeStrongExtendedLocationRequirements: false,
  jobFitEnabled: false,
  jobFitSignals: [],
  jobFitConcepts: [],
  jobFitConceptSearch: "",
  credentialOptions: [],
  credentialInventoryStatus: "notConfigured",
  heldCredentialIds: new Set(),
  credentialSearch: "",
  detailTab: "glance",
  renderedDetailJobId: null,
  newJobIds: new Set(),
  jobStates: new Map(),
  jobClosures: new Map(),
  activeResultsTab: "all",
  selectedJobId: null,
  detailLoadingIds: new Set(),
  descriptionMatches: new Map(),
  descriptionMatchGeneration: 0,
  lastRefreshedUtc: null,
  isCached: false,
  isRefreshing: false,
  isInitializing: true,
  catalogIsRefreshing: false,
  facetsLoaded: false,
  companies: [],
  hasConfiguredSource: false,
  pendingImportedSource: null,
  companyId: "",
  companyName: "Job source",
  country: { id: null, label: ALL_COUNTRIES_LABEL },
  includeAllLocations: false,
  includeRemote: true,
  physicalLocations: [],
  locationGroups: [],
  remoteLocations: [],
  facetMatchingJobs: 0,
  refreshProgressTimer: null,
  refreshStatusRequestInFlight: false,
  refreshPollingGeneration: 0,
  overlayHideTimer: null,
  copyFeedbackTimer: null,
  focusBeforeLoading: null,
  sourceMetadataLoading: false,
  sourceRequestGeneration: 0,
  sourceAbortController: null,
  sourceOverlayShowTimer: null,
  sourceOverlayHideTimer: null,
  focusBeforeSourceLoading: null,
  sourceFacetCache: new Map(),
  sourceConfirmationOpen: false,
  sourceConfirmationMode: null,
  sourceConfirmationHideTimer: null,
  focusBeforeSourceConfirmation: null,
  resetConfirmationOpen: false,
  resetConfirmationHideTimer: null,
  focusBeforeResetConfirmation: null,
  resetInProgress: false,
  closeApplicationOpen: false,
  closeApplicationHideTimer: null,
  closeApplicationJobId: null,
  closeApplicationInProgress: false,
  focusBeforeCloseApplication: null,
  loadingTitle: "Loading jobs",
  settingsSaveTimer: null
};

const elements = {
  jobsTab: document.querySelector("#jobs-tab"),
  settingsTab: document.querySelector("#settings-tab"),
  adminTab: null,
  jobsView: document.querySelector("#jobs-view"),
  settingsView: document.querySelector("#settings-view"),
  adminView: null,
  adminStatus: null,
  jobSearchSettingsTab: document.querySelector("#job-search-settings-tab"),
  qualificationsSettingsTab: document.querySelector("#qualifications-settings-tab"),
  preferencesSettingsTab: document.querySelector("#preferences-settings-tab"),
  jobFitSettingsTab: document.querySelector("#job-fit-settings-tab"),
  accountSettingsTab: document.querySelector("#account-settings-tab"),
  jobSearchSettingsPanel: document.querySelector("#job-search-settings-panel"),
  qualificationsSettingsPanel: document.querySelector("#qualifications-settings-panel"),
  preferencesSettingsPanel: document.querySelector("#preferences-settings-panel"),
  jobFitSettingsPanel: document.querySelector("#job-fit-settings-panel"),
  accountSettingsPanel: document.querySelector("#account-settings-panel"),
  anonymousAccountSection: document.querySelector("#anonymous-account-section"),
  createAccountSection: document.querySelector("#create-account-section"),
  loginAccountSection: document.querySelector("#login-account-section"),
  forgotPasswordSection: document.querySelector("#forgot-password-section"),
  resetPasswordSection: document.querySelector("#reset-password-section"),
  authenticatedAccountSection: document.querySelector("#authenticated-account-section"),
  administratorBootstrapSection: document.querySelector("#administrator-bootstrap-section"),
  administratorBootstrapForm: document.querySelector("#administrator-bootstrap-form"),
  administratorBootstrapCode: document.querySelector("#administrator-bootstrap-code"),
  administratorBootstrapStatus: document.querySelector("#administrator-bootstrap-status"),
  changePasswordSection: document.querySelector("#change-password-section"),
  createAccountForm: document.querySelector("#create-account-form"),
  createAccountEmail: document.querySelector("#create-account-email"),
  createAccountPassword: document.querySelector("#create-account-password"),
  createAccountConfirm: document.querySelector("#create-account-confirm"),
  createAccountPersistence: document.querySelector("#create-account-persistence"),
  createAccountStatus: document.querySelector("#create-account-status"),
  loginAccountForm: document.querySelector("#login-account-form"),
  loginAccountEmail: document.querySelector("#login-account-email"),
  loginAccountPassword: document.querySelector("#login-account-password"),
  loginAccountPersistence: document.querySelector("#login-account-persistence"),
  loginAccountStatus: document.querySelector("#login-account-status"),
  forgotPasswordForm: document.querySelector("#forgot-password-form"),
  forgotPasswordEmail: document.querySelector("#forgot-password-email"),
  forgotPasswordStatus: document.querySelector("#forgot-password-status"),
  resetPasswordForm: document.querySelector("#reset-password-form"),
  resetAccountPassword: document.querySelector("#reset-account-password"),
  resetAccountConfirm: document.querySelector("#reset-account-confirm"),
  resetPasswordStatus: document.querySelector("#reset-password-status"),
  changePasswordForm: document.querySelector("#change-password-form"),
  currentAccountPassword: document.querySelector("#current-account-password"),
  newAccountPassword: document.querySelector("#new-account-password"),
  newAccountConfirm: document.querySelector("#new-account-confirm"),
  changePasswordStatus: document.querySelector("#change-password-status"),
  accountEmail: document.querySelector("#account-email"),
  accountEmailStatus: document.querySelector("#account-email-status"),
  accountWorkspaceId: document.querySelector("#account-workspace-id"),
  anonymousAccountWorkspaceId: document.querySelector("#anonymous-account-workspace-id"),
  accountSessionPersistence: document.querySelector("#account-session-persistence"),
  authenticatedAccountStatus: document.querySelector("#authenticated-account-status"),
  emailVerificationActions: document.querySelector("#email-verification-actions"),
  accountEmailConfigurationNote: document.querySelector("#account-email-configuration-note"),
  showCreateAccount: document.querySelector("#show-create-account"),
  showLogin: document.querySelector("#show-login"),
  showForgotPassword: document.querySelector("#show-forgot-password"),
  showChangePassword: document.querySelector("#show-change-password"),
  cancelCreateAccount: document.querySelector("#cancel-create-account"),
  cancelLogin: document.querySelector("#cancel-login"),
  cancelForgotPassword: document.querySelector("#cancel-forgot-password"),
  cancelResetPassword: document.querySelector("#cancel-reset-password"),
  cancelChangePassword: document.querySelector("#cancel-change-password"),
  requestEmailVerification: document.querySelector("#request-email-verification"),
  signOutAccount: document.querySelector("#sign-out-account"),
  qualificationBasicsTab: document.querySelector("#qualification-basics-tab"),
  qualificationCredentialsTab: document.querySelector("#qualification-credentials-tab"),
  qualificationBasicsPanel: document.querySelector("#qualification-basics-panel"),
  qualificationCredentialsPanel: document.querySelector("#qualification-credentials-panel"),
  sourceSettingsLink: document.querySelector("#source-settings-link"),
  sourceSummary: document.querySelector("#source-summary"),
  refreshButton: document.querySelector("#refresh-button"),
  lastRefreshed: document.querySelector("#last-refreshed"),
  cacheStatus: document.querySelector("#cache-status"),
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
  applySourceSection: document.querySelector("#apply-source-section"),
  facetStatus: document.querySelector("#facet-status"),
  highlightInclusions: document.querySelector("#highlight-inclusions"),
  themeMode: document.querySelector("#theme-mode"),
  educationLevel: document.querySelector("#education-level"),
  doctorateTypeField: document.querySelector("#doctorate-type-field"),
  doctorateType: document.querySelector("#doctorate-type"),
  hideStrictEducationMismatch: document.querySelector("#hide-strict-education-mismatch"),
  clearanceProfileLevel: document.querySelector("#clearance-profile-level"),
  publicTrustProfile: document.querySelector("#public-trust-profile"),
  hideStrictClearanceMismatch: document.querySelector("#hide-strict-clearance-mismatch"),
  usWorkAuthorizationStatus: document.querySelector("#us-work-authorization-status"),
  sponsorshipProfile: document.querySelector("#sponsorship-profile"),
  hideStrictWorkAuthorizationMismatch: document.querySelector("#hide-strict-work-authorization-mismatch"),
  excludeStrongExtendedLocationRequirements: document.querySelector("#exclude-strong-extended-location-requirements"),
  jobFitEnabled: document.querySelector("#job-fit-enabled"),
  jobFitConfiguration: document.querySelector("#job-fit-configuration"),
  jobFitConceptSearch: document.querySelector("#job-fit-concept-search"),
  jobFitSurveyStatus: document.querySelector("#job-fit-survey-status"),
  jobFitSurvey: document.querySelector("#job-fit-survey"),
  credentialInventoryStatus: document.querySelector("#credential-inventory-status"),
  heldCredentialsField: document.querySelector("#held-credentials-field"),
  heldCredentials: document.querySelector("#held-credentials"),
  credentialSearch: document.querySelector("#credential-search"),
  credentialSelectionSummary: document.querySelector("#credential-selection-summary"),
  resultCount: document.querySelector("#result-count"),
  appShell: document.querySelector("#app-shell"),
  errorBanner: document.querySelector("#error-banner"),
  cacheBanner: document.querySelector("#cache-banner"),
  loadingOverlay: document.querySelector("#loading-overlay"),
  loadingTitle: document.querySelector("#loading-title"),
  loadingPhase: document.querySelector("#loading-phase"),
  loadingNote: document.querySelector("#loading-note"),
  sourceLoadingOverlay: document.querySelector("#source-loading-overlay"),
  sourceLoadingTitle: document.querySelector("#source-loading-title"),
  sourceLoadingPhase: document.querySelector("#source-loading-phase"),
  sourceConfirmationOverlay: document.querySelector("#source-confirmation-overlay"),
  sourceConfirmationTitle: document.querySelector("#source-confirmation-title"),
  sourceConfirmationCopy: document.querySelector("#source-confirmation-copy"),
  sourceConfirmationComparison: document.querySelector("#source-confirmation-comparison"),
  sourceConfirmationCurrent: document.querySelector("#source-confirmation-current"),
  sourceConfirmationPending: document.querySelector("#source-confirmation-pending"),
  sourceConfirmationQuestion: document.querySelector("#source-confirmation-question"),
  sourceConfirmationStay: document.querySelector("#source-confirmation-stay"),
  sourceConfirmationDiscard: document.querySelector("#source-confirmation-discard"),
  sourceConfirmationApply: document.querySelector("#source-confirmation-apply"),
  workspaceId: document.querySelector("#workspace-id"),
  copyWorkspaceIdButton: document.querySelector("#copy-workspace-id-button"),
  workspaceIdStatus: document.querySelector("#workspace-id-status"),
  resetWorkspaceButton: document.querySelector("#reset-workspace-button"),
  resetConfirmationOverlay: document.querySelector("#reset-confirmation-overlay"),
  resetConfirmationCancel: document.querySelector("#reset-confirmation-cancel"),
  resetConfirmationSubmit: document.querySelector("#reset-confirmation-submit"),
  resetConfirmationError: document.querySelector("#reset-confirmation-error"),
  closeApplicationOverlay: document.querySelector("#close-application-overlay"),
  closeApplicationCopy: document.querySelector("#close-application-copy"),
  closeApplicationReason: document.querySelector("#close-application-reason"),
  closeApplicationCancel: document.querySelector("#close-application-cancel"),
  closeApplicationSubmit: document.querySelector("#close-application-submit"),
  closeApplicationError: document.querySelector("#close-application-error"),
  importWorkspaceButton: document.querySelector("#import-workspace-button"),
  exportWorkspaceButton: document.querySelector("#export-workspace-button"),
  importWorkspaceFile: document.querySelector("#import-workspace-file"),
  portableWorkspaceStatus: document.querySelector("#portable-workspace-status"),
  hiddenJobCount: document.querySelector("#hidden-job-count"),
  allResultsTab: document.querySelector("#all-results-tab"),
  allJobCount: document.querySelector("#all-job-count"),
  savedResultsTab: document.querySelector("#saved-results-tab"),
  appliedResultsTab: document.querySelector("#applied-results-tab"),
  closedResultsTab: document.querySelector("#closed-results-tab"),
  hiddenResultsTab: document.querySelector("#hidden-results-tab"),
  savedJobCount: document.querySelector("#saved-job-count"),
  appliedJobCount: document.querySelector("#applied-job-count"),
  closedJobCount: document.querySelector("#closed-job-count"),
  resultsTabPanel: document.querySelector("#results-tab-panel"),
  jobList: document.querySelector("#job-list"),
  emptyDetail: document.querySelector("#empty-detail"),
  jobDetail: document.querySelector("#job-detail"),
  detailTitle: document.querySelector("#detail-title"),
  detailSavedBadge: document.querySelector("#detail-saved-badge"),
  detailAppliedBadge: document.querySelector("#detail-applied-badge"),
  detailClosedBadge: document.querySelector("#detail-closed-badge"),
  detailCloseReasonBadge: document.querySelector("#detail-close-reason-badge"),
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
  jobFitDetailTab: document.querySelector("#job-fit-detail-tab"),
  fullPostingTab: document.querySelector("#full-posting-tab"),
  atAGlancePanel: document.querySelector("#at-a-glance-panel"),
  jobFitDetailPanel: document.querySelector("#job-fit-detail-panel"),
  jobFitDetailContent: document.querySelector("#job-fit-detail-content"),
  fullPostingPanel: document.querySelector("#full-posting-panel"),
  detailFlags: document.querySelector("#detail-flags"),
  detailEducationMismatch: document.querySelector("#detail-education-mismatch"),
  detailEducationMismatchText: document.querySelector("#detail-education-mismatch-text"),
  detailClearanceMismatch: document.querySelector("#detail-clearance-mismatch"),
  detailClearanceMismatchText: document.querySelector("#detail-clearance-mismatch-text"),
  detailWorkAuthorizationMismatch: document.querySelector("#detail-work-authorization-mismatch"),
  detailWorkAuthorizationMismatchText: document.querySelector("#detail-work-authorization-mismatch-text"),
  detailCredentialNote: document.querySelector("#detail-credential-note"),
  detailCredentialNoteTitle: document.querySelector("#detail-credential-note-title"),
  detailCredentialNoteText: document.querySelector("#detail-credential-note-text"),
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
  detailRemoteWorkNote: document.querySelector("#detail-remote-work-note"),
  detailRemoteWorkNoteTitle: document.querySelector("#detail-remote-work-note-title"),
  detailRemoteWorkNoteText: document.querySelector("#detail-remote-work-note-text"),
  detailExtendedLocationRequirement: document.querySelector("#detail-extended-location-requirement"),
  detailExtendedLocationDestination: document.querySelector("#detail-extended-location-destination"),
  detailExtendedLocationSummary: document.querySelector("#detail-extended-location-summary"),
  detailExtendedLocationEvidence: document.querySelector("#detail-extended-location-evidence"),
  detailHeadroomNote: document.querySelector("#detail-headroom-note"),
  detailHeadroomNoteText: document.querySelector("#detail-headroom-note-text"),
  detailClearanceNote: document.querySelector("#detail-clearance-note"),
  detailClearanceEvidence: document.querySelector("#detail-clearance-evidence"),
  detailClearanceNoteText: document.querySelector("#detail-clearance-note-text"),
  detailWorkAuthorization: document.querySelector("#detail-work-authorization"),
  detailWorkAuthorizationRequirement: document.querySelector("#detail-work-authorization-requirement"),
  detailUserWorkAuthorization: document.querySelector("#detail-user-work-authorization"),
  detailWorkAuthorizationComparison: document.querySelector("#detail-work-authorization-comparison"),
  detailWorkAuthorizationEvidence: document.querySelector("#detail-work-authorization-evidence"),
  detailWorkAuthorizationEvidenceText: document.querySelector("#detail-work-authorization-evidence-text"),
  detailAcademic: document.querySelector("#detail-academic"),
  detailAcademicContent: document.querySelector("#detail-academic-content"),
  detailCredentials: document.querySelector("#detail-credentials"),
  detailCredentialsList: document.querySelector("#detail-credentials-list"),
  detailWarning: document.querySelector("#detail-warning"),
  detailDescription: document.querySelector("#detail-description"),
  copyPostingButton: document.querySelector("#copy-posting-button"),
  copyPostingLabel: document.querySelector("#copy-posting-label"),
  copyPostingStatus: document.querySelector("#copy-posting-status"),
  sourcePostingLink: document.querySelector("#source-posting-link")
};

document.addEventListener("DOMContentLoaded", initialize);

async function initialize() {
  setLoading(true, {
    title: "Loading jobs",
    phaseText: "Loading saved settings and job data…"
  });
  elements.loadingOverlay.addEventListener("keydown", constrainLoadingFocus);
  elements.sourceLoadingOverlay.addEventListener("keydown", constrainSourceLoadingFocus);
  elements.sourceConfirmationOverlay.addEventListener("keydown", constrainSourceConfirmationFocus);
  elements.sourceConfirmationStay.addEventListener("click", handleSourceConfirmationSecondary);
  elements.sourceConfirmationDiscard.addEventListener("click", discardPendingSourceAndGoToJobs);
  elements.sourceConfirmationApply.addEventListener("click", applyPendingSourceAndGoToJobs);
  elements.copyWorkspaceIdButton.addEventListener("click", copyWorkspaceId);
  elements.resetWorkspaceButton.addEventListener("click", showResetConfirmation);
  elements.resetConfirmationCancel.addEventListener("click", () => closeResetConfirmation(true));
  elements.resetConfirmationSubmit.addEventListener("click", resetCurrentWorkspace);
  elements.closeApplicationCancel.addEventListener("click", () => closeCloseApplicationModal(true));
  elements.closeApplicationSubmit.addEventListener("click", confirmCloseApplication);
  elements.closeApplicationOverlay.addEventListener("keydown", constrainCloseApplicationFocus);
  elements.importWorkspaceButton.addEventListener("click", () =>
    elements.importWorkspaceFile.click());
  elements.importWorkspaceFile.addEventListener("change", importWorkspace);
  elements.exportWorkspaceButton.addEventListener("click", exportWorkspace);
  elements.resetConfirmationOverlay.addEventListener("keydown", constrainResetConfirmationFocus);
  wireKeywordInput("inclusions", elements.includeInput, elements.addInclusion);
  wireKeywordInput("exclusions", elements.excludeInput, elements.addExclusion);
  elements.jobsTab.addEventListener("click", () => showView("jobs"));
  elements.settingsTab.addEventListener("click", () => showView("settings"));
  elements.sourceSettingsLink.addEventListener("click", () => showView("settings", true));
  document.querySelector(".app-tabs").addEventListener("keydown", handleTabKeydown);
  elements.jobSearchSettingsTab.addEventListener("click", () => showSettingsTab("job-search", true));
  elements.qualificationsSettingsTab.addEventListener("click", () => showSettingsTab("qualifications", true));
  elements.preferencesSettingsTab.addEventListener("click", () => showSettingsTab("preferences", true));
  elements.jobFitSettingsTab.addEventListener("click", () => showSettingsTab("job-fit", true));
  elements.accountSettingsTab.addEventListener("click", () => showSettingsTab("account", true));
  document.querySelector(".settings-tabs").addEventListener("keydown", handleSettingsTabKeydown);
  elements.qualificationBasicsTab.addEventListener("click", () => showQualificationTab("basics", true));
  elements.qualificationCredentialsTab.addEventListener("click", () => showQualificationTab("credentials", true));
  document.querySelector(".qualification-subtabs")
    .addEventListener("keydown", handleQualificationTabKeydown);
  elements.refreshButton.addEventListener("click", refreshJobs);
  elements.filterToggle.addEventListener("click", toggleSearchFilters);
  elements.companySelect.addEventListener("change", companySelectionChanged);
  elements.countrySelect.addEventListener("change", countrySelectionChanged);
  elements.includeAllLocations.addEventListener("change", sourceCoverageChanged);
  elements.includeRemote.addEventListener("change", sourceCoverageChanged);
  elements.locationSearch.addEventListener("input", filterLocationChoices);
  elements.applyLocation.addEventListener("click", applyJobSource);
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
      refreshDescriptionMatches();
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
  elements.themeMode.addEventListener("change", () => {
    state.themeMode = normalizeThemeMode(elements.themeMode.value);
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
  elements.excludeStrongExtendedLocationRequirements.addEventListener("change", () => {
    state.excludeStrongExtendedLocationRequirements =
      elements.excludeStrongExtendedLocationRequirements.checked;
    renderResults();
    queueSettingsSave();
  });
  elements.jobFitEnabled.addEventListener("change", () => {
    state.jobFitEnabled = elements.jobFitEnabled.checked;
    updateJobFitSettingsUi();
    renderResults();
    queueSettingsSave();
  });
  elements.jobFitConceptSearch.addEventListener("input", () => {
    state.jobFitConceptSearch = elements.jobFitConceptSearch.value.trim().toLocaleLowerCase();
    renderJobFitSurvey();
  });
  elements.jobFitSurvey.addEventListener("change", event => {
    const radio = event.target.closest('input[type="radio"][data-job-fit-concept-id]');
    if (!radio || !JobFit.preferenceLabels[radio.value]) return;
    const conceptId = radio.dataset.jobFitConceptId;
    state.jobFitSignals = state.jobFitSignals.filter(signal => signal.conceptId !== conceptId);
    if (radio.value !== "neutral") {
      state.jobFitSignals.push({ conceptId, preference: radio.value });
    }
    renderResults();
    queueSettingsSave();
  });
  elements.allResultsTab.addEventListener("click", () => showResultsTab("all", true));
  elements.savedResultsTab.addEventListener("click", () => showResultsTab("saved", true));
  elements.appliedResultsTab.addEventListener("click", () => showResultsTab("applied", true));
  elements.closedResultsTab.addEventListener("click", () => showResultsTab("closed", true));
  elements.hiddenResultsTab.addEventListener("click", () => showResultsTab("hidden", true));
  document.querySelector(".results-tabs").addEventListener("keydown", handleResultsTabKeydown);
  elements.usWorkAuthorizationStatus.addEventListener("change", () => {
    state.usWorkAuthorizationStatus = normalizeUsWorkAuthorizationStatus(elements.usWorkAuthorizationStatus.value);
    renderResults();
    queueSettingsSave();
  });
  elements.sponsorshipProfile.addEventListener("change", () => {
    state.sponsorshipProfile = normalizeSponsorshipProfile(elements.sponsorshipProfile.value);
    renderResults();
    queueSettingsSave();
  });
  elements.credentialInventoryStatus.addEventListener("change", () => {
    state.credentialInventoryStatus = normalizeCredentialInventoryStatus(
      elements.credentialInventoryStatus.value);
    if (state.credentialInventoryStatus !== "complete") {
      state.heldCredentialIds.clear();
    }
    updateCredentialSettingsUi();
    renderResults();
    queueSettingsSave();
  });
  elements.heldCredentials.addEventListener("change", event => {
    const checkbox = event.target.closest("input[data-credential-id]");
    if (!checkbox) return;
    if (checkbox.checked) state.heldCredentialIds.add(checkbox.dataset.credentialId);
    else state.heldCredentialIds.delete(checkbox.dataset.credentialId);
    updateCredentialSelectionSummary();
    renderResults();
    queueSettingsSave();
  });
  elements.credentialSearch.addEventListener("input", () => {
    state.credentialSearch = elements.credentialSearch.value.trim().toLocaleLowerCase();
    populateCredentialInventory();
  });
  elements.hideStrictWorkAuthorizationMismatch.addEventListener("change", () => {
    state.hideStrictWorkAuthorizationMismatch = elements.hideStrictWorkAuthorizationMismatch.checked;
    renderResults();
    queueSettingsSave();
  });
  elements.atAGlanceTab.addEventListener("click", () => showDetailTab("glance", true));
  elements.jobFitDetailTab.addEventListener("click", () => showDetailTab("fit", true));
  elements.fullPostingTab.addEventListener("click", () => showDetailTab("posting", true));
  elements.showCreateAccount.addEventListener("click", () => showAccountSection("create"));
  elements.showLogin.addEventListener("click", () => showAccountSection("login"));
  elements.showForgotPassword.addEventListener("click", () => showAccountSection("forgot"));
  elements.showChangePassword.addEventListener("click", () => showAccountSection("change"));
  elements.cancelCreateAccount.addEventListener("click", () => showAccountSection("default"));
  elements.cancelLogin.addEventListener("click", () => showAccountSection("default"));
  elements.cancelForgotPassword.addEventListener("click", () => showAccountSection("login"));
  elements.cancelResetPassword.addEventListener("click", cancelPasswordReset);
  elements.cancelChangePassword.addEventListener("click", () => showAccountSection("default"));
  elements.createAccountForm.addEventListener("submit", createAccount);
  elements.loginAccountForm.addEventListener("submit", loginAccount);
  elements.forgotPasswordForm.addEventListener("submit", requestPasswordReset);
  elements.resetPasswordForm.addEventListener("submit", resetPassword);
  elements.changePasswordForm.addEventListener("submit", changePassword);
  elements.administratorBootstrapForm.addEventListener("submit", claimAdministrator);
  elements.signOutAccount.addEventListener("click", signOutAccount);
  elements.requestEmailVerification.addEventListener("click", requestEmailVerification);
  elements.accountSessionPersistence.addEventListener("change", updateSessionPersistence);
  document.querySelector(".detail-tabs").addEventListener("keydown", handleDetailTabKeydown);
  elements.copyPostingButton.addEventListener("click", copySelectedJobPosting);
  document.addEventListener("visibilitychange", handleVisibilityChange);
  elements.sourcePostingLink.addEventListener("click", () => {
    const job = state.jobs.find(item => item.stableId === state.selectedJobId);
    if (job) {
      markJobViewed(job);
    }
  });

  await loadInitialState();
}

function showView(view, focusFirstControl = false, options = {}) {
  const adminAllowed = state.account?.isAdmin === true && elements.adminTab && elements.adminView;
  const nextView = view === "settings"
    ? "settings"
    : view === "admin" && adminAllowed
      ? "admin"
      : "jobs";
  const sourceNavigation = options.bypassSourceGuard === true
    ? "allow"
    : sourceNavigationDecision(nextView);
  if (sourceNavigation === "guard") {
    showSourceConfirmation();
    return false;
  }
  if (sourceNavigation === "require-source") {
    showSourceRequired();
    return false;
  }
  const enteringSettings = nextView === "settings" && state.activeView !== "settings";
  state.activeView = nextView;
  const jobsSelected = nextView === "jobs";
  const settingsSelected = nextView === "settings";
  const adminSelected = nextView === "admin";
  elements.jobsView.hidden = !jobsSelected;
  elements.settingsView.hidden = !settingsSelected;
  if (elements.adminView) elements.adminView.hidden = !adminSelected;
  elements.jobsTab.classList.toggle("active", jobsSelected);
  elements.settingsTab.classList.toggle("active", settingsSelected);
  elements.adminTab?.classList.toggle("active", adminSelected);
  elements.jobsTab.setAttribute("aria-selected", String(jobsSelected));
  elements.settingsTab.setAttribute("aria-selected", String(settingsSelected));
  elements.adminTab?.setAttribute("aria-selected", String(adminSelected));
  elements.jobsTab.tabIndex = jobsSelected ? 0 : -1;
  elements.settingsTab.tabIndex = settingsSelected ? 0 : -1;
  if (elements.adminTab) elements.adminTab.tabIndex = adminSelected ? 0 : -1;
  if (enteringSettings) {
    showSettingsTab("job-search");
  }
  if (focusFirstControl) {
    (jobsSelected
      ? elements.filterToggle
      : settingsSelected
        ? elements.companySelect
        : elements.adminView?.querySelector("h2"))?.focus?.();
  }
  if (adminSelected) void loadAdminStatus();
  return true;
}

function showSettingsTab(tab, moveFocus = false) {
  state.activeSettingsTab = ["job-search", "qualifications", "preferences", "job-fit", "account"].includes(tab)
    ? tab
    : "job-search";
  const tabs = [
    {
      id: "job-search",
      tab: elements.jobSearchSettingsTab,
      panel: elements.jobSearchSettingsPanel
    },
    {
      id: "qualifications",
      tab: elements.qualificationsSettingsTab,
      panel: elements.qualificationsSettingsPanel
    },
    {
      id: "preferences",
      tab: elements.preferencesSettingsTab,
      panel: elements.preferencesSettingsPanel
    },
    {
      id: "job-fit",
      tab: elements.jobFitSettingsTab,
      panel: elements.jobFitSettingsPanel
    },
    {
      id: "account",
      tab: elements.accountSettingsTab,
      panel: elements.accountSettingsPanel
    }
  ];
  for (const candidate of tabs) {
    const selected = candidate.id === state.activeSettingsTab;
    candidate.tab.classList.toggle("active", selected);
    candidate.tab.setAttribute("aria-selected", String(selected));
    candidate.tab.tabIndex = selected ? 0 : -1;
    candidate.panel.hidden = !selected;
  }
  if (moveFocus) {
    tabs.find(candidate => candidate.id === state.activeSettingsTab).tab.focus();
  }
}

function handleSettingsTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  event.preventDefault();
  const tabs = ["job-search", "qualifications", "preferences", "job-fit", "account"];
  const currentIndex = tabs.indexOf(state.activeSettingsTab);
  const nextIndex = event.key === "Home"
    ? 0
    : event.key === "End"
      ? tabs.length - 1
      : event.key === "ArrowLeft"
        ? (currentIndex - 1 + tabs.length) % tabs.length
        : (currentIndex + 1) % tabs.length;
  showSettingsTab(tabs[nextIndex], true);
}

function showQualificationTab(tab, moveFocus = false) {
  state.activeQualificationTab = tab === "credentials" ? "credentials" : "basics";
  const tabs = [
    { id: "basics", tab: elements.qualificationBasicsTab, panel: elements.qualificationBasicsPanel },
    { id: "credentials", tab: elements.qualificationCredentialsTab, panel: elements.qualificationCredentialsPanel }
  ];
  tabs.forEach(candidate => {
    const selected = candidate.id === state.activeQualificationTab;
    candidate.tab.classList.toggle("active", selected);
    candidate.tab.setAttribute("aria-selected", String(selected));
    candidate.tab.tabIndex = selected ? 0 : -1;
    candidate.panel.hidden = !selected;
  });
  if (moveFocus) tabs.find(candidate => candidate.id === state.activeQualificationTab).tab.focus();
}

function handleQualificationTabKeydown(event) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
  event.preventDefault();
  const tabs = ["basics", "credentials"];
  const currentIndex = tabs.indexOf(state.activeQualificationTab);
  const nextIndex = event.key === "Home" ? 0 : event.key === "End" ? 1 :
    event.key === "ArrowLeft" ? (currentIndex + 1) % 2 : (currentIndex + 1) % 2;
  showQualificationTab(tabs[nextIndex], true);
}

function handleTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
    return;
  }
  event.preventDefault();
  const tabs = [
    { id: "jobs", element: elements.jobsTab },
    { id: "settings", element: elements.settingsTab },
    ...(elements.adminTab ? [{ id: "admin", element: elements.adminTab }] : [])
  ];
  const currentIndex = Math.max(0, tabs.findIndex(tab => tab.id === state.activeView));
  const nextIndex = event.key === "Home"
    ? 0
    : event.key === "End"
      ? tabs.length - 1
      : event.key === "ArrowLeft"
        ? (currentIndex - 1 + tabs.length) % tabs.length
        : (currentIndex + 1) % tabs.length;
  const targetView = tabs[nextIndex].id;
  const changed = showView(targetView);
  if (changed) {
    tabs[nextIndex].element.focus();
  }
}

function showDetailTab(tab, moveFocus = false) {
  const fitAvailable = !elements.jobFitDetailTab.hidden;
  const nextTab = tab === "posting" ? "posting" : tab === "fit" && fitAvailable ? "fit" : "glance";
  const changed = state.detailTab !== nextTab;
  state.detailTab = nextTab;
  const glanceSelected = state.detailTab === "glance";
  const fitSelected = state.detailTab === "fit";
  const postingSelected = state.detailTab === "posting";
  elements.atAGlancePanel.hidden = !glanceSelected;
  elements.jobFitDetailPanel.hidden = !fitSelected;
  elements.fullPostingPanel.hidden = !postingSelected;
  elements.atAGlanceTab.classList.toggle("active", glanceSelected);
  elements.jobFitDetailTab.classList.toggle("active", fitSelected);
  elements.fullPostingTab.classList.toggle("active", postingSelected);
  elements.atAGlanceTab.setAttribute("aria-selected", String(glanceSelected));
  elements.jobFitDetailTab.setAttribute("aria-selected", String(fitSelected));
  elements.fullPostingTab.setAttribute("aria-selected", String(postingSelected));
  elements.atAGlanceTab.tabIndex = glanceSelected ? 0 : -1;
  elements.jobFitDetailTab.tabIndex = fitSelected ? 0 : -1;
  elements.fullPostingTab.tabIndex = postingSelected ? 0 : -1;
  if (changed) {
    document.querySelector(".detail-pane").scrollTop = 0;
  }
  if (moveFocus) {
    (glanceSelected
      ? elements.atAGlanceTab
      : fitSelected ? elements.jobFitDetailTab : elements.fullPostingTab).focus();
  }
}

function handleDetailTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
    return;
  }
  event.preventDefault();
  const tabs = elements.jobFitDetailTab.hidden
    ? ["glance", "posting"]
    : ["glance", "fit", "posting"];
  const currentIndex = Math.max(0, tabs.indexOf(state.detailTab));
  const nextIndex = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 :
    event.key === "ArrowLeft"
      ? (currentIndex - 1 + tabs.length) % tabs.length
      : (currentIndex + 1) % tabs.length;
  showDetailTab(tabs[nextIndex], true);
}

async function loadInitialState() {
  try {
    const [companiesResponse, credentialsResponse, jobFitConceptsResponse,
      settingsResponse, jobsResponse, workspaceResponse, accountResponse] = await Promise.all([
      fetch("/api/companies", { cache: "no-store" }),
      fetch("/api/credentials", { cache: "no-store" }),
      fetch("/api/job-fit/concepts", { cache: "no-store" }),
      fetch("/api/settings", { cache: "no-store" }),
      fetch("/api/jobs", { cache: "no-store" }),
      fetch("/api/workspace/identity", { cache: "no-store" }),
      fetch("/api/account/status", { cache: "no-store" })
    ]);
    if (!companiesResponse.ok || !credentialsResponse.ok || !jobFitConceptsResponse.ok || !settingsResponse.ok ||
        !jobsResponse.ok || !workspaceResponse.ok || !accountResponse.ok) {
      const failed = [companiesResponse, credentialsResponse, jobFitConceptsResponse,
        settingsResponse, jobsResponse, workspaceResponse, accountResponse]
        .find(response => !response.ok);
      throw new Error(await apiErrorMessage(failed, "Application data could not be loaded."));
    }
    state.companies = await companiesResponse.json();
    state.credentialOptions = await credentialsResponse.json();
    state.jobFitConcepts = await jobFitConceptsResponse.json();
    state.workspaceIdentity = await workspaceResponse.json();
    state.account = await accountResponse.json();
    updateWorkspaceIdentityUi();
    renderAccountUi();
    populateCompanySelect();
    applySettings(await settingsResponse.json());
    populateCredentialInventory();
    const initialSnapshot = await jobsResponse.json();
    const needsInitialRefresh = state.hasConfiguredSource && !initialSnapshot.isRefreshing &&
      !initialSnapshot.lastRefreshedUtc &&
      (!initialSnapshot.jobs || initialSnapshot.jobs.length === 0);
    applySnapshot(initialSnapshot);
    await hydrateSourceControls();
    state.isInitializing = false;
    const handledAccountLink = await handleAccountLink();
    if (handledAccountLink) {
      setLoading(false);
      showView("settings", false, { bypassSourceGuard: true });
      showSettingsTab("account");
      return;
    }
    if (!state.hasConfiguredSource) {
      setLoading(false);
      showView("settings");
      showSettingsTab("job-search");
    } else if (needsInitialRefresh) {
      await refreshJobs();
    } else if (!state.catalogIsRefreshing) {
      setLoading(false);
    }
  } catch (error) {
    state.isInitializing = false;
    showClientError(error);
  }
}

function updateWorkspaceIdentityUi() {
  const identity = state.workspaceIdentity || {};
  const authenticated = identity.accessMode === "authenticated";
  elements.workspaceId.value = authenticated
    ? identity.internalWorkspaceId || "Managed by account"
    : identity.workspaceId || "Unavailable";
  elements.copyWorkspaceIdButton.disabled = authenticated || !identity.workspaceId;
  elements.workspaceIdStatus.textContent = authenticated
    ? "This is an internal identifier, not an authentication credential. Account ownership controls access."
    : "Keep this ID private; it grants access while the workspace remains anonymous.";
}

function renderAccountUi() {
  const account = state.account || { authenticated: false, persistence: "session" };
  elements.accountEmailConfigurationNote.hidden = account.emailDeliveryConfigured === true;
  if (account.authenticated) {
    elements.accountEmail.textContent = account.email;
    elements.accountEmailStatus.textContent = account.emailVerified ? "Verified" : "Not verified";
    elements.accountWorkspaceId.textContent = state.workspaceIdentity?.internalWorkspaceId || "Internal";
    elements.accountSessionPersistence.value = account.persistence || "session";
    elements.emailVerificationActions.hidden = account.emailVerified;
    elements.requestEmailVerification.disabled = account.emailVerified ||
      account.emailDeliveryConfigured !== true;
  } else {
    elements.anonymousAccountWorkspaceId.textContent =
      state.workspaceIdentity?.workspaceId || "Unavailable";
  }
  elements.administratorBootstrapSection.hidden = !(
    account.authenticated && account.administratorBootstrapAvailable === true);
  synchronizeAdminNavigation(account.isAdmin === true);
  showAccountSection("default");
}

function synchronizeAdminNavigation(isAdmin) {
  if (!isAdmin) {
    if (state.activeView === "admin") showView("jobs", false, { bypassSourceGuard: true });
    elements.adminTab?.remove();
    elements.adminView?.remove();
    elements.adminTab = null;
    elements.adminView = null;
    elements.adminStatus = null;
    state.adminStatusLoaded = false;
    return;
  }
  if (elements.adminTab) return;

  const tab = document.createElement("button");
  tab.id = "admin-tab";
  tab.className = "app-tab";
  tab.type = "button";
  tab.setAttribute("role", "tab");
  tab.setAttribute("aria-selected", "false");
  tab.setAttribute("aria-controls", "admin-view");
  tab.tabIndex = -1;
  tab.textContent = "Admin";
  tab.addEventListener("click", () => showView("admin"));
  elements.settingsTab.after(tab);

  const view = document.createElement("section");
  view.id = "admin-view";
  view.className = "app-view settings-view";
  view.setAttribute("role", "tabpanel");
  view.setAttribute("aria-labelledby", "admin-tab");
  view.hidden = true;

  const header = document.createElement("header");
  header.className = "settings-header";
  const heading = document.createElement("h2");
  heading.tabIndex = -1;
  heading.textContent = "Administration";
  header.append(heading);

  const surface = document.createElement("div");
  surface.className = "settings-surface";
  const section = document.createElement("section");
  section.className = "settings-section account-section";
  const title = document.createElement("h3");
  title.textContent = "Administrator Access";
  const message = document.createElement("p");
  message.textContent = "Administrator access is active for this account.";
  const future = document.createElement("p");
  future.className = "account-help";
  future.textContent = "Administrative workspace and account management tools will be added here in future versions.";
  const status = document.createElement("p");
  status.className = "settings-status";
  status.setAttribute("role", "status");
  status.setAttribute("aria-live", "polite");
  section.append(title, message, future, status);
  surface.append(section);
  view.append(header, surface);
  elements.settingsView.after(view);

  elements.adminTab = tab;
  elements.adminView = view;
  elements.adminStatus = status;
}

async function loadAdminStatus() {
  if (!elements.adminStatus || state.adminStatusLoaded) return;
  elements.adminStatus.textContent = "Verifying administrator access…";
  try {
    const response = await fetch("/api/admin/status", { cache: "no-store" });
    if (!response.ok) throw new Error("Administrator access could not be verified.");
    const status = await response.json();
    if (status.administrator !== true) throw new Error("Administrator access could not be verified.");
    state.adminStatusLoaded = true;
    elements.adminStatus.textContent = status.email ? `Signed in as: ${status.email}` : "";
  } catch (error) {
    elements.adminStatus.textContent = error.message || String(error);
  }
}

async function claimAdministrator(event) {
  event.preventDefault();
  elements.administratorBootstrapStatus.textContent = "";
  setFormBusy(elements.administratorBootstrapForm, true);
  try {
    const result = await postAccountJson("/api/account/admin-bootstrap", {
      code: elements.administratorBootstrapCode.value
    });
    elements.administratorBootstrapForm.reset();
    state.account.isAdmin = result.isAdmin === true;
    state.account.administratorBootstrapAvailable = false;
    renderAccountUi();
    showView("admin", true, { bypassSourceGuard: true });
  } catch (error) {
    elements.administratorBootstrapStatus.textContent = error.message || String(error);
  } finally {
    setFormBusy(elements.administratorBootstrapForm, false);
  }
}

function showAccountSection(mode) {
  const sections = {
    anonymous: elements.anonymousAccountSection,
    create: elements.createAccountSection,
    login: elements.loginAccountSection,
    forgot: elements.forgotPasswordSection,
    reset: elements.resetPasswordSection,
    authenticated: elements.authenticatedAccountSection,
    change: elements.changePasswordSection
  };
  Object.values(sections).forEach(section => { section.hidden = true; });
  let selected = mode;
  if (selected === "default") selected = state.account?.authenticated ? "authenticated" : "anonymous";
  if (selected === "change" && !state.account?.authenticated) selected = "login";
  if (!sections[selected]) selected = state.account?.authenticated ? "authenticated" : "anonymous";
  sections[selected].hidden = false;
  const focusTarget = sections[selected].querySelector("input, select, button");
  if (mode !== "default") focusTarget?.focus({ preventScroll: true });
}

function setFormBusy(form, busy) {
  form.querySelectorAll("input, select, button").forEach(control => { control.disabled = busy; });
}

async function postAccountJson(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  const payload = response.status === 204 ? {} : await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(payload.error || "The account request could not be completed.");
  return payload;
}

async function createAccount(event) {
  event.preventDefault();
  elements.createAccountStatus.textContent = "";
  if (elements.createAccountPassword.value !== elements.createAccountConfirm.value) {
    elements.createAccountStatus.textContent = "The password confirmation does not match.";
    return;
  }
  setFormBusy(elements.createAccountForm, true);
  try {
    await postAccountJson("/api/account/create", {
      email: elements.createAccountEmail.value,
      password: elements.createAccountPassword.value,
      confirmPassword: elements.createAccountConfirm.value,
      persistence: elements.createAccountPersistence.value
    });
    window.location.reload();
  } catch (error) {
    elements.createAccountStatus.textContent = error.message || String(error);
    setFormBusy(elements.createAccountForm, false);
  }
}

async function loginAccount(event) {
  event.preventDefault();
  elements.loginAccountStatus.textContent = "";
  setFormBusy(elements.loginAccountForm, true);
  try {
    await postAccountJson("/api/account/login", {
      email: elements.loginAccountEmail.value,
      password: elements.loginAccountPassword.value,
      persistence: elements.loginAccountPersistence.value
    });
    window.location.reload();
  } catch (error) {
    elements.loginAccountStatus.textContent = error.message || String(error);
    setFormBusy(elements.loginAccountForm, false);
  }
}

async function requestPasswordReset(event) {
  event.preventDefault();
  elements.forgotPasswordStatus.textContent = "";
  setFormBusy(elements.forgotPasswordForm, true);
  try {
    const result = await postAccountJson("/api/account/forgot-password", {
      email: elements.forgotPasswordEmail.value
    });
    elements.forgotPasswordStatus.textContent = result.message;
  } catch (error) {
    elements.forgotPasswordStatus.textContent = error.message || String(error);
  } finally {
    setFormBusy(elements.forgotPasswordForm, false);
  }
}

async function resetPassword(event) {
  event.preventDefault();
  elements.resetPasswordStatus.textContent = "";
  if (elements.resetAccountPassword.value !== elements.resetAccountConfirm.value) {
    elements.resetPasswordStatus.textContent = "The password confirmation does not match.";
    return;
  }
  setFormBusy(elements.resetPasswordForm, true);
  try {
    const result = await postAccountJson("/api/account/reset-password", {
      token: state.accountLinkToken,
      password: elements.resetAccountPassword.value,
      confirmPassword: elements.resetAccountConfirm.value
    });
    clearAccountLink();
    showAccountSection("login");
    elements.loginAccountStatus.textContent = result.message;
  } catch (error) {
    elements.resetPasswordStatus.textContent = error.message || String(error);
  } finally {
    setFormBusy(elements.resetPasswordForm, false);
  }
}

async function changePassword(event) {
  event.preventDefault();
  elements.changePasswordStatus.textContent = "";
  if (elements.newAccountPassword.value !== elements.newAccountConfirm.value) {
    elements.changePasswordStatus.textContent = "The password confirmation does not match.";
    return;
  }
  setFormBusy(elements.changePasswordForm, true);
  try {
    const result = await postAccountJson("/api/account/change-password", {
      currentPassword: elements.currentAccountPassword.value,
      password: elements.newAccountPassword.value,
      confirmPassword: elements.newAccountConfirm.value,
      persistence: elements.accountSessionPersistence.value
    });
    elements.changePasswordForm.reset();
    showAccountSection("default");
    elements.authenticatedAccountStatus.textContent = result.message;
  } catch (error) {
    elements.changePasswordStatus.textContent = error.message || String(error);
  } finally {
    setFormBusy(elements.changePasswordForm, false);
  }
}

async function signOutAccount() {
  elements.signOutAccount.disabled = true;
  elements.authenticatedAccountStatus.textContent = "Signing out…";
  try {
    await postAccountJson("/api/account/logout", {});
    window.location.reload();
  } catch (error) {
    elements.authenticatedAccountStatus.textContent = error.message || String(error);
    elements.signOutAccount.disabled = false;
  }
}

async function updateSessionPersistence() {
  elements.accountSessionPersistence.disabled = true;
  try {
    const result = await postAccountJson("/api/account/session", {
      persistence: elements.accountSessionPersistence.value
    });
    state.account.persistence = result.persistence;
    elements.authenticatedAccountStatus.textContent = "Login persistence updated for this browser.";
  } catch (error) {
    elements.authenticatedAccountStatus.textContent = error.message || String(error);
  } finally {
    elements.accountSessionPersistence.disabled = false;
  }
}

async function requestEmailVerification() {
  elements.requestEmailVerification.disabled = true;
  try {
    const result = await postAccountJson("/api/account/request-verification", {});
    elements.authenticatedAccountStatus.textContent = result.message;
  } catch (error) {
    elements.authenticatedAccountStatus.textContent = error.message || String(error);
  } finally {
    elements.requestEmailVerification.disabled = false;
  }
}

function accountLinkParameters() {
  const fragment = window.location.hash.startsWith("#") ? window.location.hash.slice(1) : "";
  return new URLSearchParams(fragment);
}

function clearAccountLink() {
  state.accountLinkToken = null;
  history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);
}

function cancelPasswordReset() {
  clearAccountLink();
  showAccountSection("default");
}

async function handleAccountLink() {
  const parameters = accountLinkParameters();
  const resetToken = parameters.get("resetToken");
  const verificationToken = parameters.get("verifyEmailToken");
  if (resetToken) {
    state.accountLinkToken = resetToken;
    showAccountSection("reset");
    return true;
  }
  if (verificationToken) {
    try {
      const result = await postAccountJson("/api/account/verify-email", { token: verificationToken });
      clearAccountLink();
      const response = await fetch("/api/account/status", { cache: "no-store" });
      if (response.ok) state.account = await response.json();
      renderAccountUi();
      showAccountSection("default");
      const status = state.account?.authenticated
        ? elements.authenticatedAccountStatus : elements.loginAccountStatus;
      status.textContent = result.message;
    } catch (error) {
      clearAccountLink();
      showAccountSection(state.account?.authenticated ? "default" : "login");
      const status = state.account?.authenticated
        ? elements.authenticatedAccountStatus : elements.loginAccountStatus;
      status.textContent = error.message || String(error);
    }
    return true;
  }
  return false;
}

async function copyWorkspaceId() {
  const workspaceId = elements.workspaceId.value;
  if (!workspaceId || workspaceId === "Unavailable" || workspaceId === "Loading…") return;

  try {
    if (!navigator.clipboard?.writeText) throw new Error("Clipboard API is unavailable.");
    await navigator.clipboard.writeText(workspaceId);
    elements.workspaceIdStatus.textContent = "Workspace ID copied.";
  } catch {
    elements.workspaceId.select();
    elements.workspaceIdStatus.textContent = "Select the Workspace ID and copy it manually.";
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
  state.themeMode = normalizeThemeMode(settings.themeMode);
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
  state.usWorkAuthorizationStatus = normalizeUsWorkAuthorizationStatus(
    settings.userProfile?.workAuthorization?.usStatus);
  state.sponsorshipProfile = normalizeSponsorshipProfile(
    settings.userProfile?.workAuthorization?.sponsorship);
  state.credentialInventoryStatus = normalizeCredentialInventoryStatus(
    settings.userProfile?.credentials?.inventoryStatus);
  state.heldCredentialIds = new Set(
    Array.isArray(settings.userProfile?.credentials?.heldCredentialIds)
      ? settings.userProfile.credentials.heldCredentialIds
      : []);
  state.hideStrictWorkAuthorizationMismatch = settings.hideStrictWorkAuthorizationMismatch === true;
  state.excludeStrongExtendedLocationRequirements =
    settings.excludeStrongExtendedLocationRequirements === true;
  const jobFit = JobFit.normalizeConfiguration(settings.jobFit);
  const validJobFitIds = new Set(state.jobFitConcepts.map(concept => concept.id));
  state.jobFitEnabled = jobFit.enabled;
  state.jobFitSignals = jobFit.signals.filter(signal => validJobFitIds.has(signal.conceptId));
  state.hasConfiguredSource = settings.hasConfiguredSource === true;
  state.pendingImportedSource = settings.pendingSource || null;
  state.companyId = state.hasConfiguredSource ? settings.companyId || "" : "";
  state.companyName = companyById(state.companyId)?.displayName || "Job source";
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
  elements.themeMode.value = state.themeMode;
  elements.educationLevel.value = state.educationLevel;
  elements.doctorateType.value = state.doctorateType || "";
  elements.hideStrictEducationMismatch.checked = state.hideStrictEducationMismatch;
  elements.clearanceProfileLevel.value = state.clearanceProfileLevel;
  elements.publicTrustProfile.value = state.publicTrustProfile;
  elements.hideStrictClearanceMismatch.checked = state.hideStrictClearanceMismatch;
  elements.usWorkAuthorizationStatus.value = state.usWorkAuthorizationStatus;
  elements.sponsorshipProfile.value = state.sponsorshipProfile;
  elements.credentialInventoryStatus.value = state.credentialInventoryStatus;
  elements.hideStrictWorkAuthorizationMismatch.checked = state.hideStrictWorkAuthorizationMismatch;
  elements.excludeStrongExtendedLocationRequirements.checked =
    state.excludeStrongExtendedLocationRequirements;
  elements.jobFitEnabled.checked = state.jobFitEnabled;
  updateEducationSettingsUi();
  updateCredentialSettingsUi();
  updateJobFitSettingsUi();
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
    localStorage.setItem("job-search-manager-theme-hint", state.themeMode);
    localStorage.removeItem("workday-job-manager-theme-hint");
  } catch {
    // The persisted backend setting is authoritative; this cache is optional.
  }
}

function updateJobFitSettingsUi() {
  elements.jobFitConfiguration.hidden = !state.jobFitEnabled;
  elements.jobFitConceptSearch.disabled = !state.jobFitEnabled;
  renderJobFitSurvey();
}

function renderJobFitSurvey() {
  elements.jobFitSurvey.replaceChildren();
  const query = state.jobFitConceptSearch;
  const visible = state.jobFitConcepts.filter(concept => !query ||
    `${concept.displayName} ${concept.category}`.toLocaleLowerCase().includes(query));
  const configured = new Map(state.jobFitSignals.map(signal => [signal.conceptId, signal.preference]));
  const byCategory = new Map();
  visible.forEach(concept => {
    if (!byCategory.has(concept.category)) byCategory.set(concept.category, []);
    byCategory.get(concept.category).push(concept);
  });
  elements.jobFitSurveyStatus.textContent = `${visible.length} of ${state.jobFitConcepts.length} concepts shown.`;
  if (visible.length === 0) {
    const empty = document.createElement("p");
    empty.className = "job-fit-empty";
    empty.textContent = "No canonical concepts match this filter.";
    elements.jobFitSurvey.append(empty);
    return;
  }
  const preferences = [
    ["hardConflict", "HC"],
    ["negative", "NEG"],
    ["neutral", "N"],
    ["positive", "P"],
    ["ideal", "I"]
  ];
  const categoryOrder = Object.keys(JobFit.dimensionLimits);
  const orderedCategories = Array.from(byCategory.keys()).sort((left, right) =>
    categoryOrder.indexOf(left) - categoryOrder.indexOf(right));
  for (const categoryName of orderedCategories) {
    const concepts = byCategory.get(categoryName);
    const section = document.createElement("details");
    section.className = "job-fit-survey-category";
    section.open = true;
    const summary = document.createElement("summary");
    const heading = document.createElement("span");
    heading.textContent = categoryName;
    const count = document.createElement("small");
    count.textContent = `${concepts.length} concept${concepts.length === 1 ? "" : "s"}`;
    summary.append(heading, count);
    const matrix = document.createElement("div");
    matrix.className = "job-fit-matrix";
    const header = document.createElement("div");
    header.className = "job-fit-matrix-header";
    header.setAttribute("aria-hidden", "true");
    const blank = document.createElement("span");
    preferences.forEach(([value]) => {
      const label = document.createElement("span");
      label.textContent = JobFit.preferenceLabels[value];
      header.append(label);
    });
    header.prepend(blank);
    matrix.append(header);
    concepts.sort((left, right) => left.displayName.localeCompare(right.displayName)).forEach(concept => {
      const row = document.createElement("div");
      row.className = "job-fit-survey-row";
      row.setAttribute("role", "radiogroup");
      const labelId = `job-fit-concept-${concept.id.replace(/[^a-z0-9_-]/gi, "-")}`;
      row.setAttribute("aria-labelledby", labelId);
      const name = document.createElement("strong");
      name.id = labelId;
      name.className = "job-fit-survey-concept";
      name.textContent = concept.displayName;
      row.append(name);
      preferences.forEach(([value, abbreviation]) => {
        const choice = document.createElement("label");
        choice.className = "job-fit-survey-choice";
        choice.title = JobFit.preferenceLabels[value];
        const radio = document.createElement("input");
        radio.type = "radio";
        radio.name = `job-fit-${concept.id}`;
        radio.value = value;
        radio.dataset.jobFitConceptId = concept.id;
        radio.checked = (configured.get(concept.id) || "neutral") === value;
        radio.disabled = !state.jobFitEnabled;
        radio.setAttribute("aria-label", `${concept.displayName}: ${JobFit.preferenceLabels[value]}`);
        const short = document.createElement("span");
        short.className = "job-fit-choice-short";
        short.setAttribute("aria-hidden", "true");
        short.textContent = abbreviation;
        choice.append(radio, short);
        row.append(choice);
      });
      matrix.append(row);
    });
    section.append(summary, matrix);
    elements.jobFitSurvey.append(section);
  }
}

function updateEducationSettingsUi() {
  const isDoctorate = state.educationLevel === "doctorate";
  elements.doctorateTypeField.hidden = !isDoctorate;
  elements.doctorateType.disabled = !isDoctorate;
}

const CREDENTIAL_CATEGORY_ORDER = [
  "Engineering & Professional Licenses",
  "Project, Program & Construction Management",
  "Cybersecurity, Privacy & DoD",
  "Technology, IT & Systems",
  "Cloud, DevOps & Data Platforms",
  "Construction, Quality & Safety",
  "Transportation & Aviation",
  "Facilities, Electrical & Skilled Trades",
  "Business & Other Professional Credentials"
];

function credentialInventoryCategory(credential) {
  const category = credential.category || "Other";
  if (category === "Engineering License") return "Engineering & Professional Licenses";
  if (category === "Project Management") return "Project, Program & Construction Management";
  if (["Cybersecurity", "Privacy"].includes(category)) return "Cybersecurity, Privacy & DoD";
  if (["Systems", "Networking", "Linux", "IT Support", "IT Service Management"].includes(category)) {
    return "Technology, IT & Systems";
  }
  if (["Cloud", "DevOps"].includes(category) || credential.family === "Data Platforms") {
    return "Cloud, DevOps & Data Platforms";
  }
  if (["Quality", "Safety"].includes(category) || credential.family === "Construction Management") {
    return "Construction, Quality & Safety";
  }
  if (["Transportation", "Aviation"].includes(category) || credential.family === "Pilot Credentials") {
    return "Transportation & Aviation";
  }
  if (category === "Facilities / Skilled Trades") return "Facilities, Electrical & Skilled Trades";
  return "Business & Other Professional Credentials";
}

function populateCredentialInventory() {
  elements.heldCredentials.replaceChildren();
  const byCategory = new Map();
  const visible = state.credentialOptions.filter(credential => {
    if (!state.credentialSearch) return true;
    return [credential.name, credential.fullName, credential.issuer, credential.category,
      credential.family, credentialInventoryCategory(credential)]
      .filter(Boolean).join(" ").toLocaleLowerCase().includes(state.credentialSearch);
  });
  visible.forEach(credential => {
    const category = credentialInventoryCategory(credential);
    if (!byCategory.has(category)) byCategory.set(category, []);
    byCategory.get(category).push(credential);
  });
  CREDENTIAL_CATEGORY_ORDER.filter(category => byCategory.has(category)).forEach(category => {
    const details = document.createElement("details");
    details.className = "credential-inventory-category";
    details.open = Boolean(state.credentialSearch) || byCategory.size <= 3;
    const summary = document.createElement("summary");
    summary.textContent = `${category} (${byCategory.get(category).length})`;
    const options = document.createElement("div");
    options.className = "credential-inventory-options";
    byCategory.get(category)
      .sort((left, right) => left.issuer.localeCompare(right.issuer) || left.name.localeCompare(right.name))
      .forEach(credential => {
        const label = document.createElement("label");
        label.className = "credential-inventory-option";
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.dataset.credentialId = credential.id;
        checkbox.checked = state.heldCredentialIds.has(credential.id);
        const text = document.createElement("span");
        const name = document.createElement("strong");
        name.textContent = credential.name;
        const detail = document.createElement("small");
        detail.textContent = [credential.fullName !== credential.name ? credential.fullName : null,
          credential.issuer].filter(Boolean).join(" · ");
        text.append(name, detail);
        label.append(checkbox, text);
        options.append(label);
      });
    details.append(summary, options);
    elements.heldCredentials.append(details);
  });
  if (visible.length === 0) {
    const empty = document.createElement("p");
    empty.className = "credential-inventory-empty";
    empty.textContent = "No credentials match this search.";
    elements.heldCredentials.append(empty);
  }
  updateCredentialSettingsUi();
}

function updateCredentialSettingsUi() {
  const configured = state.credentialInventoryStatus === "complete";
  elements.heldCredentialsField.hidden = !configured;
  elements.credentialSearch.disabled = !configured;
  elements.heldCredentials.querySelectorAll("input[data-credential-id]").forEach(checkbox => {
    checkbox.disabled = !configured;
    checkbox.checked = configured && state.heldCredentialIds.has(checkbox.dataset.credentialId);
  });
  updateCredentialSelectionSummary();
}

function updateCredentialSelectionSummary() {
  const selected = state.credentialInventoryStatus === "complete"
    ? state.credentialOptions
      .filter(credential => state.heldCredentialIds.has(credential.id))
      .sort((left, right) => left.name.localeCompare(right.name))
    : [];
  elements.credentialSelectionSummary.replaceChildren();
  if (selected.length === 0) {
    elements.credentialSelectionSummary.textContent = "No credentials selected.";
    return;
  }

  const prefix = document.createElement("span");
  prefix.textContent = `Selected (${selected.length}):`;
  elements.credentialSelectionSummary.append(prefix);
  selected.forEach(credential => {
    const chip = document.createElement("span");
    chip.className = "chip selected-credential-chip";
    chip.append(document.createTextNode(credential.name));

    const remove = document.createElement("button");
    remove.type = "button";
    remove.setAttribute("aria-label", `Remove credential ${credential.name}`);
    remove.textContent = "×";
    remove.addEventListener("click", () => removeHeldCredential(credential.id));
    chip.append(remove);
    elements.credentialSelectionSummary.append(chip);
  });
}

function removeHeldCredential(credentialId) {
  if (!state.heldCredentialIds.delete(credentialId)) return;
  updateCredentialSettingsUi();
  renderResults();
  queueSettingsSave();
}

function normalizeCredentialInventoryStatus(value) {
  return ["none", "complete"].includes(value) ? value : "notConfigured";
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

async function hydrateSourceControls() {
  if (state.pendingImportedSource?.companyId) {
    const pending = state.pendingImportedSource;
    elements.companySelect.value = pending.companyId;
    await loadLocationFacets(
      pending.companyId,
      pending.source?.country?.id || null,
      pending.source?.selectedPhysicalLocations || [],
      pending.source?.includeAllLocations === true,
      pending.source?.includeRemote === true);
    elements.facetStatus.textContent =
      "Workspace imported. Apply Job Source to load jobs using the imported source.";
    return;
  }
  if (state.hasConfiguredSource) {
    elements.companySelect.value = state.companyId;
    await loadLocationFacets(
      state.companyId,
      state.country.id,
      state.physicalLocations,
      state.includeAllLocations,
      state.includeRemote);
    return;
  }

  elements.companySelect.value = "";
  populateCountrySelect(elements.countrySelect, [{
    id: "bc33aa3152ec42d4995f4791a106ed09",
    label: "United States of America",
    count: 0
  }], ALL_COUNTRIES_LABEL, "bc33aa3152ec42d4995f4791a106ed09");
  elements.includeAllLocations.checked = false;
  elements.includeRemote.checked = false;
  elements.includeRemoteOption.hidden = false;
  elements.locationSearch.value = "";
  elements.locationGroups.replaceChildren();
  state.locationGroups = [];
  state.remoteLocations = [];
  state.facetsLoaded = false;
  elements.facetStatus.textContent =
    "Select a company to load its available country and location choices.";
  updateSelectedLocationSummary();
  updateQueryControls();
}

function showResultsTab(tab, moveFocus = false) {
  state.activeResultsTab = ["all", "saved", "applied", "closed", "hidden"].includes(tab)
    ? tab
    : "all";
  const tabs = [
    { id: "all", element: elements.allResultsTab },
    { id: "saved", element: elements.savedResultsTab },
    { id: "applied", element: elements.appliedResultsTab },
    { id: "closed", element: elements.closedResultsTab },
    { id: "hidden", element: elements.hiddenResultsTab }
  ];
  for (const candidate of tabs) {
    const selected = candidate.id === state.activeResultsTab;
    candidate.element.classList.toggle("active", selected);
    candidate.element.setAttribute("aria-selected", String(selected));
    candidate.element.tabIndex = selected ? 0 : -1;
  }
  elements.resultsTabPanel.setAttribute("aria-labelledby", `${state.activeResultsTab}-results-tab`);
  renderResults();
  if (moveFocus) {
    tabs.find(candidate => candidate.id === state.activeResultsTab).element.focus();
  }
}

function handleResultsTabKeydown(event) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  event.preventDefault();
  const tabs = ["all", "saved", "applied", "closed", "hidden"];
  const currentIndex = tabs.indexOf(state.activeResultsTab);
  const nextIndex = event.key === "Home"
    ? 0
    : event.key === "End"
      ? tabs.length - 1
      : event.key === "ArrowLeft"
        ? (currentIndex - 1 + tabs.length) % tabs.length
        : (currentIndex + 1) % tabs.length;
  showResultsTab(tabs[nextIndex], true);
}

function normalizeUsWorkAuthorizationStatus(value) {
  return ["notSpecified", "usCitizen", "permanentResident", "otherAuthorized", "notAuthorized"]
    .includes(value) ? value : "notSpecified";
}

function normalizeSponsorshipProfile(value) {
  return ["unknown", "notRequired", "required"].includes(value) ? value : "unknown";
}

function handleVisibilityChange() {
  if (document.visibilityState !== "hidden" && state.catalogIsRefreshing) {
    beginRefreshProgressPolling(true);
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
  if (state.hideStrictWorkAuthorizationMismatch) {
    summary.push("Strict work-authorization filter");
  }
  if (state.excludeStrongExtendedLocationRequirements) {
    summary.push("Deployment / remote assignments excluded");
  }
  return summary.length
    ? summary.join(" · ")
    : "No active keyword, salary, remote-location, qualification, or deployment filters";
}

function updateSourceSummary() {
  if (!state.hasConfiguredSource) {
    elements.sourceSummary.textContent = "Job source not configured";
    return;
  }
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
  const placeholder = document.createElement("option");
  placeholder.value = "";
  placeholder.textContent = "Select a company…";
  elements.companySelect.append(placeholder);
  for (const group of CompanySelector.groupCompanies(state.companies)) {
    const optionGroup = document.createElement("optgroup");
    optionGroup.label = group.category;
    for (const company of group.companies) {
      const option = document.createElement("option");
      option.value = company.id;
      option.textContent = company.displayName;
      optionGroup.append(option);
    }
    elements.companySelect.append(optionGroup);
  }
}

async function companySelectionChanged() {
  const companyId = elements.companySelect.value;
  if (!companyId) {
    await hydrateSourceControls();
    return;
  }
  const request = beginSourceMetadataLoad(companyId);
  state.facetsLoaded = false;
  updateQueryControls();
  try {
    const response = await fetch(`/api/source/${encodeURIComponent(companyId)}`, {
      cache: "no-store",
      signal: request.signal
    });
    if (!response.ok) {
      throw new Error(`Company source returned HTTP ${response.status}.`);
    }
    const result = await response.json();
    if (!isCurrentSourceRequest(request)) return;
    const source = result.source || {};
    const country = normalizeFacetSelection(source.country, ALL_COUNTRIES_LABEL);
    await loadLocationFacets(
      companyId,
      country.id,
      source.selectedPhysicalLocations || [],
      source.includeAllLocations === true,
      source.includeRemote === true,
      request);
  } catch (error) {
    if (error?.name === "AbortError" || !isCurrentSourceRequest(request)) return;
    elements.errorBanner.textContent = `Company source could not be loaded: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  } finally {
    endSourceMetadataLoad(request);
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
  includeRemote = false,
  existingRequest = null) {
  const request = existingRequest || beginSourceMetadataLoad(companyId);
  const ownsRequest = existingRequest === null;
  state.facetsLoaded = false;
  let loaded = false;
  updateQueryControls();
  elements.facetStatus.textContent = "Loading job-source location choices…";
  try {
    const parameters = new URLSearchParams();
    parameters.set("companyId", companyId);
    if (countryId) parameters.set("countryId", countryId);
    const cacheKey = `${companyId}|${countryId || ""}`;
    let facets = state.sourceFacetCache.get(cacheKey);
    if (!facets) {
      const response = await fetch(`/api/location-facets?${parameters}`, {
        cache: "no-store",
        signal: request.signal
      });
      if (!response.ok) {
        throw new Error(await apiErrorMessage(response, "Location choices could not be loaded."));
      }
      facets = await response.json();
      if (!isCurrentSourceRequest(request)) return;
      state.sourceFacetCache.set(cacheKey, facets);
    }
    if (!isCurrentSourceRequest(request) || elements.companySelect.value !== companyId) return;
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
    if (error?.name === "AbortError" || !isCurrentSourceRequest(request)) return;
    elements.facetStatus.textContent = `Location choices unavailable: ${error.message || error}`;
    elements.errorBanner.textContent = elements.facetStatus.textContent;
    elements.errorBanner.hidden = false;
  } finally {
    if (isCurrentSourceRequest(request)) {
      state.facetsLoaded = loaded;
      updateQueryControls();
    }
    if (ownsRequest) endSourceMetadataLoad(request);
  }
}

function populateCountrySelect(select, options, allLabel, selectedId) {
  select.replaceChildren();
  const all = document.createElement("option");
  all.value = "";
  all.dataset.label = allLabel;
  all.textContent = allLabel;
  select.append(all);

  const orderedOptions = CountryOrdering.orderCountryFacets(options);
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
  const companyId = elements.companySelect.value;
  const request = beginSourceMetadataLoad(companyId);
  await loadLocationFacets(
    companyId,
    countryId,
    [],
    true,
    true,
    request);
  endSourceMetadataLoad(request);
  if (isCurrentSourceRequest(request)) updateQueryControls();
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
  return !JobSourceState.areEquivalent(appliedSourceState(), editableSourceState(), {
    remoteAvailable: state.remoteLocations.length > 0
  });
}

function appliedSourceState() {
  return {
    companyId: state.companyId,
    countryId: state.country.id,
    includeAllLocations: state.includeAllLocations,
    includeRemote: state.includeRemote,
    physicalLocations: state.physicalLocations
  };
}

function editableSourceState() {
  return {
    companyId: elements.companySelect.value,
    countryId: elements.countrySelect.value || null,
    includeAllLocations: elements.includeAllLocations.checked,
    includeRemote: elements.includeRemote.checked,
    physicalLocations: selectedPendingLocations()
  };
}

function sourceNavigationDecision(nextView) {
  // Entering Settings must never depend on editable Job Source controls. Apart
  // from keeping that direction unguarded, this avoids reading partially
  // hydrated or legacy controls before Settings has been opened.
  if (nextView !== "jobs") return "allow";
  return JobSourceState.navigationDecision(
    state.activeView,
    nextView,
    state.hasConfiguredSource,
    appliedSourceState(),
    editableSourceState(),
    { remoteAvailable: state.remoteLocations.length > 0 });
}

function formatSourceDescription(companyName, country, includeAll, includeRemote, physicalLocations) {
  return `${companyName || "Job source"} · ${country?.label || ALL_COUNTRIES_LABEL} · ${describeSourceLocations(
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
  state.sourceConfirmationMode = "pending";
  clearTimeout(state.sourceConfirmationHideTimer);
  state.sourceConfirmationOpen = true;
  state.focusBeforeSourceConfirmation = document.activeElement;
  elements.sourceConfirmationTitle.textContent = "Unapplied job source changes";
  elements.sourceConfirmationCopy.textContent = state.pendingImportedSource
    ? "The imported job source has not been applied yet."
    : "You changed the job source.";
  elements.sourceConfirmationComparison.hidden = false;
  elements.sourceConfirmationCurrent.textContent = state.hasConfiguredSource
    ? formatSourceDescription(
      state.companyName,
      state.country,
      state.includeAllLocations,
      state.includeRemote,
      state.physicalLocations)
    : "No job source configured";
  elements.sourceConfirmationPending.textContent = pendingSourceDescription();
  elements.sourceConfirmationQuestion.hidden = false;
  elements.sourceConfirmationStay.textContent = "Stay in Settings";
  elements.sourceConfirmationDiscard.hidden = !state.hasConfiguredSource;
  elements.sourceConfirmationApply.hidden = false;
  openSourceConfirmation(elements.sourceConfirmationApply);
}

function showSourceRequired() {
  state.sourceConfirmationMode = "required";
  clearTimeout(state.sourceConfirmationHideTimer);
  state.sourceConfirmationOpen = true;
  state.focusBeforeSourceConfirmation = document.activeElement;
  elements.sourceConfirmationTitle.textContent = "Job source required";
  elements.sourceConfirmationCopy.textContent = elements.companySelect.value
    ? "Finish configuring and apply the selected job source before viewing Jobs."
    : "Select a company and configure a job source before viewing Jobs.";
  elements.sourceConfirmationComparison.hidden = true;
  elements.sourceConfirmationQuestion.hidden = true;
  elements.sourceConfirmationStay.textContent = "Go to Job Source";
  elements.sourceConfirmationDiscard.hidden = true;
  elements.sourceConfirmationApply.hidden = true;
  openSourceConfirmation(elements.sourceConfirmationStay);
}

function openSourceConfirmation(initialFocus) {
  elements.appShell.inert = true;
  elements.sourceConfirmationOverlay.hidden = false;
  elements.sourceConfirmationOverlay.setAttribute("aria-hidden", "false");
  requestAnimationFrame(() => {
    elements.sourceConfirmationOverlay.classList.add("visible");
    initialFocus.focus({ preventScroll: true });
  });
}

function handleSourceConfirmationSecondary() {
  if (state.sourceConfirmationMode === "required") {
    closeSourceConfirmation(false);
    showView("settings");
    showSettingsTab("job-search", true);
    elements.facetStatus.textContent = elements.companySelect.value
      ? "Finish configuring the location source, then select Apply Job Source."
      : "Select a company and location source, then apply it to load jobs.";
    return;
  }
  closeSourceConfirmation(true);
}

function closeSourceConfirmation(restoreFocus) {
  if (!state.sourceConfirmationOpen) return;
  const closingMode = state.sourceConfirmationMode;
  state.sourceConfirmationOpen = false;
  state.sourceConfirmationMode = null;
  elements.sourceConfirmationOverlay.classList.remove("visible");
  elements.sourceConfirmationOverlay.setAttribute("aria-hidden", "true");
  elements.appShell.inert = state.isRefreshing || state.sourceMetadataLoading ||
    state.resetConfirmationOpen || state.closeApplicationOpen;
  state.sourceConfirmationHideTimer = setTimeout(() => {
    if (!state.sourceConfirmationOpen) elements.sourceConfirmationOverlay.hidden = true;
  }, OVERLAY_TRANSITION_MS);
  if (restoreFocus) {
    const focusTarget = closingMode === "required"
      ? elements.jobsTab
      : state.focusBeforeSourceConfirmation;
    if (focusTarget?.isConnected) focusTarget.focus({ preventScroll: true });
  }
  state.focusBeforeSourceConfirmation = null;
}

async function applyPendingSourceAndGoToJobs() {
  closeSourceConfirmation(false);
  elements.companySelect.focus({ preventScroll: true });
  await applyJobSource({ navigateToJobs: true });
}

function updateQueryControls() {
  const disabled = !state.facetsLoaded || state.isRefreshing || state.sourceMetadataLoading;
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
  elements.applySourceSection.classList.toggle("pending", pending);
  elements.applyLocation.textContent = disabled
    ? elements.companySelect.value ? "Loading Job Source…" : "Select a Company"
    : !hasExplicitSource
      ? "Choose a Location Source"
      : pending
        ? "Apply Job Source"
        : "Source Is Current";
  if (!state.facetsLoaded) return;
  const context = `${new Intl.NumberFormat().format(state.facetMatchingJobs)} jobs in this country context.`;
  elements.facetStatus.textContent = !hasExplicitSource
    ? "Choose at least one location source before applying."
    : pending
      ? `Pending job-source changes · ${context}`
      : `Source matches currently loaded jobs · ${context}`;
}

async function applyJobSource(options = {}) {
  const companyId = elements.companySelect.value;
  const company = companyById(companyId);
  const country = selectedFacet(elements.countrySelect, ALL_COUNTRIES_LABEL);
  const includeAllLocations = elements.includeAllLocations.checked;
  const includeRemote = includeAllLocations && state.remoteLocations.length > 0
    ? true
    : elements.includeRemote.checked;
  const physicalLocations = includeAllLocations ? [] : selectedPendingLocations();
  setLoading(true, { title: `Loading ${company?.displayName || "job-source"} jobs` });
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
    state.hasConfiguredSource = true;
    state.pendingImportedSource = null;
    applySnapshot(snapshot);
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
  } catch (error) {
    showClientError(error);
  }
}

async function refreshJobs() {
  if (!state.hasConfiguredSource) {
    showView("settings", true, { settingsTab: "job-search" });
    elements.facetStatus.textContent =
      "Choose a company and location source, then select Apply Job Source.";
    return;
  }
  setLoading(true, { title: `Refreshing ${state.companyName} jobs` });
  beginRefreshProgressPolling();
  elements.errorBanner.hidden = true;
  try {
    const response = await fetch("/api/refresh", { method: "POST", cache: "no-store" });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "Jobs could not be refreshed."));
    }
    applySnapshot(await response.json());
  } catch (error) {
    showClientError(error);
    await loadSnapshot();
  }
}

function applySnapshot(snapshot) {
  state.jobs = (snapshot.jobs || []).map(job => ({
    ...job,
    descriptionHtml: "",
    descriptionText: "",
    detailLoaded: false
  }));
  state.detailLoadingIds.clear();
  state.descriptionMatches = new Map();
  state.lastRefreshedUtc = snapshot.lastRefreshedUtc;
  state.isCached = Boolean(snapshot.isCached);
  state.newJobIds = new Set(snapshot.newJobIds || []);
  state.jobStates = new Map(Object.entries(snapshot.jobStates || {}));
  state.jobClosures = new Map(Object.entries(snapshot.jobClosures || {}));
  if (state.hasConfiguredSource && snapshot.query) {
    state.companyId = snapshot.query.companyId || "";
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
  refreshDescriptionMatches();
  updateLastRefreshed();
  updateCacheStatus(snapshot);

  if (snapshot.isRefreshing) {
    beginRefreshProgressPolling(true);
  }
  updateQueryControls();
}

function updateCacheStatus(snapshot) {
  const usingCache = Boolean(snapshot.isCached);
  const showCompactStatus = usingCache && !snapshot.isRefreshing;
  elements.cacheStatus.textContent = "Using cache";
  elements.cacheStatus.hidden = !showCompactStatus;

  if (!usingCache || snapshot.isRefreshing) {
    elements.cacheBanner.hidden = true;
    elements.cacheBanner.textContent = "";
    return;
  }

  elements.cacheBanner.textContent = snapshot.error
    ? "Cached jobs remain available because the live refresh failed."
    : "";
  elements.cacheBanner.hidden = !snapshot.error;
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
  refreshDescriptionMatches();
  queueSettingsSave();
  input.focus();
}

function removeKeyword(kind, index) {
  state[kind].splice(index, 1);
  renderChips(kind);
  renderResults();
  refreshDescriptionMatches();
  queueSettingsSave();
}

async function discardPendingSourceAndGoToJobs() {
  if (!state.hasConfiguredSource) return;
  closeSourceConfirmation(false);
  state.pendingImportedSource = null;
  elements.companySelect.value = state.companyId;
  await hydrateSourceControls();
  showView("jobs", true, { bypassSourceGuard: true });
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
        themeMode: state.themeMode,
        userProfile: {
          education: {
            level: state.educationLevel,
            doctorateType: state.doctorateType
          },
          security: {
            clearanceLevel: state.clearanceProfileLevel,
            publicTrust: state.publicTrustProfile
          },
          workAuthorization: {
            usStatus: state.usWorkAuthorizationStatus,
            sponsorship: state.sponsorshipProfile
          },
          credentials: {
            inventoryStatus: state.credentialInventoryStatus,
            heldCredentialIds: state.credentialInventoryStatus === "complete"
              ? Array.from(state.heldCredentialIds).sort()
              : []
          }
        },
        hideStrictEducationMismatch: state.hideStrictEducationMismatch,
        hideStrictClearanceMismatch: state.hideStrictClearanceMismatch,
        hideStrictWorkAuthorizationMismatch: state.hideStrictWorkAuthorizationMismatch,
        excludeStrongExtendedLocationRequirements: state.excludeStrongExtendedLocationRequirements,
        jobFit: {
          enabled: state.jobFitEnabled,
          signals: state.jobFitSignals
            .map(signal => ({ conceptId: signal.conceptId, preference: signal.preference }))
        }
      })
    });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "Settings could not be saved."));
    }
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
    const serverDescriptionMatch = state.descriptionMatches.get(job.stableId);
    const passesInclusion = state.scope === "description"
      ? serverDescriptionMatch !== false
      : inclusionTerms.length === 0 || inclusionTerms.some(term => metadata.includes(term));
    const passesExclusion = state.scope === "description"
      ? serverDescriptionMatch !== false
      : !exclusionTerms.some(term => metadata.includes(term));
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
    const workAuthorizationStatus = evaluateWorkAuthorizationMatch(
      job.workAuthorization, currentWorkAuthorizationProfile());
    const passesWorkAuthorization = !state.hideStrictWorkAuthorizationMismatch ||
      !workAuthorizationStatus.hide;
    const passesExtendedLocationRequirement =
      !state.excludeStrongExtendedLocationRequirements ||
      job.extendedLocationRequirement?.confidence !== "strong";

    return passesInclusion && passesExclusion && passesSalary && passesLocation &&
      passesEducation && passesClearance && passesWorkAuthorization &&
      passesExtendedLocationRequirement;
  });
}

async function refreshDescriptionMatches() {
  const generation = ++state.descriptionMatchGeneration;
  if (state.scope !== "description") {
    state.descriptionMatches = new Map();
    return;
  }
  try {
    const response = await fetch("/api/jobs/description-matches", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        includeKeywords: state.inclusions,
        excludeKeywords: state.exclusions
      })
    });
    if (!response.ok) {
      throw new Error(`Description filtering returned HTTP ${response.status}.`);
    }
    const matches = await response.json();
    if (generation !== state.descriptionMatchGeneration) return;
    state.descriptionMatches = new Map(Object.entries(matches || {}));
    renderResults();
  } catch (error) {
    if (generation !== state.descriptionMatchGeneration) return;
    elements.errorBanner.textContent =
      `Description filtering could not be updated: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  }
}

async function exportWorkspace() {
  elements.portableWorkspaceStatus.textContent = "Preparing workspace backup…";
  elements.exportWorkspaceButton.disabled = true;
  try {
    const response = await fetch("/api/workspace/export", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "The workspace could not be exported."));
    }
    const backup = await response.json();
    const blob = new Blob([`${JSON.stringify(backup, null, 2)}\n`], {
      type: "application/json"
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `job-search-manager-backup-${new Date().toISOString().slice(0, 10)}.json`;
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
    elements.portableWorkspaceStatus.textContent =
      "Workspace backup exported. Keep the JSON file somewhere safe.";
  } catch (error) {
    elements.portableWorkspaceStatus.textContent = error.message || String(error);
  } finally {
    elements.exportWorkspaceButton.disabled = false;
  }
}

async function importWorkspace(event) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  if (!window.confirm(
    "Import this workspace backup? Portable settings and Saved, Applied, Closed, and Hidden states will replace their current values."
  )) return;

  elements.portableWorkspaceStatus.textContent = "Validating workspace backup…";
  elements.importWorkspaceButton.disabled = true;
  elements.exportWorkspaceButton.disabled = true;
  try {
    const response = await fetch("/api/workspace/import", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: await file.text()
    });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "The workspace could not be imported."));
    }
    const imported = await response.json();
    applySettings(imported.settings);
    applySnapshot(imported.snapshot);
    await hydrateSourceControls();
    elements.portableWorkspaceStatus.textContent =
      `Workspace restored with ${new Intl.NumberFormat().format(imported.curatedJobCount || 0)} curated jobs.` +
      (state.pendingImportedSource
        ? " Apply Job Source to activate the imported source selection."
        : "");
  } catch (error) {
    elements.portableWorkspaceStatus.textContent = error.message || String(error);
  } finally {
    elements.importWorkspaceButton.disabled = false;
    elements.exportWorkspaceButton.disabled = false;
  }
}

function showResetConfirmation() {
  clearTimeout(state.resetConfirmationHideTimer);
  state.resetConfirmationOpen = true;
  state.focusBeforeResetConfirmation = document.activeElement;
  elements.resetConfirmationError.hidden = true;
  elements.resetConfirmationError.textContent = "";
  elements.appShell.inert = true;
  elements.resetConfirmationOverlay.hidden = false;
  elements.resetConfirmationOverlay.setAttribute("aria-hidden", "false");
  requestAnimationFrame(() => {
    elements.resetConfirmationOverlay.classList.add("visible");
    elements.resetConfirmationCancel.focus({ preventScroll: true });
  });
}

function closeResetConfirmation(restoreFocus) {
  if (!state.resetConfirmationOpen || state.resetInProgress) return;
  state.resetConfirmationOpen = false;
  elements.resetConfirmationOverlay.classList.remove("visible");
  elements.resetConfirmationOverlay.setAttribute("aria-hidden", "true");
  elements.appShell.inert = state.isRefreshing || state.sourceConfirmationOpen ||
    state.closeApplicationOpen;
  state.resetConfirmationHideTimer = setTimeout(() => {
    if (!state.resetConfirmationOpen) elements.resetConfirmationOverlay.hidden = true;
  }, OVERLAY_TRANSITION_MS);
  if (restoreFocus && state.focusBeforeResetConfirmation?.isConnected) {
    state.focusBeforeResetConfirmation.focus({ preventScroll: true });
  }
  state.focusBeforeResetConfirmation = null;
}

async function resetCurrentWorkspace() {
  if (state.resetInProgress) return;
  state.resetInProgress = true;
  clearTimeout(state.settingsSaveTimer);
  state.settingsSaveTimer = null;
  elements.resetConfirmationCancel.disabled = true;
  elements.resetConfirmationSubmit.disabled = true;
  elements.resetConfirmationSubmit.textContent = "Resetting…";
  elements.resetConfirmationError.hidden = true;
  try {
    const response = await fetch("/api/workspace", { method: "DELETE" });
    if (!response.ok) {
      throw new Error(await apiErrorMessage(response, "The workspace could not be reset."));
    }
    window.location.reload();
  } catch (error) {
    state.resetInProgress = false;
    elements.resetConfirmationCancel.disabled = false;
    elements.resetConfirmationSubmit.disabled = false;
    elements.resetConfirmationSubmit.textContent = "Reset Current Workspace";
    elements.resetConfirmationError.textContent = error.message || String(error);
    elements.resetConfirmationError.hidden = false;
    elements.resetConfirmationCancel.focus({ preventScroll: true });
  }
}

function currentWorkAuthorizationProfile() {
  return {
    usStatus: state.usWorkAuthorizationStatus,
    sponsorship: state.sponsorshipProfile
  };
}

function currentCredentialProfile() {
  return {
    inventoryStatus: state.credentialInventoryStatus,
    heldCredentialIds: Array.from(state.heldCredentialIds)
  };
}

function evaluateWorkAuthorizationMatch(analysis, profile) {
  const eligibility = analysis?.eligibility || "noneSpecified";
  const sponsorship = analysis?.sponsorship || "noneSpecified";
  const strength = analysis?.strength || "none";
  const sponsorshipStrength = analysis?.sponsorshipStrength ||
    (sponsorship !== "noneSpecified" && strength === "strict" ? "strict" : "none");
  const user = {
    usStatus: normalizeUsWorkAuthorizationStatus(profile?.usStatus),
    sponsorship: normalizeSponsorshipProfile(profile?.sponsorship)
  };
  const userLabel = `${usWorkAuthorizationProfileLabel(user.usStatus)}; ${sponsorshipProfileLabel(user.sponsorship)}`;

  if (eligibility === "noneSpecified" && sponsorship === "noneSpecified") {
    return { kind: "noneSpecified", hide: false, userLabel,
      summary: "No work-authorization requirement identified",
      explanation: "The posting does not state a recognized work-authorization or citizenship requirement." };
  }
  let mismatch = false;
  let profileUnknown = false;
  const comparableEligibility = ["usCitizen", "usCitizenOrPermanentResident", "usWorkAuthorized"]
    .includes(eligibility);
  if (strength === "strict" && comparableEligibility) {
    profileUnknown = user.usStatus === "notSpecified";
    const accepted = eligibility === "usCitizen"
      ? ["usCitizen"]
      : eligibility === "usCitizenOrPermanentResident"
        ? ["usCitizen", "permanentResident"]
        : ["usCitizen", "permanentResident", "otherAuthorized"];
    mismatch = !profileUnknown && !accepted.includes(user.usStatus);
  }
  if (sponsorship === "notAvailable" && sponsorshipStrength === "strict") {
    profileUnknown ||= user.sponsorship === "unknown";
    mismatch ||= user.sponsorship === "required";
  }
  if (mismatch) {
    return { kind: "strictMismatch", hide: true, userLabel,
      summary: "Does not meet a strict work-authorization requirement",
      explanation: `The posting states ${workAuthorizationRequirementLabel(analysis).toLocaleLowerCase()}, while your profile reports ${userLabel}.` };
  }
  if (profileUnknown) {
    return { kind: "profileNotConfigured", hide: false, userLabel,
      summary: "Strict requirement; profile not fully configured",
      explanation: "Complete the work-authorization profile in Settings to enable a confident comparison. The job remains visible." };
  }
  const eligibilityNeedsReview = eligibility !== "noneSpecified" &&
    (strength !== "strict" || !comparableEligibility);
  const sponsorshipNeedsReview = sponsorship !== "noneSpecified" && sponsorshipStrength !== "strict";
  if (eligibilityNeedsReview || sponsorshipNeedsReview) {
    return { kind: "review", hide: false, userLabel,
      summary: workAuthorizationRequirementLabel(analysis),
      explanation: "This wording is preferred, conditional, export-related, non-U.S., or otherwise uncertain, so it remains review-only." };
  }
  return { kind: "meets", hide: false, userLabel,
    summary: "Profile meets the detected strict requirement",
    explanation: "Your configured work status is compatible with the posting's detected requirement." };
}

function currentSecurityProfile() {
  return {
    clearanceLevel: state.clearanceProfileLevel,
    publicTrust: state.publicTrustProfile
  };
}

function currentEducationProfile() {
  return { level: state.educationLevel, doctorateType: state.doctorateType };
}

function evaluateClearanceMatch(job, profile) {
  return ClearanceFit.evaluate(job, profile);
}

function evaluateEducationMatch(academic, profile) {
  return EducationFit.evaluate(academic, profile);
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
  const filteredJobs = jobsPassingGeneralFilters();
  const populations = {
    all: filteredJobs.filter(job => JobWorkflowState.belongsToTab(
      job.stableId, "normal", state.jobStates)),
    saved: filteredJobs.filter(job => JobWorkflowState.belongsToTab(
      job.stableId, "saved", state.jobStates)),
    applied: filteredJobs.filter(job => JobWorkflowState.belongsToTab(
      job.stableId, "applied", state.jobStates)),
    closed: filteredJobs.filter(job => JobWorkflowState.belongsToTab(
      job.stableId, "closed", state.jobStates)),
    hidden: filteredJobs.filter(job => JobWorkflowState.belongsToTab(
      job.stableId, "hidden", state.jobStates))
  };
  const jobs = populations[state.activeResultsTab];
  elements.allJobCount.textContent = `(${populations.all.length})`;
  elements.savedJobCount.textContent = `(${populations.saved.length})`;
  elements.appliedJobCount.textContent = `(${populations.applied.length})`;
  elements.closedJobCount.textContent = `(${populations.closed.length})`;
  elements.hiddenJobCount.textContent = `(${populations.hidden.length})`;
  const tabLabel = state.activeResultsTab === "saved"
    ? "saved"
    : state.activeResultsTab === "applied"
      ? "applied"
      : state.activeResultsTab === "closed"
        ? "closed"
        : state.activeResultsTab === "hidden" ? "hidden" : "available";
  elements.resultCount.textContent =
    `Showing ${jobs.length} ${tabLabel} job${jobs.length === 1 ? "" : "s"}`;
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
    if (state.activeResultsTab === "saved") {
      empty.textContent = "No saved jobs are available in the current job source.";
    } else if (state.activeResultsTab === "applied") {
      empty.textContent = "No applied jobs are available in the current job source.";
    } else if (state.activeResultsTab === "closed") {
      empty.textContent = "No closed applications are available in the current job source.";
    } else if (state.activeResultsTab === "hidden") {
      empty.textContent = "No hidden jobs are available in the current job source.";
    }
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
  if (selected && !selected.detailLoaded) {
    loadJobDetail(selected);
  }
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

function evaluateJobFit(job) {
  return JobFit.evaluate(
    job?.detectedConcepts,
    { enabled: state.jobFitEnabled, signals: state.jobFitSignals },
    state.jobFitConcepts);
}

function createJobListItem(job) {
  const card = document.createElement("div");
  card.className = "job-card";
  const workflowState = JobWorkflowState.stateForJob(job.stableId, state.jobStates);
  const isHidden = workflowState === JobWorkflowState.STATES.hidden;
  const isSaved = workflowState === JobWorkflowState.STATES.saved;
  const isApplied = workflowState === JobWorkflowState.STATES.applied;
  const isClosed = workflowState === JobWorkflowState.STATES.closed;
  const closure = state.jobClosures.get(job.stableId);
  JobUnseenState.applyToCard(card, state.newJobIds, job.stableId);
  if (isHidden) {
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
  const dateColumn = document.createElement("span");
  dateColumn.className = "job-date-column";
  const dateIndicators = document.createElement("span");
  dateIndicators.className = "job-date-indicators";
  if (JobUnseenState.isUnseen(state.newJobIds, job.stableId)) {
    const unseenIndicator = document.createElement("span");
    unseenIndicator.className = "job-date-new-indicator";
    unseenIndicator.textContent = "NEW";
    dateIndicators.append(unseenIndicator);
  }
  if (isSaved) {
    const savedBadge = document.createElement("span");
    savedBadge.className = "saved-badge job-date-state-indicator";
    savedBadge.textContent = "Saved";
    dateIndicators.append(savedBadge);
  }
  if (isApplied) {
    const appliedBadge = document.createElement("span");
    appliedBadge.className = "applied-badge job-date-state-indicator";
    appliedBadge.textContent = "Applied";
    dateIndicators.append(appliedBadge);
  }
  if (isClosed) {
    const closedBadge = document.createElement("span");
    closedBadge.className = "closed-badge job-date-state-indicator";
    closedBadge.textContent = "Closed";
    dateIndicators.append(closedBadge);
  }
  if (isHidden) {
    const hiddenBadge = document.createElement("span");
    hiddenBadge.className = "hidden-badge job-date-state-indicator";
    hiddenBadge.textContent = "Hidden";
    dateIndicators.append(hiddenBadge);
  }
  dateColumn.append(date, dateIndicators);

  const title = document.createElement("strong");
  appendHighlightedText(title, job.title || "Untitled job");

  const location = document.createElement("span");
  appendHighlightedText(location, job.primaryLocation || "Location unavailable");

  const requisition = document.createElement("span");
  appendHighlightedText(requisition, job.requisitionId);

  const pay = document.createElement("span");
  pay.className = "job-pay";
  pay.textContent = `Pay: ${formatPay(job)}`;

  button.append(dateColumn, title, location, requisition, pay);
  const badges = document.createElement("span");
  badges.className = "job-badges";
  if (isClosed && closure?.reason) {
    appendJobBadge(
      badges,
      "close-reason-badge",
      closeReasonLabel(closure.reason),
      closure.closedAt ? `Application closed ${formatDateTime(closure.closedAt)}.` : "Application closed."
    );
  }
  const jobFit = evaluateJobFit(job);
  if (jobFit) {
    appendJobBadge(
      badges,
      `job-fit-badge ${JobFit.scoreClass(jobFit.score)}`,
      `Job Fit ${jobFit.score}/10`,
      JobFit.tooltip(jobFit));
  }
  if (job.analysisPending) {
    const pendingBadge = document.createElement("span");
    pendingBadge.className = "analysis-pending-badge";
    pendingBadge.textContent = "Analysis pending";
    pendingBadge.title = "Full-description analysis will be completed in a bounded refresh batch or when this job is opened.";
    badges.append(pendingBadge);
  }
  const workAuthorizationStatus = evaluateWorkAuthorizationMatch(
    job.workAuthorization, currentWorkAuthorizationProfile());
  ClearanceFit.workAuthorizationBadges(job.workAuthorization, workAuthorizationStatus)
    .forEach(badge => appendJobBadge(badges, badge.className, badge.text, badge.title));
  const clearanceStatus = evaluateClearanceMatch(job, currentSecurityProfile());
  ClearanceFit.jobCardBadges(job, clearanceStatus)
    .forEach(badge => appendJobBadge(badges, badge.className, badge.text, badge.title));
  const academicQualification = job.academicQualification;
  const educationStatus = evaluateEducationMatch(academicQualification, currentEducationProfile());
  const educationBadge = EducationFit.jobCardBadge(academicQualification, educationStatus);
  if (educationBadge) {
    appendJobBadge(
      badges,
      educationBadge.className,
      educationBadge.text,
      educationBadge.title
    );
  }
  const credentials = Array.isArray(job.credentials) ? job.credentials : [];
  const unknownCredentials = Array.isArray(job.unknownCredentialRequirements)
    ? job.unknownCredentialRequirements
    : [];
  const credentialFit = CredentialFit.evaluate(
    credentials, unknownCredentials, currentCredentialProfile());
  CredentialFit.jobCardBadges(credentialFit).forEach(badge =>
    appendJobBadge(badges, badge.className, badge.text, badge.title));
  if (job.isRemoteLocationRestricted) {
    appendJobBadge(badges, "restriction-badge", "⚠ Location restricted");
  }
  if (job.remoteWork?.concernLevel && job.remoteWork.concernLevel !== "none") {
    appendJobBadge(
      badges,
      `remote-work-badge ${job.remoteWork.concernLevel}`,
      job.remoteWork.concernLevel === "strong"
        ? "\u26A0 Remote work conflict"
        : "\u26A0 Remote work may be restricted",
      job.remoteWork.summary || "Review the posting's onsite or travel requirements."
    );
  }
  const extendedLocationBadge = ExtendedLocationUi.listBadge(job.extendedLocationRequirement);
  if (extendedLocationBadge) {
    appendJobBadge(
      badges,
      `extended-location-badge ${extendedLocationBadge.confidence}`,
      extendedLocationBadge.text,
      ExtendedLocationUi.requirementLine(job.extendedLocationRequirement)
    );
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

  let dismissButton = null;
  if (!isClosed) {
    dismissButton = document.createElement("button");
    dismissButton.type = "button";
    dismissButton.className = `job-dismiss-button${isHidden ? " restore" : ""}`;
    if (isHidden) {
      dismissButton.append(createRestoreIcon());
    } else {
      dismissButton.append(createTrashCanIcon());
    }
    dismissButton.setAttribute(
      "aria-label",
      isHidden
        ? `Restore job: ${job.title || job.requisitionId || job.stableId}`
        : `Hide this job: ${job.title || job.requisitionId || job.stableId}`);
    dismissButton.title = isHidden ? "Restore job" : "Hide this job";
    dismissButton.addEventListener("click", () => setJobHidden(job, !isHidden));
  }

  let saveButton = null;
  if (!isApplied && !isClosed && !isHidden) {
    saveButton = document.createElement("button");
    saveButton.type = "button";
    saveButton.className = `job-save-button${isSaved ? " saved" : ""}`;
    saveButton.textContent = isSaved ? "\u2605" : "\u2606";
    saveButton.setAttribute("aria-pressed", String(isSaved));
    saveButton.setAttribute(
      "aria-label",
      `${isSaved ? "Unsave" : "Save"} job ${job.requisitionId || job.stableId}`);
    saveButton.title = isSaved ? "Remove from Saved" : "Save this job";
    saveButton.addEventListener("click", () => setJobSaved(job, !isSaved));
  }

  let appliedButton = null;
  if (!isHidden && !isClosed) {
    appliedButton = document.createElement("button");
    appliedButton.type = "button";
    appliedButton.className = `job-applied-button${isApplied ? " applied" : ""}`;
    appliedButton.append(createAppliedIcon());
    appliedButton.setAttribute("aria-pressed", String(isApplied));
    appliedButton.setAttribute(
      "aria-label",
      `${isApplied ? "Mark as not applied" : "Mark as applied"}: ` +
        `${job.title || job.requisitionId || job.stableId}`);
    appliedButton.title = isApplied ? "Mark as not applied" : "Mark as applied";
    appliedButton.addEventListener("click", () => setJobApplied(job, !isApplied));
  }

  let closeButton = null;
  if (isApplied) {
    closeButton = document.createElement("button");
    closeButton.type = "button";
    closeButton.className = "job-close-application-button";
    closeButton.append(createCloseApplicationIcon());
    closeButton.setAttribute(
      "aria-label",
      `Close application: ${job.title || job.requisitionId || job.stableId}`);
    closeButton.title = "Close Application";
    closeButton.addEventListener("click", () => showCloseApplicationModal(job));
  } else if (isClosed) {
    closeButton = document.createElement("button");
    closeButton.type = "button";
    closeButton.className = "job-reopen-application-button";
    closeButton.append(createReopenApplicationIcon());
    closeButton.setAttribute(
      "aria-label",
      `Reopen application: ${job.title || job.requisitionId || job.stableId}`);
    closeButton.title = "Reopen Application";
    closeButton.addEventListener("click", () => reopenApplication(job));
  }

  card.append(button);
  if (saveButton) card.append(saveButton);
  if (appliedButton) card.append(appliedButton);
  if (closeButton) card.append(closeButton);
  if (dismissButton) card.append(dismissButton);
  return card;
}

function appendJobBadge(container, className, text, title = "") {
  const badge = document.createElement("span");
  badge.className = className;
  badge.textContent = text;
  if (title) {
    badge.title = title;
  }
  container.append(badge);
  return badge;
}

function createAppliedIcon() {
  const svgNamespace = "http://www.w3.org/2000/svg";
  const icon = document.createElementNS(svgNamespace, "svg");
  icon.classList.add("job-applied-icon");
  icon.setAttribute("viewBox", "0 0 24 24");
  icon.setAttribute("aria-hidden", "true");
  icon.setAttribute("focusable", "false");
  icon.setAttribute("fill", "none");
  icon.setAttribute("stroke", "currentColor");
  icon.setAttribute("stroke-linecap", "round");
  icon.setAttribute("stroke-linejoin", "round");

  const background = document.createElementNS(svgNamespace, "circle");
  background.classList.add("job-applied-icon-background");
  background.setAttribute("cx", "12");
  background.setAttribute("cy", "12");
  background.setAttribute("r", "9");
  const check = document.createElementNS(svgNamespace, "path");
  check.classList.add("job-applied-icon-check");
  check.setAttribute("d", "m8 12 2.6 2.6L16.5 9");
  icon.append(background, check);
  return icon;
}

function createCloseApplicationIcon() {
  const svgNamespace = "http://www.w3.org/2000/svg";
  const icon = document.createElementNS(svgNamespace, "svg");
  icon.classList.add("job-close-application-icon");
  icon.setAttribute("viewBox", "0 0 24 24");
  icon.setAttribute("aria-hidden", "true");
  icon.setAttribute("focusable", "false");
  icon.setAttribute("fill", "none");
  icon.setAttribute("stroke", "currentColor");
  icon.setAttribute("stroke-linecap", "round");
  icon.setAttribute("stroke-linejoin", "round");
  const archive = document.createElementNS(svgNamespace, "path");
  archive.setAttribute("d", "M4 7h16v13H4zM3 4h18v3H3zM9 11h6");
  const close = document.createElementNS(svgNamespace, "path");
  close.setAttribute("d", "m9 14 6 6m0-6-6 6");
  icon.append(archive, close);
  return icon;
}

function createReopenApplicationIcon() {
  const svgNamespace = "http://www.w3.org/2000/svg";
  const icon = document.createElementNS(svgNamespace, "svg");
  icon.classList.add("job-close-application-icon");
  icon.setAttribute("viewBox", "0 0 24 24");
  icon.setAttribute("aria-hidden", "true");
  icon.setAttribute("focusable", "false");
  icon.setAttribute("fill", "none");
  icon.setAttribute("stroke", "currentColor");
  icon.setAttribute("stroke-linecap", "round");
  icon.setAttribute("stroke-linejoin", "round");
  const arrow = document.createElementNS(svgNamespace, "path");
  arrow.setAttribute("d", "M8 8H4V4M4 8a8 8 0 1 1-1 7M9 12h6");
  icon.append(arrow);
  return icon;
}

function createTrashCanIcon() {
  const svgNamespace = "http://www.w3.org/2000/svg";
  const icon = document.createElementNS(svgNamespace, "svg");
  icon.classList.add("job-dismiss-icon");
  icon.setAttribute("viewBox", "0 0 24 24");
  icon.setAttribute("aria-hidden", "true");
  icon.setAttribute("focusable", "false");
  icon.setAttribute("fill", "none");
  icon.setAttribute("stroke", "currentColor");
  icon.setAttribute("stroke-linecap", "round");
  icon.setAttribute("stroke-linejoin", "round");

  const lid = document.createElementNS(svgNamespace, "path");
  lid.setAttribute("d", "M4 7h16M9 7V4h6v3");
  const bin = document.createElementNS(svgNamespace, "path");
  bin.setAttribute("d", "M6.5 7l1 13h9l1-13M10 11v5M14 11v5");
  icon.append(lid, bin);
  return icon;
}

function createRestoreIcon() {
  const svgNamespace = "http://www.w3.org/2000/svg";
  const icon = document.createElementNS(svgNamespace, "svg");
  icon.classList.add("job-dismiss-icon");
  icon.setAttribute("viewBox", "0 0 24 24");
  icon.setAttribute("aria-hidden", "true");
  icon.setAttribute("focusable", "false");
  icon.setAttribute("fill", "none");
  icon.setAttribute("stroke", "currentColor");
  icon.setAttribute("stroke-linecap", "round");
  icon.setAttribute("stroke-linejoin", "round");

  const arrow = document.createElementNS(svgNamespace, "path");
  arrow.setAttribute("d", "M9 7H4V2M4 7a9 9 0 1 1-1 8");
  icon.append(arrow);
  return icon;
}

async function setJobSaved(job, saved) {
  await setJobWorkflowState(
    job,
    saved ? JobWorkflowState.STATES.saved : JobWorkflowState.STATES.normal,
    "Saved");
}

async function setJobApplied(job, applied) {
  await setJobWorkflowState(
    job,
    applied ? JobWorkflowState.STATES.applied : JobWorkflowState.STATES.normal,
    "Applied");
}

async function setJobHidden(job, hidden) {
  await setJobWorkflowState(
    job,
    hidden ? JobWorkflowState.STATES.hidden : JobWorkflowState.STATES.normal,
    "Hidden");
}

const CLOSE_REASON_LABELS = Object.freeze({
  PositionWithdrawn: "Position Withdrawn",
  NotSelected: "Not Selected",
  ScreenedOut: "Screened Out",
  InterviewedOut: "Interviewed Out",
  Ghosted: "Ghosted",
  Withdrew: "Withdrew",
  Other: "Other"
});

function closeReasonLabel(reason) {
  return CLOSE_REASON_LABELS[reason] || "Other";
}

function showCloseApplicationModal(job) {
  if (JobWorkflowState.stateForJob(job.stableId, state.jobStates) !==
      JobWorkflowState.STATES.applied) return;
  clearTimeout(state.closeApplicationHideTimer);
  state.closeApplicationOpen = true;
  state.closeApplicationJobId = job.stableId;
  state.focusBeforeCloseApplication = document.activeElement;
  elements.closeApplicationCopy.textContent =
    `Choose why the application for ${job.title || job.requisitionId || "this job"} is closed.`;
  elements.closeApplicationReason.value = "PositionWithdrawn";
  elements.closeApplicationError.hidden = true;
  elements.closeApplicationError.textContent = "";
  elements.appShell.inert = true;
  elements.closeApplicationOverlay.hidden = false;
  elements.closeApplicationOverlay.setAttribute("aria-hidden", "false");
  requestAnimationFrame(() => {
    elements.closeApplicationOverlay.classList.add("visible");
    elements.closeApplicationReason.focus({ preventScroll: true });
  });
}

function closeCloseApplicationModal(restoreFocus) {
  if (!state.closeApplicationOpen || state.closeApplicationInProgress) return;
  state.closeApplicationOpen = false;
  state.closeApplicationJobId = null;
  elements.closeApplicationOverlay.classList.remove("visible");
  elements.closeApplicationOverlay.setAttribute("aria-hidden", "true");
  elements.appShell.inert = state.isRefreshing || state.sourceMetadataLoading ||
    state.sourceConfirmationOpen || state.resetConfirmationOpen;
  state.closeApplicationHideTimer = setTimeout(() => {
    if (!state.closeApplicationOpen) elements.closeApplicationOverlay.hidden = true;
  }, OVERLAY_TRANSITION_MS);
  if (restoreFocus && state.focusBeforeCloseApplication?.isConnected) {
    state.focusBeforeCloseApplication.focus({ preventScroll: true });
  }
  state.focusBeforeCloseApplication = null;
}

async function confirmCloseApplication() {
  if (state.closeApplicationInProgress) return;
  const job = state.jobs.find(item => item.stableId === state.closeApplicationJobId);
  const reason = elements.closeApplicationReason.value;
  if (!job || !CLOSE_REASON_LABELS[reason]) return;
  state.closeApplicationInProgress = true;
  elements.closeApplicationCancel.disabled = true;
  elements.closeApplicationSubmit.disabled = true;
  elements.closeApplicationSubmit.textContent = "Closing…";
  elements.closeApplicationError.hidden = true;
  const updated = await setJobWorkflowState(
    job, JobWorkflowState.STATES.closed, "Closed", reason);
  state.closeApplicationInProgress = false;
  elements.closeApplicationCancel.disabled = false;
  elements.closeApplicationSubmit.disabled = false;
  elements.closeApplicationSubmit.textContent = "Close Application";
  if (updated) {
    closeCloseApplicationModal(false);
  } else {
    elements.closeApplicationError.textContent =
      "The application could not be closed. Review the error banner and try again.";
    elements.closeApplicationError.hidden = false;
    elements.closeApplicationCancel.focus({ preventScroll: true });
  }
}

async function reopenApplication(job) {
  await setJobWorkflowState(job, JobWorkflowState.STATES.applied, "Reopen application");
}

async function setJobWorkflowState(job, nextState, label, closeReason = null) {
  const previousState = JobWorkflowState.stateForJob(job.stableId, state.jobStates);
  const previousClosure = state.jobClosures.get(job.stableId);
  state.jobStates.set(job.stableId, nextState);
  if (nextState === JobWorkflowState.STATES.closed) {
    state.jobClosures.set(job.stableId, {
      reason: closeReason,
      closedAt: new Date().toISOString(),
      appliedAt: previousClosure?.appliedAt || null
    });
  } else {
    state.jobClosures.delete(job.stableId);
  }
  renderResults();

  try {
    const response = await fetch("/api/history/workflow-state", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stableId: job.stableId, state: nextState, closeReason }),
      keepalive: true
    });
    if (!response.ok) {
      throw new Error(`${label}-state update returned HTTP ${response.status}.`);
    }
    return true;
  } catch (error) {
    state.jobStates.set(job.stableId, previousState);
    if (previousClosure) state.jobClosures.set(job.stableId, previousClosure);
    else state.jobClosures.delete(job.stableId);
    renderResults();
    elements.errorBanner.textContent = `${label} state could not be updated: ${error.message || error}`;
    elements.errorBanner.hidden = false;
    return false;
  }
}

async function markJobViewed(job) {
  if (!JobUnseenState.markViewed(state.newJobIds, job.stableId)) {
    return;
  }

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
    JobUnseenState.restoreUnseen(state.newJobIds, job.stableId);
    renderResults();
    elements.errorBanner.textContent = `Viewed state could not be saved: ${error.message || error}`;
    elements.errorBanner.hidden = false;
  }
}

async function loadJobDetail(job) {
  if (state.detailLoadingIds.has(job.stableId) || job.detailLoaded) return;
  state.detailLoadingIds.add(job.stableId);
  try {
    const response = await fetch(
      `/api/jobs/detail?stableId=${encodeURIComponent(job.stableId)}`,
      { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Job detail returned HTTP ${response.status}.`);
    }
    const detail = await response.json();
    const index = state.jobs.findIndex(item => item.stableId === job.stableId);
    if (index >= 0) {
      state.jobs[index] = {
        ...state.jobs[index],
        ...detail,
        descriptionText: descriptionToText(detail.descriptionHtml || ""),
        detailLoaded: true
      };
    }
    if (state.scope === "description") {
      await refreshDescriptionMatches();
    } else {
      renderResults();
    }
  } catch (error) {
    const index = state.jobs.findIndex(item => item.stableId === job.stableId);
    if (index >= 0) {
      state.jobs[index] = {
        ...state.jobs[index],
        detailLoaded: true,
        detailError: error.message || String(error)
      };
    }
    renderResults();
  } finally {
    state.detailLoadingIds.delete(job.stableId);
  }
}

function formatJobFitImpact(value) {
  const normalized = Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1);
  return `${value >= 0 ? "+" : ""}${normalized}`;
}

function appendJobFitSignal(container, signal, options = {}) {
  const row = document.createElement("article");
  row.className = "job-fit-breakdown-signal";
  const heading = document.createElement("div");
  heading.className = "job-fit-breakdown-signal-heading";
  const name = document.createElement("strong");
  name.textContent = signal.displayName;
  const preference = document.createElement("span");
  preference.className = "job-fit-breakdown-preference";
  preference.textContent = signal.preferenceLabel;
  heading.append(name, preference);
  row.append(heading);

  if (options.supersededBy?.length) {
    const note = document.createElement("p");
    note.className = "job-fit-breakdown-note";
    note.textContent = `Excluded from scoring because ${options.supersededBy.join(", ")} superseded it.`;
    row.append(note);
  } else if (signal.preference !== "neutral") {
    const impact = document.createElement("p");
    impact.className = "job-fit-breakdown-note";
    impact.textContent = `Signal impact before category bounds: ${formatJobFitImpact(signal.impact)}`;
    row.append(impact);
  }

  if (signal.evidence) {
    const evidence = document.createElement("details");
    evidence.className = "evidence-disclosure job-fit-evidence";
    const summary = document.createElement("summary");
    summary.textContent = "Show evidence";
    const text = document.createElement("p");
    text.textContent = `“${signal.evidence}”`;
    evidence.append(summary, text);
    row.append(evidence);
  }
  container.append(row);
}

function appendJobFitSignalGroup(container, title, signals, options = {}) {
  if (!signals.length) return;
  const heading = document.createElement("h4");
  heading.textContent = title;
  container.append(heading);
  const list = document.createElement("div");
  list.className = "job-fit-breakdown-signals";
  signals.forEach(signal => appendJobFitSignal(list, signal, {
    supersededBy: options.superseded ? signal.supersededBy : null
  }));
  container.append(list);
}

function appendJobFitCalculationRow(list, label, value, className = "") {
  const row = document.createElement("div");
  if (className) row.className = className;
  const term = document.createElement("dt");
  term.textContent = label;
  const definition = document.createElement("dd");
  definition.textContent = value;
  row.append(term, definition);
  list.append(row);
}

function renderJobFitDetail(result) {
  elements.jobFitDetailTab.hidden = !result;
  elements.jobFitDetailContent.replaceChildren();
  if (!result) {
    if (state.detailTab === "fit") state.detailTab = "glance";
    return;
  }

  const scoreSection = document.createElement("section");
  scoreSection.className = "job-fit-detail-section job-fit-score-section";
  const scoreHeading = document.createElement("h3");
  scoreHeading.textContent = "Job Fit";
  const score = document.createElement("strong");
  score.className = `job-fit-detail-score ${JobFit.scoreClass(result.score)}`;
  score.textContent = `${result.score} / 10`;
  const scoreSummary = document.createElement("p");
  scoreSummary.textContent = result.contributions.length
    ? `Baseline ${result.baseline} plus bounded category contributions produced this score.`
    : `No configured Job Fit preferences matched this posting, so the score remains at the ${result.baseline} baseline.`;
  scoreSection.append(scoreHeading, score, scoreSummary);
  elements.jobFitDetailContent.append(scoreSection);

  if (result.hardConflictCap.signals.length) {
    const conflict = document.createElement("section");
    conflict.className = "job-fit-detail-section job-fit-hard-conflict";
    const heading = document.createElement("h3");
    heading.textContent = "Hard Conflict";
    const explanation = document.createElement("p");
    explanation.textContent = result.hardConflictCap.applied
      ? `The calculated score was ${result.scoreBeforeHardConflictCap}/10. A Hard Conflict capped the final score at ${result.hardConflictCap.maximum}/10.`
      : `A Hard Conflict was detected, but the calculated score was already ${result.scoreBeforeHardConflictCap}/10, so the ${result.hardConflictCap.maximum}/10 cap did not lower it further.`;
    conflict.append(heading, explanation);
    appendJobFitSignalGroup(conflict, "Triggering signals", result.hardConflictCap.signals);
    elements.jobFitDetailContent.append(conflict);
  }

  const categorySection = document.createElement("section");
  categorySection.className = "job-fit-detail-section";
  const categoryHeading = document.createElement("h3");
  categoryHeading.textContent = "Category Breakdown";
  categorySection.append(categoryHeading);
  const categories = document.createElement("div");
  categories.className = "job-fit-breakdown-categories";
  result.dimensionBreakdown.forEach(dimension => {
    const relevantCount = dimension.signals.length +
      dimension.neutralSignals.length + dimension.supersededSignals.length;
    const category = document.createElement("details");
    category.className = "job-fit-breakdown-category";
    category.open = relevantCount > 0;
    const summary = document.createElement("summary");
    const name = document.createElement("span");
    name.textContent = dimension.category;
    const contribution = document.createElement("strong");
    contribution.textContent = formatJobFitImpact(dimension.impact);
    summary.append(name, contribution);
    const body = document.createElement("div");
    body.className = "job-fit-breakdown-category-body";
    const arithmetic = document.createElement("dl");
    arithmetic.className = "job-fit-category-arithmetic";
    appendJobFitCalculationRow(arithmetic, "Raw contribution", formatJobFitImpact(dimension.rawImpact));
    appendJobFitCalculationRow(
      arithmetic,
      dimension.capped ? "After category cap" : "Category contribution",
      formatJobFitImpact(dimension.impact),
      dimension.capped ? "capped" : "");
    if (dimension.capped) {
      appendJobFitCalculationRow(
        arithmetic,
        "Allowed range",
        `${formatJobFitImpact(dimension.limits.minimum)} to ${formatJobFitImpact(dimension.limits.maximum)}`);
    }
    body.append(arithmetic);
    appendJobFitSignalGroup(body, "Contributing signals", dimension.signals);
    appendJobFitSignalGroup(body, "Superseded — not counted", dimension.supersededSignals, {
      superseded: true
    });
    appendJobFitSignalGroup(body, "Detected but Neutral", dimension.neutralSignals);
    if (relevantCount === 0) {
      const empty = document.createElement("p");
      empty.className = "job-fit-breakdown-empty";
      empty.textContent = "No relevant canonical concepts were detected in this category.";
      body.append(empty);
    }
    category.append(summary, body);
    categories.append(category);
  });
  categorySection.append(categories);
  elements.jobFitDetailContent.append(categorySection);

  const calculation = document.createElement("section");
  calculation.className = "job-fit-detail-section";
  const calculationHeading = document.createElement("h3");
  calculationHeading.textContent = "Final Calculation";
  const rows = document.createElement("dl");
  rows.className = "job-fit-final-calculation";
  appendJobFitCalculationRow(rows, "Baseline", result.baseline.toFixed(1));
  result.dimensionBreakdown.forEach(dimension => appendJobFitCalculationRow(
    rows, dimension.category, formatJobFitImpact(dimension.impact)));
  appendJobFitCalculationRow(rows, "Calculated total", result.calculatedTotal.toFixed(1), "calculation-total");
  appendJobFitCalculationRow(
    rows,
    "Rounded / score bounds",
    `${result.scoreBeforeHardConflictCap}/10`);
  if (result.hardConflictCap.applied) {
    appendJobFitCalculationRow(rows, "Hard Conflict cap", `${result.hardConflictCap.maximum}/10`);
  }
  appendJobFitCalculationRow(rows, "Final Job Fit", `${result.score}/10`, "calculation-final");
  calculation.append(calculationHeading, rows);
  elements.jobFitDetailContent.append(calculation);
}

function renderDetail(job) {
  elements.emptyDetail.hidden = Boolean(job);
  elements.jobDetail.hidden = !job;
  if (!job) {
    state.renderedDetailJobId = null;
    resetCopyFeedback();
    elements.copyPostingButton.disabled = true;
    elements.detailDescription.replaceChildren();
    return;
  }

  if (state.renderedDetailJobId !== job.stableId) {
    state.renderedDetailJobId = job.stableId;
    state.detailTab = "glance";
    resetCopyFeedback();
    document.querySelector(".detail-pane").scrollTop = 0;
  }
  renderJobFitDetail(evaluateJobFit(job));
  showDetailTab(state.detailTab);

  // All metadata uses textContent. Only the description fragment is inserted as HTML,
  // and only after a strict DOMPurify allowlist has removed executable content.
  replaceWithHighlightedText(elements.detailTitle, job.title);
  replaceWithHighlightedText(elements.detailRequisition, job.requisitionId);
  const detailWorkflowState = JobWorkflowState.stateForJob(job.stableId, state.jobStates);
  elements.detailSavedBadge.hidden = detailWorkflowState !== JobWorkflowState.STATES.saved;
  elements.detailAppliedBadge.hidden = detailWorkflowState !== JobWorkflowState.STATES.applied;
  const detailClosure = state.jobClosures.get(job.stableId);
  elements.detailClosedBadge.hidden = detailWorkflowState !== JobWorkflowState.STATES.closed;
  elements.detailCloseReasonBadge.hidden = detailWorkflowState !== JobWorkflowState.STATES.closed ||
    !detailClosure?.reason;
  elements.detailCloseReasonBadge.textContent = detailClosure?.reason
    ? closeReasonLabel(detailClosure.reason)
    : "";
  elements.detailHiddenBadge.hidden = detailWorkflowState !== JobWorkflowState.STATES.hidden;
  elements.copyPostingButton.disabled = !job.descriptionHtml;
  if (!job.descriptionHtml) {
    resetCopyFeedback();
    elements.copyPostingButton.title = "Full job posting is unavailable";
  }
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
  const credentialStatus = CredentialFit.evaluate(
    job.credentials,
    job.unknownCredentialRequirements,
    currentCredentialProfile());
  renderCredentials(job, credentialStatus);
  const workAuthorizationStatus = evaluateWorkAuthorizationMatch(
    job.workAuthorization, currentWorkAuthorizationProfile());
  renderWorkAuthorization(job.workAuthorization, workAuthorizationStatus);

  elements.detailEducationMismatch.hidden = educationStatus.kind !== "strictMismatch";
  elements.detailEducationMismatchText.textContent = educationStatus.kind === "strictMismatch"
    ? educationStatus.explanation
    : "";
  elements.detailClearanceMismatch.hidden = clearanceStatus.kind !== "strictMismatch";
  elements.detailClearanceMismatchText.textContent = clearanceStatus.kind === "strictMismatch"
    ? clearanceStatus.explanation
    : "";
  elements.detailWorkAuthorizationMismatch.hidden = workAuthorizationStatus.kind !== "strictMismatch";
  elements.detailWorkAuthorizationMismatchText.textContent =
    workAuthorizationStatus.kind === "strictMismatch" ? workAuthorizationStatus.explanation : "";

  const credentialBlockers = credentialStatus.blockers.length;
  const credentialReviews = credentialStatus.reviews.length;
  elements.detailCredentialNote.hidden = credentialBlockers === 0 && credentialReviews === 0;
  elements.detailCredentialNote.className =
    `summary-note ${credentialBlockers ? "mismatch" : "caution"}`;
  elements.detailCredentialNoteTitle.textContent = credentialBlockers
    ? "Credential mismatch"
    : "Credential review";
  elements.detailCredentialNoteText.textContent = credentialBlockers
    ? `${credentialBlockers} required credential${credentialBlockers === 1 ? " is" : "s are"} not present in your configured inventory.`
    : credentialReviews
      ? `${credentialReviews} required credential${credentialReviews === 1 ? " needs" : "s need"} review because your status or an accepted equivalent cannot be confirmed.`
      : "";

  elements.detailLocationNote.hidden = !job.isRemoteLocationRestricted;
  elements.detailLocationNoteText.textContent = job.isRemoteLocationRestricted
    ? job.remoteLocationRestrictionSnippet || "The description contains a geographic restriction for remote work."
    : "";

  const remoteWorkConcern = job.remoteWork?.concernLevel;
  const hasRemoteWorkConcern = remoteWorkConcern === "questionable" || remoteWorkConcern === "strong";
  elements.detailRemoteWorkNote.hidden = !hasRemoteWorkConcern;
  elements.detailRemoteWorkNote.className = `summary-note remote-work ${remoteWorkConcern || "questionable"}`;
  elements.detailRemoteWorkNoteTitle.textContent = remoteWorkConcern === "strong"
    ? "Remote work conflict"
    : "Remote work warning";
  elements.detailRemoteWorkNoteText.textContent = hasRemoteWorkConcern
    ? job.remoteWork.summary || "The description includes onsite, field, or travel requirements worth reviewing."
    : "";

  const extendedLocation = job.extendedLocationRequirement;
  const hasExtendedLocationRequirement = extendedLocation?.confidence === "strong" ||
    extendedLocation?.confidence === "questionable";
  elements.detailExtendedLocationRequirement.hidden = !hasExtendedLocationRequirement;
  elements.detailExtendedLocationRequirement.className =
    `summary-section extended-location-requirement ${extendedLocation?.confidence || "questionable"}`;
  elements.detailExtendedLocationDestination.textContent = hasExtendedLocationRequirement
    ? ExtendedLocationUi.destinationDisplay(extendedLocation)
    : "";
  elements.detailExtendedLocationSummary.textContent = hasExtendedLocationRequirement
    ? ExtendedLocationUi.requirementLine(extendedLocation)
    : "";
  elements.detailExtendedLocationEvidence.replaceChildren();
  if (hasExtendedLocationRequirement) {
    (extendedLocation.signals || []).forEach(signal => {
      const evidence = document.createElement("p");
      evidence.textContent = signal.evidence;
      elements.detailExtendedLocationEvidence.append(evidence);
    });
  }

  const headroom = calculateSalaryHeadroom(job);
  elements.detailHeadroomNote.hidden = !headroom?.isLimited;
  elements.detailHeadroomNoteText.textContent = headroom?.isLimited
    ? `Your ${formatCurrency(state.minimumSalary)} minimum is near the top of this job's ` +
      `${formatCurrency(job.payMinimum)} – ${formatCurrency(job.payMaximum)} advertised hiring range. ` +
      `Your minimum is approximately ${Math.round(headroom.position * 100)}% through the posted range; ` +
      `negotiating room may be limited.`
    : "";

  renderQualificationFit(
    job, educationStatus, clearanceStatus, workAuthorizationStatus, credentialStatus, headroom);

  elements.sourcePostingLink.href = safeHttpUrl(job.sourceUrl) || "#";
  elements.sourcePostingLink.hidden = !safeHttpUrl(job.sourceUrl);

  elements.detailWarning.hidden = !job.detailError;
  elements.detailWarning.textContent = job.detailError
    ? `Full details could not be retrieved for this job: ${job.detailError}`
    : "";
  elements.detailFlags.hidden = !(
    educationStatus.kind === "strictMismatch" ||
    clearanceStatus.kind === "strictMismatch" ||
    workAuthorizationStatus.kind === "strictMismatch" ||
    credentialBlockers > 0 ||
    credentialReviews > 0 ||
    job.isRemoteLocationRestricted ||
    hasRemoteWorkConcern ||
    headroom?.isLimited ||
    job.detailError);

  if (!job.descriptionHtml) {
    elements.detailDescription.textContent = job.detailLoaded
      ? "The formatted description is unavailable for this job."
      : "Loading the full posting from the server-side cache...";
    return;
  }

  const normalizedHtml = JobPostingText.normalizeHtml(job.descriptionHtml);
  const cleanHtml = DOMPurify.sanitize(normalizedHtml, {
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

async function copySelectedJobPosting() {
  const job = state.jobs.find(item => item.stableId === state.selectedJobId);
  const postingText = job?.descriptionHtml
    ? JobPostingText.toPlainText(elements.detailDescription)
    : "";
  if (!postingText) {
    showCopyFeedback(
      "Copy failed",
      "Full job posting is unavailable",
      "The full job posting could not be copied because it is unavailable.");
    return;
  }

  const copied = await ClipboardText.copyText(postingText);
  if (copied) {
    showCopyFeedback("Copied", "Copied full job posting", "Full job posting copied to clipboard.");
  } else {
    showCopyFeedback(
      "Copy failed",
      "Copy failed — try again",
      "The full job posting could not be copied. Check browser clipboard permission and try again.");
  }
}

function showCopyFeedback(label, title, status) {
  if (state.copyFeedbackTimer) {
    clearTimeout(state.copyFeedbackTimer);
  }
  elements.copyPostingLabel.textContent = label;
  elements.copyPostingButton.title = title;
  elements.copyPostingStatus.textContent = status;
  state.copyFeedbackTimer = setTimeout(resetCopyFeedback, COPY_FEEDBACK_MS);
}

function resetCopyFeedback() {
  if (state.copyFeedbackTimer) {
    clearTimeout(state.copyFeedbackTimer);
    state.copyFeedbackTimer = null;
  }
  elements.copyPostingLabel.textContent = "Copy posting";
  elements.copyPostingButton.title = "Copy full job posting";
  elements.copyPostingStatus.textContent = "";
}

function renderQualificationFit(
  job, educationStatus, clearanceStatus, workAuthorizationStatus, credentialStatus, headroom) {
  const blockers = [];
  if (clearanceStatus.kind === "strictMismatch") blockers.push("Clearance mismatch");
  if (educationStatus.kind === "strictMismatch") blockers.push("Education mismatch");
  if (workAuthorizationStatus.kind === "strictMismatch") blockers.push("Work authorization mismatch");
  if (credentialStatus.blockers.length) blockers.push("Credential mismatch");

  const notes = [];
  if (job.isRemoteLocationRestricted) notes.push("remote-location condition");
  if (["questionable", "strong"].includes(job.remoteWork?.concernLevel)) {
    notes.push("remote-work warning");
  }
  if (headroom?.isLimited) notes.push("limited salary headroom");
  if (job.detailError) notes.push("incomplete job details");
  if (clearanceStatus.kind === "meetsLevelPolygraphReview") notes.push("polygraph review");
  if (["uncertain", "profileNotConfigured"].includes(clearanceStatus.kind)) {
    notes.push("clearance review");
  }
  if (["uncertain", "specificDegreeUncertain", "profileNotConfigured"].includes(educationStatus.kind)) {
    notes.push("education review");
  }
  if (["review", "profileNotConfigured"].includes(workAuthorizationStatus.kind)) {
    notes.push("work-authorization review");
  }
  if (credentialStatus.reviews.length) notes.push("credential review");

  elements.detailFitSummary.className = `fit-summary ${blockers.length ? "blocker" : notes.length ? "review" : "compatible"}`;
  elements.detailFitTitle.textContent = blockers.length
    ? `${blockers.length} potential blocker${blockers.length === 1 ? "" : "s"}`
    : credentialStatus.reviews.length
      ? "Credential status needs review"
      : "No confirmed strict blockers";

  const parts = [];
  if (blockers.length) parts.push(blockers.join(" · "));
  if (notes.length) parts.push(`${notes.length} item${notes.length === 1 ? "" : "s"} to review`);
  if (!parts.length) parts.push("Based on the configured profile and confidently parsed requirements.");
  elements.detailFitText.textContent = parts.join(" · ");
}

function renderWorkAuthorization(analysis, status) {
  const present = analysis &&
    (analysis.eligibility !== "noneSpecified" || analysis.sponsorship !== "noneSpecified");
  elements.detailWorkAuthorization.hidden = !present;
  if (!present) return;

  elements.detailWorkAuthorizationRequirement.textContent = workAuthorizationRequirementLabel(analysis);
  elements.detailUserWorkAuthorization.textContent = status.userLabel;
  elements.detailWorkAuthorizationComparison.textContent = status.summary;
  elements.detailWorkAuthorizationComparison.className =
    `work-authorization-profile-status ${status.kind}`;
  const evidence = Array.isArray(analysis.evidence) ? analysis.evidence.filter(Boolean) : [];
  elements.detailWorkAuthorizationEvidence.hidden = evidence.length === 0;
  elements.detailWorkAuthorizationEvidence.open = false;
  elements.detailWorkAuthorizationEvidenceText.replaceChildren();
  evidence.forEach((item, index) => {
    if (index > 0) elements.detailWorkAuthorizationEvidenceText.append(document.createElement("br"));
    elements.detailWorkAuthorizationEvidenceText.append(document.createTextNode(`“${item}”`));
  });
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

function usWorkAuthorizationProfileLabel(status) {
  return ({
    notSpecified: "U.S. work status not configured",
    usCitizen: "U.S. citizen",
    permanentResident: "U.S. permanent resident / Green Card holder",
    otherAuthorized: "Other U.S. work authorization",
    notAuthorized: "Not authorized to work in the U.S."
  })[status] || "U.S. work status not configured";
}

function sponsorshipProfileLabel(status) {
  return ({
    unknown: "sponsorship need unknown",
    notRequired: "no sponsorship required",
    required: "sponsorship required"
  })[status] || "sponsorship need unknown";
}

function workAuthorizationRequirementLabel(analysis) {
  const eligibility = ({
    noneSpecified: "",
    usCitizen: "U.S. citizenship required",
    usCitizenOrPermanentResident: "U.S. citizen or permanent resident required",
    usWorkAuthorized: "U.S. work authorization required",
    locationWorkAuthorized: "Valid work rights for the job location required — review for jurisdiction",
    usPerson: "U.S.-person language — review required",
    australianCitizen: "Australian citizenship language — review required",
    exportControlled: "Export-control language — review required",
    ambiguousCitizenship: "Citizenship language — country or requirement unclear"
  })[analysis?.eligibility] || "Work-authorization language requires review";
  const sponsor = analysis?.sponsorship === "notAvailable" ? "No employment sponsorship" : "";
  const requirement = [eligibility, sponsor].filter(Boolean).join("; ");
  if (analysis?.strength === "preferred") return `${requirement} (preferred)`;
  return requirement || "Work-authorization language requires review";
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
  if (hasAcademic && Array.isArray(academic.accreditations) && academic.accreditations.length) {
    rows.append(createSummaryRow("Accreditation", academic.accreditations
      .map(item => `${item.name} (${academicRequirementLabel(item.requirement)})`)
      .join(", ")));
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

function renderCredentials(job, credentialStatus) {
  const credentials = Array.isArray(job.credentials) ? job.credentials : [];
  const unknown = Array.isArray(job.unknownCredentialRequirements)
    ? job.unknownCredentialRequirements
    : [];
  elements.detailCredentials.hidden = credentials.length === 0 && unknown.length === 0;
  elements.detailCredentialsList.replaceChildren();

  credentialDisplayRows(credentials).forEach(({ credential, members }, index) => {
    const item = document.createElement("div");
    item.className = "credential-row";
    const name = document.createElement("strong");
    name.textContent = credential.name;
    const status = document.createElement("span");
    status.className = `summary-status credential${credential.requirement === "required" ? " required" : ""}`;
    const assessment = credentialStatus.assessments.find(item =>
      CredentialFit.assessmentCredentials(item).some(member => members.includes(member)));
    status.textContent = credentialRequirementLabel(credential, assessment);

    const identity = document.createElement("p");
    identity.className = "disclosure-metadata";
    identity.textContent = [
      credential.fullName,
      credential.issuer,
      credentialTypeLabel(credential.type),
      credential.category,
      credential.family
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

  unknown.forEach((credential, index) => {
    const item = document.createElement("div");
    item.className = "credential-row";
    const name = document.createElement("strong");
    name.textContent = credential.name || "Unrecognized credential";
    const status = document.createElement("span");
    status.className = "summary-status credential required";
    status.textContent = "Required · unrecognized; review";
    const details = document.createElement("details");
    details.className = "inline-details";
    details.id = `unknown-credential-details-${index}`;
    const summary = document.createElement("summary");
    summary.textContent = "Details";
    summary.setAttribute("aria-controls", details.id);
    const explanation = document.createElement("p");
    explanation.className = "disclosure-metadata";
    explanation.textContent = credential.equivalentAccepted
      ? "The posting explicitly requires this credential or an equivalent, but it is not yet in the catalog. No equivalence was inferred."
      : "The posting explicitly requires this credential, but it is not yet in the catalog.";
    details.append(summary, explanation);
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

function credentialDisplayRows(credentials) {
  const rows = [];
  const renderedGroups = new Set();
  credentials.forEach(credential => {
    const groupId = credential.alternativeGroup;
    const members = groupId
      ? credentials.filter(candidate => candidate.alternativeGroup === groupId)
      : [credential];
    if (groupId && members.length > 1) {
      if (renderedGroups.has(groupId)) return;
      renderedGroups.add(groupId);
      const distinct = key => [...new Set(members.map(member => member[key]).filter(Boolean))];
      rows.push({
        members,
        credential: {
          ...members[0],
          name: distinct("name").join(" or "),
          fullName: distinct("fullName").join(" / "),
          issuer: distinct("issuer").join(" / "),
          category: distinct("category").join(" / "),
          family: distinct("family").join(" / "),
          evidence: distinct("evidence").join(" "),
          isAlternative: true
        }
      });
      return;
    }
    rows.push({ credential, members: [credential] });
  });
  return rows;
}

function credentialRequirementLabel(credential, assessment) {
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
  if (assessment?.kind === "meets") labels.push("held");
  if (assessment?.kind === "strictMismatch") labels.push("not in your inventory");
  if (assessment?.kind === "review") labels.push("status needs review");
  if (assessment?.kind === "nonBlocking") {
    labels.push(assessment.reason === "profileNotConfiguredNonBlocking"
      ? "status unknown"
      : "not held");
  }
  return labels.join(" · ");
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
    elements.appShell.inert = state.sourceMetadataLoading ||
      state.sourceConfirmationOpen || state.resetConfirmationOpen || state.closeApplicationOpen;
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

function formatDateTime(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleString();
}

function beginSourceMetadataLoad(companyId) {
  state.sourceAbortController?.abort();
  const controller = new AbortController();
  const request = {
    generation: ++state.sourceRequestGeneration,
    companyId,
    signal: controller.signal
  };
  state.sourceAbortController = controller;
  state.sourceMetadataLoading = true;
  clearTimeout(state.sourceOverlayShowTimer);
  clearTimeout(state.sourceOverlayHideTimer);
  const company = companyById(companyId);
  elements.sourceLoadingTitle.textContent =
    `Loading ${company?.displayName || "job source"} job source`;
  elements.sourceLoadingPhase.textContent =
    "Retrieving available countries and locations...";
  state.sourceOverlayShowTimer = setTimeout(() => {
    if (!isCurrentSourceRequest(request)) return;
    if (document.activeElement !== document.body) {
      state.focusBeforeSourceLoading = document.activeElement;
    }
    elements.appShell.inert = true;
    elements.sourceLoadingOverlay.hidden = false;
    elements.sourceLoadingOverlay.setAttribute("aria-hidden", "false");
    requestAnimationFrame(() => {
      if (!isCurrentSourceRequest(request)) return;
      elements.sourceLoadingOverlay.classList.add("visible");
      elements.sourceLoadingOverlay.focus({ preventScroll: true });
    });
  }, OVERLAY_TRANSITION_MS);
  updateQueryControls();
  return request;
}

function isCurrentSourceRequest(request) {
  return JobSourceState.isCurrentRequest(
    request ? { ...request, aborted: request.signal.aborted } : null,
    state.sourceRequestGeneration,
    elements.companySelect.value);
}

function endSourceMetadataLoad(request) {
  if (!isCurrentSourceRequest(request)) return;
  clearTimeout(state.sourceOverlayShowTimer);
  state.sourceMetadataLoading = false;
  state.sourceAbortController = null;
  elements.sourceLoadingOverlay.classList.remove("visible");
  elements.sourceLoadingOverlay.setAttribute("aria-hidden", "true");
  elements.appShell.inert = state.isRefreshing ||
    state.sourceConfirmationOpen || state.resetConfirmationOpen || state.closeApplicationOpen;
  state.sourceOverlayHideTimer = setTimeout(() => {
    if (!state.sourceMetadataLoading) elements.sourceLoadingOverlay.hidden = true;
  }, OVERLAY_TRANSITION_MS);
  if (state.focusBeforeSourceLoading?.isConnected) {
    state.focusBeforeSourceLoading.focus({ preventScroll: true });
  }
  state.focusBeforeSourceLoading = null;
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
        ? `Retrieving job listings (${completed} of ${total} found)…`
        : "Retrieving job listing pages…";
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
      elements.loadingPhase.textContent = "Retrieving job listings and exact posting dates…";
      break;
  }
}

function beginRefreshProgressPolling(loadSnapshotWhenComplete = false) {
  clearTimeout(state.refreshProgressTimer);
  const generation = ++state.refreshPollingGeneration;
  const poll = async () => {
    if (!state.isRefreshing || generation !== state.refreshPollingGeneration) return;
    if (document.visibilityState === "hidden") {
      state.refreshProgressTimer = setTimeout(poll, 5000);
      return;
    }
    if (state.refreshStatusRequestInFlight) {
      state.refreshProgressTimer = setTimeout(poll, REFRESH_STATUS_POLL_MS);
      return;
    }
    state.refreshStatusRequestInFlight = true;
    try {
      const response = await fetch("/api/jobs/status", { cache: "no-store" });
      if (response.ok && generation === state.refreshPollingGeneration) {
        const status = await response.json();
        if (status.isRefreshing) {
          updateLoadingProgress(status.refreshProgress);
        } else {
          if (loadSnapshotWhenComplete) await loadSnapshot();
          return;
        }
      }
    } catch {
      // The foreground request owns error reporting; progress polling is best effort.
    } finally {
      state.refreshStatusRequestInFlight = false;
    }
    if (state.isRefreshing && generation === state.refreshPollingGeneration) {
      state.refreshProgressTimer = setTimeout(poll, REFRESH_STATUS_POLL_MS);
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

function constrainSourceLoadingFocus(event) {
  if (event.key === "Tab") {
    event.preventDefault();
    elements.sourceLoadingOverlay.focus({ preventScroll: true });
  }
}

function constrainSourceConfirmationFocus(event) {
  if (event.key === "Escape") {
    event.preventDefault();
    closeSourceConfirmation(true);
    return;
  }
  if (event.key !== "Tab") return;
  const focusable = [elements.sourceConfirmationStay, elements.sourceConfirmationDiscard,
    elements.sourceConfirmationApply]
    .filter(control => !control.hidden && !control.disabled);
  const currentIndex = focusable.indexOf(document.activeElement);
  const nextIndex = event.shiftKey
    ? (currentIndex <= 0 ? focusable.length - 1 : currentIndex - 1)
    : (currentIndex < 0 || currentIndex === focusable.length - 1 ? 0 : currentIndex + 1);
  event.preventDefault();
  focusable[nextIndex].focus({ preventScroll: true });
}

function constrainResetConfirmationFocus(event) {
  if (event.key === "Escape" && !state.resetInProgress) {
    event.preventDefault();
    closeResetConfirmation(true);
    return;
  }
  if (event.key !== "Tab") return;
  const focusable = [elements.resetConfirmationCancel, elements.resetConfirmationSubmit]
    .filter(element => !element.disabled);
  if (focusable.length === 0) {
    event.preventDefault();
    elements.resetConfirmationOverlay.focus({ preventScroll: true });
    return;
  }
  const currentIndex = focusable.indexOf(document.activeElement);
  const nextIndex = event.shiftKey
    ? (currentIndex <= 0 ? focusable.length - 1 : currentIndex - 1)
    : (currentIndex < 0 || currentIndex === focusable.length - 1 ? 0 : currentIndex + 1);
  event.preventDefault();
  focusable[nextIndex].focus({ preventScroll: true });
}

function constrainCloseApplicationFocus(event) {
  if (event.key === "Escape" && !state.closeApplicationInProgress) {
    event.preventDefault();
    closeCloseApplicationModal(true);
    return;
  }
  if (event.key !== "Tab") return;
  const focusable = [
    elements.closeApplicationReason,
    elements.closeApplicationCancel,
    elements.closeApplicationSubmit
  ].filter(element => !element.disabled);
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
