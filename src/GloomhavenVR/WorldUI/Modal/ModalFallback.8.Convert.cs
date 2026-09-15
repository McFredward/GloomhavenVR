using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{

    // THE MOD'S OWN MENU WINDOWS ARE RECOGNISED IN ONE PLACE NOW: MenuWindowFamily. ModBuild 339
    // added a private `VROptionsWindowName` here to give the VR settings pane the Options family's
    // content-fit exemption, and that name test stayed private to this file — so every OTHER
    // id-keyed rule in ModalFallback still missed the window, which is the whole of the ModBuild
    // 340 defect ("Wenn man die VR Optionen offen hat kommt die Kartenhand nicht"). The constant,
    // the registration and the family predicates now live together in MenuWindowFamily.cs.
    // ---- conversion ---------------------------------------------------------------------

    /// <summary>
    /// Float one fallback window in front of the HMD. Returns false (with the reason
    /// logged) when the window cannot be converted — the caller then raises the full
    /// flat screen for it instead.
    /// </summary>
    /// <summary>
    /// True when <c>window</c> is one of the full-screen menus (ESC / options
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

    /// <summary>
    /// THE WINDOW FLATNESS GUARANTEE (ModBuild 193) — why every window this method floats now
    /// converts with <c>flattenWindow: true</c>.
    ///
    /// <para>USER RULING, verbatim (2026-08, report item 3): <i>"Der Ring von der Magierin guckt 3D
    /// aus dem Fenster raus, liegt also nicht flach auf dem Fenster. Prüfe bei den Fenstern die man
    /// spawnen kann alle Elemente die darauf liegen, dass sie flach auf dem Fenster angezeigt werden
    /// so ausgerichtet wie es das Fenster selber auch ist."</i> This is deliberately NOT a fix for
    /// one ring: what is asked for is a property of the class of spawnable windows, so the opt-in is
    /// passed unconditionally here rather than to a list of window IDs.</para>
    ///
    /// <para>WHAT WAS TRUE BEFORE, read from source: <c>CanvasConversion.Convert</c> has taken a
    /// <c>flatten2D</c> opt-in since test #21 (CanvasConversion.1.Core.cs:109) and THIS call site
    /// never passed it. Only five surfaces did — <c>CombatLogSurface</c>, <c>PropInfoSurface</c>
    /// (Surfaces/PropInfoSurface.cs:157), <c>StatPanelSurface</c> (:488 and :602) and
    /// <c>EnemyRevealSurface</c> (:295). Two comments already in the tree say so in as many words
    /// (WorldUI/TooltipOnWindow.cs:149-151 and WorldUI/WorldUIModule.cs:277-279), and the ModBuild
    /// 192 hardware log confirms it by silence: it contains 100 <c>MODAL LAYER</c> lines and not one
    /// <c>Flattened</c> line. So inside a floated window only the tooltip family was ever clamped
    /// (<c>TooltipOnWindow.LateTick</c>); everything else rode in with whatever 3D pose it had.</para>
    ///
    /// <para>WHY THE POSE IS THERE AT ALL — read from the decompiled game, not inferred.
    /// <c>ObjectPool.SpawnCard</c> reparents a pooled card with <c>SetParent(parent)</c>
    /// (ObjectPool.cs:468), i.e. <c>worldPositionStays: true</c>, and resets local rotation only
    /// when the caller asks (<c>resetLocalRotation</c> defaults to false, :415 and :481-483); local
    /// z is always zeroed (:484-486) but rotation is not. No item-card caller passes it — including
    /// <c>UIPartyItemInventoryTooltip.cs:165</c>, which is the equipment/inventory/merchant item
    /// card, and <c>UIItemScenario.cs:136</c> and <c>TwoHandItemUI.cs:69</c>, which are not tooltips
    /// and are therefore outside <c>TooltipOnWindow</c>'s reach. Under a flat, unrotated
    /// screen-space canvas a preserved WORLD rotation is a zero LOCAL rotation and nobody ever
    /// noticed. Under a world-space host that has been YAWED to face the player, the same preserved
    /// world rotation lands as a LOCAL rotation the size of that yaw — which is precisely
    /// "guckt 3D aus dem Fenster raus".</para>
    ///
    /// <para>NOT A GAME-SIDE PATCH, AND NOT A WRITE WAR. Rotation and local z are the two components
    /// no game writer re-asserts per frame: the GUIAnimator tween family has a MOVE_LOCAL channel
    /// that writes a full <c>localPosition</c> including z (LeanTweenGuiAnimationSettingMove.cs:53)
    /// but has no rotate channel at all. So the clamp is a single writer, in LateUpdate, writing a
    /// value nobody writes differently in the same frame — it cannot alternate, which is the
    /// specific failure ("flackert stark", the two MultiPass eyes disagreeing) this project has
    /// shipped before and must not ship again. The census line names the count of re-asserts per
    /// frame so a second writer would be visible in the log rather than on the user's face.</para>
    ///
    /// <para>MULTIPLAYER: nothing here goes on the wire, and nothing here may. Local rotation and
    /// local z of a local uGUI transform are LOCAL PRESENTATION of a window the remote seat does not
    /// have open, in a coordinate system (a world-space host placed in front of THIS player's HMD)
    /// that does not exist on the other machine. There is no game state to sync and no
    /// <c>NetProtocol</c> change; the guarantee is compatible with multiplayer by having nothing to
    /// do with it.</para>
    ///
    /// <para>WHAT "FLAT" MEANS HERE, AND THE ONE THING THAT CHOICE COSTS. The clamp writes local
    /// rotation to full IDENTITY, not merely "no pitch/yaw". Strictly, a rotation about the local Z
    /// axis is a roll IN the window plane and does not stick out of it, so a decorative in-plane
    /// tilt (a diagonal banner) is straightened by this pass even though it was never the
    /// complaint. That is deliberate and it is the user's own wording — <i>"so ausgerichtet wie es
    /// das Fenster selber auch ist"</i>, oriented the way the window itself is, which is identity —
    /// and it keeps this sweep agreeing with the two flatteners already in the tree on what flat
    /// means (<c>WorldTooltips.FlattenSubtree</c>, <c>TooltipOnWindow.Flatten</c>; the epsilons are
    /// mirrored across all three for exactly that reason). If a straightened in-plane tilt is ever
    /// reported as a regression, the change is one condition in <c>CanvasConversion.RunFlattenPass</c>
    /// — clamp only the X/Y components of the local rotation — and it must then be made in all
    /// three places at once.</para>
    ///
    /// <para>INPUT: the clamp only ever removes rotation and z, never x/y. A uGUI raycast tests a
    /// graphic's own rect, so pulling an element back into the window plane moves it TOWARDS the
    /// laser/poke plane the host registers, never away from it — the elements that were hardest to
    /// hit were the ones sticking out. Nothing re-orients with head movement and nothing touches
    /// turning: the host's own pose is never read or written by this pass.</para>
    /// </summary>
    private static bool TryConvertWindow(UIWindow window)
    {
        string name = window.name;
        // ROUND 8 — hand the pre-convert 2D blackout back FIRST (part 11). The conversion's own
        // complete render hide (CanvasConversion part 6) only records components whose `enabled`
        // was TRUE when it cleared them, so a canvas still switched off by the blackout would be
        // skipped by the hide AND by the reveal: a permanently invisible menu. Restoring here is
        // free of any visible frame — nothing renders between this statement and the reveal gate's
        // hide, which runs later in this same Update (CanvasConversion.Tick).
        ReleasePreConvertHide(window, "the conversion takes over (its own pre-reveal hide records the true state)");
        // Part 12 — and put the canvas back the way the game had it BEFORE the conversion adopts
        // it. A window we re-bound onto the flat screen's UI camera is about to be re-parented
        // under a world-space host, where its render mode is inherited from that host anyway; the
        // adoption must record the game's own state, not ours, or the release would restore a
        // Screen-Space-Camera binding to a camera that is no longer capturing anything.
        ReleaseScreenBind(window, "the float takes over — the window draws itself now");
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

            // ModBuild 231 — THE MAP STORY BOX IS A CLICK-THROUGH STORY BOX TOO, AND IT HAD AN X.
            //
            // USER RULING (2026-08-23), verbatim: "Weiterhin darf dieses Story-Fenster kein 'x'
            // haben, da man durchklicken muss." The chain below has excluded "the click-through
            // story box" since test #10 — but its test resolves Singleton<StoryController> and
            // NOTHING ELSE, i.e. the SCENARIO box. The campaign map's box is a different singleton
            // on a different canvas (MapStoryController, 'Story Canvas/Map Story Window'), so it
            // fell through to the blacklist's default and got one. The ModBuild 231 hardware log
            // says so in one line: "MODAL CLOSE (X button): attached to 'Map Story Window'".
            //
            // IT IS THE SAME KIND OF WINDOW BY THE SAME EVIDENCE. Both controllers drive the SAME
            // component — UICharacterStoryBox — and the way past a page is a click on its own
            // skipButton (UICharacterStoryBox.cs:44/95, and the log's "uGUI click: 'UI Story Box'").
            // An X on it offers to dismiss what the player is required to click through, and at the
            // quest-intro moment that dismissal is over the point of no return (see StoryComposite):
            // the party has committed, the loadout screen is up, and there is nothing to go back to.
            //
            // NO SECOND MECHANISM: this sets the SAME isStoryBox flag the scenario box sets, so it
            // joins the one no-X chain (ModBuild 230's rule for the transient announcement, applied
            // to the family it was written for) instead of getting a branch of its own.
            if (!isStoryBox && Singleton<MapStoryController>.IsInitialized)
            {
                MapStoryController mc = Singleton<MapStoryController>.Instance;
                if (mc != null && ReferenceEquals(mc.window, window))
                    isStoryBox = true;
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
            // THE MOD'S OWN VR SETTINGS WINDOW BELONGS TO THE OPTIONS FAMILY, and it must be
            // recognised BY NAME because its UIWindowID is deliberately None (ModBuild 337: giving
            // it the real OptionsSubmenu id would make ResetEscMenuToggleGroup fire for its X
            // button and take the game's own options window down with it).
            //
            // USER, 2026-09-02 after the 336 round: "Das VR-Optionsmenu aendert staendig sein
            // Groesse - das ist ok wenn man den Tab wechselt - aber es passiert auch, wenn man
            // einfach nur scrollt und das darf nicht sein."
            //
            // He is right, and the reason is mechanical. Since ModBuild 335 the pane is a
            // standalone window with CENTRE anchors, so IsFullScreenMenu is false for it, so it
            // fell through to fitContent = null -- the DEFAULT PER-FRAME content fit. That fit
            // measures the union of VISIBLE graphics every frame, and scrolling a list changes
            // which rows are visible, so the host breathed with the scroll position. The Options
            // family has been exempt from this fit since Issue 3 for a closely related reason (it
            // width-collapsed a rail-plus-panel layout); ours is the same layout and wants the same
            // exemption, plus the same height cap.
            // No null test: `window` is dereferenced unguarded either side of this line
            // (window.ID, above and below), so adding one here only teaches the compiler the
            // reference is nullable and moves the warning to those.
            bool vrOptionsWindow = MenuWindowFamily.IsModOwned(window);
            bool? fitContent = (fullScreenMenu && !escMenuWidthHug) || vrOptionsWindow
                ? false
                : (bool?)null;
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
            // ModBuild 186: a guildmaster destination converts the PANEL the flat game draws as
            // one, which may sit one level above its UIWindow — the merchant's item list is a
            // SIBLING of the shop window, not a child of it, so converting the window alone split
            // the merchant across two floats. See GuildmasterDestinations.PreferredConvertRoot for
            // the measurement (56 transforms swept for the window against 1998 for the list) and
            // for the bound that stops this from walking up to the full-screen canvas.
            rect = MapRoom.GuildmasterDestinations.PreferredConvertRoot(window, rect);
            // ModBuild 187 — A HOVER CARD MUST NOT BE PICKABLE, AND THAT IS WHY THE MOUSEOVERS
            // NEVER APPEARED. The card is floated 1.2 m ahead and then flown ONTO the icon by
            // TickHoverCards — i.e. straight into the beam that is hovering that icon. A pokeable
            // conversion carries a laser/poke collider, so the ray then hit the CARD instead of the
            // location, MapLocationInteractor's pick went null, the hover exited, the game hid the
            // popup (UIQuestPopupManager.HidePreview), the card vanished, the ray reached the icon
            // again and the whole thing started over. The 186 log is that loop verbatim: float,
            // two ticks, "game reports closed", float again — 22 rounds of it.
            // A hover card is a LABEL, not a window: it has no grab bar (181) and no X (181), and
            // now no collider either. Nothing about it was ever meant to be clicked.
            bool hoverCardPick = IsMapRoomHoverCard(window);
            ConvertedPanel? panel = CanvasConversion.Convert(rect, $"Modal_{name}", pokeable: !hoverCardPick,
                fitContent: fitContent, sortingOrder: ModalHostSortingOrder,
                diagnostic: true, // FLICKER HUNT: per-frame change-gated host/child/camera diagnostics
                useModLayer: true, transparentBackground: transparentBg,
                // Issue 3: WIDTH-hug one-shot fit for the ESC menu; issue #1: confirmation dialogs
                // one-shot-fit too. The full-screen-menu family gets the HEIGHT cap (confirmations do
                // not — the one-shot fit already hugs their compact box). Issue 5: keep the backing
                // disabled on release for the full-screen-menu family only.
                fitOneShot: oneShotFit, capHeightToCanvas: fullScreenMenu || vrOptionsWindow,
                keepBackgroundHidden: fullScreenMenu,
                // THE WINDOW FLATNESS GUARANTEE (ModBuild 193) — see the block above TryConvertWindow.
                // Unconditional: EVERY window this path floats, with no per-ID whitelist, because
                // the user's ruling is about spawnable windows as a class and a whitelist would
                // reproduce the exact failure it is meant to end (a window nobody thought of).
                flattenWindow: true,
                // THE SHARED WINDOW SIZE LAW (ModBuild 450, user ruling "Gewährleiste das"). Passing
                // the window is the WHOLE hook: SharedWindowSize.Arm decides from it whether this
                // conversion is in the shared population and reads the canvas design frame while the
                // target is still under its original parent. Unconditional, exactly like
                // flattenWindow above and for the same reason — the ruling is about a CLASS of
                // window, and a per-ID whitelist here would reproduce the failure it is meant to end.
                sharedWindow: window);

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
            // ModBuild 183: in the map room the NEWEST window always spawns dead ahead and the
            // older ones are pushed out along the arc afterwards (RelayoutMapRoomArc). 182 gave the
            // new window the highest index, so with three sticky windows already open every fresh
            // one landed 68° to the side — "so weit neben mir, dass ich es zuerst nicht bemerkt
            // habe", and the hover cards went with it, which is why the mouseovers seemed to stop
            // appearing at all. A hover card never takes an arc slot: TickHoverCards owns its pose.
            int staggerIndex = MapRoom.MapRoomDriver.Active ? 0 : Converted.Count;
            // Torbogen report: level-message windows (tutorial box / action strip) get the
            // closer, gaze-centered, view-cone-guaranteed placement; every other family keeps
            // the shared 1.2 m spawn unchanged.
            bool isLevelMsg = IsLevelMessageWindow(window);
            // CHAIN POSE CONTINUITY (user ruling 2026-08-02) — rule 2: EVERY scripted
            // level-message window (tutorial box AND help-text strip — the store is SHARED
            // across the kinds, hardware round 2026-08-02) that opens mid-chain takes the
            // previous scripted window's stored pose VERBATIM: no gaze placement, no pitch/
            // board/view-cone clamps, no overlap resolve, no one-shot facing — the player
            // approved that exact spot by leaving (or grab-moving) the previous window there;
            // only finiteness was sanity-checked (TryGetChainPose). The pose is the host
            // PIVOT = window CENTER (both hosts share the centered pivot, CanvasConversion),
            // so a differently-sized window center-aligns with its predecessor. Rule 1 — the
            // in-front, view-cone-guaranteed spawn below — applies only when no valid chain
            // pose exists: the chain's FIRST window after the scenario/teardown reset, or
            // after presence regain invalidated a stale pose. Same scale convention as
            // PlaceAtHmd (ComputeHmdPose: PanelLayout.WorldScale × board-relative shrink).
            //
            // First-open pose fix (2026-08-02): the placement inputs of a rule-1 gaze spawn, kept
            // so the ONE pre-reveal re-place can replay them against the FINAL fitted geometry
            // (see TickPoseRePlace). Left INVALID on the rule-2 branch below — a verbatim stored
            // chain pose is authoritative and must never be recomputed.
            SpawnAnchor spawnAnchor = default;
            // Re-open reverted (user ruling 2026-08-04, same-day reversal): a fresh open of a
            // NON-level-message window ALWAYS runs the rule-1 gaze spawn below — it must land
            // in the view area, whatever pose the player had parked a previous instance at.
            // Only the level-message chain keeps verbatim pose continuity (its own ruling).
            if (isLevelMsg && TryGetChainPose(window, out Vector3 chainPos, out Quaternion chainRot,
                    out string chainSetBy))
            {
                // ModBuild 189 (yaw-only ruling): the chain POSITION is verbatim and authoritative —
                // the player approved that exact spot by leaving the previous window there. The
                // ORIENTATION is not the same kind of fact: the stored rotation is whatever the
                // previous host happened to carry when the store was refreshed, which can be a
                // mid-carry hand pitch (PanelGrab's Level carry yaws AND pitches with the wrist and
                // only snaps back to upright on RELEASE). Standing it upright here costs the
                // continuity guarantee nothing — a window at the same centre, facing the same way,
                // just not tipped — and closes the one path by which a pitched window could still
                // reach the player after the tilt term was removed.
                chainRot = Upright(chainRot);
                CanvasConversion.PlaceHost(panel, chainPos, chainRot,
                    PanelLayout.WorldScale * extraScale);
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) re-floated at the stored " +
                                      $"chain pose ({chainPos.x:F2},{chainPos.y:F2},{chainPos.z:F2}) " +
                                      $"shared by all scripted windows, last set by the {chainSetBy} — " +
                                      "position continuity: the next scripted window appears exactly " +
                                      "where the previous one was read (rule 2, center-aligned verbatim).");
            }
            else
            {
                if (PlaceAtHmd(panel, extraScale, staggerIndex, isLevelMsg))
                    spawnAnchor = s_lastSpawnAnchor;
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
            // ModBuild 181: a hover card gets NO grab handle and NO X. It is not a window the
            // player owns — it flies over the symbol under the pointer and leaves with it, so a
            // drag bar would be a handle on something that is about to disappear, and an X would
            // offer to close what the hover already closes. See IsMapRoomHoverCard.
            bool isHoverCard = IsMapRoomHoverCard(window);
            // ModBuild 184: TickHoverCards writes this host's pose EVERY frame, so the reveal
            // gate's stillness criterion can never be satisfied and the card stayed render-hidden
            // until the 0.6 s deadline — i.e. for most of its life. See PoseOwnedExternally.
            panel.PoseOwnedExternally = isHoverCard;
            // ModBuild 187: and no uGUI raycaster either — the same reason the collider is gone.
            // A card that intercepts the pointer cancels the hover that is showing it.
            if (isHoverCard && panel.HostGo != null)
            {
                var caster = panel.HostGo.GetComponent<GraphicRaycaster>();
                if (caster != null)
                    caster.enabled = false;
            }
            // ModBuild 226 — A HOVER CARD GETS NO GrabbableModal AT ALL, NOT AN UNBUILT ONE.
            //
            // USER REPORT (2026-08-22, .planning/debug/leeres_fenster.jpg + the stray strips in
            // Button_getrennt.jpg), verbatim: "Als mein Mitspieler gejoint ist, kam ein leeres
            // Fenster auf - sowas soll per se niemals passieren."
            //
            // WHAT WAS WRONG. Since ModBuild 181 the hover card correctly skipped `grab.Build(...)`
            // — but the object was still CONSTRUCTED here and still stored in `WindowPanel.Grab`
            // below, so every consumer that null-checks `wp.Grab` saw a grab. Two of them then call
            // `GrabbableModal.PlaceFrameAt` (the one pre-reveal re-place in
            // `ModalFallback.9.Spawn.TickPoseRePlaceOne`, and `RefloatOpenWindows` on presence
            // regain), and `PlaceFrameAt` calls `EnsureFrame()` unconditionally — which BUILDS the
            // holder, the brass bar, its collider and the grab zone. With `_panel` still null
            // (only `Build` sets it) that bar is:
            //   * never sized       — `GrabbableModal.Tick` returns on `_panel == null` before it
            //                         ever reaches SyncBar, so the strip keeps the constructor's
            //                         0.2 x 0.004 default;
            //   * never ordered     — RegisterOrderFollower got a null panel;
            //   * never render-hid  — AddRenderRoot got a null panel, so the panel's reveal gate
            //                         cannot hide it and it is visible from the frame it is made;
            //   * never MOVED with the card — TickHoverCards writes the HOST pose every frame, the
            //                         bar sits at the gaze pose PlaceFrameAt gave it.
            // i.e. exactly the reported artefact: a tan grab bar hanging in mid-air with no window
            // on it. The ModBuild 225 hardware log names the count and the identity: 204 lines of
            // `MODAL GRAB: 'Menu' is now a grabbable/scalable world element` — 'Menu' is
            // `GrabbableModal._logName`'s FIELD INITIALISER, the value it carries when `Build` (the
            // only writer) never ran — each of them immediately after a
            // `MODAL WINDOW: 'UI Quest Preview Popup' ... floated` line, and that window floated and
            // released 244 times in the session.
            //
            // THE FIX IS THE ABSENCE. No grab object, so `wp.Grab` is null, so every one of those
            // call sites takes its existing `grab != null` branch and no frame is ever built. Both
            // call sites already carry that null check (Spawn: `if (grab != null) grab.PlaceFrameAt`
            // with a `CanvasConversion.PlaceHost` else-branch; Convert: `if (wp.Grab != null)` with a
            // `PlaceAtHmd` else-branch), so the hover card keeps the same POSE behaviour it had — it
            // simply stops manufacturing a handle for itself.
            //
            // REJECTED: guarding `EnsureFrame` on `_panel == null` inside GrabbableModal. That is the
            // right belt-and-braces and it is where a future reader will look, but GrabbableModal.cs
            // is owned by another lane this round; the cause is here, where the object that must not
            // exist is made.
            GrabbableModal? grab = null;
            // TRANSPARENCY ROUND: the per-menu coplanar DEPTH MASK that used to be requested here
            // (gated to the ESC/Options family + confirmations + results) is gone. Its job was
            // "transparent HUD sitting BEHIND the floated menu must be occluded by it", and it did
            // that by stamping the menu plane's depth per visible graphic RECT - which is why the
            // panels behind a menu came away with hard-edged rectangular holes wherever the menu
            // itself was transparent (.planning/debug/initiativereihenfolge_transparenz.png). The
            // menu is now simply PAINTED in its distance order among all converted panels
            // (CanvasConversion.8.Order.cs): HUD it is in front of is painted first and covered,
            // HUD it is behind is painted after it and covers it, and nothing writes depth, so a
            // transparent menu pixel shows whatever is genuinely behind it.
            if (!isHoverCard)
            {
                grab = new GrabbableModal();
                grab.Build(panel, extraScale, name);
            }
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
            // ALSO EXCLUDED (user ruling 2026-08-02): scripted tutorial/level-message windows
            // — the player MUST engage with a tutorial hint (its own dismiss button or the
            // action it demands); an X let them skip instruction chains and strand triggers.
            // ModBuild 185 — AND THE MAP ROOM'S CHARACTER SCREEN. Closing it did not close the
            // screen, it SPLIT it: the assembly window nested inside the party display lost its
            // parent and floated on its own. See IsMapRoomPermanent for the log lines that show it.
            bool isMapRoomPermanent = IsMapRoomPermanent(window);
            // ModBuild 230 — AND A TRANSIENT ANNOUNCEMENT, user ruling (a): "Solche flüchtigen Infos
            // sollten nicht mit dem 'x' schließbar sein, es soll eher als Dialog behandelt werden was
            // damit geschlossen wird, wenn der user draufklickt." The X is not merely redundant on
            // such a window, it is the trap he fell into: he clicked the info (which IS the dismiss),
            // the info went, and the X stayed behind as the only thing left in the frame. It joins
            // the existing no-X chain rather than getting a mechanism of its own, and it is the ONLY
            // member of that chain that gets something back in exchange — the full-host click
            // catcher below. See IsTransientAnnouncement for the decompiled evidence and for the one
            // candidate family (the FTUE intro screens) deliberately NOT enrolled.
            bool isTransient = IsTransientAnnouncement(window);
            // ModBuild 236 — AND THE PRE-SCENARIO LOADOUT SCREEN, for the results-window reason and
            // for the story-box reason at once.
            //
            // (1) IT IS THE WINDOW THE QUEST INTRO IS NOW TOLD IN. StoryComposite parks the story
            //     window's whole content into it, so the user's ruling about the story box applies to
            //     it directly: "Weiterhin darf dieses Story-Fenster kein 'x' haben, da man
            //     durchklicken muss." The X decision is taken HERE, at convert time, and this window
            //     converts before the story box exists, so no live condition could do it later.
            // (2) CLOSING IT STRANDS THE PLAYER, WHICH IS TRUE WITH OR WITHOUT THE COMPOSITE. The X
            //     runs CloseFloatedWindow to UIWindow.Escape/Hide; UILoadoutManager.OnHidden only
            //     calls ClearEvents, decompiled :377 and :397-415, and nothing re-opens the screen,
            //     while the single-player continue button is a CHILD of it, :88-95. Same shape as the
            //     results-window exclusion above.
            //
            // IDENTITY IS IS-A, NOT CONTAINMENT: UILoadoutManager carries a RequireComponent for
            // UIWindow and ControllerInputArea, :18, and caches its window with GetComponent in
            // Awake, :68, so the manager and the window are ONE GameObject by construction.
            bool isLoadoutScreen = window.GetComponent<UILoadoutManager>() != null;
            // ModBuild 381 — AND ANY WINDOW WHERE A DECISION IS MANDATORY, WHICH IS WHAT TURNS THE
            // SEVEN CLAUSES ABOVE FROM A BLACKLIST INTO A RULE.
            //
            // USER REPORT (2026-09-03), verbatim: "Das Fenster 'Begegnung!' hat ein 'X' zum
            // schließen. Das darf nicht sein - hier MUSS eine Entscheidung getroffen werden, drückt
            // ein User das X kommt das einem Deadlock gleich. Prüfe nochmal das jedes Fenster bei
            // dem zwingend eine Entscheidunge getroffen werden muss auch kein X hat."
            //
            // WHAT WAS WRONG WITH THE GATE, not just with one window. Every clause above is an
            // exclusion, so the default for a window nobody has classified is TO GET AN X. The
            // comment above defends that by pointing out the X runs the same CloseFloatedWindow the
            // escape chord already applies without a whitelist — which is an argument about the
            // MECHANISM being uniform, and the mechanism is. The CONSEQUENCE is not: on most windows
            // a Hide() is a close, and on the encounter window it is a campaign map with no HUD, no
            // quest log, no party display, actions paused and the encounter button greyed out
            // forever. The 380 log has the defect as a line — 7846, "MODAL CLOSE (X button):
            // attached to 'UI Event Window' (ID EventsPanel)".
            //
            // IsMandatoryDecision carries the whole argument and the decompiled proof of the
            // strand, plus the honest limits of its second term. It is TWO terms: an identity match
            // on the encounter window (which is what makes the report provably fixed, because the
            // second term's input is serialized in a prefab and cannot be read from the decompile),
            // and the derived net "the mod's X must not do what the game's own ESC key refuses to
            // do" (escapeKeyAction None), with one exemption for the mod's own settings pane.
            bool isMandatoryDecision = IsMandatoryDecision(window, out string mandatoryReason);
            if (!isResultsPanel && !isStoryBox && !isRewardShowcase && !isLevelMsg && !isHoverCard
                && !isMapRoomPermanent && !isTransient && !isLoadoutScreen && !isMandatoryDecision)
            {
                ModalCloseButton.Attach(panel, window);
                // HW-VERIFY
                // THE AUDIT IS ONLY RE-CHECKABLE IF BOTH VERDICTS PRINT. Until this build only the
                // no-X branch logged, at VRLog.Info — the DEBUG tier, not printed at the shipped
                // default — so a hardware log could not say which windows got a cross, and the
                // user's "prüfe nochmal das JEDES Fenster …" could only ever be answered by reading
                // source. The TIER is what changed here and in the else-branch; no existing text was
                // reworded. ESCAPE POLICY is the field that decides the open question: it is the
                // only way to learn what escapeKeyAction each window actually carries, because the
                // value is [SerializeField] and lives in the asset bundles. If a window in this
                // list reads 'None' the derived term would have caught it unaided; if 'UI Event
                // Window' had read 'None', term 1 was belt-and-braces rather than the load-bearing
                // half. One line per window per conversion, never per frame.
                VRLog.Note("WorldUI", $"MODAL WINDOW X AUDIT: '{name}' (ID {window.ID}) floats WITH an X — "
                                      + "no exclusion matched, so the mod judges this window safe to "
                                      + "close: hiding it strands nothing that cannot be reopened. "
                                      + $"ESCAPE POLICY escapeKeyAction={EscapePolicyOf(window)} (the "
                                      + "game's own ESC verdict for this window; None means the game "
                                      + "would not close it on ESC, which since 2026-09-05 is NOT on "
                                      + "its own a reason to withhold the cross — see the "
                                      + "MODAL WINDOW X RESTORED line, and IsMandatoryDecision's "
                                      + "second exemption, for the two windows that answer it). "
                                      + "TERMS TESTED, all false here: results panel, story box, "
                                      + "reward showcase, level message, hover card, map-room "
                                      + "permanent, transient announcement, loadout screen, mandatory "
                                      + "decision. HOW TO READ IT: this line and the 'floats WITHOUT "
                                      + "an X' line are exhaustive over converted windows — a window "
                                      + "that appears in NEITHER was never converted at all, which is "
                                      + "a different question from whether it got a cross.");
                if (MapRoom.MapRoomDriver.Active && MapRoom.GuildmasterDestinations.IsDestination(window))
                    // HW-VERIFY
                    // ITEM 3, 2026-09-05 — THE ANSWER LINE, and it is deliberately its OWN token
                    // rather than a clause inside the census above, because the census answers "which
                    // windows got a cross" and this answers "did the ONE change of this round reach
                    // the ONE family it was made for". One line per destination conversion (two or
                    // three in a session's worth of map room), never per frame.
                    //
                    // THE FALSIFIER, and it is not "this line is absent". Absent means the merchant
                    // was never converted at all this session, which is a different failure and is
                    // read off the 'floats WITHOUT an X' census. The reading that means THE FIX IS
                    // INERT is this window still appearing on the 'floats WITHOUT an X' line with
                    // the derived-net reason ("the GAME ITSELF refuses to close this window on
                    // ESC"), because that is the exemption not firing. The reading that means THE
                    // FIX IS WRONG is this line present and a MODAL CLOSE (X button) on it followed
                    // by the party display's character slots staying dead — that would be the
                    // ModBuild 184 strand, i.e. LeaveMode's dispatch not landing, and its own line
                    // is 'MAP TABLE BUTTON … is INACTIVE'.
                    VRLog.Note("WorldUI", $"MODAL WINDOW X RESTORED: '{name}' (ID {window.ID}) is a "
                                          + "GUILDMASTER DESTINATION (matched by component off "
                                          + "UIGuildmasterHUD's own serialized references, not by name "
                                          + "and not by containment) and it now carries a close cross "
                                          + "again — user item 3, 2026-09-05: 'Der Händler und co. "
                                          + "haben kein X mehr zum schließen. Will ich aber haben.' "
                                          + $"ESCAPE POLICY escapeKeyAction={EscapePolicyOf(window)}: "
                                          + "the derived net that used to withhold the cross fires for "
                                          + "EVERY window in this room, so it could never separate this "
                                          + "family from the encounter window. WHAT MAKES IT SAFE IS "
                                          + "NOT AN ARGUMENT BUT SHIPPED CODE: the cross runs "
                                          + "ModalFallback.CloseFloatedWindow, which is the SAME close "
                                          + "the mode's own table cap performs on a second press and "
                                          + "the SAME close the point-of-no-return sweep performs on "
                                          + "all five destinations — float released, the mode's own "
                                          + "Exit run through GuildmasterDestinations.LeaveMode (which "
                                          + "is what takes the party display back out of selection "
                                          + "mode), then the game window hidden. NOTHING GOES ON THE "
                                          + "WIRE: closing a window is local presentation, and the "
                                          + "purchase, blessing or enhancement this destination may "
                                          + "have committed was committed by ITS own button.");
            }
            else
                // HW-VERIFY
                // Promoted from VRLog.Info (DEBUG tier, not printed at the shipped default) to
                // VRLog.Note (Info tier, printed) so the exclusion side of the audit survives the
                // ModBuild 331 quiet-log mapping. The TEXT is unchanged and only appended to.
                VRLog.Note("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) floats WITHOUT an X " +
                                      $"({(isResultsPanel ? "results window — native buttons are the only exit"
                                          : isRewardShowcase ? "reward showcase — native continue is the only exit (its callback releases the message pump)"
                                          : isLevelMsg ? "tutorial/level message — the player must engage, not dismiss (its own button/action is the only exit)"
                                          // ROUTED, NOT HARDCODED (ModBuild 194): the permanent set is
                                          // no longer one window. Spelling out the character screen's
                                          // reason for the quest log would make a hardware log assert
                                          // the wrong cause, which is exactly the kind of line this
                                          // project has had to retract before.
                                          : isMapRoomPermanent ? MapRoomPermanentReason(window)
                                          : isTransient ? TransientAnnouncementReason(window)
                                          : isLoadoutScreen ? "pre-scenario loadout screen — the quest intro is told IN this window by StoryComposite, and closing it would hide the window its own Enter Dungeon button is a child of"
                                          // ModBuild 381: LAST in the chain, so it can only be the
                                          // printed reason when no older exclusion already owned this
                                          // window. A window matched by BOTH is reported under the
                                          // older term, which is correct — the reason must name what
                                          // actually decided, and the clauses are evaluated in order.
                                          : isMandatoryDecision ? mandatoryReason
                                          : "click-through story box")})"
                                      // ModBuild 381: the same ESCAPE POLICY field the WITH-an-X line
                                      // carries, so the two halves of the audit are comparable. It is
                                      // the only route to the serialized escapeKeyAction values, and
                                      // the answer to "would the derived term have sufficed on its
                                      // own" is read off this field across both lines, not inferred.
                                      + $" ESCAPE POLICY escapeKeyAction={EscapePolicyOf(window)}."
                                      // NOT ONE SENTENCE FOR BOTH CASES: a map-room permanent window is
                                      // ALSO skipped by CloseTopModal and refused by CloseFloatedWindow
                                      // (ModalFallback.7.Close.cs:69-77, :101-108), so claiming the chord
                                      // still closes it would be this line asserting a mechanism that is
                                      // false on its own most common branch.
                                      // THE THREE BRANCHES ARE IN CloseTopModal'S OWN EVALUATION
                                      // ORDER (ModalFallback.7.Close.cs): it tests IsMapRoomPermanent
                                      // and `continue`s, THEN tests IsMandatoryDecision and redirects,
                                      // and only then closes. A window matching both must be reported
                                      // under the term that actually runs first, or this line asserts
                                      // a behaviour the code does not have.
                                      + (isMapRoomPermanent
                                          ? " THE ESCAPE CHORD ALSO SKIPS this window and"
                                            + " CloseFloatedWindow refuses it; the chord walks PAST it to"
                                            + " the next floated window, so nothing here can trap the room."
                                          : isMandatoryDecision
                                          ? " THE ESCAPE CHORD IS REDIRECTED for this window (ModBuild"
                                            + " 381): it does NOT close it and does not skip it either —"
                                            + " it releases the float and raises the 2D composite, so the"
                                            + " player always has a way forward and no waiter is ever"
                                            + " stranded. Look for MODAL ESCAPE CHORD REDIRECTED."
                                          : " THE ESCAPE CHORD IS UNCHANGED and still reaches this window:"
                                            + " only the drawn cross is withheld, which is the precedent"
                                            + " every other no-X window above already sets."));
            // ModBuild 230: the transient's replacement for the X. Built AFTER the X decision and
            // before the WindowPanel so a failure to build it cannot cost the window its float.
            if (isTransient)
                AttachTransientDismiss(panel, window);

            var wp = new WindowPanel
            {
                Window = window,
                Panel = panel,
                FullScreenMenu = fullScreenMenu,
                // Issue #1: ESC menu + confirmations one-shot-fit → the 5b block re-derives their
                // board-relative scale from the fitted (shrunk) width so they render board-sized.
                OneShotFitted = oneShotFit,
                Grab = grab,
                ExtraScale = extraScale,
                // First-open pose fix: rule-1 spawns carry their placement inputs (rule-2 chain
                // poses stay invalid → never re-placed); the latch arms the ONE re-place.
                SpawnAnchor = spawnAnchor,
                // Item 6: reachable menus stay floated in parallel even when the game's single-window
                // toggle hides a sibling; cache the CanvasGroup used to re-assert their visibility.
                // ModBuild 180: in the 3D map room EVERY floated window is sticky — the game's
                // single-window discipline (open the merchant, the temple disappears) is wrong for
                // a room where the windows are objects on a table. See MapRoomParallel.
                // ModBuild 230 — AND A TRANSIENT ANNOUNCEMENT IS NEVER STICKY, WHICH IS THE ROOT
                // CAUSE OF THE REPORT AND NOT A TIDY-UP. The ModBuild 229 log has the whole sequence:
                // the unlock popup floats in the map room, the player clicks the info, the game runs
                // its own dismiss and calls window.Hide() ("UIWindow hidden: 'UI Unlock Locations
                // Flow Manager'" / "hidden — untracked"), and the float SURVIVED that — 174 log lines
                // later the same window is being hovered and grabbed. It survived because
                // MapRoomParallel makes every non-confirmation window in the map room sticky, and
                // sticky means "the release loop keeps this floated when the game hides it". That
                // rule was written for windows the player OPENS and wants side by side (the merchant
                // and the temple, user ruling of ModBuild 180). An announcement the game takes away
                // is the opposite case: keeping it is keeping a frame around content that has left.
                // The window stays NON-BLOCKING (MapRoomParallel is untouched, so IsBlockingWindow
                // still reads false and no ModalUI lock is raised) — only the stickiness goes.
                // ModBuild 341 — THE STICKY QUESTION IS NOT THE BLOCKING QUESTION, and asking both
                // with one membership test is how a per-site verdict silently becomes a policy.
                // MenuWindowFamily.IsGameOwnedMenu is deliberately the GAME-ids-only half:
                // stickiness defends a window against the ESC menu's single-window ToggleGroup, and
                // ModBuild 337 took the mod's own settings window OUT of that group precisely so the
                // two windows stop closing each other. Making it sticky would buy it nothing and
                // cost the empty-window ruling: only UserClosing ever drops a sticky float, so any
                // path that hides the window without CloseFloatedWindow would leave the frame
                // standing with nothing in it. The reasoning is written out on that method.
                Sticky = (MenuWindowFamily.IsGameOwnedMenu(window) || MapRoomParallel(window))
                         && !isTransient && !IsIntroductionWindow(window),
                Transient = isTransient,
                // ModBuild 230 (liveness): the clock every grace and dwell in TickWindowLiveness is
                // measured from, and the "had been standing for" term of its release line.
                FloatedAt = Time.unscaledTime,
                HoverCard = isHoverCard,
                WindowCanvasGroup = window.GetComponent<CanvasGroup>(),
                // Item 6 (empty-shell fix): cache the window's own Canvas so ReassertStickyVisible can
                // re-enable it after a `_disableCanvas` UIWindow disables it on its hide-fade complete.
                WindowCanvas = window.GetComponent<Canvas>(),
                // User #12: Sieg/Niederlage window — thumbstick-Y scrolls its scroll area
                // (target found lazily in TickResultsStickScroll; list content pools in late).
                IsResults = isResultsPanel,
                // Chain continuity: seed the message key so the first key-change capture in
                // TickLevelMessageChain fires only on a genuine NEXT message, not on the tick after
                // this convert (null → current key would just re-store the just-placed pose).
                // Null for every non-level-message window.
                LastLevelMessageKey = CurrentLevelMessageKey(window),
            };
            Converted.Add(wp);
            // CHAIN CONTINUITY: every scripted PLACEMENT — the rule-1 first spawn just as much
            // as a rule-2 verbatim re-float — makes THIS window the shared chain anchor. This
            // is what welds the kinds together: the first tutorial box's rule-1 pose is stored
            // immediately, so a help-text strip opening moments later (box still floating, no
            // key change, no release edge yet) already inherits the box's spot instead of
            // rule-1-spawning at its own gaze position (the two-places bug).
            if (isLevelMsg)
                StoreChainPose(wp);
            // ModBuild 341: one line per mod-owned menu window, the first time it actually floats —
            // so a hardware log proves the classification RAN, not merely that the code exists.
            MenuWindowFamily.AnnounceFloat(window);
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
    ///
    /// <para><b>THIS SURVIVES THE ModBuild 149 RECALL DELETION, deliberately, and it was checked
    /// against the ruling rather than assumed to be safe.</b> What the user ruled out is a window
    /// moving ON ITS OWN — "nach einer Zeit", with the player doing nothing. This fires on exactly
    /// one thing: the headset coming back on. That is a deliberate physical act by the player, it
    /// cannot happen while they are looking at the window, and the state it recovers from is one
    /// the mod genuinely cannot see through (the player may have walked or turned with the HMD
    /// off, so the parked spot can be behind them). It is also already bounded by the SAME
    /// ownership rule as the recall was: a window the player has grab-moved is never touched here.
    /// With the recall gone this is now one of the three things that can rescue a lost window, and
    /// the only one that puts it back rather than closing it.</para>
    ///
    /// <para>IF THE USER REPORTS WINDOWS JUMPING AFTER A DOFF/DON, this is the path — the log line
    /// below names every window it moves — and the answer would then be to delete it too, not to
    /// re-tune it.</para>
    /// </summary>
    internal static int RefloatOpenWindows()
    {
        // CHAIN POSE CONTINUITY × presence regain (the stale-pose fix STAYS): a chain pose
        // captured around a doff/don is untrustworthy — the user may have physically moved or
        // turned while the HMD was off, so the parked spot can sit behind them, and a pose
        // captured MID-donning can be junk. Drop the store (rule 1) and re-seed it below from
        // the fresh in-front pose of any level-message float being re-placed, so continuity
        // resumes from where the chain is NOW readable.
        ResetChainPose("presence regained — a pose captured around the doff/don is untrusted");
        int count = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.Panel.IsAlive)
                continue;
            // USER-OWNED POSE (user ruling 2026-08-04): a window the player has grab-moved is
            // NEVER auto-repositioned while it stays open - not even on presence regain. A
            // brief doff/don (Virtual Desktop presence flickers included) used to re-yank
            // every deliberately parked window back to the gaze through this very path, which
            // is the same jumping the recall skip removes. The player knows where they put
            // it; the X and the escape chord remain the rescue if it is genuinely lost.
            // ...AND SO IS A WINDOW A PEER MOVED (ModBuild 226). `UserMoved` latches only on a LOCAL
            // grip, so until now a doff/don yanked a SHARED window — one a remote player had
            // deliberately dragged for the whole room — back to this player's gaze, re-faced it, and
            // then published that pose to everybody as this client's own move. The user's ruling for
            // record 21 is that a shared window's pose changes for exactly two reasons, a hand here
            // or a hand on a peer's client; a headset coming out of standby is neither. The argument
            // for the skip is `UserMoved`'s with "a user" widened to "any user".
            if (wp.Grab != null && (wp.Grab.UserMoved || wp.Grab.PeerPlaced))
            {
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{(wp.Window != null ? wp.Window.name : "<window>")}' " +
                                      "NOT re-floated on presence regain - " +
                                      (wp.Grab.UserMoved
                                          ? "the player moved it, so its pose is theirs"
                                          : "a PEER placed it and it is shared, so its pose belongs to "
                                            + "the room and not to this headset's standby")
                                      + " (user ruling: parked windows stay put).");
                continue;
            }
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
                placed = PlaceAtHmd(wp.Panel, wp.ExtraScale, 0, levelMessage);
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
    /// resize), but carry NO X close button — the only way out must remain their
    /// native continue/retry/exit buttons.
    ///
    /// <para>THEY USED TO PARTICIPATE IN THE LOST-MENU RECALL and no longer do: the recall is
    /// deleted outright on the user's ModBuild 149 ruling (the block is at the top of
    /// <c>ModalFallback.6.MenuGuard.cs</c>). A results window carried out of view now stays where
    /// it was put. That is the intended behaviour and it is not a lock: the modal escape chord
    /// closes the top float from anywhere, presence regain re-floats any window the player has not
    /// grab-moved, and the window's own native buttons are reachable wherever it is standing.</para>
    /// </summary>
    private static bool IsResultsPanel(UIWindowID id) =>
        id == UIWindowID.ResultsPanel || id == UIWindowID.AdventureCompletionPanel;

    // THE PLAYER-REACHABLE MENU FAMILY (item 3b) HAS MOVED to MenuWindowFamily.cs, unchanged in
    // membership. It lived here as a private HashSet<UIWindowID> and was asked, in six expressions
    // across five files, two DIFFERENT questions: "is this a menu the player opened, so it must not
    // disturb play?" and "does the game's single-window discipline hide this behind a sibling, so
    // our float must be sticky?". Those are MenuWindowFamily.IsPlayerMenu and
    // .IsGameOwnedMenu now, and only the first of them answers for the mod's own settings
    // window (whose UIWindowID is None by ModBuild 337's deliberate choice). Keeping the set here
    // is what made that window unrepresentable: an id-keyed set cannot hold a window with no id.

    /// <summary>
    /// MULTIPLAYER ROSTER FAMILY — non-blocking like <see cref="MenuWindowFamily.IsPlayerMenu"/>, but NOT
    /// sticky (they must close exactly when the game closes them; they are not part of the ESC
    /// menu's ToggleGroup fight).
    ///
    /// ROOT CAUSE (user report 2026-08-02, HOST, hardware log
    /// <c>.planning/debug/remote/LogOutput.log</c>): after the host assigned a mercenary to the
    /// other player, HIS OWN item fan could not be opened at all any more — the log shows
    /// <c>[Cards] Board: laser press on 'PileStack_Items' SUPPRESSED — blocking modal open</c>
    /// repeating for the rest of the session, and the cause one screen earlier:
    /// <c>MODAL FALLBACK: window 'UI Multiplayer Select Player Submenu_unified'
    /// (ID MutiplayerPlayerPicker)</c> → <c>MODAL FALLBACK ASSERTED: … blocking=True</c> →
    /// <c>Modal commit-block ENGAGED</c>, with NO matching RELEASED line anywhere after it (the
    /// GAME keeps that window open while it waits for the other seats, so the mod's release loop
    /// correctly never fired — the classification, not the lifecycle, was wrong).
    ///
    /// The picker is the host's "who plays this mercenary" chooser and
    /// <see cref="UIWindowID.MutiplayerHeroAssignPanel"/> is the assignment roster: both are
    /// ADMINISTRATIVE surfaces about OTHER players' seats. The local player's own board, piles
    /// and item fan have nothing to do with them, the scenario keeps running underneath, and
    /// their targets self-gate through the game's own seams. Treating them as commit-blocking is
    /// the pause-menu bug all over again (both rounds are recorded on
    /// <see cref="BlockingWindowModalActive"/>) — with the extra sting that it LATCHES for as
    /// long as the roster window is up, i.e. potentially the whole session.
    ///
    /// Deliberately NOT included: <c>MutiplayerConfirmationBox</c> — a real yes/no confirmation
    /// dialog, i.e. exactly the family whose stray near-miss trigger must not land on the cards
    /// behind it. The "Warten, bis alle Spieler Söldner zugewiesen haben" banner is not a
    /// UIWindow at all (<c>UIMultiplayerNotifications</c> → <c>UINotificationManager</c>), so it
    /// never reached this classification in the first place.
    /// </summary>
    private static readonly HashSet<UIWindowID> MultiplayerRosterMenus = new()
    {
        UIWindowID.MutiplayerPlayerPicker,
        UIWindowID.MutiplayerHeroAssignPanel,
    };

}
