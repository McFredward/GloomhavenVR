using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal enum TownActivitySound : byte { None, Coin, Spell1, Spell2, Spell3, Spell4, Spell5 }

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
            {
                // The occupation clock is authored once and replicated to every peer.
                // Deriving the take from its 48-second experiment block therefore gives
                // all listeners the same foley without adding another wire field or
                // consulting client-local randomness.
                int take = (int)Mathf.Floor(Mathf.Max(0f, clock) / 48f) % 5;
                result = (TownActivitySound)((int)TownActivitySound.Spell1 + take);
            }
            // Attention is a pose transition, not a physical contact. Builds 560-566 mapped
            // approach/departure and one priestess hand edge to the game's flat equipment-toggle
            // UI clip. Because the copied clip was spatialized at each resident it sounded like
            // windows opening/closing on every range crossing and at apparently random positions.
            // Do not invent foley for a transition. Coin releases and spell casts remain tied to
            // visible physical events; voices are scheduled independently.
        }
        _seeded = true; _author = author; _epoch = epoch; _clock = clock; _previous = shown;
        // Counting is a background gesture, not an alert on every coin. One
        // subdued contact per cycle remains enough to locate the work.
        if (result != TownActivitySound.None)
            _cooldown = result == TownActivitySound.Coin ? 2.4f : .35f;
        return result;
    }

    internal static bool IsSpell(TownActivitySound sound) =>
        sound >= TownActivitySound.Spell1 && sound <= TownActivitySound.Spell5;

    private static bool Released(float before, float after) => before > .99f && after < .01f;
}
