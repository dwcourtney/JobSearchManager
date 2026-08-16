"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

delete globalThis.CountryOrdering;
delete globalThis.JobSourceCountryOrdering;
delete globalThis.WorkdayCountryOrdering;
require("../wwwroot/country-ordering.js");

const ordering = globalThis.CountryOrdering;
const countries = [
  { id: "de", label: "Germany" },
  { id: "ca", label: "Canada" },
  { id: "us", label: "United States of America" }
];

const tests = [
  ["publishes one canonical generic runtime API", () => {
    assert.equal(typeof ordering?.orderCountryFacets, "function");
    assert.equal(globalThis.JobSourceCountryOrdering, undefined);
    assert.equal(globalThis.WorkdayCountryOrdering, undefined);
  }],
  ["prioritizes the browser locale country", () => {
    assert.deepEqual(
      ordering.orderCountryFacets(countries, ["en-CA"]).map(country => country.id),
      ["ca", "de", "us"]);
  }],
  ["falls back to the United States for an unavailable locale", () => {
    assert.deepEqual(
      ordering.orderCountryFacets(countries, ["fr-FR"]).map(country => country.id),
      ["us", "ca", "de"]);
  }],
  ["application consumer uses the published runtime API", () => {
    const app = fs.readFileSync(path.join(__dirname, "../wwwroot/app.js"), "utf8");
    assert.match(app, /\bCountryOrdering\.orderCountryFacets\b/);
    assert.doesNotMatch(app, /\b(?:Workday|JobSource)CountryOrdering\b/);
  }]
];

let failed = 0;
for (const [name, run] of tests) {
  try {
    run();
    console.log(`PASS Country ordering: ${name}`);
  } catch (error) {
    failed++;
    console.error(`FAIL Country ordering: ${name}`);
    console.error(error);
  }
}

if (failed) process.exitCode = 1;
else console.log(`All ${tests.length} deterministic country-ordering tests passed.`);
