namespace GloomhavenVR.Core;

/// <summary>Bounded Debug support snapshot of maintained consumers, not requested
/// configuration or inferred visible GPU work. Called on Unity's main thread.</summary>
internal static class QuestPerformanceEvidence
{
    internal static string[] Snapshot()
    {
        string[] hexes = Board.QuestHexPresentationEvidence.Snapshot;
        var result = new string[6 + hexes.Length];
        result[0] = ScenarioFigureDetailBudget.Diagnostics;
        result[1] = ScenarioFigureMeshBank.Diagnostics;
        result[2] = WorldMaterialBudget.Diagnostics;
        result[3] = ScenarioEnvironmentBudget.Diagnostics;
        result[4] = ScenarioTerrainBudget.Diagnostics;
        result[5] = ScenarioEnvironmentMeshBank.Diagnostics;
        System.Array.Copy(hexes, 0, result, 6, hexes.Length);
        return result;
    }
}
