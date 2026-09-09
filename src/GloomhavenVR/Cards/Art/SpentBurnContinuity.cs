using System;

namespace GloomhavenVR.Cards;

/// <summary>Retains only the actual spent shader channels of one model through its burn start.</summary>
internal sealed class SpentBurnContinuity<T> where T : class
{
    private T? _card;
    private float[]? _spent;

    internal void Clear() { _card = null; _spent = null; }

    internal void Apply(T card, bool spent, bool burning, float[] channels)
    {
        if (!ReferenceEquals(_card, card)) Clear();
        if (burning)
        {
            if (_spent == null || _spent.Length != channels.Length) return;
            for (int i = 0; i < channels.Length; i++)
                if (Finite(_spent[i]) && Finite(channels[i])) channels[i] = Math.Max(channels[i], _spent[i]);
            return;
        }
        if (!spent) { Clear(); return; }
        _card = card;
        if (_spent == null || _spent.Length != channels.Length)
        { _spent = (float[])channels.Clone(); return; }
        // Native RefreshPile can transiently reset a still-spent widget. It cannot erase an
        // observed wash until the model actually recovers or changes identity.
        for (int i = 0; i < channels.Length; i++)
            if (Finite(channels[i]) && (!Finite(_spent[i]) || channels[i] > _spent[i])) _spent[i] = channels[i];
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
