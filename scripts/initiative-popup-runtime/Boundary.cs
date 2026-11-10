using UnityEngine;
// Only surrounding native component identities and logging are fixture ports.
// Full production material treatment, geometry, real uGUI/TMP, actual shaders
// and final rendered pixels execute unchanged.
internal sealed class CardEffects : MonoBehaviour { }
internal sealed class ItemCardEffects : MonoBehaviour { }
internal sealed class UIFX_MaterialFX_Control : MonoBehaviour { }
internal sealed class UITextMeshProMaterialAnimator : MonoBehaviour { }
internal sealed class InitiativeTrackEnemyBehaviour : MonoBehaviour { public Component? Avatar { get; set; } }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Info(string scope, string message) { }
        internal static void Warn(string scope, string message) { }
    }
}
