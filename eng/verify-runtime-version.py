#!/usr/bin/env python3
"""Compare runtime version text with real assembly headers across SDK build configurations."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


CHECKER = '''using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
string? header = assembly.GetName().Version?.ToString();
var field = assembly.GetType("Xsl3.XslCompiledTransform", true)!.GetField("s_version", BindingFlags.Static | BindingFlags.NonPublic)!;
string? runtime = (string?)field.GetValue(null);
Console.WriteLine(JsonSerializer.Serialize(new { Header = header, Runtime = runtime, Framework = RuntimeInformation.FrameworkDescription, MatchesHeader = header == runtime }));
return header == runtime ? 0 : 1;
'''

MANUAL_HEADER = '[assembly: System.Reflection.AssemblyVersion("4.3.2.1")]\n'
METADATA = 'System.Reflection.AssemblyMetadata'
CASES = {
    'canonical': ('4.3.2.1', [], ''),
    'short': ('4.3', [], ''),
    'padded': ('0004.03.002.01', [], ''),
    'wildcard': ('4.3.*', ['-p:Deterministic=false'], ''),
    'no-generated-info': ('4.3.2.1', ['-p:GenerateAssemblyInfo=false'], ''),
    'no-generated-version': ('4.3.2.1', ['-p:GenerateAssemblyVersionAttribute=false'], ''),
    'manual-metadata': ('4.3.2.1', ['-p:GenerateAssemblyInfo=false'],
                        MANUAL_HEADER + f'[assembly: {METADATA}("Xsl3.RuntimeAssemblyVersion", "99.0.0.0")]\n'),
    'duplicate-metadata': ('4.3.2.1', ['-p:GenerateAssemblyInfo=false'],
                           MANUAL_HEADER + f'[assembly: {METADATA}("Xsl3.RuntimeAssemblyVersion", "99.0.0.0")]\n' * 2),
    'oversized-metadata': ('4.3.2.1', ['-p:GenerateAssemblyInfo=false'],
                           MANUAL_HEADER + f'[assembly: {METADATA}("Xsl3.RuntimeAssemblyVersion", "999999999999999999999999999999.0.0.0")]\n'),
}


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def header_matches_configuration(header, version, properties, fixture):
    if not isinstance(header, str):
        return False
    if not fixture and any(p in properties for p in ('-p:GenerateAssemblyInfo=false', '-p:GenerateAssemblyVersionAttribute=false')):
        return header == '0.0.0.0'
    parts = version.split('.')
    if '*' in parts:
        actual = header.split('.')
        return (len(actual) == 4 and actual[:2] == parts[:2]
                and all(p.isascii() and p.isdecimal() and 0 <= int(p) <= 65535 for p in actual))
    return header == '.'.join(str(int(p)) for p in parts + ['0'] * (4 - len(parts)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--frameworks', nargs='+', choices=['net8.0', 'net10.0'], default=['net8.0', 'net10.0'])
    parser.add_argument('--cases', nargs='+', choices=list(CASES), default=list(CASES))
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    controller_hash = digest(Path(__file__))
    if len(set(args.frameworks)) != len(args.frameworks) or len(set(args.cases)) != len(args.cases):
        parser.error('frameworks and cases must be unique')
    root = args.source.resolve()
    if not (root / 'src/Xsl3/Xsl3.csproj').is_file():
        parser.error('--source must contain src/Xsl3/Xsl3.csproj')
    args.output.mkdir(parents=True, exist_ok=False)
    output = args.output.resolve()
    source = output / 'source'
    shutil.copytree(root / 'src/Xsl3', source / 'src/Xsl3', ignore=shutil.ignore_patterns('bin', 'obj'))
    for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.Config', '.editorconfig'):
        if (root / name).is_file():
            shutil.copyfile(root / name, source / name)
    source_hashes = {p.relative_to(source).as_posix(): digest(p) for p in source.rglob('*') if p.is_file()}
    checker = output / 'checker'
    checker.mkdir()
    (checker / 'Checker.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFrameworks>' + ';'.join(args.frameworks) + '</TargetFrameworks><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>\n')
    (checker / 'Program.cs').write_text(CHECKER)
    with (output / 'checker-build.log').open('x') as log:
        subprocess.run([args.dotnet, 'build', str(checker), '-c', 'Release', '-m:1', '-p:UseSharedCompilation=false'],
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=300)
    report = dict(complete=False, frameworks=args.frameworks, cases=args.cases,
                  sourceHashes=source_hashes, controllerSha256=controller_hash,
                  checkerSha256={framework: digest(checker / f'bin/Release/{framework}/Checker.dll') for framework in args.frameworks}, results=[])

    def checkpoint():
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')

    checkpoint()
    fixture = source / 'src/Xsl3/VersionContractFixture.cs'
    for framework in args.frameworks:
        checker_dll = checker / f'bin/Release/{framework}/Checker.dll'
        for name in args.cases:
            version, properties, text = CASES[name]
            fixture.write_text(text)
            case = output / framework / name
            case.mkdir(parents=True)
            shutil.copyfile(fixture, case / 'VersionContractFixture.cs')
            command = [args.dotnet, 'build', str(source / 'src/Xsl3'), '-c', 'Release', '-f', framework,
                       '-m:1', '-p:UseSharedCompilation=false', '-p:AssemblyVersion=' + version, '-o', str(case), *properties]
            result = dict(framework=framework, case=name, configuredVersion=version, command=command,
                          fixtureSha256=digest(fixture), passed=False)
            try:
                with (case / 'build.log').open('x') as log:
                    build = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=300)
                result['buildExitCode'] = build.returncode
                if build.returncode == 0:
                    engine = case / 'Xsl3.dll'
                    result['engineSha256'] = digest(engine)
                    with (case / 'checker.stdout').open('x') as stdout, (case / 'checker.stderr').open('x') as stderr:
                        check = subprocess.run([args.dotnet, str(checker_dll), str(engine)], stdout=stdout, stderr=stderr, timeout=30)
                    result['checkerExitCode'] = check.returncode
                    result['actual'] = json.loads((case / 'checker.stdout').read_text())
                    expected_runtime = '.NET ' + framework.removeprefix('net').split('.')[0] + '.'
                    result['configuredHeaderVerified'] = header_matches_configuration(result['actual']['Header'], version, properties, text)
                    result['passed'] = (check.returncode == 0 and result['actual']['MatchesHeader'] is True
                                        and result['actual']['Framework'].startswith(expected_runtime)
                                        and result['configuredHeaderVerified'])
                generated = source / f'src/Xsl3/obj/Release/{framework}/Xsl3.SdkAssemblyVersion.g.cs'
                if generated.is_file():
                    shutil.copyfile(generated, case / 'generated-version.cs')
            except (OSError, ValueError, subprocess.TimeoutExpired) as error:
                result['error'] = str(error)
            report['results'].append(result)
            checkpoint()
            print(framework, name, result['actual'] if 'actual' in result else result, flush=True)
    report['complete'] = True
    checkpoint()
    return 0 if all(result['passed'] for result in report['results']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
