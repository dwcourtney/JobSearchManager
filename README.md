# Workday Job Manager

Workday Job Manager is a small, local frontend for selected public Workday job
boards. It began as a personal viewer for Leidos' Workday careers site and was
generalized to support multiple employers without duplicating the application.

The original Workday posting remains the authoritative source. The application
uses public Workday CXS JSON endpoints; it does not scrape rendered job pages.

## Why this exists

The default Workday search experience makes some personal screening workflows
awkward. This utility adds exact-date newest-first sorting, useful inclusion and
exclusion rules, persistent NEW/viewed tracking, per-job dismissal, multi-location
selection, and salary, clearance, credential, and education analysis. It preserves
the rich Workday description and provides a direct link back to the employer's
posting.

This is a personal, loopback-only utility. It has no authentication, cloud host,
database, browser automation, or multi-user architecture.

## Supported employers

- Leidos (`leidos.wd5.myworkdayjobs.com`, tenant `leidos`, site `External`)
- MTM (`mtminc.wd1.myworkdayjobs.com`, tenant `mtminc`, site
  `MTMTransit_External`)

The source-controlled [CompanyCatalog.json](CompanyCatalog.json) is the authority
for supported companies, hosts, tenants, sites, public URLs, default countries,
and known remote-location facet IDs. Adding another compatible employer should
primarily require adding and verifying one catalog entry.

Workday tenants can expose different facet structures and description conventions,
so the project does not claim universal compatibility with every Workday site.

## Run

Requirements: Windows and the .NET 10 SDK or runtime.

```powershell
dotnet run -c Release
```

Application URL: `http://127.0.0.1:54321`

Default port: `54321`

The application always binds only to IPv4 loopback. If port 54321 is occupied,
startup fails clearly rather than selecting a different port.

Build or publish with:

```powershell
dotnet build WorkdayJobManager.csproj -c Release
dotnet publish WorkdayJobManager.csproj -c Release
```

The executable is `WorkdayJobManager.exe` in a Windows build or publish output.

## Source selection

Settings > Job Search presents Company first, followed by Country, remote coverage
when that employer exposes configured remote facets, and one or more physical
locations. Changing pending controls only refreshes facet choices. It does not crawl
jobs until **Apply job source** is selected.

The application remembers source choices separately for each company while keeping
the user profile and screening preferences global. Automatic checks monitor only
the currently applied source.

The active query identity includes company, country, remote coverage, and a
canonical set of location facet IDs. Location order therefore does not change cache
identity, and cached jobs from one company cannot appear under another company.

## Persistent data

All persistent state is stored beside the running application in:

```text
<AppContext.BaseDirectory>\data\
  settings.json
  jobs-cache.json
  job-history.json
```

Nothing is stored in AppData, LocalAppData, Temp, the registry, or another hidden
per-user location. The program does not silently fall back elsewhere. If the
application directory is not writable, it reports a clear startup/save error.

Job history and stable job IDs are company-scoped. Existing pre-generalization
history is migrated to the `leidos` company without resetting NEW, viewed,
first-seen, last-seen, or dismissed state. The current cache is also tagged with
its company-aware query identity.

Copy or move the executable directory and its `data` directory together to retain
state.

## Architecture

The project intentionally remains a single small ASP.NET Core application:

- `CompanyCatalog.json` — supported Workday sites and source capabilities
- `CompanyCatalog.cs` — validated catalog loader
- `WorkdayClient.cs` — generic company-driven CXS listing/detail client
- `JobCatalog.cs` — active snapshot, cache, history, refresh, and automatic checks
- `AppStateStore.cs` — application-local JSON persistence and schema migration
- `JobAnalysis.cs` and detectors — shared salary, location, clearance, credential,
  and academic analysis
- `wwwroot/` — dependency-light HTML, JavaScript, and CSS UI

`wwwroot/theme.css` is the single authority for application-owned colors,
typography, spacing, borders, radii, shadows, and state styling. Workday description
HTML is sanitized with the bundled DOMPurify library before rendering. Any inline
presentation originating in a posting is not treated as application theme styling.

## Workday behavior verified

Both supported sites use:

- `POST /wday/cxs/{tenant}/{site}/jobs`
- JSON pagination with `limit` (maximum 20) and `offset`
- `appliedFacets` for country/location filters
- `GET /wday/cxs/{tenant}/{site}{externalPath}` for job detail
- exact `startDate`, rich HTML `jobDescription`, and authoritative `externalUrl`

Leidos exposes country and configured Remote/Teleworker location facets. MTM's
current public feed exposes location facets but no country facet and no explicit
remote-location facet. The UI adapts to those differences instead of applying
Leidos facet IDs to MTM.

## Safety and scope

The server listens only on `127.0.0.1`. Responses use a restrictive Content
Security Policy, no-store API caching, content-type protection, frame denial, and
no-referrer behavior. Job descriptions are untrusted external HTML and are
sanitized before insertion into the page.

Parser results are screening aids, not authoritative statements about eligibility,
compensation, clearance, education, or credentials. Always review the original
Workday posting before acting.
