#!/usr/bin/env python3
"""The benchmark controller must retain failures and never infer a winner from missing runs."""
import json
import hashlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'streaming-benchmark.py'
PROBE = '''#!/usr/bin/env python3
import json, os, sys, time
if sys.argv[-1] == 'streaming-info':
    print(json.dumps(dict(Framework='fixture runtime', Architecture='fixture architecture'))); sys.exit(0)
workload, mode, size, limit = sys.argv[-4:]
size = int(size)
case = os.environ['STREAM_BENCH_TEST_CASE']
result = dict(Workload=workload, Mode=mode, Status='ok', ValidOutput=True,
    RequestedInputBytes=size, InputBytes=size, LimitBytes=0,
    PeakWorkingSetBytes=1, PeakMemorySource='test counter', LiveManagedBytes=0,
    AvailableManagedBytes=int(os.environ.get('DOTNET_GCHeapHardLimit', '100000'), 16),
    DynamicCodeSupported=len(sys.argv) == 7, HasTrustedPlatformAssemblies=len(sys.argv) == 7,
    Records=1, ElapsedMs=4 if mode == 'tree' else 2, FirstOutputMs=1, InputBytesAtFirstOutput=1,
    Outputs={'principal': dict(Bytes=1, Sha256='A' * 64)})
if case == 'mismatch' and mode == 'stream': result['Outputs']['principal']['Sha256'] = 'B' * 64
if case == 'nan': result['ElapsedMs'] = float('nan')
if case == 'different-heap' and mode == 'stream': result['AvailableManagedBytes'] //= 2
if case == 'ignored-heap': result['AvailableManagedBytes'] += 1
if case == 'jit-native': result['HasTrustedPlatformAssemblies'] = True
if case == 'crash-tree' and mode == 'tree': sys.exit(1)
print(json.dumps(result), flush=True)
if case == 'timeout' and mode == 'tree': time.sleep(30)
'''


BOUNDED_READ_BOOTSTRAP = r'''import pathlib, runpy, sys
original_open = pathlib.Path.open
class BoundedBinary:
    def __init__(self, file): self.file = file
    def __enter__(self): return self
    def __exit__(self, *args): return self.file.__exit__(*args)
    def __getattr__(self, name): return getattr(self.file, name)
    def read(self, size=-1):
        if size < 0 or size > 1024 * 1024: raise AssertionError('unbounded binary read')
        return self.file.read(size)
    def readinto(self, buffer):
        if len(buffer) > 1024 * 1024: raise AssertionError('oversized binary read buffer')
        return self.file.readinto(buffer)
def bounded_open(path, mode='r', *args, **kwargs):
    file = original_open(path, mode, *args, **kwargs)
    return BoundedBinary(file) if mode == 'rb' else file
pathlib.Path.open = bounded_open
sys.argv = sys.argv[1:]
runpy.run_path(sys.argv[0], run_name='__main__')
'''


@unittest.skipUnless(sys.platform in ('darwin', 'linux') and hasattr(os, 'wait4'), 'requires Unix wait4')
class StreamingBenchmarkTests(unittest.TestCase):
    def run_benchmark(self, case='valid', native=False, extra=(), bounded_reads=False):
        with tempfile.TemporaryDirectory(prefix='streaming-benchmark-test-') as directory:
            root = Path(directory); probe = root / 'probe'; probe.write_text(PROBE); probe.chmod(0o700)
            assembly = root / 'unused.dll'; assembly.touch()
            (root / 'Xsl3.dll').write_bytes(b'engine fixture')
            source = ['--native', str(probe)] if native else ['--assembly', str(assembly), '--dotnet', str(probe)]
            command = [sys.executable, str(SCRIPT), *source, '--sizes', '1024', '--workloads', 'identity',
                       '--rounds', '2', '--output', str(root / 'report'), *extra]
            if bounded_reads:
                command = [sys.executable, '-c', BOUNDED_READ_BOOTSTRAP, *command[1:]]
            result = subprocess.run(command, env=dict(os.environ, STREAM_BENCH_TEST_CASE=case), capture_output=True, text=True, timeout=30)
            self.assertTrue((root / 'report/report.json').is_file(), result.stderr)
            report = json.loads((root / 'report/report.json').read_text())
            return result, report, (root / 'report/summary.md').read_text()

    def test_artifact_engine_and_controller_hashes_use_bounded_reads(self):
        for native in (False, True):
            with self.subTest(native=native):
                result, report, _ = self.run_benchmark(native=native, bounded_reads=True)
                self.assertEqual(0, result.returncode, result.stderr)
                expected = PROBE.encode() if native else b''
                self.assertEqual(hashlib.sha256(expected).hexdigest(), report['artifactSha256'])
                self.assertEqual(hashlib.sha256(SCRIPT.read_bytes()).hexdigest(), report['controllerSha256'])
                if not native:
                    self.assertEqual(hashlib.sha256(b'engine fixture').hexdigest(), report['engineSha256'])

    def test_complete_pairs_alternate_and_preserve_every_observation(self):
        result, report, text = self.run_benchmark()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(report['complete']); self.assertFalse(report['failures'])
        self.assertEqual(['tree', 'stream', 'stream', 'tree'], [t['mode'] for t in report['trials']])
        self.assertEqual([1, 1, 2, 2], [t['round'] for t in report['trials']])
        row = report['summary'][0]
        self.assertEqual(0.5, row['streamOverTree']); self.assertEqual('Stream observed median', row['winner'])
        self.assertEqual(4, row['tree']['medianMs']); self.assertEqual(2, row['stream']['medianMs'])
        self.assertIsNone(report['throughputTarget']['passed'])
        self.assertIn('Winner', text); self.assertIn('Tree MiB/s', text)

    def test_invalid_crashed_or_inconsistent_children_have_no_winner(self):
        for case in ['mismatch', 'nan', 'ignored-heap', 'different-heap', 'crash-tree']:
            with self.subTest(case=case):
                result, report, _ = self.run_benchmark(case)
                self.assertNotEqual(0, result.returncode); self.assertTrue(report['complete'])
                self.assertTrue(report['failures']); self.assertFalse(report['summary'][0]['complete'])
                self.assertEqual('No meaningful winner', report['summary'][0]['winner'])
                self.assertNotIn('streamOverTree', report['summary'][0])

    def test_native_evidence_is_required(self):
        result, report, _ = self.run_benchmark(native=True)
        self.assertEqual(0, result.returncode, result.stderr); self.assertTrue(report['complete'])
        result, report, _ = self.run_benchmark('jit-native', native=True)
        self.assertNotEqual(0, result.returncode); self.assertTrue(report['failures'])

    def test_timeout_is_terminal_and_retains_the_full_lifetime_counter(self):
        result, report, _ = self.run_benchmark('timeout', extra=['--timeout-seconds', '0.2'])
        self.assertNotEqual(0, result.returncode); self.assertTrue(report['complete'])
        self.assertTrue(any(t['timedOut'] and t['outerPeakWorkingSetBytes'] > 0 for t in report['trials']))
        self.assertFalse(report['summary'][0]['complete'])

    def test_partial_coverage_cannot_satisfy_the_100mb_target(self):
        result, report, _ = self.run_benchmark(extra=['--require-throughput-target'])
        self.assertNotEqual(0, result.returncode); self.assertIsNone(report['throughputTarget']['passed'])
        self.assertFalse(report['failures'])

    def test_full_100mb_coverage_evaluates_the_exact_target(self):
        result, report, _ = self.run_benchmark(extra=['--sizes', '100mb', '--workloads', 'identity', 'adjacent', 'accumulator',
            'iterate', 'merge', 'source-document', '--require-throughput-target'])
        # Missing fork must remain incomplete rather than a smaller set's success.
        self.assertNotEqual(0, result.returncode); self.assertIsNone(report['throughputTarget']['passed'])
        self.assertTrue(all(r['complete'] for r in report['summary']))

    def test_target_requires_all_canonical_workloads(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location('streaming_benchmark', SCRIPT)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        rows = [dict(workload=w, bytes=100_000_000, complete=True, streamOverTree=1, treeRuns=3, streamRuns=3) for w in module.CANONICAL]
        self.assertTrue(module.throughput_target(rows)['passed'])
        rows[0]['treeRuns'] = 1
        self.assertIsNone(module.throughput_target(rows)['passed'])
        rows[0]['treeRuns'] = 3
        rows[0]['streamOverTree'] = 1.00001
        self.assertFalse(module.throughput_target(rows)['passed'])
        rows[0]['complete'] = False
        self.assertIsNone(module.throughput_target(rows)['passed'])


if __name__ == '__main__':
    unittest.main()
