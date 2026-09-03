using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// TASK #3 (+ multiplayer) — a translucent GHOST silhouette left at a figure's HOME board pose the
/// whole time it is held, so anyone can see where a picked-up mini belongs. The ghost ANIMATES
/// (task #4: its Animator is kept, playing the idle in place with root motion off) and is VFX-free
/// (task #3: particles/trails/distort-shader renderers are stripped, so no fog/mist at the cell).
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

    /// <summary>A live ghost + its authoritative home pose. The pose is re-asserted every Tick:
    /// the ghost keeps its Animator (task #4 — it plays the idle in place) but all game scripts
    /// that normally pin animated roots are stripped, so this is the cheap insurance that no clip
    /// quirk ever walks the ghost off its home cell.</summary>
    private sealed class Ghost
    {
        public Ghost(GameObject go, Vector3 pos, Quaternion rot) { Go = go; Pos = pos; Rot = rot; }
        public readonly GameObject Go;
        public readonly Vector3 Pos;
        public readonly Quaternion Rot;
    }

    private static readonly Dictionary<ActorBehaviour, Ghost> _ghosts = new();
    private static readonly List<ActorBehaviour> _scratch = new(4);

    /// <summary>
    /// The subtree a ghost is cloned from, and the transform its home pose is read off. ONE
    /// resolver for both call sites (local grab in <c>FigureGrabbable.OnGrab</c>, remote grab in
    /// <c>NetFigures</c>) so the clone and the pose can never come from different objects.
    ///
    /// <para><b>THE ACTOR ROOT, NOT <c>m_AnimatedGameObject</c> (ModBuild 335).</b> User,
    /// 2026-09-02: "wenn ich ihn in die Hand nehme hinterlaesst er auch keine Geistervariante an
    /// der originalen Stelle. Das sollte bei ALLEN Figuren ausnahmslos der Fall sein [...] Bei den
    /// kleinen Drachen funktioniert es ohne Probleme - es ist nur dieser Boss-Drache."</para>
    ///
    /// <para>The game sets <c>m_AnimatedGameObject = MF.GetGameObjectAnimator(root).gameObject</c>,
    /// and that method (decompiled MF.cs:135-146) returns <b>the first Animator in the subtree that
    /// owns a runtimeAnimatorController</b>, in <c>GetComponentsInChildren</c> order. It is not
    /// defined to be the character's own Animator — it is whichever one the depth-first walk
    /// reaches first. On most figures those are the same object, which is why the small drakes were
    /// always fine. The boss is the one figure whose subtree provably carries FOREIGN animated
    /// content: its own reach census names a <c>WP_Scoundrel_Dart</c> and two <c>WP_Dummy</c>
    /// objects hanging off it. Clone the wrong Animator's subtree and you get a real ghost, of a
    /// dart.</para>
    ///
    /// <para><see cref="FigureHighlight"/> reached this conclusion first and moved ITSELF to the
    /// actor root in ModBuild 294; this class was left behind on the old field. The root is also
    /// the subtree <c>ActorBars</c> and <c>FigureGrabDriver</c> already call "the figure", so all
    /// four now agree on what a figure is.</para>
    ///
    /// <para>Cloning the root is safe because <c>FigureOverlay.BuildFrozenGhost</c> strips
    /// colliders, rigidbodies, cloth, particle systems and EVERY MonoBehaviour from the clone, and
    /// (since 335) any mod-owned <c>VR*</c> subtree — the highlight overlay hangs off this very
    /// root, and cloning it would ghost our own glow.</para>
    /// </summary>
    internal static GameObject? GhostSource(ActorBehaviour actor)
    {
        if (actor == null)
            return null;
        return actor.m_RootGameObject != null ? actor.m_RootGameObject : actor.m_AnimatedGameObject;
    }

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

        GameObject? animated = GhostSource(actor);
        if (animated == null)
            return;

        Material? mat = FigureOverlay.MakeOverlayMaterial(GhostTint, additive: false); // alpha-blended
        if (mat == null)
            return; // bundle missing the Overlay shader — no ghost rather than a wall-piercing one

        Vector3 scale = animated.transform.lossyScale;
        // Task #2: hand the live selection ring (m_Hilight) through so the ghost's copy of it keeps
        // the game's ORIGINAL ring materials — the ring at the home cell looks exactly vanilla
        // (the live ring under the in-hand figure is suppressed by FigureRingSuppressor).
        GameObject? ghost = FigureOverlay.BuildFrozenGhost(animated, homePos, homeRot, scale, mat,
            out string report,
            actor.m_Hilight != null ? actor.m_Hilight.transform : null);
        string who = Describe(actor);
        if (ghost == null)
        {
            Object.Destroy(mat);
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a
            // tier the DEFAULT log level prints. scripts/check-hw-verify.py enforces it.
            VRLog.Note("FigureGrab", $"NO ghost for {who} — {report}");
            return;
        }
        _ghosts[actor] = new Ghost(ghost, homePos, homeRot);

        // THE LINE MEASURES THE GHOST, NOT THE DECISION TO BUILD ONE (ModBuild 336). The user's
        // report is "hinterlaesst er keinen Geist" and the ModBuild 335 log answered "ghost spawned
        // at home for ElderDrakeID (1 active)" — a true sentence that cannot tell a ghost of a
        // dragon from a ghost of a dart hanging off it, nor either of those from a ghost drawn on a
        // layer the head camera does not render. See FigureOverlay.MeasureClones.
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"ghost spawned at home for {who} ({_ghosts.Count} active) — {report}");
        OverlayVisibilityProbe.Attach(ghost, $"{who}/ghost", $"GHOST of {who}");
    }

    /// <summary>
    /// Reconcile every ghost against the held-sets: destroy any whose actor was released (no longer in
    /// <see cref="HeldFigures"/> nor <see cref="NetHeldFigures"/>) or was torn down. Cheap no-op when
    /// no ghost exists. Call once per frame.
    /// </summary>
    /// <summary>The live ghost object standing at <paramref name="actor"/>'s home, or null. Read
    /// by <c>ActorPropBody.LogHoldPicture</c> to prove the ghost is a CLONE (its own instance)
    /// and not the held leaf wearing the ghost material.</summary>
    internal static GameObject? GhostFor(ActorBehaviour? actor)
    {
        if (actor == null || !_ghosts.TryGetValue(actor, out Ghost ghost))
            return null;
        return ghost.Go != null ? ghost.Go : null;
    }

    internal static void Tick()
    {
        if (_ghosts.Count == 0)
            return;

        _scratch.Clear();
        foreach (KeyValuePair<ActorBehaviour, Ghost> kv in _ghosts)
        {
            ActorBehaviour actor = kv.Key;
            bool stillHeld = actor != null && (HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor));
            if (!stillHeld || kv.Value.Go == null)
            {
                _scratch.Add(actor!);
                continue;
            }
            // Task #4 — the ghost animates (Animator kept, root motion off); re-assert the home
            // pose each frame so no clip can ever translate/rotate the ghost off its cell.
            kv.Value.Go.transform.SetPositionAndRotation(kv.Value.Pos, kv.Value.Rot);
        }
        for (int i = 0; i < _scratch.Count; i++)
            Destroy(_scratch[i]);
    }

    /// <summary>Tear down every ghost (module shutdown / scene teardown).</summary>
    internal static void Clear()
    {
        foreach (Ghost ghost in _ghosts.Values)
        {
            if (ghost.Go != null)
                Object.Destroy(ghost.Go);
        }
        _ghosts.Clear();
        OverlayVisibilityProbe.Reset(); // a new scenario re-reports its first hover of every figure
    }

    private static void Destroy(ActorBehaviour actor)
    {
        if (_ghosts.TryGetValue(actor, out Ghost ghost))
        {
            if (ghost.Go != null)
                Object.Destroy(ghost.Go); // the shared ghost material dies with its renderers' owner
            _ghosts.Remove(actor);
        }
    }

    private static string Describe(ActorBehaviour actor)
    {
        var ca = actor != null ? actor.Actor : null;
        return ca != null && ca.Class != null ? ca.Class.ID : "?";
    }
}
