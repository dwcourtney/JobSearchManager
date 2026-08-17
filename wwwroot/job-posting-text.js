"use strict";

(function registerJobPostingText(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.JobPostingText = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, () => {
  const ELEMENT_NODE = 1;
  const TEXT_NODE = 3;
  const BLOCK_TAGS = new Set(["ADDRESS", "ARTICLE", "DIV", "H1", "H2", "H3", "H4", "P", "SECTION"]);

  function normalizeHtml(value) {
    return String(value || "")
      .replace(/&(?:amp;)*#(?:x0*a|0*10);/gi, "<br>")
      .replace(/(?:<p\b[^>]*>\s*(?:&nbsp;)?\s*<\/p>\s*)+/gi, "<br>")
      .replace(/(?:\s*<br\s*\/?>){3,}/gi, "<br><br>");
  }

  function toPlainText(root) {
    if (!root) {
      return "";
    }
    return normalizeOutput(root.nodeType === ELEMENT_NODE
      ? renderNode(root, 0)
      : renderChildren(root, 0));
  }

  function renderChildren(node, listDepth) {
    return Array.from(node.childNodes || [], child => renderNode(child, listDepth)).join("");
  }

  function renderNode(node, listDepth) {
    if (node.nodeType === TEXT_NODE) {
      return (node.nodeValue || node.textContent || "").replace(/\u00a0/g, " ");
    }
    if (node.nodeType !== ELEMENT_NODE) {
      return "";
    }

    const tag = String(node.tagName || "").toUpperCase();
    if (tag === "BR") {
      return "\n";
    }
    if (tag === "UL" || tag === "OL") {
      return renderList(node, listDepth, tag === "OL");
    }

    const content = renderChildren(node, listDepth);
    return BLOCK_TAGS.has(tag) ? `\n\n${content}\n\n` : content;
  }

  function renderList(list, listDepth, ordered) {
    const items = Array.from(list.childNodes || [])
      .filter(node => node.nodeType === ELEMENT_NODE && String(node.tagName).toUpperCase() === "LI");
    const lines = items.map((item, index) => renderListItem(item, listDepth, ordered, index + 1));
    return `\n\n${lines.join("\n")}\n\n`;
  }

  function renderListItem(item, listDepth, ordered, ordinal) {
    const nestedLists = [];
    let body = "";
    for (const child of Array.from(item.childNodes || [])) {
      const tag = child.nodeType === ELEMENT_NODE ? String(child.tagName || "").toUpperCase() : "";
      if (tag === "UL" || tag === "OL") {
        nestedLists.push(child);
      } else {
        body += renderNode(child, listDepth);
      }
    }

    const indentation = "  ".repeat(listDepth);
    const continuationIndentation = `${indentation}  `;
    const prefix = ordered ? `${ordinal}.` : "•";
    const bodyLines = normalizeOutput(body).split("\n");
    let result = `${indentation}${prefix} ${bodyLines.shift() || ""}`.trimEnd();
    for (const line of bodyLines) {
      result += line ? `\n${continuationIndentation}${line}` : "\n";
    }
    for (const nested of nestedLists) {
      result += `\n${trimBlankLines(renderNode(nested, listDepth + 1))}`;
    }
    return result;
  }

  function normalizeOutput(value) {
    const normalizedLines = String(value || "")
      .replace(/\r\n?/g, "\n")
      .replace(/\u00a0/g, " ")
      .split("\n")
      .map(line => {
        const leadingWhitespace = line.match(/^[\t ]*/)?.[0] || "";
        return leadingWhitespace + line
          .slice(leadingWhitespace.length)
          .replace(/[\t\f\v ]+/g, " ")
          .trimEnd();
      });

    const output = [];
    for (const line of normalizedLines) {
      const normalizedLine = line.trimStart();
      if (!normalizedLine) {
        if (output.length && output[output.length - 1] !== "") {
          output.push("");
        }
      } else {
        output.push(line.match(/^\s*(?:•|\d+\.)\s/) ? line : normalizedLine);
      }
    }
    return trimBlankLines(output.join("\n"));
  }

  function trimBlankLines(value) {
    return value.replace(/^\n+|\n+$/g, "").replace(/\n{3,}/g, "\n\n");
  }

  return { normalizeHtml, toPlainText };
});
