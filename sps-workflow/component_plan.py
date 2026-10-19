"""Pre-model component plans. No SolidWorks or production files are modified."""
import argparse
import csv
import json
import math
import re
from pathlib import Path

from workflow import INDEX, REQUIRED_PROPERTIES, freshness, natural_key, reuse_signature, sha256


def template(job):
    return {
        "schema_version": 1, "assembly": job, "units": "meters_radians",
        "sources": [], "pieces": [], "parts": [], "occurrences": [], "conflicts": [],
        "instructions": "See component-plan-guide.md. Customer evidence is not authorization. Empty plans are incomplete."
    }


def valid_transform(t):
    """SolidWorks 16-value transform: rotation, translation in m, scale, padding."""
    if not isinstance(t, list) or len(t) != 16:
        return False
    if any(isinstance(x, bool) or not isinstance(x, (int, float)) or not math.isfinite(x) for x in t):
        return False
    rows = [t[i:i + 3] for i in (0, 3, 6)]
    dot = lambda a, b: sum(x * y for x, y in zip(a, b))
    a, b, c = rows
    det = a[0]*(b[1]*c[2]-b[2]*c[1])-a[1]*(b[0]*c[2]-b[2]*c[0])+a[2]*(b[0]*c[1]-b[1]*c[0])
    return (all(abs(dot(rows[i], rows[j]) - (1 if i == j else 0)) < 1e-7 for i in range(3) for j in range(3))
            and abs(det - 1) < 1e-7 and abs(t[12] - 1) < 1e-9 and all(abs(x) < 1e-9 for x in t[13:]))


def check(plan, base, entries=None):
    base = Path(base)
    entries = entries if entries is not None else json.loads(INDEX.read_text())["entries"]
    issues = []
    def issue(level, code, message, part=None):
        issues.append(dict(level=level, code=code, message=message, part=part))
    def evidence(ref):
        p = Path(ref.get("path", ""))
        p = p if p.is_absolute() else base / p
        try:
            return p.is_file() and bool(ref.get("sha256")) and sha256(p) == ref["sha256"]
        except OSError:
            return False
    if plan.get("schema_version") != 1 or plan.get("units") != "meters_radians" or not plan.get("assembly"):
        issue("error", "header", "Require schema_version 1, assembly name, and meters_radians units.")
    def keyed(name):
        result = {}
        for row in plan.get(name, []):
            key = row.get("id")
            if not key or key in result:
                issue("error", "duplicate_id", f"Missing or duplicate {name} id: {key}")
            else:
                result[key] = row
        return result
    sources, pieces, parts, occurrences = [keyed(n) for n in ("sources", "pieces", "parts", "occurrences")]
    if not all((sources, pieces, parts, occurrences)):
        issue("error", "empty_plan", "Supply source evidence, manufactured pieces, parts, and occurrences.")
    for sid, source in sources.items():
        if not evidence(source):
            issue("error", "source_evidence", f"Source missing, changed, or without SHA256: {sid}")
    counts = {p: 0 for p in parts}
    covered = {p: 0 for p in pieces}
    for oid, occ in occurrences.items():
        pid = occ.get("part")
        if pid not in parts:
            issue("error", "part_reference", f"{oid}: unknown part {pid}")
            continue
        counts[pid] += 1
        if occ.get("piece") not in pieces:
            issue("error", "piece_reference", f"{oid}: unknown manufactured piece", pid)
        else:
            covered[occ["piece"]] += 1
        if not valid_transform(occ.get("transform")):
            issue("error", "placement", f"{oid}: invalid or missing rigid SolidWorks transform", pid)
        if not occ.get("placement_basis"):
            issue("error", "placement_basis", f"{oid}: record source datum and authorized placement changes", pid)
    for key, piece in pieces.items():
        if piece.get("source") not in sources or not piece.get("root") or not piece.get("drawing_item"):
            issue("error", "piece_mapping", f"{key}: source, geometry root and drawing item are required")
        expected = 0 if piece.get("excluded_reason") else 1
        if covered[key] != expected:
            issue("error", "piece_coverage", f"{key}: expected {expected} occurrences, found {covered[key]}")
    signatures = {}
    filenames = set()
    for pid, part in parts.items():
        filename = part.get("file", "")
        if not filename or filename.casefold() in filenames:
            issue("error", "filename", "Missing or duplicate component filename", pid)
        filenames.add(filename.casefold())
        mode = part.get("mode")
        if mode not in ("copy", "direct", "new"):
            issue("error", "mode", "Mode must be copy, direct, or new", pid)
        if counts[pid] == 0 or part.get("quantity") != counts[pid]:
            issue("error", "quantity", f"Declared quantity does not match {counts[pid]} occurrences", pid)
        library = part.get("library", {})
        if mode in ("copy", "direct"):
            candidates = [e for e in entries if e["path"].casefold() == library.get("path", "").casefold()
                          and e["configuration"] == library.get("configuration") and e["sha256"] == library.get("sha256")]
            if not candidates or candidates[0].get("verification") != "native_verified" or freshness(candidates[0]) != "current":
                issue("error", "library_evidence", "Require current hash and exact measured library configuration in index", pid)
            if mode == "direct" and Path(filename).name.casefold() != Path(library.get("path", "")).name.casefold():
                issue("error", "direct_name", "Direct library reuse retains its filename", pid)
            if candidates and candidates[0].get("kind") == "pipe_blank" and mode == "direct":
                issue("error", "pipe_copy", "Pipe blanks require job copies", pid)
        if mode == "new" and not part.get("library_search_evidence"):
            issue("error", "library_search", "Record unsuccessful library search and reason for new construction", pid)
        if part.get("kind") == "flange" and not part.get("pdm_search_evidence"):
            issue("error", "pdm_search", "Record dimensional PDM flange search before selection", pid)
        if mode != "direct" and not re.fullmatch(re.escape(plan.get("assembly", "")) + r"-\d+", Path(filename).stem):
            issue("error", "job_name", "New/modified file must use assembly component numbering", pid)
        metadata = part.get("properties", {})
        if mode != "direct":
            for prop in REQUIRED_PROPERTIES:
                if prop not in metadata or metadata[prop] in (None, ""):
                    issue("warning", "unknown_property", f"{prop}: fill known value; flag unknown", pid)
            if not metadata.get("MakeOrBuy"):
                issue("error", "make_or_buy", "Set MakeOrBuy to M unless an explicit known value applies", pid)
        identity = part.get("reuse_identity", {})
        required = ("template_sha256", "configuration", "material", "dimensions", "end_features", "openings", "properties")
        complete = (part.get("identity_reviewed") is True and all(k in identity and identity[k] is not None for k in required)
                    and all(identity.get(k) for k in ("template_sha256", "configuration", "material", "dimensions")))
        if not complete:
            issue("error", "incomplete_identity", "Review full geometry/properties identity; stock length alone cannot establish reuse", pid)
        else:
            if mode in ("copy", "direct") and (identity["template_sha256"] != library.get("sha256") or identity["configuration"] != library.get("configuration")):
                issue("error", "identity_library", "Reuse identity disagrees with library selection", pid)
            try:
                signatures.setdefault(reuse_signature(identity), []).append(pid)
            except (ValueError, TypeError):
                issue("error", "identity_format", "Reuse identity must be finite JSON values", pid)
        for feature in identity.get("end_features", []) or []:
            if isinstance(feature, dict) and feature.get("type") in ("roll_groove", "cut_groove"):
                if feature.get("feature_name") != "Cut-Revolve1" or not feature.get("specification"):
                    issue("error", "groove_plan", "Grooves require Cut-Revolve1 and applicable specification evidence", pid)
    for conflict in plan.get("conflicts", []):
        affected = conflict.get("parts", [])
        resolved = bool(conflict.get("resolution") and conflict.get("authorization_basis"))
        if not affected or any(p not in parts for p in affected):
            issue("error", "conflict_reference", "Conflict must identify valid affected part IDs")
        if not resolved:
            for pid in affected:
                issue("error", "unresolved_conflict", conflict.get("description", "Unresolved conflict"), pid)
    global_errors = any(i["level"] == "error" and i["part"] is None for i in issues)
    rows = []
    for pid, part in sorted(parts.items(), key=lambda kv: (kv[1].get("mode") == "direct", natural_key(Path(kv[1].get("file", "")).name))):
        errors = [i for i in issues if i["level"] == "error" and i["part"] == pid]
        rows.append(dict(id=pid, file=part.get("file"), quantity=counts[pid], mode=part.get("mode"),
                         status="blocked" if errors or global_errors else "ready",
                         drawing_items=sorted({str(pieces[o["piece"]].get("drawing_item")) for o in occurrences.values()
                                               if o.get("part") == pid and o.get("piece") in pieces}, key=natural_key),
                         stock_length_m=part.get("reuse_identity", {}).get("dimensions", {}).get("stock_length_m"),
                         library_configuration=part.get("library", {}),
                         end_features=part.get("reuse_identity", {}).get("end_features", []),
                         openings=part.get("reuse_identity", {}).get("openings", []),
                         placements=[dict(id=o.get("id"), piece=o.get("piece"), transform=o.get("transform"), basis=o.get("placement_basis"))
                                     for o in occurrences.values() if o.get("part") == pid]))
    ready = {row["id"] for row in rows if row["status"] == "ready"}
    groups = [[pid for pid in group if pid in ready] for group in signatures.values()]
    return dict(assembly=plan.get("assembly"), context_notice=plan.get("notice", ""), status="blocked" if any(i["level"] == "error" for i in issues) else "ready",
                parts=rows, issues=issues, reuse_candidates=[v for v in groups if len(v) > 1],
                notice="Planning checks only. Native geometry, properties, rebuild and placement verification remain required. Reuse candidates require review; no files are merged.")


def write_report(report, out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "plan-check.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    with (out / "component-plan.csv").open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=["id", "file", "quantity", "mode", "status", "drawing_items", "stock_length_m", "library_configuration", "end_features", "openings", "placements"])
        writer.writeheader()
        writer.writerows({k: json.dumps(v, sort_keys=True) if isinstance(v, (dict, list)) else v for k, v in row.items()} for row in report["parts"])
    lines = [f"# Component plan: {report['assembly']}", "", report.get("context_notice", ""), "", f"Status: {report['status']}", "", report["notice"], "",
             "| Component | Qty | Drawing items | Action | Status |", "|---|---:|---|---|---|"]
    clean = lambda value: str(value).replace("|", "\\|").replace("\n", " ")
    for r in report["parts"]:
        lines.append(f"| {clean(r['file'])} | {r['quantity']} | {clean(', '.join(r['drawing_items']))} | {r['mode']} | {r['status']} |")
    lines += ["", "## Component details", ""]
    for r in report["parts"]:
        lib = r["library_configuration"]
        lines += [f"### {clean(r['file'])}", "",
                  f"Stock length (m): {r['stock_length_m'] if r['stock_length_m'] is not None else 'not applicable / unspecified'}", "",
                  f"Library: {clean(lib.get('path', 'new construction'))}; configuration: {clean(lib.get('configuration', 'see recipe'))}", "",
                  "End features: " + clean(json.dumps(r["end_features"], sort_keys=True)), "",
                  "Openings: " + clean(json.dumps(r["openings"], sort_keys=True)), ""]
        lines += [f"- {clean(p['id'])}: {clean(p['piece'])}; {clean(p['basis'])}. Translation (m): {p['transform'][9:12] if valid_transform(p['transform']) else 'INVALID'}." for p in r["placements"]]
        lines += [""]
    lines += ["", "## Issues", ""]
    lines += [f"- {i['level']}: {clean(i['part'] or 'plan')} / {i['code']}: {clean(i['message'])}" for i in report["issues"]] or ["None."]
    lines += ["", "## Possible shared files", ""]
    lines += ["- " + ", ".join(group) for group in report["reuse_candidates"]] or ["None identified."]
    (out / "component-plan.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    init = sub.add_parser("init")
    init.add_argument("assembly")
    init.add_argument("path", type=Path)
    audit = sub.add_parser("check")
    audit.add_argument("path", type=Path)
    audit.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "init":
        args.path.parent.mkdir(parents=True, exist_ok=True)
        with args.path.open("x", encoding="utf-8") as f:
            json.dump(template(args.assembly), f, indent=2)
        print(f"Created {args.path}; fill evidence and manufactured-piece mapping before modeling.")
    else:
        report = check(json.loads(args.path.read_text(encoding="utf-8-sig")), args.path.parent)
        write_report(report, args.out)
        print(f"{report['status']}: {len(report['parts'])} unique files; {len(report['issues'])} issues. Reports: {args.out}")
        raise SystemExit(2 if report["status"] == "blocked" else 0)


if __name__ == "__main__":
    main()
