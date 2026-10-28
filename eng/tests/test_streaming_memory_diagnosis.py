"""Diagnostic evidence must reject incomplete or inconsistent phase snapshots."""
import importlib.util
import hashlib
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock

PATH = Path(__file__).resolve().parents[1] / 'diagnose-streaming-memory.py'
SPEC = importlib.util.spec_from_file_location('memory_diagnosis', PATH)
DIAG = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DIAG)


class MemoryDiagnosisTests(unittest.TestCase):
    def test_executable_fingerprint_does_not_allocate_the_entire_image(self):
        data = bytes(range(256)) * 8193
        class BoundedReader(io.BytesIO):
            def read(self, size=-1):
                if size < 0 or size > 1024 * 1024:
                    raise AssertionError('Unbounded executable hashing contaminates launcher RSS')
                return super().read(size)
        path = Mock()
        path.open.return_value = BoundedReader(data)
        self.assertEqual(hashlib.sha256(data).hexdigest(), DIAG.sha256_file(path))

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

    def test_duplicate_sizes_and_nonfinite_timeouts_cannot_overwrite_or_hang_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            child = root / 'must-not-run'
            child.touch()
            for sizes, timeout in [('1024 1024', '1'), ('1024', 'nan'), ('1024', 'inf')]:
                output = root / 'report'
                result = subprocess.run([sys.executable, str(PATH), 'measure', '--native', str(child),
                    '--output', str(output), '--sizes', *sizes.split(), '--rounds', '1',
                    '--heap-limit', '1024', '--timeout-seconds', timeout], capture_output=True, timeout=10)
                self.assertEqual(2, result.returncode)
                self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
