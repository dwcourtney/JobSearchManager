#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { gunzipSync } from "node:zlib";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const JobFit = require("../wwwroot/job-fit.js");

const [cachePath, settingsPath, outputPath, detectionsPath] = process.argv.slice(2);
if (!cachePath || !settingsPath || !outputPath) {
  console.error("Usage: node scripts/job-fit-calibration-report.mjs <cache.json> <settings.json> <report.json> [detector-output.json]");
  process.exit(2);
}

const catalog = JSON.parse(fs.readFileSync(new URL("../JobConceptCatalog.json", import.meta.url), "utf8"));
const cache = JSON.parse(fs.readFileSync(path.resolve(cachePath), "utf8"));
const settings = JSON.parse(fs.readFileSync(path.resolve(settingsPath), "utf8"));
const recomputed = detectionsPath
  ? JSON.parse(fs.readFileSync(path.resolve(detectionsPath), "utf8"))
  : null;
const recomputedById = new Map((recomputed?.jobs || []).map(job => [job.requisitionId, job]));
const options = catalog.concepts.map(concept => ({
  id: concept.id,
  displayName: concept.displayName,
  category: concept.category,
  supersedes: concept.supersedes || [],
  userConfigurable: concept.userConfigurable !== false,
  travelLevel: Number.isInteger(concept.travelLevel) ? concept.travelLevel : null
}));
const byId = new Map(options.map(option => [option.id, option]));
const preferences = new Map((settings.jobFit?.signals || []).map(signal => [signal.conceptId, signal.preference]));

const auditRules = [
  rule("responsibility.customer-facing", "primary-customer-interface",
    /\b(?:primary|direct|key)\s+(?:point\s+of\s+contact|interface|liaison)\s+(?:for|to|with)\s+(?:the\s+)?(?:customer|client)\b/i),
  rule("responsibility.customer-facing", "customer-presentations",
    /\b(?:brief|present|demonstrate)\w*\s+(?:findings|results|solutions?|capabilities|recommendations?)?\s*(?:to|for)\s+(?:the\s+)?(?:customer|client)\b/i),
  rule("work.customer-site", "customer-site-duty",
    /\b(?:work(?:ing)?|perform(?:ing)?|support(?:ing)?|located)\s+(?:on[- ]?site\s+)?at\s+(?:the\s+)?customer(?:'s)?\s+(?:site|location|facility)\b/i),
  rule("work.field-engineering", "field-test-duty",
    /\b(?:conduct|perform|support|execute|participate\s+in)\w*\s+(?:on[- ]?site\s+)?field\s+(?:tests?|testing|trials?|evaluations?)\b/i),
  rule("role.test-validation-engineering", "verification-validation-duty",
    /\b(?:develop|execute|perform|conduct)\w*\s+(?:system\s+|software\s+)?(?:verification|validation|V&V)\s+(?:plans?|tests?|activities|efforts?)\b/i),
  rule("responsibility.research-oriented", "research-prototyping-duty",
    /\b(?:research|investigate|explore)\w*\s+(?:and\s+)?(?:prototype|prototype\w*|novel|emerging)\s+(?:algorithms?|approaches|technologies|solutions|capabilities)\b/i),
  rule("responsibility.hands-on-implementation", "implementation-duty",
    /\b(?:design|develop|implement|integrate),?\s+(?:test,?\s+)?and\s+(?:deploy|deliver|maintain)\s+(?:software|systems?|solutions?|capabilities|infrastructure)\b/i),
  rule("responsibility.schedule-ownership", "schedule-control-duty",
    /\b(?:develop|maintain|manage|own)\w*\s+(?:the\s+)?(?:project|program|integrated\s+master)\s+schedule\b/i),
  rule("responsibility.budget-ownership", "budget-control-duty",
    /\b(?:develop|maintain|manage|own)\w*\s+(?:the\s+)?(?:project|program|department|operating)\s+budget\b/i),
  rule("responsibility.documentation-heavy", "documentation-primary-duty",
    /\b(?:author|produce|prepare|develop|maintain)\w*\s+(?:detailed|formal|comprehensive)\s+(?:technical\s+)?(?:documentation|manuals?|reports?|procedures?)\b/i),
  rule("responsibility.operations-sustainment", "sustainment-duty",
    /\b(?:operate|maintain|sustain|support)\w*\s+(?:and\s+)?(?:maintain|sustain|support)?\s*(?:deployed|production|operational)\s+(?:systems?|services?|capabilities|environments?)\b/i),
  rule("role.program-management", "program-performance-duty",
    /\b(?:responsible\s+for|manage|lead|oversee)\w*\s+(?:the\s+)?program(?:'s)?\s+(?:execution|performance|delivery|cost|schedule|risk)\b/i),
  rule("role.project-management", "project-performance-duty",
    /\b(?:responsible\s+for|manage|lead|oversee)\w*\s+(?:the\s+)?project(?:'s)?\s+(?:execution|performance|delivery|cost|schedule|risk)\b/i),
  rule("technical.automation-scripting", "scripting-duty",
    /\b(?:develop|write|maintain|create)\w*\s+(?:automation\s+)?scripts?\s+(?:in|using|with)\s+(?:Python|PowerShell|Bash|shell)\b/i),
  rule("technical.api-development", "api-implementation-duty",
    /\b(?:design|develop|implement|maintain)\w*\s+(?:REST(?:ful)?|HTTP|web)?\s*APIs?\b/i)
];

function rule(conceptId, auditId, pattern) {
  return { conceptId, auditId, pattern };
}

function description(job) {
  if (job.descriptionHtml) return job.descriptionHtml;
  if (!job.compressedDescriptionHtml) return "";
  try {
    return gunzipSync(Buffer.from(job.compressedDescriptionHtml, "base64")).toString("utf8");
  } catch {
    return "";
  }
}

function plainText(html) {
  return html
    .replace(/<(?:br|\/p|\/li|\/div|\/h[1-6]|\/section|\/article)>/gi, " ")
    .replace(/<[^>]*>/g, " ")
    .replace(/&nbsp;/gi, " ")
    .replace(/&quot;/gi, "\"")
    .replace(/&#39;|&apos;/gi, "'")
    .replace(/&amp;/gi, "&")
    .replace(/\s+/g, " ")
    .trim();
}

function evidence(text, match) {
  const start = Math.max(0, match.index - 90);
  const end = Math.min(text.length, match.index + match[0].length + 140);
  return `${start ? "…" : ""}${text.slice(start, end).trim()}${end < text.length ? "…" : ""}`;
}

const jobs = cache.jobs.map(job => {
  const detectorResult = recomputedById.get(job.requisitionId);
  const detectedConcepts = detectorResult?.detectedConcepts || job.detectedConcepts || [];
  const text = `${job.title || ""}\n${plainText(description(job))}`;
  const result = JobFit.evaluate(detectedConcepts, settings.jobFit, options);
  const detectedIds = new Set(detectedConcepts.map(item => item.conceptId));
  const auditMisses = [];
  for (const audit of auditRules) {
    if (detectedIds.has(audit.conceptId)) continue;
    const match = audit.pattern.exec(text);
    if (!match) continue;
    auditMisses.push({
      auditId: audit.auditId,
      conceptId: audit.conceptId,
      displayName: byId.get(audit.conceptId)?.displayName || audit.conceptId,
      configuredPreference: preferences.get(audit.conceptId) || "neutral",
      evidence: evidence(text, match)
    });
  }
  return {
    requisitionId: job.requisitionId,
    title: job.title,
    descriptionCharacters: text.length,
    score: result?.score ?? null,
    detectedConcepts: detectedConcepts.map(item => ({
      conceptId: item.conceptId,
      displayName: byId.get(item.conceptId)?.displayName || item.conceptId,
      category: byId.get(item.conceptId)?.category || "Unknown",
      configuredPreference: preferences.get(item.conceptId) || "neutral",
      evidence: item.evidence
    })),
    contributingConcepts: result?.contributions || [],
    neutralConcepts: result?.neutralSignals || [],
    categoryContributions: result?.dimensions || [],
    hardConflict: result?.hardConflictCap || null,
    sparseDetection: text.length >= 1800 && detectedConcepts.length <= 2,
    auditMisses
  };
});

const distribution = Object.fromEntries(Array.from({ length: 10 }, (_, index) => {
  const score = index + 1;
  return [score, jobs.filter(job => job.score === score).length];
}));
const sorted = [...jobs].sort((left, right) => left.score - right.score || left.title.localeCompare(right.title));
const report = {
  generatedAtUtc: new Date().toISOString(),
  input: {
    companyId: cache.query?.companyId,
    jobCount: jobs.length,
    cacheSchemaVersion: cache.schemaVersion,
    jobConceptCatalogVersion: recomputed?.jobConceptCatalogVersion || catalog.version,
    jobFitEnabled: settings.jobFit?.enabled === true
  },
  scoreDistribution: distribution,
  lowest: sorted.slice(0, 20).map(job => ({ requisitionId: job.requisitionId, title: job.title, score: job.score })),
  highest: sorted.slice(-20).reverse().map(job => ({ requisitionId: job.requisitionId, title: job.title, score: job.score })),
  hardConflictJobs: jobs.filter(job => job.hardConflict?.signals?.length).map(job => ({
    requisitionId: job.requisitionId,
    title: job.title,
    score: job.score,
    concepts: job.hardConflict.signals.map(signal => signal.displayName)
  })),
  sparseJobs: jobs.filter(job => job.sparseDetection).map(job => ({
    requisitionId: job.requisitionId,
    title: job.title,
    score: job.score,
    detectedCount: job.detectedConcepts.length,
    auditMissCount: job.auditMisses.length
  })),
  auditMissSummary: Object.values(jobs.flatMap(job => job.auditMisses).reduce((summary, miss) => {
    const item = summary[miss.auditId] ||= { auditId: miss.auditId, conceptId: miss.conceptId, count: 0 };
    item.count++;
    return summary;
  }, {})).sort((left, right) => right.count - left.count || left.auditId.localeCompare(right.auditId)),
  jobs
};

fs.writeFileSync(path.resolve(outputPath), `${JSON.stringify(report, null, 2)}\n`, "utf8");
console.log(JSON.stringify({
  jobs: jobs.length,
  scoreDistribution: distribution,
  hardConflicts: report.hardConflictJobs.length,
  sparse: report.sparseJobs.length,
  auditMisses: report.auditMissSummary
}, null, 2));
