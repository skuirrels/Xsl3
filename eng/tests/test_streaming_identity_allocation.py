"""P7F must reject incomplete evidence and enforce both independent targets."""
import importlib.util
from pathlib import Path
import unittest

PATH = Path(__file__).resolve().parents[1] / 'compare-identity-allocation.py'
SPEC = importlib.util.spec_from_file_location('identity_comparison', PATH)
COMPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPARE)


class IdentityAllocationTests(unittest.TestCase):
    def runs(self):
        return [dict(input_bytes=100, variant=variant, kind=kind, round=number, valid=True,
                     allocated_bytes=1000 if variant == 'baseline' else 750,
                     elapsed_ms=10 if variant == 'baseline' else 9)
                for variant in ('baseline', 'candidate') for kind in ('control', 'instrumented')
                for number in range(1, 4)]

    def summary(self, runs):
        return COMPARE.summarize(runs, [100], 3, 0.2, 1.05)[0]

    def test_valid_evidence_requires_both_allocation_and_time_targets(self):
        result = self.summary(self.runs())
        self.assertTrue(result['passed'])
        self.assertEqual(0.75, result['allocation_ratio'])
        self.assertEqual(0.9, result['time_ratio'])
        for field, value in [('allocated_bytes', 900), ('elapsed_ms', 11)]:
            runs = self.runs()
            for run in runs:
                if run['variant'] == 'candidate':
                    run[field] = value
            self.assertFalse(self.summary(runs)['passed'])

    def test_missing_duplicate_extra_and_invalid_runs_cannot_pass(self):
        runs = self.runs()
        for invalid in (runs[:-1], runs+[runs[0]], runs[:-1]+[runs[0]]):
            with self.assertRaises(ValueError):
                self.summary(invalid)
        runs = self.runs();runs[0]['valid'] = False
        with self.assertRaises(ValueError):
            self.summary(runs)

    def test_nonfinite_or_mistyped_measurements_cannot_pass(self):
        for field, value in [('elapsed_ms', float('nan')), ('elapsed_ms', float('inf')),
                             ('elapsed_ms', True), ('allocated_bytes', True), ('allocated_bytes', 0)]:
            runs = self.runs()
            for run in runs:
                run[field] = value
            with self.assertRaises(ValueError):
                self.summary(runs)

    def test_instrumented_timings_are_not_used_for_throughput(self):
        runs = self.runs()
        for run in runs:
            if run['kind'] == 'instrumented':
                run['elapsed_ms'] = 100000
        self.assertTrue(self.summary(runs)['passed'])


if __name__ == '__main__':
    unittest.main()
