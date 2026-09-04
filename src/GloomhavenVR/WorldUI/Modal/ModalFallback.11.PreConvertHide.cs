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
        int layerBit = 1 << window.gameObject.layer;
        PreHideCensusSb.Clear();
        PreHideCensusSb.Append("layer ").Append(window.gameObject.layer).Append(':');
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        int drawers = 0;
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || !cam.enabled || (cam.cullingMask & layerBit) == 0)
                continue;
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
        string census = PreHideCensusSb.ToString();
        if (census == s_preHideCensus)
            return;
        s_preHideCensus = census;
        VRLog.Info("WorldUI", $"MODAL PRE-CONVERT BLACKOUT census (changed): {census}. A camera listed with a " +
                              "BACKBUFFER target draws the game's 2D UI straight into the HMD eye textures — " +
                              "that is the left-edge one-eye flash this blackout removes. 'flat screen HIDDEN' " +
                              "means the stereo screen's per-eye RTs do not exist here, so the two eyes share " +
                              "one render path and a per-eye RT divergence cannot explain a flash at this site.");
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
                              + "the burst was handled.");
    }

    /// <summary>
    /// FIRST step of <see cref="Tick"/>: end every blackout whose window will not be converted
    /// this tick after all — closed, dead, already floated, conversion failed, screen style /
    /// manual screen / Menu2D / VR off — and, since ModBuild 419, every blackout for which no
    /// named bounded intent to float still stands (<see cref="PreConvertHideHold"/>). Nothing else
    /// in the mod can leave a game window switched off, and this runs before any step that could
    /// throw.
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
}
