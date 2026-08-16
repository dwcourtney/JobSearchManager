using System.Text.Json.Serialization;

namespace JobSearchManager;

public sealed class JobSourceOptions
{
    public int PageSize { get; init; } = 20;
    public int DetailConcurrency { get; init; } = 6;
    public int RequestTimeoutSeconds { get; init; } = 30;
    public int MaximumListingPages { get; init; } = 200;
    public int MaximumDetailRequestsPerRefresh { get; init; } = 200;
    public int MaximumAutomaticDetailRequests { get; init; } = 50;
    public int MaximumRevalidationsPerRefresh { get; init; } = 25;
    public int DetailReuseHours { get; init; } = 168;
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
    string? CompressedDescriptionHtml = null)
{
    public string StableId => $"{CompanyId}:{(!string.IsNullOrWhiteSpace(RequisitionId)
        ? RequisitionId
        : $"path:{ExternalPath}")}";
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

public sealed record AutomaticCheckResult(
    bool Performed,
    bool SkippedBecauseBusy,
    int ListingCount,
    IReadOnlyList<string> UnknownStableIds,
    bool FullRefreshTriggered);

public sealed record AutomaticCheckStatus(
    bool Enabled,
    int IntervalMinutes,
    bool IsChecking,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? NextCheckUtc,
    DateTimeOffset? LastAutomaticRefreshUtc);

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
    RefreshProgress? RefreshProgress,
    RefreshMetrics? Metrics = null)
{
    public static JobsSnapshot Empty { get; } =
        new([], 0, null, false, null, 0, false, [], new JobSourceQuery(
            null,
            FacetDefaults.AllCountriesLabel,
            true,
            false,
            []), new Dictionary<string, string>(StringComparer.Ordinal), null);
}

public sealed record CompanySourceSettings(
    FacetSelection Country,
    bool IncludeAllLocations,
    bool IncludeRemote,
    IReadOnlyList<FacetSelection> SelectedPhysicalLocations);

public sealed record PendingJobSource(string CompanyId, CompanySourceSettings Source);

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
    bool? AutomaticCheckEnabled,
    int AutomaticCheckIntervalMinutes,
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
    PendingJobSource? PendingSource = null)
{
    public static ViewerSettings Default { get; } = new(
        [], [], 0m, "metadata", "all", true,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        FacetDefaults.UnitedStatesCountry,
        null,
        false,
        true,
        60,
        "light",
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
        null);
}

public sealed record JobListItem(
    string StableId,
    string CompanyId,
    string Title,
    string RequisitionId,
    DateOnly? StartDate,
    string PostedOn,
    string PrimaryLocation,
    IReadOnlyList<string> AdditionalLocations,
    string TimeType,
    string SourceUrl,
    decimal? PayMinimum,
    decimal? PayMaximum,
    string PayPeriod,
    string PayParseStatus,
    bool IsRemoteLocationRestricted,
    string? RemoteLocationRestrictionCategory,
    string? RemoteLocationRestrictionSnippet,
    string? DetailError,
    string ClearanceLevel,
    string ClearanceRequirement,
    bool PolygraphRequired,
    string? ClearanceEvidence,
    string ClearanceParseStatus,
    IReadOnlyList<CredentialMatch>? Credentials,
    IReadOnlyList<string>? UnrecognizedCredentialMentions,
    int CredentialCatalogVersion,
    AcademicQualificationAnalysis? AcademicQualification,
    WorkAuthorizationAnalysis? WorkAuthorization,
    RemoteWorkAnalysis? RemoteWork,
    bool DetailAvailable,
    bool AnalysisPending)
{
    public static JobListItem FromJob(JobRecord job) => new(
        job.StableId,
        job.CompanyId,
        job.Title,
        job.RequisitionId,
        job.StartDate,
        job.PostedOn,
        job.PrimaryLocation,
        job.AdditionalLocations,
        job.TimeType,
        job.SourceUrl,
        job.PayMinimum,
        job.PayMaximum,
        job.PayPeriod,
        job.PayParseStatus,
        job.IsRemoteLocationRestricted,
        job.RemoteLocationRestrictionCategory,
        null,
        job.DetailError,
        job.ClearanceLevel,
        job.ClearanceRequirement,
        job.PolygraphRequired,
        null,
        job.ClearanceParseStatus,
        string.IsNullOrWhiteSpace(job.DescriptionHtml)
            ? null
            : job.Credentials?.Select(credential => credential with { Evidence = "" }).ToArray(),
        [],
        job.CredentialCatalogVersion,
        string.IsNullOrWhiteSpace(job.DescriptionHtml) ? null : Compact(job.AcademicQualification),
        string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.WorkAuthorization is null
            ? null
            : job.WorkAuthorization with { Evidence = [] },
        string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.RemoteWork is null
            ? null
            : job.RemoteWork with { Signals = [] },
        !string.IsNullOrWhiteSpace(job.DescriptionHtml),
        string.IsNullOrWhiteSpace(job.DescriptionHtml));

    private static AcademicQualificationAnalysis? Compact(
        AcademicQualificationAnalysis? analysis) => analysis is null
            ? null
            : analysis with
            {
                Evidence = [],
                Paths = analysis.Paths.Select(path => path with { Evidence = "" }).ToArray(),
                Accreditations = analysis.Accreditations?
                    .Select(accreditation => accreditation with { Evidence = "" }).ToArray()
            };
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
        snapshot.RefreshProgress,
        snapshot.Metrics);
}

public sealed record DescriptionMatchRequest(
    IReadOnlyList<string>? IncludeKeywords,
    IReadOnlyList<string>? ExcludeKeywords);

public sealed record UserProfile(
    EducationProfile Education,
    SecurityProfile? Security = null,
    WorkAuthorizationProfile? WorkAuthorization = null)
{
    public static UserProfile Default { get; } = new(
        EducationProfile.Default,
        SecurityProfile.Default,
        WorkAuthorizationProfile.Default);
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
public sealed record JobWorkflowStateRequest(string StableId, string State);

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

internal sealed record JobHistoryDocument(
    int SchemaVersion,
    Dictionary<string, JobHistoryEntry> Jobs)
{
    public static JobHistoryDocument Empty { get; } = new(
        4, new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal));
}

internal sealed record JobHistoryEntry(
    string JobReqId,
    string ExternalPath,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool HasBeenViewed,
    string WorkflowState = JobWorkflowStates.Normal,
    DateTimeOffset? WorkflowStateChangedAt = null,
    // Schema 1-3 migration inputs. Normalized schema-4 writes always clear these.
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
    DateTimeOffset? AppliedAt = null);

internal static class JobWorkflowStates
{
    public const string Normal = "normal";
    public const string Saved = "saved";
    public const string Applied = "applied";
    public const string Hidden = "hidden";

    public static bool IsValid(string? state) => state is Normal or Saved or Applied or Hidden;

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
            Applied => nextState is Normal or Hidden,
            Hidden => nextState is Normal,
            _ => false
        };
    }
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
