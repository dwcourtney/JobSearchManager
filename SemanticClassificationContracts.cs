namespace JobSearchManager;

public sealed record SemanticConceptPrediction(string ConceptId, bool Matched, double? Score = null);
public sealed record ConceptDiagnosticRequest(string JobId, string Title, string Description);
