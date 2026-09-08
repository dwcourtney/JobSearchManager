using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using JobSearchManager;

// The hardware benchmark entry point deliberately runs before RegEx maintenance.
// Prediction nodes therefore never initialize or open the RegEx rule database.
if (args.Length >= 3 && args[0] == "--llm-benchmark")
{
    var action = args[1];
    var benchmarkDirectory = Path.GetFullPath(args[2]);
    var catalog = JobConceptCatalog.LoadDefault();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    try
    {
        switch (action)
        {
            case "preflight" when args.Length == 3:
            case "predict" when args.Length == 3:
                using (var benchmarkHttp = new HttpClient
                {
                    BaseAddress = new Uri(Environment.GetEnvironmentVariable("DEEP_ANALYSIS_URL")
                        ?? "http://deep-analysis:8081/"),
                    Timeout = TimeSpan.FromSeconds(300)
                })
                {
                    var client = new ClassifierClient(benchmarkHttp, catalog,
                        NullLogger<ClassifierClient>.Instance);
                    object result = action == "preflight"
                        ? await LlmTechnicalPreflight.RunAsync(benchmarkDirectory,
                            client.DeepAnalyzeAsync, catalog,
                            requireStablePredictions: false)
                        : await new LlmHardwareBenchmarkRunner(benchmarkDirectory,
                            client.DeepAnalyzeAsync, catalog).RunPredictionsAsync();
                    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
                }
                break;
            case "score" when args.Length == 4:
                Console.WriteLine(JsonSerializer.Serialize(
                    await LlmHardwareBenchmarkRunner.ScoreAsync(benchmarkDirectory,
                        Path.GetFullPath(args[3]), catalog), jsonOptions));
                break;
            default:
                Console.Error.WriteLine("Usage: --llm-benchmark <preflight|predict> <benchmark-directory> | --llm-benchmark score <benchmark-directory> <production-evaluation-directory>");
                Environment.ExitCode = 2;
                break;
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"LLM hardware benchmark failed: {exception.GetType().Name}: {exception.Message}");
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Length == 4 && args[0] == "--regex-maintenance" &&
    args[1] is "evaluate-llm-holdout" or "preflight-llm-holdout")
{
    var action = args[1];
    var catalog = JobConceptCatalog.LoadDefault();
    using var store = new SqliteSemanticRuleStore(Path.GetFullPath(args[2]), catalog);
    store.Initialize(Path.Combine(AppContext.BaseDirectory, "LegacyJobConceptRules.json"));
    var classifier = new LegacyRegexSemanticClassifier(store, catalog);
    await classifier.InitializeAsync();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    switch (action)
    {
        case "evaluate-llm-holdout" when args.Length == 4:
            using (var llmHttp = new HttpClient
            {
                BaseAddress = new Uri(Environment.GetEnvironmentVariable("DEEP_ANALYSIS_URL")
                    ?? "http://deep-analysis:8081/"),
                Timeout = TimeSpan.FromSeconds(300)
            })
            {
                var llmClient = new ClassifierClient(llmHttp, catalog,
                    NullLogger<ClassifierClient>.Instance);
                var evaluationDirectory = Path.GetFullPath(args[3]);
                var regexBaseline = new AiHoldoutEvaluationService(evaluationDirectory,
                    classifier, store, catalog);
                var llmEvaluation = new LlmHoldoutEvaluationService(evaluationDirectory,
                    llmClient.DeepAnalyzeAsync, store, catalog, regexBaseline.GetLatestReport);
                try
                {
                    Console.WriteLine(JsonSerializer.Serialize(
                        await llmEvaluation.RunAsync(), jsonOptions));
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"LLM holdout evaluation failed: {exception.GetType().Name}: {exception.Message}");
                    Environment.ExitCode = 1;
                }
            }
            break;
        case "preflight-llm-holdout" when args.Length == 4:
            using (var preflightHttp = new HttpClient
            {
                BaseAddress = new Uri(Environment.GetEnvironmentVariable("DEEP_ANALYSIS_URL")
                    ?? "http://deep-analysis:8081/"),
                Timeout = TimeSpan.FromSeconds(300)
            })
            {
                var preflightClient = new ClassifierClient(preflightHttp, catalog,
                    NullLogger<ClassifierClient>.Instance);
                try
                {
                    Console.WriteLine(JsonSerializer.Serialize(await LlmTechnicalPreflight.RunAsync(
                        Path.GetFullPath(args[3]), preflightClient.DeepAnalyzeAsync, catalog), jsonOptions));
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"LLM technical preflight failed: {exception.GetType().Name}: {exception.Message}");
                    Environment.ExitCode = 1;
                }
            }
            break;
    }
    return;
}
Console.Error.WriteLine("Research only: --llm-benchmark <preflight|predict|score> ... or --regex-maintenance <evaluate-llm-holdout|preflight-llm-holdout> <database> <evaluation-directory>");
Environment.ExitCode = 2;
