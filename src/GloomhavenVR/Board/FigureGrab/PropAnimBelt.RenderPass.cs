using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// ROUND SEVENTEEN — THE FIRST PROBE IN THIS FILE THAT SAMPLES <b>INSIDE A CAMERA'S RENDER
/// PASS</b>, and it exists to name a whole class rather than one more candidate.
///
/// <para><b>THE ASSUMPTION SIXTEEN ROUNDS SHARED WITHOUT WRITING IT DOWN.</b> Every instrument this
/// file has ever shipped samples in <c>Update</c> or <c>LateUpdate</c>: the standing watch (1796
/// frames, <c>CHANGES 0</c>), the home-twin comparison (<c>0 slot(s) EVER DIFFERED</c>), the
/// material-attachment clause (<c>held prop worst 0 of 2</c>), the lighting arms, the position
/// sweep. A value that exists ONLY inside the render loop is invisible to every one of them by
/// construction and reads as <c>CHANGES 0</c> forever while the picture changes:
/// <list type="bullet">
/// <item>a renderer switched on by one camera's pre-render and off again in its post-render;</item>
/// <item><c>Renderer.forceRenderingOff</c>, which is a SEPARATE flag from <c>enabled</c> and which
/// no probe in this file has ever read (this mod itself writes it — <c>Net/PeerBoardFade.cs</c>);</item>
/// <item>a shader global re-published by a command buffer part-way through a frame — the occlusion
/// generator's buffer does exactly this at <c>CameraEvent.BeforeGBuffer</c> on the ScenarioCamera,
/// so between that camera's pass and the head camera's pass the global set is NOT the set an
/// <c>Update</c> read returns;</item>
/// <item>a material or layer swapped for one camera's pass and swapped back.</item>
/// </list>
/// That is the exact shape of the defect: a per-renderer, board-wide-synchronous, NEUTRAL bleach
/// that no object probe can see.</para>
///
/// <para><b>WHY A COMPARISON AND NOT A SERIES.</b> A per-pass log of the state would be another nine
/// lines a frame that a reader has to diff by eye. The answer that matters is <i>"the render-loop
/// reading and the Update-time reading of the SAME FRAME disagree"</i>, so this arm takes both, in
/// the same frame, and prints the DISAGREEMENT COUNT PER FIELD. One number above zero names the
/// whole missing class and says which field carries it. A row of zeroes over a window that
/// contained the flash EXCLUDES the class — and that is worth more than a ninth state probe,
/// because it is the only reading that can.</para>
///
/// <para><b>THE THREE READINGS, PRE-REGISTERED, so a null result means something.</b>
/// <list type="number">
/// <item><b>INERT</b> — <c>Camera.onPreRender</c> fired 0 times, or fired but never in a frame this
/// class had already sampled from <c>Update</c> (<c>PAIRS 0</c>), or nothing was tracked. Nothing is
/// measured either way and "the white was unchanged" excludes nothing.</item>
/// <item><b>AGREE</b> — <c>PAIRS</c> above 0 and every per-field and per-global disagreement count
/// 0. The tracked renderers and the enumerated globals hold the same values inside the render loop
/// that they hold in <c>Update</c>, so the in-render class is EXCLUDED for those fields and the next
/// round must look at what is listed under WHAT IS STILL BEYOND THIS INSTRUMENT.</item>
/// <item><b>DISAGREE</b> — a count above 0. The line names the field, the renderer, the camera and
/// both values. That is the cause, or one step from it.</item>
/// </list></para>
///
/// <para><b>THE MACHINERY IS PROVEN.</b> <c>PropOcclusionGate</c> (ModBuild 467, retired in the same
/// build as this class ships) demonstrated 120 clean <c>onPreRender</c>/<c>onPostRender</c> pairs on
/// <c>GloomhavenVR.HeadCamera</c> with a verified read-back, so hooking that pass works on this rig.
/// This class reuses the hook and writes NOTHING: the gate held a global down, this one only reads.</para>
///
/// <para><b>THE MEASURED SIGNATURE ANY SURVIVING HYPOTHESIS MUST FIT</b> (ModBuild 466 photometry of
/// <c>.planning/debug/falle_aufblitzen.mp4</c> at 10 Hz in linear light): near-white pixels ramp
/// 274 → 85,495 over t=2.8..4.2 s and fall to 0 at t=4.3 — a saturating ~1.0-1.4 s ramp IN and a
/// complete clear inside one 0.1 s step. The added light is NEUTRAL (delta normalised to red
/// 1.000/0.995/1.010), which is what excludes this mod's own overlays (amber 1.000/0.342/0.056, and
/// cool blue). A per-frame toggle would FLICKER rather than ramp, so a boolean field that disagrees
/// here still owes an account of the ramp — and "the ramp is unexplained" is an honest thing to
/// write down and better than a fitted story.</para>
///
/// <para><b>AND ModBuild 470 RE-MEASURED THAT SIGNATURE AT 30 fps AND SPLIT IT IN TWO. A NEAR-WHITE
/// PIXEL COUNT IS AREA TIMES BRIGHTNESS</b>, and on this clip the two terms run one after the
/// other, not together. Over t=2.83..3.30 (0.47 s) the prop's SILHOUETTE (luma ≥ 100) grows
/// 23,669 → 111,482 px — 4.7× in projected area — while the luminance inside it moves only
/// 145 → 197. Over t=3.30..4.27 (0.97 s) the area is FLAT at 110-113k and only then does the
/// brightness ramp, 197 → 238 mean and 200 → 251 median, saturating. So "a saturating ~1.0-1.4 s
/// ramp IN" is half a scale change and half a brightening, and the ModBuild 466 exclusion built on
/// "THE PROP'S CENTROID MOVES UNDER 30 PX" is true and blind to it: a centroid is invariant under
/// scale. THIS IS THE RECORDED TRAP ONE LEVEL UP — a fixed window measures presence, and the 466
/// curve is brightness times how much of the crop the prop fills. Any instrument that reports a
/// near-white FRACTION over a fixed patch inherits the same confound and should print the
/// silhouette AREA beside it.</para>
///
/// <para><b>THE EVENT HAS A PERIOD, AND IT IS 10.000 s.</b> The clip holds a SECOND complete
/// episode nobody had looked for. Episode 1 terminates between frames 128 and 129 (100,645
/// near-white px → 1,384 in ONE frame); episode 2 ramps t=12.90..14.27 and terminates between
/// frames 428 and 429. 429 − 129 = 300 frames = exactly 10.000 s at 30 fps, and both lit spans are
/// 1.40-1.43 s. Episode 2 never reaches episode 1's plateau (58,139 vs 100,759 px), so the plateau
/// is the thing filling its extent, not a feature of the signal. A 10 s schedule with a ~1.4 s lit
/// span and a one-frame termination is the first hard number a surviving hypothesis has to fit —
/// and it is not this mod's <c>OverlayPulse</c>, whose period is 1.43 s and which never stops.</para>
///
/// <para><b>THE COLOUR EXCLUSION WAS STATED ON A SATURATED POPULATION AND SURVIVES ANYWAY.</b> At
/// peak, 33.4 % of the stone-ring pixels carry a channel at 255 and 77.5 % sit within 5/255 of the
/// ceiling, so 1.000/0.995/1.010 was read where a clipped pixel cannot testify to the colour that
/// clipped it. That caveat belonged on the sentence. It does not change the verdict: on the
/// strictly UNCLIPPED subpopulation (every channel below 245 at both times) the added light reads
/// 1.000/0.85-0.90/0.78-0.86 in linear light across the whole ramp, and simulating a real additive
/// amber pass over the clip's own baseline pixels shows clipping can only neutralise amber by
/// driving the patch to 255/255/255 everywhere, which the observed peak (mean 232/231/231, 16 %
/// fully saturated) is not.</para>
///
/// <para><b>TWO LEADS THIS CLASS DELIBERATELY DOES NOT CHASE, because the evidence already closed
/// them and a probe aimed at a closed lead answers nothing.</b>
/// <list type="number">
/// <item><b>The trap's <c>OcclusionVolume</c> MeshRenderer being drawn into the VIEW.</b> It cannot
/// be. The 467 roster reads <c>materials &lt;none&gt;</c> on it, and a renderer with zero materials
/// submits no draw to any camera whatever the culling mask says; the only thing that ever draws it
/// is <c>TilesOcclusionGenerator.UpdateCommandBuffers</c>, via
/// <c>CommandBuffer.DrawRenderer(r, m_OcclusionObjectMaterial)</c> AFTER
/// <c>SetRenderTarget(_TempObjectsRT)</c> — into the generator's own temporary target, never into
/// the view. The head camera's <c>0xFFFFFFFF</c> mask is real and is not this. (The material count
/// is nevertheless one of the eight fields compared below, so an assignment inside a render pass
/// would be caught rather than assumed away.)</item>
/// <item><b>The 'Outline' pass running on the head camera.</b> It does not. The 467
/// <c>EYE CENSUS EFFECT</c> summary reads <c>1 of 9 camera(s) carry a CommandBuffer</c> and the one
/// that does is <c>ScenarioCamera</c> ('Tile Occlusion Map Generation' at BeforeGBuffer, 'Outline'
/// at BeforeImageEffects); its <c>6 MonoBehaviour(s) … implement OnRenderImage</c> are the 4 on
/// ScenarioCamera plus the 2 on 'UI Camera'. The head camera carries neither. That was an assertion
/// in <c>NetProtocol.cs</c> and it is now a reading. This class still counts
/// <c>Camera.commandBufferCount</c> and the OnRenderImage census per camera it sees, so the claim is
/// re-measured every session instead of quoted.</item>
/// </list>
/// The mod's own <c>Can't remove Outlinable (Script) because OutlineWrapper (Script) depends on it</c>
/// — three lines, at the three grab frames — is <c>FigureOverlay.BuildFrozenGhost</c>'s component
/// sweep: it sets <c>mb.enabled = false</c> and THEN <c>Object.Destroy(mb)</c>, and Unity refuses the
/// destroy while the <c>[RequireComponent]</c> holder is still there (both destroys are deferred to
/// end of frame, so the dependency is still standing when the call is made). The disable landed, so
/// the surviving component draws nothing; and it is on the GHOST at the prop's home cell, not on the
/// held prop at all. Cosmetic, and not this defect.</para>
///
/// <para><b>WHAT THIS CLASS WRITES: NOTHING.</b> It reads renderer flags, layer, material count,
/// shader name, render queue and a fixed table of shader globals. It never assigns, never enables,
/// never instantiates a material. <c>scripts/check-instrument-writes.py</c> is the gate on that
/// promise. Multiplayer: no game state, nothing sent, nothing read from a peer; a flat player and a
/// peer see exactly what they saw before.</para>
/// </summary>
internal static partial class PropAnimBelt
{
    // ---- what is tracked ------------------------------------------------------------------------

    /// <summary>Renderers followed per side. The trap's roster is 3; eight leaves room for a chest
    /// without ever walking a subtree inside a render callback.</summary>
    private const int RpRendCap = 8;

    /// <summary>Distinct cameras NAMED. Nine rendered in the 467 session; twelve is head-room, and
    /// the overflow count is printed either way — a truncated list is not absence.</summary>
    private const int RpCamCap = 12;

    /// <summary>The fields compared. This order fixes the indices used everywhere below.</summary>
    private static readonly string[] RpFieldNames =
    {
        "enabled", "forceRenderingOff", "activeInHierarchy", "isVisible",
        "gameObject.layer", "sharedMaterials.Length", "sharedMaterial.shader.name",
        "sharedMaterial.renderQueue",
    };

    private const int RpFieldCount = 8;

    // ---- the game's shader globals, ENUMERATED rather than guessed ------------------------------
    //
    // Every `Shader.SetGlobal*` / `CommandBuffer.SetGlobal*` name that appears anywhere in
    // `decompiled/`, plus the two Unity built-ins the fog and Beautify paths OVERWRITE
    // (`_CameraDepthTexture`, `_GrabTexture`) — an overwritten built-in is the one global whose
    // wrong value is invisible to a reader who only looks for the game's own names. The set is small
    // and fully closed, which is why it is sampled entire instead of one candidate at a time.
    //
    // Concentrations, for the reader who wants the call sites: NM_Wind (9), VolumetricFog (7),
    // RFX4_DistortionAndBloom (5), ActivateWallFadeInGame + Main + DebugMenu +
    // ToggleWallTransparencyGlobal (`ToggleWallFade`, 8 sites, one int), DynamicFogManager (3),
    // EPOOutline OutlineEffect/TargetsHolder (the stencil ints, the cutout block, `_EPOColor`),
    // TilesOcclusionGenerator (the three occlusion names, all published from INSIDE a command
    // buffer, which is precisely the shape an Update-time read cannot see move), and
    // BFX_BloodDecalLayers (1).

    private enum RpKind { Float, Int, Vector, Color, Tex }

    private static readonly string[] RpGlobalNames =
    {
        // TilesOcclusionGenerator — published from a CommandBuffer at BeforeGBuffer.
        "_EnableOcclusionMap", "offsets", "_TilesOcclusionMap", "_ObjectOcclusion",
        // The wall-fade master switch: 8 write sites across 4 classes.
        "ToggleWallFade",
        // DynamicFogAndMist.
        "_FogData", "_FogData2", "_FogColor",
        // VolumetricFogAndMist.
        "_VF_ShadowBias", "_VFM_CutOff", "_VolumetricFogSunWorldPos",
        "_VolumetricFogSunShadowsData", "_VolumetricFog_OpaqueFrame",
        "_VolumetricFogDepthTexture", "_VolumetricFogSunDepthTexture",
        "_VolumetricFogShadowMapCopy",
        // Beautify.
        "_BeautifyDepthBias",
        // RFX4 distortion/bloom, and the two Unity built-ins it and the fog path overwrite.
        "_GrabTexture", "_GrabTextureMobile", "_GrabTextureScale", "_GrabTextureMobileScale",
        "_CameraDepthTexture",
        // BFX blood decals.
        "_LayerDecalDepthTexture",
        // EPOOutline.
        "_EPOColor", "_ColorMask", "_EffectSize", "_CutoutThreshold", "_TextureIndex",
        "_CutoutMask", "_CutoutTextureST", "_InfoBuffer", "PrimaryInfoBuffer", "HelperInfoBuffer",
        "_SrcBlend", "_DstBlend", "_ZTest", "_Cull", "_Operation", "_Comparison",
        "_OutlineRef", "_FillRef", "_Ref", "_ID",
        // NM_Wind.
        "WIND_SETTINGS_WorldDirectionAndSpeed", "WIND_SETTINGS_Turbulence",
        "WIND_SETTINGS_GustSpeed", "WIND_SETTINGS_GustScale", "WIND_SETTINGS_GustWorldScale",
        "WIND_SETTINGS_FlexNoiseScale", "WIND_SETTINGS_ShiverNoiseScale",
        "WIND_SETTINGS_TexNoise", "WIND_SETTINGS_TexGust",
    };

    private static readonly RpKind[] RpGlobalKinds =
    {
        RpKind.Float, RpKind.Vector, RpKind.Tex, RpKind.Tex,
        RpKind.Int,
        RpKind.Vector, RpKind.Float, RpKind.Color,
        RpKind.Float, RpKind.Float, RpKind.Vector,
        RpKind.Vector, RpKind.Tex,
        RpKind.Tex, RpKind.Tex,
        RpKind.Tex,
        RpKind.Float,
        RpKind.Tex, RpKind.Tex, RpKind.Float, RpKind.Float,
        RpKind.Tex,
        RpKind.Tex,
        RpKind.Color, RpKind.Int, RpKind.Float, RpKind.Float, RpKind.Float,
        RpKind.Vector, RpKind.Vector, RpKind.Tex, RpKind.Tex, RpKind.Tex,
        RpKind.Int, RpKind.Int, RpKind.Int, RpKind.Int, RpKind.Int, RpKind.Int,
        RpKind.Int, RpKind.Int, RpKind.Int, RpKind.Float,
        RpKind.Vector, RpKind.Float,
        RpKind.Float, RpKind.Float, RpKind.Float,
        RpKind.Float, RpKind.Float,
        RpKind.Tex, RpKind.Tex,
    };

    /// <summary>The most globals the fixed buffers below can hold. Sized past
    /// <see cref="RpGlobalNames"/> so adding a name is one edit and not three.</summary>
    private const int RpGlobalCap = 64;

    private static int[] _rpGlobalIds = System.Array.Empty<int>();

    // ---- state ----------------------------------------------------------------------------------

    private static bool _rpArmed;
    private static bool _rpHooked;

    private static readonly Renderer?[] RpRend = new Renderer?[RpRendCap * 2];
    private static readonly string[] RpRendName = new string[RpRendCap * 2];
    private static readonly bool[] RpRendHeld = new bool[RpRendCap * 2];
    private static int _rpRendCount;
    private static int _rpHeldTracked, _rpTwinTracked;

    // The three snapshots. `Upd` is taken on the verdict tick (Update order); `Ren` is taken inside a
    // camera's onPreRender; `Fst` is the FIRST in-render snapshot of the current frame, which is what
    // makes the BETWEEN-CAMERA reading possible without a second window.
    private static readonly int[] RpUpdFlags = new int[RpRendCap * 2];
    private static readonly int[] RpUpdLayer = new int[RpRendCap * 2];
    private static readonly int[] RpUpdMats = new int[RpRendCap * 2];
    private static readonly int[] RpUpdShader = new int[RpRendCap * 2];
    private static readonly int[] RpUpdQueue = new int[RpRendCap * 2];

    private static readonly int[] RpRenFlags = new int[RpRendCap * 2];
    private static readonly int[] RpRenLayer = new int[RpRendCap * 2];
    private static readonly int[] RpRenMats = new int[RpRendCap * 2];
    private static readonly int[] RpRenShader = new int[RpRendCap * 2];
    private static readonly int[] RpRenQueue = new int[RpRendCap * 2];

    private static readonly int[] RpFstFlags = new int[RpRendCap * 2];
    private static readonly int[] RpFstLayer = new int[RpRendCap * 2];
    private static readonly int[] RpFstMats = new int[RpRendCap * 2];
    private static readonly int[] RpFstShader = new int[RpRendCap * 2];
    private static readonly int[] RpFstQueue = new int[RpRendCap * 2];

    private static readonly Vector4[] RpUpdGlobal = new Vector4[RpGlobalCap];
    private static readonly Vector4[] RpFstGlobal = new Vector4[RpGlobalCap];
    private static readonly Vector4[] RpRenGlobal = new Vector4[RpGlobalCap];
    private static readonly int[] RpGlobalDisagree = new int[RpGlobalCap];
    private static readonly int[] RpGlobalCrossCam = new int[RpGlobalCap];
    private static readonly string[] RpGlobalWitness = new string[RpGlobalCap];

    private static readonly int[] RpFieldDisagree = new int[RpFieldCount];
    private static readonly int[] RpFieldCrossCam = new int[RpFieldCount];
    private static readonly string[] RpFieldWitness = new string[RpFieldCount];

    private static readonly string[] RpCamName = new string[RpCamCap];
    private static readonly int[] RpCamPasses = new int[RpCamCap];
    private static readonly uint[] RpCamMask = new uint[RpCamCap];
    private static readonly float[] RpCamDepth = new float[RpCamCap];
    private static readonly int[] RpCamBuffers = new int[RpCamCap];
    private static readonly int[] RpCamEffects = new int[RpCamCap];
    private static readonly bool[] RpCamIsHead = new bool[RpCamCap];
    private static readonly int[] RpCamAdmits = new int[RpCamCap];
    private static readonly int[] RpCamDisagree = new int[RpCamCap];
    private static int _rpCamNamed, _rpCamOverflow;

    private static int _rpCamPasses, _rpPairs, _rpUnpaired, _rpCrossPairs;
    private static int _rpUpdSamples, _rpUpdFrame = -1;
    private static int _rpFstFrame = -1;
    private static int _rpHeadPasses;
    private static int _rpCurrentMismatch;
    private static int _rpTexIdChanges;
    private static int _rpFaults;
    private static string _rpFirstFault = string.Empty;
    private static string _rpHeadCamName = string.Empty;
    private static uint _rpHeadMask;

    /// <summary>Allocation-free destination for <c>Renderer.GetSharedMaterials</c> — the
    /// <c>sharedMaterials</c> PROPERTY allocates an array on every read, and this one runs inside a
    /// render callback.</summary>
    private static readonly List<Material> RpMatScratch = new(8);

    // ---- arm / disarm ---------------------------------------------------------------------------

    /// <summary>
    /// Arm for the length of the verdict window. Called from <c>ArmVerdict</c> AFTER
    /// <c>ArmRoster</c> and <c>FindHomeTwin</c>, because the tracked set is exactly the renderers
    /// those two resolved — this class never walks a subtree of its own and never touches the scene.
    /// </summary>
    private static void ArmRenderPass()
    {
        _rpArmed = true;
        _rpCamPasses = _rpPairs = _rpUnpaired = _rpCrossPairs = 0;
        _rpUpdSamples = 0;
        _rpUpdFrame = _rpFstFrame = -1;
        _rpHeadPasses = _rpCurrentMismatch = _rpTexIdChanges = 0;
        _rpFaults = 0;
        _rpFirstFault = string.Empty;
        _rpCamNamed = _rpCamOverflow = 0;
        _rpHeadCamName = string.Empty;
        _rpHeadMask = 0u;

        for (int i = 0; i < RpFieldCount; i++)
        {
            RpFieldDisagree[i] = RpFieldCrossCam[i] = 0;
            RpFieldWitness[i] = string.Empty;
        }
        for (int g = 0; g < RpGlobalCap; g++)
        {
            RpGlobalDisagree[g] = RpGlobalCrossCam[g] = 0;
            RpGlobalWitness[g] = string.Empty;
        }
        for (int c = 0; c < RpCamCap; c++)
        {
            RpCamName[c] = string.Empty;
            RpCamPasses[c] = RpCamBuffers[c] = RpCamEffects[c] = RpCamAdmits[c] = RpCamDisagree[c] = 0;
            RpCamMask[c] = 0u;
            RpCamDepth[c] = 0f;
            RpCamIsHead[c] = false;
        }

        if (_rpGlobalIds.Length != RpGlobalNames.Length)
        {
            var ids = new int[RpGlobalNames.Length];
            for (int g = 0; g < ids.Length; g++)
                ids[g] = Shader.PropertyToID(RpGlobalNames[g]);
            _rpGlobalIds = ids;
        }

        _rpRendCount = _rpHeldTracked = _rpTwinTracked = 0;
        for (int i = 0; i < _vRenderers.Length && _rpRendCount < RpRendCap; i++)
            AddTracked(_vRenderers[i], held: true);
        _rpHeldTracked = _rpRendCount;
        for (int i = 0; i < TwinRends.Count && _rpRendCount < RpRendCap * 2; i++)
            AddTracked(TwinRends[i], held: false);
        _rpTwinTracked = _rpRendCount - _rpHeldTracked;

        if (!_rpHooked)
        {
            Camera.onPreRender += OnRenderPassProbe;
            _rpHooked = true;
        }
    }

    private static void AddTracked(Renderer? r, bool held)
    {
        if (r == null)
            return;
        int k = _rpRendCount++;
        RpRend[k] = r;
        RpRendHeld[k] = held;
        RpRendName[k] = r.gameObject.name + " [" + r.GetType().Name + "]";
    }

    /// <summary>Unhook and drop the references. Idempotent, and it runs BEFORE the line is built: a
    /// probe that has answered is spent, and one that keeps a callback on every camera of every
    /// frame after that is the shape this project has already paid for once.</summary>
    private static void DisarmRenderPass()
    {
        if (_rpHooked)
        {
            Camera.onPreRender -= OnRenderPassProbe;
            _rpHooked = false;
        }
        for (int i = 0; i < RpRend.Length; i++)
            RpRend[i] = null;
    }

    // ---- sampling -------------------------------------------------------------------------------

    /// <summary>
    /// The Update-time half of the comparison. Called from <c>SampleVerdict</c>, i.e. from exactly
    /// the place every other arm in this file reads from — that is the point: this snapshot is what
    /// the other sixteen rounds could see, and the in-render snapshot is what they could not.
    /// </summary>
    private static void SampleRenderPassUpdate()
    {
        if (!_rpArmed || _rpRendCount == 0)
            return;
        SampleRenderers(RpUpdFlags, RpUpdLayer, RpUpdMats, RpUpdShader, RpUpdQueue);
        SampleGlobals(RpUpdGlobal);
        _rpUpdFrame = Time.frameCount;
        _rpUpdSamples++;
    }

    private static void SampleRenderers(int[] flags, int[] layer, int[] mats, int[] shader, int[] queue)
    {
        for (int k = 0; k < _rpRendCount; k++)
        {
            Renderer? r = RpRend[k];
            if (r == null)
            {
                flags[k] = -1;
                layer[k] = mats[k] = shader[k] = queue[k] = -1;
                continue;
            }

            int f = 0;
            if (r.enabled) f |= 1;
            if (r.forceRenderingOff) f |= 2;
            if (r.gameObject.activeInHierarchy) f |= 4;
            if (r.isVisible) f |= 8;
            flags[k] = f;
            layer[k] = r.gameObject.layer;

            RpMatScratch.Clear();
            r.GetSharedMaterials(RpMatScratch);
            mats[k] = RpMatScratch.Count;

            Material? lead = RpMatScratch.Count > 0 ? RpMatScratch[0] : null;
            Shader? sh = lead != null ? lead.shader : null;
            shader[k] = sh != null ? sh.name.GetHashCode() : 0;
            queue[k] = lead != null ? lead.renderQueue : -1;
            RpMatScratch.Clear();
        }
    }

    /// <summary>
    /// Read the enumerated global set.
    ///
    /// <para><b>TEXTURE GLOBALS ARE COMPARED BY SIZE AND NULL-NESS, NOT BY IDENTITY, and that is a
    /// correction made to the instrument BEFORE its first reading rather than after it.</b> Half of
    /// these names are bound to TEMPORARY render targets Unity recycles every frame, so an
    /// instance-id compare would report a disagreement on essentially every pair and the line would
    /// read 100 % DISAGREE as a matter of course — a number that says nothing about the picture. The
    /// id churn is still counted, separately and under its own name, so "the target was rebound"
    /// stays visible without being dressed up as a finding.</para>
    /// </summary>
    private static void SampleGlobals(Vector4[] into)
    {
        for (int g = 0; g < _rpGlobalIds.Length; g++)
        {
            int id = _rpGlobalIds[g];
            switch (RpGlobalKinds[g])
            {
                case RpKind.Float:
                    into[g] = new Vector4(Shader.GetGlobalFloat(id), 0f, 0f, 0f);
                    break;
                case RpKind.Int:
                    into[g] = new Vector4(Shader.GetGlobalInt(id), 0f, 0f, 0f);
                    break;
                case RpKind.Vector:
                    into[g] = Shader.GetGlobalVector(id);
                    break;
                case RpKind.Color:
                    into[g] = Shader.GetGlobalColor(id);
                    break;
                default:
                    Texture? t = Shader.GetGlobalTexture(id);
                    into[g] = t == null
                        ? new Vector4(0f, 0f, 0f, 0f)
                        : new Vector4(1f, t.width, t.height, t.GetInstanceID());
                    break;
            }
        }
    }

    /// <summary>
    /// The in-render half. One callback per camera per frame, wrapped whole: an exception thrown out
    /// of a <c>Camera.onPreRender</c> subscriber is thrown inside the engine's render loop, and an
    /// unguarded instrument that throws there costs the frame rather than the reading.
    /// </summary>
    private static void OnRenderPassProbe(Camera cam)
    {
        if (!_rpArmed || cam == null)
            return;
        try
        {
            RenderPassPass(cam);
        }
        catch (System.Exception e)
        {
            _rpFaults++;
            if (_rpFirstFault.Length == 0)
                _rpFirstFault = e.GetType().Name + ": " + e.Message;
        }
    }

    private static void RenderPassPass(Camera cam)
    {
        _rpCamPasses++;
        if (Camera.current != null && !ReferenceEquals(Camera.current, cam))
            _rpCurrentMismatch++;

        Camera? head = VRRigDriver.HeadCamera;
        bool isHead = ReferenceEquals(cam, head);
        if (isHead)
        {
            _rpHeadPasses++;
            if (_rpHeadCamName.Length == 0)
            {
                _rpHeadCamName = cam.name;
                _rpHeadMask = unchecked((uint)cam.cullingMask);
            }
        }

        int c = NoteCamera(cam, isHead);
        if (_rpRendCount == 0)
            return;

        SampleRenderers(RpRenFlags, RpRenLayer, RpRenMats, RpRenShader, RpRenQueue);
        SampleGlobals(RpRenGlobal);

        bool paired = _rpUpdFrame == Time.frameCount;
        if (paired)
            _rpPairs++;
        else
            _rpUnpaired++;

        bool firstOfFrame = _rpFstFrame != Time.frameCount;
        if (!firstOfFrame)
            _rpCrossPairs++;

        for (int k = 0; k < _rpRendCount; k++)
        {
            if (paired)
            {
                CompareField(0, k, c, RpUpdFlags[k] & 1, RpRenFlags[k] & 1, crossCam: false);
                CompareField(1, k, c, (RpUpdFlags[k] >> 1) & 1, (RpRenFlags[k] >> 1) & 1, crossCam: false);
                CompareField(2, k, c, (RpUpdFlags[k] >> 2) & 1, (RpRenFlags[k] >> 2) & 1, crossCam: false);
                CompareField(3, k, c, (RpUpdFlags[k] >> 3) & 1, (RpRenFlags[k] >> 3) & 1, crossCam: false);
                CompareField(4, k, c, RpUpdLayer[k], RpRenLayer[k], crossCam: false);
                CompareField(5, k, c, RpUpdMats[k], RpRenMats[k], crossCam: false);
                CompareField(6, k, c, RpUpdShader[k], RpRenShader[k], crossCam: false);
                CompareField(7, k, c, RpUpdQueue[k], RpRenQueue[k], crossCam: false);
            }

            if (!firstOfFrame)
            {
                CompareField(0, k, c, RpFstFlags[k] & 1, RpRenFlags[k] & 1, crossCam: true);
                CompareField(1, k, c, (RpFstFlags[k] >> 1) & 1, (RpRenFlags[k] >> 1) & 1, crossCam: true);
                CompareField(2, k, c, (RpFstFlags[k] >> 2) & 1, (RpRenFlags[k] >> 2) & 1, crossCam: true);
                CompareField(3, k, c, (RpFstFlags[k] >> 3) & 1, (RpRenFlags[k] >> 3) & 1, crossCam: true);
                CompareField(4, k, c, RpFstLayer[k], RpRenLayer[k], crossCam: true);
                CompareField(5, k, c, RpFstMats[k], RpRenMats[k], crossCam: true);
                CompareField(6, k, c, RpFstShader[k], RpRenShader[k], crossCam: true);
                CompareField(7, k, c, RpFstQueue[k], RpRenQueue[k], crossCam: true);
            }
        }

        for (int g = 0; g < _rpGlobalIds.Length; g++)
        {
            Vector4 now = RpRenGlobal[g];
            bool tex = RpGlobalKinds[g] == RpKind.Tex;
            if (paired && !SameGlobal(RpUpdGlobal[g], now, tex))
            {
                RpGlobalDisagree[g]++;
                RpCamDisagree[c]++;
                if (RpGlobalWitness[g].Length == 0)
                    RpGlobalWitness[g] = DescribeGlobalDelta(g, RpUpdGlobal[g], now, cam.name, "Update");
            }
            if (!firstOfFrame && !SameGlobal(RpFstGlobal[g], now, tex))
            {
                RpGlobalCrossCam[g]++;
                if (RpGlobalWitness[g].Length == 0)
                    RpGlobalWitness[g] = DescribeGlobalDelta(g, RpFstGlobal[g], now, cam.name,
                                                            "the frame's first camera pass");
            }
            if (tex && paired && RpUpdGlobal[g].w != now.w)
                _rpTexIdChanges++;
        }

        if (firstOfFrame)
        {
            _rpFstFrame = Time.frameCount;
            System.Array.Copy(RpRenFlags, RpFstFlags, RpRenFlags.Length);
            System.Array.Copy(RpRenLayer, RpFstLayer, RpRenLayer.Length);
            System.Array.Copy(RpRenMats, RpFstMats, RpRenMats.Length);
            System.Array.Copy(RpRenShader, RpFstShader, RpRenShader.Length);
            System.Array.Copy(RpRenQueue, RpFstQueue, RpRenQueue.Length);
            System.Array.Copy(RpRenGlobal, RpFstGlobal, RpRenGlobal.Length);
        }
    }

    /// <summary>Texture globals compare on presence and SIZE only — see <see cref="SampleGlobals"/>
    /// for why an identity compare would manufacture a 100 % disagreement.</summary>
    private static bool SameGlobal(Vector4 a, Vector4 b, bool texture) =>
        texture
            ? a.x == b.x && a.y == b.y && a.z == b.z
            : a == b;

    private static void CompareField(int field, int k, int cam, int a, int b, bool crossCam)
    {
        if (a == b)
            return;
        if (crossCam)
        {
            RpFieldCrossCam[field]++;
        }
        else
        {
            RpFieldDisagree[field]++;
            RpCamDisagree[cam]++;
        }

        if (RpFieldWitness[field].Length == 0)
            RpFieldWitness[field] = RpFieldNames[field] + " on "
                + (RpRendHeld[k] ? "the HELD prop's '" : "the HOME TWIN's '") + RpRendName[k]
                + "' read " + a + " " + (crossCam ? "on the frame's first camera pass" : "in Update")
                + " and " + b + " inside '" + RpCamName[cam] + "'";
    }

    private static string DescribeGlobalDelta(int g, Vector4 a, Vector4 b, string cam, string against)
    {
        string name = RpGlobalNames[g];
        if (RpGlobalKinds[g] == RpKind.Tex)
            return name + " was " + (a.x == 0f ? "<null>" : a.y + "x" + a.z) + " against " + against
                   + " and " + (b.x == 0f ? "<null>" : b.y + "x" + b.z) + " inside '" + cam + "'";
        if (RpGlobalKinds[g] == RpKind.Float || RpGlobalKinds[g] == RpKind.Int)
            return name + " was " + a.x.ToString("0.####") + " against " + against + " and "
                   + b.x.ToString("0.####") + " inside '" + cam + "'";
        return name + " was " + a.ToString("0.###") + " against " + against + " and "
               + b.ToString("0.###") + " inside '" + cam + "'";
    }

    /// <summary>Find or claim this camera's row. Everything expensive about a camera —
    /// <c>commandBufferCount</c>, the OnRenderImage census, the mask-vs-layer count — is read ONCE,
    /// on the pass that first names it, and never per pass.</summary>
    private static int NoteCamera(Camera cam, bool isHead)
    {
        string n = cam.name;
        for (int c = 0; c < _rpCamNamed; c++)
        {
            if (RpCamName[c] == n)
            {
                RpCamPasses[c]++;
                return c;
            }
        }

        if (_rpCamNamed >= RpCamCap)
        {
            _rpCamOverflow++;
            return RpCamCap - 1;
        }

        int k = _rpCamNamed++;
        RpCamName[k] = n;
        RpCamPasses[k] = 1;
        RpCamMask[k] = unchecked((uint)cam.cullingMask);
        RpCamDepth[k] = cam.depth;
        RpCamIsHead[k] = isHead;
        RpCamBuffers[k] = cam.commandBufferCount;

        int fx = 0;
        MonoBehaviour[] behaviours = cam.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour? mb = behaviours[i];
            if (mb == null)
                continue;
            if (mb.GetType().GetMethod("OnRenderImage",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic) != null)
                fx++;
        }
        RpCamEffects[k] = fx;

        int admits = 0;
        for (int i = 0; i < _rpRendCount; i++)
        {
            Renderer? r = RpRend[i];
            if (r != null && (RpCamMask[k] & (1u << r.gameObject.layer)) != 0u)
                admits++;
        }
        RpCamAdmits[k] = admits;
        return k;
    }

    // ---- the one line ---------------------------------------------------------------------------

    /// <summary>
    /// Emit and unhook. One line per hold, its own grep token
    /// (<c>] .*HELD-PROP RENDER-PASS PROBE</c>), and it prints on EVERY outcome including the ones
    /// that measure nothing — a silent instrument reads exactly like a negative result, and that
    /// mistake is the single most expensive one in this file's record.
    /// </summary>
    private static void EmitRenderPass(string why)
    {
        if (!_rpArmed)
            return;
        _rpArmed = false;
        DisarmRenderPass();

        var sb = new StringBuilder(4096);
        sb.Append("[Props] HELD-PROP RENDER-PASS PROBE — closed because ").Append(why)
          .Append(". WHAT IT IS FOR, AND IT IS THE ASSUMPTION SIXTEEN ROUNDS SHARED WITHOUT WRITING "
                  + "IT DOWN: every previous probe in this file samples in Update or LateUpdate, so "
                  + "a value that exists ONLY inside the render loop — a renderer switched on by "
                  + "one camera's pre-render and off in its post-render, Renderer.forceRenderingOff "
                  + "(a SEPARATE flag from .enabled that no probe here had ever read), a shader "
                  + "global re-published mid-frame by a command buffer, a material or layer swapped "
                  + "for one pass — is invisible to all of them and reads as CHANGES 0 forever "
                  + "while the picture changes. This arm takes BOTH readings in the SAME FRAME and "
                  + "reports where they disagree. ");

        sb.Append("TRACKED: ").Append(_rpHeldTracked).Append(" renderer(s) on the held prop and ")
          .Append(_rpTwinTracked).Append(" on the home twin (cap ").Append(RpRendCap)
          .Append(" a side), ").Append(_rpGlobalIds.Length)
          .Append(" shader global(s) — the CLOSED set of every Shader/CommandBuffer SetGlobal name "
                  + "in decompiled/, plus the two Unity built-ins the fog and Beautify paths "
                  + "overwrite (_CameraDepthTexture, _GrabTexture). ");

        sb.Append("COUNTS: camera passes ").Append(_rpCamPasses)
          .Append(", of them head-camera passes ").Append(_rpHeadPasses)
          .Append("; Update-time samples ").Append(_rpUpdSamples)
          .Append("; PAIRS (a camera pass in a frame that had already produced an Update sample) ")
          .Append(_rpPairs).Append("; unpaired passes ").Append(_rpUnpaired)
          .Append("; SECOND-AND-LATER passes of the same frame ").Append(_rpCrossPairs).Append(". ");

        AppendRenderPassVerdict(sb);
        AppendRenderPassCameras(sb);
        AppendRenderPassFields(sb);

        if (_rpFaults > 0)
            sb.Append("THE CALLBACK THREW ").Append(_rpFaults)
              .Append(" time(s), first: ").Append(_rpFirstFault)
              .Append(" — every count above is a FLOOR and the verdict is not safe. ");

        sb.Append("WHAT IS STILL BEYOND THIS INSTRUMENT, stated so the next round does not read a "
                  + "row of zeroes as more than it is: MaterialPropertyBlock contents (a block is "
                  + "write-only from managed code, and CommandBuffer.DrawRenderer can carry one); a "
                  + "global written by a command buffer that runs AFTER onPreRender on the SAME "
                  + "camera (the head camera carries none — commandBufferCount is printed per "
                  + "camera above so that stays a reading and not a quote); a replacement shader "
                  + "(Camera.SetReplacementShader has no managed getter in 2021.3 — the game's own "
                  + "uses are on Beautify's and VolumetricFog's private depth cameras, which appear "
                  + "in the camera roster above if they rendered); anything an image effect "
                  + "composites after onPostRender; and whatever the OpenXR compositor draws outside "
                  + "Unity's camera loop, which 2021.3 exposes no managed API to enumerate. ");

        sb.Append("AND THE SIGNATURE ANY SURVIVING HYPOTHESIS STILL HAS TO FIT: the flash measured "
                  + "off .planning/debug/falle_aufblitzen.mp4 is a saturating ~1.0-1.4 s ramp IN "
                  + "that then clears COMPLETELY inside one 0.1 s step, and the light it adds is "
                  + "NEUTRAL. A boolean field that disagrees here would FLICKER, not ramp, so a "
                  + "disagreement above still owes an account of the ramp — and 'the ramp is "
                  + "unexplained' is an honest thing to write down and better than a fitted story.");

        // HW-VERIFY: this is the answer-bearing line of round seventeen. It must stay at a tier the
        // DEFAULT log level prints — the whole class exists because sixteen rounds of Update-time
        // readings could not see the render loop, and a reading nobody can read is the seventeenth.
        // scripts/check-hw-verify.py enforces the position of this comment.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    private static void AppendRenderPassVerdict(StringBuilder sb)
    {
        int fieldDis = 0, globDis = 0, fieldCross = 0, globCross = 0;
        for (int i = 0; i < RpFieldCount; i++)
        {
            fieldDis += RpFieldDisagree[i];
            fieldCross += RpFieldCrossCam[i];
        }
        for (int g = 0; g < _rpGlobalIds.Length; g++)
        {
            globDis += RpGlobalDisagree[g];
            globCross += RpGlobalCrossCam[g];
        }

        if (_rpRendCount == 0)
        {
            sb.Append("*** INERT — NOTHING WAS TRACKED. The roster and the home-twin search "
                      + "produced no renderer, so no field was compared and this excludes NOTHING. "
                      + "Read the HELD-PROP HOME TWIN line for why the roster was empty. *** ");
            return;
        }
        if (_rpCamPasses == 0)
        {
            sb.Append("*** INERT — Camera.onPreRender did not fire ONCE while this arm was armed. "
                      + "No camera rendered through the built-in callback, so the hook cannot have "
                      + "seen or missed anything, and this says nothing about the render loop. *** ");
            return;
        }
        if (_rpPairs == 0)
        {
            sb.Append("*** INERT — ").Append(_rpCamPasses)
              .Append(" camera pass(es) went past this hook, but NOT ONE of them fell in a frame "
                      + "that had already produced an Update-time sample (Update samples ")
              .Append(_rpUpdSamples)
              .Append("). There is nothing to compare against, so a 'no disagreement' reading here "
                      + "would be an ABSENCE and not an EXCLUSION. *** ");
            return;
        }
        if (_rpHeadPasses == 0)
        {
            sb.Append("*** PARTIAL — ").Append(_rpPairs)
              .Append(" pair(s) were compared, but NONE of them was the rig's own head camera "
                      + "(VRRigDriver.HeadCamera read null or never rendered through this hook). "
                      + "The verdict below is about the cameras that DID render and does NOT cover "
                      + "the VR view's own pass. *** ");
        }

        if (fieldDis == 0 && globDis == 0 && fieldCross == 0 && globCross == 0)
        {
            sb.Append("*** AGREE — over ").Append(_rpPairs).Append(" paired pass(es) and ")
              .Append(_rpCrossPairs)
              .Append(" second-or-later pass(es) of the same frame, the in-render reading was "
                      + "IDENTICAL to the same frame's Update-time reading on every tracked field "
                      + "of every tracked renderer AND on every enumerated global, and identical "
                      + "BETWEEN CAMERAS. The in-render class is EXCLUDED for these fields: nothing "
                      + "about these renderers or these globals is different inside the render loop "
                      + "from what sixteen rounds of Update-time probes already read. Read WHAT IS "
                      + "STILL BEYOND THIS INSTRUMENT below before treating that as the whole "
                      + "render loop. *** ");
        }
        else
        {
            sb.Append("*** DISAGREE — the render loop and Update do NOT hold the same values. "
                      + "Renderer-field disagreements ").Append(fieldDis)
              .Append(" (plus ").Append(fieldCross)
              .Append(" BETWEEN two cameras of the same frame); global disagreements ")
              .Append(globDis).Append(" (plus ").Append(globCross)
              .Append(" between cameras). This is the class no probe in this file could see. The "
                      + "named witnesses below say which field, which renderer, which camera and "
                      + "both values. *** ");
        }

        if (_rpTexIdChanges > 0)
            sb.Append("TEXTURE GLOBALS REBOUND (instance id changed between the Update read and the "
                      + "in-render read) on ").Append(_rpTexIdChanges)
              .Append(" reading(s) — counted, and DELIBERATELY NOT counted as a disagreement: half "
                      + "these names are bound to temporary render targets Unity recycles every "
                      + "frame, so an identity compare would print 100% DISAGREE as a matter of "
                      + "course. Texture globals are compared on presence and SIZE. ");

        if (_rpCurrentMismatch > 0)
            sb.Append("Camera.current disagreed with the callback's own argument on ")
              .Append(_rpCurrentMismatch)
              .Append(" pass(es) — on those, a reading attributed to a camera name may belong to "
                      + "another pass. ");
    }

    private static void AppendRenderPassCameras(StringBuilder sb)
    {
        sb.Append("THE CAMERAS THAT RENDERED, one row each — this is also where the ModBuild 467 "
                  + "claim that 'the head camera carries ZERO command buffers, so the Outline pass "
                  + "does not run on it' stops being an assertion and becomes a reading: ")
          .Append(_rpCamNamed).Append(" named");
        if (_rpCamOverflow > 0)
            sb.Append(" (").Append(_rpCamOverflow).Append(" further pass(es) fell past the cap of ")
              .Append(RpCamCap).Append(" and were folded into the last row)");
        sb.Append(". ");
        for (int c = 0; c < _rpCamNamed; c++)
        {
            sb.Append('[').Append(c).Append("] '").Append(RpCamName[c]).Append("' ")
              .Append(RpCamIsHead[c] ? "THE RIG'S HEAD CAMERA" : "game/other")
              .Append(", passes ").Append(RpCamPasses[c])
              .Append(", depth ").Append(RpCamDepth[c].ToString("0.##"))
              .Append(", cullingMask 0x").Append(RpCamMask[c].ToString("X8"))
              .Append(" (admits ").Append(RpCamAdmits[c]).Append(" of ").Append(_rpRendCount)
              .Append(" tracked renderer(s) by layer), commandBufferCount ").Append(RpCamBuffers[c])
              .Append(", MonoBehaviour(s) with OnRenderImage ").Append(RpCamEffects[c])
              .Append(", disagreements attributed to it ").Append(RpCamDisagree[c]).Append(". ");
        }
        if (_rpHeadCamName.Length > 0)
            sb.Append("HEAD CAMERA MATCHED: '").Append(_rpHeadCamName).Append("' mask 0x")
              .Append(_rpHeadMask.ToString("X8")).Append(". ");
        else
            sb.Append("HEAD CAMERA: never matched a pass. ");
    }

    private static void AppendRenderPassFields(StringBuilder sb)
    {
        sb.Append("PER FIELD (Update vs in-render / between two cameras of one frame): ");
        for (int i = 0; i < RpFieldCount; i++)
            sb.Append(RpFieldNames[i]).Append(' ').Append(RpFieldDisagree[i]).Append('/')
              .Append(RpFieldCrossCam[i]).Append(i == RpFieldCount - 1 ? ". " : ", ");

        int named = 0;
        for (int i = 0; i < RpFieldCount; i++)
        {
            if (RpFieldWitness[i].Length == 0)
                continue;
            if (named++ == 0)
                sb.Append("FIRST WITNESS PER DISAGREEING FIELD: ");
            sb.Append(RpFieldWitness[i]).Append("; ");
        }

        sb.Append("GLOBALS THAT MOVED: ");
        int moved = 0;
        for (int g = 0; g < _rpGlobalIds.Length; g++)
        {
            if (RpGlobalDisagree[g] == 0 && RpGlobalCrossCam[g] == 0)
                continue;
            moved++;
            sb.Append(RpGlobalNames[g]).Append(' ').Append(RpGlobalDisagree[g]).Append('/')
              .Append(RpGlobalCrossCam[g]).Append(" — ").Append(RpGlobalWitness[g]).Append("; ");
        }
        if (moved == 0)
            sb.Append("none of the ").Append(_rpGlobalIds.Length)
              .Append(" enumerated global(s) held a different value inside a render pass than it "
                      + "held in the same frame's Update, or between two cameras of one frame. ");
    }
}
