using System.Text.RegularExpressions;

namespace WorkdayJobManager;

public enum WorkdayHostingMode
{
    Local,
    Azure
}

public sealed record HostingConfiguration(
    WorkdayHostingMode Mode,
    string? StorageAccount,
    string? StorageContainer)
{
    public const string ModeSetting = "WORKDAYJOBMANAGER_HOSTING_MODE";
    public const string StorageAccountSetting = "WORKDAYJOBMANAGER_STORAGE_ACCOUNT";
    public const string StorageContainerSetting = "WORKDAYJOBMANAGER_STORAGE_CONTAINER";

    private static readonly Regex StorageAccountPattern = new(
        "^[a-z0-9]{3,24}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ContainerPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsAzure => Mode == WorkdayHostingMode.Azure;
    public bool IsLocal => Mode == WorkdayHostingMode.Local;

    public static HostingConfiguration FromConfiguration(IConfiguration configuration)
    {
        var rawMode = configuration[ModeSetting];
        var mode = string.IsNullOrWhiteSpace(rawMode) ||
            string.Equals(rawMode, "Local", StringComparison.OrdinalIgnoreCase)
                ? WorkdayHostingMode.Local
                : string.Equals(rawMode, "Azure", StringComparison.OrdinalIgnoreCase)
                    ? WorkdayHostingMode.Azure
                    : throw new InvalidOperationException(
                        $"{ModeSetting} must be either 'Local' or 'Azure'.");

        var account = NullIfWhiteSpace(configuration[StorageAccountSetting]);
        var container = NullIfWhiteSpace(configuration[StorageContainerSetting]);
        var result = new HostingConfiguration(mode, account, container);
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (IsLocal)
        {
            return;
        }

        if (StorageAccount is null || !StorageAccountPattern.IsMatch(StorageAccount))
        {
            throw new InvalidOperationException(
                $"Azure mode requires {StorageAccountSetting} to contain a valid Azure Storage account name.");
        }

        if (StorageContainer is null || !ContainerPattern.IsMatch(StorageContainer))
        {
            throw new InvalidOperationException(
                $"Azure mode requires {StorageContainerSetting} to contain a valid private Blob container name.");
        }
    }

    public Uri GetBlobServiceUri()
    {
        Validate();
        return new Uri($"https://{StorageAccount}.blob.core.windows.net", UriKind.Absolute);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
