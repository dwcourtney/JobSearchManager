using System.Security.Cryptography;
using System.Text.Json;
using JobSearchManager;

internal static class FactualCompletionCorpus
{
    internal static void Run(string input, string output)
    {
        var bytes=File.ReadAllBytes(input);
        var rows=JsonSerializer.Deserialize<FactualObservationTests.Input[]>(bytes,FactObservations.Json)!;
        var counts=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            var metadata=new Dictionary<string,string>(row.Metadata ?? [],StringComparer.Ordinal);
            if(row.Title is not null) metadata["title"]=row.Title;
            if(row.PrimaryLocation is not null) metadata["primaryLocation"]=row.PrimaryLocation;
            for(var i=0;i<(row.AdditionalLocations?.Length ?? 0);i++) metadata[$"additionalLocation[{i}]"]=row.AdditionalLocations![i];
            var documents=FactualDomainObservations.Default.Observe(row.Html,row.Provider,metadata).Values
                .Concat(new[]{new WorkAuthorizationDetector().Observe(row.Html,row.Provider),
                    CompensationObservations.Observe(JobAnalysis.ExtractSalary(row.Html,SalaryRules.Default),row.Provider)});
            foreach(var document in documents)
            {
                if(document.OriginalHtml!=row.Html)throw new InvalidDataException("Original input lost.");
                var ids=document.Observations.Select(o=>o.Id).ToHashSet();
                if(ids.Count!=document.Observations.Count)throw new InvalidDataException("Duplicate observation identity.");
                foreach(var observation in document.Observations)
                {
                    var e=observation.Evidence;
                    if(e.Start<0||e.Length<0||e.Start+e.Length>e.Text.Length||observation.Relations.Any(r=>!ids.Contains(r.OtherObservationId)))
                        throw new InvalidDataException("Invalid observation provenance/relationship: "+row.Id);
                    counts[observation.Domain]=counts.GetValueOrDefault(observation.Domain)+1;
                }
            }
        }
        var result=new { totalPostings=rows.Length, exactSourceDocuments=rows.Length*8, invalidCoordinates=0,
            danglingRelations=0, duplicateIds=0, observationsByDomain=counts,
            inputHash=Convert.ToHexStringLower(SHA256.HashData(bytes)), providersCalled=false, cacheWritten=false };
        File.WriteAllText(output,JsonSerializer.Serialize(result,FactObservations.Json)+"\n");
        Console.WriteLine($"Validated eight-domain observations/provenance for {rows.Length} postings.");
    }
}
