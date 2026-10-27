using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Bind the original live character scene to each of its actual RawImage consumers.
/// The game shares one scene/RT among creation, roster, skin and presentation windows;
/// a differently placed consumer therefore owns a different window-relative eye view.
/// No native Display/Hide, animation, character, save or request-refcount method is called.
/// </summary>
internal static class MenuDepthCharacters
{
    private sealed class Manager
    {
        internal readonly Character3DDisplayManager Original;
        internal readonly Camera Camera;
        internal Transform? Holder;
        internal RenderTexture? Target;
        internal Manager(Character3DDisplayManager original, Camera camera) { Original = original; Camera = camera; }
    }
    private static readonly List<MenuDepthCharacterSource> Sources = new(8);
    private static readonly List<Manager> Managers = new(2);
    private static readonly HashSet<RawImage> Images = new();
    private static readonly HashSet<RawImage> PendingImages = new();
    private static readonly HashSet<Character3DDisplayManager> PendingManagers = new();
    private static readonly Dictionary<Component, RenderTexture?> DisplayRequests = new(8);
    private static readonly HashSet<Component> PendingRequests = new();
    private static readonly List<Component> DeadRequests = new(4);
    private static readonly List<RawImage> RequestImages = new(8);
    private static readonly FieldInfo? HolderField = typeof(Character3DDisplayManager).GetField(
        "character3DHolder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly Func<Character3DDisplayManager, bool>? ReadHidden = CreateHiddenReader();
    private static bool _enabled, _discovering;
    internal static int GlobalCensuses, RequestCensuses, ImageVisits;
    private static bool _reflectionReported;
    private static int _openings;

    internal static void Tick() => Update(true);
    internal static void FlushForCapture() => Update(false);

    private static void Update(bool maintainResources)
    {
        if (!VRSession.IsRunning) { Shutdown(); return; }
        if (!_enabled) { _enabled = true; Discover(); }
        RetireStale(maintainResources);
        foreach (Character3DDisplayManager manager in PendingManagers) AddManager(manager);
        PendingManagers.Clear();
        // Only the few original GUI cameras are polled. The native target can be
        // assigned after Display; its first ready frame must not wait for a timer.
        for (int i = Managers.Count - 1; i >= 0; i--)
        {
            Manager manager = Managers[i];
            if (manager.Original == null || manager.Camera == null) { Managers.RemoveAt(i); continue; }
            Transform? holder = HolderField?.GetValue(manager.Original) as Transform;
            RenderTexture target = manager.Camera.targetTexture;
            if (ReferenceEquals(holder, manager.Holder) && ReferenceEquals(target, manager.Target)) continue;
            manager.Holder = holder; manager.Target = target;
            foreach (RawImage image in Images) if (image != null) PendingImages.Add(image);
        }
        foreach (Component request in PendingRequests)
        {
            if (request == null) continue;
            ++RequestCensuses;
            RequestImages.Clear(); request.GetComponentsInChildren(true, RequestImages);
            for (int i = 0; i < RequestImages.Count; i++) QueueImage(RequestImages[i]);
        }
        PendingRequests.Clear();
        foreach (RawImage image in PendingImages) if (image != null) Bind(image);
        PendingImages.Clear();
        DeadRequests.Clear();
        foreach (Component request in DisplayRequests.Keys)
            if (request == null) DeadRequests.Add(request!);
        for (int i = 0; i < DeadRequests.Count; i++) DisplayRequests.Remove(DeadRequests[i]);
    }

    // Postfixes coalesce real native identities, without global or hierarchy
    // enumeration inside native Display/OnEnable/texture assignment callbacks.
    internal static void ObserveDisplay(Character3DDisplayManager manager, Component request)
    {
        if (!_enabled || !VRSession.IsRunning || manager == null) return;
        PendingManagers.Add(manager);
        Component key = request != null ? request : manager;
        Camera camera = manager.GetComponent<Camera>();
        RenderTexture? target = camera != null ? camera.targetTexture : null;
        if (DisplayRequests.TryGetValue(key, out RenderTexture? previous) && ReferenceEquals(previous, target)) return;
        DisplayRequests[key] = target;
        PendingRequests.Add(key);
    }

    internal static void ObserveManager(Character3DDisplayManager manager)
    { if (_enabled && VRSession.IsRunning && manager != null) PendingManagers.Add(manager); }

    internal static void ObserveImage(RawImage image)
    {
        if (!_enabled || !VRSession.IsRunning || image == null) return;
        // The eye source temporarily assigns its private RT and then restores
        // native colour. BindingAlive knows both leased identities; neither is
        // an artwork arrival and must not retire/rebuild a source reentrantly.
        MenuDepthCharacterSource? current = Find(image);
        if (current != null && current.BindingAlive) return;
        QueueImage(image);
    }

    private static void QueueImage(RawImage image)
    { Images.Add(image); PendingImages.Add(image); }

    internal static void ForgetImage(RawImage image)
    { Images.Remove(image); PendingImages.Remove(image); }

    private static void RetireStale(bool maintainResources)
    {
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            if (Sources[i].BindingAlive)
            { if (maintainResources) Sources[i].MaintainResources(Time.unscaledTime); continue; }
            RawImage? image = Sources[i].Graphic as RawImage;
            MenuDepthSources.Unregister(Sources[i]); Sources[i].Dispose(); Sources.RemoveAt(i);
            if (image != null) QueueImage(image);
        }
    }

    private static void Discover()
    {
        if (_discovering) return;
        if (HolderField == null || ReadHidden == null)
        {
            if (!_reflectionReported)
            {
                _reflectionReported = true;
                VRLog.Warn("WorldUI", "MENU DEPTH CHARACTER SOURCE unavailable: original holder/visibility fields were not found; native previews remain available.");
            }
            return;
        }
        _discovering = true;
        try
        {
            // One cold census admits preexisting inactive pools and a camera
            // hidden by native HideAll. Later births/RT assignments use real
            // lifecycle identities; elapsed time never starts a scene sweep.
            ++GlobalCensuses;
            Character3DDisplayManager[] managers = Object.FindObjectsOfType<Character3DDisplayManager>(true);
            RawImage[] images = Object.FindObjectsOfType<RawImage>(true);
            for (int i = 0; i < managers.Length; i++) AddManager(managers[i]);
            for (int i = 0; i < images.Length; i++) QueueImage(images[i]);
        }
        finally { _discovering = false; }
    }

    private static void AddManager(Character3DDisplayManager manager)
    {
        if (manager == null) return;
        for (int i = 0; i < Managers.Count; i++) if (ReferenceEquals(Managers[i].Original, manager)) return;
        Camera camera = manager.GetComponent<Camera>();
        if (camera != null) Managers.Add(new Manager(manager, camera));
    }

    private static void Bind(RawImage image)
    {
        ++ImageVisits;
        if (ReadHidden == null || Find(image) != null) return;
        Texture texture = image.texture;
        for (int i = 0; i < Managers.Count; i++)
        {
            Manager manager = Managers[i];
            if (manager.Holder == null || manager.Target == null || !ReferenceEquals(texture, manager.Target)) continue;
            var source = new MenuDepthCharacterSource(image, manager.Camera, manager.Original, manager.Holder, ReadHidden);
            Sources.Add(source); MenuDepthSources.Register(source);
            int count = ++_openings;
            if (count <= 4 || (count & (count - 1)) == 0)
                VRLog.Note("WorldUI", "MENU DEPTH CHARACTER SOURCE: native model scene bound to its original preview; separate window projection registered; count=" + count + ".");
            return;
        }
    }

    private static MenuDepthCharacterSource? Find(RawImage image)
    {
        for (int i = 0; i < Sources.Count; i++)
            if (ReferenceEquals(Sources[i].Graphic, image)) return Sources[i];
        return null;
    }

    private static Func<Character3DDisplayManager, bool>? CreateHiddenReader()
    {
        try
        {
            FieldInfo? field = typeof(Character3DDisplayManager).GetField("isHidden",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null || field.FieldType != typeof(bool)) return null;
            // FieldInfo.GetValue boxes the bool on every readiness/eye pass. The
            // mod already runs on the native Mono runtime; one read-only accessor
            // preserves the original field and avoids that recurring allocation.
            var method = new DynamicMethod("GloomhavenVR_MenuDepth_ReadCharacterHidden", typeof(bool),
                new[] { typeof(Character3DDisplayManager) }, typeof(MenuDepthCharacters), true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<Character3DDisplayManager, bool>)method.CreateDelegate(typeof(Func<Character3DDisplayManager, bool>));
        }
        catch { return null; } // Unknown runtime shape keeps the original mono preview.
    }

    internal static void Shutdown()
    {
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            MenuDepthSources.Unregister(Sources[i]);
            Sources[i].Dispose();
        }
        Sources.Clear();
        DisplayRequests.Clear(); DeadRequests.Clear(); PendingRequests.Clear(); RequestImages.Clear();
        Managers.Clear(); Images.Clear(); PendingImages.Clear(); PendingManagers.Clear();
        _enabled = false;
        GlobalCensuses = RequestCensuses = ImageVisits = 0;
    }
}
