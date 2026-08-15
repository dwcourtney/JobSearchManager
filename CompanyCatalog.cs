using System.Text.Json;

namespace WorkdayJobManager;

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
            : throw new ArgumentException($"Unsupported Workday company '{companyId}'.", nameof(companyId));
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
    string WorkdayHost,
    string Tenant,
    string Site,
    string PublicSiteUrl,
    FacetSelection DefaultCountry,
    IReadOnlyList<string> RemoteLocationIds)
{
    public string BaseUrl => $"https://{WorkdayHost}";

    public bool IsRemoteLocation(string? locationId) =>
        !string.IsNullOrWhiteSpace(locationId) &&
        RemoteLocationIds.Contains(locationId, StringComparer.Ordinal);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) ||
            string.IsNullOrWhiteSpace(WorkdayHost) || string.IsNullOrWhiteSpace(Tenant) ||
            string.IsNullOrWhiteSpace(Site) ||
            !Uri.TryCreate(PublicSiteUrl, UriKind.Absolute, out var publicUri) ||
            publicUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(publicUri.Host, WorkdayHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid Workday company definition '{Id}'.");
        }
    }
}
