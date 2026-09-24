using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// WHO OWNS THIS THUMBSTICK RIGHT NOW: menu SCROLLING, or stick FLIGHT?
///
/// <para>User report 2026-08-03: "Wenn man die rechte Hand eingestellt hat zum Fliegen und dann
/// aber im Menue scrollen will, passiert beides. Scrollen soll mehr dominant sein und das Fliegen
/// ueberschreiben." Both features read the SAME axis of the SAME stick — scrolling reads y
/// (<see cref="RayUguiDriver.TickStickScroll"/> and the two sibling paths), flight reads y as
/// forward/back (<c>Rig.Flight</c>) — so a player pushing up to walk a settings list down also
/// flew forward through the room while reading it. This class is the one place that answers
/// which of the two is live, and the answer is always SCROLL WINS: a scroll is a deliberate,
/// aimed act on a surface the player is pointing at, while flight is ambient locomotion that is
/// available again a frame later. Losing the scroll costs the player the thing they came for;
/// losing a fraction of a second of flight costs nothing.</para>
///
/// <para>THE SIGNAL IS "THE POINTER IS ON SOMETHING THAT WOULD ACTUALLY SCROLL", not "a menu is
/// open somewhere". That distinction is load-bearing and mirrors an existing ruling: turning
/// deliberately stays alive in <c>VRMode.ModalUI</c> because a player must keep full movement
/// while a dialog merely floats, and <c>Flight</c> repeats that call in its own mode gate.
/// Suppressing flight for "a canvas exists" would break exactly that. So the producers below
/// only report while a hand's own beam/poke hover resolves to a live scrollable whose content
/// really overflows its viewport — the same resolution the scroll delivery itself does, taken
/// from the same code path, so the two can never disagree.</para>
///
/// <para>PER HAND, AND IT DOES NOT LATCH. State is two slots keyed by <see cref="HandSide"/>, so
/// scrolling with the left hand never grounds a right-handed flier. Hover is stamped with
/// <c>Time.frameCount</c> and expires by itself — nothing has to "release" it, so the frame the
/// beam leaves the list, flight is back. A one-frame tolerance is allowed on the stamp purely
/// because Unity gives no ordering guarantee between the hand drivers and <c>Flight.Update</c>;
/// without it the verdict would depend on which MonoBehaviour Unity happened to tick first.</para>
///
/// <para>THE ONLY HOLD IS BOUGHT WITH A REAL SCROLL. A hover raycast off a hand-held laser does
/// drop out for single frames — the beam crosses a gap between list rows, or tremor takes it a
/// pixel past the viewport edge — and a one-frame dropout in the middle of a push would let a
/// single flight step through, which is felt as a lurch. So a frame that actually DELIVERED
/// scroll also arms <see cref="ScrollHoldSeconds"/> of grace. It is armed by delivery, never by
/// hover, which is what keeps it from being a latch: point at a list without touching the stick,
/// look away, and flight is available on the very next frame because no grace was ever armed.</para>
/// </summary>
internal static class UiScrollFocus
{
    /// <summary>
    /// Grace after a frame that really scrolled, in unscaled seconds. Long enough to bridge the
    /// hover dropouts a hand-held laser produces at a list edge, far shorter than the time it
    /// takes a player to deliberately point somewhere else and push again.
    /// </summary>
    private const float ScrollHoldSeconds = 0.25f;

    // Physical scroll surfaces have no uGUI pointer driver. Poll their current geometry
    // before locomotion consumes the stick, so the first hover cannot depend on Unity's
    // unrelated MonoBehaviour update order. The producer owns registration and teardown;
    // this callback only stamps hover, never changes pages or native game state.
    internal static System.Action<VRHand>? PhysicalHoverProbe;

    /// <summary>
    /// How stale a hover stamp may be and still count. One frame, and only to absorb the
    /// undefined tick order between the hand drivers and the flight component — not a hold.
    /// </summary>
    private const int HoverFrameSlack = 1;

    /// <summary>
    /// "This slot was never stamped" sentinel, and it MUST be tested for by identity, never fed
    /// into the age subtraction.
    ///
    /// ROOT CAUSE of the 2026-08-04 hardware latch ("Ich konnte erst fliegen, NACHDEM ich einmal
    /// im Menue gescrollt habe"): the original check was the bare subtraction
    /// <c>Time.frameCount - HoverFrame[i] &lt;= HoverFrameSlack</c> with the slots initialized to
    /// <c>int.MinValue</c>. In C#'s default UNCHECKED arithmetic, <c>frameCount - int.MinValue</c>
    /// wraps: for any frameCount &gt;= 0 the result is <c>frameCount - 2147483648</c>, a large
    /// NEGATIVE number, which passes the "at most one frame old" test. So a virgin slot read as
    /// "hovered this very frame" and <see cref="IsScrolling"/> answered TRUE for a hand that had
    /// never once pointed at a scrollable — from process start until the first genuine stamp
    /// overwrote the sentinel. Evidence (Player.log of that session): "stick flight: SUSPENDED on
    /// the Right hand" fired on the exact frame of the Menu2D -&gt; TableIdle flip (line 1956, the
    /// FIRST frame Flight's mode gate ever let ScrollAllowed run, while the scenario was still
    /// loading and the pointer was on nothing), and RESUMED only ~2800 log lines later (4764),
    /// immediately after the player's beam finally landed on the options window's scroll area and
    /// the first real NoteScrollHover healed the slot; SUSPENDED/RESUMED pairs cycle normally from
    /// then on (4771/4773). The guard below makes the sentinel mean "never", unconditionally: it
    /// is compared by identity before any subtraction, so no overflow path exists. Once a slot
    /// holds a real stamp the subtraction is safe by construction — real stamps are frameCount
    /// values, i.e. in <c>[0, Time.frameCount]</c>, so the difference is non-negative and small.
    /// </summary>
    private const int NeverStamped = int.MinValue;

    private static readonly int[] HoverFrame = { NeverStamped, NeverStamped };
    private static readonly float[] ScrollTime = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>What stamped each slot last — surface + producer tag, resolved to a name only when
    /// a diagnostic line actually logs (Object.name allocates; stamping runs per hovered frame).</summary>
    private static readonly Object?[] HoverSurface = { null, null };
    private static readonly string?[] HoverProducer = { null, null };

    /// <summary>
    /// This hand's pointer is resting on a uGUI surface that a stick push WOULD scroll. Called
    /// every frame the condition holds; it expires on its own, there is no matching "clear".
    /// <paramref name="surface"/> and <paramref name="producer"/> are attribution for the
    /// flight-suppression diagnostic (see <see cref="Describe"/>): which scrollable, stamped by
    /// which code path — so the next "flight is dead" hardware log names its suppressor.
    /// </summary>
    internal static void NoteScrollHover(VRHand? hand, Object? surface, string producer)
    {
        if (hand == null)
            return;
        int i = (int)hand.Side;
        HoverFrame[i] = Time.frameCount;
        HoverSurface[i] = surface;
        HoverProducer[i] = producer;
    }

    /// <summary>This hand's stick actually moved a scrollable this frame (arms the grace window).</summary>
    internal static void NoteScrollDelivered(VRHand? hand, Object? surface, string producer)
    {
        if (hand == null)
            return;
        int i = (int)hand.Side;
        HoverFrame[i] = Time.frameCount;
        ScrollTime[i] = Time.unscaledTime;
        HoverSurface[i] = surface;
        HoverProducer[i] = producer;
    }

    /// <summary>
    /// Is this hand's thumbstick currently owned by menu scrolling? True while its pointer sits
    /// on a live scrollable, plus <see cref="ScrollHoldSeconds"/> after a delivered scroll.
    /// A slot still holding <see cref="NeverStamped"/> is NO by definition — see the sentinel's
    /// doc for the overflow latch this identity test fixes; the subtraction must never see it.
    /// </summary>
    internal static bool IsScrolling(VRHand? hand)
    {
        if (hand == null)
            return false;
        PhysicalHoverProbe?.Invoke(hand);
        int i = (int)hand.Side;
        int stamp = HoverFrame[i];
        if (stamp != NeverStamped && Time.frameCount - stamp <= HoverFrameSlack)
            return true;
        return Time.unscaledTime - ScrollTime[i] <= ScrollHoldSeconds;
    }

    /// <summary>
    /// Attribution for a flight-suppression diagnostic: WHICH surface stamped this hand's hover,
    /// by WHICH producer, and how old the stamps are. Built only when a log line actually fires
    /// (never per frame). The "never stamped" answer is load-bearing: it is the line that would
    /// have named the 2026-08-04 sentinel latch in one hardware round instead of three.
    /// </summary>
    internal static string Describe(VRHand hand)
    {
        int i = (int)hand.Side;
        int stamp = HoverFrame[i];
        if (stamp == NeverStamped)
            return "no scroll hover was ever stamped for this hand this session";
        Object? surface = HoverSurface[i];
        string name = surface != null ? surface.name : "<destroyed surface>";
        string producer = HoverProducer[i] ?? "<unknown producer>";
        int age = Time.frameCount - stamp;
        string grace = float.IsNegativeInfinity(ScrollTime[i])
            ? "no scroll ever delivered"
            : $"last delivered scroll {Time.unscaledTime - ScrollTime[i]:F2}s ago";
        return $"hover stamped by {producer} on '{name}' {age} frame(s) ago; {grace}";
    }

    /// <summary>
    /// Has this <see cref="ScrollRect"/> anything to scroll — i.e. does its content overflow its
    /// viewport on an axis it is allowed to move?
    ///
    /// <para>This is the "genuinely live" half of the arbitration, and the reason it exists is
    /// the ruling quoted in the class doc: a short list that fits its window, or a dialog whose
    /// scroll view is present but not full, must NOT cost the player their locomotion. Pointing
    /// at a floating dialog is not an act of scrolling. A ScrollRect with no content reference
    /// cannot move at all and is reported the same way, so flight keeps running there too.</para>
    ///
    /// <para>The one-pixel slack absorbs the layout rounding that leaves a "full" list a hair
    /// taller than its viewport; below that the ScrollRect would clamp straight back anyway.</para>
    ///
    /// <para>It gates ARBITRATION ONLY. Every caller still delivers its wheel stream exactly as
    /// before, so a ScrollRect this predicate misjudges (an exotic layout, a content rect not
    /// yet built on the first frame) loses no scrolling — at worst the player keeps flight for a
    /// frame they would rather not have had it.</para>
    /// </summary>
    internal static bool CanScroll(ScrollRect scroll)
    {
        RectTransform? content = scroll.content;
        if (content == null)
            return false;
        RectTransform? viewport = scroll.viewport != null
            ? scroll.viewport
            : scroll.transform as RectTransform;
        if (viewport == null)
            return false;
        const float slack = 1f; // pixels of layout rounding
        if (scroll.vertical && content.rect.height > viewport.rect.height + slack)
            return true;
        return scroll.horizontal && content.rect.width > viewport.rect.width + slack;
    }
}
