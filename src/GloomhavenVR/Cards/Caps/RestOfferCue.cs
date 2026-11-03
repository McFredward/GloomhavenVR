using GloomhavenVR.Board;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>One quiet breathing rim for local and mirrored forced-rest offers.</summary>
internal sealed class RestOfferCue
{
    private WorldFrame? _frame;
    private Transform? _parent;
    private Vector2 _size;
    private Renderer[] _renderers = System.Array.Empty<Renderer>();
    internal static bool Wanted(int handCards, bool offered, bool selected) =>
        handCards >= 0 && handCards < 2 && offered && !selected;

    internal void Tick(Transform? parent, Vector2 size, bool wanted)
    {
        if (parent == null || !wanted) { _frame?.Apply(null); return; }
        if (_frame == null || _parent != parent || _size != size)
        {
            _frame?.Destroy();
            _parent = parent; _size = size;
            _frame = WorldFrame.Build(parent, "GloomhavenVR.RestRequiredCue",
                size + Vector2.one * 0.012f, 0.0015f, -0.006f);
            _renderers = _frame.Renderers;
        }
        _frame.Apply(new Color(0.78f, 0.61f, 0.28f, SelectionReadyHighlighter.PulseAlpha));
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head != null) Net.BoardVisual.OrderWithPanels(_renderers,
            (parent.position - head.transform.position).magnitude);
    }

    internal void Destroy() { _frame?.Destroy(); _frame = null; _parent = null; }
}
