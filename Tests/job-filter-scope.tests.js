"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const workflow = require("../wwwroot/job-workflow-state.js");
const source = fs.readFileSync(path.join(__dirname, "../wwwroot/app.js"), "utf8");

// Run the production filter, renderer and tab switching with lightweight DOM stubs.
// Qualification evaluators are isolated here; their domain rules have separate tests.
function functionSource(name) {
  const start = source.indexOf(`function ${name}(`);
  assert.ok(start >= 0, name);
  const end = source.indexOf("\nfunction ", start + 1);
  const asyncEnd = source.indexOf("\nasync function ", start + 1);
  return source.slice(start, Math.min(...[end, asyncEnd].filter(i => i >= 0)));
}

function element() {
  return {
    textContent: "", children: [],
    classList: { toggle() {} }, setAttribute() {}, focus() {},
    replaceChildren() { this.children = []; },
    append(child) { this.children.push(child); }
  };
}

const tracked = ["saved", "applied", "closed", "hidden"];
function fixture() {
  const jobs = ["normal", ...tracked].map(stableId => ({
    stableId, title: "Engineer", requisitionId: "123", primaryLocation: "Virginia",
    payPeriod: "annual", payMaximum: 120000, isRemoteLocationRestricted: true,
    academicQualification: { hide: true }, workAuthorization: { hide: true },
    extendedLocationRequirement: { confidence: "strong" }, detailLoaded: true
  }));
  const state = {
    jobs, jobStates: new Map(jobs.map(j => [j.stableId, j.stableId])),
    inclusions: [], exclusions: [], scope: "metadata", minimumSalary: null,
    locationMode: "all", descriptionMatches: new Map(), activeResultsTab: "all",
    selectedJobId: null
  };
  const elements = Object.fromEntries([
    "allJobCount", "savedJobCount", "appliedJobCount", "closedJobCount", "hiddenJobCount",
    "allResultsTab", "savedResultsTab", "appliedResultsTab", "closedResultsTab",
    "hiddenResultsTab", "resultsTabPanel", "resultCount", "filterSummary", "jobList"
  ].map(name => [name, element()]));
  const context = vm.createContext({
    state, elements, JobWorkflowState: workflow,
    evaluateEducationMatch: value => value,
    evaluateClearanceMatch: () => ({ hide: true }),
    evaluateWorkAuthorizationMatch: value => value,
    currentEducationProfile: () => ({}), currentSecurityProfile: () => ({}),
    currentWorkAuthorizationProfile: () => ({}), buildFilterSummary: () => "filters",
    document: { createElement: element, createDocumentFragment: element },
    AGE_GROUPS: [{ id: "test" }],
    groupJobsByAge: jobs => new Map([["test", jobs]]),
    createAgeGroup: (_group, jobs) => ({ jobs }),
    renderDetail: job => { context.detail = job; },
    loadJobDetail: () => { throw new Error("Unexpected detail fetch"); }
  });
  for (const name of ["jobsPassingGeneralFilters", "renderResults", "showResultsTab"]) {
    vm.runInContext(functionSource(name), context);
  }
  return context;
}

const scenarios = [
  ["salary", { minimumSalary: 150000 }],
  ["include keywords", { inclusions: ["accountant"] }],
  ["exclude keywords", { exclusions: ["engineer"] }],
  ["remote location", { locationMode: "hide-restricted" }],
  ["education", { hideStrictEducationMismatch: true }],
  ["clearance", { hideStrictClearanceMismatch: true }],
  ["work authorization", { hideStrictWorkAuthorizationMismatch: true }],
  ["extended location", { excludeStrongExtendedLocationRequirements: true }],
  ["description keywords", {
    scope: "description", inclusions: ["accountant"],
    descriptionMatches: new Map(["normal", ...tracked].map(id => [id, false]))
  }]
];

for (const [name, filters] of scenarios) {
  const ctx = fixture();
  ctx.renderResults();
  assert.equal(ctx.elements.allJobCount.textContent, "(1)");
  Object.assign(ctx.state, filters);
  ctx.renderResults();
  assert.equal(ctx.elements.allJobCount.textContent, "(0)", name);
  assert.equal(ctx.detail, null, name);
  for (const tab of tracked) {
    ctx.showResultsTab(tab);
    assert.equal(ctx.elements[`${tab}JobCount`].textContent, "(1)", `${name}: ${tab}`);
    assert.equal(ctx.elements.resultCount.textContent, `Showing 1 ${tab} job`);
    assert.equal(ctx.detail, ctx.state.jobs.find(job => job.stableId === tab));
    const renderedJobs = ctx.elements.jobList.children[0].children[0].jobs;
    assert.equal(renderedJobs.length, 1);
    assert.equal(renderedJobs[0].stableId, tab);
    for (const [key, value] of Object.entries(filters)) {
      assert.equal(ctx.state[key], value, `Tab switch preserves ${key}`);
    }
  }
  ctx.showResultsTab("all");
  assert.equal(ctx.elements.allJobCount.textContent, "(0)");
  assert.equal(ctx.state.jobs.length, 5);
  console.log(`PASS discovery filter scope: ${name}`);
}

for (const [initial, actions] of [
  ["saved", ["unsave"]], ["applied", ["unapply"]],
  ["closed", ["reopen", "unapply"]], ["hidden", ["restore"]]
]) {
  const ctx = fixture();
  ctx.state.minimumSalary = 150000;
  ctx.showResultsTab(initial);
  assert.equal(ctx.detail.stableId, initial);
  for (const action of actions) workflow.applyTransition(initial, action, ctx.state.jobStates);
  assert.equal(ctx.state.jobStates.get(initial), "normal");
  ctx.showResultsTab("all");
  assert.equal(ctx.elements.allJobCount.textContent, "(0)");
  assert.equal(ctx.elements[`${initial}JobCount`].textContent, "(0)");
  ctx.state.minimumSalary = null;
  ctx.renderResults();
  assert.equal(ctx.elements.allJobCount.textContent, "(2)");
  console.log(`PASS discovery filters resume after returning ${initial} to Normal`);
}

// Preserve salary boundary/unknown-pay behavior and allow filtering a supplied subset.
const ctx = fixture();
ctx.state.minimumSalary = 150000;
const candidates = [150000, 149999, null].map(payMaximum => ({ ...ctx.state.jobs[0], payMaximum }));
const hourly = { ...ctx.state.jobs[0], payPeriod: "hourly", payMaximum: 50 };
assert.deepEqual(Array.from(ctx.jobsPassingGeneralFilters([...candidates, hourly])),
  [candidates[0], candidates[2], hourly]);
console.log("All filter-scope regression tests passed.");
