import copy
import json
import math
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from efficiency import delta, sha, compact, effort
from geometry import (source_axis_radius_point, two_arc_reducer_radius,
                      reducer_cylinder_guide, bolt_centers)


class DeltaTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.native = self.base/'J-01.SLDPRT'; self.native.write_bytes(b'verified fixture')
        source = self.base/'source'; source.write_bytes(b'preserved source')
        self.part = dict(id='p', file=self.native.name, mode='copy', identity_reviewed=True,
                         reuse_identity={'dimensions': {'length': 1}, 'openings': []})
        self.plan = dict(assembly='J', parts=[self.part], sources=[dict(path='source', sha256=sha(source))],
                         occurrences=[dict(id='o', part='p', transform=[1])])
        self.old = self.base/'v1.json'; self.new = self.base/'v2.json'
        self.write(self.old, self.plan); self.write(self.new, self.plan)
        self.receipt = Path(str(self.native)+'.verification.json')
        self.record = dict(Status='passed', Saved=True, Closed=True, File=str(self.native),
                           FileSha256=sha(self.native), Expected=dict(PlanFile=str(self.old),
                           PlanSha256=sha(self.old), PartId='p'))
        self.write(self.receipt, self.record)

    def write(self, path, value): path.write_text(json.dumps(value), encoding='utf-8')
    def state(self): return delta(self.old, self.new)['parts'][0]['status']

    def test_unchanged_and_placement_only(self):
        before = self.receipt.read_bytes()
        self.plan['occurrences'][0]['transform'] = [2]
        self.write(self.new, self.plan)
        report = delta(self.old, self.new)
        self.assertEqual(self.state(), 'retain_candidate')
        self.assertTrue(report['assembly_recheck_required'])
        self.assertTrue(report['assembly_context_changed'])
        self.assertEqual(before, self.receipt.read_bytes())

    def test_same_length_changed_opening_is_not_reused(self):
        self.part['reuse_identity']['openings'] = [{'diameter': .2}]
        self.write(self.new, self.plan)
        self.assertEqual(self.state(), 'inspect')

    def test_changed_bytes_source_and_missing_receipt(self):
        self.native.write_bytes(b'changed')
        self.assertEqual(self.state(), 'inspect')
        self.native.write_bytes(b'verified fixture')
        (self.base/'source').write_bytes(b'changed')
        self.assertEqual(self.state(), 'inspect')
        (self.base/'source').write_bytes(b'preserved source')
        self.receipt.unlink()
        self.assertEqual(self.state(), 'inspect')

    def test_stale_binding_and_unsaved_rejected(self):
        for field, value in [('Saved', False), ('Closed', False), ('Status', 'failed')]:
            record = copy.deepcopy(self.record); record[field] = value
            self.write(self.receipt, record)
            self.assertEqual(self.state(), 'inspect')
        self.record['Expected']['PlanSha256'] = 'stale'
        self.write(self.receipt, self.record)
        self.assertEqual(self.state(), 'inspect')

    def test_duplicate_and_different_job_rejected(self):
        self.plan['parts'].append(copy.deepcopy(self.part)); self.write(self.new, self.plan)
        with self.assertRaises(ValueError): delta(self.old, self.new)
        self.plan['assembly'] = 'other'; self.write(self.new, self.plan)
        with self.assertRaises(ValueError): delta(self.old, self.new)

    def test_compact_bounded_failures(self):
        self.write(self.new, {'Parts': [{'Id': str(i), 'Status': 'failed', 'Error': 'x'*1000} for i in range(25)]})
        result = compact(self.new)
        self.assertEqual(result['counts']['failed'], 25)
        self.assertEqual(result['exceptions_omitted'], 5)
        self.assertEqual(len(result['exceptions'][0]['reason']), 500)

    def test_effort_aggregates_actual_events(self):
        events = [dict(stage='repair', seconds=60, calls=2, retries=1),
                  dict(stage='inspect', seconds=10, calls=1, retries=0),
                  dict(stage='repair', seconds=30, calls=1, retries=0)]
        (self.base/'effort-events.jsonl').write_text('\n'.join(json.dumps(e) for e in events))
        first = effort(self.base)['stages_by_elapsed_time'][0]
        self.assertEqual(first, dict(stage='repair', seconds=90, calls=3, retries=1, events=2))


class GeometryTests(unittest.TestCase):
    def test_source_axis_preserves_offset(self):
        p = source_axis_radius_point((.006417108, 1, 0), (0, 4, 0), (0, 0, 0), 3.2)
        self.assertAlmostEqual(p[0], .006417108)
        self.assertAlmostEqual(sum(x*x for x in p), 3.2**2)
        with self.assertRaises(ValueError): source_axis_radius_point((4, 0, 0), (0, 1, 0), (0, 0, 0), 3)

    def test_reducer_endpoints_and_transition_continuity(self):
        ro, rs, h, off = 2.7815, 2.25, 5, .067
        radius = (h*h+(ro-rs)**2)/(4*(ro-rs))
        f = lambda y: two_arc_reducer_radius(y, ro, rs, h, off)
        self.assertAlmostEqual(f(0), ro-off)
        self.assertAlmostEqual(f(h), rs-off)
        transition = h*(radius-off)/(2*radius)
        self.assertAlmostEqual(f(transition-1e-8), f(transition+1e-8), places=7)
        # Offset arc points must lie on the respective offset circle, not a radial taper approximation.
        y = 1.25
        self.assertAlmostEqual((f(y)-(ro-radius))**2+y*y, (radius-off)**2)

    def test_reducer_guide_on_both_surfaces(self):
        pts = reducer_cylinder_guide(2.7815, 2.25, 5, .067, 5.004, 2.19, (2.5, 5), 64)
        self.assertEqual(pts[0], pts[-1])
        for x, y, z in pts:
            self.assertAlmostEqual((y-5.004)**2+z*z, 2.19**2, places=10)
            self.assertAlmostEqual(math.hypot(x, z), two_arc_reducer_radius(y, 2.7815, 2.25, 5, .067))
        with self.assertRaises(ValueError):
            reducer_cylinder_guide(2.7815, 2.25, 5, .067, 5.004, 2.19, (0, .1))

    def test_explicit_bolt_count_and_clocking(self):
        pts = bolt_centers(4.375, 4, math.pi/4)
        self.assertEqual(len(pts), 4)
        self.assertAlmostEqual(pts[0][0], 4.375/2/math.sqrt(2))
        for x, y in pts: self.assertAlmostEqual(math.hypot(x, y), 4.375/2)


if __name__ == '__main__': unittest.main()
