"use strict";

const assert = require("node:assert/strict");
const { normalizeHtml, toPlainText } = require("../wwwroot/job-posting-text.js");

const TEXT_NODE = 3;
const ELEMENT_NODE = 1;

function text(value) {
  return { nodeType: TEXT_NODE, nodeValue: value, textContent: value };
}

function element(tagName, ...children) {
  return { nodeType: ELEMENT_NODE, tagName: tagName.toUpperCase(), childNodes: children.flat() };
}

const tests = [
  ["normal paragraphs", () => {
    const root = element("div", element("p", text("First paragraph.")), element("p", text("Second paragraph.")));
    assert.equal(toPlainText(root), "First paragraph.\n\nSecond paragraph.");
  }],
  ["headings and inline emphasis", () => {
    const root = element("div", element("h2", text("Responsibilities")), element("p", text("Build "), element("strong", text("reliable")), text(" systems.")));
    assert.equal(toPlainText(root), "Responsibilities\n\nBuild reliable systems.");
  }],
  ["bulleted lists", () => {
    const root = element("ul", element("li", text("Design services")), element("li", text("Review changes")));
    assert.equal(toPlainText(root), "• Design services\n• Review changes");
  }],
  ["ordered and nested lists", () => {
    const root = element("ol",
      element("li", text("Plan"), element("ul", element("li", text("Assess risk")), element("li", text("Set scope")))),
      element("li", text("Deliver")));
    assert.equal(toPlainText(root), "1. Plan\n  • Assess risk\n  • Set scope\n2. Deliver");
  }],
  ["decoded entities and excess whitespace", () => {
    const root = element("p", text("Research &\u00a0Development   supports\tteams."));
    assert.equal(toPlainText(root), "Research & Development supports teams.");
  }],
  ["posting source excludes application chrome", () => {
    const app = element("main",
      element("nav", text("At a Glance Full Posting Open original posting")),
      element("div", element("p", text("Original posting content."))));
    assert.equal(toPlainText(app.childNodes[1]), "Original posting content.");
  }],
  ["complete source is independent of visible viewport", () => {
    const root = element("div",
      element("p", text("Visible opening.")),
      element("p", text("Middle content.")),
      element("p", text("Off-screen closing content.")));
    root.scrollTop = 0;
    root.clientHeight = 1;
    root.scrollHeight = 1000;
    assert.equal(toPlainText(root), "Visible opening.\n\nMiddle content.\n\nOff-screen closing content.");
  }],
  ["Northrop empty spacer paragraphs preserve adjacent metadata fields", () => {
    const source = "RELOCATION ASSISTANCE: No<p style=\"text-align:inherit\"></p>" +
      "<p style=\"text-align:inherit\"></p>CLEARANCE REQUIRED FOR START: No" +
      "<p></p>CLEARANCE TYPE: Secret<p></p>TRAVEL: Yes, 75% of the Time";
    const normalized = normalizeHtml(source);
    assert.match(normalized, /No<br>CLEARANCE REQUIRED/);
    assert.match(normalized, /No<br>CLEARANCE TYPE/);
    assert.match(normalized, /Secret<br>TRAVEL/);
  }],
  ["RTX encoded numeric newlines become bounded breaks", () => {
    const normalized = normalizeHtml(
      "2026-08-14&amp;#xa;&amp;#xA;&#10;&#xa;United States of America");
    assert.equal(normalized, "2026-08-14<br><br>United States of America");
    assert.doesNotMatch(normalized, /&#(?:x?a|10);/i);
  }],
  ["meaningful paragraphs, lists, named entities, and unsafe markup are preserved for sanitization", () => {
    const source = "<p>Research &amp; Development</p><ul><li>One</li></ul>" +
      "<script>alert(1)</script>";
    const normalized = normalizeHtml(source);
    assert.equal(normalized, source);
  }]
];

for (const [name, test] of tests) {
  test();
  console.log(`PASS Job posting text: ${name}`);
}

console.log(`All ${tests.length} deterministic job-posting text tests passed.`);
