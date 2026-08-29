using Microsoft.AspNetCore.DataProtection;

namespace JobSearchManager;

public static class DataProtectionConfiguration
{
    public const string ApplicationName = "WorkdayJobManager";

    public static IDataProtectionBuilder AddJobSearchManagerDataProtection(
        this IServiceCollection services,
        HostingConfiguration hosting)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (hosting.DataProtectionPath is not null)
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(hosting.DataProtectionPath));
        }
        return builder;
    }
}
