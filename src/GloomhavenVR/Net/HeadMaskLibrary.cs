using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Loads the three shippable head "mask" prefabs from the ALREADY-LOADED mod bundle. The
/// local player picks one of these in VR settings (<c>[Net] MaskId</c>) and the choice rides
/// the wire (<see cref="AvatarState.MaskId"/>) so other VR players render the right mask.
///
/// The asset contract (see PLAN.md §10, mirrored per mask): exactly three masks live at
/// <c>Assets/Bundle/Head/Mask_0.prefab</c> … <c>Mask_2.prefab</c> (index 0..2), authored at
/// real-world metres with +Z forward / +Y up, unlit, pivot at the eye midpoint, no colliders.
/// A parallel worker produces the assets; until they ship, <see cref="GetMaskPrefab"/> returns
/// null and the caller (<see cref="RemoteAvatar"/> / <see cref="WorldUI.AvatarMirror"/>) stands
/// in a placeholder head — the feature is testable now.
///
/// Never re-opens the bundle file (HandVisuals owns the single handle): it scans the loaded
/// bundles, exactly like <c>RemoteAvatar.TryLoadHeadPrefab</c> did for the old single mask.
/// Results are cached (loaded prefab, or the fact that it is absent) so repeated look-ups on a
/// mask change are cheap.
/// </summary>
internal static class HeadMaskLibrary
{
    /// <summary>Number of selectable masks (indices 0..MaskCount-1).</summary>
    public const int MaskCount = 3;

    private static readonly GameObject?[] _cache = new GameObject?[MaskCount];
    private static readonly bool[] _probed = new bool[MaskCount];

    /// <summary>
    /// Clamp <paramref name="id"/> to [0, MaskCount-1] and load the matching mask prefab from the
    /// already-loaded mod bundle, cached. Returns null when the prefab is absent (caller uses the
    /// placeholder head). Never throws.
    /// </summary>
    public static GameObject? GetMaskPrefab(int id)
    {
        id = Mathf.Clamp(id, 0, MaskCount - 1);
        if (_probed[id])
            return _cache[id];

        _probed[id] = true;
        _cache[id] = TryLoad(id);
        return _cache[id];
    }

    /// <summary>Load <c>Assets/Bundle/Head/Mask_&lt;id&gt;.prefab</c> from a loaded bundle if present
    /// (never re-open the file — HandVisuals owns the single handle).</summary>
    private static GameObject? TryLoad(int id)
    {
        string path = $"Assets/Bundle/Head/Mask_{id}.prefab";
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (bundle == null)
                continue;
            try
            {
                if (bundle.Contains(path))
                    return bundle.LoadAsset<GameObject>(path);
            }
            catch { /* not our bundle / API quirk — ignore */ }
        }
        return null;
    }

    /// <summary>Drop the cache so a freshly-shipped bundle is re-probed (module shutdown / hot reload).</summary>
    public static void Reset()
    {
        for (int i = 0; i < MaskCount; i++)
        {
            _cache[i] = null;
            _probed[i] = false;
        }
    }

    // ---- head building (shared by RemoteAvatar + the local AvatarMirror) -----------------

    /// <summary>
    /// Instantiate the chosen mask prefab under <paramref name="parent"/>, or stand in a low-poly
    /// placeholder head (tinted <paramref name="placeholderTint"/>) when that mask has not shipped.
    /// Does NOT clear existing children — the caller owns teardown. The caller should re-apply the
    /// mod layer afterwards so the head camera renders the new mesh.
    /// </summary>
    public static void BuildHead(Transform parent, int maskId, Color placeholderTint)
    {
        GameObject? prefab = GetMaskPrefab(maskId);
        if (prefab != null)
        {
            GameObject inst = Object.Instantiate(prefab, parent, worldPositionStays: false);
            inst.name = "HeadMask";
            return;
        }
        BuildPlaceholderHead(parent, placeholderTint);
    }

    /// <summary>Low-poly placeholder head: a cranium sphere + a flatter "visor" plate facing +Z
    /// (the mask's forward / where the eyes look), unlit so it reads in the lightless void.</summary>
    private static void BuildPlaceholderHead(Transform parent, Color tint)
    {
        Material mat = UnlitMaterial(tint);
        Material visorMat = UnlitMaterial(tint * new Color(0.6f, 0.65f, 0.75f, 1f));

        GameObject cranium = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(cranium.GetComponent<Collider>());
        cranium.name = "Cranium";
        cranium.transform.SetParent(parent, worldPositionStays: false);
        cranium.transform.localPosition = Vector3.zero;
        cranium.transform.localScale = new Vector3(0.17f, 0.20f, 0.21f); // ~human head, +Z long
        cranium.GetComponent<Renderer>().sharedMaterial = mat;

        // Visor/mask plate on the face (+Z forward) — a landmark so orientation reads clearly.
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(visor.GetComponent<Collider>());
        visor.name = "Visor";
        visor.transform.SetParent(parent, worldPositionStays: false);
        visor.transform.localPosition = new Vector3(0f, 0.01f, 0.10f);
        visor.transform.localScale = new Vector3(0.14f, 0.055f, 0.03f);
        visor.GetComponent<Renderer>().sharedMaterial = visorMat;
    }

    private static Material UnlitMaterial(Color color)
    {
        // The void/menu have no lights, so use an unlit shader (same rule as HandVisuals).
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                        ?? Shader.Find("Hidden/InternalErrorShader");
        var m = new Material(shader);
        m.color = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
        return m;
    }
}
