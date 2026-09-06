using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed record CheapRejectEvidence(string PredicateId, string Scope, string Text);
public sealed record CheapRejectDecision(string RulesetVersion, string RulesetFingerprint,
    string PostingFingerprint, string Decision, string Category, string Reason,
    IReadOnlyList<string> RuleIds, IReadOnlyList<CheapRejectEvidence> Evidence,
    IReadOnlyList<string> Guards, bool FailOpen);

/// <summary>A bounded Boolean matcher for the declarative cheap-reject rules only.
/// Not the Job Fit semantic classifier; never discards or writes jobs.</summary>
public sealed class CheapRejectRules
{
    public const string DefaultPath = "CheapTriage/rulesets/1.0.0.json";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(25);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, Timeout);
    private readonly Dictionary<string, Predicate> predicates;
    private readonly Dictionary<string, Regex> patterns;
    private readonly Dictionary<string, Regex> ignored;
    private readonly Rule[] rules;
    private readonly string defaultReason;
    public string Version { get; }
    public string Fingerprint { get; }
    public string Name { get; }

    private CheapRejectRules(Document document, byte[] bytes)
    {
        Version = document.RulesetVersion; Name = document.Name;
        Fingerprint = Hash(bytes); defaultReason = document.DefaultReason;
        predicates = document.Predicates.ToDictionary(p => p.Id, StringComparer.Ordinal);
        rules = document.Rules.OrderBy(r => r.Order).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        patterns = predicates.Values.Where(p => p.Pattern is not null)
            .ToDictionary(p => p.Id, p => Compile(p.Pattern!), StringComparer.Ordinal);
        ignored = predicates.Values.Where(p => p.IgnorePattern is not null)
            .ToDictionary(p => p.Id, p => Compile(p.IgnorePattern!), StringComparer.Ordinal);
    }

    public static CheapRejectRules Load(string path) => Parse(File.ReadAllBytes(path));
    public static CheapRejectRules Parse(byte[] bytes)
    {
        if (bytes.Length > 1_000_000) throw new InvalidDataException("Ruleset exceeds size limit.");
        using var json = JsonDocument.Parse(bytes);
        RejectDuplicateProperties(json.RootElement);
        var doc = JsonSerializer.Deserialize<Document>(bytes, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Missing ruleset.");
        Require(doc.SchemaVersion == 1 && doc.Status == "shadow-only", "Unsupported schema or execution status.");
        Require(doc.RulesetVersion is not null && Regex.IsMatch(doc.RulesetVersion,
            @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant, Timeout), "Invalid ruleset version.");
        Require(!string.IsNullOrWhiteSpace(doc.Name) && !string.IsNullOrWhiteSpace(doc.Description)
            && !string.IsNullOrWhiteSpace(doc.DefaultReason), "Missing ruleset description.");
        Require(doc.Predicates is { Length: > 0 and <= 512 } && doc.Rules is { Length: > 0 and <= 256 }, "Invalid rule counts.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in doc.Predicates)
        {
            Require(p is not null && ValidId(p.Id) && ids.Add(p.Id), "Invalid/duplicate predicate ID.");
            Require(p.All is not null && p.Any is not null && p.None is not null, "Null condition list.");
            if (p.Pattern is not null)
                Require(p.Scope is "title" or "body" or "titlePrefix" && p.Pattern.Length is > 0 and <= 4096
                    && p.All.Length + p.Any.Length + p.None.Length == 0, "Invalid match predicate.");
            else Require(p.Scope is null && p.IgnorePattern is null && p.All.Length + p.Any.Length + p.None.Length > 0,
                "Invalid Boolean predicate.");
            Require(p.IgnorePattern is null || p.IgnorePattern.Length is > 0 and <= 4096, "Invalid ignored pattern.");
        }
        var byId = doc.Predicates.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in doc.Predicates) Visit(p.Id, new HashSet<string>(StringComparer.Ordinal));
        ids.Clear(); var orders = new HashSet<int>();
        foreach (var r in doc.Rules)
            Require(r is not null && ValidId(r.Id) && ids.Add(r.Id) && r.Order >= 0 && orders.Add(r.Order)
                && r.Decision is "KEEP" or "REJECT" && !string.IsNullOrWhiteSpace(r.Category)
                && !string.IsNullOrWhiteSpace(r.Reason) && r.When is not null && byId.ContainsKey(r.When), "Invalid/duplicate rule.");
        Require(doc.Rules.Where(r => r.Decision == "KEEP").All(k =>
            doc.Rules.Where(r => r.Decision == "REJECT").All(r => k.Order < r.Order)), "KEEP guards must precede rejection rules.");
        return new(doc, bytes);

        int Visit(string id, HashSet<string> visiting)
        {
            Require(byId.ContainsKey(id) && visiting.Add(id), "Unknown or cyclic predicate reference.");
            if (depths.TryGetValue(id, out var cached)) { visiting.Remove(id); return cached; }
            Require(visiting.Count <= 33, "Excessively deep predicate reference.");
            var p = byId[id]; var depth = 0;
            foreach (var next in p.All.Concat(p.Any).Concat(p.None))
            {
                Require(next is not null, "Null predicate reference.");
                depth = Math.Max(depth, 1 + Visit(next, visiting));
            }
            Require(depth <= 32, "Excessively deep predicate reference.");
            visiting.Remove(id); depths[id] = depth; return depth;
        }
    }

    public CheapRejectDecision Decide(string title, string body)
    {
        var hash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { title, body })));
        var evidence = new List<CheapRejectEvidence>();
        CheapRejectDecision Result(string decision, string category, string reason, string[] ids, string[] guards, bool failOpen = false) =>
            new(Version, Fingerprint, hash, decision, category, reason, Array.AsReadOnly(ids),
                evidence.AsReadOnly(), Array.AsReadOnly(guards), failOpen);
        if (title.Length > 10_000 || body.Length > 1_000_000)
            return Result("KEEP", "input-limit", "KEEP: oversized input requires downstream review", ["engine-input-limit"], ["engine-input-limit"], true);
        try
        {
            var t = Normalize(title); var b = Normalize(body);
            var memo = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool Match(string id)
            {
                if (memo.TryGetValue(id, out var cached)) return cached;
                var p = predicates[id]; bool value;
                if (p.Pattern is not null)
                {
                    var input = p.Scope == "body" ? b : p.Scope == "titlePrefix" ? t.Split(" - ", 2)[0] : t;
                    value = false;
                    foreach (Match m in patterns[id].Matches(input))
                    {
                        if (ignored.TryGetValue(id, out var noise) && noise.IsMatch(m.Value)) continue;
                        evidence.Add(new(id, p.Scope!, m.Value[..Math.Min(m.Value.Length, 300)]));
                        value = true; break;
                    }
                }
                else value = p.All.All(Match) && (p.Any.Length == 0 || p.Any.Any(Match)) && !p.None.Any(Match);
                memo[id] = value; return value;
            }
            foreach (var guard in rules.Where(r => r.Decision == "KEEP"))
                if (Match(guard.When)) return Result("KEEP", guard.Category, guard.Reason, [guard.Id], [guard.Id]);
            var rejects = rules.Where(r => r.Decision == "REJECT" && Match(r.When)).ToArray();
            return rejects.Length == 0 ? Result("KEEP", "uncorroborated", defaultReason, ["engine-default-keep"], []) :
                Result("REJECT", string.Join(", ", rejects.Select(r => r.Category)),
                    string.Join("; ", rejects.Select(r => r.Reason)), rejects.Select(r => r.Id).ToArray(), []);
        }
        catch (RegexMatchTimeoutException)
        { return Result("KEEP", "timeout", "KEEP: rule matching timed out; downstream review required", ["engine-timeout"], ["engine-timeout"], true); }
    }

    private static string Normalize(string text) => Spaces.Replace(WebUtility.HtmlDecode(Tags.Replace(text, " ")), " ").Trim().ToLowerInvariant();
    private static Regex Compile(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, @"\A[a-z][a-z0-9-]{0,79}\z", RegexOptions.CultureInvariant, Timeout);
    private static void Require([DoesNotReturnIf(false)] bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { Require(names.Add(property.Name), "Duplicate JSON property."); RejectDuplicateProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
    private sealed record Document
    {
        public required int SchemaVersion { get; init; }
        public required string RulesetVersion { get; init; }
        public required string Name { get; init; }
        public required string Status { get; init; }
        public required string Description { get; init; }
        public required string DefaultReason { get; init; }
        public required Predicate[] Predicates { get; init; }
        public required Rule[] Rules { get; init; }
    }
    private sealed record Predicate
    {
        public required string Id { get; init; }
        public string? Scope { get; init; }
        public string? Pattern { get; init; }
        public string? IgnorePattern { get; init; }
        public string[] All { get; init; } = [];
        public string[] Any { get; init; } = [];
        public string[] None { get; init; } = [];
    }
    private sealed record Rule
    {
        public required string Id { get; init; }
        public required int Order { get; init; }
        public required string Decision { get; init; }
        public required string Category { get; init; }
        public required string Reason { get; init; }
        public required string When { get; init; }
    }
}
