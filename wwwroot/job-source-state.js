"use strict";

(function registerJobSourceState(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.JobSourceState = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, () => {
  function normalizedId(value) {
    const id = typeof value === "string" ? value.trim() : "";
    return id || null;
  }

  function normalizedLocationIds(source) {
    const values = Array.isArray(source?.physicalLocationIds)
      ? source.physicalLocationIds
      : Array.isArray(source?.physicalLocations)
        ? source.physicalLocations.map(location => location?.id)
        : [];
    return [...new Set(values.map(normalizedId).filter(Boolean))]
      .sort((left, right) => left.localeCompare(right));
  }

  function normalize(source, options = {}) {
    const includeAllLocations = source?.includeAllLocations === true;
    const remoteAvailable = options.remoteAvailable !== false;
    return {
      companyId: normalizedId(source?.companyId),
      countryId: normalizedId(source?.countryId),
      includeAllLocations,
      includeRemote: remoteAvailable &&
        (includeAllLocations || source?.includeRemote === true),
      physicalLocationIds: includeAllLocations ? [] : normalizedLocationIds(source)
    };
  }

  function areEquivalent(left, right, options = {}) {
    const normalizedLeft = normalize(left, options);
    const normalizedRight = normalize(right, options);
    return normalizedLeft.companyId === normalizedRight.companyId &&
      normalizedLeft.countryId === normalizedRight.countryId &&
      normalizedLeft.includeAllLocations === normalizedRight.includeAllLocations &&
      normalizedLeft.includeRemote === normalizedRight.includeRemote &&
      normalizedLeft.physicalLocationIds.length === normalizedRight.physicalLocationIds.length &&
      normalizedLeft.physicalLocationIds.every((id, index) =>
        id === normalizedRight.physicalLocationIds[index]);
  }

  function hasValidSelection(source, options = {}) {
    const normalized = normalize(source, options);
    return normalized.companyId !== null &&
      (normalized.includeAllLocations || normalized.includeRemote ||
        normalized.physicalLocationIds.length > 0);
  }

  function navigationDecision(
    activeView,
    nextView,
    hasAppliedSource,
    applied,
    editable,
    options = {}) {
    if (activeView !== "settings" || nextView !== "jobs") return "allow";
    if (hasAppliedSource) {
      return areEquivalent(applied, editable, options) ? "allow" : "guard";
    }
    return hasValidSelection(editable, options) ? "guard" : "require-source";
  }

  return {
    normalize,
    areEquivalent,
    hasValidSelection,
    navigationDecision
  };
});
