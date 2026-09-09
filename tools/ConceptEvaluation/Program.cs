using System.Security.Cryptography;
using System.Text.Json;
using JobSearchManager;

// Offline evaluation only. No server, cache writes, provider requests or model calls.
if (args.Length != 2 && (args.Length != 4 || args[2] != "--rules"))
    throw new ArgumentException("Usage: ConceptEvaluation sample.json predictions.json [--rules isolated-rules.json]");
var input = args[0]; var output = args[1];
var rulePath = args.Length == 4 ? Path.GetFullPath(args[3]) : Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json");
var directory = Path.GetDirectoryName(Path.GetFullPath(input))!;
using var seal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "reference-seal.json")));
if (seal.RootElement.GetProperty("sampleHash").GetString() != Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))) ||
    seal.RootElement.GetProperty("referenceHash").GetString() != Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "reference-labels.json")))))
    throw new InvalidDataException("Freeze the complete reference before generating detector predictions.");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var catalog = JobConceptCatalog.LoadDefault();
var labelTaxonomy = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(directory, "taxonomy.json")).Replace("\r\n", "\n", StringComparison.Ordinal));
var fullTaxonomyPath = Path.Combine(directory, "full-taxonomy.json");
var frozenTaxonomy = File.Exists(fullTaxonomyPath)
    ? System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(fullTaxonomyPath).Replace("\r\n", "\n", StringComparison.Ordinal))
    : labelTaxonomy;
if (Convert.ToHexStringLower(SHA256.HashData(frozenTaxonomy)) != catalog.Fingerprint)
    throw new InvalidDataException("Current taxonomy differs from the frozen labeling definitions; create a new reference run.");
// A concept-only reference may select definitions, never rewrite them.
using var fullTree = JsonDocument.Parse(frozenTaxonomy);
using var labelTree = JsonDocument.Parse(labelTaxonomy);
var definitions = fullTree.RootElement.GetProperty("concepts").EnumerateArray()
    .ToDictionary(c => c.GetProperty("id").GetString()!, StringComparer.Ordinal);
var selectedIds = new HashSet<string>(StringComparer.Ordinal);
foreach (var concept in labelTree.RootElement.GetProperty("concepts").EnumerateArray())
{
    var id = concept.GetProperty("id").GetString()!;
    if (!selectedIds.Add(id) || !definitions.TryGetValue(id, out var original) || !JsonElement.DeepEquals(concept, original))
        throw new InvalidDataException("Labeling definitions differ from the unchanged production taxonomy.");
}
if (selectedIds.Count == 0) throw new InvalidDataException("No selected concepts.");
var snapshot = ConceptRuleSnapshot.Load(rulePath, catalog);
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
    taxonomyHash = Convert.ToHexStringLower(SHA256.HashData(labelTaxonomy)), rules = snapshot.Rules, postings = rows }, json);
