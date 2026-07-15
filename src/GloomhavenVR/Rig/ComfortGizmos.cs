using System.Text;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// <c>[Comfort] DebugGizmos</c>: OnGUI overlay with the live comfort state — rig
/// pose/scale multiplier, world-grab state, per-hand grip ownership, snap-turn arming,
/// clamp and vignette status, recenter-chord progress. Placed bottom-left so it
/// coexists with the Phase-2 dev overlay (top-left).
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
        if (VRModeStateMachine.CurrentMode == VRMode.BoardTargeting)
            _sb.Append("  (SUPPRESSED: BoardTargeting owns the stick)");

        _sb.Append("\nclamp ").Append(RigClamp.LastClampActive ? "ACTIVE (head lifted)" : "ok")
           .Append("  |  vignette ")
           .Append(ComfortSettings.VignetteEnabled.Value ? "on" : "off");
        if (comfort != null && comfort.ChordProgress > 0f)
            _sb.Append("  |  recenter chord ").Append((comfort.ChordProgress * 100f).ToString("F0")).Append('%');
        _sb.Append("\nseated ").Append(ComfortSettings.SeatedMode.Value ? "YES" : "no")
           .Append("  heightOffset ").Append(ComfortSettings.TableHeightOffset.Value.ToString("F2"))
           .Append(" m  |  savedScaleMult ").Append(ComfortSettings.SavedScaleMultiplier.Value.ToString("F2"));

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
