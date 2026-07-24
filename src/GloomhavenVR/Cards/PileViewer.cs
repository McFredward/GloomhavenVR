using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Discard/burnt pile stacks on the control board (hardware test #21, [Cards]
/// PileViewer): two small physical card piles docked off the board's RIGHT edge
/// (<see cref="PlayTray.PileMount"/> — the only free edge, see the BuildMounts
/// collision math), each rendered as a stack of card slabs with a live count and a
/// localized caption. Poking a stack (finger or board laser) TOGGLES the pile
/// browse fan; pinch-grabbing it raises the browse while held and dismisses on
/// release (<see cref="PileBrowser"/>; open/close policy and content live in
/// CardsDriver). Purely informational — counts are read straight from the
/// authoritative piles (<see cref="CardsGameApi.DiscardedCount"/> /
/// <see cref="CardsGameApi.BurntCount"/>, no game state is ever written) and logged
/// change-deduped.
/// </summary>
internal sealed class PileViewer
{
    private PileStack? _discard;
    private PileStack? _burnt;
    private PileStack? _items;
    private bool _locHooked;
    private (int discard, int burnt) _loggedCounts = (int.MinValue, int.MinValue);
    private int _loggedItems = int.MinValue;

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

    /// <summary>Stack poked (finger/laser) — CardsDriver toggles the browse fan.</summary>
    internal System.Action<PileKind, VRHand>? PokeToggled;

    /// <summary>Stack pinch-grabbed — CardsDriver opens the browse fan (held mode).</summary>
    internal System.Action<PileKind, VRHand>? GrabOpened;

    /// <summary>Grabbed stack released — CardsDriver dismisses a held browse.</summary>
    internal System.Action<PileKind, VRHand>? GrabReleased;

    internal bool IsBuilt => _discard != null;

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

    /// <summary>Local caption for one pile (real game loc keys with safe English fallbacks).</summary>
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
        _itemsBrowse.SetAnchor(mount);
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

    /// <summary>Re-read both pile captions in the current language (live-follow, Loc.OnChanged).</summary>
    internal void RefreshLabels()
    {
        _discard?.SetCaption(Caption(PileKind.Discard));
        _burnt?.SetCaption(Caption(PileKind.Burnt));
        _items?.SetCaption(Caption(PileKind.Items));
    }

    /// <summary>
    /// Round-2 live-apply: re-seat both pile stacks from the active board's per-board SCALE and
    /// inter-stack SPACING (discard upper at +spacing/2, burn lower at −spacing/2). Called from
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
            _itemsBrowse.Close(); // hidden (piles off / no hand) — never leave an item browse floating
    }

    internal void Destroy()
    {
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
        _loggedCounts = (int.MinValue, int.MinValue);
        _loggedItems = int.MinValue;
    }

    // ------------------------------------------------------------------ status --

    /// <summary>
    /// Refresh counts/dimming from the authoritative piles (per frame while the
    /// tray shows; cheap — two list Counts). The Info line is change-deduped: one
    /// log per actual pile change (test #21 C), never per frame.
    /// </summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        _hand = hand;
        if (_discard == null || _burnt == null || hand == null)
            return;
        if (!_discard.gameObject.activeSelf)
            return; // hidden ([Cards] PileViewer off / no hand) — no counts, no logs
        int discard = CardsGameApi.DiscardedCount(hand);
        int burnt = CardsGameApi.BurntCount(hand);
        if (_loggedCounts != (discard, burnt))
        {
            _loggedCounts = (discard, burnt);
            VRLog.Info("Cards", $"Piles: discard={discard}, burnt={burnt} " +
                                "(authoritative CCharacterClass piles).");
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
            if (_hand != null)
                _itemsBrowse.TogglePoke(_hand, hand);
            return;
        }
        _itemsBrowse.Close();
        PokeToggled?.Invoke(kind, hand);
    }

    internal void DispatchGrabOpen(PileKind kind, VRHand hand)
    {
        if (kind == PileKind.Items)
        {
            if (_hand != null)
                _itemsBrowse.OpenHeld(_hand, hand);
            return;
        }
        _itemsBrowse.Close();
        GrabOpened?.Invoke(kind, hand);
    }

    internal void DispatchGrabRelease(PileKind kind, VRHand hand)
    {
        if (kind == PileKind.Items)
        {
            _itemsBrowse.ReleaseHeld(hand);
            return;
        }
        GrabReleased?.Invoke(kind, hand);
    }

    // ------------------------------------------------------------------ stack --

    /// <summary>
    /// One physical pile stack: a few offset card slabs + count + caption. Pokeable
    /// (finger via <see cref="OnPoke"/>, board laser via <see cref="LaserToggle"/> —
    /// CardsDriver routes laser clicks there so the finger's edge gate never eats a
    /// deliberate second laser click) AND grabbable (pinch, like cards —
    /// <c>snapToHand</c> off: the pile stays on the board, the grip only holds the
    /// browse open). VRCard's dual-registration pattern: GrabbableBehaviour base +
    /// manual pokeable registration. Internal (not private) so CardsDriver's laser
    /// dispatch can type-test it.
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

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 0.012f, h + 0.012f, 0.022f);
            box.isTrigger = true;

            var stack = go.AddComponent<PileStack>();
            stack.snapToHand = false; // the pile never leaves the board — the grip holds the browse open
            stack._owner = owner;
            stack._kind = kind;
            stack._count = count;
            stack._captionTmp = captionTmp;
            stack._topMaterial = topMaterial;
            stack._baseColor = color;
            Core.VRLayers.Apply(go); // mod layer (render-only; poke/grab via registries)
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

        // ---- grab (pinch-to-browse) ------------------------------------------------

        public override bool CanGrab => base.CanGrab && _hasCards;

        public override void OnGrab(VRHand hand)
        {
            base.OnGrab(hand); // Holder bookkeeping only (snapToHand off)
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} pinch-grabbed ({hand.Side}) — browse while held.");
            _owner.DispatchGrabOpen(_kind, hand);
        }

        public override void OnRelease(VRHand hand, Vector3 velocity)
        {
            base.OnRelease(hand, velocity);
            _owner.DispatchGrabRelease(_kind, hand);
        }

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
            if (!_hasCards)
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
            if (!_hasCards || Time.unscaledTime < _nextToggleTime)
                return;
            _nextToggleTime = Time.unscaledTime + PokeToggleCooldownSeconds;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} laser-clicked ({hand.Side}) — toggle browse.");
            _owner.DispatchPoke(_kind, hand);
        }
    }
}
