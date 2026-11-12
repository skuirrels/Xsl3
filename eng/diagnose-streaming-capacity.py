#!/usr/bin/env python3
"""Measure explicit public fork capacities with an unchanged runtime and canonical output oracles."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess


def replace_once(path, before, after):
    source = path.read_text()
    if source.count(before) != 1:
        raise ValueError(f'Expected exactly one diagnostic insertion point in {path}')
    path.write_text(source.replace(before, after))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source, output = args.source.resolve(), args.output.resolve()
    engine = source / 'bench/Xsl3.Benchmarks/bin/Release/net10.0/Xsl3.dll'
    if not engine.is_file():
        parser.error('Build the source Release/net10.0 benchmark first')
    output.mkdir(parents=True, exist_ok=False)
    consumer = output / 'consumer'
    consumer.mkdir()
    files = [source / 'tests/Xsl3.Tests' / name for name in (
        'StreamingProbe.cs', 'StreamingProbe.Memory.cs', 'StreamingProbe.Stylesheets.cs',
        'StreamingProbe.Json.cs', 'StreamingProbe.Report.cs', 'ProbeSha256.cs')]
    files += [source / 'bench/Xsl3.Benchmarks' / name for name in ('Streaming.cs', 'Streaming.Engine.cs')]
    manifest = {'scope': 'Explicit public settings diagnostic, not original acceptance',
                'sourceRevision': subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip(),
                'originalSources': {str(p.relative_to(source)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files},
                'engineSha256': hashlib.sha256(engine.read_bytes()).hexdigest()}
    for path in files:
        shutil.copy2(path, consumer / path.name)
    shutil.copy2(engine, consumer / 'Xsl3.dll')
    engine_source = consumer / 'Streaming.Engine.cs'
    replace_once(engine_source, '    private static partial Invocation',
                 '    private static int ConfiguredForkCapacity;\n    private static partial Invocation')
    replace_once(engine_source,
                 '        var executable = new XsltCompiler { Settings = new() { EnableDocumentFunction = true, EnableResultDocument = true } }\n'
                 '            .Compile(new StringReader(Stylesheet(workload)));',
                 '''        var compiler = new XsltCompiler { Settings = new() {
            EnableDocumentFunction = true, EnableResultDocument = true,
            StreamingBufferCapacity = int.Parse(System.Environment.GetEnvironmentVariable("XSL3_DIAGNOSTIC_CAPACITY")
                ?? throw new System.ArgumentException("Explicit diagnostic capacity required"), System.Globalization.CultureInfo.InvariantCulture)
        } };
        ConfiguredForkCapacity = compiler.Settings.StreamingBufferCapacity;
        var executable = compiler.Compile(new StringReader(Stylesheet(workload)));''')
    replace_once(consumer / 'StreamingProbe.Report.cs', "        return text.Append('}').ToString();",
                 '        IntegerProperty(text, "ConfiguredForkCapacity", ConfiguredForkCapacity);\n'
                 "        return text.Append('}').ToString();")
    (consumer / 'Program.cs').write_text('''return args.Length == 1 && args[0] == "streaming-info"
    ? Xsl3.Benchmarks.Streaming.WriteRuntimeInfo()
    : args.Length > 0 && args[0] == "streaming-child"
        ? Xsl3.Benchmarks.Streaming.RunChild(args[1..]) : 64;
''')
    (consumer / 'Consumer.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Xsl3.Benchmarks</AssemblyName><IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup><Reference Include="Xsl3"><HintPath>Xsl3.dll</HintPath></Reference></ItemGroup>
</Project>
''')
    manifest['diagnosticSources'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in consumer.glob('*.cs')}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    with (output / 'build.log').open('w') as log:
        subprocess.run(['dotnet', 'build', str(consumer / 'Consumer.csproj'), '-c', 'Release', '-m:1'],
                       stdout=log, stderr=subprocess.STDOUT, check=True)
    assembly = consumer / 'bin/Release/net10.0/Xsl3.Benchmarks.dll'
    manifest['binaries'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in assembly.parent.glob('*.dll')}
    if manifest['binaries']['Xsl3.dll'] != manifest['engineSha256']:
        raise ValueError('Diagnostic build changed the runtime image')
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    controller = Path(__file__).with_name('streaming-benchmark.py').resolve()
    env = dict(os.environ)
    # Observe the runner's effective processor allowance; do not override it here.
    runs, fingerprint = [], None
    for round_number, capacities in enumerate(((64, 256, 1024, 4096), (4096, 1024, 256, 64), (256, 1024, 4096, 64)), 1):
        for capacity in capacities:
            env['XSL3_DIAGNOSTIC_CAPACITY'] = str(capacity)
            directory = output / f'capacity-{capacity}-round-{round_number}'
            with directory.with_suffix('.log').open('w') as log:
                subprocess.run(['python3', str(controller), '--assembly', str(assembly), '--sizes', '100mb',
                                '--workloads', 'fork', '--rounds', '1', '--output', str(directory)],
                               env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
            report = json.loads((directory / 'report.json').read_text())
            if not report['complete'] or len(report['trials']) != 2:
                raise ValueError('Incomplete canonical pair')
            for trial in report['trials']:
                result = trial['result']
                if not trial['passed'] or result['ConfiguredForkCapacity'] != capacity:
                    raise ValueError('Invalid output or wrong applied capacity')
                observed = (result['Records'], result['Outputs'])
                if fingerprint is not None and fingerprint != observed:
                    raise ValueError('Output fingerprints differ between capacities')
                fingerprint = observed
            runs.append({'capacity': capacity, 'round': round_number, 'output': directory.name})
            (output / 'runs.json').write_text(json.dumps(runs, indent=2) + '\n')
            print(capacity, round_number, [(t['mode'], t['result']['ElapsedMs']) for t in report['trials']], flush=True)


if __name__ == '__main__':
    main()
