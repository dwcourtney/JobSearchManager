using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearchManager;

internal static class JsonAuthorityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static ConceptRuleSnapshot Snapshot() => ConceptRuleSnapshot.Load(
        Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json"), JobConceptCatalog.LoadDefault());
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

    internal static async Task RunAsync()
    {
        var snapshot = Snapshot(); var catalog = JobConceptCatalog.LoadDefault();
        var service = new SemanticClassificationService(catalog, snapshot.Matcher);
        Require(snapshot.CandidatePipelineFingerprint == "348080299ffe13518d29668fb8da61ce0ff2ee2215c6ef802a5b60ecc2d55f70", "Phase 2 identity changed");
        Require(snapshot.PipelineFingerprint != snapshot.CandidatePipelineFingerprint && snapshot.PipelineFingerprint == "0bc0baf2fc1ac410f605eb177b6d52bb6d804292cdc2c442405a2078a4cad76b", "Production identity must bind input/factual contracts");
        var job = new JobRecord("Software Engineer", "json-unit", null, "", "Remote", [], "Full time", "https://example.test/job",
            "<p>Build APIs. Occasional travel.</p>", null, null, "unknown", "not-found", false, null, null, null, "/job");
        job = job with { RemoteWork = new RemoteWorkDetector().Analyze(job.Title,job.PrimaryLocation,[],job.DescriptionHtml),
            ExtendedLocationRequirement = new ExtendedLocationRequirementDetector().Analyze(job.Title,job.PrimaryLocation,[],job.DescriptionHtml) };
        var result = (await service.ClassifyAsync(job)).Classification ?? throw new InvalidOperationException("No JSON classification");
        Require(result.ModelTag == "json-regex-v1", "Normal production authority tag");
        var complete = job with { SemanticClassification = result, SemanticClassificationStatus = "complete" };
        Require(service.IsCurrent(complete), "New classification is not current");
        Require(!service.CanPersist(job, result with { ClassifierConfigurationVersion="sqlite-regex-v1" }), "Old in-flight authority accepted");
        Require(!service.CanPersist(job, result with { ModelDigest="old-pipeline", ClassifierConfigurationFingerprint="old-pipeline" }), "Old pipeline accepted");
        foreach (var changed in new[] { job with { Title="Different title" }, job with { DescriptionHtml="<p>Other text.</p>" },
            job with { PrimaryLocation="Onsite" }, job with { AdditionalLocations=["Paris"] }, job with { CompanyId="other" },
            job with { RemoteWork=job.RemoteWork! with { Signals=[new("substantial-travel","high","reason","different evidence")] } },
            job with { ExtendedLocationRequirement=job.ExtendedLocationRequirement! with { Signals=[new("deployment","high","reason","different evidence")] } } })
        {
            Require(!service.CanPersist(changed,result), "Changed posting/fact input accepted");
            var changedResult=(await service.ClassifyAsync(changed)).Classification!;
            Require(changedResult.ClassificationFingerprint!=result.ClassificationFingerprint,"Process cache reused another input");
        }
        Require(service.IsCurrent(complete with { DetailCachedAtUtc=DateTimeOffset.UtcNow.AddDays(1), SemanticClassificationLastAttemptUtc=DateTimeOffset.UtcNow }), "Timestamps invalidate classification");
        Require(service.IsCurrent(complete with { DescriptionHtml="<div>Build APIs. Occasional travel.</div>" }), "Equivalent normalized text changed identity");
        Require(!(await service.ClassifyAsync(job with {DescriptionHtml=""})).Available, "Missing description must remain pending");
        var restarted = new SemanticClassificationService(catalog, Snapshot().Matcher);
        var persisted = JsonSerializer.Deserialize<JobRecord>(JsonSerializer.Serialize(complete,Json),Json)!;
        Require(restarted.IsCurrent(persisted), "Fresh JSON result became stale after serialization/restart");
        Require(JsonSerializer.Serialize(JobListItem.FromJob(complete,true).DetectedConcepts,Json)==
            JsonSerializer.Serialize(JobPresentation.AuthoritativeRegexDetail(complete).DetectedConcepts,Json),"List/detail diverged");
    }

    internal static async Task RetirementAsync()
    {
        var assembly = typeof(RegexSemanticClassifier).Assembly;
        Require(!assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)), "Normal assembly references SQLite tooling");
        Require(!assembly.GetTypes().Any(t => t.Name is "SqliteSemanticRuleStore" or "LegacySemanticRuleMigrator" or "RegexTelemetryFlushService" or "RegexEvaluationService" or "AiHoldoutEvaluationService" or "SemanticRulePolicy"), "Retired infrastructure remains compiled");
        var matcher = Snapshot().Matcher;
        Require(matcher.GetType().GetMethod("ReloadAsync") is null && matcher.GetType().GetMethod("FlushUsageAsync") is null, "Mutable matcher operations remain");
        var directory = Path.Combine(Path.GetTempPath(), "jsm-json-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var job = new JobRecord("Software Engineer", "audit-unit", null, "", "Remote", [], "Full time", "https://example.test/job", "<p>Build APIs.</p>", null, null, "unknown", "not-found", false, null, null, null, "/job");
            var service = new SemanticClassificationService(JobConceptCatalog.LoadDefault(), matcher);
            job = job with { SemanticClassification = (await service.ClassifyAsync(job)).Classification, SemanticClassificationStatus = "complete" };
            var path = Path.Combine(directory, "cache.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new JobsCacheDocument(5, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, [job]), Json));
            var before = await File.ReadAllTextAsync(path);
            var report = JsonSerializer.SerializeToElement(await JsonConceptAudit.RunAsync(directory), Json);
            Require(report.GetProperty("curatedExact").GetBoolean() && report.GetProperty("curatedPostings").GetInt32() == 148 && report.GetProperty("staleRecords").GetInt32() == 0, "Deployment audit changed metrics or cache identity");
            Require(await File.ReadAllTextAsync(path) == before, "Deployment audit wrote cache");
            File.Delete(path);
            try { await JsonConceptAudit.RunAsync(directory); throw new Exception("Empty benchmark accepted"); }
            catch (InvalidDataException) { }
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static Task ReportsAsync()
    {
        var snapshot=Snapshot();var directory=Path.Combine(AppContext.BaseDirectory,"evaluation","concept-detection");
        var reports=new ConceptEvaluationReports(directory);
        var view=JsonSerializer.SerializeToElement(reports.View(snapshot),Json);
        Require(view.GetProperty("reports").EnumerateArray().All(r=>r.GetProperty("status").GetString()=="CURRENT"),"Packaged current reports not current");
        foreach(var item in view.GetProperty("reports").EnumerateArray())
        {
            var report=item.GetProperty("artifact");var role=item.GetProperty("role").GetString()!;
            var dataset=report.GetProperty("datasetFingerprint").GetString()!;var reference=report.GetProperty("referenceFingerprint").GetString()!;
            foreach(var field in new[]{"datasetFingerprint","referenceFingerprint","metricImplementationHash"})
            {
                var changed=JsonNode.Parse(report.GetRawText())!;changed[field]="different";
                Require(ConceptEvaluationReports.Status(JsonSerializer.SerializeToElement(changed),role,dataset,reference,snapshot)=="STALE","Mismatched "+field+" marked current");
            }
            var authority=JsonNode.Parse(report.GetRawText())!;authority["authority"]!["pipelineFingerprint"]="old-sqlite";
            Require(ConceptEvaluationReports.Status(JsonSerializer.SerializeToElement(authority),role,dataset,reference,snapshot)=="STALE","Old authority marked current");
            Require(ConceptEvaluationReports.Status(report,"historical",dataset,reference,snapshot)=="HISTORICAL","Historical evidence marked current");
        }
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..",".."));
        if(File.Exists(Path.Combine(root,"migration/concept-rules/phase3/evaluated-source/RegexEvaluation.cs")))
        {
            var metric=string.Concat(new[]{"RegexEvaluation.cs","AiHoldoutEvaluation.cs"}.Select(p=>File.ReadAllText(Path.Combine(root,"migration/concept-rules/phase3/evaluated-source",p)).Replace("\r\n","\n",StringComparison.Ordinal)));
            Require(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(metric)))==ConceptEvaluationReports.MetricImplementationHash,"Metric implementation changed without report identity update");
        }
        return Task.CompletedTask;
    }
}
