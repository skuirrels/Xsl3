"""Diagnostic evidence must reject incomplete or inconsistent phase snapshots."""
import importlib.util
from pathlib import Path
import unittest

PATH = Path(__file__).resolve().parents[1] / 'diagnose-streaming-memory.py'
SPEC = importlib.util.spec_from_file_location('memory_diagnosis', PATH)
DIAG = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DIAG)


class MemoryDiagnosisTests(unittest.TestCase):
    def points(self):
        return [dict(Phase=name, PeakBytes=100+i, AllocatedBytes=1000+i,
                     LiveBytes=10, CommittedBytes=20, HeapSizeBytes=15,
                     FragmentedBytes=1, AvailableBytes=1024, LinuxRollup=None)
                for i, name in enumerate(DIAG.PHASES)]

    def test_complete_controlled_phases_are_valid(self):
        self.assertTrue(DIAG.validate_points(self.points(), 1024))

    def test_missing_extra_reordered_or_non_object_phases_are_rejected(self):
        points = self.points()
        for invalid in (points[:-1], points+[None], list(reversed(points)), [None]+points[1:], None):
            with self.subTest(invalid=invalid):
                self.assertFalse(DIAG.validate_points(invalid, 1024))

    def test_counter_types_missing_values_and_unenforced_heap_cap_are_rejected(self):
        for field, value in [('PeakBytes', 0), ('AllocatedBytes', -1), ('LiveBytes', True),
                             ('AvailableBytes', 1025), ('LinuxRollup', []), ('CommittedBytes', None)]:
            points = self.points();points[0][field] = value
            with self.subTest(field=field):
                self.assertFalse(DIAG.validate_points(points, 1024))
        points = self.points();del points[0]['LiveBytes']
        self.assertFalse(DIAG.validate_points(points, 1024))

    def test_cumulative_process_counters_cannot_go_backwards(self):
        for field in ('PeakBytes', 'AllocatedBytes'):
            points = self.points();points[-1][field] = 1
            self.assertFalse(DIAG.validate_points(points, 1024))

    def test_source_patch_cannot_silently_target_missing_or_ambiguous_code(self):
        self.assertEqual('axb', DIAG.replace_once('acb', 'c', 'x'))
        for text in ('ab', 'accb'):
            with self.assertRaises(ValueError):
                DIAG.replace_once(text, 'c', 'x')


if __name__ == '__main__':
    unittest.main()
