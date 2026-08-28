"use strict";

const assert = require("node:assert/strict");
const JobFit = require("../wwwroot/job-fit.js");

const concept = (id, displayName, category, supersedes = []) =>
  ({ id, displayName, category, supersedes });
const detected = concepts => concepts.map(item =>
  ({ conceptId: item.id, evidence: `Evidence for ${item.displayName}` }));
const configuration = signals => ({ enabled: true, signals });
const signal = (item, preference) => ({ conceptId: item.id, preference });

const remote = concept("work.remote", "Remote Work", "Work Arrangement");
const fullRemote = concept("work.remote.full", "100% Remote Work", "Work Arrangement", ["work.remote"]);
const hybrid = concept("work.hybrid", "Hybrid Work", "Work Arrangement");
const softwareRole = concept("role.software-engineering", "Software Engineering", "Role Type / Career Direction");
const infrastructureRole = concept("role.infrastructure-engineering", "Infrastructure Engineering", "Role Type / Career Direction");
const devopsRole = concept("role.devops-platform", "DevOps / Platform Engineering", "Role Type / Career Direction");
const dataCenter = concept("work.data-center", "Data Center", "Work Environment");
const physical = concept("work.physical-infrastructure", "Physical Infrastructure", "Work Environment");
const deployment = concept("work.deployment", "Deployment", "Work Arrangement");
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
  signal(hybrid, "negative"),
  signal(fullRemote, "positive")
]));
assert.deepEqual(sparse.signals, [
  signal(hybrid, "strongNegative"),
  signal(fullRemote, "positive")
], "Neutral must be omitted and legacy Negative must migrate explicitly to Strong Negative.");
assert.equal(JobFit.evaluate(detected([remote]), configuration([signal(remote, "neutral")]), [remote]).score, 5,
  "Neutral must contribute zero and retain the baseline score.");
assert.equal(JobFit.preferenceLabels.negative, undefined,
  "Ordinary Negative must not remain available as a sixth UI state.");

const remoteOnly = JobFit.evaluate(detected([fullRemote]),
  configuration([signal(fullRemote, "strongPositive")]), [fullRemote]);
assert.equal(remoteOnly.score, 6,
  "100% Remote Work alone must not produce a high overall score.");
assert.equal(remoteOnly.dimensions[0].impact, 1,
  "Work Arrangement positives must respect their category maximum.");

const boundedArrangement = JobFit.evaluate(detected([remote, fullRemote, hybrid]),
  configuration([
    signal(remote, "strongPositive"),
    signal(fullRemote, "strongPositive"),
    signal(hybrid, "strongPositive")
  ]), [remote, fullRemote, hybrid]);
assert.equal(boundedArrangement.dimensions[0].impact, 1);
assert.equal(boundedArrangement.dimensions[0].rawImpact, 4,
  "The superseded Remote Work signal must be removed before category scoring.");
assert.deepEqual(boundedArrangement.contributions.map(item => item.conceptId).sort(),
  [fullRemote.id, hybrid.id].sort(),
  "100% Remote Work must supersede plain Remote Work without hiding other concepts.");

const aiCluster = JobFit.evaluate(detected([ai, ml, nlp, llm]),
  configuration([ai, ml, nlp, llm].map(item => signal(item, "strongPositive"))),
  [ai, ml, nlp, llm]);
assert.equal(aiCluster.dimensions[0].impact, 1.5);
assert.equal(aiCluster.score, 7,
  "Correlated AI concepts must not inflate Technical Domain without limit.");

const wrongRole = JobFit.evaluate(
  detected([fullRemote, infrastructureRole, linux, cloud, cicd, dataCenter, physical]),
  configuration([
    signal(fullRemote, "strongPositive"),
    signal(infrastructureRole, "strongNegative"),
    signal(linux, "positive"), signal(cloud, "positive"), signal(cicd, "positive"),
    signal(dataCenter, "strongNegative"), signal(physical, "strongNegative")
  ]),
  [fullRemote, infrastructureRole, linux, cloud, cicd, dataCenter, physical]);
assert.equal(wrongRole.score, 2,
  "Role and environment incompatibility must outweigh incidental technology overlap.");
assert.equal(wrongRole.dimensions.find(item => item.category === "Role Type / Career Direction").impact, -3);
assert.equal(wrongRole.dimensions.find(item => item.category === "Work Environment").impact, -3);

const hardConflict = JobFit.evaluate(detected([fullRemote, softwareRole, deployment]),
  configuration([
    signal(fullRemote, "strongPositive"),
    signal(softwareRole, "strongPositive"),
    signal(deployment, "hardConflict")
  ]), [fullRemote, softwareRole, deployment]);
assert.equal(hardConflict.score, 2, "A hard conflict must still cap the overall score.");

const aligned = JobFit.evaluate(
  detected([fullRemote, softwareRole, devopsRole, cloud, cicd, linux, handsOn]),
  configuration([
    signal(fullRemote, "strongPositive"),
    signal(softwareRole, "strongPositive"), signal(devopsRole, "strongPositive"),
    signal(cloud, "strongPositive"), signal(cicd, "positive"), signal(linux, "positive"),
    signal(handsOn, "strongPositive")
  ]), [fullRemote, softwareRole, devopsRole, cloud, cicd, linux, handsOn]);
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
assert.match(explanation, /Work Arrangement: \+1 \(bounded from \+2\)/);
assert.match(explanation, /Role Type \/ Career Direction: -3/);
assert.match(explanation, /Technical Domain: \+1\.5 \(bounded from \+3\)/);
assert.match(explanation, /Work Environment: -3 \(bounded from -6\)/);
assert.match(explanation, /Infrastructure Engineering — Strong Negative/);
assert.doesNotMatch(explanation, /\n- Remote Work —/,
  "Explanations must exclude superseded or non-contributing signals.");

console.log("All bounded Job Fit scoring tests passed.");
