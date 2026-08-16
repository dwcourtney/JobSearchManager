using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed record PortableWorkspaceDocument(
    string Format,
    int Version,
    PortablePreferenceValues Preferences,
    IReadOnlyList<PortableCuratedJob> CuratedJobs);

public sealed record PortablePreferenceValues(
    PortableJobSource? JobSource,
    PortableSearchPreferences Search,
    PortableQualifications Qualifications,
    PortableApplicationPreferences Application);

public sealed record PortableJobSource(
    string CompanyId,
    FacetSelection Country,
    bool IncludeAllLocations,
    bool IncludeRemote,
    IReadOnlyList<FacetSelection> PhysicalLocations);

public sealed record PortableSearchPreferences(
    IReadOnlyList<string> IncludeKeywords,
    IReadOnlyList<string> ExcludeKeywords,
    string KeywordScope,
    string LocationMode,
    bool HighlightIncludeKeywords);

public sealed record PortableQualifications(
    decimal MinimumSalary,
    UserProfile UserProfile,
    bool HideStrictEducationMismatch,
    bool HideStrictClearanceMismatch,
    bool HideStrictWorkAuthorizationMismatch);

public sealed record PortableApplicationPreferences(
    bool AutomaticCheckEnabled,
    int AutomaticCheckIntervalMinutes,
    string ThemeMode);

public sealed record PortableCuratedJob(
    string CompanyId,
    string StableId,
    string RequisitionId,
    string WorkflowState,
    string ExternalPath);

internal sealed record PortableWorkspaceImport(
    ViewerSettings Settings,
    JobHistoryDocument History);

internal sealed class WorkspaceImportException(string message) : Exception(message);

internal sealed partial class PortableWorkspaceService
{
    public const string FormatIdentifier = "JobSearchManagerBackup";
    internal const string LegacyFormatIdentifier = "WorkdayJobManagerWorkspace";
    public const int CurrentVersion = 1;
    public const int MaximumImportBytes = 1_000_000;
    private readonly CompanyCatalog _companies;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public PortableWorkspaceService(CompanyCatalog companies) => _companies = companies;

    public PortableWorkspaceDocument Export(ViewerSettings settings, JobHistoryDocument history)
    {
        PortableJobSource? source = null;
        if (settings.PendingSource is { } pending)
        {
            source = ToPortableSource(pending.CompanyId, pending.Source);
        }
        else if (settings.HasConfiguredSource == true)
        {
            source = new PortableJobSource(
                settings.CompanyId,
                settings.Country,
                settings.IncludeAllLocations,
                settings.IncludeRemote,
                settings.SelectedPhysicalLocations ?? []);
        }

        var curatedJobs = history.Jobs
            .Where(pair => JobWorkflowStates.Normalize(pair.Value.WorkflowState) is
                JobWorkflowStates.Saved or JobWorkflowStates.Applied or JobWorkflowStates.Hidden)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PortableCuratedJob(
                pair.Value.CompanyId,
                pair.Key,
                pair.Value.JobReqId,
                JobWorkflowStates.Normalize(pair.Value.WorkflowState),
                pair.Value.ExternalPath))
            .ToArray();

        return new PortableWorkspaceDocument(
            FormatIdentifier,
            CurrentVersion,
            new PortablePreferenceValues(
                source,
                new PortableSearchPreferences(
                    settings.IncludeKeywords,
                    settings.ExcludeKeywords,
                    settings.KeywordScope,
                    settings.LocationMode,
                    settings.HighlightIncludeKeywords),
                new PortableQualifications(
                    settings.MinimumSalary ?? 0m,
                    settings.UserProfile ?? UserProfile.Default,
                    settings.HideStrictEducationMismatch,
                    settings.HideStrictClearanceMismatch,
                    settings.HideStrictWorkAuthorizationMismatch),
                new PortableApplicationPreferences(
                    settings.AutomaticCheckEnabled ?? true,
                    settings.AutomaticCheckIntervalMinutes,
                    settings.ThemeMode)),
            curatedJobs);
    }

    public PortableWorkspaceImport ImportJson(
        string json,
        ViewerSettings currentSettings,
        JobHistoryDocument currentHistory)
    {
        PortableWorkspaceDocument document;
        try
        {
            document = JsonSerializer.Deserialize<PortableWorkspaceDocument>(json, _json)
                ?? throw new WorkspaceImportException("The workspace file is empty.");
        }
        catch (JsonException)
        {
            throw new WorkspaceImportException("The selected file is not valid JSON.");
        }

        return Import(document, currentSettings, currentHistory);
    }

    public PortableWorkspaceImport Import(
        PortableWorkspaceDocument document,
        ViewerSettings currentSettings,
        JobHistoryDocument currentHistory)
    {
        if (!string.Equals(document.Format, FormatIdentifier, StringComparison.Ordinal) &&
            !string.Equals(document.Format, LegacyFormatIdentifier, StringComparison.Ordinal))
        {
            throw new WorkspaceImportException(
                "The selected file is not a Job Search Manager workspace export.");
        }
        if (document.Version != CurrentVersion)
        {
            throw new WorkspaceImportException($"Workspace version {document.Version} is not supported.");
        }
        if (document.Preferences is null || document.Preferences.Search is null ||
            document.Preferences.Qualifications is null || document.Preferences.Application is null ||
            document.CuratedJobs is null)
        {
            throw new WorkspaceImportException("The workspace file is missing required sections.");
        }

        var search = document.Preferences.Search;
        ValidateTerms(search.IncludeKeywords, "include keywords");
        ValidateTerms(search.ExcludeKeywords, "exclude keywords");
        if (search.KeywordScope is not ("metadata" or "description") ||
            search.LocationMode is not ("all" or "hide-restricted" or "only-restricted"))
        {
            throw new WorkspaceImportException("The workspace file contains an invalid search option.");
        }

        var qualifications = document.Preferences.Qualifications;
        if (qualifications.MinimumSalary is < 0m or > 10_000_000m)
        {
            throw new WorkspaceImportException("Minimum salary must be between 0 and 10,000,000.");
        }
        ValidateProfile(qualifications.UserProfile);

        var application = document.Preferences.Application;
        if (application.AutomaticCheckIntervalMinutes is not (30 or 60 or 120 or 240 or 480))
        {
            throw new WorkspaceImportException("The automatic-check frequency is not supported.");
        }
        if (application.ThemeMode is not ("light" or "dark"))
        {
            throw new WorkspaceImportException("The theme value is not supported.");
        }

        PendingJobSource? pendingSource = null;
        if (document.Preferences.JobSource is { } importedSource)
        {
            if (!_companies.TryGet(importedSource.CompanyId, out var company))
            {
                throw new WorkspaceImportException(
                    $"The workspace file references unsupported company '{importedSource.CompanyId}'.");
            }
            ValidateFacet(importedSource.Country, "country", allowEmptyId: true);
            if (importedSource.IncludeAllLocations && importedSource.PhysicalLocations.Count > 0)
            {
                throw new WorkspaceImportException("Country-wide coverage cannot include physical locations.");
            }
            foreach (var location in importedSource.PhysicalLocations)
            {
                ValidateFacet(location, "physical location", allowEmptyId: false);
                if (company.IsRemoteLocation(location.Id))
                {
                    throw new WorkspaceImportException(
                        "Remote facets cannot be imported as physical locations.");
                }
            }
            if (importedSource.PhysicalLocations.Count > 500)
            {
                throw new WorkspaceImportException(
                    "The workspace file contains too many physical locations.");
            }
            var source = new CompanySourceSettings(
                importedSource.Country,
                importedSource.IncludeAllLocations,
                importedSource.IncludeRemote,
                importedSource.PhysicalLocations);
            if (!MatchesAppliedSource(currentSettings, company, source))
            {
                pendingSource = new PendingJobSource(company.Id, source);
            }
        }

        if (document.CuratedJobs.Count > 10_000)
        {
            throw new WorkspaceImportException("The workspace file contains too many curated jobs.");
        }

        var importedJobs = new Dictionary<string, PortableCuratedJob>(StringComparer.Ordinal);
        foreach (var job in document.CuratedJobs)
        {
            ValidateCuratedJob(job);
            if (!importedJobs.TryAdd(job.StableId, job))
            {
                throw new WorkspaceImportException(
                    $"The workspace file contains duplicate job identity '{job.StableId}'.");
            }
        }

        var importedSettings = currentSettings with
        {
            IncludeKeywords = search.IncludeKeywords.ToArray(),
            ExcludeKeywords = search.ExcludeKeywords.ToArray(),
            MinimumSalary = qualifications.MinimumSalary,
            KeywordScope = search.KeywordScope,
            LocationMode = search.LocationMode,
            HighlightIncludeKeywords = search.HighlightIncludeKeywords,
            AutomaticCheckEnabled = application.AutomaticCheckEnabled,
            AutomaticCheckIntervalMinutes = application.AutomaticCheckIntervalMinutes,
            ThemeMode = application.ThemeMode,
            UserProfile = qualifications.UserProfile,
            HideStrictEducationMismatch = qualifications.HideStrictEducationMismatch,
            HideStrictClearanceMismatch = qualifications.HideStrictClearanceMismatch,
            HideStrictWorkAuthorizationMismatch = qualifications.HideStrictWorkAuthorizationMismatch,
            PendingSource = pendingSource
        };

        var now = DateTimeOffset.UtcNow;
        var mergedHistory = currentHistory.Jobs.ToDictionary(
            pair => pair.Key,
            pair => JobWorkflowStates.Normalize(pair.Value.WorkflowState) == JobWorkflowStates.Normal
                ? pair.Value
                : pair.Value with
                {
                    WorkflowState = JobWorkflowStates.Normal,
                    WorkflowStateChangedAt = now
                },
            StringComparer.Ordinal);
        foreach (var job in importedJobs.Values)
        {
            if (mergedHistory.TryGetValue(job.StableId, out var existing))
            {
                mergedHistory[job.StableId] = existing with
                {
                    JobReqId = job.RequisitionId,
                    ExternalPath = job.ExternalPath,
                    CompanyId = job.CompanyId,
                    HasBeenViewed = true,
                    WorkflowState = job.WorkflowState,
                    WorkflowStateChangedAt = now
                };
            }
            else
            {
                mergedHistory[job.StableId] = new JobHistoryEntry(
                    job.RequisitionId,
                    job.ExternalPath,
                    now,
                    now,
                    true,
                    job.WorkflowState,
                    now,
                    CompanyId: job.CompanyId);
            }
        }

        return new PortableWorkspaceImport(
            importedSettings,
            new JobHistoryDocument(4, mergedHistory));
    }

    private void ValidateCuratedJob(PortableCuratedJob? job)
    {
        if (job is null || string.IsNullOrWhiteSpace(job.CompanyId) ||
            !CompanyIdPattern().IsMatch(job.CompanyId))
        {
            throw new WorkspaceImportException(
                "The workspace file contains a curated job with an invalid company identity.");
        }
        if (job.WorkflowState is not
            (JobWorkflowStates.Saved or JobWorkflowStates.Applied or JobWorkflowStates.Hidden))
        {
            throw new WorkspaceImportException("A curated job contains an invalid workflow state.");
        }
        if (string.IsNullOrWhiteSpace(job.StableId) || job.StableId.Length > 600 ||
            !job.StableId.StartsWith(job.CompanyId + ":", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(job.ExternalPath) || job.ExternalPath.Length > 1000 ||
            !job.ExternalPath.StartsWith("/", StringComparison.Ordinal) ||
            job.RequisitionId.Length > 300)
        {
            throw new WorkspaceImportException("A curated job contains an invalid company-scoped identity.");
        }
        var expected = string.IsNullOrWhiteSpace(job.RequisitionId)
            ? $"{job.CompanyId}:path:{job.ExternalPath}"
            : $"{job.CompanyId}:{job.RequisitionId}";
        if (!string.Equals(job.StableId, expected, StringComparison.Ordinal))
        {
            throw new WorkspaceImportException(
                "A curated job's stable identity does not match its company and requisition ID.");
        }
    }

    private static PortableJobSource ToPortableSource(string companyId, CompanySourceSettings source) =>
        new(companyId, source.Country, source.IncludeAllLocations, source.IncludeRemote,
            source.SelectedPhysicalLocations);

    private bool MatchesAppliedSource(
        ViewerSettings current,
        CompanyDefinition company,
        CompanySourceSettings imported)
    {
        if (current.HasConfiguredSource != true ||
            !string.Equals(current.CompanyId, company.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var importedQuery = new JobSourceQuery(
            imported.Country.Id,
            imported.Country.Label,
            imported.IncludeAllLocations,
            imported.IncludeRemote,
            imported.SelectedPhysicalLocations,
            CompanyId: company.Id);
        return JobSourceQuery.FromSettings(current, _companies)
            .IsEquivalentTo(importedQuery, _companies);
    }

    private static void ValidateTerms(IReadOnlyList<string>? terms, string name)
    {
        if (terms is null || terms.Count > 100 || terms.Any(term =>
            string.IsNullOrWhiteSpace(term) || term.Length > 200))
        {
            throw new WorkspaceImportException($"The {name} section is invalid.");
        }
    }

    private static void ValidateProfile(UserProfile? profile)
    {
        if (profile?.Education?.Level is not
                ("notSpecified" or "noCredential" or "ged" or "highSchool" or "associate" or
                 "bachelor" or "master" or "doctorate") ||
            (profile.Education.Level != "doctorate" && profile.Education.DoctorateType is not null) ||
            (profile.Education.Level == "doctorate" && profile.Education.DoctorateType is not (null or "phD")) ||
            profile.Security?.ClearanceLevel is not
                ("notSpecified" or "none" or "secret" or "topSecret" or "topSecretSCI" or "otherUnknown") ||
            profile.Security.PublicTrust is not ("none" or "current" or "unknown") ||
            profile.WorkAuthorization?.UsStatus is not
                ("notSpecified" or "usCitizen" or "permanentResident" or "otherAuthorized" or "notAuthorized") ||
            profile.WorkAuthorization.Sponsorship is not ("unknown" or "notRequired" or "required"))
        {
            throw new WorkspaceImportException("The qualification profile contains an invalid value.");
        }
    }

    private static void ValidateFacet(FacetSelection? facet, string name, bool allowEmptyId)
    {
        if (facet is null || string.IsNullOrWhiteSpace(facet.Label) || facet.Label.Length > 300 ||
            (!allowEmptyId && string.IsNullOrWhiteSpace(facet.Id)) ||
            (!string.IsNullOrWhiteSpace(facet.Id) && !ExternalFacetIdPattern().IsMatch(facet.Id)))
        {
            throw new WorkspaceImportException($"The imported {name} is invalid.");
        }
    }

    [GeneratedRegex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalFacetIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex CompanyIdPattern();
}
