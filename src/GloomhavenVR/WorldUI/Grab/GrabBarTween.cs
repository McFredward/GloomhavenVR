using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE OWNER OF A GRAB BAR'S PRESENTED POSE.</b> Every writer of a window's rod — the ink
/// union, the host rect, a re-seat, the withhold and its release — now sets a TARGET here, and a
/// LateUpdate step (<see cref="TickAll"/>, registered in <c>WorldUIModule</c>'s late list after
/// every writer and before the transparency round) moves what is actually DRAWN toward that target
/// with a short ease-out. The rod's root transform, its uniform scale, its end-to-end length, the
/// laser capsule that <see cref="GrabBarVisual.SetLength"/> keeps in step and the palm zone's box
/// all follow the presented value, so the hand never reaches for a ghost.
///
/// <para><b>THE REQUEST (user, 2026-09-03, translated):</b> <i>"The grab bar of a window changes
/// often, which is intended. But I don't like that it happens WITHOUT a transition — it 'pops'
/// into its new shape. I want an active growing and shrinking as a transition animation between
/// the different states of the grab bar. Fast, so nobody waits, but visible. Not only for size
/// changes but also for POSITION changes: it should travel to its new position instead of just
/// appearing there."</i> Four requirements in that: size changes GROW or SHRINK, position changes
/// TRAVEL, it is FAST (the dial below defaults to 150 ms; 0 is instant) and it applies to every
/// state change of the bar — which is why this is one class both owners of the bar machinery
/// (<see cref="GrabbableModal"/> and <c>SurfaceGrabBar</c>) drive, rather than a tween on one of
/// them.</para>
///
/// <para><b>WHERE IT SITS IN THE CHAIN.</b> Downstream of <see cref="BarSizeSettle"/>, not beside
/// it: the settle rule decides WHICH size the bar should be (a transient that reverts inside a
/// second never reaches the rod), and this class decides HOW the rod gets there. Two different
/// questions; the settle rule's answer is this class's input.</para>
///
/// <para><b>WHAT IS TWEENED AND WHAT IS NOT.</b> A size change eases the length (and the uniform
/// scale that carries the short-panel proportion); a position change eases the root's local
/// position, so the rod slides under its window rather than re-appearing; a bar that returns from
/// withheld grows from ZERO at its target (the uniform scale starts at 0 and the length is already
/// the target's), and a bar that is withheld shrinks to zero — the owner takes it off the screen
/// only when <see cref="Shown"/> has gone false, which it does on the frame the shrink completes.
/// NOT tweened: a bar in a hand. While the handle is grabbed the owner passes a snap reason and the
/// presented value follows the target at once — the tween is for the mod's own re-seats, never a
/// lag against the player's carry. Also snapped: any change made while the rod is off the screen
/// (behind the reveal gate, or withheld), for the same reason <see cref="BarSizeSettle"/> adopts
/// off-screen changes at once — nobody can see the transition, so holding the old value only makes
/// the presented pose untrue when the rod does appear. And the two-hand resize, which writes the
/// grab root's <c>localScale</c> above this class and never enters it.</para>
///
/// <para><b>THE HIDE MECHANISM STAYS WITH EACH OWNER.</b> This class never writes
/// <c>SetActive</c> or <c>MeshRenderer.enabled</c>: <c>GrabbableModal</c> switches the rod's root
/// GameObject (so the reveal gate's restore set cannot re-enable a withheld rod behind its back) and
/// <c>SurfaceGrabBar</c> re-asserts renderer enables against the panel's reveal walk every tick.
/// Both documented mechanisms are kept; each owner reads <see cref="Shown"/> and applies its own.
/// A presented scale of zero draws nothing regardless, so the one frame between the shrink
/// completing (LateUpdate) and the owner noticing (its next Update tick) shows nothing.</para>
///
/// <para><b>THE EASE.</b> One clock for the whole pose: a leg starts from the PRESENTED values
/// (continuous, so a retarget mid-flight never jumps) and reaches the target exactly at the end of
/// the configured duration on an ease-out cubic, <c>1 - (1 - t)^3</c>. A target that moves every
/// frame — a window whose ink grows line by line — restarts the leg every frame from where the rod
/// is, which degenerates gracefully into a smoothed follow with about four frames of lag at 150 ms.
/// A target that moves by less than half a percent of the bar's width is the same target: the
/// per-frame <c>SyncBar</c> re-derives the same numbers every tick, and restarting on float noise
/// would keep the rod in the fast opening phase of the curve forever. UNSCALED time, because modal
/// windows stand while the game is paused and a paused clock would freeze a rod mid-grow.</para>
///
/// <para><b>PER-FRAME COST.</b> Bounded by the number of live bars (a handful): a bar that is not
/// moving returns on its first line; a moving one costs three transform writes, one
/// <see cref="GrabBarVisual.SetLength"/> (a dictionary hit when the tiling quantum crosses) and two
/// collider field writes. No allocation on any tick.</para>
///
/// <para><b>MULTIPLAYER.</b> A peer's copy of a shared window is a <c>GrabbableModal</c> of its own
/// (<c>RemoteMapStory</c> / <c>RemoteStorySync</c> drive its FRAME pose; the rod under it is built
/// and synced by the same code as the owner's), so the mirror gets this tween by construction and
/// nothing about the transition needs a wire field — it is viewer-local presentation of a target
/// both sides already agree on. The board bars (<c>PlayTray</c> and its mirror
/// <c>RemoteBoardFurniture</c>) are built once at a fixed length and position and have no runtime
/// writer, so there is nothing there to tween.</para>
/// </summary>
internal sealed class GrabBarTween
{
    /// <summary>
    /// Two targets closer than this fraction of the bar's frame-local width are the same target.
    /// Relative, because the frame-local metres the owners work in carry the diorama scale (tens
    /// of units at a 198x rig) and an absolute epsilon would be wrong at one end or the other. Half
    /// of <see cref="BarSizeSettle"/>'s one percent: that rule decides whether a change is REAL,
    /// this one only whether a leg needs restarting, and it must not swallow a change the settle
    /// rule has already earned.
    /// </summary>
    private const float RelativeTolerance = 0.005f;

    /// <summary>How many <c>GRAB BAR TWEEN</c> lines a session may print. The line is once per bar
    /// already; the cap keeps a session that floats forty windows from spending forty lines on a
    /// mechanism the first six have proven.</summary>
    private const int ReportCap = 6;

    private static int _reports;

    /// <summary>Every bar with a tween, for <see cref="TickAll"/>. Owners add themselves in the
    /// constructor and leave through <see cref="Release"/>; a bar whose transform died without one
    /// is dropped by the tick.</summary>
    private static readonly List<GrabBarTween> Live = new();

    private readonly GrabBarVisual _bar;
    private readonly BoxCollider? _zone;
    private readonly string _logName;

    // ---- the target: what every writer sets --------------------------------------------------
    private Vector3 _targetPos;
    private float _targetScale = 1f;
    private float _targetLength;
    private Vector3 _targetZoneSize;
    private bool _targetVisible;

    // ---- where the current leg started -------------------------------------------------------
    private Vector3 _fromPos;
    private float _fromScale;
    private float _fromLength;
    private Vector3 _fromZoneSize;
    private float _fromAppear;

    // ---- the presented value: what is drawn and what can be grabbed --------------------------
    private Vector3 _pos;
    private float _scale = 1f;
    private float _length;
    private Vector3 _zoneSize;
    /// <summary>0 = shrunk to nothing, 1 = at its full size. The uniform scale is multiplied by
    /// this, which is what "grows from zero at its target" means.</summary>
    private float _appear;

    private float _elapsed;
    private bool _moving;
    private bool _seeded;
    private string _writer = "(no writer yet)";
    private bool _reported;

    /// <summary>
    /// TRUE while the owner should have the rod on the screen: the target is visible, OR the rod is
    /// still shrinking toward hidden. The owner applies its own hide mechanism from this; see the
    /// class doc for why this class never does.
    /// </summary>
    internal bool Shown => _targetVisible || _appear > 0f;

    /// <summary>The target position, for a falsifier that must judge WHERE the bar is going rather
    /// than where a mid-leg rod happens to be (<c>GrabbableModal.ReportBarPlacement</c>).</summary>
    internal Vector3 TargetPosition => _targetPos;

    /// <summary>The configured duration in seconds; 0 = every change is instant.</summary>
    internal static float DurationSeconds => Mathf.Max(0f, WorldUIConfig.GrabBarTweenMs.Value) * 0.001f;

    /// <param name="bar">The drawn rod this tween presents. Its <see cref="GrabBarVisual.Root"/>
    /// local position and uniform scale, and its length, are written by nobody else from here on.</param>
    /// <param name="zone">The palm grab zone, if the owner has one; its centre and size follow the
    /// presented value. Its <c>enabled</c> flag stays the owner's.</param>
    /// <param name="visible">Whether the rod starts on the screen. The presented appear fraction is
    /// seeded from this, so a bar built visible is at full size on its first frame and one built
    /// withheld is at zero and grows when released.</param>
    internal GrabBarTween(GrabBarVisual bar, BoxCollider? zone, string logName, bool visible)
    {
        _bar = bar;
        _zone = zone;
        _logName = logName;
        _targetVisible = visible;
        _appear = visible ? 1f : 0f;
        Live.Add(this);
    }

    /// <summary>Leave the tick list. Called from the owner's teardown; idempotent.</summary>
    internal void Release() => Live.Remove(this);

    /// <summary>
    /// Set where the rod should be and how big. Called once per bar per frame from the owner's
    /// <c>SyncBar</c>; a call that changes nothing beyond <see cref="RelativeTolerance"/> costs a
    /// few compares and touches no transform.
    /// </summary>
    /// <param name="pos">Root local position, frame-local metres.</param>
    /// <param name="scale">The root's uniform scale (short-panel proportion x world scale).</param>
    /// <param name="length">End-to-end length in the ROD's own metres (frame-local width / scale).</param>
    /// <param name="zoneSize">The palm zone's box size, frame-local metres; its centre is <paramref name="pos"/>.</param>
    /// <param name="snapWhy">Null to ease toward the target; otherwise the reason the presented
    /// value must follow it AT ONCE (a hand is carrying the window, the rod is off the screen).
    /// The reason is printed, so it is a sentence and not a flag.</param>
    /// <param name="writer">Who set this target, for the instrument.</param>
    internal void SetTarget(Vector3 pos, float scale, float length, Vector3 zoneSize,
                            string? snapWhy, string writer)
    {
        bool changed = !_seeded || Differs(pos, scale, length, zoneSize);
        _targetPos = pos;
        _targetScale = scale;
        _targetLength = length;
        _targetZoneSize = zoneSize;
        if (changed)
            _writer = writer;

        if (!_seeded)
        {
            // THE FIRST VALUE IS ADOPTED AT ONCE, as BarSizeSettle's is: a rod must be the right
            // size and in the right place on the frame it is built, and there is nothing yet for
            // a transition to start from.
            _seeded = true;
            Snap();
            return;
        }

        if (snapWhy != null || DurationSeconds <= 0f)
        {
            if (_moving || changed)
            {
                ReportFirstLeg(snapWhy ?? "[WorldUI] GrabBarTweenMs is 0, so every change is instant");
                Snap();
            }
            return;
        }

        if (changed)
        {
            ReportFirstLeg(null);
            BeginLeg();
        }
    }

    /// <summary>
    /// Set whether the rod should be on the screen. A rising edge grows the rod from zero at its
    /// target; a falling edge shrinks it to zero, after which <see cref="Shown"/> turns false. A
    /// call that changes nothing does nothing.
    /// </summary>
    /// <param name="snapWhy">Null to ease; otherwise why the rod must appear or vanish at once —
    /// a window that never drew, a window whose pixels are leaving, a hand on the bar.</param>
    internal void SetVisible(bool visible, string? snapWhy, string writer)
    {
        if (visible == _targetVisible)
            return;
        _targetVisible = visible;
        _writer = writer;
        if (!_seeded || snapWhy != null || DurationSeconds <= 0f)
        {
            if (_seeded)
                ReportFirstLeg(snapWhy ?? "[WorldUI] GrabBarTweenMs is 0, so every change is instant");
            Snap();
            return;
        }
        ReportFirstLeg(null);
        BeginLeg();
    }

    /// <summary>Advance every live bar. Registered as <c>GrabBarTween.Late</c> in
    /// <c>WorldUIModule</c>'s LateUpdate list — after the modal follow (Update) and every surface's
    /// late tick, before the transparency round.</summary>
    internal static void TickAll()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            GrabBarTween t = Live[i];
            // A rod whose holder was destroyed without a Release: Unity's overloaded null.
            if (t._bar.Root == null)
            {
                Live.RemoveAt(i);
                continue;
            }
            t.Tick(dt);
        }
    }

    private void Tick(float dt)
    {
        if (!_moving)
            return;
        float duration = DurationSeconds;
        _elapsed += dt;
        float k = duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / duration);
        float inv = 1f - k;
        float e = 1f - inv * inv * inv;
        float appearTarget = _targetVisible ? 1f : 0f;

        if (k >= 1f)
        {
            _moving = false;
            _pos = _targetPos;
            _scale = _targetScale;
            _length = _targetLength;
            _zoneSize = _targetZoneSize;
            _appear = appearTarget;
        }
        else
        {
            _pos = Vector3.LerpUnclamped(_fromPos, _targetPos, e);
            _scale = Mathf.LerpUnclamped(_fromScale, _targetScale, e);
            _length = Mathf.LerpUnclamped(_fromLength, _targetLength, e);
            _zoneSize = Vector3.LerpUnclamped(_fromZoneSize, _targetZoneSize, e);
            _appear = Mathf.LerpUnclamped(_fromAppear, appearTarget, e);
        }
        Apply();
    }

    private void BeginLeg()
    {
        _fromPos = _pos;
        _fromScale = _scale;
        _fromLength = _length;
        _fromZoneSize = _zoneSize;
        _fromAppear = _appear;
        _elapsed = 0f;
        _moving = true;
    }

    private void Snap()
    {
        _moving = false;
        _pos = _targetPos;
        _scale = _targetScale;
        _length = _targetLength;
        _zoneSize = _targetZoneSize;
        _appear = _targetVisible ? 1f : 0f;
        Apply();
    }

    /// <summary>The presented value, written out. The only place the rod's root, its length and
    /// the zone's box are written once a bar has a tween.</summary>
    private void Apply()
    {
        Transform root = _bar.Root;
        if (root == null)
            return;
        float appear = Mathf.Clamp01(_appear);
        root.localPosition = _pos;
        // A rod at zero scale draws nothing and its capsule strikes nothing, which is the honest
        // picture of "withheld"; the floor only keeps the matrix invertible.
        root.localScale = Vector3.one * Mathf.Max(_scale * appear, 1e-4f);
        // The laser capsule rides SetLength (GrabBarVisual keeps its height in step) and the root's
        // scale above, so the far ray meets exactly the rod that is drawn.
        _bar.SetLength(_length);
        if (_zone != null)
        {
            _zone.center = _pos;
            _zone.size = _zoneSize * Mathf.Max(appear, 1e-3f);
        }
    }

    private bool Differs(Vector3 pos, float scale, float length, Vector3 zoneSize)
    {
        float refWidth = Mathf.Max(Mathf.Abs(_targetLength * _targetScale),
                                   Mathf.Abs(length * scale), 1e-3f);
        float eps = RelativeTolerance * refWidth;
        if ((pos - _targetPos).sqrMagnitude > eps * eps)
            return true;
        if ((zoneSize - _targetZoneSize).sqrMagnitude > eps * eps)
            return true;
        if (Mathf.Abs(scale - _targetScale)
            > RelativeTolerance * Mathf.Max(Mathf.Abs(scale), Mathf.Abs(_targetScale), 1e-6f))
            return true;
        return Mathf.Abs(length - _targetLength)
               > RelativeTolerance * Mathf.Max(Mathf.Abs(length), Mathf.Abs(_targetLength), 1e-6f);
    }

    /// <summary>
    /// The one printed line — on the FIRST transition of each bar, capped per session, and never
    /// per frame. Called BEFORE the leg (or the snap) so <c>from</c> is what the player was looking
    /// at and <c>to</c> is where the rod is going.
    ///
    /// <para>It is written to read correctly when nothing moved: a visibility-only edge has the
    /// same width and position on both sides and says so, and a snapped change names the reason it
    /// did not ease. It asserts only its own inputs — the two poses, the duration it will use and
    /// the writer that set the target — and does not claim the transition was seen.</para>
    /// </summary>
    private void ReportFirstLeg(string? snapWhy)
    {
        if (_reported || _reports >= ReportCap)
            return;
        _reported = true;
        _reports++;

        float fromWidth = _length * _scale;
        float toWidth = _targetLength * _targetScale;
        bool sameWidth = Mathf.Abs(fromWidth - toWidth)
                         <= RelativeTolerance * Mathf.Max(Mathf.Abs(fromWidth), Mathf.Abs(toWidth), 1e-6f);
        float travel = Vector3.Distance(_pos, _targetPos);
        bool samePos = travel <= RelativeTolerance * Mathf.Max(toWidth, 1e-3f);
        bool fromShown = _appear > 0f;
        string width = sameWidth
            ? $"width {toWidth:0.####} m (unchanged)"
            : $"width {fromWidth:0.####} -> {toWidth:0.####} m ({(toWidth > fromWidth ? "grows" : "shrinks")})";
        string position = samePos
            ? $"position ({_targetPos.x:0.####}, {_targetPos.y:0.####}) (unchanged)"
            : $"position ({_pos.x:0.####}, {_pos.y:0.####}) -> ({_targetPos.x:0.####}, {_targetPos.y:0.####}), "
              + $"travelling {travel:0.####} m";
        string visibility = fromShown == _targetVisible
            ? $"visibility {(_targetVisible ? "shown" : "hidden")} (unchanged)"
            : $"visibility {(fromShown ? "shown" : "hidden")} -> {(_targetVisible ? "shown (grows from zero at the target)" : "hidden (shrinks to zero, then leaves)")}";
        string how = snapWhy != null
            ? $"SNAPPED, no transition, because {snapWhy}"
            : $"eased over {DurationSeconds * 1000f:0} ms (ease-out cubic; [WorldUI] GrabBarTweenMs)";

        // HW-VERIFY: the answer to "es ploppt". One line per bar on its first transition, at most
        // six a session. A line whose width or position actually changes and reads "eased over
        // 150 ms" is the mechanism running; a bar the user still sees pop and whose line says
        // SNAPPED names the reason it was not eased; a bar that pops with NO line at all was
        // written by something that is not a writer of this class — grep its owner for a direct
        // write to the rod's Root.
        VRLog.Note("WorldUI", $"GRAB BAR TWEEN '{_logName}': first transition of this bar — {width}, "
                              + $"{position} (frame-local metres), {visibility}; {how}; target set by "
                              + $"{_writer}. The drawn rod, its laser capsule and the palm zone all "
                              + "follow the presented value; a grabbed bar snaps to the hand instead. "
                              + $"Printed once per bar, at most {ReportCap} per session ({_reports} so far).");
    }
}
