import sys
from pathlib import Path
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-shaders'))
import load_bounds


class NativeLoads(unittest.TestCase):
    def test_signed_coordinates_and_zero_extent_keep_native_single_load(self):
        source = ('Texture2D<float4> _Input;\nvoid main(){\n'
                  ' r0 = float4(r0.x, r0.y, 0.0f.xx.x, 0.0f.xx.y);\n'
                  ' color = _Input.Load(int3(asint(r0.xy), asint(r0.w)));\n}\n')
        result, proof = load_bounds.restore(source)
        self.assertIn('any(position.xy < 0)', result)
        self.assertIn('uint(position.x) >= width', result)
        self.assertIn('return float4(0.0, 0.0, 0.0, 0.0)', result)
        self.assertEqual(result.count('_Input.Load('), 1)
        self.assertIn('QuestNativeLoadZero__Input(int3(asint(r0.xy), asint(r0.w)))', result)
        self.assertEqual(proof[0]['originalLoadCount'], 1)

    def test_unknown_mip_offset_and_dimension_fail(self):
        for texture, position in [('Texture2D', 'int3(xy, mip)'), ('Texture3D', 'int4(xyz, 0)'), ('Texture2D', 'int3(xy, 0), int2(1, 0)')]:
            with self.subTest(texture=texture, position=position):
                with self.assertRaises(load_bounds.LoadBoundsError):
                    load_bounds.restore(texture + '<float4> _Input;\nvoid main(){ color=_Input.Load(' + position + '); }')


if __name__ == '__main__':
    unittest.main()
