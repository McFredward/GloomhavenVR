using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.Cards;

/// <summary>Keep the native flame's numerical burn mask out of sRGB decoding.</summary>
internal static class QuestCardFireMask
{
    private const int Capacity = 4;
    private sealed class Entry
    {
        internal Texture2D? Copy;
        internal int References, Width, Height, Mips;
        internal TextureFormat Format;
    }
    private static readonly Dictionary<Texture2D, Entry> Copies = new();

    internal static bool Matches(Texture? candidate, Texture? original) => candidate != null && original is Texture2D source
        && Copies.TryGetValue(source, out Entry entry) && entry.Copy != null && candidate == entry.Copy;

    // Every successful result is a lease, released when its private material
    // yields the native face. Several simultaneously burning cards can share
    // the same numerical view without destroying another card's live input.
    internal static Texture2D? For(Texture2D? source)
    {
        if (!QuestStandalonePlatform.Enabled || QualitySettings.activeColorSpace != ColorSpace.Linear
            || source == null || !GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat)) return null;
        if (source.updateCount != 0 || source.width > 2048 || source.height > 2048)
            return null;
        if (Copies.TryGetValue(source, out Entry previous))
        {
            if (previous.Copy == null || previous.Width != source.width || previous.Height != source.height
                || previous.Mips != source.mipmapCount || previous.Format != source.format) return null;
            previous.References++;
            return previous.Copy;
        }
        if (Copies.Count >= Capacity) return null;
        Texture2D? copy = null;
        try
        {
            // The owned PC PlayerSettings use Gamma. The exact recovered flame
            // fragment samples GREEN from _ParticleTexture as a numeric mask,
            // then squares it and thresholds it against the native timeline.
            // In Quest/Linear Unity instead binds that original sRGB texture
            // through a decoding view. With fixed original time 20, that alone
            // reduces 16997 native flame pixels to 69. Keep the exact encoded
            // blocks/mips and only remove this unintended sampling decode.
            // No pixel conversion, brightness change, shader/timeline rewrite,
            // XR readback or original texture mutation is involved.
            copy = new Texture2D(source.width, source.height, source.format, source.mipmapCount, true)
            {
                name = source.name + ".QuestNumericMask",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode,
                wrapModeU = source.wrapModeU, wrapModeV = source.wrapModeV, wrapModeW = source.wrapModeW,
                anisoLevel = source.anisoLevel, mipMapBias = source.mipMapBias
            };
            if (source.isReadable)
            {
                copy.LoadRawTextureData(source.GetRawTextureData());
                copy.Apply(false, true);
            }
            else
            {
                if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
                    throw new InvalidOperationException("Native texture block copies are unavailable.");
                copy.Apply(false, true);
                Graphics.CopyTexture(source, copy);
            }
            Copies.Add(source, new Entry { Copy = copy, References = 1, Width = source.width,
                Height = source.height, Mips = source.mipmapCount, Format = source.format });
            return copy;
        }
        catch (Exception error)
        {
            if (copy != null) UnityEngine.Object.Destroy(copy);
            Copies.Add(source, new Entry());
            VRLog.Warn("Cards", "Quest native fire mask retains its original sampling: " + error.GetType().Name);
            return null;
        }
    }

    internal static void Release(Texture? original)
    {
        if (original is not Texture2D source || !Copies.TryGetValue(source, out Entry entry) || entry.Copy == null)
            return;
        if (--entry.References > 0) return;
        UnityEngine.Object.Destroy(entry.Copy);
        Copies.Remove(source);
    }
}
