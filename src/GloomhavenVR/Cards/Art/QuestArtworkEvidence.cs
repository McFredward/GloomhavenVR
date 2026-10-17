using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>At most eight Debug records of actual scenario artwork sources/draw crops.
/// These are texture readbacks, not proof of a final headset shader/camera image. Each
/// role keeps at most two observed generations. No frame watcher or native mutation.</summary>
internal static class QuestArtworkEvidence
{
    private static readonly Dictionary<string, HashSet<int>> Seen = new();
    private static int _records;

    internal static void SpriteField(Component? owner, string fieldName, string role)
    {
        if (!CanObserve(role) || owner == null || !owner.gameObject.activeInHierarchy) return;
        try
        {
            FieldInfo? field = owner.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field?.GetValue(owner) is not Image image || image == null || !image.isActiveAndEnabled) return;
            Sprite? drawn = image.overrideSprite;
            if (drawn == null || drawn.texture == null) return;
            Sprite source = CardFaceMipBake.OriginalFor(drawn);
            Texture2D texture = source.texture;
            if (texture == null || !Admit(role, drawn.GetInstanceID())) return;
            Rect crop = source.textureRect;
            Rect outputCrop = drawn.textureRect;
            string detail = " role=" + role + " sprite=" + Name(source.name) + " source=" + Label(texture)
                + " crop=" + Geometry(crop) + " output=" + Label(drawn.texture)
                + " outputCrop=" + Geometry(outputCrop);
            Emit(detail, texture, crop, drawn.texture, outputCrop);
        }
        catch (Exception error) { Failed(role, error); }
    }

    internal static void Raw(Component? owner, string role)
    {
        if (!CanObserve(role) || owner == null || !owner.gameObject.activeInHierarchy) return;
        try
        {
            foreach (RawImage image in owner.GetComponentsInChildren<RawImage>(false))
            {
                if (image == null || !image.isActiveAndEnabled || image.texture == null) continue;
                Texture drawn = image.texture;
                Texture source = WorldUI.PanelMipBake.OriginalFor(drawn);
                if (!Admit(role, drawn.GetInstanceID())) return;
                Rect crop = image.uvRect;
                string detail = " role=" + role + " source=" + Label(source) + " uv=" + Geometry(crop)
                    + " output=" + Label(drawn);
                Emit(detail, source, PixelRect(source, crop), drawn, PixelRect(drawn, crop));
                return;
            }
        }
        catch (Exception error) { Failed(role, error); }
    }

    private static bool CanObserve(string role) => QuestStandalonePlatform.Enabled && VRLog.WantsDebug
        && Camera.current == null && _records < 8 && (!Seen.TryGetValue(role, out HashSet<int> seen) || seen.Count < 2);

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

    private static void Emit(string detail, Texture source, Rect crop, Texture output, Rect outputCrop)
    {
        // Two 8x8 RGBA crops (344 base64 characters each) fit one Android logcat
        // record with bounded identity/geometry metadata. No full texture readback.
        string first = Sample(source, crop);
        string second = Sample(output, outputCrop);
        string message = "[Quest startup] artwork" + detail + " rgba8=" + first + " outputRgba8=" + second;
        if (Encoding.UTF8.GetByteCount(message) <= 1000) Debug.Log(message);
        else Debug.Log("[Quest startup] artwork" + detail + " samples=omitted-record-limit");
    }

    private static string Sample(Texture texture, Rect pixels)
    {
        RenderTexture target = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prior = RenderTexture.active;
        Texture2D? readback = null;
        try
        {
            QuestTextureCopy.Copy(texture, target, new Vector2(pixels.width / texture.width, pixels.height / texture.height),
                new Vector2(pixels.x / texture.width, pixels.y / texture.height));
            RenderTexture.active = target;
            readback = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
            readback.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
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
    private static string Name(string name) => name.Length > 24 ? name.Substring(0, 24) : name;
    private static string Label(Texture texture) => Name(texture.name) + "#" + texture.GetInstanceID()
        + "@" + texture.updateCount + ":" + texture.width + "x" + texture.height;
    private static string Geometry(Rect rect) => rect.x.ToString("F3", CultureInfo.InvariantCulture) + ","
        + rect.y.ToString("F3", CultureInfo.InvariantCulture) + "," + rect.width.ToString("F3", CultureInfo.InvariantCulture)
        + "," + rect.height.ToString("F3", CultureInfo.InvariantCulture);
}
