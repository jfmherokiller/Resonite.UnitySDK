"""Contract tests for the headless converter-output runner."""
import copy
import importlib.util
from pathlib import Path
import unittest

PATH = Path(__file__).with_name('harness.py')
spec = importlib.util.spec_from_file_location('harness', PATH) if PATH.exists() else None
harness = importlib.util.module_from_spec(spec) if spec else None
if harness:
    spec.loader.exec_module(harness)


def fixture():
    return {'schemaVersion': 1, 'rootId': 'fixture_root', 'rootName': 'ConverterHarness_test',
            'batch': {'$type': 'dataModelOperationBatch', 'operations': [
                {'$type': 'addSlot', 'data': {'id': 'fixture_root', 'name': {'value': 'ConverterHarness_test'},
                                           'parent': {'$type': 'reference', 'targetId': 'Root'}}},
                {'$type': 'addComponent', 'containerSlotId': 'fixture_root', 'data': {'id': 'sink',
                    'componentType': 'TestOnly', 'members': {'Value': {'id': 'value', '$type': 'float', 'value': 0}}}}]},
            'cases': [{'name': 'output', 'updates': [], 'checks': [
                {'kind': 'component', 'id': 'sink', 'member': 'Value', 'expected': 0, 'absoluteTolerance': 0.0001}]}]}


class HarnessTests(unittest.TestCase):
    def test_rejects_reference_to_filtered_out_member(self):
        self.assertIsNotNone(harness, 'Converter-output harness is not implemented')
        bundle = fixture()
        bundle['batch']['operations'][1]['data']['members']['Target'] = {
            '$type': 'reference', 'targetId': 'missing_anchor'}
        with self.assertRaisesRegex(ValueError, 'Unresolved reference.*missing_anchor'):
            harness.validate_bundle(bundle)

    def test_accepts_closed_bundle_and_selftest(self):
        self.assertIn('value', harness.validate_bundle(fixture()))
        self.assertIn('self_sink_Value', harness.validate_bundle(harness.selftest_bundle()))

    def test_rejects_case_targeting_unknown_id(self):
        bundle = fixture()
        bundle['cases'][0]['checks'][0]['id'] = 'nope'
        with self.assertRaisesRegex(ValueError, 'unknown ID'):
            harness.validate_bundle(bundle)

    def test_rejects_duplicate_ids(self):
        bundle = fixture()
        bundle['batch']['operations'][1]['data']['members']['Value']['id'] = 'sink'
        with self.assertRaisesRegex(ValueError, 'Duplicate ID'):
            harness.validate_bundle(bundle)

    def test_namespacing_rewrites_definitions_references_and_cases(self):
        bundle = harness.selftest_bundle()
        original = copy.deepcopy(bundle)
        renamed, mapping = harness.namespace_bundle(bundle, 'P_')
        self.assertEqual(bundle, original)
        self.assertEqual(renamed['rootId'], 'P_self_root')
        copy_op = renamed['batch']['operations'][3]
        self.assertEqual(copy_op['containerSlotId'], 'P_self_root')
        self.assertEqual(copy_op['data']['members']['Source']['targetId'], 'P_self_source_Value')
        self.assertEqual(renamed['batch']['operations'][0]['data']['parent']['targetId'], 'Root')
        self.assertEqual(renamed['cases'][1]['updates'][0]['id'], 'P_self_source')
        self.assertEqual(renamed['cases'][1]['checks'][0]['id'], 'P_self_sink')
        self.assertEqual(len(mapping), len(harness.defined_ids(original['batch']['operations'])))
        harness.validate_bundle(renamed)
        with self.assertRaises(ValueError):
            harness.namespace_bundle(bundle, 'Reso_')

    def test_compare_scalars_vectors_and_references(self):
        check = {'expected': 0.75, 'absoluteTolerance': 0.01}
        self.assertIsNone(harness.compare({'$type': 'float', 'value': 0.745}, check))
        self.assertIn('diff', harness.compare({'$type': 'float', 'value': 0.7}, check))
        self.assertIn('finite', harness.compare({'value': float('nan')}, check))
        self.assertIn('finite', harness.compare({'value': True}, check))
        vector = {'expected': {'x': 0.75, 'y': 0.5}, 'absoluteTolerance': 1e-4}
        self.assertIsNone(harness.compare({'value': {'x': 0.75, 'y': 0.5}}, vector))
        self.assertIn('value.y', harness.compare({'value': {'x': 0.75, 'y': 0.4}}, vector))
        self.assertIsNone(harness.compare({'value': True}, {'expected': True}))
        self.assertIsNone(harness.compare({'targetId': 'a'}, {'expectedReference': 'a'}))
        self.assertIsNotNone(harness.compare({'targetId': None}, {'expectedReference': 'a'}))

    def test_member_paths(self):
        component = {'members': {'Points': {'$type': 'list', 'elements': [
            {'$type': 'syncObject', 'members': {'Value': {'$type': 'float', 'value': 2}}}]}}}
        self.assertEqual(harness.member_value(component, 'Points.0.Value')['value'], 2)
        slot = {'position': {'$type': 'float3', 'value': {'x': 1, 'y': 2, 'z': 3}}}
        self.assertEqual(harness.member_value(slot, 'position.y', 'slot'), 2)
        with self.assertRaises(KeyError):
            harness.member_value(component, 'Missing')


if __name__ == '__main__':
    unittest.main()
