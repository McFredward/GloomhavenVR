"""Parse original Unity null managed-reference registries without byte rewrites."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-recovery'))
from recover import serialized_pointer_tokens


GUID='0123456789abcdef0123456789abcdef'


def document():
    return ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &19218\nMonoBehaviour:\n'
            '  m_Name: "Überblick"\n  references:\n    version: 2\n    RefIds:\n'
            '    - rid: -2\n      type: {class:, ns:, asm:}\n      data: {}\n'
            '  before: {fileID: 2800000, guid: '+GUID+', type: 2}\n'
            '  fake: "{fileID: 2800000, guid: '+GUID+', type: 2}"\n'
            '--- !u!21 &2100000\nMaterial:\n  next: {fileID: 8400000, guid: '+GUID+', type: 2}\n')


class OriginalManagedReferenceParserTests(unittest.TestCase):
    def test_original_null_type_flow_map_preserves_actual_guid_token_positions(self):
        text=document();original=text.encode();tokens=list(serialized_pointer_tokens(text))
        self.assertEqual(len(tokens),2)
        self.assertEqual([text[left:right] for _,left,right in tokens],[GUID,GUID])
        self.assertEqual(tokens[0][1],text.index(GUID))
        self.assertEqual(tokens[1][1],text.rindex(GUID))
        self.assertEqual(text.encode(),original)

    def test_unrelated_bad_yaml_is_not_accepted_due_to_an_empty_type_elsewhere(self):
        import yaml
        text=document().replace('  m_Name: "Überblick"','  wrong: {foo:, bar:, baz:}')
        with self.assertRaises(yaml.YAMLError):list(serialized_pointer_tokens(text))

    def test_nonempty_managed_type_uses_normal_pointer_parser(self):
        text=document().replace('{class:, ns:, asm:}','{class: Holder, ns: Original, asm: Game}')
        self.assertEqual(len(list(serialized_pointer_tokens(text))),2)


if __name__=='__main__':unittest.main()
