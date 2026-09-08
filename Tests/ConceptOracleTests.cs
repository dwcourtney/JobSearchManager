using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobSearchManager;

internal static class ConceptOracleTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Fingerprint = "9d491a52ed1046fa319f2b6b9f4d81048b77f9d4a6774d803a14afee5ae06d2a";
    internal sealed record Input(string Id, string Title = "", string Html = "", string PrimaryLocation = "", string[]? AdditionalLocations = null,
        RemoteWorkAnalysis? RemoteWork = null, ExtendedLocationRequirementAnalysis? ExtendedLocation = null);
    private static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Root => Path.Combine(AppContext.BaseDirectory, "concept-oracle");

    internal static async Task RunAsync()
    {
        var output = Path.Combine(Path.GetTempPath(), "jsm-concept-result-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await CompareAsync(Path.Combine(AppContext.BaseDirectory, "concept-oracle-fixtures.json"), output, "seed");
            Require(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(output + ".concepts.json").Replace("\r\n", "\n")))) == "d2e6ac2e667170078d5aa01d1a71e16a462e6d8a94cfaeedd8d9fbe3a68a7196", "Frozen complete fixture output drift");
            await SyntheticAsync();
        }
        finally { File.Delete(output); File.Delete(output + ".concepts.json"); }
    }

    internal static async Task CompareAsync(string input, string output, string database)
    {
        var catalog = JobConceptCatalog.LoadDefault();
        var frozenTaxonomy = JsonSerializer.Deserialize<JobConceptCatalogDocument>(File.ReadAllBytes(Path.Combine(Root,"JobConceptCatalog.json")),Json)!;
        Require(catalog.Version == 9 && catalog.Fingerprint == "514ed1c8c644d1eec426b5fdcf4d5a2c447aa61ce5572ae70b2d03fc3815a049", "Taxonomy drift");
        var frozenCatalog = new JobConceptCatalog(frozenTaxonomy);
        using var exportDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root,"sqlite-effective-rules.json")));
        var export = exportDocument.RootElement;
        using var archiveReference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root,"archive-reference.json")));
        Require(Hash(Path.Combine(Root,"sqlite-effective-rules.json")) == archiveReference.RootElement.GetProperty("effectiveExportSha256").GetString(), "Frozen definition bytes drift");
        var policy = export.GetProperty("policy").Deserialize<SemanticRulePolicy>(Json)!;
        Require(policy == new SemanticRulePolicy(), "Policy drift");
        Require(export.GetProperty("runtimeFingerprint").GetString() == Fingerprint, "Oracle fingerprint drift");
        var definitions = export.GetProperty("rules").Deserialize<SemanticRule[]>(Json)!;
        Require(definitions.Length == 288 && definitions.Select(r => r.ConceptId).Distinct().Count() == 85, "Definition inventory drift");
        var directory = Path.Combine(Path.GetTempPath(), "jsm-concept-oracle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var db = Path.Combine(directory,"authority.db");
            if (database != "seed") File.Copy(database,db);
            using var store = new SqliteSemanticRuleStore(db,catalog,policy);
            if (database == "seed") store.Initialize(Path.Combine(AppContext.BaseDirectory,"LegacyJobConceptRules.json"));
            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Require(snapshot.Fingerprint == Fingerprint,"Backup/seed runtime fingerprint differs");
            object Fields(SemanticRule r) => new {r.RuleId,r.ConceptId,r.Pattern,r.Scope,r.RuleType,r.ContextGroupId};
            Require(Serialize(snapshot.Rules.Select(Fields)) == Serialize(definitions.Select(Fields)),"Actual C# seed/backup definitions or SQL execution order differ from frozen export");
            var current = new RegexSemanticClassifier(store,catalog); await current.InitializeAsync();
            var oracle = new FrozenSqliteConceptOracle(new(Fingerprint,DateTimeOffset.UnixEpoch,definitions,[]),frozenCatalog,policy);
            var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllBytes(input),Json)!;
            var remote = new RemoteWorkDetector(); var extended = new ExtendedLocationRequirementDetector();
            var pairs = new List<object>(); var differences = new List<string>(); var errors = 0;
            foreach (var f in inputs)
            {
                var rw = f.RemoteWork ?? remote.Analyze(f.Title,f.PrimaryLocation,f.AdditionalLocations ?? [],f.Html);
                var el = f.ExtendedLocation ?? extended.Analyze(f.Title,f.PrimaryLocation,f.AdditionalLocations ?? [],f.Html);
                RegexClassification? old=null,actual=null; string? oldError=null,newError=null;
                try { old=oracle.Classify(f.Title,f.Html,rw,el,false) with { ClassifiedUtc=DateTimeOffset.UnixEpoch }; }
                catch(Exception ex) { oldError=ex.GetType().FullName+":"+ex.Message; }
                try { actual=current.Classify(f.Title,f.Html,rw,el,false) with { ClassifiedUtc=DateTimeOffset.UnixEpoch }; }
                catch(Exception ex) { newError=ex.GetType().FullName+":"+ex.Message; }
                if(oldError is not null || newError is not null) errors++;
                var trace=CurrentTrace(current,catalog,f.Title,f.Html);
                // Explicit 85-concept presence matrix, including absent concepts.
                var presence = catalog.Concepts.Select(c=>new {c.Id,Old=old?.Concepts.Any(x=>x.ConceptId==c.Id)==true,Current=actual?.Concepts.Any(x=>x.ConceptId==c.Id)==true}).ToArray();
                if(Serialize(old)!=Serialize(actual) || oldError!=newError || Serialize(oracle.Trace)!=Serialize(trace) || presence.Any(p=>p.Old!=p.Current)) differences.Add(f.Id);
                pairs.Add(new {f.Id,Old=old?.Concepts,Current=actual?.Concepts,OldClassification=old,CurrentClassification=actual,OldError=oldError,CurrentError=newError,Presence=presence,OldTrace=oracle.Trace.ToArray(),CurrentTrace=trace});
            }
            File.WriteAllText(output+".concepts.json",Serialize(pairs)+"\n");
            var report=new {InputSha256=Hash(input),SourceDatabase=database=="seed"?"actual C# seed migration":"private temporary copy of archived authority",ArchiveDatabaseSha256=database=="seed"?null:Hash(database),RuntimeFingerprint=snapshot.Fingerprint,TaxonomyFingerprint=catalog.Fingerprint,Policy=policy,TotalPostings=inputs.Length,ConceptDecisions=inputs.Length*85,ExactMatches=inputs.Length-differences.Count,Errors=errors,Differences=differences};
            File.WriteAllText(output,Serialize(report)+"\n");
            Require(differences.Count==0 && errors==0, "Concept oracle parity failed: "+string.Join(",",differences));
            Console.WriteLine($"Exact concept oracle parity: {inputs.Length}/{inputs.Length}; {inputs.Length*85} presence decisions; IDs/evidence/order/exclusion/context/timeouts/errors equal.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory,true); }
    }

    // Diagnostic adapter only: executes the actual compiled matcher/private FirstMatch.
    // Production does not expose veto/context outcomes. Replay is deliberately confined to tests;
    // the classifier's real returned output is independently compared above.
    private static string[] CurrentTrace(RegexSemanticClassifier classifier,JobConceptCatalog catalog,string title,string html)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var compiled=typeof(RegexSemanticClassifier).GetField("_current",flags)!.GetValue(classifier)!;
        var byConcept=(IDictionary)compiled.GetType().GetProperty("ByConcept")!.GetValue(compiled)!;
        var first=typeof(RegexSemanticClassifier).GetMethod("FirstMatch",BindingFlags.NonPublic|BindingFlags.Static)!;
        var plain=string.IsNullOrWhiteSpace(html)?"":JobAnalysis.HtmlToPlainText(html); var corpus=title+"\n"+plain;
        var trace=new List<string>();
        foreach(var concept in catalog.Concepts)
        {
            if(!byConcept.Contains(concept.Id)) continue;
            var rules=((IEnumerable)byConcept[concept.Id]!).Cast<object>().Select(c=>new {Compiled=c,Rule=(SemanticRule)c.GetType().GetProperty("Rule")!.GetValue(c)!}).ToArray();
            bool Match(object c,string text) => first.Invoke(null,[c,text,false,(Action<string>)(_=>{})]) is Match;
            var excluded=rules.Where(r=>r.Rule.RuleType=="exclusion" && Match(r.Compiled,title)).ToArray();
            trace.Add("exclusion:"+concept.Id+":"+string.Join(",",excluded.Select(r=>r.Rule.RuleId)));
            if(excluded.Length>0) continue;
            foreach(var g in rules.Where(r=>r.Rule.RuleType=="required-context").GroupBy(r=>r.Rule.ContextGroupId,StringComparer.Ordinal))
                trace.Add("context:"+concept.Id+":"+g.Key+":"+string.Join(",",g.Select(r=>r.Rule.RuleId+"="+Match(r.Compiled,r.Rule.Scope=="title"?title:r.Rule.Scope=="posting"?plain:corpus))));
        }
        return trace.ToArray();
    }

    private static async Task SyntheticAsync()
    {
        var catalog=JobConceptCatalog.LoadDefault();var directory=Path.Combine(Path.GetTempPath(),"jsm-concept-synthetic-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var store=new SqliteSemanticRuleStore(Path.Combine(directory,"rules.db"),catalog);
            store.Initialize(Path.Combine(AppContext.BaseDirectory,"LegacyJobConceptRules.json"));
            // Test-only posting scope + lookahead fallback + deterministic pathological timeout.
            foreach(var pattern in new[]{@"(?=needle)needle"})
            {
                var row=await store.CreateAsync(new("technical.software-development",pattern,"posting","positive-evidence","test-only"));
                await store.TransitionForEvaluationAsync(row.RuleId,"validated");
                await store.TransitionForEvaluationAsync(row.RuleId,"active");
                var snapshot=await store.LoadRuntimeSnapshotAsync();
                var current=new RegexSemanticClassifier(store,catalog);await current.InitializeAsync();
                var old=new FrozenSqliteConceptOracle(snapshot,catalog,store.Policy);
                foreach(var input in new[]{new Input("posting-match","","needle"),new Input("posting-title-only","needle",""),new Input("negated-first","","not needle. needle")})
                {
                    var a=old.Classify(input.Title,input.Html,null,null,false) with{ClassifiedUtc=DateTimeOffset.UnixEpoch};
                    var b=current.Classify(input.Title,input.Html,null,null,false) with{ClassifiedUtc=DateTimeOffset.UnixEpoch};
                    Require(Serialize(a)==Serialize(b),"Synthetic scope/fallback drift");
                    Require(b.MatchedRuleIds.Values.Any(ids=>ids.Contains(row.RuleId))==(input.Id!="posting-title-only"),"Posting scope expectation");
                }
            }
            var timeout=await store.CreateAsync(new("technical.software-development",@"^(?=a)(a+)+$","posting","positive-evidence","test-only"));
            await store.TransitionForEvaluationAsync(timeout.RuleId,"validated");
            await store.TransitionForEvaluationAsync(timeout.RuleId,"active");
            var live=new RegexSemanticClassifier(store,catalog);await live.InitializeAsync();var frozen=new FrozenSqliteConceptOracle(await store.LoadRuntimeSnapshotAsync(),catalog,store.Policy);
            var text=new string('a',100_000)+"!";
            var before=frozen.Classify("",text,null,null,false);var after=live.Classify("",text,null,null,false);
            Require(before.TimedOutRuleIds.Contains(timeout.RuleId)&&after.TimedOutRuleIds.Contains(timeout.RuleId),"Pathological fallback must timeout in both engines");
            Require(Serialize(before with{ClassifiedUtc=DateTimeOffset.UnixEpoch})==Serialize(after with{ClassifiedUtc=DateTimeOffset.UnixEpoch}),"Timeout result drift");
            var valid = await store.LoadRuntimeSnapshotAsync();
            var invalid = valid with { Rules = [valid.Rules[0] with { Pattern = "(", RuleType = "positive-evidence" }] };
            string Failure(Action action)
            {
                try { action(); return "no error"; }
                catch(Exception ex) { var e = ex is TargetInvocationException t ? t.InnerException! : ex; return e.GetType().FullName + ":" + e.Message; }
            }
            var oldFailure = Failure(() => _ = new FrozenSqliteConceptOracle(invalid,catalog,store.Policy));
            var currentFailure = Failure(() => typeof(RegexSemanticClassifier).GetMethod("Compile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(live,[invalid]));
            Require(oldFailure != "no error" && oldFailure == currentFailure,"Invalid regex error parity");
            Console.WriteLine("PASS synthetic posting scope, lookahead fallback, negation, bounded 100ms timeout parity");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
    }
}
