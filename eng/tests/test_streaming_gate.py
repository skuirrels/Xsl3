#!/usr/bin/env python3
"""Exercise the process gate's failure handling without running an XML engine."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

GATE = Path(__file__).resolve().parents[1] / 'streaming-gate.py'

# This deliberately untrusted child lets the controller be tested independently
# of the engine/oracle. It may claim success while corrupting or omitting evidence.
PROBE = '''#!/usr/bin/env python3
import json, os, sys, time
workload, mode, size, limit = sys.argv[-4:]
size, limit = int(size), int(limit)
case = os.environ['STREAM_PROBE_TEST_CASE']
if case == 'crash' and size == 4096:
    sys.exit(1)
result = dict(Workload=workload, Mode=mode, Status='ok', ValidOutput=True,
              RequestedInputBytes=size, InputBytes=size, LimitBytes=limit,
              PeakWorkingSetBytes=1, PeakMemorySource='test counter', LiveManagedBytes=0,
              AvailableManagedBytes=limit or 1048576, DynamicCodeSupported=len(sys.argv) == 7,
              HasTrustedPlatformAssemblies=len(sys.argv) == 7,
              Records=1, ElapsedMs=2, FirstOutputMs=1, InputBytesAtFirstOutput=1,
              Outputs={'principal': dict(Bytes=1, Sha256='A' * 64)})
if case == 'jit-native':
    result['DynamicCodeSupported'] = True
if case == 'disabled-dynamic-jit':
    result['DynamicCodeSupported'] = False
    result['HasTrustedPlatformAssemblies'] = True
if case == 'missing-host-evidence':
    del result['HasTrustedPlatformAssemblies']
if case == 'ignored-heap-limit':
    result['AvailableManagedBytes'] = limit + 1
if case == 'live-heap-exceeds-limit' and size == 4096:
    result['LiveManagedBytes'] = limit + 1
if case == 'missing-output' and size == 4096:
    del result['Outputs']
if case == 'nan-time' and size == 4096:
    result['ElapsedMs'] = float('nan')
if case == 'boolean-count' and size == 4096:
    result['InputBytes'] = True
if case == 'wrong-mode' and size == 4096:
    result['Mode'] = 'tree'
if case == 'bad-hash' and size == 4096:
    result['Outputs']['principal']['Sha256'] = 'not a SHA-256 hash'
if case == 'mismatch' and size == 1024 and mode == 'stream':
    result['Outputs']['principal']['Sha256'] = 'B' * 64
print(json.dumps(result), flush=True)
if case == 'timeout' and size == 4096:
    while True: time.sleep(60)
'''


@unittest.skipUnless(sys.platform in ('linux', 'darwin') and hasattr(os, 'wait4'), 'controller requires Unix wait4')
class StreamingGateTests(unittest.TestCase):
    def run_gate(self, case, limit=0, native=False, timeout=0):
        with tempfile.TemporaryDirectory(prefix='streaming-gate-test-') as directory:
            root = Path(directory)
            probe = root / 'probe'
            probe.write_text(PROBE)
            probe.chmod(0o700)
            assembly = root / 'unused.dll'
            assembly.touch()
            env = dict(os.environ, STREAM_PROBE_TEST_CASE=case)
            source = ['--native', str(probe)] if native else ['--assembly', str(assembly), '--dotnet', str(probe)]
            result = subprocess.run([sys.executable, str(GATE), *source, '--bytes', '4096', '--limit', str(limit),
                                     '--baseline-bytes', '1024', '--workloads', 'identity', '--timeout-seconds', str(timeout),
                                     '--output', str(root / 'report')], env=env, capture_output=True,
                                    text=True, timeout=30)
            return result, json.loads((root / 'report' / 'report.json').read_text())

    def test_valid_complete_matrix_passes(self):
        result, report = self.run_gate('valid')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(report['complete'])
        self.assertEqual(3, len(report['runs']))
        self.assertFalse(report['failures'])

    def test_missing_corrupt_or_failed_evidence_cannot_pass(self):
        for case in ('missing-output', 'nan-time', 'boolean-count', 'wrong-mode', 'bad-hash', 'mismatch', 'crash', 'missing-host-evidence'):
            with self.subTest(case=case):
                result, report = self.run_gate(case)
                self.assertNotEqual(0, result.returncode)
                self.assertTrue(report['complete'])
                self.assertTrue(report['failures'])

    def test_native_command_works_and_rejects_a_jit_process(self):
        result, report = self.run_gate('valid', native=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(all(run['passed'] for run in report['runs']))
        for case in ('jit-native', 'disabled-dynamic-jit'):
            with self.subTest(case=case):
                result, report = self.run_gate(case, native=True)
                self.assertNotEqual(0, result.returncode)
                self.assertTrue(report['failures'])

    def test_an_ignored_heap_cap_cannot_pass(self):
        result, report = self.run_gate('ignored-heap-limit', limit=1024 * 1024 * 1024)
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(report['failures'])

    def test_reported_live_heap_cannot_exceed_the_verified_cap(self):
        result, report = self.run_gate('live-heap-exceeds-limit', limit=1024 * 1024 * 1024)
        measured = report['runs'][-1]
        self.assertEqual('ok', measured['result']['Status'])
        self.assertEqual(measured['result']['LimitBytes'], measured['result']['AvailableManagedBytes'])
        self.assertGreater(measured['result']['LiveManagedBytes'], measured['result']['LimitBytes'])
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(measured['passed'])
        self.assertTrue(report['failures'])

    def test_a_hung_child_fails_even_after_printing_valid_json(self):
        result, report = self.run_gate('timeout', timeout=1)
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(report['complete'])
        self.assertEqual(3, len(report['runs']))
        measured = report['runs'][-1]
        self.assertTrue(measured['timedOut'])
        self.assertFalse(measured['passed'])
        self.assertGreater(measured['outerPeakWorkingSetBytes'], 0)
        self.assertEqual('ok', measured['result']['Status'])
        self.assertTrue(any('wall-time guard' in failure for failure in report['failures']))

    def test_outer_counter_enforces_budget_even_when_child_claims_one_byte(self):
        result, report = self.run_gate('valid', limit=1)
        self.assertNotEqual(0, result.returncode)
        measured = report['runs'][-1]
        self.assertEqual(1, measured['result']['PeakWorkingSetBytes'])
        self.assertGreater(measured['outerPeakWorkingSetBytes'], 1)
        self.assertTrue(report['failures'])


if __name__ == '__main__':
    unittest.main()
