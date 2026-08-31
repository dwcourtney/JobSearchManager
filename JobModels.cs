using System.Text.Json.Serialization;

namespace JobSearchManager;

public sealed class JobSourceOptions
{
    public int PageSize { get; init; } = 20;
    public int DetailConcurrency { get; init; } = 6;
    public int RequestTimeoutSeconds { get; init; } = 30;
    public int MaximumListingPages { get; init; } = 200;
    public int MaximumDetailRequestsPerRefresh { get; init; } = 200;
    public int MaximumRevalidationsPerRefresh { get; init; } = 25;
    public int DetailReuseHours { get; init; } = 168;
    public int SourceSwitchCacheFreshnessMinutes { get; init; } = 15;
}

public static class FacetDefaults
{
    public const string AllCountriesLabel = "All countries";
    public const string AllLocationsLabel = "All locations";
    public const string UnitedStatesCountryId = "bc33aa3152ec42d4995f4791a106ed09";
    public const string UnitedStatesCountryLabel = "United States of America";
    public static FacetSelection UnitedStatesCountry { get; } =
        new(UnitedStatesCountryId, UnitedStatesCountryLabel);
}

public sealed record FacetSelection(string? Id, string Label);

public sealed record JobSourceQuery(
    string? CountryId,
    string CountryLabel,
    bool IncludeAllLocations = false,
    bool IncludeRemote = true,
    IReadOnlyList<FacetSelection>? PhysicalLocations = null,
    int SourceModelVersion = 2,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocationId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocationLabel = null,
    string CompanyId = CompanyCatalog.DefaultCompanyId)
{
    public static JobSourceQuery FromSettings(ViewerSettings settings, CompanyCatalog catalog)
    {
        if (settings.HasConfiguredSource != true)
        {
            throw new InvalidOperationException("No job source has been applied.");
        }
        var company = catalog.Get(settings.CompanyId);
        return new JobSourceQuery(
            settings.Country.Id,
            settings.Country.Label,
            settings.IncludeAllLocations,
            settings.IncludeRemote,
            settings.SelectedPhysicalLocations,
            2,
            null,
            null,
            company.Id).Normalize(company);
    }

    public JobSourceQuery Normalize(CompanyDefinition company)
    {
        var countryId = string.IsNullOrWhiteSpace(CountryId) ? null : CountryId.Trim();
        var countryLabel = string.IsNullOrWhiteSpace(CountryLabel)
            ? countryId ?? FacetDefaults.AllCountriesLabel
            : CountryLabel.Trim();

        var includeAll = IncludeAllLocations;
        var includeRemote = IncludeRemote;
        IEnumerable<FacetSelection> physical = PhysicalLocations ?? [];

        // Jobs-cache documents written by the single-location model contain
        // locationId/locationLabel. Canonicalize them through the same migration
        // rules as settings before comparing cache identity.
        if (!string.IsNullOrWhiteSpace(LocationLabel) || !string.IsNullOrWhiteSpace(LocationId))
        {
            if (string.IsNullOrWhiteSpace(LocationId))
            {
                includeAll = true;
                includeRemote = true;
                physical = [];
            }
            else if (company.IsRemoteLocation(LocationId))
            {
                includeAll = false;
                includeRemote = true;
                physical = [];
            }
            else
            {
                includeAll = false;
                includeRemote = false;
                physical = [new FacetSelection(LocationId.Trim(), LocationLabel?.Trim() ?? LocationId.Trim())];
            }
        }

        includeRemote = company.RemoteLocationIds.Count > 0 && (includeAll || includeRemote);
        var normalizedPhysical = includeAll
            ? []
            : physical
                .Where(location => !string.IsNullOrWhiteSpace(location?.Id) &&
                    !company.IsRemoteLocation(location.Id))
                .Select(location => new FacetSelection(
                    location.Id!.Trim(),
                    string.IsNullOrWhiteSpace(location.Label) ? location.Id.Trim() : location.Label.Trim()))
                .GroupBy(location => location.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(location => location.Id, StringComparer.Ordinal)
                .ToArray();

        return new JobSourceQuery(
            countryId,
            countryLabel,
            includeAll,
            includeRemote,
            normalizedPhysical,
            2,
            null,
            null,
            company.Id);
    }

    public IReadOnlyList<string> EffectiveLocationIds(CompanyDefinition company)
    {
        var query = Normalize(company);
        if (query.IncludeAllLocations)
        {
            return [];
        }

        return (query.PhysicalLocations ?? [])
            .Select(location => location.Id!)
            .Concat(query.IncludeRemote ? company.RemoteLocationIdsForCountry(query.CountryId) : [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    public bool IsEquivalentTo(JobSourceQuery? other, CompanyCatalog catalog)
    {
        if (other is null)
        {
            return false;
        }

        if (!string.Equals(CompanyId, other.CompanyId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var company = catalog.Get(CompanyId);
        var left = Normalize(company);
        var right = other.Normalize(company);
        return string.Equals(left.CompanyId, right.CompanyId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.CountryId, right.CountryId, StringComparison.Ordinal) &&
            left.IncludeAllLocations == right.IncludeAllLocations &&
            left.IncludeRemote == right.IncludeRemote &&
            left.EffectiveLocationIds(company).SequenceEqual(
                right.EffectiveLocationIds(company), StringComparer.Ordinal);
    }
}

public sealed record FacetOption(string Id, string Label, int Count, string? DisplayLabel = null);

public sealed record LocationFacetGroup(
    string Id,
    string Label,
    IReadOnlyList<FacetOption> Locations);

public sealed record LocationFacetOptions(
    int MatchingJobs,
    IReadOnlyList<FacetOption> Countries,
    IReadOnlyList<FacetOption> Locations,
    IReadOnlyList<FacetOption> RemoteLocations,
    IReadOnlyList<LocationFacetGroup> Groups,
    int PhysicalLocationCount,
    int StateMappedLocationCount,
    IReadOnlyList<string> UnmappedLocationLabels);

public sealed record JobRecord(
    string Title,
    string RequisitionId,
    DateOnly? StartDate,
    string PostedOn,
    string PrimaryLocation,
    IReadOnlyList<string> AdditionalLocations,
    string TimeType,
    string SourceUrl,
    string DescriptionHtml,
    decimal? PayMinimum,
    decimal? PayMaximum,
    string PayPeriod,
    string PayParseStatus,
    bool IsRemoteLocationRestricted,
    string? RemoteLocationRestrictionCategory,
    string? RemoteLocationRestrictionSnippet,
    string? DetailError,
    string ExternalPath,
    string ClearanceLevel = "noneMentioned",
    string ClearanceRequirement = "none",
    bool PolygraphRequired = false,
    string? ClearanceEvidence = null,
    string ClearanceParseStatus = "not-mentioned",
    IReadOnlyList<CredentialMatch>? Credentials = null,
    IReadOnlyList<string>? UnrecognizedCredentialMentions = null,
    int CredentialCatalogVersion = 0,
    AcademicQualificationAnalysis? AcademicQualification = null,
    string CompanyId = CompanyCatalog.DefaultCompanyId,
    WorkAuthorizationAnalysis? WorkAuthorization = null,
    RemoteWorkAnalysis? RemoteWork = null,
    // Read-only compatibility input for cache documents written before the product rename.
    [property: JsonPropertyName("workdayUrl")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacySourceUrl = null,
    string? ListingFingerprint = null,
    DateTimeOffset? DetailCachedAtUtc = null,
    int AnalysisVersion = 0,
    bool IsSourceAvailable = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CompressedDescriptionHtml = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? IdentityDiscriminator = null,
    IReadOnlyList<UnknownCredentialRequirement>? UnknownCredentialRequirements = null,
    ExtendedLocationRequirementAnalysis? ExtendedLocationRequirement = null,
    IReadOnlyList<DetectedJobConcept>? DetectedConcepts = null,
    int JobConceptCatalogVersion = 0)
{
    public string StableId => $"{CompanyId}:{(!string.IsNullOrWhiteSpace(RequisitionId)
        ? RequisitionId
        : $"path:{ExternalPath}")}{(string.IsNullOrWhiteSpace(IdentityDiscriminator)
            ? ""
            : $":variant:{IdentityDiscriminator}")}";
}

public sealed record CredentialMatch(
    string CredentialId,
    string Name,
    string FullName,
    string Issuer,
    string Type,
    string Category,
    string Requirement,
    bool IsAlternative,
    bool EquivalentAccepted,
    bool InProgressAccepted,
    bool PostHireAcquisitionAllowed,
    string Evidence,
    string Family = "",
    IReadOnlyList<string>? LegacyNames = null,
    IReadOnlyList<string>? EquivalentCredentialIds = null,
    IReadOnlyList<string>? RelatedCredentialIds = null,
    string? AlternativeGroup = null);

public sealed record UnknownCredentialRequirement(
    string Name,
    string Requirement,
    bool EquivalentAccepted,
    string Evidence);

public sealed record AcademicQualificationAnalysis(
    string MinimumLevel,
    string? SpecificDegree,
    string RequirementType,
    bool ExperienceSubstitutionAccepted,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> PreferredLevels,
    IReadOnlyList<AcademicQualificationPath> Paths,
    IReadOnlyList<string> Evidence,
    string ParseStatus,
    int AnalysisVersion,
    IReadOnlyList<AcademicAccreditation>? Accreditations = null);

public sealed record AcademicAccreditation(
    string Name,
    string Requirement,
    string Evidence);

public sealed record AcademicQualificationPath(
    string Level,
    string? SpecificDegree,
    string Requirement,
    int? MinimumExperienceYears,
    int? MaximumExperienceYears,
    IReadOnlyList<string> Fields,
    string Evidence);

public sealed record WorkAuthorizationAnalysis(
    string Eligibility,
    string Sponsorship,
    string Strength,
    string SponsorshipStrength,
    string? CountryCode,
    IReadOnlyList<string> Evidence,
    string ParseStatus,
    int AnalysisVersion);

public sealed record RemoteWorkAnalysis(
    bool IsRemoteDesignated,
    string ConcernLevel,
    string? Summary,
    IReadOnlyList<RemoteWorkSignal> Signals,
    string ParseStatus,
    int AnalysisVersion);

public sealed record RemoteWorkSignal(
    string Category,
    string ConcernLevel,
    string Reason,
    string Evidence);

public sealed record ExtendedLocationRequirementAnalysis(
    string Confidence,
    string? Destination,
    string? Summary,
    IReadOnlyList<ExtendedLocationRequirementSignal> Signals,
    string ParseStatus,
    int AnalysisVersion);

public sealed record ExtendedLocationRequirementSignal(
    string Category,
    string Confidence,
    string Reason,
    string Evidence);

public sealed record DetectedJobConcept(string ConceptId, string Evidence);

internal sealed record SalaryAnalysis(
    decimal? Minimum,
    decimal? Maximum,
    string Period,
    string ParseStatus);

internal sealed record RemoteLocationAnalysis(
    bool IsRestricted,
    string? Category,
    string? Snippet);

internal sealed record ClearanceAnalysis(
    string Level,
    string Requirement,
    bool PolygraphRequired,
    string? Evidence,
    string ParseStatus);

internal sealed record CredentialAnalysis(
    IReadOnlyList<CredentialMatch> Credentials,
    IReadOnlyList<string> UnrecognizedMentions,
    IReadOnlyList<UnknownCredentialRequirement> UnknownRequirements,
    int CatalogVersion);

public sealed record JobSourceFetchResult(
    IReadOnlyList<JobRecord> Jobs,
    int ListingCount,
    int DetailFailureCount,
    RefreshMetrics? Metrics = null);

public sealed record RefreshMetrics(
    int ListingsFetched,
    int DetailRequests,
    int CacheHits,
    int CacheMisses,
    int Classified,
    int ReclassifiedLocally,
    int DeferredDetails,
    int RemovedListings,
    bool ListingsTruncated,
    long ElapsedMilliseconds);

public sealed record ListingIdentity(
    string StableId,
    string RequisitionId,
    string ExternalPath);

public sealed record RefreshProgress(
    string Phase,
    int Completed,
    int? Total);

public sealed record JobsSnapshot(
    IReadOnlyList<JobRecord> Jobs,
    int TotalJobs,
    DateTimeOffset? LastRefreshedUtc,
    bool IsRefreshing,
    string? Error,
    int DetailFailureCount,
    bool IsCached,
    IReadOnlyList<string> NewJobIds,
    JobSourceQuery Query,
    IReadOnlyDictionary<string, string> JobStates,
    IReadOnlyDictionary<string, JobClosureInfo> JobClosures,
    RefreshProgress? RefreshProgress,
    RefreshMetrics? Metrics = null)
{
    public static JobsSnapshot Empty { get; } =
        new([], 0, null, false, null, 0, false, [], new JobSourceQuery(
            null,
            FacetDefaults.AllCountriesLabel,
            true,
            false,
            []), new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, JobClosureInfo>(StringComparer.Ordinal), null);
}

public sealed record JobClosureInfo(
    string Reason,
    DateTimeOffset ClosedAt,
    DateTimeOffset? AppliedAt);

public sealed record CompanySourceSettings(
    FacetSelection Country,
    bool IncludeAllLocations,
    bool IncludeRemote,
    IReadOnlyList<FacetSelection> SelectedPhysicalLocations);

public sealed record PendingJobSource(string CompanyId, CompanySourceSettings Source);

public static class ThemeModes
{
    public const string Default = "light";
    public static bool IsSupported(string? value) => value is
        "light" or "dark" or "nord-polar-night" or "nord-snow-storm" or "dracula";
    public static string Normalize(string? value) => IsSupported(value) ? value! : Default;
}

public static class JobFitPreferenceLevels
{
    public const string Neutral = "neutral";
    public const string Ideal = "ideal";
    public const string Positive = "positive";
    public const string Negative = "negative";
    // Retained as read-only compatibility values for previously saved workspaces.
    public const string StrongPositive = "strongPositive";
    public const string StrongNegative = "strongNegative";
    public const string HardConflict = "hardConflict";

    public static bool IsSupported(string? value) => value is
        Neutral or Ideal or Positive or Negative or StrongPositive or StrongNegative or HardConflict;

    public static string? Normalize(string? value) => value switch
    {
        StrongPositive => Ideal,
        StrongNegative => Negative,
        Neutral or Ideal or Positive or Negative or HardConflict => value,
        _ => null
    };
}

public sealed record JobFitSignalPreference(string ConceptId, string Preference);

public static class JobFitGroupHardConflicts
{
    public const string SoftwareDevelopment = "software-development";
    public const string AiData = "ai-data";
    public const string CloudPlatformAutomation = "cloud-platform-automation";
    public const string SystemsAdministration = "systems-administration";
    public const string NetworkPhysicalInfrastructure = "network-physical-infrastructure";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [
            SoftwareDevelopment,
            AiData,
            CloudPlatformAutomation,
            SystemsAdministration,
            NetworkPhysicalInfrastructure
        ],
        StringComparer.Ordinal);

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
            .Where(value => value is not null && Supported.Contains(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
}

public static class TravelTolerance
{
    public const int Minimum = 0;
    public const int Maximum = 6;
    public const int Default = 4;

    private static readonly IReadOnlyDictionary<string, int> LegacyConceptLevels =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["work.travel.occasional"] = 3,
            ["work.travel.moderate"] = 4,
            ["work.travel.frequent"] = 5,
            ["work.travel.substantial"] = 6
        };

    public static bool IsSupported(int? value) => value is >= Minimum and <= Maximum;

    public static bool IsLegacyConcept(string? conceptId) =>
        conceptId is not null && LegacyConceptLevels.ContainsKey(conceptId);

    public static int? Normalize(
        int? value,
        IEnumerable<JobFitSignalPreference>? legacySignals = null)
    {
        if (IsSupported(value))
        {
            return value!.Value;
        }

        var restrictive = new List<int>();
        var permissive = new List<int>();
        foreach (var signal in legacySignals ?? [])
        {
            if (signal is null || !LegacyConceptLevels.TryGetValue(signal.ConceptId, out var level))
            {
                continue;
            }

            var preference = JobFitPreferenceLevels.Normalize(signal.Preference);
            if (preference == JobFitPreferenceLevels.HardConflict)
            {
                restrictive.Add(level - 2);
            }
            else if (preference == JobFitPreferenceLevels.Negative)
            {
                restrictive.Add(level - 1);
            }
            else if (preference is JobFitPreferenceLevels.Positive or JobFitPreferenceLevels.Ideal)
            {
                permissive.Add(level);
            }
        }

        if (restrictive.Count > 0)
        {
            return Math.Clamp(restrictive.Min(), Minimum, Maximum);
        }

        return permissive.Count > 0
            ? Math.Clamp(Math.Max(Default, permissive.Max()), Minimum, Maximum)
            : null;
    }
}

public static class WorkLocationPreference
{
    public const int Minimum = 0;
    public const int Maximum = 5;
    public const int Default = 3;

    private static readonly IReadOnlyDictionary<string, int> LegacyConceptLevels =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["work.remote.full"] = 0,
            ["work.remote"] = 2,
            ["work.hybrid"] = 3,
            ["work.onsite"] = 5
        };

    public static bool IsSupported(int? value) => value is >= Minimum and <= Maximum;

    public static bool IsLegacyConcept(string? conceptId) =>
        conceptId is not null && LegacyConceptLevels.ContainsKey(conceptId);

    public static int? Normalize(
        int? value,
        IEnumerable<JobFitSignalPreference>? legacySignals = null)
    {
        if (IsSupported(value))
        {
            return value!.Value;
        }

        var signals = (legacySignals ?? [])
            .Where(signal => signal is not null && LegacyConceptLevels.ContainsKey(signal.ConceptId))
            .GroupBy(signal => signal.ConceptId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        // 100% Remote is the specific form of the generic Remote signal. Treating both
        // as separate votes would double-count one stated preference during migration.
        if (signals.Any(signal => signal.ConceptId == "work.remote.full"))
        {
            signals.RemoveAll(signal => signal.ConceptId == "work.remote");
        }
        if (signals.Count == 0)
        {
            return null;
        }

        static int Utility(string? preference, int distance) => preference switch
        {
            JobFitPreferenceLevels.Ideal => -4 * distance * distance,
            JobFitPreferenceLevels.Positive => -2 * distance * distance,
            JobFitPreferenceLevels.Negative => distance,
            JobFitPreferenceLevels.HardConflict => 2 * distance,
            _ => 0
        };

        return Enumerable.Range(Minimum, Maximum - Minimum + 1)
            .Select(candidate => new
            {
                Level = candidate,
                Score = signals.Sum(signal => Utility(
                    JobFitPreferenceLevels.Normalize(signal.Preference),
                    Math.Abs(candidate - LegacyConceptLevels[signal.ConceptId])))
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => Math.Abs(candidate.Level - Default))
            .ThenBy(candidate => candidate.Level)
            .First().Level;
    }
}

public sealed record JobFitConfiguration(
    bool Enabled,
    IReadOnlyList<JobFitSignalPreference> Signals,
    int? TravelTolerance = null,
    int? PreferredWorkLocation = null,
    IReadOnlyList<string>? GroupHardConflicts = null)
{
    public static JobFitConfiguration Disabled { get; } = new(false, [], null, null, []);

    public static JobFitConfiguration Normalize(
        JobFitConfiguration? configuration,
        JobConceptCatalog concepts)
    {
        if (configuration is null)
        {
            return Disabled;
        }

        var sourceSignals = configuration.Signals ?? [];
        var travelTolerance = JobSearchManager.TravelTolerance.Normalize(
            configuration.TravelTolerance,
            sourceSignals);
        var preferredWorkLocation = WorkLocationPreference.Normalize(
            configuration.PreferredWorkLocation,
            sourceSignals);
        var signals = sourceSignals
            .Where(signal => signal is not null && concepts.Contains(signal.ConceptId))
            .Where(signal => !JobSearchManager.TravelTolerance.IsLegacyConcept(signal.ConceptId))
            .Where(signal => !WorkLocationPreference.IsLegacyConcept(signal.ConceptId))
            .Select(signal => new
            {
                signal.ConceptId,
                Preference = JobFitPreferenceLevels.Normalize(signal.Preference)
            })
            .Where(signal => signal.Preference is not null)
            .GroupBy(signal => signal.ConceptId, StringComparer.Ordinal)
            .Select(group => new JobFitSignalPreference(group.Key, group.First().Preference!))
            .OrderBy(signal => signal.ConceptId, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
        return new JobFitConfiguration(
            configuration.Enabled,
            signals,
            travelTolerance,
            preferredWorkLocation,
            JobFitGroupHardConflicts.Normalize(configuration.GroupHardConflicts));
    }
}

public sealed record ViewerSettings(
    IReadOnlyList<string> IncludeKeywords,
    IReadOnlyList<string> ExcludeKeywords,
    decimal? MinimumSalary,
    string KeywordScope,
    string LocationMode,
    bool HighlightIncludeKeywords,
    IReadOnlyDictionary<string, bool> CollapsedAgeGroups,
    FacetSelection Country,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FacetSelection? Location,
    bool SearchFiltersCollapsed,
    string ThemeMode,
    UserProfile? UserProfile = null,
    bool HideStrictEducationMismatch = false,
    bool HideStrictClearanceMismatch = false,
    bool IncludeAllLocations = false,
    bool IncludeRemote = true,
    IReadOnlyList<FacetSelection>? SelectedPhysicalLocations = null,
    string CompanyId = CompanyCatalog.DefaultCompanyId,
    IReadOnlyDictionary<string, CompanySourceSettings>? CompanySources = null,
    bool HideStrictWorkAuthorizationMismatch = false,
    bool? HasConfiguredSource = null,
    PendingJobSource? PendingSource = null,
    bool ExcludeStrongExtendedLocationRequirements = false,
    JobFitConfiguration? JobFit = null)
{
    public static ViewerSettings Default { get; } = new(
        [], [], 0m, "metadata", "all", true,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        FacetDefaults.UnitedStatesCountry,
        null,
        false,
        ThemeModes.Default,
        UserProfile.Default,
        false,
        false,
        false,
        false,
        [],
        "",
        new Dictionary<string, CompanySourceSettings>(StringComparer.OrdinalIgnoreCase),
        false,
        false,
        null,
        false,
        JobFitConfiguration.Disabled);
}

public sealed record JobListItem(
    string StableId,
    string Title,
    string RequisitionId,
    DateOnly? StartDate,
    string PostedOn,
    string PrimaryLocation,
    IReadOnlyList<string> AdditionalLocations,
    decimal? PayMinimum,
    decimal? PayMaximum,
    string PayPeriod,
    bool IsRemoteLocationRestricted,
    string ClearanceLevel,
    string ClearanceRequirement,
    bool PolygraphRequired,
    string ClearanceParseStatus,
    IReadOnlyList<JobListCredential>? Credentials,
    IReadOnlyList<JobListUnknownCredential>? UnknownCredentialRequirements,
    JobListAcademicQualification? AcademicQualification,
    JobListWorkAuthorization? WorkAuthorization,
    JobListRemoteWork? RemoteWork,
    JobListExtendedLocationRequirement? ExtendedLocationRequirement,
    IReadOnlyList<DetectedJobConcept> DetectedConcepts,
    bool AnalysisPending)
{
    public static JobListItem FromJob(JobRecord job) => new(
        job.StableId,
        job.Title,
        job.RequisitionId,
        job.StartDate,
        job.PostedOn,
        job.PrimaryLocation,
        job.AdditionalLocations,
        job.PayMinimum,
        job.PayMaximum,
        job.PayPeriod,
        job.IsRemoteLocationRestricted,
        job.ClearanceLevel,
        job.ClearanceRequirement,
        job.PolygraphRequired,
        job.ClearanceParseStatus,
        string.IsNullOrWhiteSpace(job.DescriptionHtml)
            ? null
            : job.Credentials?.Select(JobListCredential.FromAnalysis).ToArray(),
        string.IsNullOrWhiteSpace(job.DescriptionHtml)
            ? null
            : job.UnknownCredentialRequirements?.Select(JobListUnknownCredential.FromAnalysis).ToArray(),
        string.IsNullOrWhiteSpace(job.DescriptionHtml)
            ? null
            : JobListAcademicQualification.FromAnalysis(job.AcademicQualification),
        string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.WorkAuthorization is null
            ? null
            : JobListWorkAuthorization.FromAnalysis(job.WorkAuthorization),
        string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.RemoteWork is null
            ? null
            : JobListRemoteWork.FromAnalysis(job.RemoteWork),
        string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.ExtendedLocationRequirement is null
            ? null
            : JobListExtendedLocationRequirement.FromAnalysis(job.ExtendedLocationRequirement),
        string.IsNullOrWhiteSpace(job.DescriptionHtml)
            ? []
            : job.DetectedConcepts ?? [],
        string.IsNullOrWhiteSpace(job.DescriptionHtml));
}

public sealed record JobListCredential(
    string CredentialId,
    string Name,
    string FullName,
    string Requirement,
    bool IsAlternative,
    bool EquivalentAccepted,
    bool InProgressAccepted,
    bool PostHireAcquisitionAllowed,
    IReadOnlyList<string>? EquivalentCredentialIds,
    string? AlternativeGroup)
{
    public static JobListCredential FromAnalysis(CredentialMatch credential) => new(
        credential.CredentialId,
        credential.Name,
        credential.FullName,
        credential.Requirement,
        credential.IsAlternative,
        credential.EquivalentAccepted,
        credential.InProgressAccepted,
        credential.PostHireAcquisitionAllowed,
        credential.EquivalentCredentialIds,
        credential.AlternativeGroup);
}

public sealed record JobListUnknownCredential(
    string Name,
    string Requirement,
    bool EquivalentAccepted)
{
    public static JobListUnknownCredential FromAnalysis(UnknownCredentialRequirement credential) => new(
        credential.Name,
        credential.Requirement,
        credential.EquivalentAccepted);
}

public sealed record JobListAcademicQualification(
    string MinimumLevel,
    string? SpecificDegree,
    string RequirementType,
    bool ExperienceSubstitutionAccepted,
    string ParseStatus,
    bool HasAccreditation)
{
    public static JobListAcademicQualification? FromAnalysis(
        AcademicQualificationAnalysis? analysis) => analysis is null
            ? null
            : new(
                analysis.MinimumLevel,
                analysis.SpecificDegree,
                analysis.RequirementType,
                analysis.ExperienceSubstitutionAccepted,
                analysis.ParseStatus,
                analysis.Accreditations is { Count: > 0 });
}

public sealed record JobListWorkAuthorization(
    string Eligibility,
    string Sponsorship,
    string Strength,
    string SponsorshipStrength)
{
    public static JobListWorkAuthorization FromAnalysis(WorkAuthorizationAnalysis analysis) => new(
        analysis.Eligibility,
        analysis.Sponsorship,
        analysis.Strength,
        analysis.SponsorshipStrength);
}

public sealed record JobListRemoteWork(string ConcernLevel, string? Summary)
{
    public static JobListRemoteWork FromAnalysis(RemoteWorkAnalysis analysis) =>
        new(analysis.ConcernLevel, analysis.Summary);
}

public sealed record JobListExtendedLocationRequirement(
    string Confidence,
    string? Destination,
    string? Summary)
{
    public static JobListExtendedLocationRequirement FromAnalysis(
        ExtendedLocationRequirementAnalysis analysis) =>
        new(analysis.Confidence, analysis.Destination, analysis.Summary);
}

public sealed record JobsListSnapshot(
    IReadOnlyList<JobListItem> Jobs,
    int TotalJobs,
    DateTimeOffset? LastRefreshedUtc,
    bool IsRefreshing,
    string? Error,
    int DetailFailureCount,
    bool IsCached,
    IReadOnlyList<string> NewJobIds,
    JobSourceQuery Query,
    IReadOnlyDictionary<string, string> JobStates,
    IReadOnlyDictionary<string, JobClosureInfo> JobClosures,
    RefreshProgress? RefreshProgress,
    RefreshMetrics? Metrics)
{
    public static JobsListSnapshot FromSnapshot(JobsSnapshot snapshot) => new(
        snapshot.Jobs.Select(JobListItem.FromJob).ToArray(),
        snapshot.TotalJobs,
        snapshot.LastRefreshedUtc,
        snapshot.IsRefreshing,
        snapshot.Error,
        snapshot.DetailFailureCount,
        snapshot.IsCached,
        snapshot.NewJobIds,
        snapshot.Query,
        snapshot.JobStates,
        snapshot.JobClosures,
        snapshot.RefreshProgress,
        snapshot.Metrics);
}

public sealed record DescriptionMatchRequest(
    IReadOnlyList<string>? IncludeKeywords,
    IReadOnlyList<string>? ExcludeKeywords);

public sealed record UserProfile(
    EducationProfile Education,
    SecurityProfile? Security = null,
    WorkAuthorizationProfile? WorkAuthorization = null,
    CredentialProfile? Credentials = null)
{
    public static UserProfile Default { get; } = new(
        EducationProfile.Default,
        SecurityProfile.Default,
        WorkAuthorizationProfile.Default,
        CredentialProfile.Default);
}

public sealed record CredentialProfile(
    string InventoryStatus,
    IReadOnlyList<string> HeldCredentialIds)
{
    public static CredentialProfile Default { get; } = new("notConfigured", []);
}

public sealed record EducationProfile(string Level, string? DoctorateType)
{
    public static EducationProfile Default { get; } = new("notSpecified", null);
}

public sealed record SecurityProfile(string ClearanceLevel, string PublicTrust)
{
    public static SecurityProfile Default { get; } = new("notSpecified", "unknown");
}

public sealed record WorkAuthorizationProfile(string UsStatus, string Sponsorship)
{
    public static WorkAuthorizationProfile Default { get; } = new("notSpecified", "unknown");
}

public sealed record ViewedJobRequest(string StableId);
public sealed record JobWorkflowStateRequest(
    string StableId,
    string State,
    string? CloseReason = null);

internal sealed record JobsCacheDocument(
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    DateTimeOffset? LastRefreshedUtc,
    int DetailFailureCount,
    IReadOnlyList<JobRecord> Jobs,
    JobSourceQuery? Query = null);

internal sealed record JobsCacheEnvelope(
    int SchemaVersion = 5,
    IReadOnlyDictionary<string, JobsCacheDocument>? Sources = null);

internal sealed record SourceStatusDocument(
    int SchemaVersion,
    JobSourceQuery Query,
    DateTimeOffset LastSuccessfulRefreshUtc,
    int DetailFailureCount,
    bool ListingsTruncated);

internal sealed record JobHistoryDocument(
    int SchemaVersion,
    Dictionary<string, JobHistoryEntry> Jobs)
{
    public static JobHistoryDocument Empty { get; } = new(
        5, new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal));
}

internal sealed record JobHistoryEntry(
    string JobReqId,
    string ExternalPath,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool HasBeenViewed,
    string WorkflowState = JobWorkflowStates.Normal,
    DateTimeOffset? WorkflowStateChangedAt = null,
    // Schema 1-3 migration inputs. Normalized writes clear legacy booleans and their state timestamps.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Dismissed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? DismissedAt = null,
    string CompanyId = CompanyCatalog.DefaultCompanyId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Saved = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? SavedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Applied = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? AppliedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CloseReason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ClosedAt = null);

internal static class JobWorkflowStates
{
    public const string Normal = "normal";
    public const string Saved = "saved";
    public const string Applied = "applied";
    public const string Closed = "closed";
    public const string Hidden = "hidden";

    public static bool IsValid(string? state) => state is Normal or Saved or Applied or Closed or Hidden;

    public static string Normalize(string? state) => IsValid(state) ? state! : Normal;

    public static bool CanTransition(string? currentState, string? nextState)
    {
        var current = Normalize(currentState);
        if (!IsValid(nextState)) return false;
        if (string.Equals(current, nextState, StringComparison.Ordinal)) return true;

        return current switch
        {
            Normal => nextState is Saved or Applied or Hidden,
            Saved => nextState is Normal or Applied or Hidden,
            Applied => nextState is Normal or Closed or Hidden,
            Closed => nextState is Applied,
            Hidden => nextState is Normal,
            _ => false
        };
    }
}

internal static class JobCloseReasons
{
    public const string PositionWithdrawn = "PositionWithdrawn";
    public const string NotSelected = "NotSelected";
    public const string ScreenedOut = "ScreenedOut";
    public const string InterviewedOut = "InterviewedOut";
    public const string Ghosted = "Ghosted";
    public const string Withdrew = "Withdrew";
    public const string Other = "Other";

    public static bool IsValid(string? reason) => reason is
        PositionWithdrawn or NotSelected or ScreenedOut or InterviewedOut or Ghosted or Withdrew or Other;
}

internal sealed class ListingResponse
{
    public int Total { get; init; }
    public List<ListingPosting> JobPostings { get; init; } = [];
    public List<JobSourceFacetNode> Facets { get; init; } = [];
}

internal sealed class JobSourceFacetNode
{
    public string FacetParameter { get; init; } = "";
    public string Descriptor { get; init; } = "";
    public string Id { get; init; } = "";
    public int Count { get; init; }
    public List<JobSourceFacetNode> Values { get; init; } = [];
}

internal sealed class ListingPosting
{
    public string Title { get; init; } = "";
    public string ExternalPath { get; init; } = "";
    public string LocationsText { get; init; } = "";
    public string PostedOn { get; init; } = "";
    public List<string> BulletFields { get; init; } = [];
    public List<string> AdditionalLocations { get; init; } = [];
    public List<string> EquivalentExternalPaths { get; init; } = [];
    public string? IdentityDiscriminator { get; init; }
}

internal sealed class DetailResponse
{
    public DetailPosting? JobPostingInfo { get; init; }
}

internal sealed class DetailPosting
{
    public string Title { get; init; } = "";
    public string JobReqId { get; init; } = "";
    public string Location { get; init; } = "";
    public List<string> AdditionalLocations { get; init; } = [];
    public string? StartDate { get; init; }
    public string PostedOn { get; init; } = "";
    public string TimeType { get; init; } = "";
    public string JobDescription { get; init; } = "";
    public string ExternalUrl { get; init; } = "";
}

internal sealed class SmartRecruitersPostingResponse
{
    public int TotalFound { get; init; }
    public List<SmartRecruitersPosting> Content { get; init; } = [];
}

internal class SmartRecruitersPosting
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string RefNumber { get; init; } = "";
    public string ReleasedDate { get; init; } = "";
    public SmartRecruitersLocation Location { get; init; } = new();
    public SmartRecruitersLabel TypeOfEmployment { get; init; } = new();
}

internal sealed class SmartRecruitersPostingDetail : SmartRecruitersPosting
{
    public string PostingUrl { get; init; } = "";
    public string ApplyUrl { get; init; } = "";
    public SmartRecruitersJobAd JobAd { get; init; } = new();
}

internal sealed class SmartRecruitersLocation
{
    public string City { get; init; } = "";
    public string Region { get; init; } = "";
    public string Country { get; init; } = "";
    public string FullLocation { get; init; } = "";
    public bool Remote { get; init; }
    public bool Hybrid { get; init; }
}

internal sealed class SmartRecruitersLabel
{
    public string Label { get; init; } = "";
}

internal sealed class SmartRecruitersJobAd
{
    public SmartRecruitersSections Sections { get; init; } = new();
}

internal sealed class SmartRecruitersSections
{
    public SmartRecruitersSection JobDescription { get; init; } = new();
    public SmartRecruitersSection Qualifications { get; init; } = new();
    public SmartRecruitersSection AdditionalInformation { get; init; } = new();
}

internal sealed class SmartRecruitersSection
{
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
}
