#!/usr/bin/env python3
"""Run the fixed Phase 2C embedding benchmark against an isolated classifier endpoint."""
import argparse, json, statistics, time, urllib.request
from pathlib import Path

CONCEPTS = ["role.ai-ml-engineering", "role.software-engineering",
    "technical.software-development", "technical.backend-development",
    "technical.api-development", "technical.automation-scripting",
    "role.cloud-engineering", "technical.containers"]
THRESHOLDS = (.50, .55, .60, .65, .70, .75, .80, .85, .90)
HISTORICAL = {
    "distilRoBERTa": {"bestThreshold": .3, "macroF1": .642452654, "microF1": .673123487,
        "conceptF1": [.711111111, .677966102, .818181818, .565217391, .721311475, .8125, .277777778, .555555556]},
    "deBERTa": {"bestThreshold": .7, "macroF1": .7955379526291256, "microF1": .8184281842818428,
        "conceptF1": [1, .833333333, .885245902, .634146341, .916666667, .943396226, .333333333, .818181818]}}

def ratio(numerator, denominator): return numerator / denominator if denominator else None
def f1(precision, recall):
    return None if precision is None or recall is None else (0 if precision + recall == 0 else 2 * precision * recall / (precision + recall))
def aggregate(metrics):
    defined = lambda key: [value[key] for value in metrics if value[key] is not None]
    tp, fp, fn = (sum(value[key] for value in metrics) for key in ("truePositive", "falsePositive", "falseNegative"))
    mp, mr = ratio(tp, tp + fp), ratio(tp, tp + fn)
    return {"macro": {key: statistics.mean(defined(key)) if defined(key) else None for key in ("precision", "recall", "f1")},
            "micro": {"precision": mp, "recall": mr, "f1": f1(mp, mr)}}
def post(url, payload):
    request = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json"})
    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=120) as response: result = json.load(response)
    return result, (time.perf_counter() - started) * 1000

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", required=True); parser.add_argument("--fixtures", required=True)
    parser.add_argument("--regex-report", required=True); parser.add_argument("--sha", required=True)
    parser.add_argument("--output", required=True); args = parser.parse_args()
    if len(args.sha) != 40 or any(c not in "0123456789abcdef" for c in args.sha): raise SystemExit("Full lowercase SHA required")
    document = json.loads(Path(args.fixtures).read_text(encoding="utf-8"))
    cases = [item for item in document["fixtures"] if item.get("labelScope") == "tier1-target"]
    if len(cases) != 40: raise SystemExit(f"Expected 40 scoped fixtures; got {len(cases)}")
    observations, round_trips = [], []
    for case in cases:
        response, elapsed = post(args.url.rstrip("/") + "/classify-embedding",
            {"jobId": case["id"], "title": case["title"], "description": case["excerpt"]})
        if response.get("revision") != args.sha or response.get("device") != "cuda:0" or response.get("deviceCount") != 1 or response.get("deviceName") != "NVIDIA GeForce GTX 1070":
            raise SystemExit(f"Identity/GPU validation failed for {case['id']}")
        if response.get("modelType") != "embedding" or response.get("embeddingDimension") != 768 or response.get("aggregation") != "max":
            raise SystemExit(f"Embedding contract validation failed for {case['id']}")
        similarities = {item["conceptId"]: item["similarity"] for item in response["predictions"]}
        if set(similarities) != set(CONCEPTS) or any(not -1 <= value <= 1 for value in similarities.values()):
            raise SystemExit("Unexpected embedding prediction schema")
        present = set(case.get("expectedPresentConceptIds", []))
        observations.append({"fixtureId": case["id"], "expected": {c: c in present for c in CONCEPTS},
            "similarities": similarities, "inferenceMilliseconds": response["inferenceMilliseconds"],
            "tokenCount": response["tokenCount"], "chunkCount": response["chunkCount"]})
        round_trips.append(elapsed)
    threshold_reports = []
    for threshold in THRESHOLDS:
        metrics = []
        for concept in CONCEPTS:
            tp=fp=fn=tn=0
            for item in observations:
                expected, actual = item["expected"][concept], item["similarities"][concept] >= threshold
                tp += expected and actual; fp += not expected and actual; fn += expected and not actual; tn += not expected and not actual
            precision, recall = ratio(tp, tp+fp), ratio(tp, tp+fn)
            metrics.append({"conceptId": concept, "truePositive": tp, "falsePositive": fp,
                "falseNegative": fn, "trueNegative": tn, "precision": precision,
                "recall": recall, "f1": f1(precision, recall)})
        threshold_reports.append({"threshold": threshold, **aggregate(metrics), "concepts": metrics})
    best = max(threshold_reports, key=lambda value: (
        value["macro"]["f1"] if value["macro"]["f1"] is not None else -1.0,
        -value["threshold"]))
    regex = json.loads(Path(args.regex_report).read_text(encoding="utf-8"))
    regex_metrics = [item for item in regex["concepts"] if item["conceptId"] in CONCEPTS]
    historical = {name: {**value, "concepts": [
        {"conceptId": concept, "f1": score} for concept, score in zip(CONCEPTS, value["conceptF1"], strict=True)]}
        for name, value in HISTORICAL.items()}
    for value in historical.values(): del value["conceptF1"]
    result = {"schemaVersion": 2, "candidateSha": args.sha, "fixtureVersion": document["version"],
        "fixtureCount": len(cases), "labelCount": len(cases) * len(CONCEPTS),
        "modelType": response["modelType"], "modelId": response["modelId"],
        "modelRevision": response["modelRevision"], "embeddingDimension": response["embeddingDimension"],
        "conceptEmbeddingCacheKey": response["conceptEmbeddingCacheKey"],
        "conceptEmbeddingMemoryBytes": response["conceptEmbeddingMemoryBytes"],
        "conceptEmbeddingNormMin": response["conceptEmbeddingNormMin"],
        "conceptEmbeddingNormMax": response["conceptEmbeddingNormMax"],
        "modelLoadMilliseconds": response["modelLoadMilliseconds"],
        "conceptEmbeddingInitializationMilliseconds": response["conceptEmbeddingInitializationMilliseconds"],
        "aggregation": response["aggregation"],
        "runtime": {"python": "3.12", "pytorch": "2.6.0+cu126", "transformers": "5.16.1",
            "device": response["device"], "deviceName": response["deviceName"]},
        "latency": {"averageInferenceMilliseconds": statistics.mean(item["inferenceMilliseconds"] for item in observations),
            "p95InferenceMilliseconds": sorted(item["inferenceMilliseconds"] for item in observations)[37],
            "averageRoundTripMilliseconds": statistics.mean(round_trips)},
        "regex": {**aggregate([{"truePositive": item["truePositive"], "falsePositive": item["falsePositive"],
            "falseNegative": item["falseNegative"], "trueNegative": item["trueNegative"],
            "precision": item["precision"], "recall": item["recall"], "f1": item["f1"]} for item in regex_metrics]),
            "concepts": regex_metrics}, "historical": historical,
        "bestThreshold": best["threshold"], "thresholds": threshold_reports, "observations": observations}
    Path(args.output).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("candidateSha", "fixtureCount", "labelCount", "modelId", "modelRevision", "latency", "regex", "bestThreshold", "thresholds")}, separators=(",", ":")))

if __name__ == "__main__": main()
