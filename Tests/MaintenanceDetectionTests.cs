using System.Text;
using System.Text.Json;
using JobSearchManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MaintenanceDetectionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"jsm-detection-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var queue=Path.Combine(AppContext.BaseDirectory,"CheapTriage/review-queues/human-review-v1.json");
            var db=Path.Combine(root,"reviews.db");var queues=Path.Combine(root,"queues");
            var store=new CheapTriageHumanReview(queue,db,queues);
            foreach(var c in store.Read().Cases)store.Save(new(store.QueueFingerprint,c.GetProperty("stableJobId").GetString()!,"REJECT","original",0),"test");
            var original=store.Read();var originalBytes=File.ReadAllBytes(queue);var dbBytes=File.ReadAllBytes(db);
            var rules=CheapRejectRules.Load(Path.Combine(AppContext.BaseDirectory,"CheapTriage/rulesets/1.0.1.json"));
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["CheapTriage:Mode"]="Shadow",["CheapTriage:MaintenanceDirectory"]=Path.Combine(root,"workflow")}).Build();
            var workflow=new CheapTriageMaintenance(config,new Environment(),rules);var state=workflow.Read(original);
            var prompt=new RuleMaintenancePrompt("human-reviewed-2.0.0",rules.Version,rules.Fingerprint,"rules.json","manual-review-only","safe prompt"){ArtifactBundle=new string('a',64)};
            state=workflow.Prepare(original,new(state.Key,state.Revision),prompt,"test");
            var noUpdate=new MaintenanceNoUpdate(prompt.ArtifactBundle!,rules.Version,rules.Fingerprint,original.QueueFingerprint,original.SourceManifestHash,
                original.Reviews.Select(x=>new NoUpdateHumanMatch(x.Key,x.Value,x.Value.Decision)).ToArray(),new(1,1,1,.1,.1,0),0,"PASS",[],[],new string('b',64));
            state=workflow.ImportResult(original,new(state.Key,state.Revision,new("NO_UPDATE_NEEDED",null,noUpdate)),"test");
            var journal=Path.Combine(root,"workflow",state.Key+".json");var journalBytes=File.ReadAllBytes(journal);
            var policyBytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"CheapTriage/maintenance-trigger-policy-v1.json"));
            var shadow=new CheapTriageShadow(rules,config);
            CheapTriageMaintenanceDetector Detector(CheapTriageShadow? mode=null)=>new(store,workflow,mode??shadow,Path.Combine(root,"cache"),Path.Combine(root,"discovery"),policyBytes,NullLogger<CheapTriageMaintenanceDetector>.Instance);
            var detector=Detector();await detector.ScanAsync();Check(detector.Status.NewObservations==0 && detector.Status.Status=="idle","No evidence remains idle");
            string Word(int i)=>"novel"+(char)('a'+i/26)+(char)('a'+i%26);
            JobRecord Job(int i,string decision="REJECT")
            {
                var word=Word(i);var job=new JobRecord(word+" specialist",i.ToString(),null,"","",[],"","https://example.com/",word+" duties",null,null,"","",false,null,null,null,"",CompanyId:"employer-"+(i%3));
                return job with {CheapTriage=new(1,DateTimeOffset.UtcNow,true,new(rules.Version,rules.Fingerprint,CheapTriageShadow.InputFingerprint(job),decision,"test-family","Test duties",["novel-rule-"+(i%3)],[],[],false))};
            }
            void Cache(JobRecord[] jobs)
            {var path=Path.Combine(root,"cache/shared/job-caches/example/source.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(new JobsCacheDocument(6,DateTimeOffset.UtcNow,null,0,jobs),Json));}
            Cache(Enumerable.Range(0,7).Select(i=>Job(i)).ToArray());await detector.ScanAsync();Check(detector.Status.NewObservations==7 && detector.Status.Status=="idle","Too little evidence remains idle");
            var bootstrap=new CheapTriageMaintenanceDetector(store,workflow,shadow,Path.Combine(root,"cache"),Path.Combine(root,"bootstrap"),policyBytes,NullLogger<CheapTriageMaintenanceDetector>.Instance);
            await bootstrap.ScanAsync();Check(bootstrap.Status.NewObservations==0,"Existing caches seed baseline rather than reopen completed cycle");
            Cache(Enumerable.Range(0,500).Select(i=>Job(0) with {RequisitionId="duplicate-"+i}).ToArray());await detector.ScanAsync();Check(detector.Status.NewObservations==7,"Exact duplicate postings do not inflate new evidence");
            var changed=Job(100);Cache([changed with {DescriptionHtml="new input without matching observation"}]);await detector.ScanAsync();Check(detector.Status.NewObservations==7,"Stale input observations are ignored");
            var historical=Job(90);Cache([historical with {CheapTriage=historical.CheapTriage! with {AnalyzedAtUtc=DateTimeOffset.UnixEpoch}}]);await detector.ScanAsync();Check(detector.Status.NewObservations==7,"Old cached analysis discovered later is not post-cycle evidence");
            var restarted=Detector();await restarted.ScanAsync();Check(restarted.Status.NewObservations==7,"Accumulation survives restart");
            Cache(Enumerable.Range(0,50).Select(i=>Job(i,i<12?"REJECT":"KEEP")).ToArray());await restarted.ScanAsync();
            var report=store.Read();Check(restarted.Status.Status=="review-ready" && report.Cases.Length==12 && !report.Complete && report.Reviewed==0,"Sufficient diverse risky evidence publishes a blank queue");
            Check(workflow.Read(report).Stage=="review-required" && report.QueueVersion.StartsWith("auto-v1-"),"New versioned queue drives existing workflow");
            Check(report.SourceManifestHash==restarted.Status.ManifestHash && report.QueueFingerprint==restarted.Status.QueueHash,"Selection/queue provenance");
            Check(new CheapTriageHumanReview(queue,db,queues).Read().QueueFingerprint==report.QueueFingerprint,"Atomic active queue survives restart");
            Check(File.ReadAllBytes(db).SequenceEqual(dbBytes) && File.ReadAllBytes(queue).SequenceEqual(originalBytes) && File.ReadAllBytes(journal).SequenceEqual(journalBytes),"Detector changes neither human DB, historical queue nor completed journal");
            Check(new CheapTriageHumanReview(queue,db).Read().Revisions.SequenceEqual(original.Revisions),"All completed human decisions retained");
            await restarted.ScanAsync();Check(store.Read().QueueFingerprint==report.QueueFingerprint,"Open review cannot be replaced");
            try {store.Save(new(original.QueueFingerprint,original.Cases[0].GetProperty("stableJobId").GetString()!,"KEEP","stale browser",1),"test");throw new Exception("Stale queue save accepted");}catch(InvalidOperationException){}
            var current=report.Cases[0].GetProperty("stableJobId").GetString()!;store.Save(new(report.QueueFingerprint,current,"AMBIGUOUS","new evidence",0),"test");Check(store.Read().Reviewed==1,"New queue uses normal durable review controls");
            Check(new CheapTriageHumanReview(queue,db).Read().Revisions.SequenceEqual(original.Revisions),"Reviewing new queue leaves prior decisions immutable");
            var off=Detector(new CheapTriageShadow(rules,new ConfigurationBuilder().Build()));await off.ScanAsync();Check(off.Status.Status=="off","Off pauses detector without enabling Shadow or gating");
            var p=detector.Policy;
            DiscoveryEvidence Evidence(string id,string title,string duties)=>new(id,id,"employer",title,duties,"REJECT","category","reason",["new-rule"],[],"hash",DateTimeOffset.UtcNow,true);
            var sample=original.Cases[0];var prior=Evidence("new-id",sample.GetProperty("title").GetString()!,sample.GetProperty("dutyExcerpt1").GetString()!);
            Check(CheapTriageMaintenanceDetector.Select([prior],[original],p).Length==0,"Previously reviewed title/duties not requeued");
            var a=Evidence("a","Senior unusual sensor analyst","assess component performance");var b=Evidence("b","unusual sensor analyst II","assess component performance");
            Check(CheapTriageMaintenanceDetector.Select([a,b],[],p).Length==1,"Near-identical seniority/requisition variants collapse");
            var forward=CheapTriageMaintenanceDetector.Select([a,b],[],p);var reverse=CheapTriageMaintenanceDetector.Select([b,a],[],p);
            Check(JsonSerializer.Serialize(forward,Json)==JsonSerializer.Serialize(reverse,Json),"Selection is deterministic independent of cache enumeration");
            var anomaly=a with {Decision="UNDETERMINED",RuleIds=[]};Check(CheapTriageMaintenanceDetector.Select([anomaly],[],p).Single().Reasons.Contains("fail-open"),"Fail-open anomalies are eligible");
            var rulesHash=rules.Fingerprint;Check(CheapRejectRules.Load(Path.Combine(AppContext.BaseDirectory,"CheapTriage/rulesets/1.0.1.json")).Fingerprint==rulesHash,"Rules unchanged");
        }
        finally {Directory.Delete(root,true);}
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private sealed class Environment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="Tests";public string ContentRootPath{get;set;}=AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider{get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
