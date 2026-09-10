using JobSearchManager;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FactualCompletionInspection
{
    internal static object Run(string html, string? provider, IReadOnlyDictionary<string,string>? metadata, string? inputTitle = null, string? inputLocation = null, string[]? inputAdditional = null)
    {
        var title = inputTitle ?? metadata?.GetValueOrDefault("title") ?? "";
        var location = inputLocation ?? metadata?.GetValueOrDefault("primaryLocation") ?? "";
        var additional = inputAdditional ?? [];
        var declarations = new Dictionary<string,string>(metadata ?? new Dictionary<string,string>(), StringComparer.Ordinal);
        if (inputTitle is not null) declarations["title"] = inputTitle;
        if (inputLocation is not null) declarations["primaryLocation"] = inputLocation;
        for (var i = 0; i < additional.Length; i++) declarations[$"additionalLocation[{i}]"] = additional[i];
        var observed = FactualDomainObservations.Default.Observe(html, provider, declarations);
        var auth = new WorkAuthorizationDetector();
        var authorization = auth.Extract(html, provider);
        var pay = JobAnalysis.ExtractSalary(html, SalaryRules.Default);
        var academic = new AcademicQualificationDetector();
        var education = academic.Extract(html);
        var credentialDetector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
        var credentials = credentialDetector.Extract(html);
        var remoteDetector = new RemoteWorkDetector();
        var extendedDetector = new ExtendedLocationRequirementDetector();
        var remote = remoteDetector.Extract(title, location, additional, html);
        var extended = extendedDetector.Extract(title, location, additional, html);
        var text = JobAnalysis.HtmlToPlainText(html);
        var clearanceRules = ClearanceRules.Default;
        var clearanceText = clearanceRules.Regex(clearanceRules.Definition.NegationPatternId).Replace(text, " ");
        return new Dictionary<string,object> {
            ["work-authorization"] = new { observations = auth.Observe(html, provider), legacyCandidates = authorization,
                legacySummary = WorkAuthorizationDetector.Summarize(authorization) },
            ["compensation"] = new { observations = CompensationObservations.Observe(pay, provider), legacyCandidates = pay.Observations,
                legacySummary = JobAnalysis.SummarizeSalary(pay) },
            ["clearance"] = new { observations = observed["clearance"],
                rawLevelCandidates = JobAnalysis.ExtractClearanceLevelCandidates(text, clearanceRules).ToArray(),
                legacyCandidates = JobAnalysis.ExtractClearanceLevelCandidates(clearanceText, clearanceRules).ToArray(),
                legacySummary = JobAnalysis.AnalyzeClearance(html) },
            ["education"] = new { observations = observed["education"], legacyCandidates = education,
                legacySummary = academic.Summarize(education) },
            ["credentials"] = new { observations = observed["credentials"], legacyCandidates = new {
                assertions = credentials.Candidates.Select(c => new { id = c.Credential.Definition.Id, c.Requirement,
                    c.IsAlternative, c.EquivalentAccepted, c.InProgressAccepted, c.PostHireAcquisitionAllowed,
                    c.Evidence, c.SegmentIndex, c.MatchIndex, c.AlternativeGroup }),
                credentials.Unrecognized, credentials.Unknown }, legacySummary = credentialDetector.Summarize(credentials) },
            ["remote-work"] = new { observations = observed["remote-work"], legacyCandidates = remote,
                legacySummary = remoteDetector.Summarize(remote) },
            ["extended-location"] = new { observations = observed["extended-location"], legacyCandidates = extended,
                legacySummary = extendedDetector.Summarize(extended) },
            ["geographic-restriction"] = new { observations = observed["geographic-restriction"],
                legacyCandidates = JobAnalysis.ExtractGeographicCandidates(text, GeographicRestrictionRules.Default).ToArray(),
                legacySummary = JobAnalysis.AnalyzeRemoteLocation(html, location, additional) }
        };
    }
}
