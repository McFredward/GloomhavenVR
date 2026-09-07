using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// THE PEER'S DECISION ROW, MADE OUT OF THE GAME'S OWN WIDGETS — a live clone of THIS client's
/// <c>TakeDamagePanel</c> button row, driven from wire records 12 / 24 / 29, standing at the
/// mirrored board's decision seat.
///
/// ─── THE DEFECT THIS RETIRES ───────────────────────────────────────────────────────────────────
/// User report 2026-08-09, verbatim: "Die Entscheidungsbuttons sollen auch 1:1 aussehen, aktuell
/// scheint das kaputt zu sein. … Als mein Mitspieler Schaden auswählen musste kam dort zwar was an,
/// aber der Text und auch die Buttons sahen nicht 1:1 so aus wie es bei ihm aussieht. Es sah so aus
/// als wären die Buttons und der Text eigens nachgebaut und hier nicht die Spielelemente genutzt."
///
/// It was an exact diagnosis. <see cref="RemoteBoardFurniture.SetDecisionLines"/> built one mod-drawn
/// quad per line of record 12 and lettered it with the SENDER's already-rendered string. That row was
/// missing everything the owner's row actually shows — the damage ICON, the fatal-damage icon, the
/// damage NUMBER, the toggles' own on/off art, the button's own shape — and it read the wording in
/// the SENDER's language, not the viewer's. It was a picture OF a decision, not the decision.
///
/// ─── WHY THE REAL WIDGETS ARE AVAILABLE ON A PEER AT ALL ───────────────────────────────────────
/// <c>TakeDamagePanel</c> is a per-client <c>Singleton</c>. When a player has to answer a damage
/// prompt online, EVERY OTHER client's game runs <c>TakeDamagePanel.ShowOtherPlayer</c>
/// (TakeDamagePanel.cs:1102-1134), which populates that client's own panel from the same replicated
/// damage message — attacked actor, damaging ability, numbers — resets its toggles and then hides
/// the window. So a peer already owns the whole widget tree, laid out, with the button art, the burn
/// icons, the HUD font and every label localized into THEIR language by the prefab's own
/// <c>TextLocalizedListener</c>. What the game does NOT do on that client is re-paint the row for
/// the live decision (the window is hidden, and the amount moves as the OWNER toggles shields).
///
/// That split is the whole design: THE ART, THE ICONS AND THE WORDS COME FROM THE RECEIVER'S OWN
/// GAME. Only the facts a receiver cannot know ride the wire — which options the owner is showing
/// (record 29 roles), what state each is in (record 24), and the take-damage option's live number
/// and lethal/shielded picture (record 29 flags). Localization follows for free and is MORE correct
/// than shipping the sender's string: a German host and an English guest each read their own.
///
/// ─── TWO PROMPTS, ONE MECHANISM (ModBuild 301) ─────────────────────────────────────────────────
/// The SHORT-REST confirmation is mirrored the same way, and it turned out to need no new machinery
/// and not one byte of text:
///   • the receiver owns a live <c>YesNoDialog</c> because <c>ShortRest.Init</c> instantiates it
///     EAGERLY at hand-build time and hides it (ShortRest.cs:96-119) — the same shape as the
///     take-damage panel every client owns via <c>ShowOtherPlayer</c>;
///   • the clone source is the dialog BOX, not the button row, exactly as the owner's own dock
///     docks it (the test #25 fix: isolating the row drops the question and leaves the fit nothing
///     to measure) — so the mirrored dialog carries the SENTENCE as well as the buttons;
///   • that sentence is already the right one, in the VIEWER's language, because
///     <c>ShortRest.Init</c> is <c>YesNoDialog</c>'s only caller in the whole game and always
///     passes the literal key "GUI_SHORT_REST_CONFIRMATION".
/// Only the two option roles (4, 5) and the states beside them ride the wire. The one real cost is
/// an availability window: a client that has not built a hand this scenario owns no dialog, and for
/// that window the board keeps the mod-drawn plates — with the question composed above them (see
/// <c>RemoteDecisionPrompt</c>), which is more than the plates showed before.
///
/// ─── PUPPET, NOT PROGRAM ───────────────────────────────────────────────────────────────────────
/// The clone is built by <see cref="RemoteWidgetMirror"/>, which already carries the whole
/// discipline: instantiate under an INACTIVE host so no cloned game behaviour ever reaches
/// <c>Awake</c>, destroy every component that is not pure presentation (the <c>Toggle</c>, the
/// <c>Button</c>, the raycasters, the layout groups), add a blocking <c>CanvasGroup</c>, strip
/// colliders. What is left is Images and TMP texts that cannot act — and this class then acts for
/// them, from the wire.
///
/// ─── WHY IT IS DRIVEN ON THE CONTENT CADENCE AND NOT PER FRAME ─────────────────────────────────
/// The two mirrors that ship today (initiative track, objectives) call <c>TickLive</c> every frame
/// because their SOURCE animates and the clone has to follow it. This source does the opposite: it
/// is a hidden panel that never moves and whose numbers are STALE by construction (see above). Every
/// change a viewer must see arrives on the wire, at the wire's own 4 Hz content cadence — so driving
/// per frame would only re-copy stale state and then have it overwritten again, at 90 Hz, dirtying
/// this board's canvas every frame for nothing.
///
/// ─── INERT ─────────────────────────────────────────────────────────────────────────────────────
/// Nothing here is pressable and it is not a matter of care: the mirror destroys every
/// <c>Selectable</c> and <c>GraphicRaycaster</c> before the clone activates, and the board's own
/// <see cref="RemoteBoardFurniture.StripColliders"/> sweep re-proves it. A peer can watch somebody
/// else's decision; they can never answer it.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY plumbing over a wire-fed display — the CLONE is local
/// (GLOBAL-style: pixels this client owns), the three facts that drive it ride records 12, 24 and
/// 29. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteDecisionWidgets
{
    /// <summary>Roles this class can resolve, i.e. <c>NetProtocol.DecisionRoleMax + 1</c> slots
    /// indexed BY ROLE CODE (slot 0 = <c>DecisionRoleUnknown</c> and is never filled).</summary>
    private const int RoleSlots = NetProtocol.DecisionRoleMax + 1;

    /// <summary>Row-isolation scratch, shared with nobody (the surface's own list is a different
    /// object): one decision is mirrored per board and the resolve runs on the content cadence.</summary>
    private readonly List<Transform> _rowScratch = new(4);

    /// <summary>Label scratch for the bind pass (never used per frame).</summary>
    private readonly List<TMP_Text> _labelScratch = new(16);

    private readonly RemoteWidgetMirror _mirror;

    /// <summary>The owner's authored dock scale ([Cards] DecisionScale, off record 28), applied as
    /// the mirror mount's own transform scale — see the constructor.</summary>
    private readonly float _dockScale;

    /// <summary>The scaled frame the mirror mounts on (a child of the decision drawer).</summary>
    private readonly Transform _frame;

    /// <summary>One mirrored option widget's repaintable parts, resolved ONCE per clone rebuild.</summary>
    private struct RoleNode
    {
        public Transform? Clone;

        /// <summary>The widget's own background <c>Graphic</c> (the <c>Selectable.targetGraphic</c>
        /// on the SOURCE) — what the owner's dock antique-tints and what the game's own colour-tint
        /// transition greys.</summary>
        public Graphic? Background;

        /// <summary>The <c>Toggle.graphic</c> — the "this option is picked" art. Null for a plain
        /// button.</summary>
        public Graphic? ChosenGraphic;

        /// <summary>A <c>CanvasGroup</c> on the clone widget, used for the game's 0.7 dim. Added by
        /// the bind pass when the source had none, which is safe: a group the pairing did not see
        /// at build time is never written by the mirror's own drive.</summary>
        public CanvasGroup? Group;

        /// <summary>The SOURCE background's authored colour, captured at bind time so the repaint
        /// never compounds its own previous multiplications.</summary>
        public Color BaseColor;

        /// <summary>The source <c>Selectable</c>'s own normal / disabled tints and multiplier — the
        /// game's real greyed look rather than a factor invented here.</summary>
        public Color NormalTint;
        public Color DisabledTint;
        public float TintMultiplier;

        /// <summary>The same <c>ColorBlock</c>'s HIGHLIGHTED and PRESSED entries — the owner's hover
        /// and press, drawn from the game's own numbers.
        ///
        /// <para>These have to be CAPTURED, not asked for later, and the reason is the puppet pass:
        /// <see cref="RemoteWidgetMirror"/> destroys every <c>Selectable</c> on the clone before it
        /// is ever shown, so by the time this row is painted there is no button left to hold a
        /// <c>ColorBlock</c>. That destruction is not an obstacle to work around — it is what makes
        /// a peer's own pointer unable to light up somebody else's board — so the four tints are
        /// read off the SOURCE widget at bind time, while it still exists, and the mirror paints
        /// them itself.</para></summary>
        public Color HighlightedTint;
        public Color PressedTint;
    }

    private readonly RoleNode[] _roles = new RoleNode[RoleSlots];

    /// <summary>
    /// The <c>DialogPopup</c> option nodes, indexed by WIRE OPTION INDEX rather than by role code —
    /// the one prompt whose options have no role.
    ///
    /// <para>That is not an omission, it is what those buttons ARE: <c>DialogPopup</c> pools them
    /// (<c>NormalizePool</c>) and letters them per prompt, so there is no serialized widget for a
    /// role to name. What there IS, and what is enough, is the index: records 12, 24 and 29 are
    /// filled by ONE sampler walk, so option <c>i</c> is the same option on both machines. No role
    /// code was added for this prompt precisely because "the i-th option" is all the identity that
    /// exists — a code would be a wire field with no more meaning than the index beside it.</para>
    /// </summary>
    private readonly RoleNode[] _options = new RoleNode[NetProtocol.DecisionStateMaxOptions];

    /// <summary>How many entries of <see cref="_options"/> the last <see cref="Bind"/> filled.</summary>
    private int _optionCount;

    /// <summary>Builder for the popup's source row — see <see cref="RemoteDialogOptions"/>. Held for
    /// the board's whole life because it owns an inactive holder and a change-gated row; it builds
    /// nothing until a popup is actually mirrored.</summary>
    private readonly RemoteDialogOptions _dialogOptions;

    /// <summary>Per-role "the owner is showing this widget, in this state" scratch, rebuilt on every
    /// applied repaint (high bit = shown, low bits = the record-24 state byte). A field rather than
    /// a local so the 4 Hz repaint allocates nothing.</summary>
    private readonly byte[] _present = new byte[RoleSlots];

    // ---- the take-damage option's own runtime parts (clone side) ----------------------------
    private TMP_Text? _amount;        // "Damage Amount"
    private TMP_Text? _takeDamageText; // "Text" under Receive Damage
    private GameObject? _damageIcon;
    private GameObject? _fatalIcon;
    private GameObject? _mandatory;

    /// <summary>Every OTHER label under the clone — the two burn wordings and anything the prefab
    /// adds later. Repainted to the dock's parchment gold, exactly as
    /// <c>DecisionDockSurface.AdjustDockedRow</c> repaints the owner's own row.</summary>
    private TMP_Text[] _labels = System.Array.Empty<TMP_Text>();

    /// <summary>The game's own text colours, read from THIS client's <c>UIInfoTools</c> /
    /// <c>TakeDamagePanel</c> at bind time (never invented here).</summary>
    private Color _basicText = Color.white;
    private Color _negativeText = Color.white;
    private Color _shieldText = Color.white;

    /// <summary>Which clone rebuild <see cref="_roles"/> and the take-damage parts were resolved
    /// against; -1 = nothing resolved yet. The mirror's own invalidation key.</summary>
    private int _boundStamp = -1;

    /// <summary>The POINTER bits last applied, folded to one int (two bits per option) — the
    /// per-frame drive's own gate. -1 = nothing applied yet. Separate from
    /// <see cref="_appliedKey"/> because it is tested at 90 Hz and that one is not.</summary>
    private int _appliedPointerBits = -1;

    /// <summary>What was last APPLIED (roles + states + flags + damage), packed for the change
    /// gate — a uGUI colour write dirties the canvas, so the repaint must not run on an unchanged
    /// value. -1 = nothing applied yet.</summary>
    private long _appliedKey = -1;

    /// <summary>True while the mirrored GAME widgets are what this board is showing at the decision
    /// seat (the caller then hides its mod-drawn row).</summary>
    public bool Showing { get; private set; }

    /// <summary>Why the real widgets are NOT being shown, for the board's diagnostic line. Empty
    /// while they are.</summary>
    public string Reason { get; private set; } = "not built";

    /// <summary>Height of the mirrored row in BOARD-local metres (0 while it is not shown) — what
    /// the use-bar drawer stacks below, instead of an assumed plate height.
    ///
    /// <para>IT IS THE PRESSABLE BLOCK, NOT THE CLONE'S ENVELOPE, since the short-rest position fix
    /// (see <see cref="AnchorWidgetBlock"/>). The owner publishes exactly that —
    /// <c>DecisionDockSurface.RowBottomUpMeters</c> is <c>d + blockBottomAbovePivot</c>, the widget
    /// block's bottom, and its seat is the block's TOP — so a use-bar stack hung off the clone's
    /// full height would sit the question's height too low on a short rest, the same error the
    /// buttons themselves had. Falls back to the clone envelope (the mirror measures in its own
    /// mount frame, so the dock scale converts it back — see the constructor) only while no block
    /// has been measured yet.</para></summary>
    public float RowHeight => !Showing
        ? 0f
        : _blockHeight > 0f ? _blockHeight : _mirror.FittedSize.y * _dockScale;

    /// <summary>
    /// THE OWNER's engraved-label FILL — their <c>[ButtonColors] LabelR/G/B</c> off record 28
    /// (<see cref="NetProtocol.TuneLabelColor"/>, id 48), handed down at construction by
    /// <see cref="RemoteBoardFurniture"/> from the very field its own mirrored KEYCAPS are lettered
    /// out of, already clamped to 0..1 and opaque.
    ///
    /// <para>WHY IT IS HELD AND NOT ASKED FOR PER PAINT. It is the same shape the cap palette
    /// beside it uses: a board is rebuilt when its owner's tuning revision moves, so a value taken
    /// once at construction cannot go stale, and <see cref="Apply"/> runs on the content cadence
    /// AND on the 90 Hz pointer drive — a per-paint wire read would be a struct copy in both.</para>
    ///
    /// <para>It replaces a <c>NativeButtonSkin.LabelColor</c> read that was THIS VIEWER's dial: see
    /// <c>WorldUI.NativeButtonSkin.LabelOwner</c> for the defect and why the enum exists.</para>
    /// </summary>
    private readonly Color _ownerLabelColor;

    /// <summary>
    /// Build the mirror under <paramref name="decisionRoot"/> — the drawer transform whose origin is
    /// the widget-block TOP EDGE the owner's own dock anchors at. Content grows DOWN from it, into
    /// the same width × height envelope the owner's dock fits their real row into.
    /// <paramref name="ownerLabelColor"/> is the BOARD OWNER's label fill (see
    /// <see cref="_ownerLabelColor"/>) — never this client's own.
    /// </summary>
    public RemoteDecisionWidgets(Transform decisionRoot, float dockScale, Color ownerLabelColor)
    {
        _dockScale = dockScale > 0f ? dockScale : 1f;
        _ownerLabelColor = ownerLabelColor;

        // THE SCALE NODE. The owner's dock states its budget in MOUNT-LOCAL units and lets the
        // mount's own lossyScale (board root x [Cards] DecisionScale) carry it into world metres —
        // DecisionDockSurface.Place multiplies its fit by mount.lossyScale.x and never by the dock
        // scale directly. Reproducing that with a bare board-local mount would drop the dock scale
        // entirely and render the mirrored row at 1/DecisionScale of the owner's size (62 % at the
        // shipped 1.6x). So the mirror gets its own scaled frame and the SAME unscaled budget
        // numbers the owner's dock uses, which makes the two identical by construction rather than
        // by two formulas agreeing.
        var frame = new GameObject("DecisionRowScale").transform;
        frame.SetParent(decisionRoot, worldPositionStays: false);
        frame.localPosition = Vector3.zero;
        frame.localRotation = Quaternion.identity;
        frame.localScale = Vector3.one * _dockScale;
        _frame = frame;

        _mirror = new RemoteWidgetMirror("DecisionRow", frame,
            Cards.PlayTray.DecisionMountWidth,
            Cards.PlayTray.DecisionMountMaxHeight,
            new Vector2(0f, -1f), fitWidth: true,
            // THE OWNER'S OWN DOCK DENSITY, not the shared tray one: DecisionDockSurface renders
            // choice widgets at 0.8x the tray density on purpose (1.25x bigger — "the one thing the
            // player MUST read and hit under pressure"). A mirror at the shared density would draw
            // the identical widgets 1.25x too small on every other client, which is the very drift
            // the 1:1 rule is about.
            densityScale: WorldUI.Surfaces.DecisionDockSurface.DensityScale,
            // NOT puppeted from the source — see the parameter's own doc. The source is a hidden
            // panel holding a STALE picture of somebody else's decision; everything a viewer must
            // see comes from the wire, through Apply below.
            driveFromSource: false);
        _mirror.SetShown(false);
        _dialogOptions = new RemoteDialogOptions(decisionRoot);
    }

    public void Destroy()
    {
        _dialogOptions.Destroy();
        _mirror.Destroy();
        if (_frame != null)
            Object.Destroy(_frame.gameObject);
    }

    /// <summary>
    /// Content-cadence entry point. Returns true iff the peer's decision seat is now showing THE
    /// GAME'S OWN widgets — a false return means the caller must draw (and show) its mod-drawn row.
    ///
    /// <para>Wrapped whole: a half-built panel, a destroyed singleton or a prefab reshuffle must
    /// degrade to "the mod-drawn plates", never take down the remote-board refresh this runs
    /// inside. Degrading is the designed failure direction here — a peer seeing the older, cruder
    /// row is a cosmetic loss; a peer seeing nothing (or an exception eating the rest of the
    /// board's refresh) is not.</para>
    /// </summary>
    public bool Refresh(RemoteAvatar owner)
    {
        bool mirrored = RefreshCore(owner);
        NoteShortRestOneToOne(owner, mirrored);
        return mirrored;
    }

    /// <summary>
    /// THE OBSERVER'S HALF OF THE SHORT-REST 1:1 LINE — the counterpart of
    /// <c>WorldUI.Surfaces.DecisionDockSurface.NoteShortRestOneToOne</c>, printed with the SAME
    /// token, the SAME fields, in the SAME order and the SAME units, so the two logs diff directly.
    ///
    /// <para>User item 11 of 2026-09-06: "Das Dialogfeld der kurzen Rast sieht nicht 1:1 gleich aus
    /// wie es beim Spieler angezeigt wird." The reason it did not could be read off the ModBuild 459
    /// logs — the owner published two <c>DecisionRoleUnknown</c>s and this class fell back to the
    /// mod-drawn plates — but it took two lines in two files to see it. This is one line, on both
    /// machines, that states the whole claim.</para>
    ///
    /// <para>READING IT. <c>FALLBACK</c> in place of the geometry means the game's dialog is NOT
    /// being mirrored and the reason is quoted verbatim: that alone is the 1:1 breach, whatever the
    /// numbers would have said. Otherwise <c>px</c> / <c>mount</c> / <c>world</c> / <c>mountScale</c>
    /// must equal the owner's field for field; <c>graphics</c> and <c>texts</c> say whether the two
    /// subtrees hold the same children (<c>words</c> deliberately need NOT match — each client
    /// letters its own copy in its own language, which is the design); and <c>roles</c> / <c>states</c>
    /// are what actually arrived over the wire.</para>
    ///
    /// <para>Change-gated on its own text and cleared whenever the prompt is not a short rest, so a
    /// second short rest states itself again even at byte-identical geometry.</para>
    /// </summary>
    private void NoteShortRestOneToOne(RemoteAvatar owner, bool mirrored)
    {
        if (owner.DecisionLines == null
            || owner.DecisionPromptKind != NetProtocol.DecisionKindShortRestYesNo)
        {
            _lastShortRestNote = string.Empty;
            return;
        }
        try
        {
            byte[]? roles = owner.DecisionRoles;
            byte[]? states = owner.DecisionOptionStates;
            int count = roles != null ? roles.Length : states != null ? states.Length : 0;
            if (states != null && states.Length > count)
                count = states.Length;
            string note;
            if (!mirrored)
            {
                note = $"FALLBACK (mod-drawn plates): {Reason} | "
                     + WorldUI.Surfaces.DecisionDockSurface.DescribeWireOptions(roles, states, count);
            }
            else
            {
                Vector2 px = _mirror.MeasuredSizePx;
                Vector2 mount = _mirror.FittedSize;
                float worldScale = _frame != null ? _frame.lossyScale.x : _dockScale;
                Transform? clone = _mirror.CloneOf(_boundSource);
                note = $"src='{(_boundSource != null ? _boundSource.name : "?")}' | "
                     + $"px={px.x:F0}x{px.y:F0} | mount={mount.x:F4}x{mount.y:F4} m | "
                     + $"world={mount.x * worldScale * 1000f:F1}x{mount.y * worldScale * 1000f:F1} mm | "
                     + $"mountScale={worldScale:F4} density="
                     + $"{Cards.PlayTray.TrayPixelsPerMeter * WorldUI.Surfaces.DecisionDockSurface.DensityScale:F1} | "
                     + $"blockTop={_blockSeatResidual * 1000f:F2} mm blockH={_blockHeight * 1000f:F2} mm "
                     + $"(lift {_blockLift * 1000f:F2} mm, source=MIRRORED CLONE TRANSFORM) | "
                     + WorldUI.Surfaces.DecisionDockSurface.SubtreeInventory(clone) + " | "
                     + WorldUI.Surfaces.DecisionDockSurface.DescribeWireOptions(roles, states, count)
                     + $" | labelRGB=({_ownerLabelColor.r:F3},{_ownerLabelColor.g:F3},{_ownerLabelColor.b:F3})";
            }
            if (note == _lastShortRestNote)
                return;
            _lastShortRestNote = note;
            // HW-VERIFY
            VRLog.Note("Net", $"SHORT REST DIALOG 1:1 [MIRROR player {owner.PlayerId}] " + note
                       + " — diff this against the '[OWNER]' line in THAT player's own log: same "
                       + "token, same fields, same order. FALLBACK anywhere in this line IS the 1:1 "
                       + "breach — the peer is looking at mod-made plates, not the owner's dialog. "
                       + "`words` may differ (each client letters its own copy in its own language, "
                       + "by design); `px`, `mount`, `world`, `mountScale`, `graphics` and `texts` "
                       + "may not. THE POSITION IS THE TWO NEW FIELDS AND IT READS FROM THIS LOG "
                       + "ALONE (user 2026-09-07 item 5): `blockTop` is where the PRESSABLE "
                       + "widgets' top edge sits relative to the decision seat, MEASURED BEFORE "
                       + "this tick's own correction, and every line after the first one of a "
                       + "prompt MUST read 0.00 mm — the owner's own dock solves for exactly that "
                       + "— while `blockH` must equal "
                       + "the owner's. `lift` is how far this clone had to be raised to get there, "
                       + "i.e. the size of the breach that was there before. On the ModBuild 472 "
                       + "logs that breach was 44.3 mm board-local for the SHORT REST — the cloned "
                       + "box carries the question ABOVE the buttons and used to take the seat with "
                       + "it — so a lift reading 0.00 mm on a short rest means this pass never ran. "
                       + "The take-damage and popup lifts are NOT predicted anywhere: their clone "
                       + "is an isolated row that also holds the damage number and icons, which the "
                       + "owner does not anchor on either, so this field is the only thing that "
                       + "says how far those two moved.");
        }
        catch (System.Exception e)
        {
            _lastShortRestNote = string.Empty;
            VRLog.Warn("Net", $"Short-rest 1:1 line failed ({e.Message}) — the mirror itself is "
                              + "unaffected; only this diagnostic is missing for this tick.");
        }
    }

    /// <summary>The 1:1 line's own change gate — see <see cref="NoteShortRestOneToOne"/>.</summary>
    private string _lastShortRestNote = string.Empty;

    /// <summary>Clone rects handed to <c>DecisionDockSurface.WidgetBlockEdges</c> — the mirror's
    /// counterpart of the owner's <c>ResolvePromptWidgets</c> scratch. A field so the anchor pass
    /// allocates nothing on the 4 Hz cadence.</summary>
    private readonly List<RectTransform> _blockRects = new(4);

    /// <summary>The mirrored widget block's measured HEIGHT in board-local metres, 0 until one has
    /// been measured — see <see cref="RowHeight"/> and <see cref="AnchorWidgetBlock"/>.</summary>
    private float _blockHeight;

    /// <summary>Where the mirrored widget block's TOP edge was MEASURED, relative to the decision
    /// seat, in board-local metres — read on THIS tick BEFORE the tick's own correction, never
    /// after. Zero is the pass condition, and taking it before the write is what keeps it a
    /// measurement: a residual sampled after the rigid shift that zeroes it would be a claim
    /// verifying itself, and would read 0.00 mm however wrong the seat was. So the FIRST line after
    /// a clone rebuild states the raw breach and every line after it states 0.00.</summary>
    private float _blockSeatResidual;

    /// <summary>The total lift <see cref="AnchorWidgetBlock"/> has applied to <see cref="_frame"/>,
    /// board-local metres (positive = the clone was raised). This is the size of the defect it
    /// retires, so it is logged rather than kept private.</summary>
    private float _blockLift;

    /// <summary>
    /// SEAT THE MIRRORED CLONE BY THE OWNER'S OWN RULE — the fix for user item 5 of 2026-09-07:
    /// "Die Position des Kurze-Rast Dialogs ist verschieden beim lokalen Board und beim remote
    /// Board. Er sieht den Button weiter oben als es im remote board der Fall ist."
    ///
    /// <para>THE MECHANISM, AND IT IS NOT A CANVAS-WIDTH STORY. Both sides of the 2026-09-07 logs
    /// measure this dialog at <c>px=350x130</c>, <c>mount=0.1823x0.0677 m</c>,
    /// <c>mountScale=20.2747</c> — identical to the last digit, and <c>roles=[4,5]</c> on the mirror
    /// says the game's real dialog is what the peer is looking at. The SIZE was never wrong and the
    /// mirror was never a re-implementation. What differed was which edge each side anchored:</para>
    /// <list type="bullet">
    /// <item>THE OWNER anchors the PRESSABLE WIDGETS. <c>DecisionDockSurface.Place</c> solves its
    /// host shift as <c>targetBlockTopUp - blockTopAbovePivot</c>, where the block is exactly the
    /// prompt's serialized option widgets (for this prompt, <c>YesNoDialog.yesButton</c> and
    /// <c>noButton</c>) — so the BUTTONS' top edge lands on the decision area's ceiling and the
    /// dialog's question is free to stand above it.</item>
    /// <item>THE MIRROR anchored the whole clone. <see cref="RemoteWidgetMirror"/> grows content
    /// down from the mount (<c>grow=(0,-1)</c>), so the cloned <c>box</c>'s TOP edge took the seat.
    /// For every prompt whose docked source IS the button row that is the same edge; for the short
    /// rest — the one prompt that deliberately clones the whole box so the question comes with it
    /// (see <see cref="ResolveShortRestBox"/>) — it is the question's height lower.</item>
    /// </list>
    /// <para>The 2026-09-07 pair measures that height exactly. Owner: ceiling 1216 mm above the
    /// mount, row top AT it, row bottom 900 mm, i.e. a 316 mm block, host shifted 1428 mm — so the
    /// buttons' top is 212 mm BELOW the box centre and therefore 898 mm below the box top. Mirror:
    /// <c>mount-local (0.000, -0.034, 0.000)</c>, i.e. the box's top edge exactly on the seat. The
    /// peer's buttons stood 898 mm world = <b>44.3 mm board-local</b> too low. That is the report.</para>
    ///
    /// <para>THE FIX IS THE FRAME, NOT A FACTOR. This raises <see cref="_frame"/> until the CLONED
    /// widgets' measured top edge lands on the drawer origin — the seat both sides already agree on
    /// (<c>RemoteBoardFurniture.ApplyDecisionSeat</c> puts the drawer there, and the owner's
    /// <c>targetBlockTopUp</c> is the same ceiling) — using the owner's own edge walk
    /// (<c>DecisionDockSurface.WidgetBlockEdges</c>), not a second copy of it. It is a rigid
    /// translation solved against the measurement, so it converges in one tick and is idempotent
    /// afterwards; no constant, no per-board number, nothing to re-tune when a prefab changes.</para>
    ///
    /// <para>IT RUNS FOR ALL THREE PROMPTS ON PURPOSE, AND THEIR LIFT IS NOT PREDICTED HERE. The
    /// owner anchors the same three take-damage widgets and the same pooled popup buttons — the
    /// serialized options and nothing else — so whatever this pass moves on those two prompts moves
    /// them TOWARD the owner by construction. It would be an assertion, not a finding, to write
    /// that their lift is negligible: the take-damage clone is an isolated ROW, and that row also
    /// holds the damage number and the damage/fatal icons, which are outside the block the owner
    /// anchors and may well stand above it. The logged `lift` field is what settles each prompt's
    /// magnitude in the next hardware log; no branch excuses any of them from the rule.</para>
    /// </summary>
    private void AnchorWidgetBlock()
    {
        _blockRects.Clear();
        CollectBlockRects(_roles);
        CollectBlockRects(_options);
        Transform? seat = _frame != null ? _frame.parent : null;
        if (seat == null || _blockRects.Count == 0)
        {
            // No resolvable widget: keep the clone where the mirror put it (its own top edge on the
            // seat) rather than guessing, and say so through a zeroed height so RowHeight falls back.
            _blockHeight = 0f;
            _blockSeatResidual = 0f;
            return;
        }
        float boardPerWorld = seat.lossyScale.y;
        if (boardPerWorld <= 1e-6f)
            return;
        if (!WorldUI.Surfaces.DecisionDockSurface.WidgetBlockEdges(
                _blockRects, seat.position, seat.up, out float topWorld, out float bottomWorld))
        {
            _blockHeight = 0f;
            _blockSeatResidual = 0f;
            return;
        }
        float top = topWorld / boardPerWorld;      // board-local metres above the seat
        float bottom = bottomWorld / boardPerWorld;
        _blockHeight = Mathf.Max(0f, top - bottom);
        if (_frame == null)
            return;
        _blockSeatResidual = top; // MEASURED, and recorded before the line below moves anything
        Vector3 p = _frame.localPosition;
        _frame.localPosition = new Vector3(p.x, p.y - top, p.z);
        _blockLift += -top;
    }

    /// <summary>Add every bound clone widget of <paramref name="nodes"/> to the block scratch — the
    /// mirror's counterpart of <c>DecisionDockSurface.ResolvePromptWidgets</c>. One array is always
    /// empty (roles for the popup, options for the other two), so the union is the prompt's own set;
    /// visibility is not filtered here because the edge walk excludes inactive graphics itself,
    /// exactly as it does on the owner.</summary>
    private void CollectBlockRects(RoleNode[] nodes)
    {
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Clone is RectTransform rt && !_blockRects.Contains(rt))
                _blockRects.Add(rt);
        }
    }

    /// <summary>The subtree <see cref="RefreshCore"/> last cloned FROM, held so the 1:1 line can
    /// census the clone that is actually on the board (never a second resolve, which could answer
    /// about a different object — the same race <see cref="_boundDialog"/> exists for).</summary>
    private RectTransform? _boundSource;

    /// <summary>The body of <see cref="Refresh"/> — split out only so that EVERY exit of it,
    /// including the eight <see cref="Down"/> refusals, passes through the 1:1 line above. A
    /// diagnostic that only prints on the success path cannot report the failure it exists to
    /// report.</summary>
    private bool RefreshCore(RemoteAvatar owner)
    {
        try
        {
            byte[]? roles = owner.DecisionRoles;
            if (owner.DecisionLines == null)
                return Down("the owner has no visible decision row");
            byte kind = owner.DecisionPromptKind;
            if (kind != NetProtocol.DecisionKindTakeDamage
                && kind != NetProtocol.DecisionKindShortRestYesNo
                && kind != NetProtocol.DecisionKindDialogPopup)
                return Down($"the owner's prompt (kind {kind}) is not one whose " +
                            "widgets exist on this client — see NetProtocol.DecisionRoleMax");
            // THE POPUP IS THE ONE PROMPT WITH NO ROLES, AND THAT IS CORRECT — see the _options
            // field. Its options are pooled and lettered per prompt, so the sampler publishes
            // DecisionRoleUnknown for every one of them and the index carries the identity instead.
            // The other two prompts still require a role: for them an all-unknown row means the
            // sender could not attribute a widget this client owns, which is exactly when the
            // mod-drawn plates are the honest answer.
            bool byIndex = kind == NetProtocol.DecisionKindDialogPopup;
            if (!byIndex && (roles == null || !AnyKnown(roles)))
                return Down("no widget roles on the wire (a sender predating record 29, or a row " +
                            "this build's sampler could not attribute)");

            RectTransform? source = ResolveSourceRow(kind, owner);
            _boundSource = source;
            if (source == null)
                return Down(kind switch
                {
                    NetProtocol.DecisionKindShortRestYesNo =>
                        "this client owns no short-rest YesNoDialog yet (no hand built this " +
                        "scenario), so there is nothing to clone — see CardsGameApi.AnyShortRestDialog",
                    NetProtocol.DecisionKindDialogPopup => _dialogOptions.Reason,
                    _ => "this client's own TakeDamagePanel row could not be resolved " +
                         "(singleton absent, or the prompt's widget fields are null)",
                });

            // SHOWN FIRST, fitted second. The fit measures VISIBLE clone graphics, and a host this
            // class had previously hidden (an undocked prompt, a failed tick) makes every one of
            // them invisible — the mirror would then keep its last fit and the re-shown row could
            // stand at a stale size for a whole cadence tick. Nothing is on screen yet either way:
            // the paint below happens in this same frame, before anything renders.
            _mirror.SetShown(true);
            if (!_mirror.Refresh(source))
                return Down(_mirror.Reason);

            if (_boundStamp != _mirror.RebuildStamp)
                Bind(kind);

            Apply(owner, roles ?? System.Array.Empty<byte>());
            // RE-FIT AFTER THE PAINT, not before it. The fit measures the union of VISIBLE clone
            // graphics, and it is Apply that decides which option widgets are visible (the owner may
            // be showing two of the three, and the source's own actives are the receiver's stale
            // ones). Fitting first would size the row to the wrong set for one whole cadence tick —
            // a visible wrong-size flash on the frame a prompt opens. This second call cannot
            // rebuild: the structure has not moved, so it is one walk plus the fit.
            if (!_mirror.Refresh(source))
                return Down(_mirror.Reason);
            // A rebuild on that second call would have invalidated every node Apply just wrote to
            // (it cannot happen — the structure did not move between two statements — but a stale
            // CloneOf handle is exactly the failure the mirror's RebuildStamp exists to prevent, so
            // it is re-checked rather than assumed).
            if (_boundStamp != _mirror.RebuildStamp)
            {
                Bind(kind);
                Apply(owner, roles ?? System.Array.Empty<byte>());
            }
            // …and only NOW seat it: the anchor measures the clone the paint above just decided the
            // visibility of, so it has to run after Apply and after the re-fit that follows it.
            AnchorWidgetBlock();
            if (!Showing)
            {
                Showing = true;
                Reason = string.Empty;
                VRLog.Info("Net", "Remote decision row: now mirroring THE GAME'S OWN " +
                                  $"{PromptName(kind)} " +
                                  $"widgets — a clone of THIS client's own subtree ('{source.name}'), " +
                                  "driven from wire records 12/24/29. The button art, the icons and " +
                                  "every wording are this client's own assets in THIS player's " +
                                  "language; only the roles, the option states and (for take-damage) " +
                                  "the damage number came over the wire. The mod-drawn plate row is " +
                                  "down.");
            }
            return true;
        }
        catch (System.Exception e)
        {
            return Down($"mirroring failed ({e.Message})");
        }
    }

    /// <summary>
    /// PER-FRAME POINTER DRIVE — repaint when, and only when, the OWNER's hover or press moved.
    ///
    /// <para>WHY THIS IS NOT LEFT TO <see cref="Refresh"/>. Everything else this class shows moves
    /// when the owner COMMITS something (a toggle flips, a damage number is recomputed), and the
    /// board's 4 Hz content cadence is the right rate for that. A pointer is not that kind of fact:
    /// a beam crossing three options in half a second would arrive as one arbitrary sample, and a
    /// press held under a quarter second would never be drawn at all. The mirrored board next to
    /// this one already makes exactly this split for exactly this reason — the half-card hover of
    /// record 14 is driven per frame "so the glow lands with the synced edge, not on the 4 Hz
    /// content cadence" (RemoteControlBoard). This is that argument, applied to the decision
    /// row.</para>
    ///
    /// <para>IT IS A GATE, NOT A SECOND PAINTER. The whole body is two array reads per option and
    /// an int compare; on the frames where nothing moved — which is nearly all of them — it returns
    /// before touching a single Graphic. When it does fire it calls the SAME <see cref="Apply"/> the
    /// cadence calls, so there is no second description of what the row should look like that could
    /// drift from the first one. No re-fit is needed and none is done: a colour change cannot move
    /// the row's extents, and the fit is what makes <see cref="Refresh"/> expensive.</para>
    ///
    /// <para>Wrapped whole, for the reason <see cref="Refresh"/> is: this now runs inside the
    /// per-frame board tick, and a throw here would take that tick down with it.</para>
    /// </summary>
    public void TickPointer(RemoteAvatar owner)
    {
        if (!Showing || _boundStamp != _mirror.RebuildStamp)
            return;
        // NOT gated on the roles being present: a DialogPopup has none by design (see _options),
        // and requiring them here would have silently switched the pointer drive off for exactly
        // the prompt this class most recently learned to mirror.
        int bits = FoldPointerBits(owner.DecisionOptionStates);
        if (bits == _appliedPointerBits)
            return;
        try
        {
            Apply(owner, owner.DecisionRoles ?? System.Array.Empty<byte>());
        }
        catch (System.Exception e)
        {
            Down($"the pointer repaint failed ({e.Message})");
        }
    }

    /// <summary>Every option's two POINTER bits folded into one int — the per-frame gate's whole
    /// comparison. Two bits per option, at most eight options, so it always fits and no option can
    /// alias another (the trap the paint key itself fell into: see <see cref="Apply"/>).
    ///
    /// <para>IT FOLDS THE STATES ARRAY AND NOT THE ROLES, which matters since ModBuild 303: a
    /// <c>DialogPopup</c> publishes no roles at all (its options are index-addressed), so a fold
    /// bounded by the role count would have been a constant zero for that prompt — a gate that
    /// never opens, which is the shape of defect this project has shipped before. The states array
    /// is the one that carries the bits, so it is the one that bounds the walk.</para></summary>
    private static int FoldPointerBits(byte[]? states)
    {
        if (states == null)
            return 0;
        int bits = 0;
        for (int i = 0; i < states.Length && i < 8; i++)
        {
            byte f = states[i];
            if ((f & NetProtocol.DecisionOptionHoveredBit) != 0)
                bits |= 1 << (i * 2);
            if ((f & NetProtocol.DecisionOptionPressedBit) != 0)
                bits |= 2 << (i * 2);
        }
        return bits;
    }

    /// <summary>Hide the mirrored widgets and record WHY (logged once per reason change, so a
    /// hardware log states whether a peer saw the real row or the fallback, and why).</summary>
    private bool Down(string reason)
    {
        _mirror.SetShown(false);
        // Hand the seat back UNSHIFTED. The lift belongs to one clone of one prompt; leaving it on
        // the frame would displace the next prompt's clone by the previous one's question height
        // for the tick before AnchorWidgetBlock re-solves it.
        if (_frame != null && _blockLift != 0f)
        {
            Vector3 p = _frame.localPosition;
            _frame.localPosition = new Vector3(p.x, p.y - _blockLift, p.z);
        }
        _blockLift = 0f;
        _blockHeight = 0f;
        _blockSeatResidual = 0f;
        if (Showing || Reason != reason)
        {
            Showing = false;
            Reason = reason;
            _appliedKey = -1;
            _appliedPointerBits = -1;
            VRLog.Info("Net", $"Remote decision row: NOT mirroring the game's own widgets — {reason}. " +
                              "The mod-drawn plate row (record 12's wordings) stands in, which is " +
                              "what every build before ModBuild 105 drew for every prompt.");
        }
        return false;
    }

    /// <summary>True when at least one role on the wire is one this build can resolve. A row of
    /// nothing but <c>DecisionRoleUnknown</c> is a row this class must not claim.</summary>
    private static bool AnyKnown(byte[] roles)
    {
        for (int i = 0; i < roles.Length; i++)
        {
            if (roles[i] != NetProtocol.DecisionRoleUnknown && roles[i] <= NetProtocol.DecisionRoleMax)
                return true;
        }
        return false;
    }

    /// <summary>
    /// THIS CLIENT'S OWN take-damage widget row, isolated exactly the way the LOCAL dock isolates
    /// it (<c>ModalFallback.DecisionDock.IsolateRow</c> — the deepest common ancestor of the three
    /// serialized widgets that is a strict descendant of the window root). Using the same resolver
    /// rather than a second one is the point: the object a peer clones is by construction the object
    /// their own board would have docked.
    ///
    /// <para>The panel is HIDDEN here — that is expected and harmless. <c>Object.Instantiate</c>
    /// reads an inactive subtree perfectly well, and the layout it carries is the one Unity computed
    /// while the panel was active at scene init (which it must have been, or the
    /// <c>Singleton&lt;TakeDamagePanel&gt;</c> would not be initialized and its
    /// <c>TextLocalizedListener</c>s would not have written their strings).</para>
    /// </summary>
    private static string PromptName(byte kind) => kind switch
    {
        NetProtocol.DecisionKindShortRestYesNo => "short-rest confirmation",
        NetProtocol.DecisionKindDialogPopup => "dialog-popup option",
        _ => "take-damage",
    };

    private RectTransform? ResolveSourceRow(byte kind, RemoteAvatar owner)
    {
        _boundDialog = null;
        if (kind == NetProtocol.DecisionKindShortRestYesNo)
            return ResolveShortRestBox();
        if (kind == NetProtocol.DecisionKindDialogPopup)
        {
            string? lines = owner.DecisionLines;
            return _dialogOptions.Resolve(lines != null ? lines.Split('\n') : null);
        }
        TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
            ? Singleton<TakeDamagePanel>.Instance
            : null;
        if (p == null || p.myWindow == null)
            return null;
        _rowScratch.Clear();
        if (p.burnAvailableCardsToggle != null)
            _rowScratch.Add(p.burnAvailableCardsToggle.transform);
        if (p.burnDiscardedCardsToggle != null)
            _rowScratch.Add(p.burnDiscardedCardsToggle.transform);
        if (p.takeDamageButton != null)
            _rowScratch.Add(p.takeDamageButton.transform);
        return _rowScratch.Count == 0
            ? null
            : WorldUI.ModalFallback.DecisionDock.IsolateRow(p.myWindow, _rowScratch);
    }

    /// <summary>THE DIALOG <see cref="ResolveShortRestBox"/> last cloned from, so
    /// <see cref="Bind"/> binds against the SAME object the mirror copied. Resolving twice would be
    /// a race: the local active hand can change between two statements, and a
    /// <see cref="RemoteWidgetMirror.CloneOf"/> lookup against a dialog the clone was not made from
    /// silently returns null for every widget.</summary>
    private YesNoDialog? _boundDialog;

    /// <summary>
    /// THIS CLIENT'S OWN short-rest confirmation, resolved the way the LOCAL dock resolves it —
    /// the dialog <c>box</c>, not the isolated button row.
    ///
    /// <para>WHY THE WHOLE BOX. The dock learned this the hard way (test #25 items 1b/1c, see
    /// <c>ModalFallback.DecisionDock</c>): isolating the common ancestor of the two buttons DROPS
    /// THE QUESTION — the description text is a sibling under <c>box</c>, not a child of the row —
    /// and leaves the fit with no visible graphics to measure. Mirroring the box instead of the row
    /// is therefore not a convenience, it is what makes the peer see the same thing the owner does:
    /// the sentence and both buttons, in one envelope, fitted into the same mount.</para>
    ///
    /// <para>AND IT IS WHY NO TEXT IS ON THE WIRE. The clone arrives already lettered — with the
    /// RECEIVER's own translation of "GUI_SHORT_REST_CONFIRMATION", because <c>ShortRest.Init</c> is
    /// <c>YesNoDialog</c>'s only caller in the entire game and always passes that literal key
    /// (ShortRest.cs:100). A German host and an English guest each read their own, and the mod
    /// composed nothing.</para>
    ///
    /// <para>The row fallback below is the dock's own, kept for the same reason it keeps it: a
    /// prefab reshuffle that nulls <c>box</c> should cost the question, not the whole mirror.</para>
    /// </summary>
    private RectTransform? ResolveShortRestBox()
    {
        YesNoDialog? d = Cards.CardsGameApi.AnyShortRestDialog();
        if (d == null || d.window == null)
            return null;
        _boundDialog = d;
        RectTransform? box = d.box;
        if (box != null && !ReferenceEquals(box, d.window.transform)
            && box.IsChildOf(d.window.transform))
            return box;
        _rowScratch.Clear();
        if (d.yesButton != null)
            _rowScratch.Add(d.yesButton.transform);
        if (d.noButton != null)
            _rowScratch.Add(d.noButton.transform);
        return _rowScratch.Count == 0
            ? null
            : WorldUI.ModalFallback.DecisionDock.IsolateRow(d.window, _rowScratch);
    }

    /// <summary>
    /// Resolve every clone node this class drives, ONCE per clone rebuild. Source-side references
    /// come off the game's own serialized fields (never a name lookup), and each is translated to
    /// its clone through <see cref="RemoteWidgetMirror.CloneOf"/> — the mirror's documented override
    /// seam.
    ///
    /// <para>Two prompts, two binds. The short-rest branch binds two options and nothing else: the
    /// take-damage runtime parts (the amount, the damage and fatal icons, the mandatory highlight)
    /// belong to a panel this clone was not made from, so they stay null and every write to them in
    /// <see cref="Apply"/> is already null-guarded. Branching explicitly rather than letting
    /// <c>CloneOf</c> return null four times is the difference between a decision and an
    /// accident.</para>
    /// </summary>
    private void Bind(byte kind)
    {
        _boundStamp = _mirror.RebuildStamp;
        _appliedKey = -1; // a fresh clone repaints from scratch
        _appliedPointerBits = -1;
        System.Array.Clear(_roles, 0, _roles.Length);
        System.Array.Clear(_options, 0, _options.Length);
        _optionCount = 0;
        _amount = null;
        _takeDamageText = null;
        _damageIcon = null;
        _fatalIcon = null;
        _mandatory = null;
        _labels = System.Array.Empty<TMP_Text>();

        if (kind == NetProtocol.DecisionKindDialogPopup)
        {
            System.Collections.Generic.IReadOnlyList<Selectable> buttons = _dialogOptions.Buttons;
            for (int i = 0; i < buttons.Count && i < _options.Length; i++)
            {
                _options[i] = MakeNode(buttons[i]);
                if (_options[i].Clone != null)
                    _optionCount = i + 1;
            }
            BindLabels();
            return;
        }

        if (kind == NetProtocol.DecisionKindShortRestYesNo)
        {
            YesNoDialog? d = _boundDialog;
            if (d != null)
            {
                BindRole(NetProtocol.DecisionRoleShortRestYes, d.yesButton);
                BindRole(NetProtocol.DecisionRoleShortRestNo, d.noButton);
            }
            BindLabels();
            return;
        }

        TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
            ? Singleton<TakeDamagePanel>.Instance
            : null;
        if (p == null)
            return;

        BindRole(NetProtocol.DecisionRoleBurnAvailable, p.burnAvailableCardsToggle);
        BindRole(NetProtocol.DecisionRoleBurnDiscarded, p.burnDiscardedCardsToggle);
        BindRole(NetProtocol.DecisionRoleTakeDamage, p.takeDamageButton);

        _amount = CloneTmp(p.damageAmount);
        _takeDamageText = CloneTmp(p.takeDamageText);
        _damageIcon = CloneGo(p.damageObject != null ? p.damageObject.transform : null);
        _fatalIcon = CloneGo(p.fatalDamageObject != null ? p.fatalDamageObject.transform : null);
        _mandatory = CloneGo(p.mandatoryTakeDamageHighlight != null
            ? p.mandatoryTakeDamageHighlight.transform
            : null);

        _shieldText = p.shieldAppliedColor;
        UIInfoTools? info = UIInfoTools.Instance;
        _basicText = info != null ? info.basicTextColor : Color.white;
        _negativeText = info != null ? info.negativeTextColor : Color.white;

        BindLabels();
    }

    /// <summary>
    /// Collect the labels this class repaints — every TMP text UNDER A BOUND OPTION WIDGET, and
    /// nothing else.
    ///
    /// <para>That boundary is the owner's, not one chosen here: <c>AdjustDockedRow</c> restyles a
    /// docked prompt by walking its <c>Selectable</c>s and gilding the TMP texts inside them, so a
    /// text that is NOT inside an option — the short-rest question, which is a sibling under the
    /// dialog box — is left at its authored colour on the owner's board too. Following the same
    /// rule here is what makes the mirrored dialog match without a second styling decision.</para>
    /// </summary>
    private void BindLabels()
    {
        // Every remaining label: repainted to the dock's parchment gold, which is exactly what
        // AdjustDockedRow does to the OWNER's own row. The two take-damage texts are excluded
        // because the game itself keeps rewriting them (UpdateTakeDamageOptionVisuals) — see Apply.
        _labelScratch.Clear();
        for (int r = 1; r < _roles.Length + _options.Length; r++)
        {
            // The role slots first, then the popup's index-addressed options — one walk, because
            // "every label under a bound option widget" is one rule and not two.
            Transform? clone = r < _roles.Length
                ? _roles[r].Clone
                : _options[r - _roles.Length].Clone;
            if (clone == null)
                continue;
            clone.GetComponentsInChildren(includeInactive: true, TmpScratch);
            for (int i = 0; i < TmpScratch.Count; i++)
            {
                TMP_Text t = TmpScratch[i];
                if (t == null || ReferenceEquals(t, _amount) || ReferenceEquals(t, _takeDamageText))
                    continue;
                _labelScratch.Add(t);
            }
        }
        _labels = _labelScratch.ToArray();
        _labelScratch.Clear();
        TmpScratch.Clear();
    }

    /// <summary>Shared TMP walk buffer for <see cref="Bind"/> (bind runs on a clone rebuild, not
    /// per tick, but a per-rebuild allocation on a 4 Hz path is still worth not making).</summary>
    private static readonly List<TMP_Text> TmpScratch = new(16);

    private void BindRole(byte role, Selectable? source)
    {
        if (source == null || role >= _roles.Length)
            return;
        _roles[role] = MakeNode(source);
    }

    /// <summary>
    /// Resolve one option widget's repaintable parts against the clone — the ONE place a
    /// <see cref="RoleNode"/> is made, whether the option is addressed by ROLE (take-damage,
    /// short rest) or by INDEX (a <c>DialogPopup</c>'s pooled buttons).
    ///
    /// <para>Shared deliberately: the four <c>ColorBlock</c> tints, the toggle graphic and the
    /// dim group are what the paint reads, and two prompts resolving them two ways is exactly how a
    /// mirrored row starts looking almost right. Returns an empty node when the source is not in
    /// the clone, which every consumer already treats as "skip".</para>
    /// </summary>
    private RoleNode MakeNode(Selectable? source)
    {
        if (source == null)
            return default;
        Transform? clone = _mirror.CloneOf(source.transform);
        if (clone == null)
            return default;
        Graphic? srcBg = source.targetGraphic != null ? source.targetGraphic : source.image;
        var node = new RoleNode
        {
            Clone = clone,
            Background = srcBg != null ? CloneGraphic(srcBg.transform) : null,
            BaseColor = srcBg != null ? srcBg.color : Color.white,
            NormalTint = source.colors.normalColor,
            DisabledTint = source.colors.disabledColor,
            HighlightedTint = source.colors.highlightedColor,
            PressedTint = source.colors.pressedColor,
            TintMultiplier = source.colors.colorMultiplier > 0f ? source.colors.colorMultiplier : 1f,
        };
        if (source is Toggle toggle && toggle.graphic != null)
            node.ChosenGraphic = CloneGraphic(toggle.graphic.transform);
        var group = clone.GetComponent<CanvasGroup>();
        node.Group = group != null ? group : clone.gameObject.AddComponent<CanvasGroup>();
        return node;
    }

    private Graphic? CloneGraphic(Transform? src)
    {
        Transform? clone = _mirror.CloneOf(src);
        return clone != null ? clone.GetComponent<Graphic>() : null;
    }

    private TMP_Text? CloneTmp(TMP_Text? src)
    {
        Transform? clone = src != null ? _mirror.CloneOf(src.transform) : null;
        return clone != null ? clone.GetComponent<TMP_Text>() : null;
    }

    private GameObject? CloneGo(Transform? src)
    {
        Transform? clone = _mirror.CloneOf(src);
        return clone != null ? clone.gameObject : null;
    }

    /// <summary>
    /// Paint the wire onto the clone — the ONLY thing that decides what a viewer sees here, because
    /// the mirror is built with <c>driveFromSource: false</c> and never copies the (stale, hidden)
    /// source after the initial <c>Instantiate</c>. Every write carries its own change gate.
    ///
    /// <para>WHAT COMES FROM WHERE, one line each — because this is the whole 1:1 claim:
    /// <list type="bullet">
    ///   <item>the button SHAPE, its background sprite, the burn icons, the damage icon, the fatal
    ///     icon, the fonts and every WORDING — the receiver's own game assets, carried by the
    ///     clone. Nothing about them travelled.</item>
    ///   <item>WHICH options stand in the row — record 29 roles.</item>
    ///   <item>offered / dimmed / chosen — record 24, applied through the game's OWN disabled tint
    ///     and its own 0.7 dim, not through numbers invented here.</item>
    ///   <item>the damage NUMBER and the lethal / shielded / mandatory picture — record 29 flags,
    ///     because the receiver's own panel holds a stale copy (ShowOtherPlayer never repaints the
    ///     row, and the amount moves live as the owner toggles shields).</item>
    ///   <item>the ANTIQUE TINT — the LOCAL dock's own constant
    ///     (<c>DecisionDockSurface.AntiqueTint</c>), so the mirrored row wears the same VR restyle
    ///     the owner's docked row wears. It is a fixed part of that restyle and not a dial, so
    ///     there is nothing about it that could differ between two clients.</item>
    ///   <item>the parchment LABEL COLOUR — record 28 id 48, the OWNER's
    ///     <c>[ButtonColors] LabelR/G/B</c> (<see cref="_ownerLabelColor"/>). It used to be the
    ///     local <c>NativeButtonSkin.LabelColor</c> in this same list, which was the one line here
    ///     that read a VIEWER dial to decide what a PEER's board looks like — see
    ///     <c>WorldUI.NativeButtonSkin.LabelOwner</c>.</item>
    /// </list></para>
    /// </summary>
    private void Apply(RemoteAvatar owner, byte[] roles)
    {
        byte[]? states = owner.DecisionOptionStates;
        // A ROLLING HASH, not a bit-packed word. The packed version gave each option seven bits and
        // shifted it into place, which fitted exactly while an option state was three bits wide;
        // the two POINTER bits made it five, so adjacent options would have overlapped and two
        // different rows could have hashed alike. Only the LOG line rides this key, so the damage
        // would have been a missing line rather than a wrong picture — but a change gate that
        // silently stops detecting changes is precisely the instrument this project has been bitten
        // by, so it is a hash now and it cannot run out of bits. Forced non-negative so it can
        // never collide with the -1 "nothing applied yet" sentinel.
        long key = owner.DecisionWidgetFlags | ((long)owner.DecisionDamageAmount << 8);
        int shown = 0;
        // Walked to the LONGER of the two arrays: a DialogPopup row has states and no roles, and a
        // key bounded by the roles alone would have stopped detecting its changes entirely.
        int span = roles.Length;
        if (states != null && states.Length > span)
            span = states.Length;
        for (int i = 0; i < span && i < 8; i++)
        {
            byte state = states != null && i < states.Length
                ? states[i]
                : NetProtocol.DecisionOptionOfferedBit;
            key = key * 31 + (i < roles.Length ? roles[i] : 0);
            key = key * 31 + state;
        }
        key &= long.MaxValue;
        // THE PAINT ITSELF IS NOT GATED ON THIS KEY, deliberately: every write below carries its own
        // "only if it really changed" test, which is the gate that matters, and a coarse key would
        // be a correctness trap the moment anything else touched the clone between two ticks. The
        // key gates the LOG LINE alone, so a hardware log gets one line per real change instead of
        // four a second.
        bool announce = key != _appliedKey;
        _appliedKey = key;
        _appliedPointerBits = FoldPointerBits(states);

        // 1. Which roles the owner is showing, and in what state.
        byte[] present = _present;
        System.Array.Clear(present, 0, present.Length);
        for (int i = 0; i < roles.Length; i++)
        {
            byte role = roles[i];
            if (role == NetProtocol.DecisionRoleUnknown || role >= RoleSlots)
                continue;
            byte state = states != null && i < states.Length
                ? states[i]
                : NetProtocol.DecisionOptionOfferedBit;
            present[role] = (byte)(state | 0x80); // high bit = "the owner shows this widget"
        }

        Color antique = WorldUI.Surfaces.DecisionDockSurface.AntiqueTint;
        for (int role = 1; role < RoleSlots; role++)
        {
            if (PaintOption(_roles[role], present[role], antique))
                shown++;
        }

        // THE POPUP'S OPTIONS, addressed by wire index instead of by role — the same paint, the
        // same states, the same priority. They are a second LOOP and not a second painter: the
        // difference between the two prompts is how an option is NAMED, and nothing else.
        for (int i = 0; i < _optionCount && i < _options.Length; i++)
        {
            byte state = states != null && i < states.Length
                ? states[i]
                : NetProtocol.DecisionOptionOfferedBit;
            if (PaintOption(_options[i], (byte)(state | 0x80), antique))
                shown++;
        }

        // 2. The dock's parchment labels (AdjustDockedRow's own restyle, on the clone) — in the
        //    OWNER's [ButtonColors] fill, not this viewer's. This line used to be
        //    `HasFont ? NativeButtonSkin.LabelColor : new Color(0.91f, 0.82f, 0.62f)`, and both
        //    halves of it were wrong on a mirror: the first read the VIEWER's dial (so a viewer
        //    who set their labels red saw red lettering on every team-mate's decision row while
        //    the team-mate saw parchment), and the second stood in a duller gold than the owner's
        //    own dock, which has no HasFont gate at all. See WorldUI.NativeButtonSkin.LabelOwner.
        Color gold = WorldUI.NativeButtonSkin.LabelColorFor(
            WorldUI.NativeButtonSkin.LabelOwner.TheBoardOwnersDial, _ownerLabelColor);
        for (int i = 0; i < _labels.Length; i++)
        {
            TMP_Text t = _labels[i];
            if (t == null)
                continue;
            var c = new Color(gold.r, gold.g, gold.b, t.color.a);
            if (t.color != c)
                t.color = c;
        }

        PaintTakeDamageNumbers(owner, out bool lethal, out bool shielded, out bool mandatory,
                               out bool damageValid);

        if (!announce)
            return;
        VRLog.Info("Net", $"Remote decision row PAINTED: {shown} game widget(s) shown of " +
                          $"{roles.Length} wire role(s), owner pointer " +
                          $"{DescribePointer(states)} — damage " +
                          $"{(damageValid ? owner.DecisionDamageAmount.ToString() : "n/a")}" +
                          $"{(lethal ? " (FATAL icon)" : " (normal icon)")}" +
                          $"{(shielded ? ", shield colour" : string.Empty)}" +
                          $"{(mandatory ? ", mandatory highlight lit" : string.Empty)}. Greyed uses " +
                          "the game's OWN Selectable disabled tint and dim its own 0.7 — the " +
                          "wordings, icons and art are this client's assets in this player's " +
                          "language, so nothing about them was on the wire. Still inert: no " +
                          "collider, no raycaster, nothing to press.");
    }

    /// <summary>
    /// Paint ONE option widget from its packed state byte (high bit = the owner is showing it), and
    /// return whether it ended up shown.
    ///
    /// <para>The single painter for all three prompts. Every write carries its own change gate: a
    /// uGUI colour write dirties the canvas, and this runs on the content cadence AND on the
    /// per-frame pointer drive.</para>
    /// </summary>
    private static bool PaintOption(RoleNode node, byte packed, Color antique)
    {
        if (node.Clone == null)
            return false;
        bool on = (packed & 0x80) != 0;
        if (node.Clone.gameObject.activeSelf != on)
            node.Clone.gameObject.SetActive(on);
        if (!on)
            return false;
        {
            byte state = (byte)(packed & 0x7F);
            bool offered = (state & NetProtocol.DecisionOptionOfferedBit) != 0;
            bool dimmed = (state & NetProtocol.DecisionOptionDimmedBit) != 0;
            bool chosen = (state & NetProtocol.DecisionOptionChosenBit) != 0;
            bool hovered = (state & NetProtocol.DecisionOptionHoveredBit) != 0;
            bool pressed = (state & NetProtocol.DecisionOptionPressedBit) != 0;

            if (node.Background != null)
            {
                // THE GAME'S OWN COLORBLOCK, IN THE GAME'S OWN PRIORITY, times the dock's antique
                // tint: the very two multiplications the owner's docked widget renders through.
                //
                // The order is Selectable.DoStateTransition's, not one chosen here — disabled wins
                // over pressed, pressed over highlighted, highlighted over normal. Following the
                // game's precedence rather than inventing one is what keeps a greyed option that
                // happens to sit under the owner's beam looking greyed on every board, which is
                // what the owner is looking at. (The sender agrees with this from the other end:
                // SamplePointerBits publishes no pointer bit at all for a non-interactable option,
                // so the two sides cannot disagree about a widget in that state.)
                //
                // ONE HONEST DIFFERENCE, stated rather than hidden: uGUI CROSS-FADES between these
                // colours over ColorBlock.fadeDuration (0.1 s by default) and the mirror snaps. At
                // this row's size, over a network whose own latency is the same order, the fade is
                // not a picture a viewer can miss — and faking it would mean running a tween on the
                // remote board driven by a value that only updates when a packet lands.
                Color stateTint = offered
                    ? (pressed ? node.PressedTint : hovered ? node.HighlightedTint : node.NormalTint)
                    : node.DisabledTint;
                Color tint = stateTint * node.TintMultiplier;
                Color c = node.BaseColor * tint * antique;
                if (node.Background.color != c)
                    node.Background.color = c;
            }
            if (node.Group != null)
            {
                // TakeDamagePanel.UnactiveButtonTransparency — the game's own 0.7, not a guess.
                float alpha = dimmed ? 0.7f : 1f;
                if (!Mathf.Approximately(node.Group.alpha, alpha))
                    node.Group.alpha = alpha;
            }
            if (node.ChosenGraphic != null && node.ChosenGraphic.enabled != chosen)
                node.ChosenGraphic.enabled = chosen; // what Toggle.graphic does when it is on
        }
        return true;
    }

    /// <summary>
    /// The take-damage option's live numbers and icons (record 29 flags). Evaluated with the game's
    /// OWN formula (<c>UpdateTakeDamageOptionVisuals</c> / <c>RefreshDamageInformation</c>) against
    /// this client's own colours, from the booleans the wire carried — never a guess and never the
    /// sender's rendered pixels.
    ///
    /// <para>Every part is null for the other two prompts (their binds never resolve them), so this
    /// is a handful of null tests there rather than a branch.</para>
    /// </summary>
    private void PaintTakeDamageNumbers(RemoteAvatar owner, out bool lethal, out bool shielded,
                                        out bool mandatory, out bool damageValid)
    {
        byte flags = owner.DecisionWidgetFlags;
        lethal = (flags & NetProtocol.DecisionWidgetLethalBit) != 0;
        shielded = (flags & NetProtocol.DecisionWidgetShieldedBit) != 0;
        mandatory = (flags & NetProtocol.DecisionWidgetMandatoryBit) != 0;
        damageValid = (flags & NetProtocol.DecisionWidgetDamageValidBit) != 0;

        SetActive(_damageIcon, !lethal);
        SetActive(_fatalIcon, lethal);
        SetActive(_mandatory, mandatory);
        Color numberColor = shielded ? _shieldText : (lethal ? _negativeText : _basicText);
        if (_amount != null)
        {
            if (damageValid)
            {
                string text = owner.DecisionDamageAmount.ToString();
                if (_amount.text != text)
                    _amount.text = text; // a TMP write re-runs auto-size: change-gated, always
            }
            if (_amount.color != numberColor)
                _amount.color = numberColor;
        }
        if (_takeDamageText != null)
        {
            Color c = lethal ? _negativeText : _basicText;
            if (_takeDamageText.color != c)
                _takeDamageText.color = c;
        }
    }

    /// <summary>Which option the OWNER's pointer is on, for the paint line. A hardware log has to
    /// be able to answer "did the hover reach this board, and was it the right option" — the two
    /// pointer bits are the only per-viewer facts in the record, so they are the ones a "the hover
    /// is wrong" report will be argued from.</summary>
    private static string DescribePointer(byte[]? states)
    {
        if (states == null)
            return "n/a (a sender predating the pointer bits)";
        var sb = new System.Text.StringBuilder(24);
        for (int i = 0; i < states.Length; i++)
        {
            byte f = states[i];
            bool hovered = (f & NetProtocol.DecisionOptionHoveredBit) != 0;
            bool pressed = (f & NetProtocol.DecisionOptionPressedBit) != 0;
            if (!hovered && !pressed)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(pressed ? "PRESSING #" : "on #").Append(i);
        }
        return sb.Length > 0 ? sb.ToString() : "off the row";
    }

    private static void SetActive(GameObject? go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
