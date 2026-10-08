using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    /// <summary>Read-only capture ownership for the Quest movie output boundary.</summary>
    internal static bool OwnsVideoCapture(Camera camera) =>
        camera != null && camera.isActiveAndEnabled && camera.targetTexture != null
        && CapturedSet.TryGetValue(camera, out CapturedCamera record)
        && record.Owner._visible && camera.targetTexture == record.Owner.TargetFor(record);

    internal static Material? VideoConsumer(Camera camera) =>
        OwnsVideoCapture(camera) && CapturedSet.TryGetValue(camera, out CapturedCamera record)
            ? record.IsUi && record.Owner._splitRouting
                ? record.Owner._glassMaterial : record.Owner._screenMaterial
            : null;

    internal static Camera? FinalVideoCamera(Camera source)
    {
        if (!OwnsVideoCapture(source) || !CapturedSet.TryGetValue(source, out CapturedCamera origin)) return null;
        Camera final = source;
        bool finalUi = origin.IsUi;
        foreach (var pair in CapturedSet)
        {
            Camera candidate = pair.Key;
            CapturedCamera record = pair.Value;
            if (record.Owner != origin.Owner || !OwnsVideoCapture(candidate)
                || candidate.targetTexture != source.targetTexture) continue;
            if (candidate.depth > final.depth || candidate.depth == final.depth && record.IsUi && !finalUi)
            { final = candidate; finalUi = record.IsUi; }
        }
        return final;
    }

    internal static Texture? VideoGlassCapture(Camera camera) =>
        OwnsVideoCapture(camera) && CapturedSet.TryGetValue(camera, out CapturedCamera record)
            && record.Owner._splitRouting ? record.Owner._uiRt : null;
}
