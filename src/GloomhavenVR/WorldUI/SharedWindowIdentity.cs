using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;   // UIWindow — the game ships it in this namespace

namespace GloomhavenVR.WorldUI;

/// <summary>
/// WHICH WINDOW CARRIES <see cref="SharedWindowKind.MapStory"/> RIGHT NOW — the story box itself, or
/// the window the story has been COMPOSED INTO.
///
/// <para><b>USER REQUEST (2026-08-23, verbatim):</b> <i>"Ich will aber das man die Story gemeinsam
/// erlebt. Ich weiß dass das flat spiel das anders macht und dort jeder lokal bei sich weiterklicken
/// kann. Ich möchte aber das die gesamte Story, das Fenster und damit auch der Status des Fensters
/// vollständig synchronisiert wird."</b> The CONTENT half of that has shipped since ModBuild 222:
/// <c>Net.RemoteMapStory</c> resolves the page through <c>MapStoryController.dialogBox</c> directly
/// and drives it with the game's own <c>ShowLine</c>. What went inert in ModBuild 236 is the WINDOW
/// half — the blue bar, the synced pose and the release re-face gate — because that build parked the
/// story window's whole content into the LOADOUT screen and <see cref="SharedWindows"/> still keyed
/// the kind off <c>MapStoryController.window</c>, a window that is no longer floated. The pose was
/// therefore neither published nor applied, and the window the player was looking at wore the
/// ordinary brass bar.</para>
///
/// <para><b>THE RULE THIS CLASS IS: THE SHARED IDENTITY FOLLOWS THE COMPOSED HOST FOR AS LONG AS THE
/// COMPOSITE STANDS, AND HANDS BACK WHEN IT STANDS DOWN.</b> While <see cref="StoryComposite"/>'s
/// claim is standing — the story window's own content is inside a mod-owned dock inside a loadout
/// window THE MOD IS FLOATING — that host is the window the story is being told in, so it is the
/// window that must wear the blue bar and whose pose travels on record 21. The instant the claim
/// lapses the host becomes an ordinary private window again and the story box, which is floating
/// again by then or closed, takes the kind back.</para>
///
/// <para><b>THE SCOPE IS THE COMPOSITE AND NOT THE LOADOUT SCREEN'S WHOLE LIFE</b>, and the boundary
/// is deliberate. Past the intro the host is the battle-goal / loadout picker, whose CONTENT is
/// private by an existing user ruling ("Da jeder seine eigene UI sieht, sollen diese UI Element nicht
/// synchronisiert werden") — each player picks his own goals on his own copy. Dragging every peer's
/// private picker around would be a new defect rather than a feature, so the identity is handed back
/// the moment the story stops being told in it.</para>
///
/// <para><b>WHAT IS SYNCED IS THE POSE, NEVER THE HOST'S CONTENT.</b> Record 21's map-story entry
/// carries the page, the page count, the dialog hash and a pose block. Every one of the first three
/// is read off <c>MapStoryController.dialogBox</c> — the STORY box — and none of them is read off the
/// host. Nothing of the loadout screen (card identity, battle goals, the ready toggle) touches the
/// wire, before or after this change. The only thing this class moves is WHICH TRANSFORM the pose is
/// read from and written to.</para>
///
/// <para><b>THE SWAP IS DEFERRED RATHER THAN FORCED, AND THERE ARE EXACTLY TWO REASONS TO DEFER.</b>
/// <list type="number">
/// <item><b>A HAND IS ON ONE OF THE TWO BARS.</b> <c>SharedWindows.AnyGrabbedHere</c> raises the whole
/// extras packet to the rig send rate while a hand is on a shared bar, and
/// <c>GrabbableModal._shared</c> — cached once per tick from <see cref="SharedWindows.IsShared"/> —
/// gates the release re-face. Flipping the kind mid-carry would change both under the player's hand,
/// and on the hand-back edge it would turn a shared window private WHILE HE IS HOLDING IT, so the
/// release would yaw it to face him. That is ModBuild 183's shipped bug. So the swap waits for the
/// hand, with no timeout at all: a window the player is holding is his until he lets go.</item>
/// <item><b>A PEER'S POSE WAS APPLIED IN THE LAST FEW FRAMES.</b> <c>PanelPoseWatch.Classify</c> reads
/// <see cref="SharedWindows.IsShared"/> once per tick as its <c>peerOwned</c> flag, and a pose write
/// it cannot attribute is REFUSED and snapped back (PanelPlacement.cs:571/651-657). The net apply and
/// the pose watch run from different drivers, so their order inside one frame is not ours to fix; if
/// the identity left a window in the same frame a peer pose landed on it, that write would be
/// re-classified <c>Unattributed</c> and undone in front of the player. Waiting two frames after the
/// last apply is enough, because the watch re-baselines on every sanctioned write, so by then there is
/// nothing left for it to call a move. This one IS capped
/// (<see cref="MaxDeferSeconds"/>) — a peer who drags for a minute must not hold the identity
/// hostage.</item>
/// </list></para>
///
/// <para><b>COST, BECAUSE <see cref="SharedWindows.KindOf"/> IS ASKED PER FLOATED WINDOW PER
/// FRAME.</b> The read is ONE static field and one <c>ReferenceEquals</c>. No hierarchy walk, no
/// <c>GetComponentInParent</c> ([[containment-is-not-identity]]), no singleton lookup: the answer is
/// computed once per <c>ModalFallback.Tick</c> by <see cref="TickMapStory"/> and read as a field
/// everywhere else.</para>
///
/// <para><b>THE DEPENDENCY DIRECTION IS THE ONE THIS PROJECT ALREADY HAS: <c>Net → WorldUI</c>.</b>
/// This class never calls into <c>Net</c>. <see cref="NotePoseApplied"/> is a mailbox the net applier
/// writes INTO, which is the same direction <c>RemoteStorySync</c> already takes into
/// <see cref="ModalFallback"/>.</para>
///
/// <para>GREP: <c>STORY WINDOW SHARED IDENTITY SWAPPED</c> — one line per edge, from this side and
/// from the move tracker's side, so the pair proves both halves happened.
/// <c>STORY WINDOW SHARED IDENTITY DEFERRED</c> — a swap that was wanted and waited, with the
/// reason.</para>
/// </summary>
internal static class SharedWindowIdentity
{
    private const string Scope = "WorldUI";

    /// <summary>How long after a peer pose was applied the identity refuses to move. Two frames: the
    /// pose watch re-baselines on the sanctioned write in the frame it sees it, so one clear frame
    /// afterwards is already enough and two is the cheapest number above that.</summary>
    private const int ApplySettleFrames = 2;

    /// <summary>The longest the peer-apply reason may hold a wanted swap back. A hand has no timeout;
    /// this one does, because a peer dragging continuously would otherwise pin the identity to a
    /// window whose composite has already ended.</summary>
    private const float MaxDeferSeconds = 2f;

    private const float DeferNoteSeconds = 5f;

    /// <summary>
    /// The window that carries <see cref="SharedWindowKind.MapStory"/> instead of
    /// <c>MapStoryController.window</c>, or null when the story box carries its own kind.
    ///
    /// <para>A PLAIN FIELD READ. It is deliberately not re-validated here — <see cref="TickMapStory"/>
    /// drops a destroyed or unfloated host once per tick, and a stale reference costs at most one tick
    /// in which <see cref="SharedWindows.TryGetGrab"/> finds no grab, which is a state that path
    /// already handles as "this client has no grabbable window of that kind".</para>
    /// </summary>
    internal static UIWindow? MapStoryHost => _mapStoryHost;

    private static UIWindow? _mapStoryHost;

    /// <summary>How many times the identity has changed hands this session. Printed by both halves of
    /// the swap line so a hardware log can pair them, and by the standing falsifier.</summary>
    internal static int MapStoryGeneration => _generation;

    private static int _generation;

    /// <summary>Why the kind resolves the way it does this tick, in the words the falsifier
    /// prints.</summary>
    internal static string MapStoryWhy => _why;

    private static string _why = "the composite has never stood: the map story box carries its own kind";

    private static int _lastApplyFrame = int.MinValue;
    private static float _deferSince;
    private static string _deferCode = string.Empty;
    private static float _nextDeferNoteAt;

    /// <summary>Drop everything. Called from <c>StoryComposite.Reset</c>, i.e. on module teardown: an
    /// identity that outlived the class that computes it would point the pose sync at a window nobody
    /// is drawing.</summary>
    internal static void Reset()
    {
        _mapStoryHost = null;
        _generation = 0;
        _why = "the composite has been torn down with the module; the map story box carries its own kind";
        _lastApplyFrame = int.MinValue;
        _deferSince = 0f;
        _deferCode = string.Empty;
        _nextDeferNoteAt = 0f;
    }

    /// <summary>
    /// A peer's pose has just been written to the shared window of <paramref name="kind"/>. Called by
    /// <c>Net.RemoteMapStory.ResolvePose</c> immediately after <c>GrabbableModal.PlaceFrameAt</c>.
    ///
    /// <para>It exists for ONE reason, stated on the class: the identity must not leave a window in
    /// the same frame a peer pose landed on it, or <c>PanelPoseWatch</c> re-classifies that write as
    /// unattributed and snaps it back.</para>
    /// </summary>
    internal static void NotePoseApplied(SharedWindowKind kind)
    {
        if (kind == SharedWindowKind.MapStory)
            _lastApplyFrame = Time.frameCount;
    }

    /// <summary>
    /// Recompute who carries the kind. Called once per <c>ModalFallback.Tick</c>, from
    /// <c>StoryComposite.Tick</c> immediately after the claim level has been settled — so the answer
    /// is derived from the SAME measurement that decides whether the story window is refused, and the
    /// two can never disagree about whether the composite is standing.
    ///
    /// <para><paramref name="composedHost"/> is the host while <c>StoryComposite</c>'s claim stands and
    /// null otherwise. <paramref name="storyWindow"/> is the story box, used only to ask whether a hand
    /// is on ITS bar and to name it in the log.</para>
    /// </summary>
    internal static void TickMapStory(UIWindow? composedHost, UIWindow? storyWindow)
    {
        // A host that was destroyed or released under us is dropped with no deferral and no ceremony:
        // there is no window left to hold a hand or to receive a pose, so nothing the deferral
        // protects exists.
        if (_mapStoryHost != null && composedHost == null && !HasGrab(_mapStoryHost))
        {
            Swap(null, storyWindow, "the composed host is gone or is no longer floated by the mod");
            return;
        }

        UIWindow? wanted = null;
        bool noted = false;
        if (composedHost != null)
        {
            // MEASURED, NOT ASSERTED: the identity may only move to a window this client can actually
            // resolve a grab frame for. Handing the kind to a window with no grab is exactly the
            // ModBuild 236 defect in the other direction — the pose would go silently inert while the
            // bar claimed otherwise. [[verify-outcome-not-path]].
            //
            // TAKING IS STRICTER THAN KEEPING, ON PURPOSE. To take the kind the window must have a
            // grab frame that is actually VISIBLE (GrabVisible: alive, not render-hidden, not behind
            // the reveal gate) — that is exactly the test RemoteMapStory.TryReadFrame will apply, so
            // taking the kind on anything weaker would publish nothing while the bar claimed
            // otherwise. To KEEP it, being in the float set is enough: a single render-hidden tick
            // would otherwise hand the kind back and forth, and every one of those flips costs a
            // baseline drop, a bar repaint and two log lines.
            bool held = ReferenceEquals(composedHost, _mapStoryHost);
            if (held ? HasGrab(composedHost) : AnyFloatedGrab(composedHost))
                wanted = composedHost;
            else if (!held)
            {
                noted = true;
                NoteDefer("no-visible-grab",
                          $"the composite is standing on '{composedHost.name}' but the mod has no "
                          + "VISIBLE grab frame for it yet (it is not converted, or still behind the "
                          + "reveal gate), so the kind stays where it is — taking it now would publish "
                          + "nothing while the bar claimed the window was synced");
            }
        }

        if (ReferenceEquals(wanted, _mapStoryHost))
        {
            // NOTHING TO DO — but only clear the deferral clock when nothing was DEFERRED this tick.
            // The no-visible-grab branch above lands here with wanted == _mapStoryHost == null, and
            // clearing the code there would re-arm its own "a new reason always gets one line" rule
            // every single tick: the same wait would print once per frame instead of once per five
            // seconds. [[a-fuse-cannot-tell-a-hand-from-a-loop]] in miniature.
            if (!noted)
                ClearDefer();
            return;
        }

        UIWindow? from = _mapStoryHost != null ? _mapStoryHost : storyWindow;
        UIWindow? to = wanted != null ? wanted : storyWindow;

        // ---- reason 1: a hand. No timeout, ever. -------------------------------------------------
        bool handFrom = HandOnBar(from);
        bool handTo = HandOnBar(to);
        if (handFrom || handTo)
        {
            NoteDefer("hand",
                      $"a hand is on the grab bar of '{(handFrom ? Name(from) : Name(to))}' right now. "
                      + "Flipping the kind mid-carry would change the send cadence and the release "
                      + "re-face gate under the player's own hand, and on the hand-back edge it would "
                      + "turn a window he is HOLDING private, so letting go would yaw it to face him. "
                      + "The swap waits for the release; a window the player has grabbed is his until "
                      + "he lets go, and THIS reason has no timeout at all");
            return;
        }

        // ---- reason 2: a peer pose landed a moment ago. Capped. ----------------------------------
        // THE SENTINEL IS TESTED EXPLICITLY, never by arithmetic: `Time.frameCount - int.MinValue`
        // overflows, and a sentinel that overflows into a passing comparison is
        // [[sentinel-overflow-and-silent-scans]] verbatim.
        if (_lastApplyFrame != int.MinValue)
        {
            int since = Time.frameCount - _lastApplyFrame;
            if (since >= 0 && since <= ApplySettleFrames && !DeferExpired("peer-apply"))
            {
                NoteDefer("peer-apply",
                          "a peer's pose was applied to this kind within the last "
                          + $"{ApplySettleFrames} frame(s). PanelPoseWatch reads SharedWindows.IsShared "
                          + "once per tick as its peerOwned flag, so leaving the window in the same "
                          + "frame that write landed would have it re-classified Unattributed and "
                          + "snapped back in front of the player. The watch re-baselines on every "
                          + "sanctioned write, so a couple of clear frames is enough — and unlike the "
                          + $"hand, this reason gives up after {MaxDeferSeconds:0.#} s so a peer who "
                          + "drags for a minute cannot hold the identity hostage");
                return;
            }
        }

        Swap(wanted, storyWindow,
             wanted != null
                 ? "StoryComposite's claim is standing: the story window's own content is inside a "
                   + "mod-owned dock inside this floated host, so this is the window the story is "
                   + "being told in"
                 : "StoryComposite's claim has lapsed: the story is no longer being told inside the "
                   + "host, so the host goes back to being an ordinary private window and the story "
                   + "box carries its own kind again");
    }

    // ---- the swap -------------------------------------------------------------------------------

    private static void Swap(UIWindow? wanted, UIWindow? storyWindow, string why)
    {
        UIWindow? from = _mapStoryHost != null ? _mapStoryHost : storyWindow;
        UIWindow? to = wanted != null ? wanted : storyWindow;
        // MEASURED AT THE INSTANT OF THE SWAP rather than passed in as a constant: the deferral above
        // is supposed to make this impossible, and a falsifier that prints its own assumption proves
        // nothing ([[an-instrument-can-assert-a-cause]]). A True here means the deferral failed.
        bool handOnBar = HandOnBar(from) || HandOnBar(to);
        _mapStoryHost = wanted;
        _generation++;
        _why = wanted != null
            ? $"the quest-intro composite is standing and its host is '{Name(wanted)}'"
            : "no composite is standing, so MapStoryController.window carries its own kind";

        ClearDefer();
        VRLog.Info(Scope, $"STORY WINDOW SHARED IDENTITY SWAPPED — window half, generation "
                          + $"{_generation}: SharedWindowKind.MapStory moves from '{Name(from)}' to "
                          + $"'{Name(to)}'. WHY: {why}. A HAND WAS ON THE BAR: {handOnBar}, measured "
                          + "right here — the swap is refused outright while one is, with no timeout, "
                          + "so a True is the deferral failing and not a design. WHAT THIS ALONE DOES "
                          + "NOT PROVE: that no phantom drag "
                          + "was published. The move tracker keeps its baseline per GRAB FRAME and "
                          + "drops it on exactly this change; its own half of this line "
                          + "(STORY WINDOW SHARED IDENTITY SWAPPED — move-tracker half) names the "
                          + "baseline it dropped, the fresh one it took and states that nothing was "
                          + "published on that tick. A log with only ONE of the two halves at an edge "
                          + "is the bug: the kind moved and the tracker did not notice, which is a "
                          + "drag nobody made. NOTHING IS MOVED BY THIS: the identity decides which "
                          + "window's pose TRAVELS, never where any window stands, and no path here "
                          + "re-faces or re-places anything.");
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>Is the mod floating this window with a live, VISIBLE grab frame right now — the exact
    /// predicate <c>Net.RemoteMapStory.TryReadFrame</c> reads before it publishes anything?</summary>
    private static bool AnyFloatedGrab(UIWindow? window) =>
        HasGrab(window)
        && ModalFallback.TryGetGrabFor(window, out GrabbableModal? grab) && grab != null
        && ((IPanelGrabOwner)grab).GrabVisible;

    /// <summary>Is this window in the mod's float set with a grab at all? The weaker test, used to
    /// KEEP an identity across a render-hidden tick rather than to take one.</summary>
    private static bool HasGrab(UIWindow? window) =>
        window != null
        && ModalFallback.TryGetGrabFor(window, out GrabbableModal? grab) && grab != null;

    /// <summary>Is a hand on THIS window's grab bar right now? Asked of the grab the mod owns, never
    /// of the game window.</summary>
    private static bool HandOnBar(UIWindow? window)
    {
        if (window == null)
            return false;
        return ModalFallback.TryGetGrabFor(window, out GrabbableModal? grab)
               && grab != null && grab.IsGrabbed;
    }

    private static string Name(UIWindow? window) => window != null ? window.name : "<none>";

    /// <summary>Has THIS reason been holding the swap back for longer than it is allowed to? Keyed on
    /// the reason CODE, so a hand that held the swap for a minute does not hand the next reason an
    /// already-expired clock — which is how a cap meant for one thing silently disables another.
    /// Only <c>peer-apply</c> has a cap; the hand has none.</summary>
    private static bool DeferExpired(string code)
    {
        if (_deferSince <= 0f || code != _deferCode)
            return false;
        return Time.unscaledTime - _deferSince > MaxDeferSeconds;
    }

    private static void ClearDefer()
    {
        _deferSince = 0f;
        _deferCode = string.Empty;
    }

    private static void NoteDefer(string code, string reason)
    {
        float now = Time.unscaledTime;
        if (code != _deferCode)
        {
            _deferCode = code;
            _deferSince = now;
            _nextDeferNoteAt = 0f;   // a NEW reason always gets one line
        }
        else if (now < _nextDeferNoteAt)
        {
            return;
        }
        _nextDeferNoteAt = now + DeferNoteSeconds;
        VRLog.Info(Scope, $"STORY WINDOW SHARED IDENTITY DEFERRED ({code}) — {reason}. The kind stays "
                          + $"where it is meanwhile: '{Name(_mapStoryHost)}' when a host is named, "
                          + "otherwise the map story box itself. This is a WAIT and not a refusal — the "
                          + "swap is re-offered every tick, and it has been waiting "
                          + $"{now - _deferSince:0.0} s.");
    }
}
