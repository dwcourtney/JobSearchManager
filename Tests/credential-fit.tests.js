"use strict";

const assert = require("node:assert/strict");
const fit = require("../wwwroot/credential-fit.js");

const ncda = {
  credentialId: "netapp-ncda",
  name: "NCDA",
  requirement: "required",
  equivalentCredentialIds: []
};

const tests = [
  ["held required credential is met", () => {
    const result = fit.evaluate([ncda], [], {
      inventoryStatus: "complete", heldCredentialIds: ["netapp-ncda"]
    });
    assert.equal(result.meets.length, 1);
    assert.equal(result.blockers.length, 0);
    assert.equal(result.reviews.length, 0);
  }],
  ["confirmed empty inventory makes a required credential unmet", () => {
    const result = fit.evaluate([ncda], [], { inventoryStatus: "none", heldCredentialIds: [] });
    assert.equal(result.blockers.length, 1);
  }],
  ["missing credential in a complete inventory is unmet", () => {
    const result = fit.evaluate([ncda], [], {
      inventoryStatus: "complete", heldCredentialIds: ["itil-foundation"]
    });
    assert.equal(result.blockers.length, 1);
  }],
  ["unconfigured credential inventory requires review", () => {
    const result = fit.evaluate([ncda], [], {
      inventoryStatus: "notConfigured", heldCredentialIds: []
    });
    assert.equal(result.reviews.length, 1);
    assert.equal(result.blockers.length, 0);
  }],
  ["open equivalence is not fabricated", () => {
    const result = fit.evaluate([{ ...ncda, equivalentAccepted: true }], [], {
      inventoryStatus: "complete", heldCredentialIds: ["netapp-ncse"]
    });
    assert.equal(result.reviews.length, 1);
    assert.equal(result.meets.length, 0);
    assert.equal(result.blockers.length, 0);
  }],
  ["catalog-defined equivalence is honored", () => {
    const result = fit.evaluate([{ ...ncda, equivalentCredentialIds: ["netapp-ncse"] }], [], {
      inventoryStatus: "complete", heldCredentialIds: ["netapp-ncse"]
    });
    assert.equal(result.meets.length, 1);
  }],
  ["unknown required credential is surfaced for review", () => {
    const result = fit.evaluate([], [{
      name: "Acme Certified Quantum Storage Administrator", requirement: "required"
    }], { inventoryStatus: "complete", heldCredentialIds: [] });
    assert.equal(result.reviews.length, 1);
    assert.equal(result.reviews[0].reason, "unrecognized");
  }],
  ["preferred credentials never become hard blockers", () => {
    const result = fit.evaluate([{ ...ncda, requirement: "preferred" }], [], {
      inventoryStatus: "none", heldCredentialIds: []
    });
    assert.equal(result.blockers.length, 0);
    assert.equal(result.reviews.length, 0);
  }]
];

for (const [name, test] of tests) {
  test();
  console.log(`PASS Credential fit: ${name}`);
}
console.log(`All ${tests.length} deterministic credential-fit tests passed.`);
