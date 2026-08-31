"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const production = fs.readFileSync(path.join(root, "deploy", "compose.curiosity.yaml"), "utf8");
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
const scoring = fs.readFileSync(path.join(root, "wwwroot", "job-fit.js"), "utf8");

const classifierBlock = production.split("  job-classifier:")[1].split("\nnetworks:")[0];
assert.ok(classifierBlock, "production Compose must define job-classifier");
assert.doesNotMatch(classifierBlock, /^\s*ports:/m, "classifier must not publish a host/LAN port");
assert.doesNotMatch(classifierBlock, /docker\.sock|\/app\/data|dataprotection|mailpit|ai801/i,
  "classifier must not mount or reference production data, Docker, Mailpit, or ai801");
assert.match(classifierBlock, /read_only: true/);
assert.match(classifierBlock, /no-new-privileges:true/);
assert.match(classifierBlock, /cap_drop:\s*\n\s*- ALL/);
assert.match(classifierBlock, /gpus: all/);
assert.match(production, /classifier:\s*\n\s*internal: true/,
  "classifier network must be private");

assert.match(program,
  /MapPost\("\/api\/admin\/classifier-diagnostic"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/,
  "diagnostic endpoint must enforce the server-side admin policy");
assert.doesNotMatch(scoring, /classifier/i,
  "normal Job Fit scoring must remain independent of the experimental classifier");

console.log("Classifier isolation and scoring architecture tests: PASS");
