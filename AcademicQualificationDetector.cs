using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Deterministically extracts academic qualification paths from job-posting HTML.
/// Academic degrees intentionally remain separate from professional credentials.
/// </summary>
public sealed class AcademicQualificationDetector
{
    public const int CurrentAnalysisVersion = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public int AnalysisVersion => CurrentAnalysisVersion;

    public AcademicQualificationAnalysis Analyze(string descriptionHtml)
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
            var abetMatch = AbetRegex.Match(segment);
            if (abetMatch.Success)
            {
                accreditations.Add(new AcademicAccreditation(
                    "ABET",
                    ClassifyAccreditationRequirement(segment, sectionRequirement),
                    CreateEvidence(segment, abetMatch.Index)));
            }
            var mentions = FindDegreeMentions(segment);
            if (mentions.Count == 0)
            {
                if (InLieuOfDegreeRegex.IsMatch(segment))
                {
                    degreeSubstitutionEvidence.Add(CreateEvidence(
                        segment,
                        InLieuOfDegreeRegex.Match(segment).Index));
                }
                continue;
            }

            var segmentRequirement = ClassifyRequirement(segment, sectionRequirement);
            var fields = ExtractFields(segment, mentions[0]);
            for (var index = 0; index < mentions.Count; index++)
            {
                var mention = mentions[index];
                var nextIndex = index + 1 < mentions.Count ? mentions[index + 1].Index : segment.Length;
                var experience = ExtractExperience(segment, mention, nextIndex);
                paths.Add(new AcademicQualificationPath(
                    mention.Level,
                    mention.SpecificDegree,
                    segmentRequirement,
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
            segments.Any(segment => InLieuOfDegreeRegex.IsMatch(segment)) ||
                DegreeOrExperienceRegex.IsMatch(string.Join(" ", allEvidence)),
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

    private static AcademicQualificationAnalysis NoneSpecified() => new(
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

    private static string ClassifyAccreditationRequirement(string segment, string sectionRequirement)
    {
        if (Regex.IsMatch(segment, @"\bpreferred\b|\bnot\s+required\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout))
        {
            return "preferred";
        }
        if (Regex.IsMatch(segment, @"\b(?:required|must)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout))
        {
            return "required";
        }
        return sectionRequirement;
    }

    private static string DetermineRequirementType(
        IReadOnlyList<string> segments,
        IReadOnlyList<AcademicQualificationPath> paths)
    {
        var requiredPaths = paths
            .Where(path => path.Requirement is "required" or "minimum" or "mentioned")
            .ToArray();
        var relevantText = string.Join(" ", paths.Select(path => path.Evidence));
        if (requiredPaths.Select(path => path.Level).Distinct(StringComparer.Ordinal).Count() > 1 &&
            (requiredPaths.Count(path => path.MinimumExperienceYears is not null) > 1 ||
                HigherDegreeSubstitutionRegex.IsMatch(relevantText)))
        {
            return "degreeWithExperienceSubstitution";
        }

        if (DegreeOrExperienceRegex.IsMatch(relevantText) ||
            segments.Any(segment => InLieuOfDegreeRegex.IsMatch(segment)))
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

    private static List<DegreeMention> FindDegreeMentions(string segment)
    {
        var mentions = new List<DegreeMention>();
        AddMatches(mentions, segment, HighSchoolRegex, "highSchool", null);
        AddMatches(mentions, segment, AssociateRegex, "associate", null);
        AddMatches(mentions, segment, BachelorRegex, "bachelor", null);
        AddMatches(mentions, segment, MasterRegex, "master", null);
        AddMatches(mentions, segment, DoctorateRegex, "doctorate", null);
        AddMatches(mentions, segment, PhDRegex, "doctorate", "phD");

        foreach (Match match in ContextualAbbreviationRegex.Matches(segment))
        {
            var token = Regex.Replace(match.Groups["degree"].Value, @"[.\s]", "").ToUpperInvariant();
            var level = token switch
            {
                "AA" or "AS" or "AAS" => "associate",
                "BA" or "BS" or "BSC" or "BSEE" => "bachelor",
                "MA" or "MS" or "MBA" or "MSC" => "master",
                _ => null
            };
            if (level is not null)
            {
                mentions.Add(new DegreeMention(level, null, match.Index, match.Length));
            }
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

    private static void AddMatches(
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

    private static bool RangesOverlap(DegreeMention left, DegreeMention right) =>
        left.Index < right.Index + right.Length && right.Index < left.Index + left.Length;

    private static (int? Minimum, int? Maximum) ExtractExperience(
        string segment,
        DegreeMention mention,
        int nextMentionIndex)
    {
        var afterLength = Math.Min(Math.Max(0, nextMentionIndex - mention.Index), 150);
        var after = segment.Substring(mention.Index, afterLength);
        var afterMatch = ExperienceAfterDegreeRegex.Match(after);
        if (afterMatch.Success)
        {
            return ParseExperience(afterMatch);
        }

        var beforeStart = Math.Max(0, mention.Index - 80);
        var before = segment.Substring(beforeStart, mention.Index - beforeStart);
        var beforeMatches = ExperienceBeforeDegreeRegex.Matches(before);
        return beforeMatches.Count > 0
            ? ParseExperience(beforeMatches[^1])
            : (null, null);
    }

    private static (int? Minimum, int? Maximum) ParseExperience(Match match)
    {
        var minimum = int.TryParse(match.Groups["min"].Value, out var parsedMinimum)
            ? parsedMinimum
            : (int?)null;
        var maximum = int.TryParse(match.Groups["max"].Value, out var parsedMaximum)
            ? parsedMaximum
            : minimum;
        return (minimum, maximum);
    }

    private static IReadOnlyList<string> ExtractFields(string segment, DegreeMention firstMention)
    {
        var tail = segment[(firstMention.Index + firstMention.Length)..];
        var match = FieldListRegex.Match(tail);
        if (!match.Success)
        {
            return [];
        }

        var value = FieldStopRegex.Split(match.Groups["fields"].Value, 2)[0]
            .Trim(' ', '.', ';', ':');
        if (value.Length == 0 || value.Length > 260)
        {
            return [];
        }

        var acceptedRelatedField = RelatedFieldMentionRegex.IsMatch(value);
        value = ParentheticalRegex.Replace(value, " ");
        var fields = FieldSeparatorRegex.Split(value)
            .Select(NormalizeField)
            .Where(field => field.Length is > 1 and <= 80 &&
                !IgnoredFieldFragmentRegex.IsMatch(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        if (acceptedRelatedField &&
            !fields.Contains("Related field accepted", StringComparer.OrdinalIgnoreCase))
        {
            fields.Add("Related field accepted");
        }
        return fields;
    }

    private static string NormalizeField(string value)
    {
        var field = WhitespaceRegex.Replace(value, " ")
            .Trim(' ', '.', ';', ':', '(', ')');
        field = PreferenceParentheticalRegex.Replace(field, "").Trim();
        field = TrailingFieldQualifierRegex.Replace(field, "").Trim();
        if (RelatedFieldRegex.IsMatch(field))
        {
            return "Related field accepted";
        }
        return field;
    }

    private static string ClassifyRequirement(string segment, string sectionRequirement)
    {
        if (PreferredCueRegex.IsMatch(segment))
        {
            return "preferred";
        }
        if (DesiredCueRegex.IsMatch(segment))
        {
            return "desired";
        }
        if (RequiredCueRegex.IsMatch(segment))
        {
            return "required";
        }
        if (sectionRequirement == "mentioned" && ImplicitQualificationRegex.IsMatch(segment))
        {
            return "minimum";
        }
        return sectionRequirement;
    }

    private static string UpdateSectionRequirement(string segment, string current)
    {
        if (RequiredSectionRegex.IsMatch(segment))
        {
            return RequiredSectionMinimumRegex.IsMatch(segment) ? "minimum" : "required";
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

    private static int LevelRank(string level) => level switch
    {
        "highSchool" => 1,
        "associate" => 2,
        "bachelor" => 3,
        "master" => 4,
        "doctorate" => 5,
        _ => 0
    };

    private static int RequirementPriority(string requirement) => requirement switch
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

    private static readonly Regex BlockTagRegex = CreateRegex(
        @"</?(?:p|li|ul|ol|div|h[1-6]|br|section|article|table|tr|td|th)[^>]*>");
    private static readonly Regex AnyTagRegex = CreateRegex(@"<[^>]+>");
    private static readonly Regex WhitespaceRegex = CreateRegex(@"\s+");
    private static readonly Regex HighSchoolRegex = CreateRegex(
        @"\b(?:high\s+school\s+(?:diploma|education)|GED|secondary\s+education)\b");
    private static readonly Regex AssociateRegex = CreateRegex(
        @"\bassociate(?:['’]s)?\s+(?:degree|of\s+(?:arts|science|applied\s+science))\b");
    private static readonly Regex BachelorRegex = CreateRegex(
        @"\bbachelor(?:['’]s|s)?(?:\s+degree|\s+of\s+(?:arts|science)|(?=\s+in\b))\b|\bundergraduate\s+degree\b");
    private static readonly Regex MasterRegex = CreateRegex(
        @"\bmaster(?:['’]s|s)?(?:\s+degree|\s+of\s+(?:arts|science))\b|\bgraduate\s+degree\b|\bMBA\s+degree\b|" +
        @"\badvanced\s+degree\s*\(\s*master(?:['’]s|s)?\s+or\s+higher\s*\)");
    private static readonly Regex DoctorateRegex = CreateRegex(
        @"\b(?:doctoral(?:-level)?\s+degree|doctorate(?:\s+degree)?)\b");
    private static readonly Regex PhDRegex = CreateRegex(
        @"(?<![\p{L}\p{N}])Ph\.?\s*D\.?(?![\p{L}\p{N}])|\bDoctor\s+of\s+Philosophy\b");
    private static readonly Regex ContextualAbbreviationRegex = CreateRegex(
        @"(?<![\p{L}\p{N}])(?<degree>A\.?\s*A\.?\s*S?\.?|B\.?\s*A\.?|B\.?\s*S\.?\s*(?:C|EE)?\.?|M\.?\s*A\.?|M\.?\s*S\.?\s*C?\.?|MBA)(?![\p{L}\p{N}])" +
        @"(?=\s*(?:degree\b|[/+]\s*\d|(?:and|with)\s+(?:at\s+least\s+)?\d|\bin\s+[A-Za-z]))");
    private static readonly Regex ExperienceAfterDegreeRegex = CreateRegex(
        @"(?:\b(?:with|and|plus)\b|\+)\s*(?:at\s+least\s+|minimum\s+(?:of\s+)?)?(?<min>\d{1,2})\s*\+?\s*(?:[–—-]\s*(?<max>\d{1,2}))?\s*\+?\s*(?:years?|yrs?)\b");
    private static readonly Regex ExperienceBeforeDegreeRegex = CreateRegex(
        @"(?<min>\d{1,2})\s*\+?\s*(?:[–—-]\s*(?<max>\d{1,2}))?\s*\+?\s*(?:years?|yrs?)\s+(?:of\s+[^,;.]{0,50}\s+)?(?:with|for)\s+(?:an?\s+)?$");
    private static readonly Regex DegreeOrExperienceRegex = CreateRegex(
        @"\bor\s+(?:an?\s+)?equivalent\s+(?:professional\s+|relevant\s+)?experience\b|" +
        @"\bequivalent\s+(?:experience|combination\s+of\s+education\s+and\s+experience)\b|" +
        @"\bexperience\s+(?:may|will|can)\s+be\s+(?:considered|accepted|substituted)\b.{0,80}\bin\s+lieu\s+of\b|" +
        @"\bdirect\s+experience\s+may\s+substitute\b");
    private static readonly Regex HigherDegreeSubstitutionRegex = CreateRegex(
        @"\b(?:master|doctorate|doctoral|Ph\.?\s*D\.?).{0,80}\b(?:may|can|will)\s+substitute\s+for\s+(?:the\s+)?required\s+experience\b");
    private static readonly Regex InLieuOfDegreeRegex = CreateRegex(
        @"\b(?:additional\s+(?:years?\s+of\s+)?|equivalent\s+|relevant\s+)?experience\b.{0,100}\bin\s+lieu\s+of\s+(?:an?\s+)?(?:the\s+)?degree(?:\s+requirements?)?\b");
    private static readonly Regex FieldListRegex = CreateRegex(
        @"^\s*(?:degree\s+)?(?:from\s+an?\s+accredited\s+(?:college|university|program|institution)\s+)?(?:in|in\s+the\s+field\s+of)\s+(?<fields>.+)$");
    private static readonly Regex FieldStopRegex = CreateRegex(
        @"\s+(?:with|and)\s+(?:(?:at\s+least|minimum\s+of)\s+)?(?:\d|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)|" +
        @"\s+from\s+(?:an?\s+)?(?:ABET\s+)?accredited\b|\s*;|\s*\.\s*|" +
        @"\s+or\s+(?:an?\s+)?equivalent\s+experience|\s*\((?:or\s+)?equivalent\s+experience\)");
    private static readonly Regex FieldSeparatorRegex = CreateRegex(@"\s*,\s*(?:or\s+)?|\s+or\s+");
    private static readonly Regex RelatedFieldRegex = CreateRegex(
        @"^(?:(?:an?|other)\s+)?related\s+(?:technical\s+)?(?:field|discipline|degree)\b");
    private static readonly Regex RelatedFieldMentionRegex = CreateRegex(
        @"\brelated\s+(?:technical\s+)?(?:field|discipline|degree)\b");
    private static readonly Regex PreferenceParentheticalRegex = CreateRegex(
        @"\s*\((?:[^)]*\s+)?preferred\)\s*$");
    private static readonly Regex ParentheticalRegex = CreateRegex(@"\([^)]*\)");
    private static readonly Regex TrailingFieldQualifierRegex = CreateRegex(
        @"\s+(?:is\s+)?(?:preferred|desired|required)$");
    private static readonly Regex IgnoredFieldFragmentRegex = CreateRegex(
        @"^(?:preferred|desired)$|\b(?:years?|experience|project\s+designs?|completion\s+of|more\s+years?)\b");
    private static readonly Regex PreferredCueRegex = CreateRegex(
        @"\b(?:degree|diploma|GED)\s+(?:is\s+)?preferred\b|" +
        @"\b(?:bachelor(?:['’]s|s)?|master(?:['’]s|s)?|doctorate|Ph\.?\s*D\.?)\s+(?:degree\s+)?(?:is\s+)?preferred\b");
    private static readonly Regex DesiredCueRegex = CreateRegex(
        @"\b(?:degree|diploma|GED)\s+(?:is\s+)?(?:desired|highly\s+desired|desirable)\b|" +
        @"\b(?:bachelor(?:['’]s|s)?|master(?:['’]s|s)?|doctorate|Ph\.?\s*D\.?)\s+(?:degree\s+)?(?:is\s+)?(?:desired|highly\s+desired|desirable)\b");
    private static readonly Regex RequiredCueRegex = CreateRegex(
        @"\b(?:degree\s+)?required\b|\brequires?\b.{0,45}\b(?:degree|BS|MS|doctorate|Ph\.?D\.?)\b|" +
        @"\bmust\s+(?:have|possess|hold)\b.{0,80}\b(?:degree|diploma|GED)\b");
    private static readonly Regex ImplicitQualificationRegex = CreateRegex(
        @"^(?:[•*·-]\s*)?(?:education\s*:\s*)?(?:high\s+school|GED|associate(?:['’]s|s)?|bachelor(?:['’]s|s)?|master(?:['’]s|s)?|doctorate|doctoral|Ph\.?\s*D\.?|" +
        @"A\.?\s*A\.?\s*S?\.?|B\.?\s*[AS]\.?|M\.?\s*[AS]\.?)\b");
    private static readonly Regex RequiredSectionRegex = CreateRegex(
        @"^(?:basic|required|minimum)\s+(?:qualifications?|requirements?|education|experience)\b|" +
        @"^what\s+(?:does\s+)?(?:[\p{L}\p{N}&.'-]+\s+){0,4}need\s+from\s+me\b|^about\s+the\s+must\s+haves\b");
    private static readonly Regex RequiredSectionMinimumRegex = CreateRegex(@"^(?:basic|minimum)\b");
    private static readonly Regex PreferredSectionRegex = CreateRegex(
        @"^(?:preferred|favorable)\s+(?:qualifications?|requirements?|education|experience)\b|" +
        @"^you\s+might\s+also\s+have\b|^bonus\s+points\b");
    private static readonly Regex DesiredSectionRegex = CreateRegex(
        @"^desired\s+(?:qualifications?|requirements?|education|experience)\b");
    private static readonly Regex SectionResetRegex = CreateRegex(
        @"^(?:responsibilities|primary\s+duties|what\s+you['’]ll\s+be\s+doing|original\s+posting|pay\s+range|job\s+description)\s*:?");

    private static readonly Regex AbetRegex = CreateRegex(
        @"\bABET(?:-accredited|\s+accredited|\s+accreditation)\b");

    private static Regex CreateRegex(string pattern) => new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
}
