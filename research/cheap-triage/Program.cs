using System.Text.Json;
using JobSearchManager;

if (args is ["--human-review-backup", var reviewSource, var reviewDestination])
{
    CheapTriageHumanReview.Backup(reviewSource, reviewDestination);
    Console.WriteLine("Human review SQLite backup verified.");
    return;
}

if (args.Length == 4 && args[0] == "--cheap-triage" && args[1] == "evaluate")
{
    var cheapRules = CheapRejectRules.Load(Path.GetFullPath(args[2]));
    foreach (var line in File.ReadLines(Path.GetFullPath(args[3])))
    {
        using var row = JsonDocument.Parse(line);
        var value = row.RootElement;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            id = value.GetProperty("id").GetString(),
            result = cheapRules.Decide(value.GetProperty("title").GetString() ?? "",
                value.GetProperty("body").GetString() ?? "")
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    return;
}

if (args.Length == 3 && args[0] == "--triage-holdout")
{
    var service = new TriageEvaluationService(Path.GetFullPath(args[2]));
    object result = args[1] switch
    {
        "freeze" => await service.FreezeReferenceAsync(),
        "evaluate" => await service.RunAsync(),
        _ => throw new ArgumentException("Choose freeze or evaluate.")
    };
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return;
}
Console.Error.WriteLine("Research only: --cheap-triage evaluate <ruleset.json> <corpus.jsonl> | --human-review-backup <source.db> <backup.db> | --triage-holdout <freeze|evaluate> <evaluation-directory>");
Environment.ExitCode = 2;
