using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// P2 diagnosis net (hardware test #4): the HMD image froze with ZERO exceptions,
/// the session FOCUSED, the game running and our log silent — silence proved
/// nothing because our logs are change-driven. This heartbeat logs a `[Core]` line
/// every 10 seconds unconditionally, so the NEXT freeze pinpoints which link died:
///
/// - frames+N     → 0 means OUR Update stopped (host GO destroyed/disabled) — but
///                  then the line wouldn't print at all: a missing heartbeat IS the
///                  signal that the mod's driver loop is dead.
/// - rigDriver    → VRRigDriver instance alive + enabled (rig lifecycle pump runs).
/// - head         → owned head camera alive/enabled + world pose; moved=False across
///                  beats while the player wears the HMD = pose application dead
///                  (TrackedPoseDriver stale / input poses frozen).
/// - display      → XRDisplaySubsystem.running=False = the compositor/HMD rendering
///                  stopped (runtime-side death, nothing pose-related will help).
/// - devices      → XR InputDevices count; a drop to 0 = input subsystem lost the
///                  controllers/HMD (SteamVR/VD input focus theft, tracking loss).
/// - hmd/L/R      → CommonUsages.isTracked per device: tracked-but-frozen pose vs
///                  untracked distinguishes "runtime stopped tracking" from "poses
///                  stopped flowing into Unity".
///
/// Allocation discipline: subsystem/device queries reuse static lists, the line is
/// built in a reused StringBuilder; the only per-beat allocation is the final log
/// string (once per 10 s). Per-frame cost between beats: one float compare.
///
/// Hosted on the mod's own `GloomhavenVR.Core` root (DontDestroyOnLoad +
/// HideAndDontSave, created by <see cref="CoreModule"/>) — NOT the BepInEx manager
/// GO, so game-side scene sweeps can't take it down (INSTALL.md still recommends
/// `HideManagerGameObject = true` for the BepInEx manager, which hosts the Plugin's
/// coroutines).
/// </summary>
internal sealed class VRHeartbeat : MonoBehaviour
{
    private const float IntervalSeconds = 10f;

    // Reused buffers — no per-beat collection allocations.
    private static readonly List<XRDisplaySubsystem> Displays = new(2);
    private static readonly List<XRInputSubsystem> InputSubsystems = new(2);
    private static readonly List<InputDevice> Devices = new(8);
    private readonly StringBuilder _sb = new(320);

    private float _nextBeat;
    private int _beat;
    private int _lastFrame;
    private Vector3 _lastHeadPos;
    private Vector3 _lastHeadEuler;

    private void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now < _nextBeat)
            return;
        _nextBeat = now + IntervalSeconds;
        Beat();
    }

    private void Beat()
    {
        _beat++;
        int frame = Time.frameCount;
        int frameDelta = frame - _lastFrame;
        _lastFrame = frame;

        StringBuilder sb = _sb;
        sb.Length = 0;
        sb.Append("Heartbeat #").Append(_beat)
          .Append(": frames+").Append(frameDelta);

        // Rig driver / owned head camera.
        Rig.VRRigDriver? driver = Rig.VRRigDriver.Instance;
        sb.Append(" | rigDriver=");
        if (driver == null)
            sb.Append("DEAD");
        else
            sb.Append(driver.isActiveAndEnabled ? "ok" : "DISABLED");

        Camera? head = Rig.VRRigDriver.HeadCamera;
        sb.Append(" head=");
        if (head == null)
        {
            sb.Append("none");
        }
        else
        {
            Transform t = head.transform;
            Vector3 pos = t.position;
            Vector3 euler = t.eulerAngles;
            bool moved = (pos - _lastHeadPos).sqrMagnitude > 1e-6f
                         || (euler - _lastHeadEuler).sqrMagnitude > 1e-4f;
            _lastHeadPos = pos;
            _lastHeadEuler = euler;
            sb.Append(head.isActiveAndEnabled ? "ok" : "DISABLED")
              .Append(" pos(").Append(pos.x.ToString("F2")).Append(',')
              .Append(pos.y.ToString("F2")).Append(',')
              .Append(pos.z.ToString("F2"))
              .Append(") eul(").Append(euler.x.ToString("F0")).Append(',')
              .Append(euler.y.ToString("F0")).Append(',')
              .Append(euler.z.ToString("F0"))
              .Append(") moved=").Append(moved ? 'Y' : 'N');
        }

        // XR display subsystem.
        Displays.Clear();
        SubsystemManager.GetInstances(Displays);
        sb.Append(" | display=");
        if (Displays.Count == 0)
            sb.Append("NONE");
        else
            sb.Append(Displays[0].running ? "running" : "STOPPED");

        // XR input subsystem + devices.
        InputSubsystems.Clear();
        SubsystemManager.GetInstances(InputSubsystems);
        bool inputRunning = false;
        for (int i = 0; i < InputSubsystems.Count; i++)
            inputRunning |= InputSubsystems[i].running;
        Devices.Clear();
        InputDevices.GetDevices(Devices);
        sb.Append(" input=").Append(InputSubsystems.Count == 0 ? "NONE" : inputRunning ? "running" : "STOPPED")
          .Append(" devices=").Append(Devices.Count);

        // Per-device tracking state.
        AppendTracked(sb, " hmd=", XRNode.Head);
        AppendTracked(sb, " L=", XRNode.LeftHand);
        AppendTracked(sb, " R=", XRNode.RightHand);

        VRLog.Info("Core", sb.ToString());
    }

    private static void AppendTracked(StringBuilder sb, string label, XRNode node)
    {
        sb.Append(label);
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            sb.Append("invalid");
            return;
        }
        bool tracked = device.TryGetFeatureValue(CommonUsages.isTracked, out bool value) && value;
        sb.Append(tracked ? "tracked" : "UNTRACKED");
    }
}
