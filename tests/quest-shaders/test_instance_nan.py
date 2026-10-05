"""Actual native field identity and unchanged address/NaN equations."""
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-shaders'))
import instance_nan


class NativeNaNLoads(unittest.TestCase):
    def interface(self, **changes):
        return {'buffers': [{'name': 'UnityInstancing_Original', 'structures': [{'name': 'OriginalArray', 'fields': [
            dict(name='_Value', type=0, rows=1, columns=1, matrix=False, arraySize=0, byteOffset=20, **changes)]}]}]}

    def test_exact_native_read_is_captured_without_changing_index_or_nan_rules(self):
        access = 'OriginalArray[((uint(asint(r0.y))+5u)/18)]._Value'
        source = '    float result = isnan('+access+') ? 0.0f : max('+access+',0.0f);\n'
        restored, proof = instance_nan.restore(source, self.interface())
        self.assertEqual(restored.count(access), 1)
        self.assertIn('float QuestNativeNaNInstanceRead_0 = '+access+';', restored)
        self.assertIn('isnan(QuestNativeNaNInstanceRead_0) ? 0.0f : max(QuestNativeNaNInstanceRead_0,0.0f)', restored)
        self.assertEqual(proof[0]['originalIndexExpression'], '((uint(asint(r0.y))+5u)/18)')
        self.assertFalse(proof[0]['originalAddressMathChanged']); self.assertFalse(proof[0]['originalNaNMathChanged'])

    def test_ordinary_native_reads_and_unknown_identities_remain_unmodified(self):
        for source in ('float x = OriginalArray[i]._Value;\n', 'float x = isnan(OtherArray[i]._Value)?0:1;\n'):
            restored, proof = instance_nan.restore(source, self.interface())
            self.assertEqual(restored, source); self.assertEqual(proof, [])

    def test_unaudited_layout_and_loop_evaluation_rejected(self):
        source = 'float result = isnan(OriginalArray[i]._Value)?0:1;\n'
        interface = self.interface(); interface['buffers'][0]['structures'][0]['fields'][0]['type'] = 1
        with self.assertRaises(instance_nan.InstanceReadError): instance_nan.restore(source, interface)
        with self.assertRaises(instance_nan.InstanceReadError):
            instance_nan.restore('while(isnan(OriginalArray[i]._Value)) {\n', self.interface())


if __name__ == '__main__': unittest.main()
