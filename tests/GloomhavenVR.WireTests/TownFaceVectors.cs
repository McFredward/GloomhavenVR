using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class TownFaceVectors
{
    private static readonly byte[] Golden = Hex.Bytes("50 5E 01 44 33 22 11 40 30 20 10 00 00 48 41 64 00 38 FF 2C 01 70 FE F4 01 A8 FD BC 02 34 12 78 56 34 12 00 00 C0 3F 11 22 33 64 00 38 FF 2C 01 70 FE F4 01 A8 FD BC 02 34 12 78 56 34 12 00 00 C0 3F 11 22 33 64 00 38 FF 2C 01 70 FE F4 01 A8 FD BC 02 34 12 78 56 34 12 00 00 C0 3F 11 22 33");
    private static TownFaceState Example()
    {
        var p = new TownFacePose { HeadPitch=1,HeadYaw=-2,HeadRoll=3,LeftPitch=-4,LeftYaw=5,RightPitch=-6,RightYaw=7,
            Cue=0x1234,Generation=0x12345678,SpeechAge=1.5f,Jaw=17,Wide=34,Round=51 };
        return new TownFaceState { Active=true,Epoch=0x11223344,Sequence=0x10203040,Clock=12.5f,Merchant=p,Temple=p,Enchantress=p };
    }
    public static void Run(Harness t)
    {
        t.Case("town face80: independent exact bytes and old-record compatibility");
        t.True(!TownFaceCodec.ReadPacket(null!,98,out _)&&!TownFaceCodec.TryRead(null!,0,94,out _),"null packet fails closed");
        var state=Example(); var bytes=new byte[PresenceSerializer.MaxSize]; int offset=0;
        t.True(TownFaceCodec.Write(bytes,ref offset,in state)&&offset==96,"face record exact96byte size");
        for(int n=0;n<Golden.Length;n++)t.Equal(Golden[n],bytes[n],"face golden byte"+n);
        t.True(TownFaceCodec.TryRead(Golden,2,94,out var read)&&read.Active&&read.Sequence==0x10203040,"fixed golden decodes");
        t.True(read.Merchant.Cue==0x1234&&read.Temple.Generation==0x12345678&&read.Enchantress.SpeechAge==1.5f,"voice cue and generation are exact");
        int count=TownFaceCodec.WritePacket(bytes,in state);
        t.True(count==102&&bytes[5]==21&&TownFaceCodec.ReadPacket(bytes,count,out read)&&read.Sequence==state.Sequence,"fast stream uses same record80");
        for(int cut=0;cut<count;cut++) t.True(!TownFaceCodec.ReadPacket(bytes,cut,out read),"packet truncation"+cut);
        var presence=new PresenceState{HasTownFace=true,TownFace=state};
        count=PresenceSerializer.Write(in presence,bytes);
        t.True(PresenceSerializer.TryRead(bytes,count,out var parsed)&&parsed.HasTownFace&&parsed.TownFace.Active,"presence recovery retainsface");
        presence.HasTownFace=false;count=PresenceSerializer.Write(in presence,bytes);
        t.True(PresenceSerializer.TryRead(bytes,count,out parsed)&&!parsed.HasTownFace,"absence is old-client compatible");
        for(int size=0;size<96;size++)
        {
            var limited=new byte[size];Array.Fill(limited,(byte)0xCD);offset=0;
            t.True(!TownFaceCodec.Write(limited,ref offset,in state)&&offset==0,"short writer atomic"+size);
            bool same=true;foreach(byte value in limited)same&=value==0xCD;t.True(same,"no partial write"+size);
        }
        for(int size=0;size<96;size++)
        {var limited=new byte[size];Array.Copy(Golden,limited,size);t.True(!TownFaceCodec.TryRead(limited,2,94,out read)&&!read.Active,"short reader atomic"+size);}
        foreach(int at in new[]{-1,93,int.MaxValue}) t.True(!TownFaceCodec.TryRead(Golden,at,90,out read),"invalid offset"+at);
        for(int length=0;length<256;length++)if(length!=94)t.True(!TownFaceCodec.TryRead(Golden,2,length,out read),"wrong recordlength"+length);
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,10000001f})
        {var invalid=state;invalid.Clock=bad;offset=0;t.True(!TownFaceCodec.Write(bytes,ref offset,in invalid)&&offset==0,"invalid faceclock"+bad);}
        for(int which=0;which<3;which++)for(int field=0;field<9;field++)
        {
            var invalid=state;var p=state.At(which);
            if(field==0)p.HeadPitch=26;else if(field==1)p.HeadYaw=56;else if(field==2)p.HeadRoll=9;
            else if(field==3)p.LeftPitch=21;else if(field==4)p.LeftYaw=29;else if(field==5)p.RightPitch=21;
            else if(field==6)p.RightYaw=29;else if(field==7)p.SpeechAge=float.NaN;else p.Cue=0;
            invalid.Set(which,p);offset=0;t.True(!TownFaceCodec.Write(bytes,ref offset,in invalid)&&offset==0,"invalid pose"+which+"/"+field);
        }
        var epochless=Example();epochless.Epoch=0;offset=0;t.True(!TownFaceCodec.Write(bytes,ref offset,in epochless),"active stream requires nonzero epoch");
        for(int flag=2;flag<256;flag++)
        {var bad=(byte[])Golden.Clone();bad[2]=(byte)flag;t.True(!TownFaceCodec.TryRead(bad,2,94,out read),"reserved flag"+flag);}
        for(int which=0;which<3;which++)
        {var bad=(byte[])Golden.Clone();int at=15+which*27+20;bad[at]=0;bad[at+1]=0;bad[at+2]=0xC0;bad[at+3]=0x7F;t.True(!TownFaceCodec.TryRead(bad,2,94,out read),"malformed voice age rejects atomically"+which);}
        for(int which=0;which<3;which++)for(int field=0;field<7;field++)
        {var bad=(byte[])Golden.Clone();int at=15+which*27+field*2;bad[at]=0xFF;bad[at+1]=0x7F;t.True(!TownFaceCodec.TryRead(bad,2,94,out read),"malformed boundedangle"+which+"/"+field);}
        state.Active=false;offset=0;t.True(TownFaceCodec.Write(bytes,ref offset,in state)&&offset==3&&bytes[0]==80&&bytes[1]==1&&bytes[2]==0,"inactive face is exact3bytes");
        state=Example();offset=6986;t.True(TownFaceCodec.Write(bytes,ref offset,in state)&&offset==7082,"worst snapshot adds96bytes");
        t.True(offset<=ExtrasFragments.MaxSnapshotBytes&&PresenceSerializer.MaxSize-offset>=257&&ExtrasFragments.MaxSnapshotBytes==7680,"town face remains inside the combined reassembly ceiling");
        t.True(NetProtocol.Version==3&&NetProtocol.ExtIdTownResidents==79&&TownResidentsCodec.LegacyPayload==115&&TownResidentsCodec.MaxPayload==139&&NetProtocol.ExtIdTownFace==80,"resident prefix unchanged, cloth tail bounded, face additive80");
    }
}
