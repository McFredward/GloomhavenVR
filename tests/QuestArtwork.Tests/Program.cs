using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ArtworkOwner : MonoBehaviour { public Image headerImage = null!; }

public static partial class DiagnosticProgram
{
    public static int Checks;
    public static string Metrics = "";
    private static readonly List<Object> Owned = new();
    private static T Keep<T>(T item) where T : Object { Owned.Add(item); return item; }
    private static void Check(bool value, string message) { Checks++; if (!value) throw new Exception(message); }
    private static Texture2D Pattern(string name, byte phase)
    {
        var texture = Keep(new Texture2D(32, 24, TextureFormat.RGBA32, false, false));
        texture.name = name; texture.filterMode = FilterMode.Point;
        Fill(texture, phase); return texture;
    }
    private static void Fill(Texture2D texture, byte phase)
    {
        var pixels = new Color32[texture.width * texture.height];
        for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
            pixels[y * texture.width + x] = new Color32((byte)(phase + x * 3), (byte)(20 + y * 7), (byte)(phase + (x + y) * 2), (byte)(40 + x * 6));
        texture.SetPixels32(pixels); texture.Apply(false, false);
    }
    private static Sprite SpriteOf(Texture2D texture, Rect rect) => Keep(Sprite.Create(texture, rect, new Vector2(.31f, .63f), 117, 0, SpriteMeshType.FullRect, new Vector4(1, 2, 3, 4)));
    private static Color32[] Read(Texture source)
    {
        var target = Keep(new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)); target.Create();
        var previous = RenderTexture.active;
        FilterMode filter = source.filterMode; int aniso = source.anisoLevel;
        var result = Keep(new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false));
        try
        {
            // Qualify the copied mip-zero data, independently of backend-specific
            // anisotropic footprints on this small NPOT synthetic texture. Production
            // keeps trilinear/aniso8; the separate mip fixture checks minification.
            source.filterMode = FilterMode.Point; source.anisoLevel = 1;
            QuestTextureCopy.Copy(source, target); RenderTexture.active = target;
            result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); result.Apply(false, false);
            return result.GetPixels32();
        }
        finally { source.filterMode = filter; source.anisoLevel = aniso; RenderTexture.active = previous; }
    }
    private static void Pixels(Color32[] actual, Color32[] source, int width, int x, int y, int cropWidth, int cropHeight, string reason)
    {
        Check(actual.Length == cropWidth * cropHeight, reason + " dimensions");
        for (int v = 0; v < cropHeight; v++) for (int u = 0; u < cropWidth; u++)
        {
            Color32 a = actual[v * cropWidth + u], b = source[(y + v) * width + x + u];
            bool equal = Math.Abs(a.r - b.r) <= 1 && Math.Abs(a.g - b.g) <= 1 && Math.Abs(a.b - b.b) <= 1 && Math.Abs(a.a - b.a) <= 1;
            if (!equal) Debug.Log("Actual mismatch " + reason + " at=" + u + "," + v + " got=" + a + " expected=" + b);
            Check(equal, reason);
        }
    }
    public static IEnumerator Run()
    {
        QuestStandalonePlatform.Enabled = true;
        var originalTarget = RenderTexture.active; bool originalColor = GL.sRGBWrite;
        var sentinel = Keep(new RenderTexture(7, 9, 0)); sentinel.Create();
        RenderTexture.active = sentinel; GL.sRGBWrite = !sentinel.sRGB;
        Shader.EnableKeyword("STEREO_INSTANCING_ON");
        var atlas = Pattern("ordinary-atlas", 3); var expected = atlas.GetPixels32();
        Check(QuestArtworkRegion.CacheSuffix == "." + QuestTextureCopy.Recipe, "Quest silhouette cache is isolated from old builtin-copy masks");
        foreach (Rect crop in new[] { new Rect(5, 7, 8, 6), new Rect(2, 16, 11, 7), new Rect(0, 0, 32, 24) })
        {
            var alphaRegion = Keep(QuestArtworkRegion.Read(atlas, (int)crop.x, (int)crop.y, (int)crop.width, (int)crop.height));
            var alpha = alphaRegion.GetPixels32();
            for (int y = 0; y < (int)crop.height; y++) for (int x = 0; x < (int)crop.width; x++)
                Check(Math.Abs(alpha[y * (int)crop.width + x].a - expected[((int)crop.y + y) * atlas.width + (int)crop.x + x].a) <= 1,
                    "silhouette crop preserves exact native alpha");
        }
        Check(RenderTexture.active == sentinel && GL.sRGBWrite == !sentinel.sRGB, "silhouette region restores prior target and color state");
        QualifyPanelCopies();
        QualifyNativeHeader();
        int Count(string field) => ((IDictionary)typeof(CardFaceMipBake).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Count;
        var source = SpriteOf(atlas, new Rect(5, 7, 8, 6));
        Sprite? region = CardFaceMipBake.ReplacementFor(source);
        Check(region != null && region != source && region.texture.mipmapCount > 1, "actual sprite region is mipmapped");
        Check(RenderTexture.active == sentinel && GL.sRGBWrite == !sentinel.sRGB, "actual bake restores prior target and color state");
        Check(region!.rect.size == source.rect.size && region.pivot == source.pivot && region.border == source.border && region.pixelsPerUnit == source.pixelsPerUnit, "actual sprite geometry retained");
        Pixels(Read(region.texture), expected, 32, 5, 7, 8, 6, "Quest region preserves exact pixels and alpha under stereo keyword");
        Check(CardFaceMipBake.OriginalFor(region) == source && CardFaceMipBake.IsPrepared(source), "exact original and preparation retained");
        Check(CardFaceMipBake.ReplacementFor(source) == region, "unchanged original reuses exact replacement");
        var duplicate = SpriteOf(atlas, source.rect);
        Check(CardFaceMipBake.ReplacementFor(duplicate)!.texture == region.texture, "same object and region reuse pixel allocation");
        var unrelated = Pattern("ordinary-atlas", 97);
        var other = SpriteOf(unrelated, source.rect); Sprite otherRegion = CardFaceMipBake.ReplacementFor(other)!;
        Check(otherRegion.texture != region.texture, "same-name atlas regions do not alias unrelated content");
        Pixels(Read(otherRegion.texture), unrelated.GetPixels32(), 32, 5, 7, 8, 6, "same-name atlas retains its own actual region pixels");
        int identitiesBeforeUpdate = Count("s_atlasPixelsByIdentity");
        int regionsBeforeUpdate = Count("s_regionTextureByKey");
        uint prior = atlas.updateCount; Fill(atlas, 38);
        Check(atlas.updateCount != prior && !CardFaceMipBake.IsPrepared(source), "same-object source update invalidates preparation without GPU hash");
        Check(!CardFaceMipBake.IsBakedSprite(region), "stale sprite replacement is recognized");
        Sprite changed = CardFaceMipBake.PresentationFor(region)!;
        Check(changed != region && CardFaceMipBake.OriginalFor(changed) == source, "same-object sprite update resolves original rather than old baked pixels");
        Pixels(Read(changed.texture), atlas.GetPixels32(), 32, 5, 7, 8, 6, "updated atlas region uses current pixels");
        Check(Count("s_atlasPixelsByIdentity") == identitiesBeforeUpdate && Count("s_regionTextureByKey") == regionsBeforeUpdate,
            "same-object update retires old CPU readback and region keys rather than growing metadata");
        var portrait = Pattern("MegaTexture_CharacterPortraits", 17);
        Texture2D old = CardFaceMipBake.BakedTextureFor(portrait)!;
        var nextPortrait = Pattern("MegaTexture_CharacterPortraits", 91);
        Texture2D next = CardFaceMipBake.BakedTextureFor(nextPortrait)!;
        Check(old != next, "recreated same-name portrait generation cannot alias prior actor set");
        Pixels(Read(next), nextPortrait.GetPixels32(), 32, 0, 0, 32, 24, "recreated portrait generation keeps current pixels");
        var rawObject = Keep(new GameObject("initiative-raw", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)));
        var raw = rawObject.GetComponent<RawImage>(); raw.texture = portrait; raw.uvRect = new Rect(.25f, .125f, .5f, .75f);
        Rect originalUv = raw.uvRect;
        Check(PanelMipBake.RefreshRawTexture(raw) && raw.texture == old && raw.uvRect == originalUv, "raw portrait filtering preserves native UV rectangle");
        Check(!PanelMipBake.RefreshRawTexture(raw), "unchanged raw portrait is a no-write reuse");
        Fill(portrait, 73); Check(PanelMipBake.RefreshRawTexture(raw) && raw.texture != old && raw.uvRect == originalUv, "already baked raw portrait refreshes changed original contents");
        Pixels(Read(raw.texture), portrait.GetPixels32(), 32, 0, 0, 32, 24, "raw portrait refresh retains updated source pixels");
        Check(PanelMipBake.OriginalFor(raw.texture) == portrait, "raw restore provenance remains exact after update");
        var imageObject = Keep(new GameObject("card-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
        var image = imageObject.GetComponent<Image>(); image.sprite = changed;
        CardFaceMipBake.RestoreSprites(image); Check(image.sprite == source, "pool return restores exact native sprite");
        var owner = Keep(new GameObject("actual-artwork-owner")).AddComponent<ArtworkOwner>();
        owner.headerImage = image; image.sprite = changed;
        var artworkRecords = new List<string>();
        Application.LogCallback observe = (message, trace, type) => {
            if (message.StartsWith("[Quest startup] artwork ", StringComparison.Ordinal)) artworkRecords.Add(message);
        };
        Application.logMessageReceived += observe;
        try
        {
            VRLog.WantsDebug = false;
            QuestArtworkEvidence.SpriteField(owner, "headerImage", "disabled");
            VRLog.WantsDebug = true; QuestStandalonePlatform.Enabled = false;
            QuestArtworkEvidence.SpriteField(owner, "headerImage", "desktop");
            Check(artworkRecords.Count == 0, "artwork readback is gated before work on desktop and normal log level");
            QuestStandalonePlatform.Enabled = true;
            var hiddenHost = Keep(new GameObject("parked-native-capture"));
            image.transform.SetParent(hiddenHost.transform, false);
            hiddenHost.SetActive(false);
            image.enabled = false;
            QuestArtworkEvidence.Image(image, "hidden-header");
            Check(artworkRecords.Count == 1 && artworkRecords[0].Contains("enabled=False")
                && artworkRecords[0].Contains("hierarchy=False") && artworkRecords[0].Contains(" rgba6="),
                "inactive capture source and disabled image remain passively observable");
            Check(!image.enabled && !hiddenHost.activeSelf && image.sprite == changed,
                "observing a hidden native source never enables or replaces it");
            image.enabled = true;
            QuestArtworkEvidence.Image(image, "hidden-header");
            Check(artworkRecords.Count == 2 && artworkRecords[1].Contains("enabled=True"),
                "real loader enable transition is distinguished from the parked hierarchy");
            QuestArtworkEvidence.Image(null, "missing-native");
            QuestArtworkEvidence.Image(null, "missing-native");
            Check(artworkRecords.Count == 3 && artworkRecords[2].Contains("unavailable=image"),
                "missing actual native image emits one bounded reason instead of silence");
            raw.transform.SetParent(hiddenHost.transform, false);
            raw.enabled = false;
            QuestArtworkEvidence.RawImage(raw, "hidden-avatar");
            Check(artworkRecords.Count == 4 && artworkRecords[3].Contains("hierarchy=False")
                && artworkRecords[3].Contains(" rgba6="), "parked actual avatar can be observed without a hierarchy search");
            QuestArtworkEvidence.SpriteField(owner, "not-a-native-field", "bad-binding");
            Check(artworkRecords.Count == 5 && artworkRecords[4].Contains("unavailable=field"),
                "invalid reflection binding emits a bounded failure rather than silence");
            Check(!raw.enabled && raw.texture != null && !hiddenHost.activeSelf,
                "raw observation leaves native lifecycle and texture untouched");
            ((IDictionary)typeof(QuestArtworkEvidence).GetField("Seen", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Clear();
            typeof(QuestArtworkEvidence).GetField("_records", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0);
            artworkRecords.Clear();
            image.enabled = true; hiddenHost.SetActive(true);
            Camera previousCamera = Camera.current;
            var staleCamera = Keep(new GameObject("last-multipass-camera")).AddComponent<Camera>();
            staleCamera.enabled = false;
            Camera.SetupCurrent(staleCamera);
            RenderTexture.active = sentinel; GL.sRGBWrite = !sentinel.sRGB;
            for (int role = 0; role < 4; role++)
            {
                image.sprite = changed;
                QuestArtworkEvidence.SpriteField(owner, "headerImage", "bounded-" + role);
                QuestArtworkEvidence.SpriteField(owner, "headerImage", "bounded-" + role);
                image.sprite = otherRegion;
                QuestArtworkEvidence.SpriteField(owner, "headerImage", "bounded-" + role);
            }
            Check(RenderTexture.active == sentinel && GL.sRGBWrite == !sentinel.sRGB, "bounded artwork samples restore target and color state");
            Camera.SetupCurrent(previousCamera);
            RenderTexture.active = sentinel; GL.sRGBWrite = !sentinel.sRGB;
            QuestArtworkEvidence.Raw(raw, "after-limit");
            Check(artworkRecords.Count == 8, "at most two actual artwork generations per role and eight total records");
            foreach (string record in artworkRecords)
            {
                Check(System.Text.Encoding.UTF8.GetByteCount(record) <= 1000 && record.Contains("rect=") && record.Contains("rectClip="),
                    "complete artwork consumer and pixels record fits Android logcat");
                int firstStart = record.IndexOf(" rgba6=", StringComparison.Ordinal) + 7;
                int secondStart = record.IndexOf(" outputRgba6=", StringComparison.Ordinal) + 13;
                Check(firstStart >= 7 && secondStart >= 13 && Convert.FromBase64String(record.Substring(firstStart, 192)).Length == 144
                    && Convert.FromBase64String(record.Substring(secondStart)).Length == 144, "actual paired6x6 source/effective crops and draw state fit one record");
            }
        }
        finally { Application.logMessageReceived -= observe; VRLog.WantsDebug = false; }
        var budget = typeof(CardFaceMipBake).GetField("s_bakedVramBytes", BindingFlags.Static | BindingFlags.NonPublic)!;
        long previousBudget = (long)budget.GetValue(null)!;
        budget.SetValue(null, typeof(CardFaceMipBake).GetField("MaxBakedVramBytes", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue());
        Fill(atlas, 82);
        Check(CardFaceMipBake.PresentationFor(changed) == source, "a refused updated sprite uses current native pixels rather than stale baked art");
        Fill(portrait, 88);
        Check(PanelMipBake.RefreshRawTexture(raw) && raw.texture == portrait, "a refused updated raw portrait uses its current native source");
        budget.SetValue(null, previousBudget);
        int metadataLimit = (int)typeof(CardFaceMipBake).GetField("MaxBakedTextures", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        for (int i = 0; i < metadataLimit + 1; i++)
        {
            var tiny = Keep(new Texture2D(1, 1, TextureFormat.RGBA32, false)); tiny.name = "changing-nonbakeable-" + i;
            Check(CardFaceMipBake.BakedTextureFor(tiny) == null, "unbakeable texture generation keeps original");
        }
        Check(Count("s_questIdentities") <= metadataLimit && Count("s_questSources") <= metadataLimit
            && Count("s_bakedByTexture") <= metadataLimit, "new null verdicts cannot exceed bounded Quest metadata cache");
        Check(CardFaceMipBake.BakedTextureFor(nextPortrait) == next, "metadata cap retains current prepared results");
        Shader.DisableKeyword("STEREO_INSTANCING_ON"); QuestStandalonePlatform.Enabled = false;
        Check(QuestArtworkRegion.CacheSuffix == string.Empty, "desktop silhouette cache filename stays unchanged");
        // The desktop path deliberately retains its caller's established color
        // write state; the Quest helper is what isolates the earlier sentinel.
        GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
        var desktopA = Pattern("desktop-immutable-alias", 11); var desktopB = Pattern("desktop-immutable-alias", 11);
        var desktopCopy = CardFaceMipBake.BakedTextureFor(desktopA);
        Check(desktopCopy == CardFaceMipBake.BakedTextureFor(desktopB), "desktop immutable alias behavior stays unchanged");
        var desktopRegion = CardFaceMipBake.ReplacementFor(SpriteOf(desktopA, new Rect(1, 2, 6, 5)))!;
        Pixels(Read(desktopCopy!), desktopA.GetPixels32(), 32, 0, 0, 32, 24, "desktop copy retains exact pixel/alpha path");
        Check(desktopRegion.texture == desktopCopy, "desktop existing whole-atlas reuse remains unchanged");
        RenderTexture.active = originalTarget; GL.sRGBWrite = originalColor;
        Metrics = "gpu=" + SystemInfo.graphicsDeviceType + "; colorSpace=" + QualitySettings.activeColorSpace + "; actual source pixels, regions, alpha, source revision and raw provenance verified";
        yield break;
    }
    public static void Cleanup()
    {
        Shader.DisableKeyword("STEREO_INSTANCING_ON");
        foreach (var item in Owned) if (item != null) Object.DestroyImmediate(item); Owned.Clear();
    }
}
