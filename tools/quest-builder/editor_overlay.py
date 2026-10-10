"""Exact late Editor repairs inside the completed-preparation transaction.

The preparation selector separately qualifies all original producers and the
complete input scope. This module permits only eleven reviewed existing Editor
scripts to change in an otherwise retained project. It never enumerates assets,
writes Unity Library, or replaces the scripts' existing .meta/GUID files.
"""
from __future__ import annotations

import base64
import hashlib
import os
from pathlib import Path
import uuid

from storage import BuildError, _ordinary_owned

PREFIX = "unity/GloomhavenVR.Quest/"
TARGETS = tuple(PREFIX + "Assets/Quest/Editor/" + name for name in (
    "QuestBuild.cs", "QuestWizardProgress.cs", "QuestStartupAddressablesBuild.cs",
    "QuestCampaignContentBuild.cs", "QuestSpriteGeometryValidation.cs", "QuestCampaignSpriteValidation.cs",
    "QuestOriginalScriptBindings.cs", "QuestCampaignAssetValidation.cs", "QuestCampaignTextureValidation.cs",
    "QuestCampaignComputeValidation.cs", "QuestCampaignShaderValidation.cs"))
MAX_SCRIPT_BYTES = 512 * 1024
# Filled only with exact source inventory rows reviewed for this repair. An
# unknown version remains a consumed producer, never a generally ignored file.
REVIEWED = {'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs',
                                                                 'sha256': '752f2eb86669c07eba54538a463ba36e70f14c8394b5625307fa3eb83c982f3b',
                                                                 'size': 45081},
                                                                {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs',
                                                                 'size': 49322,
                                                                 'sha256': '19d6e2358ba946b00f038c38674b1a0c37766d1a76ecfa9b0e5e29ec5ca84366'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestWizardProgress.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestWizardProgress.cs',
                                                                          'sha256': 'e08930d0941ae02897a57d31a757bc08e7c620f88b49214f3bc35ded4de5b943',
                                                                          'size': 6177},
                                                                         {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestWizardProgress.cs',
                                                                          'size': 13685,
                                                                          'sha256': '391b9285434cf8604bc91dd216afd194429f8d2f56b8f622905a19b46207de27'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs',
                                                                                    'sha256': 'd80c3aabfd110239d462d243dfa6a07689258535890b7707b3ca1cd4298f71e5',
                                                                                    'size': 32913},
                                                                                   {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs',
                                                                                    'size': 35174,
                                                                                    'sha256': '0fbeb5de4eab323fd228be507dfffe3037546dd23e27e9ca0ece0d3ccc6cf761'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignContentBuild.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignContentBuild.cs',
                                                                                'sha256': 'cdc348dba5aa1216a58831e8a29c0bc130cd8a81baaec3f45de407d10bb54ec6',
                                                                                'size': 23565},
                                                                               {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignContentBuild.cs',
                                                                                'size': 24053,
                                                                                'sha256': 'cdc7a23ddf3e75ae157dabd3b2ebae6dbde0832d0b85b750104893e5e302948a'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestSpriteGeometryValidation.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestSpriteGeometryValidation.cs',
                                                                                    'sha256': 'd3faa5882a4ac583d861fc7206252f2413a91784883f3137f1dda7c3b0a386f2',
                                                                                    'size': 8833},
                                                                                   {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestSpriteGeometryValidation.cs',
                                                                                    'size': 10988,
                                                                                    'sha256': 'b55c63d542ae0c4cae14f5723b5ec3d3711b78dc6fd595ea0d7d1d64cd3a89d1'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignSpriteValidation.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignSpriteValidation.cs',
                                                                                    'sha256': '30c07034a1377bf0d194f859b44cc7023a24e28cfed24748812db154c9d130d7',
                                                                                    'size': 5667},
                                                                                   {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignSpriteValidation.cs',
                                                                                    'size': 23947,
                                                                                    'sha256': 'dc6210044d40058190d4d00711b453c9f05804db49371a5396b1f941b4300d97'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestOriginalScriptBindings.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestOriginalScriptBindings.cs',
                                                                                  'sha256': '33fabcde96763c17366cc4324c781f489e37bf4d81c695eb732ff76423206f3d',
                                                                                  'size': 8092},
                                                                                 {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestOriginalScriptBindings.cs',
                                                                                  'size': 13979,
                                                                                  'sha256': '91519d0b53c61762f6b2bf415c1e6e8e11247a67554d3f39546fe5572490e783'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignAssetValidation.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignAssetValidation.cs',
                                                                                   'sha256': '79b67e3eca87139294eaae1efdc32c0cd21478579076ee48227ca61d8ae96cba',
                                                                                   'size': 21453},
                                                                                  {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignAssetValidation.cs',
                                                                                   'size': 24726,
                                                                                   'sha256': '4e3941712f4befa1972f128f97df2446f3f89cddeab4d6db41208736a420ab3e'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignTextureValidation.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignTextureValidation.cs',
                                                                                     'sha256': '68bf1dc7db869dfd0ab90f374a207bcd45cb0b54850b55ffe62bb3bd4a175c8a',
                                                                                     'size': 19887},
                                                                                    {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignTextureValidation.cs',
                                                                                     'size': 21965,
                                                                                     'sha256': 'b0b2bd98ad70a80ade1a2e8ef6b576abedb70f2abca66fd52ba00afe03984db9'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignComputeValidation.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignComputeValidation.cs',
                                                                                     'sha256': '16ffa7cccca43d31f4764b87c84bf5f4fb6dc5b4a9e5d8b82b52e333019a3026',
                                                                                     'size': 12761},
                                                                                    {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignComputeValidation.cs',
                                                                                     'size': 13695,
                                                                                     'sha256': '5a981cce6da6c91da08bd51c49b602bb0e720864e818a010ff0839eae559ed8d'}),
 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderValidation.cs': ({'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderValidation.cs',
                                                                                    'sha256': 'c43e6ee16a80512f1411a8e91bd0a0e6d72fcb25205e65a8fda956bbf8ecfc95',
                                                                                    'size': 45590},
                                                                                   {'path': 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderValidation.cs',
                                                                                    'size': 47297,
                                                                                    'sha256': '3af8a75b0d67b7875597e790b7a412109224514e68930615f38c77193304c865'})}


def _record(relative, raw):
    return {"path": relative, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


# The preceding delivered late-Editor profile remains a valid source of an
# interrupted project. Only this script advances again: the new receipt keeps
# the same serialized pointer transformation and retains its evidence on retry.
PREVIOUS_BINDINGS = {
    "path": PREFIX + "Assets/Quest/Editor/QuestOriginalScriptBindings.cs",
    "size": 9371,
    "sha256": "45a1049a06597e1459f51b2ce7914d3bd8b28bf61f307c25ee6ec04633020b4e",
}

# Preserve the complete preceding task-observer profile for interrupted projects.
# These rows are reviewed individually only after its whole source scope qualifies.
PREVIOUS_TASK_SCRIPTS = {
    PREFIX + "Assets/Quest/Editor/QuestBuild.cs": {
        "path": PREFIX + "Assets/Quest/Editor/QuestBuild.cs", "size": 48710,
        "sha256": "bf39135fb3e5c94f2ed5d3bc50310a8d4bb20874675aa2fa1edb703ad5c5e68c"},
    PREFIX + "Assets/Quest/Editor/QuestWizardProgress.cs": {
        "path": PREFIX + "Assets/Quest/Editor/QuestWizardProgress.cs", "size": 8940,
        "sha256": "7ad781325b0adf7d51f09bdd2e7afb958b86b688e4a8e2f13ee94935fab2fe26"},
    PREFIX + "Assets/Quest/Editor/QuestCampaignSpriteValidation.cs": {
        "path": PREFIX + "Assets/Quest/Editor/QuestCampaignSpriteValidation.cs", "size": 6055,
        "sha256": "0ec2b5bc504c84ba2e82963f0f0359a84484c7a94436f27ef97e74ba64f10138"},
}

# Delivered eed3/663 observed the complete preparation frontier before its
# loading proof rejected the subsequent, recorded case-path manifest migration.
# Retain that exact validator alongside the older complete task-script profile.
# Only its validation/reimport consumer advances; original Sprite production,
# existing .meta identities and all closed preparation outputs stay unchanged.
PREVIOUS_COUNTED_LOADING_SCRIPT = {
    "path": PREFIX + "Assets/Quest/Editor/QuestCampaignSpriteValidation.cs",
    "size": 11730,
    "sha256": "63fc7d89a99d4ff911f17d51360b1f424a3aa162d118ba555b4fe57fd70b3be5",
}


def source_profiles():
    """Whole reviewed source profiles, never independently mixed script rows."""
    current = {name: pair[1] for name, pair in REVIEWED.items()}
    counted = {**current, PREVIOUS_COUNTED_LOADING_SCRIPT["path"]: PREVIOUS_COUNTED_LOADING_SCRIPT}
    preceding = {**current, **PREVIOUS_TASK_SCRIPTS}
    return (current, {**current, PREVIOUS_BINDINGS["path"]: PREVIOUS_BINDINGS},
            preceding, {**preceding, PREVIOUS_BINDINGS["path"]: PREVIOUS_BINDINGS},
            counted, {**counted, PREVIOUS_BINDINGS["path"]: PREVIOUS_BINDINGS})


def _read(path):
    path = _ordinary_owned(Path(path))
    if not path.is_file() or path.stat().st_size > MAX_SCRIPT_BYTES:
        raise BuildError("Editor overlay has a missing/oversized script: " + path.name)
    with path.open("rb") as stream: raw = stream.read(MAX_SCRIPT_BYTES + 1)
    if len(raw) > MAX_SCRIPT_BYTES:
        raise BuildError("Editor overlay exceeded its bounded script read: " + path.name)
    return raw


def changes(previous, inputs):
    """Return an exact reviewed path pair, including during transaction replay."""
    before = {row["path"]: row for row in previous["mod"]["files"]}
    after = {row["path"]: row for row in inputs["mod"]["files"]}
    result = []
    for name in TARGETS:
        left, right = before.get(name), after.get(name)
        if left == right: continue
        profile = REVIEWED.get(name)
        if (not isinstance(profile, tuple) or len(profile) != 2
                or right != profile[1] or left not in (
                    profile[0], PREVIOUS_BINDINGS if name == PREVIOUS_BINDINGS["path"] else profile[0],
                    PREVIOUS_COUNTED_LOADING_SCRIPT if name == PREVIOUS_COUNTED_LOADING_SCRIPT["path"] else profile[0],
                    PREVIOUS_TASK_SCRIPTS.get(name, profile[0]))
                or left is None or right is None
                or set(left) != {"path", "size", "sha256"}
                or set(right) != {"path", "size", "sha256"}
                or left["path"] != name or right["path"] != name
                or not 0 < left["size"] <= MAX_SCRIPT_BYTES
                or not 0 < right["size"] <= MAX_SCRIPT_BYTES):
            raise BuildError("Editor overlay script is outside its exact reviewed source profile: " + name)
        result.append((name, left, right))
    return result


def plan(preparation, previous, inputs, latest):
    result = []
    for name, before, after in changes(previous, inputs):
        relative = name[len(PREFIX):]
        owned = latest.get(relative)
        expected = {**before, "path": relative}
        if (not owned or owned[1] != expected
                or preparation.value["steps"][owned[0]]["name"] != "base-project"):
            raise BuildError("Editor overlay lost its existing latest script owner: " + relative)
        candidates = [path for path, row in preparation.sources if row == after]
        if len(candidates) != 1:
            raise BuildError("Editor overlay has no unique qualified current snapshot source: " + name)
        original, replacement = _read(preparation.project / relative), _read(candidates[0])
        if _record(relative, original) != expected or _record(name, replacement) != after:
            raise BuildError("Editor overlay project/snapshot bytes changed: " + relative)
        result.append({"path": relative, "step": owned[0], "kind": "editor",
                       "before": expected, "after": {**after, "path": relative},
                       "original": base64.b64encode(original).decode("ascii"),
                       "replacement": base64.b64encode(replacement).decode("ascii")})
    return result


def validate(preparation, row, before, after, latest, *, old):
    """Requalify persisted payloads and their live owner before any publication."""
    relative = before["path"][len(PREFIX):]
    try:
        original = base64.b64decode(row.get("original", ""), validate=True)
        replacement = base64.b64decode(row.get("replacement", ""), validate=True)
    except (TypeError, ValueError) as error:
        raise BuildError("Editor overlay transaction contains an invalid script payload.") from error
    expected_before, expected_after = {**before, "path": relative}, {**after, "path": relative}
    if (row.get("kind") != "editor" or row.get("path") != relative
            or type(row.get("step")) is not int
            or not 0 <= row["step"] < len(preparation.value["steps"])
            or preparation.value["steps"][row["step"]]["name"] != "base-project"
            or len(original) > MAX_SCRIPT_BYTES or len(replacement) > MAX_SCRIPT_BYTES
            or row.get("before") != expected_before or row.get("after") != expected_after
            or _record(relative, original) != expected_before
            or _record(relative, replacement) != expected_after
            or latest.get(relative) != (row["step"], expected_before if old else expected_after)):
        raise BuildError("Editor overlay transaction lost its exact source/output contract: " + relative)
    candidates = [path for path, source in preparation.sources if source == after]
    if len(candidates) != 1 or _read(candidates[0]) != replacement:
        raise BuildError("Editor overlay current frozen snapshot changed: " + relative)
    actual = _record(relative, _read(preparation.project / relative))
    if actual not in ((expected_before, expected_after) if old else (expected_after,)):
        raise BuildError("Editor overlay script changed outside its transaction: " + relative)
    return replacement


def publish(path, payload):
    """Publish one already-qualified small script without changing its .meta."""
    path = _ordinary_owned(Path(path))
    temporary = _ordinary_owned(path.with_name(path.name + ".quest-editor-" + uuid.uuid4().hex))
    try:
        with temporary.open("wb") as stream:
            if stream.write(payload) != len(payload):
                raise BuildError("Editor overlay wrote fewer script bytes than requested: " + path.name)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
