using System.Collections;
using System.Diagnostics;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE LEFT RIM — THE PIXELS PULL THE TRIGGER.</b>
///
/// <para>THE SYMPTOM, unchanged for seven builds, most recently on ModBuild 424:
/// <i>"Das Problem mit dem linken Element im Auge besteht weiterhin."</i> With his screenshot
/// (<c>.planning/debug/linkes_element.png</c>): <i>"Wenn man schnell hintereinander die Optionstaste
/// drückt erscheint am linken Rand des auges so ein Rand der dem Kopf folgt statt dem Optionsmenu"</i>
/// — a uniform, translucent, dark-grey, left-anchored column spanning the FULL height of ONE eye.</para>
///
/// <para><b>WHY THIS CLASS EXISTS.</b> <see cref="EyeReachCensus"/> enumerates everything that can
/// paint into an eye and, on ModBuild 424, matched NOTHING while the user still saw the band. Exactly
/// two readings survive that, and until one of them is eliminated no fix can be aimed:</para>
/// <list type="bullet">
///   <item><b>(A) THE CENSUS NEVER SAMPLED THE ARTIFACT.</b> Its triggers are the options key: the
///     first tap, the third tap of a burst, and +0.75 s / +4.0 s after the last tap. The 424 log
///     spent 7 of its 8 runs on 24 taps, only ONE of them a BURST. If the band is bound to an
///     interrupted materialise a few hundred milliseconds long, a run fired ON the tap can precede
///     it and a run at +0.75 s can follow it. Every row would then be true and useless — the failure
///     that has now cost six builds ([[measure-the-picture-not-the-state]],
///     [[one-step-too-early]]).</item>
///   <item><b>(B) THE ARTIFACT IS IN A DECLARED BLIND SPOT</b> — a full-screen image effect, a
///     <c>CommandBuffer</c>, an XR compositor overlay, an IMGUI draw, a vertex-displaced mesh, or a
///     renderer dropped by the census's angular-size prefilter.</item>
/// </list>
///
/// <para><b>THIS CLASS CLOSES (A) PERMANENTLY.</b> It watches the actual output frame and fires the
/// census BECAUSE the band is on screen. A census that runs because the artifact is visible cannot
/// miss it. The pairing that produces — <i>"the band is on screen right now"</i> next to <i>"here is
/// everything that can paint into an eye right now"</i> — is the one thing six rounds have never
/// had, and it decides the question either way:</para>
/// <list type="bullet">
///   <item>Band on screen AND the census names a row ⇒ that row IS the artifact. Reading (A) was
///     right, and the object has a scene path.</item>
///   <item>Band on screen AND the census matches NOTHING ⇒ reading (B) is proved. That is a RESULT,
///     not a failure: the remaining search space collapses to the blind-spot list, which the same
///     run now enumerates (command buffers and <c>OnRenderImage</c> components, per camera).</item>
/// </list>
///
/// <para><b>THE SIX FALSIFIED CAUSES.</b> Do not re-derive any of them. 419 the blackout budget
/// (0 expiries); 420 the float-intent invariant (0 violations); 421 a supersample layer straddle
/// (0 refusals); 422 the ESC menu's canvas left enabled on the overlay path (0 rule firings);
/// 423 the census itself, which found the ESC panel's own graphic 4 times; and <b>424 the adopt-time
/// camera leak — that one was REAL and it FIRED 31 TIMES</b>
/// (<c>] MODAL ADOPT CAMERA LEAK</c>), it removed the 4 graphic matches, and the user still sees the
/// band. So it was at most ONE CONTRIBUTOR, never the whole cause
/// ([[one-contributor-is-not-the-union]]).</para>
///
/// <para><b>WHAT IT WATCHES.</b> The GAME VIEW BACKBUFFER at end of frame. The 424 log says
/// <c>XR mirror mode=LeftEye</c>, so the backbuffer IS the left eye — the very image the user
/// photographed. Capturing there rather than at <c>OnPostRender</c> is deliberate: end of frame is
/// AFTER image effects, after overlay canvases and after IMGUI, so three of the census's five blind
/// spots are inside this measurement even though they are outside the census's.</para>
///
/// <para><b>HOW IT DETECTS THE BAND — SHAPE, NOT COLOUR.</b> Calibrated offline against his
/// screenshot and 283 other project screenshots (see the constants below for the measured numbers).
/// The distinctive feature is not "dark grey": most of his frame is dark. It is a STRAIGHT VERTICAL
/// EDGE at one column, holding across the full height, with the region left of it far more uniform
/// than the scene beside it. On that corpus the gate set below hits his screenshot and no other
/// rendered frame.</para>
///
/// <para><b>COST.</b> Never a per-frame GPU stall and never a per-frame allocation. One
/// <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c> into a mod-owned
/// <see cref="CaptureWidth"/>x<see cref="CaptureHeight"/> target plus one
/// <see cref="AsyncGPUReadback"/>, at <see cref="SampleIntervalSeconds"/> — no <c>ReadPixels</c>, no
/// pipeline flush. Both halves are timed and the measured milliseconds are printed on every line
/// rather than asserted ([[an-instrument-can-assert-a-cause]]). The watch also STANDS DOWN once it
/// has spent its census budget or reached <see cref="MaxSamples"/>, because a probe that has answered
/// is spent ([[probe-that-answered-is-spent]]).</para>
///
/// <para><b>MULTIPLAYER.</b> Local diagnostics only: it reads the frame and writes nothing but its
/// own counters — no wire field, no game-state write, no config key.</para>
/// </summary>
internal static class EyePixelBandWatch
{
    private const string Scope = "WorldUI";

    /// <summary>The coroutine host's GameObject name. Also the handle a hot reload uses to find and
    /// destroy the host the previous assembly load left behind.</summary>
    private const string HostName = "GloomhavenVR.EyePixelBandWatch";

    // ---- THE CAPTURE ---------------------------------------------------------------------------
    //
    // 256 columns is the horizontal resolution the edge is located at (one column = 0.4 % of the
    // frame width); 64 rows is enough to prove "full height" while keeping the readback at 16,384
    // texels. The aspect is deliberately NOT preserved: every term below is computed in NORMALISED
    // 0..1 frame coordinates, which an anisotropic scale leaves unchanged, and a 4:1 target costs a
    // quarter of what a 16:9 one would.

    private const int CaptureWidth = 256;
    private const int CaptureHeight = 64;

    /// <summary>Only every fourth row is analysed — 16 rows spread over the full height. The band is
    /// a full-height object, so more rows buy nothing and cost a linear scan each.</summary>
    private const int RowStep = 4;

    private const int Rows = CaptureHeight / RowStep;

    // ---- THE SHAPE, CALIBRATED --------------------------------------------------------------
    //
    // Measured with a Python port of exactly this algorithm over 284 project screenshots, his
    // included. The numbers his frame produces at the chosen k: right edge 0.234 (the value read off
    // the photograph by hand was 0.23), row agreement 16/16, edge step 0.0345, band std 0.0076,
    // scene std 0.0392, ratio 0.194. It was the ONLY rendered frame in the corpus to pass. The four
    // other passes were documentation contact sheets with a flat margin, not camera frames.
    //
    // The gates are deliberately NOT his exact numbers: the band must still be recognisable a little
    // wider or narrower, so the edge column is SEARCHED over 0.10..0.35 and reported, never assumed.

    /// <summary>Leftmost candidate right-edge column, as a fraction of the frame width.</summary>
    private const float EdgeMinFraction = 0.10f;

    /// <summary>Rightmost candidate right-edge column. The photograph says 0.234.</summary>
    private const float EdgeMaxFraction = 0.35f;

    private const int KMin = (int)(EdgeMinFraction * CaptureWidth);
    private const int KMax = (int)(EdgeMaxFraction * CaptureWidth);

    /// <summary>Columns averaged either side of a candidate edge. Three smooths out one-column
    /// resampling noise without blurring a real step.</summary>
    private const int EdgeHalfWidth = 3;

    /// <summary>Smallest luminance step across the edge that counts as an edge at all. His band steps
    /// 0.0345; this is a third of that, and it is what rejects a uniformly black frame (a loading
    /// fade steps 0.000 and can otherwise look perfectly "uniform on the left").</summary>
    private const float EdgeStepFloor = 0.012f;

    /// <summary>Fraction of the analysed rows that must show that step, with the SAME SIGN, at the
    /// SAME column. This is the term that carries the finding: a straight edge holding across the
    /// whole height is not something a forest produces. His frame scores 1.00; the best non-band
    /// rendered frame in the corpus scored 0.69.</summary>
    private const float RowAgreementMin = 0.75f;

    /// <summary>The band must be this much more uniform than an equally wide strip of scene beside
    /// it. A RATIO rather than an absolute, so the test still works over a bright scene — his
    /// reproduction happens to be over a near-black forest, and calibrating on that alone would have
    /// fitted the scene rather than the artifact.</summary>
    private const float UniformRatioMax = 0.35f;

    /// <summary>Absolute ceiling on the band's own luminance spread, so "0.35x of a very noisy scene"
    /// cannot admit a band that is plainly textured. Eight times what his frame measures.</summary>
    private const float BandStdCeiling = 0.060f;

    /// <summary>The agreeing rows must include one in the top sixth and one in the bottom sixth of
    /// the frame — the direct test of "spanning the full height".</summary>
    private const int EdgeRows = Rows / 6;

    // ---- CADENCE AND BUDGET --------------------------------------------------------------------

    /// <summary>Seconds between captures. 5 Hz: far below a per-frame cost, far above the rate at
    /// which a head-locked artifact that "bewegt sich mit dem Kopf mit" could hide between samples.
    /// UNSCALED, because the pause menu this key opens can set <c>Time.timeScale</c> to 0.</summary>
    private const float SampleIntervalSeconds = 0.20f;

    /// <summary>Total captures before the watch stands down, whatever it has or has not seen. 6000
    /// at 5 Hz is about twenty minutes of play — long enough for any reproduction the user is
    /// actually attempting, and a hard bound on what an instrument costs a shipped session.</summary>
    private const int MaxSamples = 6000;

    /// <summary>How often a summary prints even when nothing matched. A change-gated line with a
    /// constant reason prints once and then reads exactly like a stopped instrument
    /// ([[held-instrument-reads-as-dead]]), so the watch says it is alive on a cadence and names the
    /// CLOSEST it came, not just that it came to nothing.</summary>
    private const int SummaryEverySamples = 300;

    /// <summary>Seconds a band must persist before the second, "still there and the head has moved"
    /// census fires. The artifact follows the head, so a band that survives to here is a standing
    /// object rather than a one-frame transition.</summary>
    private const float HeldSeconds = 2.0f;

    // ---- STATE ---------------------------------------------------------------------------------

    private static Host? s_host;
    private static RenderTexture? s_rt;
    private static bool s_stoodDown;
    private static bool s_inFlight;
    private static int s_generation;

    private static float s_nextSampleTime;
    private static int s_samples;
    private static int s_errors;
    private static int s_sinceSummary;

    private static bool s_bandPresent;
    private static float s_bandSinceTime;
    private static int s_bandSamples;
    private static int s_episodes;
    private static bool s_heldFired;

    private static double s_captureMsTotal;
    private static double s_captureMsWorst;
    private static double s_analyseMsTotal;
    private static double s_analyseMsWorst;
    private static int s_timed;

    /// <summary>The closest a non-matching sample came since the last summary, and WHICH term
    /// blocked it. A coverage fraction with no named blocker cost this project six rounds once
    /// already ([[name-the-blocker-not-the-number]]).</summary>
    private static float s_nearBestAgreement = -1f;
    private static string s_nearBestWhy = string.Empty;

    // ---- SCRATCH (allocated once; the sample path allocates nothing) ----------------------------

    private static readonly float[] PrefixSum = new float[Rows * (CaptureWidth + 1)];
    private static readonly float[] PrefixSq = new float[Rows * (CaptureWidth + 1)];
    private static readonly float[] RowSteps = new float[Rows];
    private static readonly bool[] RowOk = new bool[Rows];

    /// <summary>
    /// Per-frame pump, called from <see cref="EyeReachCensus.Tick"/> — which is itself called from
    /// <c>OptionsToggle.Tick</c> only once a VR session is running. Two compares and a return in the
    /// steady state; the host below owns the actual cadence.
    /// </summary>
    internal static void Tick()
    {
        // Unity overloads operator== on Object, so this is a LIVENESS test, not a reference test: a
        // host destroyed by a scene teardown or a hot reload compares equal to null here and is
        // respawned on the next tick.
        if (s_stoodDown || s_host != null)
            return;
        try
        {
            Arm();
        }
        catch (System.Exception ex)
        {
            s_stoodDown = true;
            VRLog.Error(Scope, "EYE PIXEL WATCH failed to arm and is standing down — the census keeps its "
                + $"existing options-key triggers and nothing the player does is affected: {ex}");
        }
    }

    private static void Arm()
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            s_stoodDown = true;
            // HW-VERIFY: if this line is present, there is no pixel trigger in this log and every
            // EYE CENSUS run in it came from the options key alone.
            VRLog.Note(Scope, "EYE PIXEL WATCH STOOD DOWN before it started — this graphics device "
                + $"({SystemInfo.graphicsDeviceType}) reports no AsyncGPUReadback support, and reading the "
                + "frame back synchronously would stall the GPU every sample. The eye census still runs "
                + "from its options-key triggers.");
            return;
        }

        // A BepInEx ScriptEngine hot reload throws the old assembly's statics away but NOT the
        // GameObject they created, which would leave a coroutine capturing the screen every
        // SampleIntervalSeconds forever with nothing able to stand it down. Find and destroy the
        // stale one first. HideFlags.DontSave rather than HideAndDontSave for exactly this reason:
        // GameObject.Find cannot see an object flagged HideInHierarchy.
        GameObject? stale = GameObject.Find(HostName);
        if (stale != null)
            Object.Destroy(stale);

        var go = new GameObject(HostName) { hideFlags = HideFlags.DontSave };
        Object.DontDestroyOnLoad(go);
        s_host = go.AddComponent<Host>();
        s_nextSampleTime = Time.unscaledTime;

        // HW-VERIFY: the arming line. If a hardware round reports the band and this line is absent,
        // the watch never started and no PIXEL-TRIGGERED census could have fired.
        VRLog.Note(Scope, $"EYE PIXEL WATCH armed — it captures the game view backbuffer into a mod-owned "
            + $"{CaptureWidth}x{CaptureHeight} target every {SampleIntervalSeconds:F2} s (at most {MaxSamples} "
            + "sample(s) this session) and pulls it back with AsyncGPUReadback: no ReadPixels, no GPU stall, "
            + "no per-frame allocation. It fires an EYE CENSUS run when the PICTURE shows the band the user "
            + "photographed: a straight vertical edge at one column between "
            + $"{EdgeMinFraction:F2} and {EdgeMaxFraction:F2} of the width, holding across at least "
            + $"{RowAgreementMin:P0} of {Rows} rows spread over the full height, with the region left of it "
            + $"at most {UniformRatioMax:F2}x as varied as the scene beside it. The backbuffer is the LEFT "
            + "EYE while 'XR mirror mode=LeftEye', so this is the image he screenshotted, sampled AFTER "
            + "image effects, overlay canvases and IMGUI — three of the census's five blind spots are inside "
            + "this measurement.");
    }

    /// <summary>
    /// One capture, issued at end of frame. Separate from the coroutine because a <c>yield</c> cannot
    /// live inside a <c>try/catch</c>, and a throw in a diagnostic must never take anything else with
    /// it ([[a-thrown-listener-amputates-the-chain]]).
    /// </summary>
    private static void Capture()
    {
        if (s_stoodDown || s_inFlight)
            return;
        float now = Time.unscaledTime;
        if (now < s_nextSampleTime)
            return;
        s_nextSampleTime = now + SampleIntervalSeconds;

        // A LOCAL, not the field: Unity overloads operator== on Object, so `s_rt == null` is a
        // liveness test the compiler's nullable flow analysis cannot follow. The local is what the
        // rest of this method is allowed to assume is there.
        RenderTexture? rt = s_rt;
        if (rt == null)
        {
            // sRGB explicitly. The readback below is compared against thresholds calibrated on PNG
            // screenshots, which are sRGB-encoded; letting the project's colour space pick the
            // format would silently change every luminance the gates are measured in.
            rt = new RenderTexture(CaptureWidth, CaptureHeight, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "GloomhavenVR.EyePixelBandWatch",
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
            };
            // A target the driver refuses is not a target: bail rather than capture into nothing.
            if (!rt.Create())
            {
                s_stoodDown = true;
                Object.Destroy(rt);
                VRLog.Note(Scope, "EYE PIXEL WATCH STOOD DOWN — the "
                    + $"{CaptureWidth}x{CaptureHeight} capture target could not be created.");
                return;
            }
            s_rt = rt;
        }

        long t0 = Stopwatch.GetTimestamp();
        ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
        int gen = s_generation;
        int frame = Time.frameCount;
        AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req => OnRead(req, gen, frame));
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        s_inFlight = true;
        s_samples++;
        s_captureMsTotal += ms;
        if (ms > s_captureMsWorst)
            s_captureMsWorst = ms;
    }

    private static void OnRead(AsyncGPUReadbackRequest req, int gen, int issuedFrame)
    {
        if (gen != s_generation)
            return; // a readback from before a teardown; its buffers are gone
        s_inFlight = false;
        try
        {
            Analyse(req, issuedFrame);
        }
        catch (System.Exception ex)
        {
            s_stoodDown = true;
            VRLog.Error(Scope, "EYE PIXEL WATCH threw while analysing a capture and is standing down. The "
                + "watch is read-only, so nothing was left half-applied and the eye census keeps its "
                + $"options-key triggers: {ex}");
        }
    }

    private static void Analyse(AsyncGPUReadbackRequest req, int issuedFrame)
    {
        if (req.hasError)
        {
            s_errors++;
            if (s_errors == 1 || s_errors % 100 == 0)
            {
                VRLog.Note(Scope, $"EYE PIXEL WATCH readback ERROR ({s_errors} so far of {s_samples} "
                    + "sample(s)). If this count tracks the sample count, this runtime does not hand the "
                    + "backbuffer back and there is no pixel trigger in this log.");
            }
            return;
        }

        long t0 = Stopwatch.GetTimestamp();
        var data = req.GetData<Color32>();
        if (data.Length < CaptureWidth * CaptureHeight)
        {
            s_errors++;
            return;
        }

        // ---- LUMINANCE PREFIX SUMS ---------------------------------------------------------
        // One pass over the 16 analysed rows builds running sums of L and L^2, which makes the mean
        // and variance of ANY column span an O(1) read. Without them the edge search below would be
        // a 65-candidate x 16-row x 89-column scan; with them it is 65 x 16.
        const int Stride = CaptureWidth + 1;
        float frameSum = 0f;
        for (int r = 0; r < Rows; r++)
        {
            int y = r * RowStep + RowStep / 2;
            int src = y * CaptureWidth;
            int p = r * Stride;
            PrefixSum[p] = 0f;
            PrefixSq[p] = 0f;
            for (int x = 0; x < CaptureWidth; x++)
            {
                Color32 c = data[src + x];
                float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * (1f / 255f);
                PrefixSum[p + x + 1] = PrefixSum[p + x] + l;
                PrefixSq[p + x + 1] = PrefixSq[p + x] + l * l;
            }
            frameSum += PrefixSum[p + CaptureWidth];
        }
        float frameMean = frameSum / (Rows * CaptureWidth);

        // ---- THE EDGE SEARCH ---------------------------------------------------------------
        int bestK = -1;
        int bestAgree = -1;
        float bestBandStd = 0f;
        float bestSceneStd = 0f;
        float bestStep = 0f;
        bool bestTop = false;
        bool bestBot = false;
        bool bestDarker = false;

        for (int k = KMin; k <= KMax; k++)
        {
            int positives = 0;
            for (int r = 0; r < Rows; r++)
            {
                int p = r * Stride;
                float left = (PrefixSum[p + k] - PrefixSum[p + k - EdgeHalfWidth]) / EdgeHalfWidth;
                float right = (PrefixSum[p + k + EdgeHalfWidth] - PrefixSum[p + k]) / EdgeHalfWidth;
                float step = right - left;
                RowSteps[r] = step;
                if (step > 0f)
                    positives++;
            }
            // The band is one object: every row must step the SAME WAY across its edge. A scene
            // whose left fifth happens to be busy produces steps of both signs and fails here.
            float sign = positives * 2 >= Rows ? 1f : -1f;
            int agree = 0;
            float stepSum = 0f;
            for (int r = 0; r < Rows; r++)
            {
                float step = RowSteps[r];
                bool ok = step * sign >= EdgeStepFloor;
                RowOk[r] = ok;
                if (!ok)
                    continue;
                agree++;
                stepSum += step * sign;
            }

            float bandStd = SpanStd(0, k);
            if (agree < bestAgree || (agree == bestAgree && bandStd >= bestBandStd))
                continue;

            int a = k + EdgeHalfWidth;
            int b = Mathf.Min(CaptureWidth, a + k);
            bestK = k;
            bestAgree = agree;
            bestBandStd = bandStd;
            bestSceneStd = b > a ? SpanStd(a, b) : 1f;
            bestStep = agree > 0 ? stepSum / agree : 0f;
            // `step` is right-minus-left, so a POSITIVE majority means the scene is brighter than
            // the region left of the edge — i.e. the band is the darker of the two. His screenshot
            // is the other way round (band mean luminance 0.052 against a 0.029 forest), which is
            // why this is reported and never assumed: "dark grey" is a description of the band's
            // colour, not of its contrast against whatever happens to be behind it.
            bestDarker = sign > 0f;
            bestTop = false;
            bestBot = false;
            for (int r = 0; r < EdgeRows; r++)
            {
                if (RowOk[r])
                    bestTop = true;
                if (RowOk[Rows - 1 - r])
                    bestBot = true;
            }
        }

        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        s_analyseMsTotal += ms;
        if (ms > s_analyseMsWorst)
            s_analyseMsWorst = ms;
        s_timed++;
        s_sinceSummary++;

        float agreement = bestAgree > 0 ? bestAgree / (float)Rows : 0f;
        float ratio = bestSceneStd > 1e-6f ? bestBandStd / bestSceneStd : 99f;
        float edge = bestK > 0 ? bestK / (float)CaptureWidth : 0f;

        // Every term, named. A verdict that says only "no" is a verdict that has to be re-derived
        // later ([[name-the-blocker-not-the-number]]). EdgeStepFloor does not appear here because it
        // is applied PER ROW inside the search above: a row whose step is below it never counts
        // towards the agreement, so a frame with no real edge fails the agreement term instead.
        string why = string.Empty;
        if (agreement < RowAgreementMin)
            why = $"the edge holds on only {bestAgree}/{Rows} rows at or above a {EdgeStepFloor:F4} step "
                  + $"(needs {RowAgreementMin:P0})";
        else if (!bestTop || !bestBot)
            why = $"the edge does not span the full height (top sixth: {bestTop}, bottom sixth: {bestBot})";
        else if (bestBandStd > BandStdCeiling)
            why = $"the region left of it is not uniform: std {bestBandStd:F4} > {BandStdCeiling:F3}";
        else if (ratio > UniformRatioMax)
            why = $"it is not more uniform than the scene: {bestBandStd:F4}/{bestSceneStd:F4} = {ratio:F3} "
                  + $"> {UniformRatioMax:F2}";

        bool hit = why.Length == 0;
        int sceneA = Mathf.Min(CaptureWidth, bestK + EdgeHalfWidth);
        int sceneB = Mathf.Min(CaptureWidth, sceneA + bestK);
        float bandMean = SpanMean(0, bestK);
        float sceneMean = sceneB > sceneA ? SpanMean(sceneA, sceneB) : 0f;
        string geometry = $"edge at x={edge:F3} (column {bestK} of {CaptureWidth}), rows agreeing "
            + $"{bestAgree}/{Rows}, step {bestStep:F4} ({(bestDarker ? "the band is DARKER" : "the band is LIGHTER")} "
            + $"than the scene beside it), band mean luminance {bandMean:F4} against scene {sceneMean:F4}, "
            + $"bandStd {bestBandStd:F4}, sceneStd {bestSceneStd:F4}, "
            + $"ratio {ratio:F3}, frame mean luminance {frameMean:F4}";
        string cost = $"COST: capture {s_captureMsTotal / Mathf.Max(1, s_samples):F3} ms mean / "
            + $"{s_captureMsWorst:F3} ms worst on the main thread, analysis "
            + $"{s_analyseMsTotal / Mathf.Max(1, s_timed):F3} ms mean / {s_analyseMsWorst:F3} ms worst, "
            + $"{s_samples}/{MaxSamples} sample(s)";

        if (hit)
        {
            s_bandSamples++;
            if (!s_bandPresent)
            {
                s_bandPresent = true;
                s_bandSinceTime = Time.unscaledTime;
                s_heldFired = false;
                s_episodes++;
                // HW-VERIFY: THE PIXEL TRIGGER FIRED. This line means the band the user photographed
                // is on screen RIGHT NOW, measured off the frame itself and not inferred from any mod
                // state. The EYE CENSUS run that follows it is the paired reading.
                VRLog.Note(Scope, $"EYE PIXEL BAND ONSET (episode {s_episodes}) — the OUTPUT FRAME shows the "
                    + $"photographed shape: {geometry}. Captured on frame {issuedFrame}, analysed "
                    + $"{Time.frameCount - issuedFrame} frame(s) later (AsyncGPUReadback latency; the band "
                    + "persists and follows the head, so it is still on screen). Firing an EYE CENSUS run "
                    + $"NOW. {cost}.");
                EyeReachCensus.FirePixelTriggered("PIXEL-TRIGGERED (ONSET)", geometry);
            }
            else if (!s_heldFired && Time.unscaledTime - s_bandSinceTime >= HeldSeconds)
            {
                s_heldFired = true;
                // HW-VERIFY: the band is STANDING, not a transition. A second census a full
                // HeldSeconds into the same episode, when the head has moved and every one-frame
                // cascade has landed.
                VRLog.Note(Scope, $"EYE PIXEL BAND HELD — still on screen {Time.unscaledTime - s_bandSinceTime:F2} s "
                    + $"into episode {s_episodes} ({s_bandSamples} sample(s) matched so far): {geometry}. This is a "
                    + "STANDING object, not a one-frame transition. Firing a second EYE CENSUS run.");
                EyeReachCensus.FirePixelTriggered("PIXEL-TRIGGERED (HELD)", geometry);
            }
        }
        else
        {
            if (s_bandPresent)
            {
                s_bandPresent = false;
                VRLog.Note(Scope, $"EYE PIXEL BAND GONE — episode {s_episodes} lasted "
                    + $"{Time.unscaledTime - s_bandSinceTime:F2} s over {s_bandSamples} matching sample(s). The "
                    + $"frame no longer shows it: {why}. Last geometry: {geometry}.");
                s_bandSamples = 0;
            }
            if (agreement > s_nearBestAgreement)
            {
                s_nearBestAgreement = agreement;
                s_nearBestWhy = $"{geometry} — REJECTED because {why}";
            }
        }

        if (s_sinceSummary >= SummaryEverySamples)
        {
            s_sinceSummary = 0;
            // HW-VERIFY: the liveness line. It prints whether or not anything matched, so a log with
            // no ONSET line still proves the watch was running and says how close it came.
            VRLog.Note(Scope, $"EYE PIXEL SUMMARY {s_samples} sample(s), {s_errors} readback error(s), "
                + $"{s_episodes} band episode(s). CLOSEST non-matching frame since the last summary: "
                + $"{(s_nearBestAgreement < 0f ? "none analysed" : s_nearBestWhy)}. {cost}.");
            s_nearBestAgreement = -1f;
            s_nearBestWhy = string.Empty;
        }

        if (s_samples >= MaxSamples || EyeReachCensus.PixelBudgetSpent)
        {
            StandDown(s_samples >= MaxSamples
                ? $"it has taken its full {MaxSamples} sample(s)"
                : "the census budget it exists to spend is exhausted, so a further capture could not "
                  + "produce a paired reading ([[probe-that-answered-is-spent]])");
        }
    }

    /// <summary>Mean luminance over columns <c>[a, b)</c> of every analysed row. This is what makes a
    /// reported band checkable against the user's words: "translucent dark-grey" is a number, and his
    /// screenshot puts it at 0.052 against a 0.029 forest.</summary>
    private static float SpanMean(int a, int b)
    {
        const int Stride = CaptureWidth + 1;
        int n = Rows * (b - a);
        if (n <= 0)
            return 0f;
        float sum = 0f;
        for (int r = 0; r < Rows; r++)
        {
            int p = r * Stride;
            sum += PrefixSum[p + b] - PrefixSum[p + a];
        }
        return sum / n;
    }

    /// <summary>Standard deviation of the luminance over columns <c>[a, b)</c> of every analysed row,
    /// read straight off the prefix sums.</summary>
    private static float SpanStd(int a, int b)
    {
        const int Stride = CaptureWidth + 1;
        int n = Rows * (b - a);
        if (n <= 0)
            return 0f;
        float sum = 0f;
        float sq = 0f;
        for (int r = 0; r < Rows; r++)
        {
            int p = r * Stride;
            sum += PrefixSum[p + b] - PrefixSum[p + a];
            sq += PrefixSq[p + b] - PrefixSq[p + a];
        }
        float mean = sum / n;
        return Mathf.Sqrt(Mathf.Max(0f, sq / n - mean * mean));
    }

    private static void StandDown(string why)
    {
        if (s_stoodDown)
            return;
        s_stoodDown = true;
        VRLog.Note(Scope, $"EYE PIXEL WATCH STOOD DOWN — {why}. Totals: {s_samples} sample(s), "
            + $"{s_errors} readback error(s), {s_episodes} band episode(s). COST over the whole session: "
            + $"capture {s_captureMsTotal:F1} ms and analysis {s_analyseMsTotal:F1} ms, both on the main "
            + "thread, spread over the sample count above.");
        Release();
    }

    /// <summary>Drop the host and the capture target. The generation bump makes any readback still in
    /// flight discard itself on arrival rather than read a freed buffer.</summary>
    private static void Release()
    {
        s_generation++;
        bool wasInFlight = s_inFlight;
        s_inFlight = false;
        Host? host = s_host;
        s_host = null;
        if (host != null)
            Object.Destroy(host.gameObject);
        RenderTexture? rt = s_rt;
        s_rt = null;
        if (rt != null)
        {
            // A readback may still be copying out of this target, and destroying it underneath a
            // driver copy is how a probe takes the process with it. One stall on a teardown path is
            // the price of never doing that — but ONLY when there is something to wait for: the
            // usual caller is the readback callback itself, which has already completed the only
            // request this watch ever has in flight, and calling WaitAllRequests from inside one is
            // a re-entrancy nobody needs to find out about.
            if (wasInFlight)
                AsyncGPUReadback.WaitAllRequests();
            rt.Release();
            Object.Destroy(rt);
        }
    }

    /// <summary>
    /// The end-of-frame pump. <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c> reads the
    /// backbuffer and is only meaningful after everything has been drawn into it, which is what
    /// <see cref="WaitForEndOfFrame"/> waits for — and it is precisely why this watch can see image
    /// effects, overlay canvases and IMGUI that the census cannot. The yield instruction is cached,
    /// so the loop allocates nothing per frame.
    /// </summary>
    private sealed class Host : MonoBehaviour
    {
        private static readonly WaitForEndOfFrame EndOfFrame = new();

        private void OnEnable() => StartCoroutine(Pump());

        private IEnumerator Pump()
        {
            while (!s_stoodDown)
            {
                yield return EndOfFrame;
                Capture();
            }
        }

        private void OnDestroy()
        {
            // A hot reload or a module teardown destroys this object without going through
            // StandDown; make sure nothing is left pointing at a dead host.
            if (s_host == this)
                s_host = null;
            s_generation++;
            s_inFlight = false;
        }
    }
}
