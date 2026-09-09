using System.Text.Json.Nodes;
using JobSearchManager;

internal static class VirtualizationRuleTests
{
    internal static Task RunAsync()
    {
        const string target = "technical.virtualization";
        var snapshot = JsonAuthorityTests.Snapshot();
        if (snapshot.Rules.Length != 309 || snapshot.Rules.Count(r => r.ConceptId == target) != 13)
            throw new Exception("Expected 13 Virtualization rules and 309 total rules.");
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "concept-oracle", "pre-virtualization-rules.json")))!;
        var current = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json")))!;
        var oldRules = baseline["rules"]!.AsArray();
        var newRules = current["rules"]!.AsArray().ToDictionary(r => r!["ruleId"]!.GetValue<string>());
        foreach (var old in oldRules)
        {
            var id = old!["ruleId"]!.GetValue<string>();
            var actual = newRules[id]!.DeepClone().AsObject();
            var expected = old.DeepClone().AsObject();
            actual.Remove("executionOrder"); expected.Remove("executionOrder");
            if (id == "legacy-1cdc541111c30c6f13500905")
                expected["pattern"] = @"(?<!\bdata\s)(?<!\bpaging/)\bvirtualization\b";
            if (!JsonNode.DeepEquals(actual, expected))
                throw new Exception("Unexpected existing-rule change: " + id);
        }
        var originalIds = oldRules.Select(r => r!["ruleId"]!.GetValue<string>()).ToHashSet();
        if (newRules.Where(r => !originalIds.Contains(r.Key)).Any(r => r.Value!["conceptId"]!.GetValue<string>() != target))
            throw new Exception("New rule changed an unrelated concept.");
        baseline.AsObject().Remove("rules"); current.AsObject().Remove("rules");
        if (!JsonNode.DeepEquals(baseline, current)) throw new Exception("Non-rule authority metadata changed.");
        (string Text, bool Expected)[] cases = [
            ("Support virtual machines and VM-based environments.", true),
            ("Develop virtualized infrastructures.", true),
            ("Design hypervisor software.", true),
            ("Configure vCenter, ESXi and Proxmox.", true),
            ("Experience with VMware.", true),
            ("Manage VMware Cloud Foundation.", true),
            ("Setup of virtual environments for server support.", true),
            ("Operate Azure VMs.", true),
            ("Linux host/VM networking.", true),
            ("Virtual machine management (VMware and KVM).", true),
            ("Manage virtual servers and virtual desktops.", true),
            ("VMware Horizon VDI.", true),
            ("Kubernetes on-prem, preferably Harvester.", true),
            ("Use data virtualization/replication.", false),
            ("Use server-side paging/virtualization.", false),
            ("Data virtualization. Later manage virtualization platforms.", true),
            ("Paging/virtualization. Operate Hyper-V hosts.", true),
            ("Work with virtual teams and virtual meetings.", false),
            ("Build virtual reality applications.", false),
            ("Video Management Systems (VMS).", false),
            ("Vehicle Management Systems (VMS).", false),
            ("Virtual Manufacturing (VM) simulation reviews.", false),
            ("Operate KVM switches.", false),
            ("Research long-horizon planning.", false),
            ("Develop software for a harvester.", false),
            ("Work in a virtual environment.", false),
            ("AWS VPC, Azure VNet and GCP VPC.", false),
            ("VMware is a customer.", false),
            ("VCF NSX VDI AHV are acronyms.", false)
        ];
        foreach (var (text, expected) in cases)
        {
            var result = snapshot.Matcher.Classify("Engineer", "<p>" + text + "</p>", null, null, false);
            if (result.TimedOutRuleIds.Count != 0 || result.Concepts.Any(c => c.ConceptId == target) != expected)
                throw new Exception("Virtualization boundary failed: " + text);
        }
        Console.WriteLine($"PASS Virtualization: {cases.Length} boundary cases; all 84 other concepts and authority metadata preserved.");
        return Task.CompletedTask;
    }
}
