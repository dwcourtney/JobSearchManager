using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

// Observation coordinates refer to the named, retained normalized segment, not HTML
// byte offsets. Original HTML is retained on the document; no false offset mapping.
internal sealed record FactEvidence(string Text, string CoordinateSpace, int Segment, int Start, int Length);
internal sealed record FactScope(string? Section, string? Geography, string? Temporal,
    string? JobLevel, string? EmploymentType, string RawApplicability);
internal sealed record FactValue(string Kind, string Raw, string? Code = null, decimal? Lower = null,
    decimal? Upper = null, string? Currency = null, string? Unit = null, string? Basis = null,
    IReadOnlyList<string>? Alternatives = null, string? UpperCurrency = null);
internal sealed record FactRelation(string Kind, string OtherObservationId);
internal sealed record FactualObservation(string Id, string Domain, string Type, FactValue Value,
    string Obligation, string Qualifier, FactScope Scope, FactEvidence Evidence,
    string ParserId, string RulesHash, string RuleId, string Source, string? Provider,
    string LogicalConnective, IReadOnlyList<string> Exclusions, IReadOnlyList<FactRelation> Relations, string Polarity = "affirmed");
internal sealed record FactObservationDocument(int SchemaVersion, string InputHash, string OriginalHtml,
    IReadOnlyList<string> Segments, IReadOnlyList<FactualObservation> Observations);

internal static class FactObservations
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static FactualObservation Create(string domain, string type, FactValue value, string obligation,
        string qualifier, FactScope scope, FactEvidence evidence, string parserId, string rulesHash,
        string ruleId, string source = "posting-body", string? provider = null,
        string connective = "unspecified", IReadOnlyList<string>? exclusions = null, string polarity = "affirmed")
    {
        // Keep repetitions at different evidence locations. No semantic deduplication.
        var key = JsonSerializer.Serialize(new { domain, type, value, obligation, qualifier, scope,
            evidence, parserId, rulesHash, ruleId, source, provider, connective, exclusions, polarity }, Json);
        return new(Hash(key), domain, type, value, obligation, qualifier, scope, evidence,
            parserId, rulesHash, ruleId, source, provider, connective, exclusions ?? [], [], polarity);
    }

    internal static FactualObservation[] LinkSponsorship(IReadOnlyList<FactualObservation> observations)
    {
        // This flags a review relationship, never resolves conditional/scoped claims.
        return observations.Select(o => o.Type != "sponsorship" ? o : o with {
            Relations = observations.Where(other => other.Id != o.Id && other.Type == "sponsorship" &&
                other.Source == o.Source && other.Value.Code != o.Value.Code &&
                other.Value.Code is "available" or "notAvailable" && o.Value.Code is "available" or "notAvailable")
                .Select(other => new FactRelation("potential-contradiction-review-scope", other.Id)).ToArray()
        }).ToArray();
    }
}
