using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobSearchManager;

public sealed record MaintenanceTriggerPolicy(int SchemaVersion, string Version, int PollSeconds,
    int MinimumNewObservations, int MinimumCases, int MinimumEmployers, int MaximumCases,
    int MaximumPerEmployer, int MaximumPerRuleCombination, int MinimumScore, int UnderReviewedCount,
    double DuplicateTitleSimilarity, double DuplicateDutySimilarity, double PriorKeepSimilarity,
    string[] IgnoredTokens, string[] RiskRuleIds, string[] RiskPredicateIds, Dictionary<string,int> Weights)
{
    internal static MaintenanceTriggerPolicy Load(byte[] bytes)
    {
        var p = JsonSerializer.Deserialize<MaintenanceTriggerPolicy>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing trigger policy.");
        string[] signals = ["novel-pattern", "new-reject", "under-reviewed-rule", "prior-keep-rule", "prior-keep-pattern", "novel-combination", "risk-family", "fail-open"];
        if (p.SchemaVersion != 1 || string.IsNullOrWhiteSpace(p.Version) || p.PollSeconds is < 60 or > 86400 ||
            p.MinimumCases < 2 || p.MaximumCases < p.MinimumCases || p.MaximumCases > 100 || p.MinimumNewObservations < p.MinimumCases ||
            p.MinimumEmployers < 2 || p.MinimumEmployers > p.MinimumCases || p.MaximumPerEmployer < 1 || p.MaximumPerRuleCombination < 1 ||
            p.MinimumScore < 1 || p.UnderReviewedCount < 1 || p.IgnoredTokens is null || p.RiskRuleIds is null || p.RiskPredicateIds is null ||
            p.Weights is null || signals.Any(s => !p.Weights.TryGetValue(s,out var w) || w < 0 || w > 20) ||
            new[] {p.DuplicateTitleSimilarity,p.DuplicateDutySimilarity,p.PriorKeepSimilarity}.Any(v => !double.IsFinite(v) || v <= 0 || v > 1))
            throw new InvalidDataException("Invalid maintenance trigger policy.");
        return p;
    }
}
public sealed record MaintenanceDetectionStatus(string Status, string PolicyVersion, string PolicyHash,
    string? CompletedCycle, int NewObservations, int InformativeCases, int Employers, string Message,
    string? QueueHash = null, string? ManifestHash = null);
internal sealed record DiscoveryEvidence(string Key, string StableJobId, string Employer, string Title, string Duties,
    string Decision, string Category, string Reason, string[] RuleIds, CheapRejectEvidence[] Evidence,
    string InputHash, DateTimeOffset AnalyzedAtUtc, bool DescriptionAvailable);
internal sealed record DiscoverySelection(DiscoveryEvidence Observation, int Score, string[] Reasons);
internal sealed record DiscoveryInput(string Key, string StableJobId, string Employer, string InputHash, DateTimeOffset AnalyzedAtUtc);
internal sealed record DiscoveryLedger(string CompletedCycle, int CompletedRevision, string QueueHash,
    string RulesetVersion, string RulesetHash, string PolicyHash, string StartedAtUtc,
    string[] BaselineKeys, string[] NewKeys, DiscoveryEvidence[] Evidence)
{
    public DiscoveryInput[] Considered { get; init; } = [];
}

/// <summary>Read-only cache discovery. No provider, catalog, scoring, rule-write or model dependency.</summary>
public sealed class CheapTriageMaintenanceDetector : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly CheapTriageHumanReview review;
    private readonly CheapTriageMaintenance workflow;
    private readonly CheapTriageShadow shadow;
    private readonly ILogger<CheapTriageMaintenanceDetector> logger;
    private readonly string cacheRoot, directory;
    private readonly byte[] policyBytes;
    internal readonly MaintenanceTriggerPolicy Policy;
    private readonly string policyHash;
    private MaintenanceDetectionStatus status;
    public MaintenanceDetectionStatus Status => Volatile.Read(ref status);
    public CheapTriageMaintenanceDetector(CheapTriageHumanReview review, CheapTriageMaintenance workflow,
        CheapTriageShadow shadow, IHostEnvironment environment, IConfiguration configuration,
        IWorkspaceDataStoreFactory stores, ILogger<CheapTriageMaintenanceDetector> logger)
        : this(review, workflow, shadow, stores.Create(WorkspaceContext.LocalWorkspaceId).Description,
            configuration["CheapTriage:DiscoveryDirectory"] ?? Path.Combine(Path.GetDirectoryName(review.ActiveQueueDirectory!)!, "cheap-triage-discovery"),
            File.ReadAllBytes(Path.Combine(environment.ContentRootPath,"CheapTriage","maintenance-trigger-policy-v1.json")), logger) { }
    internal CheapTriageMaintenanceDetector(CheapTriageHumanReview review, CheapTriageMaintenance workflow,
        CheapTriageShadow shadow, string cacheRoot, string directory, byte[] policyBytes, ILogger<CheapTriageMaintenanceDetector> logger)
    {
        this.review=review;this.workflow=workflow;this.shadow=shadow;this.cacheRoot=cacheRoot;this.directory=directory;this.logger=logger;
        this.policyBytes=policyBytes;Policy=MaintenanceTriggerPolicy.Load(policyBytes);policyHash=Hash(policyBytes);
        status=new("starting",Policy.Version,policyHash,null,0,0,0,"Automatic observation check is starting.");
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                status=Status with { Status="unavailable",Message="Automatic evidence check is temporarily unavailable; it will retry. Running jobs are unaffected." };
                logger.LogWarning(e,"Maintenance discovery failed; no jobs or rules were modified.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Policy.PollSeconds),stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    internal async Task ScanAsync(CancellationToken token = default)
    {
        if (!shadow.Enabled) { status=Status with { Status="off",Message="Observation discovery is paused while Cheap Triage is Off." }; return; }
        var state=workflow.Read(review.Read());
        if (state.Stage is not ("no-update-needed" or "released"))
        {
            status=Status with { Status="cycle-open",Message="The current review or maintenance cycle is in progress; no additional queue will be created." };return;
        }
        // A complete scan is required. Corrupt/unreadable cache input leaves the last successful ledger intact.
        var observations=await ReadCachedAsync(token);
        Directory.CreateDirectory(directory);
        var cycleDirectory=Path.Combine(directory,state.Key);Directory.CreateDirectory(cycleDirectory);
        var file=Path.Combine(cycleDirectory,"observations.json");
        DiscoveryLedger ledger;
        if (!File.Exists(file))
        {
            ledger=new(state.Key,state.Revision,state.Review.QueueFingerprint,shadow.Rules.Version,shadow.Rules.Fingerprint,
                policyHash,DateTimeOffset.UtcNow.ToString("O"),observations.Select(o=>o.Key).Distinct().Order(StringComparer.Ordinal).ToArray(),[],[]);
            Write(file,ledger);
        }
        else ledger=JsonSerializer.Deserialize<DiscoveryLedger>(File.ReadAllBytes(file),Json)!;
        if (ledger.PolicyHash!=policyHash || ledger.RulesetHash!=shadow.Rules.Fingerprint || ledger.CompletedRevision!=state.Revision)
            throw new InvalidDataException("Discovery provenance changed. Preserve and explicitly migrate the prior ledger before using a different policy.");
        WriteImmutable(Path.Combine(cycleDirectory,"trigger-policy.json"),policyBytes);
        var known=ledger.BaselineKeys.Concat(ledger.NewKeys).ToHashSet(StringComparer.Ordinal);
        var fresh=observations.Where(o=>o.AnalyzedAtUtc>DateTimeOffset.Parse(ledger.StartedAtUtc,System.Globalization.CultureInfo.InvariantCulture) && !known.Contains(o.Key)).GroupBy(o=>o.Key).Select(g=>g.First()).ToArray();
        ledger=ledger with { NewKeys=ledger.NewKeys.Concat(fresh.Select(o=>o.Key)).Distinct().Order(StringComparer.Ordinal).ToArray(),
            Considered=ledger.Considered.Concat(fresh.Select(o=>new DiscoveryInput(o.Key,o.StableJobId,o.Employer,o.InputHash,o.AnalyzedAtUtc))).OrderBy(o=>o.Key,StringComparer.Ordinal).ToArray(),
            Evidence=ledger.Evidence.Concat(fresh.Where(o=>o.Decision is "REJECT" or "UNDETERMINED")).OrderBy(o=>o.Key,StringComparer.Ordinal).ToArray() };
        if (ledger.BaselineKeys.Length+ledger.NewKeys.Length>50000) throw new InvalidDataException("Discovery ledger reached its safety capacity; human inspection required.");
        Write(file,ledger);
        var history=review.History();var selected=Select(ledger.Evidence,history,Policy);
        var employers=selected.Select(s=>s.Observation.Employer).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        status=new("idle",Policy.Version,policyHash,state.Key,ledger.NewKeys.Length,selected.Length,employers,
            $"{ledger.NewKeys.Length} new Shadow observations collected; no review required yet.");
        if (ledger.NewKeys.Length<Policy.MinimumNewObservations || selected.Length<Policy.MinimumCases || employers<Policy.MinimumEmployers) return;
        var manifest=new { schemaVersion=1, completedCycle=state.Key,completedRevision=state.Revision,
            previousQueueHash=state.Review.QueueFingerprint,baselineVersion=shadow.Rules.Version,baselineHash=shadow.Rules.Fingerprint,
            triggerPolicyVersion=Policy.Version,triggerPolicyHash=policyHash,ledgerHash=Hash(File.ReadAllBytes(file)),
            observationsConsidered=ledger.Considered, selected };
        var manifestBytes=JsonSerializer.SerializeToUtf8Bytes(manifest,Json);var manifestHash=Hash(manifestBytes);
        var manifestPath=Path.Combine(cycleDirectory,manifestHash+".manifest.json");
        WriteImmutable(manifestPath,manifestBytes);
        var cases=selected.Select((s,i)=>ToCase(s,i)).ToArray();
        var queue=new HumanReviewQueue("auto-v1-"+manifestHash,manifestHash,cases);
        var queueHash=review.Publish(queue,report=>{ var now=workflow.Read(report);return now.Key==state.Key && now.Revision==state.Revision && now.Stage==state.Stage; });
        status=status with { Status="review-ready",QueueHash=queueHash,ManifestHash=manifestHash,
            Message=$"{selected.Length} new Cheap Triage decisions need review before the next rule-maintenance cycle." };
        Write(Path.Combine(cycleDirectory,"selection.json"),status);
    }
    private async Task<DiscoveryEvidence[]> ReadCachedAsync(CancellationToken token)
    {
        if (!Directory.Exists(cacheRoot)) return [];
        var result=new List<DiscoveryEvidence>();
        foreach (var path in Directory.EnumerateFiles(cacheRoot,"*.json",SearchOption.AllDirectories)
            .Where(p=>p.Replace('\\','/').Contains("/shared/job-caches/",StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if(new FileInfo(path).Length>64_000_000)throw new InvalidDataException("Cache file exceeds discovery limit.");
            var doc=JsonSerializer.Deserialize<JobsCacheDocument>(await File.ReadAllBytesAsync(path,token),Json);
            if (doc?.Jobs is null) continue;
            foreach(var stored in doc.Jobs)
            {
                if (stored.CheapTriage is not { } observation || observation.Result.RulesetFingerprint!=shadow.Rules.Fingerprint) continue;
                var job=stored;
                if (string.IsNullOrEmpty(job.DescriptionHtml) && !string.IsNullOrEmpty(job.CompressedDescriptionHtml))
                {
                    using var input=new MemoryStream(Convert.FromBase64String(job.CompressedDescriptionHtml));
                    using var gzip=new GZipStream(input,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
                    var buffer=new char[2_000_001];var length=await reader.ReadBlockAsync(buffer.AsMemory(),token);
                    if (length>2_000_000) throw new InvalidDataException("Cached description exceeds discovery limit.");
                    job=job with { DescriptionHtml=new string(buffer,0,length) };
                }
                if (!shadow.IsCurrent(job)) continue;
                var duties=JobAnalysis.HtmlToPlainText(job.DescriptionHtml??"");
                if(duties.Length>8000) duties=duties[..4000]+"\n"+duties[^4000..];
                var key=Hash(Encoding.UTF8.GetBytes(Normalize(job.Title,Policy)+"\n"+Normalize(duties,Policy)));
                result.Add(new(key,job.StableId,job.CompanyId,job.Title,duties,observation.Decision,observation.Result.Category,
                    observation.Result.Reason,observation.Result.RuleIds.Order(StringComparer.Ordinal).ToArray(),observation.Result.Evidence.ToArray(),
                    observation.Result.PostingFingerprint,observation.AnalyzedAtUtc,observation.DescriptionAvailable));
            }
        }
        return result.OrderBy(o=>o.StableJobId,StringComparer.Ordinal).ThenByDescending(o=>o.AnalyzedAtUtc).GroupBy(o=>o.Key).Select(g=>g.First()).ToArray();
    }
    internal static string Normalize(string? text,MaintenanceTriggerPolicy p) => string.Join(' ',Tokens(text,p).Order(StringComparer.Ordinal));
    private static HashSet<string> Tokens(string? text,MaintenanceTriggerPolicy p) => new string((text??"").ToLowerInvariant().Select(c=>char.IsLetter(c)?c:' ').ToArray())
        .Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>t.Length>1 && !p.IgnoredTokens.Contains(t,StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal);
    private static double Similarity(string a,string b,MaintenanceTriggerPolicy p)
    { var x=Tokens(a,p);var y=Tokens(b,p);return x.Count==0&&y.Count==0?1:(double)x.Intersect(y).Count()/Math.Max(1,x.Union(y).Count()); }
    internal static DiscoverySelection[] Select(IEnumerable<DiscoveryEvidence> evidence,HumanReviewReport[] history,MaintenanceTriggerPolicy p)
    {
        var prior=history.SelectMany(h=>h.Cases.Where(c=>h.Reviews.ContainsKey(c.GetProperty("stableJobId").GetString()!))
            .Select(c=>(Case:c,Human:h.Reviews[c.GetProperty("stableJobId").GetString()!]))).ToArray();
        static string Text(JsonElement c,string k)=>c.TryGetProperty(k,out var v)?v.ToString():"";
        static string[] Rules(JsonElement c)=>Text(c,"matchedRuleIds").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
        var ranked=new List<DiscoverySelection>();
        foreach(var o in evidence.OrderBy(x=>x.Key,StringComparer.Ordinal))
        {
            if(prior.Any(x=>(Similarity(Text(x.Case,"title"),o.Title,p)>=p.DuplicateTitleSimilarity &&
                (o.Duties.Length==0 || Similarity((Text(x.Case,"descriptionText") is { Length: > 0 } full ? full : Text(x.Case,"dutyExcerpt1")+" "+Text(x.Case,"dutyExcerpt2")),o.Duties,p)>=p.DuplicateDutySimilarity)))) continue;
            var reasons=new List<string>{"novel-pattern"};
            if(o.Decision=="UNDETERMINED")reasons.Add("fail-open");else if(o.Decision=="REJECT")reasons.Add("new-reject");else continue;
            if(o.RuleIds.Any(id=>prior.Count(x=>Rules(x.Case).Contains(id))<p.UnderReviewedCount))reasons.Add("under-reviewed-rule");
            if(prior.Any(x=>x.Human.Decision=="KEEP" && Rules(x.Case).Intersect(o.RuleIds).Any()))reasons.Add("prior-keep-rule");
            if(prior.Any(x=>x.Human.Decision=="KEEP" && Similarity(Text(x.Case,"title"),o.Title,p)>=p.PriorKeepSimilarity))reasons.Add("prior-keep-pattern");
            if(o.RuleIds.Length>0 && !prior.Any(x=>Rules(x.Case).SequenceEqual(o.RuleIds)))reasons.Add("novel-combination");
            if(o.RuleIds.Intersect(p.RiskRuleIds).Any() || o.Evidence.Any(e=>p.RiskPredicateIds.Contains(e.PredicateId)))reasons.Add("risk-family");
            var score=reasons.Sum(r=>p.Weights[r]);if(score>=p.MinimumScore)ranked.Add(new(o,score,reasons.ToArray()));
        }
        var selected=new List<DiscoverySelection>();
        // Round-robin employer selection avoids a large employer consuming the queue.
        var groups=ranked.GroupBy(x=>x.Observation.Employer).OrderBy(g=>g.Key,StringComparer.Ordinal)
            .Select(g=>new Queue<DiscoverySelection>(g.OrderByDescending(x=>x.Score).ThenBy(x=>x.Observation.Key,StringComparer.Ordinal))).ToArray();
        bool added;
        do { added=false;foreach(var group in groups)
        {
            while(group.TryDequeue(out var item))
            {
                var o=item.Observation;
                if(selected.Count(x=>x.Observation.Employer==o.Employer)>=p.MaximumPerEmployer ||
                    selected.Count(x=>x.Observation.RuleIds.SequenceEqual(o.RuleIds))>=p.MaximumPerRuleCombination ||
                    selected.Any(x=>Similarity(x.Observation.Title,o.Title,p)>=p.DuplicateTitleSimilarity &&
                        (o.Duties.Length==0 || Similarity(x.Observation.Duties,o.Duties,p)>=p.DuplicateDutySimilarity)))continue;
                selected.Add(item);added=true;break;
            }
            if(selected.Count==p.MaximumCases)return selected.ToArray();
        }}while(added);
        return selected.ToArray();
    }
    private JsonElement ToCase(DiscoverySelection s,int i)
    {
        var o=s.Observation;
        return JsonSerializer.SerializeToElement(new {reviewId=$"N{i+1:000}",o.StableJobId,employer=o.Employer,title=o.Title,
            family=o.Category,currentDecision=o.Decision,category=o.Category,reason=o.Reason,matchedRuleIds=string.Join("; ",o.RuleIds),
            matchedEvidence=JsonSerializer.Serialize(o.Evidence,Json),jobFitScore="",jobFitAvailability="Not captured; no scoring or provider request made",
            workflowState="Not captured; workspace workflow is unaffected",descriptionAvailable=o.DescriptionAvailable,
            dutyExcerpt1=o.Duties[..Math.Min(1800,o.Duties.Length)], dutyExcerpt2=o.Duties.Length>1800?o.Duties[Math.Max(1800,o.Duties.Length-1800)..]:"",descriptionText=o.Duties,
            rulesetVersion=shadow.Rules.Version,rulesetHash=shadow.Rules.Fingerprint,auditClassification="REVIEW REQUESTED",
            reviewReason=string.Join(", ",s.Reasons),selectionScore=s.Score,inputFingerprint=o.InputHash,observedAtUtc=o.AnalyzedAtUtc},new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    internal static void WriteImmutable(string path, byte[] bytes)
    {
        if (File.Exists(path))
        { if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new InvalidDataException("Immutable artifact differs from expected content."); return; }
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,bytes); File.Move(temporary,path); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    private static void Write<T>(string path,T value)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,JsonSerializer.SerializeToUtf8Bytes(value,Json));File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
