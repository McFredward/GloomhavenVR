using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static byte[] AlphaFile(string name, int width, int height, byte[] alpha, int copies = 1)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
            deflate.Write(alpha, 0, alpha.Length);
        byte[] encoded = compressed.ToArray(), key = Encoding.UTF8.GetBytes(name);
        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("GHVA1")); writer.Write(copies);
            for (int copy = 0; copy < copies; copy++)
            {
                writer.Write(key.Length); writer.Write(key); writer.Write(width); writer.Write(height);
                writer.Write(encoded.Length); writer.Write(encoded);
            }
        }
        return result.ToArray();
    }

    private static void QualifyOwnedAlpha()
    {
        string originalDirectory = QuestStandalonePlatform.ResourceDirectory;
        string root = Path.Combine(Application.temporaryCachePath, "quest-owned-alpha-" + Guid.NewGuid().ToString("N"));
        int generation = 0;
        void Publish(byte[] payload)
        {
            string directory = Path.Combine(root, (++generation).ToString()); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, QuestCardAlpha.FileName), payload);
            QuestStandalonePlatform.ResourceDirectory = directory;
        }
        var source = Pattern("exact-owned-outline", 53);
        Color32[] original = source.GetPixels32();
        for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++)
            original[y * source.width + x].a = (byte)((x < 4 || x >= source.width - 4)
                && (y < 4 || y >= source.height - 4) ? 0 : 255);
        source.SetPixels32(original); source.Apply(false);
        var alpha = new byte[original.Length];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = original[i].a;
        byte[] payload = AlphaFile(source.name, source.width, source.height, alpha);
        var face = Keep(new GameObject("owned-alpha-native-face", typeof(RectTransform))).GetComponent<RectTransform>();
        face.sizeDelta = new Vector2(224, 320);
        var graphic = Keep(new GameObject("native-background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
        graphic.transform.SetParent(face, false);
        var image = graphic.GetComponent<Image>(); image.rectTransform.sizeDelta = face.sizeDelta;
        image.sprite = SpriteOf(source, new Rect(0, 0, source.width, source.height));
        object referenceState = ActualSilhouette.State(); CardMesh.Writes = 0; CardMesh.LastAlpha = null;
        ActualSilhouette.Capture(face, referenceState);
        Check(CardMesh.Writes == 1 && CardMesh.LastAlpha != null, "readable original establishes ordinary native union reference");
        byte[] reference = (byte[])CardMesh.LastAlpha!.Clone();
        source.Apply(false, true);
        try
        {
            Publish(payload);
            var region = QuestArtworkRegion.Read(source, 7, 5, 11, 9);
            Check(region != null, "owned CPU alpha is available for a GPU-only native texture");
            Color32[] actual = Keep(region!).GetPixels32();
            for (int y = 0; y < 9; y++) for (int x = 0; x < 11; x++)
                Check(actual[y * 11 + x].a == original[(y + 5) * source.width + x + 7].a,
                    "owned alpha crops exact original mip-zero bytes without a GPU readback");
            object state = ActualSilhouette.State(); CardMesh.Writes = 0; CardMesh.LastAlpha = null;
            bool debug = VRLog.WantsDebug; VRLog.WantsDebug = true;
            try { ActualSilhouette.Capture(face, state); }
            finally { VRLog.WantsDebug = debug; }
            Check(CardMesh.Writes == 1 && CardMesh.LastAlpha != null,
                "actual contour caller obtains its complete native GPU-only union from owned CPU alpha");
            Check(CardMesh.LastAlpha!.Length == reference.Length, "owned alpha keeps ordinary native union dimensions");
            for (int i = 0; i < reference.Length; i++)
                Check(CardMesh.LastAlpha[i] == reference[i], "owned alpha preserves every ordinary native union sample");
            string record = "";
            Application.LogCallback observe = (message, trace, type) => {
                if (message.StartsWith("[Quest startup] artwork role=ability-header", StringComparison.Ordinal)) record = message;
            };
            Application.logMessageReceived += observe; VRLog.WantsDebug = true;
            try { QuestArtworkEvidence.Image(image, "ability-header"); }
            finally { Application.logMessageReceived -= observe; VRLog.WantsDebug = debug; }
            Check(record.Contains("cpuAlpha=ready") && record.Contains("contour=applied")
                && Encoding.UTF8.GetByteCount(record) <= 1000,
                "existing bounded native artwork record names alpha delivery and actual contour outcome");

            var interiorTexture = Pattern("unknown-native-interior", 71); interiorTexture.Apply(false, true);
            var decoration = Keep(new GameObject("unreadable-interior", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            decoration.transform.SetParent(face, false);
            var interior = decoration.GetComponent<Image>(); interior.rectTransform.sizeDelta = new Vector2(80, 100);
            interior.sprite = SpriteOf(interiorTexture, new Rect(0, 0, interiorTexture.width, interiorTexture.height));
            state = ActualSilhouette.State(); CardMesh.Writes = 0; CardMesh.LastAlpha = null;
            ActualSilhouette.Capture(face, state);
            Check(CardMesh.Writes == 1 && CardMesh.LastAlpha != null,
                "known native alpha proves an unreadable interior cannot change the ordinary union");
            for (int i = 0; i < reference.Length; i++)
                Check(CardMesh.LastAlpha![i] == reference[i], "dominated unknown interior retains every exact native sample");
            interior.rectTransform.sizeDelta = face.sizeDelta;
            state = ActualSilhouette.State(); CardMesh.Writes = 0; CardMesh.LastAlpha = null;
            ActualSilhouette.Capture(face, state);
            Check(CardMesh.Writes == 0 && ActualSilhouette.Reason(state) == "native-alpha-unavailable",
                "unknown source extending beyond known cutouts cannot commit a partial contour");
            interior.rectTransform.sizeDelta = new Vector2(80, 100);
            var translucent = (byte[])alpha.Clone();
            for (int i = 0; i < translucent.Length; i++) if (translucent[i] > 0) translucent[i] = 127;
            Publish(AlphaFile(source.name, source.width, source.height, translucent));
            state = ActualSilhouette.State(); CardMesh.Writes = 0; CardMesh.LastAlpha = null;
            ActualSilhouette.Capture(face, state);
            Check(CardMesh.Writes == 0 && ActualSilhouette.Reason(state) == "native-alpha-unavailable",
                "partly opaque native interior does not dominate an unknown opaque decoration");
            decoration.SetActive(false);

            Publish(AlphaFile(source.name, source.width + 1, source.height, new byte[(source.width + 1) * source.height]));
            Check(QuestCardAlpha.Read(source, 0, 0, source.width, source.height) == null,
                "owned alpha refuses mismatched native dimensions instead of guessing a resized contour");
            Publish(AlphaFile(source.name + "-different", source.width, source.height, alpha));
            Check(QuestCardAlpha.Read(source, 0, 0, source.width, source.height) == null,
                "owned alpha refuses another original texture with equal dimensions");
            Publish(AlphaFile(source.name, source.width, source.height, alpha, 2));
            Check(QuestCardAlpha.Read(source, 0, 0, source.width, source.height) == null,
                "owned alpha refuses ambiguous duplicate original texture names");
            Publish(AlphaFile(source.name, source.width, source.height, new byte[alpha.Length - 1]));
            Check(QuestCardAlpha.Read(source, 0, 0, source.width, source.height) == null,
                "owned alpha refuses a truncated decoded original without a partial silhouette");
            Publish(AlphaFile(source.name, source.width, source.height, new byte[alpha.Length + 1]));
            Check(QuestCardAlpha.Read(source, 0, 0, source.width, source.height) == null,
                "owned alpha refuses excess decoded bytes");
            var trailing = new byte[payload.Length + 1]; Array.Copy(payload, trailing, payload.Length); Publish(trailing);
            Check(QuestCardAlpha.Read(source, 0, 0, source.width, source.height) == null,
                "owned alpha refuses unindexed trailing data");
            Publish(payload);
            Check(QuestCardAlpha.Read(source, -1, 0, 2, 2) == null
                && QuestCardAlpha.Read(source, source.width - 1, 0, 2, 2) == null,
                "owned alpha crop stays within its exact native texture");

            TextAsset nativeAlpha = Resources.Load<TextAsset>("NativeCardAlpha");
            foreach (string nativeKind in new[] { "NativeHeader", "NativeItem" })
            {
                Sprite native = Resources.Load<Sprite>(nativeKind);
                if (nativeAlpha == null || native == null) continue;
                Publish(nativeAlpha.bytes);
                face.sizeDelta = nativeKind == "NativeItem" ? new Vector2(270, 258) : new Vector2(294, 450);
                image.rectTransform.sizeDelta = face.sizeDelta;
                image.sprite = native; image.preserveAspect = false;
                state = ActualSilhouette.State(); CardMesh.Writes = 0; CardMesh.LastAlpha = null;
                ActualSilhouette.Capture(face, state);
                Check(CardMesh.Writes == 1 && CardMesh.LastAlpha != null,
                    "actual retained native GPU-only card receives its owned original contour: " + nativeKind);
                int solid = 0, clear = 0;
                foreach (byte opacity in CardMesh.LastAlpha!) { if (opacity >= 128) solid++; else clear++; }
                Check(solid > CardMesh.LastAlpha!.Length / 2 && clear > 32,
                    "native card contour retains real printed area and authored cutouts: " + nativeKind);
                int footprintWidth = 224, footprintHeight = CardMesh.LastAlpha.Length / footprintWidth;
                Vector2[]? contour = CardContour.Extract(CardMesh.LastAlpha, footprintWidth, footprintHeight, out string? refusal);
                Check(contour != null && contour.Length >= 8 && contour.Length <= CardContour.MaxVertices,
                    "ordinary contour extraction accepts the exact owned native card: " + nativeKind + ": " + refusal);
                Mesh? body = CardContour.BuildBody(contour!, .0635f, .088f, .0015f, .02f, out refusal);
                Check(body != null && body.subMeshCount == 2 && body.vertexCount > contour!.Length,
                    "ordinary native alpha constructs a real punched card mesh: " + refusal);
                Keep(body!);
                Check(body!.bounds.size == new Vector3(.0635f, .088f, .0015f),
                    "punched card keeps the same nominal card dimensions for layout and grabbing");
                UnityEngine.Debug.Log("Owned native alpha: source=" + native.texture.name + " size="
                    + native.texture.width + "x" + native.texture.height + " solid=" + solid + " clear=" + clear
                    + " contour=" + contour!.Length + " meshVertices=" + body.vertexCount);
            }
        }
        finally
        {
            face.gameObject.SetActive(false); QuestStandalonePlatform.ResourceDirectory = originalDirectory;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
