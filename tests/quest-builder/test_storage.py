"""Offline provider entitlement and actual native-byte save transfer controls."""
from pathlib import Path
import hashlib
import json
import sys
import tempfile
import types
import unittest
from unittest import mock
import zipfile

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/"tools/quest-builder"))
import dlcs
import profile as identity
import provider_metadata as providers
import save_export as saves
from storage import BuildError


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory();self.addCleanup(self.temporary.cleanup);self.root=Path(self.temporary.name)
        self.pc=self.root/"PC/GloomSaves";self.pc.mkdir(parents=True)
        self.original={"GloomSaven.dat":b"original native root fixture", "GlobalData.dat":b"native global index fixture",
                       "Campaign/Campaign_First_42/Campaign_First_42.dat":b"native campaign fixture",
                       "Campaign/Campaign_First_42/Checkpoints/1.dat":b"native checkpoint fixture",
                       "Guildmaster/Guildmaster_Kept_42/Guildmaster_Kept_42.dat":b"excluded mode bytes retained"}
        for relative,raw in self.original.items():
            path=self.pc/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(raw)
        self.archive=self.root/"pc-saves.zip"
    def snapshot(self):
        saves.export_snapshot(self.pc,self.archive);return self.archive
    def test_roundtrip_preserves_every_native_byte_and_excluded_mode(self):
        self.snapshot();quest=self.root/"Quest/GloomSaves";receipt=saves.import_snapshot(self.archive,quest)
        self.assertTrue(receipt["nativeBytesUnchanged"]);self.assertFalse(receipt["cloudServicesUsed"])
        self.assertEqual(len(receipt["backup"] or ""),0)
        for relative,raw in self.original.items():self.assertEqual((quest/relative).read_bytes(),raw)
        (quest/"Campaign/Campaign_First_42/Campaign_First_42.dat").write_bytes(b"native Quest gameplay progress fixture")
        export=self.root/"quest-saves.zip";saves.export_snapshot(quest,export)
        returned=self.root/"PC-return/GloomSaves";saves.import_snapshot(export,returned)
        self.assertEqual((returned/"Campaign/Campaign_First_42/Campaign_First_42.dat").read_bytes(),b"native Quest gameplay progress fixture")
        self.assertEqual((returned/"Guildmaster/Guildmaster_Kept_42/Guildmaster_Kept_42.dat").read_bytes(),self.original["Guildmaster/Guildmaster_Kept_42/Guildmaster_Kept_42.dat"])
    def test_existing_saves_require_explicit_replace_and_complete_backup(self):
        self.snapshot();quest=self.root/"Quest/GloomSaves";quest.mkdir(parents=True);(quest/"unrelated.dat").write_bytes(b"keep")
        with self.assertRaises(BuildError):saves.import_snapshot(self.archive,quest)
        self.assertEqual((quest/"unrelated.dat").read_bytes(),b"keep")
        result=saves.import_snapshot(self.archive,quest,replace_existing=True)
        self.assertEqual((Path(result["backup"])/"unrelated.dat").read_bytes(),b"keep")
        self.assertFalse((quest.parent/saves.JOURNAL).exists())
    def test_failed_new_root_move_rolls_back_original_tree(self):
        self.snapshot();quest=self.root/"Quest/GloomSaves";quest.mkdir(parents=True);(quest/"unrelated.dat").write_bytes(b"keep")
        replace=saves.os.replace
        def failure(source,destination):
            if Path(source).name.startswith(".quest-save-import-") and Path(destination)==quest:raise OSError("simulated commit failure")
            return replace(source,destination)
        with mock.patch.object(saves.os,"replace",side_effect=failure):
            with self.assertRaises(OSError):saves.import_snapshot(self.archive,quest,True)
        self.assertEqual((quest/"unrelated.dat").read_bytes(),b"keep")
        self.assertFalse((quest.parent/saves.JOURNAL).exists())
    def test_export_does_not_follow_links_or_overwrite_archives(self):
        (self.pc/"outside.dat").symlink_to(self.root/"foreign")
        with self.assertRaises(BuildError):self.snapshot()
        (self.pc/"outside.dat").unlink();self.snapshot()
        with self.assertRaises(BuildError):self.snapshot()
    def test_incomplete_save_root_is_rejected(self):
        (self.pc/"GlobalData.dat").unlink()
        with self.assertRaises(BuildError):self.snapshot()
    def test_export_detects_native_game_change_and_publishes_no_archive(self):
        original_read=Path.read_bytes
        def change(path):
            raw=original_read(path)
            if path.name=="GlobalData.dat":path.write_bytes(raw+b" changed")
            return raw
        with mock.patch.object(Path,"read_bytes",change):
            with self.assertRaises(BuildError):self.snapshot()
        self.assertFalse(self.archive.exists())
    def test_internal_atomic_backups_do_not_replace_original_snapshot_records(self):
        (self.pc/"GlobalData.dat.ghvr-save-backup").write_bytes(b"previous global")
        (self.pc/"GlobalData.dat.ghvr-save-write-abc.tmp").write_bytes(b"incomplete")
        self.snapshot();manifest=saves.validate_snapshot(self.archive)
        self.assertEqual(len(manifest["files"]),len(self.original))
    def mutate(self,mutator):
        self.snapshot()
        with zipfile.ZipFile(self.archive) as z:entries={name:z.read(name) for name in z.namelist()}
        manifest=json.loads(entries["save-manifest.json"]);mutator(manifest,entries)
        entries["save-manifest.json"]=json.dumps(manifest).encode()
        damaged=self.root/"bad.zip"
        with zipfile.ZipFile(damaged,"w") as z:
            for name,raw in entries.items():z.writestr(name,raw)
        return damaged
    def test_corrupt_payload_and_extra_file_do_not_mutate_destination(self):
        damaged=self.mutate(lambda manifest,entries:entries.update({"files/GlobalData.dat":b"corruption"}))
        with self.assertRaises(BuildError):saves.import_snapshot(damaged,self.root/"target/GloomSaves")
        self.assertFalse((self.root/"target").exists())
    def test_escaping_path_is_rejected_before_extraction(self):
        def poison(manifest,entries):
            row=manifest["files"][0];old=row["path"];row["path"]="../foreign.dat";entries["files/../foreign.dat"]=entries.pop("files/"+old)
        with self.assertRaises(BuildError):saves.validate_snapshot(self.mutate(poison))
        self.assertFalse((self.root/"foreign.dat").exists())
    def test_windows_case_collision_is_rejected(self):
        def poison(manifest,entries):
            row={**manifest["files"][0],"path":manifest["files"][0]["path"].upper()};manifest["files"].append(row);entries["files/"+row["path"]]=entries["files/"+manifest["files"][0]["path"]]
        with self.assertRaises(BuildError):saves.validate_snapshot(self.mutate(poison))
    def test_manifest_size_and_boolean_size_and_unexpected_entries_rejected(self):
        for mutator in (lambda m,e:m["files"][0].update(bytes=True), lambda m,e:e.update({"unexpected.txt":b"no"}), lambda m,e:m.update(saveRoot="../outside")):
            with self.subTest(mutator=mutator):
                self.archive.unlink(missing_ok=True)
                with self.assertRaises(BuildError):saves.validate_snapshot(self.mutate(mutator))
    def test_release_channel_and_pending_journal_do_not_overwrite(self):
        self.snapshot()
        with self.assertRaises(BuildError):saves.import_snapshot(self.archive,self.root/"Quest/GloomSavesDev")
        parent=self.root/"Quest";parent.mkdir();(parent/saves.JOURNAL).write_text("pending")
        with self.assertRaises(BuildError):saves.import_snapshot(self.archive,parent/"GloomSaves")
    def test_epic_gog_full_profile_identity_stable_and_no_credential_fields(self):
        for provider,id in (("epic","a"*32),("gog","1234567890123")):
            value={"schema":1,"provider":provider,"providerId":id,"displayName":"Local Owner"}
            first=identity.validate_identity(value);second=identity.validate_identity(value)
            self.assertEqual(first,second);self.assertEqual(first["providerId"],id);self.assertEqual(first["steamId"],"0");self.assertGreater(first["accountId"],0)
            for invalid in ({**value,"token":"must-not-read"},{**value,"accountId":first["accountId"]+1},{**value,"steamId":"76561198000000000"},{**value,"providerId":"product-not-user"}):
                with self.assertRaises(identity.ProfileError):identity.validate_identity(invalid)
    def gog(self,name,id,root="1000"):
        game=self.root/"GOG";game.mkdir(exist_ok=True)
        (game/("goggame-"+id+".info")).write_text(json.dumps({"gameId":id,"rootGameId":root,"name":name}))
        return game
    def test_gog_offline_installed_markers_capture_owned_jotl_solo(self):
        game=self.gog("Gloomhaven","1000");self.gog("Gloomhaven - Jaws of the Lion","1001");self.gog("Gloomhaven - Solo Scenarios Pack: Mercenary Challenges","1002")
        evidence=providers.gog_installation(game);self.assertEqual(evidence["ownedDlcKeys"],["jotl","solo"])
        self.assertFalse(evidence["profileAvailable"])
        profile=identity.validate_identity({"provider":"gog","providerId":"12345","displayName":"GOG Owner"})
        captured=dlcs.capture(types.SimpleNamespace(),game,profile)
        self.assertEqual(captured["ownedMask"],3);self.assertEqual(captured["providerId"],"12345")
        self.assertEqual(captured["source"],"gog-installed-mini-manifests")
    def test_gog_wrong_base_product_duplicate_dlc_or_product_id_not_granted(self):
        game=self.gog("Gloomhaven","1000");self.gog("Gloomhaven - Jaws of the Lion","1001",root="999")
        with self.assertRaises(BuildError):providers.gog_installation(game)
        (game/"goggame-1001.info").unlink();self.gog("Gloomhaven - Jaws of the Lion","1001");self.gog("Gloomhaven - Jaws of the Lion","1002")
        with self.assertRaises(BuildError):providers.gog_installation(game)
        (game/"goggame-1002.info").unlink();(game/"goggame-1001.info").write_text(json.dumps({"gameId":"999","name":"Gloomhaven - Jaws of the Lion"}))
        with self.assertRaises(BuildError):providers.gog_installation(game)
    def test_bundled_dlc_rules_never_prove_gog_or_epic_ownership(self):
        for row in dlcs.CATALOG:
            path=self.root/"Game/StreamingAssets/Rulebase/DLC"/row[4];path.mkdir(parents=True)
            (path/(row[4]+"_Global.ruleset")).write_bytes(b"universally bundled")
        for provider,id in (("gog","12345"),("epic","b"*32)):
            profile=identity.validate_identity({"provider":provider,"providerId":id,"displayName":"Owner"})
            with self.assertRaises(BuildError):dlcs.capture(types.SimpleNamespace(),self.root/"Game",profile)
    def test_epic_actual_installation_identifiers_are_not_account_or_dlc(self):
        game=self.root/"EpicGame";game.mkdir();manifests=self.root/"EpicManifests";manifests.mkdir()
        row={"DisplayName":"Gloomhaven","InstallLocation":str(game),"AppName":"OriginalApp","MainGameAppName":"OriginalApp","CatalogItemId":"c"*32,"CatalogNamespace":"originalNamespace"}
        (manifests/"install.item").write_text(json.dumps(row));(game/".egstore").mkdir()
        (game/".egstore/install.mancpn").write_text(json.dumps({field:row[field] for field in ("AppName","CatalogItemId","CatalogNamespace")}))
        evidence=providers.epic_installation(game,manifests)
        self.assertFalse(evidence["profileAvailable"]);self.assertFalse(evidence["ownershipComplete"]);self.assertEqual(evidence["baseProductId"],"c"*32)
        profile=identity.validate_identity({"provider":"epic","providerId":"d"*32,"displayName":"Epic Owner"})
        with self.assertRaises(BuildError):dlcs.capture(types.SimpleNamespace(provider_metadata_dir=manifests),game,profile)
        self.assertEqual(dlcs.capture(types.SimpleNamespace(owned_dlc=["solo"]),game,profile)["ownedMask"],2)
    def test_epic_mismatched_moved_installation_markers_fail_closed(self):
        game=self.root/"EpicGame";(game/".egstore").mkdir(parents=True)
        for n in range(2):(game/".egstore"/(str(n)+".mancpn")).write_text(json.dumps({"AppName":"app"+str(n),"CatalogItemId":"item","CatalogNamespace":"ns"}))
        with self.assertRaises(BuildError):providers.epic_installation(game)
    def test_explicit_provider_dlc_declaration_preserves_account_association(self):
        profile=identity.validate_identity({"provider":"gog","providerId":"12345","displayName":"Owner"})
        path=self.root/"ownership.json";valid={"schema":1,"provider":"gog","providerId":"12345","appId":780290,"installedAppIds":[1809490,1958560]};path.write_text(json.dumps(valid))
        self.assertEqual(dlcs.capture(types.SimpleNamespace(dlc_ownership_json=path),self.root,profile)["ownedMask"],3)
        path.write_text(json.dumps({**valid,"providerId":"other"}))
        with self.assertRaises(BuildError):dlcs.capture(types.SimpleNamespace(dlc_ownership_json=path),self.root,profile)


if __name__=="__main__":unittest.main()
