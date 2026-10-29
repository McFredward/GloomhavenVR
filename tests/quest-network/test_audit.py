"""Synthetic source/control fixtures, never original publisher configuration."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_network_audit", ROOT / "tools/quest-network/audit.py")
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

SOURCES = {
    "FFSNet.NetworkManager": '''class NetworkManager {
 public void SwitchRegion() { BoltLauncher.SetUdpPlatform(new PhotonPlatform()); }
 public void JoinSession(string sessionID) {
  CheckForPrivilegeValidityAsync(); BoltMatchmaking.JoinSession(SessionID, userToken);
 }
 private void GetUserToken() {
  var version = NetworkVersion.Current; new UserToken(); MaskBadWordsInUsername();
 }
}''',
    "PlatformNetworking": '''class PlatformNetworking {
 public void GetCurrentUserPrivilegesAsync() {
  resultCallback?.Invoke(OperationResult.Success, arg2: true);
 }
 public void EpicJoinGame() { var key = "PHOTONKEY"; EPICPendingInviteLobbyID(); }
}''',
    "GHNetworkCallbacks": '''class GHNetworkCallbacks {
 public void ConnectRequest() {
  CheckVersions(userToken.GameVersion); CrossplayEnabled(); PassesBasicConnectionTests();
 }
}''',
    "UdpKit.Platform.PhotonPlatformConfig": '''class PhotonPlatformConfig {
 internal void InitDefaults() {
  GetField("photonAppId"); AuthenticationValues = null;
 }
}''',
    "UdpKit.Platform.Photon.Realtime.PhotonClient": '''class PhotonClient {
 public PhotonClient() {
  if (config.AuthenticationValues != null) { base.AuthValues = config.AuthenticationValues; }
 }
}''',
    "Photon.Realtime.LoadBalancingPeer": '''class LoadBalancingPeer {
 private void ConfigUnitySockets() {
  SocketImplementationConfig[ConnectionProtocol.Udp] = typeof(SocketUdpAsync);
  SocketImplementationConfig[ConnectionProtocol.Tcp] = typeof(SocketTcpAsync);
 }
}''',
    "ExitGames.Client.Photon.PhotonPeer": '''class PhotonPeer {
 public void InitDatagramEncryption() {
  if (Encryptor == null) { Encryptor = new EncryptorNet(); }
 }
}''',
    "FFSNet.NetworkVersion": 'class NetworkVersion { public static string Current = "2"; }',
}


class AuditTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="network audit spaces ")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game = self.root / "owned game"
        self.managed = self.game / "Managed"
        self.managed.mkdir(parents=True)
        (self.game / "Plugins/x86_64").mkdir(parents=True)
        for name in audit.MANAGED:
            (self.managed / name).write_bytes(b"fixture managed input " + name.encode())
        self.decompiled = self.root / "decompiled"
        for name, relative in audit.GAME_TYPES.items():
            path = self.decompiled / "GH.Runtime" / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(SOURCES.get(name, "class Empty {}"))
        eos = self.game / "StreamingAssets/EOS/EpicOnlineServicesConfig.json"
        eos.parent.mkdir(parents=True)
        self.private_values = {key: "PRIVATE-" + key + "-fixture" for key in audit.EOS_FIELDS}
        eos.write_text(json.dumps(self.private_values))
        self.recovered = self.root / "recovered project"
        self.bolt = self.recovered / "Assets/Resources/BoltRuntimeSettings.asset"
        self.bolt.parent.mkdir(parents=True)
        self.bolt.write_text("photonAppId: 12345678-1234-1234-1234-123456789abc\nphotonUsePunch: 0\nRoomJoinTimeout: 30\n")
        self.ilspy = self.root / "ilspy executable"
        self.ilspy.write_bytes(b"local trusted fixture tool")
        self.sources = dict(SOURCES)
        self.calls = []

    def runner(self, command, **options):
        self.calls.append(command)
        self.assertEqual(options["timeout"], 60)
        self.assertEqual(command[1], "--disable-updatecheck")
        name = command[command.index("-t") + 1]
        return subprocess.CompletedProcess(command, 0, self.sources.get(name, "class Empty {}").encode(), b"")

    def report(self, **changes):
        values = dict(game=self.game, decompiled=self.decompiled, recovered=self.recovered,
                      ilspy=self.ilspy, runner=self.runner)
        values.update(changes)
        return audit.build_report(**values)

    def test_candidate_is_only_local_source_evidence_never_network_success(self):
        value = self.report()
        self.assertTrue(value["candidate"]["sourceSupported"])
        self.assertFalse(value["candidate"]["requiredEosByInspectedJoinPath"])
        self.assertFalse(value["candidate"]["runtimeConnected"])
        self.assertFalse(value["candidate"]["pcHostJoined"])
        self.assertFalse(value["candidate"]["backendPolicyKnown"])
        self.assertEqual(value["originalNetworkVersion"], "2")
        self.assertTrue(any(item["source"] and item["source"].startswith("Managed/") for item in value["findings"]))

    def test_configs_and_decompiler_source_literals_never_export_values(self):
        self.sources["PlatformLayer"] = 'class Hidden { private string ClientSecret = "PRIVATE-SOURCE-FIXTURE"; }'
        config = self.root / "private appclient.json"
        config.write_text('{"password":"PRIVATE-APPCLIENT-FIXTURE"}')
        value = json.dumps(self.report(appclient=[config]))
        for secret in [*self.private_values.values(), "PRIVATE-SOURCE-FIXTURE", "PRIVATE-APPCLIENT-FIXTURE", "12345678-1234-1234-1234-123456789abc"]:
            self.assertNotIn(secret, value)
        self.assertIn("private-appclientconfig/1", value)

    def test_added_join_auth_gate_prevents_candidate(self):
        self.sources["FFSNet.NetworkManager"] = self.sources["FFSNet.NetworkManager"].replace(
            "CheckForPrivilegeValidityAsync();", "EOSManager.Authenticate(); CheckForPrivilegeValidityAsync();")
        self.assertFalse(self.report()["candidate"]["sourceSupported"])

    def test_nondefault_photon_auth_prevents_candidate(self):
        self.sources["UdpKit.Platform.PhotonPlatformConfig"] = self.sources["UdpKit.Platform.PhotonPlatformConfig"].replace(
            "AuthenticationValues = null", "AuthenticationValues = requiredAuth")
        self.assertFalse(self.report()["candidate"]["sourceSupported"])

    def test_different_privilege_policy_prevents_candidate(self):
        self.sources["PlatformNetworking"] = self.sources["PlatformNetworking"].replace("arg2: true", "arg2: false")
        self.assertFalse(self.report()["candidate"]["sourceSupported"])

    def test_comments_cannot_manufacture_positive_findings(self):
        self.sources["Photon.Realtime.LoadBalancingPeer"] = "class Peer { private void ConfigUnitySockets() { /* SocketImplementationConfig[ConnectionProtocol.Udp] = typeof(SocketUdpAsync); SocketImplementationConfig[ConnectionProtocol.Tcp] = typeof(SocketTcpAsync); */ } }"
        self.assertFalse(self.report()["candidate"]["sourceSupported"])

    def test_auth_words_only_in_comments_or_log_strings_do_not_manufacture_gate(self):
        self.sources["FFSNet.NetworkManager"] = self.sources["FFSNet.NetworkManager"].replace(
            "new UserToken();", 'new UserToken(); /* EOSManager.Authenticate(); */ Log("SteamUser");')
        self.assertTrue(self.report()["candidate"]["sourceSupported"])

    def test_ambiguous_method_stays_unknown(self):
        text = "class Example {\n public void Connect() {}\n public void Connect(int other) {}\n}"
        self.assertIsNone(audit.member(text, "Connect"))

    def test_source_only_cannot_claim_correspondence_with_installed_dll(self):
        value = self.report(ilspy=None)
        self.assertFalse(value["candidate"]["sourceSupported"])
        self.assertTrue(any("source/binary correspondence" in gap for gap in value["gaps"]))
        self.assertEqual(self.calls, [])

    def test_missing_invalid_or_ambiguous_app_guid_prevents_candidate(self):
        for text in ("", "photonAppId: bad-private-value", "photonAppId: 00000000-0000-0000-0000-000000000000",
                     self.bolt.read_text() + self.bolt.read_text()):
            self.bolt.write_text(text)
            self.assertFalse(self.report()["candidate"]["sourceSupported"])

    def test_missing_original_generated_bolt_assembly_prevents_candidate(self):
        (self.managed / "bolt.user.dll").unlink()
        value = self.report()
        self.assertFalse(value["candidate"]["sourceSupported"])
        self.assertFalse(value["candidate"]["originalManagedInputsPresent"])
        self.assertIn("Managed/bolt.user.dll is missing", value["gaps"])

    def test_decompiler_failure_does_not_echo_private_output(self):
        def fail(command, **options):
            return subprocess.CompletedProcess(command, 1, b"PRIVATE-DECOMPILER", b"PRIVATE-ERROR")
        value = json.dumps(self.report(runner=fail))
        self.assertNotIn("PRIVATE-DECOMPILER", value)
        self.assertNotIn("PRIVATE-ERROR", value)
        self.assertIn("needs-local-review", value)

    def test_input_change_during_inspection_is_rejected(self):
        def mutate(command, **options):
            (self.managed / "GH.Runtime.dll").write_bytes(b"concurrent new build")
            return self.runner(command, **options)
        with self.assertRaisesRegex(audit.AuditError, "input changed"):
            self.report(runner=mutate)

    def test_config_change_invalidates_stable_input_key(self):
        before = self.report()["inputKey"]
        self.bolt.write_text(self.bolt.read_text().replace("30", "31"))
        self.assertNotEqual(before, self.report()["inputKey"])
        self.assertEqual(self.report()["inputKey"], self.report()["inputKey"])

    def test_native_arm64_elf_and_x64_pe_distinguished_without_execution(self):
        candidate = self.root / "native candidate"
        candidate.mkdir()
        elf = bytearray(64)
        elf[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", elf, 18, 183)
        (candidate / "libEOSSDK.so").write_bytes(elf)
        pe = bytearray(80)
        pe[:2] = b"MZ"
        struct.pack_into("<I", pe, 60, 64)
        pe[64:70] = b"PE\0\0\x64\x86"
        original = self.game / "Plugins/x86_64/EOSSDK-Win64-Shipping.dll"
        original.write_bytes(pe)
        value = self.report(candidates=[candidate])
        self.assertEqual(len(value["nativeLibraries"]), 2)
        self.assertEqual([item["androidArm64"] for item in value["nativeLibraries"]], [False, True])
        self.assertEqual(audit.native_info(original)["machine"], 0x8664)
        struct.pack_into("<H", elf, 18, 62)
        (candidate / "libEOSSDK.so").write_bytes(elf)
        self.assertFalse(audit.native_info(candidate / "libEOSSDK.so")["androidArm64"])

    def test_invalid_eos_json_reports_invalid_without_contents(self):
        path = self.game / "StreamingAssets/EOS/EpicOnlineServicesConfig.json"
        path.write_text('bad-json-with-PRIVATE-CONTENT')
        value = self.report()
        self.assertFalse(value["configuration"]["eos"]["validJsonObject"])
        self.assertNotIn("PRIVATE-CONTENT", json.dumps(value))

    def test_report_cannot_overwrite_inputs_or_follow_symlink_into_inputs(self):
        target = self.game / "Managed/network-audit.json"
        with self.assertRaisesRegex(audit.AuditError, "outside every"):
            audit.write_report(target, {}, [self.game])
        link = self.root / "linked game"
        link.symlink_to(self.game, target_is_directory=True)
        with self.assertRaisesRegex(audit.AuditError, "outside every"):
            audit.write_report(link / "audit.json", {}, [self.game])
        self.assertFalse(target.exists())

    def test_report_in_repository_is_restricted_to_private_output(self):
        repo = self.root / "repo"
        with self.assertRaisesRegex(audit.AuditError, "ignored private"):
            audit.write_report(repo / "tools/audit.json", {}, [], repo)
        target = repo / ".planning/debug/quest3/network/audit.json"
        audit.write_report(target, {"schema": 1}, [], repo)
        self.assertEqual(json.loads(target.read_text()), {"schema": 1})

    def test_invalid_input_is_actionable_without_traceback_or_report(self):
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            result = audit.main(["--game-data", str(self.root / "missing"), "--report", str(self.root / "out.json")])
        self.assertEqual(result, 1)
        self.assertIn("must contain", stderr.getvalue())
        self.assertNotIn("Traceback", stderr.getvalue())
        self.assertFalse((self.root / "out.json").exists())

    def test_actual_cli_source_only_exit_and_safe_report_paths_with_spaces(self):
        path = self.root / "private report.json"
        result = subprocess.run([sys.executable, str(ROOT / "scripts/inspect-quest-network.py"),
            "--game-data", str(self.game), "--decompiled", str(self.decompiled),
            "--recovered-project", str(self.recovered), "--report", str(path), "--require-local-candidate"],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=20)
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertIn("No connection was attempted", result.stdout)
        self.assertFalse(json.loads(path.read_text())["candidate"]["pcHostJoined"])
        self.assertFalse(any(secret in result.stdout + result.stderr for secret in self.private_values.values()))


if __name__ == "__main__":
    unittest.main()
