"""Read-only delta/status reports and explicit workflow effort events; no CAD writes."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def keyed(rows):
    result = {}
    for row in rows:
        if not row.get("id") or row["id"] in result:
            raise ValueError("Missing or duplicate stable ID")
        result[row["id"]] = row
    return result


def evidence_current(plan, base):
    """Conservatively invalidate reuse if any preserved source/library changed."""
    refs = list(plan.get("sources", []))
    refs += [p["library"] for p in plan.get("parts", []) if p.get("library")]
    for ref in refs:
        path = Path(ref.get("path", ""))
        path = path if path.is_absolute() else base / path
        try:
            if not ref.get("sha256") or sha(path) != ref["sha256"]:
                return False
        except OSError:
            return False
    return bool(refs)


def delta(old_path, new_path):
    old_path, new_path = Path(old_path).resolve(), Path(new_path).resolve()
    old, new = load(old_path), load(new_path)
    if old.get("assembly") != new.get("assembly") or old_path.parent != new_path.parent:
        raise ValueError("Delta requires the same assembly and job folder")
    old_parts, new_parts = keyed(old.get("parts", [])), keyed(new.get("parts", []))
    current = evidence_current(old, old_path.parent) and evidence_current(new, new_path.parent)
    rows = []
    for pid, part in new_parts.items():
        prior = old_parts.get(pid)
        status, reason = "inspect", "New or changed complete part record"
        # Compare every part field, not just stock length or a partial geometry signature.
        if prior == part and part.get("identity_reviewed") is True and current:
            if part.get("mode") == "direct":
                status, reason = "direct_current", "Unmodified library hash current; assembly checks still required"
            else:
                native = new_path.parent / part["file"]
                receipt = Path(str(native) + ".verification.json")
                try:
                    r = load(receipt)
                    e = r.get("Expected", {})
                    bound_path = Path(e.get("PlanFile", ""))
                    bound_path = bound_path if bound_path.is_absolute() else new_path.parent / bound_path
                    bound = load(bound_path)
                    if (r.get("Status") == "passed" and r.get("Saved") is True and r.get("Closed") is True
                            and Path(r.get("File", "")).resolve() == native.resolve()
                            and r.get("FileSha256") == sha(native) and e.get("PartId") == pid
                            and e.get("PlanSha256") == sha(bound_path)
                            and bound.get("assembly") == new.get("assembly")
                            and keyed(bound.get("parts", [])).get(pid) == part):
                        status = "retain_candidate"
                        reason = "Complete identity and passed saved/closed file hash unchanged; reconcile plan binding before completion"
                    else:
                        reason = "Native receipt, file hash or plan binding differs"
                except (OSError, ValueError, KeyError, TypeError):
                    reason = "Missing or unreadable native evidence"
        elif not current:
            reason = "Source/library evidence unavailable or changed; inspect applicability"
        rows.append({"id": pid, "status": status, "reason": reason})
    return {
        "old_plan_sha256": sha(old_path), "new_plan_sha256": sha(new_path),
        "counts": dict(Counter(r["status"] for r in rows)), "parts": rows,
        "removed": sorted(set(old_parts) - set(new_parts)),
        "assembly_recheck_required": True,
        "assembly_context_changed": any(old.get(k) != new.get(k) for k in
                                        ("occurrences", "pieces", "sources", "conflicts")),
        "scope": "Advisory only. No receipt edits, native verification, planner approval or completion claim."
    }


def compact(path):
    data = load(path)
    rows = data.get("Parts", data.get("parts", []))
    issues = data.get("issues", [])
    counts = Counter(r.get("Status", r.get("status", "unknown")) for r in rows)
    failures = [{"id": r.get("Id", r.get("id")),
                 "status": r.get("Status", r.get("status")),
                 "reason": str(r.get("Error", r.get("reason", "")))[:500]}
                for r in rows if r.get("Status", r.get("status")) not in
                ("passed", "ready", "retain_candidate", "direct_current")]
    return {"report": str(Path(path).resolve()), "sha256": sha(path),
            "status": data.get("Status", data.get("status", "unspecified")),
            "counts": dict(counts), "exceptions": failures[:20],
            "exceptions_omitted": max(0, len(failures)-20),
            "issue_counts": dict(Counter(i.get("level", "unknown") for i in issues)),
            "issues": issues[:10], "issues_omitted": max(0, len(issues)-10)}


def effort(folder):
    stages = {}
    path = Path(folder) / "effort-events.jsonl"
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip(): continue
        row = json.loads(line)
        totals = stages.setdefault(row["stage"], dict(seconds=0, calls=0, retries=0, events=0))
        for key in ("seconds", "calls", "retries"): totals[key] += row[key]
        totals["events"] += 1
    return {"stages_by_elapsed_time": sorted(
        [dict(stage=key, **value) for key, value in stages.items()],
        key=lambda r: r["seconds"], reverse=True),
        "scope": "Recorded effort only; incomplete instrumentation is possible. Not token or credit costs."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    d = commands.add_parser("delta"); d.add_argument("old"); d.add_argument("new")
    s = commands.add_parser("summary"); s.add_argument("report")
    m = commands.add_parser("effort"); m.add_argument("job_folder")
    for sub in (d, s, m):
        sub.add_argument("--out", help="Create a NEW report; never overwrite evidence")
    e = commands.add_parser("event", help="Append measured effort only, never estimated billing")
    e.add_argument("job_folder"); e.add_argument("stage", choices=["inspect", "plan", "build", "repair", "verify"])
    e.add_argument("--seconds", type=float, required=True)
    e.add_argument("--calls", type=int, default=0); e.add_argument("--retries", type=int, default=0)
    e.add_argument("--note", default="")
    args = parser.parse_args()
    if args.command == "event":
        import math
        if not math.isfinite(args.seconds) or min(args.seconds, args.calls, args.retries) < 0:
            parser.error("Measured seconds, calls and retries must be finite and nonnegative")
        folder = Path(args.job_folder).resolve()
        if not folder.is_dir(): parser.error("Existing job folder required")
        result = dict(utc=datetime.now(timezone.utc).isoformat(), stage=args.stage,
                      seconds=args.seconds, calls=args.calls, retries=args.retries, note=args.note,
                      billing="Not token or credit measurements")
        with (folder / "effort-events.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps(result) + "\n")
    else:
        if args.command == "delta": result = delta(args.old, args.new)
        elif args.command == "effort": result = effort(args.job_folder)
        else: result = compact(args.report)
        if args.out:
            with Path(args.out).open("x", encoding="utf-8") as f:
                json.dump(result, f, indent=2); f.write("\n")
            result = {"report": str(Path(args.out).resolve()),
                      "counts": result.get("counts", {}), "scope": result.get("scope", "Report summary")}
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
