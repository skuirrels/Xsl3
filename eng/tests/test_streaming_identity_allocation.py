"""P7F must reject incomplete evidence and enforce both independent targets."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest

PATH = Path(__file__).resolve().parents[1] / 'compare-identity-allocation.py'
SPEC = importlib.util.spec_from_file_location('identity_comparison', PATH)
COMPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPARE)


class IdentityAllocationTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform in ('darwin', 'linux') and hasattr(os, 'wait4'), 'requires the process diagnostic platform')
    def test_comparison_invokes_real_diagnostics_and_retains_every_failed_child(self):
        # A failing executable exercises the real caller/controller boundary without
        # fabricating successful native measurements or allocation/throughput results.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            image = root / 'failing-probe'
            image.write_text('#!/bin/sh\nexit 7\n')
            image.chmod(0o700)
            args = SimpleNamespace(output=root / 'comparison', sizes=[512], rounds=3, heap_limit=1024,
                minimum_allocation_reduction=0.2, maximum_slowdown=1.0, timeout_seconds=5,
                baseline_native=image, candidate_native=image, baseline_diagnostic=image, candidate_diagnostic=image)
            self.assertEqual(1, COMPARE.compare(args))
            report = json.loads((args.output / 'report.json').read_text())
            self.assertTrue(report['complete'])
            self.assertFalse(report['passed'])
            self.assertFalse(report['memory_acceptance'])
            self.assertEqual(12, len(report['runs']))
            self.assertEqual(12, len(report['failures']))
            for run in report['runs']:
                self.assertFalse(run['valid'])
                child = json.loads((args.output / run['evidence']).read_text())
                self.assertTrue(child['complete'])
                self.assertEqual('identity', child['workload'])
                self.assertFalse(child['loader_details'])
                self.assertFalse(child['mapping_snapshots_requested'])
                self.assertEqual(run['kind'] == 'instrumented', child['instrumented'])
                self.assertEqual(7, child['runs'][0]['exit_code'])
                self.assertEqual(1, len(child['failures']))

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
