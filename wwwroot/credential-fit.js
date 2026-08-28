"use strict";

(function registerCredentialFit(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.CredentialFit = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, () => {
  function normalizedProfile(profile) {
    const inventoryStatus = ["none", "complete"].includes(profile?.inventoryStatus)
      ? profile.inventoryStatus
      : "notConfigured";
    const heldCredentialIds = new Set(
      Array.isArray(profile?.heldCredentialIds)
        ? profile.heldCredentialIds.filter(Boolean).map(id => String(id).toLowerCase())
        : []);
    return { inventoryStatus, heldCredentialIds };
  }

  function assessKnown(credential, profile) {
    if (credential?.requirement !== "required") {
      if (credentialHeld(credential, profile)) {
        return { kind: "meets", reason: "heldNonBlocking", credential };
      }
      return {
        kind: "nonBlocking",
        reason: profile.inventoryStatus === "notConfigured"
          ? "profileNotConfiguredNonBlocking"
          : "preferredNotHeld",
        credential
      };
    }

    const credentialId = String(credential.credentialId || "").toLowerCase();
    const equivalentIds = Array.isArray(credential.equivalentCredentialIds)
      ? credential.equivalentCredentialIds.map(id => String(id).toLowerCase())
      : [];
    if (profile.heldCredentialIds.has(credentialId) ||
        equivalentIds.some(id => profile.heldCredentialIds.has(id))) {
      return { kind: "meets", credential };
    }
    if (credential.postHireAcquisitionAllowed) {
      return { kind: "review", reason: "postHire", credential };
    }
    if (profile.inventoryStatus === "notConfigured") {
      return { kind: "review", reason: "profileNotConfigured", credential };
    }
    if (profile.inventoryStatus === "none") {
      return { kind: "strictMismatch", reason: "confirmedNoneHeld", credential };
    }
    if (credential.equivalentAccepted || credential.isAlternative) {
      return { kind: "review", reason: "openAlternative", credential };
    }
    return { kind: "strictMismatch", reason: "notInCompleteInventory", credential };
  }

  function credentialHeld(credential, profile) {
    const credentialId = String(credential?.credentialId || "").toLowerCase();
    const equivalentIds = Array.isArray(credential?.equivalentCredentialIds)
      ? credential.equivalentCredentialIds.map(id => String(id).toLowerCase())
      : [];
    return profile.heldCredentialIds.has(credentialId) ||
      equivalentIds.some(id => profile.heldCredentialIds.has(id));
  }

  function assessAlternativeGroup(credentials, profile) {
    const satisfied = credentials.filter(credential => credentialHeld(credential, profile));
    const base = { credential: credentials[0], credentials };
    if (satisfied.length) {
      return { ...base, kind: "meets", reason: "alternativeHeld", satisfiedCredentials: satisfied };
    }
    if (credentials.some(credential => credential.postHireAcquisitionAllowed)) {
      return { ...base, kind: "review", reason: "postHire" };
    }
    if (profile.inventoryStatus === "notConfigured") {
      return { ...base, kind: "review", reason: "profileNotConfigured" };
    }
    if (credentials.some(credential => credential.equivalentAccepted)) {
      return { ...base, kind: "review", reason: "openAlternative" };
    }
    return {
      ...base,
      kind: "strictMismatch",
      reason: profile.inventoryStatus === "none" ? "confirmedNoneHeld" : "notInCompleteInventory"
    };
  }

  function evaluate(credentials, unknownRequirements, candidateProfile) {
    const profile = normalizedProfile(candidateProfile);
    const credentialList = Array.isArray(credentials) ? credentials : [];
    const groupedIds = new Set();
    const known = [];
    const alternativeGroups = new Map();
    credentialList.forEach(credential => {
      if (credential?.requirement === "required" && credential.alternativeGroup) {
        if (!alternativeGroups.has(credential.alternativeGroup)) {
          alternativeGroups.set(credential.alternativeGroup, []);
        }
        alternativeGroups.get(credential.alternativeGroup).push(credential);
        groupedIds.add(credential);
      }
    });
    for (const group of alternativeGroups.values()) {
      if (group.length > 1) known.push(assessAlternativeGroup(group, profile));
      else {
        groupedIds.delete(group[0]);
      }
    }
    credentialList.filter(credential => !groupedIds.has(credential))
      .forEach(credential => known.push(assessKnown(credential, profile)));
    const unknown = (Array.isArray(unknownRequirements) ? unknownRequirements : [])
      .filter(credential => credential?.requirement === "required")
      .map(credential => ({ kind: "review", reason: "unrecognized", credential }));
    const all = [...known, ...unknown];
    return {
      assessments: all,
      blockers: all.filter(item => item.kind === "strictMismatch"),
      reviews: all.filter(item => item.kind === "review"),
      meets: all.filter(item => item.kind === "meets"),
      nonBlocking: all.filter(item => item.kind === "nonBlocking")
    };
  }

  function assessmentCredentials(assessment) {
    return Array.isArray(assessment?.credentials)
      ? assessment.credentials
      : assessment?.credential ? [assessment.credential] : [];
  }

  function assessmentLabel(assessment) {
    return assessmentCredentials(assessment)
      .map(credential => credential.name || credential.fullName || "Credential")
      .join(" or ");
  }

  function jobCardBadges(status) {
    if (!status) return [];
    const blockers = (status.blockers || []).map(assessment => ({
      className: "credential-badge required",
      text: `${assessmentLabel(assessment)} required`,
      title: "Required credential is not in your configured inventory."
    }));
    const reviewReasons = new Set((status.reviews || []).map(item => item.reason));
    const reviews = [];
    if (reviewReasons.has("unrecognized") || reviewReasons.has("openAlternative")) {
      reviews.push({
        className: "credential-badge",
        text: "Credential requirement — review",
        title: "A required credential or allowed equivalent needs manual review."
      });
    } else if (reviewReasons.has("profileNotConfigured")) {
      reviews.push({
        className: "credential-badge",
        text: "Credential status unknown",
        title: "Configure your credential inventory to assess this requirement."
      });
    }
    const badges = [...blockers, ...reviews];
    if (badges.length <= 2) return badges;
    return [badges[0], {
      className: "credential-badge required",
      text: `+${badges.length - 1} credential issues`,
      title: "Additional credential requirements need attention."
    }];
  }

  return { evaluate, assessmentCredentials, assessmentLabel, jobCardBadges };
});
