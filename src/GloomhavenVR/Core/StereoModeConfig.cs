using BepInEx.Configuration;

namespace GloomhavenVR.Core;

/// <summary>
/// The OpenXR stereo render mode, as a plain mod-side choice.
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
/// compromise, and it therefore does not belong in the normal VR settings. It lives in the Debug
/// pane so a future build with a stereo-aware shader bundle can be tested without a rebuild.
/// Making it work would mean: adding the instancing macros to all five mod shaders, fixing
/// <c>HexDecalStable</c>'s <c>_WorldSpaceCameraPos</c> term (per-eye under MultiPass, mono under
/// SPI), enabling XR in the bundle project, reshipping the bundle — and authoring replacement
/// shaders for every game shader in view, which is the part that makes this a project rather
/// than a patch.</para>
///
/// <para>Applied once, before the XR session is created, so a change needs a game restart.
/// <see cref="OpenXRBootstrap"/> logs the mode that is actually active either way.</para>
/// </summary>
internal static class StereoModeConfig
{
    /// <summary>Stereo render modes we are willing to ask the OpenXR plugin for, in cycle order.</summary>
    internal enum Mode
    {
        /// <summary>One full scene traversal per eye. The shipping choice — the only one that renders correctly here.</summary>
        MultiPass,

        /// <summary>One traversal, both eyes as instances. Needs stereo-aware shaders; this game has none.</summary>
        SinglePassInstanced,
    }

    private static ConfigFile? _file;
    internal static ConfigEntry<string>? RenderMode;

    /// <summary>Bind-once against the core module config (canonical <see cref="ModuleConfig"/> pattern).</summary>
    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("stereo");
        RenderMode = _file.Bind("Stereo", "RenderMode", nameof(Mode.MultiPass), new ConfigDescription(
            "OpenXR stereo render mode, applied when the XR session is created (needs a game restart). "
            + "MultiPass renders the scene once per eye and is the ONLY mode that renders correctly in "
            + "this game. SinglePassInstanced would roughly halve the scene traversal cost, but this "
            + "game's shipped shaders contain no stereo variants (verified by disassembling them out of "
            + "resources.assets — see tools/ShaderDisasm/), the mod's own bundle shaders contain none "
            + "either, and the mod's stereo flat screen depends on the two-passes-per-frame contract, so "
            + "the result is a black or duplicated right eye plus mono menus. It is offered for testing a "
            + "future stereo-aware shader bundle, not as a performance setting.",
            new AcceptableValueList<string>(nameof(Mode.MultiPass), nameof(Mode.SinglePassInstanced))));
    }

    /// <summary>The configured mode, defaulting to <see cref="Mode.MultiPass"/> for any unparseable value.</summary>
    internal static Mode Current
    {
        get
        {
            Bind();
            return string.Equals(RenderMode!.Value, nameof(Mode.SinglePassInstanced), System.StringComparison.OrdinalIgnoreCase)
                ? Mode.SinglePassInstanced
                : Mode.MultiPass;
        }
    }

    // ---- settings-panel accessors (Debug pane cycle row) ------------------------------------

    internal static string Label()
    {
        Bind();
        return Current == Mode.SinglePassInstanced ? "Single-Pass" : "MultiPass";
    }

    internal static void Cycle()
    {
        Bind();
        RenderMode!.Value = Current == Mode.MultiPass
            ? nameof(Mode.SinglePassInstanced)
            : nameof(Mode.MultiPass);
        VRLog.Info("Core", $"Stereo render mode set to {RenderMode.Value} — takes effect on the next " +
                           "game start (the XR session negotiates the mode once, at creation). " +
                           "SinglePassInstanced is expected to render the right eye black/wrong on this " +
                           "game: neither the game's shaders nor the mod's bundle shaders carry stereo " +
                           "variants. See Core/StereoModeConfig for the evidence.");
    }
}
