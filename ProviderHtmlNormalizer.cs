using System.Text.RegularExpressions;

namespace JobSearchManager;

internal static partial class ProviderHtmlNormalizer
{
    public static string Normalize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";

        var normalized = EncodedNewlinePattern().Replace(html, "<br>");
        normalized = EmptyParagraphRunPattern().Replace(normalized, "<br>");
        return RepeatedBreakPattern().Replace(normalized, "<br><br>");
    }

    [GeneratedRegex(@"(?i)&(?:amp;)*#(?:x0*a|0*10);")]
    private static partial Regex EncodedNewlinePattern();

    [GeneratedRegex(@"(?is)(?:<p\b[^>]*>\s*(?:&nbsp;)?\s*</p>\s*)+")]
    private static partial Regex EmptyParagraphRunPattern();

    [GeneratedRegex(@"(?is)(?:\s*<br\s*/?>){3,}")]
    private static partial Regex RepeatedBreakPattern();
}
