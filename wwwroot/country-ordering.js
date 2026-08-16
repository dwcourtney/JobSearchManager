"use strict";

globalThis.JobSourceCountryOrdering = (() => {
  const DEFAULT_REGION = "US";
  const COUNTRY_LABEL_BY_REGION = Object.freeze({
    US: "United States of America",
    GB: "United Kingdom",
    CA: "Canada",
    AU: "Australia",
    DE: "Germany"
  });

  function browserLocales(browserNavigator = globalThis.navigator) {
    const locales = browserNavigator?.languages
      ? Array.from(browserNavigator.languages)
      : [];
    if (browserNavigator?.language) locales.push(browserNavigator.language);
    return locales.filter(locale => typeof locale === "string" && locale.trim());
  }

  function regionFromLocale(locale) {
    try {
      return new Intl.Locale(locale).region?.toUpperCase() || null;
    } catch {
      return null;
    }
  }

  function countryLabelForRegion(region) {
    const mapped = COUNTRY_LABEL_BY_REGION[region];
    if (mapped) return mapped;
    try {
      return new Intl.DisplayNames(["en"], { type: "region" }).of(region) || null;
    } catch {
      return null;
    }
  }

  function findCountryByLabel(countries, label) {
    if (!label) return null;
    return countries.find(country =>
      country.label?.localeCompare(label, undefined, { sensitivity: "accent" }) === 0) || null;
  }

  function prioritizedCountry(countries, locales) {
    for (const locale of locales) {
      const region = regionFromLocale(locale);
      const inferred = region ? findCountryByLabel(countries, countryLabelForRegion(region)) : null;
      if (inferred) return inferred;
    }
    return findCountryByLabel(countries, COUNTRY_LABEL_BY_REGION[DEFAULT_REGION]);
  }

  function orderCountryFacets(countries, locales = browserLocales()) {
    const alphabetical = [...countries].sort((left, right) =>
      (left.label || "").localeCompare(right.label || "", undefined, { sensitivity: "base" }));
    const prioritized = prioritizedCountry(alphabetical, locales);
    if (!prioritized) return alphabetical;
    return [prioritized, ...alphabetical.filter(country => country.id !== prioritized.id)];
  }

  return Object.freeze({
    browserLocales,
    orderCountryFacets
  });
})();
