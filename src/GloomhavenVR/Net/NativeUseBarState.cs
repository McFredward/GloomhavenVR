using System;

namespace GloomhavenVR.Net;

/// <summary>Public effect/action selectors, never ability-card identities or owner text.</summary>
internal enum NativeUseBarModelKind : byte
{
    DummyInfusion, Ability, InfuseAction, ChooseAbility, Augment, AugmentGroup, Item,
}

internal sealed class NativeUseBarAugment
{
    internal byte IconSymbol, ConsumeSymbol, Option, Flags;
    internal float[] ConsumePosition = new float[3];
    internal bool Validate() => IconSymbol <= 8 && ConsumeSymbol <= 7 && Option <= 32
        && (Flags & ~31) == 0 && ConsumePosition != null && ConsumePosition.Length == 3
        && NativeUseBarState.Finite(ConsumePosition);
    internal NativeUseBarAugment Snapshot() => new() { IconSymbol = IconSymbol,
        ConsumeSymbol = ConsumeSymbol, Option = Option, Flags = Flags,
        ConsumePosition = (float[])ConsumePosition.Clone() };
}

internal sealed class NativeUseBarAnimationEntry
{
    internal byte AnimatorIndex;
    internal UseBarAnimationValue Value = new();
    internal bool Validate() => AnimatorIndex <= 32 && Value != null && Value.Validate();
    internal NativeUseBarAnimationEntry Snapshot() => new() { AnimatorIndex = AnimatorIndex, Value = Value.Snapshot() };
}

/// <summary>One original ability, augmentation or item slot. Bounded visual descriptors select
/// local original prefab content and original animation targets; no controller runs remotely.</summary>
internal sealed class NativeUseBarState
{
    internal byte Bar, Slot;
    internal int ActorId;
    internal NativeUseBarModelKind ModelKind;
    internal Guid ModelId;
    internal ushort ModelIndex, ModelCount, SlotIdentity;
    internal UseBarWidgetState WidgetState = new();
    internal byte[] InfuseIcons = Array.Empty<byte>();
    internal NativeUseBarAugment[] Augments = Array.Empty<NativeUseBarAugment>();
    internal float[] HighlightColors = Array.Empty<float>();
    internal byte PreviewOption;
    internal bool PreviewVisible;
    internal NativeUseBarAnimationEntry[] Animations = Array.Empty<NativeUseBarAnimationEntry>();
    internal int Address => Bar * 8 + Slot;

    internal bool Validate()
    {
        if (Bar < 1 || Bar > 3 || Slot >= 8 || ActorId == 0 || SlotIdentity == 0
            || WidgetState == null || WidgetState.Slot != Slot || !WidgetState.Validate()
            || InfuseIcons == null || InfuseIcons.Length > 32 || Augments == null || Augments.Length > 32
            || HighlightColors == null || HighlightColors.Length > 128 || HighlightColors.Length % 4 != 0
            || !Finite(HighlightColors) || PreviewOption > 32 || Animations == null || Animations.Length > 16)
            return false;
        if (Bar == 1 && (ModelKind < NativeUseBarModelKind.DummyInfusion || ModelKind > NativeUseBarModelKind.ChooseAbility)
            || Bar == 2 && ModelKind != NativeUseBarModelKind.Augment && ModelKind != NativeUseBarModelKind.AugmentGroup
            || Bar == 3 && ModelKind != NativeUseBarModelKind.Item) return false;
        bool needsId = ModelKind != NativeUseBarModelKind.DummyInfusion && ModelKind != NativeUseBarModelKind.Item;
        if (needsId == (ModelId == Guid.Empty)) return false;
        if (Bar == 2 && (ModelCount == 0 || ModelIndex >= ModelCount)) return false;
        foreach (byte icon in InfuseIcons) if (icon > 7) return false;
        foreach (NativeUseBarAugment augment in Augments) if (augment == null || !augment.Validate()) return false;
        for (int i = 0; i < Animations.Length; i++)
        {
            if (Animations[i] == null || !Animations[i].Validate()) return false;
            for (int j = 0; j < i; j++)
                if (Animations[i].AnimatorIndex == Animations[j].AnimatorIndex
                    && Animations[i].Value.SettingIndex == Animations[j].Value.SettingIndex) return false;
        }
        return true;
    }
    internal static bool Finite(float[] values)
    { foreach (float value in values) if (!UseBarAnimationValue.Finite(value)) return false; return true; }
    internal NativeUseBarState Snapshot()
    {
        var copy = (NativeUseBarState)MemberwiseClone();
        copy.WidgetState = WidgetState.Snapshot(); copy.InfuseIcons = (byte[])InfuseIcons.Clone();
        copy.HighlightColors = (float[])HighlightColors.Clone();
        copy.Augments = Array.ConvertAll(Augments, item => item.Snapshot());
        copy.Animations = Array.ConvertAll(Animations, item => item.Snapshot());
        return copy;
    }
    internal static bool SameState(NativeUseBarState? a, NativeUseBarState? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Bar != b.Bar || a.Slot != b.Slot || a.ActorId != b.ActorId
            || a.ModelKind != b.ModelKind || a.ModelId != b.ModelId || a.ModelIndex != b.ModelIndex
            || a.ModelCount != b.ModelCount || a.SlotIdentity != b.SlotIdentity
            || a.PreviewOption != b.PreviewOption || a.PreviewVisible != b.PreviewVisible
            || !UseBarWidgetState.SameState(a.WidgetState, b.WidgetState)
            || !Same(a.InfuseIcons, b.InfuseIcons) || !Same(a.HighlightColors, b.HighlightColors)
            || a.Augments.Length != b.Augments.Length || a.Animations.Length != b.Animations.Length) return false;
        for (int i = 0; i < a.Augments.Length; i++)
        {
            NativeUseBarAugment x = a.Augments[i], y = b.Augments[i];
            if (x.IconSymbol != y.IconSymbol || x.ConsumeSymbol != y.ConsumeSymbol || x.Option != y.Option
                || x.Flags != y.Flags || !Same(x.ConsumePosition, y.ConsumePosition)) return false;
        }
        for (int i = 0; i < a.Animations.Length; i++)
        {
            NativeUseBarAnimationEntry x = a.Animations[i], y = b.Animations[i];
            if (x.AnimatorIndex != y.AnimatorIndex || x.Value.SettingIndex != y.Value.SettingIndex
                || x.Value.Kind != y.Value.Kind || !Same(x.Value.Values, y.Value.Values)) return false;
        }
        return true;
    }
    private static bool Same<T>(T[] a, T[] b) where T : IEquatable<T>
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false; return true; }
}

internal sealed class NativeUseBarSnapshot
{
    internal readonly float SampleTime;
    internal readonly byte Bar, Slot;
    internal readonly NativeUseBarState? State;
    internal int Address => Bar * 8 + Slot;
    internal NativeUseBarSnapshot(float sampleTime, byte bar, byte slot, NativeUseBarState? state)
    {
        if (!UseBarAnimationValue.Finite(sampleTime) || sampleTime < 0 || bar < 1 || bar > 3 || slot >= 8
            || state != null && (state.Bar != bar || state.Slot != slot || !state.Validate()))
            throw new ArgumentException("Invalid native use-bar snapshot.");
        SampleTime = sampleTime; Bar = bar; Slot = slot; State = state?.Snapshot();
    }
}
