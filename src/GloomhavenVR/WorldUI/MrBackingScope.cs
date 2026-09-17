using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>MR backing follows the surface owner's explicit content root. Parent layout canvases
/// and sibling input areas do not become opaque artwork. Ancestors are still visited for masks.</summary>
internal static class MrBackingScope
{
    internal static bool Valid(Transform target, Transform? contentRoot) => contentRoot == null
        || ReferenceEquals(contentRoot, target) || contentRoot.IsChildOf(target);

    internal static bool Visit(Transform node, Transform? contentRoot) => contentRoot == null
        || ReferenceEquals(node, contentRoot) || node.IsChildOf(contentRoot) || contentRoot.IsChildOf(node);

    internal static bool Paint(Transform node, Transform? contentRoot) => contentRoot == null
        || ReferenceEquals(node, contentRoot) || node.IsChildOf(contentRoot);
}
