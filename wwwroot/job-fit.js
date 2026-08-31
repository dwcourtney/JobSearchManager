"use strict";

(function initializeJobFit(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.JobFit = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function createJobFit() {
  const WEIGHTS = Object.freeze({
    ideal: 2,
    positive: 1,
    negative: -3,
    hardConflict: -6
  });
  const LABELS = Object.freeze({
    hardConflict: "Hard Conflict",
    negative: "Negative",
    neutral: "Neutral",
    positive: "Positive",
    ideal: "Ideal"
  });
  const TRAVEL_LEVELS = Object.freeze([
    Object.freeze({ level: 0, label: "No travel", shortLabel: "No travel", description: "No required business travel." }),
    Object.freeze({ level: 1, label: "Extremely rare", shortLabel: "Extremely rare travel", description: "At most one short trip every 2-3 years." }),
    Object.freeze({ level: 2, label: "Very light", shortLabel: "Very light travel", description: "About one short trip every 12-18 months." }),
    Object.freeze({ level: 3, label: "Occasional", shortLabel: "Occasional travel", description: "A few short trips per year; roughly up to 10% travel." }),
    Object.freeze({ level: 4, label: "Moderate", shortLabel: "Moderate travel", description: "Travel is a recurring part of the job; roughly 10-25%." }),
    Object.freeze({ level: 5, label: "Heavy", shortLabel: "Heavy travel", description: "Travel is substantial; roughly 25-50% of the role." }),
    Object.freeze({ level: 6, label: "Travel-heavy", shortLabel: "Travel-heavy", description: "Frequent or extensive travel is acceptable, including roles above 50%." })
  ]);
  const DEFAULT_TRAVEL_TOLERANCE = 4;
  const WORK_LOCATION_LEVELS = Object.freeze([
    Object.freeze({ level: 0, label: "100% Remote", description: "Work is fully remote with no routine onsite requirement." }),
    Object.freeze({ level: 1, label: "Remote with rare office visits", description: "Remote is the norm; occasional in-person visits may be required, perhaps once or twice per year." }),
    Object.freeze({ level: 2, label: "Mostly remote", description: "Remote most of the time with occasional onsite work." }),
    Object.freeze({ level: 3, label: "Hybrid", description: "A regular mix of remote and onsite work." }),
    Object.freeze({ level: 4, label: "Mostly onsite", description: "Onsite work is the norm with some remote flexibility." }),
    Object.freeze({ level: 5, label: "Fully onsite", description: "Routine work is performed onsite." })
  ]);
  const DEFAULT_PREFERRED_WORK_LOCATION = 3;
  // Both canonical detectors remain active for scoring and persisted settings. The
  // role-level concept duplicates the technical-domain concept in the survey, so
  // only the latter owns the single user-facing preference row.
  const SURVEY_HIDDEN_CONCEPT_IDS = Object.freeze(["role.network-engineering"]);
  const BASELINE = 5;
  const DIMENSION_LIMITS = Object.freeze({
    "Work Arrangement": Object.freeze({ minimum: -2, maximum: 1 }),
    "Role Type / Career Direction": Object.freeze({ minimum: -3, maximum: 2.5 }),
    "Technical Domain": Object.freeze({ minimum: -2, maximum: 1.5 }),
    "Work Environment": Object.freeze({ minimum: -3, maximum: 1 }),
    "Responsibility Shape": Object.freeze({ minimum: -2, maximum: 1.5 })
  });
  const DEFAULT_LIMITS = Object.freeze({ minimum: -2, maximum: 1 });
  const DIMENSION_ORDER = Object.freeze(Object.keys(DIMENSION_LIMITS));
  const section = (id, title, conceptIds, hardConflictId = null) => Object.freeze({
    id,
    title,
    hardConflictId,
    conceptIds: Object.freeze(conceptIds)
  });
  const tab = (id, title, sections, options = {}) => Object.freeze({
    id,
    title,
    specialControls: Object.freeze(options.specialControls || []),
    sections: Object.freeze(sections)
  });
  const SURVEY_TABS = Object.freeze([
    tab("work-arrangement", "Work Arrangement", [
      section("assignment-location", "Assignment / Location Constraints", [
        "work.deployment", "work.extended-away-assignment", "work.international-assignment",
        "work.rotation", "work.relocation"
      ])
    ], { specialControls: ["travel-tolerance", "normal-work-location", "work-arrangement-filtering"] }),
    tab("career-direction", "Career Direction", [
      section("technical-career-direction", "Technical Engineering", [
        "role.individual-contributor", "role.systems-engineering", "role.cybersecurity"
      ])
    ]),
    tab("software-ai-data", "Software / AI / Data", [
      section("software-development", "Software Development", [
        "role.software-engineering", "technical.api-development",
        "technical.application-development", "technical.backend-development",
        "technical.frontend-development", "technical.software-development",
        "technical.embedded-systems"
      ], "software-development"),
      section("ai-data", "AI / Data", [
        "role.ai-ml-engineering", "role.data-engineering", "role.data-science",
        "technical.artificial-intelligence", "technical.machine-learning",
        "technical.large-language-models", "technical.nlp"
      ], "ai-data")
    ]),
    tab("cloud-infrastructure-it", "Cloud / Infrastructure / IT", [
      section("cloud-platform-automation", "Cloud / Platform / Automation", [
        "role.cloud-engineering", "role.devops-platform", "technical.cloud",
        "technical.cicd", "technical.infrastructure-as-code", "technical.containers",
        "technical.automation-scripting", "technical.virtualization"
      ], "cloud-platform-automation"),
      section("systems-administration", "Systems / Administration", [
        "role.infrastructure-engineering", "technical.linux",
        "technical.linux-administration", "technical.windows-administration",
        "technical.storage"
      ], "systems-administration"),
      section("network-physical-infrastructure", "Network / Physical Infrastructure", [
        "role.network-engineering", "technical.networking", "technical.cisco-networking",
        "technical.cabling-racking", "technical.power-facilities"
      ], "network-physical-infrastructure")
    ]),
    tab("hardware-field", "Hardware / Field", [
      section("hardware-field-engineering", "Hardware / Field Engineering", [
        "role.hardware-engineering", "role.field-service", "role.test-validation-engineering"
      ]),
      section("trades-technician-work", "Trades / Technician Work", [
        "role.mechanical-maintenance-repair", "role.fabrication-assembly-machining",
        "role.physical-inspection-quality-control", "role.lab-test-technician"
      ]),
      section("physical-operations", "Physical Operations", [
        "role.warehouse-material-handling", "role.manufacturing-production-operations"
      ]),
      section("physical-field-environments", "Physical / Field Environments", [
        "work.aircraft-flight-line", "work.customer-site", "work.data-center",
        "work.field-engineering", "work.lab-environment", "work.manufacturing-floor",
        "work.outdoor-field", "work.physical-infrastructure"
      ])
    ]),
    tab("work-environment", "Work Environment", [
      section("restricted-special-facilities", "Restricted / Special Facilities", [
        "work.classified-facility"
      ]),
      section("high-risk-special-conditions", "High-Risk / Special Conditions", [
        "work.confined-spaces", "work.scuba", "work.shipboard", "work.heights"
      ])
    ]),
    tab("responsibility-shape", "Responsibility Shape", [
      section("technical-work-shape", "Technical Work Shape", [
        "responsibility.architecture-heavy", "responsibility.hands-on-implementation",
        "responsibility.research-oriented", "responsibility.operations-sustainment",
        "responsibility.documentation-heavy"
      ])
    ]),
    tab("management-delivery", "Management / Delivery", [
      section("delivery-management", "Delivery / Management", [
        "role.program-management", "role.project-management", "role.people-management"
      ]),
      section("leadership-ownership", "Leadership / Ownership", [
        "role.management-heavy", "responsibility.team-leadership",
        "responsibility.personnel-management", "responsibility.budget-ownership",
        "responsibility.schedule-ownership"
      ]),
      section("external-business-responsibility", "External / Business Responsibility", [
        "responsibility.customer-facing", "responsibility.proposal-capture"
      ])
    ])
  ]);
  const GROUP_HARD_CONFLICT_SECTIONS = Object.freeze(SURVEY_TABS
    .flatMap(item => item.sections)
    .filter(item => item.hardConflictId));
  const GROUP_HARD_CONFLICT_IDS = Object.freeze(
    GROUP_HARD_CONFLICT_SECTIONS.map(item => item.hardConflictId));
  const GROUP_OVERRIDE_BY_CONCEPT = Object.freeze(Object.fromEntries(
    GROUP_HARD_CONFLICT_SECTIONS.flatMap(item =>
      item.conceptIds.map(conceptId => [conceptId, item.hardConflictId]))));
  const SURVEY_CONCEPT_DESCRIPTIONS = Object.freeze({
    "role.ai-ml-engineering": "Building, integrating, or operationalizing machine-learning and AI systems.",
    "role.cloud-engineering": "Designing and operating cloud infrastructure, services, and platforms.",
    "role.cybersecurity": "Security engineering, analysis, architecture, or other cybersecurity work.",
    "role.data-engineering": "Building data pipelines, storage, transformation, and data-platform systems.",
    "role.data-science": "Statistical analysis, experimentation, predictive modeling, and analytical modeling.",
    "role.devops-platform": "CI/CD, developer platforms, infrastructure automation, and operational tooling.",
    "role.field-service": "Hands-on technical work performed at customer, equipment, or operational sites.",
    "role.hardware-engineering": "Design, integration, testing, or support of hardware and physical electronic systems.",
    "role.individual-contributor": "Primarily responsible for direct technical work rather than managing people.",
    "role.infrastructure-engineering": "Systems, servers, virtualization, storage, operating platforms, and related infrastructure.",
    "role.network-engineering": "Network architecture, routing, switching, connectivity, and related systems.",
    "role.people-management": "Formal responsibility for employees, staffing, performance, development, or hiring.",
    "role.program-management": "Coordinating broad programs, budgets, schedules, stakeholders, and delivery across projects.",
    "role.project-management": "Planning and coordinating defined projects, schedules, resources, risks, and deliverables.",
    "role.software-engineering": "Designing, implementing, testing, and maintaining software systems.",
    "role.systems-engineering": "Requirements, architecture, integration, interfaces, verification, and whole-system behavior.",
    "role.test-validation-engineering": "Verification, validation, test design, automation, and proving systems meet requirements.",
    "technical.api-development": "Designing and implementing programmatic service interfaces.",
    "technical.application-development": "Building user-facing or business-facing applications.",
    "technical.artificial-intelligence": "Broad AI systems, techniques, and AI-enabled capabilities.",
    "technical.automation-scripting": "Automating workflows, operations, or repetitive tasks with scripts or tooling.",
    "technical.backend-development": "Server-side services, business logic, databases, and APIs.",
    "technical.cabling-racking": "Physical installation, cabling, rack-mounted equipment, and related infrastructure work.",
    "technical.cicd": "Automated build, test, release, and deployment pipelines.",
    "technical.cisco-networking": "Cisco-specific networking technologies, platforms, and tooling.",
    "technical.cloud": "AWS, Azure, GCP, and related cloud-native services.",
    "technical.embedded-systems": "Software and systems running on embedded or device-level hardware.",
    "technical.frontend-development": "Client-side application interfaces and user-facing web or UI development.",
    "technical.infrastructure-as-code": "Declarative infrastructure using tools such as Terraform or CloudFormation.",
    "technical.containers": "Containerization, orchestration, Docker, Kubernetes, and related platforms.",
    "technical.large-language-models": "Transformer and LLM-based systems for language understanding, generation, or reasoning.",
    "technical.linux": "Developing or operating in Linux environments.",
    "technical.linux-administration": "Administration, configuration, maintenance, and troubleshooting of Linux systems.",
    "technical.machine-learning": "Statistical or learned models trained from data.",
    "technical.nlp": "Processing, understanding, or generating human language.",
    "technical.networking": "Network architecture, routing, switching, connectivity, and related technologies.",
    "technical.power-facilities": "Electrical power, cooling, facilities, or other physical infrastructure systems.",
    "technical.software-development": "General implementation and maintenance of software systems.",
    "technical.storage": "Storage systems, platforms, capacity, performance, and related administration.",
    "technical.virtualization": "Virtual machines, hypervisors, virtual infrastructure, and related platforms.",
    "technical.windows-administration": "Administration, configuration, maintenance, and troubleshooting of Windows systems.",
    "work.aircraft-flight-line": "Work around aircraft, hangars, ramps, or flight-line operations.",
    "work.classified-facility": "Routine work inside secure classified facilities with access or device restrictions.",
    "work.confined-spaces": "Work requiring entry into restricted spaces such as tanks, shafts, or crawlspaces.",
    "work.customer-site": "Routine work performed at customer-controlled facilities rather than your normal office or home.",
    "work.data-center": "Hands-on or operational work inside data-center environments.",
    "work.field-engineering": "Engineering work performed at operational, customer, or equipment sites.",
    "work.lab-environment": "Work regularly performed in engineering, research, test, or hardware laboratories.",
    "work.manufacturing-floor": "Work performed in production, assembly, or manufacturing environments.",
    "work.outdoor-field": "Regular work outdoors at operational, test, construction, or equipment sites.",
    "work.physical-infrastructure": "Hands-on interaction with racks, equipment, cabling, power, or facilities systems.",
    "work.scuba": "Work requiring underwater or diving activities.",
    "work.shipboard": "Work aboard ships or vessels, potentially including time underway.",
    "work.heights": "Work requiring ladders, lifts, towers, rooftops, or elevated platforms.",
    "responsibility.architecture-heavy": "Significant responsibility for architecture, design direction, interfaces, and technical decisions.",
    "responsibility.budget-ownership": "Accountability for budgets, cost performance, or financial planning.",
    "responsibility.customer-facing": "Regular direct interaction with customers, stakeholders, or external users.",
    "responsibility.documentation-heavy": "A substantial portion of the role involves formal documentation or compliance artifacts.",
    "responsibility.hands-on-implementation": "Significant direct building, coding, configuring, testing, or troubleshooting.",
    "role.management-heavy": "A large portion of the job is coordination, planning, or management rather than direct technical execution.",
    "role.mechanical-maintenance-repair": "Hands-on maintenance, repair, servicing, and mechanical troubleshooting of vehicles, machinery, equipment, or physical systems.",
    "role.fabrication-assembly-machining": "Producing, assembling, machining, welding, or fabricating physical parts, structures, equipment, or assemblies.",
    "role.physical-inspection-quality-control": "Hands-on inspection, acceptance, and quality-control verification of physical equipment, parts, installations, materials, or workmanship.",
    "role.lab-test-technician": "Technician-level hands-on setup, instrumentation, measurement, test execution, and laboratory support.",
    "role.warehouse-material-handling": "Physical receiving, stocking, picking, shipping, inventory movement, warehouse operations, and material handling.",
    "role.manufacturing-production-operations": "Direct plant-floor production, assembly-line, manufacturing, or production-equipment operations.",
    "responsibility.operations-sustainment": "Ongoing support, maintenance, monitoring, incident response, or lifecycle sustainment.",
    "responsibility.personnel-management": "Formal employee responsibility including hiring, reviews, staffing, or development.",
    "responsibility.proposal-capture": "Business-development work involving proposals, bids, capture strategy, or winning new work.",
    "responsibility.research-oriented": "Investigation, experimentation, prototyping, or evaluation of new techniques.",
    "responsibility.schedule-ownership": "Accountability for schedules, milestones, dependencies, or delivery performance.",
    "responsibility.team-leadership": "Leading a technical team without necessarily having formal personnel authority."
  });

  function normalizeTravelTolerance(value) {
    return Number.isInteger(value) && value >= 0 && value <= 6
      ? value
      : null;
  }

  function normalizePreferredWorkLocation(value) {
    return Number.isInteger(value) && value >= 0 && value <= 5
      ? value
      : null;
  }

  function normalizeConfiguration(configuration) {
    const seen = new Set();
    const signals = [];
    for (const signal of Array.isArray(configuration?.signals) ? configuration.signals : []) {
      if (!signal || typeof signal.conceptId !== "string" || seen.has(signal.conceptId)) {
        continue;
      }
      const preference = signal.preference === "strongNegative"
        ? "negative"
        : signal.preference === "strongPositive"
          ? "ideal"
          : signal.preference;
      seen.add(signal.conceptId);
      if (!Object.hasOwn(LABELS, preference)) continue;
      signals.push({ conceptId: signal.conceptId, preference });
    }
    const groupHardConflicts = Array.from(new Set(
      (Array.isArray(configuration?.groupHardConflicts) ? configuration.groupHardConflicts : [])
        .filter(groupId => GROUP_HARD_CONFLICT_IDS.includes(groupId))))
      .sort((left, right) => left.localeCompare(right));
    return {
      enabled: configuration?.enabled === true,
      signals,
      travelTolerance: normalizeTravelTolerance(configuration?.travelTolerance),
      preferredWorkLocation: normalizePreferredWorkLocation(configuration?.preferredWorkLocation),
      groupHardConflicts
    };
  }

  function travelLevelForPercentage(percentage) {
    if (percentage <= 0) return 0;
    if (percentage <= 10) return 3;
    if (percentage <= 25) return 4;
    if (percentage <= 50) return 5;
    return 6;
  }

  function detectedTravelRequirement(detected, concepts) {
    const candidates = [];
    for (const item of detected.values()) {
      const concept = concepts.get(item.conceptId);
      if (!Number.isInteger(concept?.travelLevel)) continue;
      const evidence = item.evidence || "Canonical travel requirement detected";
      const percentages = Array.from(evidence.matchAll(/\b(\d{1,3})\s*%/g), match => Number(match[1]))
        .filter(value => value >= 0 && value <= 100);
      let level = percentages.length
        ? travelLevelForPercentage(Math.max(...percentages))
        : concept.travelLevel;
      if (/\bone\s+(?:short\s+)?trip\s+every\s+(?:12\s*[-–—]\s*18|twelve\s+to\s+eighteen)\s+months?\b/i.test(evidence)) {
        level = 2;
      } else if (/\bat\s+most\s+one\s+(?:short\s+)?trip\s+every\s+(?:2\s*[-–—]\s*3|two\s+to\s+three)\s+years?\b/i.test(evidence)) {
        level = 1;
      }
      candidates.push({
        level,
        percentage: percentages.length ? Math.max(...percentages) : null,
        evidence,
        conceptId: item.conceptId
      });
    }
    return candidates.sort((left, right) =>
      right.level - left.level || Number(right.percentage !== null) - Number(left.percentage !== null))[0] || null;
  }

  function travelComparisonSignal(requirement, tolerance) {
    const difference = requirement.level - tolerance;
    const preference = difference <= 0
      ? "neutral"
      : difference === 1 ? "negative" : "hardConflict";
    const detectedDefinition = TRAVEL_LEVELS[requirement.level];
    const toleranceDefinition = TRAVEL_LEVELS[tolerance];
    const requirementText = requirement.percentage === null
      ? requirement.evidence
      : `approximately ${requirement.percentage}%`;
    return {
      conceptId: "work.travel.tolerance",
      displayName: "Travel Tolerance",
      category: "Work Arrangement",
      preference,
      preferenceLabel: LABELS[preference],
      impact: preference === "neutral" ? 0 : WEIGHTS[preference],
      evidence: `Detected ordinary business travel: ${requirementText}`,
      travelComparison: {
        detectedLevel: requirement.level,
        detectedLabel: detectedDefinition.label,
        detectedPercentage: requirement.percentage,
        tolerance,
        toleranceLabel: toleranceDefinition.label,
        result: preference,
        resultLabel: LABELS[preference],
        sourceEvidence: requirement.evidence
      }
    };
  }

  function detectedWorkLocation(detected, concepts) {
    const candidates = [];
    const add = (level, priority, evidence, conceptId, rule) => candidates.push({
      level, priority, evidence, conceptId, rule
    });
    for (const item of detected.values()) {
      const concept = concepts.get(item.conceptId);
      if (!Number.isInteger(concept?.workLocationLevel)) continue;
      const evidence = item.evidence || "Canonical work-location signal detected";
      const normalized = evidence.toLocaleLowerCase();
      if (/\b(?:currently|initially)\s+remote\b.{0,140}\b(?:transition|return|move)\b.{0,80}\bon[- ]?site\b/i.test(evidence)) {
        add(5, 100, evidence, item.conceptId, "future required onsite arrangement");
        continue;
      }
      const cadence = evidence.match(/\b(one|two|three|four|five|[1-5])\s*(?:-|–)?\s*days?\b.{0,70}\b(?:on[- ]?site|in[- ]person|office|facility)\b/i) ||
        evidence.match(/\b(?:on[- ]?site|in[- ]person|office|facility)\b.{0,70}\b(one|two|three|four|five|[1-5])\s*(?:-|–)?\s*days?\b/i);
      if (cadence) {
        const days = { one: 1, two: 2, three: 3, four: 4, five: 5 }[cadence[1].toLocaleLowerCase()] || Number(cadence[1]);
        const level = days <= 1 ? 2 : days <= 3 ? 3 : days === 4 ? 4 : 5;
        add(level, 90, evidence, item.conceptId, `${days} required onsite day${days === 1 ? "" : "s"} per week`);
        continue;
      }
      if (/\b(?:quarterly|once|twice)\b.{0,70}\b(?:office|on[- ]?site|in[- ]person|facility|visits?)\b/i.test(evidence)) {
        add(1, 85, evidence, item.conceptId, "explicit rare office cadence");
        continue;
      }
      if (/\bmostly\s+on[- ]?site\b/i.test(evidence)) {
        add(4, 80, evidence, item.conceptId, "explicit mostly-onsite arrangement");
        continue;
      }
      if (/\bmostly\s+remote\b|\bremote[- ]first\b|\b(?:occasional(?:ly)?|periodic(?:ally)?|as[- ]needed)\b.{0,70}\b(?:office|on[- ]?site|in[- ]person|facility|visits?)\b/i.test(evidence)) {
        add(2, 80, evidence, item.conceptId, "explicit mostly-remote or occasional-onsite arrangement");
        continue;
      }
      const explicit = /\b(?:100\s*%|fully|completely)\s+(?:remote|on[- ]?site)\b|\bremote[- ]only\b|\bno\s+remote\s+work\b/i.test(normalized);
      add(concept.workLocationLevel, explicit ? 70 : item.conceptId === "work.hybrid" ? 60 : 40,
        evidence, item.conceptId, explicit ? "explicit arrangement wording" : "canonical location signal");
    }
    return candidates.sort((left, right) =>
      right.priority - left.priority || right.level - left.level || left.conceptId.localeCompare(right.conceptId))[0] || null;
  }

  function workLocationComparisonSignal(requirement, preferredLevel) {
    const distance = Math.abs(requirement.level - preferredLevel);
    const impacts = [1, 0, -1, -2, -3, -4];
    const preference = distance === 0 ? "ideal" : distance === 1 ? "neutral" : "negative";
    return {
      conceptId: "work.location.preference",
      displayName: "Normal Work Location",
      category: "Work Arrangement",
      preference,
      preferenceLabel: LABELS[preference],
      impact: impacts[distance],
      evidence: requirement.evidence,
      locationComparison: {
        detectedLevel: requirement.level,
        detectedLabel: WORK_LOCATION_LEVELS[requirement.level].label,
        preferredLevel,
        preferredLabel: WORK_LOCATION_LEVELS[preferredLevel].label,
        distance,
        impact: impacts[distance],
        precedence: requirement.rule,
        sourceEvidence: requirement.evidence,
        sourceConceptId: requirement.conceptId
      }
    };
  }

  function evaluate(detectedConcepts, configuration, conceptOptions) {
    const normalized = normalizeConfiguration(configuration);
    if (!normalized.enabled) return null;

    const detected = new Map((Array.isArray(detectedConcepts) ? detectedConcepts : [])
      .filter(item => item && typeof item.conceptId === "string")
      .map(item => [item.conceptId, item]));
    const concepts = new Map((Array.isArray(conceptOptions) ? conceptOptions : [])
      .filter(item => item && typeof item.id === "string")
      .map(item => [item.id, item]));
    const configured = new Map(normalized.signals.map(signal => [signal.conceptId, signal.preference]));
    const groupHardConflicts = new Set(normalized.groupHardConflicts);
    const detectedSignals = Array.from(detected.values())
      .filter(item => concepts.has(item.conceptId))
      .filter(item => !Number.isInteger(concepts.get(item.conceptId).travelLevel))
      .filter(item => !Number.isInteger(concepts.get(item.conceptId).workLocationLevel))
      .map(item => {
        const concept = concepts.get(item.conceptId);
        const groupOverrideId = GROUP_OVERRIDE_BY_CONCEPT[item.conceptId];
        const preference = groupOverrideId && groupHardConflicts.has(groupOverrideId)
          ? "hardConflict"
          : configured.get(item.conceptId) || "neutral";
        return {
          conceptId: item.conceptId,
          displayName: concept.displayName,
          category: concept.category,
          preference,
          preferenceLabel: LABELS[preference],
          impact: preference === "neutral" ? 0 : WEIGHTS[preference],
          evidence: item.evidence || "Canonical concept detected",
          groupOverrideId: groupOverrideId && groupHardConflicts.has(groupOverrideId)
            ? groupOverrideId
            : null
        };
      });
    const travelRequirement = detectedTravelRequirement(detected, concepts);
    if (travelRequirement && Number.isInteger(normalized.travelTolerance)) {
      detectedSignals.push(travelComparisonSignal(
        travelRequirement,
        normalized.travelTolerance));
    }
    const workLocation = detectedWorkLocation(detected, concepts);
    if (workLocation && Number.isInteger(normalized.preferredWorkLocation)) {
      detectedSignals.push(workLocationComparisonSignal(
        workLocation,
        normalized.preferredWorkLocation));
    }
    const neutralSignals = detectedSignals.filter(signal => signal.preference === "neutral");
    const candidates = detectedSignals.filter(signal => signal.preference !== "neutral");
    const candidateIds = new Set(candidates.map(signal => signal.conceptId));
    const supersededBy = new Map();
    for (const contribution of candidates) {
      const concept = concepts.get(contribution.conceptId);
      for (const supersededId of Array.isArray(concept?.supersedes) ? concept.supersedes : []) {
        if (!candidateIds.has(supersededId)) continue;
        if (!supersededBy.has(supersededId)) supersededBy.set(supersededId, []);
        supersededBy.get(supersededId).push(contribution.conceptId);
      }
    }
    const supersededSignals = candidates
      .filter(signal => supersededBy.has(signal.conceptId))
      .map(signal => ({
        ...signal,
        supersededBy: supersededBy.get(signal.conceptId).map(id => concepts.get(id).displayName)
      }));
    const contributions = candidates.filter(signal => !supersededBy.has(signal.conceptId));

    const byCategory = new Map();
    for (const contribution of contributions) {
      if (!byCategory.has(contribution.category)) byCategory.set(contribution.category, []);
      byCategory.get(contribution.category).push(contribution);
    }
    const dimensions = Array.from(byCategory, ([category, signals]) => {
      const limits = DIMENSION_LIMITS[category] || DEFAULT_LIMITS;
      const rawImpact = signals.reduce((sum, signal) => sum + signal.impact, 0);
      const impact = Math.max(limits.minimum, Math.min(limits.maximum, rawImpact));
      return {
        category,
        impact,
        rawImpact,
        capped: impact !== rawImpact,
        limits,
        signals
      };
    }).sort((left, right) => {
      const leftIndex = DIMENSION_ORDER.indexOf(left.category);
      const rightIndex = DIMENSION_ORDER.indexOf(right.category);
      if (leftIndex < 0 && rightIndex < 0) return left.category.localeCompare(right.category);
      if (leftIndex < 0) return 1;
      if (rightIndex < 0) return -1;
      return leftIndex - rightIndex;
    });

    const dimensionBreakdown = DIMENSION_ORDER.map(category => {
      const dimension = dimensions.find(item => item.category === category);
      const limits = DIMENSION_LIMITS[category];
      return dimension || {
        category,
        impact: 0,
        rawImpact: 0,
        capped: false,
        limits,
        signals: []
      };
    }).map(dimension => ({
      ...dimension,
      neutralSignals: neutralSignals.filter(signal => signal.category === dimension.category),
      supersededSignals: supersededSignals.filter(signal => signal.category === dimension.category)
    }));

    const calculatedTotal = dimensions.reduce((sum, dimension) => sum + dimension.impact, BASELINE);
    const scoreBeforeHardConflictCap = Math.max(1, Math.min(10, Math.round(calculatedTotal)));
    const hardConflictSignals = contributions.filter(
      contribution => contribution.preference === "hardConflict");
    let score = scoreBeforeHardConflictCap;
    if (hardConflictSignals.length > 0) {
      score = Math.min(score, 2);
    }

    return {
      score,
      baseline: BASELINE,
      calculatedTotal,
      scoreBeforeHardConflictCap,
      dimensions,
      dimensionBreakdown,
      contributions,
      neutralSignals,
      supersededSignals,
      hardConflictCap: {
        maximum: 2,
        applied: hardConflictSignals.length > 0 && scoreBeforeHardConflictCap > 2,
        signals: hardConflictSignals
      }
    };
  }

  function scoreClass(score) {
    if (score <= 3) return "score-low";
    if (score === 4) return "score-four";
    if (score === 5) return "score-five";
    if (score === 6) return "score-six";
    if (score === 7) return "score-seven";
    if (score === 8) return "score-eight";
    return "score-high";
  }

  function tooltip(result) {
    if (!result) return "";
    const lines = [`Job Fit: ${result.score}/10`];
    if (result.contributions.length === 0) {
      return `${lines[0]}\n\nNo configured Job Fit signals were detected.`;
    }

    for (const dimension of result.dimensions) {
      const impact = formatImpact(dimension.impact);
      const bounded = dimension.capped
        ? ` (bounded from ${formatImpact(dimension.rawImpact)})`
        : "";
      lines.push("", `${dimension.category}: ${impact}${bounded}`);
      dimension.signals.forEach(item => {
        lines.push(`- ${item.displayName} — ${item.preferenceLabel}: ${item.evidence}`);
        if (item.travelComparison) {
          lines.push(
            `  Detected level: ${item.travelComparison.detectedLevel} - ${item.travelComparison.detectedLabel}`,
            `  Your maximum: ${item.travelComparison.tolerance} - ${item.travelComparison.toleranceLabel}`,
            `  Result: ${item.travelComparison.resultLabel}`);
        } else if (item.locationComparison) {
          lines.push(
            `  Detected location: ${item.locationComparison.detectedLevel} - ${item.locationComparison.detectedLabel}`,
            `  Your preferred location: ${item.locationComparison.preferredLevel} - ${item.locationComparison.preferredLabel}`,
            `  Distance: ${item.locationComparison.distance} level${item.locationComparison.distance === 1 ? "" : "s"}`,
            `  Impact: ${formatImpact(item.locationComparison.impact)}`);
        }
      });
    }
    return lines.join("\n");
  }

  function formatImpact(value) {
    const normalized = Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1);
    return `${value >= 0 ? "+" : ""}${normalized}`;
  }

  function calculateCompleteness(conceptOptions, configuration) {
    const normalized = normalizeConfiguration(configuration);
    const available = new Set((Array.isArray(conceptOptions) ? conceptOptions : [])
      .filter(concept => concept?.userConfigurable !== false &&
        !SURVEY_HIDDEN_CONCEPT_IDS.includes(concept.id))
      .map(concept => concept.id));
    const configured = new Set(normalized.signals.map(signal => signal.conceptId));
    const overridden = new Set(normalized.groupHardConflicts);
    const tabs = {};
    let overallTotal = 0;
    let overallConfigured = 0;
    for (const tabDefinition of SURVEY_TABS) {
      const groups = {};
      let tabTotal = 0;
      let tabConfigured = 0;
      for (const sectionDefinition of tabDefinition.sections) {
        const conceptIds = sectionDefinition.conceptIds.filter(conceptId => available.has(conceptId));
        const overrideActive = Boolean(sectionDefinition.hardConflictId &&
          overridden.has(sectionDefinition.hardConflictId));
        const configuredCount = overrideActive
          ? conceptIds.length
          : conceptIds.filter(conceptId => configured.has(conceptId)).length;
        groups[sectionDefinition.id] = {
          total: conceptIds.length,
          configured: configuredCount,
          unset: conceptIds.length - configuredCount,
          overrideActive
        };
        tabTotal += conceptIds.length;
        tabConfigured += configuredCount;
      }
      if (tabDefinition.specialControls.includes("travel-tolerance")) {
        tabTotal += 1;
        tabConfigured += Number.isInteger(normalized.travelTolerance) ? 1 : 0;
      }
      if (tabDefinition.specialControls.includes("normal-work-location")) {
        tabTotal += 1;
        tabConfigured += Number.isInteger(normalized.preferredWorkLocation) ? 1 : 0;
      }
      tabs[tabDefinition.id] = {
        total: tabTotal,
        configured: tabConfigured,
        unset: tabTotal - tabConfigured,
        groups
      };
      overallTotal += tabTotal;
      overallConfigured += tabConfigured;
    }
    return {
      total: overallTotal,
      configured: overallConfigured,
      unset: overallTotal - overallConfigured,
      tabs
    };
  }

  return {
    evaluate,
    normalizeConfiguration,
    scoreClass,
    tooltip,
    preferenceLabels: LABELS,
    baseline: BASELINE,
    dimensionLimits: DIMENSION_LIMITS,
    travelLevels: TRAVEL_LEVELS,
    defaultTravelTolerance: DEFAULT_TRAVEL_TOLERANCE,
    normalizeTravelTolerance,
    workLocationLevels: WORK_LOCATION_LEVELS,
    defaultPreferredWorkLocation: DEFAULT_PREFERRED_WORK_LOCATION,
    normalizePreferredWorkLocation,
    calculateCompleteness,
    detectedWorkLocation,
    detectedTravelRequirement,
    surveyTabs: SURVEY_TABS,
    surveyHiddenConceptIds: SURVEY_HIDDEN_CONCEPT_IDS,
    groupHardConflictIds: GROUP_HARD_CONFLICT_IDS,
    groupOverrideByConcept: GROUP_OVERRIDE_BY_CONCEPT,
    surveyConceptDescriptions: SURVEY_CONCEPT_DESCRIPTIONS
  };
});
