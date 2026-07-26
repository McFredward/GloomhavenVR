using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A READ-ONLY cosmetic mirror of a remote player's control board, rendered at their REAL synced
/// world transform (<c>owner.BoardPosition/BoardRotation/BoardScale</c>, valid only while
/// <c>owner.HasBoard</c>). It shows that player's TWO round cards so everyone can "see who placed
/// what", in initiative order (<c>InitiativeAbilityCard</c> first — mirrors the local board's
/// <c>PlayTray.SyncFromGameState</c> ordering).
///
/// ANTI-CHEAT (the linchpin): the card FRONTS are shown ONLY when
/// <see cref="RevealGate.ShowRoundCardFronts"/> is true — i.e. never during the game's own secret
/// <c>SelectAbilityCardsOrLongRest</c> phase for a remote actor. Otherwise the cards show BACKS.
/// We never transmit card identities; the cards are read locally from the already-host-replicated
/// <c>CPlayerActor.CharacterClass</c>, and turned face-up strictly on the game's authoritative
/// reveal — so this opens no new cheat vector.
///
/// VISIBILITY (<see cref="NetModule.RemoteBoards"/>, a purely LOCAL rendering choice):
///   Off             → render nothing;
///   ActionPhaseOnly → render only once the owner's cards may be shown (i.e. NOT the secret
///                     selection phase — the whole board is hidden during selection);
///   Always          → render the frame always; card BACKS during selection, real faces on reveal.
///
/// CARD FACE ART: the full painted card art is only ever instantiated by the game as an
/// <c>AbilityCardUI</c> widget for the LOCAL player's own hand — there is no clean, cheat-safe way
/// to reach a remote actor's card art/texture (the model <c>CAbilityCard</c> exposes only data:
/// name, initiative, actions — no sprite/texture). So a face-up card here is a clear card-shaped
/// panel showing the card NAME + INITIATIVE number (<c>CAbilityCard.Name</c> / <c>.Initiative</c>),
/// which is exactly what "see who placed what" needs. Backs reuse the mod's own card-back texture
/// (<c>CardMesh</c>), drawn unlit.
///
/// Strict no-op offline / single-player / when the actor is null (<see cref="NetPlayerActors"/>
/// degrades to null there); everything is re-read each <see cref="Tick"/>.
/// </summary>
internal sealed class RemoteControlBoard
{
    // Frame geometry in the same "card real-metre" units as the local board (PlayTray BoardW/H),
    // so scaling by the owner's BoardScale reproduces their board's world size.
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    // Two round-card slots, side by side and enlarged for at-a-distance legibility.
    private const float CardW = 0.15f;
    private const float CardH = CardW * (88f / 63.5f);
    private const float SlotX = 0.11f;      // ± slot centre X (board-local)
    private const float ProudZ = -0.004f;   // toward the viewer (−Z), proud of the frame face

    // ---- pile stacks (report 6) ---------------------------------------------------------------
    // The three card STACKS that hang off the right edge of the local control board
    // (PlayTray.PileMountBase + PileViewer's per-stack offsets: discard above, burnt below, items
    // two rows down). They exist here for one reason: a card flying into a peer's discard pile
    // (RemoteCardFx) needs a VISIBLE destination on that peer's board — before this, the remote
    // board had slots but no piles at all, so any pile-bound flight would have ended in empty air.
    // Kept deliberately small and back-textured (never a card identity — same anti-cheat stance as
    // everything else here).
    private const float PileX = Cards.PlayTray.BoardHalfWidthLocal + 0.012f + Cards.PlayTray.PileStackOffsetX;
    private const float PileSpacing = Cards.PlayTray.PileStackSpacing;
    private const float PileW = 0.075f;
    private const float PileH = PileW * (88f / 63.5f);

    /// <summary>Board-local position of a play SLOT (0 = left, 1 = right) — the anchor a docking
    /// card flies into. Shared with <see cref="RemoteCardFx"/> so the flight and the rendered slot
    /// agree even when the board frame itself is hidden by the visibility setting.</summary>
    internal static Vector3 SlotLocal(int slot) => new(slot == 0 ? -SlotX : SlotX, 0f, ProudZ);

    /// <summary>Board-local position of a pile stack / the board centre for a card-FX anchor.
    /// Mirrors the LOCAL board's stack layout (<c>PlayTray.PileMountBase</c> +
    /// <c>PileViewer</c>'s ±spacing/2 and −1.5·spacing rows) so a peer's piles sit where that
    /// player's own piles sit.</summary>
    internal static Vector3 AnchorLocal(CardFxAnchor anchor) => anchor switch
    {
        CardFxAnchor.Slot0 => SlotLocal(0),
        CardFxAnchor.Slot1 => SlotLocal(1),
        CardFxAnchor.Discard => new Vector3(PileX, PileSpacing * 0.5f, ProudZ),
        CardFxAnchor.Burnt => new Vector3(PileX, -PileSpacing * 0.5f, ProudZ),
        CardFxAnchor.Items => new Vector3(PileX, -PileSpacing * 1.5f, ProudZ),
        _ => new Vector3(0f, 0f, ProudZ), // Board (and any unknown future id)
    };

    private readonly RemoteAvatar _owner;

    private GameObject? _root;
    private readonly BoardCard[] _cards = new BoardCard[2];
    private OwnerTag? _tag;

    private readonly CAbilityCard?[] _ordered = new CAbilityCard?[2];

    public RemoteControlBoard(RemoteAvatar owner)
    {
        _owner = owner;
    }

    public void Tick(float dt)
    {
        RemoteBoardVisibility vis = NetModule.RemoteBoards != null
            ? NetModule.RemoteBoards.Value
            : RemoteBoardVisibility.Off;

        // Read the owner's actor fresh each frame (null offline / single-player / netcode absent /
        // benched) — a strict no-op in every one of those cases.
        CPlayerActor? actor = _owner.HasBoard && vis != RemoteBoardVisibility.Off
            ? NetPlayerActors.ActorFor(_owner.PlayerId)
            : null;

        bool showFronts = actor != null && RevealGate.ShowRoundCardFronts(actor);

        bool showBoard = actor != null && vis switch
        {
            RemoteBoardVisibility.Always => true,
            // ActionPhaseOnly: only once the cards may be shown (not the secret selection phase).
            RemoteBoardVisibility.ActionPhaseOnly => showFronts,
            _ => false,
        };

        if (!showBoard)
        {
            SetActive(false);
            return;
        }

        EnsureBuilt();
        SetActive(true);

        // Place at the REAL synced world transform.
        _root!.transform.SetPositionAndRotation(_owner.BoardPosition, _owner.BoardRotation);
        _root.transform.localScale = Vector3.one * (_owner.BoardScale > 0f ? _owner.BoardScale : 1f);

        OrderRoundCards(actor!);
        for (int i = 0; i < 2; i++)
            _cards[i].Set(_ordered[i], showFronts);

        _tag!.Tick();
    }

    /// <summary>Mirror the local board's ordering: <c>InitiativeAbilityCard</c> first, then the
    /// remaining round card(s). Falls back to list order when the initiative card is not yet set
    /// (e.g. mid-selection) — exactly like <c>PlayTray.SyncFromGameState</c>.</summary>
    private void OrderRoundCards(CPlayerActor actor)
    {
        _ordered[0] = _ordered[1] = null;
        CCharacterClass cc = actor.CharacterClass;
        var round = cc.RoundAbilityCards;
        CAbilityCard? initiative = cc.InitiativeAbilityCard;

        int idx = 0;
        if (initiative != null && round.Contains(initiative))
            _ordered[idx++] = initiative;
        for (int i = 0; i < round.Count && idx < 2; i++)
        {
            CAbilityCard c = round[i];
            if (c == null || c == initiative)
                continue;
            _ordered[idx++] = c;
        }
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject($"GloomhavenVR.RemoteControlBoard[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;

        // Board frame: a dark rounded slab. Unlit so it reads regardless of scene lighting.
        BoardVisual.Quad(_root.transform, "Frame", new Vector2(BoardW, BoardH),
            BoardVisual.Unlit(new Color(0.10f, 0.09f, 0.08f, 1f)));

        _cards[0] = new BoardCard(_root.transform, SlotLocal(0));
        _cards[1] = new BoardCard(_root.transform, SlotLocal(1));

        // The three stacks (report 6): the destinations a remote card flight lands on. Card-back
        // texture, drawn unlit, no labels — they are landing pads, not readable piles.
        Texture? stackTex = CardMesh.CreateBackMaterial().mainTexture;
        Material stackMat = BoardVisual.Unlit(new Color(0.82f, 0.82f, 0.82f, 1f), stackTex);
        BoardVisual.Quad(_root.transform, "DiscardStack", new Vector2(PileW, PileH), stackMat)
            .transform.localPosition = AnchorLocal(CardFxAnchor.Discard);
        BoardVisual.Quad(_root.transform, "BurntStack", new Vector2(PileW, PileH), stackMat)
            .transform.localPosition = AnchorLocal(CardFxAnchor.Burnt);
        BoardVisual.Quad(_root.transform, "ItemStack", new Vector2(PileW, PileH), stackMat)
            .transform.localPosition = AnchorLocal(CardFxAnchor.Items);

        // Ownership tag pinned just above the board's top-left corner, always facing the head.
        _tag = new OwnerTag(_owner.PlayerId, _root.transform,
            new Vector3(-BoardW * 0.5f + 0.02f, BoardH * 0.5f + 0.045f, ProudZ));

        VRLayers.Apply(_root);
    }

    private void SetActive(bool active)
    {
        if (_root != null && _root.activeSelf != active)
            _root.SetActive(active);
    }

    public void Destroy()
    {
        _tag?.Destroy();
        _tag = null;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }

    // ------------------------------------------------------------------ one card slot --

    /// <summary>
    /// One round-card slot: a card-shaped unlit panel. FACE-UP shows the card NAME + INITIATIVE;
    /// face-down (or empty-but-present) shows the mod card BACK. Rebuilt only on a real change
    /// (identity / face-up / initiative), so it is cheap to drive every frame.
    /// </summary>
    private sealed class BoardCard
    {
        private readonly GameObject _root;
        private readonly MeshRenderer _bg;
        private readonly Material _backMat;
        private readonly Material _faceMat;
        private readonly TextMeshPro _initLabel;
        private readonly TextMeshPro _nameLabel;

        private int _shownId = int.MinValue;
        private bool _shownFront;
        private bool _shownEmpty = true;

        public BoardCard(Transform parent, Vector3 localPos)
        {
            _root = new GameObject("Card");
            _root.transform.SetParent(parent, worldPositionStays: false);
            _root.transform.localPosition = localPos;

            // Card-back texture, drawn UNLIT (read the mod's shared back texture off CardMesh's
            // back material without mutating it, then wrap it in our own unlit material).
            Texture? backTex = CardMesh.CreateBackMaterial().mainTexture;
            _backMat = BoardVisual.Unlit(Color.white, backTex);
            _faceMat = BoardVisual.Unlit(new Color(0.86f, 0.81f, 0.68f, 1f)); // parchment

            _bg = BoardVisual.Quad(_root.transform, "Face", new Vector2(CardW, CardH), _backMat);

            _initLabel = MakeLabel("Initiative", new Vector3(0f, CardH * 0.34f, -0.001f),
                new Vector2(CardW * 0.9f, CardH * 0.28f), 0.09f,
                new Color(0.12f, 0.10f, 0.08f), FontStyles.Bold, wrap: false);
            _nameLabel = MakeLabel("Name", new Vector3(0f, -CardH * 0.12f, -0.001f),
                new Vector2(CardW * 0.86f, CardH * 0.5f), 0.045f,
                new Color(0.14f, 0.11f, 0.09f), FontStyles.Normal, wrap: true);
        }

        private TextMeshPro MakeLabel(string name, Vector3 localPos, Vector2 box, float maxFont,
            Color color, FontStyles style, bool wrap)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.fontStyle = style;
            TmpFit.Fit(tmp, box.x, box.y, maxFont, wrap);
            return tmp;
        }

        /// <summary>Show <paramref name="card"/> face-up (name+initiative) when
        /// <paramref name="front"/>, else the card back; hide entirely when there is no card.</summary>
        public void Set(CAbilityCard? card, bool front)
        {
            bool empty = card == null;
            int id = card != null ? card.CardInstanceID : int.MinValue;
            if (empty == _shownEmpty && id == _shownId && front == _shownFront)
                return;
            _shownEmpty = empty;
            _shownId = id;
            _shownFront = front;

            if (empty)
            {
                if (_root.activeSelf) _root.SetActive(false);
                return;
            }
            if (!_root.activeSelf) _root.SetActive(true);

            if (front)
            {
                _bg.sharedMaterial = _faceMat;
                _initLabel.gameObject.SetActive(true);
                _nameLabel.gameObject.SetActive(true);
                _initLabel.text = card!.Initiative.ToString();
                _nameLabel.text = CardDisplayName(card);
            }
            else
            {
                _bg.sharedMaterial = _backMat;
                _initLabel.gameObject.SetActive(false);
                _nameLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>Readable card name: <c>CAbilityCard.Name</c> (localized YML name), stripped of
        /// the <c>ABILITY_CARD_</c> loc prefix when present (as the game's own <c>StrictName</c>
        /// does). Guarded — a YML lookup miss degrades to "?".</summary>
        private static string CardDisplayName(CAbilityCard card)
        {
            string name;
            try { name = card.Name ?? "?"; }
            catch { return "?"; }
            const string prefix = "ABILITY_CARD_";
            return name.StartsWith(prefix, System.StringComparison.Ordinal)
                ? name.Substring(prefix.Length)
                : name;
        }
    }
}
