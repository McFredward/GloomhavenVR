// THE FAN HALF of the map room's loadout hand — the slabs, their faces, their pose, and the
// "take one into either hand" affordance.
//
// ─── WHY THIS REUSES THREE EXISTING MECHANISMS AND INVENTS NONE ───────────────────────────────
// Everything below is assembled out of parts that already ship, because each of them encodes a
// hardware round this project has already paid for:
//
//   1. THE SLAB. Cards.CardMesh.AttachBody(kind: Ability) — the PUNCHED-OUT card body, so a map-room
//      card has the same silhouette as a scenario card. A hand-built rectangle here would read as
//      the pre-silhouette full quad the moment the shared contour lands (Net/RemoteHandFan's Rebuild
//      documents exactly that regression). The body is sized to CardFace.VisibleFaceRect, not to the
//      nominal 63.5 x 88 mm, because the printed face is letterboxed inside it — leaving the body at
//      nominal is the reported "rim of card-back braid around every printed face" (report 12,
//      2026-08-15).
//
//   2. THE FACE. Net.RemoteAbilityCardSource.ShowFullFace(art, actor: null, card) — the POOLED
//      BORROW path (path B in that file). It spawns a widget from the game's own ObjectPool by card
//      ID onto an INACTIVE holder, Init()s it in CardHandMode.Preview, clones the face, and hands
//      the widget straight back in a finally. Path A (clone the live per-actor widget) cannot apply
//      here and is skipped by passing a null actor: there is no CPlayerActor in the map phase at
//      all — CMapCharacter.GetActor() (CMapCharacter.cs:1116) reads ScenarioManager.Scenario, which
//      is null here, so calling it would throw. The pooled path needs nothing but the CAbilityCard,
//      which is exactly what the loadout resolves to.
//
//      COST NOTE, and it is the reason ShowFullFace is called ONLY on a rebuild: the pooled path
//      opens with art.HideFront() and rebuilds the clone unconditionally (RemoteAbilityCardSource.cs
//      :447 — "the borrowed widget is transient, so its instance id carries no meaning across calls
//      and must never be allowed to dedup a genuinely new card"). Calling it per frame would
//      instantiate and destroy a full card widget per card per frame. The per-frame upkeep is
//      RemoteCardArt.MaintainMipBake, which is the cheap half — it catches the async header art the
//      instant it lands and re-runs the shared mip-bake on the 1 s cadence.
//
//   3. THE HOLD. Cards.CardBorrow, via IBorrowedCardSource — the mechanism built for user report 7
//      of 2026-08-15 ("Ich will auch in der Lage sein, dass man die fremden Handkarten jederzeit
//      auch in der Hand nehmen kann (inklusive Hand wechsel etc) damit man sie näher betrachten
//      kann. Nur interagieren oder umsortieren etc soll man nicht können."). That sentence is this
//      user's ruling 3 almost word for word, so this file adds a SOURCE to that mechanism instead of
//      writing a second one. Both hands work because CardBorrow's sweep runs over both
//      (CardBorrow.Tick's `for (int h = 0; h < 2; h++)`) and the copy is stamped AllowsGateHand.
//
// ─── THE INSPECTION-ONLY GUARANTEE (ruling 3) — WHAT MAKES STATE CHANGE IMPOSSIBLE ────────────
// It is STRUCTURAL, not a refusal, and the structure is CardBorrow's (its BeginBorrow doc comment
// enumerates it). Restated here for the map room, because the reasons differ in one place:
//
//   * The lifted object is a brand-new VRCard that no system owns. It is NOT registered with
//     VRCardFactory and is NEVER hooked by CardsDriver.HookCard, so its Released/Grabbed/Poked
//     events reach NO driver. Play, discard, the tray slots, the pile returns, SelectCard/
//     UnselectCard and the initiative reconcile are not merely refused — the routing does not exist.
//   * It is never added to any CardFan, so it has no fan membership to reorder.
//   * VRCard.InspectOnly is stamped on it and PokeSelectEnabled is left false, so it is not an
//     IPokeable and cannot be click-selected.
//   * Its face is a THROWAWAY CLONE (RemoteCardArt), made non-interactive — GraphicRaycasters
//     destroyed, a blocking CanvasGroup added — so a poke or a laser cannot reach the card widget's
//     action buttons through it.
//   * AND, THE MAP-ROOM-SPECIFIC ONE: this file never touches CMapCharacter.HandAbilityCardIDs. It
//     READS the list to resolve card models and never writes to it. The list is the loadout; the
//     only writers in the whole game are UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect
//     (:536) and .OnAbilityCardDeselect (:558), both of which follow the write with
//     Synchronizer.ReplicateControllableStateChange(GameActionType.ModifyCardInventory, …). Neither
//     is reachable from anything here.
//
// WHAT HAPPENS IF THE PLAYER TRIES ANYWAY: they lift a card, it follows the pinch, they can pass it
// to the other hand, and when they let go it glides back onto its slab in the fan and is destroyed
// (CardBorrow.End → BorrowedCardWatch.BeginReturn). It cannot be put down, cannot be dropped into a
// tray, cannot be inserted into a fan, and cannot be played. Two cards at once is refused by
// CardBorrow itself ("A live borrow owns the gesture"). Leaving the map room while holding one
// dissolves it (Release → CardBorrow.EndIfFrom).
//
// ─── NOTHING RE-ORIENTS WITH THE HEAD ─────────────────────────────────────────────────────────
// This is a HARD requirement of the feature, and it is the one place this file deliberately
// DIVERGES from Cards/CardFan. The scenario fan billboards its root at the head camera every frame
// (CardFan.cs:855-861, `Quaternion.LookRotation(_root.position - headPos, ...)`) and toes each card
// in at the head on top of that. NONE of that is here. The fan's pose is a pure function of the
// PALM ANCHOR:
//
//     position = palm.position + palm.up * (FanPalmOffset * hand.WorldScale)
//     rotation = LookRotation(forward: -palm.up, upwards: palm.forward)
//
// The palm frame is documented and MEASURED in Hands/HandRig.cs (Anchor_Palm's authored rotation
// (0, .7071, .7071, 0) is a half turn about (0,1,1)/√2, which maps palm +Y onto wrist +Z = OUT OF
// THE PALM and palm +Z onto wrist +Y = ALONG THE FINGERS). A card's +Z points AWAY from its reader
// (uGUI is read from -Z), so aiming the fan's forward at -palm.up puts the readable side toward
// whoever is looking at the palm — the owner — and using palm.forward as the up hint stands the
// cards up along the fingers. The two vectors are orthogonal by construction, so LookRotation can
// never degenerate here and needs no fallback. Turn your head and nothing moves; turn your hand and
// the whole fan turns with it.
//
// UNITS, again: FanPalmOffset is REAL METRES and is multiplied by hand.WorldScale to become a WORLD
// displacement — the identical product Cards/CardFan.cs:808 forms, with the identical reasoning
// (the palm anchor's own lossyScale is NOT usable: the bundle glove prefab hangs its anchors under
// an armature authored at localScale 100, so palm.lossyScale is WorldScale * 100 and using it put
// the scenario fan nine metres up the palm normal). The slabs themselves are parented under
// VRRigDriver.RigRoot, whose lossy scale IS the diorama scale, so their metre-sized meshes need no
// scale term at all.

using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    /// <summary>Unity's built-in "Ignore Raycast" layer — the one layer
    /// <c>Physics.DefaultRaycastLayers</c> excludes. The slab ROOTS live on it so their borrow
    /// trigger colliders can never become a <c>RayInteractor</c> world hit and steal a map-icon or
    /// furniture pick out of the beam. Verbatim the reasoning at the end of
    /// <c>Net/RemoteHandFan.Rebuild</c>; the renderer-carrying children keep the mod layer.</summary>
    private const int IgnoreRaycastLayer = 2;

    /// <summary>Per-card z stagger (metres) so the draw order across the arc is unambiguous —
    /// <c>CardFan.ZStagger</c>'s value.</summary>
    private const float ZStagger = 0.004f;

    private Transform? _root;
    private readonly List<GameObject> _slabs = new(MaxCards);
    private readonly List<RemoteCardArt> _faces = new(MaxCards);

    /// <summary>How many slabs are currently showing a real card front (state-line material).</summary>
    private int _frontsShown;

    /// <summary>The hand the fan is currently posed on — the NON-dominant one, the same hand the
    /// scenario fan opens on (<c>CardsDriver.2.Update.cs:969</c> picks it as "not
    /// <c>VRHands.Primary</c>"), so the map room and a scenario feel the same.</summary>
    private VRHand? _fanHand;

    /// <summary>Change gate for the one-shot pose line.</summary>
    private bool _posedOnce;

    internal bool HasFan => _root != null;

    // ==========================================================================================
    //  BUILD
    // ==========================================================================================

    /// <summary>
    /// Rebuild the slabs for <see cref="_loadout"/>. Called ONLY from <see cref="Reconcile"/>, i.e.
    /// only when the selected character or its loadout actually changed.
    /// </summary>
    private void RebuildFan()
    {
        ReleaseFan("the fan is being rebuilt for a new selection or loadout");

        int count = _loadout.Count;
        if (count == 0)
            return;

        var rootGo = new GameObject("GloomhavenVR.MapRoomHand");
        _root = rootGo.transform;
        // Parented to the RIG ROOT, not to the palm — see the header's UNITS note. The rig root
        // carries the diorama scale and nothing else, so a 63.5 mm card is 63.5 mm at the eye at
        // every map zoom, while the palm anchor's own lossyScale is 100x on the glove prefabs.
        Transform? rig = VRRigDriver.RigRoot;
        if (rig != null)
            _root.SetParent(rig, worldPositionStays: false);
        VRLayers.Apply(rootGo);

        float slabWidth = CardWidthMeters;
        float slabHeight = CardHeightMeters;
        Vector2 visible = CardFace.VisibleFaceRect(slabWidth, slabHeight);
        // Shared + cached by CardMesh (one material per CardBodyKind for the whole session) — never
        // destroyed here, and never instantiated per slab.
        Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability);

        float radius = Cfg(CardsConfig.FanEffectiveRadius, Defaults.FanEffectiveRadius);
        float sweep = Cfg(CardsConfig.FanArcSweepDegrees, Defaults.FanArcSweepDegrees);
        float perCard = Cfg(CardsConfig.FanPerCardStepDegrees, Defaults.FanPerCardStepDegrees);
        float step = count > 1 ? Mathf.Min(perCard, sweep / (count - 1)) : 0f;

        for (int i = 0; i < count; i++)
        {
            var slab = new GameObject($"Card{i}");
            slab.transform.SetParent(_root, worldPositionStays: false);

            // The slab ROOT stays UNIFORM: RemoteCardArt hangs a world-space canvas off it and a
            // non-uniform scale here would stretch the printed art. The non-uniform squash that
            // makes the BODY the same rectangle as the printed face rides one level down, exactly
            // as VRCard.SetCanvasSize fits its own backing.
            var body = new GameObject("Body");
            body.transform.SetParent(slab.transform, worldPositionStays: false);
            body.transform.localScale = new Vector3(
                visible.x / Mathf.Max(slabWidth, 1e-5f),
                visible.y / Mathf.Max(slabHeight, 1e-5f),
                1f);
            var mf = body.AddComponent<MeshFilter>();
            CardMesh.AttachBody(mf, CardBodyKind.Ability, slabWidth, slabHeight);
            var mr = body.AddComponent<MeshRenderer>();
            // The body mesh carries two submeshes (front+rim | back). The front is covered by the
            // printed face canvas, so the shared back material wears both slots — the same pair
            // Net/RemoteHandFan uses, and one less material to own.
            mr.sharedMaterials = new[] { back, back };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // THE AFFORDANCE (ruling 3). A trigger collider on the slab root plus the reach surface
            // CardBorrow's shared sweep elects over. Sized to the slab's VISIBLE STRIP rather than
            // the whole card, because the slabs overlap by 40-60 % in an arc and FanSweep's class
            // doc records what full-width colliders on overlapping cards do to a sweep (several
            // cards measure 0.0 cm at once, ties never switch the incumbent, cards get skipped).
            var borrow = slab.AddComponent<BorrowTarget>();
            borrow.Configure(this, i, visible.x, visible.y,
                FanSweep.StripWidth(count, radius, step, visible.x, 1f));

            _slabs.Add(slab);
            _faces.Add(new RemoteCardArt(slab.transform, slabWidth, slabHeight));
        }

        LayoutSlabs(count, radius, step);
        BuildFaces();

        // Mod layer for everything that renders (the owned head camera renders that layer only)…
        VRLayers.Apply(rootGo);
        // …and then the slab ROOTS back off it onto Ignore Raycast, so their trigger boxes are
        // invisible to RayInteractor's Physics.Raycast against DefaultRaycastLayers. See the
        // constant's own doc. The roots carry no renderer, so nothing leaves the head camera's mask.
        for (int i = 0; i < _slabs.Count; i++)
        {
            if (_slabs[i] != null)
                _slabs[i].layer = IgnoreRaycastLayer;
        }

        _posedOnce = false;
        PoseFan();   // pose before the first drawn frame, so the fan never appears at the rig origin
    }

    /// <summary>
    /// The arc, computed ONCE per rebuild. Deliberately static: this is the shape of a hand of
    /// cards, and nothing in it depends on the head, on time, or on where the player is looking.
    /// Same terms as <c>CardFan.Relayout</c>'s steady layout — angular step, radius, a
    /// curvature-by-fill arch and a roll tilt — minus every animated and head-derived term.
    /// </summary>
    private void LayoutSlabs(int count, float radius, float step)
    {
        float start = -step * (count - 1) * 0.5f;
        int maxHand = Mathf.Max(1, CfgInt(CardsConfig.FanMaxHandForCurve, Defaults.FanMaxHandForCurve));
        float fill = Mathf.Clamp01((float)count / maxHand);
        float arch = Cfg(CardsConfig.FanFlatCurvatureFactor, Defaults.FanFlatCurvatureFactor) * fill;
        float tilt = Cfg(CardsConfig.FanTiltFactor, Defaults.FanTiltFactor) * fill;

        for (int i = 0; i < _slabs.Count; i++)
        {
            GameObject slab = _slabs[i];
            if (slab == null)
                continue;
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            slab.transform.localPosition = new Vector3(
                Mathf.Sin(rad) * radius,
                (Mathf.Cos(rad) - 1f) * radius * arch,
                -ZStagger * i);
            slab.transform.localRotation = Quaternion.Euler(0f, 0f, -angle * tilt);
        }
    }

    /// <summary>
    /// Print the real card faces, ONCE per rebuild (see the header's COST NOTE). A slot the pool
    /// refuses keeps its card BACK, which is the fail-safe — never a blank white quad.
    /// </summary>
    private void BuildFaces()
    {
        _frontsShown = 0;
        for (int i = 0; i < _faces.Count && i < _loadout.Count; i++)
        {
            CAbilityCard card = _loadout[i];
            if (card == null)
                continue;
            try
            {
                // actor: null forces the POOLED path — there is no CPlayerActor in the map phase.
                // Nothing here re-checks a reveal gate, and that is correct rather than an omission:
                // RevealGate.ShowRoundCardFronts folds in RevealGate.InScenario, which is FALSE on
                // the map, so the gate is open by its own definition for every actor. The user's
                // "es gibt keine Geheimnisse in dieser Phase" is the game's own rule here.
                if (RemoteAbilityCardSource.ShowFullFace(_faces[i], null, card)
                    != RemoteAbilityCardSource.FacePath.None)
                {
                    _frontsShown++;
                }
            }
            catch (System.Exception ex)
            {
                VRLog.Debug(Scope, $"Map-room hand: card id {card.ID} kept its back ({ex.Message}).");
            }
        }
    }

    // ==========================================================================================
    //  PER FRAME
    // ==========================================================================================

    private void TickFan()
    {
        if (_root == null)
            return;
        PoseFan();
        // The async header art lands in a loader continuation some frames after the clone activates,
        // and the shared mip bake has a 1 s cadence on top of it. This is the cheap half of the face
        // upkeep — a reference compare per Image when nothing arrived (RemoteCardArt.MaintainMipBake).
        for (int i = 0; i < _faces.Count; i++)
            _faces[i].MaintainMipBake();
    }

    /// <summary>
    /// Put the fan on the hand. Pure function of the palm anchor — see the header. Hides the fan
    /// (rather than freezing it in the room) whenever the hand has no pose, so a controller that
    /// goes untracked takes its cards with it instead of leaving them floating.
    /// </summary>
    private void PoseFan()
    {
        if (_root == null)
            return;

        VRHand? hand = FanHand();
        Transform? palm = hand != null ? hand.Rig.PalmCenter : null;
        if (hand == null || !hand.HasPose || palm == null)
        {
            if (_root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
            return;
        }

        if (!ReferenceEquals(hand, _fanHand))
        {
            _fanHand = hand;
            _posedOnce = false;   // a hand swap re-states the pose line
        }

        Transform? rig = VRRigDriver.RigRoot;
        if (rig != null && _root.parent != rig)
            _root.SetParent(rig, worldPositionStays: true);

        if (!_root.gameObject.activeSelf)
            _root.gameObject.SetActive(true);

        // METRES x DIORAMA SCALE = WORLD UNITS. hand.WorldScale, never palm.lossyScale (the glove
        // prefab's anchors sit under a 100x armature) — Cards/CardFan.cs:795-808 documents the
        // nine-metre version of this mistake.
        float worldScale = hand.WorldScale;
        float standoffMeters = Cfg(CardsConfig.FanPalmOffset, Defaults.FanPalmOffset);
        Vector3 palmOut = palm.up;        // measured: Anchor_Palm +Y is out of the palm
        Vector3 fingers = palm.forward;   // measured: Anchor_Palm +Z is along the fingers

        _root.position = palm.position + palmOut * (standoffMeters * worldScale);
        // Card +Z points AWAY from the reader; the reader is on the palm side.
        _root.rotation = Quaternion.LookRotation(-palmOut, fingers);

        if (_posedOnce)
            return;
        _posedOnce = true;
        VRLog.Info(Scope,
            $"MAP-ROOM HAND POSE: fan on the {hand.Side} hand ({_slabs.Count} slab(s)), parented to the "
            + "rig root. Standoff " + $"{standoffMeters * 1000f:0.#} mm REAL (x diorama scale "
            + $"{worldScale:0.##} = {standoffMeters * worldScale:0.###} world units) up the palm "
            + "normal; the fan's readable side faces OUT OF THE PALM and its card tops run ALONG THE "
            + "FINGERS. NOTHING HERE READS THE HEAD: there is no billboard and no per-card toe-in, so "
            + "turning your head must not move a single card — if it does, something other than this "
            + "file is writing these transforms. Cards are "
            + $"{CardWidthMeters * 1000f:0.#} x {CardHeightMeters * 1000f:0.#} mm REAL at the eye, "
            + "which is scale-free by construction: the slabs are metre-sized meshes under a parent "
            + "whose lossy scale IS the diorama scale, so no world-unit product exists on that path.");
    }

    /// <summary>
    /// The fan-carrying hand: the NON-dominant one. Same rule as the scenario
    /// (<c>CardsDriver.2.Update.cs:969</c> — "not <c>VRHands.Primary</c>"), and the same hand
    /// <c>WorldUI.WristHud</c> puts its plate on, so the fan and the wrist info sit on one arm
    /// exactly as they do in a scenario. Either hand can still LIFT a card — that is
    /// <c>CardBorrow</c>'s sweep, which runs over both.
    /// </summary>
    private static VRHand? FanHand()
    {
        VRHand? primary = VRHands.Primary;
        if (primary == null)
            return VRHands.Left ?? VRHands.Right;
        return VRHands.Get(primary.Side == HandSide.Left ? HandSide.Right : HandSide.Left);
    }

    // ==========================================================================================
    //  RELEASE
    // ==========================================================================================

    /// <summary>Tear the fan down. Order matters: the borrowed copy points at a slab, the faces own
    /// cloned game widgets, and the borrow registrations must leave the sweep BEFORE the deferred
    /// Destroy so a rebuild never sweeps a slab that is on its way out this frame.</summary>
    private void ReleaseFan(string reason)
    {
        CardBorrow.EndIfFrom(this, reason);
        ReleaseBorrowFace();

        for (int i = 0; i < _slabs.Count; i++)
        {
            if (_slabs[i] != null)
                _slabs[i].GetComponent<BorrowTarget>()?.Retire();
        }

        for (int i = _faces.Count - 1; i >= 0; i--)
            _faces[i].Destroy();
        _faces.Clear();

        for (int i = _slabs.Count - 1; i >= 0; i--)
        {
            if (_slabs[i] != null)
                Object.Destroy(_slabs[i]);
        }
        _slabs.Clear();

        if (_root != null)
        {
            Object.Destroy(_root.gameObject);
            _root = null;
        }
        _frontsShown = 0;
        _fanHand = null;
        _posedOnce = false;
    }

    // ==========================================================================================
    //  IBorrowedCardSource — the map room's slabs, handed to the existing borrow mechanism
    // ==========================================================================================

    /// <summary>The borrowed copy's own printed face, and the card ID it was printed for. Kept here
    /// rather than reusing a slab's <see cref="RemoteCardArt"/> because the copy is a separate
    /// object with its own host transform and the slab keeps its own print for the whole hold.</summary>
    private RemoteCardArt? _borrowArt;
    private int _borrowCardId = int.MinValue;
    private Transform? _borrowHost;

    string IBorrowedCardSource.BorrowOwnerLabel =>
        _character != null ? DisplayName(_character) : "the selected character";

    /// <summary>Gold, not an avatar colour: these are the LOCAL player's own party's cards, so the
    /// owner-tint that tells a peer's card apart from your own has nothing to distinguish here.
    /// A near-white rim keeps the card reading as a card (CardBorrow.TintRim lerps 65 % toward
    /// this), which is what "look at it properly" wants.</summary>
    Color IBorrowedCardSource.BorrowTint => new(1f, 0.87f, 0.55f);

    /// <summary>
    /// THE MAP-ROOM GATE, and it is deliberately not a reveal gate. Asked on the grab AND on every
    /// frame of the hold (CardBorrow's BorrowedCardWatch.Update), so the copy dissolves in the frame
    /// the fan goes away — the player leaving the room, switching character, editing the loadout,
    /// or switching the feature off.
    ///
    /// <para>There is NO secrecy term here, and that is the user's ruling, not an omission: "es
    /// gibt keine Geheimnisse in dieser Phase". It also happens to be the game's own rule —
    /// RevealGate.ShowRoundCardFronts's conjunction contains RevealGate.InScenario, which is false
    /// on the map, so that gate returns true for every actor in this phase (RevealGate.cs:53-64).
    /// Adding a copy of it here would be a second statement of a rule that is already open, and the
    /// project has shipped the "two surfaces answer the same question differently" bug before.</para>
    /// </summary>
    bool IBorrowedCardSource.BorrowAllowed(int slot) =>
        !_failed
        && _engaged
        && MapRoomDriver.Active
        && MapRoomConfigOn
        && _root != null
        && slot >= 0
        && slot < _loadout.Count
        && slot < _slabs.Count
        && _loadout[slot] != null;

    string IBorrowedCardSource.BorrowGateLabel =>
        "the map room's own fan gate (room engaged AND [WorldUI] MapRoomHand on AND the slot still "
        + "indexes a resolved loadout card). NO secrecy term, by user ruling — and none is needed: "
        + "RevealGate.ShowRoundCardFronts is open off-scenario by its own definition";

    /// <summary>
    /// Print the borrowed copy's face. Called on the grab and then EVERY FRAME of the hold, so the
    /// dedup is load-bearing: without it the pooled-borrow path (which cannot dedup on its own —
    /// its source widget is a different instance on every call) would instantiate and destroy a
    /// full card widget every frame the player held a card.
    /// </summary>
    bool IBorrowedCardSource.ShowBorrowedFace(int slot, Transform host, float cardWidth, float cardHeight)
    {
        if (!((IBorrowedCardSource)this).BorrowAllowed(slot) || host == null)
            return false;

        CAbilityCard card = _loadout[slot];
        if (_borrowArt != null && _borrowCardId == card.ID && ReferenceEquals(_borrowHost, host))
        {
            _borrowArt.MaintainMipBake();   // the cheap per-frame half, same as the slabs
            return true;
        }

        ReleaseBorrowFace();
        try
        {
            var art = new RemoteCardArt(host, cardWidth, cardHeight);
            if (RemoteAbilityCardSource.ShowFullFace(art, null, card)
                == RemoteAbilityCardSource.FacePath.None)
            {
                art.Destroy();
                return false;
            }
            _borrowArt = art;
            _borrowCardId = card.ID;
            _borrowHost = host;
            return true;
        }
        catch (System.Exception ex)
        {
            VRLog.Debug(Scope, $"Map-room hand: the borrowed copy of card id {card.ID} could not be "
                + $"printed ({ex.Message}) — the grab is refused and nothing is left in the hand.");
            ReleaseBorrowFace();
            return false;
        }
    }

    void IBorrowedCardSource.ReleaseBorrowedFace() => ReleaseBorrowFace();

    /// <summary>Idempotent — CardBorrow's watch releases the face on destroy AND the fan releases it
    /// on teardown, and either may come first.</summary>
    private void ReleaseBorrowFace()
    {
        if (_borrowArt != null)
        {
            _borrowArt.Destroy();
            _borrowArt = null;
        }
        _borrowCardId = int.MinValue;
        _borrowHost = null;
    }

    // ==========================================================================================
    //  CONFIG READS
    // ==========================================================================================

    /// <summary>The card's real width in METRES — the player's own [Cards] CardWidth, so a map-room
    /// card is the size their scenario cards are.</summary>
    private static float CardWidthMeters => Cfg(CardsConfig.CardWidth, Defaults.CardWidth);

    /// <summary>The card's real height in METRES, at the printed 63.5 x 88 mm aspect —
    /// <c>CardsConfig.CardHeight</c>'s own derivation, restated so it survives an unbound dial.</summary>
    private static float CardHeightMeters => CardWidthMeters * (88f / 63.5f);

    /// <summary>Read a float dial, tolerating the window before <c>CardsConfig.Bind</c> has run (the
    /// map room can engage first). An unbound dial reads as the shipped default, so the fan looks
    /// the same either way.</summary>
    private static float Cfg(BepInEx.Configuration.ConfigEntry<float>? entry, float fallback)
        => entry != null ? entry.Value : fallback;

    private static int CfgInt(BepInEx.Configuration.ConfigEntry<int>? entry, int fallback)
        => entry != null ? entry.Value : fallback;
}
