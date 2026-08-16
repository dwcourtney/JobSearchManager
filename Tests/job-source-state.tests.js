"use strict";

const assert = require("node:assert/strict");
const sourceState = require("../wwwroot/job-source-state.js");

const applied = {
  companyId: "leidos",
  countryId: "us",
  includeAllLocations: false,
  includeRemote: true,
  physicalLocations: []
};

const tests = [
  ["opening Settings without edits remains clean", () => {
    assert.equal(sourceState.areEquivalent(applied, { ...applied }), true);
  }],
  ["fresh editable source initializes from the applied source", () => {
    assert.deepEqual(sourceState.normalize({ ...applied }), sourceState.normalize(applied));
  }],
  ["Settings to Jobs without edits does not warn", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, applied, { ...applied }), "allow");
  }],
  ["changing a source field makes the source dirty", () => {
    assert.equal(sourceState.areEquivalent(applied, { ...applied, includeRemote: false }), false);
  }],
  ["changing a source field back makes the source clean", () => {
    const editable = { ...applied, includeRemote: false };
    assert.equal(sourceState.areEquivalent(applied, editable), false);
    editable.includeRemote = true;
    assert.equal(sourceState.areEquivalent(applied, editable), true);
  }],
  ["programmatic hydration and equivalent representations remain clean", () => {
    const hydrated = {
      companyId: " leidos ",
      countryId: "us",
      includeAllLocations: false,
      includeRemote: true,
      physicalLocations: [{ id: "b", label: "Second" }, { id: "a" }, { id: "a" }]
    };
    const current = { ...applied, physicalLocationIds: ["a", "b"] };
    assert.equal(sourceState.areEquivalent(current, hydrated), true);
    assert.equal(sourceState.areEquivalent(
      { ...applied, countryId: null }, { ...applied, countryId: "" }), true);
  }],
  ["real pending changes still trigger the navigation guard", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, applied,
      { ...applied, companyId: "boeing" }), "guard");
  }],
  ["applying the editable source clears dirty state", () => {
    const editable = { ...applied, physicalLocationIds: ["location-2", "location-1"] };
    assert.equal(sourceState.areEquivalent(applied, editable), false);
    const newlyApplied = { ...editable };
    assert.equal(sourceState.areEquivalent(newlyApplied, editable), true);
  }],
  ["reload with matching persisted and applied source remains clean", () => {
    const persisted = JSON.parse(JSON.stringify(applied));
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, persisted, applied), "allow");
  }],
  ["country-wide and unavailable-remote representations normalize consistently", () => {
    const countryWide = { ...applied, includeAllLocations: true, includeRemote: false,
      physicalLocationIds: ["ignored"] };
    assert.deepEqual(sourceState.normalize(countryWide), {
      companyId: "leidos", countryId: "us", includeAllLocations: true,
      includeRemote: true, physicalLocationIds: []
    });
    assert.equal(sourceState.areEquivalent(
      applied, { ...applied, includeRemote: false }, { remoteAvailable: false }), true);
  }],
  ["fresh workspace without a valid selection requires a source", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", false,
      { companyId: null, countryId: "us" },
      { companyId: null, countryId: "us" }), "require-source");
  }],
  ["fresh workspace with an imported pending source uses the navigation guard", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", false,
      { companyId: null, countryId: "us" },
      applied), "guard");
  }],
  ["clean applied source navigates directly to Jobs", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, applied, { ...applied }), "allow");
  }],
  ["different imported or manually edited source uses the same guard", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, applied,
      { ...applied, companyId: "boeing" }), "guard");
  }],
  ["equivalent imported source does not create a false guard", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, applied,
      { ...applied, physicalLocations: [] }), "allow");
  }],
  ["clean applied workspace can enter Settings", () => {
    assert.equal(sourceState.navigationDecision(
      "jobs", "settings", true, applied, applied), "allow");
  }],
  ["first-run workspace can enter Settings", () => {
    assert.equal(sourceState.navigationDecision(
      "jobs", "settings", false, {}, {}), "allow");
  }],
  ["pending source does not prevent entering Settings", () => {
    assert.equal(sourceState.navigationDecision(
      "jobs", "settings", true, applied,
      { ...applied, companyId: "boeing" }), "allow");
  }],
  ["no-source Jobs navigation is explicit rather than silent", () => {
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", false, {},
      { companyId: null, countryId: "us" }), "require-source");
  }],
  ["only Settings to Jobs is guarded for an applied source", () => {
    const pending = { ...applied, companyId: "boeing" };
    assert.equal(sourceState.navigationDecision(
      "settings", "jobs", true, applied, pending), "guard");
    assert.equal(sourceState.navigationDecision(
      "jobs", "jobs", true, applied, pending), "allow");
  }]
];

let failures = 0;
for (const [name, run] of tests) {
  try {
    run();
    console.log(`PASS Job source state: ${name}`);
  } catch (error) {
    failures++;
    console.error(`FAIL Job source state: ${name}`);
    console.error(error.stack || error);
  }
}

if (failures > 0) process.exitCode = 1;
else console.log(`All ${tests.length} deterministic job-source state tests passed.`);
