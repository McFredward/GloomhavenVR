using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S PICK-STATUS PLACARD — the hovering parchment strip above their control board that
/// reads e.g. "Barbar: Wähle 1 Karte(n) zum Verlieren".
///
/// WHY IT RIDES THE WIRE AT ALL (user request 2026-08-03: "Dieser Text soll auch synchronisiert
/// werden an der jeweiligen richtigen Position im MP"): unlike everything else on the remote board
/// this line is not derivable from replicated game state. It is composed by the OWNER's
/// <c>CardsDriver.UpdatePickStatus</c> out of their hand's live pick mode, the requested count,
/// how many of the current batch they have already placed and whether the game's confirm dialog is
/// up — several of which are local UI state that exists nowhere else. So it is sent verbatim as
/// extension record <see cref="NetProtocol.ExtIdPickBanner"/>.
///
/// WHAT IT DOES NOT LEAK: an actor label and a count — "choose 1 card to lose" — never WHICH card.
/// That is strictly less than the game's own turn banner already tells everyone, and it is the
/// same standing rule the rest of this layer follows (no card identity on the wire; reveals only
/// through <see cref="RevealGate"/>). Nothing here consults a card.
///
/// LANGUAGE: the line arrives already composed, in the SENDER's language, and is shown verbatim.
/// Re-composing it locally would mean shipping the pick mode, the counts and the actor identity as
/// structured fields and rebuilding the sentence — more wire, more coupling, and the result would
/// still name the peer's character. It is their board; it reads in their language.
///
/// WHERE IT SITS: <see cref="RemoteBoardLayout.PickBannerMount"/>, i.e. the owner's own
/// <c>PlayTray.PickBannerBase</c> plus THEIR OWN per-board offset — from extension record 28 when
/// they have moved that dial, and from the shipped constant for their synced board style when they
/// have not (the same derivation every other dock on this board uses). The DELIBERATELY-NOT note
/// that used to stand here — "a peer's private re-tuning of that offset never rides the wire" — was
/// retired with record 28: under the 1:1 ruling a placard the owner has moved must sit where they
/// moved it on every screen.
///
/// LOOK: the owner's own placard is a parchment quad with ink-brown text
/// (<c>PlayTray.EnsurePickBanner</c>) — reproduced here with the same colours through the shared
/// remote-board helpers, so a peer's placard reads like theirs rather than like a mod overlay.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 7) — sender-composed local UI state that is
/// neither GLOBAL nor PER-ACTOR MODEL, for the reason given above. Its sibling is the board
/// tooltip (<see cref="RemoteBoardTooltip"/>, record 9), which shares the shape but adds a
/// sender-side identity gate. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemotePickBanner
{
    // THE THREE NUMBERS ARE THE OWNER'S, READ — NOT COPIED. Until now this class carried its own
    // 0.44 × 0.055, 0.42 × 0.048 and 0.30, with a doc comment stating they were the owner's values
    // and nothing checking that they still were. Under the 1:1 ruling a peer's placard must look
    // the way it looks for its owner, and "look" includes its SIZE; a mirror that is allowed to
    // drift is how this defect comes back. scripts/check-mirrors.sh exists to catch exactly that
    // kind of pair — and its own header keeps saying that DELETING the second copy beats linting
    // it, which is what these three aliases do: one value, one place to tune, nothing to drift.

    /// <summary>Plate size, metres — the owner's own authored placard size, and here as there a
    /// MINIMUM the drawn text can grow (<see cref="Apply"/>).</summary>
    private static readonly Vector2 PlateSize = Cards.PlayTray.PickPlateSize;

    /// <summary>Text box inside the plate — the owner's own fit box and font ceiling.</summary>
    private static readonly Vector2 TextBox = Cards.PlayTray.PickTextBox;
    private const float MaxFont = Cards.PlayTray.PickMaxFont;

    private readonly Transform _root;
    private readonly Transform _plate;
    private readonly TextMeshPro _label;
    private string _shown = string.Empty;

    public RemotePickBanner(Transform boardRoot, in RemoteBoardLayout layout)
    {
        _root = new GameObject("PickStatusBanner").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = layout.PickBannerMount;

        // Parchment plate, seated slightly BEHIND the text toward the board — the same 4 mm the
        // owner's own placard uses, so the text never z-fights its backing.
        MeshRenderer plate = BoardVisual.Quad(_root, "Plate", PlateSize,
            BoardVisual.Unlit(new Color(0.85f, 0.78f, 0.62f, 0.85f)));
        plate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        _plate = plate.transform;   // grown to contain the drawn text, exactly as the owner's is
        // MR readability (user: the text backing must appear on REMOTE boards exactly as on the
        // owner's): the owner's own placard parchment is Opacified (PlayTray.5.Status), so this
        // mirror's 0.85 parchment gets the identical treatment — alpha 1 while MR is on, restored
        // exactly on off. Mod-owned material; normal mode stays bit-identical.
        WorldUI.MrBacking.Opacify(plate.sharedMaterial);

        _label = RemoteBoardContent.Label(_root, "Label", Vector3.zero, TextBox, MaxFont,
            new Color(0.24f, 0.17f, 0.10f), TextAlignmentOptions.Center, wrap: true);

        // DRAW ORDER is NOT set here any more: the placard is seated by the owning board's cluster
        // sweep at the tier its own board-local depth earns (BoardVisual.AdoptBoardOrder). It still
        // lands UNDER the peer's initiative mirror at every viewing angle — the placard hovers at
        // PickBannerBase z = −0.02 and the track is docked at −0.048/−0.070 — but now because that
        // IS the geometry, not because a constant asserted it.
        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Push the peer's line (null/empty = their placard is hidden, which is also what a sender
    /// predating record 7 produces). Change-gated on both the active flag and the text: a TMP
    /// rewrite re-triggers auto-size layout every time, which is the churn the local board's own
    /// banner is careful to avoid.
    /// </summary>
    public void Apply(string? line)
    {
        bool show = !string.IsNullOrEmpty(line);
        if (!show)
        {
            if (_shown.Length != 0)
            {
                _shown = string.Empty;
                if (_root != null && _root.gameObject.activeSelf)
                    _root.gameObject.SetActive(false);
                VRLog.Info("Net", "Remote pick banner: hidden (record absent — the owner's placard is down).");
            }
            return;
        }
        if (line == _shown)
            return;
        _shown = line!;
        RemoteBoardContent.SetText(_label, _shown);

        // THE 2026-09-02 SCREENSHOT'S ACTUAL DEFECT, MADE AUDIBLE. The user reported a placard whose
        // sentence stops mid-word ("…eine Karte zum Tauschen zur"), and the recorded diagnosis read
        // that as text overflowing its plate. It is not: the visible string was BYTE-EXACTLY what
        // EncodePickBannerText produced from the full German line at the cap SHIPPING AT THE TIME,
        // NetProtocol.PickBannerTextMaxBytes = 96 (107 B in, 96 B out, the tail "ücknehmen)" gone).
        // TMP's word wrapping cannot break a word that fits on a line, so a mid-word stop can only
        // come from the codec.
        //
        // THE CAP IS 160 NOW, and this comment said 96 for long enough to be quoted back as the
        // shipped value. The 96 is the number the SCREENSHOT was taken at and it is kept for that
        // reason only; the live figure is the constant itself, which is what the line below prints,
        // so the diagnostic can never disagree with the codec the way this prose did.
        //
        // WHAT THIS SIDE CAN AND CANNOT KNOW: the receiver never sees the sender's original, so it
        // cannot report how much was lost. A line arriving AT the cap is the signature of a line
        // that was cut down to it, and that is what is said — no more.
        int bytes = System.Text.Encoding.UTF8.GetByteCount(_shown);
        if (bytes >= NetProtocol.PickBannerTextMaxBytes)
            // HW-VERIFY: this line decides item 12. While it fires, the peer's placard is showing a
            // sentence the WIRE cut, and no plate size can put the missing words back.
            VRLog.Alert("Net", $"Remote pick banner: the line arrived AT the wire cap — {bytes} of " +
                               $"{NetProtocol.PickBannerTextMaxBytes} B — so it is almost certainly " +
                               $"truncated: \"{_shown}\". EncodePickBannerText drops whole characters " +
                               "off the end with no ellipsis, which is what a placard stopping " +
                               "mid-word looks like. Raise PickBannerTextMaxBytes; the plate below " +
                               "will grow to hold the longer line, but it cannot restore it.");
        // THE WARNING HERE WAS RIGHT AND A SUPPRESSION WOULD HAVE HIDDEN A CRASH.
        //
        // `_root` is a readonly Transform assigned in the constructor, so it is never null in the
        // C# sense — but it is a UNITY object, and the two `_root != null` tests around this line
        // exist precisely because the board subtree can be DESTROYED under us (Unity's fake-null).
        // The log line then dereferenced it unguarded, one line below a guard that tolerates
        // exactly that state: on a destroyed root it throws MissingReferenceException out of a
        // DIAGNOSTIC, and a throw here amputates the rest of the caller's chain.
        //
        // Behaviour change, deliberately taken rather than suppressed: on a live root nothing
        // differs at all. On a destroyed one the old code threw and the new code says so, which
        // is the state worth hearing about.
        if (_root != null)
        {
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            // 1:1 — the peer's plate follows the peer's text exactly as the owner's does: the same
            // helper, the same authored minimum, the same padding, grow-only. A mirror that kept a
            // fixed plate would draw the owner's long line off a peer's parchment. Sized AFTER the
            // activation because the measurement is a forced mesh update, and a mesh forced on an
            // inactive object measures nothing. Runs once per distinct line — Apply is change-gated
            // on the text above — so it costs nothing per frame.
            Vector2 want = Core.TmpFit.PlateSizeFor(_label, PlateSize,
                                                    Cards.PlayTray.PickPlatePadding,
                                                    out string measurement);
            if (_plate != null
                && (Mathf.Abs(_plate.localScale.x - want.x) > 1e-4f
                    || Mathf.Abs(_plate.localScale.y - want.y) > 1e-4f))
            {
                _plate.localScale = new Vector3(want.x, want.y, 1f);
                // HW-VERIFY: the mirrored placard's parchment now contains its own sentence. A
                // height above the authored minimum means the text needed more room and got it;
                // exactly the minimum is ambiguous from the number, so the measurement text says
                // whether TMP was read or the readback failed.
                VRLog.Note("Net", $"Remote pick banner plate sized from the drawn text: " +
                                  $"{want.x:F3} x {want.y:F3} m (authored minimum " +
                                  $"{PlateSize.x:F3} x {PlateSize.y:F3} m; {measurement}).");
            }
            VRLog.Info("Net", $"Remote pick banner: \"{_shown}\" at board-local {_root.localPosition:F3}.");
        }
        else
        {
            VRLog.Warn("Net", $"Remote pick banner: BANNER ROOT DESTROYED — the text \"{_shown}\" was " +
                              "accepted but there is nothing left to draw it on. The board subtree was " +
                              "torn down under this banner; it will come back with the next board build.");
        }
    }
}
