using System.Net;
using System.Text.RegularExpressions;

namespace WorkdayJobManager;

/// <summary>
/// Conservatively identifies candidate work-authorization and citizenship wording.
/// Export-control references and uncertain language are review-only by design.
/// </summary>
public sealed class WorkAuthorizationDetector
{
    public const int CurrentAnalysisVersion = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex CitizenOrResident = Pattern(
        @"\b(?:u\.?s\.?|united states)\s+citizen\s+(?:or|and/or)\s+(?:an?\s+)?(?:(?:u\.?s\.?|united states)\s+)?(?:lawful\s+)?permanent\s+resident|" +
        @"\b(?:u\.?s\.?|united states)\s+(?:citizen(?:ship)?|national)\b.{0,55}\b(?:or|and/or)\b.{0,35}(?:an?\s+)?(?:(?:u\.?s\.?|united states)\s+)?(?:lawful\s+)?permanent\s+resident|" +
        @"\b(?:u\.?s\.?|united states)\s+(?:citizen(?:ship)?|national)\b.{0,55}\bgreen\s+card\b|" +
        @"\b(?:u\.?s\.?|united states)\s+citizenship\b.{0,20}\bor\b.{0,20}\b(?:u\.?s\.?|united states)\s+permanency\b|" +
        @"\b(?:citizen(?:ship)?\s+or\s+(?:u\.?s\.?\s+)?permanent\s+resident)\b");
    private static readonly Regex UsCitizen = Pattern(
        @"\b(?:must|shall|need(?:s)?\s+to|required\s+to)\s+be\s+(?:an?\s+)?(?:u\.?s\.?|united states)\s+citizen\b|" +
        @"\b(?:u\.?s\.?|united states)\s+citizenship\b.{0,45}\b(?:is\s+)?(?:required|mandatory|must)\b|" +
        @"\b(?:requires?|requirement)\b.{0,45}\b(?:u\.?s\.?|united states)\s+citizenship\b|" +
        @"\bcandidate\b.{0,45}\b(?:u\.?s\.?|united states)\s+citizen\b");
    private static readonly Regex AustralianCitizen = Pattern(
        @"\b(?:must|shall|need(?:s)?\s+to|required\s+to)\s+be\s+(?:an?\s+)?australian\s+citizen\b|" +
        @"\baustralian\s+citizenship\b.{0,45}\b(?:required|mandatory)\b|" +
        @"\brequires?\b.{0,45}\baustralian\s+citizen\b");
    private static readonly Regex PreferredUsCitizen = Pattern(
        @"\b(?:u\.?s\.?|united states)\s+(?:citizen|citizenship)\b.{0,35}\b(?:preferred|desired|ideally)\b|" +
        @"\b(?:preferred|desired|ideally)\b.{0,35}\b(?:u\.?s\.?|united states)\s+(?:citizen|citizenship)\b|" +
        @"\bshould\s+(?:ideally\s+)?be\s+(?:an?\s+)?(?:u\.?s\.?|united states)\s+citizen\b");
    private static readonly Regex PreferredAustralianCitizen = Pattern(
        @"\baustralian\s+(?:citizen|citizenship)\b.{0,35}\b(?:preferred|desired|ideally)\b|" +
        @"\b(?:preferred|desired|ideally)\b.{0,35}\baustralian\s+(?:citizen|citizenship)\b");
    private static readonly Regex UsWorkAuthorized = Pattern(
        @"\b(?:must\s+be|are)\s+(?:legally\s+)?authorized\s+to\s+work\s+in\s+the\s+(?:u\.?s\.?|united states)\b|" +
        @"\b(?:legal\s+)?(?:right|authorization)\s+to\s+work\s+in\s+the\s+(?:u\.?s\.?|united states)\b");
    private static readonly Regex LocationWorkAuthorized = Pattern(
        @"\bvalid\s+work(?:ing)?\s+rights?\s+for\s+the\s+(?:role|job)\s+location\b|" +
        @"\b(?:must\s+(?:have|hold)|required\s+to\s+have|valid|existing|unrestricted)\b.{0,35}" +
        @"\b(?:legal\s+)?(?:right|rights|authori[sz]ation)\s+to\s+work\s+in\s+(?:the\s+)?[A-Za-z][A-Za-z .'-]{1,40}\b");
    private static readonly Regex NoEmploymentSponsorship = Pattern(
        @"\b(?:will|does|do|can)\s+not\s+(?:provide|offer)?\s*(?:employment\s+|visa\s+|work(?:\s+authorization)?\s+)?sponsorship\b|" +
        @"\b(?:employer|company|we)\s+will\s+not\s+sponsor\s+(?:applicants?|candidates?|individuals?)\b.{0,80}\b(?:employment\s+)?visa\s+status\b|" +
        @"\bnot\s+(?:be\s+)?require(?:d)?\s+(?:employment\s+|visa\s+|work(?:\s+authorization)?\s+)?sponsorship\b|" +
        @"\bwithout\s+(?:current\s+or\s+future\s+)?(?:employment\s+|visa\s+)?sponsorship\b|" +
        @"\bno\s+(?:employment\s+|visa\s+|work(?:\s+authorization)?\s+)?sponsorship\b");
    private static readonly Regex UsPerson = Pattern(@"\b(?:u\.?s\.?|united states)\s+person\b");
    private static readonly Regex CandidateContext = Pattern(
        @"\b(?:candidate|applicant|employee|individual|personnel|must|required|requirement|eligible|qualification)\b");
    private static readonly Regex CandidateUsPersonPredicate = Pattern(
        @"\b(?:be|are)\b.{0,35}\b(?:u\.?s\.?|united states)\s+person\b");
    private static readonly Regex InformationContext = Pattern(
        @"\b(?:information|data|communications?|queries|identit(?:y|ies)|privacy|surveillance)\b");
    private static readonly Regex GenericCitizenship = Pattern(
        @"\bcitizenship\b.{0,25}\b(?:required|mandatory)\b|\b(?:required|mandatory)\s+citizenship\b");
    private static readonly Regex ExportControl = Pattern(
        @"\b(?:ITAR|EAR|export\s+control(?:led|s)?|international\s+traffic\s+in\s+arms\s+regulations?)\b");
    private static readonly Regex RequiredHeading = Pattern(
        @"^(?:basic|required|minimum|mandatory|essential)\s+(?:qualifications?|requirements?|criteria)\s*:?$");
    private static readonly Regex PreferredHeading = Pattern(
        @"^(?:preferred|desired)\s+(?:qualifications?|requirements?|criteria)\s*:?$");
    private static readonly Regex BareUsCitizen = Pattern(
        @"^(?:an?\s+)?(?:u\.?s\.?|united states)\s+(?:citizen|citizenship)\s*\.?$");
    private static readonly Regex BareAustralianCitizen = Pattern(
        @"^(?:an?\s+)?australian\s+(?:citizen|citizenship)\s*\.?$");
    private static readonly Regex Conditional = Pattern(
        @"\b(?:may|might|could)\b.{0,35}\b(?:need|required|requirement|limitation)\b|\bin\s+certain\s+circumstances\b");

    public WorkAuthorizationAnalysis Analyze(string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml)) return NoneSpecified();

        var segments = Segments(descriptionHtml).ToArray();
        var evidence = new List<string>();
        var eligibility = "noneSpecified";
        var sponsorship = "noneSpecified";
        var strength = "none";
        var sponsorshipStrength = "none";
        string? countryCode = null;
        var sectionContext = "mentioned";

        foreach (var segment in segments)
        {
            if (RequiredHeading.IsMatch(segment))
            {
                sectionContext = "strict";
                continue;
            }
            if (PreferredHeading.IsMatch(segment))
            {
                sectionContext = "preferred";
                continue;
            }
            if (NoEmploymentSponsorship.IsMatch(segment) &&
                !Regex.IsMatch(segment, @"\b(?:clearance|training|SOFA|command[- ]sponsor|enterprise\s+PKI)\b",
                    RegexOptions.IgnoreCase, RegexTimeout))
            {
                sponsorship = "notAvailable";
                sponsorshipStrength = "strict";
                evidence.Add(Evidence(segment, NoEmploymentSponsorship.Match(segment).Index));
            }

            if (UsPerson.IsMatch(segment) &&
                (CandidateContext.IsMatch(segment) || CandidateUsPersonPredicate.IsMatch(segment)) &&
                !InformationContext.IsMatch(segment))
            {
                eligibility = "usPerson";
                countryCode = "US";
                strength = "ambiguous";
                evidence.Add(Evidence(segment, UsPerson.Match(segment).Index));
            }
            else if (CitizenOrResident.IsMatch(segment))
            {
                SetEligibility("usCitizenOrPermanentResident", "US", segment, CitizenOrResident.Match(segment).Index);
            }
            else if (UsCitizen.IsMatch(segment))
            {
                SetEligibility("usCitizen", "US", segment, UsCitizen.Match(segment).Index);
            }
            else if (AustralianCitizen.IsMatch(segment))
            {
                SetEligibility("australianCitizen", "AU", segment, AustralianCitizen.Match(segment).Index);
            }
            else if (UsWorkAuthorized.IsMatch(segment))
            {
                SetEligibility("usWorkAuthorized", "US", segment, UsWorkAuthorized.Match(segment).Index);
            }
            else if (LocationWorkAuthorized.IsMatch(segment))
            {
                eligibility = "locationWorkAuthorized";
                strength = "strict";
                evidence.Add(Evidence(segment, LocationWorkAuthorized.Match(segment).Index));
            }
            else if (PreferredUsCitizen.IsMatch(segment))
            {
                SetEligibility("usCitizen", "US", segment, PreferredUsCitizen.Match(segment).Index, "preferred");
            }
            else if (PreferredAustralianCitizen.IsMatch(segment))
            {
                SetEligibility("australianCitizen", "AU", segment,
                    PreferredAustralianCitizen.Match(segment).Index, "preferred");
            }
            else if (sectionContext is "strict" or "preferred" && BareUsCitizen.IsMatch(segment))
            {
                SetEligibility("usCitizen", "US", segment, 0, sectionContext);
            }
            else if (sectionContext is "strict" or "preferred" && BareAustralianCitizen.IsMatch(segment))
            {
                SetEligibility("australianCitizen", "AU", segment, 0, sectionContext);
            }
            else if (GenericCitizenship.IsMatch(segment))
            {
                eligibility = eligibility == "noneSpecified" ? "ambiguousCitizenship" : eligibility;
                strength = "ambiguous";
                evidence.Add(Evidence(segment, GenericCitizenship.Match(segment).Index));
            }
            else if (ExportControl.IsMatch(segment) && eligibility == "noneSpecified")
            {
                eligibility = "exportControlled";
                strength = Conditional.IsMatch(segment) ? "customerDependent" : "mentioned";
                evidence.Add(Evidence(segment, ExportControl.Match(segment).Index));
            }
        }

        if (eligibility == "noneSpecified" && sponsorship == "noneSpecified") return NoneSpecified();
        return new WorkAuthorizationAnalysis(
            eligibility,
            sponsorship,
            strength,
            sponsorshipStrength,
            countryCode,
            evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
            strength is "strict" or "preferred" || sponsorshipStrength == "strict"
                ? "parsed"
                : "review",
            CurrentAnalysisVersion);

        void SetEligibility(
            string value,
            string country,
            string segment,
            int matchIndex,
            string detectedStrength = "strict")
        {
            if (eligibility != "noneSpecified" && eligibility != "exportControlled") return;
            eligibility = value;
            countryCode = country;
            strength = detectedStrength;
            evidence.Add(Evidence(segment, matchIndex));
        }
    }

    public JobRecord AnalyzeJob(JobRecord job) => job with
    {
        WorkAuthorization = Analyze(job.DescriptionHtml)
    };

    private static IEnumerable<string> Segments(string html)
    {
        var blockSeparated = Regex.Replace(html,
            @"</?(?:p|div|li|ul|ol|h[1-6]|br|section|article)[^>]*>", "\n",
            RegexOptions.IgnoreCase, RegexTimeout);
        var plain = WebUtility.HtmlDecode(Regex.Replace(blockSeparated, "<[^>]+>", " ",
            RegexOptions.Singleline, RegexTimeout));
        // Prevent sentence splitting inside the common U.S. abbreviation.
        plain = Regex.Replace(plain, @"\bU\.\s*S\.?", "US", RegexOptions.IgnoreCase, RegexTimeout);
        return Regex.Split(plain, @"(?:\r?\n)+|(?<=[.!?;])\s+(?=[A-Z])", RegexOptions.None, RegexTimeout)
            .Select(value => Regex.Replace(value, @"\s+", " ").Trim())
            .Where(value => value.Length > 0);
    }

    private static string Evidence(string segment, int index)
    {
        const int maximum = 320;
        if (segment.Length <= maximum) return segment;
        var start = Math.Clamp(index - 80, 0, segment.Length - maximum);
        return $"{(start > 0 ? "…" : "")}{segment.Substring(start, maximum)}…";
    }

    private static WorkAuthorizationAnalysis NoneSpecified() => new(
        "noneSpecified", "noneSpecified", "none", "none", null, [], "not-mentioned", CurrentAnalysisVersion);

    private static Regex Pattern(string pattern) => new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
}
