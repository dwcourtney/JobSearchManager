"use strict";

const assert = require("node:assert/strict");
const fit = require("../wwwroot/education-fit.js");

const mastersProfile = { level: "master", doctorateType: null };
function strictRequirement(minimumLevel, fields = []) {
  return {
    minimumLevel,
    specificDegree: null,
    requirementType: "strictDegree",
    experienceSubstitutionAccepted: false,
    preferredLevels: [],
    fields,
    parseStatus: "parsed"
  };
}
function badgeFor(academic, profile = mastersProfile) {
  const status = fit.evaluate(academic, profile);
  return { status, badge: fit.jobCardBadge(academic, status) };
}

for (const level of ["bachelor", "highSchool", "master"]) {
  const result = badgeFor(strictRequirement(level));
  assert.equal(result.status.kind, "meets");
  assert.equal(result.badge, null, `Master's profile should not show a satisfied ${level} badge.`);
}

const doctorate = badgeFor(strictRequirement("doctorate"));
assert.equal(doctorate.status.kind, "strictMismatch");
assert.deepEqual(doctorate.badge, {
  className: "education-mismatch-badge",
  text: "Inadequate Education",
  title: doctorate.status.explanation
});

const bachelorsProfile = { level: "bachelor", doctorateType: null };
const mastersRequired = badgeFor(strictRequirement("master"), bachelorsProfile);
assert.equal(mastersRequired.status.kind, "strictMismatch");
assert.equal(mastersRequired.badge?.text, "Inadequate Education");

const mastersPreferred = strictRequirement("bachelor");
mastersPreferred.preferredLevels = ["master"];
const preferredResult = badgeFor(mastersPreferred, bachelorsProfile);
assert.equal(preferredResult.status.kind, "meetsMinimumPreferredNotMet");
assert.equal(preferredResult.badge, null);

const fieldMismatch = fit.jobCardBadge(
  strictRequirement("bachelor", ["Civil Engineering"]),
  {
    kind: "strictFieldMismatch",
    explanation: "The configured field does not satisfy the required Civil Engineering field."
  }
);
assert.equal(fieldMismatch?.text, "Education Field Mismatch");

const relatedField = badgeFor(strictRequirement(
  "bachelor", ["Civil Engineering", "Related field accepted"]));
assert.equal(relatedField.status.kind, "meets");
assert.equal(relatedField.badge, null);

console.log("All deterministic education-fit and compact-badge tests passed.");
