(function publishDetectorEvaluationUi(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.DetectorEvaluationUi = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function createDetectorEvaluationUi() {
  "use strict";

  function tierKey(tier) {
    if (String(tier || "").startsWith("Tier 1")) return "tier1";
    if (String(tier || "").startsWith("Tier 2")) return "tier2";
    return "tier3";
  }

  function matchesMetric(metric, filter, query) {
    const normalizedFilter = filter || "all";
    const normalizedQuery = String(query || "").trim().toLocaleLowerCase();
    const searchable = `${metric.concept || ""} ${metric.conceptId || ""} ${metric.category || ""}`
      .toLocaleLowerCase();
    const hasErrors = Number(metric.falsePositive || 0) + Number(metric.falseNegative || 0) > 0;
    const filterMatch = normalizedFilter === "all" ||
      tierKey(metric.tier) === normalizedFilter ||
      normalizedFilter === "evaluated" && metric.evaluated === true ||
      normalizedFilter === "not-evaluated" && metric.evaluated !== true ||
      normalizedFilter === "errors" && hasErrors;
    return filterMatch && (!normalizedQuery || searchable.includes(normalizedQuery));
  }

  function filterMetrics(metrics, filter, query) {
    return (metrics || []).filter(metric => matchesMetric(metric, filter, query));
  }

  return Object.freeze({ tierKey, matchesMetric, filterMetrics });
});
