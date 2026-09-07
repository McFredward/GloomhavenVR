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
/// <para><b>ROUND NINETEEN — THE PHOTOMETER WITNESSED WHITE AND THEN FAILED ITS OWN COHERENCE
/// CHECK, AND FOUR OF ITS FIVE VERDICT CLAUSES WERE PRODUCED BY THEIR OWN BARS.</b> ModBuild 469
/// ran it and the line reported, about itself, that the in-process event and the video event
/// disagree: <c>RISE 0.19 s, FALL 0.23 s</c> against a video ramp of ~1.0-1.4 s and a clear inside
/// one 30 fps frame. Its own sentence says everything below such a disagreement is about something
/// else. Five defects were found behind that, each of which made a verdict unreadable:
/// <list type="number">
/// <item><b>The control patch was very plausibly still on the prop.</b> It sat at a CONSTANT 220 px
/// sideways; the eye texture on this rig is 3072x3264 (<c>] [Rig] EYE-TARGET DIAG</c>), so that is
/// 7.2 % of the width, and the reading was <c>PROP 0.0000..0.9893, CONTROL 0.0000..0.9938</c>. The
/// offset is now derived per capture from the prop's own PROJECTED BOX, and the line prints how many
/// placements a constant 220 would have got wrong.</item>
/// <item><b>The baseline blink was aimed at the darkest sample in the window</b>
/// (<c>_phPropLin &lt;= lo + 0.15f * range</c>) and then judged against a FLAT 0.005 of linear
/// light. It fired on a patch reading <c>0.0158</c>, so the bar demanded the prop account for 32 %
/// of every photon there; the drop it measured was 9.5 % RELATIVE and it printed <c>INERT</c>. The
/// aim is now a band around the series MEDIAN and the bar is <see cref="PhBlinkNoiseK"/> times this
/// instrument's own measured sample-to-sample noise, plus a relative term.</item>
/// <item><b>There was no A-B-A.</b> The white blink fires ON THE RISE and lasts ~0.15 s against a
/// fall the video times under 0.04 s, so a drop measured only against BEFORE cannot be told from the
/// event ending. Every blink now reads AFTER as well, and a drop that does not recover is reported
/// as the event's own decay.</item>
/// <item><b>There was no scene reference.</b> Nothing in the line could say whether the capture was
/// photographing the eye image at all. A whole-frame thumbnail column now runs beside the patches
/// and is directly comparable to the video's own whole-frame measurement (0.0114 -> 0.0289 linear,
/// never near white).</item>
/// <item><b>The rise and fall were measured on the FIRST excursion in 23.9 s, not on the flash.</b>
/// They are now anchored on the peak and the episode count is printed.</item>
/// </list>
/// And the origin question ModBuild 469 called "the ONE thing on this capture path that cannot be
/// settled without hardware" is settled two ways in-process: <c>SystemInfo.graphicsUVStartsAtTop</c>
/// is a property read that was available in all eighteen rounds, and a <b>Y-MIRRORED twin of the
/// prop patch</b> is now read at the same instant as the prop patch, so the blink asks both heights
/// at once and only one of them can contain the prop.</para>
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
/// into a full-eye single-sampled temporary — which resolves the multisampled eye buffer; this rig's
/// <c>QualitySettings.antiAliasing</c> reads <b>4</b> (<c>] [Rig] EYE-TARGET DIAG</c>, ModBuild 469,
/// and the ModBuild 469 comment here said 8) — and then <c>CommandBuffer.CopyTexture</c> REGION
/// COPIES out of that resolve, each followed immediately by <c>RequestAsyncReadback</c>, so readback
/// N carries the pixels copy N produced. A further <c>Blit</c> downsamples the SAME named temporary
/// into a 32x32 whole-frame thumbnail; that one reads a mod-named source and writes a target that is
/// never a source, so the ModBuild 191 self-sampling fault still cannot occur.
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
/// bisection verdict says which half of the remaining space survives — <b>but only if the COHERENCE
/// clause agrees</b>, which is the fourth reading and the one ModBuild 469 needed.</item>
/// <item><b>THE WRONG EVENT</b> — the capture is live and an excursion is there, but the whole-frame
/// column and/or the episode shape do not match the video's. Then the line says WHICH event was
/// photographed instead of only warning that the two differ, every co-recorded state reading stays
/// an ABSENCE, and the next round's job is the capture path rather than another candidate.</item>
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

    /// <summary>Patch edge in EYE PIXELS. This rig's <c>eyeTextureDesc</c> reads <b>3072x3264</b>
    /// (<c>] [Rig] EYE-TARGET DIAG</c>, ModBuild 469), so a 32 px window is 1.0 % of the eye width.
    /// The ModBuild 466 video photometry was done on a 1280x720 recording, where the lit region ran
    /// ~288 px; scaled to this eye texture that is ~690 px, so a 32 px window lies well inside it.
    /// <b>The ModBuild 469 comment sized this patch against 1280x720 directly, which is the wrong
    /// frame</b> — the conclusion survived the units error, the argument did not. A region copy
    /// cannot resample, so this is a texel-for-texel crop and not a downscale.</summary>
    private const int PhPatch = 32;

    /// <summary>
    /// The <b>MINIMUM</b> sideways offset of the CONTROL patch, in eye pixels. It is a floor and no
    /// longer the offset itself.
    ///
    /// <para><b>WHY IT COULD NOT BE A CONSTANT.</b> ModBuild 469 placed the control at exactly
    /// 220 px and read <c>PROP PATCH 0.0000..0.9893, CONTROL PATCH 0.0000..0.9938</c> — a control
    /// that swings as hard as the subject is not controlling anything. 220 px on a 3072-wide eye
    /// texture is 7.2 % of the width; on the 1280-wide video that is 92 px, and a bear trap held in
    /// the palm spans more than 92 px of that recording. <b>The control patch was very plausibly
    /// still on the prop.</b> A constant pixel offset cannot be off an object whose drawn size is a
    /// free variable, so the offset is now derived per capture from the prop's own projected
    /// bounding box and this number is only the floor.</para>
    /// </summary>
    private const int PhControlOffsetPx = 220;

    /// <summary>Clearance in eye pixels between the far edge of the prop's projected box and the near
    /// edge of the control patch, so "off the prop" has a margin rather than touching it.</summary>
    private const int PhControlGapPx = 48;

    /// <summary>Edge of the WHOLE-FRAME thumbnail, in texels. A scaling <c>Blit</c> from the named
    /// resolve into a 32x32 target point-samples 1024 pixels of the eye image on a regular grid; it
    /// is an estimator of the frame mean and the line says so rather than calling it one.</summary>
    private const int PhThumb = 32;

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

    // ---- the bars, all of them referenced to something measured ---------------------------------

    /// <summary>How many multiples of THIS INSTRUMENT'S OWN sample-to-sample noise a blink must move
    /// the patch before it counts as having reached the picture.
    ///
    /// <para><b>WHY THE ABSOLUTE BAR HAD TO GO.</b> ModBuild 469 judged the baseline blink against a
    /// flat <c>0.005</c> of linear light and printed <c>THE BLINK IS INERT</c> on
    /// <c>before 0.0158, during 0.0144</c>. That bar demanded the prop account for <b>32 % of every
    /// photon in the patch</b> (0.005 / 0.0158) at the darkest moment of the window; the drop it
    /// actually measured was <b>9.5 % relative</b>. A fixed absolute threshold against a near-black
    /// patch is a bar set below — or in this case far above — the instrument floor without ever
    /// measuring the instrument on a null input. The floor is now MEASURED, from the median absolute
    /// difference between consecutive non-blink samples of this very series, and printed beside the
    /// verdict so the next round can read it instead of trusting it.</para></summary>
    private const float PhBlinkNoiseK = 3f;

    /// <summary>…and the same drop must also be this fraction of the light that was there. A drop
    /// three sigma above the noise on a patch carrying nothing is still a drop of nothing.</summary>
    private const float PhBlinkRelDrop = 0.05f;

    /// <summary>Absolute floor under the noise-derived bar, so a pathologically quiet series cannot
    /// make an arbitrarily small drop significant.</summary>
    private const float PhBlinkAbsFloor = 0.0005f;

    /// <summary>Linear light the patch must carry for a baseline blink to be ABLE to prove anything.
    /// The ModBuild 469 video measurement of <c>falle_aufblitzen.mp4</c> reads a whole-frame mean of
    /// 0.0114 linear at rest, so a patch below this is darker than the frame average and a null
    /// result on it is an instrument limit rather than a finding.</summary>
    private const float PhMinBlinkLight = 0.006f;

    /// <summary>Samples collected AFTER a blink ends, to close the A-B-A. Without them a drop caused
    /// by the perturbation is indistinguishable from the flash simply ending: the white blink fires
    /// at 45 % of the rise and lasts ~0.15 s against a fall the video times at under 0.04 s.</summary>
    private const int PhAfterSamples = 4;

    // ---- what the VIDEO says, measured rather than remembered ------------------------------------

    /// <summary>Whole-frame mean luminance of <c>.planning/debug/falle_aufblitzen.mp4</c> AT REST and
    /// AT THE PEAK of an episode, in LINEAR light. Measured with ffmpeg <c>signalstats</c> over all
    /// 548 frames: <c>YAVG</c> runs 32.2 -> 49.8 of 255 gamma across an episode, i.e. 0.0114 ->
    /// 0.0289 linear. <b>The frame never approaches white</b> — the largest whole-frame mean in the
    /// whole recording is 52.3/255. Any in-process reading that puts a patch at 0.99 linear with
    /// 100 % near-white pixels is therefore not photographing this event.</summary>
    private const float PhVideoFrameRest = 0.0114f, PhVideoFramePeak = 0.0289f;

    /// <summary>The episode's shape in the video, measured the same way: the whole-frame mean ramps
    /// 32.4 -> 49.8 over ~1.05 s (t 2.83..3.90 s) and returns to 32.2 in <b>ONE 30 fps frame</b>
    /// (t 4.27 -> 4.30). Rise 1.0-1.4 s, fall under 0.04 s.</summary>
    private const float PhVideoRiseLo = 1.0f, PhVideoRiseHi = 1.4f, PhVideoFallMax = 0.04f;

    // ---- capture state --------------------------------------------------------------------------

    private static readonly int PhResolveId = Shader.PropertyToID("_GloomhavenVR_PropPhotoResolve");

    private static CommandBuffer? _phCb;
    private static RenderTexture? _phPatchRt;
    private static RenderTexture? _phThumbRt;
    private static Camera? _phBound;
    private static bool _phHooked, _phArmed, _phDead;
    private static int _phBudgetLeft = PhBudget;
    private static int _phGen;

    /// <summary>Which capture a pending readback belongs to, and the generation that requested it.
    /// FIFO, exactly as EyeFrameProbe proved: the copy and its readback enter the command stream
    /// together, so readback N carries copy N's pixels and one shared target serves every patch.
    /// <see cref="PhSlotFrame"/> is enqueued LAST of the four, so its arrival is what closes a row —
    /// a dropped patch readback then leaves a row carrying the previous value of that column instead
    /// of stalling the series forever, and the count of such rows is printed.</summary>
    private static readonly Queue<int> PhPendingSlot = new(8);
    private static readonly Queue<int> PhPendingGen = new(8);

    private const int PhSlotProp = 0, PhSlotCtl = 1, PhSlotMirror = 2, PhSlotFrame = 3;

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

    private static float _phPropLin, _phCtlLin, _phMirrorLin, _phFrameLin, _phPropWhite;
    private static float _phPropR, _phPropG, _phPropB;
    private static int _phSlotMask, _phPartialRows;
    private static uint _phPropHash, _phCtlHash, _phMirrorHash, _phFrameHash;
    private static int _phPropHashChanges, _phCtlHashChanges, _phMirrorHashChanges, _phFrameHashChanges;
    private static bool _phSeenAnyHash;

    /// <summary>WHERE THE LAST CAPTURE ACTUALLY LOOKED. ModBuild 469 printed a placement verdict and
    /// never printed a single coordinate, so "the patch is not on the prop" could be named as a cause
    /// but not checked by anyone reading the log.</summary>
    private static int _phPx, _phPy, _phCtlX, _phMirrorY, _phTargetW, _phTargetH;
    private static int _phRenderW, _phRenderH, _phPropPxW, _phPropPxH, _phCtlOffsetUsed;
    private static int _phCtlOnProp, _phPlacements;
    private static int _phFrameBlits;

    // ---- the series -----------------------------------------------------------------------------

    private static readonly float[] PhT = new float[PhSampleCap];
    private static readonly float[] PhProp = new float[PhSampleCap];
    private static readonly float[] PhCtl = new float[PhSampleCap];
    private static readonly float[] PhMirror = new float[PhSampleCap];
    private static readonly float[] PhFrame = new float[PhSampleCap];
    private static readonly float[] PhWhiteFrac = new float[PhSampleCap];
    private static readonly byte[] PhMark = new byte[PhSampleCap];
    private static int _phCount, _phOverflow;

    /// <summary>Scratch for the median of a partial series. Sized to the cap so the trigger allocates
    /// nothing per sample.</summary>
    private static readonly float[] PhScratch = new float[PhSampleCap];

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

    /// <summary>THE MIRROR COLUMN AT EACH BLINK. The prop patch and its Y-MIRRORED twin are read
    /// together, so the blink asks BOTH heights at once: whichever one the perturbation moves is the
    /// one that is on the prop. That turns <c>CopyTexture</c>'s source origin — which ModBuild 469
    /// called "the ONE thing on this capture path that cannot be settled without hardware" — into a
    /// one-bit answer taken from the picture itself.</summary>
    private static float _phBaseMirrorBefore = -1f, _phBaseMirrorDuring = -1f;
    private static float _phWhiteMirrorBefore = -1f, _phWhiteMirrorDuring = -1f;

    /// <summary>THE THIRD LEG OF THE A-B-A. A drop measured only against BEFORE cannot tell a
    /// perturbation that removed light from an event that ended on its own — and the white blink is
    /// fired ON THE RISE of an event the video times at a sub-0.04 s fall, so that confound is not
    /// hypothetical. AFTER is the maximum the patch recovers to in the samples following the blink.
    /// </summary>
    private static float _phBaseAfter = -1f, _phWhiteAfter = -1f;
    private static int _phBaseAfterSamples, _phWhiteAfterSamples, _phAfterLeft;
    private static byte _phAfterKind;

    /// <summary>The light present when the baseline blink fired, and the series median it was aimed
    /// at. ModBuild 469 aimed the null-perturbation control at the DARKEST 15 % of the window by
    /// construction (<c>_phPropLin &lt;= lo + 0.15f * range</c>) and then asked the prop to be a
    /// third of the light there.</summary>
    private static float _phBaseAimMedian = -1f;
    private static bool _phBaseAimTooDark;

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
        _phSlotMask = _phPartialRows = 0;
        _phSeenAnyHash = false;
        _phPropHashChanges = _phCtlHashChanges = 0;
        _phMirrorHashChanges = _phFrameHashChanges = 0;
        _phFrameBlits = 0;
        _phPx = _phPy = _phCtlX = _phMirrorY = _phTargetW = _phTargetH = 0;
        _phRenderW = _phRenderH = _phPropPxW = _phPropPxH = _phCtlOffsetUsed = 0;
        _phCtlOnProp = _phPlacements = 0;
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
        _phBaseMirrorBefore = _phBaseMirrorDuring = -1f;
        _phWhiteMirrorBefore = _phWhiteMirrorDuring = -1f;
        _phBaseAfter = _phWhiteAfter = -1f;
        _phBaseAfterSamples = _phWhiteAfterSamples = _phAfterLeft = 0;
        _phAfterKind = PhMarkNone;
        _phBaseAimMedian = -1f;
        _phBaseAimTooDark = false;
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
        if (_phPatchRt != null && _phThumbRt != null)
            return true;
        _phPatchRt ??= PhMakeTarget(PhPatch, "Patch");
        _phThumbRt ??= PhMakeTarget(PhThumb, "Frame");
        if (_phPatchRt != null && _phThumbRt != null)
            return true;
        _phDead = true;
        _phUnsupported = "a " + PhPatch + "x" + PhPatch + " capture target could not be created";
        return false;
    }

    private static RenderTexture? PhMakeTarget(int edge, string suffix)
    {
        var rt = new RenderTexture(edge, edge, 0, RenderTextureFormat.ARGB32)
        {
            name = "GloomhavenVR.PropFlashPhotometer." + suffix,
            antiAliasing = 1,
            filterMode = FilterMode.Point,
            useMipMap = false,
            autoGenerateMips = false,
        };
        if (rt.Create())
            return rt;
        rt.Release();
        Object.Destroy(rt);
        return null;
    }

    /// <summary>Unhook, hand every blinked renderer back and drop the references. Idempotent, and it
    /// runs BEFORE the line is built — a probe that has answered is spent, and one that keeps a
    /// command buffer on the head camera afterwards is a shape this project has paid for. The
    /// generation bump makes a readback still in flight arrive into nothing rather than be folded
    /// into the next window's numbers.</summary>
    private static void DisarmPhotometer()
    {
        PhEndBlink();
        _phAfterLeft = 0;
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

            if (!PhTryPlacement(cam))
            {
                _phOffTarget++;
                return;
            }

            if (!PhEnsureResolve(_phTargetW, _phTargetH))
                return;

            _phCaptureFrame = Time.frameCount;
            // Order matters and it is the row protocol: the three region copies first, then the
            // whole-frame thumbnail LAST, so the frame slot's arrival is what closes a row.
            PhCopyAndRead(_phPx, _phPy, PhSlotProp);
            PhCopyAndRead(_phCtlX, _phPy, PhSlotCtl);
            PhCopyAndRead(_phPx, _phMirrorY, PhSlotMirror);
            PhBlitFrameAndRead();
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
    /// THE WHOLE EYE FRAME, DOWNSAMPLED — the reference the ModBuild 469 line did not have, and the
    /// one number that can say WHICH event was photographed.
    ///
    /// <para>The video measurement is a whole-frame statistic: <c>YAVG</c> 0.0114 linear at rest,
    /// 0.0289 at the peak of an episode, never anywhere near white. A 32x32 point-sampled thumbnail
    /// of the same frame is directly comparable to it. So the three cases separate: the thumbnail
    /// tracking 0.011..0.029 while the patches swing 0..1 means the CAPTURE is fine and the PATCHES
    /// are not on the scene; the thumbnail swinging 0..1 too means the resolve is not the eye colour
    /// image and the capture path is the finding; the thumbnail tracking the video's ramp while the
    /// patches follow it means the event is frame-wide and no renderer bisection can reach it.</para>
    ///
    /// <para><b>This is a Blit and the ModBuild 191 fault still cannot occur:</b> 191 read
    /// <c>CurrentActive</c> twice, so its second blit sampled its own destination. This one reads the
    /// MOD-NAMED temporary and writes a target that is never a source, so source and destination can
    /// never be the same surface. It carries its own hash column and its own change count, so a dead
    /// thumbnail is visible rather than assumed live.</para>
    /// </summary>
    private static void PhBlitFrameAndRead()
    {
        if (_phCb == null || _phThumbRt == null)
            return;
        _phCb.Blit(PhResolveId, _phThumbRt);
        PhPendingSlot.Enqueue(PhSlotFrame);
        PhPendingGen.Enqueue(_phGen);
        _phFrameBlits++;
        _phCb.RequestAsyncReadback(_phThumbRt, 0, TextureFormat.RGBA32, OnPhotometerRead);
    }

    /// <summary>
    /// Where the four captures look this frame. Integer coordinates, because a region copy is a
    /// texel-for-texel move that cannot resample. Returns false rather than clamping when a window
    /// would leave the target: a clamped window silently photographs somewhere else and the series
    /// would then mix two subjects. Everything it decides is stored on the class and PRINTED, because
    /// ModBuild 469 named "the patch is not on the prop" as its likeliest cause while printing not
    /// one coordinate a reader could check it against.
    ///
    /// <para><b>THREE THINGS IT DOES THAT THE 469 VERSION DID NOT.</b></para>
    /// <list type="number">
    /// <item><b>It measures the prop's PROJECTED BOX</b>, from the eight corners of
    /// <c>Renderer.bounds</c>, and puts the control patch outside it. A control at a constant 220 px
    /// cannot be off an object whose drawn size is a free variable, and 469's control ranged
    /// 0.0000..0.9938 against the prop's own 0.0000..0.9893 — the reading a control patch still
    /// sitting on the subject produces.</item>
    /// <item><b>It bounds against the RENDERED viewport</b> (<c>eyeTexture * renderViewportScale</c>)
    /// and not the ALLOCATED eye texture. With a scale below 1 the allocated texture is larger than
    /// what the pass drew into, so 469's bound check would happily place a patch — the control patch
    /// especially, being pushed sideways — in a region the frame never wrote.</item>
    /// <item><b>It computes the Y-MIRRORED twin of the prop patch</b>, which is what turns
    /// <c>CopyTexture</c>'s source origin into a measurement instead of a caveat.</item>
    /// </list>
    /// </summary>
    private static bool PhTryPlacement(Camera cam)
    {
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

        int rw = Mathf.Clamp(Mathf.RoundToInt(w * viewportScale), PhPatch * 2, w);
        int rh = Mathf.Clamp(Mathf.RoundToInt(h * viewportScale), PhPatch * 2, h);

        // The prop's own DRAWN centre and not its transform origin: a bounds centre is where the
        // pixels are, and a pivot on this game's props frequently is not.
        Bounds bb = lead.bounds;
        Vector3 vp = cam.WorldToViewportPoint(bb.center);
        if (vp.z <= 0f)
            return false;

        // ...and the drawn EXTENT, from the eight corners, so "off the prop" is measured.
        Vector3 c = bb.center, e = bb.extents;
        float vx0 = float.MaxValue, vx1 = float.MinValue, vy0 = float.MaxValue, vy1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            var corner = new Vector3(
                c.x + ((k & 1) == 0 ? -e.x : e.x),
                c.y + ((k & 2) == 0 ? -e.y : e.y),
                c.z + ((k & 4) == 0 ? -e.z : e.z));
            Vector3 v = cam.WorldToViewportPoint(corner);
            if (v.z <= 0f)
                continue;
            if (v.x < vx0) vx0 = v.x;
            if (v.x > vx1) vx1 = v.x;
            if (v.y < vy0) vy0 = v.y;
            if (v.y > vy1) vy1 = v.y;
        }
        int propW = vx0 <= vx1 ? Mathf.RoundToInt((vx1 - vx0) * rw) : 0;
        int propH = vy0 <= vy1 ? Mathf.RoundToInt((vy1 - vy0) * rh) : 0;

        float half = PhPatch * 0.5f;
        int ix = Mathf.RoundToInt((vp.x * rw) - half);
        int iy = Mathf.RoundToInt((vp.y * rh) - half);
        if (ix < 0 || iy < 0 || ix + PhPatch > rw || iy + PhPatch > rh)
            return false;

        // The control must clear the prop's own projected box, not a constant.
        int want = Mathf.Max(PhControlOffsetPx, (propW / 2) + PhPatch + PhControlGapPx);
        int ctlX = ix + want;
        if (ctlX + PhPatch > rw)
            ctlX = ix - want;
        if (ctlX < 0 || ctlX + PhPatch > rw)
            return false;

        int mirrorY = rh - PhPatch - iy;
        if (mirrorY < 0 || mirrorY + PhPatch > rh)
            return false;

        _phTargetW = w;
        _phTargetH = h;
        _phRenderW = rw;
        _phRenderH = rh;
        _phPx = ix;
        _phPy = iy;
        _phCtlX = ctlX;
        _phMirrorY = mirrorY;
        _phPropPxW = propW;
        _phPropPxH = propH;
        _phCtlOffsetUsed = want;
        _phPlacements++;
        // Would a CONSTANT 220 px still have been on the prop? That is the 469 reading, answered.
        if (propW > 0 && PhControlOffsetPx < (propW / 2) + PhPatch)
            _phCtlOnProp++;
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
        int n = slot == PhSlotFrame ? PhThumb * PhThumb : PhPatch * PhPatch;
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

        switch (slot)
        {
            case PhSlotProp:
                if (_phSeenAnyHash && hash != _phPropHash)
                    _phPropHashChanges++;
                _phPropHash = hash;
                _phPropLin = mean;
                _phPropWhite = white * inv;
                _phPropR = (float)(sr * inv);
                _phPropG = (float)(sg * inv);
                _phPropB = (float)(sb * inv);
                break;
            case PhSlotCtl:
                if (_phSeenAnyHash && hash != _phCtlHash)
                    _phCtlHashChanges++;
                _phCtlHash = hash;
                _phCtlLin = mean;
                break;
            case PhSlotMirror:
                if (_phSeenAnyHash && hash != _phMirrorHash)
                    _phMirrorHashChanges++;
                _phMirrorHash = hash;
                _phMirrorLin = mean;
                break;
            default:
                if (_phSeenAnyHash && hash != _phFrameHash)
                    _phFrameHashChanges++;
                _phFrameHash = hash;
                _phFrameLin = mean;
                break;
        }
        _phSlotMask |= 1 << slot;

        // The FRAME slot is enqueued last of the four, so its arrival closes the row. A row missing
        // one of the other columns is stored anyway, carrying that column's previous value, and
        // counted — because the alternative is a dropped readback stalling the series for good, which
        // would read exactly like a dead capture.
        if (slot == PhSlotFrame)
        {
            if (_phSlotMask != 0xF)
                _phPartialRows++;
            _phSeenAnyHash = true;
            StorePhotometerSample();
            _phSlotMask = 0;
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
            PhMirror[k] = _phMirrorLin;
            PhFrame[k] = _phFrameLin;
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
                _phBaseMirrorDuring = _phBaseSamples == 0
                    ? _phMirrorLin
                    : Mathf.Min(_phBaseMirrorDuring, _phMirrorLin);
                _phBaseCtl = _phCtlLin;
                _phBaseSamples++;
            }
            else if (_phBlinkKind == PhMarkWhiteBlink)
            {
                _phWhiteDuring = _phWhiteSamples == 0 ? _phPropLin : Mathf.Min(_phWhiteDuring, _phPropLin);
                _phWhiteMirrorDuring = _phWhiteSamples == 0
                    ? _phMirrorLin
                    : Mathf.Min(_phWhiteMirrorDuring, _phMirrorLin);
                _phWhiteCtl = _phCtlLin;
                _phWhiteSamples++;
            }
            return;
        }

        // THE THIRD LEG. The blink has ended; watch the patch come back — or not. A drop that does
        // not recover was the event ending, not the perturbation working.
        if (_phAfterLeft > 0)
        {
            _phAfterLeft--;
            if (_phAfterKind == PhMarkBaseBlink)
            {
                _phBaseAfter = _phBaseAfterSamples == 0
                    ? _phPropLin
                    : Mathf.Max(_phBaseAfter, _phPropLin);
                _phBaseAfterSamples++;
            }
            else if (_phAfterKind == PhMarkWhiteBlink)
            {
                _phWhiteAfter = _phWhiteAfterSamples == 0
                    ? _phPropLin
                    : Mathf.Max(_phWhiteAfter, _phPropLin);
                _phWhiteAfterSamples++;
            }
            if (_phAfterLeft == 0)
                _phAfterKind = PhMarkNone;
            return;
        }

        PhConsiderBlink();
    }

    /// <summary>Median of the non-blink prop series so far. It is what the baseline blink is AIMED at:
    /// see <see cref="PhConsiderBlink"/>.</summary>
    private static float PhPropMedian()
    {
        int m = 0;
        for (int i = 0; i < _phCount && m < PhScratch.Length; i++)
        {
            if (PhMark[i] == PhMarkNone)
                PhScratch[m++] = PhProp[i];
        }
        if (m == 0)
            return -1f;
        System.Array.Sort(PhScratch, 0, m);
        return PhScratch[m / 2];
    }

    /// <summary>
    /// THIS INSTRUMENT'S OWN NOISE FLOOR: the MEDIAN absolute difference between consecutive
    /// non-blink samples of one column. The median and not the mean, so a real excursion inside the
    /// series raises it by nothing — the floor must describe the instrument at rest, not the event.
    ///
    /// <para>It exists because ModBuild 469 set an absolute 0.005 bar and reported <c>INERT</c> on a
    /// 0.0015 drop without ever measuring what 0.0015 IS on this rig. A bar chosen without measuring
    /// the instrument on a null input is this project's recorded mistake, and the fix is not a
    /// different constant.</para>
    /// </summary>
    private static float PhMedianStep(float[] series, int samples)
    {
        int m = 0;
        int prev = -1;
        for (int i = 0; i < samples && m < PhScratch.Length; i++)
        {
            if (PhMark[i] != PhMarkNone)
            {
                prev = -1;
                continue;
            }
            if (prev >= 0)
                PhScratch[m++] = Mathf.Abs(series[i] - series[prev]);
            prev = i;
        }
        if (m == 0)
            return 0f;
        System.Array.Sort(PhScratch, 0, m);
        return PhScratch[m / 2];
    }

    /// <summary>Pearson r between two columns over the non-blink samples. It is the statistic that
    /// says whether the CONTROL is controlling: two patches 220 px apart that both ranged
    /// 0.0000..0.99 in ModBuild 469 either share a frame-wide event or share a subject, and a
    /// correlation separates that from coincidence in one number. Returns 0 when either column is
    /// constant, which reads as "no relationship established" rather than as a false 1.</summary>
    private static float PhCorrelation(float[] a, float[] b, int samples)
    {
        double sa = 0, sb2 = 0;
        int n = 0;
        for (int i = 0; i < samples; i++)
        {
            if (PhMark[i] != PhMarkNone)
                continue;
            sa += a[i];
            sb2 += b[i];
            n++;
        }
        if (n < 3)
            return 0f;
        double ma = sa / n, mb = sb2 / n;
        double saa = 0, sbb = 0, sab = 0;
        for (int i = 0; i < samples; i++)
        {
            if (PhMark[i] != PhMarkNone)
                continue;
            double da = a[i] - ma, db = b[i] - mb;
            saa += da * da;
            sbb += db * db;
            sab += da * db;
        }
        double den = System.Math.Sqrt(saa * sbb);
        return den <= 1e-12 ? 0f : (float)(sab / den);
    }

    /// <summary>
    /// Decide whether this sample fires a blink.
    ///
    /// <para>THE BASELINE BLINK IS THE NULL-PERTURBATION CONTROL and it fires FIRST, once enough
    /// samples exist to know where baseline is. Without it, a white blink that changed nothing would
    /// be indistinguishable from a blink that DID nothing — precisely the failure mode ModBuild 467
    /// needed four separate clauses to rule out.</para>
    ///
    /// <para><b>AND IT IS NOW AIMED AT A LIT SAMPLE INSTEAD OF THE DARKEST ONE.</b> ModBuild 469
    /// fired it when <c>_phPropLin &lt;= lo + 0.15f * range</c> — the bottom 15 % of the observed
    /// range, which on that window meant a patch reading <c>0.0158</c> of linear light out of an
    /// observed <c>0.0000..0.9893</c>. A null-perturbation control aimed at the darkest moment in the
    /// window asks the prop to be a measurable fraction of almost nothing; the absolute 0.005 bar it
    /// was then judged against demanded 32 % of every photon in the patch. <b>The control was
    /// unmeetable by construction, and it printed INERT.</b> It now fires in a band around the
    /// series MEDIAN, and if even the median is darker than <see cref="PhMinBlinkLight"/> it fires
    /// anyway and the line SAYS the patch was too dark for the perturbation to be provable — an
    /// instrument limit stated, rather than a null result reported as a finding.</para>
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

        if (!_phBaseBlinkDone)
        {
            float median = PhPropMedian();
            if (median < 0f)
                return;
            // A band around the median, not the floor of the range.
            if (_phPropLin < 0.6f * median || _phPropLin > 1.4f * median)
                return;
            _phBaseBlinkDone = true;
            _phBaseAimMedian = median;
            _phBaseAimTooDark = _phPropLin < PhMinBlinkLight;
            _phBaseBefore = _phPropLin;
            _phBaseMirrorBefore = _phMirrorLin;
            PhBeginBlink(PhMarkBaseBlink);
            return;
        }
        if (!_phWhiteBlinkDone && range >= PhMinExcursion
            && _phPropLin >= lo + (PhTriggerFraction * range))
        {
            _phWhiteBlinkDone = true;
            _phWhiteBefore = _phPropLin;
            _phWhiteMirrorBefore = _phMirrorLin;
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
        // Arm the recovery leg only when a blink was actually running: DisarmPhotometer calls this
        // unconditionally, and a window closing must not open an A-B-A that will never be read.
        if (_phBlinkLive && _phArmed)
        {
            _phAfterKind = _phBlinkKind;
            _phAfterLeft = PhAfterSamples;
        }
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
          .Append(_phOverflow).Append(", whole-frame blits ").Append(_phFrameBlits)
          .Append(", rows stored with a column missing ").Append(_phPartialRows)
          .Append(". PATCH CONTENT CHANGED between consecutive captures on ")
          .Append(_phPropHashChanges).Append(" prop, ").Append(_phCtlHashChanges)
          .Append(" control, ").Append(_phMirrorHashChanges).Append(" mirror and ")
          .Append(_phFrameHashChanges)
          .Append(" whole-frame reading(s) — THIS IS THE DEAD-CAPTURE FALSIFIER: ModBuild 191 read "
                  + "BIT-IDENTICAL pixels for a whole build because CommandBuffer.Blit rebinds the "
                  + "active render target and its second blit was reading its own destination. All "
                  + "zeroes here mean the capture is dead and NOTHING below is a reading. ");

        // ---- WHERE IT LOOKED. ModBuild 469 named "the patch is not on the prop" as its likeliest
        // cause and printed not one coordinate anybody could check that against.
        sb.Append("WHERE IT LOOKED (last of ").Append(_phPlacements)
          .Append(" placement(s), and this is what 469 never printed): eye texture ")
          .Append(_phTargetW).Append('x').Append(_phTargetH).Append(", RENDERED viewport ")
          .Append(_phRenderW).Append('x').Append(_phRenderH)
          .Append(" (renderViewportScale ").Append(XRSettings.renderViewportScale.ToString("0.###"))
          .Append("), prop patch at (").Append(_phPx).Append(',').Append(_phPy)
          .Append("), control at (").Append(_phCtlX).Append(',').Append(_phPy)
          .Append("), Y-MIRROR of the prop patch at (").Append(_phPx).Append(',').Append(_phMirrorY)
          .Append("), patch edge ").Append(PhPatch).Append(" px. THE PROP'S OWN PROJECTED BOX is ")
          .Append(_phPropPxW).Append('x').Append(_phPropPxH)
          .Append(" px, so the control was pushed ").Append(_phCtlOffsetUsed)
          .Append(" px sideways to clear it. *** THE 469 CONTROL PATCH, ANSWERED: a CONSTANT ")
          .Append(PhControlOffsetPx).Append(" px offset would still have landed ON THE PROP on ")
          .Append(_phCtlOnProp).Append(" of ").Append(_phPlacements)
          .Append(" placement(s) — ModBuild 469 used exactly that constant and read CONTROL PATCH "
                  + "0.0000..0.9938 against a PROP PATCH of 0.0000..0.9893, which is the reading a "
                  + "control still sitting on its own subject produces. A count above 0 here NAMES "
                  + "that as the cause; a 0 says the control was genuinely off the prop and the two "
                  + "patches agreed for some other reason. *** ");

        // ---- THE ORIGIN QUESTION, WHICH IS NOT UNSETTLEABLE. 469 called CopyTexture's source
        // origin "the ONE thing on this capture path that cannot be settled without hardware". The
        // API bit is a property read, and the mirror patch settles it from the picture.
        sb.Append("THE Y-ORIGIN QUESTION, WHICH 469 CALLED UNSETTLEABLE WITHOUT HARDWARE AND IS NOT: "
                  + "SystemInfo.graphicsDeviceType ").Append(SystemInfo.graphicsDeviceType)
          .Append(", SystemInfo.graphicsUVStartsAtTop ").Append(SystemInfo.graphicsUVStartsAtTop)
          .Append(", QualitySettings.antiAliasing ").Append(QualitySettings.antiAliasing)
          .Append(". Both of the first two are plain property reads that were available every one of "
                  + "the eighteen rounds. They state the PREMISE of the flip hypothesis; the MIRROR "
                  + "PATCH below decides it from the picture, because the blink asks the prop's "
                  + "height and its mirrored height at the same instant and only one of them can "
                  + "contain the prop. ");

        float lo = float.MaxValue, hi = float.MinValue, ctlLo = float.MaxValue, ctlHi = float.MinValue;
        float frLo = float.MaxValue, frHi = float.MinValue;
        float whiteHi = 0f;
        int clean = 0;
        for (int i = 0; i < samples; i++)
        {
            if (PhMark[i] != PhMarkNone)
                continue;
            clean++;
            if (PhProp[i] < lo) lo = PhProp[i];
            if (PhProp[i] > hi) hi = PhProp[i];
            if (PhCtl[i] < ctlLo) ctlLo = PhCtl[i];
            if (PhCtl[i] > ctlHi) ctlHi = PhCtl[i];
            if (PhFrame[i] < frLo) frLo = PhFrame[i];
            if (PhFrame[i] > frHi) frHi = PhFrame[i];
            if (PhWhiteFrac[i] > whiteHi) whiteHi = PhWhiteFrac[i];
        }
        bool haveRange = lo <= hi;
        float range = haveRange ? hi - lo : 0f;

        // ---- THE INSTRUMENT'S OWN NOISE FLOOR, measured on this rig from this very series --------
        // A bar set without measuring the instrument on a null input is this project's recorded
        // mistake; 469 judged a blink against a flat 0.005 on a patch whose whole content was 0.0158.
        float noise = PhMedianStep(PhProp, samples);
        float ctlNoise = PhMedianStep(PhCtl, samples);
        float frameNoise = PhMedianStep(PhFrame, samples);
        float blinkBar = Mathf.Max(PhBlinkNoiseK * noise, PhBlinkAbsFloor);

        // ---- IS THE EXCURSION LOCAL TO THE PROP AT ALL? -------------------------------------------
        float rPropCtl = PhCorrelation(PhProp, PhCtl, samples);
        float rPropFrame = PhCorrelation(PhProp, PhFrame, samples);

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
              .Append(ctlHi.ToString("0.0000")).Append(", WHOLE FRAME ").Append(frLo.ToString("0.0000"))
              .Append("..").Append(frHi.ToString("0.0000")).Append(", peak NEAR-WHITE pixel fraction ")
              .Append((whiteHi * 100f).ToString("0.0")).Append(" %, over ").Append(clean)
              .Append(" non-blink sample(s). SAMPLE-TO-SAMPLE NOISE FLOOR (median |step|, this "
                      + "instrument on this rig): prop ").Append(noise.ToString("0.00000"))
              .Append(", control ").Append(ctlNoise.ToString("0.00000"))
              .Append(", whole frame ").Append(frameNoise.ToString("0.00000"))
              .Append(" — EVERY BAR BELOW IS REFERENCED TO IT AND NOT TO A CONSTANT. ");

            // ---- THE REFERENCE THE 469 LINE DID NOT HAVE -----------------------------------------
            sb.Append("*** WHAT THE VIDEO SAYS THE WHOLE FRAME DOES, measured with ffmpeg "
                      + "signalstats over all 548 frames of .planning/debug/falle_aufblitzen.mp4: "
                      + "YAVG runs 32.2 -> 49.8 of 255 gamma across an episode, i.e. ")
              .Append(PhVideoFrameRest.ToString("0.0000")).Append(" -> ")
              .Append(PhVideoFramePeak.ToString("0.0000"))
              .Append(" LINEAR, and the largest whole-frame mean anywhere in the recording is "
                      + "52.3/255. THE FRAME NEVER APPROACHES WHITE. So the whole-frame column above "
                      + "is directly comparable to a number measured off the picture the user "
                      + "complained about, and it is the column that says which event was "
                      + "photographed. *** ");

            // ---- LOCAL OR FRAME-WIDE ------------------------------------------------------------
            sb.Append("IS THE EXCURSION LOCAL TO THE PROP? Pearson r(prop, control) ")
              .Append(rPropCtl.ToString("0.000")).Append(", r(prop, whole frame) ")
              .Append(rPropFrame.ToString("0.000"))
              .Append(". A control patch is only a control if it does NOT track its subject: ")
              .Append(rPropCtl >= 0.8f
                  ? "*** IT TRACKS. The two patches move together, so whatever the photometer saw is "
                    + "NOT confined to the prop's own screen position and no perturbation of the "
                    + "prop's renderers can bisect it. Read every blink verdict below under that. *** "
                  : rPropCtl <= 0.3f
                      ? "the control is INDEPENDENT of the prop patch, so an excursion seen on one "
                        + "and not the other is genuinely local. "
                      : "the two are partially coupled; treat a blink verdict as provisional. ");

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
                // THE EPISODE, NOT THE WHOLE SERIES. ModBuild 469 took the FIRST sample above 10 %
                // and the FIRST above 90 % anywhere in 23.9 s, so its rise and fall described
                // whichever excursion happened to come first — which on a series that also contains
                // an unrelated event is not the flash. Anchor on the PEAK and walk outwards.
                float trig10 = lo + (0.1f * range), trig90 = lo + (0.9f * range);
                int iPeak = -1;
                for (int i = 0; i < samples; i++)
                {
                    if (PhMark[i] != PhMarkNone)
                        continue;
                    if (iPeak < 0 || PhProp[i] > PhProp[iPeak])
                        iPeak = i;
                }
                float t10 = -1f, t90 = -1f, t90End = -1f, tFall = -1f;
                float tPeak = iPeak >= 0 ? PhT[iPeak] : -1f;
                for (int i = iPeak; i >= 0; i--)
                {
                    if (PhMark[i] != PhMarkNone)
                        continue;
                    if (PhProp[i] >= trig90) t90 = PhT[i];
                    if (PhProp[i] >= trig10) t10 = PhT[i];
                    else break;
                }
                for (int i = iPeak; i >= 0 && i < samples; i++)
                {
                    if (PhMark[i] != PhMarkNone)
                        continue;
                    if (PhProp[i] >= trig90) t90End = PhT[i];
                    if (PhProp[i] <= trig10) { tFall = PhT[i]; break; }
                }
                // How many separate episodes crossed the halfway line upward.
                int episodes = 0;
                bool above = false;
                float halfway = lo + (0.5f * range);
                for (int i = 0; i < samples; i++)
                {
                    if (PhMark[i] != PhMarkNone)
                        continue;
                    bool now = PhProp[i] >= halfway;
                    if (now && !above)
                        episodes++;
                    above = now;
                }

                float riseS = t10 >= 0f && t90 >= 0f ? t90 - t10 : -1f;
                float fallS = t90End >= 0f && tFall >= 0f ? tFall - t90End : -1f;

                sb.Append("*** WHITE WITNESSED — the prop patch rose ")
                  .Append(range.ToString("0.0000"))
                  .Append(" of linear light, in ").Append(episodes)
                  .Append(" episode(s) crossing the halfway line; the LARGEST is at t ")
                  .Append(tPeak.ToString("0.00")).Append(" s and the times below are measured "
                          + "around IT and not around the first excursion in the window. RISE "
                          + "(10 % -> 90 %) ")
                  .Append(riseS >= 0f ? riseS.ToString("0.00") + " s" : "<not resolved>")
                  .Append(", FALL (90 % -> 10 %) ")
                  .Append(fallS >= 0f ? fallS.ToString("0.00") + " s" : "<not resolved>")
                  .Append(". ADDED LIGHT AT PEAK, normalised to red (the video read 1.000/0.995/1.010, "
                          + "and this mod's own overlays are amber 1.000/0.342/0.056 and cool blue, "
                          + "which is what excludes them BY COLOUR): ")
                  .Append(_phPeakR > 1e-5f
                      ? "1.000/" + (_phPeakG / _phPeakR).ToString("0.000") + "/"
                        + (_phPeakB / _phPeakR).ToString("0.000")
                      : "<peak too dark to normalise>")
                  .Append(". *** ");

                // ---- COHERENCE, AND IT NOW NAMES THE EVENT INSTEAD OF WARNING ABOUT ONE ----------
                bool riseOk = riseS >= 0f && riseS >= 0.5f * PhVideoRiseLo && riseS <= 2f * PhVideoRiseHi;
                bool fallOk = fallS < 0f || fallS <= 4f * PhVideoFallMax;
                bool frameLikeVideo = frHi <= 4f * PhVideoFramePeak;
                sb.Append("COHERENCE AGAINST THE VIDEO — the video's episode ramps over 1.0-1.4 s and "
                          + "clears inside ONE 30 fps frame (under 0.04 s), with the whole-frame mean "
                          + "moving 0.0114 -> 0.0289 linear and NEVER approaching white. ");
                if (riseOk && fallOk && frameLikeVideo)
                {
                    sb.Append("*** THE TWO AGREE. The in-process episode has the video's shape and "
                              + "the whole-frame column has the video's amplitude, so this photometer "
                              + "is photographing the event the user is complaining about and every "
                              + "state reading co-recorded in this window is a genuine EXCLUSION. *** ");
                }
                else
                {
                    sb.Append("*** THEY DISAGREE, AND HERE IS WHICH EVENT WAS PHOTOGRAPHED. ");
                    if (!frameLikeVideo && whiteHi > 0.5f)
                    {
                        sb.Append("The WHOLE FRAME reached ").Append(frHi.ToString("0.0000"))
                          .Append(" of linear light against the video's ")
                          .Append(PhVideoFramePeak.ToString("0.0000"))
                          .Append(" and ").Append((whiteHi * 100f).ToString("0.0"))
                          .Append(" % of the patch was NEAR-WHITE. A frame that is entirely white is "
                                  + "not the veil the user describes — a veil is a thing laid OVER a "
                                  + "picture that is still there. THE CAPTURE PATH IS THE FINDING: "
                                  + "the resolve is not carrying the eye colour image the headset "
                                  + "presents, and the next round's first job is this path and not "
                                  + "another candidate for the white. ");
                    }
                    else if (!frameLikeVideo)
                    {
                        sb.Append("The whole-frame column ranged ").Append(frLo.ToString("0.0000"))
                          .Append("..").Append(frHi.ToString("0.0000"))
                          .Append(" against the video's 0.0114..0.0289, so the picture this "
                                  + "instrument photographed is not the picture the video records. ");
                    }
                    else if (rPropCtl >= 0.8f)
                    {
                        sb.Append("The whole-frame amplitude MATCHES the video but the prop and "
                                  + "control patches move together (r ")
                          .Append(rPropCtl.ToString("0.000"))
                          .Append("), so the excursion is FRAME-WIDE and not the prop's own shading. "
                                  + "The bisection cannot be performed on this event by perturbing "
                                  + "the prop's renderers, whatever the blinks below read. ");
                    }
                    else
                    {
                        sb.Append("The whole-frame amplitude matches the video and the patches are "
                                  + "independent, but the EPISODE SHAPE does not: ")
                          .Append(riseOk ? "the rise fits and " : "the rise is ")
                          .Append(riseOk ? string.Empty : riseS.ToString("0.00") + " s against "
                                                          + "1.0-1.4 s, and ")
                          .Append(fallOk ? "the fall fits" : "the fall is "
                                                             + (fallS >= 0f ? fallS.ToString("0.00") : "?")
                                                             + " s against under 0.04 s")
                          .Append(". A patch on the prop can be crossed by something with its own "
                                  + "timing — a hand, a particle, the head moving — so read the "
                                  + "blinks below as being about THAT excursion. ");
                    }
                    sb.Append("EVERY STATE READING CO-RECORDED IN THIS WINDOW STAYS AN ABSENCE AND "
                              + "NOT AN EXCLUSION. *** ");
                }
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
        float baseRel = baseRan ? baseDrop / Mathf.Max(_phBaseBefore, 1e-6f) : 0f;
        float whiteRel = whiteRan ? whiteDrop / Mathf.Max(_phWhiteBefore, 1e-6f) : 0f;
        bool baseWorks = baseRan && baseDrop >= blinkBar && baseRel >= PhBlinkRelDrop;

        sb.Append("THE BAR EVERY BLINK IS JUDGED AGAINST, AND IT IS MEASURED: a drop counts when it "
                  + "clears BOTH ").Append(PhBlinkNoiseK.ToString("0"))
          .Append("x this instrument's own median sample step (")
          .Append(noise.ToString("0.00000")).Append(" -> bar ").Append(blinkBar.ToString("0.00000"))
          .Append(", floored at ").Append(PhBlinkAbsFloor.ToString("0.00000"))
          .Append(") AND ").Append((PhBlinkRelDrop * 100f).ToString("0"))
          .Append(" % of the light that was present. *** MODBUILD 469 USED A FLAT 0.005 AND CALLED "
                  + "ITS BASELINE BLINK INERT ON before 0.0158 / during 0.0144 — a drop of 9.5 % "
                  + "RELATIVE, judged against a bar demanding the prop be 32 % of every photon in a "
                  + "near-black patch. That verdict was produced by the bar, not by the picture. *** ");

        sb.Append("BASELINE BLINK (the NULL-PERTURBATION CONTROL, and it is why the white blink can "
                  + "mean anything — ModBuild 467 needed four clauses to rule out a perturbation "
                  + "that never happened): ");
        if (!baseRan)
        {
            sb.Append("NEVER FIRED, so the blink machinery is UNVERIFIED and no verdict below stands. ");
        }
        else
        {
            sb.Append("aimed at the series MEDIAN ").Append(_phBaseAimMedian.ToString("0.0000"))
              .Append(" (ModBuild 469 aimed it at the DARKEST 15 % of the range by construction), "
                      + "before ").Append(_phBaseBefore.ToString("0.0000")).Append(", during ")
              .Append(_phBaseDuring.ToString("0.0000")).Append(", after ")
              .Append(_phBaseAfterSamples > 0 ? _phBaseAfter.ToString("0.0000") : "<not resolved>")
              .Append(" (drop ").Append(baseDrop.ToString("0.0000")).Append(" = ")
              .Append((baseRel * 100f).ToString("0.0")).Append(" % relative, over ")
              .Append(_phBaseSamples).Append(" sample(s), recovery read over ")
              .Append(_phBaseAfterSamples).Append("), control patch during the blink ")
              .Append(_phBaseCtl.ToString("0.0000")).Append(", Y-MIRROR patch ")
              .Append(_phBaseMirrorBefore >= 0f ? _phBaseMirrorBefore.ToString("0.0000") : "?")
              .Append(" -> ")
              .Append(_phBaseMirrorDuring >= 0f ? _phBaseMirrorDuring.ToString("0.0000") : "?")
              .Append(". ");
            if (_phBaseAimTooDark)
            {
                sb.Append("*** THE PATCH WAS TOO DARK FOR THIS TEST TO BE ABLE TO SUCCEED: it "
                          + "carried ").Append(_phBaseBefore.ToString("0.0000"))
                  .Append(" of linear light, under the ").Append(PhMinBlinkLight.ToString("0.0000"))
                  .Append(" the video measures as the whole frame AT REST. Removing the prop from a "
                          + "patch this dark cannot move it by a measurable amount whatever the prop "
                          + "is doing. THAT IS AN INSTRUMENT LIMIT AND NOT A FINDING ABOUT THE PROP: "
                          + "the placement report above says where the patch was; the next round "
                          + "moves the patch or the window, it does not re-run this test. *** ");
            }
            sb.Append(baseWorks
                  ? "*** THE BLINK WORKS — the perturbation moved the picture by more than this "
                    + "instrument's own noise and by more than a twentieth of the light present, so "
                    + "the white blink below can be read. *** "
                  : "*** THE BLINK DID NOT CLEAR THE BAR. THREE CAUSES, AND TWO OF THEM ARE NOW "
                    + "ANSWERED ABOVE RATHER THAN LISTED: (a) THE PATCH IS NOT ON THE PROP — the "
                    + "Y-MIRROR column beside this reading is the test, because the mirrored height "
                    + "is read at the same instant and only one of the two heights can hold the "
                    + "prop; if the MIRROR fell and the prop patch did not, CopyTexture's source "
                    + "origin is flipped against WorldToViewportPoint and the fix is one "
                    + "subtraction. (b) forceRenderingOff does not reach these renderers — the "
                    + "roster line names how many of them DRAW at all, and a WRITES count spent on "
                    + "renderers with no materials is a write to nothing. (c) the prop was not drawn "
                    + "at that moment anyway. *** ");
        }

        // ---- THE ORIGIN BIT, READ OFF THE MIRROR COLUMN --------------------------------------
        sb.Append("Y-ORIGIN VERDICT: ");
        if (!baseRan || _phBaseMirrorBefore < 0f)
        {
            sb.Append("UNREAD — the baseline blink never fired, so neither height was perturbed. ");
        }
        else
        {
            float mirrorDrop = _phBaseMirrorBefore - _phBaseMirrorDuring;
            bool mirrorMoved = mirrorDrop >= blinkBar
                               && mirrorDrop / Mathf.Max(_phBaseMirrorBefore, 1e-6f) >= PhBlinkRelDrop;
            sb.Append("mirror drop ").Append(mirrorDrop.ToString("0.00000"))
              .Append(" against a bar of ").Append(blinkBar.ToString("0.00000")).Append(". ")
              .Append(mirrorMoved && !baseWorks
                  ? "*** THE Y AXIS IS FLIPPED. The prop is at the MIRRORED height: the perturbation "
                    + "moved the mirror patch and not the patch this instrument aims. Every reading "
                    + "this file has taken through a CopyTexture srcY is about the wrong row of the "
                    + "eye texture, and the remedy is to read at (renderHeight - patch - y). *** "
                  : baseWorks && !mirrorMoved
                      ? "*** THE Y AXIS IS NOT FLIPPED — the aimed patch moved and its mirror did "
                        + "not, so CopyTexture's srcY agrees with WorldToViewportPoint on this rig "
                        + "and this cause is DELETED. *** "
                      : baseWorks
                          ? "BOTH heights moved, so this test cannot separate them — either the "
                            + "prop spans both or the perturbation is frame-wide. "
                          : "NEITHER height moved, so the flip is NOT the cause: the patch is at the "
                            + "right row and cause (a) is deleted in favour of (b) or (c). ");
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
            float whiteRecover = _phWhiteAfterSamples > 0 ? _phWhiteAfter - _phWhiteDuring : -1f;
            bool recovered = whiteRecover >= 0f && whiteRecover >= 0.5f * whiteDrop;
            sb.Append("before ").Append(_phWhiteBefore.ToString("0.0000")).Append(", during ")
              .Append(_phWhiteDuring.ToString("0.0000")).Append(", after ")
              .Append(_phWhiteAfterSamples > 0 ? _phWhiteAfter.ToString("0.0000") : "<not resolved>")
              .Append(" (drop ").Append(whiteDrop.ToString("0.0000")).Append(" = ")
              .Append((whiteRel * 100f).ToString("0.0")).Append(" % relative, over ")
              .Append(_phWhiteSamples).Append(" sample(s), recovery read over ")
              .Append(_phWhiteAfterSamples).Append("), control patch during the blink ")
              .Append(_phWhiteCtl.ToString("0.0000")).Append(", Y-MIRROR patch ")
              .Append(_phWhiteMirrorBefore >= 0f ? _phWhiteMirrorBefore.ToString("0.0000") : "?")
              .Append(" -> ")
              .Append(_phWhiteMirrorDuring >= 0f ? _phWhiteMirrorDuring.ToString("0.0000") : "?")
              .Append(". *** THE A-B-A, WHICH IS WHY 'after' IS THERE: this blink fires ON THE RISE "
                      + "of an excursion, it lasts ").Append(PhBlinkFrames)
              .Append(" frames, and the video times the flash's own fall at under 0.04 s. A drop "
                      + "that does NOT come back was the event ending, not the perturbation working. "
                      + "Recovery here is ")
              .Append(whiteRecover >= 0f ? whiteRecover.ToString("0.0000") : "<not resolved>")
              .Append(" of a drop of ").Append(whiteDrop.ToString("0.0000")).Append(" — ")
              .Append(whiteRecover < 0f
                  ? "UNREAD, so this blink cannot be told apart from the event ending. "
                  : recovered
                      ? "the patch CAME BACK, so the drop was caused by the write. "
                      : "the patch did NOT come back, so the drop is the EVENT ENDING and this blink "
                        + "proves nothing about who painted it. ")
              .Append("*** ");
            if (!baseWorks)
            {
                sb.Append("NO VERDICT: the baseline blink did not establish that the perturbation "
                          + "reaches the picture, and a null result from a perturbation that never "
                          + "happened is not an exclusion. ");
            }
            else if (whiteRecover >= 0f && !recovered)
            {
                sb.Append("NO VERDICT: the patch did not recover after the write was handed back, so "
                          + "this drop is the excursion's own decay and not the bisection. ");
            }
            else if (rPropCtl >= 0.8f)
            {
                sb.Append("NO VERDICT: the control patch tracks the prop patch (r ")
                  .Append(rPropCtl.ToString("0.000"))
                  .Append("), so the excursion is not local to the prop and a perturbation of the "
                          + "prop's renderers is not a bisection of it. ");
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
