"""Parse original Unity null managed-reference registries without byte rewrites."""
from pathlib import Path
import io
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-recovery'))
from recover import audit_asset_references, serialized_pointer_tokens


GUID='0123456789abcdef0123456789abcdef'


def document():
    return ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &19218\nMonoBehaviour:\n'
            '  m_Name: "Überblick"\n  references:\n    version: 2\n    RefIds:\n'
            '    - rid: -2\n      type: {class:, ns:, asm:}\n      data: {}\n'
            '  before: {fileID: 2800000, guid: '+GUID+', type: 2}\n'
            '  fake: "{fileID: 2800000, guid: '+GUID+', type: 2}"\n'
            '--- !u!21 &2100000\nMaterial:\n  next: {fileID: 8400000, guid: '+GUID+', type: 2}\n')


class OriginalManagedReferenceParserTests(unittest.TestCase):
    def assert_pointer_offsets(self, text):
        original = text.encode()
        tokens = list(serialized_pointer_tokens(text))
        self.assertEqual(tokens, [(GUID, text.index(GUID), text.index(GUID) + 32),
                                  (GUID, text.rindex(GUID), text.rindex(GUID) + 32)])
        self.assertEqual(text.encode(), original)

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

    def test_windows_and_mixed_endings_preserve_null_registry_and_pointer_offsets(self):
        source = document().replace('Überblick', 'Überblick 🗺')
        # Unity permits negative object IDs and its stripped-prefab suffix.
        source = source.replace('&19218', '&-19218 stripped\t ')
        variants = [source, source.replace('\n', '\r\n'),
                    ''.join(line + ('\r\n' if index % 2 else '\n')
                            for index, line in enumerate(source.splitlines()))]
        import yaml
        for loader in {yaml.SafeLoader, getattr(yaml, 'CSafeLoader', yaml.SafeLoader)}:
            with patch.object(yaml, 'CSafeLoader', loader, create=True):
                for text in variants:
                    for final_newline in (True, False):
                        with self.subTest(loader=loader.__name__, endings=repr(text[:50]), final_newline=final_newline):
                            self.assert_pointer_offsets(text if final_newline else text.rstrip('\r\n'))

    def test_raw_windows_bytes_audit_resolves_all_documents_with_later_metadata(self):
        # read_text() hides Windows newlines; the production audit reads bytes.
        data = document().replace('\n', '\r\n').encode('utf-8')
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            assets = project / 'Assets'
            assets.mkdir()
            prefab = assets / 'UI Quest Marker.prefab'
            prefab.write_bytes(data)
            (assets / 'later.asset.meta').write_bytes(('fileFormatVersion: 2\r\nguid: ' + GUID + '\r\n').encode())
            with patch('sys.stdout', io.StringIO()):
                result = audit_asset_references(project)
            self.assertEqual(result['referenceCount'], 2)
            self.assertEqual(result['missing'], {})
            self.assertEqual(result['duplicateGuidCount'], 0)
            self.assertEqual(prefab.read_bytes(), data)

    def test_unrelated_bad_windows_yaml_still_fails_with_null_registry_present(self):
        import yaml
        source = document().replace('  m_Name: "Überblick"', '  broken: [one, two')
        text = source.replace('\n', '\r\n')
        for loader in {yaml.SafeLoader, getattr(yaml, 'CSafeLoader', yaml.SafeLoader)}:
            with self.subTest(loader=loader.__name__), patch.object(yaml, 'CSafeLoader', loader, create=True):
                with self.assertRaises(yaml.YAMLError):
                    list(serialized_pointer_tokens(text))


if __name__=='__main__':unittest.main()
