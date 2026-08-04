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

    /// <summary>Tear the hosted face down and forget which path drew it (the back/empty states must
    /// never report a fidelity path they are not showing).</summary>
    private void ClearFace()
    {
        _art?.HideFront();
        Path = RemoteAbilityCardSource.FacePath.None;
    }

    /// <summary>Destroy the slot's face host. Called when the owning board is torn down: the host is
    /// a child of <c>_root</c> and dies with it either way, but going through
    /// <see cref="RemoteCardArt.Destroy"/> keeps the "we own the clone, we destroy the clone"
    /// contract explicit rather than relying on hierarchy destruction order.</summary>
    public void Destroy()
    {
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
