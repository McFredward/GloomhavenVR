using System.Text;

namespace GloomhavenVR.Core;

/// <summary>
/// State-specific figure A/B windows. The Build606 hardware sweep changed both figure
/// sliders inside a single FRAME interval, so it proved mesh replacement but could not
/// price it. Preparation and stable rendering need separate intervals. This policy reads
/// four scalar settings and a driver readiness stamp; it never prepares or writes a mesh.
/// </summary>
internal sealed class PerfFigureMeasurement
{
    internal readonly struct Settings
    {
        internal readonly int Players, Enemies, Effects;
        internal readonly bool Cloth;
        internal Settings(int players, int enemies, int effects, bool cloth)
        { Players = players; Enemies = enemies; Effects = effects; Cloth = cloth; }
        internal bool Same(Settings other) => Players == other.Players && Enemies == other.Enemies
            && Effects == other.Effects && Cloth == other.Cloth;
        internal void Append(StringBuilder text) => text.Append(" players=").Append(Players)
            .Append(" enemies=").Append(Enemies).Append(" fx=").Append(Effects)
            .Append(" cloth=").Append(Cloth);
    }

    internal enum Boundary { None, Preparing, Steady }
    internal const float SettleSeconds = 2f;
    internal bool HasState { get; private set; }
    internal bool IsSteady { get; private set; }
    internal int Revision { get; private set; }
    internal Settings Current { get; private set; }
    private float _steadyAfter;

    internal Boundary Observe(Settings wanted, bool applied, float now)
    {
        if (!HasState || !Current.Same(wanted) || (IsSteady && !applied))
        {
            Current = wanted; HasState = true; IsSteady = false; Revision++;
            _steadyAfter = now + SettleSeconds;
            return Boundary.Preparing;
        }
        // Readiness may lag the setting change (bank loading, late application, or an
        // open options window). Start the quiet guard after that work, not before it.
        if (!applied) _steadyAfter = now + SettleSeconds;
        if (!IsSteady && applied && now >= _steadyAfter)
        { IsSteady = true; return Boundary.Steady; }
        return Boundary.None;
    }

    internal void Clear()
    { HasState = false; IsSteady = false; Revision = 0; _steadyAfter = 0f; }
}
