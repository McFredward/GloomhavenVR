using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>Exact alpha-region readback for Quest card silhouette capture.</summary>
internal static class QuestArtworkRegion
{
    // Existing silhouette files predate the explicit ordinary-2D copy. Keeping a
    // separate recipe path prevents a previously truncated mask from determining
    // all future card geometry. Desktop's file/header and native artwork stay put.
    internal static string CacheSuffix => QuestStandalonePlatform.Enabled ? "." + QuestTextureCopy.Recipe : string.Empty;

    internal static Texture2D Read(Texture source, int x, int y, int width, int height)
    {
        // Copy the crop into its own full target. A partial ReadPixels from a
        // full-size RT has backend-dependent Y origin, and the builtin blit also
        // borrows XR stereo state. Both can corrupt alpha used to cut real mesh.
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        Texture2D? result = null;
        try
        {
            QuestTextureCopy.Copy(source, target, new Vector2((float)width / source.width, (float)height / source.height),
                new Vector2((float)x / source.width, (float)y / source.height));
            RenderTexture.active = target;
            result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply(false);
            return result;
        }
        catch
        {
            if (result != null) Object.Destroy(result);
            throw;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
    }
}
