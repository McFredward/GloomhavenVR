using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthGraphicProgram
{
    private static int _checks;
    private static string _output = string.Empty;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Exception(label); }
    private static bool Near(float a, float b, float tolerance = .0001f) => Mathf.Abs(a - b) < tolerance;
    private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, Instance)!.SetValue(owner, value);
    private static MenuDepthHeightfield? Field(MenuDepthGraphic graphic) => (MenuDepthHeightfield?)typeof(MenuDepthGraphic).GetField("_field", Instance)!.GetValue(graphic);

    public static int Corpus(string output, Sprite unused)
    {
        _checks = 0; _output = output;
        string assemblyFolder = Path.GetDirectoryName(typeof(MenuDepthGraphicProgram).Assembly.Location)!;
        CorpusCounts expected = JsonUtility.FromJson<CorpusCounts>(File.ReadAllText(Path.Combine(assemblyFolder, "expected-corpus.json")));
        Check(MenuDepthCatalogue.EnsureLoaded(), "the complete shipped original-image manifest loads through the actual production parser");
        var fields = (Dictionary<string, MenuDepthCatalogue.Field>)typeof(MenuDepthCatalogue).GetField("Fields", Static)!.GetValue(null)!;
        var narratives = (Dictionary<string, string>)typeof(MenuDepthCatalogue).GetField("Narratives", Static)!.GetValue(null)!;
        var artwork = (Dictionary<string, List<MenuDepthCatalogue.Artwork>>)typeof(MenuDepthCatalogue).GetField("ArtBySource", Static)!.GetValue(null)!;
        var defaults = (Dictionary<string, MenuDepthCatalogue.Artwork>)typeof(MenuDepthCatalogue).GetField("Defaults", Static)!.GetValue(null)!;
        var guids = (Dictionary<string, MenuDepthCatalogue.Artwork>)typeof(MenuDepthCatalogue).GetField("Guids", Static)!.GetValue(null)!;
        Check(fields.Count == expected.fields, "every shipped original-image depth identity survives actual Mono deserialization");
        Check(narratives.Count == expected.narratives, "every shipped original narrative identity survives actual Mono deserialization");
        Check(artwork.Sum(item => item.Value.Count) == expected.artwork,
            "every shipped original native sprite mapping survives actual Mono deserialization");
        Check(defaults.Count == expected.defaults && guids.Count == expected.guids,
            "all shipped native default-image and compendium GUID identities survive actual Mono deserialization");
        using (var writer = new StreamWriter(Path.Combine(output, "shipped-depth-load.csv")))
        {
            writer.WriteLine("source_id,depth_width,depth_height,native_width,native_height,declared_sha256");
            foreach (MenuDepthCatalogue.Field entry in fields.Values.OrderBy(f => f.source_id))
            {
                MenuDepthHeightfield? depth = artwork.TryGetValue(entry.source_id, out List<MenuDepthCatalogue.Artwork> choices)
                    ? MenuDepthCatalogue.Load(choices[0]) : null;
                Check(depth != null && depth.Width == entry.depth_dimensions[0] && depth.Height == entry.depth_dimensions[1]
                    && depth.SourceWidth == entry.source_dimensions[0] && depth.SourceHeight == entry.source_dimensions[1],
                    "each shipped GMD1 companion passes actual SHA, CRC, dimension and qualified-source loading: " + entry.source_id);
                writer.WriteLine(entry.source_id + "," + depth!.Width + "," + depth.Height + "," + depth.SourceWidth + ","
                    + depth.SourceHeight + "," + entry.depth_sha256);
            }
        }
        var cache = (Dictionary<string, MenuDepthHeightfield>)typeof(MenuDepthCatalogue).GetField("Cache", Static)!.GetValue(null)!;
        Check(cache.Count <= 16, "loading the entire shipped corpus retains only the production sixteen-field cache budget");
        return _checks;
    }

    [Serializable] private sealed class CorpusCounts
    {
        public int fields = 0, narratives = 0, artwork = 0, defaults = 0, guids = 0;
    }

    public static int Run(string output, Sprite trimmed)
    {
        _checks = 0; _output = output;
        Check(trimmed.rect.min != Vector2.zero && trimmed.rect.width < trimmed.texture.width,
            "original native input is an actual Unity sprite with a full-texture subrect crop");
        byte[] bytes = DepthBytes(4, 4, trimmed.texture.width, trimmed.texture.height, false);
        MenuDepthHeightfield field = MenuDepthHeightfield.Read(bytes);
        Check(field.Width == 4 && field.Height == 4 && field.SourceWidth == trimmed.texture.width,
            "actual GMD1 reader retains full native texture dimensions");
        Check(Near(field.Sample(0, 0), .1f, .0001f) && Near(field.Sample(1, 1), .8f, .0001f),
            "actual GMD1 bilinear samples use bottom-left full-texture UV coordinates");
        byte[] broken = (byte[])bytes.Clone(); broken[broken.Length - 1] ^= 1;
        bool refused = false; try { MenuDepthHeightfield.Read(broken); } catch (InvalidDataException) { refused = true; }
        Check(refused, "corrupt GMD1 payload refuses before any native mesh change");
        MeshAndGpu(trimmed, field);
        SemanticBinding(trimmed, bytes);
        return _checks;
    }

    private static void MeshAndGpu(Sprite sprite, MenuDepthHeightfield field)
    {
        using (var scene = new Scene(sprite))
        {
            MenuDepthGraphic graphic = scene.Image.gameObject.AddComponent<MenuDepthGraphic>();
            graphic.Configure(scene.Image, field, .03f);
            var original = NativeMesh(scene.Image);
            var spatial = ModifiedMesh(scene.Image, graphic);
            Check(spatial.Count > original.Count && spatial.Count < 60000,
                "prepared depth really deforms the original native Image into a bounded relief grid");
            Check(spatial.Max(v => v.position.z) > 1f,
                "prepared depth really gives the original native Image nonzero spatial relief");
            Check(original.Min(v => v.uv0.x) > 0 || original.Max(v => v.uv0.x) < 1,
                "trim fixture exercises native atlas UVs rather than an assumed zero-to-one crop");
            float x0 = original.Min(v => v.position.x), x1 = original.Max(v => v.position.x);
            float y0 = original.Min(v => v.position.y), y1 = original.Max(v => v.position.y);
            bool exact = true;
            foreach (UIVertex vertex in spatial)
            {
                float u = (vertex.position.x - x0) / (x1 - x0), v = (vertex.position.y - y0) / (y1 - y0);
                UIVertex bottomLeft = original.First(p => Near(p.position.x, x0) && Near(p.position.y, y0));
                UIVertex topRight = original.First(p => Near(p.position.x, x1) && Near(p.position.y, y1));
                float nativeU = Mathf.Lerp(bottomLeft.uv0.x, topRight.uv0.x, u);
                float nativeV = Mathf.Lerp(bottomLeft.uv0.y, topRight.uv0.y, v);
                float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float t = Mathf.Clamp01(edge / .06f); float feather = t * t * (3 - 2 * t);
                float expected = scene.Image.rectTransform.rect.width * .03f * (1 - field.Sample(nativeU, nativeV)) * feather;
                exact &= Near(vertex.uv0.x, nativeU) && Near(vertex.uv0.y, nativeV)
                    && Near(vertex.position.z, expected, .001f);
            }
            Check(exact, "relief samples the unchanged native sprite subrect UV in its full original texture");
            Vector3 originalPosition = scene.Image.rectTransform.localPosition;
            Rect originalRect = scene.Image.rectTransform.rect;
            using (var left = scene.Capture(new Vector3(-.18f, .03f, 0), "relief-left"))
            using (var right = scene.Capture(new Vector3(.18f, .03f, 0), "relief-right"))
            {
                Check(Difference(left.Image, right.Image, new RectInt(80, 40, 160, 100)) > 40,
                    "actual root relief renders distinct tracked eye views through the original native Image");
                Check(Difference(left.Image, right.Image, new RectInt(4, 4, 105, 20)) == 0,
                    "actual illustration relief keeps ordinary native text and controls on identical eye pixels");
            }
            Check(scene.Image.sprite == sprite && scene.Image.rectTransform.localPosition == originalPosition
                && scene.Image.rectTransform.rect == originalRect && scene.Image.raycastTarget,
                "original sprite, interaction rectangle and native raycast permission remain owned by the game");
            Vector3 localEye = new(-40, 10, -300);
            graphic.BeforeCapture(new MenuDepthEye(false, scene.Image.transform.TransformPoint(localEye), null!, true));
            try
            {
                var projected = ModifiedMesh(scene.Image, graphic);
                bool ray = projected.Count == spatial.Count;
                for (int i = 0; i < spatial.Count; i++)
                {
                    Vector3 p = spatial[i].position, q = projected[i].position;
                    float t = -localEye.z / (p.z - localEye.z);
                    ray &= Near(q.x, localEye.x + (p.x - localEye.x) * t, .001f)
                        && Near(q.y, localEye.y + (p.y - localEye.y) * t, .001f) && Near(q.z, p.z);
                }
                Check(ray, "actual root mesh follows the physical local-eye ray for every native relief vertex");
            }
            finally { graphic.AfterCapture(); }
            scene.Panel.SetPositionAndRotation(new Vector3(.6f, .2f, 3.4f), Quaternion.Euler(0, 28, 0));
            scene.Panel.localScale *= 1.3f; scene.Synchronize();
            using (var first = scene.Capture(new Vector3(.1f, .2f, .1f), "rotated-window-left"))
            using (var second = scene.Capture(new Vector3(.7f, .2f, .1f), "rotated-window-side"))
                Check(Difference(first.Image, second.Image, new RectInt(70, 40, 180, 100)) > 40,
                    "moved rotated resized native window updates the root relief perspective for a side head position");
            Color tint = new(.4f, .7f, .25f, .35f); scene.Image.color = tint;
            Color32 expectedTint = tint;
            Check(ModifiedMesh(scene.Image, graphic).All(v => v.color.Equals(expectedTint)),
                "native animated vertex tint and alpha invalidate the relief mesh cache");
            scene.Image.color = Color.white;
            scene.AddMask();
            Color fullOpacity;
            using (var masked = scene.Capture(new Vector3(.1f, .2f, .1f), "native-mask"))
            {
                Check(masked.Image.GetPixel(50, 90).a < .01f && masked.Image.GetPixel(160, 90).a > .5f,
                    "actual root relief obeys the original RectMask2D aperture");
                fullOpacity = masked.Image.GetPixel(160, 90);
            }
            scene.Group.alpha = 0;
            using (var hidden = scene.Capture(new Vector3(.1f, .2f, .1f), "native-fade-zero"))
                Check(hidden.Image.GetPixels32().All(p => p.a == 0),
                    "native CanvasGroup zero-alpha fade hides every prepared illustration pixel");
            scene.Group.alpha = .5f;
            using (var faded = scene.Capture(new Vector3(.1f, .2f, .1f), "native-fade-half"))
            {
                Color pixel = faded.Image.GetPixel(160, 90);
                Check(Near(pixel.a, fullOpacity.a * .5f, .01f) && Near(pixel.r, fullOpacity.r * .5f, .01f)
                    && Near(pixel.g, fullOpacity.g * .5f, .01f),
                    "native CanvasGroup intermediate fade remains animated in the spatial material");
            }
            scene.Group.alpha = 1;
            graphic.Configure(scene.Image, null, -.018f);
            var layer = ModifiedMesh(scene.Image, graphic);
            float depth = Mathf.Min(originalRect.width, originalRect.height) * -.018f;
            Check(layer.Count == original.Count && layer.All(v => Near(v.position.z, depth)),
                "decorative native layers keep their exact mesh and receive the configured shallow plane separation");
            Check(graphic.GetModifiedMaterial(scene.Image.defaultMaterial) == scene.Image.defaultMaterial,
                "constant decorative planes keep their original native UI material");
            Image glow = MakeImage(scene.Panel, "native sprite-free glow", new Vector2(40, 24), new Vector2(120, 60));
            glow.color = Color.yellow;
            MenuDepthGraphic glowLayer = glow.gameObject.AddComponent<MenuDepthGraphic>(); glowLayer.Configure(glow, null, .006f);
            Check(glowLayer.IsReady && glow.sprite == null && ModifiedMesh(glow, glowLayer).All(v => Near(v.position.z, 24 * .006f)),
                "native sprite-free white-quad decoration receives its configured shallow layer");
            Check(glowLayer.GetModifiedMaterial(glow.defaultMaterial) == glow.defaultMaterial,
                "native sprite-free white-quad decoration retains its exact original material");
            using (var rendered = scene.Capture(new Vector3(.1f, .2f, .1f), "native-white-quad"))
                Check(rendered.Image.GetPixel(280, 150).r > .5f && rendered.Image.GetPixel(280, 150).g > .5f,
                    "the sprite-free native white quad still renders through the original material");
            graphic.enabled = false;
            var restored = ModifiedMesh(scene.Image, graphic);
            Check(restored.Count == original.Count && restored.All(v => Near(v.position.z, 0)),
                "disabled depth restores the exact original flat native Image mesh");
        }
    }

    private static void SemanticBinding(Sprite first, byte[] depth)
    {
        string folder = Path.Combine(Path.GetDirectoryName(typeof(MenuDepthGraphicProgram).Assembly.Location)!, "MenuDepth", "Story");
        Directory.CreateDirectory(folder);
        Texture2D secondTexture = new(128, 128, TextureFormat.RGBA32, false);
        secondTexture.SetPixels(Enumerable.Repeat(Color.cyan, 128 * 128).ToArray()); secondTexture.Apply();
        Sprite second = Sprite.Create(secondTexture, new Rect(0, 0, 128, 128), new Vector2(.5f, .5f)); second.name = "second-story";
        byte[] secondDepth = DepthBytes(4, 4, 128, 128, true);
        string a = new('a', 32), b = new('b', 32), c = new('c', 32);
        var artA = Art(a, first); var artB = Art(b, second); var corruptArt = Art(c, second);
        artA.native_field = "defaultEventImage"; artB.addressable_guid = "typed-native-guid";
        var data = new Manifest
        {
            schema = 1, status = "complete", format = "GMD1",
            images = new[] { Entry(a, first, depth), Entry(b, second, secondDepth), Entry(c, second, secondDepth) },
            narrative_configs = new[] { new MenuDepthCatalogue.Narrative { id = "story-first", source_id = a },
                new MenuDepthCatalogue.Narrative { id = "story-second", source_id = b } },
            artwork = new[] { artA, artB, corruptArt }
        };
        File.WriteAllText(Path.Combine(folder, "manifest.json"), ManifestJson(data));
        File.WriteAllBytes(Path.Combine(folder, a + ".gmd"), depth);
        File.WriteAllBytes(Path.Combine(folder, b + ".gmd"), secondDepth);
        // Correct header, length and CRC; only the declared SHA differs.
        File.WriteAllBytes(Path.Combine(folder, c + ".gmd"), DepthBytes(4, 4, 128, 128, false));
        Check(MenuDepthCatalogue.EnsureLoaded(), "actual production JSON parser loads the complete typed native artwork manifest");
        Check(!ReferenceEquals(MenuDepthCatalogue.NarrativeArt("STORY-FIRST", first), artA)
            && MenuDepthCatalogue.NarrativeArt("STORY-FIRST", first)?.source_id == a,
            "typed narrative mapping survives actual production deserialization and native case-insensitive identity");
        Check(MenuDepthCatalogue.NarrativeArt("story-first", second) == null,
            "a typed narrative identity refuses a different native colour sprite");
        Check(MenuDepthCatalogue.DefaultArt("defaultEventImage", first)?.source_id == a
            && MenuDepthCatalogue.GuidArt("typed-native-guid", second)?.source_id == b,
            "native default-event and compendium GUID mappings select their qualified original sprite");
        MenuDepthHeightfield fieldA = MenuDepthCatalogue.Load(MenuDepthCatalogue.NarrativeArt("story-first", first)!)!;
        MenuDepthHeightfield fieldB = MenuDepthCatalogue.Load(MenuDepthCatalogue.NarrativeArt("story-second", second)!)!;
        Check(fieldA != null && fieldB != null && !Near(fieldA.Sample(.5f, .5f), fieldB.Sample(.5f, .5f)),
            "hash-bound native identities load their own distinct prepared GMD1 fields");
        Check(MenuDepthCatalogue.Load(corruptArt) == null,
            "a length-correct CRC-correct companion with the wrong declared SHA is refused");
        using (var scene = new Scene(first))
        {
            var toolsObject = new GameObject("actual native UIInfoTools input"); toolsObject.SetActive(false);
            UIInfoTools tools = toolsObject.AddComponent<UIInfoTools>();
            tools.narrativeImages = new[] { new NarrativeConfigUI { id = "story-first", picture = first },
                new NarrativeConfigUI { id = "story-second", picture = second } };
            tools.defaultEventImage = first;
            typeof(UIInfoTools).GetField("<Instance>k__BackingField", Static)!.SetValue(null, tools);
            var animatorObject = new GameObject("unplayed native animator input"); animatorObject.SetActive(false);
            GUIAnimator animator = animatorObject.AddComponent<LeanTweenGUIAnimator>();
            var storyObject = new GameObject("actual StoryImageViewer fields"); storyObject.SetActive(false);
            storyObject.transform.SetParent(scene.Panel, false);
            StoryImageViewer story = storyObject.AddComponent<StoryImageViewer>();
            Set(story, "loadImagesAnimator", animator); Set(story, "imageHolder", scene.Image); Set(story, "currentImage", "STORY-FIRST");
            storyObject.SetActive(true);
            CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = scene.Panel });
            try
            {
                MenuDepthDriver.Tick();
                MenuDepthGraphic graphic = scene.Image.GetComponent<MenuDepthGraphic>()
                    ?? throw new Exception("actual driver discovers the real GH.Runtime StoryImageViewer typed image field");
                Check(graphic.enabled && ReferenceEquals(Field(graphic), fieldA),
                    "actual driver discovers the real GH.Runtime StoryImageViewer typed image field");
                Set(story, "currentImage", "story-second"); MenuDepthDriver.Tick();
                Check(graphic.enabled && graphic.IsReady && ReferenceEquals(Field(graphic), fieldA) && scene.Image.sprite == first,
                    "an async new story key retains the corresponding old depth while its original old colour remains visible");
                scene.Image.sprite = second; MenuDepthDriver.Tick();
                Check(graphic.enabled && graphic.IsReady && ReferenceEquals(Field(graphic), fieldB) && scene.Image.sprite == second,
                    "the asynchronously arriving native picture switches to its own qualified field without retaining the old sprite");
                scene.Image.sprite = first; Set(story, "currentImage", "missing-native-id"); MenuDepthDriver.Tick();
                Check(graphic.enabled && ReferenceEquals(Field(graphic), fieldA),
                    "actual driver follows original native first-config fallback for an unknown story identity");
                var tabObject = new GameObject("actual original decorative UITab"); tabObject.SetActive(false);
                tabObject.transform.SetParent(scene.Panel, false);
                UITab tab = tabObject.AddComponent<UITab>();
                Image decoration = MakeImage(tabObject.transform, "native tab icon", new Vector2(30, 30), new Vector2(100, 40));
                Set(tab, "m_ImageTarget", decoration); Set(tab, "m_AdditionalImageTargets", Array.Empty<Image>());
                tabObject.SetActive(true);
                typeof(MenuDepthDriver).GetField("_nextCensus", Static)!.SetValue(null, 0f);
                MenuDepthDriver.Tick();
                MenuDepthGraphic layer = decoration.GetComponent<MenuDepthGraphic>()
                    ?? throw new Exception("real GH.Runtime UITab decoration receives only its typed shallow layer separation");
                Check(layer.IsReady && Field(layer) == null
                    && ModifiedMesh(decoration, layer).All(v => v.position.z < 0),
                    "real GH.Runtime UITab decoration receives only its typed shallow layer separation");
                Check(ReferenceEquals(Field(graphic), fieldA), "decorative discovery never overwrites the narrative relief field");
                storyObject.SetActive(false); MenuDepthDriver.Tick();
                Check(!graphic.enabled, "native story container disappearance disables its illustration depth without invoking continuation");
                MenuDepthDriver.Shutdown();
                Check(!layer.enabled && !MenuDepthSources.HasContent(scene.Panel),
                    "driver shutdown unregisters all prepared native artwork and decorative sources");
            }
            finally
            {
                CanvasConversion.ActivePanels.Clear(); MenuDepthDriver.Shutdown();
                Object.DestroyImmediate(storyObject); Object.DestroyImmediate(animatorObject); Object.DestroyImmediate(toolsObject);
            }
        }
        Object.DestroyImmediate(second); Object.DestroyImmediate(secondTexture);
    }

    [Serializable] private sealed class Manifest
    {
        public int schema;
        public string status = string.Empty, format = string.Empty;
        public MenuDepthCatalogue.Field[] images = Array.Empty<MenuDepthCatalogue.Field>();
        public MenuDepthCatalogue.Narrative[] narrative_configs = Array.Empty<MenuDepthCatalogue.Narrative>();
        public MenuDepthCatalogue.Artwork[] artwork = Array.Empty<MenuDepthCatalogue.Artwork>();
    }
    // Test input is ordinary external JSON, as in the shipped offline manifest.
    // Deserialization below executes the actual production parser.
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string Pair(int[] values) => "[" + string.Join(",", values) + "]";
    private static string ManifestJson(Manifest value) => "{\"schema\":" + value.schema + ",\"status\":" + Quote(value.status)
        + ",\"format\":" + Quote(value.format) + ",\"images\":[" + string.Join(",", value.images.Select(f =>
            "{\"source_id\":" + Quote(f.source_id) + ",\"source_dimensions\":" + Pair(f.source_dimensions)
            + ",\"depth_file\":" + Quote(f.depth_file) + ",\"depth_dimensions\":" + Pair(f.depth_dimensions)
            + ",\"depth_bytes\":" + f.depth_bytes + ",\"depth_sha256\":" + Quote(f.depth_sha256) + "}"))
        + "],\"narrative_configs\":[" + string.Join(",", value.narrative_configs.Select(n =>
            "{\"id\":" + Quote(n.id) + ",\"source_id\":" + Quote(n.source_id) + "}"))
        + "],\"artwork\":[" + string.Join(",", value.artwork.Select(a =>
            "{\"source_id\":" + Quote(a.source_id) + ",\"sprite_name\":" + Quote(a.sprite_name)
            + ",\"logical_dimensions\":" + Pair(a.logical_dimensions) + ",\"texture_dimensions\":" + Pair(a.texture_dimensions)
            + ",\"native_field\":" + Quote(a.native_field) + ",\"addressable_guid\":" + Quote(a.addressable_guid)
            + ",\"raster_reconstruction\":" + Quote(a.raster_reconstruction) + "}")) + "]}";
    private static MenuDepthCatalogue.Artwork Art(string source, Sprite sprite) => new()
    {
        source_id = source, sprite_name = sprite.name, logical_dimensions = new[] { (int)sprite.rect.width, (int)sprite.rect.height },
        texture_dimensions = new[] { sprite.texture.width, sprite.texture.height }, raster_reconstruction = "native-full-texture"
    };
    private static MenuDepthCatalogue.Field Entry(string id, Sprite sprite, byte[] bytes)
    {
        using SHA256 hash = SHA256.Create();
        return new MenuDepthCatalogue.Field { source_id = id, source_dimensions = new[] { sprite.texture.width, sprite.texture.height },
            depth_file = id + ".gmd", depth_dimensions = new[] { 4, 4 }, depth_bytes = bytes.Length,
            depth_sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant() };
    }
    private static byte[] DepthBytes(int width, int height, int sourceWidth, int sourceHeight, bool second)
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write(new byte[] { (byte)'G', (byte)'M', (byte)'D', (byte)'1' });
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write(3u); writer.Write(width * height * 2); writer.Write(0u); writer.Write(sourceWidth); writer.Write(sourceHeight);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            writer.Write((ushort)Math.Round((second ? .8 : .1 + .4 * x / (width - 1) + .3 * y / (height - 1)) * 65535));
        byte[] bytes = data.ToArray(); uint crc = uint.MaxValue;
        for (int i = 32; i < bytes.Length; i++)
        {
            crc ^= bytes[i]; for (int bit = 0; bit < 8; bit++) crc = crc >> 1 ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        Array.Copy(BitConverter.GetBytes(~crc), 0, bytes, 20, 4); return bytes;
    }
    private static List<UIVertex> NativeMesh(Image image)
    {
        using var helper = new VertexHelper();
        typeof(Image).GetMethod("OnPopulateMesh", Instance, null, new[] { typeof(VertexHelper) }, null)!.Invoke(image, new object[] { helper });
        var result = new List<UIVertex>(); helper.GetUIVertexStream(result); return result;
    }
    private static List<UIVertex> ModifiedMesh(Image image, MenuDepthGraphic graphic)
    {
        using var helper = new VertexHelper();
        typeof(Image).GetMethod("OnPopulateMesh", Instance, null, new[] { typeof(VertexHelper) }, null)!.Invoke(image, new object[] { helper });
        graphic.ModifyMesh(helper); var result = new List<UIVertex>(); helper.GetUIVertexStream(result); return result;
    }
    private static int Difference(Texture2D a, Texture2D b, RectInt rect)
    {
        int count = 0;
        for (int y = rect.yMin; y < rect.yMax; y++) for (int x = rect.xMin; x < rect.xMax; x++)
        {
            Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
            if (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) > .025f) count++;
        }
        return count;
    }
    private static Image MakeImage(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = 28; go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>(); image.rectTransform.sizeDelta = size; image.rectTransform.anchoredPosition = position;
        return image;
    }
    private sealed class TextureOwner : IDisposable
    {
        internal readonly Texture2D Image;
        internal TextureOwner(Texture2D image) { Image = image; }
        public void Dispose() => Object.DestroyImmediate(Image);
    }
    private sealed class Scene : IDisposable
    {
        internal readonly RectTransform Panel;
        internal readonly Image Image;
        internal readonly CanvasGroup Group;
        private readonly Camera _camera;
        private readonly RenderTexture _target;
        internal Scene(Sprite sprite)
        {
            var go = new GameObject("actual native illustration window", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)); go.layer = 28;
            Panel = go.GetComponent<RectTransform>(); Panel.sizeDelta = new Vector2(320, 180); Panel.position = new Vector3(0, 0, 3);
            Panel.localScale = Vector3.one * .01f; go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Group = go.GetComponent<CanvasGroup>();
            Image = MakeImage(Panel, "unchanged native image", new Vector2(240, 120), Vector2.zero); Image.sprite = sprite;
            MakeImage(Panel, "stable native control", new Vector2(105, 20), new Vector2(-103, -76)).color = Color.blue;
            var textObject = new GameObject("original native label", typeof(RectTransform)); textObject.layer = 28; textObject.transform.SetParent(Panel, false);
            var text = textObject.AddComponent<Text>(); text.text = "Original UI"; text.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); text.fontSize = 13;
            text.rectTransform.sizeDelta = new Vector2(105, 20); text.rectTransform.anchoredPosition = new Vector2(-103, -76);
            _camera = new GameObject("original orthographic UI capture").AddComponent<Camera>(); _camera.enabled = false;
            _camera.orthographic = true; _camera.aspect = 16f / 9f; _camera.nearClipPlane = .01f; _camera.farClipPlane = 10;
            _camera.cullingMask = 1 << 28; _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.clear;
            _target = new RenderTexture(320, 180, 24); _target.Create(); _camera.targetTexture = _target; Synchronize();
        }
        internal void Synchronize()
        {
            _camera.transform.SetPositionAndRotation(Panel.position - Panel.forward * 1f, Panel.rotation);
            _camera.orthographicSize = Panel.rect.height * Panel.lossyScale.y * .5f;
        }
        internal TextureOwner Capture(Vector3 eye, string name)
        {
            MenuDepthSources.BeforeCapture(Panel, new MenuDepthEye(false, eye, _camera, true));
            try { Canvas.ForceUpdateCanvases(); _camera.Render(); }
            finally { MenuDepthSources.AfterCapture(); }
            RenderTexture previous = RenderTexture.active; RenderTexture.active = _target;
            var image = new Texture2D(320, 180, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, 320, 180), 0, 0); image.Apply();
            RenderTexture.active = previous; File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG()); return new TextureOwner(image);
        }
        internal void AddMask()
        {
            var go = new GameObject("native RectMask2D", typeof(RectTransform), typeof(RectMask2D)); go.layer = 28;
            go.transform.SetParent(Panel, false); go.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 100);
            Image.transform.SetParent(go.transform, false); Image.rectTransform.anchoredPosition = Vector2.zero;
        }
        public void Dispose()
        {
            Object.DestroyImmediate(Panel.gameObject); _target.Release(); Object.DestroyImmediate(_target); Object.DestroyImmediate(_camera.gameObject);
        }
    }
}
