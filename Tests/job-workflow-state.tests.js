"use strict";

const assert = require("node:assert/strict");
const workflow = require("../wwwroot/job-workflow-state.js");

const tests = [
  ["every job resolves to exactly one canonical state", () => {
    const states = new Map([
      ["normal", "normal"], ["saved", "saved"], ["applied", "applied"],
      ["hidden", "hidden"], ["invalid", "not-a-state"]
    ]);
    for (const id of states.keys()) {
      const memberships = ["normal", "saved", "applied", "hidden"]
        .filter(tab => workflow.belongsToTab(id, tab, states));
      assert.equal(memberships.length, 1);
    }
    assert.equal(workflow.stateForJob("invalid", states), "normal");
  }],
  ["Normal appears only in All Jobs", () => {
    const states = new Map([["job", "normal"]]);
    assert.equal(workflow.belongsToTab("job", "normal", states), true);
    assert.equal(workflow.belongsToTab("job", "saved", states), false);
    assert.equal(workflow.belongsToTab("job", "applied", states), false);
    assert.equal(workflow.belongsToTab("job", "hidden", states), false);
  }],
  ["Saved appears only in Saved", () => {
    const states = new Map([["job", "saved"]]);
    assert.deepEqual(["normal", "saved", "applied", "hidden"]
      .filter(tab => workflow.belongsToTab("job", tab, states)), ["saved"]);
  }],
  ["Applied appears only in Applied", () => {
    const states = new Map([["job", "applied"]]);
    assert.deepEqual(["normal", "saved", "applied", "hidden"]
      .filter(tab => workflow.belongsToTab("job", tab, states)), ["applied"]);
  }],
  ["Hidden appears only in Hidden", () => {
    const states = new Map([["job", "hidden"]]);
    assert.deepEqual(["normal", "saved", "applied", "hidden"]
      .filter(tab => workflow.belongsToTab("job", tab, states)), ["hidden"]);
  }],
  ["Save moves Normal to Saved", () => {
    assert.equal(workflow.transition("normal", "save"), "saved");
  }],
  ["Unsave moves Saved to Normal", () => {
    assert.equal(workflow.transition("saved", "unsave"), "normal");
  }],
  ["Apply moves Normal to Applied", () => {
    assert.equal(workflow.transition("normal", "apply"), "applied");
  }],
  ["Apply moves Saved to Applied", () => {
    assert.equal(workflow.transition("saved", "apply"), "applied");
  }],
  ["Undo Applied moves Applied to Normal", () => {
    assert.equal(workflow.transition("applied", "unapply"), "normal");
  }],
  ["Hide moves Normal to Hidden", () => {
    assert.equal(workflow.transition("normal", "hide"), "hidden");
  }],
  ["Hide moves Saved to Hidden", () => {
    assert.equal(workflow.transition("saved", "hide"), "hidden");
  }],
  ["Hide moves Applied to Hidden", () => {
    assert.equal(workflow.transition("applied", "hide"), "hidden");
  }],
  ["Restore moves Hidden to Normal", () => {
    assert.equal(workflow.transition("hidden", "restore"), "normal");
  }],
  ["invalid transitions cannot create overlapping states", () => {
    assert.equal(workflow.transition("applied", "save"), "applied");
    assert.equal(workflow.transition("hidden", "apply"), "hidden");
    assert.equal(workflow.transition("hidden", "save"), "hidden");
  }]
];

for (const [name, test] of tests) {
  test();
  console.log(`PASS Job workflow state: ${name}`);
}

console.log(`All ${tests.length} deterministic job-workflow state tests passed.`);
