using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>At most eight Debug observations of actual scenario artwork sources/draw crops.
/// Texture samples and passive consumer state are not proof of final headset output. Each
/// role keeps at most two observed generations. No frame watcher or native mutation.</summary>
internal static class QuestArtworkEvidence
{
    private static readonly Dictionary<string, HashSet<int>> Seen = new();
    private static int _records;

    internal static void SpriteField(Component? owner, string fieldName, string role)
    {
        if (!CanObserve(role)) return;
        try
        {
            if (owner == null) { Missing(role, "owner"); return; }
            FieldInfo? field = null;
            for (Type? type = owner.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic
                    | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field == null) { Missing(role, "field"); return; }
            Image(field.GetValue(owner) as Image, role);
        }
        catch (Exception error) { Failed(role, error); }
    }

    internal static void Image(Image? image, string role)
    {
        if (!CanObserve(role)) return;
        try
        {
            if (image == null) { Missing(role, "image"); return; }
            Sprite? drawn = image.overrideSprite;
            if (drawn == null || drawn.texture == null)
            {
                if (Admit(role, 0)) State(" role=" + role + " unavailable=sprite" + DrawState(image));
                return;
            }
            Sprite source = CardFaceMipBake.OriginalFor(drawn);
            Texture2D texture = source.texture;
            if (texture == null) { Missing(role, "texture"); return; }
            // A native offscreen-capture host is intentionally inactive between
            // draws. Its Image/texture is still the real source and must remain
            // observable. Do not change native visibility to obtain a reading.
            if (!Admit(role, Identity(drawn, image))) return;
            Rect crop = source.textureRect;
            Rect outputCrop = drawn.textureRect;
            Vector4 outerUv = UnityEngine.Sprites.DataUtility.GetOuterUV(drawn);
            string detail = " role=" + role + " sprite=" + Name(source.name) + " source=" + Label(texture)
                + " crop=" + Geometry(crop) + " output=" + Label(drawn.texture)
                + " outputCrop=" + Geometry(outputCrop) + " outerUv=" + outerUv;
            detail += DrawState(image);
            Emit(role, detail, image, texture, crop, drawn.texture, outputCrop);
        }
        catch (Exception error) { Failed(role, error); }
    }

    internal static void Raw(Component? owner, string role)
    {
        if (!CanObserve(role)) return;
        try
        {
            if (owner == null) { Missing(role, "owner"); return; }
            RawImage? fallback = null;
            foreach (RawImage image in owner.GetComponentsInChildren<RawImage>(true))
            {
                if (image == null) continue;
                if (fallback == null) fallback = image;
                if (image.texture == null || !image.gameObject.activeSelf) continue;
                RawImage(image, role);
                return;
            }
            RawImage(fallback, role);
        }
        catch (Exception error) { Failed(role, error); }
    }

    internal static void RawImage(RawImage? image, string role)
    {
        if (!CanObserve(role)) return;
        try
        {
            if (image == null) { Missing(role, "image"); return; }
            Texture? drawn = image.texture;
            if (drawn == null)
            {
                if (Admit(role, 0)) State(" role=" + role + " unavailable=texture" + DrawState(image));
                return;
            }
            if (!Admit(role, Identity(drawn, image))) return;
            Texture source = WorldUI.PanelMipBake.OriginalFor(drawn);
            Rect crop = image.uvRect;
            string detail = " role=" + role + " source=" + Label(source) + " uv=" + Geometry(crop)
                + " output=" + Label(drawn);
            detail += DrawState(image);
            Emit(role, detail, image, source, PixelRect(source, crop), drawn, PixelRect(drawn, crop));
        }
        catch (Exception error) { Failed(role, error); }
    }

    private static int Identity(UnityEngine.Object textureOrSprite, Graphic image) => unchecked(
        textureOrSprite.GetInstanceID() * 31 + (image.enabled ? 1 : 0));

    private static void Missing(string role, string what)
    {
        if (Admit(role, 0)) State(" role=" + role + " unavailable=" + what);
    }

    private static void State(string detail) => Debug.Log("[Quest startup] artwork" + detail);

    private static bool CanObserve(string role) => QuestStandalonePlatform.Enabled && VRLog.WantsDebug
        && _records < 8 && (!Seen.TryGetValue(role, out HashSet<int> seen) || seen.Count < 2);

    // These three consumers call from Update/LateUpdate. Camera.current is not a
    // phase marker on MultiPass: Unity may leave the last rendered camera there
    // between frames (the same B23 finding retained in WorldUIModule). Requiring
    // null can suppress all observations despite these known lifecycle callers;
    // B678's capture contains no artwork records, so it cannot expose that state.
    private static string DrawState(Graphic image)
    {
        CanvasRenderer renderer = image.canvasRenderer;
        Material? material = renderer.materialCount > 0 ? renderer.GetMaterial(0) : null;
        string shader = material != null && material.shader != null ? Name(material.shader.name) : "none";
        Color tint = renderer.GetColor();
        string detail = " graphic=" + Name(image.name) + " enabled=" + image.enabled
            + " self=" + image.gameObject.activeSelf + " hierarchy=" + image.gameObject.activeInHierarchy + " shader=" + shader
            + " rect=" + Geometry(image.rectTransform.rect) + " rectClip=" + renderer.hasRectClipping
            + " cull=" + renderer.cull + " tint=" + tint.r.ToString("F2", CultureInfo.InvariantCulture)
            + "," + tint.g.ToString("F2", CultureInfo.InvariantCulture) + "," + tint.b.ToString("F2", CultureInfo.InvariantCulture)
            + "," + tint.a.ToString("F2", CultureInfo.InvariantCulture)
            + " inheritedA=" + renderer.GetInheritedAlpha().ToString("F2", CultureInfo.InvariantCulture);
        if (image is Image sprite) detail += " type=" + sprite.type + " aspect=" + sprite.preserveAspect
            + " mesh=" + sprite.useSpriteMesh + " fill=" + sprite.fillAmount.ToString("F2", CultureInfo.InvariantCulture);
        if (material != null && material.HasProperty("_ClipRect")) detail += " shaderClip=" + material.GetVector("_ClipRect");
        // Disabling a native capture host clears CanvasRenderer's actual binding.
        // Keep the current source material distinct from that empty draw binding.
        Material sourceMaterial = image.material;
        if (sourceMaterial != null && !ReferenceEquals(sourceMaterial, material))
            detail += " sourceShader=" + (sourceMaterial.shader != null ? Name(sourceMaterial.shader.name) : "none");
        Canvas? canvas = image.GetComponentInParent<Canvas>(true);
        detail += " canvas=" + (canvas != null ? canvas.enabled + "," + canvas.renderMode : "none");
        return detail;
    }

    private static bool Admit(string role, int identity)
    {
        if (!Seen.TryGetValue(role, out HashSet<int> seen)) Seen[role] = seen = new HashSet<int>();
        if (!seen.Add(identity)) return false;
        _records++;
        return true;
    }

    private static void Failed(string role, Exception error)
    {
        // A tight sprite's rectangular crop can be unavailable. Its exact original
        // remains untouched; record that failure once rather than retrying readbacks.
        if (_records >= 8) return;
        _records++;
        if (!Seen.TryGetValue(role, out HashSet<int> seen)) Seen[role] = seen = new HashSet<int>();
        seen.Add(int.MinValue); seen.Add(int.MaxValue);
        Debug.Log("[Quest startup] artwork role=" + role + " unavailable=" + error.GetType().Name);
    }

    private static void Emit(string role, string detail, Graphic image, Texture source, Rect crop, Texture output, Rect outputCrop)
    {
        // Keep BOTH real6x6 pixel observations even when actual Android instance IDs,
        // long native names, clipping and current material metadata exceed one logcat
        // record. B679's missing observations cannot be diagnosed by dropping the
        // very samples once we finally reach a parked capture source. Compress only
        // metadata; no extra GPU readback, log record or quota is introduced.
        string samples = " rgba6=" + Sample(source, crop) + " outputRgba6=" + Sample(output, outputCrop);
        string message = "[Quest startup] artwork" + detail + samples;
        if (Encoding.UTF8.GetByteCount(message) > 1000)
        {
            Material sourceMaterial = image.material;
            string shader = sourceMaterial != null && sourceMaterial.shader != null ? Name(sourceMaterial.shader.name) : "none";
            string compact = " role=" + Name(role) + " metadataCompressed=true source=" + Label(source)
                + " crop=" + Geometry(crop) + " output=" + Label(output) + " outputCrop=" + Geometry(outputCrop)
                + " enabled=" + image.enabled + " self=" + image.gameObject.activeSelf
                + " hierarchy=" + image.gameObject.activeInHierarchy + " sourceShader=" + shader;
            message = "[Quest startup] artwork" + compact + samples;
        }
        Debug.Log(message);
    }

    private static string Sample(Texture texture, Rect pixels)
    {
        RenderTexture target = RenderTexture.GetTemporary(6, 6, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prior = RenderTexture.active;
        Texture2D? readback = null;
        try
        {
            QuestTextureCopy.Copy(texture, target, new Vector2(pixels.width / texture.width, pixels.height / texture.height),
                new Vector2(pixels.x / texture.width, pixels.y / texture.height));
            RenderTexture.active = target;
            readback = new Texture2D(6, 6, TextureFormat.RGBA32, false, false);
            readback.ReadPixels(new Rect(0, 0, 6, 6), 0, 0);
            Color32[] values = readback.GetPixels32();
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                bytes[i * 4] = values[i].r; bytes[i * 4 + 1] = values[i].g;
                bytes[i * 4 + 2] = values[i].b; bytes[i * 4 + 3] = values[i].a;
            }
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            RenderTexture.active = prior;
            RenderTexture.ReleaseTemporary(target);
            if (readback != null) UnityEngine.Object.Destroy(readback);
        }
    }

    private static Rect PixelRect(Texture texture, Rect uv) =>
        new(uv.x * texture.width, uv.y * texture.height, uv.width * texture.width, uv.height * texture.height);
    private static string Name(string name)
    {
        int count = Mathf.Min(name.Length, 24);
        while (count > 0 && Encoding.UTF8.GetByteCount(name.Substring(0, count)) > 24) count--;
        if (count > 0 && char.IsHighSurrogate(name[count - 1])) count--;
        return count == name.Length ? name : name.Substring(0, count);
    }
    private static string Label(Texture texture) => Name(texture.name) + "#" + texture.GetInstanceID()
        + "@" + texture.updateCount + ":" + texture.width + "x" + texture.height;
    private static string Geometry(Rect rect) => rect.x.ToString("F3", CultureInfo.InvariantCulture) + ","
        + rect.y.ToString("F3", CultureInfo.InvariantCulture) + "," + rect.width.ToString("F3", CultureInfo.InvariantCulture)
        + "," + rect.height.ToString("F3", CultureInfo.InvariantCulture);
}
