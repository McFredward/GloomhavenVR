using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// ROUND EIGHTEEN — A NAMED MECHANISM READ OUT OF THE GAME'S OWN DATA, AND THE FIRST INSTRUMENT IN
/// THIS FILE THAT CAN TELL AN <b>ABSENCE</b> FROM AN <b>EXCLUSION</b>.
///
/// <para><b>THE STRUCTURAL FAULT SEVENTEEN ROUNDS SHARE, AND IT IS NOT ANY ONE CANDIDATE.</b>
/// Every reading this file has produced has the form "over N frames, NOTHING CHANGED".
/// <c>BOARD PROP STANDING WATCH</c> reads <c>CHANGES 0</c> over 1794 frames; the twin comparison
/// reads <c>0 slot(s) EVER DIFFERED</c>; the render-pass probe reads <c>AGREE</c> over 1436 pairs.
/// Every one of them is conditional on a premise no instrument here has ever measured — <i>that the
/// white was present while they were taken</i>. The ModBuild 465 line says so about itself, in the
/// log, in these words: <c>a 0 here on a window that contained NO flash proves nothing either, so
/// check it against whether the user saw white on this hold</c>. Nobody could check it, because
/// checking it needs the picture. <b>Seventeen clean readings stay unqualified until something can
/// say the white was there.</b></para>
///
/// <para><b>AND A CANDIDATE THAT FITS EVERY TERM OF THE SIGNATURE HAS NOW BEEN FOUND IN THE GAME'S
/// OWN CODE.</b> <c>decompiled/GH.Runtime/SpawnObjectAnimateMaterial_SMB.cs</c> is a
/// <c>StateMachineBehaviour</c> whose <c>OnStateUpdate</c> is, in full:
/// <code>
/// t += Timekeeper.instance.m_GlobalClock.deltaTime / animTime;
/// float value = myCurve.Evaluate(t) * animStrength;
/// rend = animator.GetComponentsInChildren&lt;Renderer&gt;();
/// rend[i].material.SetFloat(animProperty, value);
/// </code>
/// and whose <c>OnStateExit</c> is <c>t = 0f;</c>. Term for term against the ModBuild 466 photometry
/// of <c>.planning/debug/falle_aufblitzen.mp4</c>: a rise over <c>animTime</c> (measured
/// ~1.0-1.4 s); a PLATEAU, because <c>AnimationCurve.Evaluate</c> clamps past its last key; a
/// COMPLETE CLEAR INSIDE ONE FRAME at the state boundary, because <c>t</c> is reset and the next
/// update writes <c>Evaluate(0)</c>; the WHOLE PROP, because it walks
/// <c>GetComponentsInChildren&lt;Renderer&gt;</c>; NEUTRAL light, because it is a scalar
/// <c>SetFloat</c> whose colour comes from the shader; EVERY TRAP AND CHEST AT ONCE, because every
/// prop animator starts at scenario load; and the HAND UNAFFECTED, because the hand is not under
/// that animator. Nothing else in seventeen rounds has fitted the ramp at all.</para>
///
/// <para><b>WHY IT IS A CENSUS AND NOT A FIX.</b> <c>animProperty</c>, <c>animTime</c>,
/// <c>animStrength</c> and <c>myCurve</c> are SERIALIZED FIELDS: their values live in the
/// AssetBundle, not in <c>decompiled/</c>, so no amount of source reading can say what property is
/// written or over how long — or whether this behaviour is attached to a trap's controller at all.
/// One <c>Animator.GetBehaviours</c> call answers all of it from the live object, and it is STATIC
/// DATA: unlike every other reading in this file it does not need the flash to happen while it
/// looks. If the behaviour is there, the mechanism is named and the property a fix must write is
/// named with it. If it is not, the best-fitting candidate this file has ever had is excluded from
/// the game's own data rather than by another window of zeroes.</para>
///
/// <para><b>THE CENSUS ALSO CARRIES THE FINGERPRINT THAT WOULD PROVE IT HAS RUN.</b>
/// <c>rend[i].material</c> — not <c>sharedMaterial</c> — INSTANTIATES a clone the first time it is
/// touched, and Unity renames the clone <c>&lt;name&gt; (Instance)</c>. So a material name carrying
/// that suffix is positive evidence that something took the <c>.material</c> path on that renderer;
/// its absence is positive evidence that nothing has. The ModBuild 468 roster reads
/// <c>materials 'Trap_BearTrap_MAT' on Amp_Char_Shader</c> with NO suffix, which is a real reading
/// against this candidate on that prop and is printed here every session rather than quoted once.
/// It matters for a second reason: <c>PropTable.Resolve</c> caches the material it found AT ARM, so
/// an instantiation that happens LATER would leave the property table reading an original nothing
/// writes any more — a way for the flash to move a property while <c>0 of 57 tracked slot(s)</c>
/// stays true.</para>
///
/// <para><b>THE PHOTOMETER: THE WITNESS.</b> It reads the actual pixels the eye receives, in a small
/// window centred on the prop, at ~13-23 Hz, and prints the series as numbers — baseline, peak, rise
/// time, fall time, episode count, and the R:G:B ratio of what was ADDED. Those are the quantities
/// ModBuild 466 measured by hand off the video (saturating ~1.0-1.4 s ramp in, complete clear inside
/// one 0.1 s step, added light NEUTRAL at 1.000/0.995/1.010), so the in-process reading and the
/// video either confirm each other or one of them is wrong.</para>
///
/// <para><b>AND IT BISECTS THE REMAINING SPACE WITH ONE QUESTION.</b> Every round so far has asked
/// <i>why</i> the prop's renderers might shade white. None has asked whether they are the thing
/// painting at all. So when the photometer SEES the white rising, this class sets
/// <see cref="Renderer.forceRenderingOff"/> on the prop's own renderers for
/// <see cref="PhBlinkFrames"/> frames and reads the same pixels again:
/// <list type="bullet">
/// <item>the patch falls to the background level ⇒ <b>the prop's own renderers were painting the
/// white</b>, and everything outside the prop's own shading is deleted;</item>
/// <item>the patch stays bright ⇒ <b>the prop's renderers are NOT painting it</b>, and the prop's
/// materials, shaders, property blocks and state — the whole subject of seventeen rounds — are
/// deleted in one reading.</item>
/// </list>
/// Neither answer can come back "no candidate". Both halve what is left. The answer arrives in the
/// LOG and not through the user's eye on a one-frame blink, which is what makes it an experiment.</para>
///
/// <para><b>THE NULL-PERTURBATION CONTROL, BECAUSE ROUND SEVENTEEN PAID FOR ITS ABSENCE.</b> A blink
/// that does nothing looks exactly like a blink proving the prop is not the painter. So a SECOND
/// blink fires earlier, at BASELINE, with no white present, and measures how much the prop's own
/// renderers contribute to that patch NORMALLY. If the baseline blink shows no drop, the blink is
/// inert, the white blink means nothing, and the line says that instead of claiming a finding. It is
/// the same clause structure that made the ModBuild 467 occlusion A/B a real exclusion
/// (<c>VALUE FOUND BEFORE THE WRITE: 1..1</c>), applied to a perturbation of the picture.</para>
///
/// <para><b>THE CAPTURE PATH IS NOT NEW AND IT IS NOT A GUESS.</b> It is the one
/// <c>WorldUI/EyeFrameProbe.cs</c> ran on this rig from ModBuild 192 until 219, when it was deleted
/// as spent apparatus for a solved defect: a <see cref="CommandBuffer"/> on the mod's head camera at
/// <see cref="CameraEvent.AfterEverything"/>, re-recorded per pass in <c>Camera.onPreRender</c>,
/// recording <b>exactly one</b> <c>Blit</c> from <see cref="BuiltinRenderTextureType.CurrentActive"/>
/// into a full-eye single-sampled temporary — which resolves the 8x MSAA eye buffer — and then
/// <c>CommandBuffer.CopyTexture</c> REGION COPIES out of that resolve, each followed immediately by
/// <c>RequestAsyncReadback</c>, so readback N carries the pixels copy N produced.
/// <b>ModBuild 191's fault is designed out rather than avoided by care:</b> <c>Blit</c> REBINDS the
/// active render target, so a second <c>Blit</c> from <c>CurrentActive</c> is a blit of the probe's
/// own destination onto itself — 191 read bit-identical pixels for a whole build because of it.
/// <c>CopyTexture</c> takes explicit integer coordinates, uses no material and no full-screen quad,
/// and rebinds nothing; <c>CurrentActive</c> is read once, as the pass's first command. The three
/// capture paths this project has PROVEN DEAD are avoided by construction, not by hope: the game-view
/// backbuffer (this mod empties it on purpose — <c>FlatScreen.3.Desktop.cs</c>), a DEFERRED camera
/// re-rendered into an RT we own (nothing is re-rendered here, and the rig line reads
/// <c>renderingPath=Forward/actual=Forward</c> anyway), and a capture camera on a SHARED culling
/// layer (there is no capture camera and no layer is touched).</para>
///
/// <para><b>WHY NOT AN ISOLATED RE-RENDER OF THE PROP.</b> That was the other way to ask the
/// bisection question and it is UNSOUND HERE on three independent grounds, each of which produces a
/// FALSE NEGATIVE — the prop rendering clean in isolation while the real view bleaches. Isolating it
/// needs either a private culling layer (a layer write on a game object, and this project's record
/// says a capture camera on a SHARED layer draws every other object on it) or a hand-built
/// <c>DrawRenderer</c>, and neither reproduces the main pass's per-camera state; the one off-screen
/// re-render this project ever got working is the map's forward UNLIT albedo camera, which by
/// construction cannot show a shading term at all; and a replacement shader — explicitly on the
/// render-pass probe's "still beyond this instrument" list — would not apply to an explicit
/// <c>DrawRenderer</c>. Photographing the REAL frame has none of those asymmetries.</para>
///
/// <para><b>THE PRE-REGISTERED READINGS, so a null result means something.</b>
/// <list type="number">
/// <item><b>INERT</b> — 0 readbacks completed, or the platform reports no
/// <c>AsyncGPUReadback</c>/<c>CopyTexture</c> support, or the head camera never matched, or both
/// patches were BIT-IDENTICAL across the whole window (the ModBuild 191 dead-capture signature).
/// Nothing is measured, no state reading anywhere is qualified, and the next round's first job is
/// this capture path.</item>
/// <item><b>NO WHITE IN THE WINDOW</b> — readbacks completed and the CONTROL patch varied, so the
/// capture is live, but the prop patch never rose past its own trigger. Then every "CHANGES 0" taken
/// in this window is an ABSENCE, exactly as the 465 line warned about itself, and seventeen rounds of
/// exclusions are unqualified rather than wrong.</item>
/// <item><b>WHITE WITNESSED</b> — an excursion, with its rise and fall times and the added light's
/// ratio printed. Every state reading co-recorded in this window becomes a genuine EXCLUSION, and the
/// bisection verdict says which half of the remaining space survives.</item>
/// </list></para>
///
/// <para><b>WHAT IT WRITES, AND FOR HOW LONG.</b> Only <see cref="Renderer.forceRenderingOff"/>, on
/// the prop's own roster renderers, for at most <see cref="PhBlinkFrames"/> frames, at most
/// <see cref="PhBlinkBudget"/> times per armed window, handing back the value it replaced object for
/// object — and unconditionally again when the window closes, when the belt resets and when the prop
/// is released. That is about 0.15 s of the prop not being drawn, twice, which is the price of the
/// only reading on the table that can delete half the search space. It writes NO game state, no
/// transform, no material, no layer, no shader global and no animator field; nothing goes on the
/// wire, and a flat or peer player sees exactly what they saw before.</para>
/// </summary>
internal static partial class PropAnimBelt
{
    // ---- the dials ------------------------------------------------------------------------------

    /// <summary>Window length IN SECONDS, never in frames. ModBuild 457 sized a window at 3600
    /// FRAMES against an assumed 90 Hz on a rig that measured 38.5 fps, so the window was 93 s, the
    /// session ended inside it and it printed NOTHING AT ALL. The video's episodes are ~10 s apart,
    /// and the trigger has to calibrate on one episode and fire on a later one, so this must hold
    /// three.</summary>
    private const float PhSeconds = 35f;

    /// <summary>Capture every Nth rendered frame. At the 38.5-70 fps this rig has measured that is
    /// 13-23 Hz against a ramp of ~1.0-1.4 s — the video photometry resolved the same curve at
    /// 10 Hz.</summary>
    private const int PhCadence = 3;

    /// <summary>Patch edge in EYE PIXELS. The video's lit region is ~9 % of a 1280x720 frame, about
    /// 288 px square, so a 32 px window centred on the prop lies well inside it. A region copy cannot
    /// resample, so this is a texel-for-texel crop and not a downscale.</summary>
    private const int PhPatch = 32;

    /// <summary>How far sideways the CONTROL patch sits, in eye pixels: far enough to be off the
    /// prop, near enough to share the frame's own lighting.</summary>
    private const int PhControlOffsetPx = 220;

    /// <summary>Frames one blink lasts. It must exceed <see cref="PhCadence"/> so a capture falls
    /// inside it; six gives two captures and is ~0.15 s at 40 fps.</summary>
    private const int PhBlinkFrames = 6;

    /// <summary>Blinks per armed window: one BASELINE (the null-perturbation control) and one
    /// WHITE.</summary>
    private const int PhBlinkBudget = 2;

    /// <summary>Samples that must land before the trigger may be computed. Below this the observed
    /// range is noise and a blink fired off it would prove nothing.</summary>
    private const int PhCalibrateSamples = 40;

    /// <summary>The excursion the observed range must reach, in LINEAR light on 0..1, before the
    /// white trigger may arm. The video's ramp runs 132 -> 247 in gamma-encoded V, i.e. 0.23 -> 0.93
    /// linear: a floor of 0.08 is far below that and far above sensor noise.</summary>
    private const float PhMinExcursion = 0.08f;

    /// <summary>Where in the observed range the white trigger sits. The video's ramp crosses this
    /// about 0.4 s in, leaving most of the plateau for the blink to land in.</summary>
    private const float PhTriggerFraction = 0.45f;

    /// <summary>Samples kept. 35 s at 23 Hz is 805; the series IS the evidence so it is not
    /// down-sampled, and the count actually stored is printed beside every statistic.</summary>
    private const int PhSampleCap = 1024;

    /// <summary>A pixel is NEAR-WHITE when every channel is at or above this 0..255 value — the same
    /// threshold the ModBuild 466 video photometry used, so the two fractions are comparable.</summary>
    private const byte PhNearWhite = 200;

    /// <summary>Windows per session. One armed observation is one hardware answer.</summary>
    private const int PhBudget = 2;

    /// <summary>Component type names named individually in the census. Beyond that the count is
    /// printed — a truncated list is not an absence.</summary>
    private const int PhTypeCap = 40;

    // ---- capture state --------------------------------------------------------------------------

    private static readonly int PhResolveId = Shader.PropertyToID("_GloomhavenVR_PropPhotoResolve");

    private static CommandBuffer? _phCb;
    private static RenderTexture? _phPatchRt;
    private static Camera? _phBound;
    private static bool _phHooked, _phArmed, _phDead;
    private static int _phBudgetLeft = PhBudget;
    private static int _phGen;

    /// <summary>Which patch a pending readback belongs to — 0 the prop, 1 the control — and the
    /// generation that requested it. FIFO, exactly as EyeFrameProbe proved: the copy and its readback
    /// enter the command stream together, so readback N carries copy N's pixels and one shared target
    /// serves both patches.</summary>
    private static readonly Queue<int> PhPendingSlot = new(8);
    private static readonly Queue<int> PhPendingGen = new(8);

    private static Renderer? _phLead;

    /// <summary>THE BLINK'S OWN COPY OF THE ROSTER. It must NOT read <c>_vRenderers</c>: this
    /// window is 35 s of wall clock and the verdict window is 360 frames, so the next
    /// <c>ArmVerdict</c> reassigns <c>_vRenderers</c> while this observation is still live — and the
    /// blink would then turn a DIFFERENT prop's renderers off and attribute the result to this one.
    /// A held reference also keeps the set stable across the release, which is the point of a window
    /// that outlives the hold.</summary>
    private static readonly List<Renderer> PhRoster = new(8);

    private static string _phLabel = string.Empty;

    /// <summary>True between the census and the line that prints it. It is what lets
    /// <c>EmitPhotometer</c> be called unconditionally from <c>Reset</c> without printing an empty
    /// observation on every scenario change — "a window that armed and found nothing" and "no window
    /// at all" must not print the same sentence, and they must not both print.</summary>
    private static bool _phHaveSnapshot;
    private static float _phT0;
    private static int _phFrames, _phCaptureFrame = -1, _phPassFrame = -1;
    private static int _phResolveBlits, _phCopies, _phReads, _phReadErrors, _phOffTarget;
    private static int _phHeadPasses, _phFaults;
    private static string _phFirstFault = string.Empty;
    private static bool _phResolveInPass;
    private static bool _phSupportChecked, _phSupported;
    private static string _phUnsupported = string.Empty;

    private static float _phPropLin, _phCtlLin, _phPropWhite;
    private static float _phPropR, _phPropG, _phPropB;
    private static bool _phHavePropRead, _phHaveCtlRead;
    private static uint _phPropHash, _phCtlHash;
    private static int _phPropHashChanges, _phCtlHashChanges;
    private static bool _phSeenAnyHash;

    // ---- the series -----------------------------------------------------------------------------

    private static readonly float[] PhT = new float[PhSampleCap];
    private static readonly float[] PhProp = new float[PhSampleCap];
    private static readonly float[] PhCtl = new float[PhSampleCap];
    private static readonly float[] PhWhiteFrac = new float[PhSampleCap];
    private static readonly byte[] PhMark = new byte[PhSampleCap];
    private static int _phCount, _phOverflow;

    private static float _phPeakLin = -1f, _phPeakR, _phPeakG, _phPeakB;

    // ---- the bisection --------------------------------------------------------------------------

    private const byte PhMarkNone = 0, PhMarkBaseBlink = 1, PhMarkWhiteBlink = 2;

    private static int _phBlinksLeft;
    private static bool _phBlinkLive;
    private static byte _phBlinkKind;
    private static int _phBlinkEndFrame;
    private static bool _phBaseBlinkDone, _phWhiteBlinkDone;
    private static readonly List<Renderer> PhBlinkRends = new(8);
    private static readonly List<bool> PhBlinkWas = new(8);
    private static int _phBlinkWrites, _phBlinkRestores, _phBlinkForeign;

    private static float _phBaseBefore = -1f, _phBaseDuring = -1f, _phBaseCtl = -1f;
    private static float _phWhiteBefore = -1f, _phWhiteDuring = -1f, _phWhiteCtl = -1f;
    private static int _phBaseSamples, _phWhiteSamples;

    /// <summary>gamma 0..255 -> linear 0..1, built once. "What was ADDED" is only meaningful in
    /// linear light, which is why the 466 video photometry converted before subtracting; a table
    /// keeps <c>Mathf.Pow</c> out of the readback callback.</summary>
    private static float[]? _phLinTable;

    // ---- the animator-behaviour census ------------------------------------------------------------

    /// <summary>The census snapshot, taken at ARM and printed at EMIT — the same split
    /// <c>ArmRoster</c>/<c>AppendRoster</c> already use here. It is taken at the arm point on
    /// purpose: the material-instance fingerprint is only evidence about the GRAB FRAME, because
    /// anything that touches <c>Renderer.material</c> later would change it under the reading.</summary>
    private static string _phSmbReport = string.Empty;
    private static string _phTypeReport = string.Empty;
    private static string _phMatReport = string.Empty;

    // ---- arm / disarm ---------------------------------------------------------------------------

    /// <summary>
    /// Arm the photometer and take the census. Called from <c>ArmVerdict</c> AFTER
    /// <c>ArmRenderPass</c>, because the renderer it photographs and the renderers it blinks are the
    /// roster those arms already resolved — this class walks a subtree exactly once, for the census,
    /// and never again.
    ///
    /// <para>Its window is WALL CLOCK and much longer than the verdict window on purpose: the hold
    /// lasts a few seconds and the user reports the flash on props standing on the board too, so the
    /// observation deliberately outlives the hold and keeps photographing the same renderers after
    /// the prop has been put back.</para>
    /// </summary>
    private static void ArmPhotometer(GameObject go, string label)
    {
        if (_phArmed || _phBudgetLeft <= 0)
            return;
        _phBudgetLeft--;
        _phLabel = label;
        _phHaveSnapshot = true;
        // EVERY COUNTER IS ZEROED HERE, ABOVE THE EARLY RETURNS. A stand-down path that emitted
        // while these still held the PREVIOUS window's numbers would print a stale reading as a
        // fresh one, which is a shape this project has already paid for.
        _phGen++;
        _phT0 = Time.unscaledTime;
        _phFrames = 0;
        _phCaptureFrame = _phPassFrame = -1;
        _phResolveBlits = _phCopies = _phReads = _phReadErrors = _phOffTarget = 0;
        _phHeadPasses = _phFaults = 0;
        _phFirstFault = string.Empty;
        _phResolveInPass = false;
        _phHavePropRead = _phHaveCtlRead = false;
        _phSeenAnyHash = false;
        _phPropHashChanges = _phCtlHashChanges = 0;
        _phCount = _phOverflow = 0;
        _phPeakLin = -1f;
        _phPeakR = _phPeakG = _phPeakB = 0f;
        _phBlinksLeft = PhBlinkBudget;
        _phBlinkLive = false;
        _phBlinkKind = PhMarkNone;
        _phBaseBlinkDone = _phWhiteBlinkDone = false;
        _phBlinkWrites = _phBlinkRestores = _phBlinkForeign = 0;
        _phBaseBefore = _phBaseDuring = _phBaseCtl = -1f;
        _phWhiteBefore = _phWhiteDuring = _phWhiteCtl = -1f;
        _phBaseSamples = _phWhiteSamples = 0;
        PhBlinkRends.Clear();
        PhBlinkWas.Clear();
        PhRoster.Clear();

        // The census FIRST and unconditionally: it is static data, it needs no window and no white,
        // and it must still be taken on a machine where the capture path turns out to be dead.
        CensusPropAnimators(go, _twinRoot, out _phSmbReport, out _phTypeReport, out _phMatReport);

        if (_phDead || !PhSupported())
        {
            EmitPhotometer("the capture path is unavailable on this machine");
            return;
        }

        Renderer? lead = null;
        for (int i = 0; i < _vRenderers.Length && lead == null; i++)
        {
            Renderer r = _vRenderers[i];
            if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterial != null)
                lead = r;
        }
        if (lead == null)
        {
            EmitPhotometer("no drawing renderer was resolved to photograph");
            return;
        }

        _phArmed = true;
        _phLead = lead;
        // The clock starts HERE and not above the census: the window is measured in wall
        // clock and the census walks a subtree once, which must not come out of it.
        _phT0 = Time.unscaledTime;
        for (int i = 0; i < _vRenderers.Length; i++)
        {
            Renderer r = _vRenderers[i];
            if (r != null)
                PhRoster.Add(r);
        }

        PhBind(Rig.VRRigDriver.HeadCamera);
    }

    /// <summary>Capability, once. A platform without async readback or without a basic
    /// <c>CopyTexture</c> cannot run this path at all, and saying which is a reading: "the probe was
    /// silent" and "the probe could not exist here" are different findings.</summary>
    private static bool PhSupported()
    {
        if (_phSupportChecked)
            return _phSupported;
        _phSupportChecked = true;
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            _phUnsupported = "SystemInfo.supportsAsyncGPUReadback is false on "
                             + SystemInfo.graphicsDeviceType;
        }
        else if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
        {
            _phUnsupported = "SystemInfo.copyTextureSupport has no Basic bit ("
                             + SystemInfo.copyTextureSupport + ") on "
                             + SystemInfo.graphicsDeviceType;
        }
        _phSupported = _phUnsupported.Length == 0;
        return _phSupported;
    }

    private static void PhBind(Camera? head)
    {
        if (_phBound != null && _phCb != null)
            _phBound.RemoveCommandBuffer(CameraEvent.AfterEverything, _phCb);
        _phBound = null;
        if (head == null || !PhEnsureTargets() || _phCb == null)
            return;
        _phBound = head;
        head.AddCommandBuffer(CameraEvent.AfterEverything, _phCb);
        if (!_phHooked)
        {
            Camera.onPreRender += OnPhotometerPreRender;
            _phHooked = true;
        }
    }

    private static bool PhEnsureTargets()
    {
        _phCb ??= new CommandBuffer { name = "GloomhavenVR.PropFlashPhotometer" };
        if (_phLinTable == null)
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++)
                table[i] = Mathf.Pow(i / 255f, 2.2f);
            _phLinTable = table;
        }
        if (_phPatchRt != null)
            return true;
        _phPatchRt = new RenderTexture(PhPatch, PhPatch, 0, RenderTextureFormat.ARGB32)
        {
            name = "GloomhavenVR.PropFlashPhotometer.Patch",
            antiAliasing = 1,
            filterMode = FilterMode.Point,
            useMipMap = false,
            autoGenerateMips = false,
        };
        if (_phPatchRt.Create())
            return true;
        _phPatchRt.Release();
        Object.Destroy(_phPatchRt);
        _phPatchRt = null;
        _phDead = true;
        _phUnsupported = "the " + PhPatch + "x" + PhPatch + " capture target could not be created";
        return false;
    }

    /// <summary>Unhook, hand every blinked renderer back and drop the references. Idempotent, and it
    /// runs BEFORE the line is built — a probe that has answered is spent, and one that keeps a
    /// command buffer on the head camera afterwards is a shape this project has paid for. The
    /// generation bump makes a readback still in flight arrive into nothing rather than be folded
    /// into the next window's numbers.</summary>
    private static void DisarmPhotometer()
    {
        PhEndBlink();
        if (_phHooked)
        {
            Camera.onPreRender -= OnPhotometerPreRender;
            _phHooked = false;
        }
        if (_phBound != null && _phCb != null)
            _phBound.RemoveCommandBuffer(CameraEvent.AfterEverything, _phCb);
        _phCb?.Clear();
        _phBound = null;
        _phArmed = false;
        _phLead = null;
        _phGen++;
    }

    // ---- the tick -------------------------------------------------------------------------------

    /// <summary>
    /// One frame of the photometer, from <c>PropGrab.Tick</c> ABOVE the feature gate and with no hold
    /// required — the window deliberately outlives the hold. A couple of compares when nothing is
    /// armed.
    ///
    /// <para>THE BLINK IS DRIVEN FROM HERE AND NOT FROM THE RENDER CALLBACK, and that is the
    /// measurement rather than a detail: a visibility write inside a render pass lands in ONE EYE
    /// (this project's <c>AssertNotInRenderPhase</c> rule), which would make the two eyes disagree
    /// and turn the bisection into the stereo-rivalry defect this project has already chased twice. A
    /// write in Update lands in both eyes of the SAME frame the capture reads.</para>
    /// </summary>
    internal static void TickPhotometer()
    {
        if (!_phArmed)
            return;
        if (_phLead == null)
        {
            EmitPhotometer("the photographed renderer was destroyed mid-observation");
            return;
        }
        _phFrames++;

        // The head camera is rebuilt on a rig-kind change; follow it rather than photograph a camera
        // that is no longer drawing the eye.
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (!ReferenceEquals(head, _phBound))
            PhBind(head);

        if (_phBlinkLive && Time.frameCount >= _phBlinkEndFrame)
            PhEndBlink();

        if (Time.unscaledTime - _phT0 >= PhSeconds)
            EmitPhotometer("the photometric window ran to its full length");
    }

    // ---- capture, inside the render loop ---------------------------------------------------------

    /// <summary>
    /// Re-record the command buffer for THIS pass. In MultiPass <c>AfterEverything</c> fires once per
    /// eye and this runs before it in the same pass, so the buffer always describes the pass it is
    /// about to execute in. Only the FIRST head pass of a frame captures: a photometer needs one eye,
    /// and taking both would double the cost for the same picture.
    /// </summary>
    private static void OnPhotometerPreRender(Camera cam)
    {
        if (!_phArmed || _phDead || cam == null || !ReferenceEquals(cam, _phBound) || _phCb == null)
            return;
        try
        {
            _phCb.Clear(); // a non-capturing pass costs one empty buffer, never a stale capture
            _phResolveInPass = false;

            bool firstPassOfFrame = Time.frameCount != _phPassFrame;
            _phPassFrame = Time.frameCount;
            _phHeadPasses++;
            if (!firstPassOfFrame)
                return;

            // While a blink is live EVERY frame is captured: the blink is six frames long and a
            // cadence that skipped them would leave the perturbation unmeasured — which is exactly
            // how an A/B produces a null result that means nothing.
            bool due = _phBlinkLive
                       || _phCaptureFrame < 0
                       || Time.frameCount - _phCaptureFrame >= PhCadence;
            if (!due)
                return;

            if (!PhTryPatchOrigin(cam, out int px, out int py, out int targetW, out int targetH))
            {
                _phOffTarget++;
                return;
            }
            int ctlX = px + PhControlOffsetPx;
            if (ctlX + PhPatch > targetW)
                ctlX = px - PhControlOffsetPx;
            if (ctlX < 0 || ctlX + PhPatch > targetW)
            {
                _phOffTarget++;
                return;
            }

            if (!PhEnsureResolve(targetW, targetH))
                return;

            _phCaptureFrame = Time.frameCount;
            PhCopyAndRead(px, py, 0);
            PhCopyAndRead(ctlX, py, 1);
        }
        catch (System.Exception ex)
        {
            _phFaults++;
            if (_phFirstFault.Length == 0)
                _phFirstFault = ex.GetType().Name + ": " + ex.Message;
            _phDead = true;
            // Clear() discards every command recorded this pass INCLUDING the GetTemporaryRT, so
            // there is nothing to release and nothing leaks.
            _phCb.Clear();
            _phResolveInPass = false;
            return;
        }
        finally
        {
            if (_phResolveInPass && _phCb != null)
            {
                // Rebind a mod-owned target before releasing the temporary, so the surface handed
                // back to the pool is not the one still bound. CameraTarget is deliberately NOT used:
                // in MultiPass it is not provably the same surface this eye pass rendered into, and
                // this project has paid once for assuming a builtin identifier means what it looks
                // like it means.
                if (_phPatchRt != null)
                    _phCb.SetRenderTarget(_phPatchRt);
                _phCb.ReleaseTemporaryRT(PhResolveId);
                _phResolveInPass = false;
            }
        }
    }

    /// <summary>
    /// The ONE read of <see cref="BuiltinRenderTextureType.CurrentActive"/> per pass, recorded as the
    /// first command, into a full-eye single-sampled temporary. Its only job is to resolve the
    /// multisampled eye buffer into something a region copy can address; it is a straight 1:1
    /// full-frame copy with no scale, no offset and no material — the most exercised path in the
    /// engine. Single-sampled ARGB32 with the project default read/write so the format matches the
    /// patch target exactly: <c>CopyTexture</c> refuses a copy between differing formats.
    /// </summary>
    private static bool PhEnsureResolve(int w, int h)
    {
        if (_phResolveInPass)
            return true;
        if (_phCb == null)
            return false;
        _phCb.GetTemporaryRT(PhResolveId, w, h, 0, FilterMode.Point, RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default, 1);
        _phCb.Blit(BuiltinRenderTextureType.CurrentActive, PhResolveId);
        _phResolveInPass = true;
        _phResolveBlits++;
        return true;
    }

    /// <summary>Copy one patch out of the resolve and enqueue its readback at the same position in
    /// the command stream, so readback N carries exactly the pixels copy N produced and one shared
    /// target serves both patches. The SOURCE is the mod-named temporary and never a builtin
    /// identifier, so no earlier command can have changed what it means — and <c>CopyTexture</c> does
    /// not rebind the render target, so no later command is affected either. That pair of properties
    /// is the whole of the ModBuild 191 fix.</summary>
    private static void PhCopyAndRead(int x0, int y0, int slot)
    {
        if (_phCb == null || _phPatchRt == null)
            return;
        _phCb.CopyTexture(PhResolveId, 0, 0, x0, y0, PhPatch, PhPatch, _phPatchRt, 0, 0, 0, 0);
        PhPendingSlot.Enqueue(slot);
        PhPendingGen.Enqueue(_phGen);
        _phCopies++;
        _phCb.RequestAsyncReadback(_phPatchRt, 0, TextureFormat.RGBA32, OnPhotometerRead);
    }

    /// <summary>
    /// The eye-texture PIXEL origin of the prop patch. Integer coordinates, because a region copy is
    /// a texel-for-texel move that cannot resample — the cost is up to a pixel of misalignment
    /// against the world point, which is nothing against a 32 px window on a ~288 px lit region.
    /// Returns false rather than clamping when the window would leave the target: a clamped window
    /// silently photographs somewhere else and the series would then mix two subjects.
    /// </summary>
    private static bool PhTryPatchOrigin(Camera cam, out int x0, out int y0,
        out int targetW, out int targetH)
    {
        x0 = y0 = targetW = targetH = 0;
        Renderer? lead = _phLead;
        if (lead == null)
            return false;

        float viewportScale = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        int w = XRSettings.eyeTextureWidth;
        int h = XRSettings.eyeTextureHeight;
        if (w < 2 || h < 2)
        {
            w = Screen.width;
            h = Screen.height;
            viewportScale = 1f;
        }
        if (w < PhPatch * 2 || h < PhPatch * 2)
            return false;
        targetW = w;
        targetH = h;

        // The prop's own DRAWN centre and not its transform origin: a bounds centre is where the
        // pixels are, and a pivot on this game's props frequently is not.
        Vector3 vp = cam.WorldToViewportPoint(lead.bounds.center);
        if (vp.z <= 0f)
            return false;
        float half = PhPatch * 0.5f;
        int ix = Mathf.RoundToInt((vp.x * viewportScale * w) - half);
        int iy = Mathf.RoundToInt((vp.y * viewportScale * h) - half);
        if (ix < 0 || iy < 0 || ix + PhPatch > w || iy + PhPatch > h)
            return false;
        x0 = ix;
        y0 = iy;
        return true;
    }

    // ---- readback, main thread, FIFO-paired ------------------------------------------------------

    private static void OnPhotometerRead(AsyncGPUReadbackRequest req)
    {
        if (PhPendingSlot.Count == 0 || PhPendingGen.Count == 0)
            return;
        int slot = PhPendingSlot.Dequeue();
        int gen = PhPendingGen.Dequeue();
        if (gen != _phGen)
            return; // from a previous arming — discarded, never folded into these numbers
        if (req.hasError)
        {
            _phReadErrors++;
            return;
        }
        Unity.Collections.NativeArray<Color32> data = req.GetData<Color32>();
        int n = PhPatch * PhPatch;
        if (data.Length < n)
        {
            _phReadErrors++;
            return;
        }
        float[] lin = _phLinTable ?? System.Array.Empty<float>();
        if (lin.Length < 256)
            return;
        _phReads++;

        double sr = 0.0, sg = 0.0, sb = 0.0;
        int white = 0;
        uint hash = 2166136261u;
        for (int i = 0; i < n; i++)
        {
            Color32 c = data[i];
            sr += lin[c.r];
            sg += lin[c.g];
            sb += lin[c.b];
            if (c.r >= PhNearWhite && c.g >= PhNearWhite && c.b >= PhNearWhite)
                white++;
            hash = (hash ^ c.r) * 16777619u;
            hash = (hash ^ c.g) * 16777619u;
            hash = (hash ^ c.b) * 16777619u;
        }
        float inv = 1f / n;
        var mean = (float)((sr + sg + sb) / (3.0 * n));

        if (slot == 0)
        {
            if (_phSeenAnyHash && hash != _phPropHash)
                _phPropHashChanges++;
            _phPropHash = hash;
            _phPropLin = mean;
            _phPropWhite = white * inv;
            _phPropR = (float)(sr * inv);
            _phPropG = (float)(sg * inv);
            _phPropB = (float)(sb * inv);
            _phHavePropRead = true;
        }
        else
        {
            if (_phSeenAnyHash && hash != _phCtlHash)
                _phCtlHashChanges++;
            _phCtlHash = hash;
            _phCtlLin = mean;
            _phHaveCtlRead = true;
        }

        if (_phHavePropRead && _phHaveCtlRead)
        {
            _phSeenAnyHash = true;
            StorePhotometerSample();
            _phHavePropRead = _phHaveCtlRead = false;
        }
    }

    /// <summary>Both halves of one capture have landed. Store the row and let the trigger look at it.
    /// A row is only ever stored complete: a series with a missing column cannot be differenced, and
    /// the difference is the reading.</summary>
    private static void StorePhotometerSample()
    {
        if (!_phArmed)
            return;
        if (_phCount < PhSampleCap)
        {
            int k = _phCount++;
            PhT[k] = Time.unscaledTime - _phT0;
            PhProp[k] = _phPropLin;
            PhCtl[k] = _phCtlLin;
            PhWhiteFrac[k] = _phPropWhite;
            PhMark[k] = _phBlinkLive ? _phBlinkKind : PhMarkNone;
        }
        else
        {
            _phOverflow++;
        }

        if (_phPropLin > _phPeakLin)
        {
            _phPeakLin = _phPropLin;
            _phPeakR = _phPropR;
            _phPeakG = _phPropG;
            _phPeakB = _phPropB;
        }

        if (_phBlinkLive)
        {
            if (_phBlinkKind == PhMarkBaseBlink)
            {
                _phBaseDuring = _phBaseSamples == 0 ? _phPropLin : Mathf.Min(_phBaseDuring, _phPropLin);
                _phBaseCtl = _phCtlLin;
                _phBaseSamples++;
            }
            else if (_phBlinkKind == PhMarkWhiteBlink)
            {
                _phWhiteDuring = _phWhiteSamples == 0 ? _phPropLin : Mathf.Min(_phWhiteDuring, _phPropLin);
                _phWhiteCtl = _phCtlLin;
                _phWhiteSamples++;
            }
            return;
        }

        PhConsiderBlink();
    }

    /// <summary>
    /// Decide whether this sample fires a blink.
    ///
    /// <para>THE BASELINE BLINK IS THE NULL-PERTURBATION CONTROL and it fires FIRST, once enough
    /// samples exist to know where baseline is. Without it, a white blink that changed nothing would
    /// be indistinguishable from a blink that DID nothing — precisely the failure mode ModBuild 467
    /// needed four separate clauses to rule out.</para>
    ///
    /// <para>THE WHITE BLINK arms only after the observed range is wide enough to be a flash rather
    /// than noise, so the trigger is calibrated on an episode the photometer has actually seen
    /// instead of on a number chosen in advance.</para>
    /// </summary>
    private static void PhConsiderBlink()
    {
        if (_phBlinksLeft <= 0 || _phCount < PhCalibrateSamples)
            return;

        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < _phCount; i++)
        {
            if (PhMark[i] != PhMarkNone)
                continue;
            if (PhProp[i] < lo) lo = PhProp[i];
            if (PhProp[i] > hi) hi = PhProp[i];
        }
        if (lo > hi)
            return;
        float range = hi - lo;

        if (!_phBaseBlinkDone && _phPropLin <= lo + (0.15f * Mathf.Max(range, 0.02f)))
        {
            _phBaseBlinkDone = true;
            _phBaseBefore = _phPropLin;
            PhBeginBlink(PhMarkBaseBlink);
            return;
        }
        if (_phBaseBlinkDone && !_phWhiteBlinkDone && range >= PhMinExcursion
            && _phPropLin >= lo + (PhTriggerFraction * range))
        {
            _phWhiteBlinkDone = true;
            _phWhiteBefore = _phPropLin;
            PhBeginBlink(PhMarkWhiteBlink);
        }
    }

    /// <summary>Turn the prop's own renderers off for <see cref="PhBlinkFrames"/> frames, remembering
    /// the value each one carried. <c>forceRenderingOff</c> and not <c>enabled</c>: <c>enabled</c> is
    /// a field several systems in this game and this mod write every frame — a write war this project
    /// has already lost once — while <c>forceRenderingOff</c> has exactly one other writer in the
    /// tree (<c>Net/PeerBoardFade.cs</c>), which cannot be running on a prop in this client's own
    /// hand.</summary>
    private static void PhBeginBlink(byte kind)
    {
        PhBlinkRends.Clear();
        PhBlinkWas.Clear();
        for (int i = 0; i < PhRoster.Count; i++)
        {
            Renderer r = PhRoster[i];
            if (r == null)
                continue;
            PhBlinkRends.Add(r);
            PhBlinkWas.Add(r.forceRenderingOff);
            r.forceRenderingOff = true;
            _phBlinkWrites++;
        }
        if (PhBlinkRends.Count == 0)
            return;
        _phBlinksLeft--;
        _phBlinkLive = true;
        _phBlinkKind = kind;
        _phBlinkEndFrame = Time.frameCount + PhBlinkFrames;
    }

    /// <summary>Give every value back, object for object, and count anything that was NOT still at the
    /// value this class left — a foreign write during the blink would mean the restore is putting a
    /// stale value over somebody else's, which this project has a recorded incident about. Idempotent
    /// and safe to call at any time, which is why the window close, the belt reset and the release
    /// path all call it unconditionally.</summary>
    private static void PhEndBlink()
    {
        for (int i = 0; i < PhBlinkRends.Count && i < PhBlinkWas.Count; i++)
        {
            Renderer r = PhBlinkRends[i];
            if (r == null)
                continue;
            if (!r.forceRenderingOff)
                _phBlinkForeign++;
            r.forceRenderingOff = PhBlinkWas[i];
            _phBlinkRestores++;
        }
        PhBlinkRends.Clear();
        PhBlinkWas.Clear();
        _phBlinkLive = false;
        _phBlinkKind = PhMarkNone;
    }

    // ---- the animator-behaviour census ------------------------------------------------------------

    /// <summary>
    /// Read the game's own flash machinery off the live object. STATIC DATA, so unlike every other
    /// reading in this file it does not need the white to happen while it looks.
    ///
    /// <para>It answers three questions that seventeen rounds could not: (1) is
    /// <c>SpawnObjectAnimateMaterial_SMB</c> — whose <c>OnStateUpdate</c> is a curve-driven
    /// <c>SetFloat</c> into every child renderer's <c>.material</c>, and whose <c>OnStateExit</c>
    /// zeroes its own clock — attached to this prop's controller at all; (2) if it is, WHAT PROPERTY
    /// it writes and over WHAT DURATION, both of which are serialized fields that live in the
    /// AssetBundle and cannot be read out of <c>decompiled/</c>; and (3) has anything taken the
    /// <c>Renderer.material</c> path on these renderers, which Unity records permanently by renaming
    /// the instantiated clone <c>&lt;name&gt; (Instance)</c>.</para>
    ///
    /// <para>IT ALSO NAMES EVERY COMPONENT TYPE UNDER THE PROP, not only the ones it expects. A
    /// collector typed on what we already suspect cannot see a shape nobody has named yet — this
    /// project has a recorded incident of exactly that — so the type list is enumerated and its
    /// population size is printed beside it.</para>
    /// </summary>
    private static void CensusPropAnimators(GameObject go, GameObject? twin, out string smbReport,
        out string typeReport, out string matReport)
    {
        var smb = new System.Text.StringBuilder(512);
        var types = new System.Text.StringBuilder(512);
        var mats = new System.Text.StringBuilder(512);

        var animators = new List<Animator>(4);
        go.GetComponentsInChildren(includeInactive: true, animators);
        int heldAnimators = animators.Count;
        // THE HOME TWIN IS WALKED TOO, AND IT IS THE HALF THAT CAN ANSWER. See the ordering note in
        // the summary below: by the time this runs the held prop's animators are already switched
        // off by our own hush, and a disabled Animator's controller instance is gone — this file
        // already records that one reporting layerCount 0. The twin is a prop of the same kind
        // standing on its own hex that this class never touches, so its controller is live and its
        // GetBehaviours answer is a reading rather than a non-reading. _twinRoot is the ANIMATOR'S
        // OWN GameObject (FindHomeTwin, PropAnimBelt.cs:1381), which is also exactly the transform
        // SpawnObjectAnimateMaterial_SMB.OnStateEnter parents its particles prefab to.
        if (twin != null && !ReferenceEquals(twin, go))
        {
            var twinAnimators = new List<Animator>(4);
            twin.GetComponentsInChildren(includeInactive: true, twinAnimators);
            animators.AddRange(twinAnimators);
        }
        int found = 0, behaviours = 0, offAtCensus = 0, foundOnLive = 0;
        for (int a = 0; a < animators.Count; a++)
        {
            Animator anim = animators[a];
            if (anim == null || anim.runtimeAnimatorController == null)
                continue;
            bool live = anim.enabled && anim.layerCount > 0;
            if (!live)
                offAtCensus++;
            StateMachineBehaviour[] all;
            try
            {
                all = anim.GetBehaviours<StateMachineBehaviour>();
            }
            catch (System.Exception ex)
            {
                smb.Append("[animator '").Append(anim.gameObject.name)
                   .Append("' refused GetBehaviours: ").Append(ex.GetType().Name).Append("] ");
                continue;
            }
            behaviours += all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                StateMachineBehaviour b = all[i];
                if (b == null)
                    continue;
                if (b is SpawnObjectAnimateMaterial_SMB flash)
                {
                    found++;
                    if (live)
                        foundOnLive++;
                    AnimationCurve? curve = flash.myCurve;
                    int keys = curve != null ? curve.length : -1;
                    float last = curve != null && curve.length > 0
                        ? curve.Evaluate(1f)
                        : float.NaN;
                    smb.Append("*** SpawnObjectAnimateMaterial_SMB FOUND on animator '")
                       .Append(anim.gameObject.name)
                       .Append(live ? "' (LIVE, a real reading)" : "' (DISABLED at census)")
                       .Append(": animProperty '")
                       .Append(string.IsNullOrEmpty(flash.animProperty)
                           ? "<empty>"
                           : flash.animProperty)
                       .Append("', animTime ").Append(flash.animTime.ToString("0.###"))
                       .Append(" s, animStrength ").Append(flash.animStrength.ToString("0.###"))
                       .Append(", curve ").Append(keys).Append(" key(s), value at t=1 ")
                       .Append(float.IsNaN(last) ? "<no curve>" : last.ToString("0.###"))
                       .Append(", particles ")
                       .Append(flash.particles != null ? flash.particles.name : "<none>")
                       .Append(", lifetime ").Append(flash.lifetime.ToString("0.##")).Append(". ");
                }
                else
                {
                    smb.Append(b.GetType().Name).Append(live ? " " : "(off) ");
                }
            }
        }

        smbReport = "ANIMATOR BEHAVIOUR CENSUS: " + animators.Count + " animator(s) ("
                    + heldAnimators + " under the HELD prop, the rest under the untouched HOME TWIN) "
                    + "carrying " + behaviours + " StateMachineBehaviour(s) in total, of which "
                    + found + " are SpawnObjectAnimateMaterial_SMB (" + foundOnLive
                    + " of those on a LIVE animator). " + offAtCensus
                    + " animator(s) were ALREADY DISABLED when this census ran. "
                    + "*** READ A ZERO ON A DISABLED ANIMATOR AS A NON-READING, NOT AN EXCLUSION: "
                    + "this class arms from ArmVerdict, which Engage calls AFTER Apply, and Apply's "
                    + "RewindAndStop has already set Animator.enabled = false on every animator "
                    + "under the held prop (PropAnimBelt.cs:728). A disabled Animator's controller "
                    + "instance is gone — this file already records one reporting layerCount 0 — so "
                    + "GetBehaviours on it can answer empty whatever the controller carries. THE "
                    + "HOME TWIN IS THE HALF THAT DECIDES, because nothing in this mod touches it. "
                    + "*** " + (smb.Length == 0 ? "<none named>" : smb.ToString());

        var comps = new List<Component>(64);
        go.GetComponentsInChildren(includeInactive: true, comps);
        int named = 0;
        var seen = new List<string>(PhTypeCap);
        for (int i = 0; i < comps.Count; i++)
        {
            Component c = comps[i];
            if (c == null)
                continue;
            string n = c.GetType().Name;
            if (seen.Contains(n))
                continue;
            if (seen.Count < PhTypeCap)
            {
                seen.Add(n);
                types.Append(n).Append(' ');
                named++;
            }
        }
        typeReport = "EVERY COMPONENT TYPE UNDER THE PROP (" + comps.Count
                        + " component(s), " + named + " DISTINCT type(s) named, cap " + PhTypeCap
                        + " — a truncated list is not an absence): " + types;

        var rends = new List<Renderer>(8);
        go.GetComponentsInChildren(includeInactive: true, rends);
        int instanced = 0;
        for (int i = 0; i < rends.Count; i++)
        {
            Renderer r = rends[i];
            if (r == null)
                continue;
            Material? m = r.sharedMaterial;
            string mn = m != null ? m.name : "<null>";
            bool inst = mn.EndsWith("(Instance)", System.StringComparison.Ordinal);
            if (inst)
                instanced++;
            mats.Append('\'').Append(r.gameObject.name).Append("' -> '").Append(mn).Append("'")
                .Append(inst ? " INSTANCED" : "").Append("; ");
        }
        matReport = "MATERIAL INSTANCE FINGERPRINT — Renderer.material CLONES on first touch and "
                       + "Unity renames the clone '<name> (Instance)', so this is positive evidence "
                       + "that something took the .material path (which is what "
                       + "SpawnObjectAnimateMaterial_SMB does) and its absence is positive evidence "
                       + "that nothing has: " + instanced + " of " + rends.Count
                       + " renderer(s) carry an instanced material. " + mats;
    }

    // ---- the line -------------------------------------------------------------------------------

    /// <summary>
    /// Close the observation and print it. Emitted ONCE per window, whatever the outcome — a window
    /// that armed and found nothing must be distinguishable from a window that never armed, and this
    /// file has confused those before. Everything is disarmed BEFORE the line is built: a probe that
    /// has answered is spent.
    /// </summary>
    private static void EmitPhotometer(string why)
    {
        if (!_phArmed && !_phHaveSnapshot)
            return; // nothing ever armed here: silence, not an empty observation
        _phHaveSnapshot = false;
        bool wasArmed = _phArmed;
        int samples = _phCount;
        float seconds = wasArmed ? Time.unscaledTime - _phT0 : 0f;
        DisarmPhotometer();
        PhRoster.Clear();

        var sb = new System.Text.StringBuilder(4096);
        sb.Append("[Props] HELD-PROP FLASH PHOTOMETER for ")
          .Append(_phLabel.Length == 0 ? "<unnamed prop>" : _phLabel)
          .Append(" — closed because ").Append(why).Append(". ");

        // ---- the census. Static data, so it is printed whatever the capture did. -----------------
        sb.Append("WHY THIS LINE EXISTS: every reading in this file so far has the form 'over N "
                  + "frames NOTHING CHANGED', and every one of them is conditional on a premise no "
                  + "instrument here has ever measured — that the white was present while it looked. "
                  + "The ModBuild 465 line says so about itself in the log ('a 0 here on a window "
                  + "that contained NO flash proves nothing either'). This line measures the "
                  + "PICTURE, so a state reading taken in the same window becomes an EXCLUSION "
                  + "instead of an ABSENCE; and it reads the game's own flash machinery off the live "
                  + "object, which needs no window at all. ")
          .Append(_phSmbReport).Append(' ')
          .Append("READ THE CENSUS LIKE THIS: SpawnObjectAnimateMaterial_SMB "
                  + "(decompiled/GH.Runtime/SpawnObjectAnimateMaterial_SMB.cs) ramps "
                  + "myCurve.Evaluate(t) * animStrength into animProperty on EVERY child renderer's "
                  + ".material over animTime seconds, holds it on the curve's clamp, and zeroes t in "
                  + "OnStateExit — a ramp, a plateau, then a complete clear inside one frame, on the "
                  + "whole prop, neutral because it is a scalar, and on every prop of the kind at "
                  + "once because their animators all start at scenario load. That is every term of "
                  + "the measured signature and NOTHING else in seventeen rounds has fitted the ramp. "
                  + "A COUNT OF 0 EXCLUDES IT ON THIS PROP from the game's own data rather than from "
                  + "another window of zeroes; a count above 0 NAMES THE PROPERTY a fix must write, "
                  + "which no amount of reading decompiled/ could give because animProperty is a "
                  + "serialized string that lives in the AssetBundle. ")
          .Append("AND THE ONE THING OUR OWN HUSH CAN DO TO IT, WHICH THE SHIPPED REMEDY DOES "
                  + "NOT COVER: OnStateExit is the ONLY thing that resets t, OnStateUpdate is "
                  + "the ONLY thing that writes the float, and Unity does not run OnStateExit "
                  + "when an Animator is DISABLED. RewindAndStop (PropAnimBelt.cs:728) disables "
                  + "every animator under the held prop, so a hush landing mid-ramp freezes t "
                  + "and leaves the last written value on the material for the whole hold. The "
                  + "round-seven remedy on the line above it, Animator.WriteDefaultValues, "
                  + "CANNOT undo that: it writes the defaults of properties BOUND TO THE "
                  + "ANIMATOR by clip curves, and this float is written imperatively through "
                  + "Renderer.material by code the animator does not bind. So the rewind covers "
                  + "a clip-animated latch and leaves an SMB-written one standing. THAT IS A "
                  + "SECOND MECHANISM AND NOT A REPLACEMENT FOR THE FIRST: the hush touches "
                  + "only the held prop's own subtree, so it can explain PERSISTENCE IN THE "
                  + "HAND and can never explain a board-wide event on every trap and chest. THE "
                  + "FINGERPRINT BELOW IS ITS FALSIFIER — no (Instance) means OnStateUpdate "
                  + "never wrote on that renderer, and with no write there is nothing to "
                  + "latch. ")
          .Append(_phMatReport).Append(' ')
          .Append(_phTypeReport).Append(' ');

        // ---- the picture -------------------------------------------------------------------------
        sb.Append("THE PICTURE: ").Append(samples).Append(" complete sample(s) over ")
          .Append(seconds.ToString("0.0")).Append(" s and ").Append(_phFrames)
          .Append(" tick(s); head-camera passes ").Append(_phHeadPasses)
          .Append(", resolve blits ").Append(_phResolveBlits)
          .Append(", region copies ").Append(_phCopies)
          .Append(", readbacks landed ").Append(_phReads)
          .Append(", readback errors ").Append(_phReadErrors)
          .Append(", captures skipped because the window left the eye target ").Append(_phOffTarget)
          .Append(", render-callback faults ").Append(_phFaults)
          .Append(_phFaults > 0 ? " (first: " + _phFirstFault + ")" : string.Empty)
          .Append(", overflow past the ").Append(PhSampleCap).Append("-sample cap ")
          .Append(_phOverflow).Append(". PATCH CONTENT CHANGED between consecutive captures on ")
          .Append(_phPropHashChanges).Append(" prop reading(s) and ").Append(_phCtlHashChanges)
          .Append(" control reading(s) — THIS IS THE DEAD-CAPTURE FALSIFIER: ModBuild 191 read "
                  + "BIT-IDENTICAL pixels for a whole build because CommandBuffer.Blit rebinds the "
                  + "active render target and its second blit was reading its own destination. Two "
                  + "zeroes here mean the capture is dead and NOTHING below is a reading. ");

        float lo = float.MaxValue, hi = float.MinValue, ctlLo = float.MaxValue, ctlHi = float.MinValue;
        float whiteHi = 0f;
        for (int i = 0; i < samples; i++)
        {
            if (PhMark[i] != PhMarkNone)
                continue;
            if (PhProp[i] < lo) lo = PhProp[i];
            if (PhProp[i] > hi) hi = PhProp[i];
            if (PhCtl[i] < ctlLo) ctlLo = PhCtl[i];
            if (PhCtl[i] > ctlHi) ctlHi = PhCtl[i];
            if (PhWhiteFrac[i] > whiteHi) whiteHi = PhWhiteFrac[i];
        }
        bool haveRange = lo <= hi;
        float range = haveRange ? hi - lo : 0f;

        if (!wasArmed || samples == 0 || _phReads == 0)
        {
            sb.Append("*** INERT — ");
            if (_phUnsupported.Length > 0)
                sb.Append(_phUnsupported).Append(". ");
            else if (!wasArmed)
                sb.Append("the window never armed. ");
            else
                sb.Append("no readback ever landed. ");
            sb.Append("NOTHING IS MEASURED EITHER WAY: no state reading anywhere in this file is "
                      + "qualified by this window, and the next round's first job is this capture "
                      + "path and not another candidate. *** ");
        }
        else if (_phPropHashChanges == 0 && _phCtlHashChanges == 0)
        {
            sb.Append("*** INERT — every capture came back BIT-IDENTICAL to the last, which is the "
                      + "ModBuild 191 dead-capture signature and not a still picture: a real eye "
                      + "frame with a head-tracked camera cannot repeat exactly. The capture path is "
                      + "the finding; nothing below is a reading. *** ");
        }
        else
        {
            sb.Append("PROP PATCH linear luminance ").Append(lo.ToString("0.0000")).Append("..")
              .Append(hi.ToString("0.0000")).Append(" (range ").Append(range.ToString("0.0000"))
              .Append("), CONTROL PATCH ").Append(ctlLo.ToString("0.0000")).Append("..")
              .Append(ctlHi.ToString("0.0000")).Append(", peak NEAR-WHITE pixel fraction ")
              .Append((whiteHi * 100f).ToString("0.0")).Append(" %. ");

            if (range < PhMinExcursion)
            {
                sb.Append("*** NO WHITE IN THIS WINDOW — the control patch VARIED, so the capture is "
                          + "live, but the prop patch never moved by the ")
                  .Append(PhMinExcursion.ToString("0.00"))
                  .Append(" of linear light a flash needs (the video's ramp is 0.23 -> 0.93). READ "
                          + "EVERY OTHER PROBE'S ZERO IN THIS SESSION AS AN ABSENCE AND NOT AN "
                          + "EXCLUSION: seventeen rounds of clean state readings are unqualified "
                          + "rather than wrong, and the next round needs a longer window or a "
                          + "trigger, not another field. *** ");
            }
            else
            {
                float t10 = -1f, t90 = -1f, tFall = -1f, tPeak = -1f;
                float trig10 = lo + (0.1f * range), trig90 = lo + (0.9f * range);
                for (int i = 0; i < samples; i++)
                {
                    if (PhMark[i] != PhMarkNone)
                        continue;
                    if (t10 < 0f && PhProp[i] >= trig10) t10 = PhT[i];
                    if (t10 >= 0f && t90 < 0f && PhProp[i] >= trig90) t90 = PhT[i];
                    if (PhProp[i] >= hi - 1e-4f && tPeak < 0f) tPeak = PhT[i];
                    if (t90 >= 0f && tFall < 0f && PhT[i] > t90 && PhProp[i] <= trig10) tFall = PhT[i];
                }
                sb.Append("*** WHITE WITNESSED — the prop patch rose ")
                  .Append(range.ToString("0.0000")).Append(" of linear light. RISE (10 % -> 90 %) ")
                  .Append(t10 >= 0f && t90 >= 0f
                      ? (t90 - t10).ToString("0.00") + " s"
                      : "<not resolved>")
                  .Append(", FALL (90 % -> 10 %) ")
                  .Append(t90 >= 0f && tFall >= 0f
                      ? (tFall - t90).ToString("0.00") + " s"
                      : "<not resolved>")
                  .Append(". THE VIDEO SAYS ~1.0-1.4 s RISE AND A COMPLETE CLEAR INSIDE ONE 0.1 s "
                          + "STEP; if these two disagree the in-process reading is not photographing "
                          + "the same event and everything below is about something else. ADDED "
                          + "LIGHT AT PEAK, normalised to red (the video read 1.000/0.995/1.010, and "
                          + "this mod's own overlays are amber 1.000/0.342/0.056 and cool blue, "
                          + "which is what excludes them BY COLOUR): ")
                  .Append(_phPeakR > 1e-5f
                      ? "1.000/" + (_phPeakG / _phPeakR).ToString("0.000") + "/"
                        + (_phPeakB / _phPeakR).ToString("0.000")
                      : "<peak too dark to normalise>")
                  .Append(". EVERY STATE READING TAKEN IN THIS SESSION IS NOW AN EXCLUSION AND NOT "
                          + "AN ABSENCE. *** ");
            }
        }

        // ---- the bisection -----------------------------------------------------------------------
        sb.Append("THE BISECTION — IS THE WHITE DRAWN BY THE PROP'S OWN RENDERERS AT ALL? Seventeen "
                  + "rounds asked WHY those renderers might shade white; none asked whether they are "
                  + "the thing painting. Renderer.forceRenderingOff was set on the prop's own "
                  + "renderers for ").Append(PhBlinkFrames)
          .Append(" frame(s) and the same pixels re-read. WRITES ").Append(_phBlinkWrites)
          .Append(", RESTORES ").Append(_phBlinkRestores)
          .Append(", found already handed back by a FOREIGN writer ").Append(_phBlinkForeign)
          .Append(" (0 means the restore put back exactly what this class replaced). ");

        bool baseRan = _phBaseSamples > 0 && _phBaseBefore >= 0f;
        bool whiteRan = _phWhiteSamples > 0 && _phWhiteBefore >= 0f;
        float baseDrop = baseRan ? _phBaseBefore - _phBaseDuring : 0f;
        float whiteDrop = whiteRan ? _phWhiteBefore - _phWhiteDuring : 0f;

        sb.Append("BASELINE BLINK (the NULL-PERTURBATION CONTROL, and it is why the white blink can "
                  + "mean anything — ModBuild 467 needed four clauses to rule out a perturbation "
                  + "that never happened): ");
        if (!baseRan)
        {
            sb.Append("NEVER FIRED, so the blink machinery is UNVERIFIED and no verdict below stands. ");
        }
        else
        {
            sb.Append("before ").Append(_phBaseBefore.ToString("0.0000")).Append(", during ")
              .Append(_phBaseDuring.ToString("0.0000")).Append(" (drop ")
              .Append(baseDrop.ToString("0.0000")).Append(" over ").Append(_phBaseSamples)
              .Append(" sample(s)), control patch during the blink ")
              .Append(_phBaseCtl.ToString("0.0000")).Append(". ")
              .Append(baseDrop < 0.005f
                  ? "*** THE BLINK IS INERT — turning the prop's renderers off changed the patch by "
                    + "less than 0.005 of linear light. THREE CAUSES, AND THIS CLAUSE EXISTS TO TELL "
                    + "THEM APART NEXT ROUND: (a) THE PATCH IS NOT ON THE PROP, and the likeliest "
                    + "reason is the ONE thing on this capture path that cannot be settled without "
                    + "hardware — CopyTexture's source ORIGIN, which is bottom-left on OpenGL and "
                    + "top-left on D3D, so a Y flip puts the window at the mirrored height; the "
                    + "control patch is offset in X only, so a flip moves BOTH and neither would "
                    + "show the prop. (b) forceRenderingOff does not reach these renderers. (c) the "
                    + "prop was not drawn at that moment anyway. NO VERDICT BELOW STANDS. *** "
                  : "The blink WORKS: the prop's own renderers contribute this much to this patch "
                    + "normally, so a white blink can now be read. ");
        }

        sb.Append("WHITE BLINK: ");
        if (!whiteRan)
        {
            sb.Append("NEVER FIRED — the trigger needs a calibrated range (at least ")
              .Append(PhMinExcursion.ToString("0.00"))
              .Append(" of linear light) and a later sample crossing ")
              .Append((PhTriggerFraction * 100f).ToString("0")).Append(" % of it, so either no white "
                  + "occurred in this window or it occurred only once and the range was still being "
                  + "learned. THE BISECTION IS UNANSWERED, and that is an ABSENCE. ");
        }
        else
        {
            sb.Append("before ").Append(_phWhiteBefore.ToString("0.0000")).Append(", during ")
              .Append(_phWhiteDuring.ToString("0.0000")).Append(" (drop ")
              .Append(whiteDrop.ToString("0.0000")).Append(" over ").Append(_phWhiteSamples)
              .Append(" sample(s)), control patch during the blink ")
              .Append(_phWhiteCtl.ToString("0.0000")).Append(". ");
            if (!baseRan || baseDrop < 0.005f)
            {
                sb.Append("NO VERDICT: the baseline blink did not establish that the perturbation "
                          + "reaches the picture, and a null result from a perturbation that never "
                          + "happened is not an exclusion. ");
            }
            else if (whiteDrop >= (0.6f * (_phWhiteBefore - Mathf.Max(lo, 0f))))
            {
                sb.Append("*** THE PROP'S OWN RENDERERS WERE PAINTING THE WHITE. Turning them off "
                          + "took the patch back down by most of the excursion, so the bleach is in "
                          + "THIS PROP'S OWN SHADING and every account that puts the painter "
                          + "somewhere else — an image effect compositing after onPostRender, the "
                          + "OpenXR compositor, a foreign object at the prop's place — is DELETED. "
                          + "What survives is the shading input no probe has read: a "
                          + "MaterialPropertyBlock, a replacement shader, or the material property "
                          + "the census above names. *** ");
            }
            else
            {
                sb.Append("*** THE PROP'S OWN RENDERERS ARE NOT PAINTING THE WHITE. They were off "
                          + "and the patch stayed bright, while the SAME write at baseline moved it "
                          + "by ").Append(baseDrop.ToString("0.0000"))
                  .Append(" — so the perturbation reaches the picture and this null is real. THAT "
                          + "DELETES THE ENTIRE SUBJECT OF SEVENTEEN ROUNDS: this prop's materials, "
                          + "its shaders, its property blocks, its state and its shading inputs "
                          + "cannot be the painter, and the next round must look for what ELSE draws "
                          + "at the prop's screen position. *** ");
            }
        }

        sb.Append("WHAT THIS INSTRUMENT WROTE, IN FULL: Renderer.forceRenderingOff on the prop's own "
                  + "renderers, for ").Append(PhBlinkFrames)
          .Append(" frames at a time, at most ").Append(PhBlinkBudget)
          .Append(" times, handed back object for object and again unconditionally on close, on "
                  + "release and on reset. No game state, no transform, no material, no layer, no "
                  + "shader global, no animator field; nothing on the wire, and a flat or peer "
                  + "player saw exactly what they saw before. ")
          .Append(_phBudgetLeft).Append(" more photometric window(s) this session.");

        // HW-VERIFY: this is the only line in the session that says whether the white was in the
        // window, so every other probe's zero is uninterpretable without it. It must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error) — scripts/check-hw-verify.py enforces the
        // position of this marker directly above the call.
        VRLog.Note("FigureGrab", sb.ToString());
    }
}
