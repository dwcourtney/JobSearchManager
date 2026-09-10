using System.Text.Json;
using JobSearchManager;

internal static class FactualCompletionTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static Task RunAsync()
    {
        var parser = FactualDomainObservations.Default;
        string[] cases = [
            "Active Secret required; ability to obtain Secret after hire; TS/SCI preferred before start.",
            "Secret OR TS acceptable.", "No clearance required.", "Public Trust required.",
            "Bachelor's required; Master's preferred.", "Bachelor's OR 4 years experience.",
            "Master's + 2 years OR Bachelor's + 6 years.", "degree in CS/Engineering preferred.", "no degree required.",
            "<h3>Required qualifications</h3><p>Bachelor's degree.</p><h3>Preferred qualifications</h3><p>Bachelor's degree.</p>",
            "CISSP required; CISSP preferred.", "PMP required within 6 months after hire.",
            "PE license OR equivalent registration.", "CCNA desired; active driver's license required.",
            "Must hold Example Alpha certification; Must hold Example Beta certification.",
            "fully remote", "hybrid, 3 days onsite", "remote but quarterly travel required",
            "remote eligible after training; onsite training required.", "occasional office attendance.",
            "Travel up to 25%; travel 10-20%.",
            "Deploy to Germany for 3 months; 90-day rotations in Kazakhstan.",
            "temporary assignment at customer site; six-month domestic assignment; OCONUS travel possible; long-term assignment required; winter-over assignment.",
            "must reside in Florida; remote from approved states only; not available in California; within 50 miles of office; Eastern/Central time zones only; US-based only; must live in CONUS; candidates in X/Y/Z states excluded.",
            "relocation not required; relocation assistance available."
        ];
        foreach (var html in cases)
        {
            var documents = parser.Observe(html, "fixture-provider");
            Check(documents.Count == 6, "Missing domain.");
            Check(JsonSerializer.Serialize(documents, FactObservations.Json) == JsonSerializer.Serialize(parser.Observe(html, "fixture-provider"), FactObservations.Json), "Observation identities not deterministic.");
            foreach (var document in documents.Values)
            {
                var ids = document.Observations.Select(o => o.Id).ToHashSet();
                Check(ids.Count == document.Observations.Count, "Duplicate observation IDs.");
                foreach (var o in document.Observations)
                {
                    Check(o.Evidence.Start >= 0 && o.Evidence.Start + o.Evidence.Length <= o.Evidence.Text.Length, "Invalid evidence offset.");
                    Check(o.Relations.All(r => ids.Contains(r.OtherObservationId)), "Dangling relation.");
                    Check(o.Source == "posting-body" && o.Provider == "fixture-provider", "Lost source/provider.");
                }
            }
            Check(JsonSerializer.Serialize(JobAnalysis.AnalyzeClearance(html)) == JsonSerializer.Serialize(LegacyClearanceBaseline.AnalyzeClearance(html)), "Clearance parity changed.");
            Check(JsonSerializer.Serialize(JobAnalysis.AnalyzeRemoteLocation(html, "Remote", [])) == JsonSerializer.Serialize(LegacyGeographicRestrictionBaseline.AnalyzeRemoteLocation(html, "Remote", [])), "Geographic parity changed.");
        }
        FactualObservation[] At(int i,string d,string t) => parser.Observe(cases[i])[d].Observations.Where(o=>o.Type==t).ToArray();
        Check(At(0,"clearance","level").Length==3,"Clearance levels lost.");
        Check(At(0,"clearance","level")[0].Obligation=="required" && At(0,"clearance","level")[2].Obligation=="preferred","Clearance obligations conflated.");
        Check(At(0,"clearance","level")[1].Scope.Temporal=="after hire" && At(0,"clearance","level")[2].Scope.Temporal=="before start","Clearance timing lost.");
        Check(At(1,"clearance","path").All(o=>o.Relations.Any(r=>r.Kind=="alternative-to")),"Clearance alternative links lost.");
        Check(At(2,"clearance","absence").Length==1,"Clearance negation lost.");
        Check(At(6,"education","experience").Select(o=>o.Value.Lower).SequenceEqual(new decimal?[]{2,6}),"Path-specific experience lost.");
        Check(At(9,"education","degree").Select(o=>o.Scope.Section).Distinct().Count()==2,"Repeated qualification sections collapsed.");
        Check(At(10,"credentials","credential").Select(o=>o.Obligation).Distinct().Count()==2,"Same credential obligations merged.");
        Check(At(11,"credentials","credential").Any(o=>o.Scope.Temporal=="within 6 months"),"Credential timing lost.");
        Check(At(14,"credentials","unresolved-credential").Length==2,"Unknown credentials lost.");
        Check(At(16,"remote-work","cadence").Any(o=>o.Value.Lower==3),"Office cadence lost.");
        Check(At(20,"remote-work","travel-percentage").Length==2,"Travel bounds lost.");
        Check(At(21,"extended-location","destination").Length==2,"Assignment destinations lost.");
        Check(At(21,"extended-location","duration").Select(o=>o.Value.Lower).SequenceEqual(new decimal?[]{3,90}),"Assignment durations lost.");
        Check(At(23,"geographic-restriction","excluded-jurisdiction").Length==2,"Exclusions lost.");
        Check(At(23,"geographic-restriction","radius").Single().Value.Upper==50,"Radius lost.");
        var separate = parser.Observe("CISSP required and Security+ preferred.")["credentials"].Observations.Where(o=>o.Type=="credential").ToArray();
        Check(separate.Any(o=>o.Value.Raw=="CISSP" && o.Obligation=="required") && separate.Any(o=>o.Value.Raw=="Security+" && o.Obligation=="preferred"), "Credential clause obligations cross-contaminated.");
        var fieldScoped=parser.Observe("Bachelor's degree required in Computer Science preferred.")["education"].Observations;
        Check(fieldScoped.Any(o=>o.Type=="degree" && o.Obligation=="required") && fieldScoped.Any(o=>o.Type=="field" && o.Obligation=="preferred"),"Field preference contaminated degree obligation.");
        var compound=parser.Observe("Master's + 2 years OR Bachelor's + 6 years.")["education"].Observations;
        Check(compound.Count(o=>o.Type=="path" && o.LogicalConnective=="and")==2 && compound.Any(o=>o.Type=="statement" && o.LogicalConnective=="or"),"Nested AND/OR paths flattened.");
        var fieldAlternatives=parser.Observe("Bachelor's degree in CS or Engineering preferred.")["education"].Observations;
        Check(fieldAlternatives.Count(o=>o.Type=="path")==1 && fieldAlternatives.Any(o=>o.Type=="field" && o.Value.Alternatives?.SequenceEqual(new[]{"CS","Engineering"})==true),"Field alternatives became degree alternatives.");
        var jurisdictionAlternatives=parser.Observe("Must reside in Florida or Georgia.")["geographic-restriction"].Observations;
        Check(jurisdictionAlternatives.Any(o=>o.Type=="included-jurisdiction" && o.Value.Alternatives?.SequenceEqual(new[]{"Florida","Georgia"})==true),"Jurisdiction alternatives lost their predicate.");
        var headings=parser.Observe("<h3>Preferred qualifications</h3><p>Master's degree.</p><h3>Responsibilities</h3><p>Bachelor's degree mentioned.</p>")["education"].Observations.Where(o=>o.Type=="degree").ToArray();
        Check(headings[0].Obligation=="preferred" && headings[1].Obligation=="unknown" && headings[1].Scope.Section=="Responsibilities","Heading scope leaked into another section.");
        var noCredential=parser.Observe("No CISSP required.")["credentials"].Observations.Where(o=>o.Type=="credential").ToArray();
        Check(noCredential.All(o=>o.Obligation=="not-required"),"Negation prefix was dropped from credential scope.");
        var genericClearance=parser.Observe("Clearance required before start.")["clearance"].Observations;
        Check(genericClearance.Any(o=>o.Type=="clearance" && o.Obligation=="required" && o.Scope.Temporal=="before start"),"Unspecified clearance with timing lost.");
        var intervals=parser.Observe("Assignment in Germany for three to six consecutive months.")["extended-location"].Observations;
        Check(intervals.Any(o=>o.Type=="duration" && o.Value.Lower==3 && o.Value.Upper==6 && o.Qualifier=="consecutive"),"Exact duration interval or qualifier lost.");
        var deadline=At(11,"credentials","deadline").Single();
        Check(deadline.Value.Lower is null && deadline.Value.Upper==6 && deadline.Value.Unit=="months","Obtain-by deadline lost its bound/unit.");
        var roleScope=parser.Observe("<p>Level 3:</p><p>Master's degree required.</p><p>Level 4:</p><p>Bachelor's degree required.</p>")["education"].Observations.Where(o=>o.Type=="degree").ToArray();
        Check(roleScope.Select(o=>o.Scope.JobLevel).SequenceEqual(new[]{"Level 3","Level 4"}),"Role-level scope lost.");
        var bounded = At(20,"remote-work","travel-percentage")[0];
        Check(bounded.Value.Kind=="upper-bound" && bounded.Value.Lower is null && bounded.Value.Upper==25,"Up-to travel invented an exact amount.");
        Check(At(1,"clearance","level").All(o=>o.Obligation!="preferred"),"Acceptable misrepresented as preferred.");
        var providerConflict=parser.Observe("Must work onsite.", "fixture", new Dictionary<string,string>{{"primaryLocation","Remote"}})["remote-work"].Observations;
        Check(providerConflict.Any(o=>o.Source=="provider-metadata") && providerConflict.Any(o=>o.Source=="posting-body" && o.Type=="arrangement") && providerConflict.Any(o=>o.Relations.Any(r=>r.Kind=="source-comparison-review-scope")),"Provider/body distinction lost.");
        Check(At(21,"extended-location","scoped-duration").Any(o=>o.Scope.Geography=="Germany" && o.Value.Lower==3),"Destination and duration pairing lost.");
        var manyDegrees=string.Concat(Enumerable.Repeat("<p>Bachelor's degree required.</p>",20));
        var academic=new AcademicQualificationDetector();
        Check(academic.Extract(manyDegrees).Paths.Length==20 && parser.Observe(manyDegrees)["education"].Observations.Count(o=>o.Type=="degree")==20,"Repeated degree candidates were capped/merged.");
        var unknowns=string.Concat(Enumerable.Range(0,20).Select(i=>$"<p>Must hold Example{(char)('A'+i)} certification.</p>"));
        var credentialDetector=new CredentialDetector(Microsoft.Extensions.Logging.Abstractions.NullLogger<CredentialDetector>.Instance);
        Check(credentialDetector.Extract(unknowns).Unknown.Length==20 && credentialDetector.Analyze(unknowns).UnknownRequirements.Count==10,"Unknown-candidate retention changed legacy cap.");
        Check(parser.Observe(unknowns)["credentials"].Observations.Count(o=>o.Type=="unresolved-credential")==20,"Unknown credential observations capped.");
        var travel=string.Concat(Enumerable.Range(1,6).Select(i=>$"<p>Travel {i*10}% required.</p>"));
        var remoteDetector=new RemoteWorkDetector();var travelTrace=remoteDetector.Extract("","Remote",[],travel);
        Check(travelTrace.Candidates.Length==6 && remoteDetector.Summarize(travelTrace).Signals.Count==4,"Travel trace did not retain candidates before legacy cap.");
        var assignments=string.Concat(new[]{"Germany","Japan","Guam","Antarctica","Kazakhstan","Diego Garcia"}.Select(country=>$"<p>Employee will be forward deployed to {country} for 90 days.</p>"));
        var extendedDetector=new ExtendedLocationRequirementDetector();var assignmentTrace=extendedDetector.Extract("","Remote",[],assignments);
        Check(assignmentTrace.Candidates.Length>5 && extendedDetector.Summarize(assignmentTrace).Signals.Count==5,"Assignment trace did not retain candidates before legacy cap.");
        using var inspected=JsonDocument.Parse(JsonSerializer.Serialize(FactualCompletionInspection.Run("Must work onsite.","fixture",null,null,"",["Remote"]),FactObservations.Json));
        Check(inspected.RootElement.EnumerateObject().Count()==8 && inspected.RootElement.GetProperty("remote-work").GetProperty("legacySummary").GetProperty("isRemoteDesignated").GetBoolean(),"Eight-domain inspection dropped provider inputs.");
        var json=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"rules","factual-observation-completion-v1.json"));
        foreach(var invalid in new[]{"{}",json.Replace("\"timeoutMilliseconds\": 250","\"timeoutMilliseconds\": 0"),json.Replace("\"domain\": \"clearance\"","\"domain\": \"invalid\"")})
        {
            var rejected=false;try { _=new FactualDomainObservations(invalid); } catch(Exception ex) when(ex is JsonException or InvalidDataException or ArgumentException) { rejected=true; }
            Check(rejected,"Invalid observation rules accepted.");
        }
        Console.WriteLine($"Six-domain observations: {cases.Length} adversarial fixtures; source/coordinates/relationships deterministic.");
        return Task.CompletedTask;
    }
}
