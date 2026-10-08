"""Native front-face signature and generated stereo declaration ordering."""
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-shaders'))
import produce


class FragmentStereoOrder(unittest.TestCase):
    source = '''struct SPIRV_Cross_Input
{
    float4 originalPosition : SV_POSITION0;
    float2 originalUv : TEXCOORD0;
    nointerpolation uint originalInstance : SV_InstanceID0;
    bool gl_FrontFacing : SV_IsFrontFace;
    UNITY_VERTEX_OUTPUT_STEREO
};
float4 QuestOriginalFragment(SPIRV_Cross_Input stage_input)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(stage_input);
    return gl_FrontFacing ? originalFrontColor : originalBackColor;
}
'''
    signature = [{'systemValue': 1, 'semantic': 'SV_POSITION', 'semanticIndex': 0},
                 {'systemValue': 0, 'semantic': 'SV_InstanceID', 'semanticIndex': 0},
                 {'systemValue': 9, 'semantic': 'SV_IsFrontFace', 'semanticIndex': 0}]

    def test_only_generated_declaration_moves_before_native_front_face(self):
        actual, proof = produce.fragment_stereo_input_order(self.source, 'fragment', self.signature)
        self.assertEqual(actual.replace('    UNITY_VERTEX_OUTPUT_STEREO\n', ''), self.source.replace('    UNITY_VERTEX_OUTPUT_STEREO\n', ''))
        self.assertLess(actual.index('originalInstance'), actual.index('UNITY_VERTEX_OUTPUT_STEREO'))
        self.assertLess(actual.index('UNITY_VERTEX_OUTPUT_STEREO'), actual.index('gl_FrontFacing'))
        self.assertLess(actual.index('originalPosition'), actual.index('originalUv'))
        self.assertLess(actual.index('originalUv'), actual.index('originalInstance'))
        self.assertLess(actual.index('originalInstance'), actual.index('gl_FrontFacing'))
        self.assertTrue(proof[0]['originalDeclarationOrderPreserved'])
        self.assertFalse(proof[0]['originalProgramMathChanged'])
        self.assertEqual(produce.fragment_stereo_input_order(actual, 'fragment', self.signature)[0], actual)

    def test_native_system_identity_and_stage_control_the_adapter(self):
        for stage, signatures in [('vertex', self.signature), ('fragment', self.signature[:2]),
                                  ('fragment', [{**self.signature[-1], 'systemValue': 0}])]:
            with self.subTest(stage=stage, signatures=signatures):
                self.assertEqual(produce.fragment_stereo_input_order(self.source, stage, signatures), (self.source, []))

    def test_unproven_or_ambiguous_source_and_signature_fail_closed(self):
        for source in [self.source.replace('SV_IsFrontFace', 'TEXCOORD7'),
                       self.source.replace('UNITY_VERTEX_OUTPUT_STEREO\n', ''),
                       self.source.replace('UNITY_VERTEX_OUTPUT_STEREO', 'UNITY_VERTEX_OUTPUT_STEREO\n    UNITY_VERTEX_OUTPUT_STEREO'),
                       self.source + self.source,
                       self.source.replace('bool gl_FrontFacing', 'uint gl_FrontFacing')]:
            with self.subTest(source=source), self.assertRaises(produce.ValidationError):
                produce.fragment_stereo_input_order(source, 'fragment', self.signature)
        for signatures in [self.signature + self.signature[-1:],
                           [{**self.signature[-1], 'semantic': 'Invented'}]]:
            with self.subTest(signatures=signatures), self.assertRaises(produce.ValidationError):
                produce.fragment_stereo_input_order(self.source, 'fragment', signatures)


if __name__ == '__main__':
    unittest.main()
