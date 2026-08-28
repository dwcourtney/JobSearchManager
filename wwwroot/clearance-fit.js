"use strict";

(function registerClearanceFit(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.ClearanceFit = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, () => {
  const LEVEL_RANK = Object.freeze({ none: 0, secret: 1, topSecret: 2, topSecretSCI: 3 });

  function clearanceLabel(level) {
    return ({
      publicTrust: "Public Trust",
      secret: "Secret",
      topSecret: "Top Secret",
      topSecretSCI: "TS/SCI",
      other: "Other / unclear level"
    })[level] || "Clearance mentioned";
  }

  function profileClearanceLabel(level) {
    return ({
      notSpecified: "Not configured",
      none: "None",
      secret: "Secret",
      topSecret: "Top Secret",
      topSecretSCI: "TS/SCI",
      otherUnknown: "Other / Unknown"
    })[level] || "Not configured";
  }

  function publicTrustLabel(status) {
    return ({
      unknown: "Public Trust status unknown",
      none: "Not currently held",
      current: "Currently held / active"
    })[status] || "Public Trust status unknown";
  }

  function normalizeProfile(profile) {
    return {
      clearanceLevel: ["notSpecified", "none", "secret", "topSecret", "topSecretSCI", "otherUnknown"]
        .includes(profile?.clearanceLevel) ? profile.clearanceLevel : "notSpecified",
      publicTrust: ["unknown", "none", "current"].includes(profile?.publicTrust)
        ? profile.publicTrust : "unknown"
    };
  }

  function evaluate(job, candidateProfile) {
    const level = job?.clearanceLevel || "noneMentioned";
    const requirement = job?.clearanceRequirement || "none";
    const parseStatus = job?.clearanceParseStatus || "not-mentioned";
    const strict = requirement === "activeRequired" || requirement === "mustPossess";
    const profile = normalizeProfile(candidateProfile);
    const publicTrustJob = level === "publicTrust";
    const userLabel = publicTrustJob
      ? publicTrustLabel(profile.publicTrust)
      : profileClearanceLabel(profile.clearanceLevel);

    if (level === "noneMentioned" || requirement === "none") {
      return {
        kind: "noneSpecified", hide: false, strict: false, userLabel,
        summary: "No clearance requirement identified",
        explanation: "The posting does not state a recognized clearance requirement."
      };
    }
    if (parseStatus !== "parsed" || level === "other" || requirement === "ambiguous") {
      return {
        kind: "uncertain", hide: false, strict: false, userLabel,
        summary: "Clearance wording requires review",
        explanation: "The clearance language is uncertain, so this job remains visible."
      };
    }
    if (!strict) {
      const obtainable = ["obtain", "obtainAndMaintain", "eligible", "publicTrustSuitability"]
        .includes(requirement);
      return {
        kind: requirement === "preferred" ? "preferredOnly" : "notStrict",
        hide: false,
        strict: false,
        userLabel,
        summary: requirement === "preferred"
          ? "Clearance is preferred, not required"
          : obtainable
            ? "Obtainable after hire / not automatically disqualifying"
            : "Not a strict day-one hiring blocker",
        explanation: obtainable
          ? "The posting allows the clearance or suitability status to be obtained or established; it is not treated as an already-held requirement."
          : "Only explicit active/current/day-one requirements can hide a job."
      };
    }

    if (publicTrustJob) {
      if (profile.publicTrust === "unknown") {
        return {
          kind: "profileNotConfigured", hide: false, strict: true, userLabel,
          summary: "Strict Public Trust requirement; status unknown",
          explanation: "The posting explicitly requires current Public Trust status, but your separate Public Trust status is unknown. The job remains visible."
        };
      }
      if (profile.publicTrust !== "current") {
        return {
          kind: "strictMismatch", hide: true, strict: true, userLabel,
          summary: "Does not meet strict current Public Trust requirement",
          explanation: "The posting explicitly requires current Public Trust status, which your profile says you do not hold."
        };
      }
      return {
        kind: job.polygraphRequired ? "meetsLevelPolygraphReview" : "meets",
        hide: false, strict: true, userLabel,
        summary: job.polygraphRequired
          ? "Public Trust status meets; polygraph requires separate review"
          : "Meets strict current Public Trust requirement",
        explanation: job.polygraphRequired
          ? "Your Public Trust status matches, but this posting also requires a polygraph that the profile does not track."
          : "Your separately reported Public Trust status meets this strict requirement."
      };
    }

    if (!(level in LEVEL_RANK)) {
      return {
        kind: "uncertain", hide: false, strict: true, userLabel,
        summary: "Strict clearance language requires review",
        explanation: "The required clearance level could not be compared confidently, so this job remains visible."
      };
    }
    if (["notSpecified", "otherUnknown"].includes(profile.clearanceLevel)) {
      return {
        kind: "profileNotConfigured", hide: false, strict: true, userLabel,
        summary: "Strict clearance requirement; profile not comparable",
        explanation: "Choose a specific current clearance level in Settings to enable strict comparison. The job remains visible."
      };
    }
    if ((LEVEL_RANK[profile.clearanceLevel] ?? -1) < LEVEL_RANK[level]) {
      return {
        kind: "strictMismatch", hide: true, strict: true, userLabel,
        summary: "Does not meet strict current-clearance requirement",
        explanation: `The posting requires an active/current ${clearanceLabel(level)}, while your profile reports ${userLabel}.`
      };
    }
    return {
      kind: job.polygraphRequired ? "meetsLevelPolygraphReview" : "meets",
      hide: false, strict: true, userLabel,
      summary: job.polygraphRequired
        ? "Clearance level meets; polygraph requires separate review"
        : "Meets strict current-clearance requirement",
      explanation: job.polygraphRequired
        ? `Your ${userLabel} meets the clearance level, but this posting also requires a polygraph that the profile does not track.`
        : `Your ${userLabel} meets or exceeds the strict ${clearanceLabel(level)} requirement.`
    };
  }

  function jobCardBadges(job, status) {
    if (!status) return [];
    if (status.kind === "strictMismatch") {
      return [{
        className: "clearance-mismatch-badge",
        text: `${clearanceLabel(job?.clearanceLevel)} required`,
        title: status.explanation
      }];
    }
    if (status.kind === "meetsLevelPolygraphReview") {
      return [{
        className: "clearance-badge",
        text: "Polygraph — Review",
        title: status.explanation
      }];
    }
    return [];
  }

  function workAuthorizationBadges(analysis, status) {
    if (!status) return [];
    if (status.kind === "strictMismatch") {
      return [{
        className: "work-authorization-mismatch-badge",
        text: "Work authorization mismatch",
        title: status.explanation
      }];
    }
    if (status.kind === "profileNotConfigured") {
      return [{
        className: "work-authorization-badge",
        text: "Work authorization status unknown",
        title: status.explanation
      }];
    }
    if (status.kind === "review" && analysis) {
      return [{
        className: "work-authorization-badge",
        text: "Work authorization — Review",
        title: status.explanation
      }];
    }
    return [];
  }

  return { evaluate, jobCardBadges, workAuthorizationBadges };
});
