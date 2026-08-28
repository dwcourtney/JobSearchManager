"use strict";

(function publishCompanySelector(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.CompanySelector = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function createCompanySelector() {
  function groupCompanies(companies) {
    const groups = new Map();
    for (const company of Array.isArray(companies) ? companies : []) {
      const category = String(company?.industryCategory || "").trim();
      if (!category) continue;
      if (!groups.has(category)) groups.set(category, []);
      groups.get(category).push(company);
    }
    return [...groups].map(([category, members]) => ({ category, companies: members }));
  }

  return { groupCompanies };
});
