"use strict";

(function registerJobWorkflowState(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.JobWorkflowState = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, () => {
  const STATES = Object.freeze({
    normal: "normal",
    saved: "saved",
    applied: "applied",
    closed: "closed",
    hidden: "hidden"
  });
  const VALID_STATES = new Set(Object.values(STATES));

  function normalizeState(state) {
    return VALID_STATES.has(state) ? state : STATES.normal;
  }

  function stateForJob(stableId, jobStates) {
    return normalizeState(jobStates.get(stableId));
  }

  function belongsToTab(stableId, tab, jobStates) {
    return stateForJob(stableId, jobStates) === normalizeState(tab);
  }

  function transition(currentState, action) {
    const current = normalizeState(currentState);
    switch (action) {
      case "save": return current === STATES.normal ? STATES.saved : current;
      case "unsave": return current === STATES.saved ? STATES.normal : current;
      case "apply": return current === STATES.normal || current === STATES.saved
        ? STATES.applied
        : current;
      case "unapply": return current === STATES.applied ? STATES.normal : current;
      case "close": return current === STATES.applied ? STATES.closed : current;
      case "reopen": return current === STATES.closed ? STATES.applied : current;
      case "hide": return current === STATES.normal || current === STATES.saved ||
        current === STATES.applied ? STATES.hidden : current;
      case "restore": return current === STATES.hidden ? STATES.normal : current;
      default: return current;
    }
  }

  function applyTransition(stableId, action, jobStates) {
    const next = transition(stateForJob(stableId, jobStates), action);
    jobStates.set(stableId, next);
    return next;
  }

  return { STATES, normalizeState, stateForJob, belongsToTab, transition, applyTransition };
});
