using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class CredentialRules
{
    private static readonly Lazy<CredentialRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "credential-v1.json")));
    internal static CredentialRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];
    internal static CredentialRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load credential rules '{path}': {ex.Message}", ex); }
    }
    internal static CredentialRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Credential rules must be an object.");
            return new CredentialRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid credential rules: {ex.Message}", ex); }
    }
    private CredentialRules(Definition definition, byte[] bytes)
    {
        Rules = definition; Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid credential rules: {message}"); }
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var version = definition.RulesetVersion.Split('.');
        Require(version.Length == 3 && version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "invalid rulesetVersion.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 250, "invalid finite regex timeout.");
        Require(definition.Patterns.Length is >= 1 and <= 128 && definition.Patterns.All(p => p is not null), "invalid patterns.");
        foreach (var p in definition.Patterns)
        {
            Require(!string.IsNullOrWhiteSpace(p.Id) && p.Id.Length <= 300 && !patterns.ContainsKey(p.Id), "empty or duplicate pattern ID.");
            Require(p.Pattern.Length is >= 1 and <= 8192, "invalid pattern length.");
            patterns.Add(p.Id, new Regex(p.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds)));
        }
        var required = new[] { "GeneralCredentialLanguageRegex", "IgnoredGeneralLanguageRegex" };
        Require(patterns.Count == required.Length && required.All(patterns.ContainsKey), "missing or unsupported pattern reference.");
    }
    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns);
    internal sealed record PatternDefinition(string Id, string Pattern);
}
