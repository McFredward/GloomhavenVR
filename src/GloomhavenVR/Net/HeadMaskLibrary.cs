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

    /// <summary>Localization key for a mask's display name: this prefix + the mask's own ID.</summary>
    private const string MaskNameKey = "mask_name_";

    /// <summary>
    /// The offered masks' display names, in ID ORDER — the option list for the settings panel's
    /// mask dropdown (user request 2026-08-09: "Ich will die Maske beim Avatar im Optionsmenü auch
    /// mit nem Dropdown auswählen können statt einem Schieberegler wie aktuell", which until now
    /// was a 0/1/2 slider because <c>[Net] MaskId</c> is an int with an AcceptableValueRange and
    /// the row kit reads the control off the stored type).
    ///
    /// <para>INDEX IS THE ID. Entry <c>i</c> of this array IS the value <c>[Net] MaskId</c> takes
    /// for that mask, so the dropdown needs no mapping table and cannot drift out of step with the
    /// wire (<see cref="AvatarState.MaskId"/> carries the same number).</para>
    ///
    /// <para>THE SET COMES FROM THIS CLASS, NOT FROM THE UI. The length is <see cref="MaskCount"/> —
    /// the same constant that bounds <see cref="GetMaskPrefab"/>, the config's AcceptableValueRange
    /// and the wire clamp — so a fourth mask is one edit HERE plus its name string, and the dropdown
    /// grows on its own. Only the human-readable NAMES live in the loc table, and a mask with no
    /// entry there degrades to "Mask 4" instead of vanishing from the list: an id the player can
    /// still select beats a tidy list that silently drops a shipped asset. Deliberately NOT filtered
    /// by <see cref="GetMaskPrefab"/> — that would force-load every prefab just to draw a menu, and
    /// with the bundle absent it would offer an EMPTY dropdown for a setting that still works
    /// (the placeholder head stands in, and the id still rides the wire to peers who do have it).</para>
    ///
    /// <para>Freshly built per call, like <c>MixedReality.KeyColorNames</c> and for the same reason:
    /// it is read once when a settings row is built, and caching it would freeze the strings in
    /// whatever language was current at the time.</para>
    /// </summary>
    public static string[] MaskNames()
    {
        var names = new string[MaskCount];
        for (int i = 0; i < MaskCount; i++)
        {
            string key = MaskNameKey + i;
            string text = Core.Loc.Mod(key);
            // Loc.Mod hands the id back when the table has no entry — that IS the "unnamed mask"
            // signal, and it must never reach the dropdown as the literal string "mask_name_3".
            names[i] = text == key ? Core.Loc.Mod("mask") + " " + (i + 1) : text;
        }
        return names;
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
    /// Instantiate the chosen mask prefab, or stand in a low-poly placeholder head (tinted
    /// <paramref name="placeholderTint"/>) when that mask has not shipped. Does NOT clear existing
    /// children — the caller owns teardown. The caller should re-apply the mod layer afterwards so
    /// the head camera renders the new mesh.
    ///
    /// The visual is built under a "HeadVisual" CHILD of <paramref name="parent"/>, and that child
    /// is returned so the caller can write the user's mask SIZE onto it. This mirrors the hands'
    /// "HandVisual" child exactly, and for the same hard-won reason: the HOLDER already carries the
    /// sender/diorama scale, written every time the rig zoom changes — a size written onto the same
    /// transform would be stomped by that write (which is precisely the bug that made styled remote
    /// hands render ~1.6× too big). Two transforms, two independent scales, no interaction. Other
    /// code that anchors off the head holder (the remote fans aim their cards at it) keeps reading
    /// an unscaled holder, so the mask size cannot move anybody's cards.
    /// </summary>
    public static Transform BuildHead(Transform parent, int maskId, Color placeholderTint, float size)
    {
        Transform visual = new GameObject("HeadVisual").transform;
        visual.SetParent(parent, worldPositionStays: false);
        ApplySize(visual, size);

        GameObject? prefab = GetMaskPrefab(maskId);
        if (prefab != null)
        {
            GameObject inst = Object.Instantiate(prefab, visual, worldPositionStays: false);
            inst.name = "HeadMask";
            return visual;
        }
        BuildPlaceholderHead(visual, placeholderTint);
        return visual;
    }

    /// <summary>Write a uniform mask-size multiplier onto a head visual returned by
    /// <see cref="BuildHead"/>, clamped to the transmittable window so neither a corrupt config nor
    /// a hostile wire byte can turn a peer's head into a zero-scale singularity or a wall. Cheap
    /// enough to call from a per-frame change check (the callers only call it on an actual
    /// change).</summary>
    public static void ApplySize(Transform? headVisual, float size)
    {
        if (headVisual == null)
            return;
        float s = float.IsNaN(size) || float.IsInfinity(size)
            ? 1f
            : Mathf.Clamp(size, NetProtocol.MaskSizeMin, NetProtocol.MaskSizeMax);
        headVisual.localScale = Vector3.one * s;
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
