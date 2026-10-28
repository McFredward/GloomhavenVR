namespace UnityEngine
{
    public static class Time { public static int frameCount = 42; public static double unscaledTimeAsDouble = 123.5; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static int Reads;
        public static Scene GetActiveScene() { Reads++; return new Scene { name = "ProcGen" }; }
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled = true; }
    internal static class VRLog { internal static bool WantsDebug = true; }
    internal static class ScenarioFigureDetailBudget { internal static string Diagnostics => "figure"; }
    internal static class ScenarioFigureMeshBank { internal static string Diagnostics => "figureBank"; }
    internal static class WorldMaterialBudget { internal static string Diagnostics => "worldMaterial"; }
    internal static class ScenarioEnvironmentBudget { internal static string Diagnostics => "environment"; }
    internal static class ScenarioTerrainBudget { internal static string Diagnostics => "terrain"; }
    internal static class ScenarioEnvironmentMeshBank { internal static string Diagnostics => "environmentBank"; }
    internal static class QuestCombatAudioBank { internal static string Diagnostics => "audio"; }
}
namespace GloomhavenVR.Board { internal static class QuestHexPresentationEvidence { internal static string[] Snapshot => new[] { "hex" }; } }
namespace GloomhavenVR.Cards { internal static class QuestCardFireEvidence { internal static string[] Snapshot => new[] { "fire" }; } }
