using System.Text.Json;
using JobSearchManager;

namespace JobSearchManager.Migration;

internal static class JsonConceptPresentation
{
    internal static void Compare(string input,string output)
    {
        using var document=JsonDocument.Parse(File.ReadAllBytes(input));
        var detail=document.RootElement.GetProperty("detail").Deserialize<JobRecord>(JsonConceptTests.Json)!;
        var catalog=JobConceptCatalog.LoadDefault();var candidate=JsonConceptCandidate.Load(JsonConceptTests.CandidatePath,catalog);
        var result=candidate.Classify(detail.Title,detail.DescriptionHtml,detail.RemoteWork,detail.ExtendedLocationRequirement);
        var matched=result.Concepts.Select(c=>c.ConceptId).ToHashSet(StringComparer.Ordinal);
        var predictions=catalog.Concepts.Select(c=>new SemanticConceptPrediction(c.Id,matched.Contains(c.Id))).ToArray();
        JsonConceptTests.Require(JsonConceptTests.Serialize(detail.SemanticClassification!.Predictions)==JsonConceptTests.Serialize(predictions),"Runtime JSON/SQLite predictions differ");
        var jsonJob=detail with{SemanticClassification=detail.SemanticClassification with{
            Predictions=predictions,ModelDigest=candidate.PipelineFingerprint,
            ClassifierConfigurationVersion="json-candidate-v1",
            ClassifierConfigurationFingerprint=candidate.PipelineFingerprint,
            ClassificationFingerprint=SemanticRulesetFingerprint.ClassificationFingerprint(result.PostingContentHash,candidate.PipelineFingerprint,catalog.Fingerprint)}};
        var oldList=JobListItem.FromJob(detail);var jsonList=JobListItem.FromJob(jsonJob);
        JsonConceptTests.Require(JsonConceptTests.Serialize(oldList)==JsonConceptTests.Serialize(jsonList),"Runtime Jobs list DTO differs");
        var oldDetail=JobPresentation.AuthoritativeRegexDetail(detail);var jsonDetail=JobPresentation.AuthoritativeRegexDetail(jsonJob);
        JsonConceptTests.Require(JsonConceptTests.Serialize(oldDetail)==JsonConceptTests.Serialize(jsonDetail with{SemanticClassification=oldDetail.SemanticClassification}),"Runtime detail DTO differs beyond candidate identity");
        JsonConceptTests.Require(JsonConceptTests.Serialize(jsonList.DetectedConcepts)==JsonConceptTests.Serialize(jsonDetail.DetectedConcepts),"JSON list/detail concepts differ");
        File.WriteAllText(output,JsonConceptTests.Serialize(new[]{new{Id="isolated-runtime-list-detail",Old=oldList.DetectedConcepts,Current=jsonList.DetectedConcepts}}));
        File.WriteAllText(output+".identity.json",JsonConceptTests.Serialize(new{Candidate=candidate.Identity,ListDtoExact=true,DetailDtoExactExceptIntentionalIdentity=true,PredictionsExact=85,AccountsOrDataWritten=false}));
        Console.WriteLine("PASS actual runtime Jobs/list/detail DTO and 85-prediction parity; candidate identity remains separate");
    }
}
