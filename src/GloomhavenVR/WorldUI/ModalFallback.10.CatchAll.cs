using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// ModalFallback part 10 (see the split rules in ModalFallback.1.Core.cs:13-22): the
// UNKNOWN-WINDOW CATCH-ALL + the two deadlock-insurance enrollments (reward showcase,
// GlobalErrorMessage). NOTE on compile order: the ordinal filename sort lands "…10…"
// BETWEEN parts 1 and 2 ('.' < '0'), not after part 9 — which is fine, and the reason
// this part may exist at all: it contributes ONLY NEW members and no nested types, so
// the relative member order of the original parts 1-9 (the load-bearing property the
// numbering protects) is unchanged wherever this file sorts.

internal static partial class ModalFallback
{
    // =====================================================================================
    // CATCH-ALL for unknown scenario windows (deadlock INSURANCE).
    //
    // THE HOLE THIS CLOSES: FallbackIds tracks the 24 enum-known modal IDs and three polls
    // cover the known ID-less deadlockers — but any UIWindow whose UIWindowID is
    // scene-serialized to a value this class does not know (usually the enum default,
    // None) opens on the HIDDEN 2D stack while the game waits for its click: a SILENT
    // deadlock with no VR-side symptom at all. This exact class of bug shipped once
    // already (ItemCardPicker, the item-surrender flow — see CardsGameApi.OpenItemPicker).
    // Enumerating windows can never be complete against future game patches, so the
    // choke point (UIWindow_Transition_Patch sees EVERY window transition) now feeds a
    // catch-all: an unknown window that stays open in a scenario is floated generically
    // after a short grace. The DurabilityPanel rule is the design intent: a wrongly-
    // floated window is recoverable (grab bar, X, escape chord, attributable Warn log);
    // a dropped one is a silent deadlock.
    //
    // RACE HANDLING (the grace): explicit handlers must always win. A freshly shown
    // window may be claimed by the decision dock, adopted by a surface conversion or
    // picked up by the Cards item flow one or more ticks AFTER its Show transition
    // (DecisionDockSurface converts level-triggered with its own ClaimGraceSeconds;
    // the Cards driver reads the pickers in its own update). So an unknown window only
    // joins the generic open set once it has been continuously open for
    // CatchAllGraceTicks ticks AND passes every exclusion LEVEL-TRIGGERED on the tick it
    // would join — a claim/adoption that arrives later still wins, because the
    // exclusions are re-checked every tick and windows already floated release the
    // moment they leave OpenWindows (the part-4 release loop).
    // =====================================================================================

    /// <summary>Ticks an unknown window must stay open before the catch-all floats it —
    /// explicit handlers (decision dock, surfaces, Cards flows) claim within this window.</summary>
    private const int CatchAllGraceTicks = 2;

    /// <summary>Unknown shown windows → the Time.frameCount of their Show transition.</summary>
    private static readonly Dictionary<UIWindow, int> UnknownShown = new();

    /// <summary>
    /// HOTFIX (hardware round 2026-08-01, "zwei leere Fenster flattern"): per-instance verdict
    /// cache for <see cref="IsKnownHudWindow"/> — the component walk over e.g. the whole flat
    /// hand subtree is far too heavy to repeat per tick (the un-cached catch-all measured
    /// ~1000 ms/tick converting 'Cards Hands Manager'). Pruned with <see cref="UnknownShown"/>.
    /// </summary>
    private static readonly Dictionary<UIWindow, bool> HudVerdict = new();

    /// <summary>Churn fuse: floats per window NAME within the rolling window.</summary>
    private static readonly Dictionary<string, (int Count, float WindowStart)> FloatChurn = new();

    /// <summary>Window names the churn fuse suppressed for the rest of the session.</summary>
    private static readonly HashSet<string> ChurnSuppressed = new();

    /// <summary>A window name floating more than this often within <see cref="ChurnWindowSeconds"/>
    /// is NOT a stuck decision — decisions open once and wait. It is a self-cycling HUD banner
    /// (the observed Notification/PhaseBanner pattern: show→hide every few seconds), and every
    /// re-float pays a full conversion. Suppress it for the session (one Warn) — the manual A/X
    /// screen chord still reaches it, and the Warn drives explicit enrollment/exclusion.</summary>
    /// <remarks>INTERNAL since ModBuild 234, and the visibility is the point: two other rules bound
    /// themselves by this number and both used to do it in prose. <c>StoryComposite
    /// .MaxWithdrawCycles</c> derives 2 from it ("1 + cycles ≤ 3"), and
    /// <see cref="SubViewRevival.MaxRevivalsPerWindow"/> now COMPUTES itself from it rather than
    /// repeating the arithmetic in a comment that the next edit of this constant would silently
    /// falsify.</remarks>
    internal const int ChurnMaxFloats = 3;

    /// <remarks>INTERNAL for the same reason: <see cref="SubViewRevival"/> uses this literal window
    /// so the two fuses cannot drift apart about what "recently" means for the same window.</remarks>
    internal const float ChurnWindowSeconds = 60f;

    /// <summary>Per-window-name Warn latch: ONE "enroll it explicitly" line per window type.</summary>
    private static readonly HashSet<string> CatchAllWarned = new();

    /// <summary>Scratch for pruning <see cref="UnknownShown"/> (allocation-free steady state).</summary>
    private static readonly List<UIWindow> UnknownScratch = new(4);

    /// <summary>
    /// IDs the catch-all must NEVER float because the mod already handles them
    /// deliberately elsewhere (the converted/passive window sets, class doc of
    /// ModalFallback.1.Core.cs). The four hover-driven passives carry post-mortems:
    /// HelpBox (test #16) and TextInfoPanel (test #18) each became a SELF-SUSTAINING
    /// ModalUI lock when treated as modal — asserting ModalUI stops the board hover
    /// that is their ONLY hide path — and TrapInfoPanel is the UIPropInfoPanel
    /// trap/hazard/terrain/quest-item hover card (all four Show* methods fire
    /// exclusively from hover, see the FallbackIds audit). DoorInfoPanel /
    /// MapNodeInfoPanel are the same hover-popup family. The remainder are windows
    /// other VR conversions own: ConfirmationBox (DialogSurface; the Dialogs=off case
    /// is already routed by IsFallbackWindow), the stat panels (StatPanelSurface),
    /// CardHolder (Cards module), QuestTracker/MapObjectiveManager (HUD conversions).
    /// Town windows (Village/Shop/EnhancementShop/…) are deliberately NOT excluded:
    /// they cannot legitimately open mid-scenario, and if one ever does, floating it
    /// is the recoverable outcome (the DurabilityPanel rule).
    /// </summary>
    private static readonly HashSet<UIWindowID> CatchAllKnownHandled = new()
    {
        UIWindowID.HelpBox,
        UIWindowID.TextInfoPanel,
        UIWindowID.TrapInfoPanel,
        UIWindowID.DoorInfoPanel,
        UIWindowID.MapNodeInfoPanel,
        UIWindowID.ConfirmationBox,
        UIWindowID.ActorStatPanel,
        UIWindowID.EnemyCurrentTurnStatPanel,
        UIWindowID.CardHolder,
        UIWindowID.QuestTracker,
        UIWindowID.MapObjectiveManager,
    };

    /// <summary>
    /// Observe EVERY window transition (called from <see cref="OnWindow"/>, one line
    /// there): track unknown shown windows so the per-tick catch-all can grace-float
    /// them. Tracking is UNCONDITIONAL like the Open set (test #10 lesson — windows
    /// opened during loading must survive into the scenario); every judgement call
    /// (scenario gate, exclusions, config) is level-triggered in
    /// <see cref="TickCatchAll"/> so nothing is lost to an edge-time race.
    /// </summary>
    private static void CatchAllObserve(UIWindow window, bool shown)
    {
        if (!shown)
        {
            UnknownShown.Remove(window);
            // A refusal is scoped to ONE open of ONE window. Both rows of the table sit on game
            // SINGLETONS (UIMultiplayerLockOverlay, UIReadyToggle), so the same instance comes back
            // on the next open — without this the next refusal would find its edge already set and
            // print nothing, and a re-open with no claim in force would fake a lapse warning about a
            // parker that never had a chance to run. See FloatRefusalTable.Forget.
            FloatRefusalTable.Forget(window);
            // ModBuild 234 — and the retraction bridge, for the same reason and with the same scope:
            // it is about ONE float of ONE open of this window, and a close ends it. Without this the
            // set would also grow for the whole session, one entry per closed window.
            SubViewRetracted.Remove(window);
            return;
        }
        if (IsFallbackWindow(window.ID))
            return; // the explicit path owns it
        if (!UnknownShown.ContainsKey(window))
            UnknownShown[window] = Time.frameCount;
    }

    /// <summary>
    /// Per-tick catch-all step (called from Tick after the three explicit polls, BEFORE
    /// the sticky/convert logic reads <see cref="OpenWindows"/>): prune dead/closed
    /// unknown windows, then append every eligible one to <see cref="OpenWindows"/> so
    /// the ENTIRE existing machinery treats it like any tracked modal — float via
    /// TryConvertWindow (grab bar + X), Failed → flat screen, ModalUI (unknown IDs are
    /// never in <see cref="NonBlockingMenus"/>, so they count as blocking), escape
    /// chord, release-on-close. Also runs the two explicit enrollment polls (reward
    /// showcase, GlobalErrorMessage) so their comments live next to the mechanism.
    /// </summary>
    private static void TickCatchAll(bool inScenario)
    {
        // Part 2 enrollment #2 — mid-scenario reward showcase (see AddRewardShowcaseWindow).
        AddRewardShowcaseWindow(inScenario);

        // ModBuild 234 — the double-hosting audit, ABOVE the early return and above every gate in
        // this method on purpose: it reports on the state the CONVERT loop produced last tick, so a
        // run in which no unknown window is tracked at all (and a room in which the catch-all is
        // standing down) must still be able to print it. It is the only line that can see a window
        // wearing two hosts at once. See AuditDoubleHosting.
        AuditDoubleHosting();

        if (UnknownShown.Count == 0)
            return;

        // Prune: destroyed or game-closed windows leave the tracker (level-triggered —
        // a Hide transition already removed most in CatchAllObserve; this catches
        // scene unloads / ForceHideWindows that destroy without a clean transition).
        UnknownScratch.Clear();
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            if (kv.Key == null || !kv.Key.IsOpen)
                UnknownScratch.Add(kv.Key!);
        }
        for (int i = 0; i < UnknownScratch.Count; i++)
        {
            UnknownShown.Remove(UnknownScratch[i]);
            HudVerdict.Remove(UnknownScratch[i]); // verdict cache lives exactly as long as tracking
            // ModBuild 232: and so does the refusal table's edge state. This is the branch that
            // catches a window DESTROYED with its scene or force-hidden without a clean transition —
            // the hide path in CatchAllObserve covers the ordinary close. Dropping the edge here is
            // also what makes a re-opened singleton (both table rows are singletons) print its
            // refusal line again instead of silently inheriting the last life's verdict.
            FloatRefusalTable.Forget(UnknownScratch[i]);
            SubViewRetracted.Remove(UnknownScratch[i]); // ModBuild 234 — same scope, same prune
        }
        UnknownScratch.Clear();

        // ROOM gate (ModBuild 178 — the caller's local is TableInFrontOfPlayer now): the flat
        // menu/town keeps current behaviour, because Menu2D auto-shows the full flat screen there
        // and a floated window would duplicate it. THE 3D MAP ROOM IS THE OPPOSITE CASE and is why
        // this is no longer a scenario test: the flat screen is off there by construction, so a
        // window that is not floated is shown NOWHERE. Tracking above stays live so a window that
        // opened during loading floats the moment the scenario settles. The catch-all itself is
        // always on — user ruling 2026-08-11: essential deadlock insurance (its off state
        // restored the ItemCardPicker silent-deadlock class; misbehaving window types are
        // handled by the churn fuse below, not by a global switch).
        if (!inScenario || !WorldUIConfig.ConversionActive)
            return;

        int now = Time.frameCount;
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            UIWindow window = kv.Key;
            if (window == null || now < kv.Value + CatchAllGraceTicks)
                continue; // grace: explicit handlers win same-open races
            // ModBuild 184 — THE FUSE MAY NOT COUNT A HOVER CARD. A hover card floats once per
            // hover; that IS its life cycle, not churn. The 183 hardware log is unambiguous: the
            // map's quest-preview popup floated four times while he swept the laser across the
            // icons, and the fuse then suppressed it FOR THE WHOLE SESSION — "Es kommen nun gar
            // keine Mouseovers mehr", and one build later "Mouseover funktioniert nach wie vor
            // nicht". The fuse's own premise (see ChurnMaxFloats) is "decisions open once and
            // wait"; a hover card is the one floated thing that is not a decision at all, so it
            // is exempt from both the count and the verdict. Nothing else about the fuse changes:
            // a genuinely cycling HUD banner is still capped after ChurnMaxFloats.
            bool hoverCard = IsMapRoomHoverCard(window);

            // ModBuild 232 — THE REFUSAL TABLE, AND IT IS ASKED BEFORE THE `oursAlready` RE-ADD.
            //
            // USER REPORT, items 2 and 3 of the 231 hardware round: an empty frame with nothing but a
            // close cross (leeres_fenster_multiplayer.jpg — 'MP Lock Overlay'), and a "Quest wählen"
            // button hanging in the forest inside its own frame (frei_schwebender_button_multiplayer
            // .jpg — 'Multiplayer Ready Toggle'). "Das darf nicht sein." Both floated through the two
            // CATCH-ALL lines at Player.log:12170 and :18129. WHY THEY ARE REFUSED AND WHAT HAPPENS
            // TO THEM INSTEAD lives in FloatRefusalTable, one row each, with the source citation.
            //
            // THE POSITION IN THIS LOOP IS LOAD-BEARING, AND IT IS THE HALF OF THE BUG THAT ALREADY
            // HAD A TABLE ROW AND STILL SHIPPED. The declared sibling group (ModalFallback.7.Close's
            // WindowGroups: leader UIQuestPopup, member UIReadyToggle) was added in ModBuild 226 for
            // exactly this button — and it is consulted from CatchAllEligible, which the loop only
            // reaches AFTER `oursAlready`. The toggle's Show fires BEFORE the quest popup's (the 225
            // log has them in that order), so it floats on a tick when no leader exists yet, and from
            // the next tick onwards it is re-added as "already ours" and never re-examined. That is
            // why a correct rule produced no effect for two builds. A refusal that can only be
            // evaluated before the object is taken is not a refusal, so this one is asked FIRST and
            // is able to withdraw a float that already exists.
            //
            // WITHDRAWING IS A PRESENTATION ACT AND WRITES NOTHING TO THE GAME — see
            // WithdrawRefusedFloat for why the lever is `Sticky` and NOT `UserClosing`.
            if (FloatRefusalTable.Refuse(window))
            {
                WithdrawRefusedFloat(window);
                continue;
            }

            // ModBuild 234 — AND A FLOAT THIS PASS HAS JUST RETRACTED IS ASKED IN THE SAME POSITION,
            // FOR THE SAME REASON AS THE BLOCK ABOVE IT. RetractSubViewFloats drops `Sticky` and takes
            // the window out of OpenWindows, but the panel does not go away until the release loop
            // runs three phases later — and until it does, `IsFloatedByUs` is still TRUE, so the
            // `oursAlready` branch immediately below would put the window straight back into
            // OpenWindows and the release loop's keep-alive test would then hold the very float we
            // just gave up. A withdrawal that the next statement can undo is not a withdrawal; that
            // is the ModBuild 232 lesson one block up, applied to the second event class.
            //
            // IT CANNOT LATCH, WHICH IS THE WHOLE DESIGN. The entry is dropped the moment the panel
            // is actually gone, and from that tick the ordinary path decides afresh — "parent wins"
            // refuses it while the revived host stands, and if that host never converts the window
            // floats again by itself. Nothing here is a session verdict (ModBuild 231).
            if (SubViewRetracted.Count > 0 && SubViewRetracted.Contains(window))
            {
                if (IsFloatedByUs(window))
                    continue; // the release loop has not taken the panel yet — keep it out of the set
                SubViewRetracted.Remove(window);
            }

            // ModBuild 186 — A WINDOW WE ARE ALREADY FLOATING IS RE-ADDED UNCONDITIONALLY, AND
            // THIS IS THE WHOLE OF THE OSCILLATION BUG.
            //
            // OpenWindows means "the game windows the VR layer is presenting this tick". A window
            // this class has floated is in that set BY DEFINITION — yet every exclusion below asks
            // some form of "is this already handled / not our business", and CONVERTING IT MAKES
            // SEVERAL OF THEM TRUE:
            //   * the world-space test ("never float world-space UI — it is already visible in VR")
            //     matches because OUR conversion put its root canvas in WorldSpace;
            //   * IsAdoptedByConversion matched because we convert the window's OWN RectTransform
            //     (fixed separately in 185 — one of the two, which is why the loop survived).
            // So from the tick after a float the window was ineligible, was not re-added, and the
            // part-4 release loop dropped every NON-STICKY window missing from OpenWindows. Float,
            // release, float, release — 1417 log lines of it in the 185 session, and the map's
            // quest-preview popup never lived long enough to be revealed once: "Mouseover
            // funktioniert nach wie vor nicht."
            //
            // IT ALSO SPLIT THE MERCHANT. A STICKY window survives leaving OpenWindows, so the shop
            // window stayed floated while silently dropping OUT of the set — and AncestorWillBeFloated
            // asks exactly that set whether a parent is floated. Its inventory 'Scroll View' therefore
            // saw no floated ancestor and floated as a second window: "die eigentlichen Gegenstände
            // sind in einem zweiten Fenster". One missing membership, three reported symptoms.
            //
            // Patching the individual tests would leave the next one to be found the same way. The
            // set is the invariant, so the set is what is repaired here.
            bool oursAlready = IsFloatedByUs(window);
            if (oursAlready)
            {
                if (!ContainsWindow(OpenWindows, window))
                    OpenWindows.Add(window);
                continue;
            }

            if (!hoverCard && ChurnSuppressed.Contains(window.name))
                continue; // fuse blew for this window type — session-suppressed (see ChurnMaxFloats)
            if (!CatchAllEligible(window))
                continue;
            if (ContainsWindow(OpenWindows, window))
                continue; // already carried (e.g. its serialized ID IS a tracked one)

            // CHURN FUSE (hotfix): every append below costs a full conversion when the part-4
            // loop floats it. A window type re-floating in a tight loop (show→hide HUD banners)
            // burned ~1000 ms/frame on hardware — cap it and move on.
            if (!hoverCard)
            {
                float nowT = Time.unscaledTime;
                if (!FloatChurn.TryGetValue(window.name, out (int Count, float WindowStart) churn)
                    || nowT - churn.WindowStart > ChurnWindowSeconds)
                    churn = (0, nowT);
                churn.Count++;
                FloatChurn[window.name] = churn;
                if (churn.Count > ChurnMaxFloats)
                {
                    ChurnSuppressed.Add(window.name);
                    VRLog.Warn("WorldUI", $"CATCH-ALL FUSE: window '{window.name}' re-floated " +
                                          $"{churn.Count}× in {ChurnWindowSeconds:0}s — a cycling HUD " +
                                          "banner, not a waiting decision; suppressed for this session " +
                                          "(manual A/X screen chord still reaches it). Exclude it explicitly.");
                    continue;
                }
            }

            OpenWindows.Add(window);
            // ONE Warn per window type — the hardware log drives future EXPLICIT
            // enrollment (add the ID/poll, then this line disappears for that window).
            if (CatchAllWarned.Add(window.name))
            {
                VRLog.Warn("WorldUI", $"CATCH-ALL: unknown scenario window '{window.name}' " +
                                      $"(ID {window.ID}) floated — enroll it explicitly.");
                // ModBuild 187 — NAME THE THING BEFORE BINDING IT. Two builds have now guessed at
                // what the map room's 'Scroll View' is (an ID-less window that floats early and
                // that the merchant then pours 1998 transforms into, which is why its list reads as
                // a second merchant window) and both guesses were wrong. A window with no ID is
                // identified by WHERE IT LIVES and WHAT IS ON IT, so that is printed once per
                // window type — the next log settles it instead of a third round of inference.
                LogWindowIdentity(window);
            }
        }
    }

    /// <summary>
    /// Level-triggered exclusion check, re-evaluated EVERY tick the window would join
    /// the generic set — so a claim/adoption that starts later than the grace still
    /// stands the catch-all down (and its float releases through the normal part-4
    /// release loop the moment the window leaves OpenWindows).
    /// </summary>
    private static bool CatchAllEligible(UIWindow window)
    {
        // ModBuild 232 — the refusal table, in its PURE form (no logging, no edge state): this
        // predicate is re-entered several times per tick, once per level of the AncestorWillBeFloated
        // recursion, and every consumer of it must get the same answer the loop above acts on. The
        // one-line-per-edge logging belongs to the single call in TickCatchAll. See FloatRefusalTable.
        if (FloatRefusalTable.Refuses(window))
            return false;
        // Known-handled IDs (passives with post-mortems + surface-owned windows).
        if (CatchAllKnownHandled.Contains(window.ID))
            return false;
        // ModBuild 226: this window was floated once and reached its reveal edge with NOTHING drawn
        // under it, so the empty-window invariant released it (see RefuseEmptyFloat). Do not float it
        // again until the game has closed and re-opened it — otherwise the pair churns once per tick.
        // The prune is right here rather than in a tick step because this is the one place the set is
        // read: a window the game no longer reports open has served its refusal.
        if (EmptyRefusedNow(window))
        {
            if (window.IsOpen)
                return false;
            ClearEmptyRefusal(window);
        }
        // ModBuild 230: and the liveness rule's own hold — this window was floated, drew nothing for
        // the whole dwell and was released for it. Same rule, later edge: 226 refuses a window that
        // was born empty, 230 releases one that went dark while standing. The un-enrolled path needs
        // the check here for the same reason the enrolled one needs it in the convert loop.
        if (EmptyHeldNow(window))
            return false;
        // HOTFIX (hardware round 2026-08-01): known HUD OWNERS, matched by COMPONENT (their
        // window IDs are all scene-serialized None, so the ID set above cannot carry them).
        // These are permanent flat-HUD subsystems the mod already owns elsewhere — floating
        // them produced exactly the reported bug: empty bar+X windows flapping over the view
        // (Notification/PhaseBanner cycle show→hide constantly) and ~1000 ms conversions of
        // the whole hidden hand subtree ('Cards Hands Manager').
        if (IsKnownHudWindow(window))
            return false;
        // The three explicit polls resolve their own window instances — those join
        // OpenWindows through their polls (with their special handling: story-box X
        // exclusion, level-message groups), never through the catch-all.
        if (IsPollWindow(window))
            return false;
        // Decision-dock claims stand the generic path down completely (test #21/#22).
        if (DecisionDock.ClaimsWindow(window))
            return false;
        // Cards flows own the item picker window while a surrender/refresh/lose pick is
        // live (the ItemCardPicker deadlock got its OWN VR flow — item fan + banner +
        // item-use slot; floating the hidden 2D picker over it would double-handle).
        if (IsCardsOwnedItemPickerWindow(window))
            return false;
        // A window whose subtree is already adopted by ANY live conversion (a surface
        // docked its row, a host carries its root) is owned elsewhere this instant.
        if (IsAdoptedByConversion(window))
            return false;
        // ModBuild 181 — THE PARENT WINS IN THE MAP ROOM. User, on the character screen: "das Menu
        // [ist] jetzt in mehrere Elemente unterteilt die jeweils ein eigenes Fenster bekommen. Das
        // soll nicht sein — es soll … alles direkt in dem Fenster das für die Character UI
        // zuständig ist angezeigt werden." The party/character screen is a nest of UIWindows
        // ('Campaign Adventure Party Assembly Variant' carrying 'Campaign Party Assembly Character
        // Display' carrying 'Party Display UI'), and floating each one separately scatters a single
        // screen across the room. An open ANCESTOR window already renders this subtree inside its
        // own host, so a second float of the child is a duplicate, not a window.
        //
        // Level-triggered, like every other rule here: OpenWindows is rebuilt from scratch each
        // tick, so when the ancestor closes the child becomes eligible again by itself — and a
        // child that was floated FIRST releases by itself the moment the ancestor opens, because
        // it stops being re-added.
        //
        // ModBuild 226 — THE MAP-ROOM SCOPE IS GONE, AND THE REASON RECORDED FOR IT IS NO LONGER
        // TRUE. The sentence that stood here read: "Map-room scoped: in a scenario the flat screen
        // composites whatever the mod does not float, so nesting there is not a visual problem and
        // the rule would be a behaviour change with no report behind it." Both halves are falsified.
        // (1) The mod floats windows in a SCENARIO too — the ModBuild 225 log has
        // "MODAL WINDOW: 'Story Window' (ID None) floated in front of the HMD" with
        // "room=True, scenario=True", its own MODAL GRAB and its own bar; the flat screen composites
        // only what FAILED to convert. So a nested UIWindow opening inside a floated scenario window
        // did not render quietly inside its parent — it became a second floating panel with a second
        // grab bar. (2) There is now a report behind it, verbatim: "Beim Storyfenster war der
        // Dialog/Untertitel ein eigenes Fenster und später bei der Begegnung wurde das auch in
        // mehrere Fenster statt einem einzigen getrennt! Das soll nicht sein." Both of those are
        // scenario windows. The rule now holds wherever a floatable ancestor is open, which is the
        // condition it was always really about — the map room was only the first place one existed.
        //
        // THE WIDENING IS DONE WITH THE STRICTER TEST, NOT WITH THIS ONE. Two rules now stand here
        // and the difference between them is deliberate:
        //
        //   IN THE MAP ROOM (line below, unchanged since ModBuild 188): the ancestor need only be
        //     one that WILL be floated — including one that is currently CLOSED. That is the
        //     merchant lesson: a UIWindow four levels inside a closed shop window is a fragment of a
        //     screen that is not showing at all, and floating it produced "die eigentlichen
        //     Gegenstände sind in einem zweiten Fenster". It is shipping, it is tuned, and it is left
        //     byte-for-byte alone.
        //   EVERYWHERE (the RendersInsideFloatedAncestor call after it, ModBuild 226): the ancestor
        //     must be a LIVE FLOATED HOST this instant. That is the form that can be let out of the
        //     map room safely, because it can never refuse a window on account of a parent that does
        //     not exist — the DurabilityPanel rule, "a wrongly-floated window is recoverable, a
        //     dropped one is a silent deadlock". A scenario dialog nested inside a floated scenario
        //     window IS caught by it (that is the report), and a scenario window whose ancestor never
        //     floats is untouched.
        //
        // THE PARALLEL-WINDOW FAMILIES ARE EXEMPT from both, as they already are on the enrolled path
        // (RendersInsideFloatedAncestor's own first guard, and the ModBuild 180 ruling it cites:
        // "Anders als in Flat soll es hier möglich sein mehrere Fenster parallel offen zu haben").
        // Their parents are not stable placement facts anyway — MainOptionOptions re-parents the
        // options window at RUNTIME in the main menu (MainOptionOptions.cs:20-25). This costs nothing
        // inside the map room, where the ModBuild 225 log shows both the ESC menu and the options
        // window floating standalone, i.e. neither had an ancestor for the rule to find.
        if (!NonBlockingMenus.Contains(window.ID) && !MultiplayerRosterMenus.Contains(window.ID)
            && MapRoom.MapRoomDriver.Active && HasOpenAncestorWindow(window, allowRevival: true))
            return false;
        // ModBuild 226 — the live-floated ancestor AND the declared sibling groups, in one call, so
        // the catch-all path and the enrolled path cannot disagree about what is one panel. The
        // multiplayer "Quest wählen" confirm is a ROOT under Campaign Canvas with no ancestor window
        // at all (the WINDOW IDENTITY line says "nearest ancestor UIWindow <none>"), so the walk
        // above can never find it; it is DECLARED instead. See ModalFallback.WindowGroups.
        if (RendersInsideFloatedAncestor(window))
            return false;
        // Never float world-space UI: the generic float is a screen-space→world
        // conversion; a genuinely world-space window is already visible in VR.
        var rect = window.transform as RectTransform;
        if (rect == null)
            return false;
        Canvas? canvas = window.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
            return false;
        return true;
    }

    /// <summary>
    /// Known flat-HUD owners the catch-all must never float, matched by component because
    /// their window IDs are scene-serialized <c>None</c>:
    /// <c>CardsHandManager</c> (the hidden flat hand — the VR card fans ARE its surface),
    /// <c>CombatLogHandler</c> (adopted by <see cref="Surfaces.CombatLogSurface"/> — the
    /// instant-level conversion check misses it whenever that panel is currently released),
    /// <c>UINotificationManager</c> and <c>PhaseBannerHandler</c> (transient banners cycling
    /// show→hide — a float would flap and convert forever; they are ambient info, not
    /// decisions). Verdict cached per instance (<see cref="HudVerdict"/>): the component
    /// walk over the hand subtree is far too heavy to repeat per tick.
    /// </summary>
    private static bool IsKnownHudWindow(UIWindow window)
    {
        if (HudVerdict.TryGetValue(window, out bool hud))
            return hud;
        hud = window.GetComponentInParent<CardsHandManager>() != null
              || window.GetComponentInParent<CombatLogHandler>() != null
              || window.GetComponentInParent<UINotificationManager>() != null
              || window.GetComponentInParent<PhaseBannerHandler>() != null
              // ModBuild 179: UIGuildmasterHUD is the campaign map's PERMANENT bar, not a decision.
              // Floating it produced exactly the failure mode this list exists for — the log shows
              // it converted, released ("open=True, convertWanted=True") and re-converted until the
              // churn fuse blew and named it itself: "a cycling HUD banner, not a waiting decision".
              // Its VR surface is now WorldUI/MapRoom/MapButtonRail, which stands its buttons on the
              // table as physical caps — the same relationship the card fans have to CardsHandManager
              // one line above. Unconditional, not map-room-gated: this verdict is CACHED per window
              // instance below, so a gate that changed with the room would be sticky and wrong half
              // the time — and on the flat 2D map the screen shows the bar anyway.
              //
              // ModBuild 180 — AND THE TEST IS ON THE WINDOW'S OWN GAMEOBJECT, NOT ITS ANCESTRY.
              // 179 wrote GetComponentInParent here, which is true for every window the HUD OWNS:
              // shopWindow, templeWindow, trainerWindow, enhancementWindow are all serialized
              // children of it (decompiled UIGuildmasterHUD.cs:76-92). So pressing Merchant or
              // Temple opened the game's window and the catch-all then refused to float it — the
              // hardware log shows "MAP TABLE BUTTON 'Merchant' pressed" with NOTHING after it.
              // The BAR is the HUD; its windows are not, and only the bar belongs on this list.
              || window.GetComponent<UIGuildmasterHUD>() != null
              || window.GetComponentInChildren<CardsHandManager>(true) != null
              || window.GetComponentInChildren<CombatLogHandler>(true) != null
              || window.GetComponentInChildren<UINotificationManager>(true) != null
              || window.GetComponentInChildren<PhaseBannerHandler>(true) != null;
        HudVerdict[window] = hud;
        return hud;
    }

    /// <summary>Is this instance one of the three explicit polls' windows (story box,
    /// level-message groups, dialogPopup)? Instance compare — their IDs are scene-serialized.</summary>
    private static bool IsPollWindow(UIWindow window)
    {
        if (Singleton<StoryController>.IsInitialized)
        {
            StoryController sc = Singleton<StoryController>.Instance;
            if (sc != null && ReferenceEquals(sc.window, window))
                return true;
        }
        LevelMessagesUIHandler? lm = LevelMessagesUIHandler.s_Instance;
        if (lm != null)
        {
            if (lm.LevelMessageBoxLayoutGroup != null
                && ReferenceEquals(lm.LevelMessageBoxLayoutGroup.window, window))
                return true;
            if (lm.LevelMessageHelpTextLayoutGroup != null
                && ReferenceEquals(lm.LevelMessageHelpTextLayoutGroup.window, window))
                return true;
        }
        UIManager? manager = UIManager.Instance;
        if (manager != null && manager.dialogPopup != null
            && ReferenceEquals(manager.dialogPopup.Window, window))
            return true;
        return false;
    }

    /// <summary>
    /// The ItemCardPicker window while the Cards module's item-surrender/refresh flow
    /// (or the reward lose-item flow) presents it — identified exactly like
    /// <c>CardsGameApi.OpenItemPicker</c>: the pickers' publicized <c>picker.window</c>
    /// (ItemCardRefreshPicker.cs:14 / ItemRewardLosePicker.cs:15, ItemCardPicker.cs:27).
    /// Both pickers are checked because either may drive the (possibly shared) picker
    /// component; while open, the Cards item fan + banner + item-use slot are the VR
    /// affordance for it.
    /// </summary>
    private static bool IsCardsOwnedItemPickerWindow(UIWindow window)
    {
        if (Singleton<ItemCardRefreshPicker>.IsInitialized)
        {
            ItemCardRefreshPicker rp = Singleton<ItemCardRefreshPicker>.Instance;
            if (rp != null && rp.picker != null && ReferenceEquals(rp.picker.window, window))
                return true;
        }
        if (Singleton<ItemRewardLosePicker>.IsInitialized)
        {
            ItemRewardLosePicker lp = Singleton<ItemRewardLosePicker>.Instance;
            if (lp != null && lp.picker != null && ReferenceEquals(lp.picker.window, window))
                return true;
        }
        return false;
    }

    /// <summary>Is the window root inside (or above) any live CanvasConversion target —
    /// i.e. some surface already physicalizes part of it this instant?</summary>
    /// <summary>
    /// Does an OPEN <c>UIWindow</c> sit above this one in the hierarchy? Walks parents only — the
    /// child is inside the ancestor's subtree by construction, so the ancestor's float already
    /// renders it. See the call site for why this is map-room scoped.
    /// </summary>
    /// <param name="window">The child whose float this call decides.</param>
    /// <param name="allowRevival">ModBuild 233 — may this call LIFT a liveness hold on the ancestor
    /// (see <see cref="SubViewRevival"/>)? False by default, and the default is the load-bearing
    /// value: <c>ExplainRefusal</c> in ModalFallback.4.Tick.cs calls this walk and its own doc
    /// states it is "deliberately READ-ONLY … a diagnostic must not change the state it reports
    /// on". Only <see cref="CatchAllEligible"/>, which is the decision, passes true. A probe that
    /// spends the decision it was asked to observe is the ModBuild 226 RenderTargetProbe mistake.
    /// </param>
    private static bool HasOpenAncestorWindow(UIWindow window, bool allowRevival = false)
    {
        Transform? t = window.transform.parent;
        while (t != null)
        {
            var above = t.GetComponent<UIWindow>();
            if (above != null && !ReferenceEquals(above, window)
                && AncestorWillBeFloated(above, window, allowRevival))
            {
                if (AncestorRefusalWarned.Add(window.name))
                    VRLog.Info("WorldUI", $"MAP ROOM: window '{window.name}' (ID {window.ID}) is NOT " +
                                          $"floated on its own — its ancestor '{above.name}' " +
                                          $"(ID {above.ID}) is floated and renders this subtree inside " +
                                          "its own host. The parent wins (ModBuild 181/184).");
                return true;
            }
            t = t.parent;
        }
        return false;
    }

    /// <summary>Per-child-name latch for the "parent wins" Info line — one per window type.</summary>
    private static readonly HashSet<string> AncestorRefusalWarned = new();

    /// <summary>
    /// IS THIS ANCESTOR ACTUALLY GOING TO BE A FLOATED WINDOW? (ModBuild 184.)
    ///
    /// <para>181's "parent wins" rule asked only whether an ancestor was OPEN, and 182 patched a
    /// symptom of that by exempting children with a root Canvas of their own. Both were wrong at
    /// the same spot: the rule's premise is <i>"the parent's float already renders this subtree"</i>,
    /// so the question is not whether an ancestor is open but whether it is <b>floated</b>. An
    /// ancestor that is open and permanently un-floatable suppresses its child and shows it
    /// NOWHERE — which is exactly what happened to the merchant. <c>UIShopItemWindow</c>,
    /// <c>UITempleWindow</c>, <c>UITrainerWindow</c>, <c>UINewEnhancementWindow</c> and
    /// <c>UITownRecordsWindow</c> are all serialized children of <c>UIGuildmasterHUD</c>
    /// (decompiled UIGuildmasterHUD.cs:76-92), and the HUD's own window is open forever while it
    /// is on the bar AND permanently refused by <see cref="IsKnownHudWindow"/> — so the shop
    /// window was refused, and 182's root-canvas exemption then let its INNER scroll view through
    /// instead. The 183 log shows the result exactly: 'Scroll View' floated, the shop window
    /// never did, and the merchant appeared as bare content with no frame and no background. His
    /// report: <i>"Der Händler und co sollte ein separates Fenster sein das spawned inklusive des
    /// jeweiligen Hintergrunds."</i></para>
    ///
    /// <para>THE ROOT-CANVAS EXEMPTION IS GONE WITH IT, and that is a simplification rather than a
    /// loss: a child canvas inside a floated host is ADOPTED by the conversion
    /// (CanvasConversion.2.Adopt — the MODAL DIAG lines list the adopted children and their pinned
    /// sorting order), so once the ancestor genuinely floats, the child renders inside it. The
    /// exemption only ever mattered while the ancestor did not float, and that case is now
    /// answered at its cause.</para>
    ///
    /// <para>Two ways an ancestor counts: it is already carried this tick (the explicit polls run
    /// BEFORE the catch-all, so an enrolled ancestor is in <see cref="OpenWindows"/> by now), or
    /// the catch-all itself would take it. The second test recurses up the chain — bounded by
    /// hierarchy depth, and correct by construction: "will anything above me float" is exactly
    /// the question.</para>
    ///
    /// <para>THE ANCESTOR DOES NOT HAVE TO BE OPEN RIGHT NOW (ModBuild 188), and requiring that was
    /// the whole of the merchant bug. 187's <c>WINDOW IDENTITY</c> census finally named the second
    /// merchant window instead of inferring it — two earlier rounds inferred it and both were
    /// wrong:</para>
    /// <code>
    /// WINDOW IDENTITY 'Scroll View' (ID None): path Campaign Canvas/UI Guildmaster HUD/
    ///   UI Shop Item Window/UI Shop Inventory Variant/Content/Scroll View; rect 512x887;
    ///   components [RectTransform, CanvasRenderer, ExtendedScrollRect, CanvasGroup, UIWindow];
    ///   nearest ancestor UIWindow 'UI Shop Item Window' (ID Shop, open=False).
    /// </code>
    /// <para>It is the merchant's own inventory, four levels INSIDE the shop window — not the
    /// sibling ModBuild 186 deduced from a transform count, and not the party inventory 187
    /// suspected. It floated because at that moment the shop window was <b>closed</b>: the rule
    /// asked <c>above.IsOpen</c>, a closed ancestor did not win, and a fragment of a hidden screen
    /// was floated as a window of its own — a quarter of an hour before the merchant was ever
    /// opened. The map room's sticky rule then kept it there forever, so when the merchant finally
    /// opened its rows poured into that stale float: "die eigentlichen Gegenstände sind in einem
    /// zweiten Fenster".</para>
    ///
    /// <para>A closed ancestor is if anything a STRONGER reason to refuse: an open ancestor at
    /// least renders the child somewhere, while a closed one means the screen this fragment belongs
    /// to is not showing at all. The tell is right there in the census — a <c>UIWindow</c> sitting
    /// on an <c>ExtendedScrollRect</c> is not a window in any sense the player would recognise; the
    /// game simply reuses the component as a show/hide helper for a scroll area. Dropping the
    /// <c>IsOpen</c> term costs nothing elsewhere, because an ancestor that is merely un-floatable
    /// (the permanently-open guildmaster HUD above the shop window) is still refused by
    /// <see cref="CatchAllEligible"/> and so still does not win.</para>
    /// </summary>
    /// <param name="above">The candidate ancestor.</param>
    /// <param name="child">The window that is deferring to it — the sub-view whose float this call
    /// decides. ModBuild 233 needs it: a held-out ancestor with a LIVE child is revived rather than
    /// bypassed, and "is the child drawing" is the whole precondition of that. See
    /// <see cref="SubViewRevival"/>.</param>
    /// <param name="allowRevival">May this call lift a liveness hold? See the caller's parameter
    /// doc — false for the read-only refusal diagnostic, true for the eligibility decision.</param>
    private static bool AncestorWillBeFloated(UIWindow above, UIWindow child, bool allowRevival)
    {
        if (IsConverted(above))
            return true;
        // ModBuild 232 — "IN OpenWindows" IS NOT "WILL BE FLOATED", AND THE DIFFERENCE EMPTIED THE
        // ROOM. ModBuild 184 already corrected this rule once, from "is an ancestor OPEN" to "is an
        // ancestor FLOATED", because an ancestor that is open and permanently un-floatable suppresses
        // its child and shows it NOWHERE. The same failure came back through a narrower door: a
        // window can sit in OpenWindows and still be skipped by the convert loop, and then it is
        // exactly as un-floatable as the HUD window 184 was about.
        //
        // THE HARDWARE TRACE (ModBuild 231, Player.log:93648 → :94496):
        //     EMPTY WINDOW RELEASED: 'New Party display' (ID PartyPanel) — DARK …
        //     MAP ROOM: window 'UI Battle Goal Picker Window' (ID None) is NOT floated on its own —
        //         its ancestor 'New Party display' (ID PartyPanel) is floated …
        //     MODAL LIVENESS CENSUS: 0 floated window(s) … 1 window(s) held out of the float set for
        //         having been released dark.
        // The party display was in the liveness hold, so the convert loop skipped it every tick; the
        // battle-goal picker — the one thing the player had to click to get out of the loadout —
        // deferred to a host that did not exist. That is [[parent-wins-needs-a-real-parent]]:
        // suppressing X because Y handles it requires Y to ACTUALLY handle it.
        //
        // So the holds the convert loop applies are asked HERE too, and both sides read the same
        // state. EmptyHold is read by CONTAINMENT rather than through EmptyHeldNow, deliberately:
        // that method carries the 6 Hz release probe, and a diagnostic caller must never spend the
        // probe slot the loop's own call is about to need. StoryComposite.HoldsBack is deliberately
        // NOT consulted for the mirror-image reason — it counts its own calls for the census line —
        // and it does not need to be: it is a two-tick bridge whose members are windows the gate has
        // just closed, which cannot be a live ancestor of anything for longer than that.
        //
        // ModBuild 233 — A HELD-OUT HOST WITH A LIVE SUB-VIEW IS REVIVED, NOT BYPASSED. This is the
        // COMPLETION of the 232 rule below it, not a reversal: 232 asked "may a child defer to a
        // host that will not exist?" and answered no, which was right. What it never asked is
        // whether the host could be made to exist. When the reason for the hold is the LIVENESS hold
        // specifically — the host was released for drawing nothing — and the very child now asking
        // to float is itself drawing, then the host is not empty at all; its content is one level
        // down, and rebuilding it renders the child INSIDE it (the adopted-nested-canvas path the
        // ModBuild 232 log already shows working at Player.log:3551). The 232 answer floated the
        // child beside the host instead, and the user photographed both windows side by side:
        // .planning/debug/character_ui_quest_getrennt.jpg.
        //
        // ORDER IS WHY THIS WORKS IN ONE TICK. Tick runs PhasePolls (OpenWindows is rebuilt, and the
        // party display is ID-enrolled so it is IN that list) → PhaseCatchAll (here) → PhaseRelease
        // → PhaseConvert. Lifting the hold here is already lifted when the convert loop's
        // EmptyHeldNow asks two phases later, so the host floats on THIS tick and the player never
        // sees a frame of either the detached state or an absent one.
        //
        // Only the LIVENESS hold is revivable. `Failed` means the conversion threw and the flat
        // screen has already risen for it; the refusal table is a standing ruling that this window
        // must not be a frame of its own. Neither is "the content is one level down", and reviving
        // either would be overturning a different rule from inside this one.
        if (allowRevival && EmptyHold.Contains(above) && SubViewRevival.ShouldReviveHost(above, child))
        {
            EmptyHold.Remove(above);
            // Re-arm the stand-down latch: if this host is ever held out again, that is a NEW event
            // and its falsifier line must print rather than be swallowed by a latch set minutes ago.
            AncestorHeldOutWarned.Remove(above.name);
            // ModBuild 234 — AND EVERY DESCENDANT THIS PASS HAS ALREADY TAKEN IS GIVEN BACK. See
            // RetractSubViewFloats: the ModBuild 233 hardware log lost this race by ONE window.
            RetractSubViewFloats(above);
            return true;
        }
        if (ContainsWindow(Failed, above) || EmptyHold.Contains(above) || FloatRefusalTable.Refuses(above))
        {
            if (AncestorHeldOutWarned.Add(above.name))
                VRLog.Warn("WorldUI", $"PARENT WINS STOOD DOWN: '{above.name}' (ID {above.ID}) is open "
                                      + "and would have suppressed the windows nested inside it, but the "
                                      + "float loop is holding it out — "
                                      + (ContainsWindow(Failed, above)
                                          ? "its conversion FAILED"
                                          : EmptyHold.Contains(above)
                                              ? "it was released for drawing nothing (the liveness hold)"
                                              : "the float refusal table refuses it")
                                      + ". A child may not defer to a host that will not exist, so the "
                                      + "children float on their own instead. THIS LINE IS A FALSIFIER: "
                                      + "if it never appears, no child was ever suppressed by an absent "
                                      + "parent; if it appears, the room stayed reachable BECAUSE of it.");
            return false;
        }
        if (ContainsWindow(OpenWindows, above))
            return true;
        return CatchAllEligible(above);
    }

    /// <summary>Per-ancestor-name latch for the stand-down warning above — one line per host type.
    /// Cleared with the rest of the catch-all state when the room stands down.</summary>
    private static readonly HashSet<string> AncestorHeldOutWarned = new();

    private static bool IsAdoptedByConversion(UIWindow window)
    {
        Transform root = window.transform;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel panel = panels[i];
            RectTransform target = panel.Target;
            if (target == null)
                continue;
            // ModBuild 185 — A WINDOW'S OWN MODAL FLOAT IS NOT "SOMEONE ELSE OWNS IT", AND
            // COUNTING IT WAS A TWO-TICK CONVERT/RELEASE LOOP.
            //
            // This test asks "does some OTHER surface already physicalize this subtree" so the
            // generic path can stand down. But TryConvertWindow converts the window's OWN
            // RectTransform, so from the tick after a catch-all float the window matched ITSELF
            // here. The tick order is: rebuild OpenWindows → TickCatchAll → release loop. So:
            // tick N floats it; tick N+1 finds it "adopted", does not re-add it, and the release
            // loop drops every non-sticky window that is not in OpenWindows — released; tick N+2
            // floats it again. Float, release, float, release, forever, at one full conversion
            // each. THAT is what the churn fuse was really capping (it is why floating the hidden
            // hand subtree measured ~1000 ms/frame), and ModBuild 184's exemption of hover cards
            // from that fuse removed the cap and exposed the loop underneath: the 184 log shows
            // the quest-preview popup floating and releasing on alternating ticks with
            // "open=True, convertWanted=True", never once surviving long enough to be revealed.
            // "Es kommen nun gar keine Mouseovers mehr" — they were being torn down one tick after
            // they appeared. Sticky windows (the map room's parallel rule) never showed the
            // symptom because they survive leaving OpenWindows, which is why this went unseen.
            if (IsOwnModalFloat(panel, window))
                continue;
            if (ReferenceEquals(target, root) || target.IsChildOf(root) || root.IsChildOf(target))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Print WHERE an ID-less catch-all window lives and WHAT is on it — its full transform path,
    /// the components on its own GameObject, and the nearest ancestor that carries another
    /// <c>UIWindow</c>. One line per window type, at the moment it is first floated.
    /// </summary>
    private static void LogWindowIdentity(UIWindow window)
    {
        try
        {
            var path = new System.Text.StringBuilder(160);
            for (Transform? t = window.transform; t != null; t = t.parent)
            {
                if (path.Length > 0)
                    path.Insert(0, '/');
                path.Insert(0, t.name);
            }
            var comps = new System.Text.StringBuilder(160);
            Component[] own = window.GetComponents<Component>();
            for (int i = 0; i < own.Length; i++)
            {
                if (i > 0)
                    comps.Append(", ");
                comps.Append(own[i] != null ? own[i].GetType().Name : "<null>");
            }
            string ancestor = "<none>";
            for (Transform? t = window.transform.parent; t != null; t = t.parent)
            {
                var above = t.GetComponent<UIWindow>();
                if (above == null)
                    continue;
                ancestor = $"'{above.name}' (ID {above.ID}, open={above.IsOpen})";
                break;
            }
            var rect = window.transform as RectTransform;
            VRLog.Info("WorldUI", $"WINDOW IDENTITY '{window.name}' (ID {window.ID}): path {path}; "
                                  + $"rect {(rect != null ? $"{rect.rect.width:F0}x{rect.rect.height:F0}" : "<none>")}; "
                                  + $"components [{comps}]; nearest ancestor UIWindow {ancestor}.");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"WINDOW IDENTITY probe threw for '{window.name}': {ex.Message}");
        }
    }

    /// <summary>Is this panel the modal float THIS class made for THIS window?</summary>
    private static bool IsOwnModalFloat(ConvertedPanel panel, UIWindow window)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (ReferenceEquals(wp.Panel, panel) && ReferenceEquals(wp.Window, window))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this class currently present this window as a floated VR window? A panel the user is
    /// closing does NOT count — the X must still be able to drop it (its release is what makes
    /// this go false, and then the normal eligibility path decides afresh).
    /// </summary>
    private static bool IsFloatedByUs(UIWindow window)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!ReferenceEquals(wp.Window, window) || wp.UserClosing)
                continue;
            if (wp.Panel != null && wp.Panel.IsAlive)
                return true;
        }
        return false;
    }

    /// <summary>
    /// WITHDRAW A FLOAT THE REFUSAL TABLE HAS JUST DECIDED MUST NOT EXIST — and leave the game
    /// window in a defensible state while doing it (ModBuild 232).
    ///
    /// <para>THE LEVER IS <see cref="WindowPanel.Sticky"/> AND DELIBERATELY NOT
    /// <see cref="WindowPanel.UserClosing"/>. Both would end the float, but they are not the same
    /// act. <c>UserClosing</c> means "the player closed this", and the part-4 release loop honours
    /// that by calling <c>window.Hide()</c> on a window that still reports open — GAME STATE, on a
    /// window nobody asked to close. Refusing to draw a control in a frame of its own must never
    /// take the control away from the game. Dropping <c>Sticky</c> and not re-adding the window to
    /// <see cref="OpenWindows"/> (the <c>continue</c> at the call site) makes the same release loop
    /// take the ORDINARY exit one tick later — <c>Converted.RemoveAt</c>, the grab holder destroyed,
    /// <c>CanvasConversion.Release</c> restoring the exact 2D home, and the arc slot handed back —
    /// with no <c>Hide</c>, no <c>Escape</c>, no <c>CanvasGroup</c> or <c>Canvas</c> write, and
    /// nothing on the wire. It is the same concession ModBuild 226 makes in
    /// <c>ReassertStickyVisible</c> ("only our own stickiness flag was dropped") for the same
    /// reason.</para>
    ///
    /// <para>AND THE PRE-CONVERT BLACKOUT IS HANDED BACK HERE, EXPLICITLY. That path
    /// (ModalFallback.11) switches a just-opened window's own canvases off for the one frame between
    /// the game's <c>Show()</c> and the conversion, and it ends either when the conversion takes over
    /// or when its 8-frame budget expires — the second exit being the
    /// <c>MODAL PRE-CONVERT BLACKOUT: '…' was still un-floated after 9 frames</c> warning. A refused
    /// window is never converted, so it would always take the second exit. Today it cannot even get
    /// there (the blackout only runs for ENROLLED ids — <c>OnWindow</c> returns before
    /// <c>PreConvertHide</c> unless <c>IsFallbackWindow</c>, and both table rows are
    /// <c>UIWindowID.None</c>, which is why the ModBuild 231 log's 51 blackout lines name neither of
    /// them), but a future row with an enrolled id would, so the release is done rather than
    /// assumed. Calling it when no blackout is in force is a no-op.</para>
    /// </summary>
    private static void WithdrawRefusedFloat(UIWindow window)
    {
        // Insurance against the one path that could leave a refused window invisible. No-op when
        // this window was never blacked out, which is every case there is today.
        ReleasePreConvertHide(window, "the refusal table refuses to float it — its 2D rendering is "
                                      + "handed back at once rather than sitting on the blackout budget");

        if (!DropOwnFloat(window))
            return;
        VRLog.Info("WorldUI", $"FLOAT WITHDRAWN: '{window.name}' (ID {window.ID}) was already "
                              + "floated when the refusal table refused it, so the float is given "
                              + "up: its map-room stickiness is dropped and it is not re-added to "
                              + "the open set, which means the ordinary release loop takes it down "
                              + "on the next tick — panel, grab bar, close cross and arc slot "
                              + "together — and CanvasConversion.Release restores its exact 2D "
                              + "home. NOTHING WAS WRITTEN TO THE GAME: this is not the X-button "
                              + "path (that one hides the window, which would take a control away "
                              + "from a game that still needs it), only the mod's own flag.");
    }

    /// <summary>
    /// THE WITHDRAWAL PRIMITIVE ITSELF (extracted verbatim from <see cref="WithdrawRefusedFloat"/>,
    /// ModBuild 234): drop THIS class's own <c>Sticky</c> flag on a live float of
    /// <paramref name="window"/> so the ordinary release loop takes it down. Returns true when a
    /// live float was actually given up — i.e. when the caller has something to report.
    ///
    /// <para>THE LEVER IS <see cref="WindowPanel.Sticky"/> AND STILL DELIBERATELY NOT
    /// <see cref="WindowPanel.UserClosing"/>; the whole argument is on the caller above and it is not
    /// repeated here so it cannot drift into two versions. There is exactly ONE way to un-float
    /// something in this class and this is it.</para>
    ///
    /// <para>A window whose <c>Sticky</c> is already clear returns FALSE, not true: it is on its way
    /// out through the release loop already and a second "withdrawn" line for it would be an edge
    /// that never happened.</para>
    /// </summary>
    private static bool DropOwnFloat(UIWindow window)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!ReferenceEquals(wp.Window, window) || wp.UserClosing || !wp.Panel.IsAlive)
                continue;
            if (!wp.Sticky)
                return false; // already on its way out through the ordinary release loop
            wp.Sticky = false;
            return true;
        }
        return false;
    }

    /// <summary>Per-child-name latch for the retraction line below — one line per sub-view type.</summary>
    private static readonly HashSet<string> SubViewRetractWarned = new();

    /// <summary>Scratch for the retraction sweep (it removes from <see cref="OpenWindows"/> while
    /// reading it, so the read is done backwards and the names are collected for one log line).</summary>
    private static readonly List<UIWindow> RetractScratch = new(4);

    /// <summary>
    /// Windows whose float <see cref="RetractSubViewFloats"/> has given up but whose panel the
    /// release loop has not taken down yet — see the guard in <see cref="TickCatchAll"/> for why the
    /// bridge is needed and why it cannot latch. Membership is dropped the moment
    /// <see cref="IsFloatedByUs"/> goes false, and on any close/prune with the rest of the per-window
    /// edge state.
    /// </summary>
    private static readonly HashSet<UIWindow> SubViewRetracted = new();

    /// <summary>
    /// A REVIVAL MUST TAKE BACK WHAT THE SAME PASS ALREADY FLOATED (ModBuild 234).
    ///
    /// <para><b>THE ModBuild 233 RULE IS CORRECT AND FIRED ONE WINDOW TOO LATE.</b> The hardware log
    /// (<c>.planning/debug/Player.log</c>, one pass, in this order):</para>
    /// <code>
    /// :15867  PARENT WINS STOOD DOWN: 'New Party display' … the float loop is holding it out
    /// :15868  CATCH-ALL: unknown scenario window 'UI Battle Goal Picker Window' (ID None) floated
    /// :15870  SUB-VIEW REVIVAL: the liveness hold on 'New Party display' is LIFTED because its
    ///             nested sub-view 'Party Display UI ' … is open and MEASURED DRAWING right now.
    /// :15873  Adopted nested canvas 'UI Battle Goal Picker Window' in 'GloomhavenVR.Panel_Modal_New Party display'
    /// :15894  Converted 'Modal_New Party display' to world space (1920x1080 px)
    /// :15904  Adopted nested canvas 'UI Battle Goal Picker Window' in 'GloomhavenVR.Panel_Modal_UI Battle Goal Picker Window'
    /// :15907  Converted 'Modal_UI Battle Goal Picker Window' to world space (535x1042 px)
    /// </code>
    /// <para>The picker is adopted INTO the revived host at :15873 and then converted into a host of
    /// its OWN at :15907, which reparents it straight back out — the picture in
    /// <c>.planning/debug/Questauswahl.jpg</c>, the character UI on the left and the two battle-goal
    /// cards floating in front of it with close crosses. Prevention (the widened precondition in
    /// <see cref="SubViewRevival.ShouldReviveHost"/>) closes the ordering that actually shipped; this
    /// closes the OTHER ordering, where the descendant floated on an earlier tick — from that tick on
    /// the <c>oursAlready</c> re-add at the top of <see cref="TickCatchAll"/> short-circuits its
    /// evaluation, so no later revival could ever have reconsidered it.</para>
    ///
    /// <para><b>TWO POPULATIONS, ONE SWEEP</b>, because a descendant can be in either state when the
    /// hold lifts and only one of them has a panel to take down:</para>
    /// <list type="number">
    /// <item><b>Already floated.</b> Given up through <see cref="DropOwnFloat"/> — the ModBuild 232
    /// discipline, unchanged and not re-invented: <c>Sticky</c> drops, nothing is re-added to
    /// <see cref="OpenWindows"/>, and the ordinary release loop takes the panel, the grab bar, the X
    /// and the arc slot together while <c>CanvasConversion.Release</c> restores the exact 2D home.
    /// NOTHING IS WRITTEN TO THE GAME — no <c>Hide</c>, no <c>Escape</c>, no <c>CanvasGroup</c>.
    /// <b>Matched by its 2D HOME and not by its live parent</b>, which is the whole reason this
    /// sweep can work at all: a converted window has been REPARENTED under our own host, so
    /// <c>transform.IsChildOf(host)</c> is FALSE for exactly the windows that need retracting. The
    /// 2D home is <c>ConvertedPanel.OriginalParent</c>, the same field the release restores to.</item>
    /// <item><b>Enrolled or appended this pass but not yet converted.</b> No panel exists yet (the
    /// convert loop runs two phases later), so there is nothing for <see cref="DropOwnFloat"/> to
    /// find — the lever is membership in <see cref="OpenWindows"/>, and dropping it there is what
    /// stops the conversion from ever happening. That is the ModBuild 233 case exactly.</item>
    /// </list>
    ///
    /// <para><b>ORDER MAKES THIS SAME-TICK.</b> Tick runs PhasePolls → PhaseCatchAll (here) →
    /// PhaseRelease → PhaseConvert. The release loop's keep-alive test is
    /// <c>ContainsWindow(OpenWindows, w) || w.Sticky || ScriptedLevelMessageActive(w)</c>
    /// (ModalFallback.4.Tick.cs), and this sweep falsifies the first two — so a retracted descendant
    /// is released BEFORE the host is converted in the same tick, and the host then adopts it back at
    /// its 2D home. The player never sees a frame of the double state.</para>
    ///
    /// <para>The third term is left standing deliberately: a level-message window whose scripted
    /// message the game still considers displayed is NEVER released (a user-ruling do-no-harm guard),
    /// and this rule may not overturn a different rule from inside itself. If that ever holds a
    /// descendant floated, <see cref="AuditDoubleHosting"/> is what says so.</para>
    ///
    /// <para><b>NO NEW NUMBER, AND THE OLD ONE STOPS BEING A PREFERENCE.</b> A withdrawal is a second
    /// event class and the honest question is whether the catch-all churn fuse counts it. It does —
    /// on the DESCENDANT's name, one count per float that is later taken back (the count was already
    /// spent when the float happened; the retraction adds none of its own, and the tick after the
    /// host floats the descendant is refused by "parent wins" before the counter at all). So one
    /// revive/withdraw cycle costs the descendant exactly one churn count, and the worst case over a
    /// 60 s window is <c>MaxRevivalsPerWindow</c> cycles plus the ONE fallback float that happens
    /// once the revival budget is spent. That must stay at or under <see cref="ChurnMaxFloats"/> or
    /// the sub-view is session-suppressed BY NAME and shown nowhere — the ModBuild 231 defect. It is
    /// the same inequality StoryComposite.MaxWithdrawCycles derives, and it forces the number the
    /// revival budget already carried: see <see cref="SubViewRevival.MaxRevivalsPerWindow"/>, which
    /// now computes it instead of asserting it.</para>
    /// </summary>
    private static void RetractSubViewFloats(UIWindow host)
    {
        Transform hostT = host.transform;
        RetractScratch.Clear();

        // (1) Live floats of descendants — matched by their 2D HOME, because a converted window no
        //     longer lives under the host at all.
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            UIWindow? w = wp.Window;
            if (w == null || ReferenceEquals(w, host) || wp.UserClosing || !wp.Panel.IsAlive)
                continue;
            Transform home = wp.Panel.OriginalParent;
            if (home == null || !home.IsChildOf(hostT))
                continue;
            if (wp.HoverCard || !IsRetractableSubView(w))
                continue;
            if (!DropOwnFloat(w))
                continue;
            SubViewRetracted.Add(w);
            RetractScratch.Add(w);
            LogSubViewRetraction(w, host, hadPanel: true);
        }

        // (2) Windows this tick has enrolled or appended but not yet converted — the lever is
        //     membership, and the convert loop two phases from here reads exactly this list.
        for (int i = OpenWindows.Count - 1; i >= 0; i--)
        {
            UIWindow w = OpenWindows[i];
            if (w == null || ReferenceEquals(w, host))
                continue;
            if (!w.transform.IsChildOf(hostT))
                continue;
            if (!IsRetractableSubView(w) || IsMapRoomHoverCard(w))
                continue;
            OpenWindows.RemoveAt(i);
            if (!ContainsWindow(RetractScratch, w))
                LogSubViewRetraction(w, host, hadPanel: false);
        }
        RetractScratch.Clear();
    }

    /// <summary>
    /// MAY THE RETRACTION TAKE THIS DESCENDANT BACK? The rule is not "it is under the host" but
    /// "'parent wins' would have refused it in the first place" — a retraction that reclaims a window
    /// the suppression rule itself exempts would be a NEW suppression wearing the revival's clothes.
    ///
    /// <para>So it mirrors <see cref="CatchAllEligible"/>'s own exemptions, term for term: the
    /// PARALLEL-WINDOW FAMILIES (<see cref="NonBlockingMenus"/>,
    /// <see cref="MultiplayerRosterMenus"/>) are exempt there under the ModBuild 180 ruling — "Anders
    /// als in Flat soll es hier möglich sein mehrere Fenster parallel offen zu haben" — and their
    /// parents are not stable placement facts anyway (<c>MainOptionOptions</c> re-parents the options
    /// window at RUNTIME). Hover cards are excluded by the callers: a hover card is not a sub-view of
    /// anything, it is a transient popup whose own life cycle IS show/hide, and dropping one out of
    /// the open set for a single tick is the ModBuild 184/186 oscillation ("Es kommen nun gar keine
    /// Mouseovers mehr") re-created from a new direction.</para>
    /// </summary>
    private static bool IsRetractableSubView(UIWindow window)
        => !NonBlockingMenus.Contains(window.ID) && !MultiplayerRosterMenus.Contains(window.ID);

    /// <summary>The retraction's one line per sub-view type. <paramref name="hadPanel"/> separates
    /// the two populations honestly: a window with a live float has had a PANEL taken down, a window
    /// that was only in the open set was stopped BEFORE any conversion happened. Reporting the second
    /// as the first would be an instrument asserting an event it did not observe.</summary>
    private static void LogSubViewRetraction(UIWindow w, UIWindow host, bool hadPanel)
    {
        if (!SubViewRetractWarned.Add(w.name))
            return;
        VRLog.Warn("WorldUI", $"SUB-VIEW FLOAT RETRACTED: '{w.name}' (ID {w.ID}) "
                              + (hadPanel
                                  ? "was ALREADY FLOATED as a window of its own"
                                  : "was in the float set and about to be converted into a window of "
                                    + "its own (no panel existed yet)")
                              + $", but the liveness hold on its host '{host.name}' (ID {host.ID}) "
                              + "has just been lifted, so the host floats on THIS tick and renders it "
                              + "inside itself where the game lays it out. "
                              + (hadPanel
                                  ? "The float is given up the ModBuild 232 way — our own Sticky flag "
                                    + "is dropped and the window is not carried in the open set, so "
                                    + "the ordinary release loop (which runs BEFORE the convert loop "
                                    + "in this same tick) takes the panel, the grab bar, the close "
                                    + "cross and the arc slot down together and "
                                    + "CanvasConversion.Release restores the exact 2D home. "
                                  : "It is simply dropped from the open set, so the conversion never "
                                    + "happens at all. ")
                              + "NOTHING WAS WRITTEN TO THE GAME: no Hide, no Escape, no CanvasGroup "
                              + "— the window the game still needs is untouched. WHY THIS LINE "
                              + "EXISTS: ModBuild 233 floated 'UI Battle Goal Picker Window' one "
                              + "pass before the revival fired for a sibling, and it ended up "
                              + "adopted into the host AND converted into a host of its own at the "
                              + "same time (Player.log:15868 vs :15907, photographed as "
                              + "Questauswahl.jpg). IF THIS LINE APPEARS the race still happens and "
                              + "only the repair is working; if it never appears AND no CATCH-ALL "
                              + "line names this window, the PREVENTION in SubViewRevival is doing "
                              + "the job and there was nothing to take back.");
    }

    /// <summary>Per-window-name latch for the double-hosting warning — one line per window type.</summary>
    private static readonly HashSet<string> DoubleHostWarned = new();

    /// <summary>
    /// THE ONE STATE THE SCREENSHOT SHOWS AND NO OTHER LINE IN THIS PROJECT WOULD CATCH: a window
    /// that holds a CONVERSION OF ITS OWN while its canvas is also ADOPTED into another host's
    /// conversion (ModBuild 234).
    ///
    /// <para>Both hosts are named, because the pair is the diagnosis: in the ModBuild 233 log the
    /// picker's canvas sits in <c>GloomhavenVR.Panel_Modal_New Party display</c>'s adoption list
    /// (:15873) and in <c>GloomhavenVR.Panel_Modal_UI Battle Goal Picker Window</c> as its own
    /// conversion target (:15907). Neither host's own logging can see the other one.</para>
    ///
    /// <para><b>WHY THE TEST IS ON THE ADOPTION RECORD AND NOT ON THE HIERARCHY.</b> A conversion
    /// REPARENTS its target under its own host, so by the time the state exists the window is no
    /// longer a descendant of the host that adopted it and every containment test reads clean —
    /// which is precisely why this went unseen for a build. <c>ConvertedPanel.AdoptedCanvases</c>
    /// holds the Canvas by reference and survives the reparent.</para>
    ///
    /// <para><b>AND THE CANVAS IS THE WINDOW'S OWN, NOT ONE UNDER IT</b> —
    /// <c>GetComponent</c>, never <c>GetComponentInChildren</c>. A surface that docks a row from
    /// INSIDE a floated window legitimately adopts a canvas beneath it; that is not this state, and
    /// counting it would make this warning fire on working configurations
    /// ([[containment-is-not-identity]]).</para>
    ///
    /// <para>Cheap enough to run every tick: <c>Converted</c> holds at most a handful of windows and
    /// the inner walks are over live panels and their two-entry adoption lists.</para>
    /// </summary>
    private static void AuditDoubleHosting()
    {
        if (Converted.Count == 0)
            return;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            UIWindow? window = wp.Window;
            if (window == null || wp.Panel == null || !wp.Panel.IsAlive)
                continue;
            var own = window.GetComponent<Canvas>();
            if (own == null)
                continue;
            for (int p = 0; p < panels.Count; p++)
            {
                ConvertedPanel other = panels[p];
                if (other == null || ReferenceEquals(other, wp.Panel))
                    continue;
                bool adopted = false;
                for (int a = 0; a < other.AdoptedCanvases.Count; a++)
                {
                    if (ReferenceEquals(other.AdoptedCanvases[a].Canvas, own))
                    {
                        adopted = true;
                        break;
                    }
                }
                if (!adopted)
                    continue;
                if (DoubleHostWarned.Add(window.name))
                {
                    string ownHost = wp.Panel.HostGo != null ? wp.Panel.HostGo.name : "<destroyed>";
                    string otherHost = other.HostGo != null ? other.HostGo.name : "<destroyed>";
                    VRLog.Warn("WorldUI", $"DOUBLE HOST: '{window.name}' (ID {window.ID}) holds a "
                                          + $"conversion of its OWN ('{ownHost}') and its own Canvas "
                                          + "is at the same time in the nested-canvas adoption list "
                                          + $"of '{otherHost}'. THAT IS THE STATE IN "
                                          + ".planning/debug/Questauswahl.jpg — the character UI with "
                                          + "the battle-goal cards floating in front of it instead of "
                                          + "inside it — and it is not reachable by any hierarchy "
                                          + "test, because the second conversion reparented the "
                                          + "window out of the first host's subtree. IT MUST NEVER "
                                          + "APPEAR: a window belongs to exactly one host. Read the "
                                          + "SUB-VIEW REVIVAL, SUB-VIEW FLOAT RETRACTED and "
                                          + "PARENT WINS STOOD DOWN lines for these two names next, "
                                          + "in that order — they say which pass took the window and "
                                          + "which one failed to give it back.");
                }
                break;
            }
        }
    }

    /// <summary>Catch-all state teardown (module detach — mirrors the part-4 Detach resets).</summary>
    private static void CatchAllReset()
    {
        UnknownShown.Clear();
        CatchAllWarned.Clear();
        FloatRefusalTable.Reset(); // ModBuild 232 — the refusal table's edge state and lapse counters
        AncestorRefusalWarned.Clear();
        AncestorHeldOutWarned.Clear();
        SubViewRetractWarned.Clear();  // ModBuild 234 — the retraction's per-sub-view latch
        DoubleHostWarned.Clear();      // ModBuild 234 — and the double-hosting audit's
        SubViewRetracted.Clear();      // ModBuild 234 — the one-tick bridge to the release loop
        RetractScratch.Clear();
        NestedSubViewLogged.Clear(); // the enrolled path's twin of the line above (ModBuild 196)
        EmptyFloatWarned.Clear();    // ModBuild 226 — the empty-window refusal's per-window latch
        EmptyRefused.Clear();        // ModBuild 226 — and its suppression set
        EmptyHold.Clear();           // ModBuild 230 — the liveness rule's re-float hold
        SubViewRevival.Reset();      // ModBuild 233 — the revival budget and its two edge latches
        HudVerdict.Clear();
        FloatChurn.Clear();
        ChurnSuppressed.Clear();
        _rewardShowcaseOpen = false;
        ReleaseErrorFloat("module shutdown");
        ErrorModalOpen = false;
        ErrorScreenWanted = false;
        _errorOpenLogged = false;
        _errorConvertFailedLogged = false;
    }

    // =====================================================================================
    // Part 2 enrollment #1 — the MID-SCENARIO REWARD SHOWCASE (treasure chests).
    //
    // VERIFICATION RESULT (decompiled, 2026-08-01): the chest flow is Choreographer state
    // WaitingForRewardsProcess (Choreographer.cs:9600-9606 sets m_BlockClientMessage-
    // Processing=true, :2411-2432 waits) → ScenarioRewardManager.Show. ScenarioReward-
    // Manager is ABSTRACT with two mode-dependent subclasses:
    //  - CampaignScenarioRewardManager (campaign — the main mode) → CampaignRewards-
    //    Manager.ShowRewards (CampaignScenarioRewardManager.cs:21-47) → UICampaignReward-
    //    Window.Show → its own UIWindow.Show() (UICampaignRewardWindow.cs:46/62/132-139).
    //    That window's UIWindowID is SCENE-SERIALIZED and NOWHERE assigned in code —
    //    UIWindowID.RewardsPanel appears exactly once in the whole decompile (the enum
    //    member), so "this window's ID == RewardsPanel" is NOT provable from code. The
    //    FallbackIds entry (annotated UIRewardsManager) may therefore MISS this window.
    //  - GuildmasterScenarioRewardManager → UIRewardsManager.StartRewardsShowcase
    //    (GuildmasterScenarioRewardManager.cs:8-13) — the class the RewardsPanel
    //    annotation plausibly maps to (UIRewardsManager.cs:17/69/141).
    // Both windows DO pass through UIWindow.Show() → EvaluateAndTransitionToVisual-
    // State, so the transition patch sees them — but with an unknown serialized ID the
    // event path may drop them. ENROLLMENT: a poll on the game's own
    // ScenarioRewardManager.IsShown, resolving the concrete window per subclass —
    // provable from code where the ID is not. If the ID happens to BE RewardsPanel the
    // event path already carries it and AddPollWindow's dedupe makes this a no-op.
    // (The DistributeItemsRewards/DistributeGoldRewards popups never open mid-scenario:
    // CampaignScenarioRewardManager passes showAppliedEffects:false, and they are plain
    // GameObject toggles, not UIWindows — no enrollment needed.)
    // =====================================================================================

    private static bool _rewardShowcaseOpen;

    private static void AddRewardShowcaseWindow(bool inScenario)
    {
        UIWindow? win = null;
        if (inScenario && WorldUIConfig.ConversionActive
            && Singleton<ScenarioRewardManager>.IsInitialized)
        {
            ScenarioRewardManager mgr = Singleton<ScenarioRewardManager>.Instance;
            if (mgr != null && mgr.IsShown)
            {
                // Campaign chest showcase: manager (CampaignScenarioRewardManager.cs:15)
                // → rewardsWindow (CampaignRewardsManager.cs:90) → window
                // (UICampaignRewardWindow.cs:46) — all publicized.
                if (mgr is CampaignScenarioRewardManager campaign
                    && campaign.manager != null && campaign.manager.rewardsWindow != null)
                    win = campaign.manager.rewardsWindow.window;
                // Guildmaster showcase: UIRewardsManager.myWindow (UIRewardsManager.cs:69).
                else if (Singleton<UIRewardsManager>.IsInitialized)
                {
                    UIRewardsManager rm = Singleton<UIRewardsManager>.Instance;
                    if (rm != null)
                        win = rm.myWindow;
                }
            }
        }
        bool open = win != null && (win.IsOpen || win.IsVisible);
        LogPollTransition(ref _rewardShowcaseOpen, open,
            "reward showcase (ScenarioRewardManager.IsShown)");
        if (open)
            AddPollWindow(win);
    }

    // =====================================================================================
    // Part 2 enrollment #2 — GlobalErrorMessage (the Choreographer's ~230 catch blocks).
    //
    // VERIFICATION RESULT (decompiled, 2026-08-01): SceneController.GlobalErrorMessage
    // (SceneController.cs:126/136) is an ErrorMessage — a plain MonoBehaviour + IEscapable
    // (ErrorMessage.cs:22), NOT a UIWindow. It shows via gameObject.SetActive(true)
    // (ErrorMessage.cs:353 and siblings) and NEVER passes through UIWindow.EvaluateAnd-
    // TransitionToVisualState, so the Part-1 catch-all (fed by UIWindow_Transition_Patch)
    // can NOT catch it — verified, hence this dedicated poll. While it shows, the whole
    // game halts logic-level: Choreographer.Update early-outs (Choreographer.cs:2348),
    // SRL message processing stops (:1647/:3406), buttons/camera gate on ShowingMessage —
    // an error box hidden in VR = frozen game with no explanation. Dismissal is ONLY its
    // own buttons (ErrorMessage.cs:614/604 → the passed ErrorDelegate, usually
    // ErrorHandlingUnloadSceneAndLoadMainMenu), so the float carries NO mod X: Hide()
    // without the button's delegate would swallow the error and strand whatever recovery
    // the game intended. Poll reads the publicized backing FIELD _errorMessage — never
    // the GlobalErrorMessage getter, which lazily Addressables-INSTANTIATES the prefab
    // (SceneController.cs:620-631) and must not be triggered from a per-frame poll.
    //
    // MENU EXTENSION (2026-08-01, "Spielstand ist für eine Mehrspielerpartie" deadlock):
    // the same box also opens in MENU context (save-load MP/DLC prompts, load failures,
    // boot errors) where it is equally invisible — it lives on the persistent GlobalCanvas
    // that no captured camera carries, so the Menu2D flat screen cannot show it either.
    // TickErrorMessage therefore floats it in Menu2D too (kill-switch [WorldUI]
    // MenuPopupFloat; the ModalUI lock and the screen fallback stay scenario-only).
    // =====================================================================================

    /// <summary>True while the error box shows in a scenario → ModalUI (a genuine blocker).</summary>
    internal static bool ErrorModalOpen { get; private set; }

    /// <summary>True when the error box is open but could not be floated → raise the screen.</summary>
    internal static bool ErrorScreenWanted { get; private set; }

    private static ConvertedPanel? _errorPanel;
    private static GrabbableModal? _errorGrab;
    private static bool _errorOpenLogged;
    private static bool _errorConvertFailedLogged;

    /// <summary>
    /// The error box is NOT a UIWindow, so it has no <c>WindowPanel</c> record — these three
    /// fields are its private copy of the state a floated window keeps there, purely so the ONE
    /// pose re-place (<see cref="TickPoseRePlace"/>) covers this float too: it is content-fitted
    /// like any other modal, so its spawn clamps are computed from the same PRE-fit rect.
    /// </summary>
    private static float _errorExtraScale = WindowScaleFactor;
    private static SpawnAnchor _errorSpawnAnchor;
    private static bool _errorPoseRePlaceDone;

    /// <summary>
    /// Per-tick error-box service (called from Tick before the lock/screen policy):
    /// level-triggered like the other polls. Floats the ErrorMessage root directly via
    /// CanvasConversion (the window-float pattern minus UIWindow specifics); on
    /// conversion failure ErrorScreenWanted raises the full flat screen instead. Not
    /// gated by the catch-all kill-switch — this is an explicit enrollment, same tier
    /// as the story/level-message/dialog polls.
    /// </summary>
    private static void TickErrorMessage(bool inScenario)
    {
        ErrorMessage? err = null;
        SceneController controller = SceneController.Instance;
        if (controller != null)
            err = controller._errorMessage; // backing field — the getter would instantiate
        bool showing = err != null && err.ShowingMessage;

        if (!_errorOpenLogged && showing)
            VRLog.Warn("WorldUI", "MODAL FALLBACK poll: GlobalErrorMessage SHOWING — the game is " +
                                  "halted until its button is pressed; floating the error box in VR.");
        else if (_errorOpenLogged && !showing)
            VRLog.Info("WorldUI", "Modal fallback poll: GlobalErrorMessage closed.");
        _errorOpenLogged = showing;

        // The error blocks the game wherever it appears. The LOCK (ModalUI) stays scenario-only
        // like the polls — in Menu2D the mode composition ignores aux-modal anyway.
        ErrorModalOpen = showing && inScenario;

        // MENU-context float (user deadlock: loading a multiplayer save from the MAIN MENU —
        // SaveData.cs:232/244 `ShowGenericMessage("Consoles/CREATE_NEW_LOCAL_SAVE" /
        // "Consoles/OVERWRITE_EXISTING_LOCAL_SAVE")`, German "Dieser Spielstand ist für eine
        // Mehrspielerpartie…"; same box: DLC_REQUIRED, load-failure ERROR_SCENE_*): the old
        // scenario gate here rested on "the Menu2D flat screen already shows the 2D stack" —
        // which is FALSE for exactly this box. It is SetActive-shown on the persistent
        // boot-scene GlobalCanvas (SceneController.cs:92/620-631), a canvas no captured CAMERA
        // carries, so the flat screen never composited it: invisible in VR while the game
        // raycast-blocked the whole menu behind it (ErrorMessage's full-screen panel +
        // MainMenuUIManager.RequestDisableInteraction) — nothing clickable, waiting forever.
        // Every OTHER menu popup (confirmation-box family, EULA, sign-out, lobby prompts —
        // menu-popup inventory 2026-08-01) lives in the menu canvas hierarchy, IS captured and
        // stays operable via laser→virtual mouse, so the scenario gate for the WINDOW float
        // machinery stands untouched; only this capture-invisible error family floats in menu.
        // Deliberately independent of [WorldUI] ModalStyle: that setting picks between two ways
        // of SHOWING a scenario fallback window, and neither screen path can show this canvas —
        // floating is the only visibility there is. Always on — user ruling 2026-08-11:
        // essential (the former [WorldUI] MenuPopupFloat kill switch left a menu-context
        // hard deadlock when off).
        bool menuFloat = showing && !inScenario
                         && VRModeStateMachine.CurrentMode == VRMode.Menu2D;

        bool wantFloat = ((ErrorModalOpen && WorldUIConfig.ModalWindowStyle) || menuFloat)
                         && !FlatScreen.ManualScreenActive && WorldUIConfig.ConversionActive;

        if (!wantFloat || _errorPanel is { IsAlive: false })
        {
            ReleaseErrorFloat(showing ? "float no longer wanted" : "error box closed");
            // style=screen (or float impossible): the screen is the fallback visibility.
            ErrorScreenWanted = ErrorModalOpen && !wantFloat && !WorldUIConfig.ModalWindowStyle;
            if (!showing)
            {
                ErrorScreenWanted = false;
                _errorConvertFailedLogged = false;
            }
            return;
        }
        if (_errorPanel != null)
        {
            // Same one-surface-must-stay-clickable exemption as the floated windows.
            GraphicRaycaster raycaster = _errorPanel.HostRaycaster;
            if (raycaster != null && !raycaster.enabled)
                raycaster.enabled = true;
            _errorGrab?.Tick();
            return;
        }
        if (ErrorScreenWanted)
            return; // conversion already failed this open — screen path holds (retry next open)

        try
        {
            var rect = err!.transform as RectTransform;
            ConvertedPanel? panel = rect == null
                ? null
                : CanvasConversion.Convert(rect, "Modal_GlobalErrorMessage", pokeable: true,
                    sortingOrder: ModalHostSortingOrder, useModLayer: true);
            if (panel == null)
            {
                if (!_errorConvertFailedLogged)
                {
                    _errorConvertFailedLogged = true;
                    VRLog.Warn("WorldUI", "MODAL WINDOW: GlobalErrorMessage could not be floated " +
                                          "(no rect / conversion returned null) — raising the flat " +
                                          "screen for it instead.");
                }
                ErrorScreenWanted = true;
                return;
            }
            float extraScale = DeriveWindowScale(panel);
            // Keep the placement inputs so the one-shot re-place can replay them once this float's
            // content fit lands (see TickPoseRePlace) — invalid anchor if no head pose existed.
            _errorSpawnAnchor = PlaceAtHmd(panel, extraScale, Converted.Count)
                ? s_lastSpawnAnchor
                : default;
            _errorExtraScale = extraScale;
            _errorPoseRePlaceDone = false;
            var grab = new GrabbableModal();
            grab.Build(panel, extraScale, "GlobalErrorMessage");
            _errorPanel = panel;
            _errorGrab = grab;
            VRLog.Info("WorldUI", "MODAL WINDOW: GlobalErrorMessage floated in front of the HMD " +
                                  "(grabbable, NO mod X — its own buttons are the only exit; " +
                                  "Hide() would swallow the error and skip the game's recovery).");
        }
        catch (Exception ex)
        {
            if (!_errorConvertFailedLogged)
            {
                _errorConvertFailedLogged = true;
                VRLog.Error("WorldUI", "MODAL WINDOW: floating GlobalErrorMessage FAILED " +
                                       $"({ex.GetType().Name}: {ex.Message}) — raising the flat " +
                                       "screen for it instead.");
            }
            ErrorScreenWanted = true;
        }
    }

    /// <summary>Release the error-box float (restores its exact 2D home; reversible mutation).</summary>
    private static void ReleaseErrorFloat(string reason)
    {
        if (_errorPanel == null)
            return;
        _errorGrab?.Destroy();
        _errorGrab = null;
        CanvasConversion.Release(_errorPanel);
        _errorPanel = null;
        // Drop the re-place state with the float: the next open is a fresh spawn (its own anchor).
        _errorSpawnAnchor = default;
        _errorPoseRePlaceDone = false;
        VRLog.Info("WorldUI", $"MODAL WINDOW: GlobalErrorMessage float released ({reason}) — " +
                              "restored to its 2D home.");
    }
}
