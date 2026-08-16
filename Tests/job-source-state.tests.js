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
    assert.equal(sourceState.shouldWarnWhenLeavingSettings(
      "settings", "jobs", applied, { ...applied }), false);
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
    assert.equal(sourceState.shouldWarnWhenLeavingSettings(
      "settings", "jobs", applied, { ...applied, companyId: "boeing" }), true);
  }],
  ["applying the editable source clears dirty state", () => {
    const editable = { ...applied, physicalLocationIds: ["location-2", "location-1"] };
    assert.equal(sourceState.areEquivalent(applied, editable), false);
    const newlyApplied = { ...editable };
    assert.equal(sourceState.areEquivalent(newlyApplied, editable), true);
  }],
  ["reload with matching persisted and applied source remains clean", () => {
    const persisted = JSON.parse(JSON.stringify(applied));
    assert.equal(sourceState.shouldWarnWhenLeavingSettings(
      "settings", "jobs", persisted, applied), false);
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
