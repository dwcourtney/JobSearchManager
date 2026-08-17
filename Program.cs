using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using JobSearchManager;

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
builder.Services.AddSingleton<CompanyCatalog>();
builder.Services.AddSingleton<CredentialDetector>();
builder.Services.AddSingleton<AcademicQualificationDetector>();
builder.Services.AddSingleton<WorkAuthorizationDetector>();
builder.Services.AddSingleton<RemoteWorkDetector>();
builder.Services.AddSingleton<PortableWorkspaceService>();
// Preserve the established data-protection discriminator so existing Azure
// workspace cookies can be decrypted and migrated to the new cookie name.
builder.Services.AddDataProtection().SetApplicationName("WorkdayJobManager");
builder.Services.AddScoped<WorkspaceContext>();
builder.Services.AddScoped<WorkspaceRuntimeProvider>();

if (hosting.IsAzure)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
    builder.Services.AddSingleton(_ => new DefaultAzureCredential(
        new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        }));
    builder.Services.AddSingleton(services =>
        new BlobServiceClient(
            hosting.GetBlobServiceUri(),
            services.GetRequiredService<DefaultAzureCredential>()));
    builder.Services.AddSingleton(services =>
        services.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient(hosting.StorageContainer));
    builder.Services.AddSingleton<IWorkspaceDataStoreFactory, AzureBlobWorkspaceDataStoreFactory>();
}
else
{
    builder.Services.AddSingleton<IWorkspaceDataStoreFactory, FileWorkspaceDataStoreFactory>();
    builder.Services.AddHostedService<LocalAutomaticCheckHostedService>();
}

builder.Services.AddSingleton<WorkspaceRuntimeManager>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many job-source requests. Wait briefly and try again." },
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

app.UseResponseCompression();

if (hosting.IsAzure)
{
    app.UseForwardedHeaders();
    app.UseHsts();
    app.UseHttpsRedirection();
}

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

app.UseMiddleware<WorkspaceIdentityMiddleware>();

if (hosting.IsAzure)
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
        company.PublicSiteUrl
    })));

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
    if (snapshot.Error is null)
    {
        runtime.AutomaticChecks.ResetSchedule();
    }
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
    runtime.AutomaticChecks.ApplySettings(updated);
    var snapshot = await runtime.Catalog.SwitchSourceAsync(
        JobSourceQuery.FromSettings(updated, companies), token);
    if (snapshot.Error is null)
    {
        runtime.AutomaticChecks.ResetSchedule();
    }
    else
    {
        await stateStore.SaveSettingsAsync(current);
        runtime.AutomaticChecks.ApplySettings(current);
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
    runtime.AutomaticChecks.ApplySettings(normalized);
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
        runtime.AutomaticChecks.ApplySettings(normalized);
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
    if (hosting.IsAzure)
    {
        context.Response.Cookies.Delete(
            WorkspaceIdentity.CookieName,
            WorkspaceIdentity.CreateCookieOptions(secure: true));
    }
    return Results.Ok(new { deletedDocuments });
}).RequireRateLimiting("state");

app.MapGet("/api/automatic-check/status", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    Results.Ok((await provider.GetAsync(token)).AutomaticChecks.Status));

app.MapPost("/api/automatic-check/run", async (
    WorkspaceRuntimeProvider provider,
    CancellationToken token) =>
    Results.Ok(await (await provider.GetAsync(token)).AutomaticChecks.CheckNowAsync(token)))
    .RequireRateLimiting("provider");

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
    await (await provider.GetAsync(token)).Catalog.SetWorkflowStateAsync(request.StableId, request.State)
        ? Results.NoContent()
        : Results.BadRequest())
    .RequireRateLimiting("state");

var dataStores = app.Services.GetRequiredService<IWorkspaceDataStoreFactory>();
await dataStores.ValidateAsync();
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
        "Job Search Manager started in Azure mode with anonymous Blob-backed workspaces in container {ContainerName}.",
        hosting.StorageContainer);
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
