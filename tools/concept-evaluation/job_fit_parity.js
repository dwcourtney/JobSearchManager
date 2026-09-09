"use strict";
// Offline parity through the actual Jobs caller and unchanged scorer.
// Usage: node job_fit_parity.js <repo> <baseline-predictions> <candidate-predictions> <target-concept>
const fs = require("node:fs"), path = require("node:path"), vm = require("node:vm"), assert = require("node:assert/strict");
const [repo, beforePath, afterPath, target] = process.argv.slice(2);
const read = file => JSON.parse(fs.readFileSync(file, "utf8"));
const catalog = read(path.join(repo, "JobConceptCatalog.json"));
assert.ok(catalog.concepts.some(c => c.id === target));
const before = read(beforePath), after = new Map(read(afterPath).postings.map(p => [p.id, p]));
assert.equal(before.postings.length, after.size);
const app = fs.readFileSync(path.join(repo, "wwwroot/app.js"), "utf8");
const start = app.indexOf("function evaluateJobFit(job)"), end = app.indexOf("function createJobListItem(job)", start);
assert.ok(start >= 0 && end > start);
const context = { JobFit: require(path.join(repo, "wwwroot/job-fit.js")), state: { jobFitConcepts: catalog.concepts, jobFitGroupHardConflicts: [] } };
vm.createContext(context); vm.runInContext(app.slice(start, end), context);
let comparisons = 0;
const score = concepts => context.evaluateJobFit({ semanticClassificationStatus: "complete", detectedConcepts: concepts });
for (const preference of ["neutral", "ideal", "positive", "negative", "hardConflict"]) {
  context.state.jobFitSignals = catalog.concepts.filter(c => c.id !== target).map(c => ({ conceptId: c.id, preference }));
  for (const configured of [false, true]) {
    context.state.travelTolerance = configured ? 0 : null;
    context.state.preferredWorkLocation = configured ? 0 : null;
    for (const enabled of [false, true]) {
      context.state.jobFitEnabled = enabled;
      for (const b of before.postings) {
        const a = after.get(b.id); assert.ok(a);
        const bs = score(b.concepts), as = score(a.concepts);
        // Neutral target evidence can appear in explanatory rows; it must not affect any numeric result.
        const numeric = result => result == null ? null : { score: result.score, rawScore: result.rawScore,
          calculatedTotal: result.calculatedTotal, dimensions: result.dimensions.map(d => ({ category: d.category, rawImpact: d.rawImpact, impact: d.impact, capped: d.capped })) };
        assert.deepEqual(numeric(bs), numeric(as), `${b.id}: unaffected preferences changed`);
        comparisons++;
      }
    }
  }
}
console.log(JSON.stringify({ target, postings: before.postings.length, comparisons, changedScores: 0, actualJobsCaller: true }));
