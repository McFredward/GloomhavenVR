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
/// <para><b>WHAT THIS DELIBERATELY DOES NOT TOUCH: THE ESCAPE CHORD.</b> The long-hold
/// non-dominant A/X chord routes through the same <c>CloseFloatedWindow</c> and can therefore
/// strand this window in exactly the same way. It is left working anyway, following the precedent
/// every other no-X window already sets — the results panel, the story box and the loadout screen
/// are all chord-closable today with the same stated reasoning. Two things decide it. The X is a
/// discoverable one-click affordance the mod DRAWS on the window, pressed because it looks like the
/// way out; the chord is a deliberate half-second hold documented as the hard-lock escape hatch
/// (ModalFallback.7.Close.cs:15-27, "test #17 hard-lock guarantee"). And the ModBuild 380 log shows
/// this very window reaching its reveal deadline with nothing drawable under it (MODAL APPEAR HELD
/// / MODAL REVEAL FORCED after 600 ms, log lines 7853-7856) — if the event window itself fails to
/// draw its buttons, the chord is the only thing left. Removing it would convert a recoverable
/// failure into an unrecoverable one. This is a judgement, and it is the one place where the fix is
/// narrower than the defect; it is recorded here so the next reader can overrule it on purpose
/// rather than by accident.</para>
/// </summary>
internal static partial class ModalFallback
{
    /// <summary>
    /// True when this window demands a decision the player may not walk away from, so the mod must
    /// NOT draw a close X on it. <paramref name="reason"/> receives the term that decided, phrased
    /// as what was matched — never as a mechanism this predicate cannot observe.
    /// </summary>
    internal static bool IsMandatoryDecision(UIWindow? window, out string reason)
    {
        reason = string.Empty;
        if (window == null)
            return false;

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
            return true;
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
        if (window.GetComponent<UIRewardsManager>() != null)
        {
            // [RequireComponent(typeof(UIWindow))] :17, GetComponent in Awake :100 — one GameObject.
            // onProcessEnded :77 is invoked ONLY at :522, inside EndProcess :501. There is no
            // onHidden listener anywhere in the class, and EndProcess is what CALLS
            // myWindow.Hide(instant: true) (:511) rather than what a hide triggers — so an external
            // Hide is the effect arriving without its cause: processingRewards :79 stays true, the
            // `while (processingRewards)` coroutine at :188 keeps spinning, and the callback never
            // fires. This is the window the encounter flow hands off to for a non-campaign event
            // (UIEventPanel.cs:774), so an X here reproduces the reported deadlock one step later.
            reason = "reward showcase process (UIRewardsManager) — its onProcessEnded (:77) fires "
                   + "only from EndProcess (:501/:522), the class registers NO onHidden listener, "
                   + "and EndProcess is what calls myWindow.Hide rather than what a hide triggers; "
                   + "an outside Hide leaves processingRewards true and the `while (processingRewards)` "
                   + "coroutine at :188 spinning with the callback unfired";
            return true;
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
            return true;
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
            return true;
        }

        // TERM 2 — THE GAME'S OWN REFUSAL, as a net under every window nobody has classified yet.
        // UIWindow.Escape() returns false without hiding on None (UIWindow.cs:713-716), i.e. the
        // game declines to close this window on ESC; DialogPopup writes exactly this value for its
        // own allowHide=false dialogs (:235, :316). This is the term that makes the gate stop being
        // a bet on unknown windows: an unknown window is now only given an X if the game itself
        // would let ESC close it.
        if (window.escapeKeyAction == UIWindow.EscapeKeyAction.None)
        {
            // THE ONE EXEMPTION, and it is the mod's own pane rather than a game window. See the
            // class comment: VROptionsTab sets None to leave UIWindowManager's shared escapable
            // list, not to forbid closing, and its X is the compensation that method's own comment
            // promises. Keyed on the name MenuWindowFamily already uses as this pane's identity.
            if (window.name == MenuWindowFamily.VROptionsWindowName)
                return false;

            reason = "the GAME ITSELF refuses to close this window on ESC (escapeKeyAction None: "
                   + "UIWindow.Escape returns false without hiding, UIWindow.cs:713-716, and both "
                   + "HideOrShowWindows and ForceHideWindows skip it) — so the mod must not offer a "
                   + "cross that does what the ESC key will not. This is the derived net, not an "
                   + "identity: it says the game withheld the close, NOT what waits on this window";
            return true;
        }

        return false;
    }

    /// <summary>
    /// The window's live escape policy, for the audit line only. Printed so the next hardware log
    /// states, per converted window, WHICH term decided the X — including whether Term 2 would have
    /// caught the encounter window on its own, which no amount of reading the decompile can settle
    /// because the value is serialized in the prefab.
    /// </summary>
    internal static string EscapePolicyOf(UIWindow? window) =>
        window == null ? "no window" : window.escapeKeyAction.ToString();
}
