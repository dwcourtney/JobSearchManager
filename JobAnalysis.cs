using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal static partial class JobAnalysis
{
    private const string AmountRangePattern =
        @"\$\s*(?<minimum>\d[\d,]*(?:\.\d{1,2})?)\s*(?<minimumScale>[kK])?\s*(?:-|–|—|to)\s*" +
        @"\$?\s*(?<maximum>\d[\d,]*(?:\.\d{1,2})?)\s*(?<maximumScale>[kK])?";
    private const string SummaryAmountRangePattern =
        @"(?<minimumDollar>\$)?\s*(?<minimum>\d+(?:,\s*\d{3})*(?:\.\d{1,2})?)\s*(?<minimumScale>[kK])?\s*(?:-|–|—|to)\s*" +
        @"(?<maximumDollar>\$)?\s*(?<maximum>\d+(?:,\s*\d{3})*(?:\.\d{1,2})?)\s*(?<maximumScale>[kK])?";

    private static readonly (string Category, Regex Pattern)[] LocationRules =
    [
        ("distance-radius", DistanceRadiusRegex()),
        ("commuting-distance", CommutingDistanceRegex()),
        ("hybrid-local", HybridLocalRegex()),
        ("required-region", RequiredRegionRegex()),
        ("regional-preference", RegionalPreferenceRegex())
    ];

    public static SalaryAnalysis AnalyzeSalary(string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return new SalaryAnalysis(null, null, "unknown", "description-unavailable");
        }

        var text = HtmlToPlainText(descriptionHtml);

        // Two current Antarctic postings contain a role-specific anticipated salary
        // followed by a broader job-level pay band. Prefer the role-specific range.
        var specificMatch = SpecificSalaryRegex().Match(text);
        if (specificMatch.Success)
        {
            return CreateSalaryAnalysis(specificMatch, "specific-role-range");
        }

        var summaryRanges = AnalyzeSummaryPayRanges(descriptionHtml);
        if (summaryRanges.Length > 0)
        {
            return AggregateSummaryPayRanges(summaryRanges);
        }

        var usdMatch = UsdSalaryRangeRegex().Match(text);
        if (usdMatch.Success)
        {
            return CreateSalaryAnalysis(usdMatch, "usd-pay-range");
        }

        var standardMatch = StandardPayRangeRegex().Match(text);
        if (standardMatch.Success)
        {
            return CreateSalaryAnalysis(standardMatch, "standard-pay-range");
        }

        var compensationMatch = CompensationRangeRegex().Match(text);
        if (compensationMatch.Success)
        {
            return CreateSalaryAnalysis(compensationMatch, "compensation-range");
        }

        var separatedBoundsMatch = SeparatedSalaryBoundsRegex().Match(text);
        if (separatedBoundsMatch.Success)
        {
            return CreateSalaryAnalysis(separatedBoundsMatch, "separate-salary-bounds");
        }

        if (text.Contains("Pay Range", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("salary range", StringComparison.OrdinalIgnoreCase))
        {
            return new SalaryAnalysis(null, null, "unknown", "unparseable");
        }

        return new SalaryAnalysis(null, null, "unknown", "not-found");
    }

    private static bool IsDefensibleSummaryPayRange(
        Match match,
        SalaryAnalysis analysis) =>
        match.Groups["minimumDollar"].Success ||
        match.Groups["maximumDollar"].Success ||
        analysis.Period != "unknown" ||
        analysis.Maximum >= 10_000m;

    private static SalaryAnalysis[] AnalyzeSummaryPayRanges(string descriptionHtml)
    {
        var lines = HtmlToTextLines(descriptionHtml);
        var ranges = new List<SalaryAnalysis>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!SummaryPayHeadingRegex().IsMatch(lines[index]))
            {
                continue;
            }

            AddSummaryPayRange(SummaryPayRangeRegex().Match(lines[index]), ranges);
            for (var next = index + 1; next < lines.Length; next++)
            {
                if (SummaryPayHeadingRegex().IsMatch(lines[next]))
                {
                    break;
                }

                var match = SummarySectionRangeRegex().Match(lines[next]);
                if (!match.Success)
                {
                    break;
                }
                AddSummaryPayRange(match, ranges);
                index = next;
            }
        }
        return ranges.ToArray();
    }

    private static void AddSummaryPayRange(Match match, List<SalaryAnalysis> ranges)
    {
        if (!match.Success)
        {
            return;
        }
        var analysis = CreateSalaryAnalysis(match, "summary-pay-range");
        if (IsDefensibleSummaryPayRange(match, analysis))
        {
            ranges.Add(analysis);
        }
    }

    private static SalaryAnalysis AggregateSummaryPayRanges(
        IReadOnlyList<SalaryAnalysis> ranges)
    {
        if (ranges.Any(range => range.Minimum is null || range.Maximum is null))
        {
            return new SalaryAnalysis(null, null, "unknown", "unparseable");
        }

        var periods = ranges
            .Select(range => range.Period)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (periods.Length != 1)
        {
            // Combining hourly and annual figures would produce a misleading card.
            return new SalaryAnalysis(null, null, "unknown", "ambiguous-mixed-periods");
        }

        var minimum = ranges.Min(range => range.Minimum!.Value);
        var maximum = ranges.Max(range => range.Maximum!.Value);
        var parseStatus = ranges.Count == 1
            ? ranges[0].ParseStatus
            : periods[0] == "hourly"
                ? "hourly-unconverted-summary-aggregate"
                : periods[0] == "unknown"
                    ? "ambiguous-period-summary-aggregate"
                    : "summary-pay-range-aggregate";
        return new SalaryAnalysis(minimum, maximum, periods[0], parseStatus);
    }

    public static RemoteLocationAnalysis AnalyzeRemoteLocation(
        string descriptionHtml,
        string primaryLocation,
        IReadOnlyList<string> additionalLocations)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return new RemoteLocationAnalysis(false, null, null);
        }

        var text = HtmlToPlainText(descriptionHtml);
        var isRemoteListing = primaryLocation.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
            additionalLocations.Any(location =>
                location.Contains("Remote", StringComparison.OrdinalIgnoreCase)) ||
            text.Contains("remote", StringComparison.OrdinalIgnoreCase);

        if (!isRemoteListing)
        {
            return new RemoteLocationAnalysis(false, null, null);
        }

        var sentences = SentenceSplitRegex().Split(text);
        foreach (var (category, pattern) in LocationRules)
        {
            foreach (var sentence in sentences)
            {
                var match = pattern.Match(sentence);
                if (!match.Success)
                {
                    continue;
                }

                // "Commuting distance ... is a plus" is advantageous, not a restriction.
                if (category == "commuting-distance" &&
                    CommutingPlusRegex().IsMatch(sentence))
                {
                    continue;
                }

                return new RemoteLocationAnalysis(
                    true,
                    category,
                    CreateSnippet(sentence, match.Index));
            }
        }

        return new RemoteLocationAnalysis(false, null, null);
    }

    public static ClearanceAnalysis AnalyzeClearance(string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return new ClearanceAnalysis(
                "noneMentioned", "none", false, null, "description-unavailable");
        }

        var text = HtmlToPlainText(descriptionHtml);
        // Explicit negative statements are not clearance requirements. Remove only
        // the negative clause so a separate positive requirement can still win.
        text = NoClearanceRequiredRegex().Replace(text, " ");
        var polygraphRequired = RequiredPolygraphRegex().IsMatch(text);

        var (level, levelMatch) = FindClearanceLevel(text, excludePreferredSections: true);
        var onlyPreferredLevel = false;
        if (level == "noneMentioned")
        {
            (level, levelMatch) = FindClearanceLevel(text, excludePreferredSections: false);
            onlyPreferredLevel = level != "noneMentioned";
        }

        if (level == "publicTrust" &&
            AmbiguousPublicTrustAlternativeRegex().IsMatch(text) &&
            !SuitabilityInvestigationRegex().IsMatch(text))
        {
            level = "other";
            levelMatch = AmbiguousPublicTrustAlternativeRegex().Match(text);
        }

        if (level == "noneMentioned" && polygraphRequired)
        {
            level = "other";
            levelMatch = RequiredPolygraphRegex().Match(text);
        }
        else if (level == "noneMentioned")
        {
            return new ClearanceAnalysis(
                "noneMentioned", "none", false, null, "not-mentioned");
        }

        var clearanceContext = GetClearanceContext(text);
        var requirement = onlyPreferredLevel
            ? "preferred"
            : AnalyzeClearanceRequirement(clearanceContext, level);
        var evidenceMatch = FindBestClearanceEvidenceMatch(
            text,
            level,
            requirement,
            polygraphRequired,
            levelMatch);
        var evidence = CreateSnippet(text, evidenceMatch.Index);
        var parseStatus = level == "other" || requirement == "ambiguous"
            ? "ambiguous"
            : "parsed";

        return new ClearanceAnalysis(
            level,
            requirement,
            polygraphRequired,
            evidence,
            parseStatus);
    }

    internal static string HtmlToPlainText(string html)
    {
        var withSeparators = BlockTagRegex().Replace(html, " ");
        var withoutTags = AnyTagRegex().Replace(withSeparators, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static string[] HtmlToTextLines(string html)
    {
        var withSeparators = BlockTagRegex().Replace(html, "\n");
        var withoutTags = AnyTagRegex().Replace(withSeparators, " ");
        return WebUtility.HtmlDecode(withoutTags)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static SalaryAnalysis CreateSalaryAnalysis(Match match, string parsedStatus)
    {
        if (!decimal.TryParse(
                RemoveWhitespace(match.Groups["minimum"].Value),
                NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var minimum) ||
            !decimal.TryParse(
                RemoveWhitespace(match.Groups["maximum"].Value),
                NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var maximum))
        {
            return new SalaryAnalysis(null, null, "unknown", "unparseable");
        }
        if (match.Groups["minimumScale"].Success)
        {
            minimum *= 1_000m;
        }
        if (match.Groups["maximumScale"].Success)
        {
            maximum *= 1_000m;
        }

        var nearbyText = match.Value + " " + match.Groups["context"].Value;
        if (HourlyCueRegex().IsMatch(nearbyText))
        {
            // Do not compare hourly dollars directly with an annual threshold.
            return new SalaryAnalysis(minimum, maximum, "hourly", "hourly-unconverted");
        }

        if (AnnualCueRegex().IsMatch(nearbyText) || maximum >= 10_000m)
        {
            return new SalaryAnalysis(minimum, maximum, "annual", parsedStatus);
        }

        // A small range without an explicit period is deliberately not assumed annual.
        return new SalaryAnalysis(minimum, maximum, "unknown", "ambiguous-period");
    }

    private static string RemoveWhitespace(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static string CreateSnippet(string sentence, int matchIndex)
    {
        var normalized = WhitespaceRegex().Replace(sentence, " ").Trim();
        const int maximumLength = 340;
        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var start = Math.Max(0, matchIndex - 80);
        if (start > 0)
        {
            var nextSpace = normalized.IndexOf(' ', start);
            start = nextSpace >= 0 ? nextSpace + 1 : start;
        }

        var length = Math.Min(maximumLength, normalized.Length - start);
        var snippet = normalized.Substring(start, length);
        if (start + length < normalized.Length)
        {
            var lastSpace = snippet.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                snippet = snippet[..lastSpace];
            }
        }

        return (start > 0 ? "…" : "") + snippet +
            (start + length < normalized.Length ? "…" : "");
    }

    private static string GetClearanceContext(string text)
    {
        var sentences = SentenceSplitRegex().Split(text);
        var relevant = sentences
            .Where(sentence => ClearanceContextRegex().IsMatch(sentence))
            .Select(sentence => WhitespaceRegex().Replace(sentence, " ").Trim())
            .Where(sentence => sentence.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return relevant.Length > 0 ? string.Join(" ", relevant) : text;
    }

    private static (string Level, Match Match) FindClearanceLevel(
        string text,
        bool excludePreferredSections)
    {
        var candidates = new (string Level, Regex Pattern)[]
        {
            ("topSecretSCI", TopSecretSciRegex()),
            ("topSecret", TopSecretRegex()),
            ("publicTrust", PublicTrustRegex()),
            ("secret", SecretRegex()),
            ("other", OtherClearanceRegex())
        };

        foreach (var (level, pattern) in candidates)
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (!excludePreferredSections || !IsInPreferredSection(text, match.Index))
                {
                    return (level, match);
                }
            }
        }

        return ("noneMentioned", Match.Empty);
    }

    private static bool IsInPreferredSection(string text, int matchIndex)
    {
        var start = Math.Max(0, matchIndex - 220);
        var prefix = text[start..matchIndex];
        var preferredIndex = prefix.LastIndexOf("Preferred Qualifications", StringComparison.OrdinalIgnoreCase);
        if (preferredIndex < 0)
        {
            preferredIndex = prefix.LastIndexOf("Preferred Experience", StringComparison.OrdinalIgnoreCase);
        }

        var requiredIndex = prefix.LastIndexOf("Required Qualifications", StringComparison.OrdinalIgnoreCase);
        var basicIndex = prefix.LastIndexOf("Basic Qualifications", StringComparison.OrdinalIgnoreCase);
        return preferredIndex >= 0 && preferredIndex > Math.Max(requiredIndex, basicIndex);
    }

    private static string AnalyzeClearanceRequirement(string context, string level)
    {
        if (ActiveRequiredClearanceRegex().IsMatch(context))
        {
            return "activeRequired";
        }

        if (AlternativeEligibilityRegex().IsMatch(context))
        {
            return "eligible";
        }

        if (level == "publicTrust" && SuitabilityInvestigationRegex().IsMatch(context) &&
            !ObtainAndMaintainClearanceRegex().IsMatch(context) &&
            !ObtainClearanceRegex().IsMatch(context))
        {
            return "publicTrustSuitability";
        }

        if (MustPossessClearanceRegex().IsMatch(context))
        {
            return "mustPossess";
        }

        if (ObtainAndMaintainClearanceRegex().IsMatch(context))
        {
            return "obtainAndMaintain";
        }

        if (ObtainClearanceRegex().IsMatch(context))
        {
            return "obtain";
        }

        if (MaintainClearanceRegex().IsMatch(context))
        {
            return "maintain";
        }

        if (EligibleClearanceRegex().IsMatch(context))
        {
            return "eligible";
        }

        if (PreferredClearanceRegex().IsMatch(context))
        {
            return "preferred";
        }

        return "ambiguous";
    }

    private static Match FindBestClearanceEvidenceMatch(
        string text,
        string level,
        string requirement,
        bool polygraphRequired,
        Match fallback)
    {
        Regex? preferredPattern = level switch
        {
            "topSecretSCI" => TopSecretSciRegex(),
            "topSecret" => TopSecretRegex(),
            "publicTrust" => PublicTrustRegex(),
            "secret" => SecretRegex(),
            _ => OtherClearanceRegex()
        };

        var match = preferredPattern.Match(text);
        if (match.Success)
        {
            return match;
        }

        if (polygraphRequired)
        {
            match = RequiredPolygraphRegex().Match(text);
            if (match.Success)
            {
                return match;
            }
        }

        return fallback;
    }

    [GeneratedRegex(
        @"anticipated\s+salary\s+range\s+for\s+this\s+role(?:\s+will\s+be|\s+is)?\s*" +
        AmountRangePattern + @"(?<context>.{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecificSalaryRegex();

    [GeneratedRegex(
        @"\bSummary\s+(?:Pay|Salary)\s+Ranges?" +
        @"[^$.!?]{0,100}?" + SummaryAmountRangePattern +
        @"(?<context>[^.!?]{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPayRangeRegex();

    [GeneratedRegex(
        @"\bSummary\s+(?:Pay|Salary)\s+Ranges?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPayHeadingRegex();

    [GeneratedRegex(
        @"^[^$.!?]{0,100}?" + SummaryAmountRangePattern + @"(?<context>[^.!?]{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SummarySectionRangeRegex();

    [GeneratedRegex(
        @"\b(?:base\s+)?salary\s+range(?:\s+for\s+this\s+role)?\s*(?:is|:)?\s*" +
        @"(?<minimum>\d[\d,]*(?:\.\d{1,2})?)\s*USD\s*(?:-|to)\s*" +
        @"(?<maximum>\d[\d,]*(?:\.\d{1,2})?)\s*USD(?<context>.{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsdSalaryRangeRegex();

    [GeneratedRegex(
        @"\b(?:Pay|Salary)\s+Range\s*:?\s*(?:(?:Pay|Salary)\s+Range\s*)?" + AmountRangePattern +
        @"(?<context>.{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandardPayRangeRegex();

    [GeneratedRegex(
        @"\b(?:(?:Basic|Projected)\s+Compensation|Compensation\s+Details|" +
        @"projected\s+compensation\s+range(?:\s+for\s+this\s+position)?)\s*(?:is|:)?\s*" +
        AmountRangePattern + @"(?<context>.{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompensationRangeRegex();

    [GeneratedRegex(
        @"\b(?:Minimum|Min)\s+(?:Annual\s+)?Salary\s*:?\s*\$\s*(?<minimum>\d[\d,]*(?:\.\d{1,2})?)" +
        @"(?<context>.{0,100}?)\b(?:Maximum|Max)\s+(?:Annual\s+)?Salary\s*:?\s*\$\s*" +
        @"(?<maximum>\d[\d,]*(?:\.\d{1,2})?)(?<context>.{0,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeparatedSalaryBoundsRegex();

    [GeneratedRegex(@"\b(?:hourly|per\s+hour)\b|/\s*(?:hr|hour)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HourlyCueRegex();

    [GeneratedRegex(@"\b(?:annual|annually|per\s+year|yearly)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnnualCueRegex();

    [GeneratedRegex(@"(?is)<br\s*/?>|</?(?:p|div|h[1-6]|li|ul|ol)[^>]*>")]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"(?is)<[^>]+>")]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z#*])")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(
        @"\bwithin\s+\d{1,3}\s+miles?\s+of\b|\b\d{1,2}\s*hour\s+radius\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DistanceRadiusRegex();

    [GeneratedRegex(@"\bcommuting\s+distance\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommutingDistanceRegex();

    [GeneratedRegex(
        @"\bmust\s+be\s+able\s+to\s+work\s+a\s+hybrid\s+schedule\s+in\s+either\b|" +
        @"\blocal\s+to\b.{0,160}\b(?:work\s+3\s+days|hybrid)\b|" +
        @"\bmust\s+be\s+located\s+near\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HybridLocalRegex();

    [GeneratedRegex(
        @"\bmust\s+live\s+in\s+(?!the\s+(?:u\.s\.|united\s+states))|" +
        @"\bmust\s+be\s+located\s+in\s+the\s+eastern\s+part\b|" +
        @"\bcandidate\s+must\s+reside\s+in\s+(?!the\s+(?:u\.s\.|united\s+states))|" +
        @"\bcandidates?\s+must\s+(?:be\s+)?located\s+in\s+(?!the\s+(?:u\.s\.|united\s+states))|" +
        @"\bcandidates?\s+must\s+located\s+in\b|" +
        @"\bcandidates?\s+should\s+be\s+located\s+in\b|" +
        @"\bmust\s+be\s+located\s+in\s*\(|" +
        @"\bremote(?:ly)?\s+within\s+the\s+(?:eastern|central|mountain|pacific)\s+time\s+zone\b|" +
        @"\blocated\s+in\s+the\s+United\s+States\s+within\s+the\s+following\s+states\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredRegionRegex();

    [GeneratedRegex(
        @"\bif\s+remote\b.{0,180}\b(?:ideally|preference)\b.{0,120}\b(?:reside|time\s+zone)\b|" +
        @"\bremote\s+candidates\b.{0,160}\bpreferably\b|" +
        @"\bideally\b.{0,180}\b(?:located|reside|local\s+to)\b|" +
        @"\bpreference\b.{0,160}\b(?:local\s+to|reside\s+within)\b|" +
        @"\b(?:mountain|central|eastern|pacific)\s+time\s+zone\s+is\s+preferred\b|" +
        @"\bpreferred\s+to\s+be\s+in\s+the\s+(?:central|eastern|mountain|pacific)\s+time\s+zone\b|" +
        @"\ball\s+candidates\s+residing\s+in\s+either\b.{0,160}\bremote\b|" +
        @"\bany\s+candidate\s+located\s+in\b.{0,160}\bremote\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegionalPreferenceRegex();

    [GeneratedRegex(
        @"commuting\s+distance.{0,60}\bis\s+a\s+plus\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommutingPlusRegex();

    [GeneratedRegex(
        @"\b(?:TS\s*/\s*SCI|Top\s+Secret\s*/\s*SCI|Top\s+Secret\s+(?:with\s+)?SCI|SCI\s+eligibility)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopSecretSciRegex();

    [GeneratedRegex(
        @"\bTop\s+Secret\b|\bTS\b.{0,30}\bclearance\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopSecretRegex();

    [GeneratedRegex(
        @"\bPublic\s+Trust\b|\b(?:FAA|TSA)\s+(?:Public\s+Trust\s+)?Suitability\s+Determination\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublicTrustRegex();

    [GeneratedRegex(
        @"\bSecret\b.{0,35}\bclearance\b|\bclearance\b.{0,35}\bSecret\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();

    [GeneratedRegex(
        @"\bsecurity\s+clearance\b|\bclearance\b.{0,45}\b(?:active|current|required|obtain|maintain|possess|eligible|preferred)\b|" +
        @"\b(?:active|current|required|obtain|maintain|possess|eligible|preferred)\b.{0,45}\bclearance\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OtherClearanceRegex();

    [GeneratedRegex(
        @"\b(?:security\s+clearance\s*:\s*)?(?:(?:this\s+)?(?:position|role|job)\s+does\s+not\s+require|no)\s+(?:an?\s+)?(?:u\.?s\.?\s+)?security\s+clearance(?:\s+is\s+required)?\b|" +
        @"\bsecurity\s+clearance\s+requirement\s*:\s*(?:none|not\s+required)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoClearanceRequiredRegex();

    [GeneratedRegex(
        @"\b(?:clearance|Public\s+Trust|TS\s*/\s*SCI|SCI\s+eligibility|suitability\s+determination|background\s+investigation|polygraph)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClearanceContextRegex();

    [GeneratedRegex(
        @"(?:\bpolygraph\b.{0,50}\b(?:required|must|pass|current|active)\b)|" +
        @"(?:\b(?:required|must|pass|current|active)\b.{0,50}\bpolygraph\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredPolygraphRegex();

    [GeneratedRegex(
        @"\b(?:clearance|Public\s+Trust)\b.{0,50}\bpreferred\b|" +
        @"\bpreferred\b.{0,50}\b(?:clearance|Public\s+Trust)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreferredClearanceRegex();

    [GeneratedRegex(
        @"\b(?:active|current)\b.{0,55}\b(?:clearance|Public\s+Trust)\b(?![^.!?]{0,40}\bpreferred\b)|" +
        @"\b(?:clearance|Public\s+Trust)\b.{0,55}\b(?:active|current)\b(?![^.!?]{0,40}\bpreferred\b)|" +
        @"\bholds?\b.{0,35}\bactive\b.{0,35}\bclearance\b(?![^.!?]{0,40}\bpreferred\b)|" +
        @"\bmust\s+currently\s+hold\b.{0,55}\b(?:TS\s*/\s*SCI|Top\s+Secret|Secret|clearance|Public\s+Trust)\b|" +
        @"\b(?:TS\s*/\s*SCI|Top\s+Secret|Secret|clearance|Public\s+Trust)\b.{0,45}\b(?:required\s+)?(?:on\s+day\s+one|at\s+(?:the\s+)?time\s+of\s+hire|at\s+(?:the\s+)?start\s+date)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveRequiredClearanceRegex();

    [GeneratedRegex(
        @"\bmust\s+(?:have|possess|hold)\b.{0,60}\b(?:clearance|Public\s+Trust)\b(?![^.!?]{0,40}\bpreferred\b)|" +
        @"\b(?:possess|hold)\s+and\s+maintain\b.{0,55}\bclearance\b(?![^.!?]{0,40}\bpreferred\b)|" +
        @"\bexisting\b.{0,55}\b(?:clearance|Public\s+Trust)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MustPossessClearanceRegex();

    [GeneratedRegex(
        @"\b(?:ability\s+to|able\s+to|must)\s+obtain\s+and\s+maintain\b.{0,70}\b(?:clearance|Public\s+Trust|investigation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObtainAndMaintainClearanceRegex();

    [GeneratedRegex(
        @"\b(?:ability\s+to|able\s+to|must)\s+obtain\b.{0,70}\b(?:clearance|Public\s+Trust)\b|" +
        @"\b(?:clearance|Public\s+Trust)\b.{0,50}\bprior\s+to\s+(?:start|starting)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObtainClearanceRegex();

    [GeneratedRegex(
        @"\b(?:ability\s+to|able\s+to|must)\s+maintain\b.{0,60}\b(?:clearance|Public\s+Trust)\b|" +
        @"\b(?:clearance|Public\s+Trust)\b.{0,40}\bmaintain\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaintainClearanceRegex();

    [GeneratedRegex(
        @"\b(?:eligible|eligibility|meet\s+the\s+requirements)\b.{0,70}\b(?:clearance|Public\s+Trust|one)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EligibleClearanceRegex();

    [GeneratedRegex(
        @"\bmust\s+(?:have|possess|hold)\b.{0,90}\bclearance\b.{0,90}\bor\s+be\s+able\s+to\s+meet\s+the\s+requirements\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlternativeEligibilityRegex();

    [GeneratedRegex(
        @"\b(?:suitability\s+determination|(?:favorable|extended|government)?\s*background\s+investigation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuitabilityInvestigationRegex();

    [GeneratedRegex(
        @"\bPublic\s+Trust\b.{0,30}\bor\s+(?:a\s+)?security\s+clearance\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmbiguousPublicTrustAlternativeRegex();
}
