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
    result = {'FormatVersion': 1, 'CatalogSha256': header['CatalogSha256'],
            'Sets': dict(sorted(sets.items())), 'Cases': {
                key: {field: row[field] for field in ('Set', 'DefinitionSha256', 'Outcome')}
                for key, row in sorted(cases.items())}}
    if header.get('ForwardingCaptured') is True and all(row.get('Forwarding', {}).get('FormatVersion') == 2 for row in cases.values()):
        result['StylesheetForwardCases'] = sorted(case for case, row in cases.items()
            if any(entry.get('Stage') == 'Stylesheet' and any(source.get('ActualForward') is True for source in entry['Sources'])
                   for entry in row['Forwarding']['Executions']))
    return result


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


def audit_forwarding(header, cases, required=False, require_stylesheet=False):
    required = required or require_stylesheet
    captured = header.get('ForwardingCaptured', False)
    if type(captured) is not bool:
        raise ValueError('Invalid forwarding capture declaration')
    if not captured:
        if required or any('Forwarding' in row for row in cases.values()):
            raise ValueError('A forwarding capture is required for this evidence')
        return None
    counts = Counter()
    stage_counts = {}
    staged_cases = 0
    stylesheet_forward_cases = 0
    early, no_execution, no_forward = [], [], []
    no_stylesheet, stylesheet_no_forward = [], []
    for case, row in cases.items():
        evidence = row.get('Forwarding')
        if (not isinstance(evidence, dict) or type(evidence.get('FormatVersion')) is not int
                or evidence['FormatVersion'] not in (1, 2) or type(evidence.get('Started')) is not int or evidence['Started'] < 0):
            raise ValueError(f'Missing or invalid forwarding capture: {case}')
        staged = evidence['FormatVersion'] == 2
        if require_stylesheet and not staged:
            raise ValueError(f'Stylesheet-stage forwarding evidence is required: {case}')
        staged_cases += staged
        executions = evidence.get('Executions')
        if not isinstance(executions, list) or len(executions) != evidence['Started']:
            raise ValueError(f'Incomplete forwarding executions: {case}')
        ids = [entry.get('Id') for entry in executions if isinstance(entry, dict)]
        if (len(ids) != len(executions) or any(type(i) is not int for i in ids)
                or sorted(ids) != list(range(1, evidence['Started'] + 1))):
            raise ValueError(f'Invalid or duplicate forwarding execution IDs: {case}')
        id_set = set(ids)
        case_forwarded = False
        case_stylesheet_forwarded = False
        case_stylesheet_executed = False
        if not executions:
            no_execution.append(case)
        for entry in executions:
            if not {'Id', 'Parent', 'StreamedInitialMode', 'InputKind', 'ForwardInput', 'BodyCompleted', 'Sources'} <= entry.keys():
                raise ValueError(f'Missing execution metadata: {case}')
            stage = entry.get('Stage') if staged else None
            if staged and stage not in ('Unclassified', 'Preparation', 'Compilation', 'Stylesheet', 'Assertion'):
                raise ValueError(f'Missing or invalid execution stage: {case}')
            if require_stylesheet and stage == 'Unclassified':
                raise ValueError(f'Unclassified command cannot prove stylesheet forwarding: {case}')
            bucket = stage_counts.setdefault(stage, Counter()) if staged else Counter()
            bucket['Executions'] += 1
            case_stylesheet_executed |= stage == 'Stylesheet'
            parent = entry.get('Parent')
            if parent is not None and (type(parent) is not int or parent not in id_set or parent >= entry['Id']):
                raise ValueError(f'Invalid parent execution: {case}')
            if any(type(entry.get(field)) is not bool for field in ('StreamedInitialMode', 'ForwardInput', 'BodyCompleted')):
                raise ValueError(f'Invalid forwarding execution flags: {case}')
            kind = entry.get('InputKind')
            if kind not in ('reader', 'stream', 'uri', 'location', 'navigator', 'navigable', 'other'):
                raise ValueError(f'Invalid forwarding input kind: {case}')
            if entry['ForwardInput'] != (kind in ('reader', 'stream', 'uri', 'location')):
                raise ValueError(f'Inconsistent forwarding input kind: {case}')
            sources = entry.get('Sources')
            if not isinstance(sources, list):
                raise ValueError(f'Missing source representations: {case}')
            opened_initial = False
            for source in sources:
                if not isinstance(source, dict) or source.get('Kind') not in ('Initial', 'Default', 'SourceDocument'):
                    raise ValueError(f'Invalid source entry point: {case}')
                if not {'Kind', 'RequestedForward', 'Status', 'ActualForward', 'NavigatorType'} <= source.keys():
                    raise ValueError(f'Missing source observation: {case}')
                if type(source.get('RequestedForward')) is not bool:
                    raise ValueError(f'Invalid source representation flags: {case}')
                if source['Kind'] == 'SourceDocument' and not source['RequestedForward']:
                    raise ValueError(f'Invalid forward source-document entry: {case}')
                if source['Status'] == 'Failed':
                    if source['ActualForward'] is not None or source['NavigatorType'] is not None:
                        raise ValueError(f'Failed opening cannot claim a source representation: {case}')
                    counts['FailedSourceObservations'] += 1
                    bucket['FailedSourceObservations'] += 1
                    continue
                if source['Status'] != 'Opened' or type(source['ActualForward']) is not bool:
                    raise ValueError(f'Invalid source observation status: {case}')
                name = source.get('NavigatorType')
                if not isinstance(name, str) or not name or source['ActualForward'] != (name == 'Xsl3.Streaming.StreamedNavigator'):
                    raise ValueError(f'Inconsistent actual source representation: {case}')
                if source['RequestedForward'] and not source['ActualForward']:
                    raise ValueError(f'Requested forward source was grounded: {case}')
                if source['ActualForward']:
                    case_forwarded = True
                    counts['ForwardSources'] += 1
                    bucket['ForwardSources'] += 1
                    case_stylesheet_forwarded |= stage == 'Stylesheet'
                    if source['Kind'] == 'Initial':
                        counts['InitialForwardSources'] += 1
                        bucket['InitialForwardSources'] += 1
                        opened_initial |= source['RequestedForward']
                    elif source['Kind'] == 'SourceDocument':
                        counts['SourceDocumentForwardSources'] += 1
                        bucket['SourceDocumentForwardSources'] += 1
                else:
                    counts['TreeSources'] += 1
                    bucket['TreeSources'] += 1
            counts['Executions'] += 1
            if entry['StreamedInitialMode'] and entry['ForwardInput']:
                counts['EligibleInitialExecutions'] += 1
                bucket['EligibleInitialExecutions'] += 1
                if not opened_initial:
                    if entry['BodyCompleted']:
                        raise ValueError(f'Eligible execution did not open its forward initial source: {case}')
                    early.append(dict(Case=case, Execution=entry['Id']))
        if case_forwarded:
            counts['CasesWithForwardSources'] += 1
        elif executions:
            no_forward.append(case)
        stylesheet_forward_cases += case_stylesheet_forwarded
        if staged and not case_stylesheet_executed:
            no_stylesheet.append(case)
        elif staged and not case_stylesheet_forwarded:
            stylesheet_no_forward.append(case)
    if required and not counts['ForwardSources']:
        raise ValueError('Forwarding capture contains no actual forward sources')
    if require_stylesheet and not stylesheet_forward_cases:
        raise ValueError('Forwarding capture contains no actual stylesheet-stage forward sources')
    return dict(Verified=True, CapturedCases=len(cases),
                **{name: counts[name] for name in ('CasesWithForwardSources', 'Executions', 'EligibleInitialExecutions', 'ForwardSources',
                    'InitialForwardSources', 'SourceDocumentForwardSources', 'TreeSources', 'FailedSourceObservations')},
                FailedBeforeInitialSourceObservation=early, CasesWithoutCommandExecution=no_execution,
                CasesWithCommandsWithoutForwardSources=no_forward,
                StagesCapturedCases=staged_cases, StylesheetCasesWithForwardSources=stylesheet_forward_cases,
                CasesWithoutStylesheetExecution=no_stylesheet,
                CasesWithStylesheetExecutionWithoutForwardSources=stylesheet_no_forward,
                StageObservations={key: dict(value) for key, value in sorted(stage_counts.items())},
                Scope='Observed command executions and source representations only; stylesheet applicability, physical reads, bounds and full P7 acceptance require separate evidence.')


def audit_evaluation(header, cases, required=False):
    captured = header.get('EvaluationCaptured', False)
    if type(captured) is not bool:
        raise ValueError('Invalid evaluation capture declaration')
    if not captured:
        if required or any('Evaluation' in row or 'DeclaredInitialSources' in row for row in cases.values()):
            raise ValueError('An evaluation capture is required for this evidence')
        return None
    counts = Counter()
    observations = {}
    for case, row in cases.items():
        entry = row.get('Evaluation')
        if not isinstance(entry, dict) or not {'Status', 'ExceptionType', 'Diagnostic'} <= entry.keys():
            raise ValueError(f'Missing evaluation observation: {case}')
        status = entry['Status']
        if status not in ('NotObserved', 'Returned', 'Error'):
            raise ValueError(f'Invalid evaluation status: {case}')
        diagnostic = entry['Diagnostic']
        if status != 'Error':
            if diagnostic is not None or entry['ExceptionType'] is not None:
                raise ValueError(f'An evaluation without an error cannot claim a diagnostic: {case}')
            if status == 'NotObserved' and row['Outcome'] == 'Pass':
                raise ValueError(f'A passing runtime case has no evaluation observation: {case}')
        else:
            if not isinstance(entry['ExceptionType'], str) or not entry['ExceptionType']:
                raise ValueError(f'Missing evaluation exception type: {case}')
            if (not isinstance(diagnostic, dict)
                    or not {'Code', 'IsStatic', 'ModuleUri', 'Line', 'Column', 'Reason'} <= diagnostic.keys()
                    or type(diagnostic['IsStatic']) is not bool):
                raise ValueError(f'Missing primary diagnostic: {case}')
            if diagnostic['Code'] is not None and (not isinstance(diagnostic['Code'], str)
                    or not re.fullmatch(r'Q\{[^{}]*\}[^\s{}:]+', diagnostic['Code'])):
                raise ValueError(f'Invalid primary diagnostic code: {case}')
            if any(type(diagnostic[key]) is not int or diagnostic[key] < 0 for key in ('Line', 'Column')):
                raise ValueError(f'Invalid primary diagnostic location: {case}')
            if any(diagnostic[key] is not None and not isinstance(diagnostic[key], str) for key in ('ModuleUri', 'Reason')):
                raise ValueError(f'Invalid primary diagnostic text: {case}')
        sources = row.get('DeclaredInitialSources')
        if not isinstance(sources, list):
            raise ValueError(f'Missing declared initial-source metadata: {case}')
        for source in sources:
            if (not isinstance(source, dict) or not {'Streaming', 'Select', 'Validation', 'HasDocument'} <= source.keys()
                    or any(type(source[key]) is not bool for key in ('Streaming', 'HasDocument'))
                    or any(source[key] is not None and not isinstance(source[key], str) for key in ('Select', 'Validation'))):
                raise ValueError(f'Invalid declared initial-source metadata: {case}')
        counts[status] += 1
        observations[case] = dict(Evaluation=entry, DeclaredInitialSources=sources)
    return dict(Verified=True, CapturedCases=len(cases), StatusCounts=dict(counts), Cases=observations,
                Scope='Primary evaluation results before assertions and source declarations; observations do not assign static applicability or waive failures.')


def audit(report, suite, manifest=None, engine=None, revision=None, require_forwarding=False, require_stylesheet_forwarding=False,
          require_evaluation=False):
    if revision is not None:
        verify_suite(suite, revision)
    header, cases, complete = load_report(report, suite, engine)
    forwarding = audit_forwarding(header, cases, require_forwarding, require_stylesheet_forwarding)
    evaluation = audit_evaluation(header, cases, require_evaluation)
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
        if require_stylesheet_forwarding:
            minimum = previous.get('StylesheetForwardCases')
            if (not isinstance(minimum, list) or not minimum or any(not isinstance(case, str) for case in minimum)
                    or len(set(minimum)) != len(minimum) or not set(minimum) <= cases.keys()):
                raise ValueError('A valid frozen stylesheet-forwarding case minimum is required')
            lost = sorted(set(minimum) - set(current['StylesheetForwardCases']))
            if lost:
                raise ValueError('Previously forwarding stylesheet cases lost execution evidence: ' + ', '.join(lost))
            forwarding['ExpectedMinimumStylesheetForwardCases'] = len(minimum)
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
    if forwarding is not None:
        summary['Forwarding'] = forwarding
    if evaluation is not None:
        summary['Evaluation'] = evaluation
    return summary, current


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--suite', type=Path, required=True)
    parser.add_argument('--suite-revision', help='Exact immutable suite commit; defaults to the frozen inventory pin')
    parser.add_argument('--engine', type=Path)
    parser.add_argument('--require-forwarding', action='store_true', help='Require complete actual runtime source-representation captures')
    parser.add_argument('--require-stylesheet-forwarding', action='store_true', help='Require explicit command stages and actual forwarding during stylesheet execution')
    parser.add_argument('--require-evaluation', action='store_true', help='Require primary evaluation diagnostics and declared initial-source metadata for every case')
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
        result, frozen = audit(args.report, args.suite, args.manifest, args.engine, revision, args.require_forwarding,
                               args.require_stylesheet_forwarding, args.require_evaluation)
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
