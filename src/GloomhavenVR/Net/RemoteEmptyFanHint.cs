using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S "KEINE HANDKARTEN" PLACARD — the mirror of <c>Cards.EmptyFanHint</c>.
///
/// ─── THE GAP THIS CLOSES ───────────────────────────────────────────────────────────────────────
/// The placard was the one control-board-adjacent display that was not mirrored at all. It is
/// HAND-anchored rather than board furniture, so none of the board records carried it: a peer who
/// rolled their palm open onto an empty hand got a clear answer on their own screen and produced
/// absolutely nothing on anyone else's. Under the standing 1:1 ruling ("alle Interaktionen,
/// Animationen und Anzeigen … so wie der Spieler sie sieht") that is a display of theirs, and it
/// travels — as ONE BIT (record 14, byte 1, <see cref="NetProtocol.HalfEmptyFanHintBit"/>).
///
/// WHY A BIT AND NOT AN INFERENCE. The hand-card COUNT cannot say this: 0 cards with no fan flag is
/// equally "the gate opened onto an empty hand" (placard) and "the fan is closed" (nothing), which
/// is the state of every idle player at the table. Deriving it would have flashed a placard on
/// every peer's hand every time they lowered an empty one.
///
/// ─── WHAT IS SYNCED AND WHAT IS DERIVED ────────────────────────────────────────────────────────
///   • SYNCED: that the placard is up, as a LEVEL.
///   • DERIVED — the ANCHOR: <see cref="RemoteHandFan.FanAnchorPoint"/>, i.e. the owner's own palm
///     plus their own <c>FanPalmOffset</c> (which rides record 28 when they have tuned it). That is
///     the same point the owner's placard uses, and it is the point their FAN would have opened at
///     — which is the whole meaning of the plate.
///   • DERIVED — the FADE: this mirror runs its OWN ~1.5 s ease-in fade from its OWN rising edge and
///     follows the bit down if the owner's plate is cut short. Synced state, locally animated: the
///     wanted-glow blink's contract, and it keeps an animation off the wire.
///   • DERIVED — the TEXT: composed from THIS client's localization, not the sender's. The line is a
///     fixed two-word string with an exact local equivalent, unlike the pick banner's composed
///     sentence (which names an actor and a count and therefore has to travel). Same choice, same
///     reason, as record 24's prompt-text variant. Consequence, deliberate: a German player's
///     placard reads "No hand cards" to an English peer.
///
/// FACING: billboarded at the LOCAL head, the convention every remote LABEL on this layer follows
/// (<see cref="RemoteNameTag"/>, OwnerTag, PingNameTag) — a quad + TMP read from their −Z side, so
/// a placard aimed at its owner's head would be an invisible back for everybody the message is for.
/// The owner's own plate aims at the owner because they are the only reader of theirs; this one has
/// a different reader and aims at them.
///
/// INERT, like everything else on this layer: the plate primitive's collider is destroyed at
/// creation, nothing is registered with the laser/poke/grab systems, and the whole object is on the
/// mod layer.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (record 14, byte 1 bit 4 — ONE bit, no payload bytes at all when
/// the record is already riding) + VR-ONLY-derived (anchor, fade, text). See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteEmptyFanHint
{
    /// <summary>Fade length, matched to <c>Cards.EmptyFanHint.FadeSeconds</c> so a peer's plate
    /// lasts exactly as long as the real one.</summary>
    private const float FadeSeconds = 1.5f;

    private const float PlateAlpha = 0.55f;
    private const float TextAlpha = 0.95f;

    private static readonly Color Parchment = new(0.85f, 0.78f, 0.62f);
    private static readonly Color InkBrown = new(0.24f, 0.17f, 0.10f);

    private readonly RemoteAvatar _owner;

    private GameObject? _root;
    private Material? _plateMaterial;
    private TextMeshPro? _label;
    private float _elapsed = -1f;   // -1 = idle
    private bool _wasShown;         // rising-edge detector on the synced level
    private string _labelLanguage = string.Empty;

    internal RemoteEmptyFanHint(RemoteAvatar owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Drive the mirror from the owner's synced level. Runs every frame; self-early-returns and
    /// allocation-free while nothing is shown, which is almost always.
    /// </summary>
    internal void Tick()
    {
        bool shown = _owner.EmptyFanHint;

        // RISING EDGE — the owner's palm gate just opened onto an empty hand. Restart OUR fade
        // here rather than tracking theirs on the wire: the durations are the same constant on
        // both machines, and an edge that pre-empts their send gate lands within one packet.
        if (shown && !_wasShown)
        {
            _elapsed = 0f;
            // Change-gated by construction: this is the RISING EDGE of a human-paced gesture (a
            // palm gate opening), so it can never become per-frame spam.
            VRLog.Info("Net", $"Empty-fan placard RECEIVED from player {_owner.PlayerId} " +
                              "(record 14, byte 1 bit 4): their palm gate opened on a hand the " +
                              "GAME model says is empty, so their \"Keine Handkarten\" plate is " +
                              "up — mirrored here at their own fan spot, faded on this client's " +
                              "clock over 1.5 s, worded in THIS client's language.");
        }
        // FALLING EDGE — their plate was cut short (a modal opened, the fan opened, the hand was
        // lowered). Follow it down instead of finishing our own fade: the bit is the authority.
        else if (!shown && _wasShown)
        {
            Hide();
        }
        _wasShown = shown;

        if (_elapsed < 0f)
            return;

        if (!Place())
        {
            // The peer's hand stopped being resolvable mid-fade (their hand holder went inactive,
            // the avatar was rebuilt). End it rather than parking a plate in mid-air — the same
            // ruling the local placard follows.
            Hide();
            return;
        }

        // THE FADE IS THE OWNER'S, RUN ON OUR CLOCK. Their bit stays SET for the whole ~1.5 s of
        // their own fade (Cards.EmptyFanHint clears CurrentlyShown only when it hides), so the two
        // plates ramp down together off the same curve and the same constant — the wanted-glow
        // contract: synced state, locally animated, zero per-frame traffic. Whichever ends first
        // ends this one: our own curve completing, or their bit clearing (handled as the falling
        // edge above, which is what makes a placard cut short by a modal disappear here too).
        _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float p = Mathf.Clamp01(_elapsed / FadeSeconds);
        if (p >= 1f)
        {
            Hide();
            return;
        }
        ApplyAlpha(1f - p * p);   // ease-in: holds readable, then ghosts away — their curve
    }

    /// <summary>Hide immediately (fade finished, hand lost, or the owner's bit cleared).</summary>
    internal void Hide()
    {
        _elapsed = -1f;
        if (_root != null)
            _root.SetActive(false);
    }

    internal void Destroy()
    {
        _elapsed = -1f;
        _wasShown = false;
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
    /// Pose the placard from the peer's LIVE palm — position, uniform scale, head-facing billboard —
    /// exactly as the owner's own placard re-poses itself every tick (it must ride their hand while
    /// they move, which is the defect that made the local one re-place per frame in the first
    /// place). False when their hand cannot be resolved this frame; the caller then ends the plate.
    /// </summary>
    private bool Place()
    {
        Transform? holder = _owner.NonDominantHandHolder != null
            ? _owner.NonDominantHandHolder
            : _owner.Root;
        if (holder == null || !holder.gameObject.activeInHierarchy)
            return false;

        EnsureBuilt();
        if (_root == null)
            return false;

        // The SAME anchor the peer's fan would have opened at — their palm plus their own
        // FanPalmOffset, scaled by their applied diorama scale (RemoteHandFan.FanAnchorPoint is the
        // one anchor surface on this layer, deliberately: a standoff without the frame it is
        // measured in is what put card flights behind peers' hands once already).
        Vector3 pos = RemoteHandFan.FanAnchorPoint(_owner, holder);
        _root.transform.position = pos;
        _root.transform.localScale = Vector3.one * Mathf.Max(_owner.AppliedScale, 1e-4f);

        // Language follows a live switch on THIS client, like the local placard's per-show re-read.
        if (_label != null && _labelLanguage != Loc.CurrentLanguage)
        {
            _labelLanguage = Loc.CurrentLanguage;
            _label.text = _labelLanguage == "German" ? "Keine Handkarten" : "No hand cards";
        }

        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = pos - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                _root.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        _root.SetActive(true);
        return true;
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject($"GloomhavenVR.RemoteEmptyFanHint[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        VRLayers.Apply(_root);

        // Sized from the OWNER's own card width (record 28 where they tuned it, this client's
        // shipped constant where they did not) — the local plate is sized from theirs, so a peer
        // whose cards are bigger gets a bigger plate, exactly as they see it.
        float w = _owner.BoardTuning.CardWidth > 0.001f
            ? _owner.BoardTuning.CardWidth
            : RemoteHandFan.DefaultCardWidth;
        float h = w * (88f / 63.5f);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        // Inert by construction — DestroyImmediate so the board's loud StripColliders sweep can
        // never meet it (a deferred destroy is still visible to a sweep run this frame).
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.transform.SetParent(_root.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(w * 1.1f, h * 0.42f, 1f);
        VRLayers.Apply(plate);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            _plateMaterial = new Material(shader)
            {
                color = new Color(Parchment.r, Parchment.g, Parchment.b, PlateAlpha),
            };
            plate.GetComponent<MeshRenderer>().sharedMaterial = _plateMaterial;
        }

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(_root.transform, worldPositionStays: false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        VRLayers.Apply(textGo);
        _label = textGo.AddComponent<TextMeshPro>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = new Color(InkBrown.r, InkBrown.g, InkBrown.b, TextAlpha);
        _labelLanguage = Loc.CurrentLanguage;
        _label.text = _labelLanguage == "German" ? "Keine Handkarten" : "No hand cards";
        TmpFit.Fit(_label, w, h * 0.34f, maxFontSize: 0f, wrap: false);

        // BELT AND BRACES, as everywhere on this layer: the plate primitive's own collider is
        // destroyed above, and this re-proves the whole subtree is collider-free at runtime and
        // says so loudly if it ever is not. Nothing here is registered with the laser, the poke
        // router or the grab system, and nothing implements IPokeable/IGrabbable — a peer's
        // placard is a picture.
        RemoteBoardFurniture.StripColliders(_root, "RemoteEmptyFanHint");

        _root.SetActive(false);
    }

    private void ApplyAlpha(float a)
    {
        if (_plateMaterial != null)
        {
            Color c = _plateMaterial.color;
            c.a = PlateAlpha * a;
            _plateMaterial.color = c;
        }
        if (_label != null)
        {
            Color c = _label.color;
            c.a = TextAlpha * a;
            _label.color = c;
        }
    }
}
