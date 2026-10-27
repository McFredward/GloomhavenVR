using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BeautifyEffect;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Actual native uGUI/owner/manager identities under overloaded hierarchy load.
/// Only native startup requiring a real game scene and the conversion roster are
/// fixture inputs. No discovery, binding result, field lookup or census is stubbed.
/// </summary>
public static class MenuDepthPerformanceProof
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<GameObject> Roots = new();
    private static readonly List<UIWindow> Windows = new();
    private static readonly List<Image> Decorations = new();
    private static readonly List<RawImage> Previews = new();
    private static readonly double[] Samples = new double[4096];
    private static readonly long[] Allocations = new long[4096];
    private static readonly List<RenderTexture> Targets = new();
    private static readonly List<Material> Materials = new();
    private static Harmony? _harmony;
    private static string _output = string.Empty;
    private static int _assertions, _rootCalls, _globalCalls, _count, _warmup, _lastFrame;
    private static int _steadyRoot, _steadyGlobal, _steadyVisits, _steadyAncestors;
    private static bool _baseline;
    private static float _started;
    private static Func<long>? _allocated;
    private static Character3DDisplayManager _manager = null!;
    private static Camera _camera = null!;
    private static Transform _holder = null!;
    private static Texture2D _colour = null!;
    private static Sprite _sprite = null!;
    private static int _nativeDisplays;

    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    private static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new Exception("Native field missing: " + type + "." + name);
    private static void Set(object target, string field, object? value) => Field(target.GetType(), field).SetValue(target, value);
    private static int Counter(Type type, string name) => type.GetField(name, Static)?.GetValue(null) is int value ? value : 0;
    private static void CountRoot() => _rootCalls++;
    private static void CountGlobal() => _globalCalls++;
    private static bool SkipGameSceneStart() => false;
    private static void CountDisplay() => _nativeDisplays++;

    public static void Begin(string output, bool baseline)
    {
        _output = output; _baseline = baseline; _assertions = _rootCalls = _globalCalls = _nativeDisplays = 0;
        _count = 0; _warmup = 12; _lastFrame = -1;
        _harmony = new Harmony("GloomhavenVR.MenuDepthPerf." + typeof(MenuDepthPerformanceProof).Assembly.GetName().Name);
        _harmony.Patch(AccessTools.Method(typeof(MenuDepthDriver), "Discover"), prefix: new HarmonyMethod(typeof(MenuDepthPerformanceProof), nameof(CountRoot)));
        _harmony.Patch(AccessTools.Method(typeof(MenuDepthCharacters), "Discover"), prefix: new HarmonyMethod(typeof(MenuDepthPerformanceProof), nameof(CountGlobal)));
        _harmony.Patch(AccessTools.Method(typeof(UIWindow), "Start"), prefix: new HarmonyMethod(typeof(MenuDepthPerformanceProof), nameof(SkipGameSceneStart)));
        _harmony.Patch(AccessTools.Method(typeof(Character3DDisplayManager), "Start"), prefix: new HarmonyMethod(typeof(MenuDepthPerformanceProof), nameof(SkipGameSceneStart)));
        _harmony.Patch(AccessTools.Method(typeof(Character3DDisplayManager), "Display"), postfix: new HarmonyMethod(typeof(MenuDepthPerformanceProof), nameof(CountDisplay)));
        _harmony.Patch(AccessTools.Method(typeof(Beautify), "OnEnable"), prefix: new HarmonyMethod(typeof(MenuDepthPerformanceProof), nameof(SkipGameSceneStart)));
        _harmony.CreateClassProcessor(typeof(MenuDepthCharacterDisplayObserver)).Patch();
        Type? lifecycle = typeof(MenuDepthDriver).Assembly.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthLifecycleObserver");
        if (lifecycle != null)
        {
            _harmony.CreateClassProcessor(lifecycle).Patch();
            var methods = ((IEnumerable<MethodBase>)lifecycle.GetMethod("Targets", Static)!.Invoke(null, null)!).ToArray();
            Check(methods.Select(m => m.MethodHandle).Distinct().Count() == methods.Length, "native observer inherited methods are deduplicated by exact MethodBase");
            Check(methods.Any(m => m.DeclaringType == typeof(Graphic) && m.Name == "OnEnable"),
                "real native Graphic enable is observed");
            File.WriteAllLines(Path.Combine(output, "native-targets.txt"), methods.Select(m => m.DeclaringType!.Assembly.GetName().Name + ":" + m.DeclaringType.FullName + "." + m));
        }
        _allocated = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Static, null, Type.EmptyTypes, null) is MethodInfo allocation
            ? (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocation) : null;
        long calibrationStart = _allocated?.Invoke() ?? -1;
        byte[] allocationProbe = new byte[16384];
        long calibrationDelta = calibrationStart >= 0 ? _allocated!() - calibrationStart : -1;
        GC.KeepAlive(allocationProbe);
        File.WriteAllText(Path.Combine(output, "allocation-probe.txt"), "known_array_bytes=16384\nthread_counter_observed=" + calibrationDelta + "\n");
        if (calibrationDelta < allocationProbe.Length) _allocated = null; // Unity Mono may expose a nonfunctional zero counter.
        Application.targetFrameRate = 90;
        _colour = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        _colour.SetPixels(Enumerable.Repeat(Color.white, 64).ToArray()); _colour.Apply();
        _sprite = Sprite.Create(_colour, new Rect(0, 0, 8, 8), Vector2.one * .5f);
        BuildHierarchy(); BuildNativeManager();
        long began = Stopwatch.GetTimestamp(); MenuDepthDriver.Tick();
        File.WriteAllText(Path.Combine(output, "cold.txt"), "cold_tick_ms=" + Milliseconds(Stopwatch.GetTimestamp() - began)
            + "\nroot_censuses=" + _rootCalls + "\nglobal_censuses=" + _globalCalls + "\n");
        Check(((ICollection)typeof(MenuDepthDriver).GetField("Bindings", Static)!.GetValue(null)!).Count >= Decorations.Count
            && Decorations.Where(image => image.GetComponentInParent<UIWindow>().IsOpen).All(image => image.GetComponent<MenuDepthGraphic>() != null),
            "every preexisting native typed decorative owner is retained and every visible image binds inside the overloaded hierarchy");
        Check(Previews.All(image => CharacterSource(image) != null), "cold census registers every preexisting native pooled RawImage including inactive consumers");
        if (!baseline)
        {
            Lifecycle();
            // Timing populations must be identical to the pinned baseline, not
            // include changed visibility or leftover functional-test consumers.
            MenuDepthDriver.Shutdown(); CanvasConversion.ActivePanels.Clear(); DestroyRoots();
            LateFirstDraw();
            BuildHierarchy(); BuildNativeManager(); MenuDepthDriver.Tick();
        }
        _started = Time.unscaledTime;
    }

    private static GameObject Root(string name)
    {
        var result = new GameObject(name, typeof(RectTransform)); Roots.Add(result); return result;
    }
    private static GameObject Child(Transform parent, string name, bool active = true)
    {
        var result = new GameObject(name, typeof(RectTransform)); result.SetActive(active); result.transform.SetParent(parent, false); return result;
    }
    private static Image MakeImage(Transform parent, string name)
    {
        GameObject node = Child(parent, name); Image image = node.AddComponent<Image>();
        image.sprite = _sprite; image.raycastTarget = true; image.rectTransform.sizeDelta = new Vector2(120, 80); return image;
    }
    private static Image Quest(Transform parent, string name)
    {
        GameObject node = Child(parent, name, false); UIQuestDescription owner = node.AddComponent<UIQuestDescription>();
        Image icon = MakeImage(node.transform, "actual native quest icon"); Set(owner, "icon", icon); node.SetActive(true); Decorations.Add(icon); return icon;
    }
    private static void BuildHierarchy()
    {
        // Thirteen actual converted roots mirror the supplied 724 panel caller
        // count. Each has three native UIWindow pools and a deep mixed uGUI tree.
        // Native UI/controller classes are real; unrelated content/state is input.
        for (int p = 0; p < 13; p++)
        {
            GameObject panel = Root("converted-panel-" + p); Canvas canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.enabled = false; // no unrelated software-raster load in a CPU discovery measurement
            CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = panel.transform });
            for (int w = 0; w < 3; w++)
            {
                GameObject node = Child(panel.transform, "actual-native-window-" + w, false);
                UIWindow window = node.AddComponent<UIWindow>(); node.SetActive(true);
                Set(window, "m_CurrentVisualState", w == 0 ? UIWindow.VisualState.Shown : UIWindow.VisualState.Hidden);
                node.GetComponent<CanvasGroup>().alpha = w == 0 ? 1 : 0; Windows.Add(window);
                for (int branch = 0; branch < 12; branch++)
                {
                    Transform parent = Child(node.transform, "list-branch-" + branch).transform;
                    parent = Child(parent, "native layout wrapper").transform;
                    for (int row = 0; row < 16; row++)
                    {
                        GameObject leaf = Child(parent, "native-ui-row-" + row);
                        leaf.AddComponent<LayoutElement>();
                        if (row % 3 == 0) leaf.AddComponent<RawImage>(); else leaf.AddComponent<Image>();
                        leaf.AddComponent<Button>();
                    }
                    Quest(parent, "native quest owner");
                }
                for (int tabIndex = 0; tabIndex < 8; tabIndex++)
                {
                    GameObject tabNode = Child(node.transform, "native tab owner", false);
                    UITab tab = tabNode.AddComponent<UITab>();
                    tab.OnSelected = new UnityEngine.Events.UnityEvent(); tab.OnDeselected = new UnityEngine.Events.UnityEvent();
                    Image icon = MakeImage(tabNode.transform, "tab icon");
                    Image extra = MakeImage(tabNode.transform, "tab authored secondary layer");
                    Set(tab, "m_ImageTarget", icon); Set(tab, "m_AdditionalImageTargets", new[] { extra });
                    tabNode.SetActive(true); Decorations.Add(icon); Decorations.Add(extra);
                }
                GameObject rewardNode = Child(node.transform, "native reward owner", false);
                UIReward reward = rewardNode.AddComponent<UIReward>();
                Set(reward, "_imageSpriteLoader", rewardNode.AddComponent<SpriteMemoryManagement.ImageSpriteLoader>());
                Image rewardIcon = MakeImage(rewardNode.transform, "reward icon");
                Set(reward, "rewardIcon", rewardIcon); rewardNode.SetActive(true); Decorations.Add(rewardIcon);
            }
        }
        var census = new List<string> { "panels,windows,ui_behaviours,raw_images,typed_decoration_images" };
        int behaviours = Roots.Sum(root => root.GetComponentsInChildren<MonoBehaviour>(true).Length);
        int raw = Roots.Sum(root => root.GetComponentsInChildren<RawImage>(true).Length);
        census.Add(CanvasConversion.ActivePanels.Count + "," + Windows.Count + "," + behaviours + "," + raw + "," + Decorations.Count);
        File.WriteAllLines(Path.Combine(_output, "hierarchy.csv"), census);
    }

    private static RenderTexture Target(string name)
    {
        var target = new RenderTexture(64, 64, 24) { name = name }; target.Create(); Targets.Add(target); return target;
    }
    private static void BuildNativeManager(bool makePreviews = true)
    {
        GameObject node = Root("actual Character3DDisplayManager source camera"); node.SetActive(false);
        _camera = node.AddComponent<Camera>(); _camera.enabled = false; _camera.allowHDR = false; _camera.allowMSAA = false;
        _camera.targetTexture = Target("original native preview");
        _camera.transform.position = new Vector3(1000, 0, 0); _camera.cullingMask = 1 << 8;
        _manager = node.AddComponent<Character3DDisplayManager>();
        _holder = Child(node.transform, "actual serialized child model holder").transform;
        _holder.localPosition = new Vector3(.4f, -.45f, 2.56f);
        Set(_manager, "character3DHolder", _holder); Set(_manager, "isHidden", false);
        Beautify effect = node.AddComponent<Beautify>(); effect.enabled = false; Set(_manager, "beautify", effect);
        // Actual native Display's cached-same-original branch; no model loading,
        // gameplay character manager or platform/device service is substituted.
        Type originalType = typeof(Character3DDisplayManager).GetNestedType("Character3D", BindingFlags.NonPublic)!;
        ConstructorInfo constructor = originalType.GetConstructors(Instance).Single();
        object models = Activator.CreateInstance(constructor.GetParameters()[0].ParameterType)!;
        GameObject actor = GameObject.CreatePrimitive(PrimitiveType.Cube); actor.name = "authored native original model envelope";
        actor.transform.SetParent(_holder, false); actor.transform.localScale = new Vector3(.6f, 1.6f, .4f); actor.layer = 8;
        CharacterManager nativeActor = actor.AddComponent<CharacterManager>();
        var material = new Material(Shader.Find("Unlit/Color")) { color = Color.red }; Materials.Add(material);
        actor.GetComponent<Renderer>().sharedMaterial = material;
        Type memberType = constructor.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        object member = Activator.CreateInstance(memberType)!;
        memberType.GetProperty("CharacterManager")!.SetValue(member, nativeActor, null); ((IList)models).Add(member);
        object original = constructor.Invoke(new[] { models });
        originalType.GetProperty("TypeCharacter")!.SetValue(original, ECharacter.Brute, null);
        originalType.GetProperty("Skin")!.SetValue(original, string.Empty, null);
        Set(_manager, "character3D", original); node.SetActive(true);
        for (int i = 0; makePreviews && i < 3; i++)
        {
            GameObject consumer = Child(Windows[0].transform, "native original preview pool " + i, i != 2);
            RawImage image = consumer.AddComponent<RawImage>(); image.texture = _camera.targetTexture; Previews.Add(image);
        }
    }

    private static object? CharacterSource(RawImage image)
    {
        var list = (IEnumerable)typeof(MenuDepthCharacters).GetField("Sources", Static)!.GetValue(null)!;
        foreach (object source in list) if (ReferenceEquals(((IMenuDepthSource)source).Graphic, image)) return source;
        return null;
    }
    private static float Plane(Image image)
    {
        var mesh = image.GetComponent<MenuDepthGraphic>(); if (mesh == null || !mesh.enabled) return 0;
        using var helper = new VertexHelper();
        typeof(Image).GetMethod("OnPopulateMesh", Instance, null, new[] { typeof(VertexHelper) }, null)!.Invoke(image, new object[] { helper });
        mesh.ModifyMesh(helper); UIVertex vertex = default; helper.PopulateUIVertex(ref vertex, 0); return vertex.position.z;
    }

    private static void Lifecycle()
    {
        // Every await below is one actual production Late Tick, with no private
        // timer writes and no direct calls to a discovery/notification helper.
        Image born = Quest(Windows[0].transform, "late native owner without Awake/OnEnable"); MenuDepthDriver.Tick();
        Check(born.GetComponent<MenuDepthGraphic>() != null && Plane(born) < -.1f,
            "a late active native owner without a lifecycle method binds through its real Image enable before first render");
        int roots = _rootCalls;
        for (int i = 0; i < 40; i++) { born.gameObject.SetActive(false); born.gameObject.SetActive(true); }
        MenuDepthDriver.Tick();
        Check(_rootCalls == roots && Plane(born) < -.1f, "repeated pooled enable preserves depth without rescanning unchanged windows");
        // Move the ancestor of the Image across windows under the SAME canvas.
        // Owner itself and its image remain active, so no OnEnable can repair it.
        UIWindow oldWindow = Windows[0], newWindow = Windows[1];
        Set(oldWindow, "m_CurrentVisualState", UIWindow.VisualState.Hidden); oldWindow.GetComponent<CanvasGroup>().alpha = 0;
        Set(newWindow, "m_CurrentVisualState", UIWindow.VisualState.Shown); newWindow.GetComponent<CanvasGroup>().alpha = 1;
        born.transform.parent.SetParent(newWindow.transform, false); MenuDepthDriver.Tick();
        Check(Plane(born) < -.1f, "active same-canvas ancestor reparent changes cached closed-old to open-new native window ownership");
        GameObject outside = Root("outside all converted/native window scopes");
        born.transform.parent.SetParent(outside.transform, false); MenuDepthDriver.Tick();
        Check(!born.GetComponent<MenuDepthGraphic>().enabled,
            "an active typed-owner subtree outside every enrolled root restores flat native presentation immediately");
        born.transform.parent.SetParent(newWindow.transform, false); MenuDepthDriver.Tick();
        Check(Plane(born) < -.1f, "an active re-enrolled native subtree regains its existing depth before first render");
        Set(oldWindow, "m_CurrentVisualState", UIWindow.VisualState.Shown); oldWindow.GetComponent<CanvasGroup>().alpha = 1;

        GameObject lateNode = Child(Windows[0].transform, "late original RawImage waiting for native texture");
        RawImage late = lateNode.AddComponent<RawImage>(); MenuDepthDriver.Tick();
        Check(CharacterSource(late) == null, "an unmatched native RawImage is never guessed to be a character consumer");
        late.texture = _camera.targetTexture; MenuDepthDriver.Tick();
        object? originalSource = CharacterSource(late);
        Check(originalSource != null, "a native RawImage texture assigned after its first census binds on the first ready Late Tick");
        RenderTexture replacement = Target("native replacement target");
        _camera.targetTexture = replacement; late.texture = replacement; MenuDepthDriver.Tick();
        Check(CharacterSource(late) != null && !ReferenceEquals(CharacterSource(late), originalSource),
            "actual native target replacement retires the stale source and binds its new exact consumer identity");

        _camera.targetTexture = null;
        late.texture = replacement;
        var requestNode = Child(Windows[0].transform, "new native requester before camera target exists");
        UIQuestDescription request = requestNode.AddComponent<UIQuestDescription>();
        MenuDepthDriver.Tick();
        int censusBefore = Counter(typeof(MenuDepthCharacters), "RequestCensuses");
        for (int i = 0; i < 10; i++)
        { _manager.Display(request, ECharacter.Brute); MenuDepthDriver.Tick(); }
        Check(_nativeDisplays >= 10 && Counter(typeof(MenuDepthCharacters), "RequestCensuses") - censusBefore == 1,
            "repeated actual native Display while target is null scans each requester identity only once");
        _camera.targetTexture = replacement; MenuDepthDriver.Tick();
        Check(CharacterSource(late) != null,
            "camera target assigned after actual native Display and unchanged RawImage content binds on the first ready Late Tick");
        object stable = CharacterSource(late)!;
        RenderTexture eye = Target("fixture owned-eye identity");
        // Pin a valid existing lease identity to isolate the actual lifecycle
        // reentry guard. Successful camera rendering/restoration has independent
        // native character GPU coverage; this proof measures discovery only.
        Field(stable.GetType(), "_borrowed").SetValue(stable, eye);
        late.texture = eye; MenuDepthCharacters.Tick();
        Check(ReferenceEquals(CharacterSource(late), stable), "a source-owned borrowed eye assignment does not reentrantly retire or rebuild native discovery");
        late.texture = replacement; Field(stable.GetType(), "_borrowed").SetValue(stable, null);
        MenuDepthCharacters.Tick();
        Check(ReferenceEquals(CharacterSource(late), stable), "restored native texture retains the exact original source registration");
        // The first source remains learnable while inactive; activation never
        // relies on the old quarter-second fallback to make a missing binding.
        RawImage pooled = Previews[2]; pooled.texture = replacement; pooled.gameObject.SetActive(false);
        MenuDepthDriver.Tick(); pooled.gameObject.SetActive(true); MenuDepthDriver.Tick();
        Check(CharacterSource(pooled) != null, "a pooled native first visibility retains its exact asynchronous target binding");
        GameObject transient = Child(Windows[0].transform.parent, "transient native window", false);
        transient.AddComponent<UIWindow>(); transient.SetActive(true); MenuDepthDriver.Tick();
        int knownBefore = ((IEnumerable)typeof(MenuDepthDriver).GetField("KnownWindows", Static)!.GetValue(null)!).Cast<object>().Count();
        transient.SetActive(false); MenuDepthDriver.Tick();
        Check(((IEnumerable)typeof(MenuDepthDriver).GetField("KnownWindows", Static)!.GetValue(null)!).Cast<object>().Count() == knownBefore,
            "live inactive native windows retain pooled structural identity");
        Object.DestroyImmediate(transient.GetComponent<UIWindow>()); MenuDepthDriver.Tick();
        Check(((IEnumerable)typeof(MenuDepthDriver).GetField("KnownWindows", Static)!.GetValue(null)!).Cast<object>().Count() == knownBefore - 1,
            "component-only destroyed native windows release cached managed identities while their Transform and live inactive pools survive");
        int before = _rootCalls;
        // Its UIWindow roots remain native windows. Remove one isolated nonwindow
        // converted scope independently to prove no permanently active registration.
        GameObject solo = Root("converted-only native quest panel");
        Image soloImage = Quest(solo.transform, "native converted-only owner");
        var panel = new ConvertedPanel { Target = solo.transform }; CanvasConversion.ActivePanels.Add(panel); MenuDepthDriver.Tick();
        Check(Plane(soloImage) < -.1f, "a newly converted native root discovers all existing typed owners immediately");
        CanvasConversion.ActivePanels.Remove(panel); MenuDepthDriver.Tick();
        Check(!soloImage.GetComponent<MenuDepthGraphic>().enabled, "retired converted roots stop contributing decorative sources even while their game objects remain active");
        Check(_rootCalls == before + 1, "native scoped arrivals and reparenting never trigger unrelated hierarchy censuses");
    }

    private static void LateFirstDraw()
    {
        for (int mode = 0; mode < 2; mode++)
        {
            GameObject panel = Root("first draw world canvas"); Canvas canvas = panel.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            panel.layer = 5; panel.transform.position = new Vector3(0, 0, 2);
            CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = panel.transform });
            GameObject owner = Child(panel.transform, "native first-display window", false); owner.AddComponent<UIWindow>(); owner.SetActive(true);
            Windows.Add(owner.GetComponent<UIWindow>());
            Set(Windows[0], "m_CurrentVisualState", UIWindow.VisualState.Shown); owner.GetComponent<CanvasGroup>().alpha = 1;
            BuildNativeManager(false); _camera.backgroundColor = Color.clear; _camera.clearFlags = CameraClearFlags.SolidColor;
            MenuDepthPerfRenderStamp stamp = _camera.gameObject.AddComponent<MenuDepthPerfRenderStamp>();
            GameObject previewNode = Child(owner.transform, "late exact native consumer"); previewNode.layer = 5;
            RawImage preview = previewNode.AddComponent<RawImage>(); preview.rectTransform.sizeDelta = new Vector2(.9f, 1.4f);
            RenderTexture native = _camera.targetTexture!;
            if (mode == 0) { preview.texture = native; _camera.targetTexture = null; }
            GameObject headNode = Root("actual first physical head callback"); Camera head = headNode.AddComponent<Camera>(); head.enabled = false;
            head.cullingMask = 1 << 5; head.backgroundColor = Color.black; head.clearFlags = CameraClearFlags.SolidColor;
            head.nearClipPlane = .01f; head.targetTexture = Target("first head raster");
            head.targetTexture.Release(); head.targetTexture.width = head.targetTexture.height = 256; head.targetTexture.Create();
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = head;
            MenuDepthDriver.Tick(); // The LAST tick; neither late action below is followed by another Tick/query.
            Check(!((IEnumerable)typeof(MenuDepthSources).GetField("Sources", Static)!.GetValue(null)!).Cast<object>().Any(),
                "the late first-draw boundary starts with an actually empty source registry");
            _camera.enabled = true;
            if (mode == 0)
            {
                _camera.targetTexture = native;
                _manager.Display(preview, ECharacter.Brute); // actual original cached-model native body
                _manager.GetComponent<Beautify>().enabled = false; // native shader resources are outside this CPU/registry fixture
            }
            else preview.texture = native;
            RenderTexture? active = RenderTexture.active; RenderTexture.active = native; GL.Clear(true, true, Color.blue); RenderTexture.active = active;
            string sampled = string.Empty;
            Camera.CameraCallback sample = camera => { if (ReferenceEquals(camera, head)) sampled = preview.texture.name; };
            Camera.onPreRender += sample;
            try { Canvas.ForceUpdateCanvases(); head.Render(); }
            finally { Camera.onPreRender -= sample; }
            object? boundSource = CharacterSource(preview);
            if (boundSource != null && Field(boundSource.GetType(), "_left").GetValue(boundSource) is RenderTexture eye)
            {
                RenderTexture? previous = RenderTexture.active; RenderTexture.active = eye;
                var eyePixels = new Texture2D(eye.width, eye.height, TextureFormat.RGBA32, false);
                eyePixels.ReadPixels(new Rect(0, 0, eye.width, eye.height), 0, 0); eyePixels.Apply();
                File.WriteAllBytes(Path.Combine(_output, "native-eye-" + mode + ".png"), eyePixels.EncodeToPNG());
                File.WriteAllText(Path.Combine(_output, "first-draw-" + mode + ".txt"), "sampled_texture=" + sampled + "\nnative_eye_red_pixels=" + eyePixels.GetPixels32().Count(c => c.r > 150 && c.g < 60 && c.b < 60) + "\n");
                Object.DestroyImmediate(eyePixels); RenderTexture.active = previous;
            }
            Check(stamp.Draws == 1, "the first actual head draw flushes native Display or texture arrivals after the last Tick from an empty registry");
            Check(ReferenceEquals(preview.texture, native), "the first actual late-arrival eye restores the exact native preview target");
            RenderTexture.active = head.targetTexture;
            var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false); pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
            int red = pixels.GetPixels32().Count(c => c.r > 150 && c.g < 60 && c.b < 60);
            File.WriteAllBytes(Path.Combine(_output, "first-draw-" + mode + ".png"), pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels); RenderTexture.active = active;
            Check(red > 40, "the actual first late-arrival head raster contains the native character eye rather than the initialized flat blue target");
            MenuDepthDriver.Shutdown(); CanvasConversion.ActivePanels.Clear(); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null; DestroyRoots();
        }
    }

    public static bool Step()
    {
        if (Time.frameCount == _lastFrame) return false; _lastFrame = Time.frameCount;
        if (_warmup > 0)
        {
            MenuDepthDriver.Tick();
            if (--_warmup == 0)
            {
                _steadyRoot = _rootCalls; _steadyGlobal = _globalCalls;
                _steadyVisits = Counter(typeof(MenuDepthDriver), "BehaviourVisits");
                _steadyAncestors = Counter(typeof(MenuDepthDriver), "AncestorVisits");
                _started = Time.unscaledTime;
            }
            return false;
        }
        long allocation = _allocated?.Invoke() ?? -1;
        long began = Stopwatch.GetTimestamp(); MenuDepthDriver.Tick();
        Samples[_count] = Milliseconds(Stopwatch.GetTimestamp() - began);
        Allocations[_count] = allocation >= 0 ? _allocated!() - allocation : -1;
        _count++;
        if (!_baseline)
        {
            Check(_rootCalls == _steadyRoot, "stable overloaded native UI never performs periodic root hierarchy censuses");
            Check(_globalCalls == _steadyGlobal, "stable overloaded native UI never performs periodic global character or RawImage sweeps");
            Check(Counter(typeof(MenuDepthDriver), "BehaviourVisits") == _steadyVisits
                && Counter(typeof(MenuDepthDriver), "AncestorVisits") == _steadyAncestors,
                "stable frame work is bounded to retained typed owners and known sources without behaviour or ancestor walks");
        }
        return (_count >= 180 && Time.unscaledTime - _started >= 3f) || _count >= Samples.Length;
    }
    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    private static double Percentile(double[] sorted, double probability) => sorted[(int)Math.Floor((sorted.Length - 1) * probability)];
    public static int Finish()
    {
        var times = Samples.Take(_count).OrderBy(value => value).ToArray();
        var report = new Statistics
        {
            samples = _count, elapsedSeconds = Time.unscaledTime - _started, meanMs = times.Average(),
            p50Ms = Percentile(times, .5), p95Ms = Percentile(times, .95), p99Ms = Percentile(times, .99), maxMs = times.Last(),
            rootCensuses = _rootCalls - _steadyRoot, globalCensuses = _globalCalls - _steadyGlobal,
            globalResultArrays = 2 * (_globalCalls - _steadyGlobal),
            behaviourVisits = Counter(typeof(MenuDepthDriver), "BehaviourVisits") - _steadyVisits,
            ancestorVisits = Counter(typeof(MenuDepthDriver), "AncestorVisits") - _steadyAncestors,
            allocatedBytes = _allocated == null ? -1 : Allocations.Take(_count).Sum(),
            panels = CanvasConversion.ActivePanels.Count, nativeWindows = Windows.Count, nativeDisplayCalls = _nativeDisplays,
        };
        using (var writer = new StreamWriter(Path.Combine(_output, "samples.csv")))
        {
            writer.WriteLine("sample,driver_cpu_ms,thread_allocated_bytes");
            for (int i = 0; i < _count; i++) writer.WriteLine(i + "," + Samples[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + Allocations[i]);
        }
        File.WriteAllText(Path.Combine(_output, "statistics.json"), JsonUtility.ToJson(report, true) + "\n");
        if (_baseline)
            Check(report.rootCensuses >= 13 * 3 && report.globalCensuses >= 3,
                "pinned actual Build724 executes repeated hierarchy and global sweeps under the unchanged native workload");
        else
            Check(report.rootCensuses == 0 && report.globalCensuses == 0 && report.behaviourVisits == 0 && report.ancestorVisits == 0,
                "all stable production samples have zero global searches and zero structural census work");
        return _assertions;
    }
    [Serializable] private sealed class Statistics
    {
        public int samples, rootCensuses, globalCensuses, globalResultArrays, behaviourVisits, ancestorVisits, panels, nativeWindows, nativeDisplayCalls;
        public string allocationMetric = "calibrated thread bytes, or -1 when native Mono counter is unsupported";
        public float elapsedSeconds;
        public double meanMs, p50Ms, p95Ms, p99Ms, maxMs;
        public long allocatedBytes;
    }
    private static void DestroyRoots()
    {
        foreach (GameObject root in Roots) if (root != null) Object.DestroyImmediate(root);
        foreach (RenderTexture target in Targets) { target.Release(); Object.DestroyImmediate(target); }
        foreach (Material material in Materials) Object.DestroyImmediate(material);
        Roots.Clear(); Windows.Clear(); Decorations.Clear(); Previews.Clear(); Targets.Clear(); Materials.Clear();
    }
    public static void Dispose()
    {
        MenuDepthDriver.Shutdown(); CanvasConversion.ActivePanels.Clear();
        if (_harmony != null) _harmony.UnpatchAll(_harmony.Id);
        DestroyRoots();
        Object.DestroyImmediate(_sprite); Object.DestroyImmediate(_colour);
        File.WriteAllLines(Path.Combine(_output, "lifecycle.log"), VRLog.Messages);
    }
}
