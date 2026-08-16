using System.Text.RegularExpressions;

namespace JobSearchManager;

public enum ApplicationHostingMode
{
    Local,
    Azure
}

public sealed record HostingConfiguration(
    ApplicationHostingMode Mode,
    string? StorageAccount,
    string? StorageContainer)
{
    public const string ModeSetting = "JOBSEARCHMANAGER_HOSTING_MODE";
    public const string StorageAccountSetting = "JOBSEARCHMANAGER_STORAGE_ACCOUNT";
    public const string StorageContainerSetting = "JOBSEARCHMANAGER_STORAGE_CONTAINER";
    internal const string LegacyModeSetting = "WORKDAYJOBMANAGER_HOSTING_MODE";
    internal const string LegacyStorageAccountSetting = "WORKDAYJOBMANAGER_STORAGE_ACCOUNT";
    internal const string LegacyStorageContainerSetting = "WORKDAYJOBMANAGER_STORAGE_CONTAINER";

    private static readonly Regex StorageAccountPattern = new(
        "^[a-z0-9]{3,24}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ContainerPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsAzure => Mode == ApplicationHostingMode.Azure;
    public bool IsLocal => Mode == ApplicationHostingMode.Local;

    public static HostingConfiguration FromConfiguration(IConfiguration configuration)
    {
        var rawMode = ReadCanonicalOrLegacy(configuration, ModeSetting, LegacyModeSetting);
        var mode = string.IsNullOrWhiteSpace(rawMode) ||
            string.Equals(rawMode, "Local", StringComparison.OrdinalIgnoreCase)
                ? ApplicationHostingMode.Local
                : string.Equals(rawMode, "Azure", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationHostingMode.Azure
                    : throw new InvalidOperationException(
                        $"{ModeSetting} must be either 'Local' or 'Azure'.");

        var account = NullIfWhiteSpace(ReadCanonicalOrLegacy(
            configuration, StorageAccountSetting, LegacyStorageAccountSetting));
        var container = NullIfWhiteSpace(ReadCanonicalOrLegacy(
            configuration, StorageContainerSetting, LegacyStorageContainerSetting));
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

    private static string? ReadCanonicalOrLegacy(
        IConfiguration configuration,
        string canonicalName,
        string legacyName) =>
        configuration[canonicalName] ?? configuration[legacyName];
}
