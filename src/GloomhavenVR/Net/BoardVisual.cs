using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Tiny shared factory for the read-only remote-board visuals (<see cref="RemoteControlBoard"/>
/// + <see cref="OwnerTag"/>). Everything is UNLIT: the remote board and its ownership tag are
/// mod-owned cosmetics rendered by the mod head camera, and Gloomhaven's scenario/void lighting
/// is not something we control — the same rule the hands and head masks follow
/// (<see cref="HeadMaskLibrary"/>). Never touches game state.
/// </summary>
internal static class BoardVisual
{
    /// <summary>An unlit material (optionally textured), so mod visuals read the same regardless
    /// of the surrounding scene lights. Mirrors <c>HeadMaskLibrary.UnlitMaterial</c>.</summary>
    internal static Material Unlit(Color color, Texture? texture = null)
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                        ?? Shader.Find("Hidden/InternalErrorShader");
        var m = new Material(shader)
        {
            color = new Color(color.r, color.g, color.b, color.a),
        };
        if (texture != null)
            m.mainTexture = texture;
        return m;
    }

    /// <summary>
    /// A collider-free quad on <paramref name="parent"/>, sized <paramref name="size"/> local
    /// metres in its XY plane. By the module convention (+Z points AWAY from the viewer) the quad's
    /// front reads from the −Z side, so a parent whose −Z faces the viewer shows the quad face-on.
    /// </summary>
    internal static MeshRenderer Quad(Transform parent, string name, Vector2 size, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }
}
