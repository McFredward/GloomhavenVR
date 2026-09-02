using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// WHAT THE TWO-HAND RESIZE GESTURE NEEDS FROM THE THING IT IS RESIZING — ten members, and
/// nothing else.
///
/// <para><b>THE REPORT (2026-09-03), verbatim.</b> <i>"Die props in der Hand soll man wie die
/// Figuren entsprechend auf skallieren können! (mit exakt den selben Lösungen auf die Probleme
/// die dafür schon implementiert wurden, nuzte am Besten denselben Code wenmöglich)."</i> — "use
/// the same code where possible" is the requirement, not a preference, so this exists instead of
/// a second copy of <see cref="FigureStretch"/>.</para>
///
/// <para><b>WHY AN ADAPTER AND NOT AN INTERFACE ON THE TWO GRABBABLES.</b> The obvious shape is
/// <c>interface IStretchable</c> implemented by <see cref="FigureGrabbable"/> and
/// <see cref="GrabbableProp"/>. Every member below already exists on <c>FigureGrabbable</c> with
/// exactly this signature — but declaring the interface on it is an edit to a file this lane does
/// not own, and an interface nobody may implement is not an abstraction. Adapting from the
/// OUTSIDE needs no edit there at all: <see cref="FigureStretch"/> asks these ten questions, the
/// two subclasses answer them by forwarding to members that are already public enough. The
/// interface can replace this later without touching the gesture; see
/// <c>.planning/LANE-PROPS-357-NEEDED-OUTSIDE.md</c> for that patch.</para>
///
/// <para><b>THE TEN MEMBERS ARE THE COMPLETE CONTRACT</b>, arrived at by reading every call
/// <c>FigureStretch</c> makes: <c>HeldBy</c>, <c>TryGetHeldCenter</c>, <c>Stretch</c>,
/// <c>SetStretch</c>, <c>GetStretchFactorBounds</c>, <c>IsHeld</c>, <c>Label</c>,
/// <c>HeldRenderers</c>, <c>TotalHeldSizeRatio</c>, <c>NoteCaptureVolume</c>. Nothing about
/// actors, ghosts, cloth, wire slots or info panels crosses this seam, which is precisely why one
/// gesture can drive a 30 mm miniature and a hex-sized chest without knowing which it has.</para>
///
/// <para><b>ALLOCATION-FREE BY CONSTRUCTION.</b> The gesture asks <see cref="HeldBy"/> once per
/// hand per frame, so allocating an adapter per call would be two garbage objects every frame for
/// the whole session. Instead four adapters exist for the life of the process — one per (kind,
/// hand side) — and are RE-POINTED at whatever that hand holds. That makes adapter identity
/// useless as object identity, which is why <see cref="Owner"/> exists: the gesture stores the
/// OWNER it started on and re-checks it every frame, so a release-and-regrab inside one gesture
/// ends the gesture instead of silently continuing on a different object.</para>
///
/// <para><b>FIGURES BEFORE PROPS, and the order is load-bearing exactly once.</b> A hand holds one
/// object (<c>ProximityGrabber</c> guarantees it), so the two lookups can never both answer — the
/// order below is a tie-break that cannot be reached. It is written figure-first anyway so the
/// established path is the one taken when a future change makes both answerable.</para>
///
/// <para><b>MULTIPLAYER.</b> This is a dispatch seam and carries no state. Its two subjects differ
/// on the wire and stay that way: a figure's stretch rides
/// <c>NetProtocol.ExtIdHeldStretch</c> (record 30), sampled by <c>Net/NetFigures</c> off
/// <c>FigureGrabbable.Stretch</c> on its own cadence; a PROP hold sends nothing at all and no peer
/// renders one, so a prop's factor has nothing to be mirrored against. See
/// <see cref="GrabbableProp.SetStretch"/> for that verdict in full.</para>
/// </summary>
internal abstract class StretchTarget
{
    /// <summary>The grabbable this adapter currently points at — the gesture's IDENTITY token, not
    /// a thing to call. Adapters are re-pointed every frame (see the class doc), so comparing
    /// adapters would compare nothing.</summary>
    internal abstract object? Owner { get; }

    /// <summary>Still attached to a hand? False ends a live gesture on the next frame.</summary>
    internal abstract bool IsHeld { get; }

    /// <summary>The log vocabulary for this object — a figure's <c>Describe()</c> or a prop's
    /// prefab-name-plus-import-type, so one gesture line reads the same either way.</summary>
    internal abstract string Label { get; }

    /// <summary>The manual in-hand stretch factor of this hold (1 = untouched).</summary>
    internal abstract float Stretch { get; }

    /// <summary>Write the factor and re-assert the rendered size in the same call. The CALLER owns
    /// the clamp — both implementations are dumb stores, so a sampler and the renderer can never
    /// see two differently-clamped values.</summary>
    internal abstract void SetStretch(float factor);

    /// <summary>This hold's factor envelope, converted from the TOTAL size bounds at the latch's
    /// own ratio. Asked per frame so a live dial edit governs the next frame.</summary>
    internal abstract void GetStretchFactorBounds(out float min, out float max);

    /// <summary>The held object's centre in world space — its root position, deliberately NOT a
    /// surface point (a surface point moves with the scale being written).</summary>
    internal abstract bool TryGetHeldCenter(out Vector3 world);

    /// <summary>The held visual's renderers, cached per hold, for the surface-based capture test.
    /// Entries can go Unity-null mid-hold; the consumer null-checks each.</summary>
    internal abstract Renderer[]? HeldRenderers();

    /// <summary>This hold's TOTAL size in default-zoom units (latch ratio × stretch) — the
    /// zoom-independent size variable the capture ceiling scales by.</summary>
    internal abstract float TotalHeldSizeRatio { get; }

    /// <summary>Diagnostic bookkeeping: the capture volume the test last trusted.</summary>
    internal abstract void NoteCaptureVolume(float bodyRadiusRealMeters, float ceilingRealMeters);

    // ---- the re-pointed adapters ----------------------------------------------------------------

    private static readonly FigureTarget[] Figures = { new FigureTarget(), new FigureTarget() };
    private static readonly PropTarget[] Props = { new PropTarget(), new PropTarget() };

    /// <summary>The stretchable object attached to <paramref name="side"/>'s hand, or null. Walks
    /// the two held sets (never more than two entries each) and returns a RE-POINTED adapter — see
    /// the class doc for why that is safe and why <see cref="Owner"/> is the identity.</summary>
    internal static StretchTarget? HeldBy(HandSide side)
    {
        int i = (int)side;
        FigureGrabbable? figure = FigureGrabbable.HeldBy(side);
        if (figure != null)
            return Figures[i].Bind(figure);
        GrabbableProp? prop = GrabbableProp.HeldBy(side);
        return prop != null ? Props[i].Bind(prop) : null;
    }

    /// <summary>Drop every adapter's payload — driver teardown and the config gate's ReleaseAll.
    /// Without it a torn-down grabbable stays reachable through a static field for the rest of the
    /// session, which is the pin <c>FigureStretch.Clear</c>'s own comment refuses to leave.</summary>
    internal static void ClearAll()
    {
        for (int i = 0; i < 2; i++)
        {
            Figures[i].Bind(null);
            Props[i].Bind(null);
        }
    }

    private sealed class FigureTarget : StretchTarget
    {
        private FigureGrabbable? _g;

        internal FigureTarget Bind(FigureGrabbable? g)
        {
            _g = g;
            return this;
        }

        internal override object? Owner => _g;

        internal override bool IsHeld => _g != null && _g.IsHeld;

        internal override string Label => _g != null ? _g.Label : "<gone>";

        internal override float Stretch => _g != null ? _g.Stretch : 1f;

        internal override void SetStretch(float factor) => _g?.SetStretch(factor);

        internal override void GetStretchFactorBounds(out float min, out float max)
        {
            if (_g == null)
            {
                min = FigureGrabConfig.StretchHardFloor;
                max = float.MaxValue;
                return;
            }
            _g.GetStretchFactorBounds(out min, out max);
        }

        internal override bool TryGetHeldCenter(out Vector3 world)
        {
            world = default;
            return _g != null && _g.TryGetHeldCenter(out world);
        }

        internal override Renderer[]? HeldRenderers() => _g?.HeldRenderers();

        internal override float TotalHeldSizeRatio => _g != null ? _g.TotalHeldSizeRatio : 1f;

        internal override void NoteCaptureVolume(float bodyRadiusRealMeters, float ceilingRealMeters)
            => _g?.NoteCaptureVolume(bodyRadiusRealMeters, ceilingRealMeters);
    }

    private sealed class PropTarget : StretchTarget
    {
        private GrabbableProp? _p;

        internal PropTarget Bind(GrabbableProp? p)
        {
            _p = p;
            return this;
        }

        internal override object? Owner => _p;

        internal override bool IsHeld => _p != null && _p.IsHeld;

        internal override string Label => _p != null ? _p.Label : "<gone>";

        internal override float Stretch => _p != null ? _p.Stretch : 1f;

        internal override void SetStretch(float factor) => _p?.SetStretch(factor);

        internal override void GetStretchFactorBounds(out float min, out float max)
        {
            if (_p == null)
            {
                min = FigureGrabConfig.StretchHardFloor;
                max = float.MaxValue;
                return;
            }
            _p.GetStretchFactorBounds(out min, out max);
        }

        internal override bool TryGetHeldCenter(out Vector3 world)
        {
            world = default;
            return _p != null && _p.TryGetHeldCenter(out world);
        }

        internal override Renderer[]? HeldRenderers() => _p?.HeldRenderers();

        internal override float TotalHeldSizeRatio => _p != null ? _p.TotalHeldSizeRatio : 1f;

        internal override void NoteCaptureVolume(float bodyRadiusRealMeters, float ceilingRealMeters)
            => _p?.NoteCaptureVolume(bodyRadiusRealMeters, ceilingRealMeters);
    }
}
