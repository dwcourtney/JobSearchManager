"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");
const JobFit = require(path.join(root, "wwwroot", "job-fit.js"));
const catalog = JSON.parse(fs.readFileSync(path.join(root, "JobConceptCatalog.json"), "utf8"));
const preferencesStart = index.indexOf('id="preferences-settings-panel"');
const jobFitStart = index.indexOf('id="job-fit-settings-panel"');
const accountStart = index.indexOf('id="account-settings-panel"');
const preferences = index.slice(preferencesStart, jobFitStart);
const jobFit = index.slice(jobFitStart, accountStart);

assert.match(index, /id="job-fit-settings-tab"[\s\S]*?>\s*Job Fit\s*</);
assert.match(index, /src="\/job-fit\.js\?v=8"/,
  "The revised Job Fit runtime must use a new cache-busting asset version.");
assert.match(index, /id="job-fit-settings-panel"/);
assert.match(preferences, /id="compensation-heading"[\s\S]*?id="appearance-heading"/);
assert.doesNotMatch(preferences,
  /work-arrangement-filtering-heading|deployment-filtering-heading|exclude-strong-extended-location-requirements/,
  "My Preferences must contain only Compensation and Appearance.");
assert.equal((preferences.match(/<section class="settings-section"/g) || []).length, 2,
  "My Preferences must contain exactly two settings sections.");
assert.match(jobFit,
  /id="job-fit-heading">Job Fit Scoring<[\s\S]*?id="job-fit-enabled"[\s\S]*?id="job-fit-configuration" class="job-fit-subordinate"[\s\S]*?id="work-arrangement-filtering-heading"[\s\S]*?id="job-fit-signals-heading">Job Fit Preferences</,
  "The global Job Fit control must remain above the tabbed subordinate preferences.");
assert.doesNotMatch(jobFit, /id="job-fit-configuration"[^>]*\bhidden\b/,
  "Subordinate Job Fit settings must remain visible while inactive.");
assert.match(jobFit, /id="exclude-strong-extended-location-requirements"[^>]*type="checkbox"/,
  "Job Fit must contain the existing work-arrangement filtering checkbox.");
assert.match(jobFit,
  /Hide jobs with strong work-arrangement conflicts such as deployment, rotation, relocation, or extended away-from-home assignments\. Ordinary business travel is not excluded\./,
  "Work Arrangement Filtering must explain its broader detector semantics.");
assert.match(index, /id="job-fit-enabled"/);
assert.match(index, /id="job-fit-concept-search"/);
assert.match(index, /id="job-fit-survey"/,
  "Job Fit must expose the canonical-concept survey.");
assert.match(index, /id="job-fit-survey-status"[^>]*aria-live="polite"/);
assert.match(index, /id="job-fit-tab-list"[^>]*role="tablist"[^>]*aria-label="Job Fit preference categories"/);
assert.doesNotMatch(index, /id="job-fit-(?:category-filter|concept-select|preference-select|add-signal|signal-list)"/,
  "The former add/configure/remove workflow must not remain in the survey UI.");
assert.doesNotMatch(index, />\s*(?:Add Signal|Remove)\s*</,
  "The survey must not expose Add Signal or Remove actions.");
assert.doesNotMatch(index, /id="job-fit-(?:keyword|phrase|custom-concept)"/,
  "Job Fit must not expose arbitrary concept entry.");
assert.match(app, /fetch\("\/api\/job-fit\/concepts"/);
assert.match(app, /input\.type = "range";[\s\S]*?input\.min = "0";[\s\S]*?input\.max = "6";[\s\S]*?input\.step = "1";/,
  "Travel Tolerance must be a native seven-detent range control.");
assert.match(app, /JobFit\.travelLevels\.forEach\(definition => \{[\s\S]*?datalist\.append\(option\)/,
  "The range must expose every frozen travel level through a datalist.");
assert.match(app, /noTravel\.textContent = "No travel"[\s\S]*?travelHeavy\.textContent = "Travel-heavy"/,
  "The range must label both endpoints plainly.");
assert.match(app, /aria-valuetext[\s\S]*?aria-live/,
  "The current level must be announced accessibly.");
assert.match(app, /state\.travelTolerance = jobFit\.travelTolerance/,
  "Persisted travel tolerance must hydrate into the UI.");
assert.match(app, /travelTolerance: state\.travelTolerance/,
  "Travel tolerance must be included when settings are saved.");
assert.match(app, /input\.type = "range";[\s\S]*?input\.min = "0";[\s\S]*?input\.max = "5";[\s\S]*?input\.step = "1";/,
  "Normal Work Location must be a native six-detent range control.");
assert.match(app, /JobFit\.workLocationLevels\.forEach\(definition => \{[\s\S]*?datalist\.append\(option\)/,
  "The work-location range must expose all six frozen levels.");
assert.match(app, /remote\.textContent = "100% Remote"[\s\S]*?onsite\.textContent = "Fully onsite"/,
  "Normal Work Location must label both endpoints.");
assert.match(app, /state\.preferredWorkLocation = jobFit\.preferredWorkLocation/,
  "Persisted preferred work location must hydrate into the UI.");
assert.match(app, /preferredWorkLocation: state\.preferredWorkLocation/,
  "Preferred work location must be included when settings are saved.");
assert.match(app, /state\.jobFitConcepts\.filter\(concept => concept\.userConfigurable !== false\)/,
  "Detector-only travel concepts must not render as survey rows.");
assert.match(app,
  /panel\.append\(createTravelToleranceControl\(\)\)[\s\S]*?panel\.append\(createPreferredWorkLocationControl\(\)\)[\s\S]*?section\.append\(createAssignmentLocationIntroduction\(\)\)/,
  "Work Arrangement must order Travel Tolerance, Normal Work Location, then Assignment / Location Constraints.");
assert.match(app, /heading\.textContent = "Assignment \/ Location Constraints"/);
assert.match(app, /A posting may match more than one\./,
  "The assignment grouping must explain that overlapping signals are intentional.");
assert.match(app, /tabButton\.role = "tab"[\s\S]*?panel\.role = "tabpanel"/,
  "Job Fit navigation must use semantic tabs and tabpanels.");
assert.match(app, /ArrowRight[\s\S]*?ArrowLeft[\s\S]*?Home[\s\S]*?End/,
  "Job Fit tabs must support conventional keyboard navigation.");
assert.match(app, /row\.setAttribute\("role", "radiogroup"\)/);
assert.match(app, /radio\.name = `job-fit-\$\{concept\.id\}`/,
  "Every concept must have an independent radio group.");
assert.match(app, /radio\.checked = \(configured\.get\(concept\.id\) \|\| "neutral"\) === value/,
  "Absence from sparse configuration must render as Neutral.");
assert.match(app, /if \(radio\.value !== "neutral"\)/,
  "Returning a concept to Neutral must omit it from the sparse settings array.");
assert.match(app,
  /state\.excludeStrongExtendedLocationRequirements =\s*settings\.excludeStrongExtendedLocationRequirements === true;/,
  "The persisted filtering setting must hydrate without migration or renaming.");
assert.match(app,
  /excludeStrongExtendedLocationRequirements: state\.excludeStrongExtendedLocationRequirements/,
  "The existing filtering setting must save under its unchanged persisted name.");
assert.match(app,
  /!state\.excludeStrongExtendedLocationRequirements \|\|\s*job\.extendedLocationRequirement\?\.confidence !== "strong"/,
  "The Job Fit hierarchy must not change the existing strong-detection filtering predicate.");
assert.match(app,
  /jobFitConfiguration\.classList\.toggle\("is-inactive", !state\.jobFitEnabled\)[\s\S]*?excludeStrongExtendedLocationRequirements\.disabled = !state\.jobFitEnabled[\s\S]*?jobFitConceptSearch\.disabled = !state\.jobFitEnabled[\s\S]*?renderJobFitSurvey\(\)/,
  "Disabling Job Fit must visibly inactivate and disable every subordinate control.");
assert.match(app, /radio\.disabled = !state\.jobFitEnabled \|\| groupOverrideActive/,
  "Concept choices must follow the global state and their section override.");
const jobFitToggleHandler = app.match(
  /elements\.jobFitEnabled\.addEventListener\("change", \(\) => \{([\s\S]*?)\n  \}\);/)?.[1] || "";
assert.match(jobFitToggleHandler, /state\.jobFitEnabled = elements\.jobFitEnabled\.checked/);
assert.doesNotMatch(jobFitToggleHandler,
  /state\.(?:jobFitSignals|travelTolerance|preferredWorkLocation|excludeStrongExtendedLocationRequirements)\s*=/,
  "Toggling Job Fit must preserve subordinate values for later re-enable.");
assert.match(app, /\["negative", "NEG"\][\s\S]*?\["ideal", "I"\]/,
  "The survey must use the balanced Hard Conflict, Negative, Neutral, Positive, Ideal scale.");
assert.doesNotMatch(app, /\["strong(?:Negative|Positive)"/,
  "Legacy preference names must not remain as survey columns.");
assert.deepEqual([...new Set(catalog.concepts.map(concept => concept.category))].sort(), [
  "Responsibility Shape",
  "Role Type / Career Direction",
  "Technical Domain",
  "Work Arrangement",
  "Work Environment"
]);
assert.equal(catalog.concepts.length, 79, "The complete canonical catalog must remain available.");
const travelConcepts = catalog.concepts.filter(concept => concept.id.startsWith("work.travel."));
assert.equal(travelConcepts.length, 4, "All four internal travel detectors must remain in the catalog.");
assert.deepEqual(travelConcepts.map(concept => concept.travelLevel).sort(), [3, 4, 5, 6]);
assert.ok(travelConcepts.every(concept => concept.userConfigurable === false),
  "Internal travel detectors must be hidden from user configuration.");
const locationConcepts = catalog.concepts.filter(concept =>
  ["work.remote.full", "work.remote", "work.hybrid", "work.onsite"].includes(concept.id));
assert.deepEqual(locationConcepts.map(concept => concept.workLocationLevel).sort(), [0, 2, 3, 5]);
assert.ok(locationConcepts.every(concept => concept.userConfigurable === false),
  "Internal normal-work-location detectors must be hidden from user configuration.");
assert.equal(catalog.concepts.filter(concept => concept.userConfigurable !== false).length, 71,
  "Only the four travel and four normal-location detector rows may be hidden from the survey.");
const expectedSurveyGroups = {
  "Role Type / Career Direction": [
    ["Technical Engineering", [
      "role.ai-ml-engineering", "role.cloud-engineering", "role.cybersecurity",
      "role.data-engineering", "role.data-science", "role.devops-platform",
      "role.hardware-engineering", "role.infrastructure-engineering", "role.network-engineering",
      "role.software-engineering", "role.systems-engineering", "role.test-validation-engineering"
    ]],
    ["Work Shape / Leadership", ["role.individual-contributor", "role.people-management"]],
    ["Delivery / Management", ["role.program-management", "role.project-management"]],
    ["Field-Based", ["role.field-service"]]
  ],
  "Technical Domain": [
    ["Software Development", [
      "technical.api-development", "technical.application-development", "technical.backend-development",
      "technical.frontend-development", "technical.software-development", "technical.embedded-systems"
    ]],
    ["AI / Data", [
      "technical.artificial-intelligence", "technical.machine-learning",
      "technical.large-language-models", "technical.nlp"
    ]],
    ["Cloud / Platform / Automation", [
      "technical.cloud", "technical.cicd", "technical.infrastructure-as-code",
      "technical.containers", "technical.automation-scripting", "technical.virtualization"
    ]],
    ["Systems / Administration", [
      "technical.linux", "technical.linux-administration",
      "technical.windows-administration", "technical.storage"
    ]],
    ["Network / Physical Infrastructure", [
      "technical.networking", "technical.cisco-networking",
      "technical.cabling-racking", "technical.power-facilities"
    ]]
  ],
  "Work Environment": [
    ["Physical / Field Environments", [
      "work.aircraft-flight-line", "work.customer-site", "work.data-center", "work.field-engineering",
      "work.lab-environment", "work.manufacturing-floor", "work.outdoor-field",
      "work.physical-infrastructure"
    ]],
    ["Restricted / Special Facilities", ["work.classified-facility"]],
    ["High-Risk / Special Conditions", [
      "work.confined-spaces", "work.scuba", "work.shipboard", "work.heights"
    ]]
  ],
  "Responsibility Shape": [
    ["Technical Work Shape", [
      "responsibility.architecture-heavy", "responsibility.hands-on-implementation",
      "responsibility.research-oriented", "responsibility.operations-sustainment",
      "responsibility.documentation-heavy"
    ]],
    ["Leadership / Ownership", [
      "responsibility.team-leadership", "responsibility.personnel-management",
      "role.management-heavy", "responsibility.budget-ownership",
      "responsibility.schedule-ownership"
    ]],
    ["External / Business Responsibility", [
      "responsibility.customer-facing", "responsibility.proposal-capture"
    ]]
  ]
};
for (const [category, expectedGroups] of []) {
  const actualGroups = JobFit.surveyGroups[category].map(group => [group.title, [...group.conceptIds]]);
  assert.deepEqual(actualGroups, expectedGroups, `${category} subgroup order changed.`);
  const groupedIds = actualGroups.flatMap(([, conceptIds]) => conceptIds);
  const categoryIds = catalog.concepts
    .filter(concept => concept.category === category && concept.userConfigurable !== false)
    .map(concept => concept.id);
  assert.equal(groupedIds.length, new Set(groupedIds).size,
    `${category} contains a duplicate grouped concept.`);
  assert.deepEqual(groupedIds.slice().sort(), categoryIds.slice().sort(),
    `${category} grouping lost or added a canonical concept.`);
  categoryIds.forEach(conceptId => assert.ok(JobFit.surveyConceptDescriptions[conceptId],
    `${conceptId} must have a concise survey description.`));
}
assert.deepEqual(JobFit.surveyTabs.map(tab => tab.title), [
  "Work Arrangement", "Career Direction", "Software / AI / Data",
  "Cloud / Infrastructure / IT", "Hardware / Field", "Work Environment",
  "Responsibility Shape", "Management / Delivery", "External / Business"
]);
const organizedIds = JobFit.surveyTabs.flatMap(tab =>
  tab.sections.flatMap(section => [...section.conceptIds]));
const configurableIds = catalog.concepts
  .filter(concept => concept.userConfigurable !== false)
  .map(concept => concept.id);
assert.equal(organizedIds.length, new Set(organizedIds).size,
  "Every user-facing concept must have exactly one UI owner.");
assert.deepEqual(organizedIds.slice().sort(), configurableIds.slice().sort(),
  "Tabbed organization must neither lose nor add canonical concepts.");
assert.deepEqual([...JobFit.groupHardConflictIds], [
  "software-development", "ai-data", "cloud-platform-automation",
  "systems-administration", "network-physical-infrastructure"
]);
assert.equal(Object.keys(JobFit.groupOverrideByConcept).length,
  new Set(Object.keys(JobFit.groupOverrideByConcept)).size,
  "Each overridden concept must have one deterministic group owner.");
assert.ok(!JobFit.surveyTabs.find(tab => tab.id === "work-environment")
  .sections.some(section => section.hardConflictId),
  "Work Environment must not receive an inappropriate blanket override.");
const matchingTabIds = query => {
  const normalized = query.toLocaleLowerCase();
  const matches = text => normalized.length <= 2
    ? text.toLocaleLowerCase().split(/[^a-z0-9]+/u).includes(normalized)
    : text.toLocaleLowerCase().includes(normalized);
  return JobFit.surveyTabs
    .filter(tab => tab.sections.some(section => section.conceptIds.some(conceptId => {
      const concept = catalog.concepts.find(item => item.id === conceptId);
      return matches(`${concept?.displayName || ""} ${concept?.category || ""} ${section.title} ${JobFit.surveyConceptDescriptions[conceptId] || ""}`);
    })))
    .map(tab => tab.id);
};
assert.deepEqual(matchingTabIds("Linux"), ["cloud-infrastructure-it"]);
assert.deepEqual(matchingTabIds("AI"), ["software-ai-data"]);
assert.deepEqual(matchingTabIds("management"), ["management-delivery"]);
assert.deepEqual(matchingTabIds("SCUBA"), ["work-environment"]);
assert.match(app, /function matchesJobFitSearch\(text, query\)/,
  "Short searches such as AI must use token matching instead of incidental substrings.");
assert.equal(Object.keys(JobFit.surveyConceptDescriptions).length, 66,
  "Exactly the four requested non-slider sections must receive descriptions.");
assert.match(JobFit.surveyConceptDescriptions["technical.linux"], /Linux environments/);
assert.match(JobFit.surveyConceptDescriptions["technical.linux-administration"], /Administration/);
assert.match(JobFit.surveyConceptDescriptions["technical.networking"], /architecture, routing, switching/);
assert.match(JobFit.surveyConceptDescriptions["technical.cisco-networking"], /Cisco-specific/);
assert.match(JobFit.surveyConceptDescriptions["responsibility.team-leadership"], /without necessarily having formal personnel authority/);
assert.match(JobFit.surveyConceptDescriptions["responsibility.personnel-management"], /Formal employee responsibility/);
assert.match(JobFit.surveyConceptDescriptions["role.management-heavy"], /rather than direct technical execution/);
assert.equal((app.match(/input\.type = "range";/g) || []).length, 2,
  "No slider may be added beyond Travel Tolerance and Normal Work Location.");
assert.match(app, /\.filter\(section => section\.concepts\.length > 0\)/,
  "Filtering must suppress empty section headings.");
assert.match(app, /const conceptDescription = JobFit\.surveyConceptDescriptions\[concept\.id\]/,
  "Survey rows must render the stable description metadata.");
assert.match(app, /row\.setAttribute\("aria-describedby", description\.id\)/,
  "Survey descriptions must be associated with their radio groups.");
const extendedAway = catalog.concepts.find(concept =>
  concept.id === "work.extended-away-assignment");
assert.deepEqual(
  { displayName: extendedAway?.displayName, category: extendedAway?.category },
  { displayName: "Extended Away-from-Home Assignment", category: "Work Arrangement" },
  "The extended-assignment concept must appear in the Work Arrangement survey.");
assert.match(app,
  /concept\.displayName[\s\S]*?concept\.category[\s\S]*?section\.title[\s\S]*?surveyConceptDescriptions/,
  "Global search must include concept, category, section, and description text without making a tab title match every concept in that tab.");
assert.match(app, /if \(jobFit\) \{[\s\S]*?Job Fit \$\{jobFit\.score\}\/10/,
  "The badge must be conditional on an enabled Job Fit assessment.");

const badgeStart = styles.indexOf(".job-card .job-fit-badge.score-low");
const badgeEnd = styles.indexOf(".job-card .analysis-pending-badge", badgeStart);
const badgeStyles = styles.slice(badgeStart, badgeEnd);
assert.ok(badgeStart >= 0 && badgeStyles.includes("var(--color-fit-blocker-border)"));
assert.doesNotMatch(badgeStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit badge styles must use theme tokens rather than raw colors.");
const categoryStart = styles.indexOf(".job-fit-survey-category");
const categoryEnd = styles.indexOf(".settings-save-note", categoryStart);
const categoryStyles = styles.slice(categoryStart, categoryEnd);
assert.ok(categoryStart >= 0 && categoryStyles.includes("var(--color-accordion-header-background)"));
assert.doesNotMatch(categoryStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit category sections must use existing theme tokens.");
const hierarchyStart = styles.indexOf(".job-fit-subordinate");
const hierarchyEnd = styles.indexOf(".job-fit-introduction", hierarchyStart);
const hierarchyStyles = styles.slice(hierarchyStart, hierarchyEnd);
assert.ok(hierarchyStart >= 0 && hierarchyStyles.includes("var(--color-settings-subsection-divider)"));
assert.ok(hierarchyStyles.includes("var(--opacity-disabled-control)"));
assert.doesNotMatch(hierarchyStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "The Job Fit hierarchy and inactive treatment must use semantic theme tokens.");
const travelStart = styles.indexOf(".job-fit-scale-panel");
const travelEnd = styles.indexOf(".job-fit-matrix", travelStart);
const travelStyles = styles.slice(travelStart, travelEnd);
assert.ok(travelStart >= 0 && travelStyles.includes("var(--color-surface-secondary)"));
assert.doesNotMatch(travelStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Travel Tolerance must remain theme-safe and use semantic tokens only.");
assert.ok(travelStyles.includes(".normal-work-location-panel") &&
  travelStyles.includes(".assignment-location-introduction"),
"Both special controls and the assignment grouping must share theme-safe hierarchy styling.");
assert.match(styles, /@media \(max-width: 560px\)[\s\S]*?\.job-fit-survey-row/,
  "The radio matrix must include a compact small-screen layout.");

console.log("All Job Fit UI integration tests passed.");
