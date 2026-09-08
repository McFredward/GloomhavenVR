using System;

namespace GloomhavenVR.Net;

/// <summary>Native visual property selected by an original show-animation setting. Values are
/// sampled after animation writes; no tween, callback, card name or shader-property name travels.</summary>
internal enum UseBarAnimationKind : byte
{
    Scale3 = 0,
    AnchoredPosition3 = 1,
    LocalPosition3 = 2,
    SizeDelta2 = 3,
    UvPosition2 = 4,
    CanvasGroupAlpha1 = 5,
    GraphicAlpha1 = 6,
    TextAlpha1 = 7,
    GraphicColor4 = 8,
    TextColor4 = 9,
    MaterialFloat1 = 10,
    CustomFill1 = 11,
    CustomSizeDelta2 = 12,
}

internal sealed class UseBarAnimationValue
{
    internal byte SettingIndex;
    internal UseBarAnimationKind Kind;
    internal float[] Values = Array.Empty<float>();

    internal static int Components(UseBarAnimationKind kind) => kind switch
    {
        UseBarAnimationKind.Scale3 or UseBarAnimationKind.AnchoredPosition3
            or UseBarAnimationKind.LocalPosition3 => 3,
        UseBarAnimationKind.SizeDelta2 or UseBarAnimationKind.UvPosition2
            or UseBarAnimationKind.CustomSizeDelta2 => 2,
        UseBarAnimationKind.CanvasGroupAlpha1 or UseBarAnimationKind.GraphicAlpha1
            or UseBarAnimationKind.TextAlpha1 or UseBarAnimationKind.MaterialFloat1
            or UseBarAnimationKind.CustomFill1 => 1,
        UseBarAnimationKind.GraphicColor4 or UseBarAnimationKind.TextColor4 => 4,
        _ => 0,
    };

    internal bool Validate()
    {
        int components = Components(Kind);
        if (components == 0 || Values == null || Values.Length != components) return false;
        foreach (float value in Values)
            if (!Finite(value)) return false;
        return true;
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal UseBarAnimationValue Snapshot() => new()
        { SettingIndex = SettingIndex, Kind = Kind, Values = (float[])Values.Clone() };
}

/// <summary>One native slot's sampled settings. Actor and existing record-45 slot identity prevent
/// a delayed animation frame from moving a replacement bonus. Arrays are immutable after publication.</summary>
internal sealed class UseBarAnimationState
{
    internal const int CountMax = 16;
    internal byte Slot;
    internal int ActorId;
    internal ushort SlotIdentity;
    internal UseBarAnimationValue[] Entries = Array.Empty<UseBarAnimationValue>();

    internal bool Validate()
    {
        if (Slot >= NetProtocol.UseBarsMaxSlots || ActorId == 0
            || SlotIdentity == UseBarSlotIdentity.NoIdentity || Entries == null || Entries.Length > CountMax)
            return false;
        for (int i = 0; i < Entries.Length; i++)
        {
            if (Entries[i] == null || !Entries[i].Validate()) return false;
            for (int j = 0; j < i; j++)
                if (Entries[j].SettingIndex == Entries[i].SettingIndex) return false;
        }
        return true;
    }

    internal UseBarAnimationState Snapshot()
    {
        var entries = new UseBarAnimationValue[Entries.Length];
        for (int i = 0; i < entries.Length; i++) entries[i] = Entries[i].Snapshot();
        return new UseBarAnimationState { Slot = Slot, ActorId = ActorId, SlotIdentity = SlotIdentity, Entries = entries };
    }

    internal static bool SameState(UseBarAnimationState? a, UseBarAnimationState? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Slot != b.Slot || a.ActorId != b.ActorId
            || a.SlotIdentity != b.SlotIdentity || a.Entries.Length != b.Entries.Length) return false;
        for (int i = 0; i < a.Entries.Length; i++)
        {
            UseBarAnimationValue x = a.Entries[i], y = b.Entries[i];
            if (x.SettingIndex != y.SettingIndex || x.Kind != y.Kind || x.Values.Length != y.Values.Length) return false;
            for (int j = 0; j < x.Values.Length; j++)
                if (x.Values[j] != y.Values[j]) return false;
        }
        return true;
    }

    internal static bool Equivalent(UseBarAnimationState[]? a, UseBarAnimationState[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!SameState(a[i], b[i])) return false;
        return true;
    }
}

/// <summary>One complete owner frame, copied at publication. Consumers must not mutate its arrays.</summary>
internal sealed class UseBarAnimationSnapshot
{
    internal readonly float SampleTime;
    internal readonly UseBarAnimationState[] States;

    internal UseBarAnimationSnapshot(float sampleTime, UseBarAnimationState[] states)
    {
        if (!UseBarAnimationValue.Finite(sampleTime) || sampleTime < 0 || states == null
            || states.Length > NetProtocol.UseBarsMaxSlots)
            throw new ArgumentException("Invalid native animation snapshot.");
        int seen = 0;
        foreach (UseBarAnimationState state in states)
        {
            if (state == null || !state.Validate() || (seen & (1 << state.Slot)) != 0)
                throw new ArgumentException("Invalid native animation slot.");
            seen |= 1 << state.Slot;
        }
        SampleTime = sampleTime;
        States = new UseBarAnimationState[states.Length];
        for (int i = 0; i < states.Length; i++) States[i] = states[i].Snapshot();
    }
}
