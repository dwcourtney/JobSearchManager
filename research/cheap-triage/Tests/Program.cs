using JobSearchManager;
await CheapRejectRuleTests.Run();
await HumanReviewTests.Run();
await MaintenanceDetectionTests.Run();
await TestCheapTriageAsync();
Console.WriteLine("All 4 Cheap Triage research rules/review/discovery/classifier suites passed.");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static Task TestCheapTriageAsync()
{
    var software = CheapTriageClassifier.Classify("Senior Software Engineer",
        "Build C# .NET APIs and Azure deployment automation for aircraft systems.");
    Assert(software.SendToJobFit && software.TechnicalEvidence.Any(item =>
            item.Bucket == "Software / application development"),
        "A clearly relevant software role did not survive with explainable evidence.");

    var utility = CheapTriageClassifier.Classify("Substation Technician",
        "Inspect high-voltage equipment and maintain utility substations in the field.");
    Assert(!utility.SendToJobFit && utility.RejectedAtStage == "stage-1" &&
           utility.RejectionReason == "Physical electrical utility field role",
        "A clear physical utility role was not rejected with the registered reason.");

    var ambiguous = CheapTriageClassifier.Classify("Senior Engineer",
        "Work with cross-functional teams on complex customer problems.");
    Assert(ambiguous.SendToJobFit && ambiguous.RejectionReason is null,
        "An ambiguous posting was rejected instead of conservatively retained.");

    var technicalField = CheapTriageClassifier.Classify("Field Service Technician",
        "Automate Linux systems with Python, Kubernetes, and CI/CD pipelines.");
    Assert(technicalField.SendToJobFit,
        "Technical evidence did not protect an otherwise physical-sounding title.");

    var first = CheapTriageClassifier.Classify("Network Engineer",
        "Configure Cisco routing, switching, firewalls, DNS, and TCP/IP.");
    var second = CheapTriageClassifier.Classify("Network Engineer",
        "Configure Cisco routing, switching, firewalls, DNS, and TCP/IP.");
    Assert(first.SendToJobFit == second.SendToJobFit &&
           first.RejectionReason == second.RejectionReason &&
           first.TechnicalEvidence.Select(item => item.Bucket).SequenceEqual(
               second.TechnicalEvidence.Select(item => item.Bucket)) &&
           CheapTriageClassifier.CandidateFingerprint.Length == 64,
        "Triage decisions or candidate identity were not deterministic.");
    return Task.CompletedTask;
}
