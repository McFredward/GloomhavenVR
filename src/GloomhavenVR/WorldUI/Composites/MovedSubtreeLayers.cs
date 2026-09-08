using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>WHICH LAYER A MOD-MOVED uGUI SUBTREE IS ON WHILE A COMPOSITE HOLDS IT, AND WHAT THE GAME HAD
/// THERE BEFORE.</b> One record per composite; the walk, the guarded hand-back and the two counts a
/// composite's report line prints.
///
/// <para><b>WHY IT EXISTS (refactor 2026-09, REVIEW-worldui-front.md F3).</b> Three composites had
/// written this same record — <c>StoryComposite</c>, <c>LoadoutConfirmPark</c> and
/// <c>HintOnOwnerComposite</c>, whose own doc already said <i>"<c>StoryComposite.WriteLayers</c> and
/// <c>LoadoutConfirmPark.WriteLayers</c>, verbatim"</i>. Four fields, a recursive walk and a
/// restore, three times, byte-identical after normalisation. This is the class of duplication the
/// user's standing preference names directly (<i>"alles nochmal neu … schreiben obwohl doch das
/// meiste schon … gebaut wurde"</i>): one MECHANISM, three copies that can drift.</para>
///
/// <para><b>WHY A LAYER IS WRITTEN AT ALL, which the composites must keep stating for themselves.</b>
/// Per-window capture cameras cull BY LAYER ([[one-shared-layer-leaks]]), and the host a composite
/// parks content INTO is already converted when the content arrives — <c>CanvasConversion.ApplyModLayer</c>
/// and <c>PanelSupersample.ApplyCaptureLayer</c> both re-sweep for late children, but on their OWN
/// cadence, so a subtree that arrives between two sweeps is drawn by the wrong camera or by two.
/// Writing the host root's own live layer immediately is what closes that window, and it cannot start
/// a write war: both of those sweeps are change-gated on <c>layer != theirs</c>, so neither records
/// anything for a transform this record has already written.</para>
///
/// <para><b>A FOREIGN RENDER SUBTREE IS SKIPPED WHOLE — AND NOT ITS CHILDREN EITHER.</b> That is
/// <c>CanvasConversion.ApplyModLayer</c>'s own rule for its own reason: a real <see cref="Renderer"/>
/// under a uGUI tree is 3D owned by another camera, and descending into it would take its children
/// with it. <see cref="CanvasRenderer"/> is not a <see cref="Renderer"/>, so ordinary uGUI is
/// unaffected. <see cref="Skipped"/> counts those subtrees so a report line can say how many.</para>
///
/// <para><b>THE RESTORE IS GUARDED, AND THE GUARD IS THE POINT.</b> A layer is handed back only where
/// the transform is STILL on the layer this record wrote — <c>PanelSupersample.RestoreLayers</c>'s
/// guard, for its reason: a transform somebody else has since re-layered is no longer ours to hand
/// back, and writing our stale value would strand it on a layer no camera renders. (The other writer
/// is real: <c>CanvasConversion.Release</c> restores the released panel's own <c>Relayered</c>
/// records, which NAME the very transforms a composite may be holding, with no "is it still mine"
/// test. That is what <c>StoryComposite.LayersDrifted</c> watches for, and why the composites
/// re-assert rather than assume.)</para>
///
/// <para><b>NOTHING HERE IS SHARED STATE.</b> Each composite owns its own instance, because the
/// composites can be parked at once and each hands back its own transforms. Nothing here is
/// networked: a layer decides which of THIS client's cameras draws a subtree.</para>
/// </summary>
internal sealed class MovedSubtreeLayers
{
    private readonly List<Transform> _tx;
    private readonly List<int> _was;
    private int _written = -1;
    private int _skipped;

    /// <param name="capacity">Initial list capacity — the composite's own, so a record that
    /// routinely moves 60 transforms does not grow its lists on every park.</param>
    internal MovedSubtreeLayers(int capacity)
    {
        _tx = new List<Transform>(capacity);
        _was = new List<int>(capacity);
    }

    /// <summary>The layer this record last wrote, or −1 when it holds nothing. A composite tests it
    /// before deciding whether a re-assert is even needed.</summary>
    internal int Written => _written;

    /// <summary>How many transforms this record wrote — report material.</summary>
    internal int Count => _tx.Count;

    /// <summary>How many foreign render subtrees the last walk skipped whole — report material.</summary>
    internal int Skipped => _skipped;

    /// <summary>Hand back whatever is held, then arm for a fresh walk onto
    /// <paramref name="layer"/>. Every <see cref="Walk"/> of one pass follows one
    /// <see cref="Begin"/>, so the skip count belongs to that pass alone.</summary>
    internal void Begin(int layer)
    {
        Restore();
        _written = layer;
        _skipped = 0;
    }

    /// <summary>Write <see cref="Written"/> onto <paramref name="t"/> and its whole uGUI subtree,
    /// recording each transform's original layer. Skips a foreign render subtree whole.</summary>
    internal void Walk(Transform t)
    {
        if (t.GetComponent<Renderer>() != null)
        {
            _skipped++;
            return;   // and NOT its children either — that is the whole point
        }
        if (t.gameObject.layer != _written)
        {
            _tx.Add(t);
            _was.Add(t.gameObject.layer);
            t.gameObject.layer = _written;
        }
        for (int i = t.childCount - 1; i >= 0; i--)
            Walk(t.GetChild(i));
    }

    /// <summary>Hand every layer this record wrote back to the value the GAME had there — and only
    /// where the transform is STILL on the layer we wrote (see the class doc for that guard's
    /// reason). Idempotent; a destroyed transform is skipped.</summary>
    internal void Restore()
    {
        for (int i = 0; i < _tx.Count; i++)
        {
            Transform? t = _tx[i];
            if (t != null && t.gameObject.layer == _written)
                t.gameObject.layer = _was[i];
        }
        _tx.Clear();
        _was.Clear();
        _written = -1;
    }

    /// <summary>Drop the records WITHOUT handing anything back — for the path where the transforms
    /// are already gone, so there is nothing to write to. The skip count survives, because the
    /// report line that names it may still run.</summary>
    internal void Forget()
    {
        _tx.Clear();
        _was.Clear();
        _written = -1;
    }

    /// <summary><see cref="Forget"/> plus the skip count: module teardown, where nothing is left to
    /// report either.</summary>
    internal void Reset()
    {
        Forget();
        _skipped = 0;
    }
}
