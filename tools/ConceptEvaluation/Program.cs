using System.Security.Cryptography;
using System.Text.Json;
using JobSearchManager;

// Offline evaluation only. No server, cache writes, provider requests or model calls.
if (args is not [var input, var output]) throw new ArgumentException("Usage: ConceptEvaluation sample.json predictions.json");
var directory = Path.GetDirectoryName(Path.GetFullPath(input))!;
using var seal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "reference-seal.json")));
if (seal.RootElement.GetProperty("sampleHash").GetString() != Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))) ||
    seal.RootElement.GetProperty("referenceHash").GetString() != Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "reference-labels.json")))))
    throw new InvalidDataException("Freeze the complete reference before generating detector predictions.");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var catalog = JobConceptCatalog.LoadDefault();
var frozenTaxonomy = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(directory, "taxonomy.json")).Replace("\r\n", "\n", StringComparison.Ordinal));
if (Convert.ToHexStringLower(SHA256.HashData(frozenTaxonomy)) != catalog.Fingerprint)
    throw new InvalidDataException("Current taxonomy differs from the frozen labeling definitions; create a new reference run.");
var snapshot = ConceptRuleSnapshot.Load(Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json"), catalog);
using var sample = JsonDocument.Parse(File.ReadAllBytes(input));
var rows = new List<object>();
foreach (var posting in sample.RootElement.GetProperty("postings").EnumerateArray())
{
    var title = posting.GetProperty("title").GetString()!;
    var html = posting.GetProperty("descriptionHtml").GetString()!;
    var location = posting.GetProperty("primaryLocation").GetString()!;
    var additional = posting.GetProperty("additionalLocations").EnumerateArray().Select(x => x.GetString()!).ToArray();
    var remote = new RemoteWorkDetector().Analyze(title, location, additional, html);
    var extended = new ExtendedLocationRequirementDetector().Analyze(title, location, additional, html);
    var result = snapshot.Classify(title, html, remote, extended);
    // A partial timeout result is not a valid evaluation run.
    if (result.TimedOutRuleIds.Count > 0) throw new InvalidDataException("Evaluation regex timeout; retry this run before publishing.");
    rows.Add(new { id = posting.GetProperty("id").GetString(), result.Concepts, result.MatchedRuleIds });
}
using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
JsonSerializer.Serialize(file, new { schemaVersion = 2, authority = snapshot.Identity,
    sampleHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))),
    taxonomyHash = catalog.Fingerprint, rules = snapshot.Rules, postings = rows }, json);
