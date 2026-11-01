import copy
import unittest
from test_streaming_cpu_profile import module, pair, gate

comparison = module('fork_comparison', 'compare-streaming-fork.py')


def trials():
    return [dict(variant=v, mode=m, exitCode=0, result=copy.deepcopy(r))
            for _ in range(3) for v in ('baseline', 'candidate') for m, r in pair().items()]


class ForkComparisonContracts(unittest.TestCase):
    def test_complete_matching_controls(self):
        result = comparison.validate_trials(trials(), 3, gate, 1000, 4096)
        self.assertEqual(result, {v: dict(tree=2.0, stream=2.0) for v in ('baseline', 'candidate')})

    def test_coverage_failures(self):
        for change in ('missing', 'duplicate'):
            rows = trials()
            if change == 'missing':
                rows.pop()
            else:
                rows[0]['variant'] = 'candidate'
            with self.subTest(change=change), self.assertRaises(ValueError):
                comparison.validate_trials(rows, 3, gate, 1000, 4096)

    def test_invalid_children(self):
        for field, value in [('ElapsedMs', float('inf')), ('AvailableManagedBytes', 8192),
                             ('DynamicCodeSupported', False), ('Records', 2), ('ValidOutput', False)]:
            rows = trials()
            rows[0]['result'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                comparison.validate_trials(rows, 3, gate, 1000, 4096)

    def test_cross_variant_output_mismatch(self):
        rows = trials()
        rows[0]['result']['Outputs']['principal']['Bytes'] += 1
        with self.assertRaises(ValueError):
            comparison.validate_trials(rows, 3, gate, 1000, 4096)

    def test_failed_child_is_rejected(self):
        rows = trials()
        rows[0]['exitCode'] = 1
        with self.assertRaises(ValueError):
            comparison.validate_trials(rows, 3, gate, 1000, 4096)


if __name__ == '__main__':
    unittest.main()
