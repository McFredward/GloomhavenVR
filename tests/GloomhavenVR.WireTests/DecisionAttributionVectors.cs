using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class DecisionAttributionVectors
{
    internal static void Run(Harness t)
    {
        t.Case("489: actor decisions and health survive focus changes together");
        var buffer = new byte[PresenceSerializer.MaxSize];
        var state = new PresenceState { HasDecisionAttribution = true, DecisionActorId = 0x01020304,
            DecisionPending = true, DecisionVisible = false,
            DamageDecisionPreview = new DamageDecisionPreviewState { ActorId = 0x01020304,
                CommittedHealth = 7, Damage = 1, BaseDamage = 2, OriginalMaxHealth = 10 } };
        int size = PresenceSerializer.Write(state, buffer);
        t.True(PresenceSerializer.TryRead(buffer, size, out var decoded) && decoded.HasDecisionAttribution
            && decoded.DecisionPending && !decoded.DecisionVisible && decoded.DecisionActorId == 0x01020304
            && DamageDecisionPreviewState.SamePicture(decoded.DamageDecisionPreview, state.DamageDecisionPreview),
            "hidden owner focus still carries the correct actor and shield preview");
        state.DecisionPending = false; state.DamageDecisionPreview = null;
        size = PresenceSerializer.Write(state, buffer);
        t.True(PresenceSerializer.TryRead(buffer, size, out decoded) && decoded.HasDecisionAttribution
            && !decoded.DecisionPending && decoded.DecisionActorId == 0 && decoded.DamageDecisionPreview == null,
            "actual completion clears preview and character attachment");
        buffer[size - 1] = 2;
        t.True(PresenceSerializer.TryRead(buffer, size, out decoded) && !decoded.HasDecisionAttribution,
            "visible without pending cannot fabricate a decision");
    }
}
