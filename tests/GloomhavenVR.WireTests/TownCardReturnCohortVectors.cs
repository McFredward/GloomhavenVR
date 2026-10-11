using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownCardReturnCohortVectors
{
    private static TownServiceMotionEntry Cohort(int count=1)
    {
        var entry = new TownServiceMotionEntry { Kind=10,Lane=2,Service=1,Session=1,Module=1,Structure=1,
            Revision=1,ReturnSampleTime=10f,Numbers=new[]{.1f,.5f,0f,0f,0f,1f,2f,0f,0f,0f,1f,1f,1f,1f,
                3f,4f,5f,0f,0f,0f,1f,1f,1f,1f,0f,0f,0f,0f},ReturnMembers=new ushort[count],ReturnStructures=new uint[count],
            ReturnParts=new TownServiceReturnPart[count] };
        for(int i=0;i<count;i++) { entry.ReturnMembers[i]=(ushort)(i+1);entry.ReturnStructures[i]=(uint)(i+1);
            entry.ReturnParts[i]=new TownServiceReturnPart {Index=(byte)i,Visible=true,ParentAlpha=1f,
                Child=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f}}; }
        return entry;
    }
    private static TownServiceMotionPacket Packet(TownServiceMotionEntry entry)
    { var packet=new TownServiceMotionPacket{Sequence=9,SampleTime=10f};packet.Entries.Add(entry);return packet; }
    internal static void Run(Harness t)
    {
        t.Case("Additive113 compact native physical return cohorts retain exact original clocks and child geometry");
        byte[] golden=Hex.Bytes("31 52 56 47 03 1A 61 0D 00 09 00 00 00 00 00 00 00 00 00 20 41 71 C4 C0 00 00 00 0A 02 01 01 00 00 00 00 00 00 00 01 00 01 00 00 00 00 01 00 00 00 00 00 20 41 CD CC CC 3D 00 00 00 3F 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 00 40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 40 40 00 00 80 40 00 00 A0 40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 01 00 01 00 00 00 01 00 01 00 00 80 3F 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F");
        byte[] written=TownServiceMotionCodec.Write(Packet(Cohort()));
        t.Wire(golden,written,written.Length,"independent Python little-endian struct binds every immutable cohort byte");
        t.True(TownServiceMotionCodec.TryRead(golden,golden.Length,out var read)&&read!.Entries[0].ReturnMembers.Length==1,
            "independently authored cohort decodes exact identity and native part");
        t.Equal(10f,read!.Entries[0].ReturnSampleTime,"native source sample is distinct from later packet transport time");
        for(int count=0;count<golden.Length;count++) t.True(!TownServiceMotionCodec.TryRead(golden,count,out _),
            "truncated cohort fragments never expose part of a physical picture");
        foreach(int offset in new[]{23,24,25,27,28,29,44,169,170,176,177})
        {
            byte[] bad=(byte[])golden.Clone();bad[offset]=255;
            t.True(!TownServiceMotionCodec.TryRead(bad,bad.Length,out _),"invalid native cohort boundaries fail atomically offset="+offset);
        }
        var full=Cohort(13);byte[] thirteen=TownServiceMotionCodec.Write(Packet(full));
        t.Equal(861,thirteen.Length,"thirteen complete native partitions fit raw in the unchanged864-byte event");
        t.Equal(840,TownServiceMotionCodec.EntryBytes(full),"all four byte-length113 wrappers count toward the existing numeric budget");
        t.True(TownServiceMotionCodec.TryRead(thirteen,thirteen.Length,out read)&&read!.Entries[0].ReturnParts.Length==13,
            "legitimate contiguous fragments reassemble inside the same atomic packet");
        foreach(int offset in new[]{21+257+2,21+257+4,21+257+5})
        { byte[] bad=(byte[])thirteen.Clone();bad[offset]^=1;
          t.True(!TownServiceMotionCodec.TryRead(bad,bad.Length,out _),"conflicting contiguous fragment total/offset rejected"); }
        byte[] duplicated=golden.Concat(golden.Skip(21)).ToArray();
        t.True(!TownServiceMotionCodec.TryRead(duplicated,duplicated.Length,out _),"duplicate logical native cohort identity is rejected");
        var unknown=Packet(Cohort());unknown.Entries.Add(new TownServiceMotionEntry{Kind=6,Service=1,Session=1,CueReady=true});
        byte[] skipped=TownServiceMotionCodec.Write(unknown);skipped[21]=252;
        t.True(TownServiceMotionCodec.TryRead(skipped,skipped.Length,out read)&&read!.Entries.Count==1&&read.Entries[0].Kind==6,
            "unchanged byte-TLV envelope skips unknown additive data and retains known following records");
        foreach(string fault in new[]{"future","members","structures","indices","alpha","quat","scale","reserved","revision"})
        {
            var invalid=Cohort(2);
            switch(fault) {
                case "future": invalid.ReturnSampleTime=11f;break;
                case "members": invalid.ReturnMembers[1]=1;break;
                case "structures": invalid.ReturnStructures[1]=0;break;
                case "indices": invalid.ReturnParts[1].Index=0;break;
                case "alpha": invalid.ReturnParts[0].ParentAlpha=float.NaN;break;
                case "quat": invalid.ReturnParts[0].Child[6]=0;break;
                case "scale": invalid.ReturnParts[0].Child[7]=0;break;
                case "reserved": invalid.Numbers[27]=1;break;
                case "revision": invalid.Revision=0;break;
            }
            bool rejected=false;try{TownServiceMotionCodec.Write(Packet(invalid));}catch(InvalidDataException){rejected=true;}
            t.True(rejected,"writer rejects malformed exact native cohort "+fault);
        }
        t.Case("Large native return cohort makes bounded progress on one unchanged source instant");
        foreach(int count in new[]{17,40,64})
        {
            var fullEntry=Cohort(count);var live=new List<TownServiceMotionPending>();
            for(int i=0;i<count;i++)
            {
                var root=new TownServiceMotionEntry{Kind=1,Lane=2,Service=1,Session=1,Module=(ushort)(i+1),Structure=(uint)(i+1),Visible=true,ParentAlpha=1,
                    Pose=new[]{0f,0f,0f,0f,0f,0f,1f,1f,1f,1f}};
                float[] values=new float[38];Array.Copy(fullEntry.Numbers,values,28);Array.Copy(fullEntry.ReturnParts[i].Child,0,values,28,10);
                live.Add(new TownServiceMotionPending{Entry=root});live.Add(new TownServiceMotionPending{Entry=new TownServiceMotionEntry{
                    Kind=8,Lane=2,Service=1,Session=1,Module=root.Module,Structure=root.Structure,Revision=1,Numbers=values}});
            }
            var members=new HashSet<byte>();int a=0,b=0,c=0,tick=0;
            while(members.Count<count&&tick<12)
            {
                var packet=new TownServiceMotionPacket{Sequence=(ulong)(tick+1),SampleTime=10f+tick/15f};
                byte[] bytes=TownServiceMotionBudget.FillPacked(packet,live,new(),new(),ref a,ref b,ref c,packet.SampleTime);
                t.True(bytes.Length>0&&bytes.Length<=864&&TownServiceMotionCodec.TryRead(bytes,bytes.Length,out _),"oversized native group has finite bounded numeric turns");
                foreach(var entry in packet.Entries)if(entry.Kind==10){t.Equal(10f,entry.ReturnSampleTime,"staged cohort source clock never restarts");foreach(var part in entry.ReturnParts)members.Add(part.Index);}
                tick++;
            }
            t.Equal(count,members.Count,"every native original child is admitted without trimming");
        }
    }
}
