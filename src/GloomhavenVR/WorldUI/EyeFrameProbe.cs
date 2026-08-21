using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE FLICKER, ROUND 10 — THE FIRST INSTRUMENT IN THIS FAMILY THAT LOOKS AT THE PIXELS THE EYE
/// ACTUALLY RECEIVES.
///
/// <para>WHAT NINE ROUNDS ALREADY EXCLUDED BY MEASUREMENT, so that nothing here re-opens it:
/// <list type="bullet">
/// <item><see cref="PanelFlickerProbe"/> — the floated panels' own C# state, sampled at BOTH
/// MultiPass eye passes, silent across three hardware sessions.</item>
/// <item><see cref="CameraOrderProbe"/> — exactly one camera-order shape for a whole session, with
/// ZERO cameras rendering between the two eye passes. Both eyes therefore read identical
/// RenderTexture content.</item>
/// <item><see cref="RenderTargetProbe"/> — the character RenderTexture's own content, 567 of 567
/// consecutive AsyncGPUReadback frames BIT-IDENTICAL, mean luma range 0.0000. The source is
/// static.</item>
/// <item>ModBuild 189/190 — texture undersampling of MIPPED graphics: 474 of 475 graphics on the
/// shop window read >= 1.35x minification and ALL of them now read "mips=10 Trilinear aniso 8,
/// MIP-BAKED". The user reports the flicker unchanged.</item>
/// </list>
/// EVERY ONE OF THOSE PROBES MEASURED C# STATE OR A SOURCE TEXTURE. Not one of them ever compared
/// the PIXELS the left eye receives against the pixels the right eye receives. That is the hole
/// this class exists to close, and it is the hole this project has now paid for twice.</para>
///
/// <para>THE TWO LIVE HYPOTHESES AND WHY ONLY PIXELS CAN SEPARATE THEM.
/// <list type="number">
/// <item><b>H1 — A SCREEN-SPACE SHADING TERM</b> on the panel or its content: a dithered alpha, a
/// dissolve, an LOD crossfade, an ordered dither, a noise mask evaluated from SCREEN POSITION. In
/// MultiPass this is catastrophic and INVISIBLE TO EVERY STATE PROBE: the same world surface lands
/// at a different screen position in each eye, so each eye gets a different dither phase. The two
/// eyes are shown different pictures, the brain reads that as violent flicker, and a one-eye
/// screenshot shows only a static crosshatch. This project already ruled that screen-space dither
/// differs between the eyes — see the comment at
/// <c>unity/GloomhavenVR.Assets/Assets/Bundle/Environments/EnvBeam.shader:70</c>.</item>
/// <item><b>H2 — PLAIN SPATIAL ALIASING</b> of the artwork's own high-frequency detail, i.e. still
/// undersampling, of content that mips did not fix (mipless SDF text, uGUI geometry edges) or that
/// is not minified enough for a mip to help.</item>
/// </list>
/// They are separated by two independent readings this class takes:
/// <list type="bullet">
/// <item><b>PER-EYE DISAGREEMENT, SAME FRAME, SAME WORLD REGION.</b> The source content is known
/// static (RenderTargetProbe, 567/567 bit-identical). If the two eyes' pixels over the SAME piece
/// of the SAME surface in the SAME frame disagree by materially more than one eye disagrees with
/// ITSELF between two frames at a still head, the two eyes are being shown different pictures.
/// That is stereo rivalry and it is a positive finding on its own — note carefully that BOTH H1 and
/// severe H2 can produce it (aliasing is per-eye by construction too), which is why the second
/// reading exists.</item>
/// <item><b>SPECTRAL CLASSIFICATION of the captured patch.</b> An ordered/screen-space dither has a
/// SHARP, NARROW autocorrelation peak at a FIXED pixel period that does not move when the head
/// moves, and it is locked to the screen axes or to 45°. Aliasing of artwork has a broad spectrum
/// whose beat period SHIFTS as the sampling rate changes. The report prints the dominant period and
/// its strength SEPARATELY for a still head and for a moving head, so "does the period move?" is
/// read straight off one log line instead of being argued.</item>
/// </list></para>
///
/// <para>AND IT CARRIES A REFERENCE COLUMN, the same discipline
/// <see cref="PanelSamplingProbe"/> established: every burst captures the WORST-minified graphic
/// (the surface he reports) AND the BEST-sampled graphic on the same canvas (a surface he does
/// not), through the same two eyes in the same frames. If both read the same, the hypothesis dies
/// on its own numbers. The two rects come from <see cref="PanelSamplingProbe.SubjectRect"/> and
/// <see cref="PanelSamplingProbe.ReferenceRect"/> so the two log lines name the same surfaces.</para>
///
/// <para>HOW THE PIXELS ARE OBTAINED. One <see cref="CommandBuffer"/> is attached to the mod's head
/// camera at <see cref="CameraEvent.AfterEverything"/>. In MultiPass that event fires once per eye
/// pass, and <c>Camera.onPreRender</c> — which runs before it, in the same pass — re-records the
/// buffer for THAT eye: a <c>Blit</c> from <see cref="BuiltinRenderTextureType.CurrentActive"/>
/// (the eye's own render target, MSAA-resolved by the blit) into a mod-owned
/// <see cref="PatchSize"/>x<see cref="PatchSize"/> RenderTexture, immediately followed by
/// <c>CommandBuffer.RequestAsyncReadback</c>. Because the copy and the readback are enqueued into
/// the command stream TOGETHER, readback N always carries the pixels blit N produced, and the
/// callbacks complete in submission order — so one shared small target serves every capture and a
/// FIFO pairs each callback with its own request. That is the identical argument
/// <see cref="RenderTargetProbe"/>'s CONTENT block makes, and it is why there is no
/// <c>ReadPixels</c> anywhere on this path: a blocking readback would flush the pipeline every
/// frame and change the frame timing of the very thing being measured.</para>
///
/// <para>THE CAPTURE IS A 1:1 CROP, NOT A DOWNSCALE, and that is not a detail. The pattern being
/// hunted has a period of about eight SCREEN pixels. Squeezing a 700x500 rendered graphic into a
/// 64x64 patch would push that period to 0.7 px and destroy the very signal the probe exists to
/// find. So the blit takes a window of exactly <see cref="PatchSize"/> EYE PIXELS around the
/// surface's projected centre, and the window origin is SNAPPED to the eye texture's texel grid so
/// the copy is a copy and not a resample — otherwise the resampling itself would manufacture a
/// per-eye difference and the instrument would confirm rivalry on a perfect render.</para>
///
/// <para>THE SELF-TEST IS NOT OPTIONAL AND IT GATES EVERYTHING. ModBuild 186 shipped an A-B-A test
/// whose code was the exact inverse of its own doc comment; it named a wrong root cause with
/// confidence and cost two builds. So before a single verdict about a real panel is printed:
/// <list type="number">
/// <item>A SYNTHETIC self-test runs at arm time, on the comparator and the classifier, against a
/// KNOWN-TRUE and a KNOWN-FALSE case each: identical buffers must report NO difference; buffers
/// differing by a known amount must report that amount; a synthetic period-8 stripe pattern must
/// report period 8.0 with high strength; a synthetic 45° pattern must report its period through the
/// diagonal axis; and deterministic hash NOISE must report LOW strength and must NOT report a
/// period of 8. If any of the five fails, the probe writes ONE Warn naming the consequence and
/// DISABLES ITSELF.</item>
/// <item>A LIVE self-test runs on real GPU pixels: the same eye, the same frame, captured twice —
/// once at the true rect and once at a rect deliberately offset by
/// <see cref="SelfTestOffsetPixels"/> px. The unoffset pair must be bit-identical (proving the
/// readback path and the FIFO pairing are not scrambled) and the offset one must DIFFER (proving
/// the blit actually honours the requested rectangle and the readback is returning live varying
/// pixels rather than a constant). It is INCONCLUSIVE, never a failure, when the patch is too flat
/// for an offset to change anything — that condition is stated on the line.</item>
/// </list>
/// Both results are printed on the armed line and repeated on every baseline.</para>
///
/// <para>A BASELINE EVERY <see cref="SummaryIntervalSeconds"/> SECONDS whether or not anything was
/// found, exactly like <see cref="RenderTargetProbe"/>: how many patches were captured, how many
/// readbacks failed, what the self-tests say, and what the current verdict is. A silent probe must
/// never be readable as "nothing looked".</para>
///
/// <para>COST. Bursts are throttled to <see cref="BurstsPerSecond"/> per second. A burst is two
/// CONSECUTIVE frames (it has to be: the temporal control is frame N vs frame N+1) and captures at
/// most eight <see cref="PatchSize"/>x<see cref="PatchSize"/> RGBA patches, i.e. ~32 patches/s or
/// ~512 KB/s off the render thread — noise beside one 3072x3264 per-eye frame. The command buffer
/// is CLEARED on every eye pass and only re-recorded when a capture is due, so a non-capturing pass
/// costs one empty buffer execution. The measured per-capture record cost is printed on the
/// baseline in microseconds. The probe stands completely down when no floated panel is up.</para>
///
/// <para>MULTIPLAYER: this is a purely LOCAL diagnostic. It reads pixels the local headset is
/// already rendering, writes nothing the game owns, adds no Harmony patch, sends nothing on the
/// wire and changes no value any other peer can observe. It is MP-safe by construction.</para>
///
/// <para>DEGRADATION. Every failure path writes one Warn naming the CONSEQUENCE and then stands
/// down: no AsyncGPUReadback support, no head camera, no eye target size, a failed target creation,
/// a throw anywhere in the record path. Nothing throws out of the render loop.</para>
/// </summary>
internal static class EyeFrameProbe
{
    private const string Scope = "WorldUI";

    /// <summary>Side of the captured patch, in EYE PIXELS (the crop is 1:1 — see the class doc).
    /// 64 px is four full periods of the ~8 px pattern in the user's photograph on each axis, which
    /// is the minimum an autocorrelation can call sharp, and 16 KB per readback.</summary>
    private const int PatchSize = 64;

    /// <summary>Capture bursts per second. A burst is two consecutive frames; between bursts the
    /// command buffer is empty and this class costs nothing.</summary>
    private const float BurstsPerSecond = 4f;

    /// <summary>How often the baseline is printed, hit or no hit.</summary>
    private const float SummaryIntervalSeconds = 10f;

    /// <summary>Readbacks allowed in flight before a burst is skipped. A readback lands 2-3 frames
    /// after its request and a burst issues up to eight; 24 covers three bursts of latency and
    /// bounds the queue if a driver stalls.</summary>
    private const int MaxInFlight = 24;

    /// <summary>
    /// HEAD-STILLNESS GATE, in REAL metres and degrees PER FRAME. The temporal comparison (frame N
    /// vs frame N+1, one eye) is meaningless while the head moves — every head movement trivially
    /// changes the pixels — so it is only ever REPORTED below these deltas, and the gate values are
    /// printed on the baseline so the reader can judge them rather than trust them. 0.6 mm/frame at
    /// 90 Hz is 5.4 cm/s, which is a head that is being held still and not one that is locked in a
    /// vice; 0.05°/frame is 4.5°/s.
    /// </summary>
    private const float StillPositionMetres = 0.0006f;

    /// <summary>Companion to <see cref="StillPositionMetres"/> — see it for the reasoning.</summary>
    private const float StillRotationDegrees = 0.05f;

    /// <summary>
    /// A stereo difference is only called RIVALRY when it is at least this many times the temporal
    /// difference the SAME eye shows between two frames at a still head. The temporal number is the
    /// probe's own noise floor — it contains the readback noise, any residual sub-texel snap error
    /// and any genuine per-frame content change — so comparing against it rather than against an
    /// absolute constant is what stops this instrument from confirming rivalry on a perfect render.
    /// </summary>
    private const float RivalryFactor = 3f;

    /// <summary>Absolute floor under the rivalry verdict, as a fraction of full range. Below 1 part
    /// in 250 the two eyes are the same picture no matter what the ratio says — this is the guard
    /// against a pathologically tiny temporal denominator turning noise into a verdict.</summary>
    private const float RivalryFloor = 0.004f;

    /// <summary>Autocorrelation lag at or above which a peak is a candidate period. Lags 1-2 are
    /// dominated by the point-spread of any resample and by JPEG-free but still smooth content.</summary>
    private const int MinLag = 3;

    /// <summary>Largest lag searched. 20 covers periods to 20 px on the screen axes and 28 px on
    /// the diagonals, which brackets everything from a 4x4 Bayer cell to a slow moiré beat.</summary>
    private const int MaxLag = 20;

    /// <summary>Normalized autocorrelation at the dominant lag at or above which the pattern is
    /// called SHARP, i.e. consistent with an ordered/screen-space term rather than broadband
    /// content aliasing. Deliberately high: a false "ordered" verdict is the expensive one.</summary>
    private const float SharpPeakStrength = 0.45f;

    /// <summary>How far the live self-test's second capture is displaced, in eye pixels. Big enough
    /// that no plausible content is self-similar across it, small enough to stay on the surface.</summary>
    private const int SelfTestOffsetPixels = 17;

    /// <summary>Live self-test attempts before it is declared unable to conclude (the patch is flat
    /// every time it looks). It then stops costing captures and says so.</summary>
    private const int SelfTestMaxAttempts = 24;

    /// <summary>Patch standard deviation (0..1) below which the live self-test cannot conclude: an
    /// offset capture of a flat surface is legitimately identical to the unoffset one.</summary>
    private const float SelfTestMinStdDev = 0.01f;

    // ---- slots -------------------------------------------------------------------------------

    /// <summary>The eight things a burst can capture. Frame A and frame B are separate slots on
    /// purpose: the temporal control compares SubjectLeftA against SubjectLeftB, so frame A's patch
    /// must survive frame B's arrival.</summary>
    private enum Slot
    {
        SubjectLeftA = 0,
        SubjectRightA = 1,
        SubjectLeftB = 2,
        SubjectRightB = 3,
        ReferenceLeftA = 4,
        ReferenceRightA = 5,
        SelfTestPlain = 6,
        SelfTestOffset = 7,
    }

    private const int SlotCount = 8;

    private sealed class Patch
    {
        internal bool Valid;
        internal int BurstId = -1;
        internal readonly byte[] Rgb = new byte[PatchSize * PatchSize * 3];
        internal readonly float[] Luma = new float[PatchSize * PatchSize];
        internal float Mean;
        internal float StdDev;
    }

    private readonly struct Pending
    {
        internal readonly int Gen;
        internal readonly int BurstId;
        internal readonly Slot Slot;

        internal Pending(int gen, int burstId, Slot slot)
        {
            Gen = gen;
            BurstId = burstId;
            Slot = slot;
        }
    }

    // ---- state -------------------------------------------------------------------------------

    private static bool _armed;
    private static bool _disabled;          // self-test failed, or an unrecoverable capture fault
    private static bool _hooked;
    private static Camera? _bound;
    private static CommandBuffer? _cb;
    private static RenderTexture? _patchRt;
    private static int _gen;
    private static int _inFlight;

    private static readonly Patch[] Patches = NewPatches();
    private static readonly Queue<Pending> PendingReads = new();
    private static readonly StringBuilder Sb = new(2048);

    // Burst scheduling.
    //
    // THE OUTSTANDING COUNT IS INCREMENTED ONLY BY AN ACTUAL ENQUEUE, never in advance. A count
    // predicted at burst start and reconciled later is how a probe wedges: one skipped capture (the
    // window left the eye target, the subject rect went null between the two frames, the runtime
    // rendered a single pass) leaves the count permanently above zero and Judge never runs again —
    // silently, which is the failure this whole family exists to avoid. The burst is CLOSED when
    // its two frames are over; Judge fires when the closed burst's last readback lands, and
    // <see cref="StuckBurstFrames"/> guarantees the machine can never stay closed forever.
    private enum BurstState { Idle, FrameA, FrameB }
    private static BurstState _burst = BurstState.Idle;
    private static int _burstId;
    private static int _burstFrame = -1;
    private static int _burstOutstanding;
    private static bool _burstClosed;
    private static int _closedBurstId = -1;
    private static int _closedFrame = -1;
    private static bool _closedStill;
    private static float _closedMoveMm;
    private static float _closedMoveDeg;
    private static float _nextBurstTime;

    /// <summary>Frames a closed burst may wait for its readbacks before they are written off. A
    /// readback lands 2-3 frames after its request; 30 frames is a third of a second at 90 Hz and an
    /// eternity for a driver that is going to answer at all.</summary>
    private const int StuckBurstFrames = 30;

    // Per-frame eye-pass bookkeeping (the PanelFlickerProbe/FlatScreenStereo pattern).
    private static int _passFrame = -1;
    private static int _passIndex;
    private static bool _stereoEyeTrusted = true;
    private static int _eyeObservations;

    // Head stillness.
    private static Vector3 _lastHeadPos;
    private static Quaternion _lastHeadRot = Quaternion.identity;
    private static bool _haveLastHead;
    private static bool _frameStill;
    private static Vector3 _burstHeadPos;
    private static Quaternion _burstHeadRot = Quaternion.identity;
    private static bool _burstStill;
    private static float _burstMoveMm;
    private static float _burstMoveDeg;
    private static bool _burstFrameAStill;

    // Accounting for the baseline.
    private static int _bursts;
    private static int _captured;
    private static int _readErrors;
    private static int _skippedOffTarget;
    private static int _skippedNoSubject;
    private static float _nextSummary;
    private static long _recordTicks;
    private static int _recordSamples;

    // Verdict accumulators (window-scoped, reset on every baseline).
    private static int _stereoSamples;
    private static float _stereoSum;
    private static float _stereoMax;
    private static int _refStereoSamples;
    private static float _refStereoSum;
    private static int _temporalSamples;
    private static float _temporalSum;
    private static float _temporalMax;
    private static int _rivalryHits;
    private static string _lastVerdict = "no comparison completed yet";

    // Spectral accumulators, split by head state — this split IS the H1/H2 test.
    private static int _stillSpectra;
    private static float _stillPeriodSum;
    private static float _stillPeriodMin = float.MaxValue;
    private static float _stillPeriodMax = float.MinValue;
    private static float _stillStrengthSum;
    private static string _stillAxis = "n/a";
    private static int _moveSpectra;
    private static float _movePeriodSum;
    private static float _movePeriodMin = float.MaxValue;
    private static float _movePeriodMax = float.MinValue;
    private static float _moveStrengthSum;
    private static string _moveAxis = "n/a";

    // Self-tests.
    private static bool _syntheticPassed;
    private static string _syntheticReport = "not run";
    private static int _liveAttempts;
    private static bool _livePassed;
    private static bool _liveFailed;
    private static string _liveReport = "pending (no live self-test capture has completed yet)";

    // One-shot latches — a consequence is stated once, never per frame.
    private static bool _noReadbackLogged;
    private static bool _noTargetLogged;
    private static bool _noHeadLogged;
    private static bool _recordFaultLogged;
    private static bool _inventoryLogged;
    private static string _inventorySignature = string.Empty;

    private static Patch[] NewPatches()
    {
        var p = new Patch[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            p[i] = new Patch();
        return p;
    }

    // =============================================================================================
    // ARM / DISARM
    // =============================================================================================

    /// <summary>
    /// Arm/disarm with the floated-window layer. Called every frame from
    /// <see cref="PanelSamplingProbe.Tick"/> (see that call site for why not from the tick seam).
    /// Always safe to call; does nothing at all while no panel is floated.
    /// </summary>
    internal static void Tick(bool wanted)
    {
        if (_disabled)
            return;

        if (!wanted)
        {
            if (_armed)
            {
                Baseline("STAND-DOWN");
                Disarm();
            }
            return;
        }

        if (!_armed)
        {
            if (!Arm())
                return;
        }

        // The head camera is rebuilt whenever the rig is (map room seat changes, scene loads), and
        // a command buffer on a destroyed camera is simply gone. Rebind rather than go quietly
        // blind — a probe that stops looking without saying so is the failure mode this whole
        // family exists to avoid. The explicit fake-null collapse matters: a DESTROYED Camera is
        // still a live managed reference, so ReferenceEquals would happily match it against _bound
        // forever and the probe would never notice the rig had been rebuilt under it.
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
            head = null;
        if (!ReferenceEquals(head, _bound))
            Rebind(head);

        // LIVENESS. A closed burst whose readbacks never arrive (a lost request, a device reset)
        // must not hold the machine shut. The losses are written off as readback errors so they
        // appear on the baseline rather than vanishing.
        if (_burstClosed && Time.frameCount - _closedFrame > StuckBurstFrames)
        {
            _readErrors += _burstOutstanding;
            _burstOutstanding = 0;
            FinishBurst();
        }

        if (Time.unscaledTime >= _nextSummary)
        {
            _nextSummary = Time.unscaledTime + SummaryIntervalSeconds;
            Baseline("BASELINE");
        }
    }

    /// <summary>Full teardown: hook off, readbacks drained, mod-owned target released. Called from
    /// <see cref="PanelSamplingProbe.Shutdown"/>, which the module's Shutdown drives.</summary>
    internal static void Shutdown()
    {
        Disarm();
        if (_inFlight > 0)
        {
            // A one-time stall on a path that is already tearing the module down — the price of
            // never destroying a RenderTexture a driver is still copying out of.
            AsyncGPUReadback.WaitAllRequests();
            _inFlight = 0;
        }
        PendingReads.Clear();
        if (_patchRt != null)
        {
            _patchRt.Release();
            UnityEngine.Object.Destroy(_patchRt);
            _patchRt = null;
        }
        if (_cb != null)
        {
            _cb.Release();
            _cb = null;
        }
    }

    private static bool Arm()
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            _disabled = true;
            if (!_noReadbackLogged)
            {
                _noReadbackLogged = true;
                VRLog.Warn(Scope, "EYE FRAME PROBE stood down before arming: this graphics device "
                                  + $"({SystemInfo.graphicsDeviceType}) reports no AsyncGPUReadback "
                                  + "support. THE CONSEQUENCE is that round 10 has no per-eye pixel "
                                  + "comparison at all — the log will carry no EYE FRAME lines and "
                                  + "the H1 (screen-space dither) / H2 (content aliasing) question "
                                  + "stays open on this machine. Every other probe is unaffected. A "
                                  + "blocking ReadPixels is deliberately NOT substituted: it would "
                                  + "flush the pipeline every frame and change the frame timing of "
                                  + "the very thing being measured.");
            }
            return false;
        }

        // THE SELF-TEST GATES EVERYTHING. Nothing about a real panel is reported until the
        // comparator and the classifier have each been shown a known-true and a known-false case.
        if (!_syntheticPassed)
        {
            _syntheticPassed = RunSyntheticSelfTest(out _syntheticReport);
            if (!_syntheticPassed)
            {
                _disabled = true;
                VRLog.Warn(Scope, "EYE FRAME PROBE DISABLED ITSELF: its own synthetic self-test "
                                  + $"failed — {_syntheticReport}. THE CONSEQUENCE is that no "
                                  + "per-eye pixel comparison and no spectral classification are "
                                  + "performed this session, and any EYE FRAME verdict that might "
                                  + "have been printed would have been unreliable. This is "
                                  + "deliberate: ModBuild 186 shipped an A-B-A test whose code was "
                                  + "the exact inverse of its own doc comment, named a wrong root "
                                  + "cause with confidence and cost two builds. An instrument that "
                                  + "cannot pass a known-true and a known-false case must not be "
                                  + "allowed to speak. Nothing was written and nothing else changes.");
                return false;
            }
        }

        if (!EnsurePatchTarget())
        {
            _disabled = true;
            return false;
        }

        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
        {
            if (!_noHeadLogged)
            {
                _noHeadLogged = true;
                VRLog.Warn(Scope, "EYE FRAME PROBE: no head camera (VRRigDriver.HeadCamera is null) "
                                  + "at arm time. THE CONSEQUENCE is that no eye pixels can be "
                                  + "captured this frame; the probe retries every frame and arms "
                                  + "itself as soon as the rig exists. Nothing else changes.");
            }
            return false;
        }

        _armed = true;
        _gen++;
        ResetWindow();
        _nextSummary = Time.unscaledTime + SummaryIntervalSeconds;
        _nextBurstTime = Time.unscaledTime;
        Rebind(head);

        VRLog.Info(Scope, "EYE FRAME PROBE armed (ModBuild 191 — THE FIRST INSTRUMENT IN THIS "
                          + "FAMILY THAT READS THE PIXELS THE EYE ACTUALLY RECEIVES). Rounds 1-9 all "
                          + "measured C# state or a source texture and all of them said 'steady': "
                          + "the panels' state, the camera order, the character RenderTexture's own "
                          + "content (567/567 bit-identical) and, since 189/190, the minification of "
                          + "every mipped graphic. None of them ever compared LEFT-EYE pixels with "
                          + "RIGHT-EYE pixels. This one does: every burst it copies a "
                          + $"{PatchSize}x{PatchSize} EYE-PIXEL window (a 1:1 crop, NOT a downscale "
                          + "— an ~8 px pattern would not survive a downscale) out of each eye's own "
                          + "render target over THE SAME WORLD REGION of the same surface, in the "
                          + "SAME frame, and asks two questions. (1) DO THE EYES DISAGREE by more "
                          + "than one eye disagrees with itself between two frames at a still head? "
                          + "On a surface whose source content is known static that is STEREO "
                          + "RIVALRY. (2) IS THE PATTERN ORDERED OR BROADBAND? A screen-space dither "
                          + "has a sharp autocorrelation peak at a FIXED period that does not move "
                          + "when the head moves; content aliasing has a broad spectrum whose beat "
                          + "period SHIFTS with the sampling rate — so the period is reported "
                          + "SEPARATELY for a still head and a moving head and the question is read "
                          + "off one line. SELF-TEST (synthetic, comparator + classifier, "
                          + $"known-true and known-false): {_syntheticReport}. LIVE self-test "
                          + $"(same eye, same frame, offset rect): {_liveReport}. The subject and "
                          + "the reference surface are taken from the PANEL SAMPLING line's own "
                          + "worst/best pick so the two lines describe the same two graphics. "
                          + "Baseline every " + $"{SummaryIntervalSeconds:F0}s whether or not "
                          + "anything is found. This is a LOCAL diagnostic: it writes nothing the "
                          + "game owns and nothing on the wire.");
        // The enumeration belongs NEXT TO the armed line, not ten seconds after it: nine rounds
        // argued about which shaders and which image effects were in play without ever writing
        // them down. It re-prints whenever the material set changes (a window repopulates).
        Inventory();
        return true;
    }

    private static void Disarm()
    {
        if (_hooked)
        {
            Camera.onPreRender -= OnPreRender;
            _hooked = false;
        }
        if (_bound != null && _cb != null)
            _bound.RemoveCommandBuffer(CameraEvent.AfterEverything, _cb);
        _cb?.Clear();
        _bound = null;
        _armed = false;
        _burst = BurstState.Idle;
        _burstClosed = false;
        _burstOutstanding = 0;
        _haveLastHead = false;
        for (int i = 0; i < SlotCount; i++)
            Patches[i].Valid = false;
        // Bump the generation so any readback still in flight is discarded on arrival instead of
        // being folded into the next arming's statistics. The FIFO is NOT cleared: it is what pairs
        // each callback with its request, and dropping entries would mispair every later readback.
        _gen++;
    }

    private static void Rebind(Camera? head)
    {
        if (_bound != null && _cb != null)
            _bound.RemoveCommandBuffer(CameraEvent.AfterEverything, _cb);
        _bound = head;
        if (head == null || _cb == null)
            return;
        head.AddCommandBuffer(CameraEvent.AfterEverything, _cb);
        if (!_hooked)
        {
            Camera.onPreRender += OnPreRender;
            _hooked = true;
        }
    }

    private static bool EnsurePatchTarget()
    {
        _cb ??= new CommandBuffer { name = "GloomhavenVR.EyeFrameProbe" };
        if (_patchRt != null)
            return true;
        _patchRt = new RenderTexture(PatchSize, PatchSize, 0, RenderTextureFormat.ARGB32)
        {
            name = "GloomhavenVR.EyeFrameProbe.Patch",
            antiAliasing = 1,
            filterMode = FilterMode.Point,
            useMipMap = false,
            autoGenerateMips = false,
        };
        if (_patchRt.Create())
            return true;
        _patchRt.Release();
        UnityEngine.Object.Destroy(_patchRt);
        _patchRt = null;
        if (!_noTargetLogged)
        {
            _noTargetLogged = true;
            VRLog.Warn(Scope, $"EYE FRAME PROBE stood down: the {PatchSize}x{PatchSize} capture "
                              + "target could not be created. THE CONSEQUENCE is that no eye pixels "
                              + "are read back this session, so the H1 (screen-space dither) / H2 "
                              + "(content aliasing) question is not answered here; every other probe "
                              + "is unaffected and nothing the game owns was written.");
        }
        return false;
    }

    // =============================================================================================
    // CAPTURE — inside the render loop. RECORDS ONLY. Nothing here writes a transform, a material
    // or any visibility state: this project's AssertNotInRenderPhase rule says a write here lands
    // in ONE EYE, which is the exact bug class this class is chasing.
    // =============================================================================================

    private static void OnPreRender(Camera cam)
    {
        if (!_armed || _disabled || cam == null || !ReferenceEquals(cam, _bound) || _cb == null)
            return;
        try
        {
            _cb.Clear(); // a non-capturing pass must cost one empty buffer, never a stale capture
            RecordPass(cam);
        }
        catch (Exception ex)
        {
            _disabled = true;
            if (!_recordFaultLogged)
            {
                _recordFaultLogged = true;
                VRLog.Warn(Scope, $"EYE FRAME PROBE stood down after a capture fault in the render "
                                  + $"loop ({ex.GetType().Name}: {ex.Message}). THE CONSEQUENCE is "
                                  + "that no further per-eye pixel comparison is made this session; "
                                  + "the command buffer is left empty, nothing the game owns was "
                                  + "written and every other probe is unaffected.");
            }
            _cb.Clear();
        }
    }

    private static void RecordPass(Camera cam)
    {
        // Which eye is this pass? stereoActiveEye is the primary source, exactly as
        // FlatScreenStereo.4.PerEye does it, with the same per-frame pass-parity fallback for a
        // runtime that reports Mono on a stereo camera — and the same one-time line saying which
        // of the two the log is actually built on, so no future round has to guess.
        if (Time.frameCount != _passFrame)
        {
            _passFrame = Time.frameCount;
            _passIndex = 0;
            BeginFrame(cam);
        }
        else
        {
            _passIndex++;
        }

        Camera.MonoOrStereoscopicEye reported = cam.stereoActiveEye;
        if (_eyeObservations < 2)
        {
            _eyeObservations++;
            if (_eyeObservations == 2)
            {
                _stereoEyeTrusted = reported != Camera.MonoOrStereoscopicEye.Mono;
                VRLog.Info(Scope, "EYE FRAME PROBE eye identification: Camera.stereoActiveEye "
                                  + $"reported '{reported}' on the second head pass of a frame. "
                                  + (_stereoEyeTrusted
                                      ? "stereoActiveEye is therefore trusted to name the eye, and "
                                        + "every per-eye number below is labelled from it."
                                      : "It reported Mono on a stereo camera, so the probe falls "
                                        + "back to PASS PARITY (first pass of a frame = left). The "
                                        + "comparison is unaffected — it only needs the two passes "
                                        + "of one frame to be told apart, not to be named correctly "
                                        + "— but a left/right label in a later line may be swapped."));
            }
        }

        bool right = _stereoEyeTrusted
            ? reported == Camera.MonoOrStereoscopicEye.Right
            : (_passIndex & 1) == 1;

        if (_burst == BurstState.Idle || _passIndex > 1)
            return;

        RectTransform? subject = PanelSamplingProbe.SubjectRect;
        if (subject == null)
            return;

        Camera.MonoOrStereoscopicEye eye = right
            ? Camera.MonoOrStereoscopicEye.Right
            : Camera.MonoOrStereoscopicEye.Left;

        long start = Stopwatch.GetTimestamp();

        if (_burst == BurstState.FrameA)
        {
            Capture(cam, subject, eye, right ? Slot.SubjectRightA : Slot.SubjectLeftA, 0);
            RectTransform? reference = PanelSamplingProbe.ReferenceRect;
            if (reference != null && !ReferenceEquals(reference, subject))
                Capture(cam, reference, eye, right ? Slot.ReferenceRightA : Slot.ReferenceLeftA, 0);
            // LIVE SELF-TEST: same eye, same frame, same source — one capture at the true rect and
            // one displaced by a known number of pixels. Left eye only, and only while it has not
            // concluded, so it costs nothing for the rest of the session.
            if (!right && !_livePassed && !_liveFailed && _liveAttempts < SelfTestMaxAttempts)
            {
                _liveAttempts++;
                Capture(cam, subject, eye, Slot.SelfTestPlain, 0);
                Capture(cam, subject, eye, Slot.SelfTestOffset, SelfTestOffsetPixels);
            }
        }
        else
        {
            Capture(cam, subject, eye, right ? Slot.SubjectRightB : Slot.SubjectLeftB, 0);
        }

        _recordTicks += Stopwatch.GetTimestamp() - start;
        _recordSamples++;
    }

    /// <summary>First head pass of a frame: measure head stillness, and advance the burst state
    /// machine. Both belong here and not in Update — the head transform is settled for THIS frame's
    /// render by the time onPreRender runs, and the burst has to line up with rendered frames.</summary>
    private static void BeginFrame(Camera cam)
    {
        Transform t = cam.transform;
        Vector3 pos = t.position;
        Quaternion rot = t.rotation;
        float scale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);
        if (_haveLastHead)
        {
            float moveM = Vector3.Distance(pos, _lastHeadPos) / scale;
            float turnDeg = Quaternion.Angle(rot, _lastHeadRot);
            _frameStill = moveM <= StillPositionMetres && turnDeg <= StillRotationDegrees;
        }
        else
        {
            _frameStill = false;
        }
        _lastHeadPos = pos;
        _lastHeadRot = rot;
        _haveLastHead = true;

        switch (_burst)
        {
            case BurstState.FrameA when Time.frameCount == _burstFrame + 1:
                // Frame B must be the very NEXT rendered frame or the temporal control is not a
                // frame-to-frame measurement at all. A dropped frame aborts the burst (below).
                _burst = BurstState.FrameB;
                _burstFrame = Time.frameCount;
                _burstMoveMm = Vector3.Distance(pos, _burstHeadPos) / scale * 1000f;
                _burstMoveDeg = Quaternion.Angle(rot, _burstHeadRot);
                _burstStill = _burstMoveMm <= StillPositionMetres * 1000f
                              && _burstMoveDeg <= StillRotationDegrees;
                return;

            case BurstState.FrameA:
            case BurstState.FrameB:
                CloseBurst();
                return;
        }

        // A new burst never starts while the previous one is still waiting for readbacks: one
        // shared outstanding count cannot describe two bursts, and the patch slots would be
        // overwritten under the comparison that is about to read them.
        if (Time.unscaledTime < _nextBurstTime || _burstClosed || _inFlight >= MaxInFlight)
            return;
        if (PanelSamplingProbe.SubjectRect == null)
        {
            _skippedNoSubject++;
            _nextBurstTime = Time.unscaledTime + 1f / BurstsPerSecond;
            return;
        }
        _burst = BurstState.FrameA;
        _burstFrame = Time.frameCount;
        _burstId++;
        _bursts++;
        _burstHeadPos = pos;
        _burstHeadRot = rot;
        _burstStill = false;
        // Snapshot frame A's own stillness NOW. The spectral bucket is decided by the head state at
        // CAPTURE time, and Judge runs two or three frames later, by which time the live
        // _frameStill describes a different frame entirely.
        _burstFrameAStill = _frameStill;
    }

    /// <summary>The burst's frames are over. If every readback has already landed, judge here;
    /// otherwise the last arriving readback does it.</summary>
    private static void CloseBurst()
    {
        _burst = BurstState.Idle;
        _burstClosed = true;
        _closedBurstId = _burstId;
        _closedFrame = Time.frameCount;
        _closedStill = _burstStill;
        _closedMoveMm = _burstMoveMm;
        _closedMoveDeg = _burstMoveDeg;
        _nextBurstTime = Time.unscaledTime + 1f / BurstsPerSecond;
        if (_burstOutstanding == 0)
            FinishBurst();
    }

    private static void FinishBurst()
    {
        _burstClosed = false;
        Judge(_closedBurstId);
    }

    /// <summary>
    /// Blit a <see cref="PatchSize"/>-pixel window of the CURRENT EYE TARGET, centred on
    /// <paramref name="rect"/>'s world centre as projected through <paramref name="eye"/>'s own
    /// matrices, into the mod-owned patch target, and enqueue its readback in the same command
    /// stream position. <paramref name="offsetPx"/> displaces the window for the live self-test.
    /// </summary>
    private static void Capture(Camera cam, RectTransform rect, Camera.MonoOrStereoscopicEye eye,
        Slot slot, int offsetPx)
    {
        if (_cb == null || _patchRt == null)
            return;
        if (!TryPatchUv(cam, rect, eye, offsetPx, out Vector2 uvMin, out Vector2 uvSize))
        {
            _skippedOffTarget++;
            Patches[(int)slot].Valid = false; // never let a stale patch stand in for a skipped one
            return;
        }

        // Blit and readback go into the stream TOGETHER, so readback N carries exactly the pixels
        // blit N produced and one shared target serves every capture (RenderTargetProbe's argument;
        // see the class doc). CurrentActive is the eye's own render target and the blit resolves it.
        _cb.Blit(BuiltinRenderTextureType.CurrentActive, _patchRt, uvSize, uvMin);
        PendingReads.Enqueue(new Pending(_gen, _burstId, slot));
        _inFlight++;
        _captured++;
        _burstOutstanding++;
        _cb.RequestAsyncReadback(_patchRt, 0, TextureFormat.RGBA32, OnRead);
    }

    /// <summary>
    /// The eye-texture UV window for one capture. Returns false when the window would leave the
    /// render target or the surface is behind the eye — clamping it instead would silently move the
    /// world region and make the two eyes' patches describe different pieces of the surface, which
    /// is precisely the error this comparison cannot survive.
    ///
    /// <para>The origin is SNAPPED to the eye texture's texel grid so the blit is a copy and not a
    /// resample. Without that snap the bilinear resample would differ between the eyes by
    /// construction and the instrument would report rivalry on a perfect render — the single most
    /// likely way for this class to become another ModBuild 186. The cost of the snap is that the
    /// two eyes' windows can be misaligned against the world region by up to one pixel, which is
    /// far below any pattern this probe is looking for.</para>
    /// </summary>
    private static bool TryPatchUv(Camera cam, RectTransform rect, Camera.MonoOrStereoscopicEye eye,
        int offsetPx, out Vector2 uvMin, out Vector2 uvSize)
    {
        uvMin = default;
        uvSize = default;
        if (rect == null)
            return false;
        if (!TryEyeTargetPixels(out float targetW, out float targetH, out float viewportScale))
            return false;

        Vector3 centre = rect.TransformPoint(rect.rect.center);
        Vector3 vp = cam.WorldToViewportPoint(centre, eye);
        if (vp.z <= 0f)
            return false;

        // Viewport [0,1] covers the camera's viewport, which occupies the lower-left
        // viewportScale-fraction of the eye texture. Texture UV is therefore viewport UV scaled.
        float px = vp.x * viewportScale * targetW + offsetPx;
        float py = vp.y * viewportScale * targetH + offsetPx;
        float half = PatchSize * 0.5f;
        float x0 = Mathf.Round(px - half);
        float y0 = Mathf.Round(py - half);
        if (x0 < 0f || y0 < 0f || x0 + PatchSize > targetW || y0 + PatchSize > targetH)
            return false;

        uvMin = new Vector2(x0 / targetW, y0 / targetH);
        uvSize = new Vector2(PatchSize / targetW, PatchSize / targetH);
        return true;
    }

    /// <summary>The eye render target in pixels plus the live viewport scale — the same levers
    /// PanelSamplingProbe.TryEyeTarget reads, so the two lines are directly comparable. Falls back
    /// to the desktop window outside XR (dev mode), where both "eyes" are the same pass and the
    /// stereo half of this probe correctly reports nothing.</summary>
    private static bool TryEyeTargetPixels(out float pxW, out float pxH, out float viewportScale)
    {
        viewportScale = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        int w = XRSettings.eyeTextureWidth;
        int h = XRSettings.eyeTextureHeight;
        if (w < 2 || h < 2)
        {
            w = Screen.width;
            h = Screen.height;
            viewportScale = 1f;
        }
        pxW = w;
        pxH = h;
        return pxW >= PatchSize * 2f && pxH >= PatchSize * 2f;
    }

    // =============================================================================================
    // READBACK — main thread, FIFO-paired with the requests. Touches nothing the game owns.
    // =============================================================================================

    private static void OnRead(AsyncGPUReadbackRequest req)
    {
        if (PendingReads.Count == 0)
            return; // cannot happen while the queue is never cleared out of order; belt and braces
        Pending pending = PendingReads.Dequeue();
        if (_inFlight > 0)
            _inFlight--;
        if (pending.Gen != _gen)
            return; // from a previous arming — discarded rather than folded into these numbers

        bool ok = false;
        if (req.hasError)
        {
            _readErrors++;
        }
        else
        {
            Unity.Collections.NativeArray<Color32> data = req.GetData<Color32>();
            if (data.Length < PatchSize * PatchSize)
            {
                _readErrors++;
            }
            else
            {
                Store(Patches[(int)pending.Slot], data, pending.BurstId);
                ok = true;
            }
        }
        if (!ok)
            Patches[(int)pending.Slot].Valid = false;

        if (_burstOutstanding > 0)
            _burstOutstanding--;
        if (_burstClosed && _burstOutstanding == 0)
            FinishBurst();
    }

    private static void Store(Patch patch, Unity.Collections.NativeArray<Color32> data, int burstId)
    {
        int n = PatchSize * PatchSize;
        double sum = 0.0;
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            Color32 c = data[i];
            patch.Rgb[i * 3] = c.r;
            patch.Rgb[i * 3 + 1] = c.g;
            patch.Rgb[i * 3 + 2] = c.b;
            float y = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
            patch.Luma[i] = y;
            sum += y;
            sumSq += y * y;
        }
        patch.Mean = (float)(sum / n);
        float variance = (float)(sumSq / n) - patch.Mean * patch.Mean;
        patch.StdDev = Mathf.Sqrt(Mathf.Max(variance, 0f));
        patch.Valid = true;
        patch.BurstId = burstId;
    }

    // =============================================================================================
    // JUDGEMENT
    // =============================================================================================

    private static void Judge(int burstId)
    {
        JudgeLiveSelfTest(burstId);

        Patch la = Patches[(int)Slot.SubjectLeftA];
        Patch ra = Patches[(int)Slot.SubjectRightA];
        Patch lb = Patches[(int)Slot.SubjectLeftB];
        Patch rb = Patches[(int)Slot.SubjectRightB];

        // TEMPORAL CONTROL FIRST — it is the denominator of the stereo verdict, so it has to exist
        // before the stereo number means anything, and it is only admissible at a still head.
        float temporal = -1f;
        if (_closedStill && Same(la, lb, burstId))
            temporal = MeanAbsDifference(la, lb);
        else if (_closedStill && Same(ra, rb, burstId))
            temporal = MeanAbsDifference(ra, rb);
        if (temporal >= 0f)
        {
            _temporalSamples++;
            _temporalSum += temporal;
            if (temporal > _temporalMax)
                _temporalMax = temporal;
        }

        float stereo = -1f;
        if (Same(la, ra, burstId))
            stereo = MeanAbsDifference(la, ra);
        else if (Same(lb, rb, burstId))
            stereo = MeanAbsDifference(lb, rb);
        if (stereo >= 0f)
        {
            _stereoSamples++;
            _stereoSum += stereo;
            if (stereo > _stereoMax)
                _stereoMax = stereo;
        }

        Patch refL = Patches[(int)Slot.ReferenceLeftA];
        Patch refR = Patches[(int)Slot.ReferenceRightA];
        if (Same(refL, refR, burstId))
        {
            _refStereoSamples++;
            _refStereoSum += MeanAbsDifference(refL, refR);
        }

        // THE VERDICT. Note what it does and does not claim: a stereo difference materially larger
        // than one eye's own frame-to-frame difference at a still head proves the two eyes are
        // being shown DIFFERENT PICTURES of a surface whose source content is known static. It does
        // NOT by itself distinguish H1 from H2 — severe aliasing is per-eye too — which is exactly
        // why the spectral half of this line exists.
        if (stereo >= 0f && _temporalSamples > 0)
        {
            float floor = Mathf.Max(_temporalSum / _temporalSamples, RivalryFloor);
            if (stereo >= floor * RivalryFactor)
            {
                _rivalryHits++;
                _lastVerdict = $"STEREO RIVALRY: the eyes differ by {stereo:F4} of full range, "
                               + $"{stereo / floor:F1}x the same eye's own frame-to-frame difference "
                               + "at a still head";
            }
            else
            {
                _lastVerdict = $"eyes agree within noise ({stereo:F4} vs a {floor:F4} floor)";
            }
        }

        // SPECTRUM — always taken from the LEFT-eye frame-A patch, bucketed by whether the head was
        // still on that frame. The still/moving split IS the H1-vs-H2 test.
        if (la.Valid && la.BurstId == burstId)
        {
            Classify(la.Luma, out float period, out float strength, out string axis);
            if (period > 0f)
            {
                // _burstFrameAStill, NOT the live _frameStill: this patch was captured two or three
                // frames ago and the live flag describes a different frame. Bucketing a still-head
                // sample as "moving" (or the reverse) would corrupt the one comparison the H1/H2
                // question is decided on.
                if (_burstFrameAStill)
                {
                    _stillSpectra++;
                    _stillPeriodSum += period;
                    _stillStrengthSum += strength;
                    _stillAxis = axis;
                    if (period < _stillPeriodMin) _stillPeriodMin = period;
                    if (period > _stillPeriodMax) _stillPeriodMax = period;
                }
                else
                {
                    _moveSpectra++;
                    _movePeriodSum += period;
                    _moveStrengthSum += strength;
                    _moveAxis = axis;
                    if (period < _movePeriodMin) _movePeriodMin = period;
                    if (period > _movePeriodMax) _movePeriodMax = period;
                }
            }
        }
    }

    private static bool Same(Patch a, Patch b, int burstId)
        => a.Valid && b.Valid && a.BurstId == burstId && b.BurstId == burstId;

    private static void JudgeLiveSelfTest(int burstId)
    {
        if (_livePassed || _liveFailed)
            return;
        Patch plain = Patches[(int)Slot.SelfTestPlain];
        Patch offset = Patches[(int)Slot.SelfTestOffset];
        Patch subject = Patches[(int)Slot.SubjectLeftA];
        if (!Same(plain, offset, burstId) || !Same(plain, subject, burstId))
            return;

        // KNOWN-FALSE: the plain self-test capture and the subject capture are the SAME eye, the
        // SAME frame and the SAME rect. They must be bit-identical. If they are not, the readback
        // path or the FIFO pairing is scrambled and no number this class prints can be trusted.
        float same = MeanAbsDifference(plain, subject);
        // KNOWN-TRUE: the offset capture is displaced by a known number of pixels and must differ —
        // unless the patch is genuinely flat, which is inconclusive and not a failure.
        float shifted = MeanAbsDifference(plain, offset);

        if (same > 0f)
        {
            _liveFailed = true;
            _disabled = true;
            _liveReport = $"FAILED — two captures of the SAME eye, SAME frame and SAME rect differ "
                          + $"by {same:F5}; the readback path or its FIFO pairing is scrambled";
            VRLog.Warn(Scope, "EYE FRAME PROBE DISABLED ITSELF: its LIVE self-test failed. Two "
                              + "captures taken in the same eye pass, in the same frame, of the same "
                              + $"screen rectangle came back DIFFERENT (by {same:F5} of full range). "
                              + "That means the blit/readback FIFO is not pairing each callback with "
                              + "its own request, so every left-vs-right number this probe could "
                              + "print would be comparing the wrong two patches. THE CONSEQUENCE is "
                              + "that round 10 produces no per-eye verdict on this run and the H1/H2 "
                              + "question stays open; nothing the game owns was written and every "
                              + "other probe is unaffected. This is deliberate — an instrument that "
                              + "fails its own known-false case must not be allowed to speak.");
            return;
        }

        if (plain.StdDev < SelfTestMinStdDev)
        {
            _liveReport = $"inconclusive so far ({_liveAttempts} attempt(s)): the captured patch is "
                          + $"flat (std dev {plain.StdDev:F4} < {SelfTestMinStdDev:F4}), so an "
                          + "offset capture is legitimately identical and the known-TRUE case "
                          + "cannot be exercised on it";
            if (_liveAttempts >= SelfTestMaxAttempts)
            {
                _liveReport = $"INCONCLUSIVE after {_liveAttempts} attempts — every patch it looked "
                              + "at was flat. The known-FALSE half passed every time (identical "
                              + "rects read back identical), so the readback pairing is proven; the "
                              + "known-TRUE half was never exercisable. Read the verdicts below with "
                              + "that caveat";
            }
            return;
        }

        if (shifted <= 0f)
        {
            _liveFailed = true;
            _disabled = true;
            _liveReport = $"FAILED — a capture displaced by {SelfTestOffsetPixels} px over a patch "
                          + $"with std dev {plain.StdDev:F4} came back IDENTICAL";
            VRLog.Warn(Scope, "EYE FRAME PROBE DISABLED ITSELF: its LIVE self-test failed the other "
                              + $"way. A capture deliberately displaced by {SelfTestOffsetPixels} "
                              + "eye pixels, over a patch with real contrast in it (std dev "
                              + $"{plain.StdDev:F4}), came back BIT-IDENTICAL to the undisplaced "
                              + "one. That means the blit is not honouring the requested rectangle "
                              + "(or the readback is returning a constant), so the probe is BLIND: "
                              + "it would compare two copies of the same thing and report 'the eyes "
                              + "agree' no matter what the headset is actually doing. THE "
                              + "CONSEQUENCE is that round 10 produces no per-eye verdict on this "
                              + "run; nothing the game owns was written and every other probe is "
                              + "unaffected.");
            return;
        }

        _livePassed = true;
        _liveReport = $"PASS — known-FALSE: two captures of the same eye/frame/rect were "
                      + $"bit-identical (difference {same:F5}); known-TRUE: the same capture "
                      + $"displaced by {SelfTestOffsetPixels} px differed by {shifted:F4} over a "
                      + $"patch with std dev {plain.StdDev:F4}. The capture honours the rectangle it "
                      + "is given and the readback pairing is intact";
    }

    /// <summary>Mean absolute per-channel difference between two patches, as a fraction of full
    /// range. This is the comparator the synthetic self-test validates.</summary>
    private static float MeanAbsDifference(Patch a, Patch b)
    {
        int n = PatchSize * PatchSize * 3;
        long sum = 0;
        for (int i = 0; i < n; i++)
        {
            int d = a.Rgb[i] - b.Rgb[i];
            sum += d >= 0 ? d : -d;
        }
        return (float)(sum / (double)n / 255.0);
    }

    // =============================================================================================
    // SPECTRAL CLASSIFICATION
    //
    // Normalized autocorrelation along four axes at lags MinLag..MaxLag, mean removed. The
    // dominant period is the strongest LOCAL MAXIMUM — a local maximum specifically, because a
    // smooth luminance ramp (a lit panel, a vignette) produces a monotonically decaying correlation
    // whose argmax is always the smallest lag, and reading that as "a period of 3 px" is exactly
    // the kind of confident wrong answer this file exists to prevent.
    //
    // HOW TO READ THE OUTPUT, and it is the whole point of the class: an ORDERED / SCREEN-SPACE
    // term (Bayer, interleaved-gradient, a dither texture indexed by screen position) produces a
    // SHARP, NARROW peak at a FIXED period, locked to the screen axes or to 45°, and that period
    // DOES NOT MOVE when the head moves. CONTENT ALIASING produces a broad, weak correlation whose
    // beat period SHIFTS as the sampling rate changes — i.e. as the head moves. So the report
    // prints still-head and moving-head statistics side by side.
    // =============================================================================================

    private static readonly int[] DirX = { 1, 0, 1, 1 };
    private static readonly int[] DirY = { 0, 1, 1, -1 };

    private static readonly string[] DirName =
    {
        "horizontal screen axis (vertical stripes)",
        "vertical screen axis (horizontal stripes)",
        "+45 deg diagonal",
        "-45 deg diagonal",
    };

    private static readonly float[] Correlations = new float[MaxLag + 1];

    /// <summary>Dominant period in pixels, its normalized correlation strength (0..1) and the axis
    /// it lies on. Period 0 means no periodic local maximum was found at all.</summary>
    private static void Classify(float[] luma, out float periodPx, out float strength, out string axis)
    {
        periodPx = 0f;
        strength = 0f;
        axis = "none";

        float mean = 0f;
        for (int i = 0; i < luma.Length; i++)
            mean += luma[i];
        mean /= luma.Length;

        for (int d = 0; d < DirX.Length; d++)
        {
            int dx = DirX[d];
            int dy = DirY[d];
            float step = Mathf.Sqrt(dx * dx + dy * dy);
            for (int k = 1; k <= MaxLag; k++)
                Correlations[k] = Correlate(luma, mean, dx * k, dy * k);
            for (int k = MinLag; k <= MaxLag - 1; k++)
            {
                float r = Correlations[k];
                if (r <= Correlations[k - 1] || r < Correlations[k + 1])
                    continue; // not a local maximum — a decaying ramp never qualifies
                if (r <= strength)
                    continue;
                strength = r;
                periodPx = k * step;
                axis = DirName[d];
            }
        }
    }

    private static float Correlate(float[] luma, float mean, int dx, int dy)
    {
        int x0 = dx > 0 ? 0 : -dx;
        int x1 = dx > 0 ? PatchSize - dx : PatchSize;
        int y0 = dy > 0 ? 0 : -dy;
        int y1 = dy > 0 ? PatchSize - dy : PatchSize;
        if (x1 <= x0 || y1 <= y0)
            return 0f;
        double ab = 0.0;
        double aa = 0.0;
        double bb = 0.0;
        for (int y = y0; y < y1; y++)
        {
            int row = y * PatchSize;
            int rowB = (y + dy) * PatchSize;
            for (int x = x0; x < x1; x++)
            {
                double a = luma[row + x] - mean;
                double b = luma[rowB + x + dx] - mean;
                ab += a * b;
                aa += a * a;
                bb += b * b;
            }
        }
        double denom = Math.Sqrt(aa * bb);
        return denom > 1e-12 ? (float)(ab / denom) : 0f;
    }

    // =============================================================================================
    // SYNTHETIC SELF-TEST — runs at arm time, before a single verdict about a real panel is printed.
    // Five cases: two for the comparator and three for the classifier, each with a known answer.
    // =============================================================================================

    private static bool RunSyntheticSelfTest(out string report)
    {
        var a = new Patch();
        var b = new Patch();
        int n = PatchSize * PatchSize;

        // (1) COMPARATOR, KNOWN-FALSE: identical buffers must report exactly zero. The base pattern
        // is masked to 0x7F, not 0xFF, so that case (2) below can add 8 to any channel without ever
        // clamping at 255 — a clamp would make the "known" answer not actually known.
        for (int i = 0; i < n * 3; i++)
        {
            byte v = (byte)((i * 37) & 0x7F);
            a.Rgb[i] = v;
            b.Rgb[i] = v;
        }
        float zero = MeanAbsDifference(a, b);
        bool okIdentical = zero == 0f;

        // (2) COMPARATOR, KNOWN-TRUE: every fourth channel raised by exactly 8/255, so the mean
        // absolute difference over all channels must be exactly (8/4)/255.
        for (int i = 0; i < n * 3; i += 4)
            b.Rgb[i] = (byte)(a.Rgb[i] + 8);
        float expected = (8f / 4f) / 255f;
        float measured = MeanAbsDifference(a, b);
        bool okDifference = Mathf.Abs(measured - expected) < expected * 0.02f;

        // (3) CLASSIFIER, KNOWN-TRUE, axis: a period-8 vertical stripe pattern along x.
        var stripes = new float[n];
        for (int y = 0; y < PatchSize; y++)
        for (int x = 0; x < PatchSize; x++)
            stripes[y * PatchSize + x] = (x % 8) < 4 ? 0.7f : 0.3f;
        Classify(stripes, out float stripePeriod, out float stripeStrength, out string stripeAxis);
        bool okStripes = Mathf.Abs(stripePeriod - 8f) < 0.6f && stripeStrength >= SharpPeakStrength
                         && stripeAxis == DirName[0];

        // (4) CLASSIFIER, KNOWN-TRUE, DIAGONAL. This case is here because a diagonal is exactly what
        // the user's photograph shows, and a self-test that only exercised the screen axes could
        // not have caught a broken diagonal step length.
        //
        // THE PATTERN HAD TO BE CHOSEN CAREFULLY, and the first attempt was WRONG in a way worth
        // recording: a square wave in (x+y) with period 8 is ALSO period-8 along x and along y, so
        // the classifier legitimately reported the horizontal axis and an assertion demanding the
        // +45 axis would have failed on every launch and disabled this probe permanently. A plane
        // wave in (x+y) with wavelength 32 px is the correct instrument: its period along each
        // SCREEN axis is 32 px, which is past MaxLag and therefore invisible; along -45 the wave is
        // constant, which produces r = 1 at every lag and is correctly rejected by the
        // local-maximum rule; and along +45 a lag of k steps advances the phase by 2k px, so it
        // peaks at k = 16, i.e. 16*sqrt(2) = 22.63 px. That is the only reading it can give.
        var diagonal = new float[n];
        for (int y = 0; y < PatchSize; y++)
        for (int x = 0; x < PatchSize; x++)
            diagonal[y * PatchSize + x] = 0.5f + 0.2f * Mathf.Cos(2f * Mathf.PI * (x + y) / 32f);
        Classify(diagonal, out float diagPeriod, out float diagStrength, out string diagAxis);
        float diagExpected = 16f * Mathf.Sqrt(2f);
        bool okDiagonal = Mathf.Abs(diagPeriod - diagExpected) < 0.6f
                          && diagStrength >= SharpPeakStrength && diagAxis == DirName[2];

        // (5) CLASSIFIER, KNOWN-FALSE: deterministic hash noise must NOT read as a sharp period.
        var noise = new float[n];
        uint h = 2166136261u;
        for (int i = 0; i < n; i++)
        {
            h ^= (uint)i;
            h *= 16777619u;
            noise[i] = (h >> 8 & 0xFFFF) / 65535f;
        }
        Classify(noise, out float noisePeriod, out float noiseStrength, out _);
        bool okNoise = noiseStrength < SharpPeakStrength * 0.5f;

        bool all = okIdentical && okDifference && okStripes && okDiagonal && okNoise;
        report = "comparator known-FALSE (identical buffers) "
                 + $"{(okIdentical ? "PASS" : "FAIL")} ({zero:F6}, expected 0); comparator "
                 + $"known-TRUE (a known +8/255 on a quarter of the channels) "
                 + $"{(okDifference ? "PASS" : "FAIL")} ({measured:F5}, expected {expected:F5}); "
                 + $"classifier known-TRUE axis (period-8 vertical stripes) "
                 + $"{(okStripes ? "PASS" : "FAIL")} (read {stripePeriod:F2} px at "
                 + $"{stripeStrength * 100f:F0}% on the {stripeAxis}); classifier known-TRUE "
                 + $"diagonal (a 32 px plane wave in x+y, i.e. {diagExpected:F2} px on the +45 deg "
                 + $"axis and invisible on both screen axes) "
                 + $"{(okDiagonal ? "PASS" : "FAIL")} (read {diagPeriod:F2} px at "
                 + $"{diagStrength * 100f:F0}% on the {diagAxis}); classifier known-FALSE (hash "
                 + $"noise) {(okNoise ? "PASS" : "FAIL")} (read {noisePeriod:F2} px at only "
                 + $"{noiseStrength * 100f:F0}%, which must stay under "
                 + $"{SharpPeakStrength * 50f:F0}%) => {(all ? "ALL FIVE PASS" : "FAILED")}";
        return all;
    }

    // =============================================================================================
    // INVENTORY — what is actually on the floated panels and on the head camera. Printed once per
    // arming, and again whenever the material signature changes, because a screen-space shading
    // term is a property of a SHADER and of a POST EFFECT, and no probe in nine rounds ever wrote
    // down which shaders and which image effects were in play.
    // =============================================================================================

    private static readonly List<Graphic> GraphicScratch = new(256);
    private static readonly List<string> MaterialScratch = new(16);

    private static void Inventory()
    {
        Sb.Length = 0;
        Sb.Append("EYE FRAME INVENTORY — ");

        // (a) The materials actually on the floated panels' graphics.
        MaterialScratch.Clear();
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        int graphics = 0;
        for (int p = 0; p < panels.Count; p++)
        {
            ConvertedPanel panel = panels[p];
            if (panel == null || !panel.IsAlive || panel.HostGo == null)
                continue;
            GraphicScratch.Clear();
            panel.HostGo.GetComponentsInChildren(false, GraphicScratch);
            for (int i = 0; i < GraphicScratch.Count; i++)
            {
                Graphic g = GraphicScratch[i];
                if (g == null || !g.enabled)
                    continue;
                graphics++;
                Material? mat = g.materialForRendering;
                string sig = Describe(mat);
                if (!MaterialScratch.Contains(sig))
                    MaterialScratch.Add(sig);
            }
        }
        GraphicScratch.Clear();

        Sb.Append("MATERIALS ON FLOATED PANELS (").Append(MaterialScratch.Count)
          .Append(" distinct across ").Append(graphics).Append(" visible graphic(s)): ");
        if (MaterialScratch.Count == 0)
            Sb.Append("<none measurable>");
        for (int i = 0; i < MaterialScratch.Count; i++)
        {
            if (i > 0)
                Sb.Append(" | ");
            Sb.Append(MaterialScratch[i]);
        }

        // (b) What is on the HEAD camera. Previous rounds only ever enumerated image effects on the
        // RenderTexture camera, where Beautify reads 'off'. The head camera is where a screen-space
        // post term would actually land on both eyes' pixels, and it has never been written down.
        Camera? head = VRRigDriver.HeadCamera;
        Sb.Append(" || HEAD CAMERA ('")
          .Append(head != null ? head.name : "<none>").Append("'): ");
        if (head == null)
        {
            Sb.Append("NOT PRESENT — nothing could be enumerated.");
        }
        else
        {
            Component[] comps = head.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (i > 0)
                    Sb.Append(", ");
                if (c == null)
                {
                    Sb.Append("<missing script>");
                    continue;
                }
                Sb.Append(c.GetType().Name);
                if (c is Behaviour bh)
                    Sb.Append(bh.enabled ? "(on)" : "(off)");
            }
            Sb.Append("; allowMSAA=").Append(head.allowMSAA)
              .Append(" allowHDR=").Append(head.allowHDR)
              .Append(" allowDynamicResolution=").Append(head.allowDynamicResolution)
              .Append(" renderingPath=").Append(head.actualRenderingPath)
              .Append(" depthTextureMode=").Append(head.depthTextureMode)
              .Append(" stereoTargetEye=").Append(head.stereoTargetEye)
              .Append("; QualitySettings.antiAliasing=").Append(QualitySettings.antiAliasing)
              .Append(" anisotropicFiltering=").Append(QualitySettings.anisotropicFiltering)
              .Append(" colorSpace=").Append(QualitySettings.activeColorSpace)
              .Append("; XR ").Append(XRSettings.stereoRenderingMode).Append(' ')
              .Append(XRSettings.eyeTextureWidth).Append('x').Append(XRSettings.eyeTextureHeight)
              .Append(" viewportScale ").Append(XRSettings.renderViewportScale.ToString("F2"))
              .Append('.');
        }

        Sb.Append(" READ IT LIKE THIS: any shader on this line that samples a value from SCREEN "
                  + "POSITION — an ordered-dither matrix, a noise texture indexed by screenPos or "
                  + "VPOS, a clip() against a screen-derived threshold, AlphaToMask, or a "
                  + "unity_LODFade crossfade — is a live H1 candidate, because in MultiPass the same "
                  + "world surface lands at a DIFFERENT screen position in each eye and therefore "
                  + "gets a different dither phase. So is any image effect on the head camera with a "
                  + "dithering stage. An effect that is (off) on this line cannot be the cause, and "
                  + "an effect that is not on this line at all was never on the head camera — note "
                  + "that earlier rounds only ever inspected the RenderTexture camera, where "
                  + "Beautify legitimately reads off, and that the ModBuild 186 accusation against "
                  + "Beautify was FORMALLY RETRACTED. Do not resurrect it without pixels.");

        string line = Sb.ToString();
        Sb.Length = 0;
        string signature = string.Join("|", MaterialScratch);
        if (_inventoryLogged && signature == _inventorySignature)
            return;
        _inventoryLogged = true;
        _inventorySignature = signature;
        VRLog.Info(Scope, line);
    }

    private static string Describe(Material? mat)
    {
        if (mat == null)
            return "<null material — the graphic draws with uGUI's default>";
        Shader? sh = mat.shader;
        string keywords = mat.shaderKeywords.Length > 0
            ? string.Join(",", mat.shaderKeywords)
            : "<none>";
        string blend = mat.HasProperty("_SrcBlend") && mat.HasProperty("_DstBlend")
            ? $"{(BlendMode)(int)mat.GetFloat("_SrcBlend")}/{(BlendMode)(int)mat.GetFloat("_DstBlend")}"
            : "<not exposed>";
        string zwrite = mat.HasProperty("_ZWrite")
            ? mat.GetFloat("_ZWrite").ToString("F0")
            : "<not exposed>";
        return $"[shader '{(sh != null ? sh.name : "<null>")}', queue {mat.renderQueue}, keywords "
               + $"{keywords}, blend {blend}, ZWrite {zwrite}]";
    }

    // =============================================================================================
    // THE BASELINE — printed every SummaryIntervalSeconds whether or not anything was found.
    // =============================================================================================

    private static void Baseline(string why)
    {
        Inventory();

        float meanStereo = _stereoSamples > 0 ? _stereoSum / _stereoSamples : -1f;
        float meanRef = _refStereoSamples > 0 ? _refStereoSum / _refStereoSamples : -1f;
        float meanTemporal = _temporalSamples > 0 ? _temporalSum / _temporalSamples : -1f;
        float perCaptureUs = _recordSamples > 0
            ? (float)(_recordTicks / (double)_recordSamples / Stopwatch.Frequency * 1e6)
            : -1f;

        string stillSpectrum = _stillSpectra > 0
            ? $"{_stillPeriodSum / _stillSpectra:F2} px (range {_stillPeriodMin:F2}..{_stillPeriodMax:F2}) "
              + $"at {_stillStrengthSum / _stillSpectra * 100f:F0}% strength on the {_stillAxis}, "
              + $"{_stillSpectra} sample(s)"
            : "NOT SAMPLED (the head was never still enough this window)";
        string moveSpectrum = _moveSpectra > 0
            ? $"{_movePeriodSum / _moveSpectra:F2} px (range {_movePeriodMin:F2}..{_movePeriodMax:F2}) "
              + $"at {_moveStrengthSum / _moveSpectra * 100f:F0}% strength on the {_moveAxis}, "
              + $"{_moveSpectra} sample(s)"
            : "NOT SAMPLED (the head was still on every sampled frame this window)";

        string spectralVerdict;
        if (_stillSpectra == 0 || _moveSpectra == 0)
        {
            spectralVerdict = "NOT DECIDABLE YET — the still/moving comparison needs samples in BOTH "
                              + "buckets. Hold the head still for a few seconds looking at the "
                              + "panel, then move it slowly, and read the next line.";
        }
        else
        {
            float stillMean = _stillPeriodSum / _stillSpectra;
            float moveMean = _movePeriodSum / _moveSpectra;
            float stillStrength = _stillStrengthSum / _stillSpectra;
            float drift = Mathf.Abs(moveMean - stillMean);
            bool sharp = stillStrength >= SharpPeakStrength;
            bool fixedPeriod = drift <= 1f && (_stillPeriodMax - _stillPeriodMin) <= 1.5f;
            spectralVerdict = sharp && fixedPeriod
                ? "ORDERED / SCREEN-SPACE (H1): the peak is SHARP and its period does NOT move when "
                  + "the head moves. That is a dither/dissolve/crossfade evaluated from SCREEN "
                  + "POSITION, which in MultiPass gives each eye a different phase. Find it on the "
                  + "INVENTORY line above and turn it off on the floated panels."
                : sharp
                    ? "SHARP BUT DRIFTING: a strong peak whose period MOVES with the head. That is a "
                      + "moire beat between a regular source pattern and the sampling grid, i.e. "
                      + "CONTENT (H2) — the artwork's own repeating detail undersampled — not a "
                      + "screen-space term. The cure is more rendered pixels per source texel, not "
                      + "a shader change."
                    : "BROADBAND (H2): no sharp period at either head state. This is ordinary "
                      + "spatial aliasing of detailed artwork and mipless SDF text; H1 is not "
                      + "supported by these pixels.";
        }

        string rivalryVerdict;
        if (_stereoSamples == 0)
        {
            rivalryVerdict = "NO STEREO COMPARISON COMPLETED this window — see the skip counts above; "
                             + "this is the one reading that proves nothing.";
        }
        else if (_temporalSamples == 0)
        {
            rivalryVerdict = "STEREO MEASURED BUT NOT JUDGED: there is no still-head temporal control "
                             + "this window, and without it a left-right difference cannot be told "
                             + "from ordinary per-frame change. Hold the head still for a few "
                             + "seconds on the panel.";
        }
        else if (_rivalryHits > 0)
        {
            rivalryVerdict = $"STEREO RIVALRY CONFIRMED on {_rivalryHits} of {_stereoSamples} "
                             + "comparison(s): the two eyes are being shown DIFFERENT pixels of a "
                             + "surface whose source content is known static (RenderTargetProbe, "
                             + "567/567 bit-identical). That is the mechanism that reads as violent "
                             + "flicker in the headset and as a static crosshatch in a one-eye "
                             + "screenshot. It does NOT on its own separate H1 from H2 — severe "
                             + "aliasing is per-eye too — so read it together with the SPECTRUM "
                             + "verdict on this same line.";
        }
        else
        {
            rivalryVerdict = "NO STEREO RIVALRY: over " + _stereoSamples + " comparison(s) the two "
                             + "eyes never differed by " + $"{RivalryFactor:F0}x the same eye's own "
                             + "frame-to-frame difference at a still head. THIS IS A RESULT, NOT "
                             + "SILENCE — it says the flicker is not a per-eye disagreement on the "
                             + "measured surface, which kills H1 for that surface outright and hands "
                             + "the question back to the temporal and spectral numbers.";
        }

        VRLog.Info(Scope, $"EYE FRAME {why}: {_bursts} burst(s), {_captured} patch(es) captured, "
                          + $"{_readErrors} readback error(s), {_skippedOffTarget} skipped (the "
                          + $"{PatchSize}x{PatchSize} window would have left the eye target or the "
                          + $"surface was behind the eye), {_skippedNoSubject} skipped (the PANEL "
                          + "SAMPLING scan had no measurable graphic to aim at). SUBJECT = "
                          + $"{PanelSamplingProbe.SubjectLabel}; REFERENCE = "
                          + $"{PanelSamplingProbe.ReferenceLabel} (chosen "
                          + (PanelSamplingProbe.SelectionFrame >= 0
                              ? $"{Time.frameCount - PanelSamplingProbe.SelectionFrame} frame(s) ago"
                              : "NEVER — the PANEL SAMPLING scan has not produced a pick")
                          + " by the PANEL SAMPLING scan, so both lines describe the same "
                          + "graphics). "
                          + $"STEREO: subject left-vs-right mean {Fmt(meanStereo)} of full range "
                          + $"(peak {Fmt(_stereoMax)}) over {_stereoSamples} same-frame "
                          + $"comparison(s); REFERENCE surface left-vs-right mean {Fmt(meanRef)} "
                          + $"over {_refStereoSamples}. TEMPORAL CONTROL (same eye, frame N vs N+1, "
                          + $"STILL HEAD ONLY — the gate is <= {StillPositionMetres * 1000f:F2} mm "
                          + $"and <= {StillRotationDegrees:F2} deg of head movement between the two "
                          + $"frames; the last closed burst measured {_closedMoveMm:F2} mm / "
                          + $"{_closedMoveDeg:F3} deg): mean {Fmt(meanTemporal)} (peak "
                          + $"{Fmt(_temporalMax)}) over {_temporalSamples} admissible pair(s). "
                          + $"VERDICT: {rivalryVerdict} LAST COMPARISON: {_lastVerdict}. "
                          + $"SPECTRUM — dominant period, STILL HEAD: {stillSpectrum}; MOVING HEAD: "
                          + $"{moveSpectrum}. HOW TO READ THE TWO: a SHARP NARROW peak at a FIXED "
                          + "period that is the SAME still and moving = an ordered/screen-space "
                          + "term (H1); a BROAD weak spectrum, or a period that SHIFTS between the "
                          + $"two, = content aliasing (H2). SPECTRAL VERDICT: {spectralVerdict} "
                          + $"SELF-TEST synthetic: {_syntheticReport}. SELF-TEST live: "
                          + $"{_liveReport}. COST: {Fmt(perCaptureUs, "F1")} us per capture over "
                          + $"{_recordSamples} recorded pass(es), {BurstsPerSecond:F0} burst(s)/s of "
                          + $"at most 8 x {PatchSize}x{PatchSize} RGBA patches. ZERO FINDINGS ON "
                          + "THIS LINE MEAN THE EYE PIXELS WERE READ AND AGREED — it does not mean "
                          + "nothing looked.");

        ResetWindow();
    }

    private static string Fmt(float v, string format = "F4")
        => v < 0f ? "n/a" : v.ToString(format);

    private static void ResetWindow()
    {
        _bursts = 0;
        _captured = 0;
        _readErrors = 0;
        _skippedOffTarget = 0;
        _skippedNoSubject = 0;
        _recordTicks = 0;
        _recordSamples = 0;
        _stereoSamples = 0;
        _stereoSum = 0f;
        _stereoMax = -1f;
        _refStereoSamples = 0;
        _refStereoSum = 0f;
        _temporalSamples = 0;
        _temporalSum = 0f;
        _temporalMax = -1f;
        _rivalryHits = 0;
        _stillSpectra = 0;
        _stillPeriodSum = 0f;
        _stillPeriodMin = float.MaxValue;
        _stillPeriodMax = float.MinValue;
        _stillStrengthSum = 0f;
        _moveSpectra = 0;
        _movePeriodSum = 0f;
        _movePeriodMin = float.MaxValue;
        _movePeriodMax = float.MinValue;
        _moveStrengthSum = 0f;
    }
}
