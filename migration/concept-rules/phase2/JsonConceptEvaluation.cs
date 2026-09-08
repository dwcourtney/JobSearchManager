using System.Text.Json;
using JobSearchManager;

namespace JobSearchManager.Migration;

internal static class JsonConceptEvaluation
{
    internal static async Task CompareAsync(string archive,string output)
    {
        if(Directory.Exists(output))throw new InvalidOperationException("Use a new comparison directory; historical reports are never overwritten.");
        Directory.CreateDirectory(output);
        var catalog=JobConceptCatalog.LoadDefault();var candidate=JsonConceptCandidate.Load(JsonConceptTests.CandidatePath,catalog);
        var database=Path.Combine(output,"comparison-only.db");File.Copy(Path.Combine(archive,"authority.db"),database);
        // Online archives are sealed read-only. Only this newly created disposable copy
        // receives write permission so the existing evaluator can record its comparison ledger.
        if (OperatingSystem.IsWindows()) File.SetAttributes(database, FileAttributes.Normal);
        else File.SetUnixFileMode(database, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var store=new SqliteSemanticRuleStore(database,catalog);var sql=new LegacyRegexSemanticClassifier(store,catalog);await sql.InitializeAsync();
        var corpus=Path.Combine(archive,"RegexValidationCorpus.json");
        var sqliteReport=await new RegexEvaluationService(corpus,sql,store,catalog).EvaluateAsync(persist:false);
        var jsonReport=await new RegexEvaluationService(corpus,candidate.Matcher,store,catalog).EvaluateAsync(persist:false);
        RegexEvaluationReport Normalize(RegexEvaluationReport r)=>r with {EvaluationRunId="migration-comparison",EvaluatedUtc=DateTimeOffset.UnixEpoch,RulesetFingerprint="identity compared separately"};
        JsonConceptTests.Require(JsonConceptTests.Serialize(Normalize(sqliteReport))==JsonConceptTests.Serialize(Normalize(jsonReport)),"Curated per-rule/concept evaluation drift");
        File.WriteAllText(Path.Combine(output,"curated-sqlite.json"),JsonConceptTests.Serialize(sqliteReport));
        File.WriteAllText(Path.Combine(output,"curated-json.json"),JsonConceptTests.Serialize(jsonReport));
        var files=new[]{"holdout.json","ai-holdout-manifest.json","labeler-a.jsonl","labeler-b.jsonl","adjudication.jsonl","ai-reference-labels-v1.json","labeler-a-prompt.txt","labeler-b-prompt.txt","adjudicator-prompt.txt"};
        var reports=new List<AiHoldoutEvaluationReport>();
        foreach(var item in new[]{(Name:"sqlite",Matcher:(IConceptMatcher)sql),(Name:"json",Matcher:(IConceptMatcher)candidate.Matcher)})
        {
            var directory=Path.Combine(output,"holdout-"+item.Name);Directory.CreateDirectory(directory);
            foreach(var file in files)File.Copy(Path.Combine(archive,"artifacts","0",file),Path.Combine(directory,file));
            // Existing deterministic evaluator reads frozen judgments. All generated reports/status/ledger
            // writes are confined to new comparison directories and a disposable copied DB.
            var report=await new AiHoldoutEvaluationService(directory,item.Matcher,store,catalog).RunAsync();reports.Add(report);
        }
        AiHoldoutEvaluationReport NormalizeHoldout(AiHoldoutEvaluationReport r)=>r with{EvaluationRunId="migration-comparison",EvaluatedUtc=DateTimeOffset.UnixEpoch,RulesetFingerprint="identity compared separately"};
        JsonConceptTests.Require(JsonConceptTests.Serialize(NormalizeHoldout(reports[0]))==JsonConceptTests.Serialize(NormalizeHoldout(reports[1])),"Holdout confusion matrices/evaluation drift");
        var summary=new{Candidate=candidate.Identity,SqliteFingerprint=sql.RulesetFingerprint,Curated=new{sqliteReport.FixtureCount,sqliteReport.ConceptDecisionCount,sqliteReport.PositiveDecisionCount,sqliteReport.NegativeDecisionCount,sqliteReport.Macro,sqliteReport.Micro,Rules=sqliteReport.Rules.Count,Concepts=sqliteReport.Concepts.Count,RuleTimeouts=sqliteReport.Rules.Sum(r=>r.TimeoutCount),Exact=true},Holdout=new{reports[0].PostingCount,reports[0].TotalConceptDecisions,reports[0].EligibleConceptDecisions,reports[0].UnresolvedExcludedDecisions,reports[0].Macro,reports[0].Micro,Concepts=reports[0].Concepts.Count,Exact=true},ModelsInvoked=false,LabelsChanged=false,HistoricalReportsModified=false};
        File.WriteAllText(Path.Combine(output,"comparison-summary.json"),JsonConceptTests.Serialize(summary));
        Console.WriteLine(JsonConceptTests.Serialize(summary));
    }
}
