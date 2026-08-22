// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here.

using UnityEngine;
using UnityEngine.XR;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- the report ---------------------------------------------------------------------------

    /// <summary>
    /// One state line per supersampled panel, on a 10 s cadence, hit or no hit. Shaped so the NEXT
    /// hardware round is decidable from the log alone — including the outcome in which this approach
    /// does not help.
    /// </summary>
    private static void Report(Entry e)
    {
        Camera? head = VRRigDriver.HeadCamera;
        RenderTexture shownForBias = DisplayTexture(e);
        string sampling = SamplingSentence(e, head, shownForBias != null ? shownForBias.mipMapBias : 0f);
        float ms = e.Captures > 0 ? (float)(e.CaptureMs / e.Captures) : 0f;
        float sweepMs = e.Sweeps > 0 ? (float)(e.SweepMs / e.Sweeps) : 0f;
        float contentMs = e.ContentMeasures > 0 ? (float)(e.ContentMs / e.ContentMeasures) : 0f;
        // READ BACK FROM THE TEXTURE THE EYE ACTUALLY SAMPLES — never from what was requested. The
        // ModBuild 192 line reported "MSAA 4x" from a local variable while the mip state came from
        // the target, which is why the mip failure was visible in the log and the MSAA state was
        // not independently confirmed. Both come from the objects now.
        RenderTexture shown = DisplayTexture(e);
        int mips = shown != null ? shown.mipmapCount : 0;
        int captureAa = e.Rt != null ? e.Rt.antiAliasing : 0;

        Sb.Length = 0;
        Sb.Append("PANEL SUPERSAMPLE '").Append(e.Window).Append("': host rect ")
          .Append(e.HostRectAtMeasure.width.ToString("F0")).Append('x')
          .Append(e.HostRectAtMeasure.height.ToString("F0"))
          .Append(" uGUI px, CAPTURE FRAME ").Append(e.Frame.width.ToString("F0")).Append('x')
          .Append(e.Frame.height.ToString("F0"))
          .Append(e.ExpandX > 0.5f || e.ExpandY > 0.5f
              ? $" (GROWN by {e.ExpandX:F0}x{e.ExpandY:F0} px to cover content drawn outside the host "
                + $"frame{(e.ExpandClamped ? "; CLAMPED — content IS being cropped" : "")})"
              : " (= the host rect: no content draws outside it)")
          .Append(" -> RT ").Append(e.RtW).Append('x').Append(e.RtH)
          .Append(" (factor ").Append(e.Factor.ToString("F2")).Append("), capture MSAA ")
          .Append(captureAa)
          .Append("x -> mipped display target: mips ").Append(mips)
          .Append(mips > 1 ? " RESOLVED+GENERATED after every capture"
                           : e.MipFallback ? " NONE (mip target refused; showing the capture target)"
                                           : " NONE")
          .Append(", ").Append(shown != null ? shown.filterMode.ToString() : "?")
          .Append(" aniso ").Append(shown != null ? shown.anisoLevel : 0)
          .Append(", ").Append(Mb(e.VramBytes)).Append(" MB VRAM (session total ")
          .Append(Mb(_vramTotal)).Append(" of ").Append(Mb(MaxTotalVramBytes)).Append(" MB across ")
          .Append(Entries.Count).Append(" panel(s), cap ").Append(MaxPanels).Append("); ")
          .Append(sampling)
          .Append(" CAPTURE: ").Append(e.Captures).Append(" so far, every ")
          .Append(CaptureIntervalFrames).Append(" frame(s), ").Append(ms.ToString("F2"))
          .Append(" ms CPU submit each — which includes SUBMITTING the resolve blit and the mip "
                  + "generation but not their GPU time, so if frame time regresses in this build "
                  + "the first suspect is N panels x (one full-target blit + one mip chain) per "
                  + "frame, and this number cannot see it. THE ModBuild 198 FACTOR IS NOW THE FIRST "
                  + "LEVER on that GPU cost and it must be named here: raising the factor to 2.0 "
                  + "quadruples the pixels in both the blit and the mip chain, partly offset by the "
                  + "MSAA 4x -> 1x trade on the read side (the blit no longer resolves four samples). "
                  + "If the MOTION BUDGET field below shows this build's frames worse than ModBuild "
                  + "197's, lower [WorldUI] PanelSupersampleFactor before touching MaxPanels or "
                  + "CaptureIntervalFrames — a user-set value is taken verbatim and disables the "
                  + "floor. MOTION (this 10 s window): ").Append(e.MotionFrames)
          .Append(" frame(s) with a pose/scale/rect change out of ").Append(e.MotionTicks)
          .Append(" comparison(s), ").Append(e.GeometryDirtyEvents)
          .Append(" of them a RESIZE/RESCALE that forced a re-measure; ").Append(e.Reallocations)
          .Append(" re-allocation(s) since engage, currently ").Append(IsMoving(e) ? "MOVING" : "still")
          .Append(". INSTRUMENT SELF-CHECK: largest single-frame host step ")
          .Append(e.MaxStepWorld.ToString("F4")).Append(" world units = ")
          .Append((e.MaxStepWorld / Mathf.Max(e.WorldPerAuthoredPx, 1e-9f)).ToString("F2"))
          .Append(" authored px = ")
          .Append((e.MaxStepWorld / Mathf.Max(e.WorldPerAuthoredPx, 1e-9f)
                   / Mathf.Max(e.AuthoredPerRenderedPx, 1e-6f)).ToString("F2"))
          .Append(" RENDERED eye px, against an epsilon of ").Append(e.MotionEpsWorld.ToString("F4"))
          .Append(" world units = 0.50 authored px (the host measures ")
          .Append(e.WorldPerAuthoredPx.ToString("F5"))
          .Append(" world units per authored px). ISOLATION: private capture layer ").Append(e.Layer)
          .Append(" (pool ").Append(PoolSize).Append(", ").Append(FreeLayers.Count)
          .Append(" free), display quad sortingOrder ")
          .Append(e.DisplayCanvas != null ? e.DisplayCanvas.sortingOrder : -1).Append("; ")
          .Append(ForeignPanelsInFrustum(e))
          .Append(" other supersampled panel(s) currently inside THIS camera's frustum. LAYERS: ")
          .Append(e.Relayered.Count)
          .Append(" transform(s) on capture layer ").Append(e.Layer).Append(", ")
          .Append(e.LateJoiners).Append(" late joiner(s) swept since engage, ").Append(e.Sweeps)
          .Append(" sweep(s) at ").Append(sweepMs.ToString("F2")).Append(" ms each, ")
          .Append(e.ContentMeasures).Append(" content measure(s) at ")
          .Append(contentMs.ToString("F2")).Append(" ms each, ")
          .Append(e.ForeignSkipped).Append(" foreign render subtree(s) LEFT ALONE, ")
          .Append(e.ForeignLayerSkipped)
          .Append(" transform(s) left alone because ANOTHER panel owns their layer (expect 0), ")
          .Append(e.NestedCaptured).Append('/').Append(e.NestedTotal)
          .Append(" nested canvas(es) on the capture layer.");

        AppendContentIntegrity(e);
        AppendCapturePath(e);
        AppendMotionBudget(e);

        Sb.Append(" HOW TO READ THIS LINE — MODBUILD 197 FIRST, AND IT RETIRES THE ModBuild 196 "
                  + "HEADLINE BELOW RATHER THAN EXTENDING IT. (Y1) THE ModBuild 196 GLYPH SCAN RAN, "
                  + "IT WORKED, AND IT MEASURES THE WRONG QUANTITY. Its whole output was counted "
                  + "rather than sampled: 236 readings, 152 of them '0 not in atlas / 0 not visible / "
                  + "0 zero-area' and 84 of them exactly one parsed-but-not-visible, with 0 atlas "
                  + "repacks, in a session containing real drags and up to 140 release repairs on one "
                  + "window. The photograph shows dozens of missing glyphs across six rows. A working "
                  + "instrument that never coincides with the defect is measuring something else, and "
                  + "the something else is named: every ModBuild 196 counter reads "
                  + "TMP_CharacterInfo, which is the LAYOUT RECORD written before any mesh exists. "
                  + "THE NEW 'SUBMITTED MESH' FIELD reads the vertex and UV arrays that are actually "
                  + "uploaded. A glyph whose four atlas UVs have collapsed onto one texel keeps its "
                  + "full advance, keeps a full-area quad, reports isVisible, exists in the atlas — "
                  + "and draws a flat SDF value, i.e. NOTHING. That is the photograph's exact "
                  + "signature and no ModBuild 196 counter could see it. IF THE MESH COUNTERS ALSO "
                  + "STAY AT ZERO through a session in which the user sees the defect, then the fault "
                  + "is not in the text at ANY level and the remaining fields — CAPTURE PATH and "
                  + "MOTION BUDGET — are where the next round must look; text is then finished as a "
                  + "hypothesis. (Y2) THE RELEASE REPAIR WAS A NO-OP FOR TEXT AND THE LOG SAYS SO. "
                  + "ModBuild 196 documented it as forcing a regeneration of every text component on "
                  + "release. Its regeneration is gated on the defect count, which is permanently "
                  + "zero, so across 236 lines it re-generated at most ONE component and usually "
                  + "none, on windows carrying up to 297 components. The user reporting 'no "
                  + "improvement' is therefore the EXPECTED result and carries no information about "
                  + "the text hypothesis at all. From this build the release pass is unconditional "
                  + "and its component count and cost are printed, so the next report either shows "
                  + "the defect surviving a genuine full regeneration — which kills the family — or "
                  + "shows it gone. (Y3) 'PANEL SUPERSAMPLE RELEASE' IS A SEPARATE LINE, ONE PER "
                  + "RELEASE, and it is where the user's new word ABRUPT is answered: it prints the "
                  + "LAST single-frame host step before the hand let go next to the drag's LARGEST, "
                  + "both in rendered eye pixels, plus how many frames the settle gate took against "
                  + "its threshold and how many frames of the released image the eye had ALREADY "
                  + "drawn before any repair ran. That last number bounds what a repair on this "
                  + "schedule can ever fix. (Y4) THE TWO SYMPTOMS ARE SEPARATE AND MUST NOT BE "
                  + "MERGED. 'Flackern beim Verschieben' is about a window in motion; 'kaputte "
                  + "Anzeige beim Loslassen' is about a window at rest afterwards. The CAPTURE PATH "
                  + "field settles the first one's precondition — if captures and resolves while "
                  + "MOVING equal the moving frame count, a dragged window takes exactly the same "
                  + "path as the still window the user has already accepted as fixed, so 'the "
                  + "supersampling switches off while I drag' is dead and what is left for the moving "
                  + "case is either sub-pixel sampling (field C) or JUDDER (MOTION BUDGET and the "
                  + "RELEASE line's dropped-frame count). "
                  + "NOW THE MODBUILD 196 CLAUSES. (Z1) 'CONTENT INTEGRITY' IS THE "
                  + "HEADLINE AND IT DECIDES THE BROKEN-ON-RELEASE REPORT ON ITS OWN. The user, after "
                  + "195: 'beim Loslassen kann es passieren, dass die dargestellte Anzeige kaputt ist "
                  + "... bewege ich es nochmal und lasse los, sieht es wieder anders aus'. The "
                  + "photograph (.planning/debug/kaputte_anzeige.jpg) was MEASURED, not described: "
                  + "the six stat labels lose individual GLYPHS while their LAYOUT stays exact (the "
                  + "trailing colons of 'Verstärkungen:' and 'Verbesserungen:' are one character's "
                  + "advance apart, so nothing was substituted or removed), the gaps read background "
                  + "luminance (24 against a 20 background and a 147 ink, i.e. empty and not merely "
                  + "dim), the gaps do NOT line up into vertical stripes across the rows, and the "
                  + "SAME window's SMALLER text renders every character. THOSE FOUR MEASUREMENTS KILL "
                  + "THE PRIOR DIAGNOSIS: this is not undersampled rasterization — minification dims "
                  + "and blurs uniformly, it does not delete some glyphs of one label and leave a "
                  + "smaller label perfect. What is left is that the characters keep their advances "
                  + "and their quads put no pixels down. READ THE FIELD LIKE THIS. 'NOT IN THE FONT "
                  + "ATLAS' non-zero => the font asset genuinely cannot serve the string, the "
                  + "dynamic-atlas hypothesis is CONFIRMED, and re-taking the capture would be "
                  + "useless (the repair re-requests the characters instead). 'marked NOT VISIBLE' "
                  + "non-zero => the text engine itself decided not to draw them (overflow "
                  + "truncation, a missing-glyph replacement, a maxVisibleCharacters clamp) and the "
                  + "capture is FAITHFUL — the content really is absent. 'ZERO-AREA quad' non-zero => "
                  + "the character is visible and draws nothing, which is EXACTLY the photograph, and "
                  + "that is the finding. ALL THREE ZERO ACROSS A SESSION IN WHICH THE USER SEES THE "
                  + "DEFECT RETIRES THE WHOLE FAMILY and the next round must look at the composite "
                  + "(BuildDisplay's premultiplied-alpha note) or at the display sampling, not at the "
                  + "text. A zero next to '0 glyph lookup(s)' is NOT the same statement — that is an "
                  + "instrument that never ran, and the lookup count is printed for exactly that "
                  + "reason. (Z1b) WHAT THIS FIELD DOES NOT MEASURE, STATED SO IT CANNOT BE "
                  + "OVER-READ. Every glyph counter above measures the TEXT SOURCE — does the glyph "
                  + "exist, did the layout mark it visible, does its quad have area — which is "
                  + "upstream of both the capture and the composite. 'put NO pixels into the capture "
                  + "at all' is the only field that speaks to whether a component draws, and it "
                  + "catches exactly one cause (a culled CanvasRenderer or zero alpha). NOTHING HERE "
                  + "CAN SEE A GRAPHIC THAT DREW CORRECTLY AND WAS THEN PAINTED OVER, and to a player "
                  + "that is indistinguishable from a missing element. That question belongs to the "
                  + "draw order — the 'display quad sortingOrder' field above and CanvasConversion's "
                  + "own order ladder — so a clean CONTENT INTEGRITY scan is NOT evidence that "
                  + "nothing is covering anything. For the record, the photograph this instrument was "
                  + "built for rules occlusion out on its own: an occluder is a rectangle, and the "
                  + "measured gaps are at CHARACTER granularity inside words, with ink on both sides "
                  + "of each gap and no vertical alignment across the six rows. (Z2) 'COINCIDENCE AT THE CAPTURE INSTANT' answers the other two candidate "
                  + "causes with counts instead of samples: captures that ran on the same frame as an "
                  + "atlas repack, and captures that ran before uGUI's canvas rebuild. The second is "
                  + "expected to be permanently 0 and is printed anyway, because a 0 with a "
                  + "denominator is evidence and an absent line is not. 'ATLAS REPACKS THIS SESSION' "
                  + "counts BOTH legacy Font.textureRebuilt events AND TextMeshPro atlas changes — TMP "
                  + "does not raise that event, and this game's labels are TextMeshProUGUI throughout, "
                  + "so a hook on the legacy event alone would have been blind to the only font family "
                  + "that matters here. THE TMP HALF OF THAT COUNT HAS A LATENCY and the legacy half "
                  + "does not: Font.textureRebuilt is an event and is exact to the frame, while a TMP "
                  + "atlas change is DETECTED by fingerprinting the atlas texture count and id, which "
                  + "only happens when this scan runs — on every release and once per report. So a "
                  + "TMP repack is counted, and its repair does run, but its 'same frame as a capture' "
                  + "coincidence cannot be attributed and will read 0 even when a repack occurred. "
                  + "Read the REPACK COUNT for TMP and the COINCIDENCE COUNT for the legacy font. "
                  + "(Z3) 'MOTION BUDGET' IS THE MOVING-FLICKER FIELD AND IT "
                  + "REPLACES AN ARGUMENT WITH A MEASUREMENT. ModBuild 193's movement remedy — sweep "
                  + "the capture layer EVERY frame while moving — is falsified as a fix and the 195 "
                  + "log priced it: of the 23 sweeps that actually found a late transform, 22 ran on "
                  + "the PERIODIC cadence and exactly ONE on the per-frame motion cadence, while the "
                  + "sweep itself averaged 1.48-1.94 ms on a session whose frametime reads p50 17.33 "
                  + "ms, p95 24.98, p99 29.93, max 51.75 against an 11.11 ms budget. Meanwhile the "
                  + "INSTRUMENT SELF-CHECK field measured real drags at 12, 19, 33, 47, 74, 94, 122 "
                  + "and 133 RENDERED eye pixels per frame — so a dragged window CANNOT survive a "
                  + "dropped frame, and the per-frame sweep was spending a tenth of the frame "
                  + "precisely when the frame could least afford it. The motion cadence is now "
                  + "5 frames and the release is covered outright by the repair above. READ IT LIKE "
                  + "THIS: if 'while MOVING' mean/worst is materially worse than 'while STILL' and "
                  + "'over the threshold' is high, the moving flicker is DROPPED FRAMES against a "
                  + "window travelling tens of pixels per frame — judder, which no filtering reaches "
                  + "and which is chased in the frame budget, not on the panel surface. If the two "
                  + "buckets are indistinguishable, the drag costs nothing extra and the complaint "
                  + "must be a sampling one after all, which is what field (C) below measures. "
                  + "(A) 'ISOLATION' WAS THE MODBUILD 194 HEADLINE AND IT "
                  + "ANSWERS THE USER'S REPORT THAT ONE WINDOW WAS DRAWING ANOTHER INSIDE ITSELF "
                  + "(.planning/debug/window_merge.jpg: the floated quest card showing the merchant "
                  + "window's item rows in its own rect). ModBuild 193 resolved ONE capture layer for "
                  + "the whole mod and built every per-panel camera with that single bit, so each "
                  + "camera drew EVERY supersampled panel inside its frustum into its own render "
                  + "target — and that frustum is as deep as the window is tall, on windows standing "
                  + "side by side on an arc. Every panel now owns a PRIVATE layer out of a pool. READ "
                  + "IT LIKE THIS: 'private capture layer N' must be DIFFERENT on every panel's line "
                  + "in the same report window — if two lines ever show the same number, the pool is "
                  + "broken and the merge bug is back. 'K other supersampled panel(s) currently "
                  + "inside THIS camera's frustum' is NOT a defect in this build: those panels are on "
                  + "other layers and cannot be drawn here. It is the measurement of how often the "
                  + "193 bug WAS firing, so a non-zero K is the confirmation that this fix was "
                  + "load-bearing; a K that is permanently 0 on every panel means the shared layer "
                  + "cannot explain the photograph and the next round must look elsewhere (the test "
                  + "is the neighbour's HOST RECT as a world-space AABB against the frustum planes: "
                  + "it counts a box straddling a frustum corner as inside, and it cannot see a "
                  + "neighbour's content spilled outside its own rect, so read it as a magnitude and "
                  + "not as a proof — the proof is the private layer). 'display quad sortingOrder' "
                  + "is here because "
                  + "CanvasConversion rewrites every panel's draw order EVERY frame from its measured "
                  + "eye distance: two overlapping windows whose orders SWAP mid-drag would pop in "
                  + "front of each other, which is its own flicker and is not this class's to fix — "
                  + "if two panels ever print the same sortingOrder while overlapping, that is the "
                  + "finding. "
                  + "(B) 'INSTRUMENT SELF-CHECK' RETIRES A WRONG READING OF THE 193 LOG. The MOTION "
                  + "field was read as zero everywhere and the detector was declared blind. It is "
                  + "not: 23 of the 193 log's 226 state lines carry a non-zero motion count, up to "
                  + "542 frames of a 900-frame window, several reading 'currently MOVING'. THE "
                  + "CONSEQUENCE FOR THE NEXT ROUND IS THE OPPOSITE OF WHAT WAS ASSUMED: the "
                  + "per-frame layer sweep while moving, the forced re-measure and the reallocation "
                  + "check all DID run during real drags, and the user still reported the movement "
                  + "shimmer unchanged — so those three remedies are FALSIFIED as its cause, not "
                  + "untested. The self-check makes the zero unambiguous from now on: 'N frame(s) "
                  + "with a change out of M comparison(s)' plus the largest single-frame step and the "
                  + "epsilon it was tested against, in world units AND authored px. M = 0 means the "
                  + "detector never ran. A step far ABOVE the epsilon with N = 0 means it is blind. A "
                  + "step below the epsilon with a large M means the window really was still. Those "
                  + "three used to print the same character. THE 'RENDERED eye px' FIGURE IN THAT "
                  + "FIELD IS THE ONE THAT DECIDES WHAT KIND OF DEFECT A DRAG CAN BE, and it is new. "
                  + "If a dragged window moves several RENDERED pixels per frame, a single frame of "
                  + "pose lag is several pixels of positional error and the complaint is JUDDER, "
                  + "which no filtering can fix and which would have to be chased in the pose path "
                  + "(the host is written from the hand in Update/LateUpdate while the HMD pose is "
                  + "late-latched immediately before the render). If it moves well under one "
                  + "rendered pixel per frame, pose lag is invisible by construction and the "
                  + "complaint can only be sub-pixel SAMPLING, which is what (C) measures. Two "
                  + "ordering explanations are already DEAD and must not be re-opened: the capture "
                  + "camera sits at depth -200 and every other camera in the 193 log sits at -1, 0 "
                  + "or 1, so the resolve blit and GenerateMips in its onPostRender always complete "
                  + "before the head camera culls; and the capture camera is a CHILD of the host, so "
                  + "a pure translation cannot move the panel inside the captured image at all. "
                  + "(C) 'trilinear MIP LOD' IS THE LIVE HYPOTHESIS FOR THE REMAINING MOVING "
                  + "SHIMMER, now that mips are confirmed present (193 read 'mips 10' and 'mips 11' "
                  + "and the shimmer did not change, which kills the missing-mip-chain explanation "
                  + "outright). Trilinear does not REMOVE aliasing at a fractional LOD, it "
                  + "attenuates it: at 1.34 texels per rendered pixel the LOD is 0.42 and 58 % of "
                  + "every sample still comes from level 0, which at 1.34x minification is genuinely "
                  + "undersampled. A frozen alias pattern is invisible; the same pattern under a "
                  + "moving window crawls. Anisotropic filtering makes it worse for a panel facing "
                  + "the seat, because aniso selects a LOWER lod. HOW TO DECIDE IT NEXT ROUND: if the "
                  + "user still reports movement shimmer and this field reads a level-0 share above "
                  + "roughly 30 %, the levers in order of cost are mipMapBias (+0.5 to +1.0 on the "
                  + "display target — one line in AttachMipTarget, and it costs sharpness while "
                  + "still) and [WorldUI] PanelSupersampleFactor above 1.0 (which buys texels until "
                  + "the ratio drops below 1.0, at which point there is no minification left to "
                  + "alias and this hypothesis is dead by construction). If the field already reads "
                  + "0 % and the shimmer persists, the panel surface is fully band-limited and the "
                  + "cause is not on it. "
                  + "(0) 'mips' IS THE OLD HEADLINE. ModBuild 192 "
                  + "read 'mips 1 NONE' on every panel because a render target cannot be "
                  + "multisampled AND mipmapped, so the whole mip half of this design never ran and "
                  + "the eye was still minifying an unfiltered texture — invisible while everything "
                  + "was still, crawling the moment the window or the head moved, which is exactly "
                  + "what the user reported as 'flackert beim Verschieben'. The capture is now "
                  + "resolved into a second single-sample target and the chain is generated THERE. "
                  + "If this field reads anything other than a number well above 1, that fix is not "
                  + "working and the movement shimmer WILL still be reported. (0b) 'CAPTURE FRAME' "
                  + "is the second headline: if it reads GROWN, this window draws outside its own "
                  + "host rect and ModBuild 192 was CROPPING that content away — the growth is the "
                  + "fix, and its cost is that this window's ON geometry is larger than its OFF "
                  + "geometry by exactly that many uGUI pixels of transparent margin. If it reads "
                  + "CLAMPED, content is STILL being cropped and MaxContentExpansion is the dial. "
                  + "(0c) MOTION is how the next round decides the release case: 'frame(s) with a "
                  + "pose/scale/rect change' counts drags, 'forced a re-measure' counts the "
                  + "resize/rescale events that re-derived the frame and the projection, and "
                  + "'re-allocation(s)' counts how often the render target had to follow. If the "
                  + "user still reports wrong or missing elements after a release and this line "
                  + "shows re-measures and re-allocations happening, the frame is NOT the cause and "
                  + "the next suspect is the layer sweep — read 'late joiner(s)' and the "
                  + "nested-canvas ratio below. THE 193 EDITION OF THIS CLAUSE SAID that zero motion "
                  + "frames in a session with real drags would mean a blind instrument; that reading "
                  + "was applied to the 193 log and it was WRONG, because zero is what MOST report "
                  + "windows show even in a session full of dragging — only the panel actually being "
                  + "dragged counts frames, and 23 of 226 lines did. Use the INSTRUMENT SELF-CHECK "
                  + "field in (B) to decide this, never the bare zero. "
                  + "(1) RT texels per rendered pixel is the same quantity "
                  + "PANEL SAMPLING calls 'panel scale', multiplied by the factor — but it now means "
                  + "something completely different, because the surface being minified is a MIPPED, "
                  + "trilinear, anisotropically filtered texture instead of hundreds of "
                  + "point-sampled graphics. A value above 1 was the DEFECT before this build and is "
                  + "merely a mip level now. (2) If the user reports the windows quiet, the "
                  + "diagnosis was right: the flicker was undersampled RASTERIZATION (uGUI geometry "
                  + "edges, sliced-sprite borders and mipless SDF text), which no mip bake could ever "
                  + "reach. (3) If the text is quiet but the window looks SOFTER than before, the "
                  + "factor is the dial: [WorldUI] PanelSupersampleFactor above 1.0 buys RT texels "
                  + "(and VRAM) for sharpness. (4) IF NOTHING CHANGED AT ALL, this approach is dead "
                  + "and it dies decisively: the whole window is one filtered texture here, so a "
                  + "shimmer that survives cannot be a sampling problem on the panel surface at all "
                  + "— the next round must look at the compositor, the eye buffers or the display "
                  + "path, not at the panel. (5) If the window is BLANK or the character portrait is "
                  + "missing, read the LAYERS counts: 'foreign render subtree(s) LEFT ALONE' are real "
                  + "3D Renderers this pass must never move; they are not captured, and if a window "
                  + "shows its character through one of those rather than through a RawImage, that "
                  + "character will draw in the world in front of the quad instead of inside it. A "
                  + "HIGH count here (the 192 log shows 'UI Shop Item Window' reaching 44 as items "
                  + "pool in) means most of that window's art is NOT going through this path at all: "
                  + "it is neither captured nor band-limited nor supersampled, so it keeps shimmering "
                  + "however good the rest of the window looks, and it is also excluded from the "
                  + "CAPTURE FRAME above — deliberately, because framing pixels that are guaranteed "
                  + "empty would waste the target and pull the frame off the content that IS "
                  + "captured. Excluding it cannot LOSE it: the head camera still draws it directly "
                  + "at its true world pose, exactly as before this path existed. "
                  + "(6) 'nested canvas(es) on the capture layer' must read N/N: a nested canvas left "
                  + "behind would be drawn into the eye directly AND be missing from the capture, "
                  + "i.e. one sharp panel with one shimmering sub-panel on top of it. (7) If the "
                  + "TEXT reads slightly THINNER, or the window looks darker during its ~0.3 s show "
                  + "animation, that is the known and bounded premultiplied-alpha artifact of "
                  + "compositing a transparent render target with the standard UI blend (derived in "
                  + "full at PanelSupersample.BuildDisplay) — it affects PARTIAL coverage only, is "
                  + "exact at alpha 0 and 1, and is a one-line change if it matters. (8) 'late "
                  + "joiner(s)' is the ModBuild 193 answer to 'manche Elemente fehlen im Fenster': "
                  + "every transform counted there arrived on the GAME's UI layer after this panel "
                  + "was engaged, which for however many frames it took to sweep it meant MISSING "
                  + "from the capture and drawn straight into the eye at its own sorting order. The "
                  + "sweep now runs EVERY frame while the window is moving, because that is exactly "
                  + "when GrabbableModal.ThrottleDiagWhileMoving switches the panel's per-frame "
                  + "nested-canvas adoption off. A large late-joiner count with the user reporting "
                  + "the problem GONE confirms the mechanism; a large count with the problem still "
                  + "present means the sweep is not fast enough and the next step is to hook the "
                  + "arrivals rather than to poll for them.");
        VRLog.Info(Scope, Sb.ToString());
        Sb.Length = 0;

        // The MOTION counters are per report window on purpose: "this window was dragged in the
        // last ten seconds" is a decidable statement, "this window has been dragged 4000 times
        // since it opened" is not. Everything else stays cumulative.
        e.MotionFrames = 0;
        e.GeometryDirtyEvents = 0;
        e.MotionTicks = 0;
        e.MaxStepWorld = 0f;
        e.MotionSweeps = 0;
        e.MotionSweepMs = 0.0;
        e.StillSweeps = 0;
        e.StillSweepMs = 0.0;
        e.MotionFrameSamples = 0;
        e.MotionFrameMs = 0.0;
        e.MotionFrameMsMax = 0f;
        e.MotionFramesOverBudget = 0;
        e.StillFrameSamples = 0;
        e.StillFrameMs = 0.0;
        e.StillFrameMsMax = 0f;
        e.StillFramesOverBudget = 0;
        e.ReleasesThisWindow = 0;
        e.ReleaseGateFramesMax = 0;
        e.LongestMotionRun = 0;
    }

    /// <summary>
    /// THE CONTENT-INTEGRITY FIELD — the ModBuild 196 answer to <i>"beim Loslassen kann es passieren,
    /// dass die dargestellte Anzeige kaputt ist"</i>. Every number here carries the comparison count
    /// it came from on the same line, because that is the only way "the instrument never ran",
    /// "it ran and found nothing" and "it ran and found something" can be told apart in a log.
    /// </summary>
    private static void AppendContentIntegrity(Entry e)
    {
        int totalBad = e.GlyphsNotInAtlas + e.GlyphsNotVisible + e.GlyphsBlankQuad;
        Sb.Append(" CONTENT INTEGRITY (the 'kaputte Anzeige' instrument): ").Append(e.ContentScans)
          .Append(" scan(s) so far at ")
          .Append((e.ContentScans > 0 ? e.ContentScanMs / e.ContentScans : 0.0).ToString("F2"))
          .Append(" ms each (").Append(e.ContentScanFailures)
          .Append(" of them THREW and measured nothing); the LAST scan walked ").Append(e.TextComponents)
          .Append(" text component(s) and made ").Append(e.GlyphsChecked)
          .Append(" glyph lookup(s), finding ").Append(totalBad).Append(" defect(s) = ")
          .Append(e.GlyphsNotInAtlas).Append(" character(s) NOT IN THE FONT ATLAS (fallbacks "
                  + "searched), ").Append(e.GlyphsNotVisible)
          .Append(" parsed but marked NOT VISIBLE, ").Append(e.GlyphsBlankQuad)
          .Append(" visible with a ZERO-AREA quad (threshold ").Append(DegenerateQuadArea.ToString("G3"))
          .Append(" local units squared); ").Append(e.TextClean).Append(" of ")
          .Append(e.TextComponents).Append(" component(s) were completely clean and ")
          .Append(e.TextCulled)
          .Append(" put NO pixels into the capture at all (CanvasRenderer culled or zero alpha — a "
                  + "different fault from a missing glyph, and NOT the same as being drawn and then "
                  + "painted over, which nothing in this scan can see)")
          .Append(e.ContentScanTruncated
              ? "; THE SCAN WAS TRUNCATED at " + MaxGlyphChecksPerScan + " lookups / "
                + MaxRegeneratePerScan + " regenerations, so these are LOWER BOUNDS"
              : "; the scan ran to completion, so these are totals")
          .Append(". Worst component: ")
          .Append(e.WorstTextBad > 0
              ? e.WorstText + " with " + e.WorstTextBad + " defect(s) of " + e.WorstTextChecked
                + " lookup(s)"
              : "none — every component was clean")
          .Append(". SUBMITTED MESH (ModBuild 197 — the layer above measures the text SOURCE and "
                  + "this one measures what was actually written into the vertex and UV arrays the "
                  + "eye samples; nothing before this build read a single UV): ")
          .Append(e.MeshQuadsChecked).Append(" glyph quad(s) examined, finding ")
          .Append(e.MeshMissingQuads + e.MeshDegenerateQuads + e.MeshDegenerateUv
                  + e.MeshUvOutOfRange + e.MeshNonFinite)
          .Append(" defect(s) = ").Append(e.MeshMissingQuads)
          .Append(" laid out but NEVER WRITTEN into the generated mesh, ")
          .Append(e.MeshDegenerateQuads).Append(" zero-area IN THE MESH (threshold ")
          .Append(DegenerateQuadArea.ToString("G3")).Append("), ").Append(e.MeshDegenerateUv)
          .Append(" with a COLLAPSED ATLAS UV RECT (threshold ")
          .Append(DegenerateUvArea.ToString("G3"))
          .Append(" atlas units squared — such a quad samples ONE texel, draws a flat SDF value and "
                  + "puts NO ink down at full advance and full geometry, which is the photograph "
                  + "exactly), ").Append(e.MeshUvOutOfRange)
          .Append(" sampling OUTSIDE the atlas, ").Append(e.MeshNonFinite)
          .Append(" non-finite. Worst: ")
          .Append(e.MeshWorstBad > 0 ? e.MeshWorst + " with " + e.MeshWorstBad + " defect(s)"
                                     : "none — every mesh quad was clean")
          .Append(". FONT ATLASES behind this window: ").Append(e.AtlasNote)
          .Append(". REPAIRS: ").Append(e.ReleaseRepairs)
          .Append(" release repair(s) (2 per release: one ").Append(ReleaseSettleFrames)
          .Append(" frame(s) after the last change and one at ").Append(ReleaseSecondRepairFrames)
          .Append("), ").Append(e.RebuildRepairs)
          .Append(" font-atlas-repack repair(s); the last one re-requested ")
          .Append(e.RegeneratedChars).Append(" character(s) across ").Append(e.RegeneratedComponents)
          .Append(" component(s), and the last RELEASE repair re-generated ")
          .Append(e.ReleaseRegenComponents).Append(" component(s) in ")
          .Append(e.ReleaseRegenMs.ToString("F2"))
          .Append(" ms (ModBuild 196 gated this on the defect count and therefore re-generated at "
                  + "most ONE component per release across its whole log; ModBuild 197 makes the "
                  + "release pass unconditional, so a broken image that survives it FALSIFIES the "
                  + "text-source family instead of leaving it untested). RELEASES this report "
                  + "window: ").Append(e.ReleasesThisWindow)
          .Append(", the slowest settle gate opening ").Append(e.ReleaseGateFramesMax)
          .Append(" frame(s) after the last change against a threshold of ").Append(ReleaseSettleFrames)
          .Append("; longest unbroken motion run ").Append(e.LongestMotionRun)
          .Append(" frame(s) of ").Append(e.MotionTicks)
          .Append(" (a run equal to the whole window means the pose is being rewritten every frame "
                  + "by another writer and NO release can ever be detected — the 'a stillness gate "
                  + "never opens for state someone else rewrites each frame' case). COINCIDENCE AT "
                  + "THE CAPTURE INSTANT: ").Append(e.CaptureTicks)
          .Append(" capture(s) examined, of which ").Append(e.CapturesDuringFontRebuild)
          .Append(" ran on the same frame as a font atlas repack and ")
          .Append(e.CapturesBeforeCanvasUpdate)
          .Append(" ran BEFORE uGUI's canvas rebuild for that frame (expect 0 — the capture camera "
                  + "renders inside the camera loop, which Unity runs after "
                  + "PostLateUpdate.PlayerUpdateCanvases). ATLAS REPACKS THIS SESSION: ")
          .Append(_fontRebuilds).Append(_fontRebuilds > 0 ? ", last on '" + _fontRebuildName + "'" : "")
          .Append('.');
    }

    /// <summary>
    /// <b>THE CAPTURE PATH FIELD (ModBuild 197) — WHAT A DRAGGED FRAME ACTUALLY DOES, IN COUNTS.</b>
    ///
    /// <para>Three rounds have argued about whether the moving flicker is a sampling problem on this
    /// path or something else, and every one of them had to answer "is a dragged window supersampled
    /// AT ALL, or does something short-circuit to the raw canvas?" by reading the source. This field
    /// answers it with counts over a drag: captures taken while moving against captures taken while
    /// still, the resolve+mip pass that must follow each one, and the three ways the eye could end up
    /// looking at something other than a freshly resolved mipped target.</para>
    ///
    /// <para>READ IT LIKE THIS. Captures-while-moving should equal the moving frame count in MOTION
    /// BUDGET and resolves should equal captures; if they do, the dragged window took exactly the
    /// same path as the still window that the user has already accepted as fixed, and the moving
    /// complaint CANNOT be "the supersampling switches off while I drag". The three expected-zero
    /// counters each name a different way it could still break: a frame where the quad was visible
    /// and no capture was due, a frame where the quad showed the raw multisampled target instead of
    /// the mipped one, and a resolve whose source and destination had different sizes because the
    /// target was re-allocated between them.</para>
    /// </summary>
    private static void AppendCapturePath(Entry e)
    {
        Sb.Append(" CAPTURE PATH (cumulative; the question is whether a DRAGGED window takes the "
                  + "same path as a still one): ").Append(e.MotionCaptures)
          .Append(" capture(s) taken while MOVING and ").Append(e.StillCaptures)
          .Append(" while STILL, followed by ").Append(e.MotionResolves).Append(" and ")
          .Append(e.StillResolves)
          .Append(" resolve+mip pass(es) respectively (a capture without a matching resolve leaves "
                  + "the quad showing the PREVIOUS frame's image). EXPECTED-ZERO COUNTERS: ")
          .Append(e.CameraOffWhileVisible)
          .Append(" frame(s) on which the quad was visible and no capture was due (cadence ")
          .Append(CaptureIntervalFrames).Append(" frame(s)), ").Append(e.RawQuadFrames)
          .Append(" frame(s) on which the quad showed the RAW multisampled capture target instead of "
                  + "the resolved mipped one, ").Append(e.ResolveSizeMismatch)
          .Append(" resolve(s) whose source and destination differed in size (= the target was "
                  + "re-allocated between the capture and the resolve, which would stretch one "
                  + "frame's image across another's texels).");
    }

    /// <summary>
    /// THE MOTION BUDGET FIELD — what this class costs during a drag, split from what it costs at
    /// rest, against a stated threshold.
    /// </summary>
    private static void AppendMotionBudget(Entry e)
    {
        float motionAvg = e.MotionFrameSamples > 0 ? (float)(e.MotionFrameMs / e.MotionFrameSamples) : 0f;
        float stillAvg = e.StillFrameSamples > 0 ? (float)(e.StillFrameMs / e.StillFrameSamples) : 0f;
        float motionSweep = e.MotionSweeps > 0 ? (float)(e.MotionSweepMs / e.MotionSweeps) : 0f;
        float stillSweep = e.StillSweeps > 0 ? (float)(e.StillSweepMs / e.StillSweeps) : 0f;
        Sb.Append(" MOTION BUDGET (this 10 s window, threshold ").Append(FrameBudgetMs.ToString("F2"))
          .Append(" ms = one 90 Hz frame): while MOVING, ").Append(e.MotionFrameSamples)
          .Append(" frame(s) sampled, mean ").Append(motionAvg.ToString("F2")).Append(" ms, worst ")
          .Append(e.MotionFrameMsMax.ToString("F2")).Append(" ms, ").Append(e.MotionFramesOverBudget)
          .Append(" over the threshold; while STILL, ").Append(e.StillFrameSamples)
          .Append(" frame(s) sampled, mean ").Append(stillAvg.ToString("F2")).Append(" ms, worst ")
          .Append(e.StillFrameMsMax.ToString("F2")).Append(" ms, ").Append(e.StillFramesOverBudget)
          .Append(" over the threshold. THIS CLASS'S OWN SHARE: ").Append(e.MotionSweeps)
          .Append(" layer sweep(s) while moving at ").Append(motionSweep.ToString("F2"))
          .Append(" ms each (cadence ").Append(MovingSweepIntervalFrames).Append(" frame(s)) and ")
          .Append(e.StillSweeps).Append(" while still at ").Append(stillSweep.ToString("F2"))
          .Append(" ms each (cadence ").Append(SweepIntervalFrames).Append(" frame(s)).");
    }

    /// <summary>
    /// The comparable number: RT texels per RENDERED pixel through the LEFT eye's own projection,
    /// computed exactly the way <see cref="PanelSamplingProbe"/> computes its panel scale — the
    /// RectTransform's world corners pushed through
    /// <c>WorldToViewportPoint(..., MonoOrStereoscopicEye.Left)</c> and scaled by
    /// <c>XRSettings.eyeTextureWidth/Height * renderViewportScale</c>, measured as pixel-space EDGE
    /// LENGTHS so a window yawed away from the head reports its foreshortened size. Duplicated rather
    /// than shared because that probe is another lane's file; the two numbers are therefore directly
    /// comparable in the same hardware log, which is the point.
    /// </summary>
    private static string SamplingSentence(Entry e, Camera? head, float shownBias)
    {
        // THE INSTRUMENT'S OWN BAIL-OUT COUNTERS (ModBuild 198). Every early return below used to
        // print a sentence and leave no trace, so a session in which this never completed a single
        // measurement and a session in which it measured 900 clean ones left the same evidence — the
        // "verify the instrument before you trust it" rule. The counts ride the report line.
        if (head == null)
        {
            e.SamplingNoHead++;
            return "SAMPLING NOT MEASURED (no head camera);";
        }
        if (!TryEyeTarget(out float eyeW, out float eyeH))
        {
            e.SamplingNoEyeTarget++;
            return "SAMPLING NOT MEASURED (no readable per-eye render target);";
        }
        if (!TryRenderedSize(e.Panel.HostRect, head, eyeW, eyeH, out float pxW, out float pxH))
        {
            e.SamplingOffScreen++;
            return "SAMPLING NOT MEASURED (the window is behind the eye or sub-pixel this scan);";
        }
        // Measured against the HOST RECT, not the capture frame, so the number stays directly
        // comparable with what PANEL SAMPLING reported for the same window before this path existed
        // — that comparability is the whole point of duplicating the probe's arithmetic here. RT
        // texels per AUTHORED pixel is the factor by construction (RtW = Frame.width * Factor and
        // the frame's pixels are the host's pixels), so the texel figure is the authored figure
        // scaled by the factor whether or not the frame grew past the host rect.
        float authoredPerPixel = Mathf.Max(e.HostRectAtMeasure.width / Mathf.Max(pxW, 0.01f),
            e.HostRectAtMeasure.height / Mathf.Max(pxH, 0.01f));
        e.AuthoredPerRenderedPx = authoredPerPixel;
        // ACHIEVED, not asked (ModBuild 198). A target clipped by MaxRtDimension carries fewer texels
        // per authored pixel than the factor says, and quoting the ASK here would have this line
        // report a band-limited window that is not one. See RecordAchievedFactor.
        float texelsPerPixel = authoredPerPixel * Mathf.Max(e.AchievedFactor, 0.01f);
        // THE MIP LOD THIS MINIFICATION ACTUALLY SELECTS, and how much UNFILTERED level 0 survives
        // it. This is the ModBuild 194 addition and it exists to make one specific hypothesis about
        // the residual moving shimmer decidable from the log instead of arguable: trilinear does NOT
        // remove aliasing at a fractional LOD, it ATTENUATES it. At t texels per pixel the LOD is
        // log2(t); for 0 < LOD < 1 the hardware blends level 0 — which is minified by t and is
        // therefore genuinely undersampled — with level 1, weighting level 0 by (1 - LOD). At the
        // ModBuild 193 log's measured 1.34 texels per pixel that is LOD 0.42 and 58 % of every
        // sample coming from an aliased level. A frozen alias pattern is invisible; the same pattern
        // under a moving window crawls, which is the exact shape of "flackert beim Verschieben".
        // Anisotropic filtering makes this WORSE for a near-frontal panel, because it lowers the
        // selected LOD towards the minor axis' rate — aniso is bought for the map room's windows
        // yawed up to 85 degrees, and it costs band-limiting on the ones facing the seat.
        float lod = Mathf.Max(0f, Mathf.Log(Mathf.Max(texelsPerPixel, 1e-4f), 2f));
        float level0Weight = lod >= 1f ? 0f : 1f - lod;
        float bias = shownBias;

        e.SamplingMeasured++;
        e.TexelsPerRenderedPx = texelsPerPixel;
        if (e.TexelsPerRenderedPxWorst <= 0f || texelsPerPixel < e.TexelsPerRenderedPxWorst)
            e.TexelsPerRenderedPxWorst = texelsPerPixel;
        e.Level0Weight = level0Weight;
        // Magnification (authoredPerPixel < 1) cannot alias — level 0 is then the RIGHT level and its
        // weight of 1 is not a defect. Only a MINIFIED window reading level 0 is the ModBuild 198
        // case, so the counter tests both conditions rather than the weight alone.
        if (level0Weight > 0f && authoredPerPixel > 1f)
            e.Level0Readings++;

        // THE ONE-LINE VERDICT. Three readings must be distinguishable at a glance and never print
        // the same shape: the fix IS in force (texels >= 2, no level 0 at all), the fix is PARTLY in
        // force (level 0 present but the window is magnified, i.e. harmless), and the fix is NOT in
        // force (level 0 present on a minified window — the defect the user reports).
        string verdict = texelsPerPixel >= BandLimitedTexelsPerPixel
            ? "BAND-LIMITED: the eye reads no unfiltered level 0 at all, so there is no sub-texel "
              + "phase term left to sweep while the window is carried — this is what the ModBuild 198 "
              + "factor floor exists to deliver"
            : authoredPerPixel <= 1f
                ? "MAGNIFIED: the window is drawn LARGER than its authored size, so level 0 is the "
                  + "correct level and its weight is not a defect (no minification, nothing to alias)"
                : "NOT BAND-LIMITED — THIS IS THE ModBuild 198 DEFECT LIVE: the eye is reading an "
                  + "UNFILTERED level of a MINIFIED image, whose sub-texel phase is constant while "
                  + "the window is still (reads as sharp) and sweeps every frame while it is carried "
                  + "(reads as the flicker), then LOCKS at whatever phase the release left behind";

        return $"drawn into {pxW:F0}x{pxH:F0} rendered px through the LEFT eye of a "
               + $"{eyeW:F0}x{eyeH:F0} per-eye target ({XRSettings.stereoRenderingMode}, viewportScale "
               + $"{XRSettings.renderViewportScale:F2}) = {texelsPerPixel:F2} RT texels per rendered "
               + $"pixel, against {authoredPerPixel:F2} authored px per rendered px (which is what "
               + "PANEL SAMPLING reported for this window before this build, and is now filtered "
               + $"rather than point-sampled) -> trilinear MIP LOD {lod:F2} at mipMapBias "
               + $"{bias:F2}, so {level0Weight * 100f:F0} % of every texture sample still comes from "
               + $"UNFILTERED level 0 at {texelsPerPixel:F2}x minification. RESAMPLE VERDICT — "
               + $"{texelsPerPixel:F2} RT texels per rendered eye px against a threshold of "
               + $"{BandLimitedTexelsPerPixel:F2} (the rate at which trilinear stops blending level 0 "
               + $"at all), WORST {e.TexelsPerRenderedPxWorst:F2} over {e.SamplingMeasured} completed "
               + "measurement(s) since engage — this instrument samples once per report AND once per "
               + $"release, so a session with many drags has many samples ({e.Level0Readings} of them "
               + $"read unfiltered level 0 on a MINIFIED window; instrument bail-outs: {e.SamplingNoHead} no head camera, "
               + $"{e.SamplingNoEyeTarget} no readable eye target, {e.SamplingOffScreen} off-screen) "
               + $"-> {verdict}. FACTOR: asked {e.Factor:F2} RT texels per authored px "
               + $"(config {e.ConfigFactor:F2}"
               + (e.FactorFloored
                   ? $", raised to the {BandLimitFactor:F2} band-limit floor because the config value "
                     + "is still the shipped default"
                   : ", taken verbatim — this is a value the user set")
               + $"), ACHIEVED {e.AchievedFactor:F2}"
               + (e.AchievedFactor < e.Factor - 0.01f
                   ? $" — LOWER THAN ASKED because the {MaxRtDimension} px per-axis ceiling clipped "
                     + $"a {e.Frame.width:F0}x{e.Frame.height:F0} capture frame, so the fix is only "
                     + "partly in force on this window"
                   : " (nothing clipped it)")
               + ". CAVEAT ON EVERY LEVEL-0 FIGURE ABOVE: it is a LOWER BOUND, because anisotropic "
               + $"filtering (aniso {AnisoLevel}) selects the LOD from the MINOR axis' rate, so a "
               + "window yawed away from the head reads MORE level 0 than this line states;";
    }

    // THE ModBuild 192 OVERFLOW WARN IS GONE, and deliberately so. It compared the host rect with
    // `ConvertedPanel.Target.rect` — the game window's own authored rect, which for a full-screen
    // window is 1920x1080 whether or not anything is drawn out there — latched after one hit, and
    // then told the reader that the content was being cropped and their only option was to switch
    // the feature off. All three parts were wrong: the comparison was against the wrong rectangle,
    // the latch meant the reading could never be re-checked (in the 192 log it fired at engage time
    // against the PRE-fit 328x1080 host rect and the fit then grew the host to 1920x1080 four
    // seconds later, which the warn could no longer retract), and there is no longer anything to
    // switch off — MeasureFrame now grows the capture frame to cover the overspill instead of
    // cropping it. The live, measured, un-latched replacement is the "CAPTURE FRAME ... GROWN /
    // CLAMPED" field of the state line above, and the one remaining loss case (the expansion clamp)
    // warns from MeasureFrame itself, where the numbers actually are.

    private static bool TryEyeTarget(out float pxW, out float pxH)
    {
        float viewport = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        int w = XRSettings.eyeTextureWidth;
        int h = XRSettings.eyeTextureHeight;
        if (w < 2 || h < 2)
        {
            w = Screen.width;
            h = Screen.height;
            viewport = 1f;
        }
        pxW = w * viewport;
        pxH = h * viewport;
        return pxW >= 2f && pxH >= 2f;
    }

    private static bool TryRenderedSize(RectTransform? rect, Camera head, float eyeW, float eyeH,
        out float pixelsW, out float pixelsH)
    {
        pixelsW = 0f;
        pixelsH = 0f;
        if (rect == null)
            return false;
        rect.GetWorldCorners(Corners); // 0 = bottom-left, 1 = top-left, 3 = bottom-right
        Camera.MonoOrStereoscopicEye eye = XRSettings.isDeviceActive
            ? Camera.MonoOrStereoscopicEye.Left
            : Camera.MonoOrStereoscopicEye.Mono;
        Vector3 bl = head.WorldToViewportPoint(Corners[0], eye);
        Vector3 tl = head.WorldToViewportPoint(Corners[1], eye);
        Vector3 br = head.WorldToViewportPoint(Corners[3], eye);
        if (bl.z <= 0f || tl.z <= 0f || br.z <= 0f)
            return false;
        pixelsW = PixelDistance(bl, br, eyeW, eyeH);
        pixelsH = PixelDistance(bl, tl, eyeW, eyeH);
        return pixelsW >= 1f && pixelsH >= 1f;
    }

    private static float PixelDistance(Vector3 a, Vector3 b, float eyeW, float eyeH)
    {
        float dx = (b.x - a.x) * eyeW;
        float dy = (b.y - a.y) * eyeH;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
