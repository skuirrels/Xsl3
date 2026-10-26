"""Reviewed expectations preserve sources and never depend on engine observations."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from xml.dom import minidom
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location('prepare_streamability', Path(__file__).parents[1] / 'prepare-streamability-suite.py')
PREPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREPARE)
CAT = PREPARE.CAT
XSL = PREPARE.XSL


class PrepareStreamabilitySuiteTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.output = self.root / 'reviewed'
        self.write('catalog.xml', f'<catalog xmlns="{CAT}"/>')
        self.write('tests/sandp/_create-sandp.xsl', '<generator invariant="node-type-unequal"/>')
        self.write('tests/sandp/_base-expressions.xml', '''<expressions>
          <productions axis="child"><production node-type="element">ex:a</production><production node-type="any-node">node()</production></productions>
          <productions axis="attribute"><production node-type="attribute">@code</production></productions>
          <productions axis="parent"><production node-type="elem-doc">..</production></productions>
        </expressions>''')
        self.set_file = 'tests/sandp/_example-test-set.xml'
        self.write(self.set_file, f'''<test-set xmlns="{CAT}" xmlns:ex="urn:source" name="example">
          <test-case name="mixed"><test><posture-and-sweep context-posture="striding">
            <xpath part="1">ex:a/@code</xpath><xpath part="2">ex:a/..</xpath>
          </posture-and-sweep></test><result><assert-posture-and-sweep result-posture="climbing" result-sweep="consuming"/></result></test-case>
          <test-case name="empty"><description>Base expression: node()
            Invariant: node-type-unequal</description><test><posture-and-sweep context-posture="striding"/></test>
            <result><assert-posture-and-sweep result-posture="grounded" result-sweep="motionless"/></result></test-case>
        </test-set>''')
        for name in ('si-fork', 'sx-UnionExpr'):
            self.write(f'tests/strm/{name}/_{name}-test-set.xml', '<source/>')
        self.write('tests/strm/docs/books.xml', '''<BOOKLIST><BOOKS>
          <ITEM CAT="a"><AUTHOR>A&amp;B</AUTHOR><PRICE>1.25</PRICE></ITEM>
          <ITEM CAT="b"><AUTHOR>Second</AUTHOR><PRICE>2.75</PRICE></ITEM>
          <ITEM CAT="a"><AUTHOR>Third</AUTHOR><PRICE>3</PRICE></ITEM>
        </BOOKS></BOOKLIST>''')
        self.write('tests/strm/si-fork/si-fork-953.xsl', f'''<xsl:stylesheet xmlns:xsl="{XSL}" version="3.0">
          <xsl:for-each-group select="/BOOKLIST/BOOKS/ITEM" group-by="@CAT"><xsl:sort select="position()"/>
          <xsl:sequence select="current-group()/AUTHOR"/></xsl:for-each-group></xsl:stylesheet>''')
        self.write('tests/strm/sx-UnionExpr/sx-union-202.xsl', f'''<xsl:stylesheet xmlns:xsl="{XSL}" version="3.0">
          <xsl:source-document href="../docs/books.xml"><xsl:apply-templates select="(/BOOKLIST/ITEM | /BOOKLIST/MAGAZINE)/PRICE"/>
          </xsl:source-document></xsl:stylesheet>''')

    def write(self, name, data):
        path = self.source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(data, encoding='utf-8')

    def prepare(self):
        return PREPARE.prepare(self.source, self.output, 'fixture')

    def test_preserves_all_parts_bindings_and_original_files(self):
        before = {p.relative_to(self.source): p.read_bytes() for p in self.source.rglob('*') if p.is_file()}
        manifest = self.prepare()
        self.assertEqual((2, 2, 2), tuple(manifest[k] for k in ('OriginalCases', 'ReviewedCases', 'Expressions')))
        self.assertEqual(['example/empty'], manifest['InvalidCases'])
        self.assertEqual({'1', '2'}, {p for g in manifest['Mappings'][0]['Groups'] for p in g['Parts']})
        corrected = minidom.parse(str(self.output / self.set_file))
        self.assertEqual('urn:source', corrected.documentElement.getAttribute('xmlns:ex'))
        self.assertEqual(before, {p.relative_to(self.source): p.read_bytes() for p in self.source.rglob('*') if p.is_file()})

    def test_positive_results_are_computed_from_source_and_remove_only_sibling_whitespace(self):
        manifest = self.prepare()
        fork, union = [ET.fromstring(ET.parse(self.output / c['AssertionFile']).getroot().text) for c in manifest['StaticCorrections']]
        self.assertEqual([['A&B', 'Third'], ['Second']], [[n.text for n in g] for g in fork])
        self.assertEqual(['1.25', '2.75', '3'], [n.text for n in union])
        self.assertTrue(all(n.tail is None for n in fork.iter() if n is not fork))
        correction = manifest['StaticCorrections'][1]
        self.assertEqual((2, 1), (correction['OddItems'], correction['EvenItems']))
        style = ET.parse(self.output / correction['StylesheetFile'])
        self.assertEqual(correction['ReviewedSelect'], style.find('.//{' + XSL + '}apply-templates').get('select'))

    def test_generation_is_deterministic(self):
        first = self.prepare()
        second = PREPARE.prepare(self.source, self.root / 'second', 'fixture')
        self.assertEqual(first, second)
        self.assertEqual((self.output / 'review.json').read_bytes(), (self.root / 'second/review.json').read_bytes())

    def test_rejects_existing_or_nested_output(self):
        self.output.mkdir()
        with self.assertRaises(ValueError):
            self.prepare()
        with self.assertRaises(ValueError):
            PREPARE.prepare(self.source, self.source / 'nested', 'fixture')

    def test_rejects_missing_or_duplicate_parts_without_publishing_output(self):
        for changed in ('part="1"', ''):
            with self.subTest(changed=changed):
                path = self.source / self.set_file
                original = path.read_text(encoding='utf-8')
                path.write_text(original.replace('part="2"', changed), encoding='utf-8')
                with self.assertRaises(ValueError):
                    self.prepare()
                self.assertFalse(self.output.exists())
                path.write_text(original, encoding='utf-8')

    def test_rejects_unexplained_empty_cases(self):
        path = self.source / self.set_file
        path.write_text(path.read_text(encoding='utf-8').replace('node-type-unequal', 'unknown-invariant'), encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'Unexplained empty'):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_missing_positive_source_data_is_not_filled_with_constants(self):
        self.write('tests/strm/docs/books.xml', '<BOOKLIST><BOOKS/></BOOKLIST>')
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_population_mismatch_is_rejected_before_publication(self):
        with self.assertRaisesRegex(ValueError, 'population changed'):
            PREPARE.prepare(self.source, self.output, 'fixture', expected_population=(919, 1000, 66643, 8))
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
