using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Scene actor symbols never acquire UI readability geometry. Check the
/// original component ownership, independently of a converter's later opt-out.</summary>
internal static class MrBackingSceneSymbols
{
    internal static bool Contains(Transform? target) => target != null
        && (target.GetComponentInParent<WorldspacePanelUIController>(true) != null
            || target.GetComponentInParent<WorldspaceUI.AttackModBar>(true) != null
            || target.GetComponentInParent<AttackModElementUI>(true) != null);
}
