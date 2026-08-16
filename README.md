# Workday Job Manager

Workday Job Manager is a small frontend for selected public Workday job boards.
One codebase supports a portable personal local mode and an Azure App Service
mode with isolated anonymous browser workspaces. It began as a personal viewer
for Leidos' Workday careers site and was generalized without duplicating the
application.

The original Workday posting remains the authoritative source. The application
uses public Workday CXS JSON endpoints; it does not scrape rendered job pages.

## Why this exists

The default Workday search experience makes some personal screening workflows
awkward. This utility adds exact-date newest-first sorting, useful inclusion and
exclusion rules, persistent NEW/viewed tracking, a four-state job workflow, multi-location
selection, and salary, clearance, credential, education, and work-authorization
analysis. Remote-designated jobs are also checked for description language that
requires onsite, field, commuting-area, or substantial-travel work. It preserves
the rich Workday description and provides a direct link back to the employer's
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

The source-controlled [CompanyCatalog.json](CompanyCatalog.json) is the authority
for supported companies, hosts, tenants, sites, public URLs, default countries,
and known remote-location facet IDs. Adding another compatible employer should
primarily require adding and verifying one catalog entry.

Workday tenants can expose different facet structures and description conventions,
so the project does not claim universal compatibility with every Workday site.

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
dotnet build WorkdayJobManager.csproj -c Release
dotnet publish WorkdayJobManager.csproj -c Release
```

Run the dependency-free deterministic architecture/security checks with:

```powershell
dotnet run --project Tests\WorkdayJobManager.Tests.csproj -c Release
```

Run JavaScript syntax and centralized-theme source checks with:

```powershell
.\scripts\validate-source.ps1
```

The executable is `WorkdayJobManager.exe` in a Windows build or publish output.

## Source selection

Settings > Job Source presents Company first, followed by Country, remote coverage
when that employer exposes configured remote facets, and one or more physical
locations. Changing pending controls only refreshes facet choices. It does not crawl
jobs until **Apply Job Source** is selected.

A fresh or reset workspace starts on Settings > Job Source with no company selected,
United States of America preselected, and no applied source. It does not infer Leidos
or the first catalog company and does not retrieve jobs until a valid source is
explicitly applied. The unconfigured state is distinct from an unapplied edit to an
existing source.

The application remembers source choices separately for each company while keeping
the user profile and screening preferences global. Automatic checks monitor only
the currently applied source.

Settings > My Qualifications keeps U.S. work status and employer-sponsorship need
as separate profile facts. The optional strict work-authorization screening rule
is off by default and excludes only confidently incompatible requirements. U.S.
person, export-control, preferred, non-U.S., and uncertain language remains visible
for review. These values persist through the same local or anonymous-workspace
settings document as the other profile fields.

The active query identity includes company, country, remote coverage, and a
canonical set of location facet IDs. Location order therefore does not change cache
identity, and cached jobs from one company cannot appear under another company.

## Local persistent data

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
first-seen, last-seen, or hidden state. The current cache is also tagged with
its company-aware query identity.

Each job has exactly one persisted workflow state: Normal, Saved, Applied, or
Hidden. The results tabs show those mutually exclusive populations as All Jobs,
Saved, Applied, and Hidden. Restoring a Hidden job returns it to Normal; it does
not revive an earlier Saved or Applied state. State uses the same company-scoped
history identity and workspace storage, so it returns if that company/job identity
reappears in a later Workday snapshot.

State from a company removed from the supported catalog is handled conservatively:
unsupported saved source selections and caches are not reinterpreted as another
employer, while already company-scoped history remains isolated under its original
company ID. The active source falls back to a safe supported default.

Copy or move the executable directory and its `data` directory together to retain
state.

Settings > My Preferences also provides **Export Workspace** and **Import Workspace**.
The versioned JSON file is a portable workspace backup containing:

- the pending or applied Job Source selection (company, country, coverage flags,
  and selected physical Workday facet IDs/labels);
- search preferences, My Qualifications, screening choices, automatic-check
  preferences, and theme;
- one canonical record for each Saved, Applied, or Hidden job: company ID,
  company-scoped stable ID, requisition ID when available, workflow state, and
  Workday external path.

The stable identity and external path are the minimum metadata needed to preserve
curated work even when a job is absent from today's catalog and to reconcile its
state if the same company/job appears again. The backup deliberately excludes the
current job catalog, cached Workday results, full descriptions, ordinary Normal-job
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
Workday job crawl.

Settings also provides **Reset Current Workspace**. After confirmation, local mode
deletes only the three application-owned JSON documents shown above and reloads
first-run defaults. It does not recursively clean the `data` directory, so unrelated
files are left untouched.

## Azure App Service mode

Azure mode uses App Service's listener configuration instead of forcing the local
loopback port and never attempts to launch a browser on the server. Select it only
through explicit App Service application settings:

The current Azure deployment uses these existing resources:

- App Service: `workday-job-manager`
- Storage account: `workdayjobmanagerstore`
- Private Blob container: `userdata`

| Name | Value | Purpose |
| --- | --- | --- |
| `WORKDAYJOBMANAGER_HOSTING_MODE` | `Azure` | Selects Azure hosting, HTTPS behavior, anonymous workspaces, and Blob persistence. |
| `WORKDAYJOBMANAGER_STORAGE_ACCOUNT` | `workdayjobmanagerstore` | Supplies the non-secret Azure Storage account name used to derive the Blob service endpoint. |
| `WORKDAYJOBMANAGER_STORAGE_CONTAINER` | `userdata` | Selects the existing private workspace-state container. |

All three values are required in Azure mode. Missing or invalid configuration
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
  workspaces/{workspaceId}/
    settings.json
    jobs-cache.json
    job-history.json
```

Blob names are chosen from a fixed document set, and workspace IDs are generated
and validated server-side. ETag conditions prevent a stale request from silently
overwriting a newer Blob version. Storage errors are reported without creating a
different workspace or falling back to local files.

Anonymous identity has an intentional limitation: clearing the workspace cookie,
using another browser/profile, or moving to another device prevents the system
from recognizing the previous workspace. A new workspace may be created. There is
no recovery key or login mechanism, so **Export Workspace** is the supported way to
preserve portable settings and curated job states before losing browser storage.

New Azure workspaces start with neutral defaults. They do not inherit the local
user's salary, education, clearance, filters, locations, hidden jobs, or history.
The company and credential catalogs remain shared source-controlled application
configuration.

In Azure mode, **Reset Current Workspace** deletes only the three fixed Blob names
under the server-resolved current workspace prefix. The browser cannot supply a
workspace ID. Only after all deletions succeed does the server expire the protected
workspace cookie; the reload then creates a new anonymous workspace. A storage
failure preserves the cookie and reports an error instead of pretending the reset
succeeded.

### Automatic checks in Azure

Local mode retains its background automatic-check scheduler. Azure mode does not
assume the Free F1 process remains awake and does not run one global multi-user
timer. While a workspace is open, the browser polls check status and asks the
ASP.NET Core backend to perform a due check for that workspace. All Workday calls
remain server-side and are limited to companies in `CompanyCatalog.json`.

The Azure implementation is ready for review but this repository does not deploy
or configure Azure resources automatically.

## Architecture

The project intentionally remains a single small ASP.NET Core application:

- `CompanyCatalog.json` — supported Workday sites and source capabilities
- `CompanyCatalog.cs` — validated catalog loader
- `WorkdayClient.cs` — generic company-driven CXS listing/detail client
- `JobCatalog.cs` — active snapshot, cache, history, refresh, and automatic checks
- `AppStateStore.cs` — application-local JSON persistence and schema migration
- `JobAnalysis.cs` and detectors — shared salary, location, clearance, credential,
  academic, work-authorization, and remote-work credibility analysis
- `wwwroot/` — dependency-light HTML, JavaScript, and CSS UI

The hosting and persistence split is implemented by:

- `HostingConfiguration.cs` for explicit Local/Azure selection and validation
- `WorkspaceIdentity.cs` for protected anonymous workspace cookie resolution
- `WorkspaceRuntime.cs` for isolated catalog and automatic-check state per workspace
- `WorkspaceDataStore.cs` for the persistence contract and portable local files
- `AzureBlobWorkspaceDataStore.cs` for private Blob persistence with ETag-safe writes
- `AppStateStore.cs` for storage-independent JSON normalization and migration

`wwwroot/theme.css` is the single authority for application-owned colors,
typography, spacing, borders, radii, shadows, and state styling. Workday description
HTML is sanitized with the bundled DOMPurify library before rendering. Any inline
presentation originating in a posting is not treated as application theme styling.

## Workday behavior verified

All supported sites use:

- `POST /wday/cxs/{tenant}/{site}/jobs`
- JSON pagination with `limit` (maximum 20) and `offset`
- `appliedFacets` for country/location filters
- `GET /wday/cxs/{tenant}/{site}{externalPath}` for job detail
- exact `startDate`, rich HTML `jobDescription`, and authoritative `externalUrl`

Leidos and Boeing expose country and configured remote-location facets. MTM's
current public feed exposes location facets but no country facet and no explicit
remote-location facet. The UI adapts to those differences instead of applying one
employer's facet IDs to another employer.

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
Workday/storage mutations are rate-limited. Backend Workday requests can target
only validated entries in the shared company catalog; browser input cannot supply
an arbitrary host, tenant, or URL. Job descriptions are untrusted external HTML
and are sanitized before insertion into the page.

The Blob container must remain private. The browser never receives Blob URLs and
never talks directly to Azure Storage. No storage account keys, connection strings,
SAS tokens, passwords, or user-data payloads are committed or placed in cookies.

Parser results are screening aids, not authoritative statements about eligibility,
citizenship, immigration status, sponsorship, compensation, clearance, education,
or credentials. Always review the original Workday posting before acting.
