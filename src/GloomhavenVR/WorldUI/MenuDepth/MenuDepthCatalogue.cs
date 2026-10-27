using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GloomhavenVR.Core;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Finite, hash-bound depth companions beside the DLL. Native colour/sprite ownership is unchanged.</summary>
internal static class MenuDepthCatalogue
{
    [DataContract] internal sealed class Field
    {
        [DataMember] public string source_id = string.Empty;
        [DataMember] public int[] source_dimensions = Array.Empty<int>();
        [DataMember] public string depth_file = string.Empty;
        [DataMember] public int[] depth_dimensions = Array.Empty<int>();
        [DataMember] public int depth_bytes = 0;
        [DataMember] public string depth_sha256 = string.Empty;
    }
    [DataContract] internal sealed class Narrative
    {
        [DataMember] public string id = string.Empty;
        [DataMember] public string path_picture = string.Empty;
        [DataMember] public string source_id = string.Empty;
        [DataMember] public string canonical_asset = string.Empty;
    }
    [DataContract] internal sealed class Artwork
    {
        [DataMember] public string source_id = string.Empty;
        [DataMember] public string kind = string.Empty;
        [DataMember] public string asset_path = string.Empty;
        [DataMember] public string canonical_asset = string.Empty;
        [DataMember] public string sprite_name = string.Empty;
        [DataMember] public string texture_name = string.Empty;
        [DataMember] public int[] logical_dimensions = Array.Empty<int>();
        [DataMember] public int[] texture_dimensions = Array.Empty<int>();
        [DataMember] public float[] sprite_rect = Array.Empty<float>();
        [DataMember] public float[] texture_rect = Array.Empty<float>();
        [DataMember] public float[] texture_rect_offset = Array.Empty<float>();
        [DataMember] public string addressable_guid = string.Empty;
        [DataMember] public string native_field = string.Empty;
        [DataMember] public string raster_reconstruction = string.Empty;
    }
    [DataContract] private sealed class Manifest
    {
        [DataMember] public int schema = 0;
        [DataMember] public string status = string.Empty;
        [DataMember] public string format = string.Empty;
        [DataMember] public Field[] images = Array.Empty<Field>();
        [DataMember] public Narrative[] narrative_configs = Array.Empty<Narrative>();
        [DataMember] public Artwork[] artwork = Array.Empty<Artwork>();
    }

    private static readonly Dictionary<string, Field> Fields = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Narratives = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Artwork> Defaults = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Artwork> Guids = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Artwork>> ArtBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MenuDepthHeightfield> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Arrival = new();
    private static readonly HashSet<string> Refused = new(StringComparer.Ordinal);
    private static bool _loaded;
    private static int _failures;
    private static string DirectoryPath => Path.Combine(Path.GetDirectoryName(typeof(MenuDepthCatalogue).Assembly.Location)!, "MenuDepth", "Story");

    internal static bool EnsureLoaded()
    {
        if (_loaded) return Fields.Count > 0;
        _loaded = true;
        try
        {
            string file = Path.Combine(DirectoryPath, "manifest.json");
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 8 * 1024 * 1024) throw new InvalidDataException("Missing or oversized story depth manifest.");
            // Unity2021.3.5 JsonUtility silently leaves these nested DTO arrays empty.
            // Use the same explicit data contracts as the existing native mesh banks.
            Manifest? manifest;
            using (Stream input = File.OpenRead(file))
                manifest = new DataContractJsonSerializer(typeof(Manifest),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 100000 }).ReadObject(input) as Manifest;
            if (manifest == null || manifest.schema != 1 || manifest.status != "complete" || manifest.format != "GMD1"
                || manifest.images == null || manifest.images.Length == 0 || manifest.images.Length > 4096
                || manifest.narrative_configs == null || manifest.artwork == null)
                throw new InvalidDataException("Incomplete story depth manifest.");
            foreach (Field field in manifest.images)
            {
                if (!Hex(field.source_id, 32) || !Hex(field.depth_sha256, 64)
                    || field.depth_file != field.source_id + ".gmd" || !Dimensions(field.source_dimensions, 16384)
                    || !Dimensions(field.depth_dimensions, 512) || field.depth_bytes != 32 + field.depth_dimensions[0] * field.depth_dimensions[1] * 2
                    || Fields.ContainsKey(field.source_id)) throw new InvalidDataException("Invalid story depth entry.");
                Fields.Add(field.source_id, field);
            }
            foreach (Artwork art in manifest.artwork)
            {
                if (!Fields.ContainsKey(art.source_id) || !Dimensions(art.logical_dimensions, 16384)
                    || !Dimensions(art.texture_dimensions, 16384) || art.raster_reconstruction != "native-full-texture")
                    throw new InvalidDataException("Unqualified native story image mapping.");
                if (!ArtBySource.TryGetValue(art.source_id, out List<Artwork>? items))
                    ArtBySource.Add(art.source_id, items = new List<Artwork>());
                items.Add(art);
                if (!string.IsNullOrEmpty(art.native_field)) Defaults.Add(art.native_field, art);
                if (!string.IsNullOrEmpty(art.addressable_guid))
                {
                    if (Guids.TryGetValue(art.addressable_guid, out Artwork? previous) && previous.source_id != art.source_id)
                        throw new InvalidDataException("Ambiguous compendium image identity.");
                    Guids[art.addressable_guid] = art;
                }
            }
            foreach (Narrative narrative in manifest.narrative_configs)
            {
                if (string.IsNullOrEmpty(narrative.id) || !ArtBySource.ContainsKey(narrative.source_id)
                    || Narratives.ContainsKey(narrative.id)) throw new InvalidDataException("Invalid narrative identity.");
                Narratives.Add(narrative.id, narrative.source_id);
            }
            VRLog.Note("WorldUI", $"MENU DEPTH CATALOGUE: {Narratives.Count} narrative identities, {Fields.Count} original-image depth fields; native colour retained.");
            return true;
        }
        catch (Exception error)
        {
            Fields.Clear(); Narratives.Clear(); Defaults.Clear(); Guids.Clear(); ArtBySource.Clear();
            Report(error);
            return false;
        }
    }

    internal static Artwork? NarrativeArt(string id, Sprite sprite)
    {
        if (!EnsureLoaded() || !Narratives.TryGetValue(id, out string source) || !ArtBySource.TryGetValue(source, out List<Artwork>? items)) return null;
        for (int i = 0; i < items.Count; i++) if (Matches(items[i], sprite)) return items[i];
        return null;
    }
    internal static Artwork? DefaultArt(string field, Sprite sprite) => EnsureLoaded()
        && Defaults.TryGetValue(field, out Artwork? art) && Matches(art, sprite) ? art : null;
    internal static Artwork? GuidArt(string guid, Sprite sprite) => EnsureLoaded()
        && Guids.TryGetValue(guid, out Artwork? art) && Matches(art, sprite) ? art : null;

    internal static Sprite? Original(Sprite? sprite) => sprite == null ? null : CardFaceMipBake.OriginalFor(sprite);

    private static bool Matches(Artwork art, Sprite sprite)
    {
        if (sprite == null) return false;
        Sprite? original = Original(sprite);
        // Mip sprites are presentation copies, not new artwork. Trust only the
        // existing instance-provenance map; a similar name/suffix proves nothing.
        return original != null && Mathf.Abs(original.rect.width - art.logical_dimensions[0]) < .001f
            && Mathf.Abs(original.rect.height - art.logical_dimensions[1]) < .001f
            && (string.IsNullOrEmpty(original.name) || string.Equals(original.name, art.sprite_name, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryDepthUv(Artwork art, Sprite presentation, out Vector4 uv)
    {
        uv = new Vector4(1f, 1f, 0f, 0f);
        Sprite? original = Original(presentation);
        if (original == null || !Matches(art, presentation) || original.texture == null || presentation.texture == null) return false;
        if (original == presentation) return true;
        Texture2D source = original.texture, displayed = presentation.texture;
        if (source.width != art.texture_dimensions[0] || source.height != art.texture_dimensions[1]) return false;
        // Region bakes reconstruct the logical sprite on a full, zero-origin
        // texture. Check this first: equal source/display dimensions alone do
        // not prove that a trimmed picture kept the whole atlas coordinate map.
        Rect rect = presentation.rect;
        int width = Mathf.RoundToInt(original.rect.width), height = Mathf.RoundToInt(original.rect.height);
        try
        {
            Rect region = original.textureRect;
            Vector2 offset = original.textureRectOffset;
            if (rect.position != Vector2.zero || displayed.width != width || displayed.height != height
                || Mathf.Abs(rect.width - width) >= .001f || Mathf.Abs(rect.height - height) >= .001f)
            {
                // Actual shared-atlas copies use textureRect, which can differ
                // from rect on an atlas even without trim or packing rotation.
                return displayed.width == source.width && displayed.height == source.height
                    && rect == region && offset == Vector2.zero
                    && Mathf.Abs(region.width - original.rect.width) < .5f
                    && Mathf.Abs(region.height - original.rect.height) < .5f;
            }
            // These are the same integer pixel locations used by the actual
            // CardFaceMipBake region copy, including native fractional trim data.
            uv = new Vector4(width / (float)source.width, height / (float)source.height,
                (Mathf.RoundToInt(region.x) - Mathf.RoundToInt(offset.x)) / (float)source.width,
                (Mathf.RoundToInt(region.y) - Mathf.RoundToInt(offset.y)) / (float)source.height);
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (UnityException) { return false; }
    }

    internal static MenuDepthHeightfield? Load(Artwork art)
    {
        if (Cache.TryGetValue(art.source_id, out MenuDepthHeightfield? cached)) return cached;
        if (Refused.Contains(art.source_id) || !Fields.TryGetValue(art.source_id, out Field? expected)) return null;
        try
        {
            string path = Path.Combine(DirectoryPath, expected.depth_file);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != expected.depth_bytes) throw new InvalidDataException("Missing story depth companion.");
            byte[] bytes = File.ReadAllBytes(path);
            using (SHA256 hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
                if (actual != expected.depth_sha256) throw new InvalidDataException("Story depth companion hash mismatch.");
            }
            MenuDepthHeightfield result = MenuDepthHeightfield.Read(bytes);
            if (result.Width != expected.depth_dimensions[0] || result.Height != expected.depth_dimensions[1]
                || result.SourceWidth != expected.source_dimensions[0] || result.SourceHeight != expected.source_dimensions[1])
                throw new InvalidDataException("Story depth companion dimensions disagree.");
            if (Cache.Count >= 16) Cache.Remove(Arrival.Dequeue());
            Cache.Add(art.source_id, result); Arrival.Enqueue(art.source_id);
            return result;
        }
        catch (Exception error) { Refused.Add(art.source_id); Report(error); return null; }
    }

    private static bool Dimensions(int[] values, int maximum) => values != null && values.Length == 2
        && values[0] >= 1 && values[1] >= 1 && values[0] <= maximum && values[1] <= maximum;
    private static bool Hex(string value, int count)
    {
        if (value == null || value.Length != count) return false;
        for (int i = 0; i < value.Length; i++) if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f')) return false;
        return true;
    }
    private static void Report(Exception error)
    {
        if (++_failures <= 4) VRLog.Warn("WorldUI", "MENU DEPTH COMPANION REFUSED: " + error.GetType().Name + ": " + error.Message + "; original image remains available.");
    }
}
