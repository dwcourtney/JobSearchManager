using System.Text.Json.Serialization;

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
    public const string RemoteLocationId = "da70a15d3ef40104ea4e240d39cef6a2";
    public const string RemoteLocationLabel = "6314 Remote/Teleworker US";
    public const string AdditionalRemoteLocationId = "4d3806f19fb4100117214c80588f0000";
    public const string AdditionalRemoteLocationLabel = "Remote, US";
    public const string AllCountriesLabel = "All countries";
    public const string AllLocationsLabel = "All locations";

    public static IReadOnlyList<string> RemoteLocationIds { get; } =
        [RemoteLocationId, AdditionalRemoteLocationId];

    public static bool IsUnitedStates(string? countryId) =>
        string.Equals(countryId, CountryId, StringComparison.Ordinal);

    public static bool IsRemoteLocation(string? locationId) =>
        RemoteLocationIds.Contains(locationId, StringComparer.Ordinal);
}

public sealed record FacetSelection(string? Id, string Label);

public sealed record WorkdayQuery(
    string? CountryId,
    string CountryLabel,
    bool IncludeAllLocations = false,
    bool IncludeRemote = true,
    IReadOnlyList<FacetSelection>? PhysicalLocations = null,
    int SourceModelVersion = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocationId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocationLabel = null)
{
    public static WorkdayQuery FromSettings(ViewerSettings settings) => new WorkdayQuery(
        settings.Country.Id,
        settings.Country.Label,
        settings.IncludeAllLocations,
        settings.IncludeRemote,
        settings.SelectedPhysicalLocations,
        1).Normalize();

    public WorkdayQuery Normalize()
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
            else if (FacetDefaults.IsRemoteLocation(LocationId))
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

        includeRemote = FacetDefaults.IsUnitedStates(countryId) && (includeAll || includeRemote);
        var normalizedPhysical = includeAll
            ? []
            : physical
                .Where(location => !string.IsNullOrWhiteSpace(location?.Id) &&
                    !FacetDefaults.IsRemoteLocation(location.Id))
                .Select(location => new FacetSelection(
                    location.Id!.Trim(),
                    string.IsNullOrWhiteSpace(location.Label) ? location.Id.Trim() : location.Label.Trim()))
                .GroupBy(location => location.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(location => location.Id, StringComparer.Ordinal)
                .ToArray();

        return new WorkdayQuery(
            countryId,
            countryLabel,
            includeAll,
            includeRemote,
            normalizedPhysical,
            1);
    }

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveLocationIds
    {
        get
        {
            var query = Normalize();
            if (query.IncludeAllLocations)
            {
                return [];
            }

            return (query.PhysicalLocations ?? [])
                .Select(location => location.Id!)
                .Concat(query.IncludeRemote ? FacetDefaults.RemoteLocationIds : [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public bool IsEquivalentTo(WorkdayQuery? other)
    {
        if (other is null)
        {
            return false;
        }

        var left = Normalize();
        var right = other.Normalize();
        return string.Equals(left.CountryId, right.CountryId, StringComparison.Ordinal) &&
            left.IncludeAllLocations == right.IncludeAllLocations &&
            left.IncludeRemote == right.IncludeRemote &&
            left.EffectiveLocationIds.SequenceEqual(right.EffectiveLocationIds, StringComparer.Ordinal);
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
            false,
            true,
            []), [], null);
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
    bool? AutomaticCheckEnabled,
    int AutomaticCheckIntervalMinutes,
    string ThemeMode,
    UserProfile? UserProfile = null,
    bool HideStrictEducationMismatch = false,
    bool HideStrictClearanceMismatch = false,
    bool IncludeAllLocations = false,
    bool IncludeRemote = true,
    IReadOnlyList<FacetSelection>? SelectedPhysicalLocations = null)
{
    public static ViewerSettings Default { get; } = new(
        [], [], null, "metadata", "all", true,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        new FacetSelection(FacetDefaults.CountryId, FacetDefaults.CountryLabel),
        null,
        false,
        true,
        60,
        "light",
        UserProfile.Default,
        false,
        false,
        false,
        true,
        []);
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
    public static SecurityProfile Default { get; } = new("notSpecified", "unknown");
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
