using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LeidosJobsViewer;

public sealed class CredentialDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private readonly CredentialCatalogDocument _catalog;
    private readonly CompiledCredential[] _credentials;

    public CredentialDetector(ILogger<CredentialDetector> logger)
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "CredentialCatalog.json");
        try
        {
            var json = File.ReadAllText(catalogPath);
            _catalog = JsonSerializer.Deserialize<CredentialCatalogDocument>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Credential catalog JSON was empty.");
            ValidateCatalog(_catalog);
            _credentials = _catalog.Credentials.Select(Compile).ToArray();
            logger.LogInformation(
                "Loaded {CredentialCount} credential definitions from {CatalogPath}.",
                _credentials.Length,
                catalogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The credential catalog could not be loaded from '{catalogPath}'.",
                ex);
        }
    }

    public int CatalogVersion => _catalog.SchemaVersion;

    internal CredentialAnalysis Analyze(string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return new CredentialAnalysis([], [], CatalogVersion);
        }

        var segments = CreateSegments(descriptionHtml);
        var found = new List<FoundCredential>();
        var unrecognized = new List<string>();
        var sectionRequirement = "mentioned";

        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            sectionRequirement = UpdateSectionRequirement(segment, sectionRequirement);
            var segmentMatches = new List<(CompiledCredential Credential, Match Match)>();

            foreach (var credential in _credentials)
            {
                var match = credential.Patterns
                    .Select(pattern => pattern.Match(segment))
                    .Where(match => match.Success)
                    .OrderBy(match => match.Index)
                    .FirstOrDefault();
                if (match is not null)
                {
                    segmentMatches.Add((credential, match));
                }
            }

            var hasCredentialAlternative = segmentMatches.Count > 1 && AlternativeConnectorRegex.IsMatch(segment);
            foreach (var (credential, match) in segmentMatches)
            {
                var postHireAllowed = PostHireAcquisitionRegex.IsMatch(segment);
                var inProgressAccepted = InProgressAcceptedRegex.IsMatch(segment);
                var equivalentAccepted = EquivalentAcceptedRegex.IsMatch(segment);
                var requirement = ClassifyRequirement(segment, sectionRequirement, postHireAllowed);
                var isAlternative = hasCredentialAlternative ||
                    (inProgressAccepted && InProgressAlternativeRegex.IsMatch(segment));

                found.Add(new FoundCredential(
                    credential,
                    requirement,
                    isAlternative,
                    equivalentAccepted,
                    inProgressAccepted,
                    postHireAllowed,
                    CreateEvidence(segment, match.Index),
                    segmentIndex,
                    match.Index));
            }

            if (segmentMatches.Count == 0 &&
                GeneralCredentialLanguageRegex.IsMatch(segment) &&
                !IgnoredGeneralLanguageRegex.IsMatch(segment))
            {
                unrecognized.Add(CreateEvidence(
                    segment,
                    GeneralCredentialLanguageRegex.Match(segment).Index));
            }
        }

        var normalized = found
            .GroupBy(item => item.Credential.Definition.Id, StringComparer.Ordinal)
            .Select(group => Merge(group))
            .OrderBy(match => RequirementPriority(match.Requirement))
            .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CredentialAnalysis(
            normalized,
            unrecognized.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            CatalogVersion);
    }

    internal JobRecord AnalyzeJob(JobRecord job)
    {
        var analysis = Analyze(job.DescriptionHtml);
        return job with
        {
            Credentials = analysis.Credentials,
            UnrecognizedCredentialMentions = analysis.UnrecognizedMentions,
            CredentialCatalogVersion = analysis.CatalogVersion
        };
    }

    private static CredentialMatch Merge(IEnumerable<FoundCredential> group)
    {
        var candidates = group.ToArray();
        var strongest = candidates
            .OrderBy(item => RequirementPriority(item.Requirement))
            .ThenBy(item => item.SegmentIndex)
            .ThenBy(item => item.MatchIndex)
            .First();
        var definition = strongest.Credential.Definition;
        return new CredentialMatch(
            definition.Id,
            definition.Name,
            definition.FullName,
            definition.Issuer,
            definition.Type,
            definition.Category,
            strongest.Requirement,
            candidates.Any(item => item.IsAlternative),
            candidates.Any(item => item.EquivalentAccepted),
            candidates.Any(item => item.InProgressAccepted),
            candidates.Any(item => item.PostHireAcquisitionAllowed),
            strongest.Evidence);
    }

    private static string ClassifyRequirement(
        string segment,
        string sectionRequirement,
        bool postHireAllowed)
    {
        if (postHireAllowed || RequiredCueRegex.IsMatch(segment))
        {
            return "required";
        }
        if (PreferredCueRegex.IsMatch(segment))
        {
            return "preferred";
        }
        if (DesiredCueRegex.IsMatch(segment))
        {
            return "desired";
        }
        return sectionRequirement;
    }

    private static string UpdateSectionRequirement(string segment, string current)
    {
        if (RequiredSectionRegex.IsMatch(segment))
        {
            return "required";
        }
        if (PreferredSectionRegex.IsMatch(segment))
        {
            return "preferred";
        }
        if (DesiredSectionRegex.IsMatch(segment))
        {
            return "desired";
        }
        if (SectionResetRegex.IsMatch(segment))
        {
            return "mentioned";
        }
        return current;
    }

    private static IReadOnlyList<string> CreateSegments(string html)
    {
        var withBreaks = BlockTagRegex.Replace(html, "\n");
        var withoutTags = AnyTagRegex.Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => WhitespaceRegex.Replace(segment, " ").Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();
    }

    private static string CreateEvidence(string segment, int matchIndex)
    {
        const int maximumLength = 340;
        if (segment.Length <= maximumLength)
        {
            return segment;
        }

        var start = Math.Max(0, matchIndex - 90);
        if (start > 0)
        {
            var space = segment.IndexOf(' ', start);
            start = space >= 0 ? space + 1 : start;
        }
        var length = Math.Min(maximumLength, segment.Length - start);
        var value = segment.Substring(start, length);
        if (start + length < segment.Length)
        {
            var lastSpace = value.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                value = value[..lastSpace];
            }
        }
        return (start > 0 ? "…" : "") + value +
            (start + length < segment.Length ? "…" : "");
    }

    private static int RequirementPriority(string requirement) => requirement switch
    {
        "required" => 0,
        "preferred" => 1,
        "desired" => 2,
        _ => 3
    };

    private static CompiledCredential Compile(CredentialDefinition definition)
    {
        var patterns = definition.Aliases.Select(alias =>
        {
            var expression = string.IsNullOrWhiteSpace(alias.Pattern)
                ? $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias.Text)}(?![\p{{L}}\p{{N}}])"
                : alias.Pattern;
            return new Regex(
                expression,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }).ToArray();
        return new CompiledCredential(definition, patterns);
    }

    private static void ValidateCatalog(CredentialCatalogDocument catalog)
    {
        if (catalog.SchemaVersion < 1 || catalog.Credentials.Count == 0)
        {
            throw new InvalidDataException("Credential catalog must have a positive version and at least one entry.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var credential in catalog.Credentials)
        {
            if (string.IsNullOrWhiteSpace(credential.Id) ||
                string.IsNullOrWhiteSpace(credential.Name) ||
                string.IsNullOrWhiteSpace(credential.FullName) ||
                string.IsNullOrWhiteSpace(credential.Issuer) ||
                string.IsNullOrWhiteSpace(credential.Type) ||
                string.IsNullOrWhiteSpace(credential.Category) ||
                credential.Aliases.Count == 0 ||
                credential.Aliases.Any(alias => string.IsNullOrWhiteSpace(alias.Text)) ||
                !ids.Add(credential.Id))
            {
                throw new InvalidDataException($"Invalid or duplicate credential catalog entry '{credential.Id}'.");
            }
        }
    }

    private sealed record CompiledCredential(
        CredentialDefinition Definition,
        IReadOnlyList<Regex> Patterns);

    private sealed record FoundCredential(
        CompiledCredential Credential,
        string Requirement,
        bool IsAlternative,
        bool EquivalentAccepted,
        bool InProgressAccepted,
        bool PostHireAcquisitionAllowed,
        string Evidence,
        int SegmentIndex,
        int MatchIndex);

    private static readonly Regex BlockTagRegex = CreateRegex(
        @"</?(?:p|li|ul|ol|div|h[1-6]|br|section|article|table|tr|td|th)[^>]*>");
    private static readonly Regex AnyTagRegex = CreateRegex(@"<[^>]+>");
    private static readonly Regex WhitespaceRegex = CreateRegex(@"\s+");
    private static readonly Regex AlternativeConnectorRegex = CreateRegex(@"\b(?:or|either)\b");
    private static readonly Regex InProgressAlternativeRegex = CreateRegex(
        @"\bor\s+(?:actively\s+)?progress(?:ing)?\s+(?:toward|towards)\b");
    private static readonly Regex EquivalentAcceptedRegex = CreateRegex(
        @"\bor\s+(?:an?\s+)?equivalent(?:\s+(?:certification|credential|qualification))?\b");
    private static readonly Regex InProgressAcceptedRegex = CreateRegex(
        @"\b(?:actively\s+)?progress(?:ing)?\s+(?:toward|towards)\b|" +
        @"\bprogress\s+toward\s+licensure\b|\bnear\s+completion\b|" +
        @"\baspiration\s+to\s+obtain\b|\bability\s+to\s+(?:obtain|achieve)\b|" +
        @"\bable\s+to\s+(?:obtain|achieve)\b");
    private static readonly Regex PostHireAcquisitionRegex = CreateRegex(
        @"\b(?:required|obtain(?:ed)?|achieve(?:d)?)\s+within\s+\d+\s+(?:calendar\s+)?(?:days?|weeks?|months?)\b|" +
        @"\bwithin\s+\d+\s+(?:calendar\s+)?(?:days?|weeks?|months?)\s+(?:of|after)\s+(?:hire|start|employment)\b");
    private static readonly Regex RequiredCueRegex = CreateRegex(
        @"\b(?:required|mandatory)\b|\bmust\s+(?:possess|hold|have|maintain|obtain)\b|" +
        @"\bshall\s+(?:possess|hold|have|maintain|obtain)\b|" +
        @"\bcurrent\b.{0,80}\bcertification\b");
    private static readonly Regex PreferredCueRegex = CreateRegex(
        @"\bpreferred\b|\bis\s+a\s+plus\b|\bbonus\s+points\b|\bfavorable\s+if\b|\byou\s+might\s+also\s+have\b");
    private static readonly Regex DesiredCueRegex = CreateRegex(@"\bdesir(?:ed|able)\b");
    private static readonly Regex RequiredSectionRegex = CreateRegex(
        @"^(?:required|basic|minimum)\s+(?:experience|qualifications|skills|education)\b|" +
        @"^what\s+(?:does\s+)?leidos\s+need\s+from\s+me\b|" +
        @"^about\s+the\s+must\s+haves\b|^must\s+have\b.{0,80}\brequired\s+certifications?\b");
    private static readonly Regex PreferredSectionRegex = CreateRegex(
        @"^(?:preferred|favorable)\s+(?:experience|qualifications|skills|education|if\s+you\s+have)\b|" +
        @"^you\s+might\s+also\s+have\b|^bonus\s+points\b");
    private static readonly Regex DesiredSectionRegex = CreateRegex(
        @"^desired\s+(?:experience|qualifications|skills|education)\b");
    private static readonly Regex SectionResetRegex = CreateRegex(
        @"^(?:responsibilities|primary\s+duties|what\s+you['’]ll\s+be\s+doing|original\s+posting|pay\s+range)\s*:?");
    private static readonly Regex GeneralCredentialLanguageRegex = CreateRegex(
        @"\b(?:certification|certifications|certified|credential|credentials|professional\s+licen[cs]e|professional\s+licensure|licen[cs]ed\s+[A-Za-z]+)\b");
    private static readonly Regex IgnoredGeneralLanguageRegex = CreateRegex(
        @"\bassistance\s+with\s+obtaining\s+pertinent\s+certifications\b|" +
        @"\bcertification\s+(?:activities|packages|authority)\b|" +
        @"\bfacility\s+credentials?\s*/\s*authorization\b|" +
        @"\bcertificate\s+management\b|\blicen[cs]ed\s+software\b|" +
        @"\bidentity,?\s+credential,?\s+and\s+access\s+management\b|" +
        @"\bOEM\s+certified\s+technician\b|" +
        @"^must\s+have\s+required\s+certifications?\s+to\s+be\s+considered\s*:?$|" +
        @"^possession\s+of\s+one\s+or\s+more\s+of\s+the\s+following\s+industry-recognized\s+certifications?\s*:?$");

    private static Regex CreateRegex(string pattern) => new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
}

internal sealed class CredentialCatalogDocument
{
    public int SchemaVersion { get; init; }
    public List<CredentialDefinition> Credentials { get; init; } = [];
}

internal sealed class CredentialDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Type { get; init; } = "";
    public string Category { get; init; } = "";
    public List<CredentialAliasDefinition> Aliases { get; init; } = [];
}

internal sealed class CredentialAliasDefinition
{
    public string Text { get; init; } = "";
    public string? Pattern { get; init; }
}
