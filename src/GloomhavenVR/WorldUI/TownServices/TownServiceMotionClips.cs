using System;
using System.IO;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Offline Kimodo pose-conditioned motion, sampled from the shared occupation clock.
/// The game never runs inference or accesses a service. Only measured mesh contact is solved
/// after this body sample; there is no runtime random oscillator or render-rate integration.</summary>
internal struct TownMotionBody
{
    internal Quaternion Hips, Spine, Chest, Neck, LeftShoulder, RightShoulder;
    internal Quaternion LeftThigh, LeftShin, LeftFoot, RightThigh, RightShin, RightFoot;
    internal Vector3 Offset;
    internal float Weight;
    internal Quaternion Get(int index) => index switch {
        0 => Hips, 1 => Spine, 2 => Chest, 3 => Neck, 4 => LeftShoulder, 5 => RightShoulder,
        6 => LeftThigh, 7 => LeftShin, 8 => LeftFoot, 9 => RightThigh, 10 => RightShin, _ => RightFoot };
    internal void Set(int index, Quaternion value)
    {
        switch (index) {
            case 0: Hips=value; break; case 1: Spine=value; break; case 2: Chest=value; break;
            case 3: Neck=value; break; case 4: LeftShoulder=value; break; case 5: RightShoulder=value; break;
            case 6: LeftThigh=value; break; case 7: LeftShin=value; break; case 8: LeftFoot=value; break;
            case 9: RightThigh=value; break; case 10: RightShin=value; break; default: RightFoot=value; break;
        }
    }
    internal static TownMotionBody Lerp(in TownMotionBody a, in TownMotionBody b, float t)
    {
        var result = new TownMotionBody { Offset=Vector3.Lerp(a.Offset,b.Offset,t), Weight=Mathf.Lerp(a.Weight,b.Weight,t) };
        for (int i=0;i<12;i++) result.Set(i,Quaternion.Slerp(a.Weight>0f?a.Get(i):Quaternion.identity,b.Weight>0f?b.Get(i):Quaternion.identity,t));
        return result;
    }
}
internal static partial class TownServiceMotionClips
{
    internal static readonly string[] BodyBones = { "Hips", "Spine", "Chest", "Neck", "Clavicle.L", "Clavicle.R",
        "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R" };
    private const int Stride = 63; // Four contact/pole XYZs, root XYZ, twelve world-delta quaternions.
    private const float Rate = 30f;
    private static readonly float[][] Clips = { Read(CountData), Read(ReturnData), Read(SpellData), Read(PrayerData) };
    private static float[] Read(string encoded)
    {
        byte[] bytes=Convert.FromBase64String(encoded);
        var data=new float[bytes.Length/sizeof(float)];
        using var reader=new BinaryReader(new MemoryStream(bytes,false));
        for (int i=0;i<data.Length;i++) data[i]=reader.ReadSingle();
        if (data.Length<Stride*2 || data.Length%Stride!=0) throw new InvalidDataException("Invalid authored town motion length");
        return data;
    }
    internal static TownActivityVisual Sample(int clip, float normalized)
    {
        float[] data=Clips[clip]; int count=data.Length/Stride;
        float at=Mathf.Clamp01(normalized)*(count-1); int a=(int)at, b=Mathf.Min(a+1,count-1);
        float t=at-a; a*=Stride; b*=Stride;
        var body=new TownMotionBody { Offset=Vector(data,a+12,b+12,t), Weight=1f };
        for (int i=0;i<12;i++) body.Set(i,Rotation(data,a+15+i*4,b+15+i*4,t));
        return new TownActivityVisual { Left=Vector(data,a,b,t),Right=Vector(data,a+3,b+3,t),
            LeftElbow=Vector(data,a+6,b+6,t),RightElbow=Vector(data,a+9,b+9,t),Body=body };
    }
    internal static int Frames(int clip) => Clips[clip].Length/Stride;
    internal static float Duration(int clip) => Frames(clip)/Rate;
    private static Vector3 Vector(float[] v,int a,int b,float t) => Vector3.Lerp(
        new Vector3(v[a],v[a+1],v[a+2]),new Vector3(v[b],v[b+1],v[b+2]),t);
    private static Quaternion Rotation(float[] v,int a,int b,float t) => Quaternion.Slerp(
        new Quaternion(v[a],v[a+1],v[a+2],v[a+3]),new Quaternion(v[b],v[b+1],v[b+2],v[b+3]),t);
}
