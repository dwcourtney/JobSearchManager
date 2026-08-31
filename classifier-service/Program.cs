using System.Diagnostics;
using System.Text.Json;

const string ServiceVersion = "0.1.0";
const string ProtocolVersion = "1";

if (args is ["--healthcheck"])
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var response = await client.GetAsync("http://127.0.0.1:8081/healthz");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}

if (args is ["--self-test"])
{
    var parsed = GpuProbe.Parse("NVIDIA GeForce GTX 1070, 8192, 21, 580.65\n");
    if (parsed is not { GpuAvailable: true, DeviceCount: 1,
        DeviceName: "NVIDIA GeForce GTX 1070", VramTotalMiB: 8192,
        VramUsedMiB: 21, DriverVersion: "580.65" })
        throw new InvalidOperationException("GPU diagnostic schema self-test failed.");
    Console.WriteLine("Classifier GPU schema self-test: PASS");
    return;
}

if (args is ["--gpu-diagnostic"])
{
    var gpu = GpuProbe.Current();
    Console.WriteLine(JsonSerializer.Serialize(gpu));
    Environment.ExitCode = gpu is
    {
        GpuAvailable: true,
        DeviceCount: 1,
        DeviceName: "NVIDIA GeForce GTX 1070"
    } ? 0 : 1;
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8081");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
var app = builder.Build();

app.MapGet("/healthz", () =>
{
    var gpu = GpuProbe.Current();
    return Results.Ok(new
    {
        status = "healthy",
        serviceVersion = ServiceVersion,
        protocolVersion = ProtocolVersion,
        revision = Environment.GetEnvironmentVariable("CLASSIFIER_GIT_SHA") ?? "unknown",
        gpu.GpuAvailable, gpu.DeviceCount, gpu.DeviceName,
        gpu.VramTotalMiB, gpu.VramUsedMiB, gpu.DriverVersion
    });
});

app.MapPost("/classify", (ClassifierRequest request, ILogger<Program> logger) =>
{
    var started = Stopwatch.StartNew();
    var invalid = new[]
    {
        string.IsNullOrWhiteSpace(request.JobId) ? "jobId" : null,
        string.IsNullOrWhiteSpace(request.Title) ? "title" : null,
        request.Description is null ? "description" : null
    }.Where(value => value is not null).ToArray();
    if (invalid.Length > 0)
        return Results.BadRequest(new { error = "invalid request", fields = invalid });

    var gpu = GpuProbe.Current();
    var descriptionLength = request.Description!.EnumerateRunes().Count();
    logger.LogInformation(
        "Classified jobId={JobId} characters={CharacterCount} durationMs={DurationMs:F3} gpuAvailable={GpuAvailable}",
        LogValue(request.JobId!), descriptionLength, started.Elapsed.TotalMilliseconds,
        gpu.GpuAvailable);
    return Results.Ok(new
    {
        received = true,
        jobId = request.JobId,
        title = request.Title,
        descriptionLength,
        serviceVersion = ServiceVersion,
        protocolVersion = ProtocolVersion,
        revision = Environment.GetEnvironmentVariable("CLASSIFIER_GIT_SHA") ?? "unknown",
        gpu.GpuAvailable, gpu.DeviceCount, gpu.DeviceName,
        gpu.VramTotalMiB, gpu.VramUsedMiB, gpu.DriverVersion
    });
});

app.Run();

static string LogValue(string value) =>
    value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 160)];

public sealed record ClassifierRequest(string? JobId, string? Title, string? Description);
public sealed record GpuDiagnostic(
    bool GpuAvailable, int DeviceCount, string? DeviceName,
    int? VramTotalMiB, int? VramUsedMiB, string? DriverVersion);

public static class GpuProbe
{
    public static GpuDiagnostic Current()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total,memory.used,driver_version --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null || !process.WaitForExit(3000) || process.ExitCode != 0)
            {
                try { process?.Kill(entireProcessTree: true); } catch { }
                return Unavailable;
            }
            return Parse(process.StandardOutput.ReadToEnd());
        }
        catch { return Unavailable; }
    }

    public static GpuDiagnostic Parse(string output)
    {
        var rows = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length == 0) return Unavailable;
        var values = rows[0].Split(',', 4, StringSplitOptions.TrimEntries);
        return values.Length == 4 && int.TryParse(values[1], out var total) &&
            int.TryParse(values[2], out var used)
            ? new(true, rows.Length, values[0], total, used, values[3])
            : Unavailable;
    }

    private static GpuDiagnostic Unavailable { get; } = new(false, 0, null, null, null, null);
}
