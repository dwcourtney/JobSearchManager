using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace JobSearchManager;

internal sealed record LegacySemanticRuleDocument(
    int Version, IReadOnlyList<LegacySemanticConcept> Concepts);

internal sealed record LegacySemanticConcept(
    string Id,
    IReadOnlyList<string>? EvidencePatterns = null,
    IReadOnlyList<string>? TitleEvidencePatterns = null,
    IReadOnlyList<string>? TitleExclusionPatterns = null,
    bool RemoteDesignation = false,
    IReadOnlyList<string>? RemoteSignalCategories = null,
    IReadOnlyList<string>? ExtendedLocationCategories = null,
    IReadOnlyList<LegacyContextRule>? ContextRules = null);

internal sealed record LegacyContextRule(IReadOnlyList<string> RequiredPatterns);

internal static class LegacySemanticRuleMigrator
{
    public static void Migrate(SqliteConnection connection, SqliteTransaction transaction,
        string path, JobConceptCatalog catalog, SemanticRulePolicy policy)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Legacy semantic rule migration source is missing.", path);
        var document = JsonSerializer.Deserialize<LegacySemanticRuleDocument>(File.ReadAllBytes(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Legacy semantic rule migration source is invalid.");
        var now = DateTimeOffset.UtcNow;
        foreach (var concept in document.Concepts)
        {
            if (!catalog.Contains(concept.Id))
                throw new InvalidDataException($"Legacy rules reference unknown concept '{concept.Id}'.");
            AddMany(concept.Id, concept.EvidencePatterns, SemanticRuleScopes.Both,
                SemanticRuleTypes.PositiveEvidence);
            AddMany(concept.Id, concept.TitleEvidencePatterns, SemanticRuleScopes.Title,
                SemanticRuleTypes.TitleEvidence);
            AddMany(concept.Id, concept.TitleExclusionPatterns, SemanticRuleScopes.Title,
                SemanticRuleTypes.Exclusion);
            for (var index = 0; index < (concept.ContextRules?.Count ?? 0); index++)
            {
                var group = $"legacy:{concept.Id}:context:{index + 1}";
                AddMany(concept.Id, concept.ContextRules![index].RequiredPatterns,
                    SemanticRuleScopes.Both, SemanticRuleTypes.RequiredContext, group);
            }
            if (concept.RemoteDesignation)
                Add(concept.Id, "remote-designation", SemanticRuleScopes.Both,
                    SemanticRuleTypes.RemoteDesignation, null);
            AddMany(concept.Id, concept.RemoteSignalCategories, SemanticRuleScopes.Both,
                SemanticRuleTypes.RemoteSignal);
            AddMany(concept.Id, concept.ExtendedLocationCategories, SemanticRuleScopes.Both,
                SemanticRuleTypes.ExtendedLocationSignal);
        }

        void AddMany(string conceptId, IReadOnlyList<string>? patterns, string scope,
            string type, string? group = null)
        {
            foreach (var pattern in patterns ?? []) Add(conceptId, pattern, scope, type, group);
        }

        void Add(string conceptId, string pattern, string scope, string type, string? group)
        {
            var candidate = new SemanticRuleCandidate(conceptId, pattern, scope, type,
                $"legacy-regex-catalog-v{document.Version}",
                "Migrated from the last production RegEx detector; historical runtime usage was unavailable.",
                group, SemanticRuleStatuses.Active);
            SemanticRuleValidation.Validate(candidate, catalog, policy);
            var id = DeterministicId(conceptId, pattern, scope, type, group);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO SemanticRules(RuleId,ConceptId,Pattern,Scope,RuleType,Status,ContextGroupId,
                    CreatedUtc,LastModifiedUtc,LastMatchedUtc,LastReviewedUtc,RetiredUtc,
                    MatchCountLifetime,MatchCountSinceReview,Provenance,Reason)
                VALUES($id,$concept,$pattern,$scope,$type,'active',$group,$now,$now,NULL,NULL,NULL,
                    0,0,$provenance,$reason);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$concept", conceptId);
            command.Parameters.AddWithValue("$pattern", pattern);
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$type", type);
            command.Parameters.AddWithValue("$group", group is null ? DBNull.Value : group);
            command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$provenance", candidate.Provenance);
            command.Parameters.AddWithValue("$reason", candidate.Reason!);
            command.ExecuteNonQuery();
        }
    }

    private static string DeterministicId(params string?[] values)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values)));
        return "legacy-" + Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }
}
