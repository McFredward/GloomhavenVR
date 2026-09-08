using System;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Actual native smoke on a public list/seat. No card identifier, name or art travels.</summary>
internal sealed class CardPlumeState
{
    internal const int CountMax = 64;
    internal const byte RoundList = 7; // plume-only; record36 keeps its existing six-list domain
    internal const byte Playing = 1, Paused = 2, Emitting = 4, Looping = 8;
    internal int ActorId;
    internal byte FaceCode, ListCount, Flags, EmitterIndex;
    internal uint Episode, RandomSeed;
    internal float Age, PlaybackRate, StartSizeMultiplier, StartSpeedMultiplier;
    internal bool CustomSpacePresent;
    internal Vector3 CustomPosition, CustomScale;
    internal Quaternion CustomRotation;
    internal Color Color;
    internal Vector3 LocalPosition, LocalScale;
    internal Quaternion LocalRotation;

    internal bool Validate()
    {
        if (ActorId == 0 || !(NetProtocol.HeldFaceNamesCard(FaceCode)
                || (NetProtocol.HeldFaceList(FaceCode) == RoundList
                    && NetProtocol.HeldFaceIndex(FaceCode) != NetProtocol.HeldFaceIndexUnknown))
            || ListCount <= NetProtocol.HeldFaceIndex(FaceCode) || ((Flags >> 4) & 3) == 3 || (Flags >> 6) == 3
            || !Finite(PlaybackRate) || PlaybackRate < 0 || !Finite(Age) || Age < 0 || !Finite(StartSizeMultiplier) || !Finite(StartSpeedMultiplier)) return false;
        if (CustomSpacePresent && (((Flags >> 4) & 3) != 2
            || !Finite(CustomPosition.x) || !Finite(CustomPosition.y) || !Finite(CustomPosition.z)
            || !Finite(CustomScale.x) || !Finite(CustomScale.y) || !Finite(CustomScale.z)
            || !Finite(CustomRotation.x) || !Finite(CustomRotation.y) || !Finite(CustomRotation.z) || !Finite(CustomRotation.w)
            || CustomRotation.x * CustomRotation.x + CustomRotation.y * CustomRotation.y
                + CustomRotation.z * CustomRotation.z + CustomRotation.w * CustomRotation.w <= 0.0001f)) return false;
        return Finite(Color.r) && Finite(Color.g) && Finite(Color.b) && Finite(Color.a)
            && Finite(LocalPosition.x) && Finite(LocalPosition.y) && Finite(LocalPosition.z)
            && Finite(LocalScale.x) && Finite(LocalScale.y) && Finite(LocalScale.z)
            && Finite(LocalRotation.x) && Finite(LocalRotation.y) && Finite(LocalRotation.z) && Finite(LocalRotation.w)
            && LocalRotation.x * LocalRotation.x + LocalRotation.y * LocalRotation.y
                + LocalRotation.z * LocalRotation.z + LocalRotation.w * LocalRotation.w > 0.0001f;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal CardPlumeState Snapshot() => (CardPlumeState)MemberwiseClone();

    internal static bool Same(CardPlumeState a, CardPlumeState b) => a.ActorId == b.ActorId
        && a.FaceCode == b.FaceCode && a.ListCount == b.ListCount && a.Flags == b.Flags
        && a.EmitterIndex == b.EmitterIndex && a.StartSizeMultiplier == b.StartSizeMultiplier
        && a.PlaybackRate == b.PlaybackRate && a.CustomSpacePresent == b.CustomSpacePresent
        && (!a.CustomSpacePresent || (a.CustomPosition.Equals(b.CustomPosition)
            && a.CustomRotation.Equals(b.CustomRotation) && a.CustomScale.Equals(b.CustomScale)))
        && a.StartSpeedMultiplier == b.StartSpeedMultiplier && a.Episode == b.Episode && a.RandomSeed == b.RandomSeed && a.Age == b.Age
        && a.Color.Equals(b.Color) && a.LocalPosition.Equals(b.LocalPosition)
        && a.LocalRotation.Equals(b.LocalRotation) && a.LocalScale.Equals(b.LocalScale);
}

internal sealed class CardPlumeSnapshot
{
    internal readonly float SampleTime;
    internal readonly CardPlumeState[] States;
    internal CardPlumeSnapshot(float sampleTime, CardPlumeState[] states)
    {
        if (float.IsNaN(sampleTime) || float.IsInfinity(sampleTime) || sampleTime < 0
            || states == null || states.Length > CardPlumeState.CountMax)
            throw new ArgumentException("Invalid card plume frame.");
        States = new CardPlumeState[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == null || !states[i].Validate()) throw new ArgumentException("Invalid native card plume.");
            for (int j = 0; j < i; j++)
                if (states[j].ActorId == states[i].ActorId && states[j].FaceCode == states[i].FaceCode
                    && states[j].EmitterIndex == states[i].EmitterIndex)
                    throw new ArgumentException("Duplicate native card plume seat.");
            States[i] = states[i].Snapshot();
        }
        SampleTime = sampleTime;
    }
}
