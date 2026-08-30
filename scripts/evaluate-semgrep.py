#!/usr/bin/env python3
"""Apply JSM's conservative blocking policy to pinned Semgrep JSON output."""

from __future__ import annotations

import json
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: evaluate-semgrep.py <semgrep-report.json>", file=sys.stderr)
        return 2

    report_path = Path(sys.argv[1])
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"Semgrep report cannot be read: {error}", file=sys.stderr)
        return 2

    errors = report.get("errors", [])
    if errors:
        print(f"Semgrep reported {len(errors)} analysis error(s); failing closed.", file=sys.stderr)
        for error in errors:
            print(f"- {error.get('message', error.get('type', 'unknown error'))}", file=sys.stderr)
        return 2

    timeouts = report.get("time", {}).get("fixpoint_timeouts", [])
    if timeouts:
        print(f"Semgrep reported {len(timeouts)} analysis timeout(s); failing closed.", file=sys.stderr)
        for timeout in timeouts:
            print(f"- {timeout.get('message', 'unknown timeout')}", file=sys.stderr)
        return 2

    skipped_rules = report.get("skipped_rules", [])
    if skipped_rules:
        print(f"Semgrep skipped {len(skipped_rules)} intended rule(s); failing closed.", file=sys.stderr)
        return 2

    results = report.get("results", [])
    blocking = []
    advisory = []
    for finding in results:
        extra = finding.get("extra", {})
        metadata = extra.get("metadata", {}) or {}
        confidence = str(metadata.get("confidence", "UNKNOWN")).upper()
        severity = str(extra.get("severity", "UNKNOWN")).upper()
        if confidence == "HIGH" or (confidence == "MEDIUM" and severity == "ERROR"):
            blocking.append(finding)
        else:
            advisory.append(finding)

    print(
        "Semgrep policy summary: "
        f"{len(results)} finding(s), {len(blocking)} blocking, {len(advisory)} advisory."
    )
    for finding in blocking:
        start = finding.get("start", {})
        print(
            "BLOCKING: "
            f"{finding.get('check_id', 'unknown-rule')} at "
            f"{finding.get('path', 'unknown-path')}:{start.get('line', '?')}"
        )

    return 42 if blocking else 0


if __name__ == "__main__":
    raise SystemExit(main())
