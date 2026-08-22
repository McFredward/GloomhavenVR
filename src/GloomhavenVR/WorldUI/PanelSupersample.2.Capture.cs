// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here.

using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- allocation ---------------------------------------------------------------------------

    /// <summary>
    /// Estimated VRAM for ONE panel: the capture target (colour + 24/8 depth-stencil + the
    /// multisample colour and depth surfaces when MSAA is on) PLUS the separate single-sample
    /// display target and its mip chain. Deliberately an over-estimate rather than an
    /// under-estimate — a budget that lies low is not a budget.
    /// <para>Worked example, and the one the caps are sized from: at 1920x1080 MSAA 4x the capture
    /// side is 7.9 (colour) + 7.9 (D24S8) + 63.3 (4 samples of both) = 79.1 MB and the display side
    /// is 7.9 + 2.6 (mips) = 10.5 MB, i.e. 89.7 MB for the pair. The ModBuild 192 log's 81.7 MB for
    /// the same window is the same arithmetic WITHOUT the split (mips were charged to the
    /// multisampled target, where they never existed).</para>
    /// </summary>
    private static long VramBytesFor(int w, int h, int msaa)
        => CaptureVramBytesFor(w, h, msaa) + MipVramBytesFor(w, h);

    private static long CaptureVramBytesFor(int w, int h, int msaa)
    {
        long px = (long)w * h;
        long colour = px * 4;
        long depthStencil = px * 4;
        long multisample = msaa > 1 ? (colour + depthStencil) * msaa : 0;
        return colour + depthStencil + multisample;
    }

    private static long MipVramBytesFor(int w, int h)
    {
        long colour = (long)w * h * 4;
        return colour + colour / 3;
    }

    private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("F1");

    // ---- the mip LOD offset (ModBuild 203) ----------------------------------------------------

    /// <summary>
    /// <b>THE LAST LEVER ON A MINIFIED WINDOW, and this build finally pulls it.</b> The user's
    /// report is "die Auflösung kommt mir aber immer noch etwas gering vor bei den Sub-Menus", and
    /// this class's own instrument had already named the only remaining answer: the ModBuild 202 log
    /// reads the party window <c>MINIFIED 1.58x</c> (peak 1.86x, 16 of 19 measurements minified at
    /// all) at <c>mipMapBias 0.00</c>, with the capture factor confirmed at <c>asked 2.00 / ACHIEVED
    /// 2.00</c>. The factor buys render-target texels per AUTHORED pixel; a minified window's sampler
    /// already selects a mip level at or below authored resolution, so every level the factor adds
    /// above it is a level the hardware never reads. Only two levers remain — the window's size in
    /// the eye (<c>[WorldUI] WindowLegibility</c>, which this file does not own) and this one.
    ///
    /// <para><b>WHAT THE NUMBER PHYSICALLY BUYS AND COSTS, in one identity.</b> Trilinear picks
    /// <c>LOD = log2(texelsPerRenderedPixel)</c>, i.e. the level with ~1 texel per rendered pixel. A
    /// bias <c>b</c> shifts that choice to <c>LOD + b</c>, and the level then sampled carries
    /// <c>2^-b</c> texels per rendered pixel — INDEPENDENT of how minified the window is. So -0.5
    /// hands back half an octave of trilinear's deliberate over-blur at 1.41 texels per pixel, and
    /// -1.0 reads 2.00 texels per pixel: twice what the pixel grid can carry, which is exactly the
    /// undersampling this whole path was built to remove. That is the trade in a number, and it is
    /// what the report line below prints.</para>
    ///
    /// <para><b>WHY IT IS CLAMPED AT 0 ON TOP.</b> A positive bias is BLUR — what the mip chain
    /// already does correctly — and would only re-buy the complaint. The floor of -2.0 is a floor and
    /// not a recommendation: it exists so someone experimenting can reach the failure and SEE it.</para>
    /// </summary>
    private const float MipLodOffsetMin = -2f;

    /// <summary>The top of the range — see <see cref="MipLodOffsetMin"/>. 0 is "the filtering you
    /// have today", which is what every build up to ModBuild 202 shipped.</summary>
    private const float MipLodOffsetMax = 0f;

    /// <summary>Bias write-backs are compared against the ask with this tolerance, in mip levels.
    /// Two orders of magnitude below the stepper's own 0.05 press, so a real user value can never be
    /// mistaken for a write that did not land.</summary>
    private const float MipLodOffsetEpsilon = 1e-4f;

    /// <summary>How often the bias line is printed, per window. Matches the state line's own cadence
    /// so the two can be read side by side in the log.</summary>
    private const float MipBiasReportSeconds = 10f;

    /// <summary>Per-window due time for <see cref="ReportMipLodOffset"/>. Keyed by window name and
    /// not by <c>Entry</c>, because the entry's fields belong to another file of this class.</summary>
    private static readonly Dictionary<string, float> MipBiasNextReport =
        new Dictionary<string, float>();

    /// <summary>One warn per session if a write does not survive — see
    /// <see cref="ApplyMipLodOffset"/>.</summary>
    private static bool _mipBiasWriteWarned;

    // ---- ModBuild 204: a re-allocation must be ATOMIC from the eye's point of view --------------

    /// <summary>
    /// <b>THE USER'S REPORT, VERBATIM:</b> <i>"das Flackerproblem WÄHREND DER BEWEGUNG ist noch da —
    /// inklusive der möglichen kaputten Darstellung, wenn man nach der Bewegung ABRUPT loslässt"</i>,
    /// and from the newest round <i>"mittlerweile taucht es auch initial kaputt auf wenn man das
    /// Fenster öffnet"</i>.
    ///
    /// <para><b>WHAT THE ModBuild 203 HARDWARE LOG MEASURED, and it is the reason this block
    /// exists.</b> The display render target was re-allocated <b>58 times in 62 seconds</b>,
    /// alternating <b>28/28</b> between two capture frames exactly one <see cref="FrameQuantumPx"/>
    /// (32 px) quantum apart, at <b>2.51 re-allocations per second while the hand held the window</b>
    /// against <b>0.29/s while it did not</b>. <b>45 of the 58 fell inside grabs and 12 inside a
    /// single 2.8 s stretch.</b> A parallel lane removes the FLAP. This block makes a re-allocation
    /// HARMLESS when one legitimately happens — at open, at a sub-view change and at a genuine
    /// resize — because those three will keep happening after the flap is gone.</para>
    ///
    /// <para><b>THE GAP THE 203 FORENSICS NAMED, verbatim:</b> <i>"the expected-zero set covers a
    /// size mismatch between a capture and its resolve, but there is no counter for frames on which
    /// the quad sampled a freshly re-allocated target that had not yet been captured into. With 58
    /// re-allocations, 45 of them inside grabs and 12 inside a single 2.8 s stretch, that is the one
    /// path a 'random frozen state' could take that this log cannot see."</i></para>
    ///
    /// <para><b>WHAT THE AUDIT OF THE EXISTING PATH ESTABLISHED, with file:line, because two of the
    /// four answers are "already safe" and a later round must not re-fix them.</b>
    /// <list type="number">
    /// <item><b>IS THE OLD TARGET RELEASED BEFORE THE NEW ONE HAS BEEN RENDERED INTO? NO — and it
    /// never was.</b> <see cref="Reallocate"/> creates the new capture target, calls
    /// <see cref="AttachMipTarget"/>, re-points the camera and the quad, and only then calls
    /// <c>Release()</c>/<c>Destroy()</c> on the old pair, all inside ONE statement block of ONE
    /// LateUpdate. <c>LateTick</c> (<c>PanelSupersample.1.Core.cs</c>) drives it, so the swap happens
    /// before the frame's camera loop has started, and the capture camera at <c>depth = -200</c> then
    /// renders into the new target ahead of every other camera in the frame. So on the COMMON path
    /// the new target is captured, resolved and mipped in the same frame it was created, before any
    /// eye pass. The old target's release is not the hazard.</item>
    /// <item><b>IS THERE A FRAME IN WHICH THE QUAD SAMPLES A TARGET THAT HAS BEEN Create()d BUT NOT
    /// CAPTURED INTO? THE CONTENTS ARE NOT UNDEFINED — <see cref="ClearRt"/> has run a
    /// <c>GL.Clear</c> on every new target since ModBuild 193, so level 0 is transparent black rather
    /// than whatever that VRAM last held. THE MIP CHAIN WAS THE REAL HOLE (see 3).</b> The frames on
    /// which it could happen at all are the ones this class does NOT control: a frame on which
    /// <see cref="SyncVisibility"/> leaves the camera disabled while the quad stays visible (<c>visible
    /// &amp;&amp; !due</c> — already counted as <see cref="Entry.CameraOffWhileVisible"/>, and
    /// structurally impossible while <see cref="CaptureIntervalFrames"/> is 1), a re-allocation from
    /// <see cref="ReleaseRepair"/>, which runs AFTER <c>SyncVisibility</c> in the same LateUpdate and
    /// therefore cannot re-enable a camera that frame, and any frame on which the capture camera fails
    /// to render for a reason outside this file. <b>THOSE THREE ARE UNVERIFIED, NOT PROVEN ABSENT:</b>
    /// nothing in the code makes them impossible and no instrument before ModBuild 204 could see one.
    /// That is exactly what <see cref="NoteQuadSample"/> now measures.</item>
    /// <item><b>IS THE MIP CHAIN OF A NEW TARGET GENERATED BEFORE THE FIRST EYE PASS THAT SAMPLES IT?
    /// BEFORE THIS BUILD, NO — AND THIS IS THE FINDING.</b> <c>GL.Clear</c> clears the BOUND SURFACE,
    /// i.e. mip level 0, and <see cref="CreateMipRt"/> sets <c>autoGenerateMips = false</c> on
    /// purpose, so levels 1..N of a fresh display target held uninitialised VRAM until the first
    /// <see cref="ResolveAndMip"/> called <c>GenerateMips()</c>. This window samples at a measured
    /// <b>LOD 1.66</b> — levels 1 and 2 almost exclusively — so any frame that reached the eye first
    /// would show garbage or nothing, not a slightly wrong image, which is the shape of the "random
    /// frozen state" report.</item>
    /// <item><b>CAN THE QUAD'S SIZE OR UV MAPPING DISAGREE WITH ITS TARGET FOR A FRAME? NO.</b>
    /// <see cref="SyncGeometry"/> writes <c>DisplayRect.sizeDelta = Frame.size</c> and then calls
    /// <see cref="Reallocate"/> with the SAME <c>Frame</c>, in that order, in one method. The
    /// <see cref="RawImage"/>'s <c>uvRect</c> is left at the default <c>(0,0,1,1)</c> by
    /// <see cref="BuildDisplay"/> and is never written by anything in this class, so the mapping is
    /// the identity for every target the quad will ever be pointed at — a target size change alters
    /// texel DENSITY and can never alter the mapping. The one case where the two "disagree" is a
    /// re-allocation the budget or the driver REFUSED: the quad takes the new size and the target
    /// keeps the old one, which is a resolution loss measured by <see cref="RecordAchievedFactor"/>
    /// and not a geometry fault.</item>
    /// </list></para>
    ///
    /// <para><b>THE INVARIANT THIS BUILD SHIPS.</b> <i>The quad never samples a display target that
    /// has not had at least one complete capture + resolve + mip generation.</i> It is delivered in
    /// two layers, deliberately, because one of them is universal and cheap and the other is exact
    /// and rare:
    /// <list type="number">
    /// <item><b>THE FLOOR, in <see cref="CreateMipRt"/>:</b> every mipped target has
    /// <c>GenerateMips()</c> run on it immediately after its <see cref="ClearRt"/>, so its WHOLE
    /// chain is defined (as downsampled transparent black) from the instant it exists. This closes
    /// the hazard on every path, including engage and including any path not enumerated here.
    /// <c>ClearRt</c> only ever wrote <b>mip level 0</b> — <c>GL.Clear</c> clears the bound surface,
    /// and <c>autoGenerateMips</c> is off on purpose — so before this build levels 1..N of a fresh
    /// target held whatever that VRAM last contained. This window samples at a measured
    /// <b>LOD 1.66</b>, i.e. it reads levels 1 and 2 almost exclusively: exactly the levels that were
    /// undefined. Cost: one mip-chain generation per allocation, which the allocation was going to
    /// pay one frame later anyway.</item>
    /// <item><b>THE EXACT GUARANTEE, in <see cref="PrimeNewTarget"/>:</b> a re-allocation renders the
    /// capture camera SYNCHRONOUSLY into the new pair and resolves + mips it before
    /// <see cref="Reallocate"/> returns — i.e. inside the LateUpdate that swapped the targets, before
    /// any camera in the frame has culled. The new target is therefore never merely defined, it is
    /// CORRECT, on the very frame it is bound to the quad.</item>
    /// </list></para>
    ///
    /// <para><b>REJECTED: DOUBLE-BUFFERING THE DISPLAY TARGET</b> (keep the old one bound to the quad
    /// until the new one has been filled once, then swap and release). It is the cleanest shape in the
    /// abstract and it fails this window on the arithmetic. At <see cref="PreferredMsaa"/> 1 a pair
    /// costs <c>w·h·13.33</c> bytes (<see cref="VramBytesFor"/>: 4 colour + 4 depth-stencil on the
    /// capture side, 4 + 4/3 on the display side), so the capture side is 60 % of a pair and the
    /// display side 40 %. This window's pair is ~150 MB = ~90 MB capture + ~60 MB display. Retaining
    /// one extra display target puts the panel at <b>210 MB against a
    /// <see cref="MaxPanelVramBytes"/> of 160 MB</b> — 50 MB over, 31 %. And the overrun is not
    /// merely a number in a log: <see cref="Reallocate"/> derives its budget as
    /// <c>min(MaxPanelVramBytes, MaxTotalVramBytes - (_vramTotal - e.VramBytes))</c> and hands it to
    /// <see cref="ResolveRate"/>, whose step-down loop walks the rate down by
    /// <see cref="RateQuantum"/> until the pair fits. Charging a retained 60 MB display target into
    /// <c>_vramTotal</c> would cut this panel's budget to ~100 MB = 7.9 Mtexel against the ~11.8
    /// Mtexel it needs, so the rate would step from ~2.4 to ~1.95 and STAY there — the band limit
    /// silently traded away on every re-allocation, which is precisely the failure
    /// <see cref="BandLimitFactor"/> exists to prevent. NOT charging it would make the budget a lie.
    /// Both branches are worse than the disease.</para>
    ///
    /// <para><b>REJECTED: CARRYING THE OLD IMAGE OVER</b> (blit the old display target into the new
    /// one, then <c>GenerateMips</c>, instead of taking a real capture). It costs nothing and needs no
    /// camera, and it is what <see cref="PrimeNewTarget"/> falls back to if the synchronous render
    /// fails — but as the PRIMARY it ships a resampled photograph of the PREVIOUS frame at the NEW
    /// size, i.e. a deliberate one-frame stretch. On the 32-px-quantum flap that stretch is under
    /// 3 %; on a genuine two-hand resize it is the whole scale ratio, which is the "manche Elemente
    /// nicht richtig dargestellt" artifact this class already removed once (see
    /// <see cref="SyncGeometry"/>'s ORDERING paragraph). A real capture costs one camera render on a
    /// rare frame and has no such artifact.</para>
    ///
    /// <para><b>NOTE ON WHAT WAS ALREADY TRUE.</b> <see cref="Reallocate"/> has always created the new
    /// pair BEFORE releasing the old, so this class already peaks at two full pairs (~300 MB for this
    /// window) for the few statements between them, and <c>_vramTotal</c> has never seen that peak —
    /// it tracks the STEADY state only. Nothing here changes that accounting; the synchronous prime
    /// adds no allocation at all, which is the second reason it was chosen over double-buffering.</para>
    /// </summary>
    private sealed class TargetLife
    {
        // ---- the display target currently bound to the quad -------------------------------------

        /// <summary><see cref="Object.GetInstanceID"/> of the texture the quad samples, 0 = none.</summary>
        public int TargetId;

        /// <summary><c>Time.frameCount</c> at which that texture was created.</summary>
        public int CreatedFrame;

        /// <summary>Complete capture + resolve + mip passes into THAT texture since it was created.
        /// The whole of the ModBuild 204 invariant is "this is &gt; 0 whenever the eye samples it".</summary>
        public int CompletedCaptures;

        // ---- item 3: the expected-zero counter that did not exist -------------------------------

        /// <summary>Frames on which the quad was VISIBLE and the texture it samples had never been
        /// captured into since its creation. EXPECTED ZERO.</summary>
        public int SampledUncaptured;

        /// <summary>The denominator: visible frames on which the check actually ran. Without it a
        /// zero cannot be told from an instrument that never executed.</summary>
        public int SampleChecks;

        /// <summary>Frames elapsed between a target being created and its FIRST completed
        /// capture + resolve + mip — the whole distribution, because a worst is one question's
        /// answer and the next question's blind spot.</summary>
        public int FillFramesLast = -1, FillFramesMin = int.MaxValue, FillFramesMax = -1, FillReadings;
        public long FillFramesSum;

        // ---- item 2: what the prime did ---------------------------------------------------------

        public int Primed, PrimeCarriedOver, PrimeFailed;
        public double PrimeMs;

        // ---- item 4: pricing the re-allocation itself -------------------------------------------

        /// <summary>Re-allocations that actually built a new pair, by <see cref="ReallocTrigger"/>,
        /// and the same split again for the ones taken while the window was MOVING. The 203 log could
        /// only say "58 since engage", which is why it took a session to see that 45 were in grabs.</summary>
        public readonly int[] ByTrigger = new int[ReallocTriggerCount];
        public readonly int[] ByTriggerMoving = new int[ReallocTriggerCount];

        /// <summary><see cref="Reallocate"/> calls that changed nothing: the pixel counts came back
        /// identical, so the early-out ran and no target was touched. Split the same way.</summary>
        public readonly int[] NoOpByTrigger = new int[ReallocTriggerCount];

        public int Refusals;
        public double CreateMs, DestroyMs;

        // ---- item B: the hand -------------------------------------------------------------------

        /// <summary>Frames of the CURRENT drag on which <c>ConvertedPanel.GuardHostHeld</c> read
        /// true, and the frames sampled — the denominator, same rule as everywhere else here.</summary>
        public int DragHandOnFrames, DragHandSamples;

        /// <summary>The frame on which the hand was last observed letting go. The release gate is
        /// keyed on the LATER of this and <c>Entry.LastMotionFrame</c>, so "released" means the hand
        /// is off AND the pose has settled.
        /// <para>The "never held" sentinel is <c>int.MinValue / 2</c> and NOT <c>int.MinValue</c>, on
        /// purpose: <c>Time.frameCount - int.MinValue</c> overflows and the cadence built on it then
        /// never fires, which is a bug this project has already shipped once
        /// ("sentinel-overflow-and-silent-scans"). At half the range the subtraction is a large
        /// positive number for any real frame count, which is the reading a window nobody has ever
        /// grabbed must produce: released long ago, gate governed purely by stillness.</para></summary>
        public int HandReleaseFrame = int.MinValue / 2;

        /// <summary>True on the previous sample — the edge detector for the field above.</summary>
        public bool HandWasOn;

        /// <summary>Release repairs the hand gate DEFERRED. In the 203 log this number would have
        /// been 15 of 33.
        /// <para>It counts EVENTS, not frames. A hand that pauses mid-drag holds the stillness gate
        /// open for as long as it hovers, so a naive increment would read in the hundreds per drag
        /// and say nothing; keyed on <see cref="Entry.LastMotionFrame"/> — the same stamp
        /// <c>ReleaseRepairStage</c>'s own re-arm is keyed on — one pause is one deferral, which is
        /// exactly the shape of the 203 log's "one drag fired five of them in 1.9 s".</para></summary>
        public int ReleasesDeferredHeld;

        /// <summary>The motion stamp the last deferral was counted against — see above.</summary>
        public int DeferredAtMotionFrame = int.MinValue / 2;

        /// <summary>Release repairs that ran, split by whether the window has ever been held at all —
        /// a window moved by a settings change or a re-fit is never grabbed and must still be
        /// repaired.</summary>
        public int ReleasesAfterHold, ReleasesNeverHeld;
    }

    /// <summary>
    /// Why a re-allocation was asked for. The ModBuild 203 log could attribute none of its 58, which
    /// is the whole reason the 28/28 flap between two frames one quantum apart took a session to see.
    /// </summary>
    private enum ReallocTrigger
    {
        /// <summary>The host transform's SCALE or its RectTransform's SIZE changed — a two-hand
        /// resize, or the content fit writing <c>HostRect.sizeDelta</c>. See
        /// <see cref="NoticeGeometry"/>, which deliberately does NOT report a pure translation.</summary>
        HostGeometry = 0,

        /// <summary>The measured capture frame moved past <see cref="RectChangeFraction"/> of the
        /// frame the target was sized for. This is the trigger the 32-px flap rides.</summary>
        ContentFrame = 1,

        /// <summary>The smallest substantial content scale moved — a tab press inside ONE host rect,
        /// which changes nothing about the host and doubles the rate the target must be sized for.
        /// See <see cref="Entry.MinContentScale"/>.</summary>
        SubViewScale = 2,

        /// <summary><see cref="ReleaseRepair"/>'s call, which is unconditional. See there.</summary>
        ReleaseRepair = 3,
    }

    private const int ReallocTriggerCount = 4;

    private static readonly string[] ReallocTriggerNames =
    {
        "host rect/scale change", "content frame change", "sub-view scale change", "release repair",
    };

    /// <summary>Per-window ModBuild 204 bookkeeping. Keyed by window name and NOT held on
    /// <see cref="Entry"/>, because that type lives in <c>PanelSupersample.1.Core.cs</c>, which this
    /// lane does not own — the same reason and the same shape as
    /// <see cref="MipBiasNextReport"/>.</summary>
    private static readonly Dictionary<string, TargetLife> Lives = new Dictionary<string, TargetLife>(8);

    /// <summary>The record for this window, created on first ask. One dictionary lookup; the only
    /// per-frame consumer is <see cref="NoteQuadSample"/>, which runs once per VISIBLE panel per
    /// frame inside a loop <see cref="OnPreCull"/> already performs.</summary>
    private static TargetLife LifeOf(Entry e) => LifeOf(e.Window);

    private static TargetLife LifeOf(string window)
    {
        if (!Lives.TryGetValue(window, out TargetLife life))
        {
            life = new TargetLife();
            Lives[window] = life;
        }
        return life;
    }

    /// <summary>The quad has just been pointed at <paramref name="shown"/>. Everything the invariant
    /// is stated about is keyed on this identity, so it is recorded in exactly the two places the
    /// <c>RawImage.texture</c> is written — <see cref="BuildDisplay"/> and
    /// <see cref="Reallocate"/>.</summary>
    private static void BindDisplayTarget(Entry e, RenderTexture? shown)
    {
        TargetLife life = LifeOf(e);
        int id = shown != null ? shown.GetInstanceID() : 0;
        if (life.TargetId == id)
            return;
        life.TargetId = id;
        life.CreatedFrame = Time.frameCount;
        life.CompletedCaptures = 0;
    }

    /// <summary>
    /// A capture + resolve + mip pass into the displayed target has just COMPLETED. Called from
    /// <see cref="OnPostRender"/> (after <see cref="ResolveAndMip"/>) and from
    /// <see cref="PrimeNewTarget"/>, and from nowhere else — "completed" must never be inferred.
    /// <para>It is keyed on <see cref="DisplayTexture"/> rather than on <see cref="Entry.MipRt"/> so
    /// the <see cref="Entry.MipFallback"/> case is measured by the same counter: there the quad shows
    /// the capture target and the camera's own render IS the fill.</para>
    /// </summary>
    private static void NoteCaptureCompleted(Entry e)
    {
        RenderTexture shown = DisplayTexture(e);
        if (shown == null)
            return;
        TargetLife life = LifeOf(e);
        if (life.TargetId != shown.GetInstanceID())
            return; // a target nobody is showing; the bind will start its life
        if (life.CompletedCaptures == 0)
        {
            int frames = Time.frameCount - life.CreatedFrame;
            if (frames < 0)
                frames = 0;
            life.FillFramesLast = frames;
            if (frames < life.FillFramesMin)
                life.FillFramesMin = frames;
            if (frames > life.FillFramesMax)
                life.FillFramesMax = frames;
            life.FillFramesSum += frames;
            life.FillReadings++;
        }
        life.CompletedCaptures++;
    }

    /// <summary>
    /// <b>THE PER-FRAME CHECK ITEM 3 ASKS FOR, and it is a real test of live state rather than a
    /// derivation.</b> Run once per VISIBLE panel per frame from <see cref="OnPreCull"/>'s existing
    /// once-per-frame loop — i.e. at the FIRST camera of the frame that is not one of ours. Every
    /// capture camera sits at <c>depth = -200</c> and every other camera in this game sits far above
    /// it, so by that instant each panel's own capture and resolve for this frame have already run:
    /// a hit here is a target that genuinely reached an eye pass unfilled.
    /// <para>Cost: one dictionary lookup and one integer compare per visible panel per frame, inside
    /// a loop that already existed. It adds no allocation and no work to a still window.</para>
    /// <para>THE ONE CAVEAT, stated rather than hidden: if the game ever runs a camera at a depth
    /// BELOW -200, this check would run before that frame's capture and over-report. That direction is
    /// the safe one for an expected-zero counter — it can raise a false alarm, never give a false
    /// all-clear — and the FILL LATENCY distribution printed beside it settles which of the two a
    /// non-zero reading is.</para>
    /// </summary>
    private static void NoteQuadSample(Entry e)
    {
        if (e.DisplayGo == null || !e.DisplayGo.activeSelf)
            return;
        RenderTexture shown = DisplayTexture(e);
        if (shown == null)
            return;
        TargetLife life = LifeOf(e);
        life.SampleChecks++;
        if (life.TargetId == shown.GetInstanceID() && life.CompletedCaptures == 0)
            life.SampledUncaptured++;
    }

    /// <summary>
    /// Set for exactly the duration of a <see cref="PrimeNewTarget"/> render. <c>Camera.Render()</c>
    /// fires this class's own <see cref="OnPreCull"/> and <see cref="OnPostRender"/> handlers
    /// re-entrantly, and a priming capture must not be counted as a frame's capture: the CAPTURE PATH
    /// field's whole readability rests on captures, resolves and moving/still frames being the same
    /// denominator. The handlers return early on this reference and the prime does its own resolve.
    /// </summary>
    private static Entry? _primingEntry;

    /// <summary>One warn per session if the synchronous prime is not available — see
    /// <see cref="PrimeNewTarget"/>.</summary>
    private static bool _primeFailWarned;

    /// <summary>
    /// <b>MAKE THE RE-ALLOCATION ATOMIC: fill the new pair before anything can sample it.</b> Called
    /// from <see cref="Reallocate"/> after the new targets are bound to the camera and the quad and
    /// BEFORE the old ones are released — so the carry-over fallback still has a source.
    ///
    /// <para><b>WHY A SYNCHRONOUS RENDER IS SAFE HERE.</b> <see cref="Reallocate"/> runs inside
    /// <c>LateTick</c>, i.e. in LateUpdate, before the frame's camera loop has started. Rendering the
    /// capture camera by hand at that moment produces exactly the image the camera loop would produce
    /// a few milliseconds later from the same transforms — <see cref="SyncProjection"/> is re-derived
    /// immediately before the call, which is the same write the camera's own
    /// <see cref="Camera.onPreCull"/> makes. The camera then renders AGAIN in the loop, which is one
    /// extra capture on a rare frame and is measured into <see cref="TargetLife.PrimeMs"/>.</para>
    ///
    /// <para><b>AND IT ADDS NO PER-FRAME COST.</b> Nothing in this method runs on a frame without a
    /// re-allocation. Once the parallel lane's hysteresis lands, that is a handful of frames per
    /// session instead of the 203 log's 58 in 62 s.</para>
    ///
    /// <para><b>THE FALLBACK, and why it exists at all.</b> If the camera is unusable or
    /// <c>Render()</c> throws, the new display target is filled by blitting the OLD one into it and
    /// generating the chain — a resampled photograph of the last good frame at the new size. It is
    /// the option rejected as a primary (see <see cref="TargetLife"/>), and it is the right last
    /// resort: one frame of a &lt;3 % stretch beats one frame of an uninitialised chain read at
    /// LOD 1.66. If even that is impossible the chain is still DEFINED, because
    /// <see cref="CreateMipRt"/> generated it at creation.</para>
    /// </summary>
    private static void PrimeNewTarget(Entry e, RenderTexture? previousShown)
    {
        TargetLife life = LifeOf(e);
        float start = Time.realtimeSinceStartup;
        bool primed = false;
        // activeInHierarchy, not just non-null: Camera.Render() on a camera inside a deactivated
        // subtree logs an engine error rather than throwing, which no try/catch here could turn into
        // a counted fallback. A panel whose host is deactivated has a hidden quad anyway
        // (SyncVisibility), so the carry-over is the right answer for it and PrimeFailed says so.
        if (e.Cam != null && e.CamGo != null && e.CamGo.activeInHierarchy && e.Rt != null
            && e.Layer >= 0)
        {
            _primingEntry = e;
            try
            {
                SyncProjection(e);
                e.Cam.Render();
                ResolveAndMip(e, priming: true);
                NoteCaptureCompleted(e);
                life.Primed++;
                primed = true;
            }
            catch (System.Exception ex)
            {
                if (!_primeFailWarned)
                {
                    _primeFailWarned = true;
                    VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: the synchronous prime of '{e.Window}'s new "
                                      + $"render target threw ({ex.GetType().Name}: {ex.Message}). THE "
                                      + "CONSEQUENCE: this re-allocation falls back to carrying the "
                                      + "previous image over instead of taking a fresh capture, so the "
                                      + "quad shows one frame of the last good image resampled to the "
                                      + "new size. The mip chain is still fully defined (it is "
                                      + "generated at creation), so nothing can read uninitialised "
                                      + "VRAM either way. Capture, MSAA, geometry and input are "
                                      + "unaffected.");
                }
            }
            finally
            {
                _primingEntry = null;
            }
        }
        if (!primed)
        {
            life.PrimeFailed++;
            if (CarryOverInto(e, previousShown))
                life.PrimeCarriedOver++;
        }
        life.PrimeMs += (Time.realtimeSinceStartup - start) * 1000.0;
    }

    /// <summary>Fill the new display target from the previous one and regenerate its chain. Returns
    /// false when there is nothing to carry over (engage), where the cleared-and-generated chain from
    /// <see cref="CreateMipRt"/> is already a defined image.
    /// <para><see cref="RenderTexture.active"/> is saved and restored for the same reason
    /// <see cref="ResolveAndMip"/> does it: <c>Graphics.Blit</c> re-points it at its destination.</para></summary>
    private static bool CarryOverInto(Entry e, RenderTexture? previousShown)
    {
        RenderTexture shown = DisplayTexture(e);
        if (shown == null || previousShown == null || ReferenceEquals(shown, previousShown))
            return false;
        RenderTexture? active = RenderTexture.active;
        try
        {
            Graphics.Blit(previousShown, shown);
            if (shown.mipmapCount > 1)
                shown.GenerateMips();
            return true;
        }
        catch
        {
            return false; // never throw out of a re-allocation
        }
        finally
        {
            RenderTexture.active = active;
        }
    }

    /// <summary>
    /// <c>[WorldUI] PanelMipLodOffset</c>, read LIVE and clamped to the shipped range. A user-set
    /// value is taken verbatim inside that range — there is deliberately no floor of the ModBuild 198
    /// kind here, because this dial ships at a value that already does something and a floor over a
    /// tuned value is how ModBuild 198's experiment came to never execute.
    /// </summary>
    private static float AskedMipLodOffset()
    {
        float v = WorldUIConfig.PanelMipLodOffset != null
            ? WorldUIConfig.PanelMipLodOffset.Value
            : Defaults.PanelMipLodOffset;
        return float.IsNaN(v) ? 0f : Mathf.Clamp(v, MipLodOffsetMin, MipLodOffsetMax);
    }

    /// <summary>
    /// Write the asked offset onto a live render target and RETURN WHAT THE TEXTURE THEN READS —
    /// never what was asked for. Writes only when the two differ, so this is a float compare on the
    /// common path and never a write war (nothing else in this process writes
    /// <c>mipMapBias</c> on these targets; they are created here and destroyed here).
    ///
    /// <para><b>WHICH ORDER STICKS, AND HOW THAT WAS VERIFIED.</b> The bias is written TWICE in
    /// <see cref="CreateMipRt"/> — once in the object initializer, before <c>Create()</c>, and once
    /// through this method immediately after a successful <c>Create()</c> — and only the read-back is
    /// ever reported. What was actually checked, rather than assumed: <c>mipMapBias</c> is declared on
    /// <c>UnityEngine.Texture</c> (the game's <c>UnityEngine.CoreModule.dll</c> carries exactly one
    /// <c>get_mipMapBias</c>/<c>set_mipMapBias</c> pair, the same shape and the same count as
    /// <c>anisoLevel</c>) and <c>RenderTextureDescriptor</c> — the struct <c>Create()</c> builds the
    /// SURFACE from — carries no such member. So the bias is SAMPLER state on the texture object, in
    /// the same class as <c>filterMode</c>/<c>anisoLevel</c>, which this method's own file already
    /// sets before <c>Create()</c> and which the state line has been reading back off the live target
    /// since ModBuild 192. That is an argument, not a measurement, which is exactly why the write is
    /// ALSO made after <c>Create()</c> (where the texture is unambiguously live) and why the value
    /// that reaches the log is the one read off the object. If the two ever disagree the warn below
    /// fires and the report prints both numbers: "asked" and "in force" can never collapse into one
    /// character. Four remedies in this project's history never executed at all; this one has to be
    /// able to say so itself.</para>
    /// </summary>
    private static float ApplyMipLodOffset(RenderTexture? rt, float asked)
    {
        if (rt == null)
            return 0f;
        if (Mathf.Abs(rt.mipMapBias - asked) > MipLodOffsetEpsilon)
            rt.mipMapBias = asked;
        float live = rt.mipMapBias;
        if (!_mipBiasWriteWarned && Mathf.Abs(live - asked) > MipLodOffsetEpsilon)
        {
            _mipBiasWriteWarned = true;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: [WorldUI] PanelMipLodOffset asked for "
                              + $"{asked:F2} mip levels on '{rt.name}' and the render target reads "
                              + $"{live:F2} back — THE WRITE DID NOT STICK. THE CONSEQUENCE: the "
                              + "windows keep exactly ModBuild 202's filtering, i.e. the sharpness "
                              + "this dial exists to buy is NOT in force and any judgement of it "
                              + "from this session is a judgement of the old behaviour. Nothing "
                              + "else changes: capture, MSAA, mips, input and geometry are "
                              + "untouched. The per-window line below prints both numbers every "
                              + "10 s, so this is checkable rather than inferable.");
        }
        return live;
    }

    /// <summary>
    /// Allocate the CAPTURE target — multisampled, with a stencil buffer, and deliberately WITHOUT
    /// mips. The descriptor comes from <c>FlatScreenStereo.CreateColorRt</c> — the shipped factory of
    /// this codebase's other RT path — because it is the one place that forces
    /// <c>D24_UNorm_S8_UInt</c> for a depth request, and a uGUI window without a STENCIL buffer loses
    /// every <see cref="Mask"/> in it (scroll viewports, circular avatars, the whole masked-content
    /// family).
    ///
    /// <para><b>WHY NO MIPS HERE — this is the ModBuild 192 bug.</b> That build asked this very
    /// method for <c>antiAliasing = 4</c> AND <c>useMipMap = true</c> on one target. A render texture
    /// cannot be both: <c>Create()</c> succeeded (so nothing failed loudly, and the stand-down below
    /// never fired) and the mip request was dropped on the floor — every hardware state line read
    /// <c>mips 1 NONE</c> and this class's own falsifier fired twice. <c>GenerateMips()</c> on a
    /// multisampled target is a no-op for the same reason. The mip chain now lives on a second,
    /// single-sample target (<see cref="CreateMipRt"/>) that the capture is RESOLVED into
    /// (<see cref="ResolveAndMip"/>) — which is the standard way to have both, and the same order an
    /// offline renderer uses: multisample, resolve, then band-limit.</para>
    /// </summary>
    private static RenderTexture? CreateRt(int w, int h, ref int msaa, string window)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            RenderTexture rt = FlatScreenStereo.CreateColorRt(w, h, 24, $"GloomhavenVR.PanelSS_{window}");
            rt.antiAliasing = Mathf.Max(1, msaa);
            rt.useMipMap = false;      // see the header: MSAA and mips are mutually exclusive
            rt.autoGenerateMips = false;
            rt.filterMode = FilterMode.Bilinear; // only ever read by the resolve blit
            rt.anisoLevel = 0;
            rt.wrapMode = TextureWrapMode.Clamp;
            if (rt.Create())
            {
                ClearRt(rt);
                return rt;
            }
            Object.Destroy(rt);
            if (msaa <= 1)
                break;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: the driver refused a {w}x{h} render target at MSAA "
                              + $"{msaa}x for '{window}' — retrying without MSAA. THE CONSEQUENCE if "
                              + "the retry also fails: this window keeps today's direct rendering.");
            msaa = 1;
        }
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE stands down on '{window}': a {w}x{h} render target could "
                          + "not be created at any MSAA level. THE CONSEQUENCE: this window keeps "
                          + "today's direct rendering — the same shimmer as before this build. "
                          + "Nothing was changed on the window itself.");
        return null;
    }

    /// <summary>
    /// Allocate the MIPPED DISPLAY target — single-sample, no depth, full mip chain, trilinear +
    /// anisotropic. Single-sample is what makes <c>useMipMap</c> stick at all (see
    /// <see cref="CreateRt"/>); no depth buffer because nothing is ever rendered into it, only
    /// blitted. <c>autoGenerateMips</c> is off on purpose so the chain is generated exactly once per
    /// capture, at a moment this class chooses, rather than at whatever moment Unity would pick.
    /// <para>Returns null (never throws) if the driver refuses it; the caller then displays the
    /// capture target directly, which is ModBuild 192's behaviour, and says so once.</para>
    /// </summary>
    private static RenderTexture? CreateMipRt(int w, int h, string window)
    {
        float asked = AskedMipLodOffset();
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
        {
            name = $"GloomhavenVR.PanelSSMip_{window}",
            antiAliasing = 1,
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Trilinear,
            anisoLevel = AnisoLevel,
            wrapMode = TextureWrapMode.Clamp,
            // ModBuild 203, and written HERE as well as after Create() on purpose — see
            // ApplyMipLodOffset for which order was verified and how. Nothing downstream ever
            // believes this line: the number that reaches the log is read back off the object.
            mipMapBias = asked,
        };
        if (rt.Create() && rt.mipmapCount > 1)
        {
            // The second write, on an unambiguously live texture, and the read-back that decides
            // whether the dial is in force at all. Every re-creation of a display target passes
            // through here (AttachMipTarget is its only caller, from engage and from Reallocate),
            // so a re-allocated window is never left carrying the previous build's filtering.
            ApplyMipLodOffset(rt, asked);
            ClearRt(rt);
            // ModBuild 204: THE UNIVERSAL FLOOR OF THE ATOMIC-REALLOCATION INVARIANT, and the one
            // line that closes the hazard on paths this class does not enumerate. ClearRt clears the
            // BOUND SURFACE, which is mip level 0 and nothing else — autoGenerateMips is off on
            // purpose (see the header) — so before this build levels 1..N of a freshly created
            // display target held whatever that VRAM last contained. Unity does not clear them and
            // D3D11 does not promise they are black. This window samples at a measured LOD 1.66, i.e.
            // it reads levels 1 and 2 almost exclusively: precisely the levels that were undefined,
            // which is why the failure reads as garbage or as nothing rather than as a slightly wrong
            // image. One generation here makes the whole chain a defined downsample of the cleared
            // level 0, and it costs what the next capture's own GenerateMips was going to cost one
            // frame later anyway. See TargetLife for the full argument and the rejected alternatives.
            rt.GenerateMips();
            return rt;
        }
        int got = rt.mipmapCount;
        rt.Release();
        Object.Destroy(rt);
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{window}' could not get a MIPPED display target "
                          + $"({w}x{h}, single-sample, no depth — the driver reported mipmapCount="
                          + $"{got}). THE CONSEQUENCE: the quad falls back to showing the "
                          + "multisampled capture target directly, i.e. exactly ModBuild 192's "
                          + "behaviour — sharp while still, and crawling while the window or the head "
                          + "moves, because the eye minifies an unfiltered texture. Input, geometry "
                          + "and MSAA are unaffected.");
        return null;
    }

    /// <summary>
    /// Clear a freshly created render target to transparent black.
    ///
    /// <para><b>WHY THIS IS NOT COSMETIC.</b> Unity does NOT clear a render target on
    /// <c>Create()</c>; its contents are whatever that VRAM last held. Every allocation on this path
    /// is immediately handed to the display quad — <see cref="Reallocate"/> points
    /// <c>DisplayImage.texture</c> at the new target in the same statement block that destroys the
    /// old one — so any frame in which the capture camera does not render (the panel is hidden for
    /// that frame, the camera is disabled, the resolve is skipped because one of the pair is null)
    /// shows uninitialised memory instead of the window. That is a rare window, but a re-allocation
    /// happens exactly at the moment this round is investigating: the content fit flips a released
    /// window's host rect and the target follows. Two <c>GL.Clear</c>s per allocation close it
    /// outright, and <see cref="Entry.CameraOffWhileVisible"/> measures how often the window they
    /// cover is actually open.</para>
    /// </summary>
    private static void ClearRt(RenderTexture rt)
    {
        RenderTexture? previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    /// <summary>Give <paramref name="e"/> a mipped display target for its current capture size, or
    /// record the fallback. Never throws; the entry is usable either way.</summary>
    private static void AttachMipTarget(Entry e, int w, int h)
    {
        RenderTexture? mip = CreateMipRt(w, h, e.Window);
        if (mip == null)
        {
            e.MipRt = null!;
            e.MipFallback = true;
            e.MipCount = e.Rt != null ? e.Rt.mipmapCount : 1;
            // The pair's estimate already charged for a mip target we did not get; hand it back so
            // the session total stays a true statement about what is allocated.
            e.VramBytes = CaptureVramBytesFor(w, h, e.Msaa);
            return;
        }
        e.MipRt = mip;
        e.MipFallback = false;
        e.MipCount = mip.mipmapCount;
    }

    /// <summary>The texture the display quad shows: the mipped one when we have it, else the
    /// capture target (the ModBuild 192 fallback).</summary>
    private static RenderTexture DisplayTexture(Entry e) => e.MipRt != null ? e.MipRt : e.Rt;

    /// <summary>
    /// The capture camera, parented UNDER the host so its pose is exact by construction — whoever
    /// moves the window (the grab, the order ladder, a board dock, the pose re-place) moves camera
    /// and content together, and the captured image is invariant to all of it. That removes the whole
    /// class of one-frame pose-lag bugs a world-space follower camera would have. It also puts the
    /// camera inside the subtree the layer sweeps walk, which is safe: a transform carrying a
    /// <see cref="Camera"/> is skipped by <c>CanvasConversion.ApplyModLayer</c>'s own rule and by
    /// ours, and it is neither a <see cref="Canvas"/> nor a <see cref="Renderer"/>, so the render-hide
    /// sweep does not see it either.
    /// </summary>
    private static bool BuildCamera(Entry e, ConvertedPanel panel)
    {
        var go = new GameObject($"GloomhavenVR.PanelSSCam_{e.Window}");
        go.transform.SetParent(panel.HostRect, worldPositionStays: false);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        ApplyCaptureClear(cam);
        // THE ISOLATION GUARANTEE IN ONE LINE: this camera's mask is this panel's PRIVATE pool layer
        // and nothing else, so no other supersampled window can be inside what it captures however
        // close it stands or however deep the ortho slab is. ModBuild 193 wrote the ONE shared
        // capture layer here and that is the whole of the "quest window shows the merchant window"
        // defect — see PanelSupersample.5.Isolation.cs.
        cam.cullingMask = e.Layer >= 0 ? 1 << e.Layer : 0;
        cam.depth = -200f;                                // renders before every other camera in the frame
        cam.stereoTargetEye = StereoTargetEyeMask.None;   // mono, once per frame — never part of an eye pass
        cam.renderingPath = RenderingPath.Forward;        // uGUI only; a G-buffer here would be pure cost
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.allowDynamicResolution = false;
        cam.useOcclusionCulling = false;
        cam.targetTexture = e.Rt;
        cam.enabled = true;
        e.CamGo = go;
        e.Cam = cam;
        return true;
    }

    /// <summary>
    /// The display: a mod-owned world-space canvas carrying one <see cref="RawImage"/> of the capture
    /// target, on the MOD layer (so the head camera draws it and the game's UI Camera never can), at
    /// the host rect's exact world pose and size. A SCENE ROOT, not a child of the host, for two
    /// reasons: a nested <see cref="Canvas"/> inside a converted subtree is adopted by
    /// <c>CanvasConversion.AdoptNestedCanvases</c> (which would merge it into the panel's hit-testing
    /// and re-write its sorting), and the capture camera must not be able to see it at all. It rides
    /// the far-to-near draw ladder as an offset-0 order follower, i.e. in its panel's own slot,
    /// exactly where the host canvas would have drawn.
    /// <para><c>raycastTarget</c> is false: this quad is a photograph, never a hit surface. The hit
    /// surface is, and remains, the real host canvas underneath it.</para>
    ///
    /// <para><b>THE ALPHA MATH, stated because it has one known, bounded artifact and a future round
    /// must not have to re-derive it.</b> The capture clears to transparent black and uGUI blends
    /// into it with <c>SrcAlpha, OneMinusSrcAlpha</c>, so a pixel of coverage <i>a</i> and colour
    /// <i>C</i> lands in the render target PREMULTIPLIED, as <c>(aC, a)</c>. The RawImage then
    /// composites it with the standard UI blend, which multiplies by alpha a SECOND time:
    /// <c>a·(aC) + (1-a)·dst = a²C + (1-a)·dst</c>. Fully opaque pixels (<i>a</i>=1) and fully
    /// transparent ones (<i>a</i>=0) are therefore EXACT — which is almost the whole of a floated
    /// window, because the modal family's full-window opaque backing is deliberately disabled
    /// (<c>ConvertedPanel.HideBackground</c>) and what remains is opaque content on nothing. Only
    /// PARTIAL coverage — SDF glyph edges, a CanvasGroup mid-fade — comes out darker than it should,
    /// which reads as very slightly thinner text and a slightly darker window during the ~0.3 s show
    /// animation. Correcting it needs <c>Blend One OneMinusSrcAlpha</c>, which no built-in UI shader
    /// offers and which this lane refuses to gamble a bundled/legacy <c>Shader.Find</c> on after nine
    /// failed rounds ("Shader.Find only sees loaded shaders"). It is also the exact composite
    /// <see cref="FlatScreen"/>'s split mode already ships and the user has already accepted.</para>
    /// </summary>
    /// <summary>
    /// <b>THE ALPHA IS APPLIED TWICE, AND THAT IS THE DEFECT — ModBuild 212.</b>
    ///
    /// <para>The arithmetic was written down in <see cref="BuildDisplay"/>'s own doc from the first
    /// build of this class and then treated as a bounded cosmetic footnote for nine rounds. It is not
    /// cosmetic. The capture clears to TRANSPARENT black and uGUI blends into it with
    /// <c>SrcAlpha, OneMinusSrcAlpha</c>, so a pixel of coverage <i>a</i> and colour <i>C</i> lands in
    /// the target PREMULTIPLIED as <c>(aC, a)</c>. The RawImage then composites that with the standard
    /// UI blend, which multiplies by alpha a SECOND time: <c>a·(aC) + (1-a)·dst = a²C + (1-a)·dst</c>.
    /// </para>
    ///
    /// <para><b>WHY IT READS AS MISSING LETTERS RATHER THAN AS SLIGHTLY THIN ONES.</b> The old note
    /// argued the error is bounded because a floated window is "almost entirely opaque content on
    /// nothing". That is true of the PLATE and false of everything the user is complaining about. An
    /// SDF glyph stem two texels wide is nearly ALL partial coverage; so is an icon's edge, so is the
    /// antialiased silhouette of the 3D character render. At a = 0.5 the pixel comes out at 0.25 — a
    /// quarter of its intended brightness. And the share of a glyph that is partial coverage RISES as
    /// the window is minified, because the mip chain averages the stroke with the page: carry the
    /// window further away and more of every letter falls into the squared regime at once. That is the
    /// user's video frame for frame — the window complete at 4 s, letters eaten at 11 s, the whole
    /// picture nearly extinguished at 18 s, the plate still there because the plate is a = 1.</para>
    ///
    /// <para><b>WHY EVERY INSTRUMENT SAID THE CAPTURE WAS CORRECT.</b> Because it IS correct. The
    /// second multiply happens in the RawImage composite, AFTER the render target the ink census
    /// reads. Fourteen builds of measurement were taken one stage upstream of the damage — see the
    /// memory "one step too early".</para>
    ///
    /// <para><b>THE FIX THAT WAS TRIED AND FAILED — ModBuild 212, REVERTED IN 213. DO NOT SHIP IT
    /// AGAIN.</b> Clearing the capture to OPAQUE black makes uGUI's own blend resolve the coverage
    /// against the plate: the target holds <c>(aC, 1)</c>, the composite multiplies by 1, the result
    /// is <c>aC</c>, exactly linear. The arithmetic is correct and the change did exactly what it
    /// says. <b>The symptom was unchanged</b> — the user: <i>"Das hat das Problem NICHT behoben"</i> —
    /// and the look was rejected outright: <i>"sieht auch jetzt deutlich hässlicher aus, ich mag es
    /// transparent mehr"</i>. So the double multiply is REAL and is NOT the cause of the vanishing
    /// elements; it is a genuine but second-order brightness error on partial coverage, and this
    /// window's defect survives its removal. That is a falsification, not a tuning result: any future
    /// round that rediscovers the <c>a²</c> arithmetic has already been here.</para>
    /// </summary>
    private static void ApplyCaptureClear(Camera cam)
    {
        // TRANSPARENT, and it stays transparent. See the doc above: ModBuild 212 shipped the opaque
        // clear as a fix, it did NOT fix the symptom, and the user rejected the look outright —
        // "sieht auch jetzt deutlich hässlicher aus, ich mag es transparent mehr". The dial is gone
        // rather than defaulted off: a setting that makes windows uglier and fixes nothing is not an
        // optional-content setting, and this project's standing rule is that settings configure
        // optional content and comfort only.
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
    }

    private static bool BuildDisplay(Entry e, ConvertedPanel panel)
    {
        var go = new GameObject($"GloomhavenVR.PanelSS_{e.Window}");
        var rect = go.AddComponent<RectTransform>();
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = CanvasConversion.WorldCamera;
        canvas.sortingOrder = panel.DrawSortingOrder;
        var image = go.AddComponent<RawImage>();
        image.texture = DisplayTexture(e);
        image.raycastTarget = false;
        // ModBuild 204: one of exactly TWO places the quad's texture is written (the other is
        // Reallocate), and the invariant is stated about this identity. The uvRect is left at the
        // default (0,0,1,1) and is never touched by anything in this class, so the quad's UV mapping
        // is the identity for every target it will ever be pointed at — a target size change can
        // therefore never disagree with the quad's UVs, only with its texel density.
        BindDisplayTarget(e, DisplayTexture(e));
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        VRLayers.Apply(go);
        e.DisplayGo = go;
        e.DisplayRect = rect;
        e.DisplayCanvas = canvas;
        e.DisplayImage = image;
        CanvasConversion.RegisterOrderFollower(panel, canvas, 0);
        return true;
    }

    // ---- per-frame sync -----------------------------------------------------------------------

    /// <summary>
    /// Keep the capture frustum, the display quad and the allocation on the panel's live CAPTURE
    /// FRAME (<see cref="Entry.Frame"/> — the host rect unioned with everything the window actually
    /// draws; see <see cref="MeasureFrame"/> for why it is not simply the host rect). The camera is
    /// a CHILD of the host, so only its projection needs writing (world-unit ortho size and aspect,
    /// recomputed from the live frame and lossy scale — the diorama scale is ~198 world units per
    /// real metre in the map room, and every quantity here is in WORLD units, never metres). The
    /// display quad is a scene root, so its full pose is copied; the final copy happens in
    /// <see cref="OnPreCull"/>, after every LateUpdate pose writer has run.
    ///
    /// <para>ORDERING, stated because ModBuild 192's residual defect was a movement one and the next
    /// round must not have to re-derive this. Within a frame: every Update runs (including
    /// <c>GrabbableModal.Tick</c>, which copies the grab frame onto the host), then every LateUpdate
    /// runs (including <c>GrabbableModal.LateSyncHost</c>, an ordinary MonoBehaviour LateUpdate with
    /// no defined order against this one, and this method via <c>CanvasConversion.LateTick</c>), and
    /// only THEN does the camera loop start. So a host pose written by ANY LateUpdate — before or
    /// after this method — is already final when the capture camera renders, because that camera is
    /// a CHILD of the host and reads the host's world matrix at render time, and it is already final
    /// when <see cref="OnPreCull"/> copies the quad's pose, because that runs inside the camera loop
    /// too. Capture content and quad pose therefore cannot disagree by a frame, whichever LateUpdate
    /// won. What this method WOULD be one frame late on is the PROJECTION: the capture camera's
    /// <c>orthographicSize</c> is a WORLD-unit quantity derived from the host's lossy scale, so a
    /// two-hand resize whose <c>LateSyncHost</c> lands after this method would leave the camera
    /// framing the previous frame's world size while rendering at the new one — the captured image
    /// would zoom by the scale ratio for that frame, inside a display quad that (reading the scale
    /// live in <see cref="OnPreCull"/>) is already correct. That is precisely a "manche Elemente
    /// nicht richtig dargestellt" artifact, and it is why <see cref="SyncProjection"/> is called
    /// AGAIN from the capture camera's own <see cref="Camera.onPreCull"/> — the last instant before
    /// it culls, after every LateUpdate in the frame, whoever won. The ALLOCATION can still trail a
    /// resize by a frame, which costs resolution and never geometry.</para>
    /// </summary>
    private static void SyncGeometry(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        RectTransform host = panel.HostRect;

        // THE RESIZE / REPOSE TRIGGER (ModBuild 193). Read entirely from the host transform and the
        // host RectTransform, because that is where every writer lands: the content fit writes
        // HostRect.sizeDelta (CanvasConversion.3.Fit.cs, which computes its own `resized` flag right
        // there), GrabbableModal.SyncHostToFrame writes the host's position, rotation and localScale
        // every frame from the grab frame, and the release re-face
        // (GrabbableModal.IPanelGrabOwner.OnGrabFinished) writes the rotation once. Watching the
        // resulting VALUES catches all of them without coupling to any of those files — the same
        // argument CanvasConversion's own reveal gate makes for value-based stillness.
        bool dirty = NoticeGeometry(e, host);

        // Re-measure the capture frame on the cadence, and IMMEDIATELY on any geometry change: a
        // window that was re-fitted or re-scaled on release must not keep framing last size's
        // content. This is the "a resize must force a full re-capture" requirement — the capture
        // itself runs every frame, so forcing it means forcing everything the capture is derived
        // from: the frame, the projection, the allocation and the layer sweep.
        bool forced = dirty
                      && Time.frameCount - e.LastMeasureFrame >= ContentMeasureMinIntervalFrames;
        if (forced || Time.frameCount >= e.NextContentFrame)
        {
            e.NextContentFrame = Time.frameCount + ContentMeasureIntervalFrames;
            MeasureFrame(e);
        }

        SyncProjection(e);
        e.DisplayRect.sizeDelta = e.Frame.size;
        SyncDisplayPose(e);
        Rect frame = e.Frame;

        // Re-allocate when the frame materially resized: an RT built for the pre-fit frame would
        // either waste texels or stretch across the new one, and the whole point of this path is
        // that one RT texel corresponds to one authored pixel times the factor. A geometry change
        // forces the check regardless of the fraction, so a release-time re-fit can never leave a
        // stale target behind.
        // ModBuild 201: A CHANGE OF CONTENT SCALE IS A REALLOCATION TRIGGER IN ITS OWN RIGHT. The
        // character window swaps between six sub-views inside ONE host rect of a fixed 1143x1080; two
        // of those six are written to a scale near 0.49 and four to 1.000. Nothing about the HOST
        // changes when the user presses a different tab, so neither `dirty` (which watches the host
        // transform) nor the frame test above can see it — and yet the rate the render target must be
        // sized for changes by a factor of two. Without this line the boost would be applied only if
        // the sub-view that happened to be open at the last reallocation was the scaled one.
        bool scaleChanged = Mathf.Abs(e.MinContentScale - e.ScaleAtAllocation)
                            > 0.02f * Mathf.Max(e.ScaleAtAllocation, 0.01f);
        bool frameChanged = Mathf.Abs(frame.width - e.Authored.x) > e.Authored.x * RectChangeFraction
                            || Mathf.Abs(frame.height - e.Authored.y) > e.Authored.y * RectChangeFraction;
        if (dirty || scaleChanged || frameChanged)
        {
            // ModBuild 204: ATTRIBUTE THE RE-ALLOCATION. The three conditions can co-occur and the
            // tag records the one that is most specific about what actually moved — a host rescale
            // implies a frame change, and a sub-view swap implies neither. The 203 log could say only
            // "58 re-allocations since engage", which is why it took a whole session to see that 45 of
            // them were inside grabs and that they alternated 28/28 between two frames one 32 px
            // quantum apart. See ReallocTrigger and AppendReallocations.
            Reallocate(e, frame, dirty ? ReallocTrigger.HostGeometry
                                       : scaleChanged ? ReallocTrigger.SubViewScale
                                                      : ReallocTrigger.ContentFrame);
        }

        // Kept current from the LIVE frame and the LIVE target, not only at allocation time: a frame
        // that grew without triggering a reallocation (below RectChangeFraction, or refused by the
        // budget) lowers the texels-per-authored-pixel the window actually has, and that is precisely
        // the quantity the ModBuild 198 fix is about. Two divisions.
        RecordAchievedFactor(e);
    }

    /// <summary>
    /// Record what <see cref="Entry.Factor"/> the render target ACTUALLY delivers for the current
    /// capture frame — the smaller of the two per-axis ratios, because the eye sees the worse one.
    /// <para>It is below the asked factor exactly when <see cref="MaxRtDimension"/> clipped an axis or
    /// a reallocation was refused, and in both cases the ModBuild 198 argument is only partly in
    /// force on this window. Making that a printed number rather than an inference is the whole
    /// reason this exists: a build that "shipped factor 2" and a window that is running at 1.3
    /// because its frame is 3100 px wide must not read the same in the log.</para>
    /// </summary>
    private static void RecordAchievedFactor(Entry e)
    {
        float w = e.Frame.width > 1f ? e.RtW / e.Frame.width : e.EffectiveFactor;
        float h = e.Frame.height > 1f ? e.RtH / e.Frame.height : e.EffectiveFactor;
        e.AchievedFactor = Mathf.Min(w, h);
    }

    /// <summary>
    /// Write the capture camera's projection from the entry's frame and the host's LIVE lossy scale.
    /// Called twice a frame on purpose — once from <see cref="SyncGeometry"/> (so a camera that
    /// somehow never gets a pre-cull callback is still correct) and once from the capture camera's
    /// own <see cref="Camera.onPreCull"/>, which is the last instant before it culls and therefore
    /// the only place a late LateUpdate scale write cannot beat. See <see cref="SyncGeometry"/>'s
    /// ORDERING paragraph for the artifact this second call removes. Both writes are idempotent.
    /// <para>Everything here is in WORLD units (the map room runs ~198 world units per real metre);
    /// <c>Frame</c> is in host-local uGUI pixels and the host's lossy scale is the bridge.</para>
    /// </summary>
    private static void SyncProjection(Entry e)
    {
        RectTransform? host = e.Panel.HostRect;
        if (host == null || e.Cam == null || e.CamGo == null)
            return;
        // Re-asserted every frame so the dial can be compared LIVE from the VR menu without a restart:
        // this is the one setting the user has to be able to A-B against his own eyes, because the
        // whole question is what the window looks like. Two float writes.
        ApplyCaptureClear(e.Cam);
        Rect frame = e.Frame;
        float scale = Mathf.Max(Mathf.Abs(host.lossyScale.y), 1e-6f);
        float frameHeightWorld = frame.height * scale;
        float slab = Mathf.Max(frameHeightWorld * 0.5f, 1e-4f);
        float standoff = slab * 2f;

        // ORTHOGRAPHIC, and it stays orthographic. A perspective capture was drafted in ModBuild
        // 213 on the theory that TMP's SDF sharpness is computed from the RENDERING camera's
        // projection while its CPU half is computed from the CANVAS's (perspective) camera, so the two
        // disagree by construction. The theory predicts that THIN strokes die first and thick ones
        // survive — and the user's answer killed it before it shipped: "Wie du im Video siehst stirbt
        // auch 'HILDE DIE 2TE' ... alles innerhalb des Sub-Menus ist betroffen". The heading dies too,
        // and so does the 3D character render, which is not SDF at all. A per-glyph threshold cannot
        // remove a whole sub-tree, so the projection is not the lever. Not shipped, recorded here so
        // the next round does not rediscover it.
        e.Cam.orthographic = true;
        e.Cam.orthographicSize = frameHeightWorld * 0.5f;
        e.Cam.aspect = Mathf.Max(frame.width / Mathf.Max(frame.height, 1e-4f), 1e-4f);
        e.Cam.nearClipPlane = standoff - slab;
        e.Cam.farClipPlane = standoff + slab;
        Vector2 centre = frame.center;
        e.CamGo.transform.localPosition = new Vector3(centre.x, centre.y, -standoff / scale);
        e.CamGo.transform.localRotation = Quaternion.identity;
        e.CamGo.transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Did the host's pose, scale or rect change since the last frame? Records the motion stamp that
    /// drives the per-frame layer sweep (<see cref="SweepAfterMotionFrames"/>) and returns true for
    /// the changes that invalidate the capture frame (rect or scale), NOT for a pure translation —
    /// moving a window changes nothing about what it draws or how big its render target must be.
    /// <para>The position epsilon is expressed in WORLD units and derived from the panel's own
    /// lossy scale (half an authored pixel), never in metres: at the map room's ~198 world units per
    /// metre a fixed metric epsilon would be either blind or permanently tripped.</para>
    ///
    /// <para><b>THIS INSTRUMENT IS NOT BLIND, AND ModBuild 194 MAKES THE LOG SAY SO ITSELF.</b> It
    /// was suspected of never firing, on the reading that the ModBuild 193 log's MOTION field was
    /// zero everywhere. It is not: 23 of that log's 226 state lines carry a non-zero count, the
    /// largest being 542 frames of a 900-frame report window, and several read
    /// <c>currently MOVING</c>. The reason a reader could believe otherwise is that a bare zero
    /// carries no evidence about WHY it is zero — a still window, a detector that never ran and a
    /// detector whose epsilon swallowed the drag all print the same character. So this method now
    /// also records how many comparisons it made (<see cref="Entry.MotionTicks"/>), the largest
    /// single-frame world-space step it saw (<see cref="Entry.MaxStepWorld"/>) and the epsilon that
    /// step was tested against (<see cref="Entry.MotionEpsWorld"/>), and the report prints all three
    /// in world units AND in authored uGUI pixels. A blind detector is then a large step with a zero
    /// count; a still window is a step below the epsilon with a large comparison count; a detector
    /// that never ran is a zero comparison count. The three are no longer confusable.</para>
    /// </summary>
    private static bool NoticeGeometry(Entry e, RectTransform host)
    {
        Transform t = host.transform;
        Vector3 pos = t.position;
        Quaternion rot = t.rotation;
        Vector3 scl = host.lossyScale;
        Vector2 size = host.rect.size;

        if (!e.HasPoseSnapshot)
        {
            e.HasPoseSnapshot = true;
            e.LastPos = pos;
            e.LastRot = rot;
            e.LastScale = scl;
            e.LastRectSize = size;
            return false;
        }

        float posEps = Mathf.Max(Mathf.Abs(scl.y) * 0.5f, 1e-6f);
        // The self-check, recorded BEFORE the snapshot is overwritten and regardless of the verdict.
        e.MotionTicks++;
        e.MotionEpsWorld = posEps;
        e.WorldPerAuthoredPx = Mathf.Abs(scl.y);
        float step = (pos - e.LastPos).magnitude;
        if (step > e.MaxStepWorld)
            e.MaxStepWorld = step;
        bool moved = (pos - e.LastPos).sqrMagnitude > posEps * posEps;
        bool turned = Quaternion.Angle(rot, e.LastRot) > 0.05f;
        bool rescaled = (scl - e.LastScale).magnitude > e.LastScale.magnitude * 1e-4f + 1e-9f;
        bool resized = Mathf.Abs(size.x - e.LastRectSize.x) > 0.5f
                       || Mathf.Abs(size.y - e.LastRectSize.y) > 0.5f;

        e.LastPos = pos;
        e.LastRot = rot;
        e.LastScale = scl;
        e.LastRectSize = size;

        if (moved || turned || rescaled || resized)
        {
            // THE DRAG BOUNDARY (ModBuild 197). A change arriving after the motion window has fully
            // lapsed is a NEW drag, so the per-drag accumulators start over here. Everything the
            // release edge prints — how long the drag was, how fast it was going when the hand let
            // go, how many of its frames were dropped — is accumulated between this reset and the
            // settle gate in ServiceRepairs.
            if (!IsMoving(e))
            {
                e.DragStartFrame = Time.frameCount;
                e.DragMotionFrames = 0;
                e.DragMaxStepWorld = 0f;
                e.DragDroppedFrames = 0;
                e.DragWorstFrameMs = 0f;
                // ModBuild 204 (item B): the hand census restarts with the drag, so the RELEASE line's
                // handsOn= field describes THIS drag and not the session.
                TargetLife life = LifeOf(e);
                life.DragHandOnFrames = 0;
                life.DragHandSamples = 0;
            }
            e.LastMotionFrame = Time.frameCount;
            e.MotionFrames++;
            e.DragMotionFrames++;
            // THE ABRUPT NUMBER: the step on the LAST frame that moved. A hand that stops before it
            // lets go leaves ~0 here; a hand still travelling leaves the drag's full speed, and the
            // release edge prints both this and the drag's largest step so the two are comparable.
            e.DragLastStepWorld = step;
            if (step > e.DragMaxStepWorld)
                e.DragMaxStepWorld = step;
            e.CurrentMotionRun++;
            if (e.CurrentMotionRun > e.LongestMotionRun)
                e.LongestMotionRun = e.CurrentMotionRun;
        }
        else
        {
            e.CurrentMotionRun = 0;
        }
        if (!rescaled && !resized)
            return false;
        e.GeometryDirtyEvents++;
        return true;
    }

    /// <summary>Is this window inside its motion window (moving, or settling from a move)? Drives
    /// the per-frame capture-layer sweep — see <see cref="SweepAfterMotionFrames"/>.</summary>
    private static bool IsMoving(Entry e) => Time.frameCount - e.LastMotionFrame <= SweepAfterMotionFrames;

    private static void SyncDisplayPose(Entry e)
    {
        RectTransform host = e.Panel.HostRect;
        if (host == null || e.DisplayGo == null)
            return;
        Transform t = e.DisplayGo.transform;
        t.SetPositionAndRotation(host.TransformPoint(e.Frame.center), host.rotation);
        t.localScale = host.lossyScale;
    }

    /// <summary>
    /// <b>THE CAPTURE RATE, IN TEXELS PER AUTHORED PIXEL — and from ModBuild 201 it answers to the
    /// CONTENT'S scale, not to the host's.</b>
    ///
    /// <para>The whole argument is on <see cref="Entry.MinContentScale"/>. In one line: the render
    /// target is sized from the host frame, so a subtree the content fit drew at 0.487 received
    /// <c>2.00 x 0.487 = 0.97</c> texels per authored pixel while its unscaled siblings in the same
    /// window received 2.00 — the band-limit floor was in force for four of the character window's
    /// six sub-views and silently absent for the two the user reports as broken. Asking for
    /// <c>BandLimitFactor / MinContentScale</c> puts the floor back where it belongs.</para>
    ///
    /// <para><b>AND IT SAYS WHAT IT DID NOT GET.</b> The ask is cut down, in order, by
    /// <see cref="MaxRtDimension"/>, by the VRAM budget and by <see cref="RateQuantum"/> — on the
    /// character window an ask of 4.11 comes back as roughly 2.4. Both numbers are recorded
    /// (<see cref="Entry.AskedFactor"/>, <see cref="Entry.EffectiveFactor"/>) and both are printed, so
    /// a boost that was refused can never be read as one that was granted. That matters here more
    /// than usual: the reachable rate does NOT restore the band limit for a 0.487 subtree, and the
    /// report must not imply that it does.</para>
    /// </summary>
    private static void ResolveRate(Entry e, Rect frame, ref int msaa, long budget,
                                    out float rate, out int rtW, out int rtH, out long vram)
    {
        float scale = Mathf.Clamp(e.MinContentScale, MinContentScaleFloor, 1f);
        float asked = e.Factor / scale;
        e.AskedFactor = asked;

        // THE DIMENSION CEILING, applied to the RATE rather than to the pixel counts, so that what
        // comes out is still a quantised rate and the texel grid stays locked to the authored grid.
        float ceiling = Mathf.Min(MaxRtDimension / Mathf.Max(frame.width, 1f),
                                  MaxRtDimension / Mathf.Max(frame.height, 1f));
        rate = Mathf.Min(asked, ceiling);
        rate = Mathf.Floor(rate / RateQuantum) * RateQuantum;
        rate = Mathf.Max(rate, RateQuantum);

        rtW = Mathf.Clamp(Mathf.RoundToInt(frame.width * rate), 16, MaxRtDimension);
        rtH = Mathf.Clamp(Mathf.RoundToInt(frame.height * rate), 16, MaxRtDimension);
        vram = VramBytesFor(rtW, rtH, msaa);
        while (msaa > 1 && vram > budget)
        {
            msaa /= 2;
            vram = VramBytesFor(rtW, rtH, msaa);
        }
        // THE STEP-DOWN. Floors at MinStepDownFactor, which is the pre-ModBuild-201 behaviour: a
        // window that cannot afford the boost still gets everything it had before, never less.
        while (rate > MinStepDownFactor + 1e-3f && vram > budget)
        {
            rate = Mathf.Max(MinStepDownFactor, rate - RateQuantum);
            rtW = Mathf.Clamp(Mathf.RoundToInt(frame.width * rate), 16, MaxRtDimension);
            rtH = Mathf.Clamp(Mathf.RoundToInt(frame.height * rate), 16, MaxRtDimension);
            vram = VramBytesFor(rtW, rtH, msaa);
        }
    }

    /// <summary>
    /// Re-size this window's target pair for <paramref name="frame"/>, atomically. See
    /// <see cref="TargetLife"/> for the ModBuild 204 invariant, the VRAM arithmetic that decided the
    /// shape, and the rejected alternatives.
    /// <para><paramref name="trigger"/> is recorded whether or not anything is allocated, because the
    /// question the 203 log could not answer is "what ASKED for these 58" and a call that early-outs
    /// is as much a part of that answer as one that allocates.</para>
    /// </summary>
    private static void Reallocate(Entry e, Rect frame, ReallocTrigger trigger)
    {
        TargetLife life = LifeOf(e);
        bool moving = IsMoving(e);
        int msaa = e.Msaa;
        long budget = System.Math.Min(MaxPanelVramBytes,
            MaxTotalVramBytes - (_vramTotal - e.VramBytes));
        ResolveRate(e, frame, ref msaa, budget, out float rate, out int rtW, out int rtH,
                    out long vram);
        if (rtW == e.RtW && rtH == e.RtH)
        {
            // THE EARLY-OUT, AND WHAT ITEM 5 ASKED THIS METHOD TO CONFIRM ABOUT ReleaseRepair. When
            // the frame has not moved a target-sized pixel, this branch runs and NOTHING is
            // allocated, destroyed, blitted or re-pointed — the whole cost of the release repair's
            // unconditional call is ResolveRate's float arithmetic and its two bounded step-down
            // loops, which is far below a microsecond. So the 203 log's 16.5 ms release cost is NOT
            // this call: it is MeasureFrame + ApplyCaptureLayer + MeasureContent(repairAll) in
            // ReleaseRepair, and the CanvasConversion.Late spike that follows is downstream of the
            // text regeneration, which is a separate deliberate repair and stays. Counted, so the log
            // can say that rather than leaving the next round to re-derive it.
            life.NoOpByTrigger[(int)trigger]++;
            e.Authored = frame.size;
            e.EffectiveFactor = rate;
            e.ScaleAtAllocation = e.MinContentScale;
            return;
        }
        if (vram > budget)
        {
            // Keep the existing target; the image merely resamples a little. Recording the new size
            // as `Authored` is what stops this from being re-tried (and re-logged) every frame.
            life.Refusals++;
            e.Authored = frame.size;
            e.ScaleAtAllocation = e.MinContentScale;
            return;
        }

        float createStart = Time.realtimeSinceStartup;
        RenderTexture? rt = CreateRt(rtW, rtH, ref msaa, e.Window);
        if (rt == null)
        {
            life.Refusals++;
            life.CreateMs += (Time.realtimeSinceStartup - createStart) * 1000.0;
            e.Authored = frame.size; // as above: do not retry a refused allocation per frame
            e.ScaleAtAllocation = e.MinContentScale;
            return;
        }
        long previous = e.VramBytes;
        RenderTexture old = e.Rt;
        RenderTexture? oldMip = e.MipRt;
        // The texture the quad is showing RIGHT NOW, captured before it is replaced: the carry-over
        // fallback in PrimeNewTarget needs it, and it is still alive because the old pair is not
        // released until the bottom of this method.
        RenderTexture previousShown = DisplayTexture(e);
        e.Cam.targetTexture = rt;
        e.Rt = rt;
        e.Msaa = msaa;
        e.VramBytes = vram;                 // AttachMipTarget trims this if the mip target is refused
        AttachMipTarget(e, rtW, rtH);
        life.CreateMs += (Time.realtimeSinceStartup - createStart) * 1000.0;
        e.DisplayImage.texture = DisplayTexture(e);
        BindDisplayTarget(e, DisplayTexture(e));
        _vramTotal += e.VramBytes - previous;
        if (_vramTotal < 0)
            _vramTotal = 0;
        e.RtW = rtW;
        e.RtH = rtH;
        e.Authored = frame.size;
        e.EffectiveFactor = rate;
        e.ScaleAtAllocation = e.MinContentScale;
        RecordAchievedFactor(e);
        e.Reallocations++;
        life.ByTrigger[(int)trigger]++;
        if (moving)
            life.ByTriggerMoving[(int)trigger]++;

        // ---- THE ATOMICITY STEP (ModBuild 204) -------------------------------------------------
        // Fill the new pair NOW, in the LateUpdate that swapped it in, before the frame's camera loop
        // has started and therefore before any eye pass can sample it. Placed AFTER the quad is
        // re-pointed (so the prime fills the texture the quad is actually showing) and BEFORE the old
        // pair is released (so the carry-over fallback still has a source). Everything downstream —
        // the expected-zero counter, the FILL LATENCY distribution — measures whether this worked
        // rather than asserting that it did.
        PrimeNewTarget(e, previousShown);

        float destroyStart = Time.realtimeSinceStartup;
        if (old != null)
        {
            old.Release();
            Object.Destroy(old);
        }
        if (oldMip != null)
        {
            oldMip.Release();
            Object.Destroy(oldMip);
        }
        life.DestroyMs += (Time.realtimeSinceStartup - destroyStart) * 1000.0;
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE re-allocated '{e.Window}' to {rtW}x{rtH} (MSAA {msaa}x, "
                          + $"mips {e.MipCount}, {Mb(e.VramBytes)} MB) after its capture frame changed "
                          + $"to {frame.width:F0}x{frame.height:F0} uGUI px (host rect "
                          + $"{e.HostRectAtMeasure.width:F0}x{e.HostRectAtMeasure.height:F0} + content "
                          + $"overspill {e.ExpandX:F0}x{e.ExpandY:F0}). A reallocation is REQUIRED for "
                          + "correctness, not just for sharpness: the target must match the frame the "
                          + "camera projects into it, or the window resamples through the wrong "
                          + "number of texels for as long as the mismatch lasts. RATE: asked "
                          + $"{e.AskedFactor:F2} texels per authored px (base {e.Factor:F2} raised by "
                          + $"the smallest content scale in this frame, {e.MinContentScale:F3} — see "
                          + "Entry.MinContentScale), GOT " + rate.ToString("F2")
                          + ", so the smallest-scaled subtree in this window is captured at "
                          + (rate * e.MinContentScale).ToString("F2")
                          + " texels per ITS OWN authored px against a band limit of "
                          + $"{BandLimitedTexelsPerPixel:F2}."
                          // ModBuild 204: WHO ASKED, AND WAS THE WINDOW BEING CARRIED. The 203 log
                          // carried neither and could therefore only report "58 since engage".
                          + $" TRIGGER: {ReallocTriggerNames[(int)trigger]}, window "
                          + (moving ? "MOVING" : "still")
                          + $" (re-allocation #{e.Reallocations} on this window). ATOMICITY: the new "
                          + "pair was "
                          + (life.Primed > 0 && life.PrimeFailed == 0
                              ? "captured, resolved and mipped SYNCHRONOUSLY before this line was "
                                + "printed, so the quad has never shown an unfilled target"
                              : $"primed {life.Primed} time(s) and fell back to carrying the previous "
                                + $"image over {life.PrimeCarriedOver} time(s) ({life.PrimeFailed} "
                                + "prime(s) unavailable)")
                          + $"; {life.PrimeMs:F2} ms of priming, {life.CreateMs:F2} ms of creation "
                          + $"and {life.DestroyMs:F2} ms of destruction on this window since engage.");
    }

    /// <summary>
    /// The capture camera and the display follow the panel's own visibility exactly. A panel the
    /// render hide has switched off must not pay for a capture, and its quad must not keep showing
    /// the last frame it had.
    /// </summary>
    private static void SyncVisibility(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        bool visible = panel.HostCanvas != null && panel.HostCanvas.enabled
                       && !panel.RenderHidden && !panel.OwnerRenderHidden
                       && panel.HostGo != null && panel.HostGo.activeInHierarchy;
        bool due = Time.frameCount >= e.NextCaptureFrame;
        if (visible && due)
            e.NextCaptureFrame = Time.frameCount + CaptureIntervalFrames;
        if (e.Cam != null && e.Cam.enabled != (visible && due))
            e.Cam.enabled = visible && due;
        if (e.DisplayGo != null && e.DisplayGo.activeSelf != visible)
            e.DisplayGo.SetActive(visible);
        // THE PATH COUNTER (ModBuild 197). A frame in which the quad IS shown and no capture is
        // taken is a frame in which the eye draws the previous capture — the honest answer to "is a
        // dragged window supersampled at all, or does something short-circuit". Expected 0 while
        // CaptureIntervalFrames is 1; printed with its denominator so a 0 is evidence.
        if (visible && !due)
            e.CameraOffWhileVisible++;
        if (visible && e.MipFallback)
            e.RawQuadFrames++;
    }

    // ---- the layer sweep ----------------------------------------------------------------------

    /// <summary>
    /// Move the panel's subtree onto the capture layer, WALKED not flattened, with the same rule
    /// <c>CanvasConversion.ApplyModLayer</c> uses: a subtree whose root carries a real
    /// <see cref="Renderer"/> or a <see cref="Camera"/> is skipped WHOLE — and so are its children,
    /// which is the entire point. uGUI draws through <c>CanvasRenderer</c>, which is not a
    /// <see cref="Renderer"/>, so a real Renderer inside a converted window is by definition NOT part
    /// of the UI: in the party/character windows it is the live 3D character rig, which the game
    /// renders with its own preview camera into a RenderTexture the window then shows as a
    /// <see cref="RawImage"/>. That camera culls BY LAYER, and moving the rig has already been
    /// shipped and reverted once ("relayering drew the character twice").
    /// <para>Our own capture camera IS inside this subtree (it is parented to the host so its pose is
    /// exact by construction) and is skipped by reference as well as by the Camera clause above. The
    /// display quad is a scene root and never appears in this walk at all — which is one of the two
    /// reasons it is not parented to the host; the other is that a nested <see cref="Canvas"/> inside
    /// a converted subtree gets adopted into the panel's hit-testing.</para>
    /// <para>Cost: the O(n) <see cref="IsRecorded"/> scan runs ONLY for a transform that is not yet on
    /// the capture layer, so a settled window costs one component walk and zero scans — the same
    /// shape, and the same reason, as <c>ApplyModLayer</c>'s own guard.</para>
    /// </summary>
    private static void ApplyCaptureLayer(Entry e, bool initial)
    {
        int layer = e.Layer;
        if (layer < 0 || e.Panel.HostGo == null)
            return;
        float sweepStart = Time.realtimeSinceStartup;
        Scratch.Clear();
        Scratch.Add(e.Panel.HostGo.transform);
        int moved = 0;
        int skipped = 0;
        int foreignLayer = 0;
        int nested = 0;
        int nestedOnLayer = 0;
        while (Scratch.Count > 0)
        {
            int last = Scratch.Count - 1;
            Transform t = Scratch[last];
            Scratch.RemoveAt(last);
            if (t == null)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;
            bool isRoot = ReferenceEquals(t, e.Panel.HostGo.transform);
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
            {
                skipped++;
                continue; // and NOT its children either
            }
            // NEVER STEAL ANOTHER PANEL'S PRIVATE LAYER. The converted panels' subtrees are disjoint
            // today (each gets its own scene-root host GameObject), so this cannot fire; it is here
            // so the isolation guarantee does not rest on an invariant in a file this lane does not
            // own. If two entries ever did share a transform, both would rewrite its layer every
            // frame and the value would ALTERNATE — this project's "don't win a write war" failure,
            // and under MultiPass the two eyes would disagree about it. One mask test per transform
            // buys the guarantee outright. See IsForeignPoolLayer.
            // ONE PANEL'S FINGERPRINT ON A POOLED OBJECT IS NOT THE OTHER PANEL'S CLAIM
            // (ModBuild 195). The comment above assumed the converted subtrees are disjoint, so this
            // guard "cannot fire". IT FIRED — 18 of 30 report lines on 'Character Items Equipment
            // Content' in the ModBuild 194 log, and the `continue` skips the transform AND ITS WHOLE
            // SUBTREE, so the equipment tooltip's card art was never captured while its frame was:
            // a visible-but-empty box, which is the user's "so transparent, dass man sie kaum
            // erkennen kann". The cause is the game's SHARED CARD POOL: a card the shop tooltip used
            // arrives in the equipment window still carrying the shop panel's private layer. It is
            // OUR descendant now — the sweep only ever walks our own host — so the layer VALUE is a
            // stale fingerprint, not a competing owner.
            // WE TAKE IT, AND WE TAKE THE RECORD WITH IT. Claiming without transferring would have
            // been worse than the skip: our own restore would later write the SHOP's capture layer
            // onto it (that is what it would have found there), stranding the card on a layer no
            // camera renders. TryTakeForeignRecord moves the original game layer across, so exactly
            // one entry owns the object and the restore still hands back what the game gave.
            int observed = t.gameObject.layer;
            if (!isRoot && IsForeignPoolLayer(e, observed))
            {
                foreignLayer++;
                if (TryTakeForeignRecord(e, t, out int gameLayer))
                    observed = gameLayer;
                else
                    // No record to transfer: the other entry never wrote this object (it was pooled
                    // out and back before its sweep ran). We cannot know the game layer, so refuse
                    // rather than guess — a wrong restore is permanent, a skipped capture is one
                    // aliased element. Counted above so the report still names it.
                    continue;
            }
            if (!isRoot && t.GetComponent<Canvas>() != null)
            {
                nested++;
                if (t.gameObject.layer == layer)
                    nestedOnLayer++;
            }
            if (t.gameObject.layer != layer)
            {
                if (!IsRecorded(e, t))
                    e.Relayered.Add(new LayerRecord { Transform = t, OriginalLayer = observed });
                t.gameObject.layer = layer;
                moved++;
                if (!isRoot && t.GetComponent<Canvas>() != null)
                    nestedOnLayer++;
            }
            for (int i = t.childCount - 1; i >= 0; i--)
                Scratch.Add(t.GetChild(i));
        }
        Scratch.Clear();
        e.LayersMoved = e.Relayered.Count;
        e.ForeignSkipped = skipped;
        e.ForeignLayerSkipped = foreignLayer;
        e.NestedTotal = nested;
        e.NestedCaptured = Mathf.Min(nestedOnLayer, nested);
        e.Sweeps++;
        double sweepMs = (Time.realtimeSinceStartup - sweepStart) * 1000.0;
        e.SweepMs += sweepMs;
        // Split the cost by what the window was doing, because that is the ONLY way the log can
        // price the motion cadence against the settled one. ModBuild 195 could only report one
        // averaged figure, which is why "1.5 ms per sweep" had to be multiplied by hand against
        // "sweeps every frame while moving" to see the problem.
        if (IsMoving(e))
        {
            e.MotionSweeps++;
            e.MotionSweepMs += sweepMs;
        }
        else
        {
            e.StillSweeps++;
            e.StillSweepMs += sweepMs;
        }
        if (!initial && moved > 0)
        {
            e.LateJoiners += moved;
            VRLog.Info(Scope, $"PANEL SUPERSAMPLE: {moved} pooled/late transform(s) of '{e.Window}' "
                              + $"joined capture layer {layer} (a repopulating window brings children "
                              + "on the game's UI layer; until they are swept they would be MISSING "
                              + "from the capture and drawn straight into the eye instead — which is "
                              + "the ModBuild 192 'manche Elemente ... fehlen im Fenster' report). "
                              + $"This sweep ran because the window {(IsMoving(e) ? "is MOVING (the "
                                  + MovingSweepIntervalFrames + "-frame motion cadence)"
                                  : "reached its periodic cadence")}. WHICH CADENCE THIS LINE READS "
                              + "IS THE MEASUREMENT that set MovingSweepIntervalFrames: in the "
                              + "ModBuild 195 log, 22 of the 23 lines like this one read 'periodic' "
                              + "and exactly ONE read the motion cadence, which is why the motion "
                              + "cadence is no longer every frame.");
        }
    }

    // ---- the frame budget instrument ------------------------------------------------------------

    /// <summary>
    /// Sample this frame's unscaled duration into the MOTION or the STILL bucket for this window.
    /// <para>WHY THIS EXISTS, and it is the one measurement the previous three rounds did not have.
    /// The report's own ModBuild 194 clause says a drag that moves the window several RENDERED eye
    /// pixels per frame is a JUDDER problem that no filtering can reach. The ModBuild 195 log then
    /// measured exactly that: the largest single-frame host steps on real drags are 12.39, 18.67,
    /// 32.70, 46.96, 73.70, 93.77, 122.33 and 132.85 RENDERED eye pixels. At that rate one dropped
    /// frame is over a hundred pixels of positional error, so whether the frame was DROPPED is the
    /// whole question — and the session's own frame telemetry reads p50 17.33 ms against an 11.11 ms
    /// budget. This splits that number by what the window was doing, so the next log can say whether
    /// a dragged window's frames are worse than a still window's frames and by how much.</para>
    /// </summary>
    private static void SampleFrameBudget(Entry e, bool moving)
    {
        SampleHand(e);
        float ms = Time.unscaledDeltaTime * 1000f;
        if (ms <= 0f || ms > 1000f)
            return; // a load spike or a paused frame is not a frame-budget sample
        if (moving)
        {
            e.MotionFrameSamples++;
            e.MotionFrameMs += ms;
            if (ms > e.MotionFrameMsMax)
                e.MotionFrameMsMax = ms;
            if (ms > FrameBudgetMs)
                e.MotionFramesOverBudget++;
            // THE JUDDER COUNTER, per drag. Over-budget is not the same statement as DROPPED: a
            // frame at 11.2 ms against an 11.11 ms budget is v-sync jitter, while a frame past
            // DroppedFrameMs is one the headset had to fill by showing the previous image again —
            // and at the drag speeds this class measures, that is tens of rendered pixels of
            // positional error on a high-contrast edge, which is what the eye reads as flicker.
            if (ms > DroppedFrameMs)
                e.DragDroppedFrames++;
            if (ms > e.DragWorstFrameMs)
                e.DragWorstFrameMs = ms;
        }
        else
        {
            e.StillFrameSamples++;
            e.StillFrameMs += ms;
            if (ms > e.StillFrameMsMax)
                e.StillFrameMsMax = ms;
            if (ms > FrameBudgetMs)
                e.StillFramesOverBudget++;
        }
    }

    // ---- the hand (ModBuild 204, item B) --------------------------------------------------------

    /// <summary>
    /// <b>IS A HAND ON THIS WINDOW RIGHT NOW?</b> <c>ConvertedPanel.GuardHostHeld</c> is the flag
    /// chosen, and the choice was made against the alternatives rather than by availability:
    /// <list type="bullet">
    /// <item><c>GuardHostHeld</c> (<c>ConvertedPanel.cs:426</c>) is written every
    /// <c>GrabbableModal.Tick</c> (<c>GrabbableModal.cs:467</c>) as
    /// <c>_handle != null &amp;&amp; _handle.IsGrabbed</c> — i.e. from the grab handle's own live
    /// state, in UPDATE, so a LateUpdate reader gets this frame's value and never last frame's. It is
    /// the only published flag that answers "a hand grips this window" rather than "this window
    /// moved".</item>
    /// <item><c>GuardHostMoving</c> is the wrong question outright: it is a 5 mm pose epsilon, i.e.
    /// the same class of value-watching this file already does in <see cref="NoticeGeometry"/>, and
    /// it is exactly what fires on a momentary pause of the hand.</item>
    /// <item><c>GrabbableModal.IsGrabbed</c> is the same boolean at its source, but it is
    /// <c>internal</c> on a component this class holds no reference to; reaching for it would mean a
    /// <c>GetComponent</c> per frame to learn what the panel already publishes.</item>
    /// </list>
    ///
    /// <para><b>AND WHAT IT READS FOR A WINDOW NOBODY EVER GRABS.</b> False, permanently and by
    /// construction: <c>CanvasConversion.Release</c>/conversion sets it false
    /// (<c>CanvasConversion.4.Lifecycle.cs:22</c>) and only a <c>GrabbableModal</c> ever writes it
    /// true. So a window moved by a settings change, by the content fit, by a recall or by a
    /// presence-regain refloat is never "held", its release gate is the pure stillness gate this
    /// class has always had, and it still gets both repair passes. The gate below can only ever
    /// DEFER a repair on a window a hand is actually gripping.</para>
    /// </summary>
    private static bool HandOn(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        return panel != null && panel.GuardHostHeld;
    }

    /// <summary>
    /// Sample the hand once per frame per window and record the RELEASE EDGE.
    ///
    /// <para><b>WHY AN EDGE AND NOT JUST THE FLAG.</b> Gating only on <c>!HandOn</c> would fire the
    /// repair on the frame the hand lets go of a window that had been held perfectly still for
    /// seconds — <see cref="Entry.LastMotionFrame"/> would be ancient and the settle gate would
    /// already be open. That is not wrong (the hand coming off IS the release) but it would run the
    /// repair before the window's own settle has been observed. Keying the gate on the LATER of the
    /// two stamps makes "released" mean what its name says: the hand is off AND the pose has been
    /// still for <see cref="ReleaseSettleFrames"/> frames since it came off.</para>
    /// </summary>
    private static void SampleHand(Entry e)
    {
        TargetLife life = LifeOf(e);
        bool on = HandOn(e);
        life.DragHandSamples++;
        if (on)
            life.DragHandOnFrames++;
        if (life.HandWasOn && !on)
            life.HandReleaseFrame = Time.frameCount;
        life.HandWasOn = on;
    }

    // ---- the repairs ----------------------------------------------------------------------------

    /// <summary>
    /// Run the two bounded repairs this class owes the "kaputte Anzeige" report, if either is due.
    ///
    /// <para><b>THE RELEASE REPAIR.</b> <see cref="ReleaseSettleFrames"/> frames after the last
    /// pose/scale/rect change — i.e. the instant the window comes to rest after a drag — force ALL
    /// THREE of the things a settled window's cadence would otherwise get to at its own pace: the
    /// content frame is re-measured, the capture layer is re-swept, and every text component in the
    /// subtree re-requests its glyphs and re-generates its mesh. A second pass follows
    /// <see cref="ReleaseSecondRepairFrames"/> frames later because the content fit can still flip
    /// the host rect after the release (the ModBuild 195 log's party window walks 328 -> 716 -> 1920
    /// uGUI px across one session). Two passes per release, never one per frame.</para>
    ///
    /// <para><b>THE FONT-REPACK REPAIR.</b> Armed by <see cref="OnFontTextureRebuilt"/> and by the
    /// TMP atlas check in <see cref="MeasureContent"/>. It regenerates text WITHOUT re-measuring or
    /// re-sweeping, because a repack changes what the glyphs look like and nothing about where the
    /// window is.</para>
    ///
    /// <para>Both END in a content-integrity scan, so the log records what the repair FOUND and not
    /// merely that it ran — a repair that never finds anything is a repair that should be deleted,
    /// and this is how the next round will be able to tell.</para>
    /// </summary>
    private static void ServiceRepairs(Entry e)
    {
        // A NEW motion event re-arms both stages. Keying the state machine on LastMotionFrame is what
        // makes "one release costs two repairs" true no matter how long the window then stands still.
        if (e.ReleaseRepairedMotionFrame != e.LastMotionFrame)
        {
            e.ReleaseRepairedMotionFrame = e.LastMotionFrame;
            e.ReleaseRepairStage = 0;
        }
        // ---- THE HAND GATE (ModBuild 204, item B) ------------------------------------------------
        // A REPAIR NAMED "RELEASE" MUST NOT RUN WHILE THE WINDOW IS HELD, and the ModBuild 203
        // hardware log says in one number that it did: of 33 PANEL SUPERSAMPLE RELEASE lines only 16
        // followed a hand release — 15 fired WHILE THE HAND WAS STILL HOLDING and 2 before any grab
        // — and one drag fired five of them in 1.9 s. Every one of those ran the unconditional
        // Reallocate, re-measured the capture frame at the most transient instant possible, and
        // regenerated 218 of 218 text components in ~16.5 ms, followed by a CanvasConversion.Late
        // spike of 19.2-20.7 ms in a total frame of 23.4-34.8 ms — on 19 of the 33. The most
        // expensive repair in this class was being triggered by a momentary pause of the user's hand.
        //
        // THE CAUSE was that the gate consulted nothing about the hand at all: the condition was
        // purely `Time.frameCount - e.LastMotionFrame >= 2`, and LastMotionFrame comes from OBSERVED
        // HOST VALUES against a position epsilon of half an authored pixel (NoticeGeometry). A hand
        // that hovers for two frames is, to that instrument, indistinguishable from a hand that let
        // go — this project's "a stillness gate never opens for state someone else rewrites each
        // frame", in its mirror image.
        //
        // THE GATE NOW READS BOTH: the pose must have settled AND the hand must be off. See HandOn
        // for which published flag was chosen and why, and for what a window nobody ever grabs does
        // (nothing changes for it: GuardHostHeld is false by construction there).
        TargetLife hand = LifeOf(e);
        bool held = HandOn(e);
        int sinceRelease = Time.frameCount - hand.HandReleaseFrame;
        int sinceMotion = Time.frameCount - e.LastMotionFrame;
        // The LATER of the two stamps, so "released" means the hand is off AND the pose has been
        // still for ReleaseSettleFrames frames SINCE it came off — see SampleHand.
        int sinceQuiet = sinceMotion < sinceRelease ? sinceMotion : sinceRelease;
        if (e.ReleaseRepairStage == 0 && sinceQuiet >= ReleaseSettleFrames && held)
        {
            // THE STILLNESS GATE IS OPEN AND THE HAND IS STILL ON. This is exactly the frame ModBuild
            // 203 ran the whole repair on, 15 times out of 33. Counted once per motion event, not
            // once per frame — see TargetLife.ReleasesDeferredHeld.
            if (hand.DeferredAtMotionFrame != e.LastMotionFrame)
            {
                hand.DeferredAtMotionFrame = e.LastMotionFrame;
                hand.ReleasesDeferredHeld++;
            }
            return; // deferred, NOT skipped: the stage stays 0 and the gate opens when the hand does
        }
        if (e.ReleaseRepairStage == 0 && sinceQuiet >= ReleaseSettleFrames)
        {
            e.ReleaseRepairStage = 1;
            e.ReleasesThisWindow++;
            if (hand.DragHandOnFrames > 0)
                hand.ReleasesAfterHold++;
            else
                hand.ReleasesNeverHeld++;
            if (sinceMotion > e.ReleaseGateFramesMax)
                e.ReleaseGateFramesMax = sinceMotion;
            ReleaseRepair(e);
            ReportRelease(e, sinceMotion);
            return;
        }
        if (e.ReleaseRepairStage == 1 && !held && sinceQuiet >= ReleaseSecondRepairFrames)
        {
            e.ReleaseRepairStage = 2;
            ReleaseRepair(e);
            return;
        }

        if (e.RebuildRepairFrame >= 0 && Time.frameCount >= e.RebuildRepairFrame)
        {
            e.RebuildRepairFrame = -1;
            e.RebuildRepairs++;
            // repairAll: a repack invalidates the UVs of meshes that still pass every integrity test,
            // so "nothing measured wrong" is not a reason to leave them alone here.
            MeasureContent(e, repairAll: true);
        }
    }

    /// <summary>
    /// <b>MODBUILD 197 MAKES THE ModBuild 196 RELEASE REPAIR ACTUALLY REGENERATE TEXT.</b> Its own
    /// documentation said it forces <i>"every text component in the subtree to re-request its glyphs
    /// and re-generate its mesh"</i>. It did not, and the hardware log says so in one number: across
    /// 236 state lines the largest regeneration this repair ever performed was <b>one character
    /// across one component</b>, and 151 of those lines read <c>0 character(s) across 0
    /// component(s)</c> — on windows carrying up to 297 text components and 4600 glyphs.
    ///
    /// <para>THE CAUSE was a gate, not a failure: <c>ScanTmpText</c> returns without regenerating
    /// whenever its own defect count is zero (<c>if ((!repairAll &amp;&amp; bad == 0) || ...) return
    /// false;</c>), and that count is zero in every reading this session produced. So the repair was
    /// conditioned on the very instrument ModBuild 196 shipped to find out whether the condition was
    /// the right one. The user reporting "no improvement" is therefore the expected outcome and NOT
    /// evidence about the text hypothesis: the remedy never ran.</para>
    ///
    /// <para>THE RELEASE REPAIR NOW PASSES <c>repairAll</c>. The report cadence's scan does not — a
    /// settled window must keep costing what it costs today — so the regeneration is bounded to two
    /// passes per release, capped by <c>MaxRegeneratePerScan</c>, and its cost is measured into
    /// <see cref="Entry.ReleaseRegenMs"/> and printed. THE CONSEQUENCE IF IT IS TOO EXPENSIVE: the
    /// release frame gets longer, which the RELEASE line reports per release; the lever is that cap.
    /// THE CONSEQUENCE FOR THE NEXT ROUND EITHER WAY: if the broken image survives a release on which
    /// every text component in the window was genuinely re-parsed and re-generated, the text-source
    /// family is dead outright rather than untested.</para>
    /// </summary>
    private static void ReleaseRepair(Entry e)
    {
        e.ReleaseRepairs++;
        MeasureFrame(e);
        SyncProjection(e);
        if (e.DisplayRect != null)
            e.DisplayRect.sizeDelta = e.Frame.size;
        SyncDisplayPose(e);
        // ITEM 5, ANSWERED IN THE CODE RATHER THAN IN A COMMENT. This call IS unconditional, and it
        // is ALREADY a no-op whenever the frame has not changed: Reallocate's first branch compares
        // the resolved pixel counts against the live ones and returns without touching a target. What
        // it costs in that case is ResolveRate — a handful of float operations and two bounded loops
        // — so the 203 log's 16.5 ms release is not here. It is the three lines around it:
        // MeasureFrame, ApplyCaptureLayer and MeasureContent(repairAll: true), the last of which
        // regenerated 218 of 218 text components and is the DELIBERATE repair that must not be
        // removed. The no-op is now COUNTED per trigger (TargetLife.NoOpByTrigger) and printed, so
        // the next log states it instead of leaving it to be re-derived from the source.
        Reallocate(e, e.Frame, ReallocTrigger.ReleaseRepair);
        e.NextSweepFrame = Time.frameCount + SweepIntervalFrames;
        ApplyCaptureLayer(e, initial: false);
        // Scan AND repair in one walk: the counts recorded are the PRE-repair state, so the report
        // says what was wrong at the release rather than only that a repair ran.
        float regenStart = Time.realtimeSinceStartup;
        MeasureContent(e, repairAll: true);
        e.ReleaseRegenComponents = e.RegeneratedComponents;
        e.ReleaseRegenMs = (Time.realtimeSinceStartup - regenStart) * 1000.0;
    }

    /// <summary>
    /// <b>ONE LINE PER RELEASE — the instrument this symptom did not have.</b>
    ///
    /// <para>THE USER'S NEW WORD IS <i>ABRUPT</i>: <i>"das Flackerproblem WÄHREND DER BEWEGUNG ist
    /// noch da — inklusive der möglichen kaputten Darstellung, wenn man nach der Bewegung ABRUPT
    /// loslässt"</i>. That is a claim about the release VELOCITY, and nothing in ModBuild 196 could
    /// confirm or deny it: the state line carried only a cumulative repair count on a 10-second
    /// cadence, so a gate that opened late, a gate that never opened and a gate that opened on time
    /// and found nothing all printed the same character.</para>
    ///
    /// <para>WHAT THIS LINE DECIDES, and each field carries the comparison it is read against:
    /// <list type="number">
    /// <item>WAS THE RELEASE ABRUPT? The drag's LAST single-frame step next to its LARGEST, both in
    /// rendered eye pixels. A hand that slowed before letting go leaves a last step far below the
    /// largest; a hand still travelling leaves them equal. If the broken image really does correlate
    /// with abruptness, these two numbers are where it shows.</item>
    /// <item>DID THE SETTLE GATE OPEN, AND WHEN? The frames from the last change to this repair,
    /// against <see cref="ReleaseSettleFrames"/>. A gate that opens exactly on the threshold is a
    /// gate that works; one that opens tens of frames late means somebody kept writing the pose after
    /// the hand let go, which is this project's "a stillness gate never opens for state someone else
    /// rewrites each frame". THE LONGEST MOTION RUN is printed with it, because a gate that never
    /// opens at all produces no line here and only that field would show why.</item>
    /// <item>HOW MANY BROKEN FRAMES THE EYE ALREADY SAW. The repair runs in the LateUpdate of the
    /// frame the gate opens, so the eye has ALREADY drawn every frame since the hand let go with
    /// whatever state the release left behind. That number is printed, because it bounds what a
    /// repair on this schedule can ever fix: a defect the user sees for 2 frames and a defect that
    /// persists are different reports, and this instrument cannot be read as the second one.</item>
    /// <item>WHAT THE SCAN FOUND AT THAT EXACT INSTANT, with its comparison count, its worst value
    /// and its threshold on the same line — including the MESH counters, which are new and which are
    /// the only ones that can see the photograph's fault (see <see cref="ScanTmpMesh"/>).</item>
    /// <item>WHETHER THE DRAG WAS DROPPING FRAMES. The count, the worst frame time and the
    /// <see cref="DroppedFrameMs"/> threshold, so the moving complaint can be read as judder or not
    /// from the same line as the release.</item>
    /// </list></para>
    /// </summary>
    private static void ReportRelease(Entry e, int gateFrames)
    {
        // ModBuild 205: ARM THE TWO RELEASE READINGS OF THE INK CENSUS — one on this very frame (the
        // release edge) and one InkSettleFrames frames later. The user reports the picture FREEZING on
        // release, so those two readings are what separate a transient broken image from a latched
        // one, and no earlier instrument in this class could express that sentence about the CAPTURED
        // IMAGE at all. Both are issued from the capture camera's onPostRender, so each reads the
        // frame it names. See the ink-census block at the foot of this file.
        ArmInkCensusForRelease(e);
        // MEASURE THE RESAMPLE AT THE RELEASE INSTANT (ModBuild 198), before anything else on this
        // line is computed. Two things follow from doing it here rather than reading the last 10 s
        // report: the RENDERED-eye-px figures below stop reading -1 on a release that lands before
        // this panel's first report (15 of the ModBuild 197 log's 100 release lines did), and the
        // line can state what the eye was sampling AT THE MOMENT THE HAND LET GO — which is the only
        // instant the user's "the state gets FROZEN" claim is about. See BandLimitFactor.
        RenderTexture releaseShown = DisplayTexture(e);
        string resample = SamplingSentence(e, Rig.VRRigDriver.HeadCamera,
            releaseShown != null ? releaseShown.mipMapBias : 0f);

        // The world -> rendered-eye-pixel bridge is measured by SamplingSentence, i.e. on the 10 s
        // report cadence and (since ModBuild 198) on every release. Before any completed measurement
        // it is 0, and dividing by a floor would print a number thousands of times too large. A
        // release that lands in that gap says so instead, because an unlabelled wrong number is worse
        // than a missing one.
        bool scaled = e.WorldPerAuthoredPx > 1e-9f && e.AuthoredPerRenderedPx > 1e-6f;
        float perAuthored = Mathf.Max(e.WorldPerAuthoredPx, 1e-9f);
        float perRendered = Mathf.Max(e.AuthoredPerRenderedPx, 1e-6f);
        float lastPx = scaled ? e.DragLastStepWorld / perAuthored / perRendered : -1f;
        float maxPx = scaled ? e.DragMaxStepWorld / perAuthored / perRendered : -1f;
        string pxNote = scaled
            ? string.Empty
            : " (the RENDERED eye px figures read -1 because the world-to-eye-pixel scale has not "
              + "been measured yet — it is derived once per 10 s report and this release landed "
              + "before this panel's first one; the world-unit figures are exact regardless)";
        int meshBad = e.MeshMissingQuads + e.MeshDegenerateQuads + e.MeshDegenerateUv
                      + e.MeshUvOutOfRange + e.MeshNonFinite;
        int glyphBad = e.GlyphsNotInAtlas + e.GlyphsNotVisible + e.GlyphsBlankQuad;
        // ModBuild 204 (item B): THE HAND, AT THE INSTANT THIS REPAIR FIRED. In the 203 log 15 of 33
        // of these lines fired while the hand was still holding, and nothing on the line could say
        // so. `held=` must now read False on every line this build prints; a True is the gate having
        // failed, which is a finding rather than a detail.
        TargetLife hand = LifeOf(e);
        string handSentence =
            $" HAND AT THIS RELEASE: held={HandOn(e)}, handsOn={hand.DragHandOnFrames} of "
            + $"{hand.DragHandSamples} frame(s) of this drag had a hand on the window "
            + "(ConvertedPanel.GuardHostHeld, written every GrabbableModal.Tick from the grab "
            + $"handle's own IsGrabbed), the hand last let go {Time.frameCount - hand.HandReleaseFrame} "
            + "frame(s) ago, and this class has DEFERRED "
            + $"{hand.ReleasesDeferredHeld} release repair(s) since engage because a hand was still "
            + $"holding. SINCE ENGAGE: {hand.ReleasesAfterHold} release(s) followed a real hold and "
            + $"{hand.ReleasesNeverHeld} fired on a window no hand had touched during the drag (a "
            + "settings change, a content re-fit, a recall or a presence-regain refloat — those are "
            + "legitimate and are exactly why the gate DEFERS on the hand rather than requiring one). "
            + "READ held= FIRST: the ModBuild 203 log had 15 of 33 of these lines firing MID-DRAG, "
            + "each costing an unconditional re-allocation plus a ~16.5 ms text regeneration plus a "
            + "19-21 ms CanvasConversion.Late spike in a 23-35 ms frame, and it could not say so "
            + "because the gate consulted only an observed pose epsilon of half an authored pixel. If "
            + "held= ever reads True on this build, the hand gate is not in force and every judgement "
            + "of the release behaviour from that session is a judgement of ModBuild 203's.";
        VRLog.Info(Scope,
            $"PANEL SUPERSAMPLE RELEASE '{e.Window}': the drag ran {e.DragMotionFrames} frame(s) "
            + $"({Time.frameCount - e.DragStartFrame} frame(s) wall) and the settle gate opened "
            + $"{gateFrames} frame(s) after the last change, against a threshold of "
            + $"{ReleaseSettleFrames}." + handSentence + " ABRUPTNESS (the user's own word, measured): the LAST "
            + $"single-frame host step before the hand let go was {e.DragLastStepWorld:F4} world "
            + $"units = {lastPx:F2} RENDERED eye px, against this drag's LARGEST step of "
            + $"{e.DragMaxStepWorld:F4} = {maxPx:F2} RENDERED eye px and a detector epsilon of "
            + $"{e.MotionEpsWorld:F4} world units{pxNote} — the two being equal means the window was "
            + "still travelling at full speed when it was released, and a last step far below the "
            + "largest means the hand slowed first. THE EYE HAS ALREADY DRAWN "
            + $"{gateFrames} frame(s) of the released image before this repair ran, and will draw "
            + $"{ReleaseSecondRepairFrames - gateFrames} more before the second pass, so a defect "
            + "this repair removes is one the player still sees for that long; a defect the player "
            + "reports as PERSISTING is one this repair did not remove at all. WHAT THE SCAN FOUND "
            + $"AT THIS INSTANT: {glyphBad} text-source defect(s) out of {e.GlyphsChecked} glyph "
            + $"lookup(s) across {e.TextComponents} component(s) ({e.GlyphsNotInAtlas} not in the "
            + $"atlas, {e.GlyphsNotVisible} not visible, {e.GlyphsBlankQuad} zero-area layout quad at "
            + $"threshold {DegenerateQuadArea:G3}); and {meshBad} SUBMITTED-MESH defect(s) out of "
            + $"{e.MeshQuadsChecked} quad(s) examined ({e.MeshMissingQuads} never written into the "
            + $"mesh, {e.MeshDegenerateQuads} zero-area in the mesh, {e.MeshDegenerateUv} with a "
            + $"collapsed atlas UV rect at threshold {DegenerateUvArea:G3}, {e.MeshUvOutOfRange} "
            + $"sampling outside the atlas, {e.MeshNonFinite} non-finite)"
            + (e.MeshWorstBad > 0 ? $", worst: {e.MeshWorst} with {e.MeshWorstBad}" : ", no worst")
            // THE SUB-MESH READING AT THE RELEASE INSTANT (ModBuild 200). Both counts above describe
            // the TEXT COMPONENT; these describe the CHILD objects that draw every glyph its first
            // material does not serve. See Entry.SubMeshesSeen. This is the family that can be
            // perfect in the mesh and absent from the capture at the same time, which is the shape of
            // the photograph, and it is the one the release repair has never been able to see.
            + $"; and {e.SubMeshesSeen} TMP SUB-MESH(ES) — the child objects that draw a second atlas "
            + "page, a fallback font or an inline sprite, and that every count above is blind to — of "
            + $"which {e.SubMeshesEmpty} EMPTY AND POOLED (ModBuild 201: TMP keeps one child per "
            + "material reference the string has ever needed and empties the surplus, and an empty "
            + "one is legitimately culled, transparent and untextured — the ModBuild 200 reading of "
            + "'19 of which 17 culled' on this window was that, not a defect), leaving "
            + $"{e.SubMeshesInUse} that CARRY VERTICES as the real denominator, of which "
            + $"{e.SubMeshesInactive} inactive, {e.SubMeshesCulled} culled/transparent, "
            + $"{e.SubMeshesNoTexture} with no bound texture; and {e.SubMeshesWrongLayer} of all "
            + $"{e.SubMeshesSeen} OFF THE PRIVATE CAPTURE LAYER {e.Layer} (present in the mesh, "
            + "present in the eye, missing from the texture — repaired on sight, never gated on a "
            + "count)"
            + (e.SubMeshWorst.Length > 0 ? $", worst: {e.SubMeshWorst}" : ", no worst")
            // THE CONTENT-SCALE READING AT THE RELEASE INSTANT (ModBuild 201) — the field that
            // answers the user's own question. Full argument on Entry.MinContentScale.
            + $". CONTENT SCALE AT THIS RELEASE: the smallest substantial content scale in this "
            + $"window is {e.MinContentScale:F3}"
            + (e.MinContentScale < ScaledContentThreshold
                ? $" ('{e.MinContentScaleName}'), so that subtree is captured at "
                  + $"{(Mathf.Max(e.AchievedFactor, 0.01f) * e.MinContentScale):F2} texels per ITS "
                  + $"OWN authored px while the rest of the window gets {e.AchievedFactor:F2} — "
                  + $"against a band limit of {BandLimitedTexelsPerPixel:F2}. THAT ASYMMETRY IS THE "
                  + "COMPLETE CORRELATION with the report: the two sub-views the user names are the "
                  + "two the content fit scales, and they are the only content in this mod captured "
                  + "at about one texel per authored pixel"
                : " — nothing substantial in this window is downscaled, so every subtree in it is "
                  + "captured at the same rate")
            + $". DISTRIBUTION SINCE ENGAGE: LOWEST {e.MinContentScaleLowest:F3}, MEAN "
            + $"{(e.ContentScaleReadings > 0 ? e.ContentScaleSum / e.ContentScaleReadings : 1f):F3}, "
            + $"HIGHEST {e.MinContentScaleHighest:F3} over {e.ContentScaleReadings} reading(s) "
            + $"({e.ContentScaleUnmeasured} unmeasured and excluded)"
            + $". THE REPAIR THEN RE-GENERATED {e.ReleaseRegenComponents} of {e.TextComponents} "
            + $"component(s) in {e.ReleaseRegenMs:F2} ms (cap {MaxRegeneratePerScan}) — ModBuild 196 "
            + "re-generated at most ONE component per release because it gated the regeneration on "
            + "the defect count, which is permanently zero; this pass is unconditional, so if the "
            + "broken image survives it the text-source family is falsified rather than untested. "
            + $"FRAME PACING DURING THIS DRAG: {e.DragDroppedFrames} of {e.DragMotionFrames} "
            + $"frame(s) exceeded {DroppedFrameMs:F2} ms (worst {e.DragWorstFrameMs:F2} ms, budget "
            + $"{FrameBudgetMs:F2} ms), i.e. that many frames on which the headset re-showed the "
            + $"previous image while the window was travelling up to {maxPx:F2} rendered px per "
            + "frame — that product, not the panel's filtering, is what a JUDDER reading of the "
            + "moving complaint rests on. LONGEST UNBROKEN MOTION RUN this report window: "
            + $"{e.LongestMotionRun} frame(s); if that ever equals the whole window, no release can "
            + "be detected at all and this line would simply be absent. "
            + "WHAT THE EYE IS SAMPLING AT THIS EXACT RELEASE — the ModBuild 198 field, and the one "
            + "that decides the FROZEN half of the report. The displayed texture is NOT frozen: the "
            + "capture camera keeps rendering and resolving every frame after a release (the CAPTURE "
            + "PATH field's still-capture count climbs by ~900 per 10 s report window forever), so "
            + "the quad is re-photographed at 90 Hz from a settled canvas. What can freeze is the "
            + "sub-texel PHASE at which the eye resamples that photograph, and only a reading below "
            + "the threshold below can produce it — "
            + resample);
    }

    /// <summary>
    /// Move <paramref name="t"/>'s layer record from whichever OTHER entry owns it to us, and hand
    /// back the layer the GAME originally gave it. Returns false when no other entry has a record —
    /// see the call site for why that case refuses instead of guessing.
    /// </summary>
    private static bool TryTakeForeignRecord(Entry mine, Transform t, out int originalLayer)
    {
        originalLayer = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry other = Entries[i];
            if (ReferenceEquals(other, mine))
                continue;
            for (int r = 0; r < other.Relayered.Count; r++)
            {
                if (!ReferenceEquals(other.Relayered[r].Transform, t))
                    continue;
                originalLayer = other.Relayered[r].OriginalLayer;
                other.Relayered.RemoveAt(r);
                other.LayersMoved = other.Relayered.Count;
                return true;
            }
        }
        return false;
    }

    private static bool IsRecorded(Entry e, Transform t)
    {
        for (int i = 0; i < e.Relayered.Count; i++)
        {
            if (ReferenceEquals(e.Relayered[i].Transform, t))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Hand every recorded transform its original layer back — and ONLY while it is still on OUR
    /// layer. That guard is what makes this restore commute with
    /// <c>CanvasConversion.Release</c>'s: whichever runs first, the transform ends on the layer the
    /// game gave it, and neither restore can overwrite the other's work.
    /// </summary>
    private static void RestoreLayers(Entry e)
    {
        int layer = e.Layer;
        if (layer < 0)
        {
            e.Relayered.Clear();
            return;
        }
        for (int i = 0; i < e.Relayered.Count; i++)
        {
            LayerRecord record = e.Relayered[i];
            if (record.Transform != null && record.Transform.gameObject.layer == layer)
                record.Transform.gameObject.layer = record.OriginalLayer;
        }
        e.Relayered.Clear();
    }

    // ---- camera hooks -------------------------------------------------------------------------

    private static void InstallHooks()
    {
        if (_hooksInstalled)
            return;
        _hooksInstalled = true;
        Camera.onPreCull += OnPreCull;
        Camera.onPostRender += OnPostRender;
        // THE TWO EVENTS THAT MAKE THE RELEASE REPORT DECIDABLE. Font.textureRebuilt fires when a
        // DYNAMIC font atlas is re-packed, which silently invalidates the UVs of every text mesh
        // already generated against it; Canvas.willRenderCanvases fires when uGUI runs its layout and
        // graphic rebuild queue for the frame. Recording the FRAME of each is what lets the capture
        // say whether it ran mid-repack or ahead of the rebuild — see Entry.CapturesDuringFontRebuild
        // and Entry.CapturesBeforeCanvasUpdate. Both are cheap: one int store per event.
        Font.textureRebuilt += OnFontTextureRebuilt;
        Canvas.willRenderCanvases += OnWillRenderCanvases;
    }

    private static void UninstallHooks()
    {
        if (!_hooksInstalled)
            return;
        _hooksInstalled = false;
        Camera.onPreCull -= OnPreCull;
        Camera.onPostRender -= OnPostRender;
        Font.textureRebuilt -= OnFontTextureRebuilt;
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
    }

    /// <summary>
    /// A DYNAMIC FONT ATLAS WAS RE-PACKED. Every text mesh already generated against it now points at
    /// atlas regions that may hold different glyphs or nothing at all, and a canvas that is not
    /// re-generated keeps showing those stale UVs. uGUI's own <c>Text</c> subscribes to this event and
    /// re-generates itself, but only if it <c>IsActive()</c>, and TextMeshPro does not use this event
    /// at all — so this class arms its OWN repair for every engaged panel rather than trusting either.
    /// <para>Armed for the NEXT frame, not this one: an atlas being re-packed is usually still being
    /// filled by the requests that caused the repack, and regenerating inside that would just be
    /// first in the queue for the next repack.</para>
    /// </summary>
    private static void OnFontTextureRebuilt(Font font)
    {
        try
        {
            _fontRebuilds++;
            _fontRebuildFrame = Time.frameCount;
            _fontRebuildName = font != null ? font.name : "(null)";
            ArmRebuildRepair();
        }
        catch
        {
            // Never throw out of an engine callback.
        }
    }

    private static void OnWillRenderCanvases() => _canvasUpdateFrame = Time.frameCount;

    /// <summary>Ask every live entry to re-request its glyphs and re-generate its text on the next
    /// LateUpdate. Idempotent: an entry already armed keeps its earlier (never later) due frame.</summary>
    private static void ArmRebuildRepair()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry e = Entries[i];
            if (e.RebuildRepairFrame < 0)
                e.RebuildRepairFrame = Time.frameCount + 1;
        }
    }

    /// <summary>
    /// Clear the capture-layer bit from every camera that is not one of ours, for the duration of
    /// that camera's own render. WHY HERE and not on the head camera once per frame:
    /// <c>VRRigDriver.TickHeadCullingMask</c> re-writes the head mask every frame from the anchor
    /// camera's mask (0xFFFFFFFF in a scenario), so a mask edit from a tick would be reverted and
    /// re-applied forever — a write war, which this project has already paid for once. Editing
    /// inside the camera's own callback and handing the value back in
    /// <see cref="Camera.onPostRender"/> means no other writer ever observes a changed value, and
    /// MultiPass's two eye passes are treated identically because the pair fires once per pass.
    /// <para>The display quad's final pose is copied here too — the last moment in the frame, after
    /// every LateUpdate pose writer (grab, order ladder, board docks) has run.</para>
    /// </summary>
    private static void OnPreCull(Camera cam)
    {
        try
        {
            if (cam == null || Entries.Count == 0 || _poolMask == 0)
                return;
            // ---- THE PHASE CENSUS (ModBuild 214) ---------------------------------------------
            // BEFORE the own-camera branch and outside it: this has to run for EVERY camera in the
            // frame, especially the two MultiPass eye passes, because the whole question is whether
            // the drawn set differs between two cameras of one frame. Sampling only our own capture
            // camera would reproduce exactly the blind spot this instrument exists to remove.
            for (int pi = 0; pi < Entries.Count; pi++)
                SamplePhaseFor(Entries[pi], cam);

            Entry? mine = EntryForCamera(cam);
            if (mine != null)
            {
                // ModBuild 204: a PRIMING render is not a frame's capture. PrimeNewTarget calls
                // Camera.Render() by hand, which fires this callback re-entrantly; letting it through
                // would inflate CaptureTicks, MotionCaptures and the two coincidence counters, and
                // the CAPTURE PATH field's whole readability rests on those sharing one denominator
                // with the frame count. The prime writes its own projection and does its own resolve.
                if (ReferenceEquals(mine, _primingEntry))
                    return;
                mine.LastCaptureStart = Time.realtimeSinceStartup;
                // THE CAPTURE-COINCIDENCE COUNTERS, recorded at the LAST instant before this camera
                // culls — i.e. the exact state the captured image is taken from. Every one of them
                // has a denominator (CaptureTicks) recorded on the same line, so "the instrument
                // never ran", "it ran and found nothing" and "it ran and found something" are three
                // different readings and can never print the same character.
                mine.CaptureTicks++;
                // Split by what the window is doing, so "was this window supersampled WHILE IT
                // MOVED" is a count and not an argument from the source.
                if (IsMoving(mine))
                    mine.MotionCaptures++;
                else
                    mine.StillCaptures++;
                if (_fontRebuildFrame == Time.frameCount)
                    mine.CapturesDuringFontRebuild++;
                if (_canvasUpdateFrame != Time.frameCount)
                    mine.CapturesBeforeCanvasUpdate++;
                // THE LAST INSTANT BEFORE THIS CAMERA CULLS. Re-deriving the projection here is what
                // makes a two-hand resize correct in the frame it happens: orthographicSize is a
                // WORLD-unit quantity read from the host's lossy scale, and GrabbableModal's own
                // LateUpdate host re-sync has no defined order against ours. See SyncGeometry's
                // ORDERING paragraph.
                SyncProjection(mine);
                return; // our own capture camera: it is the ONE camera that may see the layer
            }
            if (_poseSyncFrame != Time.frameCount)
            {
                _poseSyncFrame = Time.frameCount;
                for (int i = 0; i < Entries.Count; i++)
                {
                    SyncDisplayPose(Entries[i]);
                    // ModBuild 204, THE COUNTER THAT DID NOT EXIST. This is the first camera of the
                    // frame that is not one of ours, i.e. the instant after every depth = -200
                    // capture camera has rendered and resolved and before any eye pass draws the
                    // quad. Full argument, and the one caveat, on NoteQuadSample.
                    NoteQuadSample(Entries[i]);
                }
            }
            // THE UNION, not one bit: since ModBuild 194 every engaged panel holds its own pool
            // layer, so a camera that is not one of ours must lose ALL of them for the duration of
            // its render or a panel would be drawn into the eye as well as into its own capture.
            int bits = _poolMask;
            if ((cam.cullingMask & bits) == 0)
                return;
            MaskedCameras[cam] = cam.cullingMask;
            cam.cullingMask &= ~bits;
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the pre-cull mask guard");
        }
    }

    private static void OnPostRender(Camera cam)
    {
        try
        {
            if (cam == null)
                return;
            if (MaskedCameras.TryGetValue(cam, out int original))
            {
                cam.cullingMask = original;
                MaskedCameras.Remove(cam);
                return;
            }
            Entry? e = EntryForCamera(cam);
            if (e == null)
                return;
            // See OnPreCull: a priming render is not a frame's capture, and PrimeNewTarget resolves
            // and books it itself.
            if (ReferenceEquals(e, _primingEntry))
                return;
            ResolveAndMip(e);
            // ModBuild 204: the display target the quad shows has now had one COMPLETE capture +
            // resolve + mip. Recorded here and in PrimeNewTarget and nowhere else — the invariant
            // must never be inferred from a frame count or from "the camera was enabled".
            NoteCaptureCompleted(e);
            // ModBuild 205: THE INK CENSUS IS ISSUED HERE, and this is the only instant in the frame
            // at which it can be. The readback must read THIS frame's resolved image, so it has to be
            // requested AFTER ResolveAndMip and BEFORE any eye pass — which is exactly where this
            // callback sits (the capture camera is at depth = -200). Requesting it from LateUpdate
            // instead would read the PREVIOUS frame's picture and every release-edge reading would be
            // one frame stale, on the one edge the whole instrument exists to measure.
            ServiceInkCensus(e);
            e.Captures++;
            if (e.LastCaptureStart > 0f)
                e.CaptureMs += (Time.realtimeSinceStartup - e.LastCaptureStart) * 1000.0;

            // THE FALSIFIER, kept from ModBuild 192 and now pointed at the texture the eye actually
            // samples. If this ever fires again, the mip half of this design is not running and the
            // window will crawl under motion no matter how sharp it looks while still.
            RenderTexture shown = DisplayTexture(e);
            if (!e.MipWarned && (shown == null || shown.mipmapCount <= 1))
            {
                e.MipWarned = true;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' is displayed from a render target "
                                  + $"with mipmapCount={(shown != null ? shown.mipmapCount : 0)} — NO "
                                  + "mip chain. THE CONSEQUENCE: the panel is drawn from a single "
                                  + "full-resolution level, so the eye minifies it unfiltered and the "
                                  + "shimmer this path exists to remove will still be there — "
                                  + "invisible while the head and the window are still, and crawling "
                                  + "as soon as either moves. Everything else (input, geometry, MSAA) "
                                  + "is unaffected.");
            }

            ReportMipLodOffset(e, shown);
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the post-render resolve and mip generation");
        }
    }

    /// <summary>
    /// RESOLVE, THEN BAND-LIMIT — run in the capture camera's own <see cref="Camera.onPostRender"/>,
    /// i.e. immediately after the frame it belongs to and (because that camera sits at
    /// <c>depth = -200</c>) before any other camera in the frame has culled. Both MultiPass eye
    /// passes therefore read one finished, identical, fully mipped texture, which is the invariant
    /// <c>CameraOrderProbe</c> measured and this path must not break.
    ///
    /// <para><see cref="Graphics.Blit(Texture, RenderTexture)"/> is what performs the MSAA resolve:
    /// binding a multisampled RenderTexture as a source texture resolves it, and the default blit
    /// material is a straight copy (<c>Blend Off</c>), so the capture's PREMULTIPLIED alpha survives
    /// the trip byte for byte — the composite derived in <see cref="BuildDisplay"/> is unchanged by
    /// this indirection. The project renders in Gamma colour space and both targets are
    /// <c>RenderTextureFormat.Default</c> with sRGB=False, so no colour conversion happens either.</para>
    ///
    /// <para><see cref="RenderTexture.active"/> is saved and restored around the blit because
    /// <c>Graphics.Blit</c> re-points it at its destination and we are inside the engine's own camera
    /// loop; leaving it moved would hand the next camera a target it did not ask for.</para>
    /// </summary>
    /// <param name="priming">ModBuild 204: this resolve belongs to a <see cref="PrimeNewTarget"/>
    /// render and not to a frame's capture, so it must not enter the moving/still resolve census —
    /// those counts are read directly against the moving/still CAPTURE counts on the same line, and a
    /// resolve without a matching capture would read as the very anomaly that field exists to
    /// detect.</param>
    private static void ResolveAndMip(Entry e, bool priming = false)
    {
        if (e.Rt == null || e.MipRt == null)
            return; // fallback: the quad shows the capture target directly (one Warn at allocation)
        RenderTexture? previous = RenderTexture.active;
        try
        {
            // THE CAPTURE-SIDE FALSIFIER (ModBuild 197). "The capture, not the content, is what
            // breaks" needs a measurement, and this is the cheapest one that can find it: a resolve
            // whose source and destination disagree in size is a target that was re-allocated
            // between the capture and the resolve, and the blit then stretches one frame's image
            // across the other's texels. Expected 0 — Reallocate replaces BOTH targets together,
            // inside a LateUpdate, before the camera loop — and printed with its denominator so the
            // zero is evidence rather than an absent line.
            if (e.Rt.width != e.MipRt.width || e.Rt.height != e.MipRt.height)
                e.ResolveSizeMismatch++;
            Graphics.Blit(e.Rt, e.MipRt);
            e.MipRt.GenerateMips();
            e.MipCount = e.MipRt.mipmapCount;
            // RE-ASSERT THE MIP LOD OFFSET (ModBuild 203). Two reasons, and neither is paranoia
            // about the write itself. (1) The dial is live-tunable in the headset's options tab, and
            // a target allocated before the player touched the arrows would otherwise keep the old
            // value until the window happened to be re-allocated — the "changed it and nothing
            // happened" report this project has already had twice. (2) GenerateMips rebuilds the
            // chain every capture, and re-stating sampler state right after it costs one float
            // compare. ApplyMipLodOffset writes ONLY when the live value differs, so this is not a
            // per-frame write and cannot become a write war.
            ApplyMipLodOffset(e.MipRt, AskedMipLodOffset());
            if (priming)
                return; // counted as a PRIME, not as a frame's resolve — see the parameter doc
            if (IsMoving(e))
                e.MotionResolves++;
            else
                e.StillResolves++;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    /// <summary>
    /// <b>ONE LINE PER WINDOW PER 10 s FOR THE ModBuild 203 DIAL — and it lives here, in its own
    /// line, rather than inside the state line's legibility sentence, because that sentence is built
    /// in <c>PanelSupersample.3.Report.cs</c>, which this lane does not own and whose
    /// <c>Entry</c> carries no field to hand a read-back through.</b>
    ///
    /// <para><b>WHAT IT MUST NEVER DO IS PRINT THE ASK TWICE.</b> The value called "in force" is read
    /// off the live <see cref="RenderTexture"/> the eye is sampling this very frame, in
    /// <see cref="ApplyMipLodOffset"/>; the value called "asked" comes from the config. Four remedies
    /// in this project shipped and never executed, and every one of them logged as if it had. If
    /// those two numbers ever differ, that is the finding and the line says so.</para>
    /// </summary>
    private static void ReportMipLodOffset(Entry e, RenderTexture? shown)
    {
        float now = Time.realtimeSinceStartup;
        if (MipBiasNextReport.TryGetValue(e.Window, out float due) && now < due)
            return;
        MipBiasNextReport[e.Window] = now + MipBiasReportSeconds;

        float asked = AskedMipLodOffset();
        float live = shown != null ? shown.mipMapBias : 0f;
        bool mipped = shown != null && shown.mipmapCount > 1;
        // THE IDENTITY THE WHOLE TRADE REDUCES TO. Trilinear selects LOD = log2(texels per rendered
        // px), i.e. the level with ~1 texel per pixel; a bias b shifts that to LOD + b, so the level
        // actually sampled carries 2^-b texels per rendered pixel WHATEVER the minification is. At
        // 0.00 that is 1.00 (matched); at -0.50 it is 1.41; at -1.00 it is 2.00 — twice what the
        // pixel grid can carry, which is the undersampling this whole path exists to remove.
        float sampledTexelsPerPx = Mathf.Pow(2f, -live);
        // …and the other side of the same identity: unfiltered level 0 comes back into the blend as
        // soon as LOD + b < 1, i.e. below this many RT texels per rendered eye pixel.
        float level0Returns = Mathf.Pow(2f, 1f - live);
        float minification = e.AuthoredPerRenderedPx;
        string minified = e.SamplingMeasured <= 0
            ? "not measured yet on this window (no completed sampling measurement since engage — the "
              + "bridge is derived once per 10 s state line and once per release)"
            : minification > 1f
                ? $"MINIFIED {minification:F2}x (peak since engage {e.AuthoredPerRenderedPxMax:F2}x, "
                  + $"{e.MinifiedReadings} of {e.SamplingMeasured} measurement(s) minified at all), so "
                  + $"the eye receives {100f / Mathf.Max(minification, 1e-4f):F0} % of the authored "
                  + "resolution and THIS is the regime the capture factor cannot reach"
                : $"MAGNIFIED (every authored px covers {1f / Mathf.Max(minification, 1e-4f):F2} "
                  + "rendered eye px), so level 0 is already the correct level here and the offset "
                  + "buys nothing on this reading — read the peak figure, not this one";

        VRLog.Info(Scope,
            $"PANEL SUPERSAMPLE MIP LOD OFFSET '{e.Window}': asked {asked:F2} mip level(s) from "
            + $"[WorldUI] PanelMipLodOffset (shipped default {Defaults.PanelMipLodOffset:F2}, range "
            + $"{MipLodOffsetMin:F2}..{MipLodOffsetMax:F2}), and the LIVE display render target reads "
            + $"{live:F2} back"
            + (Mathf.Abs(live - asked) > MipLodOffsetEpsilon
                ? " — THESE TWO DISAGREE, so the dial is NOT in force and every judgement of window "
                  + "sharpness from this session is a judgement of ModBuild 202's filtering"
                : " — asked and in force AGREE, so the dial is running")
            + (mipped
                ? $" (mip chain present, {shown!.mipmapCount} level(s), {shown.filterMode} aniso "
                  + $"{shown.anisoLevel})"
                : " — BUT THE SHOWN TARGET HAS NO MIP CHAIN (the mipped display target was refused; "
                  + "the quad is showing the capture target directly), and a mip LOD offset on a "
                  + "texture with one level is INERT: this dial is doing nothing on this window "
                  + "whatever the number says")
            + $". THE WINDOW RIGHT NOW: {minified}. WHAT THE NUMBER BUYS AND COSTS, as one identity: "
            + "trilinear selects LOD = log2(RT texels per rendered eye px), i.e. the mip level with "
            + "about one texel per pixel, and a bias b shifts that choice to LOD + b — so the level "
            + $"actually sampled carries 2^-b = {sampledTexelsPerPx:F2} texels per rendered pixel, "
            + "REGARDLESS of how minified the window is. Above 1.00 that is detail the pixel grid "
            + "cannot carry, which is aliasing bought back: it is invisible while the window is "
            + "still (the sub-texel phase is constant) and crawls while the window is carried (the "
            + "phase sweeps every frame) — the exact symptom ModBuild 198-202 removed. It also puts "
            + $"unfiltered level 0 back into the blend below {level0Returns:F2} RT texels per rendered "
            + $"eye px (this window is at {e.TexelsPerRenderedPx:F2} on its last measurement, "
            + $"{e.Level0Readings} of {e.SamplingMeasured} measurement(s) have read level 0 on a "
            + "MINIFIED window since engage). HOW TO READ IT: compare the ASKED and LIVE numbers "
            + "first — if they differ, nothing below this sentence is about this build. Then read "
            + "the window's minification: the offset is worth something exactly where the window is "
            + "MINIFIED, because there the sampler is already reading a level at or below authored "
            + "resolution and no capture factor can put that back; where the window is MAGNIFIED it "
            + "buys nothing and only costs. THEN JUDGE THE PICTURE, NOT THIS LINE: if the sub-menu "
            + "text reads sharper and does NOT crawl while you carry the window, the offset is "
            + "paying for itself. THE NUMBER TO TURN IT BACK AT IS -1.00: there the sampled level "
            + "carries 2.00 texels per rendered pixel, exactly twice what the eye can resolve, and "
            + "the crawl returns by construction rather than by taste. If text crawls at the shipped "
            + "-0.50, halve it to -0.25 or set it to 0 — 0 is bit-for-bit ModBuild 202's filtering "
            + "and costs nothing but the sharpness. This is a LOCAL rendering dial: no peer's board "
            + "changes by so much as a pixel, so it needs no wire field.");
    }

    private static Entry? EntryForCamera(Camera cam)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (ReferenceEquals(Entries[i].Cam, cam))
                return Entries[i];
        }
        return null;
    }

    private static Entry? EntryForWindow(string window)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (string.Equals(Entries[i].Window, window, System.StringComparison.Ordinal))
                return Entries[i];
        }
        return null;
    }

    private static void RestoreMaskedCameras()
    {
        if (MaskedCameras.Count == 0)
            return;
        foreach (KeyValuePair<Camera, int> pair in MaskedCameras)
        {
            if (pair.Key != null)
                pair.Key.cullingMask = pair.Value;
        }
        MaskedCameras.Clear();
    }

    // ---- stand-down ---------------------------------------------------------------------------

    private static void StandDownAll(string why)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
            StandDown(Entries[i], i, why);
        RestoreMaskedCameras();
        UninstallHooks();
        _capLogged = false;
    }

    private static void StandDown(Entry e, int index, string why)
    {
        RestoreLayers(e);   // must run BEFORE the layer goes back in the pool: it reads e.Layer
        MipBiasNextReport.Remove(e.Window); // a re-engaged window reports its bias immediately
        // ModBuild 204: the target-life record dies with the entry, for the same reason. Its counters
        // are all "since engage" and a re-engaged window is a new engagement — carrying them over
        // would make the FILL LATENCY distribution and the trigger census describe two lifetimes.
        Lives.Remove(e.Window);
        // ModBuild 205: the ink census's state dies with the entry too. A readback still in flight
        // then finds no record and discards itself (see OnInkRead), which is the correct outcome: its
        // texel coordinates describe a target that has just been released.
        // ModBuild 208: the budget is charged ONCE PER CENSUS, so it has to be handed back HERE and
        // not in the callback — a window that dies with four planes outstanding would otherwise leak
        // it and, at MaxInkCensusesInFlight = 1, wedge the instrument shut for the whole session.
        ReleaseInkCensus(e.Window);
        int layer = e.Layer;
        ReleaseLayer(layer);
        e.Layer = -1;
        DestroyEntryObjects(e);
        _vramTotal -= e.VramBytes;
        if (_vramTotal < 0)
            _vramTotal = 0;
        if (index >= 0 && index < Entries.Count && ReferenceEquals(Entries[index], e))
            Entries.RemoveAt(index);
        else
            Entries.Remove(e);
        _capLogged = false;
        // Budget just freed up: every window refused for cost reasons deserves one fresh look.
        // (Refusals are latched at all only so a permanent refusal cannot flood the log per frame.)
        Refused.Clear();
        if (Entries.Count == 0)
        {
            RestoreMaskedCameras();
            UninstallHooks();
        }
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE stood down on '{e.Window}' ({why}): the window's canvas "
                          + "is back on its original layer and is drawn straight into the eye again, "
                          + $"and {Mb(e.VramBytes)} MB of render target was released (session total "
                          + $"now {Mb(_vramTotal)} MB). Its private capture layer {layer} went back "
                          + $"into the pool ({FreeLayers.Count} of {PoolSize} free), so a window that "
                          + "was refused for want of one can now be re-considered. Input was never "
                          + "affected either way.");
    }

    private static void DestroyEntryObjects(Entry e)
    {
        if (e.Cam != null)
            e.Cam.targetTexture = null;
        if (e.CamGo != null)
            Object.Destroy(e.CamGo);
        if (e.DisplayGo != null)
            Object.Destroy(e.DisplayGo);
        if (e.Rt != null)
        {
            e.Rt.Release();
            Object.Destroy(e.Rt);
        }
        if (e.MipRt != null)
        {
            e.MipRt.Release();
            Object.Destroy(e.MipRt);
        }
        e.CamGo = null!;
        e.Cam = null!;
        e.DisplayGo = null!;
        e.Rt = null!;
        e.MipRt = null!;
    }

    // =============================================================================================
    // THE PER-GLYPH INK CENSUS OF THE CAPTURED RENDER TARGET (ModBuild 205)
    // =============================================================================================
    //
    // WHY THIS EXISTS, AND WHY IT IS THE ONLY THING LEFT TO BUILD.
    //
    // Thirteen builds have measured the STATE around the missing glyphs and every single reading is
    // clean, on the newest hardware log as much as on the first: 1 text-source defect in ~3500 glyph
    // lookups and that one a legitimate U+200B; 0 SUBMITTED-MESH defects out of ~3900 glyph quads
    // (unwritten, zero-area, collapsed UV rect, outside the atlas, non-finite: all zero); 0 CULL FLAG
    // in every bucket on components and sub-meshes alike; the TMP parent -> sub-mesh cull invariant
    // reading "REPAIRED: 0 sub-mesh(es) since engage" with baseline 0, drag min 0 max 0 over 192
    // samples, at release 0 and settled 0; 0 sub-meshes on the wrong capture layer; content scale
    // 1.000 over hundreds of readings; 0 font atlas repacks; 0 captures before uGUI's canvas rebuild;
    // draw order, over-paint and foreign renderers all measured and all clean. THREE separate
    // root-cause hypotheses have been falsified by measurement in three consecutive builds, and both
    // prior audits ended on the same sentence: the instrument must move from state inspection to a
    // PER-ELEMENT READBACK OF THE CAPTURE RT. Nobody has ever looked at what is actually IN the image.
    //
    // WHAT THIS ANSWERS, IN ONE LINE PER COMPONENT: the mesh says there should be a glyph quad HERE —
    // is there ink in the captured texture at that place, or not?
    //
    //   * GLYPHS EMPTY IN THE CAPTURE  => the loss happens AT OR BEFORE rasterisation into our render
    //     target. The capture path or the game's submission is guilty and the next round works there.
    //   * GLYPHS PRESENT WITH INK, and the user still sees them missing => the capture is CORRECT and
    //     the loss is DOWNSTREAM: the resolve, the mip chain, the quad, or the eye. Thirteen builds of
    //     work on the capture content are exonerated in one line and the search moves to the display
    //     side.
    //
    // Either answer is worth the build, which is why every path below that CANNOT answer says so in as
    // many words rather than printing a zero. A census that did not run must never look like a census
    // that ran and found nothing — that mistake is most of why the last eight rounds were unreadable.
    //
    // ---------------------------------------------------------------------------------------------
    // ModBuild 208: THE CENSUS ANSWERED (2), AND THE ANSWER REDIRECTS THE WHOLE SEARCH.
    // ---------------------------------------------------------------------------------------------
    //
    // The ModBuild 207 session paired every verdict with its glyph fates and the pairing is clean:
    //
    //     MAPPING VERIFIED | 0 EMPTY                             x13 readings
    //     ARTEFACT         | 113 empty (alpha 0.035 / 0.160)     x9
    //     MIXED            | 30-50 empty                         x3
    //
    // Not ONE reading is both "components fully opaque, mapping verified" and has empty glyphs. The 13
    // clean readings report 139 of 139 and 384 of 384 — every glyph present — and every reading with
    // missing glyphs has at least one component at an effective alpha of 0.035, 0.160 or 0.400, named
    // by the chain walk. ComponentsHiddenByGroupOnly is 0, so the exclusion is not hiding anything
    // either. THE CAPTURED TEXTURE IS CORRECT AT MIP LEVEL 0, which is branch (2) above: the loss is
    // DOWNSTREAM of the capture, and the capture-content work of thirteen builds was aimed at the
    // wrong half.
    //
    // AND THAT IS EXACTLY WHERE THE INSTRUMENT STOPPED LOOKING. The ModBuild 205-207 census read mip
    // level 0 and nothing else, while the SAME log line reports this window at trilinear MIP LOD 1.66
    // — the hardware samples levels 1 and 2 almost exclusively, blended. So the census has proven
    // level 0 correct and has said NOTHING WHATSOEVER about the levels the player actually sees.
    //
    // There is history that makes the mip chain the first place to look rather than a speculative one:
    // ModBuild 204 established that ClearRt's GL.Clear clears THE BOUND SURFACE, i.e. level 0 only,
    // and that levels 1..N of a fresh display target held uninitialised VRAM until the first
    // GenerateMips(). That specific case was fixed (mips generated at creation, the new pair primed
    // before Reallocate returns). Nobody has ever verified that the chain is correct on an ORDINARY
    // frame. This build does, and it also reads the CAPTURE target so the resolve blit is isolated:
    //
    //     plane 0  e.Rt     mip 0   — what the capture camera rasterised, BEFORE Graphics.Blit
    //     plane 1  e.MipRt  mip 0   — after the blit; what ModBuild 205-207 measured, and only this
    //     plane 2  e.MipRt  mip 1   — the eye reads here
    //     plane 3  e.MipRt  mip 2   — and here
    //
    // THE TRAP IN THIS MEASUREMENT, and it has to be handled or the new numbers are worthless: a glyph
    // correctly MINIFIED is not a defect. A stroke two texels wide at level 0 is half a texel at level
    // 2 and legitimately averages away. So every level applies a SIZE FLOOR
    // (<see cref="InkMinQuadTexels"/>), the glyphs under it are counted as BELOW THE FLOOR rather than
    // as EMPTY, and the finding is never an absolute count at one level — it is the COMPARISON
    // BETWEEN levels: "inked at mip 0, EMPTY at mip N despite being W x H texels there".

    /// <summary>
    /// <b>THE READBACK BUDGET, in texels, and it is what sizes the census STRIP.</b> 2,000,000 texels
    /// = 8.0 MB at RGBA32, requested asynchronously and at most once every
    /// <see cref="ReportIntervalSeconds"/> per window plus twice per release.
    /// <para>A blocking <c>ReadPixels</c> of this window's whole 4040x2992 target during a drag would
    /// cost 48 MB and a full pipeline stall on the frame the user is complaining about, i.e. the
    /// instrument would manufacture the symptom it is measuring. <see cref="AsyncGPUReadback"/> never
    /// stalls the GPU and the result arrives some frames later, which is handled explicitly below.</para>
    /// <para><b>ModBuild 208 spends this budget FOUR TIMES on one census</b> — see the plane table in
    /// the header. Levels 1 and 2 are a quarter and a sixteenth of the area, so the total is
    /// 2.0 + 2.0 + 0.5 + 0.125 = 4.625 Mtexel = 17.6 MB of readback staging for one census, against
    /// the 15.3 MB two concurrent single-plane censuses cost before (both figures in the MiB the COST
    /// line prints). <see cref="MaxInkCensusesInFlight"/> is therefore ONE, and it now counts CENSUSES
    /// rather than requests; the peak rises by 15 %, which is the whole cost of the change and is
    /// stated here rather than discovered.</para>
    /// </summary>
    private const int MaxInkCensusTexels = 2_000_000;

    /// <summary>
    /// <b>THE CENSUS REGION IS A FULL-HEIGHT VERTICAL STRIP, AND THAT IS A CORRECTNESS DECISION, NOT
    /// A CONVENIENCE ONE.</b>
    ///
    /// <para><c>AsyncGPUReadback</c> returns the data in the SOURCE TEXTURE'S OWN LAYOUT and Unity does
    /// not flip it, so whether row 0 of the returned buffer is the BOTTOM or the TOP row of the image
    /// is a graphics-API property, not something this file may assume. Worse, the same ambiguity
    /// applies to the <c>y</c> argument of a sub-rect request: a region asked for at <c>y = 500</c>
    /// under a top-down convention is a DIFFERENT part of the window than under a bottom-up one, and
    /// no amount of flipping the returned rows can repair having read the wrong band of the image.
    /// This project has already shipped a probe that was INVERTED against its own doc comment, named a
    /// wrong root cause with confidence and cost two builds ("verify the instrument first").</para>
    ///
    /// <para><b>A FULL-HEIGHT STRIP IS IDENTICAL UNDER BOTH CONVENTIONS</b> — <c>y = 0</c>,
    /// <c>height = RtH</c> selects the whole image either way — so the PLACEMENT ambiguity is gone by
    /// construction and only the ROW ORDER of the returned buffer is left. That is decided per census,
    /// from the data itself, by <see cref="InkOrientation"/>: the mesh predicts where the ink bands
    /// are, and the correlation of that prediction against the measured band profile as-is and
    /// reversed says which way up the buffer is. If neither wins clearly the census REFUSES to report
    /// EMPTY counts and says which check failed.</para>
    ///
    /// <para>The price is that only <see cref="MaxInkCensusTexels"/> / RtH texels of WIDTH fit — about
    /// 668 on this window's 2992-texel-tall target, i.e. ~334 authored px at the shipped factor 2.
    /// Glyphs outside the strip are COUNTED and named as excluded, never silently dropped.</para>
    ///
    /// <para><b>AND UNTIL ModBuild 209 THE STRIP NEVER MOVED, WHICH IS THE GAP THAT DOMINATED
    /// EVERYTHING ELSE THE INSTRUMENT SAID.</b> See <see cref="MaxInkBands"/>.</para>
    /// </summary>
    private const int MinInkStripTexels = 64;

    /// <summary>
    /// <b>THE STRIP ROAMS (ModBuild 209). THE 208 SESSION PRINTED "strip 668x2992 at x=2932" 142 TIMES
    /// AND x NEVER CHANGED ONCE.</b>
    ///
    /// <para>668 of 4040 texels is 16.5 % of the window's width, and x=2932 puts it over authored
    /// x ~1466..1800 of a 2020 px frame — the RIGHT-HAND EDGE. The damage in the user's photograph
    /// (<c>kaputt.jpg</c>: 'SÖLDNER' drawn as 'LDNER', 'Reich Ratsch' as 'R tch Ratsch', 'Scream' as
    /// 'Sc e') is in the MIDDLE COLUMN, at roughly authored x 600..820. Every "the capture is correct"
    /// verdict this instrument has ever produced was therefore correct ABOUT A BAND THAT DOES NOT
    /// CONTAIN THE DEFECT. That is not a competing hypothesis; it is missing coverage, and no amount
    /// of sharpening the judgement inside one fixed strip could have found it.</para>
    ///
    /// <para><b>WHY x NEVER MOVED.</b> ModBuild 205 centred the strip on the component with the most
    /// glyph quads. That component is a property of the WINDOW, not of the census, so a settled window
    /// re-elects the same seed every single time — the rule was deterministic, and deterministic in
    /// exactly the way that guarantees a blind spot rather than exposing one.</para>
    ///
    /// <para><b>THE FIX IS COVERAGE OVER THE SEQUENCE, NOT A BIGGER REQUEST.</b> The frame width is
    /// tiled into BANDS of one strip width each, a per-window cursor walks them, and successive
    /// censuses take successive bands until the whole width has been seen, then wrap. The readback
    /// budget per census is untouched — <see cref="MaxInkCensusTexels"/> still bounds one request and
    /// the same 6 ms per-frame pool still bounds the delivery — so this changes WHERE a census reads
    /// and not HOW MUCH, which is the one property that must not regress.</para>
    ///
    /// <para><b>THE BANDS OVERLAP BY HALF A STRIP, AND ABUTTING THEM WOULD HAVE BUILT A FRESH BLIND
    /// SPOT INTO THE FIX.</b> A glyph is censused only when its quad is WHOLLY inside the strip (a
    /// half-clipped quad has no honest verdict and this file refuses to invent one), so under an
    /// abutting tiling a label straddling a boundary would be excluded from BOTH neighbours and no
    /// census could ever see it. On the very window this change is about that is not hypothetical: the
    /// damaged middle column sits at authored x 600..820, i.e. texels 1200..1640 at this window's rate
    /// of 2.0, and an abutting tiling of 668-texel bands puts a boundary at 1336 — straight through it.
    /// The stride is therefore <c>stripW / 2</c>: consecutive strips overlap by half their width, and
    /// any run of ink up to half a strip wide (334 texels, 167 authored px here) is GUARANTEED to fall
    /// wholly inside at least one band. The price is twice as many bands to walk, paid out of a session
    /// that produced 142 censuses.</para>
    ///
    /// <para><b>A BAND WITH NO GLYPH QUADS IS SKIPPED, A BAND THAT CAME BACK CLEAN IS NOT.</b> The
    /// candidate walk builds its glyph list over the WHOLE frame before the band is chosen, so the
    /// per-band content histogram is free and a census is never spent on empty margin. Skipping a band
    /// because a PREVIOUS pass found it clean would be a different thing entirely and is forbidden:
    /// the complaint is that the picture freezes and breaks intermittently, so a defect that comes and
    /// goes must keep being re-sampled. The skipped-as-empty count is printed.</para>
    ///
    /// <para><b>ROW ORDER IS NOT A PROPERTY OF THE BAND.</b> The orientation self-check decides once
    /// per window (ModBuild 208) and a roaming strip must not invalidate that decision — it does not:
    /// row order is a property of the GRAPHICS API and the TEXTURE LAYOUT, identical for every sub-rect
    /// of the same texture, and the sub-rect this file asks for is full-height at y=0 under every band
    /// (see above), so the placement ambiguity the full-height rule removes stays removed no matter
    /// what x is. The cheap 2,500-sample confirmation keeps running per plane per census and still
    /// refuses the census outright if it ever decides the other way; it correlates the mesh prediction
    /// and the measurement OVER THE SAME BAND, so both sides move together.</para>
    ///
    /// <para>128 bands is the ceiling and it is derived rather than picked: <see cref="MaxRtDimension"/>
    /// is 4096, the narrowest strip is <see cref="MinInkStripTexels"/> = 64 and the stride is half of
    /// that, so <c>(4096 - 64) / 32 + 1 = 127</c> positions is the finest the tiling can ever get.
    /// The window this round is about needs 12.</para>
    /// </summary>
    private const int MaxInkBands = 128;

    /// <summary>Text components the candidate walk will consider at all. The party window carries up
    /// to 297; this bounds the build cost and the surplus is reported as skipped.</summary>
    private const int MaxInkCandidateComponents = 64;

    /// <summary>Glyph quads recorded per candidate component, and in total across one census. 384
    /// glyphs x ~96 texel samples each is ~37k texel reads, i.e. well under a millisecond, and the
    /// measured cost is printed on the line so this can be revisited with a number.</summary>
    private const int MaxInkGlyphsPerComponent = 96;
    private const int MaxInkCensusGlyphs = 384;

    /// <summary>How many components the line names in full, worst (most EMPTY) first. The totals are
    /// always printed with their denominators; only the sentences are capped.</summary>
    private const int MaxInkComponentsReported = 6;

    /// <summary>How many EMPTY characters one component names. Beyond this the line says how many more
    /// there were — the count is never truncated, only the character list.</summary>
    private const int MaxInkEmptyNamed = 24;

    /// <summary>Frames after the RELEASE-EDGE census at which the SETTLED census is taken. THIRTY,
    /// which is <see cref="SweepAfterMotionFrames"/> and <see cref="SubMeshCullSettleFrames"/> — the
    /// same window this class already calls "moving, or settling from a move" — so the settled reading
    /// is by construction the first one taken outside the drag. The user reports the picture FREEZING
    /// on release, so "at the release edge" and "a third of a second later" are the two readings that
    /// decide whether the broken image is transient or latched.</summary>
    private const int InkSettleFrames = 30;

    /// <summary>
    /// <b>THE INK THRESHOLD: how far a texel inside a glyph quad must deviate from that quad's OWN
    /// LOCAL BACKGROUND before it counts as ink.</b> 16/255 = 6.3 % of full range.
    ///
    /// <para><b>WHY NOT THE ALPHA CHANNEL, even though the capture clears to transparent black.</b>
    /// The capture camera clears to <c>(0,0,0,0)</c> (see <see cref="BuildCamera"/>), so on bare
    /// background alpha IS the cleanest signal — but this window's text is drawn ON TOP OF opaque dark
    /// plates, where alpha reads 1.0 both on the glyph and on the plate beside it. Alpha alone would
    /// call every glyph on a plate "ink" and the census would answer PRESENT for a photograph full of
    /// holes. The measurement therefore uses PREMULTIPLIED LUMINANCE — <c>lum x alpha</c> — which
    /// degenerates to alpha over the transparent clear (background 0) and to plain luminance over an
    /// opaque plate, and is signed-agnostic because the test is on the ABSOLUTE deviation from the
    /// local background. Light text on dark and dark text on light are both caught.</para>
    ///
    /// <para><b>WHY THIS NUMBER, measured rather than picked.</b> ModBuild 196 measured the
    /// photograph's own pixels: where the missing <i>d h e</i> of "Gesundheit" belong the peak
    /// luminance is 24 against a 20 background and a 147 ink — i.e. a genuine gap deviates by 4/255
    /// and a genuine glyph by 127/255. 16/255 sits an order of magnitude below the ink and four times
    /// above the gap. <b>AND IT IS NOT TAKEN ON TRUST:</b> the report prints the threshold AND the
    /// median deviation of the "with ink" set next to the median of the "EMPTY" set, so a wrong
    /// choice is visible on the line instead of silently deciding the build.</para>
    /// </summary>
    private const float InkThreshold = 16f / 255f;

    /// <summary>How far a glyph quad is shrunk before its interior is sampled, per side. A TMP glyph
    /// quad carries the SDF padding of its atlas entry, so its outer 10-20 % is background by
    /// construction on nearly every glyph; sampling it would put background inside the "interior" set
    /// and pull the maximum deviation down for thin glyphs only. 0.15 is inside the padding of this
    /// game's Sarala-Regular SDF atlas and still leaves the full stem of an 'l' or an 'i'.</summary>
    private const float InkQuadInset = 0.15f;

    /// <summary>Interior samples per axis inside one glyph quad (so up to 144 per glyph), and the
    /// width in texels of the ring just OUTSIDE the quad from which the local background is taken.
    /// <para>TWELVE, raised from eight in ModBuild 206, and the reason is the INK CENTROID: at 8x8 a
    /// glyph quad ~25x40 texels wide is sampled every 3x5 texels and the centroid it yields cannot
    /// resolve the sub-advance displacement the mapping self-check exists to measure. At 12 the grid
    /// is ~2x3 texels and the centroid is good to about a texel, which is a fifth of the smallest
    /// displacement that could produce the observed pattern.</para></summary>
    private const int InkInnerSamples = 12;
    private const int InkRingBandTexels = 2;

    // ---- THE MAPPING SELF-CHECK (ModBuild 206) --------------------------------------------------
    //
    // WHY IT EXISTS. ModBuild 205's census answered, and the SHAPE of the answer is what forced this.
    // The same component — 'Quest freischalten', 17 glyphs, ONE mesh, ONE draw call — came back
    // across one session as 0/17 EMPTY, 3/14, 3/14 with a DIFFERENT subset, 7/10, 9/8, 12/5, 16/1,
    // and 33 readings of 139-of-139 with nothing empty at all. YOU CANNOT RASTERISE HALF A MESH: a
    // varying, spatially scattered subset of quads out of one submitted mesh cannot be produced by a
    // rasteriser dropping some quads and keeping others.
    //
    // WHAT CAN PRODUCE EXACTLY THAT is a POSITIONAL DISAGREEMENT between where this census predicts a
    // quad and where the ink actually is. Displace the whole string by a fraction of a glyph advance
    // and some predicted quads land on ink and others land in the gaps BETWEEN letters — pseudo-random
    // along the string, a different subset for every different offset, 0-of-17 for a large offset and
    // 16-of-17 for a small one. That fits every reading including the perfect ones (offset ~ 0).
    //
    // AND THE CENSUS COULD NOT TELL THAT APART FROM A GENUINELY DISPLACED CAPTURE, which would itself
    // be the bug. So it measures the displacement instead of assuming it is zero, and says which.
    // Two independent estimates are taken, deliberately, because either alone is arguable:
    //
    //   (1) THE INK CENTROID, over the glyphs judged INKED. Sub-texel, cheap, and it needs no search
    //       — but a single glyph's ink is not centred in its own quad ('j' sits low and left, 'T' is
    //       top-heavy), so only the MEAN over many glyphs is meaningful and the report prints the
    //       whole distribution rather than one number.
    //   (2) THE JOINT REGISTRATION SEARCH, per COMPONENT. One offset is fitted to ALL of a
    //       component's glyphs at once — the offset at which the most of them pass the ink test —
    //       because a whole-string displacement is ONE number for the string, not one per glyph.
    //       Fitting per glyph would be meaningless: in running text, shifting a single glyph by one
    //       advance lands it on its NEIGHBOUR, which is also ink, so every glyph would report a
    //       spurious match. Fitting the string as a whole is what breaks that degeneracy — and where
    //       it does not (a best offset a full advance away is a LATTICE ALIAS, not a finding) the
    //       report says so instead of claiming a displacement.

    /// <summary>
    /// <b>THE SEARCH WINDOW, IN THE COMPONENT'S OWN UNITS — and ModBuild 206's was too small.</b>
    /// FOUR glyph advances either side in X and THREE line heights either side in Y, against 206's one
    /// and one.
    ///
    /// <para><b>WHAT THE 206 LOG SHOWED.</b> Almost every EMPTY glyph came back AMBIGUOUS with the
    /// reason <i>"the best offset sits ON the search-window boundary"</i> — 'Party Name', 'XP Amount
    /// Levelup', 'XP Amount', 'Level text', 21 of 21, 19 of 19, 130 of 130. A boundary argmax is what
    /// you get from a window that is too small AND, far more often here, from a FLAT field: if there is
    /// no ink anywhere in the window the score is the same everywhere and the argmax lands wherever the
    /// scan order left it, very often an edge. 206 read both of those as "the true match may lie
    /// outside", which made GENUINELY ABSENT nearly unreachable — the instrument could not deliver the
    /// verdict it exists for. <see cref="InkFlatFieldFraction"/> is the other half of that fix.</para>
    /// </summary>
    private const float InkSearchSpanAdvances = 4f;
    private const float InkSearchSpanLines = 3f;

    /// <summary>
    /// <b>THE SEARCH IS COARSE-TO-FINE, because a wide window and a sub-advance resolution cannot both
    /// come out of one grid.</b> Stage 1 sweeps the whole window at half-advance steps; stage 2 refines
    /// inside one stage-1 cell at about an eighth of an advance. Widening 206's single 13x13 grid to
    /// four advances without this would have made its step 0.67 of an advance — coarser than the
    /// displacement being hunted, i.e. a wider window that could no longer see the thing it was widened
    /// for.
    /// <para>Stage 2 is SKIPPED outright when stage 1's field is flat (see
    /// <see cref="InkFlatFieldFraction"/>), which is the common case for a fully dark component and is
    /// what keeps the cost of the wider window off the frame.</para>
    /// </summary>
    private const int InkCoarseSteps = 17;
    private const int InkFineSteps = 9;

    /// <summary>Interior samples per axis used INSIDE the search (so 16 per glyph per offset, against
    /// <see cref="InkInnerSamples"/>'s 144 for the verdict itself). The search only has to FIND where
    /// the ink is — a whole-string fit aggregates over many glyphs, so a missed stem here and there
    /// does not move it — and the final per-glyph judgement at the fitted offset then re-tests on the
    /// full grid, so FOUND/ABSENT is measured to the same standard as INKED/EMPTY.</summary>
    private const int InkSearchCoarseSamples = 4;

    /// <summary>
    /// <b>THE FLAT-FIELD BAR: below this fraction of a component's glyphs passing at the BEST offset in
    /// the whole window, the field has no peak at all and the component is GENUINELY ABSENT — whatever
    /// the argmax did.</b> 0.25.
    ///
    /// <para>This is the fix for 206's central failure. A flat field means "there is no ink anywhere
    /// within four advances and three lines of where the mesh says this text is", which is the
    /// strongest possible form of ABSENT — and 206 classified exactly that as AMBIGUOUS because the
    /// arbitrary argmax of a flat field usually lands on an edge. The bar is expressed in the
    /// component's OWN glyph count rather than as an absolute, because a 3-glyph label and a 40-glyph
    /// sentence cannot share one number: at 0.25 a 17-glyph string needs 5 of its glyphs to find ink
    /// somewhere in the window before the field counts as having a peak worth chasing.</para>
    /// </summary>
    private const float InkFlatFieldFraction = 0.25f;

    /// <summary>
    /// How much better than the ZERO offset a boundary fit must score before it is believed to be a
    /// real peak being cut off by the window — as a fraction of the component's glyph count. Below it,
    /// a boundary argmax is noise and the component falls through to the flat-field or the no-fit
    /// branch instead of being written off as ambiguous. ModBuild 206 had no such test: ANY boundary
    /// argmax disqualified the component.
    /// </summary>
    private const float InkMaterialGainFraction = 0.15f;

    /// <summary>
    /// <b>A DIM CAPTURE IS NOT MISSING GLYPHS, AND THE VERDICT MUST NEVER CONFLATE THE TWO.</b> The
    /// INKED median must fall below this AND the strip background must rise above
    /// <see cref="InkDimBackground"/> before a reading is called dim.
    ///
    /// <para>Sized against the ModBuild 206 log's own distribution rather than against its outlier —
    /// which is the point. ONE reading of that session carried <c>INKED median 39.6/255, background
    /// 16.5/255</c> with 130 EMPTY, and it is tempting to promote that to "the whole capture is fading
    /// out". THE OTHER BROKEN READINGS DO NOT AGREE: they sit at INKED 195-208 and background ~8,
    /// indistinguishable from the clean ones. So the dim case is real and it is ONE outlier, not the
    /// family — this project has made the promote-one-extreme-to-the-operating-point mistake three
    /// times and this constant exists so the next reader does not have to dig the numbers out to avoid
    /// the fourth. 96/255 sits between the outlier's 39.6 and the family's 195; 12/255 sits between the
    /// family's ~8 and the outlier's 16.5.</para>
    /// </summary>
    private const float InkDimInkMedian = 96f / 255f;
    private const float InkDimBackground = 12f / 255f;

    /// <summary>Effective alpha at or below which a component is treated as NOT DRAWN. Matches
    /// <c>NoteRendererState</c>'s own bar exactly, so this census and the ModBuild 204 split cannot
    /// disagree about where the line is — only, if they disagree at all, about what they measured.</summary>
    private const float InkAlphaFloor = 0.004f;

    /// <summary>Effective alpha at or above which a component counts as FULLY OPAQUE, i.e. one whose
    /// missing ink is a defect and not a design choice. 0.99 and not 1.0 so a float product of four
    /// values that are each exactly 1.0 cannot fall out of its own class on rounding.</summary>
    private const float InkOpaqueBar = 0.99f;

    /// <summary>CanvasGroups the alpha-chain walk will name in the report per component. The PRODUCT is
    /// always exact; only the naming is capped.</summary>
    private const int MaxInkGroupsNamed = 4;

    /// <summary>
    /// Components whose full alpha evidence the line prints. The counts are always complete.
    /// <para>ModBuild 210 raised it from 4. At 4 the ModBuild 209 log produced ELEVEN readings whose
    /// verdict was ARTEFACT — a verdict that fires on the LOWEST effective alpha among the components
    /// that produced EMPTY glyphs — while the evidence field named no component below 1.000 at all,
    /// because the component that triggered it sat past the naming cap. A field whose stated job is
    /// "the ALPHA EVIDENCE field names them" must name the one that decided the verdict; from 210 that
    /// component is named FIRST and unconditionally, and this cap only bounds the rest.</para>
    /// </summary>
    private const int MaxInkAlphaNamed = 8;

    /// <summary>
    /// <b>Graphics the draw-state ledger will NAME per transition report, and how many it tracks.</b>
    /// The transition COUNTS are always complete; only the naming is capped. 4096 tracked entries at
    /// roughly 48 bytes is under 200 kB for a window whose subtree is ~2700 transforms, and the ledger
    /// is per panel and cleared at release, so the bound is a backstop and not a real limit.
    /// </summary>
    private const int MaxDrawStateNamed = 16;
    private const int MaxDrawStateTracked = 4096;

    /// <summary>Non-text graphics one census will sample the capture under, the grid it samples each of
    /// them on, and how many it will name. 24 plates at 6x6 is 864 texel reads — under one percent of
    /// what the glyph judging already spends, and it is charged to the same per-frame pool.</summary>
    /// <summary>Stale inherited alphas one census will repair. A bound, not a policy: if a window ever
    /// needs more than this the line says so and the number itself is the finding.</summary>
    private const int MaxInheritedRepairsPerCensus = 512;

    /// <summary>Graphics one phase sample re-reads, and how often a frame is sampled. 256 of ~900 is
    /// a subset by design: a set that FLIPS shows up in any fair subset, and the sample has to be
    /// cheap enough to run at EVERY camera's onPreCull without moving the frame time it is measuring.
    /// Every 4th frame keeps the cost under a tenth of a percent of the budget.</summary>
    private const int MaxPhaseGraphics = 256;
    private const int PhaseSampleEveryFrames = 4;

    private const int MaxInkPlates = 24;
    private const int InkPlateSamples = 6;
    private const int MaxInkPlatesNamed = 8;

    /// <summary>A plate smaller than this on either axis is not sampled: a 6x6 grid inside a few texels
    /// reads its own edges, and an edge is not evidence about the middle.</summary>
    private const float MinInkPlateTexels = 12f;

    /// <summary>Censuses an unseen ledger entry survives before it is evicted. A graphic that leaves the
    /// hierarchy must NOT keep printing as "lost" for ever — that would turn a sub-view switch, which is
    /// the user closing a tab, into a permanent finding. Two, so a single skipped census cannot evict.</summary>
    private const int DrawLedgerEvictAfter = 2;

    /// <summary>Glyphs whose component may be given a joint registration search in one census. Beyond
    /// this the remaining components' EMPTY glyphs are reported as NOT SEARCHED rather than as
    /// genuinely absent — a cap must never be able to manufacture the more alarming verdict.</summary>
    private const int MaxInkSearchGlyphs = 384;

    /// <summary>
    /// A fitted offset counts as a genuine SUB-ADVANCE displacement only inside this fraction of one
    /// glyph advance in X and one line height in Y. Beyond it the offset is a LATTICE ALIAS — the
    /// string re-registered onto its own neighbouring letters or its neighbouring line, which any
    /// is-there-ink test is degenerate against in running text — and the component's EMPTY glyphs are
    /// reported AMBIGUOUS instead of FOUND OFFSET. 0.6, i.e. comfortably inside half an advance.
    /// </summary>
    private const float InkSubAdvanceFraction = 0.6f;

    /// <summary>Mean ink-centroid displacement, in texels, at or below which the mapping is called
    /// VERIFIED. 1.5 texels is 0.6 authored px at the shipped factor 2.5 — below the sub-pixel
    /// registration this whole path is built on, and far below the several-texel offset that would be
    /// needed to move a glyph quad off its own ink.</summary>
    private const float InkMappingVerifiedTexels = 1.5f;

    /// <summary>Fraction of the classified EMPTY glyphs one bucket must hold before the verdict names
    /// it outright rather than reporting MIXED.</summary>
    private const float InkVerdictMajority = 0.6f;

    /// <summary>Bands the orientation self-check splits the strip into, the stride at which the
    /// measured band profile is sampled, and the two bars the verdict must clear: an absolute
    /// correlation and a margin over the reversed reading. Below either, the orientation is UNDECIDED
    /// and the census reports NOT ANSWERABLE rather than guessing which way up the buffer is.</summary>
    private const int InkBands = 24;
    private const int InkProfileStride = 4;
    private const float InkOrientMinCorrelation = 0.35f;
    private const float InkOrientMinMargin = 0.15f;

    /// <summary>Transforms one candidate walk may visit. The party window's subtree is ~2700, so this
    /// is a hard bound rather than a real limit; when it bites the line says so.</summary>
    private const int MaxInkWalkTransforms = 4096;

    /// <summary>
    /// <b>CENSUSES IN FLIGHT ACROSS THE WHOLE MOD, and this bound is not decorative.</b> The report
    /// cadence is a single shared timer in <c>LateTick</c>, so EVERY engaged panel arms its census on
    /// the SAME frame — with <see cref="EffectiveMaxPanels"/> at seven and a census now costing 17.6 MB
    /// of readback staging across its four planes, an unbounded version would put 123 MB in flight on
    /// one frame, every ten seconds, forever. ONE census at a time caps it at 17.6 MB; the rest stay
    /// ARMED (not cancelled) and go out on the next frames, and every deferral is counted and printed
    /// so a census that slipped is visible rather than silent.
    /// <para>ModBuild 208 changed the UNIT from requests to censuses. A census issues all four of its
    /// planes ON ONE FRAME, and that is a correctness requirement rather than a convenience: the mip
    /// chain is regenerated from a fresh capture every single frame (see
    /// <see cref="ResolveAndMip"/>), so planes requested on different frames would compare DIFFERENT
    /// pictures and the whole "inked at mip 0, empty at mip 1" finding would be an artefact of the
    /// cadence. Splitting the four planes across frames to spread the cost is therefore not available,
    /// and the cost is bounded by <see cref="InkJudgeBudgetMs"/> and a cursor instead.</para>
    /// </summary>
    private const int MaxInkCensusesInFlight = 1;

    /// <summary>How many ink CENSUSES are currently in flight, across every panel. Incremented once
    /// when the planes go out and released exactly once by <see cref="EndInkCensus"/> — on the report
    /// path, on every unanswerable path, and when a window stands down with a census outstanding.
    /// Without that last one the budget would leak and the census would wedge itself shut for the
    /// session while every line still claimed it was armed.</summary>
    private static int _inkInFlight;

    // ---- THE MOD-WIDE PER-FRAME MILLISECOND POOL (ModBuild 208) ----------------------------------
    // Reset lazily on the first consultation of a new frame rather than from a tick, so it cannot be
    // wrong for a frame in which no tick ran and it needs no cooperation from any other lane.
    private static int _inkBudgetFrame = -1;
    private static double _inkBudgetSpentMs;

    /// <summary>Milliseconds of <see cref="InkFrameBudgetMs"/> still unspent on THIS frame, across
    /// every panel. Never negative.</summary>
    private static double InkBudgetRemainingMs()
    {
        if (_inkBudgetFrame != Time.frameCount)
        {
            _inkBudgetFrame = Time.frameCount;
            _inkBudgetSpentMs = 0.0;
        }
        return System.Math.Max(0.0, InkFrameBudgetMs - _inkBudgetSpentMs);
    }

    /// <summary>What ONE stage may take: its even share of what is left this frame, capped by
    /// <paramref name="ceiling"/> and floored by <see cref="InkMinStageBudgetMs"/>.</summary>
    private static double InkStageBudgetMs(int stagesStillToCome, double ceiling)
        => System.Math.Max(InkMinStageBudgetMs,
                           System.Math.Min(ceiling,
                                           InkBudgetRemainingMs()
                                           / System.Math.Max(1, stagesStillToCome + 1)));

    private static void ChargeInkBudget(double ms)
    {
        if (_inkBudgetFrame != Time.frameCount)
        {
            _inkBudgetFrame = Time.frameCount;
            _inkBudgetSpentMs = 0.0;
        }
        _inkBudgetSpentMs += ms;
    }

    // ---- THE FOUR PLANES ONE CENSUS READS (ModBuild 208) -----------------------------------------

    /// <summary>Plane 0: <see cref="Entry.Rt"/> at mip 0 — what the capture camera rasterised, BEFORE
    /// <c>Graphics.Blit</c>. Reading it is what isolates the resolve blit: identical here and at plane
    /// 1 clears the blit, different accuses it.</summary>
    private const int InkPlaneCapture = 0;

    /// <summary>Plane 1: the DISPLAY target at mip 0 — the only plane ModBuild 205-207 ever read.</summary>
    private const int InkPlaneMip0 = 1;

    /// <summary>Planes 2 and 3: the display target at mip 1 and mip 2 — <b>the levels the eye actually
    /// samples</b>. The same log line that carried the 207 census reports this window at trilinear MIP
    /// LOD 1.66, so the hardware reads levels 1 and 2 blended and level 0 essentially not at all.</summary>
    private const int InkPlaneMip1 = 2;
    private const int InkPlaneMip2 = 3;

    private const int InkPlanes = 4;

    /// <summary>The order the planes are REQUESTED in. Callbacks may land in any order, so nothing
    /// depends on this; it exists so the two full-resolution planes go out first and the cheap ones
    /// trail them, which keeps the peak staging shorter-lived.</summary>
    private static readonly int[] InkPlaneOrder =
        { InkPlaneCapture, InkPlaneMip0, InkPlaneMip1, InkPlaneMip2 };

    /// <summary>
    /// <b>THE SIZE FLOOR, and it is THE trap in a per-level census.</b> A glyph correctly MINIFIED is
    /// not a defect: a stroke two texels wide at level 0 is half a texel at level 2 and legitimately
    /// averages into its background. A census that counted those as EMPTY would report a broken mip
    /// chain on a perfect one.
    /// <para><b>THREE TEXELS ON THE SMALLER AXIS, and the bar is about the INSTRUMENT, not about the
    /// eye.</b> The interior of a quad is inset by <see cref="InkQuadInset"/> per side and sampled on
    /// an <see cref="InkInnerSamples"/> grid, and the local background is a ring
    /// <see cref="InkRingBandTexels"/> texels outside it. Under three texels the inset interior is a
    /// single texel wide, the sample grid re-reads that one texel 144 times, and the ring overlaps the
    /// quad's own ink — so the reading is decided by the instrument's geometry and not by the picture.
    /// That is the same bar <see cref="CollectInkCandidate"/> already applies at level 0 (it drops
    /// quads under two texels), raised by one texel because the ring contamination gets worse as
    /// neighbouring glyphs close in under minification.</para>
    /// <para><b>WHY THE FLOOR IS NOT A CONTRAST ARGUMENT.</b> Mip generation is a repeated 2x2 BOX
    /// filter, which is linear: a stroke covering a fraction f of a texel contributes f times its full
    /// deviation. Against a measured INKED median of ~200/255 and a <see cref="InkThreshold"/> of
    /// 16/255, a stroke survives down to f = 0.08 texels — so minification alone does NOT explain a
    /// glyph vanishing at level 1 or 2, and the report prints that arithmetic next to the counts so
    /// "it just averaged away" cannot be asserted without a number. The floor exists to protect the
    /// measurement, not to excuse the result.</para>
    /// </summary>
    private const float InkMinQuadTexels = 3f;

    /// <summary>
    /// <b>WHERE ModBuild 207's 46 MILLISECONDS ACTUALLY WENT, because the fix has to be aimed at the
    /// right thing.</b> Its COST line prices a texel read on this machine at roughly 140 ns
    /// (22.98 ms for a 139-glyph census, which is ~157,000 reads). Of those 157,000, the ORIENTATION
    /// BAND PROFILE is 125,000 — it swept the whole 2 Mtexel strip at a fixed
    /// <see cref="InkProfileStride"/> of 4. <b>The prologue was ~18 ms of the 23 and it ran before a
    /// single glyph was looked at.</b> The per-glyph work was ~4 ms.
    ///
    /// <para><b>SO THE PROFILE IS RUN ONCE PER WINDOW, NOT ONCE PER CENSUS.</b> Row order is a
    /// property of the graphics API and the texture layout — it is constant for the session, and
    /// re-deriving it every ten seconds bought nothing. The FIRST census on a window runs the full
    /// ModBuild 207 profile at the shipped stride and DECIDES; the guarantee is unchanged, and an
    /// undecided first reading still refuses to report EMPTY counts exactly as before. Every census
    /// after it runs a cheap CONFIRMATION at <see cref="InkConfirmProfileSamples"/> that never gates
    /// anything — it only speaks if it decides clearly and DISAGREES, which is a finding in its own
    /// right and is treated as one.</para>
    ///
    /// <para>2,500 samples is ~104 per <see cref="InkBands"/> band. That is deliberately too weak to
    /// gate a census and quite strong enough to catch a flipped buffer, which is a total inversion of
    /// the profile and not a marginal shift.</para>
    /// </summary>
    private const int InkConfirmProfileSamples = 2_500;

    /// <summary>
    /// <b>THE MOD-WIDE PER-FRAME BUDGET, and it is the one that actually binds.</b> A census issues all
    /// four planes on ONE frame (see <see cref="MaxInkCensusesInFlight"/> for why it must), and
    /// <c>AsyncGPUReadback</c> drains its completed-request queue per frame — so all four callbacks
    /// can, and usually will, land on the SAME frame. A per-plane budget alone would therefore have
    /// bounded a quarter of the problem and let four planes plus a search spend 14 ms on one frame.
    /// <para>SIX MILLISECONDS of an 11.11 ms frame, shared by every plane and the search of every
    /// panel. Each stage takes <c>remaining / (planes still outstanding + 1)</c>, clamped to
    /// <see cref="InkJudgeBudgetMs"/> and floored at <see cref="InkMinStageBudgetMs"/> so a late stage
    /// always makes some progress; planes that land on separate frames each see a fresh pool and take
    /// their full share.</para>
    /// </summary>
    private const double InkFrameBudgetMs = 6.0;

    /// <summary>The most any ONE plane's glyph loop may take, however much of the frame pool is free.
    /// Two milliseconds is ~50 glyphs at the 140 ns per texel read the 207 log implies and the whole
    /// census on any machine where a texel read costs what it ought to.</summary>
    private const double InkJudgeBudgetMs = 2.0;

    /// <summary>The neighbourhood search's own ceiling, spent after the judging on the mip 0 plane
    /// only, and drawn from the same frame pool. A per-COMPONENT cursor resumes an overrun.</summary>
    private const double InkSearchBudgetMs = 2.0;

    /// <summary>The floor under every stage's share of <see cref="InkFrameBudgetMs"/>. A stage that
    /// arrived after the pool was empty still judges a handful of glyphs rather than none, because a
    /// plane that measured NOTHING contributes nothing to the level comparison and would quietly
    /// shrink the denominator to zero. It is a floor and not a reservation: the overshoot is at most
    /// one stage's worth, which the COST line prints.</summary>
    private const double InkMinStageBudgetMs = 0.3;

    /// <summary>How often the judging and search loops consult the clock.
    /// <c>Time.realtimeSinceStartup</c> is a property call into the engine, so reading it per glyph
    /// would itself be a measurable share of the budget it is protecting.</summary>
    private const int InkBudgetCheckStride = 8;

    /// <summary>
    /// <b>GLYPHS ONE COMPONENT'S REGISTRATION SEARCH SCORES, and this is the other half of the cost
    /// fix.</b> The search sweeps a 17x17 coarse grid and a 9x9 refinement; at
    /// <see cref="MaxInkGlyphsPerComponent"/> = 96 glyphs and 16 samples each that is 444,000 texel
    /// reads for ONE component, which is where the 46 ms came from.
    /// <para>A whole-string fit AGGREGATES over glyphs — it asks at which offset the most of them find
    /// ink — so it does not need every glyph to locate the peak, and 24 is already a 24-sample
    /// majority test. The final per-glyph FOUND/ABSENT verdict at the fitted offset still re-tests
    /// EVERY empty glyph on the full <see cref="InkInnerSamples"/> grid, so nothing that is reported
    /// per glyph is measured to a lower standard. The flat-field and material-gain bars are taken
    /// against the SCORED count rather than the glyph count, and both numbers are printed.</para>
    /// </summary>
    private const int InkSearchMaxGlyphs = 24;

    /// <summary>How many "inked at mip 0, EMPTY at mip N" glyphs the line names with their characters
    /// and their size at that level. The COUNT is always complete.</summary>
    private const int MaxInkMipLostNamed = 16;

    /// <summary>One glyph quad, in RENDER-TARGET TEXEL space with y measured from the capture frame's
    /// BOTTOM edge — the same orientation the orthographic capture camera's viewport uses.</summary>
    private struct InkGlyph
    {
        internal int Comp;
        internal char Ch;
        internal float X0, Y0, X1, Y1;
    }

    /// <summary>One node of the candidate walk, carrying the CLIP RECT inherited from its ancestors
    /// (host-local uGUI px). See <see cref="InkCensus.GlyphsClipped"/> for why a census that ignored
    /// clipping would answer the whole investigation wrongly.</summary>
    private struct InkFrame
    {
        internal readonly Transform T;
        internal readonly Rect Clip;
        internal readonly bool ClipEmpty;

        internal InkFrame(Transform t, Rect clip, bool clipEmpty)
        {
            T = t;
            Clip = clip;
            ClipEmpty = clipEmpty;
        }
    }

    /// <summary>A candidate text component found by the walk, and the slice of
    /// <see cref="InkCandidates"/> that holds its glyph quads.</summary>
    private struct InkCandidateComp
    {
        internal string Name;
        internal string Text;
        internal int MeshGlyphs;
        internal int First;
        internal int Count;
        internal float CentreX;

        // ---- the alpha evidence (ModBuild 207) --------------------------------------------------
        internal float OwnAlpha;
        internal float CrAlpha;
        internal float InheritedAlpha;
        internal float GroupAlpha;
        internal string GroupNote;
    }

    /// <summary>One censused component and what the readback found for it.</summary>
    /// <summary>
    /// <b>ONE NON-TEXT GRAPHIC THAT BELIEVES IT DREW — and what the capture actually holds where it
    /// says it is.</b>
    ///
    /// <para>The draw-state ledger answers "did uGUI think this graphic drew". This answers the other
    /// half, and the two together are what the user's report needs: he sees images and symbols come and
    /// go, and a graphic that is DRAWN, opaque, unclipped and still absent from the texture is a
    /// completely different finding from one that was switched off. Nothing in eight builds could
    /// express either sentence about an Image, because the census population was TMP text.</para>
    ///
    /// <para><b>THE VERDICT IS DELIBERATELY ONE-SIDED, and that is the whole design.</b> A dark image on
    /// a dark plate is indistinguishable from a missing one by any is-there-ink test, so this must
    /// never be allowed to call something missing on thin evidence — this project has promoted an
    /// over-eager threshold to a root cause before. Only the unambiguous case is named: the sampled
    /// region is FLAT (no sample anywhere in it deviates from the strip background by
    /// <see cref="InkThreshold"/>). Everything else reports as "content present, not judged", which is
    /// an honest non-answer rather than a quiet acquittal.</para>
    /// </summary>
    private struct InkPlate
    {
        internal string Name;
        internal string Kind;

        /// <summary>The graphic's rect in LEVEL-0 texels, mapped exactly as the glyph quads are.</summary>
        internal float X0, X1, Y0, Y1;

        /// <summary>Filled in by the mip-0 plane: the largest and the mean deviation from the strip
        /// background over the sampled grid, and how many samples landed inside the buffer.</summary>
        internal float Worst, Mean;
        internal int Samples;
    }

    private sealed class InkComponent
    {
        internal string Name = string.Empty;
        internal string Text = string.Empty;
        internal int MeshGlyphs;
        internal int InStrip;
        internal int Ink;
        internal int Empty;
        internal string EmptyChars = string.Empty;
        internal int EmptyNotNamed;

        /// <summary>Of <see cref="InStrip"/>, how many the MIP 0 plane actually judged and how many
        /// were under <see cref="InkMinQuadTexels"/>. <c>Ink + Empty == Judged</c>, and Judged can be
        /// short of InStrip when the millisecond budget stopped the plane — both are printed, because
        /// "14 with ink, 0 empty of 21" and "14 with ink, 7 empty of 21" are opposite readings.</summary>
        internal int Judged;
        internal int BelowFloor;

        /// <summary>How many of this component's glyphs the registration search actually SCORED. The
        /// search subsamples to <see cref="InkSearchMaxGlyphs"/>, so the flat-field and material-gain
        /// bars are taken against this and not against <see cref="GlyphCount"/>.</summary>
        internal int ScoreGlyphs;
        internal int ScoreStride = 1;

        // ---- the slice of InkCensus.Glyphs this component owns, and its own scale ---------------
        // The glyph list is built component by component, so each component's glyphs are contiguous.
        // The two radii are what the joint registration search spans: one glyph advance in X and one
        // line height in Y, both estimated from this component's OWN quads rather than from a
        // constant, because this window carries body text and headings at very different sizes.
        internal int FirstGlyph;
        internal int GlyphCount;
        internal float AdvanceX;
        internal float LineY;

        // ---- the three DISJOINT fates of an EMPTY glyph (ModBuild 206) -------------------------
        /// <summary>EMPTY at the predicted place and EMPTY everywhere in the search window: the ink
        /// is genuinely absent from the capture.</summary>
        internal int Absent;

        /// <summary>EMPTY at the predicted place but FOUND at a sub-advance offset: a mapping or
        /// capture DISPLACEMENT, and the offset is the finding.</summary>
        internal int FoundOffset;

        /// <summary>Neither could be established — the search could not run, ran into the strip edge,
        /// or fitted a LATTICE ALIAS (an offset a whole advance or line away, which an is-there-ink
        /// test cannot distinguish from the truth in running text).</summary>
        internal int Ambiguous;

        /// <summary>EMPTY glyphs sitting within one glyph advance of a strip edge. An OVERLAY count,
        /// not a fourth bucket: a partially covered quad is not a clean reading whichever bucket it
        /// landed in, and the line says so rather than quietly counting it as evidence.</summary>
        internal int EmptyNearStripEdge;

        /// <summary>The offset this component's glyphs registered best at, in texels, and whether the
        /// fit is usable (in the window, sub-advance, and better than the null hypothesis).</summary>
        internal float FitDx, FitDy;
        internal int FitPassAtBest, FitPassAtZero;
        internal string FitNote = string.Empty;

        /// <summary>The searched window in texels, per side, and whether the score field turned out to
        /// be FLAT (no offset in the whole window found ink in a meaningful share of this component's
        /// glyphs). Both are printed on every component that was searched, because ModBuild 206 could
        /// not show whether a field had a peak at all.</summary>
        internal float SpanX, SpanY;
        internal bool FlatField;

        // ---- THE ALPHA EVIDENCE (ModBuild 207) --------------------------------------------------
        // The check that decides whether anything this census has found is real. If a component is
        // hidden by a CanvasGroup alpha 0 somewhere above it, it draws nothing CORRECTLY, and counting
        // its glyphs as EMPTY would make the whole finding an artefact of this instrument's own
        // exclusion test. The ModBuild 204 split reported 155-216 of 216 text components at inherited
        // alpha 0 on this very window, first named 'Gold Warning' — so the population that COULD be
        // wrongly censused is most of the window, and "my test looks equivalent to theirs" is not
        // good enough. These four numbers are printed for the components that produced EMPTY glyphs,
        // so a reader can see that the empties are fully visible rather than take it on trust.

        /// <summary>The component's own <c>Graphic.color.a</c>.</summary>
        internal float OwnAlpha;

        /// <summary>Its <c>CanvasRenderer.GetAlpha()</c> and <c>GetInheritedAlpha()</c> — the two
        /// values <c>NoteRendererState</c> reads, so the two instruments can be compared directly.</summary>
        internal float CrAlpha;
        internal float InheritedAlpha;

        /// <summary>THE PRODUCT OF EVERY <see cref="CanvasGroup"/> BETWEEN THIS COMPONENT AND THE HOST,
        /// measured directly by walking the transform chain. Deliberately INDEPENDENT of
        /// <see cref="InheritedAlpha"/>: that value is maintained by the canvas during its render pass
        /// and can be stale or absent, and a census that trusted it would inherit its blind spots. A
        /// disagreement between the two is itself the finding and is counted.</summary>
        internal float GroupAlpha;

        /// <summary>The groups found, named with their alphas — evidence rather than a number.</summary>
        internal string GroupNote = string.Empty;
    }

    /// <summary>
    /// <b>ONE SURFACE THE CENSUS READS — a texture and a mip level — and everything that plane's own
    /// readback found.</b> See the plane table in this region's header.
    /// <para>The per-glyph verdicts are held HERE rather than in one shared array because the whole
    /// point of ModBuild 208 is the COMPARISON between planes: "inked at mip 0, EMPTY at mip 2" needs
    /// both planes' answers to exist at the same time, and the <c>NativeArray</c> a readback delivers
    /// is only valid inside its own callback.</para>
    /// </summary>
    private sealed class InkPlane
    {
        internal readonly int Index;

        /// <summary>The mip level within its own texture. Every texel coordinate this plane uses is
        /// the level-0 coordinate times <see cref="Scale"/>.</summary>
        internal readonly int Mip;

        /// <summary>True for the plane that reads <see cref="Entry.Rt"/> (the capture target) rather
        /// than the display target.</summary>
        internal readonly bool FromCapture;

        internal readonly float Scale;
        internal readonly string Short;
        internal readonly string Label;

        internal InkPlane(int index, int mip, bool fromCapture, string shortName, string label)
        {
            Index = index;
            Mip = mip;
            FromCapture = fromCapture;
            Scale = 1f / (1 << mip);
            Short = shortName;
            Label = label;
        }

        // ---- whether this plane could be read at all, and why not -------------------------------
        internal bool Available;
        internal string Unavailable = string.Empty;

        internal bool Requested, Landed, Failed;
        internal string FailWhy = string.Empty;

        // ---- the strip AT THIS LEVEL ------------------------------------------------------------
        internal int X, W, H;
        internal int RequestFrame, DeliveredFrame;
        internal int ProfileStride;

        // ---- what it measured -------------------------------------------------------------------
        internal float Background;
        internal float CorrAsIs, CorrFlip;
        internal bool Decided, Flipped;

        /// <summary>True when this plane ran the cheap CONFIRMATION profile because the window had
        /// already decided its row order, false when it ran the full-strength deciding sweep. See
        /// <see cref="InkConfirmProfileSamples"/>.</summary>
        internal bool Confirming;

        /// <summary>The glyph index this plane STARTED judging at, how many it judged, and how many it
        /// left for the next census when <see cref="InkJudgeBudgetMs"/> ran out. A plane never
        /// silently covers less than the line claims.</summary>
        internal int JudgeFrom, Judged, Deferred;

        internal int Ink, Empty;

        /// <summary>Glyphs whose quad is under <see cref="InkMinQuadTexels"/> on an axis AT THIS
        /// LEVEL. They are neither INKED nor EMPTY — a correctly minified glyph is not a defect and
        /// this instrument cannot judge one, so it says so instead of counting it.</summary>
        internal int BelowFloor;

        /// <summary>The smallest and largest quad this plane could still judge, in texels, so a reader
        /// can see how close the floor came to biting.</summary>
        internal float SmallestJudged, LargestJudged;

        internal float MedDevInk = -1f, MedDevEmpty = -1f;

        /// <summary>The whole plane's time, and the share of it the fixed orientation prologue took.
        /// Both are printed: a plane that spent its budget before reaching a glyph is a different
        /// reading from one that spent it judging.</summary>
        internal double JudgeMs, ProfileMs;

        /// <summary>The share of <see cref="InkFrameBudgetMs"/> this plane's glyph loop was actually
        /// granted, which is what <see cref="Overran"/> is measured against. Printed, because a plane
        /// that got 0.3 ms and one that got 2.0 ms are completely different readings of the same
        /// "budget spent" flag.</summary>
        internal double BudgetMs;

        internal bool Overran;

        /// <summary>Per glyph: judged at all (above the floor and inside the budget), and inked.
        /// Indexed in lockstep with <see cref="InkCensus.Glyphs"/>.</summary>
        internal readonly bool[] Measured = new bool[MaxInkCensusGlyphs];
        internal readonly bool[] Inked = new bool[MaxInkCensusGlyphs];

        /// <summary>Per glyph: the local background measured at THIS level. The mip 0 plane's copy is
        /// what the registration search reuses for every offset it tries.</summary>
        internal readonly float[] Bg = new float[MaxInkCensusGlyphs];

        internal void Reset()
        {
            Available = false;
            Unavailable = string.Empty;
            Requested = Landed = Failed = false;
            FailWhy = string.Empty;
            X = W = H = 0;
            RequestFrame = DeliveredFrame = 0;
            ProfileStride = 0;
            Background = 0f;
            CorrAsIs = CorrFlip = 0f;
            Decided = Flipped = Confirming = false;
            JudgeFrom = Judged = Deferred = 0;
            Ink = Empty = BelowFloor = 0;
            SmallestJudged = float.MaxValue;
            LargestJudged = 0f;
            MedDevInk = MedDevEmpty = -1f;
            JudgeMs = ProfileMs = BudgetMs = 0.0;
            Overran = false;
            System.Array.Clear(Measured, 0, Measured.Length);
            System.Array.Clear(Inked, 0, Inked.Length);
        }
    }

    /// <summary>Per-window census state: what is armed, what is in flight, and every counter the line
    /// is read against. Keyed by window name and NOT held on <see cref="Entry"/> for the same reason
    /// <see cref="TargetLife"/> is not — that type lives in <c>PanelSupersample.1.Core.cs</c>, which
    /// this lane does not own.</summary>
    private sealed class InkCensus
    {
        // ---- THE FOUR PLANES (ModBuild 208) -----------------------------------------------------
        internal readonly InkPlane[] Planes =
        {
            new InkPlane(InkPlaneCapture, 0, true, "CAPTURE",
                         "the CAPTURE TARGET at mip 0 — what the capture camera rasterised, BEFORE "
                         + "Graphics.Blit resolved it into the display target"),
            new InkPlane(InkPlaneMip0, 0, false, "MIP 0",
                         "the DISPLAY TARGET at mip 0 — after the resolve blit; the ONLY plane "
                         + "ModBuild 205-207 ever read"),
            new InkPlane(InkPlaneMip1, 1, false, "MIP 1",
                         "the DISPLAY TARGET at mip 1 — one of the two levels the eye actually samples"),
            new InkPlane(InkPlaneMip2, 2, false, "MIP 2",
                         "the DISPLAY TARGET at mip 2 — the other level the eye actually samples"),
        };

        /// <summary>Planes still to land. The cross-plane comparison, the search and the report all
        /// happen when this reaches zero, in whichever callback got there last.</summary>
        internal int PlanesOutstanding;
        internal int PlanesRequested, PlanesLanded, PlanesFailed;
        internal long TexelsRequested;

        /// <summary>The capture target's instance id at request time, checked at delivery exactly as
        /// <see cref="TargetId"/> is.</summary>
        internal int CaptureId;
        internal int MipCount;

        // ---- what is scheduled ------------------------------------------------------------------
        internal int ArmedFrame = -1;
        internal string ArmedReason = string.Empty;
        internal int SettleFrame = -1;

        // ---- what is in flight ------------------------------------------------------------------
        internal bool InFlight;
        internal int Gen;
        internal string Reason = string.Empty;
        internal int RequestFrame;
        internal int TargetId;
        internal int RtW, RtH;
        internal int StripX, StripW, StripH;

        // ---- THE ROAMING BAND AND ITS COVERAGE LEDGER (ModBuild 209) ----------------------------
        // See MaxInkBands. Every field here is per WINDOW and survives across censuses; the strip
        // geometry above is per census and is derived from BandIndex.

        /// <summary>The band this reading covered, and the tiling it was chosen from. The tiling is
        /// re-derived every census from the LIVE target width and strip width, because both can change
        /// under a re-allocation, and the ledger below is reset when they do.</summary>
        internal int BandIndex, BandCount, BandW;

        /// <summary>The distance between consecutive bands' strips, HALF a strip width, so the strips
        /// overlap and no label can fall between two of them and be censused by neither. See
        /// <see cref="MaxInkBands"/>.</summary>
        internal int BandStride;

        /// <summary>Where the roam resumes. Advanced past the band this census took, so a settled
        /// window walks the whole width instead of re-electing the same seed for ever.</summary>
        internal int BandCursor;

        /// <summary>Whether THIS census got as far as choosing a band. A build that failed before the
        /// choice leaves <see cref="BandIndex"/> pointing at the PREVIOUS census's band, and writing an
        /// outcome into the ledger there would attribute one band's silence to another.</summary>
        internal bool BandSelected;

        /// <summary>The band in AUTHORED px (host-local uGUI, the space the user's screenshot and every
        /// layout number in this class are in) and the component that owns most of its glyphs. The
        /// authored range is what makes the line comparable against the photograph at all.</summary>
        internal float BandAuthoredLo, BandAuthoredHi;
        internal string BandDominantComp = string.Empty;

        /// <summary>Bands passed over on THIS census because no glyph quad of the whole frame falls in
        /// them — margin, not content. Never a band skipped for having read clean before.</summary>
        internal int BandsSkippedEmpty;

        /// <summary>How the band was chosen, in one clause, printed on the line. A reading whose band
        /// was AIMED is a different kind of evidence from one that came up in the roam and the line
        /// must not blur the two.</summary>
        internal string BandAimRule = string.Empty;

        /// <summary>The one-shot aim a caller may set before arming. Component name wins over authored
        /// x, authored x wins over the busiest-elsewhere rule, and any of them loses to the roam if the
        /// band it resolves to holds no glyph quads at all. Cleared as soon as it is consumed — it
        /// forces the NEXT census and no others.</summary>
        internal string AimComponent = string.Empty;
        internal float AimAuthoredX = float.NaN;
        internal bool AimBusiestOther;
        internal string AimWhy = string.Empty;

        /// <summary>The geometry the coverage ledger below was built for. A re-allocation changes the
        /// tiling, which makes every stored band index mean a different piece of the window, so the
        /// ledger is thrown away rather than silently re-interpreted — and the line says so.</summary>
        internal int CoverRtW, CoverStripW;
        internal int CoverResets;

        /// <summary>THE COVERAGE LEDGER. Per band: how many censuses covered it, whether it has ever
        /// held a glyph quad, how many glyphs the last reading there judged, and that reading's
        /// verdict. One line must be able to answer "which parts of this window have ever been looked
        /// at, and what did they say" — until 209 no line in this class could.</summary>
        internal readonly int[] BandVisits = new int[MaxInkBands];
        internal readonly bool[] BandHasContent = new bool[MaxInkBands];
        internal readonly int[] BandJudged = new int[MaxInkBands];
        internal readonly string[] BandVerdict = new string[MaxInkBands];

        internal readonly List<InkGlyph> Glyphs = new(MaxInkCensusGlyphs);
        internal readonly List<InkComponent> Comps = new(16);

        // ---- what the build had to leave out ----------------------------------------------------
        internal int ComponentsFound;
        internal int ComponentsSkippedCap;
        internal int GlyphsOutsideStrip;
        internal int GlyphsSkippedCap;
        internal bool WalkTruncated;

        /// <summary>
        /// <b>GLYPH QUADS THE COMMITTED CAPTURE FRAME DOES NOT CONTAIN — i.e. CROPPED, AND THEREFORE
        /// LEGITIMATELY ABSENT.</b> Split out of <see cref="GlyphsOutsideStrip"/> in ModBuild 206
        /// because the two mean opposite things and were being added together.
        /// <para>The capture camera's viewport IS <see cref="Entry.Frame"/> (see
        /// <see cref="SyncProjection"/>), so in texel space the frame is exactly [0, RtW] x [0, RtH]
        /// and a quad straddling that boundary is content the capture cannot hold. That is the
        /// authoritative crop test and it needs no cooperation from any other lane: whatever clamps
        /// the frame — the expansion limit, the hysteresis, a band-limit clamp — does it by making
        /// <c>Frame</c> smaller, and this test reads <c>Frame</c> live. A cropped glyph must NEVER be
        /// counted as a defect, and before this split it was.</para>
        /// </summary>
        internal int GlyphsOutsideFrame;

        /// <summary>Glyphs excluded because their material is served by a TMP_SubMeshUI CHILD whose
        /// local transform is NOT identity, so the parent's matrix would map them to the wrong texels.
        /// Expected 0 — TMP creates its sub-mesh children matching the parent rect — and excluded
        /// rather than mismapped, because an instrument that mismaps a glyph reports it EMPTY and that
        /// is exactly the wrong answer to hand this investigation.</summary>
        internal int GlyphsSubMeshTransform;

        /// <summary>
        /// <b>GLYPH QUADS EXCLUDED BECAUSE A MASK OR A SCROLL VIEWPORT CLIPS THEM, AND THIS COUNTER IS
        /// LOAD-BEARING.</b> uGUI's <see cref="RectMask2D"/> and <see cref="Mask"/> discard fragments
        /// in the SHADER, so a label scrolled out of a viewport keeps a perfectly good quad in the
        /// submitted mesh and puts no pixels down — legitimately. A census that did not exclude those
        /// would report them EMPTY, and "glyphs are missing from the capture" is the verdict that sends
        /// the next round to work on the capture path. This window is full of scroll viewports, so
        /// without this the instrument would have manufactured its own answer.
        /// </summary>
        internal int GlyphsClipped;

        /// <summary>Text components excluded because uGUI/TMP has switched them off at the renderer
        /// (the cull flag, own alpha, inherited alpha, or an authored alpha of 0) or because an
        /// ancestor mask clips them away entirely. Their glyphs are legitimately absent from the
        /// capture and counting them as EMPTY would be the same manufactured answer. Every prior
        /// instrument in this class reads 0 for the renderer half, so a non-zero value here is itself
        /// worth reading rather than a quiet shrinking of the census.</summary>
        internal int ComponentsNotDrawn;

        // ---- the last completed result ----------------------------------------------------------
        internal int DeliveredFrame;
        internal string SourceNote = string.Empty;
        internal float FrameW, FrameH, RateX, RateY;
        internal float Background;
        internal bool Flipped;
        internal float CorrAsIs, CorrFlip;
        internal bool OrientationDecided;
        internal string Orientation = "not measured yet";
        internal int TotalGlyphs, TotalInk, TotalEmpty;

        /// <summary>Of <see cref="TotalGlyphs"/>, how many the MIP 0 plane actually judged, how many
        /// were under <see cref="InkMinQuadTexels"/>, and how many the millisecond budget deferred to
        /// the next census. <c>TotalInk + TotalEmpty == TotalJudged</c> by construction, and the line
        /// prints all four so a shortened census can never read as a complete one.</summary>
        internal int TotalJudged, TotalBelowFloor, TotalDeferred;

        internal float MedDevInk = -1f, MedDevEmpty = -1f;
        internal double BuildMs, ReadMs;

        // ---- THE MAPPING SELF-CHECK'S OWN RESULT (ModBuild 206) ---------------------------------

        /// <summary>The capture frame as it stood when the request went out. Compared against the LIVE
        /// frame at delivery: any movement at all makes every texel coordinate in the result describe a
        /// viewport that no longer exists, so the reading is DROPPED rather than judged. The frame is
        /// quantised to a 32 px grid and damped by hysteresis, so it is either identical or a whole
        /// quantum different — there is no near-miss to tolerate.</summary>
        internal Rect FrameAtRequest;
        internal int DroppedFrameMoved;

        /// <summary>THE INK-CENTROID DISPLACEMENT over the glyphs judged INKED, per axis, in texels:
        /// lowest / mean / highest over a count. Never one number — a single glyph's ink is not
        /// centred in its own quad, so the per-glyph scatter carries the glyph SHAPES as well as any
        /// displacement and only the mean is about the mapping.</summary>
        internal int CentroidCount;
        internal float CentroidDxLow, CentroidDxHigh, CentroidDyLow, CentroidDyHigh;
        internal double CentroidDxSum, CentroidDySum;

        /// <summary>The three disjoint fates of an EMPTY glyph, summed over the components, plus the
        /// EMPTY glyphs no search could be spent on because the cap bit.</summary>
        internal int EmptyAbsent, EmptyFoundOffset, EmptyAmbiguous, EmptyNotSearched;
        internal int EmptyNearStripEdge;

        /// <summary>The distribution of the FITTED offsets across the components that produced a
        /// usable sub-advance fit, in texels. A whole-string displacement gives a tight cluster; noise
        /// gives scatter, and the spread is printed so the two cannot be confused.</summary>
        internal int FitCount;
        internal float FitDxLow, FitDxHigh, FitDyLow, FitDyHigh;
        internal double FitDxSum, FitDySum;
        internal int FitLatticeAlias, FitNoFit;

        /// <summary>Fitted offsets of the components that came back LATTICE ALIAS. ModBuild 206 threw
        /// these away because the classification stays ambiguous; the OFFSET is informative anyway —
        /// five components fitting the same alias is a very different reading from five fitting five
        /// different ones.</summary>
        internal int AliasCount;
        internal double AliasDxSum, AliasDySum;

        /// <summary>THE ONE-SENTENCE VERDICT, chosen by the numbers rather than left to the reader.</summary>
        internal string MappingVerdict = "not measured yet";
        internal string FitNote = string.Empty;

        // ---- THE ALPHA EVIDENCE, censused (ModBuild 207) -----------------------------------------

        /// <summary>
        /// <b>COMPONENTS EXCLUDED BY THE CANVASGROUP CHAIN THAT THE RENDERER TEST WOULD HAVE LET
        /// THROUGH — THE NUMBER THAT DECIDES WHETHER ANY PREVIOUS FINDING WAS REAL.</b>
        /// <para>A non-zero value means ModBuild 205/206 censused components that are legitimately
        /// hidden and counted their glyphs as EMPTY, i.e. the headline EMPTY numbers of those builds
        /// are inflated by exactly this many components' worth. The verdict says so in those words
        /// rather than leaving it to be inferred from a counter.</para>
        /// </summary>
        internal int ComponentsHiddenByGroupOnly;

        /// <summary>Components excluded by the CanvasGroup chain in total (including the ones the
        /// renderer test would also have caught) — the denominator that makes the number above
        /// readable.</summary>
        internal int ComponentsHiddenByGroup;

        /// <summary>The per-component alpha evidence for the components that produced EMPTY glyphs,
        /// named. This is the proof that the empties are NOT hidden components.</summary>
        internal string AlphaNote = string.Empty;

        /// <summary>The lowest effective alpha (own x renderer x inherited x group product) among the
        /// components that produced EMPTY glyphs. If this is not ~1, the empties are partly a
        /// visibility artefact and the verdict says so.
        /// <para><b>THIS FIELD IS A MINIMUM AND A MINIMUM IS THE WRONG SHAPE FOR THE QUESTION.</b> Read
        /// the ModBuild 210 note on <see cref="EmptyOpaqueGlyphs"/> before using it for anything: on the
        /// 209 log it dismissed 28 of 49 readings on the strength of one label that is greyed out by
        /// design, and the opaque components missing their ink in the same readings went unreported.</para></summary>
        internal float EmptyLowestAlpha = 1f;

        /// <summary>The name and effective alpha of the component that SET
        /// <see cref="EmptyLowestAlpha"/>. The evidence field's stated job is "it names them", and on
        /// eleven ModBuild 209 readings it named nothing below 1.000 at all because the component that
        /// triggered the verdict sat past the naming cap. It is now named first and unconditionally.</summary>
        internal string LowestAlphaComp = string.Empty;

        /// <summary>
        /// <b>THE SPLIT THAT SHOULD HAVE BEEN THE HEADLINE ALL ALONG: empties from components that are
        /// FULLY OPAQUE, counted apart from empties that come out of a faded one.</b>
        /// <para>A faded component's missing glyphs say nothing about the capture path — that part of
        /// the old ARTEFACT reasoning was right. What was wrong was applying it to the WHOLE READING.
        /// The two populations are disjoint, they are counted separately from ModBuild 210, and the
        /// verdict speaks about the opaque one whatever the faded one is doing.</para>
        /// </summary>
        internal int EmptyOpaqueComps, EmptyOpaqueGlyphs;
        internal int EmptyDimComps, EmptyDimGlyphs;

        /// <summary>The non-text graphics that believed they drew inside this census's strip, and what
        /// the capture holds where each says it is. See <see cref="InkPlate"/>; the verdict is
        /// deliberately one-sided and only names the unambiguous case.</summary>
        /// <summary>Graphics whose stale inherited alpha this census repaired, and how many of them the
        /// line names. See <see cref="DrawReason.StaleInheritedAlpha"/>.</summary>
        internal int InheritedRepairs, InheritedRepairNamed, StaleSeen, StaleNoGroup;

        internal readonly List<InkPlate> Plates = new(MaxInkPlates);
        internal int PlatesSeen, PlatesOutsideStrip, PlatesBlank, PlatesPresent, PlatesUnjudged;
        internal string PlateNote = string.Empty;

        // ---- THE LEVEL COMPARISON (ModBuild 208) -------------------------------------------------

        /// <summary>
        /// <b>THE FINDING, and it is a COMPARISON and never an absolute.</b> Glyphs INKED at mip 0 and
        /// EMPTY at mip 1 / mip 2 while their quad at that level is comfortably above
        /// <see cref="InkMinQuadTexels"/>. An absolute EMPTY count at mip 2 would be dominated by
        /// glyphs that legitimately averaged away; this pair cannot be.
        /// </summary>
        internal int MipLost1, MipLost2;

        /// <summary>How many glyphs the comparison could actually be made on, per level — inked at
        /// mip 0 AND judged at that level. The denominator without which the counts above say nothing.</summary>
        internal int MipCompared1, MipCompared2;

        /// <summary>The lost glyphs, named with their size at the level that lost them.</summary>
        internal string MipLostNote = string.Empty;

        /// <summary>The reverse direction, which must be counted or "lost" is not a finding but a
        /// threshold artefact: glyphs EMPTY at mip 0 and INKED at mip 1 / mip 2. Minification makes
        /// thin ink DENSER per texel as often as it dilutes it, so a healthy chain produces a few of
        /// these and the line prints them next to the losses.</summary>
        internal int MipGained1, MipGained2;

        /// <summary>THE RESOLVE BLIT, isolated: glyphs judged on both the capture plane and the mip 0
        /// plane, and the two directions of disagreement between them.</summary>
        internal int BlitCompared, BlitOnlyCapture, BlitOnlyMip;

        internal string MipVerdict = "not measured yet";
        internal string BlitVerdict = "not measured yet";

        // ---- THE COST BOUND (ModBuild 208) -------------------------------------------------------

        /// <summary>
        /// <b>WHERE THE NEXT CENSUS RESUMES JUDGING.</b> The budget stops a plane mid-list; the
        /// remainder is carried here rather than dropped, and the next census starts there.
        /// <para>The glyph list is REBUILT by <see cref="BuildInkCensus"/> on every census, so this is
        /// an index into a list that is only approximately the same one — the walk order is stable but
        /// the window's own content is not. The line therefore prints the RANGE that was judged and
        /// the count deferred, and never claims that a rotation covered the window exactly.</para>
        ///
        /// <para><b>AND IT IS PER BAND SINCE ModBuild 209, WHICH THE ROAMING STRIP MADE NECESSARY.</b>
        /// A deferral means "the rest of THIS list", and with a roaming strip the next census's list
        /// is a different band's glyphs — resuming at index 40 of a band that has 90 would have left
        /// that band's first 40 glyphs unjudged and called the remainder a continuation. So each band
        /// carries its own resume point in <see cref="BandGlyphCursor"/>: returning to a band picks up
        /// where its own last reading stopped, and a band being read for the first time starts at 0.
        /// This field is the value for the band that was just read, kept because the line prints
        /// it.</para>
        /// </summary>
        internal int GlyphCursor;

        /// <summary>Where judging resumes IN EACH BAND. See <see cref="GlyphCursor"/>. Reset with the
        /// coverage ledger, because a re-allocation makes every band index mean something else.</summary>
        internal readonly int[] BandGlyphCursor = new int[MaxInkBands];

        /// <summary>The cursor this census's planes all started from. Every plane of one census must
        /// judge the SAME glyphs or the level comparison would compare different subsets, so the
        /// cursor is frozen at issue time and only advanced afterwards.</summary>
        internal int CensusCursor;

        /// <summary>Where the neighbourhood search resumes, by component index.</summary>
        internal int SearchCursor;

        /// <summary>Whether the search ran at all this census, and why not when it did not.</summary>
        internal bool SearchRan;
        internal double SearchBudgetMs;
        internal string SearchNote = string.Empty;
        internal int SearchComponents, SearchDeferredComps;
        internal double SearchMs;
        internal bool SearchOverran;

        /// <summary>The PREVIOUS census's level-comparison result, which is what gates the search.
        /// The mip 0 buffer and the mip 1/2 buffers never coexist — each dies with its own callback —
        /// so the gate cannot read this census's own comparison and reads the last one instead. The
        /// first census after engage therefore always searches.</summary>
        internal int LastMipLost = -1;

        /// <summary>Judging time summed over the planes of this census, and the worst single plane.
        /// Printed against <see cref="FrameBudgetMs"/> because a plane lands on its own frame.</summary>
        internal double JudgeMsTotal, JudgeMsWorst;

        // ---- since engage -----------------------------------------------------------------------
        internal int Armed, Issued, Completed, Errors;
        internal int DroppedInFlight, DroppedStale, Unanswerable, Threw;
        internal string LastUnanswerable = string.Empty;
        internal float UnanswerableNextPrint;
    }

    private static readonly Dictionary<string, InkCensus> InkCensuses = new(8);

    private static InkCensus InkOf(Entry e)
    {
        if (!InkCensuses.TryGetValue(e.Window, out InkCensus c))
        {
            c = new InkCensus();
            InkCensuses[e.Window] = c;
        }
        return c;
    }

    // Scratch, all reused and all cleared by their own users — nothing here allocates per frame.
    private static readonly List<InkFrame> InkWalk = new(256);
    private static readonly List<InkGlyph> InkCandidates = new(1024);
    private static readonly List<InkCandidateComp> InkCandidateComps = new(MaxInkCandidateComponents);
    private static readonly float[] InkRing = new float[128];
    private static readonly float[] InkBandPredicted = new float[InkBands];
    private static readonly float[] InkBandMeasured = new float[InkBands];

    // The per-band CONTENT histogram of the roaming strip (ModBuild 209). Nothing to do with InkBands
    // above, which are the HORIZONTAL slices of the orientation self-check's vertical ink profile:
    // these are the VERTICAL tiles of the frame's WIDTH that the strip walks. Two different axes and
    // two different jobs; the names are kept apart deliberately.
    private static readonly int[] InkBandGlyphCount = new int[MaxInkBands];
    private static readonly List<float> InkDevInk = new(MaxInkCensusGlyphs);
    private static readonly List<float> InkDevEmpty = new(MaxInkCensusGlyphs);
    private static readonly List<float> InkScratch = new(4096);
    private static readonly StringBuilder InkCharsSb = new(128);

    // The mapping self-check's per-glyph working set now lives on the PLANE (InkPlane.Bg / .Inked /
    // .Measured), because ModBuild 208's whole finding is a comparison BETWEEN planes and a single
    // shared array could only ever hold one of them.
    private static readonly StringBuilder InkFitSb = new(512);
    private static readonly StringBuilder InkMipSb = new(512);
    private static readonly StringBuilder InkGroupSb = new(256);
    private static readonly StringBuilder InkAlphaSb = new(512);

    // The draw-state ledger's own scratch (ModBuild 210). DrawSeen is the set of path hashes this
    // census visited, used to evict entries whose graphic has left the hierarchy; DrawLost and
    // DrawGained hold the transitions until the report is written.
    private static readonly HashSet<int> DrawSeen = new();
    private static readonly List<DrawLedgerEntry> DrawLost = new(64);
    private static readonly List<DrawLedgerEntry> DrawGained = new(64);
    private static readonly List<int> DrawEvict = new(64);
    private static readonly StringBuilder DrawSb = new(1024);
    // Sized from the enum rather than a literal: ModBuild 211 added StaleInheritedAlpha = 9 and a
    // hand-kept literal would have indexed out of range on the first graphic that hit it.
    private static readonly int[] DrawReasonCount =
        new int[System.Enum.GetValues(typeof(DrawReason)).Length + 1];

    /// <summary>Component indices in the order the registration search will spend its budget on them —
    /// fully opaque empty-producers first. See FitInkComponents for why walk order was the wrong one.</summary>
    private static readonly List<int> InkSearchOrder = new(MaxInkCandidateComponents);
    private static readonly StringBuilder InkPlateSb = new(512);
    private static readonly StringBuilder InkRepairSb = new(512);
    private static readonly StringBuilder PhaseFlipSb = new(512);
    private static readonly StringBuilder PhaseLayerSb = new(512);
    private static readonly StringBuilder PhaseFrameSb = new(512);
    private static readonly List<bool> PhaseDrawn = new(MaxPhaseGraphics);

    // ---- THE DRAW-STATE LEDGER (ModBuild 210) ---------------------------------------------------
    //
    // THE REPORT THAT MOVED THE POPULATION. Asked whether the broken picture loses images as well as
    // text, the user answered: "es betrifft auch bilder/symbole! Die auch random je nachdem wann man
    // loslässt da sind oder verschwinden es betrifft NICHT nur text". Every census this project has
    // built reads TMP_Text and only TMP_Text. An Image is not a TMP_Text. So the elements he is
    // describing have never once been inside the measured population, and eight builds of clean
    // readings were clean about the wrong set of objects.
    //
    // WHAT SURVIVES THAT. A cause that takes text and images alike cannot live in TMP's mesh, its
    // atlas, its sub-meshes or its glyph registration — all of which have now been measured clean
    // anyway. It has to sit on the one thing an Image and a label share: the Graphic/CanvasRenderer
    // pair. That is a very short list, and DrawReason enumerates it.
    //
    // WHY A TRANSITION AND NOT A COUNT. The ModBuild 209 log reads "155 text component(s) were
    // excluded because the renderer had switched them off" on 39 of 49 readings. That number is not a
    // finding: this window carries six sub-views and most of it is SUPPOSED to be switched off. What
    // is a finding is a graphic that was DRAWN on one census and EXCLUDED on the next, because that is
    // the sentence the user is speaking — "da sind oder verschwinden". The ledger therefore keeps the
    // last state per graphic and reports only what CHANGED, with the reason it changed and where in
    // the picture it sits.
    //
    // AND THE TRAP THIS MUST NOT FALL INTO, which this project has fallen into three times: a census
    // that cannot separate its own bookkeeping from the defect. A graphic that leaves the hierarchy —
    // the user pressing a different tab — must not print as "lost" for ever, so unseen entries are
    // evicted after DrawLedgerEvictAfter censuses and COUNTED as evictions rather than as losses.

    /// <summary>
    /// <b>ONE PHASE SAMPLE: which of the cached graphics would draw AT THIS INSTANT.</b> Returns the
    /// count and writes a set signature, so two samples of one frame can be compared both by how many
    /// draw and by WHICH — a swap of one element for another keeps the count and changes the picture.
    /// <para>No tree walk: the list was built by the census and is re-read in place. The clip test is
    /// deliberately omitted (it needs the walk's inherited rect), so this measures the RENDERER-side
    /// state only — which is exactly the half that can flip between two cameras of one frame.</para>
    /// </summary>
    private static int SamplePhase(Entry e, out int signature)
    {
        int drew = 0;
        PhaseDrawn.Clear();
        unchecked
        {
            int sig = 17;
            for (int i = 0; i < e.PhaseGraphics.Count; i++)
            {
                Graphic g = e.PhaseGraphics[i];
                bool draws = g != null
                             && ClassifyDraw(g, e.Panel.HostRect!, clipEmpty: false, out float _)
                                == DrawReason.Drawn;
                PhaseDrawn.Add(draws);
                if (draws)
                {
                    drew++;
                    sig = sig * 31 + i;
                }
            }
            signature = sig;
        }
        return drew;
    }

    /// <summary>
    /// <b>DID THE DRAWN SET CHANGE SINCE THE PREVIOUS SAMPLED FRAME?</b> The half ModBuild 214 did not
    /// build. Its within-frame comparison answered cleanly — every camera of a frame agrees — which
    /// closes "the two eyes see different pictures" and leaves the user's actual sentence untouched:
    /// <i>"die Elemente sind ständig kurz sichtbar und dann wieder nicht"</i> is about SUCCESSIVE
    /// frames. Split by MOVING vs STILL because his report is exactly that the two differ: a still
    /// window freezes, a carried one flickers.
    /// </summary>
    private static void ComparePhaseFrames(Entry e, bool moving)
    {
        if (e.PhasePrevFrame >= 0 && e.PhasePrevDrawn.Count == PhaseDrawn.Count)
        {
            int flips = 0;
            PhaseFlipSb.Length = 0;
            int named = 0;
            for (int i = 0; i < PhaseDrawn.Count; i++)
            {
                if (PhaseDrawn[i] == e.PhasePrevDrawn[i])
                    continue;
                flips++;
                if (named < MaxDrawStateNamed && i < e.PhaseGraphics.Count
                    && e.PhaseGraphics[i] != null)
                {
                    named++;
                    PhaseFlipSb.Append(" '").Append(e.PhaseGraphics[i].gameObject.name).Append("' (")
                               .Append(e.PhaseGraphics[i].GetType().Name).Append(") -> ")
                               .Append(PhaseDrawn[i] ? "ON" : "OFF").Append(';');
                }
            }
            e.PhaseFrameCompares++;
            if (moving)
                e.PhaseMovingCompares++;
            else
                e.PhaseStillCompares++;
            if (flips > 0)
            {
                e.PhaseFrameChanges++;
                if (moving)
                    e.PhaseMovingChanges++;
                else
                    e.PhaseStillChanges++;
                if (flips >= e.PhaseWorstFlips)
                {
                    e.PhaseWorstFlips = flips;
                    e.PhaseFlipNote = $"{flips} graphic(s) changed between two consecutive sampled "
                                      + $"frames while the window was {(moving ? "MOVING" : "STILL")}:"
                                      + PhaseFlipSb;
                }
            }
            PhaseFlipSb.Length = 0;
        }
        e.PhasePrevDrawn.Clear();
        e.PhasePrevDrawn.AddRange(PhaseDrawn);
        e.PhasePrevFrame = Time.frameCount;
    }

    /// <summary>
    /// <b>THE COMPARISON THAT THE ink CENSUS CANNOT MAKE, because it samples in lockstep with the
    /// capture.</b> Called from the GLOBAL <see cref="Camera.onPreCull"/> for every camera in the
    /// frame — our capture camera and both MultiPass eye passes among them. The first sample of a
    /// frame is the reference; every later one is compared against it.
    /// <para>A disagreement means the set of graphics that would draw CHANGED between two cameras of
    /// the SAME frame. That is the user's "die Elemente sind ständig kurz sichtbar und dann wieder
    /// nicht" expressed as a number, and no instrument in this project could express it before.</para>
    /// </summary>
    private static void SamplePhaseFor(Entry e, Camera cam)
    {
        if (e.PhaseGraphics.Count == 0 || e.Panel.HostRect == null)
            return;
        int frame = Time.frameCount;
        if (frame % PhaseSampleEveryFrames != 0)
            return;

        float started = Time.realtimeSinceStartup;
        int drew = SamplePhase(e, out int sig);
        e.PhaseSamples++;

        if (e.PhaseFrame != frame)
        {
            e.PhaseFrame = frame;
            e.PhaseFrames++;
            e.PhaseFirstDrew = drew;
            e.PhaseFirstSig = sig;
            e.PhaseFirstCam = cam.name;
            // Frame-to-frame on the FIRST sample of the frame only, so the comparison is always
            // like-for-like: the same point in the camera order, one frame apart.
            ComparePhaseFrames(e, IsMoving(e));
        }
        else if (sig != e.PhaseFirstSig)
        {
            // Counted once per FRAME, not once per sample: three cameras disagreeing is one event,
            // and counting it three times would make the rate depend on how many cameras happen to
            // render — which is a property of the scene and not of the defect.
            if (e.PhaseDisagreeFrames == 0 || frame != e.PhaseLastDisagreeFrame)
                e.PhaseDisagreeFrames++;
            e.PhaseLastDisagreeFrame = frame;
            int delta = Mathf.Abs(drew - e.PhaseFirstDrew);
            if (delta >= e.PhaseWorstDelta)
            {
                e.PhaseWorstDelta = delta;
                e.PhaseWorstNote = $"'{e.PhaseFirstCam}' saw {e.PhaseFirstDrew} of "
                                   + $"{e.PhaseGraphics.Count} graphic(s) drawing and '{cam.name}' saw "
                                   + $"{drew} in the SAME frame ({frame})";
            }
        }
        ChargeInkBudget((Time.realtimeSinceStartup - started) * 1000.0);
    }

    /// <summary>
    /// A stable identity for a graphic that survives the object being destroyed and rebuilt. The
    /// hierarchy PATH from the host, not the instance id: uGUI rebuilds replace objects while the
    /// element on screen stays the same element to the user, and an instance id would report every
    /// rebuild as a loss plus a gain and drown the real transitions.
    /// </summary>
    private static int DrawPathHash(Transform t, Transform host)
    {
        unchecked
        {
            int h = 17;
            Transform? cur = t;
            int guard = 0;
            while (cur != null && !ReferenceEquals(cur, host) && ++guard < 64)
            {
                h = h * 31 + cur.name.GetHashCode();
                h = h * 31 + cur.GetSiblingIndex();
                cur = cur.parent;
            }
            return h;
        }
    }

    /// <summary>
    /// <b>WHY THIS GRAPHIC PUT NO PIXELS INTO THE CAPTURE, in the order uGUI applies the tests.</b>
    /// First reason wins — see <see cref="DrawReason"/> for why the order is part of the answer and not
    /// a detail. The alphas are read exactly as <c>CollectInkCandidate</c> reads them and against the
    /// same <see cref="InkAlphaFloor"/>, so the ledger and the ink census can never disagree about
    /// whether a component drew.
    /// </summary>
    private static DrawReason ClassifyDraw(Graphic g, RectTransform host, bool clipEmpty,
                                           out float groupAlpha)
    {
        groupAlpha = 1f;
        if (!g.isActiveAndEnabled)
            return DrawReason.Inactive;
        CanvasRenderer cr = g.canvasRenderer;
        if (cr == null)
            return DrawReason.NoRenderer;
        if (cr.cull)
            return DrawReason.Culled;
        if (g.color.a <= InkAlphaFloor)
            return DrawReason.OwnAlpha;
        if (cr.GetAlpha() <= InkAlphaFloor)
            return DrawReason.RendererAlpha;

        // ---- THE COMPARISON ModBuild 210 COULD NOT MAKE -----------------------------------------
        // The group chain is measured HERE, unconditionally, and BEFORE the inherited-alpha test
        // returns. ModBuild 210 tested inherited alpha first and returned on the first hit, so a
        // renderer holding a stale zero and a legitimately hidden panel produced the SAME answer —
        // and this window has 155 legitimately hidden components, so that answer said nothing. The
        // whole finding is the DISAGREEMENT between two independent measurements of the same thing.
        groupAlpha = InkGroupChainAlpha(g.transform, host, out string _);
        bool groupHides = groupAlpha <= InkAlphaFloor;
        if (cr.GetInheritedAlpha() <= InkAlphaFloor)
            return groupHides ? DrawReason.InheritedAlpha : DrawReason.StaleInheritedAlpha;
        if (groupHides)
            return DrawReason.GroupAlpha;
        if (clipEmpty)
            return DrawReason.ClippedOut;
        return DrawReason.Drawn;
    }

    /// <summary>Record one graphic's draw state and, if it differs from the last census's, hold the
    /// transition for the report. Called from the candidate walk for EVERY <see cref="Graphic"/>, which
    /// is the whole point: the ink census's population is TMP text and the user's report is not.</summary>
    private static void NoteDrawState(Entry e, InkCensus c, RectTransform host, Rect frame, Graphic g,
                                      bool clipEmpty)
    {
        DrawReason reason = ClassifyDraw(g, host, clipEmpty, out float groupAlpha);

        // ---- THE REPAIR (ModBuild 211) ------------------------------------------------------------
        // It acts on EXACTLY ONE state and nowhere else: the renderer says it inherits zero alpha while
        // the CanvasGroup chain above it, walked independently, says the graphic is visible. Those two
        // are measurements of the SAME quantity, uGUI maintains the first during its rebuild and the
        // second is the authority on what it should hold, so a disagreement is a stale value and not a
        // decision anybody made.
        //
        // WHY IT IS SAFE TO WRITE, and this is the line that decides it: the repair NEVER reveals a
        // panel that is meant to be hidden, because a hidden panel's group chain reads zero and lands
        // in DrawReason.InheritedAlpha, which this branch does not touch. The 155 legitimately hidden
        // components on this window are therefore out of reach by construction rather than by a
        // threshold. It is capped per census and every repair is NAMED, so if something appears that
        // should not, the log says which graphic and the dial turns it off.
        //
        // AND IT IS NOT GATED BEHIND THE INSTRUMENT. This project has shipped a fix that could only run
        // when the diagnostic that was meant to test it had already fired, and then read "no
        // improvement" as evidence. The repair runs on its own dial; the count is reported either way.
        // HOW IT REPAIRS, and the route is forced rather than chosen: CanvasRenderer in Unity 2021.3.5f1
        // exposes GetAlpha, SetAlpha and GetInheritedAlpha — and NO SetInheritedAlpha. The inherited
        // value is written by the native canvas during its rebuild and by nothing else, so the only
        // lever is to make that rebuild happen again. Nudging the nearest CanvasGroup does exactly
        // that and nothing else: the alpha is written to a hair off and straight back, which marks the
        // group dirty and re-propagates the accumulated alpha over its whole subtree. The value the
        // game set is restored in the same statement, so there is no state left behind to fight over.
        if (reason == DrawReason.StaleInheritedAlpha)
        {
            c.StaleSeen++;
            CanvasGroup? group = WorldUIConfig.PanelRepairInheritedAlpha.Value
                                 && c.InheritedRepairs < MaxInheritedRepairsPerCensus
                ? NearestGroup(g.transform, host)
                : null;
            if (group != null)
            {
                float groupWas = group.alpha;
                group.alpha = groupWas >= 0.5f ? groupWas - 0.001f : groupWas + 0.001f;
                group.alpha = groupWas;
                c.InheritedRepairs++;
                if (c.InheritedRepairNamed < MaxDrawStateNamed)
                {
                    c.InheritedRepairNamed++;
                    InkRepairSb.Append(" '").Append(g.gameObject.name).Append("' (")
                               .Append(g.GetType().Name).Append(") via group '")
                               .Append(group.gameObject.name).Append("' at alpha ")
                               .Append(groupWas.ToString("F3")).Append(';');
                }
            }
            else if (WorldUIConfig.PanelRepairInheritedAlpha.Value)
            {
                // NO GROUP TO NUDGE. That is a finding in its own right and it is counted rather than
                // silently skipped: if the graphics in this state have no CanvasGroup above them at
                // all, then the zero was not written by a group and the next round needs a different
                // lever — a nested Canvas boundary is the first suspect.
                c.StaleNoGroup++;
            }
        }

        DrawReasonCount[(int)reason]++;

        int key = DrawPathHash(g.transform, host);
        if (!DrawSeen.Add(key))
            return;

        // Cache a bounded subset for the ModBuild 214 phase census. Rebuilt with the ledger, so it
        // follows the window's content instead of pinning objects that have gone.
        if (e.PhaseGraphics.Count < MaxPhaseGraphics)
            e.PhaseGraphics.Add(g);

        // ---- THE LAYER AND THE FRUSTUM (ModBuild 216) ------------------------------------------
        // Read on the graphic itself, at census time, and compared against the two things the capture
        // camera actually tests. Only for graphics that DRAW — a hidden one being off-layer or out of
        // frame is not a defect, and counting it would bury the signal under this window's 484
        // legitimately hidden components.
        if (reason == DrawReason.Drawn)
        {
            if (e.Layer >= 0 && g.gameObject.layer != e.Layer)
            {
                e.PhaseWrongLayer++;
                if (PhaseLayerSb.Length < 400)
                    PhaseLayerSb.Append(" '").Append(g.gameObject.name).Append("' (")
                                .Append(g.GetType().Name).Append(") on layer ")
                                .Append(g.gameObject.layer).Append(" not ").Append(e.Layer)
                                .Append(';');
            }
        }

        float cx = 0f, cy = 0f;
        bool haveRect = false;
        Rect b = default;
        if (g.transform is RectTransform rt && TryHostLocalBounds(host, rt, out b))
        {
            cx = b.center.x;
            cy = b.center.y;
            haveRect = true;
        }

        // The FRUSTUM half of ModBuild 216, placed here because it needs the host-local bounds above.
        // A graphic that draws, is on the right layer, and whose rect does not meet the capture frame
        // is simply not in the photograph — and the frame is the mod's own measurement, so a graphic
        // outside it is a defect in that measurement rather than in the window.
        if (reason == DrawReason.Drawn && haveRect && !b.Overlaps(e.Frame))
        {
            e.PhaseOutsideFrame++;
            if (PhaseFrameSb.Length < 400)
                PhaseFrameSb.Append(" '").Append(g.gameObject.name).Append("' at (")
                            .Append(b.center.x.ToString("F0")).Append(',')
                            .Append(b.center.y.ToString("F0")).Append(") size ")
                            .Append(b.width.ToString("F0")).Append('x')
                            .Append(b.height.ToString("F0")).Append(';');
        }

        // ---- THE PLATE CANDIDATE (ModBuild 210) ----------------------------------------------
        // A NON-TEXT graphic that believes it drew. Its rect goes on the list in level-0 texels, and
        // the mip 0 plane later reads the capture under it. TMP is excluded here because its glyph
        // quads are censused properly a few lines further on and a whole label's rect is mostly page:
        // sampling it as one flat region would call every correctly-drawn line of text "blank".
        if (haveRect && reason == DrawReason.Drawn && g is not TMPro.TMP_Text
            && c.Plates.Count < MaxInkPlates)
        {
            c.PlatesSeen++;
            float px0 = (b.xMin - frame.xMin) * c.RateX;
            float px1 = (b.xMax - frame.xMin) * c.RateX;
            float py0 = (b.yMin - frame.yMin) * c.RateY;
            float py1 = (b.yMax - frame.yMin) * c.RateY;
            if (px1 - px0 >= MinInkPlateTexels && py1 - py0 >= MinInkPlateTexels)
            {
                c.Plates.Add(new InkPlate
                {
                    Name = g.gameObject.name,
                    Kind = g.GetType().Name,
                    X0 = px0,
                    X1 = px1,
                    Y0 = py0,
                    Y1 = py1,
                });
            }
        }

        var now = new DrawLedgerEntry
        {
            Name = g.gameObject.name,
            Kind = g.GetType().Name,
            Reason = reason,
            CentreX = cx,
            CentreY = cy,
            SeenGen = c.Gen,
        };

        if (e.DrawLedger.TryGetValue(key, out DrawLedgerEntry was))
        {
            bool drewBefore = was.Reason == DrawReason.Drawn;
            bool drawsNow = reason == DrawReason.Drawn;
            if (drewBefore && !drawsNow)
                DrawLost.Add(now);
            else if (!drewBefore && drawsNow)
            {
                // Carry the reason it USED to fail, because "it came back" and "it came back after
                // being culled" are different sentences and only the second names a writer.
                DrawLedgerEntry gained = now;
                gained.Kind = $"{now.Kind}, was {was.Reason}";
                DrawGained.Add(gained);
            }
            e.DrawLedger[key] = now;
            return;
        }
        if (e.DrawLedger.Count < MaxDrawStateTracked)
            e.DrawLedger[key] = now;
    }

    /// <summary>
    /// Close the ledger for this census: evict graphics that have left the hierarchy, then format the
    /// transitions into the sentence the report prints.
    /// <para>The eviction is what keeps a sub-view switch from reading as a defect. An entry not
    /// visited for <see cref="DrawLedgerEvictAfter"/> censuses is gone from the window, not hidden in
    /// it, and it leaves as an EVICTION — counted separately and never as a loss.</para>
    /// </summary>
    private static void FinishDrawLedger(Entry e, InkCensus c)
    {
        DrawEvict.Clear();
        foreach (KeyValuePair<int, DrawLedgerEntry> kv in e.DrawLedger)
        {
            if (c.Gen - kv.Value.SeenGen >= DrawLedgerEvictAfter)
                DrawEvict.Add(kv.Key);
        }
        for (int i = 0; i < DrawEvict.Count; i++)
            e.DrawLedger.Remove(DrawEvict[i]);

        e.DrawLedgerLost = DrawLost.Count;
        e.DrawLedgerGained = DrawGained.Count;
        e.DrawLedgerTracked = e.DrawLedger.Count;
        e.DrawLedgerEvicted = DrawEvict.Count;

        DrawSb.Length = 0;
        DrawSb.Append(" DRAW-STATE LEDGER — WHICH GRAPHICS STOPPED DRAWING, AND WHY. This is the ")
              .Append("population the previous eight builds did not have: EVERY Graphic in the ")
              .Append("subtree, Images and symbols included, not only TMP text. The user's report is ")
              .Append("\"es betrifft auch bilder/symbole ... die random da sind oder verschwinden\", ")
              .Append("and an Image cannot appear in a TMP census at all. THE STATE THIS CENSUS ")
              .Append("FOUND, one disjoint reason per graphic, in the order uGUI applies them: ")
              .Append(DrawReasonCount[(int)DrawReason.Drawn]).Append(" DREW, ")
              .Append(DrawReasonCount[(int)DrawReason.Culled])
              .Append(" CULLED (CanvasRenderer.cull — a RectMask2D sets exactly this on a graphic ")
              .Append("whose rect leaves the clip rectangle, and it hits an Image the same way it ")
              .Append("hits a label, which is the ONLY short list left after text-only causes were ")
              .Append("measured out), ")
              .Append(DrawReasonCount[(int)DrawReason.OwnAlpha]).Append(" at authored alpha 0, ")
              .Append(DrawReasonCount[(int)DrawReason.RendererAlpha]).Append(" at renderer alpha 0, ")
              .Append(DrawReasonCount[(int)DrawReason.InheritedAlpha])
              .Append(" at inherited alpha 0 WITH THE GROUP CHAIN AGREEING (a legitimately hidden "
                      + "panel — this window has six sub-views and most of it is supposed to be in "
                      + "this state), ")
              .Append(DrawReasonCount[(int)DrawReason.StaleInheritedAlpha])
              .Append(" AT INHERITED ALPHA 0 WHILE THE CANVASGROUP CHAIN SAYS THEY ARE FULLY VISIBLE "
                      + "— THIS IS THE FINDING AND IT IS THE ONE NUMBER ON THIS LINE THAT CANNOT BE "
                      + "ANYTHING ELSE. Two independent measurements of the same quantity disagree: "
                      + "uGUI maintains the renderer's inherited alpha during its rebuild, the "
                      + "transform walk says what it should hold, and a graphic in this state draws "
                      + "NOTHING while it is not culled, its own colour is opaque, its mesh is intact "
                      + "and nothing above it is faded. That is the exact state eight builds of ink "
                      + "census measured and could not name, and it takes an Image the same way it "
                      + "takes a label. ")
              .Append(DrawReasonCount[(int)DrawReason.GroupAlpha])
              .Append(" behind a CanvasGroup chain at 0, ")
              .Append(DrawReasonCount[(int)DrawReason.ClippedOut]).Append(" clipped away entirely, ")
              .Append(DrawReasonCount[(int)DrawReason.Inactive]).Append(" switched off, ")
              .Append(DrawReasonCount[(int)DrawReason.NoRenderer]).Append(" with no CanvasRenderer. ")
              .Append("READ THE COUNTS AS CONTEXT AND THE TRANSITIONS AS THE FINDING: this window ")
              .Append("carries six sub-views and most of it is SUPPOSED to be switched off, so a ")
              .Append("large exclusion count is the window's normal shape and says nothing. What ")
              .Append("says something is a graphic that DREW on the previous census and does not ")
              .Append("draw now. TRANSITIONS SINCE THE PREVIOUS CENSUS: ")
              .Append(DrawLost.Count).Append(" STOPPED DRAWING, ").Append(DrawGained.Count)
              .Append(" STARTED DRAWING, over ").Append(e.DrawLedgerTracked)
              .Append(" graphic(s) tracked; ").Append(DrawEvict.Count)
              .Append(" entry(s) were EVICTED because their graphic left the hierarchy entirely — a ")
              .Append("tab the user closed, counted here and NEVER as a loss, because an instrument ")
              .Append("that reports the user's own navigation as a defect is worse than no ")
              .Append("instrument.");

        // ---- THE REPAIR'S OWN REPORT (ModBuild 211) ----------------------------------------------
        DrawSb.Append(" THE REPAIR: ")
              .Append(WorldUIConfig.PanelRepairInheritedAlpha.Value
                  ? $"{c.InheritedRepairs} of {c.StaleSeen} stale renderer(s) were repaired by nudging "
                    + "the nearest CanvasGroup, which makes the canvas re-propagate the accumulated "
                    + "alpha over its subtree (CanvasRenderer in this Unity exposes GetInheritedAlpha "
                    + "and NO setter, so the value cannot be written directly — the rebuild is the "
                    + $"only lever). {c.StaleNoGroup} had NO CanvasGroup above them at all, which is "
                    + "its own finding: a zero that no group wrote needs a different lever, and a "
                    + "nested Canvas boundary is the first suspect. The repair touches ONLY the "
                    + "disagreement above — a panel the game means to hide reads zero on BOTH "
                    + "measurements and is out of reach by construction rather than by a threshold, "
                    + "so this can never reveal a closed sub-view. The group's own alpha is restored "
                    + $"in the same statement. Capped at {MaxInheritedRepairsPerCensus} per census. "
                    + "Switch it off with [WorldUI] PanelRepairInheritedAlpha if anything appears "
                    + "that should not."
                  : "SWITCHED OFF by [WorldUI] PanelRepairInheritedAlpha, so the count above is what "
                    + "the picture is still carrying. Nothing was written.")
              .Append(InkRepairSb.Length > 0
                  ? " REPAIRED, NAMED:" + InkRepairSb
                  : string.Empty);
        InkRepairSb.Length = 0;

        AppendDrawTransitions(DrawSb, "STOPPED DRAWING", DrawLost);
        AppendDrawTransitions(DrawSb, "STARTED DRAWING", DrawGained);

        if (DrawLost.Count == 0 && DrawGained.Count == 0)
        {
            DrawSb.Append(" NOTHING CHANGED STATE ON THIS CENSUS. Taken alone that is one quiet ")
                  .Append("reading and not an acquittal — the transitions that matter are the ones ")
                  .Append("at the RELEASE EDGE, and this line has to be read at all three moments ")
                  .Append("before the draw state can be ruled out. If the release-edge and ")
                  .Append("settled readings BOTH say nothing changed while the user still sees ")
                  .Append("elements missing, then the graphics all believe they drew and the loss is ")
                  .Append("downstream of the CanvasRenderer — which would be a genuine finding and ")
                  .Append("the first one to point past uGUI's own state.");
        }

        e.PhaseLayerNote = PhaseLayerSb.ToString();
        e.PhaseFrameNote = PhaseFrameSb.ToString();
        PhaseLayerSb.Length = 0;
        PhaseFrameSb.Length = 0;
        e.DrawLedgerNote = DrawSb.ToString();
        DrawSb.Length = 0;
        DrawLost.Clear();
        DrawGained.Clear();
        DrawSeen.Clear();
        DrawEvict.Clear();
        for (int i = 0; i < DrawReasonCount.Length; i++)
            DrawReasonCount[i] = 0;
    }

    private static void AppendDrawTransitions(StringBuilder sb, string label, List<DrawLedgerEntry> list)
    {
        if (list.Count == 0)
            return;
        sb.Append(' ').Append(label).Append(", named with the reason and the host-local centre so a ")
          .Append("lost element can be found in the photograph:");
        int named = Mathf.Min(list.Count, MaxDrawStateNamed);
        for (int i = 0; i < named; i++)
        {
            DrawLedgerEntry d = list[i];
            sb.Append(" '").Append(d.Name).Append("' (").Append(d.Kind).Append(") ")
              .Append(d.Reason).Append(" at (").Append(d.CentreX.ToString("F0")).Append(',')
              .Append(d.CentreY.ToString("F0")).Append(");");
        }
        if (list.Count > named)
            sb.Append(" and ").Append(list.Count - named).Append(" more (naming cap).");
    }

    /// <summary>Arm a census for this window. It is ISSUED later in the same frame, from the capture
    /// camera's own <see cref="Camera.onPostRender"/> — i.e. after <see cref="ResolveAndMip"/> has
    /// finished, so the readback reads THIS frame's resolved image and not the previous one.</summary>
    private static void ArmInkCensus(Entry e, string reason)
    {
        InkCensus c = InkOf(e);
        c.ArmedFrame = Time.frameCount;
        c.ArmedReason = reason;
        c.Armed++;
    }

    /// <summary>
    /// <b>AIM THE NEXT CENSUS AT A PARTICULAR PART OF THE WINDOW (ModBuild 209).</b> One-shot: the aim
    /// is consumed by the next <see cref="BuildInkCensus"/> and cleared there, so it forces exactly one
    /// reading and the roam resumes after it.
    ///
    /// <para>Three ways in, in priority order. <paramref name="component"/> is a GameObject name
    /// (ordinal, case-insensitive, substring) — preferred whenever the caller knows what it wants to
    /// look at, because a name survives a layout that moved between two readings and a coordinate does
    /// not. <paramref name="authoredX"/> is a HOST-LOCAL uGUI x, the same space
    /// <see cref="Entry.Frame"/> and every glyph quad in this file live in, and it is the fallback for
    /// when only a position is known. With neither, the band containing the busiest component that is
    /// NOT the band just read is chosen — which is the cheap "go somewhere that matters and somewhere
    /// new" rule, and the one the release edge uses.</para>
    ///
    /// <para>An aim NEVER overrides the emptiness rule: if the band it resolves to holds no glyph quad
    /// at all, the roam takes over and the line says the aim missed. Aiming a census at blank margin
    /// would waste the most interesting moment this instrument gets.</para>
    /// </summary>
    private static void AimInkCensus(Entry e, string? component, float authoredX, string why)
    {
        InkCensus c = InkOf(e);
        c.AimComponent = component ?? string.Empty;
        c.AimAuthoredX = authoredX;
        c.AimBusiestOther = string.IsNullOrEmpty(c.AimComponent) && float.IsNaN(authoredX);
        c.AimWhy = why;
    }

    /// <summary>Arm the two release readings: one at the release edge itself and one
    /// <see cref="InkSettleFrames"/> frames later. Called from <see cref="ReportRelease"/>, i.e. on
    /// the exact frame the settle gate opened.
    ///
    /// <para>ModBuild 209 AIMS the release-edge reading (see <see cref="AimInkCensus"/>) rather than
    /// letting the roam decide it. The release edge is the single most informative moment this
    /// instrument gets — it is the frame the user's "the picture FREEZES" complaint is about — and
    /// spending it on whichever band the cursor happened to reach would be a waste of it.</para></summary>
    private static void ArmInkCensusForRelease(Entry e)
    {
        AimInkCensus(e, null, float.NaN,
                     "the RELEASE EDGE is aimed, not roamed: it is the moment the complaint is about");
        ArmInkCensus(e, "the RELEASE EDGE (the frame the settle gate opened)");
        InkOf(e).SettleFrame = Time.frameCount + InkSettleFrames;
    }

    /// <summary>
    /// Issue an armed census, from the capture camera's <see cref="Camera.onPostRender"/>. Never
    /// throws: a failure here costs this census and nothing else, and is counted so it can never read
    /// as a census that came back clean.
    /// </summary>
    private static void ServiceInkCensus(Entry e)
    {
        InkCensus c = InkOf(e);
        if (c.SettleFrame >= 0 && Time.frameCount >= c.SettleFrame)
        {
            c.SettleFrame = -1;
            // ---- THE ONE PLACE A ROAMING STRIP COULD HAVE BROKEN AN EXISTING READING ------------
            // The release-edge census and this one exist to be COMPARED — transient against latched.
            // That comparison is only meaningful over THE SAME PART OF THE WINDOW, and with the strip
            // roaming the cursor would have handed this reading a different band and the pair would
            // have silently become a comparison of two different places. So the settled reading is
            // aimed back at whatever the release-edge reading actually covered: by the NAME of the
            // component that owned most of that band's glyphs where there is one (a release can
            // re-fit the host rect — the ModBuild 195 log walks a window 328 -> 716 -> 1920 uGUI px
            // across one session — so a name is the robust handle and a coordinate is not), and by the
            // band's authored centre otherwise.
            AimInkCensus(e,
                         c.BandDominantComp.Length > 0 ? c.BandDominantComp : null,
                         c.BandDominantComp.Length > 0
                             ? float.NaN
                             : (c.BandAuthoredLo + c.BandAuthoredHi) * 0.5f,
                         "the SETTLED reading is aimed back at the band the release-edge reading "
                         + "covered, because transient-against-latched is only a comparison at all if "
                         + "both readings looked at the same part of the window");
            ArmInkCensus(e, $"{InkSettleFrames} frame(s) AFTER the release edge (the SETTLED reading — "
                            + "the user reports the picture FREEZING on release, so this reading and "
                            + "the release-edge one are what decide transient against latched)");
        }
        if (c.ArmedFrame < 0)
            return;
        if (c.InFlight || _inkInFlight >= MaxInkCensusesInFlight)
        {
            // Kept armed on purpose: the outstanding readback is a frame or two from landing and this
            // census then goes out immediately after it. Counted so a census that slipped is visible.
            c.DroppedInFlight++;
            return;
        }
        string reason = c.ArmedReason;
        c.ArmedFrame = -1;
        c.ArmedReason = string.Empty;
        try
        {
            IssueInkCensus(e, c, reason);
        }
        catch (System.Exception ex)
        {
            // If the throw came from AsyncGPUReadback.Request itself the global budget has already
            // been charged and no callback will ever land to hand it back — so it is handed back
            // here (EndInkCensus, called by ReportInkUnanswerable, releases it exactly once). Without
            // that, two such throws would wedge the census shut for the session.
            c.Threw++;
            ReportInkUnanswerable(e, c, reason,
                $"the census BUILD threw ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>
    /// Release the mod-wide census budget exactly once, whatever ended this census. Every exit —
    /// the report, every NOT ANSWERABLE path, and a window standing down with planes outstanding —
    /// goes through here, because ModBuild 208 charges the budget ONCE PER CENSUS rather than once
    /// per request and a leak would wedge the instrument shut for the session while every line still
    /// claimed it was armed.
    /// </summary>
    private static void EndInkCensus(InkCensus c)
    {
        if (!c.InFlight)
            return;
        c.InFlight = false;
        c.PlanesOutstanding = 0;
        if (_inkInFlight > 0)
            _inkInFlight--;
    }

    /// <summary>Drop a window's census state, releasing the budget first. Called from
    /// <see cref="StandDown"/>: a readback still in flight then finds no record and discards itself,
    /// which is correct, but the budget it charged must not go with it.</summary>
    /// <summary>
    /// Write this census's outcome into the coverage ledger of the band it covered (ModBuild 209).
    /// Called from BOTH report paths: a census that produced no reading still has to be recorded as
    /// having LOOKED, or the ledger would report a band as never covered when in truth it was covered
    /// and answered nothing — the same silent truncation every other counter in this region forbids.
    /// </summary>
    private static void NoteInkBandOutcome(InkCensus c, string verdict, int judged)
    {
        if (!c.BandSelected || c.BandIndex < 0 || c.BandIndex >= MaxInkBands)
            return;
        c.BandVerdict[c.BandIndex] = verdict;
        c.BandJudged[c.BandIndex] = judged;
    }

    /// <summary>How much of this window's width the SEQUENCE of censuses has now seen. The denominator
    /// is the bands that hold a drawable glyph quad AS OF THIS CENSUS, not the bands the tiling has:
    /// a band over blank margin holds no text and there is nothing there for any census to answer
    /// about, so counting it would make full coverage unreachable by construction and the number
    /// meaningless. The numerator is historical — a band counts as covered if ANY census since engage
    /// read it — so the pair means "of the window as it stands, this much has been looked at".
    /// </summary>
    private static void InkCoverage(InkCensus c, out int covered, out int withContent, out int emptyBands)
    {
        covered = 0;
        withContent = 0;
        emptyBands = 0;
        for (int b = 0; b < c.BandCount && b < MaxInkBands; b++)
        {
            if (c.BandHasContent[b])
            {
                withContent++;
                if (c.BandVisits[b] > 0)
                    covered++;
            }
            else
            {
                emptyBands++;
            }
        }
    }

    private static void ReleaseInkCensus(string window)
    {
        if (InkCensuses.TryGetValue(window, out InkCensus c))
            EndInkCensus(c);
        InkCensuses.Remove(window);
    }

    private static void IssueInkCensus(Entry e, InkCensus c, string reason)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            ReportInkUnanswerable(e, c, reason,
                "this GPU / graphics API reports NO AsyncGPUReadback support "
                + $"(SystemInfo.supportsAsyncGPUReadback = false on {SystemInfo.graphicsDeviceType}), "
                + "and a blocking ReadPixels of a "
                + $"{e.RtW}x{e.RtH} target during a drag would stall the pipeline on the very frames "
                + "this bug lives on — so no census can be taken on this machine at all");
            return;
        }

        RenderTexture? src = InkSource(e, out string sourceNote, out string? refusal);
        if (src == null)
        {
            ReportInkUnanswerable(e, c, reason, refusal!);
            return;
        }

        float started = Time.realtimeSinceStartup;
        if (!BuildInkCensus(e, c, out string? why))
        {
            c.BuildMs = (Time.realtimeSinceStartup - started) * 1000.0;
            ReportInkUnanswerable(e, c, reason, why!);
            return;
        }
        c.BuildMs = (Time.realtimeSinceStartup - started) * 1000.0;

        c.SourceNote = sourceNote;
        c.Reason = reason;
        c.RequestFrame = Time.frameCount;
        c.TargetId = src.GetInstanceID();
        c.CaptureId = e.Rt != null ? e.Rt.GetInstanceID() : 0;
        c.MipCount = src.mipmapCount;
        c.RtW = e.RtW;
        c.RtH = e.RtH;
        c.FrameW = e.Frame.width;
        c.FrameH = e.Frame.height;
        // ModBuild 206: the WHOLE frame rect, not only its size. The mapping subtracts Frame.xMin /
        // Frame.yMin, so a frame that keeps its size and MOVES its origin re-maps every glyph without
        // changing a single number the previous staleness test compared. See InkCensus.FrameAtRequest.
        c.FrameAtRequest = e.Frame;
        c.Gen++;
        c.Issued++;

        // ---- WHICH OF THE FOUR PLANES CAN BE READ, AND WHY THE OTHERS CANNOT (ModBuild 208) ------
        PrepareInkPlanes(e, c, src);

        // Every plane of one census must judge the SAME glyphs or the level comparison would compare
        // different subsets of the window. The cursor is therefore frozen here and only advanced when
        // the census is complete. It comes from THIS BAND's own resume point (ModBuild 209) — see
        // InkCensus.GlyphCursor for why a single shared cursor stopped meaning anything the moment
        // the strip started to roam.
        int bandResume = c.BandSelected && c.BandIndex >= 0 && c.BandIndex < MaxInkBands
            ? c.BandGlyphCursor[c.BandIndex]
            : 0;
        c.CensusCursor = c.Glyphs.Count > 0 ? bandResume % c.Glyphs.Count : 0;
        c.PlanesOutstanding = 0;
        c.PlanesRequested = 0;
        c.PlanesFailed = 0;
        c.PlanesLanded = 0;
        c.TexelsRequested = 0;
        c.MipLost1 = c.MipLost2 = c.MipCompared1 = c.MipCompared2 = 0;
        c.MipGained1 = c.MipGained2 = 0;
        c.BlitCompared = c.BlitOnlyCapture = c.BlitOnlyMip = 0;
        c.MipLostNote = string.Empty;
        c.JudgeMsTotal = 0.0;
        c.JudgeMsWorst = 0.0;

        // Count the planes BEFORE issuing any of them. If the first callback landed while the count
        // still read 1, PlanesOutstanding would hit zero and the census would report on one plane.
        for (int i = 0; i < InkPlaneOrder.Length; i++)
        {
            if (c.Planes[InkPlaneOrder[i]].Available)
                c.PlanesOutstanding++;
        }
        if (c.PlanesOutstanding == 0)
        {
            // Cannot happen — InkSource has already established that the mip 0 plane's texture is
            // readable — but a census that reported nothing and said nothing would be exactly the
            // failure mode this whole region exists to make impossible.
            ReportInkUnanswerable(e, c, reason,
                "not one of the four census planes could be read, which contradicts the source check "
                + "that has already passed and therefore means this instrument is broken rather than "
                + "the picture");
            return;
        }

        c.InFlight = true;
        _inkInFlight++;

        string window = e.Window;
        int gen = c.Gen;
        for (int i = 0; i < InkPlaneOrder.Length; i++)
        {
            InkPlane p = c.Planes[InkPlaneOrder[i]];
            if (!p.Available)
                continue;
            // PrepareInkPlanes only marks the capture plane Available after establishing e.Rt is
            // non-null, single-sample and the same size as the display target.
            RenderTexture tex = p.FromCapture ? e.Rt! : src;
            int plane = p.Index;
            p.RequestFrame = Time.frameCount;
            try
            {
                // The strip in x at THIS level, the FULL height in y (see MinInkStripTexels for why
                // that is a correctness requirement and not a convenience), one slice in z.
                AsyncGPUReadback.Request(tex, p.Mip, p.X, p.W, 0, p.H, 0, 1, TextureFormat.RGBA32,
                                         req => OnInkRead(window, gen, plane, req));
                p.Requested = true;
                c.PlanesRequested++;
                c.TexelsRequested += (long)p.W * p.H;
            }
            catch (System.Exception ex)
            {
                // A plane that never got a request will never get a callback either, so its share of
                // PlanesOutstanding is handed back here or the census would never complete.
                p.Failed = true;
                p.FailWhy = $"AsyncGPUReadback.Request threw ({ex.GetType().Name}: {ex.Message})";
                c.PlanesFailed++;
                c.PlanesOutstanding--;
            }
        }
        if (c.PlanesOutstanding <= 0)
        {
            ReportInkUnanswerable(e, c, reason,
                $"every one of the {c.PlanesRequested + c.PlanesFailed} census plane(s) was refused by "
                + "AsyncGPUReadback.Request itself, so no readback is coming and nothing was measured");
        }
    }

    /// <summary>
    /// <b>DECIDE WHICH OF THE FOUR PLANES THIS CENSUS CAN READ, AND RECORD WHY THE OTHERS CANNOT.</b>
    /// An unavailable plane is never silent: its reason is printed on the census line, because "mip 2
    /// reported nothing" and "mip 2 does not exist on this target" are opposite readings.
    /// <para>The strip at mip level m is the level-0 strip shifted down by m. That is exact rather
    /// than approximate: <c>floor(a/k) + floor(b/k) &lt;= floor((a+b)/k)</c>, so a strip that fits
    /// inside the level-0 target still fits inside every level of it.</para>
    /// </summary>
    private static void PrepareInkPlanes(Entry e, InkCensus c, RenderTexture shown)
    {
        for (int i = 0; i < c.Planes.Length; i++)
            c.Planes[i].Reset();

        bool fallback = !ReferenceEquals(shown, e.MipRt);
        int mipCount = shown.mipmapCount;

        for (int i = 0; i < c.Planes.Length; i++)
        {
            InkPlane p = c.Planes[i];
            int m = p.Mip;
            p.X = c.StripX >> m;
            p.W = Mathf.Max(1, c.StripW >> m);
            p.H = Mathf.Max(1, c.StripH >> m);

            if (p.FromCapture)
            {
                if (e.Rt == null)
                {
                    p.Unavailable = "this panel has no capture target right now, so the resolve blit "
                                    + "cannot be isolated";
                    continue;
                }
                if (fallback)
                {
                    p.Unavailable = "the display target IS the capture target on the MipFallback path, "
                                    + "so there is no blit between them to isolate — plane MIP 0 below "
                                    + "already reads this texture";
                    continue;
                }
                if (e.Rt.antiAliasing > 1)
                {
                    p.Unavailable = $"the capture target is MULTISAMPLED (antiAliasing "
                                    + $"{e.Rt.antiAliasing}x) and a multisampled render target cannot "
                                    + "be read back at all, so the blit cannot be isolated on this "
                                    + "machine";
                    continue;
                }
                if (e.Rt.width != c.RtW || e.Rt.height != c.RtH)
                {
                    p.Unavailable = $"the capture target is {e.Rt.width}x{e.Rt.height} against the "
                                    + $"display target's {c.RtW}x{c.RtH}, so the same texel "
                                    + "coordinates do not name the same place in both and comparing "
                                    + "them would invent a disagreement";
                    continue;
                }
                p.Available = true;
                continue;
            }

            if (m >= mipCount)
            {
                p.Unavailable = $"the display target carries {mipCount} mip level(s), so level {m} "
                                + "does not exist. READ THAT AS A FINDING BEFORE READING IT AS A GAP: "
                                + "a display target with no mip chain is the ModBuild 192 falsifier's "
                                + "own subject and the eye then minifies an unfiltered level 0";
                continue;
            }
            if (m > 0 && (p.W < 8 || p.H < 8))
            {
                p.Unavailable = $"the census strip is only {p.W}x{p.H} texels at this level, under the "
                                + "8x8 needed for the orientation band profile to mean anything";
                continue;
            }
            p.Available = true;
        }
    }

    /// <summary>
    /// Which texture the census reads, and why that one. It is the RESOLVED, MIPPED DISPLAY TARGET at
    /// mip 0 — i.e. the exact texture the <see cref="RawImage"/> quad samples — so "ink present here"
    /// is precisely the boundary between the capture side and the display side, which is the whole
    /// point of the instrument.
    /// <para>A MULTISAMPLED render target cannot be read back at all, so the
    /// <see cref="Entry.MipFallback"/> case (the quad shows the capture target directly) is answerable
    /// only while that target is single-sample. It is at <see cref="PreferredMsaa"/> 1, and if a
    /// future build raises it this says NOT ANSWERABLE instead of reading something else.</para>
    /// </summary>
    private static RenderTexture? InkSource(Entry e, out string note, out string? refusal)
    {
        note = string.Empty;
        refusal = null;
        RenderTexture shown = DisplayTexture(e);
        if (shown == null)
        {
            refusal = "this panel has no display render target at all right now (both the mipped "
                      + "display target and the capture target read null), so there is no captured "
                      + "image to census";
            return null;
        }
        if (shown.antiAliasing > 1)
        {
            refusal = $"the texture the quad samples is MULTISAMPLED (antiAliasing {shown.antiAliasing}x) "
                      + "and a multisampled render target cannot be read back — this only happens on "
                      + "the MipFallback path (the mipped display target was refused), and the census "
                      + "refuses rather than reading a different texture than the eye does";
            return null;
        }
        note = ReferenceEquals(shown, e.MipRt)
            ? "the RESOLVED, MIPPED DISPLAY TARGET — the exact texture the display quad samples. "
              + "ModBuild 208 reads it at mip 0, 1 AND 2 and reads the CAPTURE target beside it, so "
              + "the census now spans the resolve blit and the two levels the eye actually reads "
              + "instead of stopping at the level it does not"
            : "the CAPTURE TARGET directly (MipFallback: the mipped display target was refused, so the "
              + "quad shows this texture as well — and there is then neither a blit nor a mip chain "
              + "to census, which the plane table below states rather than leaves blank)";
        return shown;
    }

    /// <summary>
    /// <b>MAP THE MESH INTO TEXELS, FROM THE SAME TWO VALUES <see cref="SyncProjection"/> USES.</b>
    ///
    /// <para>The authoritative values are <see cref="Entry.Frame"/> (the capture frame, in HOST-LOCAL
    /// uGUI px) and <see cref="Entry.RtW"/>/<see cref="Entry.RtH"/> (the target's real dimensions).
    /// <see cref="SyncProjection"/> centres the orthographic camera on <c>Frame.center</c>, sets
    /// <c>orthographicSize = Frame.height * hostScale / 2</c> and <c>aspect = Frame.width /
    /// Frame.height</c> — so the camera's viewport IS <c>Frame</c>, exactly, and the render target
    /// spans it. The authored-to-texel rate is therefore <c>RtW / Frame.width</c> and
    /// <c>RtH / Frame.height</c>, which is character for character the pair
    /// <see cref="RecordAchievedFactor"/> already reports as the ACHIEVED factor.</para>
    ///
    /// <para><b>WHY THOSE AND NOT THE CONFIG FACTOR.</b> <c>[WorldUI] PanelSupersampleFactor</c>, the
    /// band-limit floor, the content-scale boost, <see cref="RateQuantum"/>, the VRAM step-down and the
    /// <see cref="MaxRtDimension"/> clip all sit BETWEEN the configured factor and the target that was
    /// actually allocated — five places the two can diverge, and this class already prints "asked 2.00,
    /// achieved 1.31" lines where they do. Deriving the mapping from the ALLOCATED target and the
    /// COMMITTED frame means the census and the capture camera cannot disagree however those five
    /// move.</para>
    ///
    /// <para>The per-component transform is one matrix, <c>host.worldToLocalMatrix *
    /// tmp.localToWorldMatrix</c>, so a rotated or nested text object is handled exactly rather than
    /// approximately, and each glyph costs four <c>MultiplyPoint3x4</c> calls.</para>
    /// </summary>
    private static bool BuildInkCensus(Entry e, InkCensus c, out string? why)
    {
        why = null;
        // The ledger's answer belongs to THIS census or to no census. A build that refuses before the
        // walk (a degenerate frame, no host) must not leave the previous census's transitions standing
        // where the next line will print them as if they had just been measured.
        e.DrawLedgerNote = string.Empty;
        e.PhaseGraphics.Clear();
        e.PhaseWrongLayer = e.PhaseOutsideFrame = 0;
        PhaseLayerSb.Length = 0;
        PhaseFrameSb.Length = 0;
        c.Plates.Clear();
        c.PlatesSeen = c.PlatesOutsideStrip = c.PlatesBlank = c.PlatesPresent = c.PlatesUnjudged = 0;
        c.PlateNote = string.Empty;
        c.InheritedRepairs = c.InheritedRepairNamed = c.StaleSeen = c.StaleNoGroup = 0;
        InkRepairSb.Length = 0;
        c.Glyphs.Clear();
        c.Comps.Clear();
        InkCandidates.Clear();
        InkCandidateComps.Clear();
        c.ComponentsFound = 0;
        c.ComponentsSkippedCap = 0;
        c.GlyphsOutsideStrip = 0;
        c.GlyphsSkippedCap = 0;
        c.GlyphsClipped = 0;
        c.GlyphsOutsideFrame = 0;
        c.GlyphsSubMeshTransform = 0;
        c.ComponentsNotDrawn = 0;
        c.ComponentsHiddenByGroup = 0;
        c.ComponentsHiddenByGroupOnly = 0;
        c.WalkTruncated = false;
        c.BandSelected = false;
        c.BandsSkippedEmpty = 0;

        ConvertedPanel panel = e.Panel;
        RectTransform? host = panel.HostRect;
        if (host == null || panel.HostGo == null)
        {
            why = "the panel's host RectTransform is gone, so mesh space cannot be mapped into texels";
            return false;
        }
        Rect frame = e.Frame;
        if (frame.width < 1f || frame.height < 1f || e.RtW < 2 || e.RtH < 2)
        {
            why = $"the capture frame ({frame.width:F0}x{frame.height:F0} uGUI px) or the render target "
                  + $"({e.RtW}x{e.RtH}) is degenerate, so no authored-to-texel mapping exists";
            return false;
        }
        c.RateX = e.RtW / frame.width;
        c.RateY = e.RtH / frame.height;

        // ---- gather every text component's glyph quads, in texel space --------------------------
        InkWalk.Clear();
        InkWalk.Add(new InkFrame(panel.HostGo.transform, Unbounded, clipEmpty: false));
        int visited = 0;
        int tmpSeen = 0;
        Transform? camT = e.CamGo != null ? e.CamGo.transform : null;
        while (InkWalk.Count > 0)
        {
            int last = InkWalk.Count - 1;
            InkFrame node = InkWalk[last];
            InkWalk.RemoveAt(last);
            Transform t = node.T;
            if (t == null || !t.gameObject.activeInHierarchy)
                continue;
            if (ReferenceEquals(t, camT))
                continue;
            if (++visited > MaxInkWalkTransforms)
            {
                c.WalkTruncated = true;
                break;
            }
            // The same foreign-subtree rule the frame measurement, the layer sweep and the over-paint
            // census use: a real Renderer or Camera in here belongs to somebody else.
            if (!ReferenceEquals(t, panel.HostGo.transform)
                && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;

            // THE INHERITED CLIP, resolved exactly as MeasureContentCore resolves it and with the same
            // helpers, so the two censuses cannot disagree about what a scroll viewport hides. The
            // childCount guard is not an optimisation: a mask on a LEAF clips nothing, and a text
            // component IS a leaf, so its own node never narrows its own clip.
            Rect clip = node.Clip;
            bool clipEmpty = node.ClipEmpty;
            var rt = t as RectTransform;
            if (!clipEmpty && rt != null && t.childCount > 0 && ClipsChildren(t)
                && TryHostLocalBounds(host, rt, out Rect clipBounds))
            {
                if (!Intersect(clip, clipBounds, out clip))
                    clipEmpty = true;
            }

            // ---- THE DRAW-STATE LEDGER, over EVERY graphic and not only the text (ModBuild 210) ---
            // This runs before the TMP branch and independently of it. The ink census's population is
            // TMP_Text; the user's report is "es betrifft auch bilder/symbole". One GetComponent per
            // visited transform on a walk that is already capped at MaxInkWalkTransforms.
            var graphic = t.GetComponent<Graphic>();
            if (graphic != null)
                NoteDrawState(e, c, host, frame, graphic, clipEmpty);

            var tmp = t.GetComponent<TMPro.TMP_Text>();
            if (tmp != null)
            {
                tmpSeen++;
                if (InkCandidateComps.Count >= MaxInkCandidateComponents)
                    c.ComponentsSkippedCap++;
                else
                    CollectInkCandidate(e, c, host, frame, tmp, clip, clipEmpty);
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                InkWalk.Add(new InkFrame(t.GetChild(i), clip, clipEmpty));
        }
        InkWalk.Clear();
        c.ComponentsFound = tmpSeen;

        // BEFORE the no-candidates return below, and that placement is the point. The case where the
        // ink census finds nothing censusable is EXACTLY the case where everything stopped drawing —
        // if the ledger were closed after that return, the one reading that would have named the
        // cause would be the one reading that threw it away.
        FinishDrawLedger(e, c);

        if (InkCandidateComps.Count == 0)
        {
            why = "the walk found NO censusable TextMeshPro component in this window's subtree — "
                  + $"{visited} transform(s) visited, {tmpSeen} TMP component(s) seen, of which "
                  + $"{c.ComponentsNotDrawn} were excluded because the renderer had switched them off "
                  + "(cull flag or alpha 0) or a mask hid them entirely, and "
                  + $"{c.GlyphsClipped} individual glyph quad(s) were clipped away by a viewport. "
                  + "THIS IS A STATEMENT ABOUT THE WINDOW AND NOT ABOUT THE CAPTURE: it says the "
                  + "window had no drawable text to look for, not that its text was missing";
            return false;
        }

        // ---- the strip: full height, width bounded by the readback budget, and ROAMING in x --------
        // ModBuild 205-208 centred it on the busiest component, which is a property of the WINDOW and
        // not of the census, so it re-elected the same x on all 142 readings of the 208 session and
        // 83.5 % of the window's width was never looked at once. See MaxInkBands.
        int stripW = Mathf.Clamp(MaxInkCensusTexels / Mathf.Max(e.RtH, 1), MinInkStripTexels, e.RtW);
        // HALF-STRIP STRIDE, so consecutive bands overlap and no label can fall between two of them.
        // See MaxInkBands — this is the difference between a roam that covers the width and a roam
        // that covers the width except for a boundary through the middle column.
        int stride = Mathf.Max(1, stripW / 2);
        int bandCount = Mathf.Clamp(Mathf.CeilToInt((e.RtW - stripW) / (float)stride) + 1,
                                    1, MaxInkBands);
        int stripX = SelectInkBand(e, c, stripW, stride, bandCount);
        c.StripX = stripX;
        c.StripW = stripW;
        c.StripH = e.RtH;
        c.BandAuthoredLo = frame.xMin + stripX / Mathf.Max(c.RateX, 1e-6f);
        c.BandAuthoredHi = frame.xMin + (stripX + stripW) / Mathf.Max(c.RateX, 1e-6f);
        if (c.BandVisits[c.BandIndex] == 0)
            c.BandVerdict[c.BandIndex] = "covered, no reading reported yet";
        c.BandVisits[c.BandIndex]++;

        // ---- keep the glyphs that fall wholly inside it -----------------------------------------
        for (int i = 0; i < InkCandidateComps.Count; i++)
        {
            InkCandidateComp cand = InkCandidateComps[i];
            var comp = new InkComponent
            {
                Name = cand.Name,
                Text = cand.Text,
                MeshGlyphs = cand.MeshGlyphs,
                OwnAlpha = cand.OwnAlpha,
                CrAlpha = cand.CrAlpha,
                InheritedAlpha = cand.InheritedAlpha,
                GroupAlpha = cand.GroupAlpha,
                GroupNote = cand.GroupNote ?? string.Empty,
            };
            int compIndex = c.Comps.Count;
            comp.FirstGlyph = c.Glyphs.Count;
            float sumW = 0f, sumH = 0f;
            for (int g = cand.First; g < cand.First + cand.Count; g++)
            {
                InkGlyph gl = InkCandidates[g];
                // CROPPED BY THE CAPTURE FRAME comes FIRST and is its own count. The frame is the
                // camera's viewport, so [0, RtW] x [0, RtH] IS the frame in texel space and a quad
                // outside it is content the capture legitimately cannot hold — never a defect. Before
                // ModBuild 206 this was added to the outside-the-strip total, where it read as a
                // budget outcome rather than as a crop. See InkCensus.GlyphsOutsideFrame.
                if (gl.X0 < 0f || gl.X1 > e.RtW || gl.Y0 < 0f || gl.Y1 > e.RtH)
                {
                    c.GlyphsOutsideFrame++;
                    continue;
                }
                if (gl.X0 < stripX || gl.X1 > stripX + stripW)
                {
                    c.GlyphsOutsideStrip++;
                    continue;
                }
                if (c.Glyphs.Count >= MaxInkCensusGlyphs)
                {
                    c.GlyphsSkippedCap++;
                    continue;
                }
                gl.Comp = compIndex;
                c.Glyphs.Add(gl);
                comp.InStrip++;
                sumW += gl.X1 - gl.X0;
                sumH += gl.Y1 - gl.Y0;
            }
            comp.GlyphCount = comp.InStrip;
            if (comp.InStrip > 0)
            {
                // THE SEARCH RADII, from this component's OWN quads. A glyph advance is a little wider
                // than a glyph box (side bearings), and a line height is well above a glyph box, so
                // both are scaled up from the means rather than taken raw — the search window has to
                // CONTAIN the displacement being hunted or the fit sits on the window boundary and is
                // reported ambiguous, which is a silent way to answer nothing.
                comp.AdvanceX = Mathf.Max(sumW / comp.InStrip * 1.2f, 4f);
                comp.LineY = Mathf.Max(sumH / comp.InStrip * 1.5f, 6f);
                c.Comps.Add(comp);
            }
        }
        InkCandidates.Clear();
        InkCandidateComps.Clear();

        if (c.Glyphs.Count == 0)
        {
            why = $"every one of this window's glyph quads fell OUTSIDE the {stripW}x{e.RtH}-texel "
                  + $"census strip at x={stripX} — BAND {c.BandIndex} of {c.BandCount}, authored x "
                  + $"{c.BandAuthoredLo:F0}..{c.BandAuthoredHi:F0}, chosen because {c.BandAimRule} — "
                  + $"({c.GlyphsOutsideStrip} excluded), or outside the "
                  + $"COMMITTED CAPTURE FRAME itself ({c.GlyphsOutsideFrame} — those are CROPPED and "
                  + "legitimately absent from the picture, which is a finding about the frame and not "
                  + "about the capture). The strip is "
                  + "full-height by construction (see MinInkStripTexels) and its WIDTH is what the "
                  + $"{MaxInkCensusTexels}-texel readback budget leaves against a {e.RtH}-texel-tall "
                  + "target, so this is a budget outcome and not a fault of the capture";
            return false;
        }
        return true;
    }

    /// <summary>The band that OWNS a level-0 texel x under the current overlapping tiling: the one
    /// whose strip is most nearly centred on it, which is the band most likely to hold a glyph there
    /// WHOLLY. With overlapping strips several bands can contain a given x and picking the
    /// best-centred one is what keeps the content histogram from marking a band as content-bearing
    /// when a neighbour already covers that text with room to spare.</summary>
    private static int BandOfTexelX(float texelX, int stripW, int stride, int bandCount)
        => Mathf.Clamp(Mathf.RoundToInt((texelX - stripW * 0.5f) / Mathf.Max(stride, 1)),
                       0, bandCount - 1);

    /// <summary>The level-0 x of a band's strip. Clamped into the target, so the last band or two
    /// overlap their predecessor rather than running off the edge.</summary>
    private static int BandStripX(int band, int stripW, int stride, int rtW)
        => Mathf.Clamp(band * stride, 0, Mathf.Max(0, rtW - stripW));

    /// <summary>
    /// <b>CHOOSE THIS CENSUS'S BAND, AND KEEP THE LEDGER THAT SAYS WHICH PARTS OF THE WINDOW HAVE EVER
    /// BEEN LOOKED AT (ModBuild 209).</b> Returns the level-0 <c>stripX</c>.
    ///
    /// <para>Called after the candidate walk and BEFORE the keep-what-falls-inside loop, which is what
    /// makes the content histogram free: <see cref="InkCandidates"/> already holds every drawable glyph
    /// quad of the WHOLE frame at this point, not just the strip's, so counting them per band costs one
    /// pass over a list that had to be built anyway.</para>
    ///
    /// <para>The rules, in the order they are tried, and all of them printed on the line:</para>
    /// <list type="number">
    /// <item>A one-shot AIM from the caller (<see cref="AimInkCensus"/>), unless the band it resolves
    /// to holds no glyph quad — an aim at blank margin is refused rather than honoured.</item>
    /// <item>Otherwise the ROAM: from the cursor, the first band that holds a glyph quad, wrapping.
    /// Empty bands are counted as skipped and named as such. A band is NEVER skipped for having read
    /// clean before — the complaint is intermittent, so a clean band has to keep being re-sampled.</item>
    /// <item>If not one band in the frame holds a glyph quad, the cursor's own band is taken and the
    /// downstream "every quad fell outside" refusal reports it honestly.</item>
    /// </list>
    /// </summary>
    private static int SelectInkBand(Entry e, InkCensus c, int stripW, int stride, int bandCount)
    {
        // ---- the tiling, and the ledger that indexes it -----------------------------------------
        // A re-allocation changes RtW or the strip width, and then band 3 of the old tiling and band 3
        // of the new one are different pieces of the window. Re-interpreting the ledger across that
        // would make the coverage claim a lie, so it is discarded and the discard is counted.
        if (c.CoverRtW != e.RtW || c.CoverStripW != stripW || c.BandCount != bandCount)
        {
            for (int b = 0; b < MaxInkBands; b++)
            {
                c.BandVisits[b] = 0;
                c.BandHasContent[b] = false;
                c.BandJudged[b] = 0;
                c.BandVerdict[b] = string.Empty;
                c.BandGlyphCursor[b] = 0;
            }
            if (c.CoverRtW != 0)
                c.CoverResets++;
            c.CoverRtW = e.RtW;
            c.CoverStripW = stripW;
            c.BandCursor = 0;
            c.BandIndex = 0;
        }
        c.BandCount = bandCount;
        c.BandW = stripW;
        c.BandStride = stride;

        // ---- which bands carry content, AS OF THIS CENSUS ----------------------------------------
        // Recomputed every time rather than accumulated, and that is what makes "the whole width has
        // been seen" reachable: a band whose text has since been destroyed or scrolled away would
        // otherwise sit in the coverage denominator for ever, never be selected (it is empty now) and
        // hold the claim open permanently. The question the line answers is "is the window AS IT
        // STANDS fully covered", and this is that question's denominator.
        //
        // Each quad is filed under the band whose strip is most nearly CENTRED on it, which under the
        // half-strip overlap is the band that holds it wholly if any does — so a band marked empty
        // here is 'blank margin or already covered by a neighbour', never 'nobody looks here'.
        for (int b = 0; b < bandCount; b++)
        {
            InkBandGlyphCount[b] = 0;
            c.BandHasContent[b] = false;
        }
        for (int i = 0; i < InkCandidates.Count; i++)
        {
            InkGlyph g = InkCandidates[i];
            if (g.X0 < 0f || g.X1 > e.RtW || g.Y0 < 0f || g.Y1 > e.RtH)
                continue;   // CROPPED by the committed frame — see InkCensus.GlyphsOutsideFrame.
            int b = BandOfTexelX((g.X0 + g.X1) * 0.5f, stripW, stride, bandCount);
            InkBandGlyphCount[b]++;
            c.BandHasContent[b] = true;
        }

        // ---- the one-shot aim -------------------------------------------------------------------
        int aimed = -1;
        string aimNote = string.Empty;
        if (c.AimComponent.Length > 0)
        {
            int best = -1;
            for (int i = 0; i < InkCandidateComps.Count; i++)
            {
                if (InkCandidateComps[i].Name.IndexOf(c.AimComponent,
                        System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (best < 0 || InkCandidateComps[i].Count > InkCandidateComps[best].Count)
                    best = i;
            }
            if (best >= 0)
            {
                aimed = BandOfTexelX(InkCandidateComps[best].CentreX, stripW, stride, bandCount);
                aimNote = $"AIMED BY NAME at '{InkCandidateComps[best].Name}' ("
                          + $"{InkCandidateComps[best].Count} glyph quad(s))";
            }
            else
            {
                aimNote = $"the aim at component '{c.AimComponent}' MISSED — this window's subtree has "
                          + "no censusable component of that name right now";
            }
        }
        else if (!float.IsNaN(c.AimAuthoredX))
        {
            float tx = (c.AimAuthoredX - e.Frame.xMin) * c.RateX;
            aimed = BandOfTexelX(tx, stripW, stride, bandCount);
            aimNote = $"AIMED BY POSITION at authored x={c.AimAuthoredX:F0} host-local uGUI px "
                      + $"(texel x={tx:F0})";
        }
        else if (c.AimBusiestOther)
        {
            int best = -1;
            for (int i = 0; i < InkCandidateComps.Count; i++)
            {
                if (BandOfTexelX(InkCandidateComps[i].CentreX, stripW, stride, bandCount) == c.BandIndex)
                    continue;
                if (best < 0 || InkCandidateComps[i].Count > InkCandidateComps[best].Count)
                    best = i;
            }
            if (best >= 0)
            {
                aimed = BandOfTexelX(InkCandidateComps[best].CentreX, stripW, stride, bandCount);
                aimNote = "AIMED at the BUSIEST COMPONENT NOT IN THE BAND JUST READ — '"
                          + InkCandidateComps[best].Name + $"', {InkCandidateComps[best].Count} glyph "
                          + $"quad(s), band {aimed} against band {c.BandIndex} last time";
            }
            else
            {
                aimNote = "the busiest-elsewhere aim found NO component outside the band just read, so "
                          + "there was nowhere else worth aiming at";
            }
        }

        // ---- aim, else roam ---------------------------------------------------------------------
        int chosen;
        int skipped = 0;
        string rule;
        if (aimed >= 0 && InkBandGlyphCount[aimed] > 0)
        {
            chosen = aimed;
            rule = aimNote + " — " + c.AimWhy;
        }
        else
        {
            if (aimed >= 0)
            {
                aimNote += ", but band " + aimed + " holds no glyph quad this census, so the aim was "
                           + "REFUSED rather than spent on blank margin";
            }
            chosen = -1;
            for (int k = 0; k < bandCount; k++)
            {
                int b = (c.BandCursor + k) % bandCount;
                if (InkBandGlyphCount[b] > 0)
                {
                    chosen = b;
                    break;
                }
                skipped++;
            }
            if (chosen < 0)
            {
                chosen = Mathf.Clamp(c.BandCursor, 0, bandCount - 1);
                skipped = 0;
                rule = $"the ROAM found NO band of the {bandCount} holding a single drawable glyph "
                       + $"quad, so it took the cursor's own band {chosen}; the refusal below says "
                       + "what that means";
            }
            else
            {
                rule = $"the ROAM: the cursor stood at band {c.BandCursor} and band {chosen} is the "
                       + $"first from there holding content ({skipped} empty band(s) stepped over)";
            }
            if (aimNote.Length > 0)
                rule = aimNote + "; " + rule;
        }

        c.BandsSkippedEmpty = skipped;
        c.BandSelected = true;
        c.BandIndex = chosen;
        c.BandCursor = (chosen + 1) % bandCount;
        c.BandAimRule = rule;

        // ONE SHOT. An aim forces the NEXT census and no others — a sticky aim would re-create the
        // fixed strip this whole change exists to remove.
        c.AimComponent = string.Empty;
        c.AimAuthoredX = float.NaN;
        c.AimBusiestOther = false;
        c.AimWhy = string.Empty;

        // The band's dominant component, kept so the SETTLED reading can be aimed back at this band
        // BY NAME thirty frames later, when a release re-fit may have moved every coordinate.
        c.BandDominantComp = string.Empty;
        int dom = -1;
        for (int i = 0; i < InkCandidateComps.Count; i++)
        {
            if (BandOfTexelX(InkCandidateComps[i].CentreX, stripW, stride, bandCount) != chosen)
                continue;
            if (dom < 0 || InkCandidateComps[i].Count > InkCandidateComps[dom].Count)
                dom = i;
        }
        if (dom >= 0)
            c.BandDominantComp = InkCandidateComps[dom].Name;

        return BandStripX(chosen, stripW, stride, e.RtW);
    }

    /// <summary>Record one text component's visible glyph quads in texel space. Reads the SUBMITTED
    /// MESH (<c>meshInfo[m].vertices</c>), not the layout record, and applies exactly the bounds test
    /// <see cref="ScanTmpMesh"/> uses — a quad that was never written into this generation is not a
    /// quad the picture can be missing.</summary>
    private static void CollectInkCandidate(Entry e, InkCensus c, RectTransform host, Rect frame,
                                            TMPro.TMP_Text t, Rect clip, bool clipEmpty)
    {
        if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text))
            return;
        // A component uGUI/TMP has switched off at the renderer draws nothing LEGITIMATELY, and
        // counting its glyphs as EMPTY would manufacture the very verdict this instrument exists to
        // decide. Same three disjoint reasons NoteRendererState uses, collapsed here because the
        // census only needs "does it draw at all" — the split is already reported by that field.
        CanvasRenderer cr = t.canvasRenderer;
        if (cr == null)
        {
            c.ComponentsNotDrawn++;
            return;
        }
        float ownAlpha = t.color.a;
        float crAlpha = cr.GetAlpha();
        float inheritedAlpha = cr.GetInheritedAlpha();
        bool rendererHides = cr.cull || crAlpha <= InkAlphaFloor || inheritedAlpha <= InkAlphaFloor
                             || ownAlpha <= InkAlphaFloor;

        // ---- THE CANVASGROUP CHAIN, MEASURED DIRECTLY (ModBuild 207) -----------------------------
        // The check that decides whether anything this census has found is real. See
        // InkComponent.GroupAlpha for why it does NOT trust CanvasRenderer.GetInheritedAlpha(): that
        // value is maintained by the canvas during its render pass, and a census built on it inherits
        // whatever it is blind to. The ModBuild 204 split measured 155-216 of 216 text components on
        // THIS window at inherited alpha 0 — so the population that could be wrongly censused is most
        // of the window, and any disagreement between the two tests is a defect in this instrument
        // that would show up as EMPTY glyphs, which is the one answer it must not invent.
        float groupAlpha = InkGroupChainAlpha(t.transform, host, out string groupNote);
        bool groupHides = groupAlpha <= InkAlphaFloor;
        if (groupHides)
        {
            c.ComponentsHiddenByGroup++;
            // THE DECISIVE COUNTER: hidden by a group and NOT caught by the renderer test, i.e. a
            // component ModBuild 205/206 would have censused and counted as EMPTY.
            if (!rendererHides)
                c.ComponentsHiddenByGroupOnly++;
        }
        if (rendererHides || groupHides || clipEmpty)
        {
            c.ComponentsNotDrawn++;
            return;
        }
        TMPro.TMP_TextInfo? info = t.textInfo;
        if (info == null || info.characterInfo == null || info.meshInfo == null)
            return;

        Matrix4x4 toHost = host.worldToLocalMatrix * t.transform.localToWorldMatrix;

        // ---- CAN THE PARENT'S MATRIX SPEAK FOR THE SUB-MESHES? (ModBuild 206) --------------------
        // Every glyph whose materialReferenceIndex is above 0 is drawn by a TMP_SubMeshUI on a CHILD
        // GameObject with its own transform, and this mapping uses the PARENT's matrix for all of
        // them. TMP creates those children matching the parent's rect, so the matrices agree — but
        // "so it should" is exactly the assumption that has cost this project two instruments. If any
        // sub-mesh child's local transform is not identity, the m > 0 glyphs are EXCLUDED and counted
        // rather than mapped through the wrong matrix: a mismapped glyph reports EMPTY, which is the
        // one answer this instrument must not be able to invent.
        bool subMeshesAligned = true;
        for (int i = 0; i < t.transform.childCount; i++)
        {
            Transform child = t.transform.GetChild(i);
            if (child == null || child.GetComponent<TMPro.TMP_SubMeshUI>() == null)
                continue;
            if (child.localPosition.sqrMagnitude > 1e-8f
                || Quaternion.Angle(child.localRotation, Quaternion.identity) > 0.01f
                || (child.localScale - Vector3.one).sqrMagnitude > 1e-8f)
            {
                subMeshesAligned = false;
                break;
            }
        }

        int first = InkCandidates.Count;
        int meshGlyphs = 0;
        float sumX = 0f;
        int n = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < n; i++)
        {
            TMPro.TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || char.IsWhiteSpace(ci.character) || char.IsControl(ci.character))
                continue;
            int m = ci.materialReferenceIndex;
            if (m < 0 || m >= info.meshInfo.Length)
                continue;
            if (m > 0 && !subMeshesAligned)
            {
                c.GlyphsSubMeshTransform++;
                continue;
            }
            TMPro.TMP_MeshInfo mi = info.meshInfo[m];
            Vector3[] verts = mi.vertices;
            int v = ci.vertexIndex;
            if (verts == null || v < 0 || v + 3 >= mi.vertexCount || v + 3 >= verts.Length)
                continue;
            Vector3 p0 = verts[v], p1 = verts[v + 1], p2 = verts[v + 2], p3 = verts[v + 3];
            if (!Finite(p0) || !Finite(p1) || !Finite(p2) || !Finite(p3))
                continue;
            meshGlyphs++;
            if (InkCandidates.Count - first >= MaxInkGlyphsPerComponent)
            {
                c.GlyphsSkippedCap++;
                continue;
            }

            Vector3 h0 = toHost.MultiplyPoint3x4(p0);
            Vector3 h1 = toHost.MultiplyPoint3x4(p1);
            Vector3 h2 = toHost.MultiplyPoint3x4(p2);
            Vector3 h3 = toHost.MultiplyPoint3x4(p3);
            float minX = Mathf.Min(Mathf.Min(h0.x, h1.x), Mathf.Min(h2.x, h3.x));
            float maxX = Mathf.Max(Mathf.Max(h0.x, h1.x), Mathf.Max(h2.x, h3.x));
            float minY = Mathf.Min(Mathf.Min(h0.y, h1.y), Mathf.Min(h2.y, h3.y));
            float maxY = Mathf.Max(Mathf.Max(h0.y, h1.y), Mathf.Max(h2.y, h3.y));

            // CLIPPED GLYPHS ARE NOT MISSING GLYPHS — see InkCensus.GlyphsClipped. The test is
            // WHOLLY-INSIDE rather than intersecting on purpose: a glyph half-clipped by a viewport
            // edge would read as partial ink and there is no honest verdict for it, so it is excluded
            // and counted like any other exclusion.
            if (minX < clip.xMin || maxX > clip.xMax || minY < clip.yMin || maxY > clip.yMax)
            {
                c.GlyphsClipped++;
                continue;
            }

            var g = new InkGlyph
            {
                Ch = ci.character,
                X0 = (minX - frame.xMin) * c.RateX,
                X1 = (maxX - frame.xMin) * c.RateX,
                Y0 = (minY - frame.yMin) * c.RateY,
                Y1 = (maxY - frame.yMin) * c.RateY,
            };
            // A quad under two texels on either axis cannot be judged: its interior after the inset is
            // a single texel and the ring around it overlaps its own ink. Excluded, and it counts as
            // outside the census rather than as a finding.
            if (g.X1 - g.X0 < 2f || g.Y1 - g.Y0 < 2f)
            {
                c.GlyphsOutsideStrip++;
                continue;
            }
            sumX += (g.X0 + g.X1) * 0.5f;
            InkCandidates.Add(g);
        }

        int count = InkCandidates.Count - first;
        if (count <= 0)
            return;
        InkCandidateComps.Add(new InkCandidateComp
        {
            Name = t.gameObject.name,
            Text = t.text,
            MeshGlyphs = meshGlyphs,
            First = first,
            Count = count,
            CentreX = sumX / count,
            OwnAlpha = ownAlpha,
            CrAlpha = crAlpha,
            InheritedAlpha = inheritedAlpha,
            GroupAlpha = groupAlpha,
            GroupNote = groupNote,
        });
    }

    /// <summary>
    /// The product of every <see cref="CanvasGroup"/> alpha between <paramref name="from"/> and
    /// <paramref name="host"/> inclusive, measured by walking the transform chain — independently of
    /// <c>CanvasRenderer.GetInheritedAlpha()</c>, which is what makes it evidence rather than a
    /// restatement.
    /// <para>A group with <c>ignoreParentGroups</c> TERMINATES the walk, because that is exactly what
    /// uGUI does with it; a disabled group contributes nothing, for the same reason. Both are read off
    /// the component rather than assumed, and the groups found are named in
    /// <paramref name="note"/> with their alphas so a surprising product can be traced to an object
    /// instead of argued about.</para>
    /// </summary>
    /// <summary>The nearest enabled <see cref="CanvasGroup"/> between <paramref name="from"/> and
    /// <paramref name="host"/> inclusive — the one whose alpha the ModBuild 211 repair nudges to make
    /// the canvas re-propagate. Null when the chain holds none, which is itself reported.
    /// <para>The walk terminates on <c>ignoreParentGroups</c> for the same reason
    /// <see cref="InkGroupChainAlpha"/> does: that is where uGUI stops accumulating, so a group above
    /// it does not own this graphic's alpha and nudging it would repair nothing.</para></summary>
    private static CanvasGroup? NearestGroup(Transform from, RectTransform host)
    {
        Transform? cur = from;
        int guard = 0;
        while (cur != null && ++guard < 64)
        {
            var cg = cur.GetComponent<CanvasGroup>();
            if (cg != null && cg.enabled)
                return cg;
            if (cg != null && cg.ignoreParentGroups)
                return null;
            if (ReferenceEquals(cur, host))
                break;
            cur = cur.parent;
        }
        return null;
    }

    private static float InkGroupChainAlpha(Transform from, RectTransform host, out string note)
    {
        note = string.Empty;
        float alpha = 1f;
        int named = 0;
        InkGroupSb.Length = 0;
        Transform? t = from;
        // Bounded by the hierarchy depth and by the host, and by a hard step count in case a future
        // re-parent ever puts the host outside this component's ancestry.
        for (int guard = 0; t != null && guard < 64; guard++)
        {
            var group = t.GetComponent<CanvasGroup>();
            if (group != null && group.enabled)
            {
                alpha *= Mathf.Clamp01(group.alpha);
                if (named < MaxInkGroupsNamed)
                {
                    named++;
                    if (InkGroupSb.Length > 0)
                        InkGroupSb.Append(", ");
                    InkGroupSb.Append(t.name).Append(' ').Append(group.alpha.ToString("F3"));
                }
                if (group.ignoreParentGroups)
                    break;
            }
            if (ReferenceEquals(t, host))
                break;
            t = t.parent;
        }
        note = InkGroupSb.Length > 0 ? InkGroupSb.ToString() : "no CanvasGroup between it and the host";
        InkGroupSb.Length = 0;
        return alpha;
    }

    /// <summary>
    /// The readback landed — possibly several frames after it was requested, possibly after the target
    /// it was taken from was re-allocated, possibly with an error. All three are handled here and all
    /// three are COUNTED, so no outcome of this instrument can be silent.
    /// </summary>
    private static void OnInkRead(string window, int gen, int plane, AsyncGPUReadbackRequest req)
    {
        try
        {
            // The mod-wide budget is charged ONCE PER CENSUS from ModBuild 208 and is released by
            // EndInkCensus on every exit — including StandDown, which is why a window that died while
            // this was in flight does not leak it here.
            if (!InkCensuses.TryGetValue(window, out InkCensus c))
                return; // the window stood down while this was in flight; its state died with it
            if (!c.InFlight || c.Gen != gen)
                return; // superseded
            InkPlane p = c.Planes[plane];
            if (p.Landed || p.Failed)
                return; // a duplicate callback for a plane already accounted for
            c.PlanesOutstanding--;
            Entry? e = EntryForWindow(window);
            if (e == null)
            {
                c.DroppedStale++;
                EndInkCensus(c);
                return;
            }
            if (req.hasError)
            {
                // ONE plane failing is not the census failing. It is recorded, the line names it, and
                // the remaining planes still answer — a mip 2 readback the driver refused must not
                // silently become "mip 2 lost every glyph".
                p.Failed = true;
                p.FailWhy = "the AsyncGPUReadback came back with request.hasError — the GPU refused or "
                            + "aborted the copy, so NOTHING may be concluded about this plane in "
                            + "either direction";
                c.PlanesFailed++;
                c.Errors++;
                FinishInkPlane(e, c);
                return;
            }
            RenderTexture shown = DisplayTexture(e);
            if (shown == null || shown.GetInstanceID() != c.TargetId
                || e.RtW != c.RtW || e.RtH != c.RtH
                || (p.FromCapture && (e.Rt == null || e.Rt.GetInstanceID() != c.CaptureId)))
            {
                // The mesh positions were mapped through the frame and target that existed when the
                // request went out; a re-allocation between then and now makes every texel coordinate
                // in this result describe a texture that no longer exists. Dropped, and said so.
                c.DroppedStale++;
                ReportInkUnanswerable(e, c, c.Reason,
                    "the render target was RE-ALLOCATED between the request and its delivery (was "
                    + $"{c.RtW}x{c.RtH} id {c.TargetId}, now "
                    + $"{(shown != null ? shown.width : 0)}x{(shown != null ? shown.height : 0)} id "
                    + $"{(shown != null ? shown.GetInstanceID() : 0)}), so every texel coordinate in "
                    + "this result describes a texture that no longer exists and the result is DROPPED "
                    + "rather than mapped onto the new one");
                return;
            }
            // ModBuild 206: THE FRAME ITSELF, not only the target. The capture frame can be re-committed
            // WITHOUT a re-allocation — the hysteresis commits a new overspill inside RectChangeFraction
            // and the target keeps its pixel count — and the mapping subtracts Frame.xMin/yMin, so a
            // frame that moved by one 32 px quantum re-maps every glyph in the census by 80 texels at
            // the shipped rate while every check above still passes. The frame is quantised and damped,
            // so it is either identical or a whole quantum away; there is no near-miss to tolerate.
            Rect liveFrame = e.Frame;
            if (Mathf.Abs(liveFrame.xMin - c.FrameAtRequest.xMin) > 0.01f
                || Mathf.Abs(liveFrame.yMin - c.FrameAtRequest.yMin) > 0.01f
                || Mathf.Abs(liveFrame.width - c.FrameAtRequest.width) > 0.01f
                || Mathf.Abs(liveFrame.height - c.FrameAtRequest.height) > 0.01f)
            {
                c.DroppedFrameMoved++;
                ReportInkUnanswerable(e, c, c.Reason,
                    "the COMMITTED CAPTURE FRAME moved between the request and its delivery (was "
                    + $"[{c.FrameAtRequest.xMin:F1},{c.FrameAtRequest.yMin:F1} "
                    + $"{c.FrameAtRequest.width:F0}x{c.FrameAtRequest.height:F0}], now "
                    + $"[{liveFrame.xMin:F1},{liveFrame.yMin:F1} "
                    + $"{liveFrame.width:F0}x{liveFrame.height:F0}] host-local uGUI px) WITHOUT a "
                    + "re-allocation — the target kept its pixel count, so every other staleness check "
                    + "passed. The mapping subtracts the frame's origin, so this result is stale by "
                    + "construction and is DROPPED rather than judged; a frame that moves between "
                    + "request and delivery is itself worth reading, because it is the same "
                    + "re-mapping that would displace the picture the eye sees");
                return;
            }
            Unity.Collections.NativeArray<Color32> data = req.GetData<Color32>();
            long need = (long)p.W * p.H;
            if (data.Length < need)
            {
                p.Failed = true;
                p.FailWhy = $"the readback delivered {data.Length} texel(s) against the {need} asked "
                            + $"for ({p.W}x{p.H}), so the buffer cannot be indexed safely";
                c.PlanesFailed++;
                c.Errors++;
                FinishInkPlane(e, c);
                return;
            }
            float started = Time.realtimeSinceStartup;
            bool answered = EvaluateInkPlane(e, c, p, data, out string? why);
            p.JudgeMs = (Time.realtimeSinceStartup - started) * 1000.0;
            p.DeliveredFrame = Time.frameCount;
            c.JudgeMsTotal += p.JudgeMs;
            if (p.JudgeMs > c.JudgeMsWorst)
                c.JudgeMsWorst = p.JudgeMs;
            if (!answered)
            {
                // The ORIENTATION SELF-CHECK on the MIP 0 plane is the only thing that can refuse
                // here, and it refuses for the whole census rather than for one plane: a census that
                // could not establish which way up the buffer is would read the mirrored row of every
                // glyph on every plane, and the level comparison would then be a comparison of two
                // wrong answers.
                ReportInkUnanswerable(e, c, c.Reason, why!);
                return;
            }
            if (!p.Failed)
            {
                p.Landed = true;
                c.PlanesLanded++;
            }
            FinishInkPlane(e, c);
        }
        catch (System.Exception ex)
        {
            if (InkCensuses.TryGetValue(window, out InkCensus c2))
            {
                c2.Threw++;
                ReportInkUnanswerable(EntryForWindow(window), c2, c2.Reason,
                    $"the census EVALUATION threw ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }

    /// <summary>
    /// One plane is accounted for. When the last one is, do the CROSS-PLANE work — which is the whole
    /// of ModBuild 208 — and report.
    /// <para>The neighbourhood search happens here rather than in the mip 0 callback for a reason
    /// worth stating: it needs the mip 0 buffer, which is dead by now, so it does NOT run at this
    /// point. It ran (or was gated out) inside <see cref="EvaluateInkPlane"/> while its buffer was
    /// alive. This method only assembles what the planes found.</para>
    /// </summary>
    private static void FinishInkPlane(Entry e, InkCensus c)
    {
        if (c.PlanesOutstanding > 0)
            return;
        c.DeliveredFrame = Time.frameCount;
        c.ReadMs = c.JudgeMsTotal + c.SearchMs;

        InkPlane mip0 = c.Planes[InkPlaneMip0];
        if (!mip0.Landed)
        {
            ReportInkUnanswerable(e, c, c.Reason,
                "the MIP 0 plane — the one every ModBuild 205-207 census read, and the reference every "
                + "other plane is compared against — did not land ("
                + (mip0.FailWhy.Length > 0 ? mip0.FailWhy : "no reason recorded")
                + "), so there is no baseline to compare the mip levels to and nothing may be "
                + "concluded about the chain in either direction");
            return;
        }

        CompareInkPlanes(c);
        BuildMappingVerdict(c);
        BuildMipVerdict(c);
        BuildBlitVerdict(c);

        // THE CURSOR ADVANCES BY THE LEAST ANY LANDED PLANE COVERED, never by the most. A plane that
        // got further than its siblings still only contributes to the level comparison over the range
        // they all reached, so advancing past that would leave a band of glyphs no census ever
        // compares — which is exactly the silent truncation this whole region forbids.
        int covered = int.MaxValue;
        for (int i = 0; i < c.Planes.Length; i++)
        {
            InkPlane p = c.Planes[i];
            if (p.Landed && p.Judged + p.BelowFloor < covered)
                covered = p.Judged + p.BelowFloor;
        }
        if (covered == int.MaxValue || covered <= 0)
            covered = 0;
        c.GlyphCursor = c.Glyphs.Count > 0
            ? (c.CensusCursor + covered) % c.Glyphs.Count
            : 0;
        // Filed against the band that was actually read — a deferral is "the rest of THIS band".
        if (c.BandSelected && c.BandIndex >= 0 && c.BandIndex < MaxInkBands)
            c.BandGlyphCursor[c.BandIndex] = c.GlyphCursor;
        c.LastMipLost = c.MipLost1 + c.MipLost2;

        c.Completed++;
        ReportInkCensus(e, c);
    }

    /// <summary>
    /// Judge every censused glyph against ONE delivered plane. Returns false — with a reason — when
    /// the orientation self-check cannot decide which way up the buffer is, because a census that
    /// reads the mirrored row of every glyph would produce a confident, wrong answer.
    ///
    /// <para><b>EVERY TEXEL COORDINATE HERE IS THE LEVEL-0 ONE TIMES <see cref="InkPlane.Scale"/>.</b>
    /// The glyph list is built once, in level-0 texels, and each plane scales it — so the four planes
    /// are by construction judging THE SAME GLYPHS in the same places, which is the only way the
    /// level comparison means anything.</para>
    ///
    /// <para><b>THE BUDGET (ModBuild 208).</b> The per-glyph loop stops after
    /// <see cref="InkJudgeBudgetMs"/> and carries the rest to the next census. Every plane of one
    /// census starts at the SAME cursor (<see cref="InkCensus.CensusCursor"/>) so that a budget
    /// overrun narrows the compared set rather than misaligning it.</para>
    /// </summary>
    private static bool EvaluateInkPlane(Entry e, InkCensus c, InkPlane p,
                                         Unity.Collections.NativeArray<Color32> data, out string? why)
    {
        why = null;
        int w = p.W, h = p.H;
        int n = c.Glyphs.Count;
        bool isMip0 = p.Index == InkPlaneMip0;
        // THE PLANE'S DEADLINE STARTS HERE, not at the glyph loop. The orientation profile is a fixed
        // prologue of up to InkMaxProfileSamples texel reads and at ModBuild 207's measured ~140 ns a
        // read it is the single most expensive thing in this method; a budget that started after it
        // would be a budget in name only.
        float started = Time.realtimeSinceStartup;

        // ---- the strip's own background level, as a MEDIAN over a coarse grid -------------------
        InkScratch.Clear();
        int gridStride = Mathf.Max(16, Mathf.Max(w, h) / 64);
        for (int y = gridStride / 2; y < h && InkScratch.Count < 4096; y += gridStride)
        {
            for (int x = gridStride / 2; x < w && InkScratch.Count < 4096; x += gridStride)
                InkScratch.Add(InkValue(data[y * w + x]));
        }
        p.Background = InkMedian(InkScratch);

        // ---- THE ORIENTATION SELF-CHECK ---------------------------------------------------------
        // AsyncGPUReadback returns the source texture's own layout and Unity does not flip it, so
        // whether row 0 is the bottom or the top of the image is a property of the graphics API and
        // must be MEASURED, never assumed. See MinInkStripTexels for why the strip is full height (it
        // makes the placement identical under both conventions, leaving only the row order).
        //
        // ModBuild 208 runs it PER PLANE rather than once. The row order is a property of the API and
        // ought to be identical on all four, so a plane that disagrees with its siblings is itself a
        // finding — and a plane that cannot decide on its own borrows the census's decision rather
        // than guessing, saying so on the line.
        for (int b = 0; b < InkBands; b++)
        {
            InkBandPredicted[b] = 0f;
            InkBandMeasured[b] = 0f;
        }
        for (int i = 0; i < n; i++)
        {
            InkGlyph g = c.Glyphs[i];
            float centre = (g.Y0 + g.Y1) * 0.5f * p.Scale;
            int band = Mathf.Clamp((int)(centre * InkBands / h), 0, InkBands - 1);
            InkBandPredicted[band] += (g.X1 - g.X0) * (g.Y1 - g.Y0) * p.Scale * p.Scale;
        }
        // DECIDE ONCE PER WINDOW, CONFIRM CHEAPLY AFTER THAT — see InkConfirmProfileSamples. The
        // deciding sweep is ModBuild 207's exactly (fixed stride 4, ~125k texel reads, ~18 ms of the
        // 23 that log reported) and it runs on the FIRST census only; every census after it pays 2,500
        // samples for a cross-check that never gates anything.
        p.Confirming = c.OrientationDecided;
        int profileStride = p.Confirming
            ? Mathf.Max(InkProfileStride,
                        Mathf.CeilToInt(Mathf.Sqrt((float)w * h / InkConfirmProfileSamples)))
            : InkProfileStride;
        p.ProfileStride = profileStride;
        for (int y = 0; y < h; y += profileStride)
        {
            int band = Mathf.Clamp(y * InkBands / h, 0, InkBands - 1);
            int row = y * w;
            for (int x = 0; x < w; x += profileStride)
            {
                if (Mathf.Abs(InkValue(data[row + x]) - p.Background) >= InkThreshold)
                    InkBandMeasured[band] += 1f;
            }
        }
        p.CorrAsIs = InkCorrelation(InkBandPredicted, InkBandMeasured, false);
        p.CorrFlip = InkCorrelation(InkBandPredicted, InkBandMeasured, true);
        float best = Mathf.Max(p.CorrAsIs, p.CorrFlip);
        float margin = Mathf.Abs(p.CorrAsIs - p.CorrFlip);
        p.Decided = best >= InkOrientMinCorrelation && margin >= InkOrientMinMargin;

        if (p.Confirming)
        {
            // THE STORED DECISION GOVERNS. This plane's own profile is 2,500 samples — deliberately
            // too weak to gate a census and quite strong enough to catch a flipped buffer, which is a
            // total inversion of the profile and not a marginal shift.
            p.Flipped = c.Flipped;
            if (p.Decided && (p.CorrFlip > p.CorrAsIs) != c.Flipped)
            {
                // A CONFIRMATION THAT DECIDED THE OTHER WAY IS A FINDING, NOT A TIE-BREAK. Two planes
                // of one texture cannot honestly disagree about row order, so one of the two readings
                // is wrong and this instrument cannot say which. It refuses.
                string clash = "ITS ORIENTATION CONFIRMATION DISAGREES WITH THE DECISION THIS WINDOW "
                               + "ALREADY MADE: the stored row order is "
                               + (c.Flipped ? "row 0 = TOP" : "row 0 = BOTTOM")
                               + $" and this plane's own profile says the opposite ({p.CorrAsIs:F2} as "
                               + $"delivered against {p.CorrFlip:F2} reversed). Two planes of one "
                               + "texture cannot honestly disagree about which way up a readback "
                               + "buffer is, so ONE of the two readings is wrong and nothing here can "
                               + "say which";
                if (!isMip0)
                {
                    p.Failed = true;
                    p.FailWhy = clash + ", so this plane contributes NOTHING rather than a possibly "
                                + "mirrored reading";
                    c.PlanesFailed++;
                    return true;
                }
                c.Orientation = "CONTRADICTED";
                why = clash + ". The census is DROPPED rather than judged on either of them, and the "
                      + "next reading re-derives the row order from scratch";
                c.OrientationDecided = false;
                return false;
            }
        }
        else if (!p.Decided)
        {
            if (!isMip0)
            {
                p.Failed = true;
                p.FailWhy = "its own orientation self-check could not decide which way up the buffer "
                            + $"is (correlation {p.CorrAsIs:F2} as delivered against {p.CorrFlip:F2} "
                            + $"reversed, needing {InkOrientMinCorrelation:F2} and a "
                            + $"{InkOrientMinMargin:F2} margin) and no plane had decided yet, so it "
                            + "contributes NOTHING rather than a mirrored reading";
                c.PlanesFailed++;
                return true;
            }
            c.Orientation = "UNDECIDED";
            why = "THE ORIENTATION SELF-CHECK COULD NOT DECIDE WHICH WAY UP THE READBACK BUFFER IS. "
                  + "AsyncGPUReadback returns the source texture's own layout and Unity does not flip "
                  + "it, so row 0 is the bottom of the image on some graphics APIs and the top on "
                  + "others; a census that guesses reads the MIRRORED row of every glyph and answers "
                  + "confidently and wrongly. The check correlates the ink bands the MESH predicts "
                  + $"against the ink bands actually measured, as delivered ({p.CorrAsIs:F2}) and "
                  + $"reversed ({p.CorrFlip:F2}), over {InkBands} bands of the strip; it needs a best "
                  + $"correlation of at least {InkOrientMinCorrelation:F2} and a margin of at least "
                  + $"{InkOrientMinMargin:F2} and got {best:F2} / {margin:F2}. THE TWO READINGS THAT "
                  + "PRODUCE THIS: the censused text is confined to one horizontal band (nothing to "
                  + "correlate), or the strip carries almost no ink at all — and the SECOND of those "
                  + "is itself a finding, so read the measured-band total on the next line before "
                  + "treating this as a mere instrument failure";
            return false;
        }
        else
        {
            // THE DECIDING SWEEP, once per window: ModBuild 207's full-strength profile, and it fixes
            // the row order for the session.
            p.Flipped = p.CorrFlip > p.CorrAsIs;
            c.OrientationDecided = true;
            c.Flipped = p.Flipped;
            c.CorrAsIs = p.CorrAsIs;
            c.CorrFlip = p.CorrFlip;
            c.Orientation = (p.Flipped
                ? $"row 0 of the readback is the TOP of the image (mesh-vs-image band correlation "
                  + $"{p.CorrFlip:F2} reversed against {p.CorrAsIs:F2} as delivered), so every glyph is "
                  + "sampled from the mirrored row"
                : $"row 0 of the readback is the BOTTOM of the image (mesh-vs-image band correlation "
                  + $"{p.CorrAsIs:F2} as delivered against {p.CorrFlip:F2} reversed), i.e. the same "
                  + "orientation the orthographic capture camera's viewport uses")
                + $" — DECIDED by the full-strength sweep on the {p.Short} plane and then held for "
                + "this window, because row order is a property of the graphics API and the texture "
                + "layout and does not change between frames. ModBuild 207 re-derived it every census "
                + "and that sweep was ~18 ms of its 23 ms cost; every census after this one pays "
                + $"{InkConfirmProfileSamples} samples for a CONFIRMATION that never gates anything "
                + "and refuses the census outright if it ever decides the other way";
        }

        // ---- the per-glyph verdict, AND the ink centroid that judges the mapping -----------------
        InkDevInk.Clear();
        InkDevEmpty.Clear();
        if (isMip0)
        {
            for (int i = 0; i < c.Comps.Count; i++)
            {
                InkComponent reset = c.Comps[i];
                reset.Ink = 0;
                reset.Empty = 0;
                reset.Judged = 0;
                reset.BelowFloor = 0;
                reset.EmptyChars = string.Empty;
                reset.EmptyNotNamed = 0;
                reset.Absent = 0;
                reset.FoundOffset = 0;
                reset.Ambiguous = 0;
                reset.EmptyNearStripEdge = 0;
                reset.FitDx = 0f;
                reset.FitDy = 0f;
                reset.FitPassAtBest = 0;
                reset.FitPassAtZero = 0;
                reset.ScoreGlyphs = 0;
                reset.FitNote = string.Empty;
                reset.SpanX = 0f;
                reset.SpanY = 0f;
                reset.FlatField = false;
            }
            c.TotalGlyphs = n;
            c.TotalInk = 0;
            c.TotalEmpty = 0;
            c.CentroidCount = 0;
            c.CentroidDxSum = 0;
            c.CentroidDySum = 0;
            c.CentroidDxLow = float.MaxValue;
            c.CentroidDxHigh = float.MinValue;
            c.CentroidDyLow = float.MaxValue;
            c.CentroidDyHigh = float.MinValue;
        }

        p.JudgeFrom = c.CensusCursor;
        p.ProfileMs = (Time.realtimeSinceStartup - started) * 1000.0;
        ChargeInkBudget(p.ProfileMs);

        // THIS PLANE'S SHARE OF THE FRAME. The four planes of a census can all land on the SAME frame
        // — AsyncGPUReadback drains its completed queue per frame and they were all requested on one —
        // so a per-plane ceiling alone would have bounded a quarter of the problem. The share is what
        // is left this frame divided by the planes still to come, capped at InkJudgeBudgetMs and
        // floored at InkMinStageBudgetMs so a late plane still measures SOMETHING: a plane that
        // measured nothing contributes nothing to the level comparison and would shrink its
        // denominator to zero without ever saying so.
        double budgetMs = InkStageBudgetMs(c.PlanesOutstanding + (isMip0 ? 1 : 0), InkJudgeBudgetMs);
        p.BudgetMs = budgetMs;
        float glyphStart = Time.realtimeSinceStartup;
        int processed = 0;
        for (int k = 0; k < n; k++)
        {
            // THE BUDGET, checked every InkBudgetCheckStride glyphs. Time.realtimeSinceStartup is a
            // call into the engine, so reading it per glyph would be a measurable share of the budget
            // it is protecting.
            if (k > 0 && (k % InkBudgetCheckStride) == 0
                && (Time.realtimeSinceStartup - glyphStart) * 1000.0 >= budgetMs)
            {
                p.Overran = true;
                break;
            }
            int i = (c.CensusCursor + k) % n;
            InkGlyph g = c.Glyphs[i];
            processed++;

            // ---- THE SIZE FLOOR — see InkMinQuadTexels. A correctly minified glyph is not a defect
            // and this instrument cannot judge one, so it is counted as BELOW THE FLOOR and is
            // neither INKED nor EMPTY. Without this, level 2 would report a broken mip chain on a
            // perfect one and the whole build would be an artefact of its own geometry.
            float qw = (g.X1 - g.X0) * p.Scale;
            float qh = (g.Y1 - g.Y0) * p.Scale;
            float minor = Mathf.Min(qw, qh);
            if (minor < InkMinQuadTexels)
            {
                p.BelowFloor++;
                if (isMip0 && g.Comp >= 0 && g.Comp < c.Comps.Count)
                    c.Comps[g.Comp].BelowFloor++;
                continue;
            }
            if (minor < p.SmallestJudged) p.SmallestJudged = minor;
            if (minor > p.LargestJudged) p.LargestJudged = minor;

            float bg = InkLocalBackground(data, p, g);
            p.Bg[i] = bg;
            float dev = InkQuadScore(data, p, g, 0f, 0f, InkInnerSamples, bg,
                                     out float cx, out float cy, out _);
            bool ink = dev >= InkThreshold;
            p.Measured[i] = true;
            p.Inked[i] = ink;
            p.Judged++;
            if (ink)
            {
                p.Ink++;
                InkDevInk.Add(dev);
            }
            else
            {
                p.Empty++;
                InkDevEmpty.Add(dev);
            }
            if (!isMip0)
                continue;

            if (ink)
            {
                c.TotalInk++;
                // REQUIREMENT 1: MEASURE THE OFFSET, DO NOT ASSUME IT IS ZERO. This is the cheap,
                // sub-texel estimate — where the ink actually sits inside a quad the census says has
                // ink. It carries the glyph's own shape asymmetry as well as any displacement, which
                // is why only the MEAN over the distribution is a statement about the mapping and why
                // the whole distribution is printed.
                c.CentroidCount++;
                c.CentroidDxSum += cx;
                c.CentroidDySum += cy;
                if (cx < c.CentroidDxLow) c.CentroidDxLow = cx;
                if (cx > c.CentroidDxHigh) c.CentroidDxHigh = cx;
                if (cy < c.CentroidDyLow) c.CentroidDyLow = cy;
                if (cy > c.CentroidDyHigh) c.CentroidDyHigh = cy;
            }
            else
            {
                c.TotalEmpty++;
            }
            if (g.Comp < 0 || g.Comp >= c.Comps.Count)
                continue;
            InkComponent comp = c.Comps[g.Comp];
            comp.Judged++;
            if (ink)
            {
                comp.Ink++;
            }
            else
            {
                comp.Empty++;
                if (comp.Empty <= MaxInkEmptyNamed)
                {
                    InkCharsSb.Length = 0;
                    InkCharsSb.Append(comp.EmptyChars);
                    if (InkCharsSb.Length > 0)
                        InkCharsSb.Append(',');
                    InkCharsSb.Append('\'').Append(g.Ch).Append('\'');
                    comp.EmptyChars = InkCharsSb.ToString();
                }
                else
                {
                    comp.EmptyNotNamed++;
                }
            }
        }
        p.Deferred = n - processed;
        ChargeInkBudget((Time.realtimeSinceStartup - glyphStart) * 1000.0);
        if (p.SmallestJudged > p.LargestJudged)
            p.SmallestJudged = 0f;

        InkScratch.Clear();
        InkScratch.AddRange(InkDevInk);
        p.MedDevInk = InkMedian(InkScratch);
        InkScratch.Clear();
        InkScratch.AddRange(InkDevEmpty);
        p.MedDevEmpty = InkMedian(InkScratch);

        if (!isMip0)
            return true;

        if (c.CentroidCount == 0)
            c.CentroidDxLow = c.CentroidDxHigh = c.CentroidDyLow = c.CentroidDyHigh = 0f;
        c.Background = p.Background;
        c.MedDevInk = p.MedDevInk;
        c.MedDevEmpty = p.MedDevEmpty;
        c.TotalJudged = p.Judged;
        c.TotalBelowFloor = p.BelowFloor;
        c.TotalDeferred = p.Deferred;

        // ---- THE ALPHA EVIDENCE FOR THE COMPONENTS THAT PRODUCED EMPTIES ------------------------
        BuildInkAlphaEvidence(c);
        // ---- THE NON-TEXT GRAPHICS (ModBuild 210) -----------------------------------------------
        // Here for the same reason the search is: it needs this plane's buffer. And it runs BEFORE the
        // search, because a blank plate is a whole missing element and a fitted glyph offset is a
        // refinement — if the frame budget can only afford one of them this session, it must be this.
        JudgeInkPlates(data, c, p);
        // ---- REQUIREMENT 2: SEARCH A NEIGHBOURHOOD BEFORE DECLARING EMPTY -----------------------
        // It runs HERE and nowhere else: it needs this plane's buffer, which dies with this callback.
        FitInkComponents(data, c, p);
        return true;
    }

    /// <summary>
    /// <b>THE CHECK THAT DECIDES WHETHER ANY OF THIS IS REAL.</b> For every component that produced an
    /// EMPTY glyph, record its own <c>Graphic.color.a</c>, its <c>CanvasRenderer</c> alpha, its
    /// inherited alpha and the product of every <see cref="CanvasGroup"/> between it and the host.
    ///
    /// <para>A component hidden by a CanvasGroup alpha 0 somewhere above it draws nothing CORRECTLY,
    /// and if this census's exclusion test and the ModBuild 204 renderer split disagree even slightly,
    /// some of them are being censused and counted as EMPTY — which would make the whole finding an
    /// artefact of this instrument. The population at risk is not hypothetical: the 204 split measured
    /// <b>155-216 of 216 text components on this very window at inherited alpha 0</b>, first named
    /// 'Gold Warning', and the components the 206 census named as EMPTY — 'Party Name', 'XP Amount
    /// Levelup', 'XP Amount', 'Level text' — belong to panels that may legitimately be hidden.</para>
    ///
    /// <para>So the numbers are PRINTED rather than asserted. If the empties are alpha-0 components,
    /// the verdict says the finding is an artefact in exactly those words. If they are all at 1.000,
    /// that is the proof that the empties are fully visible components whose ink is missing.</para>
    /// </summary>
    private static void BuildInkAlphaEvidence(InkCensus c)
    {
        c.EmptyLowestAlpha = 1f;
        c.LowestAlphaComp = string.Empty;
        c.EmptyOpaqueComps = c.EmptyOpaqueGlyphs = c.EmptyDimComps = c.EmptyDimGlyphs = 0;
        InkAlphaSb.Length = 0;

        // PASS 1 — the two disjoint populations, complete and uncapped (ModBuild 210). The counts are
        // what the verdict is now built on, so they may never be shortened by a naming cap the way the
        // evidence text is. OPAQUE is the population the user's complaint lives in: a component at full
        // alpha whose glyphs are not in the capture is a defect, whatever its neighbours are doing.
        for (int i = 0; i < c.Comps.Count; i++)
        {
            InkComponent comp = c.Comps[i];
            if (comp.Empty == 0)
                continue;
            float effective = comp.OwnAlpha * comp.CrAlpha * comp.InheritedAlpha * comp.GroupAlpha;
            if (effective >= InkOpaqueBar)
            {
                c.EmptyOpaqueComps++;
                c.EmptyOpaqueGlyphs += comp.Empty;
            }
            else
            {
                c.EmptyDimComps++;
                c.EmptyDimGlyphs += comp.Empty;
            }
            if (effective < c.EmptyLowestAlpha)
            {
                c.EmptyLowestAlpha = effective;
                c.LowestAlphaComp = $"'{comp.Name}' at effective {effective:F3}";
            }
        }

        // PASS 2 — the evidence text. OPAQUE COMPONENTS ARE NAMED FIRST, because they are the finding
        // and the cap used to spend itself on whatever came earliest in the walk.
        int named = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            bool wantOpaque = pass == 0;
            for (int i = 0; i < c.Comps.Count && named < MaxInkAlphaNamed; i++)
            {
                InkComponent comp = c.Comps[i];
                if (comp.Empty == 0)
                    continue;
                float effective = comp.OwnAlpha * comp.CrAlpha * comp.InheritedAlpha * comp.GroupAlpha;
                if (effective >= InkOpaqueBar != wantOpaque)
                    continue;
                named++;
                InkAlphaSb.Append(' ').Append(wantOpaque ? "[OPAQUE] " : "[FADED] ")
                          .Append('\'').Append(comp.Name).Append("': ").Append(comp.Empty)
                          .Append(" empty of ").Append(comp.Judged).Append(" judged; colour.a ")
                          .Append(comp.OwnAlpha.ToString("F3")).Append(", CanvasRenderer alpha ")
                          .Append(comp.CrAlpha.ToString("F3")).Append(", inherited alpha ")
                          .Append(comp.InheritedAlpha.ToString("F3"))
                          .Append(", CanvasGroup chain product ")
                          .Append(comp.GroupAlpha.ToString("F3")).Append(" (").Append(comp.GroupNote)
                          .Append("), effective ").Append(effective.ToString("F3")).Append('.');
            }
        }
        c.AlphaNote = InkAlphaSb.Length > 0
            ? InkAlphaSb.ToString()
            : " (no component produced an EMPTY glyph, so there is nothing to prove visible.)";
        InkAlphaSb.Length = 0;
    }

    /// <summary>
    /// <b>THE JOINT REGISTRATION SEARCH — one fitted offset per COMPONENT, not per glyph.</b>
    ///
    /// <para>The whole argument is in the MAPPING SELF-CHECK block above <see cref="InkSearchSteps"/>.
    /// In one sentence: a whole-string displacement is ONE number for the string, and fitting it per
    /// glyph would be degenerate — in running text a single glyph shifted by one advance lands on its
    /// neighbour, which is also ink, so every glyph would report a spurious match. Fitting the string
    /// as a whole breaks that, and where it does not (a best offset a whole advance or line away is a
    /// LATTICE ALIAS) the component is reported AMBIGUOUS rather than as a displacement.</para>
    ///
    /// <para>The score at an offset is HOW MANY of the component's glyphs pass the ink test there,
    /// using each glyph's OWN local background measured at its predicted position — the plate under a
    /// line of text is far larger than one glyph advance, so that background is still the right one a
    /// few texels away, and re-measuring a ring per offset would multiply the cost by twenty for no
    /// information.</para>
    /// </summary>
    private static void FitInkComponents(Unity.Collections.NativeArray<Color32> data, InkCensus c,
                                         InkPlane p)
    {
        c.EmptyAbsent = 0;
        c.EmptyFoundOffset = 0;
        c.EmptyAmbiguous = 0;
        c.EmptyNotSearched = 0;
        c.EmptyNearStripEdge = 0;
        c.FitCount = 0;
        c.FitDxSum = 0;
        c.FitDySum = 0;
        c.FitDxLow = float.MaxValue;
        c.FitDxHigh = float.MinValue;
        c.FitDyLow = float.MaxValue;
        c.FitDyHigh = float.MinValue;
        c.FitLatticeAlias = 0;
        c.FitNoFit = 0;
        InkFitSb.Length = 0;

        c.AliasCount = 0;
        c.AliasDxSum = 0;
        c.AliasDySum = 0;
        c.SearchRan = false;
        c.SearchComponents = 0;
        c.SearchDeferredComps = 0;
        c.SearchOverran = false;
        c.SearchMs = 0.0;
        int searched = 0;
        int named = 0;

        // ---- THE GATE (ModBuild 208), and it is most of the cost fix -----------------------------
        // The search exists for ONE purpose: to decide whether a glyph EMPTY at its predicted place is
        // absent or displaced. It has nothing to say about a glyph that HAS ink at mip 0, and it has
        // nothing to add once the level comparison has located the loss in the mip chain — at that
        // point the next round works on GenerateMips and not on a registration offset.
        //
        // The gate reads the PREVIOUS census's comparison rather than this one's, and that is forced
        // rather than sloppy: this search needs the mip 0 buffer, the comparison needs the mip 1/2
        // buffers, and no two readback buffers are ever alive at the same instant. The first census
        // after engage therefore always searches, and the line says which census's evidence gated it.
        if (c.TotalEmpty == 0)
        {
            c.SearchNote = "NOT RUN, and there was nothing to run it on: no glyph was EMPTY at mip 0, "
                           + "so there is no missing ink for a positional search to look for. This is "
                           + "the shape of all thirteen clean ModBuild 207 readings";
            c.FitNote = " (no component needed a fit.)";
            return;
        }
        if (c.LastMipLost > 0)
        {
            for (int ci = 0; ci < c.Comps.Count; ci++)
            {
                InkComponent skip = c.Comps[ci];
                if (skip.Empty == 0)
                    continue;
                skip.Ambiguous += skip.Empty;
                c.EmptyNotSearched += skip.Empty;
                c.EmptyAmbiguous += skip.Empty;
                skip.FitNote = "not searched (the mip-chain finding gates the search out)";
            }
            c.SearchNote = $"NOT RUN — the PREVIOUS census on this window found {c.LastMipLost} "
                           + "glyph(s) inked at mip 0 and EMPTY at a mip level the eye reads, which "
                           + "locates the loss in the mip chain. A registration search over the mip 0 "
                           + "plane cannot add to that and costs 3 ms of an 11.11 ms frame, so it is "
                           + "gated out and its EMPTY glyphs are left UNCLASSIFIED rather than "
                           + "silently called absent. It resumes as soon as a census reads 0 lost";
            c.FitNote = " (the search was gated out; no component was fitted.)";
            return;
        }

        c.SearchRan = true;
        // The search draws from the SAME per-frame pool the planes do, and it is the last stage of the
        // census, so it asks for the whole remainder capped at its own ceiling.
        double searchBudget = InkStageBudgetMs(0, InkSearchBudgetMs);
        c.SearchBudgetMs = searchBudget;
        float searchStarted = Time.realtimeSinceStartup;
        int compCount = c.Comps.Count;
        int startComp = compCount > 0 ? c.SearchCursor % compCount : 0;
        bool overran = false;

        // ---- SPEND THE BUDGET ON THE OPAQUE COMPONENTS FIRST (ModBuild 210) ----------------------
        // The 209 log classified 47 of 1320 empty glyphs as genuinely absent, 10 as found at an
        // offset, and left 1263 AMBIGUOUS — of which 680 were never searched at all because the budget
        // ran out. Round-robin over the component list spends that budget in walk order, which on this
        // window means it is largely spent on faded labels whose empties were never going to decide
        // anything. Opaque components are the ones the verdict now speaks about, so they are searched
        // first and the faded ones take what is left. The cursor still rotates WITHIN each group, so a
        // window with more opaque components than one budget can hold still covers them all across
        // consecutive censuses instead of re-searching the same prefix for ever.
        InkSearchOrder.Clear();
        for (int pass = 0; pass < 2; pass++)
        {
            bool wantOpaque = pass == 0;
            for (int step = 0; step < compCount; step++)
            {
                int ci = (startComp + step) % compCount;
                InkComponent cand = c.Comps[ci];
                if (cand.Empty == 0)
                    continue;
                float eff = cand.OwnAlpha * cand.CrAlpha * cand.InheritedAlpha * cand.GroupAlpha;
                if (eff >= InkOpaqueBar != wantOpaque)
                    continue;
                InkSearchOrder.Add(ci);
            }
        }

        for (int step = 0; step < InkSearchOrder.Count; step++)
        {
            int ci = InkSearchOrder[step];
            // THE SEARCH CURSOR AND ITS BUDGET. A component is either searched completely or not at
            // all — a half-swept score field has an argmax that means nothing — so the deadline is
            // consulted BETWEEN components and the remainder is carried to the next census, which
            // resumes at the component the budget stopped on rather than starting over.
            if (c.SearchComponents > 0
                && (Time.realtimeSinceStartup - searchStarted) * 1000.0 >= searchBudget)
            {
                overran = true;
                c.SearchOverran = true;
                for (int rest = step; rest < InkSearchOrder.Count; rest++)
                {
                    InkComponent left = c.Comps[InkSearchOrder[rest]];
                    if (left.Empty == 0)
                        continue;
                    left.Ambiguous += left.Empty;
                    c.EmptyNotSearched += left.Empty;
                    c.EmptyAmbiguous += left.Empty;
                    left.FitNote = $"DEFERRED — the {searchBudget:F2} ms search budget was spent "
                                   + "before this component was reached; it is carried to the next "
                                   + "census and is NOT counted as absent";
                    c.SearchDeferredComps++;
                }
                c.SearchCursor = ci;
                break;
            }
            InkComponent comp = c.Comps[ci];
            if (comp.Empty == 0)
                continue;
            c.SearchComponents++;
            if (searched + comp.GlyphCount > MaxInkSearchGlyphs)
            {
                // The cap must never be able to manufacture the more alarming verdict, so an
                // unsearched EMPTY is NOT "genuinely absent" — it is its own count and it is printed.
                comp.Ambiguous += comp.Empty;
                c.EmptyNotSearched += comp.Empty;
                comp.FitNote = "not searched (the census-wide search cap bit)";
                continue;
            }
            searched += comp.GlyphCount;

            // ---- THE SCORED SUBSET — see InkSearchMaxGlyphs. A whole-string fit AGGREGATES, so it
            // locates its peak from a stratified sample; the per-glyph verdict at the fitted offset
            // still re-tests every EMPTY glyph on the full grid further down. This is what takes one
            // component's coarse sweep from 444,000 texel reads to 111,000.
            comp.ScoreStride = Mathf.Max(1, Mathf.CeilToInt(comp.GlyphCount / (float)InkSearchMaxGlyphs));
            comp.ScoreGlyphs = 0;
            for (int gi = comp.FirstGlyph; gi < comp.FirstGlyph + comp.GlyphCount;
                 gi += comp.ScoreStride)
            {
                if (p.Measured[gi])
                    comp.ScoreGlyphs++;
            }
            if (comp.ScoreGlyphs == 0)
            {
                // Every glyph of this component was left unjudged by the millisecond budget or fell
                // under the size floor, so there is nothing to score an offset against.
                comp.Ambiguous += comp.Empty;
                c.EmptyNotSearched += comp.Empty;
                c.EmptyAmbiguous += comp.Empty;
                comp.FitNote = "not searched (none of its glyphs was judged on this plane, so the "
                               + "score field would have no samples in it)";
                NameInkFit(comp, ref named);
                continue;
            }

            // ---- THE WINDOW, in this component's own units, CLAMPED TO WHAT THE STRIP CAN SERVE --
            // Widening the window from one advance to four makes strip-edge clipping four times as
            // likely, and an offset that runs off the strip is unavailable — so a naive widening would
            // have re-created ModBuild 206's "everything is ambiguous" through a different door. The
            // window is therefore clamped to the room this component actually has inside the strip,
            // and the CLAMPED size is what gets printed: the line says what was searched, not what was
            // asked for.
            float gMinX = float.MaxValue, gMaxX = float.MinValue;
            float gMinY = float.MaxValue, gMaxY = float.MinValue;
            for (int gi = comp.FirstGlyph; gi < comp.FirstGlyph + comp.GlyphCount; gi++)
            {
                InkGlyph gq = c.Glyphs[gi];
                if (gq.X0 < gMinX) gMinX = gq.X0;
                if (gq.X1 > gMaxX) gMaxX = gq.X1;
                if (gq.Y0 < gMinY) gMinY = gq.Y0;
                if (gq.Y1 > gMaxY) gMaxY = gq.Y1;
            }
            float roomX = Mathf.Min(gMinX - c.StripX, c.StripX + c.StripW - gMaxX);
            float roomY = Mathf.Min(gMinY, c.StripH - gMaxY);
            comp.SpanX = Mathf.Min(comp.AdvanceX * InkSearchSpanAdvances, Mathf.Max(0f, roomX));
            comp.SpanY = Mathf.Min(comp.LineY * InkSearchSpanLines, Mathf.Max(0f, roomY));
            if (comp.SpanX < comp.AdvanceX * 0.5f || comp.SpanY < comp.LineY * 0.5f)
            {
                comp.Ambiguous += comp.Empty;
                c.EmptyAmbiguous += comp.Empty;
                comp.FitNote = $"the census strip leaves only {roomX:F0}x{roomY:F0} texels of room "
                               + "around this component's glyphs, under half a glyph advance "
                               + $"({comp.AdvanceX:F1}) or half a line ({comp.LineY:F1}), so there is "
                               + "no window to search in and neither absence nor displacement can be "
                               + "established for it. This is a STRIP BUDGET outcome, not a finding";
                NameInkFit(comp, ref named);
                continue;
            }

            // ---- STAGE 1: the whole window at half-advance steps ---------------------------------
            float coarseX = Mathf.Max(1f, comp.SpanX * 2f / (InkCoarseSteps - 1));
            float coarseY = Mathf.Max(1f, comp.SpanY * 2f / (InkCoarseSteps - 1));
            bool usable = InkSearchGrid(data, c, p, comp, 0f, 0f, coarseX, coarseY, InkCoarseSteps,
                                        out float bdx, out float bdy, out int bestPass,
                                        out bool onBorder, out int unavailable);
            // The SCORE AT ZERO is measured explicitly and always, so the line can show whether the
            // field has a peak at all. ModBuild 206 printed it only on the paths that reached a fit,
            // which is precisely the set of readings that did NOT need it.
            comp.FitPassAtZero = Mathf.Max(0, InkScoreAt(data, c, p, comp, 0f, 0f));
            comp.FitPassAtBest = bestPass;
            comp.FitDx = bdx;
            comp.FitDy = bdy;

            if (!usable)
            {
                comp.Ambiguous += comp.Empty;
                c.EmptyAmbiguous += comp.Empty;
                comp.FitNote = $"{unavailable} of {InkCoarseSteps * InkCoarseSteps} candidate offsets "
                               + "ran off the census strip, so the search window is mostly unavailable "
                               + $"and no fit can be trusted ({comp.FitPassAtBest} pass at the best "
                               + $"available offset, {comp.FitPassAtZero} at zero)";
                NameInkFit(comp, ref named);
                continue;
            }

            // ---- THE FLAT-FIELD TEST — ModBuild 207's central fix --------------------------------
            // If nothing in the whole window finds ink in a meaningful share of this component's
            // glyphs, the score field has NO PEAK: the argmax is arbitrary and lands wherever the scan
            // order left it, very often an edge. ModBuild 206 read that as "the true match may lie
            // outside the window" and returned AMBIGUOUS, which made GENUINELY ABSENT nearly
            // unreachable — 21 of 21, 19 of 19 and 130 of 130 EMPTY glyphs classified as nothing at
            // all. A flat field is "nothing here", and it is the STRONGEST form of absent: no ink
            // within four glyph advances and three line heights of where the mesh says this text is.
            // ModBuild 208: THE DENOMINATOR IS THE SCORED SUBSET, NOT THE GLYPH COUNT. The search now
            // scores at most InkSearchMaxGlyphs of a component's glyphs, so a bar taken against the
            // full count would be unreachable on a long string and every one of them would come back
            // FLAT FIELD — i.e. GENUINELY ABSENT — which is the alarming verdict, manufactured by a
            // cost optimisation. Both numbers are printed.
            int flatBar = Mathf.Max(1, Mathf.CeilToInt(comp.ScoreGlyphs * InkFlatFieldFraction));
            if (bestPass < flatBar)
            {
                comp.FlatField = true;
                comp.Absent += comp.Empty;
                c.EmptyAbsent += comp.Empty;
                comp.FitNote = $"FLAT FIELD — the best offset anywhere in the searched "
                               + $"{comp.SpanX:F0}x{comp.SpanY:F0}-texel window finds ink in only "
                               + $"{comp.FitPassAtBest} of the {comp.ScoreGlyphs} glyph(s) scored "
                               + $"(every {comp.ScoreStride} of {comp.GlyphCount}) "
                               + $"({comp.FitPassAtZero} at the predicted position), below the "
                               + $"{flatBar}-glyph bar, so there is NO PEAK to chase and the argmax "
                               + "carries no information wherever it landed. Its EMPTY glyphs have no "
                               + "ink within four glyph advances and three line heights of where the "
                               + "mesh says they are: GENUINELY ABSENT";
                NameInkFit(comp, ref named);
                continue;
            }

            // A boundary argmax only means "the peak may be cut off" when there IS a peak — i.e. when
            // it scores MATERIALLY better than the null hypothesis. Otherwise it is noise on a nearly
            // flat field and must not disqualify the component.
            int materialBar = Mathf.Max(1, Mathf.CeilToInt(comp.ScoreGlyphs * InkMaterialGainFraction));
            bool better = bestPass - comp.FitPassAtZero >= materialBar;
            if (onBorder && better)
            {
                comp.Ambiguous += comp.Empty;
                c.EmptyAmbiguous += comp.Empty;
                comp.FitNote = $"a REAL peak is being cut off: the best offset ({comp.FitDx:F1},"
                               + $"{comp.FitDy:F1}) sits on the boundary of the searched "
                               + $"{comp.SpanX:F0}x{comp.SpanY:F0}-texel window and scores "
                               + $"{comp.FitPassAtBest} of the {comp.ScoreGlyphs} scored against "
                               + $"{comp.FitPassAtZero} at zero, a gain of "
                               + $"{comp.FitPassAtBest - comp.FitPassAtZero} over a {materialBar} bar, "
                               + "so the true match may lie further out and the window needs widening "
                               + "again";
                NameInkFit(comp, ref named);
                continue;
            }
            if (!better)
            {
                c.FitNoFit++;
                comp.Absent += comp.Empty;
                c.EmptyAbsent += comp.Empty;
                comp.FitNote = "the predicted position IS the best registration in the whole searched "
                               + $"{comp.SpanX:F0}x{comp.SpanY:F0}-texel window ({comp.FitPassAtZero} "
                               + $"of the {comp.ScoreGlyphs} scored pass there, and the best offset anywhere "
                               + $"manages {comp.FitPassAtBest}, under the {materialBar}-glyph "
                               + "material-gain bar), so its EMPTY glyphs are genuinely absent from "
                               + "the capture";
                NameInkFit(comp, ref named);
                continue;
            }

            // ---- STAGE 2: refine inside the winning stage-1 cell ---------------------------------
            // A window four advances wide cannot also resolve a fraction of an advance out of one
            // grid, so the coarse pass locates the cell and this pass resolves inside it.
            if (InkSearchGrid(data, c, p, comp, bdx, bdy, coarseX * 2f / (InkFineSteps - 1),
                              coarseY * 2f / (InkFineSteps - 1), InkFineSteps,
                              out float fdx2, out float fdy2, out int finePass, out _, out _))
            {
                comp.FitDx = fdx2;
                comp.FitDy = fdy2;
                comp.FitPassAtBest = Mathf.Max(bestPass, finePass);
            }

            bool subAdvance = Mathf.Abs(comp.FitDx) <= comp.AdvanceX * InkSubAdvanceFraction
                              && Mathf.Abs(comp.FitDy) <= comp.LineY * InkSubAdvanceFraction;
            if (!subAdvance)
            {
                // THE ALIAS OFFSET IS REPORTED ANYWAY (ModBuild 207). The classification stays
                // ambiguous — an is-there-ink test genuinely cannot separate a one-advance shift from
                // the truth in running text — but five components fitting the SAME alias and five
                // fitting five different ones are completely different readings, and ModBuild 206
                // discarded the number that tells them apart.
                c.FitLatticeAlias++;
                c.AliasCount++;
                c.AliasDxSum += comp.FitDx;
                c.AliasDySum += comp.FitDy;
                comp.Ambiguous += comp.Empty;
                c.EmptyAmbiguous += comp.Empty;
                comp.FitNote = $"LATTICE ALIAS at ({comp.FitDx:F1},{comp.FitDy:F1}) texels — a whole "
                               + $"advance ({comp.AdvanceX:F1}) or line ({comp.LineY:F1}) away, i.e. "
                               + "the string re-registered onto its own neighbouring letters, which an "
                               + "is-there-ink test cannot tell from the truth. THE OFFSET IS PRINTED "
                               + "ANYWAY because it is informative even though the classification "
                               + $"stays ambiguous: {comp.FitPassAtBest} of the {comp.ScoreGlyphs} scored pass "
                               + $"there against {comp.FitPassAtZero} at zero";
                NameInkFit(comp, ref named);
                continue;
            }

            // ---- a usable sub-advance fit: classify each EMPTY glyph at it ----------------------
            c.FitCount++;
            c.FitDxSum += comp.FitDx;
            c.FitDySum += comp.FitDy;
            if (comp.FitDx < c.FitDxLow) c.FitDxLow = comp.FitDx;
            if (comp.FitDx > c.FitDxHigh) c.FitDxHigh = comp.FitDx;
            if (comp.FitDy < c.FitDyLow) c.FitDyLow = comp.FitDy;
            if (comp.FitDy > c.FitDyHigh) c.FitDyHigh = comp.FitDy;

            for (int gi = comp.FirstGlyph; gi < comp.FirstGlyph + comp.GlyphCount; gi++)
            {
                // A glyph this plane never judged — under the size floor, or past the millisecond
                // budget — has Inked = false, which is indistinguishable from EMPTY here. Skipping it
                // is what stops the budget from manufacturing ABSENT verdicts.
                if (!p.Measured[gi] || p.Inked[gi])
                    continue;
                InkGlyph g = c.Glyphs[gi];
                // REQUIREMENT 3b: an EMPTY glyph within one advance of a strip edge is not a clean
                // reading whichever bucket it lands in. An OVERLAY count, printed with the rest.
                if (g.X0 - c.StripX < comp.AdvanceX
                    || (c.StripX + c.StripW) - g.X1 < comp.AdvanceX)
                {
                    comp.EmptyNearStripEdge++;
                    c.EmptyNearStripEdge++;
                }
                // THE FULL GRID, not the search's coarse one. The search may use a cheap 4x4 to FIND
                // the offset — a whole-string fit aggregates over many glyphs, so a missed stem here
                // and there does not move it — but the per-glyph FOUND/ABSENT verdict is reported next
                // to the INKED/EMPTY verdict and must be measured to the same standard, or a thin
                // glyph would be called absent by a test the primary verdict would have passed.
                float s = InkQuadScore(data, p, g, comp.FitDx, comp.FitDy, InkInnerSamples,
                                       p.Bg[gi], out _, out _, out bool clipped);
                if (clipped)
                {
                    comp.Ambiguous++;
                    c.EmptyAmbiguous++;
                }
                else if (s >= InkThreshold)
                {
                    comp.FoundOffset++;
                    c.EmptyFoundOffset++;
                }
                else
                {
                    comp.Absent++;
                    c.EmptyAbsent++;
                }
            }
            comp.FitNote = $"registers best at ({comp.FitDx:F1},{comp.FitDy:F1}) texels, where "
                           + $"{comp.FitPassAtBest} of the {comp.ScoreGlyphs} scored glyph(s) find ink against "
                           + $"{comp.FitPassAtZero} at the predicted position, over a searched "
                           + $"{comp.SpanX:F0}x{comp.SpanY:F0}-texel window";
            NameInkFit(comp, ref named);
        }

        if (c.FitCount == 0)
            c.FitDxLow = c.FitDxHigh = c.FitDyLow = c.FitDyHigh = 0f;
        c.FitNote = InkFitSb.Length > 0 ? InkFitSb.ToString() : " (no component needed a fit.)";
        InkFitSb.Length = 0;
        c.SearchMs = (Time.realtimeSinceStartup - searchStarted) * 1000.0;
        if (!overran)
            c.SearchCursor = 0;
        c.SearchNote = overran
            ? $"RAN and OVERRAN: {c.SearchComponents} component(s) searched in "
              + $"{c.SearchMs:F2} ms against the {searchBudget:F2} ms it was granted of the per-frame pool, and "
              + $"{c.SearchDeferredComps} component(s) were DEFERRED to the next census, which resumes "
              + $"at component {c.SearchCursor} of {compCount}. Their EMPTY glyphs are counted as "
              + "UNCLASSIFIED and explicitly NOT as absent — a budget must never be able to "
              + "manufacture the more alarming verdict"
            : $"RAN COMPLETE: {c.SearchComponents} component(s) with EMPTY glyphs searched in "
              + $"{c.SearchMs:F2} ms against the {searchBudget:F2} ms it was granted of the per-frame pool, scoring at most "
              + $"{InkSearchMaxGlyphs} glyph(s) per component";
    }

    /// <summary>Append one component's fit sentence to the report, up to the naming cap. The COUNTS are
    /// always complete; only the sentences are capped.</summary>
    private static void NameInkFit(InkComponent comp, ref int named)
    {
        if (named >= MaxInkComponentsReported)
            return;
        named++;
        InkFitSb.Append(" '").Append(comp.Name).Append("': ").Append(comp.FitNote).Append('.');
    }

    /// <summary>
    /// One grid of the joint registration search, centred on (<paramref name="centreDx"/>,
    /// <paramref name="centreDy"/>). Returns false when too much of the grid ran off the census strip
    /// for any fit to be trusted.
    /// <para>An offset that ran off the strip is UNAVAILABLE, not unsuccessful: it can never win, and
    /// it is counted rather than scored zero, because "no ink there" and "we could not look there" must
    /// not print the same character.</para>
    /// </summary>
    private static bool InkSearchGrid(Unity.Collections.NativeArray<Color32> data, InkCensus c,
                                      InkPlane p, InkComponent comp, float centreDx, float centreDy,
                                      float stepX, float stepY, int steps,
                                      out float bestDx, out float bestDy, out int bestPass,
                                      out bool onBorder, out int unavailable)
    {
        int half = (steps - 1) / 2;
        bestDx = centreDx;
        bestDy = centreDy;
        bestPass = -1;
        onBorder = false;
        unavailable = 0;
        int bi = half, bj = half;
        for (int j = 0; j < steps; j++)
        {
            float dy = centreDy + (j - half) * stepY;
            for (int i = 0; i < steps; i++)
            {
                float dx = centreDx + (i - half) * stepX;
                int pass = InkScoreAt(data, c, p, comp, dx, dy);
                if (pass < 0)
                {
                    unavailable++;
                    continue;
                }
                if (pass > bestPass)
                {
                    bestPass = pass;
                    bestDx = dx;
                    bestDy = dy;
                    bi = i;
                    bj = j;
                }
            }
        }
        onBorder = bi == 0 || bi == steps - 1 || bj == 0 || bj == steps - 1;
        return bestPass >= 0 && unavailable <= steps * steps * 2 / 5;
    }

    /// <summary>How many of <paramref name="comp"/>'s SCORED glyphs find ink with the whole string
    /// shifted by (<paramref name="dx"/>, <paramref name="dy"/>) texels, or -1 when any of them ran off
    /// the census strip at that offset. Uses the search's coarse sample grid; the final per-glyph
    /// judgement at the fitted offset re-tests on the full grid.
    /// <para>ModBuild 208: it walks every <see cref="InkComponent.ScoreStride"/>'th glyph rather than
    /// all of them (see <see cref="InkSearchMaxGlyphs"/>) and skips any glyph this plane did not judge,
    /// so the returned count is out of <see cref="InkComponent.ScoreGlyphs"/> — which is what the
    /// flat-field and material-gain bars are taken against.</para></summary>
    private static int InkScoreAt(Unity.Collections.NativeArray<Color32> data, InkCensus c,
                                  InkPlane p, InkComponent comp, float dx, float dy)
    {
        int pass = 0;
        int stride = Mathf.Max(1, comp.ScoreStride);
        for (int gi = comp.FirstGlyph; gi < comp.FirstGlyph + comp.GlyphCount; gi += stride)
        {
            if (!p.Measured[gi])
                continue;
            float s = InkQuadScore(data, p, c.Glyphs[gi], dx, dy, InkSearchCoarseSamples,
                                   p.Bg[gi], out _, out _, out bool clipped);
            if (clipped)
                return -1;
            if (s >= InkThreshold)
                pass++;
        }
        return pass;
    }

    /// <summary>
    /// <b>THE MEASUREMENT ModBuild 208 EXISTS FOR: compare the planes against each other, glyph by
    /// glyph.</b>
    ///
    /// <para>The comparison is between LEVELS and never against an absolute. An absolute EMPTY count
    /// at mip 2 would be dominated by glyphs that legitimately averaged away, which is why the size
    /// floor exists and why every count here has its own denominator: a glyph enters a comparison only
    /// when BOTH planes judged it — above the floor at both levels, and inside the millisecond budget
    /// at both.</para>
    ///
    /// <para>Both DIRECTIONS are counted. "Inked at mip 0, empty at mip N" is the finding; "empty at
    /// mip 0, inked at mip N" is its control, and it is not a defect at all — box-filtering thin ink
    /// concentrates it into fewer texels as often as it dilutes it, so a healthy chain produces a few
    /// of these. A reading with a large count in BOTH directions is threshold noise and not a mip
    /// chain fault, and the verdict says so rather than reporting only the alarming half.</para>
    /// </summary>
    private static void CompareInkPlanes(InkCensus c)
    {
        InkPlane mip0 = c.Planes[InkPlaneMip0];
        InkMipSb.Length = 0;
        int named = 0;
        int n = c.Glyphs.Count;

        for (int which = 0; which < 2; which++)
        {
            InkPlane p = c.Planes[which == 0 ? InkPlaneMip1 : InkPlaneMip2];
            if (!p.Landed)
                continue;
            int compared = 0, lost = 0, gained = 0;
            for (int i = 0; i < n; i++)
            {
                if (!mip0.Measured[i] || !p.Measured[i])
                    continue;
                compared++;
                if (mip0.Inked[i] && !p.Inked[i])
                {
                    lost++;
                    if (named < MaxInkMipLostNamed)
                    {
                        named++;
                        InkGlyph g = c.Glyphs[i];
                        InkMipSb.Append(named > 1 ? ", " : " ")
                                .Append('\'').Append(g.Ch).Append("' at mip ").Append(p.Mip)
                                .Append(" (")
                                .Append(((g.X1 - g.X0) * p.Scale).ToString("F1")).Append(" x ")
                                .Append(((g.Y1 - g.Y0) * p.Scale).ToString("F1"))
                                .Append(" texels there)");
                    }
                }
                else if (!mip0.Inked[i] && p.Inked[i])
                {
                    gained++;
                }
            }
            if (which == 0)
            {
                c.MipCompared1 = compared;
                c.MipLost1 = lost;
                c.MipGained1 = gained;
            }
            else
            {
                c.MipCompared2 = compared;
                c.MipLost2 = lost;
                c.MipGained2 = gained;
            }
        }
        int totalLost = c.MipLost1 + c.MipLost2;
        c.MipLostNote =
            c.MipCompared1 + c.MipCompared2 == 0
                ? " (NOT ONE GLYPH COULD BE COMPARED — read that as a gap in the measurement, never as "
                  + "a clean chain: either no mip level landed, or every glyph fell under the size "
                  + "floor at every level. The plane table above says which.)"
                : totalLost == 0
                    ? " (none — no glyph inked at mip 0 was empty at a level the eye reads.)"
                    : InkMipSb.ToString()
                      + (totalLost > named ? $", and {totalLost - named} more not named." : ".");
        InkMipSb.Length = 0;

        // ---- THE RESOLVE BLIT, ISOLATED ---------------------------------------------------------
        // Graphics.Blit(e.Rt, e.MipRt) sits between the two full-resolution planes and is a straight
        // copy with a default material; identical readings on both sides clear it, and a disagreement
        // accuses it. Nothing in this class has ever measured across it.
        InkPlane cap = c.Planes[InkPlaneCapture];
        if (!cap.Landed)
            return;
        for (int i = 0; i < n; i++)
        {
            if (!mip0.Measured[i] || !cap.Measured[i])
                continue;
            c.BlitCompared++;
            if (cap.Inked[i] && !mip0.Inked[i])
                c.BlitOnlyCapture++;
            else if (!cap.Inked[i] && mip0.Inked[i])
                c.BlitOnlyMip++;
        }
    }

    /// <summary>
    /// <b>STATE THE MIP-CHAIN VERDICT, CHOSEN BY THE NUMBERS.</b> There is deliberately no branch
    /// that ends in silence, and the two honest undecided forms are as explicit as the two decisive
    /// ones — a level that could not be read must never look like a level that read clean.
    /// </summary>
    private static void BuildMipVerdict(InkCensus c)
    {
        InkPlane mip1 = c.Planes[InkPlaneMip1];
        InkPlane mip2 = c.Planes[InkPlaneMip2];
        int lost = c.MipLost1 + c.MipLost2;
        int gained = c.MipGained1 + c.MipGained2;
        int compared = c.MipCompared1 + c.MipCompared2;

        // The floor arithmetic, printed on every branch. Mip generation is a repeated 2x2 BOX filter
        // and box filtering is LINEAR, so a stroke covering a fraction f of a texel keeps f of its
        // deviation. Against this census's own INKED median that gives the level at which minification
        // alone would legitimately erase a glyph — and it is far past level 2, which is what makes a
        // loss at level 1 or 2 a finding rather than an expected minification.
        float inkMedian = c.MedDevInk >= 0f ? c.MedDevInk * 255f : -1f;
        string physics = inkMedian > 0f
            ? $"THE AVERAGING ARGUMENT, WITH A NUMBER IN IT: mip generation is a repeated 2x2 BOX "
              + $"filter and box filtering is linear, so a stroke keeps the fraction of a texel it "
              + $"covers. At this census's INKED median of {inkMedian:F1}/255 a stroke survives the "
              + $"{InkThreshold * 255f:F0}/255 ink bar down to {InkThreshold * 255f / inkMedian:F3} "
              + "texels of coverage, i.e. roughly "
              + $"{Mathf.Log(inkMedian / (InkThreshold * 255f), 2f):F1} halvings below a one-texel "
              + "stroke — several levels past mip 2. 'It just averaged away' is therefore NOT "
              + "available as an explanation at these levels and is not being used as one"
            : "THE AVERAGING ARGUMENT CANNOT BE CHECKED on this reading, because no glyph was judged "
              + "INKED at mip 0 and there is no ink median to scale against";

        if (!mip1.Landed && !mip2.Landed)
        {
            c.MipVerdict =
                "MIP CHAIN NOT MEASURED — neither level the eye reads could be censused, so this "
                + "reading says NOTHING about the chain in either direction and must not be pooled "
                + "with one that does. MIP 1: "
                + (mip1.Available
                    ? (mip1.FailWhy.Length > 0 ? mip1.FailWhy : "requested but never delivered")
                    : mip1.Unavailable)
                + ". MIP 2: "
                + (mip2.Available
                    ? (mip2.FailWhy.Length > 0 ? mip2.FailWhy : "requested but never delivered")
                    : mip2.Unavailable)
                + ".";
            return;
        }

        string coverage = $"THE DENOMINATORS: {c.MipCompared1} glyph(s) could be compared at mip 1 and "
                          + $"{c.MipCompared2} at mip 2 — a glyph enters a comparison only when BOTH "
                          + "levels judged it, i.e. it is above the "
                          + $"{InkMinQuadTexels:F0}-texel size floor at both and inside the share of "
                          + $"the {InkFrameBudgetMs:F1} ms per-frame pool each was granted. Of "
                          + $"{c.TotalGlyphs} glyph(s) in the census the MIP 0 plane judged "
                          + $"{c.TotalJudged}, left {c.TotalBelowFloor} under the floor and deferred "
                          + $"{c.TotalDeferred} to the next census.";

        if (compared < 8)
        {
            c.MipVerdict =
                $"MIP CHAIN UNDECIDED — only {compared} glyph(s) in total could be compared between "
                + "mip 0 and the levels the eye reads, which is too few for either verdict. That is a "
                + "statement about the SIZE FLOOR and the budget, not about the chain: at these levels "
                + "the window's glyphs are mostly under the "
                + $"{InkMinQuadTexels:F0}-texel bar this instrument needs to judge one at all. "
                + coverage + " " + physics + ".";
            return;
        }

        if (lost == 0)
        {
            c.MipVerdict =
                "MIP CHAIN VERIFIED — the levels the eye samples carry the same glyphs level 0 does. "
                + $"NOT ONE of the {compared} comparable glyph(s) is inked at mip 0 and empty at mip 1 "
                + $"or mip 2 ({c.MipLost1} at mip 1, {c.MipLost2} at mip 2), so the loss is NOT in the "
                + "mip chain and the next place to look is the display quad or the eye — the RawImage "
                + "material, the sampler state, the mip LOD bias, the stereo pass, or the resolve into "
                + "the eye target. This exonerates the chain in the same way ModBuild 207 exonerated "
                + $"the mip 0 capture. THE CONTROL, which must be read with it: {gained} glyph(s) went "
                + "the OTHER way (empty at mip 0, inked at a lower level), which is what box-filtering "
                + "thin ink into fewer texels does and is not a defect; a large count in both "
                + "directions would mean the ink threshold is sitting inside the noise and neither "
                + "number could be trusted. " + coverage + " " + physics + ".";
            return;
        }

        c.MipVerdict =
            $"MIP CHAIN DEFECTIVE — {lost} glyph(s) inked at level 0 are EMPTY at level 1 or 2 at "
            + $"sizes well above the averaging floor ({c.MipLost1} of {c.MipCompared1} at mip 1, "
            + $"{c.MipLost2} of {c.MipCompared2} at mip 2), so the chain is where the picture is lost. "
            + "THE CHARACTERS AND THEIR SIZE AT THE LEVEL THAT LOST THEM:" + c.MipLostNote
            + $" READ THE CONTROL BEFORE ACTING: {gained} glyph(s) went the OTHER way (empty at mip 0, "
            + "inked at a lower level). If that number is comparable to the losses this is threshold "
            + "noise around the ink bar and NOT a chain fault; if it is small next to them the losses "
            + "are one-directional and real. " + coverage + " " + physics
            + ". WHERE TO WORK: GenerateMips() runs in ResolveAndMip on every capture and ModBuild 204 "
            + "already found one way for levels 1..N to hold something other than a filtered level 0 "
            + "(GL.Clear clears the BOUND SURFACE, i.e. level 0 only). That case was fixed at "
            + "allocation; this is the ordinary-frame case, which nobody has ever verified.";
    }

    /// <summary>Isolate the resolve blit. See <see cref="CompareInkPlanes"/> — this only phrases what
    /// it counted, and it phrases NOT MEASURED as loudly as either finding.</summary>
    private static void BuildBlitVerdict(InkCensus c)
    {
        InkPlane cap = c.Planes[InkPlaneCapture];
        if (!cap.Landed)
        {
            c.BlitVerdict = "RESOLVE BLIT NOT MEASURED — "
                            + (cap.Available
                                ? (cap.FailWhy.Length > 0 ? cap.FailWhy
                                                          : "requested but never delivered")
                                : cap.Unavailable)
                            + ", so nothing follows about Graphics.Blit in either direction.";
            return;
        }
        if (c.BlitCompared == 0)
        {
            c.BlitVerdict = "RESOLVE BLIT UNDECIDED — the capture plane landed but not one glyph was "
                            + "judged on BOTH it and the mip 0 plane, so there is nothing to compare.";
            return;
        }
        if (c.BlitOnlyCapture == 0 && c.BlitOnlyMip == 0)
        {
            c.BlitVerdict = $"RESOLVE BLIT VERIFIED — all {c.BlitCompared} glyph(s) judged on both "
                            + "sides give the SAME verdict before and after Graphics.Blit, so the "
                            + "resolve is not where anything is lost and the capture target and the "
                            + "display target's level 0 carry the same picture.";
            return;
        }
        c.BlitVerdict = $"THE RESOLVE BLIT LOSES INK — of {c.BlitCompared} glyph(s) judged on both "
                        + $"sides, {c.BlitOnlyCapture} are INKED in the capture target and EMPTY in "
                        + $"the display target after the blit, and {c.BlitOnlyMip} go the other way. "
                        + "Graphics.Blit(e.Rt, e.MipRt) is a straight copy with the default material, "
                        + "so a one-directional disagreement here is a format, sRGB or MSAA-resolve "
                        + "fault and not a filtering one — the two targets are both "
                        + "RenderTextureFormat.Default with sRGB=False by construction, so whichever "
                        + "of those is untrue is the finding.";
    }

    /// <summary>
    /// <b>REQUIREMENT 4: STATE THE VERDICT, CHOSEN BY THE NUMBERS.</b> The reader is not asked to
    /// weigh a centroid distribution against a fit distribution — this does it, and names which rule
    /// fired. There is deliberately no branch that ends in silence.
    /// </summary>
    private static void BuildMappingVerdict(InkCensus c)
    {
        // THE RETRACTION PREFIX, and it is deliberately a PREFIX and not a terminal branch. If this
        // census found components that the ModBuild 205/206 exclusion test would have let through, then
        // THOSE builds' EMPTY numbers are inflated — but THIS census excluded them, so its own counts
        // are clean and suppressing them would throw away a good reading to report a historical one.
        // The line has to say both things: retract the old number, and give today's.
        string prefix = c.ComponentsHiddenByGroupOnly > 0
            ? $"RETRACT THE ModBuild 205/206 NUMBERS FIRST — {c.ComponentsHiddenByGroupOnly} text "
              + "component(s) in this window are HIDDEN BY A CANVASGROUP and were NOT caught by the "
              + "CanvasRenderer alpha test that those builds used as their only exclusion. They draw "
              + "nothing CORRECTLY, so every glyph of theirs that those builds reported as EMPTY was "
              + "the instrument's own artefact and their headline EMPTY counts are inflated by exactly "
              + $"that much ({c.ComponentsHiddenByGroup} component(s) excluded by the chain walk in "
              + "total on this census). THIS census excludes them, so everything that follows is "
              + "clean. THEN: "
            : string.Empty;
        c.MappingVerdict = prefix + BuildMappingVerdictBody(c);
    }

    /// <summary>The verdict itself — see <see cref="BuildMappingVerdict"/>, which prepends the
    /// retraction prefix when this census's exclusion test caught something the previous builds' did
    /// not.</summary>
    private static string BuildMappingVerdictBody(InkCensus c)
    {
        float mdx = c.CentroidCount > 0 ? (float)(c.CentroidDxSum / c.CentroidCount) : 0f;
        float mdy = c.CentroidCount > 0 ? (float)(c.CentroidDySum / c.CentroidCount) : 0f;
        float mag = Mathf.Sqrt(mdx * mdx + mdy * mdy);
        int classified = c.EmptyAbsent + c.EmptyFoundOffset + c.EmptyAmbiguous;
        float fdx = c.FitCount > 0 ? (float)(c.FitDxSum / c.FitCount) : 0f;
        float fdy = c.FitCount > 0 ? (float)(c.FitDySum / c.FitCount) : 0f;
        float rateX = Mathf.Max(c.RateX, 1e-4f), rateY = Mathf.Max(c.RateY, 1e-4f);

        // ---- THE THREE SIGNAL NUMBERS, carried into EVERY branch (ModBuild 207) ------------------
        // The ModBuild 206 log had one reading at INKED median 39.6/255 against a background of
        // 16.5/255 with 130 EMPTY, next to readings at 195-208 / ~8 that were otherwise identical. Two
        // completely different failures — a DIM CAPTURE and a scattered subset at full intensity — and
        // the numbers that separate them had to be dug out of two other fields. They go in the verdict
        // now, on every branch, so the next reader cannot miss the difference.
        string signal = $"INKED median {(c.MedDevInk >= 0f ? c.MedDevInk * 255f : -1f):F1}/255, EMPTY "
                        + $"median {(c.MedDevEmpty >= 0f ? c.MedDevEmpty * 255f : -1f):F1}/255, strip "
                        + $"background {c.Background * 255f:F1}/255";

        // ---- THE ARTEFACT CHECK THAT DOES invalidate this census's own numbers -------------------
        // A component that is SUBSTANTIALLY transparent draws faint glyphs LEGITIMATELY, so empties
        // coming out of one are not a statement about the capture path at all. (The CanvasGroup-only
        // exclusions are handled as a prefix by the caller: those components were excluded HERE, so
        // they contaminate the previous builds' numbers and not these.)
        // ---- THE ARTEFACT CHECK, NOW PER COMPONENT AND NOT AS A MINIMUM (ModBuild 210) -----------
        // It fires only when EVERY empty-producing component is faded. Until 209 it fired on the
        // LOWEST alpha in the reading, which meant one label greyed out by design — 'XP Amount' at
        // authored alpha 0.200 — dismissed 28 of 49 readings AND the fully opaque components standing
        // beside it in the same readings. That is the third time in this project a worst-case summary
        // field has hidden the finding it was built to expose; see the memory "a WORST field is the
        // tail". A reading with even ONE opaque component missing ink now falls through to the real
        // verdicts below, and the faded ones are subtracted rather than allowed to speak for it.
        if (c.TotalEmpty > 0 && c.EmptyOpaqueGlyphs == 0 && c.EmptyDimGlyphs > 0)
        {
            return $"ARTEFACT — ALL {c.EmptyDimComps} component(s) that produced EMPTY glyphs are "
                   + $"faded, the lowest at {c.LowestAlphaComp}, so their glyphs are legitimately "
                   + "faint rather than missing and NOT ONE fully opaque component lost ink in this "
                   + "reading. That second clause is the one that matters and it is new: the same "
                   + "verdict used to fire on the lowest alpha ANYWHERE in the reading, which "
                   + "dismissed 28 of 49 ModBuild 209 readings that did contain opaque components "
                   + "missing their ink. The ALPHA EVIDENCE field names every component with all four "
                   + "of its alphas, opaque ones first. " + signal + ".";
        }
        if (c.EmptyOpaqueGlyphs > 0)
        {
            // NOT a return: the reading is real and the classification below must run on it. This only
            // makes sure the number the next reader needs is in front of the argument rather than
            // behind it.
            signal = $"{c.EmptyOpaqueGlyphs} EMPTY glyph(s) come from {c.EmptyOpaqueComps} FULLY "
                     + $"OPAQUE component(s) (effective alpha at or above {InkOpaqueBar:F2}) and "
                     + $"{c.EmptyDimGlyphs} from {c.EmptyDimComps} faded one(s) — the opaque count is "
                     + "the defect and the faded count is not. " + signal;
        }
        string alphaCaveat = c.TotalEmpty > 0 && c.EmptyLowestAlpha < 0.99f
            ? $" CAVEAT: the lowest effective alpha among the EMPTY components is "
              + $"{c.EmptyLowestAlpha:F3} rather than 1.000, so their ink is slightly attenuated by "
              + "design; the ALPHA EVIDENCE field names them."
            : string.Empty;

        // ---- A DIM CAPTURE IS A DIFFERENT BUG AND MUST SAY SO IN THOSE WORDS ---------------------
        if (c.MedDevInk >= 0f && c.MedDevInk < InkDimInkMedian && c.Background > InkDimBackground)
        {
            return
                $"A DIM CAPTURE, NOT MISSING GLYPHS — the ink median has COLLAPSED to "
                + $"{c.MedDevInk * 255f:F1}/255 (below the {InkDimInkMedian * 255f:F0}/255 bar; a "
                + "healthy reading on this window is 195-208) AND the strip background has RISEN to "
                + $"{c.Background * 255f:F1}/255 (above the {InkDimBackground * 255f:F0}/255 bar; "
                + "healthy is ~8). Both moved together, which is what a fade or a wash does to a whole "
                + $"picture and is NOT what a scattered subset of missing glyphs looks like. The "
                + $"{c.TotalEmpty} EMPTY glyph(s) on this line are glyphs whose contrast fell under "
                + "the ink threshold, not glyphs that are absent, and this reading must NEVER be "
                + "pooled with the full-intensity ones. READ THE DISTRIBUTION BEFORE ACTING ON IT: in "
                + "the ModBuild 206 session exactly ONE reading looked like this and every other "
                + "broken reading sat at full intensity, so a single line of this shape is an outlier "
                + "and not the family — this project has promoted an extreme to the operating point "
                + "three times already.";
        }

        if (c.CentroidCount == 0)
        {
            return "UNDECIDED, because NOT ONE glyph in this census was judged INKED, so "
                               + "there is no ink anywhere to measure a centroid against and the "
                               + "mapping cannot be checked at all. Read that as a finding in its own "
                               + "right before reading it as an instrument failure: a whole strip of "
                               + "predicted text with no ink under any of it is either a completely "
                               + "displaced capture or a completely blank one, and the ORIENTATION and "
                               + "band figures above say which is more likely. " + signal + ".";
        }
        string centroid = $"mean ink centroid {mdx:F2},{mdy:F2} texels = "
                          + $"{mdx / rateX:F2},{mdy / rateY:F2} authored px over {c.CentroidCount} "
                          + $"INKED glyph(s), against a {InkMappingVerifiedTexels:F1}-texel bar; "
                          + signal + alphaCaveat;

        if (c.TotalEmpty == 0)
        {
            return mag <= InkMappingVerifiedTexels
                ? $"MAPPING VERIFIED — the ink sits where the mesh says ({centroid}) and there were no "
                  + "EMPTY glyphs at all in this census, so this reading contains no evidence of any "
                  + "defect anywhere in the capture path."
                : $"MIXED — no glyph was EMPTY, so nothing is missing, but the ink is systematically "
                  + $"OFF-CENTRE in its own quads ({centroid}). That is a real registration error "
                  + "which happens to be small enough that every quad still catches its own ink; a "
                  + "reading with EMPTY glyphs and this same centroid would be a displacement, not "
                  + "missing ink.";
        }
        // ModBuild 208: A SEARCH THAT NEVER RAN MUST NOT PRINT AS A SEARCH THAT FOUND NOTHING. With the
        // gate closed every EMPTY glyph lands in AMBIGUOUS, so `classified` is non-zero and both
        // fractions are zero — which would have fallen through to MIXED and read as a per-component
        // displacement finding. That is precisely the "a census that did not run must never look like a
        // census that ran and found nothing" rule this whole region is built on.
        if (!c.SearchRan && c.TotalEmpty > 0)
        {
            return $"UNDECIDED ON THE EMPTIES, because the neighbourhood search DID NOT RUN on this "
                   + $"census: {c.SearchNote}. All {c.TotalEmpty} EMPTY glyph(s) are therefore "
                   + "UNCLASSIFIED — not absent, not displaced, not measured. The centroid over the "
                   + $"glyphs that DO have ink still reads {centroid}, and that half of the mapping "
                   + "check is unaffected. READ THE MIP CHAIN VERDICT INSTEAD: when the gate is what "
                   + "closed the search, that verdict is the reason it closed.";
        }
        if (classified == 0)
        {
            return $"UNDECIDED, because all {c.TotalEmpty} EMPTY glyph(s) fell outside "
                               + "what the neighbourhood search could classify "
                               + $"({c.EmptyNotSearched} not searched at all because the cap bit). "
                               + $"The centroid still reads {centroid}. NOTE, because ModBuild 206 "
                               + "reached this state on nearly every reading and it was an INSTRUMENT "
                               + "fault, not a finding: a flat score field is now classified GENUINELY "
                               + "ABSENT rather than ambiguous, so if this line still says UNDECIDED "
                               + "the cause is the search cap or the strip edge and the counts above "
                               + "say which.";
        }
        float foundFrac = c.EmptyFoundOffset / (float)classified;
        float absentFrac = c.EmptyAbsent / (float)classified;

        if (foundFrac >= InkVerdictMajority)
        {
            return $"MAPPING DISPLACED by ({fdx:F1},{fdy:F1}) texels = ({fdx / rateX:F2},{fdy / rateY:F2}) "
                + $"authored px — the EMPTY verdicts above are THAT DISPLACEMENT, not missing ink. "
                + $"{c.EmptyFoundOffset} of {classified} classified EMPTY glyph(s) were found at the "
                + $"fitted offset, {c.EmptyAbsent} were absent there too, {c.EmptyAmbiguous} could not "
                + $"be classified. The fit spans {c.FitDxLow:F1}..{c.FitDxHigh:F1} x "
                + $"{c.FitDyLow:F1}..{c.FitDyHigh:F1} texels over {c.FitCount} component(s) — a TIGHT "
                + "span is one coherent shift of the whole picture and a WIDE one is not a single "
                + $"displacement at all. Independently, {centroid}. WHAT THIS DOES NOT YET SAY: "
                + "whether the displacement is in the CAPTURE (the picture really is shifted, which is "
                + "the bug) or in THIS INSTRUMENT'S mapping (the picture is fine and the census is "
                + "looking in the wrong place). The two are separated by the sign and the stability of "
                + "the offset across the three moments and across windows: an instrument error is the "
                + "SAME offset on every reading of every window, while a capture displacement moves "
                + "with the drag and is what the user sees.";
        }
        if (absentFrac >= InkVerdictMajority && mag <= InkMappingVerifiedTexels)
        {
            return $"MAPPING VERIFIED — the ink sits where the mesh says ({centroid}), and "
                + $"{c.EmptyAbsent} of {classified} classified EMPTY glyph(s) had no ink ANYWHERE "
                + "within a whole glyph advance and a whole line height of their predicted position. "
                + "SO EMPTY MEANS ABSENT: those glyphs are genuinely not in the captured texture, and "
                + "the loss is at or before rasterisation into the render target.";
        }
        return $"MIXED — {c.EmptyFoundOffset} of {classified} classified EMPTY glyph(s) were FOUND at a "
            + $"fitted offset, {c.EmptyAbsent} were genuinely ABSENT and {c.EmptyAmbiguous} could not "
            + $"be classified, with {centroid}"
            + (c.FitCount > 0
                ? $" and a fitted offset of ({fdx:F1},{fdy:F1}) texels over {c.FitCount} component(s)"
                : " and NO component producing a usable fit")
            + $" ({c.FitLatticeAlias} component(s) fitted a lattice alias, {c.FitNoFit} found nothing "
            + "better than the predicted position). NEITHER a clean displacement NOR a clean absence: "
            + "read the per-component fits above, because a MIXED verdict on a window where some "
            + "components register at zero and others do not is a per-COMPONENT displacement, which "
            + "neither a whole-picture shift nor a rasterisation loss can produce.";
    }

    /// <summary>
    /// The LOCAL BACKGROUND against which <paramref name="g"/>'s interior is judged: a ring just
    /// outside the quad, read at the percentile on the PAGE side. See <see cref="InkThreshold"/> for
    /// why the measurement is premultiplied luminance against a local background and not the alpha
    /// channel.
    /// <para>Measured ONCE per glyph at its PREDICTED position and then reused for every offset the
    /// registration search tries. That is deliberate and it is sound: the plate under a line of text
    /// is far larger than one glyph advance, so the page level a few texels away is the same level,
    /// and re-measuring a ring per offset would multiply the search cost by twenty for no
    /// information.</para>
    /// </summary>
    private static float InkLocalBackground(Unity.Collections.NativeArray<Color32> data, InkPlane p,
                                            InkGlyph g)
    {
        int w = p.W, h = p.H;
        float x0 = g.X0 * p.Scale - p.X, x1 = g.X1 * p.Scale - p.X;
        float y0 = g.Y0 * p.Scale, y1 = g.Y1 * p.Scale;

        // ---- the local background: a ring just outside the quad ---------------------------------
        int ring = 0;
        int rx0 = Mathf.FloorToInt(x0) - InkRingBandTexels;
        int rx1 = Mathf.CeilToInt(x1) + InkRingBandTexels;
        int ry0 = Mathf.FloorToInt(y0) - InkRingBandTexels;
        int ry1 = Mathf.CeilToInt(y1) + InkRingBandTexels;
        int strideX = Mathf.Max(1, (rx1 - rx0) / 12);
        int strideY = Mathf.Max(1, (ry1 - ry0) / 12);
        for (int x = rx0; x <= rx1 && ring < InkRing.Length; x += strideX)
        {
            AddRingSample(data, w, h, p, x, ry0, ref ring);
            AddRingSample(data, w, h, p, x, ry1, ref ring);
        }
        for (int y = ry0; y <= ry1 && ring < InkRing.Length; y += strideY)
        {
            AddRingSample(data, w, h, p, rx0, y, ref ring);
            AddRingSample(data, w, h, p, rx1, y, ref ring);
        }
        // ---- and which END of that ring is the background ---------------------------------------
        // A plain MEDIAN is wrong here, and wrong in the expensive direction. A glyph in the middle of
        // a word has its left and right ring columns sitting on its NEIGHBOURS' ink, so up to half the
        // ring can be ink; the median then lands between page and ink, an EMPTY quad deviates from it,
        // and the census reports INK PRESENT for a hole. That is precisely the false all-clear that
        // would wrongly exonerate the capture path, which is the one answer this instrument must not
        // be able to give by accident.
        //
        // The polarity is not guessed either: the strip-wide background (a median over the whole
        // strip, which is dominated by page and not by glyphs) says whether this is light text on a
        // dark page or dark text on a light one, and the ring percentile is taken on the PAGE side —
        // the 25th percentile for a dark page, the 75th for a light one. Contamination by neighbouring
        // ink then pushes samples AWAY from the value being read, so it cannot corrupt the estimate.
        //
        // ModBuild 208, and it is why the ring survives minification: at mip 2 neighbouring glyphs sit
        // ~3 texels apart, so a ring 2 texels outside a quad lands squarely on them. The percentile is
        // taken on the PAGE side precisely so contamination by neighbouring ink pushes samples AWAY
        // from the value being read — the estimate degrades towards "more page", never towards "more
        // ink", and an over-read background is the direction that would make this instrument report a
        // hole where there is none.
        if (ring < 4)
            return p.Background; // the quad sits against the strip edge; the plane-wide median stands in
        System.Array.Sort(InkRing, 0, ring);
        bool darkPage = p.Background < 0.5f;
        return darkPage ? InkRing[ring / 4] : InkRing[ring - 1 - ring / 4];
    }

    /// <summary>
    /// Score <paramref name="g"/>'s quad SHIFTED by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// texels: the largest absolute deviation of any interior sample from <paramref name="bg"/>, plus
    /// the ink CENTROID's displacement from the shifted quad's centre.
    ///
    /// <para>The shift is the whole of ModBuild 206. At (0,0) this is exactly ModBuild 205's ink test
    /// and nothing about the verdict thresholds changed; every other offset is the registration search
    /// asking "and is the ink over HERE instead".</para>
    ///
    /// <para><paramref name="clipped"/> reports that a sample fell outside the census strip, which is
    /// how a search that ran off the edge becomes AMBIGUOUS instead of silently scoring low — an
    /// out-of-strip offset would otherwise look exactly like an offset with no ink at it.</para>
    /// </summary>
    /// <summary>
    /// <b>READ THE CAPTURE UNDER EVERY NON-TEXT GRAPHIC THAT BELIEVED IT DREW.</b> Runs on the mip 0
    /// plane only, inside that plane's callback, because that is the only place its buffer is alive.
    ///
    /// <para>See <see cref="InkPlate"/> for why the verdict is one-sided. In short: a flat region at
    /// the strip background is the only reading that cannot also be a dark image, and every other
    /// reading is reported as unjudged rather than as evidence in either direction.</para>
    /// </summary>
    private static void JudgeInkPlates(Unity.Collections.NativeArray<Color32> data, InkCensus c,
                                       InkPlane p)
    {
        c.PlatesBlank = c.PlatesPresent = c.PlatesUnjudged = c.PlatesOutsideStrip = 0;
        if (c.Plates.Count == 0)
        {
            c.PlateNote = " (no non-text graphic in this window reported itself as drawn inside the "
                          + "census strip, so there is nothing to read the capture under.)";
            return;
        }

        float start = Time.realtimeSinceStartup;
        int w = p.W, h = p.H;
        InkPlateSb.Length = 0;
        int named = 0;

        for (int i = 0; i < c.Plates.Count; i++)
        {
            InkPlate plate = c.Plates[i];
            float x0 = plate.X0 * p.Scale - p.X, x1 = plate.X1 * p.Scale - p.X;
            float y0 = plate.Y0 * p.Scale, y1 = plate.Y1 * p.Scale;

            // WHOLLY inside the strip, exactly as the glyph quads are required to be. A plate hanging
            // over the edge would be judged on the part that happens to be in frame, and "the left
            // third of this image is blank" is not a sentence this instrument can honestly produce.
            if (x0 < 0f || x1 > w || y0 < 0f || y1 > h)
            {
                c.PlatesOutsideStrip++;
                continue;
            }

            float worst = 0f;
            double sum = 0;
            int samples = 0;
            for (int sy = 0; sy < InkPlateSamples; sy++)
            {
                float fy = y0 + (y1 - y0) * (sy + 0.5f) / InkPlateSamples;
                int py = Mathf.FloorToInt(fy);
                if (py < 0 || py >= h)
                    continue;
                int row = (p.Flipped ? (h - 1 - py) : py) * w;
                for (int sx = 0; sx < InkPlateSamples; sx++)
                {
                    float fx = x0 + (x1 - x0) * (sx + 0.5f) / InkPlateSamples;
                    int px = Mathf.FloorToInt(fx);
                    if (px < 0 || px >= w)
                        continue;
                    float d = Mathf.Abs(InkValue(data[row + px]) - p.Background);
                    if (d > worst)
                        worst = d;
                    sum += d;
                    samples++;
                }
            }
            if (samples == 0)
            {
                c.PlatesUnjudged++;
                continue;
            }
            plate.Worst = worst;
            plate.Mean = (float)(sum / samples);
            plate.Samples = samples;
            c.Plates[i] = plate;

            bool blank = worst < InkThreshold;
            if (blank)
                c.PlatesBlank++;
            else
                c.PlatesPresent++;

            if (named < MaxInkPlatesNamed && blank)
            {
                named++;
                InkPlateSb.Append(" '").Append(plate.Name).Append("' (").Append(plate.Kind)
                          .Append(") over ").Append((x1 - x0).ToString("F0")).Append('x')
                          .Append((y1 - y0).ToString("F0")).Append(" texels: worst deviation ")
                          .Append((worst * 255f).ToString("F1")).Append("/255, mean ")
                          .Append((plate.Mean * 255f).ToString("F1")).Append("/255 over ")
                          .Append(samples).Append(" sample(s).");
            }
        }

        ChargeInkBudget((Time.realtimeSinceStartup - start) * 1000.0);

        c.PlateNote = $" {c.PlatesPresent} held content, {c.PlatesBlank} were FLAT AT THE STRIP "
                      + $"BACKGROUND, {c.PlatesOutsideStrip} were not wholly inside the strip and "
                      + $"{c.PlatesUnjudged} could not be sampled, of {c.Plates.Count} listed "
                      + $"({c.PlatesSeen} non-text graphic(s) reported themselves drawn; the list is "
                      + $"capped at {MaxInkPlates} and plates under {MinInkPlateTexels:F0} texels on "
                      + "an axis are not sampled at all)."
                      + (InkPlateSb.Length > 0
                          ? " THE FLAT ONES, NAMED — a graphic that uGUI says drew, at full alpha, "
                            + "unclipped, with NOTHING under it in the capture:" + InkPlateSb
                          : " No graphic that believed it drew came back flat.");
        InkPlateSb.Length = 0;
    }

    private static float InkQuadScore(Unity.Collections.NativeArray<Color32> data, InkPlane p,
                                      InkGlyph g, float dx, float dy, int n, float bg,
                                      out float cx, out float cy, out bool clipped)
    {
        int w = p.W, h = p.H;
        cx = 0f;
        cy = 0f;
        clipped = false;
        // The quad is in LEVEL-0 texels and this plane may be a mip level, so both the quad and the
        // strip origin are scaled. (dx, dy) are in THIS plane's texels: the registration search only
        // ever runs on the mip 0 plane, where the two are the same thing.
        float x0 = g.X0 * p.Scale - p.X + dx, x1 = g.X1 * p.Scale - p.X + dx;
        float y0 = g.Y0 * p.Scale + dy, y1 = g.Y1 * p.Scale + dy;
        float insetX = (x1 - x0) * InkQuadInset;
        float insetY = (y1 - y0) * InkQuadInset;
        float ix0 = x0 + insetX, ix1 = x1 - insetX;
        float iy0 = y0 + insetY, iy1 = y1 - insetY;
        float centreX = (x0 + x1) * 0.5f, centreY = (y0 + y1) * 0.5f;

        float worst = 0f;
        double wsum = 0, wx = 0, wy = 0;
        for (int sy = 0; sy < n; sy++)
        {
            float fy = iy0 + (iy1 - iy0) * (sy + 0.5f) / n;
            int py = Mathf.FloorToInt(fy);
            if (py < 0 || py >= h)
            {
                clipped = true;
                continue;
            }
            int row = (p.Flipped ? (h - 1 - py) : py) * w;
            for (int sx = 0; sx < n; sx++)
            {
                float fx = ix0 + (ix1 - ix0) * (sx + 0.5f) / n;
                int px = Mathf.FloorToInt(fx);
                if (px < 0 || px >= w)
                {
                    clipped = true;
                    continue;
                }
                float d = Mathf.Abs(InkValue(data[row + px]) - bg);
                if (d > worst)
                    worst = d;
                // The centroid is weighted by deviation and only over samples that ARE ink, so the
                // page around a thin stem cannot drag it back toward the quad centre and make a real
                // displacement read as zero.
                if (d >= InkThreshold)
                {
                    wsum += d;
                    wx += d * (fx - centreX);
                    wy += d * (fy - centreY);
                }
            }
        }
        if (wsum > 0)
        {
            cx = (float)(wx / wsum);
            cy = (float)(wy / wsum);
        }
        return worst;
    }

    private static void AddRingSample(Unity.Collections.NativeArray<Color32> data, int w, int h,
                                      InkPlane p, int x, int y, ref int ring)
    {
        if (ring >= InkRing.Length || x < 0 || x >= w || y < 0 || y >= h)
            return;
        int row = (p.Flipped ? (h - 1 - y) : y) * w;
        InkRing[ring++] = InkValue(data[row + x]);
    }

    /// <summary>PREMULTIPLIED LUMINANCE, 0..1 — see <see cref="InkThreshold"/>. Over the transparent
    /// clear this degenerates to alpha (background 0); over an opaque plate it is plain luminance.</summary>
    private static float InkValue(Color32 p)
        => (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) * p.a / (255f * 255f);

    private static float InkMedian(List<float> values)
    {
        if (values.Count == 0)
            return -1f;
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>Pearson correlation of the predicted band profile against the measured one, optionally
    /// reversed. Returns 0 when either profile is flat, which the caller treats as UNDECIDED.</summary>
    private static float InkCorrelation(float[] predicted, float[] measured, bool reverse)
    {
        int n = predicted.Length;
        double sa = 0, sb = 0;
        for (int i = 0; i < n; i++)
        {
            sa += predicted[i];
            sb += measured[i];
        }
        double ma = sa / n, mb = sb / n;
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < n; i++)
        {
            double a = predicted[i] - ma;
            double b = measured[reverse ? n - 1 - i : i] - mb;
            num += a * b;
            da += a * a;
            db += b * b;
        }
        if (da <= 1e-12 || db <= 1e-12)
            return 0f;
        return (float)(num / System.Math.Sqrt(da * db));
    }

    private static void Fail(System.Exception ex, string where)
    {
        if (_errorLogged)
            return;
        _errorLogged = true;
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE failed in {where} ({ex.GetType().Name}: {ex.Message}) "
                          + "— the path stands down completely. THE CONSEQUENCE: every floated window "
                          + "goes back to being rasterized directly into the eye, i.e. exactly the "
                          + "behaviour with the dial off, including the reported shimmer. Nothing "
                          + "about input, placement or multiplayer changes.");
        try
        {
            StandDownAll("a failure in " + where);
        }
        catch
        {
            // Nothing left to do: never throw out of a tick.
        }
    }
}
