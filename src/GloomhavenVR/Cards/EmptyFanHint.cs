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
    private const float FadeSeconds = 1.5f;
    private const float PlateAlpha = 0.55f;
    private const float TextAlpha = 0.95f;

    private static readonly Color Parchment = new(0.85f, 0.78f, 0.62f);
    private static readonly Color InkBrown = new(0.24f, 0.17f, 0.10f);

    private GameObject? _root;
    private Material? _plateMaterial;
    private TextMeshPro? _label;
    private float _elapsed = -1f; // -1 = idle

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
            // Re-read per show: cheap, and it follows a live language switch.
            _label.text = Loc.CurrentLanguage == "German" ? "Keine Handkarten" : "No hand cards";
        }
        _elapsed = 0f;
        ApplyAlpha(1f);
        _root.SetActive(true);
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
        ApplyAlpha(1f - p * p); // ease-in fade: holds readable, then ghosts away
    }

    /// <summary>Hide immediately (fade finished / a modal or the fan itself opened mid-fade).</summary>
    internal void Hide()
    {
        _elapsed = -1f;
        _hand = null;
        if (_root != null)
            _root.SetActive(false);
    }

    internal void Destroy()
    {
        _elapsed = -1f;
        _hand = null;
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

        // Parchment plate: a collider-free alpha-blended quad (deliberately NOT the
        // additive slot-glow material — parchment must read as a soft solid, and the
        // dark ink line must sit legibly on it).
        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Object.Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(_root.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(w * 1.1f, h * 0.42f, 1f); // low, placard-like strip
        VRLayers.Apply(plate);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            _plateMaterial = new Material(shader) { color = new Color(Parchment.r, Parchment.g, Parchment.b, PlateAlpha) };
            plate.GetComponent<MeshRenderer>().sharedMaterial = _plateMaterial;
        }

        // Ink line, slightly proud of the plate (toward the viewer = -Z, like the cards).
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(_root.transform, worldPositionStays: false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        VRLayers.Apply(textGo);
        _label = textGo.AddComponent<TextMeshPro>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = new Color(InkBrown.r, InkBrown.g, InkBrown.b, TextAlpha);
        TmpFit.Fit(_label, w * 1.0f, h * 0.34f, maxFontSize: 0f, wrap: false); // cap from box height

        _root.SetActive(false);
    }

    private void ApplyAlpha(float k)
    {
        if (_plateMaterial != null)
        {
            Color c = _plateMaterial.color;
            // MR readability: the 0.55 parchment is the weakest backing in the mod — the room
            // bleeds straight through it in see-through mode. While MR is on the plate runs
            // opaque (the fade tail still applies, so it still ghosts away); alpha is rewritten
            // here every frame, so the OFF state restores itself the next tick (no registration
            // with MrBacking — this site keeps its own fade authority).
            c.a = (WorldUI.MrBacking.WantOpaque ? 1f : PlateAlpha) * k;
            _plateMaterial.color = c;
        }
        if (_label != null)
        {
            Color c = _label.color;
            c.a = TextAlpha * k;
            _label.color = c;
        }
    }
}
