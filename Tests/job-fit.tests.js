"use strict";

const assert = require("node:assert/strict");
const JobFit = require("../wwwroot/job-fit.js");

const concept = (id, displayName, category, supersedes = [], options = {}) =>
  ({ id, displayName, category, supersedes, ...options });
const detected = concepts => concepts.map(item =>
  ({ conceptId: item.id, evidence: `Evidence for ${item.displayName}` }));
const detectedWithEvidence = (item, evidence) => ({ conceptId: item.id, evidence });
const configuration = (signals, travelTolerance = 4, preferredWorkLocation = 3) =>
  ({ enabled: true, signals, travelTolerance, preferredWorkLocation });
const signal = (item, preference) => ({ conceptId: item.id, preference });

const remote = concept("work.remote", "Remote Work", "Work Arrangement", [],
  { userConfigurable: false, workLocationLevel: 2 });
const fullRemote = concept("work.remote.full", "100% Remote Work", "Work Arrangement", ["work.remote"],
  { userConfigurable: false, workLocationLevel: 0 });
const hybrid = concept("work.hybrid", "Hybrid Work", "Work Arrangement", [],
  { userConfigurable: false, workLocationLevel: 3 });
const onsite = concept("work.onsite", "Onsite Work", "Work Arrangement", [],
  { userConfigurable: false, workLocationLevel: 5 });
const softwareRole = concept("role.software-engineering", "Software Engineering", "Role Type / Career Direction");
const infrastructureRole = concept("role.infrastructure-engineering", "Infrastructure Engineering", "Role Type / Career Direction");
const devopsRole = concept("role.devops-platform", "DevOps / Platform Engineering", "Role Type / Career Direction");
const dataCenter = concept("work.data-center", "Data Center", "Work Environment");
const physical = concept("work.physical-infrastructure", "Physical Infrastructure", "Work Environment");
const deployment = concept("work.deployment", "Deployment", "Work Arrangement");
const extendedAway = concept("work.extended-away-assignment", "Extended Away-from-Home Assignment", "Work Arrangement");
const international = concept("work.international-assignment", "International / OCONUS Assignment", "Work Arrangement");
const rotation = concept("work.rotation", "Rotational Assignment", "Work Arrangement");
const relocation = concept("work.relocation", "Relocation", "Work Arrangement");
const occasionalTravel = concept("work.travel.occasional", "Occasional Travel", "Work Arrangement", [],
  { userConfigurable: false, travelLevel: 3 });
const moderateTravel = concept("work.travel.moderate", "Moderate Travel", "Work Arrangement", [occasionalTravel.id],
  { userConfigurable: false, travelLevel: 4 });
const frequentTravel = concept("work.travel.frequent", "Frequent Travel", "Work Arrangement", [],
  { userConfigurable: false, travelLevel: 5 });
const substantialTravel = concept("work.travel.substantial", "Substantial Travel", "Work Arrangement",
  [frequentTravel.id, moderateTravel.id, occasionalTravel.id],
  { userConfigurable: false, travelLevel: 6 });
const handsOn = concept("responsibility.hands-on-implementation", "Hands-on Implementation", "Responsibility Shape");
const ai = concept("technical.artificial-intelligence", "Artificial Intelligence", "Technical Domain");
const ml = concept("technical.machine-learning", "Machine Learning", "Technical Domain");
const nlp = concept("technical.nlp", "Natural Language Processing", "Technical Domain");
const llm = concept("technical.large-language-models", "Large Language Models", "Technical Domain");
const linux = concept("technical.linux", "Linux", "Technical Domain");
const cloud = concept("technical.cloud", "Cloud Platforms", "Technical Domain");
const cicd = concept("technical.cicd", "CI/CD", "Technical Domain");

assert.equal(JobFit.evaluate([], { enabled: false, signals: [] }, []), null,
  "Disabled Job Fit must not produce an assessment.");

const sparse = JobFit.normalizeConfiguration(configuration([
  signal(remote, "neutral"),
  signal(hybrid, "strongNegative"),
  signal(fullRemote, "strongPositive"),
  signal(cloud, "negative")
]));
assert.deepEqual(sparse.signals, [
  signal(hybrid, "negative"),
  signal(fullRemote, "ideal"),
  signal(cloud, "negative")
], "Neutral must be omitted and legacy preference names must migrate to Negative and Ideal.");
assert.equal(JobFit.evaluate(detected([remote]), configuration([signal(remote, "neutral")]), [remote]).score, 5,
  "Neutral must contribute zero and retain the baseline score.");
assert.equal(JobFit.preferenceLabels.strongNegative, undefined);
assert.equal(JobFit.preferenceLabels.strongPositive, undefined,
  "Legacy names must not remain available as extra UI states.");
assert.equal(JobFit.preferenceLabels.negative, "Negative");
assert.equal(JobFit.preferenceLabels.ideal, "Ideal");
assert.equal(sparse.travelTolerance, 4, "New configurations must use the non-rejecting moderate default.");
assert.equal(JobFit.normalizeTravelTolerance(2.5), 4, "Intermediate travel values must not be accepted.");
assert.deepEqual(JobFit.travelLevels.map(item => item.level), [0, 1, 2, 3, 4, 5, 6]);
assert.deepEqual(JobFit.travelLevels.map(item => item.label), [
  "No travel", "Extremely rare", "Very light", "Occasional", "Moderate", "Heavy", "Travel-heavy"
]);
assert.equal(sparse.preferredWorkLocation, 3,
  "New configurations must default to the neutral corpus center, Hybrid.");
assert.equal(JobFit.normalizePreferredWorkLocation(2.5), 3,
  "Intermediate work-location values must not be accepted.");
assert.deepEqual(JobFit.workLocationLevels.map(item => item.level), [0, 1, 2, 3, 4, 5]);

const locationConcepts = [fullRemote, remote, hybrid, onsite];
const locationCases = [
  [fullRemote, "This position is 100% remote.", 0],
  [remote, "Remote role with quarterly office visits.", 1],
  [remote, "This is a mostly remote position.", 2],
  [hybrid, "This is a hybrid role.", 3],
  [onsite, "This role is mostly onsite with limited flexibility.", 4],
  [onsite, "This role is fully onsite.", 5],
  [onsite, "Work onsite 2 days per week and remote 3 days.", 3],
  [onsite, "Work onsite 4 days per week with one remote day.", 4],
  [onsite, "This position is currently remote but will transition onsite.", 5]
];
for (const [locationConcept, evidence, expectedLevel] of locationCases) {
  const result = JobFit.evaluate(
    [detectedWithEvidence(locationConcept, evidence)],
    configuration([], 4, expectedLevel),
    locationConcepts);
  const comparison = [...result.contributions, ...result.neutralSignals]
    .find(item => item.locationComparison)?.locationComparison;
  assert.equal(comparison.detectedLevel, expectedLevel, evidence);
  assert.equal(comparison.distance, 0);
  assert.equal(comparison.impact, 1);
}

const locationDistanceImpacts = [1, 0, -1, -2, -3, -4];
for (let preferred = 0; preferred <= 5; preferred += 1) {
  const result = JobFit.evaluate(
    [detectedWithEvidence(fullRemote, "This position is 100% remote.")],
    configuration([], 4, preferred), locationConcepts);
  const contribution = [...result.contributions, ...result.neutralSignals]
    .find(item => item.locationComparison);
  assert.equal(contribution.locationComparison.distance, preferred);
  assert.equal(contribution.impact, locationDistanceImpacts[preferred]);
  assert.notEqual(contribution.preference, "hardConflict",
    "Normal-location distance must never create a Hard Conflict by itself.");
}
const conflictResolution = JobFit.evaluate([
  detectedWithEvidence(remote, "Remote role"),
  detectedWithEvidence(onsite, "Currently remote but will transition onsite after integration.")
], configuration([], 4, 5), locationConcepts);
assert.equal(conflictResolution.contributions[0].locationComparison.detectedLevel, 5);
assert.equal(conflictResolution.contributions[0].locationComparison.precedence,
  "future required onsite arrangement");

const travelConcepts = [occasionalTravel, moderateTravel, frequentTravel, substantialTravel];
const travelCases = [
  [occasionalTravel, "This role requires occasional travel.", 3],
  [occasionalTravel, "This role requires up to 10% travel.", 3],
  [moderateTravel, "This role requires 25% travel.", 4],
  [moderateTravel, "This role requires 40% travel.", 5],
  [substantialTravel, "This role requires 50% travel.", 5],
  [substantialTravel, "This role requires 75% travel.", 6],
  [frequentTravel, "Frequent travel is required.", 5],
  [frequentTravel, "Extensive travel is required.", 5],
  [occasionalTravel, "Travel, as needed, for client meetings.", 3],
  [occasionalTravel, "Travel typically lasting no more than one week may be required.", 3],
  [occasionalTravel, "At most one short trip every 2-3 years.", 1],
  [occasionalTravel, "One short trip every 12-18 months.", 2]
];
for (const [travelConcept, evidence, expectedLevel] of travelCases) {
  const result = JobFit.evaluate(
    [detectedWithEvidence(travelConcept, evidence)],
    configuration([], 6),
    travelConcepts);
  const comparison = result.neutralSignals[0].travelComparison;
  assert.equal(comparison.detectedLevel, expectedLevel, evidence);
  assert.equal(comparison.tolerance, 6);
  assert.equal(comparison.result, "neutral");
}

const atTolerance = JobFit.evaluate(
  [detectedWithEvidence(moderateTravel, "This role requires 25% travel.")],
  configuration([], 4), travelConcepts);
assert.equal(atTolerance.score, 5);
assert.equal(atTolerance.neutralSignals[0].conceptId, "work.travel.tolerance");
assert.ok(!atTolerance.neutralSignals.some(item =>
  item.conceptId !== "work.travel.tolerance" && item.conceptId.startsWith("work.travel.")),
  "Internal travel concepts must not appear as legacy preference rows in scoring output.");

const oneAboveTolerance = JobFit.evaluate(
  [detectedWithEvidence(moderateTravel, "This role requires 40% travel.")],
  configuration([], 4), travelConcepts);
assert.equal(oneAboveTolerance.contributions[0].preference, "negative");
assert.equal(oneAboveTolerance.dimensions[0].rawImpact, -3);
assert.equal(oneAboveTolerance.dimensions[0].impact, -2,
  "Travel must retain the existing Work Arrangement category bound.");

const twoAboveTolerance = JobFit.evaluate(
  [detectedWithEvidence(frequentTravel, "Frequent travel is required.")],
  configuration([], 3), [...travelConcepts, deployment]);
assert.equal(twoAboveTolerance.contributions[0].preference, "hardConflict");
assert.equal(twoAboveTolerance.score, 2);
assert.equal(twoAboveTolerance.hardConflictCap.applied, true);
assert.match(JobFit.tooltip(twoAboveTolerance), /Detected level: 5 - Heavy/);
assert.match(JobFit.tooltip(twoAboveTolerance), /Your maximum: 3 - Occasional/);

const separateArrangementConcepts = [deployment, extendedAway, international, rotation, relocation];
const travelAndDeployment = JobFit.evaluate(
  [
    detectedWithEvidence(occasionalTravel, "Occasional travel is required."),
    ...separateArrangementConcepts.map(item =>
      detectedWithEvidence(item, `Evidence for ${item.displayName}`))
  ],
  configuration(separateArrangementConcepts.map(item => signal(item, "hardConflict")), 3),
  [...travelConcepts, ...separateArrangementConcepts]);
assert.equal(travelAndDeployment.neutralSignals[0].conceptId, "work.travel.tolerance");
assert.deepEqual(travelAndDeployment.hardConflictCap.signals.map(item => item.conceptId).sort(),
  separateArrangementConcepts.map(item => item.id).sort(),
  "Deployment, extended-away, OCONUS, rotation, and relocation must remain independent from ordinary business-travel tolerance.");

const remoteOnly = JobFit.evaluate(detected([fullRemote]),
  configuration([], 4, 0), [fullRemote]);
assert.equal(remoteOnly.score, 6,
  "100% Remote Work alone must not produce a high overall score.");
assert.equal(remoteOnly.dimensions[0].impact, 1,
  "Work Arrangement positives must respect their category maximum.");

const boundedArrangement = JobFit.evaluate([
  detectedWithEvidence(remote, "Remote role"),
  detectedWithEvidence(fullRemote, "This position is 100% remote."),
  detectedWithEvidence(hybrid, "Hybrid role")
], configuration([], 4, 0), [remote, fullRemote, hybrid]);
assert.equal(boundedArrangement.dimensions[0].impact, 1);
assert.equal(boundedArrangement.dimensions[0].rawImpact, 1,
  "Location detectors must collapse to one distance contribution without double-counting.");
assert.deepEqual(boundedArrangement.contributions.map(item => item.conceptId),
  ["work.location.preference"]);
assert.equal(boundedArrangement.dimensionBreakdown[0].rawImpact, 1);
assert.equal(boundedArrangement.dimensionBreakdown[0].impact, 1);
assert.equal(boundedArrangement.dimensionBreakdown[0].capped, false);

const boundedExtendedAssignment = JobFit.evaluate(detected([deployment, extendedAway]),
  configuration([signal(deployment, "negative"), signal(extendedAway, "negative")]),
  [deployment, extendedAway]);
assert.equal(boundedExtendedAssignment.dimensions[0].impact, -2);
assert.equal(boundedExtendedAssignment.dimensions[0].rawImpact, -6);
assert.equal(boundedExtendedAssignment.dimensionBreakdown[0].capped, true,
  "Deployment and extended-assignment preferences must coexist but remain category-bounded.");

const aiCluster = JobFit.evaluate(detected([ai, ml, nlp, llm]),
  configuration([ai, ml, nlp, llm].map(item => signal(item, "ideal"))),
  [ai, ml, nlp, llm]);
assert.equal(aiCluster.dimensions[0].impact, 1.5);
assert.equal(aiCluster.score, 7,
  "Correlated AI concepts must not inflate Technical Domain without limit.");

const wrongRole = JobFit.evaluate(
  detected([fullRemote, infrastructureRole, linux, cloud, cicd, dataCenter, physical]),
  configuration([
    signal(infrastructureRole, "negative"),
    signal(linux, "positive"), signal(cloud, "positive"), signal(cicd, "positive"),
    signal(dataCenter, "negative"), signal(physical, "negative")
  ], 4, 0),
  [fullRemote, infrastructureRole, linux, cloud, cicd, dataCenter, physical]);
assert.equal(wrongRole.score, 2,
  "Role and environment incompatibility must outweigh incidental technology overlap.");
assert.equal(wrongRole.dimensions.find(item => item.category === "Role Type / Career Direction").impact, -3);
assert.equal(wrongRole.dimensions.find(item => item.category === "Work Environment").impact, -3);

const hardConflict = JobFit.evaluate(detected([fullRemote, softwareRole, deployment]),
  configuration([
    signal(softwareRole, "ideal"),
    signal(deployment, "hardConflict")
  ], 4, 0), [fullRemote, softwareRole, deployment]);
assert.equal(hardConflict.score, 2, "A hard conflict must still cap the overall score.");
assert.equal(hardConflict.scoreBeforeHardConflictCap, 5);
assert.equal(hardConflict.hardConflictCap.applied, true);
assert.equal(hardConflict.hardConflictCap.maximum, 2);
assert.deepEqual(hardConflict.hardConflictCap.signals.map(item => item.conceptId), [deployment.id],
  "The explanation result must identify the signal imposing the global cap.");

const individualSoftwarePreference = configuration([signal(softwareRole, "ideal")]);
const softwareBeforeOverride = JobFit.evaluate(
  detected([softwareRole]), individualSoftwarePreference, [softwareRole]);
const softwareWithOverride = JobFit.evaluate(
  detected([softwareRole]),
  { ...individualSoftwarePreference, groupHardConflicts: ["software-development"] },
  [softwareRole]);
const softwareAfterOverride = JobFit.evaluate(
  detected([softwareRole]), individualSoftwarePreference, [softwareRole]);
assert.equal(softwareBeforeOverride.contributions[0].preference, "ideal");
assert.equal(softwareWithOverride.score, 2,
  "A section override must invoke the existing Hard Conflict score cap.");
assert.equal(softwareWithOverride.contributions[0].preference, "hardConflict");
assert.equal(softwareWithOverride.contributions[0].groupOverrideId, "software-development");
assert.deepEqual(softwareAfterOverride.contributions, softwareBeforeOverride.contributions,
  "Removing a section override must restore the preserved individual preference exactly.");
const simultaneousOverrides = JobFit.evaluate(
  detected([softwareRole, ai]),
  {
    ...configuration([signal(softwareRole, "positive"), signal(ai, "negative")]),
    groupHardConflicts: ["software-development", "ai-data"]
  },
  [softwareRole, ai]);
assert.equal(simultaneousOverrides.hardConflictCap.maximum, 2);
assert.equal(simultaneousOverrides.hardConflictCap.signals.length, 2,
  "Multiple section overrides must coexist in the existing Hard Conflict cap explanation.");
assert.deepEqual(
  simultaneousOverrides.contributions.map(item => [item.conceptId, item.groupOverrideId]),
  [[softwareRole.id, "software-development"], [ai.id, "ai-data"]],
  "Each simultaneous section override must retain its deterministic section owner.");
const normalizedOverrides = JobFit.normalizeConfiguration({
  ...individualSoftwarePreference,
  groupHardConflicts: ["systems-administration", "software-development",
    "software-development", "not-a-real-group"]
});
assert.deepEqual(normalizedOverrides.groupHardConflicts,
  ["software-development", "systems-administration"],
  "Section overrides must be validated, deduplicated, and normalized deterministically.");

const neutralDetection = JobFit.evaluate(
  detected([cloud, linux]),
  configuration([signal(cloud, "positive")]),
  [cloud, linux]);
assert.equal(neutralDetection.score, 6);
assert.deepEqual(neutralDetection.neutralSignals.map(item => item.conceptId), [linux.id]);
assert.equal(neutralDetection.neutralSignals[0].impact, 0);
assert.equal(neutralDetection.neutralSignals[0].evidence, "Evidence for Linux");
assert.ok(!neutralDetection.neutralSignals.some(item => item.conceptId === cicd.id),
  "Concepts not detected in the job must not enter the explanation result.");
assert.equal(neutralDetection.dimensionBreakdown.length, 5,
  "The explanation result must expose every authoritative bounded scoring dimension.");

const aligned = JobFit.evaluate(
  detected([fullRemote, softwareRole, devopsRole, cloud, cicd, linux, handsOn]),
  configuration([
    signal(softwareRole, "ideal"), signal(devopsRole, "ideal"),
    signal(cloud, "ideal"), signal(cicd, "positive"), signal(linux, "positive"),
    signal(handsOn, "ideal")
  ], 4, 0), [fullRemote, softwareRole, devopsRole, cloud, cicd, linux, handsOn]);
assert.equal(aligned.score, 10, "A genuinely aligned software/cloud role should still score highly.");

const lowerBound = JobFit.evaluate(
  detected([deployment, infrastructureRole, dataCenter, linux, handsOn]),
  configuration([
    signal(deployment, "hardConflict"), signal(infrastructureRole, "hardConflict"),
    signal(dataCenter, "hardConflict"), signal(linux, "hardConflict"), signal(handsOn, "hardConflict")
  ]), [deployment, infrastructureRole, dataCenter, linux, handsOn]);
assert.equal(lowerBound.score, 1, "The final score must remain bounded at 1.");
assert.ok(aligned.score <= 10, "The final score must remain bounded at 10.");

const explanation = JobFit.tooltip(wrongRole);
assert.match(explanation, /Work Arrangement: \+1/);
assert.match(explanation, /Normal Work Location — Ideal/);
assert.match(explanation, /Distance: 0 levels/);
assert.match(explanation, /Role Type \/ Career Direction: -3/);
assert.match(explanation, /Technical Domain: \+1\.5 \(bounded from \+3\)/);
assert.match(explanation, /Work Environment: -3 \(bounded from -6\)/);
assert.match(explanation, /Infrastructure Engineering — Negative/);
assert.doesNotMatch(explanation, /\n- Remote Work —/,
  "Explanations must exclude superseded or non-contributing signals.");

console.log("All bounded Job Fit scoring tests passed.");
