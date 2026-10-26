#!/usr/bin/env python3
"""Prepare the owner-approved P7B reviewed fixtures without editing the official suite.

Expectations come from the independent axis/path rules and the canonical books fixture,
never from Xsl3 execution or its exported assessments. Use a fresh output directory.
"""
import argparse
from collections import defaultdict
from copy import deepcopy
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import tempfile
from xml.dom import minidom
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location('streamability_audit', Path(__file__).with_name('audit-streamability.py'))
REFERENCE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REFERENCE)
CAT = REFERENCE.NS['t']
XSL = 'http://www.w3.org/1999/XSL/Transform'


def elements(node, name):
    return [n for n in node.childNodes if n.nodeType == n.ELEMENT_NODE and n.namespaceURI == CAT and n.localName == name]


def annotate(node, message):
    descriptions = elements(node, 'description')
    description = descriptions[0] if descriptions else node.ownerDocument.createElementNS(CAT, 'description')
    original = text(description)
    for child in list(description.childNodes):
        description.removeChild(child)
    description.appendChild(node.ownerDocument.createTextNode(message + '\nOriginal generator description (historical):\n' + original))
    if not descriptions:
        node.insertBefore(description, node.firstChild)


def copy_node(node):
    clone = deepcopy(node)
    clone.tail = None  # XDM copies the selected node, not following sibling whitespace.
    return clone


def text(node):
    return ''.join(n.data for n in node.childNodes if n.nodeType in (n.TEXT_NODE, n.CDATA_SECTION_NODE))


def prepare(source, destination, revision, expected_population=None):
    source, destination = Path(source).resolve(), Path(destination).resolve()
    if destination.exists() or destination.is_relative_to(source):
        raise ValueError('Output must be a fresh directory outside the official suite')
    destination.parent.mkdir(parents=True, exist_ok=True)
    sources, generated, mappings, sets = {}, {}, [], []

    def original(relative):
        path = source / relative
        sources[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
        return path

    base = REFERENCE.productions(original('tests/sandp/_base-expressions.xml'))
    original('tests/sandp/_create-sandp.xsl')
    original('catalog.xml')
    total_parts = 0
    corrected_count = 0
    retired = []
    with tempfile.TemporaryDirectory(prefix='p7b-review-', dir=destination.parent) as temporary:
        staging = Path(temporary)

        def write(relative, data):
            path = staging / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            generated[relative] = hashlib.sha256(data).hexdigest()

        for path in sorted((source / 'tests/sandp').rglob('_*-test-set.xml')):
            relative = path.relative_to(source).as_posix()
            dom = minidom.parse(str(original(relative)))
            root = dom.documentElement
            annotate(root, 'P7B reviewed groups; each case asserts its derived posture and sweep.')
            for case in list(elements(root, 'test-case')):
                original_id = root.getAttribute('name') + '/' + case.getAttribute('name')
                input_ = elements(elements(case, 'test')[0], 'posture-and-sweep')[0]
                if input_.getAttribute('context-type') not in ('', 'item()'):
                    raise ValueError(f'Unsupported context type: {original_id}')
                context = input_.getAttribute('context-posture')
                if context not in ('striding', 'crawling', 'climbing', 'grounded', 'roaming'):
                    raise ValueError(f'Unknown context posture: {original_id}')
                if any(n.nodeType == n.ELEMENT_NODE and (n.namespaceURI != CAT or n.localName != 'xpath') for n in input_.childNodes):
                    raise ValueError(f'Unsupported input: {original_id}')
                groups, parts = defaultdict(list), set()
                for expression in elements(input_, 'xpath'):
                    part = expression.getAttribute('part')
                    if not part or part in parts:
                        raise ValueError(f'Missing/duplicate source part: {original_id}/{part}')
                    parts.add(part)
                    groups[REFERENCE.derive(text(expression), base, context)].append(expression)
                mapping = {'OriginalId': original_id, 'Groups': []}
                if not groups:
                    description = text(elements(case, 'description')[0])
                    left = re.search(r'Base expression:\s*([^\n]+)', description)
                    if not left or 'node-type-unequal' not in description or base[left.group(1).strip()][1] != REFERENCE.NODES:
                        raise ValueError(f'Unexplained empty source fixture: {original_id}')
                    mapping['InvalidReason'] = 'Generator node-type-unequal excludes a left production of any-node; no candidates exist.'
                    retired.append(original_id)
                for (posture, sweep), expressions in sorted(groups.items()):
                    clone = case.cloneNode(deep=True)
                    name = case.getAttribute('name')
                    if len(groups) > 1:
                        name += '--' + posture + '-' + sweep
                    clone.setAttribute('name', name)
                    annotate(clone, f'P7B reviewed {posture}/{sweep}; source {original_id}; original part IDs retained.')
                    clone_input = elements(elements(clone, 'test')[0], 'posture-and-sweep')[0]
                    for child in list(clone_input.childNodes):
                        clone_input.removeChild(child)
                    for expression in expressions:
                        clone_input.appendChild(expression.cloneNode(deep=True))
                    result = elements(clone, 'result')[0]
                    assertions = elements(result, 'assert-posture-and-sweep')
                    if len(assertions) != 1:
                        raise ValueError(f'Unsupported assertion: {original_id}')
                    assertions[0].setAttribute('result-posture', posture)
                    assertions[0].setAttribute('result-sweep', sweep)
                    root.insertBefore(clone, case)
                    mapping['Groups'].append({'ReviewedId': root.getAttribute('name') + '/' + name,
                                              'Parts': [e.getAttribute('part') for e in expressions],
                                              'Posture': posture, 'Sweep': sweep})
                    total_parts += len(expressions)
                    corrected_count += 1
                root.removeChild(case)
                mappings.append(mapping)
            write(relative, dom.toxml(encoding='utf-8'))
            sets.append({'OriginalFile': relative, 'ReviewedFile': relative})

        # Positive results are derived from fixture data and instruction semantics independently.
        books_path = original('tests/strm/docs/books.xml')
        books = ET.parse(books_path).getroot().findall('./BOOKS/ITEM')
        if len(books) < 2:
            raise ValueError('The positive peer-union fixture needs both nonempty positional partitions')
        corrections = []
        fork_file = 'tests/strm/si-fork/_si-fork-test-set.xml'
        original(fork_file)
        fork_style = ET.parse(original('tests/strm/si-fork/si-fork-953.xsl')).getroot()
        group = fork_style.find('.//{' + XSL + '}for-each-group')
        if group is None or group.get('select') != '/BOOKLIST/BOOKS/ITEM' or group.get('group-by') != '@CAT' or group.find('{' + XSL + '}sort').get('select') != 'position()' or group.find('.//{' + XSL + '}sequence').get('select') != 'current-group()/AUTHOR':
            raise ValueError('The reviewed fork source shape changed')
        grouped = {}
        for book in books:
            if not book.get('CAT') or book.find('AUTHOR') is None:
                raise ValueError('The grouping source lacks a category or author')
            grouped.setdefault(book.attrib['CAT'], []).extend(copy_node(n) for n in book.findall('AUTHOR'))
        expected_fork = ET.Element('out')
        for authors in grouped.values():
            ET.SubElement(expected_fork, 'in').extend(authors)
        expected_union = ET.Element('out')
        for book in books:
            prices = book.findall('PRICE')
            if not prices:
                raise ValueError('The positive union source lacks a price')
            expected_union.extend(copy_node(n) for n in prices)

        union_file = 'tests/strm/sx-UnionExpr/_sx-UnionExpr-test-set.xml'
        original(union_file)
        union_dom = minidom.parse(str(original('tests/strm/sx-UnionExpr/sx-union-202.xsl')))
        apply = union_dom.getElementsByTagNameNS(XSL, 'apply-templates')[0]
        old_select = apply.getAttribute('select')
        if old_select != '(/BOOKLIST/ITEM | /BOOKLIST/MAGAZINE)/PRICE':
            raise ValueError('The reviewed union source shape changed')
        # The official fixture has neither direct ITEM nor MAGAZINE children. Target its real
        # books in disjoint odd/even peer partitions, so both arms contribute and document order matters.
        new_select = '(/BOOKLIST/BOOKS/ITEM[position() mod 2 = 0] | /BOOKLIST/BOOKS/ITEM[position() mod 2 = 1])/PRICE'
        apply.setAttribute('select', new_select)
        union_dom.getElementsByTagNameNS(XSL, 'source-document')[0].setAttribute('href', books_path.as_uri())
        union_style = 'static/sx-union-202.xsl'
        write(union_style, union_dom.toxml(encoding='utf-8'))
        for case_id, expected, authority, stylesheet in (
            ('si-fork/si-fork-953', expected_fork, 'XSLT 3.0 §19.8.4.19 direct fork child sort exception', None),
            ('sx-UnionExpr/sx-union-202', expected_union, 'XSLT 3.0 §19.10 permitted compiler peer-union rewrite', union_style),
        ):
            assertion = ET.Element('{' + CAT + '}assert-xml')
            assertion.text = ET.tostring(expected, encoding='unicode')
            name = 'static/' + case_id.split('/')[1] + '-result.xml'
            write(name, ET.tostring(assertion, encoding='utf-8', xml_declaration=True))
            row = {'OriginalId': case_id, 'OriginalExpectedCode': 'XTSE3430', 'AssertionFile': name,
                   'StylesheetFile': stylesheet, 'Authority': authority}
            if stylesheet:
                row.update({'OriginalSelect': old_select, 'ReviewedSelect': new_select,
                            'Reason': 'Correct the empty fixture selection; both peer partitions must contribute.',
                            'OddItems': len(books[::2]), 'EvenItems': len(books[1::2])})
            corrections.append(row)
        if len({m['OriginalId'] for m in mappings}) != len(mappings):
            raise ValueError('Duplicate source case IDs')
        manifest = {'Version': 1, 'Review': 'P7B owner-approved 1000-case correction', 'SuiteRevision': revision,
                    'DefinitionHashes': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (Path(__file__), Path(REFERENCE.__file__))},
                    'SourceHashes': sources, 'GeneratedHashes': generated, 'Sets': sets, 'Mappings': mappings,
                    'OriginalCases': len(mappings), 'ReviewedCases': corrected_count, 'Expressions': total_parts,
                    'InvalidCases': retired, 'StaticCorrections': corrections}
        population = (len(mappings), corrected_count, total_parts, len(retired))
        if expected_population is not None and population != expected_population:
            raise ValueError('Approved P7B population changed; reconcile the source and plan')
        (staging / 'review.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        staging.rename(destination)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    fetch = Path(__file__).with_name('fetch-w3c.sh').read_text(encoding='utf-8')
    pinned = re.search(r'^XSLT30_PIN=([0-9a-f]{40})$', fetch, re.M)
    if pinned is None:
        raise ValueError('No canonical XSLT30_PIN in eng/fetch-w3c.sh')
    pin = pinned.group(1)
    revision = subprocess.check_output(['git', '-C', str(args.suite), 'rev-parse', 'HEAD'], text=True).strip()
    dirty = subprocess.check_output(['git', '-C', str(args.suite), 'status', '--porcelain', '--untracked-files=no'], text=True)
    if revision != pin or dirty.strip():
        raise ValueError('Prepare reviewed fixtures from the clean suite revision pinned by eng/fetch-w3c.sh')
    manifest = prepare(args.suite, args.output, revision, expected_population=(919, 1000, 66643, 8))
    print(json.dumps({key: manifest[key] for key in ('SuiteRevision', 'OriginalCases', 'ReviewedCases', 'Expressions', 'InvalidCases')}))


if __name__ == '__main__':
    main()
