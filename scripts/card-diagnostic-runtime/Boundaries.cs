using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

// Native card/game ownership and logging are boundaries; Unity geometry, uGUI, discovery and
// the entire production scheduler, sampling, verdict, shader diff and blackout run unchanged.
public sealed class FullAbilityCard : MonoBehaviour
{
    public FullAbilityCardAction? topActionButton, bottomActionButton;
    public bool Adopted;
    // Native widget bodies are boundaries; Unity executes these actual lifetime callbacks
    // and the exact production Harmony patches observe them without changing their bodies.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init() { }
    private void OnEnable() { }
    private void OnDestroy() { }
}
public sealed class FullAbilityCardAction : MonoBehaviour
{
    public CanvasGroup? canvasGroup;
    public Button? actionButton;
}
public sealed class AbilityCardUI : MonoBehaviour { }
public sealed class ObjectPool : MonoBehaviour { }

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static bool WantsDebug;
        internal static readonly List<string> Lines = new();
        internal static void Info(string scope, string text) => Lines.Add(text);
        internal static void Warn(string scope, string text) => throw new Exception(text);
    }
    internal static class PerfMonitor
    {
        internal static readonly Dictionary<string, int> Calls = new();
        internal static readonly Dictionary<string, double> WorstMs = new();
        internal static IDisposable Scope(string scope) => new Timer(scope);
        private sealed class Timer : IDisposable
        {
            private readonly string _scope;
            private readonly long _start = Stopwatch.GetTimestamp();
            internal Timer(string scope)
            {
                _scope = scope;
                Calls.TryGetValue(scope, out int count); Calls[scope] = count + 1;
            }
            public void Dispose()
            {
                double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
                WorstMs.TryGetValue(_scope, out double worst); WorstMs[_scope] = Math.Max(worst, ms);
            }
        }
    }
}
namespace GloomhavenVR.Cards
{
    internal enum CardBodyKind { Ability, Item, Other }
    internal static class CardArtGuard
    {
        internal static int Samples;
        internal static bool IsAdopted(FullAbilityCard face) { Samples++; return face.Adopted; }
    }
    internal static class BurnLookPolicy { internal static void Reset() { } }
    internal static class CardDiagnosticProbe
    {
        internal static int GeometryWalks, FootprintProbes, InventoryEntries, HeapQueries;
        internal static FullAbilityCard[] FindAllCards() { HeapQueries++; return Resources.FindObjectsOfTypeAll<FullAbilityCard>(); }
        internal static void Clear() { GeometryWalks = 0; FootprintProbes = 0; InventoryEntries = 0; }
    }
    internal static partial class CardHalfTone
    {
        private static int s_fxCorrected = 0, s_beforeHalves = 0, s_beforeNonInteractable = 0;
        private static float s_fxWorstBefore = 0, s_beforeMinAlpha = 0, s_beforeMaxAlpha = 0;
        private static bool s_errorLogged;
        private static readonly Dictionary<int, Material> s_restCopies = new();
        private static readonly HashSet<int> s_mirroredDim = new(), s_cardFxHold = new();
        private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut"), FlowId = Shader.PropertyToID("_Flow"),
            DissolveId = Shader.PropertyToID("_Dissolve"), BurnId = Shader.PropertyToID("_Burn"), PosAndBoundsId = Shader.PropertyToID("_PosAndBounds");
        private static bool IsCardFxMaterial(Material material) => material.HasProperty(GreyOutId) && material.HasProperty(PosAndBoundsId);
        private static float SafeGet(Material material, int id) => material.HasProperty(id) ? material.GetFloat(id) : 0;
        internal static void Tick() => MaybeCensus();
        internal static bool Busy => s_censusFaces != null;
        internal static bool DiagnosticRefs => s_censusFaces != null || s_diffClone != null || s_diffReference != null;
        internal static int Population => CensusRegistry.Count;
        internal static int Completed => s_censuses;
        internal static int Sampled => s_censusIndex;
        internal static void Due() => s_nextCensus = 0;
        internal static void HeartbeatDue() => s_lastCensusLog = float.NegativeInfinity;
        internal static void Clear()
        {
            Reset(); s_censuses = 0; s_normalizedCount = 0; s_errorLogged = false; CardArtGuard.Samples = 0;
        }
    }
    internal static partial class CardFace
    {
        private const float MinOutlineAreaFraction = 0.03f;
        internal static void Offer() { }
        internal static void Maintain(RectTransform root, byte[] mask, bool arrived = true) =>
            FaceBlackout.Maintain(root, CardBodyKind.Ability, mask, 24, 36, new HashSet<int>(), arrived);
        internal static void Restore(RectTransform root) => FaceBlackout.Restore(root);
    }
}
