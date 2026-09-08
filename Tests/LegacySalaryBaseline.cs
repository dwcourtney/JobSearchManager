using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal static partial class LegacySalaryBaseline
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

}
