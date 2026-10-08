"""Player-memory stage/target contract; actual release proof runs under Mono."""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[2] / 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs'

class MemoryBoundary(unittest.TestCase):
    def test_release_runs_inside_content_exclusion_before_actual_player(self):
        source = SOURCE.read_text()
        body = source[source.index('BuildReport report;'):source.index('byte[] profile =')]
        self.assertRegex(body, r'using \(target == "game" \? new QuestCampaignContentBuild[^\n]+\n#endif\s*\{\s*#if GHVR_QUEST_GAME\s*if \(target == "game"\) ReleaseCampaignBuildMemory\(manifest.inputKey\);\s*#endif\s*report = BuildPipeline.BuildPlayer')
        self.assertEqual(body.count('ReleaseCampaignBuildMemory('), 1)
        self.assertIn('QuestCampaignComputeValidation.Validate();', body)

    def test_release_retains_safe_public_api_and_bounded_diagnostics(self):
        source = SOURCE.read_text()
        body = source[source.index('public static void ReleaseCampaignBuildMemory('):source.index('static CampaignMemorySample SampleCampaignMemory(')]
        self.assertRegex(body, r'GC.Collect\(\);\s*GC.WaitForPendingFinalizers\(\);\s*EditorUtility.UnloadUnusedAssetsImmediate\(\);\s*GC.Collect\(\);')
        self.assertIn('samples = new[] { before, after }', body)
        self.assertNotIn('UnloadUnusedAssetsImmediate(true)', body)
        for changing_api in ('DestroyImmediate(', 'NewScene(', 'OpenScene(', 'ClearCache(', 'PurgeCache(', 'FindObjectsOfTypeAll'):
            self.assertNotIn(changing_api, body)
        self.assertNotIn('playerBuildCompleted = true', body)
        self.assertNotIn('headsetPictureVerified = true', body)

if __name__ == '__main__': unittest.main()
