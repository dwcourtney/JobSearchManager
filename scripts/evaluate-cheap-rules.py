#!/usr/bin/env python3
"""Compare versioned cheap rules through the actual C# engine; no models or holdout access."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_rows(path):
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if len({r["id"] for r in rows}) != len(rows):
        raise ValueError(f"Duplicate posting identifiers: {path}")
    return rows


def metrics(rows, decisions):
    binary = [r for r in rows if r.get("label") in ("KEEP", "REJECT")]
    keep = [r for r in binary if r["label"] == "KEEP"]
    rejects = [r for r in rows if decisions[r["id"]]["decision"] == "REJECT"]
    false = [r for r in keep if decisions[r["id"]]["decision"] == "REJECT"]
    labeled_rejects = [r for r in rejects if r.get("label") in ("KEEP", "REJECT")]
    return {"n": len(rows), "keepCount": len(keep), "keepRecall": (len(keep)-len(false))/len(keep) if keep else None,
            "falseRejectCount": len(false), "rejectionCount": len(rejects), "rejectionRate": len(rejects)/len(rows) if rows else None,
            "rejectPrecision": sum(r["label"] == "REJECT" for r in labeled_rejects)/len(labeled_rejects) if labeled_rejects else None,
            "falseRejects": [{"id": r["id"], "title": r["title"], "label": r["label"], "decision": decisions[r["id"]]} for r in false]}


def run(repo, dll, rules, inputs):
    outputs = {}; elapsed = 0
    for path in inputs:
        start = time.perf_counter()
        proc = subprocess.run(["dotnet", str(dll), "--cheap-triage", "evaluate", str(rules), str(path)], cwd=repo, check=True, capture_output=True, encoding="utf-8")
        elapsed += time.perf_counter()-start
        for line in proc.stdout.splitlines():
            row = json.loads(line)
            if row["id"] in outputs:
                raise ValueError("Duplicate ID across inputs")
            outputs[row["id"]] = row["result"]
    return outputs, elapsed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--baseline", type=Path, default=Path("CheapTriage/rulesets/1.0.0.json"))
    parser.add_argument("--candidate", type=Path, help="New ruleset path. Never changes the active configuration.")
    parser.add_argument("--output", type=Path, required=True, help="New report directory outside preserved research")
    parser.add_argument("--require-frozen-parity", action="store_true", help="Require baseline agreement with every frozen electrical-safe decision")
    args = parser.parse_args()
    repo = args.repo.resolve(); out = args.output.resolve()
    research = repo / "Tests/cheap_reject_evaluation"
    if out.is_relative_to(research.resolve()):
        parser.error("Do not overwrite preserved research; use a separate report directory")
    source = research / "supervised_v2"
    inputs = [source / name for name in ("corpus.jsonl", "cache.jsonl")]
    split = json.loads((source / "splits.json").read_text())
    for path, key in zip(inputs, ("corpusSha256", "cacheSha256")):
        if digest(path) != split[key]:
            raise ValueError(f"Frozen input hash mismatch: {path}")
    corpus, cache = [read_rows(p) for p in inputs]
    allrows = corpus+cache
    dll = repo / "research/cheap-triage/bin/Release/net10.0/Jsm.CheapTriage.Research.dll"
    baseline = (repo / args.baseline).resolve()
    base, elapsed = run(repo, dll, baseline, inputs)
    if set(base) != {r["id"] for r in allrows}:
        raise ValueError("Missing/unexpected decisions")
    frozen = json.loads((source / "baseline.json").read_text())["decisions"]
    mismatches = [{"id": r["id"], "title": r["title"], "frozen": frozen[r["id"]], "actual": base[r["id"]]} for r in allrows if frozen[r["id"]]["keep"] != (base[r["id"]]["decision"] == "KEEP")]
    runs = [("baseline", baseline, base, elapsed)]
    if args.candidate:
        candidate = (repo / args.candidate).resolve(); decisions, elapsed = run(repo, dll, candidate, inputs)
        runs.append(("candidate", candidate, decisions, elapsed))
    report = {"schemaVersion": 1, "labelWarning": "Provisional Codex labels, title-only evidence weak; unlabeled cache has no accuracy claim. No holdout used.",
              "sourceHashes": {p.name: digest(p) for p in inputs+[source / "splits.json", source / "baseline.json"]}, "frozenParityMismatches": mismatches, "runs": {}}
    out.mkdir(parents=True, exist_ok=True)
    for name, path, decisions, seconds in runs:
        binary = [r for r in corpus if r["label"] in ("KEEP", "REJECT")]
        cohorts = {"binary": binary, "described": [r for r in binary if r["body"].strip()], "titleOnly": [r for r in binary if not r["body"].strip()], "ambiguous": [r for r in corpus if r["label"] == "AMBIGUOUS"], "oldCache": cache}
        result = {"rulesetVersion": json.loads(path.read_text())["rulesetVersion"], "rulesetSha256": digest(path), "wallSecondsIncludingStartupAndDiagnostics": seconds,
                  "postingsPerSecondIncludingStartupAndDiagnostics": len(allrows)/seconds, "cohorts": {k: metrics(rows, decisions) for k, rows in cohorts.items()},
                  "failOpenCount": sum(d["failOpen"] for d in decisions.values()),
                  "changedDecisions": [{"id": r["id"], "title": r["title"], "label": r.get("label"), "before": base[r["id"]], "after": decisions[r["id"]]} for r in allrows if base[r["id"]]["decision"] != decisions[r["id"]]["decision"]]}
        report["runs"][name] = result
        with (out / f"{name}-decisions.jsonl").open("w", encoding="utf-8", newline="\n") as f:
            for r in allrows:
                f.write(json.dumps({"id": r["id"], "result": decisions[r["id"]]}, ensure_ascii=False)+"\n")
    legacy_paths = [research / name for name in ("development.jsonl", "leakage-audit.jsonl", "leakage-delta-audit.jsonl", "fixtures.jsonl")]
    for legacy_path in legacy_paths:
        rows = read_rows(legacy_path)
        report["sourceHashes"][legacy_path.name] = digest(legacy_path)
        previous = None
        for name, path, _, _ in runs:
            decisions, _ = run(repo, dll, path, [legacy_path])
            cohort = metrics(rows, decisions)
            cohort["changedDecisions"] = [] if previous is None else [{"id": r["id"], "title": r["title"], "label": r.get("label"), "before": previous[r["id"]], "after": decisions[r["id"]]} for r in rows if previous[r["id"]]["decision"] != decisions[r["id"]]["decision"]]
            report["runs"][name]["cohorts"][legacy_path.stem] = cohort
            previous = decisions
    if args.candidate:
        b, c = (report["runs"][n]["cohorts"] for n in ("baseline", "candidate"))
        report["candidateSafetyRegression"] = any(c[k]["falseRejectCount"] > b[k]["falseRejectCount"] for k in ("binary", "described", "titleOnly", "development", "leakage-audit", "leakage-delta-audit", "fixtures"))
    (out / "comparison.json").write_text(json.dumps(report, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    print(json.dumps({"frozenParityMismatches": len(mismatches), "runs": {k: {c: {m: v for m,v in x.items() if m != "falseRejects"} for c,x in r["cohorts"].items()} for k,r in report["runs"].items()}}, indent=2))
    if args.require_frozen_parity and mismatches:
        raise SystemExit("FAIL: electrical-safe parity differs; inspect comparison.json")
    if report.get("candidateSafetyRegression"):
        raise SystemExit("FAIL: candidate increases false rejects; do not activate")


if __name__ == "__main__":
    main()
