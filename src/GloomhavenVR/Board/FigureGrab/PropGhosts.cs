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
/// <para><b>MULTIPLAYER — LANDED 2026-09-05 with extension record 37.</b> The still-held test in
/// <see cref="Tick"/> carries the remote term (<c>|| NetHeldProps.Owns(prop)</c>) and
/// <see cref="NotifyHeld"/> is called from the receive side (<c>Net.NetProps.ApplyRemoteHeld</c>)
/// on the first frame a peer's hold arrives, from the prop's board pose, before anything moves it —
/// the identical shape and the identical ORDER <c>Net/NetFigures</c> uses against
/// <see cref="FigureGhosts"/>. Nothing else changed: this class still never asks WHO holds the prop,
/// only WHETHER it is held, which is exactly why one term was enough.</para>
/// </summary>
internal static class PropGhosts
{
    /// <summary>Dim cool translucent tint — <c>FigureGhosts.GhostTint</c> itself, not a second
    /// copy of its four numbers (2026-09-05). A prop ghost and a figure ghost standing on
    /// neighbouring hexes must read as the same thing, and that is now true by construction rather
    /// than by anyone remembering to edit both.</summary>
    private static Color GhostTint => FigureGhosts.GhostTint;

    /// <summary>Budget for the two REFUSAL lines below. Small: a refusal repeats on every grab of
    /// every prop for the rest of the session (both causes are permanent within a run), so six
    /// lines name the cause and the first few props without turning a hardware log into a
    /// wall.</summary>
    private static int _refusalLogsLeft = 6;

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
        {
            // no ghost rather than a wall-piercing one — but SAY SO (2026-09 refactor, F-22).
            // Until this line existed the success path printed and both failure paths returned in
            // silence, so "no ghost appeared at the hex" — a report we have had — read in the log
            // exactly like "the prop was never grabbed". Note tier, because a log that cannot
            // distinguish those two is the only evidence a hardware round gets.
            NoteRefusal(prop, "the Overlay shader is missing from the bundle, so "
                + "FigureOverlay.MakeOverlayMaterial returned null (BundleShaders could not find it)");
            return;
        }

        // No preserveOriginal subtree: a prop has no m_Hilight selection ring to keep vanilla.
        GameObject? ghost = FigureOverlay.BuildFrozenGhost(source, homePos, homeRot, homeScale,
                                                          mat, out string ghostReport);
        if (ghost == null)
        {
            Object.Destroy(mat);
            NoteRefusal(prop, "FigureOverlay.BuildFrozenGhost found nothing to clone on "
                + $"'{source.name}' (no surviving MeshRenderer/SkinnedMeshRenderer): {ghostReport}");
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

    /// <summary>The counterpart of the "ghost spawned" line: why one did NOT appear. Budgeted by
    /// <see cref="_refusalLogsLeft"/>.</summary>
    private static void NoteRefusal(CObjectProp prop, string why)
    {
        if (_refusalLogsLeft <= 0)
            return;
        _refusalLogsLeft--;
        // HW-VERIFY: the failing half of the ghost report — it must stay at a tier the DEFAULT log
        // level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", $"[Props] NO ghost for '{prop.InstanceName}' ({prop.ObjectType}) — "
            + $"{why}. The hold itself is unaffected; the hex simply stays empty while the prop is "
            + $"in the hand. ({_refusalLogsLeft} more prop ghost refusal lines this session.)");
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
            // HELD IS HELD, WHOEVER'S HAND IT IS (2026-09-05). NetHeldProps is the receive-side
            // membership record for extension record 37, so a ghost stands on the hex for as long as
            // ANY player is carrying that prop — this class still never asks WHO, only WHETHER.
            if (!(HeldProps.Owns(kv.Key) || NetHeldProps.Owns(kv.Key)) || kv.Value == null)
                Scratch.Add(kv.Key);
        }
        for (int i = 0; i < Scratch.Count; i++)
            Destroy(Scratch[i]);
    }

    /// <summary>
    /// A REMOTE hold ended: take that prop's ghost down now, without waiting for
    /// <see cref="Tick"/> to reconcile it.
    ///
    /// <para><b>WHY THE RECEIVE SIDE CANNOT RELY ON THE RECONCILE.</b> <see cref="Tick"/> is called
    /// from <c>PropGrab.Tick</c>, which is called from <c>FigureGrabDriver</c> BELOW the
    /// <c>[FigureGrab] GrabFigures</c> gate — so a player who has turned figure grabbing off on
    /// their own machine stops reconciling ghosts entirely. Their own ghosts cannot outlive that,
    /// because the same gate has already released every local hold; a REMOTE hold is not theirs to
    /// release and keeps running (it is driven from <c>Net.NetProps.Tick</c>, on the network
    /// driver's frame), so without this the ghost of a peer's released chest would stand on the
    /// board for the rest of the scenario. The 1:1 ruling cuts the same way: what a peer is holding
    /// is not something a local presentation dial gets a vote on.</para>
    ///
    /// <para>Refuses while the LOCAL player holds the same prop — the ghost is then theirs and the
    /// reconcile above owns its lifetime.</para>
    /// </summary>
    internal static void NotifyRemoteReleased(CObjectProp? prop)
    {
        if (prop == null || HeldProps.Owns(prop))
            return;
        Destroy(prop);
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
