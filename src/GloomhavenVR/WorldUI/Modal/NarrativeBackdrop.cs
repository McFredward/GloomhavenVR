using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The native narrative dimmer is a desktop overlay, not narrative artwork.</summary>
internal static class NarrativeBackdrop
{
    internal enum Kind { Story, Encounter }

    // Build745 paired logs identify these exact original Black_Backdrop roles:
    // UI Story Box/UI Story Background and UI Event Window/Background. Both span
    // the 1920x1080 desktop, well beyond their actual dialog/card. Restrict this
    // opt-in to those roles: large original illustrations, frames and input
    // graphics remain native, including the event root's transparent raycaster.
    internal static Func<Graphic, bool>? Filter(UIWindow window, bool isStory)
    {
        if (isStory) return Filter(window.transform, Kind.Story);
        return window.GetComponent<UIEventPanel>() != null
            ? Filter(window.transform, Kind.Encounter) : null;
    }

    internal static Func<Graphic, bool> Filter(Transform root, Kind kind)
        => graphic => IsBackdrop(root, kind, graphic);

    internal static bool IsBackdrop(Transform root, Kind kind, Graphic graphic)
    {
        if (root == null || graphic == null || !(graphic is Image image)
            || graphic.GetComponent<Mask>() != null || !graphic.transform.IsChildOf(root)) return false;
        // The original Blur is a separate child Graphic. Disabling the dimmer's
        // Image must not disable its GameObject: that same branch owns the real
        // StoryImageViewer and its distinct imageHolder.
        if (image.name == "Blur")
        {
            Material? material = image.material;
            if (material == null || material.shader == null
                || material.shader.name.IndexOf("GrabPass", StringComparison.OrdinalIgnoreCase) < 0) return false;
            Image? parentImage = image.transform.parent != null ? image.transform.parent.GetComponent<Image>() : null;
            if (parentImage == null) return false;
            image = parentImage;
        }
        if (image.raycastTarget || image.sprite == null || image.sprite.name != "Black_Backdrop"
            || image.GetComponent<Mask>() != null) return false;
        Transform? parent = image.transform.parent;
        return kind == Kind.Story
            ? image.name == "UI Story Background" && parent != null && parent.name == "UI Story Box"
            : image.name == "Background" && ReferenceEquals(parent, root);
    }

    /// <summary>The story composite temporarily owns original children outside their UIWindow.</summary>
    internal sealed class Lease : IDisposable
    {
        private readonly Transform _root;
        private readonly List<Graphic> _hidden = new(2);
        private readonly List<Graphic> _scratch = new(32);

        internal Lease(Transform root) => _root = root;

        internal void Sync()
        {
            if (_root == null) return;
            _root.GetComponentsInChildren(true, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                Graphic graphic = _scratch[i];
                if (!IsBackdrop(_root, Kind.Story, graphic) || !graphic.enabled) continue;
                if (!_hidden.Contains(graphic)) _hidden.Add(graphic);
                graphic.enabled = false;
            }
            _scratch.Clear();
        }

        public void Dispose()
        {
            // Restore before the composite returns its original children. A
            // pooled graphic already borrowed elsewhere belongs to its new owner.
            for (int i = 0; i < _hidden.Count; i++)
            {
                Graphic graphic = _hidden[i];
                if (IsBackdrop(_root, Kind.Story, graphic)) graphic.enabled = true;
            }
            _hidden.Clear(); _scratch.Clear();
        }
    }
}
