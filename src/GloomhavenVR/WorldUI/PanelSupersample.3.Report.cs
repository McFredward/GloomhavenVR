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
        Sb.Append("PANEL SUPERSAMPLE '").Append(e.Window).Append("'").Append(OpenViewTag(e))
          .Append(": host rect ")
          .Append(e.HostRectAtMeasure.width.ToString("F0")).Append('x')
          .Append(e.HostRectAtMeasure.height.ToString("F0"))
          .Append(" uGUI px, CAPTURE FRAME ").Append(e.Frame.width.ToString("F0")).Append('x')
          .Append(e.Frame.height.ToString("F0"))
          .Append(e.ExpandX > 0.5f || e.ExpandY > 0.5f
              ? $" (GROWN by {e.ExpandX:F0}x{e.ExpandY:F0} px to cover content drawn outside the host "
                + $"frame{(e.ExpandClamped ? "; CLAMPED — content IS being cropped" : "")})"
              : " (= the host rect: no content draws outside it)")
          .Append(" -> RT ").Append(e.RtW).Append('x').Append(e.RtH)
          // BOTH numbers, because from ModBuild 201 they can differ: Factor is the band-limited
          // config value and EffectiveFactor is the rate the target was actually sized from, after
          // the content-scale boost and after the dimension and VRAM ceilings cut it back.
          .Append(" (base factor ").Append(e.Factor.ToString("F2")).Append(", target sized at ")
          .Append(e.EffectiveFactor.ToString("F2")).Append("), capture MSAA ")
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

        AppendContentScale(e);
        AppendContentIntegrity(e);
        AppendOverPaint(e);
        AppendSubViewBurst(e);
        AppendCapturePath(e);
        AppendReallocations(e);
        AppendMotionBudget(e);

        Sb.Append(" HOW TO READ THIS LINE — MODBUILD 203 FIRST. (W0) THE ATTRIBUTION TAG "
                  + "'[SUB-VIEW: ...]' IS ON EVERY FIELD AND IT IS WHY THE LAST EIGHT ROUNDS WERE "
                  + "UNREADABLE. Inside 'New Party display' exactly TWO of six sub-views render "
                  + "broken — the CHARACTER SHEET ('Campaign Adventure Party Assembly Variant') and "
                  + "PERKS ('New UIPerksWindow Variant'); ability cards, items, enhancements and "
                  + "battle goals are correct. Every field this class has ever printed averaged over "
                  + "whichever view happened to be open when the 10 s cadence fired, so '1 defect out "
                  + "of 3919 glyph lookups' and '0 defects out of 3933 glyph quads' were correct and "
                  + "unassignable. From this build every field says which view it was measured of, "
                  + "and it says it TWICE from two independent derivations (the largest open sub-view "
                  + "root this class measured, and the game's own NewPartyDisplayUI.ActiveDisplay) — "
                  + "if those two ever disagree, that disagreement is itself the finding. "
                  + "(W1) THE 'OVER-PAINT CENSUS' FIELD IS THE HEADLINE AND IT CLOSES THE BLIND SPOT "
                  + "THE CONTENT-INTEGRITY FIELD NAMES IN ITS OWN SENTENCE: 'NOT the same as being "
                  + "drawn and then painted over, which nothing in this scan can see'. Twenty-two "
                  + "rounds of instruments have come back clean and every one of those readings is "
                  + "still believed — the text source, the submitted mesh, the TMP sub-meshes, the "
                  + "content scale, the sampling and the capture path are all correct, and a glyph "
                  + "that is generated, uploaded and captured perfectly and THEN PAINTED OVER by an "
                  + "opaque rectangle produces exactly those readings and exactly the user's picture. "
                  + "THE ONE FIELD IN THE WHOLE LOG THAT SEPARATES THE BROKEN TWO FROM THE WORKING "
                  + "FOUR IS THE BACKDROP CENSUS: char sheet '2 full-frame plate(s) inside it, the "
                  + "largest Container at 1620x1080 px', perks '2 full-frame plate(s) ... the largest "
                  + "Blur at 1620x1080 px', ability cards and items 'no full-frame plate inside it'. "
                  + "That census counts plates; it never asks what is UNDERNEATH one. READ THE ANSWER "
                  + "FIELD LIKE THIS. (a) A NON-ZERO 'PAINTS OVER N of M' on a plate of a BROKEN view, "
                  + "with that plate marked OPAQUE, IS THE FINDING: content is being drawn and then "
                  + "deleted, which no glyph, mesh, sub-mesh or sampling counter can see, and the "
                  + "remedy is that plate (the game's own UIBlurDisabler defeats these by setting "
                  + "_image.material = null, which proves the plate's appearance IS its material — so "
                  + "read the shader name and the _GrabTexture/_BackgroundTexture/_CameraOpaqueTexture "
                  + "probes on the same line, which nobody has ever read at runtime). (b) 0 GRAPHICS "
                  + "PAINTED OVER ON BOTH BROKEN VIEWS KILLS THE HYPOTHESIS: the plates are then "
                  + "legitimate backdrops drawn FIRST inside their own order band, over-paint is NOT "
                  + "the cause, and the next round must stop looking at layering altogether. (c) A "
                  + "TRANSLUCENT plate over a large count TINTS rather than deletes, which is the "
                  + "'perks comes up as a near-empty DARK plate' report specifically — read the "
                  + "colour RGBA, and note that it is printed RAW and unclamped because "
                  + "UIBlurDisabler's other branch writes Color(17,17,17,85), whose alpha saturates "
                  + "to fully opaque. (d) '0 full-frame plates' on a view the user calls broken rules "
                  + "this family out FOR THAT VIEW, which is a different statement from 'the plates "
                  + "were clean'. EVERY COUNT ON THAT FIELD CARRIES ITS DENOMINATOR and every extreme "
                  + "carries a mean and a sample count, because this project has been burned three "
                  + "times by a summary field read as an operating point. "
                  + "(W2) THE 'SUB-VIEW BURST' FIELD IS THE FIRST REMEDY IN THIS CLASS KEYED ON A "
                  + "CONTENT CHANGE RATHER THAN ON MOTION, and it exists for the user's newest words: "
                  + "'mittlerweile taucht es auch initial kaputt auf wenn man das Fenster öffnet' — "
                  + "no drag at all, which is the one case no previous remedy touched. A tab press is "
                  + "deliberately NOT a geometry change (the fit advances no generation and never "
                  + "touches the host rect), so nothing used to force a capture-layer sweep when the "
                  + "window repopulated; until the next 15-frame cadence tick every transform the "
                  + "game created sat on the GAME's UI layer, missing from the capture and drawn "
                  + "straight into the eye. THE ModBuild 202 LOG MEASURES IT: five late-joiner lines "
                  + "for this window whose counts sum exactly to its reported 2853 late joiners — "
                  + "2448 transforms 0.25 s after ABILITY CARDS opened, 294 transforms 0.25 s after "
                  + "PERKS opened, and three smaller ITEMS arrivals — and all 13 such lines in that "
                  + "session read 'periodic', not one the motion cadence. READ 'THE HOLE' LIKE THIS: "
                  + "0 frames (or a burst that moved 0) means there was no hole to close on that "
                  + "switch and the 'initial kaputt' report is NOT a late-joiner problem — go to the "
                  + "OVER-PAINT CENSUS instead. 1-3 frames means the hole existed and the burst "
                  + "closed it, and the user should notice on the very next tab press. A hole that "
                  + "keeps reaching the " + MaxSweepBurstFrames + "-frame cap means the game is "
                  + "still repopulating after a "
                  + "quarter of a second, the hole is the GAME's cadence and not ours, and the only "
                  + "remaining fix is an arrival HOOK rather than any poll. -1 means no burst has "
                  + "completed and is NOT a hole of 0. TWO REMEDIES ARE ALREADY DEAD AND MUST NOT BE "
                  + "REBUILT FROM THIS FIELD: sweeping every frame WHILE MOVING (ModBuild 193 shipped "
                  + "it, 3501 sweeps and 2448 late joiners prove it ran, the user reported the "
                  + "flicker identical) and sweeping every frame outright (ModBuild 196 priced it at "
                  + "1.48-1.94 ms of a 17 ms frame and measured 22 of 23 arrivals caught by the "
                  + "ORDINARY cadence). This burst is armed by a content change and disarms itself. "
                  + "(W3) THE SIBLING-CANVAS DRAW-ORDER HYPOTHESIS IS DEAD and the 'UNSTABLE TIE' "
                  + "count exists only to keep it dead: 19 of this window's 20 adopted nested "
                  + "canvases carry overrideSorting = false and are therefore not sorting roots at "
                  + "all, and the single exception cannot tie with itself. Expect 0; a non-zero "
                  + "reading means the adoption's sorting state changed under us. "
                  + "(W4) 'FOREIGN RENDER SUBTREES, NAMED' finally answers a count that has been "
                  + "printed since ModBuild 193 without ever being resolved: the party window reports "
                  + "4 and only the first ('FX_Smoke') has ever appeared in a log. These are excluded "
                  + "from the capture ON PURPOSE and drawn by the HEAD camera at their true world "
                  + "pose — so the one thing that can go wrong is DEPTH: the display quad sits at "
                  + "host-local z = 0, and a foreign renderer at z ~ 0 is coplanar with it, which two "
                  + "MultiPass eyes can resolve differently. A COPLANAR count above 0 is a live "
                  + "hypothesis for one-eyed flicker that nothing else on this line can see; a count "
                  + "of 0, with every foreign renderer's z well away from 0, retires it. "
                  + "NOW THE OLDER CLAUSES. MODBUILD 197 NEXT, AND IT RETIRES THE ModBuild 196 "
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

        // ModBuild 205: THE THIRD OF THE INK CENSUS'S THREE MOMENTS. The other two are the release
        // edge and thirty frames after it (armed in ReportRelease); this one is the ordinary cadence,
        // and it exists so a window that has NOT been touched for ten minutes still says what is in
        // its captured image. The window in the ModBuild 196 photograph had been standing still when
        // it was photographed, so "it only moved ten minutes ago" must not mean "measured ten minutes
        // ago". The census is ISSUED from the capture camera's onPostRender later in this same frame
        // and its line arrives a frame or two after that, with both frame numbers on it.
        ArmInkCensus(e, "the ordinary 10 s report cadence (a SETTLED window, no drag involved)");
    }

    /// <summary>
    /// <b>ONE LINE PER COMPLETED INK CENSUS — the measurement thirteen builds of clean state readings
    /// have been pointing at, and the first one in this class that looks at the CAPTURED IMAGE.</b>
    ///
    /// <para>The whole argument, the caps and every constant's derivation live with the machinery at
    /// the foot of <c>PanelSupersample.2.Capture.cs</c>; this method only has to make the answer
    /// unmissable and un-mis-readable. The shape that ends the investigation in one hardware session
    /// is a single sentence of the form <i>"'Gebundene Gegenstände:' — 21 glyph(s) in the mesh, 14
    /// with ink, 7 EMPTY: 'u','n','e','s','t','ä','d'"</i>.</para>
    /// </summary>
    private static void ReportInkCensus(Entry e, InkCensus c)
    {
        // The census is over, whatever it found. ModBuild 208 charges the mod-wide budget once per
        // census, so it is handed back HERE rather than in a callback.
        EndInkCensus(c);

        // Worst first — the components with the most EMPTY glyphs are the ones the next round works
        // on. The COUNTS are always complete; only the sentences are capped, and the line says by how
        // much (this project has shipped five remedies that quietly covered part of their subject).
        //
        // THIS SORT INVALIDATES InkGlyph.Comp, which indexes into this list. That is safe and only
        // because of two facts, both worth stating rather than rediscovering: the glyph list is fully
        // consumed by EvaluateInkPlane and CompareInkPlanes before this method is reached (ModBuild
        // 208 moved the cross-plane comparison in front of this sort for exactly that reason), and
        // BOTH lists are cleared and
        // rebuilt by BuildInkCensus at the start of every census. Nothing reads Comps by index after
        // this point.
        c.Comps.Sort((a, b) => b.Empty != a.Empty ? b.Empty.CompareTo(a.Empty)
                                                  : b.InStrip.CompareTo(a.InStrip));

        Sb.Length = 0;
        Sb.Append("PANEL SUPERSAMPLE INK CENSUS '").Append(e.Window).Append('\'').Append(OpenViewTag(e))
          .Append(": ").Append(c.Reason).Append(" — requested on frame ").Append(c.RequestFrame)
          .Append(", delivered on frame ").Append(c.DeliveredFrame).Append(" (")
          .Append(c.DeliveredFrame - c.RequestFrame)
          .Append(" frame(s) later; AsyncGPUReadback, so the GPU was never stalled). THE VERDICT: ")
          .Append(c.TotalGlyphs).Append(" glyph quad(s) the SUBMITTED MESH says are there, ")
          .Append(c.TotalInk).Append(" WITH INK in the captured texture, ").Append(c.TotalEmpty)
          .Append(" EMPTY, across ").Append(c.Comps.Count).Append(" text component(s)")
          .Append(c.TotalBelowFloor > 0 || c.TotalDeferred > 0
              ? $" — of {c.TotalGlyphs} the MIP 0 plane JUDGED {c.TotalJudged}, left "
                + $"{c.TotalBelowFloor} under the {InkMinQuadTexels:F0}-texel size floor and DEFERRED "
                + $"{c.TotalDeferred} to the next census, so INK + EMPTY sums to the judged count and "
                + "not to the mesh count"
              : string.Empty)
          .Append('.');

        AppendInkPlanes(e, c);

        int named = 0;
        for (int i = 0; i < c.Comps.Count && named < MaxInkComponentsReported; i++)
        {
            InkComponent comp = c.Comps[i];
            named++;
            Sb.Append(" '").Append(InkLabel(comp.Text)).Append("' (").Append(comp.Name).Append(") — ")
              .Append(comp.InStrip).Append(" glyph(s) in the mesh")
              .Append(comp.MeshGlyphs != comp.InStrip
                  ? $" inside the census strip of {comp.MeshGlyphs} it carries"
                  : string.Empty)
              .Append(comp.Judged != comp.InStrip
                  ? $" of which {comp.Judged} judged at mip 0 ({comp.BelowFloor} under the size floor)"
                  : string.Empty)
              .Append(", ").Append(comp.Ink).Append(" with ink, ").Append(comp.Empty).Append(" EMPTY")
              .Append(comp.Empty > 0 ? ": " + comp.EmptyChars : string.Empty)
              .Append(comp.EmptyNotNamed > 0 ? $" (+{comp.EmptyNotNamed} more not named)" : string.Empty)
              // ModBuild 206: WHAT BECAME OF THOSE EMPTIES. Without this the line said "something is
              // missing"; with it the line says what is wrong, which is the difference between another
              // wrong diagnosis and a fix.
              .Append(comp.Empty > 0
                  ? $" — of which {comp.Absent} genuinely ABSENT (no ink anywhere in the search "
                    + $"window), {comp.FoundOffset} FOUND OFFSET at ({comp.FitDx:F1},{comp.FitDy:F1}) "
                    + $"texels, {comp.Ambiguous} AMBIGUOUS"
                    + (comp.EmptyNearStripEdge > 0
                        ? $"; {comp.EmptyNearStripEdge} of them sit within one glyph advance of a "
                          + "strip edge and are not clean readings whichever bucket they fell in"
                        : string.Empty)
                    + (comp.FitNote.Length > 0 ? ". FIT: " + comp.FitNote : string.Empty)
                  : string.Empty)
              .Append('.');
        }
        if (c.Comps.Count > named)
            Sb.Append(" (").Append(c.Comps.Count - named).Append(" further component(s) censused and "
                      + "not named here; the counts above them are complete.)");

        // ---- HOW THE MEASUREMENT WAS MADE, so a wrong choice is visible rather than silent --------
        Sb.Append(" HOW INK WAS DECIDED: a glyph counts as INKED when any interior texel of its quad "
                  + "deviates from that quad's OWN LOCAL BACKGROUND by at least ")
          .Append((InkThreshold * 255f).ToString("F0")).Append("/255 (")
          .Append((InkThreshold * 100f).ToString("F1"))
          .Append(" %) in PREMULTIPLIED LUMINANCE (lum x alpha). Alpha alone was rejected: this "
                  + "window's text sits on OPAQUE dark plates where alpha reads 1.0 on the glyph and "
                  + "on the plate beside it, so an alpha test would call every glyph present in a "
                  + "picture full of holes; premultiplied luminance degenerates to alpha over the "
                  + "transparent clear and to plain luminance over a plate, and the test is on the "
                  + "ABSOLUTE deviation so light-on-dark and dark-on-light are both caught. The local "
                  + "background is the MEDIAN of a ring ")
          .Append(InkRingBandTexels).Append(" texel(s) outside the quad (a median, so an adjacent "
                  + "glyph intruding into the ring cannot drag the estimate onto ink); the quad's "
                  + "interior is inset by ")
          .Append((InkQuadInset * 100f).ToString("F0")).Append(" % per side to clear the SDF padding, "
                  + "and is sampled on an ").Append(InkInnerSamples).Append('x').Append(InkInnerSamples)
          .Append(" grid. READ THE THRESHOLD AGAINST THE DATA, NOT ON TRUST: the MEDIAN deviation of "
                  + "the glyphs called INKED is ")
          .Append(c.MedDevInk >= 0f ? (c.MedDevInk * 255f).ToString("F1") : "n/a")
          .Append("/255 and of the glyphs called EMPTY is ")
          .Append(c.MedDevEmpty >= 0f ? (c.MedDevEmpty * 255f).ToString("F1") : "n/a")
          .Append("/255, against the ").Append((InkThreshold * 255f).ToString("F0"))
          .Append("/255 bar and a strip background of ")
          .Append((c.Background * 255f).ToString("F1"))
          .Append("/255. If those two medians are not an order of magnitude apart, the threshold is "
                  + "the thing to question and NOTHING below may be concluded — ModBuild 196 measured "
                  + "the photograph's own pixels at 4/255 for a gap and 127/255 for a glyph, so a "
                  + "healthy reading has the EMPTY median in the single digits and the INKED median in "
                  + "three.");

        // ---- THE LEVEL COMPARISON (ModBuild 208) — THE FIELD THIS BUILD EXISTS FOR ---------------
        Sb.Append(" THE LEVELS THE EYE ACTUALLY READS. ModBuild 205-207 censused mip level 0 and "
                  + "nothing else, while the same log line reported this window at trilinear MIP LOD "
                  + "1.66 — the hardware samples levels 1 and 2 blended and level 0 essentially not at "
                  + "all. So thirteen builds proved level 0 correct and said NOTHING about the levels "
                  + "the player sees. THE FINDING IS A COMPARISON AND NEVER AN ABSOLUTE, because an "
                  + "absolute EMPTY count at mip 2 would be dominated by glyphs that legitimately "
                  + "averaged away: GLYPHS INKED AT MIP 0 AND EMPTY AT A LEVEL THE EYE READS — ")
          .Append(c.MipLost1).Append(" of ").Append(c.MipCompared1).Append(" comparable at MIP 1, ")
          .Append(c.MipLost2).Append(" of ").Append(c.MipCompared2)
          .Append(" comparable at MIP 2. THE CHARACTERS, WITH THEIR SIZE AT THE LEVEL THAT LOST THEM:")
          .Append(c.MipLostNote)
          .Append(" THE CONTROL, in the other direction, and it must be read with them: ")
          .Append(c.MipGained1 + c.MipGained2)
          .Append(" glyph(s) were EMPTY at mip 0 and INKED at a lower level (")
          .Append(c.MipGained1).Append(" at mip 1, ").Append(c.MipGained2)
          .Append(" at mip 2). That is NOT a defect — box-filtering thin ink concentrates it into "
                  + "fewer texels as often as it dilutes it — but a count comparable to the losses "
                  + "means the ink bar is sitting inside the noise and NEITHER number may be trusted. "
                  + "THE SIZE FLOOR, which is the trap in this whole measurement: a glyph correctly "
                  + "MINIFIED is not a defect (a two-texel stroke at level 0 is half a texel at level "
                  + "2), so a quad under ")
          .Append(InkMinQuadTexels.ToString("F0"))
          .Append(" texels on its smaller axis at a level is counted BELOW THE FLOOR at that level "
                  + "and is neither INKED nor EMPTY there. The bar is about this INSTRUMENT and not "
                  + "about the eye: under three texels the inset interior is one texel wide, the "
                  + "sample grid re-reads it 144 times and the background ring overlaps the quad's own "
                  + "ink, so the reading would be decided by the instrument's geometry. Each plane "
                  + "above prints how many it excluded and the smallest quad it still judged.");

        Sb.Append(" THE RESOLVE BLIT, ISOLATED (ModBuild 208): the pipeline is capture camera -> "
                  + "e.Rt, Graphics.Blit(e.Rt, e.MipRt), GenerateMips(). Reading e.Rt beside e.MipRt "
                  + "mip 0 puts the blit between two measured planes for the first time — ")
          .Append(c.BlitCompared).Append(" glyph(s) judged on both sides, ")
          .Append(c.BlitOnlyCapture).Append(" INKED before the blit and EMPTY after it, ")
          .Append(c.BlitOnlyMip).Append(" the other way round. ").Append(c.BlitVerdict);

        // ---- THE MAPPING SELF-CHECK (ModBuild 206) ----------------------------------------------
        // WHY IT IS HERE: the ModBuild 205 census answered, and the shape of the answer forced this.
        // The SAME component — 'Quest freischalten', 17 glyphs, ONE mesh, ONE draw call — reported
        // 0/17 EMPTY, then 3/14, then 3/14 with a DIFFERENT subset, then 7/10, 9/8, 12/5, 16/1, and 33
        // readings of 139-of-139 clean. You cannot rasterise half a mesh, so a scattered varying
        // subset out of one submitted mesh is not the rasteriser dropping quads — it is a POSITIONAL
        // disagreement between where the census predicts a quad and where the ink is. These two fields
        // measure that displacement instead of assuming it is zero.
        float rateX = Mathf.Max(c.RateX, 1e-4f), rateY = Mathf.Max(c.RateY, 1e-4f);
        float mdx = c.CentroidCount > 0 ? (float)(c.CentroidDxSum / c.CentroidCount) : 0f;
        float mdy = c.CentroidCount > 0 ? (float)(c.CentroidDySum / c.CentroidCount) : 0f;
        Sb.Append(" MAPPING SELF-CHECK, PART 1 — WHERE THE INK ACTUALLY SITS INSIDE THE QUADS THAT DO "
                  + "HAVE INK. Displacement of the ink centroid from the predicted quad centre, over ")
          .Append(c.CentroidCount).Append(" INKED glyph(s): X lowest ")
          .Append(c.CentroidDxLow.ToString("F2")).Append(", MEAN ").Append(mdx.ToString("F2"))
          .Append(", highest ").Append(c.CentroidDxHigh.ToString("F2")).Append(" texels; Y lowest ")
          .Append(c.CentroidDyLow.ToString("F2")).Append(", MEAN ").Append(mdy.ToString("F2"))
          .Append(", highest ").Append(c.CentroidDyHigh.ToString("F2")).Append(" texels — i.e. a mean "
                  + "of ")
          .Append((mdx / rateX).ToString("F2")).Append(',').Append((mdy / rateY).ToString("F2"))
          .Append(" AUTHORED px, against a ").Append(InkMappingVerifiedTexels.ToString("F1"))
          .Append("-texel bar. READ THE MEAN, NOT THE EXTREMES: a single glyph's ink is not centred in "
                  + "its own quad ('j' sits low and left, 'T' is top-heavy), so the per-glyph scatter "
                  + "carries the glyph SHAPES as well as any displacement and only the mean is a "
                  + "statement about the mapping. A systematic non-zero mean is a displaced capture or "
                  + "a wrong mapping; a mean near zero with this much scatter is the mapping being "
                  + "right, and then an EMPTY verdict really does mean absent.");

        Sb.Append(" MAPPING SELF-CHECK, PART 2 — WHERE THE MISSING GLYPHS ARE, IF THEY ARE ANYWHERE. "
                  + "Every EMPTY glyph is re-tested over a bounded search window of +/- one glyph "
                  + "advance in X and +/- one line height in Y around its predicted position, and the "
                  + "offset is fitted PER COMPONENT rather than per glyph — a whole-string "
                  + "displacement is ONE number for the string, and fitting per glyph is degenerate "
                  + "because in running text a glyph shifted by one advance lands on its NEIGHBOUR, "
                  + "which is also ink. OF ").Append(c.TotalEmpty).Append(" EMPTY glyph(s): ")
          .Append(c.EmptyAbsent)
          .Append(" GENUINELY ABSENT (nothing within the search window either), ")
          .Append(c.EmptyFoundOffset).Append(" FOUND OFFSET, ").Append(c.EmptyAmbiguous)
          .Append(" AMBIGUOUS (of which ").Append(c.EmptyNotSearched)
          .Append(" were never searched at all — the ").Append(MaxInkSearchGlyphs)
          .Append("-glyph search cap bit, the ").Append(c.SearchBudgetMs.ToString("F2"))
          .Append(" ms the search was granted of the per-frame pool was spent, or the ModBuild 208 "
                  + "gate skipped the search because "
                  + "the previous census had already located the loss in the mip chain; the SEARCH "
                  + "field under COST says which. A cap, a budget or a gate must never be able to "
                  + "manufacture the more alarming verdict, so those are NOT counted as absent), and ")
          .Append(c.EmptyNearStripEdge)
          .Append(" sit within one glyph advance of a strip edge (an OVERLAY count, not a fourth "
                  + "bucket: a partially covered quad is not a clean reading whichever bucket it "
                  + "landed in). THE FITTED OFFSETS, over ").Append(c.FitCount)
          .Append(" component(s) that produced a usable sub-advance fit: X ")
          .Append(c.FitDxLow.ToString("F1")).Append("..").Append(c.FitDxHigh.ToString("F1"))
          .Append(", Y ").Append(c.FitDyLow.ToString("F1")).Append("..")
          .Append(c.FitDyHigh.ToString("F1")).Append(" texels; ").Append(c.FitLatticeAlias)
          .Append(" component(s) fitted a LATTICE ALIAS (an offset a whole advance or line away, "
                  + "which an is-there-ink test cannot tell from the truth in running text, so it is "
                  + "reported as ambiguous and never as a displacement")
          .Append(c.AliasCount > 0
              ? $", MEAN ALIAS OFFSET ({c.AliasDxSum / c.AliasCount:F1},"
                + $"{c.AliasDySum / c.AliasCount:F1}) texels — printed anyway, because five components "
                + "fitting the SAME alias and five fitting five different ones are completely "
                + "different readings and ModBuild 206 discarded the number that tells them apart"
              : string.Empty)
          .Append(") and ").Append(c.FitNoFit)
          .Append(" found NOTHING better than the predicted position. THE SEARCH ITSELF: the window is "
                  + "+/- ").Append(InkSearchSpanAdvances.ToString("F0"))
          .Append(" glyph advance(s) in X and +/- ").Append(InkSearchSpanLines.ToString("F0"))
          .Append(" line height(s) in Y (ModBuild 206 searched ONE of each, and almost every EMPTY "
                  + "glyph came back 'the best offset sits ON the search-window boundary'), swept "
                  + "coarse-to-fine as a ").Append(InkCoarseSteps).Append('x').Append(InkCoarseSteps)
          .Append(" grid at half-advance steps and then a ").Append(InkFineSteps).Append('x')
          .Append(InkFineSteps).Append(" refinement inside the winning cell — a window this wide and a "
                  + "sub-advance resolution cannot come out of one grid, and the per-component window "
                  + "in TEXELS is printed with each fit below. A FLAT score field (best offset finding "
                  + "ink in under ").Append((InkFlatFieldFraction * 100f).ToString("F0"))
          .Append(" % of the component's glyphs anywhere in that window) is now classified GENUINELY "
                  + "ABSENT rather than ambiguous: a flat field means 'nothing here', not 'maybe "
                  + "outside', and reading it the other way is what made ABSENT nearly unreachable in "
                  + "ModBuild 206. A boundary argmax is only called ambiguous when it beats the ZERO "
                  + "offset by at least ").Append((InkMaterialGainFraction * 100f).ToString("F0"))
          .Append(" % of the glyph count, i.e. when there really is a peak being cut off. EVERY "
                  + "searched component prints BOTH its best score and its score at ZERO offset, so a "
                  + "field with no peak is visible as such. PER COMPONENT:")
          .Append(c.FitNote);

        // ---- THE ALPHA EVIDENCE (ModBuild 207) --------------------------------------------------
        // The check that decides whether anything above is real, printed as evidence rather than
        // asserted as a conclusion. See BuildInkAlphaEvidence for the whole argument.
        Sb.Append(" ALPHA EVIDENCE — IS EVERY 'EMPTY' COMPONENT ACTUALLY SUPPOSED TO BE DRAWN? A "
                  + "component hidden by a CanvasGroup alpha 0 above it draws nothing CORRECTLY, and "
                  + "counting its glyphs as EMPTY would make this whole finding the instrument's own "
                  + "artefact. That population is not hypothetical: the ModBuild 204 split measured "
                  + "155-216 of 216 text components on THIS window at inherited alpha 0, first named "
                  + "'Gold Warning', and the components ModBuild 206 named as EMPTY — 'Party Name', "
                  + "'XP Amount Levelup', 'XP Amount', 'Level text' — belong to panels that may "
                  + "legitimately be hidden. THE EXCLUSION IS NOW MEASURED TWO WAYS AND THEY ARE "
                  + "COMPARED: ").Append(c.ComponentsHiddenByGroup)
          .Append(" component(s) were excluded by walking the CanvasGroup chain to the host directly, "
                  + "of which ").Append(c.ComponentsHiddenByGroupOnly)
          .Append(" would NOT have been caught by the CanvasRenderer alpha test that ModBuild 205 and "
                  + "206 used as their only exclusion. THAT SECOND NUMBER IS THE ONE THAT MATTERS: "
                  + "non-zero means those builds censused hidden components and their EMPTY counts are "
                  + "inflated by exactly that much. The chain walk is deliberately INDEPENDENT of "
                  + "CanvasRenderer.GetInheritedAlpha() — that value is maintained by the canvas during "
                  + "its own render pass, and a census built on it inherits whatever it is blind to. "
                  + "THE COMPONENTS THAT PRODUCED EMPTY GLYPHS, each with its own colour alpha, its "
                  + "CanvasRenderer alpha, its inherited alpha and its CanvasGroup chain product "
                  + "(lowest effective alpha among them ")
          .Append(c.EmptyLowestAlpha.ToString("F3"))
          .Append("; anything below 1.000 means an EMPTY verdict is partly a visibility artefact and "
                  + "the verdict says so):").Append(c.AlphaNote);

        Sb.Append(" ORIENTATION SELF-CHECK: ").Append(c.Orientation)
          .Append(". This is MEASURED and not assumed because AsyncGPUReadback returns the source "
                  + "texture's own layout and Unity does not flip it; a census that guessed would read "
                  + "the mirrored row of every glyph and answer confidently and wrongly, which is a "
                  + "mistake this project has already paid two builds for. The census strip is FULL "
                  + "HEIGHT precisely so the row order is the ONLY ambiguity left (y=0, height=RtH "
                  + "selects the same texels under either convention), and the verdict comes from "
                  + "correlating the ink bands the mesh predicts against the ink bands measured, over ")
          .Append(InkBands).Append(" bands, needing at least ")
          .Append(InkOrientMinCorrelation.ToString("F2")).Append(" correlation and a ")
          .Append(InkOrientMinMargin.ToString("F2")).Append(" margin.");

        Sb.Append(" THE MAPPING, from the same two values SyncProjection uses: the capture frame is ")
          .Append(c.FrameW.ToString("F0")).Append('x').Append(c.FrameH.ToString("F0"))
          .Append(" host-local uGUI px and the target is ").Append(c.RtW).Append('x').Append(c.RtH)
          .Append(" texels, so the authored-to-texel rate is ").Append(c.RateX.ToString("F3"))
          .Append(" x ").Append(c.RateY.ToString("F3"))
          .Append(". THE FRAME'S ORIGIN, which is what a VARYING displacement would have to come from: "
                  + "xMin ").Append(c.FrameAtRequest.xMin.ToString("F3")).Append(", yMin ")
          .Append(c.FrameAtRequest.yMin.ToString("F3")).Append(" host-local uGUI px, i.e. a sub-texel "
                  + "PHASE of ")
          .Append((c.FrameAtRequest.xMin * c.RateX - Mathf.Floor(c.FrameAtRequest.xMin * c.RateX))
                  .ToString("F3"))
          .Append(',')
          .Append((c.FrameAtRequest.yMin * c.RateY - Mathf.Floor(c.FrameAtRequest.yMin * c.RateY))
                  .ToString("F3"))
          .Append(" texels. The mapping subtracts this origin and so does SyncProjection, so the two "
                  + "cannot disagree ABOUT IT — but it is printed because a frame origin that moves "
                  + "between readings is the only quantity in this path that could make the SAME "
                  + "string register at a different offset on different captures, which is exactly the "
                  + "pattern that forced the mapping self-check. Compare it across the three moments: "
                  + "if the origin is identical and the fitted offset is not, the displacement is not "
                  + "coming from the frame")
          .Append(" — character for character the pair RecordAchievedFactor reports as the ACHIEVED "
                  + "factor. The configured factor is deliberately NOT used: the band-limit floor, the "
                  + "content-scale boost, RateQuantum, the VRAM step-down and the ")
          .Append(MaxRtDimension).Append(" px axis ceiling all sit between it and the target that was "
                  + "actually allocated, and deriving the mapping from the ALLOCATED target and the "
                  + "COMMITTED frame is what makes it impossible for this census and the capture "
                  + "camera to disagree. Source texture: ").Append(c.SourceNote).Append('.');

        Sb.Append(" WHAT WAS LEFT OUT, never silently: the census strip is ").Append(c.StripW)
          .Append('x').Append(c.StripH).Append(" texels at x=").Append(c.StripX).Append(" of ")
          .Append(c.RtW).Append(" (the ").Append(MaxInkCensusTexels)
          .Append("-texel readback budget divided by the target's height, i.e. ")
          .Append((c.StripW * c.StripH * 4f / (1024f * 1024f)).ToString("F1"))
          .Append(" MB per request); ").Append(c.GlyphsOutsideFrame)
          .Append(" glyph quad(s) fell outside the COMMITTED CAPTURE FRAME itself and are therefore "
                  + "CROPPED — legitimately absent from the picture and NEVER a defect. That is the "
                  + "authoritative crop test and it needs no cooperation from any other lane: the "
                  + "camera's viewport IS the frame, so whatever shrinks the frame (the expansion "
                  + "limit, the hysteresis, a band-limit clamp) shows up here, live. ")
          .Append(c.GlyphsOutsideStrip)
          .Append(" further quad(s) were inside the frame but outside the census strip, or were under "
                  + "two texels on an axis, ")
          .Append(c.GlyphsSubMeshTransform)
          .Append(" were excluded because a TMP_SubMeshUI child's local transform is not identity, so "
                  + "the parent's matrix could not speak for them (expect 0; excluded rather than "
                  + "mismapped, because a mismapped glyph reports EMPTY and that is the one answer "
                  + "this instrument must not be able to invent), ")
          .Append(c.GlyphsClipped)
          .Append(" were CLIPPED AWAY by a mask or a scroll viewport (uGUI clips in the SHADER, so a "
                  + "scrolled-out label keeps a perfect quad in the submitted mesh and draws nothing "
                  + "LEGITIMATELY — counting those as EMPTY would have manufactured this instrument's "
                  + "own verdict, and this window is full of scroll viewports), ")
          .Append(c.GlyphsSkippedCap).Append(" hit the per-component (").Append(MaxInkGlyphsPerComponent)
          .Append(") or per-census (").Append(MaxInkCensusGlyphs).Append(") glyph cap, ")
          .Append(c.ComponentsNotDrawn)
          .Append(" text component(s) were excluded because the renderer had switched them off (cull "
                  + "flag, own/inherited/authored alpha 0), a CANVASGROUP CHAIN above them multiplied "
                  + "out to zero, or a mask hid them entirely — see the ALPHA EVIDENCE field, which "
                  + "is what decides whether the EMPTY counts on this line are real at all, ")
          .Append(c.ComponentsSkippedCap).Append(" text component(s) of ").Append(c.ComponentsFound)
          .Append(" hit the ").Append(MaxInkCandidateComponents).Append("-component cap")
          .Append(c.WalkTruncated
              ? $", and the subtree walk hit its {MaxInkWalkTransforms}-transform ceiling so the "
                + "component list itself is a LOWER BOUND"
              : string.Empty)
          .Append(". Every count above is therefore a count over WHAT WAS CENSUSED, and the excluded "
                  + "figures are printed so it can never be read as a count over the whole window.");

        // ---- THE COST, AND IT IS BOUNDED NOW (ModBuild 208) --------------------------------------
        // The 207 log read "46.13 ms to judge 206 glyph(s)" and "22.98 ms to judge 139 glyph(s)" on
        // the delivery frame against an 11.11 ms budget — an instrument that exists to measure a
        // rendering complaint was causing 2-4x frame overruns of its own. Every stage now has a
        // millisecond budget, a cursor and a printed deferral count.
        Sb.Append(" COST, BOUNDED: ").Append(c.BuildMs.ToString("F2"))
          .Append(" ms to build the glyph list on the request frame; ")
          .Append(c.JudgeMsTotal.ToString("F2")).Append(" ms of judging summed over ")
          .Append(c.PlanesLanded).Append(" plane(s), WORST SINGLE PLANE ")
          .Append(c.JudgeMsWorst.ToString("F2")).Append(" ms; ").Append(c.SearchMs.ToString("F2"))
          .Append(" ms of neighbourhood search against the ").Append(c.SearchBudgetMs.ToString("F2"))
          .Append(" ms it was granted. THE WHOLE CENSUS THEREFORE COST ")
          .Append((c.JudgeMsTotal + c.SearchMs).ToString("F2"))
          .Append(" ms of delivery-side work against a mod-wide PER-FRAME POOL of ")
          .Append(InkFrameBudgetMs.ToString("F1")).Append(" ms and an ")
          .Append(FrameBudgetMs.ToString("F2"))
          .Append(" ms frame. THE POOL IS WHAT BINDS, AND IT HAS TO BE: all four planes are requested "
                  + "on ONE frame (the mip chain is regenerated from a fresh capture every frame, so "
                  + "planes read on different frames would compare different pictures) and "
                  + "AsyncGPUReadback drains its completed queue per frame, so all four callbacks CAN "
                  + "land together. Each stage takes what is left of the pool divided by the stages "
                  + "still to come, capped at ").Append(InkJudgeBudgetMs.ToString("F1"))
          .Append(" ms and floored at ").Append(InkMinStageBudgetMs.ToString("F1"))
          .Append(" ms so a late stage still measures SOMETHING — a plane that measured nothing "
                  + "contributes nothing to the level comparison and would shrink its denominator to "
                  + "zero. Each plane above prints the share it was granted. ModBuild 207 spent 46.13 "
                  + "and 22.98 ms on this same work, and ~18 ms of that was the ORIENTATION PROFILE "
                  + "re-deriving a row order that is constant for the session; it is now decided once "
                  + "per window and merely confirmed afterwards. WHAT WAS DEFERRED, never silently: ")
          .Append(c.TotalDeferred)
          .Append(" glyph(s) were left unjudged by the millisecond budget and are carried to the next "
                  + "census, which resumes at glyph ").Append(c.GlyphCursor).Append(" of ")
          .Append(c.TotalGlyphs).Append(" (this census started at ").Append(c.CensusCursor)
          .Append("; the cursor advances by the LEAST any plane covered, so no band of glyphs can "
                  + "fall between two censuses uncompared). THE SEARCH: ").Append(c.SearchNote)
          .Append(". THE GATE ON IT — the search exists only to decide whether a glyph EMPTY at its "
                  + "predicted place is absent or displaced, so it is skipped when nothing is empty "
                  + "and skipped when the PREVIOUS census located the loss in the mip chain (this "
                  + "census's own mip 1/2 buffers and its mip 0 buffer are never alive at the same "
                  + "instant, so the gate cannot read its own comparison; the previous reading was ")
          .Append(c.LastMipLost < 0 ? "none yet — the first census after engage always searches"
                                    : $"{c.LastMipLost} lost")
          .Append("). It also scores at most ").Append(InkSearchMaxGlyphs)
          .Append(" glyph(s) per component instead of all ").Append(MaxInkGlyphsPerComponent)
          .Append(", which is what takes one component's coarse sweep from 444,000 texel reads to "
                  + "111,000 — a whole-string fit AGGREGATES, so it locates its peak from a "
                  + "stratified sample, and the per-glyph FOUND/ABSENT verdict at the fitted offset "
                  + "still re-tests every empty glyph on the full grid. THE READBACK: ")
          .Append(c.PlanesRequested).Append(" plane(s) requested totalling ")
          .Append(c.TexelsRequested).Append(" texel(s) = ")
          .Append((c.TexelsRequested * 4f / (1024f * 1024f)).ToString("F1"))
          .Append(" MB of staging, ").Append(c.PlanesLanded).Append(" landed, ")
          .Append(c.PlanesFailed).Append(" failed. Three censuses per release plus one per 10 s "
                  + "report. SINCE ENGAGE: ")
          .Append(c.Armed).Append(" armed, ").Append(c.Issued).Append(" issued, ").Append(c.Completed)
          .Append(" completed, ").Append(c.Errors).Append(" readback error(s), ")
          .Append(c.DroppedStale)
          .Append(" dropped because the target was re-allocated between request and delivery, ")
          .Append(c.DroppedFrameMoved)
          .Append(" dropped because the COMMITTED CAPTURE FRAME moved between request and delivery "
                  + "WITHOUT a re-allocation (the mapping subtracts the frame's origin, so such a "
                  + "reading is stale by construction — and a frame that moves between two frames is "
                  + "itself the same re-mapping that would displace the picture the eye sees), ")
          .Append(c.DroppedInFlight)
          .Append(" deferred because a readback was still in flight (the mod-wide budget is ")
          .Append(MaxInkCensusesInFlight)
          .Append(" CENSUS at a time from ModBuild 208 — the unit changed from requests to censuses "
                  + "because one census is now four planes and 17.6 MB of staging — because the 10 s "
                  + "report cadence is ONE shared timer and every engaged panel arms on the same "
                  + "frame; a deferred census stays armed and goes out on a later frame, it is never "
                  + "cancelled), ").Append(c.Unanswerable)
          .Append(" reported NOT ANSWERABLE, ").Append(c.Threw).Append(" threw.");

        Sb.Append(" HOW TO READ IT — AND THIS LINE IS DECISIVE IN BOTH DIRECTIONS. (1) GLYPHS EMPTY IN "
                  + "THE CAPTURE means the loss happens AT OR BEFORE rasterisation into our render "
                  + "target: the capture path or the game's own submission is guilty, every state "
                  + "instrument in this class is measuring the wrong stage, and the next round works "
                  + "between the mesh upload and the capture camera's rasteriser — the characters "
                  + "named above are the sample to work from. (2) GLYPHS PRESENT WITH INK, while the "
                  + "user still sees them missing, means THE CAPTURE IS CORRECT and the loss is "
                  + "DOWNSTREAM: the resolve blit, the mip chain, the display quad or the eye. That "
                  + "exonerates thirteen builds of capture-content work in one line and moves the "
                  + "entire search to the display side. ModBuild 208 measures the FIRST TWO of those "
                  + "four — see the RESOLVE BLIT and MIP CHAIN verdicts — so branch (2) is no longer "
                  + "a list of four suspects but at most two, and if both of those verdicts read "
                  + "VERIFIED the remaining pair is the display quad's material and sampler state and "
                  + "the stereo eye pass, neither of which any counter in this class has ever "
                  + "looked at. (3) THIS LINE CAN NEVER MEAN 'INCONCLUSIVE'. If the census could not run "
                  + "— no readback support, the target re-allocated under it, the orientation check "
                  + "undecided, the caps biting, a multisampled source — it prints a NOT ANSWERABLE "
                  + "line naming WHICH, and this line is not printed at all. (4) READ THE THREE "
                  + "MOMENTS TOGETHER: the release edge, thirty frames later, and the settled cadence. "
                  + "The user's report is that the picture FREEZES broken on release, so 'EMPTY at the "
                  + "release edge and INKED thirty frames later' is a transient the eye saw for a third "
                  + "of a second, while 'EMPTY at both' is a LATCHED image and a different bug. (5) "
                  + "AND READ THE VERDICT BELOW BEFORE ANY OF THAT. ModBuild 205 could not tell a "
                  + "missing glyph from a glyph this instrument was looking for in the wrong place, "
                  + "and its own output said so: one component, one mesh, one draw call, reporting a "
                  + "different scattered subset of its 17 glyphs on every reading. You cannot "
                  + "rasterise half a mesh. Everything above is only about MISSING INK if the verdict "
                  + "says the mapping is VERIFIED. (6) ONE HYPOTHESIS IS ALREADY DEAD AND SHOULD NOT "
                  + "BE RE-OPENED: sub-texel PHASE ATTENUATION — a one-texel stroke split across two "
                  + "texels at half intensity each, which this class chased from ModBuild 198 to 202 "
                  + "and which would also produce a varying scattered subset. It is killed by the "
                  + "EMPTY median deviation printed above: an attenuated stroke reads tens of 255, not "
                  + "single digits. If that median is at or near zero, the ink at the predicted place "
                  + "is not faint, it is NOT THERE — which leaves exactly two live possibilities, and "
                  + "the verdict below picks between them: the ink is somewhere else (displacement), "
                  + "or it was never drawn (absence). NOTE the distinction from the DIM CAPTURE case "
                  + "the verdict can also name: that one is about the INKED median COLLAPSING while "
                  + "the background RISES, i.e. the whole picture fading. This one is about the EMPTY "
                  + "median, i.e. whether the gaps are faint or bare. They are different readings of "
                  + "different numbers and only one of them was ever chased.");

        Sb.Append(" (7) AND THE ONE THING THAT CHANGED IN ModBuild 208: the census now reads the "
                  + "levels the eye SAMPLES, not only the level it does not. The ModBuild 207 session "
                  + "paired every verdict with its glyph fates and the pairing was clean — 13 readings "
                  + "of MAPPING VERIFIED with 0 EMPTY, 9 of ARTEFACT with 113 empty at effective "
                  + "alpha 0.035/0.160, 3 MIXED — with not ONE reading that was both fully opaque and "
                  + "missing glyphs. THE MIP 0 CAPTURE IS CORRECT, which is branch (2) above, and the "
                  + "next question is entirely the MIP CHAIN VERDICT below. If that reads VERIFIED "
                  + "too, the whole capture side is exonerated and the remaining suspects are the "
                  + "display quad's material and sampler state, the mip LOD bias, and the stereo eye "
                  + "pass — none of which any counter in this class has ever measured.");

        // ---- REQUIREMENT 4: THE VERDICTS, LAST AND EACH IN ONE SENTENCE, CHOSEN BY THE NUMBERS ---
        Sb.Append(" MAPPING VERDICT: ").Append(c.MappingVerdict);
        Sb.Append(" MIP CHAIN VERDICT: ").Append(c.MipVerdict);

        VRLog.Info(Scope, Sb.ToString());
        Sb.Length = 0;
    }

    /// <summary>
    /// <b>THE FOUR PLANES SIDE BY SIDE ON ONE LINE — the shape that makes ModBuild 208 readable in a
    /// glance.</b> A plane that could not be read prints WHY in the same position a count would have
    /// been, because "mip 2 lost every glyph" and "mip 2 does not exist" must never look alike.
    /// </summary>
    private static void AppendInkPlanes(Entry e, InkCensus c)
    {
        Sb.Append(" THE PLANES, SIDE BY SIDE (mip 0 is what every previous build measured; MIP 1 and "
                  + "MIP 2 are what the eye actually samples at this window's trilinear LOD of ")
          .Append(Mathf.Max(0f, Mathf.Log(Mathf.Max(e.TexelsPerRenderedPx, 1e-4f), 2f)).ToString("F2"))
          .Append(", out of ").Append(c.MipCount).Append(" level(s) the display target carries):");
        for (int i = 0; i < InkPlaneOrder.Length; i++)
        {
            InkPlane p = c.Planes[InkPlaneOrder[i]];
            Sb.Append(' ').Append(p.Short).Append(": ");
            if (!p.Available)
            {
                Sb.Append("NOT READ — ").Append(p.Unavailable).Append('.');
                continue;
            }
            if (p.Failed)
            {
                Sb.Append("FAILED — ").Append(p.FailWhy).Append('.');
                continue;
            }
            if (!p.Landed)
            {
                Sb.Append("requested but never delivered.");
                continue;
            }
            Sb.Append(p.Ink).Append(" of ").Append(p.Judged).Append(" judged inked, ")
              .Append(p.Empty).Append(" empty, ").Append(p.BelowFloor)
              .Append(" below the ").Append(InkMinQuadTexels.ToString("F0")).Append("-texel floor")
              .Append(p.Deferred > 0 ? $", {p.Deferred} deferred by the budget" : string.Empty)
              .Append(" (strip ").Append(p.W).Append('x').Append(p.H).Append(" at x=").Append(p.X)
              .Append("; smallest quad still judged ").Append(p.SmallestJudged.ToString("F1"))
              .Append(" texels; background ").Append((p.Background * 255f).ToString("F1"))
              .Append("/255; INKED median ")
              .Append(p.MedDevInk >= 0f ? (p.MedDevInk * 255f).ToString("F1") + "/255"
                                        : "n/a (no glyph on this plane was judged INKED)")
              .Append(", EMPTY median ")
              .Append(p.MedDevEmpty >= 0f ? (p.MedDevEmpty * 255f).ToString("F1") + "/255"
                                          : "n/a (no glyph on this plane was judged EMPTY)")
              .Append("; orientation ")
              .Append(p.Decided
                  ? (p.Flipped ? "row 0 = TOP" : "row 0 = BOTTOM")
                  : "undecided on its own data, borrowed from the reference plane")
              .Append(", correlation ").Append(p.CorrAsIs.ToString("F2")).Append(" as delivered / ")
              .Append(p.CorrFlip.ToString("F2")).Append(" reversed; profile stride ")
              .Append(p.ProfileStride).Append(p.Confirming ? " (a CONFIRMATION sweep)"
                                                           : " (the DECIDING sweep)")
              .Append("; judged in ").Append(p.JudgeMs.ToString("F2"))
              .Append(" ms of which ").Append(p.ProfileMs.ToString("F2"))
              .Append(" ms was the orientation prologue, against a granted share of ")
              .Append(p.BudgetMs.ToString("F2")).Append(" ms")
              .Append(p.Overran ? ", BUDGET SPENT" : string.Empty).Append(").");
        }
    }

    /// <summary>
    /// <b>THE CENSUS COULD NOT ANSWER, AND SAYS WHICH.</b> There is deliberately no third state: a
    /// census either prints <see cref="ReportInkCensus"/>'s verdict or prints this, naming the exact
    /// reason. Five remedies in this project have shipped covering only part of their subject and
    /// reading clean; an instrument that fell over must never be mistaken for one that found nothing.
    /// <para>Throttled on the REASON: the same refusal repeats at most once per
    /// <see cref="ReportIntervalSeconds"/>, while a NEW reason prints immediately. A window with no
    /// text would otherwise print three identical lines every ten seconds forever.</para>
    /// </summary>
    private static void ReportInkUnanswerable(Entry? e, InkCensus c, string reason, string why)
    {
        // Same as ReportInkCensus: this census is over and its share of the mod-wide budget goes back
        // here. If it did not, MaxInkCensusesInFlight = 1 would mean ONE refusal silences the
        // instrument for the rest of the session — while every line still claimed it was armed.
        EndInkCensus(c);
        c.Unanswerable++;
        float now = Time.unscaledTime;
        bool sameReason = string.Equals(c.LastUnanswerable, why, System.StringComparison.Ordinal);
        if (sameReason && now < c.UnanswerableNextPrint)
            return;
        c.LastUnanswerable = why;
        c.UnanswerableNextPrint = now + ReportIntervalSeconds;
        VRLog.Warn(Scope, "PANEL SUPERSAMPLE INK CENSUS NOT ANSWERABLE '"
                          + (e != null ? e.Window : "(window gone)") + "'"
                          + (e != null ? OpenViewTag(e) : string.Empty)
                          + ": the census asked for at " + (reason.Length > 0 ? reason : "an unnamed moment")
                          + " produced NO reading, because " + why + ". THE CONSEQUENCE, stated so this "
                          + "cannot be read as a clean result: NOTHING follows about the captured "
                          + "image from this census in EITHER direction — it is not evidence that the "
                          + "glyphs are present and it is not evidence that they are missing. "
                          + $"SINCE ENGAGE: {c.Completed} census(es) completed, {c.Unanswerable} "
                          + $"unanswerable, {c.Errors} readback error(s), {c.DroppedStale} dropped on a "
                          + $"re-allocation, {c.Threw} threw. Nothing about the capture, the mip chain, "
                          + "the layer isolation, input or multiplayer is affected by this instrument "
                          + "either way — it only reads.");
    }

    /// <summary>The string a component is named by on the census line, trimmed so one very long label
    /// cannot make the line unreadable. Newlines and rich-text angle brackets are flattened for the
    /// same reason.</summary>
    private static string InkLabel(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "(empty)";
        const int max = 48;
        string s = text.Replace('\n', ' ').Replace('\r', ' ').Replace('<', '{').Replace('>', '}');
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    /// <summary>
    /// <b>THE CONTENT-SCALE FIELD — ModBuild 201, and it is the answer to the user's own question:
    /// "what do these two windows do differently from the other sub-menus?"</b>
    ///
    /// <para>Everything about why is on <see cref="Entry.MinContentScale"/>. What this field must do
    /// is print the WHOLE DISTRIBUTION with its count, never one summary number: last round's
    /// <c>RESAMPLE VERDICT WORST</c> was quoted as an operating point when it was a minimum, and that
    /// cost a build. So: LOWEST / MEAN / HIGHEST over N readings, plus the DERIVED quantity that
    /// actually decides the picture — texels per authored pixel FOR THE SCALED SUBTREE — next to the
    /// band limit it is being judged against.</para>
    /// </summary>
    private static void AppendContentScale(Entry e)
    {
        float mean = e.ContentScaleReadings > 0 ? e.ContentScaleSum / e.ContentScaleReadings : 1f;
        float rate = Mathf.Max(e.AchievedFactor, 0.01f);
        Sb.Append(" CONTENT SCALE").Append(OpenViewTag(e))
          .Append(" (ModBuild 201 — the field that answers 'what do the character and "
                  + "perks views do differently from the other four'): the smallest SUBSTANTIAL "
                  + "content scale inside this window's capture frame is ")
          .Append(e.MinContentScale.ToString("F3"))
          .Append(e.MinContentScale < ScaledContentThreshold
              ? $" ('{e.MinContentScaleName}', {e.MinContentScaleArea:F0} uGUI px², measured against "
                + $"an area floor of {MinScaledAreaFraction:P0} of the host rect)"
              : " (nothing substantial in this window is downscaled at all)")
          .Append(", out of ").Append(e.ContentScaleSamples)
          .Append(" graphic(s) the last frame measurement could read a scale from, ")
          .Append(e.ScaledGraphics).Append(" of them below ")
          .Append(ScaledContentThreshold.ToString("F2")).Append(" (largest ")
          .Append(e.ScaledGraphicsArea.ToString("F0"))
          .Append(" uGUI px²). THE WHOLE DISTRIBUTION SINCE ENGAGE, because the last reading is not "
                  + "the operating point: LOWEST ")
          .Append(e.MinContentScaleLowest.ToString("F3")).Append(", MEAN ")
          .Append(mean.ToString("F3")).Append(", HIGHEST ")
          .Append(e.MinContentScaleHighest.ToString("F3")).Append(", over ")
          .Append(e.ContentScaleReadings).Append(" reading(s) (")
          .Append(e.ContentScaleUnmeasured)
          .Append(" measurement(s) produced no scale sample at all and are excluded, so an "
                  + "UNMEASURED window can never read as an unscaled one). WHAT IT COSTS: the render "
                  + "target is sized from the HOST frame, so a subtree at scale s receives "
                  + "rate x s texels per ITS OWN authored pixel. RATE asked ")
          .Append(e.AskedFactor.ToString("F2")).Append(" (base ").Append(e.Factor.ToString("F2"))
          .Append(" / ").Append(e.MinContentScale.ToString("F3")).Append("), GOT ")
          .Append(rate.ToString("F2"))
          .Append(e.AskedFactor > rate + 1e-3f
              ? $" — CUT DOWN by the {MaxRtDimension} px per-axis ceiling and the "
                + $"{Mb(MaxPanelVramBytes)} MB per-panel budget, not by choice"
              : " — nothing cut it down")
          .Append("; so host-scale content is captured at ").Append(rate.ToString("F2"))
          .Append(" texels per authored px and the smallest-scaled subtree at ")
          .Append((rate * e.MinContentScale).ToString("F2")).Append(", against a band limit of ")
          .Append(BandLimitedTexelsPerPixel.ToString("F2"))
          .Append(rate * e.MinContentScale < BandLimitedTexelsPerPixel - 0.01f
              ? ". THE SECOND NUMBER IS BELOW THE BAND LIMIT AND THIS PATH CANNOT REACH IT: at "
                + "13.3 bytes per texel a 160 MB panel is ~12.6 Mtexel, and a 1715x1107 capture "
                + "frame is already 1.9 Mpx, so the rate ceiling is about 2.5 whatever is asked. "
                + "That subtree is therefore rasterised at or near ONE texel per authored pixel — "
                + "the un-supersampled state the ModBuild 198 floor exists to abolish — and no "
                + "capture setting can finish the job. The only complete cure is for the sub-view "
                + "not to be scaled."
              : ". Both are at or above the band limit, so this window's rasterisation is "
                + "band-limited for every subtree in it.")
          .Append(" THE FRAME IS QUANTISED to ").Append(FrameQuantumPx.ToString("F0"))
          .Append(" authored px and the rate to ").Append(RateQuantum.ToString("F2"))
          .Append(", so a frame that grows or shrinks shifts the captured image by a WHOLE number of "
                  + "texels and the sub-texel rasterisation phase never moves — which is what stops "
                  + "the picture re-rolling on the measurement cadence. Frame changes since engage: ")
          .Append(e.Reallocations).Append(" re-allocation(s) over ").Append(e.ContentMeasures)
          .Append(" measurement(s); on a quantised frame those two numbers should now diverge "
                  + "sharply, and if they do not, the frame is being moved by something this grid "
                  + "does not cover.");
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
        Sb.Append(" CONTENT INTEGRITY").Append(OpenViewTag(e))
          .Append(" (the 'kaputte Anzeige' instrument): ").Append(e.ContentScans)
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
                  + "painted over, which nothing in THIS scan can see. FROM ModBuild 203 THAT BLIND "
                  + "SPOT IS NO LONGER OPEN: it is measured by the OVER-PAINT CENSUS field below, "
                  + "which is where a clean reading here must be taken next)")
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
          .Append(". TMP SUB-MESHES (ModBuild 200 — THE OBJECT EVERY COUNT ABOVE IS BLIND TO. A "
                  + "TextMeshProUGUI draws only the glyphs its FIRST material serves; a second atlas "
                  + "page, a fallback font or an inline sprite goes to a TMP_SubMeshUI on a CHILD "
                  + "GameObject with its own CanvasRenderer, material, LAYER and active state. Their "
                  + "vertex data is inside the meshInfo the mesh scan reads and finds clean, and the "
                  + "cull test above asks the PARENT. So 'every quad is perfect' and 'half the word "
                  + "is missing from the capture' are not in contradiction — they are measurements of "
                  + "two different objects): ")
          .Append(e.SubMeshesSeen)
          .Append(" sub-mesh(es) on the last scan, of which ").Append(e.SubMeshesEmpty)
          .Append(" EMPTY AND POOLED (ModBuild 201's correction: TMP keeps one child per material "
                  + "reference the string has EVER needed and empties the surplus, and an empty one "
                  + "is legitimately culled, transparent and untextured — its normal life looks "
                  + "exactly like the abuse. The ModBuild 200 log read '19 sub-mesh(es), of which 17 "
                  + "culled/transparent' on the character window and 0-of-0 or 0-of-1 everywhere "
                  + "else, and that gap was NOT evidence: the character window is simply the only "
                  + "one with enough text to pool any). THE REAL DENOMINATOR IS THE ")
          .Append(e.SubMeshesInUse)
          .Append(" THAT CARRY VERTICES, of which ").Append(e.SubMeshesInactive)
          .Append(" INACTIVE, ").Append(e.SubMeshesCulled)
          .Append(" CULLED or fully transparent, ").Append(e.SubMeshesNoTexture)
          .Append(" with NO MATERIAL OR NO BOUND TEXTURE, and ").Append(e.SubMeshesWrongLayer)
          .Append(" (counted over ALL ").Append(e.SubMeshesSeen)
          .Append(", pooled included) NOT ON THIS PANEL'S PRIVATE CAPTURE LAYER ").Append(e.Layer)
          .Append(" — that last count is glyphs that are present in the mesh, present in the eye and "
                  + "MISSING FROM THE TEXTURE, and it is REPAIRED ON SIGHT rather than reported "
                  + "(unconditional, never gated on a defect count, and deliberately BEFORE the "
                  + "pooled test so a surplus child that the next string fills is never filled on "
                  + "the wrong layer). A denominator of 0 means this window's text needs a single "
                  + "material and this family CANNOT be its fault, which is a different finding from "
                  + "'they were all clean'. Worst: ")
          .Append(e.SubMeshWorst.Length > 0 ? e.SubMeshWorst
                                            : "none — every sub-mesh was active, drawn, textured and "
                                              + "on the capture layer")
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
    /// THE ATTRIBUTION TAG — which sub-view every number on this line was measured of.
    ///
    /// <para><b>WHY IT IS ON EVERY FIELD AND NOT ONCE AT THE TOP.</b> Two of the party window's six
    /// sub-views render broken and four do not. Every field this class has ever printed averaged over
    /// whichever one happened to be open when the 10 s cadence fired, so eight rounds of correct
    /// measurements produced no decision: "0 defects out of 3919 glyph lookups" is unreadable until
    /// the line says whether it was taken of the CHARACTER SHEET or of the ability cards. The tag is
    /// repeated per field so that a log grepped for one field name still carries its attribution.</para>
    ///
    /// <para>It prints TWO independently derived answers — the largest open sub-view ROOT this class
    /// measured for itself, and the game's own <c>NewPartyDisplayUI.ActiveDisplay</c> — because a
    /// disagreement between them is information and a single merged answer would hide it.</para>
    /// </summary>
    private static string OpenViewTag(Entry e)
    {
        if (e.OverPaintScans <= 0)
            return " [SUB-VIEW: not yet derived — no census has run on this panel]";
        string name = e.OpenViewName.Length > 0 ? "'" + e.OpenViewName + "'" : "none open";
        string active = e.OpenViewActive.Length > 0 ? e.OpenViewActive : "unavailable";
        return $" [SUB-VIEW: {name}, ActiveDisplay={active}, {e.OpenViewCount} open]";
    }

    /// <summary>
    /// <b>THE OVER-PAINT CENSUS FIELD (ModBuild 203).</b> Everything about why is on
    /// <see cref="Entry.OverPaintCovered"/>. What this field must do is print the ANSWER with its
    /// denominator, the plates that produced it in full, the distribution behind it, and the outcome
    /// that KILLS the hypothesis, in a form that cannot be read as "the instrument found nothing yet".
    /// </summary>
    private static void AppendOverPaint(Entry e)
    {
        Sb.Append(" OVER-PAINT CENSUS").Append(OpenViewTag(e))
          .Append(" (ModBuild 203 — THE BLIND SPOT THE FIELD ABOVE NAMES IN ITS OWN SENTENCE: "
                  + "'NOT the same as being drawn and then painted over, which nothing in this scan "
                  + "can see'. This is that scan): ").Append(e.OverPaintScans)
          .Append(" census(es) so far at ")
          .Append((e.OverPaintScans > 0 ? e.OverPaintMs / e.OverPaintScans : 0.0).ToString("F3"))
          .Append(" ms each for the parts that are NOT shared with the text scan (resolving the open "
                  + "sub-view, and the plate-versus-graphic comparison); its PER-NODE share rides the "
                  + "content-integrity walk above and is inside that walk's ms figure, because this "
                  + "census adds no traversal of its own. ATTRIBUTION: the reference rect is ")
          .Append(e.OpenViewRect.width.ToString("F0")).Append('x')
          .Append(e.OpenViewRect.height.ToString("F0")).Append(" uGUI px, taken from ")
          .Append(e.OpenViewSource.Length > 0 ? e.OpenViewSource : "nothing yet")
          .Append(". THE LAST CENSUS visited ").Append(e.OverPaintVisited)
          .Append(" transform(s) and recorded ").Append(e.OverPaintGraphics)
          .Append(" DRAWING graphic(s) — that count is the DENOMINATOR of every number below and is "
                  + "printed first for exactly that reason — of which ").Append(e.OverPaintPlates)
          .Append(" are FULL-FRAME PLATES (covering at least ")
          .Append((OverPaintPlateFraction * 100f).ToString("F0"))
          .Append(" % of the open sub-view's AREA), ").Append(e.OverPaintOpaquePlates)
          .Append(" of them effectively OPAQUE (own colour alpha x CanvasRenderer alpha x inherited "
                  + "alpha >= 0.99 — an opaque plate DELETES what it covers, a translucent one only "
                  + "TINTS it). THE ANSWER FIELD: those plate(s) are painted over ")
          .Append(e.OverPaintCovered).Append(" of the ").Append(e.OverPaintGraphics)
          .Append(" drawing graphic(s) IN TOTAL, summed over every plate (worst SINGLE plate ")
          .Append(e.OverPaintWorstCovered).Append("), across ")
          .Append(e.OverPaintCoveredArea.ToString("F0"))
          .Append(" uGUI px² of intersecting area, and ").Append(e.OverPaintTied)
          .Append(" of those victims sit at the SAME resolved sortingOrder under a DIFFERENT canvas "
                  + "(an UNSTABLE TIE, which Unity resolves by canvas registration and no census can "
                  + "predict). THAT LAST COUNT IS EXPECTED TO BE 0 AND IS PRINTED ANYWAY: the "
                  + "sibling-canvas draw-order hypothesis is DEAD — 19 of this window's 20 adopted "
                  + "nested canvases carry overrideSorting = false and are not sorting roots at all, "
                  + "and the one exception ('UI Party Inventory Item Tooltip') cannot tie with "
                  + "itself. A non-zero reading here would mean the adoption's sorting state changed "
                  + "under us; a zero is what makes the ordering behind the ANSWER above auditable "
                  + "rather than asserted")
          .Append(e.OverPaintTruncated
              ? "; THE CENSUS WAS TRUNCATED at " + MaxOverPaintGraphics + " recorded graphics, so "
                + "every count here is a LOWER BOUND"
              : "; the census ran to completion, so these are totals")
          .Append(". PER PLATE (at most ").Append(MaxPlatesReported)
          .Append(" named; the COUNT above is not capped): ")
          .Append(e.OverPaintNote.Length > 0
              ? e.OverPaintNote
              : e.OverPaintScans > 0
                  ? "none — this sub-view has NO graphic covering " + (OverPaintPlateFraction * 100f)
                    .ToString("F0") + " % of it, so there is nothing here that could paint over "
                    + "anything, and this whole family is ruled out FOR THIS VIEW"
                  : "the census has not run yet")
          .Append(". THE WHOLE DISTRIBUTION SINCE ENGAGE, because one reading is not the operating "
                  + "point (the ModBuild 201 mistake this project has now paid for three times): "
                  + "LOWEST ").Append(e.OverPaintCoveredLowest).Append(", MEAN ")
          .Append((e.OverPaintReadings > 0
                      ? (double)e.OverPaintCoveredSum / e.OverPaintReadings : 0.0).ToString("F1"))
          .Append(", HIGHEST ").Append(e.OverPaintCoveredHighest)
          .Append(" painted-over graphic(s), over ").Append(e.OverPaintReadings)
          .Append(" census(es) — and each of those readings belongs to WHICHEVER SUB-VIEW WAS OPEN "
                  + "at the time, which is why the attribution tag is on every field of this line "
                  + "and not once at the top. FOREIGN RENDER SUBTREES, NAMED (ModBuild 193 has "
                  + "counted these since it was written and the engage line names exactly the FIRST "
                  + "one; the party window reports 4 and only 'FX_Smoke' has ever been in a log): ")
          .Append(e.ForeignRenderers).Append(" recorded out of ").Append(e.OverPaintVisited)
          .Append(" transform(s) visited, ").Append(e.ForeignCoplanar)
          .Append(" of them at host-local |z| <= ").Append(ForeignCoplanarEpsPx.ToString("F2"))
          .Append(" px. WHY THE z MATTERS: the mod excludes these from the capture entirely and lets "
                  + "the HEAD camera draw them at their true world pose, while the display quad sits "
                  + "at host-local z = 0 — so a foreign renderer at z ~ 0 is COPLANAR with the quad, "
                  + "and a depth tie can resolve differently in the two MultiPass eyes, which reads "
                  + "as one-eyed flicker and is invisible to every other field on this line. THEY "
                  + "ARE (at most ").Append(MaxForeignNamed).Append(" named): ")
          .Append(e.ForeignNote.Length > 0
              ? e.ForeignNote
              : "none inside this window, so that hypothesis cannot apply to it at all")
          .Append('.');
    }

    /// <summary>
    /// <b>THE SUB-VIEW SWEEP BURST FIELD (ModBuild 203).</b> The per-burst line
    /// (<c>PANEL SUPERSAMPLE SUB-VIEW BURST</c>) is the detailed one; this is the standing summary on
    /// the periodic line, so a session in which no tab was ever pressed and a session in which every
    /// burst converged immediately cannot leave the same evidence. Everything about why is on
    /// <see cref="Entry.SubViewChanges"/>.
    /// </summary>
    private static void AppendSubViewBurst(Entry e)
    {
        Sb.Append(" SUB-VIEW BURST").Append(OpenViewTag(e))
          .Append(" (ModBuild 203 — the first remedy in this class keyed on a CONTENT change instead "
                  + "of on MOTION, which is what the user's newest report needs: 'mittlerweile taucht "
                  + "es auch initial kaputt auf wenn man das Fenster öffnet', with no drag at all): ")
          .Append(e.SubViewChanges)
          .Append(" sub-view change(s) noticed since engage, each forcing a capture-frame re-measure "
                  + "on THAT frame; ").Append(e.SubViewChangesCoalesced)
          .Append(" of them arrived within ").Append(SweepBurstCooldownFrames)
          .Append(" frame(s) of the last armed burst and were COALESCED into it rather than arming a "
                  + "second one (the cost fuse — a large gap between those two numbers is a window "
                  + "whose active-child signature FLAPS, which is a finding and not a reason to stay "
                  + "quiet); ").Append(e.SweepBursts)
          .Append(" burst(s) run at ")
          .Append((e.SweepBursts > 0 ? e.SweepBurstMs / e.SweepBursts : 0.0).ToString("F2"))
          .Append(" ms each in total across all their frames (floor ").Append(MinSweepBurstFrames)
          .Append(" frame(s), cap ").Append(MaxSweepBurstFrames)
          .Append(", extended only while sweeps keep finding arrivals, ended after ")
          .Append(SweepBurstMissTolerance)
          .Append(" consecutive empty sweeps). LAST BURST: ").Append(e.SweepBurstFramesLast)
          .Append(" frame(s), ").Append(e.SweepBurstMovedLast)
          .Append(" transform(s) moved onto the capture layer. THE HOLE — frames between the sub-view "
                  + "changing and the LAST sweep that still found a late joiner, i.e. how long this "
                  + "view's content was MISSING FROM THE CAPTURE and drawn straight into the eye: "
                  + "LAST ")
          .Append(e.SweepBurstHoleFramesLast).Append(", WORST ").Append(e.SweepBurstHoleFramesMax)
          .Append(", MEAN ")
          .Append((e.SweepBurstHoleReadings > 0
                      ? (double)e.SweepBurstHoleSum / e.SweepBurstHoleReadings : 0.0).ToString("F1"))
          .Append(" over ").Append(e.SweepBurstHoleReadings).Append(" completed burst(s), of which ")
          .Append(e.SweepBurstsConverged)
          .Append(" found NOTHING AT ALL (-1 anywhere here means no burst has completed yet, which is "
                  + "a different statement from a hole of 0 and must not be read as one). The "
                  + "ordinary cadences are unchanged and still own every other frame: ")
          .Append(SweepIntervalFrames).Append(" frame(s) when still, ")
          .Append(MovingSweepIntervalFrames).Append(" while moving.");
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
        Sb.Append(" CAPTURE PATH").Append(OpenViewTag(e))
          .Append(" (cumulative; the question is whether a DRAGGED window takes the "
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
                  + "frame's image across another's texels)");

        // ---- THE FOURTH EXPECTED-ZERO COUNTER (ModBuild 204) -------------------------------------
        // The 203 forensics named this gap in one sentence and it is quoted verbatim in TargetLife:
        // "there is no counter for frames on which the quad sampled a freshly re-allocated target
        // that had not yet been captured into. With 58 re-allocations, 45 of them inside grabs and 12
        // inside a single 2.8 s stretch, that is the one path a 'random frozen state' could take that
        // this log cannot see." It is a REAL per-frame test of live state — NoteQuadSample compares
        // the texture identity the quad is bound to against a per-target completed-capture count, at
        // the first camera of the frame that is not one of ours — and never a derivation from the
        // reallocation count.
        TargetLife life = LifeOf(e);
        Sb.Append(", and ").Append(life.SampledUncaptured)
          .Append(" frame(s) on which the quad was VISIBLE AND SAMPLED A TARGET THAT HAD NEVER BEEN "
                  + "CAPTURED INTO SINCE ITS CREATION, out of ").Append(life.SampleChecks)
          .Append(" visible frame(s) checked (the denominator matters as much as the count here: a "
                  + "zero with a zero denominator is an instrument that never ran, which is how four "
                  + "remedies in this class's history came to ship and never execute). WHAT A HIT "
                  + "WOULD MEAN: a freshly created RenderTexture's contents are UNDEFINED in D3D11, "
                  + "not black, and ClearRt only ever wrote mip level 0 — this window samples at "
                  + "LOD 1.66, i.e. levels 1 and 2, which is why that failure reads as garbage or as "
                  + "nothing rather than as a slightly wrong image. ModBuild 204 closes it in two "
                  + "layers (CreateMipRt generates the whole chain at creation; Reallocate primes the "
                  + "new pair with a synchronous capture+resolve+mip before it returns), so this "
                  + "number is the PROOF of that rather than its assumption. FILL LATENCY — frames "
                  + "between a display target being created and its FIRST completed "
                  + "capture+resolve+mip, the whole distribution because one end of it is not the "
                  + "operating point: LOWEST ")
          .Append(life.FillReadings > 0 ? life.FillFramesMin : -1).Append(", MEAN ")
          .Append((life.FillReadings > 0 ? (double)life.FillFramesSum / life.FillReadings : -1.0)
                  .ToString("F2"))
          .Append(", HIGHEST ").Append(life.FillFramesMax).Append(", LAST ")
          .Append(life.FillFramesLast).Append(" over ").Append(life.FillReadings)
          .Append(" target(s) (-1 = no target has completed a first fill yet, which is a different "
                  + "statement from a latency of 0 and must not be read as one; with the prime in "
                  + "force every reading should be 0, and a 1 anywhere here is a target that reached "
                  + "an eye pass before its first capture).");
    }

    /// <summary>
    /// <b>THE RE-ALLOCATION PRICE LIST (ModBuild 204) — what asked for each one, whether the window
    /// was being carried at the time, and what it cost.</b>
    ///
    /// <para><b>WHY IT EXISTS.</b> The ModBuild 203 hardware log could say only <i>"58 re-allocations
    /// since engage"</i>. Getting from that to the actual finding — that they alternated 28/28
    /// between two capture frames one 32 px quantum apart, that 45 of the 58 fell inside grabs, that
    /// the rate was 2.51/s while held against 0.29/s while not, and that 12 landed in a single 2.8 s
    /// stretch — took a whole session of cross-referencing timestamps against the grab lines. Every
    /// one of those numbers is a field on this line now.</para>
    ///
    /// <para><b>AND IT PRICES THE NO-OP.</b> <see cref="ReleaseRepair"/> calls
    /// <see cref="Reallocate"/> unconditionally. That call is already free when the frame has not
    /// moved a target-sized pixel — the pixel-count compare early-outs before anything is allocated —
    /// and the NO-OP column is what lets the log say so instead of leaving the next round to read the
    /// source for it. Where the release-repair row's no-op count equals its total, the unconditional
    /// call cost nothing at all and the release's real cost is the text regeneration beside it.</para>
    /// </summary>
    private static void AppendReallocations(Entry e)
    {
        TargetLife life = LifeOf(e);
        int allocated = 0, moving = 0, noOp = 0;
        for (int i = 0; i < ReallocTriggerCount; i++)
        {
            allocated += life.ByTrigger[i];
            moving += life.ByTriggerMoving[i];
            noOp += life.NoOpByTrigger[i];
        }
        Sb.Append(" RE-ALLOCATION PRICE LIST").Append(OpenViewTag(e)).Append(" (since engage): ")
          .Append(allocated).Append(" allocation(s) that actually built a new target pair, of which ")
          .Append(moving).Append(" were taken while the window was MOVING");
        for (int i = 0; i < ReallocTriggerCount; i++)
        {
            Sb.Append(i == 0 ? " — " : ", ").Append(ReallocTriggerNames[i]).Append(' ')
              .Append(life.ByTrigger[i]).Append(" (").Append(life.ByTriggerMoving[i])
              .Append(" moving, ").Append(life.NoOpByTrigger[i]).Append(" no-op)");
        }
        Sb.Append(". NO-OPS: ").Append(noOp)
          .Append(" call(s) returned without touching a target because the resolved pixel counts were "
                  + "identical — that is what ReleaseRepair's UNCONDITIONAL call costs when the frame "
                  + "has not changed, and it is ResolveRate's float arithmetic and nothing else. ")
          .Append(life.Refusals)
          .Append(" refusal(s) (the VRAM budget or the driver said no; the window keeps its existing "
                  + "target and merely resamples). COST: ").Append(life.CreateMs.ToString("F2"))
          .Append(" ms creating targets, ").Append(life.DestroyMs.ToString("F2"))
          .Append(" ms releasing and destroying them, and ").Append(life.PrimeMs.ToString("F2"))
          .Append(" ms priming (").Append(life.Primed)
          .Append(" synchronous capture+resolve+mip pass(es) run inside the allocating LateUpdate, ")
          .Append(life.PrimeCarriedOver)
          .Append(" fell back to carrying the previous image over, ").Append(life.PrimeFailed)
          .Append(" could not prime at all) = ")
          .Append((life.CreateMs + life.DestroyMs + life.PrimeMs).ToString("F2"))
          .Append(" ms total, i.e. ")
          .Append((allocated > 0
                      ? (life.CreateMs + life.DestroyMs + life.PrimeMs) / allocated : 0.0).ToString("F2"))
          .Append(" ms per allocation. HOW TO READ IT: the ModBuild 203 log measured 58 "
                  + "re-allocations in 62 s at 2.51/s WHILE HELD against 0.29/s while not, 45 of them "
                  + "inside grabs and 28/28 alternating between two capture frames one 32 px quantum "
                  + "apart. A 'content frame change' row that is large and mostly MOVING is that flap "
                  + "still running; a 'release repair' row whose no-op count equals its total is the "
                  + "unconditional call costing nothing, which is the expected reading; and the ms "
                  + "column is what a re-allocation is worth paying to avoid at all.");
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
        Sb.Append(" MOTION BUDGET").Append(OpenViewTag(e))
          .Append(" (this 10 s window, threshold ").Append(FrameBudgetMs.ToString("F2"))
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
        float level0Unbiased = lod >= 1f ? 0f : 1f - lod;
        float bias = shownBias;

        // ---- ModBuild 204: THE BIAS BELONGS IN THE VERDICT, AND IT WAS NEVER IN IT ---------------
        // THE DEFECT, and it is the exact shape of this project's "an instrument that models a SUBSET
        // of what the eye sees agrees with every broken build". The three lines above compute the LOD
        // from the minification ALONE; `bias` was assigned and then used only for PRINTING, so the
        // verdict below tested `texelsPerPixel >= 2.00` and announced "BAND-LIMITED: the eye reads no
        // unfiltered level 0 at all". ModBuild 203 shipped [WorldUI] PanelMipLodOffset at -0.50 and
        // its own hardware log confirms the dial is live ("asked -0.50 ... the LIVE display render
        // target reads -0.50 back ... so the dial is running"). With a bias b, trilinear selects
        // LOD + b, so unfiltered level 0 re-enters the blend below 2^(1-b) texels per rendered pixel
        // — 2.83 at b = -0.5, NOT 2.00. Of that session's 47 RESAMPLE VERDICT samples, 34 read below
        // 2.83 (range 1.73 .. 3.03): a large majority carried unfiltered level 0 while this line
        // asserted the opposite.
        //
        // WHY IT IS NOT COSMETIC. ReportRelease calls this very sentence to judge the FROZEN half of
        // the user's report, and BandLimitFactor's own header argues that unfiltered level 0 is
        // precisely what makes the sub-texel phase sweep during a drag and LOCK at the release —
        // which is the user's symptom verbatim. ReportMipLodOffset, in this same class, has done this
        // arithmetic correctly since ModBuild 203; the two instruments disagreed.
        //
        // BOTH FIGURES ARE PRINTED. The unbiased pair is kept because it is what every log before
        // this build carried and a reader comparing sessions needs it; the BIASED pair decides the
        // verdict, because that is what the hardware samples. The arithmetic is correct for ANY bias,
        // including 0.00, where the two collapse into one number by construction.
        float lodBiased = Mathf.Log(Mathf.Max(texelsPerPixel, 1e-4f), 2f) + bias;
        float level0Biased = 1f - Mathf.Clamp(lodBiased, 0f, 1f);
        float biasedThreshold = Mathf.Pow(2f, 1f - bias);
        bool bandLimited = texelsPerPixel >= biasedThreshold;
        bool verdictsDisagree = bandLimited != (texelsPerPixel >= BandLimitedTexelsPerPixel);
        // The counters follow the BIASED figure, deliberately: Level0Weight and Level0Readings are
        // read by ReportMipLodOffset as "measurement(s) that have read level 0 on a MINIFIED window",
        // and that claim is about the hardware, not about an unbiased model of it.
        float level0Weight = level0Biased;

        e.SamplingMeasured++;
        e.TexelsPerRenderedPx = texelsPerPixel;
        if (e.TexelsPerRenderedPxWorst <= 0f || texelsPerPixel < e.TexelsPerRenderedPxWorst)
            e.TexelsPerRenderedPxWorst = texelsPerPixel;
        if (texelsPerPixel > e.TexelsPerRenderedPxMax)
            e.TexelsPerRenderedPxMax = texelsPerPixel;
        e.TexelsPerRenderedPxSum += texelsPerPixel;
        if (authoredPerPixel > e.AuthoredPerRenderedPxMax)
            e.AuthoredPerRenderedPxMax = authoredPerPixel;
        if (authoredPerPixel > 1f)
            e.MinifiedReadings++;
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
        // ModBuild 204: DECIDED BY THE BIASED FIGURE. See the derivation above — the threshold is
        // 2^(1-bias) and not the constant, because a mip LOD offset moves the rate at which trilinear
        // stops blending level 0 by exactly that factor.
        string verdict = bandLimited
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
               + $"rather than point-sampled) -> trilinear MIP LOD {lod:F2} BEFORE the bias and "
               + $"{lodBiased:F2} AFTER it at mipMapBias {bias:F2}, so {level0Biased * 100f:F0} % of "
               + "every texture sample comes from UNFILTERED level 0 at "
               + $"{texelsPerPixel:F2}x minification (the unbiased model, which every log before "
               + $"ModBuild 204 printed and which is what a cross-session comparison needs, says "
               + $"{level0Unbiased * 100f:F0} %). {LegibilitySentence(e, authoredPerPixel)} "
               + $"RESAMPLE VERDICT — "
               + $"{texelsPerPixel:F2} RT texels per rendered eye px against a threshold of "
               + $"{biasedThreshold:F2} = 2^(1 - {bias:F2}) (the rate at which trilinear stops "
               + "blending level 0 at all, WITH the mip LOD offset in it; the unbiased constant is "
               + $"{BandLimitedTexelsPerPixel:F2} and it is the right threshold only at bias 0.00)"
               + (verdictsDisagree
                   ? " — AND THE TWO THRESHOLDS DISAGREE ON THIS SAMPLE: the unbiased model would "
                     + "have called this window "
                     + (bandLimited ? "NOT band-limited when the hardware is"
                                    : "BAND-LIMITED when the hardware is not")
                     + ", which is exactly the mislabel ModBuild 204 fixed"
                   : " — both thresholds agree on this sample")
               + ". THE WHOLE DISTRIBUTION SINCE ENGAGE, because one end of it is not the "
               + $"operating point: LOWEST {e.TexelsPerRenderedPxWorst:F2} (most MAGNIFIED — the "
               + "window held up to the face, the best case for legibility and the worst for "
               + $"aliasing), MEAN {(e.SamplingMeasured > 0 ? e.TexelsPerRenderedPxSum / e.SamplingMeasured : 0.0):F2}, "
               + $"HIGHEST {e.TexelsPerRenderedPxMax:F2} (most MINIFIED — the worst case for "
               + $"legibility), over {e.SamplingMeasured} completed "
               + "measurement(s) — this instrument samples once per report AND once per "
               + $"release, so a session with many drags has many samples ({e.Level0Readings} of them "
               + $"read unfiltered level 0 on a MINIFIED window; instrument bail-outs: {e.SamplingNoHead} no head camera, "
               + $"{e.SamplingNoEyeTarget} no readable eye target, {e.SamplingOffScreen} off-screen) "
               + $"-> {verdict}. FACTOR: asked {e.Factor:F2} RT texels per authored px "
               + $"(config {e.ConfigFactor:F2}"
               + (e.FactorFloored
                   ? $", raised to the {BandLimitFactor:F2} band-limit floor because the config value "
                     + "is still the shipped default"
                   : ", taken verbatim — this is a value the user set")
               + (e.AskedFactor > e.Factor + 0.01f
                   ? $", RAISED to {e.AskedFactor:F2} because this window's smallest substantial "
                     + $"content is drawn at {e.MinContentScale:F3} of host scale and the render "
                     + "target is sized from the HOST — see the CONTENT SCALE field"
                   : string.Empty)
               + $"), ACHIEVED {e.AchievedFactor:F2}"
               + (e.AchievedFactor < Mathf.Min(e.AskedFactor, e.Factor) - 0.01f
                   ? $" — LOWER THAN ASKED because the {MaxRtDimension} px per-axis ceiling clipped "
                     + $"a {e.Frame.width:F0}x{e.Frame.height:F0} capture frame, so the fix is only "
                     + "partly in force on this window"
                   : e.AchievedFactor < e.AskedFactor - 0.01f
                       ? $" — the {e.AskedFactor:F2} ask was cut back by the {MaxRtDimension} px "
                         + "per-axis ceiling and the VRAM budget, so the scaled subtree is at "
                         + $"{(e.AchievedFactor * e.MinContentScale):F2} texels per its own authored "
                         + $"px against a {BandLimitedTexelsPerPixel:F2} band limit — read the "
                         + "CONTENT SCALE field, it says whether that is reachable at all"
                       : " (nothing clipped it)")
               + ". CAVEAT ON EVERY LEVEL-0 FIGURE ABOVE: it is a LOWER BOUND, because anisotropic "
               + $"filtering (aniso {AnisoLevel}) selects the LOD from the MINOR axis' rate, so a "
               + "window yawed away from the head reads MORE level 0 than this line states. "
               + "HOW TO READ THE VERDICT — THE ModBuild 204 CORRECTION AND THE TRAP IT NAMES: "
               + "A FILTERING DIAL THAT THE SAMPLING VERDICT DOES NOT MODEL WILL AGREE WITH EVERY "
               + "BROKEN BUILD. Up to ModBuild 203 this verdict was computed from the minification "
               + "alone and tested against a hard 2.00, while [WorldUI] PanelMipLodOffset shipped at "
               + "-0.50 and moved the real threshold to 2.83 — so 34 of that session's 47 samples "
               + "carried unfiltered level 0 while this very sentence read 'the eye reads no "
               + "unfiltered level 0 at all'. ReportMipLodOffset had the arithmetic right the whole "
               + "time and the two instruments in one class disagreed. The lesson generalises past "
               + "this dial: whenever a new knob is added that changes what the hardware SAMPLES, "
               + "this sentence is the second place it has to land, and a verdict that cannot see a "
               + "knob will confirm whatever the knob is set to;";
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

    /// <summary>
    /// <b>THE LEGIBILITY TERM — the one quantity this instrument has never carried, and the whole of
    /// the ModBuild 199 report "die Auflösung der Fensterelemente ist stellenweise so gering, dass
    /// man den Text kaum lesen kann".</b>
    ///
    /// <para><b>WHY IT IS A DIFFERENT QUESTION FROM EVERYTHING ELSE ON THIS LINE.</b> Every other
    /// number here judges ALIASING: how much unfiltered mip level 0 the eye reads, whether the
    /// band-limit floor is in force, what the sub-texel phase can do while the window is carried.
    /// Aliasing is about which LEVEL the sampler picks. LEGIBILITY is about how many RENDERED EYE
    /// PIXELS the window is given at all, and the sampler cannot change that number by one pixel. A
    /// window minified 1.5x is showing the player two thirds of its authored resolution no matter how
    /// perfectly it is filtered, and no matter how many texels the capture target holds.</para>
    ///
    /// <para><b>AND THAT IS WHY RAISING <see cref="BandLimitFactor"/> AGAIN CANNOT HELP.</b> The
    /// factor sets RT texels per AUTHORED pixel. In the minified regime the eye's rate is already
    /// BELOW authored, so trilinear selects a mip level at or coarser than authored, and every level
    /// the factor adds ABOVE authored is a level the sampler never selects. The ModBuild 199 hardware
    /// log measures the party window at a median 2.50 RT texels per rendered eye px (1.25 authored px
    /// per rendered px) with 70 of 97 samples reading 0 % level 0 — a correctly band-limited,
    /// correctly filtered, MINIFIED image. Doubling the factor to 4 would move that to LOD 2.25 of a
    /// 4x target, which is the SAME physical resolution at 4x the VRAM. The lever is the window's
    /// SIZE IN THE EYE (its world scale and its distance), which this file does not own, or a
    /// negative <c>mipMapBias</c>, which trades the blur back for the aliasing the last two builds
    /// bought — and is printed on this same line as 0.00 so the next round can price it.</para>
    ///
    /// <para>Stated in the user's own terms: the effective resolution delivered, as a percentage of
    /// what the window was authored at, and the authored text height that survives it. 8 authored px
    /// is roughly the smallest stroke this game's UI font draws a legible glyph with; below that a
    /// stroke lands on less than one eye pixel and NOTHING downstream can put it back.</para>
    /// </summary>
    private static string LegibilitySentence(Entry e, float authoredPerPixel)
    {
        if (authoredPerPixel <= 1f)
            return "LEGIBILITY: the window is MAGNIFIED (every authored pixel covers "
                   + $"{1f / Mathf.Max(authoredPerPixel, 1e-4f):F2} rendered eye px), so the eye is "
                   + "given the window's full authored resolution and legibility is not sampling-bound "
                   + "here — this is the END of the distribution, not its operating point; read the "
                   + "HIGHEST figure below for the worst case.";
        float delivered = 100f / authoredPerPixel;
        float smallestLegible = 8f * authoredPerPixel;
        return $"LEGIBILITY: the window is MINIFIED {authoredPerPixel:F2}x, so the eye receives "
               + $"{delivered:F0} % of the authored resolution (peak minification since engage "
               + $"{e.AuthoredPerRenderedPxMax:F2}x = {100f / Mathf.Max(e.AuthoredPerRenderedPxMax, 1e-4f):F0} %, "
               + $"and {e.MinifiedReadings} of {e.SamplingMeasured} completed measurement(s) were "
               + "minified at all), against a threshold of 1.00 authored px per rendered px (the rate "
               + "at which the eye is given every pixel the window drew). AT THIS RATE THE SMALLEST "
               + $"LEGIBLE AUTHORED TEXT IS ~{smallestLegible:F0} px tall, because an 8-authored-px "
               + "stroke — about the floor for this game's UI font — arrives as "
               + $"{8f / authoredPerPixel:F1} eye px. THIS IS NOT A FILTERING FAULT AND NO CAPTURE "
               + "FACTOR CAN REACH IT: the factor buys RT texels per AUTHORED pixel, and a minified "
               + "window's sampler already selects a mip level at or below authored resolution, so "
               + "every level above it is one the hardware never reads. The levers are the window's "
               + "size in the eye and a negative mipMapBias (printed above, currently 0.00);";
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
