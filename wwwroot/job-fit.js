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
    const configured = new Map(normalized.signals.map(signal => [signal.conceptId, signal.preference]));
    const detectedSignals = Array.from(detected.values())
      .filter(item => concepts.has(item.conceptId))
      .map(item => {
        const concept = concepts.get(item.conceptId);
        const preference = configured.get(item.conceptId) || "neutral";
        return {
          conceptId: item.conceptId,
          displayName: concept.displayName,
          category: concept.category,
          preference,
          preferenceLabel: LABELS[preference],
          impact: preference === "neutral" ? 0 : WEIGHTS[preference],
          evidence: item.evidence || "Canonical concept detected"
        };
      });
    const neutralSignals = detectedSignals.filter(signal => signal.preference === "neutral");
    const candidates = detectedSignals.filter(signal => signal.preference !== "neutral");
    const candidateIds = new Set(candidates.map(signal => signal.conceptId));
    const supersededBy = new Map();
    for (const contribution of candidates) {
      const concept = concepts.get(contribution.conceptId);
      for (const supersededId of Array.isArray(concept.supersedes) ? concept.supersedes : []) {
        if (!candidateIds.has(supersededId)) continue;
        if (!supersededBy.has(supersededId)) supersededBy.set(supersededId, []);
        supersededBy.get(supersededId).push(contribution.conceptId);
      }
    }
    const supersededSignals = candidates
      .filter(signal => supersededBy.has(signal.conceptId))
      .map(signal => ({
        ...signal,
        supersededBy: supersededBy.get(signal.conceptId).map(id => concepts.get(id).displayName)
      }));
    const contributions = candidates.filter(signal => !supersededBy.has(signal.conceptId));

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
        limits,
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

    const dimensionBreakdown = DIMENSION_ORDER.map(category => {
      const dimension = dimensions.find(item => item.category === category);
      const limits = DIMENSION_LIMITS[category];
      return dimension || {
        category,
        impact: 0,
        rawImpact: 0,
        capped: false,
        limits,
        signals: []
      };
    }).map(dimension => ({
      ...dimension,
      neutralSignals: neutralSignals.filter(signal => signal.category === dimension.category),
      supersededSignals: supersededSignals.filter(signal => signal.category === dimension.category)
    }));

    const calculatedTotal = dimensions.reduce((sum, dimension) => sum + dimension.impact, BASELINE);
    const scoreBeforeHardConflictCap = Math.max(1, Math.min(10, Math.round(calculatedTotal)));
    const hardConflictSignals = contributions.filter(
      contribution => contribution.preference === "hardConflict");
    let score = scoreBeforeHardConflictCap;
    if (hardConflictSignals.length > 0) {
      score = Math.min(score, 2);
    }

    return {
      score,
      baseline: BASELINE,
      calculatedTotal,
      scoreBeforeHardConflictCap,
      dimensions,
      dimensionBreakdown,
      contributions,
      neutralSignals,
      supersededSignals,
      hardConflictCap: {
        maximum: 2,
        applied: hardConflictSignals.length > 0 && scoreBeforeHardConflictCap > 2,
        signals: hardConflictSignals
      }
    };
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
