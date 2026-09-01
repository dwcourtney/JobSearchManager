#!/usr/bin/env python3
"""Run the fixed Phase 3 LLM benchmark and separate qualitative probes."""
import argparse, json, math, statistics, time, urllib.request
from pathlib import Path

CONCEPTS = ["role.ai-ml-engineering", "role.software-engineering",
    "technical.software-development", "technical.backend-development",
    "technical.api-development", "technical.automation-scripting",
    "role.cloud-engineering", "technical.containers"]
MODEL_DIGEST = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"
HISTORICAL = {
    "distilRoBERTa": {"macroF1": .642452654, "microF1": .673123487},
    "deBERTa": {"macroF1": .7955379526291256, "microF1": .8184281842818428},
    "bgeEmbeddings": {"macroF1": .5742, "microF1": .5951, "bestThreshold": .50,
                      "hardNegativeOvermatches": 6}}

def ratio(numerator, denominator): return numerator / denominator if denominator else None
def f1(precision, recall):
    return None if precision is None or recall is None else (0 if precision + recall == 0 else 2 * precision * recall / (precision + recall))
def aggregate(metrics):
    defined = lambda key: [item[key] for item in metrics if item[key] is not None]
    tp, fp, fn = (sum(item[key] for item in metrics) for key in ("truePositive", "falsePositive", "falseNegative"))
    mp, mr = ratio(tp, tp + fp), ratio(tp, tp + fn)
    return {"macro": {key: statistics.mean(defined(key)) if defined(key) else None for key in ("precision", "recall", "f1")},
            "micro": {"precision": mp, "recall": mr, "f1": f1(mp, mr)}}
def post(url, case):
    payload = {"jobId": case["id"], "title": case["title"], "description": case["excerpt"]}
    request = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json"})
    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=300) as response: result = json.load(response)
    return result, (time.perf_counter() - started) * 1000
def validate(response, sha, prompt_version, prompt_hash):
    if response.get("revision") != sha or response.get("device") != "cuda:0" or response.get("deviceCount") != 1 or response.get("deviceName") != "NVIDIA GeForce GTX 1070":
        raise SystemExit("Identity/GPU validation failed")
    if (response.get("modelType") != "generative-llm" or response.get("modelDigest") != MODEL_DIGEST
            or response.get("temperature") != 0 or response.get("promptVersion") != prompt_version
            or response.get("promptHash") != prompt_hash):
        raise SystemExit("Pinned LLM/prompt contract validation failed")
    predictions = response.get("predictions")
    if not isinstance(predictions, list) or len(predictions) != 8:
        raise SystemExit("Unexpected LLM prediction schema")
    values = {item.get("conceptId"): item.get("matched") for item in predictions}
    if set(values) != set(CONCEPTS) or any(type(value) is not bool for value in values.values()):
        raise SystemExit("Unexpected LLM prediction values")
    return values
def evaluate(cases, url, sha, prompt_version, prompt_hash):
    observations, round_trips, malformed = [], [], 0
    for case in cases:
        try:
            response, elapsed = post(url, case); actual = validate(
                response, sha, prompt_version, prompt_hash)
        except (ValueError, KeyError, json.JSONDecodeError):
            malformed += 1; raise
        expected_set = set(case.get("expectedPresentConceptIds", []))
        observations.append({"fixtureId": case["id"], "source": case.get("source"),
            "expected": {key: key in expected_set for key in CONCEPTS}, "actual": actual,
            "correct": all(actual[key] == (key in expected_set) for key in CONCEPTS),
            "inferenceMilliseconds": response["inferenceMilliseconds"],
            "promptTokenCount": response.get("promptTokenCount"),
            "outputTokenCount": response.get("outputTokenCount"),
            "tokensPerSecond": response.get("tokensPerSecond"),
            "loadDurationNanoseconds": response.get("loadDurationNanoseconds")})
        round_trips.append(elapsed)
    return observations, round_trips, malformed, response
def metrics(observations):
    result = []
    for concept in CONCEPTS:
        tp=fp=fn=tn=0
        for item in observations:
            expected, actual = item["expected"][concept], item["actual"][concept]
            tp += expected and actual; fp += not expected and actual
            fn += expected and not actual; tn += not expected and not actual
        precision, recall = ratio(tp, tp+fp), ratio(tp, tp+fn)
        result.append({"conceptId": concept, "truePositive": tp, "falsePositive": fp,
            "falseNegative": fn, "trueNegative": tn, "precision": precision,
            "recall": recall, "f1": f1(precision, recall)})
    return result

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", required=True); parser.add_argument("--fixtures", required=True)
    parser.add_argument("--regex-report", required=True); parser.add_argument("--qualitative", required=True)
    parser.add_argument("--sha", required=True); parser.add_argument("--output", required=True)
    parser.add_argument("--prompt-version", required=True); parser.add_argument("--prompt-hash", required=True)
    args = parser.parse_args()
    if len(args.sha) != 40 or any(c not in "0123456789abcdef" for c in args.sha): raise SystemExit("Full lowercase SHA required")
    if len(args.prompt_hash) != 64 or any(c not in "0123456789abcdef" for c in args.prompt_hash): raise SystemExit("Full lowercase prompt hash required")
    document = json.loads(Path(args.fixtures).read_text(encoding="utf-8"))
    cases = [item for item in document["fixtures"] if item.get("labelScope") == "tier1-target"]
    if len(cases) != 40: raise SystemExit(f"Expected 40 scoped fixtures; got {len(cases)}")
    url = args.url.rstrip("/") + "/classify-llm"
    observations, round_trips, malformed, response = evaluate(
        cases, url, args.sha, args.prompt_version, args.prompt_hash)
    concept_metrics = metrics(observations); llm = {**aggregate(concept_metrics), "concepts": concept_metrics}
    qualitative = json.loads(Path(args.qualitative).read_text(encoding="utf-8"))
    hard, hard_rt, hard_bad, _ = evaluate(
        qualitative["hardNegatives"], url, args.sha, args.prompt_version, args.prompt_hash)
    general, general_rt, general_bad, _ = evaluate(
        qualitative["generalization"], url, args.sha, args.prompt_version, args.prompt_hash)
    regex = json.loads(Path(args.regex_report).read_text(encoding="utf-8"))
    regex_metrics = [item for item in regex["concepts"] if item["conceptId"] in CONCEPTS]
    inference = [item["inferenceMilliseconds"] for item in observations]
    numeric = lambda key: [item[key] for item in observations if isinstance(item[key], (int, float))]
    result = {"schemaVersion": 3, "candidateSha": args.sha, "fixtureVersion": document["version"],
        "fixtureCount": len(cases), "labelCount": len(cases) * len(CONCEPTS),
        "model": {key: response[key] for key in ("modelType", "modelId", "modelTag", "modelDigest", "quantization", "ollamaVersion", "contextLength")},
        "prompt": {key: response[key] for key in ("promptVersion", "promptHash", "temperature", "seed", "maxOutputTokens")},
        "malformedOutputCount": malformed + hard_bad + general_bad,
        "latency": {"averageInferenceMilliseconds": statistics.mean(inference),
            "p95InferenceMilliseconds": sorted(inference)[math.ceil(.95 * len(inference)) - 1],
            "averageRoundTripMilliseconds": statistics.mean(round_trips),
            "averageTokensPerSecond": statistics.mean(numeric("tokensPerSecond")),
            "averagePromptTokenCount": statistics.mean(numeric("promptTokenCount")),
            "averageOutputTokenCount": statistics.mean(numeric("outputTokenCount")),
            "maximumLoadDurationNanoseconds": max(numeric("loadDurationNanoseconds"), default=None)},
        "llm": llm, "regex": {**aggregate(regex_metrics), "concepts": regex_metrics},
        "historical": HISTORICAL, "hardNegatives": hard, "generalization": general,
        "qualitativeRoundTripMilliseconds": hard_rt + general_rt, "observations": observations}
    Path(args.output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"candidateSha": args.sha, "model": result["model"], "latency": result["latency"],
        "regex": result["regex"], "llm": llm, "hardNegatives": hard,
        "generalization": general, "malformedOutputCount": result["malformedOutputCount"]}, separators=(",", ":")))

if __name__ == "__main__": main()
