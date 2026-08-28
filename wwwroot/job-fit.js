"use strict";

(function initializeJobFit(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.JobFit = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function createJobFit() {
  const WEIGHTS = Object.freeze({
    strongPositive: 2,
    positive: 1,
    negative: -1,
    strongNegative: -3,
    hardConflict: -6
  });
  const LABELS = Object.freeze({
    strongPositive: "Strong Positive",
    positive: "Positive",
    negative: "Negative",
    strongNegative: "Strong Negative",
    hardConflict: "Hard Conflict"
  });

  function normalizeConfiguration(configuration) {
    const seen = new Set();
    const signals = [];
    for (const signal of Array.isArray(configuration?.signals) ? configuration.signals : []) {
      if (!signal || !Object.hasOwn(WEIGHTS, signal.preference) ||
          typeof signal.conceptId !== "string" || seen.has(signal.conceptId)) {
        continue;
      }
      seen.add(signal.conceptId);
      signals.push({ conceptId: signal.conceptId, preference: signal.preference });
    }
    return { enabled: configuration?.enabled === true, signals };
  }

  function evaluate(detectedConcepts, configuration, conceptOptions) {
    const normalized = normalizeConfiguration(configuration);
    if (!normalized.enabled) return null;

    const detected = new Map((Array.isArray(detectedConcepts) ? detectedConcepts : [])
      .filter(item => item && typeof item.conceptId === "string")
      .map(item => [item.conceptId, item]));
    const concepts = new Map((Array.isArray(conceptOptions) ? conceptOptions : [])
      .filter(item => item && typeof item.id === "string")
      .map(item => [item.id, item]));
    const contributions = normalized.signals
      .filter(signal => detected.has(signal.conceptId) && concepts.has(signal.conceptId))
      .map(signal => {
        const concept = concepts.get(signal.conceptId);
        return {
          conceptId: signal.conceptId,
          displayName: concept.displayName,
          category: concept.category,
          preference: signal.preference,
          preferenceLabel: LABELS[signal.preference],
          impact: WEIGHTS[signal.preference],
          evidence: detected.get(signal.conceptId).evidence || "Canonical concept detected"
        };
      });

    const total = contributions.reduce((sum, contribution) => sum + contribution.impact, 6);
    let score = Math.max(1, Math.min(10, Math.round(total)));
    if (contributions.some(contribution => contribution.preference === "hardConflict")) {
      score = Math.min(score, 2);
    }

    return { score, contributions };
  }

  function scoreClass(score) {
    if (score <= 3) return "score-low";
    if (score === 4) return "score-four";
    if (score === 5) return "score-five";
    if (score === 6) return "score-six";
    if (score === 7) return "score-seven";
    if (score === 8) return "score-eight";
    return "score-high";
  }

  function tooltip(result) {
    if (!result) return "";
    const lines = [`Job Fit: ${result.score}/10`];
    if (result.contributions.length === 0) {
      return `${lines[0]}\n\nNo configured Job Fit signals were detected.`;
    }

    const groups = [
      ["Hard conflicts", ["hardConflict"]],
      ["Strong negatives", ["strongNegative"]],
      ["Negatives", ["negative"]],
      ["Positives", ["positive", "strongPositive"]]
    ];
    for (const [heading, preferences] of groups) {
      const matches = result.contributions.filter(item => preferences.includes(item.preference));
      if (matches.length === 0) continue;
      lines.push("", `${heading}:`);
      matches.forEach(item => lines.push(`- ${item.displayName}: ${item.evidence}`));
    }
    return lines.join("\n");
  }

  return { evaluate, normalizeConfiguration, scoreClass, tooltip, preferenceLabels: LABELS };
});
