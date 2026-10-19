"""Create a standard job handoff from current, hash-bound verification evidence."""
import argparse
import json
import math
import re
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path

from component_plan import check as check_plan
from workflow import sha256

LENGTH_PROPERTIES = {"Stock Length", "Cut Length", "Max Modeled Length", "Finished Length", "Overall Length", "Length"}


def format_literal_length(value):
    match = re.fullmatch(r'(\s*)([-+]?(?:\d+\.\d+|\d+))(\s*(?:(?:in(?:ch(?:es)?)?|mm|m|"|\s))*)', str(value), re.IGNORECASE)
    if not match:
        return str(value)
    number = Decimal(match[2]).quantize(Decimal(".001"), rounding=ROUND_HALF_UP)
    text = format(number, "f").rstrip("0").rstrip(".") if "." in format(number, "f") else format(number, "f")
    return match[1] + ("0" if number == 0 else text) + match[3]


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def finish(plan_path, packet, entries=None):
    plan_path = Path(plan_path).resolve()
    base, plan_hash = plan_path.parent, sha256(plan_path)
    plan = load(plan_path)
    planning = check_plan(plan, base, entries)
    errors = [i["message"] for i in planning["issues"] if i["level"] == "error"]
    warnings = []
    resolved_properties = set()
    def resolve(path):
        p = Path(path)
        return (p if p.is_absolute() else base / p).resolve()
    def evidence(ref, purpose):
        try:
            p = resolve(ref["path"])
            if sha256(p) != ref["sha256"]:
                raise ValueError("hash changed")
            return load(p)
        except (OSError, ValueError, KeyError, TypeError) as exc:
            errors.append(f"{purpose}: missing, changed or invalid evidence ({exc})")
            return {}
    if packet.get("plan_sha256") != plan_hash:
        errors.append("Completion packet does not match the current reviewed plan")
    if packet.get("batch_used") is True:
        batch = evidence(packet.get("batch", {}), "Batch verification")
        expected_ids = {p["id"] for p in plan["parts"] if p["mode"] != "direct"}
        if (batch.get("Status") != "passed" or batch.get("PlanSha256") != plan_hash or
                {p.get("Id") for p in batch.get("Parts", [])} != expected_ids or
                any(p.get("Status") != "passed" for p in batch.get("Parts", []))):
            errors.append("Batch must have passed every planned job part; failures or unfinished checkpoints cannot complete the job")
    elif packet.get("batch_used") is not False:
        errors.append("Record whether a batch was used; its final checkpoint is required when applicable")
    files, receipts = {}, []
    parts = {p["id"]: p for p in plan["parts"]}
    for row in planning["parts"]:
        part, pid = parts[row["id"]], row["id"]
        path = resolve(part["library"]["path"] if part["mode"] == "direct" else part["file"])
        if part["mode"] != "direct" and path.parent != base:
            errors.append(f"{pid}: job part must be directly inside its assembly job folder")
        try:
            current = sha256(path)
        except OSError:
            errors.append(f"{pid}: native component file missing")
            continue
        files[pid] = dict(path=str(path), sha256=current, configuration=part.get("library", {}).get("configuration", "Default"))
        if part["mode"] == "direct":
            continue
        receipt = evidence(packet.get("parts", {}).get(pid, {}), pid + " verification")
        expected = receipt.get("Expected", {})
        if (receipt.get("ProtocolVersion") != 1 or receipt.get("Status") != "passed" or
                not receipt.get("Saved") or not receipt.get("Closed") or receipt.get("FileSha256") != current or
                expected.get("PlanSha256") != plan_hash or expected.get("PartId") != pid or
                receipt.get("Snapshot", {}).get("Configuration") != files[pid]["configuration"]):
            errors.append(f"{pid}: requires a passed, saved/closed native receipt bound to this plan, part ID, file and configuration")
        try:
            if resolve(receipt.get("File", "")) != path:
                errors.append(f"{pid}: receipt belongs to a different native file")
        except (TypeError, ValueError):
            errors.append(f"{pid}: invalid receipt file path")
        warnings += [f"{pid}: {w}" for w in receipt.get("Warnings", [])]
        resolved_properties.update((pid, key) for key, reading in receipt.get("Properties", {}).items() if reading.get("Raw"))
        for key, value in part.get("properties", {}).items():
            if value not in (None, "") and key not in ("Material", "MaterialQty", "Number", "Description", "Part Description"):
                expected_value = format_literal_length(value) if key in LENGTH_PROPERTIES else str(value)
                reading = receipt.get("Properties", {}).get(key, {})
                actual_value = reading.get("Raw")
                if key in LENGTH_PROPERTIES and "@" not in str(value):
                    actual_value = format_literal_length(reading.get("Evaluated") or actual_value or "")
                if actual_value != expected_value:
                    errors.append(f"{pid}: verified {key} differs from the known planned value")
        receipts.append(pid)
    warnings += [f"{i['part']}: {i['message']}" for i in planning["issues"] if i["level"] == "warning" and
                 (i["code"] != "unknown_property" or (i["part"], i["message"].split(":", 1)[0]) not in resolved_properties)]
    assembly_path = resolve(plan["assembly"] + ".SLDASM")
    try:
        assembly_hash = sha256(assembly_path)
    except OSError:
        assembly_hash = None
        errors.append("Saved main assembly is missing")
    assembly = evidence(packet.get("assembly", {}), "Assembly verification")
    if (assembly.get("ProtocolVersion") != 1 or assembly.get("Status") != "passed" or not assembly.get("Saved") or
            not assembly.get("MainAssemblyOpen") or assembly.get("FileSha256") != assembly_hash or assembly.get("PlanSha256") != plan_hash):
        errors.append("Assembly verification must match the current plan/file and record a saved, open main assembly")
    if assembly.get("File") and resolve(assembly["File"]) != assembly_path:
        errors.append("Assembly receipt belongs to a different main assembly")
    ordered = []
    for row in planning["parts"]:
        for occ in plan["occurrences"]:
            if occ.get("part") == row["id"]:
                ordered.append((row["id"], occ))
    actual = assembly.get("Components", [])
    if len(actual) != len(ordered):
        errors.append("Assembly occurrence count differs from the plan")
    else:
        for (pid, occ), actual_occ in zip(ordered, actual):
            info = files.get(pid, {})
            try:
                transform = actual_occ["Transform"]
                same = (resolve(actual_occ["File"]) == Path(info["path"]) and actual_occ["FileSha256"] == info["sha256"] and
                        actual_occ["Configuration"] == info["configuration"] and len(transform) == 16 and
                        all(isinstance(v, (int, float)) and math.isfinite(v) and abs(v - target) < 1e-7 for v, target in zip(transform, occ["transform"])))
            except (KeyError, TypeError, ValueError):
                same = False
            if not same:
                errors.append(f"Assembly order, file, configuration or placement mismatch: {occ['id']}")
    reviews = ["drawing_dimensions", "connection_rules", "visual_review"]
    if packet.get("bom_created") is True:
        reviews.append("bom_order")
    elif packet.get("bom_created") is not False:
        errors.append("Record whether a BOM was created; an existing BOM needs its own order review")
    for name in reviews:
        review = evidence(packet.get("reviews", {}).get(name, {}), name)
        valid_status = review.get("status") == "passed" or (review.get("status") == "not_applicable" and bool(review.get("reason")))
        if (not valid_status or review.get("plan_sha256") != plan_hash or review.get("assembly_sha256") != assembly_hash or
                review.get("part_hashes") != {pid: f["sha256"] for pid, f in files.items()} or not review.get("reviewer") or not review.get("findings")):
            errors.append(f"{name}: explicit review with findings, reviewer and current plan/assembly/component hashes required")
    return dict(assembly=plan["assembly"], status="incomplete" if errors else "complete_with_flags" if warnings else "complete",
                plan_sha256=plan_hash, assembly_sha256=assembly_hash, unique_files=len(parts),
                occurrences=len(ordered), parts=planning["parts"], verified_files=files, verified_job_parts=receipts,
                assembly_file=str(assembly_path), decisions=plan.get("conflicts", []), errors=sorted(set(errors)), warnings=sorted(set(warnings)),
                physical_test="CAD verification does not perform any required pressure/physical test.",
                scope="Evidence integrity and recorded checks verified. Review reports remain responsible for engineering accuracy; unknown metadata stays flagged.")


def write(report, out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "completion-status.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = [f"# Completion: {report['assembly']}", "", f"Status: {report['status']}", "",
             f"{report['occurrences']} manufactured occurrences; {report['unique_files']} unique component files.", "", report["scope"], "", report["physical_test"], "", "## Remaining issues", ""]
    lines += ["- " + s for s in report["errors"]] or ["None."]
    lines += ["", "## Metadata and other flags", ""]
    lines += ["- " + s for s in report["warnings"]] or ["None."]
    (out / "completion-status.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    init = sub.add_parser("init")
    init.add_argument("plan", type=Path)
    init.add_argument("packet", type=Path)
    refresh = sub.add_parser("refresh")
    refresh.add_argument("plan", type=Path)
    refresh.add_argument("packet", type=Path)
    audit = sub.add_parser("check")
    audit.add_argument("plan", type=Path)
    audit.add_argument("packet", type=Path)
    audit.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.command in ("init", "refresh"):
        plan = load(args.plan)
        if args.command == "init":
            packet = dict(plan_sha256=sha256(args.plan), bom_created=None, batch_used=None, batch=dict(path="final-batch-checkpoint.json", sha256=""),
                          parts={p["id"]: dict(path=p["file"]+".verification.json", sha256="") for p in plan["parts"] if p["mode"] != "direct"},
                          assembly=dict(path="assembly-verification.json", sha256=""),
                          reviews={n:dict(path="reviews/"+n+".json", sha256="") for n in ("drawing_dimensions", "connection_rules", "visual_review", "bom_order")})
        else:
            packet = load(args.packet)
            if packet.get("plan_sha256") != sha256(args.plan):
                raise SystemExit("Plan changed. Reconcile affected native receipts and reviews, then create a new packet; refresh cannot authorize a new plan.")
        for ref in [*packet["parts"].values(), packet["assembly"], *packet["reviews"].values(), *([packet["batch"]] if packet.get("batch") else [])]:
            path = Path(ref["path"])
            path = path if path.is_absolute() else args.plan.resolve().parent / path
            ref["sha256"] = sha256(path) if path.is_file() else ""
        args.packet.parent.mkdir(parents=True, exist_ok=True)
        with args.packet.open("x" if args.command == "init" else "w", encoding="utf-8") as stream:
            json.dump(packet, stream, indent=2)
            stream.write("\n")
        print("Evidence references prepared; no verification or review was marked passed.")
        return
    report = finish(args.plan, load(args.packet))
    write(report, args.out)
    print(f"{report['status']}: {len(report['errors'])} remaining issues; {len(report['warnings'])} flags. {args.out}")
    raise SystemExit(2 if report["errors"] else 0)


if __name__ == "__main__":
    main()
