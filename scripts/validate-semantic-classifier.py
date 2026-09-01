#!/usr/bin/env python3
"""Run the bounded, taxonomy-wide semantic acceptance set against a classifier."""
import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path


def post_json(url: str, payload: dict) -> dict:
    request = urllib.request.Request(
        url,
        json.dumps(payload, separators=(",", ":")).encode(),
        {"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=180) as response:
        return json.load(response)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", required=True, help="Classifier base URL")
    parser.add_argument("--fixtures", default=str(
        Path(__file__).resolve().parents[1] / "SemanticClassifierValidationFixtures.json"))
    parser.add_argument("--minimum-accuracy", type=float, default=0.90)
    parser.add_argument("--maximum-false-positive-rate", type=float, default=0.05)
    args = parser.parse_args()
    fixture_document = json.loads(Path(args.fixtures).read_text(encoding="utf-8"))
    failures = []
    checks = 0
    false_negatives = 0
    false_positives = 0
    negative_checks = 0
    referenced_ids = set()
    for case in fixture_document["cases"]:
        try:
            result = post_json(args.url.rstrip("/") + "/classify", {
                "jobId": case["id"],
                "title": case["title"],
                "description": case["description"],
            })
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as error:
            print(f"FAIL {case['id']}: request failed: {error}")
            return 2
        predictions = {item["conceptId"]: item["matched"] for item in result["predictions"]}
        case_failures = []
        for concept_id in case["expectedTrue"]:
            checks += 1
            referenced_ids.add(concept_id)
            if predictions.get(concept_id) is not True:
                false_negatives += 1
                case_failures.append(f"expected true: {concept_id}")
        for concept_id in case["expectedFalse"]:
            checks += 1
            negative_checks += 1
            referenced_ids.add(concept_id)
            if predictions.get(concept_id) is not False:
                false_positives += 1
                case_failures.append(f"expected false: {concept_id}")
        if result.get("conceptCount") != 85 or len(predictions) != 85:
            case_failures.append("response did not contain exactly 85 unique concepts")
        if case_failures:
            failures.append((case["id"], case_failures))
            print(f"FAIL {case['id']}: {'; '.join(case_failures)}")
        else:
            print(f"PASS {case['id']}")
    accuracy = (checks - false_negatives - false_positives) / checks
    false_positive_rate = false_positives / negative_checks
    accepted = (len(referenced_ids) == 85 and accuracy >= args.minimum_accuracy and
                false_positive_rate <= args.maximum_false_positive_rate)
    print(f"Semantic acceptance: {len(fixture_document['cases'])} cases, {checks} labeled checks, "
          f"{len(referenced_ids)} canonical concepts referenced, accuracy={accuracy:.3%}, "
          f"false-positive-rate={false_positive_rate:.3%}, {len(failures)} cases with mismatches.")
    print("ACCEPT" if accepted else "REJECT")
    return 0 if accepted else 1


if __name__ == "__main__":
    sys.exit(main())
