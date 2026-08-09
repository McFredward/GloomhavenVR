using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Discard/burnt/items pile stacks on the control board (hardware test #21, [Cards]
/// PileViewer): THREE small physical card piles docked off the board's RIGHT edge
/// (<see cref="PlayTray.PileMount"/> — the only free edge, see the BuildMounts
/// collision math), each rendered as a stack of card slabs with a live count and a
/// localized caption. Poking a stack (finger or board laser) TOGGLES the pile
/// browse fan (<see cref="PileBrowser"/>; open/close policy and content live in
/// CardsDriver) — poke/laser is the ONLY way a stack opens: no stack is grabbable
/// any more (user report 2026-08-06, see <see cref="PileStack.CanGrab"/>), so a bare
/// trigger press near a pile can never pick it up. Purely informational — counts are
/// read straight from the
/// authoritative piles (<see cref="CardsGameApi.DiscardedCount"/> /
/// <see cref="CardsGameApi.BurntCount"/>, plus the inventory for the items stack — no
/// game state is ever written) and logged change-deduped. The items stack (item 4) is
/// the odd one out: its browse is the <see cref="ItemsPile"/> fan, not a
/// <see cref="PileBrowser"/>, and its caption is a MOD string, not a game loc key.
/// </summary>
internal sealed class PileViewer
{
    private PileStack? _discard;
    private PileStack? _burnt;
    private PileStack? _items;
    private bool _locHooked;
    private (int discard, int burnt, int actorId) _loggedCounts = (int.MinValue, int.MinValue, 0);
    private int _loggedItems = int.MinValue;

    // Usable-item highlight diagnostic (throttled + change-gated) — see TickItemsUsableHighlight.
    private float _nextUsableLogAt;
    private int _loggedUsable = int.MinValue;
    private bool _loggedStackCue;

    /// <summary>
    /// The character-items pile (item 4): a self-contained THIRD stack below the burnt
    /// pile, browsed like the others but rendered from <c>Inventory.AllItems</c> instead
    /// of ability-card widgets (see <see cref="ItemsPile"/>). Its poke/grab is routed here
    /// (not through <see cref="PokeToggled"/>/<see cref="GrabOpened"/>) so it opens its own
    /// item browse without any CardsDriver wiring.
    /// </summary>
    private readonly ItemsPile _itemsBrowse = new();

    /// <summary>The active hand last seen in <see cref="TickStatus"/> — the items pile's inventory source.</summary>
    private CardsHandUI? _hand;

    /// <summary>Stack poked (finger/laser) — CardsDriver toggles the browse fan. The
    /// <c>GrabOpened</c>/<c>GrabReleased</c> siblings that used to sit here died with the
    /// stack pinch-grab (user report 2026-08-06, see <see cref="PileStack.CanGrab"/>): the
    /// poke/laser toggle is now the only stack interaction there is.</summary>
    internal System.Action<PileKind, VRHand>? PokeToggled;

    /// <summary>The items browse is being opened — CardsDriver closes the discard/burnt ability browser so
    /// only ONE pile fan is ever up (the discard/burnt→items direction already closes the items fan; this
    /// is the missing items→discard/burnt direction).</summary>
    internal System.Action? ItemsOpening;

    internal bool IsBuilt => _discard != null;

    /// <summary>Requirement 4: is the item fan currently open? CardsDriver's foreign-interaction /
    /// click-away path polls this to dismiss the item fan on the SAME seams that close the
    /// discard/burnt ability browser (board button, card grab, rest, action play, click-away).</summary>
    internal bool ItemsBrowseOpen => _itemsBrowse.IsOpen;

    /// <summary>
    /// NEVER-STUCK GUARANTEE (user report 2026-08-07: "es darf NIE einen Zustand geben, in dem
    /// sich der Fächer nicht mehr schliessen lässt"). A stack refuses its poke/laser toggle while
    /// it holds 0 cards (<see cref="PileStack.OnPoke"/>) — which, on its own, means a fan that is
    /// ALREADY open when its pile empties (last item consumed, last card recovered) has no toggle
    /// left to close it. This tells the stack "your own browse is up right now", so the CLOSING
    /// half of the toggle always survives the empty gate. Discard/burnt resolve through the
    /// browser's static <see cref="PileBrowser.Current"/> for the same reason
    /// <see cref="CurrentCounts"/> is static: the browser instance is a private of CardsDriver.
    /// </summary>
    internal bool BrowseOpenFor(PileKind kind)
    {
        if (kind == PileKind.Items)
            return _itemsBrowse.IsOpen;
        PileBrowser? browser = PileBrowser.Current;
        return browser != null && browser.IsOpen && browser.Kind == kind;
    }

    /// <summary>Laser pick over the OPEN item fan (geometric, sticky — the item twin of
    /// <see cref="PileBrowser.TryRaycast"/>; the full every-second-card root cause lives on
    /// <see cref="ItemsPile.TryLaserRaycast"/>). Forwarded so CardsDriver's laser chain never
    /// reaches into the privately-owned fan instance. False while the fan is closed.</summary>
    internal bool TryRaycastItemChips(Vector3 origin, Vector3 direction, ItemsPile.ItemChip? sticky,
        out ItemsPile.ItemChip? chip, out Vector3 point, out float distance,
        bool allowNearMiss = false) =>
        _itemsBrowse.TryLaserRaycast(origin, direction, sticky, out chip, out point, out distance,
            allowNearMiss);

    /// <summary>What the last item-fan laser pick decided (hit/miss, which chip, exact or rescued
    /// by the angular pad) — forwarded for CardsDriver's throttled laser diagnostic.</summary>
    internal FanSweep.FanLaserPick LastItemLaserPick => _itemsBrowse.LastLaserPick;

    /// <summary>The item chip <paramref name="hand"/> is physically in contact with (hand-sweep
    /// winner elected by that hand, else its proximity-grab candidate), or null. Forwarded so the
    /// laser chain can honour the single-owner contract — see
    /// <see cref="ItemsPile.HandOwnedChip"/> for WHY.</summary>
    internal ItemsPile.ItemChip? HandOwnedItemChip(VRHand? hand) => _itemsBrowse.HandOwnedChip(hand);

    /// <summary>True while an item card LIES IN the board's use recess with the item fan CLOSED
    /// (<see cref="ItemsPile.HasPlacedCardWhileClosed"/>). The interaction drivers gate on the fan
    /// being open; that card is reachable without one, so they need this second question.</summary>
    internal bool ItemCardInRecess => _itemsBrowse.HasPlacedCardWhileClosed;

    /// <summary>Requirement 4: dismiss the item fan on a foreign interaction (the item counterpart of
    /// <c>CardsDriver.CloseBrowser</c>). The item→ability mutual-exclusion is separate (<see cref="ItemsOpening"/>);
    /// this is the general click-away close for the item fan itself.</summary>
    internal void CloseItemsBrowse(string reason = "foreign interaction") => _itemsBrowse.Close(reason);

    /// <summary>Item-surrender pick (event consume/refresh mali): per-frame pump, driven by the
    /// CardsDriver INDEPENDENTLY of the stack visibility gate in <see cref="TickStatus"/> —
    /// the demand can arrive at scenario start before any pile UI has ever shown.</summary>
    internal void TickItemDemand(CardsHandUI? hand)
    {
        _itemsBrowse.TickDemandPick(hand);
        _itemsBrowse.TickTakeDamagePick(hand); // req C: take-damage shield place (no-op outside the decision)
        // Every item is played by PLACING its card (user ruling 2026-08-08) — so no item symbol may
        // be clickable on the docked items bar unless its card is already in the board's item slot.
        // Driven from HERE, not from ItemsPile.Tick, because the bar exists (and would dock) whether
        // or not the item FAN is open, and ItemsPile.Tick early-returns on a closed fan.
        _itemsBrowse.TickItemSymbolSplit();
    }

    /// <summary>
    /// Issue 5 (fly-to-pile): world placement of one pile stack, for animating a just-cleared
    /// played card INTO its destination stack. <paramref name="worldPos"/> is the stack centre;
    /// <paramref name="slabWorldWidth"/> is the on-screen width of a slab in the stack
    /// (<see cref="PileStack.SlabFactor"/> × the card width, at the stack's live world scale) —
    /// the fly shrinks the card toward this so it reads as slotting into the pile. Returns false
    /// when the requested stack isn't built or is hidden (piles off / no hand): the caller then
    /// falls back to the instant hide.
    /// </summary>
    internal bool TryGetPileWorld(PileKind kind, out Vector3 worldPos, out float slabWorldWidth)
    {
        worldPos = Vector3.zero;
        slabWorldWidth = 0f;
        PileStack? stack = kind switch
        {
            PileKind.Discard => _discard,
            PileKind.Burnt => _burnt,
            PileKind.Items => _items,
            _ => _discard,
        };
        if (stack == null || !stack.gameObject.activeInHierarchy)
            return false;
        worldPos = stack.transform.position;
        slabWorldWidth = stack.transform.lossyScale.x * CardsConfig.CardWidth.Value * PileStack.SlabFactor;
        return true;
    }

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>Local caption for one pile: real GAME loc keys for discard/burnt (safe English
    /// fallbacks), and a MOD string for items — the game has no section header for the
    /// inventory.</summary>
    // Pile captions use the game's OWN card-overview SECTION-HEADER keys — proper pile
    // NOUNS in every shipped language. The old GUI_TAKE_DAMAGE_* keys were action verb
    // phrases ("Burn 1 Available Card" / "1 verfügbare Karte verbrennen"), so the caption
    // read the truncated "Verfügbare Karte Ver"; the DISCARD one was likewise a verb
    // ("Burn 2 Discarded Cards"). GUI_CARD_SECTION_DISCARDED = Discarded/Abgeworfen and
    // GUI_CARD_SECTION_BURNT = Burned/Verbrannt are the labels the game's card sections use.
    internal static string Caption(PileKind kind) => kind switch
    {
        PileKind.Discard => Core.Loc.Game("GUI_CARD_SECTION_DISCARDED", "Discarded"),
        PileKind.Burnt => Core.Loc.Game("GUI_CARD_SECTION_BURNT", "Burned"),
        PileKind.Items => Core.Loc.Mod("items"),
        _ => Core.Loc.Mod("items"),
    };

    internal void EnsureBuilt(PlayTray tray)
    {
        Transform? mount = tray.PileMount;
        if (mount == null)
            return;
        // Round-2: the inter-stack gap is PER-BOARD (debug-menu tunable), seeded 0.116 (Oak).
        float spacing = CardsConfig.PileSpacing(CardsConfig.CurrentBoard).Value;
        // A tray teardown destroys the stacks with the mount — the Unity fake-null
        // makes the == checks below true and the stacks rebuild from scratch.
        if (_discard == null)
        {
            _discard = PileStack.Create(mount, PileKind.Discard,
                new Color(0.55f, 0.48f, 0.34f), Caption(PileKind.Discard), this,
                new Vector3(PlayTray.PileStackOffsetX, spacing * 0.5f, 0f));
            tray.RegisterLaserTarget(_discard.GetComponent<Collider>(), _discard);
        }
        if (_burnt == null)
        {
            _burnt = PileStack.Create(mount, PileKind.Burnt,
                new Color(0.45f, 0.22f, 0.16f), Caption(PileKind.Burnt), this,
                new Vector3(PlayTray.PileStackOffsetX, -spacing * 0.5f, 0f));
            tray.RegisterLaserTarget(_burnt.GetComponent<Collider>(), _burnt);
        }
        // Item 4: the character-items stack, mounted BELOW the burnt pile (a further
        // −spacing down). Same physical stack + poke/grab, but its browse is the item
        // pile (routed to _itemsBrowse in DispatchPoke/DispatchGrab).
        if (_items == null)
        {
            _items = PileStack.Create(mount, PileKind.Items,
                new Color(0.30f, 0.42f, 0.26f), Caption(PileKind.Items), this,
                new Vector3(PlayTray.PileStackOffsetX, -spacing * 1.5f, 0f));
            tray.RegisterLaserTarget(_items.GetComponent<Collider>(), _items);
        }
        // Requirement 3: the item fan must emerge from AND collapse into ITS OWN stack — hand the
        // ITEMS stack transform (not the shared PileMount origin, which sits up by the DISCARD stack)
        // as the converge anchor, so ItemsPile.PileConvergeWorld reads the real items-pile position.
        _itemsBrowse.SetAnchor(_items != null ? _items.transform : mount);
        ApplyLayout(); // seat the per-board scale + spacing (all three stacks)

        // Live language following: the pile captions are built once — re-read them on a
        // language change (subscribe once; Destroy detaches).
        if (!_locHooked)
        {
            _locHooked = true;
            Core.Loc.OnChanged += RefreshLabels;
            // One-time: surface the resolved (active-language) pile captions so the next
            // hardware log confirms the German/EN values from the game's section keys.
            VRLog.Info("Cards", $"Pile captions: discard=\"{Caption(PileKind.Discard)}\", " +
                                $"burnt=\"{Caption(PileKind.Burnt)}\" (GUI_CARD_SECTION_* section nouns).");
        }
    }

    /// <summary>Re-read all three pile captions in the current language (live-follow, Loc.OnChanged).</summary>
    internal void RefreshLabels()
    {
        _discard?.SetCaption(Caption(PileKind.Discard));
        _burnt?.SetCaption(Caption(PileKind.Burnt));
        _items?.SetCaption(Caption(PileKind.Items));
    }

    /// <summary>
    /// Round-2 live-apply: re-seat all three pile stacks from the active board's per-board SCALE
    /// and inter-stack SPACING — discard upper at +spacing/2, burn at −spacing/2, items lowest at
    /// −spacing·1.5 (the items stack was added by item 4 and hangs below the original pair). Called from
    /// <see cref="EnsureBuilt"/> and by CardsDriver when the debug menu / cfg edits either.
    /// </summary>
    internal void ApplyLayout()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        float scale = CardsConfig.PileScale(b).Value;
        float spacing = CardsConfig.PileSpacing(b).Value;
        if (_discard != null)
        {
            _discard.transform.localScale = Vector3.one * scale;
            _discard.transform.localPosition = new Vector3(PlayTray.PileStackOffsetX, spacing * 0.5f, 0f);
        }
        if (_burnt != null)
        {
            _burnt.transform.localScale = Vector3.one * scale;
            _burnt.transform.localPosition = new Vector3(PlayTray.PileStackOffsetX, -spacing * 0.5f, 0f);
        }
        if (_items != null)
        {
            _items.transform.localScale = Vector3.one * scale;
            _items.transform.localPosition = new Vector3(PlayTray.PileStackOffsetX, -spacing * 1.5f, 0f);
        }
    }

    internal void SetVisible(bool visible)
    {
        if (_discard != null && _discard.gameObject.activeSelf != visible)
            _discard.gameObject.SetActive(visible);
        if (_burnt != null && _burnt.gameObject.activeSelf != visible)
            _burnt.gameObject.SetActive(visible);
        if (_items != null && _items.gameObject.activeSelf != visible)
            _items.gameObject.SetActive(visible);
        if (!visible)
        {
            // keepPlacedCard:false — the board furniture is going away, so there is nothing for the
            // placed card to lie on and nothing ticking to resolve its decision.
            _itemsBrowse.Close("pile stacks hidden (piles off / no hand)", keepPlacedCard: false);
            _itemsBrowse.RetirePlacedCardIfAny("the pile stacks were hidden");
        }
    }

    internal void Destroy()
    {
        CurrentCounts = null; // a torn-down viewer displays nothing — the wire must not claim it does
        if (_locHooked)
        {
            Core.Loc.OnChanged -= RefreshLabels;
            _locHooked = false;
        }
        _itemsBrowse.Destroy();
        if (_discard != null)
            Object.DestroyImmediate(_discard.gameObject);
        if (_burnt != null)
            Object.DestroyImmediate(_burnt.gameObject);
        if (_items != null)
            Object.DestroyImmediate(_items.gameObject);
        _discard = null;
        _burnt = null;
        _items = null;
        _hand = null;
        _loggedCounts = (int.MinValue, int.MinValue, 0);
        _loggedItems = int.MinValue;
        _loggedUsable = int.MinValue;
        _loggedStackCue = false;
    }

    // ------------------------------------------------------------------ status --

    /// <summary>
    /// Refresh counts/dimming from the authoritative piles (per frame while the tray shows).
    /// The Info line is change-deduped: one log per actual pile change (test #21 C), never
    /// per frame.
    ///
    /// NOT cheap — this doc used to advertise "two list Counts", which is an order of magnitude
    /// off and is exactly the claim a perf pass would trust. Per frame it does three counts PLUS
    /// <see cref="ItemsPile.Tick"/> (the whole item-fan tick, including its hand sweep) and
    /// <see cref="TickItemsUsableHighlight"/> (which scans the inventory).
    /// </summary>
    internal void TickStatus(CardsHandUI? hand, CardsHandUI? presented)
    {
        _hand = hand;
        // THE COUNTS BELONG TO THE CHARACTER ON THE BOARD, not to the one the game presents (user
        // report, hardware ModBuild 89 — "Wird der Character gewechselt sollen immer sofort die
        // jeweiligen richtigen Zahlen des Characters angezeigt werden", and its twin "beim
        // verbrannt Stapel wurde nicht sofort aktualisiert"). Both symptoms were ONE defect: this
        // method was fed CardsDriver.CurrentHand(), the hand the GAME presents, while every other
        // board surface renders CharacterFocus.ResolveHand(CurrentHand()). So a focus switch moved
        // the fan, the slots, the active column and the piles' CONTENT and left the two NUMBERS on
        // the previous character — and a card the focused character burned never reached the burnt
        // label at all, which is exactly the "discard went to 1, burnt stayed 0" in the hardware
        // log (LogOutput.log:4513). `presented` is the focus-resolved hand and is DISPLAY ONLY;
        // `hand` (the game's) still drives every item path below, because those reach real game
        // seams (item use / the surrender pick) and must never name a merely-watched character.
        CardsHandUI? counted = presented != null ? presented : hand;
        if (_discard == null || _burnt == null || hand == null || counted == null)
        {
            CurrentCounts = null; // nothing displayed ⇒ nothing for the wire to claim
            // A card lying in the item-use recess is serviced from _itemsBrowse.Tick below, which
            // this return skips — so it must not be left there unserviced (it would be frozen on a
            // board nobody is ticking, with a decision nobody can resolve).
            _itemsBrowse.RetirePlacedCardIfAny("the board is not presenting a hand any more");
            return;
        }
        if (!_discard.gameObject.activeSelf)
        {
            CurrentCounts = null;
            _itemsBrowse.RetirePlacedCardIfAny("the pile stacks are hidden");
            return; // hidden ([Cards] PileViewer off / no hand) — no counts, no logs
        }
        // THE NUMBER BECOMES TRUE WHEN THE CARD LANDS, NOT WHEN THE MODEL MOVES IT (user report:
        // "Wenn man gerade eine Karte abgeworfen oder verbrannt hat, sie aber noch auf dem
        // Controllboard liegt, wird aber schon der Pile aktualisiert. So kann es sein, dass zwar im
        // Pile '1' steht, wenn man ihn aber öffnen will nichts angezeigt wird … So wird es nie
        // einen '0er-Fächer' geben.").
        //
        // The rules engine puts the card in its pile list the instant the action resolves, while on
        // the VR table the same card is still lying in a board slot / burning where it lies / arcing
        // over the board. CardsDriver.PendingPileArrivals counts exactly those — the cards the MODEL
        // already lists in this pile whose VISUAL has not arrived — and it is recomputed from live
        // objects every frame, so any way a flight ends (landed, cancelled, card destroyed, board
        // switch, hand switch, scenario teardown, the bounded burn-artwork hold expiring) converges
        // this number back onto the model on the next frame. See the region header at
        // CardsDriver.4.Rebuild.cs "pile ARRIVAL" for the full self-healing argument.
        //
        // IT EXTENDS THE ModBuild-89 FIX RATHER THAN REVERTING IT: the character these counts belong
        // to is still `counted` (the hand the BOARD presents), which is what made the burnt count
        // follow a focus switch at all. This only changes WHEN a card enters the number.
        //
        // Max() is belt only — PendingPileArrivals counts members of the pile's own widget list, so
        // it can never exceed the pile — and it keeps a torn model from ever printing a negative.
        int discard = Mathf.Max(0, CardsGameApi.DiscardedCount(counted)
                                   - CardsDriver.PendingPileArrivals(counted, PileKind.Discard));
        int burnt = Mathf.Max(0, CardsGameApi.BurntCount(counted)
                                 - CardsDriver.PendingPileArrivals(counted, PileKind.Burnt));
        // The character is part of the change key: switching to a character whose piles happen to
        // hold the SAME two numbers is still a state change worth one line, and without the id the
        // log would go silent across exactly the switch a "die Zahlen stimmen nicht" report needs.
        int countedId = Net.NetFigures.StableActorId(counted.PlayerActor);
        if (_loggedCounts != (discard, burnt, countedId))
        {
            _loggedCounts = (discard, burnt, countedId);
            int modelDiscard = CardsGameApi.DiscardedCount(counted);
            int modelBurnt = CardsGameApi.BurntCount(counted);
            VRLog.Info("Cards", $"Piles: discard={discard}, burnt={burnt} for " +
                                $"'{Board.CharacterFocus.Describe(counted.PlayerActor)}' " +
                                "(authoritative CCharacterClass piles, read against the character " +
                                "the BOARD presents — CharacterFocus.PresentedHand, so the numbers " +
                                "follow a focus switch on the same edge the rest of the board does)" +
                                (modelDiscard != discard || modelBurnt != burnt
                                    ? $". DEFERRED: the model already lists discard={modelDiscard}, " +
                                      $"burnt={modelBurnt}, but {modelDiscard - discard} discard / " +
                                      $"{modelBurnt - burnt} burnt card(s) are still ON THEIR WAY " +
                                      "(lying on the board, held by their burn artwork, or in " +
                                      "flight). The label becomes true when they LAND — so the fan " +
                                      "can never open emptier than the number claims."
                                    : "."));
        }
        _discard.SetCount(discard);
        _burnt.SetCount(burnt);

        // Item 4: the character-items stack count + its browse follow/refresh.
        int items = _itemsBrowse.Count(hand);
        if (_items != null)
            _items.SetCount(items);
        if (_loggedItems != items)
        {
            _loggedItems = items;
            VRLog.Info("Cards", $"Piles: items={items} (Inventory.AllItems).");
        }
        _itemsBrowse.Tick(hand);
        TickItemsUsableHighlight(hand, items);

        // MULTIPLAYER seam (extras extension record 15): the numbers this board is DISPLAYING
        // right now, published for NetAvatarDriver's extras sender. Deliberately the RENDERED
        // values and not a second model read — "was der User auch sieht" is the standing MP rule,
        // and these three ints are, by construction, exactly what the owner's three stack labels
        // show this frame. Null while the stacks are hidden / no hand is presented, which the
        // sender turns into "record absent" ⇒ receivers fall back to their own model read.
        CurrentCounts = (discard, burnt, items);
    }

    /// <summary>
    /// The pile counts the LOCAL board's three stacks are displaying right now
    /// (discard / burnt / items), or null while none are shown. THE sender-side source of extras
    /// extension record 15 — see <see cref="TickStatus"/> for why it publishes the rendered
    /// values rather than re-deriving them. Static for the same reason <c>PileBrowser.Current</c>
    /// is: the viewer instance is a private of CardsDriver and the Net layer must not thread
    /// through it.
    /// </summary>
    internal static (int discard, int burnt, int items)? CurrentCounts { get; private set; }

    /// <summary>
    /// USABLE-HIGHLIGHT on the CLOSED items stack: drift soft gold embers off the "Gegenstände" stack
    /// whenever AT LEAST ONE equipped item can be used right now, so the player sees there is something
    /// to play WITHOUT having to open the fan — and the emission stops again the moment nothing is
    /// usable (turn ends, the last usable item is spent/consumed).
    ///
    /// WHY the count comes from <see cref="ItemsPile.UsableCount"/> and not from the fan's chips: while
    /// the fan is closed there ARE no chips — the chips are built on open and destroyed on close. The
    /// count is therefore read live from the inventory through the exact same activatability predicate
    /// the chips and <c>UseItemService</c> use, so the stack can never advertise a use the game would
    /// reject, and the stack cue and the per-card frames can never disagree.
    ///
    /// <para>Since 2026-08-09 that predicate has a SECOND arm, and it is why the cue can now light up
    /// off-turn: an item whose ACTIVE BONUS is being offered (the "Brille" asking, per attack or per
    /// incoming hit, whether to spend itself) is playable by placing its card, and the game offers
    /// that question in windows that are not the owner's action turn. The user asked for exactly
    /// this cue — "dann soll die Brille im Gegenstands-Pile gehighlighted werden".</para>
    ///
    /// Runs every frame the tray shows (a turn check + a pass over a handful of items), so it tracks
    /// turn/phase changes live. Purely local visual — nothing here touches game state or the network.
    /// </summary>
    private void TickItemsUsableHighlight(CardsHandUI? hand, int itemCount)
    {
        int usable = _itemsBrowse.UsableCount(hand);
        bool on = usable > 0;
        _items?.SetUsableHighlight(on);

        // Throttled + change-gated diagnostic so the next hardware log can verify the cue end-to-end:
        // how many items are usable this instant, and whether the stack's ember drift is actually running.
        if (Time.unscaledTime < _nextUsableLogAt)
            return;
        _nextUsableLogAt = Time.unscaledTime + 2f;
        if (usable == _loggedUsable && on == _loggedStackCue)
            return;
        _loggedUsable = usable;
        _loggedStackCue = on;
        VRLog.Info("Cards", $"ITEM highlight: {usable}/{itemCount} item(s) usable now — " +
                            $"stack embers {(on ? "ON" : "off")}, fan {(_itemsBrowse.IsOpen ? "open" : "closed")} " +
                            "(usable cards wear the soft gold frame; nothing is dimmed).");
    }

    // ------------------------------------------------------------------ dispatch --

    /// <summary>
    /// Route a stack poke: the ITEMS stack opens its own item browse (self-contained,
    /// needs no CardsDriver wiring); discard/burnt raise <see cref="PokeToggled"/> for
    /// CardsDriver to open the ability-card browse as before. Poking discard/burnt also
    /// dismisses any open item browse so only one pile fan is up at a time.
    /// </summary>
    internal void DispatchPoke(PileKind kind, VRHand hand)
    {
        if (kind == PileKind.Items)
        {
            // CLOSE first, unconditionally: an open fan must be closable even if the presented
            // hand went away under it (_hand null) — that used to swallow the toggle and leave a
            // fan nothing could dismiss (never-stuck guarantee).
            if (_itemsBrowse.IsOpen)
            {
                _itemsBrowse.Close($"user toggle (items stack poke/laser, {hand.Side})");
                return;
            }
            if (_hand != null)
            {
                ItemsOpening?.Invoke(); // close the ability browser first — one pile fan at a time
                _itemsBrowse.TogglePoke(_hand, hand);
            }
            return;
        }
        _itemsBrowse.Close($"discard/burnt stack poked ({kind}) — one pile fan at a time");
        PokeToggled?.Invoke(kind, hand);
    }

    // NOTE: the DispatchGrabOpen/DispatchGrabRelease pair that used to live here (the
    // pinch-to-browse-while-held route for discard/burnt) is GONE with the stack grab itself —
    // see PileStack.CanGrab for the full ruling chain. DispatchPoke above is the only route left.

    // ------------------------------------------------------------------ stack --

    /// <summary>
    /// One physical pile stack: a few offset card slabs + count + caption. Pokeable
    /// ONLY (finger via <see cref="OnPoke"/>, board laser via <see cref="LaserToggle"/> —
    /// CardsDriver routes laser clicks there so the finger's edge gate never eats a
    /// deliberate second laser click). It is deliberately NOT grabbable — the class still
    /// derives from GrabbableBehaviour (VRCard's dual-registration pattern), but
    /// <see cref="CanGrab"/> refuses every kind, so the ProximityGrabber never candidates
    /// a stack and the trigger can never pick a pile up (see CanGrab for the ruling
    /// chain). Internal (not private) so CardsDriver's laser dispatch can type-test it.
    /// </summary>
    internal sealed class PileStack : GrabbableBehaviour, IPokeable
    {
        private PileViewer _owner = null!;
        private PileKind _kind;
        private TextMeshPro? _count;
        private TextMeshPro? _captionTmp;
        private Material? _topMaterial;
        private Color _baseColor;
        private int _shown = int.MinValue;
        private bool _hasCards;

        // USABLE-HIGHLIGHT (items stack only, built lazily on first use): a slow drift of soft gold
        // embers rising off the stack while at least one equipped item can be played right now. Built
        // lazily because only the ITEMS stack ever asks for it — the discard/burnt stacks must not pay
        // for a particle system they never show.
        private ParticleSystem? _usableEmbers;
        private bool _usableCueOn;

        // Slab footprint: 0.62× card size — reads as a mini pile without crowding
        // the 0.10 m column budget (PlayTray.BuildMounts collision math).
        internal const float SlabFactor = 0.62f;

        internal static PileStack Create(Transform mount, PileKind kind, Color color,
            string caption, PileViewer owner, Vector3 localPos)
        {
            float w = CardsConfig.CardWidth.Value * SlabFactor;
            float h = CardsConfig.CardHeight * SlabFactor;

            var go = new GameObject($"PileStack_{kind}");
            go.transform.SetParent(mount, worldPositionStays: false);
            go.transform.localPosition = localPos;

            // Stack body: 4 thin slabs, each a step behind the previous (+Z is into
            // the board) with a small alternating jitter so it reads as a real pile.
            Material? topMaterial = null;
            for (int i = 0; i < 4; i++)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Slab{i}";
                Object.Destroy(slab.GetComponent<Collider>());
                slab.transform.SetParent(go.transform, worldPositionStays: false);
                slab.transform.localScale = new Vector3(w, h, 0.0018f);
                float jitter = (i % 2 == 0 ? 1f : -1f) * 0.0015f;
                slab.transform.localPosition = new Vector3(jitter, -jitter, 0.0022f * (3 - i));
                slab.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * 2.5f);
                bool top = i == 3;
                var renderer = slab.GetComponent<MeshRenderer>();
                Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var material = new Material(shader)
                    {
                        color = top ? color : Color.Lerp(color, Color.black, 0.45f),
                    };
                    renderer.sharedMaterial = material;
                    if (top)
                        topMaterial = material;
                }
            }

            // Count on the top slab (change-gated writes — the badge flicker lesson).
            var countGo = new GameObject("Count");
            countGo.transform.SetParent(go.transform, worldPositionStays: false);
            countGo.transform.localPosition = new Vector3(0f, 0f, -0.0025f); // viewer side (-Z)
            var count = countGo.AddComponent<TextMeshPro>();
            count.text = "-";
            count.alignment = TextAlignmentOptions.Center;
            count.color = new Color(1f, 0.95f, 0.8f);
            WorldUI.NativeButtonSkin.ApplyFont(count); // native HUD font (test #25 item 3)
            Core.TmpFit.Fit(count, w * 0.9f, h * 0.62f, maxFontSize: 0.30f, wrap: false);

            // Caption under the stack (localized names shrink to fit, test #12).
            var captionGo = new GameObject("Caption");
            captionGo.transform.SetParent(go.transform, worldPositionStays: false);
            captionGo.transform.localPosition = new Vector3(0f, -h * 0.5f - 0.016f, -0.0025f);
            var captionTmp = captionGo.AddComponent<TextMeshPro>();
            captionTmp.text = caption.ToUpperInvariant();
            captionTmp.alignment = TextAlignmentOptions.Center;
            captionTmp.color = new Color(0.85f, 0.8f, 0.7f);
            WorldUI.NativeButtonSkin.ApplyFont(captionTmp); // native HUD font (test #25 item 3)
            Core.TmpFit.Fit(captionTmp, 0.095f, 0.024f, maxFontSize: 0.22f, wrap: false);
            WorldUI.MrBacking.Label(captionTmp); // below the slabs, off-board → sky/room behind it in MR

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 0.012f, h + 0.012f, 0.022f);
            box.isTrigger = true;

            var stack = go.AddComponent<PileStack>();
            stack.snapToHand = false; // defensive only — CanGrab refuses every kind, the pile never leaves the board
            stack._owner = owner;
            stack._kind = kind;
            stack._count = count;
            stack._captionTmp = captionTmp;
            stack._topMaterial = topMaterial;
            stack._baseColor = color;
            Core.VRLayers.Apply(go); // mod layer (render-only; poke/grab via registries)
            // 2026-08-04 (status-placard defect family): the count/caption TMP labels are
            // depth-less transparent renderers at order 0 — a converted panel BEHIND the board
            // painted over them. Ride the board's furniture order group (the opaque slab cubes
            // are skipped by the queue filter).
            PlayTray.AdoptFurniture(go);
            return stack;
        }

        /// <summary>Re-read the pile caption in the current language (live-follow).</summary>
        internal void SetCaption(string caption)
        {
            if (_captionTmp != null)
                _captionTmp.text = caption.ToUpperInvariant();
        }

        internal void SetCount(int count)
        {
            if (count == _shown)
                return;
            _shown = count;
            _hasCards = count > 0;
            if (_count != null)
                _count.text = count.ToString();
            if (_topMaterial != null)
            {
                Color color = _hasCards ? _baseColor : Color.Lerp(_baseColor, Color.gray, 0.7f);
                if (_topMaterial.color != color)
                    _topMaterial.color = color;
            }
        }

        /// <summary>
        /// USABLE-HIGHLIGHT — start (or stop) the stack's ember drift. Called every frame by
        /// <see cref="PileViewer.TickItemsUsableHighlight"/> with the live "at least one item is usable
        /// right now" answer, and change-gated here so the emitter is never re-triggered per frame.
        ///
        /// WHY PARTICLES AND NOT A FRAME (user's own split): the item CARDS get a frame because a card
        /// has a silhouette worth tracing; the closed stack does not — it is a 4-slab lump lying flat on
        /// the board, and a frame around it would be exactly the rectangle of light the user rejected.
        /// So the deck hints at "something in here is playable" the way a fantasy table would: a few soft
        /// gold motes lifting off the pile and fading out, round and irregular, never a shape.
        ///
        /// SUBTLE BY CONSTRUCTION: ~5 motes a second, each a few millimetres across, living under two
        /// seconds, at well under half opacity, drifting a couple of centimetres. At any instant there
        /// are under a dozen on screen — a shimmer you notice in peripheral vision, not an effect that
        /// competes with the board.
        ///
        /// Switching OFF stops EMISSION only, so the motes already in flight finish their fade instead of
        /// vanishing mid-air (a hard clear is what would read as a bug when a turn ends).
        ///
        /// Mod-owned child of this stack: hidden with the stack, destroyed with it, nothing game-side
        /// touched. Purely local — no game state, no network traffic (multiplayer-neutral).
        /// </summary>
        internal void SetUsableHighlight(bool on)
        {
            if (on == _usableCueOn)
                return;
            _usableCueOn = on;
            if (_usableEmbers == null)
            {
                if (!on)
                    return; // never built, never needed — don't pay for the emitter
                _usableEmbers = BuildUsableEmbers();
                if (_usableEmbers == null)
                    return; // shader-less environment — the cue degrades to nothing (never to a crash)
            }
            if (on)
                _usableEmbers.Play();
            else
                _usableEmbers.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// Build the stack's ember emitter once. Local simulation space so the motes ride the tray if the
        /// player repositions the control board (world space would smear them into a trail behind it), and
        /// a flattened box shape spanning the pile face so they lift off the WHOLE deck rather than from a
        /// single point. Velocity is authored rather than taken from the shape's normal: the stack lies
        /// flat against the tray, so "up" for this cue is the tray's own +Y with a slight lean toward the
        /// viewer (-Z), which is what makes the motes read as rising off the deck from every seat.
        ///
        /// The material is <c>Sprites/Default</c> (the same shader the button dust FX proved on hardware —
        /// vertex-coloured, alpha-blended, always present) textured with
        /// <see cref="WorldUI.SoftCueArt.MoteTexture"/> so each particle is a soft ROUND ember; untextured,
        /// that shader draws hard squares, which is precisely the look being replaced. Returns null only
        /// when even that shader is missing.
        /// </summary>
        private ParticleSystem? BuildUsableEmbers()
        {
            Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                return null;

            float w = CardsConfig.CardWidth.Value * SlabFactor;
            float h = CardsConfig.CardHeight * SlabFactor;

            var go = new GameObject("UsableEmbers");
            go.transform.SetParent(transform, worldPositionStays: false);
            // Just proud of the top slab (which spans ±0.0009 about z 0) so the motes are never born
            // inside the pile, but behind the count/caption text at -0.0025 so they never fog the number.
            go.transform.localPosition = new Vector3(0f, 0f, -0.0016f);
            go.transform.localRotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // follows a scaled tray
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 24;
            main.startSpeed = 0f;      // drift comes from velocityOverLifetime below
            main.gravityModifier = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(w * 0.045f, w * 0.11f);
            main.startColor = EmberColor;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 5f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(w * 0.85f, h * 0.85f, 0.0001f); // a flat sheet over the pile face
            shape.randomDirectionAmount = 0f;

            // Slow lift along the tray's +Y with a small lean toward the viewer, and a per-particle spread
            // so the column never looks like a machine-made jet.
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.004f, 0.004f);
            vel.y = new ParticleSystem.MinMaxCurve(0.010f, 0.022f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.008f, -0.002f);

            // Gentle organic wander — this is what keeps the drift from reading as a straight line.
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = new ParticleSystem.MinMaxCurve(0.006f);
            noise.frequency = 0.35f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.12f);
            noise.damping = true;

            // Fade in, hold under half opacity, fade out — a mote is never "switched on".
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(shader);
            mat.mainTexture = WorldUI.SoftCueArt.MoteTexture();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 2; // over the slabs, under the panel canvases

            Core.VRLayers.Apply(go); // mod-owned FX on the mod layer (no children — recursion-safe)
            // 2026-08-04: the ember motes are alpha-blended and depth-less like the labels —
            // ride the board's furniture order group (offset 2 captured from the line above).
            PlayTray.AdoptFurniture(go);
            return ps;
        }

        /// <summary>The mod's telegraph gold, warmed toward the initiative ring's amber so the deck cue and
        /// the item cards' frame read as the same voice. Alpha is the ember's CEILING — the lifetime
        /// gradient above never lets a mote reach it for long.</summary>
        private static readonly Color EmberColor = new Color(1f, 0.80f, 0.36f, 0.45f);

        // ---- grab (refused) --------------------------------------------------------

        /// <summary>
        /// NO stack is grabbable — for ANY pile kind. This is the one gate that makes a bare
        /// trigger press unable to pick a pile up: <c>ProximityGrabber.UpdateHighlight</c>
        /// consults it before a collider can even become a grab candidate, so a refusing stack
        /// is simply invisible to the trigger and the press falls straight through to the
        /// laser/poke toggle instead of being claimed.
        ///
        /// RULING CHAIN. The ITEMS stack was de-grabbed first (user ruling 2026-08-02: "Das
        /// Greifen des GANZEN Fächers mit dem Trigger war möglich — das komplett entfernen,
        /// das war nie gewollt" — full context on <see cref="ItemsPile.TogglePoke"/>). The
        /// DISCARD and BURNT stacks kept their pinch-grab (grip = browse-while-held) until the
        /// user report 2026-08-06: "Der BURNT Stapel und der DISCARDED Stapel dürfen nicht
        /// direkt mit dem Trigger nehmbar sein" — the held-browse gesture read as an ACCIDENTAL
        /// pile pickup, exactly the failure the items ruling removed. All three stacks now share
        /// the identical policy: poke (finger) or laser click toggles the browse; nothing else.
        /// The whole grab route died with this gate — <c>OnGrab</c>/<c>OnRelease</c> overrides,
        /// <c>PileViewer.DispatchGrabOpen/Release</c>, CardsDriver's held-browse mode and
        /// <c>PileBrowser</c>'s follow-hand pose are all removed; peers keep receiving
        /// <c>PileBrowseHeld=false</c> through the unchanged wire seam
        /// (<see cref="PileBrowser.IsHandHeld"/>), which is now the only state that exists.
        /// </summary>
        public override bool CanGrab => false;

        // ---- poke (toggle browse) --------------------------------------------------

        protected override void OnEnable()
        {
            base.OnEnable(); // grab registration (P2 GrabbableBehaviour)
            Collider? collider = GetComponent<Collider>();
            if (collider != null)
                VRInteractables.RegisterPokeable(this, collider);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            VRInteractables.UnregisterPokeable(this);
        }

        // Poke edge-gating (hardware: pile poke double-trigger): a physical poke used to
        // toggle the browse OPEN on finger entry and then toggle it AGAIN while the finger
        // retracted back out through the collider — the PokeInteractor re-arms on hover
        // flicker (the opening browse fan spawns colliders near the fingertip, stealing and
        // returning the nearest-pokeable hover), so a single physical poke could fire
        // OnPoke twice. Two guards make the toggle edge-robust:
        // 1. ENTRY-ONLY: after a toggle the stack stays disarmed until the fingertip has
        //    LEFT the collider region (OnPokeExit re-arms — the interactor raises it once
        //    the tip is beyond hover range, i.e. genuinely out of the stack).
        // 2. COOLDOWN: no second toggle within 0.4 s, killing the hover-flicker re-arm
        //    path (exit+enter within the same physical poke) outright.
        // The board LASER routes through LaserToggle below (cooldown only): a deliberate
        // second trigger click while still pointing at the stack must keep working.
        private const float PokeToggleCooldownSeconds = 0.4f;
        private bool _pokeArmed = true;
        private float _nextToggleTime;

        public void OnPokeEnter(VRHand hand)
        {
            if (_hasCards)
                hand.SendHaptic(HapticPreset.HoverTick);
        }

        public void OnPokeExit(VRHand hand) => _pokeArmed = true; // left the stack — re-arm

        public void OnPoke(VRHand hand)
        {
            // Empty stacks refuse to OPEN — but never refuse to CLOSE their own open browse
            // (never-stuck guarantee, see PileViewer.BrowseOpenFor).
            if (!_hasCards && !_owner.BrowseOpenFor(_kind))
                return;
            if (!_pokeArmed || Time.unscaledTime < _nextToggleTime)
                return; // retract/flicker edge — one toggle per physical poke
            _pokeArmed = false;
            _nextToggleTime = Time.unscaledTime + PokeToggleCooldownSeconds;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} poked ({hand.Side}) — toggle browse.");
            _owner.DispatchPoke(_kind, hand);
        }

        /// <summary>
        /// Board-laser click path (CardsDriver): same toggle, but WITHOUT the finger's
        /// leave-the-collider re-arm requirement — a laser click is already a clean
        /// TriggerDown edge, and the beam legitimately stays on the stack between two
        /// deliberate clicks. The shared cooldown still debounces trigger bounce and
        /// cross-path double-fires (poke + laser inside the same 0.4 s).
        /// </summary>
        internal void LaserToggle(VRHand hand)
        {
            if ((!_hasCards && !_owner.BrowseOpenFor(_kind)) || Time.unscaledTime < _nextToggleTime)
                return;
            _nextToggleTime = Time.unscaledTime + PokeToggleCooldownSeconds;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} laser-clicked ({hand.Side}) — toggle browse.");
            _owner.DispatchPoke(_kind, hand);
        }
    }
}
