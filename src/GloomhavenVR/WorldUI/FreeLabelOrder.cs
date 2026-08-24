using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// PERSPECTIVE FOR A FREE-FLOATING MOD LABEL — the label-shaped sibling of
/// <c>Cards.CardCueOrder</c> (card cues) and <c>Net.BoardVisual.OrderWithPanels</c> (identity
/// tags). Per frame, seat this label at the converted-panel distance ladder's answer for its OWN
/// eye distance, so it paints over everything genuinely behind it and under everything genuinely
/// in front of it.
///
/// <para>USER REPORT this was written for (hardware, 2026-08-25, <c>gegenstände_text.jpg</c>):
/// <i>"Mir ist aufgefallen, dass der Text über dem Item-Fächer von der Initiativreihenfolge
/// verdeckt wird … wie alles andere auch, soll hier auch die Perspektive gewahrt bleiben — ist der
/// Text davor, soll er auch davor sein und sich nicht die Bilder der Initiativreihenfolge dahinter
/// durchsetzen."</i> Scope correction from the same round, and it is why this is a shared
/// component rather than a line in <c>ItemsPile</c>: <i>"Selbstverständlich soll der fix mit dem
/// Text aus dem 'Gegenstände' Pile für jeglichen Text — auch der anderen Piles — gelten."</i></para>
///
/// ─── WHAT THE PIXELS SAID, AND WHY THIS IS NOT A DEPTH FIX ──────────────────────────────────────
/// In the screenshot the glyphs of "Gegenstände (5)" are cut with a razor edge along EACH portrait
/// card's own rect silhouette, are completely absent inside it (not a trace of orange showing
/// through — so this is not the alpha-compositing failure class), and are fully drawn in the black
/// gaps BETWEEN the portraits. That is a paint-order verdict, not a depth-buffer one: neither
/// subject writes depth. The label is a world <see cref="TextMeshPro"/> (TMP distance field,
/// queue 3000, <c>ZWrite Off</c>, <c>ZTest LEqual</c>, sortingLayer Default, sortingOrder <b>0</b>);
/// the initiative row is the game's <c>Panel_InitiativeTrack</c> converted to a world-space Canvas
/// whose <c>UI/Default</c> images are queue ~3000, <c>ZWrite Off</c>, and whose Canvas.sortingOrder
/// is rewritten every frame by the ladder (<c>CanvasConversion.ApplyPanelOrder</c>) to
/// ≥ <c>PanelOrderBase</c> (100). With no depth on either side Unity resolves them by
/// sortingLayer → sortingOrder → renderQueue → distance, and 0 loses to 100+ at every distance and
/// every angle. The label was not behind the portraits; it was simply painted before them.
///
/// ─── THE LEVER, AND WHO ELSE WRITES IT ──────────────────────────────────────────────────────────
/// The label's OWN <see cref="Renderer.sortingOrder"/>. Nothing else writes it: the label is a
/// mod-built GameObject that no game component owns, it is not in the control board's furniture
/// group (<c>PlayTray.AdoptFurniture</c> — that group's members are excluded here on purpose, see
/// below), and it is not an order follower of any panel. <c>MrBacking</c> READS it every tick and
/// copies it onto the label's backing plate, so the plate rides along for free — that seam already
/// existed and its own doc names the case ("an unranked order-0 label keeps an order-0 plate").
///
/// <para>DELIBERATELY NOT A FIXED WINNER. The answer is recomputed from the label's measured eye
/// distance every frame through <see cref="CanvasConversion.OrderAboveDistanceAndClusters"/>: a
/// menu (or the initiative row itself) that the player pulls genuinely IN FRONT of the label still
/// covers it, because the cluster-aware helper lowers the label's ceiling to just under any subject
/// measurably nearer. The label also keeps <c>ZTest LEqual</c>, so real depth-writing geometry — a
/// wall, the board slab, a card — still occludes it exactly as before.</para>
///
/// ─── WHICH LABELS RIDE THIS, AND WHICH MUST NOT ─────────────────────────────────────────────────
/// Only labels that hang in FREE SPACE with nothing depth-writing behind them: the pile fan titles
/// (<c>ItemsPile</c>, <c>PileBrowser</c>, <c>ActivePileViewer</c>) and the empty-fan placard over
/// the palm. A label lying ON the control board is NOT one of these: the board's opaque slab is
/// immediately behind it and already discards every farther panel per pixel, and those labels ride
/// the board's furniture band instead (<c>PlayTray.AdoptFurniture</c>) — a band that is capped
/// BELOW every board-docked panel on purpose. That cap is the structural fix of the 2026-08-04 #2
/// report ("Die Initiativbilder vermischen sich mit dem Text der Statustafel … je nach Winkel
/// ploppt es manchmal auf"): distance may never arbitrate INSIDE the board's own plane, because the
/// board's big face rect and the docked track's thin strip are two nearest-point measures that move
/// differently as the head orbits. Putting a furniture label on this component instead would give
/// it a SECOND writer of the same field and re-open exactly that defect — see
/// <c>CanvasConversion.9.Furniture.cs</c>'s ROUND 2 header.
///
/// ─── TMP SUB-MESHES ─────────────────────────────────────────────────────────────────────────────
/// A <see cref="TMP_Text"/> that needs a fallback font atlas or an inline sprite spawns CHILD
/// GameObjects with their own <see cref="Renderer"/>s, and those copy the parent's sortingOrder
/// only at spawn time. Writing the parent alone would leave a fallback glyph (the "✓" of
/// "✓ READY" is the recorded case) stranded in the defect band. The child set is rescanned only
/// when <c>transform.childCount</c> moves, so the steady-state cost is one int compare. The scan is
/// filtered to <see cref="TMP_SubMesh"/> carriers, which is also what keeps it off
/// <c>MrBacking</c>'s plate — that plate is a child of the label too, and it has its own writer.
///
/// <para>COST: one <c>Vector3.Distance</c>, one walk over the listed panels plus the ≤2 furniture
/// clusters, and a change-gated int write, per ACTIVE label per frame — the same price
/// <c>CardCueOrder</c>, <c>WristHud</c> and the board tooltip already pay. At most four of these
/// components exist and each is on a GameObject that is inactive unless its fan is open, so a
/// closed fan costs nothing at all. No scene sweep, no <c>FindObjectsOfType</c>.</para>
///
/// <para>MULTIPLAYER: local presentation only. No game state is read or written, no wire field is
/// added, and the value is derived per client from that client's own head pose — a peer's mirrored
/// board keeps ranking through its own cluster (<c>Net.BoardVisual</c>), untouched.</para>
/// </summary>
internal sealed class FreeLabelOrder : MonoBehaviour
{
    /// <summary>
    /// Sub-step lift above the farther subject's ladder slot. Deliberately the SAME value as
    /// <c>Cards.CardCueOrder.CuePanelLift</c> and <c>Net.BoardVisual.TagPanelLift</c>, for the same
    /// reason: it must stay under <c>CanvasConversion.PanelOrderStep</c> (16) so a label can never
    /// climb into the NEXT panel's slot, and 12 clears that window's own decorations (close X +2,
    /// grab bar +4, menu-laid tooltip +10) — a title hanging in front of a window covers the whole
    /// window, its furniture included. Sharing the constant also means a fan title and that fan's
    /// own card cues resolve against each other by DISTANCE (equal order → renderQueue → distance)
    /// instead of by an arbitrary tier.
    /// </summary>
    private const int LabelPanelLift = 12;

    /// <summary>Floor between two attribution lines from ONE label. The line is change-gated first;
    /// this only caps the burst rate if a label sits exactly on a ladder boundary and chatters, and
    /// a change suppressed by it is remembered (<see cref="_logPending"/>) and printed on the next
    /// allowed tick, so the log can never claim a stale order.</summary>
    private const float LogMinIntervalSeconds = 2f;

    /// <summary>
    /// Attach the ranking to a free-floating mod LABEL (idempotent; a null label is a no-op).
    /// Board-mounted labels must NOT use this — see the class doc's WHICH LABELS section.
    /// </summary>
    internal static void Rank(TMP_Text? label)
    {
        if (label != null)
            Rank(label.gameObject);
    }

    /// <summary>
    /// Attach the ranking to any free-floating mod renderer that carries a label's plate or ink
    /// (idempotent; null and renderer-less objects are no-ops). Used for the empty-fan placard's
    /// parchment quad, which must ride the ladder with the ink line it backs.
    /// </summary>
    internal static void Rank(GameObject? carrier)
    {
        if (carrier == null || carrier.GetComponent<FreeLabelOrder>() != null)
            return;
        if (carrier.GetComponent<Renderer>() == null)
            return; // nothing whose sortingOrder could be written
        carrier.AddComponent<FreeLabelOrder>();
    }

    private Renderer? _renderer;
    private TMP_Text? _label;

    /// <summary>TMP fallback/sprite sub-mesh renderers under this label (see the class doc).</summary>
    private readonly List<Renderer> _subMeshes = new(2);

    /// <summary>Child count the sub-mesh list was built from; -1 forces the first scan.</summary>
    private int _subMeshChildCount = -1;

    /// <summary>Last written order (change-gate). <c>int.MinValue</c> = never written, so the first
    /// tick always seats the label — order 0 is the defect band itself.</summary>
    private int _applied = int.MinValue;

    private float _nextLogAt;
    private bool _logPending;
    private int _logPrevious = int.MinValue;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _label = GetComponent<TMP_Text>();
    }

    /// <summary>
    /// LateUpdate, not Update: the fans pose themselves in their own tick, and ranking a label from
    /// a stale pose would be one frame late on a fan that is still flying to its anchor. Reads the
    /// PREVIOUS frame's measured panel/cluster distances (<c>CanvasConversion.TickPanelOrder</c>
    /// runs last in the WorldUI chain) — a one-frame lag on a hysteresis-damped ladder is not
    /// observable, the same trade every other ladder caller already accepts.
    /// </summary>
    private void LateUpdate()
    {
        if (_renderer == null)
            return;
        Camera? cam = CanvasConversion.WorldCamera;
        if (cam == null)
            return;

        float eyeDistance = Vector3.Distance(cam.transform.position, transform.position);
        int order = CanvasConversion.OrderAboveDistanceAndClusters(eyeDistance, LabelPanelLift);
        bool subMeshesMoved = RefreshSubMeshes();
        bool orderMoved = order != _applied;
        // _logPending is part of the exit test on purpose: a change whose line the throttle
        // swallowed must still be able to reach LogOrder on a LATER, otherwise-idle frame. Without
        // it a swallowed change would be silently dropped for good, and the hardware log would show
        // a stale order — the "gated remedy never ran" failure this project has paid for before.
        if (!orderMoved && !subMeshesMoved && !_logPending)
            return; // steady scene, nothing owed: no write at all

        if (orderMoved || subMeshesMoved)
        {
            if (orderMoved && !_logPending)
                _logPrevious = _applied; // the value the NEXT line reports as "was"
            _logPending |= orderMoved;
            _applied = order;
            _renderer.sortingOrder = order;
            for (int i = 0; i < _subMeshes.Count; i++)
            {
                if (_subMeshes[i] != null)
                    _subMeshes[i].sortingOrder = order;
            }
        }
        if (_logPending)
            LogOrder(order, eyeDistance);
    }

    /// <summary>
    /// Rebuild <see cref="_subMeshes"/> when TMP has added or removed a sub-mesh child. Returns
    /// true on a rebuild, which forces the caller to re-seat the (possibly brand new) children even
    /// when the order itself did not move. Steady state: one int compare.
    /// </summary>
    private bool RefreshSubMeshes()
    {
        int n = transform.childCount;
        if (n == _subMeshChildCount)
            return false;
        _subMeshChildCount = n;
        _subMeshes.Clear();
        for (int i = 0; i < n; i++)
        {
            Transform child = transform.GetChild(i);
            // TMP_SubMesh ONLY: a label's other children are not ours to order — MrBacking's
            // backing plate is a child too and copies this label's live order through its own tick.
            if (child.GetComponent<TMP_SubMesh>() == null)
                continue;
            var r = child.GetComponent<Renderer>();
            if (r != null)
                _subMeshes.Add(r);
        }
        return true;
    }

    /// <summary>
    /// The falsifier for the next hardware log: names THIS label, the subject it now covers and the
    /// subject that now covers it, with the distances and orders the decision was made from. A
    /// "the title is still hidden" report is then decidable from the log alone — either the line is
    /// absent (the label was never ranked), or it names the initiative track BEHIND the label at a
    /// lower order (the fix ran and the picture must agree), or it names it IN FRONT (the label is
    /// correctly under it and the complaint is about geometry, not order).
    /// </summary>
    private void LogOrder(int order, float eyeDistance)
    {
        float now = Time.unscaledTime;
        if (now < _nextLogAt)
            return; // keep _logPending set: the next allowed tick reports the CURRENT order
        _nextLogAt = now + LogMinIntervalSeconds;
        _logPending = false;
        string what = _label != null && !string.IsNullOrEmpty(_label.text) ? _label.text : name;
        string was = _logPrevious == int.MinValue ? "unranked (0)" : _logPrevious.ToString();
        VRLog.Info("WorldUI",
            $"FREE LABEL ORDER: '{what}' (d={eyeDistance:F2} m) -> sortingOrder {order} (was {was}); " +
            CanvasConversion.DescribeOrderNeighbours(eyeDistance) +
            ". It now paints OVER every subject measurably behind it and UNDER every subject in front.");
        _logPrevious = order;
    }
}
