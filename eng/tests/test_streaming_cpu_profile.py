import copy
import hashlib
import importlib.util
from pathlib import Path
import unittest


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parents[1] / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


profile = module('cpu_profile', 'profile-streaming-cpu.py')
gate = module('cpu_gate', 'streaming-gate.py')


def pair():
    outputs = {name: {'Bytes': len(data), 'Sha256': hashlib.sha256(data).hexdigest()}
               for name, data in [('principal', b''), ('urn:stream-probe:first', b'a'),
                                  ('urn:stream-probe:second', b'b')]}
    result = dict(Workload='fork', Status='ok', ValidOutput=True, RequestedInputBytes=1000,
                  InputBytes=1000, LimitBytes=0, PeakWorkingSetBytes=1, LiveManagedBytes=1,
                  Records=1, InputBytesAtFirstOutput=1, AvailableManagedBytes=4096,
                  ElapsedMs=2.0, FirstOutputMs=1.0, PeakMemorySource='fixture',
                  DynamicCodeSupported=True, HasTrustedPlatformAssemblies=True, Outputs=outputs)
    return {mode: dict(copy.deepcopy(result), Mode=mode) for mode in ('tree', 'stream')}


class CpuProfileContracts(unittest.TestCase):
    def test_heap_aliases_are_cleared_without_mutating_the_parent(self):
        before = {'DOTNET_GCHeapHardLimitPercent': '1', 'COMPlus_GCHeapHardLimitSOH': '2',
                  'DOTNET_GCHeapHardLimit': '3', 'APP_SETTING': 'retained'}
        actual = profile.environment(before, 4096)
        self.assertEqual(actual['DOTNET_GCHeapHardLimit'], '1000')
        self.assertNotIn('COMPlus_GCHeapHardLimitSOH', actual)
        self.assertNotIn('DOTNET_GCHeapHardLimitPercent', actual)
        self.assertEqual(actual['APP_SETTING'], 'retained')
        self.assertEqual(before['DOTNET_GCHeapHardLimit'], '3')

    def test_matching_complete_children_are_accepted(self):
        profile.validate_pair(pair(), gate, 1000, 4096)

    def test_missing_mode_is_rejected(self):
        results = pair()
        del results['stream']
        with self.assertRaises(ValueError):
            profile.validate_pair(results, gate, 1000, 4096)

    def test_different_fingerprint_is_rejected(self):
        results = pair()
        results['stream']['Outputs']['principal']['Sha256'] = hashlib.sha256(b'wrong').hexdigest()
        with self.assertRaises(ValueError):
            profile.validate_pair(results, gate, 1000, 4096)

    def test_different_record_count_is_rejected(self):
        results = pair()
        results['stream']['Records'] += 1
        with self.assertRaises(ValueError):
            profile.validate_pair(results, gate, 1000, 4096)

    def test_wrong_actual_guard_is_rejected(self):
        results = pair()
        results['stream']['AvailableManagedBytes'] *= 2
        with self.assertRaises(ValueError):
            profile.validate_pair(results, gate, 1000, 4096)

    def test_nonfinite_or_invalid_output_is_rejected(self):
        for field, value in [('ElapsedMs', float('nan')), ('ValidOutput', False),
                             ('DynamicCodeSupported', False)]:
            results = pair()
            results['stream'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                profile.validate_pair(results, gate, 1000, 4096)


if __name__ == '__main__':
    unittest.main()
