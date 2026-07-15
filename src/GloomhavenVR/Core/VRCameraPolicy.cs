using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// SINGLE OWNER of the stereo-exclusion policy (docs/CAMERA-POLICY.md §1).
///
/// Problem this solves (hardware test #3): with the XR display running, ANY enabled
/// camera without a stereo restriction renders into the HMD (stereoTargetEye defaults
/// to Both) — the MainMenu scene's 'Main Camera' (stereo=Both, backbuffer) hijacked
/// the headset next to/instead of the VR rig. Per-feature exclusions (the old
/// FlatScreen UICamera guard) only ever covered one camera.
///
/// Policy: while VR runs and a rig head camera exists, ONLY that camera may render
/// stereo. Every other camera — including RenderTexture cameras like 'GUI 3D Camera',
/// where stereo=Both is just wasted double rendering — is forced to
/// <see cref="StereoTargetEyeMask.None"/> (renders to the main/desktop display only)
/// with implicit XR head tracking disabled. Idempotent; originals are recorded and
/// restored on VR-off / hot reload (<see cref="RestoreAll"/>) or when a camera is
/// promoted to rig head (<see cref="Reclaim"/>).
///
/// Pump: <c>VRRigDriver</c> sweeps on every scene load, after every rig (re)build,
/// and periodically (covers newly created cameras). While no rig head exists
/// (e.g. [Rig] MenuRig=false) the sweep stands down — a vanilla stereo camera is
/// better than a void HMD.
///
/// No per-frame allocations: cameras are enumerated via
/// <see cref="Camera.GetAllCameras"/> into a reused buffer; the originals map only
/// allocates when a NEW camera is first forced.
/// </summary>
internal static class VRCameraPolicy
{
    private static Camera[] _buffer = new Camera[16];
    private static readonly Dictionary<Camera, StereoTargetEyeMask> Originals = new();
    private static readonly List<Camera> Scratch = new(8);

    /// <summary>The rig head camera — the ONLY camera allowed to render stereo. Set by VRRigDriver.</summary>
    internal static Camera? AllowedHead { get; set; }

    /// <summary>
    /// Enumerate active cameras without allocating (shared scan buffer). Entries at
    /// index &gt;= the returned count are stale — do not read them.
    /// </summary>
    internal static int GetAllCamerasNonAlloc(out Camera[] cameras)
    {
        int count = Camera.allCamerasCount;
        if (_buffer.Length < count)
            _buffer = new Camera[Mathf.NextPowerOfTwo(count)];
        count = Camera.GetAllCameras(_buffer);
        cameras = _buffer;
        return count;
    }

    /// <summary>
    /// Enforce the policy on every active camera. <paramref name="reason"/> must be a
    /// constant/interned string (no per-frame formatting). Logs only when at least one
    /// camera was newly forced.
    /// </summary>
    internal static void Sweep(string reason)
    {
        if (!VRSession.IsRunning)
            return;
        Camera? head = AllowedHead;
        if (head == null)
            return; // no rig head — stand down (vanilla stereo beats a void HMD)

        int count = GetAllCamerasNonAlloc(out Camera[] cams);
        int forced = 0;
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || cam == head)
                continue;
            if (cam.stereoTargetEye == StereoTargetEyeMask.None)
                continue;

            if (!Originals.ContainsKey(cam))
                Originals.Add(cam, cam.stereoTargetEye);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            XRDevice.DisableAutoXRCameraTracking(cam, true);
            forced++;
            VRLog.Info("Core", $"Stereo policy: '{cam.name}' forced to StereoTargetEyeMask.None ({reason}; " +
                               $"head '{head.name}' keeps the HMD).");
        }

        if (forced > 0)
            VRLog.Info("Core", $"Stereo policy sweep ({reason}): forced None on {forced} camera(s), " +
                               $"{Originals.Count} tracked total, head='{head.name}'.");
    }

    /// <summary>
    /// Promote <paramref name="cam"/> to rig head: forget the policy's claim on it and
    /// return the stereo mask it had BEFORE the policy touched it (current value when
    /// it was never swept). The rig records that for teardown restore.
    /// </summary>
    internal static StereoTargetEyeMask Reclaim(Camera cam)
    {
        if (Originals.TryGetValue(cam, out StereoTargetEyeMask original))
        {
            Originals.Remove(cam);
            return original;
        }
        return cam.stereoTargetEye;
    }

    /// <summary>Drop bookkeeping for cameras destroyed by scene unloads (called on scene-load sweeps).</summary>
    internal static void PruneDead()
    {
        Scratch.Clear();
        foreach (KeyValuePair<Camera, StereoTargetEyeMask> pair in Originals)
        {
            // Unity fake-null: the managed key object survives Destroy — keep the
            // reference (needed for Dictionary.Remove) even though it compares null.
            if (pair.Key == null)
                Scratch.Add(pair.Key!);
        }
        for (int i = 0; i < Scratch.Count; i++)
            Originals.Remove(Scratch[i]);
        Scratch.Clear();
    }

    /// <summary>Restore every surviving camera to its original stereo behavior (VR off / hot reload).</summary>
    internal static void RestoreAll()
    {
        int restored = 0;
        foreach (KeyValuePair<Camera, StereoTargetEyeMask> pair in Originals)
        {
            Camera cam = pair.Key;
            if (cam == null)
                continue;
            cam.stereoTargetEye = pair.Value;
            XRDevice.DisableAutoXRCameraTracking(cam, false);
            restored++;
        }
        Originals.Clear();
        AllowedHead = null;
        if (restored > 0)
            VRLog.Info("Core", $"Stereo policy released — {restored} camera(s) restored to vanilla stereo behavior.");
    }

    /// <summary>One-line policy summary for the camera inventory log (allocates — inventory-only).</summary>
    internal static string Describe()
    {
        int live = 0;
        foreach (KeyValuePair<Camera, StereoTargetEyeMask> pair in Originals)
        {
            if (pair.Key != null)
                live++;
        }
        Camera? head = AllowedHead;
        return $"stereo forced None on {live} camera(s), head={(head != null ? $"'{head.name}'" : "NONE")}";
    }
}
