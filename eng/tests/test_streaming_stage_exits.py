"""Stage exit accounting must expose regressions, missing dispositions and named-case failures."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('stage_exit_report', Path(__file__).parents[1] / 'stage-exit-report.py')
EXITS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXITS)


class StageExitReportTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.dispositions = self.root / 'dispositions.csv'
        self.dispositions.write_text('case,disposition\n')
        outcomes = {'si-copy/a': 'Pass', 'su-filter/b': 'Pass', 'si-fork/c': 'Pass'}
        outcomes.update({case: 'Pass' for case in EXITS.REQUIRED['S5']})
        self.strict = dict(outcomes)
        self.unstreamed = dict(outcomes)

    def write(self, name, outcomes, strict):
        rows = [dict(Kind='run', Profile='strm', Strict=strict, SetPrefixes=None, CatalogSha256='c' * 64, EngineSha256='e' * 64)]
        rows += [dict(Kind='case', Case=case, Set=case.split('/')[0], DefinitionSha256='d' * 64, Outcome=outcome)
                 for case, outcome in outcomes.items()]
        rows.append(dict(Kind='complete', Selected=len(outcomes)))
        path = self.root / name
        path.write_text(''.join(json.dumps(row) + '\n' for row in rows))
        return path

    def run_report(self):
        return EXITS.report(self.write('strict.jsonl', self.strict, True),
                            self.write('unstreamed.jsonl', self.unstreamed, False), self.dispositions)['Stages']

    def test_all_passing_meets_every_exit(self):
        stages = self.run_report()
        for stage in ('S3', 'S4', 'S5', 'S6'):
            self.assertTrue(stages[stage]['BaselineLevelMet'], stage)
            self.assertTrue(stages[stage]['RequiredCasesMet'], stage)
        self.assertTrue(stages['S7']['ConformanceMet'])

    def test_strict_regression_fails_baseline_level(self):
        self.strict['si-copy/a'] = 'Fail'
        stages = self.run_report()
        self.assertEqual(stages['S3']['Regressions'], ['si-copy/a'])
        self.assertFalse(stages['S3']['BaselineLevelMet'])
        self.assertEqual(stages['S3']['Unaccounted'], ['si-copy/a'])

    def test_disposition_accounts_for_failure_but_not_regression(self):
        self.strict['si-copy/a'] = 'Fail'
        self.dispositions.write_text('case,disposition\nsi-copy/a,fixture conflict\n')
        stages = self.run_report()
        self.assertEqual(stages['S3']['Unaccounted'], [])
        self.assertFalse(stages['S3']['BaselineLevelMet'])

    def test_named_merge_case_must_pass(self):
        self.strict['merge/merge-084'] = self.unstreamed['merge/merge-084'] = 'Fail'
        self.dispositions.write_text('case,disposition,owner_decision\nmerge/merge-084,fixture conflict,\n')
        stages = self.run_report()
        self.assertTrue(stages['S5']['BaselineLevelMet'])
        self.assertFalse(stages['S5']['RequiredCasesMet'])

    def test_owner_accepted_disposition_meets_a_named_case_without_passing_it(self):
        self.strict['merge/merge-084'] = self.unstreamed['merge/merge-084'] = 'Fail'
        self.dispositions.write_text('case,disposition,owner_decision\nmerge/merge-084,fixture conflict,accepted 2026-11-01\n')
        stages = self.run_report()
        self.assertFalse(stages['S5']['RequiredCasesPass'])
        self.assertTrue(stages['S5']['RequiredCasesMet'])

    def test_unstreamed_improvement_is_reported(self):
        self.unstreamed['su-filter/b'] = 'Fail'
        self.assertEqual(self.run_report()['S4']['Improvements'], ['su-filter/b'])

    def test_different_populations_are_rejected(self):
        del self.unstreamed['si-fork/c']
        with self.assertRaisesRegex(ValueError, 'different cases'):
            self.run_report()

    def test_wrong_strict_flag_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Strict=True'):
            EXITS.report(self.write('a.jsonl', self.strict, False), self.write('b.jsonl', self.unstreamed, False), self.dispositions)


if __name__ == '__main__':
    unittest.main()
