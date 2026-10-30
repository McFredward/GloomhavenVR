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
        if (window.GetComponent<UIEventPanel>() != null) return Filter(window.transform, Kind.Encounter);
        // The loadout viewer exists before its addressable illustration arrives.
        // Build747 hid it only at StoryComposite.Park; the earlier native fade,
        // first content fit and first eye capture had already painted its dimmer.
        UILoadoutManager? manager = window.GetComponent<UILoadoutManager>();
        StoryImageViewer? viewer = manager != null && manager.questInfo != null ? manager.questInfo.imagePaper : null;
        return viewer != null && viewer.imageHolder != null
            ? new LoadoutLease(window, viewer.imageHolder.rectTransform).Contains : null;
    }

    internal static Func<Graphic, bool> Filter(Transform root, Kind kind)
    {
        // Capture neutralizes a native GrabPass material before Release. Retain
        // the exact original child/parent/source identity so that material change
        // cannot strand an owned Blur disabled or admit a pooled foreign graphic.
        var blurs = new Dictionary<Image, (Image Parent, Sprite Source)>();
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.name != "Blur" || !IsBackdrop(root, kind, image)) continue;
            Image parent = image.transform.parent.GetComponent<Image>();
            blurs.Add(image, (parent, parent.sprite));
        }
        return graphic => IsBackdrop(root, kind, graphic)
            || (graphic is Image image && blurs.TryGetValue(image, out var original)
                && image.transform.parent == original.Parent.transform && image.sprite == null
                && image.GetComponent<Mask>() == null && original.Parent.sprite == original.Source
                && IsBackdrop(root, kind, original.Parent));
    }

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
        private readonly Dictionary<Graphic, (Transform Parent, Image Dimmer, Sprite Source)> _borrowed = new();

        internal Lease(Transform root, ConvertedPanel? previous = null)
        {
            _root = root;
            if (previous == null || previous.BackgroundFilter == null) return;
            // The source may already be converted when Park moves its children.
            // Take its exact receipts while the previous native role still owns
            // them; after the move its release must refuse this foreign subtree.
            for (int i = previous.HiddenBackgrounds.Count - 1; i >= 0; i--)
            {
                Graphic graphic = previous.HiddenBackgrounds[i];
                if (!(graphic is Image image) || !previous.BackgroundFilter(graphic)) continue;
                Image? dimmer = image.name == "Blur" ? image.transform.parent?.GetComponent<Image>() : image;
                if (dimmer == null || !IsBackdrop(previous.Target, Kind.Story, dimmer)) continue;
                Transform? parent = image.transform.parent;
                if (parent == null) continue;
                if (!_borrowed.ContainsKey(graphic))
                {
                    _borrowed.Add(graphic, (parent, dimmer, dimmer.sprite));
                    _hidden.Add(graphic);
                }
                previous.HiddenBackgrounds.RemoveAt(i);
            }
        }

        private bool OwnsBorrowed(Graphic graphic) => graphic != null
            && _borrowed.TryGetValue(graphic, out var original) && graphic.transform.IsChildOf(_root)
            && graphic.transform.parent == original.Parent && original.Dimmer != null
            && original.Dimmer.sprite == original.Source && IsBackdrop(_root, Kind.Story, original.Dimmer)
            && (graphic == original.Dimmer || (graphic is Image image && image.sprite == null
                && image.name == "Blur" && image.GetComponent<Mask>() == null));

        internal void Sync()
        {
            if (_root == null) return;
            _root.GetComponentsInChildren(true, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                Graphic graphic = _scratch[i];
                if ((!IsBackdrop(_root, Kind.Story, graphic) && !OwnsBorrowed(graphic)) || !graphic.enabled) continue;
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
                if (IsBackdrop(_root, Kind.Story, graphic) || OwnsBorrowed(graphic)) graphic.enabled = true;
            }
            _hidden.Clear(); _scratch.Clear(); _borrowed.Clear();
        }
    }

    /// <summary>The active composite also borrows the loadout viewer's own desktop dimmer.</summary>
    internal sealed class LoadoutLease : IDisposable
    {
        private readonly UIWindow _owner;
        private readonly UILoadoutManager? _manager;
        private readonly UILoadoutQuestWindow? _info;
        private readonly StoryImageViewer? _viewer;
        private readonly Image? _picture;
        private readonly Image? _dimmer;
        private readonly Sprite? _source;
        private readonly List<Image> _graphics = new(3);
        private readonly List<Image> _hidden = new(3);

        internal LoadoutLease(UIWindow owner, RectTransform picture)
        {
            _owner = owner;
            _manager = owner.GetComponent<UILoadoutManager>();
            _info = _manager != null ? _manager.questInfo : null;
            _viewer = _info != null ? _info.imagePaper : null;
            _picture = picture.GetComponent<Image>();
            _dimmer = _viewer != null ? _viewer.GetComponent<Image>() : null;
            if (!OwnsViewer() || _dimmer == null || _dimmer.raycastTarget
                || _dimmer.GetComponent<Mask>() != null || _dimmer.sprite == null
                || _dimmer.sprite.name != "Black_Backdrop") return;

            _source = _dimmer.sprite;
            _graphics.Add(_dimmer);
            // These are the two authored, direct GrabPass siblings of the
            // Paper/Image branch. Resolve them once, never scan the host per tick.
            AddBlur("Blur");
            AddBlur("Blur (1)");
        }

        private bool OwnsViewer() => _owner != null && _manager != null && _info != null
            && _viewer != null && _picture != null && _manager.questInfo == _info
            && _info.imagePaper == _viewer && _info.transform.parent == _owner.transform
            && _viewer.transform == _info.transform && _viewer.container == _info.gameObject
            && _viewer.imageHolder == _picture && _picture.transform.IsChildOf(_viewer.transform);

        private void AddBlur(string name)
        {
            Transform? child = _viewer!.transform.Find(name);
            Image? image = child != null ? child.GetComponent<Image>() : null;
            Material? material = image != null ? image.material : null;
            if (image != null && image.sprite == null && image.GetComponent<Mask>() == null
                && material != null && material.shader != null
                && material.shader.name == "Custom/SimpleGrabPassBlur") _graphics.Add(image);
        }

        private bool StillOwned(Image image) => image != null && OwnsViewer() && _dimmer != null
            && _dimmer.sprite == _source && (image == _dimmer
                || (image.transform.parent == _viewer!.transform && image.sprite == null));

        internal bool Contains(Graphic graphic) => graphic is Image image
            && _graphics.Contains(image) && StillOwned(image);

        internal void Sync()
        {
            for (int i = 0; i < _graphics.Count; i++)
            {
                Image image = _graphics[i];
                if (!StillOwned(image) || !image.enabled) continue;
                if (!_hidden.Contains(image)) _hidden.Add(image);
                image.enabled = false;
            }
        }

        public void Dispose()
        {
            // Restore while the same native controller chain still owns the
            // original image, before the composite hands back the story roots.
            // Disabled native graphics and objects already reused elsewhere stay untouched.
            for (int i = 0; i < _hidden.Count; i++)
            {
                Image image = _hidden[i];
                if (!StillOwned(image)) continue;
                ConvertedPanel? panel = ModalFallback.PanelFor(_owner);
                if (panel != null && panel.HideBackground && panel.BackgroundFilter != null
                    && panel.BackgroundFilter(image))
                {
                    // Park may precede Convert. The converter skips already
                    // disabled images, so hand this original receipt to it
                    // rather than re-enable a backing inside its live window.
                    if (!panel.HiddenBackgrounds.Contains(image)) panel.HiddenBackgrounds.Add(image);
                }
                else image.enabled = true;
            }
            _hidden.Clear(); _graphics.Clear();
        }
    }
}
