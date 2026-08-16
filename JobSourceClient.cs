using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace JobSearchManager;

public sealed class JobSourceClient
{
    private readonly HttpClient _httpClient;
    private readonly JobSourceOptions _options;
    private readonly ILogger<JobSourceClient> _logger;
    private readonly CredentialDetector _credentialDetector;
    private readonly AcademicQualificationDetector _academicQualificationDetector;
    private readonly WorkAuthorizationDetector _workAuthorizationDetector;
    private readonly RemoteWorkDetector _remoteWorkDetector;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public JobSourceClient(
        HttpClient httpClient,
        IOptions<JobSourceOptions> options,
        ILogger<JobSourceClient> logger,
        CredentialDetector credentialDetector,
        AcademicQualificationDetector academicQualificationDetector,
        WorkAuthorizationDetector workAuthorizationDetector,
        RemoteWorkDetector remoteWorkDetector)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialDetector = credentialDetector;
        _academicQualificationDetector = academicQualificationDetector;
        _workAuthorizationDetector = workAuthorizationDetector;
        _remoteWorkDetector = remoteWorkDetector;

        if (_options.PageSize is < 1 or > 20)
        {
            throw new InvalidOperationException("JobSource:PageSize must be between 1 and 20.");
        }

        if (_options.DetailConcurrency is < 1 or > 20)
        {
            throw new InvalidOperationException("JobSource:DetailConcurrency must be between 1 and 20.");
        }

    }

    public async Task<JobSourceFetchResult> FetchAllJobsAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        Action<RefreshProgress>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        var listings = await FetchListingsAsync(company, query, reportProgress, cancellationToken);
        var jobs = new ConcurrentBag<JobRecord>();
        var detailFailureCount = 0;
        var completedDetails = 0;

        reportProgress?.Invoke(new RefreshProgress("details", 0, listings.Count));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.DetailConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(listings, parallelOptions, async (listing, token) =>
        {
            try
            {
                var detail = await FetchDetailAsync(company, listing.ExternalPath, token);
                jobs.Add(Normalize(company, listing, detail, null));
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
                    listing.Title,
                    listing.ExternalPath);
                jobs.Add(Normalize(company, listing, null, ex.Message));
            }
            finally
            {
                var completed = Interlocked.Increment(ref completedDetails);
                reportProgress?.Invoke(new RefreshProgress("details", completed, listings.Count));
            }
        });

        reportProgress?.Invoke(new RefreshProgress("finalizing", listings.Count, listings.Count));

        var sorted = jobs
            .OrderByDescending(job => job.StartDate)
            .ThenBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.RequisitionId, StringComparer.Ordinal)
            .ToArray();

        return new JobSourceFetchResult(sorted, listings.Count, detailFailureCount);
    }

    public async Task<IReadOnlyList<ListingIdentity>> FetchListingIdentitiesAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        var listings = await FetchListingsAsync(company, query, null, cancellationToken);
        return listings.Select(listing =>
        {
            var requisitionId = listing.BulletFields.FirstOrDefault() ?? "";
            var stableId = !string.IsNullOrWhiteSpace(requisitionId)
                ? $"{company.Id}:{requisitionId}"
                : $"{company.Id}:path:{listing.ExternalPath}";
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

    private async Task<List<ListingPosting>> FetchListingsAsync(
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

        while (true)
        {
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

        return listings;
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
        string? detailError)
    {
        var requisitionId = FirstNonEmpty(detail?.JobReqId, listing.BulletFields.FirstOrDefault());
        var title = FirstNonEmpty(detail?.Title, listing.Title);
        var primaryLocation = FirstNonEmpty(detail?.Location, listing.LocationsText);
        var additionalLocations = detail?.AdditionalLocations
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

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
        var descriptionHtml = detail?.JobDescription ?? "";
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
            remoteWork);
    }

    private static Uri GetCxsBaseUri(CompanyDefinition company) => new(
        $"{company.BaseUrl.TrimEnd('/')}/wday/cxs/{Uri.EscapeDataString(company.Tenant)}/{Uri.EscapeDataString(company.Site)}/");

    private async Task<LocationFacetOptions> FetchSmartRecruitersLocationFacetsAsync(
        CompanyDefinition company,
        string? countryId,
        CancellationToken cancellationToken)
    {
        var postings = await FetchSmartRecruitersSummariesAsync(
            company, null, cancellationToken);
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

    private async Task<List<ListingPosting>> FetchSmartRecruitersListingsAsync(
        CompanyDefinition company,
        JobSourceQuery query,
        Action<RefreshProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        query = query.Normalize(company);
        var postings = await FetchSmartRecruitersSummariesAsync(
            company, query.CountryId, cancellationToken);
        var allowedLocations = query.EffectiveLocationIds(company).ToHashSet(StringComparer.Ordinal);
        var filtered = query.IncludeAllLocations
            ? postings
            : postings.Where(posting => allowedLocations.Contains(
                SmartRecruitersLocationId(posting.Location))).ToArray();
        var listings = filtered
            .Where(posting => !string.IsNullOrWhiteSpace(posting.Id))
            .GroupBy(posting => posting.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(posting => new ListingPosting
            {
                Title = posting.Name,
                ExternalPath = posting.Id,
                LocationsText = posting.Location.Remote
                    ? $"{posting.Location.FullLocation} (Remote)"
                    : posting.Location.FullLocation,
                PostedOn = posting.ReleasedDate,
                BulletFields = string.IsNullOrWhiteSpace(posting.RefNumber)
                    ? []
                    : [posting.RefNumber]
            })
            .ToList();
        reportProgress?.Invoke(new RefreshProgress("listings", listings.Count, listings.Count));
        return listings;
    }

    private async Task<IReadOnlyList<SmartRecruitersPosting>> FetchSmartRecruitersSummariesAsync(
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
            return first.Content;
        }

        var offsets = Enumerable.Range(1, (first.TotalFound + pageSize - 1) / pageSize - 1)
            .Select(page => page * pageSize);
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
        return first.Content.Concat(pages.SelectMany(page => page.Content)).ToArray();
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
