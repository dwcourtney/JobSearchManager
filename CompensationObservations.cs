using System.Globalization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal static class CompensationObservations
{
    internal static FactObservationDocument Observe(JobAnalysis.SalaryObservationSet extracted, string? provider = null)
    {
        var rules = FactualObservationRules.Default;
        var observations = new List<FactualObservation>();
        string? heading = null;
        string? geography = null;
        for (var index = 0; index < extracted.Lines.Length; index++)
        {
            var line = extracted.Lines[index];
            var geographyMatch = rules.Pattern("geography-heading").Match(line);
            if (geographyMatch.Success) geography = geographyMatch.Value.Trim().TrimEnd(':', ',');
            if (rules.Pattern("scope-heading").IsMatch(line) ||
                (!rules.Pattern("money").IsMatch(line) && rules.Pattern("job-level").IsMatch(line)))
            {
                heading = line;
                // An unrecognized scope is preserved verbatim, not guessed to be a jurisdiction.
                if (!geographyMatch.Success) geography = null;
            }
            var money = rules.Pattern("money").Matches(line).Cast<Match>().ToArray();
            var consumedThrough = -1;
            var previousEnd = 0;
            foreach (var match in money)
            {
                if (match.Index < consumedThrough) continue;
                var end = match.Index + match.Length;
                var tail = line[end..];
                var range = rules.Pattern("amount-range").Match(tail);
                var malformed = !range.Success && rules.Pattern("unknown-range").IsMatch(tail);
                decimal? lower = Amount(match.Groups["amount"].Value, match.Groups["scale"].Value);
                decimal? upper = range.Success ? Amount(range.Groups["amount"].Value, range.Groups["scale"].Value) : null;
                var currency = Currency(match.Groups["currency"].Value);
                var secondCurrency = range.Groups["currency"].Success ? Currency(range.Groups["currency"].Value) :
                    range.Groups["suffix"].Success ? Currency(range.Groups["suffix"].Value) : currency;
                var currencyMismatch = range.Success && currency is not null && secondCurrency is not null && currency != secondCurrency;
                if (range.Success) end += range.Length;
                consumedThrough = end;
                var next = money.FirstOrDefault(m => m.Index >= end);
                var suffix = line[end..(next?.Index ?? line.Length)];
                var prefix = line[previousEnd..match.Index];
                var context = prefix + " " + suffix;
                var unit = Unit(context, rules);
                if (unit is null && heading is not null) unit = Unit(heading, rules);
                var basis = Basis(prefix, rules) ?? Basis(suffix, rules) ?? (heading is null ? null : Basis(heading, rules));
                var scope = rules.Scope(line, heading, geography) with {
                    JobLevel = rules.Capture("job-level", line) ?? (heading is null ? null : rules.Capture("job-level", heading)),
                    EmploymentType = rules.Capture("employment-type", line) ?? (heading is null ? null : rules.Capture("employment-type", heading)),
                    RawApplicability = string.Join("\n", new[] { heading, line }.Where(s => s is not null))
                };
                // Unknown currency remains null for '$'. A mismatched range keeps both raw endpoints,
                // and no numeric interval is asserted across currencies.
                var value = new FactValue(range.Success ? "range" : malformed ? "partial-range" : "amount",
                    line[match.Index..end], Lower: lower, Upper: currencyMismatch ? null : upper,
                    Currency: currency, Unit: unit, Basis: basis, UpperCurrency: range.Success ? secondCurrency : null);
                var qualifier = currencyMismatch ? "currency-mismatch-review" : malformed ? "partial-review" :
                    lower is null || range.Success && upper is null ? "unparseable-review" :
                    range.Success && lower > upper ? "reversed-range-review" :
                    rules.Pattern("pay-context").IsMatch(line + " " + heading) || geography is not null ? "explicit" : "monetary-context-unresolved";
                observations.Add(FactObservations.Create("compensation", "monetary-observation", value,
                    "unknown", qualifier, scope, new FactEvidence(line, "salary-line-v1", index, match.Index, end - match.Index),
                    "factual-observations-v1", rules.Fingerprint, "money", provider: provider,
                    connective: rules.Connective(line)));
                if (currencyMismatch)
                    observations.Add(FactObservations.Create("compensation", "mismatched-range-endpoint",
                        new FactValue("amount", range.Value, Lower: upper, Currency: secondCurrency, Unit: unit, Basis: basis),
                        "unknown", "currency-mismatch-review", scope,
                        new FactEvidence(line, "salary-line-v1", index, match.Index + match.Length, range.Length),
                        "factual-observations-v1", rules.Fingerprint, "amount-range", provider: provider));
                previousEnd = end;
            }
        }
        return new(1, FactObservations.Hash(extracted.OriginalHtml), extracted.OriginalHtml, extracted.Lines, observations);
    }

    private static decimal? Amount(string text, string scale)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value)) return null;
        try { return scale.Length == 0 ? value : checked(value * 1000); }
        catch (OverflowException) { return null; }
    }
    private static string? Currency(string text) => text.ToUpperInvariant() switch {
        "USD" or "US$" => "USD", "CAD" or "CA$" => "CAD", "AUD" or "AU$" => "AUD",
        "EUR" or "€" => "EUR", "GBP" or "£" => "GBP", _ => null
    };
    private static string? Unit(string text, FactualObservationRules rules)
    {
        var units = new[] { "hourly", "annual", "monthly", "weekly" }.Where(id => rules.Pattern(id).IsMatch(text)).ToArray();
        return units.Length == 1 ? units[0] : null;
    }
    private static string? Basis(string text, FactualObservationRules rules) => new[] { "base", "bonus", "total" }
        .Select(id => (Id: id, Match: rules.Pattern(id).Matches(text).Cast<Match>().LastOrDefault()))
        .Where(x => x.Match is not null).OrderByDescending(x => x.Match!.Index).Select(x => x.Id).FirstOrDefault();
}
