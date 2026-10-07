using System;
using GloomhavenVR.Net;
using UnityEngine;
public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool pass, string message)
    { _checks++; if (!pass) throw new InvalidOperationException(message); }
    private static AvatarState Pair(float time, bool rigid)
    {
        Vector3 position = new Vector3(Mathf.Sin(time * 9f) * 3f, Mathf.Cos(time * 13f) * 2f, time * .2f);
        Quaternion rotation = Quaternion.Euler(time * 200f, time * -140f, time * 80f);
        return new AvatarState {
            HeadValid = true, Head = new RigPose { Position = new Vector3(0, 4, -3), Rotation = Quaternion.identity },
            HasHeldCard = true, HeldCardPose = new RigPose { Position = position, Rotation = rotation },
            HasSecondHeldCardState = true, HasSecondHeldCard = true,
            SecondHeldCardPose = new RigPose { Position = position + Vector3.right, Rotation = rotation },
            HasSecondHeldCardFace = true, SecondHeldFaceCode = 0x9f, SecondHeldFaceCount = 87,
            SecondHeldTownItem = new TownItemHeldSource(1, 0x04030201, 4131, 44, 87),
            HeldCardGripMask = rigid ? (byte)3 : (byte)0
        };
    }
    private static AvatarState Packet(AvatarState state)
    {
        var bytes = new byte[AvatarSerializer.MaxSize]; int length = AvatarSerializer.Write(state, bytes);
        Check(AvatarSerializer.TryRead(bytes, length, out var received), "complete two-card rig decodes");
        Check(AvatarSerializer637.TryRead(bytes, length, out var old) && old.HasHeldCard == state.HasHeldCard
            && (!state.HasHeldCard || (old.HeldCardPose.Position - state.HeldCardPose.Position).sqrMagnitude < .000001f)
            && !old.HasSecondHeldCardState, "unchanged637 reader skips111 and retains primary pose");
        return received;
    }
    public static int Run()
    {
        _checks = 0;
        foreach (bool rigid in new[] { false, true })
        {
            var avatar = new RemoteAvatar(); var legacy = new PresenceState();
            try {
                for (int frame = 0; frame < 540; frame++) {
                    float now = frame / 90f;
                    if (frame % 6 == 0) avatar.Rig(Packet(Pair(now, rigid)));
                    if (frame % 18 == 0) {
                        AvatarState stale = Pair(Mathf.Max(0, now - .2f), rigid);
                        legacy.HasSecondHeldCard = true; legacy.SecondHeldCardPose = stale.SecondHeldCardPose;
                        legacy.HasHeldCardGrip = true; legacy.HeldCardGripMask = rigid ? (byte)3 : (byte)0;
                        legacy.HasHeldCardFace = true; legacy.SecondHeldFaceCode = 0x9f; legacy.SecondHeldFaceCount = 87;
                        legacy.HeldTownItem = stale.SecondHeldTownItem;
                        avatar.Legacy(legacy);
                    }
                    avatar.Frame(1f / 90f);
                    Check(avatar.Primary != null && avatar.Secondary != null, "both rig cards activate without awaiting fragmented extras");
                    Check((avatar.Secondary!.position - avatar.Primary!.position - Vector3.right).magnitude < .00001f,
                        "same rig delivery keeps both cards equally smooth despite delayed extras");
                    Check(avatar.Source(Pair(now, rigid).SecondHeldTownItem!.Value), "secondary source stays atomic with its moving pose");
                    Check(Mathf.Abs(avatar.Primary.localScale.x - avatar.Secondary.localScale.x) < .00001f,
                        "both held cards retain identical owner width inspect and world sizing");
                    if (rigid) Check(Quaternion.Angle(avatar.Primary.rotation, avatar.Secondary.rotation) < .05f,
                        "delayed extras cannot change either atomic grip rule");
                }
                legacy.HeldTownItem = new TownItemHeldSource(2, 0, 2, 0, 0);
                legacy.HasHeldCardGrip = true; legacy.HeldCardGripMask = rigid ? (byte)0 : (byte)3;
                avatar.Legacy(legacy);
                Check(avatar.Source(Pair(6f, rigid).SecondHeldTownItem!.Value), "delayed extras cannot rewind the secondary item address");
                var one = Pair(6f, rigid); one.HasSecondHeldCard = false; avatar.Rig(Packet(one)); avatar.Legacy(legacy); avatar.Frame(1f / 90f);
                Check(avatar.Primary!.gameObject.activeSelf && !avatar.Secondary!.gameObject.activeSelf,
                    "one-card rig release cannot be revived by delayed secondary extras");
                one.HasHeldCard = false; avatar.Rig(Packet(one)); avatar.Legacy(legacy); avatar.Frame(1f / 90f);
                Check(!avatar.Primary!.gameObject.activeSelf && !avatar.Secondary!.gameObject.activeSelf,
                    "complete release retires both cards without stale extras resurrection");
                avatar.Rig(Packet(Pair(7f, rigid))); avatar.Frame(1f / 90f);
                Check(avatar.Primary!.gameObject.activeSelf && avatar.Secondary!.gameObject.activeSelf,
                    "two-card regrab activates both immediately on the same rig sample");
            } finally { avatar.Cleanup(); }
        }
        var old = new RemoteAvatar(); try {
            var state = Pair(1, false); state.HasSecondHeldCardState = false; old.Rig(Packet(state));
            var legacy = new PresenceState { HasSecondHeldCard = true, SecondHeldCardPose = state.SecondHeldCardPose };
            old.Legacy(legacy); old.Frame(1f / 90f);
            Check(old.Secondary != null && old.Secondary.gameObject.activeSelf, "legacy rig still accepts record10 second held cards");
            legacy.HasSecondHeldCard = false; old.Legacy(legacy); old.Frame(1f / 90f);
            Check(!old.Secondary!.gameObject.activeSelf, "legacy extras absence still hides its second card");
        } finally { old.Cleanup(); }
        return _checks;
    }
}
