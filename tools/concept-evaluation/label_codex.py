"""Explicit offline label adapter. Fresh ephemeral Codex sessions; no repository context or tools."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import evaluate as ev


def audit(events):
    session = None
    completed = False
    for line in events.splitlines():
        item = json.loads(line)
        kind = item.get("type")
        if kind not in ("thread.started", "turn.started", "turn.completed", "item.started", "item.updated", "item.completed"):
            raise ValueError("Unrecognized or failed labeling session event")
        if kind == "thread.started":
            session = item["thread_id"]
        if kind == "turn.completed":
            completed = True
        if kind in ("turn.failed", "error"):
            raise ValueError("Label session failed")
        if kind in ("item.started", "item.updated", "item.completed"):
            if item["item"].get("type") not in ("agent_message", "reasoning"):
                raise ValueError("Labeling session used a tool or other disallowed item")
    if not session or not completed:
        raise ValueError("Incomplete labeling session")
    return session


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", required=True)
    parser.add_argument("--phase", required=True, choices=["a", "b", "adjudication"])
    parser.add_argument("--codex", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--limit", type=int, default=100000)
    parser.add_argument("--shard", type=int, default=0)
    parser.add_argument("--shards", type=int, default=1)
    args = parser.parse_args()
    if args.shards < 1 or not 0 <= args.shard < args.shards or args.limit < 1:
        raise ValueError("Invalid shard/limit")
    run = Path(args.run).resolve()
    ev.verify(run)
    if (run / "reference-labels.json").exists():
        raise ValueError("Reference already frozen")
    version = subprocess.check_output([args.codex, "--version"], text=True).strip()
    count = 0
    for index, path in enumerate(sorted((run / "batches" / args.phase).glob("*.json"))):
        if index % args.shards != args.shard:
            continue
        if (run / "labels" / args.phase / path.name).exists():
            continue
        if count >= args.limit:
            break
        claims = run / ".claims"
        claims.mkdir(exist_ok=True)
        claim = claims / path.name
        try:
            with claim.open("x") as stream:
                stream.write(ev.now())
        except FileExistsError:
            continue
        try:
            if (run / "labels" / args.phase / path.name).exists():
                continue
            batch = ev.read(path)
            ev.verify_batch(run, batch, args.phase)
            raw = run / "raw" / args.phase / path.stem
            raw.mkdir(parents=True, exist_ok=True)
            attempt = raw / ("attempt-" + ev.now().replace(":", "-").replace("+", "_"))
            attempt.mkdir(exist_ok=False)
            prompt = ev.encoded(batch)
            (attempt / "prompt.json").write_bytes(prompt)
            started = ev.now()
            ev.freeze(attempt / "invocation.json", dict(model=args.model, tool=version, startedUtc=started,
                      batchId=batch["batchId"], promptHash=ev.digest(prompt), protocol="fresh-ephemeral-no-tools-v1"))
            # CWD contains no repository, labels, detector results, or history. Never resume.
            with tempfile.TemporaryDirectory(prefix="jsm-blinded-label-") as isolated:
                result_path = Path(isolated) / "response.json"
                command = [args.codex, "exec", "--ignore-user-config", "--ephemeral", "--skip-git-repo-check",
                           "--sandbox", "read-only", "-c", "features.shell_tool=false", "-c", 'web_search="disabled"',
                           "-m", args.model, "-C", isolated, "--json", "-o", str(result_path), "-"]
                ev.freeze(attempt / "command.json", command)
                with (attempt / "events.jsonl").open("wb") as events, (attempt / "stderr.log").open("wb") as errors:
                    proc = subprocess.run(command, input=prompt, stdout=events, stderr=errors, timeout=1200)
                events_bytes = (attempt / "events.jsonl").read_bytes()
                if result_path.exists():
                    (attempt / "response.json").write_bytes(result_path.read_bytes())
                if proc.returncode:
                    raise RuntimeError("Label invocation failed; raw attempt retained: " + str(attempt))
                session = audit(events_bytes.decode())
                response = ev.read(attempt / "response.json")
            ev.validate_response(batch, response)
            envelope = dict(identity=dict(model=args.model, tool=version, startedUtc=started, completedUtc=ev.now(), sessionId=session,
                           modelIdentityNote="Explicit requested model; backend revision is not exposed by the CLI.",
                           rawPath=attempt.relative_to(run).as_posix(), promptHash=ev.digest(prompt),
                           eventsHash=ev.digest(events_bytes)), response=response)
            ev.freeze(attempt / "envelope.json", envelope)
            ev.import_labels(argparse.Namespace(run=str(run), phase=args.phase, batch=str(path), response=str(attempt / "envelope.json")))
        finally:
            claim.unlink(missing_ok=True)
        count += 1
        print(json.dumps({"batch": path.stem, "completed": count}), flush=True)


if __name__ == "__main__":
    main()
