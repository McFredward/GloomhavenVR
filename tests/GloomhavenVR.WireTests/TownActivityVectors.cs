using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;
internal static class TownActivityVectors
{
    private static readonly byte[] Golden = Hex.Bytes("51 34 01 44 33 22 11 40 30 20 10 00 00 48 41 00 00 20 41 00 00 00 3E 00 00 80 3E 01 00 00 20 41 00 00 00 3E 00 00 80 3E 01 00 00 20 41 00 00 00 3E 00 00 80 3E 01");
    private static readonly byte[] PacketGolden = Hex.Bytes("31 52 56 47 03 16 50 5E 01 44 33 22 11 40 30 20 10 00 00 48 41 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 51 34 01 44 33 22 11 40 30 20 10 00 00 48 41 00 00 20 41 00 00 00 3E 00 00 80 3E 01 00 00 20 41 00 00 00 3E 00 00 80 3E 01 00 00 20 41 00 00 00 3E 00 00 80 3E 01");
    public static void Run(Harness t)
    {
        var p = new TownActivityPose { WorkClock=10f,TransitionAge=.125f,FromBlend=.25f,Engaged=true };
        var state = new TownActivityState { Active=true,Epoch=0x11223344,Sequence=0x10203040,Clock=12.5f,Merchant=p,Temple=p,Enchantress=p };
        var bytes = new byte[PresenceSerializer.MaxSize]; int offset=0;
        t.Case("town occupation81: literal bytes and bounded map-only stream");
        t.True(TownActivityCodec.Write(bytes,ref offset,in state)&&offset==54,"exact54byte additive record");
        for(int i=0;i<Golden.Length;i++)t.Equal(Golden[i],bytes[i],"literal byte"+i);
        t.True(TownActivityCodec.TryRead(Golden,2,52,out var read)&&read.Merchant.Engaged&&read.Temple.WorkClock==10,"literal vector reads");
        var face=new TownFaceState{Active=true,Epoch=state.Epoch,Sequence=state.Sequence,Clock=state.Clock};
        int count=TownActivityCodec.WritePacket(bytes,in state,in face);
        t.True(count==156&&bytes[5]==22&&TownActivityCodec.ReadPacket(bytes,count,out read,out _),"atomic156byte message22");
        for(int i=0;i<PacketGolden.Length;i++)t.Equal(PacketGolden[i],bytes[i],"atomic packet literal"+i);
        var broken=(byte[])PacketGolden.Clone();broken[155]=2;
        t.True(!TownActivityCodec.ReadPacket(broken,broken.Length,out var noActivity,out var noFace)&&!noFace.Active&&!noActivity.Active,"malformed second TLV cannot partially accept face");
        broken=(byte[])PacketGolden.Clone();broken[113]=1;
        t.True(!TownActivityCodec.ReadPacket(broken,broken.Length,out _,out _),"mismatched sampled clocks rejected atomically");
        broken=(byte[])PacketGolden.Clone();broken[102]=80;
        t.True(!TownActivityCodec.ReadPacket(broken,broken.Length,out _,out _),"duplicate face cannot replace occupation sibling");
        for(int cut=0;cut<count;cut++)t.True(!TownActivityCodec.ReadPacket(bytes,cut,out _,out _),"packet truncation"+cut);
        for(int size=0;size<54;size++)
        {var shortBuffer=new byte[size];Array.Fill(shortBuffer,(byte)0xCD);offset=0;t.True(!TownActivityCodec.Write(shortBuffer,ref offset,in state)&&offset==0,"short write atomic"+size);foreach(byte b in shortBuffer)t.True(b==0xCD,"short write unchanged");}
        for(int length=0;length<256;length++)if(length!=52)t.True(!TownActivityCodec.TryRead(Golden,2,length,out _),"record length"+length);
        for(int npc=0;npc<3;npc++)
        {
            var bad=(byte[])Golden.Clone();bad[15+npc*13+12]=2;t.True(!TownActivityCodec.TryRead(bad,2,52,out _),"reserved targetflag"+npc);
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,10000001f})
                for(int field=0;field<3;field++)
                {var q=p;if(field==0)q.WorkClock=invalid;else if(field==1)q.TransitionAge=invalid;else q.FromBlend=invalid;var wrong=state;wrong.Set(npc,q);offset=0;t.True(!TownActivityCodec.Write(bytes,ref offset,in wrong)&&offset==0,"invalid analytic phase");}
        }
        var presence=new PresenceState {HasTownActivity=true,TownActivity=state};
        count=PresenceSerializer.Write(in presence,bytes);t.True(PresenceSerializer.TryRead(bytes,count,out var parsed)&&parsed.HasTownActivity&&parsed.TownActivityRecordSeen&&parsed.TownActivity.Merchant.Engaged,"presence recovery");
        bytes[count-1]=2;
        t.True(PresenceSerializer.TryRead(bytes,count,out parsed)&&!parsed.HasTownActivity&&parsed.TownActivityRecordSeen,"malformed81 remains distinguishable from absent legacy extension");
        var tail=new byte[400];Hex.Bytes("31 52 56 47 03 01 80 00 80 00 03").CopyTo(tail,0);
        int tailAt=11;var residents=default(TownResidentsState);
        TownResidentsCodec.Write(tail,ref tailAt,in residents);TownFaceCodec.Write(tail,ref tailAt,in face);
        int activityAt=tailAt;Golden.CopyTo(tail,tailAt);
        for(int remaining=1;remaining<54;remaining++)
            t.True(PresenceSerializer.TryRead(tail,activityAt+remaining,out parsed)&&parsed.HasTownResidents&&parsed.HasTownFace
                &&parsed.TownActivityRecordSeen&&!parsed.HasTownActivity,"reordered79+80+truncated81 preserves atomic marker"+remaining);
        presence.HasTownActivity=false;count=PresenceSerializer.Write(in presence,bytes);t.True(PresenceSerializer.TryRead(bytes,count,out parsed)&&!parsed.HasTownActivity,"absence retains oldwire");
        offset=7082;t.True(TownActivityCodec.Write(bytes,ref offset,in state)&&offset==7136,"worst snapshot exact7136");
        t.True(ExtrasFragments.MaxSnapshotBytes-(offset+3+510)==31&&PresenceSerializer.MaxSize-(offset+3+510)==257,"combined opening histories and public loadout88 retain fragment and allocation margins");
        t.True(NetProtocol.Version==3&&NetProtocol.ExtIdTownResidents==79&&TownResidentsCodec.MaxPayload==115&&TownFaceCodec.MaxPayload==94,"79and80unchanged");
    }
}
