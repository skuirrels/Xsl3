#!/usr/bin/env python3
"""Account for P7's original S3-S7 conformance exits from a strict and an unstreamed run.

Both reports must cover the same population. Validate the strict report with
audit-streaming-runtime.py first. Outcome accounting is not proof of forward
execution, bounded memory or the stages' behavioural contracts.
"""
import argparse
from collections import Counter
import csv
import json
from pathlib import Path

FORK_AND_STATEFUL = ('si-fork', 'si-merge', 'si-iterate', 'si-for-each-group')

# The exit column of p7-streaming.md's stage table, set by set.
EXITS = {
    'S3': ('source-document, sf-*, sx-* and si-* other than fork, merge, iterate and for-each-group',
           lambda s: s == 'source-document' or s.startswith(('sf-', 'sx-'))
           or (s.startswith('si-') and s not in FORK_AND_STATEFUL)),
    'S4': ('si-apply-templates, si-next-match, si-apply-imports, su-* and streamable',
           lambda s: s in ('si-apply-templates', 'si-next-match', 'si-apply-imports', 'streamable') or s.startswith('su-')),
    'S5': ('accumulator, si-iterate, si-for-each-group, si-merge and merge',
           lambda s: s in ('accumulator', 'si-iterate', 'si-for-each-group', 'si-merge', 'merge')),
    'S6': ('si-fork', lambda s: s == 'si-fork'),
}

# The nine XTDE2220 cases named by the S5 exit, recovered by rerunning the 2026-10-22
# baseline (0c4fac0 with only a temporary streaming feature claim).
REQUIRED = {'S5': ['merge/merge-0' + n for n in ('70', '71', '76', '78', '79', '80', '81', '83', '84')]}

# SPEC section 22: the streaming tests at 90% or more, in strict mode.
S7_THRESHOLD = 0.90


def load(path, strict):
    rows = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()]
    if len(rows) < 3:
        raise ValueError(f'{path}: expected a header, cases and a completion record')
    header, complete = rows[0], rows[-1]
    if header.get('Kind') != 'run' or header.get('Profile') != 'strm' or header.get('SetPrefixes') not in (None, ''):
        raise ValueError(f'{path}: expected an unfiltered strm report')
    if header.get('Strict') is not strict:
        raise ValueError(f'{path}: expected Strict={strict}')
    if complete.get('Kind') != 'complete':
        raise ValueError(f'{path}: interrupted report')
    cases = {}
    for row in rows[1:-1]:
        if row.get('Kind') != 'case' or row.get('Case') in cases or row.get('Outcome') not in ('Pass', 'Fail', 'NotRun'):
            raise ValueError(f'{path}: unexpected record {row.get("Case")}')
        cases[row['Case']] = row
    if complete.get('Selected') != len(cases):
        raise ValueError(f'{path}: inconsistent total')
    return header, cases


def counts(rows):
    tally = Counter(row['Outcome'] for row in rows)
    return {'Pass': tally['Pass'], 'Fail': tally['Fail'], 'NotRun': tally['NotRun']}


def report(strict_path, baseline_path, dispositions_path):
    strict_header, strict = load(strict_path, True)
    baseline_header, baseline = load(baseline_path, False)
    if strict_header['CatalogSha256'] != baseline_header['CatalogSha256']:
        raise ValueError('The reports use different catalogues')
    if strict.keys() != baseline.keys():
        raise ValueError('The reports select different cases')
    changed = [case for case in strict if strict[case]['DefinitionSha256'] != baseline[case]['DefinitionSha256']]
    if changed:
        raise ValueError(f'Case definitions differ: {changed[:5]}')
    with dispositions_path.open(encoding='utf-8', newline='') as source:
        rows = list(csv.DictReader(source))
    dispositions = {row['case']: row['disposition'] for row in rows}
    # A named exit case that fails can only be met by an explicit owner decision on its disposition.
    accepted = {row['case'] for row in rows if (row.get('owner_decision') or '').startswith('accepted')}

    stages = {}
    for stage, (description, member) in EXITS.items():
        selected = sorted(case for case, row in strict.items() if member(row['Set']))
        regressions = [case for case in selected if baseline[case]['Outcome'] == 'Pass' and strict[case]['Outcome'] != 'Pass']
        non_passing = [{'Case': case, 'Outcome': strict[case]['Outcome'], 'Disposition': dispositions.get(case)}
                       for case in selected if strict[case]['Outcome'] != 'Pass']
        required = [{'Case': case, 'Outcome': strict[case]['Outcome'] if case in strict else 'Missing',
                     'Disposition': dispositions.get(case), 'OwnerAccepted': case in accepted}
                    for case in REQUIRED.get(stage, [])]
        stages[stage] = {
            'Exit': description,
            'Sets': sorted({strict[case]['Set'] for case in selected}),
            'Strict': counts(strict[case] for case in selected),
            'Unstreamed': counts(baseline[case] for case in selected),
            'Regressions': regressions,
            'Improvements': [case for case in selected if baseline[case]['Outcome'] != 'Pass' and strict[case]['Outcome'] == 'Pass'],
            'NonPassing': non_passing,
            'Unaccounted': [row['Case'] for row in non_passing if row['Disposition'] is None],
            'RequiredCases': required,
            'BaselineLevelMet': not regressions,
            'RequiredCasesPass': all(row['Outcome'] == 'Pass' for row in required),
            'RequiredCasesMet': all(row['Outcome'] == 'Pass' or row['OwnerAccepted'] for row in required),
        }
    total = counts(strict.values())
    rate = total['Pass'] / max(1, total['Pass'] + total['Fail'])
    stages['S7'] = {'Exit': 'the streaming tests at 90% or more in strict mode', 'Strict': total,
                    'PassRate': round(rate, 6), 'Threshold': S7_THRESHOLD, 'ConformanceMet': rate >= S7_THRESHOLD}
    return {
        'FormatVersion': 1,
        'Scope': 'Conformance outcomes only; forward execution, memory, throughput and behavioural contracts need separate evidence.',
        'CatalogSha256': strict_header['CatalogSha256'],
        'StrictEngineSha256': strict_header['EngineSha256'],
        'UnstreamedEngineSha256': baseline_header['EngineSha256'],
        'Stages': stages,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--strict', type=Path, required=True, help='Audited strict strm export')
    parser.add_argument('--unstreamed', type=Path, required=True, help='strm export of the same population without XSL3_STREAM_STRICT')
    parser.add_argument('--dispositions', type=Path, required=True, help='CSV of reviewed non-passing cases (case, disposition, optional owner_decision)')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = report(args.strict, args.unstreamed, args.dispositions)
    with args.output.open('x', encoding='utf-8') as target:
        json.dump(result, target, indent=2)
        target.write('\n')
    for stage, data in result['Stages'].items():
        print(stage, json.dumps({k: v for k, v in data.items() if k in ('Strict', 'Unstreamed', 'PassRate', 'BaselineLevelMet', 'RequiredCasesMet', 'ConformanceMet')}))


if __name__ == '__main__':
    main()
