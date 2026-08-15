using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace WorkdayJobManager;

public sealed class WorkspaceContext
{
    public const string LocalWorkspaceId = "local";

    public string WorkspaceId { get; private set; } = LocalWorkspaceId;

    internal void SetWorkspace(string workspaceId) => WorkspaceId = workspaceId;
}

public static partial class WorkspaceIdentity
{
    public const string CookieName = "WorkdayJobManager.Workspace";
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

    public WorkspaceIdentityMiddleware(
        RequestDelegate next,
        HostingConfiguration hosting,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<WorkspaceIdentityMiddleware> logger)
    {
        _next = next;
        _hosting = hosting;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector(
            "WorkdayJobManager.AnonymousWorkspace.v1");
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
        if (!string.IsNullOrWhiteSpace(protectedWorkspace))
        {
            try
            {
                workspaceId = _protector.Unprotect(protectedWorkspace);
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("Rejected an invalid anonymous workspace cookie.");
            }
        }

        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            workspaceId = WorkspaceIdentity.Create();
            httpContext.Response.Cookies.Append(
                WorkspaceIdentity.CookieName,
                _protector.Protect(workspaceId),
                WorkspaceIdentity.CreateCookieOptions(secure: true));
            _logger.LogInformation(
                "Created anonymous workspace {WorkspaceReference}.",
                WorkspaceIdentity.Redact(workspaceId));
        }

        workspace.SetWorkspace(workspaceId!);
        await _next(httpContext);
    }
}
