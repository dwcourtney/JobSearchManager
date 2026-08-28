"use strict";

(function registerJobUnseenState(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.JobUnseenState = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, () => {
  function isUnseen(unseenIds, stableId) {
    return unseenIds instanceof Set && unseenIds.has(stableId);
  }

  function applyToCard(card, unseenIds, stableId) {
    card.classList.toggle("unseen", isUnseen(unseenIds, stableId));
  }

  function markViewed(unseenIds, stableId) {
    if (!isUnseen(unseenIds, stableId)) return false;
    unseenIds.delete(stableId);
    return true;
  }

  function restoreUnseen(unseenIds, stableId) {
    unseenIds.add(stableId);
  }

  return { isUnseen, applyToCard, markViewed, restoreUnseen };
});
