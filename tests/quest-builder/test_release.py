"""Exact historical Builder overlay cleanup without admitting unknown source."""
from pathlib import Path
import hashlib
import json
import os
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import release

# Exact authored B713 bytes, retained independently of local Git/history access.
OLD_MENU_CAMERA = '''using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>
    /// Retain the desktop anchor priority. Quest also accepts native backbuffer
    /// cameras already redirected by FlatScreen, then a native UI camera as the
    /// last reference-only anchor. The owned head remains the sole XR renderer.
    /// </summary>
    private static Camera? ResolveMenuCamera()
    {
        bool quest = QuestStandalonePlatform.Enabled;
        Camera? cam = NativeCameraRenderBudget.Main;
        if (cam != null && (!quest || cam != HeadCamera))
            return cam;

        // The owned-game MainMenu scene disables its MainCamera-tagged object;
        // only UICamera plus a private character-preview camera remain active.
        // The B616 capture records rig=False and that UICamera on our scrub sink.
        // Requiring an untouched backbuffer here prevents recovery after a scene
        // unload or anchor disable: both capture and scrub legitimately own those
        // native targets already. This changes only Quest's anchor eligibility,
        // never native camera activation, gameplay, output, or input routing.
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] all);
        Camera? best = null;
        Camera? ui = null;
        for (int i = 0; i < count; i++)
        {
            Camera candidate = all[i];
            if (candidate == null || !candidate.enabled || candidate == HeadCamera)
                continue;
            if (candidate.targetTexture != null && (!quest || !WorldUI.FlatScreen.OwnsPresentationTarget(candidate)))
                continue;
            if (candidate.CompareTag("UICamera"))
            {
                if (quest && (ui == null || candidate.depth > ui.depth))
                    ui = candidate;
                continue;
            }
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }
        return best != null ? best : ui;
    }
}
'''.encode()
OLD_PATH = 'src/GloomhavenVR/Rig/VRRigDriver.MenuCamera.cs'


class ReleaseOverlayTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='quest release overlay ')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        # Public converter ZIP bytes have their own pin/packaging tests. Here
        # every actual required source is represented, without copying tools.
        public = set(release.PUBLIC_PACKAGES)
        for patch in (mock.patch.object(release, 'PUBLIC_PACKAGES', {}),
                      mock.patch.object(release, 'REQUIRED', release.REQUIRED - public)):
            patch.start(); self.addCleanup(patch.stop)
        names = release.REQUIRED | release.LINUX_REQUIRED | {'src/GloomhavenVR/Net/NetProtocol.cs'}
        for name in names:
            self.put(name, b'public controlled fixture input')
        self.put('tools/quest-movies/recipe.json', b'{"schema":1,"movies":[]}')
        depth_name = '1' * 32 + '.gmd'
        depth = self.put(release.MENU_STORY_ROOT + depth_name, b'controlled numeric relief fixture')
        record = release.record_file(depth, release.MENU_STORY_ROOT + depth_name)
        self.put(release.MENU_STORY_ROOT + 'manifest.json', json.dumps({
            'schema': 1, 'status': 'complete', 'format': 'GMD1', 'images': [{
                'source_id': '1' * 32, 'depth_file': depth_name,
                'depth_bytes': record['size'], 'depth_sha256': record['sha256']}]}).encode())
        names.add(record['path'])
        self.rows = [release.record_file(self.root / name, name) for name in sorted(names)]
        value = {'schema': 1, 'kind': release.RELEASE_KIND, 'sourceCommit': 'b' * 40,
                 'modBuild': 716, 'files': self.rows,
                 'localDependencyRoots': list(release.LOCAL_DEPENDENCIES)}
        self.put(release.MANIFEST, json.dumps(value).encode())

    def put(self, relative, data):
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        return target

    def old(self):
        expected = release.RETIRED_B713_INPUTS[OLD_PATH]
        self.assertEqual((len(OLD_MENU_CAMERA), hashlib.sha256(OLD_MENU_CAMERA).hexdigest()), expected)
        return self.put(OLD_PATH, OLD_MENU_CAMERA)

    def test_fresh_release_has_no_cleanup_and_retains_manifest_inventory(self):
        rows, commit, dirty = release.verified_source_inventory(self.root)
        self.assertEqual(rows[:-1], self.rows)
        self.assertEqual(commit, 'b' * 40)
        self.assertFalse(dirty)

    def test_actual_b713_overlay_removes_only_exact_retired_source_then_is_idempotent(self):
        old = self.old()
        before = {row['path']: (self.root / row['path']).read_bytes() for row in self.rows}
        release.verified_source_inventory(self.root)
        self.assertFalse(old.exists())
        self.assertEqual(before, {name: (self.root / name).read_bytes() for name in before})
        release.verified_source_inventory(self.root)

    def test_prequalified_obsolete_file_already_removed_by_another_loader_is_complete(self):
        old = self.old()
        witness = release._retired_source_witness(old, OLD_PATH)
        old.unlink()
        release._remove_retired_sources([witness])
        release.verified_source_inventory(self.root)
        self.assertFalse(old.exists())

    def test_same_sized_modified_retired_source_is_rejected_and_preserved(self):
        old = self.old()
        old.write_bytes(b'X' + OLD_MENU_CAMERA[1:])
        with self.assertRaisesRegex(release.BuildError, 'Retired B713 Builder file was modified'):
            release.verified_source_inventory(self.root)
        self.assertEqual(old.read_bytes(), b'X' + OLD_MENU_CAMERA[1:])

    def test_unrelated_extra_source_is_rejected_without_deleting_valid_retirement(self):
        old = self.old()
        self.put('src/GloomhavenVR/Unknown.cs', b'unreviewed source')
        with self.assertRaisesRegex(release.BuildError, 'Unlisted builder release file'):
            release.verified_source_inventory(self.root)
        self.assertTrue(old.exists())

    def test_changed_current_source_is_rejected_before_obsolete_cleanup(self):
        old = self.old()
        self.put(self.rows[0]['path'], b'mutated current source')
        with self.assertRaisesRegex(release.BuildError, 'Builder release file changed'):
            release.verified_source_inventory(self.root)
        self.assertTrue(old.exists())

    def test_historical_map_cannot_retire_a_still_listed_current_source(self):
        old = self.old()
        value = json.loads((self.root / release.MANIFEST).read_text())
        value['files'].append(release.record_file(old, OLD_PATH))
        self.put(release.MANIFEST, json.dumps(value).encode())
        release.verified_source_inventory(self.root)
        self.assertEqual(old.read_bytes(), OLD_MENU_CAMERA)

    @unittest.skipIf(os.name == 'nt', 'Symlink creation requires separate Windows privileges.')
    def test_retired_source_symlink_is_rejected_without_reading_or_deleting_target(self):
        target = self.put('foreign-content.bin', OLD_MENU_CAMERA)
        old = self.root / OLD_PATH
        old.parent.mkdir(parents=True, exist_ok=True)
        old.symlink_to(target)
        with self.assertRaises(release.BuildError):
            release.verified_source_inventory(self.root)
        self.assertTrue(old.is_symlink())
        self.assertEqual(target.read_bytes(), OLD_MENU_CAMERA)

    def test_file_changed_after_qualification_is_preserved(self):
        old = self.old()
        real = release._retired_source_witness
        def change(path, relative):
            witness = real(path, relative)
            path.write_bytes(b'Y' + OLD_MENU_CAMERA[1:])
            return witness
        with mock.patch.object(release, '_retired_source_witness', side_effect=change):
            with self.assertRaisesRegex(release.BuildError, 'changed during verification'):
                release.verified_source_inventory(self.root)
        self.assertEqual(old.read_bytes(), b'Y' + OLD_MENU_CAMERA[1:])

    def test_obsolete_source_delete_denial_has_actionable_clean_folder_route(self):
        old = self.old()
        real = Path.unlink
        def unlink(path, *args, **kwargs):
            if path == old:
                raise PermissionError('controlled deletion denial')
            return real(path, *args, **kwargs)
        with mock.patch.object(Path, 'unlink', unlink):
            with self.assertRaisesRegex(release.BuildError, 'extract the new Builder into a clean folder'):
                release.verified_source_inventory(self.root)
        self.assertEqual(old.read_bytes(), OLD_MENU_CAMERA)


if __name__ == '__main__':
    unittest.main()
