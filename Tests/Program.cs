using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using JobSearchManager;

if (args.Length >= 2 && args[0] == "--remote-corpus")
{
    var detector = new RemoteWorkDetector();
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var results = jobs.Select(job => new
        {
            Company = job.TryGetProperty("company", out var company) ? company.GetString() ?? "unknown" : "unknown",
            Requisition = job.TryGetProperty("requisitionId", out var requisition) ? requisition.GetString() ?? "" : "",
            Title = job.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            Analysis = detector.Analyze(
                job.TryGetProperty("title", out title) ? title.GetString() ?? "" : "",
                job.TryGetProperty("location", out var location) ? location.GetString() ?? "" : "",
                job.TryGetProperty("additionalLocations", out var additional) &&
                    additional.ValueKind == JsonValueKind.Array
                        ? additional.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
                        : [],
                job.TryGetProperty("descriptionHtml", out var description) ? description.GetString() ?? "" : "")
        }).ToArray();

        Console.WriteLine($"REMOTE CORPUS {Path.GetFileName(path)} jobs={jobs.Length}");
        foreach (var company in results.GroupBy(result => result.Company).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {company.Key}: total={company.Count()}, " +
                $"strong={company.Count(item => item.Analysis.ConcernLevel == "strong")}, " +
                $"questionable={company.Count(item => item.Analysis.ConcernLevel == "questionable")}, " +
                $"none={company.Count(item => item.Analysis.ConcernLevel == "none")}");
            foreach (var item in company.Where(item => item.Analysis.ConcernLevel != "none"))
            {
                Console.WriteLine($"    {item.Analysis.ConcernLevel} {item.Requisition} {item.Title}: {item.Analysis.Summary}");
                foreach (var signal in item.Analysis.Signals)
                {
                    Console.WriteLine($"      {signal.Category}: {signal.Evidence}");
                }
            }
        }
    }
    return;
}

if (args.Length >= 2 && args[0] == "--authorization-corpus")
{
    var detector = new WorkAuthorizationDetector();
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var analyses = jobs.Select(job => new
        {
            Requisition = job.TryGetProperty("requisitionId", out var requisition) ? requisition.GetString() : "",
            Analysis = detector.Analyze(job.TryGetProperty("descriptionHtml", out var description)
                ? description.GetString() ?? ""
                : "")
        }).ToArray();
        Console.WriteLine($"CORPUS {Path.GetFileName(path)} jobs={jobs.Length}");
        foreach (var group in analyses
            .Where(item => item.Analysis.Eligibility != "noneSpecified" || item.Analysis.Sponsorship != "noneSpecified")
            .GroupBy(item => new { item.Analysis.Eligibility, item.Analysis.Sponsorship, item.Analysis.Strength })
            .OrderByDescending(group => group.Count()))
        {
            Console.WriteLine($"  {group.Key.Eligibility} | {group.Key.Sponsorship} | {group.Key.Strength}: {group.Count()}");
            foreach (var sample in group.Take(3))
            {
                Console.WriteLine($"    {sample.Requisition}: {sample.Analysis.Evidence.FirstOrDefault()}");
            }
        }

        var descriptions = jobs.Select(job => job.TryGetProperty("descriptionHtml", out var description)
            ? description.GetString() ?? ""
            : "").ToArray();
        var clearanceAnalyses = descriptions.Select(JobAnalysis.AnalyzeClearance).ToArray();
        Console.WriteLine("CLEARANCE");
        foreach (var group in clearanceAnalyses
            .GroupBy(item => new { item.Level, item.Requirement })
            .OrderByDescending(group => group.Count()))
        {
            Console.WriteLine($"  {group.Key.Level} | {group.Key.Requirement}: {group.Count()}");
        }
        var explicitNoClearance = descriptions
            .Select((description, index) => new { description, analysis = clearanceAnalyses[index] })
            .Where(item => item.description.Contains(
                "does not require a Security Clearance", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Console.WriteLine($"  explicit-no-clearance={explicitNoClearance.Length}; " +
            $"none-mentioned={explicitNoClearance.Count(item => item.analysis.Level == "noneMentioned")}");

        var academicDetector = new AcademicQualificationDetector();
        var academicAnalyses = descriptions.Select(academicDetector.Analyze).ToArray();
        Console.WriteLine($"ACADEMIC ABET={academicAnalyses.Count(item =>
            item.Accreditations?.Any(accreditation => accreditation.Name == "ABET") == true)}");

        var credentialDetector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
        var credentialMatches = descriptions
            .SelectMany(description => credentialDetector.Analyze(description).Credentials)
            .GroupBy(item => item.CredentialId)
            .OrderByDescending(group => group.Count());
        Console.WriteLine("CREDENTIALS");
        foreach (var group in credentialMatches)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
    }
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Local mode is the safe default", TestLocalDefaultAsync),
    ("Azure mode requires explicit storage configuration", TestAzureValidationAsync),
    ("Legacy Azure settings migrate without overriding canonical settings", TestLegacyAzureConfigurationAsync),
    ("Workspace identifiers are random and strictly validated", TestWorkspaceIdentityAsync),
    ("Workspace cookie has durable security settings", TestCookieOptionsAsync),
    ("Workspace cookie value is integrity-protected", TestProtectedCookieAsync),
    ("Workspace middleware preserves isolation through a protected cookie", TestWorkspaceMiddlewareAsync),
    ("Legacy workspace cookies migrate without changing workspace identity", TestLegacyWorkspaceCookieMigrationAsync),
    ("Azure state changes require the exact application origin", TestOriginValidationAsync),
    ("File storage round-trips beside its configured base", TestFileStoreAsync),
    ("Workspace reset deletes only known local state documents", TestFileResetAsync),
    ("Blob namespaces are isolated and traversal-resistant", TestBlobNamespaceAsync),
    ("New workspace settings are neutral", TestNeutralDefaultsAsync),
    ("Legacy applied source remains configured", TestLegacyAppliedSourceMigrationAsync),
    ("Legacy cached posting URLs migrate to the canonical field", TestLegacyCacheUrlMigrationAsync),
    ("Portable workspace round-trips settings and curated states", TestPortableWorkspaceRoundTripAsync),
    ("Portable source import distinguishes pending and equivalent state", TestPortableSourceImportStateAsync),
    ("Portable workspace validates company-scoped canonical job state", TestPortableWorkspaceValidationAsync),
    ("Portable workspace restores after a complete reset", TestPortableWorkspaceResetRestoreAsync),
    ("Fresh catalog snapshots retain the applied source", TestFreshCatalogSourceAsync),
    ("Boeing is a catalog-driven U.S. job source", TestBoeingCatalogAsync),
    ("Expanded company catalog contains the five selected live sources", TestExpandedCompanyCatalogAsync),
    ("Cross-provider location labels are grouped by U.S. state", TestExpandedLocationGroupingAsync),
    ("SmartRecruiters postings normalize through the generic source client", TestSmartRecruitersSourceAsync),
    ("Unsupported active company state migrates without source reinterpretation", TestUnsupportedCompanyMigrationAsync),
    ("Unsupported company history remains isolated", TestUnsupportedCompanyHistoryAsync),
    ("Established credential catalog entries validate", TestCredentialCatalogAsync),
    ("Academic detector recognizes advanced master's-or-higher wording", TestAdvancedDegreeAsync),
    ("Academic detector treats ABET as accreditation", TestAbetAccreditationAsync),
    ("Work authorization detector recognizes strict U.S. citizenship", TestUsCitizenshipAsync),
    ("Work authorization detector distinguishes citizens and permanent residents", TestCitizenOrResidentAsync),
    ("Work authorization detector recognizes employment sponsorship", TestSponsorshipAsync),
    ("Work authorization detector treats U.S.-person wording conservatively", TestUsPersonAsync),
    ("Work authorization detector ignores unrelated sponsor and resident language", TestAuthorizationFalsePositivesAsync),
    ("Work authorization detector surfaces non-U.S. and export wording for review", TestInternationalAuthorizationAsync),
    ("Work authorization detector recognizes location-specific work rights", TestLocationWorkRightsAsync),
    ("Clearance detector ignores explicit no-clearance statements", TestNoClearanceRequiredAsync),
    ("Remote detector finds explicit onsite duties", TestRemoteOnsiteAsync),
    ("Remote detector finds field deployment requirements", TestRemoteFieldDeploymentAsync),
    ("Remote detector finds commuting-distance restrictions", TestRemoteCommuteAsync),
    ("Remote detector finds recurring onsite days", TestRemoteOnsiteDaysAsync),
    ("Remote detector classifies substantial travel", TestRemoteTravelAsync),
    ("Remote detector accepts ordinary true-remote wording", TestTrueRemoteAsync),
    ("Remote detector leaves ambiguous site wording alone", TestRemoteAmbiguousAsync),
    ("Remote detector ignores prior site experience", TestRemoteHistoricalExperienceAsync),
    ("Remote detector recognizes sanitized Leidos deployment language", TestLeidosRemoteFixtureAsync),
    ("Remote detector keeps a sanitized MTM-style remote role neutral", TestMtmRemoteFixtureAsync),
    ("Remote detector recognizes sanitized Boeing frequent-travel language", TestBoeingRemoteFixtureAsync),
    ("Selected-company terminology remains covered by sanitized fixtures", TestExpandedCompanyFixturesAsync),
    ("Workflow state transitions are canonical and validated", TestWorkflowStateTransitionsAsync),
    ("Workflow state persists through a catalog restart", TestWorkflowStateRoundTripAsync),
    ("Workflow state remains workspace-isolated", TestWorkflowStateWorkspaceIsolationAsync),
    ("Workflow identity remains company-scoped", TestWorkflowStateCompanyIsolationAsync),
    ("Legacy Saved and history data migrate safely", TestLegacySavedHistoryMigrationAsync),
    ("Invalid legacy combinations migrate to one state", TestLegacyCombinationMigrationAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"All {tests.Length} deterministic architecture tests passed.");

static Task TestLocalDefaultAsync()
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
    var hosting = HostingConfiguration.FromConfiguration(configuration);
    Assert(hosting.IsLocal, "Hosting must default to Local.");
    Assert(hosting.StorageAccount is null && hosting.StorageContainer is null,
        "Local mode must not require Azure storage.");
    return Task.CompletedTask;
}

static Task TestAzureValidationAsync()
{
    AssertThrows<InvalidOperationException>(() => HostingConfiguration.FromConfiguration(
        Configuration(new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Azure"
        })));
    var hosting = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Azure",
            [HostingConfiguration.StorageAccountSetting] = "workdayjobmanagerstore",
            [HostingConfiguration.StorageContainerSetting] = "userdata"
        }));
    Assert(hosting.IsAzure, "Explicit Azure mode was not selected.");
    Assert(hosting.GetBlobServiceUri() ==
        new Uri("https://workdayjobmanagerstore.blob.core.windows.net"),
        "Blob service endpoint was not derived centrally.");
    return Task.CompletedTask;
}

static Task TestLegacyAzureConfigurationAsync()
{
    var legacy = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.LegacyModeSetting] = "Azure",
            [HostingConfiguration.LegacyStorageAccountSetting] = "workdayjobmanagerstore",
            [HostingConfiguration.LegacyStorageContainerSetting] = "userdata"
        }));
    Assert(legacy.IsAzure && legacy.StorageAccount == "workdayjobmanagerstore" &&
           legacy.StorageContainer == "userdata",
        "Existing Azure application settings did not migrate through the legacy aliases.");

    var canonical = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Local",
            [HostingConfiguration.LegacyModeSetting] = "Azure"
        }));
    Assert(canonical.IsLocal,
        "A legacy Azure setting overrode the canonical hosting-mode setting.");
    return Task.CompletedTask;
}

static Task TestWorkspaceIdentityAsync()
{
    var first = WorkspaceIdentity.Create();
    var second = WorkspaceIdentity.Create();
    Assert(first != second, "Secure workspace identifiers must not repeat.");
    Assert(WorkspaceIdentity.IsValid(first) && first.Length == 64,
        "Generated workspace ID is not a 256-bit lowercase hexadecimal value.");
    Assert(!WorkspaceIdentity.IsValid("../another-workspace") &&
           !WorkspaceIdentity.IsValid(first.ToUpperInvariant()) &&
           !WorkspaceIdentity.IsValid("local"),
        "Workspace validation accepted unsafe or non-Azure values.");
    return Task.CompletedTask;
}

static Task TestCookieOptionsAsync()
{
    var options = WorkspaceIdentity.CreateCookieOptions(secure: true);
    Assert(options.HttpOnly && options.Secure && options.IsEssential,
        "Azure cookie security flags are incomplete.");
    Assert(options.SameSite == SameSiteMode.Lax && options.Path == "/",
        "Workspace cookie scope is incorrect.");
    Assert(options.MaxAge >= TimeSpan.FromDays(365),
        "Workspace cookie is not long-lived.");
    return Task.CompletedTask;
}

static Task TestProtectedCookieAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var protector = provider.CreateProtector("JobSearchManager.AnonymousWorkspace.v1");
    var workspaceId = WorkspaceIdentity.Create();
    var protectedValue = protector.Protect(workspaceId);
    Assert(protectedValue != workspaceId && protector.Unprotect(protectedValue) == workspaceId,
        "Workspace cookie protection did not preserve integrity.");
    AssertThrows<System.Security.Cryptography.CryptographicException>(() =>
        protector.Unprotect(protectedValue + "tampered"));
    return Task.CompletedTask;
}

static async Task TestWorkspaceMiddlewareAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var hosting = new HostingConfiguration(
        ApplicationHostingMode.Azure,
        "workdayjobmanagerstore",
        "userdata");
    var middleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        hosting,
        provider,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);

    var firstContext = new DefaultHttpContext();
    var firstWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(firstContext, firstWorkspace);
    var setCookie = firstContext.Response.Headers.SetCookie.ToString();
    Assert(setCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
           setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
           setCookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase),
        "First Azure response did not set the secure workspace cookie.");
    var cookiePair = setCookie.Split(';', 2)[0];
    Assert(!cookiePair.Contains(firstWorkspace.WorkspaceId, StringComparison.Ordinal),
        "Raw workspace ID leaked into the cookie.");

    var returningContext = new DefaultHttpContext();
    returningContext.Request.Headers.Cookie = cookiePair;
    var returningWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(returningContext, returningWorkspace);
    Assert(returningWorkspace.WorkspaceId == firstWorkspace.WorkspaceId,
        "Protected cookie did not restore the same workspace.");

    var tamperedContext = new DefaultHttpContext();
    tamperedContext.Request.Headers.Cookie = cookiePair + "tampered";
    var tamperedWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(tamperedContext, tamperedWorkspace);
    Assert(tamperedWorkspace.WorkspaceId != firstWorkspace.WorkspaceId,
        "Tampered cookie crossed into the original workspace.");
}

static async Task TestLegacyWorkspaceCookieMigrationAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var workspaceId = WorkspaceIdentity.Create();
    var legacyProtector = provider.CreateProtector(WorkspaceIdentity.LegacyProtectorPurpose);
    var context = new DefaultHttpContext();
    context.Request.Headers.Cookie =
        $"{WorkspaceIdentity.LegacyCookieName}={legacyProtector.Protect(workspaceId)}";
    var workspace = new WorkspaceContext();
    var middleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        new HostingConfiguration(ApplicationHostingMode.Azure, "workdayjobmanagerstore", "userdata"),
        provider,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);

    await middleware.InvokeAsync(context, workspace);

    var setCookies = context.Response.Headers.SetCookie.ToString();
    Assert(workspace.WorkspaceId == workspaceId,
        "The legacy protected cookie did not retain its workspace identity.");
    Assert(setCookies.Contains(WorkspaceIdentity.CookieName, StringComparison.Ordinal) &&
           setCookies.Contains(WorkspaceIdentity.LegacyCookieName, StringComparison.Ordinal),
        "Legacy cookie migration did not issue the canonical cookie and retire the old cookie.");
}

static Task TestOriginValidationAsync()
{
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Put;
    context.Request.Path = "/api/settings";
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("workday-job-manager.azurewebsites.net");
    context.Request.Headers.Origin = "https://workday-job-manager.azurewebsites.net";
    Assert(RequestSecurity.IsStateChangingApiRequest(context.Request) &&
           RequestSecurity.HasSameOrigin(context.Request),
        "Valid same-origin Azure request was rejected.");
    context.Request.Headers.Origin = "https://attacker.example";
    Assert(!RequestSecurity.HasSameOrigin(context.Request),
        "Cross-site state-changing request was accepted.");
    context.Request.Headers.Remove("Origin");
    Assert(!RequestSecurity.HasSameOrigin(context.Request),
        "Missing Origin was accepted for an Azure state change.");
    return Task.CompletedTask;
}

static async Task TestFileStoreAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-test-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(
            directory,
            NullLogger<FileWorkspaceDataStore>.Instance);
        var document = new TestDocument("portable", 42);
        await store.WriteJsonAsync(WorkspaceDataFile.Settings, document);
        var loaded = await store.ReadJsonAsync<TestDocument>(WorkspaceDataFile.Settings);
        Assert(loaded == document, "Application-local JSON did not round-trip.");
        Assert(store.Describe(WorkspaceDataFile.Settings) ==
            Path.Combine(Path.GetFullPath(directory), "settings.json"),
            "File storage escaped its configured application-local directory.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestBlobNamespaceAsync()
{
    var container = new BlobContainerClient(new Uri("https://example.blob.core.windows.net/userdata"));
    var firstId = WorkspaceIdentity.Create();
    var secondId = WorkspaceIdentity.Create();
    var first = new AzureBlobWorkspaceDataStore(
        container, firstId, NullLogger<AzureBlobWorkspaceDataStore>.Instance);
    var second = new AzureBlobWorkspaceDataStore(
        container, secondId, NullLogger<AzureBlobWorkspaceDataStore>.Instance);
    Assert(first.Describe(WorkspaceDataFile.Settings) ==
        $"workspaces/{WorkspaceIdentity.Redact(firstId)}/settings.json", "First Blob description is incorrect.");
    Assert(second.Describe(WorkspaceDataFile.Settings) ==
        $"workspaces/{WorkspaceIdentity.Redact(secondId)}/settings.json", "Second Blob description is incorrect.");
    Assert(first.Describe(WorkspaceDataFile.Settings) != second.Describe(WorkspaceDataFile.Settings),
        "Distinct workspaces resolved to the same Blob.");
    Assert(AzureBlobWorkspaceDataStore.BuildBlobName(firstId, WorkspaceDataFile.Settings) ==
        $"workspaces/{firstId}/settings.json", "Actual Blob namespace is incorrect.");
    Assert(AzureBlobWorkspaceDataStore.BuildBlobName(secondId, WorkspaceDataFile.JobHistory) ==
        $"workspaces/{secondId}/job-history.json", "Second actual Blob namespace is incorrect.");
    AssertThrows<ArgumentException>(() => AzureBlobWorkspaceDataStore.BuildBlobName(
        "../another-workspace", WorkspaceDataFile.Settings));
    AssertThrows<ArgumentException>(() => new AzureBlobWorkspaceDataStore(
        container, "../another-workspace", NullLogger<AzureBlobWorkspaceDataStore>.Instance));
    return Task.CompletedTask;
}

static Task TestNeutralDefaultsAsync()
{
    var settings = ViewerSettings.Default;
    Assert(settings.MinimumSalary == 0m,
        "New workspaces did not default minimum acceptable annual pay to zero.");
    Assert(settings.IncludeKeywords.Count == 0 && settings.ExcludeKeywords.Count == 0,
        "New workspaces inherited personal search preferences.");
    Assert(settings.UserProfile?.Education.Level == "notSpecified" &&
           settings.UserProfile.Security?.ClearanceLevel == "notSpecified" &&
           settings.UserProfile.Security?.PublicTrust == "unknown" &&
           settings.UserProfile.WorkAuthorization?.UsStatus == "notSpecified" &&
           settings.UserProfile.WorkAuthorization?.Sponsorship == "unknown" &&
           !settings.HideStrictWorkAuthorizationMismatch,
        "New workspaces inherited personal qualification data.");
    Assert(settings.HasConfiguredSource == false && settings.CompanyId == "" &&
           settings.Country == FacetDefaults.UnitedStatesCountry &&
           !settings.IncludeAllLocations && !settings.IncludeRemote &&
           settings.SelectedPhysicalLocations?.Count == 0,
        "A new workspace was not an explicit unconfigured source with the United States preselected.");
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    AssertThrows<InvalidOperationException>(() => JobSourceQuery.FromSettings(settings, companies));
    return Task.CompletedTask;
}

static async Task TestLegacyAppliedSourceMigrationAsync()
{
    var directory = TestDirectory("legacy-applied-source");
    try
    {
        var store = new FileWorkspaceDataStore(
            directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var legacy = ViewerSettings.Default with
        {
            HasConfiguredSource = null,
            CompanyId = "leidos",
            Country = FacetDefaults.UnitedStatesCountry,
            Location = new FacetSelection(
                "da70a15d3ef40104ea4e240d39cef6a2",
                "Remote/Teleworker"),
            IncludeAllLocations = false,
            IncludeRemote = false,
            SelectedPhysicalLocations = []
        };
        await store.WriteJsonAsync(WorkspaceDataFile.Settings, legacy);
        var state = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var migrated = await state.LoadSettingsAsync();

        Assert(migrated.HasConfiguredSource == true && migrated.CompanyId == "leidos" &&
               migrated.Country == FacetDefaults.UnitedStatesCountry &&
               !migrated.IncludeAllLocations && migrated.IncludeRemote &&
               migrated.SelectedPhysicalLocations?.Count == 0 && migrated.PendingSource is null,
            "A legacy applied Leidos Remote source was not preserved as configured.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacyCacheUrlMigrationAsync()
{
    var directory = TestDirectory("legacy-cache-url");
    try
    {
        var (_, state, job) = await CreateCatalogAsync(directory);
        var canonicalJson = await File.ReadAllTextAsync(state.JobsCachePath);
        Assert(canonicalJson.Contains("\"sourceUrl\"", StringComparison.Ordinal) &&
               !canonicalJson.Contains("\"workdayUrl\"", StringComparison.Ordinal),
            "A newly written cache did not use the canonical sourceUrl field.");
        await File.WriteAllTextAsync(
            state.JobsCachePath,
            canonicalJson.Replace("\"sourceUrl\"", "\"workdayUrl\"", StringComparison.Ordinal));

        var migrated = await state.LoadJobsCacheAsync();
        var rewritten = await File.ReadAllTextAsync(state.JobsCachePath);
        Assert(migrated?.Jobs.Single().SourceUrl == job.SourceUrl,
            "The legacy cached posting URL was not restored.");
        Assert(rewritten.Contains("\"sourceUrl\"", StringComparison.Ordinal) &&
               !rewritten.Contains("\"workdayUrl\"", StringComparison.Ordinal),
            "The migrated cache was not rewritten with the canonical field.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestPortableWorkspaceRoundTripAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var source = new CompanySourceSettings(
        FacetDefaults.UnitedStatesCountry,
        false,
        true,
        []);
    var settings = ViewerSettings.Default with
    {
        IncludeKeywords = ["integration"],
        ExcludeKeywords = ["substation", "power distribution"],
        MinimumSalary = 115_000m,
        KeywordScope = "description",
        ThemeMode = "dark",
        UserProfile = new UserProfile(
            new EducationProfile("bachelor", null),
            new SecurityProfile("secret", "current"),
            new WorkAuthorizationProfile("usCitizen", "notRequired")),
        HideStrictEducationMismatch = true,
        HideStrictClearanceMismatch = true,
        HideStrictWorkAuthorizationMismatch = true,
        HasConfiguredSource = true,
        CompanyId = "leidos",
        Country = source.Country,
        IncludeRemote = true,
        CompanySources = new Dictionary<string, CompanySourceSettings> { ["leidos"] = source }
    };
    var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    var history = new JobHistoryDocument(4, new Dictionary<string, JobHistoryEntry>
    {
        ["leidos:REQ-SAVED"] = new("REQ-SAVED", "/job/saved", now, now, true,
            JobWorkflowStates.Saved, now, CompanyId: "leidos"),
        ["leidos:REQ-SAME"] = new("REQ-SAME", "/job/applied", now, now, true,
            JobWorkflowStates.Applied, now, CompanyId: "leidos"),
        ["boeing:REQ-SAME"] = new("REQ-SAME", "/job/hidden", now, now, true,
            JobWorkflowStates.Hidden, now, CompanyId: "boeing"),
        ["leidos:REQ-NORMAL"] = new("REQ-NORMAL", "/job/normal", now, now, true,
            CompanyId: "leidos")
    });

    var exported = portable.Export(settings, history);
    var json = JsonSerializer.Serialize(exported, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var imported = portable.ImportJson(json, ViewerSettings.Default, JobHistoryDocument.Empty);

    Assert(exported.Format == PortableWorkspaceService.FormatIdentifier && exported.Version == 1 &&
           exported.CuratedJobs.Count == 3 &&
           exported.CuratedJobs.All(job => job.WorkflowState != JobWorkflowStates.Normal),
        "The portable file did not contain exactly the three curated workflow records.");
    Assert(exported.Format == "JobSearchManagerBackup" &&
           !json.Contains("WorkdayJobManager", StringComparison.OrdinalIgnoreCase),
        "A new portable backup used a legacy product-owned identifier.");
    Assert(imported.Settings.HasConfiguredSource == false &&
           imported.Settings.PendingSource?.CompanyId == "leidos" &&
           imported.Settings.IncludeKeywords.SequenceEqual(["integration"]) &&
           imported.Settings.ExcludeKeywords.SequenceEqual(["substation", "power distribution"]) &&
           imported.Settings.MinimumSalary == 115_000m && imported.Settings.ThemeMode == "dark",
        "Portable preferences or pending source selection did not round-trip.");
    Assert(imported.History.Jobs["leidos:REQ-SAVED"].WorkflowState == JobWorkflowStates.Saved &&
           imported.History.Jobs["leidos:REQ-SAME"].WorkflowState == JobWorkflowStates.Applied &&
           imported.History.Jobs["boeing:REQ-SAME"].WorkflowState == JobWorkflowStates.Hidden &&
           !imported.History.Jobs.ContainsKey("leidos:REQ-NORMAL"),
        "Saved, Applied, Hidden, absent-catalog, or company-isolated state did not round-trip.");
    var legacyImported = portable.Import(
        exported with { Format = PortableWorkspaceService.LegacyFormatIdentifier },
        ViewerSettings.Default,
        JobHistoryDocument.Empty);
    Assert(legacyImported.History.Jobs["leidos:REQ-SAVED"].WorkflowState == JobWorkflowStates.Saved &&
           legacyImported.Settings.ExcludeKeywords.SequenceEqual(["substation", "power distribution"]),
        "A backup exported under the previous product name did not import safely.");
    return Task.CompletedTask;
}

static Task TestPortableWorkspaceValidationAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var baseline = portable.Export(ViewerSettings.Default, JobHistoryDocument.Empty);
    AssertThrows<WorkspaceImportException>(() => portable.ImportJson(
        "{not valid json", ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Format = "AnotherFormat" },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Version = PortableWorkspaceService.CurrentVersion + 1 },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));
    var unknownSource = baseline with
    {
        Preferences = baseline.Preferences with
        {
            JobSource = new PortableJobSource(
                "unknown-source",
                FacetDefaults.UnitedStatesCountry,
                false,
                true,
                [])
        }
    };
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        unknownSource, ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Preferences = baseline.Preferences with
        {
            Search = baseline.Preferences.Search with { KeywordScope = "executable-regex" }
        } }, ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Preferences = baseline.Preferences with
        {
            Qualifications = baseline.Preferences.Qualifications with { MinimumSalary = -1m }
        } }, ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Preferences = baseline.Preferences with
        {
            Application = baseline.Preferences.Application with { ThemeMode = "system-script" }
        } }, ViewerSettings.Default, JobHistoryDocument.Empty));
    var unsupported = baseline with
    {
        CuratedJobs =
        [
            new PortableCuratedJob(
                "unsupported-company", "unsupported-company:REQ-1", "REQ-1",
                JobWorkflowStates.Saved, "/job/REQ-1")
        ]
    };
    var isolatedUnsupported = portable.Import(
        unsupported, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(isolatedUnsupported.History.Jobs.TryGetValue(
               "unsupported-company:REQ-1", out var unsupportedEntry) &&
           unsupportedEntry.CompanyId == "unsupported-company" &&
           !isolatedUnsupported.History.Jobs.ContainsKey("leidos:REQ-1"),
        "An unsupported curated company identity was dropped or silently remapped.");

    var conflictingDuplicate = baseline with
    {
        CuratedJobs =
        [
            new PortableCuratedJob("leidos", "leidos:REQ-1", "REQ-1",
                JobWorkflowStates.Saved, "/job/REQ-1"),
            new PortableCuratedJob("leidos", "leidos:REQ-1", "REQ-1",
                JobWorkflowStates.Applied, "/job/REQ-1")
        ]
    };
    AssertThrows<WorkspaceImportException>(() =>
        portable.Import(conflictingDuplicate, ViewerSettings.Default, JobHistoryDocument.Empty));

    var mismatchedIdentity = baseline with
    {
        CuratedJobs =
        [
            new PortableCuratedJob("leidos", "leidos:REQ-OTHER", "REQ-1",
                JobWorkflowStates.Hidden, "/job/REQ-1")
        ]
    };
    AssertThrows<WorkspaceImportException>(() =>
        portable.Import(mismatchedIdentity, ViewerSettings.Default, JobHistoryDocument.Empty));

    using var exportedJson = JsonDocument.Parse(JsonSerializer.Serialize(baseline,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var root = exportedJson.RootElement;
    Assert(root.TryGetProperty("format", out _) && root.TryGetProperty("version", out _) &&
           root.TryGetProperty("preferences", out _) && root.TryGetProperty("curatedJobs", out _) &&
           !root.TryGetProperty("workspaceId", out _) && !root.TryGetProperty("jobs", out _) &&
           !root.TryGetProperty("cache", out _) && !root.TryGetProperty("securityToken", out _),
        "The external DTO is missing its versioned structure or exposed an internal runtime field.");
    return Task.CompletedTask;
}

static Task TestPortableSourceImportStateAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var leidos = ViewerSettings.Default with
    {
        HasConfiguredSource = true,
        CompanyId = "leidos",
        Country = FacetDefaults.UnitedStatesCountry,
        IncludeRemote = true,
        CompanySources = new Dictionary<string, CompanySourceSettings>
        {
            ["leidos"] = new(FacetDefaults.UnitedStatesCountry, false, true, [])
        }
    };
    var document = portable.Export(leidos, JobHistoryDocument.Empty);

    var freshImport = portable.Import(document, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(freshImport.Settings.HasConfiguredSource == false &&
           freshImport.Settings.PendingSource?.CompanyId == "leidos",
        "Importing a source into a fresh workspace did not create explicit pending state.");

    var equalImport = portable.Import(document, leidos, JobHistoryDocument.Empty);
    Assert(equalImport.Settings.HasConfiguredSource == true &&
           equalImport.Settings.CompanyId == "leidos" &&
           equalImport.Settings.PendingSource is null,
        "A source equivalent to the applied source created a false pending state.");

    var boeing = ViewerSettings.Default with
    {
        HasConfiguredSource = true,
        CompanyId = "boeing",
        Country = FacetDefaults.UnitedStatesCountry,
        IncludeAllLocations = true,
        IncludeRemote = true
    };
    var differentImport = portable.Import(document, boeing, JobHistoryDocument.Empty);
    Assert(differentImport.Settings.CompanyId == "boeing" &&
           differentImport.Settings.PendingSource?.CompanyId == "leidos",
        "A different imported source replaced the applied source instead of becoming pending.");

    var noSourceDocument = portable.Export(ViewerSettings.Default, JobHistoryDocument.Empty);
    var noSourceImport = portable.Import(
        noSourceDocument, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(noSourceImport.Settings.HasConfiguredSource == false &&
           noSourceImport.Settings.PendingSource is null,
        "A backup without a Job Source created a conflicting source state.");
    return Task.CompletedTask;
}

static async Task TestPortableWorkspaceResetRestoreAsync()
{
    var directory = TestDirectory("portable-reset-restore");
    try
    {
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var portable = new PortableWorkspaceService(companies);
        var configured = state.NormalizeSettings(ViewerSettings.Default with
        {
            IncludeKeywords = ["cloud"],
            MinimumSalary = 125_000m,
            HasConfiguredSource = true,
            CompanyId = "leidos",
            IncludeRemote = true
        });
        var now = DateTimeOffset.UtcNow;
        var curated = new JobHistoryDocument(4, new Dictionary<string, JobHistoryEntry>
        {
            ["leidos:REQ-SAVED"] = new("REQ-SAVED", "/job/REQ-SAVED", now, now, true,
                JobWorkflowStates.Saved, now, CompanyId: "leidos"),
            ["leidos:REQ-A"] = new("REQ-A", "/job/a", now, now, true,
                JobWorkflowStates.Applied, now, CompanyId: "leidos"),
            ["leidos:REQ-H"] = new("REQ-H", "/job/h", now, now, true,
                JobWorkflowStates.Hidden, now, CompanyId: "leidos")
        });
        await state.SaveSettingsAsync(configured);
        await state.SaveJobHistoryAsync(curated);
        var exported = portable.Export(
            await state.LoadSettingsAsync(), await state.LoadJobHistoryAsync());
        var json = JsonSerializer.Serialize(exported, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await store.DeleteAllAsync();
        await state.EnsureSettingsFileAsync();
        var resetSettings = await state.LoadSettingsAsync();
        var resetHistory = await state.LoadJobHistoryAsync();
        Assert(resetSettings.HasConfiguredSource == false && resetHistory.Jobs.Count == 0,
            "Reset did not return the workspace to first-run state.");

        var restored = portable.ImportJson(json, resetSettings, resetHistory);
        await state.SaveSettingsAsync(restored.Settings);
        await state.SaveJobHistoryAsync(restored.History);
        var loadedSettings = await state.LoadSettingsAsync();
        var loadedHistory = await state.LoadJobHistoryAsync();
        Assert(loadedSettings.IncludeKeywords.SequenceEqual(["cloud"]) &&
               loadedSettings.MinimumSalary == 125_000m &&
               loadedSettings.HasConfiguredSource == false &&
               loadedSettings.PendingSource?.CompanyId == "leidos",
            "Settings or explicit-apply source semantics did not survive reset and restore.");
        Assert(loadedHistory.Jobs.Count(pair => pair.Value.WorkflowState == JobWorkflowStates.Saved) == 1 &&
               loadedHistory.Jobs.Count(pair => pair.Value.WorkflowState == JobWorkflowStates.Applied) == 1 &&
               loadedHistory.Jobs.Count(pair => pair.Value.WorkflowState == JobWorkflowStates.Hidden) == 1 &&
               loadedHistory.Jobs.Values.All(entry => JobWorkflowStates.IsValid(entry.WorkflowState)),
            "Curated workflow state did not survive the full export-reset-import round-trip.");
        var reconciled = await CreateCatalogAsync(directory);
        Assert(reconciled.Catalog.Snapshot.JobStates[reconciled.Job.StableId] ==
               JobWorkflowStates.Saved,
            "A restored curated state did not reconcile when the company/job identity reappeared.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestFreshCatalogSourceAsync()
{
    var directory = TestDirectory("fresh-catalog-source");
    try
    {
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var settings = state.NormalizeSettings(ViewerSettings.Default with
        {
            HasConfiguredSource = true,
            CompanyId = CompanyCatalog.DefaultCompanyId,
            IncludeRemote = true
        });
        await state.SaveSettingsAsync(settings);
        var query = JobSourceQuery.FromSettings(settings, companies);
        var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
        var academics = new AcademicQualificationDetector();
        var authorization = new WorkAuthorizationDetector();
        var remote = new RemoteWorkDetector();
        var client = new JobSourceClient(
            new HttpClient(),
            Options.Create(new JobSourceOptions()),
            NullLogger<JobSourceClient>.Instance,
            credentials,
            academics,
            authorization,
            remote);
        var catalog = new JobCatalog(
            client,
            state,
            NullLogger<JobCatalog>.Instance,
            credentials,
            academics,
            authorization,
            remote,
            companies);

        await catalog.InitializeAsync(query);

        Assert(catalog.Snapshot.Jobs.Count == 0 && catalog.Snapshot.LastRefreshedUtc is null,
            "Fresh catalog test unexpectedly loaded a jobs cache.");
        Assert(catalog.Snapshot.Query.IsEquivalentTo(query, companies),
            "The cacheless snapshot exposed a generic source instead of the applied source.");
        Assert(catalog.Snapshot.Query.CountryId == settings.Country.Id &&
               catalog.Snapshot.Query.IncludeAllLocations == settings.IncludeAllLocations &&
               catalog.Snapshot.Query.IncludeRemote == settings.IncludeRemote,
            "The cacheless snapshot did not retain all normalized source dimensions.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task TestBoeingCatalogAsync()
{
    using var document = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "CompanyCatalog.json")));
    var companies = document.RootElement.GetProperty("companies").EnumerateArray().ToArray();
    var company = companies.Single(item => item.GetProperty("id").GetString() == "boeing");
    Assert(company.GetProperty("displayName").GetString() == "Boeing" &&
           company.GetProperty("apiHost").GetString() ==
           "boeing.wd1.myworkdayjobs.com" &&
           company.GetProperty("tenant").GetString() == "boeing" &&
           company.GetProperty("site").GetString() == "EXTERNAL_CAREERS" &&
           company.GetProperty("defaultCountry").GetProperty("label").GetString() ==
           "United States of America",
        "Boeing was not represented solely by the verified generic catalog fields.");
    Assert(companies.All(item => item.GetProperty("id").GetString() != "deloitte-ie"),
        "Deloitte Ireland remains an active catalog company.");
    return Task.CompletedTask;
}

static Task TestExpandedCompanyCatalogAsync()
{
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["northrop-grumman"] = "Northrop Grumman",
        ["nvidia"] = "NVIDIA",
        ["parsons"] = "Parsons",
        ["aecom"] = "AECOM",
        ["rtx"] = "RTX"
    };
    foreach (var company in expected)
    {
        var definition = catalog.Get(company.Key);
        Assert(definition.DisplayName == company.Value && definition.RemoteLocationIds.Count > 0,
            $"The verified {company.Value} source or its remote facet configuration is missing.");
    }
    Assert(catalog.Get("aecom").IsSmartRecruiters &&
           catalog.Get("aecom").IsRemoteLocation("remote:ca") &&
           catalog.Get("aecom").RemoteLocationIdsForCountry("ca").Single() == "remote:ca" &&
           catalog.Get("nvidia").Provider == JobSourceProviders.Workday,
        "Provider selection was not represented by catalog data.");
    return Task.CompletedTask;
}

static Task TestExpandedLocationGroupingAsync()
{
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var cases = new[]
    {
        ("northrop-grumman", "ng-orlando", "United States-Florida-Orlando", "Orlando"),
        ("nvidia", "nv-santa-clara", "US, CA, Santa Clara", "Santa Clara"),
        ("parsons", "pa-orlando", "US - FL, Orlando", "Orlando"),
        ("aecom", "location:us:Orlando, FL, United States", "Orlando, FL, United States", "Orlando"),
        ("rtx", "rtx-orlando", "US-FL-ORLANDO", "ORLANDO")
    };
    foreach (var item in cases)
    {
        var organization = LocationFacetOrganizer.Organize(
            catalog.Get(item.Item1), null, [new FacetOption(item.Item2, item.Item3, 1)]);
        Assert(organization.StateMappedLocationCount == 1 &&
               organization.PhysicalLocations.Single().DisplayLabel == item.Item4,
            $"The {item.Item1} location format was not mapped to its U.S. state.");
    }
    return Task.CompletedTask;
}

static async Task TestSmartRecruitersSourceAsync()
{
    const string summary = """
        {"totalFound":1,"content":[{"id":"744000100000001","name":"Remote Project Analyst","refNumber":"J10000001","releasedDate":"2026-08-15T12:30:00Z","location":{"city":"Orlando","region":"FL","country":"us","remote":true,"hybrid":false,"fullLocation":"Orlando, FL, United States"},"typeOfEmployment":{"label":"Full-time"}}]}
        """;
    const string detail = """
        {"id":"744000100000001","name":"Remote Project Analyst","refNumber":"J10000001","releasedDate":"2026-08-15T12:30:00Z","postingUrl":"https://jobs.smartrecruiters.com/AECOM2/744000100000001-remote-project-analyst","location":{"city":"Orlando","region":"FL","country":"us","remote":true,"hybrid":false,"fullLocation":"Orlando, FL, United States"},"typeOfEmployment":{"label":"Full-time"},"jobAd":{"sections":{"companyDescription":{"title":"Company Description","text":"<p>Generic company remote-location boilerplate.</p>"},"jobDescription":{"title":"Job Description","text":"<p>This position is fully remote within the United States.</p>"},"qualifications":{"title":"Qualifications","text":"<p>Bachelor's degree plus four years of experience.</p>"},"additionalInformation":{"title":"Additional Information","text":"<p>Sponsorship for US employment authorization is not available now or in the future. The salary range for this role is 90,000 USD - 120,000 USD.</p>"}}}}
        """;
    var handler = new StubHttpMessageHandler(request =>
        request.RequestUri!.AbsolutePath.EndsWith("/744000100000001", StringComparison.Ordinal)
            ? detail
            : summary);
    var client = CreateSourceClient(new HttpClient(handler));
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var company = catalog.Get("aecom");

    var facets = await client.FetchLocationFacetsAsync(company, "us");
    Assert(facets.MatchingJobs == 1 && facets.RemoteLocations.Single().Id == "remote:us",
        "SmartRecruiters remote metadata did not become a selectable source facet.");

    var result = await client.FetchAllJobsAsync(
        company,
        new JobSourceQuery("us", "United States", false, true, [], CompanyId: "aecom"));
    var job = result.Jobs.Single();
    Assert(job.CompanyId == "aecom" && job.StableId == "aecom:J10000001" &&
           job.SourceUrl.Contains("jobs.smartrecruiters.com", StringComparison.Ordinal) &&
           job.PayMinimum == 90000m && job.PayMaximum == 120000m &&
           job.WorkAuthorization?.Sponsorship == "notAvailable" &&
           job.RemoteWork?.IsRemoteDesignated == true &&
           !job.DescriptionHtml.Contains("Generic company", StringComparison.Ordinal),
        $"SmartRecruiters normalization: company={job.CompanyId}, stable={job.StableId}, url={job.SourceUrl}, pay={job.PayMinimum}-{job.PayMaximum}/{job.PayPeriod}, sponsorship={job.WorkAuthorization?.Sponsorship}, remote={job.RemoteWork?.IsRemoteDesignated}, boilerplate={job.DescriptionHtml.Contains("Generic company", StringComparison.Ordinal)}.");
}

static async Task TestUnsupportedCompanyMigrationAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-company-migration-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var stale = ViewerSettings.Default with
        {
            HasConfiguredSource = true,
            CompanyId = "deloitte-ie",
            Country = new FacetSelection("04a05835925f45b3a59406a2a6b72c8a", "Ireland"),
            IncludeAllLocations = false,
            IncludeRemote = false,
            SelectedPhysicalLocations =
            [
                new FacetSelection("deloitte-dublin", "Dublin")
            ],
            CompanySources = new Dictionary<string, CompanySourceSettings>
            {
                ["deloitte-ie"] = new(
                    new FacetSelection("04a05835925f45b3a59406a2a6b72c8a", "Ireland"),
                    false,
                    false,
                    [new FacetSelection("deloitte-dublin", "Dublin")])
            }
        };
        await store.WriteJsonAsync(WorkspaceDataFile.Settings, stale);
        var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, catalog, store);

        var migrated = await state.LoadSettingsAsync();

        Assert(migrated.CompanyId == CompanyCatalog.DefaultCompanyId,
            "Unsupported active company did not migrate to the safe default company.");
        Assert(migrated.Country.Id == "bc33aa3152ec42d4995f4791a106ed09" &&
               migrated.IncludeAllLocations && migrated.IncludeRemote &&
               migrated.SelectedPhysicalLocations?.Count == 0,
            "Deloitte Ireland source facets were reinterpreted as the default company's source.");
        Assert(migrated.CompanySources is not null &&
               !migrated.CompanySources.ContainsKey("deloitte-ie"),
            "Unsupported per-company source state survived migration.");
        var persisted = await store.ReadJsonAsync<ViewerSettings>(WorkspaceDataFile.Settings);
        Assert(persisted?.CompanyId == CompanyCatalog.DefaultCompanyId,
            "The safe company migration was not persisted.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestUnsupportedCompanyHistoryAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-history-migration-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        var stale = new JobHistoryDocument(2, new Dictionary<string, JobHistoryEntry>
        {
            ["deloitte-ie:REQ-42"] = new("REQ-42", "/job/REQ-42", now, now, true,
                CompanyId: "deloitte-ie")
        });
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, stale);
        var state = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var loaded = await state.LoadJobHistoryAsync();
        Assert(loaded.Jobs.TryGetValue("deloitte-ie:REQ-42", out var entry) &&
               entry.CompanyId == "deloitte-ie" &&
               !loaded.Jobs.ContainsKey("boeing:REQ-42") &&
               !loaded.Jobs.ContainsKey("leidos:REQ-42"),
            "Unsupported Deloitte history was reassigned to an active company.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestCredentialCatalogAsync()
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    Assert(detector.CatalogVersion == 11, "The expanded credential catalog version was not loaded.");
    using var document = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "CredentialCatalog.json")));
    var ids = document.RootElement.GetProperty("credentials").EnumerateArray()
        .Select(item => item.GetProperty("id").GetString())
        .ToHashSet(StringComparer.Ordinal);
    var expected = new[]
    {
        "aca-chartered-accountant", "acca-qualification", "cima-professional-qualification",
        "cfa-charter", "iapp-cipp-e", "iapp-aigp", "iapp-cipm", "iapp-cipt",
        "ipass-payroll-qualification", "cta-chartered-tax-adviser",
        "oracle-certification-unspecified", "sap-certification-unspecified",
        "faa-airframe-powerplant", "epa-section-608-universal", "cpsm"
    };
    Assert(expected.All(ids.Contains), "One or more verified credentials are absent.");
    return Task.CompletedTask;
}

static Task TestAdvancedDegreeAsync()
{
    var analysis = new AcademicQualificationDetector().Analyze(
        "<p>Advanced degree (Master’s or higher) in computer science or a related discipline.</p>");
    Assert(analysis.MinimumLevel == "master" &&
           analysis.AnalysisVersion == AcademicQualificationDetector.CurrentAnalysisVersion,
        "Advanced degree (Master's or higher) was not normalized to a master's minimum.");
    return Task.CompletedTask;
}

static Task TestAbetAccreditationAsync()
{
    var detector = new AcademicQualificationDetector();
    var preferred = detector.Analyze(
        "<h3>Basic Qualifications</h3><p>Bachelor's degree in engineering.</p>" +
        "<p>In the USA, ABET accreditation is the preferred, although not required, accreditation standard.</p>");
    Assert(preferred.Accreditations is [{ Name: "ABET", Requirement: "preferred" }],
        "Boeing's standard ABET wording was not retained as a preferred academic accreditation.");
    var required = detector.Analyze(
        "<h3>Required Qualifications</h3><p>A bachelor's degree from an ABET-accredited program is required.</p>");
    Assert(required.Accreditations is [{ Name: "ABET", Requirement: "required" }],
        "An explicit ABET requirement was not classified as required.");
    return Task.CompletedTask;
}

static async Task TestFileResetAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-reset-test-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(
            directory,
            NullLogger<FileWorkspaceDataStore>.Instance);
        foreach (var file in Enum.GetValues<WorkspaceDataFile>())
        {
            await store.WriteJsonAsync(file, new TestDocument(file.ToString(), 1));
        }
        var unrelatedPath = Path.Combine(directory, "leave-me-alone.txt");
        await File.WriteAllTextAsync(unrelatedPath, "not application state");

        var deleted = await store.DeleteAllAsync();
        Assert(deleted == 3, $"Expected three known documents to be deleted, but deleted {deleted}.");
        Assert(Enum.GetValues<WorkspaceDataFile>().All(file => !File.Exists(store.Describe(file))),
            "A known workspace document survived reset.");
        Assert(File.Exists(unrelatedPath), "Reset deleted an unrelated file from the data directory.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestUsCitizenshipAsync()
{
    var detector = new WorkAuthorizationDetector();
    var strict = detector.Analyze("<p>Candidate must be a U.S. citizen.</p>");
    Assert(strict.Eligibility == "usCitizen" && strict.CountryCode == "US" &&
           strict.Strength == "strict" && strict.ParseStatus == "parsed",
        "Explicit U.S. citizenship was not parsed as a strict requirement.");
    var preferred = detector.Analyze("<p>U.S. citizenship is preferred.</p>");
    Assert(preferred.Eligibility == "usCitizen" && preferred.Strength == "preferred" &&
           preferred.ParseStatus == "parsed",
        "Explicitly preferred citizenship wording was not kept non-strict.");
    return Task.CompletedTask;
}

static Task TestCitizenOrResidentAsync()
{
    var analysis = new WorkAuthorizationDetector().Analyze(
        "<p>Must be either a U.S. Citizen OR a U.S. Permanent Resident/Green Card holder.</p>");
    Assert(analysis.Eligibility == "usCitizenOrPermanentResident" && analysis.Strength == "strict",
        $"Citizen-or-permanent-resident wording parsed as {analysis.Eligibility}/{analysis.Strength}.");
    return Task.CompletedTask;
}

static Task TestSponsorshipAsync()
{
    var analysis = new WorkAuthorizationDetector().Analyze(
        "<p>Must be authorized to work in the United States and not require work authorization sponsorship by our company now or in the future.</p>");
    Assert(analysis.Eligibility == "usWorkAuthorized" && analysis.Sponsorship == "notAvailable" &&
           analysis.Strength == "strict" && analysis.SponsorshipStrength == "strict",
        "Combined U.S. authorization/no-sponsorship requirement was not parsed.");
    var sponsorshipOnly = new WorkAuthorizationDetector().Analyze(
        "<p>No employment sponsorship available.</p>");
    Assert(sponsorshipOnly.Eligibility == "noneSpecified" &&
           sponsorshipOnly.Sponsorship == "notAvailable" &&
           sponsorshipOnly.SponsorshipStrength == "strict" &&
           sponsorshipOnly.ParseStatus == "parsed",
        "Standalone no-sponsorship wording was not preserved as a strict sponsorship predicate.");
    var boeing = new WorkAuthorizationDetector().Analyze(
        "<p>Employer will not sponsor applicants for employment visa status.</p>");
    Assert(boeing.Sponsorship == "notAvailable" && boeing.SponsorshipStrength == "strict",
        "The common employer-will-not-sponsor wording was not recognized.");
    return Task.CompletedTask;
}

static Task TestNoClearanceRequiredAsync()
{
    var none = JobAnalysis.AnalyzeClearance(
        "<p>This position does not require a Security Clearance.</p>");
    Assert(none.Level == "noneMentioned" && none.Requirement == "none",
        "An explicit no-clearance statement was treated as a clearance requirement.");
    var positiveElsewhere = JobAnalysis.AnalyzeClearance(
        "<p>This position does not require a Security Clearance.</p>" +
        "<p>Candidate must obtain a U.S. Secret clearance.</p>");
    Assert(positiveElsewhere.Level == "secret",
        $"Removing a negative clearance statement hid a separate positive requirement: {positiveElsewhere.Level}/{positiveElsewhere.Requirement}; {positiveElsewhere.Evidence}.");
    return Task.CompletedTask;
}

static Task TestUsPersonAsync()
{
    var detector = new WorkAuthorizationDetector();
    var candidate = detector.Analyze(
        "<p>Must be a US Citizen or US Person who has lived in the United States for three years.</p>");
    Assert(candidate.Eligibility == "usPerson" && candidate.Strength == "ambiguous" &&
           candidate.ParseStatus == "review",
        "U.S.-person candidate wording must remain review-only.");
    var information = detector.Analyze(
        "<p>Protect U.S. person information collected under FISA authorities.</p>");
    Assert(information.Eligibility == "noneSpecified",
        "U.S.-person information was misclassified as candidate eligibility.");
    return Task.CompletedTask;
}

static Task TestAuthorizationFalsePositivesAsync()
{
    var detector = new WorkAuthorizationDetector();
    var clearance = detector.Analyze("<p>We are not able to sponsor the clearance requirement.</p>");
    Assert(clearance.Sponsorship == "noneSpecified",
        "Clearance sponsorship was misclassified as employment sponsorship.");
    var residentEngineer = detector.Analyze("<p>The resident engineer supports a permanent position.</p>");
    Assert(residentEngineer.Eligibility == "noneSpecified",
        "Ordinary resident/permanent wording was misclassified.");
    var citizenDeveloper = detector.Analyze("<p>Support the citizen developer community.</p>");
    Assert(citizenDeveloper.Eligibility == "noneSpecified",
        "Citizen-developer wording was misclassified.");
    return Task.CompletedTask;
}

static Task TestInternationalAuthorizationAsync()
{
    var detector = new WorkAuthorizationDetector();
    var australian = detector.Analyze("<p>The successful applicant must be an Australian Citizen.</p>");
    Assert(australian.Eligibility == "australianCitizen" && australian.CountryCode == "AU" &&
           australian.Strength == "strict",
        "Australian citizenship wording was not surfaced.");
    var export = detector.Analyze(
        "<p>Applicants may also need to meet International Traffic in Arms Regulations (ITAR) requirements.</p>");
    Assert(export.Eligibility == "exportControlled" && export.Strength == "customerDependent" &&
           export.ParseStatus == "review",
        "Conditional ITAR wording should be review-only.");
    return Task.CompletedTask;
}

static Task TestLocationWorkRightsAsync()
{
    var analysis = new WorkAuthorizationDetector().Analyze(
        "<p>Valid work rights for the role location (no sponsorship available).</p>");
    Assert(analysis.Eligibility == "locationWorkAuthorized" &&
           analysis.Strength == "strict" &&
           analysis.CountryCode is null &&
           analysis.Sponsorship == "notAvailable" &&
           analysis.SponsorshipStrength == "strict" &&
           analysis.ParseStatus == "parsed",
        "Location-specific work-rights wording was not parsed conservatively.");
    return Task.CompletedTask;
}

static Task TestRemoteOnsiteAsync()
{
    var analysis = RemoteAnalysis(
        "<p>This role will provide onsite installation support at the customer facility.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "onsite-duty"),
        "A current onsite duty was not treated as a strong remote-work conflict.");
    return Task.CompletedTask;
}

static Task TestRemoteFieldDeploymentAsync()
{
    var analysis = RemoteAnalysis(
        "<p>You will conduct field integration testing and support operational validation.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "field-deployment"),
        "Field deployment/testing was not detected.");
    return Task.CompletedTask;
}

static Task TestRemoteCommuteAsync()
{
    var analysis = RemoteAnalysis(
        "<p>Candidates must live within 50 miles of the regional office.</p>");
    Assert(analysis.ConcernLevel == "questionable" &&
           analysis.Signals.Any(signal => signal.Category == "commuting-area"),
        "A commuting-radius restriction was not surfaced conservatively.");
    return Task.CompletedTask;
}

static Task TestRemoteOnsiteDaysAsync()
{
    var analysis = RemoteAnalysis(
        "<p>The employee will work three days per week in the office.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "scheduled-onsite"),
        "Recurring onsite days were not treated as a strong conflict.");
    return Task.CompletedTask;
}

static Task TestRemoteTravelAsync()
{
    var questionable = RemoteAnalysis("<p>This position requires 25% travel.</p>");
    var strong = RemoteAnalysis("<p>This position requires up to 75% travel to airport locations.</p>");
    var trailingPercentage = RemoteAnalysis("<p>The position may require travel up to 50% of the time.</p>");
    Assert(questionable.ConcernLevel == "questionable" && strong.ConcernLevel == "strong" &&
           trailingPercentage.ConcernLevel == "strong",
        $"Travel severity was incorrect: {questionable.ConcernLevel}/{strong.ConcernLevel}.");
    return Task.CompletedTask;
}

static Task TestTrueRemoteAsync()
{
    var analysis = RemoteAnalysis(
        "<p>This is a fully remote software role. Collaborate through video meetings and deliver cloud services from your home office.</p>");
    Assert(analysis.IsRemoteDesignated && analysis.ConcernLevel == "none",
        "Ordinary work-from-home language was incorrectly warned.");
    return Task.CompletedTask;
}

static Task TestRemoteAmbiguousAsync()
{
    var analysis = RemoteAnalysis(
        "<p>The team builds software used by customer facilities and may attend occasional team events.</p>");
    var periodic = RemoteAnalysis(
        "<p>Working onsite in a company or client office is a possibility; periodic travel may be required.</p>");
    Assert(analysis.ConcernLevel == "none" && periodic.ConcernLevel == "questionable",
        "A facility mention without a current physical-presence requirement was escalated.");
    return Task.CompletedTask;
}

static Task TestRemoteHistoricalExperienceAsync()
{
    var analysis = RemoteAnalysis(
        "<p>Prior experience working at customer sites and conducting field testing is preferred.</p>");
    Assert(analysis.ConcernLevel == "none",
        "Historical site experience was mistaken for a current-job obligation.");
    return Task.CompletedTask;
}

static Task TestLeidosRemoteFixtureAsync()
{
    var analysis = RemoteAnalysis(
        "<h3>Field Deployment Support</h3>" +
        "<p>Support installation and activation of sensing systems.</p>" +
        "<p>Assist field operations during deployment and troubleshoot equipment integration issues in the field.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "physical-installation"),
        "The sanitized Leidos sensor-integration fixture was not classified as strongly inconsistent with WFH.");
    return Task.CompletedTask;
}

static Task TestMtmRemoteFixtureAsync()
{
    var analysis = RemoteAnalysis(
        "<p>Coordinate member transportation by telephone and document service issues. " +
        "The platform connects members with medical facilities.</p>");
    Assert(analysis.ConcernLevel == "none",
        "The neutral MTM-style service-coordination fixture produced a false positive.");
    return Task.CompletedTask;
}

static Task TestBoeingRemoteFixtureAsync()
{
    var detector = new RemoteWorkDetector();
    var analysis = detector.Analyze(
        "Senior Quality Auditor (Virtual)",
        "United States - Virtual",
        [],
        "<p>This position requires the ability to travel frequently to company sites, as needed.</p>" +
        "<p>The position may require travel up to 50% of the time.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "frequent-travel") &&
           analysis.Signals.Any(signal => signal.Category == "substantial-travel"),
        "The sanitized Boeing virtual/frequent-travel fixture was not surfaced at the supported severity.");
    return Task.CompletedTask;
}

static Task TestExpandedCompanyFixturesAsync()
{
    const string northrop = """
        <p>This position is remote; candidates will need to be within a commutable distance from one of the listed company locations.</p>
        <p>Bachelor's degree with eight years of experience or a master's degree with six years of experience.</p>
        <p>U.S. citizen. Ability to obtain and maintain a Secret clearance. Travel up to 75%.</p>
        """;
    var northropRemote = RemoteAnalysis(northrop);
    var northropAcademic = new AcademicQualificationDetector().Analyze(northrop);
    Assert(northropRemote.ConcernLevel == "strong" &&
           northropRemote.Signals.Any(signal => signal.Category == "commuting-area") &&
           northropRemote.Signals.Any(signal => signal.Category == "substantial-travel") &&
           new WorkAuthorizationDetector().Analyze(northrop).Eligibility == "usCitizen" &&
           JobAnalysis.AnalyzeClearance(northrop).Level == "secret" &&
           northropAcademic.MinimumLevel == "bachelor",
        $"Northrop fixture: remote={northropRemote.ConcernLevel} [{string.Join(',', northropRemote.Signals.Select(signal => signal.Category))}], authorization={new WorkAuthorizationDetector().Analyze(northrop).Eligibility}, clearance={JobAnalysis.AnalyzeClearance(northrop).Level}, academic={northropAcademic.MinimumLevel}.");

    var nvidiaSalary = JobAnalysis.AnalyzeSalary(
        "<p>The base salary range is 184,000 USD - 287,500 USD for this level.</p>");
    Assert(nvidiaSalary.Minimum == 184000m && nvidiaSalary.Maximum == 287500m &&
           nvidiaSalary.Period == "annual",
        "The sanitized NVIDIA USD salary format was not parsed.");

    const string parsons = """
        <p>Professional Registration (PE) and Project Management Professional (PMP) certification are required.</p>
        <p>Must be a U.S. Citizen and able to pass required federal background checks. Security Clearance Requirement: None.</p>
        """;
    var parsonsCredentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance)
        .Analyze(parsons).Credentials;
    Assert(parsonsCredentials.Any(item => item.CredentialId == "pe") &&
           parsonsCredentials.Any(item => item.CredentialId == "pmp") &&
           JobAnalysis.AnalyzeClearance(parsons).Level == "noneMentioned",
        $"Parsons fixture credentials={string.Join(',', parsonsCredentials.Select(item => item.CredentialId))}, clearance={JobAnalysis.AnalyzeClearance(parsons).Level}/{JobAnalysis.AnalyzeClearance(parsons).Requirement}.");

    const string aecom = """
        <p>This role can work remotely within the Eastern Time Zone. Candidates must be authorized to work in the United States without current or future sponsorship.</p>
        """;
    Assert(new WorkAuthorizationDetector().Analyze(aecom).Sponsorship == "notAvailable" &&
           JobAnalysis.AnalyzeRemoteLocation(aecom, "Remote", []).IsRestricted,
        $"AECOM fixture sponsorship={new WorkAuthorizationDetector().Analyze(aecom).Sponsorship}, locationRestricted={JobAnalysis.AnalyzeRemoteLocation(aecom, "Remote", []).IsRestricted}/{JobAnalysis.AnalyzeRemoteLocation(aecom, "Remote", []).Category}.");

    const string rtx = """
        <p>This is a remote position. If you live within a reasonable commute of a company site, your manager will discuss whether there is onsite presence associated with this role.</p>
        <p>U.S. citizenship is required, as only U.S. citizens are authorized to access program information.</p>
        """;
    var rtxRemote = RemoteAnalysis(rtx);
    Assert(rtxRemote.ConcernLevel == "questionable" &&
           rtxRemote.Signals.Any(signal => signal.Category == "commuting-area") &&
           new WorkAuthorizationDetector().Analyze(rtx).Eligibility == "usCitizen",
        "The sanitized RTX remote/citizenship fixture regressed.");
    return Task.CompletedTask;
}

static async Task TestWorkflowStateTransitionsAsync()
{
    var directory = TestDirectory("workflow-transitions");
    try
    {
        var (catalog, state, job) = await CreateCatalogAsync(directory);
        Assert(catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Normal,
            "A new job did not begin in Normal state.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Saved,
            "Normal -> Saved did not update the canonical state.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied,
            "Saved -> Applied did not update the canonical state.");
        Assert(!await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied,
            "An invalid Applied -> Saved transition was accepted.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Hidden) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Hidden,
            "Applied -> Hidden did not update the canonical state.");
        Assert(!await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied),
            "An invalid Hidden -> Applied transition was accepted.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Normal) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Normal,
            "Hidden -> Normal restore did not update the canonical state.");

        var persisted = (await state.LoadJobHistoryAsync()).Jobs[job.StableId];
        Assert(persisted.WorkflowState == JobWorkflowStates.Normal &&
               !persisted.Dismissed && !persisted.Saved && !persisted.Applied &&
               persisted.DismissedAt is null && persisted.SavedAt is null && persisted.AppliedAt is null,
            "Persistence retained an independent legacy state alongside the canonical state.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestWorkflowStateRoundTripAsync()
{
    var directory = TestDirectory("workflow-roundtrip");
    try
    {
        var (first, _, job) = await CreateCatalogAsync(directory);
        await first.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied);
        var (restarted, _, _) = await CreateCatalogAsync(directory);
        Assert(restarted.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied,
            "Applied state did not survive catalog reinitialization.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestWorkflowStateWorkspaceIsolationAsync()
{
    var firstDirectory = TestDirectory("workflow-workspace-a");
    var secondDirectory = TestDirectory("workflow-workspace-b");
    try
    {
        var (first, _, job) = await CreateCatalogAsync(firstDirectory);
        await first.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved);
        var (second, _, _) = await CreateCatalogAsync(secondDirectory);
        Assert(first.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Saved &&
               second.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Normal,
            "Workflow state crossed workspace storage boundaries.");
    }
    finally
    {
        if (Directory.Exists(firstDirectory)) Directory.Delete(firstDirectory, recursive: true);
        if (Directory.Exists(secondDirectory)) Directory.Delete(secondDirectory, recursive: true);
    }
}

static async Task TestWorkflowStateCompanyIsolationAsync()
{
    var directory = TestDirectory("workflow-company");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        var history = new JobHistoryDocument(4, new Dictionary<string, JobHistoryEntry>
        {
            ["leidos:REQ-SAME"] = new("REQ-SAME", "/leidos/job", now, now, true,
                WorkflowState: JobWorkflowStates.Saved, WorkflowStateChangedAt: now,
                CompanyId: "leidos"),
            ["boeing:REQ-SAME"] = new("REQ-SAME", "/boeing/job", now, now, true,
                WorkflowState: JobWorkflowStates.Applied, WorkflowStateChangedAt: now,
                CompanyId: "boeing")
        });
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, history);
        var state = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);
        var loaded = await state.LoadJobHistoryAsync();
        Assert(loaded.Jobs["leidos:REQ-SAME"].WorkflowState == JobWorkflowStates.Saved &&
               loaded.Jobs["boeing:REQ-SAME"].WorkflowState == JobWorkflowStates.Applied,
            "Same requisition text cross-mapped workflow state between companies.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacySavedHistoryMigrationAsync()
{
    var directory = TestDirectory("workflow-legacy-saved");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, new JobHistoryDocument(3,
            new Dictionary<string, JobHistoryEntry>
            {
                ["REQ-LEGACY"] = new(
                    "REQ-LEGACY", "/legacy/job", now.AddDays(-3), now, false,
                    CompanyId: "leidos", Saved: true, SavedAt: now.AddHours(-1))
            }));
        var appState = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var migrated = await appState.LoadJobHistoryAsync();
        var entry = migrated.Jobs["leidos:REQ-LEGACY"];
        Assert(migrated.SchemaVersion == 4 && entry.WorkflowState == JobWorkflowStates.Saved,
            "Legacy Saved state did not migrate to canonical Saved.");
        Assert(!entry.HasBeenViewed && entry.FirstSeenAt == now.AddDays(-3) && entry.LastSeenAt == now,
            "Migration changed existing NEW/viewed history timestamps.");
        Assert(!entry.Dismissed && !entry.Saved && !entry.Applied &&
               entry.DismissedAt is null && entry.SavedAt is null && entry.AppliedAt is null,
            "Legacy state fields survived schema-4 migration.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacyCombinationMigrationAsync()
{
    var directory = TestDirectory("workflow-legacy-combinations");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, new JobHistoryDocument(3,
            new Dictionary<string, JobHistoryEntry>
            {
                ["leidos:REQ-HIDDEN"] = new(
                    "REQ-HIDDEN", "/hidden/job", now, now, true,
                    Dismissed: true, DismissedAt: now, CompanyId: "leidos",
                    Saved: true, SavedAt: now, Applied: true, AppliedAt: now),
                ["leidos:REQ-APPLIED"] = new(
                    "REQ-APPLIED", "/applied/job", now, now, true,
                    CompanyId: "leidos", Saved: true, SavedAt: now,
                    Applied: true, AppliedAt: now),
                ["leidos:REQ-STALE-TIMESTAMP"] = new(
                    "REQ-STALE-TIMESTAMP", "/normal/job", now, now, true,
                    CompanyId: "leidos", SavedAt: now)
            }));
        var appState = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var migrated = await appState.LoadJobHistoryAsync();
        Assert(migrated.Jobs["leidos:REQ-HIDDEN"].WorkflowState == JobWorkflowStates.Hidden,
            "Dismissed+Saved+Applied did not migrate to the single safe Hidden state.");
        Assert(migrated.Jobs["leidos:REQ-APPLIED"].WorkflowState == JobWorkflowStates.Applied,
            "Saved+Applied did not migrate to the single Applied state.");
        Assert(migrated.Jobs["leidos:REQ-STALE-TIMESTAMP"].WorkflowState == JobWorkflowStates.Normal,
            "A stray legacy timestamp changed the canonical Normal state.");
        Assert(migrated.Jobs.Values.All(entry =>
                !entry.Dismissed && !entry.Saved && !entry.Applied &&
                entry.DismissedAt is null && entry.SavedAt is null && entry.AppliedAt is null),
            "An invalid independent-state combination survived migration.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static RemoteWorkAnalysis RemoteAnalysis(string html) => new RemoteWorkDetector().Analyze(
    "Software Engineer", "Remote / Teleworker US", [], html);

static JobSourceClient CreateSourceClient(HttpClient httpClient)
{
    var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    return new JobSourceClient(
        httpClient,
        Options.Create(new JobSourceOptions()),
        NullLogger<JobSourceClient>.Instance,
        credentials,
        new AcademicQualificationDetector(),
        new WorkAuthorizationDetector(),
        new RemoteWorkDetector());
}

static string TestDirectory(string purpose) =>
    Path.Combine(Path.GetTempPath(), $"job-search-manager-{purpose}-{Guid.NewGuid():N}");

static async Task<(JobCatalog Catalog, AppStateStore State, JobRecord Job)> CreateCatalogAsync(
    string directory)
{
    var companyCatalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
    var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companyCatalog, store);
    var job = new JobRecord(
        "Test Engineer",
        "REQ-SAVED",
        new DateOnly(2026, 8, 15),
        "Posted Today",
        "Remote / Teleworker US",
        [],
        "Full time",
        "https://leidos.wd5.myworkdayjobs.com/en-US/External/job/REQ-SAVED",
        "<p>Fully remote software engineering role.</p>",
        null,
        null,
        "unknown",
        "not-found",
        false,
        null,
        null,
        null,
        "/job/REQ-SAVED",
        CompanyId: "leidos");
    var query = new JobSourceQuery(
        "bc33aa3152ec42d4995f4791a106ed09",
        "United States of America",
        false,
        true,
        []);
    await state.SaveJobsCacheAsync([job], DateTimeOffset.UtcNow, 0, query);

    var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var academics = new AcademicQualificationDetector();
    var authorization = new WorkAuthorizationDetector();
    var remote = new RemoteWorkDetector();
    var client = new JobSourceClient(
        new HttpClient(),
        Options.Create(new JobSourceOptions()),
        NullLogger<JobSourceClient>.Instance,
        credentials,
        academics,
        authorization,
        remote);
    var catalog = new JobCatalog(
        client,
        state,
        NullLogger<JobCatalog>.Instance,
        credentials,
        academics,
        authorization,
        remote,
        companyCatalog);
    await catalog.InitializeAsync(query);
    return (catalog, state, job);
}

static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
    new ConfigurationBuilder().AddInMemoryCollection(values).Build();

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

internal sealed record TestDocument(string Name, int Value);

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, string> responseFactory)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(
                responseFactory(request),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        return Task.FromResult(response);
    }
}

internal sealed class TestHostEnvironment(string contentRootPath) : Microsoft.Extensions.Hosting.IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "JobSearchManager.Tests";
    public string ContentRootPath { get; set; } = contentRootPath;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
}
