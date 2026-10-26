#!/usr/bin/env python3
"""Audit the pinned sandp generator's two-axis paths, independently of Xsl3.

This is an inspection tool, not an alternate conformance runner or fixture repair.
Rule authority: XSLT 3.0 §§19.1.1, 19.8.8.8–9 and 19.8.10. Production spellings
and node-kind constraints come from the suite's canonical _base-expressions.xml.
Unknown syntax, incomplete exports and duplicated assessments are errors.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

NS = {'t': 'http://www.w3.org/2012/10/xslt-test-catalog'}
NODES = frozenset(('document', 'element', 'attribute', 'text', 'comment', 'pi', 'namespace'))
PARENTS = frozenset(('document', 'element'))
CHILDREN = frozenset(('element', 'text', 'comment', 'pi'))
TYPES = {name: frozenset((name,)) for name in NODES}
TYPES.update({'elem-doc': PARENTS, 'any-node': NODES, 'empty': frozenset(), 'error': frozenset()})
AXES = frozenset(('self', 'child', 'attribute', 'namespace', 'descendant', 'descendant-or-self',
                  'parent', 'ancestor', 'ancestor-or-self', 'following', 'preceding', 'following-sibling', 'preceding-sibling'))
PATTERN_AXES = frozenset(('self', 'child', 'attribute', 'namespace', 'descendant', 'descendant-or-self'))
SWEEPS = ('motionless', 'consuming', 'free-ranging')


def productions(path):
    result = {}
    for group in ET.parse(path).getroot().findall('productions'):
        axis = group.get('axis')
        if axis not in AXES:
            raise ValueError(f'Unsupported production axis: {axis}')
        for node in group.findall('production'):
            expression = (node.text or '').strip()
            value = (axis, TYPES[node.get('node-type')])
            if not expression or expression in result:
                raise ValueError(f'Empty/duplicate production: {expression}')
            result[expression] = value
    if not result:
        raise ValueError('No base productions')
    return result


def step(production, context_type, context_posture):
    """Reachable kinds (§19.1.1), then the ordered axis rules (§19.8.8.9)."""
    axis, node_kinds = production
    if not context_type:
        reachable = frozenset()
    elif axis == 'self':
        reachable = context_type
    elif axis in ('attribute', 'namespace'):
        reachable = frozenset((axis,)) if 'element' in context_type else frozenset()
    elif axis in ('child', 'descendant'):
        reachable = CHILDREN if context_type & PARENTS else frozenset()
    elif axis == 'descendant-or-self':
        reachable = context_type | (CHILDREN if context_type & PARENTS else frozenset())
    elif axis in ('parent', 'ancestor', 'ancestor-or-self'):
        reachable = frozenset() if context_type == {'document'} else PARENTS
        if axis == 'ancestor-or-self':
            reachable |= context_type
    elif axis in ('following', 'preceding', 'following-sibling', 'preceding-sibling'):
        reachable = frozenset() if context_type == {'document'} else CHILDREN
    else:
        raise ValueError(f'Unsupported axis: {axis}')
    result_type = reachable & node_kinds
    if context_posture == 'grounded':
        posture, sweep = 'grounded', 0
    elif context_posture == 'roaming':
        posture, sweep = 'roaming', 2
    elif not result_type:
        posture, sweep = 'grounded', 0
    elif context_posture == 'climbing' and axis in ('self', 'parent', 'ancestor', 'ancestor-or-self'):
        posture, sweep = 'climbing', 0
    elif axis in ('attribute', 'namespace'):
        posture, sweep = 'striding', 0
    elif axis in ('parent', 'ancestor', 'ancestor-or-self'):
        posture, sweep = 'climbing', 0
    elif axis == 'self':
        posture, sweep = ('crawling' if context_posture == 'crawling' and 'element' in result_type else 'striding'), 0
    elif context_posture == 'striding' and axis == 'child':
        posture, sweep = 'striding', 1
    elif context_posture == 'striding' and axis in ('descendant', 'descendant-or-self'):
        posture, sweep = ('crawling' if 'element' in result_type else 'striding'), 1
    else:
        posture, sweep = 'roaming', 2
    return result_type, posture, sweep


def derive(expression, base, context_posture):
    # Resolve complete productions, so slashes inside Q{http://...} are never path separators.
    pairs = [(base[expression[:i]], base[expression[i + 1:]])
             for i, char in enumerate(expression) if char == '/' and expression[:i] in base and expression[i + 1:] in base]
    if len(pairs) != 1:
        raise ValueError(f'Not a unique generated two-axis path: {expression}')
    left, right = pairs[0]
    left_type, left_posture, left_sweep = step(left, NODES, context_posture)
    result_type, posture, right_sweep = step(right, left_type, left_posture)
    sweep = max(left_sweep, right_sweep)
    # These productions contain no predicates; the downward axis pairs are motionless patterns.
    if posture == 'roaming' and context_posture in ('striding', 'crawling') and left[0] in PATTERN_AXES and right[0] in PATTERN_AXES:
        posture, sweep = ('crawling' if 'element' in result_type else 'striding'), 1
    return posture, SWEEPS[sweep]


def audit(suite, assessments):
    suite, assessments = Path(suite), Path(assessments)
    base_file = suite / 'tests/sandp/_base-expressions.xml'
    base = productions(base_file)
    observed = {}
    with assessments.open(encoding='utf-8') as stream:
        for line in stream:
            row = json.loads(line)
            key = (row['Case'], row['Part'])
            if key in observed:
                raise ValueError(f'Duplicate assessment: {key}')
            observed[key] = row
    cases, mismatches, seen = [], [], set()
    hashes = {'tests/sandp/_base-expressions.xml': hashlib.sha256(base_file.read_bytes()).hexdigest()}
    expression_count = 0
    for file in sorted((suite / 'tests/sandp').rglob('_*-test-set.xml')):
        hashes[str(file.relative_to(suite))] = hashlib.sha256(file.read_bytes()).hexdigest()
        test_set = ET.parse(file).getroot()
        for test in test_set.findall('t:test-case', NS):
            case_id = test_set.get('name') + '/' + test.get('name')
            if case_id in seen:
                raise ValueError(f'Duplicate source case: {case_id}')
            seen.add(case_id)
            input_ = test.find('t:test/t:posture-and-sweep', NS)
            expected = test.find('t:result/t:assert-posture-and-sweep', NS)
            if input_ is None or expected is None or input_.get('context-type', 'item()') != 'item()':
                raise ValueError(f'Unsupported fixture structure/context: {case_id}')
            context = input_.get('context-posture')
            if context not in ('striding', 'crawling', 'climbing', 'grounded', 'roaming'):
                raise ValueError(f'Unknown context posture: {context}')
            if any(child.tag != '{' + NS['t'] + '}xpath' for child in input_):
                raise ValueError(f'Unsupported fixture input: {case_id}')
            target = (expected.get('result-posture'), expected.get('result-sweep'))
            groups, wrong = Counter(), 0
            for expression in input_:
                key = (case_id, expression.get('part'))
                row = observed.pop(key, None)
                if row is None or row['Expression'] != expression.text:
                    raise ValueError(f'Missing or changed assessment: {key}')
                actual = (row['Posture'].lower(), row['Sweep'].lower().replace('freeranging', 'free-ranging'))
                reference = derive(expression.text, base, context)
                if actual != reference:
                    mismatches.append({'Case': case_id, 'Part': expression.get('part'), 'Actual': actual, 'Reference': reference})
                groups['/'.join(reference)] += 1
                wrong += reference != target
                expression_count += 1
            description = test.findtext('t:description', '', NS)
            cases.append({'Case': case_id, 'Source': str(file.relative_to(suite)), 'Expressions': len(input_),
                          'Expected': '/'.join(target), 'DerivedGroups': dict(groups), 'ConflictingExpressions': wrong,
                          'Disposition': 'empty-generator-output' if len(input_) == 0 else 'mixed-expectations' if len(groups) > 1 else 'uniform-expectation-conflict' if wrong else 'matches',
                          'GeneratorDescription': description.strip() if len(input_) == 0 else None})
    if observed:
        raise ValueError(f'Unexpected assessment: {next(iter(observed))}')
    if not cases or not expression_count:
        raise ValueError('Empty source or assessment inventory')
    counts = Counter(row['Disposition'] for row in cases)
    return {'Authority': 'XSLT 3.0 §§19.1.1, 19.8.8.8–9, 19.8.10; pinned base production constraints',
            'Note': 'Independent inspection only; original suite assertions and the S1 conformance gate are unchanged.',
            'Cases': len(cases), 'Expressions': expression_count, 'Dispositions': dict(counts),
            'ReferenceDisagreements': mismatches, 'SourceHashes': hashes, 'CaseDetails': cases}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', type=Path, required=True)
    parser.add_argument('--assessments', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.suite, args.assessments)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key not in ('CaseDetails', 'SourceHashes', 'ReferenceDisagreements')}))
    print(f"Reference disagreements: {len(result['ReferenceDisagreements'])}")
    return bool(result['ReferenceDisagreements'])


if __name__ == '__main__':
    raise SystemExit(main())
