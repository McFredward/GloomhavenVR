using System;
using HarmonyLib;
using UnityEngine;

// NativeActorPoseAudit resolves the REAL publisher types, never lookalike scripts. These
// boundaries are unrelated mod services/configuration and render-clone decoration only.
public class UnknownBoneWriter : MonoBehaviour { }
public class UnknownStateBoneWriter : StateMachineBehaviour { }
public class CharacterManagerSubclass : CharacterManager { }
public class UnknownDetailProvider : MonoBehaviour, IDetailDisablerProvider { public void StartDisable() { } }
public class NativeEventReceipt : MonoBehaviour
{
    public int Events;
    public void OnNativeReceipt() => Events++;
}
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal const string ModOwnedNamePrefix = "VR", ModOwnedQualifiedPrefix = "GloomhavenVR."; }
    internal static class VRLog
    {
        internal static bool WantsDebug => true;
        internal static int BudgetFailures;
        internal static void Note(string area, string text)
        {
            if (text.StartsWith("Idle animation budget failed", StringComparison.Ordinal)) BudgetFailures++;
            UnityEngine.Debug.Log(area + ": " + text);
        }
        internal static void Info(string area, string text) => UnityEngine.Debug.Log(area + ": " + text);
        internal static void Debug(string area, string text) { }
    }
    internal static class PerfMonitor
    {
        internal readonly struct Sample : IDisposable { public void Dispose() { } }
        internal static Sample Scope(string label) => default;
        internal static void Count(string label, long value) { }
        internal static void Register(string label) { }
    }
    internal static class PerfConfig { internal static float ActorBarPoseCheckInterval = 0.2f; }
    internal static class VRSession
    {
        internal static bool IsRunning = true;
        internal static readonly Harmony Harmony = new("ghvr.nativeActorAudit");
    }
    internal static class ScenarioFigureDetailBudget
    {
        internal static ScenarioFigureMeshBank.Record? OriginalRecordFor(Renderer renderer) => null;
    }
    internal static class ScenarioFigureMeshBank
    {
        internal sealed class Record
        {
            internal Mesh Original = null!;
            internal bool UsesDerivative => false;
        }
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldFigures
    {
        internal static ActorBehaviour? Held;
        internal static bool Owns(ActorBehaviour actor) => Held == actor;
    }
    internal static class NetHeldFigures
    {
        internal static ActorBehaviour? Held;
        internal static bool Owns(ActorBehaviour actor) => Held == actor;
    }
    internal static class ActorPropBody { internal static bool IsHeld(ActorBehaviour actor) => false; }
    internal static class FigureOverlay
    {
        internal static void CopyBlendShapeWeights(SkinnedMeshRenderer source, SkinnedMeshRenderer copy)
        { for (int i = 0; i < source.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i)); }
        internal static void MatchCloneWorldScale(Transform target, Transform parent, Transform source)
            => target.localScale = source.localScale;
    }
}
