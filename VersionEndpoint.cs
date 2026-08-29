using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed record ApplicationVersionInfo(
    string Commit,
    string Version,
    string HostingMode);

public static class VersionEndpoint
{
    public const string Path = "/version";
    public const string CommitSetting = "JOBSEARCHMANAGER_COMMIT_SHA";
    public const string UnknownCommit = "unknown";

    private static readonly Regex FullCommitPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ApplicationVersionInfo Create(
        IConfiguration configuration,
        HostingConfiguration hosting)
    {
        var configuredCommit = configuration[CommitSetting]?.Trim().ToLowerInvariant();
        var commit = configuredCommit is not null && FullCommitPattern.IsMatch(configuredCommit)
            ? configuredCommit
            : UnknownCommit;
        var assembly = typeof(VersionEndpoint).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString() ?? "unknown";
        }

        return new ApplicationVersionInfo(commit, version, hosting.Mode.ToString());
    }

    public static async Task<bool> TryHandleAsync(
        HttpContext context,
        ApplicationVersionInfo versionInfo)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path != Path)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            versionInfo,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            context.RequestAborted);
        return true;
    }
}
