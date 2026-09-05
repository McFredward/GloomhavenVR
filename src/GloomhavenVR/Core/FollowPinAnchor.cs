using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// <b>WHAT FOLGEN AND FIXIERT MEAN, ONCE, FOR EVERY GRABBABLE WORLD OBJECT THE PLAYER CAN PIN.</b>
///
/// <para>USER RULING (2026-09-05, verbatim): <i>"Beim Kampflog funktioniert das 'Folgen' nicht genau
/// gleich wie es bei dem Controlboard der Fall ist. Wieso nicht? Es soll hier am besten den selben
/// Code nutzen und sich genauso verhalten was fixiert und folgen genau bedeutet."</i></para>
///
/// <para>He is right, and the answer to "wieso nicht" is that the two words had been implemented
/// twice by two different ideas of what following is. The CONTROL BOARD's version — the one he
/// approves of — is a RE-PARENT: FOLGEN hangs the object off the rig-space anchor so the rig
/// transform carries it and no per-frame arithmetic exists at all, and FIXIERT hangs it off a
/// world-static holder whose scale mirrors the rig anchor's, so the object's own localScale keeps
/// its 0.5x-2x meaning. The COMBAT LOG's version was a per-tick recomputation from three persisted
/// offset keys, which meant FOLGEN followed the TABLE rather than the player, FIXIERT still rode the
/// world-grab zoom (the holder scale was re-read every tick), and toggling INTO follow teleported
/// the panel to its config pose — the exact thing the board's code refuses to do in writing.</para>
///
/// <para>This class is the board's mechanism, extracted verbatim, so that both callers get the same
/// three behaviours from the same lines:</para>
/// <list type="number">
/// <item><b>FOLGEN is a re-parent onto the rig anchor</b>, world-pose-preserving. Nothing is
/// re-derived, so nothing can drift, and the object keeps the pose the player last gave it.</item>
/// <item><b>FIXIERT is a re-parent onto a world-static holder</b> whose localScale is the rig
/// anchor's lossy scale <i>frozen at pin time</i>. That freeze is the whole reason the holder
/// exists: a bare world detach would bake the diorama-scale factor into the object's own localScale
/// and break every 0.5x-2x clamp that reads it, and a holder that tracked the live rig would make a
/// pinned object swell with the world-grab zoom (tester report: "world zoom zooms the pinned control
/// board too — that must not happen").</item>
/// <item><b>A pinned object is carried through a TRACKING-ORIGIN CHANGE</b> by its rig-relative
/// pose. A pin is raw world space, but a recentre teleports the rig without moving the world, which
/// would strand the object at the old seat. <see cref="Rig.VRRigDriver.RigPoseVersion"/> bumps on
/// exactly those events (rig (re)build + deliberate recentre) and on nothing else — snap turns and
/// world grab deliberately do NOT bump it — so it is the stable signal to key on.</item>
/// </list>
///
/// <para><b>WHAT THIS CLASS DELIBERATELY DOES NOT OWN.</b> It never writes a SCALE on the payload,
/// never decides where the object should first be placed, never touches a config key and never
/// logs. Those are the three things the two callers genuinely differ on — the board seats itself
/// from the head with a solved board scale, the panel seats itself from persisted table-anchor
/// offsets with a healed-into-view clamp — and folding either of them in here is how one caller's
/// tuning would end up applied to the other. What is shared is the MECHANISM; what stays with the
/// caller is the POLICY.</para>
///
/// <para><b>THE HOLDER SCALE HAS EXACTLY TWO LEGITIMATE WRITERS AND NEITHER IS PER-FRAME.</b>
/// <see cref="Apply"/> writes it at PIN time from the live rig — correct there, because the player
/// is fixing the object in the world at the size they are currently seeing it. The board's
/// <c>PlayTray.TryRestoreCapturedPinFrame</c> writes it across a BOARD SWITCH from the scale the
/// previous holder CARRIED, via <see cref="EnsureHolder"/>, because re-deriving it from the live rig
/// there shrank the board by 3.224x on his hardware (the measurement is in PlayTray.3.Pose.cs). A
/// third writer that re-asserts it every frame has existed once and was the bug; do not add it.</para>
/// </summary>
internal sealed class FollowPinAnchor
{
    private readonly string _holderName;
    private readonly bool _dontDestroyOnLoad;

    /// <summary>World-anchor holder while pinned; null while following. Carries the frozen rig scale.</summary>
    private Transform? _holder;

    // ---- tracking-origin carry state ----------------------------------------------------------
    private int _poseVersion = -1;
    private Vector3 _rigLocalPos;
    private Quaternion _rigLocalRot = Quaternion.identity;
    private bool _rigLocalValid;

    /// <param name="holderName">Scene name of the world-static holder object, so a hierarchy dump
    /// names the owner rather than showing two identically-named strays.</param>
    /// <param name="dontDestroyOnLoad">Whether the holder survives a scene load. TRUE for the
    /// control board, which is a permanent fixture of the session. FALSE for a panel that only
    /// lives inside a scenario and whose surface rebuilds its frame from scratch when the scene
    /// goes — the ONE honest difference between the two callers, and it is a lifetime question,
    /// not a behaviour one: what FOLGEN and FIXIERT mean while the object exists is identical.</param>
    internal FollowPinAnchor(string holderName, bool dontDestroyOnLoad)
    {
        _holderName = holderName;
        _dontDestroyOnLoad = dontDestroyOnLoad;
    }

    /// <summary>The live world-static holder, or null while following. Identity, never a copy —
    /// the board's board-switch capture compares it by reference to tell "the holder is gone,
    /// re-establish it" from "the holder is still here, do not touch its scale".</summary>
    internal Transform? Holder => _holder;

    /// <summary>
    /// The ONE place the holder object is created, so its identity (hideFlags, DontDestroyOnLoad,
    /// world origin, identity rotation) is stated once and cannot drift between its callers. It
    /// deliberately does NOT set the SCALE: that number is the subject of the 2026-08-25
    /// board-switch fix and its callers need DIFFERENT answers — a first pin bakes the LIVE rig
    /// scale (<see cref="Apply"/>), a board switch re-establishes the CAPTURED one. Folding the
    /// scale in here is how the two cases got confused in the first place.
    /// </summary>
    internal Transform EnsureHolder()
    {
        if (_holder == null)
        {
            _holder = new GameObject(_holderName).transform;
            _holder.gameObject.hideFlags = HideFlags.HideAndDontSave;
            if (_dontDestroyOnLoad)
                Object.DontDestroyOnLoad(_holder.gameObject);
        }
        _holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return _holder;
    }

    /// <summary>Drop the holder (teardown, or a toggle back into FOLGEN). Immediate on a teardown
    /// that must not leave the object visible for another frame.</summary>
    internal void DestroyHolder(bool immediate)
    {
        if (_holder == null)
            return;
        if (immediate)
            Object.DestroyImmediate(_holder.gameObject);
        else
            Object.Destroy(_holder.gameObject);
        _holder = null;
    }

    /// <summary>
    /// Put <paramref name="target"/> into the anchor mode <paramref name="follow"/> asks for, and
    /// return true when this call left it PINNED (the caller then announces the write to whatever
    /// freeze sentinel it owns).
    ///
    /// <para>BOTH BRANCHES PRESERVE THE WORLD POSE (<c>worldPositionStays: true</c>) and neither
    /// re-seats anything. Toggling FOLGEN/FIXIERT must never move the object: the board's own
    /// comment says so in as many words, and the combat log's per-tick derivation used to violate
    /// it by teleporting the panel to its config offsets on every toggle into follow. A caller that
    /// wants a first placement does it BEFORE calling this, exactly as
    /// <c>PlayTray.PlaceAtHead</c> does — place in the follow frame, then pin.</para>
    /// </summary>
    /// <param name="target">The transform whose PARENT switches between the two frames.</param>
    /// <param name="follow">The live config value: true = FOLGEN, false = FIXIERT.</param>
    /// <param name="rigAnchor">The rig-space anchor (a transform carrying the diorama scale).
    /// Null while the rig is down: FOLGEN then leaves the object where it is rather than orphaning
    /// it, and FIXIERT falls back to a scale of 1 only if there is no parent to read either.</param>
    internal bool Apply(Transform target, bool follow, Transform? rigAnchor)
    {
        if (target == null)
            return false;
        if (follow)
        {
            if (rigAnchor != null && target.parent != rigAnchor)
                target.SetParent(rigAnchor, worldPositionStays: true);
            // ONLY ONCE THE PAYLOAD IS OUT FROM UNDER IT. Object.Destroy takes the CHILDREN with
            // it, so dropping the holder while the target still hangs off it would destroy the very
            // object we were asked to re-home. That is reachable whenever the rig is down —
            // rigAnchor null, the re-parent above skipped, the holder still the parent — which is a
            // real state for a panel that can be up with the hands away. On the control board the
            // clause is unreachable in practice (every caller runs with a live hands anchor), and it
            // writes no number either way: it only decides whether an object is destroyed.
            if (_holder != null && target.parent != _holder)
                DestroyHolder(immediate: false);
            return false;
        }

        Transform pin = EnsureHolder();
        // THE LIVE RIG SCALE, WHICH IS CORRECT HERE AND ONLY HERE. This is an object being PINNED —
        // the player is fixing it in the world at the size they are currently seeing it, so the
        // frame to bake is the one they are standing in. It is NOT correct for a board SWITCH, where
        // the outgoing board's holder already carries the (possibly very different) rig scale the
        // player pinned at; that path re-establishes the holder from the CAPTURED scale first, which
        // leaves the parent guard below already satisfied so this branch is skipped.
        Transform? scaleRef = target.parent != null ? target.parent : rigAnchor;
        pin.localScale = Vector3.one * (scaleRef != null ? scaleRef.lossyScale.x : 1f);
        if (target.parent != pin)
            target.SetParent(pin, worldPositionStays: true);
        return true;
    }

    /// <summary>What <see cref="TickCarry"/> observed, so the caller can log it in its own words
    /// and announce it to its own freeze sentinel. <see cref="Carried"/> false means "nothing moved
    /// this tick", which is the answer on all but a handful of frames per session.</summary>
    internal readonly struct Carry
    {
        internal Carry(bool carried, Vector3 from, Vector3 to, int fromVersion, int toVersion)
        {
            Carried = carried;
            From = from;
            To = to;
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }

        internal bool Carried { get; }
        internal Vector3 From { get; }
        internal Vector3 To { get; }
        internal int FromVersion { get; }
        internal int ToVersion { get; }
    }

    /// <summary>
    /// Per-frame housekeeping for a PINNED object: carry it through a tracking-origin change by its
    /// RIG-RELATIVE pose, i.e. it keeps the same position and orientation relative to the player,
    /// which is what "it stayed where I put it" means to the user. A FOLLOWING object is
    /// structurally immune (it is rig-parented, so it carries itself) and returns a no-op.
    ///
    /// <para><b>NO LIVE HOLDER RESCALE. DO NOT ADD ONE.</b> An earlier cut of this housekeeping
    /// re-asserted the holder scale from the live rig every frame on the theory that a world-grab
    /// zoom would otherwise drift the pinned object. That theory was wrong twice over: the holder
    /// sits at the world ORIGIN with identity rotation and is NOT parented under the rig, so
    /// rescaling the rig cannot move or resize anything underneath it; and the rescale itself was
    /// the bug the tester reported. With the holder FIXED, a pinned object's world size and world
    /// distance are both constant, which is the whole point of pinning it.</para>
    /// </summary>
    internal Carry TickCarry(Transform? target, bool follow)
    {
        int version = VRRigDriver.RigPoseVersion;
        if (target == null || follow || _holder == null)
        {
            // FOLGEN is rig-parented: the problem is structurally impossible there.
            _poseVersion = version;
            return default;
        }

        Transform? rig = VRRigDriver.RigRoot;
        Carry report = default;
        if (rig != null && _poseVersion >= 0 && _poseVersion != version && _rigLocalValid)
        {
            Vector3 pos = rig.TransformPoint(_rigLocalPos);
            Quaternion rot = rig.rotation * _rigLocalRot;
            if (IsFinite(pos))
            {
                Vector3 before = target.position;
                target.SetPositionAndRotation(pos, rot);
                report = new Carry(true, before, pos, _poseVersion, version);
            }
        }
        _poseVersion = version;

        // Re-cache the rig-relative pose every frame the origin is stable, so the NEXT origin
        // change has a fresh, correct offset to carry the object by.
        if (rig != null)
        {
            _rigLocalPos = rig.InverseTransformPoint(target.position);
            _rigLocalRot = Quaternion.Inverse(rig.rotation) * target.rotation;
            _rigLocalValid = IsFinite(_rigLocalPos);
        }
        else
        {
            _rigLocalValid = false;
        }
        return report;
    }

    /// <summary>
    /// Forget the carry bookkeeping entirely: the payload this anchor was tracking is gone and a
    /// stale rig-relative offset would otherwise be applied to whatever replaces it. Called from
    /// the owner's teardown, never from a per-frame path.
    /// </summary>
    internal void ResetCarry()
    {
        _poseVersion = -1;
        _rigLocalValid = false;
    }

    /// <summary>
    /// Re-author the pin against the CURRENT tracking origin after the OWNER deliberately re-seated
    /// the object (a recovery, a recentre reset). Without this the very next
    /// <see cref="TickCarry"/> would see a version mismatch it did not cause and carry the object
    /// away from the seat the owner just chose for it.
    /// </summary>
    internal void ReauthorOrigin() => _poseVersion = VRRigDriver.RigPoseVersion;

    /// <summary>
    /// Re-cache the rig-relative pose from the object's CURRENT world pose. The other half of
    /// <see cref="ReauthorOrigin"/> for an owner that moved the object in the same breath: without
    /// it the next origin change would carry the object by an offset measured BEFORE that
    /// correction and quietly undo it.
    /// </summary>
    internal void RecacheRigLocal(Transform? target)
    {
        Transform? rig = VRRigDriver.RigRoot;
        if (rig == null || target == null)
            return;
        _rigLocalPos = rig.InverseTransformPoint(target.position);
        _rigLocalRot = Quaternion.Inverse(rig.rotation) * target.rotation;
        _rigLocalValid = IsFinite(_rigLocalPos);
    }

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
