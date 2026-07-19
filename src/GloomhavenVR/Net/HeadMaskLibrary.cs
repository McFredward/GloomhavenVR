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
}
