namespace JobSearchManager;

public enum ApplicationHostingMode
{
    Local,
    Container
}

public sealed record HostingConfiguration(
    ApplicationHostingMode Mode,
    string? DataProtectionPath = null,
    string? AdminBootstrapPath = null)
{
    public const string ModeSetting = "JOBSEARCHMANAGER_HOSTING_MODE";
    public const string DataProtectionPathSetting = "JOBSEARCHMANAGER_DATA_PROTECTION_PATH";
    public const string AdminBootstrapPathSetting = "JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH";
    internal const string LegacyModeSetting = "WORKDAYJOBMANAGER_HOSTING_MODE";

    public bool IsLocal => Mode == ApplicationHostingMode.Local;
    public bool IsContainer => Mode == ApplicationHostingMode.Container;
    public bool UsesPerBrowserWorkspaces => !IsLocal;
    public bool RequiresSameOriginProtection => !IsLocal;
    public bool AdminBootstrapEnabled => AdminBootstrapPath is not null;

    public static HostingConfiguration FromConfiguration(IConfiguration configuration)
    {
        var rawMode = ReadCanonicalOrLegacy(configuration, ModeSetting, LegacyModeSetting);
        var mode = string.IsNullOrWhiteSpace(rawMode) ||
            string.Equals(rawMode, "Local", StringComparison.OrdinalIgnoreCase)
                ? ApplicationHostingMode.Local
                : string.Equals(rawMode, "Container", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationHostingMode.Container
                    : throw new InvalidOperationException(
                        $"{ModeSetting} must be 'Local' or 'Container'.");
        var dataProtectionPath = NullIfWhiteSpace(configuration[DataProtectionPathSetting]);
        var adminBootstrapPath = NullIfWhiteSpace(configuration[AdminBootstrapPathSetting]);
        var result = new HostingConfiguration(mode, dataProtectionPath, adminBootstrapPath);
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (IsContainer && DataProtectionPath is null)
        {
            throw new InvalidOperationException(
                $"Container mode requires {DataProtectionPathSetting} so cookies survive container replacement.");
        }

    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadCanonicalOrLegacy(
        IConfiguration configuration,
        string canonicalName,
        string legacyName) =>
        configuration[canonicalName] ?? configuration[legacyName];
}
