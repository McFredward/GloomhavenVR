using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// THE MERCENARY LOG / TOWN RECORDS WINDOW MUST NOT TAKE THE CHARACTER UI DOWN WITH IT.
///
/// <para>THE USER REPORT, 2026-08-22, verbatim: <i>"Wenn das Fenster mit der Liste der
/// in-Ruhestand-Charaktere geöffnet wird, verschwindet das Fenster der Character-UI, das nicht
/// geschlossen werden darf. Wird das wieder geschlossen, taucht es wieder auf. Das darf nicht
/// sein!"</i> — opening the retired-characters list makes the character UI, a window the map room
/// declares permanent and gives no X, disappear for exactly as long as that list is open.</para>
///
/// <para>THE MECHANISM, READ OUT OF THE DECOMPILE. The retired-characters list is the guildmaster
/// bar's <b>Mercenary Log</b> destination — <c>MercenaryLogMode</c>, whose
/// <c>IsUnlocked</c> is literally <c>MapParty.RetiredCharacterRecords.Count &gt; 0</c>
/// (MercenaryLogMode.cs:11-18). It is NOT a <c>UIWindowID</c> and it does NOT go through
/// <c>UIWindowManager</c>: there is no window stack, no modal flag and no "hide the others" sweep
/// anywhere in <c>UIWindow</c>/<c>UIWindowManager</c>. The hide is a single explicit call, the
/// FIRST statement of the mode's Enter:</para>
/// <code>
/// public override void Enter()                                   // MercenaryLogMode.cs:31-33
/// {
///     NewPartyDisplayUI.PartyDisplay.Hide(this, instant: true);
///     ...
/// }
/// public override void Exit()                                    // MercenaryLogMode.cs:44-48
/// {
///     NewPartyDisplayUI.PartyDisplay.Show(this);
///     ...
/// }
/// </code>
/// <para><c>TownRecordsMode</c> — the OTHER destination that opens the very same
/// <c>UI Town Records Window</c>, on the achievements tab — is byte-for-byte the same pair
/// (TownRecordsMode.cs:52-58 and :60-66). Those two are the whole population: a sweep of every
/// <c>PartyDisplay.Hide(</c> call site in <c>decompiled/</c> (34 of them, across MapChoreographer,
/// UILoadoutManager, UIGuildmasterHUD, UIDistributeRewardManager, UICharacterCreatorWindow,
/// UIEventPanel, UIMapFTUEInitialStep, UIPersonalQuestResultManager) finds no other caller that
/// passes a <c>GuildmasterMode</c> as the request token. The trainer's hide at UIGuildmasterHUD.cs:181
/// sits inside a lambda and passes the <b>HUD</b>, not the mode, so it is not caught here — see
/// REJECTED ALTERNATIVES.</para>
///
/// <para><c>Hide</c> itself (NewPartyDisplayUI.cs:690-721) is refcounted by requester token:</para>
/// <code>
/// private HashSet&lt;object&gt; hideRequests = new HashSet&lt;object&gt;();          // :172
///
/// public void Hide(object request, bool instant, Action callback = null, bool deselectCurrentCharacter = true)
/// {
///     hideRequests.Add(request);
///     if (!window.IsOpen) { ...log...; return; }
///     if (deselectCurrentCharacter) UnselectCurrentCharacter();
///     CancelShowAnimations();
///     if (instant) { hidePanelAnimator.GoToFinishState(); window.Hide(); callback?.Invoke(); }
///     else         { hideCallback = callback; animLockInteraction.blocksRaycasts = false; hidePanelAnimator.Play(); }
/// }
///
/// public void Show(object request)                                              // :651-667
/// {
///     hideRequests.Remove(request);
///     if ((!window.IsOpen || hidePanelAnimator.IsPlaying) &amp;&amp; ...PartyUIUnlocked &amp;&amp; hideRequests.Count &lt;= 0)
///     { ... window.ShowOrUpdateStartingState(); showPanelAnimator.Play(); ... }
/// }
/// </code>
///
/// <para>THE LOG SAYS EXACTLY THIS, FOUR TIMES, IN THE ModBuild 225 HARDWARE LOG. The game's own
/// <c>[GUI]</c> lines bracket our <c>UIWindow</c> lines and name the requester:</para>
/// <code>
/// 67415  [GUI] Hide party panel immediatly requested by MercenaryLogMode (False) (True)
/// 67415  UIWindow hidden: 'Party Display UI ' (ID None, room=True, mode=TableIdle)
/// 67417  UIWindow SHOWN:  'UI Town Records Window' (ID None, room=True, mode=TableIdle)
/// ...
/// 67645  [GUI] Show party panel requested by MercenaryLogMode (current hidding request: ) isOpen False isHidding False
/// 67645  UIWindow SHOWN:  'Party Display UI ' (ID None, room=True, mode=TableIdle)
/// 67651  UIWindow hidden: 'UI Town Records Window' (ID None, room=True, mode=TableIdle)
/// </code>
/// <para>and the same quartet of lines again at 156685/159228, 163030/163133 and 163202/163277. The
/// hide always PRECEDES the town-records show and the re-show always PRECEDES the town-records hide,
/// which is the signature of an open/close routine doing it, not of a race. Note WHICH window goes:
/// the inner <c>'Party Display UI '</c> (the one <c>NewPartyDisplayUI.window</c> points at), not the
/// outer <c>'New Party display'</c> (ID PartyPanel) that the map room floats — the float stays up and
/// its CONTENT is emptied, which is what "das Fenster … verschwindet" looks like from inside the
/// headset. <c>'New Party display'</c> is hidden exactly once in the whole session, at line 189190,
/// on the scenario transition, and that has nothing to do with this report.</para>
///
/// <para>WHY THE FLAT GAME IS RIGHT AND WE ARE NOT. In the flat game the guildmaster bar shows ONE
/// destination at a time on one screen, so folding the party panel away to make room for the town
/// records is correct there. The map room's whole premise is the opposite (user ruling, ModBuild 180:
/// <i>"Anders als in Flat soll es hier möglich sein mehrere Fenster parallel offen zu haben"</i>),
/// and it goes further for this particular panel: <c>ModalFallback.IsMapRoomPermanent</c> makes the
/// character UI un-closable and gives it no X. A window the player is forbidden to close must not be
/// closed on his behalf by a sibling opening.</para>
///
/// <para>THE FIX: SKIP THAT ONE HIDE WHOLE, INCLUDING ITS BOOKKEEPING. The prefix returns false for a
/// <c>GuildmasterMode</c> requester while the VR map room stands, so <c>hideRequests.Add(request)</c>
/// does not run either. THAT IS THE POINT, not an oversight — see the refcount paragraph below.</para>
///
/// <para>REFCOUNT DISCIPLINE, WHICH THIS PROJECT HAS ALREADY PAID FOR ONCE (see
/// <see cref="Character3DDisplayRefcount"/>: the map room opens windows the flat game never has open
/// together, and a game-side refcount written for a single requester was driven into an inconsistent
/// state by it). Here the token set is a <c>HashSet&lt;object&gt;</c>, so Add and Remove are
/// idempotent, and suppressing the ADD alongside the hide is what keeps the pair balanced:</para>
/// <list type="bullet">
/// <item><b>Exit still runs, and is harmless.</b> <c>Show(this)</c> does
/// <c>hideRequests.Remove(request)</c> on a token that was never added — a HashSet Remove of an
/// absent key returns false and throws nothing. Its outer guard
/// <c>(!window.IsOpen || hidePanelAnimator.IsPlaying)</c> is FALSE, because we left the window open
/// and stopped no animator, so the whole re-show block is skipped. Net effect of Exit: nothing. There
/// is no double-show and no animator restart.</item>
/// <item><b>A reference is never left behind.</b> The failure mode of a half-suppressed hide would be
/// a token that stays in <c>hideRequests</c> forever, after which SOMEBODY ELSE'S <c>Show</c> finds
/// <c>Count &gt; 0</c> and refuses to bring the panel back — the panel would then be stuck hidden for
/// the rest of the session, a strictly worse bug than the one reported. Skipping the whole method is
/// the only variant that cannot do that.</item>
/// <item><b>The one measurable divergence, stated rather than discovered later.</b> If the panel is
/// ALREADY hidden by another requester (a MapChoreographer action progression, the character creator)
/// when a destination is entered, vanilla would stack a second token and keep it down until BOTH
/// released; with this patch only the other token is held, so the panel returns as soon as that one
/// releases — while the town records are still open. That is the state this report asks for, so the
/// divergence runs in the requested direction.</item>
/// <item><b>Deselection.</b> Vanilla's <c>deselectCurrentCharacter: true</c> path
/// (<c>UnselectCurrentCharacter()</c>) is skipped with the rest of the method, so the selected
/// character survives the town records being opened and the map room's card fan keeps showing that
/// character's hand. Deliberate: the panel stays up, so it must also stay usable.</item>
/// </list>
///
/// <para>REJECTED ALTERNATIVE (a): TEACH THE VR SIDE THAT THIS IS A "STACKING" HIDE AND KEEP THE
/// PANEL FLOATED ANYWAY. This is the one the brief offers as the other option, and it loses on the
/// project's own standing rule — [[dont-win-a-write-war]] — for three separate reasons. (1) The hide
/// is not a flag we could concede: <c>window.Hide()</c> drives a CanvasGroup alpha tween to 0 (and,
/// on a prefab with <c>m_DisableOnZeroAlpha</c>, a <c>SetActive(false)</c>), plus
/// <c>hidePanelAnimator.GoToFinishState()</c> parks a GUIAnimator at its finished pose; holding the
/// panel visible against all three means re-writing them every frame. (2) It would make
/// <c>UIWindow.IsOpen</c> LIE — the game would believe the party panel is closed while we render it,
/// and <c>ModalFallback</c>, the escape chord, the mip-bake watcher and the supersampler all key off
/// exactly that flag. (3) It leaves the token in <c>hideRequests</c>, i.e. the stuck-hidden failure
/// above. Suppressing the call at its single source is smaller, needs no per-frame work, and leaves
/// every observer of the game's state agreeing with the picture.</para>
///
/// <para>REJECTED ALTERNATIVE (b): a prefix on <c>MercenaryLogMode.Enter</c> / <c>TownRecordsMode.Enter</c>
/// setting a suppression flag that a <c>Hide</c> prefix then reads. It needs three patches instead of
/// one and a finalizer to guarantee the flag comes down if Enter throws, and it buys nothing: the
/// requester token the game already passes IS the identity we would be re-deriving.</para>
///
/// <para>REJECTED ALTERNATIVE (c): catching the trainer's hide as well (UIGuildmasterHUD.cs:181,
/// requester = the HUD). Not done. That destination also hides
/// <c>UIPartyCharacterEquipmentDisplay</c> and <c>AbilityCardsDisplay</c> in the same breath, so
/// leaving only the party panel up would show a half-torn-down screen; and its requester token is the
/// HUD, which is not distinguishable here from any other HUD-driven hide. The report names the
/// retired-characters list, and the fix is scoped to the bar destinations that hide the party panel
/// and nothing else.</para>
///
/// <para>MULTIPLAYER. Local presentation only. <c>Enter</c>/<c>Exit</c> put nothing on the wire; the
/// suppressed call is a CanvasGroup tween on this client's own UI. <c>TownRecordsMode.Enter</c>'s
/// <c>RequestPauseActionProgression</c> and everything else in both Enter bodies still runs — only
/// the party panel's own hide is skipped, and it is skipped identically whether or not a session is
/// online.</para>
///
/// <para>IT ALWAYS SAYS WHAT IT DID — [[gated-remedy-never-ran]]. <see cref="Prepare"/> prints one arm
/// line at registration whether or not the seam is ever reached; the FIRST <c>Hide</c> of the session
/// prints its verdict unconditionally, as does the first call from each distinct requester type; and
/// every guildmaster-destination hide prints either SUPPRESSED or PASSED with the term that declined.
/// So "the patch never fired" and "the patch fired and did not help" are different, decidable
/// readings of one hardware log rather than the same silence. Registered by <c>WorldUIModule</c>.</para>
/// </summary>
[HarmonyPatch]
internal static class PartyPanelStackingHide
{
    private const string Scope = "WorldUI";

    /// <summary>Guildmaster-destination hides swallowed this session.</summary>
    private static int _suppressed;

    /// <summary>Hides that ran vanilla, for any of the reasons in <see cref="Declined"/>.</summary>
    private static int _passed;

    /// <summary>One arm line per session, from Harmony's own hook — proof the class was reached.</summary>
    private static bool _armed;

    /// <summary>The long first-suppression explanation is printed once.</summary>
    private static bool _explained;

    /// <summary>Requester type names already reported once, so the log carries one line per distinct
    /// caller instead of one per call. Bounded by the ten hider types in the decompile.</summary>
    private static readonly HashSet<string> SeenRequesters = new();

    private static bool Prepare()
    {
        if (!_armed)
        {
            _armed = true;
            VRLog.Info(Scope,
                "PARTY PANEL STACKING HIDE: arming on NewPartyDisplayUI.Hide(object, bool, Action, "
                + "bool) (user report 2026-08-22 — opening the retired-characters list makes the "
                + "character UI, which the map room forbids closing, disappear until the list is "
                + "closed again). A hide requested by a GuildmasterMode (MercenaryLogMode = the "
                + "retired-characters list, TownRecordsMode = the same window's achievements tab) is "
                + "SWALLOWED while the VR map room stands; every other requester runs vanilla. From "
                + "here on the FIRST hide of the session, the first hide from each distinct requester "
                + "type, and EVERY guildmaster-destination hide print a line — so if none of them "
                + "ever appears, this prefix is not reaching the call at all.");
        }
        return true;
    }

    /// <summary>
    /// Swallow the party panel's hide when it is a guildmaster-bar destination folding a sibling
    /// away for screen real estate the VR room does not lack. Returns false to skip the original
    /// ENTIRELY — including <c>hideRequests.Add</c>; see the class doc's refcount paragraph for why
    /// that is the required variant and not a shortcut.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NewPartyDisplayUI), nameof(NewPartyDisplayUI.Hide),
        typeof(object), typeof(bool), typeof(Action), typeof(bool))]
    // ISOLATED (ModBuild 334). NewPartyDisplayUI.PartyDisplay receives 10 of the game's ~121
    // network actions, and this is a SKIP prefix: a throw would both surface as the game's
    // desynchronisation dialog and leave the panel in an undefined state. On a throw we return
    // TRUE — vanilla Hide runs, which is the behaviour this suppression exists to refine, not to
    // depend on. See docs/NET-ACTION-SURFACE.md.
    private static bool BeforeHide(NewPartyDisplayUI __instance, object request)
        => Net.Desync.DispatchGuard.Run("PartyPanelStackingHide", () => HideBody(__instance, request),
                                        onThrow: true, "WorldUI");

    private static bool HideBody(NewPartyDisplayUI __instance, object request)
    {
        string who = request != null ? request.GetType().Name : "<null requester>";
        bool firstOfKind = SeenRequesters.Add(who);
        bool destination = request is GuildmasterMode;
        string? declined = Declined(request);

        if (declined != null)
        {
            _passed++;
            // Loud for a destination (that is the reported case declining) and once per requester
            // type otherwise, so the very first Hide of a session always leaves a line behind.
            if (destination || firstOfKind)
            {
                VRLog.Info(Scope,
                    $"PARTY PANEL HIDE PASSED THROUGH (requester '{who}', panel open="
                    + $"{(__instance != null && __instance.IsOpen ? "yes" : "no")}): " + declined
                    + $" Vanilla NewPartyDisplayUI.Hide runs unchanged. ({_suppressed} suppressed / "
                    + $"{_passed} passed this session.)");
            }
            return true;
        }

        _suppressed++;
        if (!_explained)
        {
            _explained = true;
            VRLog.Info(Scope,
                $"PARTY PANEL STACKING HIDE SUPPRESSED, first time this session (requester '{who}', "
                + $"panel open={(__instance != null && __instance.IsOpen ? "yes" : "no")}). "
                + "MercenaryLogMode.Enter (MercenaryLogMode.cs:33) and TownRecordsMode.Enter "
                + "(TownRecordsMode.cs:56) both open with NewPartyDisplayUI.PartyDisplay.Hide(this, "
                + "instant: true) — that call, and NOTHING else in either Enter, is what took the "
                + "character UI down; the game's own '[GUI] Hide party panel immediatly requested by "
                + "MercenaryLogMode' line no longer follows this one. The whole method is skipped, so "
                + "the requester token is NOT added to hideRequests: the matching Show(this) on Exit "
                + "removes an absent key (a no-op) and its (!window.IsOpen || hidePanelAnimator."
                + "IsPlaying) guard is false, so closing the list re-shows nothing and disturbs no "
                + "animator. EXPECTED SIDE EFFECT: the selected character is no longer deselected "
                + "when the list opens (UnselectCurrentCharacter is part of the skipped method), so "
                + "the map room's card fan keeps showing that character's hand — deliberate, the "
                + "panel stays up so it stays usable. If the character UI STILL vanishes after this "
                + "line, the cause is not this call and the next thing to read is which UIWindow the "
                + "'UIWindow hidden' line names.");
        }
        else
        {
            VRLog.Info(Scope,
                $"PARTY PANEL STACKING HIDE SUPPRESSED (requester '{who}'): the character UI stays "
                + $"up while this guildmaster destination is open. ({_suppressed} suppressed / "
                + $"{_passed} passed this session.)");
        }
        return false;
    }

    /// <summary>
    /// The gate, as one sentence naming the term that declined — or null to suppress. A message
    /// rather than a bool so a hardware log never has to guess which conjunct was false.
    /// </summary>
    private static string? Declined(object? request)
    {
        if (request is not GuildmasterMode)
            return "the requester is not a guildmaster-bar destination (only MercenaryLogMode and "
                   + "TownRecordsMode hide the party panel with a GuildmasterMode token; the "
                   + "choreographer, the loadout, the character creator, the event panel and the "
                   + "FTUE all hide it as part of a SEQUENCE, which is a real close and not a "
                   + "stacking hide).";

        if (!WorldUIConfig.ConversionActive)
            return "VR canvas conversion is not active (mod off, or before conversion), so the party "
                   + "panel is an ordinary 2D window and the flat game's one-at-a-time stacking is "
                   + "correct.";

        if (!MapRoom.MapRoomDriver.Active)
            return "the VR map room is not standing, so the party panel is not a permanent floated "
                   + "window and there is nothing this suppression would protect.";

        if (MapFTUEManager.IsPlaying)
            return "the scripted map tutorial (MapFTUEManager) is playing and gets vanilla "
                   + "behaviour — its own steps drive which panels are up.";

        return null;
    }
}
