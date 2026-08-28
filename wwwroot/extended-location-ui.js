"use strict";

(function initializeExtendedLocationUi(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.ExtendedLocationUi = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function createExtendedLocationUi() {
  function hasDetection(analysis) {
    return analysis?.confidence === "strong" || analysis?.confidence === "questionable";
  }

  function knownDestination(analysis) {
    const value = typeof analysis?.destination === "string" ? analysis.destination.trim() : "";
    return value && value.toLocaleLowerCase() !== "destination not specified" ? value : "";
  }

  function destinationDisplay(analysis) {
    return (knownDestination(analysis) || "Destination not specified").toLocaleUpperCase();
  }

  function listBadge(analysis) {
    if (!hasDetection(analysis)) {
      return null;
    }
    const prefix = analysis.confidence === "strong"
      ? "Deployment / relocation"
      : "Possible deployment / relocation";
    const destination = knownDestination(analysis);
    return {
      confidence: analysis.confidence,
      text: destination ? `${prefix}: ${destination.toLocaleUpperCase()}` : prefix
    };
  }

  function requirementLine(analysis) {
    if (!hasDetection(analysis)) {
      return "";
    }
    const confidence = analysis.confidence === "strong" ? "Strong" : "Questionable";
    const summary = typeof analysis.summary === "string" && analysis.summary.trim()
      ? analysis.summary.trim()
      : "Deployment or relocation requires review";
    return `${confidence} requirement — ${summary}`;
  }

  return { destinationDisplay, listBadge, requirementLine };
});
