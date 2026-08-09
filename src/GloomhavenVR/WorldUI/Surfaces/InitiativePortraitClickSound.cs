using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// The audible ANSWER to an initiative-track portrait click — the game's own REFUSAL item when the
/// click did nothing, and the game's own CHARACTER-SWITCH item when it did something.
///
/// ─────────────────────────────────────────────────────────────────────────────── THE TWO REPORTS ──
///
/// <list type="number">
/// <item>USER REPORT 2026-08-09 (the reason this class exists): "Ich will das das ablehnen-Geräuch
///   das das Spiel an manchen Situation schon nutzt generell immer dann kommt, wenn man in der
///   Initiativreihenfolge ein Bild von einem nicht-spielbaren Character anklickt (Gegner oder
///   Verbündete)." Clicking an enemy or an allied/AI portrait in VR produced nothing audible, which
///   reads as a broken click.</item>
/// <item>USER REPORT (this build, verbatim): "Das 'Abgelehnt-Geräusch' kommt wenn ich auf ein
///   Character den ich selber nicht besitze — obwohl es ja gar nicht (mehr) abgelehnt wird, dort
///   sollte dieses Geräusch nicht kommen sondern das wenn man ganz normal einen character
///   ändert."</item>
/// </list>
///
/// The second report is the first one's gate having fallen out of step with the game. The sound was
/// keyed on OWNERSHIP — "is this one of MY characters" — which was a faithful reading of report #1
/// at the time, because a portrait you did not own did nothing when clicked. Then FREE CHARACTER
/// FOCUS landed (<see cref="Board.CharacterFocus"/>): clicking a character you do not control is now
/// a LEGITIMATE, SUCCESSFUL action — it opens a read-only view of them, which is the whole feature.
/// Ownership stopped predicting refusal, so the sound started calling a success a refusal.
///
/// <para><b>THE RULE, RE-KEYED ON THE OUTCOME</b>: refusal sound when the click was REFUSED
/// (nothing happened — no focus change, no selection change), character-change sound when it
/// SUCCEEDED IN ANY WAY, including "I now have a read-only view of a character I do not own".
/// Ownership is not consulted for that decision any more, and must not be re-introduced: it is
/// exactly the term that went stale. This is the DISPLAY-vs-ACTION split the codebase already
/// documents — <c>presented</c> (focus-resolved) drives what is SHOWN, <c>hand</c> (the game's)
/// drives anything reaching a real game seam — and a click that only changes what is presented is
/// still a success.</para>
///
/// ───────────────────────────────────────────────────────── HOW THE OUTCOME IS KNOWN, NOT GUESSED ──
///
/// Not re-derived here from game state after the fact (a post-hoc comparison would race the card
/// pipeline's deferred rebuild). The one place that KNOWS is the click seam itself:
/// <c>Board.Patches.InitiativeTrackPlayerAvatar_OnClick_Guard</c>, the prefix/postfix pair on
/// <c>InitiativeTrackPlayerAvatar.OnClick</c>. Every one of its branches reports what it did through
/// <see cref="NoticeOutcome"/>, and <see cref="NoticeClick"/> — which
/// <c>Hands.Interact.UguiPointer.Release</c> calls one line later, in the SAME FRAME — reads that
/// ledger back. The enumerated branches are:
/// <list type="bullet">
/// <item><b>SUCCEEDED</b> — <c>CharacterFocus.TryFocus</c> took the focus (CharacterFocus.cs:612,
///   including the idempotent "already focused" answer at :633). This is the branch the second
///   report is about.</item>
/// <item><b>SUCCEEDED</b> — vanilla ran and its select LANDED: the track's
///   <c>SelectedActor()</c> is now the clicked character (InitiativeTrack.cs:332/527). Reached in
///   the card-selection phase, where switching between your own characters is vanilla's own
///   mechanism.</item>
/// <item><b>REFUSED</b> — vanilla ran and its select did NOT land, i.e.
///   <c>InitiativeTrack.IsSelectable</c> was false for this phase (InitiativeTrack.cs:114/334) or
///   the avatar's <c>isSelectableByClick</c> was clear. Nothing changed.</item>
/// <item><b>ALREADY ANSWERED</b> — the mod's own guards refused the click AND already played the
///   game's invalid-click item themselves: <c>CardsGameApi.RejectForeignSelect</c>
///   (CardsGameApi.cs:3028, MP ownership) and <c>CardsGameApi.RejectActionPhaseSelect</c>
///   (CardsGameApi.cs:2970, docked-card deadlock guard). Silent here — the refusal WAS played, once,
///   by them. Before this build both sounded, back to back, on the same click (visible in the
///   hardware log: "Select REJECTED … played the game's invalid-click SFX" immediately followed by
///   "INITIATIVE REFUSAL SOUND … played=True").</item>
/// <item><b>no entry at all</b> — the click never reached that seam: an ENEMY / ALLIED-AI row (whose
///   avatar runs the un-overridden <c>InitiativeTrackActorAvatar.OnClick</c>, :154, and is not
///   patched), a gamepad-mode click, or an <c>ExtendedButton</c> the game itself refused to
///   dispatch. Then — and only then — the ORIGINAL report-#1 rule applies unchanged: not one of my
///   playable characters ⇒ refusal sound, one of mine ⇒ silence. That is deliberate: report #1 asked
///   for enemies and allies specifically, and this fallback keeps every case that predates free
///   character focus byte-for-byte as it was.</item>
/// </list>
///
/// ─────────────────────────────────────────────────────────── WHICH SOUNDS, AND WHERE THEY COME FROM ──
///
/// Neither is invented and neither is hardcoded while the game can be asked; both are resolved at
/// runtime from the game's own authored assets and every candidate is validated with
/// <c>AudioController.IsValidAudioID</c> before it is played.
///
/// <para><b>REFUSAL</b> (<see cref="ResolveRefusalItem"/>), in order:
/// <c>UIInfoTools.Instance.generalAudioButtonProfile.nonInteractableMouseDownAudioItem</c> — which
/// the game exposes under the name that settles the question,
/// <c>public string InvalidOptionAudioItem =&gt; generalAudioButtonProfile.nonInteractableMouseDownAudioItem;</c>
/// (UIInfoTools.cs:467) — then the clicked button's own <c>nonInteractableMouseDownAudioItem</c> and
/// its profile's (the per-widget override <c>ExtendedButton.OnPointerDown</c> consults for exactly
/// this purpose, ExtendedButton.cs:215-232), then the literal
/// <c>PlaySound_UIPingRewardNegative</c> — the game's own NEGATIVE ping (UIRewardsManager.cs:57),
/// deliberately not a positive click item. The game plays the first of those for an unaffordable
/// shop item (UIShopItemSlot.cs:207), an unbuyable perk (UIPerksInventory.cs:266), an illegal map
/// move (Waypoint.cs:915) and a card you may not pick (FullAbilityCard.cs:625). Hardware logs show
/// it resolving to <c>PlaySound_UINegativeSelect</c>.</para>
///
/// <para><b>CHARACTER CHANGE</b> (<see cref="ResolveChangeItem"/>), in order:
/// <c>UIInfoTools.Instance.tileClickAudioItem</c> FIRST, because that is literally the sound the
/// game itself plays when a character switch SUCCEEDS: <c>Choreographer.TileHandler</c>'s select
/// branch is <c>if (InitiativeTrack.Instance.Select(cPlayerActor)) { PlaySound(tileClickAudioItem);
/// CardsHandManager.Instance.SwitchHand(cPlayerActor); … }</c> (Choreographer.cs:1832-1835) — select
/// landed, confirm it, then switch the hand. That is "das Geräusch wenn man ganz normal einen
/// character ändert", read off the game's own character-switch seam rather than chosen by taste.
/// Then the clicked button's own <c>mouseClickAudioItem</c> and its profile's (what
/// <c>ExtendedButton.OnPointerClick</c> would play for a normal successful click on this very
/// button, ExtendedButton.cs:168-183), then the general profile's, then the literal
/// <c>PlaySound_ScenarioUI_TileConfirm</c> — which is not a taste choice either: it is the game's
/// own field INITIALIZER for <c>tileClickAudioItem</c> (UIInfoTools.cs:431), i.e. the same id, used
/// only when the singleton is not alive to be asked.</para>
///
/// <para>WHY THE MOD HAS TO SUPPLY THE CHANGE SOUND AT ALL, rather than leaving it to vanilla: on
/// this prefab the portrait button is silent on a successful click. The hardware logs prove it —
/// every refusal line for a PLAYER portrait ('Summoner', 'Elementalist', 'Brute') was emitted, and
/// that line is only reachable when <see cref="VanillaAlreadySpoke"/> answered false, i.e. the
/// button had no valid <c>mouseClickAudioItem</c>. (An ENEMY row does speak: the same logs carry
/// "click on the non-playable portrait 'City Guard' left SILENT — the game already answers this
/// button itself".) So the choice is between the wrong sound and no sound, and the user asked for
/// the right one.</para>
///
/// ────────────────────────────────────────────────────────────────── HOW IT IS PLAYED (the VR trap) ──
///
/// Through <see cref="GameAudio.PlayListenerAnchored"/>, i.e. <c>AudioController.Play(item)</c> with
/// no transform. The mod never moves the <c>AudioListener</c> — it rides the game's 2D camera — so
/// the POSITIONAL overload plays a 3D item metres from the listener and attenuates it to silence
/// while still "succeeding". That cost the card fan a whole hardware round; the listener-anchored
/// call is also the exact shape every game refusal site above uses
/// (<c>AudioControllerUtils.PlaySound</c>). See the <see cref="GameAudio"/> class doc.
///
/// ───────────────────────────────────────────────────────────────────── WHEN IT FIRES, AND WHEN NOT ──
///
/// <see cref="NoticeClick"/> is called from <c>Hands.Interact.UguiPointer.Release</c>, the ONE choke
/// point both VR click paths deliver <c>pointerClick</c> through — the far laser
/// (<c>RayUguiDriver</c>) and the fingertip poke (<c>PokeInteractor</c>, uGUI path). The player will
/// try both and both must answer, so the hook sits where they meet rather than in either driver. A
/// fingertip touch that <c>PokeInteractor.PressAllowed</c> withholds (the grip chord, ModBuild 92)
/// never presses, therefore never clicks, therefore stays silent here — a withheld touch is not a
/// click.
///
/// A sound is emitted ONLY when all of:
/// <list type="bullet">
/// <item>the click handler is an <c>ExtendedButton</c> that is the row's own <c>avatarButton</c> —
///   the portrait itself. Any other button inside the entry (the persistent active-bonus icons under
///   the avatar) is somebody else's click and is left alone. Same identification the focus bypass
///   already uses, <c>Board.Patches.InteractabilityManager_PortraitFocusBypass</c>.</item>
/// <item>the outcome (above) calls for one.</item>
/// <item>vanilla did not already answer this click audibly — see <see cref="VanillaAlreadySpoke"/>.
///   The requirement is to fill a SILENCE, not to talk over the game.</item>
/// <item>the debounce has elapsed (<see cref="DebounceSeconds"/>): holding the trigger or resting a
///   fingertip on a portrait must not machine-gun the sound. A held trigger produces one click on
///   release, but a jittering poke contact can re-arm, and two hands can click at once.</item>
/// </list>
///
/// MULTIPLAYER: nothing here is on the wire and nothing here can be, by construction. This is a
/// LOCAL acknowledgement of a LOCAL pointer click on a LOCAL widget — the peer never made that
/// click, so there is nothing for them to hear or see, exactly as a peer does not hear the hover
/// ticks, the button clicks or the fan sounds this client's own pointer produces. It reads only
/// presentation state (an <c>ExtendedButton</c>'s serialized audio fields, a <c>ScriptableObject</c>
/// id) plus a ledger the local click seam wrote, writes no rules state, sends no packet, and
/// suppresses no game-side callback — so the 1:1 requirement is satisfied by there being no
/// cross-client effect to sync at all. The one model field still read, <c>CActor.IsUnderMyControl</c>
/// (in the no-ledger fallback only), is per-client BY DESIGN and is guarded exactly as the game
/// guards it (see <see cref="PlayableByMe"/>).
/// </summary>
internal static class InitiativePortraitClickSound
{
    /// <summary>
    /// What a portrait click ACTUALLY DID, as reported by the seam that did it. The whole point of
    /// this type after the second user report: the sound is keyed on this and on nothing else.
    /// </summary>
    internal enum Outcome
    {
        /// <summary>Nothing happened — no focus change, no selection change. Refusal sound.</summary>
        Refused,

        /// <summary>Something changed: a focus was taken (a read-only view of a character this
        /// client does not control counts, and is the reported case) or the game's own select
        /// landed. Character-change sound.</summary>
        Succeeded,

        /// <summary>Refused, and the refusing guard ALREADY played the game's invalid-click item
        /// itself. Silent — the answer was given, once.</summary>
        AlreadyAnswered,
    }

    /// <summary>
    /// Minimum gap between two sounds. One deliberate click per ~third of a second is well past any
    /// human click rate, and it covers the three ways this could repeat without a second intent: a
    /// poke contact that bounces on the plane, both hands clicking the same portrait, and a laser
    /// re-press during one trigger squeeze.
    /// </summary>
    private const float DebounceSeconds = 0.3f;

    /// <summary>The audio id the last emitted proof line named — the line repeats only when the
    /// resolved id, the outcome or the played/valid result CHANGES, so a session prints it once per
    /// distinct answer.</summary>
    private static string? _loggedItem;
    private static bool _loggedPlayed;
    private static bool _loggedValid;
    private static Outcome _loggedOutcome = Outcome.Refused;

    /// <summary>One-shot: vanilla already answers this portrait audibly (see
    /// <see cref="VanillaAlreadySpoke"/>). Worth exactly one line — after that it is a steady state,
    /// not news.</summary>
    private static bool _loggedVanillaSpoke;

    /// <summary><see cref="Time.unscaledTime"/> before which no sound plays.</summary>
    private static float _nextAllowedAt = float.NegativeInfinity;

    // -------------------------------------------------------------------- the outcome ledger --

    /// <summary>The actor the last <see cref="NoticeOutcome"/> was about, the frame it was recorded
    /// on, and what it said. Read back by <see cref="NoticeClick"/> one line later in the same
    /// frame; a stale entry (different actor, or an older frame) is ignored and the pre-focus
    /// fallback applies instead.</summary>
    private static CActor? _outcomeActor;
    private static int _outcomeFrame = -1;
    private static Outcome _outcome;
    private static string _outcomeBranch = string.Empty;

    /// <summary>
    /// The click seam reports WHAT IT DID. Called from every branch of
    /// <c>Board.Patches.InitiativeTrackPlayerAvatar_OnClick_Guard</c> — during the click dispatch,
    /// i.e. strictly before the <see cref="NoticeClick"/> that follows it in
    /// <c>UguiPointer.Release</c>.
    ///
    /// <para><paramref name="branch"/> is a short human sentence naming the branch that fired; it
    /// goes straight into the proof line, so the next hardware log answers "which outcome did this
    /// click take" without anyone having to re-derive it.</para>
    ///
    /// <para>Pure bookkeeping: no allocation beyond the caller's own string, no game call, cannot
    /// throw, and cannot influence the click. A record that is never consumed (a flat-screen click,
    /// which does not run <see cref="NoticeClick"/>) is simply overwritten by the next one.</para>
    /// </summary>
    internal static void NoticeOutcome(CActor? actor, Outcome outcome, string branch)
    {
        _outcomeActor = actor;
        _outcomeFrame = Time.frameCount;
        _outcome = outcome;
        _outcomeBranch = branch ?? string.Empty;
    }

    /// <summary>The ledger entry for <paramref name="actor"/> if it belongs to THIS frame and THIS
    /// actor, consumed on read so it can never answer a second click.</summary>
    private static bool TakeOutcome(CActor actor, out Outcome outcome, out string branch)
    {
        outcome = Outcome.Refused;
        branch = string.Empty;
        if (_outcomeFrame != Time.frameCount || !ReferenceEquals(_outcomeActor, actor))
            return false;
        outcome = _outcome;
        branch = _outcomeBranch;
        _outcomeActor = null; // consumed — never let one record answer two clicks
        _outcomeFrame = -1;
        return true;
    }

    // ------------------------------------------------------------------------ the click itself --

    /// <summary>
    /// A synthesized uGUI click has just been delivered to <paramref name="clickHandler"/>. Plays
    /// the game's refusal item when the click was refused and the game's character-switch item when
    /// it succeeded — see the class doc for how the outcome is known. Every click that is not an
    /// initiative portrait returns in two reference tests.
    ///
    /// <para>Called AFTER the click was dispatched, on purpose: the acknowledgement follows the
    /// click it acknowledges, the outcome is only knowable once the handler has run, and vanilla has
    /// already had its full turn at the event (including its own click sound, which
    /// <see cref="VanillaAlreadySpoke"/> then defers to). Nothing in here can influence what the
    /// click did.</para>
    ///
    /// <para>Wrapped whole. This sits on the mod's central click path: a torn-down track, a
    /// half-initialised audio controller or a missing singleton must degrade to "no sound", never
    /// throw into the pointer and cost the player the click itself.</para>
    /// </summary>
    internal static void NoticeClick(GameObject? clickHandler)
    {
        if (clickHandler == null)
            return;
        try
        {
            // The portrait's clickable widget is an ExtendedButton (InitiativeTrackActorBehaviour
            // .avatarButton, decompiled InitiativeTrackActorBehaviour.cs:14). Anything else on the
            // whole converted UI leaves here immediately.
            var button = clickHandler.GetComponent<ExtendedButton>();
            if (button == null)
                return;
            var row = button.GetComponentInParent<InitiativeTrackActorBehaviour>();
            if (row == null)
                return;
            if (!IsPortraitButton(row, button))
                return;

            CActor actor = row.Actor;
            if (actor == null)
                return;

            if (!TakeOutcome(actor, out Outcome outcome, out string branch))
            {
                // No seam reported: an enemy / allied-AI row, or a click the game never dispatched.
                // The pre-focus rule, unchanged (class doc, "no entry at all").
                if (PlayableByMe(actor))
                    return; // one of MY characters — untouched, vanilla owns this click end to end
                outcome = Outcome.Refused;
                branch = "no click-seam report — an enemy / allied-AI portrait, or a click the game " +
                         "never dispatched: nothing ran, so nothing happened";
            }

            if (outcome == Outcome.AlreadyAnswered)
            {
                // The refusing guard already played the game's invalid-click item. Playing ours on
                // top is the double sound the hardware log shows. Logged by that guard, not here.
                return;
            }

            if (VanillaAlreadySpoke(button, out string spoke))
            {
                if (!_loggedVanillaSpoke)
                {
                    _loggedVanillaSpoke = true;
                    VRLog.Info("WorldUI",
                        $"INITIATIVE PORTRAIT SOUND: click on '{Describe(actor)}' left SILENT — the " +
                        $"game already answers this button itself ({spoke}). The mod's " +
                        "acknowledgement exists to fill a silence, not to play a second sound on " +
                        "top of vanilla's.");
                }
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextAllowedAt)
                return;
            _nextAllowedAt = now + DebounceSeconds;

            string preferred = outcome == Outcome.Succeeded
                ? ResolveChangeItem(button, out string[] fallbacks, out string source)
                : ResolveRefusalItem(button, out fallbacks, out source);
            bool played = GameAudio.PlayListenerAnchored(preferred, fallbacks,
                out string item, out bool valid, out string note);
            LogItem(actor, outcome, branch, item, valid, played, source, note);
        }
        catch (System.Exception ex)
        {
            // Never let an acknowledgement break the click it acknowledges.
            VRLog.Warn("WorldUI", $"INITIATIVE PORTRAIT SOUND skipped — {ex.GetType().Name}: {ex.Message}.");
        }
    }

    // ------------------------------------------------------------- is this click THE portrait? --

    /// <summary>
    /// True when <paramref name="button"/> is the row's PORTRAIT button rather than some other
    /// control inside the same entry.
    ///
    /// <para>WHY THIS IS NOT A PLAIN <c>ReferenceEquals(row.avatarButton, button)</c>. The two VR
    /// click paths hand this method DIFFERENT objects for the same portrait, and both are
    /// legitimate:
    /// <list type="bullet">
    /// <item>the LASER resolves the portrait geometrically and returns
    ///   <c>behaviour.avatarButton.gameObject</c> itself
    ///   (<c>TablePanelSurfaces.IDepthPortraitPicker.TryPickPortrait</c>), so the click handler IS
    ///   <c>avatarButton</c>;</item>
    /// <item>the POKE goes through the GraphicRaycaster and hits whichever Graphic is topmost
    ///   under the fingertip (the avatar's RawImage, a plate, a label). <c>UguiPointer</c> then
    ///   resolves the click handler with <c>ExecuteEvents.GetEventHandler</c>, i.e. the NEAREST
    ///   ANCESTOR that handles clicks — normally <c>avatarButton</c>, but an identity test would
    ///   silently drop the whole poke path if a prefab ever put the click handler one level
    ///   above it. "Both must produce the sound" is the requirement, so the test is written to
    ///   survive that.</item>
    /// </list></para>
    ///
    /// <para>What must be EXCLUDED is the opposite direction: the avatar's persistent
    /// active-bonus icons (<c>InitiativeTrackActorAvatar.persistentAbilities</c>) are their own
    /// Selectables STRICTLY INSIDE the portrait button's subtree. A click on one of those is that
    /// widget's click, not a portrait click, and must stay untouched. So: reject a strict
    /// descendant of <c>avatarButton</c>, accept <c>avatarButton</c> itself and anything above
    /// it inside the row. A row without an <c>avatarButton</c> at all has no such sub-widgets to
    /// confuse us with, and its only ExtendedButton is the portrait.</para>
    /// </summary>
    private static bool IsPortraitButton(InitiativeTrackActorBehaviour row, ExtendedButton button)
    {
        ExtendedButton avatar = row.avatarButton;
        if (avatar == null)
            return true;
        Transform node = button.transform;
        Transform portrait = avatar.transform;
        if (ReferenceEquals(node, portrait))
            return true;
        // Strict descendant of the portrait button ⇒ some inner widget (active-bonus icon).
        for (Transform? t = node.parent; t != null; t = t.parent)
        {
            if (ReferenceEquals(t, portrait))
                return false;
        }
        return true; // at or above the portrait button, still inside the row entry
    }

    // ------------------------------------------------------------------- is this one of mine? --

    /// <summary>
    /// "Ein spielbarer Character" from THIS client's point of view — the ORIGINAL (report #1) rule,
    /// now reached ONLY when no click seam reported an outcome (class doc, "no entry at all"): an
    /// enemy or allied-AI row, or a click the game never dispatched. It no longer decides anything
    /// about a character portrait whose click actually ran, and must not be widened back into that
    /// role: ownership is exactly the term the second user report showed had gone stale.
    ///
    /// <para>ENEMIES AND ALLIES ARE THE SAME TEST. The track holds exactly two kinds of row
    /// (<c>InitiativeTrack.NormalizeActorsPool</c>, InitiativeTrack.cs:544-560): actors whose
    /// class is a <c>CCharacterClass</c> (<c>IsPlayerByDefault</c> → an
    /// <c>InitiativeTrackPlayerBehaviour</c>) and actors whose class is a <c>CMonsterClass</c>
    /// (<c>IsEnemyByDefault</c> → an <c>InitiativeTrackEnemyBehaviour</c>). The game models an
    /// allied / neutral AI figure — the user's "Verbündete" — as a MONSTER-class actor with an
    /// ally type, so it is a <c>CMonsterClass</c> row too and this one predicate covers both
    /// halves of report #1 without a second rule.</para>
    ///
    /// <para>ONLINE OWNERSHIP, GUARDED THE WAY THE GAME GUARDS IT.
    /// <c>CActor.IsUnderMyControl</c> is a Bolt control-assignment latch
    /// (<c>CharacterManager.OnControlAssigned</c>, decompiled CharacterManager.cs:483) and is only
    /// meaningful while a session is online — which is why EVERY vanilla read of it is written
    /// <c>!FFSNetwork.IsOnline || actor.IsUnderMyControl</c> or
    /// <c>FFSNetwork.IsOnline &amp;&amp; actor.IsUnderMyControl</c>
    /// (<c>CPlayerActorExtensions.cs:9</c>, <c>InitiativeTrackActorAvatar.RefreshInitiative</c>,
    /// <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c>, <c>AbilityCardUI.cs:980</c> …).
    /// Offline, the whole party is the local player's, so the first disjunct is the correct — and
    /// the game's own — answer.</para>
    /// </summary>
    private static bool PlayableByMe(CActor actor)
        => actor.IsPlayerByDefault() && (!FFSNetwork.IsOnline || actor.IsUnderMyControl);

    // ------------------------------------------------------------ does vanilla already answer? --

    /// <summary>
    /// True when the game's own <c>ExtendedButton</c> audio already makes a noise for this exact
    /// click, so the mod must stay quiet. Both branches are read straight off
    /// <c>ExtendedButton</c> (decompiled ExtendedButton.cs:160-250):
    /// <list type="bullet">
    /// <item><b>NOT interactable</b> — <c>OnPointerDown</c>'s else-branch has ALREADY played
    ///   <c>nonInteractableMouseDownAudioItem</c> (falling back to
    ///   <c>audioProfile.nonInteractableMouseDownAudioItem</c>) at press time, i.e. vanilla's own
    ///   refusal for this very widget. An enemy row is exactly in this state whenever the track
    ///   was built with <c>enemiesSelectable: false</c>. Playing ours too would double it. Such a
    ///   button also never invokes its <c>onClick</c>, so this state and a SUCCEEDED outcome cannot
    ///   coexist.</item>
    /// <item><b>interactable with a click item</b> — <c>OnPointerClick</c> plays
    ///   <c>mouseClickAudioItem</c> (or the profile's) whenever the button is interactable
    ///   (ExtendedButton.cs:179-182), INDEPENDENTLY of whether the mod's prefix let the portrait's
    ///   <c>OnClick</c> run. On today's prefab it is empty for player portraits (proven by the
    ///   hardware logs — see the class doc), which is why the mod supplies the character-change
    ///   sound at all; the branch exists so that if a game update ever authors one, the mod defers
    ///   to it rather than stacking a second sound on the same click.</item>
    /// </list>
    /// Each branch reproduces vanilla's own preconditions — <c>useAudioController</c>, the
    /// non-empty item, and <c>AudioController.GetAudioItem(id) != null</c> (mirrored here as
    /// <c>IsValidAudioID</c>, which is the same table lookup) — so "vanilla spoke" is only
    /// claimed when vanilla really could have.
    /// </summary>
    private static bool VanillaAlreadySpoke(ExtendedButton button, out string what)
    {
        what = string.Empty;
        if (!button.useAudioController)
            return false; // the UIManager clip path; no audio item is consulted at all

        if (!button.interactable)
        {
            string id = Pick(button.nonInteractableMouseDownAudioItem,
                             button.audioProfile != null ? button.audioProfile.nonInteractableMouseDownAudioItem : null);
            if (!Valid(id))
                return false;
            what = $"ExtendedButton.OnPointerDown played its own nonInteractableMouseDownAudioItem '{id}' — the button is not interactable";
            return true;
        }

        string click = Pick(button.mouseClickAudioItem,
                            button.audioProfile != null ? button.audioProfile.mouseClickAudioItem : null);
        if (!Valid(click))
            return false;
        what = $"ExtendedButton.OnPointerClick played mouseClickAudioItem '{click}'";
        return true;
    }

    // ------------------------------------------------------------------------ id resolution --

    /// <summary>
    /// The game's own negative ping — one of the two audio id literals in this file, and only ever
    /// the last resort for a REFUSAL. See the class doc for why it is this one and not a
    /// button-select sound.
    /// </summary>
    private const string NegativePingFallback = "PlaySound_UIPingRewardNegative";

    /// <summary>
    /// The game's own tile-confirm id — the last resort for a CHARACTER CHANGE, and not a taste
    /// choice: it is the field initializer of <c>UIInfoTools.tileClickAudioItem</c>
    /// (UIInfoTools.cs:431), i.e. the very id the preferred candidate reads, used only when the
    /// singleton is not alive to be asked.
    /// </summary>
    private const string TileConfirmFallback = "PlaySound_ScenarioUI_TileConfirm";

    /// <summary>Reused fallback buffer: the longest chain is four deep and this runs on a click.</summary>
    private static readonly string[] Fallbacks = new string[4];

    /// <summary>
    /// Build the ordered REFUSAL candidate chain documented on the class: the game's global
    /// "invalid option" item first, then the clicked button's own non-interactable item and its
    /// profile's, then the negative ping. <paramref name="source"/> names where the PREFERRED
    /// candidate came from, so the proof line can say whether the id is the game's or the mod's
    /// last resort.
    /// </summary>
    private static string ResolveRefusalItem(ExtendedButton button, out string[] fallbacks, out string source)
    {
        // UIInfoTools.InvalidOptionAudioItem, read field-by-field instead of through the property:
        // the property is `generalAudioButtonProfile.nonInteractableMouseDownAudioItem` with no
        // null check (UIInfoTools.cs:467) and would NRE before a scene has assigned the profile.
        AudioButtonProfile? general = GeneralProfile();
        string invalidOption = general != null ? general.nonInteractableMouseDownAudioItem : string.Empty;

        Fallbacks[0] = button.nonInteractableMouseDownAudioItem ?? string.Empty;
        Fallbacks[1] = button.audioProfile != null
            ? button.audioProfile.nonInteractableMouseDownAudioItem ?? string.Empty
            : string.Empty;
        Fallbacks[2] = NegativePingFallback;
        Fallbacks[3] = string.Empty;
        fallbacks = Fallbacks;

        source = string.IsNullOrEmpty(invalidOption)
            ? "UIInfoTools.InvalidOptionAudioItem unavailable (no UIInfoTools / no generalAudioButtonProfile)"
            : "UIInfoTools.InvalidOptionAudioItem (the game's own generalAudioButtonProfile.nonInteractableMouseDownAudioItem)";
        return invalidOption ?? string.Empty;
    }

    /// <summary>
    /// Build the ordered CHARACTER-CHANGE candidate chain documented on the class: the game's own
    /// character-switch confirmation (<c>UIInfoTools.tileClickAudioItem</c>, the item
    /// <c>Choreographer.TileHandler</c> plays the moment a character select lands,
    /// Choreographer.cs:1832-1835), then the clicked button's normal click item and its profile's,
    /// then the general profile's, then the same id as a literal.
    /// </summary>
    private static string ResolveChangeItem(ExtendedButton button, out string[] fallbacks, out string source)
    {
        UIInfoTools? tools = UIInfoTools.Instance;
        string tileConfirm = tools != null ? tools.tileClickAudioItem : string.Empty;
        AudioButtonProfile? general = GeneralProfile();

        Fallbacks[0] = button.mouseClickAudioItem ?? string.Empty;
        Fallbacks[1] = button.audioProfile != null
            ? button.audioProfile.mouseClickAudioItem ?? string.Empty
            : string.Empty;
        Fallbacks[2] = general != null ? general.mouseClickAudioItem ?? string.Empty : string.Empty;
        Fallbacks[3] = TileConfirmFallback;
        fallbacks = Fallbacks;

        source = string.IsNullOrEmpty(tileConfirm)
            ? "UIInfoTools.tileClickAudioItem unavailable (no UIInfoTools)"
            : "UIInfoTools.tileClickAudioItem (the game's own character-switch confirmation, " +
              "Choreographer.TileHandler plays it when a select lands — Choreographer.cs:1832-1835)";
        return tileConfirm ?? string.Empty;
    }

    /// <summary>The game's shared button audio profile, or null before a scene has assigned it —
    /// read field-by-field so an unset profile answers null instead of throwing.</summary>
    private static AudioButtonProfile? GeneralProfile()
    {
        UIInfoTools? tools = UIInfoTools.Instance;
        return tools != null ? tools.generalAudioButtonProfile : null;
    }

    private static string Pick(string? first, string? second)
        => !string.IsNullOrEmpty(first) ? first! : (second ?? string.Empty);

    private static bool Valid(string id)
    {
        try
        {
            return !string.IsNullOrEmpty(id) && AudioController.IsValidAudioID(id);
        }
        catch
        {
            return false; // audio controller not alive yet — treat as "vanilla said nothing"
        }
    }

    // --------------------------------------------------------------------------------- proof --

    /// <summary>
    /// THE line the next hardware log is read for: WHICH OUTCOME the click took (and which branch
    /// said so), which audio id that outcome resolved to, where the id came from, and whether it
    /// actually played. Emitted on the first sound of a session and again whenever any of those
    /// change — a player clicking through a monster row must not turn the log into a drum solo, but
    /// the first click that answers "refused" where the user expects "changed" (or a silent
    /// valid=False / played=False) must be visible the moment it starts happening.
    /// </summary>
    private static void LogItem(CActor actor, Outcome outcome, string branch, string item,
                                bool valid, bool played, string source, string note)
    {
        if (_loggedItem == item && _loggedValid == valid && _loggedPlayed == played
            && _loggedOutcome == outcome)
            return;
        _loggedItem = item;
        _loggedValid = valid;
        _loggedPlayed = played;
        _loggedOutcome = outcome;
        string kind = outcome == Outcome.Succeeded
            ? "the game's CHARACTER-CHANGE item"
            : "the game's REFUSAL item";
        VRLog.Info("WorldUI",
            $"INITIATIVE PORTRAIT SOUND: click on '{Describe(actor)}' — outcome {outcome} " +
            $"({branch}), so {kind} '{item}' was played listener-anchored: valid={valid} " +
            $"played={played}{note}. Source: {source}. valid=IsValidAudioID at call time; " +
            "played=AudioController.Play returned an AudioObject (false ⇒ audio disabled or the " +
            "item's own MinTimeBetweenPlayCalls throttle). The sound is keyed on the OUTCOME, never " +
            "on ownership: a read-only view of a character this client does not control is a " +
            "SUCCESS and must sound like a normal character change (user report 2026-08-09).");
    }

    /// <summary>Best available name for the clicked actor — the same prefab name the game's own
    /// track logging uses (<c>InitiativeTrack.Select</c>), so the two lines can be correlated.</summary>
    private static string Describe(CActor actor)
    {
        try
        {
            string name = actor.GetPrefabName();
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch
        {
            return "?";
        }
    }
}
