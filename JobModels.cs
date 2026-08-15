namespace LeidosJobsViewer;

public sealed class WorkdayOptions
{
    public string BaseUrl { get; init; } = "https://leidos.wd5.myworkdayjobs.com";
    public string Tenant { get; init; } = "leidos";
    public string Site { get; init; } = "External";
    public int PageSize { get; init; } = 20;
    public int DetailConcurrency { get; init; } = 6;
    public int RequestTimeoutSeconds { get; init; } = 30;
}

public static class FacetDefaults
{
    public const string CountryId = "bc33aa3152ec42d4995f4791a106ed09";
    public const string CountryLabel = "United States of America";
    public const string LocationId = "da70a15d3ef40104ea4e240d39cef6a2";
    public const string LocationLabel = "6314 Remote/Teleworker US";
    public const string AllCountriesLabel = "All countries";
    public const string AllLocationsLabel = "All locations";
}

public sealed record FacetSelection(string? Id, string Label);

public sealed record WorkdayQuery(
    string? CountryId,
    string CountryLabel,
    string? LocationId,
    string LocationLabel)
{
    public static WorkdayQuery FromSettings(ViewerSettings settings) => new(
        settings.Country.Id,
        settings.Country.Label,
        settings.Location.Id,
        settings.Location.Label);
}

public sealed record FacetOption(string Id, string Label, int Count);

public sealed record LocationFacetOptions(
    int MatchingJobs,
    IReadOnlyList<FacetOption> Countries,
    IReadOnlyList<FacetOption> Locations);

public sealed record JobRecord(
    string Title,
    string RequisitionId,
    DateOnly? StartDate,
    string PostedOn,
    string PrimaryLocation,
    IReadOnlyList<string> AdditionalLocations,
    string TimeType,
    string WorkdayUrl,
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
    AcademicQualificationAnalysis? AcademicQualification = null)
{
    public string StableId => !string.IsNullOrWhiteSpace(RequisitionId)
        ? RequisitionId
        : $"path:{ExternalPath}";
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
    int AnalysisVersion);

public sealed record AcademicQualificationPath(
    string Level,
    string? SpecificDegree,
    string Requirement,
    int? MinimumExperienceYears,
    int? MaximumExperienceYears,
    IReadOnlyList<string> Fields,
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

public sealed record WorkdayFetchResult(
    IReadOnlyList<JobRecord> Jobs,
    int ListingCount,
    int DetailFailureCount);

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
    WorkdayQuery Query,
    IReadOnlyList<string> DismissedJobIds,
    RefreshProgress? RefreshProgress)
{
    public static JobsSnapshot Empty { get; } =
        new([], 0, null, false, null, 0, false, [], new WorkdayQuery(
            FacetDefaults.CountryId,
            FacetDefaults.CountryLabel,
            FacetDefaults.LocationId,
            FacetDefaults.LocationLabel), [], null);
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
    FacetSelection Location,
    bool SearchFiltersCollapsed,
    bool? AutomaticCheckEnabled,
    int AutomaticCheckIntervalMinutes,
    string ThemeMode,
    UserProfile? UserProfile = null,
    bool HideStrictEducationMismatch = false,
    bool HideStrictClearanceMismatch = false)
{
    public static ViewerSettings Default { get; } = new(
        [], [], null, "metadata", "all", true,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        new FacetSelection(FacetDefaults.CountryId, FacetDefaults.CountryLabel),
        new FacetSelection(FacetDefaults.LocationId, FacetDefaults.LocationLabel),
        false,
        true,
        60,
        "light",
        UserProfile.Default,
        false,
        false);
}

public sealed record UserProfile(EducationProfile Education, SecurityProfile? Security = null)
{
    public static UserProfile Default { get; } = new(EducationProfile.Default, SecurityProfile.Default);
}

public sealed record EducationProfile(string Level, string? DoctorateType)
{
    public static EducationProfile Default { get; } = new("notSpecified", null);
}

public sealed record SecurityProfile(string ClearanceLevel, string PublicTrust)
{
    public static SecurityProfile Default { get; } = new("notSpecified", "notSpecified");
}

public sealed record ViewedJobRequest(string StableId);
public sealed record DismissedJobRequest(string StableId, bool Dismissed);

internal sealed record JobsCacheDocument(
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    DateTimeOffset? LastRefreshedUtc,
    int DetailFailureCount,
    IReadOnlyList<JobRecord> Jobs,
    WorkdayQuery? Query = null);

internal sealed record JobHistoryDocument(
    int SchemaVersion,
    Dictionary<string, JobHistoryEntry> Jobs)
{
    public static JobHistoryDocument Empty { get; } = new(
        1, new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal));
}

internal sealed record JobHistoryEntry(
    string JobReqId,
    string ExternalPath,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool HasBeenViewed,
    bool Dismissed = false,
    DateTimeOffset? DismissedAt = null);

internal sealed class ListingResponse
{
    public int Total { get; init; }
    public List<ListingPosting> JobPostings { get; init; } = [];
    public List<WorkdayFacetNode> Facets { get; init; } = [];
}

internal sealed class WorkdayFacetNode
{
    public string FacetParameter { get; init; } = "";
    public string Descriptor { get; init; } = "";
    public string Id { get; init; } = "";
    public int Count { get; init; }
    public List<WorkdayFacetNode> Values { get; init; } = [];
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
