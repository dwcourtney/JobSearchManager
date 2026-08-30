using System.Text.Json;
using System.Text.Json.Serialization;
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
    PortableApplicationPreferences Application,
    PortableCompensationPreferences? Compensation = null,
    JobFitConfiguration? JobFit = null);

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MinimumSalary,
    UserProfile UserProfile,
    bool HideStrictEducationMismatch,
    bool HideStrictClearanceMismatch,
    bool HideStrictWorkAuthorizationMismatch);

public sealed record PortableCompensationPreferences(decimal MinimumSalary);

public sealed record PortableApplicationPreferences(
    string? ThemeMode,
    bool ExcludeStrongExtendedLocationRequirements = false);

public sealed record PortableCuratedJob(
    string CompanyId,
    string StableId,
    string RequisitionId,
    string WorkflowState,
    string ExternalPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CloseReason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ClosedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? AppliedAt = null);

internal sealed record PortableWorkspaceImport(
    ViewerSettings Settings,
    JobHistoryDocument History);

internal sealed class WorkspaceImportException(string message) : Exception(message);

internal sealed partial class PortableWorkspaceService
{
    public const string FormatIdentifier = "JobSearchManagerBackup";
    internal const string LegacyFormatIdentifier = "WorkdayJobManagerWorkspace";
    public const int CurrentVersion = 4;
    public const int MaximumImportBytes = 1_000_000;
    private readonly CompanyCatalog _companies;
    private readonly JobConceptCatalog _jobConcepts;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public PortableWorkspaceService(
        CompanyCatalog companies,
        JobConceptCatalog? jobConcepts = null)
    {
        _companies = companies;
        _jobConcepts = jobConcepts ?? JobConceptCatalog.LoadDefault();
    }

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
                JobWorkflowStates.Saved or JobWorkflowStates.Applied or
                JobWorkflowStates.Closed or JobWorkflowStates.Hidden)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PortableCuratedJob(
                pair.Value.CompanyId,
                pair.Key,
                pair.Value.JobReqId,
                JobWorkflowStates.Normalize(pair.Value.WorkflowState),
                pair.Value.ExternalPath,
                pair.Value.CloseReason,
                pair.Value.ClosedAt,
                pair.Value.AppliedAt))
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
                    null,
                    settings.UserProfile ?? UserProfile.Default,
                    settings.HideStrictEducationMismatch,
                    settings.HideStrictClearanceMismatch,
                    settings.HideStrictWorkAuthorizationMismatch),
                new PortableApplicationPreferences(
                    settings.ThemeMode,
                    settings.ExcludeStrongExtendedLocationRequirements),
                new PortableCompensationPreferences(settings.MinimumSalary ?? 0m),
                JobFitConfiguration.Normalize(settings.JobFit, _jobConcepts)),
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
        if (document.Version is not (1 or 2 or 3 or CurrentVersion))
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
        var minimumSalary = document.Preferences.Compensation?.MinimumSalary ??
            qualifications.MinimumSalary ?? 0m;
        if (minimumSalary is < 0m or > 10_000_000m)
        {
            throw new WorkspaceImportException("Minimum salary must be between 0 and 10,000,000.");
        }
        ValidateProfile(qualifications.UserProfile);

        var application = document.Preferences.Application;
        if (!string.IsNullOrWhiteSpace(application.ThemeMode) &&
            !ThemeModes.IsSupported(application.ThemeMode))
        {
            throw new WorkspaceImportException("The theme value is not supported.");
        }
        var importedThemeMode = ThemeModes.Normalize(application.ThemeMode);
        var importedJobFit = document.Preferences.JobFit ?? JobFitConfiguration.Disabled;
        ValidateJobFit(importedJobFit);
        importedJobFit = JobFitConfiguration.Normalize(importedJobFit, _jobConcepts);

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
            MinimumSalary = minimumSalary,
            KeywordScope = search.KeywordScope,
            LocationMode = search.LocationMode,
            HighlightIncludeKeywords = search.HighlightIncludeKeywords,
            ThemeMode = importedThemeMode,
            UserProfile = qualifications.UserProfile,
            HideStrictEducationMismatch = qualifications.HideStrictEducationMismatch,
            HideStrictClearanceMismatch = qualifications.HideStrictClearanceMismatch,
            HideStrictWorkAuthorizationMismatch = qualifications.HideStrictWorkAuthorizationMismatch,
            ExcludeStrongExtendedLocationRequirements =
                application.ExcludeStrongExtendedLocationRequirements,
            JobFit = importedJobFit,
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
                    WorkflowStateChangedAt = now,
                    AppliedAt = null,
                    CloseReason = null,
                    ClosedAt = null
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
                    WorkflowStateChangedAt = job.WorkflowState == JobWorkflowStates.Closed
                        ? job.ClosedAt
                        : now,
                    AppliedAt = job.AppliedAt ??
                        (job.WorkflowState is JobWorkflowStates.Applied or JobWorkflowStates.Closed
                            ? now
                            : null),
                    CloseReason = job.WorkflowState == JobWorkflowStates.Closed
                        ? job.CloseReason
                        : null,
                    ClosedAt = job.WorkflowState == JobWorkflowStates.Closed
                        ? job.ClosedAt
                        : null
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
                    job.WorkflowState == JobWorkflowStates.Closed ? job.ClosedAt : now,
                    CompanyId: job.CompanyId,
                    AppliedAt: job.AppliedAt ??
                        (job.WorkflowState is JobWorkflowStates.Applied or JobWorkflowStates.Closed
                            ? now
                            : null),
                    CloseReason: job.WorkflowState == JobWorkflowStates.Closed
                        ? job.CloseReason
                        : null,
                    ClosedAt: job.WorkflowState == JobWorkflowStates.Closed
                        ? job.ClosedAt
                        : null);
            }
        }

        return new PortableWorkspaceImport(
            importedSettings,
            new JobHistoryDocument(5, mergedHistory));
    }

    private void ValidateJobFit(JobFitConfiguration configuration)
    {
        if (configuration.Signals is null || configuration.Signals.Count > 100)
        {
            throw new WorkspaceImportException(
                "The workspace file contains an invalid number of Job Fit signals.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signal in configuration.Signals)
        {
            if (signal is null || !_jobConcepts.Contains(signal.ConceptId))
            {
                throw new WorkspaceImportException(
                    "The workspace file contains an unknown canonical Job Fit concept.");
            }
            if (!JobFitPreferenceLevels.IsSupported(signal.Preference))
            {
                throw new WorkspaceImportException(
                    $"The Job Fit preference for '{signal.ConceptId}' is not supported.");
            }
            if (!ids.Add(signal.ConceptId))
            {
                throw new WorkspaceImportException(
                    $"The workspace file contains duplicate Job Fit concept '{signal.ConceptId}'.");
            }
        }
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
            (JobWorkflowStates.Saved or JobWorkflowStates.Applied or
             JobWorkflowStates.Closed or JobWorkflowStates.Hidden))
        {
            throw new WorkspaceImportException("A curated job contains an invalid workflow state.");
        }
        var hasClosureMetadata = job.CloseReason is not null || job.ClosedAt is not null;
        if (job.WorkflowState == JobWorkflowStates.Closed)
        {
            if (!JobCloseReasons.IsValid(job.CloseReason) || job.ClosedAt is null)
            {
                throw new WorkspaceImportException(
                    "A closed job is missing a valid close reason or closed timestamp.");
            }
        }
        else if (hasClosureMetadata)
        {
            throw new WorkspaceImportException(
                "A non-closed job contains unexpected close metadata.");
        }
        var providerPathIsValid = _companies.TryGet(job.CompanyId, out var company) &&
            company.IsSmartRecruiters
                ? job.ExternalPath.All(char.IsDigit)
                : job.ExternalPath.StartsWith("/", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(job.StableId) || job.StableId.Length > 600 ||
            !job.StableId.StartsWith(job.CompanyId + ":", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(job.ExternalPath) || job.ExternalPath.Length > 1000 ||
            !providerPathIsValid ||
            job.RequisitionId.Length > 300)
        {
            throw new WorkspaceImportException("A curated job contains an invalid company-scoped identity.");
        }
        var expected = string.IsNullOrWhiteSpace(job.RequisitionId)
            ? $"{job.CompanyId}:path:{job.ExternalPath}"
            : $"{job.CompanyId}:{job.RequisitionId}";
        var deterministicVariant = $"{expected}:variant:path:{job.ExternalPath.Trim('/')}";
        if (!string.Equals(job.StableId, expected, StringComparison.Ordinal) &&
            !string.Equals(job.StableId, deterministicVariant, StringComparison.Ordinal))
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
            profile.WorkAuthorization.Sponsorship is not ("unknown" or "notRequired" or "required") ||
            (profile.Credentials is not null &&
                (profile.Credentials.InventoryStatus is not ("notConfigured" or "none" or "complete") ||
                 profile.Credentials.HeldCredentialIds is null ||
                 profile.Credentials.HeldCredentialIds.Count > 200 ||
                 (profile.Credentials.InventoryStatus != "complete" &&
                    profile.Credentials.HeldCredentialIds.Count > 0) ||
                 profile.Credentials.HeldCredentialIds.Any(id =>
                    string.IsNullOrWhiteSpace(id) || id.Length > 100 ||
                    id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))))
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
