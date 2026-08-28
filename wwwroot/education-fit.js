"use strict";

(function initializeEducationFit(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.EducationFit = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function createEducationFit() {
  const LEVEL_RANK = Object.freeze({
    noCredential: 0,
    ged: 1,
    highSchool: 1,
    associate: 2,
    bachelor: 3,
    master: 4,
    doctorate: 5
  });

  function normalizeLevel(value) {
    return ["notSpecified", "noCredential", "ged", "highSchool", "associate", "bachelor", "master", "doctorate"]
      .includes(value) ? value : "notSpecified";
  }

  function levelLabel(level, specificDegree = null) {
    if (level === "doctorate" && specificDegree === "phD") return "Ph.D.";
    return ({
      highSchool: "High School/GED",
      associate: "Associate",
      bachelor: "Bachelor's",
      master: "Master's",
      doctorate: "Doctorate",
      noneSpecified: "None specified"
    })[level] || "Academic qualification";
  }

  function profileLabel(profile) {
    if (profile?.level === "doctorate" && profile?.doctorateType === "phD") return "Ph.D.";
    return ({
      notSpecified: "Not configured",
      noCredential: "No high school credential",
      ged: "GED",
      highSchool: "High school diploma",
      associate: "Associate degree",
      bachelor: "Bachelor's degree",
      master: "Master's degree",
      doctorate: "Doctorate"
    })[profile?.level] || "No high school credential";
  }

  function evaluate(academic, profile) {
    const user = {
      level: normalizeLevel(profile?.level),
      doctorateType: profile?.level === "doctorate" && profile?.doctorateType === "phD"
        ? "phD" : null
    };
    const userLabel = profileLabel(user);
    if (user.level === "notSpecified") {
      return {
        kind: "profileNotConfigured", hide: false, userLabel,
        summary: "Education profile not configured",
        explanation: "Choose your highest completed education in Settings to enable personal comparison."
      };
    }
    if (!academic || academic.requirementType === "noDegreeSpecified") {
      return {
        kind: "noneSpecified", hide: false, userLabel,
        summary: "No academic requirement specified",
        explanation: "The posting does not state a recognized academic requirement."
      };
    }

    const requiredLabel = levelLabel(academic.minimumLevel, academic.specificDegree);
    if (academic.parseStatus !== "parsed" || academic.requirementType === "mentionedUnclear") {
      return {
        kind: "uncertain", hide: false, userLabel, requiredLabel,
        summary: "Academic wording is uncertain",
        explanation: "The parser found academic language but will not use it to exclude this job."
      };
    }
    if (academic.requirementType === "preferredOnly") {
      return {
        kind: "preferredOnly", hide: false, userLabel, requiredLabel,
        summary: `${requiredLabel} preferred`,
        explanation: "This is a preference, not a strict minimum requirement."
      };
    }
    if (academic.experienceSubstitutionAccepted ||
        academic.requirementType === "degreeOrExperience" ||
        academic.requirementType === "degreeWithExperienceSubstitution") {
      return {
        kind: "flexible", hide: false, userLabel, requiredLabel,
        summary: academic.requirementType === "degreeWithExperienceSubstitution"
          ? "Alternative degree/experience paths"
          : `${requiredLabel} or experience alternative`,
        explanation: "The posting provides an experience or alternate education path, so no automatic mismatch is applied."
      };
    }
    if (academic.requirementType !== "strictDegree") {
      return {
        kind: "uncertain", hide: false, userLabel, requiredLabel,
        summary: "Academic requirement is not strict",
        explanation: "This academic language is informational and will not exclude the job."
      };
    }

    const requiredRank = LEVEL_RANK[academic.minimumLevel] ?? 0;
    const userRank = LEVEL_RANK[user.level] ?? 0;
    if (academic.minimumLevel === "doctorate" && academic.specificDegree === "phD" &&
        user.level === "doctorate" && user.doctorateType !== "phD") {
      return {
        kind: "specificDegreeUncertain", hide: false, userLabel, requiredLabel,
        summary: "Specific Ph.D. requirement is uncertain",
        explanation: "You reported a doctorate without specifying Ph.D.; the application will not assume equivalence or hide this job."
      };
    }
    if (requiredRank > userRank) {
      return {
        kind: "strictMismatch", hide: true, userLabel, requiredLabel,
        summary: `${requiredLabel} required`,
        explanation: `The posting's strict ${requiredLabel} requirement is above your completed ${userLabel}.`
      };
    }

    const preferredLevels = Array.isArray(academic.preferredLevels) ? academic.preferredLevels : [];
    const unmetPreferred = preferredLevels
      .filter(level => (LEVEL_RANK[level] ?? 0) > userRank)
      .sort((left, right) => (LEVEL_RANK[left] ?? 0) - (LEVEL_RANK[right] ?? 0))[0];
    return {
      kind: unmetPreferred ? "meetsMinimumPreferredNotMet" : "meets",
      hide: false,
      userLabel,
      requiredLabel,
      preferredLabel: unmetPreferred ? levelLabel(unmetPreferred) : null,
      summary: unmetPreferred
        ? `Meets minimum; ${levelLabel(unmetPreferred)} preferred`
        : "Meets strict education requirement",
      explanation: unmetPreferred
        ? `Your ${userLabel} meets the strict ${requiredLabel} minimum; ${levelLabel(unmetPreferred)} is preferred.`
        : `Your ${userLabel} meets or exceeds the strict ${requiredLabel} requirement.`
    };
  }

  function jobCardBadge(academic, status) {
    if (!academic || !status ||
        (status.kind !== "strictMismatch" && status.kind !== "strictFieldMismatch")) {
      return null;
    }
    const fieldMismatch = status.kind === "strictFieldMismatch";
    return {
      className: "education-mismatch-badge",
      text: fieldMismatch ? "Education Field Mismatch" : "Inadequate Education",
      title: status.explanation || "The configured education profile does not satisfy this requirement."
    };
  }

  return { evaluate, jobCardBadge };
});
