using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>A quiet, cap-shaped light on the original local or mirrored forced-rest button.</summary>
internal sealed class RestOfferCue
{
    private Transform? _parent;
    private Renderer? _source, _glow;
    private Material? _material;
    private SpriteRenderer? _sprite;
    private Renderer[] _renderers = System.Array.Empty<Renderer>();
    internal static bool Wanted(bool requiresRest, bool offered, bool selected) =>
        requiresRest && offered && !selected;

    internal void Tick(Transform? parent, Vector2 size, bool wanted)
    {
        if (parent != _parent || (_glow != null && _source == null)) Destroy();
        _parent = parent;
        if (parent == null || !wanted) { Show(false); return; }
        if (_glow == null) Build(parent);
        if (_source == null || _glow == null || _material == null) return;

        // The original moving cap owns visibility, press travel and assembly. The
        // light is its child, with no collider and no replacement of its materials.
        float alpha = 1f;
        if (_source is SpriteRenderer face)
        {
            alpha = face.color.a;
            if (_sprite != null)
            {
                _sprite.sprite = face.sprite;
                _sprite.drawMode = face.drawMode;
                _sprite.size = face.size;
                _sprite.flipX = face.flipX; _sprite.flipY = face.flipY;
                // One/One additive colour must sample the original sprite alpha
                // as well; a transparent atlas corner must never light up as a box.
                if (_material.HasProperty("_AlphaMaskTex") && face.sprite != null
                    && _material.GetTexture("_AlphaMaskTex") != face.sprite.texture)
                    _material.SetTexture("_AlphaMaskTex", face.sprite.texture);
            }
        }
        else
        {
            Material? original = _source.sharedMaterial;
            if (original != null && original.HasProperty("_Color")) alpha = original.color.a;
        }
        alpha = Mathf.Clamp01(alpha);
        // Constant light has identical strength on every board without another
        // unsynchronised animation clock. Scale RGB too: additive ignores alpha.
        _material.color = new Color(.12f * alpha, .087f * alpha, .026f * alpha, alpha);
        Show(_source.enabled && _source.gameObject.activeInHierarchy && alpha > 0f);
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head != null) Net.BoardVisual.OrderWithPanels(_renderers,
            (parent.position - head.transform.position).magnitude);
        _glow.sortingLayerID = _source.sortingLayerID;
        if (_glow.sortingOrder <= _source.sortingOrder) _glow.sortingOrder = _source.sortingOrder + 1;
    }

    private void Build(Transform parent)
    {
        // Both builders name the actual travelling geometry, independently of its
        // round/square style. Never approximate it with the root's rectangular size.
        Transform? cap = parent.Find("Cap/CapMesh") ?? parent.Find("CapMesh")
                         ?? parent.Find("Cap/NativeFace");
        if (cap == null) return;
        Renderer? source = cap.GetComponent<Renderer>();
        MeshFilter? mesh = cap.GetComponent<MeshFilter>();
        SpriteRenderer? face = source as SpriteRenderer;
        if (source == null || (face == null && (mesh == null || mesh.sharedMesh == null))) return;
        Material? material = CardGlow.MakeGlowMaterial(Color.clear);
        if (material == null) return;
        var go = new GameObject("GloomhavenVR.RestRequiredCue") { layer = cap.gameObject.layer };
        go.transform.SetParent(cap, false);
        go.transform.localScale = new Vector3(1.035f, 1.035f, 1f);
        // A polygon offset avoids coplanar shimmer and retains actual depth tests;
        // it cannot illuminate a nearer window, card, board or wall.
        if (material.HasProperty("_OffsetFactor")) material.SetFloat("_OffsetFactor", -1f);
        if (material.HasProperty("_OffsetUnits")) material.SetFloat("_OffsetUnits", -1f);
        if (material.HasProperty("_VertexColor")) material.SetFloat("_VertexColor", 0f);
        if (face != null)
        {
            _sprite = go.AddComponent<SpriteRenderer>();
            _sprite.color = Color.white;
            _glow = _sprite;
            if (material.HasProperty("_UseAlphaMask")) material.SetFloat("_UseAlphaMask", 1f);
            if (material.HasProperty("_AlphaMaskCutoff")) material.SetFloat("_AlphaMaskCutoff", .0001f);
        }
        else
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh!.sharedMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[mesh.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            _glow = renderer;
        }
        _source = source; _material = material;
        _glow.sharedMaterial = material;
        _renderers = new[] { _glow };
    }

    private void Show(bool visible) { if (_glow != null) _glow.enabled = visible; }

    internal void Destroy()
    {
        Show(false);
        if (_glow != null) Object.Destroy(_glow.gameObject);
        if (_material != null) Object.Destroy(_material);
        _source = _glow = null; _sprite = null; _material = null; _parent = null;
        _renderers = System.Array.Empty<Renderer>();
    }
}
