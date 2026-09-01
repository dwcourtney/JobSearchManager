"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const theme = fs.readFileSync(path.join(root, "wwwroot", "theme.css"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const bootstrap = fs.readFileSync(path.join(root, "wwwroot", "theme-bootstrap.js"), "utf8");

const officialNord = [
  "#2E3440", "#3B4252", "#434C5E", "#4C566A",
  "#D8DEE9", "#E5E9F0", "#ECEFF4",
  "#8FBCBB", "#88C0D0", "#81A1C1", "#5E81AC",
  "#BF616A", "#D08770", "#EBCB8B", "#A3BE8C", "#B48EAD"
];
officialNord.forEach((value, index) => {
  assert.match(theme, new RegExp(`--nord${index}:\\s*${value}`, "i"));
});

const officialDracula = {
  background: "#282A36",
  "current-line": "#6272A4",
  selection: "#44475A",
  foreground: "#F8F8F2",
  comment: "#6272A4",
  red: "#FF5555",
  orange: "#FFB86C",
  yellow: "#F1FA8C",
  green: "#50FA7B",
  cyan: "#8BE9FD",
  purple: "#BD93F9",
  pink: "#FF79C6"
};
Object.entries(officialDracula).forEach(([name, value]) => {
  assert.match(theme, new RegExp(`--dracula-${name}:\\s*${value}`, "i"));
});

for (const [id, label] of [
  ["nord-polar-night", "Nord — Polar Night"],
  ["nord-snow-storm", "Nord — Snow Storm"],
  ["dracula", "Dracula"]
]) {
  assert.match(index, new RegExp(`<option value="${id}">${label}</option>`));
  assert.match(theme, new RegExp(`\\[data-theme="${id}"\\]`));
  assert.match(app, new RegExp(`"${id}"`));
  assert.match(bootstrap, new RegExp(`"${id}"`));
}
assert.match(index, /theme\.css\?v=11/);
assert.match(index, /styles\.css\?v=32/);
assert.match(index, /app\.js\?v=39/);
assert.match(app, /function normalizeThemeMode\(value\)/);
assert.match(app, /SUPPORTED_THEME_MODES\.has\(value\) \? value : "light"/);

const lightStart = theme.indexOf('[data-theme="light"] {');
const darkStart = theme.indexOf('/* Dark theme:');
const sharedNordStart = theme.indexOf('/* Shared Nord semantic mapping.');
assert.ok(lightStart >= 0 && darkStart > lightStart && sharedNordStart > darkStart);
const lightTheme = theme.slice(lightStart, darkStart);
const sharedNord = theme.slice(sharedNordStart);
const requiredPaintTokens = [...lightTheme.matchAll(/^\s*(--color-[a-z0-9-]+)\s*:/gmi)]
  .map(match => match[1]);
const nordPaintTokens = new Set(
  [...sharedNord.matchAll(/^\s*(--color-[a-z0-9-]+)\s*:/gmi)].map(match => match[1]));
const missingNordTokens = [...new Set(requiredPaintTokens)]
  .filter(token => !nordPaintTokens.has(token));
assert.deepEqual(missingNordTokens, [],
  `Nord semantic mapping is missing paint tokens: ${missingNordTokens.join(", ")}`);

const draculaStart = theme.indexOf('/* Dracula: official Dracula Classic palette');
assert.ok(draculaStart > sharedNordStart, "Dracula semantic block is missing.");
const draculaTheme = theme.slice(draculaStart);
const draculaPaintTokens = new Set(
  [...draculaTheme.matchAll(/^\s*(--color-[a-z0-9-]+)\s*:/gmi)].map(match => match[1]));
const missingDraculaTokens = [...new Set(requiredPaintTokens)]
  .filter(token => !draculaPaintTokens.has(token));
assert.deepEqual(missingDraculaTokens, [],
  `Dracula semantic mapping is missing paint tokens: ${missingDraculaTokens.join(", ")}`);
assert.match(draculaTheme, /--color-primary-action-text:\s*var\(--dracula-background\)/);
assert.match(theme, /--dracula-canvas:\s*#20222B/i);
assert.match(theme, /--dracula-panel:\s*#2D2F3C/i);
assert.match(theme, /--dracula-elevated:\s*#333544/i);
assert.match(theme, /--dracula-control:\s*#242631/i);
assert.match(theme, /--dracula-hover:\s*#363948/i);
assert.match(theme, /--dracula-selected:\s*#4C536F/i);
assert.match(theme, /--dracula-outline:\s*#3C435E/i);
assert.match(theme, /--dracula-text-muted:\s*#D7DBE1/i);
for (const semantic of ["red", "orange", "yellow", "green", "cyan", "purple", "pink"]) {
  assert.match(theme, new RegExp(`--dracula-${semantic}-tint:\\s*#[0-9a-f]{6}`, "i"));
}
assert.match(draculaTheme, /--color-background:\s*var\(--dracula-canvas\)/);
assert.match(draculaTheme, /--color-surface:\s*var\(--dracula-panel\)/);
assert.match(draculaTheme, /--color-header-background:\s*var\(--dracula-control\)/);
assert.match(draculaTheme,
  /--color-settings-tab-bar-background:\s*var\(--dracula-control\)/);
assert.match(draculaTheme,
  /--color-settings-section-header-background:\s*transparent/);
assert.match(draculaTheme,
  /--color-accordion-header-background:\s*var\(--dracula-elevated\)/);
assert.match(draculaTheme,
  /--color-accordion-header-border:\s*var\(--dracula-current-line\)/);
assert.match(draculaTheme,
  /--color-accordion-body-background:\s*var\(--dracula-panel\)/);
assert.match(draculaTheme,
  /--color-job-selected:\s*var\(--dracula-selected\)/);
assert.match(draculaTheme,
  /--color-error-background:\s*var\(--dracula-red-tint\)/);
assert.match(draculaTheme,
  /--color-fit-compatible-background:\s*var\(--dracula-green-tint\)/);
assert.match(draculaTheme,
  /--color-summary-status-background:\s*var\(--dracula-cyan-tint\)/);
assert.match(draculaTheme,
  /--color-deployment-strong-background:\s*var\(--dracula-pink-tint\)/);
assert.doesNotMatch(draculaTheme,
  /--color-(?:text-secondary|evidence-text|input-prefix):\s*var\(--dracula-comment\)/);

assert.doesNotMatch(styles, /#[0-9a-f]{3,8}\b|(?:rgb|hsl)a?\s*\(/i);
assert.doesNotMatch(index, /\sstyle\s*=/i);
assert.doesNotMatch(app, /#(?:[0-9a-f]{6}|[0-9a-f]{8})\b|(?:rgb|hsl)a?\s*\(/i);
assert.match(styles,
  /\.settings-tabs\s*\{[\s\S]*?background:\s*var\(--color-settings-tab-bar-background\)/);
assert.match(styles,
  /\.qualification-subtabs\s*\{[\s\S]*?background:\s*var\(--color-settings-subtab-bar-background\)/);
assert.match(styles,
  /\.settings-section h3\s*\{[\s\S]*?background:\s*var\(--color-settings-section-header-background\)/);
assert.match(styles,
  /\.credential-inventory-category > summary\s*\{[\s\S]*?background:\s*var\(--color-accordion-header-background\)/);

function luminance(hex) {
  const channels = hex.slice(1).match(/../g).map(value => parseInt(value, 16) / 255)
    .map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}
function contrast(first, second) {
  const values = [luminance(first), luminance(second)].sort((a, b) => b - a);
  return (values[0] + 0.05) / (values[1] + 0.05);
}

assert.ok(contrast("#ECEFF4", "#3B4252") >= 4.5,
  "Polar Night primary text contrast is below WCAG AA.");
assert.ok(contrast("#D8DEE9", "#3B4252") >= 4.5,
  "Polar Night secondary text contrast is below WCAG AA.");
assert.ok(contrast("#2E3440", "#ECEFF4") >= 4.5,
  "Snow Storm primary text contrast is below WCAG AA.");
assert.ok(contrast("#4C566A", "#ECEFF4") >= 4.5,
  "Snow Storm secondary/action text contrast is below WCAG AA.");
assert.ok(contrast("#F8F8F2", "#282A36") >= 4.5,
  "Dracula foreground contrast is below WCAG AA.");
assert.ok(contrast("#F8F8F2", "#44475A") >= 4.5,
  "Dracula text on selected surfaces is below WCAG AA.");
assert.ok(contrast("#282A36", "#BD93F9") >= 4.5,
  "Dracula primary-action text contrast is below WCAG AA.");
assert.ok(contrast("#F8F8F2", "#2D2F3C") >= 4.5,
  "Dracula foreground on the derived panel is below WCAG AA.");
assert.ok(contrast("#D7DBE1", "#2D2F3C") >= 4.5,
  "Dracula secondary text on the derived panel is below WCAG AA.");
assert.ok(contrast("#F8F8F2", "#242631") >= 4.5,
  "Dracula input text on the derived control surface is below WCAG AA.");
assert.ok(contrast("#F8F8F2", "#4C536F") >= 4.5,
  "Dracula selected-row text contrast is below WCAG AA.");

console.log("All deterministic Nord and Dracula theme/persistence UI tests passed.");
