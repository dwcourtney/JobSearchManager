using System.Text.Json;
using JobSearchManager;

internal static class ArtificialIntelligenceRuleTests
{
    internal static Task RunAsync()
    {
        const string target = "technical.artificial-intelligence";
        var snapshot = JsonAuthorityTests.Snapshot();
        if (snapshot.Rules.Length != 299 || snapshot.Rules.Count(r => r.ConceptId == target) != 14)
            throw new Exception("Expected exactly eleven added AI rules and the original three.");
        using var baseline = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "concept-oracle", "sqlite-effective-rules.json")));
        foreach (var old in baseline.RootElement.GetProperty("rules").EnumerateArray())
        {
            var rule = snapshot.Rules.Single(r => r.RuleId == old.GetProperty("ruleId").GetString());
            if (rule.ConceptId != old.GetProperty("conceptId").GetString() || rule.Kind != old.GetProperty("ruleType").GetString() ||
                rule.Scope != old.GetProperty("scope").GetString() ||
                (rule.Pattern ?? rule.Selector?.Category ?? "remote-designation") != old.GetProperty("pattern").GetString())
                throw new Exception("Original production rule changed: " + rule.RuleId);
        }
        if (snapshot.Rules.Where(r => r.RuleId.StartsWith("ai-20260909-", StringComparison.Ordinal)).Any(r => r.ConceptId != target))
            throw new Exception("An added rule changed another concept.");
        (string Title, string Text, bool Expected)[] cases = [
            ("AI Engineer", "Develop production services.", true),
            ("Senior Engineer, AI Storage", "Develop storage software.", true),
            ("AI&T Technician", "Assembly, integration and test of hardware.", false),
            ("AI & T Technician", "Hardware qualification testing.", false),
            ("Engineer", "Develop generative AI solutions.", true),
            ("Engineer", "Experience implementing GenAI.", true),
            ("Engineer", "Build agentic AI systems.", true),
            ("Engineer", "Develop agentic workflows.", true),
            ("Engineer", "Deploy AI models and AI applications.", true),
            ("Engineer", "AI inference serving software.", true),
            ("Engineer", "Experience using AI-assisted tools for programming.", true),
            ("Engineer", "Evaluate and implement AI-driven testing.", true),
            ("Engineer", "Apply AI to improve engineering workflows.", true),
            ("Engineer", "Operate AI training infrastructure.", true),
            ("Engineer", "Optimize AI workloads.", true),
            ("Engineer", "Develop large language models.", true),
            ("Engineer", "Build statistical and machine learning models.", true),
            ("Engineer", "Optimize deep learning models.", true),
            ("Engineer", "NVIDIA uses AI tools in its recruiting processes.", false),
            ("Engineer", "Our GPUs are the brains of computers, generative AI, robots, and self-driving cars.", false),
            ("Exercise Planner", "Our business area integrates technologies including AI-driven analytics.", false),
            ("Engineer", "Build core infrastructure services that power global AI infrastructure.", false),
            ("Data Center Buildouts", "Build the world's largest AI factories and cooling systems.", false),
            ("Border Advisor", "Disrupt diversion of advanced AI technologies.", false),
            ("Strategist", "Explore the AI adoption cliff and AI-accelerated operations.", false),
            ("Lawyer", "An LLM degree is preferred.", false),
            ("Analyst", "Machine learning and NLP familiarity preferred.", false),
            ("Administrator", "Basic understanding of Copilot Studio.", false),
            ("Engineer", "This is not agentic AI. Ordinary automation only.", false),
            ("Engineer", "This is not agentic AI. Later, build agentic AI systems.", true)
        ];
        foreach (var (title, text, expected) in cases)
        {
            var result = snapshot.Matcher.Classify(title, "<p>" + text + "</p>", null, null, false);
            if (result.TimedOutRuleIds.Count != 0 || result.Concepts.Any(c => c.ConceptId == target) != expected)
                throw new Exception("AI rule boundary failed: " + title + " / " + text);
        }
        Console.WriteLine($"PASS AI rules: {cases.Length} positive/acronym/marketing/context/negation cases; original rules preserved.");
        return Task.CompletedTask;
    }
}
