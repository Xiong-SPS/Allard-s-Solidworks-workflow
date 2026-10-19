"""Local SPS library shortlist and shared planning checks. Standard library only."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
INDEX = ROOT / "index" / "library-index.json"
REQUIRED_PROPERTIES = ["Customer", "Description", "Job Number", "Item Number", "DrawnBy",
                       "Part Description", "Material", "Number", "MaterialQty", "Material ID",
                       "Drawn By", "Drawn Date", "MakeOrBuy", "Revision"]


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def freshness(entry):
    try:
        return "current" if sha256(entry["path"]) == entry["sha256"] else "changed"
    except FileNotFoundError:
        return "not_cached"
    except OSError:
        return "unreadable"


def natural_key(text):
    return [int(p) if p.isdigit() else p.casefold() for p in re.split(r"(\d+)", text)]


def read_native_log(path):
    entries = []
    current = None
    for line in Path(path).read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("FILE "):
            current = {"path": line[5:], "properties": {}, "dimensions_m_or_rad": {},
                       "cylinders": [], "tori": [], "planes": []}
            entries.append(current)
        elif current:
            if line.startswith("CONFIGS "):
                current["known_configurations"] = line[8:].split(",")
            elif line.startswith("MATERIAL "):
                current["material"], current["material_database"] = line[9:].split(" DATABASE ", 1)
            elif line.startswith("PROPERTY "):
                name, values = line[9:].split("=", 1)
                raw, evaluated = values.split(" -> ", 1)
                current["properties"][name] = {"raw": raw, "evaluated": evaluated}
            elif line.startswith("DIM "):
                name, value = line[4:].rsplit("=", 1)
                current["dimensions_m_or_rad"]["@".join(name.split("@")[:2])] = float(value)
            elif line.startswith("SOLIDS "):
                current["solid_count"] = int(line[7:])
            else:
                for prefix, field in [("CYL ", "cylinders"), ("TOR ", "tori"), ("PLANE ", "planes")]:
                    if line.startswith(prefix):
                        current[field].append([float(n) for n in line[len(prefix):].split(",")])
    return entries


def build_index():
    """Import measured configurations only when the audited source hashes still match."""
    native_log = PROJECT / "tmp/16186-262-06/library-log.txt"
    audit_path = PROJECT / "16186-262-06/native-validation.txt"
    hash_path = PROJECT / "16186-262-06/library-original-hashes.json"
    audit = audit_path.read_text()
    if "PASS 26 component placements" not in audit:
        raise ValueError("Seed job does not have a completed native audit")
    hashes = json.loads(hash_path.read_text())
    entries = []
    for measured in read_native_log(native_log):
        file = Path(measured["path"])
        expected = hashes.get(str(file))
        if not expected:
            raise ValueError(f"No audit hash for {file}")
        if sha256(file) != expected:
            raise ValueError(f"Library changed since measurement; remeasure {file}")
        name = file.stem
        dims = measured["dimensions_m_or_rad"]
        geo = {}
        kind = "fitting"
        aliases = []
        if name.startswith("PIPE "):
            kind = "pipe_blank"
            radii = sorted({round(c[6], 10) for c in measured["cylinders"]})
            if len(radii) != 2:
                raise ValueError(f"Ambiguous pipe radii {file}")
            geo = dict(od_m=radii[1] * 2, id_m=radii[0] * 2,
                       wall_m=radii[1] - radii[0], length_parameter_m=dims["D1@Boss-Extrude1"])
            aliases = ["pipe", "schedule 10", "Sch10", name.split()[1]]
        elif name.startswith("I414B") or name.startswith("I414A"):
            kind = "elbow"
            geo = dict(bend_radius_m=measured["tori"][0][6],
                       od_m=max(t[7] for t in measured["tori"]) * 2,
                       id_m=min(t[7] for t in measured["tori"]) * 2,
                       angle_rad=dims["D1@Revolve1"])
            aliases = ["Sch10", "SR" if name.startswith("I414B") else "LR", "90 degree"]
        elif name == "F4SQ534X3":
            kind = "flange"
            geo = dict(square_m=dims["D1@Sketch1"], bore_m=dims["D1@Sketch2"],
                       thickness_m=dims["D1@Boss-Extrude1"],
                       slot_circle_min_m=dims["D2@Sketch3"], slot_circle_max_m=dims["D3@Sketch3"],
                       bolt_slot_width_m=dims["D4@Sketch3"])
            aliases = ["Akron 3", "Elkhart 3", "Hale 3", "4 inch pipe", "square valve flange"]
        elif name == "I414K8X6":
            kind = "reducer"
            geo = dict(length_m=dims["D1@Sketch1"], small_od_m=dims["D5@Sketch1"],
                       large_od_m=dims["D6@Sketch1"], shell_thickness_m=dims["D1@Shell1"])
            aliases = ["8 x 6", "concentric", "Sch10"]
        elif name == "CAST0019":
            kind = "pipe_end"
            aliases = ["6 inch", "NST", "3.50 inch long", "simplified thread"]
            ys = [p[4] for p in measured["planes"] if abs(p[1]) > .999999]
            geo = dict(length_m=max(ys) - min(ys))
        elif name in ("T4HCM34", "T4HCM3"):
            kind = "half_coupling"
            aliases = ["3/4 inch" if name.endswith("34") else "3 inch", "NPT", "MSS304"]
            zs = [p[5] for p in measured["planes"] if abs(p[2]) > .999999]
            geo = dict(length_m=max(zs) - min(zs))
        description = measured["properties"]["Description"]["evaluated"]
        entries.append({"part_number": name, "path": str(file), "sha256": expected,
                        "kind": kind, "configuration": "Default", "verification": "native_verified",
                        "material": measured["material"], "material_database": measured["material_database"],
                        "description": description, "aliases": aliases, "geometry": geo,
                        "properties": measured["properties"],
                        "other_configurations_not_measured": [c for c in measured["known_configurations"] if c != "Default"],
                        "provenance": {"job": "16186-262-06", "verified_local_date": "2026-10-15",
                                       "native_measurement": str(native_log.relative_to(PROJECT)),
                                       "native_measurement_sha256": sha256(native_log),
                                       "completed_audit": str(audit_path.relative_to(PROJECT)),
                                       "completed_audit_sha256": sha256(audit_path)},
                        "flags": ["ERP Material ID missing"] if not measured["properties"]["Material ID"]["raw"] else []})
    entries.sort(key=lambda e: natural_key(e["part_number"]))
    result = {"schema_version": 1, "generated_utc": datetime.now(timezone.utc).isoformat(),
              "units": "meters and radians; fields identify physical quantity",
              "scope": "Verified local shortlist, not the complete SPS/PDM catalog. Unmeasured configurations are not interchangeable.",
              "flange_policy": "PDM dimensional search is still required; cached matches do not establish absence of a better candidate.",
              "entries": entries}
    INDEX.parent.mkdir(parents=True, exist_ok=True)
    INDEX.write_text(json.dumps(result, indent=2) + "\n")
    with INDEX.with_suffix(".csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=["part_number", "configuration", "kind", "material", "description", "geometry", "path", "sha256", "flags"])
        writer.writeheader()
        for e in entries:
            row = {key: e[key] for key in writer.fieldnames}
            row["geometry"] = json.dumps(row["geometry"], sort_keys=True)
            row["flags"] = "; ".join(row["flags"])
            writer.writerow(row)
    return result


def search(entries, text="", kind=None, configuration=None, dimensions=None, tolerance_m=.000254):
    dimensions = dimensions or {}
    if tolerance_m < 0 or not math.isfinite(tolerance_m):
        raise ValueError("Tolerance must be finite and nonnegative")
    tokens = text.casefold().split()
    result = []
    for e in entries:
        haystack = " ".join([e["part_number"], e["description"], *e["aliases"]]).casefold()
        if kind and e["kind"] != kind or configuration and e["configuration"] != configuration:
            continue
        if any(t not in haystack for t in tokens):
            continue
        if any(k not in e["geometry"] or abs(e["geometry"][k] - value) > tolerance_m for k, value in dimensions.items()):
            continue
        result.append(e)
    return sorted(result, key=lambda e: natural_key(e["part_number"]))


def flange_rule(flange_id_m, pipe_od_m):
    if not all(math.isfinite(n) and n > 0 for n in (flange_id_m, pipe_od_m)):
        raise ValueError("Positive actual diameters are required")
    return {"connection": "slip_on" if flange_id_m > pipe_od_m else "butt_weld",
            "pipe_projection_m": .00635 if flange_id_m > pipe_od_m else 0,
            "preserve_pipe_placement_and_length": True}


def reuse_signature(component):
    """Require all relevant geometry/property inputs; length alone is insufficient."""
    keys = ("template_sha256", "configuration", "material", "dimensions", "end_features", "openings", "properties")
    missing = [k for k in keys if k not in component]
    if missing:
        raise ValueError("Incomplete reuse identity: " + ", ".join(missing))
    return hashlib.sha256(json.dumps({k: component[k] for k in keys}, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("build-index")
    sub.add_parser("check-index")
    query = sub.add_parser("search")
    query.add_argument("text", nargs="?", default="")
    query.add_argument("--kind")
    query.add_argument("--configuration")
    for dim in ("od", "id", "bore", "square", "thickness"):
        query.add_argument("--" + dim + "-in", type=float)
    query.add_argument("--tolerance-in", type=float, default=.01)
    query.add_argument("--brief", action="store_true")
    args = parser.parse_args()
    if args.command == "build-index":
        print(f"Indexed {len(build_index()['entries'])} hash-matched, measured configurations")
        return
    data = json.loads(INDEX.read_text())
    checked = [(e, freshness(e)) for e in data["entries"]]
    if args.command == "check-index":
        report = [{"part_number": e["part_number"], "configuration": e["configuration"], "status": status} for e, status in checked]
        print(json.dumps(report, indent=2))
        if any(status != "current" for _, status in checked):
            raise SystemExit(2)
    else:
        dimensions = {dim + "_m": getattr(args, dim + "_in") * .0254 for dim in ("od", "id", "bore", "square", "thickness") if getattr(args, dim + "_in") is not None}
        candidates = search([e for e, status in checked if status == "current"], args.text, args.kind, args.configuration, dimensions, args.tolerance_in * .0254)
        matches = candidates if not args.brief else [{k: e[k] for k in ("part_number", "configuration", "kind", "geometry", "flags")} for e in candidates]
        print(json.dumps({"matches": matches,
                          "excluded": [{"part_number": e["part_number"], "reason": status} for e, status in checked if status != "current"],
                          "notice": "Cached shortlist only. Search PDM by dimensions for flanges; zero matches does not establish library absence."}, indent=2))


if __name__ == "__main__":
    main()
