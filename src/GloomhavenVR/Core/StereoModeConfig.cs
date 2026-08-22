namespace GloomhavenVR.Core;

/// <summary>
/// The OpenXR stereo render mode. A CONSTANT since the 2026-08-22 settings audit — not a choice.
///
/// <para>WHY IT IS A STRING AND NOT <c>OpenXRSettings.RenderMode</c>: this type is read by the
/// settings panel, which is JIT-compiled long before / independently of
/// <see cref="RuntimeDepsLoader"/> loading the mod-shipped XR assemblies. Naming an XR type in a
/// field or signature here would drag those assemblies into the panel's JIT and reintroduce
/// exactly the load-order hazard <see cref="OpenXRBootstrap"/>'s class doc warns about. So the
/// choice is stored as our own enum and mapped to the XR enum inside the bootstrap, at the one
/// point where the XR assemblies are guaranteed to be resolvable.</para>
///
/// <para>WHY MULTIPASS IS THE DEFAULT AND WHY SPI IS NOT A NORMAL SETTING: Single-Pass Instanced
/// would render the scene once instead of twice — the largest structural GPU win available on
/// paper. On THIS game it does not work, and that is not a guess. Three independent blockers,
/// each with evidence in this repo:</para>
/// <list type="number">
/// <item><b>The game's shaders carry no stereo variants.</b> <c>tools/ShaderDisasm/</c> extracts
/// real shaders out of the shipped <c>resources.assets</c>; every extract report shows the
/// shader-wide keyword table declaring STEREO_INSTANCING_ON / UNITY_SINGLE_PASS_STEREO /
/// STEREO_MULTIVIEW_ON while NOT ONE compiled sub-program carries any of them (see
/// <c>tools/ShaderDisasm/evidence/OmniDecal_Shd.extract-report.txt</c>,
/// <c>UI_Default.extract-report.txt</c>, <c>ParticleMasterUnlitAdd.extract-report.txt</c>,
/// <c>SimpleParticleAlphaDFade.extract-report.txt</c>, summarised in
/// <c>tools/ShaderDisasm/FINDINGS.md</c>). Variants cannot be recompiled out of a shipped player
/// build, so under SPI the game's own materials draw into slice 0 only — the right eye is
/// black or a duplicate of the left.</item>
/// <item><b>The mod's own shaders carry none either.</b> All five bundle shaders
/// (<c>HeadUnlit</c>, <c>BoardLit</c>, <c>Overlay</c>, <c>MapUnlit</c>, <c>HexDecalStable</c>)
/// are hand-written vertex/fragment pairs with no <c>UNITY_VERTEX_INPUT_INSTANCE_ID</c> /
/// <c>UNITY_VERTEX_OUTPUT_STEREO</c> macros, and the bundle's Unity project has no XR loader
/// assigned (<c>unity/GloomhavenVR.Assets/ProjectSettings/ProjectSettings.asset</c>,
/// <c>m_StereoRenderingPath: 0</c>), so Unity strips the stereo variants at build time. Hands,
/// board tray, map and menus would disappear from the right eye too.</item>
/// <item><b>The mod's stereo flat screen is built on the two-pass contract, and fails
/// SILENTLY.</b> <c>FlatScreenStereo.4.PerEye.OnPreRenderCamera</c> swaps the quad's texture per
/// eye pass off <c>Camera.stereoActiveEye</c>; under SPI it fires once per frame and its
/// pass-parity fallback then always resolves to "left", so every 2D menu, the campaign map and
/// all video collapse to mono with no error anywhere.</item>
/// </list>
///
/// <para>So this is a control that reliably breaks rendering, not a quality-against-smoothness
/// compromise. Making the other mode work would mean: adding the instancing macros to all five
/// mod shaders, fixing <c>HexDecalStable</c>'s <c>_WorldSpaceCameraPos</c> term (per-eye under
/// MultiPass, mono under SPI), enabling XR in the bundle project, reshipping the bundle — and
/// authoring replacement shaders for every game shader in view, which is the part that makes this
/// a project rather than a patch.</para>
///
/// <para>IT WAS A SETTING UNTIL THE 2026-08-22 AUDIT, and it is now a constant. User, verbatim:
/// <i>"a) Lösche alle Einstellungen die das Spiel breaken könnten wenn die verändert werden. Etwas
/// was das spiel kaputt macht wenn man es umstellt ist nicht optional und sollte daher nicht
/// einstellbar sein."</i> The harm and the value that caused it: <c>[Stereo] RenderMode =
/// SinglePassInstanced</c> is applied when the XR session is created, so it lands at the NEXT
/// START, and it lands as a black or duplicated right eye with every 2D menu, the campaign map and
/// all video collapsed to mono — a broken picture with no error anywhere and no row left to undo
/// it from, because the row lived in a menu you can no longer read. The three blockers above are
/// facts about the shipped shaders, not a trade a player can weigh, so there is nothing here to
/// decide. The evidence stays written down for the day a stereo-aware bundle exists: the one line
/// to change is <see cref="Current"/>.</para>
///
/// <para>Applied once, before the XR session is created. <see cref="OpenXRBootstrap"/> logs the
/// mode that is actually active either way.</para>
/// </summary>
internal static class StereoModeConfig
{
    /// <summary>Stereo render modes the OpenXR plugin can be asked for.</summary>
    internal enum Mode
    {
        /// <summary>One full scene traversal per eye. The shipping choice — the only one that renders correctly here.</summary>
        MultiPass,

        /// <summary>One traversal, both eyes as instances. Needs stereo-aware shaders; this game has none.</summary>
        SinglePassInstanced,
    }

    /// <summary>
    /// The mode the bootstrap asks for. A constant: see the class doc for why this is not a dial
    /// and for what would have to ship before it could become one again.
    /// </summary>
    internal const Mode Current = Mode.MultiPass;
}
