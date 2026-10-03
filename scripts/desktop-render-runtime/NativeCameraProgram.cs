using System;
using System.Reflection;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The exact production camera policy and transpiler run against the read-only original
// GH.Runtime.dll. This fixture has no attached OpenXR device or running campaign.
public static class NativeCameraProgram
{
    private static int count;
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new InvalidOperationException(message); }
    public static int Run()
    {
        count = 0;
        var native = new GameObject("SuspendFixture.Native").AddComponent<Camera>();
        native.tag = "MainCamera";
        var ui = new GameObject("SuspendFixture.UI").AddComponent<Camera>();
        ui.tag = "UICamera"; ui.cullingMask = 1 << 5;
        var head = new GameObject("SuspendFixture.Head").AddComponent<Camera>();
        var preview = new GameObject("SuspendFixture.Preview").AddComponent<Camera>();
        var target = new RenderTexture(64, 64, 24); target.Create(); preview.targetTexture = target;
        var originallyOff = new GameObject("SuspendFixture.AlreadyOff").AddComponent<Camera>();
        originallyOff.enabled = false;
        var events = new GameObject("SuspendFixture.Events").AddComponent<EventSystem>();
        var reader = new GameObject("SuspendFixture.NativeHover");
        var offset = new GameObject("SuspendFixture.NativeOffset");
        var movie = new GameObject("SuspendFixture.Movie").AddComponent<Camera>();
        var videoOwner = movie.gameObject.AddComponent<VideoCamera>();
        videoOwner.m_Camera = movie; VideoCamera.s_This = videoOwner;
        var nativeCanvas = new GameObject("SuspendFixture.NativeCanvas", typeof(Canvas));
        var nativeTooltip = new GameObject("SuspendFixture.NativeTooltip", typeof(Canvas));
        var canvasManager = nativeCanvas.AddComponent<CanvasManager>();
        var panel = new GameObject("SuspendFixture.Panel", typeof(RectTransform));
        var flat = new FlatScreen();
        VRSession.IsRunning = true; GloomhavenVR.Rig.VRRigDriver.HeadCamera = head;
        VRSession.Harmony = new HarmonyLib.Harmony("ghvr.camera-budget." + typeof(NativeCameraProgram).Assembly.GetName().Name);
        try
        {
            PerfConfig.UnusedCamerasSuspended = true;
            flat.Tick(false, false);
            Check(!native.enabled && !ui.enabled && !Camera.main,
                "unused native and UI cameras leave Unity's automatic camera list");
            Check(movie.enabled && !NativeCameraRenderBudget.Owns(movie),
                "native movie decoder and continuation camera remain enabled");
            Check(CanvasConversion.OriginalUi(head) == ui,
                "original UI camera recovery retains suspended native identities");
            typeof(CanvasManager).GetField("persistentUICanvas", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(canvasManager, nativeCanvas.GetComponent<Canvas>());
            typeof(CanvasManager).GetField("tooltipCanvas", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(canvasManager, nativeTooltip.GetComponent<Canvas>());
            typeof(CanvasManager).GetMethod("OnSceneLoaded", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(canvasManager,
                new object[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single });
            Check(nativeCanvas.GetComponent<Canvas>().worldCamera == ui && nativeTooltip.GetComponent<Canvas>().worldCamera == ui,
                "actual native CanvasManager binds persistent and tooltip canvases to the original suspended UI camera");
            Check(head.enabled && preview.enabled && preview.targetTexture == target,
                "head and independently consumed preview remain rendering");
            Check(!originallyOff.enabled && !NativeCameraRenderBudget.Owns(originallyOff),
                "a native disabled camera is never claimed or enabled");
            Check(NativeCameraRenderBudget.Main == native, "projection resolver preserves exact original camera identity");
            int readers = 0;
            foreach (string assemblyName in new[] { "GH.Runtime", "GH.Runtime.FirstPass", "ThirdParty" })
            {
                var assembly = Assembly.Load(assemblyName);
                using var metadata = Mono.Cecil.ModuleDefinition.ReadModule(assembly.Location);
                foreach (var type in metadata.GetTypes())
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        bool reads = false;
                        foreach (var instruction in method.Body.Instructions)
                            if (instruction.Operand is Mono.Cecil.MethodReference call
                                && call.DeclaringType.FullName == "UnityEngine.Camera" && call.Name == "get_main") reads = true;
                        if (!reads) continue;
                        readers++;
                        MethodBase original = assembly.ManifestModule.ResolveMethod(method.MetadataToken.ToInt32());
                        var patches = HarmonyLib.Harmony.GetPatchInfo(original);
                        Check(patches != null && patches.Owners.Contains(VRSession.Harmony.Id),
                            "every actual shipped managed Camera.main reader has the identity bridge: " + original);
                    }
            }
            Check(readers > 30, "native IL coverage contains original game, water/fog, UI and plugin readers");
            var game = Assembly.Load("GH.Runtime");
            var hoverType = game.GetType("HoverRegisterer", true)!;
            var hover = reader.AddComponent(hoverType);
            Check((Camera?)hoverType.GetField("m_Camera", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hover) == native,
                "actual original HoverRegisterer.Awake retains the suspended camera");
            var offsetType = game.GetType("OffsetTowardsCamera", true)!;
            offset.transform.position = new Vector3(2, 0, 0);
            var movement = offset.AddComponent(offsetType);
            offsetType.GetField("offsetAmount")!.SetValue(movement, .5f);
            offsetType.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(movement, null);
            Check(Mathf.Abs(offset.transform.position.x - 1.5f) < .0001f,
                "actual native Camera.main-dependent geometry still uses the original viewpoint");
            var mf = game.GetType("MF", true)!;
            object? miss = mf.GetMethod("FindNearestInteractableToPosition")!.Invoke(null,
                new object[] { false, (LayerMask)0, Vector3.zero });
            Check(miss == null, "actual native projection/raycast method runs while Camera.main is disabled");

            // Converted world canvases render and raycast through the retained headset camera;
            // native projection-only cameras no longer execute a separate render pass.
            var canvas = panel.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            var rect = (RectTransform)panel.transform; rect.sizeDelta = new Vector2(100, 100);
            rect.position = new Vector3(0, 0, 2); rect.localScale = Vector3.one * .01f;
            var raycaster = panel.AddComponent<GraphicRaycaster>();
            var button = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(panel.transform, false);
            var buttonRect = (RectTransform)button.transform; buttonRect.sizeDelta = new Vector2(80, 80);
            Canvas.ForceUpdateCanvases();
            head.Render(); // complete native uGUI registration/geometry on the real graphics device
            Canvas.ForceUpdateCanvases();
            var results = new System.Collections.Generic.List<RaycastResult>();
            raycaster.Raycast(new PointerEventData(events) { position = head.WorldToScreenPoint(buttonRect.position) }, results);
            Debug.Log("CAMERA INPUT PROBE: depth=" + button.GetComponent<Image>().depth + " cull=" + button.GetComponent<Image>().canvasRenderer.cull + " point=" + head.WorldToScreenPoint(buttonRect.position) + " screen=" + Screen.width + "x" + Screen.height + " hits=" + results.Count);
            Check(results.Count > 0 && results[0].gameObject == button,
                "actual world-space confirmation button remains raycastable while native cameras are suspended");
            int clicks = 0; button.GetComponent<Button>().onClick.AddListener(() => clicks++);
            ExecuteEvents.Execute(button, new PointerEventData(events), ExecuteEvents.pointerClickHandler);
            Check(clicks == 1, "confirmation click continues through the original uGUI handler");

            native.gameObject.SetActive(false); flat.Tick(false, false);
            Check(!NativeCameraRenderBudget.Owns(native) && NativeCameraRenderBudget.Main != native,
                "native object deactivation releases camera ownership and invalidates the anchor identity");
            native.gameObject.SetActive(true); flat.Tick(false, false);
            native.tag = "Untagged"; ui.tag = "MainCamera";
            Check(NativeCameraRenderBudget.Main == ui,
                "native retagging of an already suspended camera immediately changes the projection identity");
            ui.tag = "Untagged"; native.tag = "MainCamera";
            flat.ReleaseForCapture();
            Check(native.enabled && ui.enabled && Camera.main == native,
                "same-frame native capture restores camera execution before assigning its target");
            flat.Tick(false, false);
            PerfConfig.UnusedCamerasSuspended = false;
            flat.Tick(false, false);
            Check(native.enabled && ui.enabled && Camera.main == native,
                "turning the optional budget off restores both original native cameras");
            PerfConfig.UnusedCamerasSuspended = true; flat.Tick(false, false);
            VRSession.IsRunning = false; flat.Tick(false, false);
            Check(native.enabled && ui.enabled, "VR teardown fully restores original camera enablement");
        }
        finally
        {
            flat.End(); NativeCameraRenderBudget.Shutdown(); VRSession.Harmony.UnpatchSelf(); VRSession.Harmony = null;
            PerfConfig.UnusedCamerasSuspended = false; VRSession.IsRunning = true;
            preview.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target);
            foreach (GameObject go in new[] { native.gameObject, ui.gameObject, head.gameObject,
                preview.gameObject, originallyOff.gameObject, events.gameObject, reader, offset, panel, movie.gameObject, nativeCanvas, nativeTooltip })
                UnityEngine.Object.DestroyImmediate(go);
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        }
        return count;
    }
}
