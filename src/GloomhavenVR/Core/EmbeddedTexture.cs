using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// DECODE A PNG THAT SHIPS INSIDE THE PLUGIN DLL, once per process.
///
/// <para><b>WHY ANYTHING SHIPS THIS WAY.</b> <c>gloomhavenvr.bundle</c> has been byte-identical at
/// 70,204,340 bytes since ModBuild 296, and every install since is plugin-DLL-only. A texture that
/// goes in the bundle costs the user a full re-install; one embedded in the DLL does not. The
/// wordmark (<c>WorldUI.Patches.MainMenuLogoSwap</c>) made that trade first and has shipped on it
/// for a hundred builds. This is the same route, factored out — the grab-bar strips wanted it, and
/// then the shared-window badge wanted it too, and a second hand-written copy of a resource loader
/// is how two of them end up with different failure behaviour.</para>
///
/// <para><b>A NULL IS CACHED TOO, on purpose.</b> Without that, a missing resource re-attempts a
/// <c>LoadImage</c> at every call — and these callers build per window and per board — which turns
/// one silent failure into a per-object allocation and a log line nobody can read past. This
/// project has shipped a probe that kept blitting for 44,200 ticks after it had its answer; a
/// failed lookup is an answer.</para>
///
/// <para>The <c>linear</c> flag is not cosmetic. A normal or an MRS map decoded as sRGB hands
/// the shader gamma-curved numbers where it wants raw ones, which shows up as a normal that leans
/// the wrong way and a roughness wrong everywhere except 0 and 1. Colour maps want false.</para>
/// </summary>
internal static class EmbeddedTexture
{
    private const string Scope = "Core";

    private static readonly Dictionary<string, Texture2D?> _cache = new();

    /// <summary>
    /// The decoded texture, or null when the resource is missing or undecodable. EVERY caller must
    /// have a picture for null — this returns one rather than throwing, because a build that lost a
    /// PNG should lose a texture, not a handle.
    /// </summary>
    /// <param name="resource">Manifest name, e.g. <c>GloomhavenVR.Assets.net_shared.png</c>. These
    /// must match the <c>LogicalName</c> attributes in <c>GloomhavenVR.csproj</c> exactly.</param>
    /// <param name="linear">True for data maps (normal, MRS), false for colour.</param>
    /// <param name="wrap">Repeat for anything tiled around or along a mesh; Clamp for a badge,
    /// where Repeat would let bilinear filtering fetch the opposite edge at the border.</param>
    internal static Texture2D? Get(string resource, bool linear,
                                   TextureWrapMode wrap = TextureWrapMode.Repeat)
    {
        if (_cache.TryGetValue(resource, out Texture2D? cached))
            return cached;

        Texture2D? tex = null;
        try
        {
            byte[]? bytes = Read(resource);
            if (bytes != null)
            {
                // mipChain: true — these are seen at every distance from a hand's width to across
                // the room, and an unmipped map on a small object is the classic per-eye shimmer
                // this project has already chased once in the wall fade.
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: linear);
                if (tex.LoadImage(bytes))
                {
                    tex.wrapMode = wrap;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.anisoLevel = 4;
                    tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
                    VRLog.Info(Scope,
                        $"Embedded texture '{resource}' decoded: {tex.width}x{tex.height} " +
                        $"({bytes.Length} bytes, linear={linear}, wrap={wrap}).");
                }
                else
                {
                    UnityEngine.Object.Destroy(tex);
                    tex = null;
                    VRLog.Warn(Scope, $"Embedded texture '{resource}' failed to decode.");
                }
            }
            else
            {
                VRLog.Warn(Scope, $"Embedded texture '{resource}' is not in the plugin.");
            }
        }
        catch (Exception e)
        {
            VRLog.Warn(Scope, $"Embedded texture '{resource}' load threw ({e.GetType().Name}).");
            tex = null;
        }

        _cache[resource] = tex;
        return tex;
    }

    private static byte[]? Read(string name)
    {
        Assembly asm = typeof(EmbeddedTexture).Assembly;
        Stream? stream = asm.GetManifestResourceStream(name);
        if (stream == null)
        {
            // Suffix scan, the same degradation MainMenuLogoSwap uses: a renamed Assets folder
            // becomes a log line rather than a silent no-op.
            int lastDot = name.LastIndexOf('.', name.Length - 5);
            string tail = lastDot >= 0 ? name.Substring(lastDot + 1) : name;
            foreach (string candidate in asm.GetManifestResourceNames())
            {
                if (!candidate.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    continue;
                VRLog.Warn(Scope,
                    $"Embedded resource '{name}' not found; using '{candidate}' — the LogicalName " +
                    "in GloomhavenVR.csproj and the name asked for here have drifted apart.");
                stream = asm.GetManifestResourceStream(candidate);
                break;
            }
        }
        if (stream == null)
            return null;

        using (stream)
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
