using GloomhavenVR.Net;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Cabinet layout of the original merchant row, without another item title or icon.
/// Native text, price/stock colours, gold and reputation arrows remain source-driven. The
/// caller applies this after each mirror sync and before publishing the actual clone, so
/// observers receive the same originals and geometry rather than composing a second caption.</summary>
internal sealed class TownServiceMerchantCaption
{
    private const float FontSize = 40f;
    private readonly RemoteWidgetMirror _mirror;
    // These paths are the serialized UIShopItemSlot variant in sharedassets4.assets (GO330).
    // Content contains only the duplicate name, item icon and their highlight/warning copies.
    // Price and Amount are independent siblings. Never hide or alter the native source row.
    private readonly Transform?[] _sources;
    private readonly Transform?[] _clones = new Transform?[9];
    private readonly TMP_Text?[] _text = new TMP_Text?[3];
    private int _stamp = -1;

    internal TownServiceMerchantCaption(RemoteWidgetMirror mirror, Transform original)
    {
        _mirror = mirror;
        _sources = new[] { original.Find("Content"), original.Find("Amount"), original.Find("Price"),
            original.Find("Price/TextMeshPro Text"), original.Find("Price/Icon"), original.Find("Price/Reputation"),
            original.Find("Price/Price warning"), original.Find("Price/Price warning/TextMeshPro Text (1)"),
            original.Find("Price/Price warning/Icon (1)") };
    }

    internal void Apply()
    {
        if (_stamp != _mirror.RebuildStamp)
        {
            _stamp = _mirror.RebuildStamp;
            for (int i = 0; i < _sources.Length; i++) _clones[i] = _sources[i] != null ? _mirror.CloneOf(_sources[i]!) : null;
            _text[0] = _clones[1]?.GetComponent<TMP_Text>();
            _text[1] = _clones[3]?.GetComponent<TMP_Text>();
            _text[2] = _clones[7]?.GetComponent<TMP_Text>();
        }
        if (_clones[0] != null && _clones[0]!.gameObject.activeSelf) _clones[0]!.gameObject.SetActive(false);
        // Keep a compact, centered one-line price / quantity group. The same native
        // reputation arrow preserves its source flip (discount/surcharge) and active flag.
        Place(_clones[1], new Vector2(88f, 0f), new Vector2(124f, 70f));
        Place(_clones[2], new Vector2(-70f, 0f), new Vector2(170f, 70f));
        Place(_clones[3], Vector2.zero, new Vector2(90f, 70f));
        Place(_clones[4], new Vector2(-60f, 0f), new Vector2(30f, 30f));
        Place(_clones[5], new Vector2(61f, 0f), new Vector2(24f, 24f));
        Place(_clones[6], Vector2.zero, new Vector2(170f, 70f));
        Place(_clones[7], Vector2.zero, new Vector2(90f, 70f));
        Place(_clones[8], new Vector2(-60f, 0f), new Vector2(30f, 30f));
        Text(_text[0]); Text(_text[1]); Text(_text[2]);
    }

    private static void Place(Transform? node, Vector2 position, Vector2 size)
    {
        if (node is not RectTransform rect) return;
        Vector2 center = new(.5f, .5f);
        if (!rect.anchorMin.Equals(center)) rect.anchorMin = center;
        if (!rect.anchorMax.Equals(center)) rect.anchorMax = center;
        if (!rect.pivot.Equals(center)) rect.pivot = center;
        if (!rect.sizeDelta.Equals(size)) rect.sizeDelta = size;
        Vector3 point = new(position.x, position.y, rect.anchoredPosition3D.z);
        if (!rect.anchoredPosition3D.Equals(point)) rect.anchoredPosition3D = point;
    }

    private static void Text(TMP_Text? text)
    {
        if (text == null) return;
        if (text.enableAutoSizing) text.enableAutoSizing = false;
        if (text.fontSize != FontSize) text.fontSize = FontSize;
        if (text.enableWordWrapping) text.enableWordWrapping = false;
        if (text.alignment != TextAlignmentOptions.Center) text.alignment = TextAlignmentOptions.Center;
    }
}
