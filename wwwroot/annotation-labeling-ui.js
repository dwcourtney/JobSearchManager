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
    return tag === "input" || tag === "textarea" || tag === "select" || target?.isContentEditable === true;
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

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function mount(host) {
    if (!host || host.dataset.annotationMounted === "true") return;
    host.dataset.annotationMounted = "true";
    const state = { response: null, source: null, filters: { status: "unreviewed", concept: "", company: "" }, saving: false };

    const heading = element("h3", "", "Human Labeling");
    const intro = element("p", "account-help",
      "Review deterministic detector evidence to create a provenance-rich corpus. Unsure items remain excluded from training.");
    const toolbar = element("div", "annotation-toolbar");
    const generate = element("button", "primary-button", "Generate 200-item pilot");
    generate.type = "button";
    const exportLink = element("a", "secondary-link-button", "Export reviewed JSONL");
    exportLink.href = "/api/admin/annotations/export";
    exportLink.download = "jsm-annotation-corpus.jsonl";
    const statusFilter = document.createElement("select");
    statusFilter.setAttribute("aria-label", "Review status");
    [["unreviewed", "Unreviewed"], ["reviewed", "Reviewed"], ["unsure", "Unsure"],
      ["disagreement", "Disagreements"], ["trainingEligible", "Training eligible"],
      ["excluded", "Excluded"], ["all", "All"]]
      .forEach(([value, label]) => statusFilter.add(new Option(label, value)));
    const conceptFilter = document.createElement("select");
    conceptFilter.setAttribute("aria-label", "Concept filter");
    conceptFilter.add(new Option("All concepts", ""));
    const companyFilter = document.createElement("select");
    companyFilter.setAttribute("aria-label", "Company filter");
    companyFilter.add(new Option("All companies", ""));
    toolbar.append(generate, exportLink, statusFilter, conceptFilter, companyFilter);

    const stats = element("div", "annotation-stats", "Loading annotation queue…");
    stats.setAttribute("role", "status");
    stats.setAttribute("aria-live", "polite");
    const error = element("p", "reset-confirmation-error");
    error.hidden = true;
    error.setAttribute("role", "alert");
    const card = element("article", "annotation-card");
    const shortcuts = element("p", "annotation-shortcuts",
      "Keys: 1 Correct · 2 Incorrect · 3 Different · 4 Multiple · 5 None · S Unsure · C Context · F Full posting");
    host.append(heading, intro, toolbar, stats, error, card, shortcuts);

    function setError(message) {
      error.textContent = message || "";
      error.hidden = !message;
    }

    function updateFilters(response) {
      if (conceptFilter.options.length === 1) {
        (response.concepts || []).forEach(concept =>
          conceptFilter.add(new Option(`${concept.displayName} · ${concept.category}`, concept.id)));
      }
      const selectedCompany = companyFilter.value;
      while (companyFilter.options.length > 1) companyFilter.remove(1);
      (response.companies || []).forEach(company => companyFilter.add(new Option(company, company)));
      companyFilter.value = selectedCompany;
    }

    function selectedConceptIds(picker) {
      return [...picker.querySelectorAll("input[data-concept-id]:checked")].map(input => input.dataset.conceptId);
    }

    async function decide(decision, conceptIds = [], unsureReason = "") {
      if (state.saving || !state.response?.item) return;
      const validation = validateDecision(decision, conceptIds);
      if (validation) { setError(validation); return; }
      state.saving = true;
      setError("");
      card.setAttribute("aria-busy", "true");
      try {
        const response = await fetch(queueUrl(state.filters,
          `/api/admin/annotations/${encodeURIComponent(state.response.item.id)}/decision`), {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ decision, conceptIds, unsureReason: unsureReason || null })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.error || "The annotation could not be saved.");
        state.response = payload;
        state.source = null;
        render();
      } catch (failure) {
        setError(failure.message || String(failure));
      } finally {
        state.saving = false;
        card.removeAttribute("aria-busy");
      }
    }

    async function showFullPosting(container) {
      if (container.hidden === false) { container.hidden = true; return; }
      try {
        if (!state.source) {
          const response = await fetch(`/api/admin/annotations/${encodeURIComponent(state.response.item.id)}/source`, { cache: "no-store" });
          if (!response.ok) throw new Error("The full posting could not be loaded.");
          state.source = await response.json();
        }
        container.textContent = state.source.fullPosting;
        container.hidden = false;
      } catch (failure) { setError(failure.message || String(failure)); }
    }

    function buildPicker(mode) {
      const picker = element("section", "annotation-picker");
      const prompt = element("label", "", mode === "differentLabel" ? "Replacement concept" : "Confirmed concepts");
      const search = document.createElement("input");
      search.type = "search";
      search.placeholder = "Search concept names or IDs";
      const options = element("div", "annotation-picker-options");
      (state.response.concepts || []).forEach(concept => {
        const label = element("label", "annotation-concept-option");
        const input = document.createElement("input");
        input.type = mode === "differentLabel" ? "radio" : "checkbox";
        input.name = mode === "differentLabel" ? "replacement-concept" : `concept-${concept.id}`;
        input.dataset.conceptId = concept.id;
        const text = element("span", "", concept.displayName);
        const canonical = element("small", "", `${concept.id} · ${concept.category}`);
        label.dataset.search = `${concept.displayName} ${concept.id} ${concept.category}`.toLowerCase();
        label.append(input, text, canonical);
        options.append(label);
      });
      search.addEventListener("input", () => {
        const query = search.value.trim().toLowerCase();
        options.querySelectorAll("label").forEach(option => { option.hidden = !option.dataset.search.includes(query); });
      });
      const save = element("button", "primary-button", "Save label");
      save.type = "button";
      save.addEventListener("click", () => void decide(mode, selectedConceptIds(picker)));
      picker.append(prompt, search, options, save);
      setTimeout(() => search.focus(), 0);
      return picker;
    }

    function render() {
      const response = state.response;
      updateFilters(response);
      const topLabels = Object.entries(response.conceptDistribution || {})
        .sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0])).slice(0, 3)
        .map(([id, count]) => `${id} ${count}`).join(", ");
      stats.textContent = `${response.stats.reviewed} reviewed / ${response.stats.total} queued · ` +
        `${response.stats.unreviewed} remaining · ${response.stats.unsure} unsure · ` +
        `${response.stats.accepted} accepted · ${response.stats.rejected} rejected · ` +
        `${response.stats.relabeled} relabeled · ${response.stats.trainingEligible} training-eligible` +
        (topLabels ? ` · leading labels: ${topLabels}` : "");
      card.replaceChildren();
      const item = response.item;
      if (!item) {
        card.append(element("p", "annotation-empty", response.stats.total ? "No item matches these filters." : "Generate a bounded pilot from the current cached jobs to begin."));
        return;
      }
      const meta = element("p", "annotation-meta", `${item.companyId} · ${item.title} · ${item.jobId}`);
      const proposed = element("div", "annotation-proposed");
      proposed.append(element("strong", "", "Suggested: "));
      item.candidateConceptIds.forEach(id => {
        const concept = response.concepts.find(candidate => candidate.id === id);
        proposed.append(element("span", "annotation-concept-chip", concept ? concept.displayName : id));
      });
      const evidence = element("blockquote", "annotation-evidence", item.evidence);
      const context = element("p", "annotation-context",
        `${item.contextBefore}${item.evidence}${item.contextAfter}`.trim());
      context.hidden = true;
      const full = element("pre", "annotation-full-posting");
      full.hidden = true;
      const controls = element("div", "annotation-actions");
      const actions = [
        ["Correct", "correct"], ["Incorrect", "incorrect"], ["Different label", "differentLabel"],
        ["Multiple labels", "multipleLabels"], ["None", "none"], ["Unsure / Skip", "unsure"]
      ];
      actions.forEach(([label, decision]) => {
        const button = element("button", decision === "correct" ? "primary-button" : "confirmation-secondary-button", label);
        button.type = "button";
        button.dataset.decision = decision;
        button.addEventListener("click", () => {
          card.querySelector(".annotation-picker")?.remove();
          if (decision === "differentLabel" || decision === "multipleLabels") controls.after(buildPicker(decision));
          else void decide(decision);
        });
        controls.append(button);
      });
      const toggles = element("div", "annotation-toggles");
      const contextButton = element("button", "link-button", "Show context");
      contextButton.type = "button";
      contextButton.addEventListener("click", () => { context.hidden = !context.hidden; contextButton.textContent = context.hidden ? "Show context" : "Hide context"; });
      const fullButton = element("button", "link-button", "Show full posting");
      fullButton.type = "button";
      fullButton.addEventListener("click", () => void showFullPosting(full));
      toggles.append(contextButton, fullButton);
      card.append(meta, proposed, evidence, toggles, context, full, controls);
    }

    async function load() {
      setError("");
      try {
        const response = await fetch(queueUrl(state.filters), { cache: "no-store" });
        if (!response.ok) throw new Error("The annotation queue could not be loaded.");
        state.response = await response.json();
        state.source = null;
        render();
      } catch (failure) { setError(failure.message || String(failure)); }
    }

    generate.addEventListener("click", async () => {
      generate.disabled = true;
      setError("");
      try {
        const response = await fetch("/api/admin/annotations/generate", {
          method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ maxItems: 200 })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.error || "The pilot could not be generated.");
        state.response = payload.queue;
        render();
      } catch (failure) { setError(failure.message || String(failure)); }
      finally { generate.disabled = false; }
    });
    [statusFilter, conceptFilter, companyFilter].forEach(control => control.addEventListener("change", () => {
      state.filters = { status: statusFilter.value, concept: conceptFilter.value, company: companyFilter.value };
      void load();
    }));
    host.addEventListener("keydown", event => {
      const action = shortcutFor(event);
      if (!action || !state.response?.item) return;
      event.preventDefault();
      if (action === "toggleContext") card.querySelector(".annotation-toggles button:first-child")?.click();
      else if (action === "toggleFull") card.querySelector(".annotation-toggles button:last-child")?.click();
      else card.querySelector(`[data-decision="${action}"]`)?.click();
    });
    void load();
  }

  return { shortcutFor, validateDecision, queueUrl, mount };
});
