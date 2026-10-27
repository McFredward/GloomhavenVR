using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Actual XR eye matrices and observation are external inputs. UI, camera rendering,
// mesh projection, target/mip resources, source borrowing and display shader are real.
internal static class MenuDepthFixture
{
    internal static Camera.MonoOrStereoscopicEye CurrentEye = Camera.MonoOrStereoscopicEye.Left;
    internal static Vector3 Left = new(-.15f, 0, 0), Right = new(.15f, 0, 0);
    internal static bool StereoEnabled = true;
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Warn(string scope, string message) => Messages.Add(message);
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera; }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel
    {
        internal RectTransform HostRect = null!;
        internal Canvas HostCanvas = null!;
        internal GameObject HostGo = null!;
        internal bool RenderHidden = false, OwnerRenderHidden = false;
    }
    internal static partial class PanelSupersample
    {
        internal sealed class Entry
        {
            internal ConvertedPanel Panel = null!;
            internal string Window = "actual fixture window";
            internal Camera Cam = null!;
            internal GameObject CamGo = null!, DisplayGo = null!;
            internal RenderTexture Rt = null!, MipRt = null!;
            internal RawImage DisplayImage = null!;
            internal Rect Frame;
            internal int RtW, RtH, Msaa = 1;
            internal long VramBytes;
            internal MenuDepth.MenuDepthDisplay? MenuDepthDisplay;
            internal long MenuDepthBytes;
            internal int MenuDepthFrame = -1, MenuDepthFailures;
            internal int NextCaptureFrame, CameraOffWhileVisible, RawQuadFrames;
            internal bool MipFallback = false;
            internal float MinContentScale = 1f, AskedFactor;
        }
        private const string Scope = "WorldUI";
        private static readonly List<Entry> Entries = new();
        private static long _vramTotal;
        private static int _completed;
        private enum ReallocTrigger { EyeGrid }
        // Eye-grid demand is an explicit fixture input. The exact production
        // ResolveRate (including dimension/cost/step-down policy) executes; only
        // its original resource factory/swap boundary is supplied here.
        private sealed class TargetLife { internal long GridVramLocked, GridVramUnlocked; internal float GridRateUnlocked = 1f; }
        private static readonly TargetLife AllocationLife = new();
        private static TargetLife LifeOf(Entry entry) => AllocationLife;
        private static float EyeGridRate(Entry entry, float scale) => 1f;
        internal static Rect AllocationFrame(Entry entry) => MenuDepthAllocationFrame(entry,entry.Frame);
        private static void Reallocate(Entry entry, Rect frame, ReallocTrigger trigger)
        {
            frame = MenuDepthAllocationFrame(entry,frame);
            int msaa = entry.Msaa;
            long budget = Math.Min(MaxPanelVramBytes,MaxTotalVramBytes - (_vramTotal - entry.VramBytes));
            ResolveRate(entry,frame,ref msaa,budget,out float rate,out int width,out int height,out long bytes);
            if (bytes > budget || (width == entry.RtW && height == entry.RtH)) return;
            ReleaseMenuDepth(entry,preserveFrame:true);
            RenderTexture old = entry.Rt, oldMip = entry.MipRt;
            var target = new RenderTexture(width,height,24); target.Create();
            var mip = new RenderTexture(width,height,0) { useMipMap=true,autoGenerateMips=false,filterMode=FilterMode.Trilinear }; mip.Create();
            _vramTotal += bytes - entry.VramBytes; entry.VramBytes = bytes;
            entry.Rt = target; entry.MipRt = mip; entry.RtW=width; entry.RtH=height;
            entry.Cam.targetTexture = target; entry.DisplayImage.texture = mip;
            old.Release(); oldMip.Release(); UnityEngine.Object.DestroyImmediate(old); UnityEngine.Object.DestroyImmediate(oldMip);
        }
        private static void NoteCaptureCompleted(Entry entry) => _completed++;
        private static void ApplyCaptureClear(Camera camera) => camera.backgroundColor = Color.clear;
        internal static void Add(Entry entry) { Entries.Add(entry); _vramTotal += entry.VramBytes; }
        internal static void Capture(Camera head)
        {
            foreach (Entry entry in Entries)
            {
                entry.MenuDepthFrame = -1; // explicit next render boundary
                SyncProjection(entry); // actual legacy geometry sync preceding head capture
            }
            CaptureMenuDepth(head);
        }
        internal static void Synchronize(Entry entry) => SyncProjection(entry);
        // Explicit editor tick boundary; native frame scheduling is not emulated
        // by a replacement renderer. Actual enabled cameras execute Camera.Render.
        internal static void Visibility(Entry entry) { entry.NextCaptureFrame = Time.frameCount; SyncVisibility(entry); }
        internal static void CaptureAgain(Camera head) => CaptureMenuDepth(head);
        internal static void Clear()
        {
            foreach (Entry entry in Entries) ReleaseMenuDepth(entry);
            Entries.Clear(); _vramTotal = 0;
        }
        internal static long ExtraBytes => _menuDepthVramTotal;
        internal static int Completed => _completed;
        internal static void ExhaustBudget() => _vramTotal = MaxTotalVramBytes;
    }
}
