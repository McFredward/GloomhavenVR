using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ScenarioRuleLibrary;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>A character's original decision picture, independent of which board is viewing it.</summary>
internal sealed class CharacterDecisionPresentation
{
    private sealed class Entry
    {
        internal readonly CharacterDecisionPresentation Board = new(), Character = new();
        internal int ActorId;
        internal bool Pending, Suppressed, Visible = true;
    }
    private static readonly ConditionalWeakTable<RemoteAvatar, Entry> Entries = new();
    internal bool Visible { get; private set; } = true;
    internal int PlayerId { get; private set; }
    internal string? DecisionNames { get; private set; }
    internal string? DecisionLines { get; private set; }
    internal byte DecisionPromptKind { get; private set; }
    internal byte DecisionTextVariant { get; private set; }
    internal byte DecisionWidgetFlags { get; private set; }
    internal byte DecisionDamageAmount { get; private set; }
    internal byte UseBarsMask { get; private set; }
    internal byte[]? DecisionOptionStates { get; private set; }
    internal byte[]? DecisionRoles { get; private set; }
    internal byte[]? UseBarFlags { get; private set; }
    internal byte[]? UseBarSlotCounts { get; private set; }
    internal byte[]? UseBarSlotStates { get; private set; }
    internal ushort[]? UseBarSlotIds { get; private set; }
    internal UseBarWidgetState[]? UseBarWidgetStates { get; private set; }
    internal NativeDecisionHighlightState? DecisionHighlight { get; private set; }
    internal NativeUseBarState?[] NativeUseBarStates { get; private set; } = null!;
    internal float[] NativeUseBarSampleTimes { get; private set; } = null!;
    internal List<NativeUseBarSnapshot>[] NativeUseBarHistories { get; private set; } = null!;
    internal UseBarAnimationState[]? AnimationStates { get; private set; }
    internal float AnimationSampleTime { get; private set; }
    internal List<UseBarAnimationSnapshot> AnimationHistory { get; private set; } = null!;

    private static readonly CharacterDecisionPresentation Local = NewLocal();
    private static int LocalFrame = -1;
    private static CharacterDecisionPresentation NewLocal()
    {
        var state = new CharacterDecisionPresentation {
            DecisionOptionStates = new byte[8], DecisionRoles = new byte[8],
            UseBarFlags = new byte[4], UseBarSlotCounts = new byte[4], UseBarSlotStates = new byte[32],
            UseBarSlotIds = new ushort[32], NativeUseBarStates = new NativeUseBarState?[32],
            NativeUseBarSampleTimes = new float[32], NativeUseBarHistories = new List<NativeUseBarSnapshot>[32],
            AnimationHistory = new List<UseBarAnimationSnapshot>(32)
        };
        for (int i = 0; i < 32; i++) state.NativeUseBarHistories[i] = new List<NativeUseBarSnapshot>(32);
        return state;
    }

    internal static bool TryGetLocal(CPlayerActor actor, out CharacterDecisionPresentation? picture)
    {
        picture = null;
        if (GloomhavenVR.Board.CharacterFocus.IsForeign(actor)) return false;
        UseBarsSurface.SampleDecisionAttribution(out int id, out bool pending, out _);
        if (!pending || id != NetFigures.StableActorId(actor)) return false;
        Local.CaptureLocal(); picture = Local; return true;
    }

    private void CaptureLocal()
    {
        DecisionLines = DecisionDockSurface.WireButtonLines;
        DecisionNames = DamageTooltipSurface.WireMandatoryNames;
        DecisionPromptKind = DecisionDockSurface.WirePromptKind;
        DecisionTextVariant = DamageTooltipSurface.WireTextVariant;
        DecisionWidgetFlags = DecisionDockSurface.WireWidgetFlags;
        DecisionDamageAmount = DecisionDockSurface.WireDamageAmount;
        DecisionDockSurface.CopyWireOptionStates(DecisionOptionStates!);
        DecisionDockSurface.CopyWireOptionRoles(DecisionRoles!);
        UseBarsMask = UseBarsSurface.WireBarMask;
        UseBarsSurface.CopyWireBars(UseBarFlags, UseBarSlotCounts, UseBarSlotStates, UseBarSlotIds);
        UseBarWidgetStates = UseBarsSurface.WireWidgetStates;
        DecisionHighlight = NativeDecisionHighlightSampler.Sample();
        UseBarsSurface.ReadNativePresentation(out UseBarAnimationState[]? animation, out NativeUseBarState?[] native);
        if (LocalFrame == Time.frameCount) return;
        LocalFrame = Time.frameCount;
        if (!ReferenceEquals(AnimationStates, animation))
        {
            AnimationStates = animation; AnimationSampleTime = Time.unscaledTime;
            if (AnimationHistory.Count == 32) AnimationHistory.RemoveAt(0);
            AnimationHistory.Add(new UseBarAnimationSnapshot(AnimationSampleTime, animation ?? System.Array.Empty<UseBarAnimationState>()));
        }
        for (int i = 8; i < 32; i++)
        {
            if (ReferenceEquals(NativeUseBarStates[i], native[i])) continue;
            NativeUseBarStates[i] = native[i]; NativeUseBarSampleTimes[i] = Time.unscaledTime;
            if (NativeUseBarHistories[i].Count == 32) NativeUseBarHistories[i].RemoveAt(0);
            NativeUseBarHistories[i].Add(new NativeUseBarSnapshot(Time.unscaledTime, (byte)(i / 8), (byte)(i % 8), native[i]));
        }
    }

    internal static CharacterDecisionPresentation From(RemoteAvatar owner)
    {
        Entry entry = Entries.GetOrCreateValue(owner);
        entry.Board.Capture(owner);
        entry.Board.Visible = !entry.Suppressed && (!entry.Pending || entry.Visible);
        return entry.Board;
    }

    internal static void Observe(RemoteAvatar owner, int actorId, bool pending, bool visible)
    {
        Entry entry = Entries.GetOrCreateValue(owner);
        entry.ActorId = pending ? actorId : 0;
        entry.Pending = pending && actorId != 0;
        entry.Visible = visible;
        if (entry.Pending) entry.Character.Capture(owner);
    }

    internal static void Refresh(RemoteAvatar owner)
    {
        if (Entries.TryGetValue(owner, out Entry entry) && entry.Pending)
            entry.Character.Capture(owner);
    }

    internal static bool TryGet(RemoteAvatar owner, CPlayerActor actor, out CharacterDecisionPresentation? picture)
    {
        picture = null;
        if (!Entries.TryGetValue(owner, out Entry entry) || !entry.Pending
            || entry.ActorId != NetFigures.StableActorId(actor)) return false;
        entry.Character.Capture(owner);
        entry.Character.Visible = true;
        picture = entry.Character;
        return true;
    }

    internal static bool BoardVisible(RemoteAvatar owner) =>
        !Entries.TryGetValue(owner, out Entry entry) || !entry.Suppressed && (!entry.Pending || entry.Visible);

    internal static void SuppressBoard(RemoteAvatar owner, bool suppress) =>
        Entries.GetOrCreateValue(owner).Suppressed = suppress;

    internal static void Remove(RemoteAvatar owner) => Entries.Remove(owner);

    private void Capture(RemoteAvatar owner)
    {
        PlayerId = owner.PlayerId;
        DecisionLines = owner.DecisionLines;
        DecisionNames = owner.DecisionNames;
        DecisionPromptKind = owner.DecisionPromptKind;
        DecisionTextVariant = owner.DecisionTextVariant;
        DecisionWidgetFlags = owner.DecisionWidgetFlags;
        DecisionDamageAmount = owner.DecisionDamageAmount;
        UseBarsMask = owner.UseBarsMask;
        DecisionOptionStates = owner.DecisionOptionStates;
        DecisionRoles = owner.DecisionRoles;
        UseBarFlags = owner.UseBarFlags;
        UseBarSlotCounts = owner.UseBarSlotCounts;
        UseBarSlotStates = owner.UseBarSlotStates;
        UseBarSlotIds = owner.UseBarSlotIds;
        UseBarWidgetStates = owner.UseBarWidgetStates;
        DecisionHighlight = owner.DecisionHighlight;
        NativeUseBarStates = owner.NativeUseBarStates;
        NativeUseBarSampleTimes = owner.NativeUseBarSampleTimes;
        NativeUseBarHistories = owner.NativeUseBarHistories;
        AnimationStates = owner.AnimationStates;
        AnimationSampleTime = owner.AnimationSampleTime;
        AnimationHistory = owner.AnimationHistory;
    }
}
