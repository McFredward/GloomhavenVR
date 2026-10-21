namespace GloomhavenVR.Core;

/// <summary>Bounded Debug support snapshot of maintained consumers, not requested
/// configuration or inferred visible GPU work. Called on Unity's main thread.</summary>
internal static class QuestPerformanceEvidence
{
    internal static string[] Snapshot()
    {
        string[] hexes = Board.QuestHexPresentationEvidence.Snapshot;
        string[] fire = Cards.QuestCardFireEvidence.Snapshot;
        var result = new string[7 + hexes.Length + fire.Length];
        result[0] = ScenarioFigureDetailBudget.Diagnostics;
        result[1] = ScenarioFigureMeshBank.Diagnostics;
        result[2] = WorldMaterialBudget.Diagnostics;
        result[3] = ScenarioEnvironmentBudget.Diagnostics;
        result[4] = ScenarioTerrainBudget.Diagnostics;
        result[5] = ScenarioEnvironmentMeshBank.Diagnostics;
        result[6] = QuestCombatAudioBank.Diagnostics;
        System.Array.Copy(hexes, 0, result, 7, hexes.Length);
        System.Array.Copy(fire, 0, result, 7 + hexes.Length, fire.Length);
        return result;
    }
}
