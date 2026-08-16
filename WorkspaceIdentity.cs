using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace JobSearchManager;

public sealed class WorkspaceContext
{
    public const string LocalWorkspaceId = "local";

    public string WorkspaceId { get; private set; } = LocalWorkspaceId;

    internal void SetWorkspace(string workspaceId) => WorkspaceId = workspaceId;
}

public static partial class WorkspaceIdentity
{
    public const string CookieName = "JobSearchManager.Workspace";
    internal const string LegacyCookieName = "WorkdayJobManager.Workspace";
    internal const string ProtectorPurpose = "JobSearchManager.AnonymousWorkspace.v1";
    internal const string LegacyProtectorPurpose = "WorkdayJobManager.AnonymousWorkspace.v1";
    private const int WorkspaceIdBytes = 32;

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceIdPattern();

    public static string Create() => Convert.ToHexString(
        RandomNumberGenerator.GetBytes(WorkspaceIdBytes)).ToLowerInvariant();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && WorkspaceIdPattern().IsMatch(value);

    public static string Redact(string workspaceId) =>
        IsValid(workspaceId) ? $"{workspaceId[..8]}..." : "invalid";

    public static CookieOptions CreateCookieOptions(bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        MaxAge = TimeSpan.FromDays(365 * 5)
    };
}

public sealed class WorkspaceIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HostingConfiguration _hosting;
    private readonly ILogger<WorkspaceIdentityMiddleware> _logger;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _legacyProtector;

    public WorkspaceIdentityMiddleware(
        RequestDelegate next,
        HostingConfiguration hosting,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<WorkspaceIdentityMiddleware> logger)
    {
        _next = next;
        _hosting = hosting;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector(WorkspaceIdentity.ProtectorPurpose);
        _legacyProtector = dataProtectionProvider.CreateProtector(
            WorkspaceIdentity.LegacyProtectorPurpose);
    }

    public async Task InvokeAsync(HttpContext httpContext, WorkspaceContext workspace)
    {
        if (_hosting.IsLocal)
        {
            workspace.SetWorkspace(WorkspaceContext.LocalWorkspaceId);
            await _next(httpContext);
            return;
        }

        var protectedWorkspace = httpContext.Request.Cookies[WorkspaceIdentity.CookieName];
        string? workspaceId = null;
        var canonicalCookieValid = false;
        if (!string.IsNullOrWhiteSpace(protectedWorkspace))
        {
            try
            {
                workspaceId = _protector.Unprotect(protectedWorkspace);
                canonicalCookieValid = WorkspaceIdentity.IsValid(workspaceId);
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("Rejected an invalid anonymous workspace cookie.");
            }
        }

        var legacyCookie = httpContext.Request.Cookies[WorkspaceIdentity.LegacyCookieName];
        var migratedLegacyCookie = false;
        if (!WorkspaceIdentity.IsValid(workspaceId) && !string.IsNullOrWhiteSpace(legacyCookie))
        {
            try
            {
                workspaceId = _legacyProtector.Unprotect(legacyCookie);
                migratedLegacyCookie = WorkspaceIdentity.IsValid(workspaceId);
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("Rejected an invalid legacy anonymous workspace cookie.");
            }
        }

        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            workspaceId = WorkspaceIdentity.Create();
        }

        if (!canonicalCookieValid || migratedLegacyCookie)
        {
            httpContext.Response.Cookies.Append(
                WorkspaceIdentity.CookieName,
                _protector.Protect(workspaceId!),
                WorkspaceIdentity.CreateCookieOptions(secure: true));
            if (migratedLegacyCookie)
            {
                httpContext.Response.Cookies.Delete(
                    WorkspaceIdentity.LegacyCookieName,
                    WorkspaceIdentity.CreateCookieOptions(secure: true));
                _logger.LogInformation(
                    "Migrated anonymous workspace {WorkspaceReference} to the current cookie name.",
                    WorkspaceIdentity.Redact(workspaceId!));
            }
            else
            {
                _logger.LogInformation(
                    "Created anonymous workspace {WorkspaceReference}.",
                    WorkspaceIdentity.Redact(workspaceId!));
            }
        }

        workspace.SetWorkspace(workspaceId!);
        await _next(httpContext);
    }
}
