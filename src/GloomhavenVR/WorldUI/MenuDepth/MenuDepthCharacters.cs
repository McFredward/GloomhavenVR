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
    private const float CensusSeconds = .25f;
    private static readonly List<MenuDepthCharacterSource> Sources = new(8);
    private static readonly FieldInfo? HolderField = typeof(Character3DDisplayManager).GetField(
        "character3DHolder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly Func<Character3DDisplayManager, bool>? ReadHidden = CreateHiddenReader();
    private static Camera[] _cameras = new Camera[16];
    private static float _nextCensus;
    private static bool _reflectionReported;
    private static int _openings;

    internal static void Tick()
    {
        if (!VRSession.IsRunning) { Shutdown(); return; }
        // Existing registrations are cheap to retire between bounded discoveries.
        // Native pooled windows may change their texture before the next census.
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            if (Sources[i].BindingAlive) continue;
            MenuDepthSources.Unregister(Sources[i]);
            Sources[i].Dispose();
            Sources.RemoveAt(i);
        }
        if (Time.unscaledTime < _nextCensus) return;
        _nextCensus = Time.unscaledTime + CensusSeconds;
        if (HolderField == null || ReadHidden == null)
        {
            if (!_reflectionReported)
            {
                _reflectionReported = true;
                VRLog.Warn("WorldUI", "MENU DEPTH CHARACTER SOURCE unavailable: original holder/visibility fields were not found; native previews remain available.");
            }
            return;
        }

        int cameraCount = Camera.allCamerasCount;
        if (cameraCount > _cameras.Length) Array.Resize(ref _cameras, Mathf.NextPowerOfTwo(cameraCount));
        cameraCount = Camera.GetAllCameras(_cameras);
        // This array is allocated only at the bounded discovery cadence, never per
        // eye or per frame. Native window creation has no mod-owned registration seam.
        RawImage[] images = Object.FindObjectsOfType<RawImage>();
        for (int c = 0; c < cameraCount; c++)
        {
            Camera camera = _cameras[c];
            if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture == null) continue;
            Character3DDisplayManager manager = camera.GetComponent<Character3DDisplayManager>();
            if (manager == null || !manager.isActiveAndEnabled) continue;
            Transform? holder = HolderField.GetValue(manager) as Transform;
            if (holder == null) continue;
            for (int i = 0; i < images.Length; i++)
            {
                RawImage image = images[i];
                if (image == null || !image.isActiveAndEnabled || !image.gameObject.activeInHierarchy
                    || !ReferenceEquals(image.texture, camera.targetTexture) || Find(image) != null) continue;
                var source = new MenuDepthCharacterSource(image, camera, manager, holder, ReadHidden);
                Sources.Add(source);
                MenuDepthSources.Register(source);
                int count = ++_openings;
                if (count <= 4 || (count & (count - 1)) == 0)
                    VRLog.Note("WorldUI", "MENU DEPTH CHARACTER SOURCE: native model scene bound to its original preview; separate window projection registered; count=" + count + ".");
            }
        }
        // Do not retain every camera in the scene between censuses.
        Array.Clear(_cameras, 0, cameraCount);
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
        Array.Clear(_cameras, 0, _cameras.Length);
        _nextCensus = 0f;
    }
}
