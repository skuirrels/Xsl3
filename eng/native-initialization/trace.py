#!/usr/bin/env python3
"""Diagnostic native initialization, never an acceptance gate or an RSS subtraction."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import math
import signal
import time
import resource
import subprocess
import sys


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(65536), b''):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native', required=True, type=Path)
    parser.add_argument('--tracer', type=Path)
    parser.add_argument('--parent-bytes', type=int, default=0)
    parser.add_argument('--timeout-seconds', type=float, default=60)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if sys.platform != 'linux' or args.parent_bytes < 0 or not args.native.is_file() or not os.access(args.native, os.X_OK) or not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0:
        parser.error('Linux, a native executable and nonnegative parent allocation are required')
    if args.tracer is not None and not args.tracer.is_file():
        parser.error('tracer must exist')
    args.output.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location('gate', Path(__file__).parents[1] / 'streaming-gate.py')
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
    image_sha = sha(args.native)
    env = dict(os.environ)
    for key in list(env):
        if key.startswith(('DOTNET_GCHeapHardLimit', 'COMPlus_GCHeapHardLimit')) or key == 'LD_PRELOAD':
            del env[key]
    env['DOTNET_GCHeapHardLimit'] = format(20971520, 'X')
    if args.tracer is not None:
        env['LD_PRELOAD'] = str(args.tracer.resolve())
    parent = bytearray(args.parent_bytes)
    # Retain and touch the explicit diagnostic allocation; it is never an input
    # or a desired value for the transformation and no counter is adjusted by it.
    for offset in range(0, len(parent), os.sysconf('SC_PAGESIZE')):
        parent[offset] = 1
    parent_peak = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * 1024
    command = [str(args.native.resolve()), 'stream-probe', 'identity', 'stream', '2097152', '0']
    with (args.output / 'native.stdout').open('x') as stdout, (args.output / 'native.stderr').open('x') as stderr:
        child = subprocess.Popen(command, env=env, stdout=stdout, stderr=stderr)
        timed_out = False
        deadline = time.monotonic() + args.timeout_seconds
        while True:
            pid, status, usage = os.wait4(child.pid, os.WNOHANG)
            if pid:
                break
            if time.monotonic() >= deadline:
                timed_out = True
                try:
                    os.kill(child.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                _, status, usage = os.wait4(child.pid, 0)
                break
            time.sleep(0.01)
        child.returncode = os.waitstatus_to_exitcode(status)
    error = None
    try:
        result = json.loads((args.output / 'native.stdout').read_text())
    except ValueError as failure:
        result = None
        error = str(failure)
    valid = (not timed_out and child.returncode == 0
             and gate.valid_measurement(result, 'identity', 'stream', 2097152, 0, True)
             and result['AvailableManagedBytes'] == 20971520)
    trace = (args.output / 'native.stderr').read_text()
    addresses = []
    if 'P7_DLOPEN' in trace:
        addresses = re.findall(re.escape(args.native.name) + r'\(\+(0x[0-9a-f]+)\)', trace.split('P7_DLOPEN')[1])
    if addresses:
        symbols = subprocess.check_output(['addr2line', '-f', '-C', '-e', str(args.native) + '.dbg', *addresses], text=True)
        (args.output / 'first-icu-callers.txt').write_text(symbols)
    report = dict(acceptance=False, valid=valid, platform=platform.platform(), native_sha256=image_sha,
                  tracer_sha256=sha(args.tracer) if args.tracer is not None else None,
                  parent_allocation_bytes=args.parent_bytes, parent_peak_bytes_before_child=parent_peak,
                  outer_peak_bytes=usage.ru_maxrss * 1024, command=command, exit_code=child.returncode,
                  timed_out=timed_out, parse_error=error,
                  icu_dlopen_observed='P7_DLOPEN' in trace, first_icu_addresses=addresses, result=result)
    (args.output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    return 0 if valid else 1


if __name__ == '__main__':
    raise SystemExit(main())
