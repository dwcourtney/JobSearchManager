using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace JobSearchManager;

public sealed record EvaluationRunSummary(
    string EvaluationRunId, DateTimeOffset EvaluatedUtc, string DatasetId, string DatasetRole,
    string DatasetDisplayName, string Purpose, string DatasetFingerprint,
    string RulesetFingerprint, string TaxonomyFingerprint, int TaxonomyVersion,
    string ConfigurationFingerprint,
    string LabelProvenance, string SamplingMethod, long? RandomSeed, int SampleSize,
    int PostingCount, int ConceptDecisionCount, int PositiveDecisionCount,
    int NegativeDecisionCount, string EvaluationStatus,
    double? MacroPrecision, double? MacroRecall, double? MacroF1,
    double? MicroPrecision, double? MicroRecall, double? MicroF1,
    double? HistoricalMacroPrecision, double? HistoricalMacroRecall, double? HistoricalMacroF1,
    double? HistoricalMicroPrecision, double? HistoricalMicroRecall, double? HistoricalMicroF1,
    string? Notes);

public sealed class SqliteSemanticRuleStore : IDisposable
{
    private const int SchemaVersion = 4;
    private readonly string _connectionString;
    private readonly JobConceptCatalog _catalog;
    private readonly SemanticRulePolicy _policy;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SqliteSemanticRuleStore(
        IConfiguration configuration,
        IHostEnvironment environment,
        HostingConfiguration hosting,
        JobConceptCatalog catalog)
        : this(ResolvePath(configuration, environment, hosting), catalog,
            configuration.GetSection("SemanticRules:Policy").Get<SemanticRulePolicy>() ?? new())
    {
        Initialize(Path.Combine(environment.ContentRootPath, "LegacyJobConceptRules.json"));
    }

    internal SqliteSemanticRuleStore(string databasePath, JobConceptCatalog catalog,
        SemanticRulePolicy? policy = null)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _catalog = catalog;
        _policy = policy ?? new();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }
    public SemanticRulePolicy Policy => _policy;

    internal void Initialize(string legacyRulesPath)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaInfo (
                SchemaVersion INTEGER NOT NULL,
                AppliedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SemanticRules (
                RuleId TEXT PRIMARY KEY,
                ConceptId TEXT NOT NULL,
                Pattern TEXT NOT NULL,
                Scope TEXT NOT NULL,
                RuleType TEXT NOT NULL,
                Status TEXT NOT NULL,
                ContextGroupId TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                LastMatchedUtc TEXT NULL,
                LastReviewedUtc TEXT NULL,
                RetiredUtc TEXT NULL,
                MatchCountLifetime INTEGER NOT NULL DEFAULT 0,
                MatchCountSinceReview INTEGER NOT NULL DEFAULT 0,
                TimeoutCountLifetime INTEGER NOT NULL DEFAULT 0,
                LastTimedOutUtc TEXT NULL,
                Provenance TEXT NOT NULL,
                Reason TEXT NULL,
                CHECK (MatchCountLifetime >= 0),
                CHECK (MatchCountSinceReview >= 0),
                CHECK (TimeoutCountLifetime >= 0)
            );
            CREATE INDEX IF NOT EXISTS IX_SemanticRules_StatusConcept
                ON SemanticRules(Status, ConceptId);
            CREATE INDEX IF NOT EXISTS IX_SemanticRules_LastMatched
                ON SemanticRules(LastMatchedUtc);
            CREATE TABLE IF NOT EXISTS RuleRelationships (
                SourceRuleId TEXT NOT NULL REFERENCES SemanticRules(RuleId),
                TargetRuleId TEXT NOT NULL REFERENCES SemanticRules(RuleId),
                RelationshipType TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                PRIMARY KEY (SourceRuleId, TargetRuleId, RelationshipType)
            );
            CREATE TABLE IF NOT EXISTS EvaluationRuns (
                EvaluationRunId TEXT PRIMARY KEY,
                EvaluatedUtc TEXT NOT NULL,
                RulesetFingerprint TEXT NOT NULL,
                ValidationCorpusFingerprint TEXT NOT NULL,
                TaxonomyFingerprint TEXT NOT NULL,
                TaxonomyVersion INTEGER NOT NULL DEFAULT 0,
                ConfigurationFingerprint TEXT NOT NULL,
                MacroPrecision REAL NULL, MacroRecall REAL NULL, MacroF1 REAL NULL,
                MicroPrecision REAL NULL, MicroRecall REAL NULL, MicroF1 REAL NULL,
                HistoricalMacroPrecision REAL NULL, HistoricalMacroRecall REAL NULL,
                HistoricalMacroF1 REAL NULL, HistoricalMicroPrecision REAL NULL,
                HistoricalMicroRecall REAL NULL, HistoricalMicroF1 REAL NULL,
                RuleCount INTEGER NOT NULL, ConceptCount INTEGER NOT NULL,
                DatasetId TEXT NOT NULL DEFAULT 'curated-regression-v1',
                DatasetRole TEXT NOT NULL DEFAULT 'development-regression',
                DatasetDisplayName TEXT NOT NULL DEFAULT 'CURATED REGRESSION BENCHMARK',
                Purpose TEXT NOT NULL DEFAULT '',
                LabelProvenance TEXT NOT NULL DEFAULT 'unknown',
                SamplingMethod TEXT NOT NULL DEFAULT 'unknown',
                RandomSeed INTEGER NULL,
                SampleSize INTEGER NOT NULL DEFAULT 0,
                PostingCount INTEGER NOT NULL DEFAULT 0,
                ConceptDecisionCount INTEGER NOT NULL DEFAULT 0,
                PositiveDecisionCount INTEGER NOT NULL DEFAULT 0,
                NegativeDecisionCount INTEGER NOT NULL DEFAULT 0,
                EvaluationStatus TEXT NOT NULL DEFAULT 'scored',
                Notes TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS RuleEvaluationResults (
                EvaluationRunId TEXT NOT NULL REFERENCES EvaluationRuns(EvaluationRunId),
                RuleId TEXT NOT NULL,
                ValidationMatchCount INTEGER NOT NULL,
                TruePositiveMatches INTEGER NOT NULL,
                FalsePositiveMatches INTEGER NOT NULL,
                Precision REAL NULL,
                UniqueTruePositives INTEGER NOT NULL,
                RedundantTruePositives INTEGER NOT NULL,
                RepresentativeExamplesJson TEXT NOT NULL,
                FalsePositiveExamplesJson TEXT NOT NULL,
                TimeoutCount INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (EvaluationRunId, RuleId)
            );
            CREATE TABLE IF NOT EXISTS ConceptEvaluationResults (
                EvaluationRunId TEXT NOT NULL REFERENCES EvaluationRuns(EvaluationRunId),
                ConceptId TEXT NOT NULL,
                TruePositive INTEGER NOT NULL, FalsePositive INTEGER NOT NULL,
                FalseNegative INTEGER NOT NULL, TrueNegative INTEGER NOT NULL,
                Precision REAL NULL, Recall REAL NULL, F1 REAL NULL,
                PRIMARY KEY (EvaluationRunId, ConceptId)
            );
            CREATE TABLE IF NOT EXISTS CandidateValidations (
                RuleId TEXT PRIMARY KEY REFERENCES SemanticRules(RuleId),
                RuleFingerprint TEXT NOT NULL,
                ValidatedUtc TEXT NOT NULL,
                EvidenceJson TEXT NOT NULL
            );
            """);
        var version = ScalarLong(connection, transaction,
            "SELECT COALESCE(MAX(SchemaVersion), 0) FROM SchemaInfo;");
        if (version > SchemaVersion)
            throw new InvalidDataException($"Semantic rule database schema {version} is newer than this application.");
        if (version is > 0 and < 3)
        {
            Execute(connection, transaction, """
                ALTER TABLE SemanticRules ADD COLUMN TimeoutCountLifetime INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE SemanticRules ADD COLUMN LastTimedOutUtc TEXT NULL;
                ALTER TABLE RuleEvaluationResults ADD COLUMN TimeoutCount INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN DatasetId TEXT NOT NULL DEFAULT 'curated-regression-v1';
                ALTER TABLE EvaluationRuns ADD COLUMN DatasetRole TEXT NOT NULL DEFAULT 'development-regression';
                ALTER TABLE EvaluationRuns ADD COLUMN DatasetDisplayName TEXT NOT NULL DEFAULT 'CURATED REGRESSION BENCHMARK';
                ALTER TABLE EvaluationRuns ADD COLUMN Purpose TEXT NOT NULL DEFAULT '';
                ALTER TABLE EvaluationRuns ADD COLUMN LabelProvenance TEXT NOT NULL DEFAULT 'unknown';
                ALTER TABLE EvaluationRuns ADD COLUMN SamplingMethod TEXT NOT NULL DEFAULT 'unknown';
                ALTER TABLE EvaluationRuns ADD COLUMN RandomSeed INTEGER NULL;
                ALTER TABLE EvaluationRuns ADD COLUMN SampleSize INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN PostingCount INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN ConceptDecisionCount INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN PositiveDecisionCount INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN NegativeDecisionCount INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN EvaluationStatus TEXT NOT NULL DEFAULT 'scored';
                """);
        }
        if (version is > 0 and < 4)
            Execute(connection, transaction, """
                ALTER TABLE EvaluationRuns ADD COLUMN TaxonomyVersion INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE EvaluationRuns ADD COLUMN HistoricalMacroPrecision REAL NULL;
                ALTER TABLE EvaluationRuns ADD COLUMN HistoricalMacroRecall REAL NULL;
                ALTER TABLE EvaluationRuns ADD COLUMN HistoricalMacroF1 REAL NULL;
                ALTER TABLE EvaluationRuns ADD COLUMN HistoricalMicroPrecision REAL NULL;
                ALTER TABLE EvaluationRuns ADD COLUMN HistoricalMicroRecall REAL NULL;
                ALTER TABLE EvaluationRuns ADD COLUMN HistoricalMicroF1 REAL NULL;
                """);
        if (version < SchemaVersion)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO SchemaInfo(SchemaVersion, AppliedUtc) VALUES ($v, $u);";
            command.Parameters.AddWithValue("$v", SchemaVersion);
            command.Parameters.AddWithValue("$u", Format(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
        if (ScalarLong(connection, transaction, "SELECT COUNT(*) FROM SemanticRules;") == 0)
            LegacySemanticRuleMigrator.Migrate(connection, transaction, legacyRulesPath, _catalog, _policy);
        transaction.Commit();
    }

    public async Task<SemanticRulesSnapshot> LoadRuntimeSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var rules = await ReadRulesAsync(connection,
            "WHERE Status IN ('active', 'review-due')", cancellationToken);
        var relationships = await ReadRelationshipsAsync(connection, cancellationToken);
        return new(SemanticRulesetFingerprint.Calculate(rules, relationships, _catalog, _policy),
            DateTimeOffset.UtcNow, rules, relationships);
    }

    public async Task<IReadOnlyList<SemanticRule>> ListRulesAsync(string? status = null,
        string? conceptId = null, CancellationToken cancellationToken = default)
    {
        if (status is not null && !SemanticRuleStatuses.All.Contains(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (conceptId is not null && !_catalog.Contains(conceptId))
            throw new ArgumentOutOfRangeException(nameof(conceptId));
        await using var connection = await OpenAsync(cancellationToken);
        var clauses = new List<string>();
        if (status is not null) clauses.Add("Status = $status");
        if (conceptId is not null) clauses.Add("ConceptId = $concept");
        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return await ReadRulesAsync(connection, where, cancellationToken,
            command =>
            {
                if (status is not null) command.Parameters.AddWithValue("$status", status);
                if (conceptId is not null) command.Parameters.AddWithValue("$concept", conceptId);
            });
    }

    public async Task<SemanticRule> CreateAsync(SemanticRuleCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Status != SemanticRuleStatuses.Proposed)
            throw new InvalidDataException("New and imported rules must begin in proposed status.");
        SemanticRuleValidation.Validate(candidate, _catalog, _policy);
        var now = DateTimeOffset.UtcNow;
        var rule = new SemanticRule(Guid.NewGuid().ToString("N"), candidate.ConceptId,
            candidate.Pattern, candidate.Scope, candidate.RuleType, candidate.Status,
            now, now, null, null, null, 0, 0, candidate.Provenance, candidate.Reason,
            candidate.ContextGroupId);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await InsertRuleAsync(connection, null, rule, cancellationToken);
            return rule;
        }
        finally { _writeGate.Release(); }
    }

    public async Task<SemanticRule> TransitionAsync(string ruleId, string nextStatus,
        CancellationToken cancellationToken = default)
        => await TransitionAsync(ruleId, nextStatus, requireValidation: true, cancellationToken);

    internal async Task<SemanticRule> TransitionForEvaluationAsync(string ruleId, string nextStatus,
        CancellationToken cancellationToken = default)
        => await TransitionAsync(ruleId, nextStatus, requireValidation: false, cancellationToken);

    private async Task<SemanticRule> TransitionAsync(string ruleId, string nextStatus,
        bool requireValidation, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var current = (await ReadRulesAsync(connection, "WHERE RuleId = $id", cancellationToken,
                command => command.Parameters.AddWithValue("$id", ruleId))).SingleOrDefault()
                ?? throw new KeyNotFoundException("Semantic rule not found.");
            SemanticRuleValidation.ValidateTransition(current.Status, nextStatus);
            if (requireValidation && current.Status == SemanticRuleStatuses.Proposed &&
                nextStatus == SemanticRuleStatuses.Validated)
            {
                await using var validation = connection.CreateCommand();
                validation.CommandText = """
                    SELECT COUNT(*) FROM CandidateValidations
                    WHERE RuleId=$id AND RuleFingerprint=$fingerprint;
                    """;
                validation.Parameters.AddWithValue("$id", ruleId);
                validation.Parameters.AddWithValue("$fingerprint",
                    SemanticRulesetFingerprint.RuleVersion(current));
                if (Convert.ToInt64(await validation.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new InvalidOperationException(
                        "A current comparative validation is required before this rule can be validated.");
            }
            var now = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE SemanticRules SET Status=$status, LastModifiedUtc=$modified,
                    LastReviewedUtc=CASE WHEN $reviewed = 1 THEN $modified ELSE LastReviewedUtc END,
                    MatchCountSinceReview=CASE WHEN $reviewed = 1 THEN 0 ELSE MatchCountSinceReview END,
                    RetiredUtc=CASE WHEN $status = 'retired' THEN $modified ELSE RetiredUtc END
                WHERE RuleId=$id;
                """;
            command.Parameters.AddWithValue("$status", nextStatus);
            command.Parameters.AddWithValue("$modified", Format(now));
            command.Parameters.AddWithValue("$reviewed",
                nextStatus is SemanticRuleStatuses.Active or SemanticRuleStatuses.Retired ? 1 : 0);
            command.Parameters.AddWithValue("$id", ruleId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return (await ReadRulesAsync(connection, "WHERE RuleId = $id", cancellationToken,
                value => value.Parameters.AddWithValue("$id", ruleId))).Single();
        }
        finally { _writeGate.Release(); }
    }

    public async Task SaveCandidateValidationAsync(SemanticRuleCandidateValidation evidence,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var rule = (await ReadRulesAsync(connection, "WHERE RuleId = $id", cancellationToken,
                command => command.Parameters.AddWithValue("$id", evidence.RuleId))).SingleOrDefault()
                ?? throw new KeyNotFoundException("Semantic rule not found.");
            if (rule.Status != SemanticRuleStatuses.Proposed ||
                evidence.RuleFingerprint != SemanticRulesetFingerprint.RuleVersion(rule) ||
                evidence.TaxonomyFingerprint != _catalog.Fingerprint)
                throw new InvalidDataException("Candidate validation does not match the current proposed rule.");
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CandidateValidations(RuleId,RuleFingerprint,ValidatedUtc,EvidenceJson)
                VALUES($id,$fingerprint,$utc,$json)
                ON CONFLICT(RuleId) DO UPDATE SET RuleFingerprint=excluded.RuleFingerprint,
                    ValidatedUtc=excluded.ValidatedUtc,EvidenceJson=excluded.EvidenceJson;
                """;
            command.Parameters.AddWithValue("$id", evidence.RuleId);
            command.Parameters.AddWithValue("$fingerprint", evidence.RuleFingerprint);
            command.Parameters.AddWithValue("$utc", Format(evidence.ValidatedUtc));
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(evidence,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<int> MarkReviewDueAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now.AddDays(-_policy.ReviewAfterDays);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE SemanticRules SET Status='review-due', LastModifiedUtc=$now
                WHERE Status='active' AND COALESCE(LastReviewedUtc, CreatedUtc) <= $cutoff;
                """;
            command.Parameters.AddWithValue("$now", Format(now));
            command.Parameters.AddWithValue("$cutoff", Format(cutoff));
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<int> ApplyRetiredRetentionAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now.AddDays(-_policy.RetiredRetentionDays);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE SemanticRules SET Status='deleted', LastModifiedUtc=$now
                WHERE Status='retired' AND RetiredUtc IS NOT NULL AND RetiredUtc <= $cutoff;
                """;
            command.Parameters.AddWithValue("$now", Format(now));
            command.Parameters.AddWithValue("$cutoff", Format(cutoff));
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    public async Task AddRelationshipAsync(string sourceRuleId, string targetRuleId,
        string relationshipType, CancellationToken cancellationToken = default)
    {
        if (!SemanticRuleRelationshipTypes.All.Contains(relationshipType) ||
            sourceRuleId == targetRuleId)
            throw new InvalidDataException("Invalid semantic-rule relationship.");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO RuleRelationships(SourceRuleId,TargetRuleId,RelationshipType,CreatedUtc)
                SELECT $s,$t,$r,$u WHERE EXISTS(SELECT 1 FROM SemanticRules WHERE RuleId=$s)
                    AND EXISTS(SELECT 1 FROM SemanticRules WHERE RuleId=$t);
                """;
            command.Parameters.AddWithValue("$s", sourceRuleId);
            command.Parameters.AddWithValue("$t", targetRuleId);
            command.Parameters.AddWithValue("$r", relationshipType);
            command.Parameters.AddWithValue("$u", Format(DateTimeOffset.UtcNow));
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new KeyNotFoundException("Relationship rules were not found.");
        }
        finally { _writeGate.Release(); }
    }

    public async Task ApplyUsageAsync(IReadOnlyDictionary<string, long> matches,
        DateTimeOffset matchedUtc, CancellationToken cancellationToken = default)
    {
        if (matches.Count == 0) return;
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var item in matches.Where(item => item.Value > 0))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    UPDATE SemanticRules SET LastMatchedUtc=$u,
                        MatchCountLifetime=MatchCountLifetime+$count,
                        MatchCountSinceReview=MatchCountSinceReview+$count
                    WHERE RuleId=$id AND Status IN ('active','review-due');
                    """;
                command.Parameters.AddWithValue("$u", Format(matchedUtc));
                command.Parameters.AddWithValue("$count", item.Value);
                command.Parameters.AddWithValue("$id", item.Key);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    public async Task ApplyTimeoutsAsync(IReadOnlyDictionary<string, long> timeouts,
        DateTimeOffset timedOutUtc, CancellationToken cancellationToken = default)
    {
        if (timeouts.Count == 0) return;
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var item in timeouts.Where(item => item.Value > 0))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    UPDATE SemanticRules SET LastTimedOutUtc=$u,
                        TimeoutCountLifetime=TimeoutCountLifetime+$count
                    WHERE RuleId=$id AND Status IN ('active','review-due');
                    """;
                command.Parameters.AddWithValue("$u", Format(timedOutUtc));
                command.Parameters.AddWithValue("$count", item.Value);
                command.Parameters.AddWithValue("$id", item.Key);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var export = new SemanticRuleExport(SchemaVersion, _catalog.Fingerprint,
            DateTimeOffset.UtcNow, await ReadRulesAsync(connection, "", cancellationToken),
            await ReadRelationshipsAsync(connection, cancellationToken));
        return JsonSerializer.Serialize(export, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { WriteIndented = true });
    }

    public async Task<IReadOnlyList<SemanticRule>> ImportCandidatesAsync(string json,
        string importProvenance, CancellationToken cancellationToken = default)
    {
        var document = JsonSerializer.Deserialize<SemanticRuleExport>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Rule import is empty.");
        if (document.SchemaVersion != SchemaVersion || document.TaxonomyFingerprint != _catalog.Fingerprint)
            throw new InvalidDataException("Rule import schema or taxonomy fingerprint does not match.");
        if (document.Rules.Select(item => item.RuleId).Distinct(StringComparer.Ordinal).Count() !=
            document.Rules.Count)
            throw new InvalidDataException("Rule import contains duplicate source rule identifiers.");
        var created = new List<SemanticRule>();
        var importedIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var value in document.Rules)
        {
            var candidate = new SemanticRuleCandidate(value.ConceptId, value.Pattern, value.Scope,
                value.RuleType, importProvenance, value.Reason, value.ContextGroupId,
                SemanticRuleStatuses.Proposed);
            SemanticRuleValidation.Validate(candidate, _catalog, _policy);
            var imported = new SemanticRule(Guid.NewGuid().ToString("N"), candidate.ConceptId,
                candidate.Pattern, candidate.Scope, candidate.RuleType, candidate.Status,
                now, now, null, null, null, 0, 0, candidate.Provenance, candidate.Reason,
                candidate.ContextGroupId);
            created.Add(imported);
            importedIds[value.RuleId] = imported.RuleId;
        }
        var relationships = document.Relationships.Select(relationship =>
        {
            if (!SemanticRuleRelationshipTypes.All.Contains(relationship.RelationshipType) ||
                !importedIds.TryGetValue(relationship.SourceRuleId, out var source) ||
                !importedIds.TryGetValue(relationship.TargetRuleId, out var target) || source == target)
                throw new InvalidDataException("Rule import contains an invalid relationship.");
            return new SemanticRuleRelationship(source, target, relationship.RelationshipType, now);
        }).ToArray();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var rule in created)
                await InsertRuleAsync(connection, (SqliteTransaction)transaction, rule, cancellationToken);
            foreach (var relationship in relationships)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO RuleRelationships(SourceRuleId,TargetRuleId,RelationshipType,CreatedUtc)
                    VALUES($s,$t,$r,$u);
                    """;
                command.Parameters.AddWithValue("$s", relationship.SourceRuleId);
                command.Parameters.AddWithValue("$t", relationship.TargetRuleId);
                command.Parameters.AddWithValue("$r", relationship.RelationshipType);
                command.Parameters.AddWithValue("$u", Format(relationship.CreatedUtc));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
        return created;
    }

    public async Task SaveEvaluationAsync(RegexEvaluationReport report,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO EvaluationRuns(EvaluationRunId,EvaluatedUtc,RulesetFingerprint,
                        ValidationCorpusFingerprint,TaxonomyFingerprint,TaxonomyVersion,ConfigurationFingerprint,
                        MacroPrecision,MacroRecall,MacroF1,MicroPrecision,MicroRecall,MicroF1,
                        HistoricalMacroPrecision,HistoricalMacroRecall,HistoricalMacroF1,
                        HistoricalMicroPrecision,HistoricalMicroRecall,HistoricalMicroF1,
                        RuleCount,ConceptCount,DatasetId,DatasetRole,DatasetDisplayName,Purpose,
                        LabelProvenance,SamplingMethod,RandomSeed,SampleSize,PostingCount,
                        ConceptDecisionCount,PositiveDecisionCount,NegativeDecisionCount,
                        EvaluationStatus,Notes)
                    VALUES($id,$utc,$rules,$corpus,$taxonomy,$taxonomyVersion,$config,$map,$mar,$maf,
                        $mip,$mir,$mif,$hmap,$hmar,$hmaf,$hmip,$hmir,$hmif,
                        $ruleCount,$conceptCount,$datasetId,$datasetRole,$datasetName,
                        $purpose,$provenance,$sampling,$seed,$sampleSize,$postingCount,$decisions,
                        $positive,$negative,$status,$notes);
                    """;
                command.Parameters.AddWithValue("$id", report.EvaluationRunId);
                command.Parameters.AddWithValue("$utc", Format(report.EvaluatedUtc));
                command.Parameters.AddWithValue("$rules", report.RulesetFingerprint);
                command.Parameters.AddWithValue("$corpus", report.ValidationCorpusFingerprint);
                command.Parameters.AddWithValue("$taxonomy", report.TaxonomyFingerprint);
                command.Parameters.AddWithValue("$taxonomyVersion", report.TaxonomyVersion);
                command.Parameters.AddWithValue("$config", report.ConfigurationFingerprint);
                command.Parameters.AddWithValue("$map", Db(report.Macro.Precision));
                command.Parameters.AddWithValue("$mar", Db(report.Macro.Recall));
                command.Parameters.AddWithValue("$maf", Db(report.Macro.F1));
                command.Parameters.AddWithValue("$mip", Db(report.Micro.Precision));
                command.Parameters.AddWithValue("$mir", Db(report.Micro.Recall));
                command.Parameters.AddWithValue("$mif", Db(report.Micro.F1));
                command.Parameters.AddWithValue("$hmap", Db(report.HistoricalBenchmarkMacro.Precision));
                command.Parameters.AddWithValue("$hmar", Db(report.HistoricalBenchmarkMacro.Recall));
                command.Parameters.AddWithValue("$hmaf", Db(report.HistoricalBenchmarkMacro.F1));
                command.Parameters.AddWithValue("$hmip", Db(report.HistoricalBenchmarkMicro.Precision));
                command.Parameters.AddWithValue("$hmir", Db(report.HistoricalBenchmarkMicro.Recall));
                command.Parameters.AddWithValue("$hmif", Db(report.HistoricalBenchmarkMicro.F1));
                command.Parameters.AddWithValue("$ruleCount", report.RuleCount);
                command.Parameters.AddWithValue("$conceptCount", report.Concepts.Count);
                command.Parameters.AddWithValue("$datasetId", report.DatasetId);
                command.Parameters.AddWithValue("$datasetRole", report.DatasetRole);
                command.Parameters.AddWithValue("$datasetName", report.DatasetDisplayName);
                command.Parameters.AddWithValue("$purpose", report.Purpose);
                command.Parameters.AddWithValue("$provenance", report.LabelProvenance);
                command.Parameters.AddWithValue("$sampling", report.SamplingMethod);
                command.Parameters.AddWithValue("$seed", report.RandomSeed is null ? DBNull.Value : report.RandomSeed.Value);
                command.Parameters.AddWithValue("$sampleSize", report.PostingCount);
                command.Parameters.AddWithValue("$postingCount", report.PostingCount);
                command.Parameters.AddWithValue("$decisions", report.ConceptDecisionCount);
                command.Parameters.AddWithValue("$positive", report.PositiveDecisionCount);
                command.Parameters.AddWithValue("$negative", report.NegativeDecisionCount);
                command.Parameters.AddWithValue("$status", report.EvaluationStatus);
                command.Parameters.AddWithValue("$notes", report.Notes is null ? DBNull.Value : report.Notes);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var result in report.Rules)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO RuleEvaluationResults(EvaluationRunId,RuleId,ValidationMatchCount,
                        TruePositiveMatches,FalsePositiveMatches,Precision,UniqueTruePositives,
                        RedundantTruePositives,RepresentativeExamplesJson,FalsePositiveExamplesJson,
                        TimeoutCount)
                    VALUES($run,$rule,$matches,$tp,$fp,$precision,$unique,$redundant,$examples,
                        $falseExamples,$timeouts);
                    """;
                command.Parameters.AddWithValue("$run", report.EvaluationRunId);
                command.Parameters.AddWithValue("$rule", result.RuleId);
                command.Parameters.AddWithValue("$matches", result.ValidationMatchCount);
                command.Parameters.AddWithValue("$tp", result.TruePositiveMatches);
                command.Parameters.AddWithValue("$fp", result.FalsePositiveMatches);
                command.Parameters.AddWithValue("$precision", Db(result.Precision));
                command.Parameters.AddWithValue("$unique", result.UniqueTruePositives);
                command.Parameters.AddWithValue("$redundant", result.RedundantTruePositives);
                command.Parameters.AddWithValue("$examples", JsonSerializer.Serialize(result.RepresentativeExamples));
                command.Parameters.AddWithValue("$falseExamples", JsonSerializer.Serialize(result.FalsePositiveExamples));
                command.Parameters.AddWithValue("$timeouts", result.TimeoutCount);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var result in report.Concepts)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO ConceptEvaluationResults VALUES($run,$concept,$tp,$fp,$fn,$tn,
                        $precision,$recall,$f1);
                    """;
                command.Parameters.AddWithValue("$run", report.EvaluationRunId);
                command.Parameters.AddWithValue("$concept", result.ConceptId);
                command.Parameters.AddWithValue("$tp", result.TruePositive);
                command.Parameters.AddWithValue("$fp", result.FalsePositive);
                command.Parameters.AddWithValue("$fn", result.FalseNegative);
                command.Parameters.AddWithValue("$tn", result.TrueNegative);
                command.Parameters.AddWithValue("$precision", Db(result.Precision));
                command.Parameters.AddWithValue("$recall", Db(result.Recall));
                command.Parameters.AddWithValue("$f1", Db(result.F1));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<EvaluationRunSummary>> ListEvaluationRunsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<EvaluationRunSummary>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EvaluationRunId,EvaluatedUtc,DatasetId,DatasetRole,DatasetDisplayName,Purpose,
                ValidationCorpusFingerprint,RulesetFingerprint,TaxonomyFingerprint,TaxonomyVersion,
                ConfigurationFingerprint,LabelProvenance,SamplingMethod,RandomSeed,SampleSize,
                PostingCount,ConceptDecisionCount,PositiveDecisionCount,NegativeDecisionCount,
                EvaluationStatus,MacroPrecision,MacroRecall,MacroF1,MicroPrecision,MicroRecall,
                MicroF1,HistoricalMacroPrecision,HistoricalMacroRecall,HistoricalMacroF1,
                HistoricalMicroPrecision,HistoricalMicroRecall,HistoricalMicroF1,Notes
            FROM EvaluationRuns ORDER BY EvaluatedUtc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new(reader.GetString(0), Parse(reader.GetString(1)), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetInt32(9), reader.GetString(10),
                reader.GetString(11), reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetInt64(13),
                reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16), reader.GetInt32(17),
                reader.GetInt32(18), reader.GetString(19), NullableDouble(reader, 20),
                NullableDouble(reader, 21), NullableDouble(reader, 22), NullableDouble(reader, 23),
                NullableDouble(reader, 24), NullableDouble(reader, 25),
                NullableDouble(reader, 26), NullableDouble(reader, 27), NullableDouble(reader, 28),
                NullableDouble(reader, 29), NullableDouble(reader, 30), NullableDouble(reader, 31),
                reader.IsDBNull(32) ? null : reader.GetString(32)));
        return results;
    }

    public async Task BackupAsync(string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var source = await OpenAsync(cancellationToken);
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }
        finally { _writeGate.Release(); }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Configure(connection);
        return connection;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        Configure(connection);
        return connection;
    }

    private static void Configure(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }

    private static async Task InsertRuleAsync(SqliteConnection connection, SqliteTransaction? transaction,
        SemanticRule rule, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SemanticRules(RuleId,ConceptId,Pattern,Scope,RuleType,Status,ContextGroupId,
                CreatedUtc,LastModifiedUtc,LastMatchedUtc,LastReviewedUtc,RetiredUtc,
                MatchCountLifetime,MatchCountSinceReview,TimeoutCountLifetime,LastTimedOutUtc,
                Provenance,Reason)
            VALUES($id,$concept,$pattern,$scope,$type,$status,$group,$created,$modified,
                $matched,$reviewed,$retired,$lifetime,$recent,$timeouts,$lastTimeout,
                $provenance,$reason);
            """;
        command.Parameters.AddWithValue("$id", rule.RuleId);
        command.Parameters.AddWithValue("$concept", rule.ConceptId);
        command.Parameters.AddWithValue("$pattern", rule.Pattern);
        command.Parameters.AddWithValue("$scope", rule.Scope);
        command.Parameters.AddWithValue("$type", rule.RuleType);
        command.Parameters.AddWithValue("$status", rule.Status);
        command.Parameters.AddWithValue("$group", Db(rule.ContextGroupId));
        command.Parameters.AddWithValue("$created", Format(rule.CreatedUtc));
        command.Parameters.AddWithValue("$modified", Format(rule.LastModifiedUtc));
        command.Parameters.AddWithValue("$matched", Db(rule.LastMatchedUtc));
        command.Parameters.AddWithValue("$reviewed", Db(rule.LastReviewedUtc));
        command.Parameters.AddWithValue("$retired", Db(rule.RetiredUtc));
        command.Parameters.AddWithValue("$lifetime", rule.MatchCountLifetime);
        command.Parameters.AddWithValue("$recent", rule.MatchCountSinceReview);
        command.Parameters.AddWithValue("$timeouts", rule.TimeoutCountLifetime);
        command.Parameters.AddWithValue("$lastTimeout", Db(rule.LastTimedOutUtc));
        command.Parameters.AddWithValue("$provenance", rule.Provenance);
        command.Parameters.AddWithValue("$reason", Db(rule.Reason));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<SemanticRule>> ReadRulesAsync(SqliteConnection connection,
        string where, CancellationToken cancellationToken, Action<SqliteCommand>? configure = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT RuleId,ConceptId,Pattern,Scope,RuleType,Status,CreatedUtc,LastModifiedUtc,
                LastMatchedUtc,LastReviewedUtc,RetiredUtc,MatchCountLifetime,
                MatchCountSinceReview,Provenance,Reason,ContextGroupId,
                TimeoutCountLifetime,LastTimedOutUtc
            FROM SemanticRules {where} ORDER BY ConceptId,RuleType,RuleId;
            """;
        configure?.Invoke(command);
        var values = new List<SemanticRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), Parse(reader.GetString(6)),
                Parse(reader.GetString(7)), NullableDate(reader, 8), NullableDate(reader, 9),
                NullableDate(reader, 10), reader.GetInt64(11), reader.GetInt64(12), reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15), reader.GetInt64(16),
                NullableDate(reader, 17)));
        return values;
    }

    private static async Task<IReadOnlyList<SemanticRuleRelationship>> ReadRelationshipsAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceRuleId,TargetRuleId,RelationshipType,CreatedUtc FROM RuleRelationships ORDER BY SourceRuleId,TargetRuleId;";
        var values = new List<SemanticRuleRelationship>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                Parse(reader.GetString(3))));
        return values;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ResolvePath(IConfiguration configuration, IHostEnvironment environment,
        HostingConfiguration hosting) => configuration["SemanticRules:DatabasePath"] ??
        (hosting.IsContainer ? "/app/data/regex-rules.db" :
            Path.Combine(environment.ContentRootPath, "data", "regex-rules.db"));

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O");
    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static object Db(DateTimeOffset? value) => value is null ? DBNull.Value : Format(value.Value);
    private static object Db(double? value) => value.HasValue ? value.Value : DBNull.Value;
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value,
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal);
    private static DateTimeOffset? NullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));

    private static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    public void Dispose()
    {
        _writeGate.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
