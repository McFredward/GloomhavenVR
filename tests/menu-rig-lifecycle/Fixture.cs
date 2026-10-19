using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class MenuRigLifecycleFixture
{
    [Serializable] private sealed class Report
    {
        public bool passed, actual_unity_lifecycle = true, actual_production_source_callbacks = true;
        public bool xr_executed = false, receiver_executed = false, headset_picture_proven = false;
        public bool actual_mixed_reality_owner_executed = false, native_anchor_gap_closed_source;
        public int checks, renders, swaps;
        public string unity = "", backend = "", device = "";
    }
    private static Report report = new Report();
    private static readonly FieldInfo OutputField = typeof(SteamLinkSceneRuntime).GetField("_output", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly List<Object> Owned = new List<Object>();
    private static string output = "";
    private static VRRigDriver driver = null!;
    private static Material probe = null!, ink = null!;
    private static GameObject surface = null!;
    private static RenderTexture probeTarget = null!, target = null!;
    private static Texture2D read = null!;
    private static readonly Color Background = new Color(.125f, .0625f, .03125f, 0);
    private static SceneHeadOutput? Current => (SceneHeadOutput?)OutputField.GetValue(null);
    private static Camera Head => VRRigDriver.HeadCamera!;
    private static T Keep<T>(T value) where T : Object { Owned.Add(value); return value; }
    private static void Check(bool value, string reason)
    { report.checks++; if (!value) throw new InvalidOperationException(reason); }
    private static void Stage(string text) => File.AppendAllText(Path.Combine(output, "stages.log"), text + "\n");
    private static void Near(float a, float b, string why) => Check(Mathf.Abs(a - b) < .013f, why + ": " + a + " versus " + b);
    private static Color At(RenderTexture texture)
    {
        var previous = RenderTexture.active;
        try
        {
            probe.SetVector("_PointUV", new Vector4(.5f, .5f, 0, 0));
            Graphics.Blit(texture, probeTarget, probe); RenderTexture.active = probeTarget;
            read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0, false); read.Apply(false, false);
            return read.GetPixel(16, 16);
        }
        finally { RenderTexture.active = previous; }
    }
    private static Camera Anchor(string name, float x = 0)
    {
        var camera = Keep(new GameObject(name)).AddComponent<Camera>();
        camera.transform.position = new Vector3(x, 0, -2); camera.transform.rotation = Quaternion.Euler(0, x * 5, 0);
        camera.clearFlags = CameraClearFlags.Depth; camera.cullingMask = 0; camera.depth = x;
        camera.allowHDR = true; camera.farClipPlane = 100 + x; camera.useOcclusionCulling = x % 2 == 0;
        camera.gameObject.tag = "MainCamera"; camera.enabled = true;
        NativeCameraRenderBudget.MainOverride = camera; NativeCameraRenderBudget.UseOverride = true;
        return camera;
    }
    private static void ConfigureHead()
    {
        VRRigDriver.RigRoot!.position = new Vector3(0, 0, -2); VRRigDriver.RigRoot.rotation = Quaternion.identity;
        Head.orthographic = true; Head.orthographicSize = 1; Head.aspect = 1;
        Head.allowHDR = false; Head.backgroundColor = Background; Head.targetTexture = target;
        // The physical pose input replaces an absent XR device; the production
        // tracker component, parent and camera identities are never replaced.
        Head.transform.localPosition = Vector3.zero;
    }
    private static void EnterFresh(string why)
    {
        MixedReality.TickBoundary();
        Check(Current != null && Current.Ready, why + ": source prepared");
        Check(Head.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length == 1, why + ": one coverage buffer");
        // A brand new source must see a complete explicit current Left+Right
        // frame before admitting coverage. No previous source/image is supplied.
        LoadingSourceBoundary.FrameCount++;
        foreach (var eye in new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right })
        {
            LoadingSourceBoundary.CurrentEye = eye; int before = LoadingSourceBoundary.EyeReads;
            int published = LoadingSourceBoundary.OutputCalls;
            Head.Render(); report.renders++;
            Check(LoadingSourceBoundary.EyeReads == before + 1, why + ": current warmup eye callback");
            Check(LoadingSourceBoundary.OutputCalls == published, why + ": new cohort stays ordinary for both warmup eyes");
        }
        LoadingSourceBoundary.FrameCount++;
    }
    private static void Pair(string why)
    {
        var current = Current;
        Check(current != null && current.Ready, why + ": current source is live");
        LoadingSourceBoundary.FrameCount++;
        foreach (var eye in new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right })
        {
            LoadingSourceBoundary.CurrentEye = eye;
            bool visible = (LoadingSourceBoundary.FrameCount + (int)eye) % 2 == 0;
            surface.SetActive(visible);
            Color color = visible ? (eye == Camera.MonoOrStereoscopicEye.Left ? Color.red : Color.cyan) : Background;
            ink.SetColor("_Color", new Color(color.r, color.g, color.b, 0));
            int before = LoadingSourceBoundary.EyeReads, published = LoadingSourceBoundary.OutputCalls;
            Head.Render(); report.renders++;
            Check(LoadingSourceBoundary.EyeReads == before + 1, why + ": exactly one actual current-eye callback");
            Check(ReferenceEquals(Current, current) && current!.Ready, why + ": same coverage source survives raster");
            Check(LoadingSourceBoundary.OutputCalls == published + 1, why + ": one fresh admitted output for this current eye");
            Color sample = At(target);
            Near(sample.a, visible ? 1 : 0, why + ": fresh current-eye depth coverage");
            Near(sample.r, color.r, why + ": current-eye red");
            Near(sample.g, color.g, why + ": current-eye green");
            Near(sample.b, color.b, why + ": current-eye blue");
        }
    }
    private static void StableStep(Camera head, Component tracker, Transform root, SceneHeadOutput source,
                                   int version, Vector3 position, Quaternion rotation, Vector3 scale, string why, bool scene = false)
    {
        driver.Step(scene: scene); driver.Maintain();
        // MixedReality's external owner boundary applies its current clear/SDR
        // policy after ordinary rig maintenance, matching the shipping tail order.
        if (VRRigDriver.HeadCamera != null) { Head.backgroundColor = Background; }
        MixedReality.TickBoundary();
        if (Current == null || !source.Ready) report.native_anchor_gap_closed_source = true;
        Check(ReferenceEquals(Current, source) && source.Ready, why + ": native menu anchor gap must retain current coverage source");
        Check(Head == head && VRCameraPolicy.AllowedHead == head && driver.PoseDriver == tracker,
            why + ": same owned tracked head and allowed authority");
        Check(VRRigDriver.RigRoot == root && VRRigDriver.RigPoseVersion == version,
            why + ": menu swap has no new head/pose generation");
        Check(root.position == position && Quaternion.Angle(root.rotation, rotation) < .001f && root.localScale == scale,
            why + ": rebind preserves physical rig pose and scale");
        Check(!driver.PendingRecenter && VRRigDriver.BaseWorldScale == 1, why + ": rebind never rearms arrival or changes base scale");
        Check(head.clearFlags == CameraClearFlags.SolidColor && head.stereoTargetEye == StereoTargetEyeMask.Both
            && head.cullingMask == VRLayers.ModLayerMask && head.targetTexture == target && head.allowMSAA && !head.allowHDR,
            why + ": camera clear/culling/stereo/extent permissions preserved");
        Check(head.targetTexture.width == 256 && head.targetTexture.height == 256, why + ": no resolution substitution");
        Pair(why);
    }
    public static IEnumerator Run(string evidence, string bank)
    {
        output = evidence; report.unity = Application.unityVersion;
        report.backend = SystemInfo.graphicsDeviceType.ToString(); report.device = SystemInfo.graphicsDeviceName;
        try
        {
            Check(report.backend != "Null" && QualitySettings.activeColorSpace == ColorSpace.Gamma, "real Gamma GPU fixture");
            ScenePayloadAssets.Load(bank);
            probe = Keep(new Material(Shader.Find("LoadingSourceFixture/Probe")));
            ink = Keep(new Material(Shader.Find("LoadingSourceFixture/Opaque"))); ink.SetColor("_Color", new Color(1, 0, 0, 0));
            probeTarget = Keep(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            Check(probeTarget.Create(), "numeric probe target created");
            read = Keep(new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true));
            target = Keep(new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            Check(target.Create(), "actual unchanged camera target created");
            surface = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); surface.layer = 30;
            surface.GetComponent<Renderer>().sharedMaterial = ink;
            driver = Keep(new GameObject("fixture owner")).AddComponent<VRRigDriver>(); VRRigDriver.Instance = driver;
            Stage("initial absence does not invent a native vantage or a head");
            NativeCameraRenderBudget.UseOverride = true; driver.Step();
            Check(Head == null && VRCameraPolicy.AllowedHead == null, "no first menu camera means no invented initial rig");
            Camera anchor = Anchor("Intro native anchor"); CameraController.s_CameraController.m_Camera = anchor;
            driver.Step(); ConfigureHead(); driver.PendingRecenter = false;
            var head = Head!; var tracker = driver.PoseDriver!; var root = VRRigDriver.RigRoot!;
            int version = VRRigDriver.RigPoseVersion;
            Vector3 position = root.position, scale = root.localScale; Quaternion rotation = root.rotation;
            EnterFresh("initial menu source"); surface.SetActive(true); Pair("initial current pair");
            var source = Current!;
            for (int i = 0; i < 12; i++)
            {
                Stage("native anchor loss/replacement " + i);
                if (i % 3 == 0) anchor.enabled = false;
                else if (i % 3 == 1) anchor.gameObject.SetActive(false);
                else { Object.DestroyImmediate(anchor.gameObject); Check(anchor == null, "real destroyed Unity camera has null truthiness"); }
                NativeCameraRenderBudget.MainOverride = null;
                // Native Camera.main is also absent when the component alone is
                // disabled, despite its GameObject remaining active.
                Check(Camera.main == null, "native main camera absent across exact anchor loss");
                StableStep(head, tracker, root, source, version, position, rotation, scale, "anchor gap " + i, true);
                Check(driver.Anchor == null, "dead/disabled native anchor reference retired");
                // A fresh XR pose reaches the same owned head while there is no
                // native anchor. Anchor destruction cannot freeze its transform.
                head.transform.localPosition = new Vector3(.01f * i, .005f * i, 0);
                StableStep(head, tracker, root, source, version, position, rotation, scale, "tracked gap " + i);
                Check(head.transform.position == root.TransformPoint(head.transform.localPosition), "live owned tracking-space pose during anchor absence");
                head.transform.localPosition = Vector3.zero;
                anchor = Anchor("MainMenu replacement " + i, i + 1);
                bool originalEnabled = anchor.enabled; Transform? originalParent = anchor.transform.parent;
                StableStep(head, tracker, root, source, version, position, rotation, scale, "replacement " + i);
                Check(driver.Anchor == anchor && anchor.enabled == originalEnabled && anchor.transform.parent == originalParent,
                    "live successor bound without native camera mutation");
                Near(head.depth, anchor.depth + 1, "successor native depth metadata");
                Near(head.farClipPlane, Mathf.Max(anchor.farClipPlane, 100), "successor native far metadata");
                Check(head.useOcclusionCulling == anchor.useOcclusionCulling, "successor occlusion metadata"); report.swaps++;
            }
            Stage("budget-owned suspension remains a live native reference");
            NativeCameraRenderBudget.Suspended.Add(anchor); anchor.enabled = false;
            StableStep(head, tracker, root, source, version, position, rotation, scale, "owned native suspension", true);
            Check(driver.Anchor == anchor, "mod-owned native suspension is not native disablement");
            anchor.enabled = true; NativeCameraRenderBudget.Suspended.Clear();
            Stage("live scene successor rebind and future manual recenter");
            Camera previousAnchor = anchor; previousAnchor.gameObject.tag = "Untagged";
            anchor = Anchor("Better scene native anchor", 15);
            StableStep(head, tracker, root, source, version, position, rotation, scale, "live scene replacement", true);
            Check(driver.Anchor == anchor, "scene signal rebinds better live native camera without destroying previous native camera");
            head.transform.localPosition = new Vector3(.1f, 1.6f, .2f); driver.ManualRecenter();
            Check(Vector3.Distance(head.transform.position, anchor.transform.position) < .0001f,
                "manual recenter uses successor authored vantage and current physical head offset");
            Check(VRRigDriver.RigPoseVersion == version + 1 && Head == head && driver.PoseDriver == tracker,
                "only deliberate recenter increments pose version and retains tracked head");
            // Restore a stable test viewpoint after verifying the actual recenter.
            root.position = position; root.rotation = rotation; head.transform.localPosition = Vector3.zero;
            Stage("explicit MR off closes authority while menu tracking remains alive");
            MixedReality.Enabled.Value = false; MixedReality.TickBoundary();
            Check(Current == null && !source.Ready && head == Head && driver.PoseDriver == tracker,
                "explicit MR off closes coverage without destroying the menu head");
            Check(head.GetCommandBuffers(CameraEvent.AfterForwardOpaque).Length == 0, "explicit off detaches coverage commands");
            MixedReality.Enabled.Value = true; surface.SetActive(true); EnterFresh("explicit MR on fresh cohort");
            Check(!ReferenceEquals(Current, source), "explicit reenable never reuses old coverage epoch"); Pair("explicit reenable current pair");
            Stage("scenario and map context changes retain their teardown policy");
            foreach (int kind in new[] { 1, 3, 2 })
            {
                var previousHead = Head; var previousSource = Current!; int priorVersion = VRRigDriver.RigPoseVersion;
                CameraController.s_CameraController.m_Camera = anchor; driver.Step(kind);
                Check(Head != previousHead && VRCameraPolicy.AllowedHead == Head && VRRigDriver.RigPoseVersion == priorVersion + 1,
                    "real context change routes teardown and successor builder");
                ConfigureHead(); driver.PendingRecenter = false; surface.SetActive(true); EnterFresh("context fresh cohort " + kind);
                Check(!previousSource.Ready, "context transition revokes previous camera source");
                yield return null; Check(previousHead == null, "context transition destroys previous owned native object");
                Pair("context pair " + kind);
            }
            Check(GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.StandDowns == 1, "map teardown still restores its native room ownership");
            Stage("explicit rebuild and real owned-object loss still rebuild");
            foreach (int reason in new[] { 0, 1, 2 })
            {
                var previousHead = Head; int priorVersion = VRRigDriver.RigPoseVersion;
                if (reason == 0) driver.Rebuild();
                else { Object.DestroyImmediate(reason == 1 ? Head!.gameObject : VRRigDriver.RigRoot!.gameObject); driver.Step(); }
                Check(Head != null && Head != previousHead && VRRigDriver.RigPoseVersion == priorVersion + 1,
                    "explicit rebuild/owned camera/root loss retains real rebuild semantics");
                ConfigureHead(); driver.PendingRecenter = false; surface.SetActive(true); EnterFresh("owned-object recovery " + reason);
                yield return null; Pair("owned-object recovery pair " + reason);
            }
            Stage("VR stop and actual driver shutdown revoke source synchronously");
            var stoppedHead = Head; var stoppedSource = Current!;
            VRSession.IsRunning = false; driver.Step(0); MixedReality.TickBoundary();
            Check(Head == null && VRCameraPolicy.AllowedHead == null && Current == null && !stoppedSource.Ready,
                "VR stop immediately clears head and source authority");
            yield return null; Check(stoppedHead == null, "VR stop destroys owned head");
            VRSession.IsRunning = true; driver.Step(); ConfigureHead(); driver.PendingRecenter = false;
            surface.SetActive(true); EnterFresh("restart fresh cohort"); var shutdownSource = Current!; var shutdownHead = Head;
            driver.Shutdown();
            Check(Head == null && VRCameraPolicy.AllowedHead == null && Current == null && !shutdownSource.Ready,
                "actual production OnDestroy releases camera and source authority");
            driver.Shutdown(); yield return null; Check(shutdownHead == null, "shutdown is idempotent and destroys head");
            report.passed = true;
        }
        finally
        {
            if (driver != null) driver.Shutdown();
            SteamLinkSceneRuntime.Reset(); NativeCameraRenderBudget.Suspended.Clear();
            VRCameraPolicy.AllowedHead = null;
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i]) Object.DestroyImmediate(Owned[i]);
            ScenePayloadAssets.Close();
            File.WriteAllText(Path.Combine(output, "gpu-result.json"), JsonUtility.ToJson(report, true) + "\n");
        }
    }
}
