#!/usr/bin/env python3
"""Serial, fresh-process Xsl3 comparison. macOS RSS uses libproc; no third-party packages."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import struct
import subprocess
import threading
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
WORKLOADS = ('convert', 'filter', 'sum')
ALL_WORKLOADS = WORKLOADS + ('avg', 'min', 'max')
MODES = ('tree', 'stream')

class TaskInfo(ctypes.Structure):
    _fields_ = [(n, ctypes.c_uint64) for n in
                ('virtual_size', 'resident_size', 'total_user', 'total_system', 'threads_user', 'threads_system')] + [
                (n, ctypes.c_int32) for n in ('policy', 'faults', 'pageins', 'cow_faults', 'messages_sent',
                'messages_received', 'syscalls_mach', 'syscalls_unix', 'csw', 'threadnum', 'numrunning', 'priority')]

def rss_reader():
    if platform.system() != 'Darwin':
        raise RuntimeError('Transform-phase RSS sampler currently requires macOS libproc')
    lib = ctypes.CDLL('/usr/lib/libproc.dylib')
    lib.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]
    def read(pid):
        info = TaskInfo()
        if lib.proc_pidinfo(pid, 4, 0, ctypes.byref(info), ctypes.sizeof(info)) != ctypes.sizeof(info):
            raise RuntimeError('proc_pidinfo failed for benchmark child')
        return info.resident_size
    return read

def digest(path):
    with open(path, 'rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

def corpus(path, size, expected_dir=None):
    # Explicitly synthetic data. Expected outputs are derived independently here, not used by runtime.
    outputs = {w: open(expected_dir / (w + '.expected'), 'wb') for w in ALL_WORKLOADS} if expected_dir else {}
    total = count = 0
    minimum = maximum = None
    with open(path, 'wb') as f:
        f.write(b'<doc>')
        while True:
            i = count + 1
            value, code = i % 997 + 1, f'C{i % 23:02d}'
            label = f'record-{i:08d}-' + 'x' * 64
            record = f'<record id="{i}" code="{code}" value="{value}"><label>{label}</label></record>'.encode()
            if f.tell() + len(record) + len(b'</doc>') > size:
                break
            f.write(record)
            count, total = i, total + value
            minimum = value if minimum is None else min(minimum, value)
            maximum = value if maximum is None else max(maximum, value)
            if outputs:
                line = f'{i}|{code}|{value}|{label}\n'.encode()
                outputs['convert'].write(line)
                if i % 5 == 0:
                    outputs['filter'].write(line)
        # Whitespace inside the document makes the file exactly the requested decimal MB size.
        f.write(b' ' * (size - f.tell() - len(b'</doc>')))
        f.write(b'</doc>')
    if outputs:
        outputs['sum'].write(str(total).encode())
        average = total / count
        outputs['avg'].write((str(int(average)) if average.is_integer() else repr(average)).encode())
        outputs['min'].write(str(minimum).encode())
        outputs['max'].write(str(maximum).encode())
        for f in outputs.values():
            f.close()
    assert path.stat().st_size == size and count > 0
    return {'records': count, 'expectedSum': total, 'expectedAvg': total / count, 'expectedMin': minimum, 'expectedMax': maximum,
            'expectedFloat64Bits': {w: struct.pack('>d', float(v)).hex() for w,v in [('avg',total/count),('min',minimum),('max',maximum)]},
            'bytes': size, 'sha256': digest(path)}

def same_bytes(a, b):
    with open(a, 'rb') as fa, open(b, 'rb') as fb:
        while True:
            ca, cb = fa.read(1024 * 1024), fb.read(1024 * 1024)
            if ca != cb:
                return False
            if not ca:
                return True

def run_child(dll, mode, workload, input_path, warmup, output, sample, rss):
    env = dict(os.environ, DOTNET_TieredCompilation='0', XSL3_STREAM_STRICT='1' if mode == 'stream' else '0')
    child = subprocess.Popen(['dotnet', str(dll), mode, workload, str(input_path), str(warmup),
                              str(output) if output else '-', 'handshake'],
                             stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, env=env)
    def expect(value):
        line = child.stdout.readline().strip()
        if line != value:
            child.kill()
            _, err = child.communicate()
            raise RuntimeError(f'{mode}/{workload}: expected {value}, got {line}: {err}')
    expect('READY')
    samples, stamps, errors = [], [], []
    baseline = rss(child.pid) if sample else None
    stop = threading.Event()
    def monitor():
        try:
            while not stop.is_set():
                samples.append(rss(child.pid))
                stamps.append(time.monotonic_ns())
                stop.wait(0.001)
        except Exception as e:
            errors.append(str(e))
    thread = threading.Thread(target=monitor) if sample else None
    if thread:
        thread.start()
    child.stdin.write('GO\n'); child.stdin.flush()
    expect('DONE')
    if sample:
        samples.append(rss(child.pid))
    stop.set()
    if thread:
        thread.join()
    child.stdin.write('ACK\n'); child.stdin.flush()
    stdout, stderr = child.communicate(timeout=60)
    if child.returncode or errors:
        raise RuntimeError(f'{mode}/{workload}: {stderr} {errors}')
    result = json.loads(stdout)
    result.update(baselineRssBytes=baseline, phasePeakRssBytes=max(samples) if samples else None,
                  rssSamples=len(samples), maxSampleGapMs=max((b-a for a,b in zip(stamps, stamps[1:])), default=0)/1e6)
    if result['inputBytes'] != input_path.stat().st_size:
        raise AssertionError('Input not fully consumed')
    if mode == 'stream' and workload in ('convert', 'filter') and result['inputBytesAtFirstOutput'] >= result['inputBytes']:
        raise AssertionError('Streamed first output arrived only after all input was read')
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workloads', default=','.join(WORKLOADS))
    parser.add_argument('--sizes', default='1,5,10', help='decimal MB, comma separated')
    parser.add_argument('--timing-runs', type=int, default=9)
    parser.add_argument('--memory-runs', type=int, default=3)
    parser.add_argument('--warmup-bytes', type=int, default=65536)
    parser.add_argument('--output', type=Path, required=True)
    a = parser.parse_args()
    if a.timing_runs < 1 or a.memory_runs < 1:
        parser.error('Run counts must be positive')
    workloads = a.workloads.split(',')
    if not workloads or any(w not in ALL_WORKLOADS for w in workloads):
        parser.error('Unknown workload')
    sizes = [int(s) for s in a.sizes.split(',')]
    if any(s < 1 for s in sizes):
        parser.error('Sizes must be positive integer MB')
    out = a.output.resolve(); out.mkdir(parents=True, exist_ok=True)
    dll = HERE / 'bin/Release/net10.0/Xsl3.StreamingBenchmarks.dll'
    if not dll.exists():
        raise RuntimeError('Build the Release benchmark first')
    rss = rss_reader()
    warmup = out / 'warmup.xml'; corpus(warmup, a.warmup_bytes)
    sources = sorted(p for p in (ROOT / 'src/Xsl3').rglob('*')
                     if p.is_file() and not {'bin', 'obj'} & set(p.parts))
    fingerprint = hashlib.sha256()
    for p in sources:
        fingerprint.update(str(p.relative_to(ROOT)).encode()); fingerprint.update(p.read_bytes())
    data = {'method': {'sizesMB': sizes, 'mbBytes': 1000000, 'timingRuns': a.timing_runs,
                      'memoryRuns': a.memory_runs, 'warmupBytes': a.warmup_bytes,
                      'warmupTransformsPerChild': 5, 'tieredCompilation': False,
                      'memorySampleTargetMs': 1, 'clock': 'Stopwatch monotonic',
                      'executionIncludes': 'reader creation, parsing, transform, serialization to counting sink; excludes source compilation and warmup',
                      'firstOutput': 'first nonempty serialized byte write, with normal serializer buffering',
                      'peakMemory': 'absolute resident set sampled from outside child during transform; timing runs have no sampler',
                      'input': 'deterministic synthetic repeated records; filesystem cache warm, no network or artificial delay',
                      'workloads': {'convert': 'all records to id|code|value|label lines',
                                    'filter': 'same output for id mod 5 = 0',
                                    'sum': 'sum of every record value attribute',
                                    'avg': 'average of every record value attribute',
                                    'min': 'minimum record value attribute',
                                    'max': 'maximum record value attribute'}},
            'environment': {'platform': platform.platform(), 'machine': platform.machine(),
                            'dotnet': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
                            'gitHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                            'gitBranch': subprocess.check_output(['git', 'branch', '--show-current'], cwd=ROOT, text=True).strip(),
                            'librarySha256': digest(dll.parent / 'Xsl3.dll'), 'sourceSha256': fingerprint.hexdigest()},
            'corpora': {}, 'correctness': [], 'runs': [], 'summary': []}
    data['method']['workloads'] = {w: data['method']['workloads'][w] for w in workloads}
    (out / 'git-status.txt').write_text(subprocess.check_output(['git', 'status', '--short'], cwd=ROOT, text=True))
    def save():
        (out / 'results.json').write_text(json.dumps(data, indent=2) + '\n')
    for size in sizes:
        folder = out / f'{size}mb'; folder.mkdir(exist_ok=True)
        input_path = folder / 'input.xml'
        data['corpora'][str(size)] = corpus(input_path, size * 1000000, folder)
        for workload in workloads:
            paths = {}
            for mode in MODES:
                paths[mode] = folder / f'{workload}-{mode}.actual'
                r = run_child(dll, mode, workload, input_path, warmup, paths[mode], False, rss)
                assert same_bytes(paths[mode], folder / (workload + '.expected')), f'{mode}/{workload} differs from independent expected result'
                data['correctness'].append({'sizeMB': size, 'workload': workload, 'mode': mode,
                    'identicalToExpected': True, 'sha256': digest(paths[mode]), 'outputBytes': r['outputBytes'],
                    'inputBytesAtFirstOutput': r['inputBytesAtFirstOutput']})
            assert same_bytes(paths['tree'], paths['stream'])
            for kind, repetitions in [('timing', a.timing_runs), ('memory', a.memory_runs)]:
                for rep in range(repetitions):
                    for mode in MODES[::1 if rep % 2 == 0 else -1]:
                        r = run_child(dll, mode, workload, input_path, warmup, None, kind == 'memory', rss)
                        r.update(sizeMB=size, kind=kind, repetition=rep + 1)
                        expected_size = (folder / (workload + '.expected')).stat().st_size
                        assert r['outputBytes'] == expected_size, 'Measured output length mismatch'
                        data['runs'].append(r)
                save()
            for mode in MODES:
                runs = [r for r in data['runs'] if r['sizeMB'] == size and r['workload'] == workload and r['mode'] == mode]
                timing = [r for r in runs if r['kind'] == 'timing']
                memory = [r for r in runs if r['kind'] == 'memory']
                row = {'sizeMB': size, 'workload': workload, 'mode': mode,
                       'elapsedMs': statistics.median(r['elapsedMs'] for r in timing),
                       'minElapsedMs': min(r['elapsedMs'] for r in timing),
                       'maxElapsedMs': max(r['elapsedMs'] for r in timing),
                       'firstOutputMs': statistics.median(r['firstOutputMs'] for r in timing),
                       'peakRssMB': statistics.median(r['phasePeakRssBytes'] for r in memory)/1e6,
                       'baselineRssMB': statistics.median(r['baselineRssBytes'] for r in memory)/1e6,
                       'maxObservedRssMB': max(r['phasePeakRssBytes'] for r in memory)/1e6,
                       'inputBytesAtFirstOutput': statistics.median(r['inputBytesAtFirstOutput'] for r in timing)}
                data['summary'].append(row)
            save()
            print(f'{size} MB {workload}: outputs identical, timing + memory complete', flush=True)
    save()
    print(json.dumps(data['summary'], indent=2), flush=True)

if __name__ == '__main__':
    main()
