using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A translucent GHOST left at a PROP's home board pose the whole time it is held — the prop-side
/// twin of <see cref="FigureGhosts"/> (user, 2026-09-02: "Es soll wie Figuren reagieren — also
/// highlighting beim drüberfahren mit der Hand und wenn man es in der Hand hat einen Geist
/// hinterlassen").
///
/// <para><b>WHY A SECOND STORE.</b> <see cref="FigureGhosts"/> is keyed on
/// <c>ActorBehaviour</c> and reconciles against <see cref="HeldFigures"/> /
/// <see cref="NetHeldFigures"/>. A prop has no <c>ActorBehaviour</c> at all (see
/// <see cref="HeldProps"/> for the CMap.cs:502 guard that decides it), so it cannot be a key in
/// that dictionary. The GHOST ITSELF is not duplicated: this class calls the very same builder
/// the figure ghost uses, <see cref="FigureOverlay.BuildFrozenGhost"/>, which takes a plain
/// <c>GameObject</c> and therefore serves a prop unchanged.</para>
///
/// <para><b>AND WHY IT IS SIMPLER.</b> A figure ghost re-asserts its home pose every Tick because
/// the clone keeps an Animator whose clip could translate it. A prop's visual is static geometry
/// the game places once and never animates per frame, and the builder strips every script, so
/// there is nothing left that could walk the ghost off its cell — the pose is written once, at
/// build time. Tick therefore only reconciles membership.</para>
///
/// <para><b>MULTIPLAYER.</b> Local-only in this build, exactly like the prop hold itself. When the
/// sync lands, the still-held test in <see cref="Tick"/> gains the remote term
/// (<c>|| NetHeldProps.Owns(prop)</c>) and <see cref="NotifyHeld"/> is called from the receive
/// side the first frame a peer's hold arrives — the identical shape
/// <c>Net/NetFigures</c> already uses against <see cref="FigureGhosts"/>. Nothing else changes:
/// this class never asks WHO holds the prop, only WHETHER it is held.</para>
/// </summary>
internal static class PropGhosts
{
    /// <summary>Dim cool translucent tint. MIRRORS <c>FigureGhosts.GhostTint</c> deliberately — a
    /// prop ghost and a figure ghost standing on neighbouring hexes must read as the same thing.
    /// Not merged because that field is private to a file this lane does not own; if the two ever
    /// need to be tuned, tune them together.</summary>
    private static readonly Color GhostTint = new Color(0.45f, 0.62f, 1.0f, 0.30f);

    private static readonly Dictionary<CObjectProp, GameObject> Ghosts = new();
    private static readonly List<CObjectProp> Scratch = new(4);

    internal static int Count => Ghosts.Count;

    /// <summary>
    /// Ensure a frozen ghost exists for <paramref name="prop"/> at its home pose. Called the
    /// instant a hold begins — from <see cref="GrabbableProp.OnGrab"/>, BEFORE the visual is
    /// reparented into the hand, so the captured pose is the board pose. Idempotent.
    /// </summary>
    internal static void NotifyHeld(CObjectProp? prop, GameObject? source,
                                   Vector3 homePos, Quaternion homeRot, Vector3 homeScale)
    {
        if (prop == null || source == null || Ghosts.ContainsKey(prop))
            return;

        Material? mat = FigureOverlay.MakeOverlayMaterial(GhostTint, additive: false); // alpha-blended
        if (mat == null)
            return; // bundle missing the Overlay shader — no ghost rather than a wall-piercing one

        // No preserveOriginal subtree: a prop has no m_Hilight selection ring to keep vanilla.
        GameObject? ghost = FigureOverlay.BuildFrozenGhost(source, homePos, homeRot, homeScale,
                                                          mat, out string ghostReport);
        if (ghost == null)
        {
            Object.Destroy(mat);
            return;
        }
        Ghosts[prop] = ghost;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        // The census rides along for the same reason it does on a figure: "the ghost spawned"
        // and "the player can see a ghost" are different claims, and only the second one is the
        // complaint. It names the clones, their bounds, and any renderer destroyed as VFX.
        VRLog.Note("FigureGrab", $"[Props] ghost spawned at home for '{prop.InstanceName}' "
            + $"({prop.ObjectType}) — {Ghosts.Count} prop ghost(s) active. {ghostReport}");
    }

    /// <summary>
    /// Reconcile every prop ghost against <see cref="HeldProps"/>: destroy any whose prop was
    /// released or whose clone died. Cheap no-op when no ghost exists; call once per frame.
    /// </summary>
    internal static void Tick()
    {
        if (Ghosts.Count == 0)
            return;

        Scratch.Clear();
        foreach (KeyValuePair<CObjectProp, GameObject> kv in Ghosts)
        {
            // The remote term goes here when the sync lands: `|| NetHeldProps.Owns(kv.Key)`.
            if (!HeldProps.Owns(kv.Key) || kv.Value == null)
                Scratch.Add(kv.Key);
        }
        for (int i = 0; i < Scratch.Count; i++)
            Destroy(Scratch[i]);
    }

    /// <summary>Tear down every prop ghost (scenario teardown / module shutdown / feature off).</summary>
    internal static void Clear()
    {
        foreach (GameObject ghost in Ghosts.Values)
        {
            if (ghost != null)
                Object.Destroy(ghost);
        }
        Ghosts.Clear();
    }

    private static void Destroy(CObjectProp prop)
    {
        if (!Ghosts.TryGetValue(prop, out GameObject ghost))
            return;
        if (ghost != null)
            Object.Destroy(ghost); // the shared ghost material dies with its renderers' owner
        Ghosts.Remove(prop);
    }
}
