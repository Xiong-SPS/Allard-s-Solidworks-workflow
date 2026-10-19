import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("workflow", Path(__file__).resolve().parents[1] / "workflow.py")
w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)


class WorkflowTests(unittest.TestCase):
    def test_configuration_geometry_is_not_interchangeable(self):
        entries = [dict(part_number="F4SQ534X3", configuration=cfg, kind="flange", description="Akron", aliases=[], geometry={"thickness_m": thickness}) for cfg, thickness in [("Default", .0127), ("Finished flange", .011938)]]
        result = w.search(entries, dimensions={"thickness_m": .0127}, tolerance_m=.0001)
        self.assertEqual([e["configuration"] for e in result], ["Default"])

    def test_changed_and_missing_files_cannot_be_current(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            self.assertTrue(Path(directory).resolve().is_relative_to(w.ROOT.resolve()))
            path = Path(directory) / "part.SLDPRT"
            path.write_bytes(b"verified geometry")
            entry = {"path": str(path), "sha256": w.sha256(path)}
            self.assertEqual(w.freshness(entry), "current")
            path.write_bytes(b"different geometry")
            self.assertEqual(w.freshness(entry), "changed")
            path.unlink()
            self.assertEqual(w.freshness(entry), "not_cached")

    def test_equal_flange_id_uses_butt_weld(self):
        self.assertEqual(w.flange_rule(.1143, .1143)["connection"], "butt_weld")
        result = w.flange_rule(.115, .1143)
        self.assertEqual(result["pipe_projection_m"], .00635)
        self.assertTrue(result["preserve_pipe_placement_and_length"])

    def test_equal_stock_length_does_not_mean_identical_part(self):
        a = dict(template_sha256="verified", configuration="Default", material="304/304L", dimensions={"stock_length_m": .1}, end_features=["plain", "cope"], openings=[], properties={"Material ID": "stock"})
        b = copy.deepcopy(a)
        b["openings"] = [{"kind": "double_d", "station_m": .05}]
        self.assertNotEqual(w.reuse_signature(a), w.reuse_signature(b))
        del b["end_features"]
        with self.assertRaises(ValueError):
            w.reuse_signature(b)

    def test_all_seed_entries_have_hash_bound_measurement_provenance(self):
        data = json.loads(w.INDEX.read_text())
        # The verified shortlist grows with new native evidence; keep the seed
        # minimum while checking provenance/freshness for every added entry.
        self.assertGreaterEqual(len(data["entries"]), 13)
        for entry in data["entries"]:
            self.assertEqual(entry["verification"], "native_verified")
            self.assertEqual(entry["configuration"], "Default")
            self.assertTrue(entry["provenance"]["native_measurement_sha256"])
            self.assertEqual(w.freshness(entry), "current")


if __name__ == "__main__":
    unittest.main()
