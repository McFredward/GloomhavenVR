"""The explicit verified catalog build owns packaging; Player must not repeat it."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs"
ASSIGNMENT = "settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;"


def check_contract(source):
    if source.count(ASSIGNMENT) != 1:
        raise AssertionError("The native Player processor must be explicitly excluded exactly once.")
    loaded = source.index('?? AddressableAssetSettings.Create(folder, "QuestStartup", true, true);')
    configured = source.index(ASSIGNMENT)
    registered = source.index("AddressableAssetSettingsDefaultObject.Settings = settings;")
    dirty = source.index("EditorUtility.SetDirty(settings);")
    saved = source.index("AssetDatabase.SaveAssets();")
    built = source.index("AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);")
    failure = source.index("if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(")
    native_settings = source.index('if (!File.Exists(Path.Combine(built, "settings.json")))')
    packed = source.index("RepackContent(built);")
    if not loaded < configured < registered < dirty < saved < built < failure < native_settings < packed:
        raise AssertionError("Settings, explicit native build and verified packaging lost their ownership/order.")
    if source.count("AddressableAssetSettings.BuildPlayerContent(") != 1:
        raise AssertionError("Exactly one mandatory explicit native catalog build is required.")


class NativeCatalogBuildOwnership(unittest.TestCase):
    def test_load_and_create_both_override_preference_before_explicit_native_build(self):
        check_contract(SOURCE.read_text())

    def test_missing_preference_override_and_dropped_native_build_are_rejected(self):
        source = SOURCE.read_text()
        for changed in (source.replace(ASSIGNMENT, ""),
                        source.replace("PlayerBuildOption.DoNotBuildWithPlayer;", "PlayerBuildOption.PreferencesValue;"),
                        source.replace("AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);", ""),
                        source.replace(ASSIGNMENT, "").replace("RepackContent(built);", "RepackContent(built);\n" + ASSIGNMENT)):
            with self.subTest(changed=changed[-32:]), self.assertRaises((AssertionError, ValueError)):
                check_contract(changed)


if __name__ == "__main__":
    unittest.main()
