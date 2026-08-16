using System.Text.Json;

namespace JobSearchManager;

public sealed class CompanyCatalog
{
    private readonly IReadOnlyDictionary<string, CompanyDefinition> _byId;

    public CompanyCatalog(IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "CompanyCatalog.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "CompanyCatalog.json");
        }

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<CompanyCatalogDocument>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"Company catalog is empty: {path}");
        if (document.SchemaVersion != 1 || document.Companies.Count == 0)
        {
            throw new InvalidDataException($"Unsupported or empty company catalog: {path}");
        }

        foreach (var company in document.Companies)
        {
            company.Validate();
        }

        Companies = document.Companies.ToArray();
        _byId = Companies.ToDictionary(company => company.Id, StringComparer.OrdinalIgnoreCase);
        CatalogPath = path;
    }

    public const string DefaultCompanyId = "leidos";
    public string CatalogPath { get; }
    public IReadOnlyList<CompanyDefinition> Companies { get; }

    public CompanyDefinition Get(string? companyId)
    {
        var normalized = string.IsNullOrWhiteSpace(companyId) ? DefaultCompanyId : companyId.Trim();
        return _byId.TryGetValue(normalized, out var company)
            ? company
            : throw new ArgumentException($"Unsupported job source company '{companyId}'.", nameof(companyId));
    }

    public bool TryGet(string? companyId, out CompanyDefinition company) =>
        _byId.TryGetValue(companyId?.Trim() ?? "", out company!);
}

public sealed record CompanyCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<CompanyDefinition> Companies);

public sealed record CompanyDefinition(
    string Id,
    string DisplayName,
    string ApiHost,
    string Tenant,
    string Site,
    string PublicSiteUrl,
    FacetSelection DefaultCountry,
    IReadOnlyList<string> RemoteLocationIds,
    string Provider = JobSourceProviders.Workday,
    string CountryFacetParameter = "locationCountry")
{
    public string BaseUrl => $"https://{ApiHost}";

    public bool IsSmartRecruiters => string.Equals(
        Provider,
        JobSourceProviders.SmartRecruiters,
        StringComparison.OrdinalIgnoreCase);

    public bool IsRemoteLocation(string? locationId) =>
        !string.IsNullOrWhiteSpace(locationId) &&
        (RemoteLocationIds.Contains(locationId, StringComparer.Ordinal) ||
         IsSmartRecruiters && locationId.StartsWith("remote:", StringComparison.Ordinal));

    public IReadOnlyList<string> RemoteLocationIdsForCountry(string? countryId) =>
        IsSmartRecruiters && !string.IsNullOrWhiteSpace(countryId)
            ? [$"remote:{countryId.Trim().ToLowerInvariant()}"]
            : RemoteLocationIds;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) ||
            string.IsNullOrWhiteSpace(ApiHost) || string.IsNullOrWhiteSpace(Tenant) ||
            string.IsNullOrWhiteSpace(Site) || string.IsNullOrWhiteSpace(CountryFacetParameter) ||
            !Uri.TryCreate(PublicSiteUrl, UriKind.Absolute, out var publicUri) ||
            publicUri.Scheme != Uri.UriSchemeHttps ||
            (!IsSmartRecruiters && !string.Equals(publicUri.Host, ApiHost, StringComparison.OrdinalIgnoreCase)) ||
            (!string.Equals(Provider, JobSourceProviders.Workday, StringComparison.OrdinalIgnoreCase) &&
             !IsSmartRecruiters))
        {
            throw new InvalidDataException($"Invalid job source company definition '{Id}'.");
        }
    }
}

public static class JobSourceProviders
{
    public const string Workday = "workday";
    public const string SmartRecruiters = "smartRecruiters";
}
