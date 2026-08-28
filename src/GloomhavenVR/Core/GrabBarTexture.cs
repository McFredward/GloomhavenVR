using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE GRAB BAR'S ALBEDO STRIPS — four textures, one per rod, shipped INSIDE THE PLUGIN DLL as
/// <c>EmbeddedResource</c>.
///
/// <para><b>WHY NOT THE ASSET BUNDLE, which is where every other texture in this mod lives.</b>
/// <c>gloomhavenvr.bundle</c> has been byte-identical at 70,204,340 bytes since ModBuild 296, and
/// every install since is plugin-DLL-only. Putting four small strips in it would cost the user a
/// full re-install for a handle. The wordmark
/// (<c>WorldUI.Patches.MainMenuLogoSwap</c>) already made exactly this trade for exactly this
/// reason and has shipped on it for a hundred builds, so this follows that route rather than
/// inventing a second one. <c>LogicalName</c> is pinned in <c>GloomhavenVR.csproj</c> so a folder
/// rename cannot silently change the manifest name asked for here.</para>
///
/// <para><b>THE STRIP LAYOUT IS A CONTRACT WITH TWO CONSUMERS, AND THIS FILE IS ITS ONE SOURCE.</b>
/// <see cref="GrabBarMesh"/> writes <c>u</c> against <see cref="ShaftU0"/> / <see cref="ShaftU1"/>,
/// and the compositing script that BUILDS the PNGs
/// (<c>scripts/grabbar-strips.py</c>) reads <see cref="ShaftU0"/> out of this
/// very file rather than re-typing it — it refuses to run if it cannot parse it. Two hand-kept
/// copies of one boundary is precisely how the caps end up painted in shaft material, and this
/// project has paid for that shape of drift often enough to wire the two together instead.</para>
///
/// <para>Layout, left to right: <c>[cap 0 … ShaftU0][shaft ShaftU0 … ShaftU1][cap ShaftU1 … 1]</c>,
/// with <c>v</c> around the circumference. The right cap band is the left one mirrored, so both
/// ends of a rod carry the same material and the wrap at <c>u=1</c> meets <c>u=0</c> on identical
/// pixels.</para>
/// </summary>
internal static class GrabBarTexture
{
    /// <summary>Where the CAP band ends and the shaft begins, in <c>u</c> — forwarded from
    /// <see cref="GrabBarMesh.ShaftU0"/>, which is the one source. It is declared over there and
    /// not here because <c>GrabBar.cs</c> has to compile against <c>UnityEngine</c> alone (it is
    /// symlinked into the Unity companion project so the preview renders the same mesh code the
    /// game runs), and this file pulls in <c>VRLog</c> and <c>Cards.PlayTray</c>.</summary>
    internal const float ShaftU0 = GrabBarMesh.ShaftU0;

    /// <summary>Where the shaft ends and the far CAP band begins, in <c>u</c>.</summary>
    internal const float ShaftU1 = GrabBarMesh.ShaftU1;

    private const string Scope = "Core";

    private static readonly Dictionary<string, Texture2D?> _cache = new();

    /// <summary>
    /// The three maps one rod draws with. <see cref="Normal"/> and <see cref="Mrs"/> are null for
    /// the window rod ON PURPOSE — it draws through the UNLIT overlay shader, which reads neither,
    /// so shipping them would be two textures nothing samples.
    /// </summary>
    internal readonly struct Maps
    {
        internal Maps(Texture2D? albedo, Texture2D? normal, Texture2D? mrs)
        {
            Albedo = albedo;
            Normal = normal;
            Mrs = mrs;
        }

        /// <summary>Colour. Null means the resource was missing — the caller falls back to a flat
        /// tint rather than drawing nothing.</summary>
        internal Texture2D? Albedo { get; }

        /// <summary>Tangent-space normal, LINEAR. Null for the window rod.</summary>
        internal Texture2D? Normal { get; }

        /// <summary>R = metallic, G = roughness, LINEAR — the packing
        /// <c>GloomhavenVR/BoardLit</c> declares. Null for the window rod.</summary>
        internal Texture2D? Mrs { get; }
    }

    /// <summary>Manifest resource name per style. These must match the <c>LogicalName</c> attributes
    /// in <c>GloomhavenVR.csproj</c> exactly.</summary>
    private static string BaseName(GrabBarStyle style) => style switch
    {
        GrabBarStyle.Oak => "grabbar_oak",
        GrabBarStyle.Steel => "grabbar_steel",
        GrabBarStyle.Bronze => "grabbar_bronze",
        _ => "grabbar_generic",
    };

    private static string ResourceName(string baseName, string suffix) =>
        "GloomhavenVR.Assets." + baseName + suffix + ".png";

    /// <summary>
    /// The albedo strip for one rod, decoded once and cached for the process. Null means the
    /// resource was missing or undecodable — every caller falls back to a flat tint, so a broken
    /// build loses the texture and keeps the handle rather than losing the handle.
    ///
    /// <para>A NULL IS CACHED TOO, on purpose. Without that, a missing resource would re-attempt a
    /// <c>LoadImage</c> on every bar built — and the window bars are rebuilt whenever a window is —
    /// which turns one silent failure into a per-window allocation and a log line nobody can read
    /// past. This project has shipped a probe that kept blitting for 44,200 ticks after it had its
    /// answer; a failed lookup is an answer.</para>
    /// </summary>
    internal static Maps Get(GrabBarStyle style)
    {
        string baseName = BaseName(style);
        // The window rod is unlit; a normal and an MRS map for it would be two textures nothing
        // ever samples, so the pipeline does not build them and this does not ask for them.
        bool lit = style != GrabBarStyle.Generic;
        return new Maps(
            Load(ResourceName(baseName, string.Empty), linear: false),
            lit ? Load(ResourceName(baseName, "_n"), linear: true) : null,
            lit ? Load(ResourceName(baseName, "_mrs"), linear: true) : null);
    }

    /// <summary>
    /// Decode one embedded PNG, once per process.
    ///
    /// <para>A NULL IS CACHED TOO, on purpose. Without that, a missing resource would re-attempt a
    /// <c>LoadImage</c> on every bar built — and the window bars are rebuilt whenever a window is —
    /// which turns one silent failure into a per-window allocation and a log line nobody can read
    /// past. This project has shipped a probe that kept blitting for 44,200 ticks after it had its
    /// answer; a failed lookup is an answer.</para>
    ///
    /// <para><paramref name="linear"/> is not cosmetic: a normal or an MRS map decoded as sRGB
    /// feeds the shader gamma-curved numbers where it expects raw ones, which shows up as a normal
    /// that leans the wrong way and a roughness that is wrong everywhere except 0 and 1.</para>
    /// </summary>
    private static Texture2D? Load(string resource, bool linear)
    {
        if (_cache.TryGetValue(resource, out Texture2D? cached))
            return cached;

        Texture2D? tex = null;
        try
        {
            byte[]? bytes = ReadResource(resource);
            if (bytes != null)
            {
                // mipChain: true — these rods are seen at every distance from a hand's width to
                // across the room, and an unmipped 1024-wide strip on a 24 mm rod is the classic
                // per-eye shimmer this project has already chased once in the wall fade.
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: linear);
                if (tex.LoadImage(bytes))
                {
                    // The strip wraps around the rod in v and butts cap-to-cap in u; Repeat is
                    // correct in both and Clamp would show a seam line at v=0.
                    tex.wrapMode = TextureWrapMode.Repeat;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.anisoLevel = 4;
                    tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
                    VRLog.Info(Scope,
                        $"Grab-bar map '{resource}' decoded: {tex.width}x{tex.height} " +
                        $"({bytes.Length} bytes, linear={linear}).");
                }
                else
                {
                    UnityEngine.Object.Destroy(tex);
                    tex = null;
                    VRLog.Warn(Scope, $"Grab-bar map '{resource}' failed to decode.");
                }
            }
            else
            {
                VRLog.Warn(Scope, $"Grab-bar map '{resource}' is not embedded in the plugin.");
            }
        }
        catch (Exception e)
        {
            VRLog.Warn(Scope, $"Grab-bar map '{resource}' load threw ({e.GetType().Name}).");
            tex = null;
        }

        _cache[resource] = tex;
        return tex;
    }

    private static byte[]? ReadResource(string name)
    {
        Assembly asm = typeof(GrabBarTexture).Assembly;
        Stream? stream = asm.GetManifestResourceStream(name);
        if (stream == null)
        {
            // Suffix scan, same degradation MainMenuLogoSwap uses: a renamed Assets folder becomes
            // a log line rather than a silent no-op.
            string tail = name.Substring(name.LastIndexOf('.', name.Length - 5) + 1);
            foreach (string candidate in asm.GetManifestResourceNames())
            {
                if (!candidate.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    continue;
                VRLog.Warn(Scope,
                    $"Embedded resource '{name}' not found; using '{candidate}' instead — the " +
                    "LogicalName in GloomhavenVR.csproj and ResourceName here have drifted apart.");
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

/// <summary>
/// Which of the four rods to draw. Deliberately NOT <c>Cards.ControlBoard</c>: three of these map
/// onto a board style, but <see cref="Generic"/> is the windows' rod and has no board behind it —
/// and member 0 is the GENERIC one so a call site that forgets to say gets the quiet neutral rod
/// rather than somebody else's board material. Same shape as the other permission-style enums in
/// this codebase (<c>CardDustFx.Permission</c>, <c>NativeButtonSkin.LabelOwner</c>): member 0 is
/// the answer that cannot be wrong in the wrong place.
/// </summary>
internal enum GrabBarStyle
{
    /// <summary>The windows' rod: dark oiled walnut with small aged-brass knobs, chosen to sit on
    /// parchment without competing with the text on it.</summary>
    Generic = 0,

    /// <summary>Honey-brown oak with visible grain and dark aged-brass caps — the Oak board.</summary>
    Oak = 1,

    /// <summary>Cool mottled blue-grey forged steel, no gold anywhere — the Steel board.</summary>
    Steel = 2,

    /// <summary>Gold-brass crowns over verdigris hollows, bright gold caps — the Bronze board.</summary>
    Bronze = 3,
}
