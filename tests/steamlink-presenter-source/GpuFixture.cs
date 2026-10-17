using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core
{
    // Unused real-XR callback boundary only; all texture/blit/fence APIs are actual Unity.
    internal static class SteamLinkMaskRuntime
    {
        internal static bool CanRender(Camera camera, ProbePolicy policy, PayloadOutput output) => false;
    }
}

// Reuse the existing bounded Editor coroutine runner; do not rerun its old fixture.
public static class RegistrationGpuFixture
{
    private static int _checks, _pendingChecks;
    private static string _evidence = string.Empty;
    private sealed class Output
    {
        internal string Name = string.Empty;
        internal byte[] Expected = null!;
        internal RegistrationSourceRecord Record = null!;
        internal RenderTexture Surface = null!;
        internal int TextureId;
    }
    private sealed class Pending
    {
        internal GraphicsFence Fence;
        internal Texture2D Left = null!, Right = null!;
        internal byte[] LeftBytes = null!, RightBytes = null!;
    }
    [Serializable] private sealed class Event
    {
        public string name = string.Empty, detail = string.Empty, profile = PresenterSource.Profile;
        public string expected_sha256 = string.Empty, actual_sha256 = string.Empty;
        public int checks, max_error, changed_bytes, pending_checks, reused_textures;
        public bool new_profile_only = true, xr_callbacks_executed = false, passthrough_authorized = false;
    }
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception("Presenter pool GPU: " + message);
    }
    private static void Emit(Event value) => File.AppendAllText(Path.Combine(_evidence, "runtime.jsonl"), JsonUtility.ToJson(value) + "\n");
    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static List<Pending> SnapshotPending(RegistrationTextures owner)
    {
        var result = new List<Pending>();
        var slots = (Array)typeof(RegistrationTextures).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        foreach (object slot in slots)
        {
            Type type = slot.GetType();
            if (!(bool)type.GetField("Retiring")!.GetValue(slot)!) continue;
            var fence = (GraphicsFence)type.GetField("Fence")!.GetValue(slot)!;
            if (fence.passed) continue;
            var left = (Texture2D)type.GetField("LeftTexture")!.GetValue(slot)!;
            var right = (Texture2D)type.GetField("RightTexture")!.GetValue(slot)!;
            result.Add(new Pending { Fence = fence, Left = left, Right = right,
                LeftBytes = left.GetRawTextureData(), RightBytes = right.GetRawTextureData() });
        }
        return result;
    }
    private static void Prepare(RegistrationTextures owner, long cohort)
    {
        List<Pending> pending = SnapshotPending(owner);
        owner.Prepare(cohort);
        foreach (Pending slot in pending)
            if (!slot.Fence.passed)
            {
                Check(Equal(slot.LeftBytes, slot.Left.GetRawTextureData()) && Equal(slot.RightBytes, slot.Right.GetRawTextureData()),
                    "Still-pending real fences prevent BOTH eye textures from overwrite");
                _pendingChecks++;
            }
    }
    private static void Capture(RegistrationTextures owner, long cohort, List<Output> outputs)
    {
        RegistrationPairContext context = owner.ActiveContext ?? throw new Exception("Prepared pair unavailable");
        Check(context.EngineFrameCohort == cohort, "actual prepared cohort");
        foreach (ProbeEye eye in new[] { ProbeEye.Left, ProbeEye.Right })
        {
            var expected = RegistrationSource.CreateBuffers();
            RegistrationSourceRecord record = PresenterSource.Stamp(RegistrationSource.PreparePattern(owner.Session, eye, context.Phase), context, expected);
            Check(owner.TryBeginEye(eye, cohort, out Texture2D? texture) && texture != null, "Actual production immutable eye texture");
            var surface = new RenderTexture(688, 548, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "Presenter-pool-" + context.PairId + "-" + eye, antiAliasing = 1, filterMode = FilterMode.Point, useMipMap = false };
            Check(surface.Create(), "Actual output RenderTexture created");
            bool srgb = GL.sRGBWrite; RenderTexture? active = RenderTexture.active;
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            bool chosen = GL.sRGBWrite;
            PayloadOutput.BlitPreservingState(texture, surface);
            Check(RenderTexture.active == active && GL.sRGBWrite == chosen, "Original blit preserves caller state");
            GL.sRGBWrite = srgb;
            owner.CompleteEye(eye);
            Check(owner.ActiveContext == context, "Both callbacks borrow same frozen published pair");
            outputs.Add(new Output { Name = "pair-" + context.PairId + "-" + record.Eye, Expected = expected.Canvas,
                Record = record, Surface = surface, TextureId = texture!.GetInstanceID() });
        }
    }
    public static IEnumerator Run(string evidence)
    {
        _evidence = evidence;
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.supportsGraphicsFence, "Actual graphics and CPU-fence support");
        Emit(new Event { name = "graphics", detail = Application.unityVersion + "; " + SystemInfo.graphicsDeviceType + "; "
            + SystemInfo.graphicsDeviceName + "; " + QualitySettings.activeColorSpace });
        using (var legacy = new RegistrationTextures())
            Check(legacy.Profile == RegistrationSource.Profile && legacy.ProfileRevision == 1 && legacy.ActiveContext == null,
                "Default constructor remains canonical v1 with no output");
        var outputs = new List<Output>();
        var owner = new RegistrationTextures(captureSubmissionMetadata: true, presenterProfile: true);
        bool oldSrgb = GL.sRGBWrite; RenderTexture? oldTarget = RenderTexture.active;
        try
        {
            Check(owner.Profile == PresenterSource.Profile && owner.ProfileRevision == 2, "Explicit new-profile owner");
            Check(!owner.TryBeginEye(ProbeEye.Left, 0, out _), "Cold owner has no source to borrow");
            for (int cohort = 1; cohort <= 3; cohort++) { Prepare(owner, cohort); Capture(owner, cohort, outputs); }
            // Finish this burst before checking actual post-fence reuse.
            GraphicsFence finish = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
            GL.Flush(); var clock = Stopwatch.StartNew();
            while (!finish.passed) { Check(clock.Elapsed.TotalSeconds < 15, "Bounded real burst completion"); yield return null; }
            Prepare(owner, 4); Capture(owner, 4, outputs);
            Prepare(owner, 5);
            Check(owner.CompletedPairs == 4, "Only the four whole stereo callback pairs are completed");
            Check(owner.TakeCompletedReceipt(out RegistrationSourceRecord? left, out RegistrationSourceRecord? right)
                && left != null && right != null && left.Profile == PresenterSource.Profile && right.Profile == PresenterSource.Profile
                && left.PairId == right.PairId && left.Session == right.Session, "Completed receipts preserve actual v2 stereo profile/tuple");
            int reused = 0;
            for (int later = 6; later < 8; later++)
                for (int earlier = 0; earlier < 6; earlier++)
                    if (outputs[later].TextureId == outputs[earlier].TextureId) { reused++; break; }
            Check(reused == 2, "Both later textures reuse actual passed-fence pool slots");
            owner.Dispose();
            Check(!owner.TryBeginEye(ProbeEye.Left, 6, out _) && !owner.TryBeginEye(ProbeEye.Right, 6, out _),
                "Explicit OFF closes both eye admissions without a timer or GPU wait");
            finish = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.AllGPUOperations);
            GL.Flush(); clock.Restart();
            while (!finish.passed) { Check(clock.Elapsed.TotalSeconds < 15, "Bounded actual reuse completion"); yield return null; }
            foreach (Output output in outputs) Save(output);
            Emit(new Event { name = "complete", checks = _checks, pending_checks = _pendingChecks, reused_textures = reused,
                detail = "Only new-profile production upload/blit/pool/fence/OFF. Still-pending checks0 means not exercised. No XR/native codec or visible passthrough proof." });
        }
        finally
        {
            owner.Dispose(); GL.sRGBWrite = oldSrgb; RenderTexture.active = oldTarget;
            foreach (Output output in outputs) Object.Destroy(output.Surface);
        }
    }
    private static void Save(Output output)
    {
        RenderTexture? prior = RenderTexture.active;
        var readback = new Texture2D(688, 548, TextureFormat.RGB24, false, false);
        try
        {
            RenderTexture.active = output.Surface;
            readback.ReadPixels(new Rect(0, 0, 688, 548), 0, 0); readback.Apply(false, false);
            byte[] raw = readback.GetRawTextureData(), topDown = new byte[raw.Length];
            for (int y = 0; y < 548; y++) Buffer.BlockCopy(raw, (547 - y) * 688 * 3, topDown, y * 688 * 3, 688 * 3);
            int max = 0, changed = 0;
            for (int i = 0; i < raw.Length; i++) { int difference = Math.Abs(topDown[i] - output.Expected[i]); max = Math.Max(max, difference); if (difference != 0) changed++; }
            Check(max <= 1, "Entire actual GPU canvas matches source within one code including guard, old headers/panels/markers");
            Check(ProbeManifest.Sha256(output.Expected) == output.Record.Manifest.CanvasSha256, "GPU expected bytes bind to exact source receipt");
            File.WriteAllBytes(Path.Combine(_evidence, output.Name + ".png"), readback.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(_evidence, output.Name + ".json"), output.Record.ToJsonBytes());
            Emit(new Event { name = "pixels", detail = output.Name, max_error = max, changed_bytes = changed,
                expected_sha256 = ProbeManifest.Sha256(output.Expected), actual_sha256 = ProbeManifest.Sha256(topDown) });
        }
        finally { RenderTexture.active = prior; Object.Destroy(readback); }
    }
}
