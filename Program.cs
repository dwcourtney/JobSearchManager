using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using JobSearchManager;

if (args is ["--healthcheck"])
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync("http://127.0.0.1:8080/healthz");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        Environment.ExitCode = 1;
    }
    return;
}

if (args is ["--classifier-diagnostic"])
{
    try
    {
        var baseUrl = Environment.GetEnvironmentVariable("Classifier__BaseUrl")
            ?? "http://job-classifier:8081/";
        using var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(120)
        };
        using var content = ClassifierClient.CreateJsonContent(new ClassifierRequest(
            "R180395", "Senior Software Developer", "Phase 1 deployment plumbing proof."));
        using var response = await client.PostAsync("classify", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SemanticClassifierResponse>(
            ClassifierClient.JsonOptions);
        var valid = result is
        {
            Received: true,
            JobId: "R180395",
            Title: "Senior Software Developer",
            DescriptionLength: 34,
            GpuAvailable: true,
            DeviceName: "NVIDIA GeForce GTX 1070",
            ConceptCount: 85,
            Predictions.Count: 85
        };
        Console.WriteLine(JsonSerializer.Serialize(new { valid, result }));
        Environment.ExitCode = valid ? 0 : 1;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Classifier diagnostic failed: {exception.GetType().Name}");
        Environment.ExitCode = 1;
    }
    return;
}

const int ApplicationPort = 54321;
const string ApplicationUrl = "http://127.0.0.1:54321";

var builder = WebApplication.CreateBuilder(args);
var hosting = HostingConfiguration.FromConfiguration(builder.Configuration);

if (hosting.IsLocal)
{
    // Local mode retains its authoritative fixed loopback endpoint regardless
    // of launchSettings.json, the current directory, or shell configuration.
    builder.WebHost.ConfigureKestrel(options =>
        options.Listen(IPAddress.Loopback, ApplicationPort));
}

builder.Services.AddSingleton(hosting);
var jobSourceConfiguration = builder.Configuration.GetSection("JobSource");
if (!jobSourceConfiguration.Exists())
{
    // Compatibility for deployments that supplied the pre-rebrand provider section.
    jobSourceConfiguration = builder.Configuration.GetSection("Workday");
}
builder.Services.Configure<JobSourceOptions>(jobSourceConfiguration);
builder.Services.AddHttpClient<JobSourceClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<JobSourceOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 5, 120));
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("JobSearchManager/1.0");
});
builder.Services.AddHttpClient<ClassifierClient>((services, client) =>
{
    var baseUrl = services.GetRequiredService<IConfiguration>()["Classifier:BaseUrl"]
        ?? "http://job-classifier:8081/";
    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(300);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("JobSearchManager/1.0");
});
builder.Services.AddSingleton<CompanyCatalog>();
builder.Services.AddSingleton<CredentialDetector>();
builder.Services.AddSingleton<AcademicQualificationDetector>();
builder.Services.AddSingleton<WorkAuthorizationDetector>();
builder.Services.AddSingleton<RemoteWorkDetector>();
builder.Services.AddSingleton<ExtendedLocationRequirementDetector>();
builder.Services.AddSingleton<JobConceptCatalog>();
builder.Services.AddSingleton<SemanticClassificationService>();
builder.Services.AddSingleton<PortableWorkspaceService>();
builder.Services.AddSingleton<SharedSourceRefreshCoordinator>();
builder.Services.AddJobSearchManagerDataProtection(hosting);
builder.Services.AddScoped<WorkspaceContext>();
builder.Services.AddScoped<WorkspaceRuntimeProvider>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<AccountRecord>, PasswordHasher<AccountRecord>>();
builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();

builder.Services.AddSingleton<IWorkspaceDataStoreFactory, FileWorkspaceDataStoreFactory>();
builder.Services.AddSingleton<IAccountRegistryStore, FileAccountRegistryStore>();

builder.Services.AddSingleton<WorkspaceRuntimeManager>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<AdminBootstrapService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = AccountAuthentication.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(180);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            if (!context.HttpContext.Request.Path.StartsWithSegments("/api")) return;
            var accountId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var versionText = context.Principal?.FindFirstValue(AccountAuthentication.SecurityVersionClaim);
            var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
            var account = await accounts.GetByIdAsync(accountId, context.HttpContext.RequestAborted);
            if (account is null || !int.TryParse(versionText, out var version) ||
                version != account.SecurityVersion)
            {
                context.RejectPrincipal();
            }
            else
            {
                context.HttpContext.Items[AccountAuthentication.ResolvedAccountItem] = account;
            }
        };
    });
builder.Services.AddSingleton<IAuthorizationHandler, AdminAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AdminAuthorization.Policy, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new AdminRequirement());
    }));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Wait briefly and try again." },
            cancellationToken);
    };
    options.AddPolicy("provider", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("state", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("authentication", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("admin-bootstrap", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);

var app = builder.Build();
var versionInfo = VersionEndpoint.Create(builder.Configuration, hosting);

app.Use(async (context, next) =>
{
    if (await HealthEndpoint.TryHandleAsync(context)) return;
    if (await VersionEndpoint.TryHandleAsync(context, versionInfo)) return;
    await next();
});

app.UseResponseCompression();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = exception switch
    {
        WorkspaceConcurrencyException => (
            StatusCodes.Status409Conflict,
            "Workspace state changed in another request. Reload and try again."),
        WorkspaceBusyException => (
            StatusCodes.Status409Conflict,
            "The workspace cannot be reset while a job refresh is in progress. Try again when it finishes."),
        WorkspaceStorageException => (
            StatusCodes.Status503ServiceUnavailable,
            "Workspace storage is temporarily unavailable. Your workspace was not changed."),
        _ => (
            StatusCodes.Status500InternalServerError,
            "The server could not complete the request.")
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/json";
    context.Response.Headers.CacheControl = "no-store";
    await context.Response.WriteAsJsonAsync(new { error = message });
}));

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self'; font-src 'self'; object-src 'none'; frame-src 'none'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.XFrameOptions = "DENY";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next();
});

app.UseAuthentication();
app.UseMiddleware<WorkspaceIdentityMiddleware>();

if (hosting.RequiresSameOriginProtection)
{
    app.Use(async (context, next) =>
    {
        if (RequestSecurity.IsStateChangingApiRequest(context.Request))
        {
            if (!RequestSecurity.HasSameOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "The state-changing request did not originate from this application."
                });
                return;
            }
        }

        await next();
    });
}

app.UseRateLimiter();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/jobs", async (WorkspaceRuntimeProvider provider, CancellationToken token) =>
{
    var compact = JobsListSnapshot.FromSnapshot((await provider.GetAsync(token)).Catalog.Snapshot);
    app.Logger.LogInformation(
        "Compact job-list response contains {JobCount} jobs; full descriptions are excluded.",
        compact.Jobs.Count);
    return Results.Ok(compact);
});

app.MapGet("/api/jobs/status", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
{
    var snapshot = (await provider.GetAsync(token)).Catalog.Snapshot;
    return Results.Ok(new
    {
        snapshot.IsRefreshing,
        snapshot.Error,
        snapshot.RefreshProgress,
        snapshot.LastRefreshedUtc,
        snapshot.Metrics
    });
});

app.MapGet("/api/jobs/detail", async Task<IResult> (
    string stableId,
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
{
    var detail = await (await provider.GetAsync(token)).Catalog.GetJobDetailAsync(stableId, token);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}).RequireRateLimiting("provider");

app.MapPost("/api/jobs/description-matches", async (
    DescriptionMatchRequest request,
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    Results.Ok((await provider.GetAsync(token)).Catalog.GetDescriptionMatches(request)))
    .RequireRateLimiting("state");

app.MapGet("/api/companies", (CompanyCatalog companies) => Results.Ok(
    companies.Companies.Select(company => new
    {
        company.Id,
        company.DisplayName,
        company.IndustryCategory,
        company.PublicSiteUrl
    })));

app.MapGet("/api/credentials", (CredentialDetector credentials) =>
    Results.Ok(credentials.CatalogItems));

app.MapGet("/api/job-fit/concepts", (JobConceptCatalog concepts) =>
    Results.Ok(concepts.Options));

app.MapGet("/api/workspace/identity", (WorkspaceContext workspace, HttpContext context) =>
{
    var authenticated = context.User.Identity?.IsAuthenticated == true;
    return Results.Ok(new
    {
        workspaceId = authenticated ? null : workspace.WorkspaceId,
        internalWorkspaceId = authenticated && WorkspaceIdentity.IsValid(workspace.WorkspaceId)
            ? WorkspaceIdentity.Redact(workspace.WorkspaceId) : null,
        accessMode = authenticated ? "authenticated" : "anonymous",
        canCopyWorkspaceId = !authenticated
    });
});

app.MapGet("/api/account/status", async (
    HttpContext context,
    WorkspaceContext workspace,
    AccountService accounts,
    AdminBootstrapService adminBootstrap,
    CancellationToken token) =>
{
    var accountId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var account = accountId is null
        ? null
        : context.Items[AccountAuthentication.ResolvedAccountItem] as AccountRecord ??
            await accounts.GetByIdAsync(accountId, token);
    var bootstrapAvailable = account is not null &&
        await adminBootstrap.IsAvailableAsync(token);
    return Results.Ok(account is null
        ? (object)new
        {
            authenticated = false,
            email = (string?)null,
            emailVerified = false,
            workspace = "anonymous",
            persistence = AccountPersistence.Session,
            emailDeliveryConfigured = accounts.EmailDeliveryConfigured,
            isAdmin = false,
            administratorBootstrapAvailable = false
        }
        : (object)new
        {
            authenticated = true,
            email = account.Email,
            emailVerified = account.EmailVerified,
            workspace = "authenticated",
            persistence = AccountPersistence.Normalize(
                context.User.FindFirstValue(AccountAuthentication.PersistenceClaim)),
            emailDeliveryConfigured = accounts.EmailDeliveryConfigured,
            isAdmin = AccountRoles.IsAdmin(account),
            administratorBootstrapAvailable = bootstrapAvailable
        });
});

app.MapPost("/api/account/admin-bootstrap", async Task<IResult> (
    AdminBootstrapRequest request,
    HttpContext context,
    AdminBootstrapService adminBootstrap,
    CancellationToken token) =>
{
    var result = await adminBootstrap.ClaimAsync(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.Code, token);
    return result.Succeeded
        ? Results.Ok(new
        {
            isAdmin = true,
            administratorBootstrapAvailable = false
        })
        : Results.BadRequest(new { error = result.Error });
}).RequireAuthorization().RequireRateLimiting("admin-bootstrap");

app.MapGet("/api/admin/status", (HttpContext context) =>
{
    var account = context.Items[AccountAuthentication.ResolvedAccountItem] as AccountRecord;
    return Results.Ok(new
    {
        administrator = true,
        email = account?.Email ?? context.User.FindFirstValue(ClaimTypes.Name)
    });
}).RequireAuthorization(AdminAuthorization.Policy);

app.MapPost("/api/admin/classifier-diagnostic", async Task<IResult> (
    ClassifierRequest request,
    ClassifierClient classifier,
    CancellationToken token) =>
{
    if (string.IsNullOrWhiteSpace(request.JobId) || string.IsNullOrWhiteSpace(request.Title) ||
        request.Description is null)
    {
        return Results.BadRequest(new { error = "jobId, title, and description are required." });
    }
    var result = await classifier.ClassifyAsync(request, token);
    return Results.Json(result, statusCode: result.Available
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable);
}).RequireAuthorization(AdminAuthorization.Policy).RequireRateLimiting("state");

app.MapGet("/api/admin/classifier/backfill/status", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    Results.Ok((await provider.GetAsync(token)).Catalog.GetSemanticClassificationStatus()))
    .RequireAuthorization(AdminAuthorization.Policy);

app.MapPost("/api/admin/classifier/backfill", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    Results.Accepted(value:
        (await provider.GetAsync(token)).Catalog.StartSemanticClassificationBackfill()))
    .RequireAuthorization(AdminAuthorization.Policy).RequireRateLimiting("state");

app.MapPost("/api/account/create", async Task<IResult> (
    CreateAccountRequest request,
    HttpContext context,
    WorkspaceContext workspace,
    AccountService accounts,
    IConfiguration configuration,
    CancellationToken token) =>
{
    if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "The password confirmation does not match." });
    var result = await accounts.CreateAsync(
        workspace.WorkspaceId, request.Email, request.Password,
        PublicBaseUri(context.Request, configuration), token);
    if (!result.Succeeded) return Results.BadRequest(new { error = result.Error });
    var persistence = AccountPersistence.Normalize(request.Persistence);
    await SignInAccountAsync(context, result.Account!, persistence);
    context.Response.Cookies.Delete(
        WorkspaceIdentity.CookieName, WorkspaceIdentity.CreateCookieOptions(secure: false));
    return Results.Ok(new
    {
        authenticated = true,
        email = result.Account!.Email,
        emailVerified = result.Account.EmailVerified,
        workspace = "authenticated",
        persistence,
        emailDeliveryConfigured = accounts.EmailDeliveryConfigured
    });
}).RequireRateLimiting("authentication");

app.MapPost("/api/account/login", async Task<IResult> (
    LoginRequest request,
    HttpContext context,
    AccountService accounts,
    CancellationToken token) =>
{
    var account = await accounts.AuthenticateAsync(request.Email, request.Password, token);
    if (account is null) return Results.Json(
        new { error = "The email or password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);
    var persistence = AccountPersistence.Normalize(request.Persistence);
    await SignInAccountAsync(context, account, persistence);
    context.Response.Cookies.Delete(
        WorkspaceIdentity.CookieName, WorkspaceIdentity.CreateCookieOptions(secure: false));
    return Results.Ok(new { authenticated = true, account.Email, account.EmailVerified, persistence });
}).RequireRateLimiting("authentication");

app.MapPost("/api/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Cookies.Delete(
        WorkspaceIdentity.CookieName, WorkspaceIdentity.CreateCookieOptions(secure: false));
    return Results.NoContent();
}).RequireRateLimiting("state");

app.MapPost("/api/account/forgot-password", async (
    EmailRequest request,
    HttpContext context,
    AccountService accounts,
    IConfiguration configuration,
    CancellationToken token) =>
{
    await accounts.RequestPasswordResetAsync(
        request.Email, PublicBaseUri(context.Request, configuration), token);
    return Results.Ok(new
    {
        message = "If an account exists for that email, a reset message has been sent."
    });
}).RequireRateLimiting("authentication");

app.MapPost("/api/account/reset-password", async Task<IResult> (
    ResetPasswordRequest request,
    AccountService accounts,
    CancellationToken token) =>
{
    if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "The password confirmation does not match." });
    var result = await accounts.ResetPasswordAsync(request.Token, request.Password, token);
    return result.Succeeded
        ? Results.Ok(new { message = "Your password has been reset. Sign in with the new password." })
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("authentication");

app.MapPost("/api/account/change-password", async Task<IResult> (
    ChangePasswordRequest request,
    HttpContext context,
    AccountService accounts,
    CancellationToken token) =>
{
    if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "The password confirmation does not match." });
    var accountId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var result = await accounts.ChangePasswordAsync(
        accountId, request.CurrentPassword, request.Password, token);
    if (!result.Succeeded) return Results.BadRequest(new { error = result.Error });
    await SignInAccountAsync(
        context, result.Account!, AccountPersistence.Normalize(request.Persistence));
    return Results.Ok(new { message = "Password changed. Other signed-in sessions are no longer valid." });
}).RequireAuthorization().RequireRateLimiting("authentication");

app.MapPost("/api/account/session", async Task<IResult> (
    SessionPersistenceRequest request,
    HttpContext context,
    AccountService accounts,
    CancellationToken token) =>
{
    var account = await accounts.GetByIdAsync(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier), token);
    if (account is null) return Results.Unauthorized();
    var persistence = AccountPersistence.Normalize(request.Persistence);
    await SignInAccountAsync(context, account, persistence);
    return Results.Ok(new { persistence });
}).RequireAuthorization().RequireRateLimiting("state");

app.MapPost("/api/account/request-verification", async (
    HttpContext context,
    AccountService accounts,
    IConfiguration configuration,
    CancellationToken token) =>
{
    await accounts.RequestVerificationAsync(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        PublicBaseUri(context.Request, configuration), token);
    return Results.Ok(new { message = "If email delivery is configured, a verification message has been sent." });
}).RequireAuthorization().RequireRateLimiting("authentication");

app.MapPost("/api/account/verify-email", async Task<IResult> (
    TokenRequest request,
    AccountService accounts,
    CancellationToken token) =>
{
    var result = await accounts.VerifyEmailAsync(request.Token, token);
    return result.Succeeded
        ? Results.Ok(new { message = "Email address verified." })
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("authentication");

app.MapPost("/api/refresh", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
{
    var runtime = await provider.GetAsync(token);
    var settings = await runtime.StateStore.LoadSettingsAsync();
    if (settings.HasConfiguredSource != true)
    {
        return Results.Conflict(new { error = "Configure and apply a job source before refreshing jobs." });
    }
    var snapshot = await runtime.Catalog.RefreshAsync(token);
    return Results.Ok(JobsListSnapshot.FromSnapshot(snapshot));
}).RequireRateLimiting("provider");

app.MapGet("/api/location-facets", async Task<IResult> (
    string companyId,
    string? countryId,
    JobSourceClient jobSourceClient,
    CompanyCatalog companies,
    CancellationToken token) =>
{
    if (!companies.TryGet(companyId, out var company))
    {
        return Results.BadRequest(new { error = "Choose a supported company." });
    }

    return Results.Ok(await jobSourceClient.FetchLocationFacetsAsync(company, countryId, token));
}).RequireRateLimiting("provider");

app.MapGet("/api/source/{companyId}", async Task<IResult> (
    string companyId,
    WorkspaceRuntimeProvider provider,
    CompanyCatalog companies,
    CancellationToken token) =>
{
    if (!companies.TryGet(companyId, out var company))
    {
        return Results.NotFound();
    }

    var stateStore = (await provider.GetAsync(token)).StateStore;
    var settings = await stateStore.LoadSettingsAsync();
    var hasSavedCompanySource = settings.CompanySources?.ContainsKey(company.Id) == true;
    var source = settings.HasConfiguredSource != true && !hasSavedCompanySource
        ? new CompanySourceSettings(company.DefaultCountry, false, false, [])
        : stateStore.GetSourceSettings(settings, company.Id);
    return Results.Ok(new
    {
        company.Id,
        company.DisplayName,
        source
    });
});

app.MapPost("/api/query", async Task<IResult> (
    JobSourceQuery requestedQuery,
    WorkspaceRuntimeProvider provider,
    CompanyCatalog companies,
    CancellationToken token) =>
{
    if (!companies.TryGet(requestedQuery.CompanyId, out var company))
    {
        return Results.BadRequest(new { error = "Choose a supported company." });
    }

    var query = requestedQuery.Normalize(company);
    if (!query.IncludeAllLocations && query.EffectiveLocationIds(company).Count == 0)
    {
        return Results.BadRequest(new
        {
            error = "Choose at least one physical location, include remote jobs when available, or include all locations."
        });
    }

    var runtime = await provider.GetAsync(token);
    var stateStore = runtime.StateStore;
    var current = await stateStore.LoadSettingsAsync();
    var source = new CompanySourceSettings(
        new FacetSelection(query.CountryId, query.CountryLabel),
        query.IncludeAllLocations,
        query.IncludeRemote,
        query.PhysicalLocations ?? []);
    var companySources = new Dictionary<string, CompanySourceSettings>(
        current.CompanySources ?? new Dictionary<string, CompanySourceSettings>(),
        StringComparer.OrdinalIgnoreCase)
    {
        [company.Id] = source
    };
    var updated = stateStore.NormalizeSettings(current with
    {
        CompanyId = company.Id,
        Country = new FacetSelection(query.CountryId, query.CountryLabel),
        Location = null,
        IncludeAllLocations = query.IncludeAllLocations,
        IncludeRemote = query.IncludeRemote,
        SelectedPhysicalLocations = query.PhysicalLocations,
        CompanySources = companySources,
        HasConfiguredSource = true,
        PendingSource = null
    });
    await stateStore.SaveSettingsAsync(updated);
    var snapshot = await runtime.Catalog.SwitchSourceAsync(
        JobSourceQuery.FromSettings(updated, companies), token);
    if (snapshot.Error is not null)
    {
        await stateStore.SaveSettingsAsync(current);
    }
    return Results.Ok(JobsListSnapshot.FromSnapshot(snapshot));
}).RequireRateLimiting("provider");

app.MapGet("/api/settings", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    Results.Ok(await (await provider.GetAsync(token)).StateStore.LoadSettingsAsync()));

app.MapPut("/api/settings", async (
    ViewerSettings settings,
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
{
    var runtime = await provider.GetAsync(token);
    var current = await runtime.StateStore.LoadSettingsAsync();
    var normalized = runtime.StateStore.NormalizeSettings(settings with
    {
        CompanySources = current.CompanySources,
        HasConfiguredSource = current.HasConfiguredSource,
        PendingSource = current.PendingSource
    });
    await runtime.StateStore.SaveSettingsAsync(normalized);
    return Results.NoContent();
}).RequireRateLimiting("state");

app.MapGet("/api/workspace/export", async (
    WorkspaceRuntimeProvider provider,
    PortableWorkspaceService portableWorkspace,
    CancellationToken token) =>
{
    var runtime = await provider.GetAsync(token);
    var settings = await runtime.StateStore.LoadSettingsAsync();
    var history = await runtime.StateStore.LoadJobHistoryAsync();
    return Results.Ok(portableWorkspace.Export(settings, history));
});

app.MapPost("/api/workspace/import", async Task<IResult> (
    HttpRequest request,
    WorkspaceRuntimeProvider provider,
    PortableWorkspaceService portableWorkspace,
    CancellationToken token) =>
{
    if (request.ContentLength is > PortableWorkspaceService.MaximumImportBytes)
    {
        return Results.BadRequest(new { error = "The workspace file is too large." });
    }
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync(token);
    if (json.Length > PortableWorkspaceService.MaximumImportBytes)
    {
        return Results.BadRequest(new { error = "The workspace file is too large." });
    }

    var runtime = await provider.GetAsync(token);
    var currentSettings = await runtime.StateStore.LoadSettingsAsync();
    var currentHistory = await runtime.StateStore.LoadJobHistoryAsync();
    try
    {
        // Validate the complete document before either durable file is changed.
        var imported = portableWorkspace.ImportJson(json, currentSettings, currentHistory);
        var normalized = runtime.StateStore.NormalizeSettings(imported.Settings);
        try
        {
            await runtime.StateStore.SaveJobHistoryAsync(imported.History);
            await runtime.StateStore.SaveSettingsAsync(normalized);
        }
        catch
        {
            // Restore both prior documents if a valid import cannot be persisted completely.
            await runtime.StateStore.SaveJobHistoryAsync(currentHistory);
            await runtime.StateStore.SaveSettingsAsync(currentSettings);
            throw;
        }
        await runtime.Catalog.ReloadHistoryAsync();
        return Results.Ok(new
        {
            settings = normalized,
            snapshot = JobsListSnapshot.FromSnapshot(runtime.Catalog.Snapshot),
            curatedJobCount = imported.History.Jobs.Count(pair =>
                JobWorkflowStates.Normalize(pair.Value.WorkflowState) != JobWorkflowStates.Normal)
        });
    }
    catch (WorkspaceImportException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("state");

app.MapDelete("/api/workspace", async (
    WorkspaceContext workspace,
    WorkspaceRuntimeManager runtimes,
    HttpContext context,
    CancellationToken token) =>
{
    var deletedDocuments = await runtimes.ResetAsync(workspace.WorkspaceId, token);
    if (hosting.UsesPerBrowserWorkspaces && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Cookies.Delete(
            WorkspaceIdentity.CookieName,
            WorkspaceIdentity.CreateCookieOptions(secure: false));
    }
    return Results.Ok(new { deletedDocuments });
}).RequireRateLimiting("state");

app.MapPost("/api/history/viewed", async (
    ViewedJobRequest request,
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    await (await provider.GetAsync(token)).Catalog.MarkViewedAsync(request.StableId)
        ? Results.NoContent()
        : Results.NotFound())
    .RequireRateLimiting("state");

app.MapPut("/api/history/workflow-state", async (
    JobWorkflowStateRequest request,
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    await (await provider.GetAsync(token)).Catalog.SetWorkflowStateAsync(
        request.StableId, request.State, request.CloseReason)
        ? Results.NoContent()
        : Results.BadRequest())
    .RequireRateLimiting("state");

var dataStores = app.Services.GetRequiredService<IWorkspaceDataStoreFactory>();
await dataStores.ValidateAsync();
await app.Services.GetRequiredService<IAccountRegistryStore>().ValidateAsync();
await app.Services.GetRequiredService<AdminBootstrapService>().InitializeAsync();
WorkspaceRuntime? localRuntime = null;
if (hosting.IsLocal)
{
    localRuntime = await app.Services.GetRequiredService<WorkspaceRuntimeManager>()
        .GetAsync(WorkspaceContext.LocalWorkspaceId);
}

try
{
    await app.StartAsync();
}
catch (Exception ex) when (hosting.IsLocal && IsAddressInUse(ex))
{
    const string message =
        "Job Search Manager could not start because TCP port 54321 is already in use. " +
        "Close the other program using http://127.0.0.1:54321 and try again.";
    app.Logger.LogCritical(ex, "{StartupError}", message);
    Console.Error.WriteLine(message);
    Environment.ExitCode = 1;
    return;
}

if (hosting.IsLocal)
{
    app.Logger.LogInformation("Job Search Manager is available at {ApplicationUrl}", ApplicationUrl);
    app.Logger.LogInformation(
        "Persistent state directory: {DataDirectory}",
        localRuntime!.StateStore.DataDirectory);
    if (builder.Configuration.GetValue("Application:RefreshOnStartup", true) &&
        (await localRuntime.StateStore.LoadSettingsAsync()).HasConfiguredSource == true)
    {
        _ = localRuntime.Catalog.RefreshAsync();
    }

    if (builder.Configuration.GetValue("Application:OpenBrowser", true))
    {
        try
        {
            Process.Start(new ProcessStartInfo(ApplicationUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(
                ex,
                "Could not open the default browser. Open {ApplicationUrl} manually.",
                ApplicationUrl);
        }
    }
}
else
{
    app.Logger.LogInformation(
        "Job Search Manager started in Container mode with browser-isolated workspaces, local filesystem persistence, and Data Protection keys at {DataProtectionPath}.",
        hosting.DataProtectionPath);
}

await app.WaitForShutdownAsync();

static bool IsAddressInUse(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException!)
    {
        if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } ||
            string.Equals(current.GetType().Name, "AddressInUseException", StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static async Task SignInAccountAsync(HttpContext context, AccountRecord account, string persistence)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, account.AccountId),
        new Claim(ClaimTypes.Name, account.Email),
        new Claim(AccountAuthentication.SecurityVersionClaim, account.SecurityVersion.ToString()),
        new Claim(AccountAuthentication.PersistenceClaim, persistence)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var lifetime = AccountPersistence.Lifetime(persistence);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = lifetime.HasValue,
            ExpiresUtc = lifetime.HasValue ? DateTimeOffset.UtcNow.Add(lifetime.Value) : null,
            AllowRefresh = lifetime.HasValue
        });
}

static Uri PublicBaseUri(HttpRequest request, IConfiguration configuration)
{
    var configured = configuration["JOBSEARCHMANAGER_PUBLIC_BASE_URL"]?.Trim();
    if (Uri.TryCreate(configured, UriKind.Absolute, out var publicUri) &&
        publicUri.Scheme is "https" or "http") return new Uri(publicUri, "/");
    return new Uri($"{request.Scheme}://{request.Host}/", UriKind.Absolute);
}
