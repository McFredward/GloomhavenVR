using GloomhavenVR.Board;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>A small repeated downward motion above the existing Steam identity picture.</summary>
internal sealed class DecisionWaitArrow
{
    private GameObject? _root;
    private Material? _material;
    internal bool Tick(Transform parent, Vector3 avatarSeat, bool visible)
    {
        if (!visible) { if (_root != null) _root.SetActive(false); return false; }
        bool built = _root == null;
        if (built)
        {
            if (_material != null) Object.Destroy(_material);
            _root = new GameObject("GloomhavenVR.WaitingForPlayerArrow");
            _root.transform.SetParent(parent, false);
            _material = BoardVisual.Unlit(new Color(0.82f, 0.64f, 0.30f, 1f));
            Part("Stem", new Vector2(0.006f, 0.035f), new Vector3(0f, 0.0175f, 0f), 0f);
            Part("Left", new Vector2(0.006f, 0.025f), new Vector3(-0.008f, 0.004f, 0f), 45f);
            Part("Right", new Vector2(0.006f, 0.025f), new Vector3(0.008f, 0.004f, 0f), -45f);
            Core.VRLayers.Apply(_root);
        }
        _root!.SetActive(true);
        float bounce = (1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.5f)) * 0.005f;
        _root.transform.localPosition = avatarSeat + new Vector3(0f, 0.063f + bounce, -0.006f);
        return built;
    }
    private void Part(string name, Vector2 size, Vector3 position, float angle)
    {
        Renderer r = BoardVisual.Quad(_root!.transform, name, size, _material!);
        r.transform.localPosition = position;
        r.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
    internal void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
        if (_material != null) Object.Destroy(_material);
        _root = null; _material = null;
    }
}
