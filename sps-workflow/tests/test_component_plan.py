import copy
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from component_plan import check, template, valid_transform
from workflow import REQUIRED_PROPERTIES, sha256

HERE = Path(__file__).resolve()
T = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]


def fixture():
    p = template("DEMO")
    p["sources"] = [dict(id="drawing", path=str(HERE), sha256=sha256(HERE))]
    p["pieces"] = [dict(id="piece1", source="drawing", root="fused-root", drawing_item="1"),
                   dict(id="piece2", source="drawing", root="fused-root", drawing_item="1")]
    props = {k: "" for k in REQUIRED_PROPERTIES}
    props["MakeOrBuy"] = "M"
    part = dict(id="p1", file="DEMO-01.SLDPRT", mode="new", kind="pipe", quantity=1,
                library_search_evidence="Synthetic test: no production part", properties=props,
                identity_reviewed=True, reuse_identity=dict(template_sha256="new-native-recipe-v1", configuration="Default",
                material="304", dimensions=dict(stock_length_m=.1, od_m=.06, wall_m=.003),
                end_features=[dict(type="plain")], openings=[], properties=dict(schedule=10)))
    p["parts"] = [part, dict(copy.deepcopy(part), id="p2", file="DEMO-02.SLDPRT")]
    p["occurrences"] = [dict(id="o1", part="p1", piece="piece1", transform=T.copy(), placement_basis="source datum"),
                          dict(id="o2", part="p2", piece="piece2", transform=T.copy(), placement_basis="source datum")]
    return p


class ComponentPlanTests(unittest.TestCase):
    def audit(self, p):
        return check(p, HERE.parent, entries=[])

    def test_direct_library_order_uses_part_number_not_folder(self):
        p = fixture()
        with patch("component_plan.freshness", return_value="current"):
            entries = []
            for part, folder, filename in zip(p["parts"], ("Z-flanges", "A-elbows"),
                                               ("F4OEM10.SLDPRT", "I414A2.SLDPRT")):
                path = HERE.parent / folder / filename
                digest = ("a" if part["id"] == "p1" else "b") * 64
                part.update(mode="direct", file=str(path), kind="library_component",
                            library=dict(path=str(path), configuration="Default", sha256=digest))
                part["reuse_identity"].update(template_sha256=digest, configuration="Default")
                entries.append(dict(path=str(path), configuration="Default", sha256=digest,
                                    verification="native_verified", kind="library_component"))
            report = check(p, HERE.parent, entries=entries)
            self.assertEqual(report["status"], "ready")
            self.assertEqual([row["id"] for row in report["parts"]], ["p1", "p2"])

    def test_fused_root_maps_two_manufactured_pieces(self):
        r = self.audit(fixture())
        self.assertEqual(r["status"], "ready")
        self.assertEqual(r["reuse_candidates"], [["p1", "p2"]])

    def test_equal_length_different_opening_not_reused(self):
        p = fixture()
        p["parts"][1]["reuse_identity"]["openings"] = [dict(type="round", diameter_m=.02, local_center_m=[.02, 0, 0])]
        self.assertEqual(self.audit(p)["reuse_candidates"], [])

    def test_position_does_not_change_reuse(self):
        p = fixture()
        p["occurrences"][1]["transform"][9] = 4
        self.assertEqual(self.audit(p)["reuse_candidates"], [["p1", "p2"]])

    def test_incomplete_identity_cannot_suggest_reuse(self):
        p = fixture()
        del p["parts"][0]["reuse_identity"]["openings"]
        r = self.audit(p)
        self.assertEqual(r["reuse_candidates"], [])
        self.assertEqual(r["parts"][0]["status"], "blocked")

    def test_conflict_blocks_only_affected_part(self):
        p = fixture()
        p["conflicts"] = [dict(parts=["p1"], description="drawing conflicts with source")]
        self.assertEqual([r["status"] for r in self.audit(p)["parts"]], ["blocked", "ready"])
        p["conflicts"][0].update(resolution="use drawing", authorization_basis="explicit user instruction")
        self.assertEqual(self.audit(p)["status"], "ready")

    def test_transform_rejects_reflection_scale_nan(self):
        for index, value in ((0, -1), (12, 2), (9, float("nan")), (15, 1)):
            t = T.copy()
            t[index] = value
            self.assertFalse(valid_transform(t))

    def test_missing_source_or_duplicate_piece_blocks(self):
        p = fixture()
        p["sources"][0]["sha256"] = "stale"
        self.assertEqual(self.audit(p)["status"], "blocked")
        p = fixture()
        p["occurrences"][1]["piece"] = "piece1"
        self.assertIn("piece_coverage", [i["code"] for i in self.audit(p)["issues"]])

    def test_unmeasured_configuration_not_ready(self):
        p = fixture()
        p["parts"][0].update(mode="copy", library=dict(path="missing.SLDPRT", configuration="Finished", sha256="bad"))
        self.assertIn("library_evidence", [i["code"] for i in self.audit(p)["issues"]])

    def test_unknown_metadata_warns_and_missing_make_or_buy_blocks(self):
        p = fixture()
        r = self.audit(p)
        self.assertTrue(any(i["code"] == "unknown_property" for i in r["issues"]))
        self.assertEqual(r["status"], "ready")
        p["parts"][0]["properties"]["MakeOrBuy"] = ""
        self.assertEqual(self.audit(p)["parts"][0]["status"], "blocked")

    def test_quantity_and_natural_order(self):
        p = fixture()
        p["parts"][0]["quantity"] = 3
        p["parts"][0]["file"] = "DEMO-10.SLDPRT"
        r = self.audit(p)
        self.assertEqual([x["id"] for x in r["parts"]], ["p2", "p1"])
        self.assertIn("quantity", [i["code"] for i in r["issues"]])

    def test_empty_template_blocks(self):
        self.assertEqual(self.audit(template("NEW"))["status"], "blocked")


if __name__ == "__main__":
    unittest.main()
