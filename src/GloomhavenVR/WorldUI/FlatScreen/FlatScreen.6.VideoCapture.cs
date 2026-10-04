using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    /// <summary>Read-only capture ownership for the Quest movie output boundary.</summary>
    internal static bool OwnsVideoCapture(Camera camera) =>
        camera != null && camera.isActiveAndEnabled && camera.targetTexture != null
        && CapturedSet.TryGetValue(camera, out CapturedCamera record)
        && record.Owner._visible && camera.targetTexture == record.Owner.TargetFor(record);
}
