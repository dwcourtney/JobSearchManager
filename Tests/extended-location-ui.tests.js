"use strict";

const assert = require("node:assert/strict");
const ui = require("../wwwroot/extended-location-ui.js");

const locations = ["Guam", "Antarctica", "Kwajalein", "Diego Garcia", "Germany", "Japan"];
for (const location of locations) {
  const analysis = { confidence: "strong", destination: location, summary: "Deployment required" };
  assert.deepEqual(ui.listBadge(analysis), {
    confidence: "strong",
    text: `Deployment / relocation: ${location.toLocaleUpperCase()}`
  });
  assert.equal(ui.destinationDisplay(analysis), location.toLocaleUpperCase());
  assert.equal(ui.requirementLine(analysis), "Strong requirement — Deployment required");
}

assert.deepEqual(ui.listBadge({
  confidence: "questionable",
  destination: "Antarctica",
  summary: "Deployment may be required"
}), {
  confidence: "questionable",
  text: "Possible deployment / relocation: ANTARCTICA"
});
assert.deepEqual(ui.listBadge({
  confidence: "strong",
  destination: "Destination not specified",
  summary: "Extended site deployment required"
}), {
  confidence: "strong",
  text: "Deployment / relocation"
});
assert.deepEqual(ui.listBadge({
  confidence: "strong",
  destination: null,
  summary: "Deployment required"
}), {
  confidence: "strong",
  text: "Deployment / relocation"
});
assert.notDeepEqual(
  ui.listBadge({ confidence: "strong", destination: "Guam" }),
  ui.listBadge({ confidence: "questionable", destination: "Guam" })
);
assert.equal(ui.listBadge({ confidence: "none", destination: "Japan" }), null);
assert.equal(ui.requirementLine(null), "");

console.log("All deterministic extended-location rendering tests passed.");
