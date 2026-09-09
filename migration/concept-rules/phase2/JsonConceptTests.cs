using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearchManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace JobSearchManager.Migration;

internal static class JsonConceptTests
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static string CandidatePath => Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json");
    private static string OracleRoot => Path.Combine(AppContext.BaseDirectory, "concept-oracle");
    internal static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);
    internal static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static byte[] Bytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private static JsonObject Document() => JsonNode.Parse(File.ReadAllBytes(CandidatePath))!.AsObject();
    private static JsonObject Rule(JsonObject d, string kind) => d["rules"]!.AsArray().Select(r => r!.AsObject()).First(r => r["kind"]!.GetValue<string>() == kind);
    internal static IReadOnlyDictionary<string, byte[]> InvalidCases()
    {
        var mutations = new Dictionary<string, Action<JsonObject>>
        {
            ["schema-version"] = d => d["schemaVersion"] = 2,
            ["ruleset-version"] = d => d["rulesetVersion"] = "2.0.0",
            ["engine-contract"] = d => d["engineContract"]!["version"] = 2,
            ["duplicate-id"] = d => d["rules"]![1]!["ruleId"] = d["rules"]![0]!["ruleId"]!.DeepClone(),
            ["unknown-concept"] = d => d["rules"]![0]!["conceptId"] = "unknown.concept",
            ["duplicate-position"] = d => d["rules"]![1]!["executionOrder"] = 0,
            ["wrong-order"] = d => { d["rules"]![0]!["executionOrder"] = 1; d["rules"]![1]!["executionOrder"] = 0; },
            ["kind"] = d => d["rules"]![0]!["kind"] = "unknown",
            ["scope"] = d => d["rules"]![0]!["scope"] = "unknown",
            ["title-scope"] = d => Rule(d,"title-evidence")["scope"] = "both",
            ["regex"] = d => Rule(d,"positive-evidence")["pattern"] = "(",
            ["long-pattern"] = d => Rule(d,"positive-evidence")["pattern"] = new string('x',4097),
            ["missing-context"] = d => Rule(d,"required-context").Remove("contextGroupId"),
            ["singleton-context"] = d => Rule(d,"required-context")["contextGroupId"] = "singleton",
            ["unexpected-context"] = d => Rule(d,"positive-evidence")["contextGroupId"] = "unexpected",
            ["unknown-source"] = d => Rule(d,"remote-signal")["selector"]!["source"] = "unknown",
            ["unknown-category"] = d => Rule(d,"extended-location-signal")["selector"]!["category"] = "unknown",
            ["selector-operator"] = d => Rule(d,"remote-designation")["selector"]!["operator"] = "firstCategoryEquals",
            ["missing-selector"] = d => Rule(d,"remote-signal").Remove("selector"),
            ["regex-on-selector"] = d => Rule(d,"remote-signal")["pattern"] = ".*",
            ["selector-on-regex"] = d => Rule(d,"positive-evidence")["selector"] = Rule(d,"remote-designation")["selector"]!.DeepClone(),
            ["timeout"] = d => d["regexPolicy"]!["timeoutMilliseconds"] = 0,
            ["options"] = d => d["regexPolicy"]!["options"] = new JsonArray("IgnoreCase"),
            ["fallback"] = d => d["regexPolicy"]!["unsupportedRegexFallback"] = "unbounded",
            ["taxonomy"] = d => d["taxonomy"]!["sha256"] = new string('0',64),
            ["unknown-property"] = d => d["lifecycle"] = "active",
            ["missing-required"] = d => d.Remove("rulesetVersion"),
            ["null-rule"] = d => d["rules"]![0] = null,
            ["null-selector"] = d => Rule(d,"remote-designation")["selector"] = null,
        };
        var result = mutations.ToDictionary(p => p.Key, p => { var d=Document();p.Value(d);return Bytes(d); });
        result["malformed-json"] = Encoding.UTF8.GetBytes("{");
        result["duplicate-property"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(File.ReadAllBytes(CandidatePath)).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"));
        return result;
    }

    internal static async Task RunAsync()
    {
        var catalog=JobConceptCatalog.LoadDefault();var candidate=JsonConceptCandidate.Load(CandidatePath,catalog);
        Require(candidate.Rules.Length==299 && candidate.Rules.Select(r=>r.ConceptId).Distinct().Count()==85,"Candidate inventory");
        Require(candidate.PipelineFingerprint!=candidate.MigrationProvenance.SqliteRuntimeFingerprint,"Separate candidate identity required");
        foreach(var item in InvalidCases())
        {
            try { JsonConceptCandidate.Parse(item.Value,catalog);throw new InvalidOperationException("Accepted invalid candidate: "+item.Key); }
            catch(InvalidDataException) { }
        }
        var d=Document();var reversed=d["rules"]!.AsArray().Reverse().Select(r=>r!.DeepClone()).ToArray();d["rules"]=new JsonArray(reversed);
        var shuffled=JsonConceptCandidate.Parse(Bytes(d),catalog);
        Require(Serialize(candidate.Rules)==Serialize(shuffled.Rules),"Physical array order leaked into execution");
        Require(candidate.ByteHash!=shuffled.ByteHash && candidate.PipelineFingerprint!=shuffled.PipelineFingerprint,"Exact-byte identity");
        var alternateCatalog = new JobConceptCatalog(JsonSerializer.Deserialize<JobConceptCatalogDocument>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"JobConceptCatalog.json")),Json)!);
        var alternate = Document(); alternate["taxonomy"]!["sha256"] = alternateCatalog.Fingerprint;
        try { JsonConceptCandidate.Parse(Bytes(alternate),alternateCatalog);throw new InvalidOperationException("Accepted taxonomy bytes outside the supported schema"); }
        catch(InvalidDataException) { }
        var remote = new RemoteWorkDetector(); var extended = new ExtendedLocationRequirementDetector();
        foreach(var f in JsonSerializer.Deserialize<ConceptOracleTests.Input[]>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"concept-oracle-fixtures.json")),Json)!)
        {
            var rw=f.RemoteWork??remote.Analyze(f.Title,f.PrimaryLocation,f.AdditionalLocations??[],f.Html);
            var el=f.ExtendedLocation??extended.Analyze(f.Title,f.PrimaryLocation,f.AdditionalLocations??[],f.Html);
            Require(Serialize(Comparable(candidate.Classify(f.Title,f.Html,rw,el)))==Serialize(Comparable(shuffled.Classify(f.Title,f.Html,rw,el))),"Physical JSON order changed actual classification");
        }
        Require(candidate.Matcher.GetType().GetMethod("ReloadAsync") is null,"Production snapshot must not expose store reload");
        try { candidate.Matcher.Classify("","",null,null,true);throw new InvalidOperationException("Candidate accepted telemetry"); } catch(InvalidOperationException ex) when(ex.Message.Contains("Immutable concept")) { }
        var output=Path.Combine(Path.GetTempPath(),"jsm-json-fixtures-"+Guid.NewGuid().ToString("N")+".json");
        try { await CompareAsync(Path.Combine(AppContext.BaseDirectory,"concept-oracle-fixtures.json"),output,"seed"); }
        finally { File.Delete(output);File.Delete(output+".concepts.json"); }
        await SyntheticAsync();
        Console.WriteLine($"PASS JSON loader: {InvalidCases().Count} rejected cases, immutable snapshot, explicit order, independent identities");
    }

    private static string[] Trace(IConceptMatcher matcher,JobConceptCatalog catalog,string title,string html) =>
        (string[])typeof(ConceptOracleTests).GetMethod("CurrentTrace",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[matcher,catalog,title,html])!;
    private static RegexClassification Comparable(RegexClassification r) => r with {ClassifiedUtc=DateTimeOffset.UnixEpoch,RulesetFingerprint="comparison-only: identities verified separately"};
    private static string? Error(Action action) { try {action();return null;} catch(Exception ex){return ex.GetType().FullName+":"+ex.Message;} }

    internal static async Task CompareAsync(string input,string output,string database)
    {
        var catalog=JobConceptCatalog.LoadDefault();var candidate=JsonConceptCandidate.Load(CandidatePath,catalog);
        using var export=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(OracleRoot,"sqlite-effective-rules.json")));
        var definitions=export.RootElement.GetProperty("rules").Deserialize<SemanticRule[]>(Json)!;
        var directory=Path.Combine(Path.GetTempPath(),"jsm-json-parity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"rules.db");if(database!="seed") File.Copy(database,path);
            using var store=new SqliteSemanticRuleStore(path,catalog);if(database=="seed")store.Initialize(Path.Combine(AppContext.BaseDirectory,"LegacyJobConceptRules.json"));
            var snapshot=await store.LoadRuntimeSnapshotAsync();
            Require(snapshot.Fingerprint==candidate.MigrationProvenance.SqliteRuntimeFingerprint,"Archived runtime drift");
            // The accepted AI update adds rules; every original rule must retain exact semantics and relative order.
            var originalRules=candidate.Rules.Where(r=>!r.RuleId.StartsWith("ai-20260909-",StringComparison.Ordinal)).ToArray();
            foreach(var pair in snapshot.Rules.Zip(originalRules))
                Require(pair.First.RuleId==pair.Second.RuleId && pair.First.ConceptId==pair.Second.ConceptId && pair.First.RuleType==pair.Second.Kind && pair.First.Scope==pair.Second.Scope && pair.First.ContextGroupId==pair.Second.ContextGroupId && pair.First.Pattern==(pair.Second.Pattern??pair.Second.Selector!.Category??"remote-designation"),"Definition or order drift");
            Require(snapshot.Rules.Count==originalRules.Length,"Rule count drift");
            var sqlite=new LegacyRegexSemanticClassifier(store,catalog);await sqlite.InitializeAsync();
            var frozen=new FrozenSqliteConceptOracle(new(snapshot.Fingerprint,DateTimeOffset.UnixEpoch,definitions,[]),catalog,store.Policy);
            var fixtures=JsonSerializer.Deserialize<ConceptOracleTests.Input[]>(File.ReadAllBytes(input),Json)!;
            var remote=new RemoteWorkDetector();var extended=new ExtendedLocationRequirementDetector();
            var pairs=new List<object>();var differences=new List<string>();var errors=0;var timeouts=0;
            foreach(var f in fixtures)
            {
                var rw=f.RemoteWork??remote.Analyze(f.Title,f.PrimaryLocation,f.AdditionalLocations??[],f.Html);
                var el=f.ExtendedLocation??extended.Analyze(f.Title,f.PrimaryLocation,f.AdditionalLocations??[],f.Html);
                RegexClassification? old=null,sql=null,json=null;
                var a=Error(()=>old=frozen.Classify(f.Title,f.Html,rw,el,false));
                var b=Error(()=>sql=sqlite.Classify(f.Title,f.Html,rw,el,false));
                var c=Error(()=>json=candidate.Classify(f.Title,f.Html,rw,el));
                var sqlTrace=Trace(sqlite,catalog,f.Title,f.Html);var jsonTrace=Trace(candidate.Matcher,catalog,f.Title,f.Html);
                var presence=catalog.Concepts.Select(x=>new{x.Id,Frozen=old?.Concepts.Any(v=>v.ConceptId==x.Id)==true,Sqlite=sql?.Concepts.Any(v=>v.ConceptId==x.Id)==true,Json=json?.Concepts.Any(v=>v.ConceptId==x.Id)==true}).ToArray();
                if(a is not null||b is not null||c is not null)errors++;
                timeouts+=json?.TimedOutRuleIds.Count??0;
                if(a!=b||b!=c||Serialize(old is null?null:Comparable(old))!=Serialize(sql is null?null:Comparable(sql))||Serialize(sql is null?null:Comparable(sql))!=Serialize(json is null?null:Comparable(json))||Serialize(frozen.Trace)!=Serialize(sqlTrace)||Serialize(sqlTrace)!=Serialize(jsonTrace)||presence.Any(p=>p.Frozen!=p.Sqlite||p.Sqlite!=p.Json))differences.Add(f.Id);
                pairs.Add(new{f.Id,Old=sql?.Concepts,Current=json?.Concepts,Presence=presence,Frozen=old is null?null:Comparable(old),Sqlite=sql is null?null:Comparable(sql),Json=json is null?null:Comparable(json),FrozenTrace=frozen.Trace.ToArray(),SqliteTrace=sqlTrace,JsonTrace=jsonTrace,Errors=new[]{a,b,c}});
            }
            File.WriteAllText(output+".concepts.json",Serialize(pairs));
            File.WriteAllText(output,Serialize(new{Candidate=candidate.Identity,SqliteFingerprint=snapshot.Fingerprint,InputHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),Total=fixtures.Length,Exact=fixtures.Length-differences.Count,ConceptDecisions=fixtures.Length*85,Errors=errors,Timeouts=timeouts,Differences=differences}));
            Require(differences.Count==0 && errors==0,"SQLite/JSON/frozen parity failed: "+string.Join(",",differences));
            Console.WriteLine($"PASS three-way SQLite/JSON/frozen parity {fixtures.Length}/{fixtures.Length}; {fixtures.Length*85} concept decisions");
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
    }

    private static async Task SyntheticAsync()
    {
        var catalog=JobConceptCatalog.LoadDefault();var directory=Path.Combine(Path.GetTempPath(),"jsm-json-synthetic-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var store=new SqliteSemanticRuleStore(Path.Combine(directory,"rules.db"),catalog);store.Initialize(Path.Combine(AppContext.BaseDirectory,"LegacyJobConceptRules.json"));
            foreach(var pattern in new[]{@"(?=needle)needle",@"^(?=a)(a+)+$"})
            {
                var d=Document();var rule=Rule(d,"positive-evidence");rule["pattern"]=pattern;rule["scope"]="posting";
                var candidate=JsonConceptCandidate.Parse(Bytes(d),catalog);
                using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+Path.Combine(directory,"rules.db")))
                {db.Open();using var command=db.CreateCommand();command.CommandText="UPDATE SemanticRules SET Pattern=$pattern,Scope='posting' WHERE RuleId=$id";command.Parameters.AddWithValue("$pattern",pattern);command.Parameters.AddWithValue("$id",rule["ruleId"]!.GetValue<string>());command.ExecuteNonQuery();}
                var snapshot=await store.LoadRuntimeSnapshotAsync();var sql=new LegacyRegexSemanticClassifier(store,catalog);await sql.InitializeAsync();var old=new FrozenSqliteConceptOracle(snapshot,catalog,store.Policy);
                foreach(var f in pattern.Contains("needle")?new[]{new ConceptOracleTests.Input("posting","","needle"),new("title-only","needle",""),new("first-acceptable","","not needle. needle")}:new[]{new ConceptOracleTests.Input("timeout","",new string('a',100000)+"!")})
                {
                    var a=old.Classify(f.Title,f.Html,null,null,false);var b=sql.Classify(f.Title,f.Html,null,null,false);var c=candidate.Classify(f.Title,f.Html,null,null);
                    Require(Serialize(Comparable(a))==Serialize(Comparable(b))&&Serialize(Comparable(b))==Serialize(Comparable(c)),"Synthetic parity");
                    if(f.Id=="timeout")Require(c.TimedOutRuleIds.Contains(rule["ruleId"]!.GetValue<string>()),"Expected bounded fallback timeout");
                }
            }
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
        Console.WriteLine("PASS three-way posting scope, local negation, regex fallback and 100ms timeout parity");
    }

    internal static void WriteValidationCases(string directory)
    {
        Directory.CreateDirectory(directory);
        var cases=InvalidCases().ToDictionary(p=>p.Key,p=>p.Value);cases["valid"]=File.ReadAllBytes(CandidatePath);
        cases["missing-file"]=[];cases["invalid-schema"]=File.ReadAllBytes(CandidatePath);
        foreach(var item in cases)
        {
            var path=Path.Combine(directory,item.Key);Directory.CreateDirectory(path);
            if(item.Key!="missing-file")File.WriteAllBytes(Path.Combine(path,"concepts-v1.json"),item.Value);
            File.Copy(Path.ChangeExtension(CandidatePath,".schema.json"),Path.Combine(path,"concepts-v1.schema.json"),true);
            if(item.Key=="invalid-schema")File.WriteAllText(Path.Combine(path,"concepts-v1.schema.json"),"{}");
        }
    }

    internal static async Task HostAsync(string path,string url)
    {
        var candidate=JsonConceptCandidate.Load(path,JobConceptCatalog.LoadDefault()); // MUST precede host creation/listening.
        var builder=WebApplication.CreateSlimBuilder();builder.WebHost.UseSetting("urls",url);
        var app=builder.Build();app.MapGet("/healthz",()=>"Healthy");app.MapGet("/version",()=>candidate.Identity);
        app.MapPost("/migration/classify",(ConceptOracleTests.Input input)=>candidate.Classify(input.Title,input.Html,input.RemoteWork,input.ExtendedLocation));
        await app.RunAsync();
    }
}
