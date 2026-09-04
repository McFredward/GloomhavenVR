using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 11 — THE PRE-CONVERT 2D BLACKOUT. New members only, appended after the
// existing parts in the filename sort (part 10 already sits between 1 and 2; the numbering is
// the split, see part 1's header). Nothing here participates in another part's static
// initializer order: the two collections are plain `new()` with no cross-references.

internal static partial class ModalFallback
{
    // ---- ROUND 8: THE LAST FRAME THE GAME'S 2D WINDOW WAS STILL VISIBLE ---------------------
    //
    // THE SYMPTOM: "das kurze Flackern am LINKEN RAND des LINKEN AUGES" when the pause menu
    // opens — a brief bright band at the extreme left of the LEFT eye, and nowhere else.
    //
    // THE MECHANISM (hardware-observed, not inferred — see the two citations below):
    //
    //  * The rig head camera renders EVERY layer. ModBuild 24's own ITEM9 inventory prints
    //    `'GloomhavenVR.HeadCamera' … mask=0xFFFFFFFF stereo=Both target=backbuffer`, so the
    //    game's UI layer is in the HMD's culling mask — anything the game's UI draws is drawn
    //    into the headset too, not only into the game's (scrubbed, blinded) 'UI Camera'.
    //  * An ENABLED 2D ESC menu therefore SHOWS IN THE HMD, and it shows at the left edge:
    //    <see cref="CanvasConversion.Release"/>'s FIX B records exactly that hardware finding —
    //    "the ESC menu's width-hugged content column (~412/1920 px, docked LEFT) rendered as a
    //    gray band on the left edge of the view until the next float hid it again" — and
    //    <see cref="ConvertedPanel.KeepBackgroundHidden"/> names the same artifact "a left-edge
    //    stereo-split flicker". ModBuild 24's fit log confirms the geometry: the menu's own
    //    'UI Menu Panel' is 368x1080 px anchored at x = -960 of a 1920-wide canvas, i.e. hard
    //    against the LEFT edge. Being far off-axis to the left puts it in the left eye's
    //    MONOCULAR region — the sliver the left eye sees and the right eye does not — which is
    //    why one eye reports it and the other does not.
    //  * FIX B closed the CLOSE side (a released window parked visible at its 2D home). The
    //    OPEN side was never closed, and it is structurally guaranteed to be at least one
    //    rendered frame long:
    //        Update, frame N : ModalFallback.Tick runs …
    //                          … then OptionsToggle.Tick runs (driver order, WorldUIModule) and
    //                            OPENS the ESC menu — the game's window is now live, enabled,
    //                            screen-space, on the game UI layer.
    //        RENDER, frame N : the head camera draws it. ← THE FLICKER
    //        Update, frame N+1: ModalFallback.Tick converts it (mod layer 27 + world space) and
    //                            CanvasConversion's reveal gate render-hides it.
    //    ModBuild 24's log shows exactly this ordering: "[OptionsToggle] X tap … -> OPEN",
    //    "MODAL FALLBACK: window 'UI Scenario Esc Menu' … opened", "OPTIONS TAP: pause menu
    //    OPENED" all in frame N, with "Adopted nested canvas …" / "MODAL LAYER: moved 197
    //    transform(s) …" / "Converted 'Modal_UI Scenario Esc Menu' to world space" only in the
    //    next tick. The reveal gate cannot help here: it starts at Convert, one frame too late.
    //
    // THE FIX: kill that frame. <see cref="UIWindow_Transition_Patch"/> raises
    // <see cref="VREvents.WindowVisibility"/> from a POSTFIX on the game's single visibility
    // choke point, i.e. SYNCHRONOUSLY inside the game's own Show(), on the main thread, before
    // anything renders. Switching the window's own Canvas(es) off right there means the window
    // is never drawn in 2D at all — not in one eye, not in both — and the conversion picks it up
    // in the very next tick exactly as before.
    //
    // WHY IT IS SAFE, AND WHY IT CAN NEVER STRAND A WINDOW INVISIBLE:
    //  * It is the same lever the reveal gate already pulls (Canvas.enabled), on the same
    //    canvases, a single frame earlier — layout, tweens, game logic and input are untouched
    //    (a disabled Canvas still lays out; the fit path measures render-hidden hosts all day).
    //  * <see cref="TryConvertWindow"/> RESTORES the blackout as its first act, so the
    //    conversion's own complete render hide records the TRUE enabled state and the reveal
    //    re-enables exactly what it disabled (CanvasConversion part 6's "record instead of
    //    re-enable everything" contract). Hiding straight through the hand-off would make the
    //    reveal skip them and leave a permanently invisible menu — hence the explicit release.
    //  * <see cref="TickPreConvertHide"/> runs FIRST in <see cref="Tick"/> and force-restores on
    //    every other outcome: window closed, conversion failed, the flat-screen/screen-style
    //    path took over, Menu2D, VR stopped — plus, since ModBuild 419, the hand-back rule below:
    //    the blackout stands only while a NAMED, BOUNDED intent to float this window stands, and
    //    goes back the moment none does.
    //
    // ---- ROUND 2026-09-04: THE BUDGET WAS A FRAME COUNT WHERE THE QUESTION WAS A CONDITION ----
    //
    // THE SYMPTOM, verbatim: "Wenn man schnell hintereinander die Optionstaste drückt erscheint am
    // linken Rand des auges so ein Rand der dem Kopf folgt statt dem Optionsmenu. Gehe dem nach und
    // fix das. Ein schneller Drücken der Optionstaste auch hintereinander soll das nicht auslösen
    // sondern eben einfach das Optionsmeenu als Fenster öffnen/schließen." — rapid repeated presses
    // of the options key produced a head-locked rim at the LEFT EDGE of the eye instead of the
    // options menu. That rim is the artifact THIS FILE EXISTS TO PREVENT, and the ModBuild 418 log
    // measured it: 'MODAL CLOSE X ON THE INK' reports the ESC menu's FRAME at x -960..960 of a
    // 1920x1080 canvas and its INK at x -979..-591 — a 388 px strip hard against the left edge,
    // overhanging it — and screen space means head-locked.
    //
    // THE CHAIN, with both constants:
    //   * A close plays the dissolve; ModalFallback.4.Tick's convert loop refuses to float a window
    //     whose previous float is still dissolving (WindowMaterialise.IsVanishing), and that vanish
    //     runs 0.90 s ("VANISH … ended (completed) after 0.92s of 0.90s").
    //   * The blackout gave up after PreConvertHideMaxFrames = 8 frames, ~0.11 s at 72 Hz.
    //   * So a close followed at once by a re-open left the window un-floatable for ~0.9 s and
    //     un-blacked-out after ~0.11 s. It was never "one flat frame": the flat, head-locked canvas
    //     stayed up until the vanish finished or the player tapped again.
    //   * ModBuild 418 log :19849 is the single 'was still un-floated after 9 frames' line in the
    //     whole session, and it sits inside the rapid-press burst. The other 24 opens handed back
    //     after 0 or 1 frames.
    //
    // THE TWO FIXES, and they are deliberately BOTH kept (either one alone would mask the other):
    //   1. ModalFallback.4.Tick's convert loop now ENDS the vanish on a re-open
    //      (WindowMaterialise.EndVanishNow via ResolveVanishForReopen below) instead of skipping the
    //      window for half a second. The gap collapses from ~0.9 s to nothing.
    //   2. This file no longer counts frames. A FRAME COUNT CANNOT TELL "the conversion is stuck"
    //      FROM "the conversion is deliberately waiting for a bounded thing" — that is the whole
    //      defect. The budget's PRINCIPLE ("a window must never stay invisible") is right and is
    //      kept; its INSTRUMENT is replaced by PreConvertHideHold, which names the standing intent
    //      and its bound, and hands the 2D rendering back the moment no such intent stands.
    //
    // AND THE NUMBER WAS NOT SIMPLY RAISED. Standing ruling from the user: "Ich will gar keine
    // Zeitlimits dieser Art." The one outer safety left, PreConvertHideDefectSeconds, is set far
    // above every intent's own bound precisely so it can never fire before them, and firing it logs
    // as a DEFECT naming which intent overran — never as routine.
    //  * It never runs in Menu2D: there the head camera renders the mod layer ONLY and the flat
    //    screen composites the game's 2D UI, so there is nothing to hide and hiding would blink
    //    the flat screen. The artifact is a SCENARIO one by construction.
    //
    // MULTIPLAYER: local rendering only. Two Canvas.enabled writes on the local player's own UI,
    // no game state, no wire traffic.

    /// <summary>
    /// <b>THE OUTER SAFETY, AND IT IS A DEFECT DETECTOR RATHER THAN A BUDGET.</b> A window must
    /// never stay invisible — that ruling is unchanged, and it is what
    /// <see cref="PreConvertHideHold"/> enforces by holding the blackout ONLY while a named,
    /// bounded intent stands. This number exists for the case where one of those intents overruns
    /// its own bound, i.e. a bug somewhere else.
    ///
    /// <para><b>WHY 5 s AND NOT SOMETHING TIGHTER.</b> It has to be unreachable by every honoured
    /// intent, or it becomes the budget again and re-creates the 2026-09-04 defect one number
    /// higher. The largest honoured bound is the dissolve's, which is
    /// <see cref="WindowMaterialise.HardCeilingSeconds"/> = 2.0 s plus
    /// <see cref="WindowMaterialise.WatchdogSlackSeconds"/> = 0.5 s of watchdog slack = 2.5 s; the
    /// story bridge is 2 ticks; the first-pass grace is 1 tick. 5 s is twice the largest of them.
    /// If this ever fires, the log line says WHICH intent overran, and that is the bug to fix —
    /// not this number.</para>
    /// </summary>
    private const float PreConvertHideDefectSeconds = 5f;

    /// <summary>
    /// How many <see cref="TickPreConvertHide"/> passes ago a convert-loop refusal still counts as
    /// current. ONE: <see cref="TickPreConvertHide"/> runs FIRST in <see cref="Tick"/> and the
    /// convert loop runs LATER IN THE SAME TICK, so the freshest refusal a pass can ever read was
    /// stamped on the previous pass. Anything older is a stamp nobody renewed, which is exactly the
    /// state in which the blackout must go back.
    /// </summary>
    private const int PreConvertHideStampTicks = 1;

    /// <summary>
    /// Monotonic count of <see cref="TickPreConvertHide"/> passes. The only clock the stamps below
    /// are compared against — deliberately NOT <c>Time.frameCount</c>, because the convert loop and
    /// this method share a tick and not a frame, and a rule that assumed one tick per frame would be
    /// wrong the first time <see cref="Tick"/> is skipped.
    /// </summary>
    private static int s_preConvertTick;

    /// <summary>Session tally of blackouts that hit <see cref="PreConvertHideDefectSeconds"/>.
    /// Bounded by window opens (one line per entry at most, and the entry is dropped in the same
    /// pass); printed so a hardware log says whether this happened once or kept happening.</summary>
    private static int s_preConvertDefects;

    /// <summary>Session tally of re-opens that cut a dissolve short — see
    /// <see cref="NoteReopenDuringVanish"/>.</summary>
    private static int s_reopenDuringVanish;

    /// <summary>One game window whose 2D rendering is suppressed until the mod converts it.</summary>
    private sealed class PreHiddenWindow
    {
        public UIWindow Window = null!;
        public string Name = "<window>";
        /// <summary>Canvases WE switched off (they were enabled) — restored one for one.</summary>
        public readonly List<Canvas> Canvases = new(4);
        /// <summary>Fallback lever for a window with no Canvas of its own (UIWindow requires a CanvasGroup).</summary>
        public CanvasGroup? Group;
        public float GroupAlpha;
        public int HiddenAtFrame;
        /// <summary>Unscaled time the blackout was raised — the only term
        /// <see cref="PreConvertHideDefectSeconds"/> is measured against.</summary>
        public float HiddenAtTime;
        /// <summary><see cref="TickPreConvertHide"/> passes this entry has survived. The convert
        /// loop's first pass is the one the conversion normally lands on, and this counts it.</summary>
        public int Passes;
        /// <summary>The pass on which the convert loop last reported that
        /// <c>StoryComposite.HoldsBack</c> refused this window. Stamped by the convert loop with the
        /// answer it already had — this file never asks StoryComposite itself, so that subsystem's
        /// own hold tally still counts exactly one consultation per tick.</summary>
        public int StoryHeldTick;
        /// <summary>The last intent that held the blackout, for the hand-back line. Null once
        /// nothing stands.</summary>
        public string? LastHold;
        /// <summary>ModBuild 420: this blackout was raised by <see cref="TickFloatIntentGuard"/>
        /// rather than by the game's Show(), i.e. the invariant found a live canvas on a window
        /// the mod still intends to float. Such an entry is stood down when it is handed back, so
        /// a guard that guessed wrong costs ONE rendered frame and can never strobe.</summary>
        public bool RaisedByLateGuard;
        /// <summary>ModBuild 420: the first invariant violation on this window has been printed.
        /// One line per window per blackout — the FIRST one is the diagnostic; a per-frame repeat
        /// would be the flood ModBuild 331 removed.</summary>
        public bool ViolationLogged;
    }

    private static readonly List<PreHiddenWindow> PreHidden = new(2);

    /// <summary>Sweep scratch (single-threaded; reused, no per-frame allocation).</summary>
    private static readonly List<Canvas> PreHideScratch = new(8);

    /// <summary>
    /// Last "who can draw this window" census line (change-gate). Steady state collapses to ONE
    /// line for the whole session; a new camera, a changed culling mask, a flat-screen state flip
    /// or a different stereo mode prints a fresh one. Never per frame — only on a window open.
    /// </summary>
    private static string s_preHideCensus = string.Empty;

    /// <summary>Census scratch (only used on a window open, never per frame).</summary>
    private static readonly System.Text.StringBuilder PreHideCensusSb = new(320);

    /// <summary>
    /// Suppress a just-opened game window's 2D rendering until the mod floats it (file header).
    /// Called from <see cref="OnWindow"/>, i.e. from the game's own Show() before this frame
    /// renders — that is the whole point: one tick later is already one visible frame too late.
    /// Idempotent: a second call only catches canvases that became enabled since the first.
    /// </summary>
    private static void PreConvertHide(UIWindow window)
    {
        if (window == null || !VRSession.IsRunning || !WorldUIConfig.ConversionActive
            || !WorldUIConfig.ModalWindowStyle || FlatScreen.ManualScreenActive)
            return;
        // Menu2D: the flat screen composites the game's 2D UI and the head camera renders the mod
        // layer only — nothing to hide, and hiding would blink the screen.
        if (VRModeStateMachine.CurrentMode == VRMode.Menu2D)
            return;
        // A dock-claimed window is not floated (its rows are re-hosted on the control board); the
        // generic path never converts it, so blacking it out would only cost it a frame.
        if (DecisionDock.ClaimsWindow(window))
            return;
        // ALREADY FLOATED — do not touch it. A STICKY menu the game re-Shows (its single-window
        // toggle, or ESCMenu.OnControllerAreaFocused) fires this transition while its subtree
        // lives inside a REVEALED mod host: switching those canvases off would blink the visible
        // window out for a frame, which is the exact artifact this guard exists to prevent.
        if (IsConverted(window))
            return;
        // ModBuild 232: a window the refusal table refuses is NEVER converted, so a blackout started
        // for it could only ever end on the outer safety with the "was still un-floated after N
        // frames — handing its 2D rendering back regardless" warning (before ModBuild 419, on the
        // 8-frame budget that warning used to carry). The
        // catch-all releases it explicitly (WithdrawRefusedFloat), but not starting it is cheaper and
        // cannot be forgotten. Inert for the two rows that exist today, whose ids are None: OnWindow
        // returns before this method unless IsFallbackWindow(e.Id).
        if (FloatRefusalTable.Refuses(window))
            return;
        // A SUB-VIEW OF AN ALREADY-FLOATED SCREEN IS NEVER CONVERTED, SO IT MUST NEVER BE BLACKED
        // OUT (ModBuild 196 — the equipment tab). This blackout buys exactly one thing: the frame
        // between the game's Show() and the mod's conversion, during which a screen-space window
        // would be drawn flat into the HMD. A window that renders inside a world-space host
        // (RendersInsideFloatedAncestor) has no such frame — its subtree is already parented under
        // the host, on the mod layer, so there is nothing flat to suppress. Blacking it out would
        // instead make it invisible until the PreConvertHideMaxFrames budget expired and then log a
        // warning about a window that was never meant to float.
        //
        // ModBuild 226: that predicate now answers for TWO shapes of group — the hierarchy one this
        // paragraph describes, and the DECLARED sibling one (the multiplayer "Quest wählen" confirm,
        // which MapTravelConfirm parks into the quest window). The declared member is NOT yet under
        // the host on the frame its transition fires — the park is level-triggered from the map
        // room's own tick — so strictly it does have the flat frame this blackout is for. It is
        // deliberately skipped anyway: the strip lives on 'Campaign Canvas'
        // (ScreenSpaceCamera on 'UI Camera', which the room does not render), so there is nothing to
        // suppress in practice, and blacking it out would switch off a canvas that NO conversion is
        // ever going to hand back — the budget would expire, the window would be handed back with a
        // warning, and for those frames the button would have been invisible INSIDE the quest window
        // it had meanwhile been parked into. An unnecessary blackout of an unreachable flat frame is
        // the cheaper mistake.
        if (RendersInsideFloatedAncestor(window))
            return;

        PreHiddenWindow? entry = FindPreHidden(window);
        bool fresh = entry == null;
        if (entry == null)
        {
            entry = new PreHiddenWindow
            {
                Window = window,
                Name = window.name,
                HiddenAtFrame = Time.frameCount,
                HiddenAtTime = Time.unscaledTime,
            };
            PreHidden.Add(entry);
        }

        int switched = 0;
        window.transform.GetComponentsInChildren(false, PreHideScratch);
        for (int i = 0; i < PreHideScratch.Count; i++)
        {
            Canvas c = PreHideScratch[i];
            if (c == null || !c.enabled)
                continue; // already off (by us, or by the game on purpose)
            c.enabled = false;
            entry.Canvases.Add(c);
            switched++;
        }

        // Fallback for a window that renders through an ANCESTOR canvas only (no Canvas of its
        // own): UIWindow is [RequireComponent(typeof(CanvasGroup))], so the group always exists.
        // Best-effort — the game's show tween writes the same alpha and may win the frame — which
        // is why the log below states which lever actually bit.
        if (entry.Canvases.Count == 0 && entry.Group == null)
        {
            CanvasGroup? group = window.GetComponent<CanvasGroup>();
            if (group != null)
            {
                entry.Group = group;
                entry.GroupAlpha = group.alpha;
                group.alpha = 0f;
            }
        }

        if (!fresh)
            return;
        VRLog.Info("WorldUI", $"MODAL PRE-CONVERT BLACKOUT: '{entry.Name}' (ID {window.ID}) switched off " +
                              $"{switched} own canvas(es)" +
                              (entry.Canvases.Count == 0
                                  ? entry.Group != null
                                      ? " — NONE found, fell back to its CanvasGroup alpha (the game's show " +
                                        "tween may overwrite it: if a left-edge one-eye flash survives for THIS " +
                                        "window, it needs its parent canvas handled instead)"
                                      : " — NONE found and no CanvasGroup: this window CAN still render one 2D " +
                                        "frame in the HMD before the conversion takes over"
                                  : "") +
                              $" at frame {Time.frameCount}, before the frame rendered. The game shows a window " +
                              "from its own Update, the conversion runs in the NEXT tick, and the head camera " +
                              "renders the game UI layer — so without this the 2D window is drawn for one frame " +
                              "at its screen-space home (the ESC menu's column is docked hard LEFT, i.e. in the " +
                              "left eye's monocular sliver). Restored the instant the conversion takes over.");
        LogPreHideCameraCensus(window);
    }

    /// <summary>
    /// CHANGE-GATED DIAGNOSTIC (evaluated only when a tracked window opens, never per frame):
    /// every enabled camera that renders the opening window's layer, with its render target,
    /// culling mask and stereo mode — plus the stereo environment and whether the flat screen
    /// (the mod's only per-eye render targets) is up at all.
    ///
    /// <para>This is THE decisive line for the next hardware log, and it discriminates between
    /// the two candidate mechanisms in one read:</para>
    /// <list type="bullet">
    /// <item><description>If <c>GloomhavenVR.HeadCamera</c> is listed with a BACKBUFFER target,
    /// the game's 2D window provably reaches the HMD — this blackout is the fix.</description></item>
    /// <item><description><c>flat screen HIDDEN</c> says the stereo screen's left/right RTs do
    /// not exist here, so "the two eyes have different render targets" cannot be the cause of a
    /// scenario-side artifact; a surviving flash would then have to come from geometry.</description></item>
    /// <item><description>The stereo mode + eye-texture size state whether a one-eye artifact is
    /// even possible the way MultiPass makes it possible (two independent eye targets, left
    /// rendered first).</description></item>
    /// </list>
    /// </summary>
    private static void LogPreHideCameraCensus(UIWindow window)
    {
        int layer = window.gameObject.layer;
        int layerBit = 1 << layer;
        PreHideCensusSb.Clear();
        PreHideCensusSb.Append("layer ").Append(layer).Append(':');
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        int drawers = 0;
        Camera? capture = null;
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || !cam.enabled || (cam.cullingMask & layerBit) == 0)
                continue;
            // A LIVE MOD CAPTURE CAMERA ON THE OPENING WINDOW'S LAYER IS THE ANOMALY THIS ROUND
            // FOUND — see NoteStaleCaptureLayer below for the whole account.
            if (capture == null && cam.name.StartsWith("GloomhavenVR.PanelSSCam", System.StringComparison.Ordinal))
                capture = cam;
            drawers++;
            string target = cam.targetTexture != null ? cam.targetTexture.name : string.Empty;
            if (string.IsNullOrEmpty(target))
                target = "BACKBUFFER (the HMD eye textures)";
            PreHideCensusSb.Append(" [").Append(cam.name).Append(" depth=").Append(cam.depth.ToString("F0"))
                .Append(" stereo=").Append(cam.stereoTargetEye)
                .Append(" mask=0x").Append(cam.cullingMask.ToString("X8"))
                .Append(" →").Append(target).Append(']');
        }
        if (drawers == 0)
            PreHideCensusSb.Append(" (none — nothing could have drawn it)");
        PreHideCensusSb.Append(" | flat screen ").Append(FlatScreen.ScreenVisible ? "SHOWN" : "HIDDEN")
            .Append(" | stereo mode=").Append(UnityEngine.XR.XRSettings.stereoRenderingMode)
            .Append(" eye texture ").Append(UnityEngine.XR.XRSettings.eyeTextureWidth).Append('x')
            .Append(UnityEngine.XR.XRSettings.eyeTextureHeight)
            .Append(" XR ").Append(UnityEngine.XR.XRSettings.enabled ? "on" : "off");
        NoteStaleCaptureLayer(window, layer, capture);
        string census = PreHideCensusSb.ToString();
        if (census == s_preHideCensus)
            return;
        s_preHideCensus = census;
        VRLog.Info("WorldUI", $"MODAL PRE-CONVERT BLACKOUT census (changed): {census}. A camera listed with a " +
                              "BACKBUFFER target draws the game's 2D UI straight into the HMD eye textures — " +
                              "that is the left-edge one-eye flash this blackout removes. 'flat screen HIDDEN' " +
                              "means the stereo screen's per-eye RTs do not exist here, so the two eyes share " +
                              "one render path and a per-eye RT divergence cannot explain a flash at this site." +
                              // APPENDED 2026-09-04 round 2, never reworded. THE 'mask=0xFFFFFFFF'
                              // FIELD ABOVE IS READ ONE STAGE TOO EARLY TO ANSWER THE QUESTION IT IS
                              // BEING ASKED. PanelSupersample strips its capture-pool layers from
                              // every foreign camera in OnPreCull — after every Update and
                              // LateUpdate, i.e. after this census runs — so for a POOL layer the
                              // head camera's Update-time mask always reads 0xFFFFFFFF whether or
                              // not that layer is drawn into the eye. The sentence above is correct
                              // for the game's own UI layer 5 and OVER-CLAIMS for a pool layer.
                              " CAVEAT (ModBuild 420): the culling masks above are read in Update. " +
                              "PanelSupersample strips its capture-pool layers from foreign cameras " +
                              "in OnPreCull, which runs AFTER every Update and LateUpdate — so for a " +
                              "layer in that pool this mask is measured one stage too early and " +
                              "cannot say whether the eye drew it. It is only decisive for the " +
                              "game's own UI layer.");
    }

    /// <summary>Session tally of opens that landed on a live capture layer — see below.</summary>
    private static int s_staleCaptureOpens;

    /// <summary>
    /// <b>THE GAME RE-OPENED THIS WINDOW WHILE THE MOD'S SUPERSAMPLE FOR ITS PREVIOUS FLOAT WAS
    /// STILL ENGAGED.</b> Measured, not inferred: in the ModBuild 420 hardware log the census
    /// changes seven times across a 42-cycle options-key burst, and THREE of those reads put the ESC
    /// menu on layer 26 — a PanelSupersample capture layer — with
    /// <c>GloomhavenVR.PanelSSCam_UI Scenario Esc Menu</c> listed alive beside it. The interleaving
    /// is unambiguous in the line numbers:
    ///
    /// <code>
    ///   7214  PANEL SUPERSAMPLE engaged on 'UI Scenario Esc Menu'
    ///   7252  BLACKOUT census: layer 26   &lt;- the game Show()s the window again, HERE
    ///   7260  PANEL SUPERSAMPLE stood down on 'UI Scenario Esc Menu'
    ///   7318  BLACKOUT census: layer 5    &lt;- the next open reads normal again
    /// </code>
    ///
    /// <para>The re-open lands BETWEEN the engage and the stand-down, three times, at 7252, 7878 and
    /// 6462. That is the ordering nothing in this mod had a line for.</para>
    ///
    /// <para><b>WHY THIS IS THE LEAD AND THE FLOAT-INTENT INVARIANT IS NOT.</b> That invariant
    /// returned ZERO violations over the same burst — no game canvas of a tracked window was ever
    /// enabled while un-floated — so the rim is something the invariant cannot see BY CONSTRUCTION.
    /// A capture layer is exactly that: it is outside the game's own 'UI Camera' mask
    /// (<c>0x00000020</c>, layer 5 alone, and it renders to a scrub sink rather than to the eye) and
    /// inside the head camera's, so anything stranded on it is INVISIBLE ON THE DESKTOP AND VISIBLE
    /// IN THE HEADSET — which is the shape of a symptom only the VR user can report. It is also
    /// outside the subtree this file walks whenever the stranded object is a late-arriving child
    /// rather than the window root ("1 pooled/late transform(s) … joined capture layer 26", four
    /// times in the same burst).</para>
    ///
    /// <para><b>WHAT THIS LINE DOES AND DOES NOT CLAIM.</b> It reports an ORDERING, not a fault: a
    /// window opening on a live capture layer is not by itself wrong, because PanelSupersample's
    /// OnPreCull strips that layer from the head camera while the entry lives. What it names is the
    /// window in which a lost or late layer restore would become visible — the moment the entry is
    /// stood down, the strip lifts, and anything still on that layer is drawn into the eye with no
    /// second camera left to hide it. PanelSupersample is not this lane's to edit; this line is the
    /// evidence that says whether it should be.</para>
    ///
    /// <para>BOUNDED: fires on a window OPEN, never per frame, and capped at the first eight plus one
    /// per doubling with the running total on every line.</para>
    /// </summary>
    private static void NoteStaleCaptureLayer(UIWindow window, int layer, Camera? capture)
    {
        if (capture == null)
            return;
        s_staleCaptureOpens++;
        if (s_staleCaptureOpens > 8
            && (s_staleCaptureOpens & (s_staleCaptureOpens - 1)) != 0)
            return;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        // HW-VERIFY
        VRLog.Alert("WorldUI", $"MODAL OPEN ON A LIVE CAPTURE LAYER ({s_staleCaptureOpens} this " +
            $"session): the game re-opened '{window.name}' (ID {window.ID}) while it still sits on " +
            $"layer {layer}, and the mod's own capture camera '{capture.name}' is ALIVE on that " +
            $"layer, rendering into " +
            $"{(capture.targetTexture != null ? capture.targetTexture.name : "the BACKBUFFER")}. " +
            "That means this window's PREVIOUS float is still supersampled at the moment its NEXT " +
            "open begins: the engage, this open and the stand-down interleave in that order, which " +
            "the ModBuild 420 log shows three times in one 42-cycle options-key burst (log lines " +
            "6462, 7252, 7878 read layer 26 while 6471, 7260 and 7886 stand the capture down " +
            "afterwards). A capture layer is outside the game's 'UI Camera' mask (0x00000020) and " +
            "inside the head camera's, so anything left on it is INVISIBLE ON THE DESKTOP AND " +
            "VISIBLE IN THE HEADSET — the shape of the user's report. IT IS THE STAND-DOWN THAT " +
            "DECIDES: while the entry lives PanelSupersample strips this layer from foreign cameras " +
            "in OnPreCull, and the moment it is stood down that strip lifts. THE QUESTION FOR THE " +
            "NEXT ROUND: does the stand-down restore the layer of EVERYTHING it moved, including " +
            "the 'pooled/late transform(s) … joined capture layer' arrivals, when the window has " +
            $"already re-opened underneath it? Head camera '{(head != null ? head.name : "<none>")}' " +
            $"Update-time mask 0x{(head != null ? head.cullingMask : 0):X8} — NOT decisive on its " +
            "own, because the OnPreCull strip runs after every Update. USER REPORT 2026-09-04: " +
            "'Wenn ich den button oft genug spamme, tritt das Problem immer noch auf.'");
    }

    /// <summary>
    /// Hand the window's 2D rendering back. Called as <see cref="TryConvertWindow"/>'s first act
    /// (so the conversion's own complete render hide records the TRUE enabled state and the
    /// reveal restores exactly what it disabled) and from <see cref="TickPreConvertHide"/> on
    /// every other outcome. Destroyed components are skipped; calling it twice is a no-op.
    /// </summary>
    private static void ReleasePreConvertHide(UIWindow? window, string reason)
    {
        PreHiddenWindow? entry = FindPreHidden(window);
        if (entry == null)
            return;
        RestorePreHidden(entry, reason);
        PreHidden.Remove(entry);
    }

    /// <summary>Release every blackout (module shutdown / VR off) — a window must never stay hidden.</summary>
    private static void ReleaseAllPreConvertHide(string reason)
    {
        for (int i = PreHidden.Count - 1; i >= 0; i--)
            RestorePreHidden(PreHidden[i], reason);
        PreHidden.Clear();
        // ModBuild 420: the float-intent invariant's stand-down latch is per SESSION-state too. A
        // shutdown / VR-off is a clean slate, and a window left latched across one would be a window
        // the guard has silently stopped watching for the rest of the process.
        PreHideGuardStoodDown.Clear();
    }

    private static void RestorePreHidden(PreHiddenWindow entry, string reason)
    {
        int restored = 0;
        for (int i = 0; i < entry.Canvases.Count; i++)
        {
            Canvas c = entry.Canvases[i];
            if (c == null || c.enabled)
                continue;
            c.enabled = true;
            restored++;
        }
        entry.Canvases.Clear();
        if (entry.Group != null)
        {
            entry.Group.alpha = entry.GroupAlpha;
            entry.Group = null;
        }
        // ModBuild 420: a blackout the float-intent invariant raised is STOOD DOWN the moment it is
        // handed back, so the guard cannot raise it again on the very next frame. Without this a
        // guard that guessed wrong is a 2-frames-off / 1-frame-on strobe — worse than the rim it
        // removes. The latch is per open-cycle: PruneGuardStandDown drops the window when it closes
        // or is floated, and NoteReleasedWhileOpen lifts it when the mod hands a still-open window
        // back to its 2D home, which is the one event that makes the question fresh again.
        if (entry.RaisedByLateGuard && entry.Window != null
            && !ContainsWindow(PreHideGuardStoodDown, entry.Window))
            PreHideGuardStoodDown.Add(entry.Window);
        VRLog.Debug("WorldUI", $"MODAL PRE-CONVERT BLACKOUT: '{entry.Name}' handed back after " +
                               $"{Time.frameCount - entry.HiddenAtFrame} frame(s) — {reason} " +
                               $"({restored} canvas(es) re-enabled)." +
                               // ModBuild 419: the seconds, and the last intent that stood. A frame
                               // count alone could not tell the 2026-09-04 defect (0.11 s of blackout
                               // against a 0.9 s dissolve) from a normal open; the seconds and the
                               // named intent can, and they are the two fields that decide the next
                               // hardware log.
                               $" It stood for {Time.unscaledTime - entry.HiddenAtTime:F3}s over " +
                               $"{entry.Passes} convert pass(es); the last bounded intent that held " +
                               $"it was {(entry.LastHold == null ? "NONE" : "'" + entry.LastHold + "'")}.");
    }

    private static PreHiddenWindow? FindPreHidden(UIWindow? window)
    {
        if (window == null)
            return null;
        for (int i = 0; i < PreHidden.Count; i++)
        {
            if (ReferenceEquals(PreHidden[i].Window, window))
                return PreHidden[i];
        }
        return null;
    }

    /// <summary>
    /// <b>THE NAMED, BOUNDED INTENTS THAT KEEP THE BLACKOUT STANDING</b> — the ModBuild 419
    /// replacement for the 8-frame budget. Returns the printable reason, or null when the mod is no
    /// longer waiting for anything and the game's 2D rendering must go back.
    ///
    /// <para>EVERY ENTRY HERE IS BOUNDED AND SAYS SO IN ITS OWN TEXT. That is the acceptance rule,
    /// not a nicety: a blackout that can be held open forever is a permanently invisible window,
    /// which is the failure the budget was defending against and is worse than the rim.</para>
    ///
    /// <para><b>WHAT IS DELIBERATELY NOT AN INTENT, AND WHY.</b> ModBuild 230's empty-window hold
    /// (<c>EmptyHeldNow</c>) is NOT honoured, although it too makes the convert loop skip the
    /// window. Two reasons, either sufficient. (a) It is not bounded: it lifts when the window's
    /// content comes back or when the window closes, and neither is a bound. (b) It does not need
    /// to be: a window that rule is holding is by definition drawing NOTHING, so its 2D home draws
    /// nothing either and handing the rendering back cannot show a rim. Honouring it would trade a
    /// bounded artifact for an unbounded invisible window. It is also asked at a 6 Hz cadence the
    /// convert loop owns, and a second caller here would eat that probe slot.</para>
    /// </summary>
    private static string? PreConvertHideHold(PreHiddenWindow entry, UIWindow window)
    {
        // (a) THE CONVERT LOOP HAS NOT HAD A PASS YET. This is what the frame budget was really
        //     buying on a normal open: the blackout is raised from the game's own Show(), and the
        //     conversion lands on the next convert pass. BOUNDED: exactly one pass, counted on this
        //     entry, never re-armed.
        if (entry.Passes < PreConvertHideStampTicks)
            return "the convert loop has not run a pass since the blackout was raised, and the "
                   + "conversion lands on that pass (BOUNDED: exactly one tick)";
        // (b) ITS PREVIOUS FLOAT IS STILL DISSOLVING. Re-floating inside that gap would record the
        //     DYING HOST as the window's original parent, so the convert loop refuses — see the
        //     IsVanishing clause in ModalFallback.4.Tick.cs. BOUNDED by the effect's own hard code
        //     ceiling plus its watchdog slack. Since ModBuild 419 the convert loop ENDS that vanish
        //     on a re-open rather than waiting it out, so this should now stand for at most the one
        //     pass between the blackout going up and the convert loop reaching the window — and if
        //     a log ever shows it standing longer, ResolveVanishForReopen refused, which is the bug.
        if (WindowMaterialise.IsVanishing(window.transform))
            return "its previous float is still dissolving, so the convert loop cannot record the "
                   + "window's true parent yet (BOUNDED: the dissolve's hard code ceiling "
                   + $"{WindowMaterialise.HardCeilingSeconds:F1}s + "
                   + $"{WindowMaterialise.WatchdogSlackSeconds:F1}s watchdog slack)";
        // (c) THE STORY CURTAIN'S CONVERT BRIDGE. BOUNDED: 2 ticks per gate, never re-armed, and
        //     bounded again by StoryComposite's own deadlock floor. Read from the stamp the convert
        //     loop leaves, never by asking StoryComposite a second time — HoldsBack increments that
        //     subsystem's own hold tally, and a diagnostic must not write another one's counters.
        if (entry.StoryHeldTick > 0 && s_preConvertTick - entry.StoryHeldTick <= PreConvertHideStampTicks)
            return "the story curtain's convert bridge holds it back (BOUNDED: 2 ticks per gate, "
                   + "never re-armed, plus StoryComposite's own deadlock floor)";
        return null;
    }

    /// <summary>
    /// The convert loop refused this window because <c>StoryComposite.HoldsBack</c> said so. Record
    /// the answer it ALREADY HAS on the standing blackout, so <see cref="PreConvertHideHold"/> can
    /// honour it next pass without asking StoryComposite again. No-op when no blackout stands, which
    /// is the usual case.
    /// </summary>
    private static void NoteStoryHold(UIWindow window)
    {
        PreHiddenWindow? entry = FindPreHidden(window);
        if (entry != null)
            entry.StoryHeldTick = s_preConvertTick;
    }

    /// <summary>
    /// <b>A RE-OPEN MUST NOT WAIT OUT THE DISSOLVE.</b> Called from the convert loop's
    /// <c>IsVanishing</c> clause: end the previous float's vanish NOW so the window is floatable on
    /// this very tick, instead of skipping it for the ~0.9 s the dissolve still owes.
    ///
    /// <para>USER REPORT 2026-09-04 (ModBuild 418, hardware), verbatim: <i>"Wenn man schnell
    /// hintereinander die Optionstaste drückt erscheint am linken Rand des auges so ein Rand der dem
    /// Kopf folgt statt dem Optionsmenu. Gehe dem nach und fix das. Ein schneller Drücken der
    /// Optionstaste auch hintereinander soll das nicht auslösen sondern eben einfach das
    /// Optionsmeenu als Fenster öffnen/schließen."</i></para>
    ///
    /// <para>Returns true when the window may be floated now. False leaves EVERYTHING as it was and
    /// the caller keeps its previous refusal — that is the whole failure mode, and it is a
    /// <c>continue</c>, not a fault.</para>
    /// </summary>
    private static bool ResolveVanishForReopen(UIWindow window)
    {
        if (window == null || !window.IsOpen)
            return false;
        // EndVanishNow goes through the same exit Cancel does: alphas restored, visibility hold
        // handed back, and THEN the pending onDone — which is CanvasConversion.Release(dying), the
        // call that re-parents the game's window back to its 2D home. Cancelling the ANIMATION never
        // cancels the RELEASE (WindowMaterialiseRunner.Finish's contract); this fix depends on that
        // invariant rather than fighting it, which is why re-adopting the dying host and playing the
        // appear back — nicer to look at — was NOT the shape taken: the caller has already removed
        // the window from every list, so a re-adopted host would have no owner left.
        if (!WindowMaterialise.EndVanishNow(window.transform,
                "the window was re-opened while its float was still dissolving",
                out float elapsed, out float total))
            return false;
        // THE RELEASE RAN INSIDE THAT CALL, so re-ask both questions about the window afterwards.
        // Release re-parents game hierarchy and destroys (or defers) the dying host, and Unity
        // activation callbacks can run inside a SetParent — so the window can in principle be gone,
        // or the game can have closed it, between the two statements. Returning false here leaves
        // the caller with its previous refusal, which is the safe answer for a window that no
        // longer wants floating.
        if (window == null || !window.IsOpen)
            return false;
        // Belt: if anything is STILL dissolving over this window, the hazard the refusal exists for
        // is still live and the old refusal is still the right answer.
        if (WindowMaterialise.IsVanishing(window.transform))
            return false;
        // THE RELEASE JUST RAN, AND IT RESTORED A VISIBLE 2D WINDOW. CanvasConversion.Release forces
        // the game's hidden state only for a window the game reports CLOSED (its FIX B); this one is
        // OPEN, so it is now sitting enabled at its screen-space home — which is the head-locked rim
        // itself. Re-assert the blackout over whatever the release re-enabled, in this same Update,
        // before anything renders. PreConvertHide is idempotent, keeps this entry's original
        // HiddenAtFrame, and TryConvertWindow releases it as its first act a few statements later.
        // (ModBuild 418's log shows the old code losing exactly this race: the expiry line reads
        // "(0 canvas(es) re-enabled)" because something else had already switched the canvas back
        // on under the standing blackout.)
        PreConvertHide(window);
        NoteReopenDuringVanish(window, elapsed, total);
        return true;
    }

    /// <summary>
    /// One line per re-open that cut a dissolve short: which window, how much of the vanish was
    /// still owed, and what was done about it.
    ///
    /// <para>BOUNDED BY THE PLAYER'S HAND — it fires on an EVENT (a re-open landing inside a vanish),
    /// and a vanish can only be ended once because the runner is gone afterwards, so a held key
    /// cannot make this a per-frame line. The tally below is belt against a future caller that is
    /// not the convert loop: the first eight are printed in full, then one per doubling, and every
    /// line carries the running total so a burst is still visible.</para>
    /// </summary>
    private static void NoteReopenDuringVanish(UIWindow window, float elapsed, float total)
    {
        s_reopenDuringVanish++;
        if (s_reopenDuringVanish > 8
            && (s_reopenDuringVanish & (s_reopenDuringVanish - 1)) != 0)
            return;
        float left = Mathf.Max(0f, total - elapsed);
        // HW-VERIFY
        VRLog.Note("WorldUI", $"MODAL RE-OPEN DURING VANISH ({s_reopenDuringVanish} this session): "
                              + $"'{window.name}' (ID {window.ID}) was re-opened {elapsed:F2}s into "
                              + $"the {total:F2}s dissolve of its PREVIOUS float, with {left:F2}s "
                              + "still to run. The dissolve was ENDED NOW and its pending release "
                              + "ran inside that call, so the window is floatable on this tick "
                              + "instead of being skipped for the rest of the dissolve. USER REPORT "
                              + "2026-09-04: 'Wenn man schnell hintereinander die Optionstaste "
                              + "drückt erscheint am linken Rand des auges so ein Rand der dem Kopf "
                              + "folgt statt dem Optionsmenu' — that rim was the game's own 2D ESC "
                              + "menu (ink x -979..-591 of a 1920 px canvas, i.e. hard left, "
                              + "screen-space and therefore head-locked) drawn because the blackout "
                              + "expired after 8 frames while this refusal ran for 0.9 s. IF THIS "
                              + "LINE IS PRESENT AND 'was still un-floated after N frames' IS NOT, "
                              + "the burst was handled."
                              // APPENDED 2026-09-04 round 2, never reworded (the sentence above is a
                              // grep token). THAT LAST TEST IS FALSIFIED AND MUST NOT BE TRUSTED: the
                              // ModBuild 419 hardware log satisfies it exactly and the user's verdict
                              // on that build was "Das Problem besteht weiterhin". A line proving that
                              // THIS mechanism ran proves nothing about the artifact.
                              //
                              // AND THIS TEXT DELIBERATELY SPELLS NO OTHER INSTRUMENT'S GREP TOKEN.
                              // The first draft of this correction quoted two of them verbatim, and
                              // both `grep -c` reads of the 420 log then returned 3 hits that were
                              // THIS SENTENCE rather than the instrument — a diagnostic that answers
                              // for itself. Name the instrument, never quote its token.
                              + " CORRECTION (ModBuild 420, from the 419 hardware log): THAT LAST TEST "
                              + "IS FALSIFIED. The 419 log satisfies it exactly and the user still "
                              + "reported the rim ('Das Problem besteht weiterhin'). Read the "
                              + "float-intent invariant's own line instead (ModalFallback.11"
                              + ".PreConvertHide.cs, NoteFloatIntentViolation): it reports the PICTURE "
                              + "— a live canvas on a tracked, un-floated window going into the render "
                              + "loop — rather than this mechanism's own bookkeeping. On ModBuild 420 "
                              + "it fired ZERO times over a 42-cycle burst, so the artifact is NOT a "
                              + "game canvas of a tracked window while un-floated.");
    }

    /// <summary>
    /// FIRST step of <see cref="Tick"/>: end every blackout whose window will not be converted
    /// this tick after all — closed, dead, already floated, conversion failed, screen style /
    /// manual screen / Menu2D / VR off — and, since ModBuild 419, every blackout for which no
    /// named bounded intent to float still stands (<see cref="PreConvertHideHold"/>). Nothing else
    /// in the mod can leave a game window switched off, and this runs before any step that could
    /// throw.
    ///
    /// <para>ModBuild 420: THIS IS STILL THE ONLY OWNER OF THE HAND-BACK, and that sentence is why
    /// <see cref="TickFloatIntentGuard"/> — the LateUpdate half of the same rule — only ever
    /// switches canvases OFF and never back on. The guard can raise a blackout this method has just
    /// handed back; that is the intended shape, and the stand-down latch in
    /// <see cref="RestorePreHidden"/> is what stops the two from strobing against each other.</para>
    /// </summary>
    private static void TickPreConvertHide()
    {
        // Advanced before the early return so the stamps below are compared against a clock that
        // moves whenever Tick runs, blackout or no blackout.
        s_preConvertTick++;
        if (PreHidden.Count == 0)
            return;
        bool globallyOff = !VRSession.IsRunning || !WorldUIConfig.ConversionActive
                           || !WorldUIConfig.ModalWindowStyle || FlatScreen.ManualScreenActive
                           || VRModeStateMachine.CurrentMode == VRMode.Menu2D;
        for (int i = PreHidden.Count - 1; i >= 0; i--)
        {
            PreHiddenWindow entry = PreHidden[i];
            UIWindow window = entry.Window;
            if (window == null)
            {
                PreHidden.RemoveAt(i); // window destroyed with its scene — its canvases died with it
                continue;
            }
            int age = Time.frameCount - entry.HiddenAtFrame;
            float stood = Time.unscaledTime - entry.HiddenAtTime;
            // The standing intent, evaluated ONCE and reused by both the hold decision and the log
            // line — a rule that measured one thing and printed another is how the 8-frame budget
            // survived for as long as it did.
            string? hold = globallyOff || !VRModeStateMachine.TableInFrontOfPlayer || !window.IsOpen
                           || IsConverted(window) || ContainsWindow(Failed, window)
                ? null
                : PreConvertHideHold(entry, window);
            bool defect = hold != null && stood > PreConvertHideDefectSeconds;
            string? reason =
                globallyOff ? "the floated-window path is no longer active (screen style / Menu2D / VR off)"
                // Floating requires a scenario board (Tick's `want`). A window that opens during the
                // loading transition — the scenario-start story box — will not be converted this
                // tick, so hand it back at once instead of sitting on the frame budget: quiet, and
                // exactly the pre-round-8 behaviour for that case.
                : !VRModeStateMachine.TableInFrontOfPlayer
                    ? "no room to float in yet — the floated-window path does not run here"
                : !window.IsOpen ? "the game closed it again before it was ever floated"
                : IsConverted(window) ? "it is already floated"
                : ContainsWindow(Failed, window) ? "its conversion failed — it belongs to the flat screen now"
                // THE ModBuild 419 RULE. No named bounded intent to float this window stands any
                // more, so the mod is not waiting for anything and the 2D rendering goes back. This
                // is the routine hand-back and it is not a warning.
                : hold == null
                    ? "no bounded intent to float it stands any more — nothing the mod is waiting " +
                      "for, so a window must never stay invisible"
                : defect
                    ? $"a bounded intent OVERRAN its own bound ({hold}) — a window must never stay " +
                      "invisible"
                : null;
            if (reason == null)
            {
                entry.Passes++;
                entry.LastHold = hold;
                continue;
            }
            if (defect)
            {
                s_preConvertDefects++;
                // HW-VERIFY
                VRLog.Alert("WorldUI", $"MODAL PRE-CONVERT BLACKOUT: '{entry.Name}' was still un-floated after " +
                                      $"{age} frames — handing its 2D rendering back regardless. It may show one " +
                                      "flat frame at its screen-space home (the artifact this guard exists to " +
                                      "prevent), but an invisible window is strictly worse." +
                                      // ModBuild 419 appendix — APPENDED, never reworded: the
                                      // sentence above is the grep token the 2026-09-04 round was
                                      // read with. What is new is that this line is now a DEFECT
                                      // report rather than a routine budget expiry.
                                      $" THIS IS A DEFECT AND NOT ROUTINE ({s_preConvertDefects} this session): " +
                                      $"the blackout stood {stood:F2}s over {entry.Passes} convert pass(es), past " +
                                      $"the {PreConvertHideDefectSeconds:F1}s outer safety. That safety is set far " +
                                      "above every honoured intent's own bound (the largest is the dissolve's " +
                                      $"{WindowMaterialise.HardCeilingSeconds:F1}s ceiling + " +
                                      $"{WindowMaterialise.WatchdogSlackSeconds:F1}s watchdog slack) so that it can " +
                                      "never fire before them, so reaching it means an intent overran. THE INTENT " +
                                      $"THAT OVERRAN: {hold}. Fix that, not this number — the 8-frame budget this " +
                                      "replaced was itself the 2026-09-04 defect, and raising a number is how it " +
                                      "would come back.");
            }
            RestorePreHidden(entry, reason);
            PreHidden.RemoveAt(i);
        }
    }
    // ==============================================================================================
    // ROUND 2026-09-04 #2 — THE INVARIANT. 419 FIXED TWO REAL THINGS AND THE ARTIFACT SURVIVED BOTH.
    // ==============================================================================================
    //
    // THE USER, ON ModBuild 419: "Das Problem besteht weiterhin." The head-locked rim at the left
    // edge of one eye is still there on a rapid options-key burst.
    //
    // ---- 419's HYPOTHESIS IS FALSIFIED, BY 419's OWN INSTRUMENTS. DO NOT RE-DERIVE IT. ----------
    //
    // 419 assumed the pre-convert blackout's 8-frame budget was expiring during a burst and handing
    // the game's flat canvas back mid-gap. In the ModBuild 419 hardware log
    // (.planning/debug/Player.log):
    //
    //   * 'was still un-floated after N frames' — ZERO occurrences. The budget's replacement never
    //     fired at all, so the mechanism 419 replaced was not running during the defect either.
    //   * 'handed back after 1 frame(s)' — 24, and NO OTHER COUNT appears in the whole session.
    //     Every single blackout ended the healthy way.
    //   * 'MODAL RE-OPEN DURING VANISH' — 3. The second 419 mechanism DID run and did cut live
    //     dissolves short (0.20-0.25 s in, 0.65-0.70 s still owed). It works. The artifact survives
    //     it.
    //   * The burst is real and large: 72 'OPTIONS TAP' lines between log lines 5713 and 7257,
    //     ~36 opens — and only 11 reveals (rect=412x1080 / 405x1080 with canvas.enabled=True).
    //     Roughly two thirds of the opens never reached a VR reveal at all.
    //   * No panel was ever revealed at the unfitted 1920x1080 rect; no HOST DESTROY ABANDONED; all
    //     24 deferred destroys resolved. Nothing downstream of the conversion is stuck either.
    //
    // So BOTH 419 mechanisms are correct and NEITHER is the cause. Both are kept.
    //
    // ---- WHAT NO INSTRUMENT IN THIS FILE COULD ANSWER, WHICH IS THE WHOLE JOB --------------------
    //
    // ON WHICH FRAMES is one of the game's own canvases ENABLED, and reachable by the head camera,
    // while its window is NOT floated? Every line this file prints reports the blackout's own
    // BOOKKEEPING — how many passes an entry survived, which intent held it, when it was handed
    // back. Not one of them reports THE PICTURE. Eight clean bookkeeping reads mean the defect is in
    // what no read covers ([[the-blind-spot-is-the-lead]], [[measure-the-picture-not-the-state]]).
    //
    // ---- THE RULE, STATED ONCE AND HELD EVERY FRAME ---------------------------------------------
    //
    //     A GAME WINDOW THAT THE MOD TRACKS AND INTENDS TO FLOAT MUST NEVER HAVE ITS OWN CANVASES
    //     ENABLED WHILE IT IS NOT FLOATED.
    //
    // <see cref="TickFloatIntentGuard"/> below is that rule, and the per-open blackout is now a
    // SPECIAL CASE of it rather than a second mechanism beside it: <see cref="PreConvertHide"/> is
    // the one place that decides whether the mod intends to float a window and the one place that
    // switches its canvases off, <see cref="PreHidden"/> is the one ledger, and
    // <see cref="TickPreConvertHide"/> is the one owner of the hand-back. The guard adds no third
    // lever — it re-asks the SAME question at the LAST moment before the frame renders.
    //
    // WHY LateUpdate AND NOT THE TICK. ModalFallback.Tick runs in Update, and so do the writers that
    // break the invariant: OptionsToggle opens the window AFTER ModalFallback in the same Update
    // pass, CanvasConversion.Release re-parents a released window back to its 2D home from
    // PhaseRelease, and WindowMaterialise hands a held Canvas back from inside a vanish's onDone.
    // Unity runs every Update before every LateUpdate and every LateUpdate before the render loop,
    // so LateUpdate is the last phase in which a canvas that WOULD be drawn can still be switched
    // off — the same argument CanvasConversion's own LateTick render-hide makes, and the reason the
    // guard is called from there.
    //
    // WHY IT CANNOT BECOME A SECOND WRITER OF THE GAME'S SWITCH ([[dont-win-a-write-war]],
    // [[a-remedy-knows-one-writer]]):
    //   * It only ever acts through <see cref="PreConvertHide"/>, which applies EVERY gate the
    //     blackout already applies (VR running, conversion active, window style, no manual screen,
    //     not Menu2D, not dock-claimed, not refused, not a sub-view of a floated ancestor, not
    //     already floated). A window outside those gates is never touched.
    //   * Everything it switches off is recorded in the SAME ledger, so the restore stays
    //     one-for-one and the conversion's own render hide still records the TRUE enabled state.
    //   * It NEVER restores. The hand-back has exactly one owner, <see cref="TickPreConvertHide"/>,
    //     which runs FIRST in the next Update — so a blackout the guard raised in error costs ONE
    //     rendered frame and is then handed back with a named reason.
    //   * A blackout the guard raised is STOOD DOWN when it is handed back
    //     (<see cref="PreHideGuardStoodDown"/>), so the guard can never re-raise it on the next
    //     frame. Without that latch a wrongly-raised blackout is a 2-frames-off / 1-frame-on strobe,
    //     which is worse than the rim it removes. The stand-down is lifted by the two events that
    //     make the question fresh again: the window closing or being floated (pruned below), and
    //     CanvasConversion releasing it back to its 2D home while it is STILL OPEN
    //     (<see cref="NoteReleasedWhileOpen"/>).
    //
    // WHAT HAPPENED TO THE OLD FRAME-BASED PATH: there is none left to remove. ModBuild 419 already
    // deleted PreConvertHideMaxFrames; the only frame number this file still keeps is
    // <c>HiddenAtFrame</c>, and it is printed, never compared. The outer safety is
    // <see cref="PreConvertHideDefectSeconds"/>, which is a defect detector and stays.
    //
    // ---- THE HEAD CAMERA'S REACH, MEASURED — AND THE ONE-EYE READING, NOT ASSUMED ---------------
    //
    // THE MASK IS NOT AN ASSUMPTION. The ModBuild 419 log prints seven
    // 'MODAL PRE-CONVERT BLACKOUT census (changed)' lines across the burst and every one of them
    // reads `[GloomhavenVR.HeadCamera depth=0 stereo=Both mask=0xFFFFFFFF →BACKBUFFER (the HMD eye
    // textures)]`. The head camera renders EVERY layer, straight into the eye textures, at the exact
    // moments the windows opened. Two further measured facts from the same lines:
    //
    //   * THE WINDOW'S OWN LAYER ALTERNATES BETWEEN 5 AND 26 ACROSS CONSECUTIVE OPENS IN THE BURST.
    //     26 is the mod's PanelSupersample capture layer. A window sitting at its screen-space 2D
    //     home on layer 26 is OUTSIDE the game's own 'UI Camera' mask (0x00000020, layer 5 alone,
    //     and it renders to GloomhavenVR.DesktopScrubSink, not to the eye) and INSIDE the head
    //     camera's. The log shows how it gets there: 'PANEL SUPERSAMPLE: 1 pooled/late transform(s)
    //     of "UI Scenario Esc Menu" joined capture layer 26' fires AFTER the release that already
    //     stood the supersample down and put the layer back. That is a stale mod layer left on a
    //     GAME window at its 2D home. The layer restore lives in PanelSupersample, which this lane
    //     does not own — so this round MEASURES it (the violation line prints the layer and whether
    //     the head camera's mask contains it) and does not fix it.
    //   * 'flat screen HIDDEN' on every census: the mod's per-eye render targets do not exist at
    //     this site, so a per-eye RT divergence cannot be the source of a one-eye artifact here.
    //
    // THE ONE-EYE READING IS CONSISTENT WITH ONE renderMode AND INCONSISTENT WITH THE OTHER, AND
    // THAT FIELD IS THE ONE NOTHING HAS EVER PRINTED. XRSettings reports `MultiPass`, eye texture
    // 3072x3264, i.e. the left eye and the right eye are two full independent passes:
    //
    //   * RenderMode.ScreenSpaceCamera / WorldSpace — drawn BY a camera. The head camera runs both
    //     eye passes, so a canvas it draws appears in BOTH eyes. This does NOT explain
    //     "am linken Rand des AUGES" (singular), and the geometric explanation the round-8 header
    //     above offers for it — the left monocular sliver — requires the strip to sit at the very
    //     edge of the binocular overlap, which is an argument about where the ink is, not about
    //     which pass drew it.
    //   * RenderMode.ScreenSpaceOverlay — drawn by NO camera. Unity composites an overlay canvas
    //     once per FRAME onto the target display, after the camera loop, not once per eye pass.
    //     Under MultiPass that single composite lands in whichever eye texture is bound at that
    //     moment, i.e. in ONE eye. That reads exactly as the user's report and as the census's own
    //     "one-eye flash" sentence.
    //
    // So: the mask question is ANSWERED (0xFFFFFFFF, measured, seven times). The one-eye question is
    // NOT yet answered, it is now DECIDABLE, and it is decided by a single field — the offending
    // canvas's renderMode — which the violation line below prints together with its worldCamera. If
    // the next log's violation line says ScreenSpaceOverlay, the overlay composite is the mechanism
    // and the round-8 monocular-sliver explanation is a coincidence of geometry. If it says
    // ScreenSpaceCamera with worldCamera = GloomhavenVR.HeadCamera, the artifact is in both eyes and
    // the user's "des auges" was about where it sits, not about how many eyes see it.
    //
    // MULTIPLAYER: local rendering only. Canvas.enabled writes on the local player's own UI, no game
    // state, no wire traffic, no config key.

    /// <summary>Session tally of invariant violations found by <see cref="TickFloatIntentGuard"/> —
    /// i.e. of frames on which a tracked, un-floated window had a live canvas going into the render
    /// loop. ZERO here on the next hardware log means the rim did not come from this class of
    /// defect at all, which is information the 419 round did not have.</summary>
    private static int s_floatIntentViolations;

    /// <summary>
    /// Windows <see cref="TickFloatIntentGuard"/> will not act on again until the question is fresh.
    /// Two things put a window here: the guard raised a blackout for it and the hand-back rule then
    /// found no standing intent (so the mod is NOT waiting to float it), or one of
    /// <see cref="PreConvertHide"/>'s gates refused it outright (so the mod does not intend to float
    /// it at all). Both are per-open-cycle answers: the prune in the guard drops a window that has
    /// closed or been floated, and <see cref="NoteReleasedWhileOpen"/> lifts it the moment the mod
    /// hands a still-open window back to its 2D home.
    ///
    /// <para>THIS LATCH IS WHAT MAKES THE GUARD FREE IN STEADY STATE. Without it the guard would run
    /// a subtree walk per tracked window per frame forever — the same mistake
    /// [[findobjectsoftype-is-the-default-suspect]] names, one size down. With it, a window is
    /// walked at most once per open-cycle unless a blackout actually stands for it.</para>
    /// </summary>
    private static readonly List<UIWindow> PreHideGuardStoodDown = new(2);

    /// <summary>
    /// <b>THE INVARIANT, ENFORCED AND MEASURED EVERY FRAME.</b> See the section header above for the
    /// rule, the falsified 419 hypothesis and the ownership argument.
    ///
    /// <para>Called from <see cref="CanvasConversion.LateTick"/>, i.e. after every Update-phase
    /// writer and immediately before the render loop — the last phase in which a canvas that would
    /// be drawn this frame can still be switched off.</para>
    ///
    /// <para><b>COST.</b> Steady state (nothing tracked, or everything floated): one integer
    /// compare. While a blackout stands — 1 to 2 frames per window open in the log — one
    /// <c>GetComponentsInChildren&lt;Canvas&gt;</c> into a reused list per standing entry, over the
    /// window's own subtree (207 transforms for the ESC menu, the largest in the log). For a tracked
    /// window with NO blackout it is at most ONE such walk per open-cycle, because the stand-down
    /// latch above closes the question afterwards. No scene sweep, no allocation, no
    /// <c>FindObjectsOfType</c>.</para>
    /// </summary>
    internal static void TickFloatIntentGuard()
    {
        if (PreHidden.Count == 0 && OpenWindows.Count == 0 && PreHideGuardStoodDown.Count == 0)
            return;
        PruneGuardStandDown();
        // The same global gate TickPreConvertHide uses. Outside it the mod is not floating anything,
        // so it must not own a single one of the game's switches — and the restore is the other
        // method's job, which is why this one simply stops.
        if (!VRSession.IsRunning || !WorldUIConfig.ConversionActive || !WorldUIConfig.ModalWindowStyle
            || FlatScreen.ManualScreenActive || VRModeStateMachine.CurrentMode == VRMode.Menu2D
            || !VRModeStateMachine.TableInFrontOfPlayer)
            return;

        // (A) EVERY STANDING BLACKOUT. This is the lead the round was handed: CanvasConversion
        //     .Release re-enables the canvas of a window the game reports OPEN (its FIX B forces the
        //     hidden state only for a window reported CLOSED), and WindowMaterialise's visibility
        //     hold restores held Canvases from inside a vanish's onDone. Both run from Update, both
        //     can land under a standing blackout, and the ModBuild 418 log recorded the shape
        //     exactly once — an expiry line reading "(0 canvas(es) re-enabled)", i.e. something else
        //     had already switched the canvas back on. ModBuild 419 re-asserted the blackout on ONE
        //     of those orderings (ResolveVanishForReopen); this covers all of them, by not caring
        //     which writer did it.
        for (int i = PreHidden.Count - 1; i >= 0; i--)
        {
            PreHiddenWindow entry = PreHidden[i];
            UIWindow window = entry.Window;
            if (window == null)
                continue; // TickPreConvertHide owns the removal; it runs first in the next Update
            // The three states in which this entry is ALREADY on its way out and its canvases being
            // live is not a violation but the correct picture: the window is floated (so what is
            // enabled is the FLOATED copy), the game closed it, or its conversion failed and it
            // belongs to the flat screen now. TickPreConvertHide hands each of them back by name in
            // the next Update; reporting them here would be an instrument measuring one term
            // ([[instrument-measures-one-term]]) and re-hiding them would be a second writer.
            if (IsConverted(window) || !window.IsOpen || ContainsWindow(Failed, window))
                continue;
            Canvas? live = FirstLiveCanvas(window);
            if (live == null)
                continue;
            NoteFloatIntentViolation(entry, window, live, "a blackout is STANDING for this window");
            PreConvertHide(window); // idempotent: catches exactly the canvases that came back on
        }

        // (B) A TRACKED WINDOW THE MOD WANTS TO FLOAT, WITH NO BLACKOUT STANDING. The shape this is
        //     for: a re-open runs the PREVIOUS float's pending release, that release re-parents the
        //     game window back to its screen-space 2D home and leaves it ENABLED because the game
        //     reports it OPEN, and no blackout is in force at that moment because the entry was
        //     handed back when the conversion took over. PreConvertHide applies every gate, so a
        //     window the mod does not intend to float produces no entry and is stood down instead.
        for (int i = 0; i < OpenWindows.Count; i++)
        {
            UIWindow window = OpenWindows[i];
            if (window == null || !window.IsOpen || IsConverted(window) || ContainsWindow(Failed, window))
                continue;
            if (FindPreHidden(window) != null || ContainsWindow(PreHideGuardStoodDown, window))
                continue;
            Canvas? live = FirstLiveCanvas(window);
            if (live == null)
                continue;
            PreConvertHide(window);
            PreHiddenWindow? raised = FindPreHidden(window);
            if (raised == null)
            {
                // A gate refused it — the mod does not intend to float this window. Close the
                // question for this open-cycle rather than walking its subtree again next frame.
                PreHideGuardStoodDown.Add(window);
                continue;
            }
            raised.RaisedByLateGuard = true;
            NoteFloatIntentViolation(raised, window, live,
                "NO blackout stood — the window is tracked, open, wanted and NOT floated");
        }
    }

    /// <summary>Drop stand-down entries whose question is no longer open: destroyed, closed, or
    /// floated. Bounded by the number of tracked windows (0-2 in every log to date).</summary>
    private static void PruneGuardStandDown()
    {
        for (int i = PreHideGuardStoodDown.Count - 1; i >= 0; i--)
        {
            UIWindow w = PreHideGuardStoodDown[i];
            if (w == null || !w.IsOpen || IsConverted(w))
                PreHideGuardStoodDown.RemoveAt(i);
        }
    }

    /// <summary>
    /// <b>CanvasConversion HANDED A STILL-OPEN WINDOW BACK TO ITS 2D HOME.</b> Called from
    /// <see cref="CanvasConversion.Release"/> for exactly the case its FIX B does NOT cover: the
    /// game reports the window OPEN, so the release leaves it enabled at its screen-space home
    /// instead of forcing the hidden state. That is the head-locked rim itself, and it is also the
    /// one event that makes the guard's question fresh again for a window it had stood down.
    ///
    /// <para>This is NOT a second fix bolted onto the release path — it lifts a latch, and the
    /// invariant does the work on the same frame in LateUpdate. Cost: one list scan over 0-2
    /// entries, on an event that fires once per release.</para>
    /// </summary>
    internal static void NoteReleasedWhileOpen(UIWindow? window)
    {
        if (window == null)
            return;
        for (int i = PreHideGuardStoodDown.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(PreHideGuardStoodDown[i], window))
                PreHideGuardStoodDown.RemoveAt(i);
        }
    }

    /// <summary>
    /// The first ENABLED Canvas anywhere in <paramref name="window"/>'s own subtree, or null.
    /// Allocation-free — the shared scratch list <see cref="PreConvertHide"/> already uses, on the
    /// same thread and never re-entered.
    /// </summary>
    private static Canvas? FirstLiveCanvas(UIWindow window)
    {
        window.transform.GetComponentsInChildren(false, PreHideScratch);
        for (int i = 0; i < PreHideScratch.Count; i++)
        {
            Canvas c = PreHideScratch[i];
            if (c != null && c.enabled)
                return c;
        }
        return null;
    }

    /// <summary>
    /// The last violation SHAPE printed in full — window, mod state, canvas, renderMode and layer.
    /// A change here is a NEW defect and always prints; a repeat is the same defect happening again
    /// and is counted rather than re-printed (see the cap in <see cref="NoteFloatIntentViolation"/>).
    /// </summary>
    private static string s_floatIntentSignature = string.Empty;

    /// <summary>
    /// ONE LINE PER WINDOW PER BLACKOUT: the picture, at the moment the invariant was broken.
    /// This is the line the next hardware round is read with — it is the only place in the mod that
    /// states WHAT would have been drawn rather than what the bookkeeping believed.
    ///
    /// <para><b>WHY IT IS CAPPED, AND WHY THE CAP IS A CHANGE GATE AND NOT A COUNT.</b> The report
    /// this round answers is a 72-tap burst, so a line per blackout is up to ~36 Alert lines from one
    /// press-and-hold — the flood ModBuild 331 removed, arriving through the front door. The cap is
    /// the pattern this file already uses for <see cref="NoteReopenDuringVanish"/> (the first eight
    /// in full, then one per doubling, every line carrying the running total so a burst is still
    /// visible) with one addition: a violation whose SHAPE differs from the last printed one always
    /// prints, because that is a different defect and a count would hide it
    /// ([[a-summary-stat-is-not-the-field]]).</para>
    /// </summary>
    private static void NoteFloatIntentViolation(PreHiddenWindow entry, UIWindow window, Canvas live,
                                                 string state)
    {
        if (entry.ViolationLogged)
            return;
        entry.ViolationLogged = true;
        s_floatIntentViolations++;
        string signature = $"{entry.Name}|{state}|{live.name}|{live.renderMode}|{live.gameObject.layer}";
        bool shapeIsNew = signature != s_floatIntentSignature;
        s_floatIntentSignature = signature;
        if (!shapeIsNew && s_floatIntentViolations > 8
            && (s_floatIntentViolations & (s_floatIntentViolations - 1)) != 0)
            return;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        int layer = live.gameObject.layer;
        int layerBit = 1 << layer;
        bool inHeadMask = head != null && (head.cullingMask & layerBit) != 0;
        bool headToEye = head != null && head.targetTexture == null;
        Camera? world = live.worldCamera;
        // WHO WOULD HAVE DRAWN IT. An OVERLAY canvas is drawn by no camera at all — it is composited
        // onto the target display after the camera loop, which under MultiPass is the one-eye path
        // described in the section header. A ScreenSpaceCamera canvas with no worldCamera falls back
        // to overlay behaviour, so it reads the same way. Anything else is drawn by a camera, and
        // then the question is whether that camera is the head camera or the game's blinded
        // 'UI Camera' (→ GloomhavenVR.DesktopScrubSink, which never reaches the eye).
        bool overlayPath = live.renderMode == RenderMode.ScreenSpaceOverlay
                           || (live.renderMode == RenderMode.ScreenSpaceCamera && world == null);
        string reach = overlayPath
            ? "OVERLAY — no camera draws it; Unity composites it onto the target display ONCE PER " +
              "FRAME after the camera loop, so under MultiPass it lands in ONE eye texture. THIS IS " +
              "THE ONE-EYE READING, and this field is what decides it"
            : world != null && head != null && ReferenceEquals(world, head)
                ? "drawn BY THE HEAD CAMERA, which runs BOTH MultiPass eye passes — so this would " +
                  "show in BOTH eyes, and a one-eye report would have to come from geometry (the " +
                  "left monocular sliver) rather than from the draw path"
                : world != null
                    ? $"drawn by '{world.name}' (target " +
                      $"{(world.targetTexture != null ? world.targetTexture.name : "BACKBUFFER")}) " +
                      "— reaches the eye only if that target is the backbuffer"
                    : "WORLD SPACE — drawn by every camera whose culling mask contains its layer";
        // HW-VERIFY
        VRLog.Alert("WorldUI", $"MODAL FLOAT INTENT VIOLATION ({s_floatIntentViolations} this " +
            $"session): '{entry.Name}' (ID {window.ID}) is tracked and NOT floated, yet its canvas " +
            $"'{live.name}' was ENABLED going into the render loop at frame {Time.frameCount}. " +
            $"MOD STATE: {state}; blackout raised at frame {entry.HiddenAtFrame} and standing for " +
            $"{Time.unscaledTime - entry.HiddenAtTime:F3}s over {entry.Passes} convert pass(es); " +
            $"last bounded intent {(entry.LastHold == null ? "NONE" : "'" + entry.LastHold + "'")}; " +
            $"previous float {(WindowMaterialise.IsVanishing(window.transform) ? "STILL VANISHING" : "not vanishing")}. " +
            $"THE CANVAS: renderMode={live.renderMode}, layer {layer}, overrideSorting=" +
            $"{live.overrideSorting}, sortingOrder={live.sortingOrder}, worldCamera=" +
            $"{(world != null ? world.name : "<none>")}. THE HEAD CAMERA: " +
            $"{(head != null ? head.name : "<none>")}, mask=0x{(head != null ? head.cullingMask : 0):X8}, " +
            $"contains layer {layer}: {(inHeadMask ? "YES" : "no")}, renders to " +
            $"{(head == null ? "<none>" : headToEye ? "the BACKBUFFER (the HMD eye textures)" : head.targetTexture!.name)}, " +
            $"stereo {UnityEngine.XR.XRSettings.stereoRenderingMode} eye texture " +
            $"{UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}. " +
            $"WHO DRAWS IT: {reach}. THE CANVAS HAS BEEN SWITCHED OFF THROUGH THE BLACKOUT'S OWN " +
            "LEDGER, so the restore stays one for one. USER REPORT 2026-09-04 on ModBuild 419: " +
            "'Das Problem besteht weiterhin' — the head-locked rim at the left edge of one eye on a " +
            "rapid options-key burst. 419's frame-budget hypothesis is FALSIFIED by its own log (its " +
            "outer-safety warning never fired once, and every blackout handed back after one frame); " +
            "THIS line is the picture that bookkeeping could not see. It quotes no other " +
            "instrument's grep token on purpose — the first draft did, and both counts of the 420 " +
            "log came back matching this sentence instead of the instrument.");
    }
}
