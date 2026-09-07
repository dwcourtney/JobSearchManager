using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using JobSearchManager;

await TestClientAsync();
await TestLlmHoldoutEvaluationAsync();
await TestLlmFixtureMetricsAsync();
Console.WriteLine("All 3 Qwen research contract/holdout/identity tests passed.");

static async Task TestClientAsync()
{
    var catalog = JobConceptCatalog.LoadDefault();
    var calls = 0;
    var client = new ClassifierClient(new HttpClient(new StubHttpMessageHandler(request => {
        calls++;
        Assert(request.RequestUri?.AbsolutePath == "/deep-analyze", "Wrong research endpoint.");
        Assert(request.Content?.Headers.ContentLength > 0 && request.Headers.TransferEncodingChunked is not true, "Request must be length-delimited.");
        var value = JsonSerializer.Deserialize<ClassifierRequest>(request.Content!.ReadAsStringAsync().Result, ClassifierClient.JsonOptions)!;
        return QwenPayload(catalog,value.JobId,value.Title,value.Description);
    })) { BaseAddress = new Uri("http://deep-analysis:8081/") }, catalog, NullLogger<ClassifierClient>.Instance);
    var result = await client.DeepAnalyzeAsync(new("fixture", "Backend engineer", "Build APIs."));
    Assert(calls == 1 && result?.Predictions.Count == 85, "Research client contract changed.");
}
static async Task TestLlmHoldoutEvaluationAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "jsm-llm-holdout-" + Guid.NewGuid().ToString("N"));
    var evaluationDirectory = Path.Combine(directory, "evaluation");
    Directory.CreateDirectory(evaluationDirectory);
    try
    {
        static string Hash(string value) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
        var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var samplingRunId = "holdout-test-llm-200";
        var examples = Enumerable.Range(0, 200).Select(index =>
        {
            var id = $"llm-{index:D3}";
            var title = $"Fixture role {index:D3}";
            var description = $"<p>Fixture posting {index:D3} with assigned responsibilities.</p>";
            return new EvaluationExample(id, EvaluationDatasetRoles.ProductionHoldout,
                samplingRunId, null, "unresolved", "unlabeled", false, false, null, null,
                Hash($"{title}\n{description}"), "fixture", id, title, description,
                $"https://example.test/{id}", null, true);
        }).ToArray();
        var sampleFingerprint = Hash(string.Join("\n", examples.Select(item =>
            $"{item.EvaluationExampleId}|{item.PostingContentHash}")));
        var holdout = new HoldoutSampleDocument(1, EvaluationDatasetRoles.ProductionHoldout,
            "frozen-unlabeled", samplingRunId, DateTimeOffset.UtcNow,
            new HoldoutSamplingPlan(1, "Deterministic LLM test population.", null, null, null,
                "active-and-inactive", 200, "simple-random", 20260902),
            Hash("plan"), Hash("population"), 200, sampleFingerprint,
            "Label independently without detector output.", examples);
        await ProductionHoldoutSampler.WriteAtomicallyAsync(holdout,
            Path.Combine(evaluationDirectory, "holdout.json"));

        var completed = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
        var reviewerA = new AiReviewerIdentity("Labeler A", "Codex", "isolated",
            "a-v1", Hash("a"), completed);
        var reviewerB = new AiReviewerIdentity("Labeler B", "Codex", "isolated",
            "b-v1", Hash("b"), completed);
        var adjudicator = new AiReviewerIdentity("Adjudicator", "Codex", "isolated",
            "c-v1", Hash("c"), completed);
        var decisions = examples.SelectMany(example => catalog.Concepts.Select(concept =>
            new AiReferenceDecision(example.EvaluationExampleId, example.PostingContentHash,
                concept.Id, AiReferenceJudgments.Absent, AiReferenceJudgments.Absent, true,
                null, AiReferenceJudgments.Absent,
                "prediction-blinded-codex-a-b-agreement", false, false))).ToArray();
        var references = new AiReferenceDataset(1, samplingRunId + "-ai-reference-v1",
            EvaluationDatasetRoles.ProductionHoldout, "AI-ADJUDICATED PRODUCTION HOLDOUT",
            "frozen-ai-adjudicated-reference-labels", completed, sampleFingerprint,
            samplingRunId, 20260902, 200, 85, 17_000, catalog.Fingerprint, catalog.Version,
            reviewerA, reviewerB, adjudicator, 17_000, 0, 0, 0,
            AiHoldoutEvaluationService.ExactDisclaimer, decisions);
        references = references with
        {
            ReferenceDatasetFingerprint =
                AiHoldoutEvaluationService.CalculateReferenceFingerprint(references)
        };
        await File.WriteAllTextAsync(Path.Combine(evaluationDirectory, "ai-reference-labels-v1.json"),
            JsonSerializer.Serialize(references, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { WriteIndented = true }));

        using var store = new SqliteSemanticRuleStore(Path.Combine(directory, "regex-rules.db"), catalog);
        store.Initialize(RepositoryAsset("LegacyJobConceptRules.json"));
        var noPredictions = examples.ToDictionary(item => item.EvaluationExampleId,
            _ => (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var baselineMetrics = HoldoutMetricCalculator.Calculate(catalog, references, noPredictions);
        var agreement = new AiLabelingAgreement(17_000, 17_000, 0, 0, 0, 0,
            baselineMetrics.Concepts, []);
        var baseline = new AiHoldoutEvaluationReport("regex-baseline", completed,
            references.DatasetId, EvaluationDatasetRoles.ProductionHoldout,
            references.DatasetDisplayName, "fixture", sampleFingerprint,
            references.ReferenceDatasetFingerprint, "fixture-rules", catalog.Fingerprint,
            catalog.Version, "fixture-config", samplingRunId, "simple-random", 20260902,
            200, "AI-adjudicated reference labels; not human ground truth",
            AiHoldoutEvaluationService.ExactDisclaimer, reviewerA, reviewerB, adjudicator,
            agreement, 200, 17_000, 17_000, 0, 0, 17_000, 0, 1,
            baselineMetrics.Macro, baselineMetrics.Micro, baselineMetrics.Concepts,
            "one point", "scored", null);
        var requests = 0;
        var injectTechnicalFailure = true;
        async Task<QwenDeepAnalysis?> Predict(ClassifierRequest request, CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            var requestNumber = Interlocked.Increment(ref requests);
            if (requestNumber == 4 && injectTechnicalFailure)
            {
                injectTechnicalFailure = false;
                return null;
            }
            var contentHash = SemanticRulesetFingerprint.PostingContentHash(
                request.Title, request.Description);
            return new QwenDeepAnalysis(contentHash, QwenDeepAnalysisContract.ModelId,
                QwenDeepAnalysisContract.ModelTag, QwenDeepAnalysisContract.ModelDigest,
                catalog.Version, catalog.Fingerprint, QwenDeepAnalysisContract.PromptVersion,
                QwenDeepAnalysisContract.PromptHash,
                QwenDeepAnalysisContract.ClassificationFingerprint(contentHash, catalog),
                completed, catalog.Concepts.Select(item =>
                    new SemanticConceptPrediction(item.Id, false)).ToArray(), "fixture",
                new QwenInferenceMetrics(2_000_000, 0, 100, 1_000_000, 20, 1_000_000,
                    20, 2_500_000_000, 2_400_000_000, 50_000_000));
        }
        var service = new LlmHoldoutEvaluationService(evaluationDirectory, Predict, store,
            catalog, () => baseline);
        await AssertThrowsAsync<InvalidDataException>(() => service.RunAsync());
        var checkpoint = JsonSerializer.Deserialize<IReadOnlyList<LlmHoldoutPredictionItem>>(
            await File.ReadAllBytesAsync(Path.Combine(evaluationDirectory,
                "llm-predictions-progress-v2.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert(checkpoint?.Count == 3 &&
               service.GetStatus().State == LlmHoldoutEvaluationStates.Failed,
            "A technical evaluator failure did not preserve the validated checkpoint and durable failed state.");
        service = new LlmHoldoutEvaluationService(evaluationDirectory, Predict, store,
            catalog, () => baseline);
        var first = await service.RunAsync();
        var frozenPath = Path.Combine(evaluationDirectory, "llm-predictions-v2.json");
        var frozenBefore = await File.ReadAllBytesAsync(frozenPath);
        var second = await service.RunAsync();
        var frozenAfter = await File.ReadAllBytesAsync(frozenPath);
        var preflight = await LlmTechnicalPreflight.RunAsync(evaluationDirectory,
            Predict, catalog);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LlmEvaluationRunDetails;";
        var ledgerCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert(requests == 203 && frozenBefore.SequenceEqual(frozenAfter) &&
               first.PredictionFingerprint == second.PredictionFingerprint &&
               first.EligibleConceptDecisions == 17_000 && first.PostingCount == 200 &&
               first.LlmMacro == first.RegexMacro && first.LlmMicro == first.RegexMicro &&
               first.Runtime.ApproximateInferenceCount == 200 && ledgerCount == 2 &&
               preflight.Status == "passed" && preflight.CompleteStructuredOutput &&
               preflight.StablePredictions && preflight.BoundedOutput &&
               preflight.CheckpointRoundTrip && preflight.OutputTokenCounts.Count == 2 &&
               preflight.SemanticDisagreementCount == 0 &&
               service.GetStatus().State == LlmHoldoutEvaluationStates.Complete,
            "The LLM holdout did not freeze exactly one prediction set before common scoring and ledger persistence.");
    }
    finally
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestLlmFixtureMetricsAsync()
{
    var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    Assert(catalog.Concepts.Count == 85 &&
           catalog.Concepts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == 85 &&
           catalog.Concepts.All(item => !string.IsNullOrWhiteSpace(item.Definition)),
        "The canonical semantic taxonomy did not preserve 85 unique, defined concepts.");
    Assert(catalog.Fingerprint == "514ed1c8c644d1eec426b5fdcf4d5a2c447aa61ce5572ae70b2d03fc3815a049" &&
           QwenDeepAnalysisContract.PromptVersion == "job-fit-85-compact-json-v2" &&
           QwenDeepAnalysisContract.OutputContractVersion == "compact-85-boolean-map-v2",
        "The canonical taxonomy or opt-in LLM prompt identity changed without an explicit version update.");
    return Task.CompletedTask;
}

static string QwenPayload(
    JobConceptCatalog catalog, string jobId, string title, string description)
{
    var contentHash = SemanticRulesetFingerprint.PostingContentHash(title, description);
    return JsonSerializer.Serialize(new QwenDeepAnalysisResponse(
        true, jobId, title, contentHash, QwenDeepAnalysisContract.ModelId,
        QwenDeepAnalysisContract.ModelTag, QwenDeepAnalysisContract.ModelDigest,
        catalog.Version, catalog.Fingerprint, QwenDeepAnalysisContract.PromptVersion,
        QwenDeepAnalysisContract.PromptHash, QwenDeepAnalysisContract.OutputContractVersion,
        QwenDeepAnalysisContract.OutputSchemaHash,
        QwenDeepAnalysisContract.ClassificationFingerprint(contentHash, catalog),
        new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        catalog.Concepts.Select(item => new SemanticConceptPrediction(item.Id, true)).ToArray(),
        "Opt-in analysis.", new QwenInferenceMetrics(1_000_000, 0, 100, 500_000,
            20, 500_000, 40, 2_500_000_000, 2_400_000_000, 50_000_000)),
        ClassifierClient.JsonOptions);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}


static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}


static string RepositoryAsset(string name)
{
    var outputCopy = Path.Combine(AppContext.BaseDirectory, name);
    if (File.Exists(outputCopy)) return outputCopy;
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", name));
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
