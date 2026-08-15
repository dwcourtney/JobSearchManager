using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LeidosJobsViewer;

public sealed class WorkdayClient
{
    private readonly HttpClient _httpClient;
    private readonly WorkdayOptions _options;
    private readonly ILogger<WorkdayClient> _logger;
    private readonly CredentialDetector _credentialDetector;
    private readonly Uri _baseUri;
    private readonly Uri _cxsBaseUri;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public WorkdayClient(
        HttpClient httpClient,
        IOptions<WorkdayOptions> options,
        ILogger<WorkdayClient> logger,
        CredentialDetector credentialDetector)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialDetector = credentialDetector;

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Workday:BaseUrl must be an absolute HTTPS URL.");
        }

        if (_options.PageSize is < 1 or > 20)
        {
            throw new InvalidOperationException("Workday:PageSize must be between 1 and 20.");
        }

        if (_options.DetailConcurrency is < 1 or > 20)
        {
            throw new InvalidOperationException("Workday:DetailConcurrency must be between 1 and 20.");
        }

        _baseUri = new Uri(baseUri.ToString().TrimEnd('/') + "/");
        _cxsBaseUri = new Uri(
            _baseUri,
            $"wday/cxs/{Uri.EscapeDataString(_options.Tenant)}/{Uri.EscapeDataString(_options.Site)}/");
    }

    public async Task<WorkdayFetchResult> FetchAllJobsAsync(
        WorkdayQuery query,
        CancellationToken cancellationToken = default)
    {
        var listings = await FetchListingsAsync(query, cancellationToken);
        var jobs = new ConcurrentBag<JobRecord>();
        var detailFailureCount = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.DetailConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(listings, parallelOptions, async (listing, token) =>
        {
            try
            {
                var detail = await FetchDetailAsync(listing.ExternalPath, token);
                jobs.Add(Normalize(listing, detail, null));
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
                jobs.Add(Normalize(listing, null, ex.Message));
            }
        });

        var sorted = jobs
            .OrderByDescending(job => job.StartDate)
            .ThenBy(job => job.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.RequisitionId, StringComparer.Ordinal)
            .ToArray();

        return new WorkdayFetchResult(sorted, listings.Count, detailFailureCount);
    }

    public async Task<IReadOnlyList<ListingIdentity>> FetchListingIdentitiesAsync(
        WorkdayQuery query,
        CancellationToken cancellationToken = default)
    {
        var listings = await FetchListingsAsync(query, cancellationToken);
        return listings.Select(listing =>
        {
            var requisitionId = listing.BulletFields.FirstOrDefault() ?? "";
            var stableId = !string.IsNullOrWhiteSpace(requisitionId)
                ? requisitionId
                : $"path:{listing.ExternalPath}";
            return new ListingIdentity(stableId, requisitionId, listing.ExternalPath);
        }).ToArray();
    }

    public async Task<LocationFacetOptions> FetchLocationFacetsAsync(
        string? countryId,
        CancellationToken cancellationToken = default)
    {
        var jobsEndpoint = new Uri(_cxsBaseUri, "jobs");
        // Do not apply the current location here: doing so makes Workday collapse
        // the country facet to the one country containing that location. Country
        // alone returns the complete country chooser plus dependent locations.
        var query = new WorkdayQuery(countryId, "", null, "");
        var payload = new
        {
            appliedFacets = CreateAppliedFacets(query),
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
            string.Equals(facet.FacetParameter, "locationCountry", StringComparison.Ordinal));
        var locationFacet = locationGroup?.Values.FirstOrDefault(facet =>
            string.Equals(facet.FacetParameter, "locations", StringComparison.Ordinal));

        static FacetOption[] ConvertOptions(WorkdayFacetNode? facet) => (facet?.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value.Id) &&
                !string.IsNullOrWhiteSpace(value.Descriptor))
            .Select(value => new FacetOption(value.Id, value.Descriptor, value.Count))
            .ToArray();

        return new LocationFacetOptions(
            page.Total,
            ConvertOptions(countryFacet),
            ConvertOptions(locationFacet));
    }

    private async Task<List<ListingPosting>> FetchListingsAsync(
        WorkdayQuery query,
        CancellationToken cancellationToken)
    {
        var jobsEndpoint = new Uri(_cxsBaseUri, "jobs");
        var listings = new List<ListingPosting>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;

        while (true)
        {
            var payload = new
            {
                appliedFacets = CreateAppliedFacets(query),
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
                "Retrieved Workday listing page at offset {Offset}: {Count} jobs ({UniqueCount} unique total).",
                offset,
                page.JobPostings.Count,
                listings.Count);

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

    private static Dictionary<string, string[]> CreateAppliedFacets(WorkdayQuery query)
    {
        var facets = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(query.CountryId))
        {
            facets["locationCountry"] = [query.CountryId];
        }
        if (!string.IsNullOrWhiteSpace(query.LocationId))
        {
            facets["locations"] = [query.LocationId];
        }
        return facets;
    }

    private async Task<DetailPosting> FetchDetailAsync(
        string externalPath,
        CancellationToken cancellationToken)
    {
        var detailUri = new Uri(_cxsBaseUri, externalPath.TrimStart('/'));
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
                    "Workday returned HTTP {StatusCode} for a job detail; retrying attempt {NextAttempt} of {MaximumAttempts} after {DelaySeconds:F0} seconds.",
                    (int)response.StatusCode,
                    attempt + 1,
                    maximumAttempts,
                    retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, cancellationToken);
                continue;
            }

            var detail = await ReadJsonAsync<DetailResponse>(response, detailUri, cancellationToken);
            return detail.JobPostingInfo
                ?? throw new InvalidDataException($"Workday detail response had no jobPostingInfo: {detailUri}");
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
                $"Workday returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for {requestUri}. " +
                $"Response: {excerpt}",
                null,
                response.StatusCode);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"Workday returned an empty JSON response for {requestUri}.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Workday returned invalid JSON for {requestUri}.", ex);
        }
    }

    private JobRecord Normalize(
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
            _baseUri,
            $"{Uri.EscapeDataString(_options.Site)}/{listing.ExternalPath.TrimStart('/')}").ToString();
        var workdayUrl = IsSafeHttpUrl(detail?.ExternalUrl) ? detail!.ExternalUrl : fallbackUrl;
        var descriptionHtml = detail?.JobDescription ?? "";
        var salary = JobAnalysis.AnalyzeSalary(descriptionHtml);
        var remoteLocation = JobAnalysis.AnalyzeRemoteLocation(
            descriptionHtml,
            primaryLocation,
            additionalLocations);
        var clearance = JobAnalysis.AnalyzeClearance(descriptionHtml);
        var credentials = _credentialDetector.Analyze(descriptionHtml);

        return new JobRecord(
            title,
            requisitionId,
            startDate,
            FirstNonEmpty(detail?.PostedOn, listing.PostedOn),
            primaryLocation,
            additionalLocations,
            detail?.TimeType ?? "",
            workdayUrl,
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
            credentials.CatalogVersion);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static bool IsSafeHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
