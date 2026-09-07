using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearchManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class CheapTriageShadowTests
{
    public static async Task Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jsm-shadow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var rules = CheapRejectRules.Load(Path.Combine(AppContext.BaseDirectory, CheapRejectRules.DefaultPath));
            var off = Service(rules, "Off");
            Check(!off.Enabled, "Off default/configuration");
            Check(new CheapTriageShadow(rules, new ConfigurationBuilder().Build()).Mode == "Off", "Default Off");
            try { Service(rules, "Active"); throw new Exception("Active accepted"); }
            catch (InvalidDataException) { }
            var shadow = Service(rules, "Shadow");
            var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
            var storage = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
            var store = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, storage);
            var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
            var academic = new AcademicQualificationDetector(); var authorization = new WorkAuthorizationDetector();
            var remote = new RemoteWorkDetector(); var options = Options.Create(new JobSourceOptions());
            var http = new NoProviderRequests();
            var client = new JobSourceClient(new HttpClient(http), options, NullLogger<JobSourceClient>.Instance,
                credentials, academic, authorization, remote, new ExtendedLocationRequirementDetector());
            var coordinator = new SharedSourceRefreshCoordinator();
            JobCatalog Catalog(CheapTriageShadow service) => new(client, store, NullLogger<JobCatalog>.Instance,
                credentials, academic, authorization, remote, companies, options, coordinator, cheapTriage: service);
            var query = new JobSourceQuery("bc33aa3152ec42d4995f4791a106ed09", "United States of America", false, true, []);
            JobRecord Job(string id, string title, string body) => client.Reclassify(new(title, id, null, "", "Remote", [],
                "Full time", "https://example.com/" + id, body, null, null, "unknown", "not-found", false,
                null, null, null, "/job/" + id));
            var jobs = new[] { Job("keep", "Software Developer", "Develop C# applications."),
                Job("reject", "Registered Nurse", "Provide nursing and patient care in a hospital."),
                Job("title", "Registered Nurse", ""), Job("limit", new string('x', 10001), "") };
            await store.SaveJobsCacheAsync(jobs, DateTimeOffset.UtcNow, 0, query);
            var offCatalog = Catalog(off); await offCatalog.InitializeAsync(query);
            await offCatalog.WaitForCheapTriageAsync();
            Check(offCatalog.GetCheapTriageReport().AnalyzedJobs == 0 &&
                (await store.LoadJobsCacheAsync(query))!.Jobs.All(job => job.CheapTriage is null), "Off performs no evaluation/write");
            var beforeHistory = JsonSerializer.Serialize(await store.LoadJobHistoryAsync());
            var catalog = Catalog(shadow); await catalog.InitializeAsync(query); await catalog.WaitForCheapTriageAsync();
            var report = catalog.GetCheapTriageReport();
            Check(report.AnalyzedJobs == 4 && report.Keep == 2 && report.Reject == 1 && report.Undetermined == 1,
                "Normal cache hydration gets live KEEP/REJECT/UNDETERMINED");
            Check(report.TitleOnly == 2 && report.DescriptionBacked == 2 && report.RejectionPercentage == 25,
                "Live metric cohorts");
            Check(report.RequiringReconciliation == 0 && report.LatestAnalysisUtc is not null, "Current reconciliation status");
            var persisted = (await store.LoadJobsCacheAsync(query))!;
            foreach (var job in persisted.Jobs)
            {
                Check(shadow.IsCurrent(job), "Persisted hash/version/input");
                var original = jobs.Single(item => item.StableId == job.StableId);
                Check(JsonSerializer.Serialize(job with { CheapTriage = null }) == JsonSerializer.Serialize(original),
                    "Shadow changed a Job Fit or source field: " + job.StableId);
            }
            var visible = await catalog.GetListSnapshotAsync();
            Check(visible.Jobs.Count == 4 && visible.Jobs.Any(job => job.RequisitionId == "reject"), "REJECT remains visible/searchable");
            Check(JsonSerializer.Serialize(await store.LoadJobHistoryAsync()) == beforeHistory, "Shadow changed workflow history");
            var rejects = report.RejectedJobs;
            Check(rejects.Count == 1 && rejects[0].WorkflowState == "normal" && rejects[0].Current &&
                rejects[0].Observation.Result.Evidence.Count > 0, "Review set includes reasons/evidence/workflow");
            var beforeSaved = persisted.SavedAtUtc;
            await catalog.GetListSnapshotAsync(); await catalog.WaitForCheapTriageAsync();
            Check((await store.LoadJobsCacheAsync(query))!.SavedAtUtc == beforeSaved, "No repeated cache writes");
            var second = Catalog(shadow); await second.InitializeAsync(query); await second.WaitForCheapTriageAsync();
            Check((await store.LoadJobsCacheAsync(query))!.SavedAtUtc == beforeSaved, "Shared-cache reuse across catalogs");
            var detailed = await catalog.GetJobDetailAsync("leidos:reject"); await catalog.WaitForCheapTriageAsync();
            Check(detailed is not null && detailed.CheapTriage?.Decision == "REJECT", "REJECT detail still accessible");
            Check(http.Count == 0, "Shadow or described detail caused provider requests");

            // A cached body arrival changes only triage's relevant input. Off retains but marks stale.
            var changed = (await store.LoadJobsCacheAsync(query))!;
            await store.SaveJobsCacheAsync(changed.Jobs.Select(job => job.RequisitionId == "title"
                ? job with { DescriptionHtml = "Provide nursing and patient care in a hospital." } : job).ToArray(),
                changed.LastRefreshedUtc!.Value, 0, query);
            await offCatalog.GetListSnapshotAsync();
            Check(offCatalog.GetCheapTriageDiagnostic("leidos:title")?.Current == false, "Off discloses stale input");
            await catalog.GetListSnapshotAsync(); await catalog.WaitForCheapTriageAsync();
            Check(catalog.GetCheapTriageDiagnostic("leidos:title") is { Current: true, Observation.Decision: "REJECT" },
                "Body arrival reconciles without provider requests");
            var revised = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, CheapRejectRules.DefaultPath)))!;
            revised["rulesetVersion"] = "1.0.1";
            var newRules = CheapRejectRules.Parse(JsonSerializer.SerializeToUtf8Bytes(revised));
            var revisedOff = Catalog(Service(newRules, "Off")); await revisedOff.InitializeAsync(query);
            Check(revisedOff.GetCheapTriageReport().StalePreviousRuleset == 4, "Old rule observations marked stale in Off");
            var restarted = Catalog(Service(newRules, "Shadow")); await restarted.InitializeAsync(query);
            await restarted.WaitForCheapTriageAsync();
            Check(restarted.GetCheapTriageReport().RequiringReconciliation == 0 &&
                (await store.LoadJobsCacheAsync(query))!.Jobs.All(job => job.CheapTriage!.Result.RulesetVersion == "1.0.1"),
                "Ruleset change reconciles cached jobs");
            Check(http.Count == 0, "Reconciliation downloaded a provider");

            // Real normal Job Fit classification still runs for BOTH a KEEP and a REJECT.
            var concepts = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
            using var semanticStore = new SqliteSemanticRuleStore(Path.Combine(directory, "rules.db"), concepts);
            semanticStore.Initialize(Path.Combine(AppContext.BaseDirectory, "LegacyJobConceptRules.json"));
            var regex = new RegexSemanticClassifier(semanticStore, concepts); await regex.InitializeAsync();
            var semantic = new SemanticClassificationService(concepts, regex);
            var normal = new JobCatalog(client, store, NullLogger<JobCatalog>.Instance, credentials, academic,
                authorization, remote, companies, options, coordinator, semantic, Service(newRules, "Shadow"));
            await normal.InitializeAsync(query); await normal.WaitForCheapTriageAsync();
            foreach (var id in new[] { "keep", "reject" })
            {
                var detail = await normal.GetJobDetailAsync("leidos:" + id);
                Check(detail is not null && semantic.IsCurrent(detail) && detail.SemanticClassification!.Predictions.Count == 85,
                    "Shadow blocked normal concept detection for " + id);
                var expected = await semantic.ClassifyAsync(detail! with { CheapTriage = null });
                Check(JsonSerializer.Serialize(detail!.SemanticClassification) == JsonSerializer.Serialize(expected.Classification),
                    "Shadow changed Job Fit classification for " + id);
            }
            for (var attempt = 0; normal.GetSemanticClassificationStatus().Running && attempt < 500; attempt++)
                await Task.Delay(10);
            Check(!normal.GetSemanticClassificationStatus().Running, "Normal Job Fit worker did not finish");
            await normal.WaitForCheapTriageAsync();
            Check(http.Count == 0, "Normal deterministic Job Fit called an external model/provider");

            // Pause persistence, change content + another analysis field, then let the old observation finish.
            var sourceKey = $"{query.CompanyId}:{store.QueryFingerprint(query)}";
            var lease = await coordinator.AcquireAsync(sourceKey);
            var oldRulesCatalog = Catalog(shadow);
            await oldRulesCatalog.InitializeAsync(query);
            var concurrent = (await store.LoadJobsCacheAsync(query))!;
            await store.SaveJobsCacheAsync(concurrent.Jobs.Select(job => job.RequisitionId == "reject"
                ? job with { Title = "Software Developer", DetailError = "concurrent-marker" } : job).ToArray(),
                concurrent.LastRefreshedUtc!.Value, 0, query);
            await lease.DisposeAsync(); await oldRulesCatalog.WaitForCheapTriageAsync();
            var raced = (await store.LoadJobsCacheAsync(query))!.Jobs.Single(job => job.RequisitionId == "reject");
            Check(raced.Title == "Software Developer" && raced.DetailError == "concurrent-marker" &&
                !shadow.IsCurrent(raced), "Old observation overwrote changed input/analysis or claimed current");
            await oldRulesCatalog.GetListSnapshotAsync(); await oldRulesCatalog.WaitForCheapTriageAsync();
            Check(oldRulesCatalog.GetCheapTriageDiagnostic("leidos:reject") is { Current: true, Observation.Decision: "KEEP" },
                "Concurrent content change did not converge after shared hydration");
            Check(http.Count == 0, "Race reconciliation requested provider data");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static CheapTriageShadow Service(CheapRejectRules rules, string mode) => new(rules,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["CheapTriage:Mode"] = mode }).Build());
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class NoProviderRequests : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Count); throw new Exception("Unexpected provider request"); }
    }
}
