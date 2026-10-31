namespace GloomhavenVR.Core;

/// <summary>Bounded Debug support snapshot of maintained consumers, not requested
/// configuration or inferred visible GPU work. Called on Unity's main thread.</summary>
internal static class QuestPerformanceEvidence
{
    private static readonly QuestPerformanceHistory History = new();

    // B716's exported state retained frame pacing but Android had evicted every
    // named FRAME/STEPS/SPLIT report after the app was closed. Preserve the SAME
    // existing summaries, including explicit unavailable GPU counters, so the
    // next two-room test can distinguish measured work from an assumed cause.
    internal static void RetainTiming(string summary)
    {
        if (!QuestStandalonePlatform.Enabled || !VRLog.WantsDebug) return;
        History.Record(true, UnityEngine.Time.frameCount, UnityEngine.Time.unscaledTimeAsDouble,
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, summary);
    }

    internal static void Clear() => History.Clear();

    internal static string[] Snapshot()
    {
        string[] hexes = Board.QuestHexPresentationEvidence.Snapshot;
        string[] fire = Cards.QuestCardFireEvidence.Snapshot;
        string[] timings = History.Snapshot();
        var result = new string[7 + hexes.Length + fire.Length + timings.Length];
        result[0] = ScenarioFigureDetailBudget.Diagnostics;
        result[1] = ScenarioFigureMeshBank.Diagnostics;
        result[2] = WorldMaterialBudget.Diagnostics;
        result[3] = ScenarioEnvironmentBudget.Diagnostics;
        result[4] = ScenarioTerrainBudget.Diagnostics;
        result[5] = ScenarioEnvironmentMeshBank.Diagnostics;
        result[6] = QuestCombatAudioBank.Diagnostics;
        System.Array.Copy(hexes, 0, result, 7, hexes.Length);
        System.Array.Copy(fire, 0, result, 7 + hexes.Length, fire.Length);
        System.Array.Copy(timings, 0, result, 7 + hexes.Length + fire.Length, timings.Length);
        return result;
    }
}
