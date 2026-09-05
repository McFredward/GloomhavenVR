using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Task #9 (empty-fan feedback): when the palm-roll gate opens but the hand has ZERO
/// cards, nothing used to appear — which read as "the fan is broken". This shows a
/// small ghost placard at the exact spot the fan would open (palm + FanPalmOffset,
/// facing the head like the fan does): a parchment-toned card-sized plate with a
/// localized "Keine Handkarten / No hand cards" line, fading out over ~1.5 s. Purely
/// visual — no colliders, no interaction, mod layer only. The driver edge-triggers
/// <see cref="Show"/> (once per gate opening, never mid-modal) and pumps
/// <see cref="Tick"/> every frame; unscaled time so a paused selection still fades.
///
/// <para>IT POSE-FOLLOWS ITS HAND FOR ITS WHOLE LIFETIME (user 2026-08-03: "Der 'Keine
/// Handkarten'-Hinweis geht nicht mit der Hand mit waehrend man sich bewegt"). The first
/// version wrote the placard's world pose ONCE, inside <see cref="Show"/>, and
/// <see cref="Tick"/> then only faded the alpha — so the plate stayed nailed to the world
/// point the palm happened to occupy at the gate edge. Standing still that is invisible
/// (1.5 s is short, the hand barely drifts); the moment the player MOVES it is not, and
/// stick FLIGHT made moving the normal case: the placard is left behind in mid-air, metres
/// from the hand that asked for it, which reads as a stray artefact rather than an answer.
/// <see cref="Place"/> is now re-run every tick, so position, scale AND the head-facing
/// billboard are recomputed from the LIVE palm — the plate rides the hand and stays
/// readable no matter how the player moves or turns while it fades.</para>
///
/// <para>It also ENDS cleanly instead of freezing: a tick that cannot resolve the palm
/// (hand untracked, controller put down, hands rebuilt) hides the placard outright rather
/// than leaving the last good pose hanging in the air, and the driver hides it the frame a
/// modal opens or the fan itself opens — an open fan has already answered the question the
/// placard was asking about.</para>
/// </summary>
internal sealed class EmptyFanHint
{
    // ---- THE PLACARD, ONE DEFINITION -----------------------------------------------------------
    //
    // A peer's copy of this plate (Net.RemoteEmptyFanHint, wire record 14 byte 1 bit 4) is the SAME
    // object seen from the other side of the table, and until 2026-09-05 it was built from EIGHT
    // hand-copied values: the fade length, the two alphas, the two colours, the plate's two size
    // factors and the text box's. THREE of them are Colors, which scripts/check-mirrors.sh (a
    // float/string extractor) cannot lint by construction — so half of this placard's look had no
    // checker at all and the other half had one that could not see it. Both carriers now build the
    // plate through Placard() and fade it through FadeAlpha()/ApplyAlpha(), so the two are
    // identical because they are the same expression rather than because two lists agree.
    //
    // WHAT IS DELIBERATELY *NOT* SHARED: the WORDS' language. Both sides compose them from the
    // VIEWER's localization (Loc.Mod("no_hand_cards")), which is a standing ruling and not a gap —
    // see RemoteEmptyFanHint's header: a German player's placard reads "No hand cards" to an
    // English peer, because only a key travels here, never the text.

    /// <summary>Fade length, seconds — how long the placard stays up. A peer's mirror runs the same
    /// ~1.5 s on ITS own clock from ITS own rising edge, so no animation rides the wire.</summary>
    internal const float FadeSeconds = 1.5f;

    /// <summary>Parchment plate opacity at full strength (before the fade and before the MR opaque
    /// bump).</summary>
    internal const float PlateAlpha = 0.55f;

    /// <summary>Ink opacity at full strength.</summary>
    internal const float TextAlpha = 0.95f;

    /// <summary>Plate width as a multiple of the card width — a low, placard-like strip.</summary>
    internal const float PlateWidthFactor = 1.1f;

    /// <summary>Plate height as a multiple of the card height.</summary>
    internal const float PlateHeightFactor = 0.42f;

    /// <summary>The ink's fit box height as a multiple of the card height (TmpFit caps the font
    /// from the box, so this is what sets the text size).</summary>
    internal const float TextBoxHeightFactor = 0.34f;

    /// <summary>How far the ink sits proud of the plate, toward the viewer (−Z, like the cards).</summary>
    internal const float InkProudZ = -0.002f;

    internal static readonly Color Parchment = new(0.85f, 0.78f, 0.62f);
    internal static readonly Color InkBrown = new(0.24f, 0.17f, 0.10f);

    /// <summary>The fade CURVE: ease-in on the elapsed fraction — holds readable, then ghosts
    /// away. One expression, so the two plates ramp down together off the same shape.</summary>
    internal static float FadeAlpha(float p) => 1f - p * p;

    /// <summary>
    /// Build the placard's two pieces — the collider-free parchment plate and the ink line — under
    /// <paramref name="root"/>, sized from a card metric. The CALLER owns everything that is a
    /// statement about which placard this is: the local one ranks both halves on
    /// <c>WorldUI.FreeLabelOrder</c>, the mirror re-proves its subtree is collider-free through the
    /// remote board's loud sweep, and each writes its own text.
    /// </summary>
    /// <param name="root">The placard root; both pieces are parented under it.</param>
    /// <param name="cardWidth">Card width in metres — the OWNER's, on both sides (a peer whose
    /// cards are bigger gets a bigger plate, exactly as they see it).</param>
    /// <param name="cardHeight">Card height in the same metres.</param>
    /// <param name="colliderDestroyImmediate">true where a deferred destroy would still be visible
    /// to a collider sweep running this frame — the remote board's case, which is why it is a
    /// parameter and not a second spelling of this method.</param>
    /// <param name="plate">The plate object, so a caller can rank or measure it.</param>
    /// <param name="plateMaterial">The plate's own material instance, or null if no sprite shader
    /// resolved; the fade writes its alpha.</param>
    /// <param name="label">The ink line, fitted but with NO text written yet.</param>
    internal static void Placard(Transform root, float cardWidth, float cardHeight,
                                 bool colliderDestroyImmediate,
                                 out GameObject plate, out Material? plateMaterial,
                                 out TextMeshPro label)
    {
        // Parchment plate: a collider-free alpha-blended quad (deliberately NOT the additive
        // slot-glow material — parchment must read as a soft solid, and the dark ink line must sit
        // legibly on it).
        plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Collider box = plate.GetComponent<Collider>();
        if (colliderDestroyImmediate)
            Object.DestroyImmediate(box);
        else
            Object.Destroy(box);
        plate.transform.SetParent(root, worldPositionStays: false);
        plate.transform.localScale =
            new Vector3(cardWidth * PlateWidthFactor, cardHeight * PlateHeightFactor, 1f);
        VRLayers.Apply(plate);
        plateMaterial = null;
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            plateMaterial = new Material(shader)
            {
                color = new Color(Parchment.r, Parchment.g, Parchment.b, PlateAlpha),
            };
            plate.GetComponent<MeshRenderer>().sharedMaterial = plateMaterial;
        }

        // Ink line, slightly proud of the plate (toward the viewer = -Z, like the cards).
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(root, worldPositionStays: false);
        textGo.transform.localPosition = new Vector3(0f, 0f, InkProudZ);
        VRLayers.Apply(textGo);
        label = textGo.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(InkBrown.r, InkBrown.g, InkBrown.b, TextAlpha);
        TmpFit.Fit(label, cardWidth, cardHeight * TextBoxHeightFactor, maxFontSize: 0f, wrap: false);
    }

    /// <summary>
    /// Write the fade level onto one placard's plate and ink — <paramref name="k"/> is the curve's
    /// output, 1 at full strength and 0 gone.
    ///
    /// <para>MR READABILITY: the 0.55 parchment is the weakest backing in the mod and the room
    /// bleeds straight through it in see-through mode, so while MR is on the plate runs opaque (the
    /// fade tail still applies, so it still ghosts away). Alpha is rewritten every frame, so the
    /// OFF state restores itself the next tick and this site keeps its own fade authority — no
    /// registration with MrBacking. It applies to a PEER's placard too: passthrough is a fact about
    /// the person looking, not about whose hand the plate hangs on.</para>
    /// </summary>
    internal static void ApplyAlpha(Material? plateMaterial, TextMeshPro? label, float k)
    {
        if (plateMaterial != null)
        {
            Color c = plateMaterial.color;
            c.a = (WorldUI.MrBacking.WantOpaque ? 1f : PlateAlpha) * k;
            plateMaterial.color = c;
        }
        if (label != null)
        {
            Color c = label.color;
            c.a = TextAlpha * k;
            label.color = c;
        }
    }

    private GameObject? _root;
    private Material? _plateMaterial;
    private TextMeshPro? _label;
    private float _elapsed = -1f; // -1 = idle

    /// <summary>
    /// TRUE WHILE THE PLACARD IS ACTUALLY ON SCREEN — the seam the multiplayer sender reads
    /// (record 14, byte 1, <see cref="Net.NetProtocol.HalfEmptyFanHintBit"/>).
    ///
    /// <para>STATIC because the hint instance is a private of <c>CardsDriver</c>, exactly like
    /// <see cref="PileViewer.CurrentCounts"/> and for the same reason: the wire sampler must be
    /// able to ask "is this display up?" without a handle on the driver's internals.</para>
    ///
    /// <para>IT IS THE RENDERED STATE, NOT A RE-DERIVATION. That is the whole point of putting it
    /// here rather than re-testing the hand in the sampler: the recorded trap
    /// (<c>INVARIANTS-Net-Rig.md</c>) is that the hand-card COUNT cannot distinguish "the gate
    /// opened onto an empty hand" from "the fan is closed", so any inference from the count would
    /// flash a placard on every peer's screen whenever a player lowers an empty hand. This flag is
    /// set and cleared by the same code that shows and hides the plate, so the wire bit and the
    /// picture cannot disagree.</para>
    /// </summary>
    internal static bool CurrentlyShown { get; private set; }

    /// <summary>
    /// The hand the placard belongs to, held for the whole fade so <see cref="Tick"/> can
    /// re-derive the pose from the LIVE palm. Cleared by <see cref="Hide"/>, so an idle
    /// hint never keeps a stale hand reference alive across a hands rebuild.
    /// </summary>
    private VRHand? _hand;

    /// <summary>Place the placard at the would-be fan position and start the fade.</summary>
    internal void Show(VRHand hand)
    {
        if (hand == null)
            return;
        EnsureBuilt();
        if (_root == null)
            return;

        _hand = hand;
        if (!Place())
        {
            _hand = null;
            return;
        }

        if (_label != null)
        {
            // Re-read per show: cheap, and it follows a live language switch. ONE key, so the
            // words over your own hand and the words over your teammate's cannot differ.
            _label.text = Loc.Mod("no_hand_cards");
        }
        _elapsed = 0f;
        ApplyAlpha(1f);
        _root.SetActive(true);
        CurrentlyShown = true;   // the wire bit and the picture are set by the same statement
    }

    /// <summary>
    /// Advance the fade (no-op while idle) AND re-pose the placard on its hand; hides itself
    /// when the fade finishes or the hand stops being resolvable.
    /// </summary>
    internal void Tick()
    {
        if (_elapsed < 0f || _root == null)
            return;

        // Pose FIRST, fade second: a frame is never presented at last frame's world point.
        // A hand that cannot be resolved any more (tracking lost, controller put down, the
        // VRHand instance rebuilt) ends the hint instead of parking it in mid-air — the fade
        // tail is not worth a placard that no longer belongs to anything the player can see.
        if (!Place())
        {
            VRLog.Info("Cards", "Empty-fan hint: its hand stopped being trackable mid-fade — " +
                                "the placard is hidden rather than left behind at the last " +
                                "known palm position.");
            Hide();
            return;
        }

        _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float p = Mathf.Clamp01(_elapsed / FadeSeconds);
        if (p >= 1f)
        {
            Hide();
            return;
        }
        ApplyAlpha(FadeAlpha(p)); // ease-in fade: holds readable, then ghosts away
    }

    /// <summary>Hide immediately (fade finished / a modal or the fan itself opened mid-fade).</summary>
    internal void Hide()
    {
        _elapsed = -1f;
        _hand = null;
        CurrentlyShown = false;
        if (_root != null)
            _root.SetActive(false);
    }

    internal void Destroy()
    {
        _elapsed = -1f;
        _hand = null;
        CurrentlyShown = false;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _plateMaterial = null;
        _label = null;
    }

    // ------------------------------------------------------------------ internals --

    /// <summary>
    /// Write the placard's world pose from the CURRENT palm — position, uniform scale and the
    /// head-facing billboard. Called from <see cref="Show"/> and from every <see cref="Tick"/>,
    /// which is the whole "it follows the hand" behaviour: nothing here is cached between
    /// frames, so it is correct after any amount of player movement, flight or turning.
    ///
    /// <para>Returns false when the hand cannot be resolved this frame (destroyed VRHand,
    /// tracking lost, no palm anchor). The caller treats that as end-of-life rather than
    /// keeping the previous pose, because a placard that stops following is exactly the bug
    /// this method exists to fix.</para>
    /// </summary>
    private bool Place()
    {
        if (_root == null || _hand == null || !_hand.HasPose)
            return false;
        Transform? palm = _hand.Rig.PalmCenter;
        if (palm == null)
            return false;

        // Same spawn math as CardFan.Tick's palm target: FanPalmOffset up the palm
        // normal, scaled by the true diorama scale (NOT palm.lossyScale — the glove
        // armature carries a 100x that would fling the placard out of view).
        float scale = Mathf.Max(_hand.WorldScale, 1e-4f);
        Vector3 pos = palm.position + palm.up * (CardsConfig.FanPalmOffset.Value * scale);
        _root.transform.position = pos;
        _root.transform.localScale = Vector3.one * scale;

        // Billboard re-aimed every frame too, not just at spawn: the fade is long enough for
        // the player to fly past or turn around the spot, and a placard frozen at its birth
        // orientation goes edge-on (unreadable, then invisible) exactly while they do that.
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = pos - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                _root.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        return true;
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject("GloomhavenVR.EmptyFanHint");
        Object.DontDestroyOnLoad(_root);
        VRLayers.Apply(_root);

        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        Placard(_root.transform, w, h, colliderDestroyImmediate: false,
                out GameObject plate, out _plateMaterial, out TextMeshPro label);
        _label = label;

        // PERSPECTIVE (same class as the pile fan titles — WorldUI.FreeLabelOrder): this placard
        // hangs at the palm with nothing but the room behind it, and both halves were transparent,
        // depth-less and parked at sortingOrder 0, so any converted panel painted over them however
        // far away it was. Plate and ink take the SAME ladder order: within one order Unity falls
        // back to renderQueue then distance, and the ink sits 2 mm proud of the plate, so their
        // relative draw order is unchanged from the shipped picture.
        WorldUI.FreeLabelOrder.Rank(plate);
        WorldUI.FreeLabelOrder.Rank(_label);

        _root.SetActive(false);
    }

    private void ApplyAlpha(float k) => ApplyAlpha(_plateMaterial, _label, k);
}
