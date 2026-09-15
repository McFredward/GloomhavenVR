using GloomhavenVR.Core;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// ModBuild 381 — A WINDOW THAT DEMANDS A DECISION MUST NOT OFFER A WAY OUT OF IT.
///
/// <para><b>USER REPORT (2026-09-03), verbatim:</b> <i>"Das Fenster 'Begegnung!' hat ein 'X' zum
/// schließen. Das darf nicht sein - hier MUSS eine Entscheidung getroffen werden, drückt ein User
/// das X kommt das einem Deadlock gleich. Prüfe nochmal das jedes Fenster bei dem zwingend eine
/// Entscheidunge getroffen werden muss auch kein X hat."</i> He is right, and the deadlock is
/// literal rather than figurative — see THE STRAND below.</para>
///
/// <para><b>WHY THIS FILE EXISTS AT ALL: THE X GATE WAS A BLACKLIST.</b> The gate in
/// <c>ModalFallback.8.Convert.cs</c> reads <c>if (!isResultsPanel &amp;&amp; !isStoryBox &amp;&amp; …)
/// ModalCloseButton.Attach(…)</c> — every converted window gets an X unless it is one of eight
/// named exclusions. Its own comment argues that is safe for an UNKNOWN window "because the X runs
/// the SAME CloseFloatedWindow the modal escape chord already applies to EVERY floated window
/// without a whitelist". That argument is about the MECHANISM being uniform. It says nothing about
/// the CONSEQUENCE, and the consequence is not uniform: on most windows a Hide() is a close, and on
/// this one it is a campaign the player cannot resume. A default-to-X policy is a bet that every
/// window nobody has looked at yet is harmless to hide, settled once per unknown window, and the
/// stake is a save the player has to abandon. 'UI Event Window' is the bet being lost.</para>
///
/// <para><b>THE STRAND, PROVEN IN THE DECOMPILE — AND ONE CORRECTION TO THE OBVIOUS READING.</b>
/// <c>UIEventPanel</c> (decompiled/GH.Runtime/UIEventPanel.cs) is
/// <c>[RequireComponent(typeof(UIWindow))]</c>, :26, and caches its window with
/// <c>GetComponent&lt;UIWindow&gt;()</c> in Awake, :137 — so the panel and the window are ONE
/// GameObject and the mod's <c>window.Hide()</c> IS <c>myWindow.Hide()</c>. It holds
/// <c>private Action onEventCompleted</c>, :114, taken in <c>StartEvent(CRoadEvent, Action)</c>,
/// :178-183, and invoked at :812 inside <c>OnDistributedRewards</c>. The tempting conclusion is
/// that hiding the window strands that callback and freezes the campaign map. <b>THAT IS WRONG,
/// and it was checked rather than assumed:</b> Awake ALSO does
/// <c>myWindow.onHidden.AddListener(OnFinishedEvent)</c>, :139, so a bare <c>Hide()</c> DOES enter
/// <c>OnFinishedEvent</c>, :747, and the reward chain does eventually reach :812 on this client.
/// The local map most likely comes back. The defect is the four things below, each of which is
/// sufficient on its own, and the multiplayer one is not arguable at all.</para>
///
/// <para><b>(1) THE DECISION IS SKIPPED, WHICH IS EXACTLY THE REPORT.</b> The options are
/// <c>EventButton</c>s built from <c>CRoadEventScreen.Options</c>, :355-390, and every press routes
/// through <c>ContinueEvent</c>/<c>UpdateScreen</c>. The X resolves the encounter with whatever
/// <c>appliedRewards</c> happens to hold and NO option chosen — the cross decides the outcome
/// instead of the player. "hier MUSS eine Entscheidung getroffen werden."</para>
///
/// <para><b>(2) THE EVENT CARD IS NEVER RETURNED TO THE DECK, AND THAT IS SAVED.</b>
/// <c>FinalScreen()</c>, :667-708, is the ONLY site in the class that calls
/// <c>cCardDeck.RemoveCard(eventData.ID)</c> and, on the <c>ActionAtEventEnd.Reuse</c> branch,
/// <c>cCardDeck.AddCard(eventData.ID, Bottom)</c>. It is reached only from <c>UpdateScreen</c>
/// after a button press. Hide() skips it, so the drawn card is never taken off the deck — and
/// <c>MapChoreographer.FinishCityEvent</c> then calls
/// <c>SaveData.Instance.Global.CurrentAdventureData.Save()</c>, :2097, persisting the deck that was
/// never advanced.</para>
///
/// <para><b>(3) MULTIPLAYER — THE UNAMBIGUOUS DEADLOCK, and the standing MP requirement settles it
/// on its own.</b> The wire message is <c>GameActionType.ContinueRoadEvent</c>, sent ONLY from
/// <c>ContinueEvent</c>, :606 and :610, and <c>CompleteEvent</c>, :724 — i.e. only from an
/// <c>EventButton</c> press — and replayed on the far side by <c>ClientContinueRoadEvent</c>,
/// :869-879, which looks the button up by ID and THROWS when there is none. A host who presses the
/// X hides his own window and sends NOTHING, so every remote client's event window stays open with
/// no action that can ever arrive to close it, parked at
/// <c>ActionProcessor.SetState(ProcessOneAndHalt, ActionPhaseType.MapEvent)</c>, :162 and :397,
/// while the host walks on. The failure is ASYMMETRIC, which is why it cannot be argued away as
/// "the player chose to close it". Closing a window is not replicated and must not be — this is a
/// control that must not exist, not a sync gap to be filled.</para>
///
/// <para><b>(4) AND THE DEACTIVATION PATH FIRES NOTHING AT ALL.</b> <c>OnDisable</c>, :815-818,
/// calls only <c>OnHidden</c>, :820-833, which stops the coroutines and all five animators, unloads
/// the image and calls <c>controllerArea.Destroy()</c> — it DESTROYS the input area and never
/// reaches <c>OnFinishedEvent</c>. Whether a <c>Hide()</c> ends in a deactivated GameObject is
/// decided by <c>UIWindow.m_DisableOnZeroAlpha</c>, which is <c>[SerializeField]</c> and therefore
/// not readable from the decompile. So the silent-strand path is live and cannot be ruled out from
/// source; it is simply not the one that is PROVEN, and this comment says which is which.</para>
///
/// <para><b>WHAT THE CITY-EVENT CONTEXT ADDS.</b> <c>OpenCityEventInternal</c>,
/// MapChoreographer.cs:2076-2084, hides the party display, the quest log and
/// <c>UIGuildmasterHUD</c>, sets <c>AdventureState.MapState.CanDrawCityEvent = false</c>, and :2010
/// calls <c>actionProgression.RequestPauseActions(this)</c>. Only <c>FinishCityEvent</c>,
/// :2087-2119, Shows those three back and resumes the actions. On the Hide() path that callback
/// does still run, so this is NOT claimed as the local symptom — it is the size of what rides on
/// the chain, and the reason path (4) would be unrecoverable if it is the one taken.</para>
///
/// <para><b>THE RULE THIS SHIPS, AND ITS TWO TERMS.</b> Term 1 is an IDENTITY match on the window
/// whose strand is proven above. Term 2 is the GAME'S OWN REFUSAL: a window whose
/// <c>escapeKeyAction</c> is <c>None</c> is one <c>UIWindow.Escape()</c> declines to close, at
/// UIWindow.cs:713-716, and which both <c>HideOrShowWindows</c> and <c>ForceHideWindows</c> skip.
/// The mod's X must not do what the game's own ESC key refuses to do. The clearest evidence that
/// <c>None</c> carries exactly this meaning is <c>DialogPopup.cs:235</c> and <c>:316</c>, which
/// write <c>escapeKeyAction = (allowHide ? Hide : None)</c> — the engine's own "may this be
/// dismissed" bit, mapped onto the enum one-to-one.</para>
///
/// <para><b>WHY TERM 2 IS THE NET AND NOT THE MECHANISM — the honest limit.</b> The integrator's
/// hypothesis was that Term 2 alone could replace the blacklist. It cannot, for a reason that is
/// worth writing down rather than rediscovering:</para>
/// <list type="number">
/// <item><b>Its value is not knowable from the decompile.</b> <c>m_EscapeKeyAction</c> is
/// <c>[SerializeField]</c>, UIWindow.cs:82-83, so for any window whose code never assigns it the
/// value lives in prefab data inside the asset bundles. <c>UIEventPanel</c> contains no assignment
/// to <c>escapeKeyAction</c> anywhere. The C# field initialiser is <c>Hide</c>. So there is NO
/// static evidence that Term 2 would fire on the very window that was reported — and a fix gated
/// behind a term that may never evaluate true is a fix that ships and does nothing, which this
/// project has done before. Term 1 is what makes the report provably fixed.</item>
/// <item><b>It over-fires on the mod's own settings pane.</b> <c>VROptionsTab.LeaveSharedStacks</c>
/// (WorldUI/Options/VROptionsTab.1.Inject.cs:594-595) sets <c>escapeKeyAction = None</c> on
/// <c>GloomhavenVR.OptionsTabWindow</c> deliberately, to leave <c>UIWindowManager</c>'s single
/// shared escapable list so one ESC press cannot pick between our pane and the game's. That is a
/// REGISTRY reason, not a "you may not close this" reason, and the method's own doc comment names
/// the corner X as the compensation it keeps. A blanket Term 2 would silently delete that X. Hence
/// the one exemption below, keyed on the name <c>MenuWindowFamily</c> already treats as this
/// pane's identity.</item>
/// <item><b>Its value is per-Show, and the X decision is per-convert.</b> Nine game classes rewrite
/// <c>escapeKeyAction</c> from inside <c>Show</c>/<c>OnShown</c> (DialogPopup, ESCMenu,
/// LevelMessageUILayoutGroup, UIOptionsWindow, the three character-creator steps,
/// UITutorialSelectorWindow, UIAdventurePartyAssemblyWindow). Reading it once is a snapshot. The
/// snapshot is taken at the right moment — the game Shows a window from its own Update and the
/// conversion runs on the NEXT tick, which is what MODAL PRE-CONVERT BLACKOUT exists to cover — so
/// any Show-time write has already landed. But a window whose value changes while it stays open
/// keeps the X decision it was converted with.</item>
/// </list>
///
/// <para><b>AND THE ESCAPE CHORD, WHICH IS NO LONGER A HOLE.</b> The long-hold non-dominant A/X
/// chord routes through the same <c>CloseFloatedWindow</c> and could strand this window in exactly
/// the same way, so withholding only the X would have left half the defect standing. The first
/// version of this file recorded that as an accepted residual, on the grounds that the alternative
/// — blocking the chord — would leave the player with no hatch out of a float that failed to draw
/// its own buttons, which the ModBuild 380 log shows this very window doing (MODAL APPEAR HELD /
/// MODAL REVEAL FORCED after 600 ms, lines 7853-7856). Both of those options assumed the chord's
/// only available action is to HIDE the game's window. There is a third, and it is now what the
/// chord does here: RELEASE THE FLOAT AND RAISE THE 2D COMPOSITE, writing nothing to the game. The
/// player keeps a guaranteed way forward, the window is never hidden, and no waiter is stranded.
/// See <see cref="RescueForMandatoryDecision"/> for the mechanism, for the map-room check that had
/// to pass before it could work in the room this report came from, and for who owns the release.
/// Every OTHER window's chord behaviour is untouched.</para>
/// </summary>
internal static partial class ModalFallback
{
    /// <summary>
    /// True when this window demands a decision the player may not walk away from, so the mod must
    /// NOT draw a close X on it. <paramref name="reason"/> receives the term that decided, phrased
    /// as what was matched — never as a mechanism this predicate cannot observe.
    ///
    /// <para><b>THIS IS THE UNION AND IT ANSWERS ONE QUESTION: MAY THE MOD OFFER A WAY OUT OF THIS
    /// WINDOW?</b> The X gate (ModalFallback.8.Convert.cs) and the escape chord
    /// (ModalFallback.7.Close.cs) are the two callers that ask it, and for both of them the derived
    /// net below is a good answer — a window the GAME will not close on ESC is a window the mod
    /// must not close either. A caller that instead wants to know WHAT this window is must use
    /// <see cref="ClassifyMandatoryDecision"/> and
    /// <see cref="MandatoryDecisionTerms.IdentifiesTheWindow"/>; see
    /// <see cref="MandatoryDecisionTerm"/> for the seventeen map-room windows that were torn down
    /// because those two questions shared one predicate.</para>
    /// </summary>
    internal static bool IsMandatoryDecision(UIWindow? window, out string reason)
    {
        // IntroductionManager queues the next hint from its native continue callback.
        // A generic modal X/Hide bypasses that callback and leaves the queue unresolved.
        // Its layout group is distinct from LevelMessagesUIHandler's scenario tutorial.
        if (IsIntroductionWindow(window))
        {
            reason = "introduction message — its native continue callback owns the message queue";
            return true;
        }
        return MandatoryDecisionTerms.IsMandatory(ClassifyMandatoryDecision(window, out reason));
    }

    /// <summary>
    /// The same sweep as <see cref="IsMandatoryDecision"/>, answering WHICH TERM matched instead of
    /// merely whether one did. Terms are tested in the order below and the FIRST match wins, so an
    /// identity always beats the derived net — which matters, because the encounter window carries
    /// both and the log line must name the identity.
    /// </summary>
    internal static MandatoryDecisionTerm ClassifyMandatoryDecision(UIWindow? window, out string reason)
    {
        reason = string.Empty;
        if (window == null)
            return MandatoryDecisionTerm.None;

        // TERM 1 — IDENTITY, and IS-A rather than containment. UIEventPanel is
        // [RequireComponent(typeof(UIWindow))] (UIEventPanel.cs:26) and caches the window off its
        // OWN GameObject in Awake (:137), so a GetComponent on the window IS the panel test — the
        // same construction the loadout-screen exclusion relies on. The ID is checked as well
        // because it is what the hardware log prints ('UI Event Window' (ID EventsPanel), ModBuild
        // 380 log line 7846, which is the reported X being attached), but it is serialized in the
        // prefab and so is evidence rather than proof; the component is the proof.
        if (window.GetComponent<UIEventPanel>() != null || window.ID == UIWindowID.EventsPanel)
        {
            // THE REASON NAMES ONLY WHAT WAS PROVEN. An earlier draft of this string said the
            // callback is stranded and the campaign map freezes; UIEventPanel.cs:139 adds
            // OnFinishedEvent as an onHidden listener, so on THIS client it is not. A log line
            // asserting a mechanism it cannot observe is the failure this project keeps having to
            // retract, so the wire consequence — which is provable and asymmetric — leads.
            reason = "road/city ENCOUNTER window ('Begegnung!') — the player MUST answer it "
                   + "(UIEventPanel, decompiled UIEventPanel.cs). MULTIPLAYER, THE PROVEN HALF: the "
                   + "only sender of GameActionType.ContinueRoadEvent is an EventButton press "
                   + "(:606, :610, :724), so an X sends NOTHING and every remote client's event "
                   + "window stays open forever at ProcessOneAndHalt on ActionPhaseType.MapEvent "
                   + "(:162, :397) while this client walks on. SINGLE PLAYER: the option chosen "
                   + "decides the outcome, and FinalScreen (:667-708) — the only code that takes the "
                   + "drawn card off the event deck — is reached only from a button press, while "
                   + "FinishCityEvent then saves that unadvanced deck. NOT CLAIMED: that the local "
                   + "callback strands — myWindow.onHidden fires OnFinishedEvent (:139), so it "
                   + "usually does not; the OnDisable path (:815-818) fires nothing, but whether a "
                   + "Hide reaches it depends on the serialized m_DisableOnZeroAlpha";
            return MandatoryDecisionTerm.EncounterPanel;
        }

        // TERM 1b — THE THREE OTHER UIWindow-BASED WINDOWS THE AUDIT FOUND, each verified the same
        // way and each carrying a waiter with NO onHidden fallback. The user asked for the sweep in
        // as many words ("Prüfe nochmal das jedes Fenster bei dem zwingend eine Entscheidunge
        // getroffen werden muss auch kein X hat"), so finding them and not enrolling them would be
        // answering the report and refusing the request.
        //
        // THE FILTER THAT MADE THIS LIST SHORT: only a window that IS a UIWindow can be converted,
        // so only such a window can be given an X. UIDistributeReward, UIAbilityCardPicker (the Doom
        // picker), UIElementPicker, UIOptionPicker and UIMultiplayerImportProgressWindow all hold
        // waiters with no hide fallback too, and every one of them was left out because it carries
        // no UIWindow at all — their Hide() is a bare SetActive(false) on a plain GameObject. They
        // are unreachable by this gate, not overlooked by it.
        //
        // WHAT WOULD ACTUALLY REACH THEM, so this note is a lead and not a disclaimer: they are
        // floated (when they are floated at all) by WorldUI/Surfaces/FloatingDecisionSurfaces, a
        // completely separate path that finds a plain GameObject panel via ShownPanel() rather than
        // by tracking UIWindow opens. Its equivalent of this decision is its own chrome, so the
        // enrolment would go THERE — as a term in the surface that decides whether to draw a
        // dismiss affordance — and it would need the same treatment for its own escape hatch. That
        // file is owned by another lane, so nothing here touches it.
        //
        // AND FOR ONE OF THEM "UNREACHABLE BY THIS GATE" IS TRUE BUT NOT THE WHOLE STORY:
        // UIDistributeReward is the window behind the four rounds of deadlock reports that produced
        // FlatScreen.RequestRescueScreen in the first place — the ModBuild 373 comment on
        // WantVisible names UIDistributeRewardManager.IsDistributing standing true on a locked
        // campaign map as the state it was built for. It is therefore already covered by a REMEDY,
        // just not by a policy: a watchdog raises the 2D composite when it detects the stall,
        // rather than a gate refusing to offer a close in the first place. That is also the same
        // mechanism this file's chord branch now uses, which is why the two ought to stay in step.
        if (window.GetComponent<UIRewardsManager>() != null)
        {
            // THE STRONGEST FINDING OF THE WHOLE AUDIT, AND THE MECHANISM IN FULL — because it is
            // an UNCONDITIONAL INFINITE LOOP, not merely an unfired callback.
            //
            // [RequireComponent(typeof(UIWindow))] :17, GetComponent in Awake :100 — one GameObject,
            // so the mod's window.Hide() is myWindow.Hide().
            //
            // THE THREE FACTS, EACH CHECKED IN THE BODY RATHER THAN GREPPED FOR:
            //   (a) THE FLAG IS POLLED, NOT AWAITED. `processingRewards` :79 is set true at :180 and
            //       the process coroutine is `while (processingRewards)` at :188. Nothing else ends
            //       that loop. A flag that is polled forever is a spin, not a pause.
            //   (b) THE ONLY WRITER OF false IS EndProcess. :510, inside EndProcess :501, which also
            //       invokes onProcessEnded :522 — the only invocation in the class.
            //   (c) EndProcess is what CALLS myWindow.Hide(instant: true), :511 — it is the CAUSE of
            //       the hide, not something a hide triggers. And the class registers NO onHidden
            //       listener at all: unlike ItemCardPicker (:60) and TakeDamagePanel (:184), which
            //       at least subscribe something, there is no hide path back into this class.
            //
            // PUT TOGETHER: a Hide() arriving from OUTSIDE EndProcess is the effect without its
            // cause. `processingRewards` is never cleared by anyone, so the `while` at :188 runs for
            // the rest of the session and `onProcessEnded` never fires — an unconditional infinite
            // loop, on a hidden window, with no user-visible symptom except that the game stops
            // advancing. That is a strictly worse failure than the encounter window's, which at
            // least has an onHidden listener (UIEventPanel.cs:139) that usually rescues it.
            //
            // AND IT IS ONE STEP DOWNSTREAM OF THE REPORTED WINDOW: the encounter flow hands off to
            // exactly this showcase for a non-campaign event (UIEventPanel.cs:774), so an X here
            // reproduces the reported deadlock one window later and in a harder-to-see form.
            reason = "reward showcase process (UIRewardsManager) — its onProcessEnded (:77) fires "
                   + "only from EndProcess (:501/:522), the class registers NO onHidden listener, "
                   + "and EndProcess is what calls myWindow.Hide rather than what a hide triggers; "
                   + "an outside Hide leaves processingRewards true and the `while (processingRewards)` "
                   + "coroutine at :188 spinning with the callback unfired";
            return MandatoryDecisionTerm.RewardShowcase;
        }
        if (window.GetComponent<ItemCardPicker>() != null)
        {
            // [RequireComponent(typeof(UIWindow))] :9, GetComponent in Awake :59. It DOES register
            // window.onHidden.AddListener(OnHidden) at :60 — and OnHidden, :80-84, is exactly
            // `ClearContent(); controllerArea.Destroy();`. It tears the picker down and fires
            // neither onConfirmPressed (:39, invoked only at :159) nor onItemsSelected (:41). A
            // registered hide listener is not a fallback; this is why the audit read every body
            // rather than grepping for the subscription.
            reason = "item card picker (ItemCardPicker) — the player must pick or confirm. Its "
                   + "onHidden listener (:60) is OnHidden (:80-84), which only clears the content "
                   + "and destroys the controller area; onConfirmPressed (:39) fires only at :159 "
                   + "and onItemsSelected (:41) only from a selection, so a hide drops both";
            return MandatoryDecisionTerm.ItemCardPicker;
        }
        if (window.GetComponent<TakeDamagePanel>() != null)
        {
            // [RequireComponent(typeof(UIWindow))] :18, GetComponent in Awake :183. Its onHidden
            // listener is ClearButtonsSelected (:184) — button visuals, nothing else. The resolution
            // is a WIRE action: GameActionType.TakeDamage on ActionPhaseType.TakeDamageConfirmation,
            // sent only at :774. A hide sends nothing, so the confirmation phase never resolves.
            // Same shape as the encounter window and on the scenario board rather than the map.
            reason = "take-damage confirmation (TakeDamagePanel) — the player must answer it. Its "
                   + "onHidden listener is ClearButtonsSelected (:184), which only clears button "
                   + "visuals; the phase is resolved by GameActionType.TakeDamage on "
                   + "ActionPhaseType.TakeDamageConfirmation, sent only at :774, so a hide sends "
                   + "nothing and leaves the confirmation phase unresolved";
            return MandatoryDecisionTerm.TakeDamagePanel;
        }

        // TERM 2 — THE GAME'S OWN REFUSAL, as a net under every window nobody has classified yet.
        // UIWindow.Escape() returns false without hiding on None (UIWindow.cs:713-716), i.e. the
        // game declines to close this window on ESC; DialogPopup writes exactly this value for its
        // own allowHide=false dialogs (:235, :316). This is the term that makes the gate stop being
        // a bet on unknown windows: an unknown window is now only given an X if the game itself
        // would let ESC close it.
        //
        // AND IT IS A NET, NOT AN IDENTITY — which is now stated by the VALUE this returns and not
        // only by the reason string. It is the last term on purpose: it fires for every window the
        // four identity terms above did not name, and in the 3D map room that is nearly all of them
        // (the merchant and the temple both carry escapeKeyAction None; the mod prints so itself in
        // the reason below every time it floats one). A caller reading this as "the player owes this
        // window an answer" would be reading the ESC key's policy as a fact about the window; see
        // MandatoryDecisionTerm for the seventeen destinations that cost.
        if (window.escapeKeyAction == UIWindow.EscapeKeyAction.None)
        {
            // THE ONE EXEMPTION, and it is the mod's own pane rather than a game window. See the
            // class comment: VROptionsTab sets None to leave UIWindowManager's shared escapable
            // list, not to forbid closing, and its X is the compensation that method's own comment
            // promises. Keyed on the name MenuWindowFamily already uses as this pane's identity.
            if (window.name == MenuWindowFamily.VROptionsWindowName)
                return MandatoryDecisionTerm.None;

            // THE SECOND EXEMPTION — THE GUILDMASTER DESTINATIONS (user item 3, 2026-09-05:
            // "Der Händler und co. haben kein X mehr zum schließen. Will ich aber haben.").
            //
            // IT IS AN IDENTITY AND A CONSEQUENCE, WHICH IS WHAT THE DERIVED NET IS NOT.
            // MapRoom.GuildmasterDestinations.IsDestination matches the FIVE window classes off
            // UIGuildmasterHUD's own serialized references — UIShopItemWindow, UITempleWindow,
            // UITrainerWindow, UINewEnhancementWindow, UITownRecordsWindow — each by a GetComponent
            // on the window's OWN GameObject (all five carry [RequireComponent(typeof(UIWindow))]).
            // That names WHAT this window is. The net names only what the ESC key does, and in the
            // 3D map room it fires for every window there is: the ModBuild 447 hardware log has
            // escapeKeyAction=None on all nine no-X windows on the host, and the co-player's log
            // carries NO 'MODAL WINDOW X AUDIT' line at all — not one window on that client got a
            // cross all session.
            //
            // THE CONSEQUENCE TEST IS ANSWERED BY CODE THAT ALREADY SHIPS, not by an argument.
            // ModalFallback.CloseFloatedWindow is this family's vetted close and has been since
            // ModBuild 184/226/230: it flags the parallel float for release, runs the mode's own
            // Exit through GuildmasterDestinations.LeaveMode (the ONLY thing that takes the party
            // display back out of selection mode), then hides the game window. The room ALREADY
            // runs it on this window twice over — a second press of the mode's own table cap
            // (GuildmasterDestinations.CloseMode, the "Tasten sollen Toggles sein" ruling), and the
            // point-of-no-return sweep, which closes all five destinations at the rising edge and
            // did so on BOTH clients in the 447 logs ("CLOSED 8 of 8 NAMED member(s)"). A window
            // the mod already closes on its own initiative is a window the mod may offer a cross
            // for; withholding the cross was protecting nothing.
            //
            // AND IT DOES NOT RE-OPEN THE ModBuild 445 WOUND. That build tore down 7 merchants and
            // 10 temples by reading THIS SAME NET as "the player has answered a decision" and
            // spending a float's stickiness — a caller that must use IdentifiesTheWindow, which
            // still answers false here (this exemption removes a term, it does not add one). The
            // question this predicate answers is "may the mod offer a way out", and for these five
            // the answer is yes and always was; only the net said otherwise.
            //
            // SCOPE, STATED. The five destinations and nothing else. The quest popups
            // (UIQuestPopupManager's three) keep the net: the travel confirm and the multiplayer
            // ready toggle are PARKED INSIDE the quest window by MapTravelConfirm/MapQuestReadyUp,
            // so a cross there would take the player's confirm off the table with it, and that
            // window's layout is another lane's this round. The character screen, the quest log,
            // the story box, the loadout screen and the encounter window are all named by OLDER
            // clauses that are evaluated BEFORE this one, so none of them can reach this line.
            //
            // AND IT IS GATED ON THE 3D ROOM STANDING, which is not belt-and-braces but the
            // consequence test again. What makes the cross safe is LeaveMode running the mode's own
            // Exit, and LeaveMode's FIRST line is `if (!MapRoomDriver.Active || !IsDestination(...))
            // return false`. With the vanilla 2D map chosen (Plugin.Vanilla2DMap) these windows can
            // still be floated by the ordinary modal path while the room does not stand, and a cross
            // there would hide the merchant and leave the Merchant MODE current — the ModBuild 184
            // strand, with the party display's character slots dead for the rest of the session.
            // Outside the room the derived net keeps the cross off, exactly as it does today.
            if (MapRoom.MapRoomDriver.Active && MapRoom.GuildmasterDestinations.IsDestination(window))
                return MandatoryDecisionTerm.None;

            reason = "the GAME ITSELF refuses to close this window on ESC (escapeKeyAction None: "
                   + "UIWindow.Escape returns false without hiding, UIWindow.cs:713-716, and both "
                   + "HideOrShowWindows and ForceHideWindows skip it) — so the mod must not offer a "
                   + "cross that does what the ESC key will not. This is the derived net, not an "
                   + "identity: it says the game withheld the close, NOT what waits on this window";
            return MandatoryDecisionTerm.GameRefusesEscape;
        }

        return MandatoryDecisionTerm.None;
    }

    /// <summary>
    /// The window's live escape policy, for the audit line only. Printed so the next hardware log
    /// states, per converted window, WHICH term decided the X — including whether Term 2 would have
    /// caught the encounter window on its own, which no amount of reading the decompile can settle
    /// because the value is serialized in the prefab.
    /// </summary>
    internal static string EscapePolicyOf(UIWindow? window) =>
        window == null ? "no window" : window.escapeKeyAction.ToString();

    // ---- the escape chord's third action -------------------------------------------------

    /// <summary>
    /// The window a rescue screen was raised FOR, and only when THIS code raised it. Null means we
    /// hold no rescue. Deliberately not a bool: the release condition is a question about a SPECIFIC
    /// window ("is the flow this rescue was raised for over?"), and a bool could only ask the weaker
    /// "is any mandatory window open?".
    /// </summary>
    private static UIWindow? _rescueWindow;

    /// <summary>
    /// THE ESCAPE CHORD'S THIRD ACTION, AND WHY THE HOLE THIS FILE FIRST RECORDED IS NOW CLOSED.
    ///
    /// <para>The first version of this file recorded an ACCEPTED RESIDUAL: the X was withheld from a
    /// mandatory-decision window, but the long-hold A/X chord still routed to
    /// <c>CloseFloatedWindow</c> and could strand the very same waiter. That was framed as a choice
    /// between two bad options — let the chord strand the window, or block the chord and risk
    /// leaving the player in front of a float that never drew its own buttons (which the ModBuild
    /// 380 log shows this window doing, lines 7853-7856). Both horns assumed the chord's only
    /// available action is to HIDE the game's window. It is not.</para>
    ///
    /// <para><b>THE THIRD ACTION: release the float and raise the 2D composite, writing nothing.</b>
    /// <c>FlatScreen.RequestRescueScreen</c> sets the same latch the manual chord drives, so
    /// <c>FlatScreen.ManualScreenActive</c> goes true; <c>ConvertBaseActive</c>
    /// (ModalFallback.12.ScreenBind.cs:148) is ANDed with <c>!ManualScreenActive</c>, so
    /// <c>FloatWantedFor</c> returns false, so the release loop's <c>stillOpen</c> is false and the
    /// float is given up through the ORDINARY path — <c>CanvasConversion.Release</c> restoring the
    /// exact 2D home with <c>UserClosing</c> NOT set, which that loop's own comment states means
    /// "no Hide, no Escape and no CanvasGroup write" (ModalFallback.4.Tick.cs:2752-2757). The player
    /// then gets the game's own screen-space widgets on the quad, clickable with the laser, and
    /// presses a real <c>EventButton</c>. The event resolves through the game's own code:
    /// <c>ContinueRoadEvent</c> goes out on the wire, no remote client is left spinning, and the
    /// card comes off the deck. The player always has a way forward AND nothing is ever stranded.
    /// </para>
    ///
    /// <para><b>THE ONE THING THAT HAD TO BE CHECKED, AND IT PASSES.</b> The encounter window is a
    /// MAP-ROOM window (the 380 log seats it with MAP ROOM WINDOW SLOT / BAR HEIGHT, lines
    /// 7837-7841), and the flat screen is normally REFUSED outright while
    /// <c>MapRoom.MapRoomDriver.Active</c> — which is also why the manual A/X chord does not exist
    /// on the campaign map at all (<c>TickManualChord</c> clears <c>_manualShow</c> unless a
    /// scenario board exists). Had the rescue sat UNDER that gate, this whole option would have been
    /// inert in the exact room the report came from. It does not: <c>WantVisible</c> tests
    /// <c>if (_rescueShow) return true;</c> ABOVE the map-room gate (FlatScreen.4.Lifecycle.cs:116),
    /// and that comment says the placement IS the fix, for the neighbouring
    /// <c>UIDistributeRewardManager</c> deadlock. So the rescue reaches the map room — and on the
    /// map it is not a shortcut past the standing escape, it is the only escape there is.</para>
    ///
    /// <para><b>THE REQUESTER OWNS THE RELEASE, AND ON THE MAP NOBODY ELSE CAN.</b>
    /// <c>RequestRescueScreen</c> has deliberately no timeout, and on the campaign map the player
    /// cannot chord it away either. A one-shot press raising a latch nothing takes down would have
    /// traded a stranded window for a permanently flattened map room — a worse bug than the one
    /// being fixed. <see cref="TickMandatoryRescue"/> is therefore a real per-tick watchdog: it
    /// releases the moment the window it was raised for is closed or destroyed, which is exactly the
    /// "requester reports the flow unstuck" ending the latch documents.</para>
    ///
    /// <para><b>SCOPE.</b> A per-window branch inside <c>CloseTopModal</c>, not a redefinition of the
    /// chord. Every other window still gets <c>CloseFloatedWindow</c>, and the chord still consumes
    /// the press one-for-one.</para>
    /// </summary>
    internal static void RescueForMandatoryDecision(UIWindow window, string reason, float heldSeconds)
    {
        // ONLY OWN A RELEASE WE OWN. Another watchdog (FloatingDecisionSurfaces) may already hold
        // the screen up for its own stuck flow; the rescue is a single latch and not a refcount, so
        // releasing one we did not raise would take THEIRS down. Their per-tick watchdog re-raises
        // within a frame and the latch's own doc comment blesses exactly that, but not creating the
        // flicker is cheaper than relying on it healing.
        bool alreadyStanding = FlatScreen.RescueScreenActive;

        FlatScreen.RequestRescueScreen("ModalFallback.MandatoryDecision",
            $"the long-hold escape chord ({heldSeconds:F1}s) was used on '{window.name}' (ID "
            + $"{window.ID}), a window where a decision is MANDATORY — {reason}. The chord must NOT "
            + "hide this window, so it raises the 2D composite instead.");

        if (!alreadyStanding)
            _rescueWindow = window;

        // HW-VERIFY: the line that says the chord took its THIRD action instead of closing the
        // window. A deadlock report carrying this line means the player WAS given the 2D composite
        // and the game's own buttons — look at what he could not press on it. A report without it,
        // on a window this predicate names, means the branch did not run and CloseTopModal is the
        // next thing to read. Fires once per chord press, never per frame.
        VRLog.Note("WorldUI", $"MODAL ESCAPE CHORD REDIRECTED: '{window.name}' (ID {window.ID}) is a "
                              + "window where a decision is MANDATORY, so the chord did NOT close it "
                              + $"({reason}). Instead the mod raised the 2D composite through the same "
                              + "latch the manual A/X chord uses: this window's float is released by "
                              + "the ordinary path (CanvasConversion.Release restores its exact 2D "
                              + "home, UserClosing is NOT set, so no Hide, no Escape and no "
                              + "CanvasGroup write) and the game's own buttons are on the screen quad, "
                              + "clickable with the laser. NOTHING WAS WRITTEN TO THE GAME and no "
                              + "waiter was stranded — the player presses the game's own button and "
                              + "the flow completes normally, wire action included. THE SCREEN IS "
                              + "RELEASED AUTOMATICALLY when this window closes; a rescue that was "
                              + $"already standing for another requester (was standing: {alreadyStanding}) "
                              + "keeps its own owner and is not taken over.");
    }

    /// <summary>
    /// Per-tick watchdog for the rescue raised by <see cref="RescueForMandatoryDecision"/>. Releases
    /// on the falling edge — the window closed, or was destroyed — because on the campaign map
    /// nothing else can: there is no manual chord there and the latch has no timeout by design.
    /// Driven from the same tick as the escape chord.
    /// </summary>
    private static void TickMandatoryRescue()
    {
        UIWindow? w = _rescueWindow;
        // `is null` is a PURE REFERENCE test and bypasses Unity's == overload — the only thing it
        // means is "this code holds no rescue", which is the one case that must return silently.
        if (w is null)
            return;
        // Unity's overloaded == reports a DESTROYED window as null as well, and that case must
        // RELEASE rather than return: a destroyed window's flow can never end on its own, so an
        // early return there would leave the map room flattened for the rest of the session — the
        // exact failure this watchdog exists to prevent. Getting these two nulls the same way round
        // is the whole reason the local exists.
        // The `!` is load-bearing and NOT a shrug: `w is null` above already proved the REFERENCE is
        // non-null, but `w == null` is Unity's operator overload rather than a null test, so the
        // compiler's flow analysis stops tracking it here. Suppressing that is correct; removing the
        // `is null` guard above and relying on this would not be.
        bool gone = w == null;
        if (!gone && w!.IsOpen)
            return;

        string name = gone ? "<destroyed>" : w!.name;
        _rescueWindow = null;
        FlatScreen.ReleaseRescueScreen(
            $"the mandatory-decision window '{name}' the chord was used on has closed, so the flow "
            + "the player was stuck in front of is over and the 3D room can come back");
    }
}
