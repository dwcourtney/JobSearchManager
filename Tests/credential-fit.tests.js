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
    assert.equal(result.nonBlocking.length, 1);
  }],
  ["held preferred credentials remain visible in the full assessment", () => {
    const result = fit.evaluate([{ ...ncda, requirement: "preferred" }], [], {
      inventoryStatus: "complete", heldCredentialIds: ["netapp-ncda"]
    });
    assert.equal(result.meets.length, 1);
    assert.equal(result.blockers.length, 0);
    assert.deepEqual(fit.jobCardBadges(result), []);
  }],
  ["explicit PMP or CCM requirement is met by either alternative", () => {
    const alternatives = [
      { credentialId: "pmp", name: "PMP", requirement: "required", alternativeGroup: "g1" },
      { credentialId: "ccm", name: "CCM", requirement: "required", alternativeGroup: "g1" }
    ];
    const result = fit.evaluate(alternatives, [], {
      inventoryStatus: "complete", heldCredentialIds: ["ccm"]
    });
    assert.equal(result.meets.length, 1);
    assert.equal(result.blockers.length, 0);
    assert.equal(fit.assessmentLabel(result.meets[0]), "PMP or CCM");
  }],
  ["missing every explicit OR alternative is one blocker", () => {
    const result = fit.evaluate([
      { credentialId: "pmp", name: "PMP", requirement: "required", alternativeGroup: "g1" },
      { credentialId: "ccm", name: "CCM", requirement: "required", alternativeGroup: "g1" }
    ], [], { inventoryStatus: "complete", heldCredentialIds: [] });
    assert.equal(result.blockers.length, 1);
    assert.equal(fit.jobCardBadges(result)[0].text, "PMP or CCM required");
  }],
  ["catalog-defined SecurityX hierarchy satisfies Security+", () => {
    const result = fit.evaluate([{
      credentialId: "security-plus", name: "Security+", requirement: "required",
      equivalentCredentialIds: ["securityx"]
    }], [], { inventoryStatus: "complete", heldCredentialIds: ["securityx"] });
    assert.equal(result.meets.length, 1);
    assert.equal(result.blockers.length, 0);
  }],
  ["post-hire acquisition is reviewable but not a compact warning", () => {
    const result = fit.evaluate([{ ...ncda, postHireAcquisitionAllowed: true }], [], {
      inventoryStatus: "complete", heldCredentialIds: []
    });
    assert.equal(result.reviews[0].reason, "postHire");
    assert.deepEqual(fit.jobCardBadges(result), []);
  }],
  ["unknown inventory does not fabricate a missing-credential blocker", () => {
    const result = fit.evaluate([ncda], [], {
      inventoryStatus: "notConfigured", heldCredentialIds: []
    });
    assert.equal(result.blockers.length, 0);
    assert.equal(fit.jobCardBadges(result)[0].text, "Credential status unknown");
  }],
  ["job cards show problems but suppress satisfied and preferred credentials", () => {
    const result = fit.evaluate([
      ncda,
      { credentialId: "pmp", name: "PMP", requirement: "preferred" }
    ], [], { inventoryStatus: "complete", heldCredentialIds: ["netapp-ncda"] });
    assert.deepEqual(fit.jobCardBadges(result), []);
  }]
];

for (const [name, test] of tests) {
  test();
  console.log(`PASS Credential fit: ${name}`);
}
console.log(`All ${tests.length} deterministic credential-fit tests passed.`);
