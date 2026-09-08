using System.Diagnostics;
using System.IO.Compression;
using JobSearchManager;
using System.Text.Json;
if (args.Length >= 3 && args[0] == "--regex-maintenance")
{
    var action = args[1];
    var databasePath = Path.GetFullPath(args[2]);
    var catalog = JobConceptCatalog.LoadDefault();
    using var store = new SqliteSemanticRuleStore(databasePath, catalog);
    store.Initialize(Path.Combine(AppContext.BaseDirectory, "LegacyJobConceptRules.json"));
    var classifier = new LegacyRegexSemanticClassifier(store, catalog);
    await classifier.InitializeAsync();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    switch (action)
    {
        case "overview":
            var rules = await store.ListRulesAsync();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                databasePath,
                classifier.RulesetFingerprint,
                classifier.ActiveRuleCount,
                statuses = rules.GroupBy(item => item.Status, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
            }, jsonOptions));
            break;
        case "evaluate":
            var evaluation = new RegexEvaluationService(
                Path.Combine(AppContext.BaseDirectory, "RegexValidationCorpus.json"),
                classifier, store, catalog);
            Console.WriteLine(JsonSerializer.Serialize(await evaluation.EvaluateAsync(), jsonOptions));
            break;
        case "benchmark-cache" when args.Length == 4:
            var cacheRoot = Path.GetFullPath(args[3]);
            var cacheFiles = Directory.EnumerateFiles(cacheRoot, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var benchmarkJobs = new List<JobRecord>();
            foreach (var path in cacheFiles)
            {
                try
                {
                    var document = JsonSerializer.Deserialize<JobsCacheDocument>(
                        await File.ReadAllBytesAsync(path),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (document?.Jobs is { } jobs) benchmarkJobs.AddRange(jobs);
                }
                catch (JsonException) { }
            }
            var classifiedJobs = 0;
            var matchedConcepts = 0;
            var benchmarkTimer = Stopwatch.StartNew();
            foreach (var job in benchmarkJobs)
            {
                var description = job.DescriptionHtml;
                if (string.IsNullOrWhiteSpace(description) &&
                    !string.IsNullOrWhiteSpace(job.CompressedDescriptionHtml))
                {
                    using var input = new MemoryStream(Convert.FromBase64String(job.CompressedDescriptionHtml));
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip);
                    description = await reader.ReadToEndAsync();
                }
                if (string.IsNullOrWhiteSpace(description)) continue;
                var result = classifier.Classify(job.Title, description, job.RemoteWork,
                    job.ExtendedLocationRequirement, productionUsage: false);
                classifiedJobs++;
                matchedConcepts += result.Concepts.Count;
            }
            benchmarkTimer.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                cacheRoot,
                cacheFileCount = cacheFiles.Length,
                jobRecordCount = benchmarkJobs.Count,
                classifiedJobs,
                matchedConcepts,
                elapsedMilliseconds = benchmarkTimer.Elapsed.TotalMilliseconds,
                millisecondsPerJob = classifiedJobs == 0 ? 0 :
                    benchmarkTimer.Elapsed.TotalMilliseconds / classifiedJobs,
                jobsPerSecond = benchmarkTimer.Elapsed.TotalSeconds == 0 ? 0 :
                    classifiedJobs / benchmarkTimer.Elapsed.TotalSeconds,
                classifier.RulesetFingerprint,
                classifier.ActiveRuleCount
            }, jsonOptions));
            break;
        case "sample-holdout" when args.Length == 6:
            var holdoutPlan = JsonSerializer.Deserialize<HoldoutSamplingPlan>(
                await File.ReadAllTextAsync(Path.GetFullPath(args[4])), jsonOptions)
                ?? throw new InvalidDataException("The holdout sampling plan is empty.");
            var holdout = await ProductionHoldoutSampler.SampleAsync(Path.GetFullPath(args[3]),
                holdoutPlan);
            await ProductionHoldoutSampler.WriteAtomicallyAsync(holdout,
                Path.GetFullPath(args[5]));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                output = Path.GetFullPath(args[5]), holdout.SamplingRunId,
                holdout.PopulationSize, sampleSize = holdout.Examples.Count,
                holdout.PlanFingerprint, holdout.PopulationFingerprint, holdout.SampleFingerprint,
                holdout.DatasetStatus
            }, jsonOptions));
            break;
        case "evaluate-ai-holdout" when args.Length == 4:
            var aiEvaluation = new AiHoldoutEvaluationService(Path.GetFullPath(args[3]),
                classifier, store, catalog);
            Console.WriteLine(JsonSerializer.Serialize(await aiEvaluation.RunAsync(), jsonOptions));
            break;
        case "reconcile-cache" when args.Length == 4:
            Console.WriteLine(JsonSerializer.Serialize(await LegacyRegexCacheReconciler.ReconcileAsync(
                Path.GetFullPath(args[3]), classifier, catalog), jsonOptions));
            break;
        case "export":
            Console.WriteLine(await store.ExportJsonAsync());
            break;
        case "import" when args.Length == 4:
            Console.WriteLine(JsonSerializer.Serialize(await store.ImportCandidatesAsync(
                await File.ReadAllTextAsync(Path.GetFullPath(args[3])), "maintenance-cli-import"),
                jsonOptions));
            break;
        case "review-stale":
            Console.WriteLine(JsonSerializer.Serialize(new
            { reviewDue = await store.MarkReviewDueAsync(DateTimeOffset.UtcNow) }, jsonOptions));
            break;
        case "retention":
            Console.WriteLine(JsonSerializer.Serialize(new
            { deleted = await store.ApplyRetiredRetentionAsync(DateTimeOffset.UtcNow) }, jsonOptions));
            break;
        case "backup" when args.Length == 4:
            await store.BackupAsync(Path.GetFullPath(args[3]));
            Console.WriteLine(JsonSerializer.Serialize(new { backup = Path.GetFullPath(args[3]) }, jsonOptions));
            break;
        default:
            Console.Error.WriteLine("Usage: --regex-maintenance <overview|evaluate|evaluate-ai-holdout|benchmark-cache|reconcile-cache|sample-holdout|export|import|review-stale|retention|backup> <regex-rules.db> [evaluation-directory|cache-root] [plan.json] [output.json]");
            Environment.ExitCode = 2;
            break;
    }
    return;
}
