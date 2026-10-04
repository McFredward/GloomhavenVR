using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>Move original ghost construction off the first local/remote pickup. Preparation is
/// explicitly driven during scenario loading; parked visuals have no native script, Animator,
/// collider, cloth, particle or camera. Acquisition validates identities and binds today's pose.
/// New/replaced actors retain the existing immediate construction fallback.</summary>
internal static class FigureInteractionPreparation
{
    private sealed class Entry
    {
        internal Entry(GameObject source, GameObject? ring, GameObject ghost, FigureVisualMirror mirror, string report)
        { Source = source; Ring = ring; Ghost = ghost; Mirror = mirror; Report = report; }
        internal readonly GameObject Source, Ghost;
        internal readonly GameObject? Ring;
        internal readonly FigureVisualMirror Mirror;
        internal readonly string Report;
        internal bool InUse;
    }

    private static readonly Dictionary<ActorBehaviour, Entry> Entries = new();
    private static ActorBehaviour[] _pending = Array.Empty<ActorBehaviour>();
    private static Transform? _parking;
    private static int _completed, _failures, _hits, _misses, _invalidations;
    private static bool _begun;
    private static int _missReports;
    internal static int CompletedCount => _completed + StatPanelSurface.InteractionPreparationCompleted;
    internal static int TotalCount => _pending.Length + StatPanelSurface.InteractionPreparationTotal;
    internal static bool IsReady => !_begun || (_completed == _pending.Length && StatPanelSurface.InteractionPreparationReady);
    internal static int PreparedCount => Entries.Count;

    /// <summary>Call after native scenario actors/UI have been established, while the loader is
    /// still presented. One snapshot, never a per-frame scene inventory or a synthetic Show.</summary>
    internal static void Begin()
    {
        Reset();
        _begun = true;
        _pending = Object.FindObjectsOfType<ActorBehaviour>(includeInactive: true);
        StatPanelSurface.BeginInteractionPreparation(_pending);
    }

    /// <summary>At most one complete native ghost OR one stat-art cache resource per tick.
    /// The existing visual clone is atomic; its loading cost is intentionally not a frame-time
    /// guarantee. A failed actor counts as complete and keeps the immediate pickup fallback.</summary>
    internal static void Tick()
    {
        if (!_begun || IsReady) return;
        if (_completed < _pending.Length)
        {
            ActorBehaviour actor = _pending[_completed++];
            GameObject? source = FigureGhosts.GhostSource(actor);
            if (actor == null || actor.Actor == null || source == null) return;
            // Loading is presentation work, never an input lock. A first pickup can have
            // already created its live ghost while this actor was still in the pending queue.
            if (HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor)
                || (Entries.TryGetValue(actor, out Entry active) && active.InUse)) return;
            using var timing = VRLog.WantsDebug ? PerfMonitor.Scope("FigurePreparation.Ghost") : default;
            Material? material = null;
            GameObject? ghost = null;
            try
            {
                material = FigureOverlay.MakeOverlayMaterial(FigureGhosts.GhostTint, additive: false);
                if (material == null) { Failure(actor, "overlay material unavailable"); return; }
                ghost = FigureOverlay.BuildFrozenGhost(source, source.transform.position, source.transform.rotation,
                    source.transform.lossyScale, material, out string report,
                    actor.m_Hilight != null ? actor.m_Hilight.transform : null, activate: false);
                if (ghost == null) { Failure(actor, report); return; }
                Remember(actor, source, ghost, report, inUse: false);
                material = null; // OverlayMaterialOwner now owns its lifetime.
            }
            catch (Exception ex)
            {
                if (ghost != null) DestroyPrepared(ghost);
                Failure(actor, ex.GetType().Name + ": " + ex.Message);
            }
            finally { if (material != null) Object.Destroy(material); }
        }
        else StatPanelSurface.TickInteractionPreparation();
    }

    private static void Failure(ActorBehaviour actor, string reason)
    {
        _failures++;
        if (_failures <= 3)
            VRLog.Warn("FigureGrab", $"Figure interaction preparation skipped '{actor.name}' ({reason}); immediate original pickup remains available (report {_failures}/3).");
    }

    internal static bool TryAcquire(ActorBehaviour actor, GameObject source, Vector3 position,
        Quaternion rotation, out GameObject? ghost, out string report)
    {
        ghost = null; report = string.Empty;
        if (!Entries.TryGetValue(actor, out Entry entry) || entry.InUse || entry.Ghost == null)
        { _misses++; ReportMiss(actor, entry == null ? "not-prepared" : entry.InUse ? "already-acquired" : "destroyed-ghost"); return false; }
        using var timing = VRLog.WantsDebug ? PerfMonitor.Scope("FigurePreparation.Acquire") : default;
        GameObject? ring = actor.m_Hilight != null ? actor.m_Hilight : null;
        if (entry.Source != source || entry.Ring != ring || entry.Mirror == null
            || !entry.Mirror.MatchesPreparedSource(source))
        {
            _invalidations++; _misses++;
            ReportMiss(actor, entry.Source != source ? "source-root" : entry.Ring != ring ? "selection-ring"
                : entry.Mirror == null ? "destroyed-mirror" : entry.Mirror.PreparedMismatchReason);
            Retire(entry); Entries.Remove(actor);
            return false;
        }
        // Inactive throughout validation/pose binding. No preparation-time sleep/flying pose
        // or previous held root pose can leak into the first rendered pickup frame.
        entry.Ghost.SetActive(false);
        entry.Ghost.transform.SetParent(null, worldPositionStays: false);
        entry.Ghost.transform.SetPositionAndRotation(position, rotation);
        entry.Ghost.transform.localScale = source.transform.lossyScale;
        entry.Mirror.RefreshPreparedPose(source);
        entry.InUse = true; _hits++;
        ghost = entry.Ghost;
        report = $"PREPARED ghost reused (hits {_hits}, misses {_misses}, invalidations {_invalidations}); "
            + "current original bones/blend shapes/masks bound before activation. Construction receipt: " + entry.Report;
        ghost.SetActive(true);
        return true;
    }

    private static void ReportMiss(ActorBehaviour actor, string reason)
    {
        // A cache miss is not an error: late summons and valid native visual changes keep
        // their immediate fallback. Only Debug allocates a report, bounded per load.
        if (!VRLog.WantsDebug || _missReports >= 8) return;
        _missReports++;
        VRLog.Debug("FigureGrab", $"PREPARED GHOST MISS actor='{actor.name}' reason={reason} (report {_missReports}/8); immediate current-original construction retained.");
    }

    internal static void Remember(ActorBehaviour actor, GameObject source, GameObject ghost,
        string report, bool inUse = true)
    {
        FigureVisualMirror? mirror = ghost.GetComponent<FigureVisualMirror>();
        if (mirror == null) return;
        if (Entries.TryGetValue(actor, out Entry previous) && previous.Ghost != ghost) Retire(previous);
        mirror.SealPreparedSource(source);
        var entry = new Entry(source, actor.m_Hilight, ghost, mirror, report) { InUse = inUse };
        Entries[actor] = entry;
        if (!inUse) Park(ghost);
    }

    internal static bool Return(ActorBehaviour actor, GameObject ghost)
    {
        if (!Entries.TryGetValue(actor, out Entry entry) || entry.Ghost != ghost || ghost == null) return false;
        Park(ghost); entry.InUse = false;
        return true;
    }

    private static void Park(GameObject ghost)
    {
        ghost.SetActive(false);
        if (_parking == null)
        {
            var holder = new GameObject("GloomhavenVR.FigurePreparation");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            _parking = holder.transform;
        }
        ghost.transform.SetParent(_parking, worldPositionStays: false);
    }

    private static void Retire(Entry entry)
    {
        if (entry.Ghost == null) return;
        DestroyPrepared(entry.Ghost);
    }

    private static void DestroyPrepared(GameObject ghost)
    {
        ghost.SetActive(false);
        foreach (OverlayMaterialOwner owner in ghost.GetComponentsInChildren<OverlayMaterialOwner>(true))
            owner.Release();
        Object.Destroy(ghost);
    }

    internal static void Reset()
    {
        foreach (Entry entry in Entries.Values) Retire(entry);
        Entries.Clear();
        if (_parking != null) Object.Destroy(_parking.gameObject);
        _parking = null; _pending = Array.Empty<ActorBehaviour>();
        _completed = _failures = _hits = _misses = _invalidations = _missReports = 0;
        _begun = false;
        StatPanelSurface.ResetInteractionPreparation();
    }

    /// <summary>Stop loading work at a fail-open timeout without retiring any live hold or
    /// reusable parked visual. Full Reset is reserved for scene/load/VR teardown.</summary>
    internal static void CancelPreparation()
    {
        _pending = Array.Empty<ActorBehaviour>(); _completed = 0; _begun = false;
        StatPanelSurface.ResetInteractionPreparation();
    }
}
