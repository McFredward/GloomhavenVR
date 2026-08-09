using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Shared card panel
// =================================================================================================

/// <summary>
/// One card slot on a remote board. FACE-UP shows the REAL, fully detailed game card face;
/// face-down (or empty-but-present) shows the mod card BACK. Rebuilt only on a real change
/// (identity / face-up / owner), so it is cheap to drive on the board's 4 Hz cadence.
///
/// This is the ONE card widget every remote-board surface uses (the two round-card slots and the
/// active-card column), extracted from <see cref="RemoteControlBoard"/> so the size is a parameter:
/// the round cards read large at a distance, the active column deliberately smaller — the same size
/// split the LOCAL board makes between its slot cards and <c>ActivePileViewer</c>.
///
/// CARD FACE ART (the user requirement "vollständig alle Details … damit Mitspieler die Karte
/// vollständig lesen können"): a face-up card here is the game's OWN card widget — full painted art,
/// both action halves with all their icons and numbers, the initiative disc, the level, the
/// enhancement stickers — cloned onto a world-space canvas by <see cref="RemoteCardArt"/> from a
/// source resolved by <see cref="RemoteAbilityCardSource"/> (the peer's own live
/// <c>AbilityCardUI</c>, or a widget borrowed from the game's pool). See that class for the evidence
/// that this is possible at all; the mod-drawn NAME + INITIATIVE panel this slot used to show is now
/// only the LAST-RESORT fallback for when neither source can be resolved.
///
/// ANTI-CHEAT: this panel never decides anything. It renders a face only when its caller passes
/// <c>front: true</c>, and every caller derives that strictly from
/// <see cref="RevealGate.ShowRoundCardFronts"/>. The face host is created INSIDE the
/// <c>if (front)</c> branch and <see cref="RemoteCardArt"/> builds its clone under an INACTIVE host,
/// so no face object can ever render for a frame ahead of the gate; on <c>front: false</c> the face
/// is torn down before anything else happens.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. Source: the peer's round-card slots off
/// the host-replicated <c>CPlayerActor.CharacterClass</c> (<c>NetPlayerActors.ActorFor</c>), every
/// face gated by <see cref="RevealGate"/>. Card IDENTITY is DELIBERATELY-NOT on the wire — drawing
/// a readable card WITHOUT transmitting one is the requirement this panel exists to satisfy. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteBoardCard
{
    private readonly GameObject _root;
    private readonly MeshRenderer _bg;
    private readonly Material _backMat;
    private readonly Material _faceMat;
    private readonly Material _bodyMat;
    private readonly TextMeshPro _initLabel;
    private readonly TextMeshPro _nameLabel;

    /// <summary>The real-card-face overlay for this slot (created lazily on the first face-up card, so
    /// a slot that never turns face-up never allocates one). Owned by us and destroyed with the
    /// board root; the CLONE inside it is a throwaway we own outright.</summary>
    private RemoteCardArt? _art;
    private readonly float _width;
    private readonly float _height;

    private int _shownId = int.MinValue;
    private bool _shownFront;
    private bool _shownEmpty = true;
    private int _shownOwner = int.MinValue;

    /// <summary>
    /// Change-key identity for <see cref="SetAnonymousBack"/> — "a card is lying here, and this
    /// client does not (yet) know which one". It must be distinct from
    /// <c>int.MinValue</c> (which is this class's "no card at all" key, see <see cref="Set"/> and
    /// <see cref="Blank"/>) or the panel would early-return between the empty state and the
    /// anonymous back and never repaint.
    /// </summary>
    private const int AnonymousCardId = int.MinValue + 1;

    /// <summary>Which path produced the face currently shown — surfaced to the board's diagnostics so
    /// a hardware log can state the FIDELITY per slot, not just that a card is drawn.</summary>
    public RemoteAbilityCardSource.FacePath Path { get; private set; }
        = RemoteAbilityCardSource.FacePath.None;

    public RemoteBoardCard(Transform parent, Vector3 localPos, float width, float height)
    {
        _root = new GameObject("Card");
        _root.transform.SetParent(parent, worldPositionStays: false);
        _root.transform.localPosition = localPos;
        _width = width;
        _height = height;

        // Card-back texture, drawn UNLIT (read the mod's shared back texture off CardMesh's back
        // material without mutating it, then wrap it in our own unlit material).
        Texture? backTex = CardMesh.CreateBackMaterial().mainTexture;
        _backMat = BoardVisual.Unlit(Color.white, backTex);
        _faceMat = BoardVisual.Unlit(new Color(0.86f, 0.81f, 0.68f, 1f)); // parchment (fallback panel)
        // Dark card BODY behind a hosted real face: RemoteCardArt insets the art by its BorderFraction,
        // so a rim of this quad shows around it and reads as the card's own dark edge — the same
        // relationship CardMesh's slab has to an adopted face on the local board.
        _bodyMat = BoardVisual.Unlit(new Color(0.09f, 0.08f, 0.07f, 1f));

        _bg = BoardVisual.Quad(_root.transform, "Face", new Vector2(width, height), _backMat);

        _initLabel = RemoteBoardContent.Label(_root.transform, "Initiative",
            new Vector3(0f, height * 0.34f, -0.001f),
            new Vector2(width * 0.9f, height * 0.28f), 0.09f,
            new Color(0.12f, 0.10f, 0.08f), TextAlignmentOptions.Center, FontStyles.Bold);
        _nameLabel = RemoteBoardContent.Label(_root.transform, "Name",
            new Vector3(0f, -height * 0.12f, -0.001f),
            new Vector2(width * 0.86f, height * 0.5f), 0.045f,
            new Color(0.14f, 0.11f, 0.09f), TextAlignmentOptions.Center, FontStyles.Normal, wrap: true);

        // Start HIDDEN and in step with the _shownEmpty seed: Set() early-returns while nothing
        // changed, so a panel that never receives a card (an unused active-grid cell, an empty
        // round slot) must not be left standing here showing a card back.
        _root.SetActive(false);
    }

    /// <summary>Re-seat the panel (the active-card grid relays its cards as the pile changes).</summary>
    public void Move(Vector3 localPos) => _root.transform.localPosition = localPos;

    /// <summary>
    /// Mip-bake upkeep for a hosted real face (see <see cref="RemoteCardArt.MaintainMipBake"/>).
    /// <see cref="Set"/> is change-gated, so without this the clone would only ever get the single
    /// build-time sprite swap and its ASYNC header art would stay mipless — the "extreme aliasing"
    /// the user reported on remote cards. Called by the owners on their 4 Hz content cadence;
    /// self-early-returns while no front is up, so it is free on backs/empty slots.
    /// </summary>
    public void MaintainMips() => _art?.MaintainMipBake();

    /// <summary>
    /// Show <paramref name="card"/> face-up when <paramref name="front"/> — as the REAL game card
    /// face when one can be resolved for <paramref name="owner"/>, else as the mod-drawn
    /// name+initiative panel — otherwise the card BACK; hide entirely when there is no card.
    ///
    /// <paramref name="owner"/> is the actor whose board this is; it is used ONLY to find that
    /// player's own already-existing card widget (see <see cref="RemoteAbilityCardSource"/>) and is
    /// never written to. Passing null simply drops the slot to the pooled/fallback paths.
    ///
    /// Change-gated on identity + face-up + owner, so the expensive part (cloning a card widget) runs
    /// once per actual change and NEVER per tick — the board's 4 Hz refresh stays a handful of
    /// early-returns.
    /// </summary>
    public void Set(CAbilityCard? card, bool front, CPlayerActor? owner = null)
    {
        bool empty = card == null;
        int id = card != null ? card.CardInstanceID : int.MinValue;
        int ownerId = OwnerKey(owner);
        if (empty == _shownEmpty && id == _shownId && front == _shownFront && ownerId == _shownOwner)
            return;
        _shownEmpty = empty;
        _shownId = id;
        _shownFront = front;
        _shownOwner = ownerId;

        if (empty)
        {
            // ANTI-CHEAT + hygiene: drop any hosted face BEFORE the slot goes away, so a slot that is
            // re-used for a different card (the active grid re-packs its cells) can never flash the
            // previous card's face.
            ClearFace();
            if (_root.activeSelf) _root.SetActive(false);
            return;
        }
        if (!_root.activeSelf) _root.SetActive(true);

        if (front)
        {
            // Try the REAL card widget first. The host is created here, inside the front branch —
            // there is no code path in which a face object exists while the gate says "backs".
            _art ??= new RemoteCardArt(_root.transform, _width, _height);
            Path = RemoteAbilityCardSource.ShowFullFace(_art, owner, card!);

            bool real = Path != RemoteAbilityCardSource.FacePath.None;
            if (real)
            {
                // A real face is up: show the dark card BODY behind it (the art is inset, so this is
                // the card's edge) and retire the mod-drawn labels — the face carries all of it, in
                // the game's own typography, and a second name on top would only fight it.
                _bg.sharedMaterial = _bodyMat;
                _initLabel.gameObject.SetActive(false);
                _nameLabel.gameObject.SetActive(false);
                // The board is contractually INERT. The clone is uGUI (no colliders today) and
                // RemoteCardArt already strips its raycasters, but the board's guarantee is a runtime
                // fact, not a review claim — so sweep the hosted subtree too.
                RemoteBoardFurniture.StripColliders(_root, "RemoteBoardCard face");
                return;
            }

            // LAST RESORT (no live widget AND the pool could not manufacture one): the legacy
            // parchment panel with the card's name + initiative. Strictly better than a blank back,
            // strictly worse than the real card — the log line says which one you are looking at.
            _bg.sharedMaterial = _faceMat;
            _initLabel.gameObject.SetActive(true);
            _nameLabel.gameObject.SetActive(true);
            _initLabel.text = card!.Initiative.ToString();
            _nameLabel.text = DisplayName(card);
        }
        else
        {
            ClearFace();
            _bg.sharedMaterial = _backMat;
            _initLabel.gameObject.SetActive(false);
            _nameLabel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Show a card BACK for a slot that is known to be OCCUPIED without knowing WHICH card occupies
    /// it — the user's requirement "Ich will auch sehen wenn eine Karte abgelegt wurde auf dem
    /// controllboard (mit der Rueckseite)". The occupancy comes off the wire
    /// (<see cref="NetProtocol.BoardUiSlotMask"/>, a POSITION and nothing else); the identity does
    /// not, and never will.
    ///
    /// WHY IT IS NOT JUST <c>Set(card, front: false)</c>. There IS no card to pass. The three cases
    /// this panel now distinguishes are "no card here" (hidden), "a card whose identity we hold"
    /// (<see cref="Set"/> — face or back per <see cref="RevealGate"/>) and "a card we can see but
    /// cannot name" (here). Only the middle one can ever turn face-up, so this method is
    /// structurally incapable of revealing anything: it does not take a card, it tears any hosted
    /// face down, and it hard-sets the back material.
    /// </summary>
    public void SetAnonymousBack()
    {
        if (!_shownEmpty && _shownId == AnonymousCardId && !_shownFront)
            return; // already showing the anonymous back — nothing to repaint
        _shownEmpty = false;
        _shownId = AnonymousCardId;
        _shownFront = false;
        _shownOwner = int.MinValue;

        ClearFace();
        if (!_root.activeSelf) _root.SetActive(true);
        _bg.sharedMaterial = _backMat;
        _initLabel.gameObject.SetActive(false);
        _nameLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// ANTI-CHEAT TEARDOWN — return the slot to "nothing shown" AND invalidate its change key.
    ///
    /// WHY THE CHANGE KEY MUST GO TOO. <see cref="Set"/> early-returns while identity + face-up +
    /// owner are unchanged, so simply hiding the slot's GameObject would leave the key claiming a
    /// face is up. Two things then go wrong on the way back: (a) if the slot reappears while the
    /// gate is SHUT, the whole board is re-activated one statement before <see cref="Set"/> runs —
    /// a single frame in which a stale face would render behind a closed gate; and (b) if it
    /// reappears while the gate is OPEN with the same card, the early-return would skip the rebuild
    /// and the slot would stay blank. Clearing the key makes the next <see cref="Set"/> a real
    /// decision in both directions, which is the only state this class is allowed to be in.
    ///
    /// Called whenever the board stops being drawn (visibility off, peer without a board, the
    /// ActionPhaseOnly setting hiding the board during the secret selection phase). Cheap enough to
    /// call every frame while hidden: it self-early-returns once blank.
    /// </summary>
    public void Blank()
    {
        if (_shownEmpty && _shownId == int.MinValue && !_shownFront
            && Path == RemoteAbilityCardSource.FacePath.None)
            return; // already blank — nothing to undo

        ClearFace();
        SetHalfStates(-1, -1); // a re-shown slot must never come back with a stale glow lit
        _shownEmpty = true;
        _shownId = int.MinValue;
        _shownFront = false;
        _shownOwner = int.MinValue;
        _bg.sharedMaterial = _backMat;
        _initLabel.gameObject.SetActive(false);
        _nameLabel.gameObject.SetActive(false);
        if (_root.activeSelf)
            _root.SetActive(false);
    }

    // ---------------------------------------------------------- half hover/selection glow --

    /// <summary>The two half glow quads (top / bottom action region), built lazily on the first
    /// synced hover/selection so a board whose owner never touches a half allocates nothing.</summary>
    private GameObject? _halfGlowTop;
    private GameObject? _halfGlowBottom;

    /// <summary>The glow quads' material + renderer refs (index 0 = bottom, 1 = top), captured
    /// at build time so the per-frame pulse never calls GetComponent.</summary>
    private Material?[] _halfGlowMats = System.Array.Empty<Material?>();

    /// <summary>The shared telegraph gold — the additive Overlay shader emits RGB as brightness
    /// (alpha only matters on the Sprites/Default fallback), so the pulse scales both, exactly
    /// like <c>PlayTray.SlotPulse</c>.</summary>
    private static readonly Color GlowGold = new(1f, 0.85f, 0.3f, 0.95f);

    /// <summary>Currently shown hover half (-1 none, 0 bottom, 1 top) — change gate.</summary>
    private int _shownHalfHover = -1;

    /// <summary>Currently shown SELECTED half (-1 none, 0 bottom, 1 top) — change gate.</summary>
    private int _shownHalfSelected = -1;

    /// <summary>
    /// Drive the two-state half highlight (extras extension record 14 — "worüber hovert mein
    /// Mitspieler" + the follow-up "welche Hälfte hat er GEKLICKT"). Called every frame by the
    /// owning board. <paramref name="hoverHalf"/> / <paramref name="selectedHalf"/>:
    /// -1 none, 0 bottom, 1 top.
    ///
    /// ─── THE GAME'S OWN HIGHLIGHT, NOT A MOD RECTANGLE (user report 2026-08-08) ────────────────
    /// "Das Synchronisieren des Mouseovers über eine Action einer Karte in der Aktionsauswahlphase
    /// highlighted ein ganzes Rechteck der Hälfte der Karte bei dem Remote-Spieler. Es soll so
    /// angezeigt werden, wie der Spieler selbst es auch sieht, also das Highlighting vom Spiel
    /// selbst auf der jeweiligen Karte."
    ///
    /// The first revision drew two additive gold QUADS sized off <c>HalfSelection</c>'s zone
    /// fractions — 96 % × 42 % of the card, i.e. literally "ein ganzes Rechteck der Hälfte der
    /// Karte". Nothing about that is what the owner sees: the game frames the action REGION with
    /// <c>CardActionHighlight</c>, a 9-sliced highlight <c>Image</c> authored into the card prefab
    /// with an angular shine sweep, pulsing (alpha 1↔0.3 over 0.5 s legs) for a hover and steady
    /// at full alpha for a click.
    ///
    /// It does not have to be imitated, because IT IS ALREADY HERE. A face-up slot on a remote
    /// board hosts a real <c>Object.Instantiate</c> clone of the game's own <c>FullAbilityCard</c>
    /// widget (<see cref="RemoteCardArt"/>), and that clone keeps its <c>FullAbilityCardAction</c>
    /// and <c>CardActionHighlight</c> components — only the raycasters and <c>CardEffects</c> are
    /// stripped. So the mirror now calls the GAME's own <c>ShowHover</c> / <c>ShowSelected</c> /
    /// <c>Hide</c> on the clone's own highlight objects: same sprite, same shader, same shine
    /// sweep, same LeanTween cadence, same rect, frame for frame the owner's picture. This is the
    /// remote twin of the LOCAL fix (<c>CardsDriver.SetActiveHighlight</c> →
    /// <c>FullAbilityCard.ToggleHighlightHover</c>), which retired the same overlay quads on the
    /// player's own cards for the same complaint.
    ///
    /// FALLBACK, and only there: a slot that is NOT showing a real card face (a card back, an
    /// anonymous back, or the last-resort parchment name panel) has no <c>CardActionHighlight</c>
    /// to drive — there is no game card in the scene to highlight. Those keep the mod quads, so
    /// "the owner is pointing at the top half of this slot" stays visible rather than silently
    /// disappearing. <see cref="HighlightPath"/> reports which of the two is live, per slot.
    ///
    /// POSITIONS only — no card data is read, and the clone is never told which card it is.
    /// </summary>
    public void SetHalfStates(int hoverHalf, int selectedHalf)
    {
        _shownHalfHover = hoverHalf;
        _shownHalfSelected = selectedHalf;

        if (TryDriveGameHighlight(hoverHalf, selectedHalf))
        {
            // Never both: a slot that upgraded from a back to a real face must not keep the quad
            // it lit while it was a back.
            HideModGlows();
            LogHighlightPathIfChanged();
            return;
        }

        if ((hoverHalf >= 0 || selectedHalf >= 0) && _halfGlowTop == null)
            BuildHalfGlows();
        if (_halfGlowTop == null || _halfGlowBottom == null)
        {
            LogHighlightPathIfChanged();
            return;
        }

        DriveHalf(_halfGlowBottom, 0, hoverHalf, selectedHalf);
        DriveHalf(_halfGlowTop, 1, hoverHalf, selectedHalf);
        LogHighlightPathIfChanged();
    }

    /// <summary>Last logged (path, hover, selected) triple — the change gate for the line below.</summary>
    private string _loggedHighlight = string.Empty;

    /// <summary>
    /// State ONCE per real change which mechanism lit this slot. The regression this guards is
    /// exactly the reported one: "the peer sees a big rectangle" is `mod-quad` on a slot whose
    /// `face=` says a real card is up — a contradiction a grep for `Remote board card highlight`
    /// finds in the hardware log without a screenshot.
    /// </summary>
    private void LogHighlightPathIfChanged()
    {
        string now = $"{HighlightPath}|{_shownHalfHover}|{_shownHalfSelected}|{Path}";
        if (now == _loggedHighlight)
            return;
        _loggedHighlight = now;
        if (HighlightPath == "none")
            return; // "nothing is lit" is the resting state, not news
        string half(int v) => v == 1 ? "TOP" : v == 0 ? "BOTTOM" : "none";
        VRLog.Info("Net", $"Remote board card highlight: path={HighlightPath} " +
                          $"(hover {half(_shownHalfHover)}, clicked {half(_shownHalfSelected)}) " +
                          $"on a slot whose face={Path} — 'game' means the owner's own " +
                          "CardActionHighlight on the mirrored widget (record 14); 'mod-quad' is " +
                          "the back/fallback stand-in and is only correct while face=None.");
    }

    // ------------------------------------------------ the game's own action highlight --

    /// <summary>Which mechanism lit this slot's last half state — surfaced so a hardware log
    /// PROVES the owner's own highlight is what a peer sees, instead of merely proving something
    /// glowed. "game" = the clone's <c>CardActionHighlight</c>, "mod-quad" = the back/fallback
    /// stand-in, "none" = nothing lit.</summary>
    public string HighlightPath { get; private set; } = "none";

    /// <summary>The clone's own card widget, re-resolved whenever the hosted face is rebuilt.
    /// Unity-null aware: a destroyed clone must re-resolve, never be dereferenced.</summary>
    private FullAbilityCard? _faceCard;

    /// <summary>Instance id of the widget <see cref="_faceCard"/> was resolved from — the
    /// change key that survives a same-card rebuild.</summary>
    private int _faceCardKey;

    /// <summary>Last frame the clone was searched for — one probe per frame, at most.</summary>
    private int _faceProbeFrame = -1;

    /// <summary>Highlight materials we minted for the clone (index 0 = bottom, 1 = top), so the
    /// game's own shine-width write can never reach a SHARED material. See
    /// <see cref="IsolateHighlightMaterials"/>.</summary>
    private readonly Material?[] _highlightMats = new Material?[2];

    /// <summary>Last state pushed per half (index 0 = bottom, 1 = top): -1 nothing, 0 hover,
    /// 1 selected. Re-asserted whenever it disagrees with the highlight object's ACTUAL active
    /// flag, which is what makes this self-healing against the clone's own
    /// <c>FullAbilityCardAction.OnEnable → Show() → highlight.Hide()</c>.</summary>
    private readonly int[] _appliedHighlight = { -1, -1 };

    /// <summary>
    /// Drive the clone's own <c>CardActionHighlight</c> pair. Returns false when this slot has no
    /// real card face (back / anonymous back / parchment fallback), which is the ONLY case the mod
    /// quads still serve.
    ///
    /// Wrapped whole: a clone caught mid-rebuild, a card prefab variant without one of the two
    /// highlights, a widget the pool reclaimed — all degrade to "no game highlight", never take
    /// down the per-frame board tick this runs inside.
    /// </summary>
    private bool TryDriveGameHighlight(int hoverHalf, int selectedHalf)
    {
        try
        {
            if (Path == RemoteAbilityCardSource.FacePath.None)
            {
                ForgetGameHighlight();
                return false;
            }
            if (_faceCard == null)
            {
                // The clone lives under _root (slot → RemoteCardArt host → clone); one per slot.
                // Probed at most once a frame: a face path that somehow has no card widget must
                // not turn this per-frame drive into a per-frame hierarchy search.
                if (_faceProbeFrame == Time.frameCount)
                    return false;
                _faceProbeFrame = Time.frameCount;
                _faceCard = _root.GetComponentInChildren<FullAbilityCard>(includeInactive: true);
                if (_faceCard == null)
                    return false;
                int key = _faceCard.GetInstanceID();
                if (key != _faceCardKey)
                {
                    _faceCardKey = key;
                    ReleaseHighlightMaterials();
                    IsolateHighlightMaterials(_faceCard);
                    _appliedHighlight[0] = _appliedHighlight[1] = -1; // a fresh clone starts dark
                }
            }

            bool bottom = ApplyHalf(_faceCard.bottomActionButton, 0, hoverHalf, selectedHalf);
            bool top = ApplyHalf(_faceCard.topActionButton, 1, hoverHalf, selectedHalf);
            if (!bottom && !top)
                return false; // widget without highlights: let the quads speak rather than nothing

            HighlightPath = hoverHalf < 0 && selectedHalf < 0 ? "none" : "game";
            return true;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"Remote board card: the game's own action highlight could not be " +
                              $"driven ({ex.Message}) — this slot falls back to the mod half quad.");
            ForgetGameHighlight();
            return false;
        }
    }

    /// <summary>
    /// Push one half's wanted state onto the clone's highlight, using the GAME's own methods so
    /// the pulse, the shine width and the alpha curve are the owner's and not an imitation.
    /// Returns false when this half has no highlight object at all.
    ///
    /// SELF-HEALING re-assert: the write is gated on the last pushed state AND on the highlight
    /// object's real <c>activeSelf</c>. That second term matters — the clone's own
    /// <c>FullAbilityCardAction.OnEnable</c> calls <c>Show()</c>, which hides both highlights, so
    /// a purely state-gated driver would go dark for good the first time the face host re-enabled.
    /// A hover that is already running is left alone, so the game's LeanTween loop is never
    /// restarted mid-cycle (which would visibly re-snap the alpha to 1 every frame).
    /// </summary>
    private bool ApplyHalf(FullAbilityCardAction? action, int half, int hoverHalf, int selectedHalf)
    {
        CardActionHighlight? hl = action != null ? action.highlightAction : null;
        if (hl == null)
            return false;

        int want = selectedHalf == half ? 1 : hoverHalf == half ? 0 : -1;
        bool wantOn = want >= 0;
        bool isOn = hl.gameObject.activeSelf;
        if (want == _appliedHighlight[half] && wantOn == isOn)
            return true;

        _appliedHighlight[half] = want;
        if (want == 1)
            hl.ShowSelected();     // steady, selectedShineWidth — the owner's committed half
        else if (want == 0)
            hl.ShowHover();        // the game's own 1↔0.3 LeanTween loop, hoverShineWidth
        else
            hl.Hide();
        return true;
    }

    /// <summary>
    /// Give the clone's two highlight <c>Image</c>s their OWN material instance.
    ///
    /// WHY THIS IS NOT OPTIONAL: <c>CardActionHighlight.ShowHover/ShowSelected</c> write
    /// <c>imageHighlight.material.SetFloat("_AngularHighlightWidth", …)</c>, and
    /// <c>Graphic.material</c> is the SHARED asset (unlike <c>Renderer.material</c>, it does not
    /// instantiate). Driving a peer's mirrored card would therefore reach through the shared
    /// material and re-write the shine width on the LOCAL player's own cards — a peer's hover
    /// silently restyling your hand. One instance per clone closes that: identical pixels,
    /// private state. The instances are ours and are destroyed with the face.
    /// </summary>
    private void IsolateHighlightMaterials(FullAbilityCard card)
    {
        _highlightMats[0] = IsolateOne(card.bottomActionButton);
        _highlightMats[1] = IsolateOne(card.topActionButton);

        static Material? IsolateOne(FullAbilityCardAction? action)
        {
            CardActionHighlight? hl = action != null ? action.highlightAction : null;
            UnityEngine.UI.Image? img = hl != null ? hl.imageHighlight : null;
            Material? shared = img != null ? img.material : null;
            if (img == null || shared == null)
                return null;
            var owned = new Material(shared) { name = shared.name + " (RemoteBoardCard)" };
            img.material = owned;
            return owned;
        }
    }

    /// <summary>Destroy the material instances minted for the previous clone.</summary>
    private void ReleaseHighlightMaterials()
    {
        for (int i = 0; i < _highlightMats.Length; i++)
        {
            if (_highlightMats[i] != null)
                Object.Destroy(_highlightMats[i]);
            _highlightMats[i] = null;
        }
    }

    /// <summary>Drop every reference into a face that is gone (or never was), so the next call
    /// re-resolves from scratch instead of touching a destroyed clone.</summary>
    private void ForgetGameHighlight()
    {
        if (_faceCard == null && _faceCardKey == 0)
            return;
        ReleaseHighlightMaterials();
        _faceCard = null;
        _faceCardKey = 0;
        _appliedHighlight[0] = _appliedHighlight[1] = -1;
        HighlightPath = "none";
    }

    /// <summary>Park both mod quads (they exist only on slots that once had no real face).</summary>
    private void HideModGlows()
    {
        if (_halfGlowTop != null && _halfGlowTop.activeSelf)
            _halfGlowTop.SetActive(false);
        if (_halfGlowBottom != null && _halfGlowBottom.activeSelf)
            _halfGlowBottom.SetActive(false);
    }

    /// <summary>Per-half state resolve + write for the BACK/FALLBACK stand-in only (a slot showing
    /// a real card face drives the game's own highlight instead — see
    /// <see cref="TryDriveGameHighlight"/>): selected → steady, hovered → pulse, else off. Active
    /// flips are change-gated; the colour write runs only while a pulse is showing (at most one
    /// half per card) or on the steady half's first frame.</summary>
    private void DriveHalf(GameObject glow, int half, int hoverHalf, int selectedHalf)
    {
        bool selected = selectedHalf == half;
        bool hovered = hoverHalf == half;
        bool on = selected || hovered;
        if (glow.activeSelf != on)
            glow.SetActive(on);
        if (on)
            HighlightPath = "mod-quad";
        else if (half == 1 && hoverHalf < 0 && selectedHalf < 0)
            HighlightPath = "none";
        if (!on)
            return;
        Material? mat = half < _halfGlowMats.Length ? _halfGlowMats[half] : null;
        if (mat == null)
            return;
        // Selection wins on a doubly-lit half — the steady latch is the stronger statement,
        // matching the local RefreshHighlight, which parks the selected look over the hover's.
        float k;
        if (selected)
        {
            k = 1f;
        }
        else
        {
            // The game's hover loop: LeanTween alpha 1 → 0.3 → 1 at 0.5 s per leg = a 1 s
            // cycle. A sine at that period reads identically at glow scale.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f);
            k = Mathf.Lerp(0.3f, 1f, t);
        }
        Color c = GlowGold;
        c.r *= k;
        c.g *= k;
        c.b *= k;
        c.a *= k; // the alpha-blended Sprites/Default fallback (Overlay absent) pulses too
        if (mat.color != c)
            mat.color = c;
    }

    /// <summary>Build the two glow quads + capture their materials (lazy — first hover/click).</summary>
    private void BuildHalfGlows()
    {
        var size = new Vector3(_width * Cards.HalfSelection.ZoneWidthFrac,
                               _height * Cards.HalfSelection.ZoneHeightFrac, 1f);
        float centerY = _height * Cards.HalfSelection.ZoneCenterYFrac;
        const float glowZ = -0.004f; // in front of the face art's ~1.4 mm standoff
        _halfGlowTop = CardGlow.CreateGlowQuad("HalfHoverTop", _root.transform,
            size, new Vector3(0f, centerY, glowZ), GlowGold);
        _halfGlowBottom = CardGlow.CreateGlowQuad("HalfHoverBottom", _root.transform,
            size, new Vector3(0f, -centerY, glowZ), GlowGold);
        _halfGlowMats = new Material?[2];
        _halfGlowMats[0] = CaptureGlow(_halfGlowBottom);
        _halfGlowMats[1] = CaptureGlow(_halfGlowTop);
    }

    /// <summary>
    /// The material ref the pulse writes. DRAW ORDER is no longer forced here: the glow is 4 mm
    /// PROUD of the face art it belongs to (glowZ above), and the owning board's cluster seats both
    /// from their board-local depth (BoardVisual.AdoptBoardOrder) — a tie inside one tier, which
    /// Unity breaks by distance, i.e. in the glow's favour at every angle. The old constant said
    /// the same thing with a number that also outranked half the board.
    /// </summary>
    private static Material? CaptureGlow(GameObject glow)
    {
        var mr = glow.GetComponent<MeshRenderer>();
        return mr != null ? mr.sharedMaterial : null; // CreateGlowQuad mints one material per quad
    }

    /// <summary>Tear the hosted face down and forget which path drew it (the back/empty states must
    /// never report a fidelity path they are not showing).</summary>
    private void ClearFace()
    {
        // The clone (and its CardActionHighlight objects, and the material instances we minted for
        // them) dies with the face — every reference into it must go FIRST, or the next
        // SetHalfStates would drive a destroyed widget.
        ForgetGameHighlight();
        _art?.HideFront();
        Path = RemoteAbilityCardSource.FacePath.None;
    }

    /// <summary>Destroy the slot's face host. Called when the owning board is torn down: the host is
    /// a child of <c>_root</c> and dies with it either way, but going through
    /// <see cref="RemoteCardArt.Destroy"/> keeps the "we own the clone, we destroy the clone"
    /// contract explicit rather than relying on hierarchy destruction order.</summary>
    public void Destroy()
    {
        ForgetGameHighlight(); // drops the highlight material instances we minted for the clone
        _art?.Destroy();
        _art = null;
        Path = RemoteAbilityCardSource.FacePath.None;
        _shownId = int.MinValue;
        _shownOwner = int.MinValue;
        _shownEmpty = true;
        _shownFront = false;
    }

    /// <summary>Cheap, stable change key for "whose card is this" — the actor id, guarded because the
    /// actor can be mid-teardown. Only used to invalidate the slot when the owner changes.</summary>
    private static int OwnerKey(CPlayerActor? owner)
    {
        if (owner == null)
            return int.MinValue;
        try { return owner.ID; }
        catch { return int.MinValue; }
    }

    /// <summary>Readable card name: <c>CAbilityCard.Name</c> (localized YML name), stripped of the
    /// <c>ABILITY_CARD_</c> loc prefix when present (as the game's own <c>StrictName</c> does).
    /// Guarded — a YML lookup miss degrades to "?".</summary>
    private static string DisplayName(CAbilityCard card)
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
