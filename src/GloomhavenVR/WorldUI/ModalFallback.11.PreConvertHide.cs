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
    //    path took over, Menu2D, VR stopped — plus a hard <see cref="PreConvertHideMaxFrames"/>
    //    frame budget that ends the blackout no matter what went wrong.
    //  * It never runs in Menu2D: there the head camera renders the mod layer ONLY and the flat
    //    screen composites the game's 2D UI, so there is nothing to hide and hiding would blink
    //    the flat screen. The artifact is a SCENARIO one by construction.
    //
    // MULTIPLAYER: local rendering only. Two Canvas.enabled writes on the local player's own UI,
    // no game state, no wire traffic.

    /// <summary>
    /// Hard bound on the blackout (frames). The conversion lands in the very next tick, so this
    /// is pure insurance: whatever goes wrong, the game's window is handed back after ~0.1 s at
    /// 72 Hz. A window must never stay invisible — same ruling as the reveal gate's 0.6 s bound.
    /// </summary>
    private const int PreConvertHideMaxFrames = 8;

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
        // for it could only ever end on the PreConvertHideMaxFrames budget with the "was still
        // un-floated after N frames — handing its 2D rendering back regardless" warning. The
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
                               $"({restored} canvas(es) re-enabled).");
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
    /// FIRST step of <see cref="Tick"/>: end every blackout whose window will not be converted
    /// this tick after all — closed, dead, already floated, conversion failed, screen style /
    /// manual screen / Menu2D / VR off — and, unconditionally, any blackout older than
    /// <see cref="PreConvertHideMaxFrames"/>. Nothing else in the mod can leave a game window
    /// switched off, and this runs before any step that could throw.
    /// </summary>
    private static void TickPreConvertHide()
    {
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
                : age > PreConvertHideMaxFrames
                    ? $"the {PreConvertHideMaxFrames}-frame budget expired without a conversion — a window " +
                      "must never stay invisible"
                : null;
            if (reason == null)
                continue;
            if (age > PreConvertHideMaxFrames && window.IsOpen && !IsConverted(window))
                VRLog.Warn("WorldUI", $"MODAL PRE-CONVERT BLACKOUT: '{entry.Name}' was still un-floated after " +
                                      $"{age} frames — handing its 2D rendering back regardless. It may show one " +
                                      "flat frame at its screen-space home (the artifact this guard exists to " +
                                      "prevent), but an invisible window is strictly worse.");
            RestorePreHidden(entry, reason);
            PreHidden.RemoveAt(i);
        }
    }
}
