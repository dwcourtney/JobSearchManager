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
      return { kind: "nonBlocking", credential };
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

  function evaluate(credentials, unknownRequirements, candidateProfile) {
    const profile = normalizedProfile(candidateProfile);
    const known = (Array.isArray(credentials) ? credentials : [])
      .map(credential => assessKnown(credential, profile));
    const unknown = (Array.isArray(unknownRequirements) ? unknownRequirements : [])
      .filter(credential => credential?.requirement === "required")
      .map(credential => ({ kind: "review", reason: "unrecognized", credential }));
    const all = [...known, ...unknown];
    return {
      assessments: all,
      blockers: all.filter(item => item.kind === "strictMismatch"),
      reviews: all.filter(item => item.kind === "review"),
      meets: all.filter(item => item.kind === "meets")
    };
  }

  return { evaluate };
});
