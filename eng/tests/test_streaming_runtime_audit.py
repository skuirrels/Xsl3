"""Acceptance evidence must fail closed when inventory or execution data is lost."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import subprocess
import unittest

SPEC = importlib.util.spec_from_file_location('runtime_audit', Path(__file__).parents[1] / 'audit-streaming-runtime.py')
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class RuntimeAuditTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        (self.root / 'catalog.xml').write_text('<catalog/>')
        (self.root / 'set.xml').write_text('<test-set/>')
        self.engine = self.root / 'engine.dll'
        self.engine.write_bytes(b'test engine')
        self.report = self.root / 'run.jsonl'
        self.manifest = self.root / 'manifest.json'
        self.rows = [
            dict(Kind='run', FormatVersion=1, Profile='strm', Strict=True, SetPrefixes=None,
                 Api=True, Configuration='Release', Framework='.NET test',
                 CatalogSha256=AUDIT.digest(self.root / 'catalog.xml'), EngineSha256=AUDIT.digest(self.engine)),
            dict(Kind='case', Case='si-copy/example', Set='si-copy', SetFile='set.xml',
                 SetSha256=AUDIT.digest(self.root / 'set.xml'), DefinitionSha256='a' * 64, Outcome='Pass', Detail=''),
            dict(Kind='complete', Selected=1, Passed=1, Failed=0, NotRun=0,
                 UnmatchedExpectedErrorCodes=0, NotRunReasons={})]
        self.write()
        _, frozen = AUDIT.audit(self.report, self.root, engine=self.engine)
        self.manifest.write_text(json.dumps(frozen))

    def write(self):
        self.report.write_text(''.join(json.dumps(row) + '\n' for row in self.rows))

    def check(self):
        return AUDIT.audit(self.report, self.root, self.manifest, self.engine)[0]

    def test_complete_inventory_is_not_full_streaming_acceptance(self):
        result = self.check()
        self.assertTrue(result['InventoryVerified'])
        self.assertEqual({'S3': {'Pass': 1}}, result['StageOutcomes'])
        self.assertIn('separate evidence', result['Scope'])

    def test_rejects_truncated_duplicate_and_embedded_terminal_records(self):
        original = copy.deepcopy(self.rows)
        for rows in (original[:-1], original + [original[-1]],
                     original[:2] + [original[1]] + original[2:]):
            with self.subTest(rows=rows):
                self.rows = rows
                self.write()
                with self.assertRaises(ValueError):
                    self.check()

    def test_rejects_missing_or_extra_cases_even_when_totals_match(self):
        self.rows[1]['Case'] = 'si-copy/replacement'
        self.write()
        with self.assertRaisesRegex(ValueError, 'missing or additional'):
            self.check()

    def test_rejects_non_strict_filtered_and_missing_configuration(self):
        original = copy.deepcopy(self.rows)
        for field, value in (('Strict', False), ('SetPrefixes', 'si-copy'), ('Api', None),
                             ('Profile', '3.0'), ('Configuration', None), ('FormatVersion', True)):
            with self.subTest(field=field):
                self.rows = copy.deepcopy(original)
                self.rows[0][field] = value
                self.write()
                with self.assertRaises(ValueError):
                    self.check()

    def test_rejects_inconsistent_totals_and_wrong_error_codes(self):
        original = copy.deepcopy(self.rows)
        for field, value in (('Selected', 2), ('Passed', True), ('NotRunReasons', {'missing': 1}),
                             ('UnmatchedExpectedErrorCodes', 1)):
            with self.subTest(field=field):
                self.rows = copy.deepcopy(original)
                self.rows[-1][field] = value
                self.write()
                with self.assertRaises(ValueError):
                    self.check()

    def test_rejects_fixture_catalog_and_engine_drift(self):
        for path in (self.root / 'catalog.xml', self.root / 'set.xml', self.engine):
            original = path.read_bytes()
            with self.subTest(path=path):
                path.write_bytes(b'changed')
                with self.assertRaises(ValueError):
                    self.check()
                path.write_bytes(original)

    def test_rejects_case_definition_drift_and_new_unassigned_sets(self):
        self.rows[1]['DefinitionSha256'] = 'b' * 64
        self.write()
        with self.assertRaisesRegex(ValueError, 'fixture changed'):
            self.check()
        self.rows[1].update(Case='new-set/example', Set='new-set')
        self.write()
        with self.assertRaisesRegex(ValueError, 'Unassigned'):
            self.check()

    def test_rejects_lost_passing_cases_but_allows_real_improvements(self):
        self.rows[1]['Outcome'] = 'Fail'
        self.rows[-1].update(Passed=0, Failed=1)
        self.write()
        with self.assertRaisesRegex(ValueError, 'regressed'):
            self.check()
        frozen = json.loads(self.manifest.read_text())
        frozen['Cases']['si-copy/example']['Outcome'] = 'Fail'
        self.manifest.write_text(json.dumps(frozen))
        self.rows[1]['Outcome'] = 'Pass'
        self.rows[-1].update(Passed=1, Failed=0)
        self.write()
        self.assertTrue(self.check()['InventoryVerified'])

    def test_rejects_fixture_path_escape(self):
        self.rows[1]['SetFile'] = '../outside.xml'
        self.write()
        with self.assertRaisesRegex(ValueError, 'outside suite'):
            self.check()

    def test_failing_cases_cannot_be_removed_from_execution(self):
        frozen = json.loads(self.manifest.read_text())
        frozen['Cases']['si-copy/example']['Outcome'] = 'Fail'
        self.manifest.write_text(json.dumps(frozen))
        self.rows[1].update(Outcome='NotRun', Detail='new unsupported path')
        self.rows[-1].update(Passed=0, NotRun=1, NotRunReasons={'new unsupported path': 1})
        self.write()
        with self.assertRaisesRegex(ValueError, 'became unsupported'):
            self.check()

    def test_diagnostic_notes_cannot_hide_a_wrong_code_in_a_passing_case(self):
        self.rows[1]['Detail'] = 'error code: expected XTDE0000, got XPTY0004'
        self.write()
        with self.assertRaisesRegex(ValueError, 'marked as passing'):
            self.check()

    def test_suite_pin_covers_referenced_stylesheets_and_untracked_files(self):
        def git(*args):
            return subprocess.check_output(['git', '-C', str(self.root), *args], stderr=subprocess.DEVNULL, text=True).strip()
        git('init')
        git('add', '.')
        git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'fixture')
        revision = git('rev-parse', 'HEAD')
        AUDIT.verify_suite(self.root, revision)
        with self.assertRaisesRegex(ValueError, 'revision'):
            AUDIT.verify_suite(self.root, '0' * 40)
        (self.root / 'untracked.xsl').write_text('<stylesheet/>')
        with self.assertRaisesRegex(ValueError, 'untracked'):
            AUDIT.verify_suite(self.root, revision)
        (self.root / 'untracked.xsl').unlink()
        (self.root / 'set.xml').write_text('modified')
        with self.assertRaisesRegex(ValueError, 'modified'):
            AUDIT.verify_suite(self.root, revision)

    def forwarding(self):
        self.rows[0]['ForwardingCaptured'] = True
        self.rows[1]['Forwarding'] = dict(FormatVersion=1, Started=1, Executions=[dict(
            Id=1, Parent=None, StreamedInitialMode=True, InputKind='reader', ForwardInput=True,
            BodyCompleted=True, Sources=[dict(Kind='Initial', RequestedForward=True, Status='Opened',
                ActualForward=True, NavigatorType='Xsl3.Streaming.StreamedNavigator')])])

    def forwarding_check(self):
        self.write()
        return AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_forwarding=True)[0]

    def test_forwarding_capture_is_required_and_counts_actual_sources(self):
        with self.assertRaisesRegex(ValueError, 'capture'):
            self.forwarding_check()
        self.forwarding()
        execution = self.rows[1]['Forwarding']['Executions'][0]
        execution['Sources'].append(dict(execution['Sources'][0], Kind='SourceDocument'))
        result = self.forwarding_check()['Forwarding']
        self.assertEqual(1, result['EligibleInitialExecutions'])
        self.assertEqual(1, result['InitialForwardSources'])
        self.assertEqual(1, result['SourceDocumentForwardSources'])
        self.assertIn('separate', result['Scope'])

    def test_forwarding_rejects_grounding_or_inconsistent_representation(self):
        self.forwarding()
        source = self.rows[1]['Forwarding']['Executions'][0]['Sources'][0]
        for actual, name in [(False, 'Xsl3.Tree.CompactNavigator'),
                             (True, 'Xsl3.Tree.CompactNavigator'),
                             (False, 'Xsl3.Streaming.StreamedNavigator')]:
            with self.subTest(actual=actual, name=name):
                source.update(ActualForward=actual, NavigatorType=name)
                with self.assertRaises(ValueError):
                    self.forwarding_check()

    def test_successful_eligible_input_must_open_but_failed_early_input_stays_visible(self):
        self.forwarding()
        execution = self.rows[1]['Forwarding']['Executions'][0]
        execution['Sources'] = []
        with self.assertRaisesRegex(ValueError, 'initial source'):
            self.forwarding_check()
        execution['BodyCompleted'] = False
        self.write()
        result = AUDIT.audit(self.report, self.root, self.manifest, self.engine)[0]['Forwarding']
        self.assertEqual([dict(Case='si-copy/example', Execution=1)], result['FailedBeforeInitialSourceObservation'])
        self.assertEqual(0, result['InitialForwardSources'])

    def test_forwarding_rejects_lost_duplicate_or_malformed_executions(self):
        self.forwarding()
        original = copy.deepcopy(self.rows)
        changes = [lambda r: r[1]['Forwarding'].update(Started=2),
                   lambda r: r[1]['Forwarding'].update(FormatVersion=True),
                   lambda r: r[1].pop('Forwarding'),
                   lambda r: r[1]['Forwarding'].update(Started=True),
                   lambda r: r[1]['Forwarding']['Executions'][0].update(Id=True),
                   lambda r: r[1]['Forwarding']['Executions'][0].update(Parent=1),
                   lambda r: r[1]['Forwarding']['Executions'][0].update(ForwardInput=False),
                   lambda r: r[1]['Forwarding']['Executions'][0].update(BodyCompleted='yes'),
                   lambda r: r[1]['Forwarding']['Executions'].append(copy.deepcopy(r[1]['Forwarding']['Executions'][0]))]
        for change in changes:
            self.rows = copy.deepcopy(original)
            change(self.rows)
            with self.subTest(rows=self.rows), self.assertRaises(ValueError):
                self.forwarding_check()

    def test_forwarding_data_cannot_be_silently_ignored(self):
        self.forwarding()
        self.rows[0]['ForwardingCaptured'] = False
        self.write()
        with self.assertRaisesRegex(ValueError, 'capture'):
            self.check()
        self.rows[0]['ForwardingCaptured'] = 'true'
        self.write()
        with self.assertRaisesRegex(ValueError, 'capture'):
            self.check()

    def test_source_opening_failures_do_not_claim_a_representation(self):
        self.forwarding()
        source = dict(Kind='SourceDocument', RequestedForward=True, Status='Failed', ActualForward=None, NavigatorType=None)
        self.rows[1]['Forwarding']['Executions'][0]['Sources'].append(source)
        self.assertEqual(1, self.forwarding_check()['Forwarding']['FailedSourceObservations'])
        for actual, name in [(False, 'Xsl3.Tree.CompactNavigator'), (True, 'Xsl3.Streaming.StreamedNavigator')]:
            with self.subTest(actual=actual), self.assertRaisesRegex(ValueError, 'cannot claim'):
                source.update(ActualForward=actual, NavigatorType=name)
                self.forwarding_check()


if __name__ == '__main__':
    unittest.main()
