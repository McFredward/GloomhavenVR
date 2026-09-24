using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal enum TownActivitySound : byte { None, Coin, Cloth, Spell }

/// <summary>Audible contacts follow the displayed shared performance. Joining, seeking,
/// authority changes and hidden stations seed silently; no historical sounds are replayed.</summary>
internal sealed class TownServiceActivitySoundClock
{
    private bool _seeded;
    private int _author;
    private uint _epoch;
    private float _clock, _cooldown;
    private TownActivityVisual _previous;

    internal void Reset() { _seeded = false; _cooldown = 0f; }

    internal TownActivitySound Sample(byte service, int author, uint epoch, float clock,
        float elapsed, bool visible, in TownActivityVisual shown)
    {
        if (!visible) { Reset(); return TownActivitySound.None; }
        float delta = clock - _clock;
        bool continuous = _seeded && author == _author && epoch == _epoch
            && delta >= -.001f && delta <= .25f && elapsed >= 0f && elapsed <= .25f;
        TownActivitySound result = TownActivitySound.None;
        _cooldown = Mathf.Max(0f, _cooldown - Mathf.Max(0f, elapsed));
        if (continuous && _cooldown <= 0f)
        {
            if (service == 1 && (Released(_previous.CoinGrip.x, shown.CoinGrip.x)
                || Released(_previous.CoinGrip.y, shown.CoinGrip.y)
                || Released(_previous.CoinGrip.z, shown.CoinGrip.z)))
                result = TownActivitySound.Coin;
            else if (service == 3 && _previous.Cast <= .16f && shown.Cast > .16f)
                result = TownActivitySound.Spell;
            else if ((_previous.Attention < .25f && shown.Attention >= .25f)
                || (_previous.Attention > .75f && shown.Attention <= .75f)
                || (service == 2 && _previous.Left.y > 1.31f && shown.Left.y <= 1.31f))
                result = TownActivitySound.Cloth;
        }
        _seeded = true; _author = author; _epoch = epoch; _clock = clock; _previous = shown;
        if (result != TownActivitySound.None) _cooldown = result == TownActivitySound.Cloth ? 2f : .35f;
        return result;
    }

    private static bool Released(float before, float after) => before > .99f && after < .01f;
}
