"""Integer register proofs for native denormal MOV and loop bit carriers."""
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-shaders'))
import integer_bits


class IntegerBits(unittest.TestCase):
    def test_integer_payload_never_crosses_float_storage_at_extraction(self):
        source = ('static float4 r0;\nvoid main(){\n'
                  ' r0.w = asfloat(uint(weight));\n'
                  ' InterlockedAdd(counter, asuint(r0.w));\n'
                  ' color = r0.w;\n}\n')
        result, proof = integer_bits.restore(source)
        self.assertIn('QuestCapturedBits_0 = asuint(uint(weight));', result)
        self.assertIn('QuestOriginalBits_r0.w = QuestCapturedBits_0;', result)
        self.assertIn('InterlockedAdd(counter, QuestOriginalBits_r0.w);', result)
        self.assertIn('color = r0.w;', result)
        self.assertEqual(proof['registerCount'], 1)

    def test_literal_bits_and_raw_moves_preserve_subnormal_offsets(self):
        source = ('static float4 r0;\nstatic float4 r1;\nvoid main(){\n'
                  ' r0.y = 5.605193857299268e-45f;\n r1.x = r0.y;\n'
                  ' offset = asuint(r1.x);\n}\n')
        result, proof = integer_bits.restore(source)
        self.assertIn('QuestCapturedBits_0 = 4u;', result)
        self.assertIn('QuestCapturedBits_1 = QuestOriginalBits_r0.y;', result)
        self.assertIn('offset = QuestOriginalBits_r1.x;', result)
        self.assertEqual(proof['registerCount'], 2)

    def test_loop_and_self_assignment_keep_old_bits_until_original_write(self):
        source = ('static float r0;\nvoid main(){\n r0 = asfloat(0u);\n'
                  ' while(asuint(r0) < 4u){\n r0 = asfloat(asuint(r0) + 1u);\n }\n}\n')
        result, proof = integer_bits.restore(source)
        capture = result.index('QuestCapturedBits_1 = asuint(QuestOriginalBits_r0 + 1u)')
        float_write = result.index('r0 = asfloat(QuestOriginalBits_r0 + 1u)')
        integer_write = result.index('QuestOriginalBits_r0 = QuestCapturedBits_1')
        self.assertLess(capture, float_write)
        self.assertLess(float_write, integer_write)
        self.assertIn('while(QuestOriginalBits_r0 < 4u)', result)
        self.assertEqual(proof['mirroredWrites'], 2)

    def test_integer_literal_extraction_differs_from_float_literal_assignment(self):
        source = 'static float r0;\nvoid main(){\n r0 = 4;\n value = asuint(r0) + asuint(4);\n}\n'
        result, _ = integer_bits.restore(source)
        self.assertIn('QuestCapturedBits_0 = 1082130432u;', result)
        self.assertIn('value = QuestOriginalBits_r0 + 4u;', result)

    def test_float_only_original_program_is_unchanged(self):
        source = 'static float4 r0;\nvoid main(){\n r0 = texture.Sample(sampler, uv);\n color = r0;\n}\n'
        result, proof = integer_bits.restore(source)
        self.assertEqual(result, source)
        self.assertEqual(proof['registerCount'], 0)

    def test_local_and_cbuffer_carriers_preserve_integer_coordinate_bits(self):
        source = ('static float4 cb0[2];\nvoid main(){\n cb0[0] = float4(asfloat(4u), 0.0, 0.0, 0.0);\n'
                  ' float2 raw = asfloat(int2(pixel.xy));\n float4 r0;\n'
                  ' r0 = float4(raw.x, raw.y, 0.0f.xx.x, 0.0f.xx.y);\n'
                  ' value = asuint(r0.x) + asuint(cb0[0].x);\n}\n')
        result, proof = integer_bits.restore(source)
        self.assertIn('static uint4 QuestOriginalBits_cb0[2];', result)
        self.assertIn('QuestCapturedBits_1 = asuint(int2(pixel.xy));', result)
        self.assertIn('uint4(QuestOriginalBits_raw.x, QuestOriginalBits_raw.y, (0u).xx.x, (0u).xx.y)', result)
        self.assertEqual(proof['registerCount'], 3)

    def test_operand_swizzle_does_not_move_across_arithmetic(self):
        source = ('void main(){\n float2 raw = position.xy + offset.zw;\n'
                  ' value = asuint(raw);\n}\n')
        result, _ = integer_bits.restore(source)
        self.assertIn('QuestCapturedBits_0 = uint2(asuint(float2(position.xy + offset.zw)));', result)
        self.assertNotIn('asuint(position.xy + offset)).zw', result)

    def test_builtin_fixed_values_keep_original_float_conversion_width(self):
        source = ('static float4 cb0[1];\nvoid main(){\n'
                  ' cb0[0] = float4(_LightColor0.x, _LightColor0.y, _LightColor0.z, _LightColor0.w);\n'
                  ' index = asuint(cb0[0].x);\n}\n')
        result, proof = integer_bits.restore(source)
        self.assertIn('uint4(asuint(float(_LightColor0.x)), asuint(float(_LightColor0.y)), asuint(float(_LightColor0.z)), asuint(float(_LightColor0.w)))', result)
        self.assertIn('cb0[0] = float4(_LightColor0.x, _LightColor0.y, _LightColor0.z, _LightColor0.w);', result)
        self.assertEqual(proof['registerCount'], 1)


if __name__ == '__main__':
    unittest.main()
