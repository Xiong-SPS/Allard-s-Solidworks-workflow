"""Synthetic files test evidence validation; they are not CAD validation fixtures."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from completion import finish, format_literal_length
from test_component_plan import fixture
from workflow import sha256


class CompletionTests(unittest.TestCase):
    def test_length_values_compare_at_three_decimal_display_precision(self):
        self.assertEqual(format_literal_length("1.9375 in"), "1.938 in")
        self.assertEqual(format_literal_length("2.0000"), "2")
        self.assertEqual(format_literal_length('10.452123 mm'), '10.452 mm')
        self.assertEqual(format_literal_length('"D1@Boss-Extrude1"'), '"D1@Boss-Extrude1"')

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=Path(__file__).parent)
        self.root = Path(self.temp.name).resolve()
        self.assertTrue(self.root.is_relative_to(Path(__file__).resolve().parents[1]))
        self.addCleanup(self.temp.cleanup)
        self.plan = fixture()
        self.path = self.root / "component-plan-v1.json"
        self.path.write_text(json.dumps(self.plan))
        self.plan_hash = sha256(self.path)
        self.packet = dict(plan_sha256=self.plan_hash, bom_created=False, batch_used=False, parts={}, reviews={})
        self.hashes = {}
        self.components = []
        for i, part in enumerate(self.plan["parts"]):
            native = self.root / part["file"]
            native.write_bytes(b"SYNTHETIC UNIT TEST ONLY")
            self.hashes[part["id"]] = sha256(native)
            receipt = dict(ProtocolVersion=1, Status="passed", Saved=True, Closed=True, File=str(native), FileSha256=sha256(native),
                           Expected=dict(PlanSha256=self.plan_hash, PartId=part["id"]), Snapshot=dict(Configuration="Default"),
                           Properties={k:dict(Raw=v) for k,v in part["properties"].items()}, Warnings=[])
            self.packet["parts"][part["id"]] = self.save(part["id"] + ".json", receipt)
            self.components.append(dict(File=str(native), FileSha256=sha256(native), Configuration="Default", Transform=self.plan["occurrences"][i]["transform"]))
        self.assembly = self.root / "DEMO.SLDASM"
        self.assembly.write_bytes(b"SYNTHETIC ASSEMBLY UNIT TEST ONLY")
        self.asm_receipt = dict(ProtocolVersion=1, Status="passed", Saved=True, MainAssemblyOpen=True, File=str(self.assembly),
                                FileSha256=sha256(self.assembly), PlanSha256=self.plan_hash, Components=self.components)
        self.packet["assembly"] = self.save("assembly.json", self.asm_receipt)
        for name in ("drawing_dimensions", "connection_rules", "visual_review"):
            self.packet["reviews"][name] = self.save(name + ".json", dict(status="passed", plan_sha256=self.plan_hash,
                assembly_sha256=sha256(self.assembly), part_hashes=self.hashes, reviewer="synthetic test", findings="Synthetic validator test only"))

    def save(self, name, data):
        p = self.root / name
        p.write_text(json.dumps(data))
        return dict(path=name, sha256=sha256(p))

    def audit(self):
        return finish(self.path, self.packet, entries=[])

    def test_current_evidence_complete_with_unknown_metadata_flags(self):
        self.assertEqual(self.audit()["status"], "complete_with_flags")
        self.assertTrue(self.audit()["warnings"])

    def test_missing_review_blocks_completion(self):
        del self.packet["reviews"]["visual_review"]
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_changed_native_file_invalidates_verification(self):
        (self.root / "DEMO-01.SLDPRT").write_bytes(b"changed after native check")
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_changed_plan_invalidates_packet(self):
        self.plan["parts"][0]["properties"]["Customer"] = "new customer"
        self.path.write_text(json.dumps(self.plan))
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_part_must_have_closed_verified_receipt(self):
        p = self.root / "p1.json"
        r = json.loads(p.read_text())
        r["Closed"] = False
        self.packet["parts"]["p1"] = self.save("p1.json", r)
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_assembly_wrong_order_or_placement_rejected(self):
        self.asm_receipt["Components"].reverse()
        self.packet["assembly"] = self.save("assembly.json", self.asm_receipt)
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_stale_evidence_hash_rejected(self):
        (self.root / "visual_review.json").write_text("{}")
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_missing_assembly_never_complete(self):
        self.assembly.unlink()
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_known_property_must_match_verified_part(self):
        p = self.root / "p1.json"
        r = json.loads(p.read_text())
        r["Properties"]["MakeOrBuy"]["Raw"] = "B"
        self.packet["parts"]["p1"] = self.save("p1.json", r)
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_existing_bom_needs_separate_order_review(self):
        self.packet["bom_created"] = True
        self.assertEqual(self.audit()["status"], "incomplete")

    def test_incomplete_batch_cannot_complete_job(self):
        self.packet["batch_used"] = True
        self.packet["batch"] = self.save("batch.json", dict(Status="incomplete", PlanSha256=self.plan_hash,
            Parts=[dict(Id="p1", Status="passed"), dict(Id="p2", Status="failed")]))
        self.assertEqual(self.audit()["status"], "incomplete")


if __name__ == "__main__":
    unittest.main()
