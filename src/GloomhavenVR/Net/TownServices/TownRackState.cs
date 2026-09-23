using System;
using System.Collections.Generic;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Owner-authored cosmetic cabinet clock and bounded page dependencies. This is
/// additive TLV85; the existing native module content/poses remain unchanged TLV78 data.</summary>
internal sealed class TownRackState
{
    internal const byte RecordId = 85;
    internal const int MaxMembers = 384;
    internal const float TurnDuration = .85f;
    internal uint Turn;
    internal float Elapsed, LeadAngle;
    internal ushort Crank, Page, From, To;
    internal TownRackMember[] Members = Array.Empty<TownRackMember>();
    internal TownRackState Copy() => new() { Turn=Turn,Elapsed=Elapsed,LeadAngle=LeadAngle,Crank=Crank,
        Page=Page,From=From,To=To,Members=(TownRackMember[])Members.Clone() };
    internal static float Progress(float elapsed)
    { float t=Math.Max(0f,Math.Min(1f,elapsed/TurnDuration));return t*t*(3f-2f*t); }
    internal bool Same(TownRackState? other)
    {
        if(other==null||Turn!=other.Turn||Elapsed!=other.Elapsed||LeadAngle!=other.LeadAngle||Crank!=other.Crank
            ||Page!=other.Page||From!=other.From||To!=other.To||Members.Length!=other.Members.Length)return false;
        for(int i=0;i<Members.Length;i++)if(!Members[i].Same(other.Members[i]))return false;
        return true;
    }
    internal void Validate(ushort rack)
    {
        if(rack>=TownServiceFrame.BundleStream||Crank>=TownServiceFrame.BundleStream||Crank==rack
            ||Members==null||Members.Length>MaxMembers||!Finite(Elapsed)||Elapsed<0f||Elapsed>TurnDuration+.001f
            ||!Finite(LeadAngle)||LeadAngle<0f||LeadAngle>35.01f||Page>4095||From>4095||To>4095)
            throw new InvalidDataException("Invalid cabinet presentation clock");
        ushort before=0;bool first=true;int pageA=-1,pageB=-1,pageC=-1;
        foreach(var member in Members)
        {
            if(member.Id>=TownServiceFrame.BundleStream||member.Id==rack||member.Id==Crank||member.Page>4095
                ||!first&&member.Id<=before)throw new InvalidDataException("Invalid cabinet presentation dependencies");
            if(member.Page!=pageA&&member.Page!=pageB&&member.Page!=pageC)
            {if(pageA<0)pageA=member.Page;else if(pageB<0)pageB=member.Page;else if(pageC<0)pageC=member.Page;else throw new InvalidDataException("Cabinet prewarm exceeds three trays");}
            before=member.Id;first=false;
        }
    }
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    internal byte[] Write(ushort rack)
    {
        Validate(rack);using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        writer.Write((byte)1);writer.Write(Turn);writer.Write(Elapsed);writer.Write(LeadAngle);
        writer.Write(Crank);writer.Write(Page);writer.Write(From);writer.Write(To);writer.Write((ushort)Members.Length);
        foreach(var m in Members){writer.Write(m.Id);writer.Write(m.Page);writer.Write(m.Detached);}
        return bytes.ToArray();
    }
    internal static TownRackState Read(byte[] bytes,ushort rack)
    {
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
        if(reader.ReadByte()!=1)throw new InvalidDataException("Unknown cabinet clock version");
        var value=new TownRackState {Turn=reader.ReadUInt32(),Elapsed=reader.ReadSingle(),LeadAngle=reader.ReadSingle(),
            Crank=reader.ReadUInt16(),Page=reader.ReadUInt16(),From=reader.ReadUInt16(),To=reader.ReadUInt16()};
        int count=reader.ReadUInt16();if(count>MaxMembers)throw new InvalidDataException("Cabinet dependency limit");
        value.Members=new TownRackMember[count];
        for(int i=0;i<count;i++)
        {ushort id=reader.ReadUInt16(),page=reader.ReadUInt16();byte detached=reader.ReadByte();if(detached>1)throw new InvalidDataException("Cabinet detached flag");value.Members[i]=new(id,page,detached!=0);}
        if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing cabinet clock data");value.Validate(rack);return value;
    }
}
internal readonly struct TownRackMember
{
    internal readonly ushort Id,Page;
    internal readonly bool Detached;
    internal TownRackMember(ushort id,ushort page,bool detached){Id=id;Page=page;Detached=detached;}
    internal bool Same(TownRackMember other)=>Id==other.Id&&Page==other.Page&&Detached==other.Detached;
}

/// <summary>Per-module causal stamp. A held card can overtake its rack clock lane;
/// this stamp prevents a delayed revolution from moving that card out of a hand.</summary>
internal sealed class TownRackStamp
{
    internal ushort Rack, Page;
    internal uint Turn;
    internal bool Detached;
    internal float Alpha = 1f;
    internal TownRackStamp Copy()=>new(){Rack=Rack,Page=Page,Turn=Turn,Detached=Detached,Alpha=Alpha};
    internal bool Same(TownRackStamp? other)=>other!=null&&Rack==other.Rack&&Page==other.Page&&Turn==other.Turn&&Detached==other.Detached&&Alpha==other.Alpha;
    internal byte[] Write(ushort module)
    {
        if(Rack>=TownServiceFrame.BundleStream||Rack==module||Page>4095||float.IsNaN(Alpha)||float.IsInfinity(Alpha)||Alpha<0f||Alpha>1f)throw new InvalidDataException("Invalid cabinet member stamp");
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        writer.Write((byte)2);writer.Write(Rack);writer.Write(Page);writer.Write(Turn);writer.Write(Detached);writer.Write(Alpha);return bytes.ToArray();
    }
    internal static TownRackStamp Read(byte[] bytes,ushort module)
    {
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
        if(reader.ReadByte()!=2)throw new InvalidDataException("Unknown cabinet stamp version");
        var result=new TownRackStamp{Rack=reader.ReadUInt16(),Page=reader.ReadUInt16(),Turn=reader.ReadUInt32()};
        byte detached=reader.ReadByte();result.Alpha=reader.ReadSingle();if(detached>1||stream.Position!=stream.Length)throw new InvalidDataException("Invalid cabinet stamp payload");
        result.Detached=detached!=0;result.Write(module);return result;
    }
}
