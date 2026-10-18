"""Keep witnessed original preparation prefixes across compatible input updates.

The real input key still owns the mutable archive, settings and final Player.
Compatibility here only permits the ordered preparation journal to retain
completed conversions. Game, profile, templates and actual producer logic stay
scoped. Later mod source and release metadata do not produce original assets;
the current real input key still owns all subsequent mod and Player work.
"""
from __future__ import annotations

import ast
import copy
import hashlib
from pathlib import Path
import re
import editor_overlay

from storage import BuildError, _ordinary_owned, value_hash

BUILDER = "tools/quest-builder/builder.py"
IDENTITY = "tools/quest-builder/preparation_identity.py"
RECIPE_IDENTITY = "tools/quest-builder/recovery_resume.py"
METADATA_IDENTITY = "tools/quest-builder/preparation_metadata.py"
DELIVERY_PREFIXES = ("tools/quest-wizard/", "tools/quest-wizard-ui/", "tools/quest-installer/")
HEX = re.compile(r"[0-9a-f]{64}\Z")
# The completed preparation journal lists original output contracts, rather than
# only a small control manifest. Capture134446 reports 25,864,174 journal bytes
# before ownership or contents can be checked. Bound this inventory separately;
# retain the recovery reader's regular-file, bounded-read and JSON checks.
# Its ordinary 16MiB limit still owns input/control manifests. No assets reopen.
MAX_JOURNAL_BYTES = 64 * 1024 * 1024
# Before UI reconstruction the only mod files copied or read by the closed
# startup producers are the Quest template, localization and loading logo.
# Runtime C# and mod bundles are consumed later, under the new real input key.
PREFIX_MOD_INPUTS = {
    "src/GloomhavenVR/Core/Loc/QuestText.cs",
    "src/GloomhavenVR/Assets/GloomhavenVR_logo.png",
}
PREFIX_UNUSED_TOOLS = {"tools/quest-builder/ui_assets.py", "tools/quest-builder/ui_blur.py"}
# These authored mod-bank projects and their original-mesh producers first run
# after startup-archive. They do not produce the closed original-game prefix.
# The complete prepare() AST still rejects an added earlier consumer.
PREFIX_MOD_BANK_ROOTS = ("unity/GloomhavenVR.Assets/", "unity/GloomhavenVR.FigureMeshes/",
                         "tools/environment-mesh/")
PREFIX_MOD_BANK_HELPERS = {"tools/quest-builder/environment_bank.py"}
# Static weaving starts after prepare() returns. These managed runtime adapters
# cannot produce any retained preparation asset; the real current input key
# still owns compilation and weaving, including engine-boundary specialization.
# The complete Builder AST rejects an added earlier consumer of these sources.
PREFIX_PLAYER_ROOTS = ("tools/QuestWeaver/",)
# These exact desktop reference assemblies are consumed by weave(), after
# prepare() returns. Android package binding remains owned by the real input
# key. An unknown RuntimeDeps file stays qualified, as does any earlier new
# consumer in the complete preparation AST.
PREFIX_PLAYER_REFERENCES = {
    "libs/RuntimeDeps/Unity.XR.CoreUtils.dll",
    "libs/RuntimeDeps/Unity.XR.Management.dll",
    "libs/RuntimeDeps/Unity.XR.OpenXR.dll",
    "libs/RuntimeDeps/wizard-dependencies.json",
}
PREFIX_MEMORY_HELPER = "tools/quest-builder/native_admission.py"
MEMORY_HELPER_FIXED = {"path": PREFIX_MEMORY_HELPER, "size": 2053,
    "sha256": "a6459ceed7c9cad382778ba1cfa78b3a44ef41af4f04d0b257c7850fd78d78f0"}
# Exact reviewed host-observer repair: available commit and scheduling change,
# while original converters, arguments, publication and hashes remain identical.
# Unknown host-policy source edits cannot silently claim this compatibility.
MEMORY_POLICY_PREVIOUS = {"path": "tools/quest-builder/host_resources.py", "size": 19578,
    "sha256": "80b22e442ad27e621a2b153714dfe987d65d594db895729770c51285724429fd"}
MEMORY_POLICY_FIXED = {"path": "tools/quest-builder/host_resources.py", "size": 27519,
    "sha256": "93da440e549b07568dfe5efca34b30133ff63ff3c025b05ac4ff6913303aea09"}
# Capture165405's repair changes host concurrency/debug metadata only after
# preparation. Codec retries accept the same bytes through the same existing
# publication/receipt path, and the progress extension only reports resources.
# Alias exact reviewed rows; an unknown helper edit remains a new producer scope.
MEMORY_POLICY_ADAPTIVE = {"path": "tools/quest-builder/host_resources.py", "size": 26600,
    "sha256": "d1e19a5e85955a085c58eff36db0fbaa694fbe41b8837bd4beacf364e103cd7a"}
MEMORY_HELPER_ADAPTIVE = {"path": PREFIX_MEMORY_HELPER, "size": 10337,
    "sha256": "6276af8830d071d0a119f0d5e6da1803a1166d8110f90c4b67394fee405f2dd4"}
MEMORY_OBSERVER_PROFILES = {
    # The live content-pack observer reads the same bytes through the same
    # archival/hash operations. Only its explicitly owned sidecar is new.
    "tools/quest-builder/native_content_pack.py": (
        {"path": "tools/quest-builder/native_content_pack.py", "size": 16256,
         "sha256": "18bb1c74ec6da61c1c4500b0c94ba3b505762da645de10c338694bbb1c163ddd"},
        {"path": "tools/quest-builder/native_content_pack.py", "size": 9829,
         "sha256": "d619a988a88b9a9ac92440cc91a38a46613a3ef5e9d434ea41ff475fed4e32bd"}),
    "tools/quest-builder/asset_jobs.py": (
        {"path": "tools/quest-builder/asset_jobs.py", "size": 15970,
         "sha256": "d2b87a6ac15f11d027d35d1ad770a61ff3978790a50477ae49e4c662b8d6cfdc"},
        {"path": "tools/quest-builder/asset_jobs.py", "size": 10285,
         "sha256": "4056ba17d909ff4c42b85f4202179a90d346d1b3dfa7c606e9d41885c1d8a725"}),
    "tools/quest-builder/progress.py": (
        {"path": "tools/quest-builder/progress.py", "size": 7555,
         "sha256": "fda1de48b41939e992da19c09a22f67669d247e07f7311f91cc866cc86c6ed7f"},
        {"path": "tools/quest-builder/progress.py", "size": 6314,
         "sha256": "f1baf3f33cb80959891d6d4864dec4b468458660d3c733a9aecb64406e340fcb"}),
}


MEMORY_POLICY_MINIMUM = {'path': 'tools/quest-builder/host_resources.py', 'size': 26933, 'sha256': 'b3f6c466e52edbccb8a0497adf6f57ffd0197e1b5c33a0ff641af6cd6eb3ee12'}

# The original FEX build is still available in Launchpad's librarian archive.
# Only these complete reviewed downloader bytes route to that same pinned file.
# Proton native sources, locks, archive/DLL hashes and payloads are unchanged;
# Box64 includes runtime.py in its native key and receives no acquisition alias.
PROTON_DOWNLOAD_PREVIOUS = {"path": "tools/quest-procedural-runtime/runtime.py", "size": 23737,
    "sha256": "8a78594e78ad8d415e0121871c6474113e1ab78484b41ba193b9f464fd9babbe"}
PROTON_DOWNLOAD_FIXED = {"path": "tools/quest-procedural-runtime/runtime.py", "size": 25710,
    "sha256": "383f865db376d001eef05fddbaf385653cc10af897a6ec34af65849f2974bd9d"}
UPDATE_DOWNLOAD_PREVIOUS = {"path": "tools/quest-builder/update_driver.py", "size": 65025,
    "sha256": "7e50d673bb456c66cc08d939ecf025827811c6b1785c52f7a78365223a1bd0c2"}
UPDATE_DOWNLOAD_FIXED = {"path": "tools/quest-builder/update_driver.py", "size": 66742,
    "sha256": "47268c18c60e326d74f1c1684baf995994b2ec0dddcbffc8fb57e56ecad8916c"}

MEMORY_POLICY_PARALLEL = {'path': 'tools/quest-builder/host_resources.py', 'size': 34185, 'sha256': '024e6993e83d319d76c215c9512fffb2e6c3c7c2e360652b2b8ff3cfcdf5294a'}
NATIVE_BANK_HOST_ROW = {'path': 'tools/quest-builder/native_bank_resume.py', 'size': 13057, 'sha256': '49d77c797c3a412ee7bb6a848efabc40e6d10593a284edb39b7f0dc4165bcadf'}
# Capture191013 corrects only the host checkpoint selector: stream legitimate
# large controls and decline malformed optional provenance. Neither complete
# reviewed source writes prepared assets; unknown selector edits still differ.
NATIVE_BANK_HOST_ROWS = (NATIVE_BANK_HOST_ROW,
    {"path": "tools/quest-builder/native_bank_resume.py", "size": 14554,
     "sha256": "49063c51bb183f37437337b7688ecd05a94de90341267f9fa28eb0dd4793c766"})
UPDATE_DOWNLOAD_PARALLEL_PREVIOUS = UPDATE_DOWNLOAD_FIXED
UPDATE_DOWNLOAD_FIXED = {'path': 'tools/quest-builder/update_driver.py', 'size': 68217, 'sha256': '6a68046fff158d94604c7352fdd7b9171b6575b3b06364337c0269e16553b56b'}
# Reviewed host-only retention branch: preserves the qualified current APK and
# original build/bank receipts. Runtime compilation and asset producers are
# unchanged; unknown driver bodies remain independent source identities.
UPDATE_RETAINED_APK_FIXED = {"path": "tools/quest-builder/update_driver.py", "size": 73081,
    "sha256": "ec085d07994c319ea7c35366be96e3e1c38bf6494920d243d3d8bbe01f1fac3b"}
# Accept the Wizard's cached host metadata without changing any runtime or
# preparation producer. Alias only these complete reviewed reader bytes.
UPDATE_TOOL_HANDOFF_FIXED = {"path": "tools/quest-builder/update_driver.py", "size": 74433,
    "sha256": "7dcb5d94d4d20acab4cb1e6c88a718de8016983742588106bd2f73698b153838"}

def _memory_row(row):
    if row in (MEMORY_POLICY_FIXED, MEMORY_POLICY_ADAPTIVE, MEMORY_POLICY_MINIMUM, MEMORY_POLICY_PARALLEL):
        return MEMORY_POLICY_PREVIOUS
    fixed, previous = MEMORY_OBSERVER_PROFILES.get(row["path"], (None, row))
    return previous if row == fixed else row
# Inventory/release/support validation is complete before preparation begins.
# Its immutable records remain qualified; these helpers do not generate assets.
PREFIX_DELIVERY_HELPERS = {"tools/quest-builder/release.py", "tools/quest-builder/support.py",
                           "tools/quest-builder/README.md", "Quest-Builder.cmd", "Quest-Builder.sh",
                           "QUEST-BUILDER-START.txt", "tools/quest-builder/update_capacity.py",
                           "tools/quest-builder/apk_input_cache.py", "tools/quest-builder/portable_package.py",
                           "tools/quest-builder/native_content_pack.py"}
# The captured Windows TLS failure precedes the first completed native-runtime
# transaction. Its Opus downloader cannot produce any earlier original assets.
# Once that transaction closes, this source again qualifies the native outputs.
PREFIX_NATIVE_DOWNLOAD_HELPERS = {"tools/quest-network/native.py"}
# These generators first run in campaign-compute/campaign-shaders. Before the
# exact seventeen-step frontier none can produce a completed original prefix.
# Keep all shared observers, converters and original CAB readers qualified.
PREFIX_UNCONSUMED_GRAPHICS = {"tools/quest-builder/campaign_compute.py",
    "tools/quest-compute/recovery.py", "tools/quest-compute/references.py",
    "tools/quest-shaders/produce.py"}

# Exact AST alias for bounded identity/reference progress in the Builder. Every
# producer statement and argument remains qualified; no general logger stripping.
# Unknown orchestration edits still differ, including an added earlier consumer.
BUILDER_PROGRESS_AST = {
    False: ("274710930270adb41b33ed225247fbf35c8e06044d32f9ae726ed5b28dac6e73",
            "4b1ff16c22bc3b351d9e714d91df56285c5b005c4b975e2385f65ece7ee890c4"),
    True: ("1a6f7d2798bf6d0106a4765bb24b382fd9cd51a1ab55d8c8ba3fd2885380585e",
           "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f"),
}
# This exact reviewed AST adds import/API boundaries, assembly counters and the
# owned content-pack log path after preparation. Producer arguments and retained
# asset bytes are unchanged. An unknown orchestration edit is still a new scope.
BUILDER_UNITY_OBSERVER_AST = (
    "cd66aec43971e36bdabaebc9b7afd8b1adc74704ef1e7696ecc6445ac358775e",
    "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1",
)
# The reviewed repair seam restores only a damaged, already-recorded file to
# its exact closed byte contract. Unity access waits do not produce assets.
# Preserve completed producers for these exact complete orchestration trees;
# every helper byte is independently qualified by the whole source profile.
BUILDER_TARGETED_REPAIR_AST = {
    False: ("9248a3e63cdef1746f02710b173620538cbcd08ffbb1d59ae9ece83cf33de74d",
            "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
    True: ("dfce486b9394d37eda65e23dde1fee1aa7e1fee68e0d4bc65388b76825094161",
           "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f"),
}

# Finite work plans observe Unity commands; they never generate or replace an
# owned asset. Only these exact complete orchestration trees retain that scope.
BUILDER_UNITY_WORK_AST = {
    False: ("515a62abbab83393b8c9c804d4701b4400d3af44a979dd3773036a38bd921552",
            "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
    True: ("ae48c5ed4bd681a70b83f0ae027f7735d059229f24dc28b626a63d59436e6f7f",
           "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f"),
}

# Exact reviewed minimum-work release: required producer order and decoded asset
# bytes stay the same; atomic ZIP wrappers use faster compression. Existing ZIP
# bytes remain owned by their real receipts, not this compatibility fingerprint.
# The two preceding CLI reconstructions differ only in late update/export code.
BUILDER_MINIMUM_WORK_AST = {
    "1cb377c5b5db046330deb95984f5c9d403e01b6cb9b8f2ff166f5dbcb1d6b0a0": "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1",
    "c8b33abda427e267d71ca88e5ca5c8e9a5ec6d41ecc4b64365c93aedde241009": "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1",
    "ea8ab0377918b31a9c35213974997d8665588412254ba0435e4e0e51c3e0aad8": "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"
}

# The exact complete orchestration trees only change bounded file scheduling,
# Unity importer admission and post-cooking checkpoint ownership. Preparation
# writer calls, arguments and order are unchanged. The two preceding late CLI
# reconstructions retain their existing finite aliases; unknown code differs.
BUILDER_MINIMUM_WORK_AST.update({'b55c8f6d3a22b30ace5a8928f081979397e0c7e0df21096f7714b531a32a64e7': '6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1', '56032530ce8ea516109e65f9920d04f1ee836f21eb692ca923a6ea80c1f984c3': 'cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f', 'afc713a6b91e91c39fe33e6018f6094b685f0efa071e8c47e290886f24e5c408': '6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1', 'b1a5e67ed01db87adc2b00dfb6601675c40244ce699a6a7cafffa1613752a4b2': 'cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f', '054b9fa067826ea9435d5af0dc8f1e233e8f061fa503bc6897e22aef75e3a76c': '6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1', 'c7801ffeb1618b2a95b2b4099afcdbc68c6cf211b046054528d27c0c1b636660': 'cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f'})

# Once every preparation owner has closed, the old loading drawing bytes and
# their receipts remain valid under the exact late Editor transport repair.
# New builds avoid the former no-op newline rewrite; unknown producers differ.
LOADING_DRAWING_PREVIOUS = {"path": "tools/quest-builder/sprites.py", "size": 15922,
    "sha256": "823c7d0b0cc1b32ec8fa90ef9e6bc8222c8ca11ebb169606478979af72b9a910"}
LOADING_DRAWING_FIXED = {"path": "tools/quest-builder/sprites.py", "size": 16375,
    "sha256": "dd958eaa9991f3601a6c08acfb13108ba90c8afe117a6f182c23ffd85fd4abac"}


# Exact producer profiles reviewed against the original movie/audio outputs.
# These are aliases for this repair, never a general exclusion from identity.
# The UI manifest gains physical-byte provenance, so only the committed prefix
# before that producer can migrate across its transport repair.
UI_TRANSPORT_PREVIOUS = {
    "path": "tools/quest-builder/ui_assets.py", "size": 15502,
    "sha256": "3a7deb92b1984d0c15ab2821b1ad6f211673f2d587b4da8548c5c048efd3de24",
}
UI_TRANSPORT_FIXED = {
    "path": "tools/quest-builder/ui_assets.py", "size": 17347,
    "sha256": "7c56bb464fe316aeaeeb7c42f029d7d0a789bf52cfb79b47298bf94c3a6519f6",
}
STARTUP_PROGRESS_PREVIOUS = {
    "tools/quest-builder/startup.py": {
        "path": "tools/quest-builder/startup.py", "size": 17374,
        "sha256": "7b37a9dfe70d39d45c642b17037dad65c2f7a4cefb77ffe1616e72e4fac9484e"},
    "tools/quest-builder/full_sprites.py": {
        "path": "tools/quest-builder/full_sprites.py", "size": 7248,
        "sha256": "fb587ef18e4113cd5484bf9050ab350b2d67b9748502c382d5c53d70365ea8d2"},
    "tools/quest-builder/audio.py": {
        "path": "tools/quest-builder/audio.py", "size": 21531,
        "sha256": "cc8fc63e44010d73f89dfde75f5c5d78ae00d9d5e43d7d131131302fe5177c02"},
}
STARTUP_PROGRESS_FIXED = {
    "tools/quest-builder/startup.py": {
        "path": "tools/quest-builder/startup.py", "size": 19926,
        "sha256": "0203bae66b4156a17f394c78961185d4f0c922afbb7cd524b57d020c3ad18233"},
    "tools/quest-builder/full_sprites.py": {
        "path": "tools/quest-builder/full_sprites.py", "size": 9217,
        "sha256": "28d4822eec0e4684f237004c63a595c9c9fe6bed419e85fdea015e09fa33fc0f"},
    "tools/quest-builder/audio.py": {
        "path": "tools/quest-builder/audio.py", "size": 22646,
        "sha256": "167a595a97ec838bf9d88bdb2378969f9175f7e231043f1d1f6a679b8c443b05"},
}


def _reviewed_startup_rows(rows):
    by_path = {row["path"]: row for row in rows}
    if (STARTUP_PROGRESS_FIXED and all(by_path.get(name) == fixed
                                      for name, fixed in STARTUP_PROGRESS_FIXED.items())):
        rows = [STARTUP_PROGRESS_PREVIOUS.get(row["path"], row) for row in rows]
    return [UI_TRANSPORT_PREVIOUS if row == UI_TRANSPORT_FIXED else row for row in rows]


def _transport_prefix(value, target):
    names = ["base-project", "post-effects", "loading-resources", "startup-movies"]
    if target == "game": names.append("native-sprites")
    names += ["loading-sprite", "startup-audio"]
    steps = value.get("steps")
    if not isinstance(steps, list) or len(steps) > len(names): return False
    for index, step in enumerate(steps):
        if (not isinstance(step, dict) or step.get("name") != names[index]
                or step.get("operation") != ("project-files" if index == 0 else "startup-content")):
            return False
    pending = value.get("pending")
    if pending is None: return True
    next_name = names[len(steps)] if len(steps) < len(names) else "ui-recipes" if target == "game" else "startup-ui"
    return (isinstance(pending, dict) and pending.get("name") == next_name
            and pending.get("operation") == ("project-files" if not steps else "startup-content"))


def _original_game_prefix(value, target):
    """Prove exact order before any mod bank or current-input archive is written."""
    if target not in ("startup", "game"): return False
    phases = [("base-project", "project-files")]
    phases += [(name, "startup-content") for name in (
        "post-effects", "loading-resources", "startup-movies")]
    if target == "game": phases.append(("native-sprites", "startup-content"))
    phases += [(name, "startup-content") for name in ("loading-sprite", "startup-audio")]
    if target == "game": phases.append(("ui-recipes", "startup-content"))
    phases += [(name, "startup-content") for name in ("startup-ui", "startup-blur", "dlc-selection")]
    if target == "game":
        phases += [("file-extras", "startup-content"), ("native-runtime", "native-runtime"), ("bundled-audio", "audio")]
        phases += [(name, "textures") for name in ("native-cubemaps", "ordinary-texture-audit", "native-texture2d")]
        phases += [(name, "graphics") for name in ("campaign-compute", "campaign-shaders")]
    steps = value.get("steps")
    if not isinstance(steps, list) or len(steps) > len(phases): return False
    if any(not isinstance(step, dict) or (step.get("name"), step.get("operation")) != phases[index]
           for index, step in enumerate(steps)): return False
    pending = value.get("pending")
    if pending is None: return True
    next_phase = phases[len(steps)] if len(steps) < len(phases) else ("startup-archive", "mod-banks")
    return isinstance(pending, dict) and (pending.get("name"), pending.get("operation")) == next_phase


def _native_unconsumed(value, target):
    """Admit only the witnessed ordered prefix before native-runtime commits."""
    return (target == "game" and _original_game_prefix(value, target)
            and not any(step["name"] == "native-runtime" for step in value["steps"]))


LOAD = ast.parse('preparation_identity = _local_helper("preparation_identity")').body[0]
REBIND = ast.parse(
    "prior_preparation_key = preparation_identity.rebind_key("
    "output, project, inputs, source, target=args.target, recipe=RECIPE, recovery=recovery_resume)").body[0]
PREPARATION_CALL = ast.parse("prepare_resume.Preparation").body[0].value

MEMORY_LOAD = ast.parse('native_admission = _local_helper("native_admission")').body[0]
MEMORY_PREFLIGHT = ast.parse('''if args.command == "build" and args.target == "game":
    native_admission.require_capacity(host_resources, build_progress, output, target=args.target)''').body[0]
MEMORY_FAILURE = ast.parse("native_admission.persist_failure(exc, output, write_json)").body[0]
MEMORY_LAUNCH = ast.parse("policy = native_admission.require_capacity(host_resources, build_progress, output, target=args.target)").body[0]
MEMORY_ENVIRONMENT = ast.parse("env = native_admission.player_environment(env, target=args.target)").body[0]
MEMORY_PLAYER = ast.parse('''native_admission.run_player(host_resources, build_progress, output, policy, command,
    unity_launcher(tools["editor"], graphics=args.target == "game") + ["-quit", "-projectPath", str(project),
        "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build",
        "-logFile", str(output / "logs" / ("unity-build-" + key[:12] + ".log"))],
    output / "logs" / ("unity-launch-" + key[:12] + ".log"), env=env,
    compiler_log=output / "logs" / ("unity-build-" + key[:12] + ".log"), project=project,
    recover_delivery=lambda: recover_delivery_pending(output, key, provenance))''').body[0]
MEMORY_PLAYER_PREVIOUS = ast.parse('''command(
    unity_launcher(tools["editor"], graphics=args.target == "game") + ["-quit", "-projectPath", str(project),
        "-buildTarget", "Android", "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build",
        "-logFile", str(output / "logs" / ("unity-build-" + key[:12] + ".log"))],
    output / "logs" / ("unity-launch-" + key[:12] + ".log"), env=env)''').body[0]
MEMORY_PREVIOUS_LAUNCH = ast.parse('''policy = host_resources.phase_budget("il2cpp" if args.target in ("startup", "game") else "unity", output)
if not policy["nativeLaunchAllowed"]:
    raise BuildError("Available RAM/commit is insufficient or unknown for the large native game compiler; "
                     "close other programs or provide sufficient Windows pagefile commit headroom and retry. "
                     "Completed stages are retained; --jobs cannot bypass the memory reserve.")''').body


def _without_memory_coordination(tree):
    """Normalize exact host-only seams after prepare; all asset producers stay scoped."""
    same = lambda left, right: ast.dump(left, include_attributes=False) == ast.dump(right, include_attributes=False)
    tree.body = [node for node in tree.body if not same(node, MEMORY_LOAD)]
    for owner in tree.body:
        if not isinstance(owner, ast.FunctionDef): continue
        if owner.name == "main":
            for node in ast.walk(owner):
                if isinstance(node, ast.If):
                    node.body = [child for child in node.body if not same(child, MEMORY_PREFLIGHT)]
                if isinstance(node, ast.ExceptHandler):
                    node.body = [child for child in node.body if not same(child, MEMORY_FAILURE)]
        if owner.name == "build":
            for node in owner.body:
                if not isinstance(node, ast.FunctionDef) or node.name != "compile_player_files": continue
                body, index = [], 0
                while index < len(node.body):
                    if same(node.body[index], MEMORY_LAUNCH) or same(node.body[index], MEMORY_ENVIRONMENT):
                        index += 1
                    elif same(node.body[index], MEMORY_PLAYER):
                        body.append(copy.deepcopy(MEMORY_PLAYER_PREVIOUS)); index += 1
                    elif (index + 1 < len(node.body) and same(node.body[index], MEMORY_PREVIOUS_LAUNCH[0])
                          and same(node.body[index + 1], MEMORY_PREVIOUS_LAUNCH[1])):
                        index += 2
                    else:
                        body.append(node.body[index]); index += 1
                node.body = body
    return tree


# Captures065619/075701 change post-Player delivery/signing and read witnesses.
# These exact reviewed profiles leave all original asset producers unchanged;
# an unknown helper edit or earlier consumer still changes preparation identity.
BUILDER_DELIVERY_AST = {
    False: ("9d022492e3e779c4c9402bee6c0f0a82ff8dfe999658b38b0a7ac49d2ce936c3",
            "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
    True: ("343529c3b7286c90450229fcc601cde00618d462fc763b50eac3bfe3cdf2b931",
           "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f"),
}
# Retain the preceding delivered complete tree as a separate exact alias.
BUILDER_DELIVERY_PREVIOUS_AST = {
    False: ("3ae90de2ff4bc4b319e48330b149fb249cb4658565d6fc29a2bce8d5cac888ec",
            "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
}
LATE_DELIVERY_ROWS = {'tools/quest-builder/campaign_compute.py': ({'path': 'tools/quest-builder/campaign_compute.py',
                                              'size': 23104,
                                              'sha256': 'b7215b2c8e61918816f4050e24c0a78469d4f12ea572aabda1e59d54497b4c86'},
                                             {'path': 'tools/quest-builder/campaign_compute.py',
                                              'size': 22840,
                                              'sha256': 'a9f9201ba8a5c2c205f2c334efe1d57c715244846e3899dba8e83384d4158a97'}),
 'tools/quest-builder/update_driver.py': ({'path': 'tools/quest-builder/update_driver.py',
                                           'size': 28600,
                                           'sha256': '451731d076d9bf08258d0e4d2e4a0f3fa8e2cd306bf01abf72f6fb4f3f9cc6d8'},
                                          {'path': 'tools/quest-builder/update_driver.py',
                                           'size': 27312,
                                           'sha256': 'ff2c17ace30cdeb34d955367418a40f593e4d47d04f3b90ef7fe1ee96141a727'})}
PLAYER_DELIVERY_ROW = {'path': 'tools/quest-builder/player_delivery.py',
 'size': 21980,
 'sha256': 'c5897c245e6da3dabedf7a1089cb4d69ac5c691ce2acaeaa899160a3f07241c7'}
# Capture092443 changes only the final native-bank consumer. The stage/recovery
# producer is unchanged. Keep the exact preceding rows above and alias this
# reviewed consumer; an unknown Compute source edit still changes scope.
COMPUTE_OWNER_DELIVERY_ROW = {
    'path': 'tools/quest-builder/campaign_compute.py', 'size': 24752,
    'sha256': '4c12d3cc7023b2a576e4049e8b62ba7bc9ce8e3b0f46b1d250d247793ba1a4bc'}
# Exact reviewed ZIP/signing and final-gate consumers run after preparation.
# Preserve their preceding identities without excluding unknown future edits.
ZIP32_DELIVERY_PREVIOUS_ROWS = {
    "tools/quest-builder/apk_update.py": (
        {"path": "tools/quest-builder/apk_update.py", "size": 45828,
         "sha256": "b0f6e986b18f91c9e537b6a364987640a52079195e8af3119617dc984af6343d"},
        {"path": "tools/quest-builder/apk_update.py", "size": 42360,
         "sha256": "43bf9fd8387aec8ce8a2206f76499b0a176761092b3720563139bafa6e46e446"}),
    "tools/quest-builder/update_driver.py": (
        {"path": "tools/quest-builder/update_driver.py", "size": 33575,
         "sha256": "53e1d7a1695d2d759a3ec8e99ddd30def3b7990d8e9eb2e857e6a8760a68fcf7"},
        LATE_DELIVERY_ROWS["tools/quest-builder/update_driver.py"][1]),
}
# Exact raw-alignment consumers change only late delivery, not original assets.
ZIP32_DELIVERY_ROWS = {
    "tools/quest-builder/apk_update.py": (
        {"path": "tools/quest-builder/apk_update.py", "size": 55585,
         "sha256": "534f745bf2e863a88157ca2aa19d303ae91404c972d6ccccc172fa618804e197"},
        {"path": "tools/quest-builder/apk_update.py", "size": 42360,
         "sha256": "43bf9fd8387aec8ce8a2206f76499b0a176761092b3720563139bafa6e46e446"}),
    "tools/quest-builder/update_driver.py": (
        {"path": "tools/quest-builder/update_driver.py", "size": 34056,
         "sha256": "f28cf077b37d2503f8bf14cad71e3d44bf352547fe82f7ceb97f85a2b9e9ef47"},
        LATE_DELIVERY_ROWS["tools/quest-builder/update_driver.py"][1]),
}
FINAL_DELIVERY_ROW = {
    "path": "tools/quest-builder/delivery_finalization.py", "size": 9425,
    "sha256": "7dc7d851824933e83cc6da10be67e07b65657a7ad15bedf68004247ac9a484c9"}

# Exact reviewed late update consumers and cached migration orchestration.
# The new art key qualifies existing helper bodies more precisely; it changes
# no produced art when those fully scoped bodies/inputs match. Earlier original
# converters and the complete unknown-code AST remain qualified. These aliases
# retain completed preparation instead of repeating it for an APK-only repair.
BUILDER_MOD_UPDATE_AST = {
    False: ("23e1a9ba55fd16169f6d93ef45e496fcf6a0afed7bb31df69d4b64d4fd13b8cf",
            "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
    True: ("5584e655d668f31ef54c28d17d51b35de8a0da219b33166d1331a9d1c92a226e",
           "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f"),
}
MOD_UPDATE_DELIVERY_ROWS = {
    "tools/quest-builder/update_driver.py": (
        {"path": "tools/quest-builder/update_driver.py", "size": 44935,
         "sha256": "a5a2ae749e8d9dea3e1fc0107ab7f7b789e3f9f7faac5dd6e1d0f0a096cc4d02"},
        ZIP32_DELIVERY_ROWS["tools/quest-builder/update_driver.py"][0]),
    "tools/quest-builder/apk_update.py": (
        {"path": "tools/quest-builder/apk_update.py", "size": 59360,
         "sha256": "276cde06438bdf8976e73838f25817fd719cc786427a94258eea61c7705ad952"},
        ZIP32_DELIVERY_ROWS["tools/quest-builder/apk_update.py"][0]),
}

# Portable export, source selection and recipient signing run after preparation.
# These exact orchestration trees do not change any retained asset writer;
# unknown earlier calls/import consumers still produce another identity.
BUILDER_PORTABLE_AST = {
    False: ("f80bd4b4261a4e30f5671b0fd1e260f0fbc81680a60eef36a5b68493cad586f9",
            "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
    True: ("566b05ba1e939ae229d751f78cd810e53c2c8ef4891b63bbe78e1eafab0cdd30",
           "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f"),
}
PORTABLE_DELIVERY_ROWS = {
    "tools/quest-builder/update_driver.py": (
        {"path": "tools/quest-builder/update_driver.py", "size": 52109,
         "sha256": "fc9e2f733dc1662cf29b9281f40eda4e9e13045caf0a6916fdd351fdf5d64f7f"},
        MOD_UPDATE_DELIVERY_ROWS["tools/quest-builder/update_driver.py"][0]),
}
PORTABLE_LOCATOR_DELIVERY_ROWS = {
    "tools/quest-builder/update_driver.py": (
        {"path": "tools/quest-builder/update_driver.py", "size": 53582,
         "sha256": "23949b52b7b51e2a5047399d66b43b67cb1477bfccd0d5aa98d884f1e36740ea"},
        PORTABLE_DELIVERY_ROWS["tools/quest-builder/update_driver.py"][0]),
}
UPDATE_REQUIREMENTS_DELIVERY_ROWS = {
    # Exact reviewed classification/small compiler snapshots live after the
    # preparation frontier. Retain closed original producers across this repair;
    # an unknown update-driver source still requires its own compatible scope.
    "tools/quest-builder/update_driver.py": (
        {"path": "tools/quest-builder/update_driver.py", "size": 61505,
         "sha256": "ef11f025c9143bb94e54bbdb70eb65b96bdae8e27e01eb7e3cc45de5c38be51e"},
        PORTABLE_LOCATOR_DELIVERY_ROWS["tools/quest-builder/update_driver.py"][0]),
}


def builder_producer_digest(raw, *, original_prefix=False):
    """Qualify complete Builder code, or consumed original-prefix orchestration.

    This avoids a self-referential whole-file hash. No producer function, global
    constant, call order or unrelated constructor argument is omitted. Even an
    unknown change elsewhere in prepare() produces a different identity. The
    original-prefix mode retains its consumed helpers/imports/constants while
    omitting unrelated later Player/CLI functions.
    """
    try:
        tree = ast.parse(raw.decode("utf-8"))
    except (UnicodeError, SyntaxError) as error:
        raise BuildError("Preparation Builder source cannot be parsed.") from error
    # The reviewed late build() addition stages a separately keyed owned figure
    # sidecar, outside every retained original-asset producer. Qualify the ENTIRE
    # function first; an unknown ordering/argument/call/body edit stays a mismatch.
    # Figure recipe and original bundle identity are admitted independently by
    # figure_banks.plan, never by this original Campaign compatibility alias.
    for node in tree.body:
        if (isinstance(node, ast.FunctionDef) and node.name == "build"
                and hashlib.sha256(ast.dump(node, include_attributes=False).encode()).hexdigest()
                    == "e3e70e215d72af2f63164f4e999f7e6a2766b5e4725688b02997bb85a138dd4b"):
            del node.body[1]
    tree = _without_memory_coordination(tree)
    tree.body = [node for node in tree.body
                 if ast.dump(node, include_attributes=False) != ast.dump(LOAD, include_attributes=False)]
    for prepare in tree.body:
        if not isinstance(prepare, ast.FunctionDef) or prepare.name != "prepare":
            continue
        prepare.body = [node for node in prepare.body
                        if ast.dump(node, include_attributes=False) != ast.dump(REBIND, include_attributes=False)]
        for generate in prepare.body:
            if not isinstance(generate, ast.FunctionDef) or generate.name != "generate":
                continue
            for node in generate.body:
                if not isinstance(node, ast.Assign) or not isinstance(node.value, ast.Call):
                    continue
                call = node.value
                if (len(node.targets) != 1 or not isinstance(node.targets[0], ast.Name)
                        or node.targets[0].id != "resume"
                        or ast.dump(call.func, include_attributes=False) != ast.dump(PREPARATION_CALL, include_attributes=False)):
                    continue
                call.keywords = [keyword for keyword in call.keywords
                                 if not (isinstance(keyword.value, ast.Name)
                                         and (keyword.arg, keyword.value.id) in (
                                             ("compatible_input_key", "prior_preparation_key"),
                                             ("current_inputs", "inputs")))]
    if original_prefix:
        # The complete preparation body, import bindings and top-level constants
        # still qualify these producers. CLI inspection, APK update, signing and
        # Player-export functions do not run while producing the retained prefix.
        # An edit to those unrelated functions must not recopy the original game.
        tree.body = [node for node in tree.body if isinstance(node, (ast.Import, ast.ImportFrom, ast.Assign, ast.AnnAssign))
                     or isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
                     and node.name in ("prepare", "_local_helper", "command", "tool_path", "toolchain",
                                       "campaign_shader_cache", "recovery_helper_preflight", "owned_tmp_source_archive")]
        for node in tree.body:
            if isinstance(node, ast.ImportFrom) and node.module == "storage":
                node.names = [name for name in node.names if not (name.name == "persistent_inventory" and name.asname is None)]
    result = hashlib.sha256(ast.dump(tree, include_attributes=False).encode("utf-8")).hexdigest()
    result = BUILDER_MINIMUM_WORK_AST.get(result, result)
    observed, preceding = BUILDER_PORTABLE_AST[bool(original_prefix)]
    if result == observed: result = preceding
    observed, preceding = BUILDER_MOD_UPDATE_AST[bool(original_prefix)]
    if result == observed: result = preceding
    observed, preceding = BUILDER_DELIVERY_AST[bool(original_prefix)]
    if result == observed: result = preceding
    prior = BUILDER_DELIVERY_PREVIOUS_AST.get(bool(original_prefix))
    if prior and result == prior[0]: result = prior[1]
    if not original_prefix and result == BUILDER_UNITY_OBSERVER_AST[0]:
        result = BUILDER_UNITY_OBSERVER_AST[1]
    observed, preceding = BUILDER_TARGETED_REPAIR_AST[bool(original_prefix)]
    if result == observed: result = preceding
    observed, preceding = BUILDER_UNITY_WORK_AST[bool(original_prefix)]
    if result == observed: result = preceding
    observed, preceding = BUILDER_PROGRESS_AST[bool(original_prefix)]
    return preceding if result == observed else result


def _builder_bytes(root, records):
    record = records.get(BUILDER)
    if record is None:
        raise BuildError("Preparation compatibility requires its immutable Builder source record.")
    path = _ordinary_owned(Path(root) / BUILDER)
    if not path.is_file():
        raise BuildError("Previous immutable Builder snapshot is missing; existing preparation was retained.")
    raw = path.read_bytes()
    if len(raw) != record["bytes"] or hashlib.sha256(raw).hexdigest() != record["sha256"]:
        raise BuildError("Preparation Builder snapshot differs from its immutable source manifest.")
    return raw


def _completed_game_preparation(value, target):
    """Accept only the complete audited game schedule, including later owners."""
    if target != "game" or value.get("pending") is not None: return False
    names = [("base-project", "project-files")]
    names += [(name, "startup-content") for name in (
        "post-effects", "loading-resources", "startup-movies", "native-sprites",
        "loading-sprite", "startup-audio", "ui-recipes", "startup-ui", "startup-blur",
        "dlc-selection", "file-extras")]
    names += [("native-runtime", "native-runtime"), ("bundled-audio", "audio")]
    names += [(name, "textures") for name in ("native-cubemaps", "ordinary-texture-audit", "native-texture2d")]
    names += [(name, "graphics") for name in ("campaign-compute", "campaign-shaders")]
    names += [(name, "mod-banks") for name in ("startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks")]
    names += [(name, "preparation-contracts") for name in ("compiler-contracts", "case-paths", "script-orders", "final-settings")]
    steps = value.get("steps")
    return (isinstance(steps, list) and len(steps) == len(names)
            and all(isinstance(step, dict) and (step.get("name"), step.get("operation")) == expected
                    for step, expected in zip(steps, names)))


def _completed_editor_rows(rows):
    """Alias one exact late Editor repair, only after all asset owners close.

    The actual current source snapshot stays qualified. The bounded refresh
    transaction publishes reviewed scripts under their existing latest owners;
    original assets, existing GUIDs and Library remain untouched. Unknown or
    partially different template profiles cannot acquire this compatibility.
    """
    profiles = editor_overlay.REVIEWED
    by_path = {}
    for row in rows:
        by_path.setdefault(row["path"], []).append(row)
    if profiles and any(all(by_path.get(name) == [row] for name, row in source.items())
                        for source in editor_overlay.source_profiles()):
        return [profiles[row["path"]][0] if row["path"] in profiles else
                LOADING_DRAWING_PREVIOUS if row == LOADING_DRAWING_FIXED else row for row in rows]
    return rows


def _scope(inputs, source, recovery, *, original_prefix=False, ui_unconsumed=False, native_unconsumed=False, graphics_unconsumed=False, completed=False):
    records = recovery._records(inputs["mod"]["files"], "size")
    # Before authored banks, only the original-prefix helpers have run. Once
    # all phases close, late archive/package/compiler function bodies have also
    # produced retained bytes; require the complete reviewed Builder AST.
    builder = builder_producer_digest(_builder_bytes(source, records), original_prefix=original_prefix and not completed)
    rows = _reviewed_startup_rows(recovery.preparation_source_rows(inputs["mod"]["files"]))
    if inputs.get("proceduralBackend") == "proton-arm64ec-fex":
        rows = [PROTON_DOWNLOAD_PREVIOUS if row == PROTON_DOWNLOAD_FIXED else row for row in rows]
    if completed:
        rows = _completed_editor_rows(rows)
    # These modules validate/select identity; neither generates game assets.
    # Delivery tools are never read by generate_files or its conversion tools.
    rows = [row for row in rows if row["path"] not in (BUILDER, IDENTITY, RECIPE_IDENTITY, "quest-builder-release.json")
            and not row["path"].startswith(DELIVERY_PREFIXES) and row not in NATIVE_BANK_HOST_ROWS]
    if original_prefix:
        reviewed = []
        for row in rows:
            if row in (UPDATE_DOWNLOAD_FIXED, UPDATE_DOWNLOAD_PARALLEL_PREVIOUS, UPDATE_RETAINED_APK_FIXED, UPDATE_TOOL_HANDOFF_FIXED): row = UPDATE_DOWNLOAD_PREVIOUS
            if row in (PLAYER_DELIVERY_ROW, FINAL_DELIVERY_ROW): continue
            if row == COMPUTE_OWNER_DELIVERY_ROW:
                row = LATE_DELIVERY_ROWS[row['path']][1]
            for aliases in (UPDATE_REQUIREMENTS_DELIVERY_ROWS, PORTABLE_LOCATOR_DELIVERY_ROWS, PORTABLE_DELIVERY_ROWS, MOD_UPDATE_DELIVERY_ROWS, ZIP32_DELIVERY_ROWS, ZIP32_DELIVERY_PREVIOUS_ROWS):
                current, preceding = aliases.get(row["path"], (None, row))
                if row == current: row = preceding
            current, preceding = LATE_DELIVERY_ROWS.get(row["path"], (None, row))
            reviewed.append(preceding if row == current else row)
        rows = reviewed
        rows = [row for row in rows if not row["path"].startswith("src/")
                or row["path"] in PREFIX_MOD_INPUTS]
        # Developer checks and release/install launchers are not read by any
        # producer in this prefix. Keep all conversion tools and template files.
        rows = [row for row in rows if not row["path"].startswith("scripts/")
                or completed and row["path"] == "scripts/generate-environment-meshes.py"]
        if not completed:
            rows = [row for row in rows if not row["path"].startswith(PREFIX_MOD_BANK_ROOTS)
                    and row["path"] not in PREFIX_MOD_BANK_HELPERS]
        rows = [row for row in rows if row["path"] not in PREFIX_DELIVERY_HELPERS
                and row["path"] not in PREFIX_PLAYER_REFERENCES
                and row not in (MEMORY_HELPER_FIXED, MEMORY_HELPER_ADAPTIVE)
                and not row["path"].startswith(PREFIX_PLAYER_ROOTS)]
        rows = [_memory_row(row) for row in rows]
        if ui_unconsumed:
            rows = [row for row in rows if row["path"] not in PREFIX_UNUSED_TOOLS]
        if native_unconsumed:
            rows = [row for row in rows if row["path"] not in PREFIX_NATIVE_DOWNLOAD_HELPERS]
        if graphics_unconsumed:
            rows = [row for row in rows if row["path"] not in PREFIX_UNCONSUMED_GRAPHICS]
    scope = copy.deepcopy({key: value for key, value in inputs.items() if key not in ("inputKey", "mod")})
    scope["mod"] = {"producerFiles": rows, "builderProducerAstSha256": builder}
    if not original_prefix:
        scope["mod"]["modBuild"] = inputs["mod"].get("modBuild")
    return scope


def rebind_key(output, project, inputs, source, *, target, recipe, recovery):
    """Return one fully scoped preceding key; never modify journals or assets."""
    output, project = Path(output), _ordinary_owned(Path(project))
    journal = _ordinary_owned(output / "cache/prepare-resume" / project.name / "journal.json")
    if not journal.exists():
        print("preparation resume: no retained preparation journal; a new owned preparation is required", flush=True)
        return None
    value = recovery._read(journal, max_bytes=MAX_JOURNAL_BYTES)
    previous_key = value.get("inputKey")
    if (value.get("schema") != 1 or value.get("owner") != "Quest preparation substage journal"
            or value.get("project") != project.relative_to(output).as_posix()
            or value.get("target") != target or type(value.get("recipe")) is not int or value.get("recipe") != recipe
            or not isinstance(previous_key, str) or not HEX.fullmatch(previous_key)):
        raise BuildError("Preparation journal identity differs; existing project was retained.")
    if previous_key == inputs["inputKey"]:
        return None
    previous = recovery._manifest(output / "manifests" / (previous_key + ".json"))
    current = recovery._manifest(output / "manifests" / (inputs["inputKey"] + ".json"))
    if current != inputs:
        raise BuildError("Current preparation inputs differ from their immutable manifest.")
    previous_source = _ordinary_owned(output / "inputs/mod" / previous["mod"]["key"])
    old_rows = {name: {"path": name, "size": row["bytes"], "sha256": row["sha256"]}
                for name, row in recovery._records(previous["mod"]["files"], "size").items()}
    new_rows = {name: {"path": name, "size": row["bytes"], "sha256": row["sha256"]}
                for name, row in recovery._records(inputs["mod"]["files"], "size").items()}
    before_ui, after_ui = old_rows.get(UI_TRANSPORT_PREVIOUS["path"]), new_rows.get(UI_TRANSPORT_FIXED["path"])
    ui_unconsumed = _transport_prefix(value, target)
    if before_ui != after_ui:
        # UI recipes are consumed immediately after this prefix. A current UI
        # repair may retry there; it cannot claim any already completed UI work.
        if not ui_unconsumed:
            print("preparation resume: reuse unavailable; the UI producer changed after its completed owner; "
                  "existing project and Unity Library remain owned", flush=True)
            return None
    completed = _completed_game_preparation(value, target)
    original_prefix = _original_game_prefix(value, target) or completed
    native_unconsumed = _native_unconsumed(value, target)
    graphics_unconsumed = (target == "game" and original_prefix
                           and not any(step["name"] == "campaign-compute" for step in value["steps"]))
    before = _scope(previous, previous_source, recovery, original_prefix=original_prefix,
                    ui_unconsumed=ui_unconsumed, native_unconsumed=native_unconsumed, graphics_unconsumed=graphics_unconsumed, completed=completed)
    after = _scope(inputs, source, recovery, original_prefix=original_prefix,
                   ui_unconsumed=ui_unconsumed, native_unconsumed=native_unconsumed, graphics_unconsumed=graphics_unconsumed, completed=completed)
    if value_hash(before) != value_hash(after):
        old = {row["path"]: row for row in before["mod"]["producerFiles"]}
        new = {row["path"]: row for row in after["mod"]["producerFiles"]}
        changed = sorted(name for name in old.keys() | new.keys() if old.get(name) != new.get(name))
        if changed:
            reason = "consumed producer inputs changed: " + ", ".join(changed[:8])
            if len(changed) > 8: reason += " (+" + str(len(changed) - 8) + " more)"
        elif before["mod"]["builderProducerAstSha256"] != after["mod"]["builderProducerAstSha256"]:
            reason = "the qualified preparation orchestration changed"
        else: reason = "game/profile/preparation scope changed"
        print("preparation resume: reuse unavailable; " + reason + "; existing project and Unity Library remain owned", flush=True)
        return None
    print("preparation resume: retaining " + str(len(value["steps"])) + " witnessed conversions across compatible producer inputs; "
          "mutable content and final settings retain the current input key", flush=True)
    return previous_key
