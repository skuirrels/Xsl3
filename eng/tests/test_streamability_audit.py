"""Coverage failures must not become a successful inspection report."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('streamability_audit', Path(__file__).parents[1] / 'audit-streamability.py')
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class StreamabilityAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.sandp = self.root / 'tests/sandp'
        self.sandp.mkdir(parents=True)
        # The published axis/path examples: a consumes; @code is striding/motionless.
        (self.sandp / '_base-expressions.xml').write_text('''<expressions>
          <productions axis="child"><production node-type="element">a</production></productions>
          <productions axis="attribute"><production node-type="attribute">@code</production></productions>
        </expressions>''', encoding='utf-8')
        self.case_file = self.sandp / '_example-test-set.xml'
        self.case_file.write_text('''<test-set xmlns="http://www.w3.org/2012/10/xslt-test-catalog" name="example">
          <test-case name="path"><test><posture-and-sweep context-posture="striding">
            <xpath part="1">a/@code</xpath>
          </posture-and-sweep></test><result>
            <assert-posture-and-sweep result-posture="climbing" result-sweep="consuming"/>
          </result></test-case></test-set>''', encoding='utf-8')
        self.export = self.root / 'assessments.jsonl'
        self.row = {'Case': 'example/path', 'Part': '1', 'Expression': 'a/@code', 'Posture': 'Striding', 'Sweep': 'Consuming'}
        self.write_rows(self.row)

    def write_rows(self, *rows):
        self.export.write_text(''.join(json.dumps(r) + '\n' for r in rows), encoding='utf-8')

    def test_reports_a_fixture_conflict_without_changing_the_source(self):
        original = self.case_file.read_bytes()
        result = AUDIT.audit(self.root, self.export)
        self.assertEqual({'uniform-expectation-conflict': 1}, result['Dispositions'])
        self.assertEqual([], result['ReferenceDisagreements'])
        self.assertEqual(original, self.case_file.read_bytes())

    def test_records_an_engine_disagreement_instead_of_copying_its_answer(self):
        self.write_rows(dict(self.row, Posture='Climbing'))
        result = AUDIT.audit(self.root, self.export)
        self.assertEqual(1, len(result['ReferenceDisagreements']))
        self.assertEqual(('striding', 'consuming'), result['ReferenceDisagreements'][0]['Reference'])

    def test_rejects_missing_duplicate_changed_and_unexpected_assessments(self):
        scenarios = [(), (self.row, self.row), (dict(self.row, Expression='a/a'),),
                     (self.row, dict(self.row, Case='unknown/path'))]
        for rows in scenarios:
            with self.subTest(rows=rows):
                self.write_rows(*rows)
                with self.assertRaises(ValueError):
                    AUDIT.audit(self.root, self.export)

    def test_rejects_syntax_outside_the_generator_instead_of_guessing(self):
        base = AUDIT.productions(self.sandp / '_base-expressions.xml')
        with self.assertRaises(ValueError):
            AUDIT.derive('a/unknown()', base, 'striding')

    def test_empty_generated_cases_remain_invalid(self):
        text = self.case_file.read_text(encoding='utf-8')
        empty = '''<test-case name="empty"><test><posture-and-sweep context-posture="striding"/></test>
          <result><assert-posture-and-sweep result-posture="grounded" result-sweep="motionless"/></result></test-case>'''
        self.case_file.write_text(text.replace('</test-set>', empty + '</test-set>'), encoding='utf-8')
        result = AUDIT.audit(self.root, self.export)
        self.assertEqual(1, result['Dispositions']['empty-generator-output'])
        self.assertEqual(2, result['Cases'])
        self.assertEqual(1, result['Expressions'])


if __name__ == '__main__':
    unittest.main()
