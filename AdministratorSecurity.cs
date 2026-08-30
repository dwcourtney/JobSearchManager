using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace JobSearchManager;

public static class AdminAuthorization
{
    public const string Policy = "JsmAdmin";

    public static int ExpectedStatusCode(ClaimsPrincipal user, AccountRecord? account) =>
        user.Identity?.IsAuthenticated != true
            ? StatusCodes.Status401Unauthorized
            : AccountRoles.IsAdmin(account)
                ? StatusCodes.Status200OK
                : StatusCodes.Status403Forbidden;
}

public sealed class AdminRequirement : IAuthorizationRequirement
{
}

public sealed class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly AccountService _accounts;

    public AdminAuthorizationHandler(AccountService accounts)
    {
        _accounts = accounts;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.Resource is not HttpContext httpContext)
        {
            return;
        }

        var account = httpContext.Items[AccountAuthentication.ResolvedAccountItem] as AccountRecord;
        if (account is null)
        {
            account = await _accounts.GetByIdAsync(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                httpContext.RequestAborted);
        }

        if (AccountRoles.IsAdmin(account)) context.Succeed(requirement);
    }
}

public sealed record AdminBootstrapRequest(string? Code);
public sealed record AdminBootstrapClaimResult(bool Succeeded, string? Error, AccountRecord? Account = null)
{
    public static AdminBootstrapClaimResult Success(AccountRecord account) => new(true, null, account);
    public static AdminBootstrapClaimResult Failure() =>
        new(false, "The administrator bootstrap code is invalid or expired.");
}

public sealed class AdminBootstrapService
{
    internal const int CodeLength = 8;
    internal const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
    internal static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    private readonly AccountService _accounts;
    private readonly HostingConfiguration _hosting;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveBootstrap? _active;

    public AdminBootstrapService(
        AccountService accounts,
        HostingConfiguration hosting,
        TimeProvider time)
    {
        _accounts = accounts;
        _hosting = hosting;
        _time = time;
    }

    internal byte[]? ActiveCodeHash => _active?.Hash.ToArray();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_hosting.AdminBootstrapEnabled) return false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStateAsync(cancellationToken);
            return _active is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AdminBootstrapClaimResult> ClaimAsync(
        string accountId,
        string? submittedCode,
        CancellationToken cancellationToken = default)
    {
        if (!_hosting.AdminBootstrapEnabled) return AdminBootstrapClaimResult.Failure();
        var normalized = NormalizeCode(submittedCode);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStateAsync(cancellationToken);
            if (_active is null || normalized is null || _active.ExpiresUtc <= _time.GetUtcNow())
            {
                return AdminBootstrapClaimResult.Failure();
            }

            var submittedHash = HashCode(normalized);
            if (!CryptographicOperations.FixedTimeEquals(submittedHash, _active.Hash))
            {
                return AdminBootstrapClaimResult.Failure();
            }

            var granted = await _accounts.GrantFirstAdminAsync(accountId, cancellationToken);
            if (!granted.Succeeded || granted.Account is null)
            {
                await EnsureStateAsync(cancellationToken);
                return AdminBootstrapClaimResult.Failure();
            }

            _active = null;
            DeleteCodeFile();
            return AdminBootstrapClaimResult.Success(granted.Account);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStateAsync(CancellationToken cancellationToken)
    {
        if (!_hosting.AdminBootstrapEnabled)
        {
            _active = null;
            return;
        }

        var accountCount = await _accounts.GetAccountCountAsync(cancellationToken);
        var adminCount = await _accounts.GetAdminCountAsync(cancellationToken);
        if (accountCount == 0 || adminCount > 0)
        {
            _active = null;
            DeleteCodeFile();
            return;
        }

        var now = _time.GetUtcNow();
        var existing = await ReadCodeFileAsync(cancellationToken);
        if (existing is not null && existing.ExpiresUtc > now)
        {
            _active = new ActiveBootstrap(HashCode(existing.Code), existing.ExpiresUtc);
            return;
        }

        DeleteCodeFile();
        var code = GenerateCode();
        var expiresUtc = now.Add(CodeLifetime);
        await WriteCodeFileAsync(code, expiresUtc, cancellationToken);
        _active = new ActiveBootstrap(HashCode(code), expiresUtc);
    }

    private async Task<PlaintextBootstrap?> ReadCodeFileAsync(CancellationToken cancellationToken)
    {
        var path = _hosting.AdminBootstrapPath!;
        if (!File.Exists(path)) return null;
        SetOwnerOnlyPermissions(path);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length != 2 || NormalizeCode(lines[0]) is not { } code ||
            !DateTimeOffset.TryParseExact(
                lines[1], "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var expiresUtc))
        {
            return null;
        }
        return new PlaintextBootstrap(code, expiresUtc);
    }

    private async Task WriteCodeFileAsync(
        string code,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken)
    {
        var path = _hosting.AdminBootstrapPath!;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The administrator bootstrap path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                $"{code}\n{expiresUtc:O}\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            SetOwnerOnlyPermissions(temporary);
            File.Move(temporary, path, overwrite: true);
            SetOwnerOnlyPermissions(path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void DeleteCodeFile()
    {
        var path = _hosting.AdminBootstrapPath;
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    private static void SetOwnerOnlyPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string GenerateCode()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
        return new string(code);
    }

    private static string? NormalizeCode(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is { Length: CodeLength } && normalized.All(CodeAlphabet.Contains)
            ? normalized
            : null;
    }

    private static byte[] HashCode(string code) =>
        SHA256.HashData(Encoding.ASCII.GetBytes(code));

    private sealed record ActiveBootstrap(byte[] Hash, DateTimeOffset ExpiresUtc);
    private sealed record PlaintextBootstrap(string Code, DateTimeOffset ExpiresUtc);
}
