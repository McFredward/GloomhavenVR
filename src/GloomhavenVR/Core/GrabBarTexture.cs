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
            EmbeddedTexture.Get(ResourceName(baseName, string.Empty), linear: false),
            lit ? EmbeddedTexture.Get(ResourceName(baseName, "_n"), linear: true) : null,
            lit ? EmbeddedTexture.Get(ResourceName(baseName, "_mrs"), linear: true) : null);
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

/// <summary>Which rod a control board wears.</summary>
internal static class GrabBarStyles
{
    /// <summary>
    /// THE ONE MAP FROM A BOARD TO ITS ROD, and it lives here because two call sites need it.
    ///
    /// <para>The local tray (<c>Cards.PlayTray.BuildHandle</c>) and the mirrored board
    /// (<c>Net.Remote.RemoteBoardFurniture</c>) each wrote their own copy of this switch when they
    /// were built in parallel, and the second one flagged the duplication itself. Two hand-kept
    /// copies of one rule is how a peer's board ends up wearing a different rod from its owner's —
    /// a 1:1 defect that no checker on this project would catch, because both copies would be
    /// individually correct C#.</para>
    ///
    /// <para><b>NOT A CAST, DELIBERATELY.</b> <c>ControlBoard</c> is Oak = 0, Steel = 1, Bronze = 2
    /// and <see cref="GrabBarStyle"/> is Generic = 0, Oak = 1, Steel = 2, Bronze = 3 — the ordinals
    /// are OFF BY ONE and a cast would silently hand every board the wrong rod, with Bronze falling
    /// off the end into nothing. The enums are numbered differently on purpose:
    /// <see cref="GrabBarStyle.Generic"/> has to be member 0 so a caller that forgets gets the
    /// neutral window rod rather than somebody else's board material.</para>
    ///
    /// <para>The default arm is OAK, not Generic: every <c>ControlBoard</c> value IS a board, so
    /// falling through to the dark-walnut window rod would be the one material guaranteed not to
    /// match. <c>ControlBoards.Clamp</c> already treats out-of-range as Oak, so this agrees with
    /// it.</para>
    /// </summary>
    internal static GrabBarStyle For(Cards.ControlBoard board) => board switch
    {
        Cards.ControlBoard.Steel => GrabBarStyle.Steel,
        Cards.ControlBoard.Bronze => GrabBarStyle.Bronze,
        _ => GrabBarStyle.Oak,
    };
}
