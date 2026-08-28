using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Identity;

namespace JobSearchManager;

public static class AccountAuthentication
{
    public const string CookieName = "JobSearchManager.Account";
    public const string SecurityVersionClaim = "jsm_security_version";
    public const string PersistenceClaim = "jsm_persistence";
    public const string ResolvedAccountItem = "JobSearchManager.ResolvedAccount";
}

public static class AccountPersistence
{
    public const string Session = "session";
    public const string OneDay = "day1";
    public const string SevenDays = "days7";
    public const string FourteenDays = "days14";
    public const string ThirtyDays = "days30";
    public const string KeepSignedIn = "keep";

    public static string Normalize(string? value) => value switch
    {
        OneDay => OneDay,
        SevenDays => SevenDays,
        FourteenDays => FourteenDays,
        ThirtyDays => ThirtyDays,
        KeepSignedIn => KeepSignedIn,
        _ => Session
    };

    public static TimeSpan? Lifetime(string? value) => Normalize(value) switch
    {
        OneDay => TimeSpan.FromDays(1),
        SevenDays => TimeSpan.FromDays(7),
        FourteenDays => TimeSpan.FromDays(14),
        ThirtyDays => TimeSpan.FromDays(30),
        KeepSignedIn => TimeSpan.FromDays(180),
        _ => null
    };
}

public sealed class AccountRecord
{
    public string AccountId { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool EmailVerified { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? EmailVerifiedUtc { get; set; }
    public DateTimeOffset? LastLoginUtc { get; set; }
    public DateTimeOffset? PasswordChangedUtc { get; set; }
    public int SecurityVersion { get; set; } = 1;
    public string WorkspaceId { get; set; } = "";
}

public sealed class AccountTokenRecord
{
    public string AccountId { get; set; } = "";
    public string Purpose { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}

public sealed class AccountRegistryDocument
{
    public int Version { get; set; } = 1;
    public Dictionary<string, AccountRecord> Accounts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> EmailIndex { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> WorkspaceOwners { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, AccountTokenRecord> Tokens { get; set; } = new(StringComparer.Ordinal);

    internal void Normalize()
    {
        Accounts = new Dictionary<string, AccountRecord>(Accounts ?? [], StringComparer.Ordinal);
        EmailIndex = new Dictionary<string, string>(EmailIndex ?? [], StringComparer.Ordinal);
        WorkspaceOwners = new Dictionary<string, string>(WorkspaceOwners ?? [], StringComparer.Ordinal);
        Tokens = new Dictionary<string, AccountTokenRecord>(Tokens ?? [], StringComparer.Ordinal);
    }
}

public sealed record AccountRegistryMutation<TResult>(bool Changed, TResult Result);

public interface IAccountRegistryStore
{
    Task<TResult> MutateAsync<TResult>(
        Func<AccountRegistryDocument, AccountRegistryMutation<TResult>> mutation,
        CancellationToken cancellationToken = default);
    Task ValidateAsync(CancellationToken cancellationToken = default);
}

public sealed class FileAccountRegistryStore : IAccountRegistryStore
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "data", "authentication", "accounts.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TResult> MutateAsync<TResult>(
        Func<AccountRegistryDocument, AccountRegistryMutation<TResult>> mutation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            var result = mutation(document);
            if (result.Changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await using (var stream = new FileStream(
                        temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384,
                        FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await JsonSerializer.SerializeAsync(stream, document, _json, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temporary, _path, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            return result.Result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return Task.CompletedTask;
    }

    private async Task<AccountRegistryDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new AccountRegistryDocument();
        await using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<AccountRegistryDocument>(stream, _json, cancellationToken)
            ?? new AccountRegistryDocument();
        document.Normalize();
        return document;
    }
}

public sealed class AzureBlobAccountRegistryStore : IAccountRegistryStore
{
    internal const string BlobName = "authentication/accounts.json";
    private readonly BlobClient _blob;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public AzureBlobAccountRegistryStore(BlobContainerClient container) => _blob = container.GetBlobClient(BlobName);

    public async Task<TResult> MutateAsync<TResult>(
        Func<AccountRegistryDocument, AccountRegistryMutation<TResult>> mutation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var (document, etag) = await ReadAsync(cancellationToken);
                var result = mutation(document);
                if (!result.Changed) return result.Result;
                try
                {
                    var content = BinaryData.FromObjectAsJson(document, _json);
                    await _blob.UploadAsync(content, new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                        Conditions = etag.HasValue
                            ? new BlobRequestConditions { IfMatch = etag.Value }
                            : new BlobRequestConditions { IfNoneMatch = ETag.All }
                    }, cancellationToken);
                    return result.Result;
                }
                catch (RequestFailedException ex) when (ex.Status is 409 or 412)
                {
                    if (attempt == 4) throw;
                }
            }
            throw new InvalidOperationException("The account registry could not be updated.");
        }
        catch (RequestFailedException ex)
        {
            throw new WorkspaceStorageException("Account storage is temporarily unavailable.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await _blob.ExistsAsync(cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new WorkspaceStorageException("Account storage is temporarily unavailable.", ex);
        }
    }

    private async Task<(AccountRegistryDocument Document, ETag? ETag)> ReadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blob.DownloadContentAsync(cancellationToken);
            var document = response.Value.Content.ToObjectFromJson<AccountRegistryDocument>(_json)
                ?? new AccountRegistryDocument();
            document.Normalize();
            return (document, response.Value.Details.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (new AccountRegistryDocument(), null);
        }
    }
}

internal sealed class MemoryAccountRegistryStore : IAccountRegistryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccountRegistryDocument _document = new();
    public bool FailNextWrite { get; set; }

    public async Task<TResult> MutateAsync<TResult>(
        Func<AccountRegistryDocument, AccountRegistryMutation<TResult>> mutation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(_document);
            var copy = JsonSerializer.Deserialize<AccountRegistryDocument>(json) ?? new();
            copy.Normalize();
            var result = mutation(copy);
            if (result.Changed)
            {
                if (FailNextWrite)
                {
                    FailNextWrite = false;
                    throw new WorkspaceStorageException("Simulated account write failure.", new IOException());
                }
                _document = copy;
            }
            return result.Result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record AccountOperationResult(bool Succeeded, string? Error, AccountRecord? Account = null)
{
    public static AccountOperationResult Success(AccountRecord account) => new(true, null, account);
    public static AccountOperationResult Failure(string error) => new(false, error);
}

public sealed record CreateAccountRequest(string? Email, string? Password, string? ConfirmPassword, string? Persistence);
public sealed record LoginRequest(string? Email, string? Password, string? Persistence);
public sealed record EmailRequest(string? Email);
public sealed record TokenRequest(string? Token);
public sealed record ResetPasswordRequest(string? Token, string? Password, string? ConfirmPassword);
public sealed record ChangePasswordRequest(
    string? CurrentPassword, string? Password, string? ConfirmPassword, string? Persistence);
public sealed record SessionPersistenceRequest(string? Persistence);

public interface IAccountEmailSender
{
    bool IsConfigured { get; }
    Task SendVerificationAsync(string email, Uri link, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string email, Uri link, CancellationToken cancellationToken = default);
}

public sealed class SmtpAccountEmailSender : IAccountEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpAccountEmailSender(IConfiguration configuration) => _configuration = configuration;

    public bool IsConfigured => Required("JOBSEARCHMANAGER_SMTP_HOST") is not null &&
        Required("JOBSEARCHMANAGER_EMAIL_FROM") is not null;

    public Task SendVerificationAsync(string email, Uri link, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Verify your Job Search Manager email",
            $"Verify your email address by opening this link:\n\n{link}\n\nIf you did not create this account, ignore this message.",
            cancellationToken);

    public Task SendPasswordResetAsync(string email, Uri link, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Reset your Job Search Manager password",
            $"Reset your password by opening this link within one hour:\n\n{link}\n\nIf you did not request this, ignore this message.",
            cancellationToken);

    private async Task SendAsync(string email, string subject, string body, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return;
        var host = Required("JOBSEARCHMANAGER_SMTP_HOST")!;
        var from = Required("JOBSEARCHMANAGER_EMAIL_FROM")!;
        var port = int.TryParse(_configuration["JOBSEARCHMANAGER_SMTP_PORT"], out var configuredPort)
            ? Math.Clamp(configuredPort, 1, 65535) : 587;
        using var message = new MailMessage(from, email, subject, body);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = !string.Equals(
                _configuration["JOBSEARCHMANAGER_SMTP_ENABLE_SSL"], "false", StringComparison.OrdinalIgnoreCase)
        };
        var username = Required("JOBSEARCHMANAGER_SMTP_USERNAME");
        var password = Required("JOBSEARCHMANAGER_SMTP_PASSWORD");
        if (username is not null && password is not null)
        {
            client.Credentials = new NetworkCredential(username, password);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }

    private string? Required(string key) => string.IsNullOrWhiteSpace(_configuration[key])
        ? null : _configuration[key]!.Trim();
}

public sealed class AccountService
{
    private const string VerificationPurpose = "verify-email";
    private const string ResetPurpose = "password-reset";
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;
    private readonly IAccountRegistryStore _store;
    private readonly IPasswordHasher<AccountRecord> _passwordHasher;
    private readonly IAccountEmailSender _emailSender;
    private readonly TimeProvider _time;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        IAccountRegistryStore store,
        IPasswordHasher<AccountRecord> passwordHasher,
        IAccountEmailSender emailSender,
        TimeProvider time,
        ILogger<AccountService> logger)
    {
        _store = store;
        _passwordHasher = passwordHasher;
        _emailSender = emailSender;
        _time = time;
        _logger = logger;
    }

    public bool EmailDeliveryConfigured => _emailSender.IsConfigured;

    public async Task<AccountOperationResult> CreateAsync(
        string workspaceId,
        string? email,
        string? password,
        Uri verificationBaseUri,
        CancellationToken cancellationToken = default)
    {
        var emailValidation = ValidateEmail(email);
        if (emailValidation.Error is not null) return AccountOperationResult.Failure(emailValidation.Error);
        var passwordError = ValidatePassword(password);
        if (passwordError is not null) return AccountOperationResult.Failure(passwordError);
        if (!IsWorkspaceIdAllowed(workspaceId)) return AccountOperationResult.Failure("The current workspace is invalid.");

        var now = _time.GetUtcNow();
        var account = new AccountRecord
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Email = emailValidation.Email!,
            NormalizedEmail = emailValidation.Normalized!,
            CreatedUtc = now,
            WorkspaceId = workspaceId,
            SecurityVersion = 1
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, password!);
        var rawToken = CreateToken();
        var tokenHash = HashToken(rawToken);
        var result = await _store.MutateAsync<AccountOperationResult>(document =>
        {
            RemoveExpiredTokens(document, now);
            if (document.EmailIndex.ContainsKey(account.NormalizedEmail))
                return new(false, AccountOperationResult.Failure("An account already uses that email address."));
            if (document.WorkspaceOwners.ContainsKey(workspaceId))
                return new(false, AccountOperationResult.Failure("This workspace is already linked to an account."));
            document.Accounts.Add(account.AccountId, account);
            document.EmailIndex.Add(account.NormalizedEmail, account.AccountId);
            document.WorkspaceOwners.Add(workspaceId, account.AccountId);
            document.Tokens[tokenHash] = new AccountTokenRecord
            {
                AccountId = account.AccountId,
                Purpose = VerificationPurpose,
                CreatedUtc = now,
                ExpiresUtc = now.AddHours(24)
            };
            return new(true, AccountOperationResult.Success(account));
        }, cancellationToken);

        if (result.Succeeded && _emailSender.IsConfigured)
        {
            try
            {
                var link = BuildLink(verificationBaseUri, "verifyEmailToken", rawToken);
                await _emailSender.SendVerificationAsync(account.Email, link, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Account created, but the verification email could not be delivered.");
            }
        }
        return result;
    }

    public async Task<AccountRecord?> AuthenticateAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateEmail(email);
        if (validation.Error is not null || string.IsNullOrEmpty(password) ||
            password.Length > MaximumPasswordLength) return null;
        var now = _time.GetUtcNow();
        return await _store.MutateAsync<AccountRecord?>(document =>
        {
            if (!document.EmailIndex.TryGetValue(validation.Normalized!, out var accountId) ||
                !document.Accounts.TryGetValue(accountId, out var account))
                return new(false, (AccountRecord?)null);
            var verification = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
            if (verification == PasswordVerificationResult.Failed)
                return new(false, (AccountRecord?)null);
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                account.PasswordHash = _passwordHasher.HashPassword(account, password);
            account.LastLoginUtc = now;
            return new(true, account);
        }, cancellationToken);
    }

    public Task<AccountRecord?> GetByIdAsync(string? accountId, CancellationToken cancellationToken = default) =>
        _store.MutateAsync<AccountRecord?>(document => new(false,
            accountId is not null && document.Accounts.TryGetValue(accountId, out var account)
                ? account : null), cancellationToken);

    public Task<string?> GetWorkspaceOwnerAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        _store.MutateAsync<string?>(document => new(false,
            document.WorkspaceOwners.TryGetValue(workspaceId, out var owner) ? owner : null), cancellationToken);

    public async Task RequestPasswordResetAsync(
        string? email,
        Uri resetBaseUri,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateEmail(email);
        if (validation.Error is not null) return;
        var rawToken = CreateToken();
        var tokenHash = HashToken(rawToken);
        var now = _time.GetUtcNow();
        var account = await _store.MutateAsync<AccountRecord?>(document =>
        {
            RemoveExpiredTokens(document, now);
            if (!document.EmailIndex.TryGetValue(validation.Normalized!, out var accountId) ||
                !document.Accounts.TryGetValue(accountId, out var found))
                return new(false, (AccountRecord?)null);
            RemoveTokens(document, found.AccountId, ResetPurpose);
            document.Tokens[tokenHash] = new AccountTokenRecord
            {
                AccountId = found.AccountId,
                Purpose = ResetPurpose,
                CreatedUtc = now,
                ExpiresUtc = now.AddHours(1)
            };
            return new(true, found);
        }, cancellationToken);
        if (account is not null && _emailSender.IsConfigured)
        {
            try
            {
                await _emailSender.SendPasswordResetAsync(
                    account.Email, BuildLink(resetBaseUri, "resetToken", rawToken), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The public response remains identical for known and unknown addresses.
                _logger.LogWarning(ex, "A password-reset email could not be delivered.");
            }
        }
    }

    public Task<AccountOperationResult> ResetPasswordAsync(
        string? rawToken,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        var passwordError = ValidatePassword(newPassword);
        if (passwordError is not null) return Task.FromResult(AccountOperationResult.Failure(passwordError));
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 256)
            return Task.FromResult(AccountOperationResult.Failure("The password reset link is invalid or expired."));
        var tokenHash = HashToken(rawToken);
        var now = _time.GetUtcNow();
        return _store.MutateAsync<AccountOperationResult>(document =>
        {
            RemoveExpiredTokens(document, now);
            if (!document.Tokens.TryGetValue(tokenHash, out var token) ||
                token.Purpose != ResetPurpose || token.ExpiresUtc <= now ||
                !document.Accounts.TryGetValue(token.AccountId, out var account))
                return new(false, AccountOperationResult.Failure("The password reset link is invalid or expired."));
            account.PasswordHash = _passwordHasher.HashPassword(account, newPassword!);
            account.PasswordChangedUtc = now;
            account.SecurityVersion++;
            RemoveTokens(document, account.AccountId, ResetPurpose);
            return new(true, AccountOperationResult.Success(account));
        }, cancellationToken);
    }

    public async Task<AccountOperationResult> ChangePasswordAsync(
        string accountId,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        var passwordError = ValidatePassword(newPassword);
        if (passwordError is not null) return AccountOperationResult.Failure(passwordError);
        var now = _time.GetUtcNow();
        return await _store.MutateAsync<AccountOperationResult>(document =>
        {
            if (!document.Accounts.TryGetValue(accountId, out var account) ||
                string.IsNullOrEmpty(currentPassword) ||
                _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) ==
                    PasswordVerificationResult.Failed)
                return new(false, AccountOperationResult.Failure("The current password is incorrect."));
            account.PasswordHash = _passwordHasher.HashPassword(account, newPassword!);
            account.PasswordChangedUtc = now;
            account.SecurityVersion++;
            RemoveTokens(document, account.AccountId, ResetPurpose);
            return new(true, AccountOperationResult.Success(account));
        }, cancellationToken);
    }

    public async Task RequestVerificationAsync(
        string accountId,
        Uri verificationBaseUri,
        CancellationToken cancellationToken = default)
    {
        var rawToken = CreateToken();
        var tokenHash = HashToken(rawToken);
        var now = _time.GetUtcNow();
        var account = await _store.MutateAsync<AccountRecord?>(document =>
        {
            RemoveExpiredTokens(document, now);
            if (!document.Accounts.TryGetValue(accountId, out var found) || found.EmailVerified)
                return new(false, (AccountRecord?)null);
            RemoveTokens(document, found.AccountId, VerificationPurpose);
            document.Tokens[tokenHash] = new AccountTokenRecord
            {
                AccountId = found.AccountId,
                Purpose = VerificationPurpose,
                CreatedUtc = now,
                ExpiresUtc = now.AddHours(24)
            };
            return new(true, found);
        }, cancellationToken);
        if (account is not null && _emailSender.IsConfigured)
        {
            try
            {
                await _emailSender.SendVerificationAsync(
                    account.Email, BuildLink(verificationBaseUri, "verifyEmailToken", rawToken), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "An email-verification message could not be delivered.");
            }
        }
    }

    public Task<AccountOperationResult> VerifyEmailAsync(
        string? rawToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 256)
            return Task.FromResult(AccountOperationResult.Failure("The verification link is invalid or expired."));
        var tokenHash = HashToken(rawToken);
        var now = _time.GetUtcNow();
        return _store.MutateAsync<AccountOperationResult>(document =>
        {
            RemoveExpiredTokens(document, now);
            if (!document.Tokens.TryGetValue(tokenHash, out var token) ||
                token.Purpose != VerificationPurpose || token.ExpiresUtc <= now ||
                !document.Accounts.TryGetValue(token.AccountId, out var account))
                return new(false, AccountOperationResult.Failure("The verification link is invalid or expired."));
            account.EmailVerified = true;
            account.EmailVerifiedUtc = now;
            RemoveTokens(document, account.AccountId, VerificationPurpose);
            return new(true, AccountOperationResult.Success(account));
        }, cancellationToken);
    }

    public static string? ValidatePassword(string? password) => password switch
    {
        null or "" => "Enter a password.",
        { Length: < MinimumPasswordLength } => $"Use at least {MinimumPasswordLength} characters.",
        { Length: > MaximumPasswordLength } => $"Use no more than {MaximumPasswordLength} characters.",
        _ => null
    };

    public static (string? Email, string? Normalized, string? Error) ValidateEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 254)
            return (null, null, "Enter a valid email address.");
        try
        {
            var parsed = new MailAddress(trimmed);
            if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
                return (null, null, "Enter a valid email address.");
            return (trimmed, trimmed.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), null);
        }
        catch (FormatException)
        {
            return (null, null, "Enter a valid email address.");
        }
    }

    internal static string HashToken(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Uri BuildLink(Uri baseUri, string parameter, string token)
    {
        // The fragment is handled by the client and is never sent in the HTTP request or server logs.
        var builder = new UriBuilder(baseUri) { Fragment = $"{parameter}={Uri.EscapeDataString(token)}" };
        return builder.Uri;
    }

    private static bool IsWorkspaceIdAllowed(string workspaceId) =>
        workspaceId == WorkspaceContext.LocalWorkspaceId || WorkspaceIdentity.IsValid(workspaceId);

    private static void RemoveExpiredTokens(AccountRegistryDocument document, DateTimeOffset now)
    {
        foreach (var key in document.Tokens.Where(pair => pair.Value.ExpiresUtc <= now)
            .Select(pair => pair.Key).ToArray()) document.Tokens.Remove(key);
    }

    private static void RemoveTokens(AccountRegistryDocument document, string accountId, string purpose)
    {
        foreach (var key in document.Tokens.Where(pair =>
            pair.Value.AccountId == accountId && pair.Value.Purpose == purpose)
            .Select(pair => pair.Key).ToArray()) document.Tokens.Remove(key);
    }
}
