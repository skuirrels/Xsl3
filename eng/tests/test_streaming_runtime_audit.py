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

    def verifier_fixture(self):
        self.rows[0].update(Configuration='Debug', CompilationsCaptured=True)
        self.rows[1]['Compilation'] = dict(FormatVersion=1, Compilations=[dict(
            Stage='Compilation', HasStreamedInputs=True, OptimizationCompleted=True,
            VerifierStarted=1, VerifierCompleted=1)])
        return self.rows[1]['Compilation']['Compilations'][0]

    def check_verifier(self):
        self.write()
        return AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_verifier=True)[0]['Verifier']

    def test_verifier_counts_real_eligible_compilations_and_allows_pre_qil_errors(self):
        self.verifier_fixture()
        self.rows[1]['Compilation']['Compilations'].append(dict(Stage='Assertion', HasStreamedInputs=False,
            OptimizationCompleted=True, VerifierStarted=0, VerifierCompleted=0))
        result = self.check_verifier()
        self.assertEqual((2, 1, 1), (result['Compilations'], result['Eligible'], result['Verified']))
        cases = {'si-copy/good': self.rows[1], 'si-copy/static-error': dict(
            Compilation=dict(FormatVersion=1, Compilations=[]))}
        self.assertEqual(2, AUDIT.audit_verifier(self.rows[0], cases, True)['CapturedCases'])

    def test_verifier_rejects_skipped_failed_duplicate_and_incomplete_checks(self):
        entry = self.verifier_fixture()
        original = copy.deepcopy(entry)
        for changes in (dict(VerifierStarted=0, VerifierCompleted=0), dict(VerifierCompleted=0),
                        dict(VerifierStarted=2, VerifierCompleted=2), dict(OptimizationCompleted=False),
                        dict(HasStreamedInputs=False), dict(VerifierCompleted=True), dict(VerifierStarted=-1)):
            with self.subTest(changes=changes):
                entry.clear()
                entry.update(original | changes)
                with self.assertRaises(ValueError):
                    self.check_verifier()

    def test_verifier_rejects_missing_capture_release_and_empty_eligibility(self):
        self.verifier_fixture()
        original = copy.deepcopy(self.rows)
        for kind in ('header', 'case', 'release', 'empty', 'version'):
            with self.subTest(kind=kind):
                self.rows = copy.deepcopy(original)
                if kind == 'header':
                    del self.rows[0]['CompilationsCaptured']
                elif kind == 'case':
                    del self.rows[1]['Compilation']
                elif kind == 'release':
                    self.rows[0]['Configuration'] = 'Release'
                elif kind == 'empty':
                    self.rows[1]['Compilation']['Compilations'] = []
                else:
                    self.rows[1]['Compilation']['FormatVersion'] = True
                with self.assertRaises(ValueError):
                    self.check_verifier()

    def test_forward_execution_cannot_be_credited_to_assertion_compilation(self):
        entry = self.verifier_fixture()
        self.rows[1]['Forwarding'] = dict(Executions=[dict(Stage='Stylesheet', Sources=[dict(ActualForward=True)])])
        entry['Stage'] = 'Assertion'
        with self.assertRaisesRegex(ValueError, 'no compilation evidence'):
            AUDIT.audit_verifier(self.rows[0], {'si-copy/example': self.rows[1]}, True)
        self.rows[1]['Compilation']['Compilations'].append(dict(Stage='Compilation', HasStreamedInputs=False,
            OptimizationCompleted=True, VerifierStarted=0, VerifierCompleted=0))
        with self.assertRaisesRegex(ValueError, 'no eligible compilation'):
            AUDIT.audit_verifier(self.rows[0], {'si-copy/example': self.rows[1]}, True)
        entry['Stage'] = 'Stylesheet'  # Lazy compilation for a named initial mode.
        result = AUDIT.audit_verifier(self.rows[0], {'si-copy/example': self.rows[1]}, True)
        self.assertEqual(1, result['Verified'])

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
        manifest = json.loads(self.manifest.read_text())
        manifest['StylesheetForwardCases'] = ['si-copy/example']
        self.manifest.write_text(json.dumps(manifest))

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

    def test_stylesheet_forwarding_cannot_be_proved_by_compilation_or_assertions(self):
        self.forwarding()
        self.rows[1]['Forwarding']['FormatVersion'] = 2
        entry = self.rows[1]['Forwarding']['Executions'][0]
        for stage in ['Compilation', 'Preparation', 'Assertion']:
            entry['Stage'] = stage
            self.write()
            with self.subTest(stage=stage), self.assertRaisesRegex(ValueError, 'no actual stylesheet-stage'):
                AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_stylesheet_forwarding=True)
        entry['Stage'] = 'Stylesheet'
        self.write()
        result = AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_stylesheet_forwarding=True)[0]['Forwarding']
        self.assertEqual(1, result['StylesheetCasesWithForwardSources'])
        self.assertEqual(1, result['StageObservations']['Stylesheet']['ForwardSources'])
        self.assertEqual(1, result['StagesCapturedCases'])

    def test_stylesheet_gate_rejects_legacy_missing_or_unclassified_stages(self):
        self.forwarding()
        self.write()
        with self.assertRaisesRegex(ValueError, 'stage forwarding evidence'):
            AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_stylesheet_forwarding=True)
        self.rows[1]['Forwarding']['FormatVersion'] = 2
        entry = self.rows[1]['Forwarding']['Executions'][0]
        for stage in [None, 'Unclassified', 'other', [], True]:
            entry['Stage'] = stage
            self.write()
            with self.subTest(stage=stage), self.assertRaises(ValueError):
                AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_stylesheet_forwarding=True)
        entry.pop('Stage')
        self.write()
        with self.assertRaisesRegex(ValueError, 'execution stage'):
            AUDIT.audit(self.report, self.root, self.manifest, self.engine)

    def test_stylesheet_case_floor_rejects_a_regression_even_when_another_case_still_forwards(self):
        self.forwarding()
        self.rows[1]['Forwarding']['FormatVersion'] = 2
        self.rows[1]['Forwarding']['Executions'][0]['Stage'] = 'Stylesheet'
        other = copy.deepcopy(self.rows[1])
        other['Case'] = 'si-copy/other'
        other['Forwarding']['Executions'][0].update(StreamedInitialMode=False, InputKind='navigator', ForwardInput=False, Sources=[])
        self.rows.insert(2, other)
        self.rows[-1].update(Selected=2, Passed=2)
        manifest = json.loads(self.manifest.read_text())
        manifest['Cases']['si-copy/other'] = dict(manifest['Cases']['si-copy/example'])
        manifest['StylesheetForwardCases'].append('si-copy/other')
        self.manifest.write_text(json.dumps(manifest))
        self.write()
        with self.assertRaisesRegex(ValueError, 'si-copy/other'):
            AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_stylesheet_forwarding=True)

    def test_an_engine_recorded_tree_fallback_fails_the_stylesheet_floor(self):
        # S3-27: the execution record the engine writes when a guaranteed-streamable stylesheet falls back to a tree
        # (RuntimeCaseReportTests.AnInjectedTreeFallbackPassesItsOutcomeButLosesItsRecordedForwardSource). The case
        # still passes, and no per-source rule rejects it, since it never requested a forward source. The frozen
        # stylesheet-forwarding floor must.
        self.forwarding()
        self.rows[1]['Forwarding']['FormatVersion'] = 2
        self.rows[1]['Forwarding']['Executions'][0]['Stage'] = 'Stylesheet'
        fallback = copy.deepcopy(self.rows[1])
        fallback['Case'] = 'si-copy/fallback'
        fallback['Forwarding']['Executions'] = [dict(
            Id=1, Parent=None, Stage='Stylesheet', StreamedInitialMode=False, InputKind='reader', ForwardInput=True,
            BodyCompleted=True, Sources=[dict(Kind='Initial', RequestedForward=False, Status='Opened',
                ActualForward=False, NavigatorType='Xsl3.Tree.CompactNavigator')])]
        self.rows.insert(2, fallback)
        self.rows[-1].update(Selected=2, Passed=2)
        manifest = json.loads(self.manifest.read_text())
        manifest['Cases']['si-copy/fallback'] = dict(manifest['Cases']['si-copy/example'])
        manifest['StylesheetForwardCases'].append('si-copy/fallback')
        self.manifest.write_text(json.dumps(manifest))
        self.assertEqual('Pass', fallback['Outcome'])
        self.assertEqual(['si-copy/fallback'], self.forwarding_check()['Forwarding']['CasesWithStylesheetExecutionWithoutForwardSources'])
        with self.assertRaisesRegex(ValueError, 'si-copy/fallback'):
            AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_forwarding=True,
                        require_stylesheet_forwarding=True)

    def test_stylesheet_gate_requires_a_valid_frozen_case_floor(self):
        self.forwarding()
        self.rows[1]['Forwarding']['FormatVersion'] = 2
        self.rows[1]['Forwarding']['Executions'][0]['Stage'] = 'Stylesheet'
        self.write()
        manifest = json.loads(self.manifest.read_text())
        for minimum in [None, [], [True], ['si-copy/missing'], ['si-copy/example', 'si-copy/example']]:
            manifest['StylesheetForwardCases'] = minimum
            self.manifest.write_text(json.dumps(manifest))
            with self.subTest(minimum=minimum), self.assertRaisesRegex(ValueError, 'frozen stylesheet-forwarding'):
                AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_stylesheet_forwarding=True)

    def evaluation(self):
        self.rows[0]['EvaluationCaptured'] = True
        self.rows[1].update(Evaluation=dict(Status='Returned', ExceptionType=None, Diagnostic=None),
                            DeclaredInitialSources=[dict(Streaming=True, Select=None, Validation=None, HasDocument=True)])

    def evaluation_check(self):
        self.write()
        return AUDIT.audit(self.report, self.root, self.manifest, self.engine, require_evaluation=True)[0]

    def test_evaluation_observations_are_complete_and_separate_from_applicability(self):
        with self.assertRaisesRegex(ValueError, 'evaluation capture'):
            self.evaluation_check()
        self.evaluation()
        result = self.evaluation_check()['Evaluation']
        self.assertEqual({'Returned': 1}, result['StatusCounts'])
        self.assertEqual(self.rows[1]['DeclaredInitialSources'], result['Cases']['si-copy/example']['DeclaredInitialSources'])
        self.assertIn('do not assign static applicability', result['Scope'])

    def test_evaluation_cannot_disappear_or_hide_under_an_invalid_header(self):
        self.evaluation()
        original = copy.deepcopy(self.rows)
        for change in [lambda r: r[1].pop('Evaluation'), lambda r: r[1].pop('DeclaredInitialSources'),
                       lambda r: r[0].update(EvaluationCaptured=False), lambda r: r[0].update(EvaluationCaptured=1),
                       lambda r: r[1]['Evaluation'].update(Status='NotObserved')]:
            self.rows = copy.deepcopy(original)
            change(self.rows)
            with self.subTest(rows=self.rows), self.assertRaises(ValueError):
                self.evaluation_check()

    def test_only_an_error_can_claim_a_primary_diagnostic(self):
        self.evaluation()
        entry = self.rows[1]['Evaluation']
        entry.update(Status='Error', ExceptionType='XsltStaticException', Diagnostic=dict(
            Code='Q{http://www.w3.org/2005/xqt-errors}XTSE3430', IsStatic=True,
            ModuleUri='urn:stylesheet', Line=3, Column=7, Reason='actual primary error'))
        self.assertEqual({'Error': 1}, self.evaluation_check()['Evaluation']['StatusCounts'])
        original = copy.deepcopy(self.rows)
        for change in [lambda e: e.update(Status='Returned'), lambda e: e.update(Diagnostic=None),
                       lambda e: e.update(ExceptionType=''), lambda e: e['Diagnostic'].update(Code='XTSE3430'),
                       lambda e: e['Diagnostic'].update(Line=True), lambda e: e['Diagnostic'].update(Column=-1),
                       lambda e: e['Diagnostic'].update(IsStatic='true'), lambda e: e['Diagnostic'].update(ModuleUri=[]),
                       lambda e: e['Diagnostic'].update(Reason=1)]:
            self.rows = copy.deepcopy(original)
            change(self.rows[1]['Evaluation'])
            with self.subTest(rows=self.rows), self.assertRaises(ValueError):
                self.evaluation_check()

    def test_unobserved_failures_and_skips_remain_visible_without_claiming_an_error(self):
        self.evaluation()
        self.rows[1]['Evaluation']['Status'] = 'NotObserved'
        frozen = json.loads(self.manifest.read_text())
        frozen['Cases']['si-copy/example']['Outcome'] = 'NotRun'
        self.manifest.write_text(json.dumps(frozen))
        for outcome in ['Fail', 'NotRun']:
            self.rows[1].update(Outcome=outcome, Detail='actual unavailable environment')
            self.rows[-1].update(Passed=0, Failed=int(outcome == 'Fail'), NotRun=int(outcome == 'NotRun'),
                                 NotRunReasons={} if outcome == 'Fail' else {'actual unavailable environment': 1})
            with self.subTest(outcome=outcome):
                self.assertEqual({'NotObserved': 1}, self.evaluation_check()['Evaluation']['StatusCounts'])

    def test_declared_source_flags_and_optional_text_are_not_coerced(self):
        self.evaluation()
        original = copy.deepcopy(self.rows)
        for field, value in [('Streaming', 1), ('HasDocument', 'yes'), ('Select', []), ('Validation', True)]:
            self.rows = copy.deepcopy(original)
            self.rows[1]['DeclaredInitialSources'][0][field] = value
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, 'initial-source metadata'):
                self.evaluation_check()


if __name__ == '__main__':
    unittest.main()
