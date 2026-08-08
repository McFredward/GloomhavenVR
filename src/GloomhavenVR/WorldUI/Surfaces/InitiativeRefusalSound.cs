using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// The game's own REFUSAL sound on an initiative-track portrait this client cannot play.
///
/// USER REPORT (2026-08-09, verbatim): "Ich will das das ablehnen-Geräuch das das Spiel an
/// manchen Situation schon nutzt generell immer dann kommt, wenn man in der Initiativreihenfolge
/// ein Bild von einem nicht-spielbaren Character anklickt (Gegner oder Verbündete)." Clicking an
/// enemy or an allied/AI portrait in VR produces nothing audible, which reads as a broken click:
/// the player cannot tell "the mod ate my click" from "there is nothing to do here". This class
/// adds the missing ACKNOWLEDGEMENT and nothing else — what the click DOES is not touched
/// anywhere in this file.
///
/// ─────────────────────────────────────────────────────────── WHICH SOUND, AND WHERE IT COMES FROM ──
///
/// Not invented, not guessed, and not hardcoded if the game can be asked: the id is READ AT
/// RUNTIME from the game's own authored asset, in this order (<see cref="ResolveItem"/>):
/// <list type="number">
/// <item><b><c>UIInfoTools.Instance.generalAudioButtonProfile.nonInteractableMouseDownAudioItem</c></b>
///   — which the game exposes under the name that settles the question:
///   <c>public string InvalidOptionAudioItem =&gt; generalAudioButtonProfile.nonInteractableMouseDownAudioItem;</c>
///   (decompiled GH.Runtime/UIInfoTools.cs:467). This IS "das Ablehnen-Geräusch das das Spiel an
///   manchen Situationen schon nutzt": the game plays it for an unaffordable shop item
///   (UIShopItemSlot.cs:207, UITempleShopSlot.cs:226/232), a perk you cannot buy yet
///   (UIPerksInventory.cs:266/302, UIPerkInventorySlot.cs:179), an illegal map move
///   (AdventureMapUIManager.cs:252, Waypoint.cs:915), a card you may not pick
///   (FullAbilityCard.cs:625, UIPartyCharacterAbilityCardsDisplay.cs:191) and two refusals inside
///   Choreographer (:11361, :11401). Same sound, same meaning, one more situation.</item>
/// <item>the CLICKED portrait button's own <c>nonInteractableMouseDownAudioItem</c>, then its
///   <c>audioProfile.nonInteractableMouseDownAudioItem</c> — the per-widget override
///   <c>ExtendedButton.OnPointerDown</c> consults for exactly this purpose
///   (ExtendedButton.cs:215-232). Reached only when <c>UIInfoTools</c> is not alive or its profile
///   is unset, e.g. very early in a scene load.</item>
/// <item>last resort, the only literal in this file: <c>PlaySound_UIPingRewardNegative</c>, the
///   game's own NEGATIVE ping (UIRewardsManager.cs:57, UICampaignReward.cs:28 — played for
///   <c>reward.IsNegative()</c>). A genuine refusal-flavoured item, deliberately NOT
///   <c>PlaySound_UIButtonSelect</c>: a positive click confirmation is the opposite of the answer
///   the user asked for, and would make a rejected click sound accepted.</item>
/// </list>
/// Every candidate is validated with <c>AudioController.IsValidAudioID</c> before it is played and
/// the winner is named in the log (see <see cref="LogItem"/>), so the next hardware log PROVES
/// which sound was chosen instead of leaving it to be inferred.
///
/// ────────────────────────────────────────────────────────────────── HOW IT IS PLAYED (the VR trap) ──
///
/// Through <see cref="GameAudio.PlayListenerAnchored"/>, i.e. <c>AudioController.Play(item)</c>
/// with no transform. The mod never moves the <c>AudioListener</c> — it rides the game's 2D
/// camera — so the POSITIONAL overload plays a 3D item metres from the listener and attenuates it
/// to silence while still "succeeding". That cost the card fan a whole hardware round; the
/// listener-anchored call is also the exact shape every game refusal site above uses
/// (<c>AudioControllerUtils.PlaySound</c>). See the <see cref="GameAudio"/> class doc.
///
/// ───────────────────────────────────────────────────────────────────── WHEN IT FIRES, AND WHEN NOT ──
///
/// <see cref="NoticeClick"/> is called from <c>Hands.Interact.UguiPointer.Release</c>, the ONE
/// choke point both VR click paths deliver <c>pointerClick</c> through — the far laser
/// (<c>RayUguiDriver</c>) and the fingertip poke (<c>PokeInteractor</c>, uGUI path). The player
/// will try both and both must answer, so the hook sits where they meet rather than in either
/// driver. A fingertip touch that <c>PokeInteractor.PressAllowed</c> withholds (the grip chord,
/// ModBuild 92) never presses, therefore never clicks, therefore stays silent here — a withheld
/// touch is not a click.
///
/// It fires ONLY when all of:
/// <list type="bullet">
/// <item>the click handler is an <c>ExtendedButton</c> that is the row's own
///   <c>avatarButton</c> — the portrait itself. Any other button inside the entry (the persistent
///   active-bonus icons under the avatar) is somebody else's click and is left alone. Same
///   identification the focus bypass already uses,
///   <c>Board.Patches.InteractabilityManager_PortraitFocusBypass</c>.</item>
/// <item>the row's actor is NOT playable by this client — see <see cref="PlayableByMe"/>.</item>
/// <item>vanilla did not already answer this click audibly — see <see cref="VanillaAlreadySpoke"/>.
///   The requirement is to fill a SILENCE, not to talk over the game.</item>
/// <item>the debounce has elapsed (<see cref="DebounceSeconds"/>): holding the trigger or resting
///   a fingertip on a portrait must not machine-gun the sound. A held trigger produces one click
///   on release, but a jittering poke contact can re-arm, and two hands can click at once.</item>
/// </list>
///
/// MULTIPLAYER: nothing here is on the wire and nothing here can be, by construction. This is a
/// LOCAL acknowledgement of a LOCAL pointer click on a LOCAL widget — the peer never made that
/// click, so there is nothing for them to hear or see, exactly as a peer does not hear the
/// hover ticks, the button clicks or the fan sounds this client's own pointer produces. It reads
/// only presentation state (an <c>ExtendedButton</c>'s serialized audio fields, a
/// <c>ScriptableObject</c> id) and the replicated actor model, writes no rules state, sends no
/// packet, and suppresses no game-side callback — so the 1:1 requirement is satisfied by there
/// being no cross-client effect to sync at all. The one model field it reads,
/// <c>CActor.IsUnderMyControl</c>, is per-client BY DESIGN and is guarded exactly as the game
/// guards it (see <see cref="PlayableByMe"/>), which is what makes each client refuse for its own
/// characters rather than for the host's.
/// </summary>
internal static class InitiativeRefusalSound
{
    /// <summary>
    /// Minimum gap between two refusal sounds. One deliberate click per ~third of a second is
    /// well past any human click rate, and it covers the three ways this could repeat without a
    /// second intent: a poke contact that bounces on the plane, both hands clicking the same
    /// portrait, and a laser re-press during one trigger squeeze.
    /// </summary>
    private const float DebounceSeconds = 0.3f;

    /// <summary>The audio id the last emitted proof line named — the line repeats only when the
    /// resolved id or the played/valid outcome CHANGES, so a session prints it once.</summary>
    private static string? _loggedItem;
    private static bool _loggedPlayed;
    private static bool _loggedValid;

    /// <summary>One-shot: vanilla already answers this portrait audibly (see
    /// <see cref="VanillaAlreadySpoke"/>). Worth exactly one line — after that it is a steady
    /// state, not news.</summary>
    private static bool _loggedVanillaSpoke;

    /// <summary><see cref="Time.unscaledTime"/> before which no refusal sound plays.</summary>
    private static float _nextAllowedAt = float.NegativeInfinity;

    /// <summary>
    /// A synthesized uGUI click has just been delivered to <paramref name="clickHandler"/>.
    /// Plays the game's refusal item when — and only when — that click landed on an initiative
    /// portrait whose character this client cannot play. Every other click returns in two
    /// reference tests.
    ///
    /// <para>Called AFTER the click was dispatched, on purpose: the acknowledgement follows the
    /// click it acknowledges, and vanilla has already had its full turn at the event (including
    /// its own click sound, which <see cref="VanillaAlreadySpoke"/> then defers to). Nothing in
    /// here can influence what the click did.</para>
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
            if (PlayableByMe(actor))
                return; // one of MY characters — untouched, vanilla owns this click end to end

            if (VanillaAlreadySpoke(button, out string spoke))
            {
                if (!_loggedVanillaSpoke)
                {
                    _loggedVanillaSpoke = true;
                    VRLog.Info("WorldUI",
                        $"INITIATIVE REFUSAL SOUND: click on the non-playable portrait " +
                        $"'{Describe(actor)}' left SILENT — the game already answers this button " +
                        $"itself ({spoke}). The mod's refusal exists to fill a silence, not to " +
                        "play a second sound on top of vanilla's.");
                }
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextAllowedAt)
                return;
            _nextAllowedAt = now + DebounceSeconds;

            string preferred = ResolveItem(button, out string[] fallbacks, out string source);
            bool played = GameAudio.PlayListenerAnchored(preferred, fallbacks,
                out string item, out bool valid, out string note);
            LogItem(actor, item, valid, played, source, note);
        }
        catch (System.Exception ex)
        {
            // Never let an acknowledgement break the click it acknowledges.
            VRLog.Warn("WorldUI", $"INITIATIVE REFUSAL SOUND skipped — {ex.GetType().Name}: {ex.Message}.");
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
    /// "Ein spielbarer Character" from THIS client's point of view.
    ///
    /// <para>ENEMIES AND ALLIES ARE THE SAME TEST. The track holds exactly two kinds of row
    /// (<c>InitiativeTrack.NormalizeActorsPool</c>, InitiativeTrack.cs:544-560): actors whose
    /// class is a <c>CCharacterClass</c> (<c>IsPlayerByDefault</c> → an
    /// <c>InitiativeTrackPlayerBehaviour</c>) and actors whose class is a <c>CMonsterClass</c>
    /// (<c>IsEnemyByDefault</c> → an <c>InitiativeTrackEnemyBehaviour</c>). The game models an
    /// allied / neutral AI figure — the user's "Verbündete" — as a MONSTER-class actor with an
    /// ally type, so it is a <c>CMonsterClass</c> row too and this one predicate covers both
    /// halves of the report without a second rule.</para>
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
    /// the game's own — answer. Copying the idiom rather than reading the raw flag is what stops
    /// a solo session from refusing every one of its own heroes.</para>
    ///
    /// <para>DELIBERATELY NOT PART OF THIS TEST: whether the click would currently do anything.
    /// A phase gate (<c>InitiativeTrack.IsSelectable</c>), an isolation profile, an exhausted
    /// hero of my own — those are MY characters in a moment where the game says no, and the user
    /// asked about non-playable CHARACTERS ("Gegner oder Verbündete"), not about non-permitted
    /// moments. Widening this predicate would start refusing clicks on the player's own party,
    /// which is a different feature and was not asked for.</para>
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
    ///   was built with <c>enemiesSelectable: false</c>. Playing ours too would double it.</item>
    /// <item><b>interactable with a click item</b> — <c>OnPointerClick</c> plays
    ///   <c>mouseClickAudioItem</c> (or the profile's). The user's report is that the portrait
    ///   click is silent today, so on the shipped prefab this item is empty; the branch exists so
    ///   that if a game update ever authors one, the mod defers to it rather than stacking a
    ///   second sound on the same click.</item>
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
    /// The game's own negative ping — the ONLY audio id literal in this file, and only ever the
    /// last resort. See the class doc for why it is this one and not a button-select sound.
    /// </summary>
    private const string NegativePingFallback = "PlaySound_UIPingRewardNegative";

    /// <summary>Reused fallback buffer: the chain is at most three deep and this runs on a click.</summary>
    private static readonly string[] Fallbacks = new string[3];

    /// <summary>
    /// Build the ordered candidate chain documented on the class: the game's global
    /// "invalid option" item first, then the clicked button's own non-interactable item and its
    /// profile's, then the negative ping. <paramref name="source"/> names where the PREFERRED
    /// candidate came from, so the proof line can say whether the id is the game's or the mod's
    /// last resort.
    /// </summary>
    private static string ResolveItem(ExtendedButton button, out string[] fallbacks, out string source)
    {
        // UIInfoTools.InvalidOptionAudioItem, read field-by-field instead of through the property:
        // the property is `generalAudioButtonProfile.nonInteractableMouseDownAudioItem` with no
        // null check (UIInfoTools.cs:467) and would NRE before a scene has assigned the profile.
        UIInfoTools? tools = UIInfoTools.Instance;
        AudioButtonProfile? general = tools != null ? tools.generalAudioButtonProfile : null;
        string invalidOption = general != null ? general.nonInteractableMouseDownAudioItem : string.Empty;

        string own = button.nonInteractableMouseDownAudioItem;
        string profile = button.audioProfile != null
            ? button.audioProfile.nonInteractableMouseDownAudioItem
            : string.Empty;

        Fallbacks[0] = own ?? string.Empty;
        Fallbacks[1] = profile ?? string.Empty;
        Fallbacks[2] = NegativePingFallback;
        fallbacks = Fallbacks;

        source = string.IsNullOrEmpty(invalidOption)
            ? "UIInfoTools.InvalidOptionAudioItem unavailable (no UIInfoTools / no generalAudioButtonProfile)"
            : "UIInfoTools.InvalidOptionAudioItem (the game's own generalAudioButtonProfile.nonInteractableMouseDownAudioItem)";
        return invalidOption ?? string.Empty;
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
    /// THE line the next hardware log is read for: which audio id the refusal resolved to, where
    /// that id came from, and whether it actually played. Emitted on the FIRST refusal of a
    /// session and again only when the id or the outcome changes — a player clicking through a
    /// monster row must not turn the log into a drum solo, but a silent refusal (valid=False or
    /// played=False) must still be visible the moment it starts happening.
    /// </summary>
    private static void LogItem(CActor actor, string item, bool valid, bool played, string source, string note)
    {
        if (_loggedItem == item && _loggedValid == valid && _loggedPlayed == played)
            return;
        _loggedItem = item;
        _loggedValid = valid;
        _loggedPlayed = played;
        VRLog.Info("WorldUI",
            $"INITIATIVE REFUSAL SOUND: click on '{Describe(actor)}' — not one of this client's " +
            $"playable characters (enemy or allied/AI figure), so the game's refusal item " +
            $"'{item}' was played listener-anchored: valid={valid} played={played}{note}. " +
            $"Source: {source}. valid=IsValidAudioID at call time; played=AudioController.Play " +
            "returned an AudioObject (false ⇒ audio disabled or the item's own " +
            "MinTimeBetweenPlayCalls throttle). What the click DOES is unchanged — this is the " +
            "audible acknowledgement only (user report 2026-08-09).");
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
