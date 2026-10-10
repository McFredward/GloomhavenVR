using System;
using System.Linq;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownReturnOrigin665Vectors
{
    internal static void Run(Harness t)
    {
        TownServiceFrame frame = Frame(); byte[] legacy=TownServiceCodec.Write(frame);
        frame.ReturnOrigin=new TownCardReturnOrigin(3,frame.Session,frame.Module,frame.Structure,0,0);
        byte[] packet=TownServiceCodec.Write(frame);
        byte[] suffix={116,20,1,3,0x44,0x33,0x22,0x11,7,0,0x88,0x77,0x66,0x55,0,0,0,0,0,0,0,0};
        t.Wire(suffix,packet.Skip(legacy.Length).ToArray(),22,"independent little-endian116 origin bytes");
        t.Wire(legacy,packet.Take(legacy.Length).ToArray(),legacy.Length,"optional116 leaves complete legacy module bytes unchanged");
        t.True(TownServiceCodec.TryRead(packet,packet.Length,out var decoded)&&decoded!.ReturnOrigin!.Equals(frame.ReturnOrigin),"origin retains exact private identity");
        t.Equal(1u,frame.ReturnOrigin.FlightRevision,"preparation zero permits only native flight one");
        t.Equal(1u,new TownCardReturnOrigin(3,1,1,1,0,uint.MaxValue).FlightRevision,"native nonzero uint wrap remains exact");
        t.Equal(2u,new TownCardReturnOrigin(3,1,1,1,0,1).FlightRevision,"reoffer preparation one permits only native flight two");
        var retained=TownServiceDelta.Retain(frame); var copied=TownServiceDelta.Copy(frame);
        t.True(ReferenceEquals(retained.ReturnOrigin,frame.ReturnOrigin)&&ReferenceEquals(copied.ReturnOrigin,frame.ReturnOrigin),"immutable origin survives retained clone/delta headers");
        frame.Sequence++;TownServiceFrame delta=TownServiceDelta.Create(retained,frame)!;
        t.True(TownServiceDelta.Expand(retained,delta)!.ReturnOrigin!.Equals(frame.ReturnOrigin),"cumulative delta expansion retains origin");
        for(int length=legacy.Length+1;length<packet.Length;length++)
            t.True(!TownServiceCodec.TryRead(packet,length,out _),"truncated116 cannot partially authorize a transition");
        foreach(int offset in new[]{2,3,4,8,10,14}) {
            byte[] bad=(byte[])packet.Clone();bad[legacy.Length+offset]=255;
            t.True(!TownServiceCodec.TryRead(bad,bad.Length,out _),"invalid origin identity boundary rejected offset="+offset);
        }
        byte[] duplicated=packet.Concat(suffix).ToArray();
        t.True(!TownServiceCodec.TryRead(duplicated,duplicated.Length,out _),"duplicate116 rejected atomically");
        frame.VisitorStock=true;frame.Service=1;frame.Session=99;frame.Module=21;
        packet=TownServiceCodec.Write(frame);
        t.True(TownServiceCodec.TryRead(packet,packet.Length,out decoded)&&decoded!.ReturnOrigin!.Module==7&&decoded.Module==21,
            "independent stock destination retains exact private source module");
        byte[][]? bundled=null;
        var builder=new TownServiceCodec.OriginalValuePoolBuilder();
        t.True(builder.TryAdd(TownServiceCodec.Write(frame))&&builder.TryAdd(TownServiceCodec.Write(Other(frame))),"pooled origin originals are admitted");
        byte[] pool=builder.Write();
        t.True(TownServiceCodec.TryReadBundle(pool,pool.Length,out bundled),"pooled native originals retain immutable origin headers");
        foreach(byte[] member in bundled!) t.True(TownServiceCodec.TryRead(member,member.Length,out decoded)&&decoded!.ReturnOrigin!=null,"each pooled original retains its own116");
        foreach(string fault in new[]{"public","service","structure","session","reserved","address"}) {
            var bad=Frame();bad.ReturnOrigin=new TownCardReturnOrigin(3,bad.Session,bad.Module,bad.Structure,0,0);
            switch(fault) {
                case "public":bad.PublicCatalog=true;break;
                case "service":bad.Service=2;break;
                case "structure":bad.Structure++;break;
                case "session":bad.Session++;break;
                case "reserved":bad.Module=TownServiceFrame.ManifestModule;break;
                case "address":bad.TemplateAddress="item.6651|";break;
            }
            bool rejected=false;try{TownServiceCodec.Write(bad);}catch(System.IO.InvalidDataException){rejected=true;}
            t.True(rejected,"malformed source origin cannot cross lane/service/topology/address "+fault);
        }
    }
    private static TownServiceFrame Frame()=>new() {Service=3,Session=0x11223344,Sequence=1,Module=7,Template=1,Structure=0x55667788,
        TemplateAddress="face.6651|",Visible=true,SampleTime=10,Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f},
        Nodes=new[]{new TownServiceNode{Binding=1}}};
    private static TownServiceFrame Other(TownServiceFrame source)
    {var other=TownServiceDelta.Retain(source);other.Module=22;other.ReturnOrigin=new TownCardReturnOrigin(3,0x11223344,8,other.Structure,0,0);return other;}
}
internal static class ReturnOrigin665Program
{private static int Main(){var t=new Harness();TownReturnOrigin665Vectors.Run(t);return t.Report();}}
