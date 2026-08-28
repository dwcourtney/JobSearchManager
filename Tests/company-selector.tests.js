"use strict";

const assert = require("node:assert/strict");
const { groupCompanies } = require("../wwwroot/company-selector.js");

const companies = [
  { id: "leidos", displayName: "Leidos", industryCategory: "Defense & Federal" },
  { id: "mtm", displayName: "MTM", industryCategory: "Healthcare & Transportation Services" },
  { id: "boeing", displayName: "Boeing", industryCategory: "Defense & Federal" },
  { id: "nvidia", displayName: "NVIDIA", industryCategory: "Semiconductors" },
  { id: "nxp-semiconductors", displayName: "NXP Semiconductors", industryCategory: "Semiconductors" }
];

const groups = groupCompanies(companies);
assert.deepEqual(groups.map(group => group.category), [
  "Defense & Federal",
  "Healthcare & Transportation Services",
  "Semiconductors"
]);
assert.deepEqual(groups[0].companies.map(company => company.id), ["leidos", "boeing"]);
assert.deepEqual(groups[2].companies.map(company => company.id), ["nvidia", "nxp-semiconductors"]);
assert.deepEqual(groupCompanies(null), []);
assert.deepEqual(groupCompanies([{ id: "invalid", displayName: "Invalid" }]), []);

console.log("All deterministic company-selector category tests passed.");
