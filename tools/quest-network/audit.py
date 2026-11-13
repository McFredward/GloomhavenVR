"""Read-only local evidence for the original Quest multiplayer candidate.

No backend requests, credential output, assembly rewriting, or runtime success
claims. ILSpy output stays in memory and reports contain hashes/line references.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import uuid

REPO = Path(__file__).resolve().parents[2]
MAX_TEXT = 4 * 1024 * 1024
GAME_TYPES = {
    "PlatformLayer": "PlatformLayer.cs", "PlatformNetworking": "PlatformNetworking.cs",
    "PlatformUserData": "PlatformUserData.cs", "SaveData": "SaveData.cs",
    "GHNetworkCallbacks": "GHNetworkCallbacks.cs",
    "FFSNet.NetworkManager": "FFSNet/NetworkManager.cs",
    "FFSNet.NetworkCallbacks": "FFSNet/NetworkCallbacks.cs",
    "FFSNet.NetworkVersion": "FFSNet/NetworkVersion.cs",
    "FFSNet.UserToken": "FFSNet/UserToken.cs", "FFSNet.PlayerToken": "FFSNet/PlayerToken.cs",
}
DEPENDENCIES = {
    "SM.Consoles.dll": ["Platforms.Utils.PlatformConstructor", "Platforms.Generic.PlatformGeneric",
        "Platforms.Generic.PlatformSocialGeneric", "Platforms.Generic.PlatformProfanityGeneric",
        "Platforms.PlatformBase", "Platforms.Steam.PlatformHydraAnalyticsSteam"],
    "udpkit.platform.photon.dll": ["UdpKit.Platform.PhotonPlatformConfig",
        "UdpKit.Platform.PhotonPlatform", "UdpKit.Platform.Photon.Realtime.PhotonClient"],
    "PhotonRealtime.dll": ["Photon.Realtime.LoadBalancingPeer"],
    "Photon3Unity3D.dll": ["ExitGames.Client.Photon.PhotonPeer"],
    "com.playeveryware.eos.core.dll": ["EOSPackageInfo", "PlayEveryWare.EpicOnlineServices.EOSManager"],
    "com.Epic.OnlineServices.dll": ["Epic.OnlineServices.Config"],
}
MANAGED = tuple(sorted(set(DEPENDENCIES) | {"GH.Runtime.dll", "GH.Shared.dll", "bolt.dll",
    "bolt.user.dll", "PhotonBolt.dll", "udpkit.dll", "udpkit.common.dll", "udpkit.platform.dotnet.dll"}))
PHOTON_MANAGED = ("GH.Runtime.dll", "GH.Shared.dll", "SM.Consoles.dll", "bolt.dll", "bolt.user.dll",
    "PhotonBolt.dll", "PhotonRealtime.dll", "Photon3Unity3D.dll", "udpkit.dll", "udpkit.common.dll",
    "udpkit.platform.photon.dll", "udpkit.platform.dotnet.dll")
EOS_FIELDS = ("productID", "sandboxID", "deploymentID", "clientID", "clientSecret", "encryptionKey")
NATIVE_NAMES = ("EOSSDK-Win64-Shipping.dll", "steam_api64.dll", "opus_egpv.dll",
    "webrtc-audio.dll", "AudioIn.dll", "libEOSSDK.so", "libPhotonSocketPlugin.so",
    "libPhotonEncryptorPlugin.so", "libopus_egpv.so", "libwebrtc-audio.so", "libAudioIn.so")


class AuditError(Exception):
    pass


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def read_text(path):
    if path.stat().st_size > MAX_TEXT:
        raise AuditError("A source/config file exceeds the bounded audit size")
    return path.read_text(encoding="utf-8-sig")


def code_only(text, strings=False):
    # Preserve offsets and line numbers while excluding comments and optionally
    # strings from evidence. A comment cannot manufacture a positive finding.
    pattern = r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    def replace(match):
        value = match.group()
        if strings and not value.startswith(("//", "/*")):
            return value
        return "".join("\n" if char == "\n" else " " for char in value)
    return re.sub(pattern, replace, text)


def member(text, name, parameter=None):
    """Find one block member; ambiguity or unsupported syntax stays unknown."""
    clean = code_only(text)
    pattern = r"^\s*(?:public|private|protected|internal)\b[^\n;{}]*\b" + re.escape(name) + r"\s*\([^\n{}]*\)"
    found = [match for match in re.finditer(pattern, clean, re.M)
             if parameter is None or parameter in match.group()]
    if len(found) != 1:
        return None
    start = clean.find("{", found[0].end())
    if start < 0 or ";" in clean[found[0].end():start]:
        return None
    depth = 0
    for end in range(start, len(clean)):
        depth += (clean[end] == "{") - (clean[end] == "}")
        if depth == 0:
            return text[start:end + 1], text[:start].count("\n") + 1
    return None


class Evidence:
    def __init__(self, managed, decompiled, ilspy=None, runner=subprocess.run):
        self.managed, self.decompiled, self.ilspy, self.runner = managed, decompiled, ilspy, runner
        self.inputs, self.texts, self.provenance, self.gaps = [], {}, {}, []
        self.snapshots = {}

    def record(self, path, reference):
        if not path.is_file():
            self.gaps.append(reference + " is missing")
            return None
        sha = digest(path)
        self.snapshots[path] = sha
        value = {"reference": reference, "bytes": path.stat().st_size, "sha256": sha}
        if value not in self.inputs:
            self.inputs.append(value)
        return value

    def load(self):
        self.record(Path(__file__), "tool/network-audit.py")
        for name in MANAGED:
            self.record(self.managed / name, "Managed/" + name)
        if self.ilspy:
            self.record(self.ilspy, "tool/ilspycmd")
        for name, relative in GAME_TYPES.items():
            if self.ilspy:
                self.extract("GH.Runtime.dll", name)
            else:
                path = self.decompiled / "GH.Runtime" / relative
                if self.record(path, "decompiled/GH.Runtime/" + relative):
                    self.texts[name] = read_text(path)
                    self.provenance[name] = "decompiled/GH.Runtime/" + relative
        if self.ilspy:
            for assembly, names in DEPENDENCIES.items():
                for name in names:
                    self.extract(assembly, name)
        else:
            self.gaps.append("No ILSpy supplied: dependency bodies and source/binary correspondence are unverified")

    def extract(self, assembly, name):
        if not (self.managed / assembly).is_file():
            return
        command = [str(self.ilspy), "--disable-updatecheck", "-r", str(self.managed),
                   "-t", name, str(self.managed / assembly)]
        try:
            result = self.runner(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
        except (OSError, subprocess.TimeoutExpired):
            self.gaps.append("ILSpy could not inspect " + name)
            return
        if result.returncode or len(result.stdout) > MAX_TEXT:
            # Tool output may contain source literals or credentials. Never echo it.
            self.gaps.append("ILSpy failed or exceeded the limit for " + name)
            return
        self.texts[name] = result.stdout.decode("utf-8-sig")
        self.provenance[name] = "Managed/" + assembly + "::" + name

    def finding(self, name, source, method, required, forbidden=(), parameter=None):
        text = self.texts.get(source, "")
        selected = member(text, method, parameter) if method else (text, 1)
        value = {"id": name, "matched": False, "source": self.provenance.get(source), "member": method}
        if not selected or not selected[0]:
            value["status"] = "unknown-member"
            return value
        body, line = selected
        clean = code_only(body, strings=True)
        value.update(line=line, bodySha256=hashlib.sha256(body.encode()).hexdigest())
        value["matched"] = all(re.search(pattern, clean) for pattern in required) and not any(
            re.search(pattern, code_only(body)) for pattern in forbidden)
        value["status"] = "matched-patterns" if value["matched"] else "review-required"
        return value

    def unchanged(self):
        if any(not path.is_file() or digest(path) != sha for path, sha in self.snapshots.items()):
            raise AuditError("An input changed during inspection; rerun against a stable local snapshot")


def native_info(path):
    """Inspect ELF/PE bytes without loading or executing the library."""
    value = {"bytes": path.stat().st_size, "sha256": digest(path), "androidArm64": False}
    with path.open("rb") as stream:
        head = stream.read(64)
        if head[:4] == b"\x7fELF" and len(head) == 64:
            value["format"] = "ELF"
            if head[4] not in (1, 2) or head[5] not in (1, 2):
                return value
            endian = "<" if head[5] == 1 else ">"
            value["bits"] = 64 if head[4] == 2 else 32
            value["machine"] = struct.unpack_from(endian + "H", head, 18)[0]
            value["androidArm64"] = value["bits"] == 64 and value["machine"] == 183 and head[5] == 1
        elif head[:2] == b"MZ" and len(head) == 64:
            offset = struct.unpack_from("<I", head, 60)[0]
            if offset <= value["bytes"] - 6:
                stream.seek(offset)
                signature = stream.read(6)
                if signature[:4] == b"PE\0\0":
                    value.update(format="PE", machine=struct.unpack_from("<H", signature, 4)[0])
        if "format" not in value:
            value["format"] = "unknown"
    return value


def configuration(evidence, game, recovered, appclient):
    result = {"valuesExported": False}
    path = game / "StreamingAssets/EOS/EpicOnlineServicesConfig.json"
    if evidence.record(path, "StreamingAssets/EOS/EpicOnlineServicesConfig.json"):
        try:
            content = json.loads(read_text(path))
            if not isinstance(content, dict):
                raise ValueError()
            result["eos"] = {"validJsonObject": True, "presentFields": {
                key: isinstance(content.get(key), str) and bool(content[key]) for key in EOS_FIELDS}}
        except (ValueError, UnicodeError):
            result["eos"] = {"validJsonObject": False}
    if appclient:
        for index, item in enumerate(appclient):
            evidence.record(item, "private-appclientconfig/" + str(index + 1))
        result["appClientConfiguration"] = "hash-only; semantic requirements are not inferred"
    path = recovered / "Assets/Resources/BoltRuntimeSettings.asset" if recovered else None
    if path and evidence.record(path, "recovered/Assets/Resources/BoltRuntimeSettings.asset"):
        text = read_text(path)
        fields = re.findall(r"^\s*photonAppId:\s*([^\n]*)", text, re.M)
        valid = False
        if len(fields) == 1:
            try:
                valid = uuid.UUID(fields[0].strip().strip('\"\'' )).int != 0
            except ValueError:
                pass
        result["bolt"] = {"appIdPresentAndValid": valid, "values": {}}
        for key in ("photonUsePunch", "photonCloudRegionIndex", "RoomCreateTimeout", "RoomJoinTimeout",
                    "framesPerSecond", "packetSize", "EnableIPv6", "serverConnectionAcceptMode"):
            values = re.findall(r"^\s*" + key + r":\s*([0-9]+(?:\.[0-9]+)?)\s*$", text, re.M)
            if len(values) == 1:
                result["bolt"]["values"][key] = float(values[0]) if "." in values[0] else int(values[0])
    return result


def build_report(game, decompiled, recovered=None, ilspy=None, appclient=(), candidates=(), runner=subprocess.run):
    game, decompiled = Path(game).resolve(), Path(decompiled).resolve()
    if not (game / "Managed").is_dir():
        raise AuditError("--game-data must contain the original Managed directory")
    evidence = Evidence(game / "Managed", decompiled, ilspy, runner)
    evidence.load()
    rules = [
        ("desktop-steam-quit", "PlatformLayer", "Init", [r"SteamClient\.Init\(", r"Application\.Quit\(-1\)"]),
        ("startup-eos-instantiation", "PlatformLayer", "Initialize", [r"Instantiate\(EOSManagerPrefab", r"EOSManager\.Instance\.Init\("]),
        ("startup-hydra-enabled", "PlatformLayer", "InitialisePlatformLayer", [r"bool initHydra = true", r"BuildPlatform\("]),
        ("session-code-photon", "FFSNet.NetworkManager", "SwitchRegion", [r"new PhotonPlatform\(\)", r"SetUdpPlatform\("]),
        ("join-code-no-provider-auth", "FFSNet.NetworkManager", "JoinSession", [r"BoltMatchmaking\.JoinSession\(SessionID, userToken\)", r"CheckForPrivilegeValidityAsync"], [r"EOSManager|SteamUser|SessionTicket|AuthSessionTicket"], "string sessionID"),
        ("token-no-provider-ticket", "FFSNet.NetworkManager", "GetUserToken", [r"NetworkVersion\.Current", r"new UserToken\(", r"MaskBadWordsInUsername"], [r"EOSManager|ProductUserId|SteamUser|SessionTicket|AuthSessionTicket"]),
        ("desktop-local-privilege-policy", "PlatformNetworking", "GetCurrentUserPrivilegesAsync", [r"resultCallback\?\.Invoke\(OperationResult\.Success, arg2: true\)"], [r"EOSManager|SteamUser|PlatformSocial"]),
        ("host-original-admission", "GHNetworkCallbacks", "ConnectRequest", [r"CheckVersions\(userToken\.GameVersion\)", r"CrossplayEnabled", r"PassesBasicConnectionTests"], [r"EOSManager|ProductUserId|SteamUser|SessionTicket"]),
        ("optional-save-eos-login", "SaveData", "InitialiseDataManagers", [r"if \(Instance\.Global\.EpicLogin\)", r"EOSInitialise\(\)"]),
        ("eos-legitimate-login-candidate", "PlatformUserData", None, [r"LoginCredentialType\.PersistentAuth", r"LoginCredentialType\.AccountPortal", r"StartConnectLoginWithEpicAccount"]),
        ("eos-invite-code-bridge", "PlatformNetworking", "EpicJoinGame", [r'"PHOTONKEY"', r"EPICPendingInviteLobbyID"]),
        ("compiled-steam-constructor", "Platforms.Utils.PlatformConstructor", "BuildPlatform", [r"return new PlatformSteam\("]),
        ("generic-local-services", "Platforms.Generic.PlatformGeneric", "PlatformGeneric", [r"new PlatformSocialGeneric\(\)", r"new PlatformProfanityGeneric\(\)", r"if \(initHydra\)"]),
        ("generic-social-not-desktop-policy", "Platforms.Generic.PlatformSocialGeneric", "GetCurrentUserPrivilegesAsync", [r"OperationResult\.UnspecifiedError"]),
        ("generic-profanity-local", "Platforms.Generic.PlatformProfanityGeneric", "MaskBadWordsAsync", [r"GetBadWords\(", r"resultCallback\?\.Invoke\(OperationResult\.Success"]),
        ("photon-default-auth-null", "UdpKit.Platform.PhotonPlatformConfig", "InitDefaults", [r'GetField\("photonAppId"', r"AuthenticationValues = null"]),
        ("photon-conditional-custom-auth", "UdpKit.Platform.Photon.Realtime.PhotonClient", "PhotonClient", [r"if \(config\.AuthenticationValues != null\)", r"base\.AuthValues = config\.AuthenticationValues"]),
        ("photon-managed-sockets", "Photon.Realtime.LoadBalancingPeer", "ConfigUnitySockets", [r"ConnectionProtocol\.Udp\] = typeof\(SocketUdpAsync\)", r"ConnectionProtocol\.Tcp\] = typeof\(SocketTcpAsync\)"]),
        ("photon-managed-encryption-fallback", "ExitGames.Client.Photon.PhotonPeer", "InitDatagramEncryption", [r"if \(Encryptor == null\)", r"new EncryptorNet\(\)"]),
        ("eos-windows-bindings", "Epic.OnlineServices.Config", None, [r'LibraryName = "EOSSDK-Win64-Shipping"']),
        ("hydra-steam-ticket", "Platforms.Steam.PlatformHydraAnalyticsSteam", "SignInForPlatform", [r"GetAuthTicket\(\)", r"SignInSteam\("]),
    ]
    findings = [evidence.finding(*rule) for rule in rules]
    matched = {item["id"]: item["matched"] for item in findings}
    configs = configuration(evidence, game, Path(recovered).resolve() if recovered else None, appclient)
    natives = []
    for label, base in [("original", game / "Plugins")] + [("candidate-" + str(index + 1), Path(path)) for index, path in enumerate(candidates)]:
        if not base.is_dir():
            evidence.gaps.append(label + " native directory is missing")
            continue
        for path in sorted(base.rglob("*")):
            if path.name in NATIVE_NAMES and path.is_file():
                row = native_info(path)
                row["reference"] = label + "/" + path.relative_to(base).as_posix()
                natives.append(row)
                evidence.record(path, row["reference"])
    local_ids = ("session-code-photon", "join-code-no-provider-auth", "token-no-provider-ticket",
                 "desktop-local-privilege-policy", "host-original-admission", "photon-default-auth-null",
                 "photon-conditional-custom-auth", "photon-managed-sockets", "photon-managed-encryption-fallback")
    managed_present = all((game / "Managed" / name).is_file() for name in PHOTON_MANAGED)
    candidate = bool(ilspy) and managed_present and all(matched[name] for name in local_ids) and configs.get("bolt", {}).get("appIdPresentAndValid", False)
    evidence.unchanged()
    inputs = sorted(evidence.inputs, key=lambda item: item["reference"])
    input_key = hashlib.sha256(json.dumps(inputs, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    version_text = evidence.texts.get("FFSNet.NetworkVersion", "")
    version = re.search(r'Current\s*=\s*"([0-9]{1,8})"', code_only(version_text, strings=True))
    return {"schema": 1, "inputKey": input_key, "inputs": inputs, "findings": findings,
        "configuration": configs, "nativeLibraries": natives, "gaps": evidence.gaps,
        "originalNetworkVersion": version.group(1) if version else None,
        "candidate": {"status": "source-supported-photon-session-code" if candidate else "needs-local-review",
            "sourceSupported": candidate, "originalManagedInputsPresent": managed_present,
            "requiredEosByInspectedJoinPath": False if candidate else None,
            "providerBoundaryAdaptationRequired": True, "backendPolicyKnown": False,
            "runtimeConnected": False, "pcHostJoined": False, "fullGameCrossplayProven": False},
        "limitations": ["Pattern evidence is scoped to the named original members, not a complete call-graph proof",
            "Original client configuration does not prove publisher backend acceptance",
            "Embedded Steam profile is offline presentation/save identity, never an authentication ticket",
            "IL2CPP preservation, Android startup/socket/encryption and host join remain device tests"]}


def write_report(path, report, inputs, repo=REPO):
    target = Path(path).resolve()
    if any(target == root or root in target.parents for root in (Path(item).resolve() for item in inputs)):
        raise AuditError("The report must be outside every read-only input directory")
    repo = Path(repo).resolve()
    if repo in target.parents and not any(root in target.parents for root in (
            repo / ".planning/debug", repo / ".planning/quest3-local")):
        raise AuditError("Reports inside the repository must use an ignored private output directory")
    target.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=".network-report-", dir=target.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(report, stream, indent=2, sort_keys=True)
            stream.write("\n")
        os.replace(temporary, target)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, required=True)
    parser.add_argument("--decompiled", type=Path, default=REPO / "decompiled")
    parser.add_argument("--recovered-project", type=Path)
    parser.add_argument("--ilspy", type=Path, help="Local trusted ilspycmd; output never exported")
    parser.add_argument("--appclient-config", type=Path, action="append", default=[], help="Optional existing config, hash only")
    parser.add_argument("--native-candidates", type=Path, action="append", default=[])
    parser.add_argument("--report", type=Path, default=REPO / ".planning/debug/quest3/network/network-audit.json")
    parser.add_argument("--require-local-candidate", action="store_true", help="Exit 2 unless inspected local session-code prerequisites match")
    args = parser.parse_args(argv)
    try:
        report = build_report(args.game_data, args.decompiled, args.recovered_project, args.ilspy,
                              args.appclient_config, args.native_candidates)
        inputs = [args.game_data, args.decompiled, *args.native_candidates]
        if args.recovered_project:
            inputs.append(args.recovered_project)
        inputs.extend(args.appclient_config)
        if args.ilspy:
            inputs.append(args.ilspy)
        write_report(args.report, report, inputs)
        print("Quest network audit: " + report["candidate"]["status"])
        print("Private report: " + str(args.report))
        print("No connection was attempted; Android/backend acceptance remains unverified.")
        return 2 if args.require_local_candidate and not report["candidate"]["sourceSupported"] else 0
    except (AuditError, OSError, UnicodeError, ValueError) as failure:
        # JSON/decompiler exceptions can include private input contents: keep output generic.
        reason = str(failure) if isinstance(failure, AuditError) else "Cannot read or validate a local input/report; check paths and permissions"
        print("Quest network audit failed: " + reason, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
