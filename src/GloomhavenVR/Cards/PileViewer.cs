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
    private (int discard, int burnt) _loggedCounts = (int.MinValue, int.MinValue);

    /// <summary>Stack poked (finger/laser) — CardsDriver toggles the browse fan.</summary>
    internal System.Action<PileKind, VRHand>? PokeToggled;

    /// <summary>Stack pinch-grabbed — CardsDriver opens the browse fan (held mode).</summary>
    internal System.Action<PileKind, VRHand>? GrabOpened;

    /// <summary>Grabbed stack released — CardsDriver dismisses a held browse.</summary>
    internal System.Action<PileKind, VRHand>? GrabReleased;

    internal bool IsBuilt => _discard != null;

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>Local caption for one pile (game terms with safe fallbacks).</summary>
    internal static string Caption(PileKind kind) => kind == PileKind.Discard
        ? CardsGameApi.Localize("GUI_TAKE_DAMAGE_DISCARD", "Discard")
        : CardsGameApi.Localize("GUI_TAKE_DAMAGE_BURN", "Burnt");

    internal void EnsureBuilt(PlayTray tray)
    {
        Transform? mount = tray.PileMount;
        if (mount == null)
            return;
        // A tray teardown destroys the stacks with the mount — the Unity fake-null
        // makes the == checks below true and the stacks rebuild from scratch.
        if (_discard == null)
        {
            _discard = PileStack.Create(mount, PileKind.Discard,
                new Color(0.55f, 0.48f, 0.34f), Caption(PileKind.Discard), this,
                new Vector3(PlayTray.PileStackOffsetX, PlayTray.PileStackSpacing * 0.5f, 0f));
            tray.RegisterLaserTarget(_discard.GetComponent<Collider>(), _discard);
        }
        if (_burnt == null)
        {
            _burnt = PileStack.Create(mount, PileKind.Burnt,
                new Color(0.45f, 0.22f, 0.16f), Caption(PileKind.Burnt), this,
                new Vector3(PlayTray.PileStackOffsetX, -PlayTray.PileStackSpacing * 0.5f, 0f));
            tray.RegisterLaserTarget(_burnt.GetComponent<Collider>(), _burnt);
        }
    }

    internal void SetVisible(bool visible)
    {
        if (_discard != null && _discard.gameObject.activeSelf != visible)
            _discard.gameObject.SetActive(visible);
        if (_burnt != null && _burnt.gameObject.activeSelf != visible)
            _burnt.gameObject.SetActive(visible);
    }

    internal void Destroy()
    {
        if (_discard != null)
            Object.DestroyImmediate(_discard.gameObject);
        if (_burnt != null)
            Object.DestroyImmediate(_burnt.gameObject);
        _discard = null;
        _burnt = null;
        _loggedCounts = (int.MinValue, int.MinValue);
    }

    // ------------------------------------------------------------------ status --

    /// <summary>
    /// Refresh counts/dimming from the authoritative piles (per frame while the
    /// tray shows; cheap — two list Counts). The Info line is change-deduped: one
    /// log per actual pile change (test #21 C), never per frame.
    /// </summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        if (_discard == null || _burnt == null || hand == null)
            return;
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
    }

    // ------------------------------------------------------------------ stack --

    /// <summary>
    /// One physical pile stack: a few offset card slabs + count + caption. Pokeable
    /// (finger + board laser, both land in <see cref="OnPoke"/>) AND grabbable
    /// (pinch, like cards — <c>snapToHand</c> off: the pile stays on the board, the
    /// grip only holds the browse open). VRCard's dual-registration pattern:
    /// GrabbableBehaviour base + manual pokeable registration.
    /// </summary>
    private sealed class PileStack : GrabbableBehaviour, IPokeable
    {
        private PileViewer _owner = null!;
        private PileKind _kind;
        private TextMeshPro? _count;
        private Material? _topMaterial;
        private Color _baseColor;
        private int _shown = int.MinValue;
        private bool _hasCards;

        // Slab footprint: 0.62× card size — reads as a mini pile without crowding
        // the 0.10 m column budget (PlayTray.BuildMounts collision math).
        private const float SlabFactor = 0.62f;

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
            Core.TmpFit.Fit(count, w * 0.9f, h * 0.62f, maxFontSize: 0.30f, wrap: false);

            // Caption under the stack (localized names shrink to fit, test #12).
            var captionGo = new GameObject("Caption");
            captionGo.transform.SetParent(go.transform, worldPositionStays: false);
            captionGo.transform.localPosition = new Vector3(0f, -h * 0.5f - 0.016f, -0.0025f);
            var captionTmp = captionGo.AddComponent<TextMeshPro>();
            captionTmp.text = caption.ToUpperInvariant();
            captionTmp.alignment = TextAlignmentOptions.Center;
            captionTmp.color = new Color(0.85f, 0.8f, 0.7f);
            Core.TmpFit.Fit(captionTmp, 0.095f, 0.024f, maxFontSize: 0.22f, wrap: false);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w + 0.012f, h + 0.012f, 0.022f);
            box.isTrigger = true;

            var stack = go.AddComponent<PileStack>();
            stack.snapToHand = false; // the pile never leaves the board — the grip holds the browse open
            stack._owner = owner;
            stack._kind = kind;
            stack._count = count;
            stack._topMaterial = topMaterial;
            stack._baseColor = color;
            Core.VRLayers.Apply(go); // mod layer (render-only; poke/grab via registries)
            return stack;
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
            _owner.GrabOpened?.Invoke(_kind, hand);
        }

        public override void OnRelease(VRHand hand, Vector3 velocity)
        {
            base.OnRelease(hand, velocity);
            _owner.GrabReleased?.Invoke(_kind, hand);
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

        public void OnPokeEnter(VRHand hand)
        {
            if (_hasCards)
                hand.SendHaptic(HapticPreset.HoverTick);
        }

        public void OnPokeExit(VRHand hand) { }

        public void OnPoke(VRHand hand)
        {
            if (!_hasCards)
                return;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Cards", $"Board: {name} poked ({hand.Side}) — toggle browse.");
            _owner.PokeToggled?.Invoke(_kind, hand);
        }
    }
}
