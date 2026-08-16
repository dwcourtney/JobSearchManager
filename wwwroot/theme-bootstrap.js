"use strict";

// This is only an early paint hint. The backend settings JSON remains the
// authority and app.js replaces this value as soon as settings are loaded.
try {
  const hint = localStorage.getItem("job-search-manager-theme-hint") ||
    localStorage.getItem("workday-job-manager-theme-hint") ||
    localStorage.getItem("leidos-jobs-theme-hint");
  if (hint === "light" || hint === "dark") {
    document.documentElement.dataset.theme = hint;
  }
} catch {
  // Storage can be unavailable in hardened browser modes; default light is safe.
}
