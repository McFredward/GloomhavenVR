using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// TASK #3 (+ multiplayer) — a translucent GHOST silhouette left at a figure's HOME board pose the
/// whole time it is held, so anyone can see where a picked-up mini belongs.
///
/// The ghost is driven off "is this figure held by ANYONE": <see cref="HeldFigures.Owns"/> (this
/// client physically holds it) OR <see cref="NetHeldFigures.Owns"/> (a REMOTE player holds it). So a
/// ghost appears at the home spot on every client, for local and remote pickups alike. Spawn is
/// explicit (at the exact moment a hold begins, before the figure is moved, so the frozen snapshot
/// and home pose are captured at the board) via <see cref="NotifyHeld"/>; despawn is reconciled in
/// <see cref="Tick"/> purely from the two held-sets, so no release path can leak a ghost (the local
/// <c>FigureGrabbable.Restore</c> and the remote <c>NetFigures</c> release both simply drop the actor
/// from its set and the next Tick tears the ghost down).
///
/// Strict no-op offline for remote pickups (<see cref="NetHeldFigures"/> is only ever populated by
/// <c>NetFigures</c> when a modded VR peer reports a held figure); a purely local pickup still ghosts
/// in single-player, which is the desired behaviour.
/// </summary>
internal static class FigureGhosts
{
    // Dim cool translucent tint — low alpha so it reads as a ghost, not a solid figure.
    private static readonly Color GhostTint = new Color(0.45f, 0.62f, 1.0f, 0.30f);

    private static readonly Dictionary<ActorBehaviour, GameObject> _ghosts = new();
    private static readonly List<ActorBehaviour> _scratch = new(4);

    /// <summary>
    /// Ensure a frozen ghost exists for <paramref name="actor"/> at its current (home) pose. Called
    /// the instant a hold begins — LOCALLY from <c>FigureGrabbable.OnGrab</c> (before the mini is
    /// reparented to the hand) and REMOTELY from <c>NetFigures</c> (the first frame a peer's hold
    /// arrives, before the mini is eased toward the remote hand). Idempotent: a second call for an
    /// already-ghosted actor (e.g. both the local grab and a late remote echo) is ignored.
    /// <paramref name="homePos"/>/<paramref name="homeRot"/> are the figure's authoritative board
    /// world pose captured at that moment.
    /// </summary>
    internal static void NotifyHeld(ActorBehaviour actor, Vector3 homePos, Quaternion homeRot)
    {
        if (actor == null || _ghosts.ContainsKey(actor))
            return;

        GameObject? animated = actor.m_AnimatedGameObject != null
            ? actor.m_AnimatedGameObject
            : actor.m_RootGameObject;
        if (animated == null)
            return;

        Material? mat = FigureOverlay.MakeOverlayMaterial(GhostTint, additive: false); // alpha-blended
        if (mat == null)
            return; // bundle missing the Overlay shader — no ghost rather than a wall-piercing one

        Vector3 scale = animated.transform.lossyScale;
        GameObject? ghost = FigureOverlay.BuildFrozenGhost(animated, homePos, homeRot, scale, mat);
        if (ghost == null)
        {
            Object.Destroy(mat);
            return;
        }
        _ghosts[actor] = ghost;
        VRLog.Info("FigureGrab", $"ghost spawned at home for {Describe(actor)} ({_ghosts.Count} active).");
    }

    /// <summary>
    /// Reconcile every ghost against the held-sets: destroy any whose actor was released (no longer in
    /// <see cref="HeldFigures"/> nor <see cref="NetHeldFigures"/>) or was torn down. Cheap no-op when
    /// no ghost exists. Call once per frame.
    /// </summary>
    internal static void Tick()
    {
        if (_ghosts.Count == 0)
            return;

        _scratch.Clear();
        foreach (KeyValuePair<ActorBehaviour, GameObject> kv in _ghosts)
        {
            ActorBehaviour actor = kv.Key;
            bool stillHeld = actor != null && (HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor));
            if (!stillHeld || kv.Value == null)
                _scratch.Add(actor!);
        }
        for (int i = 0; i < _scratch.Count; i++)
            Destroy(_scratch[i]);
    }

    /// <summary>Tear down every ghost (module shutdown / scene teardown).</summary>
    internal static void Clear()
    {
        foreach (GameObject go in _ghosts.Values)
        {
            if (go != null)
                Object.Destroy(go);
        }
        _ghosts.Clear();
    }

    private static void Destroy(ActorBehaviour actor)
    {
        if (_ghosts.TryGetValue(actor, out GameObject go))
        {
            if (go != null)
                Object.Destroy(go); // the shared ghost material dies with its renderers' owner
            _ghosts.Remove(actor);
        }
    }

    private static string Describe(ActorBehaviour actor)
    {
        var ca = actor != null ? actor.Actor : null;
        return ca != null && ca.Class != null ? ca.Class.ID : "?";
    }
}
