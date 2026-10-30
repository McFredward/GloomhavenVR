using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Original UI identities select decorative depth; text, native callbacks and layout remain original.</summary>
internal static class MenuDepthDriver
{
    private enum Role { Narrative, Event, Compendium, Layer }
    private sealed class Spec
    {
        internal readonly string TypeName, Field;
        internal readonly Role Role;
        internal readonly float Depth;
        internal Spec(string type, string field, Role role, float depth)
        { TypeName = type; Field = field; Role = role; Depth = depth; }
    }
    private sealed class Binding
    {
        internal readonly MonoBehaviour Owner;
        internal readonly Spec Spec;
        internal readonly FieldInfo? Field;
        internal readonly Image? DirectImage;
        internal UIWindow? Window;
        internal Transform? Root;
        internal Sprite? PreviousOriginal;
        internal string PreviousKey = string.Empty;
        internal MenuDepthCatalogue.Artwork? Artwork;
        internal Sprite? PendingOriginal, NotedPresentation;
        internal string PendingKey = string.Empty, NotedKey = string.Empty, NotedState = string.Empty;
        internal float PendingSince;
        internal bool PendingReported;
        internal Binding(MonoBehaviour owner, Spec spec, FieldInfo field)
        { Owner = owner; Spec = spec; Field = field; Window = owner.GetComponentInParent<UIWindow>(); }
        internal Binding(UIWindow window, Image frame) { Owner = window; Window = window; Spec = FrameSpec; DirectImage = frame; }
    }
    private sealed class Owned
    {
        internal readonly MenuDepthGraphic Mesh;
        internal int Epoch;
        internal int Priority;
        internal Owned(MenuDepthGraphic mesh) { Mesh = mesh; }
    }

    // Only original typed fields. No image-name heuristic and no generic extrusion of text/input.
    private static readonly Spec[] Specs =
    {
        new("StoryImageViewer", "imageHolder", Role.Narrative, .03f),
        new("UIEventPanel", "eventImage", Role.Event, .03f),
        new("CompendiumWindow", "screenshotHolder", Role.Compendium, .01f),
        new("UICharacterStoryBox", "portrait", Role.Layer, -.018f),
        new("UICharacterStoryBox", "shieldIcon", Role.Layer, -.02f),
        new("UIReward", "rewardIcon", Role.Layer, -.02f),
        new("UIReward", "rewardSubIcon", Role.Layer, -.025f),
        new("UICampaignReward", "glows", Role.Layer, .006f),
        new("UIQuestDescription", "icon", Role.Layer, -.018f),
        new("UICharacterClassInformation", "characterIcon", Role.Layer, -.018f),
        new("UICharacterCreatorClassInformation", "characterIcon", Role.Layer, -.018f),
        new("UICharacterCreatorClassRosterSlot", "classIcon", Role.Layer, -.018f),
        new("UICharacterCreatorClassRosterSlot", "selected", Role.Layer, .006f),
        new("UIPartyCharacterEquippementSlot", "_selectionFrame", Role.Layer, .006f),
        new("UIEnhancementButtonHighlight", "frame", Role.Layer, .006f),
        new("UIPartyCharacterEnhancementAbilityCardsDisplay", "headerBackground", Role.Layer, .006f),
        new("UITab", "m_ImageTarget", Role.Layer, -.006f),
        new("UITab", "m_AdditionalImageTargets", Role.Layer, .004f),
    };
    private static readonly Spec FrameSpec = new("UIWindow", string.Empty, Role.Layer, .006f);
    private static readonly FieldInfo? StoryKey = AccessTools.Field(typeof(StoryImageViewer), "currentImage");
    private static readonly FieldInfo? EventData = AccessTools.Field(typeof(UIEventPanel), "eventData");
    private static readonly FieldInfo? CurrentSection = AccessTools.Field(typeof(CompendiumWindow), "currentSection");
    private static readonly FieldInfo? CurrentSubsection = AccessTools.Field(typeof(CompendiumWindow), "currentSubsection");
    private static readonly Dictionary<Image, Owned> OwnedImages = new();
    private static readonly List<Binding> Bindings = new(64);
    private static readonly Dictionary<MonoBehaviour, List<Binding>> OwnerBindings = new();
    private static readonly Dictionary<Type, List<(Spec Spec, FieldInfo Field)>> TypeFields = new();
    private static readonly List<MonoBehaviour> Behaviours = new(1024);
    private static readonly Dictionary<Transform, int> Roots = new();
    private static readonly HashSet<UIWindow> KnownWindows = new();
    private static readonly HashSet<Graphic> KnownGraphics = new();
    private static readonly HashSet<Graphic> PendingGraphics = new();
    private static readonly HashSet<Graphic> MovedGraphics = new();
    private static readonly HashSet<Transform> WalkedAncestors = new();
    private static readonly HashSet<MonoBehaviour> PendingOwners = new();
    private static readonly List<Transform> DeadRoots = new();
    private static readonly List<MonoBehaviour> DeadOwners = new();
    private static readonly List<Image> DeadImages = new();
    // Structural work counters are advanced only at real births, never a timed sweep.
    internal static int RootCensuses, BehaviourVisits, AncestorVisits;
    private static int _epoch, _rootEpoch;
    private static bool _running, _pumping;
    private static readonly Action CapturePump = FlushBeforeCapture;
    private static int _bindingNotes, _missingNotes;

    internal static void Tick()
    {
        if (!VRSession.IsRunning || !WorldUIConfig.ConversionActive)
        { if (_running) Shutdown(); return; }
        if (!_running)
        {
            _running = true;
            MenuDepthSources.SetDiscoveryPump(CapturePump);
        }
        MenuDepthCharacters.Tick();
        MenuDepthCards.Maintain();
        RefreshRoots();
        FlushArrivals();
        ApplyBindings();
    }

    private static void RefreshRoots()
    {
        // Native registries are small identity sets, not scene/hierarchy censuses.
        // Include hidden enabled windows: pooled content is learned before first reveal.
        ++_rootEpoch;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
            if (panels[i].Target != null) Enroll(panels[i].Target);
        foreach (UIWindow window in UIWindow.GetWindows())
        {
            if (window == null) continue;
            Enroll(window.transform);
            if (KnownWindows.Add(window))
            {
                Image frame = window.GetComponent<Image>();
                if (frame != null) AddFrame(window, frame);
            }
        }
    }

    private static void FlushBeforeCapture()
    {
        if (!_running || _pumping || !VRSession.IsRunning || !WorldUIConfig.ConversionActive) return;
        _pumping = true;
        try
        {
            // Display and asynchronous target/texture assignment may occur after
            // the last Late Tick. Existing capture admission flushes these exact
            // queued identities before its first source selection, even from an
            // empty registry. Stable draws never repeat typed binding/hierarchy work.
            MenuDepthCharacters.FlushForCapture();
            if (PendingGraphics.Count == 0 && PendingOwners.Count == 0) return;
            RefreshRoots(); FlushArrivals(); ApplyBindings();
        }
        finally { _pumping = false; }
    }

    private static void ApplyBindings()
    {
        ++_epoch;
        for (int i = 0; i < Bindings.Count; i++)
        {
            Binding binding = Bindings[i];
            if (binding.Owner == null || !binding.Owner.isActiveAndEnabled || binding.Root == null
                || !Roots.TryGetValue(binding.Root, out int rootEpoch) || rootEpoch != _rootEpoch) continue;
            UIWindow? parent = binding.Window;
            if (parent != null && !parent.IsOpen && !parent.IsVisible) continue;
            object? value = binding.DirectImage != null ? binding.DirectImage : binding.Field?.GetValue(binding.Owner);
            if (value is Image image) BindImage(binding, image);
            else if (value is IList<Image> images)
                for (int n = 0; n < images.Count; n++) BindImage(binding, images[n]);
        }
        DeadImages.Clear();
        foreach (KeyValuePair<Image, Owned> item in OwnedImages)
        {
            // Destroyed Unity objects compare equal to null but retain their dictionary key.
            if (item.Key == null || item.Value.Mesh == null) { DeadImages.Add(item.Key!); continue; }
            if (item.Value.Epoch != _epoch && item.Value.Mesh.enabled) item.Value.Mesh.enabled = false;
        }
        for (int i = 0; i < DeadImages.Count; i++) OwnedImages.Remove(DeadImages[i]);
    }

    private static bool Enroll(Transform root)
    {
        if (Roots.ContainsKey(root)) { Roots[root] = _rootEpoch; return false; }
        Roots.Add(root, _rootEpoch);
        Discover(root);
        return true;
    }

    private static void AddFrame(UIWindow window, Image image)
    {
        // A pooled frame can receive its sprite later; readiness is checked live.
        if (!OwnerBindings.TryGetValue(window, out List<Binding>? bindings))
        { bindings = new List<Binding>(); OwnerBindings.Add(window, bindings); }
        for (int i = 0; i < bindings.Count; i++) if (ReferenceEquals(bindings[i].Spec, FrameSpec)) return;
        var binding = new Binding(window, image) { Root = window.transform }; bindings.Add(binding); Bindings.Add(binding);
    }

    private static void Discover(Transform root)
    {
        ++RootCensuses;
        Behaviours.Clear(); root.GetComponentsInChildren(true, Behaviours);
        BehaviourVisits += Behaviours.Count;
        for (int i = 0; i < Behaviours.Count; i++)
        {
            MonoBehaviour owner = Behaviours[i];
            if (owner == null) continue;
            if (owner is Image || owner is RawImage) KnownGraphics.Add((Graphic)owner);
            Register(owner);
        }
    }

    // These native postfix notifications only coalesce identities. No observer
    // callback walks children or reflects fields while the game initializes them.
    internal static void ObserveGraphic(Graphic graphic, bool hierarchyChanged = false)
    {
        if (!_running || graphic == null || (graphic is not Image && graphic is not RawImage)) return;
        if (hierarchyChanged) { MovedGraphics.Add(graphic); PendingGraphics.Add(graphic); }
        else if (!KnownGraphics.Contains(graphic)) PendingGraphics.Add(graphic);
    }

    internal static void ObserveOwner(MonoBehaviour owner)
    {
        if (_running && owner != null) PendingOwners.Add(owner);
    }

    private static bool HasRoot(Transform value)
    {
        for (Transform? parent = value; parent != null; parent = parent.parent)
            if (Roots.ContainsKey(parent)) return true;
        return false;
    }

    private static void FlushArrivals()
    {
        foreach (MonoBehaviour owner in PendingOwners)
            if (owner != null && (OwnerBindings.ContainsKey(owner) || HasRoot(owner.transform) || MenuDepthCards.IsCardOwner(owner))) Register(owner);
        PendingOwners.Clear();
        WalkedAncestors.Clear();
        foreach (Graphic graphic in PendingGraphics)
        {
            if (graphic == null || (!MovedGraphics.Contains(graphic) && KnownGraphics.Contains(graphic))) continue;
            bool scoped = HasRoot(graphic.transform);
            if (!scoped && !MovedGraphics.Contains(graphic) && !MenuDepthCards.IsNativeFlatGraphic(graphic)) continue;
            KnownGraphics.Add(graphic);
            // New active prefab branches expose their original typed owner on
            // the path to the enrolled window. Existing neighbours are never rescanned.
            for (Transform? parent = graphic.transform; parent != null; parent = parent.parent)
            {
                if (WalkedAncestors.Add(parent))
                {
                    ++AncestorVisits;
                    Behaviours.Clear(); parent.GetComponents(Behaviours);
                    for (int i = 0; i < Behaviours.Count; i++)
                        if (scoped || OwnerBindings.ContainsKey(Behaviours[i]) || MenuDepthCards.IsCardOwner(Behaviours[i])) Register(Behaviours[i]);
                }
                if (Roots.ContainsKey(parent)) break;
            }
        }
        PendingGraphics.Clear(); MovedGraphics.Clear(); WalkedAncestors.Clear();
        DeadOwners.Clear();
        foreach (MonoBehaviour owner in OwnerBindings.Keys) if (owner == null) DeadOwners.Add(owner!);
        for (int i = 0; i < DeadOwners.Count; i++)
        {
            List<Binding> bindings = OwnerBindings[DeadOwners[i]];
            for (int n = 0; n < bindings.Count; n++) Bindings.Remove(bindings[n]);
            OwnerBindings.Remove(DeadOwners[i]);
        }
        DeadRoots.Clear(); foreach (Transform root in Roots.Keys) if (root == null) DeadRoots.Add(root!);
        for (int i = 0; i < DeadRoots.Count; i++) Roots.Remove(DeadRoots[i]);
        // UIWindow components can be destroyed while their Transform survives.
        KnownWindows.RemoveWhere(window => window == null);
        if (DeadRoots.Count != 0)
        {
            // Never-active pooled graphics need not receive OnDestroy. A real
            // scope retirement can prune their dead cached identities once.
            KnownGraphics.RemoveWhere(graphic => graphic == null);
        }
    }

    private static void Register(MonoBehaviour owner)
    {
        if (owner == null) return;
        MenuDepthCards.Register(owner, FindRoot(owner.transform));
        if (OwnerBindings.TryGetValue(owner, out List<Binding>? existing))
        {
            UIWindow window = owner.GetComponentInParent<UIWindow>();
            Transform? root = FindRoot(owner.transform);
            for (int i = 0; i < existing.Count; i++) { existing[i].Window = window; existing[i].Root = root; }
            return;
        }
        Type ownerType = owner.GetType();
        if (!TypeFields.TryGetValue(ownerType, out List<(Spec Spec, FieldInfo Field)>? fields))
        {
            fields = new List<(Spec, FieldInfo)>();
            for (int n = 0; n < Specs.Length; n++)
            {
                Spec spec = Specs[n]; Type? type = ownerType;
                while (type != null && type.Name != spec.TypeName) type = type.BaseType;
                if (type == null) continue;
                FieldInfo? field = AccessTools.Field(type, spec.Field);
                if (field != null) fields.Add((spec, field));
            }
            TypeFields.Add(ownerType, fields);
        }
        if (fields.Count == 0) return;
        var bindings = new List<Binding>(fields.Count);
        OwnerBindings.Add(owner, bindings);
        for (int n = 0; n < fields.Count; n++)
        { var binding = new Binding(owner, fields[n].Spec, fields[n].Field) { Root = FindRoot(owner.transform) }; bindings.Add(binding); Bindings.Add(binding); }
    }

    private static Transform? FindRoot(Transform value)
    {
        for (Transform? parent = value; parent != null; parent = parent.parent)
            if (Roots.ContainsKey(parent)) return parent;
        return null;
    }

    internal static void ForgetGraphic(Graphic graphic)
    { KnownGraphics.Remove(graphic); PendingGraphics.Remove(graphic); MovedGraphics.Remove(graphic); }

    internal static IEnumerable<MethodBase> NativeOwnerLifecycles()
    {
        var emitted = new HashSet<RuntimeMethodHandle>();
        for (int i = 0; i < Specs.Length; i++)
        {
            Type? type = typeof(StoryImageViewer).Assembly.GetType(Specs[i].TypeName)
                ?? typeof(UITab).Assembly.GetType("UnityEngine.UI." + Specs[i].TypeName);
            if (type == null) continue;
            foreach (string name in new[] { "Awake", "OnEnable" })
            {
                MethodInfo? method = AccessTools.Method(type, name, Type.EmptyTypes);
                if (method != null && method.DeclaringType?.Assembly == typeof(StoryImageViewer).Assembly
                    && emitted.Add(method.MethodHandle)) yield return method;
            }
        }
        foreach (Type type in MenuDepthCards.NativeTypes())
            foreach (string name in new[] { "Awake", "OnEnable" })
            {
                MethodInfo? method = AccessTools.Method(type, name, Type.EmptyTypes);
                if (method != null && method.DeclaringType?.Assembly == typeof(StoryImageViewer).Assembly
                    && emitted.Add(method.MethodHandle)) yield return method;
            }
    }

    private static void BindImage(Binding binding, Image image)
    {
        if (image == null || !image.isActiveAndEnabled || !image.gameObject.activeInHierarchy
            || (image.overrideSprite == null && binding.Spec.Role != Role.Layer)) return;
        if (ReferenceEquals(binding.Spec, FrameSpec) && (image.type != Image.Type.Sliced
            || image.overrideSprite == null || image.overrideSprite.border.sqrMagnitude <= 0f)) return;
        MenuDepthHeightfield? field = null;
        Vector4 depthUv = new(1f, 1f, 0f, 0f);
        if (binding.Spec.Role != Role.Layer)
        {
            Sprite? sprite = image.overrideSprite;
            if (sprite == null) return;
            Sprite? original = MenuDepthCatalogue.Original(sprite);
            if (original == null) return;
            string key = Key(binding, image, out string defaultField);
            MenuDepthCatalogue.Artwork? art;
            if (binding.PreviousOriginal == original && binding.PreviousKey == key && binding.Artwork != null) art = binding.Artwork;
            else
            {
                art = binding.Spec.Role == Role.Compendium ? MenuDepthCatalogue.GuidArt(key, sprite)
                    : defaultField.Length != 0 ? MenuDepthCatalogue.DefaultArt(defaultField, sprite)
                    : MenuDepthCatalogue.NarrativeArt(key, sprite);
                // An async new story key can precede the replacement sprite. Retain
                // native artwork until that exact original colour source arrives.
                if (binding.PreviousOriginal == original && binding.Artwork != null
                    && binding.PreviousKey != key && (art == null || binding.Artwork.source_id != art.source_id))
                    art = binding.Artwork; // The original old sprite is still visible during its async replacement.
                else
                { binding.PreviousOriginal = original; binding.PreviousKey = key; binding.Artwork = art; }
            }
            if (art == null)
            {
                Missing(binding, sprite, original, key, "source identity unavailable");
                return;
            }
            if (binding.Spec.Role != Role.Compendium)
            {
                if (image.type != Image.Type.Simple) return;
                field = MenuDepthCatalogue.Load(art);
                if (field == null) return;
                if (!MenuDepthCatalogue.TryDepthUv(art, sprite, out depthUv))
                {
                    Missing(binding, sprite, original, key, "presentation UV reconstruction unavailable");
                    return;
                }
            }
            binding.PendingOriginal = null; binding.PendingReported = false;
            Note(binding, sprite, key, "bound", art.source_id, depthUv);
        }
        if (!OwnedImages.TryGetValue(image, out Owned? owned) || owned.Mesh == null)
        {
            MenuDepthGraphic mesh = image.gameObject.AddComponent<MenuDepthGraphic>();
            owned = new Owned(mesh); OwnedImages[image] = owned;
        }
        int priority = binding.Spec.Role != Role.Layer ? 2 : ReferenceEquals(binding.Spec, FrameSpec) ? -1 : binding.Spec.TypeName == "UITab" ? 0 : 1;
        if (owned.Epoch == _epoch && owned.Priority > priority) return;
        owned.Epoch = _epoch;
        owned.Priority = priority;
        if (!owned.Mesh.enabled) owned.Mesh.enabled = true;
        owned.Mesh.ConfigureMapped(image, field, binding.Spec.Depth, depthUv);
    }

    private static void Missing(Binding binding, Sprite presentation, Sprite original, string key, string reason)
    {
        Note(binding, presentation, key, reason, string.Empty, default);
        if (binding.PendingOriginal != original || binding.PendingKey != key)
        {
            binding.PendingOriginal = original; binding.PendingKey = key;
            binding.PendingSince = Time.unscaledTime; binding.PendingReported = false;
        }
        // A source key can legitimately arrive before its picture. Only a stable,
        // visible unmatched original merits a bounded normal-level anomaly note.
        if (!binding.PendingReported && key.Length != 0 && Time.unscaledTime - binding.PendingSince >= 1f)
        {
            binding.PendingReported = true;
            if (++_missingNotes <= 4) VRLog.Note("WorldUI", $"MENU DEPTH IMAGE UNBOUND: {binding.Spec.TypeName}.{binding.Spec.Field}; "
                + reason + "; original image retained; occurrence=" + _missingNotes + ".");
        }
    }

    private static void Note(Binding binding, Sprite presentation, string key, string state, string source, Vector4 depthUv)
    {
        if (!VRLog.WantsDebug || _bindingNotes >= 32 || (binding.NotedPresentation == presentation
            && binding.NotedKey == key && binding.NotedState == state)) return;
        binding.NotedPresentation = presentation; binding.NotedKey = key; binding.NotedState = state; ++_bindingNotes;
        Sprite? original = MenuDepthCatalogue.Original(presentation);
        VRLog.Debug("WorldUI", $"MENU DEPTH IMAGE BINDING: {binding.Spec.TypeName}.{binding.Spec.Field}; state={state}; "
            + $"key='{key}'; native='{(original != null ? original.name : "<missing>")}'; colour='{presentation.name}'; "
            + $"source={source}; depthUV={depthUv}; note={_bindingNotes}/32.");
    }

    private static string Key(Binding binding, Image image, out string defaultField)
    {
        defaultField = string.Empty;
        if (binding.Spec.Role == Role.Narrative)
            return NarrativeKey(StoryKey?.GetValue(binding.Owner) as string ?? string.Empty);
        if (binding.Spec.Role == Role.Compendium)
        {
            var section = CurrentSection?.GetValue(binding.Owner) as UICompendiumButton;
            int index = CurrentSubsection?.GetValue(binding.Owner) is int value ? value : 0;
            UICompendiumButton? page = section?.Subsections != null && index >= 0 && index < section.Subsections.Count
                ? section.Subsections[index] : null;
            return (page?.GetScreenshot() ?? section?.GetScreenshot())?.SpriteReference?.AssetGUID ?? string.Empty;
        }
        UIInfoTools tools = UIInfoTools.Instance;
        if (tools == null) return string.Empty;
        Sprite? original = MenuDepthCatalogue.Original(image.overrideSprite);
        if (original == MenuDepthCatalogue.Original(tools.defaultEventImage)) { defaultField = "defaultEventImage"; return defaultField; }
        if (original == MenuDepthCatalogue.Original(tools.defaultCityEventImage)) { defaultField = "defaultCityEventImage"; return defaultField; }
        object road = EventData?.GetValue(binding.Owner)!;
        string id = road == null ? string.Empty : AccessTools.Property(road.GetType(), "NarrativeImageId")?.GetValue(road, null) as string ?? string.Empty;
        return NarrativeKey(id);
    }

    private static string NarrativeKey(string id)
    {
        // Read the same native identity/fallback without invoking logging or starting an asset load.
        UIInfoTools tools = UIInfoTools.Instance;
        if (tools == null) return string.Empty;
        NarrativeConfigUI[] configs = tools.narrativeImages;
        if (configs == null || configs.Length == 0) return string.Empty;
        for (int i = 0; i < configs.Length; i++)
            if (string.Equals(configs[i].id, id, StringComparison.OrdinalIgnoreCase)) return configs[i].id;
        return configs[0].id; // Same original fallback as UIInfoTools.GetNarrativeImageConfig.
    }

    internal static void Shutdown()
    {
        _running = false;
        MenuDepthSources.SetDiscoveryPump(null);
        MenuDepthCharacters.Shutdown();
        MenuDepthCards.Shutdown();
        foreach (KeyValuePair<Image, Owned> item in OwnedImages)
            if (item.Value.Mesh != null)
            { item.Value.Mesh.enabled = false; UnityEngine.Object.Destroy(item.Value.Mesh); }
        OwnedImages.Clear(); Bindings.Clear(); OwnerBindings.Clear(); Roots.Clear(); KnownWindows.Clear(); KnownGraphics.Clear();
        PendingGraphics.Clear(); MovedGraphics.Clear(); WalkedAncestors.Clear(); PendingOwners.Clear(); DeadRoots.Clear(); DeadOwners.Clear();
        _running = false;
        RootCensuses = BehaviourVisits = AncestorVisits = 0;
        _bindingNotes = _missingNotes = 0;
    }
}
