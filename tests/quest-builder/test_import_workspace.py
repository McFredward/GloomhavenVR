"""Import settings and unchanged-original workspace invalidation contracts."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-builder'))
import import_workspace
import storage


SETTINGS=(b'%YAML 1.1\n--- !u!129 &1\nPlayerSettings:\n  serializedVersion: 26\n'
          b'  m_StereoRenderingPath: 0\n  m_ActiveColorSpace: 0\n'
          b'  defaultCursor: {fileID: 2800000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n'
          b'  m_BuildTargetGraphicsAPIs: []\n  m_BuildTargetVRSettings: []\n  keepOriginal: value\n')


def inputs(): return {'inputKey':'1'*64,'game':{'key':'a'*64,'unityVersion':'2021.3.5f1'},'mod':{'key':'b'*64,'modBuild':623},'profile':{'provider':'steam','displayName':'Original owner','id':'fixture','dlcOwnership':[]}}


class ImportSettingsTests(unittest.TestCase):
    def test_first_android_import_uses_final_color_and_vulkan_without_cursor_stereo_changes(self):
        changed=import_workspace.patch_settings(SETTINGS,'game')
        expected=SETTINGS.replace(b'm_ActiveColorSpace: 0',b'm_ActiveColorSpace: 1').replace(b'  m_BuildTargetGraphicsAPIs: []\n',b'  m_BuildTargetGraphicsAPIs:\n  - m_BuildTarget: AndroidPlayer\n    m_APIs: 15000000\n    m_Automatic: 0\n')
        self.assertEqual(changed,expected);self.assertEqual(import_workspace.patch_settings(changed,'game'),changed)
    def test_existing_other_platform_apis_and_crlf_remain_byte_exact(self):
        raw=SETTINGS.replace(b'  m_BuildTargetGraphicsAPIs: []\n',b'  m_BuildTargetGraphicsAPIs:\n  - m_BuildTarget: Standalone\n    m_APIs: 02000000\n    m_Automatic: 1\n  - m_BuildTarget: AndroidPlayer\n    m_APIs: 0b000000\n    m_Automatic: 1\n').replace(b'\n',b'\r\n')
        expected=raw.replace(b'm_ActiveColorSpace: 0',b'm_ActiveColorSpace: 1').replace(b'm_APIs: 0b000000',b'm_APIs: 15000000').replace(b'    m_APIs: 15000000\r\n    m_Automatic: 1',b'    m_APIs: 15000000\r\n    m_Automatic: 0')
        self.assertEqual(import_workspace.patch_settings(raw,'game'),expected)
        startup=import_workspace.patch_settings(SETTINGS,'startup');self.assertIn(b'm_APIs: 0b000000',startup);self.assertNotIn(b'15000000',startup)
    def test_unknown_missing_and_duplicate_fields_fail_before_editor(self):
        for raw in (SETTINGS.replace(b'm_ActiveColorSpace: 0',b'futureColorSpace: 0'),SETTINGS.replace(b'm_ActiveColorSpace: 0',b'm_ActiveColorSpace: 7'),SETTINGS+ b'  m_ActiveColorSpace: 0\n',SETTINGS.replace(b'm_BuildTargetGraphicsAPIs: []',b'futureGraphics: []')):
            with self.subTest(raw=raw),self.assertRaises(storage.BuildError):import_workspace.patch_settings(raw,'game')
    def test_receipt_binds_staged_bytes_without_claiming_a_measured_import(self):
        with tempfile.TemporaryDirectory() as temp:
            project=Path(temp);settings=project/'ProjectSettings/ProjectSettings.asset';settings.parent.mkdir();settings.write_bytes(SETTINGS)
            receipt=import_workspace.stage(project,'game');proof=json.loads(receipt.read_text())
            self.assertEqual(proof['sha256'],storage.digest(settings));self.assertFalse(proof['unityImportTimingVerified']);self.assertTrue(proof['changed'])
            self.assertFalse(json.loads(import_workspace.stage(project,'game').read_text())['changed'])


class WorkspaceIdentityTests(unittest.TestCase):
    def test_mod_profile_and_entitlement_changes_reuse_original_import_identity(self):
        before=inputs();after=copy.deepcopy(before);after['inputKey']='2'*64;after['mod']={'key':'c'*64,'modBuild':624};after['profile']={'provider':'gog','displayName':'New local owner','id':'other','dlcOwnership':['jotl','solo']}
        self.assertEqual(import_workspace.workspace_key(before,'game'),import_workspace.workspace_key(after,'game'))
        after['game']['key']='d'*64;self.assertNotEqual(import_workspace.workspace_key(before,'game'),import_workspace.workspace_key(after,'game'))
        self.assertNotEqual(import_workspace.workspace_key(before,'game'),import_workspace.workspace_key(before,'startup'))
        after['game']['unityVersion']='different';
        with self.assertRaises(storage.BuildError):import_workspace.workspace_key(after,'game')
    def test_new_generation_updates_assets_and_removes_old_mod_code_but_retains_library(self):
        with tempfile.TemporaryDirectory() as temp:
            output=Path(temp);before=inputs();after=copy.deepcopy(before);after['inputKey']='2'*64
            project=output/'projects'/import_workspace.workspace_key(before,'game')
            storage.project_generation_paths(output,project,before['inputKey']);(project/'Library').mkdir(parents=True);(project/'Library/original-import').write_bytes(b'verified original native import')
            (project/'obsolete-mod.cs').write_bytes(b'old source removed in next revision');(project/'native.asset').write_bytes(b'old asset recipe')
            with storage.regenerate_project(output,project,after['inputKey']):
                project.mkdir();(project/'new-mod.cs').write_bytes(b'new selected source');(project/'native.asset').write_bytes(b'new actual asset recipe')
            self.assertEqual((project/'Library/original-import').read_bytes(),b'verified original native import');self.assertEqual((project/'native.asset').read_bytes(),b'new actual asset recipe');self.assertFalse((project/'obsolete-mod.cs').exists())
    def test_changed_profile_can_recover_previous_interrupted_content_before_regeneration(self):
        with tempfile.TemporaryDirectory() as temp:
            output=Path(temp);before=inputs();project=output/'projects'/import_workspace.workspace_key(before,'game');storage.project_generation_paths(output,project,before['inputKey'])
            archive=project/storage.CONTENT_PATHS[0];archive.parent.mkdir(parents=True);archive.write_bytes(b'previous owned payload')
            manifest=project/storage.CONTENT_PATHS[1];storage.write_json(manifest,{'inputKey':before['inputKey'],'archive':archive.name,'archiveSha256':storage.digest(archive)})
            with self.assertRaises(KeyboardInterrupt):
                with storage.project_content_transaction(output,project,before['inputKey']):archive.unlink();raise KeyboardInterrupt()
            storage.recover_project_content(output,project,'2'*64)
            self.assertEqual(archive.read_bytes(),b'previous owned payload');self.assertFalse(storage.project_content_valid(output,project,'2'*64))

if __name__=='__main__':unittest.main()
