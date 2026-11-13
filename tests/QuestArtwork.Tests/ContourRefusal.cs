using GloomhavenVR.Cards;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static void QualifyContourRefusal()
    {
        var root = Keep(new GameObject("actual-contour-face", typeof(RectTransform))).GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(224, 320);
        var readable = Pattern("original-contour-readable", 43);
        var gpuOnly = Pattern("original-contour-gpu-only", 71); gpuOnly.Apply(false, true);
        Image Add(Texture2D source, string name)
        {
            var child = Keep(new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            child.transform.SetParent(root, false);
            var image = child.GetComponent<Image>(); image.rectTransform.sizeDelta = root.sizeDelta;
            image.sprite = SpriteOf(source, new Rect(0, 0, source.width, source.height));
            image.preserveAspect = false; return image;
        }
        Image cpuImage = Add(readable, "readable-outline");
        Image gpuImage = Add(gpuOnly, "unreadable-outline");
        object state = ActualSilhouette.State();
        CardMesh.Writes = 0; CardMesh.LastAlpha = null;
        ActualSilhouette.Capture(root, state);
        Check(CardMesh.Writes == 0 && CardMesh.LastAlpha == null,
            "actual contour caller never writes a mask from GPU-only original alpha");
        Check(ActualSilhouette.Reason(state) == "native-alpha-unavailable" && ActualSilhouette.Attempts(state) == 1,
            "actual contour refusal uses its existing reason and attempt ownership");
        for (int repeat = 0; repeat < 16; repeat++) ActualSilhouette.Capture(root, state);
        Check(ActualSilhouette.Attempts(state) == 1 && CardMesh.Writes == 0,
            "unchanged refused outline never retries or mutates the existing complete card body");
        gpuImage.gameObject.SetActive(false);
        state = ActualSilhouette.State();
        // The UI may already wear our no-longer-readable mip copy. The contour
        // must resolve its native readable original rather than rejecting that copy.
        cpuImage.sprite = CardFaceMipBake.PresentationFor(cpuImage.sprite);
        ActualSilhouette.Capture(root, state);
        Check(CardMesh.Writes == 1 && CardMesh.LastAlpha != null,
            "actual contour caller reads native alpha behind its owned mip replacement");
        int half = CardMesh.LastAlpha!.Length / 2, upper = 0, lower = 0;
        for (int i = 0; i < CardMesh.LastAlpha.Length; i++)
            if (CardMesh.LastAlpha[i] > 128) { if (i < half) lower++; else upper++; }
        Check(upper > half / 4 && lower > half / 4,
            "trusted native contour retains real coverage in both card halves");
        root.gameObject.SetActive(false);
    }
}
