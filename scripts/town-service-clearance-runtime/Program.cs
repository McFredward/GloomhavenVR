using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;

// Only the rig's yaw helper is a fixture boundary. Layout/room classification uses
// production code with real Unity transforms and the untouched shipped room prefabs.
// This is a read-only layout contract, not a simulation of the complete resident lifecycle.
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver
    { internal static Quaternion YawOnly(Quaternion q) => Quaternion.Euler(0f,q.eulerAngles.y,0f); }
}
public static class InteractionProgram
{
    private static int count;
    private static void Check(bool pass,string message) { count++;if(!pass)throw new Exception(message); }
    private readonly struct Pose
    {
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal Pose(Transform t) { Position=t.localPosition;Scale=t.localScale;Rotation=t.localRotation; }
        internal bool Same(Transform t)=>Position==t.localPosition&&Scale==t.localScale&&Rotation==t.localRotation;
    }
    private static void OriginalRooms()
    {
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-clearanceBundle");
        if(at<0)throw new Exception("Original environment bundle is required");
        var bundle=AssetBundle.LoadFromFile(args[at+1]);
        if(bundle==null)throw new Exception("actual environment bundle loads"); count++;
        int rooms=0;
        try
        {
            foreach(string path in bundle.GetAllAssetNames().Where(p=>p.EndsWith("env_cellar.prefab")||p.EndsWith("env_swamp.prefab")))
            {
                rooms++;
                var room=UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(path));
                try
                {
                    var transforms=room.GetComponentsInChildren<Transform>(true);
                    var poses=transforms.Select(t=>new Pose(t)).ToArray();
                    var filters=room.GetComponentsInChildren<MeshFilter>(true);
                    var meshes=filters.Select(f=>f.sharedMesh).ToArray();
                    var vertices=meshes.Select(m=>m!=null&&m.isReadable?m.vertices:null).ToArray();
                    TownServiceLayout.Environment expected=path.Contains("swamp")?TownServiceLayout.Environment.Forest:TownServiceLayout.Environment.Cellar;
                    Check(TownServiceLayout.ForRoom(room.transform)==expected,"original custom rooms are classified without alteration");
                    for(int repeat=0;repeat<20;repeat++)
                    {
                        _=TownServiceLayout.Frame(room.transform,null);
                        foreach(var environment in new[]{TownServiceLayout.Environment.Open,TownServiceLayout.Environment.Cellar,TownServiceLayout.Environment.Forest})
                            for(byte service=1;service<=3;service++)for(int visitor=0;visitor<4;visitor++)
                                TownServiceLayout.Resolve(environment,service,visitor,out _,out _);
                    }
                    for(int i=0;i<transforms.Length;i++)Check(poses[i].Same(transforms[i]),"layout preserves every original scenery transform");
                    for(int i=0;i<filters.Length;i++)
                    {
                        Check(filters[i].sharedMesh==meshes[i],"layout never replaces original scenery meshes");
                        if(vertices[i]!=null)Check(vertices[i]!.SequenceEqual(meshes[i].vertices),"layout preserves readable scenery vertex coordinates");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(room); }
            }
            Check(rooms==2,"both original custom rooms are audited");
        }
        finally { bundle.Unload(true); }
    }
    public static int Run()
    {
        count=0;OriginalRooms();
        var rows=new List<string>();
        foreach(var environment in new[]{TownServiceLayout.Environment.Cellar,TownServiceLayout.Environment.Forest})
        {
            string label=environment==TownServiceLayout.Environment.Cellar?"cellar":"forest";
            for(byte service=1;service<=3;service++)
            {
                TownServiceLayout.Resolve(environment,service,0,out Vector3 p,out float yaw);
                Check(p.magnitude>=2f&&p.magnitude<=2.41f,"residents fit the original room radius");
                Check(p.x >= 0f, "residents stay in the front semicircle of the authored map reading side");
                TownServiceLayout.Resolve(TownServiceLayout.Environment.Open,service,0,out Vector3 other,out float otherYaw);
                Check(Vector3.Distance(p,other)<.00001f&&yaw==otherYaw,"environment choice preserves shared layout");
                rows.Add(FormattableString.Invariant($"{label},resident,{service},{p.x},{p.z},{yaw}"));
            }
            for(int visitor=1;visitor<=3;visitor++)
            {
                TownServiceLayout.Resolve(environment,1,visitor,out Vector3 p,out float yaw);
                Check(p.magnitude<2.71f,"visitor workspaces fit the original room radius");
                for(byte service=1;service<=3;service++)
                {
                    TownServiceLayout.Resolve(TownServiceLayout.Environment.Open,service,visitor,out Vector3 other,out float otherYaw);
                    Check(Vector3.Distance(p,other)<.00001f&&yaw==otherYaw,"visitor reservations are shared across room and service choices");
                }
                rows.Add(FormattableString.Invariant($"{label},visitor,{visitor},{p.x},{p.z},{yaw}"));
            }
        }
        bool badVisitor=false,badService=false;
        try { TownServiceLayout.Resolve(TownServiceLayout.Environment.Open,1,4,out _,out _); }catch(ArgumentOutOfRangeException){badVisitor=true;}
        try { TownServiceLayout.Resolve(TownServiceLayout.Environment.Open,0,0,out _,out _); }catch(ArgumentOutOfRangeException){badService=true;}
        Check(badVisitor&&badService,"invalid station identities cannot silently claim an existing reservation");
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-clearancePoses");
        if(at>=0)File.WriteAllLines(args[at+1],rows);
        return count;
    }
}
