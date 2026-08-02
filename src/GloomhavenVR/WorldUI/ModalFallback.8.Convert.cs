using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    // ---- conversion ---------------------------------------------------------------------

    /// <summary>
    /// Float one fallback window in front of the HMD. Returns false (with the reason
    /// logged) when the window cannot be converted — the caller then raises the full
    /// flat screen for it instead.
    /// </summary>
    /// <summary>
    /// True when <paramref name="window"/> is one of the full-screen menus (ESC / options
    /// family) AND its root rect genuinely fills the screen. Only these are exempted from
    /// the central content fit (P6 flicker fix): they are meant to float as a whole screen,
    /// so their host must stay fixed at the window's own rect rather than being re-measured
    /// and re-centered every ~30 frames as their fade/focus animations cross the fit's
    /// alpha threshold. The ID gate keeps every other modal (confirmation boxes, message,
    /// rewards/results, the story box) on the fit path; the geometry gate guards against a
    /// non-full-screen variant of a menu ID being wrongly exempted.
    /// </summary>
    private static bool IsFullScreenMenu(UIWindowID id, RectTransform rect)
    {
        if (id != UIWindowID.ESCMenu && id != UIWindowID.Options
            && id != UIWindowID.OptionsSubmenu && id != UIWindowID.ViceOptionsSubmenu)
            return false;

        // Stretch anchors filling the parent → a full-screen root (resolution-independent).
        Vector2 aMin = rect.anchorMin;
        Vector2 aMax = rect.anchorMax;
        if (aMin.x <= 0.01f && aMin.y <= 0.01f && aMax.x >= 0.99f && aMax.y >= 0.99f)
            return true;

        // Or the rect covers (near) the whole root canvas.
        Canvas? canvas = rect.GetComponentInParent<Canvas>();
        var canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRect != null)
        {
            Vector2 cs = canvasRect.rect.size;
            Vector2 ws = rect.rect.size;
            if (cs.x > 1f && cs.y > 1f && ws.x >= cs.x * 0.9f && ws.y >= cs.y * 0.9f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The full-screen menu family whose floated host would otherwise show an opaque
    /// full-window backing/blur rectangle (user #8 part 2): the ESC / Options menus and
    /// the end-of-scenario Results / Rewards / adventure-completion panels. For these the
    /// backing image is disabled while floated so only the foreground content shows; every
    /// other modal (story box, events, messages) keeps its backing.
    ///
    /// PAUSE/OPTIONS CONFIRMATIONS (issue #1): the generic-warning confirmation boxes the
    /// pause menu spawns (restart round/turn, skip mission/tutorial, quit dungeon — all
    /// <c>UIConfirmationBoxManager.MainMenuInstance.ShowGenericWarningConfirmation</c>,
    /// UIScenarioEscMenu.cs, plus the multiplayer/character confirmations) are a full-window
    /// DARK OVERLAY behind a small centered dialog box. Un-hidden it floated as the reported
    /// "big dark FLAT full-screen rectangle" with the popup in the middle. It IS the same
    /// un-hidden full-window backing the ESC/Options menus get stripped of, so these
    /// confirmation IDs join the transparent-background family: the dark overlay is disabled
    /// and only the centered dialog box remains, floated on its own world-space board panel.
    /// </summary>
    private static bool WantsTransparentBackground(UIWindowID id) =>
        id == UIWindowID.ESCMenu || id == UIWindowID.Options
        || id == UIWindowID.OptionsSubmenu || id == UIWindowID.ViceOptionsSubmenu
        || id == UIWindowID.ResultsPanel || id == UIWindowID.RewardsPanel
        || id == UIWindowID.AdventureCompletionPanel
        || MenuConfirmations.Contains(id);

    /// <summary>
    /// Issue #1: pause/options-spawned CONFIRMATION dialogs — the family that must be treated
    /// exactly like the other floated submenus (own world-space board panel, grab bar, X
    /// close, one-shot content fit, hidden full-window dark backing, no flicker) rather than
    /// floating as a big dark flat rectangle. Every member is a <c>ConfirmationBox</c>-style
    /// window a scenario menu button opens:
    /// - <c>MainMenuConfirmationBox</c> — THE reported one: restart round/turn, skip
    ///   mission/tutorial, quit dungeon (UIScenarioEscMenu.cs →
    ///   UIConfirmationBoxManager.MainMenuInstance.ShowGenericWarningConfirmation;
    ///   verified in the runtime log as 'Confirmation Box_MainMenu_pc'/'_gamepad').
    /// - <c>ConfirmationBox</c> — the in-scenario generic confirmation ('Confirmation Box_pc'/
    ///   '_gamepad'); normally physicalized by <see cref="Surfaces.DialogSurface"/>, so it only
    ///   reaches this fallback when that surface is off (<see cref="IsFallbackWindow"/>).
    /// - <c>MutiplayerConfirmationBox</c> / <c>CharacterConfirmationBox</c> — the multiplayer /
    ///   character-assign confirmations reachable from the multiplayer submenu.
    /// These stay BLOCKING (they assert ModalUI like any genuine prompt) and are NOT sticky —
    /// so clicking Yes/No (or the mod X) closes them cleanly through the normal release path;
    /// only their VISUAL treatment is unified with the submenus.
    /// </summary>
    private static readonly HashSet<UIWindowID> MenuConfirmations = new()
    {
        UIWindowID.MainMenuConfirmationBox,
        UIWindowID.ConfirmationBox,
        UIWindowID.MutiplayerConfirmationBox,
        UIWindowID.CharacterConfirmationBox,
    };

    private static bool TryConvertWindow(UIWindow window)
    {
        string name = window.name;
        try
        {
            var rect = window.transform as RectTransform;
            if (rect == null)
            {
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) has no RectTransform " +
                                      "root — cannot float it, falling back to the full flat screen.");
                return false;
            }

            // Story-box sanity: the click-to-advance UICharacterStoryBox (serialized
            // separately from the window, StoryController.cs:62-66) must live INSIDE
            // the converted subtree, or the floating panel could never advance the
            // story. Same for its skip button (UICharacterStoryBox.cs:44). While
            // here, remember it as the content root for the host-rect fit — the
            // story window root is a full-screen 1920x1080 stretch rect, the visible
            // dialog is the UICharacterStoryBox subtree.
            RectTransform? contentRoot = null;
            bool isStoryBox = false; // the click-through story/subtitle window (X exclusion below)
            if (Singleton<StoryController>.IsInitialized)
            {
                StoryController sc = Singleton<StoryController>.Instance;
                if (sc != null && ReferenceEquals(sc.window, window))
                    isStoryBox = true;
                if (sc != null && ReferenceEquals(sc.window, window) && sc.dialogBox != null)
                {
                    if (!sc.dialogBox.transform.IsChildOf(window.transform))
                    {
                        VRLog.Warn("WorldUI", "MODAL WINDOW: story box dialog (UICharacterStoryBox) is NOT " +
                                              "under the story window root — clicking the floating panel could " +
                                              "not advance the story; falling back to the full flat screen.");
                        return false;
                    }
                    contentRoot = sc.dialogBox.transform as RectTransform;
                }
            }

            // FLICKER FIX (P6): a FULL-SCREEN menu (ESC / options) is meant to float as a
            // whole screen in front of the player — its 1920x1080 stretch root IS the
            // content. Enrolling it in the central content-FIT is actively wrong: as its
            // LeanTween background fade + button-focus animations cross the fit's alpha
            // threshold, the measured visible-Graphic union alternates between the full
            // frame and the smaller button cluster, and each accepted fit re-centers +
            // resizes the host in place → a visible per-frame flicker/jump. Exempt these
            // windows from the fit (host stays fixed at the window's own rect); strip-style
            // windows that genuinely need the fit (story box via contentRoot below, and any
            // non-full-screen dialog) keep it.
            bool fullScreenMenu = IsFullScreenMenu(window.ID, rect);
            // Issue 3 (Options controls unclickable): the width-collapsing one-shot content fit is
            // correct for the ESC MENU (a narrow button column) but WRONG for the Options family —
            // Options is a LEFT category rail + a RIGHT settings/slider panel, and hugging the
            // visible-graphic union (dominated by the left rail) shrank the host ~1552->416 px and
            // shifted content ~580 px left, pushing the right panel OUTSIDE the clickable host rect
            // (only Audio/Video on the rail stayed hittable). Split the shared full-screen-menu
            // signal: the HEIGHT cap applies to the whole family (Issue 2), but the WIDTH-hug (the
            // one-shot content fit) applies to the ESC menu ONLY. The Options family keeps its full
            // width — height-capped only, NOT enrolled in the content fit — so both the rail and the
            // slider panel stay inside the visible/clickable host rect.
            bool escMenuWidthHug = fullScreenMenu && window.ID == UIWindowID.ESCMenu;
            // Issue #1: a pause/options-spawned CONFIRMATION dialog (restart round/turn, skip
            // mission/tutorial, quit dungeon, multiplayer/character confirms). These are NOT a
            // full-screen menu (a small centered dialog box behind a full-window DARK OVERLAY), so
            // fullScreenMenu is false — but they get the SAME submenu treatment: the dark overlay is
            // hidden (transparentBg, via WantsTransparentBackground) AND the host is content-fit
            // ONCE and LOCKED so it hugs the visible dialog box and the per-frame re-fit that made it
            // FLICKER can never recur (the un-hidden overlay + per-frame fit were exactly the
            // reported "big dark flat rectangle that flickers").
            bool isConfirmDialog = MenuConfirmations.Contains(window.ID);
            // Options family: do NOT enroll in the content fit at all (fitContent:false) — the fit
            // would width-collapse it. It floats at its own (full) width, with only the height cap
            // trimming the tall empty bottom. Every other window keeps the default (fit pokeable
            // hosts): non-menu modals fit as before, the ESC menu + confirmations one-shot-fit
            // (fitContent:null).
            bool? fitContent = fullScreenMenu && !escMenuWidthHug ? false : (bool?)null;
            // One-shot content fit (fit once → lock, no per-frame re-fit flicker): the ESC menu
            // (compact width-hug column) AND every pause/options confirmation dialog (hugs the
            // centered dialog box once its dark overlay is hidden). The Options family stays OUT
            // (full width, height-capped only, see above); non-menu modals keep the default fit.
            bool oneShotFit = escMenuWidthHug || isConfirmDialog;
            // User #8 part 2: the full-screen menu family (ESC / Options / Results / Rewards)
            // floats as an opaque backing rectangle — hide that backing so only the
            // foreground content shows.
            bool transparentBg = WantsTransparentBackground(window.ID);
            // FLICKER FIX: float the modal host at a dominant sortingOrder so it composites
            // ON TOP of every other order-0 world-space host instead of tying with them and
            // swapping render order as the head micro-moves — see ModalHostSortingOrder.
            // User #8 part 1: float on the dedicated mod layer (useModLayer) so ONLY the HMD
            // head camera renders it — the game's mono UI Camera can no longer double-draw
            // the world-space modal (the confirmed flicker root cause).
            // NOTE (item 1a REVERTED): menus are NOT rendered on top anymore. The ZTest-Always
            // "render-on-top" treatment was removed at the user's request — menus ZTest normally
            // (LEqual) again, so hands and the board occlude them like every other world-space UI.
            // The sky/backdrop is kept from clipping floated menus by a SEPARATE non-occluding-sky
            // change (Core.MixedReality), not by lifting the menu over all geometry.
            // Item 1 (pause-menu size): a full-screen menu (ESC / Options family) is now content-fit
            // ONCE and then LOCKED (fitOneShot) instead of exempted. The old exemption kept the host
            // at the game window's own rect (1920x2040 — hugely tall with empty space, and a stale/
            // smaller rect on the very first open before layout), which read as inconsistent + mostly
            // blank. A single fit to the visible-button bounds trims the empty space and lands the
            // same compact size every open; locking after the one apply keeps the P6 per-frame re-fit
            // flicker from recurring (the opaque backing is already hidden, so the fit measures only
            // the stable foreground content). The reveal waits for that single fit, so it pops in
            // already compact rather than flashing at the full rect.
            // Bug #7 (pause-menu height): the ESC/Options full-screen menus have a tall content
            // column that the game grows over the first open, so a WARM reopen captured ~2040 px
            // (panel ~1.9x taller with an empty bottom) while a COLD first open captured ~1080.
            // Cap the captured height to the root canvas reference height (~1080) for exactly this
            // full-screen-menu family so every open lands the same compact height — width/scale and
            // all non-menu modals are unaffected (capHeightToCanvas is false for them).
            ConvertedPanel? panel = CanvasConversion.Convert(rect, $"Modal_{name}", pokeable: true,
                fitContent: fitContent, sortingOrder: ModalHostSortingOrder,
                diagnostic: true, // FLICKER HUNT: per-frame change-gated host/child/camera diagnostics
                useModLayer: true, transparentBackground: transparentBg,
                // Issue 3: WIDTH-hug one-shot fit for the ESC menu; issue #1: confirmation dialogs
                // one-shot-fit too. The full-screen-menu family gets the HEIGHT cap (confirmations do
                // not — the one-shot fit already hugs their compact box). Issue 5: keep the backing
                // disabled on release for the full-screen-menu family only.
                fitOneShot: oneShotFit, capHeightToCanvas: fullScreenMenu,
                keepBackgroundHidden: fullScreenMenu);

            if (escMenuWidthHug)
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) is the ESC menu — one-shot " +
                                      "content fit (compact width-hug + height cap, locked after the single fit " +
                                      "so the per-frame re-fit flicker cannot recur).");
            else if (isConfirmDialog)
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) is a pause/options confirmation " +
                                      "dialog — treated like a submenu: full-window dark overlay hidden, one-shot " +
                                      "content fit hugging the dialog box (locked, no re-fit flicker), grab + X.");
            else if (fullScreenMenu)
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) is an Options-family menu — " +
                                      "FULL width preserved (height-capped only, no width-hug fit) so both the " +
                                      "category rail and the settings/slider panel stay inside the clickable host.");
            if (panel == null)
            {
                VRLog.Warn("WorldUI", $"MODAL WINDOW: conversion of '{name}' (ID {window.ID}) returned " +
                                      "null (target destroyed?) — falling back to the full flat screen.");
                return false;
            }

            // Item 1 (size): board-relative default shrink. A window WIDER than the board is
            // capped down to ModalTargetWidthMeters (so the pause/Options slab opens board-sized,
            // not the ~1.3 m default); narrower dialogs keep WindowScaleFactor. Grabbable resize
            // still rides on top. Item 2: stagger each stacked window so secondaries overlap but
            // do not coincide with the primary.
            float extraScale = DeriveWindowScale(panel);
            int staggerIndex = Converted.Count;
            // Torbogen report: level-message windows (tutorial box / action strip) get the
            // closer, gaze-centered, view-cone-guaranteed placement; every other family keeps
            // the shared 1.2 m spawn unchanged.
            bool isLevelMsg = IsLevelMessageWindow(window);
            // CHAIN POSE CONTINUITY (user ruling 2026-08-02) — rule 2: a level-message group
            // window that reopens MID-CHAIN (the game closed the group between two messages)
            // takes the previous message's stored pose VERBATIM: no gaze placement, no pitch/
            // board/view-cone clamps, no overlap resolve, no one-shot facing — the player
            // approved that exact spot by leaving (or grab-moving) the window there; only
            // finiteness was sanity-checked (TryGetChainPose). Rule 1 — the in-front,
            // view-cone-guaranteed spawn below — applies only when no valid chain pose
            // exists: the chain's FIRST message, after the scenario/teardown reset, or after
            // presence regain invalidated a stale pose. Same scale convention as PlaceAtHmd
            // (ComputeHmdPose: PanelLayout.WorldScale × the window's board-relative shrink).
            if (isLevelMsg && TryGetChainPose(window, out Vector3 chainPos, out Quaternion chainRot))
            {
                CanvasConversion.PlaceHost(panel, chainPos, chainRot,
                    PanelLayout.WorldScale * extraScale);
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) re-floated at the stored " +
                                      $"chain pose ({chainPos.x:F2},{chainPos.y:F2},{chainPos.z:F2}) — " +
                                      "position continuity: the next hint of a scripted chain appears " +
                                      "exactly where the previous one was read (rule 2, pose verbatim).");
            }
            else
            {
                PlaceAtHmd(panel, extraScale, staggerIndex, isLevelMsg);
                // ONE-SHOT FACING (task #1): the host was just yawed to face the head (ComputeHmdPose,
                // PanelPlacement convention) — a spawn-only orient, not a per-frame billboard, so once
                // the grab frame is seeded from it below the user's grab-rotation is authoritative and
                // persists. Log the applied yaw (window name) for on-device diagnosis. Rule-1 spawns
                // only — a rule-2 chain spawn above keeps the stored rotation verbatim.
                if (panel.HostGo != null)
                    VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) one-shot facing applied — " +
                                          $"yawed {panel.HostGo.transform.eulerAngles.y:F1}° to face the head upright " +
                                          "(spawn-only; grab-rotation authoritative afterwards).");
            }
            // Narrower measure root for the content fit (story window: the visible
            // UICharacterStoryBox, not the 1920x1080 stretch root). The fit itself
            // runs centrally in CanvasConversion.Tick (test #14 item 1).
            panel.FitContentRoot = contentRoot;

            // Sub-item B + Sieg/Niederlage rework (user request A): EVERY floated modal —
            // now INCLUDING the end-of-scenario Sieg/Niederlage results windows
            // (UIResultsManager / UINewAdventureResultsManager, IDs ResultsPanel /
            // AdventureCompletionPanel) — becomes a grabbable + two-hand-scalable world
            // element via the shared PanelGrabHandle, so the results window behaves exactly
            // like the other sub-menu/modal windows. Build reads the host's just-placed HMD
            // pose so the panel does not jump.
            bool isResultsPanel = IsResultsPanel(window.ID);
            // Part 10 (reward-showcase enrollment): the mid-scenario chest showcase window
            // (UICampaignRewardWindow — reached via the ScenarioRewardManager poll; its
            // UIWindowID is scene-serialized and unprovable from code, see the part-10
            // verification comment) must NOT carry the mod X. Its ONLY sane exit is its
            // native continue button: the close chain runs CampaignRewardsManager.Confirm →
            // Finish → the Choreographer callback that clears m_BlockClientMessageProcessing
            // (Choreographer.cs:2419-2426) — an X calling Hide() would close the window
            // WITHOUT that callback, leaving the message pump blocked forever with no way
            // to reopen the showcase: a worse deadlock than the one being fixed. Matched by
            // COMPONENT (provable from code) rather than by the unprovable ID.
            bool isRewardShowcase = window.GetComponent<UICampaignRewardWindow>() != null;
            var grab = new GrabbableModal();
            // Problem #4 (HUD bleed-through): the pause/options/confirmation menu family gets a
            // coplanar DEPTH MASK behind its content so the game's transparent HUD (initiative
            // track, button-cluster labels) that sits BEHIND the floated menu is depth-occluded by
            // it — the menu writes no depth of its own (ZWrite OFF, deliberate, so hands/board still
            // occlude it), so without the mask that HUD bled through. Gated to EXACTLY the floated
            // full-screen menu (ESC/Options family, fullScreenMenu) + the pause/options confirmation
            // dialogs (isConfirmDialog) + the Sieg/Niederlage results windows (their full-window
            // backing is stripped like the menu family's — WantsTransparentBackground — so the same
            // HUD bleed applies); normal small modals/tooltips/story and content windows
            // (Compendium, friend list) get no mask.
            bool wantDepthMask = fullScreenMenu || isConfirmDialog || isResultsPanel;
            grab.Build(panel, extraScale, name, depthMask: wantDepthMask);
            // Item 3c + MP test ("Kontrolle übergeben" had no X): a small mod-drawn X (top-right
            // of the host, mod layer 27, poke+laser clickable) closes THIS window through the
            // game's own Escape/Hide path. RULE (user): EVERY floated window must be closable
            // via X. The old gate was a WHITELIST (reachable menus + confirmation dialogs), so
            // any window outside it — the reported one: the multiplayer transfer-control player
            // picker, 'UI Multiplayer Select Player Submenu' (ID MutiplayerPlayerPicker) —
            // floated with no X and was closable only through the escape chord. Flipped to a
            // BLACKLIST: every converted window gets the X except the two windows with an
            // explicit user ruling. This is safe for unknown windows because the X runs the SAME
            // CloseFloatedWindow (UIWindow.Escape() with a forced Hide() fallback) the modal
            // escape chord already applies to EVERY floated window without a whitelist.
            // EXCLUDED: the click-through Story/dialog box (user: "the dialog must be clicked
            // through, it may not have an X") and — HARD exclusion, user request A — the
            // Sieg/Niederlage results windows: the ONLY way out of the end-of-scenario window
            // must remain its native continue/retry/exit buttons (an X would Hide() the window
            // and strand the scenario-end flow with no way to re-open it).
            if (!isResultsPanel && !isStoryBox && !isRewardShowcase)
                ModalCloseButton.Attach(panel, window);
            else
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) floats WITHOUT an X " +
                                      $"({(isResultsPanel ? "results window — native buttons are the only exit"
                                          : isRewardShowcase ? "reward showcase — native continue is the only exit (its callback releases the message pump)"
                                          : "click-through story box")}).");

            Converted.Add(new WindowPanel
            {
                Window = window,
                Panel = panel,
                FullScreenMenu = fullScreenMenu,
                // Issue #1: ESC menu + confirmations one-shot-fit → the 5b block re-derives their
                // board-relative scale from the fitted (shrunk) width so they render board-sized.
                OneShotFitted = oneShotFit,
                Grab = grab,
                ExtraScale = extraScale,
                // Item 6: reachable menus stay floated in parallel even when the game's single-window
                // toggle hides a sibling; cache the CanvasGroup used to re-assert their visibility.
                Sticky = NonBlockingMenus.Contains(window.ID),
                WindowCanvasGroup = window.GetComponent<CanvasGroup>(),
                // Item 6 (empty-shell fix): cache the window's own Canvas so ReassertStickyVisible can
                // re-enable it after a `_disableCanvas` UIWindow disables it on its hide-fade complete.
                WindowCanvas = window.GetComponent<Canvas>(),
                // User #12: Sieg/Niederlage window — thumbstick-Y scrolls its scroll area
                // (target found lazily in TickResultsStickScroll; list content pools in late).
                IsResults = isResultsPanel,
                // Chain continuity: seed the message key so the first key-change capture in
                // TickMenuRecall fires only on a genuine NEXT message, not on the tick after
                // this convert (null → current key would just re-store the just-placed pose).
                // Null for every non-level-message window.
                LastLevelMessageKey = CurrentLevelMessageKey(window),
            });
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) floated in front of the HMD " +
                                  $"({WindowDistanceMeters:F1} m, poke + laser clickable) — " +
                                  "restored to 2D when it closes.");
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"MODAL WINDOW: conversion of '{name}' FAILED " +
                                   $"({ex.GetType().Name}: {ex.Message}) — falling back to the full " +
                                   "flat screen for this window.");
            return false;
        }
    }

    /// <summary>
    /// Presence-regained recovery (test #17): re-place every floating modal window in
    /// front of the CURRENT head pose. The user may have physically moved while the
    /// HMD was off — a modal stranded out of view is an un-dismissable lock. Returns
    /// how many windows were re-floated (for the "[Core] Session resumed" report).
    /// </summary>
    internal static int RefloatOpenWindows()
    {
        // CHAIN POSE CONTINUITY × presence regain (the stale-pose fix STAYS): a chain pose
        // captured around a doff/don is untrustworthy — the user may have physically moved or
        // turned while the HMD was off, so the parked spot can sit behind them, and a pose
        // captured MID-donning can be junk. Drop the store (rule 1) and re-seed it below from
        // the fresh in-front pose of any level-message float being re-placed, so continuity
        // resumes from where the chain is NOW readable.
        ResetChainPoses("presence regained — a pose captured around the doff/don is untrusted");
        int count = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.Panel.IsAlive)
                continue;
            // Sub-item B: for a grabbable modal re-seat the GRAB FRAME (the host follows it
            // every tick) — placing the host directly would be snapped straight back by the
            // next follow tick. (Every floated modal is grabbable now; the PlaceAtHmd branch
            // remains as a safety net should Grab ever be null.)
            bool levelMessage = IsLevelMessageWindow(wp.Window);
            bool placed = false;
            if (wp.Grab != null)
            {
                // User request A: pass the panel's size so the refloat pose also avoids the
                // control board / other open modals (this panel excluded from the obstacles).
                Vector2 half = PanelWorldHalfSize(wp.Panel, PanelLayout.WorldScale * wp.ExtraScale);
                if (ComputeHmdPose(out Vector3 pos, out Quaternion rot, out _, 0, half, wp.Panel,
                        levelMessage))
                {
                    wp.Grab.PlaceFrameAt(pos, rot);
                    placed = true;
                }
            }
            else
            {
                PlaceAtHmd(wp.Panel, wp.ExtraScale, 0, levelMessage);
                placed = true;
            }
            // Chain continuity: the refloat pose is the chain's new anchor (see the reset
            // above) — only after a SUCCESSFUL placement, so a no-head-pose tick can never
            // re-store the stale pre-doff pose.
            if (placed && levelMessage)
                StoreChainPose(wp);
            count++;
        }
        return count;
    }

    /// <summary>
    /// The end-of-scenario Sieg/Niederlage results windows: the scenario results panel
    /// (UIResultsManager, header GUI_RESULTS_WIN / GUI_RESULTS_LOSE — "Sieg"/"Niederlage")
    /// and the adventure-completion variant (UINewAdventureResultsManager). User request A:
    /// they float as GRABBABLE modals exactly like every other window (grab bar, two-hand
    /// resize, depth mask), but carry NO X close button — the only way out must remain their
    /// native continue/retry/exit buttons — and they participate in the lost-menu recall
    /// (<see cref="TickMenuRecall"/>) so a carried-away results window can never be lost
    /// off-view while it blocks the scenario-end flow.
    /// </summary>
    private static bool IsResultsPanel(UIWindowID id) =>
        id == UIWindowID.ResultsPanel || id == UIWindowID.AdventureCompletionPanel;

    /// <summary>
    /// Item 3b: player-reachable menus that must NOT assert ModalUI, so the user keeps FULL
    /// world interaction (board / cards / fan) while the pause menu — or a submenu opened from
    /// it (options / multiplayer / compendium) — is open. Every other window that reaches the
    /// modal path (story, level messages, dialog-confirms, results, durability) still blocks.
    /// These menus still float, stay grabbable, and carry the X button — only the mode lock is
    /// lifted for them.
    /// </summary>
    private static readonly HashSet<UIWindowID> NonBlockingMenus = new()
    {
        UIWindowID.ESCMenu,
        UIWindowID.Options,
        UIWindowID.OptionsSubmenu,
        UIWindowID.ViceOptionsSubmenu,
        UIWindowID.CompendiumPanel,
        UIWindowID.MultiplayerFriendList,
        UIWindowID.HelpBox,
    };

}
