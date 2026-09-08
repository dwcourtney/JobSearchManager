using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Deterministically extracts academic qualification paths from job-posting HTML.
/// Academic degrees intentionally remain separate from professional credentials.
/// </summary>
public sealed class AcademicQualificationDetector
{
    public const int CurrentAnalysisVersion = 4;
    private readonly EducationRules rules;
    public AcademicQualificationDetector() : this(EducationRules.Default) { }
    internal AcademicQualificationDetector(EducationRules rules) => this.rules = rules;
    public string RulesetVersion => rules.Version;
    public string RulesetFingerprint => rules.Fingerprint;

    public int AnalysisVersion => CurrentAnalysisVersion;

    public AcademicQualificationAnalysis Analyze(string descriptionHtml)
    {
        try { return Execute(descriptionHtml); }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException($"Education rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Rules.RegexTimeoutMilliseconds}ms regex timeout.", ex);
        }
    }

    private AcademicQualificationAnalysis Execute(string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return NoneSpecified();
        }

        var segments = CreateSegments(descriptionHtml);
        var paths = new List<AcademicQualificationPath>();
        var accreditations = new List<AcademicAccreditation>();
        var degreeSubstitutionEvidence = new List<string>();
        var sectionRequirement = "mentioned";

        foreach (var segment in segments)
        {
            sectionRequirement = UpdateSectionRequirement(segment, sectionRequirement);
            var abetMatch = rules.Pattern(rules.Rules.Accreditation.PatternId).Match(segment);
            if (abetMatch.Success)
            {
                accreditations.Add(new AcademicAccreditation(
                    rules.Rules.Accreditation.Name,
                    ClassifyAccreditationRequirement(segment, sectionRequirement),
                    CreateEvidence(segment, abetMatch.Index)));
            }
            var mentions = FindDegreeMentions(segment);
            if (mentions.Count == 0)
            {
                if (rules.Pattern("InLieuOfDegree").IsMatch(segment))
                {
                    degreeSubstitutionEvidence.Add(CreateEvidence(
                        segment,
                        rules.Pattern("InLieuOfDegree").Match(segment).Index));
                }
                continue;
            }

            var fields = ExtractFields(segment, mentions[0]);
            for (var index = 0; index < mentions.Count; index++)
            {
                var mention = mentions[index];
                var nextIndex = index + 1 < mentions.Count ? mentions[index + 1].Index : segment.Length;
                var experience = ExtractExperience(segment, mention, nextIndex);
                var mentionRequirement = ClassifyRequirement(
                    segment,
                    mentions,
                    index,
                    sectionRequirement);
                paths.Add(new AcademicQualificationPath(
                    mention.Level,
                    mention.SpecificDegree,
                    mentionRequirement,
                    experience.Minimum,
                    experience.Maximum,
                    fields,
                    CreateEvidence(segment, mention.Index)));
            }
        }

        var mergedAccreditations = accreditations
            .GroupBy(item => new { item.Name, item.Requirement })
            .Select(group => group.First())
            .ToArray();

        if (paths.Count == 0)
        {
            return degreeSubstitutionEvidence.Count == 0 && mergedAccreditations.Length == 0
                ? NoneSpecified()
                : new AcademicQualificationAnalysis(
                    "noneSpecified",
                    null,
                    mergedAccreditations.Length > 0 ? "accreditationOnly" : "degreeOrExperience",
                    degreeSubstitutionEvidence.Count > 0,
                    [],
                    [],
                    [],
                    degreeSubstitutionEvidence
                        .Concat(mergedAccreditations.Select(item => item.Evidence))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    "ambiguous",
                    AnalysisVersion,
                    mergedAccreditations);
        }

        var mergedPaths = paths
            .GroupBy(path => new
            {
                path.Level,
                path.SpecificDegree,
                path.Requirement,
                path.MinimumExperienceYears,
                path.MaximumExperienceYears,
                Fields = string.Join("\u001f", path.Fields)
            })
            .Select(group => group.First())
            .OrderBy(path => RequirementPriority(path.Requirement))
            .ThenBy(path => LevelRank(path.Level))
            .ThenBy(path => path.MinimumExperienceYears)
            .ToArray();

        var substantivePaths = mergedPaths
            .Where(path => path.Requirement is "required" or "minimum" or "mentioned")
            .ToArray();
        var preferredPaths = mergedPaths
            .Where(path => path.Requirement is "preferred" or "desired")
            .ToArray();
        var minimumSource = substantivePaths.Length > 0 ? substantivePaths : preferredPaths;
        var minimumLevel = minimumSource.Length == 0
            ? "noneSpecified"
            : minimumSource.OrderBy(path => LevelRank(path.Level)).First().Level;
        var specificDegree = minimumSource.Any(path =>
            path.Level == minimumLevel && path.SpecificDegree == "phD")
            ? "phD"
            : null;

        var allEvidence = mergedPaths
            .Select(path => path.Evidence)
            .Concat(degreeSubstitutionEvidence)
            .Concat(mergedAccreditations.Select(item => item.Evidence))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var allFields = mergedPaths
            .SelectMany(path => path.Fields)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preferredLevels = preferredPaths
            .Select(path => path.Level)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(LevelRank)
            .ToArray();

        return new AcademicQualificationAnalysis(
            minimumLevel,
            specificDegree,
            DetermineRequirementType(segments, mergedPaths),
            segments.Any(segment => rules.Pattern("InLieuOfDegree").IsMatch(segment)) ||
                rules.Pattern("DegreeOrExperience").IsMatch(string.Join(" ", allEvidence)),
            allFields,
            preferredLevels,
            mergedPaths,
            allEvidence,
            mergedPaths.Any(path => path.Requirement == "mentioned") ? "ambiguous" : "parsed",
            AnalysisVersion,
            mergedAccreditations);
    }

    public JobRecord AnalyzeJob(JobRecord job) => job with
    {
        AcademicQualification = Analyze(job.DescriptionHtml)
    };

    private AcademicQualificationAnalysis NoneSpecified() => new(
        "noneSpecified",
        null,
        "noDegreeSpecified",
        false,
        [],
        [],
        [],
        [],
        "notFound",
        CurrentAnalysisVersion,
        []);

    private string ClassifyAccreditationRequirement(string segment, string sectionRequirement)
    {
        foreach (var rule in rules.Rules.Accreditation.RequirementRules)
            if (rules.Pattern(rule.PatternId).IsMatch(segment)) return rule.Result;
        return sectionRequirement;
    }

    private string DetermineRequirementType(
        IReadOnlyList<string> segments,
        IReadOnlyList<AcademicQualificationPath> paths)
    {
        var requiredPaths = paths
            .Where(path => path.Requirement is "required" or "minimum" or "mentioned")
            .ToArray();
        var relevantText = string.Join(" ", paths.Select(path => path.Evidence));
        if (requiredPaths.Select(path => path.Level).Distinct(StringComparer.Ordinal).Count() > 1 &&
            (requiredPaths.Count(path => path.MinimumExperienceYears is not null) > 1 ||
                rules.Pattern("HigherDegreeSubstitution").IsMatch(relevantText)))
        {
            return "degreeWithExperienceSubstitution";
        }

        if (rules.Pattern("DegreeOrExperience").IsMatch(relevantText) ||
            segments.Any(segment => rules.Pattern("InLieuOfDegree").IsMatch(segment)))
        {
            return "degreeOrExperience";
        }

        if (requiredPaths.Length == 0 && paths.Count > 0)
        {
            return "preferredOnly";
        }

        return requiredPaths.Any(path => path.Requirement is "required" or "minimum")
            ? "strictDegree"
            : "mentionedUnclear";
    }

    private List<DegreeMention> FindDegreeMentions(string segment)
    {
        var mentions = new List<DegreeMention>();
        foreach (var rule in rules.Rules.DegreeRules)
            AddMatches(mentions, segment, rules.Pattern(rule.PatternId), rule.Level, rule.SpecificDegree);

        var abbreviations = rules.Rules.Abbreviations;
        foreach (Match match in rules.Pattern(abbreviations.PatternId).Matches(segment))
        {
            var token = rules.Pattern(abbreviations.NormalizationPatternId)
                .Replace(match.Groups[abbreviations.CaptureGroup].Value, "").ToUpperInvariant();
            if (abbreviations.Levels.TryGetValue(token, out var level))
                mentions.Add(new DegreeMention(level, null, match.Index, match.Length));
        }

        // Prefer the explicit Ph.D classification over a generic doctorate match at
        // the same position, then discard overlaps and repeated wording.
        return mentions
            .OrderBy(mention => mention.Index)
            .ThenByDescending(mention => mention.SpecificDegree is not null)
            .ThenByDescending(mention => mention.Length)
            .Aggregate(new List<DegreeMention>(), (result, mention) =>
            {
                if (!result.Any(existing => RangesOverlap(existing, mention)))
                {
                    result.Add(mention);
                }
                return result;
            });
    }

    private void AddMatches(
        ICollection<DegreeMention> target,
        string segment,
        Regex regex,
        string level,
        string? specificDegree)
    {
        foreach (Match match in regex.Matches(segment))
        {
            target.Add(new DegreeMention(level, specificDegree, match.Index, match.Length));
        }
    }

    private bool RangesOverlap(DegreeMention left, DegreeMention right) =>
        left.Index < right.Index + right.Length && right.Index < left.Index + left.Length;

    private (int? Minimum, int? Maximum) ExtractExperience(
        string segment,
        DegreeMention mention,
        int nextMentionIndex)
    {
        var afterLength = Math.Min(Math.Max(0, nextMentionIndex - mention.Index), 150);
        var after = segment.Substring(mention.Index, afterLength);
        var afterMatch = rules.Pattern("ExperienceAfterDegree").Match(after);
        if (afterMatch.Success)
        {
            return ParseExperience(afterMatch);
        }

        var beforeStart = Math.Max(0, mention.Index - 80);
        var before = segment.Substring(beforeStart, mention.Index - beforeStart);
        var beforeMatches = rules.Pattern("ExperienceBeforeDegree").Matches(before);
        return beforeMatches.Count > 0
            ? ParseExperience(beforeMatches[^1])
            : (null, null);
    }

    private (int? Minimum, int? Maximum) ParseExperience(Match match)
    {
        var minimum = int.TryParse(match.Groups["min"].Value, out var parsedMinimum)
            ? parsedMinimum
            : (int?)null;
        var maximum = int.TryParse(match.Groups["max"].Value, out var parsedMaximum)
            ? parsedMaximum
            : minimum;
        return (minimum, maximum);
    }

    private IReadOnlyList<string> ExtractFields(string segment, DegreeMention firstMention)
    {
        var tail = segment[(firstMention.Index + firstMention.Length)..];
        var match = rules.Pattern("FieldList").Match(tail);
        if (!match.Success)
        {
            return [];
        }

        var value = rules.Pattern("FieldStop").Split(match.Groups["fields"].Value, 2)[0]
            .Trim(' ', '.', ';', ':');
        if (value.Length == 0 || value.Length > 260)
        {
            return [];
        }

        var acceptedRelatedField = rules.Pattern("RelatedFieldMention").IsMatch(value);
        value = rules.Pattern("Parenthetical").Replace(value, " ");
        var fields = rules.Pattern("FieldSeparator").Split(value)
            .Select(NormalizeField)
            .Where(field => field.Length is > 1 and <= 80 &&
                !rules.Pattern("IgnoredFieldFragment").IsMatch(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        if (acceptedRelatedField &&
            !fields.Contains(rules.Rules.RelatedFieldResult, StringComparer.OrdinalIgnoreCase))
        {
            fields.Add(rules.Rules.RelatedFieldResult);
        }
        return fields;
    }

    private string NormalizeField(string value)
    {
        var field = rules.Pattern("Whitespace").Replace(value, " ")
            .Trim(' ', '.', ';', ':', '(', ')');
        field = rules.Pattern("PreferenceParenthetical").Replace(field, "").Trim();
        field = rules.Pattern("TrailingFieldQualifier").Replace(field, "").Trim();
        if (rules.Pattern("RelatedField").IsMatch(field))
        {
            return rules.Rules.RelatedFieldResult;
        }
        return field;
    }

    private string ClassifyRequirement(
        string segment,
        IReadOnlyList<DegreeMention> mentions,
        int mentionIndex,
        string sectionRequirement)
    {
        var groupStart = mentionIndex;
        while (groupStart > 0 && rules.Pattern("DegreeAlternativeConnector").IsMatch(
            segment[RangeEnd(mentions[groupStart - 1])..mentions[groupStart].Index]))
        {
            groupStart--;
        }
        var groupEnd = mentionIndex;
        while (groupEnd + 1 < mentions.Count && rules.Pattern("DegreeAlternativeConnector").IsMatch(
            segment[RangeEnd(mentions[groupEnd])..mentions[groupEnd + 1].Index]))
        {
            groupEnd++;
        }

        var contextStart = mentions[groupStart].Index;
        var contextEnd = groupEnd + 1 < mentions.Count
            ? mentions[groupEnd + 1].Index
            : segment.Length;
        var context = segment[contextStart..contextEnd];
        var explicitRequirement = EarliestExplicitRequirement(context);
        if (explicitRequirement is not null)
        {
            return explicitRequirement;
        }
        if (sectionRequirement == "mentioned" && rules.Pattern("ImplicitQualification").IsMatch(segment))
        {
            return "minimum";
        }
        return sectionRequirement;
    }

    private int RangeEnd(DegreeMention mention) => mention.Index + mention.Length;

    private string? EarliestExplicitRequirement(string context)
    {
        var matches = rules.Rules.QualifierRules.Select(rule =>
            (Match: rules.Pattern(rule.PatternId).Match(context), Requirement: rule.Result));
        return matches
            .Where(candidate => candidate.Match.Success)
            .Where(candidate => candidate.Requirement != "preferred" ||
                !IsFieldPreferenceQualifier(context, candidate.Match))
            .OrderBy(candidate => candidate.Match.Index)
            .ThenBy(candidate => RequirementPriority(candidate.Requirement))
            .Select(candidate => candidate.Requirement)
            .FirstOrDefault();
    }

    private bool IsFieldPreferenceQualifier(string context, Match qualifier)
    {
        foreach (Match fieldPreference in rules.Pattern("FieldPreferenceQualifier").Matches(context))
        {
            if (qualifier.Index >= fieldPreference.Index &&
                qualifier.Index < fieldPreference.Index + fieldPreference.Length)
            {
                return true;
            }
        }
        return false;
    }

    private string UpdateSectionRequirement(string segment, string current)
    {
        foreach (var rule in rules.Rules.SectionRules)
        {
            if (!rules.Pattern(rule.PatternId).IsMatch(segment)) continue;
            return rule.ConditionalPatternId is not null && rules.Pattern(rule.ConditionalPatternId).IsMatch(segment)
                ? rule.ConditionalResult! : rule.Result;
        }
        return current;
    }

    private IReadOnlyList<string> CreateSegments(string html)
    {
        var withBreaks = rules.Pattern("BlockTag").Replace(html, "\n");
        var withoutTags = rules.Pattern("AnyTag").Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => rules.Pattern("Whitespace").Replace(segment, " ").Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();
    }

    private string CreateEvidence(string segment, int matchIndex)
    {
        const int maximumLength = 360;
        if (segment.Length <= maximumLength)
        {
            return segment;
        }

        var start = Math.Max(0, matchIndex - 80);
        var length = Math.Min(maximumLength, segment.Length - start);
        var value = segment.Substring(start, length).Trim();
        return (start > 0 ? "…" : "") + value +
            (start + length < segment.Length ? "…" : "");
    }

    private int LevelRank(string level) => level switch
    {
        "highSchool" => 1,
        "associate" => 2,
        "bachelor" => 3,
        "master" => 4,
        "doctorate" => 5,
        _ => 0
    };

    private int RequirementPriority(string requirement) => requirement switch
    {
        "required" => 0,
        "minimum" => 1,
        "preferred" => 2,
        "desired" => 3,
        _ => 4
    };

    private sealed record DegreeMention(
        string Level,
        string? SpecificDegree,
        int Index,
        int Length);

    }
