using System;

namespace GloomhavenVR.Net;

/// <summary>Original card FX output, addressed only through existing actor/list positions.</summary>
internal sealed class CardAppearanceNode
{
    internal const int RoleCount = 20, ValueCount = 33;
    internal byte Role, Flags;
    internal uint Binding; // stable original CanvasGroup path, only roles12..19 // active, enabled, TMP gradient
    internal uint Mask;
    // Graphic RGBA, renderer RGBA, fifteen shader floats, two shader colours, noise scale.
    internal float[] Values = new float[ValueCount];
    internal static uint AllowedMask(byte role) => role < 7 ? 0x2807Fu : role == 11 ? 0x17F80u : 0u;
    internal bool Validate()
    {
        if (Role >= RoleCount || (Flags & ~31) != 0 || Role >= 7 && (Flags & 16) != 0 || Role != 11 && (Flags & 8) != 0
            || Role < 12 && Binding != 0 || Role >= 12 && Binding == 0 || (Role < 7 || Role == 11) && (Flags & 4) != 0
            || Role >= 12 && (Values == null || Values.Length == 0 || Values[0] < 0f || Values[0] > 1f)
            || (Mask & ~AllowedMask(Role)) != 0 || Values == null || Values.Length != ValueCount) return false;
        foreach (float value in Values) if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        return true;
    }
    internal CardAppearanceNode Copy() => new() { Role = Role, Flags = Flags, Binding = Binding, Mask = Mask, Values = (float[])Values.Clone() };
    internal static bool Carries(uint mask, int index) => index < 8
        || index < 23 && (mask & (1u << (index - 8))) != 0
        || index < 27 && index >= 23 && (mask & (1u << 15)) != 0
        || index < 31 && index >= 27 && (mask & (1u << 16)) != 0
        || index >= 31 && (mask & (1u << 17)) != 0;
}

internal sealed class CardAppearanceState
{
    internal const int CountMax = 32;
    internal int ActorId;
    internal byte FaceCode, ListCount;
    internal CardAppearanceNode[] Nodes = Array.Empty<CardAppearanceNode>();
    internal bool Validate()
    {
        if (ActorId == 0 || ListCount == 0 || NetProtocol.HeldFaceIndex(FaceCode) >= ListCount
            || (!NetProtocol.HeldFaceNamesCard(FaceCode)
                && NetProtocol.HeldFaceList(FaceCode) != CardPlumeState.RoundList)
            || NetProtocol.HeldFaceIndex(FaceCode) == NetProtocol.HeldFaceIndexUnknown
            || Nodes == null || Nodes.Length == 0 || Nodes.Length > CardAppearanceNode.RoleCount) return false;
        int roles = 0;
        var groups = new System.Collections.Generic.HashSet<uint>();
        foreach (CardAppearanceNode node in Nodes)
        {
            if (node == null || !node.Validate() || (roles & (1 << node.Role)) != 0) return false;
            roles |= 1 << node.Role;
            if (node.Role >= 12 && !groups.Add(node.Binding)) return false;
        }
        return true;
    }
    internal CardAppearanceState Copy()
    {
        var copy = new CardAppearanceState { ActorId = ActorId, FaceCode = FaceCode, ListCount = ListCount,
            Nodes = new CardAppearanceNode[Nodes.Length] };
        for (int i = 0; i < Nodes.Length; i++) copy.Nodes[i] = Nodes[i].Copy();
        return copy;
    }
    internal static bool Same(CardAppearanceState a, CardAppearanceState b)
    {
        if (a.ActorId != b.ActorId || a.FaceCode != b.FaceCode || a.ListCount != b.ListCount || a.Nodes.Length != b.Nodes.Length) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
        {
            var x = a.Nodes[i]; var y = b.Nodes[i];
            if (x.Role != y.Role || x.Flags != y.Flags || x.Binding != y.Binding || x.Mask != y.Mask) return false;
            for (int j = 0; j < x.Values.Length; j++) if (x.Values[j] != y.Values[j]) return false;
        }
        return true;
    }
}

internal sealed class CardAppearanceSnapshot
{
    internal readonly float SampleTime;
    internal readonly CardAppearanceState[] States;
    internal static bool SameIdentity(CardAppearanceSnapshot a, CardAppearanceSnapshot b)
    {
        if (a.States.Length != b.States.Length) return false;
        for (int i = 0; i < a.States.Length; i++)
            if (a.States[i].ActorId != b.States[i].ActorId || a.States[i].FaceCode != b.States[i].FaceCode
                || a.States[i].ListCount != b.States[i].ListCount) return false;
        return true;
    }
    internal CardAppearanceSnapshot(float time, CardAppearanceState[] states)
    {
        if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || states == null || states.Length > CardAppearanceState.CountMax)
            throw new ArgumentException("Invalid card appearance frame.");
        SampleTime = time;
        States = new CardAppearanceState[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == null || !states[i].Validate()) throw new ArgumentException("Invalid native card appearance.");
            for (int j = 0; j < i; j++) if (states[j].ActorId == states[i].ActorId && states[j].FaceCode == states[i].FaceCode)
                throw new ArgumentException("Duplicate card appearance address.");
            States[i] = states[i].Copy();
        }
    }
}
