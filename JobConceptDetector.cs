using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed class JobConceptDetector
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private static readonly Regex WhitespacePattern = new(@"\s+", Options, Timeout);
    private static readonly Regex LocalNegationPattern = new(
        @"(?:\b(?:do|does|did|is|are|was|were|will|would|should|can|cannot)\s+not|\bnot\s+(?:directly\s+)?(?:responsible\s+for\s+)?|\bno\s+(?:direct\s+)?)$",
        Options,
        Timeout);
    private readonly JobConceptCatalog _catalog;
    private readonly Dictionary<string, Regex[]> _patterns;
    private readonly Dictionary<string, Regex[]> _titlePatterns;
    private readonly Dictionary<string, Regex[]> _titleExclusionPatterns;
    private readonly Dictionary<string, Regex[][]> _contextRules;

    public JobConceptDetector(JobConceptCatalog catalog)
    {
        _catalog = catalog;
        _patterns = catalog.Concepts.ToDictionary(
            concept => concept.Id,
            concept => (concept.EvidencePatterns ?? [])
                .Select(pattern => new Regex(pattern, Options, Timeout))
                .ToArray(),
            StringComparer.Ordinal);
        _titlePatterns = catalog.Concepts.ToDictionary(
            concept => concept.Id,
            concept => (concept.TitleEvidencePatterns ?? [])
                .Select(pattern => new Regex(pattern, Options, Timeout))
                .ToArray(),
            StringComparer.Ordinal);
        _titleExclusionPatterns = catalog.Concepts.ToDictionary(
            concept => concept.Id,
            concept => (concept.TitleExclusionPatterns ?? [])
                .Select(pattern => new Regex(pattern, Options, Timeout))
                .ToArray(),
            StringComparer.Ordinal);
        _contextRules = catalog.Concepts.ToDictionary(
            concept => concept.Id,
            concept => (concept.ContextRules ?? [])
                .Select(rule => rule.RequiredPatterns
                    .Select(pattern => new Regex(pattern, Options, Timeout))
                    .ToArray())
                .ToArray(),
            StringComparer.Ordinal);
    }

    public int CatalogVersion => _catalog.Version;

    public IReadOnlyList<DetectedJobConcept> Analyze(
        string title,
        string primaryLocation,
        IReadOnlyList<string> additionalLocations,
        string descriptionHtml,
        RemoteWorkAnalysis? remoteWork,
        ExtendedLocationRequirementAnalysis? extendedLocation)
    {
        var description = string.IsNullOrWhiteSpace(descriptionHtml)
            ? ""
            : JobAnalysis.HtmlToPlainText(descriptionHtml);
        var corpusText = string.Join('\n', new[] { title }.Concat([description]));
        var detected = new Dictionary<string, DetectedJobConcept>(StringComparer.Ordinal);

        foreach (var concept in _catalog.Concepts)
        {
            if (_titleExclusionPatterns[concept.Id].Any(pattern => pattern.IsMatch(title ?? "")))
            {
                continue;
            }

            foreach (var pattern in _titlePatterns[concept.Id])
            {
                var match = pattern.Match(title ?? "");
                if (!match.Success)
                {
                    continue;
                }

                Add(detected, concept.Id, NormalizeEvidence(match.Value));
                break;
            }

            foreach (var pattern in _patterns[concept.Id])
            {
                var match = pattern.Matches(corpusText)
                    .Cast<Match>()
                    .FirstOrDefault(candidate => !IsLocallyNegated(corpusText, candidate));
                if (match is null)
                {
                    continue;
                }

                Add(detected, concept.Id, NormalizeEvidence(match.Value));
                break;
            }

            foreach (var rule in _contextRules[concept.Id])
            {
                var matches = rule.Select(pattern => pattern.Match(corpusText)).ToArray();
                if (matches.All(match => match.Success))
                {
                    Add(detected, concept.Id, string.Join("; ", matches
                        .Select(match => NormalizeEvidence(match.Value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)));
                    break;
                }
            }

            if (concept.RemoteDesignation && remoteWork?.IsRemoteDesignated == true)
            {
                var metadata = new[] { title, primaryLocation }
                    .Concat(additionalLocations ?? [])
                    .FirstOrDefault(value =>
                        value?.Contains("remote", StringComparison.OrdinalIgnoreCase) == true ||
                        value?.Contains("telework", StringComparison.OrdinalIgnoreCase) == true);
                Add(detected, concept.Id, string.IsNullOrWhiteSpace(metadata)
                    ? "Remote designation detected in the posting"
                    : NormalizeEvidence(metadata));
            }

            var remoteCategories = new HashSet<string>(
                concept.RemoteSignalCategories ?? [], StringComparer.Ordinal);
            foreach (var signal in remoteWork?.Signals ?? [])
            {
                if (remoteCategories.Contains(signal.Category))
                {
                    Add(detected, concept.Id, signal.Evidence);
                }
            }

            var extendedCategories = new HashSet<string>(
                concept.ExtendedLocationCategories ?? [], StringComparer.Ordinal);
            foreach (var signal in extendedLocation?.Signals ?? [])
            {
                if (extendedCategories.Contains(signal.Category))
                {
                    Add(detected, concept.Id, signal.Evidence);
                }
            }
        }

        return detected.Values
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static JobConceptDetector CreateDefault() =>
        new(JobConceptCatalog.LoadDefault());

    private static void Add(
        IDictionary<string, DetectedJobConcept> detected,
        string conceptId,
        string evidence)
    {
        if (!detected.ContainsKey(conceptId) && !string.IsNullOrWhiteSpace(evidence))
        {
            detected[conceptId] = new DetectedJobConcept(conceptId, NormalizeEvidence(evidence));
        }
    }

    private static string NormalizeEvidence(string value)
    {
        var normalized = WhitespacePattern.Replace(value, " ").Trim(' ', '.', ';', '\u2022');
        return normalized.Length <= 300 ? normalized : normalized[..297] + "...";
    }

    private static bool IsLocallyNegated(string corpusText, Match match)
    {
        var prefixStart = Math.Max(0, match.Index - 60);
        var prefix = corpusText[prefixStart..match.Index].TrimEnd();
        return LocalNegationPattern.IsMatch(prefix);
    }
}
