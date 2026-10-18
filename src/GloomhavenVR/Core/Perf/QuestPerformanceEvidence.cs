namespace GloomhavenVR.Core;

/// <summary>Bounded Debug support snapshot of maintained consumers, not requested
/// configuration or inferred visible GPU work. Called on Unity's main thread.</summary>
internal static class QuestPerformanceEvidence
{
    internal static string[] Snapshot() => new[] { ScenarioFigureDetailBudget.Diagnostics,
        ScenarioFigureMeshBank.Diagnostics, WorldMaterialBudget.Diagnostics, ScenarioEnvironmentBudget.Diagnostics };
}
