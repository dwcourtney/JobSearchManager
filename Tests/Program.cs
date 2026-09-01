using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.IO.Compression;
using System.Net.Mail;
using System.Security.Claims;
using JobSearchManager;

if (args.Length is 2 or 3 && args[0] == "--detector-evaluation")
{
    var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var detector = new JobConceptDetector(catalog);
    var evaluation = new DetectorEvaluationService(
        new TestHostEnvironment(AppContext.BaseDirectory), catalog, detector);
    var report = evaluation.Evaluate(args.Length == 3 ? args[2] : null);
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    Console.WriteLine($"DETECTOR EVALUATION fixtures={report.FixtureCount} labels={report.LabelCount} concepts={report.Concepts.Count}");
    return;
}

if (args.Length == 3 && args[0] == "--job-fit-calibration-detect")
{
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
    var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var detector = new JobConceptDetector(catalog);
    var remoteDetector = new RemoteWorkDetector();
    var extendedDetector = new ExtendedLocationRequirementDetector();
    var results = document.RootElement.GetProperty("jobs").EnumerateArray().Select(job =>
    {
        var title = job.TryGetProperty("title", out var value) ? value.GetString() ?? "" : "";
        var requisition = job.TryGetProperty("requisitionId", out value) ? value.GetString() ?? "" : "";
        var primaryLocation = job.TryGetProperty("primaryLocation", out value) ? value.GetString() ?? "" : "";
        var additionalLocations = job.TryGetProperty("additionalLocations", out value) &&
            value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
                : [];
        var html = job.TryGetProperty("descriptionHtml", out value) &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString() ?? ""
                : job.TryGetProperty("compressedDescriptionHtml", out value) &&
                    !string.IsNullOrWhiteSpace(value.GetString())
                        ? ExpandCachedDescription(value.GetString()!)
                        : "";
        var remote = remoteDetector.Analyze(title, primaryLocation, additionalLocations, html);
        var extended = extendedDetector.Analyze(title, primaryLocation, additionalLocations, html);
        return new
        {
            requisitionId = requisition,
            title,
            detectedConcepts = detector.Analyze(
                title, primaryLocation, additionalLocations, html, remote, extended),
            remoteWork = remote,
            extendedLocationRequirement = extended
        };
    }).ToArray();
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new
    {
        jobConceptCatalogVersion = catalog.Version,
        jobs = results
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    Console.WriteLine($"JOB FIT CALIBRATION DETECTOR jobs={results.Length} catalog={catalog.Version}");
    return;
}

if (args.Length >= 2 && args[0] == "--extended-location-corpus")
{
    var detector = new ExtendedLocationRequirementDetector();
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var results = jobs.Select(job =>
        {
            var html = job.TryGetProperty("descriptionHtml", out var description) &&
                !string.IsNullOrWhiteSpace(description.GetString())
                    ? description.GetString() ?? ""
                    : job.TryGetProperty("compressedDescriptionHtml", out var compressed) &&
                        !string.IsNullOrWhiteSpace(compressed.GetString())
                            ? ExpandCachedDescription(compressed.GetString()!)
                            : "";
            return new
            {
                Company = job.TryGetProperty("companyId", out var company) ? company.GetString() ?? "unknown" : "unknown",
                Requisition = job.TryGetProperty("requisitionId", out var requisition) ? requisition.GetString() ?? "" : "",
                Title = job.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                Analysis = detector.Analyze(
                    job.TryGetProperty("title", out title) ? title.GetString() ?? "" : "",
                    job.TryGetProperty("primaryLocation", out var location) ? location.GetString() ?? "" : "",
                    job.TryGetProperty("additionalLocations", out var additional) &&
                        additional.ValueKind == JsonValueKind.Array
                            ? additional.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
                            : [],
                    html)
            };
        }).ToArray();

        Console.WriteLine($"EXTENDED LOCATION CORPUS {Path.GetFileName(path)} jobs={jobs.Length}");
        foreach (var company in results.GroupBy(result => result.Company).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {company.Key}: total={company.Count()}, " +
                $"strong={company.Count(item => item.Analysis.Confidence == "strong")}, " +
                $"questionable={company.Count(item => item.Analysis.Confidence == "questionable")}, " +
                $"none={company.Count(item => item.Analysis.Confidence == "none")}");
            foreach (var item in company.Where(item => item.Analysis.Confidence != "none"))
            {
                Console.WriteLine($"    {item.Analysis.Confidence} {item.Requisition} {item.Title}: {item.Analysis.Summary}");
            }
        }
    }
    return;
}

if (args.Length >= 2 && args[0] == "--credential-corpus")
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var catalog = detector.CatalogItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var results = jobs.Select(job =>
        {
            var html = job.TryGetProperty("descriptionHtml", out var description) &&
                !string.IsNullOrWhiteSpace(description.GetString())
                    ? description.GetString() ?? ""
                    : job.TryGetProperty("compressedDescriptionHtml", out var compressed) &&
                        !string.IsNullOrWhiteSpace(compressed.GetString())
                            ? ExpandCachedDescription(compressed.GetString()!)
                            : "";
            return new
            {
                Company = job.TryGetProperty("companyId", out var company)
                    ? company.GetString() ?? "unknown" : "unknown",
                Requisition = job.TryGetProperty("requisitionId", out var requisition)
                    ? requisition.GetString() ?? "" : "",
                Analysis = detector.Analyze(html)
            };
        }).ToArray();

        Console.WriteLine($"CREDENTIAL CORPUS {Path.GetFileName(path)} jobs={jobs.Length}");
        foreach (var company in results.GroupBy(result => result.Company).OrderBy(group => group.Key))
        {
            var matches = company.SelectMany(item => item.Analysis.Credentials).ToArray();
            Console.WriteLine($"  COMPANY {company.Key}: jobs-with-credentials=" +
                $"{company.Count(item => item.Analysis.Credentials.Count > 0)}/{company.Count()}, " +
                $"matches={matches.Length}, required={matches.Count(item => item.Requirement == "required")}");
            foreach (var category in matches
                .GroupBy(match => catalog[match.CredentialId].Category)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
            {
                Console.WriteLine($"    CATEGORY {category.Key}: {category.Count()}");
                foreach (var credential in category.GroupBy(match => match.CredentialId)
                    .OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
                {
                    Console.WriteLine($"      {credential.Key}: {credential.Count()} " +
                        $"(required={credential.Count(item => item.Requirement == "required")}, " +
                        $"preferred={credential.Count(item => item.Requirement == "preferred")})");
                }
            }
            foreach (var unknown in company.SelectMany(item => item.Analysis.UnknownRequirements
                .Select(requirement => new { item.Requisition, Requirement = requirement }))
                .GroupBy(item => item.Requirement.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key))
            {
                Console.WriteLine($"    UNKNOWN REQUIRED {unknown.Key}: {unknown.Count()} " +
                    $"[{string.Join(',', unknown.Select(item => item.Requisition).Distinct().Take(5))}]");
            }
            foreach (var mention in company.SelectMany(item => item.Analysis.UnrecognizedMentions)
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()).Take(20))
            {
                Console.WriteLine($"    UNRESOLVED MENTION ({mention.Count()}): {mention.Key}");
            }
        }
    }
    return;
}

if (args.Length >= 2 && args[0] == "--academic-corpus")
{
    var detector = new AcademicQualificationDetector();
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var results = jobs.Select(job =>
        {
            var html = job.TryGetProperty("descriptionHtml", out var description) &&
                !string.IsNullOrWhiteSpace(description.GetString())
                    ? description.GetString() ?? ""
                    : job.TryGetProperty("compressedDescriptionHtml", out var compressed) &&
                        !string.IsNullOrWhiteSpace(compressed.GetString())
                            ? ExpandCachedDescription(compressed.GetString()!)
                            : "";
            return new
            {
                Requisition = job.TryGetProperty("requisitionId", out var requisition)
                    ? requisition.GetString() ?? "" : "",
                Title = job.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                Analysis = detector.Analyze(html)
            };
        }).ToArray();
        var mixed = results.Where(result =>
            result.Analysis.Paths.Any(item => item.Requirement is "required" or "minimum") &&
            result.Analysis.Paths.Any(item => item.Requirement is "preferred" or "desired"))
            .ToArray();
        Console.WriteLine($"ACADEMIC CORPUS {Path.GetFileName(path)} jobs={jobs.Length}, " +
            $"parsed={results.Count(result => result.Analysis.ParseStatus == "parsed")}, " +
            $"mixed-strict-preferred={mixed.Length}");
        foreach (var result in mixed.Take(30))
        {
            Console.WriteLine($"  {result.Requisition} | {result.Title} | " +
                $"minimum={result.Analysis.MinimumLevel} | " +
                string.Join(", ", result.Analysis.Paths.Select(item =>
                    $"{item.Level}:{item.Requirement}")));
            foreach (var evidence in result.Analysis.Evidence.Take(4))
            {
                Console.WriteLine($"    {evidence}");
            }
        }
    }
    return;
}

if (args.Length >= 2 && args[0] == "--remote-corpus")
{
    var detector = new RemoteWorkDetector();
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var results = jobs.Select(job => new
        {
            Company = job.TryGetProperty("company", out var company) ? company.GetString() ?? "unknown" : "unknown",
            Requisition = job.TryGetProperty("requisitionId", out var requisition) ? requisition.GetString() ?? "" : "",
            Title = job.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            Analysis = detector.Analyze(
                job.TryGetProperty("title", out title) ? title.GetString() ?? "" : "",
                job.TryGetProperty("location", out var location) ? location.GetString() ?? "" : "",
                job.TryGetProperty("additionalLocations", out var additional) &&
                    additional.ValueKind == JsonValueKind.Array
                        ? additional.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
                        : [],
                job.TryGetProperty("descriptionHtml", out var description) ? description.GetString() ?? "" : "")
        }).ToArray();

        Console.WriteLine($"REMOTE CORPUS {Path.GetFileName(path)} jobs={jobs.Length}");
        foreach (var company in results.GroupBy(result => result.Company).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {company.Key}: total={company.Count()}, " +
                $"strong={company.Count(item => item.Analysis.ConcernLevel == "strong")}, " +
                $"questionable={company.Count(item => item.Analysis.ConcernLevel == "questionable")}, " +
                $"none={company.Count(item => item.Analysis.ConcernLevel == "none")}");
            foreach (var item in company.Where(item => item.Analysis.ConcernLevel != "none"))
            {
                Console.WriteLine($"    {item.Analysis.ConcernLevel} {item.Requisition} {item.Title}: {item.Analysis.Summary}");
                foreach (var signal in item.Analysis.Signals)
                {
                    Console.WriteLine($"      {signal.Category}: {signal.Evidence}");
                }
            }
        }
    }
    return;
}

if (args.Length >= 2 && args[0] == "--authorization-corpus")
{
    var detector = new WorkAuthorizationDetector();
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var jobs = document.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        var analyses = jobs.Select(job => new
        {
            Requisition = job.TryGetProperty("requisitionId", out var requisition) ? requisition.GetString() : "",
            Analysis = detector.Analyze(job.TryGetProperty("descriptionHtml", out var description)
                ? description.GetString() ?? ""
                : "")
        }).ToArray();
        Console.WriteLine($"CORPUS {Path.GetFileName(path)} jobs={jobs.Length}");
        foreach (var group in analyses
            .Where(item => item.Analysis.Eligibility != "noneSpecified" || item.Analysis.Sponsorship != "noneSpecified")
            .GroupBy(item => new { item.Analysis.Eligibility, item.Analysis.Sponsorship, item.Analysis.Strength })
            .OrderByDescending(group => group.Count()))
        {
            Console.WriteLine($"  {group.Key.Eligibility} | {group.Key.Sponsorship} | {group.Key.Strength}: {group.Count()}");
            foreach (var sample in group.Take(3))
            {
                Console.WriteLine($"    {sample.Requisition}: {sample.Analysis.Evidence.FirstOrDefault()}");
            }
        }

        var descriptions = jobs.Select(job => job.TryGetProperty("descriptionHtml", out var description)
            ? description.GetString() ?? ""
            : "").ToArray();
        var clearanceAnalyses = descriptions.Select(JobAnalysis.AnalyzeClearance).ToArray();
        Console.WriteLine("CLEARANCE");
        foreach (var group in clearanceAnalyses
            .GroupBy(item => new { item.Level, item.Requirement })
            .OrderByDescending(group => group.Count()))
        {
            Console.WriteLine($"  {group.Key.Level} | {group.Key.Requirement}: {group.Count()}");
        }
        var explicitNoClearance = descriptions
            .Select((description, index) => new { description, analysis = clearanceAnalyses[index] })
            .Where(item => item.description.Contains(
                "does not require a Security Clearance", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Console.WriteLine($"  explicit-no-clearance={explicitNoClearance.Length}; " +
            $"none-mentioned={explicitNoClearance.Count(item => item.analysis.Level == "noneMentioned")}");

        var academicDetector = new AcademicQualificationDetector();
        var academicAnalyses = descriptions.Select(academicDetector.Analyze).ToArray();
        Console.WriteLine($"ACADEMIC ABET={academicAnalyses.Count(item =>
            item.Accreditations?.Any(accreditation => accreditation.Name == "ABET") == true)}");

        var credentialDetector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
        var credentialMatches = descriptions
            .SelectMany(description => credentialDetector.Analyze(description).Credentials)
            .GroupBy(item => item.CredentialId)
            .OrderByDescending(group => group.Count());
        Console.WriteLine("CREDENTIALS");
        foreach (var group in credentialMatches)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
    }
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Local mode is the safe default", TestLocalDefaultAsync),
    ("Container mode is isolated from Local and Azure hosting semantics", TestContainerConfigurationAsync),
    ("Container workspaces are browser-isolated with non-secure LAN cookies", TestContainerWorkspaceMiddlewareAsync),
    ("Configured Data Protection keys persist to the requested directory", TestDataProtectionPersistenceAsync),
    ("Health endpoint is lightweight and returns HTTP 200", TestHealthEndpointAsync),
    ("Log fields cannot forge additional lines", TestLogValueSanitizationAsync),
    ("Version endpoint exposes only immutable deployment identity", TestVersionEndpointAsync),
    ("Azure mode requires explicit storage configuration", TestAzureValidationAsync),
    ("Legacy Azure settings migrate without overriding canonical settings", TestLegacyAzureConfigurationAsync),
    ("Workspace identifiers are random and strictly validated", TestWorkspaceIdentityAsync),
    ("Workspace cookie has durable security settings", TestCookieOptionsAsync),
    ("Workspace cookie value is integrity-protected", TestProtectedCookieAsync),
    ("Workspace middleware preserves isolation through a protected cookie", TestWorkspaceMiddlewareAsync),
    ("Legacy workspace cookies migrate without changing workspace identity", TestLegacyWorkspaceCookieMigrationAsync),
    ("Azure state changes require the exact application origin", TestOriginValidationAsync),
    ("Legacy and new accounts default to no administrator roles", TestAccountRoleCompatibilityAsync),
    ("Admin authorization distinguishes anonymous, non-admin, and admin users", TestAdminAuthorizationAsync),
    ("Classifier client serializes and validates the diagnostic contract", TestClassifierClientContractAsync),
    ("LLM client validates GPU, model, schema, and prompt contracts", TestLlmClassifierContractAsync),
    ("LLM evaluation reuses the independently labeled fixture scope", TestLlmFixtureMetricsAsync),
    ("Classifier unavailability is isolated from JSM", TestClassifierUnavailableAsync),
    ("First-admin bootstrap is hashed, expiring, single-use, and durable", TestAdminBootstrapLifecycleAsync),
    ("Concurrent first-admin claims grant exactly one account", TestAdminBootstrapConcurrencyAsync),
    ("Admin bootstrap requires explicit non-Azure configuration", TestAdminBootstrapConfigurationAsync),
    ("Account passwords are hashed and normalized emails are unique", TestAccountPasswordAndEmailAsync),
    ("Anonymous workspace claims preserve state and establish ownership", TestAccountWorkspaceClaimAsync),
    ("Failed account claims remain atomic and retryable", TestFailedAccountClaimAsync),
    ("Claimed workspaces reject duplicate and cross-account ownership", TestAccountAuthorizationAsync),
    ("Password reset tokens expire, are single use, and invalidate old credentials", TestPasswordResetAsync),
    ("Email verification tokens are hashed and single use", TestEmailVerificationAsync),
    ("Password reset requests do not enumerate accounts", TestPasswordResetEnumerationAsync),
    ("Authentication persistence choices are bounded", TestAccountPersistenceAsync),
    ("Workspace reset does not delete its owning account", TestAccountSurvivesWorkspaceResetAsync),
    ("Portable workspace exports exclude authentication secrets", TestAccountSecretsExcludedFromExportAsync),
    ("File storage round-trips beside its configured base", TestFileStoreAsync),
    ("Workspace reset deletes only known local state documents", TestFileResetAsync),
    ("Blob namespaces are isolated and traversal-resistant", TestBlobNamespaceAsync),
    ("Annotation corpus is isolated, durable, and exports conservative labels", TestAnnotationLabelingAsync),
    ("Different workspaces resolve identical sources to one shared cache", TestSharedSourceCacheAsync),
    ("Concurrent workspace refreshes use one provider request", TestSharedRefreshSingleFlightAsync),
    ("Workspace preferences cannot mutate canonical shared source data", TestPreferencesDoNotMutateSharedCacheAsync),
    ("Legacy workspace split caches are never active storage", TestLegacyWorkspaceCacheInactiveAsync),
    ("New workspace settings are neutral", TestNeutralDefaultsAsync),
    ("Job Fit defaults off and rejects unknown concepts", TestJobFitSettingsAsync),
    ("Travel tolerance migrates legacy preferences deterministically", TestTravelToleranceMigrationAsync),
    ("Preferred work location migrates legacy preferences deterministically", TestWorkLocationMigrationAsync),
    ("Canonical job concepts are detected during normalization", TestJobConceptDetectionAsync),
    ("Detector evaluation uses independent labels and valid deterministic metrics", TestDetectorEvaluationAsync),
    ("Obsolete automatic-refresh settings are ignored", TestObsoleteAutomaticSettingsIgnoredAsync),
    ("Legacy applied source remains configured", TestLegacyAppliedSourceMigrationAsync),
    ("Legacy cached posting URLs migrate to the canonical field", TestLegacyCacheUrlMigrationAsync),
    ("Portable workspace round-trips settings and curated states", TestPortableWorkspaceRoundTripAsync),
    ("Portable Job Fit settings reject unknown concepts and preserve legacy imports", TestPortableJobFitAsync),
    ("Portable source import distinguishes pending and equivalent state", TestPortableSourceImportStateAsync),
    ("Portable workspace validates company-scoped canonical job state", TestPortableWorkspaceValidationAsync),
    ("Portable workspace restores after a complete reset", TestPortableWorkspaceResetRestoreAsync),
    ("Fresh catalog snapshots retain the applied source", TestFreshCatalogSourceAsync),
    ("Boeing is a catalog-driven U.S. job source", TestBoeingCatalogAsync),
    ("Expanded company catalog contains the five selected live sources", TestExpandedCompanyCatalogAsync),
    ("Expanded company catalog contains the next five categorized sources", TestNextCompanyCatalogAsync),
    ("Workday top-level country facets normalize generically", TestTopLevelCountryFacetAsync),
    ("Cross-provider location labels are grouped by U.S. state", TestExpandedLocationGroupingAsync),
    ("SmartRecruiters postings normalize through the generic source client", TestSmartRecruitersSourceAsync),
    ("SmartRecruiters exact duplicates across pages are deterministic", TestSmartRecruitersCrossPageDuplicateAsync),
    ("SmartRecruiters location variants merge without identity collisions", TestSmartRecruitersLocationVariantAsync),
    ("SmartRecruiters conflicting requisitions retain unique identities", TestSmartRecruitersConflictAsync),
    ("AECOM duplicate requisitions complete a catalog refresh", TestAecomDuplicateRefreshAsync),
    ("Repeated refresh reuses unchanged cached job details", TestRepeatedRefreshCacheReuseAsync),
    ("Recent company switches use cache while explicit and stale refreshes use providers", TestRecentSourceSwitchAsync),
    ("Changed listing metadata invalidates one cached detail", TestChangedListingInvalidationAsync),
    ("Boeing parser-version changes reclassify only Boeing cached text without provider download", TestLocalReclassificationAsync),
    ("Large source hydration obeys detail and concurrency bounds", TestBoundedLargeSourceAsync),
    ("Removed source jobs remain cached without remaining available", TestRemovedJobCacheAsync),
    ("Untracked jobs missing from refresh leave the visible catalog", () => TestMissingJobWorkflowRetentionAsync(JobWorkflowStates.Normal)),
    ("Saved jobs missing from refresh remain visible", () => TestMissingJobWorkflowRetentionAsync(JobWorkflowStates.Saved)),
    ("Applied jobs missing from refresh remain visible", () => TestMissingJobWorkflowRetentionAsync(JobWorkflowStates.Applied)),
    ("Closed jobs missing from refresh remain visible", () => TestMissingJobWorkflowRetentionAsync(JobWorkflowStates.Closed)),
    ("Retained jobs reconcile without duplication when relisted", TestRetainedJobRelistingAsync),
    ("Compact list responses omit full descriptions", TestCompactListPayloadAsync),
    ("Lazy detail loading fetches a missing detail only once", TestLazyDetailCacheAsync),
    ("Company caches coexist without requisition collisions", TestPerCompanyCacheEnvelopeAsync),
    ("Company/query cache writes remain isolated", TestCompanyCacheWriteIsolationAsync),
    ("Legacy cumulative caches migrate safely and idempotently", TestSplitCacheMigrationAsync),
    ("Cached descriptions are compressed at rest and hydrate losslessly", TestCompressedCacheAsync),
    ("Cached detail reads perform no persistence writes", TestCachedDetailPureReadAsync),
    ("Unchanged refreshes avoid cache and history writes", TestUnchangedRefreshWriteSuppressionAsync),
    ("First empty refresh persists a source baseline", TestFirstEmptyRefreshPersistsBaselineAsync),
    ("Partial hydration advances through the final batch", TestPartialHydrationCyclesAsync),
    ("Listing page safety limits surface truncation", TestListingPageLimitAsync),
    ("Mocked Northrop-scale refresh remains bounded and compact", TestNorthropScaleMetricsAsync),
    ("Unsupported active company state migrates without source reinterpretation", TestUnsupportedCompanyMigrationAsync),
    ("Unsupported company history remains isolated", TestUnsupportedCompanyHistoryAsync),
    ("Established credential catalog entries validate", TestCredentialCatalogAsync),
    ("NetApp and Dell storage credentials are structured", TestStorageCredentialsAsync),
    ("Existing credential recognition does not regress", TestExistingCredentialRegressionAsync),
    ("Credential alternatives retain explicit OR semantics", TestCredentialAlternativeSemanticsAsync),
    ("Unknown mandatory credentials are surfaced conservatively", TestUnknownRequiredCredentialAsync),
    ("Today's named credentials avoid obvious false positives", TestLeidosCredentialDiscoveryFixtureAsync),
    ("Academic detector recognizes advanced master's-or-higher wording", TestAdvancedDegreeAsync),
    ("Academic detector separates strict minimums from preferred higher degrees", TestAcademicPreferenceSeparationAsync),
    ("Academic detector treats ABET as accreditation", TestAbetAccreditationAsync),
    ("Work authorization detector recognizes strict U.S. citizenship", TestUsCitizenshipAsync),
    ("Work authorization detector distinguishes citizens and permanent residents", TestCitizenOrResidentAsync),
    ("Work authorization detector recognizes employment sponsorship", TestSponsorshipAsync),
    ("Work authorization detector treats U.S.-person wording conservatively", TestUsPersonAsync),
    ("Work authorization detector ignores unrelated sponsor and resident language", TestAuthorizationFalsePositivesAsync),
    ("Work authorization detector surfaces non-U.S. and export wording for review", TestInternationalAuthorizationAsync),
    ("Work authorization detector recognizes location-specific work rights", TestLocationWorkRightsAsync),
    ("Clearance detector ignores explicit no-clearance statements", TestNoClearanceRequiredAsync),
    ("Remote detector finds explicit onsite duties", TestRemoteOnsiteAsync),
    ("Remote detector finds field deployment requirements", TestRemoteFieldDeploymentAsync),
    ("Remote detector finds commuting-distance restrictions", TestRemoteCommuteAsync),
    ("Remote detector finds recurring onsite days", TestRemoteOnsiteDaysAsync),
    ("Remote detector classifies substantial travel", TestRemoteTravelAsync),
    ("Remote detector accepts ordinary true-remote wording", TestTrueRemoteAsync),
    ("Remote detector leaves ambiguous site wording alone", TestRemoteAmbiguousAsync),
    ("Remote detector ignores prior site experience", TestRemoteHistoricalExperienceAsync),
    ("Remote detector recognizes sanitized Leidos deployment language", TestLeidosRemoteFixtureAsync),
    ("Remote detector keeps a sanitized MTM-style remote role neutral", TestMtmRemoteFixtureAsync),
    ("Remote detector recognizes sanitized Boeing frequent-travel language", TestBoeingRemoteFixtureAsync),
    ("New-company prose identifies remote roles without trusting generic boilerplate", TestExpandedRemoteTerminologyAsync),
    ("New-company compensation wording parses deterministically", TestExpandedSalaryTerminologyAsync),
    ("Boeing summary pay ranges parse narrowly and aggregate compatible bands", TestBoeingSummaryPayRangesAsync),
    ("New-company credential terminology is cataloged", TestExpandedCredentialTerminologyAsync),
    ("Extended-location detector recognizes explicit assignment obligations", TestExtendedLocationPositiveAsync),
    ("Extended-location detector rejects incidental locations and ordinary travel", TestExtendedLocationNegativeAsync),
    ("Extended-location detector preserves relevant evidence in mixed context", TestExtendedLocationMixedContextAsync),
    ("Extended-location detector separates questionable from strong evidence", TestExtendedLocationConfidenceAsync),
    ("Extended away-from-home assignments are detected without ordinary-travel noise", TestExtendedAwayAssignmentAsync),
    ("Selected-company terminology remains covered by sanitized fixtures", TestExpandedCompanyFixturesAsync),
    ("Provider HTML normalization preserves parsing and display separators", TestProviderHtmlNormalizationAsync),
    ("Workflow state transitions are canonical and validated", TestWorkflowStateTransitionsAsync),
    ("Workflow state persists through a catalog restart", TestWorkflowStateRoundTripAsync),
    ("Workflow state remains workspace-isolated", TestWorkflowStateWorkspaceIsolationAsync),
    ("Workflow identity remains company-scoped", TestWorkflowStateCompanyIsolationAsync),
    ("Legacy Saved and history data migrate safely", TestLegacySavedHistoryMigrationAsync),
    ("Invalid legacy combinations migrate to one state", TestLegacyCombinationMigrationAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"All {tests.Length} deterministic architecture tests passed.");

static Task TestLocalDefaultAsync()
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
    var hosting = HostingConfiguration.FromConfiguration(configuration);
    Assert(hosting.IsLocal, "Hosting must default to Local.");
    Assert(hosting.StorageAccount is null && hosting.StorageContainer is null,
        "Local mode must not require Azure storage.");
    return Task.CompletedTask;
}

static Task TestLogValueSanitizationAsync()
{
    var sanitized = JobCatalog.SanitizeLogValue("United States\r\nforged\u2028entry\u2029tail");
    Assert(sanitized == "United States  forged entry tail",
        "Log sanitization did not neutralize every supported line separator.");
    return Task.CompletedTask;
}

static Task TestContainerConfigurationAsync()
{
    AssertThrows<InvalidOperationException>(() => HostingConfiguration.FromConfiguration(
        Configuration(new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Container"
        })));

    var hosting = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Container",
            [HostingConfiguration.DataProtectionPathSetting] = "/var/lib/jsm/dataprotection"
        }));
    Assert(hosting.IsContainer && !hosting.IsLocal && !hosting.IsAzure,
        "Container mode was not selected independently of Local and Azure.");
    Assert(hosting.UsesLocalStorage && hosting.UsesPerBrowserWorkspaces,
        "Container mode did not select local persistence with browser-isolated workspaces.");
    Assert(hosting.RequiresSameOriginProtection && !hosting.UsesAzureTransportSecurity,
        "Container mode did not retain browser request protection without Azure HTTPS behavior.");
    Assert(hosting.DataProtectionPath == "/var/lib/jsm/dataprotection",
        "Container Data Protection path was not read from configuration.");
    return Task.CompletedTask;
}

static async Task TestContainerWorkspaceMiddlewareAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var hosting = new HostingConfiguration(
        ApplicationHostingMode.Container, null, null, "/var/lib/jsm/dataprotection");
    var middleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        hosting,
        provider,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);
    var accounts = TestAccountService().Service;

    var firstContext = new DefaultHttpContext();
    firstContext.Request.Path = "/api/workspace/identity";
    var firstWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(firstContext, firstWorkspace, accounts);

    var secondContext = new DefaultHttpContext();
    secondContext.Request.Path = "/api/workspace/identity";
    var secondWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(secondContext, secondWorkspace, accounts);

    Assert(WorkspaceIdentity.IsValid(firstWorkspace.WorkspaceId) &&
           WorkspaceIdentity.IsValid(secondWorkspace.WorkspaceId) &&
           firstWorkspace.WorkspaceId != secondWorkspace.WorkspaceId,
        "Independent Container browsers were assigned one shared workspace.");
    Assert(!firstContext.Response.Headers.SetCookie.ToString()
            .Contains("secure", StringComparison.OrdinalIgnoreCase),
        "HTTP Container mode issued an Azure-only Secure workspace cookie.");
}

static Task TestDataProtectionPersistenceAsync()
{
    var directory = Path.Combine(
        Path.GetTempPath(), $"job-search-manager-dataprotection-test-{Guid.NewGuid():N}");
    try
    {
        var services = new ServiceCollection();
        services.AddJobSearchManagerDataProtection(new HostingConfiguration(
            ApplicationHostingMode.Container, null, null, directory));
        using var serviceProvider = services.BuildServiceProvider();
        var protector = serviceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("container-test");
        var protectedValue = protector.Protect("persistent payload");
        Assert(protector.Unprotect(protectedValue) == "persistent payload",
            "Configured Data Protection provider did not round-trip a payload.");
        Assert(Directory.EnumerateFiles(directory, "key-*.xml").Any(),
            "Data Protection did not write its key ring to the configured directory.");
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestHealthEndpointAsync()
{
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = HealthEndpoint.Path;
    context.Response.Body = new MemoryStream();

    Assert(await HealthEndpoint.TryHandleAsync(context),
        "The health endpoint did not handle GET /healthz.");
    context.Response.Body.Position = 0;
    using var reader = new StreamReader(context.Response.Body);
    Assert(context.Response.StatusCode == StatusCodes.Status200OK &&
           await reader.ReadToEndAsync() == HealthEndpoint.ResponseBody,
        "The health endpoint did not return its expected HTTP 200 response.");

    var post = new DefaultHttpContext();
    post.Request.Method = HttpMethods.Post;
    post.Request.Path = HealthEndpoint.Path;
    Assert(!await HealthEndpoint.TryHandleAsync(post),
        "The health endpoint accepted a state-changing request.");
}

static async Task TestVersionEndpointAsync()
{
    const string commit = "0123456789abcdef0123456789abcdef01234567";
    var configuration = Configuration(new Dictionary<string, string?>
    {
        [VersionEndpoint.CommitSetting] = commit
    });
    var hosting = new HostingConfiguration(
        ApplicationHostingMode.Container,
        null,
        null,
        "/var/lib/jsm/dataprotection");
    var versionInfo = VersionEndpoint.Create(configuration, hosting);
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = VersionEndpoint.Path;
    context.Response.Body = new MemoryStream();

    Assert(await VersionEndpoint.TryHandleAsync(context, versionInfo),
        "The version endpoint did not handle GET /version.");
    context.Response.Body.Position = 0;
    using var document = await JsonDocument.ParseAsync(context.Response.Body);
    var root = document.RootElement;
    Assert(context.Response.StatusCode == StatusCodes.Status200OK &&
           root.GetProperty("commit").GetString() == commit &&
           root.GetProperty("hostingMode").GetString() == "Container" &&
           !string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()),
        "The version endpoint did not return the expected deployment identity.");
    Assert(root.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(
            new[] { "commit", "hostingMode", "version" }.Order()),
        "The version endpoint exposed fields beyond commit, version, and hostingMode.");

    var invalid = VersionEndpoint.Create(
        Configuration(new Dictionary<string, string?>
        {
            [VersionEndpoint.CommitSetting] = "not-a-full-commit"
        }),
        hosting);
    Assert(invalid.Commit == VersionEndpoint.UnknownCommit,
        "An invalid deployment commit was reported as trusted identity.");

    var post = new DefaultHttpContext();
    post.Request.Method = HttpMethods.Post;
    post.Request.Path = VersionEndpoint.Path;
    Assert(!await VersionEndpoint.TryHandleAsync(post, versionInfo),
        "The version endpoint accepted a state-changing request.");
}

static Task TestAzureValidationAsync()
{
    AssertThrows<InvalidOperationException>(() => HostingConfiguration.FromConfiguration(
        Configuration(new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Azure"
        })));
    var hosting = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Azure",
            [HostingConfiguration.StorageAccountSetting] = "workdayjobmanagerstore",
            [HostingConfiguration.StorageContainerSetting] = "userdata"
        }));
    Assert(hosting.IsAzure, "Explicit Azure mode was not selected.");
    Assert(hosting.GetBlobServiceUri() ==
        new Uri("https://workdayjobmanagerstore.blob.core.windows.net"),
        "Blob service endpoint was not derived centrally.");
    return Task.CompletedTask;
}

static Task TestLegacyAzureConfigurationAsync()
{
    var legacy = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.LegacyModeSetting] = "Azure",
            [HostingConfiguration.LegacyStorageAccountSetting] = "workdayjobmanagerstore",
            [HostingConfiguration.LegacyStorageContainerSetting] = "userdata"
        }));
    Assert(legacy.IsAzure && legacy.StorageAccount == "workdayjobmanagerstore" &&
           legacy.StorageContainer == "userdata",
        "Existing Azure application settings did not migrate through the legacy aliases.");

    var canonical = HostingConfiguration.FromConfiguration(Configuration(
        new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Local",
            [HostingConfiguration.LegacyModeSetting] = "Azure"
        }));
    Assert(canonical.IsLocal,
        "A legacy Azure setting overrode the canonical hosting-mode setting.");
    return Task.CompletedTask;
}

static Task TestWorkspaceIdentityAsync()
{
    var first = WorkspaceIdentity.Create();
    var second = WorkspaceIdentity.Create();
    Assert(first != second, "Secure workspace identifiers must not repeat.");
    Assert(WorkspaceIdentity.IsValid(first) && first.Length == 64,
        "Generated workspace ID is not a 256-bit lowercase hexadecimal value.");
    Assert(!WorkspaceIdentity.IsValid("../another-workspace") &&
           !WorkspaceIdentity.IsValid(first.ToUpperInvariant()) &&
           !WorkspaceIdentity.IsValid("local"),
        "Workspace validation accepted unsafe or non-Azure values.");
    return Task.CompletedTask;
}

static Task TestCookieOptionsAsync()
{
    var options = WorkspaceIdentity.CreateCookieOptions(secure: true);
    Assert(options.HttpOnly && options.Secure && options.IsEssential,
        "Azure cookie security flags are incomplete.");
    Assert(options.SameSite == SameSiteMode.Lax && options.Path == "/",
        "Workspace cookie scope is incorrect.");
    Assert(options.MaxAge >= TimeSpan.FromDays(365),
        "Workspace cookie is not long-lived.");
    return Task.CompletedTask;
}

static Task TestProtectedCookieAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var protector = provider.CreateProtector("JobSearchManager.AnonymousWorkspace.v1");
    var workspaceId = WorkspaceIdentity.Create();
    var protectedValue = protector.Protect(workspaceId);
    Assert(protectedValue != workspaceId && protector.Unprotect(protectedValue) == workspaceId,
        "Workspace cookie protection did not preserve integrity.");
    AssertThrows<System.Security.Cryptography.CryptographicException>(() =>
        protector.Unprotect(protectedValue + "tampered"));
    return Task.CompletedTask;
}

static async Task TestWorkspaceMiddlewareAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var hosting = new HostingConfiguration(
        ApplicationHostingMode.Azure,
        "workdayjobmanagerstore",
        "userdata");
    var middleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        hosting,
        provider,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);
    var accounts = TestAccountService().Service;

    var firstContext = new DefaultHttpContext();
    firstContext.Request.Path = "/api/workspace/identity";
    var firstWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(firstContext, firstWorkspace, accounts);
    var setCookie = firstContext.Response.Headers.SetCookie.ToString();
    Assert(setCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
           setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
           setCookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase),
        "First Azure response did not set the secure workspace cookie.");
    var cookiePair = setCookie.Split(';', 2)[0];
    Assert(!cookiePair.Contains(firstWorkspace.WorkspaceId, StringComparison.Ordinal),
        "Raw workspace ID leaked into the cookie.");

    var returningContext = new DefaultHttpContext();
    returningContext.Request.Path = "/api/workspace/identity";
    returningContext.Request.Headers.Cookie = cookiePair;
    var returningWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(returningContext, returningWorkspace, accounts);
    Assert(returningWorkspace.WorkspaceId == firstWorkspace.WorkspaceId,
        "Protected cookie did not restore the same workspace.");

    var tamperedContext = new DefaultHttpContext();
    tamperedContext.Request.Path = "/api/workspace/identity";
    tamperedContext.Request.Headers.Cookie = cookiePair + "tampered";
    var tamperedWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(tamperedContext, tamperedWorkspace, accounts);
    Assert(tamperedWorkspace.WorkspaceId != firstWorkspace.WorkspaceId,
        "Tampered cookie crossed into the original workspace.");
}

static async Task TestLegacyWorkspaceCookieMigrationAsync()
{
    var provider = new EphemeralDataProtectionProvider();
    var workspaceId = WorkspaceIdentity.Create();
    var legacyProtector = provider.CreateProtector(WorkspaceIdentity.LegacyProtectorPurpose);
    var context = new DefaultHttpContext();
    context.Request.Path = "/api/workspace/identity";
    context.Request.Headers.Cookie =
        $"{WorkspaceIdentity.LegacyCookieName}={legacyProtector.Protect(workspaceId)}";
    var workspace = new WorkspaceContext();
    var middleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        new HostingConfiguration(ApplicationHostingMode.Azure, "workdayjobmanagerstore", "userdata"),
        provider,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);

    await middleware.InvokeAsync(context, workspace, TestAccountService().Service);

    var setCookies = context.Response.Headers.SetCookie.ToString();
    Assert(workspace.WorkspaceId == workspaceId,
        "The legacy protected cookie did not retain its workspace identity.");
    Assert(setCookies.Contains(WorkspaceIdentity.CookieName, StringComparison.Ordinal) &&
           setCookies.Contains(WorkspaceIdentity.LegacyCookieName, StringComparison.Ordinal),
        "Legacy cookie migration did not issue the canonical cookie and retire the old cookie.");
}

static Task TestOriginValidationAsync()
{
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Put;
    context.Request.Path = "/api/settings";
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("workday-job-manager.azurewebsites.net");
    context.Request.Headers.Origin = "https://workday-job-manager.azurewebsites.net";
    Assert(RequestSecurity.IsStateChangingApiRequest(context.Request) &&
           RequestSecurity.HasSameOrigin(context.Request),
        "Valid same-origin Azure request was rejected.");
    context.Request.Headers.Origin = "https://attacker.example";
    Assert(!RequestSecurity.HasSameOrigin(context.Request),
        "Cross-site state-changing request was accepted.");
    context.Request.Headers.Remove("Origin");
    Assert(!RequestSecurity.HasSameOrigin(context.Request),
        "Missing Origin was accepted for an Azure state change.");
    return Task.CompletedTask;
}

static async Task TestFileStoreAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-test-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(
            directory,
            NullLogger<FileWorkspaceDataStore>.Instance);
        var document = new TestDocument("portable", 42);
        await store.WriteJsonAsync(WorkspaceDataFile.Settings, document);
        var loaded = await store.ReadJsonAsync<TestDocument>(WorkspaceDataFile.Settings);
        Assert(loaded == document, "Application-local JSON did not round-trip.");
        Assert(store.Describe(WorkspaceDataFile.Settings) ==
            Path.Combine(Path.GetFullPath(directory), "settings.json"),
            "File storage escaped its configured application-local directory.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestAnnotationLabelingAsync()
{
    var directory = TestDirectory("annotation-labeling");
    try
    {
        var factory = new TestWorkspaceDataStoreFactory(directory);
        var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var firstConcept = catalog.Concepts[0].Id;
        var secondConcept = catalog.Concepts[1].Id;
        var thirdConcept = catalog.Concepts[2].Id;
        var service = new AnnotationLabelingService(factory, catalog, TimeProvider.System);
        var job = new JobRecord(
            "Platform Engineer", "REQ-LABEL-1", new DateOnly(2026, 8, 31), "Today", "Remote", [],
            "Full time", "https://example.test/jobs/REQ-LABEL-1",
            "<p>Build resilient service interfaces with Kubernetes and careful operational ownership.</p>",
            null, null, "unknown", "not-found", false, null, null, null, "/jobs/REQ-LABEL-1",
            CompanyId: "example", DetectedConcepts:
            [new DetectedJobConcept(firstConcept, "service interfaces"),
             new DetectedJobConcept(secondConcept, "Kubernetes"),
             new DetectedJobConcept(thirdConcept, "operational ownership")],
            JobConceptCatalogVersion: catalog.Version);

        var generated = await service.GenerateAsync([job, job], 200);
        Assert(generated.Added >= 3 && generated.Added <= 6 && generated.Total == generated.Added,
            "Annotation generation was not idempotent or retained duplicate machine candidates.");
        Assert(factory.CreatedWorkspaceIds.SequenceEqual([AnnotationLabelingService.CorpusWorkspaceId]),
            "The annotation corpus was not isolated in its reserved workspace namespace.");
        var firstItem = generated.Queue.Item ?? throw new InvalidOperationException("Generated queue was empty.");
        Assert(generated.Queue.TaxonomyFingerprint.Length == 64 &&
            generated.Queue.TaxonomyVersion == catalog.Version &&
            firstItem.Machine.Confidence is null && firstItem.Machine.Model is null,
            "Annotation provenance omitted the exact taxonomy identity or fabricated confidence/model data.");

        var afterCorrect = await service.DecideAsync(
            firstItem.Id, new AnnotationDecisionRequest(AnnotationDecisions.Correct),
            "admin@example.test", new AnnotationQueueFilter());
        Assert(afterCorrect?.Stats.Reviewed == 1 && afterCorrect.Stats.TrainingEligible == 1 &&
            afterCorrect.Item?.Id != firstItem.Id,
            "A saved annotation did not advance and resume at the next unreviewed item.");
        var secondItem = afterCorrect!.Item!;
        var afterIncorrect = await service.DecideAsync(
            secondItem.Id, new AnnotationDecisionRequest(AnnotationDecisions.Incorrect),
            "admin@example.test", new AnnotationQueueFilter());
        var unsureItem = afterIncorrect!.Item!;
        await service.DecideAsync(
            unsureItem.Id, new AnnotationDecisionRequest(AnnotationDecisions.Unsure, UnsureReason: "Needs domain review"),
            "admin@example.test", new AnnotationQueueFilter());
        var reviewedExport = await service.ExportJsonLinesAsync(AnnotationExportModes.Reviewed);
        var reviewedLines = reviewedExport.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var unsureExport = await service.ExportJsonLinesAsync(AnnotationExportModes.Unsure);
        var allExport = await service.ExportJsonLinesAsync(AnnotationExportModes.All);
        var unreviewedExport = await service.ExportJsonLinesAsync(AnnotationExportModes.Unreviewed);
        Assert(reviewedLines.Length == 2 && unsureExport.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1 &&
            allExport.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == generated.Total &&
            unreviewedExport.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == generated.Total - 3,
            "All/reviewed/unreviewed/unsure export subsets were not exact.");
        using var incorrect = JsonDocument.Parse(reviewedLines.Single(line => line.Contains(secondItem.Id)));
        Assert(incorrect.RootElement.GetProperty("confirmedPresentConceptIds").GetArrayLength() == 0 &&
            incorrect.RootElement.GetProperty("confirmedAbsentCandidateConceptIds").GetArrayLength() == 1,
            "An incorrect candidate was broadened into unsupported taxonomy-wide negatives.");
        using var unsure = JsonDocument.Parse(unsureExport.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single());
        Assert(!unsure.RootElement.GetProperty("currentReview").GetProperty("trainingEligible").GetBoolean() &&
            unsure.RootElement.GetProperty("currentReview").GetProperty("status").GetString() == "unsure",
            "Unsure annotations were not explicitly excluded from training eligibility.");

        static string ImportLine(AnnotationItem item, string fingerprint, string decision, string reviewerType,
            string? concept = null, string? contentHash = null, string? itemId = null, string? taxonomy = null) =>
            JsonSerializer.Serialize(new
            {
                annotationItemId = itemId ?? item.Id, contentHash = contentHash ?? item.ContentHash,
                taxonomyFingerprint = taxonomy ?? fingerprint, decision,
                selectedConceptIds = concept is null ? Array.Empty<string>() : new[] { concept },
                reviewerType, reviewerIdentity = "test-reviewer", rationale = "bounded test rationale"
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var machineImport = string.Join('\n',
            ImportLine(firstItem, service.TaxonomyFingerprint, "incorrect", "codex"),
            ImportLine(firstItem, service.TaxonomyFingerprint, "incorrect", "codex"),
            ImportLine(firstItem, service.TaxonomyFingerprint, "correct", "chatgpt"),
            ImportLine(firstItem, service.TaxonomyFingerprint, "correct", "human-reviewed"),
            ImportLine(firstItem, service.TaxonomyFingerprint, "correct", "codex", itemId: "annotation-missing"),
            ImportLine(firstItem, service.TaxonomyFingerprint, "correct", "codex", contentHash: new string('0', 64)),
            ImportLine(firstItem, service.TaxonomyFingerprint, "correct", "codex", taxonomy: new string('f', 64)),
            ImportLine(firstItem, service.TaxonomyFingerprint, "different-label", "codex", "unknown.concept"),
            "{not-json}") + "\n";
        var importSummary = await service.ImportMachineReviewsAsync(machineImport, "machine-review.jsonl");
        Assert(importSummary.Imported == 2 && importSummary.Unchanged == 1 && importSummary.Conflicts == 2 &&
            importSummary.InvalidProvenance == 1 && importSummary.UnknownItem == 1 &&
            importSummary.ContentHashMismatch == 1 && importSummary.StaleFingerprint == 1 &&
            importSummary.UnknownConcept == 1 && importSummary.Malformed == 1,
            $"Machine import validation, duplicate handling, or conflict accounting regressed: {JsonSerializer.Serialize(importSummary)}");
        var conflicts = await service.GetQueueAsync(new AnnotationQueueFilter("machineDisagreement"));
        Assert(conflicts.Item?.Id == firstItem.Id && conflicts.Stats.MachineDisagreements == 1 &&
            conflicts.Stats.HumanMachineConflicts == 1 && conflicts.Item.TrainingEligible == false &&
            conflicts.Item.Reviewer == "admin@example.test" && conflicts.Item.Decision == AnnotationDecisions.Correct,
            "Machine opinions overwrote human truth or failed to surface an unresolved conflict.");
        await service.DecideAsync(firstItem.Id, new AnnotationDecisionRequest(AnnotationDecisions.Correct),
            "admin@example.test", new AnnotationQueueFilter("humanReviewed"));
        var overridden = await service.GetQueueAsync(new AnnotationQueueFilter("humanReviewed"));
        Assert(overridden.Stats.TrainingEligible == 2 && overridden.Item?.HumanProvenance == "human-overridden",
            "Authenticated human conflict resolution did not restore conservative training eligibility.");

        var scaledJobs = Enumerable.Range(0, 400).Select(index => job with
        {
            RequisitionId = $"REQ-SCALE-{index:D4}",
            SourceUrl = $"https://example.test/jobs/REQ-SCALE-{index:D4}",
            ExternalPath = $"/jobs/REQ-SCALE-{index:D4}",
            DescriptionHtml = $"<p>Project {index:D4} builds resilient service interfaces with Kubernetes and careful operational ownership for unique system {index:D4}.</p>",
            CompanyId = index % 2 == 0 ? "example" : "second-company"
        }).ToArray();
        var scaled = await service.GenerateAsync(scaledJobs, new AnnotationGenerateRequest(1000));
        Assert(scaled.Total == 1000 && scaled.Added == 1000 - generated.Total && scaled.Queue.Item is not null &&
            scaled.Queue.CompanyDistribution.Count == 2,
            "Target-size generation did not append a deterministic 1,000-item corpus with bounded one-item retrieval.");
        var generatedAgain = await service.GenerateAsync(scaledJobs, new AnnotationGenerateRequest(1000));
        Assert(generatedAgain.Added == 0 && generatedAgain.Total == 1000,
            "Repeated generation duplicated items or rebuilt the corpus destructively.");

        var reloaded = new AnnotationLabelingService(factory, catalog, TimeProvider.System);
        var resumed = await reloaded.GetQueueAsync(new AnnotationQueueFilter("reviewed"));
        Assert(resumed.Stats.Reviewed == 2 && resumed.Stats.Unsure == 1 && resumed.Stats.Total == 1000 &&
            resumed.Stats.TrainingEligible == 2 && resumed.Item is not null,
            "Durable annotation decisions or stable item history did not survive scale-up and reload.");
        Assert(AnnotationLabelingService.ValidateDecision(
            new AnnotationDecisionRequest(AnnotationDecisions.DifferentLabel, [firstConcept])) is null &&
            AnnotationLabelingService.ValidateDecision(
                new AnnotationDecisionRequest(AnnotationDecisions.MultipleLabels, [firstConcept])) is not null,
            "Replacement and multi-label validation semantics regressed.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestBlobNamespaceAsync()
{
    var container = new BlobContainerClient(new Uri("https://example.blob.core.windows.net/userdata"));
    var firstId = WorkspaceIdentity.Create();
    var secondId = WorkspaceIdentity.Create();
    var first = new AzureBlobWorkspaceDataStore(
        container, firstId, NullLogger<AzureBlobWorkspaceDataStore>.Instance);
    var second = new AzureBlobWorkspaceDataStore(
        container, secondId, NullLogger<AzureBlobWorkspaceDataStore>.Instance);
    Assert(first.Describe(WorkspaceDataFile.Settings) ==
        $"workspaces/{WorkspaceIdentity.Redact(firstId)}/settings.json", "First Blob description is incorrect.");
    Assert(second.Describe(WorkspaceDataFile.Settings) ==
        $"workspaces/{WorkspaceIdentity.Redact(secondId)}/settings.json", "Second Blob description is incorrect.");
    Assert(first.Describe(WorkspaceDataFile.Settings) != second.Describe(WorkspaceDataFile.Settings),
        "Distinct workspaces resolved to the same Blob.");
    Assert(AzureBlobWorkspaceDataStore.BuildBlobName(firstId, WorkspaceDataFile.Settings) ==
        $"workspaces/{firstId}/settings.json", "Actual Blob namespace is incorrect.");
    Assert(AzureBlobWorkspaceDataStore.BuildBlobName(secondId, WorkspaceDataFile.JobHistory) ==
        $"workspaces/{secondId}/job-history.json", "Second actual Blob namespace is incorrect.");
    var fingerprint = new string('a', 64);
    Assert(AzureBlobWorkspaceDataStore.BuildCompanyCacheBlobName(firstId, "northrop-grumman", fingerprint) ==
        $"shared/job-caches/northrop-grumman/{fingerprint}.json" &&
        AzureBlobWorkspaceDataStore.BuildCompanyCacheBlobName(secondId, "northrop-grumman", fingerprint) ==
        $"shared/job-caches/northrop-grumman/{fingerprint}.json" &&
        first.DescribeCompanyCache("northrop-grumman", fingerprint) ==
        second.DescribeCompanyCache("northrop-grumman", fingerprint),
        "Identical company/query caches remained workspace-scoped.");
    AssertThrows<ArgumentException>(() => AzureBlobWorkspaceDataStore.BuildBlobName(
        "../another-workspace", WorkspaceDataFile.Settings));
    AssertThrows<ArgumentException>(() => AzureBlobWorkspaceDataStore.BuildCompanyCacheBlobName(
        firstId, "../another-company", fingerprint));
    AssertThrows<ArgumentException>(() => AzureBlobWorkspaceDataStore.BuildCompanyCacheBlobName(
        firstId, "northrop-grumman", "not-a-sha256"));
    AssertThrows<ArgumentException>(() => new AzureBlobWorkspaceDataStore(
        container, "../another-workspace", NullLogger<AzureBlobWorkspaceDataStore>.Instance));
    return Task.CompletedTask;
}

static Task TestNeutralDefaultsAsync()
{
    var settings = ViewerSettings.Default;
    Assert(settings.MinimumSalary == 0m,
        "New workspaces did not default minimum acceptable annual pay to zero.");
    Assert(settings.IncludeKeywords.Count == 0 && settings.ExcludeKeywords.Count == 0,
        "New workspaces inherited personal search preferences.");
    Assert(settings.UserProfile?.Education.Level == "notSpecified" &&
           settings.UserProfile.Security?.ClearanceLevel == "notSpecified" &&
           settings.UserProfile.Security?.PublicTrust == "unknown" &&
           settings.UserProfile.WorkAuthorization?.UsStatus == "notSpecified" &&
           settings.UserProfile.WorkAuthorization?.Sponsorship == "unknown" &&
           !settings.HideStrictWorkAuthorizationMismatch,
        "New workspaces inherited personal qualification data.");
    Assert(settings.HasConfiguredSource == false && settings.CompanyId == "" &&
           settings.Country == FacetDefaults.UnitedStatesCountry &&
           !settings.IncludeAllLocations && !settings.IncludeRemote &&
           settings.SelectedPhysicalLocations?.Count == 0,
        "A new workspace was not an explicit unconfigured source with the United States preselected.");
    Assert(settings.JobFit is { Enabled: false } && settings.JobFit.Signals.Count == 0,
        "A new workspace did not default Job Fit to disabled with no required configuration.");
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    AssertThrows<InvalidOperationException>(() => JobSourceQuery.FromSettings(settings, companies));
    return Task.CompletedTask;
}

static async Task TestAccountRoleCompatibilityAsync()
{
    var legacy = JsonSerializer.Deserialize<AccountRegistryDocument>("""
        {"accounts":{"legacy":{"accountId":"legacy","email":"legacy@example.test"}}}
        """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    legacy.Normalize();
    Assert(legacy.Accounts["legacy"].Roles.Count == 0,
        "An account registry without Roles did not load as an empty role list.");
    Assert(new AccountRecord().Roles.Count == 0,
        "A new account record did not default to no roles.");

    var fixture = TestAccountService();
    var created = await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "davidcourtney@outlook.com", "role defaults passphrase",
        new Uri("https://example.test/"));
    Assert(created.Succeeded && created.Account!.Roles.Count == 0 &&
           !AccountRoles.IsAdmin(created.Account),
        "A new account or a particular email address received an implicit Admin role.");
}

static async Task TestAdminAuthorizationAsync()
{
    var fixture = TestAccountService();
    var nonAdmin = (await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "user@example.test", "non admin passphrase",
        new Uri("https://example.test/"))).Account!;
    var admin = (await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "admin@example.test", "admin role passphrase",
        new Uri("https://example.test/"))).Account!;
    await fixture.Store.MutateAsync<bool>(document =>
    {
        document.Accounts[admin.AccountId].Roles = [AccountRoles.Admin];
        return new(true, true);
    });
    admin = (await fixture.Service.GetByIdAsync(admin.AccountId))!;

    var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
    var nonAdminPrincipal = TestPrincipal(nonAdmin);
    var adminPrincipal = TestPrincipal(admin);
    Assert(AdminAuthorization.ExpectedStatusCode(anonymous, null) == StatusCodes.Status401Unauthorized &&
           AdminAuthorization.ExpectedStatusCode(nonAdminPrincipal, nonAdmin) == StatusCodes.Status403Forbidden &&
           AdminAuthorization.ExpectedStatusCode(adminPrincipal, admin) == StatusCodes.Status200OK,
        "The reusable Admin authorization decision did not produce 401/403/200 semantics.");

    var requirement = new AdminRequirement();
    var handler = new AdminAuthorizationHandler(fixture.Service);
    var nonAdminHttp = new DefaultHttpContext { User = nonAdminPrincipal };
    nonAdminHttp.Items[AccountAuthentication.ResolvedAccountItem] = nonAdmin;
    var nonAdminContext = new AuthorizationHandlerContext([requirement], nonAdminPrincipal, nonAdminHttp);
    await handler.HandleAsync(nonAdminContext);
    Assert(!nonAdminContext.HasSucceeded,
        "The Admin policy authorized an authenticated account without the server-side role.");

    var adminHttp = new DefaultHttpContext { User = adminPrincipal };
    adminHttp.Items[AccountAuthentication.ResolvedAccountItem] = admin;
    var adminContext = new AuthorizationHandlerContext([requirement], adminPrincipal, adminHttp);
    await handler.HandleAsync(adminContext);
    Assert(adminContext.HasSucceeded,
        "The Admin policy rejected an authenticated account with the server-side role.");
}

static async Task TestClassifierClientContractAsync()
{
    string? postedJson = null;
    var handler = new StubHttpMessageHandler(request =>
    {
        Assert(request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/classify",
            "Classifier client did not POST to the isolated classify endpoint.");
        Assert(request.Content?.Headers.ContentLength is > 0 &&
               request.Headers.TransferEncodingChunked is not true,
            "Classifier client did not send a bounded, length-delimited JSON request.");
        postedJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return """
            {"received":true,"jobId":"R180395","title":"Senior Software Developer",
             "descriptionLength":18,"serviceVersion":"0.1.0","protocolVersion":"1",
             "revision":"test-sha","gpuAvailable":false,"deviceCount":0,
             "deviceName":null,"vramTotalMiB":null,"vramUsedMiB":null,"driverVersion":null}
            """;
    });
    var client = new ClassifierClient(
        new HttpClient(handler) { BaseAddress = new Uri("http://job-classifier:8081/") },
        NullLogger<ClassifierClient>.Instance);
    var result = await client.ClassifyAsync(new(
        "R180395", "Senior Software Developer", "exactly 18 chars!!"));
    var posted = JsonSerializer.Deserialize<ClassifierRequest>(
        postedJson!, ClassifierClient.JsonOptions)!;
    Assert(result.Available && result.Response is
           { JobId: "R180395", Title: "Senior Software Developer", DescriptionLength: 18 } &&
           posted == new ClassifierRequest(
               "R180395", "Senior Software Developer", "exactly 18 chars!!"),
        $"Classifier client did not preserve or validate the exact request/response contract: " +
        $"available={result.Available}, error={result.Error}, response={JsonSerializer.Serialize(result.Response)}, " +
        $"posted={postedJson}");
}

static async Task TestClassifierUnavailableAsync()
{
    var client = new ClassifierClient(
        new HttpClient(new ThrowingHttpMessageHandler())
        {
            BaseAddress = new Uri("http://job-classifier:8081/")
        },
        NullLogger<ClassifierClient>.Instance);
    var result = await client.ClassifyAsync(new("R180395", "Title", "Description"));
    Assert(!result.Available && result.Response is null &&
           result.Error == "Classifier service is unavailable.",
        "An unavailable experimental classifier was not handled as an isolated diagnostic failure.");
}

static async Task TestLlmClassifierContractAsync()
{
    var predictions = LlmEvaluationService.Concepts.Select(item => new {
        conceptId = item.ConceptId, matched = true });
    var payload = JsonSerializer.Serialize(new {
        received = true, jobId = "fixture", title = "Backend Engineer", descriptionLength = 11,
        serviceVersion = "0.4.0", protocolVersion = "4", revision = new string('a', 40),
        gpuAvailable = true, deviceCount = 1, deviceName = "NVIDIA GeForce GTX 1070",
        vramTotalMiB = (int?)null, vramUsedMiB = 3000, driverVersion = (string?)null,
        modelType = "generative-llm", modelId = "Qwen/Qwen3-4B-Instruct-2507",
        modelTag = "qwen3:4b-instruct-2507-q4_K_M",
        modelDigest = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0",
        quantization = "Q4_K_M", ollamaVersion = "0.33.2", device = "cuda:0",
        promptVersion = "phase3-zero-shot-v1", promptHash = new string('c', 64),
        temperature = 0, seed = 42, contextLength = 8192, maxOutputTokens = 384,
        totalDurationNanoseconds = 1_000_000_000L, loadDurationNanoseconds = 0L,
        promptTokenCount = 500, outputTokenCount = 50, tokensPerSecond = 20.0,
        inferenceMilliseconds = 1000.0, malformedOutputCount = 0, predictions
    }, ClassifierClient.JsonOptions);
    var client = new ClassifierClient(new HttpClient(new StubHttpMessageHandler(request => {
        Assert(request.RequestUri?.AbsolutePath == "/classify-llm",
            "LLM request used the wrong isolated endpoint.");
        Assert(request.Content?.Headers.ContentLength is > 0 &&
               request.Headers.TransferEncodingChunked is not true,
            "LLM request was not bounded and length-delimited.");
        return payload;
    })) { BaseAddress = new Uri("http://job-classifier:8081/") }, NullLogger<ClassifierClient>.Instance);
    var result = await client.ClassifyLlmAsync(new("fixture", "Backend Engineer", "Build APIs."));
    Assert(result.Available && result.Response is { Device: "cuda:0", Predictions.Count: 8,
               ContextLength: 8192, ModelType: "generative-llm" },
        "A valid pinned-model GPU response did not pass the JSM contract.");
}

static Task TestLlmFixtureMetricsAsync()
{
    var environment = new TestHostEnvironment(AppContext.BaseDirectory);
    var catalog = new JobConceptCatalog(environment);
    var detector = new JobConceptDetector(catalog);
    var evaluation = new DetectorEvaluationService(environment, catalog, detector);
    var cases = evaluation.BuildLlmCases();
    Assert(cases.Count == 40 && cases.Sum(item => item.Labels.Count) == 320,
        "LLM evaluation did not select the expected 40 fixtures / 320 independent labels.");
    var predictions = cases.Select(item => new LlmEvaluationService.Prediction(item,
        new LlmClassifierResponse(true, item.FixtureId, item.Title, item.Description.Length,
            "0.4.0", "4", new string('a', 40), true, 1, "NVIDIA GeForce GTX 1070",
            null, 3000, null, "generative-llm", "model", "tag",
            "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0",
            "Q4_K_M", "0.33.2", "cuda:0", "phase3-zero-shot-v1", new string('c', 64),
            0, 42, 8192, 384, 1000, 0, 500, 50, 20, 1000, 0,
            item.Labels.Select(label => new LlmPrediction(label.Key, label.Value)).ToArray()), 1010)).ToArray();
    var metrics = LlmEvaluationService.Calculate(predictions);
    Assert(metrics.Count == 8 && metrics.All(item => item.F1 == 1),
        "LLM metrics did not preserve deterministic expected labels.");
    var regexReport = evaluation.Evaluate(new string('a', 40));
    var regexSelected = regexReport.Concepts.Where(item => LlmEvaluationService.Concepts.Any(
        concept => concept.ConceptId == item.ConceptId)).ToArray();
    var comparisons = LlmEvaluationService.Compare(regexSelected, metrics);
    Assert(comparisons.Count == 8 && comparisons.All(item => item.LlmF1 == 1) &&
           comparisons.All(item => item.F1Delta is >= 0),
        "The per-concept regex/LLM comparison did not preserve all metrics and F1 deltas.");
    return Task.CompletedTask;
}

static async Task TestAdminBootstrapLifecycleAsync()
{
    var directory = TestDirectory("admin-bootstrap");
    var workspaceDirectory = TestDirectory("admin-workspace-reset");
    var codePath = Path.Combine(directory, "admin-bootstrap-code");
    try
    {
        var fixture = TestAccountService();
        var account = (await fixture.Service.CreateAsync(
            WorkspaceIdentity.Create(), "first-admin@example.test", "first admin passphrase",
            new Uri("https://example.test/"))).Account!;
        var hosting = new HostingConfiguration(
            ApplicationHostingMode.Container, null, null, "/var/lib/jsm/dataprotection", codePath);
        var bootstrap = new AdminBootstrapService(fixture.Service, hosting, fixture.Time);
        await bootstrap.InitializeAsync();

        Assert(await bootstrap.IsAvailableAsync() && File.Exists(codePath),
            "An enabled zero-admin instance did not create its bootstrap file.");
        var lines = await File.ReadAllLinesAsync(codePath);
        Assert(lines.Length == 2 && lines[0].Length == AdminBootstrapService.CodeLength &&
               lines[0].All(AdminBootstrapService.CodeAlphabet.Contains),
            "The bootstrap file did not contain one unambiguous eight-character code.");
        var code = lines[0];
        Assert(DateTimeOffset.TryParse(lines[1], out var expiry) &&
               expiry - fixture.Time.GetUtcNow() == AdminBootstrapService.CodeLifetime,
            "The bootstrap code did not receive its fifteen-minute expiration.");
        Assert(bootstrap.ActiveCodeHash is { Length: 32 } &&
               !bootstrap.ActiveCodeHash.SequenceEqual(System.Text.Encoding.ASCII.GetBytes(code)),
            "Bootstrap validation material was not a SHA-256 hash.");
        if (!OperatingSystem.IsWindows())
        {
            Assert(File.GetUnixFileMode(codePath) ==
                   (UnixFileMode.UserRead | UnixFileMode.UserWrite),
                "The bootstrap file was not restricted to mode 0600.");
        }
        var registryJson = await fixture.Store.MutateAsync<string>(document =>
            new(false, JsonSerializer.Serialize(document)));
        Assert(!registryJson.Contains(code, StringComparison.Ordinal) &&
               !registryJson.Contains("adminBootstrap", StringComparison.OrdinalIgnoreCase),
            "Plaintext bootstrap material leaked into the account registry.");

        bootstrap = new AdminBootstrapService(fixture.Service, hosting, fixture.Time);
        await bootstrap.InitializeAsync();
        Assert((await File.ReadAllLinesAsync(codePath))[0] == code &&
               bootstrap.ActiveCodeHash is { Length: 32 },
            "Application restart did not preserve the still-valid one-time bootstrap code.");

        var wrongCode = (code[0] == '2' ? '3' : '2') + code[1..];
        Assert(!(await bootstrap.ClaimAsync(account.AccountId, wrongCode)).Succeeded &&
               await fixture.Service.GetAdminCountAsync() == 0,
            "An incorrect bootstrap code granted Admin.");

        fixture.Time.Advance(TimeSpan.FromMinutes(16));
        Assert(!(await bootstrap.ClaimAsync(account.AccountId, code)).Succeeded &&
               await fixture.Service.GetAdminCountAsync() == 0,
            "An expired bootstrap code granted Admin.");
        var replacementCode = (await File.ReadAllLinesAsync(codePath))[0];
        var claimed = await bootstrap.ClaimAsync(account.AccountId, replacementCode);
        Assert(claimed.Succeeded && AccountRoles.IsAdmin(claimed.Account) &&
               await fixture.Service.GetAdminCountAsync() == 1,
            "The valid replacement code did not grant the first Admin role.");
        Assert(!File.Exists(codePath) && !(await bootstrap.ClaimAsync(account.AccountId, replacementCode)).Succeeded,
            "A successful bootstrap left a reusable plaintext code or token.");

        await fixture.Service.RequestPasswordResetAsync(
            account.Email, new Uri("https://example.test/"));
        Assert((await fixture.Service.ResetPasswordAsync(
                   fixture.Email.ResetToken, "reset administrator passphrase")).Succeeded,
            "The Admin account password reset failed.");
        Assert((await fixture.Service.ChangePasswordAsync(
                   account.AccountId, "reset administrator passphrase", "changed administrator passphrase")).Succeeded,
            "The Admin account password change failed.");

        var workspaceStore = new FileWorkspaceDataStore(
            workspaceDirectory, NullLogger<FileWorkspaceDataStore>.Instance);
        await workspaceStore.WriteJsonAsync(WorkspaceDataFile.Settings, ViewerSettings.Default);
        await workspaceStore.DeleteAllAsync();
        Assert(AccountRoles.IsAdmin(await fixture.Service.GetByIdAsync(account.AccountId)),
            "Password operations or workspace reset removed the account-level Admin role.");

        await bootstrap.InitializeAsync();
        Assert(!File.Exists(codePath) && !await bootstrap.IsAvailableAsync(),
            "An instance with an Admin generated another bootstrap code.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        if (Directory.Exists(workspaceDirectory)) Directory.Delete(workspaceDirectory, recursive: true);
    }
}

static async Task TestAdminBootstrapConcurrencyAsync()
{
    var directory = TestDirectory("admin-bootstrap-concurrency");
    var codePath = Path.Combine(directory, "admin-bootstrap-code");
    try
    {
        var fixture = TestAccountService();
        var first = (await fixture.Service.CreateAsync(
            WorkspaceIdentity.Create(), "first@example.test", "first claimant passphrase",
            new Uri("https://example.test/"))).Account!;
        var second = (await fixture.Service.CreateAsync(
            WorkspaceIdentity.Create(), "second@example.test", "second claimant passphrase",
            new Uri("https://example.test/"))).Account!;
        var bootstrap = new AdminBootstrapService(
            fixture.Service,
            new HostingConfiguration(
                ApplicationHostingMode.Container, null, null,
                "/var/lib/jsm/dataprotection", codePath),
            fixture.Time);
        await bootstrap.InitializeAsync();
        var code = (await File.ReadAllLinesAsync(codePath))[0];
        var results = await Task.WhenAll(
            bootstrap.ClaimAsync(first.AccountId, code),
            bootstrap.ClaimAsync(second.AccountId, code));
        Assert(results.Count(result => result.Succeeded) == 1 &&
               await fixture.Service.GetAdminCountAsync() == 1 &&
               !File.Exists(codePath),
            "Concurrent claims did not atomically grant exactly one first Admin.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestAdminBootstrapConfigurationAsync()
{
    var disabledFixture = TestAccountService();
    await disabledFixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "disabled@example.test", "disabled bootstrap passphrase",
        new Uri("https://example.test/"));
    var disabled = new AdminBootstrapService(
        disabledFixture.Service,
        new HostingConfiguration(ApplicationHostingMode.Local, null, null),
        disabledFixture.Time);
    await disabled.InitializeAsync();
    Assert(!await disabled.IsAvailableAsync(),
        "Bootstrap was available without an explicit server-side path.");

    var emptyDirectory = TestDirectory("admin-bootstrap-no-accounts");
    var emptyPath = Path.Combine(emptyDirectory, "admin-bootstrap-code");
    try
    {
        var emptyFixture = TestAccountService();
        var empty = new AdminBootstrapService(
            emptyFixture.Service,
            new HostingConfiguration(
                ApplicationHostingMode.Container, null, null,
                "/var/lib/jsm/dataprotection", emptyPath),
            emptyFixture.Time);
        await empty.InitializeAsync();
        Assert(!File.Exists(emptyPath) && !await empty.IsAvailableAsync(),
            "A bootstrap code was generated before any authenticated account existed.");
    }
    finally
    {
        if (Directory.Exists(emptyDirectory)) Directory.Delete(emptyDirectory, recursive: true);
    }

    AssertThrows<InvalidOperationException>(() => HostingConfiguration.FromConfiguration(
        Configuration(new Dictionary<string, string?>
        {
            [HostingConfiguration.ModeSetting] = "Azure",
            [HostingConfiguration.StorageAccountSetting] = "workdayjobmanagerstore",
            [HostingConfiguration.StorageContainerSetting] = "userdata",
            [HostingConfiguration.AdminBootstrapPathSetting] = "/tmp/unsafe-bootstrap"
        })));
}

static ClaimsPrincipal TestPrincipal(AccountRecord account) => new(new ClaimsIdentity(
    [new Claim(ClaimTypes.NameIdentifier, account.AccountId)], "test"));

static async Task TestAccountPasswordAndEmailAsync()
{
    var fixture = TestAccountService();
    var workspaceId = WorkspaceIdentity.Create();
    const string password = "a long memorable passphrase";
    var created = await fixture.Service.CreateAsync(
        workspaceId, "Person@Example.com", password, new Uri("https://example.test/"));
    Assert(created.Succeeded && created.Account is not null,
        "A valid account could not be created.");
    Assert(created.Account!.PasswordHash != password && !created.Account.PasswordHash.Contains(password),
        "The original password appeared in the persisted credential field.");
    Assert(await fixture.Service.AuthenticateAsync("person@example.COM", password) is not null,
        "The platform password hash did not verify the correct password.");
    Assert(await fixture.Service.AuthenticateAsync("person@example.com", "incorrect password") is null,
        "An invalid password authenticated successfully.");
    var duplicate = await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), " person@example.com ", password, new Uri("https://example.test/"));
    Assert(!duplicate.Succeeded, "Normalized duplicate email was accepted.");
    var registryJson = await fixture.Store.MutateAsync<string>(document =>
        new(false, JsonSerializer.Serialize(document)));
    Assert(!registryJson.Contains(password, StringComparison.Ordinal),
        "Plaintext password leaked into account persistence.");
    Assert(!registryJson.Contains(fixture.Email.VerificationToken!, StringComparison.Ordinal),
        "A plaintext verification token leaked into account persistence.");
    Assert(AccountService.ValidatePassword("short") is not null &&
           AccountService.ValidatePassword(password) is null,
        "The length-focused password policy is incorrect.");
    fixture.Email.FailDelivery = true;
    var deliveryFailure = await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "delivery@example.com", "delivery failure passphrase",
        new Uri("https://example.test/"));
    Assert(deliveryFailure.Succeeded,
        "Email-provider failure left a completed account claim inaccessible.");
}

static async Task TestAccountWorkspaceClaimAsync()
{
    var directory = TestDirectory("account-claim-state");
    try
    {
        var workspaceStore = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var settings = ViewerSettings.Default with
        {
            IncludeKeywords = ["linux", "cloud"],
            ThemeMode = "dracula",
            JobFit = new JobFitConfiguration(true,
                [new JobFitSignalPreference("remote-work", "ideal")])
        };
        var history = new JobHistoryDocument(3, new Dictionary<string, JobHistoryEntry>
        {
            ["leidos:claim-test"] = new(
                "REQ-CLAIM", "/jobs/claim-test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                true, JobWorkflowStates.Saved, DateTimeOffset.UtcNow, CompanyId: "leidos")
        });
        await workspaceStore.WriteJsonAsync(WorkspaceDataFile.Settings, settings);
        await workspaceStore.WriteJsonAsync(WorkspaceDataFile.JobHistory, history);
        var beforeSettings = JsonSerializer.Serialize(
            await workspaceStore.ReadJsonAsync<ViewerSettings>(WorkspaceDataFile.Settings));
        var beforeHistory = JsonSerializer.Serialize(
            await workspaceStore.ReadJsonAsync<JobHistoryDocument>(WorkspaceDataFile.JobHistory));

        var fixture = TestAccountService();
        var workspaceId = WorkspaceIdentity.Create();
        var created = await fixture.Service.CreateAsync(
            workspaceId, "claim@example.com", "claim workspace safely", new Uri("https://example.test/"));
        Assert(created.Succeeded && await fixture.Service.GetWorkspaceOwnerAsync(workspaceId) ==
            created.Account!.AccountId, "The anonymous workspace was not linked to its account.");
        Assert(beforeSettings == JsonSerializer.Serialize(
                   await workspaceStore.ReadJsonAsync<ViewerSettings>(WorkspaceDataFile.Settings)) &&
               beforeHistory == JsonSerializer.Serialize(
                   await workspaceStore.ReadJsonAsync<JobHistoryDocument>(WorkspaceDataFile.JobHistory)),
            "Claiming changed settings, Job Fit, qualifications, or curated history.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestFailedAccountClaimAsync()
{
    var fixture = TestAccountService();
    var workspaceId = WorkspaceIdentity.Create();
    fixture.Store.FailNextWrite = true;
    await AssertThrowsAsync<WorkspaceStorageException>(() => fixture.Service.CreateAsync(
        workspaceId, "retry@example.com", "retryable account claim", new Uri("https://example.test/")));
    Assert(await fixture.Service.GetWorkspaceOwnerAsync(workspaceId) is null &&
           await fixture.Service.AuthenticateAsync("retry@example.com", "retryable account claim") is null,
        "A failed atomic claim left an owner or credential behind.");
    var retry = await fixture.Service.CreateAsync(
        workspaceId, "retry@example.com", "retryable account claim", new Uri("https://example.test/"));
    Assert(retry.Succeeded, "A failed claim could not be retried safely.");
}

static async Task TestAccountAuthorizationAsync()
{
    var fixture = TestAccountService();
    var workspaceId = WorkspaceIdentity.Create();
    var first = await fixture.Service.CreateAsync(
        workspaceId, "owner@example.com", "workspace owner passphrase", new Uri("https://example.test/"));
    var second = await fixture.Service.CreateAsync(
        workspaceId, "other@example.com", "another secure passphrase", new Uri("https://example.test/"));
    Assert(first.Succeeded && !second.Succeeded,
        "A second account was allowed to claim an owned workspace.");

    var protection = new EphemeralDataProtectionProvider();
    var protectedWorkspace = protection.CreateProtector(WorkspaceIdentity.ProtectorPurpose).Protect(workspaceId);
    var middleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        new HostingConfiguration(ApplicationHostingMode.Azure, "workdayjobmanagerstore", "userdata"),
        protection,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);
    var anonymousContext = new DefaultHttpContext();
    anonymousContext.Request.Path = "/api/settings";
    anonymousContext.Request.Headers.Cookie = $"{WorkspaceIdentity.CookieName}={protectedWorkspace}";
    var anonymousWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(anonymousContext, anonymousWorkspace, fixture.Service);
    Assert(anonymousWorkspace.WorkspaceId != workspaceId,
        "Possession of an old anonymous cookie still opened a claimed workspace.");

    var ownerContext = new DefaultHttpContext();
    ownerContext.Request.Path = "/api/settings";
    ownerContext.User = new System.Security.Claims.ClaimsPrincipal(
        new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, first.Account!.AccountId)], "test"));
    var ownerWorkspace = new WorkspaceContext();
    await middleware.InvokeAsync(ownerContext, ownerWorkspace, fixture.Service);
    Assert(ownerWorkspace.WorkspaceId == workspaceId,
        "The authenticated owner did not resolve to the claimed workspace.");

    var localFixture = TestAccountService();
    await localFixture.Service.CreateAsync(
        WorkspaceContext.LocalWorkspaceId, "local@example.com", "local workspace passphrase",
        new Uri("http://127.0.0.1:54321/"));
    var localMiddleware = new WorkspaceIdentityMiddleware(
        _ => Task.CompletedTask,
        new HostingConfiguration(ApplicationHostingMode.Local, null, null),
        protection,
        NullLogger<WorkspaceIdentityMiddleware>.Instance);
    var localAnonymousContext = new DefaultHttpContext();
    localAnonymousContext.Request.Path = "/api/settings";
    var localAnonymousWorkspace = new WorkspaceContext();
    await localMiddleware.InvokeAsync(
        localAnonymousContext, localAnonymousWorkspace, localFixture.Service);
    Assert(WorkspaceIdentity.IsValid(localAnonymousWorkspace.WorkspaceId),
        "Local anonymous access was not rotated away from a claimed local workspace.");
}

static async Task TestPasswordResetAsync()
{
    var fixture = TestAccountService();
    var created = await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "reset@example.com", "original secure passphrase",
        new Uri("https://example.test/"));
    var originalVersion = created.Account!.SecurityVersion;
    await fixture.Service.RequestPasswordResetAsync(
        "reset@example.com", new Uri("https://example.test/"));
    var expiredToken = fixture.Email.ResetToken!;
    fixture.Time.Advance(TimeSpan.FromMinutes(61));
    Assert(!(await fixture.Service.ResetPasswordAsync(expiredToken, "replacement passphrase one")).Succeeded,
        "An expired reset token was accepted.");

    await fixture.Service.RequestPasswordResetAsync(
        "reset@example.com", new Uri("https://example.test/"));
    var token = fixture.Email.ResetToken!;
    var reset = await fixture.Service.ResetPasswordAsync(token, "replacement passphrase two");
    Assert(reset.Succeeded && reset.Account!.SecurityVersion == originalVersion + 1,
        "Password reset did not invalidate existing session versions.");
    Assert(!(await fixture.Service.ResetPasswordAsync(token, "replacement passphrase three")).Succeeded,
        "A single-use reset token was accepted twice.");
    Assert(await fixture.Service.AuthenticateAsync("reset@example.com", "original secure passphrase") is null &&
           await fixture.Service.AuthenticateAsync("reset@example.com", "replacement passphrase two") is not null,
        "Reset did not replace the password hash correctly.");
}

static async Task TestEmailVerificationAsync()
{
    var fixture = TestAccountService();
    var created = await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "verify@example.com", "verification passphrase",
        new Uri("https://example.test/"));
    var token = fixture.Email.VerificationToken!;
    var verified = await fixture.Service.VerifyEmailAsync(token);
    Assert(verified.Succeeded && verified.Account!.EmailVerified,
        "A valid email-verification token did not verify the account.");
    Assert(!(await fixture.Service.VerifyEmailAsync(token)).Succeeded,
        "A verification token was reusable.");
    Assert(created.Account!.Email == "verify@example.com", "Account email changed during verification.");
}

static async Task TestPasswordResetEnumerationAsync()
{
    var fixture = TestAccountService();
    await fixture.Service.CreateAsync(
        WorkspaceIdentity.Create(), "known@example.com", "enumeration test password",
        new Uri("https://example.test/"));
    var before = fixture.Email.ResetMessages;
    await fixture.Service.RequestPasswordResetAsync("missing@example.com", new Uri("https://example.test/"));
    Assert(fixture.Email.ResetMessages == before,
        "A reset message was sent for an unknown account.");
    await fixture.Service.RequestPasswordResetAsync("known@example.com", new Uri("https://example.test/"));
    Assert(fixture.Email.ResetMessages == before + 1,
        "A known account did not receive its reset message.");
    fixture.Email.FailDelivery = true;
    await fixture.Service.RequestPasswordResetAsync("known@example.com", new Uri("https://example.test/"));
    await fixture.Service.RequestPasswordResetAsync("missing@example.com", new Uri("https://example.test/"));
}

static Task TestAccountPersistenceAsync()
{
    Assert(AccountPersistence.Lifetime(AccountPersistence.Session) is null,
        "Session-only login unexpectedly created a persistent lifetime.");
    Assert(AccountPersistence.Lifetime(AccountPersistence.OneDay) == TimeSpan.FromDays(1) &&
           AccountPersistence.Lifetime(AccountPersistence.SevenDays) == TimeSpan.FromDays(7) &&
           AccountPersistence.Lifetime(AccountPersistence.FourteenDays) == TimeSpan.FromDays(14) &&
           AccountPersistence.Lifetime(AccountPersistence.ThirtyDays) == TimeSpan.FromDays(30) &&
           AccountPersistence.Lifetime(AccountPersistence.KeepSignedIn) == TimeSpan.FromDays(180),
        "A persisted login duration is incorrect or unbounded.");
    Assert(AccountPersistence.Normalize("unexpected") == AccountPersistence.Session,
        "Unknown persistence did not fail safely to session-only.");
    return Task.CompletedTask;
}

static async Task TestAccountSurvivesWorkspaceResetAsync()
{
    var directory = TestDirectory("account-reset-separation");
    try
    {
        var workspaceStore = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        await workspaceStore.WriteJsonAsync(WorkspaceDataFile.Settings, ViewerSettings.Default);
        var fixture = TestAccountService();
        var account = await fixture.Service.CreateAsync(
            WorkspaceIdentity.Create(), "survives@example.com", "account survives reset",
            new Uri("https://example.test/"));
        await workspaceStore.DeleteAllAsync();
        Assert(await fixture.Service.GetByIdAsync(account.Account!.AccountId) is not null,
            "Resetting workspace content deleted the separate account record.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestAccountSecretsExcludedFromExportAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var json = JsonSerializer.Serialize(portable.Export(ViewerSettings.Default, JobHistoryDocument.Empty));
    foreach (var forbidden in new[]
    {
        "passwordHash", "resetToken", "verificationToken", "sessionToken",
        AccountAuthentication.CookieName, "normalizedEmail", "securityVersion",
        "roles", "administratorBootstrap", "Admin"
    })
    {
        Assert(!json.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
            $"Portable export contained authentication field '{forbidden}'.");
    }
    return Task.CompletedTask;
}

static async Task TestJobFitSettingsAsync()
{
    var directory = TestDirectory("job-fit-settings");
    try
    {
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var concepts = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var store = new FileWorkspaceDataStore(
        directory, NullLogger<FileWorkspaceDataStore>.Instance);
    var state = new AppStateStore(
        NullLogger<AppStateStore>.Instance, companies, store, concepts);

    var missing = JsonSerializer.Deserialize<ViewerSettings>(
        "{}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var legacy = state.NormalizeSettings(missing!);
    Assert(legacy.JobFit is
               { Enabled: false, TravelTolerance: null, PreferredWorkLocation: null } &&
           legacy.JobFit.Signals.Count == 0,
        "An existing workspace without Job Fit fields did not behave as disabled.");

    var normalized = state.NormalizeSettings(ViewerSettings.Default with
    {
        JobFit = new JobFitConfiguration(true,
        [
            new("technical.machine-learning", JobFitPreferenceLevels.StrongPositive),
            new("work.onsite", JobFitPreferenceLevels.StrongNegative),
            new("work.deployment", JobFitPreferenceLevels.Negative),
            new("work.remote", JobFitPreferenceLevels.Neutral),
            new("user.arbitrary-concept", JobFitPreferenceLevels.HardConflict),
            new("work.relocation", "unsupported")
        ], 2, null, [JobFitGroupHardConflicts.SoftwareDevelopment,
            JobFitGroupHardConflicts.AiData])
    });
    Assert(normalized.JobFit is
               { Enabled: true, TravelTolerance: 2, PreferredWorkLocation: 0 } &&
           normalized.JobFit.Signals.Count == 2 &&
           normalized.JobFit.Signals.Any(signal =>
               signal.ConceptId == "technical.machine-learning" &&
               signal.Preference == JobFitPreferenceLevels.Ideal) &&
           normalized.JobFit.Signals.Any(signal =>
               signal.ConceptId == "work.deployment" &&
               signal.Preference == JobFitPreferenceLevels.Negative) &&
           normalized.JobFit.Signals.All(signal => signal.ConceptId != "work.remote") &&
           normalized.JobFit.GroupHardConflicts?.SequenceEqual(
               [JobFitGroupHardConflicts.AiData,
                JobFitGroupHardConflicts.SoftwareDevelopment]) == true,
        "Sparse normalization did not omit Neutral, migrate legacy preference names, or reject invalid signals.");
    await state.SaveSettingsAsync(normalized);
    var reloaded = await state.LoadSettingsAsync();
    Assert(reloaded.JobFit is
               { Enabled: true, TravelTolerance: 2, PreferredWorkLocation: 0 } &&
           reloaded.JobFit.Signals.SequenceEqual(normalized.JobFit!.Signals) &&
           reloaded.JobFit.GroupHardConflicts?.SequenceEqual(
               normalized.JobFit.GroupHardConflicts ?? []) == true,
        "Enabling Job Fit and selecting canonical concepts did not persist.");

    var cleared = state.NormalizeSettings(reloaded with
    {
        JobFit = new JobFitConfiguration(true,
        [new("technical.machine-learning", JobFitPreferenceLevels.Neutral)])
    });
    await state.SaveSettingsAsync(cleared);
    var clearedReloaded = await state.LoadSettingsAsync();
    Assert(clearedReloaded.JobFit is { Enabled: true, TravelTolerance: null,
               PreferredWorkLocation: null } &&
           clearedReloaded.JobFit.Signals.Count == 1 &&
           clearedReloaded.JobFit.Signals.Single().Preference == JobFitPreferenceLevels.Neutral,
        "Explicit Neutral did not persist as a configured zero-impact preference.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestTravelToleranceMigrationAsync()
{
    var concepts = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var currentWorkspace = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
    [
        new("work.travel.frequent", JobFitPreferenceLevels.HardConflict),
        new("work.travel.substantial", JobFitPreferenceLevels.HardConflict),
        new("work.deployment", JobFitPreferenceLevels.HardConflict)
    ]), concepts);
    Assert(currentWorkspace.TravelTolerance == 3 &&
           currentWorkspace.Signals.All(signal =>
               !TravelTolerance.IsLegacyConcept(signal.ConceptId)) &&
           currentWorkspace.Signals.Any(signal => signal.ConceptId == "work.deployment"),
        "The current workspace's legacy frequent/substantial hard conflicts did not migrate to level 3 while retaining unrelated preferences.");

    var negative = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
        [new("work.travel.moderate", JobFitPreferenceLevels.Negative)]), concepts);
    var permissive = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
        [new("work.travel.frequent", JobFitPreferenceLevels.Positive)]), concepts);
    var explicitValue = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
        [new("work.travel.substantial", JobFitPreferenceLevels.HardConflict)], 2), concepts);
    var invalidValue = JobFitConfiguration.Normalize(new JobFitConfiguration(true, [], 7), concepts);
    Assert(negative.TravelTolerance == 3 && permissive.TravelTolerance == 5 &&
           explicitValue.TravelTolerance == 2 && invalidValue.TravelTolerance is null,
        "Legacy negative/positive migration, explicit-value precedence, or invalid-value fallback was not deterministic.");
    return Task.CompletedTask;
}

static Task TestWorkLocationMigrationAsync()
{
    var concepts = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var currentWorkspace = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
    [
        new("work.hybrid", JobFitPreferenceLevels.Negative),
        new("work.onsite", JobFitPreferenceLevels.Negative),
        new("work.remote", JobFitPreferenceLevels.Ideal),
        new("work.remote.full", JobFitPreferenceLevels.Ideal),
        new("work.deployment", JobFitPreferenceLevels.HardConflict)
    ]), concepts);
    Assert(currentWorkspace.PreferredWorkLocation == 0 &&
           currentWorkspace.Signals.All(signal =>
               !WorkLocationPreference.IsLegacyConcept(signal.ConceptId)) &&
           currentWorkspace.Signals.Any(signal => signal.ConceptId == "work.deployment"),
        "The current workspace's full-remote ideal did not migrate to level 0 without double-counting generic Remote Work.");

    var centered = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
    [
        new("work.remote.full", JobFitPreferenceLevels.Ideal),
        new("work.hybrid", JobFitPreferenceLevels.Ideal)
    ]), concepts);
    var negativeOnly = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
        [new("work.onsite", JobFitPreferenceLevels.Negative)]), concepts);
    var explicitValue = JobFitConfiguration.Normalize(new JobFitConfiguration(true,
        [new("work.remote.full", JobFitPreferenceLevels.Ideal)], 4, 5), concepts);
    var invalidValue = JobFitConfiguration.Normalize(new JobFitConfiguration(true, [], 4, 6), concepts);
    Assert(centered.PreferredWorkLocation == 2 && negativeOnly.PreferredWorkLocation == 0 &&
           explicitValue.PreferredWorkLocation == 5 &&
           invalidValue.PreferredWorkLocation is null,
        "Legacy center/negative migration, explicit-value precedence, or invalid-value fallback was not deterministic.");
    return Task.CompletedTask;
}

static Task TestJobConceptDetectionAsync()
{
    var concepts = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var detector = new JobConceptDetector(concepts);
    var remote = new RemoteWorkDetector().Analyze(
        "Machine Learning Engineer", "Remote / Teleworker US", [],
        "<p>This is a fully remote role using Linux and CI/CD. Frequent travel is required.</p>");
    var extended = new ExtendedLocationRequirementDetector().Analyze(
        "Machine Learning Engineer", "Remote / Teleworker US", [],
        "<p>This is a fully remote role using Linux and CI/CD. Frequent travel is required.</p>");
    var detected = detector.Analyze(
        "Machine Learning Engineer",
        "Remote / Teleworker US",
        [],
        "<p>This is a fully remote role using Linux and CI/CD. Frequent travel is required.</p>",
        remote,
        extended);
    var ids = detected.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal);
    Assert(ids.IsSupersetOf([
               "work.remote",
               "work.remote.full",
               "work.travel.frequent",
               "role.ai-ml-engineering",
               "technical.machine-learning",
               "technical.linux",
               "technical.cicd"
           ]) && detected.All(item => !string.IsNullOrWhiteSpace(item.Evidence)),
        "Canonical corpus concepts or their actual evidence were not detected consistently.");

    IReadOnlySet<string> DetectConcepts(string title, string text)
    {
        var html = $"<p>{text}</p>";
        return detector.Analyze(
                title, "United States", [], html,
                new RemoteWorkDetector().Analyze(title, "United States", [], html),
                new ExtendedLocationRequirementDetector().Analyze(
                    title, "United States", [], html))
            .Select(item => item.ConceptId)
            .ToHashSet(StringComparer.Ordinal);
    }

    var generalizedSoftware = DetectConcepts(
        "Senior AI Platform Engineer",
        "Design and implement server-side microservices and REST APIs. Build AI/ML-enabled tools, " +
        "write Python and Bash scripts to automate deployments, and administer Linux hosts.");
    Assert(generalizedSoftware.IsSupersetOf([
               "role.software-engineering", "role.ai-ml-engineering",
               "technical.software-development", "technical.backend-development",
               "technical.api-development", "technical.automation-scripting",
               "technical.artificial-intelligence", "technical.machine-learning",
               "technical.linux-administration"
           ]),
        "Generalized software, backend, API, automation, AI/ML, and Linux-administration phrasing regressed. " +
        $"Detected: {string.Join(", ", generalizedSoftware.OrderBy(id => id, StringComparer.Ordinal))}");

    var oversightOnly = DetectConcepts(
        "Senior Test Program Manager",
        "Manages engineers and owns the budget. The program uses APIs and cloud-hosted software, " +
        "but this role does not develop software, write scripts, build backend services, or implement APIs.");
    Assert(!oversightOnly.Overlaps([
               "role.software-engineering", "role.cloud-engineering",
               "technical.software-development", "technical.backend-development",
               "technical.api-development", "technical.automation-scripting"
           ]),
        "Negated or subordinate technical work was misclassified as direct target-technical responsibility. " +
        $"Detected: {string.Join(", ", oversightOnly.OrderBy(id => id, StringComparer.Ordinal))}");

    var infrastructureDetected = detector.Analyze(
        "Data Center Infrastructure Engineer",
        "Customer data center",
        [],
        "<p>Hands-on work within a data center environment supporting physical infrastructure. " +
        "Perform rack and cable installation, Linux administration, Docker, cloud platforms, and CI/CD.</p>",
        new RemoteWorkDetector().Analyze(
            "Data Center Infrastructure Engineer", "Customer data center", [], ""),
        new ExtendedLocationRequirementDetector().Analyze(
            "Data Center Infrastructure Engineer", "Customer data center", [], ""));
    var infrastructureIds = infrastructureDetected
        .Select(item => item.ConceptId)
        .ToHashSet(StringComparer.Ordinal);
    Assert(infrastructureIds.IsSupersetOf([
               "role.infrastructure-engineering",
               "work.data-center",
               "work.physical-infrastructure",
               "responsibility.hands-on-implementation",
               "technical.linux-administration",
               "technical.containers",
               "technical.cabling-racking"
           ]),
        "Infrastructure-heavy jobs did not produce canonical role, environment, responsibility, and technical concepts. " +
        $"Detected: {string.Join(", ", infrastructureIds.OrderBy(id => id, StringComparer.Ordinal))}");

    var travelBands = new[]
    {
        ("This role requires occasional travel.", "work.travel.occasional"),
        ("This role requires up to 10% travel.", "work.travel.occasional"),
        ("This role requires 25% travel.", "work.travel.moderate"),
        ("This role requires travel 10%-40% depending on project needs.", "work.travel.moderate"),
        ("This role requires 50% travel.", "work.travel.substantial"),
        ("Travel as needed to customer sites.", "work.travel.occasional"),
        ("Travel typically lasting no more than one week.", "work.travel.occasional"),
        ("At most one short trip every 2-3 years.", "work.travel.occasional"),
        ("About one short trip every 12-18 months.", "work.travel.occasional")
    };
    foreach (var (description, expected) in travelBands)
    {
        var remoteTravel = new RemoteWorkDetector().Analyze(
            "Remote Engineer", "Remote, US", [], $"<p>{description}</p>");
        var travelIds = detector.Analyze(
            "Remote Engineer", "Remote, US", [], $"<p>{description}</p>", remoteTravel,
            new ExtendedLocationRequirementDetector().Analyze(
                "Remote Engineer", "Remote, US", [], $"<p>{description}</p>"))
            .Select(item => item.ConceptId)
            .ToHashSet(StringComparer.Ordinal);
        Assert(travelIds.Contains(expected),
            $"Travel band '{expected}' was not detected for: {description}");
        if (description.Contains("10%-40%", StringComparison.Ordinal))
        {
            Assert(!travelIds.Contains("work.travel.occasional"),
                "A 10%-40% range was also misclassified as occasional travel.");
        }
    }
    var travelNegative = detector.Analyze(
        "Travel Industry Analyst", "Remote, US", [],
        "<p>Twenty-five years of travel-industry experience is preferred.</p>",
        new RemoteWorkDetector().Analyze(
            "Travel Industry Analyst", "Remote, US", [],
            "<p>Twenty-five years of travel-industry experience is preferred.</p>"),
        new ExtendedLocationRequirementDetector().Analyze(
            "Travel Industry Analyst", "Remote, US", [],
            "<p>Twenty-five years of travel-industry experience is preferred.</p>"));
    Assert(travelNegative.All(item => !item.ConceptId.StartsWith("work.travel.", StringComparison.Ordinal)),
        "Travel-industry experience was misclassified as a current travel obligation.");

    var locationEvidenceCases = new[]
    {
        ("This role is mostly remote.", "work.remote", "mostly remote"),
        ("This role is remote with quarterly office visits.", "work.remote", "quarterly office visits"),
        ("This position is currently remote but will transition onsite.", "work.remote", "transition onsite"),
        ("This is a hybrid role with 2 days onsite per week.", "work.hybrid", "2 days onsite"),
        ("This role is mostly onsite with limited remote flexibility.", "work.onsite", "mostly onsite")
    };
    foreach (var (description, expectedId, expectedEvidence) in locationEvidenceCases)
    {
        var remoteLocation = new RemoteWorkDetector().Analyze(
            "Engineer", "Remote, US", [], $"<p>{description}</p>");
        var locationDetected = detector.Analyze(
            "Engineer", "Remote, US", [], $"<p>{description}</p>", remoteLocation,
            new ExtendedLocationRequirementDetector().Analyze(
                "Engineer", "Remote, US", [], $"<p>{description}</p>"));
        var match = locationDetected.Single(item => item.ConceptId == expectedId);
        Assert(match.Evidence.Contains(expectedEvidence, StringComparison.OrdinalIgnoreCase),
            $"Specific location evidence was lost for: {description}");
    }

    var titleCases = new[]
    {
        ("DDI Architect", "responsibility.architecture-heavy"),
        ("Technical Writer", "responsibility.documentation-heavy"),
        ("Airborne Sensor Operator", "work.aircraft-flight-line"),
        ("Technical Manager - Data Transmission", "role.management-heavy")
    };
    foreach (var (title, expected) in titleCases)
    {
        var titleIds = detector.Analyze(title, "Remote, US", [], "<p>General duties.</p>",
            new RemoteWorkDetector().Analyze(title, "Remote, US", [], "<p>General duties.</p>"),
            new ExtendedLocationRequirementDetector().Analyze(
                title, "Remote, US", [], "<p>General duties.</p>"))
            .Select(item => item.ConceptId)
            .ToHashSet(StringComparer.Ordinal);
        Assert(titleIds.Contains(expected),
            $"Title-aware concept '{expected}' was not detected for '{title}'.");
    }
    var titleContextNegative = detector.Analyze(
        "Software Engineer", "Remote, US", [],
        "<p>Collaborate with the architect, technical writer, airborne sensor operator, and manager.</p>",
        new RemoteWorkDetector().Analyze(
            "Software Engineer", "Remote, US", [],
            "<p>Collaborate with the architect, technical writer, airborne sensor operator, and manager.</p>"),
        new ExtendedLocationRequirementDetector().Analyze(
            "Software Engineer", "Remote, US", [],
            "<p>Collaborate with the architect, technical writer, airborne sensor operator, and manager.</p>"));
    Assert(titleContextNegative.All(item => item.ConceptId is not
            ("responsibility.architecture-heavy" or "responsibility.documentation-heavy" or
             "work.aircraft-flight-line" or "role.management-heavy")),
        "A title-only concept was inferred from a body reference to another role.");

    HashSet<string> DetectWorkType(string title, string description) => detector.Analyze(
        title, "United States", [], $"<p>{description}</p>",
        new RemoteWorkDetector().Analyze(title, "United States", [], $"<p>{description}</p>"),
        new ExtendedLocationRequirementDetector().Analyze(
            title, "United States", [], $"<p>{description}</p>"))
        .Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal);
    var workTypeCases = new[]
    {
        ("Fleet Technician/Auditor", "Inspect, maintain, troubleshoot, and repair fleet vehicles.",
            new[] { "role.mechanical-maintenance-repair", "role.physical-inspection-quality-control" }),
        ("Manufacturing Machinist", "Machine metal parts to blueprint specifications.",
            new[] { "role.fabrication-assembly-machining" }),
        ("Electrical Test Inspector", "Inspect electrical assemblies and verify hardware workmanship.",
            new[] { "role.physical-inspection-quality-control" }),
        ("Laboratory Technician", "Operate laboratory instrumentation and record physical measurements.",
            new[] { "role.lab-test-technician" }),
        ("Warehouse Associate", "Receive shipments, stock parts, pick inventory, and move pallets.",
            new[] { "role.warehouse-material-handling" }),
        ("Production Technician", "Operate manufacturing equipment and monitor a production line.",
            new[] { "role.manufacturing-production-operations" })
    };
    foreach (var (title, description, expected) in workTypeCases)
    {
        var workTypeIds = DetectWorkType(title, description);
        Assert(workTypeIds.IsSupersetOf(expected),
            $"Reusable work-type concepts were missed for '{title}': {string.Join(", ", workTypeIds)}");
    }
    var hardNegatives = new[]
    {
        ("Software Engineer, Automotive Platform", "Develop cloud software for automotive telemetry APIs."),
        ("Manufacturing Software Engineer", "Develop manufacturing execution software and assembly scheduling systems."),
        ("Software QA Engineer", "Inspect application logs and review automated test results."),
        ("Software Test Engineer", "Write automated tests in Python for the CI test framework."),
        ("Data Warehouse Developer", "Build ETL pipelines for an enterprise data warehouse.")
    };
    var newWorkTypeIds = new HashSet<string>([
        "role.mechanical-maintenance-repair", "role.fabrication-assembly-machining",
        "role.physical-inspection-quality-control", "role.lab-test-technician",
        "role.warehouse-material-handling", "role.manufacturing-production-operations"
    ], StringComparer.Ordinal);
    foreach (var (title, description) in hardNegatives)
    {
        Assert(!DetectWorkType(title, description).Overlaps(newWorkTypeIds),
            $"Industry or software context produced a work-type false positive for '{title}'.");
    }

    Assert(concepts.Version == 9 && concepts.Concepts.Count == 85 &&
           concepts.Concepts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == 85 &&
           concepts.Concepts.Any(item => item.Category == "Role Type / Career Direction") &&
           concepts.Concepts.Any(item => item.Category == "Responsibility Shape") &&
           concepts.Concepts.All(item => !item.Id.StartsWith("qualification.", StringComparison.Ordinal)),
        "The expanded versioned corpus is incomplete, duplicated, or improperly duplicates Qualification Fit.");
    Assert(concepts.Get("work.remote.full").Supersedes?.SequenceEqual(["work.remote"]) == true &&
           concepts.Get("work.travel.moderate").Supersedes?.SequenceEqual(["work.travel.occasional"]) == true &&
           concepts.Get("work.travel.substantial").Supersedes?.SequenceEqual(
               ["work.travel.frequent", "work.travel.moderate", "work.travel.occasional"]) == true,
        "Canonical correlated-signal supersedence metadata is missing.");
    Assert(concepts.Get("role.lab-test-technician").Supersedes?.SequenceEqual(
               ["work.lab-environment"]) == true &&
           concepts.Get("role.manufacturing-production-operations").Supersedes?.SequenceEqual(
               ["work.manufacturing-floor"]) == true,
        "New work-type concepts must suppress duplicate environment scoring when both detect.");
    var internalTravel = concepts.Options
        .Where(option => option.Id.StartsWith("work.travel.", StringComparison.Ordinal))
        .ToArray();
    Assert(internalTravel.Length == 4 && internalTravel.All(option => !option.UserConfigurable) &&
           internalTravel.Select(option => option.TravelLevel).Order().SequenceEqual(
               new int?[] { 3, 4, 5, 6 }),
        "The four travel detectors were not retained as hidden, level-bearing corpus signals.");
    var internalLocations = concepts.Options
        .Where(option => WorkLocationPreference.IsLegacyConcept(option.Id))
        .ToArray();
    Assert(internalLocations.Length == 4 && internalLocations.All(option => !option.UserConfigurable) &&
           internalLocations.Select(option => option.WorkLocationLevel).Order().SequenceEqual(
               new int?[] { 0, 2, 3, 5 }),
        "The four location detectors were not retained as hidden, level-bearing corpus signals.");
    Assert(concepts.Options.All(option =>
            !string.IsNullOrWhiteSpace(option.Id) &&
            !string.IsNullOrWhiteSpace(option.DisplayName) &&
            !string.IsNullOrWhiteSpace(option.Category) &&
            option.Supersedes is not null),
        "A selectable canonical concept lacks a stable ID or display metadata.");
    return Task.CompletedTask;
}

static Task TestDetectorEvaluationAsync()
{
    var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var detector = new JobConceptDetector(catalog);
    var service = new DetectorEvaluationService(
        new TestHostEnvironment(AppContext.BaseDirectory), catalog, detector);
    var report = service.Evaluate("0123456789abcdef0123456789abcdef01234567");
    var retainedConcepts = new HashSet<string>([
        "role.mechanical-maintenance-repair", "role.fabrication-assembly-machining",
        "role.physical-inspection-quality-control", "role.lab-test-technician",
        "role.warehouse-material-handling", "role.manufacturing-production-operations"
    ], StringComparer.Ordinal);
    Assert(report.FixtureVersion == 3 && report.FixtureCount == 148 &&
           report.LabelCount == 1740 && report.CanonicalConceptCount == catalog.Concepts.Count &&
           report.Concepts.Count == catalog.Concepts.Count &&
           report.Concepts.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
               .SetEquals(catalog.Concepts.Select(item => item.Id)) &&
           report.EvaluatableCount == 73 && report.PartiallyEvaluatableCount == 12 &&
           report.ExcludedCount == 4 &&
           report.Concepts.Where(item => retainedConcepts.Contains(item.ConceptId)).All(item =>
               item.PositiveSupport == 8 && item.NegativeExamples == 8 && item.TotalExamples == 16 &&
               item.SampleSize == "Developing sample" && item.Examples.Count == 16) &&
           report.BuildSha == "0123456789abcdef0123456789abcdef01234567",
        $"The full-taxonomy evaluation corpus changed identity, coverage, support, or SHA metadata: " +
        $"version={report.FixtureVersion} fixtures={report.FixtureCount} labels={report.LabelCount} " +
        $"canonical={report.CanonicalConceptCount}/{catalog.Concepts.Count} " +
        $"classes={report.EvaluatableCount}/{report.PartiallyEvaluatableCount}/{report.ExcludedCount}.");
    Assert(report.Concepts.Where(item => item.Evaluated).All(item =>
               item.TruePositive + item.FalseNegative == item.PositiveSupport &&
               item.FalsePositive + item.TrueNegative == item.NegativeExamples &&
               (item.Precision is null or >= 0 and <= 1) &&
               (item.Recall is null or >= 0 and <= 1) &&
               (item.F1 is null or >= 0 and <= 1) &&
               item.ErrorFixtureIds.Count == item.FalsePositives.Concat(item.FalseNegatives)
                   .Select(example => example.FixtureId).Distinct(StringComparer.Ordinal).Count()) &&
           report.Macro.Precision is >= 0 and <= 1 &&
           report.Macro.Recall is >= 0 and <= 1 && report.Macro.F1 is >= 0 and <= 1 &&
           report.Micro.Precision is >= 0 and <= 1 &&
           report.Micro.Recall is >= 0 and <= 1 && report.Micro.F1 is >= 0 and <= 1 &&
           report.TierAggregates.Count == 3 &&
           report.TierAggregates.All(item => item.Macro.ConceptCount > 0 &&
               item.Micro.ConceptCount == item.Macro.ConceptCount),
        "Detector confusion counts, support, overall aggregates, or tier aggregates are invalid.");
    Assert(report.Concepts.Where(item => !item.Evaluated).All(item =>
               item.SampleSize == "Not evaluated" && item.Precision is null &&
               item.Recall is null && item.F1 is null) &&
           report.Concepts.Where(item => item.Tier == DetectorEvaluationService.Tier1)
               .All(item => item.Evaluated) &&
           report.Concepts.Any(item => item.EvaluationClass == "Partially evaluatable"),
        "Unevaluated, tier, or classification status is not deterministic.");
    var international = report.Concepts.Single(item =>
        item.ConceptId == "work.international-assignment");
    Assert(international.Examples.Any(item =>
               item.FixtureId == "tier2-ops-09" && item.Result == "TP"),
        "Detector Evaluation did not run the production extended-location analyzer for an explicit OCONUS assignment.");

    var concept = catalog.Get("role.mechanical-maintenance-repair");
    var fixture = new DetectorEvaluationService.LabeledFixture(
        "metric", "Synthetic", "Metric case", "Evidence", concept.Id, true,
        "Independent label", null, "synthetic", "Codex-reviewed");
    DetectorEvaluationService.Observation Observation(bool expected, bool predicted) => new(
        fixture with { Id = $"metric-{expected}-{predicted}", ExpectedPresent = expected },
        predicted ? new DetectedJobConcept(concept.Id, "Production evidence") : null);
    var matrix = DetectorEvaluationService.CalculateConcept(concept,
        [Observation(true, true), Observation(false, true),
         Observation(true, false), Observation(false, false)]);
    Assert(matrix.TruePositive == 1 && matrix.FalsePositive == 1 &&
           matrix.FalseNegative == 1 && matrix.TrueNegative == 1 &&
           matrix.Precision == 0.5 && matrix.Recall == 0.5 && matrix.F1 == 0.5 &&
           matrix.FalsePositives.Count == 1 && matrix.FalseNegatives.Count == 1 &&
           matrix.Examples.Select(item => item.Result).ToHashSet(StringComparer.Ordinal)
               .SetEquals(["TP", "FP", "FN", "TN"]) &&
           DetectorEvaluationService.SampleSizeLabel(4, 20) == "Small sample" &&
           DetectorEvaluationService.SampleSizeLabel(5, 15) == "Developing sample" &&
           DetectorEvaluationService.SampleSizeLabel(15, 15) == "Established sample" &&
           DetectorEvaluationService.SampleSizeLabel(15, 1) == "Small sample" &&
           DetectorEvaluationService.SampleSizeLabel(0, 20) == "Not evaluated",
        "TP/FP/FN/TN, precision, recall, F1, or error details were calculated incorrectly.");
    Assert(DetectorEvaluationService.Divide(0, 0) is null &&
           DetectorEvaluationService.HarmonicMean(null, 1) is null &&
           DetectorEvaluationService.AverageDefined([null, null]) is null,
        "Zero-denominator detector metrics must be explicitly undefined rather than NaN.");

    void AssertInvalid(DetectorEvaluationFixtureDocument document, string message)
    {
        try
        {
            _ = new DetectorEvaluationService(document, catalog, detector);
            throw new InvalidOperationException(message);
        }
        catch (InvalidDataException)
        {
        }
    }
    var validFixture = new DetectorEvaluationFixture(
        "schema-one", "Synthetic", "Schema case", "Explicit evidence", concept.Id, true);
    AssertInvalid(new DetectorEvaluationFixtureDocument(3, [validFixture, validFixture]),
        "Duplicate evaluation fixture IDs were accepted.");
    AssertInvalid(new DetectorEvaluationFixtureDocument(3,
        [validFixture with { Id = "schema-unknown", ConceptId = "missing.concept" }]),
        "An unknown evaluation concept ID was accepted.");
    AssertInvalid(new DetectorEvaluationFixtureDocument(3,
        [new DetectorEvaluationFixture("schema-scope", "Synthetic", "Scope case", "Evidence",
            LabelScope: "scope", ExpectedPresentConceptIds: ["technical.cloud"])],
        new Dictionary<string, IReadOnlyList<string>> { ["scope"] = ["technical.linux"] }),
        "A Present label outside its closed label scope was accepted.");
    return Task.CompletedTask;
}

static async Task TestObsoleteAutomaticSettingsIgnoredAsync()
{
    var directory = TestDirectory("obsolete-automatic-settings");
    try
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var currentJson = JsonSerializer.Serialize(ViewerSettings.Default, jsonOptions);
        var legacyJson = currentJson[..^1] +
            ",\"automaticCheckEnabled\":true,\"automaticCheckIntervalMinutes\":60}";
        var loaded = JsonSerializer.Deserialize<ViewerSettings>(legacyJson, jsonOptions);
        Assert(loaded is not null, "A settings document with obsolete automatic fields did not load.");

        var store = new FileWorkspaceDataStore(
            directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var normalized = state.NormalizeSettings(loaded!);
        var savedJson = JsonSerializer.Serialize(normalized, jsonOptions);
        Assert(!savedJson.Contains("automaticCheck", StringComparison.OrdinalIgnoreCase),
            "Obsolete automatic-refresh settings were written back into canonical settings.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestSharedSourceCacheAsync()
{
    var directory = TestDirectory("shared-source-cache");
    try
    {
        var firstStore = new FileWorkspaceDataStore(
            directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var secondStore = new FileWorkspaceDataStore(
            directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var firstState = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, firstStore);
        var secondState = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, secondStore);
        var query = WorkdaySource().Query;
        var refreshedAt = DateTimeOffset.UtcNow;
        await firstState.SaveJobsCacheAsync(
            [CachedJob("leidos", "REQ-SHARED", "/shared/job", "<p>Canonical posting.</p>")],
            refreshedAt, 0, query);
        await firstState.SaveSourceStatusAsync(query, refreshedAt, 0, null);

        var secondCache = await secondState.LoadJobsCacheAsync(query);
        var secondStatus = await secondState.LoadSourceStatusAsync(query);
        Assert(secondCache?.Jobs.Single().RequisitionId == "REQ-SHARED" &&
               secondStatus?.LastSuccessfulRefreshUtc == refreshedAt &&
               firstState.JobsCachePathFor(query) == secondState.JobsCachePathFor(query) &&
               firstState.JobsCachePathFor(query).Contains(
                   $"shared{Path.DirectorySeparatorChar}job-caches", StringComparison.Ordinal),
            "A cache/status refresh written by one workspace was not visible through the shared source identity.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestPreferencesDoNotMutateSharedCacheAsync()
{
    var directory = TestDirectory("shared-cache-preference-isolation");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var query = WorkdaySource().Query;
        await state.SaveJobsCacheAsync(
            [CachedJob("leidos", "REQ-CANONICAL", "/canonical/job", "<p>Bachelor's required.</p>")],
            DateTimeOffset.UtcNow, 0, query);
        var cachePath = state.JobsCachePathFor(query);
        var before = await File.ReadAllBytesAsync(cachePath);
        await state.SaveSettingsAsync(ViewerSettings.Default with
        {
            MinimumSalary = 250_000m,
            UserProfile = new UserProfile(new EducationProfile("doctorate", "Physics"), null, null, null)
        });
        _ = await state.LoadJobsCacheAsync(query);
        var after = await File.ReadAllBytesAsync(cachePath);
        Assert(before.SequenceEqual(after),
            "Workspace qualifications or compensation preferences altered the canonical shared job document.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacyWorkspaceCacheInactiveAsync()
{
    var directory = TestDirectory("legacy-workspace-cache-inactive");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var query = WorkdaySource().Query;
        var fingerprint = state.QueryFingerprint(query);
        var legacyRelative = WorkspaceDataFiles.LegacyWorkspaceCompanyCacheRelativePath(
            "leidos", fingerprint).Replace('/', Path.DirectorySeparatorChar);
        var legacyPath = Path.Combine(directory, legacyRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, "{\"schemaVersion\":6,\"jobs\":[]}");

        Assert(await state.LoadJobsCacheAsync(query) is null &&
               state.JobsCachePathFor(query) != legacyPath && File.Exists(legacyPath),
            "A legacy workspace-local split cache became the active source cache path.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacyAppliedSourceMigrationAsync()
{
    var directory = TestDirectory("legacy-applied-source");
    try
    {
        var store = new FileWorkspaceDataStore(
            directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var legacy = ViewerSettings.Default with
        {
            HasConfiguredSource = null,
            CompanyId = "leidos",
            Country = FacetDefaults.UnitedStatesCountry,
            Location = new FacetSelection(
                "da70a15d3ef40104ea4e240d39cef6a2",
                "Remote/Teleworker"),
            IncludeAllLocations = false,
            IncludeRemote = false,
            SelectedPhysicalLocations = []
        };
        await store.WriteJsonAsync(WorkspaceDataFile.Settings, legacy);
        var state = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var migrated = await state.LoadSettingsAsync();

        Assert(migrated.HasConfiguredSource == true && migrated.CompanyId == "leidos" &&
               migrated.Country == FacetDefaults.UnitedStatesCountry &&
               !migrated.IncludeAllLocations && migrated.IncludeRemote &&
               migrated.SelectedPhysicalLocations?.Count == 0 && migrated.PendingSource is null,
            "A legacy applied Leidos Remote source was not preserved as configured.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacyCacheUrlMigrationAsync()
{
    var directory = TestDirectory("legacy-cache-url");
    try
    {
        var (_, state, job) = await CreateCatalogAsync(directory);
        var query = new JobSourceQuery(
            FacetDefaults.UnitedStatesCountryId,
            FacetDefaults.UnitedStatesCountryLabel,
            false,
            true,
            []);
        var cachePath = state.JobsCachePathFor(query);
        var canonicalJson = await File.ReadAllTextAsync(cachePath);
        Assert(canonicalJson.Contains("\"sourceUrl\"", StringComparison.Ordinal) &&
               !canonicalJson.Contains("\"workdayUrl\"", StringComparison.Ordinal),
            "A newly written cache did not use the canonical sourceUrl field.");
        await File.WriteAllTextAsync(
            cachePath,
            canonicalJson.Replace("\"sourceUrl\"", "\"workdayUrl\"", StringComparison.Ordinal));

        var migrated = await state.LoadJobsCacheAsync(query);
        var rewritten = await File.ReadAllTextAsync(cachePath);
        Assert(migrated?.Jobs.Single().SourceUrl == job.SourceUrl,
            "The legacy cached posting URL was not restored.");
        Assert(rewritten.Contains("\"sourceUrl\"", StringComparison.Ordinal) &&
               !rewritten.Contains("\"workdayUrl\"", StringComparison.Ordinal),
            "The migrated cache was not rewritten with the canonical field.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestPortableWorkspaceRoundTripAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var source = new CompanySourceSettings(
        FacetDefaults.UnitedStatesCountry,
        false,
        true,
        []);
    var settings = ViewerSettings.Default with
    {
        IncludeKeywords = ["integration"],
        ExcludeKeywords = ["substation", "power distribution"],
        MinimumSalary = 115_000m,
        KeywordScope = "description",
        ThemeMode = "dark",
        UserProfile = new UserProfile(
            new EducationProfile("bachelor", null),
            new SecurityProfile("secret", "current"),
            new WorkAuthorizationProfile("usCitizen", "notRequired"),
            new CredentialProfile("complete", ["netapp-ncda", "itil-foundation"])),
        HideStrictEducationMismatch = true,
        HideStrictClearanceMismatch = true,
        HideStrictWorkAuthorizationMismatch = true,
        ExcludeStrongExtendedLocationRequirements = true,
        HasConfiguredSource = true,
        CompanyId = "leidos",
        Country = source.Country,
        IncludeRemote = true,
        CompanySources = new Dictionary<string, CompanySourceSettings> { ["leidos"] = source }
    };
    var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    var history = new JobHistoryDocument(5, new Dictionary<string, JobHistoryEntry>
    {
        ["leidos:REQ-SAVED"] = new("REQ-SAVED", "/job/saved", now, now, true,
            JobWorkflowStates.Saved, now, CompanyId: "leidos"),
        ["leidos:REQ-SAME"] = new("REQ-SAME", "/job/applied", now, now, true,
            JobWorkflowStates.Applied, now, CompanyId: "leidos"),
        ["boeing:REQ-SAME"] = new("REQ-SAME", "/job/hidden", now, now, true,
            JobWorkflowStates.Hidden, now, CompanyId: "boeing"),
        ["leidos:REQ-CLOSED"] = new("REQ-CLOSED", "/job/closed", now, now, true,
            JobWorkflowStates.Closed, now.AddHours(2), CompanyId: "leidos",
            AppliedAt: now, CloseReason: JobCloseReasons.ScreenedOut,
            ClosedAt: now.AddHours(2)),
        ["leidos:REQ-NORMAL"] = new("REQ-NORMAL", "/job/normal", now, now, true,
            CompanyId: "leidos")
    });

    var exported = portable.Export(settings, history);
    var json = JsonSerializer.Serialize(exported, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var imported = portable.ImportJson(json, ViewerSettings.Default, JobHistoryDocument.Empty);
    var importedCredentials = imported.Settings.UserProfile?.Credentials;

    Assert(exported.Format == PortableWorkspaceService.FormatIdentifier &&
           exported.Version == PortableWorkspaceService.CurrentVersion &&
           exported.CuratedJobs.Count == 4 &&
           exported.CuratedJobs.All(job => job.WorkflowState != JobWorkflowStates.Normal),
        "The portable file did not contain exactly the four curated workflow records.");
    Assert(exported.Format == "JobSearchManagerBackup" &&
           !json.Contains("WorkdayJobManager", StringComparison.OrdinalIgnoreCase) &&
           !json.Contains("automaticCheck", StringComparison.OrdinalIgnoreCase),
        "A new portable backup used a legacy identifier or retained automatic-refresh settings.");
    Assert(imported.Settings.HasConfiguredSource == false &&
           imported.Settings.PendingSource?.CompanyId == "leidos" &&
           imported.Settings.IncludeKeywords.SequenceEqual(["integration"]) &&
           imported.Settings.ExcludeKeywords.SequenceEqual(["substation", "power distribution"]) &&
           imported.Settings.MinimumSalary == 115_000m && imported.Settings.ThemeMode == "dark" &&
           imported.Settings.ExcludeStrongExtendedLocationRequirements &&
           importedCredentials is { InventoryStatus: "complete" } &&
           importedCredentials.HeldCredentialIds.SequenceEqual(
               ["netapp-ncda", "itil-foundation"]),
        "Portable preferences or pending source selection did not round-trip.");
    foreach (var themeMode in new[] { "nord-polar-night", "nord-snow-storm", "dracula" })
    {
        var nordExport = portable.Export(
            settings with { ThemeMode = themeMode }, JobHistoryDocument.Empty);
        var nordImport = portable.Import(
            nordExport, ViewerSettings.Default, JobHistoryDocument.Empty);
        Assert(nordImport.Settings.ThemeMode == themeMode,
            $"Theme {themeMode} did not survive workspace export/import.");
    }
    Assert(exported.Preferences.Compensation?.MinimumSalary == 115_000m &&
           exported.Preferences.Qualifications.MinimumSalary is null &&
           json.Contains("\"compensation\":{\"minimumSalary\":115000}", StringComparison.Ordinal) &&
           !json.Contains("\"qualifications\":{\"minimumSalary\"", StringComparison.Ordinal),
        "Compensation was not exported in the My Preferences section.");
    var legacyWorkspace = exported with
    {
        Version = 1,
        Preferences = exported.Preferences with
        {
            Compensation = null,
            Qualifications = exported.Preferences.Qualifications with
            {
                MinimumSalary = 99_000m,
                UserProfile = exported.Preferences.Qualifications.UserProfile with { Credentials = null }
            }
        }
    };
    var legacyPreferences = portable.Import(
        legacyWorkspace, ViewerSettings.Default, JobHistoryDocument.Empty).Settings;
    Assert(legacyPreferences.MinimumSalary == 99_000m &&
           legacyPreferences.UserProfile?.Credentials is null,
        "A version-1 workspace without credential inventory did not import conservatively.");
    var preFeatureJson = json.Replace(
        ",\"excludeStrongExtendedLocationRequirements\":true",
        "",
        StringComparison.Ordinal);
    var preFeatureImported = portable.ImportJson(
        preFeatureJson, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(!preFeatureImported.Settings.ExcludeStrongExtendedLocationRequirements,
        "A pre-feature workspace backup did not retain the conservative disabled default.");
    var preThemeJson = json.Replace("\"themeMode\":\"dark\",", "", StringComparison.Ordinal);
    var preThemeImported = portable.ImportJson(
        preThemeJson, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(preThemeImported.Settings.ThemeMode == ThemeModes.Default,
        "A workspace without a theme value did not fall back safely.");
    var obsoleteAutomaticJson = json.Replace(
        "\"application\":{",
        "\"application\":{\"automaticCheckEnabled\":true,\"automaticCheckIntervalMinutes\":60,",
        StringComparison.Ordinal);
    var obsoleteAutomaticImported = portable.ImportJson(
        obsoleteAutomaticJson, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(obsoleteAutomaticImported.Settings.ThemeMode == "dark",
        "A workspace backup with obsolete automatic-refresh fields did not import normally.");
    Assert(ThemeModes.Normalize("unknown-theme") == ThemeModes.Default &&
           ViewerSettings.Default.ThemeMode == ThemeModes.Default,
        "Unknown themes or reset defaults do not resolve to Light.");
    Assert(imported.History.Jobs["leidos:REQ-SAVED"].WorkflowState == JobWorkflowStates.Saved &&
           imported.History.Jobs["leidos:REQ-SAME"].WorkflowState == JobWorkflowStates.Applied &&
           imported.History.Jobs["boeing:REQ-SAME"].WorkflowState == JobWorkflowStates.Hidden &&
           imported.History.Jobs["leidos:REQ-CLOSED"] is
           {
               WorkflowState: JobWorkflowStates.Closed,
               CloseReason: JobCloseReasons.ScreenedOut
           } importedClosed &&
           importedClosed.AppliedAt == now && importedClosed.ClosedAt == now.AddHours(2) &&
           !imported.History.Jobs.ContainsKey("leidos:REQ-NORMAL"),
        "Saved, Applied, Closed, Hidden, absent-catalog, or company-isolated state did not round-trip.");
    var legacyImported = portable.Import(
        exported with { Format = PortableWorkspaceService.LegacyFormatIdentifier },
        ViewerSettings.Default,
        JobHistoryDocument.Empty);
    Assert(legacyImported.History.Jobs["leidos:REQ-SAVED"].WorkflowState == JobWorkflowStates.Saved &&
           legacyImported.Settings.ExcludeKeywords.SequenceEqual(["substation", "power distribution"]),
        "A backup exported under the previous product name did not import safely.");
    return Task.CompletedTask;
}

static Task TestPortableJobFitAsync()
{
    var environment = new TestHostEnvironment(AppContext.BaseDirectory);
    var concepts = new JobConceptCatalog(environment);
    var portable = new PortableWorkspaceService(new CompanyCatalog(environment), concepts);
    var configured = ViewerSettings.Default with
    {
        JobFit = new JobFitConfiguration(true,
        [
            new("technical.machine-learning", JobFitPreferenceLevels.StrongPositive),
            new("work.deployment", JobFitPreferenceLevels.HardConflict),
            new("work.onsite", JobFitPreferenceLevels.StrongNegative),
            new("work.remote", JobFitPreferenceLevels.Neutral),
            new("technical.cloud", JobFitPreferenceLevels.Neutral)
        ], 2, null, [JobFitGroupHardConflicts.SoftwareDevelopment,
            JobFitGroupHardConflicts.AiData])
    };

    var exported = portable.Export(configured, JobHistoryDocument.Empty);
    var imported = portable.Import(exported, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(exported.Version == 8 && exported.Preferences.JobFit is
               { Enabled: true, TravelTolerance: 2, PreferredWorkLocation: 0 } &&
           imported.Settings.JobFit is
               { Enabled: true, TravelTolerance: 2, PreferredWorkLocation: 0 } &&
           imported.Settings.JobFit.Signals.Count == 3 &&
           exported.Preferences.JobFit.Signals.Any(signal =>
               signal.ConceptId == "technical.machine-learning" &&
               signal.Preference == JobFitPreferenceLevels.Ideal) &&
           imported.Settings.JobFit.Signals.All(signal =>
               signal.Preference is not JobFitPreferenceLevels.StrongPositive and
                   not JobFitPreferenceLevels.StrongNegative) &&
           imported.Settings.JobFit.Signals.Any(signal =>
               signal.ConceptId == "technical.cloud" &&
               signal.Preference == JobFitPreferenceLevels.Neutral) &&
           imported.Settings.JobFit.GroupHardConflicts?.SequenceEqual(
               [JobFitGroupHardConflicts.AiData,
                JobFitGroupHardConflicts.SoftwareDevelopment]) == true &&
           imported.Settings.JobFit.Signals.All(signal => signal.ConceptId != "work.remote"),
        "New canonical Job Fit names or legacy-name migration did not round-trip through portable settings.");

    var legacy = exported with
    {
        Version = 3,
        Preferences = exported.Preferences with { JobFit = null }
    };
    var legacyImported = portable.Import(legacy, configured, JobHistoryDocument.Empty);
    Assert(legacyImported.Settings.JobFit is { Enabled: false } &&
           legacyImported.Settings.JobFit.Signals.Count == 0 &&
           legacyImported.Settings.JobFit.TravelTolerance is null &&
           legacyImported.Settings.JobFit.PreferredWorkLocation is null &&
           legacyImported.Settings.JobFit.GroupHardConflicts?.Count == 0,
        "An older workspace import without Job Fit data did not default to disabled.");

    var versionSixImported = portable.Import(exported with
    {
        Version = 6,
        Preferences = exported.Preferences with
        {
            JobFit = new JobFitConfiguration(true,
                [new("technical.machine-learning", JobFitPreferenceLevels.Ideal)], 4, 3)
        }
    }, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(versionSixImported.Settings.JobFit is { Enabled: true } &&
           versionSixImported.Settings.JobFit.GroupHardConflicts?.Count == 0,
        "A version-6 workspace without section overrides did not default every override to off.");

    var legacyTravel = portable.Import(exported with
    {
        Version = 4,
        Preferences = exported.Preferences with
        {
            JobFit = new JobFitConfiguration(true,
            [
                new("work.travel.frequent", JobFitPreferenceLevels.HardConflict),
                new("work.travel.substantial", JobFitPreferenceLevels.HardConflict)
            ])
        }
    }, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(legacyTravel.Settings.JobFit is { Enabled: true, TravelTolerance: 3 } &&
           legacyTravel.Settings.JobFit.Signals.Count == 0,
        "A version-4 portable workspace did not migrate legacy travel rows to level 3.");

    var legacyLocation = portable.Import(exported with
    {
        Version = 5,
        Preferences = exported.Preferences with
        {
            JobFit = new JobFitConfiguration(true,
            [
                new("work.remote.full", JobFitPreferenceLevels.Ideal),
                new("work.remote", JobFitPreferenceLevels.Ideal),
                new("work.hybrid", JobFitPreferenceLevels.Negative),
                new("work.onsite", JobFitPreferenceLevels.Negative)
            ], 4)
        }
    }, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(legacyLocation.Settings.JobFit is
               { Enabled: true, PreferredWorkLocation: 0 } &&
           legacyLocation.Settings.JobFit.Signals.Count == 0,
        "A version-5 portable workspace did not migrate the current location preferences to level 0.");

    AssertThrows<WorkspaceImportException>(() => portable.Import(
        exported with
        {
            Preferences = exported.Preferences with
            {
                JobFit = new JobFitConfiguration(true, [], 7)
            }
        },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));

    AssertThrows<WorkspaceImportException>(() => portable.Import(
        exported with
        {
            Preferences = exported.Preferences with
            {
                JobFit = new JobFitConfiguration(true, [], 4, 6)
            }
        },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));

    AssertThrows<WorkspaceImportException>(() => portable.Import(
        exported with
        {
            Preferences = exported.Preferences with
            {
                JobFit = new JobFitConfiguration(true,
                [new("arbitrary.user.phrase", JobFitPreferenceLevels.Positive)])
            }
        },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));

    AssertThrows<WorkspaceImportException>(() => portable.Import(
        exported with
        {
            Preferences = exported.Preferences with
            {
                JobFit = new JobFitConfiguration(
                    true, [], 4, 3, ["unsupported-group"])
            }
        },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));
    return Task.CompletedTask;
}

static Task TestPortableWorkspaceValidationAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var baseline = portable.Export(ViewerSettings.Default, JobHistoryDocument.Empty);
    AssertThrows<WorkspaceImportException>(() => portable.ImportJson(
        "{not valid json", ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Format = "AnotherFormat" },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Version = PortableWorkspaceService.CurrentVersion + 1 },
        ViewerSettings.Default,
        JobHistoryDocument.Empty));
    var unknownSource = baseline with
    {
        Preferences = baseline.Preferences with
        {
            JobSource = new PortableJobSource(
                "unknown-source",
                FacetDefaults.UnitedStatesCountry,
                false,
                true,
                [])
        }
    };
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        unknownSource, ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Preferences = baseline.Preferences with
        {
            Search = baseline.Preferences.Search with { KeywordScope = "executable-regex" }
        } }, ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Preferences = baseline.Preferences with
        {
            Compensation = new PortableCompensationPreferences(-1m)
        } }, ViewerSettings.Default, JobHistoryDocument.Empty));
    AssertThrows<WorkspaceImportException>(() => portable.Import(
        baseline with { Preferences = baseline.Preferences with
        {
            Application = baseline.Preferences.Application with { ThemeMode = "system-script" }
        } }, ViewerSettings.Default, JobHistoryDocument.Empty));
    var unsupported = baseline with
    {
        CuratedJobs =
        [
            new PortableCuratedJob(
                "unsupported-company", "unsupported-company:REQ-1", "REQ-1",
                JobWorkflowStates.Saved, "/job/REQ-1")
        ]
    };
    var isolatedUnsupported = portable.Import(
        unsupported, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(isolatedUnsupported.History.Jobs.TryGetValue(
               "unsupported-company:REQ-1", out var unsupportedEntry) &&
           unsupportedEntry.CompanyId == "unsupported-company" &&
           !isolatedUnsupported.History.Jobs.ContainsKey("leidos:REQ-1"),
        "An unsupported curated company identity was dropped or silently remapped.");

    var conflictingDuplicate = baseline with
    {
        CuratedJobs =
        [
            new PortableCuratedJob("leidos", "leidos:REQ-1", "REQ-1",
                JobWorkflowStates.Saved, "/job/REQ-1"),
            new PortableCuratedJob("leidos", "leidos:REQ-1", "REQ-1",
                JobWorkflowStates.Applied, "/job/REQ-1")
        ]
    };
    AssertThrows<WorkspaceImportException>(() =>
        portable.Import(conflictingDuplicate, ViewerSettings.Default, JobHistoryDocument.Empty));

    var mismatchedIdentity = baseline with
    {
        CuratedJobs =
        [
            new PortableCuratedJob("leidos", "leidos:REQ-OTHER", "REQ-1",
                JobWorkflowStates.Hidden, "/job/REQ-1")
        ]
    };
    AssertThrows<WorkspaceImportException>(() =>
        portable.Import(mismatchedIdentity, ViewerSettings.Default, JobHistoryDocument.Empty));

    using var exportedJson = JsonDocument.Parse(JsonSerializer.Serialize(baseline,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var root = exportedJson.RootElement;
    Assert(root.TryGetProperty("format", out _) && root.TryGetProperty("version", out _) &&
           root.TryGetProperty("preferences", out _) && root.TryGetProperty("curatedJobs", out _) &&
           !root.TryGetProperty("workspaceId", out _) && !root.TryGetProperty("jobs", out _) &&
           !root.TryGetProperty("cache", out _) && !root.TryGetProperty("securityToken", out _),
        "The external DTO is missing its versioned structure or exposed an internal runtime field.");
    return Task.CompletedTask;
}

static Task TestPortableSourceImportStateAsync()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var portable = new PortableWorkspaceService(companies);
    var leidos = ViewerSettings.Default with
    {
        HasConfiguredSource = true,
        CompanyId = "leidos",
        Country = FacetDefaults.UnitedStatesCountry,
        IncludeRemote = true,
        CompanySources = new Dictionary<string, CompanySourceSettings>
        {
            ["leidos"] = new(FacetDefaults.UnitedStatesCountry, false, true, [])
        }
    };
    var document = portable.Export(leidos, JobHistoryDocument.Empty);

    var freshImport = portable.Import(document, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(freshImport.Settings.HasConfiguredSource == false &&
           freshImport.Settings.PendingSource?.CompanyId == "leidos",
        "Importing a source into a fresh workspace did not create explicit pending state.");

    var equalImport = portable.Import(document, leidos, JobHistoryDocument.Empty);
    Assert(equalImport.Settings.HasConfiguredSource == true &&
           equalImport.Settings.CompanyId == "leidos" &&
           equalImport.Settings.PendingSource is null,
        "A source equivalent to the applied source created a false pending state.");

    var boeing = ViewerSettings.Default with
    {
        HasConfiguredSource = true,
        CompanyId = "boeing",
        Country = FacetDefaults.UnitedStatesCountry,
        IncludeAllLocations = true,
        IncludeRemote = true
    };
    var differentImport = portable.Import(document, boeing, JobHistoryDocument.Empty);
    Assert(differentImport.Settings.CompanyId == "boeing" &&
           differentImport.Settings.PendingSource?.CompanyId == "leidos",
        "A different imported source replaced the applied source instead of becoming pending.");

    var noSourceDocument = portable.Export(ViewerSettings.Default, JobHistoryDocument.Empty);
    var noSourceImport = portable.Import(
        noSourceDocument, ViewerSettings.Default, JobHistoryDocument.Empty);
    Assert(noSourceImport.Settings.HasConfiguredSource == false &&
           noSourceImport.Settings.PendingSource is null,
        "A backup without a Job Source created a conflicting source state.");
    return Task.CompletedTask;
}

static async Task TestPortableWorkspaceResetRestoreAsync()
{
    var directory = TestDirectory("portable-reset-restore");
    try
    {
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var portable = new PortableWorkspaceService(companies);
        var configured = state.NormalizeSettings(ViewerSettings.Default with
        {
            IncludeKeywords = ["cloud"],
            MinimumSalary = 125_000m,
            HasConfiguredSource = true,
            CompanyId = "leidos",
            IncludeRemote = true
        });
        var now = DateTimeOffset.UtcNow;
        var curated = new JobHistoryDocument(4, new Dictionary<string, JobHistoryEntry>
        {
            ["leidos:REQ-SAVED"] = new("REQ-SAVED", "/job/REQ-SAVED", now, now, true,
                JobWorkflowStates.Saved, now, CompanyId: "leidos"),
            ["leidos:REQ-A"] = new("REQ-A", "/job/a", now, now, true,
                JobWorkflowStates.Applied, now, CompanyId: "leidos"),
            ["leidos:REQ-H"] = new("REQ-H", "/job/h", now, now, true,
                JobWorkflowStates.Hidden, now, CompanyId: "leidos")
        });
        await state.SaveSettingsAsync(configured);
        await state.SaveJobHistoryAsync(curated);
        var exported = portable.Export(
            await state.LoadSettingsAsync(), await state.LoadJobHistoryAsync());
        var json = JsonSerializer.Serialize(exported, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await store.DeleteAllAsync();
        await state.EnsureSettingsFileAsync();
        var resetSettings = await state.LoadSettingsAsync();
        var resetHistory = await state.LoadJobHistoryAsync();
        Assert(resetSettings.HasConfiguredSource == false && resetHistory.Jobs.Count == 0,
            "Reset did not return the workspace to first-run state.");

        var restored = portable.ImportJson(json, resetSettings, resetHistory);
        await state.SaveSettingsAsync(restored.Settings);
        await state.SaveJobHistoryAsync(restored.History);
        var loadedSettings = await state.LoadSettingsAsync();
        var loadedHistory = await state.LoadJobHistoryAsync();
        Assert(loadedSettings.IncludeKeywords.SequenceEqual(["cloud"]) &&
               loadedSettings.MinimumSalary == 125_000m &&
               loadedSettings.HasConfiguredSource == false &&
               loadedSettings.PendingSource?.CompanyId == "leidos",
            "Settings or explicit-apply source semantics did not survive reset and restore.");
        Assert(loadedHistory.Jobs.Count(pair => pair.Value.WorkflowState == JobWorkflowStates.Saved) == 1 &&
               loadedHistory.Jobs.Count(pair => pair.Value.WorkflowState == JobWorkflowStates.Applied) == 1 &&
               loadedHistory.Jobs.Count(pair => pair.Value.WorkflowState == JobWorkflowStates.Hidden) == 1 &&
               loadedHistory.Jobs.Values.All(entry => JobWorkflowStates.IsValid(entry.WorkflowState)),
            "Curated workflow state did not survive the full export-reset-import round-trip.");
        var reconciled = await CreateCatalogAsync(directory);
        Assert(reconciled.Catalog.Snapshot.JobStates[reconciled.Job.StableId] ==
               JobWorkflowStates.Saved,
            "A restored curated state did not reconcile when the company/job identity reappeared.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestFreshCatalogSourceAsync()
{
    var directory = TestDirectory("fresh-catalog-source");
    try
    {
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var settings = state.NormalizeSettings(ViewerSettings.Default with
        {
            HasConfiguredSource = true,
            CompanyId = CompanyCatalog.DefaultCompanyId,
            IncludeRemote = true
        });
        await state.SaveSettingsAsync(settings);
        var query = JobSourceQuery.FromSettings(settings, companies);
        var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
        var academics = new AcademicQualificationDetector();
        var authorization = new WorkAuthorizationDetector();
        var remote = new RemoteWorkDetector();
        var client = new JobSourceClient(
            new HttpClient(),
            Options.Create(new JobSourceOptions()),
            NullLogger<JobSourceClient>.Instance,
            credentials,
            academics,
            authorization,
            remote,
            new ExtendedLocationRequirementDetector());
        var catalog = new JobCatalog(
            client,
            state,
            NullLogger<JobCatalog>.Instance,
            credentials,
            academics,
            authorization,
            remote,
            companies,
            Options.Create(new JobSourceOptions()));

        await catalog.InitializeAsync(query);

        Assert(catalog.Snapshot.Jobs.Count == 0 && catalog.Snapshot.LastRefreshedUtc is null,
            "Fresh catalog test unexpectedly loaded a jobs cache.");
        Assert(catalog.Snapshot.Query.IsEquivalentTo(query, companies),
            "The cacheless snapshot exposed a generic source instead of the applied source.");
        Assert(catalog.Snapshot.Query.CountryId == settings.Country.Id &&
               catalog.Snapshot.Query.IncludeAllLocations == settings.IncludeAllLocations &&
               catalog.Snapshot.Query.IncludeRemote == settings.IncludeRemote,
            "The cacheless snapshot did not retain all normalized source dimensions.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task TestBoeingCatalogAsync()
{
    using var document = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "CompanyCatalog.json")));
    var companies = document.RootElement.GetProperty("companies").EnumerateArray().ToArray();
    var company = companies.Single(item => item.GetProperty("id").GetString() == "boeing");
    Assert(company.GetProperty("displayName").GetString() == "Boeing" &&
           company.GetProperty("apiHost").GetString() ==
           "boeing.wd1.myworkdayjobs.com" &&
           company.GetProperty("tenant").GetString() == "boeing" &&
           company.GetProperty("site").GetString() == "EXTERNAL_CAREERS" &&
           company.GetProperty("defaultCountry").GetProperty("label").GetString() ==
           "United States of America",
        "Boeing was not represented solely by the verified generic catalog fields.");
    Assert(companies.All(item => item.GetProperty("id").GetString() != "deloitte-ie"),
        "Deloitte Ireland remains an active catalog company.");
    return Task.CompletedTask;
}

static Task TestExpandedCompanyCatalogAsync()
{
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["northrop-grumman"] = "Northrop Grumman",
        ["nvidia"] = "NVIDIA",
        ["parsons"] = "Parsons",
        ["aecom"] = "AECOM",
        ["rtx"] = "RTX"
    };
    foreach (var company in expected)
    {
        var definition = catalog.Get(company.Key);
        Assert(definition.DisplayName == company.Value && definition.RemoteLocationIds.Count > 0,
            $"The verified {company.Value} source or its remote facet configuration is missing.");
    }
    Assert(catalog.Get("aecom").IsSmartRecruiters &&
           catalog.Get("aecom").IsRemoteLocation("remote:ca") &&
           catalog.Get("aecom").RemoteLocationIdsForCountry("ca").Single() == "remote:ca" &&
           catalog.Get("nvidia").Provider == JobSourceProviders.Workday,
        "Provider selection was not represented by catalog data.");
    return Task.CompletedTask;
}

static Task TestNextCompanyCatalogAsync()
{
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var expected = new Dictionary<string, (string Name, string Category, string Provider)>
    {
        ["amentum"] = ("Amentum", "Defense & Federal", JobSourceProviders.Workday),
        ["kbr"] = ("KBR", "Defense & Federal", JobSourceProviders.Workday),
        ["booz-allen-hamilton"] = ("Booz Allen Hamilton", "Defense & Federal", JobSourceProviders.Workday),
        ["servicenow"] = ("ServiceNow", "Enterprise Software", JobSourceProviders.SmartRecruiters),
        ["nxp-semiconductors"] = ("NXP Semiconductors", "Semiconductors", JobSourceProviders.Workday)
    };
    foreach (var (id, source) in expected)
    {
        var company = catalog.Get(id);
        Assert(company.DisplayName == source.Name &&
               company.IndustryCategory == source.Category &&
               string.Equals(company.Provider, source.Provider, StringComparison.OrdinalIgnoreCase),
            $"The categorized source definition for {source.Name} is incomplete.");
    }

    var categories = catalog.Companies.ToDictionary(company => company.Id, company => company.IndustryCategory);
    Assert(categories.Count == 13 &&
           new[] { "leidos", "boeing", "northrop-grumman", "parsons", "rtx", "amentum", "kbr", "booz-allen-hamilton" }
               .All(id => categories[id] == "Defense & Federal") &&
           categories["servicenow"] == "Enterprise Software" &&
           new[] { "nvidia", "nxp-semiconductors" }.All(id => categories[id] == "Semiconductors") &&
           categories["aecom"] == "Engineering & Infrastructure" &&
           categories["mtm"] == "Healthcare & Transportation Services",
        "The supported-company category metadata does not match the broad industry taxonomy.");
    Assert(catalog.Get("kbr").CountryFacetParameter == "locationHierarchy1" &&
           catalog.Get("kbr").DefaultCountry.Id == "7d7dca02efe301804a21b8e9f401c00f" &&
           catalog.Get("nxp-semiconductors").CountryFacetParameter == "Location_Country" &&
           catalog.Get("servicenow").IsSmartRecruiters &&
           catalog.Get("booz-allen-hamilton").RemoteLocationIds.Count == 0,
        "A source-specific provider, country, or conservative remote capability is incorrect.");
    return Task.CompletedTask;
}

static async Task TestTopLevelCountryFacetAsync()
{
    const string response = """
        {
          "total": 161,
          "jobPostings": [],
          "facets": [
            {
              "facetParameter": "Location_Country",
              "descriptor": "Country/Territory",
              "values": [
                { "descriptor": "United States of America", "id": "bc33aa3152ec42d4995f4791a106ed09", "count": 161 }
              ]
            },
            {
              "facetParameter": "locationMainGroup",
              "values": [
                {
                  "facetParameter": "locations",
                  "descriptor": "Location",
                  "values": [
                    { "descriptor": "USA (home based)", "id": "98d67abaaa8a100fa63430f6acfc9346", "count": 12 },
                    { "descriptor": "Austin (Oakhill, Office)", "id": "nxp-austin", "count": 74 }
                  ]
                }
              ]
            }
          ]
        }
        """;
    var client = CreateSourceClient(new HttpClient(new StubHttpMessageHandler(_ => response)));
    var company = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory))
        .Get("nxp-semiconductors");
    var facets = await client.FetchLocationFacetsAsync(company, company.DefaultCountry.Id);
    Assert(facets.Countries.Single().Label == "United States of America" &&
           facets.RemoteLocations.Single().Label == "USA (home based)" &&
           facets.Groups.SelectMany(group => group.Locations).Single().Label == "Austin (Oakhill, Office)",
        "A top-level Workday country facet or nested location facet was not normalized generically.");
}

static Task TestExpandedLocationGroupingAsync()
{
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var cases = new[]
    {
        ("northrop-grumman", "ng-orlando", "United States-Florida-Orlando", "Orlando"),
        ("nvidia", "nv-santa-clara", "US, CA, Santa Clara", "Santa Clara"),
        ("parsons", "pa-orlando", "US - FL, Orlando", "Orlando"),
        ("aecom", "location:us:Orlando, FL, United States", "Orlando, FL, United States", "Orlando"),
        ("rtx", "rtx-orlando", "US-FL-ORLANDO", "ORLANDO")
    };
    foreach (var item in cases)
    {
        var organization = LocationFacetOrganizer.Organize(
            catalog.Get(item.Item1), null, [new FacetOption(item.Item2, item.Item3, 1)]);
        Assert(organization.StateMappedLocationCount == 1 &&
               organization.PhysicalLocations.Single().DisplayLabel == item.Item4,
            $"The {item.Item1} location format was not mapped to its U.S. state.");
    }
    return Task.CompletedTask;
}

static async Task TestSmartRecruitersSourceAsync()
{
    const string summary = """
        {"totalFound":1,"content":[{"id":"744000100000001","name":"Remote Project Analyst","refNumber":"J10000001","releasedDate":"2026-08-15T12:30:00Z","location":{"city":"Orlando","region":"FL","country":"us","remote":true,"hybrid":false,"fullLocation":"Orlando, FL, United States"},"typeOfEmployment":{"label":"Full-time"}}]}
        """;
    const string detail = """
        {"id":"744000100000001","name":"Remote Project Analyst","refNumber":"J10000001","releasedDate":"2026-08-15T12:30:00Z","postingUrl":"https://jobs.smartrecruiters.com/AECOM2/744000100000001-remote-project-analyst","location":{"city":"Orlando","region":"FL","country":"us","remote":true,"hybrid":false,"fullLocation":"Orlando, FL, United States"},"typeOfEmployment":{"label":"Full-time"},"jobAd":{"sections":{"companyDescription":{"title":"Company Description","text":"<p>Generic company remote-location boilerplate.</p>"},"jobDescription":{"title":"Job Description","text":"<p>This position is fully remote within the United States.</p>"},"qualifications":{"title":"Qualifications","text":"<p>Bachelor's degree plus four years of experience.</p>"},"additionalInformation":{"title":"Additional Information","text":"<p>Sponsorship for US employment authorization is not available now or in the future. The salary range for this role is 90,000 USD - 120,000 USD.</p>"}}}}
        """;
    var handler = new StubHttpMessageHandler(request =>
        request.RequestUri!.AbsolutePath.EndsWith("/744000100000001", StringComparison.Ordinal)
            ? detail
            : summary);
    var client = CreateSourceClient(new HttpClient(handler));
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var company = catalog.Get("aecom");

    var facets = await client.FetchLocationFacetsAsync(company, "us");
    Assert(facets.MatchingJobs == 1 && facets.RemoteLocations.Single().Id == "remote:us",
        "SmartRecruiters remote metadata did not become a selectable source facet.");

    var result = await client.FetchAllJobsAsync(
        company,
        new JobSourceQuery("us", "United States", false, true, [], CompanyId: "aecom"));
    var job = result.Jobs.Single();
    Assert(job.CompanyId == "aecom" && job.StableId == "aecom:J10000001" &&
           job.SourceUrl.Contains("jobs.smartrecruiters.com", StringComparison.Ordinal) &&
           job.PayMinimum == 90000m && job.PayMaximum == 120000m &&
           job.WorkAuthorization?.Sponsorship == "notAvailable" &&
           job.RemoteWork?.IsRemoteDesignated == true &&
           !job.DescriptionHtml.Contains("Generic company", StringComparison.Ordinal),
        $"SmartRecruiters normalization: company={job.CompanyId}, stable={job.StableId}, url={job.SourceUrl}, pay={job.PayMinimum}-{job.PayMaximum}/{job.PayPeriod}, sponsorship={job.WorkAuthorization?.Sponsorship}, remote={job.RemoteWork?.IsRemoteDesignated}, boilerplate={job.DescriptionHtml.Contains("Generic company", StringComparison.Ordinal)}.");
}

static async Task TestSmartRecruitersCrossPageDuplicateAsync()
{
    var detailRequests = 0;
    var handler = new StubHttpMessageHandler(request =>
    {
        if (!string.IsNullOrEmpty(request.RequestUri!.Query))
        {
            return SmartSummaryJson(101, SmartPostingJson(
                "744000100000001", "Project Analyst", "J-DUP", "Orlando, FL, United States"));
        }
        detailRequests++;
        return SmartDetailJson("744000100000001", "Project Analyst", "J-DUP",
            "Orlando, FL, United States", "<p>Exact duplicate fixture.</p>");
    });
    var result = await FetchAecomAsync(handler);
    Assert(result.Jobs.Count == 1 && detailRequests == 1 &&
           result.Jobs.Single().StableId == "aecom:J-DUP",
        "An exact SmartRecruiters duplicate across listing pages was not removed before detail hydration.");
}

static async Task TestSmartRecruitersLocationVariantAsync()
{
    var detailRequests = 0;
    var handler = new StubHttpMessageHandler(request =>
    {
        if (!string.IsNullOrEmpty(request.RequestUri!.Query))
        {
            return SmartSummaryJson(2,
                SmartPostingJson("744000100000010", "Lead Geologist", "J10156797",
                    " Bloomfield, New Jersey, United States"),
                SmartPostingJson("744000100000011", "Lead Geologist", "J10156797",
                    "Oakland, CA, United States"));
        }
        detailRequests++;
        return SmartDetailJson("744000100000010", "Lead Geologist", "J10156797",
            " Bloomfield, New Jersey, United States", "<p>Location-variant fixture.</p>");
    });
    var result = await FetchAecomAsync(handler);
    var job = result.Jobs.Single();
    Assert(job.StableId == "aecom:J10156797" && detailRequests == 1 &&
           job.PrimaryLocation.Contains("Bloomfield", StringComparison.Ordinal) &&
           job.AdditionalLocations.Single().Contains("Oakland", StringComparison.Ordinal),
        "Equivalent requisition location variants were not merged deterministically.");
}

static async Task TestSmartRecruitersConflictAsync()
{
    var handler = new StubHttpMessageHandler(request =>
    {
        if (!string.IsNullOrEmpty(request.RequestUri!.Query))
        {
            return SmartSummaryJson(2,
                SmartPostingJson("744000100000020", "Project Engineer", "J-CONFLICT",
                    "Orlando, FL, United States"),
                SmartPostingJson("744000100000021", "Program Manager", "J-CONFLICT",
                    "Orlando, FL, United States"));
        }
        var id = request.RequestUri.AbsolutePath.Split('/').Last();
        var title = id.EndsWith("20", StringComparison.Ordinal)
            ? "Project Engineer"
            : "Program Manager";
        return SmartDetailJson(id, title, "J-CONFLICT", "Orlando, FL, United States",
            $"<p>{title} materially distinct fixture.</p>");
    });
    var result = await FetchAecomAsync(handler);
    Assert(result.Jobs.Count == 2 &&
           result.Jobs.Select(job => job.StableId).Distinct(StringComparer.Ordinal).Count() == 2 &&
           result.Jobs.Any(job => job.StableId == "aecom:J-CONFLICT") &&
           result.Jobs.Any(job => job.StableId.Contains(":variant:path:", StringComparison.Ordinal)),
        "Conflicting provider records were discarded or retained with colliding identities.");
}

static async Task TestAecomDuplicateRefreshAsync()
{
    var directory = TestDirectory("aecom-duplicate-refresh");
    try
    {
        var handler = new StubHttpMessageHandler(request =>
            !string.IsNullOrEmpty(request.RequestUri!.Query)
                ? SmartSummaryJson(2,
                    SmartPostingJson("744000100000030", "Lead Geologist", "J10156797",
                        "Bloomfield, New Jersey, United States"),
                    SmartPostingJson("744000100000031", "Lead Geologist", "J10156797",
                        "Oakland, CA, United States"))
                : SmartDetailJson("744000100000030", "Lead Geologist", "J10156797",
                    "Bloomfield, New Jersey, United States", "<p>Refresh fixture.</p>"));
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
        var options = Options.Create(new JobSourceOptions());
        var client = new JobSourceClient(new HttpClient(handler), options,
            NullLogger<JobSourceClient>.Instance, credentials,
            new AcademicQualificationDetector(), new WorkAuthorizationDetector(),
            new RemoteWorkDetector(), new ExtendedLocationRequirementDetector());
        var catalog = new JobCatalog(client, state, NullLogger<JobCatalog>.Instance,
            credentials, new AcademicQualificationDetector(), new WorkAuthorizationDetector(),
            new RemoteWorkDetector(), companies, options);
        var query = AecomQuery();
        await catalog.InitializeAsync(query);
        var snapshot = await catalog.RefreshAsync(query);
        Assert(snapshot.Error is null && snapshot.Jobs.Count == 1 &&
               snapshot.Jobs.Single().StableId == "aecom:J10156797",
            $"AECOM duplicate requisition refresh failed: {snapshot.Error}");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task<JobSourceFetchResult> FetchAecomAsync(HttpMessageHandler handler)
{
    var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    return CreateSourceClient(new HttpClient(handler)).FetchAllJobsAsync(
        catalog.Get("aecom"), AecomQuery());
}

static JobSourceQuery AecomQuery() =>
    new("us", "United States", false, true, [], CompanyId: "aecom");

static string SmartSummaryJson(int totalFound, params string[] postings) =>
    $"{{\"totalFound\":{totalFound},\"content\":[{string.Join(',', postings)}]}}";

static string SmartPostingJson(string id, string title, string requisition, string location) =>
    JsonSerializer.Serialize(new
    {
        id,
        name = title,
        refNumber = requisition,
        releasedDate = "2026-08-15T12:30:00Z",
        location = new { city = "", region = "", country = "us", remote = true,
            hybrid = false, fullLocation = location },
        typeOfEmployment = new { label = "Full-time" }
    });

static string SmartDetailJson(
    string id, string title, string requisition, string location, string description) =>
    JsonSerializer.Serialize(new
    {
        id,
        name = title,
        refNumber = requisition,
        releasedDate = "2026-08-15T12:30:00Z",
        postingUrl = $"https://jobs.smartrecruiters.com/AECOM2/{id}",
        location = new { city = "", region = "", country = "us", remote = true,
            hybrid = false, fullLocation = location },
        typeOfEmployment = new { label = "Full-time" },
        jobAd = new { sections = new
        {
            companyDescription = new { title = "Company", text = "" },
            jobDescription = new { title = "Job Description", text = description },
            qualifications = new { title = "Qualifications", text = "" },
            additionalInformation = new { title = "Additional Information", text = "" }
        }}
    });

static async Task TestRepeatedRefreshCacheReuseAsync()
{
    var handler = CreateWorkdayHandler(() => 3);
    var client = CreateSourceClient(new HttpClient(handler));
    var (company, query) = WorkdaySource();
    var first = await client.FetchAllJobsAsync(company, query);
    var firstDetailRequests = handler.DetailRequests;
    var second = await client.FetchAllJobsAsync(company, query, cachedJobs: first.Jobs);

    Assert(firstDetailRequests == 3 && handler.DetailRequests == 3,
        "An unchanged repeated refresh downloaded cached descriptions again.");
    Assert(second.Metrics is { DetailRequests: 0, CacheHits: 3, CacheMisses: 0 },
        "Repeated-refresh cache metrics did not report complete reuse.");
}

static async Task TestRecentSourceSwitchAsync()
{
    var directory = TestDirectory("recent-source-switch");
    try
    {
        var handler = CreateWorkdayHandler(() => 3);
        var client = CreateSourceClient(new HttpClient(handler));
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var (_, baseQuery) = WorkdaySource();
        var northropQuery = baseQuery with { CompanyId = "northrop-grumman" };
        var hydrated = await client.FetchAllJobsAsync(
            companies.Get("northrop-grumman"), northropQuery);
        var (catalog, store, stateStore) = await CreateTestCatalogAsync(
            directory, handler, hydrated.Jobs, northropQuery);
        var northrop = await catalog.RefreshAsync(northropQuery);
        var nvidiaQuery = northropQuery with { CompanyId = "nvidia" };
        var rtxQuery = northropQuery with { CompanyId = "rtx" };
        store.Diagnostics.Reset();
        var nvidia = await catalog.RefreshAsync(nvidiaQuery);
        var nvidiaIo = store.Diagnostics.Snapshot();
        store.Diagnostics.Reset();
        var rtx = await catalog.RefreshAsync(rtxQuery);
        var rtxIo = store.Diagnostics.Snapshot();
        Console.WriteLine(
            $"METRIC Northrop listing={northrop.Metrics?.ListingsFetched} details={northrop.Metrics?.DetailRequests} reused={northrop.Metrics?.CacheHits}; " +
            $"NVIDIA listing={nvidia.Metrics?.ListingsFetched} details={nvidia.Metrics?.DetailRequests} writes={nvidiaIo.Writes} bytes={nvidiaIo.BytesWritten}; " +
            $"RTX listing={rtx.Metrics?.ListingsFetched} details={rtx.Metrics?.DetailRequests} writes={rtxIo.Writes} bytes={rtxIo.BytesWritten}");

        var listingBeforeReturn = handler.ListingRequests;
        var detailsBeforeReturn = handler.DetailRequests;
        store.Diagnostics.Reset();
        var cachedReturn = await catalog.SwitchSourceAsync(northropQuery);
        var cachedIo = store.Diagnostics.Snapshot();
        Console.WriteLine(
            $"METRIC Northrop->NVIDIA->RTX->Northrop return-listings=0 return-details=0 cache-reads={cachedIo.Reads} " +
            $"cache-writes={cachedIo.Writes} jobs={cachedReturn.Jobs.Count} elapsed-ms={cachedReturn.Metrics?.ElapsedMilliseconds}");
        Assert(handler.ListingRequests == listingBeforeReturn &&
               handler.DetailRequests == detailsBeforeReturn && cachedIo.Reads == 2 &&
               cachedIo.Writes == 0 && cachedReturn.IsCached &&
               cachedReturn.Metrics is { ListingsFetched: 0, DetailRequests: 0, CacheHits: 3 },
            "Returning to a recent company cache performed provider requests or persistence writes.");

        store.Diagnostics.Reset();
        await catalog.RefreshAsync(northropQuery);
        Assert(handler.ListingRequests == listingBeforeReturn + 1 &&
               handler.DetailRequests == detailsBeforeReturn &&
               store.Diagnostics.Snapshot().Writes == 1,
            "Explicit Refresh did not remain distinct from a cached source switch.");

        var repeatListings = handler.ListingRequests;
        var repeatDetails = handler.DetailRequests;
        await catalog.SwitchSourceAsync(nvidiaQuery);
        await catalog.SwitchSourceAsync(rtxQuery);
        await catalog.SwitchSourceAsync(northropQuery);
        Assert(handler.ListingRequests == repeatListings && handler.DetailRequests == repeatDetails,
            "The repeated NVIDIA/RTX/Northrop switch sequence did not remain cache-only.");

        await catalog.SwitchSourceAsync(rtxQuery);
        await stateStore.SaveSourceStatusAsync(northropQuery,
            DateTimeOffset.UtcNow.AddMinutes(-16), 0, null);
        var listingsBeforeStale = handler.ListingRequests;
        await catalog.SwitchSourceAsync(northropQuery);
        Assert(handler.ListingRequests == listingsBeforeStale + 1,
            "A stale source cache bypassed the conservative provider freshness check.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task TestSharedRefreshSingleFlightAsync()
{
    var directory = TestDirectory("shared-refresh-single-flight");
    try
    {
        var handler = CreateWorkdayHandler(() => 3, delayMilliseconds: 100);
        var query = WorkdaySource().Query;
        var coordinator = new SharedSourceRefreshCoordinator();
        var (first, _, _) = await CreateTestCatalogAsync(
            directory, handler, [], query, seedCache: false, coordinator: coordinator);
        var (second, _, _) = await CreateTestCatalogAsync(
            directory, handler, [], query, seedCache: false, coordinator: coordinator);

        var results = await Task.WhenAll(first.RefreshAsync(query), second.RefreshAsync(query));
        Assert(handler.ListingRequests == 1 &&
               results.All(result => result.Jobs.Count == 3) &&
               results.Count(result => result.IsCached) == 1,
            "Simultaneous workspaces performed duplicate provider refreshes instead of sharing one result.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestProviderHtmlNormalizationAsync()
{
    const string northrop = "RELOCATION ASSISTANCE: No relocation assistance available" +
        "<p style=\"text-align:inherit\"></p><p style=\"text-align:inherit\"></p>" +
        "CLEARANCE REQUIRED FOR START: No<p></p>CLEARANCE TYPE: Secret<p></p>" +
        "TRAVEL: Yes, 75% of the Time";
    const string rtx = "2026-08-14&amp;#xa;&amp;#xa;United States of America&#10;&#xA;";
    var normalizedNorthrop = ProviderHtmlNormalizer.Normalize(northrop);
    var normalizedRtx = ProviderHtmlNormalizer.Normalize(rtx);
    var clearance = JobAnalysis.AnalyzeClearance(normalizedNorthrop);
    Assert(normalizedNorthrop.Contains("available<br>CLEARANCE", StringComparison.Ordinal) &&
           normalizedNorthrop.Contains("No<br>CLEARANCE TYPE", StringComparison.Ordinal) &&
           normalizedNorthrop.Contains("Secret<br>TRAVEL", StringComparison.Ordinal) &&
           !normalizedRtx.Contains("&#", StringComparison.Ordinal) &&
           normalizedRtx.Contains("2026-08-14<br><br>United States", StringComparison.Ordinal) &&
           clearance.Level == "secret",
        "Provider HTML artifacts remained visible or normalization stopped feeding classification correctly.");
    return Task.CompletedTask;
}

static async Task TestChangedListingInvalidationAsync()
{
    var changed = false;
    var handler = CreateWorkdayHandler(
        () => 2,
        index => changed && index == 1 ? "Changed Engineer" : $"Engineer {index}");
    var client = CreateSourceClient(new HttpClient(handler));
    var (company, query) = WorkdaySource();
    var first = await client.FetchAllJobsAsync(company, query);
    changed = true;
    var before = handler.DetailRequests;
    var second = await client.FetchAllJobsAsync(company, query, cachedJobs: first.Jobs);

    Assert(handler.DetailRequests - before == 1 && second.Metrics?.CacheMisses == 1,
        "A material listing change did not invalidate exactly one cached detail.");
}

static async Task TestLocalReclassificationAsync()
{
    var handler = CreateWorkdayHandler(() => 1, description: _ =>
        "<p>The position will be onsite at the Customer Site in The Hague for 3 months to onboard.</p>" +
        "<p>Summary pay range: Level 3 (Experienced): $104,550 - $141,450</p>");
    var client = CreateSourceClient(new HttpClient(handler));
    var (company, query) = BoeingSource();
    var first = await client.FetchAllJobsAsync(company, query);
    var stale = first.Jobs.Select(job => job with
    {
        PayMinimum = null,
        PayMaximum = null,
        PayPeriod = "unknown",
        PayParseStatus = "unparseable",
        AnalysisVersion = 6
    }).ToArray();
    var before = handler.DetailRequests;
    var second = await client.FetchAllJobsAsync(company, query, cachedJobs: stale);

    Assert(handler.DetailRequests == before && second.Metrics?.ReclassifiedLocally == 1 &&
           second.Jobs.Single().AnalysisVersion == JobSourceClient.CurrentAnalysisVersion &&
           second.Jobs.Single().ExtendedLocationRequirement?.AnalysisVersion == 4 &&
           second.Jobs.Single().JobConceptCatalogVersion == 9 &&
           second.Jobs.Single().PayMinimum == 104_550m &&
           second.Jobs.Single().PayMaximum == 141_450m &&
           second.Jobs.Single().PayPeriod == "annual" &&
           second.Jobs.Single().DetectedConcepts?.Any(item =>
               item.ConceptId == "work.extended-away-assignment") == true,
        $"Parser invalidation did not reclassify cached text locally: details {before}->{handler.DetailRequests}, " +
        $"reclassified={second.Metrics?.ReclassifiedLocally}, company={second.Jobs.Single().CompanyId}, " +
        $"analysis={second.Jobs.Single().AnalysisVersion}, pay={second.Jobs.Single().PayMinimum}-" +
        $"{second.Jobs.Single().PayMaximum}/{second.Jobs.Single().PayPeriod}, " +
        $"extended={second.Jobs.Single().ExtendedLocationRequirement?.AnalysisVersion}, " +
        $"conceptCatalog={second.Jobs.Single().JobConceptCatalogVersion}.");

    var unrelatedHandler = CreateWorkdayHandler(() => 1);
    var unrelatedClient = CreateSourceClient(new HttpClient(unrelatedHandler));
    var (unrelatedCompany, unrelatedQuery) = WorkdaySource();
    var unrelatedFirst = await unrelatedClient.FetchAllJobsAsync(
        unrelatedCompany, unrelatedQuery);
    var unrelatedVersionSix = unrelatedFirst.Jobs
        .Select(job => job with { AnalysisVersion = 6 })
        .ToArray();
    var unrelatedBefore = unrelatedHandler.DetailRequests;
    var unrelatedSecond = await unrelatedClient.FetchAllJobsAsync(
        unrelatedCompany, unrelatedQuery, cachedJobs: unrelatedVersionSix);
    Assert(unrelatedHandler.DetailRequests == unrelatedBefore &&
           unrelatedSecond.Metrics?.ReclassifiedLocally == 0,
        "The Boeing-only parser migration rewrote an unrelated source cache.");
}

static async Task TestBoundedLargeSourceAsync()
{
    var handler = CreateWorkdayHandler(() => 45, delayMilliseconds: 15);
    var client = CreateSourceClient(new HttpClient(handler), new JobSourceOptions
    {
        DetailConcurrency = 2,
        MaximumDetailRequestsPerRefresh = 5
    });
    var (company, query) = WorkdaySource();
    var result = await client.FetchAllJobsAsync(company, query);

    Assert(result.Metrics is { ListingsFetched: 45, DetailRequests: 5, DeferredDetails: 40 } &&
           handler.DetailRequests == 5 && handler.MaximumConcurrentRequests <= 2,
        "Large-source hydration exceeded its detail or concurrency safety bound.");
}

static async Task TestRemovedJobCacheAsync()
{
    var listingCount = 2;
    var handler = CreateWorkdayHandler(() => listingCount);
    var client = CreateSourceClient(new HttpClient(handler));
    var (company, query) = WorkdaySource();
    var first = await client.FetchAllJobsAsync(company, query);
    listingCount = 1;
    var second = await client.FetchAllJobsAsync(company, query, cachedJobs: first.Jobs);

    Assert(second.Jobs.Count == 2 && second.Jobs.Count(job => job.IsSourceAvailable) == 1 &&
           second.Metrics?.RemovedListings == 1,
        "A removed source job was destroyed instead of retained as unavailable cache history.");
}

static async Task TestMissingJobWorkflowRetentionAsync(string workflowState)
{
    var directory = TestDirectory($"missing-{workflowState}");
    try
    {
        var listingCount = 1;
        var handler = CreateWorkdayHandler(() => listingCount);
        var (_, query) = WorkdaySource();
        var job = CachedJob("leidos", "REQ-0000", "/job/0", "<p>Last known description</p>");
        var (catalog, _, _) = await CreateTestCatalogAsync(directory, handler, [job], query);

        if (workflowState == JobWorkflowStates.Closed)
        {
            Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied) &&
                   await catalog.SetWorkflowStateAsync(
                       job.StableId, JobWorkflowStates.Closed, JobCloseReasons.PositionWithdrawn),
                "Could not arrange the closed-job refresh test state.");
        }
        else if (workflowState != JobWorkflowStates.Normal)
        {
            Assert(await catalog.SetWorkflowStateAsync(job.StableId, workflowState),
                $"Could not arrange the {workflowState}-job refresh test state.");
        }

        listingCount = 0;
        var refreshed = await catalog.RefreshAsync();

        if (workflowState == JobWorkflowStates.Normal)
        {
            Assert(refreshed.Jobs.Count == 0,
                "An untracked job missing from the provider remained in the visible catalog.");
        }
        else
        {
            var retained = refreshed.Jobs.SingleOrDefault();
            Assert(retained is not null && !retained.IsSourceAvailable &&
                   retained.StableId == job.StableId &&
                   retained.RequisitionId == job.RequisitionId &&
                   retained.Title == job.Title &&
                   retained.PrimaryLocation == job.PrimaryLocation &&
                   retained.StartDate == job.StartDate &&
                   retained.DescriptionHtml == job.DescriptionHtml &&
                   refreshed.JobStates[retained.StableId] == workflowState,
                $"A missing {workflowState} job or its last-known metadata was not retained.");
        }
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestRetainedJobRelistingAsync()
{
    var directory = TestDirectory("retained-job-relisting");
    try
    {
        var listingCount = 1;
        var handler = CreateWorkdayHandler(() => listingCount);
        var (_, query) = WorkdaySource();
        var job = CachedJob("leidos", "REQ-0000", "/job/0", "<p>Last known description</p>");
        var (catalog, _, state) = await CreateTestCatalogAsync(directory, handler, [job], query);
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved),
            "Could not arrange the retained-job relisting test state.");

        listingCount = 0;
        var missing = await catalog.RefreshAsync();
        listingCount = 1;
        var relisted = await catalog.RefreshAsync();
        var cached = await state.LoadJobsCacheAsync(query);

        Assert(missing.Jobs is [var retained] && !retained.IsSourceAvailable &&
               relisted.Jobs is [var current] && current.IsSourceAvailable &&
               current.StableId == job.StableId &&
               relisted.JobStates[current.StableId] == JobWorkflowStates.Saved &&
               cached?.Jobs.Count(item => item.StableId == job.StableId) == 1,
            "A relisted retained requisition duplicated or lost its saved workflow state.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestCompactListPayloadAsync()
{
    var description = $"<p>{new string('x', 5000)} secret-description-marker</p>";
    var job = CachedJob("leidos", "REQ-COMPACT", "/job/compact", description);
    var query = WorkdaySource().Query;
    var snapshot = new JobsSnapshot(
        [job], 1, DateTimeOffset.UtcNow, false, null, 0, false, [], query,
        new Dictionary<string, string>(), new Dictionary<string, JobClosureInfo>(), null);
    var compactJson = JsonSerializer.Serialize(JobsListSnapshot.FromSnapshot(snapshot));
    var fullJson = JsonSerializer.Serialize(snapshot);

    Assert(!compactJson.Contains("descriptionHtml", StringComparison.OrdinalIgnoreCase) &&
           !compactJson.Contains("secret-description-marker", StringComparison.Ordinal) &&
           compactJson.Length < fullJson.Length / 2,
        "The compact job-list DTO still contains the full description payload.");
    return Task.CompletedTask;
}

static async Task TestLazyDetailCacheAsync()
{
    var directory = TestDirectory("lazy-detail");
    try
    {
        var handler = CreateWorkdayHandler(() => 1);
        var query = WorkdaySource().Query;
        var summary = CachedJob("leidos", "REQ-0000", "/job/0", "") with
        {
            AnalysisVersion = JobSourceClient.CurrentAnalysisVersion
        };
        var (catalog, store, _) = await CreateTestCatalogAsync(directory, handler, [summary], query);

        var first = await catalog.GetJobDetailAsync(summary.StableId);
        store.Diagnostics.Reset();
        var second = await catalog.GetJobDetailAsync(summary.StableId);
        Assert(first?.DescriptionHtml.Length > 0 && second?.DescriptionHtml.Length > 0 &&
               handler.DetailRequests == 1 && store.Diagnostics.Snapshot().Writes == 0,
            "Lazy detail loading did not persist and reuse the first provider response.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestPerCompanyCacheEnvelopeAsync()
{
    var directory = TestDirectory("source-cache-envelope");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var leidosQuery = WorkdaySource().Query;
        var boeingQuery = leidosQuery with { CompanyId = "boeing" };
        await state.SaveJobsCacheAsync(
            [CachedJob("leidos", "REQ-SAME", "/leidos/same", "<p>Leidos</p>")],
            DateTimeOffset.UtcNow, 0, leidosQuery);
        await state.SaveJobsCacheAsync(
            [CachedJob("boeing", "REQ-SAME", "/boeing/same", "<p>Boeing</p>")],
            DateTimeOffset.UtcNow, 0, boeingQuery);

        var leidos = await state.LoadJobsCacheAsync(leidosQuery);
        var boeing = await state.LoadJobsCacheAsync(boeingQuery);
        Assert(leidos?.Jobs.Single().StableId == "leidos:REQ-SAME" &&
               boeing?.Jobs.Single().StableId == "boeing:REQ-SAME",
            "Independent company caches collided or replaced each other.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestCompanyCacheWriteIsolationAsync()
{
    var directory = TestDirectory("source-cache-isolation");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var northropQuery = WorkdaySource().Query with { CompanyId = "northrop-grumman" };
        var nvidiaQuery = WorkdaySource().Query with { CompanyId = "nvidia" };
        await state.SaveJobsCacheAsync(
            [CachedJob("northrop-grumman", "REQ-N", "/northrop/one", "<p>Northrop</p>")],
            DateTimeOffset.UtcNow, 0, northropQuery);
        var northropPath = state.JobsCachePathFor(northropQuery);

        store.Diagnostics.Reset();
        await state.SaveJobsCacheAsync(
            [CachedJob("nvidia", "REQ-V", "/nvidia/one", "<p>NVIDIA</p>")],
            DateTimeOffset.UtcNow, 0, nvidiaQuery);
        var write = store.Diagnostics.Snapshot();
        Assert(write.Writes == 1 &&
               write.WritesByDocument.Keys.Single().Contains("job-caches/nvidia/", StringComparison.Ordinal) &&
               File.Exists(northropPath),
            "Writing NVIDIA touched or replaced the Northrop company cache.");

        var alternateNorthrop = northropQuery with { IncludeAllLocations = false, IncludeRemote = true };
        await state.SaveJobsCacheAsync(
            [CachedJob("northrop-grumman", "REQ-N2", "/northrop/two", "<p>Alternate</p>")],
            DateTimeOffset.UtcNow, 0, alternateNorthrop);
        Assert(state.JobsCachePathFor(northropQuery) != state.JobsCachePathFor(alternateNorthrop) &&
               (await state.LoadJobsCacheAsync(northropQuery))?.Jobs.Single().RequisitionId == "REQ-N" &&
               (await state.LoadJobsCacheAsync(alternateNorthrop))?.Jobs.Single().RequisitionId == "REQ-N2",
            "Independent query fingerprints collided within one company cache.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestSplitCacheMigrationAsync()
{
    var directory = TestDirectory("split-cache-migration");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var leidosQuery = WorkdaySource().Query;
        var boeingQuery = leidosQuery with { CompanyId = "boeing" };
        var leidosJob = CachedJob("leidos", "REQ-L", "/leidos/legacy", "<p>Legacy description</p>") with
        {
            ClearanceLevel = "secret",
            ClearanceRequirement = "activeRequired"
        };
        var boeingJob = CachedJob("boeing", "REQ-B", "/boeing/legacy", "<p>Boeing description</p>");
        var now = DateTimeOffset.UtcNow;
        await store.WriteJsonAsync(
            WorkspaceDataFile.JobsCache,
            new JobsCacheEnvelope(5, new Dictionary<string, JobsCacheDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["leidos"] = new(5, now, now, 0, [leidosJob], leidosQuery),
                ["boeing"] = new(5, now, now, 0, [boeingJob], boeingQuery)
            }));
        var history = new JobHistoryDocument(4, new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal)
        {
            [leidosJob.StableId] = new(
                leidosJob.RequisitionId,
                leidosJob.ExternalPath,
                now,
                now,
                true,
                JobWorkflowStates.Saved,
                now,
                CompanyId: "leidos")
        });
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, history);

        var migratedLeidos = await state.LoadJobsCacheAsync(leidosQuery);
        var migratedBoeing = await state.LoadJobsCacheAsync(boeingQuery);
        Assert(!await store.ExistsAsync(WorkspaceDataFile.JobsCache) &&
               migratedLeidos?.Jobs.Single().DescriptionHtml.Contains("Legacy description", StringComparison.Ordinal) == true &&
               migratedLeidos.Jobs.Single().ClearanceLevel == "secret" &&
               migratedBoeing?.Jobs.Single().CompanyId == "boeing" &&
               (await state.LoadJobHistoryAsync()).Jobs[leidosJob.StableId].WorkflowState == JobWorkflowStates.Saved,
            "Cumulative-cache migration lost detail, classification, company identity, or curated history.");

        store.Diagnostics.Reset();
        var restarted = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        _ = await restarted.LoadJobsCacheAsync(leidosQuery);
        _ = await restarted.LoadJobsCacheAsync(boeingQuery);
        Assert(store.Diagnostics.Snapshot().Writes == 0,
            "Split-cache migration was not idempotent on restart.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestCachedDetailPureReadAsync()
{
    var directory = TestDirectory("cached-detail-pure-read");
    try
    {
        var handler = CreateWorkdayHandler(() => 1);
        var client = CreateSourceClient(new HttpClient(handler));
        var (company, query) = WorkdaySource();
        var hydrated = await client.FetchAllJobsAsync(company, query);
        var providerCalls = handler.DetailRequests;
        var (catalog, store, _) = await CreateTestCatalogAsync(
            directory, handler, hydrated.Jobs, query);
        store.Diagnostics.Reset();

        var detail = await catalog.GetJobDetailAsync(hydrated.Jobs.Single().StableId);
        var writes = store.Diagnostics.Snapshot();
        Assert(detail?.DescriptionHtml.Length > 0 && handler.DetailRequests == providerCalls &&
               writes.Writes == 0,
            "Opening a current cached posting caused provider or persistence activity.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestUnchangedRefreshWriteSuppressionAsync()
{
    var directory = TestDirectory("unchanged-write-suppression");
    try
    {
        var handler = CreateWorkdayHandler(() => 3);
        var client = CreateSourceClient(new HttpClient(handler));
        var (company, query) = WorkdaySource();
        var hydrated = await client.FetchAllJobsAsync(company, query);
        var (catalog, store, _) = await CreateTestCatalogAsync(directory, handler, hydrated.Jobs, query);
        var providerCalls = handler.DetailRequests;

        store.Diagnostics.Reset();
        await catalog.RefreshAsync();
        var manual = store.Diagnostics.Snapshot();
        Console.WriteLine(
            $"METRIC unchanged-refresh manual-writes={manual.Writes} manual-bytes={manual.BytesWritten}");
        Assert(handler.DetailRequests == providerCalls && manual.Writes == 1 &&
               manual.WritesByDocument.Keys.Single().StartsWith("shared/source-status/", StringComparison.Ordinal),
            "An unchanged manual refresh rewrote cache/history instead of only its tiny source status.");

    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestFirstEmptyRefreshPersistsBaselineAsync()
{
    var directory = TestDirectory("first-empty-refresh");
    try
    {
        var handler = CreateWorkdayHandler(() => 0);
        var (_, query) = WorkdaySource();
        var (catalog, store, state) = await CreateTestCatalogAsync(
            directory, handler, [], query, seedCache: false);
        store.Diagnostics.Reset();

        await catalog.RefreshAsync();
        var writes = store.Diagnostics.Snapshot();
        Assert(writes.WritesByDocument.Keys.Any(path =>
                   path.StartsWith("shared/job-caches/", StringComparison.Ordinal)) &&
               (await state.LoadJobsCacheAsync(query)) is { Jobs.Count: 0 },
            "The first empty provider result was not persisted as a valid source baseline.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestPartialHydrationCyclesAsync()
{
    var handler = CreateWorkdayHandler(() => 12);
    var client = CreateSourceClient(new HttpClient(handler), new JobSourceOptions
    {
        MaximumDetailRequestsPerRefresh = 5,
        DetailConcurrency = 2
    });
    var (company, query) = WorkdaySource();
    var first = await client.FetchAllJobsAsync(company, query);
    var afterFirst = handler.DetailRequests;
    var second = await client.FetchAllJobsAsync(company, query, cachedJobs: first.Jobs);
    var afterSecond = handler.DetailRequests;
    var third = await client.FetchAllJobsAsync(company, query, cachedJobs: second.Jobs);
    var afterThird = handler.DetailRequests;
    var fourth = await client.FetchAllJobsAsync(company, query, cachedJobs: third.Jobs);

    Assert(afterFirst == 5 && afterSecond - afterFirst == 5 && afterThird - afterSecond == 2 &&
           handler.DetailRequests - afterThird == 0 &&
           second.Metrics is { CacheHits: 5, DetailRequests: 5 } &&
           third.Metrics is { CacheHits: 10, DetailRequests: 2, DeferredDetails: 0 } &&
           fourth.Metrics is { CacheHits: 12, DetailRequests: 0, DeferredDetails: 0 } &&
           fourth.Jobs.All(job => !string.IsNullOrWhiteSpace(job.DescriptionHtml)),
        "Partial hydration redownloaded prior batches or mishandled the final/full cycle.");
}

static async Task TestCompressedCacheAsync()
{
    var directory = TestDirectory("compressed-cache");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
        var description = $"<p>compression-marker {new string('z', 10000)}</p>";
        await state.SaveJobsCacheAsync(
            [CachedJob("leidos", "REQ-ZIP", "/job/zip", description)],
            DateTimeOffset.UtcNow,
            0,
            WorkdaySource().Query);

        var raw = await File.ReadAllTextAsync(state.JobsCachePathFor(WorkdaySource().Query));
        var loaded = await state.LoadJobsCacheAsync(WorkdaySource().Query);
        Assert(!raw.Contains("compression-marker", StringComparison.Ordinal) &&
               raw.Length < description.Length / 2 &&
               loaded?.Jobs.Single().DescriptionHtml == description,
            "The cache did not compress at rest and hydrate the exact description.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestListingPageLimitAsync()
{
    var handler = CreateWorkdayHandler(() => 100);
    var client = CreateSourceClient(new HttpClient(handler), new JobSourceOptions
    {
        MaximumListingPages = 1,
        MaximumDetailRequestsPerRefresh = 1
    });
    var (company, query) = WorkdaySource();
    var result = await client.FetchAllJobsAsync(company, query);
    Assert(result.Metrics is { ListingsFetched: 20, ListingsTruncated: true } &&
           handler.ListingRequests == 1,
        "The listing-page safety limit was not enforced or surfaced.");
}

static async Task TestNorthropScaleMetricsAsync()
{
    const int listingCount = 2000;
    var handler = CreateWorkdayHandler(() => listingCount);
    var client = CreateSourceClient(new HttpClient(handler), new JobSourceOptions
    {
        MaximumListingPages = 200,
        MaximumDetailRequestsPerRefresh = 200,
        DetailConcurrency = 3
    });
    var (company, query) = WorkdaySource();
    var first = await client.FetchAllJobsAsync(company, query);
    var detailAfterFirst = handler.DetailRequests;
    var second = await client.FetchAllJobsAsync(company, query, cachedJobs: first.Jobs);
    var secondDetailRequests = handler.DetailRequests - detailAfterFirst;
    var snapshot = new JobsSnapshot(
        second.Jobs.Where(job => job.IsSourceAvailable).ToArray(),
        listingCount,
        DateTimeOffset.UtcNow,
        false,
        null,
        0,
        false,
        [],
        query,
        new Dictionary<string, string>(),
        new Dictionary<string, JobClosureInfo>(),
        null,
        second.Metrics);
    var compactBytes = JsonSerializer.SerializeToUtf8Bytes(
        JobsListSnapshot.FromSnapshot(snapshot)).Length;
    var compactWireBytes = GzipSize(JsonSerializer.SerializeToUtf8Bytes(
        JobsListSnapshot.FromSnapshot(snapshot)));
    var fullBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot).Length;

    Console.WriteLine(
        $"METRIC mocked-large summaries={listingCount} first-details={detailAfterFirst} " +
        $"second-details={secondDetailRequests} second-cache-hits={second.Metrics?.CacheHits} " +
        $"compact-bytes={compactBytes} gzip-bytes={compactWireBytes} full-cache-bytes={fullBytes} " +
        $"first-ms={first.Metrics?.ElapsedMilliseconds} second-ms={second.Metrics?.ElapsedMilliseconds}");
    Assert(detailAfterFirst == 200 && secondDetailRequests == 200 &&
           second.Metrics is { ListingsFetched: listingCount } &&
           compactBytes < fullBytes && compactWireBytes < 350_000,
        "The mocked large-source refresh exceeded its batch or compact-response boundary.");
}

static int GzipSize(byte[] value)
{
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
    {
        gzip.Write(value, 0, value.Length);
    }
    return checked((int)output.Length);
}

static async Task TestUnsupportedCompanyMigrationAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-company-migration-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var stale = ViewerSettings.Default with
        {
            HasConfiguredSource = true,
            CompanyId = "deloitte-ie",
            Country = new FacetSelection("04a05835925f45b3a59406a2a6b72c8a", "Ireland"),
            IncludeAllLocations = false,
            IncludeRemote = false,
            SelectedPhysicalLocations =
            [
                new FacetSelection("deloitte-dublin", "Dublin")
            ],
            CompanySources = new Dictionary<string, CompanySourceSettings>
            {
                ["deloitte-ie"] = new(
                    new FacetSelection("04a05835925f45b3a59406a2a6b72c8a", "Ireland"),
                    false,
                    false,
                    [new FacetSelection("deloitte-dublin", "Dublin")])
            }
        };
        await store.WriteJsonAsync(WorkspaceDataFile.Settings, stale);
        var catalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
        var state = new AppStateStore(NullLogger<AppStateStore>.Instance, catalog, store);

        var migrated = await state.LoadSettingsAsync();

        Assert(migrated.CompanyId == CompanyCatalog.DefaultCompanyId,
            "Unsupported active company did not migrate to the safe default company.");
        Assert(migrated.Country.Id == "bc33aa3152ec42d4995f4791a106ed09" &&
               migrated.IncludeAllLocations && migrated.IncludeRemote &&
               migrated.SelectedPhysicalLocations?.Count == 0,
            "Deloitte Ireland source facets were reinterpreted as the default company's source.");
        Assert(migrated.CompanySources is not null &&
               !migrated.CompanySources.ContainsKey("deloitte-ie"),
            "Unsupported per-company source state survived migration.");
        var persisted = await store.ReadJsonAsync<ViewerSettings>(WorkspaceDataFile.Settings);
        Assert(persisted?.CompanyId == CompanyCatalog.DefaultCompanyId,
            "The safe company migration was not persisted.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestUnsupportedCompanyHistoryAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-history-migration-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        var stale = new JobHistoryDocument(2, new Dictionary<string, JobHistoryEntry>
        {
            ["deloitte-ie:REQ-42"] = new("REQ-42", "/job/REQ-42", now, now, true,
                CompanyId: "deloitte-ie")
        });
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, stale);
        var state = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var loaded = await state.LoadJobHistoryAsync();
        Assert(loaded.Jobs.TryGetValue("deloitte-ie:REQ-42", out var entry) &&
               entry.CompanyId == "deloitte-ie" &&
               !loaded.Jobs.ContainsKey("boeing:REQ-42") &&
               !loaded.Jobs.ContainsKey("leidos:REQ-42"),
            "Unsupported Deloitte history was reassigned to an active company.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestCredentialCatalogAsync()
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    Assert(detector.CatalogVersion == 15, "The expanded credential catalog version was not loaded.");
    using var document = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "CredentialCatalog.json")));
    var ids = document.RootElement.GetProperty("credentials").EnumerateArray()
        .Select(item => item.GetProperty("id").GetString())
        .ToHashSet(StringComparer.Ordinal);
    var expected = new[]
    {
        "aca-chartered-accountant", "acca-qualification", "cima-professional-qualification",
        "cfa-charter", "iapp-cipp-e", "iapp-aigp", "iapp-cipm", "iapp-cipt",
        "ipass-payroll-qualification", "cta-chartered-tax-adviser",
        "oracle-certification-unspecified", "sap-certification-unspecified",
        "faa-airframe-powerplant", "epa-section-608-universal", "cpsm",
        "netapp-ncda", "netapp-ncse", "netapp-ncsie", "dell-proven-professional",
        "ipc-whma-a-620", "ipc-j-std-001", "ifma-cfm", "gccc-scmp", "gccc-cmp",
        "splunk-enterprise-admin", "giac-gcih", "giac-gcti",
        "independent-mental-health-license", "driver-license",
        "ccm", "ptoe", "aicp", "usace-cqm", "leed", "faa-part-107",
        "aws-cloud-practitioner", "aws-solutions-architect", "elastic-certification",
        "confluent-certification", "databricks-certification",
        "six-sigma-green-belt", "six-sigma-black-belt", "pmi-sp",
        "wilderness-first-responder", "cpa", "isa-cap"
    };
    Assert(expected.All(ids.Contains), "One or more verified credentials are absent.");
    return Task.CompletedTask;
}

static Task TestStorageCredentialsAsync()
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var required = detector.Analyze(
        "<p>Must possess a NetApp Certified Data Administrator (NCDA) certification or equivalent.</p>");
    var ncda = required.Credentials.Single(item => item.CredentialId == "netapp-ncda");
    Assert(ncda.Requirement == "required" && ncda.EquivalentAccepted &&
           ncda.Family == "NetApp Certification Program" &&
           ncda.EquivalentCredentialIds is { Count: 0 },
        "Full-name NCDA was not modeled as a required open-equivalence credential.");

    var abbreviation = detector.Analyze("<p>NCDA certification required.</p>");
    Assert(abbreviation.Credentials.Any(item =>
            item.CredentialId == "netapp-ncda" && item.Requirement == "required"),
        "NCDA abbreviation was not recognized as required.");

    var preferred = detector.Analyze(
        "<h3>Preferred Qualifications</h3><p>NCDA certification preferred.</p>");
    Assert(preferred.Credentials.Single(item => item.CredentialId == "netapp-ncda").Requirement == "preferred",
        "Preferred NCDA language became a hard requirement.");

    var related = detector.Analyze(
        "<p>NetApp Certified Support Engineer (NCSE) or NetApp Certified Storage Installation Engineer (NCSIE).</p>" +
        "<p>Dell EMC Proven Professional certification.</p><p>ITIL 4 Foundation.</p>");
    var ids = related.Credentials.Select(item => item.CredentialId).ToHashSet(StringComparer.Ordinal);
    Assert(new[] { "netapp-ncse", "netapp-ncsie", "dell-proven-professional", "itil-foundation" }
            .All(ids.Contains),
        $"Storage fixture recognized only: {string.Join(',', ids)}.");
    return Task.CompletedTask;
}

static Task TestExistingCredentialRegressionAsync()
{
    var analysis = new CredentialDetector(NullLogger<CredentialDetector>.Instance).Analyze(
        "<p>PE license; Security+; Network+; CCNA; CCNP; CISSP; ITIL Foundation certifications.</p>");
    var ids = analysis.Credentials.Select(item => item.CredentialId).ToHashSet(StringComparer.Ordinal);
    Assert(new[] { "pe", "security-plus", "network-plus", "ccna", "ccnp", "cissp", "itil-foundation" }
            .All(ids.Contains),
        $"Existing credential regression fixture recognized only: {string.Join(',', ids)}.");
    return Task.CompletedTask;
}

static Task TestCredentialAlternativeSemanticsAsync()
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var alternatives = detector.Analyze("<p>PMP or CCM certification required.</p>").Credentials
        .Where(item => item.CredentialId is "pmp" or "ccm")
        .ToArray();
    Assert(alternatives.Length == 2 &&
           alternatives.All(item => item.Requirement == "required" && item.IsAlternative) &&
           alternatives.Select(item => item.AlternativeGroup).Distinct().Count() == 1 &&
           !string.IsNullOrWhiteSpace(alternatives[0].AlternativeGroup),
        "PMP-or-CCM was not preserved as one explicit alternative group.");

    var corpusCredentials = detector.Analyze(
        "<p>PTOE certification preferred; AICP certification desired; " +
        "AWS Certified Solutions Architect required; FAA Part 107 certificate preferred; " +
        "USACE Construction Quality Management certification required.</p>");
    var ids = corpusCredentials.Credentials.Select(item => item.CredentialId)
        .ToHashSet(StringComparer.Ordinal);
    Assert(new[] { "ptoe", "aicp", "aws-solutions-architect", "faa-part-107", "usace-cqm" }
            .All(ids.Contains),
        $"Corpus-backed credential fixture recognized only: {string.Join(',', ids)}.");
    return Task.CompletedTask;
}

static Task TestUnknownRequiredCredentialAsync()
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var unknown = detector.Analyze(
        "<p>Must possess an Acme Certified Quantum Storage Administrator certification.</p>");
    Assert(unknown.UnknownRequirements is [{
            Name: "Acme Certified Quantum Storage Administrator",
            Requirement: "required",
            EquivalentAccepted: false
        }],
        $"Unknown mandatory credential was not surfaced: {string.Join('|', unknown.UnknownRequirements.Select(item => item.Name))}.");

    var mixed = detector.Analyze(
        "<p>Must possess a Security+ certification and Microsoft Associate Level certification.</p>");
    Assert(mixed.Credentials.Any(item => item.CredentialId == "security-plus") &&
           mixed.UnknownRequirements.Any(item =>
               item.Name.Contains("Microsoft Associate Level", StringComparison.OrdinalIgnoreCase)),
        "A new mandatory credential beside a known credential was silently ignored.");
    Assert(unknown.UnknownRequirements.All(item => !item.EquivalentAccepted),
        "An unknown requirement received a fabricated equivalence.");
    var certified = detector.Analyze("<p>Applicants must be Acme Quantum Platform certified.</p>");
    Assert(certified.UnknownRequirements.Any(item =>
            item.Name == "Acme Quantum Platform" && item.Requirement == "required"),
        "Unknown 'must be certified' language was not surfaced.");
    return Task.CompletedTask;
}

static Task TestLeidosCredentialDiscoveryFixtureAsync()
{
    var detector = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var named = detector.Analyze(
        "<h3>Preferred Qualifications</h3>" +
        "<p>Certified Facility Manager (CFM) or similar professional certification.</p>" +
        "<p>Splunk Certified Administrator certification strongly preferred.</p>" +
        "<p>GIAC Certified Incident Handler (GCIH) Certification.</p>" +
        "<p>GIAC Cyber Threat Intelligence (GCIT) Certification.</p>" +
        "<p>Current certification in IPC/WHMA-A-620 with Space addendum and J-STD-001.</p>" +
        "<p>Strategic Communication Management Professional (SCMP) or Communication Management Professional (CMP).</p>" +
        "<p>The candidate must hold a valid independent license in the mental health field.</p>");
    var ids = named.Credentials.Select(item => item.CredentialId).ToHashSet(StringComparer.Ordinal);
    Assert(new[] { "ifma-cfm", "splunk-enterprise-admin", "giac-gcih", "giac-gcti",
                   "ipc-whma-a-620", "ipc-j-std-001", "gccc-scmp", "gccc-cmp",
                   "independent-mental-health-license" }.All(ids.Contains),
        $"Today's named-credential fixture recognized only: {string.Join(',', ids)}.");

    var traps = detector.Analyze(
        "<p>Support Certification and Accreditation (C&A) activities and certificate management.</p>" +
        "<p>Relevant professional certifications are preferred.</p>" +
        "<p>Additional experience/certifications may be considered in lieu of a degree.</p>" +
        "<p>Active Professional Engineer (PE) license required; must hold or be able to obtain licensure in Pennsylvania.</p>");
    Assert(traps.Credentials.All(item => item.CredentialId == "pe") &&
           traps.UnknownRequirements.Count == 0,
        $"Corpus language created false requirements: {string.Join(',', traps.UnknownRequirements.Select(item => item.Name))}.");
    return Task.CompletedTask;
}

static Task TestAdvancedDegreeAsync()
{
    var analysis = new AcademicQualificationDetector().Analyze(
        "<p>Advanced degree (Master’s or higher) in computer science or a related discipline.</p>");
    Assert(analysis.MinimumLevel == "master" &&
           analysis.AnalysisVersion == AcademicQualificationDetector.CurrentAnalysisVersion,
        "Advanced degree (Master's or higher) was not normalized to a master's minimum.");
    return Task.CompletedTask;
}

static Task TestAcademicPreferenceSeparationAsync()
{
    var detector = new AcademicQualificationDetector();
    var fixtures = new[]
    {
        "<p>Bachelor's required. Master's preferred.</p>",
        "<p>B.S. required. M.S./PhD preferred.</p>",
        "<p>Bachelor's minimum; graduate degree desirable.</p>",
        "<p>Bachelor's or equivalent experience required. Master's a plus.</p>",
        "<h3>Minimum Qualifications</h3><p>Bachelor's degree</p>" +
            "<h3>Preferred Qualifications</h3><p>PhD</p>",
        "<h3>What we need to see</h3>" +
            "<p>B.S. or degree in Computer Science/Engineering or equivalent experience</p>" +
            "<h3>Ways to stand out from the crowd:</h3>" +
            "<p>M.S./PhD with significant compiler related project or thesis work preferred</p>"
    };

    foreach (var fixture in fixtures)
    {
        var analysis = detector.Analyze(fixture);
        Assert(analysis.MinimumLevel == "bachelor" &&
               analysis.RequirementType is "strictDegree" or "degreeOrExperience" &&
               analysis.ParseStatus == "parsed" &&
               analysis.Paths.Any(path =>
                   path.Level == "bachelor" && path.Requirement is "required" or "minimum") &&
               analysis.Paths.Any(path =>
                   path.Level is "master" or "doctorate" &&
                   path.Requirement is "preferred" or "desired"),
            $"Strict/preferred education separation failed: minimum={analysis.MinimumLevel}, " +
            $"type={analysis.RequirementType}, paths={string.Join('|', analysis.Paths.Select(path => $"{path.Level}:{path.Requirement}"))}, " +
            $"fixture={fixture}.");
    }

    var acceptedPaths = detector.Analyze("<p>Bachelor's or Master's degree required.</p>");
    Assert(acceptedPaths.MinimumLevel == "bachelor" &&
           acceptedPaths.Paths.Where(path => path.Level is "bachelor" or "master")
               .All(path => path.Requirement == "required"),
        "Bachelor's-or-Master's accepted paths became a master's minimum.");

    var fieldPreference = detector.Analyze(
        "<h3>Required Qualifications</h3>" +
        "<p>M.S. degree in Structural Engineering.</p>" +
        "<p>Bachelor's degree in Civil, preferred, or Mechanical Engineering.</p>");
    Assert(fieldPreference.MinimumLevel == "bachelor",
        "A preferred field inside a Bachelor's path must not make the degree itself preferred.");

    var trailingFieldPreference = detector.Analyze(
        "<h3>Required Qualifications</h3>" +
        "<p>Master's degree in Structural Engineering.</p>" +
        "<p>Bachelor's degree in Civil, Electrical, or Mechanical Engineering; Civil Engineering is preferred.</p>");
    Assert(trailingFieldPreference.MinimumLevel == "bachelor",
        "A trailing preferred field must not make the Bachelor's path optional.");

    var mastersRequired = detector.Analyze(
        "<h3>Minimum Qualifications</h3><p>Master's degree in Engineering required.</p>" +
        "<h3>Preferred Qualifications</h3><p>PhD preferred.</p>");
    Assert(mastersRequired.MinimumLevel == "master" &&
           mastersRequired.Paths.Any(path =>
               path.Level == "master" && path.Requirement == "required") &&
           mastersRequired.Paths.Any(path =>
               path.Level == "doctorate" && path.Requirement == "preferred"),
        "A true master's minimum with preferred PhD was weakened or inflated.");

    var doctorateRequired = detector.Analyze(
        "<h3>Required Qualifications</h3><p>PhD required.</p>");
    Assert(doctorateRequired.MinimumLevel == "doctorate" &&
           doctorateRequired.RequirementType == "strictDegree",
        "A true required doctorate was no longer strict.");
    return Task.CompletedTask;
}

static Task TestAbetAccreditationAsync()
{
    var detector = new AcademicQualificationDetector();
    var preferred = detector.Analyze(
        "<h3>Basic Qualifications</h3><p>Bachelor's degree in engineering.</p>" +
        "<p>In the USA, ABET accreditation is the preferred, although not required, accreditation standard.</p>");
    Assert(preferred.Accreditations is [{ Name: "ABET", Requirement: "preferred" }],
        "Boeing's standard ABET wording was not retained as a preferred academic accreditation.");
    var required = detector.Analyze(
        "<h3>Required Qualifications</h3><p>A bachelor's degree from an ABET-accredited program is required.</p>");
    Assert(required.Accreditations is [{ Name: "ABET", Requirement: "required" }],
        "An explicit ABET requirement was not classified as required.");
    return Task.CompletedTask;
}

static async Task TestFileResetAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"job-search-manager-reset-test-{Guid.NewGuid():N}");
    try
    {
        var store = new FileWorkspaceDataStore(
            directory,
            NullLogger<FileWorkspaceDataStore>.Instance);
        foreach (var file in Enum.GetValues<WorkspaceDataFile>())
        {
            await store.WriteJsonAsync(file, new TestDocument(file.ToString(), 1));
        }
        var fingerprint = new string('b', 64);
        await store.WriteCompanyCacheJsonAsync(
            "northrop-grumman", fingerprint, new TestDocument("cache", 1));
        await store.WriteSourceStatusJsonAsync(
            "northrop-grumman", fingerprint, new TestDocument("status", 1));
        var unrelatedPath = Path.Combine(directory, "leave-me-alone.txt");
        await File.WriteAllTextAsync(unrelatedPath, "not application state");

        var deleted = await store.DeleteAllAsync();
        Assert(deleted == 2, $"Expected two user-specific documents to be deleted, but deleted {deleted}.");
        Assert(!File.Exists(store.Describe(WorkspaceDataFile.Settings)) &&
               !File.Exists(store.Describe(WorkspaceDataFile.JobHistory)),
            "A user-specific workspace document survived reset.");
        Assert(File.Exists(store.Describe(WorkspaceDataFile.JobsCache)),
            "Reset deleted a retained legacy cache before migration verification.");
        Assert(File.Exists(store.DescribeCompanyCache("northrop-grumman", fingerprint)) &&
               File.Exists(store.DescribeSourceStatus("northrop-grumman", fingerprint)),
            "Reset deleted shared company cache or source-status data.");
        Assert(File.Exists(unrelatedPath), "Reset deleted an unrelated file from the data directory.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static Task TestUsCitizenshipAsync()
{
    var detector = new WorkAuthorizationDetector();
    var strict = detector.Analyze("<p>Candidate must be a U.S. citizen.</p>");
    Assert(strict.Eligibility == "usCitizen" && strict.CountryCode == "US" &&
           strict.Strength == "strict" && strict.ParseStatus == "parsed",
        "Explicit U.S. citizenship was not parsed as a strict requirement.");
    var preferred = detector.Analyze("<p>U.S. citizenship is preferred.</p>");
    Assert(preferred.Eligibility == "usCitizen" && preferred.Strength == "preferred" &&
           preferred.ParseStatus == "parsed",
        "Explicitly preferred citizenship wording was not kept non-strict.");
    return Task.CompletedTask;
}

static Task TestCitizenOrResidentAsync()
{
    var analysis = new WorkAuthorizationDetector().Analyze(
        "<p>Must be either a U.S. Citizen OR a U.S. Permanent Resident/Green Card holder.</p>");
    Assert(analysis.Eligibility == "usCitizenOrPermanentResident" && analysis.Strength == "strict",
        $"Citizen-or-permanent-resident wording parsed as {analysis.Eligibility}/{analysis.Strength}.");
    return Task.CompletedTask;
}

static Task TestSponsorshipAsync()
{
    var analysis = new WorkAuthorizationDetector().Analyze(
        "<p>Must be authorized to work in the United States and not require work authorization sponsorship by our company now or in the future.</p>");
    Assert(analysis.Eligibility == "usWorkAuthorized" && analysis.Sponsorship == "notAvailable" &&
           analysis.Strength == "strict" && analysis.SponsorshipStrength == "strict",
        "Combined U.S. authorization/no-sponsorship requirement was not parsed.");
    var sponsorshipOnly = new WorkAuthorizationDetector().Analyze(
        "<p>No employment sponsorship available.</p>");
    Assert(sponsorshipOnly.Eligibility == "noneSpecified" &&
           sponsorshipOnly.Sponsorship == "notAvailable" &&
           sponsorshipOnly.SponsorshipStrength == "strict" &&
           sponsorshipOnly.ParseStatus == "parsed",
        "Standalone no-sponsorship wording was not preserved as a strict sponsorship predicate.");
    var boeing = new WorkAuthorizationDetector().Analyze(
        "<p>Employer will not sponsor applicants for employment visa status.</p>");
    Assert(boeing.Sponsorship == "notAvailable" && boeing.SponsorshipStrength == "strict",
        "The common employer-will-not-sponsor wording was not recognized.");
    return Task.CompletedTask;
}

static Task TestNoClearanceRequiredAsync()
{
    var none = JobAnalysis.AnalyzeClearance(
        "<p>This position does not require a Security Clearance.</p>");
    Assert(none.Level == "noneMentioned" && none.Requirement == "none",
        "An explicit no-clearance statement was treated as a clearance requirement.");
    var positiveElsewhere = JobAnalysis.AnalyzeClearance(
        "<p>This position does not require a Security Clearance.</p>" +
        "<p>Candidate must obtain a U.S. Secret clearance.</p>");
    Assert(positiveElsewhere.Level == "secret",
        $"Removing a negative clearance statement hid a separate positive requirement: {positiveElsewhere.Level}/{positiveElsewhere.Requirement}; {positiveElsewhere.Evidence}.");
    return Task.CompletedTask;
}

static Task TestUsPersonAsync()
{
    var detector = new WorkAuthorizationDetector();
    var candidate = detector.Analyze(
        "<p>Must be a US Citizen or US Person who has lived in the United States for three years.</p>");
    Assert(candidate.Eligibility == "usPerson" && candidate.Strength == "ambiguous" &&
           candidate.ParseStatus == "review",
        "U.S.-person candidate wording must remain review-only.");
    var information = detector.Analyze(
        "<p>Protect U.S. person information collected under FISA authorities.</p>");
    Assert(information.Eligibility == "noneSpecified",
        "U.S.-person information was misclassified as candidate eligibility.");
    return Task.CompletedTask;
}

static Task TestAuthorizationFalsePositivesAsync()
{
    var detector = new WorkAuthorizationDetector();
    var clearance = detector.Analyze("<p>We are not able to sponsor the clearance requirement.</p>");
    Assert(clearance.Sponsorship == "noneSpecified",
        "Clearance sponsorship was misclassified as employment sponsorship.");
    var residentEngineer = detector.Analyze("<p>The resident engineer supports a permanent position.</p>");
    Assert(residentEngineer.Eligibility == "noneSpecified",
        "Ordinary resident/permanent wording was misclassified.");
    var citizenDeveloper = detector.Analyze("<p>Support the citizen developer community.</p>");
    Assert(citizenDeveloper.Eligibility == "noneSpecified",
        "Citizen-developer wording was misclassified.");
    return Task.CompletedTask;
}

static Task TestInternationalAuthorizationAsync()
{
    var detector = new WorkAuthorizationDetector();
    var australian = detector.Analyze("<p>The successful applicant must be an Australian Citizen.</p>");
    Assert(australian.Eligibility == "australianCitizen" && australian.CountryCode == "AU" &&
           australian.Strength == "strict",
        "Australian citizenship wording was not surfaced.");
    var export = detector.Analyze(
        "<p>Applicants may also need to meet International Traffic in Arms Regulations (ITAR) requirements.</p>");
    Assert(export.Eligibility == "exportControlled" && export.Strength == "customerDependent" &&
           export.ParseStatus == "review",
        "Conditional ITAR wording should be review-only.");
    return Task.CompletedTask;
}

static Task TestLocationWorkRightsAsync()
{
    var analysis = new WorkAuthorizationDetector().Analyze(
        "<p>Valid work rights for the role location (no sponsorship available).</p>");
    Assert(analysis.Eligibility == "locationWorkAuthorized" &&
           analysis.Strength == "strict" &&
           analysis.CountryCode is null &&
           analysis.Sponsorship == "notAvailable" &&
           analysis.SponsorshipStrength == "strict" &&
           analysis.ParseStatus == "parsed",
        "Location-specific work-rights wording was not parsed conservatively.");
    return Task.CompletedTask;
}

static Task TestRemoteOnsiteAsync()
{
    var analysis = RemoteAnalysis(
        "<p>This role will provide onsite installation support at the customer facility.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "onsite-duty"),
        "A current onsite duty was not treated as a strong remote-work conflict.");
    return Task.CompletedTask;
}

static Task TestRemoteFieldDeploymentAsync()
{
    var analysis = RemoteAnalysis(
        "<p>You will conduct field integration testing and support operational validation.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "field-deployment"),
        "Field deployment/testing was not detected.");
    return Task.CompletedTask;
}

static Task TestRemoteCommuteAsync()
{
    var analysis = RemoteAnalysis(
        "<p>Candidates must live within 50 miles of the regional office.</p>");
    Assert(analysis.ConcernLevel == "questionable" &&
           analysis.Signals.Any(signal => signal.Category == "commuting-area"),
        "A commuting-radius restriction was not surfaced conservatively.");
    return Task.CompletedTask;
}

static Task TestRemoteOnsiteDaysAsync()
{
    var analysis = RemoteAnalysis(
        "<p>The employee will work three days per week in the office.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "scheduled-onsite"),
        "Recurring onsite days were not treated as a strong conflict.");
    return Task.CompletedTask;
}

static Task TestRemoteTravelAsync()
{
    var occasional = RemoteAnalysis("<p>This position requires up to 10% travel.</p>");
    var moderate = RemoteAnalysis("<p>This position requires 25% travel.</p>");
    var range = RemoteAnalysis("<p>This position requires travel 10%-40% depending on project needs.</p>");
    var strong = RemoteAnalysis("<p>This position requires up to 75% travel to airport locations.</p>");
    var trailingPercentage = RemoteAnalysis("<p>The position may require travel up to 50% of the time.</p>");
    Assert(occasional.ConcernLevel == "none" &&
           occasional.Signals.Any(signal => signal.Category == "occasional-travel"),
        $"Occasional travel was incorrect: {occasional.ConcernLevel} " +
        $"[{string.Join(',', occasional.Signals.Select(signal => signal.Category))}].");
    Assert(moderate.ConcernLevel == "questionable" &&
           moderate.Signals.Any(signal => signal.Category == "moderate-travel"),
        $"Moderate travel was incorrect: {moderate.ConcernLevel} " +
        $"[{string.Join(',', moderate.Signals.Select(signal => signal.Category))}].");
    Assert(range.ConcernLevel == "questionable" &&
           range.Signals.Any(signal => signal.Category == "moderate-travel" &&
               signal.Reason == "10-40% travel"),
        $"Travel range was incorrect: {range.ConcernLevel} " +
        $"[{string.Join(',', range.Signals.Select(signal => $"{signal.Category}:{signal.Reason}"))}].");
    Assert(strong.ConcernLevel == "strong" &&
           strong.Signals.Any(signal => signal.Category == "substantial-travel"),
        "Substantial travel was not classified as strong.");
    Assert(trailingPercentage.ConcernLevel == "strong" &&
           trailingPercentage.Signals.Any(signal => signal.Category == "substantial-travel"),
        "A trailing travel percentage was not classified as substantial.");
    return Task.CompletedTask;
}

static Task TestTrueRemoteAsync()
{
    var analysis = RemoteAnalysis(
        "<p>This is a fully remote software role. Collaborate through video meetings and deliver cloud services from your home office.</p>");
    Assert(analysis.IsRemoteDesignated && analysis.ConcernLevel == "none",
        "Ordinary work-from-home language was incorrectly warned.");
    return Task.CompletedTask;
}

static Task TestRemoteAmbiguousAsync()
{
    var analysis = RemoteAnalysis(
        "<p>The team builds software used by customer facilities and may attend occasional team events.</p>");
    var periodic = RemoteAnalysis(
        "<p>Working onsite in a company or client office is a possibility; periodic travel may be required.</p>");
    Assert(analysis.ConcernLevel == "none" && periodic.ConcernLevel == "questionable",
        "A facility mention without a current physical-presence requirement was escalated.");
    return Task.CompletedTask;
}

static Task TestRemoteHistoricalExperienceAsync()
{
    var analysis = RemoteAnalysis(
        "<p>Prior experience working at customer sites and conducting field testing is preferred.</p>");
    Assert(analysis.ConcernLevel == "none",
        "Historical site experience was mistaken for a current-job obligation.");
    return Task.CompletedTask;
}

static Task TestLeidosRemoteFixtureAsync()
{
    var analysis = RemoteAnalysis(
        "<h3>Field Deployment Support</h3>" +
        "<p>Support installation and activation of sensing systems.</p>" +
        "<p>Assist field operations during deployment and troubleshoot equipment integration issues in the field.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "physical-installation"),
        "The sanitized Leidos sensor-integration fixture was not classified as strongly inconsistent with WFH.");
    return Task.CompletedTask;
}

static Task TestMtmRemoteFixtureAsync()
{
    var analysis = RemoteAnalysis(
        "<p>Coordinate member transportation by telephone and document service issues. " +
        "The platform connects members with medical facilities.</p>");
    Assert(analysis.ConcernLevel == "none",
        "The neutral MTM-style service-coordination fixture produced a false positive.");
    return Task.CompletedTask;
}

static Task TestBoeingRemoteFixtureAsync()
{
    var detector = new RemoteWorkDetector();
    var analysis = detector.Analyze(
        "Senior Quality Auditor (Virtual)",
        "United States - Virtual",
        [],
        "<p>This position requires the ability to travel frequently to company sites, as needed.</p>" +
        "<p>The position may require travel up to 50% of the time.</p>");
    Assert(analysis.ConcernLevel == "strong" &&
           analysis.Signals.Any(signal => signal.Category == "frequent-travel") &&
           analysis.Signals.Any(signal => signal.Category == "substantial-travel"),
        "The sanitized Boeing virtual/frequent-travel fixture was not surfaced at the supported severity.");
    return Task.CompletedTask;
}

static Task TestExpandedRemoteTerminologyAsync()
{
    var detector = new RemoteWorkDetector();
    var amentum = detector.Analyze(
        "Supplier Excellence Lead", "United States", [],
        "<p>This position is remote-telework.</p><p>Occasional travel to operating sites.</p>");
    var kbr = detector.Analyze(
        "Risk Manager", "Houston, Texas", [],
        "<p>This position is based out of our headquarters and follows a remote work schedule.</p>");
    var conflicting = detector.Analyze(
        "Program Specialist", "United States", [],
        "<p>This is a US remote-telework role.</p><p>This role requires one day per week on-site.</p>");
    var serviceNowBoilerplate = detector.Analyze(
        "Software Engineer", "Mountain View, California", [],
        "<p>Work personas (flexible, remote, or required in office) are categories assigned depending on the nature of the work.</p>");

    Assert(amentum.IsRemoteDesignated && amentum.ConcernLevel == "none" &&
           kbr.IsRemoteDesignated && kbr.ConcernLevel == "none",
        "Explicit Amentum or KBR role-specific remote prose was not recognized.");
    Assert(conflicting.IsRemoteDesignated && conflicting.ConcernLevel == "strong" &&
           conflicting.Signals.Any(signal => signal.Category == "scheduled-onsite"),
        "An explicit remote role with scheduled onsite attendance did not produce a conflict.");
    Assert(!serviceNowBoilerplate.IsRemoteDesignated,
        "Generic ServiceNow work-persona boilerplate falsely designated an onsite posting as remote.");
    return Task.CompletedTask;
}

static Task TestExpandedSalaryTerminologyAsync()
{
    var cases = new[]
    {
        ("<p>Compensation Details: $100k - $121k. The listed amount is an annual estimate.</p>", 100_000m, 121_000m),
        ("<p>Basic Compensation: $127,000 - $158,600 annually.</p>", 127_000m, 158_600m),
        ("<p>The projected compensation range for this position is $69,400.00 to $158,000.00 (annualized USD).</p>", 69_400m, 158_000m)
    };
    foreach (var (fixture, minimum, maximum) in cases)
    {
        var salary = JobAnalysis.AnalyzeSalary(fixture);
        Assert(salary.Minimum == minimum && salary.Maximum == maximum && salary.Period == "annual",
            $"Expanded-corpus compensation wording did not parse: {salary.Minimum}-{salary.Maximum}/{salary.Period}.");
    }
    return Task.CompletedTask;
}

static Task TestBoeingSummaryPayRangesAsync()
{
    var parsedCases = new[]
    {
        ("level after heading", "<p>Summary pay range:</p><p>Level 3 (Experienced): $104,550 - $141,450</p>", 104_550m, 141_450m, "annual"),
        ("parenthetical level", "<p>Summary pay range (Experienced, Level 3): $104,550 - $141,450</p>", 104_550m, 141_450m, "annual"),
        ("plain range", "<p>Summary Pay Range: $184,450 - $249,550</p>", 184_450m, 249_550m, "annual"),
        ("decimal amounts", "<p>Summary pay range: Experienced Engineer: $96,050.00 - $129,950.00.</p>", 96_050m, 129_950m, "annual"),
        ("en dash", "<p>SUMMARY PAY RANGE: $120,000 – $160,000;</p>", 120_000m, 160_000m, "annual"),
        ("em dash", "<p>summary salary range: $130,000—$175,000.</p>", 130_000m, 175_000m, "annual"),
        ("compact hyphen", "<p>Summary Pay Range:$88,000-$112,000,</p>", 88_000m, 112_000m, "annual"),
        ("optional second dollar", "<p>Summary Pay Range: $91,500 - 123,500 USD.</p>", 91_500m, 123_500m, "annual"),
        ("optional dollar signs", "<p>Summary Pay Range: 97,750 - 132,250 USD.</p>", 97_750m, 132_250m, "annual"),
        ("for label", "<p>Summary pay range for Senior Engineer: $142,800 to $193,200.</p>", 142_800m, 193_200m, "annual"),
        ("bare level label", "<p>Summary pay range Level 3: $96,900 – $131,100.</p>", 96_900m, 131_100m, "annual"),
        ("slash level label", "<p>Summary Pay Range / level 2 - Associate: $106,250 - $143,750.</p>", 106_250m, 143_750m, "annual"),
        ("label without colon", "<p>Summary Pay Range for Associate Level $98,600 - $133,400.</p>", 98_600m, 133_400m, "annual"),
        ("location after heading", "<p>Summary pay range:</p><p>Tukwila, WA: $126,650 - $171,350.</p>", 126_650m, 171_350m, "annual"),
        ("hourly", "<p>Summary pay range: $42.25 - $58.75 per hour.</p>", 42.25m, 58.75m, "hourly")
    };
    foreach (var (name, fixture, minimum, maximum, period) in parsedCases)
    {
        var salary = JobAnalysis.AnalyzeSalary(fixture);
        Assert(salary.Minimum == minimum && salary.Maximum == maximum && salary.Period == period,
            $"Boeing {name} fixture parsed as {salary.Minimum}-{salary.Maximum}/{salary.Period} ({salary.ParseStatus}).");
    }

    var levels = JobAnalysis.AnalyzeSalary("""
        <p>Summary pay range for Mid-Level: $104,550 – $162,150</p>
        <p>Summary pay range for Senior: $127,500 – $197,800</p>
        <p>Summary pay range for Lead: $153,000 – $239,200</p>
        """);
    Assert(levels.Minimum == 104_550m && levels.Maximum == 239_200m &&
           levels.Period == "annual" && levels.ParseStatus == "summary-pay-range-aggregate",
        $"Boeing level bands did not aggregate to their safe outer bounds: {levels}.");

    var engineerLevels = JobAnalysis.AnalyzeSalary("""
        <p>Summary pay range for Experienced Engineer: $118,150.00 - $159,850.00.</p>
        <p>Summary pay range for Senior Engineer: $149,600.00 - $202,400.00.</p>
        """);
    Assert(engineerLevels.Minimum == 118_150m && engineerLevels.Maximum == 202_400m &&
           engineerLevels.Period == "annual" &&
           engineerLevels.ParseStatus == "summary-pay-range-aggregate",
        $"Boeing Experienced/Senior bands did not aggregate correctly: {engineerLevels}.");

    var sharedHeadingLevels = JobAnalysis.AnalyzeSalary("""
        <p>Summary pay range:</p>
        <p>Level 1: $76,500 - $112, 500</p>
        <p>Level 2: $90,950 - $133,750</p>
        """);
    Assert(sharedHeadingLevels.Minimum == 76_500m &&
           sharedHeadingLevels.Maximum == 133_750m &&
           sharedHeadingLevels.Period == "annual" &&
           sharedHeadingLevels.ParseStatus == "summary-pay-range-aggregate",
        $"Boeing ranges under one heading or with a spaced thousands separator failed: {sharedHeadingLevels}.");

    var geography = JobAnalysis.AnalyzeSalary("""
        <p>Summary Pay Range for Mesa, Arizona: $96,050 - $129,950</p>
        <p>Summary Pay Range for Berkeley, Missouri: $104,550 - $141,450</p>
        """);
    Assert(geography.Minimum == 96_050m && geography.Maximum == 141_450m &&
           geography.Period == "annual",
        $"Boeing geography bands did not retain a defensible overall range: {geography}.");

    var hourlyBands = JobAnalysis.AnalyzeSalary("""
        <p>Summary Pay Range for Grade A: $31.50 - $42.25 hourly</p>
        <p>Summary Pay Range for Grade B: $39.75 - $55.00 hourly</p>
        """);
    Assert(hourlyBands.Minimum == 31.50m && hourlyBands.Maximum == 55m &&
           hourlyBands.Period == "hourly" &&
           hourlyBands.ParseStatus == "hourly-unconverted-summary-aggregate",
        $"Hourly Boeing bands were annualized or aggregated incorrectly: {hourlyBands}.");

    var mixedPeriods = JobAnalysis.AnalyzeSalary("""
        <p>Summary Pay Range for salaried employees: $104,550 - $141,450 annually</p>
        <p>Summary Pay Range for hourly employees: $42.25 - $58.75 per hour</p>
        """);
    Assert(mixedPeriods.Minimum is null && mixedPeriods.Maximum is null &&
           mixedPeriods.Period == "unknown" && mixedPeriods.ParseStatus == "ambiguous-mixed-periods",
        $"Mixed annual/hourly Boeing bands produced a misleading combined range: {mixedPeriods}.");

    var unrelatedMoneyCases = new[]
    {
        "<p>Eligible for a $10,000 signing bonus and $5,000 relocation assistance.</p>",
        "<p>Benefits include up to $25,000 in tuition reimbursement.</p>",
        "<p>Annual incentive target is $20,000 - $30,000 and stock awards may apply.</p>",
        "<p>Summary bonus range: $15,000 - $25,000.</p>",
        "<p>Summary pay range depends on grade. For example, levels 3 - 5 require increasing experience.</p>"
    };
    foreach (var fixture in unrelatedMoneyCases)
    {
        var salary = JobAnalysis.AnalyzeSalary(fixture);
        Assert(salary.Minimum is null && salary.Maximum is null,
            $"Unrelated Boeing compensation was misidentified as base pay: {fixture} => {salary}.");
    }

    var noSalary = JobAnalysis.AnalyzeSalary(
        "<p>This position offers comprehensive benefits and professional development.</p>");
    Assert(noSalary.Minimum is null && noSalary.Maximum is null &&
           noSalary.ParseStatus == "not-found",
        $"A posting without salary language did not remain Unknown: {noSalary}.");

    var salaryWithIncentives = JobAnalysis.AnalyzeSalary("""
        <p>Summary Pay Range: $184,450 - $249,550.</p>
        <p>A $20,000 signing bonus and relocation assistance up to $8,000 may be available.</p>
        """);
    Assert(salaryWithIncentives.Minimum == 184_450m &&
           salaryWithIncentives.Maximum == 249_550m &&
           salaryWithIncentives.Period == "annual",
        $"Signing-bonus or relocation prose contaminated the labeled salary range: {salaryWithIncentives}.");

    var malformed = JobAnalysis.AnalyzeSalary(
        "<p>Summary pay range for Senior Engineer: $149,600 - pending approval.</p>");
    Assert(malformed.Minimum is null && malformed.Maximum is null &&
           malformed.ParseStatus == "unparseable",
        $"An incomplete Boeing pay range did not fail safely: {malformed}.");

    var regressions = new[]
    {
        ("Leidos", "<p>Pay Range: $101,400 - $183,300 annually.</p>", 101_400m, 183_300m),
        ("Northrop", "<p>Salary Range: $133,100 - $199,700 per year.</p>", 133_100m, 199_700m),
        ("Parsons", "<p>Minimum Annual Salary: $90,000. Maximum Annual Salary: $162,000.</p>", 90_000m, 162_000m),
        ("NVIDIA", "<p>The base salary range is 184,000 USD - 287,500 USD for this level.</p>", 184_000m, 287_500m)
    };
    foreach (var (source, fixture, minimum, maximum) in regressions)
    {
        var salary = JobAnalysis.AnalyzeSalary(fixture);
        Assert(salary.Minimum == minimum && salary.Maximum == maximum && salary.Period == "annual",
            $"{source} salary regression fixture parsed as {salary}.");
    }
    return Task.CompletedTask;
}

static Task TestExpandedCredentialTerminologyAsync()
{
    const string fixture = """
        <h2>Preferred Skills &amp; Certifications</h2>
        <p>CDPSE, AWS Certified Security, Azure Security Engineer, or Google Cloud Professional Security Engineer.</p>
        <p>Certifications in Systems Engineering such as INCOSE ESEP or CSEP are preferred.</p>
        <p>Illumio certifications such as Illumio Platform Associate are preferred.</p>
        <p>Acquisition Professional Development Program (APDP) Program Management Level II or higher Certification.</p>
        <p>The regulatory counsel must be a U.S.-licensed attorney with an active state bar license.</p>
        """;
    var analysis = new CredentialDetector(NullLogger<CredentialDetector>.Instance).Analyze(fixture);
    var ids = analysis.Credentials.Select(match => match.CredentialId).ToHashSet(StringComparer.Ordinal);
    var expected = new[]
    {
        "cdpse",
        "aws-security-specialty",
        "azure-security-engineer-associate",
        "google-cloud-security-engineer",
        "incose-esep",
        "incose-csep",
        "illumio-platform-certification",
        "apdp-program-management",
        "attorney-bar-license"
    };
    Assert(expected.All(ids.Contains),
        $"Expanded-corpus credential fixture recognized only: {string.Join(',', ids.Order())}.");
    return Task.CompletedTask;
}

static Task TestExtendedLocationPositiveAsync()
{
    var detector = new ExtendedLocationRequirementDetector();
    var cases = new Dictionary<string, string>
    {
        ["Job location is Guam."] = "Guam",
        ["Candidate must deploy to Antarctica for the austral summer season."] = "Antarctica",
        ["This is a 90-day rotational assignment in Kwajalein."] = "Kwajalein",
        ["Employee will be forward deployed to Germany for the duration of the contract."] = "Germany",
        ["Must relocate to Diego Garcia."] = "Diego Garcia",
        ["Employee will be forward deployed to Japan for the duration of the contract."] = "Japan",
        ["This position requires a temporary duty assignment in the Marshall Islands."] = "Marshall Islands",
        ["Ability and willingness to deploy to client and federal sites for extended periods."] = "Destination not specified"
    };

    foreach (var (text, expectedDestination) in cases)
    {
        var analysis = detector.Analyze(
            "Remote Program Specialist", "US - Remote (Any Location)", [], $"<p>{text}</p>");
        Assert(analysis.Confidence == "strong" && analysis.Signals.Count > 0 &&
               analysis.Destination == expectedDestination &&
               !string.IsNullOrWhiteSpace(analysis.Summary) &&
               analysis.Signals.Any(signal => !string.IsNullOrWhiteSpace(signal.Evidence)),
            $"Extended-location normalization failed: {text} => {analysis.Confidence}/{analysis.Destination}/{analysis.Summary}.");
    }
    var titleOnly = detector.Analyze(
        "Instructor — This position is located in Germany (International Assignment) NO REMOTE WORK",
        "US - Remote (Any Location)", [], "<p>Provide classroom instruction.</p>");
    Assert(titleOnly.Confidence == "strong",
        "An explicit international-assignment/no-remote title was not classified.");
    Assert(titleOnly.Destination == "Germany",
        $"Title-only destination was not normalized: {titleOnly.Destination}.");
    return Task.CompletedTask;
}

static Task TestExtendedLocationNegativeAsync()
{
    var detector = new ExtendedLocationRequirementDetector();
    var cases = new[]
    {
        "Our company supports customers in Guam, Germany, and Japan.",
        "Prior Guam experience preferred.",
        "Program operations include sites in Antarctica.",
        "Up to 10% international travel may be required.",
        "Experience supporting OCONUS customers preferred.",
        "Prior OCONUS assignment experience preferred.",
        "Previous experience in a 90-day rotational assignment is desirable.",
        "The candidate will support teams deployed throughout the Pacific.",
        "Must be able to travel to Guam occasionally.",
        "The platform is deployed to customer data centers worldwide.",
        "Relocation assistance is not available.",
        "Travel required throughout CONUS and some OCONUS travel driven by project needs."
    };

    foreach (var text in cases)
    {
        var analysis = detector.Analyze(
            "Program Analyst", "US - Remote (Any Location)", [], $"<p>{text}</p>");
        Assert(analysis.Confidence == "none",
            $"Incidental location or ordinary-travel text was classified: {text} => {analysis.Confidence}.");
        Assert(analysis.Destination is null,
            $"Incidental location produced a destination: {text} => {analysis.Destination}.");
    }
    return Task.CompletedTask;
}

static Task TestExtendedLocationMixedContextAsync()
{
    const string fixture = """
        <p>Our company supports customers in Guam, Germany, and Japan.</p>
        <p>Prior OCONUS experience is preferred and occasional international travel may be required.</p>
        <p>***job location is Guam***</p>
        """;
    var analysis = new ExtendedLocationRequirementDetector().Analyze(
        "UXO Technician", "US - VA, Centreville", [], fixture);
    Assert(analysis.Confidence == "strong" &&
           analysis.Destination == "Guam" &&
           analysis.Summary == "Job location is Guam" &&
           analysis.Signals.Any(signal => signal.Category == "explicit-job-location") &&
           analysis.Signals.Any(signal => signal.Evidence.Contains(
               "job location is Guam", StringComparison.OrdinalIgnoreCase)) &&
           analysis.Signals.All(signal => !signal.Evidence.Contains(
               "supports customers", StringComparison.OrdinalIgnoreCase)),
        "Mixed relevant and incidental location references did not preserve only useful evidence.");
    return Task.CompletedTask;
}

static Task TestExtendedLocationConfidenceAsync()
{
    var detector = new ExtendedLocationRequirementDetector();
    var questionable = detector.Analyze(
        "Electrical Engineer", "Remote / Teleworker US", [],
        "<p>Deployment to Antarctica may be necessary at the discretion of management.</p>");
    var strong = detector.Analyze(
        "Safety Engineer", "Remote / Teleworker US", [],
        "<p>Deployment to Antarctica is required in this role for seven months.</p>");
    Assert(questionable.Confidence == "questionable" && strong.Confidence == "strong",
        $"Confidence separation regressed: possible={questionable.Confidence}, required={strong.Confidence}.");
    return Task.CompletedTask;
}

static Task TestExtendedAwayAssignmentAsync()
{
    var catalog = new JobConceptCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var detector = new ExtendedLocationRequirementDetector();
    var conceptDetector = new JobConceptDetector(catalog);
    const string conceptId = "work.extended-away-assignment";

    var positiveCases = new[]
    {
        "The position will be onsite at the Customer Site in The Hague, Netherlands for 3 months to onboard.",
        "Must be willing and able to deploy internationally to Kazakhstan for assignments of up to 90 consecutive days.",
        "This winter-over assignment requires deployment to Antarctica for the austral winter.",
        "Employees work a required rotation of 6 weeks onsite and 6 weeks remote.",
        "The employee must reside aboard the ship for 8 weeks at sea.",
        "The role requires an initial 12-week customer-site onboarding assignment.",
        "Temporary duty in Guam is required for 60 days.",
        "Ability to travel on continuous travel assignments sometimes up to 5 months is required.",
        "Willing to travel to Wallops Island for around 10 months/year.",
        "This is a 100% long-term OCONUS assignment in Iraq."
    };

    foreach (var text in positiveCases)
    {
        var extended = detector.Analyze("Site Specialist", "United States", [], $"<p>{text}</p>");
        var detected = conceptDetector.Analyze("Site Specialist", "United States", [],
            $"<p>{text}</p>", null, extended);
        Assert(detected.Count(item => item.ConceptId == conceptId) == 1,
            $"Extended assignment concept was not detected exactly once: {text}");
        var evidence = detected.Single(item => item.ConceptId == conceptId).Evidence;
        Assert(text.Contains("winter-over", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("long-term", StringComparison.OrdinalIgnoreCase) ||
               System.Text.RegularExpressions.Regex.IsMatch(evidence, @"\b(?:\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)[- ]?(?:consecutive\s+)?(?:days?|weeks?|months?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            $"Extended assignment evidence omitted the qualifying duration: {evidence}");
    }

    var negativeCases = new[]
    {
        "This role requires 25% travel.",
        "Travel may be required up to 50%.",
        "Occasional international travel is expected.",
        "Visit the customer site for one week per month.",
        "Travel for one week per quarter is required.",
        "This hybrid role is onsite three days per week.",
        "Relocation is required with no temporary assignment duration.",
        "Travel destinations include Germany and Japan.",
        "Requires five years of experience and twelve months of project leadership.",
        "Requires a four-year degree and a certification earned within the last three years.",
        "The project duration is 18 months.",
        "Must remain at a workstation for extended periods.",
        "A passport valid for 12 months is required.",
        "Possess a passport valid for 12 months after employment start and be able to travel internationally.",
        "Deployment to Antarctica may be necessary for approximately 6 months at management discretion."
    };

    foreach (var text in negativeCases)
    {
        var extended = detector.Analyze("Program Analyst", "United States", [], $"<p>{text}</p>");
        var detected = conceptDetector.Analyze("Program Analyst", "United States", [],
            $"<p>{text}</p>", null, extended);
        Assert(detected.All(item => item.ConceptId != conceptId),
            $"Ordinary, incidental, or conditional language produced the extended assignment concept: {text}");
    }

    var overlapText = "This is a 90-day rotational assignment in Guam and the employee must deploy.";
    var overlapExtended = detector.Analyze("Field Engineer", "United States", [], $"<p>{overlapText}</p>");
    var overlap = conceptDetector.Analyze("Field Engineer", "United States", [],
        $"<p>{overlapText}</p>", null, overlapExtended);
    Assert(overlap.Count(item => item.ConceptId == conceptId) == 1 &&
           overlap.Any(item => item.ConceptId == "work.deployment") &&
           overlap.Any(item => item.ConceptId == "work.rotation") &&
           (catalog.Get(conceptId).Supersedes?.Count ?? 0) == 0,
        "Overlapping deployment/rotation signals were duplicated or incorrectly superseded.");
    Assert(ExtendedLocationRequirementDetector.CurrentAnalysisVersion == 4 && catalog.Version == 9,
        "Extended assignment changes did not invalidate cached classification versions.");
    return Task.CompletedTask;
}

static Task TestExpandedCompanyFixturesAsync()
{
    const string northrop = """
        <p>This position is remote; candidates will need to be within a commutable distance from one of the listed company locations.</p>
        <p>Bachelor's degree with eight years of experience or a master's degree with six years of experience.</p>
        <p>U.S. citizen. Ability to obtain and maintain a Secret clearance. Travel up to 75%.</p>
        """;
    var northropRemote = RemoteAnalysis(northrop);
    var northropAcademic = new AcademicQualificationDetector().Analyze(northrop);
    Assert(northropRemote.ConcernLevel == "strong" &&
           northropRemote.Signals.Any(signal => signal.Category == "commuting-area") &&
           northropRemote.Signals.Any(signal => signal.Category == "substantial-travel") &&
           new WorkAuthorizationDetector().Analyze(northrop).Eligibility == "usCitizen" &&
           JobAnalysis.AnalyzeClearance(northrop).Level == "secret" &&
           northropAcademic.MinimumLevel == "bachelor",
        $"Northrop fixture: remote={northropRemote.ConcernLevel} [{string.Join(',', northropRemote.Signals.Select(signal => signal.Category))}], authorization={new WorkAuthorizationDetector().Analyze(northrop).Eligibility}, clearance={JobAnalysis.AnalyzeClearance(northrop).Level}, academic={northropAcademic.MinimumLevel}.");

    var nvidiaSalary = JobAnalysis.AnalyzeSalary(
        "<p>The base salary range is 184,000 USD - 287,500 USD for this level.</p>");
    Assert(nvidiaSalary.Minimum == 184000m && nvidiaSalary.Maximum == 287500m &&
           nvidiaSalary.Period == "annual",
        "The sanitized NVIDIA USD salary format was not parsed.");

    const string parsons = """
        <p>Professional Registration (PE) and Project Management Professional (PMP) certification are required.</p>
        <p>Must be a U.S. Citizen and able to pass required federal background checks. Security Clearance Requirement: None.</p>
        """;
    var parsonsCredentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance)
        .Analyze(parsons).Credentials;
    Assert(parsonsCredentials.Any(item => item.CredentialId == "pe") &&
           parsonsCredentials.Any(item => item.CredentialId == "pmp") &&
           JobAnalysis.AnalyzeClearance(parsons).Level == "noneMentioned",
        $"Parsons fixture credentials={string.Join(',', parsonsCredentials.Select(item => item.CredentialId))}, clearance={JobAnalysis.AnalyzeClearance(parsons).Level}/{JobAnalysis.AnalyzeClearance(parsons).Requirement}.");

    const string aecom = """
        <p>This role can work remotely within the Eastern Time Zone. Candidates must be authorized to work in the United States without current or future sponsorship.</p>
        """;
    Assert(new WorkAuthorizationDetector().Analyze(aecom).Sponsorship == "notAvailable" &&
           JobAnalysis.AnalyzeRemoteLocation(aecom, "Remote", []).IsRestricted,
        $"AECOM fixture sponsorship={new WorkAuthorizationDetector().Analyze(aecom).Sponsorship}, locationRestricted={JobAnalysis.AnalyzeRemoteLocation(aecom, "Remote", []).IsRestricted}/{JobAnalysis.AnalyzeRemoteLocation(aecom, "Remote", []).Category}.");

    const string rtx = """
        <p>This is a remote position. If you live within a reasonable commute of a company site, your manager will discuss whether there is onsite presence associated with this role.</p>
        <p>U.S. citizenship is required, as only U.S. citizens are authorized to access program information.</p>
        """;
    var rtxRemote = RemoteAnalysis(rtx);
    Assert(rtxRemote.ConcernLevel == "questionable" &&
           rtxRemote.Signals.Any(signal => signal.Category == "commuting-area") &&
           new WorkAuthorizationDetector().Analyze(rtx).Eligibility == "usCitizen",
        "The sanitized RTX remote/citizenship fixture regressed.");
    return Task.CompletedTask;
}

static async Task TestWorkflowStateTransitionsAsync()
{
    var directory = TestDirectory("workflow-transitions");
    try
    {
        var (catalog, state, job) = await CreateCatalogAsync(directory);
        Assert(catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Normal,
            "A new job did not begin in Normal state.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Saved,
            "Normal -> Saved did not update the canonical state.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied,
            "Saved -> Applied did not update the canonical state.");
        var appliedAt = (await state.LoadJobHistoryAsync()).Jobs[job.StableId].AppliedAt;
        Assert(appliedAt is not null,
            "Applying a job did not record its original application timestamp.");
        Assert(!await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied,
            "An invalid Applied -> Saved transition was accepted.");
        Assert(!await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Closed) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied,
            "A close transition without a canonical reason was accepted.");
        Assert(await catalog.SetWorkflowStateAsync(
                   job.StableId, JobWorkflowStates.Closed, JobCloseReasons.NotSelected) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Closed &&
               catalog.Snapshot.JobClosures[job.StableId].Reason == JobCloseReasons.NotSelected,
            "Applied -> Closed did not persist and expose its canonical close reason.");
        var closed = (await state.LoadJobHistoryAsync()).Jobs[job.StableId];
        Assert(closed.CloseReason == JobCloseReasons.NotSelected &&
               closed.ClosedAt is not null && closed.AppliedAt == appliedAt,
            "Closing an application did not retain AppliedAt or persist closure metadata.");
        Assert(!await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Hidden),
            "Closed state incorrectly transitioned directly to Hidden.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Applied &&
               !catalog.Snapshot.JobClosures.ContainsKey(job.StableId),
            "Reopening did not return Closed to Applied or clear the active closure.");
        var reopened = (await state.LoadJobHistoryAsync()).Jobs[job.StableId];
        Assert(reopened.CloseReason is null && reopened.ClosedAt is null && reopened.AppliedAt == appliedAt,
            "Reopening did not clear close reason/timestamp while retaining original AppliedAt.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Hidden) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Hidden,
            "Applied -> Hidden did not update the canonical state.");
        Assert(!await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied),
            "An invalid Hidden -> Applied transition was accepted.");
        Assert(await catalog.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Normal) &&
               catalog.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Normal,
            "Hidden -> Normal restore did not update the canonical state.");

        var persisted = (await state.LoadJobHistoryAsync()).Jobs[job.StableId];
        Assert(persisted.WorkflowState == JobWorkflowStates.Normal &&
               !persisted.Dismissed && !persisted.Saved && !persisted.Applied &&
               persisted.DismissedAt is null && persisted.SavedAt is null &&
               persisted.AppliedAt is null && persisted.CloseReason is null && persisted.ClosedAt is null,
            "Persistence retained an independent legacy state alongside the canonical state.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestWorkflowStateRoundTripAsync()
{
    var directory = TestDirectory("workflow-roundtrip");
    try
    {
        var (first, _, job) = await CreateCatalogAsync(directory);
        await first.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Applied);
        await first.SetWorkflowStateAsync(
            job.StableId, JobWorkflowStates.Closed, JobCloseReasons.InterviewedOut);
        var (restarted, _, _) = await CreateCatalogAsync(directory);
        Assert(restarted.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Closed &&
               restarted.Snapshot.JobClosures[job.StableId] is
               { Reason: JobCloseReasons.InterviewedOut, AppliedAt: not null },
            "Closed state, reason, timestamp, or original applied date did not survive catalog reinitialization.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestWorkflowStateWorkspaceIsolationAsync()
{
    var firstDirectory = TestDirectory("workflow-workspace-a");
    var secondDirectory = TestDirectory("workflow-workspace-b");
    try
    {
        var (first, _, job) = await CreateCatalogAsync(firstDirectory);
        await first.SetWorkflowStateAsync(job.StableId, JobWorkflowStates.Saved);
        var (second, _, _) = await CreateCatalogAsync(secondDirectory);
        Assert(first.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Saved &&
               second.Snapshot.JobStates[job.StableId] == JobWorkflowStates.Normal,
            "Workflow state crossed workspace storage boundaries.");
    }
    finally
    {
        if (Directory.Exists(firstDirectory)) Directory.Delete(firstDirectory, recursive: true);
        if (Directory.Exists(secondDirectory)) Directory.Delete(secondDirectory, recursive: true);
    }
}

static async Task TestWorkflowStateCompanyIsolationAsync()
{
    var directory = TestDirectory("workflow-company");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        var history = new JobHistoryDocument(4, new Dictionary<string, JobHistoryEntry>
        {
            ["leidos:REQ-SAME"] = new("REQ-SAME", "/leidos/job", now, now, true,
                WorkflowState: JobWorkflowStates.Saved, WorkflowStateChangedAt: now,
                CompanyId: "leidos"),
            ["boeing:REQ-SAME"] = new("REQ-SAME", "/boeing/job", now, now, true,
                WorkflowState: JobWorkflowStates.Applied, WorkflowStateChangedAt: now,
                CompanyId: "boeing")
        });
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, history);
        var state = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);
        var loaded = await state.LoadJobHistoryAsync();
        Assert(loaded.Jobs["leidos:REQ-SAME"].WorkflowState == JobWorkflowStates.Saved &&
               loaded.Jobs["boeing:REQ-SAME"].WorkflowState == JobWorkflowStates.Applied,
            "Same requisition text cross-mapped workflow state between companies.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacySavedHistoryMigrationAsync()
{
    var directory = TestDirectory("workflow-legacy-saved");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, new JobHistoryDocument(3,
            new Dictionary<string, JobHistoryEntry>
            {
                ["REQ-LEGACY"] = new(
                    "REQ-LEGACY", "/legacy/job", now.AddDays(-3), now, false,
                    CompanyId: "leidos", Saved: true, SavedAt: now.AddHours(-1))
            }));
        var appState = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var migrated = await appState.LoadJobHistoryAsync();
        var entry = migrated.Jobs["leidos:REQ-LEGACY"];
        Assert(migrated.SchemaVersion == 5 && entry.WorkflowState == JobWorkflowStates.Saved,
            "Legacy Saved state did not migrate to canonical Saved.");
        Assert(!entry.HasBeenViewed && entry.FirstSeenAt == now.AddDays(-3) && entry.LastSeenAt == now,
            "Migration changed existing NEW/viewed history timestamps.");
        Assert(!entry.Dismissed && !entry.Saved && !entry.Applied &&
               entry.DismissedAt is null && entry.SavedAt is null && entry.AppliedAt is null &&
               entry.CloseReason is null && entry.ClosedAt is null,
            "Legacy state fields survived schema-5 migration.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static async Task TestLegacyCombinationMigrationAsync()
{
    var directory = TestDirectory("workflow-legacy-combinations");
    try
    {
        var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        await store.WriteJsonAsync(WorkspaceDataFile.JobHistory, new JobHistoryDocument(3,
            new Dictionary<string, JobHistoryEntry>
            {
                ["leidos:REQ-HIDDEN"] = new(
                    "REQ-HIDDEN", "/hidden/job", now, now, true,
                    Dismissed: true, DismissedAt: now, CompanyId: "leidos",
                    Saved: true, SavedAt: now, Applied: true, AppliedAt: now),
                ["leidos:REQ-APPLIED"] = new(
                    "REQ-APPLIED", "/applied/job", now, now, true,
                    CompanyId: "leidos", Saved: true, SavedAt: now,
                    Applied: true, AppliedAt: now),
                ["leidos:REQ-STALE-TIMESTAMP"] = new(
                    "REQ-STALE-TIMESTAMP", "/normal/job", now, now, true,
                    CompanyId: "leidos", SavedAt: now)
            }));
        var appState = new AppStateStore(
            NullLogger<AppStateStore>.Instance,
            new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory)),
            store);

        var migrated = await appState.LoadJobHistoryAsync();
        Assert(migrated.Jobs["leidos:REQ-HIDDEN"].WorkflowState == JobWorkflowStates.Hidden,
            "Dismissed+Saved+Applied did not migrate to the single safe Hidden state.");
        Assert(migrated.Jobs["leidos:REQ-APPLIED"].WorkflowState == JobWorkflowStates.Applied,
            "Saved+Applied did not migrate to the single Applied state.");
        Assert(migrated.Jobs["leidos:REQ-STALE-TIMESTAMP"].WorkflowState == JobWorkflowStates.Normal,
            "A stray legacy timestamp changed the canonical Normal state.");
        Assert(migrated.Jobs.Values.All(entry =>
                !entry.Dismissed && !entry.Saved && !entry.Applied &&
                entry.DismissedAt is null && entry.SavedAt is null) &&
               migrated.Jobs["leidos:REQ-APPLIED"].AppliedAt == now &&
               migrated.Jobs["leidos:REQ-HIDDEN"].AppliedAt is null &&
               migrated.Jobs.Values.All(entry => entry.CloseReason is null && entry.ClosedAt is null),
            "An invalid independent-state combination survived migration.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static RemoteWorkAnalysis RemoteAnalysis(string html) => new RemoteWorkDetector().Analyze(
    "Software Engineer", "Remote / Teleworker US", [], html);

static string ExpandCachedDescription(string encoded)
{
    var bytes = Convert.FromBase64String(encoded);
    using var input = new MemoryStream(bytes);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
    return reader.ReadToEnd();
}

static (CompanyDefinition Company, JobSourceQuery Query) WorkdaySource()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var company = companies.Get("leidos");
    return (company, new JobSourceQuery(
        FacetDefaults.UnitedStatesCountryId,
        FacetDefaults.UnitedStatesCountryLabel,
        true,
        true,
        [],
        CompanyId: company.Id));
}

static (CompanyDefinition Company, JobSourceQuery Query) BoeingSource()
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var company = companies.Get("boeing");
    return (company, new JobSourceQuery(
        FacetDefaults.UnitedStatesCountryId,
        FacetDefaults.UnitedStatesCountryLabel,
        true,
        true,
        [],
        CompanyId: company.Id));
}

static InstrumentedHttpMessageHandler CreateWorkdayHandler(
    Func<int> listingCount,
    Func<int, string>? title = null,
    int delayMilliseconds = 0,
    Func<int, string>? description = null) => new(async (request, cancellationToken) =>
{
    if (request.Method == HttpMethod.Get)
    {
        var idText = request.RequestUri!.AbsolutePath.Split('/').Last();
        var index = int.TryParse(idText, out var parsed) ? parsed : 0;
        return JsonSerializer.Serialize(new
        {
            jobPostingInfo = new
            {
                title = title?.Invoke(index) ?? $"Engineer {index}",
                jobReqId = $"REQ-{index:D4}",
                location = "Remote / Teleworker US",
                additionalLocations = Array.Empty<string>(),
                startDate = "2026-08-15",
                postedOn = "Posted Today",
                timeType = "Full time",
                jobDescription = description?.Invoke(index) ??
                    $"<p>Cached description {index}. Bachelor's degree. US citizenship required.</p>",
                externalUrl = $"https://example.test/job/{index}"
            }
        });
    }

    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
    using var payload = JsonDocument.Parse(body);
    var offset = payload.RootElement.GetProperty("offset").GetInt32();
    var count = listingCount();
    var postings = Enumerable.Range(offset, Math.Max(0, Math.Min(20, count - offset)))
        .Select(index => new
        {
            title = title?.Invoke(index) ?? $"Engineer {index}",
            externalPath = $"/job/{index}",
            locationsText = "Remote / Teleworker US",
            postedOn = "Posted Today",
            bulletFields = new[] { $"REQ-{index:D4}" }
        })
        .ToArray();
    return JsonSerializer.Serialize(new
    {
        total = count,
        jobPostings = postings,
        facets = Array.Empty<object>()
    });
}, delayMilliseconds);

static JobRecord CachedJob(string companyId, string requisitionId, string path, string description) => new(
    "Cached Engineer",
    requisitionId,
    new DateOnly(2026, 8, 15),
    "Posted Today",
    "Remote / Teleworker US",
    [],
    "Full time",
    $"https://example.test{path}",
    description,
    null,
    null,
    "unknown",
    "not-found",
    false,
    null,
    null,
    null,
    path,
    CompanyId: companyId,
    DetailCachedAtUtc: DateTimeOffset.UtcNow,
    AnalysisVersion: JobSourceClient.CurrentAnalysisVersion);

static async Task<(JobCatalog Catalog, FileWorkspaceDataStore Store, AppStateStore State)> CreateTestCatalogAsync(
    string directory,
    HttpMessageHandler handler,
    IReadOnlyList<JobRecord> cachedJobs,
    JobSourceQuery query,
    bool seedCache = true,
    SharedSourceRefreshCoordinator? coordinator = null)
{
    var companies = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
    var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companies, store);
    if (seedCache)
    {
        await state.SaveJobsCacheAsync(cachedJobs, DateTimeOffset.UtcNow, 0, query);
    }
    var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var academics = new AcademicQualificationDetector();
    var authorization = new WorkAuthorizationDetector();
    var remote = new RemoteWorkDetector();
    var client = new JobSourceClient(
        new HttpClient(handler),
        Options.Create(new JobSourceOptions()),
        NullLogger<JobSourceClient>.Instance,
        credentials,
        academics,
        authorization,
        remote,
        new ExtendedLocationRequirementDetector());
    var catalog = new JobCatalog(
        client,
        state,
        NullLogger<JobCatalog>.Instance,
        credentials,
        academics,
        authorization,
        remote,
        companies,
        Options.Create(new JobSourceOptions()),
        coordinator);
    await catalog.InitializeAsync(query);
    return (catalog, store, state);
}

static JobSourceClient CreateSourceClient(
    HttpClient httpClient,
    JobSourceOptions? options = null)
{
    var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    return new JobSourceClient(
        httpClient,
        Options.Create(options ?? new JobSourceOptions()),
        NullLogger<JobSourceClient>.Instance,
        credentials,
        new AcademicQualificationDetector(),
        new WorkAuthorizationDetector(),
        new RemoteWorkDetector(),
        new ExtendedLocationRequirementDetector());
}

static string TestDirectory(string purpose) =>
    Path.Combine(Path.GetTempPath(), $"job-search-manager-{purpose}-{Guid.NewGuid():N}");

static async Task<(JobCatalog Catalog, AppStateStore State, JobRecord Job)> CreateCatalogAsync(
    string directory)
{
    var companyCatalog = new CompanyCatalog(new TestHostEnvironment(AppContext.BaseDirectory));
    var store = new FileWorkspaceDataStore(directory, NullLogger<FileWorkspaceDataStore>.Instance);
    var state = new AppStateStore(NullLogger<AppStateStore>.Instance, companyCatalog, store);
    var job = new JobRecord(
        "Test Engineer",
        "REQ-SAVED",
        new DateOnly(2026, 8, 15),
        "Posted Today",
        "Remote / Teleworker US",
        [],
        "Full time",
        "https://leidos.wd5.myworkdayjobs.com/en-US/External/job/REQ-SAVED",
        "<p>Fully remote software engineering role.</p>",
        null,
        null,
        "unknown",
        "not-found",
        false,
        null,
        null,
        null,
        "/job/REQ-SAVED",
        CompanyId: "leidos");
    var query = new JobSourceQuery(
        "bc33aa3152ec42d4995f4791a106ed09",
        "United States of America",
        false,
        true,
        []);
    await state.SaveJobsCacheAsync([job], DateTimeOffset.UtcNow, 0, query);

    var credentials = new CredentialDetector(NullLogger<CredentialDetector>.Instance);
    var academics = new AcademicQualificationDetector();
    var authorization = new WorkAuthorizationDetector();
    var remote = new RemoteWorkDetector();
    var client = new JobSourceClient(
        new HttpClient(),
        Options.Create(new JobSourceOptions()),
        NullLogger<JobSourceClient>.Instance,
        credentials,
        academics,
        authorization,
        remote,
        new ExtendedLocationRequirementDetector());
    var catalog = new JobCatalog(
        client,
        state,
        NullLogger<JobCatalog>.Instance,
        credentials,
        academics,
        authorization,
        remote,
        companyCatalog,
        Options.Create(new JobSourceOptions()));
    await catalog.InitializeAsync(query);
    return (catalog, state, job);
}

static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
    new ConfigurationBuilder().AddInMemoryCollection(values).Build();

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

static (AccountService Service, MemoryAccountRegistryStore Store,
    TestAccountEmailSender Email, ManualTimeProvider Time) TestAccountService()
{
    var store = new MemoryAccountRegistryStore();
    var email = new TestAccountEmailSender();
    var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero));
    var service = new AccountService(
        store, new PasswordHasher<AccountRecord>(), email, time,
        NullLogger<AccountService>.Instance);
    return (service, store, email, time);
}

internal sealed record TestDocument(string Name, int Value);

internal sealed class TestWorkspaceDataStoreFactory(string root) : IWorkspaceDataStoreFactory
{
    public List<string> CreatedWorkspaceIds { get; } = [];

    public IWorkspaceDataStore Create(string workspaceId)
    {
        CreatedWorkspaceIds.Add(workspaceId);
        return new FileWorkspaceDataStore(
            Path.Combine(root, workspaceId), NullLogger<FileWorkspaceDataStore>.Instance);
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class ManualTimeProvider(DateTimeOffset value) : TimeProvider
{
    private DateTimeOffset _value = value;
    public override DateTimeOffset GetUtcNow() => _value;
    public void Advance(TimeSpan duration) => _value = _value.Add(duration);
}

internal sealed class TestAccountEmailSender : IAccountEmailSender
{
    public bool IsConfigured => true;
    public string? VerificationToken { get; private set; }
    public string? ResetToken { get; private set; }
    public int ResetMessages { get; private set; }
    public bool FailDelivery { get; set; }

    public Task SendVerificationAsync(
        string email, Uri link, CancellationToken cancellationToken = default)
    {
        if (FailDelivery) throw new SmtpException("Simulated provider failure.");
        VerificationToken = TokenFromFragment(link, "verifyEmailToken");
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string email, Uri link, CancellationToken cancellationToken = default)
    {
        if (FailDelivery) throw new SmtpException("Simulated provider failure.");
        ResetToken = TokenFromFragment(link, "resetToken");
        ResetMessages++;
        return Task.CompletedTask;
    }

    private static string TokenFromFragment(Uri link, string key)
    {
        var fragment = link.Fragment.TrimStart('#');
        var parts = fragment.Split('=', 2);
        if (parts.Length != 2 || parts[0] != key)
            throw new InvalidOperationException("Account email link did not contain the expected fragment token.");
        return Uri.UnescapeDataString(parts[1]);
    }
}

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, string> responseFactory)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(
                responseFactory(request),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        return Task.FromResult(response);
    }
}

internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("Simulated classifier outage.");
}

internal sealed class InstrumentedHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<string>> responseFactory,
    int delayMilliseconds = 0) : HttpMessageHandler
{
    private int _activeRequests;
    private int _maximumConcurrentRequests;
    private int _detailRequests;
    private int _listingRequests;

    public int DetailRequests => Volatile.Read(ref _detailRequests);
    public int ListingRequests => Volatile.Read(ref _listingRequests);
    public int MaximumConcurrentRequests => Volatile.Read(ref _maximumConcurrentRequests);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
        {
            Interlocked.Increment(ref _detailRequests);
        }
        else
        {
            Interlocked.Increment(ref _listingRequests);
        }
        var active = Interlocked.Increment(ref _activeRequests);
        while (true)
        {
            var maximum = Volatile.Read(ref _maximumConcurrentRequests);
            if (active <= maximum || Interlocked.CompareExchange(
                ref _maximumConcurrentRequests, active, maximum) == maximum) break;
        }
        try
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
            var json = await responseFactory(request, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
        }
    }
}

internal sealed class TestHostEnvironment(string contentRootPath) : Microsoft.Extensions.Hosting.IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "JobSearchManager.Tests";
    public string ContentRootPath { get; set; } = contentRootPath;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
}
