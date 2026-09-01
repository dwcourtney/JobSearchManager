(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.AnnotationLabeling = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";
  const shortcutDecisions = Object.freeze({ "1": "correct", "2": "incorrect", "3": "differentLabel", "4": "multipleLabels", "5": "none", "s": "unsure" });
  function isTypingTarget(target) { const tag = String(target?.tagName || "").toLowerCase(); return ["input", "textarea", "select"].includes(tag) || target?.isContentEditable === true; }
  function shortcutFor(event) {
    if (!event || event.ctrlKey || event.metaKey || event.altKey || isTypingTarget(event.target)) return null;
    const key = String(event.key || "").toLowerCase(); return shortcutDecisions[key] || (key === "c" ? "toggleContext" : key === "f" ? "toggleFull" : null);
  }
  function validateDecision(decision, conceptIds) {
    const count = [...new Set(conceptIds || [])].length;
    if (decision === "differentLabel" && count !== 1) return "Choose exactly one replacement concept.";
    if (decision === "multipleLabels" && count < 2) return "Choose at least two concepts.";
    if (!["correct", "incorrect", "differentLabel", "multipleLabels", "none", "unsure"].includes(decision)) return "Choose a valid decision.";
    return "";
  }
  function queryUrl(base, filters, includeStatus = false) {
    const query = new URLSearchParams();
    if (includeStatus && filters?.status) query.set("status", filters.status);
    if (filters?.concept) query.set("concept", filters.concept);
    if (filters?.company) query.set("company", filters.company);
    return `${base}?${query}`;
  }
  function queueUrl(filters, base = "/api/admin/annotations/queue") { return queryUrl(base, filters, true); }
  function generationStatusUrl(filters) { return queryUrl("/api/admin/annotations/generation-status", filters); }
  function exportUrl(mode, filters) { return queryUrl("/api/admin/annotations/export", { ...filters, status: mode }, true).replace("status=", "mode="); }
  function generationPayload(itemsToAdd, allEligible, filters) {
    return { requestedItems: allEligible ? null : Number(itemsToAdd), allEligible: Boolean(allEligible), company: filters?.company || null, concept: filters?.concept || null };
  }
  function formatImportSummary(summary) { return `Imported: ${summary.imported} · Unchanged: ${summary.unchanged} · Conflicts: ${summary.conflicts} · Rejected: ${summary.rejected}`; }
  function formatGenerationResult(result) {
    const total = Number(result.total || 0).toLocaleString();
    if (!result.added) return `No items were added because no eligible ungenerated items remain. Corpus total: ${total}.`;
    const message = `Added ${Number(result.added).toLocaleString()} new ${result.added === 1 ? "item" : "items"}. Corpus total: ${total}.`;
    return result.remainingEligible === 0 ? `${message} No additional eligible items remain.` : message;
  }
  function element(tag, className, text) { const node = document.createElement(tag); if (className) node.className = className; if (text !== undefined) node.textContent = text; return node; }
  function replaceCompanies(select, companies) { const selected = select.value; select.replaceChildren(new Option("All companies", "")); companies.forEach(company => select.add(new Option(company, company))); select.value = selected; }

  function mountHuman(host) {
    if (!host || host.dataset.annotationMounted === "human") return;
    host.dataset.annotationMounted = "human";
    const state = { response: null, source: null, filters: { status: "unreviewed", concept: "", company: "" }, saving: false };
    const heading = element("h3", "", "Human Labeling");
    const intro = element("p", "account-help", "Review individual annotation decisions and resolve uncertain or conflicting labels.");
    const filters = element("div", "annotation-toolbar annotation-review-toolbar");
    const statusFilter = document.createElement("select"); statusFilter.setAttribute("aria-label", "Review queue");
    [["unreviewed", "Unreviewed"], ["machineDisagreement", "Machine disagreements"], ["humanUnreviewedMachine", "Human-unreviewed machine labels"], ["unsure", "Unsure / ambiguous"], ["rareConcept", "Rare concepts"], ["relabeledConflicting", "Relabeled / conflicting"], ["humanReviewed", "Human-reviewed"], ["trainingEligible", "Training-eligible"], ["excluded", "Excluded"], ["all", "All"]]
      .forEach(([value, label]) => statusFilter.add(new Option(label, value)));
    const conceptFilter = document.createElement("select"); conceptFilter.setAttribute("aria-label", "Concept filter"); conceptFilter.add(new Option("All concepts", ""));
    const companyFilter = document.createElement("select"); companyFilter.setAttribute("aria-label", "Company filter"); companyFilter.add(new Option("All companies", ""));
    filters.append(statusFilter, conceptFilter, companyFilter);
    const stats = element("div", "annotation-stats", "Loading annotation queue…"); stats.setAttribute("role", "status"); stats.setAttribute("aria-live", "polite");
    const error = element("p", "reset-confirmation-error"); error.hidden = true; error.setAttribute("role", "alert");
    const card = element("article", "annotation-card");
    const shortcuts = element("p", "annotation-shortcuts", "Keys: 1 Correct · 2 Incorrect · 3 Different · 4 Multiple · 5 None · S Unsure · C Context · F Full posting");
    host.append(heading, intro, filters, stats, error, card, shortcuts);
    function setError(message) { error.textContent = message || ""; error.hidden = !message; }
    function updateFilters(response) {
      if (conceptFilter.options.length === 1) (response.concepts || []).forEach(concept => conceptFilter.add(new Option(`${concept.displayName} · ${concept.category}`, concept.id)));
      replaceCompanies(companyFilter, response.companies || []);
    }
    function selectedConceptIds(picker) { return [...picker.querySelectorAll("input[data-concept-id]:checked")].map(input => input.dataset.conceptId); }
    async function decide(decision, conceptIds = [], unsureReason = "") {
      if (state.saving || !state.response?.item) return; const validation = validateDecision(decision, conceptIds);
      if (validation) { setError(validation); return; } state.saving = true; setError(""); card.setAttribute("aria-busy", "true");
      try {
        const response = await fetch(queueUrl(state.filters, `/api/admin/annotations/${encodeURIComponent(state.response.item.id)}/decision`), { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ decision, conceptIds, unsureReason: unsureReason || null }) });
        const payload = await response.json().catch(() => ({})); if (!response.ok) throw new Error(payload.error || "The annotation could not be saved.");
        state.response = payload; state.source = null; render();
      } catch (failure) { setError(failure.message || String(failure)); } finally { state.saving = false; card.removeAttribute("aria-busy"); }
    }
    async function showFullPosting(container) {
      if (!container.hidden) { container.hidden = true; return; }
      try { if (!state.source) { const response = await fetch(`/api/admin/annotations/${encodeURIComponent(state.response.item.id)}/source`, { cache: "no-store" }); if (!response.ok) throw new Error("The full posting could not be loaded."); state.source = await response.json(); }
        container.textContent = state.source.fullPosting; container.hidden = false;
      } catch (failure) { setError(failure.message || String(failure)); }
    }
    function buildPicker(mode) {
      const picker = element("section", "annotation-picker"); const prompt = element("label", "", mode === "differentLabel" ? "Replacement concept" : "Confirmed concepts");
      const search = document.createElement("input"); search.type = "search"; search.placeholder = "Search concept names or IDs";
      const options = element("div", "annotation-picker-options");
      (state.response.concepts || []).forEach(concept => { const label = element("label", "annotation-concept-option"); const input = document.createElement("input"); input.type = mode === "differentLabel" ? "radio" : "checkbox"; input.name = mode === "differentLabel" ? "replacement-concept" : `concept-${concept.id}`; input.dataset.conceptId = concept.id; label.dataset.search = `${concept.displayName} ${concept.id} ${concept.category}`.toLowerCase(); label.append(input, element("span", "", concept.displayName), element("small", "", `${concept.id} · ${concept.category}`)); options.append(label); });
      search.addEventListener("input", () => { const query = search.value.trim().toLowerCase(); options.querySelectorAll("label").forEach(option => { option.hidden = !option.dataset.search.includes(query); }); });
      const save = element("button", "primary-button", "Save label"); save.type = "button"; save.addEventListener("click", () => void decide(mode, selectedConceptIds(picker)));
      picker.append(prompt, search, options, save); setTimeout(() => search.focus(), 0); return picker;
    }
    function render() {
      const response = state.response; updateFilters(response); const top = Object.entries(response.conceptDistribution || {}).sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).slice(0, 3).map(([id, count]) => `${id} ${count}`).join(", ");
      stats.textContent = `${response.stats.reviewed} human-reviewed / ${response.stats.total} queued · ${response.stats.unreviewed} unreviewed · ${response.stats.machineLabeled} machine-labeled · ${response.stats.machineDisagreements} machine disagreements · ${response.stats.humanMachineConflicts} human/machine conflicts · ${response.stats.unsure} unsure · ${response.stats.trainingEligible} training-eligible${top ? ` · leading labels: ${top}` : ""}`;
      card.replaceChildren(); const item = response.item;
      if (!item) { card.append(element("p", "annotation-empty", response.stats.total ? "No item matches these filters." : "No annotation items are available for review.")); return; }
      const meta = element("p", "annotation-meta", `${item.companyId} · ${item.title} · ${item.jobId}`);
      const proposed = element("div", "annotation-proposed"); proposed.append(element("strong", "", "Suggested: "));
      item.candidateConceptIds.forEach(id => { const concept = response.concepts.find(candidate => candidate.id === id); proposed.append(element("span", "annotation-concept-chip", concept ? concept.displayName : id)); });
      if ((item.machineReviews || []).length) { const provenance = element("div", "annotation-provenance"); provenance.append(element("strong", "", "Imported machine opinions: ")); item.machineReviews.forEach(opinion => provenance.append(element("span", "annotation-concept-chip", `${opinion.reviewerType}${opinion.reviewerIdentity ? ` · ${opinion.reviewerIdentity}` : ""}: ${opinion.decision}`))); proposed.append(provenance); }
      if (item.humanProvenance) proposed.append(element("p", "annotation-meta", `Authenticated decision provenance: ${item.humanProvenance}`));
      const evidence = element("blockquote", "annotation-evidence", item.evidence);
      const context = element("p", "annotation-context", `${item.contextBefore}${item.evidence}${item.contextAfter}`.trim()); context.hidden = true;
      const full = element("pre", "annotation-full-posting"); full.hidden = true;
      const controls = element("div", "annotation-actions");
      [["Correct", "correct"], ["Incorrect", "incorrect"], ["Different label", "differentLabel"], ["Multiple labels", "multipleLabels"], ["None", "none"], ["Unsure / Skip", "unsure"]].forEach(([label, decision]) => { const button = element("button", decision === "correct" ? "primary-button" : "confirmation-secondary-button", label); button.type = "button"; button.dataset.decision = decision; button.addEventListener("click", () => { card.querySelector(".annotation-picker")?.remove(); if (["differentLabel", "multipleLabels"].includes(decision)) controls.after(buildPicker(decision)); else void decide(decision); }); controls.append(button); });
      const toggles = element("div", "annotation-toggles"); const contextButton = element("button", "link-button", "Show context"); contextButton.type = "button"; contextButton.addEventListener("click", () => { context.hidden = !context.hidden; contextButton.textContent = context.hidden ? "Show context" : "Hide context"; });
      const fullButton = element("button", "link-button", "Show full posting"); fullButton.type = "button"; fullButton.addEventListener("click", () => void showFullPosting(full)); toggles.append(contextButton, fullButton);
      card.append(meta, proposed, evidence, toggles, context, full, controls);
    }
    async function load() { setError(""); try { const response = await fetch(queueUrl(state.filters), { cache: "no-store" }); if (!response.ok) throw new Error("The annotation queue could not be loaded."); state.response = await response.json(); state.source = null; render(); } catch (failure) { setError(failure.message || String(failure)); } }
    [statusFilter, conceptFilter, companyFilter].forEach(control => control.addEventListener("change", () => { state.filters = { status: statusFilter.value, concept: conceptFilter.value, company: companyFilter.value }; void load(); }));
    host.addEventListener("annotation:select-queue", event => {
      const queue = event.detail?.queue;
      if (![...statusFilter.options].some(option => option.value === queue)) return;
      statusFilter.value = queue;
      state.filters = { ...state.filters, status: queue };
      void load();
    });
    host.addEventListener("keydown", event => { const action = shortcutFor(event); if (!action || !state.response?.item) return; event.preventDefault(); if (action === "toggleContext") card.querySelector(".annotation-toggles button:first-child")?.click(); else if (action === "toggleFull") card.querySelector(".annotation-toggles button:last-child")?.click(); else card.querySelector(`[data-decision="${action}"]`)?.click(); });
    void load();
  }

  function mountMachine(host, options = {}) {
    if (!host || host.dataset.annotationMounted === "machine") return;
    host.dataset.annotationMounted = "machine";
    const state = { filters: { concept: "", company: "" }, eligible: 0, busy: false };
    const heading = element("h3", "", "Machine Labeling");
    const intro = element("p", "account-help", "Build the annotation corpus, exchange machine-review JSONL, and route exceptions to human review.");
    const error = element("p", "reset-confirmation-error"); error.hidden = true; error.setAttribute("role", "alert");
    const guide = element("section", "annotation-workflow-guide"); guide.append(element("h4", "", "Machine labeling workflow"));
    const guideSteps = element("ol", "annotation-workflow-steps");
    [["Build corpus", "Add eligible annotation items from cached jobs."], ["Export unreviewed", "Download items that still need machine review."],
      ["Review externally", "Send the JSONL to ChatGPT, Codex, or another reviewer."], ["Import machine review", "Upload the returned JSONL to record machine opinions."]]
      .forEach(([title, description]) => { const step = element("li", ""); step.append(element("strong", "", title), element("span", "", description)); guideSteps.append(step); });
    guide.append(guideSteps);
    const currentState = element("section", "annotation-dataset-controls annotation-current-state"); currentState.append(element("h4", "", "Current workflow state"));
    const stateGrid = element("dl", "annotation-state-grid");
    const stateValues = {};
    [["total", "Current corpus"], ["eligible", "Eligible ungenerated"], ["unreviewed", "Unreviewed"],
      ["machineReviewed", "Machine-reviewed"], ["humanReviewed", "Human-reviewed"], ["unsure", "Unsure / excluded"]]
      .forEach(([key, label]) => { const item = element("div", "annotation-state-item"); item.append(element("dt", "", label), stateValues[key] = element("dd", "", "—")); stateGrid.append(item); });
    const workflowAction = element("p", "annotation-next-action", "Loading recommended next actions…"); workflowAction.setAttribute("role", "status"); workflowAction.setAttribute("aria-live", "polite");
    currentState.append(stateGrid, workflowAction);
    const generation = element("section", "annotation-dataset-controls"); generation.append(element("h4", "", "Corpus generation"));
    const generationStatus = element("p", "annotation-generation-status", "Loading corpus availability…"); generationStatus.setAttribute("role", "status"); generationStatus.setAttribute("aria-live", "polite");
    const scope = element("div", "annotation-toolbar");
    const conceptFilter = document.createElement("select"); conceptFilter.setAttribute("aria-label", "Generation concept filter"); conceptFilter.add(new Option("All concepts", ""));
    const companyFilter = document.createElement("select"); companyFilter.setAttribute("aria-label", "Generation company filter"); companyFilter.add(new Option("All companies", "")); scope.append(conceptFilter, companyFilter);
    const generationRow = element("div", "annotation-toolbar"); const itemsLabel = element("label", "annotation-inline-field", "Items to add");
    const items = document.createElement("input"); items.type = "number"; items.min = "1"; items.max = "50000"; items.value = "100"; items.setAttribute("aria-label", "Items to add"); itemsLabel.append(items);
    const addItems = element("button", "primary-button", "Add items"); addItems.type = "button"; const addAll = element("button", "confirmation-secondary-button", "Add all eligible"); addAll.type = "button";
    generationRow.append(itemsLabel, addItems, addAll); generation.append(generationStatus, scope, generationRow, element("p", "account-help", "New deterministic items are appended; existing corpus items and decisions are preserved."));
    const exchange = element("section", "annotation-dataset-controls"); exchange.append(element("h4", "", "Machine review exchange"));
    const primaryExports = element("div", "annotation-export-group annotation-primary-export"); primaryExports.append(element("h5", "", "Primary workflow"));
    const primaryExportRow = element("div", "annotation-toolbar");
    const unreviewedExport = element("a", "primary-link-button", "Export unreviewed"); unreviewedExport.dataset.exportMode = "unreviewed"; unreviewedExport.download = "jsm-annotations-unreviewed.jsonl"; primaryExportRow.append(unreviewedExport); primaryExports.append(primaryExportRow);
    const otherExports = element("div", "annotation-export-group"); otherExports.append(element("h5", "", "Other exports"));
    const otherExportRow = element("div", "annotation-toolbar");
    const exportLinks = [["all", "Export all JSONL"], ["reviewed", "Export reviewed"], ["unsure", "Export unsure"], ["trainingEligible", "Export training-eligible"]]
      .map(([mode, label]) => { const link = element("a", "secondary-link-button", label); link.dataset.exportMode = mode; link.download = `jsm-annotations-${mode}.jsonl`; otherExportRow.append(link); return link; });
    otherExports.append(otherExportRow);
    const importGroup = element("div", "annotation-import-group"); importGroup.append(element("h5", "", "Import reviewed results"));
    const importRow = element("div", "annotation-import-sequence");
    const fileStep = element("label", "annotation-file-step"); fileStep.append(element("strong", "", "1. Choose reviewed JSONL"));
    const importFile = document.createElement("input"); importFile.type = "file"; importFile.accept = ".jsonl,application/x-ndjson,application/json"; importFile.setAttribute("aria-label", "Choose reviewed machine JSONL file"); fileStep.append(importFile);
    const sequenceArrow = element("span", "annotation-sequence-arrow", "→"); sequenceArrow.setAttribute("aria-hidden", "true");
    const importButton = element("button", "confirmation-secondary-button", "2. Import machine review JSONL"); importButton.type = "button"; importButton.disabled = true;
    const operationStatus = element("p", "annotation-import-status"); operationStatus.setAttribute("role", "status"); operationStatus.setAttribute("aria-live", "polite"); importRow.append(fileStep, sequenceArrow, importButton); importGroup.append(importRow, operationStatus);
    exchange.append(primaryExports, otherExports, importGroup);
    const handoff = element("section", "annotation-dataset-controls annotation-handoff"); handoff.append(element("h4", "", "Resolve in Human Labeling"));
    const handoffIntro = element("p", "account-help", "Machine disagreements, human/machine conflicts, and unsure items remain human decisions.");
    const handoffActions = element("div", "annotation-handoff-actions"); handoff.append(handoffIntro, handoffActions);
    const distribution = element("section", "annotation-dataset-controls"); distribution.append(element("h4", "", "Corpus distribution")); const distributionStatus = element("p", "annotation-stats", "Loading corpus distribution…"); distribution.append(distributionStatus);
    host.append(heading, intro, guide, currentState, generation, exchange, handoff, distribution, error);
    function setError(message) { error.textContent = message || ""; error.hidden = !message; }
    function updateControls() { addItems.disabled = state.busy || state.eligible === 0; addAll.disabled = state.busy || state.eligible === 0; importButton.disabled = state.busy || !importFile.files?.[0]; }
    function updateLinks() { [unreviewedExport, ...exportLinks].forEach(link => { link.href = exportUrl(link.dataset.exportMode, state.filters); }); }
    function addHandoff(count, singular, plural, queue) {
      if (!count) return;
      const button = element("button", "confirmation-secondary-button", `${count.toLocaleString()} ${count === 1 ? singular : plural} · Review in Human Labeling`);
      button.type = "button"; button.addEventListener("click", () => options.navigateToHuman?.(queue)); handoffActions.append(button);
    }
    function renderQueue(response, status) {
      if (conceptFilter.options.length === 1) (response.concepts || []).forEach(concept => conceptFilter.add(new Option(`${concept.displayName} · ${concept.category}`, concept.id)));
      replaceCompanies(companyFilter, response.companies || []); updateLinks();
      stateValues.total.textContent = status.total.toLocaleString(); stateValues.eligible.textContent = status.eligibleUngenerated.toLocaleString();
      stateValues.unreviewed.textContent = response.stats.unreviewed.toLocaleString(); stateValues.machineReviewed.textContent = response.stats.machineLabeled.toLocaleString();
      stateValues.humanReviewed.textContent = response.stats.reviewed.toLocaleString(); stateValues.unsure.textContent = response.stats.unsure.toLocaleString();
      const actions = [];
      if (status.eligibleUngenerated > 0) actions.push("More eligible items can be added to the corpus.");
      if (response.stats.unreviewed > 0) actions.push("Export unreviewed items for the next machine-review batch.");
      if (response.stats.machineDisagreements > 0 || response.stats.humanMachineConflicts > 0 || response.stats.unsure > 0) actions.push("Use the Human Labeling handoff below to resolve exceptions.");
      workflowAction.textContent = actions.join(" ") || "No generation, machine review, or human-resolution action is currently required.";
      handoffActions.replaceChildren();
      addHandoff(response.stats.machineDisagreements, "machine disagreement", "machine disagreements", "machineDisagreement");
      addHandoff(response.stats.humanMachineConflicts, "conflict", "conflicts", "relabeledConflicting");
      addHandoff(response.stats.unsure, "unsure item", "unsure items", "unsure");
      if (!handoffActions.children.length) handoffActions.append(element("p", "annotation-stats", "No disagreement, conflict, or unsure queue currently needs handoff."));
      const companies = Object.entries(response.companyDistribution || {}).sort((a, b) => b[1] - a[1]).slice(0, 4).map(([id, count]) => `${id} ${count}`).join(" · ");
      distributionStatus.textContent = `${response.stats.total.toLocaleString()} total · ${response.stats.unreviewed.toLocaleString()} unreviewed · ${response.stats.reviewed.toLocaleString()} reviewed · ${response.stats.unsure.toLocaleString()} unsure · ${response.stats.trainingEligible.toLocaleString()} training-eligible${companies ? ` · ${companies}` : ""}`;
    }
    async function load() {
      setError(""); try { const [queueResponse, statusResponse] = await Promise.all([fetch(queueUrl({ ...state.filters, status: "all" }), { cache: "no-store" }), fetch(generationStatusUrl(state.filters), { cache: "no-store" })]);
        if (!queueResponse.ok) throw new Error("The annotation corpus could not be loaded."); if (!statusResponse.ok) throw new Error("Corpus availability could not be calculated.");
        const queue = await queueResponse.json(); const status = await statusResponse.json(); state.eligible = status.eligibleUngenerated; renderQueue(queue, status); generationStatus.textContent = `Current corpus: ${status.total.toLocaleString()} items · Eligible ungenerated items: ${status.eligibleUngenerated.toLocaleString()}`;
      } catch (failure) { setError(failure.message || String(failure)); } finally { updateControls(); }
    }
    async function generate(allEligible) {
      const payload = generationPayload(items.value, allEligible, state.filters); if (!allEligible && (!Number.isInteger(payload.requestedItems) || payload.requestedItems < 1 || payload.requestedItems > 50000)) { setError("Choose an item count from 1 to 50,000."); return; }
      state.busy = true; updateControls(); setError(""); operationStatus.textContent = allEligible ? "Adding all eligible items…" : `Adding up to ${payload.requestedItems.toLocaleString()} items…`;
      try { const response = await fetch("/api/admin/annotations/generate", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) }); const result = await response.json().catch(() => ({})); if (!response.ok) throw new Error(result.error || "Corpus generation failed."); state.eligible = result.remainingEligible; operationStatus.textContent = formatGenerationResult(result); await load(); }
      catch (failure) { operationStatus.textContent = ""; setError(failure.message || String(failure)); } finally { state.busy = false; updateControls(); }
    }
    addItems.addEventListener("click", () => void generate(false)); addAll.addEventListener("click", () => void generate(true)); importFile.addEventListener("change", updateControls);
    importButton.addEventListener("click", async () => { if (!importFile.files?.[0]) return; state.busy = true; updateControls(); setError(""); operationStatus.textContent = "Validating import…";
      try { const body = new FormData(); body.append("file", importFile.files[0]); const response = await fetch("/api/admin/annotations/import", { method: "POST", body }); const summary = await response.json().catch(() => ({})); if (!response.ok) throw new Error(summary.error || "Machine-review import failed."); operationStatus.textContent = formatImportSummary(summary); await load(); }
      catch (failure) { operationStatus.textContent = ""; setError(failure.message || String(failure)); } finally { state.busy = false; updateControls(); } });
    [conceptFilter, companyFilter].forEach(control => control.addEventListener("change", () => { state.filters = { concept: conceptFilter.value, company: companyFilter.value }; void load(); }));
    updateControls(); void load();
  }
  return { shortcutFor, validateDecision, queueUrl, generationStatusUrl, exportUrl, generationPayload, formatImportSummary, formatGenerationResult, mountHuman, mountMachine };
});
