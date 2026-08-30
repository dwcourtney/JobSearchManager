# Job Search Manager

Job Search Manager is a small frontend for selected public job boards.
One codebase supports a portable personal local mode and an Azure App Service
mode with isolated anonymous browser workspaces. It began as a personal viewer
for Leidos' careers site and was generalized without duplicating the
application.

The original employer posting remains the authoritative source. The application
uses public Workday CXS and SmartRecruiters Posting API JSON endpoints; it does
not scrape rendered job pages.

## Why this exists

The default job-board search experience makes some personal screening workflows
awkward. This utility adds exact-date newest-first sorting, useful inclusion and
exclusion rules, persistent NEW/viewed tracking, a four-state job workflow, multi-location
selection, and salary, clearance, credential, education, and work-authorization
analysis. Remote-designated jobs are also checked for description language that
requires onsite, field, commuting-area, or substantial-travel work. It preserves
the rich job description and provides a direct link back to the employer's
posting.

The application has no login or account system and uses no browser automation.
Local mode remains loopback-only. Azure mode identifies an anonymous workspace
with a protected browser cookie and stores that workspace's durable JSON state in
a private Azure Blob container.

## Supported employers

- Leidos (`leidos.wd5.myworkdayjobs.com`, tenant `leidos`, site `External`)
- MTM (`mtminc.wd1.myworkdayjobs.com`, tenant `mtminc`, site
  `MTMTransit_External`)
- Boeing (`boeing.wd1.myworkdayjobs.com`, tenant `boeing`, site
  `EXTERNAL_CAREERS`)
- Northrop Grumman (`ngc.wd1.myworkdayjobs.com`, tenant `ngc`, site
  `Northrop_Grumman_External_Site`)
- NVIDIA (`nvidia.wd5.myworkdayjobs.com`, tenant `nvidia`, site
  `NVIDIAExternalCareerSite`)
- Parsons (`parsons.wd5.myworkdayjobs.com`, tenant `parsons`, site `Search`)
- AECOM (SmartRecruiters company `AECOM2`)
- RTX (`globalhr.wd5.myworkdayjobs.com`, tenant `globalhr`, site
  `REC_RTX_Ext_Gateway`)
- Amentum (`pae.wd1.myworkdayjobs.com`, tenant `pae`, site `Amentum_Careers`)
- KBR (`kbr.wd5.myworkdayjobs.com`, tenant `kbr`, site `KBR_Careers`)
- Booz Allen Hamilton (`bah.wd1.myworkdayjobs.com`, tenant `bah`, site `BAH_Jobs`)
- ServiceNow (SmartRecruiters company `ServiceNow`)
- NXP Semiconductors (`nxp.wd3.myworkdayjobs.com`, tenant `nxp`, site `careers`)

The source-controlled [CompanyCatalog.json](CompanyCatalog.json) is the authority
for supported companies, providers, hosts, tenants/company keys, sites, public
URLs, default countries, provider-specific country facets, and known remote-location
facet IDs. It also assigns each employer a broad industry category. The native
Company selector renders those categories as groups without duplicating industry
rules in the browser. Adding another compatible employer should primarily require
adding and verifying one catalog entry; a new provider requires one reusable adapter.

Provider sites can expose different facet structures and description conventions,
so the project does not claim universal compatibility with every public career site.

Future company additions must be verified for both Workday compatibility and the
intended geographic/business scope. For a multinational with multiple career
systems or regional boards, verify the U.S. or general external board first unless
a different region is explicitly requested.

## Local mode

Requirements: Windows and the .NET 10 SDK or runtime.

```powershell
dotnet run -c Release
```

Application URL: `http://127.0.0.1:54321`

Default port: `54321`

Local is the default hosting mode and requires no Azure credentials or settings.
The application always binds only to IPv4 loopback. If port 54321 is occupied,
startup fails clearly rather than selecting a different port. It opens the
default browser after startup.

Build or publish with:

```powershell
dotnet build JobSearchManager.csproj -c Release
dotnet publish JobSearchManager.csproj -c Release
```

## Linux container mode

Container mode keeps Local mode's filesystem persistence while using browser-isolated
anonymous workspaces and normal Kestrel URL configuration. It never uses Azure Blob
Storage, launches a browser, or enables Azure-only HTTPS redirection and HSTS behavior.

Required container settings:

| Name | Example | Purpose |
| --- | --- | --- |
| `JOBSEARCHMANAGER_HOSTING_MODE` | `Container` | Selects Linux container hosting with local filesystem persistence. |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` | Makes Kestrel reachable through Docker port publishing. |
| `JOBSEARCHMANAGER_DATA_PROTECTION_PATH` | `/var/lib/jsm/dataprotection` | Persists cookie-protection keys across container replacement. |
| `JOBSEARCHMANAGER_PUBLIC_BASE_URL` | `http://192.168.1.20:8080` | Supplies the origin used in optional account links. |

The included multi-stage `Dockerfile` builds with the .NET 10 SDK and runs on the
ASP.NET Core 10 runtime as non-root UID/GID 1001. The included `compose.yaml` publishes
only `192.168.1.20:8080`, uses no named volumes, and bind-mounts application data and
Data Protection keys under `./data`. `GET /healthz` is an unauthenticated process-health
probe and performs no workspace, account, provider, or storage operation. `GET /version`
reports only the build commit, application version, and hosting mode so a deployment can
prove that the running image is the exact commit selected by CI.

Start the isolated project with:

```bash
docker compose -p jsm-lab up -d --build
```

Stop it while preserving bind-mounted state with:

```bash
docker compose -p jsm-lab down
```

Run the dependency-free deterministic architecture/security checks with:

```powershell
dotnet run --project Tests\JobSearchManager.Tests.csproj -c Release
```

Run JavaScript syntax and centralized-theme source checks with:

```powershell
.\scripts\validate-source.ps1
```

The repository pins .NET SDK 10.0.400 in `global.json` and commits NuGet lock files.
Automation and release-operation details are documented in
[`docs/curiosity-cicd.md`](docs/curiosity-cicd.md).

The executable is `JobSearchManager.exe` in a Windows build or publish output.

## Source selection

Settings > Job Source presents Company first, followed by Country, remote coverage
when that employer exposes configured remote facets, and one or more physical
locations. Changing pending controls only refreshes facet choices. It does not crawl
jobs until **Apply Job Source** is selected.

A fresh or reset workspace starts on Settings > Job Source with no company selected,
United States of America preselected, and no applied source. It does not infer Leidos
or the first catalog company and does not retrieve jobs until a valid source is
explicitly applied. Choosing Jobs explains that a job source is required and returns
the user to Job Source; it is never a silent, disabled navigation action. The
unconfigured state is distinct from an unapplied edit to an existing source.

The application remembers source choices separately for each company while keeping
the user profile and screening preferences global.

Settings > My Qualifications keeps U.S. work status and employer-sponsorship need
as separate profile facts. The optional strict work-authorization screening rule
is off by default and excludes only confidently incompatible requirements. U.S.
person, export-control, preferred, non-U.S., and uncertain language remains visible
for review. These values persist through the same local or anonymous-workspace
settings document as the other profile fields.

Settings > My Preferences also offers **Exclude deployment / remote-assignment
jobs**. It is off by default and hides only Strong extended-location detections:
explicit deployment, rotation, relocation, or extended-presence obligations at
unusual remote or overseas locations. Questionable detections remain visible, as
do ordinary business travel and ordinary onsite geography. Every Strong or
Questionable detection is shown in a dedicated **Deployment / Location
Requirement** section in At a Glance with the preserved posting evidence.

Settings > Job Fit provides optional workspace-specific suitability scoring. It is
disabled by default for new and existing workspaces. When enabled, users rate every
canonical concept from `JobConceptCatalog.json` as Hard Conflict, Negative,
Neutral, Positive, or Ideal; arbitrary Job Fit keywords cannot be entered. Ordinary
business travel is configured separately at the top of Work Arrangement with one
native seven-level **Travel Tolerance** control, from No travel (0) through
Travel-heavy (6). The four travel-band concepts remain internal detectors and no
longer appear as independent preference rows. A detected requirement at or below the
selected maximum is Neutral, one level above is Negative, and two or more levels
above is a Hard Conflict. Immediately below it, **Normal Work Location** records the
user's ideal on a six-level scale: 100% Remote (0), Remote with rare office visits
(1), Mostly remote (2), Hybrid (3), Mostly onsite (4), and Fully onsite (5). Its four
legacy location concepts remain internal corpus detectors rather than independent
preference rows. Generic Remote Work maps conservatively to Mostly remote when no
cadence is stated. Explicit future requirements and weekly onsite cadence outrank
generic designations; rare visits, mostly-remote/onsite language, explicit fully
remote/onsite wording, and canonical signals then follow in that order. Same-priority
conflicts choose the more onsite required arrangement. Location contributes by
absolute distance from the ideal: `0 => +1`, `1 => 0`, `2 => -1`, `3 => -2`,
`4 => -3`, and `5 => -4`; it never creates a Hard Conflict by itself and remains
subject to the existing Work Arrangement bound. Deployment, relocation, rotations,
extended-away, and international-assignment preferences remain independent and are
grouped as overlapping **Assignment / Location Constraints**. Concept evidence
is detected during normal ingestion or cache reclassification and stored with each
job. The versioned catalog currently provides 79 job-level concepts grouped as Work
Arrangement, Role Type / Career Direction, Technical Domain, Work Environment, and
Responsibility Shape. Its 71 user-configurable concepts appear in a searchable radio
matrix organized into collapsible category sections. Neutral is the default and is omitted from sparse
workspace settings. The retired `strongNegative` and `strongPositive` values import
as Negative and Ideal, respectively, so older workspaces and exports remain compatible
while new saves and exports use the current terminology. Legacy travel-row preferences
are deterministically migrated to one tolerance value. For a legacy travel level
`L`, Hard Conflict maps to `L-2`, Negative maps to `L-1`, and the most restrictive
result wins; Positive or Ideal preserves at least that level when no restrictive
travel preference exists. The former Frequent and Substantial Travel hard conflicts
used by the existing workspace therefore become level 3 (Occasional). An already
persisted valid `0..6` value takes precedence. New, neutral, invalid, or otherwise
unspecified workspaces default to level 4 (Moderate), which avoids unexpectedly
rejecting the common 10–25% band. Preferred normal work location persists as an
integer `0..5`; a valid explicit value wins. Legacy Ideal and Positive location rows
pull toward their detector levels while Negative and Hard Conflict rows push away,
with stronger preferences weighted more heavily. The specific 100% Remote signal
supersedes generic Remote during migration. Ties prefer the level nearest the neutral
Hybrid default, then the lower level. Thus the existing workspace's 100% Remote Ideal,
Remote Ideal, Hybrid Negative, and Onsite Negative settings migrate to level 0 without
double-counting the generic Remote row. Unconfigured workspaces default to Hybrid (3),
the center of the corpus scale, avoiding an assumed remote-only or onsite-only bias.

Cards show a themed 1–10 badge. Scoring starts at 5 and bounds each category's total
contribution, so aligned normal-location evidence can improve Work Arrangement by at most one point,
and several related AI or infrastructure technologies cannot stack without limit.
Internal location and travel detectors each produce one comparison signal, so related location or travel
phrases cannot stack. Role and environment
negatives have wider bounds than positives, and a Hard Conflict still caps the final
score at 2. The tooltip reports each category's actual bounded contribution, any
uncapped total, and its detected evidence. Profile-aware degree, experience,
clearance, license, and credential mismatches remain in Qualification Fit rather than
being duplicated as description-only Job Fit concepts. Job Fit does not change
filtering, sorting, or qualification behavior.

When Job Fit is enabled, each job detail includes a Job Fit tab between At a Glance
and Full Posting. It renders the scoring engine's structured result: the final score,
raw and bounded category contributions, contributing and Neutral detections, evidence,
superseded concepts, Hard Conflict cap behavior, and the final arithmetic. The tab is
hidden when Job Fit is disabled and does not independently recalculate the score.

## Optional accounts

Job Search Manager continues to support anonymous workspaces without sign-in. The
Account settings tab can optionally claim the current workspace for an email/password
account without copying or replacing its settings, qualifications, Job Fit preferences,
or curated history. Once claimed, the old anonymous workspace cookie no longer grants
access; the authenticated account is the authoritative owner.

Passwords are processed only by the server and stored with ASP.NET Core's versioned
`PasswordHasher<T>` format. Account records, hashed one-time verification/reset tokens,
and workspace ownership live in a separate private authentication registry and are never
included in portable workspace exports. Authentication uses HttpOnly SameSite cookies;
the UI offers session-only, 1-, 7-, 14-, 30-, and renewable 180-day persistence choices.

Email delivery is optional deployment configuration. If it is not configured, accounts
and sign-in still work, while the UI reports that verification and password-recovery
messages cannot be delivered. SMTP secrets must be supplied through secure environment
or App Service settings, never source files:

- `JOBSEARCHMANAGER_PUBLIC_BASE_URL` — public HTTPS origin used in account links
- `JOBSEARCHMANAGER_SMTP_HOST`
- `JOBSEARCHMANAGER_SMTP_PORT` — defaults to `587`
- `JOBSEARCHMANAGER_SMTP_ENABLE_SSL` — defaults to `true`
- `JOBSEARCHMANAGER_SMTP_USERNAME` and `JOBSEARCHMANAGER_SMTP_PASSWORD` when required
- `JOBSEARCHMANAGER_EMAIL_FROM` — verified sender address

Verification and reset secrets are placed in URL fragments so they are handled by the
client and are not sent in the initial HTTP request or server request logs.

The active query identity includes company, country, remote coverage, and a
canonical set of location facet IDs. Location order therefore does not change cache
identity, and cached jobs from one company cannot appear under another company.

## Efficient refresh and detail caching

Refresh is summary-first. The provider listing/index is retrieved before any job
detail, and stable company-plus-job identity and a listing fingerprint are compared
with the server-side cache. Unchanged descriptions and their derived qualification
analysis are reused. A changed listing, a missing detail, or an explicit cache
incompatibility schedules detail work; providers without a reliable modified marker
are covered by a seven-day, bounded revalidation policy rather than a complete crawl.

Manual refreshes retrieve at most 200 details, with at most 25 age-based
revalidations. Detail requests use configured bounded concurrency. Listing retrieval is
also page-bounded and reports truncation instead of silently issuing unbounded
requests. Deferred jobs remain visible with an Analysis pending badge and are
hydrated in later manual batches or immediately when opened.

The normal jobs API returns compact list records without full descriptions or
large evidence collections. Selecting a job requests that one detail from the
server-side cache, fetching it from the provider only when absent. Description-scope
keyword matching runs against cached text on the server and returns only matching
stable IDs. Refresh-progress polling uses a status-only endpoint and transfers the
complete job list once, when a detached refresh completes. Refresh-progress polls
do not overlap and stop when the tab is hidden.

Cached descriptions are compressed at rest. Parser-version changes re-run analysis
against inflated cached HTML locally and do not cause provider downloads. Cache
schema 6 stores one compact document per company and canonical query fingerprint.
An unchanged refresh writes neither cache nor history. Manual refreshes update
only the small source-status document so the last successful
provider pass remains accurate across process restarts.

## Local persistent data

All persistent state is stored beside the running application in:

```text
<AppContext.BaseDirectory>\data\
  settings.json
  job-history.json
  shared\job-caches\{companyId}\{queryFingerprint}.json
  shared\source-status\{companyId}\{queryFingerprint}.json
```

Nothing is stored in AppData, LocalAppData, Temp, the registry, or another hidden
per-user location. The program does not silently fall back elsewhere. If the
application directory is not writable, it reports a clear startup/save error.

Job history and stable job IDs are company-scoped. Existing pre-generalization
history is migrated to the `leidos` company without resetting NEW, viewed,
first-seen, last-seen, or hidden state. A legacy single-source or cumulative
`jobs-cache.json` envelope is split into company/query documents idempotently and
removed only after every supported entry has migrated safely.

Each job has exactly one persisted workflow state: Normal, Saved, Applied, Closed, or
Hidden. The results tabs show those mutually exclusive populations as All Jobs,
Saved, Applied, Closed, and Hidden. Restoring a Hidden job returns it to Normal; it does
not revive an earlier Saved or Applied state. State uses the same company-scoped
history identity and workspace storage, so it returns if that company/job identity
reappears in a later provider snapshot.

State from a company removed from the supported catalog is handled conservatively:
unsupported saved source selections and caches are not reinterpreted as another
employer, while already company-scoped history remains isolated under its original
company ID. The active source falls back to a safe supported default.

Copy or move the executable directory and its `data` directory together to retain
state.

Settings > Account also provides **Export Workspace** and **Import Workspace**.
The versioned JSON file is a portable workspace backup containing:

- the pending or applied Job Source selection (company, country, coverage flags,
  and selected physical provider facet IDs/labels);
- search preferences, My Qualifications, screening choices, optional Job Fit
  configuration, and theme;
- one canonical record for each Saved, Applied, Closed, or Hidden job: company ID,
  company-scoped stable ID, requisition ID when available, workflow state, and
  provider external path.

The stable identity and external path are the minimum metadata needed to preserve
curated work even when a job is absent from today's catalog and to reconcile its
state if the same company/job appears again. The backup deliberately excludes the
current job catalog, cached provider results, full descriptions, ordinary Normal-job
history, workspace/cookie IDs, Blob identifiers, secrets, parser caches, and
temporary refresh/UI state.

Import validates the complete file before changing durable state. An unsupported
Job Source is rejected; an otherwise valid curated record for a no-longer-supported
company remains isolated under its exact company ID and is never remapped. Mismatched
stable IDs, duplicate job identities, unknown workflow states, and malformed
preference values are rejected.
On success, imported portable preferences and curated states replace their current
counterparts; ordinary viewed/NEW history remains intact. An imported Job Source is
staged for review and requires **Apply Job Source**, so import itself never starts a
job-source crawl. If the imported source is already equivalent to the applied source,
it remains clean and needs no redundant apply. Otherwise, choosing Jobs uses the same
unapplied-source guard as a manual edit and offers **Apply and go to Jobs** when the
imported source is valid.

Settings also provides **Reset Current Workspace**. After confirmation, local mode
deletes only `settings.json` and `job-history.json` and reloads first-run defaults.
Shared employer cache/status data and unrelated files are left untouched.

## Azure App Service mode

Azure mode uses App Service's listener configuration instead of forcing the local
loopback port and never attempts to launch a browser on the server. Select it only
through explicit App Service application settings:

The current Azure deployment retains these pre-rebrand infrastructure resource
names so the rebrand does not replace or migrate live Azure resources:

- App Service: `workday-job-manager`
- Storage account: `workdayjobmanagerstore`
- Private Blob container: `userdata`

| Name | Value | Purpose |
| --- | --- | --- |
| `JOBSEARCHMANAGER_HOSTING_MODE` | `Azure` | Selects Azure hosting, HTTPS behavior, anonymous workspaces, and Blob persistence. |
| `JOBSEARCHMANAGER_STORAGE_ACCOUNT` | `workdayjobmanagerstore` | Supplies the non-secret Azure Storage account name used to derive the Blob service endpoint. |
| `JOBSEARCHMANAGER_STORAGE_CONTAINER` | `userdata` | Selects the existing private workspace-state container. |

Deployments configured with the previous product-owned setting names remain readable
as migration aliases; canonical settings take precedence and all new configuration
uses the names above. All three values are required in Azure mode. Missing or invalid configuration
fails startup clearly; Azure mode never falls back to local disk. Do not add a
storage connection string, account key, or SAS token. `DefaultAzureCredential`
uses the App Service's system-assigned managed identity, which needs Storage Blob
Data Contributor access to the private `userdata` container.

On a browser's first Azure visit, the server creates a cryptographically random
256-bit workspace ID. An ASP.NET Core Data Protection payload containing that ID
is stored in a long-lived `HttpOnly`, `Secure`, `SameSite=Lax` cookie. The cookie
contains no settings, history, job data, storage credentials, or personally
entered profile values. Blob access remains entirely server-side.

Azure App Service's standard ASP.NET Core behavior persists Data Protection keys
under its network-backed `%HOME%\ASP.NET\DataProtection-Keys` directory so cookies
survive process recycle and scale-out within one deployment slot. Deployment slots
do not share that key ring; a future slot-swap deployment would require an explicit
shared key-ring design. See Microsoft's
[App Service Data Protection guidance](https://learn.microsoft.com/aspnet/core/host-and-deploy/azure-apps#data-protection-key-ring-and-deployment-slots).

Each workspace is isolated under:

```text
userdata (private container)
  shared/
    job-caches/{companyId}/{queryFingerprint}.json
    source-status/{companyId}/{queryFingerprint}.json
  workspaces/{workspaceId}/
    settings.json
    job-history.json
```

Employer listings, full details, and refresh status are canonical shared source data.
Identical normalized source configurations resolve to the same SHA-256 query
fingerprint regardless of Workspace ID; genuinely different company/location queries
remain separate. Settings and curated/viewed workflow history remain isolated under
the workspace prefix. ETag conditions prevent stale writes, and source-keyed
single-flight coordination prevents simultaneous workspaces from duplicating a
provider refresh within the running service.

Anonymous identity has an intentional limitation: clearing the workspace cookie,
using another browser/profile, or moving to another device prevents the system
from recognizing the previous workspace. A new workspace may be created. There is
no recovery key or login mechanism, so **Export Workspace** is the supported way to
preserve portable settings and curated job states before losing browser storage.

New Azure workspaces start with neutral defaults. They do not inherit the local
user's salary, education, clearance, filters, locations, hidden jobs, or history.
The company and credential catalogs remain shared source-controlled application
configuration.

In Azure mode, **Reset Current Workspace** deletes only `settings.json` and
`job-history.json` under the server-resolved current workspace prefix. It never
deletes shared employer caches. The browser cannot supply a
workspace ID. Only after all deletions succeed does the server expire the protected
workspace cookie; the reload then creates a new anonymous workspace. A storage
failure preserves the cookie and reports an error instead of pretending the reset
succeeded.

Job data is refreshed when the user explicitly selects **Refresh**, when a source is
applied without a sufficiently fresh shared cache, or through the existing local
startup-refresh option. Azure does not advertise or run periodic background checks.
All provider calls remain server-side and are limited to companies in
`CompanyCatalog.json`.

The Azure implementation is ready for review but this repository does not deploy
or configure Azure resources automatically.

## Architecture

The project intentionally remains a single small ASP.NET Core application:

- `CompanyCatalog.json` — supported job sites and source capabilities
- `CompanyCatalog.cs` — validated catalog loader
- `JobSourceClient.cs` — generic company-driven CXS listing/detail client
- `JobCatalog.cs` — active snapshot, cache, history, and refresh coordination
- `AppStateStore.cs` — application-local JSON persistence and schema migration
- `JobAnalysis.cs` and detectors — shared salary, location, clearance, credential,
  academic, work-authorization, remote-work credibility, and extended-location
  obligation analysis
- `JobConceptCatalog.json`, `JobConceptCatalog.cs`, and `JobConceptDetector.cs` —
  stable canonical concept IDs, corpus evidence mappings, and persisted detections
- `wwwroot/` — dependency-light HTML, JavaScript, and CSS UI

The hosting and persistence split is implemented by:

- `HostingConfiguration.cs` for explicit Local/Azure selection and validation
- `WorkspaceIdentity.cs` for protected anonymous workspace cookie resolution
- `WorkspaceRuntime.cs` for isolated catalog state per workspace
- `WorkspaceDataStore.cs` for the persistence contract and portable local files
- `AzureBlobWorkspaceDataStore.cs` for private Blob persistence with ETag-safe writes
- `AppStateStore.cs` for storage-independent JSON normalization and migration

`wwwroot/theme.css` is the single authority for application-owned colors,
typography, spacing, borders, radii, shadows, and state styling. Job-description
HTML is sanitized with the bundled DOMPurify library before rendering. Any inline
presentation originating in a posting is not treated as application theme styling.

## Current source protocols verified

Leidos, MTM, Boeing, Northrop Grumman, NVIDIA, Parsons, RTX, Amentum, KBR,
Booz Allen Hamilton, and NXP Semiconductors use the Workday CXS protocol:

- `POST /wday/cxs/{tenant}/{site}/jobs`
- JSON pagination with `limit` (maximum 20) and `offset`
- `appliedFacets` for country/location filters
- `GET /wday/cxs/{tenant}/{site}{externalPath}` for job detail
- exact `startDate`, rich HTML `jobDescription`, and authoritative `externalUrl`

AECOM and ServiceNow use the official public SmartRecruiters Posting API:

- paged `GET /v1/companies/{companyKey}/postings` listing metadata
- server-side country filtering plus stable remote/hybrid and location fields
- `GET /v1/companies/{companyKey}/postings/{id}` for structured job-ad sections and
  canonical posting/apply URLs

The shared SmartRecruiters adapter deliberately excludes generic company-description boilerplate
from job analysis, while retaining job description, qualifications, and additional
information where requirements, compensation, travel, and restrictions appear.

Leidos, Boeing, NVIDIA, Parsons, KBR, and NXP expose usable country or regional
facets. NVIDIA and KBR name their hierarchy differently, while NXP exposes its
country facet at the top level instead of beneath Workday's location group. Those
differences are handled through catalog metadata and generic facet normalization.
MTM, Northrop Grumman, RTX, Amentum, and Booz Allen currently expose usable
location facets without a country facet. The UI adapts to those differences instead
of applying one employer's facet IDs to another employer.

Boeing's public source defaults to the United States and supports country-wide,
physical-location, and configured Remote location selection. Its recurring ABET
language is retained as an academic accreditation attribute, separate from the
professional credential catalog. Export-control “U.S. Person” wording remains a
review-only authorization signal rather than being inferred as citizen-only.

## Safety and scope

Local mode listens only on `127.0.0.1`. Azure mode honors App Service hosting and
forwarded HTTPS information. Responses use a restrictive Content Security Policy,
no-store API caching, content-type protection, frame denial, and no-referrer
behavior. Azure state-changing requests require a same-origin `Origin`, and
Provider/storage mutations are rate-limited. Backend provider requests can target
only validated entries in the shared company catalog; browser input cannot supply
an arbitrary host, tenant, or URL. Job descriptions are untrusted external HTML
and are sanitized before insertion into the page.

The Blob container must remain private. The browser never receives Blob URLs and
never talks directly to Azure Storage. No storage account keys, connection strings,
SAS tokens, passwords, or user-data payloads are committed or placed in cookies.

Parser results are screening aids, not authoritative statements about eligibility,
citizenship, immigration status, sponsorship, compensation, clearance, education,
or credentials. Always review the original employer posting before acting.
