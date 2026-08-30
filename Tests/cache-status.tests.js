"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

assert.match(index,
  /class="subtle header-status"[\s\S]*?id="last-refreshed"[\s\S]*?id="cache-status"[^>]*hidden/);
assert.match(index, /id="cache-banner"[^>]*aria-live="polite"[^>]*hidden/);
assert.match(index, /styles\.css\?v=24/);
assert.match(index, /app\.js\?v=29/);

const updaterStart = app.indexOf("function updateCacheStatus(snapshot)");
const updaterEnd = app.indexOf("function wireKeywordInput", updaterStart);
const updater = updaterStart >= 0 && updaterEnd > updaterStart
  ? app.slice(updaterStart, updaterEnd)
  : "";
assert.ok(updater, "Compact cache-status updater is missing.");
assert.match(updater, /showCompactStatus = usingCache && !snapshot\.isRefreshing/);
assert.match(updater, /elements\.cacheStatus\.textContent = "Using cache"/);
assert.match(updater, /elements\.cacheStatus\.hidden = !showCompactStatus/);
assert.match(updater, /elements\.cacheBanner\.hidden = !snapshot\.error/);
assert.match(updater, /Cached jobs remain available because the live refresh failed/);
assert.doesNotMatch(updater, /toLocaleString|lastRefreshedUtc|Showing cached jobs from/);
assert.doesNotMatch(app, /function updateCacheBanner|Showing cached jobs from/);

assert.match(styles, /\.header-status\s*\{[\s\S]*?display:\s*flex[\s\S]*?flex-wrap:\s*wrap/);
assert.match(styles, /\.cache-status::before\s*\{[^}]*content:\s*"·"/);
assert.match(styles,
  /@media \(max-width: 900px\)[\s\S]*?\.app-header \.header-status\s*\{[^}]*justify-content:\s*flex-start/);

console.log("All deterministic compact cache-status tests passed.");
