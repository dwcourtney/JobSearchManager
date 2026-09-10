using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

// Supplemental observation parsing never supplies the legacy reducer's decisions.
// Clause/path nodes retain the syntax around atomic observations, including ambiguity.
internal sealed class FactualDomainObservations
{
    internal sealed record PatternRule(string Id, string Domain, string Type, string Pattern, string? Code, string? Unit, string Kind, string? ContextPattern = null);
    internal sealed record Definition(int SchemaVersion, string Version, int TimeoutMilliseconds,
        string ClauseBoundary, string AlternativeBoundary, string Required, string Preferred, string Optional,
        string Negated, string Temporal, string Section, Dictionary<string,int> Numbers, PatternRule[] Patterns);
    private static readonly Lazy<FactualDomainObservations> Instance = new(() => new(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "rules", "factual-observation-completion-v1.json"))));
    internal static FactualDomainObservations Default => Instance.Value;
    private readonly Definition definition;
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Regex> contexts = new(StringComparer.Ordinal);
    private readonly List<PatternRule> ruleDefinitions = [];
    private readonly Dictionary<string,string> sourceHashes = new(StringComparer.Ordinal);
    internal string Fingerprint { get; }
    private readonly Regex clauseBoundary, alternativeBoundary, required, preferred, optional, negated, temporal, section;
    private readonly Regex block, tag, whitespace, listSeparator;
    private readonly (string Id, Regex Regex)[] credentials;
    private readonly string credentialFingerprint;

    internal FactualDomainObservations(string json)
    {
        definition = JsonSerializer.Deserialize<Definition>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("Observation completion rules missing.");
        if (definition.SchemaVersion != 1 || definition.Version != "1.0.0" || definition.TimeoutMilliseconds is < 1 or > 250)
            throw new InvalidDataException("Observation completion schema/version/timeout invalid.");
        if (definition.Patterns.Length is < 30 or > 256 || Domains.Any(domain => !definition.Patterns.Any(p => p.Domain == domain)))
            throw new InvalidDataException("Incomplete observation pattern set.");
        Fingerprint = FactObservations.Hash(json);
        Regex Compile(string pattern) => pattern.Length is > 0 and <= 8192
            ? new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(definition.TimeoutMilliseconds))
            : throw new InvalidDataException("Invalid observation pattern length.");
        foreach (var p in definition.Patterns)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 100 || string.IsNullOrWhiteSpace(p.Type) ||
                !new[] { "categorical", "range", "upper-bound", "raw-field-list", "unresolved-name", "surface-destination", "surface-jurisdiction-list", "surface-zone-list" }.Contains(p.Kind) || !Domains.Contains(p.Domain) || !patterns.TryAdd(p.Id, Compile(p.Pattern)))
                throw new InvalidDataException("Invalid/duplicate observation pattern.");
            if (p.ContextPattern is not null) contexts.Add(p.Id, Compile(p.ContextPattern));
        }
        ruleDefinitions.AddRange(definition.Patterns);
        void Import(string domain, string id, string pattern, string code, string hash)
        {
            var key = "legacy:" + domain + ":" + id;
            patterns.Add(key, Compile(pattern)); sourceHashes.Add(key, hash);
            ruleDefinitions.Add(new(key, domain, "legacy-pattern-candidate", pattern, code, null, "unfiltered-native-pattern"));
        }
        var clearance = ClearanceRules.Default;
        foreach (var r in clearance.Definition.LevelRules)
            Import("clearance", r.Id, clearance.Definition.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Result, clearance.Fingerprint);
        foreach (var r in clearance.Definition.RequirementRules)
            Import("clearance", r.Id, clearance.Definition.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Result, clearance.Fingerprint);
        foreach (var r in clearance.Definition.LevelOverrides)
            Import("clearance", r.Id, clearance.Definition.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Result, clearance.Fingerprint);
        foreach (var id in new[] { clearance.Definition.NegationPatternId, clearance.Definition.PolygraphPatternId })
            Import("clearance", id, clearance.Definition.Patterns.Single(p => p.Id == id).Pattern, id, clearance.Fingerprint);
        var education = EducationRules.Default;
        foreach (var r in education.Rules.DegreeRules)
            Import("education", r.Id, education.Rules.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Level, education.Fingerprint);
        Import("education", "abbreviations", education.Rules.Patterns.Single(p => p.Id == education.Rules.Abbreviations.PatternId).Pattern, "degree-abbreviation", education.Fingerprint);
        var remote = RemoteWorkRules.Default;
        foreach (var r in remote.Rules.SignalRules)
            Import("remote-work", r.Id, remote.Rules.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Category, remote.Fingerprint);
        var extended = ExtendedLocationRules.Default;
        foreach (var r in extended.Rules.SignalRules)
            Import("extended-location", r.Id, extended.Rules.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Category, extended.Fingerprint);
        var geography = GeographicRestrictionRules.Default;
        foreach (var r in geography.OrderedRules)
            Import("geographic-restriction", r.Id, geography.Rules.Patterns.Single(p => p.Id == r.PatternId).Pattern, r.Category, geography.Fingerprint);
        clauseBoundary = Compile(definition.ClauseBoundary); alternativeBoundary = Compile(definition.AlternativeBoundary);
        required = Compile(definition.Required); preferred = Compile(definition.Preferred); optional = Compile(definition.Optional);
        negated = Compile(definition.Negated); temporal = Compile(definition.Temporal); section = Compile(definition.Section);
        block = Compile(@"</?(?:p|li|ul|ol|div|h[1-6]|br|section|article|table|tr|td|th)[^>]*>");
        listSeparator = Compile(@"\s*(?:/|\bor\b)\s*");
        tag = Compile(@"<[^>]+>"); whitespace = Compile(@"\s+");
        var catalogJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CredentialCatalog.json"));
        credentialFingerprint = FactObservations.Hash(catalogJson);
        var catalog = JsonSerializer.Deserialize<CredentialCatalogDocument>(catalogJson, FactObservations.Json)!;
        credentials = catalog.Credentials.SelectMany(c => c.Aliases.Select(a => (c.Id, Compile(
            string.IsNullOrWhiteSpace(a.Pattern) ? $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(a.Text)}(?![\p{{L}}\p{{N}}])" : a.Pattern)))).ToArray();
    }

    internal static readonly string[] Domains = ["clearance", "education", "credentials", "remote-work", "extended-location", "geographic-restriction"];
    internal IReadOnlyDictionary<string, FactObservationDocument> Observe(string html, string? provider = null,
        IReadOnlyDictionary<string,string>? metadata = null)
    {
        var lines = new List<(string Text, bool Heading)>();
        var cursor = 0;
        var inHeading = false;
        void AddLine(string fragment)
        {
            var normalized = whitespace.Replace(WebUtility.HtmlDecode(tag.Replace(fragment, " ")), " ").Trim();
            if (normalized.Length > 0) lines.Add((normalized, inHeading));
        }
        foreach (Match boundary in block.Matches(html))
        {
            AddLine(html[cursor..boundary.Index]);
            if (boundary.Value.StartsWith("<h", StringComparison.OrdinalIgnoreCase)) inHeading = true;
            if (boundary.Value.StartsWith("</h", StringComparison.OrdinalIgnoreCase)) inHeading = false;
            cursor = boundary.Index + boundary.Length;
        }
        AddLine(html[cursor..]);
        var segments = lines.Select(line => line.Text).ToArray();
        var results = Domains.ToDictionary(d => d, _ => new List<FactualObservation>(), StringComparer.Ordinal);
        string? heading = null;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (lines[index].Heading || section.IsMatch(segment) || segment.Length <= 120 && segment.EndsWith(":")) heading = segment;
            foreach (var (clause, offset) in Split(segment, clauseBoundary))
            {
                foreach (var domain in Domains)
                {
                    var domainRules = ruleDefinitions.Where(r => r.Domain == domain).ToArray();
                    if (!domainRules.Any(r => patterns[r.Id].IsMatch(clause) && (!contexts.TryGetValue(r.Id, out var context) || context.IsMatch((heading ?? "") + "\n" + clause))) &&
                        !(domain == "credentials" && credentials.Any(c => c.Regex.IsMatch(clause)))) continue;
                    if (domain == "credentials" && !credentials.Any(c => c.Regex.IsMatch(clause)) &&
                        !domainRules.Where(r => r.Type is "unresolved-credential" or "equivalence").Any(r => patterns[r.Id].IsMatch(clause))) continue;
                    var branches = Split(clause, alternativeBoundary).ToArray();
                    if (domain == "geographic-restriction" || domain == "education" && branches.Skip(1).Any(branch =>
                        !domainRules.Where(r => r.Type is "degree" or "experience").Any(r => patterns[r.Id].IsMatch(branch.Text))))
                        branches = [(clause, 0)];
                    // The container describes retained wording, not an asserted resolved truth.
                    var root = Create(domain, "statement", new("unresolved-syntax", clause), "unknown", "unresolved-syntax",
                        clause, offset, clause.Length, "statement", branches.Length > 1 ? "or" : "unspecified");
                    var branchNodes = new List<FactualObservation>();
                    foreach (var (branch, branchOffset) in branches)
                    {
                        var node = Create(domain, "path", new("unresolved-path", branch), "unknown", "source-path",
                            branch, offset + branchOffset, branch.Length, "path", branch.Contains("+") || branch.Contains(" and ", StringComparison.OrdinalIgnoreCase) ? "and" : "unspecified");
                        node = node with { Relations = [new("component-of", root.Id)] };
                        var children = new List<FactualObservation>();
                        var anchors = domainRules.Where(r => r.Type is "level" or "degree" or "arrangement")
                            .SelectMany(r => patterns[r.Id].Matches(branch).Cast<Match>())
                            .Concat(domain == "credentials" ? credentials.SelectMany(c => c.Regex.Matches(branch).Cast<Match>()) : [])
                            .OrderBy(m => m.Index).ToArray();
                        string Applicability(Match match)
                        {
                            foreach (Match negative in negated.Matches(branch))
                                if (negative.Index <= match.Index && match.Index < negative.Index + negative.Length) return branch;
                            var next = anchors.Where(a => a.Index > match.Index).Select(a => a.Index).DefaultIfEmpty(branch.Length).Min();
                            var local = branch[match.Index..next];
                            // A qualifier belongs to its own assertion window. Other assertions' cues cannot overwrite it.
                            if (required.IsMatch(local) || preferred.IsMatch(local) || negated.IsMatch(local) || optional.IsMatch(local)) return local;
                            return anchors.Length <= 1 ? branch : local;
                        }
                        string AssertionObligation(string context)
                        {
                            var local = Obligation(context);
                            if (local != "unknown" || branches.Length <= 1) return local;
                            var kinds = new[] { required.IsMatch(clause) ? "required" : null, preferred.IsMatch(clause) ? "preferred" : null }
                                .Where(k => k is not null).ToArray();
                            return kinds.Length == 1 && !negated.IsMatch(clause) ? kinds[0]! : "unknown";
                        }
                        foreach (var rule in domainRules)
                        foreach (Match match in patterns[rule.Id].Matches(branch))
                        {
                            if (contexts.TryGetValue(rule.Id, out var context) && !context.IsMatch((heading ?? "") + "\n" + clause)) continue;
                            var raw = match.Value;
                            var surface = match.Groups["value"].Success ? match.Groups["value"].Value : raw;
                            var lower = Number(match.Groups["lower"].Value);
                            var upper = Number(match.Groups["upper"].Value) ?? lower;
                            var isUpperBound = rule.Kind == "upper-bound" || match.Groups["bound"].Success;
                            var value = new FactValue(isUpperBound ? "upper-bound" : rule.Kind, raw, rule.Code ?? surface, isUpperBound ? null : lower, upper,
                                Unit: rule.Unit ?? (match.Groups["unit"].Success ? match.Groups["unit"].Value : null),
                                Alternatives: rule.Kind.EndsWith("list", StringComparison.Ordinal) && listSeparator.IsMatch(surface) ? listSeparator.Split(surface) : null);
                            var child = Create(domain, rule.Type, value, rule.Type == "legacy-pattern-candidate" ? "unknown" : AssertionObligation(Applicability(match)), rule.Type == "legacy-pattern-candidate" ? "unfiltered-legacy-pattern" : match.Groups["qualifier"].Success ? match.Groups["qualifier"].Value : optional.Match(Applicability(match)) is { Success: true } modality ? modality.Value.ToLowerInvariant() : "recognized-surface",
                                Applicability(match), offset + branchOffset + match.Index, match.Length, rule.Id, "unspecified", sourceHashes.GetValueOrDefault(rule.Id));
                            children.Add(child with { Relations = [new("component-of", node.Id)] });
                        }
                        if (domain == "credentials")
                        foreach (var credential in credentials)
                        foreach (Match match in credential.Regex.Matches(branch))
                        {
                            var child = Create(domain, "credential", new("catalog-credential", match.Value, credential.Id),
                                AssertionObligation(Applicability(match)), "catalog-mention", Applicability(match), offset + branchOffset + match.Index,
                                match.Length, "catalog:" + credential.Id, "unspecified", credentialFingerprint);
                            children.Add(child with { Relations = [new("component-of", node.Id)] });
                        }
                        foreach (Match timing in temporal.Matches(branch))
                        {
                            var child = Create(domain, "temporal-condition", new("context", timing.Value, timing.Value), "unknown", "explicit-timing",
                                branch, offset + branchOffset + timing.Index, timing.Length, "temporal-condition", "unspecified");
                            children.Add(child with { Relations = [new("component-of", node.Id)] });
                        }
                        // Repeat aliases at the same location are source-identical; separate clauses never deduplicate.
                        children = children.DistinctBy(c => c.Id).ToList();
                        results[domain].AddRange(children);
                        branchNodes.Add(node with { Relations = node.Relations.Concat(children.Select(c => new FactRelation("has-component", c.Id))).ToArray() });
                    }
                    foreach (var node in branchNodes)
                        results[domain].Add(node with { Relations = node.Relations.Concat(branchNodes.Where(n => branches.Length > 1 && n.Id != node.Id)
                            .Select(n => new FactRelation("alternative-to", n.Id))).ToArray() });
                    results[domain].Add(root with { Relations = branchNodes.Select(n => new FactRelation("has-path", n.Id)).ToArray() });
                }

                FactualObservation Create(string domain, string type, FactValue value, string obligation, string qualifier,
                    string applicability, int start, int length, string rule, string connective, string? hash = null) =>
                    FactObservations.Create(domain, type, value, obligation, qualifier,
                        new FactScope(heading, type is "destination" or "included-jurisdiction" or "excluded-jurisdiction" or "scoped-duration" ? value.Code : null, temporal.Match(applicability) is { Success: true } t ? t.Value : null,
                            FactualObservationRules.Default.Capture("job-level", (heading ?? "") + "\n" + applicability),
                            FactualObservationRules.Default.Capture("employment-type", (heading ?? "") + "\n" + applicability), applicability), new FactEvidence(segment, "completion-normalized-line-v1", index, start, length),
                        "factual-observation-completion-v1", hash ?? Fingerprint, rule, provider: provider, connective: connective,
                        exclusions: type == "excluded-jurisdiction" ? [value.Code ?? value.Raw] : [],
                        polarity: type == "excluded-jurisdiction" ? "excluded" : negated.IsMatch(applicability) ? "negated-requirement" : "affirmed");
            }
        }
        foreach (var (field, value) in metadata ?? new Dictionary<string,string>())
        {
            if (!new[] { "title", "primaryLocation", "additionalLocation", "workLocation", "workLocationType", "remote" }.Contains(field, StringComparer.OrdinalIgnoreCase) && !field.StartsWith("additionalLocation[", StringComparison.OrdinalIgnoreCase)) continue;
            // Keep literal provider declarations separate; do not choose body or metadata as authoritative here.
            foreach (var domain in new[] { "remote-work", "extended-location", "geographic-restriction" })
            {
            var o = FactObservations.Create(domain, "provider-declaration", new("declared", value, value),
                "unknown", "provider-declared-review-source-and-scope", new(null, null, null, null, null, field),
                new(value, "provider-field", 0, 0, value.Length), "input-v1", FactObservations.Hash(field + "\n" + value),
                field, "provider-metadata", provider);
            results[domain].Add(o);
            }
        }
        var declarations = results["remote-work"].Where(o => o.Source == "provider-metadata").ToArray();
        var bodyArrangements = results["remote-work"].Where(o => o.Type == "arrangement").ToArray();
        for (var i = 0; i < results["remote-work"].Count; i++)
        {
            var o = results["remote-work"][i];
            var otherSource = o.Source == "provider-metadata" ? bodyArrangements : o.Type == "arrangement" ? declarations : [];
            results["remote-work"][i] = o with { Relations = o.Relations.Concat(otherSource.Select(other =>
                new FactRelation("source-comparison-review-scope", other.Id))).ToArray() };
        }
        return results.ToDictionary(p => p.Key, p => new FactObservationDocument(1, FactObservations.Hash(html), html, segments, p.Value));

        string Obligation(string text) => negated.IsMatch(text) ? negated.Match(text).Value.Contains("required", StringComparison.OrdinalIgnoreCase) ? "not-required" : "unknown" : required.IsMatch(text) ? "required" :
            preferred.IsMatch(text) ? "preferred" : optional.IsMatch(text) ? optional.Match(text).Value.Equals("optional", StringComparison.OrdinalIgnoreCase) ? "optional" : "unknown" :
            heading is not null && preferred.IsMatch(heading) ? "preferred" : heading is not null && required.IsMatch(heading) ? "required" : "unknown";
    }
    private decimal? Number(string value) => decimal.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        ? n : definition.Numbers.TryGetValue(value.ToLowerInvariant(), out var mapped) ? mapped : null;
    private static IEnumerable<(string Text,int Offset)> Split(string text, Regex delimiter)
    {
        var start = 0;
        foreach (Match match in delimiter.Matches(text))
        {
            if (match.Index > start) yield return (text[start..match.Index], start);
            start = match.Index + match.Length;
        }
        if (start < text.Length) yield return (text[start..], start);
    }
}
