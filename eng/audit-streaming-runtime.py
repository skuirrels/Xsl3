#!/usr/bin/env python3
"""Validate strict runtime evidence, without selecting tests or changing their verdicts.

An inventory audit is not proof of forward execution, bounded memory, or full P7
acceptance. Case selection and assertion evaluation belong to the W3C runner.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import subprocess


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def stage(name):
    # Ownership follows p7-streaming.md's original stage table. Cross-cutting
    # integration sets are explicit, so a new set requires review rather than a default.
    if name in ('accumulator', 'merge', 'si-merge', 'si-iterate', 'si-for-each-group'):
        return 'S5', 'Stateful constructs'
    if name == 'si-fork':
        return 'S6', 'Fork lifecycle and buffering'
    if name == 'system-property':
        return 'S7', 'Public feature claim'
    if name in ('streamable', 'si-apply-templates', 'si-next-match', 'si-apply-imports',
                'function', 'mode', 'override', 'use-package', 'docbook') or name.startswith('su-'):
        return 'S4', 'Streamable modes, functions and stylesheet integration'
    if name in ('source-document', 'disable-output-escaping', 'error', 'evaluate', 'xml-to-json') or name.startswith(('sf-', 'sx-', 'si-')):
        return 'S3', 'Forward runtime, expressions, instructions and diagnostics'
    raise ValueError(f'Unassigned test set: {name}')


def load_report(path, suite, engine=None):
    try:
        rows = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()]
    except (ValueError, UnicodeError) as error:
        raise ValueError(f'Invalid JSONL report: {error}') from error
    if len(rows) < 3 or any(not isinstance(row, dict) for row in rows):
        raise ValueError('Report must contain a header, cases and completion record')
    header, complete = rows[0], rows[-1]
    if (header.get('Kind') != 'run' or type(header.get('FormatVersion')) is not int or header['FormatVersion'] != 1
            or header.get('Profile') != 'strm' or header.get('Strict') is not True
            or 'SetPrefixes' not in header or header['SetPrefixes'] not in (None, '')):
        raise ValueError('Expected an unfiltered strict streaming report')
    if (type(header.get('Api')) is not bool or header.get('Configuration') not in ('Release', 'Debug')
            or not isinstance(header.get('Framework'), str) or not header['Framework']):
        raise ValueError('Missing execution configuration')
    for key in ('CatalogSha256', 'EngineSha256'):
        if not isinstance(header.get(key), str) or not re.fullmatch('[0-9a-f]{64}', header[key]):
            raise ValueError(f'Invalid {key}')
    if digest(suite / 'catalog.xml') != header['CatalogSha256']:
        raise ValueError('Catalog fingerprint changed')
    if engine is not None and digest(engine) != header['EngineSha256']:
        raise ValueError('Engine fingerprint changed')
    if complete.get('Kind') != 'complete':
        raise ValueError('Interrupted report: no terminal completion record')
    cases, set_hashes = {}, {}
    for row in rows[1:-1]:
        case = row.get('Case')
        if row.get('Kind') != 'case' or not isinstance(case, str) or case in cases:
            raise ValueError(f'Unexpected record or duplicate case: {case}')
        name = row.get('Set')
        if not isinstance(name, str) or not case.startswith(name + '/') or case == name + '/':
            raise ValueError(f'Invalid case identity: {case}')
        stage(name)
        if row.get('Outcome') not in ('Pass', 'Fail', 'NotRun'):
            raise ValueError(f'Invalid outcome: {case}')
        if not isinstance(row.get('Detail'), str):
            raise ValueError(f'Missing outcome detail: {case}')
        for key in ('SetSha256', 'DefinitionSha256'):
            if not isinstance(row.get(key), str) or not re.fullmatch('[0-9a-f]{64}', row[key]):
                raise ValueError(f'Invalid {key}: {case}')
        relative = row.get('SetFile')
        if not isinstance(relative, str) or Path(relative).is_absolute():
            raise ValueError(f'Invalid fixture path: {case}')
        source = (suite / relative).resolve()
        if not source.is_relative_to(suite.resolve()):
            raise ValueError(f'Fixture outside suite: {case}')
        if source not in set_hashes:
            set_hashes[source] = digest(source)
        if set_hashes[source] != row['SetSha256']:
            raise ValueError(f'Test set fingerprint changed: {case}')
        cases[case] = row
    counts = Counter(row['Outcome'] for row in cases.values())
    expected = {'Selected': len(cases), 'Passed': counts['Pass'], 'Failed': counts['Fail'], 'NotRun': counts['NotRun']}
    for key, value in expected.items():
        if type(complete.get(key)) is not int or complete[key] != value:
            raise ValueError(f'Inconsistent total: {key}')
    wrong_codes = complete.get('UnmatchedExpectedErrorCodes')
    if type(wrong_codes) is not int or wrong_codes < 0:
        raise ValueError('Missing exact-code accounting')
    if wrong_codes:
        raise ValueError('Unmatched expected error codes remain')
    if any(r['Outcome'] == 'Pass' and r['Detail'].startswith('error code') for r in cases.values()):
        raise ValueError('Unmatched expected error code marked as passing')
    reasons = complete.get('NotRunReasons')
    if not isinstance(reasons, dict) or any(type(v) is not int or v <= 0 for v in reasons.values()) or sum(reasons.values()) != counts['NotRun']:
        raise ValueError('Inconsistent not-run reasons')
    if reasons != Counter(r['Detail'] for r in cases.values() if r['Outcome'] == 'NotRun'):
        raise ValueError('Not-run reasons disagree with case outcomes')
    return header, cases, complete


def inventory(header, cases):
    sets = {}
    for row in cases.values():
        definition = {field: row[field] for field in ('SetFile', 'SetSha256')}
        if row['Set'] in sets and sets[row['Set']] != definition:
            raise ValueError(f'Inconsistent set identity: {row["Set"]}')
        sets[row['Set']] = definition
    return {'FormatVersion': 1, 'CatalogSha256': header['CatalogSha256'],
            'Sets': dict(sorted(sets.items())), 'Cases': {
                key: {field: row[field] for field in ('Set', 'DefinitionSha256', 'Outcome')}
                for key, row in sorted(cases.items())}}


def verify_suite(suite, revision):
    """Check referenced stylesheets, inputs and schemas too, not just catalog XML."""
    def git(*args):
        return subprocess.check_output(['git', '-C', str(suite), *args], text=True).strip()
    if Path(git('rev-parse', '--show-toplevel')).resolve() != suite.resolve():
        raise ValueError('Suite must be its own pinned checkout')
    if not re.fullmatch('[0-9a-f]{40}', revision) or git('rev-parse', 'HEAD') != revision:
        raise ValueError('Suite revision does not match the requested pin')
    if git('status', '--porcelain', '--untracked-files=normal'):
        raise ValueError('Suite contains modified or untracked fixtures')


def audit(report, suite, manifest=None, engine=None, revision=None):
    if revision is not None:
        verify_suite(suite, revision)
    header, cases, complete = load_report(report, suite, engine)
    current = inventory(header, cases)
    current['SuiteRevision'] = revision
    if manifest is not None:
        previous = json.loads(manifest.read_text(encoding='utf-8'))
        if type(previous.get('FormatVersion')) is not int or previous['FormatVersion'] != 1 or previous.get('CatalogSha256') != current['CatalogSha256']:
            raise ValueError('Inventory catalog/version mismatch')
        if previous.get('SuiteRevision') != revision or previous.get('Sets') != current['Sets']:
            raise ValueError('Inventory suite revision or test sets changed')
        if previous.get('Cases', {}).keys() != current['Cases'].keys():
            raise ValueError('Inventory has missing or additional cases')
        for key, old in previous['Cases'].items():
            new = current['Cases'][key]
            if old.get('Outcome') not in ('Pass', 'Fail', 'NotRun'):
                raise ValueError(f'Invalid inventory outcome: {key}')
            if any(old.get(field) != new[field] for field in ('Set', 'DefinitionSha256')):
                raise ValueError(f'Inventory fixture changed: {key}')
            if old.get('Outcome') == 'Pass' and new['Outcome'] != 'Pass':
                raise ValueError(f'Previously passing case regressed: {key}')
            if old['Outcome'] == 'Fail' and new['Outcome'] == 'NotRun':
                raise ValueError(f'Previously runnable case became unsupported: {key}')
    totals = {}
    for row in cases.values():
        owner, _ = stage(row['Set'])
        bucket = totals.setdefault(owner, Counter())
        bucket[row['Outcome']] += 1
    summary = {'InventoryVerified': True, 'SuiteRevision': revision, 'Execution': header, 'Totals': complete,
               'StageOutcomes': dict(sorted(totals.items())),
               'SetOwnership': {name: dict(zip(('Stage', 'Reason'), stage(name))) for name in sorted({r['Set'] for r in cases.values()})},
               'NonPassing': [{**row, 'Stage': stage(row['Set'])[0]} for row in cases.values() if row['Outcome'] != 'Pass'],
               'Scope': 'Inventory and reported outcomes only; forward execution and full P7 acceptance require separate evidence.'}
    return summary, current


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--suite', type=Path, required=True)
    parser.add_argument('--suite-revision', help='Exact immutable suite commit; defaults to the frozen inventory pin')
    parser.add_argument('--engine', type=Path)
    choice = parser.add_mutually_exclusive_group(required=True)
    choice.add_argument('--manifest', type=Path)
    choice.add_argument('--write-manifest', type=Path, help='Freeze a reviewed complete selection; never overwrite')
    parser.add_argument('--expected-count', type=int, help='Required when initially freezing the selection')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        revision = args.suite_revision
        if revision is None and args.manifest:
            revision = json.loads(args.manifest.read_text(encoding='utf-8')).get('SuiteRevision')
        if not isinstance(revision, str):
            raise ValueError('An immutable suite revision is required')
        result, frozen = audit(args.report, args.suite, args.manifest, args.engine, revision)
        if args.write_manifest:
            if args.expected_count is None or len(frozen['Cases']) != args.expected_count:
                raise ValueError('Initial inventory requires a matching reviewed expected count')
            with args.write_manifest.open('x', encoding='utf-8') as target:
                json.dump(frozen, target, indent=2, sort_keys=True)
                target.write('\n')
        with args.output.open('x', encoding='utf-8') as target:
            json.dump(result, target, indent=2, sort_keys=True)
            target.write('\n')
    except (OSError, ValueError, TypeError, AttributeError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Runtime audit failed: {error}\n')


if __name__ == '__main__':
    main()
