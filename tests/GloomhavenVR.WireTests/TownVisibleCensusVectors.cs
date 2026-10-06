using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

/// <summary>Independent TLV108 bytes, including legacy absence and malformed streams.</summary>
internal static class TownVisibleCensusVectors
{
    // Schema2 body104: private mage/session1/sequence1, manifest, IDs1/2/4,
    // identity pose, no external canvas, zero native property values/nodes.
    private static readonly byte[] Legacy = Hex.Bytes(
        "31 52 56 47 03 13 4E 68 02 03 01 00 00 00 01 00" +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 FF FF" +
        "00 00 00 00 00 00 00 00 01 FF FF 00 00 00 00 00" +
        "00 80 3F 00 00 00 00 00 00 00 00 03 00 01 00 02" +
        "00 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00" +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00" +
        "00 80 3F 00 00 80 3F 00 00 80 3F 00 00 00 00 00");
    private static TownServiceFrame Manifest() => new()
    {
        Service=3, Session=1, Sequence=1, Module=TownServiceFrame.ManifestModule,
        Visible=true, Modules=new ushort[]{1,2,4},
        Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f}
    };
    internal static void Run(Harness t)
    {
        t.Case("TLV108 independent complete manifest and absent/empty/current visible census");
        TownServiceFrame frame=Manifest(); byte[] packet=TownServiceCodec.Write(frame);
        t.Wire(Legacy,packet,packet.Length,"frozen schema2 legacy manifest remains byte-exact");
        t.True(TownServiceCodec.TryRead(Legacy,Legacy.Length,out var read)&&read!.RequiredVisibleModules==null,
            "independent legacy packet has historical absence, not an empty visible picture");
        byte[] current=Hex.Bytes("6C 07 01 02 00 01 00 04 00");
        frame.RequiredVisibleModules=new ushort[]{1,4}; packet=TownServiceCodec.Write(frame);
        byte[] golden=Legacy.Concat(current).ToArray();
        t.Wire(golden,packet,packet.Length,"frozen108 version1/count/ushort subset follows the unchanged original packet");
        t.True(TownServiceCodec.TryRead(golden,golden.Length,out read)&&read!.RequiredVisibleModules!.SequenceEqual(new ushort[]{1,4}),
            "receiver reads independently authored current visible dependencies");
        frame.RequiredVisibleModules=Array.Empty<ushort>();packet=TownServiceCodec.Write(frame);
        byte[] empty=Legacy.Concat(Hex.Bytes("6C 03 01 00 00")).ToArray();
        t.Wire(empty,packet,packet.Length,"explicit empty108 remains present on the wire");
        t.True(TownServiceCodec.TryRead(empty,empty.Length,out read)&&read!.RequiredVisibleModules!=null&&read.RequiredVisibleModules.Length==0,
            "receiver distinguishes explicit empty from absence");
        byte[] unknown=golden.Concat(Hex.Bytes("FA 03 0A 0B 0C")).ToArray();
        t.True(TownServiceCodec.TryRead(unknown,unknown.Length,out read)&&read!.RequiredVisibleModules!.Length==2,
            "unknown additive record cannot alter the original census");
        byte[] split=Legacy.Concat(Hex.Bytes("6C 03 01 02 00 6C 04 01 00 04 00")).ToArray();
        t.True(TownServiceCodec.TryRead(split,split.Length,out read)&&read!.RequiredVisibleModules!.SequenceEqual(new ushort[]{1,4}),
            "independent fragmented108 stream retains exact ushort order");
        foreach(string suffix in new[]{
            "6C 00", "6C 03 02 00 00", "6C 03 01 01 00", "6C 03 01 01 10",
            "6C 05 01 01 00 03 00", "6C 07 01 02 00 01 00 01 00", "6C 07 01 02 00 04 00 01 00",
            "6C 07 01 02 00 01 00 04 00 6C 07 01 02 00 01 00 04 00"})
        {
            byte[] invalid=Legacy.Concat(Hex.Bytes(suffix)).ToArray();
            t.True(!TownServiceCodec.TryRead(invalid,invalid.Length,out _),"reject independent malformed108: "+suffix);
        }
        foreach(Action<TownServiceFrame> mutate in new Action<TownServiceFrame>[] {
            f=>f.Service=1, f=>{f.Service=1;f.PublicCatalog=true;}, f=>{f.Service=1;f.VisitorStock=true;}, f=>{f.Visible=false;f.Modules=Array.Empty<ushort>();} })
        {
            frame=Manifest();mutate(frame);
            byte[] invalid=TownServiceCodec.Write(frame).Concat(current).ToArray();
            t.True(!TownServiceCodec.TryRead(invalid,invalid.Length,out _),"visible dependencies cannot escape their live private mage manifest");
        }
        var native=new TownServiceFrame { Service=3,Session=1,Sequence=1,Module=1,Template=1,TemplateAddress="face.1|",
            Structure=7,Visible=true,Nodes=new[]{new TownServiceNode{Binding=1}},Pose=Manifest().Pose };
        byte[] original=TownServiceCodec.Write(native);
        native.NativeTemplateBasisKey=0x0102030405060708UL;
        packet=TownServiceCodec.Write(native);
        byte[] basis=Hex.Bytes("69 09 01 08 07 06 05 04 03 02 01");
        t.Wire(original.Concat(basis).ToArray(),packet,packet.Length,"independent105 tail is not overwritten by optional108 handling");
        byte[] combined=packet.Concat(current).ToArray();
        t.True(!TownServiceCodec.TryRead(combined,combined.Length,out _),
            "105 original modules and108 manifest census are distinct identities, never an overlapping tail");
        byte[] nativeUnknown=packet.Concat(Hex.Bytes("FA 01 7F")).ToArray();
        t.True(TownServiceCodec.TryRead(nativeUnknown,nativeUnknown.Length,out read)&&read!.NativeTemplateBasisKey==0x0102030405060708UL,
            "105 exact64-bit key survives a following unknown additive record");
        frame=Manifest();frame.RequiredVisibleModules=new ushort[]{4,1};
        bool rejected=false;try{TownServiceCodec.Write(frame);}catch(InvalidDataException){rejected=true;}
        t.True(rejected,"writer refuses unordered actual dependency IDs");
        frame=Manifest();frame.RequiredVisibleModules=new ushort[]{3};
        rejected=false;try{TownServiceCodec.Write(frame);}catch(InvalidDataException){rejected=true;}
        t.True(rejected,"writer refuses an absent actual module");
        frame=Manifest();frame.RequiredVisibleModules=new ushort[]{1,4};
        TownServiceFrame retained=TownServiceDelta.Retain(frame);frame.RequiredVisibleModules[0]=2;
        t.True(retained.RequiredVisibleModules![0]==1,"queued immutable108 retains the original snapshot's own census");
    }
}
