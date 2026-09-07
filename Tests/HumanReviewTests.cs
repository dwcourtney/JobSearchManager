using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using JobSearchManager;

internal static class HumanReviewTests
{
    public static Task Run()
    {
        var root = Directory.GetCurrentDirectory();
        var source = Path.Combine(root, "Tests/cheap_reject_evaluation/human_review_v1");
        var queue = Path.Combine(AppContext.BaseDirectory, "CheapTriage/review-queues/human-review-v1.json");
        var rules = Path.Combine(AppContext.BaseDirectory, CheapRejectRules.DefaultPath);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var queueHash = Hash(queue); var rulesHash = Hash(rules); var machineHash = Hash(Path.Combine(source, "queue.csv"));
        var dir = Path.Combine(Path.GetTempPath(), "jsm-human-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var db = Path.Combine(dir, "reviews.db"); var store = new CheapTriageHumanReview(queue, db);
            var blank = store.Read();
            Check(blank.Cases.Length == 29 && blank.Reviewed == 0 && blank.Remaining == 29 && !blank.Complete, "Initial blank queue");
            using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "excerpt-provenance.json")));
            var expected = provenance.RootElement.EnumerateArray().Select(x => x.GetProperty("stableId").GetString()).ToHashSet();
            Check(expected.SetEquals(blank.Cases.Select(x => x.GetProperty("stableJobId").GetString())), "Exact source 29 IDs");
            Check(blank.SourceManifestHash == Hash(Path.Combine(source, "manifest.json")), "Frozen source provenance");
            var first = blank.Cases[0].GetProperty("stableJobId").GetString()!;
            store.Save(new(store.QueueFingerprint, first, "KEEP", "technical duties", 0), "reviewer-one");
            var reopened = new CheapTriageHumanReview(queue, db); var saved = reopened.Read();
            Check(saved.Reviews[first].Decision == "KEEP" && saved.Reviewed == 1 && saved.Remaining == 28, "Durability/progress");
            Check(saved.Reviews[first].Reviewer == "reviewer-one" && DateTimeOffset.TryParse(saved.Reviews[first].ReviewedAtUtc, out _), "Reviewer/time provenance");
            Throws<InvalidOperationException>(() => reopened.Save(new(store.QueueFingerprint, first, "REJECT", "", 0), "other"));
            store.Save(new(store.QueueFingerprint, first, "AMBIGUOUS", "needs context", saved.Reviews[first].Revision), "reviewer-two");
            Check(store.Read().Reviews[first].Decision == "AMBIGUOUS" && store.Read().Revisions.Count == 2, "Revision history and latest selection");
            foreach (var c in blank.Cases.Skip(1))
                store.Save(new(store.QueueFingerprint, c.GetProperty("stableJobId").GetString()!, "REJECT", "reviewed", 0), "reviewer-one");
            var complete = store.Read();
            WorkflowChecks(blank, saved, complete, dir, rules);
            var maintenance = new RuleMaintenance(CheapRejectRules.Load(rules),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["CheapTriage:ReviewArtifactDirectory"] = Path.Combine(dir, "artifacts") }).Build(),
                new ReviewEnvironment { ContentRootPath = AppContext.BaseDirectory });
            Throws<InvalidOperationException>(() => maintenance.GenerateReviewed(blank, null));
            var beforePrompt = Hash(db);
            var prepared = maintenance.GenerateReviewed(complete, null);
            Check(prepared.Prompt.Length < 6000 && !prepared.Prompt.Contains("needs context") && !prepared.Prompt.Contains("\"decisions\""), "Compact prompt does not embed evidence");
            Check(prepared.Prompt.Contains(complete.QueueFingerprint) && prepared.Prompt.Contains(complete.SourceManifestHash)
                && prepared.Prompt.Contains(CheapRejectRules.Load(rules).Fingerprint) && prepared.Prompt.Contains("29 reviewed; KEEP 0, REJECT 28, AMBIGUOUS 1"), "Prompt identities and counts");
            var artifactDir = Path.Combine(dir, "artifacts", prepared.ArtifactBundle!);
            foreach (var artifact in prepared.ArtifactHashes!)
            {
                Check(Hash(Path.Combine(artifactDir, artifact.Key)) == artifact.Value, "Artifact bytes match SHA-256");
                Check(prepared.Prompt.Contains("docs/evaluations/human-review-snapshots/" + prepared.ArtifactBundle + "/" + artifact.Key)
                    && prepared.Prompt.Contains(artifact.Value), "Prompt references immutable files and hashes");
            }
            var evidence = File.ReadAllText(Path.Combine(artifactDir, "human-review.json"));
            foreach (var decision in complete.Reviews.Values)
                Check(evidence.Contains(decision.StableJobId), "Every decision exported");
            Check(evidence.Contains("needs context") && evidence.Contains("matchedEvidence") && evidence.Contains("disagreesWithMachine"), "Full evidence preserved outside prompt");
            Check(maintenance.GenerateReviewed(complete, null).ArtifactBundle == prepared.ArtifactBundle, "Identical snapshot reuses immutable bundle");
            var revised = complete with { Reviews = complete.Reviews.ToDictionary(x => x.Key, x => x.Value with { Note = "revised note" }) };
            Check(maintenance.GenerateReviewed(revised, null).ArtifactBundle != prepared.ArtifactBundle, "Changed review gets new bundle");
            Check(File.ReadAllText(Path.Combine(artifactDir, "human-review.json")) == evidence, "Prior version preserved");
            File.AppendAllText(Path.Combine(artifactDir, "live-shadow.json"), "corrupt");
            Throws<InvalidDataException>(() => maintenance.GenerateReviewed(complete, null));
            Check(prepared.Prompt.Contains("Prefer false KEEP over false REJECT") && prepared.Prompt.Contains("Active gating")
                && prepared.Prompt.Contains("EVERY changed decision") && prepared.Prompt.Contains("frozen AND live"), "Prompt safety constraints");
            Check(Hash(db) == beforePrompt && store.Read().Revisions.SequenceEqual(complete.Revisions), "Prompt generation never writes review data");
            var backupPath = Path.Combine(dir, "backup.db");
            var sourceHash = Hash(db);
            CheapTriageHumanReview.Backup(db, backupPath);
            var backup = new CheapTriageHumanReview(queue, backupPath).Read();
            Check(backup.Complete && backup.Revisions.SequenceEqual(complete.Revisions), "Backup restores all human revisions and provenance");
            Check(Hash(db) == sourceHash, "Backup does not mutate source");
            Throws<IOException>(() => CheapTriageHumanReview.Backup(db, backupPath));
            Check(complete.Complete && complete.Remaining == 0 && complete.Counts["AMBIGUOUS"] == 1 && complete.Counts["REJECT"] == 28, "Completion summary");
            var export = JsonSerializer.Serialize(complete, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(export.Contains(store.QueueFingerprint) && export.Contains("needs context") && export.Contains("reviewer-two") && !complete.RulesChanged, "Export provenance and decisions");
            Throws<ArgumentException>(() => store.Save(new(store.QueueFingerprint, "missing", "KEEP", "", 0), "reviewer"));
            Throws<ArgumentException>(() => store.Save(new(store.QueueFingerprint, first, "ACTIVE", "", 0), "reviewer"));
            Throws<ArgumentException>(() => store.Save(new(store.QueueFingerprint, first, "KEEP", new string('x',2001), 0), "reviewer"));
            Throws<InvalidOperationException>(() => store.Save(new("stale", first, "KEEP", "", 0), "reviewer"));
            var changedQueue = Path.Combine(dir,"changed.json"); File.WriteAllText(changedQueue, File.ReadAllText(queue)+"\n");
            Check(new CheapTriageHumanReview(changedQueue, db).Read().Reviewed == 0, "Changed queue cannot inherit old labels");
            Check(Hash(queue)==queueHash && Hash(rules)==rulesHash && Hash(Path.Combine(source,"queue.csv"))==machineHash, "Queue/machine labels/rules immutable");
        }
        finally { Directory.Delete(dir, true); }
        return Task.CompletedTask;
    }
    private static void WorkflowChecks(HumanReviewReport blank, HumanReviewReport partial, HumanReviewReport complete, string dir, string rulesPath)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["CheapTriage:MaintenanceDirectory"] = Path.Combine(dir, "workflow") }).Build();
        var environment = new ReviewEnvironment { ContentRootPath = AppContext.BaseDirectory };
        var rules = CheapRejectRules.Load(rulesPath);
        var workflow = new CheapTriageMaintenance(config, environment, rules);
        Check(workflow.Read(blank).Stage == "review-required", "Workflow requires initial review");
        Check(workflow.Read(partial).Stage == "review-progress", "Partial review state");
        var state = workflow.Read(complete);
        Check(state.Stage == "review-complete", "Complete review leads to prompt preparation");
        var prompt = new RuleMaintenancePrompt("human-reviewed-2.0.0", rules.Version, rules.Fingerprint, rulesPath, "manual-review-only", "Compact prompt") { ArtifactBundle = new string('a',64) };
        Throws<InvalidOperationException>(() => workflow.Prepare(blank, new(state.Key,0), prompt,"test"));
        state = workflow.Prepare(complete, new(state.Key,state.Revision), prompt,"test");
        Check(state.Stage == "prompt-ready" && state.Prompt!.Prompt.Contains("candidate-result.json"), "Explicit durable Codex handoff");
        Check(new CheapTriageMaintenance(config,environment,rules).Read(complete).Stage == "prompt-ready", "Prompt handoff survives restart");
        var bytes = File.ReadAllText(rulesPath).Replace("\"rulesetVersion\": \"1.0.0\"", "\"rulesetVersion\": \"1.0.9\"");
        var candidateRules = CheapRejectRules.Parse(System.Text.Encoding.UTF8.GetBytes(bytes));
        var metrics = new CandidateMetrics(.995,.99,1,.1,.13,6);
        var candidate = new MaintenanceCandidate(prompt.ArtifactBundle!,rules.Version,rules.Fingerprint,candidateRules.Version,candidateRules.Fingerprint,bytes,0,metrics,metrics,[],["Provisional evidence"],[],"PASS");
        Throws<InvalidOperationException>(() => workflow.Import(complete,new(state.Key,state.Revision,candidate with {SnapshotBundle="stale"}),"test"));
        Throws<InvalidDataException>(() => workflow.Import(complete,new(state.Key,state.Revision,candidate with {CandidateHash="wrong"}),"test"));
        Throws<InvalidOperationException>(() => workflow.Import(complete,new(state.Key,0,candidate),"test"));
        state=workflow.Import(complete,new(state.Key,state.Revision,candidate),"test");
        Check(state.Stage=="candidate", "Import opens candidate evaluation");
        state=workflow.Decide(complete,new(state.Key,state.Revision,candidate.CandidateHash,"rejected"),"test");
        Check(state.Stage=="rejected", "Explicit rejection recorded");
        state=workflow.Prepare(complete,new(state.Key,state.Revision),prompt,"test");
        Check(state.Stage=="prompt-ready" && state.Candidate is null, "Revision request resets candidate, keeps history");
        state=workflow.Import(complete,new(state.Key,state.Revision,candidate with {ValidationStatus="INCOMPLETE"}),"test");
        Throws<InvalidOperationException>(() => workflow.Decide(complete,new(state.Key,state.Revision,candidate.CandidateHash,"approved"),"test"));
        state=workflow.Import(complete,new(state.Key,state.Revision,candidate with {After=metrics with {KeepRecall=.97}}),"test");
        Throws<InvalidOperationException>(() => workflow.Decide(complete,new(state.Key,state.Revision,candidate.CandidateHash,"approved"),"test"));
        state=workflow.Import(complete,new(state.Key,state.Revision,candidate),"test");
        state=workflow.Decide(complete,new(state.Key,state.Revision,candidate.CandidateHash,"approved"),"test");
        Check(state.Stage=="approved" && CheapRejectRules.Load(rulesPath).Fingerprint==rules.Fingerprint, "Approval is durable and never activates rules");
        Check(new CheapTriageMaintenance(config,environment,rules).Read(complete).Stage=="approved", "Approval survives restart");
        state=workflow.PrepareRelease(complete,new(state.Key,state.Revision),"test");
        Check(state.Stage=="release-request-ready" && state.Disposition=="approved" && state.ReleasePrompt!.Prompt.Contains(candidate.CandidateHash), "Release request identifies accepted candidate");
        Check(state.ReleasePrompt!.Prompt.Contains("Shadow") && state.ReleasePrompt.Prompt.Contains("Do not configure Docker/WSL"), "Release prompt safety");
        Check(new CheapTriageMaintenance(config,environment,rules).Read(complete).Stage=="release-request-ready", "Release request survives restart");
        Throws<InvalidOperationException>(()=>workflow.PrepareRelease(complete,new(state.Key,0),"test"));
        state=workflow.Decide(complete,new(state.Key,state.Revision,candidate.CandidateHash,"rejected"),"test");
        Check(state.ReleasePrompt is null, "Changed disposition invalidates release request");
        Throws<InvalidOperationException>(()=>workflow.PrepareRelease(complete,new(state.Key,state.Revision),"test"));
        var revised = complete with { Reviews=complete.Reviews.ToDictionary(x=>x.Key,x=>x.Value with {Note="later review"}) };
        Check(workflow.Read(revised).Stage=="review-complete", "Changed human review invalidates prior prompt/candidate");
        Check(new CheapTriageMaintenance(config,environment,candidateRules).Read(complete).Stage=="review-complete", "Changed baseline invalidates candidate");
        Check(File.ReadAllText(Path.Combine(dir,"workflow",state.Key+".json")).Contains("rejected"), "Earlier disposition history preserved");
        Throws<InvalidOperationException>(() => workflow.ReadInbox());
        // Separate in-memory test fixture; never rewrite the persisted human store.
        var binaryReview = complete with { Reviews = complete.Reviews.OrderBy(x=>x.Key).Select((x,i)=>new {x.Key, Value=x.Value with {Decision=i<7?"KEEP":"REJECT"}}).ToDictionary(x=>x.Key,x=>x.Value),
            Counts = new Dictionary<string,int> { ["KEEP"]=7,["REJECT"]=22,["AMBIGUOUS"]=0 } };
        state=workflow.Read(binaryReview);state=workflow.Prepare(binaryReview,new(state.Key,state.Revision),prompt,"test");
        var noUpdate=new MaintenanceNoUpdate(prompt.ArtifactBundle!,rules.Version,rules.Fingerprint,binaryReview.QueueFingerprint,binaryReview.SourceManifestHash,
            binaryReview.Reviews.Select(x=>new NoUpdateHumanMatch(x.Key,x.Value,x.Value.Decision)).ToArray(),metrics,0,"PASS",["Provisional evidence"],[],new string('b',64));
        MaintenanceResultImport Request(MaintenanceNoUpdate value) => new(state.Key,state.Revision,new("NO_UPDATE_NEEDED",null,value));
        Throws<InvalidOperationException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {SnapshotBundle="stale"}),"test"));
        Throws<InvalidOperationException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {RulesetHash="stale"}),"test"));
        Throws<InvalidOperationException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {QueueFingerprint="stale"}),"test"));
        Throws<InvalidOperationException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {SourceManifestHash="stale"}),"test"));
        Throws<InvalidOperationException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate) with {Revision=0},"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {ChangedDecisionCount=1}),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {ValidationStatus="INCOMPLETE"}),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {SafetyFailures=["failure"]}),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {Metrics=metrics with {KeepRecall=.97}}),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {Metrics=metrics with {KeepRecall=double.NaN}}),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {HumanMatches=noUpdate.HumanMatches.Skip(1).ToArray()}),"test"));
        var altered=noUpdate.HumanMatches.ToArray();altered[0]=altered[0] with {Human=altered[0].Human with {Note="tampered"}};
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {HumanMatches=altered}),"test"));
        altered=noUpdate.HumanMatches.ToArray();altered[0]=altered[1];
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,Request(noUpdate with {HumanMatches=altered}),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,new(state.Key,state.Revision,new("NO_UPDATE_NEEDED",candidate,noUpdate)),"test"));
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,new(state.Key,state.Revision,new("UNKNOWN",null,noUpdate)),"test"));
        var sameVersion=candidate with {CandidateVersion=rules.Version,CandidateHash=rules.Fingerprint,RulesetJson=File.ReadAllText(rulesPath)};
        Throws<InvalidDataException>(()=>workflow.ImportResult(binaryReview,new(state.Key,state.Revision,new("CANDIDATE",sameVersion,null)),"test"));
        state=workflow.ImportResult(binaryReview,Request(noUpdate),"test");
        Check(state.Stage=="no-update-needed" && state.Candidate is null && state.Release is null && state.ResultHash?.Length==64, "No-update is a distinct completed outcome, not a candidate or release");
        var restored=new CheapTriageMaintenance(config,environment,rules).Read(binaryReview);
        Check(restored.Stage=="no-update-needed" && restored.NoUpdate!.HumanMatches.Length==29 && restored.ResultHash==state.ResultHash,"No-update survives restart with exact provenance");
        Throws<InvalidOperationException>(()=>workflow.PrepareRelease(binaryReview,new(state.Key,state.Revision),"test"));
        Throws<InvalidOperationException>(()=>workflow.Decide(binaryReview,new(state.Key,state.Revision,candidate.CandidateHash,"approved"),"test"));
        Throws<InvalidOperationException>(()=>workflow.Prepare(binaryReview,new(state.Key,state.Revision),prompt,"test"));
        Check(workflow.Read(binaryReview with {Reviews=binaryReview.Reviews.ToDictionary(x=>x.Key,x=>x.Value with {Note="new review"})}).Stage=="review-complete","Changed human revisions invalidate no-update");
        Check(new CheapTriageMaintenance(config,environment,candidateRules).Read(binaryReview).Stage=="review-complete","Changed rules invalidate no-update");
        Check(CheapRejectRules.Load(rulesPath).Fingerprint==rules.Fingerprint,"No-update does not change rules");
        var inbox=Path.Combine(dir,"workflow","candidate-inbox.json");
        File.WriteAllText(inbox,JsonSerializer.Serialize(new MaintenanceResult("NO_UPDATE_NEEDED",null,noUpdate),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Check(workflow.ReadInbox().ResultType=="NO_UPDATE_NEEDED","Synced result discriminator");
        File.WriteAllText(inbox,JsonSerializer.Serialize(sameVersion,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Check(workflow.ReadInbox().ResultType=="CANDIDATE","Legacy same-version input is never inferred as no-update");

    }
    private sealed class ReviewEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
}
