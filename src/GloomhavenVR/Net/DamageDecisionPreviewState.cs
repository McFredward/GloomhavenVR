using System;

namespace GloomhavenVR.Net;

/// <summary>Public, actor-addressed pending health picture. No game state or card identity.</summary>
internal sealed class DamageDecisionPreviewState
{
    internal int ActorId;
    internal int CommittedHealth, Damage, BaseDamage, OriginalMaxHealth;
    internal bool Validate() => ActorId != 0 && CommittedHealth >= 0 && CommittedHealth <= ushort.MaxValue
        && Damage >= 0 && Damage <= ushort.MaxValue && BaseDamage >= 0 && BaseDamage <= ushort.MaxValue
        && OriginalMaxHealth > 0 && OriginalMaxHealth <= ushort.MaxValue;
    internal static bool SamePicture(DamageDecisionPreviewState? a, DamageDecisionPreviewState? b) =>
        ReferenceEquals(a, b) || a != null && b != null && a.ActorId == b.ActorId
        && a.CommittedHealth == b.CommittedHealth && a.Damage == b.Damage
        && a.BaseDamage == b.BaseDamage && a.OriginalMaxHealth == b.OriginalMaxHealth;
}

/// <summary>Record57 payload: i32 actor, u16 committed HP, damage, base damage, original max HP.</summary>
internal static class DamageDecisionPreviewCodec
{
    internal const int MaxSize = 12;
    internal static int Write(DamageDecisionPreviewState state, byte[] buffer, int offset = 0)
    {
        if (state == null || !state.Validate() || buffer == null || offset < 0 || offset > buffer.Length - MaxSize) return 0;
        int at = offset;
        void U16(int value) { buffer[at++] = (byte)value; buffer[at++] = (byte)(value >> 8); }
        U16(state.ActorId); U16(state.ActorId >> 16);
        U16(state.CommittedHealth); U16(state.Damage); U16(state.BaseDamage); U16(state.OriginalMaxHealth);
        return MaxSize;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out DamageDecisionPreviewState? state)
    {
        state = null;
        if (buffer == null || length != MaxSize || offset < 0 || offset > buffer.Length - length) return false;
        int at = offset;
        int U16() { int value = buffer[at] | buffer[at + 1] << 8; at += 2; return value; }
        int actor = U16() | U16() << 16;
        var result = new DamageDecisionPreviewState { ActorId = actor, CommittedHealth = U16(),
            Damage = U16(), BaseDamage = U16(), OriginalMaxHealth = U16() };
        if (!result.Validate()) return false;
        state = result; return true;
    }
}

/// <summary>Keep the original-health endpoint fixed while mitigation changes projected HP.</summary>
internal static class DamageDecisionPreviewMath
{
    internal static int Damage(int committedHealth, int projectedHealth) =>
        (int)Math.Min(ushort.MaxValue, Math.Max(0L, (long)committedHealth - projectedHealth));
}
