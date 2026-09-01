(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.AnnotationLabeling = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  const shortcutDecisions = Object.freeze({
    "1": "correct", "2": "incorrect", "3": "differentLabel",
    "4": "multipleLabels", "5": "none", "s": "unsure"
  });
  function isTypingTarget(target) {
    const tag = String(target?.tagName || "").toLowerCase();
    return ["input", "textarea", "select"].includes(tag) || target?.isContentEditable === true;
  }
  function shortcutFor(event) {
    if (!event || event.ctrlKey || event.metaKey || event.altKey || isTypingTarget(event.target)) return null;
    const key = String(event.key || "").toLowerCase();
    return shortcutDecisions[key] || (key === "c" ? "toggleContext" : key === "f" ? "toggleFull" : null);
  }
  function validateDecision(decision, conceptIds) {
    const count = [...new Set(conceptIds || [])].length;
    if (decision === "differentLabel" && count !== 1) return "Choose exactly one replacement concept.";
    if (decision === "multipleLabels" && count < 2) return "Choose at least two concepts.";
    if (!["correct", "incorrect", "differentLabel", "multipleLabels", "none", "unsure"].includes(decision))
      return "Choose a valid decision.";
    return "";
  }
  function queueUrl(filters, base = "/api/admin/annotations/queue") {
    const query = new URLSearchParams();
    if (filters?.status) query.set("status", filters.status);
    if (filters?.concept) query.set("concept", filters.concept);
    if (filters?.company) query.set("company", filters.company);
    return `${base}?${query}`;
  }
  function exportUrl(mode, filters) {
    const query = new URLSearchParams({ mode });
    if (filters?.concept) query.set("concept", filters.concept);
    if (filters?.company) query.set("company", filters.company);
    return `/api/admin/annotations/export?${query}`;
  }
  function generationPayload(target, allEligible, filters) {
    return { requestedItems: Number(target), allEligible: Boolean(allEligible),
      company: filters?.company || null, concept: filters?.concept || null };
  }
  function formatImportSummary(summary) {
    return `${summary.recordsRead} read · ${summary.imported} imported · ${summary.unchanged} unchanged · ` +
      `${summary.conflicts} conflicts · ${summary.rejected} rejected`;
  }
  function element(tag, className, text) {
    const node = document.createElement(tag); if (className) node.className = className;
    if (text !== undefined) node.textContent = text; return node;
  }

  function mount(host) {
    if (!host || host.dataset.annotationMounted === "true") return;
    host.dataset.annotationMounted = "true";
    const state = { response: null, source: null,
      filters: { status: "unreviewed", concept: "", company: "" }, saving: false };
    const heading = element("h3", "", "Human Labeling");
    const intro = element("p", "account-help",
      "Machine reviews are provenance-rich opinions, not human gold. Unsure and unresolved conflicts remain excluded from training.");

    const generation = element("section", "annotation-dataset-controls");
    generation.append(element("h4", "", "Generate corpus"));
    const generationRow = element("div", "annotation-toolbar");
    const targetLabel = element("label", "annotation-inline-field", "Target corpus size");
    const target = document.createElement("input"); target.type = "number"; target.min = "1"; target.max = "50000";
    target.value = "1000"; target.setAttribute("aria-label", "Target corpus size"); targetLabel.append(target);
    const allLabel = element("label", "annotation-check-field");
    const allEligible = document.createElement("input"); allEligible.type = "checkbox";
    allLabel.append(allEligible, document.createTextNode(" All eligible"));
    const generate = element("button", "primary-button", "Generate corpus"); generate.type = "button";
    generationRow.append(targetLabel, allLabel, generate); generation.append(generationRow,
      element("p", "account-help", "Generation appends deterministic eligible items and preserves every existing decision."));

    const exchange = element("section", "annotation-dataset-controls"); exchange.append(element("h4", "", "Machine review exchange"));
    const exports = element("div", "annotation-toolbar");
    const exportModes = [["all", "Export all JSONL"], ["reviewed", "Export reviewed"],
      ["unreviewed", "Export unreviewed"], ["unsure", "Export unsure"],
      ["trainingEligible", "Export training-eligible"]];
    const exportLinks = exportModes.map(([mode, label]) => {
      const link = element("a", "secondary-link-button", label); link.dataset.exportMode = mode;
      link.download = `jsm-annotations-${mode}.jsonl`; exports.append(link); return link;
    });
    const importRow = element("div", "annotation-toolbar");
    const importFile = document.createElement("input"); importFile.type = "file"; importFile.accept = ".jsonl,application/x-ndjson,application/json";
    importFile.setAttribute("aria-label", "Machine review JSONL file");
    const importButton = element("button", "confirmation-secondary-button", "Import machine review JSONL");
    importButton.type = "button"; const importStatus = element("p", "annotation-import-status"); importStatus.setAttribute("role", "status");
    importRow.append(importFile, importButton); exchange.append(exports, importRow, importStatus);

    const filters = element("div", "annotation-toolbar");
    const statusFilter = document.createElement("select"); statusFilter.setAttribute("aria-label", "Review queue");
    [["unreviewed", "Unreviewed"], ["machineDisagreement", "Machine disagreements"],
      ["humanUnreviewedMachine", "Human-unreviewed machine labels"], ["unsure", "Unsure / ambiguous"],
      ["rareConcept", "Rare concepts"], ["relabeledConflicting", "Relabeled / conflicting"],
      ["humanReviewed", "Human-reviewed"], ["trainingEligible", "Training-eligible"],
      ["excluded", "Excluded"], ["all", "All"]]
      .forEach(([value, label]) => statusFilter.add(new Option(label, value)));
    const conceptFilter = document.createElement("select"); conceptFilter.setAttribute("aria-label", "Concept filter");
    conceptFilter.add(new Option("All concepts", ""));
    const companyFilter = document.createElement("select"); companyFilter.setAttribute("aria-label", "Company filter");
    companyFilter.add(new Option("All companies", "")); filters.append(statusFilter, conceptFilter, companyFilter);

    const stats = element("div", "annotation-stats", "Loading annotation queue…"); stats.setAttribute("role", "status"); stats.setAttribute("aria-live", "polite");
    const error = element("p", "reset-confirmation-error"); error.hidden = true; error.setAttribute("role", "alert");
    const card = element("article", "annotation-card");
    const shortcuts = element("p", "annotation-shortcuts", "Keys: 1 Correct · 2 Incorrect · 3 Different · 4 Multiple · 5 None · S Unsure · C Context · F Full posting");
    host.append(heading, intro, generation, exchange, filters, stats, error, card, shortcuts);

    function setError(message) { error.textContent = message || ""; error.hidden = !message; }
    function updateLinks() { exportLinks.forEach(link => { link.href = exportUrl(link.dataset.exportMode, state.filters); }); }
    function updateFilters(response) {
      if (conceptFilter.options.length === 1) (response.concepts || []).forEach(c => conceptFilter.add(new Option(`${c.displayName} · ${c.category}`, c.id)));
      const selected = companyFilter.value; while (companyFilter.options.length > 1) companyFilter.remove(1);
      (response.companies || []).forEach(company => companyFilter.add(new Option(company, company))); companyFilter.value = selected;
      updateLinks();
    }
    function selectedConceptIds(picker) { return [...picker.querySelectorAll("input[data-concept-id]:checked")].map(i => i.dataset.conceptId); }
    async function decide(decision, conceptIds = [], unsureReason = "") {
      if (state.saving || !state.response?.item) return; const validation = validateDecision(decision, conceptIds);
      if (validation) { setError(validation); return; } state.saving = true; setError(""); card.setAttribute("aria-busy", "true");
      try {
        const response = await fetch(queueUrl(state.filters, `/api/admin/annotations/${encodeURIComponent(state.response.item.id)}/decision`),
          { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ decision, conceptIds, unsureReason: unsureReason || null }) });
        const payload = await response.json().catch(() => ({})); if (!response.ok) throw new Error(payload.error || "The annotation could not be saved.");
        state.response = payload; state.source = null; render();
      } catch (failure) { setError(failure.message || String(failure)); }
      finally { state.saving = false; card.removeAttribute("aria-busy"); }
    }
    async function showFullPosting(container) {
      if (container.hidden === false) { container.hidden = true; return; }
      try { if (!state.source) { const response = await fetch(`/api/admin/annotations/${encodeURIComponent(state.response.item.id)}/source`, { cache: "no-store" });
          if (!response.ok) throw new Error("The full posting could not be loaded."); state.source = await response.json(); }
        container.textContent = state.source.fullPosting; container.hidden = false;
      } catch (failure) { setError(failure.message || String(failure)); }
    }
    function buildPicker(mode) {
      const picker = element("section", "annotation-picker"); const prompt = element("label", "", mode === "differentLabel" ? "Replacement concept" : "Confirmed concepts");
      const search = document.createElement("input"); search.type = "search"; search.placeholder = "Search concept names or IDs";
      const options = element("div", "annotation-picker-options");
      (state.response.concepts || []).forEach(concept => { const label = element("label", "annotation-concept-option"); const input = document.createElement("input");
        input.type = mode === "differentLabel" ? "radio" : "checkbox"; input.name = mode === "differentLabel" ? "replacement-concept" : `concept-${concept.id}`;
        input.dataset.conceptId = concept.id; label.dataset.search = `${concept.displayName} ${concept.id} ${concept.category}`.toLowerCase();
        label.append(input, element("span", "", concept.displayName), element("small", "", `${concept.id} · ${concept.category}`)); options.append(label); });
      search.addEventListener("input", () => { const query = search.value.trim().toLowerCase(); options.querySelectorAll("label").forEach(o => { o.hidden = !o.dataset.search.includes(query); }); });
      const save = element("button", "primary-button", "Save label"); save.type = "button"; save.addEventListener("click", () => void decide(mode, selectedConceptIds(picker)));
      picker.append(prompt, search, options, save); setTimeout(() => search.focus(), 0); return picker;
    }
    function render() {
      const response = state.response; updateFilters(response); const top = Object.entries(response.conceptDistribution || {}).sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).slice(0, 3).map(([id, count]) => `${id} ${count}`).join(", ");
      stats.textContent = `${response.stats.reviewed} human-reviewed / ${response.stats.total} queued · ${response.stats.unreviewed} unreviewed · ` +
        `${response.stats.machineLabeled} machine-labeled · ${response.stats.machineDisagreements} machine disagreements · ` +
        `${response.stats.humanMachineConflicts} human/machine conflicts · ${response.stats.unsure} unsure · ${response.stats.trainingEligible} training-eligible` + (top ? ` · leading labels: ${top}` : "");
      card.replaceChildren(); const item = response.item;
      if (!item) { card.append(element("p", "annotation-empty", response.stats.total ? "No item matches these filters." : "Generate a corpus from the current cached jobs to begin.")); return; }
      const meta = element("p", "annotation-meta", `${item.companyId} · ${item.title} · ${item.jobId}`);
      const proposed = element("div", "annotation-proposed"); proposed.append(element("strong", "", "Suggested: "));
      item.candidateConceptIds.forEach(id => { const concept = response.concepts.find(c => c.id === id); proposed.append(element("span", "annotation-concept-chip", concept ? concept.displayName : id)); });
      const machine = item.machineReviews || []; if (machine.length) {
        const provenance = element("div", "annotation-provenance"); provenance.append(element("strong", "", "Imported machine opinions: "));
        machine.forEach(review => provenance.append(element("span", "annotation-concept-chip", `${review.reviewerType}${review.reviewerIdentity ? ` · ${review.reviewerIdentity}` : ""}: ${review.decision}`))); proposed.append(provenance);
      }
      if (item.humanProvenance) proposed.append(element("p", "annotation-meta", `Authenticated decision provenance: ${item.humanProvenance}`));
      const evidence = element("blockquote", "annotation-evidence", item.evidence);
      const context = element("p", "annotation-context", `${item.contextBefore}${item.evidence}${item.contextAfter}`.trim()); context.hidden = true;
      const full = element("pre", "annotation-full-posting"); full.hidden = true;
      const controls = element("div", "annotation-actions");
      [["Correct", "correct"], ["Incorrect", "incorrect"], ["Different label", "differentLabel"], ["Multiple labels", "multipleLabels"], ["None", "none"], ["Unsure / Skip", "unsure"]]
        .forEach(([label, decision]) => { const button = element("button", decision === "correct" ? "primary-button" : "confirmation-secondary-button", label); button.type = "button"; button.dataset.decision = decision;
          button.addEventListener("click", () => { card.querySelector(".annotation-picker")?.remove(); if (["differentLabel", "multipleLabels"].includes(decision)) controls.after(buildPicker(decision)); else void decide(decision); }); controls.append(button); });
      const toggles = element("div", "annotation-toggles"); const contextButton = element("button", "link-button", "Show context"); contextButton.type = "button";
      contextButton.addEventListener("click", () => { context.hidden = !context.hidden; contextButton.textContent = context.hidden ? "Show context" : "Hide context"; });
      const fullButton = element("button", "link-button", "Show full posting"); fullButton.type = "button"; fullButton.addEventListener("click", () => void showFullPosting(full)); toggles.append(contextButton, fullButton);
      card.append(meta, proposed, evidence, toggles, context, full, controls);
    }
    async function load() { setError(""); try { const response = await fetch(queueUrl(state.filters), { cache: "no-store" });
        if (!response.ok) throw new Error("The annotation queue could not be loaded."); state.response = await response.json(); state.source = null; render();
      } catch (failure) { setError(failure.message || String(failure)); } }
    generate.addEventListener("click", async () => { generate.disabled = true; setError("");
      try { const payload = generationPayload(target.value, allEligible.checked, state.filters); if (!payload.allEligible && (!Number.isInteger(payload.requestedItems) || payload.requestedItems < 1 || payload.requestedItems > 50000)) throw new Error("Choose a target from 1 to 50,000.");
        const response = await fetch("/api/admin/annotations/generate", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) });
        const result = await response.json().catch(() => ({})); if (!response.ok) throw new Error(result.error || "Corpus generation failed.");
        state.response = result.queue; render(); importStatus.textContent = `${result.added} items added; ${result.total} total.`;
      } catch (failure) { setError(failure.message || String(failure)); } finally { generate.disabled = false; } });
    importButton.addEventListener("click", async () => { if (!importFile.files?.[0]) { setError("Choose a machine-review JSONL file."); return; }
      importButton.disabled = true; setError(""); importStatus.textContent = "Validating import…";
      try { const body = new FormData(); body.append("file", importFile.files[0]); const response = await fetch("/api/admin/annotations/import", { method: "POST", body });
        const summary = await response.json().catch(() => ({})); if (!response.ok) throw new Error(summary.error || "Machine-review import failed.");
        importStatus.textContent = formatImportSummary(summary); await load();
      } catch (failure) { importStatus.textContent = ""; setError(failure.message || String(failure)); } finally { importButton.disabled = false; } });
    [statusFilter, conceptFilter, companyFilter].forEach(control => control.addEventListener("change", () => {
      state.filters = { status: statusFilter.value, concept: conceptFilter.value, company: companyFilter.value }; void load(); }));
    allEligible.addEventListener("change", () => { target.disabled = allEligible.checked; });
    host.addEventListener("keydown", event => { const action = shortcutFor(event); if (!action || !state.response?.item) return; event.preventDefault();
      if (action === "toggleContext") card.querySelector(".annotation-toggles button:first-child")?.click();
      else if (action === "toggleFull") card.querySelector(".annotation-toggles button:last-child")?.click();
      else card.querySelector(`[data-decision="${action}"]`)?.click(); });
    void load();
  }
  return { shortcutFor, validateDecision, queueUrl, exportUrl, generationPayload, formatImportSummary, mount };
});
