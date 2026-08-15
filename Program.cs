using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LeidosJobsViewer;
using Microsoft.Extensions.Options;

const int ApplicationPort = 54321;
const string ApplicationUrl = "http://127.0.0.1:54321";

var builder = WebApplication.CreateBuilder(args);

// Bind only the IPv4 loopback adapter. The fixed port is authoritative for
// command-line, Visual Studio, and published-executable launches.
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, ApplicationPort));

builder.Services.Configure<WorkdayOptions>(builder.Configuration.GetSection("Workday"));
builder.Services.AddHttpClient<WorkdayClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<WorkdayOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 5, 120));
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("LeidosJobsViewer/1.0 (personal local utility)");
});
builder.Services.AddSingleton<JobCatalog>();
builder.Services.AddSingleton<AppStateStore>();
builder.Services.AddSingleton<CredentialDetector>();
builder.Services.AddSingleton<AcademicQualificationDetector>();
builder.Services.AddSingleton<AutomaticJobCheckService>();
builder.Services.AddHostedService(services => services.GetRequiredService<AutomaticJobCheckService>());
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

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

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/jobs", (JobCatalog catalog) => Results.Ok(catalog.Snapshot));
app.MapPost("/api/refresh", async (
    JobCatalog catalog,
    AutomaticJobCheckService automaticChecks) =>
{
    var snapshot = await catalog.RefreshAsync();
    if (snapshot.Error is null)
    {
        automaticChecks.ResetSchedule();
    }
    return Results.Ok(snapshot);
});
app.MapGet("/api/location-facets", async (
    string? countryId,
    WorkdayClient workdayClient) =>
    Results.Ok(await workdayClient.FetchLocationFacetsAsync(countryId)));
app.MapPost("/api/query", async Task<IResult> (
    WorkdayQuery requestedQuery,
    AppStateStore stateStore,
    JobCatalog catalog,
    AutomaticJobCheckService automaticChecks) =>
{
    var query = requestedQuery.Normalize();
    if (!query.IncludeAllLocations && query.EffectiveLocationIds.Count == 0)
    {
        return Results.BadRequest(new
        {
            error = "Choose at least one physical location, include Remote/Teleworker jobs, or include all locations."
        });
    }

    var current = await stateStore.LoadSettingsAsync();
    var updated = AppStateStore.NormalizeSettings(current with
    {
        Country = new FacetSelection(query.CountryId, query.CountryLabel),
        Location = null,
        IncludeAllLocations = query.IncludeAllLocations,
        IncludeRemote = query.IncludeRemote,
        SelectedPhysicalLocations = query.PhysicalLocations
    });
    await stateStore.SaveSettingsAsync(updated);
    var snapshot = await catalog.RefreshAsync(WorkdayQuery.FromSettings(updated));
    if (snapshot.Error is null)
    {
        automaticChecks.ResetSchedule();
    }
    return Results.Ok(snapshot);
});
app.MapGet("/api/settings", async (AppStateStore stateStore) =>
    Results.Ok(await stateStore.LoadSettingsAsync()));
app.MapPut("/api/settings", async (
    ViewerSettings settings,
    AppStateStore stateStore,
    AutomaticJobCheckService automaticChecks) =>
{
    var normalized = AppStateStore.NormalizeSettings(settings);
    await stateStore.SaveSettingsAsync(normalized);
    automaticChecks.ApplySettings(normalized);
    return Results.NoContent();
});
app.MapGet("/api/automatic-check/status", (AutomaticJobCheckService automaticChecks) =>
    Results.Ok(automaticChecks.Status));
app.MapPost("/api/automatic-check/run", async (AutomaticJobCheckService automaticChecks) =>
    Results.Ok(await automaticChecks.CheckNowAsync()));
app.MapPost("/api/history/viewed", async (ViewedJobRequest request, JobCatalog catalog) =>
    await catalog.MarkViewedAsync(request.StableId)
        ? Results.NoContent()
        : Results.NotFound());
app.MapPut("/api/history/dismissed", async (DismissedJobRequest request, JobCatalog catalog) =>
    await catalog.SetDismissedAsync(request.StableId, request.Dismissed)
        ? Results.NoContent()
        : Results.NotFound());

var stateStore = app.Services.GetRequiredService<AppStateStore>();
await stateStore.EnsureSettingsFileAsync();
var initialSettings = await stateStore.LoadSettingsAsync();
// Persist normalized defaults when upgrading an older settings document so the
// selected Workday facet IDs and their display labels are explicit on disk.
await stateStore.SaveSettingsAsync(initialSettings);
var catalog = app.Services.GetRequiredService<JobCatalog>();
await catalog.InitializeAsync(WorkdayQuery.FromSettings(initialSettings));
var automaticChecks = app.Services.GetRequiredService<AutomaticJobCheckService>();
automaticChecks.ApplySettings(initialSettings);

try
{
    await app.StartAsync();
}
catch (Exception ex) when (IsAddressInUse(ex))
{
    const string message =
        "Leidos Jobs Viewer could not start because TCP port 54321 is already in use. " +
        "Close the other program using http://127.0.0.1:54321 and try again.";
    app.Logger.LogCritical(ex, "{StartupError}", message);
    Console.Error.WriteLine(message);
    Environment.ExitCode = 1;
    return;
}

app.Logger.LogInformation("Leidos Jobs Viewer is available at {ApplicationUrl}", ApplicationUrl);

app.Logger.LogInformation("Persistent state directory: {DataDirectory}", stateStore.DataDirectory);
if (builder.Configuration.GetValue("Application:RefreshOnStartup", true))
{
    _ = catalog.RefreshAsync();
}

if (builder.Configuration.GetValue("Application:OpenBrowser", true))
{
    try
    {
        Process.Start(new ProcessStartInfo(ApplicationUrl) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not open the default browser. Open {ApplicationUrl} manually.", ApplicationUrl);
    }
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
