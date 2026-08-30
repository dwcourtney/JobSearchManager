using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace JobSearchManager;

public sealed class JobSourceClient
{
    public const int CurrentAnalysisVersion = 6;

    private sealed record ListingBatch(IReadOnlyList<ListingPosting> Listings, bool Truncated);
    private sealed record SmartSummaryBatch(IReadOnlyList<SmartRecruitersPosting> Postings, bool Truncated);
    private sealed record DetailCandidate(int Index, ListingPosting Listing, JobRecord? Cached, bool Revalidation);

    private readonly HttpClient _httpClient;
    private readonly JobSourceOptions _options;
    private readonly ILogger<JobSourceClient> _logger;
    private readonly CredentialDetector _credentialDetector;
    private readonly AcademicQualificationDetector _academicQualificationDetector;
    private readonly WorkAuthorizationDetector _workAuthorizationDetector;
    private readonly RemoteWorkDetector _remoteWorkDetector;
    private readonly ExtendedLocationRequirementDetector _extendedLocationRequirementDetector;
    private readonly JobConceptDetector _jobConceptDetector;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public JobSourceClient(
        HttpClient httpClient,
        IOptions<JobSourceOptions> options,
        ILogger<JobSourceClient> logger,
        CredentialDetector credentialDetector,
        AcademicQualificationDetector academicQualificationDetector,
        WorkAuthorizationDetector workAuthorizationDetector,
        RemoteWorkDetector remoteWorkDetector,
        ExtendedLocationRequirementDetector extendedLocationRequirementDetector,
        JobConceptDetector? jobConceptDetector = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialDetector = credentialDetector;
        _academicQualificationDetector = academicQualificationDetector;
        _workAuthorizationDetector = workAuthorizationDetector;
        _remoteWorkDetector = remoteWorkDetector;
        _extendedLocationRequirementDetector = extendedLocationRequirementDetector;
        _jobConceptDetector = jobConceptDetector ?? JobConceptDetector.CreateDefault();

        if (_options.PageSize is < 1 or > 20)
        {
            throw new InvalidOperationException("JobSource:PageSize must be between 1 and 20.");
        }

        if (_options.DetailConcurrency is < 1 or > 20)
        {
            throw new InvalidOperationException("JobSource:DetailConcurrency must be between 1 and 20.");
        }
        if (_options.MaximumListingPages is < 1 or > 1000 ||
            _options.MaximumDetailRequestsPerRefresh is < 1 or > 2000 ||
            _options.MaximumRevalidationsPerRefresh is < 0 or > 500 ||
            _options.DetailReuseHours is < 1 or > 8760 ||
            _options.SourceSwitchCacheFreshnessMinutes is < 1 or > 120)
        {
            throw new InvalidOperationException("Job-source safety limits are outside their supported ranges.");
        }
    }

    public async Task<JobSourceFetchResult> FetchAllJobsAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        Action<RefreshProgress>? reportProgress = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<JobRecord>? cachedJobs = null,
        ViewerSettings? filterSettings = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var batch = await FetchListingsAsync(company, query, reportProgress, cancellationToken);
        var listings = batch.Listings;
        cachedJobs ??= [];
        var cachedByPath = cachedJobs
            .Where(job => !string.IsNullOrWhiteSpace(job.ExternalPath))
            .GroupBy(job => job.ExternalPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var jobs = new JobRecord?[listings.Count];
        var candidates = new List<DetailCandidate>();
        var detailFailureCount = 0;
        var cacheHits = 0;
        var cacheMisses = 0;
        var reclassified = 0;
        var classified = 0;
        var completedDetails = 0;
        var currentPaths = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < listings.Count; index++)
        {
            var listing = listings[index];
            currentPaths.Add(listing.ExternalPath);
            foreach (var equivalentPath in listing.EquivalentExternalPaths)
            {
                currentPaths.Add(equivalentPath);
            }
            cachedByPath.TryGetValue(listing.ExternalPath, out var cached);
            cached ??= listing.EquivalentExternalPaths
                .Select(path => cachedByPath.GetValueOrDefault(path))
                .FirstOrDefault(job => job is not null);
            var fingerprint = ListingFingerprint(listing);
            if (cached is not null && !string.IsNullOrWhiteSpace(cached.DescriptionHtml) &&
                (string.IsNullOrWhiteSpace(cached.ListingFingerprint) ||
                 string.Equals(cached.ListingFingerprint, fingerprint, StringComparison.Ordinal)))
            {
                var reusable = MergeListing(company, listing, cached, fingerprint);
                if (!IsAnalysisCurrent(reusable))
                {
                    reusable = Reclassify(reusable);
                    reclassified++;
                }
                jobs[index] = reusable;
                cacheHits++;
                if (ShouldRevalidate(reusable))
                {
                    candidates.Add(new DetailCandidate(index, listing, reusable, true));
                }
            }
            else
            {
                cacheMisses++;
                var materiallyChanged = cached?.ListingFingerprint is { Length: > 0 } previous &&
                    !string.Equals(previous, fingerprint, StringComparison.Ordinal);
                jobs[index] = cached is not null && !materiallyChanged
                    ? MergeListing(company, listing, cached, fingerprint)
                    : Normalize(company, listing, null, null, fingerprint);
                candidates.Add(new DetailCandidate(index, listing, cached, false));
            }
        }

        var detailLimit = _options.MaximumDetailRequestsPerRefresh;
        var selectedCandidates = candidates
            .Where(candidate => !candidate.Revalidation)
            .OrderByDescending(candidate => IsSafePreliminaryMatch(candidate.Listing, filterSettings))
            .Take(detailLimit)
            .Concat(candidates
                .Where(candidate => candidate.Revalidation)
                .OrderBy(candidate => candidate.Cached?.DetailCachedAtUtc)
                .Take(Math.Min(
                    _options.MaximumRevalidationsPerRefresh,
                    Math.Max(0, detailLimit - candidates.Count(candidate => !candidate.Revalidation)))))
            .ToArray();

        reportProgress?.Invoke(new RefreshProgress("details", 0, selectedCandidates.Length));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.DetailConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(selectedCandidates, parallelOptions, async (candidate, token) =>
        {
            try
            {
                var detail = await FetchDetailAsync(company, candidate.Listing.ExternalPath, token);
                jobs[candidate.Index] = Normalize(
                    company,
                    candidate.Listing,
                    detail,
                    null,
                    ListingFingerprint(candidate.Listing));
                Interlocked.Increment(ref classified);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref detailFailureCount);
                _logger.LogWarning(
                    ex,
                    "Could not retrieve details for {Title} ({ExternalPath}); retaining listing metadata.",
                    candidate.Listing.Title,
                    candidate.Listing.ExternalPath);
                jobs[candidate.Index] = candidate.Cached is not null &&
                    !string.IsNullOrWhiteSpace(candidate.Cached.DescriptionHtml)
                        ? MergeListing(
                            company,
                            candidate.Listing,
                            candidate.Cached with { DetailError = ex.Message },
                            ListingFingerprint(candidate.Listing))
                        : Normalize(
                            company,
                            candidate.Listing,
                            null,
                            ex.Message,
                            ListingFingerprint(candidate.Listing));
            }
            finally
            {
                var completed = Interlocked.Increment(ref completedDetails);
                reportProgress?.Invoke(new RefreshProgress("details", completed, selectedCandidates.Length));
            }
        });

        reportProgress?.Invoke(new RefreshProgress("finalizing", listings.Count, listings.Count));

        var removed = cachedJobs
            .Where(job => !currentPaths.Contains(job.ExternalPath))
            .Select(job => job with { IsSourceAvailable = false })
            .ToArray();

        var sorted = jobs
            .Where(job => job is not null)
            .Select(job => job!)
            .Concat(removed)
            .OrderByDescending(job => job.IsSourceAvailable)
            .ThenByDescending(job => job.StartDate)
            .ThenBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.RequisitionId, StringComparer.Ordinal)
            .ToArray();
        stopwatch.Stop();
        var metrics = new RefreshMetrics(
            listings.Count,
            selectedCandidates.Length,
            cacheHits,
            cacheMisses,
            classified,
            reclassified,
            Math.Max(0, candidates.Count - selectedCandidates.Length),
            removed.Length,
            batch.Truncated,
            stopwatch.ElapsedMilliseconds);
        _logger.LogInformation(
            "Refresh metrics: {Listings} listings, {DetailRequests} detail requests, {CacheHits} cache hits, {CacheMisses} cache misses, {Reclassified} local reclassifications, {Deferred} deferred details, {Removed} removed listings, {ElapsedMs} ms.",
            metrics.ListingsFetched,
            metrics.DetailRequests,
            metrics.CacheHits,
            metrics.CacheMisses,
            metrics.ReclassifiedLocally,
            metrics.DeferredDetails,
            metrics.RemovedListings,
            metrics.ElapsedMilliseconds);

        return new JobSourceFetchResult(sorted, listings.Count, detailFailureCount, metrics);
    }

    public async Task<IReadOnlyList<ListingIdentity>> FetchListingIdentitiesAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        var batch = await FetchListingsAsync(company, query, null, cancellationToken);
        return batch.Listings.Select(listing =>
        {
            var requisitionId = listing.BulletFields.FirstOrDefault() ?? "";
            var stableId = !string.IsNullOrWhiteSpace(requisitionId)
                ? $"{company.Id}:{requisitionId}"
                : $"{company.Id}:path:{listing.ExternalPath}";
            if (!string.IsNullOrWhiteSpace(listing.IdentityDiscriminator))
            {
                stableId += $":variant:{listing.IdentityDiscriminator}";
            }
            return new ListingIdentity(stableId, requisitionId, listing.ExternalPath);
        }).ToArray();
    }

    public async Task<LocationFacetOptions> FetchLocationFacetsAsync(
        CompanyDefinition company,
        string? countryId,
        CancellationToken cancellationToken = default)
    {
        if (company.IsSmartRecruiters)
        {
            return await FetchSmartRecruitersLocationFacetsAsync(company, countryId, cancellationToken);
        }

        var jobsEndpoint = new Uri(GetCxsBaseUri(company), "jobs");
        // Do not apply the current location here: doing so makes the provider collapse
        // the country facet to the one country containing that location. Country
        // alone returns the complete country chooser plus dependent locations.
        var query = new JobSourceQuery(countryId, "", true, false, [], CompanyId: company.Id)
            .Normalize(company);
        var payload = new
        {
            appliedFacets = CreateAppliedFacets(company, query),
            limit = _options.PageSize,
            offset = 0,
            searchText = ""
        };
        using var response = await _httpClient.PostAsJsonAsync(
            jobsEndpoint,
            payload,
            _jsonOptions,
            cancellationToken);
        var page = await ReadJsonAsync<ListingResponse>(response, jobsEndpoint, cancellationToken);
        var locationGroup = page.Facets.FirstOrDefault(facet =>
            string.Equals(facet.FacetParameter, "locationMainGroup", StringComparison.Ordinal));
        var countryFacet = locationGroup?.Values.FirstOrDefault(facet =>
                string.Equals(facet.FacetParameter, company.CountryFacetParameter, StringComparison.Ordinal))
            ?? page.Facets.FirstOrDefault(facet =>
                string.Equals(facet.FacetParameter, company.CountryFacetParameter, StringComparison.Ordinal));
        var locationFacet = locationGroup?.Values.FirstOrDefault(facet =>
            string.Equals(facet.FacetParameter, "locations", StringComparison.Ordinal));

        static FacetOption[] ConvertOptions(JobSourceFacetNode? facet) => (facet?.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value.Id) &&
                !string.IsNullOrWhiteSpace(value.Descriptor))
            .Select(value => new FacetOption(value.Id, value.Descriptor, value.Count))
            .ToArray();

        var countries = ConvertOptions(countryFacet)
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var organization = LocationFacetOrganizer.Organize(
            company,
            countryId,
            ConvertOptions(locationFacet));

        return new LocationFacetOptions(
            page.Total,
            countries,
            organization.PhysicalLocations,
            organization.RemoteLocations,
            organization.Groups,
            organization.PhysicalLocations.Count,
            organization.StateMappedLocationCount,
            organization.UnmappedLocationLabels);
    }

    private async Task<ListingBatch> FetchListingsAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        Action<RefreshProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (company.IsSmartRecruiters)
        {
            return await FetchSmartRecruitersListingsAsync(
                company, query, reportProgress, cancellationToken);
        }

        query = query.Normalize(company);
        var jobsEndpoint = new Uri(GetCxsBaseUri(company), "jobs");
        var listings = new List<ListingPosting>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        var pageCount = 0;
        var truncated = false;

        while (true)
        {
            if (pageCount >= _options.MaximumListingPages)
            {
                truncated = true;
                _logger.LogWarning(
                    "Stopped listing retrieval after the configured maximum of {MaximumPages} pages.",
                    _options.MaximumListingPages);
                break;
            }
            pageCount++;
            var payload = new
            {
                appliedFacets = CreateAppliedFacets(company, query),
                limit = _options.PageSize,
                offset,
                searchText = ""
            };

            using var response = await _httpClient.PostAsJsonAsync(
                jobsEndpoint,
                payload,
                _jsonOptions,
                cancellationToken);
            var page = await ReadJsonAsync<ListingResponse>(response, jobsEndpoint, cancellationToken);

            var addedThisPage = 0;
            foreach (var posting in page.JobPostings)
            {
                if (!string.IsNullOrWhiteSpace(posting.ExternalPath) && seenPaths.Add(posting.ExternalPath))
                {
                    listings.Add(posting);
                    addedThisPage++;
                }
            }

            _logger.LogInformation(
                "Retrieved job-source listing page at offset {Offset}: {Count} jobs ({UniqueCount} unique total).",
                offset,
                page.JobPostings.Count,
                listings.Count);

            reportProgress?.Invoke(new RefreshProgress(
                "listings",
                listings.Count,
                page.Total > 0 ? page.Total : null));

            // Later pages can report total=0, and unfiltered queries clamp offsets at
            // 2,000 by repeating the first page. A short page or an all-duplicate
            // page is therefore the reliable, query-independent terminator.
            if (page.JobPostings.Count < _options.PageSize || addedThisPage == 0)
            {
                break;
            }

            offset += _options.PageSize;
        }

        return new ListingBatch(listings, truncated);
    }

    private static Dictionary<string, string[]> CreateAppliedFacets(
        CompanyDefinition company,
        JobSourceQuery query)
    {
        var facets = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(query.CountryId))
        {
            facets[company.CountryFacetParameter] = [query.CountryId];
        }
        var locationIds = query.EffectiveLocationIds(company);
        if (!query.IncludeAllLocations && locationIds.Count > 0)
        {
            facets["locations"] = locationIds.ToArray();
        }
        return facets;
    }

    private async Task<DetailPosting> FetchDetailAsync(
        CompanyDefinition company,
        string externalPath,
        CancellationToken cancellationToken)
    {
        if (company.IsSmartRecruiters)
        {
            return await FetchSmartRecruitersDetailAsync(company, externalPath, cancellationToken);
        }

        var detailUri = new Uri(GetCxsBaseUri(company), externalPath.TrimStart('/'));
        const int maximumAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            using var response = await _httpClient.GetAsync(detailUri, cancellationToken);
            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                attempt < maximumAttempts)
            {
                var serverDelay = response.Headers.RetryAfter?.Delta;
                var retryDelay = serverDelay is { } delay && delay > TimeSpan.Zero
                    ? delay
                    : TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                retryDelay = retryDelay > TimeSpan.FromSeconds(30)
                    ? TimeSpan.FromSeconds(30)
                    : retryDelay;

                _logger.LogInformation(
                    "The job source returned HTTP {StatusCode} for a job detail; retrying attempt {NextAttempt} of {MaximumAttempts} after {DelaySeconds:F0} seconds.",
                    (int)response.StatusCode,
                    attempt + 1,
                    maximumAttempts,
                    retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, cancellationToken);
                continue;
            }

            var detail = await ReadJsonAsync<DetailResponse>(response, detailUri, cancellationToken);
            return detail.JobPostingInfo
                ?? throw new InvalidDataException($"The job-source detail response had no jobPostingInfo: {detailUri}");
        }
    }

    private async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var excerpt = responseText.Length > 500 ? responseText[..500] + "…" : responseText;
            throw new HttpRequestException(
                $"The job source returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for {requestUri}. " +
                $"Response: {excerpt}",
                null,
                response.StatusCode);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"The job source returned an empty JSON response for {requestUri}.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The job source returned invalid JSON for {requestUri}.", ex);
        }
    }

    private JobRecord Normalize(
        CompanyDefinition company,
        ListingPosting listing,
        DetailPosting? detail,
        string? detailError,
        string? listingFingerprint = null)
    {
        var requisitionId = FirstNonEmpty(detail?.JobReqId, listing.BulletFields.FirstOrDefault());
        var title = FirstNonEmpty(detail?.Title, listing.Title);
        var primaryLocation = FirstNonEmpty(detail?.Location, listing.LocationsText);
        var additionalLocations = (detail?.AdditionalLocations ?? [])
            .Concat(listing.AdditionalLocations)
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Where(location => !string.Equals(location.Trim(), primaryLocation.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Select(location => location.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(location => location, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DateOnly? startDate = null;
        if (!string.IsNullOrWhiteSpace(detail?.StartDate) &&
            DateOnly.TryParseExact(
                detail.StartDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            startDate = parsedDate;
        }

        var fallbackUrl = new Uri(
            new Uri(company.PublicSiteUrl.TrimEnd('/') + "/"),
            listing.ExternalPath.TrimStart('/')).ToString();
        var sourceUrl = IsSafeHttpUrl(detail?.ExternalUrl) ? detail!.ExternalUrl : fallbackUrl;
        var descriptionHtml = ProviderHtmlNormalizer.Normalize(detail?.JobDescription);
        var salary = JobAnalysis.AnalyzeSalary(descriptionHtml);
        var remoteLocation = JobAnalysis.AnalyzeRemoteLocation(
            descriptionHtml,
            primaryLocation,
            additionalLocations);
        var clearance = JobAnalysis.AnalyzeClearance(descriptionHtml);
        var credentials = _credentialDetector.Analyze(descriptionHtml);
        var academicQualification = _academicQualificationDetector.Analyze(descriptionHtml);
        var workAuthorization = _workAuthorizationDetector.Analyze(descriptionHtml);
        var remoteWork = _remoteWorkDetector.Analyze(
            title, primaryLocation, additionalLocations, descriptionHtml);
        var extendedLocationRequirement = _extendedLocationRequirementDetector.Analyze(
            title, primaryLocation, additionalLocations, descriptionHtml);
        var detectedConcepts = _jobConceptDetector.Analyze(
            title,
            primaryLocation,
            additionalLocations,
            descriptionHtml,
            remoteWork,
            extendedLocationRequirement);

        return new JobRecord(
            title,
            requisitionId,
            startDate,
            FirstNonEmpty(detail?.PostedOn, listing.PostedOn),
            primaryLocation,
            additionalLocations,
            detail?.TimeType ?? "",
            sourceUrl,
            descriptionHtml,
            salary.Minimum,
            salary.Maximum,
            salary.Period,
            salary.ParseStatus,
            remoteLocation.IsRestricted,
            remoteLocation.Category,
            remoteLocation.Snippet,
            detailError,
            listing.ExternalPath,
            clearance.Level,
            clearance.Requirement,
            clearance.PolygraphRequired,
            clearance.Evidence,
            clearance.ParseStatus,
            credentials.Credentials,
            credentials.UnrecognizedMentions,
            credentials.CatalogVersion,
            academicQualification,
            company.Id,
            workAuthorization,
            remoteWork,
            null,
            listingFingerprint ?? ListingFingerprint(listing),
            detail is null ? null : DateTimeOffset.UtcNow,
            CurrentAnalysisVersion,
            true,
            null,
            listing.IdentityDiscriminator,
            credentials.UnknownRequirements,
            extendedLocationRequirement,
            detectedConcepts,
            _jobConceptDetector.CatalogVersion);
    }

    public async Task<JobRecord> FetchJobDetailAsync(
        CompanyDefinition company,
        JobRecord cachedOrSummary,
        CancellationToken cancellationToken = default)
    {
        var listing = new ListingPosting
        {
            Title = cachedOrSummary.Title,
            ExternalPath = cachedOrSummary.ExternalPath,
            LocationsText = cachedOrSummary.PrimaryLocation,
            PostedOn = cachedOrSummary.PostedOn,
            BulletFields = string.IsNullOrWhiteSpace(cachedOrSummary.RequisitionId)
                ? []
                : [cachedOrSummary.RequisitionId],
            AdditionalLocations = cachedOrSummary.AdditionalLocations.ToList(),
            IdentityDiscriminator = cachedOrSummary.IdentityDiscriminator
        };
        var detail = await FetchDetailAsync(company, listing.ExternalPath, cancellationToken);
        return Normalize(company, listing, detail, null, ListingFingerprint(listing));
    }

    internal bool IsAnalysisCurrent(JobRecord job) =>
        IsPrimaryAnalysisCurrent(job) &&
        job.CredentialCatalogVersion == _credentialDetector.CatalogVersion &&
        job.Credentials is not null &&
        job.UnrecognizedCredentialMentions is not null &&
        job.UnknownCredentialRequirements is not null &&
        job.AcademicQualification?.AnalysisVersion == _academicQualificationDetector.AnalysisVersion &&
        job.WorkAuthorization?.AnalysisVersion == WorkAuthorizationDetector.CurrentAnalysisVersion &&
        job.RemoteWork?.AnalysisVersion == RemoteWorkDetector.CurrentAnalysisVersion &&
        job.ExtendedLocationRequirement?.AnalysisVersion ==
            ExtendedLocationRequirementDetector.CurrentAnalysisVersion &&
        job.DetectedConcepts is not null &&
        job.JobConceptCatalogVersion == _jobConceptDetector.CatalogVersion;

    private static bool IsPrimaryAnalysisCurrent(JobRecord job)
    {
        if (job.AnalysisVersion == CurrentAnalysisVersion)
        {
            return true;
        }

        // Version 6 changes only Boeing summary-pay parsing. Preserve version-5
        // caches for other companies so their persisted derived data is untouched.
        return job.AnalysisVersion == 5 &&
            !string.Equals(job.CompanyId, "boeing", StringComparison.OrdinalIgnoreCase);
    }

    internal JobRecord Reclassify(JobRecord job)
    {
        var description = job.DescriptionHtml ?? "";
        var salary = JobAnalysis.AnalyzeSalary(description);
        var remoteLocation = JobAnalysis.AnalyzeRemoteLocation(
            description, job.PrimaryLocation, job.AdditionalLocations);
        var clearance = JobAnalysis.AnalyzeClearance(description);
        var credentials = _credentialDetector.Analyze(description);
        var academic = _academicQualificationDetector.Analyze(description);
        var authorization = _workAuthorizationDetector.Analyze(description);
        var remoteWork = _remoteWorkDetector.Analyze(
            job.Title, job.PrimaryLocation, job.AdditionalLocations, description);
        var extendedLocationRequirement = _extendedLocationRequirementDetector.Analyze(
            job.Title, job.PrimaryLocation, job.AdditionalLocations, description);
        var detectedConcepts = _jobConceptDetector.Analyze(
            job.Title,
            job.PrimaryLocation,
            job.AdditionalLocations,
            description,
            remoteWork,
            extendedLocationRequirement);
        return job with
        {
            PayMinimum = salary.Minimum,
            PayMaximum = salary.Maximum,
            PayPeriod = salary.Period,
            PayParseStatus = salary.ParseStatus,
            IsRemoteLocationRestricted = remoteLocation.IsRestricted,
            RemoteLocationRestrictionCategory = remoteLocation.Category,
            RemoteLocationRestrictionSnippet = remoteLocation.Snippet,
            ClearanceLevel = clearance.Level,
            ClearanceRequirement = clearance.Requirement,
            PolygraphRequired = clearance.PolygraphRequired,
            ClearanceEvidence = clearance.Evidence,
            ClearanceParseStatus = clearance.ParseStatus,
            Credentials = credentials.Credentials,
            UnrecognizedCredentialMentions = credentials.UnrecognizedMentions,
            UnknownCredentialRequirements = credentials.UnknownRequirements,
            CredentialCatalogVersion = credentials.CatalogVersion,
            AcademicQualification = academic,
            WorkAuthorization = authorization,
            RemoteWork = remoteWork,
            ExtendedLocationRequirement = extendedLocationRequirement,
            DetectedConcepts = detectedConcepts,
            JobConceptCatalogVersion = _jobConceptDetector.CatalogVersion,
            AnalysisVersion = CurrentAnalysisVersion
        };
    }

    private bool ShouldRevalidate(JobRecord job) =>
        job.DetailCachedAtUtc is { } cachedAt &&
        cachedAt < DateTimeOffset.UtcNow.AddHours(-_options.DetailReuseHours);

    private static bool IsSafePreliminaryMatch(
        ListingPosting listing,
        ViewerSettings? settings)
    {
        if (settings is null || !string.Equals(
            settings.KeywordScope, "metadata", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var metadata = string.Join('\n',
            listing.Title,
            listing.ExternalPath,
            listing.LocationsText,
            string.Join('\n', listing.BulletFields));
        var inclusions = settings.IncludeKeywords
            .Where(term => !string.IsNullOrWhiteSpace(term)).ToArray();
        var exclusions = settings.ExcludeKeywords
            .Where(term => !string.IsNullOrWhiteSpace(term)).ToArray();
        return (inclusions.Length == 0 || inclusions.Any(term =>
                metadata.Contains(term, StringComparison.OrdinalIgnoreCase))) &&
            !exclusions.Any(term => metadata.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private JobRecord MergeListing(
        CompanyDefinition company,
        ListingPosting listing,
        JobRecord cached,
        string fingerprint)
    {
        var fallbackUrl = new Uri(
            new Uri(company.PublicSiteUrl.TrimEnd('/') + "/"),
            listing.ExternalPath.TrimStart('/')).ToString();
        return cached with
        {
            Title = string.IsNullOrWhiteSpace(cached.Title) ? listing.Title : cached.Title,
            RequisitionId = string.IsNullOrWhiteSpace(cached.RequisitionId)
                ? listing.BulletFields.FirstOrDefault() ?? ""
                : cached.RequisitionId,
            PostedOn = string.IsNullOrWhiteSpace(cached.PostedOn) ? listing.PostedOn : cached.PostedOn,
            PrimaryLocation = string.IsNullOrWhiteSpace(cached.PrimaryLocation)
                ? listing.LocationsText
                : cached.PrimaryLocation,
            AdditionalLocations = cached.AdditionalLocations
                .Concat(listing.AdditionalLocations)
                .Where(location => !string.IsNullOrWhiteSpace(location))
                .Select(location => location.Trim())
                .Where(location => !string.Equals(location, cached.PrimaryLocation.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(location => location, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SourceUrl = string.IsNullOrWhiteSpace(cached.SourceUrl) ? fallbackUrl : cached.SourceUrl,
            ExternalPath = listing.ExternalPath,
            CompanyId = company.Id,
            ListingFingerprint = fingerprint,
            DetailCachedAtUtc = cached.DetailCachedAtUtc ?? DateTimeOffset.UtcNow,
            IsSourceAvailable = true,
            IdentityDiscriminator = listing.IdentityDiscriminator
        };
    }

    private static string ListingFingerprint(ListingPosting listing)
    {
        // Workday often returns relative PostedOn text. Excluding it prevents a
        // harmless age-label change from invalidating every cached description.
        var value = string.Join('\n',
            listing.ExternalPath.Trim(),
            listing.Title.Trim(),
            listing.LocationsText.Trim(),
            listing.BulletFields.FirstOrDefault()?.Trim() ?? "",
            string.Join('\n', listing.AdditionalLocations.Order(StringComparer.OrdinalIgnoreCase)),
            listing.IdentityDiscriminator ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static Uri GetCxsBaseUri(CompanyDefinition company) => new(
        $"{company.BaseUrl.TrimEnd('/')}/wday/cxs/{Uri.EscapeDataString(company.Tenant)}/{Uri.EscapeDataString(company.Site)}/");

    private async Task<LocationFacetOptions> FetchSmartRecruitersLocationFacetsAsync(
        CompanyDefinition company,
        string? countryId,
        CancellationToken cancellationToken)
    {
        var summaryBatch = await FetchSmartRecruitersSummariesAsync(
            company, null, cancellationToken);
        var postings = summaryBatch.Postings;
        var countries = postings
            .Where(posting => !string.IsNullOrWhiteSpace(posting.Location.Country))
            .GroupBy(posting => posting.Location.Country.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(group => new FacetOption(
                group.Key,
                CountryLabel(group.First().Location),
                group.Count()))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var relevant = string.IsNullOrWhiteSpace(countryId)
            ? postings
            : postings.Where(posting => string.Equals(
                posting.Location.Country, countryId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var locations = relevant
            .Where(posting => !string.IsNullOrWhiteSpace(posting.Location.FullLocation))
            .GroupBy(posting => SmartRecruitersLocationId(posting.Location), StringComparer.Ordinal)
            .Select(group => new FacetOption(
                group.Key,
                group.First().Location.Remote
                    ? $"{CountryLabel(group.First().Location)} - Remote"
                    : group.First().Location.FullLocation,
                group.Count()))
            .ToArray();
        var organization = LocationFacetOrganizer.Organize(company, countryId, locations);

        return new LocationFacetOptions(
            relevant.Count,
            countries,
            organization.PhysicalLocations,
            organization.RemoteLocations,
            organization.Groups,
            organization.PhysicalLocations.Count,
            organization.StateMappedLocationCount,
            organization.UnmappedLocationLabels);
    }

    private async Task<ListingBatch> FetchSmartRecruitersListingsAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        Action<RefreshProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        query = query.Normalize(company);
        var summaryBatch = await FetchSmartRecruitersSummariesAsync(
            company, query.CountryId, cancellationToken);
        var rawPostings = summaryBatch.Postings
            .Where(posting => !string.IsNullOrWhiteSpace(posting.Id))
            .ToArray();
        var postings = rawPostings
            .GroupBy(posting => posting.Id.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(SmartRecruitersRecordSignature, StringComparer.Ordinal)
                    .ThenBy(posting => posting.ReleasedDate, StringComparer.Ordinal)
                    .ToArray();
                if (ordered.Length > 1)
                {
                    var distinct = ordered.Select(SmartRecruitersRecordSignature)
                        .Distinct(StringComparer.Ordinal).Count();
                    _logger.Log(distinct == 1 ? LogLevel.Information : LogLevel.Warning,
                        "SmartRecruiters returned provider posting {PostingId} {DuplicateCount} times across listing pages with {VariantCount} metadata variants; using the deterministic first record.",
                        group.Key, ordered.Length, distinct);
                }
                return ordered[0];
            })
            .ToArray();
        var allowedLocations = query.EffectiveLocationIds(company).ToHashSet(StringComparer.Ordinal);
        var filtered = query.IncludeAllLocations
            ? postings
            : postings.Where(posting => allowedLocations.Contains(
                SmartRecruitersLocationId(posting.Location))).ToArray();
        var listings = new List<ListingPosting>();
        foreach (var identityGroup in filtered
            .GroupBy(posting => string.IsNullOrWhiteSpace(posting.RefNumber)
                ? $"path:{posting.Id.Trim()}"
                : $"req:{posting.RefNumber.Trim()}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var variants = identityGroup
                .GroupBy(SmartRecruitersConflictSignature, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            if (variants.Length > 1)
            {
                _logger.LogWarning(
                    "SmartRecruiters provider-data conflict for {Company} identity {Identity}: {PostingCount} postings contain {VariantCount} materially different title/employment variants. All variants will be retained with deterministic company-scoped identities.",
                    company.DisplayName, identityGroup.Key, identityGroup.Count(), variants.Length);
            }
            else if (identityGroup.Count() > 1)
            {
                _logger.LogInformation(
                    "SmartRecruiters returned {PostingCount} equivalent location variants for {Company} identity {Identity}; merging them deterministically.",
                    identityGroup.Count(), company.DisplayName, identityGroup.Key);
            }

            for (var variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                var members = variants[variantIndex]
                    .OrderBy(posting => posting.Id.Trim(), StringComparer.Ordinal)
                    .ToArray();
                var canonical = members[0];
                var primaryLocation = SmartRecruitersLocationLabel(canonical);
                listings.Add(new ListingPosting
                {
                    Title = canonical.Name.Trim(),
                    ExternalPath = canonical.Id.Trim(),
                    LocationsText = primaryLocation,
                    AdditionalLocations = members
                        .Select(SmartRecruitersLocationLabel)
                        .Where(location => !string.Equals(location, primaryLocation,
                            StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(location => location, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    EquivalentExternalPaths = members
                        .Select(posting => posting.Id.Trim())
                        .Where(path => !string.Equals(path, canonical.Id.Trim(), StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToList(),
                    PostedOn = members.Select(posting => posting.ReleasedDate)
                        .OrderByDescending(value => value, StringComparer.Ordinal)
                        .FirstOrDefault() ?? "",
                    BulletFields = string.IsNullOrWhiteSpace(canonical.RefNumber)
                        ? []
                        : [canonical.RefNumber.Trim()],
                    IdentityDiscriminator = variantIndex == 0
                        ? null
                        : $"path:{canonical.Id.Trim()}"
                });
            }
        }
        reportProgress?.Invoke(new RefreshProgress("listings", listings.Count, listings.Count));
        return new ListingBatch(listings, summaryBatch.Truncated);
    }

    private async Task<SmartSummaryBatch> FetchSmartRecruitersSummariesAsync(
        CompanyDefinition company,
        string? countryId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var firstUri = GetSmartRecruitersPostingsUri(company, countryId, 0, pageSize);
        using var firstResponse = await _httpClient.GetAsync(firstUri, cancellationToken);
        var first = await ReadJsonAsync<SmartRecruitersPostingResponse>(
            firstResponse, firstUri, cancellationToken);
        if (first.TotalFound <= first.Content.Count)
        {
            return new SmartSummaryBatch(first.Content, false);
        }

        var remainingPageCount = (first.TotalFound + pageSize - 1) / pageSize - 1;
        var allowedPageCount = Math.Max(0, _options.MaximumListingPages - 1);
        var truncated = remainingPageCount > allowedPageCount;
        var offsets = Enumerable.Range(1, Math.Min(remainingPageCount, allowedPageCount))
            .Select(page => page * pageSize)
            .ToArray();
        if (truncated)
        {
            _logger.LogWarning(
                "Stopped SmartRecruiters listing retrieval after the configured maximum of {MaximumPages} pages.",
                _options.MaximumListingPages);
        }
        var gate = new SemaphoreSlim(_options.DetailConcurrency);
        var pages = await Task.WhenAll(offsets.Select(async offset =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var uri = GetSmartRecruitersPostingsUri(company, countryId, offset, pageSize);
                using var response = await _httpClient.GetAsync(uri, cancellationToken);
                return await ReadJsonAsync<SmartRecruitersPostingResponse>(response, uri, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));
        return new SmartSummaryBatch(
            first.Content.Concat(pages.SelectMany(page => page.Content)).ToArray(),
            truncated);
    }

    private async Task<DetailPosting> FetchSmartRecruitersDetailAsync(
        CompanyDefinition company,
        string externalPath,
        CancellationToken cancellationToken)
    {
        var postingId = externalPath.Trim('/');
        var detailUri = new Uri(
            $"{company.BaseUrl.TrimEnd('/')}/v1/companies/{Uri.EscapeDataString(company.Tenant)}/postings/{Uri.EscapeDataString(postingId)}");
        using var response = await _httpClient.GetAsync(detailUri, cancellationToken);
        var detail = await ReadJsonAsync<SmartRecruitersPostingDetail>(
            response, detailUri, cancellationToken);
        var sections = detail.JobAd.Sections;
        var description = string.Concat(
            SectionHtml(sections.JobDescription),
            SectionHtml(sections.Qualifications),
            SectionHtml(sections.AdditionalInformation));
        var releasedDate = detail.ReleasedDate.Length >= 10
            ? detail.ReleasedDate[..10]
            : detail.ReleasedDate;

        return new DetailPosting
        {
            Title = detail.Name,
            JobReqId = detail.RefNumber,
            Location = detail.Location.Remote
                ? $"{detail.Location.FullLocation} (Remote)"
                : detail.Location.FullLocation,
            StartDate = releasedDate,
            PostedOn = detail.ReleasedDate,
            TimeType = detail.TypeOfEmployment.Label,
            JobDescription = description,
            ExternalUrl = detail.PostingUrl
        };
    }

    private static Uri GetSmartRecruitersPostingsUri(
        CompanyDefinition company,
        string? countryId,
        int offset,
        int limit)
    {
        var country = string.IsNullOrWhiteSpace(countryId)
            ? ""
            : $"&country={Uri.EscapeDataString(countryId.Trim().ToLowerInvariant())}";
        return new Uri(
            $"{company.BaseUrl.TrimEnd('/')}/v1/companies/{Uri.EscapeDataString(company.Tenant)}/postings?limit={limit}&offset={offset}{country}");
    }

    private static string SmartRecruitersLocationId(SmartRecruitersLocation location) =>
        location.Remote
            ? $"remote:{location.Country.Trim().ToLowerInvariant()}"
            : $"location:{location.Country.Trim().ToLowerInvariant()}:{location.FullLocation.Trim()}";

    private static string SmartRecruitersLocationLabel(SmartRecruitersPosting posting)
    {
        var location = posting.Location.FullLocation.Trim();
        return posting.Location.Remote ? $"{location} (Remote)" : location;
    }

    private static string SmartRecruitersConflictSignature(SmartRecruitersPosting posting) =>
        string.Join('\n',
            posting.Name.Trim().ToUpperInvariant(),
            posting.RefNumber.Trim().ToUpperInvariant(),
            posting.TypeOfEmployment.Label.Trim().ToUpperInvariant());

    private static string SmartRecruitersRecordSignature(SmartRecruitersPosting posting) =>
        string.Join('\n',
            SmartRecruitersConflictSignature(posting),
            posting.Location.Country.Trim().ToUpperInvariant(),
            posting.Location.FullLocation.Trim().ToUpperInvariant(),
            posting.Location.Remote ? "REMOTE" : "PHYSICAL");

    private static string CountryLabel(SmartRecruitersLocation location)
    {
        var parts = location.FullLocation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : location.Country.ToUpperInvariant();
    }

    private static string SectionHtml(SmartRecruitersSection section) =>
        string.IsNullOrWhiteSpace(section.Text)
            ? ""
            : $"<section><h2>{WebUtility.HtmlEncode(section.Title)}</h2>{section.Text}</section>";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static bool IsSafeHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
