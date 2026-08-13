using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;

namespace GloomhavenVR.Core;

/// <summary>
/// The <c>RESTART TEARDOWN</c> diagnostic: two lines, once per scenario teardown, saying what the
/// mod let go of and what it was still holding that had already been destroyed.
///
/// <para>WHY (user report 2026-08-13: "beim Test ein Fehler aufgetreten nachdem ich die Runde neu
/// gestartet hatte"). Restarting a round unloads the whole scenario scene under a mod that keeps
/// every driver alive across it (all module hosts are DontDestroyOnLoad) while its rig, hands,
/// control board and every adopted panel die with the scene. The mod log showed an orderly
/// teardown and an orderly rebuild — but a stale cached Unity reference is invisible in that log,
/// and it is exactly what produces a teardown NullReferenceException: a destroyed Unity object
/// compares <c>== null</c>, yet the C# field holding it is not null, and <c>?.</c> does NOT do the
/// fake-null check.</para>
///
/// <para>WHAT IT REPORTS. Line 1 is the ledger: every teardown step that ran, in the order it ran
/// (<see cref="Note"/>). Line 2 is the census taken ONE FRAME LATER — <c>Object.Destroy</c> is
/// deferred to end of frame, so a probe taken inside the teardown would still see live objects —
/// naming each well-known cached reference the mod still holds and whether it is alive or already
/// destroyed, plus the count of interaction registrations left pointing at dead colliders. A
/// non-zero "already destroyed" column is the lead to follow after any restart report.</para>
///
/// <para>Cost: two log lines per scenario teardown and one bool test per rig frame. Nothing is
/// allocated until a teardown actually arms the report.</para>
/// </summary>
internal static class TeardownReport
{
    private const string Name = "Core";

    /// <summary>Prefix every line carries, so one grep pulls the whole story out of a run.</summary>
    private const string Tag = "RESTART TEARDOWN";

    /// <summary>Hard cap on the ledger — a runaway caller can never turn this into a flood.</summary>
    private const int MaxNotes = 24;

    private static readonly List<string> Notes = new(MaxNotes);

    /// <summary>
    /// Frames between arming and emitting. TWO, not one: <c>Object.Destroy</c> defers to end of
    /// frame (so a census taken in the teardown frame still sees everything alive), and the other
    /// modules that release in the same teardown are ordinary MonoBehaviours with NO execution-order
    /// relation to the rig driver — one that ticks before it reports its step a frame later. Two
    /// frames is the smallest delay that cannot split one teardown across two reports.
    /// </summary>
    private const int EmitDelayFrames = 2;

    private static int _countdown;
    private static bool _armed;
    private static string _reason = string.Empty;

    /// <summary>
    /// Record one thing the mod released or unsubscribed during a teardown. Ignored (free) while
    /// no teardown is armed, so the ordinary rebuild path pays nothing.
    /// </summary>
    public static void Note(string released)
    {
        if (!_armed || Notes.Count >= MaxNotes || Notes.Contains(released))
            return;
        Notes.Add(released);
    }

    /// <summary>
    /// A scenario teardown has started. The census is deliberately NOT taken here: everything the
    /// teardown destroys is destroyed at end of frame, so a probe now would report the pre-teardown
    /// world. <see cref="Pump"/> emits on the next frame instead.
    /// </summary>
    public static void Arm(string reason)
    {
        _armed = true;
        _countdown = EmitDelayFrames;
        _reason = reason;
        Notes.Clear();
    }

    /// <summary>Called once per rig frame; emits at most one report, <see cref="EmitDelayFrames"/>
    /// frames after <see cref="Arm"/>.</summary>
    public static void Pump()
    {
        if (!_armed || --_countdown > 0)
            return;
        _armed = false;

        string released = Notes.Count == 0
            ? "nothing recorded (no teardown step reported in)"
            : string.Join(", ", Notes.ToArray());
        Notes.Clear();

        VRLog.Info(Name,
            $"{Tag} ({_reason}) — the mod released: {released}. Every module driver itself is "
            + "DontDestroyOnLoad and survives the scene unload; only scene-parented mod objects "
            + "die with it.");

        int pokeStale = 0;
        for (int i = 0; i < VRInteractables.Pokeables.Count; i++)
        {
            if (VRInteractables.Pokeables[i].Collider == null)
                pokeStale++;
        }
        int grabStale = 0;
        for (int i = 0; i < VRInteractables.Grabbables.Count; i++)
        {
            if (VRInteractables.Grabbables[i].Collider == null)
                grabStale++;
        }

        VRLog.Info(Name,
            $"{Tag} census (one frame later, after the destroy wave): rig root "
            + $"{State(Rig.VRRigDriver.RigRoot)}, head camera {State(Rig.VRRigDriver.HeadCamera)}, "
            + $"left hand {State(VRHands.Left)}, right hand {State(VRHands.Right)}; "
            + $"interaction registry {VRInteractables.Pokeables.Count} pokeable(s) "
            + $"({pokeStale} on a DESTROYED collider), {VRInteractables.Grabbables.Count} "
            + $"grabbable(s) ({grabStale} on a DESTROYED collider). A destroyed entry here is a "
            + "stale reference the mod is still holding — the classic source of a teardown "
            + "NullReferenceException.");
    }

    /// <summary>
    /// Alive / already destroyed / never held, told apart properly: a destroyed Unity object is
    /// fake-null (<c>== null</c> is true) while the C# reference is not, which is exactly the
    /// distinction this census exists to make.
    /// </summary>
    private static string State(UnityEngine.Object? reference) =>
        ReferenceEquals(reference, null) ? "released (reference cleared)"
        : reference == null ? "ALREADY DESTROYED (stale reference still held)"
        : "still alive";
}
