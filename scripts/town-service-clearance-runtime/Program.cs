using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

// The room provider and clock are fixture boundaries. Actual Unity Transform/Quaternion
// behavior and the production lifecycle/layout implementation run in the real player loop.
// Destruction is observed for cleanup assertions and still delegated to Unity unchanged.
namespace GloomhavenVR.Core { internal static class SkyAlternative { internal static Transform? PlacedRoomRoot; } internal static class VRLog { internal static int Warnings; internal static void Warn(string category,string message) { Warnings++; } } }
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Quaternion YawOnly(Quaternion q) => Quaternion.Euler(0f,q.eulerAngles.y,0f); } }
internal static class ClearanceDestroy
{
    internal static readonly List<UnityEngine.Object> Destroyed=new();
    internal static void Record(UnityEngine.Object value) { Destroyed.Add(value);UnityEngine.Object.Destroy(value); }
}
internal static class ClearanceClock { internal static float Now; }
public static class InteractionProgram
{
    private static int count;
    private static void Check(bool pass,string message) { count++;if(!pass)throw new Exception(message); }
    private static void Scale(Transform room,Vector3 expected,string message) => Check(Vector3.Distance(room.localScale,expected)<.0001f,message);
    private static Vector3 Expanded(float scale) => new Vector3(scale*TownServiceRoomClearance.HorizontalExpansion,scale,scale*TownServiceRoomClearance.HorizontalExpansion);
    private static void Geometry()
    {
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-clearanceBundle");
        if(at<0)throw new Exception("Original environment bundle is required");
        var bundle=AssetBundle.LoadFromFile(args[at+1]);
        if(bundle==null)throw new Exception("actual environment bundle loads"); count++;
        try
        {
            foreach(string path in bundle.GetAllAssetNames().Where(p=>p.EndsWith("env_cellar.prefab") || p.EndsWith("env_swamp.prefab")))
            {
                var room=UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(path));
                try
                {
                    var root=room.transform;root.localScale=Vector3.one;
                    var original=new System.Collections.Generic.Dictionary<MeshFilter,Mesh>();
                    foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                        if(filter.name=="TrunksNear" || filter.name=="TrunksFar")original.Add(filter,filter.sharedMesh);
                    Transform? prop=root.Find(path.Contains("swamp")?"RoomGeo/Fern2":"RoomGeo/Barrel0");
                    if(prop==null)throw new Exception("original discrete scenery reference exists"); count++;
                    Vector3 propScale=prop.lossyScale;
                    using(var geometry=new TownServiceRoomGeometry(root))
                    {
                        root.localScale=new Vector3(3.5f,1,3.5f);geometry.Apply(3.5f);
                        Check(Vector3.Distance(prop.lossyScale,propScale)<.0001f,"discrete props keep original world proportions");
                        foreach(var pair in original)
                        {
                            Mesh before=pair.Value,after=pair.Key.sharedMesh;
                            Check(before!=after,"tree expansion owns a private mesh");
                            Check(before.uv.SequenceEqual(after.uv) && before.normals.SequenceEqual(after.normals),"tree UVs and normals remain byte-equivalent");
                            var v=before.vertices;var w=after.vertices;var t=before.triangles;
                            for(int i=0;i<t.Length;i+=3)
                                Check(Vector3.Distance(v[t[i]]-v[t[i+1]],w[t[i]]-w[t[i+1]])<.00001f,"tree translation preserves every triangle edge");
                            Check(after.bounds.size.x>before.bounds.size.x,"individual tree centres expand clearing");
                        }
                        geometry.Apply(1f);
                        foreach(var pair in original)Check(pair.Key.sharedMesh==pair.Value,"disable restores original tree mesh");
                    }
                    foreach(var pair in original)Check(pair.Key.sharedMesh==pair.Value,"geometry disposal restores original mesh");
                }
                finally { UnityEngine.Object.DestroyImmediate(room); }
            }
        }
        finally { bundle.Unload(true); }
    }
    private static void FailureRecovery()
    {
        TownServiceRoomClearance.Reset();
        var root=new GameObject("UnreadableOldRoom").transform;
        var geo=new GameObject("RoomGeo").transform;geo.SetParent(root,false);
        var good=new Mesh { name="ReadableTrunk" };
        good.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};good.triangles=new[]{0,1,2};
        var bad=UnityEngine.Object.Instantiate(good);bad.name="UnreadableTrunk";bad.UploadMeshData(true);
        MeshFilter Add(string name,Mesh mesh)
        {
            var go=new GameObject(name);go.transform.SetParent(geo,false);
            var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;return filter;
        }
        var first=Add("TrunksNear",good);Add("TrunksFar",bad);
        try
        {
            int copies=Resources.FindObjectsOfTypeAll<Mesh>().Count(m=>m.name.EndsWith(" TownClearance"));
            int warnings=VRLog.Warnings,disposed=ClearanceDestroy.Destroyed.Count;
            root.localScale=Vector3.one*2;SkyAlternative.PlacedRoomRoot=root;
            TownServiceRoomClearance.Tick(false);
            Check(Resources.FindObjectsOfTypeAll<Mesh>().Count(m=>m.name.EndsWith(" TownClearance"))==copies,"initial disabled mode never clones room meshes");
            Check(VRLog.Warnings==warnings,"disabled mode never reads incompatible room geometry");
            ClearanceClock.Now+=10;TownServiceRoomClearance.Tick(true);
            Check(ClearanceDestroy.Destroyed.Count==disposed+1,"partially constructed geometry disposes existing private meshes");
            Check(first.sharedMesh==good,"failed preparation retains original mesh references");
            Scale(root,Expanded(2),"incompatible geometry falls back to safe expanded shell");
            for(int i=0;i<100;i++)TownServiceRoomClearance.Tick(true);
            Check(VRLog.Warnings==warnings+1,"incompatible geometry reports once without repeated retries");
            TownServiceRoomClearance.Reset();Scale(root,Vector3.one*2,"failed geometry still restores exact authored scale");
            UnityEngine.Object.DestroyImmediate(geo.Find("TrunksFar").gameObject);
            TownServiceRoomClearance.Tick(true);disposed=ClearanceDestroy.Destroyed.Count;
            Check(first.sharedMesh!=good,"replacement room can prepare valid geometry after reset");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            SkyAlternative.PlacedRoomRoot=null;TownServiceRoomClearance.Tick(false);
            Check(ClearanceDestroy.Destroyed.Count==disposed+1,"destroyed native room releases private meshes on next tick");
        }
        finally
        {
            TownServiceRoomClearance.Reset();SkyAlternative.PlacedRoomRoot=null;
            if(root!=null)UnityEngine.Object.DestroyImmediate(root.gameObject);
            UnityEngine.Object.DestroyImmediate(good);UnityEngine.Object.DestroyImmediate(bad);
        }
    }
    public static int Run()
    {
        var a=new GameObject("OriginalCellarRoom").transform;
        var b=new GameObject("OriginalForestRoom").transform;
        try
        {
            SkyAlternative.PlacedRoomRoot=a;a.localScale=Vector3.one*2f;
            TownServiceRoomClearance.Tick(true);
            Scale(a,Expanded(2),"new room widens immediately while preserving vertical scale");
            for(int i=0;i<500;i++){ClearanceClock.Now+=.01f;TownServiceRoomClearance.Tick(true);}
            Scale(a,Expanded(2),"repeated updates cannot compound expansion");
            a.localScale*=1.5f;TownServiceRoomClearance.Tick(true);
            Scale(a,Expanded(3),"relative external zoom cannot apply expansion twice");
            a.localScale=Vector3.one*4f;TownServiceRoomClearance.Tick(true);
            Scale(a,Expanded(4),"absolute room reseat adopts new authored scale");
            ClearanceClock.Now+=.1f;TownServiceRoomClearance.Tick(false);
            Check(a.localScale.x<Expanded(4).x && a.localScale.x>4f,"disable transition reduces scale smoothly");
            ClearanceClock.Now+=10;TownServiceRoomClearance.Tick(false);
            Scale(a,Vector3.one*4f,"disabled room restores exact authored scale");
            ClearanceClock.Now+=10;TownServiceRoomClearance.Tick(true);
            a.localScale*=.5f;TownServiceRoomClearance.Reset();
            Scale(a,Vector3.one*2,"reset preserves external zoom before next tick");
            TownServiceRoomClearance.Tick(true);b.localScale=Vector3.one*3;SkyAlternative.PlacedRoomRoot=b;
            TownServiceRoomClearance.Tick(true);
            Scale(a,Vector3.one*2,"room switch restores old authored scale");
            Scale(b,Expanded(3),"room switch expands replacement once");
            SkyAlternative.PlacedRoomRoot=null;TownServiceRoomClearance.Tick(true);
            Scale(b,Vector3.one*3,"missing room restores previous authored scale");
            SkyAlternative.PlacedRoomRoot=a;TownServiceRoomClearance.Tick(true);
            UnityEngine.Object.DestroyImmediate(a.gameObject);TownServiceRoomClearance.Reset();
            Check(true,"destroyed room teardown is safe");
            Geometry();
            FailureRecovery();
            var rows=new List<string>();
            foreach(TownServiceLayout.Environment environment in Enum.GetValues(typeof(TownServiceLayout.Environment)))
            {
                for(int visitor=0;visitor<4;visitor++)for(byte service=1;service<=(visitor==0?3:1);service++)
                {
                    TownServiceLayout.Resolve(environment,service,visitor,out var pose,out float yaw);
                    TownServiceLayout.Resolve(TownServiceLayout.Environment.Open,service,visitor,out var canonical,out float heading);
                    Check(Vector3.Distance(pose,canonical)<.0001f && Math.Abs(yaw-heading)<.0001f,"environment choice preserves shared layout");
                    if(visitor==0)Check(Math.Abs(pose.magnitude-4.8f)<.0001f,"residents retain requested semicircle radius");
                    if(environment!=TownServiceLayout.Environment.Open)
                        rows.Add(string.Join(",",environment.ToString().ToLowerInvariant(),visitor==0?"resident":"visitor",visitor==0?service:visitor,pose.x.ToString("R",CultureInfo.InvariantCulture),pose.z.ToString("R",CultureInfo.InvariantCulture),yaw.ToString("R",CultureInfo.InvariantCulture)));
                }
            }
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-clearancePoses");
            if(at>=0)File.WriteAllLines(args[at+1],rows);
            return count;
        }
        finally
        {
            TownServiceRoomClearance.Reset();SkyAlternative.PlacedRoomRoot=null;
            if(a!=null)UnityEngine.Object.DestroyImmediate(a.gameObject);
            if(b!=null)UnityEngine.Object.DestroyImmediate(b.gameObject);
        }
    }
}
