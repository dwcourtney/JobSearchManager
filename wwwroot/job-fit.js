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
    ideal: 2,
    positive: 1,
    negative: -3,
    hardConflict: -6
  });
  const LABELS = Object.freeze({
    hardConflict: "Hard Conflict",
    negative: "Negative",
    neutral: "Neutral",
    positive: "Positive",
    ideal: "Ideal"
  });
  const BASELINE = 5;
  const DIMENSION_LIMITS = Object.freeze({
    "Work Arrangement": Object.freeze({ minimum: -2, maximum: 1 }),
    "Role Type / Career Direction": Object.freeze({ minimum: -3, maximum: 2.5 }),
    "Technical Domain": Object.freeze({ minimum: -2, maximum: 1.5 }),
    "Work Environment": Object.freeze({ minimum: -3, maximum: 1 }),
    "Responsibility Shape": Object.freeze({ minimum: -2, maximum: 1.5 })
  });
  const DEFAULT_LIMITS = Object.freeze({ minimum: -2, maximum: 1 });
  const DIMENSION_ORDER = Object.freeze(Object.keys(DIMENSION_LIMITS));

  function normalizeConfiguration(configuration) {
    const seen = new Set();
    const signals = [];
    for (const signal of Array.isArray(configuration?.signals) ? configuration.signals : []) {
      if (!signal || typeof signal.conceptId !== "string" || seen.has(signal.conceptId)) {
        continue;
      }
      const preference = signal.preference === "strongNegative"
        ? "negative"
        : signal.preference === "strongPositive"
          ? "ideal"
          : signal.preference;
      seen.add(signal.conceptId);
      if (preference === "neutral") continue;
      if (!Object.hasOwn(WEIGHTS, preference)) continue;
      signals.push({ conceptId: signal.conceptId, preference });
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
    let contributions = normalized.signals
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
    const superseded = new Set();
    for (const contribution of contributions) {
      const concept = concepts.get(contribution.conceptId);
      for (const supersededId of Array.isArray(concept.supersedes) ? concept.supersedes : []) {
        superseded.add(supersededId);
      }
    }
    contributions = contributions.filter(contribution => !superseded.has(contribution.conceptId));

    const byCategory = new Map();
    for (const contribution of contributions) {
      if (!byCategory.has(contribution.category)) byCategory.set(contribution.category, []);
      byCategory.get(contribution.category).push(contribution);
    }
    const dimensions = Array.from(byCategory, ([category, signals]) => {
      const limits = DIMENSION_LIMITS[category] || DEFAULT_LIMITS;
      const rawImpact = signals.reduce((sum, signal) => sum + signal.impact, 0);
      const impact = Math.max(limits.minimum, Math.min(limits.maximum, rawImpact));
      return {
        category,
        impact,
        rawImpact,
        capped: impact !== rawImpact,
        signals
      };
    }).sort((left, right) => {
      const leftIndex = DIMENSION_ORDER.indexOf(left.category);
      const rightIndex = DIMENSION_ORDER.indexOf(right.category);
      if (leftIndex < 0 && rightIndex < 0) return left.category.localeCompare(right.category);
      if (leftIndex < 0) return 1;
      if (rightIndex < 0) return -1;
      return leftIndex - rightIndex;
    });

    const total = dimensions.reduce((sum, dimension) => sum + dimension.impact, BASELINE);
    let score = Math.max(1, Math.min(10, Math.round(total)));
    if (contributions.some(contribution => contribution.preference === "hardConflict")) {
      score = Math.min(score, 2);
    }

    return { score, baseline: BASELINE, dimensions, contributions };
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

    for (const dimension of result.dimensions) {
      const impact = formatImpact(dimension.impact);
      const bounded = dimension.capped
        ? ` (bounded from ${formatImpact(dimension.rawImpact)})`
        : "";
      lines.push("", `${dimension.category}: ${impact}${bounded}`);
      dimension.signals.forEach(item => lines.push(
        `- ${item.displayName} — ${item.preferenceLabel}: ${item.evidence}`));
    }
    return lines.join("\n");
  }

  function formatImpact(value) {
    const normalized = Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1);
    return `${value >= 0 ? "+" : ""}${normalized}`;
  }

  return {
    evaluate,
    normalizeConfiguration,
    scoreClass,
    tooltip,
    preferenceLabels: LABELS,
    baseline: BASELINE,
    dimensionLimits: DIMENSION_LIMITS
  };
});
