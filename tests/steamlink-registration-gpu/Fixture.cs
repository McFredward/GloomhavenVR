using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Only a compile boundary for the unused real-XR callback. No fake Unity API,
// texture, Graphics call, fence, generator or pair/slot policy is supplied.
namespace GloomhavenVR.Core
{
    internal static class SteamLinkMaskRuntime
    {
        internal static bool CanRender(Camera camera, ProbePolicy policy, PayloadOutput output) => false;
    }
}

public static class RegistrationGpuFixture
{
    private const int Width = 688, Height = 548;
    private static int _checks;
    private static string _evidence = string.Empty;
    private sealed class Output
    {
        internal string Name = string.Empty;
        internal RenderTexture Surface = null!;
        internal byte[] Expected = null!;
        internal RegistrationSourceRecord Record = null!;
        internal bool FlipNegative;
    }
    private sealed class SlotObservation
    {
        internal object Slot = null!;
        internal object? Context;
        internal GraphicsFence Fence;
        internal byte[] Left = null!;
        internal byte[] Right = null!;
    }
    [Serializable] private sealed class Event
    {
        public string name = string.Empty, detail = string.Empty, expected_file = string.Empty;
        public string actual_file = string.Empty, source_file = string.Empty, expected_sha256 = string.Empty;
        public string actual_sha256 = string.Empty, eye = string.Empty, session = string.Empty;
        public ulong pair_id;
        public int mismatched_bytes, max_error, checks, pending_observed, unmodified_pending_checks, texture_id;
        public bool negative, srgb_write, source_srgb, target_srgb;
    }

    public static IEnumerator Run(string evidence)
    {
        _evidence = evidence;
        Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A real graphics device is required");
        Check(SystemInfo.supportsGraphicsFence, "Actual CPU graphics fence support is required");
        Emit(new Event { name = "graphics", detail = Application.unityVersion + "; " + SystemInfo.graphicsDeviceType
            + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceVersion + "; color="
            + QualitySettings.activeColorSpace + "; uv_top=" + SystemInfo.graphicsUVStartsAtTop });
        var outputs = new List<Output>();
        RegistrationTextures? owner = null;
        RenderTexture? initialTarget = RenderTexture.active;
        bool initialSrgb = GL.sRGBWrite;
        int pendingObserved = 0, pendingChecked = 0;
        try
        {
            owner = new RegistrationTextures();
            Check(owner.ActiveContext == null && owner.CompletedPairs == 0, "Cold construction does not publish an eye pair");
            Check(!owner.TryBeginEye(ProbeEye.Left, 0, out _), "Cold owner refuses output before Update preparation");
            for (long cohort = 1; cohort <= 3; cohort++)
            {
                var before = Pending(owner);
                pendingObserved += before.Count;
                owner.Prepare(cohort);
                pendingChecked += CheckPendingUnchanged(before);
                var context = owner.ActiveContext;
                Check(context != null, "Three real slots admit the first three completed pairs without an artificial wait");
                CapturePair(owner, cohort, outputs, cohort == 1);
            }
            var finalBefore = Pending(owner);
            pendingObserved += finalBefore.Count;
            owner.Prepare(4);
            pendingChecked += CheckPendingUnchanged(finalBefore);
            Check(owner.CompletedPairs == 3, "Only completed stereo pairs become completed receipts");
            Check(owner.TakeCompletedReceipt(out RegistrationSourceRecord? left, out RegistrationSourceRecord? right)
                && left != null && right != null && left.PairId == right.PairId && left.Session == right.Session,
                "A completed receipt is the same frozen left/right pair");
            if (owner.ActiveContext != null)
            {
                // No eye begins in this cohort. Its next Update may discard it.
                owner.Prepare(5);
                Check(owner.CompletedPairs == 3, "An unrendered prepared cohort is discarded, never called completed");
            }
            Emit(new Event { name = "pool", pending_observed = pendingObserved,
                unmodified_pending_checks = pendingChecked, detail = "Pending checked before any readback; zero means not exercised, not a fence proof" });
            // This additional fixture fence waits for all output surfaces without
            // substituting for the earlier production pending/slot checks.
            GraphicsFence finished = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            GL.Flush(); // Submit the empty Editor fixture's queue; never waits or reads pixels.
            var clock = Stopwatch.StartNew();
            while (!finished.passed)
            {
                Check(clock.Elapsed.TotalSeconds < 15, "Bounded fixture GPU completion");
                yield return null;
            }
            Emit(new Event { name = "actual_fence_passed", detail = "CPUSynchronisation; AllGPUOperations; first burst finished before reuse" });
            owner.Prepare(6);
            Check(owner.ActiveContext != null, "Passed production fences allow a later real pair to reuse a slot");
            CapturePair(owner, 6, outputs, false);
            owner.Prepare(7); // Queue retirement after the actual reuse consumers.
            owner.Dispose();
            Check(!owner.TryBeginEye(ProbeEye.Left, 8, out _) && !owner.TryBeginEye(ProbeEye.Right, 8, out _),
                "Stop closes both output admissions synchronously");
            owner = null;
            finished = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            GL.Flush();
            clock.Restart();
            while (!finished.passed)
            {
                Check(clock.Elapsed.TotalSeconds < 15, "Bounded fixture reuse completion");
                yield return null;
            }
            Emit(new Event { name = "reuse_fence_passed", detail = "Actual later pair consumers complete before readback" });
            foreach (Output output in outputs) Save(output);
            BoundaryControls();
            Emit(new Event { name = "complete", checks = _checks, detail = "Actual production upload/blit/fence; no XR callbacks or native video association" });
        }
        finally
        {
            owner?.Dispose();
            RenderTexture.active = initialTarget;
            GL.sRGBWrite = initialSrgb;
            foreach (Output output in outputs) Object.Destroy(output.Surface);
        }
    }

    private static void CapturePair(RegistrationTextures owner, long cohort, List<Output> outputs, bool flipNegative)
    {
        RegistrationPairContext context = owner.ActiveContext ?? throw new Exception("A prepared pair is required");
        foreach (ProbeEye eye in new[] { ProbeEye.Left, ProbeEye.Right })
                {
                    var pattern = RegistrationSource.PreparePattern(owner.Session, eye, context!.Phase);
                    var expected = RegistrationSource.CreateBuffers();
                    var record = RegistrationSource.Stamp(pattern, RegistrationSource.ExpectedContext(owner.Session,
                        context.PairId, cohort), expected);
                    Check(owner.TryBeginEye(eye, cohort, out Texture2D? texture) && texture != null,
                        "Production eye acquisition supplies a fully prepared texture");
                    var surface = Surface("registration-" + cohort + "-" + eye);
                    // Explicitly model the intended linear->sRGB destination state;
                    // record it rather than pretending an unknown XR state was run.
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    bool srgb = GL.sRGBWrite;
                    RenderTexture? active = RenderTexture.active;
                    PayloadOutput.BlitPreservingState(texture, surface);
                    Check(RenderTexture.active == active && GL.sRGBWrite == srgb, "Production blit restores graphics state");
                    owner.CompleteEye(eye);
                    string name = "pair-" + context.PairId + "-" + record.Eye;
                    outputs.Add(new Output { Name = name, Surface = surface, Expected = (byte[])expected.Canvas.Clone(), Record = record });
                    Emit(new Event { name = "submitted", detail = name, session = record.Session, pair_id = record.PairId,
                        eye = record.Eye, texture_id = texture!.GetInstanceID(), srgb_write = srgb,
                        source_srgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture!.graphicsFormat),
                        target_srgb = surface.sRGB });
                    if (flipNegative && eye == ProbeEye.Left)
                    {
                        var flipped = Surface("intentional vertical flip");
                        Graphics.Blit(texture, flipped, new Vector2(1f, -1f), new Vector2(0f, 1f));
                        outputs.Add(new Output { Name = name + "-flip-negative", Surface = flipped,
                            Expected = (byte[])expected.Canvas.Clone(), Record = record, FlipNegative = true });
                    }
                }
    }
    private static RenderTexture Surface(string name)
    {
        var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        { name = name, filterMode = FilterMode.Point, antiAliasing = 1, useMipMap = false };
        Check(target.Create(), "Real RGBA8 output surface creation");
        return target;
    }
    private static void Save(Output output)
    {
        RenderTexture? previous = RenderTexture.active;
        var read = new Texture2D(Width, Height, TextureFormat.RGB24, false, true);
        byte[] actual = new byte[Width * Height * 3];
        try
        {
            RenderTexture.active = output.Surface;
            read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            read.Apply(false, false);
            Color32[] bottomUp = read.GetPixels32();
            // Independent row indexing; never call the production upload helper.
            for (int row = 0; row < Height; row++)
                for (int col = 0; col < Width; col++)
                {
                    Color32 color = bottomUp[(Height - row - 1) * Width + col];
                    int at = (row * Width + col) * 3;
                    actual[at] = color.r; actual[at + 1] = color.g; actual[at + 2] = color.b;
                }
        }
        finally { RenderTexture.active = previous; Object.Destroy(read); }
        int changed = 0, maximum = 0;
        for (int i = 0; i < actual.Length; i++)
        {
            int error = Math.Abs(actual[i] - output.Expected[i]);
            if (error != 0) changed++;
            maximum = Math.Max(maximum, error);
        }
        if (output.FlipNegative) Check(changed > actual.Length / 10 && maximum > 32, "Independent pixel oracle rejects a real vertical-flip output");
        else Check(maximum <= 1, "Actual production output preserves all original RGB8 source codes within one code");
        string expectedFile = output.Name + ".expected.rgb", actualFile = output.Name + ".actual.rgb";
        string sourceFile = output.Name + ".source.json";
        File.WriteAllBytes(Path.Combine(_evidence, expectedFile), output.Expected);
        File.WriteAllBytes(Path.Combine(_evidence, actualFile), actual);
        File.WriteAllBytes(Path.Combine(_evidence, sourceFile), output.Record.ToJsonBytes());
        Emit(new Event { name = "pixels", detail = output.Name, expected_file = expectedFile,
            actual_file = actualFile, source_file = sourceFile, expected_sha256 = Hash(output.Expected),
            actual_sha256 = Hash(actual), mismatched_bytes = changed, max_error = maximum, negative = output.FlipNegative,
            session = output.Record.Session, pair_id = output.Record.PairId, eye = output.Record.Eye });
    }
    private static List<SlotObservation> Pending(RegistrationTextures owner)
    {
        var result = new List<SlotObservation>();
        var slots = (Array)typeof(RegistrationTextures).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        foreach (object slot in slots)
        {
            Type type = slot.GetType();
            if (!(bool)type.GetField("Retiring")!.GetValue(slot)!) continue;
            var fence = (GraphicsFence)type.GetField("Fence")!.GetValue(slot)!;
            if (fence.passed) continue;
            result.Add(new SlotObservation { Slot = slot, Fence = fence,
                Context = type.GetField("Context")!.GetValue(slot),
                Left = (byte[])((RegistrationSourceBuffers)type.GetField("Left")!.GetValue(slot)!).Canvas.Clone(),
                Right = (byte[])((RegistrationSourceBuffers)type.GetField("Right")!.GetValue(slot)!).Canvas.Clone() });
        }
        return result;
    }
    private static int CheckPendingUnchanged(List<SlotObservation> observations)
    {
        int checks = 0;
        foreach (SlotObservation item in observations)
        {
            if (item.Fence.passed) continue; // Completion during Prepare permits reuse.
            Type type = item.Slot.GetType();
            Check(ReferenceEquals(item.Context, type.GetField("Context")!.GetValue(item.Slot)), "An actually pending slot retains its original pair context");
            var left = (RegistrationSourceBuffers)type.GetField("Left")!.GetValue(item.Slot)!;
            var right = (RegistrationSourceBuffers)type.GetField("Right")!.GetValue(item.Slot)!;
            Check(Hash(left.Canvas) == Hash(item.Left) && Hash(right.Canvas) == Hash(item.Right), "No source bytes overwritten before their real fence passes");
            checks++;
        }
        return checks;
    }
    private static void BoundaryControls()
    {
        foreach (string scenario in new[] { "missing-eye", "duplicate-eye", "unknown-eye", "stale-cohort" })
        {
            using (var owner = new RegistrationTextures())
            {
                owner.Prepare(10);
                if (scenario == "unknown-eye") Throws(() => owner.TryBeginEye(ProbeEye.Unknown, 10, out _), "UnknownEye");
                else if (scenario == "stale-cohort") Throws(() => owner.TryBeginEye(ProbeEye.Left, 11, out _), "StaleCohort");
                else
                {
                    Check(owner.TryBeginEye(ProbeEye.Left, 10, out _), "Boundary case starts one real eye reservation");
                    owner.CompleteEye(ProbeEye.Left);
                    if (scenario == "duplicate-eye") Throws(() => owner.TryBeginEye(ProbeEye.Left, 10, out _), "DuplicateEye");
                    else Throws(() => owner.Prepare(11), "Incomplete stereo");
                }
                Check(!owner.TryBeginEye(ProbeEye.Right, 11, out _), "Faulted owner never publishes an old half-pair");
            }
            Emit(new Event { name = "boundary", detail = scenario });
        }
        using (var owner = new RegistrationTextures())
        {
            string session = owner.Session;
            owner.Dispose();
            for (int i = 0; i < 8; i++) Check(!owner.TryBeginEye(ProbeEye.Left, i, out _), "Disposed off path refuses without touching a fence");
            using (var fresh = new RegistrationTextures()) Check(fresh.Session != session, "A new run cannot reuse the retired source session");
        }
    }
    private static void Throws(Action action, string reason)
    {
        try { action(); }
        catch (InvalidOperationException error) { Check(error.Message.Contains(reason), "Expected precise production refusal: " + reason); return; }
        throw new Exception("Missing production refusal: " + reason);
    }
    private static string Hash(byte[] data)
    {
        using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); _checks++; }
    private static void Emit(Event entry) => File.AppendAllText(Path.Combine(_evidence, "runtime.jsonl"), JsonUtility.ToJson(entry) + "\n");
}
