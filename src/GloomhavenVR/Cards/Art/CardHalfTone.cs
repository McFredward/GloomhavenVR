using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE TWO ACTION HALVES OF A CARD FACE, AND WHY A MOD-BUILT COPY OF ONE USED TO BE GREYER THAN
/// THE GAME'S OWN.
///
/// <para>USER REPORT (2026-08-22, translated): <i>"The hand cards in the fan have a GREYER colour in
/// the SELECTABLE AREAS. I've just noticed that in the card OVERLAYS this is not the case.
/// Independently of just the world map, check this generally and make sure the cards are really
/// displayed with all their colours the way they are in the game too. There should therefore be no
/// divergence between the card overlays and the hand cards in how they look."</i></para>
///
/// <para>THE "SELECTABLE AREAS" ARE A GAME COMPONENT WITH A GAME DIMMER ON IT. An ability face is
/// <c>FullAbilityCard</c> + two <c>FullAbilityCardAction</c>s (<c>topActionButton</c>,
/// <c>bottomActionButton</c>). Each of those owns a serialized <c>CanvasGroup</c> over its
/// <c>cardContentTransform</c>, and the game has exactly ONE writer for it:</para>
/// <code>
///   FullAbilityCardAction.SetInteractable(bool active, bool defaultAction)   // :322
///       if (canvasGroup != null &amp;&amp; !defaultAction)
///           canvasGroup.alpha = (active ? 1f : 0.5f);                        // :334
///       actionButton.interactable = active;  // + infusion / enhancement elements Enable/Disable
/// </code>
/// <para>So "greyer, and only in the selectable areas" is not a shader, not a tint, not the mod's
/// lighting and not a colour space: it is <b>alpha 0.5 on the half's own CanvasGroup</b> (the card
/// header, title and initiative disc sit OUTSIDE that group, which is exactly why they stayed
/// correct), plus the disabled ColorBlock on <c>actionButton</c> whenever that Selectable is
/// enabled. Note also that nothing ever puts the alpha back on its own:
/// <c>FullAbilityCardAction.ResetInteractable()</c> (:382, the call
/// <c>AbilityCardUI.OnReturnedToPool</c> makes) clears the BOOLEANS and leaves the CanvasGroup at
/// whatever the last user left. A pooled widget therefore carries its dimming forward.</para>
///
/// <para>WHAT GATES THE OVERLAY, AND WHAT THE FAN WAS MISSING. Both surfaces the user compared are
/// built out of the SAME pool of <c>AbilityCardUI</c> widgets — the difference is one call:</para>
/// <list type="bullet">
/// <item><description>OVERLAY (the hover preview over a converted window; the map room's ability
/// list is <c>UIPartyCharacterAbilityCardsDisplay</c>, which spawns its rows with
/// <c>AbilityCardUI.Init(card, fullCardHolder, characterID, …)</c> — UIPartyCharacterAbilityCardsDisplay.cs:280/412).
/// That <c>Init</c> runs <c>SetMode(CardHandMode.DeckSelection, {Any})</c>, and the
/// <c>DeckSelection</c> arm of <c>AbilityCardUI.SetMode</c> calls
/// <c>fullAbilityCard.SetInteractable(active: true)</c> (AbilityCardUI.cs:929) — <b>alpha 1 on both
/// halves, both action buttons interactable, infusion and enhancement elements enabled.</b></description></item>
/// <item><description>FAN (the map-room hand, and every other mod surface that prints a REAL card
/// front without owning the game's live widget). Its face is a throwaway <c>Object.Instantiate</c>
/// of a widget BORROWED from the game's pool — <c>Net.RemoteAbilityCardSource.TryPooledClone</c> →
/// <c>AbilityCardUI.Init(card, disableEventDetection: true)</c> → <c>SetMode(CardHandMode.Preview,
/// null)</c>, and the <c>Preview</c> arm does <c>ToggleFullCard(active: true)</c> and <b>nothing
/// else</b> (AbilityCardUI.cs:951-956). <c>SetInteractable</c> is never called on that path, so the
/// clone inherits whatever dimming the pooled template happened to carry. The ModBuild-195 hardware
/// log confirms the path in one line: <c>[Net] Remote card FACE path = PooledBorrow</c> immediately
/// followed by <c>[MapRoom] MAP-ROOM HAND FACES: 10 of 10 …(pooled borrow)</c>.</description></item>
/// </list>
///
/// <para>THE FIX IS THE GATE, NOT THE LOOK. This class does not tune a colour until it matches: it
/// runs <b>the game's own <c>FullAbilityCard.SetInteractable(true)</c></b> on the mod's clone — the
/// identical call <c>SetMode(DeckSelection)</c> makes for the overlay — so the fan takes the same
/// route rather than an imitation of its result. Everything that call touches (both half
/// CanvasGroups, both action Buttons, the default-action Buttons, <c>InfuseElement</c>s and the
/// enhancement infusions) lands the same way on both surfaces, including anything a future game
/// patch adds to it.</para>
///
/// <para>IT IS ONLY EVER APPLIED TO AN OBJECT THE MOD OWNS OUTRIGHT, and that is checked twice: the
/// face must not be registered as an ADOPTED live widget (<see cref="CardArtGuard.IsAdopted"/>) and
/// it must have no <c>AbilityCardUI</c> anywhere in its parent chain — the parent
/// <c>ObjectPool.RecycleCard</c> re-attaches every game-owned face to (ObjectPool.cs:545-547). A
/// clone satisfies both; a live widget can satisfy neither. Nothing is written to a face the game
/// is holding, so no restore contract is created and there is no write war to lose.</para>
///
/// <para>WHY THE ADOPTED (SCENARIO) FAN IS DELIBERATELY NOT WRITTEN TO. A card in the scenario hand
/// fan is the game's REAL <c>FullAbilityCard</c>, re-parented by <see cref="CardFace.Adopt"/> and
/// left in the game's own state. Whatever dimming it shows is the dimming the flat game shows for
/// that same widget in that same moment — the halves of a played card on the control board go to
/// alpha 0.5 through <c>CardsActionControlller.Finish()</c> (CardsActionControlller.cs:422-423) and
/// that is real information about what you have already played. There is nothing to correct there
/// and correcting it would DELETE a game cue; the divergence only exists where the mod manufactures
/// a face of its own. The census below measures that population anyway and prints its numbers, so
/// the claim is a reading and not an assumption.</para>
///
/// <para>THE CENSUS. <see cref="RunCensus"/> samples every <c>FullAbilityCard</c> in the scene —
/// inactive ones included, because a window row's preview is only active while hovered — splits
/// them into GAME-LIVE (the overlay population: a face parented under an <c>AbilityCardUI</c> that
/// is in a UI, i.e. one an <c>Init</c>/<c>SetMode</c> has run on), GAME-POOL (parked under
/// <c>ObjectPool</c>, which is precisely the object a borrowed clone is copied FROM and must not be
/// blurred into the reference), ADOPTED (the scenario fan/tray) and CLONE (every mod-built copy),
/// and prints the measured half state of each population in ONE line: the CanvasGroup alpha RANGE
/// (min…max, never a single sample — a mode is not a distribution), how many halves report
/// <c>interactable</c>, the actual rendered tint the Selectable left on the action image
/// (<c>CanvasRenderer.GetColor()</c>, i.e. the number the eye sees) and the shader the half's image
/// draws with. It always prints the number of faces compared, so "the populations agree" and "the
/// census never ran" can never look alike.</para>
///
/// <para>═══ ModBuild 197: THE ALPHA WAS NEVER THE DEFECT, AND THE 196 LOG SAID SO ═══</para>
///
/// <para>The 196 hardware log ran the census 55 times and NEVER printed the "first mod-built card
/// front normalised" line: <c>corrections written 0</c>, and every CLONE half already read
/// <c>halfCanvasGroup.alpha 1.00..1.00, interactable 20/20, renderedTint 1.00..1.00</c>, on the same
/// <c>'GUI/AbilityCard_Shd'</c> as the overlay. The gate above never even closed, because it was
/// never open. <b>The dimming 196 describes is real in the flat game and was simply not what the
/// user was photographing</b> — that correction is kept (it costs one change-gated test and it is
/// the right state for a mod-owned copy), but it is not this bug.</para>
///
/// <para>WHAT THE PHOTOGRAPH ACTUALLY MEASURES (Farbunterschied.jpg, 3840x2160, sampled across the
/// SAME feature on the overlay card and on a fan card of the same deck):</para>
/// <list type="bullet">
/// <item><description>the action plate's ORNAMENT stroke — overlay rgb(198,150,146) vs fan
/// rgb(191,162,158): the same stroke, within 4 %;</description></item>
/// <item><description>the card frame rail just OUTSIDE that plate — overlay rgb(55,6,2) vs fan
/// rgb(50,3,0): the same rail;</description></item>
/// <item><description>the plate's FILL, ten pixels further in — overlay rgb(87,0,8), a fully
/// saturated red, vs fan rgb(43,28,31), a NEUTRAL grey;</description></item>
/// <item><description>the TITLE BAR, i.e. the header sprite's own red band, over a 140x18 px box
/// on each card — overlay mean rgb(184.5,63.9,58.3) / reddest rgb(179,0,9), fan mean
/// rgb(179.9,65.7,61.9) / reddest rgb(186,1,9). <b>The header is not affected at all.</b> Both cards
/// are also the same size on screen (~320 px wide), so no sampling or mip difference is in play
/// either — the plate's 2 px ornament stroke resolves identically on both.</description></item>
/// </list>
/// <para>So the whole card is NOT darker, and the halves are not DIMMED. Over a 50x40 px box on the
/// plate fill the fan reads mean rgb(46.7,31.5,33.2) against the overlay's rgb(105.9,16.8,27.9),
/// and that pair fits <c>lerp(overlay, Rec.709 luminance(overlay) = 36.5, 0.85)</c> to within a
/// count or two on every channel — a <b>desaturation</b> of ≈0.85, confined to the two action
/// plates. No tint, alpha or CanvasGroup can produce it: a multiply cannot raise a green channel
/// from 17 to 32. Whatever does this is INSIDE the shader, and it acts on the plates' material and
/// not on the header's.</para>
///
/// <para>IT IS, AND THE GAME NAMES IT: <c>CardEffects</c> owns
/// <c>Shader.PropertyToID("_GreyOut")</c> (CardEffects.cs:18) on the card's own
/// <c>GUI/AbilityCard_Shd</c> materials — the "this card is spent" wash the flat game plays over a
/// discarded or lost card (<c>SetFloat(_greyOut, 1f)</c>, CardEffects.cs:600). Its rest value is
/// written in exactly one place: <c>CardEffects.RestoreCard()</c> (CardEffects.cs:466-486), which
/// sets <c>_GreyOut/_Flow/_Dissolve/_Burn</c> to 0 — and <c>RestoreCard</c> is called from
/// <c>Initialize()</c>, which is called from <c>Awake()</c>.</para>
///
/// <para>WHICH IS PRECISELY THE CALL A MOD-BUILT FRONT NEVER MAKES, for two independent reasons that
/// both hold on the same object:</para>
/// <list type="number">
/// <item><description>the borrowed SOURCE is spawned with <c>activate: false</c> under an INACTIVE
/// holder (<c>Net.RemoteAbilityCardSource.TryPooledClone</c>) — Unity does not run <c>Awake</c> on
/// an object that has never been active, so that widget's Images still point at the SHARED
/// <c>GUI_CardEffect_Mat</c> asset, at whatever value the asset carries;</description></item>
/// <item><description><c>Net.RemoteCardArt.StripFragileEffects</c> then <c>DestroyImmediate</c>s
/// <c>CardEffects</c> off the clone before it activates — deliberately, because that component's
/// <c>Initialize</c> also writes a <c>_PosAndBounds</c> that is only valid at the card's ORIGINAL
/// hand-canvas position and renders a detached world-space clone DEEP BLACK. Correct call, but it
/// takes <c>RestoreCard()</c> with it, so nobody ever writes the rest state.</description></item>
/// </list>
/// <para>The OVERLAY is the game's own live widget: it activates, <c>Awake</c> runs,
/// <c>Initialize</c> mints a per-image material and calls <c>RestoreCard()</c>. That one call is the
/// entire difference between the two cards in the photograph.</para>
///
/// <para>AND IT PREDICTS THE HEADER MEASUREMENT, which is the part that makes this more than a
/// plausible story: a never-initialised widget keeps every Image's PREFAB-serialized material, and
/// only the plates ship with the card-FX material on them — the header's effects are something
/// <c>Initialize</c> ADDS. So the divergence must be confined to the plates, which is exactly what
/// the title-bar box measured. It also matches the user's own first wording, "greyer in the
/// SELECTABLE areas", which was literally correct all along.</para>
///
/// <para>WHICH SURFACES ARE AFFECTED, AND WHICH ARE NOT. Every mod surface that MANUFACTURES a
/// front is: the map-room hand (<c>MapRoomHand</c> → <c>Net.RemoteCardArt</c>, the photograph), a
/// peer's cards, and every other pooled-borrow clone. The SCENARIO hand fan is not: its face is the
/// game's real widget ADOPTED into the card's own canvas (<c>VRCard.AttachGameCard</c> →
/// <see cref="CardFace.Adopt"/>), so <c>Awake</c>/<c>Initialize</c>/<c>RestoreCard</c> all ran on
/// it — and the 2026-08-15 scenario photograph confirms it, its plates measuring rgb(83,2,15)
/// against the overlay's rgb(92,10,15). "General, not only the world map" is therefore a statement
/// about the clone surfaces, and the census's ADOPTED column is what settles it in a scenario log.</para>
///
/// <para>THE FIX IS THE GATE AGAIN, NOT A COLOUR. <see cref="NormalizeCardFx"/> writes the four
/// numbers <c>RestoreCard()</c> writes — <c>_GreyOut = _Flow = _Dissolve = _Burn = 0</c> — and
/// nothing else. It does NOT touch <c>_PosAndBounds</c>: at rest those four are 0, so the
/// screen-space term they drive is inert, and writing a reconstructed one is exactly the guess that
/// <c>StripFragileEffects</c> exists to avoid. The write lands on a PRIVATE copy of the material,
/// never on the shared asset: that asset is the material every ability card in the flat game draws
/// through, and a mod that writes 0 into it would be mutating game state with no restore contract.
/// One rest copy is minted per distinct source material and shared by every clone, so a fan that
/// rebuilds ten fronts a second cannot leak ten materials a second.</para>
///
/// <para>NOT THROUGH A PROPERTY BLOCK, deliberately. This project has already paid for the lesson
/// that writes to an Amplify <c>[Toggle]</c> are INERT through a <c>MaterialPropertyBlock</c> and
/// that "the block is still attached" proves nothing was read. The write here is
/// <c>Material.SetFloat</c> on a real material — the identical call <c>CardEffects.RestoreCard()</c>
/// makes on the identical property id — so if the game's own reset is consumed, so is this one, and
/// a copy made with <c>new Material(source)</c> carries the source's keywords with it. The census
/// then reports the value it reads back, so "written" and "consumed" do not have to be assumed.</para>
///
/// <para>THE REAL SIGNAL SURVIVES. The grey wash IS information — it is how the flat game says
/// "discarded" / "lost" — and it keeps working everywhere the game owns the widget, because
/// <see cref="IsModOwnedCopy"/> refuses every adopted and every pooled face. On a mod-built CLONE
/// there is nothing to lose: <c>CardEffects</c> has been destroyed on that object, so no mod surface
/// can play the wash at all and a grey clone can only ever be a stale inherited value.</para>
///
/// <para>AND IT IS MEASURED EITHER WAY. The census now reads <c>_GreyOut/_Flow/_Dissolve/_Burn</c>
/// off the SAME action-plate image it already samples, prints the range per population and the
/// largest CLONE-vs-overlay deviation, and counts the graphics it read. If the next log shows
/// CLONE <c>_GreyOut</c> at 0 with the fan still grey, this diagnosis is falsified in one line and
/// the next round starts from a measurement rather than from this paragraph.</para>
///
/// <para>═══ ModBuild 198: THAT IS EXACTLY WHAT THE 197 LOG SHOWED — IT IS A SPRITE, NOT A COLOUR ═══</para>
///
/// <para>The paragraph above asked for its own falsification and got it, in the line it asked for.
/// ModBuild 197's census ran 34 times and printed, every time:</para>
/// <code>
///   GAME-POOL  143 faces/286 halves: alpha 1.00..1.00, interactable 286/286, renderedTint 1.00..1.00,
///              shader 'GUI/AbilityCard_Shd', _GreyOut 0.00..0.00 over 286 plate image(s)
///   CLONE       10 faces/ 20 halves: alpha 1.00..1.00, interactable  20/20,  renderedTint 1.00..1.00,
///              shader 'GUI/AbilityCard_Shd', _GreyOut 0.00..0.00 over  20 plate image(s)
/// </code>
/// <para>GAME-POOL is the population a clone is COPIED FROM and is never written to by this class, so
/// its 0.00 over 286 images is the unbiased reading: <b><c>_GreyOut</c> was never set on an action
/// plate on any surface.</b> The <c>CARD FX REST</c> writes the same log reports (10 images, worst
/// term 1.000) landed on ONE non-plate image per face — the sweep walks every <c>Graphic</c> — which
/// is why they were real writes that moved no pixel. Diagnosis (c): the fix ran, the write was
/// consumed, and <c>_GreyOut</c> is simply not the term.</para>
///
/// <para>THE TERM IS <c>Image.overrideSprite</c>. The action plate is a <c>Selectable</c> whose
/// transition is SPRITE SWAP, and <c>AbilityCardUISkin</c> ships a separate DISABLED plate artwork
/// per half (<c>TopActionDisabledSprite</c>/<c>BottomActionDisabledSprite</c>,
/// AbilityCardUISkin.cs:22/30, packed by <c>GetActionSpriteState</c> and streamed onto
/// <c>button.spriteState</c> by <c>ButtonLoadingContext</c>). Unity picks between them from
/// <c>Selectable.IsInteractable()</c> — which is <c>m_GroupsAllowInteraction &amp;&amp;
/// m_Interactable</c>, a CONJUNCTION — and <c>RemoteCardArt.Neutralize</c> writes
/// <c>CanvasGroup.interactable = false</c> on every mod-built clone (anti-cheat), which falsifies the
/// FIRST term. Every census so far read only the second one and printed <c>interactable 20/20</c>.
/// A sprite swap changes PIXELS without touching the CanvasGroup alpha, the CanvasRenderer tint or
/// any material float — which is precisely why three rounds of correct measurements were all
/// useless, and why "a multiply cannot raise green from 17 to 32" was true and pointed nowhere: no
/// multiply is involved, the two cards are drawing DIFFERENT ARTWORK.</para>
///
/// <para>The remedy is in <see cref="Net.RemoteAbilityCardSource.NeutralizePlateLook"/> (the
/// <c>CanvasGroup</c> stays; the TRANSITION goes, through the game's own
/// <c>FullAbilityCardAction.DisableHoverHighlight</c> route). What changed HERE is the measurement:
/// the census now prints, per population, the EFFECTIVE <c>IsInteractable()</c>, how many halves
/// carry a live <c>overrideSprite</c>, the <c>Graphic.color</c> vertex tint (never read before) and
/// above all the NAME OF THE SPRITE THE PLATE ACTUALLY DRAWS. Two populations printing the same
/// plate sprite name is an OUTCOME; a "corrections written" counter never was one. And
/// <see cref="MaybeDumpPlateDiff"/> adds the brute-force backstop this file should have had two
/// rounds ago: every property of both plates, enumerated off the shader's own property table,
/// printed where they differ and counted where they agree.</para>
/// </summary>
internal static class CardHalfTone
{
    private const string Scope = "Cards";

    /// <summary>Seconds between two censuses. The sweep is a whole-scene component scan, so it is
    /// cadenced hard; it only runs at all while the mod is pumping card faces (it is driven from
    /// <see cref="CardFace.Offer"/>), which is exactly when the comparison means anything.</summary>
    private const float CensusIntervalSeconds = 10f;

    /// <summary>Unscaled time of the next census.</summary>
    private static float s_nextCensus;

    /// <summary>Last verdict string printed — the line repeats only when the numbers change.</summary>
    private static string? s_lastVerdict;

    /// <summary>Censuses run so far, so a repeat-suppressed line can still be dated.</summary>
    private static int s_censuses;

    /// <summary>Clones normalised this session, for the verdict line.</summary>
    private static int s_normalizedCount;

    /// <summary>True once the "what this fixed" line has been printed.</summary>
    private static bool s_fixLogged;

    /// <summary>True once a refused <c>SetInteractable</c> has been reported.</summary>
    private static bool s_refusalLogged;

    private static bool s_errorLogged;

    /// <summary>
    /// Offer one ability face. Called from <see cref="CardFace.Offer"/> — the single per-face pump
    /// BOTH card paths already run (the adopted local face through
    /// <c>CardFace.Adopt</c>/<c>MaintainArtArrival</c>, a mod clone through
    /// <c>Net.RemoteCardArt</c>'s build-time and cadenced rescans) — so no new update loop exists to
    /// fall out of step with that one.
    /// </summary>
    internal static void Observe(FullAbilityCard? face)
    {
        if (face == null)
            return;
        try
        {
            if (IsModOwnedCopy(face))
            {
                // ITEM 8 (2026-09-05): a clone the PEER MIRROR is deliberately dimming is not a
                // clone that needs correcting. See HoldMirroredDim for why this is a hold and not
                // a race: the two are the same write to the same CanvasGroup, and exactly one of
                // them may own its final value.
                if (!HasMirroredDim(face) && NeedsCorrection(face))
                    Normalize(face);
                // The card-FX rest state — the ACTUAL subject of the report, see the class doc. Its
                // own change gate is the measured deviation from 0, so a face that is already at
                // rest costs one property read per card image and writes nothing.
                //
                // ITEM 4 (2026-09-07 correction): a face the mirror is deliberately painting BURNT
                // or GHOSTED is not a face that needs resting. BurnLookPolicy's rule 1 says an
                // ACTIVATED card wears the look its destination earns on EVERY board, and a card in
                // a peer's discard or burnt arc wears the look the game left on the owner's own
                // widget; this hold is what lets the mirror write either without this sweep undoing
                // it on the next pump. See HoldCardFxLook.
                if (!HasCardFxHold(face))
                    NormalizeCardFx(face);
            }
            MaybeCensus();
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("observe", ex);
        }
    }

    /// <summary>
    /// Instance ids of the mod-owned faces a PEER MIRROR is currently dimming on purpose (report
    /// item 8, extension record 41). Ids and not references: this set is only ever asked "is this
    /// face held", so holding a strong reference to a clone that is being destroyed would buy
    /// nothing and cost a leak.
    /// </summary>
    private static readonly HashSet<int> s_mirroredDim = new();

    /// <summary>
    /// ITEM 8: TAKE OR RELEASE THE HOLD that stops <see cref="Normalize"/> re-brightening a face
    /// whose halves a peer mirror is deliberately drawing at alpha 0.5.
    ///
    /// <para>WHY A HOLD AND NOT A RACE. Both writers make the SAME call —
    /// <c>FullAbilityCard.SetInteractable</c> — to the SAME <c>CanvasGroup</c>, for opposite
    /// reasons that are each correct in their own place. <see cref="Normalize"/> exists because a
    /// POOLED widget carries the previous user's dimming forward
    /// (<c>FullAbilityCardAction.ResetInteractable</c> clears the booleans and leaves the alpha),
    /// so a mod clone must start bright. <c>Net.Remote.RemoteBoardCard.SetSpentHalves</c> exists
    /// because the owner's own board draws a PLAYED half at 0.5 and the 1:1 rule says a peer must
    /// see that. Left to fight, they alternate per rebuild and the user sees a flicker instead of
    /// an answer; so the mirror announces itself and this file stands down for exactly the faces
    /// it names. One writer owns the final value — the rule this project has already paid for
    /// breaking more than once.</para>
    ///
    /// <para>The census still MEASURES a held face; only the correcting write is withheld. That is
    /// deliberate: the alpha range it prints is the instrument that would show a hold gone wrong,
    /// and an instrument that stops looking where the risk moved is no instrument.</para>
    /// </summary>
    internal static void HoldMirroredDim(FullAbilityCard? face, bool held)
    {
        if (face == null)
            return;
        try
        {
            int id = face.GetInstanceID();
            if (held)
                s_mirroredDim.Add(id);
            else
                s_mirroredDim.Remove(id);
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("mirrored-dim hold", ex);
        }
    }

    /// <summary>Is a peer mirror deliberately dimming this face right now?</summary>
    private static bool HasMirroredDim(FullAbilityCard face)
        => s_mirroredDim.Count > 0 && s_mirroredDim.Contains(face.GetInstanceID());

    /// <summary>Instance ids of the mod-owned faces a PEER MIRROR is deliberately painting with a
    /// card-FX look — BURNT or GHOSTED, the two the game's own timelines produce.
    /// Ids and not references, for the same reason as <see cref="s_mirroredDim"/>.</summary>
    private static readonly HashSet<int> s_cardFxHold = new();

    /// <summary>
    /// ITEM 4 (2026-09-07, the user's correction): TAKE OR RELEASE THE HOLD that stops
    /// <see cref="NormalizeCardFx"/> re-resting a face a peer mirror is deliberately painting.
    ///
    /// <para>WHY IT IS OWED, AND WHY IT IS A HOLD. The rule is now
    /// <see cref="BurnLookPolicy"/>'s rule 1a: an ACTIVATED card bound for <c>Lost</c> wears the
    /// permanent burnt wash and keeps it, <i>"lokal und remote"</i>. On a mirror the face is a
    /// mod-built clone whose <c>CardEffects</c> has been stripped, so the burnt terms have to be
    /// written straight onto its materials — which is exactly what
    /// <c>NormalizeCardFx</c> exists to UNDO, because a clone of a pooled widget normally inherits
    /// the previous user's wash and must start fresh. Two correct writers, opposite directions,
    /// same four floats: left to fight they alternate per rebuild and the user sees a flicker
    /// instead of an answer. So the mirror announces itself and this file stands down for exactly
    /// the faces it names — the same contract, for the same reason, as
    /// <see cref="HoldMirroredDim"/> one method up.</para>
    ///
    /// <para>ONE PRODUCER, AND IT IS THE CHOKE POINT RATHER THAN A SURFACE (ModBuild 479, user item
    /// 2b). The 478 build shipped this hold with a single caller — the mirrored ACTIVE cell — and
    /// left every other mirrored surface to remember it. The mirrored BURNT PILE FAN did not, and
    /// the write war this doc predicted is exactly what the user then reported, in his own words:
    /// <i>"Öffnet der andere Spieler seine verbrannten Karten kämpfen scheinbar zwei Effekte
    /// gegeneinander: Ich sehe die verbrannte Karte mit Feuer Effekt und dann wieder ohne Feuer
    /// Effekt alternierend dauerhaft."</i> A per-surface hold is a rule every future surface can
    /// forget once, so the hold is now taken and released by
    /// <c>Net.RemoteCardArt.SetAbilityCardFxProgress</c> / <c>ClearAbilityCardFx</c> — the ONE call
    /// through which a mirrored look is ever written — and no mirrored surface takes it by
    /// hand.</para>
    ///
    /// <para>THE HOLD IS THE LOOK, NOT THE COLOUR. A GHOST is as much a mod-written look as a BURN:
    /// <c>CardEffects.GhostOutOnTimeline</c> drives the same <c>_GreyOut/_Flow/_Dissolve</c>
    /// (CardEffects.cs:686-720) that <see cref="NormalizeCardFx"/> zeroes, so a face wearing the
    /// mirrored discard ghost needs this hold for the identical reason a burnt one does.</para>
    /// </summary>
    internal static void HoldCardFxLook(FullAbilityCard? face, bool held)
    {
        if (face == null)
            return;
        try
        {
            int id = face.GetInstanceID();
            if (held)
                s_cardFxHold.Add(id);
            else
                s_cardFxHold.Remove(id);
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("card-FX look hold", ex);
        }
    }

    /// <summary>Is a peer mirror deliberately painting a card-FX look on this face right now?</summary>
    private static bool HasCardFxHold(FullAbilityCard face)
        => s_cardFxHold.Count > 0 && s_cardFxHold.Contains(face.GetInstanceID());

    /// <summary>Drop all state (scene teardown / hot reload), mirroring <c>CardArtGuard.Reset</c>.
    /// The rest-state material copies are destroyed here and only here: this runs on scene teardown,
    /// where every clone that could still be wearing one is going away in the same breath.</summary>
    internal static void Reset()
    {
        s_nextCensus = 0f;
        s_lastVerdict = null;
        // The diff pair belongs to the scene that is going away; the latch clears with it so the next
        // scene gets its own reading rather than inheriting a stale "already reported".
        s_diffClone = null;
        s_diffReference = null;
        s_diffReferenceRank = int.MaxValue;
        s_diffLogged = false;
        // The held faces belong to the boards going away with the scene; a stale id could otherwise
        // collide with a fresh clone's and stand this file down for a face nobody is mirroring.
        s_mirroredDim.Clear();
        s_cardFxHold.Clear();
        // The burn-look policy tracks the same population — adopted and mod-built ability faces —
        // and its per-face bookkeeping goes away with the same scene. Routed through here rather
        // than through a second CardsModule.Reset line so the card-art teardown stays one call.
        BurnLookPolicy.Reset();
        foreach (Material copy in s_restCopies.Values)
        {
            if (copy != null)
                Object.Destroy(copy);
        }
        s_restCopies.Clear();
    }

    // ------------------------------------------------------------------- the gate --

    /// <summary>
    /// Is this clone's half state still different from the overlay's? The write is CHANGE-GATED
    /// rather than remembered-once, which is what keeps it free of per-clone bookkeeping (a set of
    /// instance ids would grow for the whole session — the map-room fan rebuilds its fronts) and
    /// what makes it safe to run on the face pump: once corrected the test is false and nothing is
    /// written again, so this can never become a per-frame write war.
    ///
    /// <para>The rendered-tint half of the test is asked ONLY of a DISABLED Selectable. An enabled
    /// one is driven by Unity's own colour transition every state change, so testing its
    /// <c>CanvasRenderer</c> colour against white would be a permanent disagreement with the engine
    /// — the exact shape of a write war this project has already paid for once. A disabled
    /// Selectable is driven by nobody, so one write closes the gate for good.</para>
    /// </summary>
    private static bool NeedsCorrection(FullAbilityCard face) =>
        NeedsCorrection(face.topActionButton) || NeedsCorrection(face.bottomActionButton);

    private static bool NeedsCorrection(FullAbilityCardAction? half)
    {
        if (half == null)
            return false;
        if (half.canvasGroup != null && half.canvasGroup.alpha < 0.999f)
            return true;
        return NeedsCorrection(half.actionButton) || NeedsCorrection(half.defaultActionButton);
    }

    private static bool NeedsCorrection(Selectable? selectable)
    {
        if (selectable == null)
            return false;
        if (!selectable.interactable)
            return true;
        if (selectable.enabled)
            return false;   // Unity owns this one's tint — never argue with it
        Graphic? target = selectable.targetGraphic;
        if (target == null || target.canvasRenderer == null)
            return false;
        Color c = target.canvasRenderer.GetColor();
        return c.r < 0.999f || c.g < 0.999f || c.b < 0.999f || c.a < 0.999f;
    }

    // ---------------------------------------------------------------- ownership --

    /// <summary>
    /// True only for a face the mod built for itself and nobody else can be holding: NOT a
    /// registered adopted live widget, and with no <c>AbilityCardUI</c> anywhere above it. The
    /// second half of the test is the game's own invariant, not a guess — <c>ObjectPool.RecycleCard</c>
    /// re-parents every game-owned <c>fullAbilityCard</c> back under its row
    /// (<c>fullAbilityCard.transform.SetParent(component.transform)</c>, ObjectPool.cs:545-547), and
    /// the only faces that escape that parent are the ones <see cref="CardFace.Adopt"/> re-hosted,
    /// which the first half already excludes.
    /// </summary>
    private static bool IsModOwnedCopy(FullAbilityCard face)
    {
        if (CardArtGuard.IsAdopted(face))
            return false;
        // includeInactive: a clone is configured under an INACTIVE host, which is the one moment a
        // correction lands before a first drawn frame.
        return face.GetComponentInParent<AbilityCardUI>(includeInactive: true) == null;
    }

    // ------------------------------------------------------------- the same gate --

    /// <summary>
    /// Put a mod-owned clone in the state the OVERLAY is put in, by making the overlay's call.
    /// <c>FullAbilityCard.SetInteractable(true)</c> is what <c>SetMode(CardHandMode.DeckSelection)</c>
    /// runs (AbilityCardUI.cs:929); running it here is the whole fix.
    ///
    /// <para>The trailing CanvasRenderer write is not a second opinion about the colour — it is the
    /// state Unity's own <c>Selectable.InstantClearState()</c> leaves behind (white = NO tint), and
    /// it is needed because the borrowed source runs <c>DisableEventDetection(true)</c>
    /// (<c>actionButton.enabled = false</c>, AbilityCardUI.cs:584-588). A DISABLED Selectable never
    /// re-runs its colour transition, so flipping <c>interactable</c> alone would leave a cloned
    /// disabled-grey tint frozen on the action image. On an ENABLED Selectable this is a no-op that
    /// Unity overwrites with its own normal colour on the next transition.</para>
    /// </summary>
    private static void Normalize(FullAbilityCard face)
    {
        // READ IT BEFORE WRITING IT. Once the correction lands, every later census reports the
        // CORRECTED value — so the number that PROVES the divergence only exists in this instant.
        // It is kept as a running range over every clone corrected this session (a range, not the
        // one sample that happened to be first: a mode is not a distribution).
        NoteBefore(face.topActionButton);
        NoteBefore(face.bottomActionButton);
        string before = $"halfCanvasGroup.alpha {s_beforeMinAlpha:F2}..{s_beforeMaxAlpha:F2}, " +
                        $"{s_beforeNonInteractable} of {s_beforeHalves} half/halves reported " +
                        "actionButton.interactable = false";

        bool viaGameCall = false;
        try
        {
            face.SetInteractable(active: true);
            viaGameCall = true;
        }
        catch (System.Exception ex)
        {
            // The game call reaches InfuseElement / CardEnhancementElements collections; a clone
            // that lost one of them must still come out un-dimmed, so fall back to the one value
            // that IS the reported symptom.
            if (!s_refusalLogged)
            {
                s_refusalLogged = true;
                VRLog.Debug(Scope, $"CARD HALF TONE: FullAbilityCard.SetInteractable(true) refused on a mod " +
                                   $"clone ({ex.GetType().Name}: {ex.Message}) — writing the half CanvasGroups " +
                                   "directly. If the census keeps reporting a rising 'corrections' count, THIS " +
                                   "is why the gate never closes.");
            }
            ForceHalfAlpha(face.topActionButton);
            ForceHalfAlpha(face.bottomActionButton);
        }

        ClearTint(face.topActionButton);
        ClearTint(face.bottomActionButton);
        s_normalizedCount++;

        if (s_fixLogged)
            return;
        s_fixLogged = true;
        VRLog.Info(Scope, $"CARD HALF TONE: first mod-built card front normalised — MEASURED BEFORE THE " +
                          $"WRITE: {before}" +
                          (viaGameCall ? "; corrected through the game's own FullAbilityCard.SetInteractable(true)"
                                       : "; corrected through the CanvasGroup fallback (the game call refused)") +
                          " — the SAME call AbilityCardUI.SetMode(DeckSelection) makes for the hover " +
                          "OVERLAY (AbilityCardUI.cs:929). Without it a pooled-borrow clone keeps the " +
                          "template's leftover FullAbilityCardAction.canvasGroup.alpha (0.5 = the " +
                          "reported grey in the two selectable areas) because ResetInteractable() " +
                          "clears the booleans and never the alpha. Adopted LIVE faces are never " +
                          "written to — read the CARD HALF TONE CENSUS line for their numbers.");
    }

    /// <summary>Running pre-correction range over every clone half this session.</summary>
    private static float s_beforeMinAlpha = float.PositiveInfinity;
    private static float s_beforeMaxAlpha = float.NegativeInfinity;
    private static int s_beforeHalves;
    private static int s_beforeNonInteractable;

    private static void NoteBefore(FullAbilityCardAction? half)
    {
        if (half == null)
            return;
        s_beforeHalves++;
        float alpha = half.canvasGroup != null ? half.canvasGroup.alpha : 1f;
        if (alpha < s_beforeMinAlpha) s_beforeMinAlpha = alpha;
        if (alpha > s_beforeMaxAlpha) s_beforeMaxAlpha = alpha;
        if (half.actionButton != null && !half.actionButton.interactable)
            s_beforeNonInteractable++;
    }

    private static void ForceHalfAlpha(FullAbilityCardAction? half)
    {
        if (half == null || half.canvasGroup == null)
            return;
        half.canvasGroup.alpha = 1f;
    }

    private static void ClearTint(FullAbilityCardAction? half)
    {
        if (half == null)
            return;
        ClearTint(half.actionButton);
        ClearTint(half.defaultActionButton);
    }

    private static void ClearTint(Selectable? selectable)
    {
        // Only for a Selectable Unity is NOT driving — see NeedsCorrection. An enabled one gets its
        // normal colour from the engine's own transition the moment interactable flips.
        if (selectable == null || selectable.enabled)
            return;
        Graphic? target = selectable.targetGraphic;
        if (target != null && target.canvasRenderer != null)
            target.canvasRenderer.SetColor(Color.white);
    }

    // ------------------------------------------------- the card-FX material rest state --

    /// <summary>The four floats <c>CardEffects.RestoreCard()</c> writes (CardEffects.cs:474-483) —
    /// the card's REST state. <c>_GreyOut</c> is the desaturation wash the photograph measures;
    /// the other three are the burn/discard animation terms that ride the same materials and are
    /// zeroed by the same call, so they are restored together or the reconstruction is partial.</summary>
    private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int BurnId = Shader.PropertyToID("_Burn");

    /// <summary>The SIGNATURE that identifies a card-FX material without naming a shader or a
    /// material asset: <c>_GreyOut</c> AND <c>_PosAndBounds</c> together. Both are
    /// <c>CardEffects</c>-only properties (CardEffects.cs:18/26); requiring the pair means a future
    /// UI shader that happens to expose a "_GreyOut" cannot be mistaken for one of these. The same
    /// signature style the earlier card-material work used, for the same reason: a shader NAME is a
    /// string a game patch can change, a property pair is what the code actually reads.</summary>
    private static readonly int PosAndBoundsId = Shader.PropertyToID("_PosAndBounds");

    /// <summary>How far from 0 a rest term may sit before it is worth a write. Well below anything
    /// the eye can see and well above float noise, so a corrected face never re-triggers.</summary>
    private const float RestEpsilon = 0.002f;

    /// <summary>Rest-state copies, keyed by a CONTENT signature of the source material. One copy per
    /// distinct <i>look</i>, shared by every clone that draws through it — a mod-built front has no
    /// <c>CardEffects</c> and therefore nothing that could ever want a per-card animation, so
    /// sharing is free and it is what keeps a fan that rebuilds ten fronts per second from leaking
    /// ten materials per second.
    ///
    /// <para>THE KEY USED TO BE THE SOURCE'S INSTANCE ID AND THAT IS WHY THE CAP WAS REACHED. The
    /// note on the cap below asserted that per-image materials for clones were something
    /// <i>"nothing does today"</i>. <c>CardEffects.Initialize</c> does exactly that, on every widget
    /// it wakes: <c>image2.material = new Material(image2.material)</c> for each of the up-to-ten
    /// <c>imgComp</c> entries, plus one more for <c>fgFx</c> (CardEffects.cs:333-340). So a clone of
    /// a pool widget the game has already used contributes ~8 DISTINCT instance ids that are byte
    /// -identical copies of one authored asset, and 32 entries is four faces. Both hardware logs of
    /// the ModBuild 476 session hit the cap and said so — host <c>Player.log:106863</c>, peer
    /// <c>remote/Player.log:68760</c> — which is the falsifying reading for that sentence.</para>
    ///
    /// <para>The signature is (shader instance id, source material NAME, <c>_PosAndBounds</c>). All
    /// of one card's FX images carry the SAME <c>_PosAndBounds</c> — <c>Initialize</c> writes the
    /// card's single <c>canvasPosition</c>/<c>cardBounds</c> into every one of them (:347) — so a
    /// whole face now collapses to one entry, and two cards at the same canvas slot share it. The
    /// name separates the <c>_useLowEffect</c> variant, which is a different authored material on a
    /// possibly identical shader (:318). The population is therefore the number of distinct card
    /// SLOTS, not the number of faces ever built.</para></summary>
    private static readonly Dictionary<RestKey, Material> s_restCopies = new(16);

    /// <summary>The content signature above. A struct key so the lookup allocates nothing on the
    /// per-rebuild path this sits on.</summary>
    private readonly struct RestKey : System.IEquatable<RestKey>
    {
        private readonly int _shader;
        private readonly string _name;
        private readonly Vector4 _bounds;

        internal RestKey(Material source)
        {
            Shader? shader = source.shader;
            _shader = shader != null ? shader.GetInstanceID() : 0;
            _name = source.name ?? string.Empty;
            _bounds = source.HasProperty(PosAndBoundsId) ? source.GetVector(PosAndBoundsId) : Vector4.zero;
        }

        public bool Equals(RestKey other) =>
            _shader == other._shader && _name == other._name && _bounds == other._bounds;

        public override bool Equals(object? obj) => obj is RestKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _shader;
                hash = (hash * 397) ^ _name.GetHashCode();
                hash = (hash * 397) ^ _bounds.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>Hard cap on distinct rest copies — the leak guard, not the fix. Sized to the card
    /// SLOTS a session can produce now that the key is a content signature rather than an instance
    /// id (see <see cref="s_restCopies"/>): a hand fan is a dozen positions and a browse fan a few
    /// dozen, so a session that reaches this has found a new producer and the refusal is logged
    /// rather than silent. It was 32 while the key was per-image, which both ModBuild 476 logs
    /// exhausted inside one scenario.</summary>
    private const int MaxRestCopies = 256;

    /// <summary>Card images whose material was swapped to a rest copy this session.</summary>
    private static int s_fxCorrected;

    /// <summary>Largest deviation from rest seen BEFORE a write — the number that proves the defect
    /// existed, kept because every later reading reports the corrected value.</summary>
    private static float s_fxWorstBefore;

    /// <summary>True once the card-FX correction has logged its first write, and once its cap has
    /// been reported.</summary>
    private static bool s_fxLogged;
    private static bool s_fxCapLogged;

    /// <summary>
    /// Put a mod-owned front's card-FX materials in the state <c>CardEffects.RestoreCard()</c> would
    /// have left them in, had the component that calls it survived on this object. See the class
    /// doc for the whole derivation; in one sentence: the borrowed widget never activates and the
    /// clone has <c>CardEffects</c> stripped, so <c>Awake → Initialize → RestoreCard</c> never runs
    /// and the face inherits the shared material asset's <c>_GreyOut</c> — the desaturation the
    /// photograph measures.
    ///
    /// <para>Sweeps EVERY <c>Graphic</c> under the face, not just the two action plates: the same
    /// materials sit on the header, the two default-action plates and their icons
    /// (<c>CardEffects.imgComp</c>, CardEffects.cs:294-301), and a fix that reddened the halves
    /// while leaving the header washed would be a new divergence rather than the end of this one.
    /// Inactive children included — a clone is configured under an INACTIVE host, which is the one
    /// moment a correction lands before a first drawn frame.</para>
    /// </summary>
    private static void NormalizeCardFx(FullAbilityCard face)
    {
        Graphic[] graphics;
        try
        {
            graphics = face.GetComponentsInChildren<Graphic>(includeInactive: true);
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("card-FX sweep", ex);
            return;
        }

        int corrected = 0;
        float worst = 0f;
        string? worstWhere = null;
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic g = graphics[i];
            if (g == null)
                continue;
            Material source = g.material;
            if (source == null || !IsCardFxMaterial(source))
                continue;
            float deviation = RestDeviation(source);
            if (deviation <= RestEpsilon)
                continue;   // already at rest — the gate, and it closes for good after one write
            Material? rest = RestCopyOf(source);
            if (rest == null)
                continue;
            g.material = rest;
            corrected++;
            if (deviation > worst)
            {
                worst = deviation;
                // NAME THE IMAGE AND THE TERM. The ModBuild-197 log printed "worst term 1.000" and
                // 10 corrected images while EVERY action plate independently read _GreyOut 0.00 —
                // so this pass has always been correcting something that is NOT a plate, and the log
                // could not say what. It can now, which is the difference between a fixed number and
                // a readable one.
                worstWhere = $"'{g.name}' ({WorstTermName(source)})";
            }
        }

        if (corrected == 0)
            return;
        s_fxCorrected += corrected;
        if (worst > s_fxWorstBefore)
            s_fxWorstBefore = worst;
        if (s_fxLogged)
            return;
        s_fxLogged = true;
        VRLog.Info(Scope, "CARD FX REST: a mod-built card front was drawing through card-FX materials " +
                          $"that had never been reset — {corrected} card image(s) on this face, worst " +
                          $"term {worst:F3} away from rest BEFORE the write on {worstWhere ?? "(unknown)"} " +
                          "(0 = the game's own rest " +
                          "value). NOTE (ModBuild 198): this is NOT the reported colour defect — the " +
                          "action plates read _GreyOut 0.00 on the CLONE and on the GAME-POOL widget " +
                          "they are copied from, so whatever image is named above, it is not a plate. " +
                          "The plate defect is a SPRITE SWAP; see REMOTE PLATE. Corrected to " +
                          "_GreyOut = _Flow = _Dissolve = _Burn = 0 on a PRIVATE " +
                          "copy of each material, which is exactly what CardEffects.RestoreCard() " +
                          "(CardEffects.cs:466) writes and the ONLY place the game ever writes it. A " +
                          "mod-built front never gets that call: the borrowed pool widget is spawned " +
                          "activate:false under an inactive holder so its Awake never runs, and " +
                          "Net.RemoteCardArt.StripFragileEffects destroys CardEffects on the clone " +
                          "before it activates (correctly — its _PosAndBounds is only valid at the " +
                          "card's original canvas position). _GreyOut is the game's 'this card is " +
                          "spent' DESATURATION, which is why the fan's red plates read neutral grey " +
                          "while their ornament strokes and the card frame around them measured " +
                          "identical to the overlay's. Adopted and pooled faces are never written to, " +
                          "so the real discarded/lost wash still shows wherever the game owns the " +
                          "widget. Read CARD HALF TONE CENSUS for the per-population numbers.");
    }

    /// <summary>Does this material carry the <c>CardEffects</c> property pair? See
    /// <see cref="PosAndBoundsId"/> for why it is a pair and not a shader name.</summary>
    private static bool IsCardFxMaterial(Material material)
    {
        try
        {
            return material.HasProperty(GreyOutId) && material.HasProperty(PosAndBoundsId);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>The largest of the four rest terms, i.e. "how far from RestoreCard() is this".</summary>
    private static float RestDeviation(Material material)
    {
        float worst = Mathf.Abs(material.GetFloat(GreyOutId));
        worst = Mathf.Max(worst, Mathf.Abs(SafeGet(material, FlowId)));
        worst = Mathf.Max(worst, Mathf.Abs(SafeGet(material, DissolveId)));
        worst = Mathf.Max(worst, Mathf.Abs(SafeGet(material, BurnId)));
        return worst;
    }

    /// <summary>Which of the four rest terms is the one that is off — so the log can NAME it instead
    /// of printing an anonymous magnitude.</summary>
    private static string WorstTermName(Material material)
    {
        float grey = Mathf.Abs(SafeGet(material, GreyOutId));
        float flow = Mathf.Abs(SafeGet(material, FlowId));
        float dissolve = Mathf.Abs(SafeGet(material, DissolveId));
        float burn = Mathf.Abs(SafeGet(material, BurnId));
        float best = grey;
        string name = "_GreyOut";
        if (flow > best) { best = flow; name = "_Flow"; }
        if (dissolve > best) { best = dissolve; name = "_Dissolve"; }
        if (burn > best) { name = "_Burn"; }
        return name;
    }

    /// <summary>A term the low-effect material variant may not expose reads as already-at-rest —
    /// <c>RestoreCard</c> itself skips those three under <c>_useLowEffect</c> (CardEffects.cs:476).</summary>
    private static float SafeGet(Material material, int id) =>
        material.HasProperty(id) ? material.GetFloat(id) : 0f;

    /// <summary>
    /// The shared rest copy for one source material, minted on first use. The copy — never the
    /// source — is what gets written: the source is the SHARED <c>GUI_CardEffect_Mat</c> asset every
    /// ability card in the flat game draws through, and zeroing it would be an unrestorable write
    /// into game state (and would silently un-grey the flat game's own discard pile).
    /// </summary>
    private static Material? RestCopyOf(Material source)
    {
        RestKey key = new(source);
        if (s_restCopies.TryGetValue(key, out Material existing) && existing != null)
            return existing;
        if (s_restCopies.Count >= MaxRestCopies)
        {
            if (!s_fxCapLogged)
            {
                s_fxCapLogged = true;
                // HW-VERIFY (2026-09-07 item 4, "lokal und remote"): the cap that decides whether a
                // mod-built front is left drawing through a GAME-OWNED material. Grep token:
                // "CARD FX REST" with "entry cap".
                //
                // WHY THIS MATTERS TO THE BURN LOOK. Past the cap this method returns null and the
                // caller leaves the game's own material on the clone — so a peer's mirrored card
                // shows whatever burn/grey state the pooled widget happens to carry, and a later
                // burn on that widget repaints the peer's card underneath it. That is a
                // mirror-side source of exactly the inconsistency item 4 reports.
                // FALSIFIER — this line was NOT inert before: host Player.log:106863 and peer
                // remote/Player.log:68760 both printed it on ModBuild 476, with the old per-image
                // key and a cap of 32. INERT is now the expected reading; if it fires again with a
                // content-signature key and 256 entries, a genuinely new producer exists and THAT
                // is the lead, not this number.
                VRLog.Note(Scope, $"CARD FX REST: the rest-copy cache hit its {MaxRestCopies} entry cap — " +
                                  "further mod-built fronts keep the source material and may still read " +
                                  "grey, and a later burn on the pooled widget they borrowed from would " +
                                  "repaint them. The key is a CONTENT signature (shader + material name " +
                                  "+ _PosAndBounds), so one card face is ONE entry and this cap is a " +
                                  "count of distinct card SLOTS; reaching it means a new producer of " +
                                  "card-FX materials exists. The cap is the leak guard, not the fix.");
            }
            return null;
        }
        Material copy;
        try
        {
            copy = new Material(source) { name = source.name + " (VR-rest)" };
            copy.SetFloat(GreyOutId, 0f);
            if (copy.HasProperty(FlowId)) copy.SetFloat(FlowId, 0f);
            if (copy.HasProperty(DissolveId)) copy.SetFloat(DissolveId, 0f);
            if (copy.HasProperty(BurnId)) copy.SetFloat(BurnId, 0f);
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("card-FX rest copy", ex);
            return null;
        }
        s_restCopies[key] = copy;
        return copy;
    }

    // -------------------------------------------------------------- the measurement --

    /// <summary>Accumulated half-state readings for one population.</summary>
    private struct Bucket
    {
        public int Faces;
        public int Halves;
        public float MinAlpha;
        public float MaxAlpha;
        public float SumAlpha;
        public int Interactable;
        public int ButtonsEnabled;
        public float MinTint;
        public float MaxTint;
        public float SumTint;
        public string? Shader;
        public bool MixedShaders;

        // The card-FX rest state, read off the SAME action-plate image the columns above measure —
        // no extra traversal, and it is the exact image whose fill the user photographed.
        public int FxImages;
        public float MinGrey;
        public float MaxGrey;
        public float SumGrey;
        public float MaxOtherFx;   // worst of _Flow/_Dissolve/_Burn — the burn/discard siblings
        public int FxZeroBounds;   // plates whose _PosAndBounds is still the material default
        public Vector4 FxBoundsSample;

        // ═══ THE TERM THAT ACTUALLY DECIDES THE PLATE'S PIXELS (ModBuild 198) ═══
        // The action plate is a SPRITE-SWAP Selectable, so what it draws is
        // `overrideSprite ?? sprite` — chosen by Unity from `currentSelectionState`, i.e. from
        // Selectable.IsInteractable(), which is `m_GroupsAllowInteraction && m_Interactable`. Three
        // rounds read the second term (the `interactable` column above) and never the conjunction,
        // and none of them ever read which SPRITE was drawn. Both are here now, and the sprite NAME
        // is the outcome measurement: CLONE and GAME-LIVE printing the same plate sprite is the
        // proof, and printing different ones is the defect, in one line either way.
        public int Effective;        // halves whose Selectable.IsInteractable() is TRUE
        public int Overridden;       // halves drawing through an overrideSprite (a live swap)
        public int SwapTransition;   // halves still carrying Transition.SpriteSwap
        public string? PlateSprite;  // the DRAWN plate sprite name
        public bool MixedPlates;
        public float MinGraphicAlpha;   // Graphic.color.a — vertex colour, NOT the CanvasRenderer's
        public float MaxGraphicAlpha;
        public string? GraphicColor;    // one sample of Graphic.color, never measured before 198

        public void Seed()
        {
            MinAlpha = float.PositiveInfinity;
            MaxAlpha = float.NegativeInfinity;
            MinTint = float.PositiveInfinity;
            MaxTint = float.NegativeInfinity;
            MinGrey = float.PositiveInfinity;
            MaxGrey = float.NegativeInfinity;
            MinGraphicAlpha = float.PositiveInfinity;
            MaxGraphicAlpha = float.NegativeInfinity;
        }

        /// <summary>One half's plate-draw state — what the engine actually puts on screen.</summary>
        public void AddPlate(bool effective, bool overridden, bool swap, string? sprite,
                             Color graphicColor)
        {
            if (effective) Effective++;
            if (overridden) Overridden++;
            if (swap) SwapTransition++;
            if (graphicColor.a < MinGraphicAlpha) MinGraphicAlpha = graphicColor.a;
            if (graphicColor.a > MaxGraphicAlpha) MaxGraphicAlpha = graphicColor.a;
            GraphicColor ??= $"{graphicColor.r:F2},{graphicColor.g:F2},{graphicColor.b:F2},{graphicColor.a:F2}";
            if (sprite == null)
                return;
            if (PlateSprite == null) PlateSprite = sprite;
            else if (PlateSprite != sprite) MixedPlates = true;
        }

        public readonly string DescribePlate() =>
            $", EFFECTIVE IsInteractable() {Effective}/{Halves}, spriteSwap {SwapTransition}/{Halves}, " +
            $"overrideSprite live on {Overridden}/{Halves}, DRAWN PLATE SPRITE " +
            $"'{PlateSprite ?? "(none)"}'{(MixedPlates ? " (MIXED)" : string.Empty)}, " +
            $"Graphic.color {GraphicColor ?? "n/a"} (alpha " +
            (Halves > 0 && MinGraphicAlpha <= MaxGraphicAlpha
                ? $"{MinGraphicAlpha:F2}..{MaxGraphicAlpha:F2})"
                : "n/a)");

        public void AddFx(float grey, float otherWorst, Vector4 posAndBounds)
        {
            FxImages++;
            if (grey < MinGrey) MinGrey = grey;
            if (grey > MaxGrey) MaxGrey = grey;
            SumGrey += grey;
            if (otherWorst > MaxOtherFx) MaxOtherFx = otherWorst;
            if (posAndBounds.z == 0f && posAndBounds.w == 0f) FxZeroBounds++;
            else FxBoundsSample = posAndBounds;
        }

        public readonly float AvgGrey => FxImages > 0 ? SumGrey / FxImages : 0f;

        public readonly string DescribeFx() =>
            FxImages == 0
                ? ", no card-FX material on the plate (nothing to read)"
                : $", _GreyOut {MinGrey:F2}..{MaxGrey:F2} (avg {AvgGrey:F2}) over {FxImages} plate " +
                  $"image(s), worst _Flow/_Dissolve/_Burn {MaxOtherFx:F2}, _PosAndBounds unset on " +
                  $"{FxZeroBounds}/{FxImages} (a set one reads {FxBoundsSample.x:F0},{FxBoundsSample.y:F0}," +
                  $"{FxBoundsSample.z:F0}x{FxBoundsSample.w:F0})";

        public void AddHalf(float alpha, bool interactable, bool buttonEnabled, float tint, string? shader)
        {
            Halves++;
            if (alpha < MinAlpha) MinAlpha = alpha;
            if (alpha > MaxAlpha) MaxAlpha = alpha;
            SumAlpha += alpha;
            if (interactable) Interactable++;
            if (buttonEnabled) ButtonsEnabled++;
            if (tint < MinTint) MinTint = tint;
            if (tint > MaxTint) MaxTint = tint;
            SumTint += tint;
            if (shader == null)
                return;
            if (Shader == null) Shader = shader;
            else if (Shader != shader) MixedShaders = true;
        }

        public readonly string Describe(string name)
        {
            if (Halves == 0)
                return $"{name} {Faces} face(s), no readable half";
            return $"{name} {Faces} face(s)/{Halves} half/halves: " +
                   $"halfCanvasGroup.alpha {MinAlpha:F2}..{MaxAlpha:F2} (avg {SumAlpha / Halves:F2}), " +
                   $"interactable {Interactable}/{Halves}, buttonEnabled {ButtonsEnabled}/{Halves}, " +
                   $"renderedTint {MinTint:F2}..{MaxTint:F2} (avg {SumTint / Halves:F2}), " +
                   $"shader '{Shader ?? "n/a"}'{(MixedShaders ? " (MIXED)" : string.Empty)}" +
                   DescribePlate() + DescribeFx();
        }
    }

    private static void MaybeCensus()
    {
        if (Time.unscaledTime < s_nextCensus)
            return;
        s_nextCensus = Time.unscaledTime + CensusIntervalSeconds;
        RunCensus();
    }

    /// <summary>
    /// One whole-scene reading of every ability face, split into the three populations, printed as
    /// one line. <c>Resources.FindObjectsOfTypeAll</c> rather than <c>FindObjectsOfType</c> on
    /// purpose: a window row's full-card preview is only ACTIVE while the pointer is on it, and the
    /// overlay's half state is written by <c>Init</c>/<c>SetMode</c> long before that — so the
    /// reference population would otherwise be invisible to the census in almost every frame it
    /// runs. Assets and prefabs are skipped (<c>gameObject.scene.IsValid()</c>), which also keeps the
    /// pool TEMPLATE instance — the object a clone is copied from — out of the comparison unless it
    /// really is in the scene.
    /// </summary>
    private static void RunCensus()
    {
        FullAbilityCard[] all;
        try
        {
            all = Resources.FindObjectsOfTypeAll<FullAbilityCard>();
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("census sweep", ex);
            return;
        }

        s_diffClone = null;
        s_diffReference = null;
        s_diffReferenceRank = int.MaxValue;

        Bucket game = default, adopted = default, clone = default, pool = default;
        game.Seed();
        adopted.Seed();
        clone.Seed();
        pool.Seed();
        int skipped = 0;

        for (int i = 0; i < all.Length; i++)
        {
            FullAbilityCard face = all[i];
            if (face == null || !face.gameObject.scene.IsValid())
            {
                skipped++;
                continue;
            }
            try
            {
                bool isAdopted = CardArtGuard.IsAdopted(face);
                bool owned = !isAdopted
                             && face.GetComponentInParent<AbilityCardUI>(includeInactive: true) == null;
                // A widget PARKED IN THE POOL is game-owned but is nobody's overlay: it has been
                // through no Init/SetMode since it was handed back, and OnReturnedToPool's
                // ResetInteractable() clears the booleans without restoring the alpha. It is
                // therefore the exact object a pooled-borrow clone is copied FROM, and mixing it
                // into the overlay's reference population would blur the very comparison this
                // census exists to make. Its own numbers are printed instead.
                bool pooled = !isAdopted && !owned
                              && face.GetComponentInParent<ObjectPool>(includeInactive: true) != null;
                ref Bucket bucket = ref (isAdopted
                    ? ref adopted
                    : ref (owned ? ref clone : ref (pooled ? ref pool : ref game)));
                int population = isAdopted ? 2 : (owned ? 3 : (pooled ? 1 : 0));
                bucket.Faces++;
                Sample(ref bucket, face.topActionButton, population);
                Sample(ref bucket, face.bottomActionButton, population);
            }
            catch (System.Exception ex)
            {
                skipped++;
                LogErrorOnce("census sample", ex);
            }
        }

        s_censuses++;
        MaybeDumpPlateDiff();
        int compared = game.Faces + adopted.Faces + clone.Faces + pool.Faces;
        string verdict = Verdict(game, adopted, clone, pool);
        string line = $"CARD HALF TONE CENSUS #{s_censuses}: compared {compared} ability face(s) " +
                      $"({skipped} skipped as assets/unreadable); " +
                      $"{game.Describe("GAME-LIVE(the overlay's population)")}; " +
                      $"{pool.Describe("GAME-POOL(parked, = what a clone is copied from)")}; " +
                      $"{adopted.Describe("ADOPTED(scenario fan/tray)")}; " +
                      $"{clone.Describe("CLONE(mod-built fronts)")}; card-FX rest writes " +
                      $"{s_fxCorrected} card image(s) over {s_restCopies.Count} distinct source " +
                      $"material(s), worst term {s_fxWorstBefore:F3} before the write (0 writes with " +
                      "the fan still grey means the sweep never reached the drawn face; writes rising " +
                      "every census means the swap is not sticking); half corrections written " +
                      $"{s_normalizedCount} " +
                      "(this counts WRITES, not clones — the write is change-gated, so a count that keeps " +
                      "rising while CLONE faces stay dim means the correction is not sticking)" +
                      (s_beforeHalves > 0
                          ? $"; the corrected halves read alpha {s_beforeMinAlpha:F2}..{s_beforeMaxAlpha:F2} " +
                            $"and {s_beforeNonInteractable}/{s_beforeHalves} non-interactable BEFORE the write"
                          : string.Empty) + ". " + verdict;
        if (line == s_lastVerdict)
            return;
        s_lastVerdict = line;
        VRLog.Info(Scope, line);
    }

    /// <summary>Name the divergence as a sentence, or say plainly that there is none — including
    /// when a population was empty, so an absent comparison never reads as a passing one.</summary>
    private static string Verdict(in Bucket game, in Bucket adopted, in Bucket clone, in Bucket pool)
    {
        if (game.Halves == 0)
            return "VERDICT: no GAME-LIVE face was in the scene (no window was showing ability rows), " +
                   "so nothing was compared against the overlay this pass — this is NOT a pass, it is " +
                   "a missing reference. Read the GAME-POOL numbers instead: they are what a clone " +
                   "would have inherited" +
                   (clone.PlateSprite != null && pool.PlateSprite != null
                       ? clone.PlateSprite == pool.PlateSprite
                           ? $", and on the DRAWN PLATE SPRITE they agree ('{clone.PlateSprite}')."
                           : $", and on the DRAWN PLATE SPRITE they do NOT: CLONE draws " +
                             $"'{clone.PlateSprite}' where the pooled source draws '{pool.PlateSprite}' " +
                             $"— effective IsInteractable() {clone.Effective}/{clone.Halves} vs " +
                             $"{pool.Effective}/{pool.Halves}. A sprite-swap Selectable whose " +
                             "IsInteractable() is false draws the skin's DISABLED plate; that is " +
                             "different ARTWORK and no colour term can correct it."
                       : ".");
        float gameAlpha = game.SumAlpha / game.Halves;
        var parts = new List<string>(5);
        // THE DRAWN SPRITE FIRST — the ModBuild-198 subject and the only term that has ever been
        // able to raise a green channel while every colour number stayed correct. Two populations
        // printing the same plate sprite name is the OUTCOME this class exists to report; a "written"
        // counter never was one.
        if (clone.PlateSprite != null && game.PlateSprite != null)
        {
            parts.Add(clone.PlateSprite == game.PlateSprite
                ? $"CLONE and overlay plates draw the SAME sprite ('{clone.PlateSprite}'), " +
                  $"overrideSprite live on {clone.Overridden}/{clone.Halves} vs " +
                  $"{game.Overridden}/{game.Halves} — the sprite-swap divergence is CLOSED"
                : $"CLONE plates draw '{clone.PlateSprite}' where the overlay draws " +
                  $"'{game.PlateSprite}' — DIFFERENT ARTWORK, not a colour. Effective " +
                  $"IsInteractable() {clone.Effective}/{clone.Halves} vs {game.Effective}/{game.Halves}, " +
                  $"spriteSwap {clone.SwapTransition}/{clone.Halves} vs " +
                  $"{game.SwapTransition}/{game.Halves}: a false IsInteractable() on a SpriteSwap " +
                  "Selectable draws AbilityCardUISkin.*ActionDisabledSprite, and no alpha, tint or " +
                  "material term can explain or correct that");
        }
        else if (clone.Halves > 0 && clone.PlateSprite == null)
        {
            parts.Add("no CLONE plate carried a sprite at all, so the plate reading is MISSING, not " +
                      "equal — the action background never streamed (see REMOTE FRONT)");
        }
        // THE CARD-FX TERM FIRST — it is the reported defect (see the class doc); the alpha columns
        // below are the ModBuild-196 subject and are kept because they are cheap, not because they
        // were ever the answer.
        if (clone.FxImages > 0 && game.FxImages > 0)
        {
            float deviation = Mathf.Abs(clone.AvgGrey - game.AvgGrey);
            parts.Add(deviation > 0.01f
                ? $"CLONE plates average _GreyOut {clone.AvgGrey:F2} against the overlay's " +
                  $"{game.AvgGrey:F2} — deviation {deviation:F2}, and _GreyOut IS the desaturation " +
                  "the photograph measures (CardEffects.cs:18/600). Corrections written this " +
                  $"session: {s_fxCorrected} image(s), worst pre-write term {s_fxWorstBefore:F3}. A " +
                  "deviation still standing here means the write is not reaching the drawn image"
                : $"CLONE and overlay plates agree on _GreyOut ({clone.AvgGrey:F2} vs " +
                  $"{game.AvgGrey:F2}) over {clone.FxImages}+{game.FxImages} plate image(s) — if the " +
                  "fan still reads grey against this, the desaturation is NOT _GreyOut and this " +
                  "diagnosis is falsified");
        }
        else if (clone.FxImages == 0 && clone.Halves > 0)
        {
            parts.Add("no CLONE plate carried a card-FX material at all, so the _GreyOut reading is " +
                      "MISSING, not zero — either the clones draw through a plain UI material or " +
                      "the signature test (_GreyOut + _PosAndBounds) no longer matches the game's");
        }
        if (clone.Halves > 0 && Mathf.Abs(clone.SumAlpha / clone.Halves - gameAlpha) > 0.01f)
            parts.Add($"CLONE halves average {clone.SumAlpha / clone.Halves:F2} against the overlay's " +
                      $"{gameAlpha:F2} — that difference IS the reported grey");
        if (adopted.Halves > 0 && Mathf.Abs(adopted.SumAlpha / adopted.Halves - gameAlpha) > 0.01f)
            parts.Add($"ADOPTED halves average {adopted.SumAlpha / adopted.Halves:F2} against the " +
                      $"overlay's {gameAlpha:F2}; the adopted face IS the game's own live widget, so " +
                      "this is the game's own state for that card (a played half on the control board " +
                      "is alpha 0.5 by CardsActionControlller.Finish) and is NOT written to here");
        return parts.Count == 0
            ? "VERDICT: every population matches the overlay on half alpha."
            : "VERDICT: " + string.Join("; ", parts.ToArray()) + ".";
    }

    /// <summary><paramref name="population"/>: 0 = GAME-LIVE (the overlay, the preferred diff
    /// reference), 1 = GAME-POOL (the fallback reference — it is what a clone is copied from),
    /// 2 = ADOPTED, 3 = CLONE (the diff subject).</summary>
    private static void Sample(ref Bucket bucket, FullAbilityCardAction? half, int population)
    {
        if (half == null)
            return;
        float alpha = half.canvasGroup != null ? half.canvasGroup.alpha : 1f;
        Button? button = half.actionButton;
        bool interactable = button != null && button.interactable;
        bool enabled = button != null && button.enabled;
        float tint = 1f;
        string? shader = null;
        Graphic? target = button != null ? button.targetGraphic : null;
        if (button != null)
        {
            // THE PLATE'S DRAWN SPRITE, and the EFFECTIVE gate that picks it. `IsInteractable()` is
            // `m_GroupsAllowInteraction && m_Interactable`; the `interactable` column above is only
            // the second term, and a false first term (a CanvasGroup above the face) is what put a
            // mod-built plate on the skin's DISABLED artwork for three hardware rounds.
            var image = target as Image;
            // Image.overrideSprite's GETTER returns activeSprite (the override if there is one, the
            // plain sprite otherwise) — so it IS the drawn sprite, and "a swap is live" is the
            // REFERENCE inequality against .sprite, never a null test.
            Sprite? drawn = image != null ? image.overrideSprite : null;
            Sprite? plain = image != null ? image.sprite : null;
            bool effective;
            try { effective = button.IsInteractable(); }
            catch (System.Exception) { effective = interactable; }
            bucket.AddPlate(effective, !ReferenceEquals(drawn, plain),
                            button.transition == Selectable.Transition.SpriteSwap,
                            drawn != null ? drawn.name : null,
                            target != null ? target.color : Color.white);
            if (image != null)
            {
                if (population == 3)
                    s_diffClone = image;
                else if (population <= 1 && population < s_diffReferenceRank)
                {
                    s_diffReference = image;
                    s_diffReferenceRank = population;   // a GAME-LIVE plate always beats a pooled one
                }
            }
        }
        if (target != null)
        {
            if (target.canvasRenderer != null)
            {
                Color c = target.canvasRenderer.GetColor();
                // One number for "how grey/dim is it": the rendered luminance times the rendered
                // alpha, i.e. what actually reaches the eye through the Selectable's tint.
                tint = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * c.a;
            }
            Material mat = target.materialForRendering;
            if (mat != null && mat.shader != null)
                shader = mat.shader.name;

            // THE CARD-FX REST STATE, read off the ASSIGNED material (target.material, not
            // materialForRendering — a mask variant is a different object and would not carry a
            // write we made). This is the number the 196 log could not have contained and the one
            // that decides whether ModBuild 197's diagnosis was right: a CLONE reading _GreyOut > 0
            // against a GAME-LIVE 0 IS the photographed desaturation.
            Material assigned = target.material;
            if (assigned != null && IsCardFxMaterial(assigned))
            {
                float other = Mathf.Max(Mathf.Abs(SafeGet(assigned, FlowId)),
                                        Mathf.Max(Mathf.Abs(SafeGet(assigned, DissolveId)),
                                                  Mathf.Abs(SafeGet(assigned, BurnId))));
                // _PosAndBounds is READ and never written. On a clone it is the material asset's
                // own default because CardEffects.Initialize (the only writer, CardEffects.cs:347)
                // never ran — a second, independent divergence worth naming in the log. It is NOT
                // corrected: a reconstructed screen-space rect is precisely the guess that makes a
                // detached world-space card render DEEP BLACK, which is why
                // Net.RemoteCardArt.StripFragileEffects exists. Measure it; do not touch it.
                bucket.AddFx(assigned.GetFloat(GreyOutId), other, assigned.GetVector(PosAndBoundsId));
            }
        }
        bucket.AddHalf(alpha, interactable, enabled, tint, shader);
    }

    // ---------------------------------------------- the brute-force plate diff (once a session) --

    /// <summary>The two plate images the next diff compares: a mod-built one and the best available
    /// game-owned reference. Re-picked on every census, so a diff that has not fired yet always uses
    /// the freshest pair.</summary>
    private static Image? s_diffClone;
    private static Image? s_diffReference;
    private static int s_diffReferenceRank = int.MaxValue;
    private static bool s_diffLogged;

    /// <summary>
    /// EVERY readable property of the two action plates, side by side, printed the first time both
    /// populations are in the scene at once.
    ///
    /// <para>WHY BRUTE FORCE. Three hardware rounds each shipped ONE hypothesis (the half
    /// CanvasGroup's alpha; then the material's <c>_GreyOut</c>), each measured its own term, each
    /// found it already correct, and each cost a full test round to learn only that. A diff of the
    /// WHOLE property distribution cannot miss the term the way a hypothesis can: it enumerates the
    /// shader's own property table (<c>Shader.GetPropertyCount/GetPropertyName/GetPropertyType</c>),
    /// the material keywords, the sprite identities, the vertex colour, the CanvasRenderer state and
    /// the canvas the plate draws on, prints only what DIFFERS, and counts what agrees so an empty
    /// diff can never be confused with a diff that never ran.</para>
    ///
    /// <para>One shot per session: it is a wide reflection walk, and a difference this stable does
    /// not need a cadence.</para>
    /// </summary>
    private static void MaybeDumpPlateDiff()
    {
        if (s_diffLogged || s_diffClone == null || s_diffReference == null)
            return;
        s_diffLogged = true;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int same = 0;
            sb.Append("CARD PLATE DIFF: every readable property of the action plate a MOD-BUILT front " +
                      "draws with, against the same plate on a ")
              .Append(s_diffReferenceRank == 0 ? "GAME-LIVE (the overlay's own)" : "GAME-POOL (parked)")
              .Append(" face. Only DIFFERENCES are listed; agreements are counted. — ");
            int before = sb.Length;

            Image a = s_diffClone, b = s_diffReference;
            Add(sb, ref same, "sprite", Name(a.sprite), Name(b.sprite));
            Add(sb, ref same, "DRAWN sprite (overrideSprite)", Name(a.overrideSprite), Name(b.overrideSprite));
            Add(sb, ref same, "swap live", (!ReferenceEquals(a.overrideSprite, a.sprite)).ToString(),
                                           (!ReferenceEquals(b.overrideSprite, b.sprite)).ToString());
            Add(sb, ref same, "sprite texture", TexName(a.sprite), TexName(b.sprite));
            Add(sb, ref same, "Graphic.color", Col(a.color), Col(b.color));
            Add(sb, ref same, "Graphic.enabled", a.enabled.ToString(), b.enabled.ToString());
            Add(sb, ref same, "Image.type", a.type.ToString(), b.type.ToString());
            Add(sb, ref same, "CanvasRenderer.color", a.canvasRenderer != null ? Col(a.canvasRenderer.GetColor()) : "n/a",
                                                      b.canvasRenderer != null ? Col(b.canvasRenderer.GetColor()) : "n/a");
            Add(sb, ref same, "CanvasRenderer.inheritedAlpha",
                a.canvasRenderer != null ? a.canvasRenderer.GetInheritedAlpha().ToString("F3") : "n/a",
                b.canvasRenderer != null ? b.canvasRenderer.GetInheritedAlpha().ToString("F3") : "n/a");
            AddSelectable(sb, ref same, a, b);
            AddCanvas(sb, ref same, a, b);
            AddMaterials(sb, ref same, a, b);

            if (sb.Length == before)
                sb.Append("NOTHING DIFFERS at all");
            sb.Append(". ").Append(same).Append(" further properties are IDENTICAL on both plates. If ")
              .Append("the fan still reads grey against an empty diff, the cause is NOT on the plate ")
              .Append("Graphic — look one level out (the canvas, the camera, a parent tint).");
            VRLog.Info(Scope, sb.ToString());
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("plate diff", ex);
        }
    }

    private static void Add(System.Text.StringBuilder sb, ref int same, string label, string a, string b)
    {
        if (a == b)
        {
            same++;
            return;
        }
        sb.Append(label).Append(": CLONE ").Append(a).Append(" vs REF ").Append(b).Append("; ");
    }

    private static string Name(Object? o) => o != null ? "'" + o.name + "'" : "(null)";

    private static string TexName(Sprite? s) =>
        s != null && s.texture != null ? $"'{s.texture.name}' {s.texture.width}x{s.texture.height}" : "(none)";

    private static string Col(Color c) => $"({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";

    /// <summary>The Selectable state that PICKS the sprite — including the conjunction
    /// <c>IsInteractable()</c> and each of its two terms separately, so a future reading can never
    /// again mistake the flag for the gate.</summary>
    private static void AddSelectable(System.Text.StringBuilder sb, ref int same, Image a, Image b)
    {
        Selectable? sa = a.GetComponent<Selectable>();
        Selectable? sb2 = b.GetComponent<Selectable>();
        Add(sb, ref same, "Selectable.IsInteractable()",
            sa != null ? sa.IsInteractable().ToString() : "n/a",
            sb2 != null ? sb2.IsInteractable().ToString() : "n/a");
        Add(sb, ref same, "Selectable.interactable(flag)",
            sa != null ? sa.interactable.ToString() : "n/a",
            sb2 != null ? sb2.interactable.ToString() : "n/a");
        Add(sb, ref same, "Selectable.transition",
            sa != null ? sa.transition.ToString() : "n/a",
            sb2 != null ? sb2.transition.ToString() : "n/a");
        Add(sb, ref same, "Selectable.enabled",
            sa != null ? sa.enabled.ToString() : "n/a",
            sb2 != null ? sb2.enabled.ToString() : "n/a");
        Add(sb, ref same, "spriteState.disabledSprite",
            sa != null ? Name(sa.spriteState.disabledSprite) : "n/a",
            sb2 != null ? Name(sb2.spriteState.disabledSprite) : "n/a");
        Add(sb, ref same, "nearest CanvasGroup(interactable/alpha)", GroupOf(a), GroupOf(b));
    }

    private static string GroupOf(Graphic g)
    {
        CanvasGroup? group = g.GetComponentInParent<CanvasGroup>(includeInactive: true);
        return group != null
            ? $"'{group.name}' interactable={group.interactable} alpha={group.alpha:F2}"
            : "(none)";
    }

    /// <summary>The canvas the plate draws on. A world-space canvas the mod created carries Unity's
    /// DEFAULT <c>additionalShaderChannels</c>, and a UI-effect shader that reads a channel the canvas
    /// does not feed gets zeroes — a divergence no material property would ever show.</summary>
    private static void AddCanvas(System.Text.StringBuilder sb, ref int same, Image a, Image b)
    {
        Canvas? ca = a.canvas;
        Canvas? cb = b.canvas;
        Add(sb, ref same, "canvas.renderMode",
            ca != null ? ca.renderMode.ToString() : "n/a", cb != null ? cb.renderMode.ToString() : "n/a");
        Add(sb, ref same, "canvas.additionalShaderChannels",
            ca != null ? ca.additionalShaderChannels.ToString() : "n/a",
            cb != null ? cb.additionalShaderChannels.ToString() : "n/a");
        Add(sb, ref same, "canvas.sortingLayer/order",
            ca != null ? $"{ca.sortingLayerName}/{ca.sortingOrder}" : "n/a",
            cb != null ? $"{cb.sortingLayerName}/{cb.sortingOrder}" : "n/a");
    }

    /// <summary>Both plates' materials, property by property, off the shader's OWN property table —
    /// the part that cannot miss a term because it never names one.</summary>
    private static void AddMaterials(System.Text.StringBuilder sb, ref int same, Image a, Image b)
    {
        Material? ma = a.materialForRendering;
        Material? mb = b.materialForRendering;
        Add(sb, ref same, "material", Name(ma), Name(mb));
        Add(sb, ref same, "material shader",
            ma != null && ma.shader != null ? "'" + ma.shader.name + "'" : "(null)",
            mb != null && mb.shader != null ? "'" + mb.shader.name + "'" : "(null)");
        Add(sb, ref same, "material keywords",
            ma != null ? string.Join("|", ma.shaderKeywords) : "n/a",
            mb != null ? string.Join("|", mb.shaderKeywords) : "n/a");
        if (ma == null || mb == null || ma.shader == null || mb.shader == null
            || ma.shader != mb.shader)
            return;   // different shaders: the line above already says so, and the tables would not align

        Shader shader = ma.shader;
        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            string name = shader.GetPropertyName(i);
            int id = shader.GetPropertyNameId(i);
            switch (shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    Add(sb, ref same, name, Col(ma.GetColor(id)), Col(mb.GetColor(id)));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    Add(sb, ref same, name, ma.GetVector(id).ToString("F2"), mb.GetVector(id).ToString("F2"));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    Add(sb, ref same, name, ma.GetFloat(id).ToString("F3"), mb.GetFloat(id).ToString("F3"));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    Add(sb, ref same, name,
                        Name(ma.GetTexture(id)) + ma.GetTextureScale(id).ToString("F2"),
                        Name(mb.GetTexture(id)) + mb.GetTextureScale(id).ToString("F2"));
                    break;
                default:
                    same++;
                    break;
            }
        }
    }

    private static void LogErrorOnce(string what, System.Exception ex)
    {
        if (s_errorLogged)
            return;
        s_errorLogged = true;
        VRLog.Warn(Scope, $"CARD HALF TONE {what} failed ({ex.GetType().Name}: {ex.Message}) — card " +
                          "fronts keep the game's own half state.");
    }
}
