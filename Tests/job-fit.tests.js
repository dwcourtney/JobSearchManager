"use strict";

const assert = require("node:assert/strict");
const JobFit = require("../wwwroot/job-fit.js");

const concepts = [
  { id: "positive.one", displayName: "Machine Learning", category: "Technical Domain" },
  { id: "positive.two", displayName: "Linux", category: "Technical Domain" },
  { id: "positive.three", displayName: "Remote Work", category: "Work Arrangement" },
  { id: "negative.one", displayName: "Onsite Work", category: "Work Arrangement" },
  { id: "negative.two", displayName: "Deployment", category: "Work Arrangement" }
];
const detected = concepts.map(concept => ({
  conceptId: concept.id,
  evidence: `Evidence for ${concept.displayName}`
}));
const configuration = signals => ({ enabled: true, signals });

assert.equal(JobFit.evaluate(detected, { enabled: false, signals: [] }, concepts), null,
  "Disabled Job Fit must not produce an assessment.");

const positive = JobFit.evaluate(detected, configuration([
  { conceptId: "positive.one", preference: "strongPositive" },
  { conceptId: "positive.two", preference: "positive" }
]), concepts);
assert.equal(positive.score, 9, "Positive signals should raise the neutral score.");

const negative = JobFit.evaluate(detected, configuration([
  { conceptId: "negative.one", preference: "negative" },
  { conceptId: "negative.two", preference: "strongNegative" }
]), concepts);
assert.equal(negative.score, 2, "Negative signals should lower the neutral score.");

const hardConflict = JobFit.evaluate(detected, configuration([
  { conceptId: "positive.one", preference: "strongPositive" },
  { conceptId: "positive.two", preference: "strongPositive" },
  { conceptId: "positive.three", preference: "strongPositive" },
  { conceptId: "negative.two", preference: "hardConflict" }
]), concepts);
assert.equal(hardConflict.score, 2,
  "A hard conflict must cap the score despite multiple strong positives.");

const lowerBound = JobFit.evaluate(detected, configuration([
  { conceptId: "negative.one", preference: "hardConflict" },
  { conceptId: "negative.two", preference: "hardConflict" }
]), concepts);
assert.equal(lowerBound.score, 1, "Scores must be bounded at 1.");

const upperConcepts = Array.from({ length: 5 }, (_, index) => ({
  id: `upper.${index}`, displayName: `Upper ${index}`, category: "Test"
}));
const upperBound = JobFit.evaluate(
  upperConcepts.map(concept => ({ conceptId: concept.id, evidence: concept.displayName })),
  configuration(upperConcepts.map(concept => ({
    conceptId: concept.id, preference: "strongPositive"
  }))),
  upperConcepts);
assert.equal(upperBound.score, 10, "Scores must be bounded at 10.");

const explained = JobFit.evaluate(
  [{ conceptId: "positive.one", evidence: "Machine learning systems" }],
  configuration([
    { conceptId: "positive.one", preference: "positive" },
    { conceptId: "negative.one", preference: "strongNegative" }
  ]),
  concepts);
const tooltip = JobFit.tooltip(explained);
assert.match(tooltip, /Machine Learning: Machine learning systems/);
assert.doesNotMatch(tooltip, /Onsite Work/,
  "Explanations must not include configured signals that were not detected.");

console.log("All Job Fit scoring tests passed.");
