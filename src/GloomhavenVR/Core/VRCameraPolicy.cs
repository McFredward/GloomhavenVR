using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// SINGLE OWNER of the stereo-exclusion policy (docs/CAMERA-POLICY.md §1).
///
/// Policy (hardened after hardware test #4): while VR runs, GAME CAMERAS NEVER
/// RENDER STEREO — period. The only camera allowed to render into the HMD is the
/// rig's OWN head camera (<c>GloomhavenVR.HeadCamera</c>, created and registered by
/// <c>VRRigDriver</c> via <see cref="AllowedHead"/>). Every other camera — menu
/// cameras, the UICamera, RenderTexture cameras like 'GUI 3D Camera', late-created
/// ones like 'MainMenuVideo' — is forced to <see cref="StereoTargetEyeMask.None"/>
/// (desktop-only) with implicit XR head tracking disabled.
///
/// History: test #3 showed ANY enabled camera with stereo=Both hijacks the HMD next
/// to/instead of the rig. The first fix promoted ONE game camera to rig head
/// (Reclaim) — test #4 showed that head-tracking a GAME-owned camera lets game code
/// (menu camera writers, component toggles, VideoPlayer interactions) break pose
/// application invisibly. The rig now owns a dedicated head camera, so the
/// tracked-head special-case is gone: there is no game camera to exempt, ever.
///
/// Idempotent; originals are recorded and restored on VR-off / hot reload
/// (<see cref="RestoreAll"/>). No per-frame allocations: cameras are enumerated via
/// <see cref="Camera.GetAllCameras"/> into a reused buffer; the originals map only
/// allocates when a NEW camera is first forced.
///
/// Pump: <c>VRRigDriver</c> sweeps on every scene load, after every rig (re)build,
/// and periodically (covers newly created cameras).
/// </summary>
internal static class VRCameraPolicy
{
    private static Camera[] _buffer = new Camera[16];
    private static readonly Dictionary<Camera, StereoTargetEyeMask> Originals = new();
    private static readonly List<Camera> Scratch = new(8);

    /// <summary>
    /// The rig's OWN head camera — the only camera allowed to render stereo. Set by
    /// VRRigDriver when it builds its owned camera; null while no rig exists (then
    /// EVERY camera is forced to None — game cameras never stereo, period).
    /// </summary>
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

        int count = GetAllCamerasNonAlloc(out Camera[] cams);
        int forced = 0;
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || (head != null && cam == head))
                continue;
            if (cam.stereoTargetEye == StereoTargetEyeMask.None)
                continue;

            if (!Originals.ContainsKey(cam))
                Originals.Add(cam, cam.stereoTargetEye);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            XRDevice.DisableAutoXRCameraTracking(cam, true);
            forced++;
            VRLog.Info("Core", $"Stereo policy: '{cam.name}' forced to StereoTargetEyeMask.None ({reason}; " +
                               $"{(head != null ? $"head '{head.name}' keeps the HMD" : "no rig head — game cameras never stereo")}).");
        }

        if (forced > 0)
            VRLog.Info("Core", $"Stereo policy sweep ({reason}): forced None on {forced} camera(s), " +
                               $"{Originals.Count} tracked total, head={(head != null ? $"'{head.name}'" : "NONE")}.");
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
