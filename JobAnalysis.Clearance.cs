using System.Text.RegularExpressions;

namespace JobSearchManager;

internal static partial class JobAnalysis
{
    public static ClearanceAnalysis AnalyzeClearance(string descriptionHtml) => AnalyzeClearance(descriptionHtml, ClearanceRules.Default);

    internal static ClearanceAnalysis AnalyzeClearance(string descriptionHtml, ClearanceRules rules)
    {
        try { return ExecuteClearance(descriptionHtml, rules); }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException($"Clearance rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Definition.RegexTimeoutMilliseconds}ms regex timeout.", ex);
        }
    }

    private static ClearanceAnalysis ExecuteClearance(string descriptionHtml, ClearanceRules rules)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
            return new ClearanceAnalysis("noneMentioned", "none", false, null, "description-unavailable");

        var definition = rules.Definition;
        var text = rules.Regex(definition.NegationPatternId).Replace(HtmlToPlainText(descriptionHtml), " ");
        var polygraphRequired = rules.Regex(definition.PolygraphPatternId).IsMatch(text);
        var (level, levelMatch) = FindLevel(true);
        var onlyPreferredLevel = false;
        if (level == "noneMentioned")
        {
            (level, levelMatch) = FindLevel(false);
            onlyPreferredLevel = level != "noneMentioned";
        }
        foreach (var rule in definition.LevelOverrides)
        {
            if (level == rule.FromLevel && rules.Regex(rule.PatternId).IsMatch(text) &&
                !rule.UnlessPatternIds.Any(id => rules.Regex(id).IsMatch(text)))
            {
                level = rule.Result;
                levelMatch = rules.Regex(rule.PatternId).Match(text);
            }
        }
        if (level == "noneMentioned" && polygraphRequired)
        {
            level = "other";
            levelMatch = rules.Regex(definition.PolygraphPatternId).Match(text);
        }
        else if (level == "noneMentioned")
            return new ClearanceAnalysis("noneMentioned", "none", false, null, "not-mentioned");

        var relevant = SentenceSplitRegex().Split(text)
            .Where(sentence => rules.Regex(definition.ContextPatternId).IsMatch(sentence))
            .Select(sentence => WhitespaceRegex().Replace(sentence, " ").Trim())
            .Where(sentence => sentence.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var context = relevant.Length > 0 ? string.Join(" ", relevant) : text;
        var requirement = onlyPreferredLevel ? "preferred" : definition.RequirementRules.FirstOrDefault(rule =>
            (rule.Level is null || rule.Level == level) && rules.Regex(rule.PatternId).IsMatch(context) &&
            !rule.UnlessPatternIds.Any(id => rules.Regex(id).IsMatch(context)))?.Result ?? "ambiguous";

        // Preserve the first full-text evidence match, even when level selection skipped a preferred section.
        var evidenceRule = definition.LevelRules.FirstOrDefault(rule => rule.Result == level)
            ?? definition.LevelRules.First(rule => rule.Result == "other");
        var evidence = rules.Regex(evidenceRule.PatternId).Match(text);
        if (!evidence.Success && polygraphRequired) evidence = rules.Regex(definition.PolygraphPatternId).Match(text);
        if (!evidence.Success) evidence = levelMatch;
        return new ClearanceAnalysis(level, requirement, polygraphRequired, CreateSnippet(text, evidence.Index),
            level == "other" || requirement == "ambiguous" ? "ambiguous" : "parsed");

        (string Level, Match Match) FindLevel(bool excludePreferred)
        {
            foreach (var rule in definition.LevelRules)
                foreach (Match match in rules.Regex(rule.PatternId).Matches(text))
                    if (!excludePreferred || !InPreferredSection(match.Index)) return (rule.Result, match);
            return ("noneMentioned", Match.Empty);
        }
        bool InPreferredSection(int index)
        {
            var prefix = text[Math.Max(0, index - definition.Sections.LookbehindCharacters)..index];
            // Marker order is fallback order, not the latest match across all preferred markers.
            var preferred = definition.Sections.PreferredMarkerIds.Select(id => prefix.LastIndexOf(rules.Literal(id), StringComparison.OrdinalIgnoreCase)).FirstOrDefault(position => position >= 0, -1);
            var reset = definition.Sections.ResetMarkerIds.Max(id => prefix.LastIndexOf(rules.Literal(id), StringComparison.OrdinalIgnoreCase));
            return preferred >= 0 && preferred > reset;
        }
    }
}
