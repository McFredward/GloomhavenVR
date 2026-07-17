using System.Collections.Generic;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// Doff/don detector (hardware test #17): the user took the HMD off mid-session and
/// put it back on; the standby made Unity's InputSystem re-enumerate devices and the
/// session came back with dead input and a modal window stranded at the pre-doff
/// head pose. The heartbeat showed the away period plainly (beats #32-62:
/// <c>moved=N</c> with a byte-identical pose — a worn HMD never freezes below the
/// sensor-jitter epsilon), but nothing acted on it.
///
/// This watch turns that signal into recovery: it tracks user presence per frame via
/// two independent signals — the XR head device's <c>CommonUsages.userPresence</c>
/// feature (proximity sensor; not every runtime reports it) and a frozen-head-pose
/// fallback (no pose change for <see cref="FrozenPoseSeconds"/>, same epsilons as
/// <see cref="VRHeartbeat"/>). When presence returns after a gap of at least
/// <see cref="MinGapSeconds"/> it raises <see cref="VREvents.SessionResumed"/> —
/// subscribers re-assert the virtual mouse, re-float open modal windows in front of
/// the CURRENT head pose, and report what they recovered — then logs one
/// "[Core] Session resumed" line with the combined report.
///
/// Allocation discipline: steady state is two vector compares and one XR feature
/// read per frame; the recovery list and log string allocate only on the rare
/// resume edge. Hosted on the mod's hardened Core root next to the heartbeat.
/// </summary>
internal sealed class VRPresenceWatch : MonoBehaviour
{
    /// <summary>Head pose unchanged this long = user away (a worn HMD always jitters).</summary>
    private const float FrozenPoseSeconds = 5f;

    /// <summary>Shorter gaps are sensor blips, not a doff — no recovery sweep.</summary>
    private const float MinGapSeconds = 3f;

    /// <summary>Reused recovery report (cleared per resume; handlers append).</summary>
    private static readonly List<string> Recovered = new(4);

    private bool _poseKnown;
    private Vector3 _lastPos;
    private Vector3 _lastEuler;
    private float _lastPoseChange;

    private bool _away;
    private float _awaySince;
    private string _awaySignal = "head pose";

    private void Update()
    {
        float now = Time.realtimeSinceStartup;

        // Head pose activity (same epsilons as the heartbeat's moved flag).
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head != null)
        {
            Transform t = head.transform;
            Vector3 pos = t.position;
            Vector3 euler = t.eulerAngles;
            if (!_poseKnown || (pos - _lastPos).sqrMagnitude > 1e-6f
                            || (euler - _lastEuler).sqrMagnitude > 1e-4f)
            {
                _poseKnown = true;
                _lastPos = pos;
                _lastEuler = euler;
                _lastPoseChange = now;
            }
        }

        // Proximity sensor, where the runtime reports it (Quest via VDXR may not).
        bool presenceKnown = false, present = false;
        InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool value))
        {
            presenceKnown = true;
            present = value;
        }

        bool poseFrozen = _poseKnown && now - _lastPoseChange > FrozenPoseSeconds;
        bool away = (presenceKnown && !present) || poseFrozen;

        if (away && !_away)
        {
            _away = true;
            // The pose-frozen path detects late: the user has been away since the
            // pose STOPPED, not since the threshold fired.
            bool byPresence = presenceKnown && !present;
            _awaySignal = byPresence ? "userPresence" : "head pose";
            _awaySince = byPresence ? now : _lastPoseChange;
            VRLog.Info("Core", $"User presence lost ({_awaySignal}) — recovery sweep armed for re-don.");
        }
        else if (!away && _away)
        {
            _away = false;
            float gap = now - _awaySince;
            if (gap < MinGapSeconds)
                return; // sensor blip — nothing to recover

            Recovered.Clear();
            VREvents.Raise(new SessionResumedEvent(gap, _awaySignal, Recovered));
            VRLog.Info("Core", $"Session resumed after {gap:F0}s away ({_awaySignal} regained): " +
                               (Recovered.Count > 0 ? string.Join(", ", Recovered)
                                                    : "nothing needed recovery") + ".");
            Recovered.Clear();
        }
    }
}
