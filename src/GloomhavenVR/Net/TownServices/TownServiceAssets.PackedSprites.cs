using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net.TownServices;

internal sealed partial class TownServiceAssets
{
    private readonly HashSet<int> _packedAtlases = new();
    private readonly List<Sprite> _packedSprites = new();
    private float _nextPackedScan;

    /// <summary>Load-time native dependency preparation, independent of the general
    /// font/texture census interval. Existing preparation can discover an atlas
    /// arriving after its first scan without scanning unrelated assets again.</summary>
    internal void PreparePackedSprites()
    {
        // The shipped TMP Settings fallback chain retains the original
        // BattleOverlayCanvas atlas. Access its native resource dependency before
        // the loaded-object census: the atlas is not a direct Resources root,
        // so LoadAll<SpriteAtlas> cannot discover a cold, unloaded instance.
        // No native window or gameplay controller is activated by this lookup.
        _ = TMP_Settings.instance;
        ScanPackedSprites();
    }

    /// <summary>Build638 delivered all33 required originals, but the observer never
    /// admitted them: Poison/Disarm belonged to dormant BattleOverlayCanvas members.
    /// A loaded-object Sprite census cannot create those members. The shipped atlas
    /// contains895 sprites and two Poison/Disarm variants with different pivots;
    /// GetSprite(name) would silently select one of them. GetSprites preserves every
    /// original member, including packed UV/mesh data, without activating game UI.
    /// Retain these native clones once per atlas and dispose them with this registry.
    /// Only clones obtained from this exact original atlas may drop Unity's added
    /// (Clone) suffix. Arbitrary same-name sprites never acquire that alias.</summary>
    private void ScanPackedSprites()
    {
        if (Time.unscaledTime < _nextPackedScan) return;
        _nextPackedScan = Time.unscaledTime + 0.1f;
        foreach (SpriteAtlas atlas in Resources.FindObjectsOfTypeAll<SpriteAtlas>())
        {
            if (atlas == null || _packedAtlases.Contains(atlas.GetInstanceID())) continue;
            var sprites = new Sprite[atlas.spriteCount];
            int count = atlas.GetSprites(sprites);
            bool ready = count == sprites.Length;
            for (int i = 0; i < count; i++)
                ready &= sprites[i] != null && sprites[i].texture != null;
            if (!ready)
            {
                // Atlas binding can finish after its Object appears in Resources.
                // Never retain an empty-texture descriptor or mark that atlas done.
                foreach (Sprite sprite in sprites)
                    if (sprite != null) Object.Destroy(sprite);
                continue;
            }
            for (int i = 0; i < count; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null) continue;
                _packedSprites.Add(sprite);
                string name = sprite.name;
                if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - "(Clone)".Length);
                Register(SpriteKey(sprite, name), sprite);
            }
            _packedAtlases.Add(atlas.GetInstanceID());
        }
    }

    private void ClearPackedSprites()
    {
        foreach (Sprite sprite in _packedSprites)
            if (sprite != null) Object.Destroy(sprite);
        _packedSprites.Clear(); _packedAtlases.Clear(); _nextPackedScan = 0;
    }

    private string SpriteKey(Sprite sprite, string name)
    {
        Rect rect = sprite.rect; Vector2 pivot = sprite.pivot; Vector4 border = sprite.border;
        // The original atlas also has10 pairs whose names AND logical geometry
        // coincide while their packed render-data keys differ. Full native topology
        // and UV provenance prevents discovery order from choosing another picture.
        return "sprite|" + Key(sprite.texture) + "|" + name + "|"
            + Numbers(rect.x, rect.y, rect.width, rect.height, pivot.x, pivot.y,
                border.x, border.y, border.z, border.w, sprite.pixelsPerUnit)
            + "|mesh|" + SpriteGeometry(sprite);
    }

    private static string SpriteGeometry(Sprite sprite)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            Vector2[] vertices = sprite.vertices; Vector2[] uv = sprite.uv;
            ushort[] triangles = sprite.triangles;
            writer.Write(vertices.Length);
            foreach (Vector2 v in vertices) { writer.Write(v.x); writer.Write(v.y); }
            writer.Write(uv.Length);
            foreach (Vector2 v in uv) { writer.Write(v.x); writer.Write(v.y); }
            writer.Write(triangles.Length);
            foreach (ushort triangle in triangles) writer.Write(triangle);
        }
        using SHA256 hash = SHA256.Create();
        byte[] value = hash.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length);
        return BitConverter.ToString(value).Replace("-", string.Empty);
    }
}
