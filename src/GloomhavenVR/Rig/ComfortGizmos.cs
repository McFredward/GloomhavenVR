using System.Text;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// <c>[Comfort] DebugGizmos</c>: OnGUI overlay with the live comfort state — rig
/// pose/scale multiplier, world-grab state, per-hand world-grab ownership (and whether an
/// unused grip is pressed), snap-turn arming, <see cref="RigClamp"/> status, recenter-chord
/// progress. Placed bottom-left so it coexists with the Phase-2 dev overlay (top-left).
///
/// There is no vignette row, and this doc used to claim one. The comfort vignette — component,
/// settings row and Loc label — was REMOVED (8454e88, 5e4942a) and must not be restored: its
/// absence is the decision, not an omission (INVARIANTS-Net-Rig.md, "The comfort vignette and
/// seated mode were REMOVED").
///
/// Like the DevConsole, OnGUI string building only runs while the toggle is on —
/// the per-frame allocation rule applies to the always-on path, not debug overlays.
/// </summary>
internal sealed class ComfortGizmos : MonoBehaviour
{
    private readonly StringBuilder _sb = new(512);

    private void OnGUI()
    {
        if (!ComfortSettings.IsBound || !ComfortSettings.DebugGizmos.Value)
            return;

        WorldGrab? grab = WorldGrab.Instance;
        SnapTurn? turn = SnapTurn.Instance;
        Comfort? comfort = Comfort.Instance;
        Transform? rig = RigTarget.Current;

        _sb.Length = 0;
        _sb.Append("COMFORT  |  mode ").Append(VRModeStateMachine.CurrentMode)
           .Append("  |  rig ");
        if (rig == null)
        {
            _sb.Append("—");
        }
        else
        {
            Vector3 p = rig.position;
            _sb.Append(RigTarget.IsDevProxy ? "DEV-PROXY " : string.Empty)
               .Append('(').Append(p.x.ToString("F1")).Append(", ")
               .Append(p.y.ToString("F1")).Append(", ")
               .Append(p.z.ToString("F1")).Append(")  yaw ")
               .Append(rig.eulerAngles.y.ToString("F0"))
               .Append("°  scale ").Append(rig.localScale.x.ToString("F2"))
               .Append(" (x").Append((rig.localScale.x / RigTarget.BaseScale).ToString("F2"))
               .Append(" of base ").Append(RigTarget.BaseScale.ToString("F1")).Append(')');
        }

        _sb.Append("\ngrab ");
        if (grab == null || !grab.IsGrabbing)
            _sb.Append("idle");
        else
            _sb.Append(grab.IsTwoHand ? "TWO-HAND (rotate/scale)" : "ONE-HAND (drag)");
        AppendHandGrip(grab, VRHands.Left);
        AppendHandGrip(grab, VRHands.Right);

        _sb.Append("\nturn ").Append(ComfortSettings.Turn.Value);
        if (ComfortSettings.Turn.Value == TurnMode.Snap)
            _sb.Append(turn != null && turn.WaitingForRearm ? " [waiting re-arm]" : " [armed]");
        // The overlay must show the gate SnapTurn actually applies, not the mode alone — since
        // ModBuild 138 the mode is not part of the test at all (TURN NEVER: board state never
        // suppresses turning; only a consumer that would REALLY read the turn stick this frame
        // can, and AoE rotation deliberately reads the other hand's). An overlay that still said
        // "SUPPRESSED" while turning worked would be the next round's bug report.
        if (VRModeStateMachine.CurrentMode == VRMode.BoardTargeting)
            _sb.Append(LocalTurnControl.TargetingOwnsStick
                ? "  (SUPPRESSED: a live AoE pattern claims the TURN stick — not expected, the "
                  + "hands should be split)"
                : "  (targeting up, nothing claims the turn stick — turning stays mine)");
        else if (turn != null && turn.ScrollBlocked)
            _sb.Append("  (SUPPRESSED: menu scrolling owns the stick — release the stick sideways, "
                       + "or flick it hard sideways, to turn)");

        _sb.Append("\nclamp ").Append(RigClamp.LastClampActive ? "ACTIVE (head lifted)" : "ok");
        if (comfort != null && comfort.ChordProgress > 0f)
            _sb.Append("  |  recenter chord ").Append((comfort.ChordProgress * 100f).ToString("F0")).Append('%');

        GUI.Label(new Rect(10f, Screen.height - 130f, 1100f, 120f), _sb.ToString());
    }

    private void AppendHandGrip(WorldGrab? grab, VRHand? hand)
    {
        _sb.Append("  ");
        if (hand == null)
        {
            _sb.Append("—");
            return;
        }
        _sb.Append(hand.Side == HandSide.Left ? 'L' : 'R').Append(':');
        if (grab != null && grab.IsHandGrabbing(hand))
            _sb.Append("WORLD");
        else if (hand.Grabber.Held != null)
            _sb.Append("object");
        else if (hand.GripPressed)
            _sb.Append("grip(unused)");
        else
            _sb.Append("free");
    }
}
