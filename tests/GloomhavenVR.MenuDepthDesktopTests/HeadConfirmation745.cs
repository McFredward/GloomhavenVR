using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    [Serializable] public sealed class NativeButton745
    {
        public int imageType;
        public float[] border = Array.Empty<float>(), pivot = Array.Empty<float>(), size = Array.Empty<float>();
        public float ppu;
    }

    private static RectTransform ConfirmationCanvas745(string name, int layer, Camera camera, Vector2 size, Vector3 position)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)); node.layer = layer;
        var rect = (RectTransform)node.transform; rect.sizeDelta = size; rect.position = position; rect.localScale = Vector3.one * .001f;
        var canvas = node.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.sortingOrder = 40;
        return rect;
    }

    // The original game font, material, sprite and actual uGUI Button execute.
    // This boundary supplies an already-admitted producer and a native callback
    // listener; it neither instantiates gameplay controllers nor substitutes a
    // computed AA factor for the real coordinator and final head draw.
    public static IEnumerator HeadConfirmation745(string output, Action<int> complete)
    {
        _count = 0; DesktopEyeInput.Available = true;
        using (var scene = new Scene())
        using (var font = new NativeFont742(output))
        {
            MenuDepthCharacters.Shutdown(); scene.HideSpatial730();
            foreach (var graphic in scene.RootA.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            foreach (var graphic in scene.RootB.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            scene.Screen.GetComponent<Renderer>().enabled = false;
            scene.HeadTarget.Release(); scene.HeadTarget.width = 640; scene.HeadTarget.height = 480; scene.HeadTarget.Create();
            scene.Head.orthographic = false; scene.Head.fieldOfView = 60; scene.Head.aspect = 4f / 3;
            scene.Head.nearClipPlane = .01f; scene.Head.farClipPlane = 20;
            scene.OriginalA.cullingMask = 1 << 24; scene.OriginalA.targetTexture = scene.Ui; scene.OriginalA.enabled = false;
            var owner = ConfirmationCanvas745("original producer", 24, scene.OriginalA, new Vector2(1600, 1000), new Vector3(0, 0, 2));
            var display = ConfirmationCanvas745("original producer photograph", 27, scene.Head, owner.rect.size, owner.position);
            var image = display.gameObject.AddComponent<RawImage>(); image.texture = scene.Ui;
            var entry = PanelSupersample.BindHead(owner, scene.OriginalA, scene.Ui, image);
            // Reproduce the supplied477MiB mono-session refusal without changing
            // either production budget or invoking a second admission port.
            PanelSupersample.MonoBudget(477L * 1024 * 1024);
            var member = ConfirmationCanvas745("Confirmation Box_pc", 23, scene.OriginalA, new Vector2(700, 440), owner.position - Vector3.forward * .032f);
            member.GetComponent<Canvas>().sortingOrder = 41; Object.DontDestroyOnLoad(member.gameObject);
            string persistentScene = member.gameObject.scene.name;
            var memberPanel = PanelSupersample.MemberPanel(member);
            // A leading hidden decoration is a real Graphic but cannot witness
            // visible content. The later native button must still admit AA.
            var hidden = new GameObject("hidden original decoration group", typeof(RectTransform), typeof(CanvasGroup));
            hidden.layer = 23; hidden.transform.SetParent(member, false); hidden.GetComponent<CanvasGroup>().alpha = 0;
            var hiddenInk = new GameObject("hidden leading original Graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hiddenInk.layer = 23; hiddenInk.transform.SetParent(hidden.transform, false);
            var plate = new GameObject("original small dialog plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plate.layer = 23; plate.transform.SetParent(member, false); var plateImage = plate.GetComponent<Image>();
            plateImage.color = new Color(.07f, .07f, .07f); plateImage.rectTransform.sizeDelta = member.rect.size;
            string inputs = Path.Combine(Path.GetDirectoryName(output)!, "native-tmp-inputs");
            var authored = JsonUtility.FromJson<NativeButton745>(File.ReadAllText(Path.Combine(inputs, "button-sprite.json")));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); texture.LoadImage(File.ReadAllBytes(Path.Combine(inputs, "button-sprite.png")));
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(authored.pivot[0], authored.pivot[1]),
                authored.ppu, 0, SpriteMeshType.FullRect, new Vector4(authored.border[0], authored.border[1], authored.border[2], authored.border[3]));
            var buttonNode = new GameObject("original native confirmation button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonNode.layer = 23; buttonNode.transform.SetParent(member, false);
            var buttonImage = buttonNode.GetComponent<Image>(); buttonImage.sprite = sprite; buttonImage.type = (Image.Type)authored.imageType;
            buttonImage.rectTransform.sizeDelta = new Vector2(307, 65); buttonImage.rectTransform.anchoredPosition = new Vector2(0, -125);
            var button = buttonNode.GetComponent<Button>(); button.targetGraphic = buttonImage; button.transition = Selectable.Transition.None;
            int callbacks = 0; button.onClick.AddListener(() => callbacks++);
            font.Label(buttonNode.transform, "original native confirmation button text", "Continue", new Vector2(307, 65), Vector2.zero, 30);
            font.Label(member, "original native confirmation description", "Confirm this choice?", new Vector2(640, 100), new Vector2(0, 65), 34);
            Vector3 originalPosition = member.position; Vector2 originalSize = member.sizeDelta;
            GameObject? reference = null; var scratchIds = new System.Collections.Generic.HashSet<int>();
            double filteredError = 0, rawError = 0, minimumCorrelation = 1; long edgePixels = 0;
            try
            {
                Canvas.ForceUpdateCanvases();
                PanelSupersample.BindConfirmationMember(entry.Panel, memberPanel);
                Check(PanelSupersample.MemberCaptured(memberPanel) && MenuDepthSources.HasContent(owner),
                    "owned native confirmation joins the exact producer head route at an already exhausted mono budget");
                Check(PanelSupersample.OwnsPanelLayers(memberPanel) && PanelSupersample.OwnsPanelLayers(entry.Panel),
                    "the actual generic conversion and mono admission seam respects its captured native member");
                Check(PanelSupersample.ConsiderAdmissions() == 0,
                    "actual generic admission never requests a second confirmation allocation at the exhausted mono budget");
                Check(!MenuDepthSources.HasContent(member) && !MenuDepthSources.HasContent(scene.RootB),
                    "an owned confirmation never enrolls itself or an unrelated window");
                Check(!member.IsChildOf(owner) && member.gameObject.scene.name == persistentScene && persistentScene == "DontDestroyOnLoad",
                    "native confirmation grouping preserves its original independent persistent home");
                reference = CloneUi730(member, Matrix4x4.identity);
                for (int pose = 0; pose < 8; pose++)
                {
                    int frame = Time.frameCount; while (Time.frameCount == frame) yield return null;
                    // Real subpixel translations plus two substantially oblique
                    // views keep both first-draw and movement quality observable.
                    Vector3 center = pose < 6 ? new Vector3(pose * .00317f, pose * .00113f, pose * .013f)
                        : new Vector3(pose == 6 ? .85f : -.85f, .12f, .45f);
                    Quaternion rotation = Quaternion.LookRotation(member.position - center, Vector3.up);
                    Vector3 left = center - rotation * Vector3.right * .032f, right = center + rotation * Vector3.right * .032f;
                    scene.Head.SetStereoViewMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(left, rotation, Vector3.one).inverse);
                    scene.Head.SetStereoViewMatrix(Camera.StereoscopicEye.Right, Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(right, rotation, Vector3.one).inverse);
                    for (int eye = 0; eye < 2; eye++)
                    {
                        scene.Head.cullingMask = 1 << 27; scene.Render(eye != 0);
                        Check(entry.MenuDepthDisplay != null && entry.MenuDepthFailures == 0,
                            "owned native confirmation joins the exact producer head route at an already exhausted mono budget");
                        RenderTexture? scratch = FilterTarget742();
                        Check(scratch != null && MenuDepthHeadRaster.SamplesPerAxis >= 1,
                            "native confirmation executes an actual head AA raster");
                        scratchIds.Add(scratch!.GetInstanceID());
                        var actual = Read(scene.HeadTarget, Path.Combine(output, "confirmation-" + pose + "-" + eye + ".png"));
                        VRRigDriver.HeadCamera = null; scene.Head.cullingMask = 1 << 26;
                        scene.Head.Render(); var raw = Read(scene.HeadTarget, Path.Combine(output, "direct-" + pose + "-" + eye + ".png"));
                        var expected = SupersampledReference742(scene, Path.Combine(output, "reference-" + pose + "-" + eye + ".png"), 4);
                        var native = SupersampledReference742(scene, Path.Combine(output, "native-detail-" + pose + "-" + eye + ".png"), 2);
                        VRRigDriver.HeadCamera = scene.Head;
                        Color32[] a = actual.GetPixels32(), r = raw.GetPixels32(), e = expected.GetPixels32();
                        double correlation = Corr730(a, native.GetPixels32(), false, out _); minimumCorrelation = Math.Min(minimumCorrelation, correlation);
                        long actualInk = 0, expectedInk = 0;
                        RectTransform buttonRect = buttonImage.rectTransform; var plane = new Plane(buttonRect.forward, buttonRect.position);
                        for (int y = 1; y < actual.height - 1; y++) for (int x = 1; x < actual.width - 1; x++)
                        {
                            int p = y * actual.width + x; actualInk += a[p].r; expectedInk += e[p].r * e[p].a / 255;
                            Ray ray = scene.Head.ViewportPointToRay(new Vector3((x + .5f) / actual.width, (y + .5f) / actual.height));
                            if (!plane.Raycast(ray, out float distance) || !buttonRect.rect.Contains(buttonRect.InverseTransformPoint(ray.GetPoint(distance)))) continue;
                            double low = 255, high = 0;
                            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            { Color32 q = e[p + dy * actual.width + dx]; double value = q.r * q.a / 255.0; low = Math.Min(low, value); high = Math.Max(high, value); }
                            if (high - low < 25) continue;
                            double target = e[p].r * e[p].a / 255.0;
                            filteredError += Math.Pow(a[p].r - target, 2); rawError += Math.Pow(r[p].r * r[p].a / 255.0 - target, 2); edgePixels++;
                        }
                        File.AppendAllText(Path.Combine(output, "confirmation-pixels.csv"), pose + "," + eye + "," + correlation + "," + actualInk + "," + expectedInk + "," + scratch.width + "," + scratch.height + "\n");
                        Check(actualInk > expectedInk * .8 && expectedInk > 10000,
                            "both final head eyes retain original native confirmation button and font pixels at every movement pose");
                        CheckTmpPixels742(actual, native, reference, scene.Head, output, 2, pose, eye);
                        Object.DestroyImmediate(actual); Object.DestroyImmediate(raw); Object.DestroyImmediate(expected); Object.DestroyImmediate(native);
                    }
                }
                File.WriteAllText(Path.Combine(output, "confirmation-aa.txt"), "filtered=" + filteredError + ";direct=" + rawError + ";edges=" + edgePixels + ";minimumCorrelation=" + minimumCorrelation + ";scratchIds=" + scratchIds.Count + "\n");
                Check(edgePixels > 500 && filteredError < rawError * .97,
                    "confirmation button edges reduce alias error against independent original native supersampled pixels");
                Check(minimumCorrelation > .99,
                    "original native confirmation text and sprite retain final head detail through oblique movement");
                Check(member.position == originalPosition && member.sizeDelta == originalSize && !member.IsChildOf(owner),
                    "head movement never changes original confirmation input geometry or its32px presentation depth");
                var eventNode = new GameObject("original native pointer event system", typeof(EventSystem));
                ExecuteEvents.Execute(buttonNode, new PointerEventData(eventNode.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                Check(callbacks == 1, "original native confirmation button retains its explicit callback exactly once");
                plateImage.enabled = false;
                scene.Head.cullingMask = 1 << 27; scene.Render(false);
                Check(PanelSupersample.MemberCaptured(memberPanel) && MenuDepthSources.HasContent(owner),
                    "hiding the former visible anchor elects a remaining native button before the next capture");
                plateImage.enabled = true;
                entry.Panel.OwnerRenderHidden = true; scene.Render(true);
                Check(!PanelSupersample.MemberCaptured(memberPanel) && member.gameObject.layer == 23 && button.isActiveAndEnabled,
                    "owner render permission loss immediately restores native member layers and retains its input");
                entry.Panel.OwnerRenderHidden = false; scene.Render(false);
                Check(PanelSupersample.MemberCaptured(memberPanel) && member.gameObject.layer == 24,
                    "restored producer permission readmits its own native confirmation at unchanged quality");
                entry.Panel.Target = scene.RootB; scene.Render(true);
                Check(!PanelSupersample.MemberCaptured(memberPanel) && member.gameObject.layer == 23 && PanelSupersample.OwnedRelationCount == 0,
                    "a pooled native identity change revokes and forgets the old exact capture binding");
                entry.Panel.Target = owner; PanelSupersample.BindConfirmationMember(entry.Panel, memberPanel);
                PanelSupersample.BindConfirmationMember(memberPanel, entry.Panel);
                Check(PanelSupersample.OwnedRelationCount == 1 && PanelSupersample.MemberCaptured(memberPanel),
                    "an attempted cyclic native ownership relation cannot replace a live producer");
                // Move a recorded node into a different native capture which
                // deliberately reuses the integer. Late release must respect
                // its new root instead of restoring this member's old layer.
                buttonNode.transform.SetParent(owner, true);
                var foreignRoot = ConfirmationCanvas745("independent pooled conversion inside the old home", 24, scene.OriginalA, new Vector2(20, 20), member.position);
                foreignRoot.SetParent(member, true); var foreignPanel = PanelSupersample.MemberPanel(foreignRoot);
                hiddenInk.transform.SetParent(foreignRoot, true);
                PanelSupersample.ForgetMember(memberPanel);
                Check(member.gameObject.layer == 23 && plate.gameObject.layer == 23
                    && buttonNode.layer == 24 && hiddenInk.layer == 24 && member.gameObject.scene.name == persistentScene,
                    "member release restores only its own native root and preserves a pooled foreign capture handoff");
                PanelSupersample.ForgetMember(foreignPanel);
                Check(!PanelSupersample.MemberCaptured(memberPanel) && !MenuDepthSources.HasContent(owner),
                    "closing an owned native confirmation revokes its head membership and pixel permission");
                // A conversion without an admitted producer keeps original
                // native visibility/input; this relation cannot manufacture AA.
                var refusedOwner = PanelSupersample.MemberPanel(scene.RootB);
                CanvasConversion.ActivePanels.Add(memberPanel);
                PanelSupersample.BindConfirmationMember(refusedOwner, memberPanel);
                Check(!PanelSupersample.MemberCaptured(memberPanel) && member.gameObject.layer == 23,
                    "an unadmitted producer cannot steal its native confirmation into a private capture");
                PanelSupersample.ResetOwnedRelations();
                Check(PanelSupersample.OwnedRelationCount == 0 && member.gameObject.layer == 23,
                    "full shutdown clears an unadmitted native member relation without touching its home");
                PanelSupersample.ForgetMember(memberPanel); PanelSupersample.ForgetMember(refusedOwner);
            }
            finally
            {
                VRRigDriver.HeadCamera = scene.Head;
                PanelSupersample.ForgetMember(memberPanel); PanelSupersample.EndHead(entry);
                if (reference != null) Object.DestroyImmediate(reference);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
                if (member != null) Object.DestroyImmediate(member.gameObject);
                if (owner != null) Object.DestroyImmediate(owner.gameObject);
                if (display != null) Object.DestroyImmediate(display.gameObject);
            }
        }
        complete(_count);
    }
}
