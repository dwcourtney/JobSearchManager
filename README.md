# Leidos Jobs Viewer

A small local viewer for the public Leidos Workday job feed. It discovers
Workday's current country and location facets, retrieves every job matching the
user-selected scope, loads exact posting dates and formatted descriptions, and
presents the results in a browser-based master/detail interface. It also
extracts the stated annual pay range and flags remote listings whose
descriptions contain geographic or proximity conditions.

## Why this exists

The default Workday interface did not provide the reliable filtering, sorting,
and review workflow needed here. In particular, it did not offer a useful way
to consume results in exact posting-date order, and its exclusion behavior was
not dependable for practical title filtering. During development, using
`-Substation` to exclude titles such as “Substation Engineer” did not work as
expected; the observed results instead favored postings containing that term.

This viewer adds a small, predictable local layer for include/exclude keywords,
exact-date sorting, salary analysis, NEW tracking, dismissal, clearance and
remote-location indicators, persistent history, automatic checks, and more
usable grouping. It does not scrape rendered pages: it consumes Workday's public
CXS JSON endpoints, and the original Workday posting remains authoritative.

Every result has an **Open in Workday** link back to that authoritative source.

## Architecture

- .NET 10 / ASP.NET Core Minimal API
- One local process bound only to `127.0.0.1:54321`
- Vanilla HTML, CSS, and JavaScript frontend
- Central semantic theme variables in `wwwroot/theme.css`
- Application-local JSON persistence; no database
- Vendored DOMPurify for description sanitization
- No Node, React, Angular, Blazor, IIS, cloud service, or Python dependency

The backend calls the Workday CXS JSON endpoints, walks listing pages in groups
of 20, and retrieves detail records with bounded concurrency. A failed overall
refresh retains the last successful snapshot. A failed individual detail request
retains that job's listing metadata and reports a warning.

## Persistent state

All persistent state is stored in a visible `data` directory beside the running
executable, resolved from `AppContext.BaseDirectory` rather than the current
working directory:

```text
LeidosJobsViewer.exe
data/
    settings.json
    jobs-cache.json
    job-history.json
```

The application never redirects state to `%LOCALAPPDATA%`, `%APPDATA%`, `%TEMP%`,
or another user-profile application-data directory. If the executable directory
is not writable, startup reports a clear error and does not fall back elsewhere.
Copy or move the executable files and their `data` directory together to retain
all state.

`settings.json` contains the selected Workday country/location IDs and display
labels, inclusion and exclusion terms, annual minimum salary, keyword scope,
remote-location mode, inclusion-highlighting preference, automatic-check
settings, selected light/dark theme, the extensible user qualification profile's
completed-education and separate security/clearance entries, the conservative
strict-education and strict day-one-clearance filters, and collapsed posting-age
groups. It also stores whether the complete **Search & Filters**
section is collapsed. Local filter changes save automatically after a short
debounce. `jobs-cache.json` contains the complete most recent successful
normalized snapshot, including formatted descriptions and the Workday query
that produced it. `job-history.json`
contains persistent identity records with requisition ID, Workday external path,
first/last-seen timestamps, viewed status, and per-job dismissed state. Writes
use a temporary file in the same `data` directory and replace the previous
document only after the new JSON has been flushed.

At startup, settings and cached jobs are loaded before the live Workday refresh
begins. Cached results appear immediately and are identified as cached while a
refresh runs. A successful refresh replaces the current snapshot and cache. A
failed refresh leaves cached results visible and reports the failure. Missing
files initialize cleanly. Malformed files are logged, left untouched, and
replaced by sensible defaults in memory rather than crashing the application.
An older or mismatched cache is never displayed for a different country/location
selection. Job history remains global across selections, so moving between
scopes does not make an already-known requisition NEW again.

Major foreground retrievals use a modal loading panel over a dimmed, blurred
application. The underlying Jobs and Settings controls are inert until startup
or refresh finishes. The panel reports real backend phases and, while details
are being fetched, the completed and total job counts; its progress animation
remains indeterminate rather than inventing a percentage. It appears for the
initial load, manual refreshes, source changes, and complete refreshes started
after an automatic check discovers a new identity. The lightweight automatic
identity-only check remains silent. Cached or existing results can remain
visible behind the panel, but are deliberately unavailable until the refresh
completes.

## Automatic new-job checks

While the application is running, it checks for new posting identities once an
hour by default. The interval can be disabled or changed to 30 minutes, 1, 2,
4, or 8 hours in **Search & Filters**. The setting is persisted in the local
`data/settings.json` file. Closing the application stops the timer; this is not
a Windows service or scheduled task.

Each automatic check downloads only Workday's paginated listing metadata and
compares requisition IDs (with external paths as fallback identities) with the
global job history. If every identity is already known, descriptions are not
downloaded and the current snapshot is not replaced. If at least one unknown
identity is found, the existing complete refresh pipeline runs once so the new
posting receives its full sanitized description and normal NEW tracking.

Automatic checks never overlap a manual refresh or another automatic check.
Completing a manual refresh resets the interval so an automatic check does not
immediately follow it. A transient automatic-check failure is logged but does
not replace the current snapshot or show a disruptive error banner. The UI
shows the last-check and next-check times and notices a successful automatic
refresh without clearing the selected job, changing filters, or deliberately
resetting the list/detail scroll positions.

## Appearance

The **Theme** control offers Light and Dark modes and persists the choice in
`data/settings.json`. It is located in the dedicated **Settings** view alongside
the Workday source and automatic-check preferences. Light mode preserves the
original appearance. Dark mode
uses purpose-built semantic colors for the page, controls, result states,
badges, warnings, highlights, and formatted Workday descriptions. Workday
inline colors and backgrounds are stripped by the existing HTML sanitizer, so
description content inherits the selected local theme while preserving its
headings, paragraphs, emphasis, lists, and safe links.

All visual tokens are centralized in `wwwroot/theme.css`; layout remains in
`wwwroot/styles.css`. A small value in browser local storage is used only as an
early-paint hint to avoid a light flash before settings load. The backend JSON
setting beside the executable remains authoritative and corrects the hint as
soon as the page initializes.

## Jobs and Settings views

The compact application toolbar switches between two client-side views without
reloading the page. **Jobs** is the default and contains only controls used while
actively evaluating postings: include/exclude keywords, matching scope,
highlighting, remote-location analysis, hidden-job visibility, result counts, the
result list, and job details. Highlighting sits directly beside the Include label
as part of the same control group. Its Workday source indicator is a small link to
**Settings**. The collapsible filter summary still shows the active saved minimum
salary even though its editor is an application-level preference.

**Settings** contains the less frequently changed Workday country/location query,
minimum acceptable salary, completed education profile, security/clearance profile,
strict qualification filters, automatic-check enablement and interval, and theme.
Moving between views does not alter filter values, job selection, scroll state,
cached data, or settings. The navigation itself is presentation-only and the
application always opens on Jobs.

The selected-job pane has two local tabs. **At a Glance** is the default whenever
a different job is selected and presents one unified qualification dossier rather
than independent parser cards. Compact aligned sections move from job facts and a
deterministic blocker summary through clearance, academic paths, credentials, and
notes/warnings. Academic alternatives remain visible as concise accepted paths;
clearance and credential evidence remains available through native disclosure
controls without dominating the default scan view. **Full Posting** is the clean
reading view: a small source strip and the sanitized Workday description, without
duplicated analysis blocks above it. The shared title and authoritative **Open in
Workday** link remain available in both views.

## Workday country and location scope

The **Workday country** and **Workday location** selectors are populated from
the live CXS response's `locationCountry` and `locations` facets. Counts shown
beside each choice are Workday's current counts. Changing country reloads the
dependent location choices but does not start a job crawl. Choose **Apply
location** to persist the selection and explicitly retrieve that scope.

The initial and upgrade-safe default remains **United States of America** /
**6314 Remote/Teleworker US**. **All countries** and **All locations** are true
unfiltered choices: the corresponding facet key is omitted from the CXS request,
not sent as an empty or invented value. The controls are deliberately
single-select for a small personal UI. Workday accepts multiple location IDs
with OR semantics, so a future multi-select could use the same backend request
shape if it becomes useful.

Workday currently exposes at most 2,000 unique postings from an unrestricted
query. At offset 2,000 it repeats its first page; the viewer detects the
all-duplicate page and terminates safely. Narrower country/location queries are
not affected by this ceiling. Broad selections also require many detail calls,
so the viewer uses bounded concurrency and retries temporary 429/503 responses
with backoff.

The Workday selectors define which postings are downloaded. The separate
**Remote location** radio buttons remain local analysis filters over the loaded
descriptions and never alter the Workday facet query.

The complete filter area can be collapsed from its keyboard-accessible header.
Its compact row retains a short active-filter summary while the results and
description panes reclaim the remaining viewport height. This is presentation
state only: collapsing does not alter filters, rerender the job set, or issue a
Workday request. The preference persists in the same application-local
`data/settings.json` file as the filter values.

## NEW jobs and posting-age groups

A job whose stable identity has never appeared in `job-history.json` receives a
distinct **NEW** badge. Requisition ID is the preferred identity; Workday's
external path is retained as a fallback. NEW persists across application
restarts and ordinary refreshes. It is cleared only by an actual click on the job
row or **Open in Workday**. Automatic initial selection does not clear it.
History is retained when a posting disappears, so the same requisition is not
treated as new if it later returns.

Each result row also has a compact, keyboard-accessible dismiss control. A
dismissal is a local decision about that exact stable job identity; it does not
change keywords, salary or location filters, and it never calls Workday.
Dismissed jobs remain in `job-history.json` with `dismissed` and `dismissedAt`
fields, continue receiving normal `lastSeenAt` updates, and remain dismissed if
the requisition disappears and later returns. Requisition ID is the preferred
identity, with the same external-path fallback used by NEW tracking.

**Show hidden** displays dismissed jobs in their normal posting-age groups with
muted styling, a **Hidden** badge, and a **Restore** action. Normal group and
result counts exclude dismissed jobs; when shown, the counts include them.
Dismissal and restoration do not count as viewing, so an unviewed NEW job keeps
its NEW status through both actions. Clicking the job row itself retains the
existing viewed-state behavior.

Filtered results are grouped into mutually exclusive, collapsible sections:

- Posted Today
- Posted Yesterday
- Posted 2–7 Days Ago
- Posted 8–14 Days Ago
- Posted 15–30 Days Ago
- Posted 31+ Days Ago

Dates are compared as local calendar dates because Workday supplies `startDate`
without a posting time. Empty groups are omitted. Each header shows the number
of matching jobs, and collapse state persists in `settings.json`. Collapsing is
only presentation state: `Showing X of Y jobs` continues to count every job that
passes the filters. Jobs remain newest-first, with title and requisition ID as
stable secondary ordering.

Filtering occurs entirely in the browser against the loaded snapshot. Inclusion
keywords use ANY matching; exclusion keywords remove a job on ANY match. Both
can search title/metadata or title/metadata plus the full description. An annual
minimum-pay filter compares against the stated maximum, while unknown, ambiguous,
or hourly compensation remains visible. A remote-location filter can show all,
hide flagged jobs, or show only flagged jobs. Changing any filter does not call
Workday.

**Highlight include keywords** is enabled by default. With no inclusion terms it
has no visual effect, making this the least surprising default. When terms are
present, case-insensitive matches are marked in result titles/metadata and in the
selected job's formatted description. Highlighting is visual only and remains
independent of the selected matching scope: a job can qualify on metadata while
the same term is also highlighted in its description.

When an annual minimum is configured, visible jobs receive a **Limited salary
headroom** warning if that minimum is at least 75% of the way through the posted
hiring range. This warning is informational and never changes filtering or result
counts. It describes only the currently advertised hiring range—not a future
compensation ceiling. Unknown, hourly, one-bound, and unconfigured cases do not
receive the warning.

Salary extraction prefers a role-specific phrase such as "anticipated salary
range for this role" when present; otherwise it uses the standard Leidos `Pay
Range` section. Dollar amounts may contain commas and optional cents. Explicitly
hourly ranges are classified separately and are not silently compared with an
annual threshold.

The remote-location detector is deterministic and intentionally conservative.
It recognizes the wording currently present in Leidos descriptions: commuting
distance, numeric mile/hour radius, local hybrid attendance, required states or
regions, and explicit regional/time-zone preferences. It ignores generic U.S.
residency, ordinary travel, deployments, and statements that proximity is only
"a plus." The matching description snippet is displayed with each flag.

Clearance analysis is also deterministic and conservative. It inspects only the
actual posting text and does not infer a clearance from the customer, agency, or
mission. The normalized cache fields separately record clearance level
(`noneMentioned`, `publicTrust`, `secret`, `topSecret`, `topSecretSCI`, or
`other`), requirement posture, whether a polygraph is explicitly required, a
short evidence excerpt, and parse status. More specific levels take precedence,
but a higher level found only under a preferred-qualifications heading does not
override a lower required level.

Result rows show a neutral informational clearance badge such as **Secret —
Active**, **Secret — Obtainable**, **TS/SCI**, or **Public Trust — Suitability**.
The selected-job metadata spells out the normalized level and requirement and
shows the source excerpt. Ambiguous generic security-clearance language is kept
as **Other / unclear level** rather than guessing. Derived clearance fields live
in `jobs-cache.json`; they are deliberately not written to job history and are
regenerated from each fresh Workday description.

The Security / Clearance Profile persists two independent values: current
national-security clearance (`None`, `Secret`, `Top Secret`, `TS/SCI`, or an
uncomparable `Other / Unknown`) and current Public Trust status. Public Trust is
not placed into the national-security hierarchy, and holding Secret or higher
never implies a Public Trust determination. Both profile axes default to **Not
configured**, which keeps every job visible.

The optional strict-clearance filter is off by default. It can hide a posting
only when the existing parser is confident, the requirement is explicitly
active/current/already-held/day-one (`activeRequired` or `mustPossess`), and the
configured profile does not satisfy it. The national-security comparison is
`None < Secret < Top Secret < TS/SCI`; Top Secret does not satisfy TS/SCI.
Obtain-and-maintain, obtain, eligibility, suitability, maintain, preferred, and
ambiguous wording never hides a job. A separate result-row mismatch badge and
the **At a Glance** Clearance card explain the comparison. The original **Full
Posting** remains free of derived analysis.

Polygraph detection remains separate from clearance level. A detected required
polygraph is surfaced in the job badge and Clearance card, but the current
profile does not claim a polygraph type or status. Even when the clearance level
matches, the UI therefore says the polygraph needs separate review; polygraph
alone does not trigger automatic hiding.

Credential analysis is similarly deterministic and informational. The
source-controlled `CredentialCatalog.json` defines each recognized credential's
canonical name, issuer, type, category, aliases, and any context-sensitive
matching patterns. The detector uses token-aware matching, recognizes surrounding
required/preferred/desired language, and records alternatives, equivalent
credentials, in-progress acceptance, post-hire acquisition, and a short evidence
excerpt. Short or overloaded acronyms use stricter catalog patterns rather than
plain substring matching.

Result rows show at most two credential badges followed by a compact count; the
selected-job pane shows every recognized credential and its evidence. Credentials
are not warnings and do not affect filtering. Derived matches and conservative
unrecognized-credential diagnostics are stored in `jobs-cache.json`, while the
catalog itself remains application configuration beside the executable. When the
catalog schema version changes, compatible cached descriptions are reanalyzed at
startup without contacting Workday. To recognize a future established credential,
verify it with its issuer, add one catalog entry and aliases, then increment the
catalog schema version.

Academic qualification analysis is a separate deterministic category. It
normalizes High School/GED, Associate, Bachelor's, Master's, and Doctorate levels;
tracks an explicitly named Ph.D. without treating every doctorate as a Ph.D.; and
preserves alternative degree/experience paths instead of flattening them into
misleading independent requirements. The analysis also records degree fields,
preferred levels, evidence excerpts, and whether equivalent or additional
experience may replace a degree. Short acronyms such as BA, BS, MA, and MS are
accepted only in degree-shaped context. Result rows use one compact academic badge,
while the selected-job pane shows the complete path structure in an **Academic
Qualifications** section that remains separate from **Credentials**.

Academic results are derived fields in `jobs-cache.json`. They are not stored in
`settings.json` or `job-history.json`. A version mismatch reanalyzes compatible
cached descriptions at startup without a Workday request; every successful live
refresh also regenerates the analysis from the current description.

The Education Profile defaults to **Not configured**, which never flags or hides
jobs. Once configured, it stores only completed education: no secondary credential,
GED, high school diploma, Associate, Bachelor's, Master's, or Doctorate, with an
optional Ph.D. subtype for a completed doctorate. GED and high school are kept as
distinct profile values but compare at the same secondary-school tier for general
requirements. A doctorate is the top hierarchy level; Ph.D. is a subtype, not a
higher level.

Education mismatch analysis is deliberately conservative. Automatic hiding can
occur only for confidently parsed `strictDegree` requirements above the completed
level. Preferred degrees, degree-or-experience language, multiple education and
experience paths, uncertain parsing, and unspecified education remain visible.
An explicit Ph.D. requirement is also left visible when the profile says only an
unspecified doctorate. The filter is off by default, composes with every existing
local filter, and changes the displayed result count without affecting NEW or
dismissal history.

The browser cannot call Workday directly: the CXS responses do not grant a local
origin access through CORS, and the JSON POST preflight is not supported. The
loopback ASP.NET Core backend is therefore the same-origin proxy and data
normalization layer.

## Build

Requires the .NET 10 SDK:

```powershell
dotnet build -c Release
```

## Source control

The project directory is a local Git repository. Major changes should begin
from a known-good commit and end with a tested checkpoint. Generated `bin`,
`obj`, IDE, log, publish, and application-local `data` content is excluded by
`.gitignore`; never add personal `settings.json`, cached descriptions, or job
history merely to make a source commit.

## Run

```powershell
dotnet run -c Release
```

Application URL: `http://127.0.0.1:54321`

Default port: `54321`

The fixed endpoint is configured in the application itself, so command-line,
Visual Studio, and published-executable launches all use the same URL. It binds
only to the IPv4 loopback adapter and is not exposed to the LAN. The program
opens the URL in the default browser. Keep the console window open while using
the viewer; press Ctrl+C to stop it. If TCP port 54321 is already occupied,
startup fails with a clear diagnostic and does not select another port.

To suppress browser launch for diagnostics or automation:

```powershell
dotnet run -c Release -- --Application:OpenBrowser=false
```

## Configuration

`appsettings.json` contains the Workday host, tenant/site, page size, request
timeout, and detail-request concurrency. The selected country/location IDs are
user state in `data/settings.json`, not hard-coded application configuration.
Workday currently caps the page size at 20.

Description HTML is sanitized with a strict element/attribute allowlist before
it enters the DOM. Workday inline styles and executable content are removed, and
description links are restricted to HTTP(S) and opened in a separate tab.
Highlighting never performs replacement against raw HTML. Each detail render
starts again from the sanitized description, walks text nodes, and wraps only
matching text in newly created `<mark>` elements. Tags, attributes, URLs, and
link behavior therefore remain unchanged, and repeated renders cannot accumulate
nested highlights.
