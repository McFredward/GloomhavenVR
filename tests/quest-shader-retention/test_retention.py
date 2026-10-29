"""Compile exact production retention bodies; focused failure/ownership controls.

Run with MCS=/path/to/mcs MONO=/path/to/mono python -m unittest discover
-s tests/quest-shader-retention -v. Actual Unity/native-bank proof is separate.
"""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs"
MCS = os.environ.get("MCS") or shutil.which("mcs")
MONO = os.environ.get("MONO") or shutil.which("mono")


def method(text, declaration):
    start = text.index(declaration)
    begin = text.index("{", start)
    depth = 0
    for index in range(begin, len(text)):
        if text[index] == "{": depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0: return text[start:index + 1]
    raise AssertionError("Incomplete production method")


@unittest.skipUnless(MCS and MONO, "MCS and MONO are needed for managed method controls")
class RetentionMethods(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.root = Path(cls.temporary.name)
        text = SOURCE.read_text()
        bodies = "\n".join(method(text, declaration) for declaration in (
            "internal static void ConfigureCampaignShaderRetention()",
            "internal static void AddCampaignShaderRetention(AddressableAssetSettings settings)",
            "internal static AddressableAssetGroup GetOrCreateOwnedGroup("))
        constants = "\n".join(re.findall(r'        internal const string Campaign\w+ = "[^"\n]+";', text))
        cls.production = cls.root / "Production.cs"
        cls.production.write_text("using System;using System.IO;using System.Linq;using System.Collections.Generic;"
            "using UnityEngine;using UnityEditor;using UnityEngine.Rendering;"
            "using UnityEditor.AddressableAssets.Settings;using UnityEditor.AddressableAssets.Settings.GroupSchemas;"
            "namespace GloomhavenVR.Quest.Editor { public static class QuestStartupAddressablesBuild {\n"
            + constants + "\n" + bodies + "\n} }\n")
        cls.executable = cls.root / "Witness.exe"
        result = subprocess.run([MCS, "-langversion:latest", "-out:" + str(cls.executable),
            str(cls.production), str(Path(__file__).with_name("Stubs.cs")),
            str(Path(__file__).with_name("RetentionWitness.cs"))], capture_output=True, text=True)
        if result.returncode: raise AssertionError(result.stdout + result.stderr)

    @classmethod
    def tearDownClass(cls): cls.temporary.cleanup()

    def test_public_ownership_private_roots_original_collection_and_warm_reuse(self):
        self.run_case("success")

    def run_case(self, case):
        with tempfile.TemporaryDirectory(dir=self.root) as directory:
            result = subprocess.run([MONO, str(self.executable), case], cwd=directory, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS " + case + " assertions=", result.stdout)

    def test_native_setting_contract_rejects_unknown_or_unwritten_enum(self):
        for case in ("enum-kind", "enum-name", "enum-absent", "enum-unwritten"):
            with self.subTest(case=case): self.run_case(case)

    def test_native_fog_contract_rejects_missing_or_unwritten_modes(self):
        for case in ("fog-enum-kind", "fog-enum-name", "fog-enum-absent", "fog-enum-unwritten",
                     "fog-flag-kind", "fog-flag-absent", "fog-linear-unwritten", "fog-exp-unwritten", "fog-exp2-unwritten"):
            with self.subTest(case=case): self.run_case(case)

    def test_original_shader_and_alias_identity_negative_controls(self):
        for case in ("scope", "api", "guid", "name", "missing-alias", "extra-alias", "pass-type"):
            with self.subTest(case=case): self.run_case(case)

    def test_unowned_or_drifted_roots_are_rejected_without_moving_public_entries(self):
        for case in ("foreign-group-root", "foreign-clone-owner", "private-address", "private-label", "unowned-clone"):
            with self.subTest(case=case): self.run_case(case)


class BuildScope(unittest.TestCase):
    def test_campaign_only_before_native_addressables_build_and_no_warmup(self):
        text = SOURCE.read_text()
        build = method(text, "public static void Build()")
        self.assertIn('if (campaign) AddCampaignShaderRetention(settings);', build)
        self.assertLess(build.index("AddCampaignShaderRetention(settings)"), build.index("AddressableAssetSettings.BuildPlayerContent"))
        self.assertLess(build.index("AddressableAssetSettings.BuildPlayerContent"), build.index("ValidateNativeCampaignShaders(built)"))
        self.assertLess(build.index("ValidateNativeCampaignShaders(built)"), build.index("RepackContent(built)"))
        self.assertIn("if (campaign) ValidateNativeCampaignShaders(built);", build)
        self.assertIn("PlayerBuildOption.DoNotBuildWithPlayer", build)
        self.assertNotIn("WarmUp(", text)
        self.assertIn("EditorUtility.CopySerialized(collection, addressable)", text)

    def test_android_player_setting_is_quest_campaign_guarded(self):
        path = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs"
        self.assertRegex(path.read_text(), r"#if GHVR_QUEST_GAME\s+if \(fullCampaign\) QuestStartupAddressablesBuild.ConfigureCampaignShaderRetention\(\);\s+#endif")


if __name__ == "__main__": unittest.main()
