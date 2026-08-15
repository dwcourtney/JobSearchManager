using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using WorkdayJobManager;

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
    }
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Local mode is the safe default", TestLocalDefaultAsync),
    ("Azure mode requires explicit storage configuration", TestAzureValidationAsync),
    ("Workspace identifiers are random and strictly validated", TestWorkspaceIdentityAsync),
    ("Workspace cookie has durable security settings", TestCookieOptionsAsync),
    ("Workspace cookie value is integrity-protected", TestProtectedCookieAsync),
    ("Workspace middleware preserves isolation through a protected cookie", TestWorkspaceMiddlewareAsync),
    ("Azure state changes require the exact application origin", TestOriginValidationAsync),
    ("File storage round-trips beside its configured base", TestFileStoreAsync),
    ("Blob namespaces are isolated and traversal-resistant", TestBlobNamespaceAsync),
    ("New workspace settings are neutral", TestNeutralDefaultsAsync),
    ("Work authorization detector recognizes strict U.S. citizenship", TestUsCitizenshipAsync),
    ("Work authorization detector distinguishes citizens and permanent residents", TestCitizenOrResidentAsync),
    ("Work authorization detector recognizes employment sponsorship", TestSponsorshipAsync),
    ("Work authorization detector treats U.S.-person wording conservatively", TestUsPersonAsync),
    ("Work authorization detector ignores unrelated sponsor and resident language", TestAuthorizationFalsePositivesAsync),
    ("Work authorization detector surfaces non-U.S. and export wording for review", TestInternationalAuthorizationAsync)
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
    var protector = provider.CreateProtector("WorkdayJobManager.AnonymousWorkspace.v1");
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
        WorkdayHostingMode.Azure,
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
    var directory = Path.Combine(Path.GetTempPath(), $"workday-manager-test-{Guid.NewGuid():N}");
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
    Assert(settings.MinimumSalary is null && settings.IncludeKeywords.Count == 0 &&
           settings.ExcludeKeywords.Count == 0,
        "New workspaces inherited personal search preferences.");
    Assert(settings.UserProfile?.Education.Level == "notSpecified" &&
           settings.UserProfile.Security?.ClearanceLevel == "notSpecified" &&
           settings.UserProfile.Security?.PublicTrust == "unknown" &&
           settings.UserProfile.WorkAuthorization?.UsStatus == "notSpecified" &&
           settings.UserProfile.WorkAuthorization?.Sponsorship == "unknown" &&
           !settings.HideStrictWorkAuthorizationMismatch,
        "New workspaces inherited personal qualification data.");
    return Task.CompletedTask;
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
