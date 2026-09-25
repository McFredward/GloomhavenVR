using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

public static class InteractionProgram
{
    private static int count;
    private static readonly List<string> PoseEvidence=new();
    private static void RecordPose(string environment,string role,int id,Vector3 position,Quaternion rotation,Quaternion frame)
    {
        Vector3 local=Quaternion.Inverse(frame)*((position-MapRoomDriver.Center)/MapRoomDriver.Scale);
        float heading=(Quaternion.Inverse(frame)*rotation).eulerAngles.y;
        PoseEvidence.Add(string.Join(",",environment,role,id.ToString(),local.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),local.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),heading.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
    }
    private static void Check(bool yes, string message) { count++; if (!yes) throw new Exception(message); }
    private static bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .001f;
    private static void Roster(params int[] ids)
    { NetPlayerActors.Roster.Clear(); foreach (int id in ids) NetPlayerActors.Roster.Add((id, "account" + id, "user" + id)); }
    private static Vector3 Expected(int slot)
    {
        var room=SkyAlternative.PlacedRoomRoot;
        TownServiceLayout.Resolve(TownServiceLayout.ForRoom(room),0,slot,out var offset,out var heading);
        return MapRoomDriver.Center+TownServiceLayout.Frame(room,MapRoomDriver.ParchmentRenderer?.transform)*offset*MapRoomDriver.Scale;
    }
    private static void PlaceStation(Transform station, byte service = 3)
    {
        Check(TownServicePlacement.TryResolve(service, MapRoomDriver.Center, MapRoomDriver.Scale, out var p, out var q), "actual workspace resident placement resolves");
        station.SetPositionAndRotation(p, q); station.localScale = Vector3.one * MapRoomDriver.Scale;
    }
    private static Vector2[] Rectangle(Vector3 p, Quaternion q, float x, float zmin, float zmax)
    {
        return new[] { new Vector3(-x, 0, zmin), new Vector3(x, 0, zmin), new Vector3(x, 0, zmax), new Vector3(-x, 0, zmax) }
            .Select(v => p + q * v).Select(v => new Vector2(v.x, v.z)).ToArray();
    }
    private static bool Separated(Vector2[] a, Vector2[] b)
    {
        foreach (var shape in new[] { a, b }) for (int i = 0; i < 2; i++)
        {
            Vector2 edge = shape[i + 1] - shape[i], axis = new Vector2(-edge.y, edge.x).normalized;
            var aa = a.Select(v => Vector2.Dot(v, axis)).ToArray(); var bb = b.Select(v => Vector2.Dot(v, axis)).ToArray();
            if (aa.Max() < bb.Min() - .01f || bb.Max() < aa.Min() - .01f) return true;
        }
        return false;
    }
    // Measured room-audit envelopes, not the obsolete visitor merchant cabinet. Five
    // centimetres of clearance surround each real component in the reservation SAT test.
    private static readonly Vector4 Cabinet = new(-1.64f, -.44f, -.09f, .72f);
    private static readonly Vector4 Lectern = new(-.36f, .36f, -.08f, .52f);
    private static readonly Vector4 Worktop = new(-.88f, .88f, -.44f, .50f);
    private static readonly Vector4 Lantern = new(-.88f, -.48f, .28f, .96f);
    private static readonly Vector4 RightLantern = new(.48f, .88f, .28f, .96f);
    private static readonly Vector4 Actor = new(-.60f, .60f, .30f, 1.20f);
    private static Vector2[] Envelope(Vector3 p, Quaternion q, Vector4 bounds, float padding = .05f)
    {
        return new[] { new Vector3(bounds.x-padding,0,bounds.z-padding), new Vector3(bounds.y+padding,0,bounds.z-padding),
            new Vector3(bounds.y+padding,0,bounds.w+padding), new Vector3(bounds.x-padding,0,bounds.w+padding) }
            .Select(v => p + q * v).Select(v => new Vector2(v.x,v.z)).ToArray();
    }
    private static bool Contains(Vector4 bounds, Vector3 point) => point.x >= bounds.x-.001f
        && point.x <= bounds.y+.001f && point.z >= bounds.z-.001f && point.z <= bounds.w+.001f;
    private static List<Vector2[]> Parts(Vector3 p,Quaternion q,byte service,bool actor)
    {
        var parts=new List<Vector2[]>();
        bool merchant=actor && service==1;
        if(merchant) { parts.Add(Envelope(p,q,Cabinet)); parts.Add(Envelope(p,q,Lectern)); }
        else parts.Add(Envelope(p,q,Worktop));
        if(actor) parts.Add(Envelope(p,q,Actor));
        if(!actor || service==3) { parts.Add(Envelope(p,q,Lantern)); parts.Add(Envelope(p,q,RightLantern)); }
        return parts;
    }
    private static void CheckGeometry(List<TownServiceWorkspace> workspaces)
    {
        var fixedStations=new List<List<Vector2[]>>();
        for(byte service=1;service<=3;service++)
        {
            Check(TownServicePlacement.TryResolve(service,MapRoomDriver.Center,MapRoomDriver.Scale,out var p,out var q),"actual resident source supplies clearance pose");
            var parts=Parts((p-MapRoomDriver.Center)/MapRoomDriver.Scale,q,service,true);
            var map = Rectangle(Vector3.zero, TownServiceLayout.Frame(SkyAlternative.PlacedRoomRoot,MapRoomDriver.ParchmentRenderer?.transform), .80f, -1.16f, 1.16f);
            Check(parts.All(part => Separated(part,map)),"resident furniture clears full native map table diagonal");
            foreach(var previous in fixedStations)foreach(var a in parts)foreach(var b in previous)
                Check(Separated(a,b),"permanent stations clear each other's actual work and actor envelopes");
            fixedStations.Add(parts);
        }
        var extras=new List<List<Vector2[]>>();
        for(int i=1;i<workspaces.Count;i++)
        {
            var root=workspaces[i].Root;
            var parts=Parts((root.position-MapRoomDriver.Center)/MapRoomDriver.Scale,root.rotation,3,false);
            Vector3 inward=-root.forward;
            var map = Rectangle(Vector3.zero,TownServiceLayout.Frame(SkyAlternative.PlacedRoomRoot,MapRoomDriver.ParchmentRenderer?.transform),.80f,-1.16f,1.16f);
            Check(parts.All(part => Separated(part,map)),"open counter stays outside complete native map table diagonal");
            Vector3 toward=MapRoomDriver.Center-root.position;toward.y=0f;
            Check(Vector3.Dot(inward,toward.normalized)>.65f,"angled counter still faces toward map");
            foreach(var fixedParts in fixedStations)foreach(var a in parts)foreach(var b in fixedParts)
                Check(Separated(a,b),"extra counter clears all three actual resident envelopes");
            foreach(var previous in extras)foreach(var a in parts)foreach(var b in previous)
                Check(Separated(a,b),"full-size extra counters cannot overlap");
            extras.Add(parts);
            var returnTemplate = workspaces[i].FurnitureRoot.Find("CounterReturn");
            if (returnTemplate != null) Check(!returnTemplate.gameObject.activeSelf, "return authoring template stays hidden in the main counter");
            foreach(var filter in workspaces[i].FurnitureRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (returnTemplate != null && filter.transform.IsChildOf(returnTemplate)) continue;
                var mesh=filter.sharedMesh;
                Check(mesh != null, "shipping furniture mesh is present: " + filter.name);
                // Material batches can join the worktop and a rear lantern into one mesh.
                // Empty corners of that combined AABB are not occupied furniture.
                foreach(Vector3 v in mesh!.vertices)
                {
                    var local=root.InverseTransformPoint(filter.transform.TransformPoint(v));
                    Check(Contains(Worktop,local)||Contains(Lantern,local)||Contains(RightLantern,local),
                        "shipping furniture fits reserved physical counter envelope: " + filter.name + " " + local.ToString("F5"));
                }
            }
        }
    }
    private static void GroundingParity(Transform room)
    {
        foreach(byte service in new byte[]{1,2,3})
        {
            string name=service==1?"townmerchant":service==2?"townpriestess":"townenchantress";
            var owner=UnityEngine.Object.Instantiate(TownServiceAssets.Prefab(name)!);
            var peer=UnityEngine.Object.Instantiate(TownServiceAssets.Prefab(name)!);
            owner.transform.position=peer.transform.position=room.position+room.TransformVector(new Vector3(2,.2f,2));
            owner.transform.rotation=peer.transform.rotation=Quaternion.Euler(0,37,0);
            owner.transform.localScale=peer.transform.localScale=room.lossyScale;
            using var local=new TownServiceGrounding(owner.transform);
            using var remote=new TownServiceGrounding(peer.transform);
            try
            {
                var before=owner.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t,t=>(t.localPosition,t.localScale));
                var supports=owner.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("GroundSupport") && f.gameObject.activeInHierarchy).ToArray();
                var top=supports.ToDictionary(f=>f,f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)).Max(v=>v.y));
                Check(supports.Length>0,"every authored station exposes measured supports");
                local.Resolve(out float actorOffset,out float bottom);local.Apply(actorOffset,bottom);
                // Observers consume the owner's parameters even if their local room differs.
                SkyAlternative.PlacedRoomRoot=null;remote.Apply(actorOffset,bottom);SkyAlternative.PlacedRoomRoot=room;
                var mirrored=peer.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("GroundSupport") && t.gameObject.activeInHierarchy).ToDictionary(t=>t.name,t=>t);
                foreach(var foot in supports)
                {
                    var world=foot.sharedMesh.vertices.Select(v=>foot.transform.TransformPoint(v)).ToArray();
                    Check(Math.Abs(world.Max(v=>v.y)-top[foot])<.001f,"ground support stretching preserves authored top anchor");
                    var target=mirrored[foot.name];
                    Check(Close(foot.transform.localPosition,target.localPosition) && Close(foot.transform.localScale,target.localScale),"remote uses identical support transforms without local floor resolution");
                }
                foreach(var item in before)
                    if(!item.Key.name.StartsWith("GroundSupport") && item.Key.name!="Actor")
                        Check(Close(item.Key.localPosition,item.Value.localPosition) && Close(item.Key.localScale,item.Value.localScale),"grounding leaves all non-support furniture transforms untouched");
                local.Dispose();
                foreach(var item in before)Check(Close(item.Key.localPosition,item.Value.localPosition) && Close(item.Key.localScale,item.Value.localScale),"grounding disposal restores exact original transforms");
            }
            finally { SkyAlternative.PlacedRoomRoot=room;UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(peer); }
        }
    }
    private static void ReadingIndependentRooms()
    {
        float previousYaw=MapRoomDriver.Yaw;
        var parchment=new GameObject("SharedParchment",typeof(MeshRenderer));
        parchment.transform.rotation=Quaternion.Euler(0,37,0);MapRoomDriver.ParchmentRenderer=parchment.GetComponent<MeshRenderer>();
        var sharedPosition=new Vector3[3];var sharedRotation=new Quaternion[3];bool firstRoom=true;
        foreach(bool forest in new[]{false,true})
        {
            var room=new GameObject("MeasuredRoom");room.transform.rotation=Quaternion.Euler(0,37,0);
            var geometry=new GameObject("RoomGeo");geometry.transform.SetParent(room.transform,false);
            var floor=new GameObject(forest?"Ground":"Floor");floor.transform.SetParent(geometry.transform,false);
            SkyAlternative.PlacedRoomRoot=room.transform;
            Check(Quaternion.Angle(TownServiceLayout.Frame(room.transform,parchment.transform),room.transform.rotation)<.001f,"room frame matches canonical parchment yaw");
            for(byte service=1;service<=3;service++)
            {
                MapRoomDriver.Yaw=0;TownServicePlacement.TryResolve(service,MapRoomDriver.Center,MapRoomDriver.Scale,out var p,out var q);
                if(firstRoom){sharedPosition[service-1]=p;sharedRotation[service-1]=q;}
                else Check(Close(p,sharedPosition[service-1])&&Quaternion.Angle(q,sharedRotation[service-1])<.001f,"mixed environment peers resolve identical station poses");
                RecordPose(forest?"forest":"cellar","resident",service,p,q,room.transform.rotation);
                for(int reading=15;reading<360;reading+=15)
                {
                    MapRoomDriver.Yaw=reading;TownServicePlacement.TryResolve(service,MapRoomDriver.Center,MapRoomDriver.Scale,out var other,out var rotation);
                    Check(Close(p,other)&&Quaternion.Angle(q,rotation)<.001f,"reading-side change cannot rotate stations into room scenery");
                }
            }
            var station=new GameObject("RoomStation");PlaceStation(station.transform);
            var visitors=new List<TownServiceWorkspace>();Roster(1,7,19,53);
            try
            {
                foreach(int id in new[]{1,7,19,53})
                {NetPlayerActors.Local=id;visitors.Add(new TownServiceWorkspace(station.transform, id==7? (byte)2 : (byte)3));}
                CheckGeometry(visitors);
                for(int reading=0;reading<360;reading+=45)
                {
                    MapRoomDriver.Yaw=reading;WorkspaceClock.Now+=.3f;
                    for(int i=0;i<visitors.Count;i++)
                    {
                        NetPlayerActors.Local=new[]{1,7,19,53}[i];visitors[i].Tick();
                        Check(visitors[i].RelocationRevision==0&&visitors[i].RelocationVisibility==1f,"reading-side changes cannot restart unchanged workspace fade");
                    }
                }
                for(int i=1;i<visitors.Count;i++)RecordPose(forest?"forest":"cellar","visitor",i,visitors[i].Root.position,visitors[i].Root.rotation,room.transform.rotation);
            }
            finally{foreach(var visitor in visitors)visitor.Dispose();UnityEngine.Object.DestroyImmediate(station);}
            UnityEngine.Object.DestroyImmediate(room);SkyAlternative.PlacedRoomRoot=null;firstRoom=false;
        }
        for(byte service=1;service<=3;service++)
        {
            TownServicePlacement.TryResolve(service,MapRoomDriver.Center,MapRoomDriver.Scale,out var p,out var q);
            p.y=sharedPosition[service-1].y;
            Check(Close(p,sharedPosition[service-1])&&Quaternion.Angle(q,sharedRotation[service-1])<.001f,"default MR shares original parchment frame with custom rooms");
        }
        UnityEngine.Object.DestroyImmediate(parchment);MapRoomDriver.ParchmentRenderer=null;
        MapRoomDriver.Yaw=previousYaw;
    }
    private static void BakedActorEnvelope()
    {
        foreach(string name in new[]{"merchant","priestess","enchantress"})
        {
            var source=TownServiceAssets.Prefab("town"+name)!;
            var instance=UnityEngine.Object.Instantiate(source);

            try
            {
                var actor=instance.transform.Find("Actor");
                var animation=actor.GetComponent<Animation>();var total=new Bounds();bool firstBounds=true;
                foreach(AnimationState state in animation)foreach(float phase in new[]{0f,.25f,.5f,.75f})
                {
                    state.clip.SampleAnimation(actor.gameObject,state.length*phase);
                    foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        // FBX renderer scale is 100. Skin actual vertices with bind/bone
                        // matrices in station space, avoiding BakeMesh useScale ambiguity.
                        Mesh mesh=renderer.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;
                        var bind=mesh.bindposes;var bones=renderer.bones;
                        var matrices=bones.Select((bone,n)=>instance.transform.worldToLocalMatrix*bone.localToWorldMatrix*bind[n]).ToArray();
                        var bounds=new Bounds();bool first=true;
                        for(int n=0;n<vertices.Length;n++)
                        {
                            BoneWeight w=weights[n];Vector3 vertex=vertices[n];
                            Vector3 local=matrices[w.boneIndex0].MultiplyPoint3x4(vertex)*w.weight0
                                +matrices[w.boneIndex1].MultiplyPoint3x4(vertex)*w.weight1
                                +matrices[w.boneIndex2].MultiplyPoint3x4(vertex)*w.weight2
                                +matrices[w.boneIndex3].MultiplyPoint3x4(vertex)*w.weight3;
                            if(first){bounds=new Bounds(local,Vector3.zero);first=false;}else bounds.Encapsulate(local);
                        }
                        if(firstBounds){total=bounds;firstBounds=false;}else total.Encapsulate(bounds);

                    }
                }
                Debug.Log("TOWN_LAYOUT_ACTOR " + name + " " + total);
                Check(total.min.x>=-.60f && total.max.x<=.60f && total.min.z>=.30f && total.max.z<=1.20f && total.max.y<=2.15f,
                    "actual baked actor mesh fits measured anatomy volume "+name+" "+total);
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
        }
    }
    public static int Run()
    {
        count=0; PoseEvidence.Clear(); WorkspaceClock.Now=0; SkyAlternative.PlacedRoomRoot=null;
        ReadingIndependentRooms(); BakedActorEnvelope();
        foreach(byte service in new byte[]{2,3}) { WorkspaceClock.Now=0; ServiceWorkspace(service); }
        count += WorkspacePropsProgram.Run();
        return count;
    }
    private static void ServiceWorkspace(byte service)
    {
        var station = new GameObject("Shared station"); PlaceStation(station.transform,service);
        var template = TownServiceWorkspace.FurnitureTemplate(service)!;
        var originals = template.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
        var originalVisibility = originals.Select(m => m.GetFloat("_TownVisibility")).ToArray();
        var workspaces = new List<TownServiceWorkspace>();
        try
        {
            Roster(1, 7, 19, 53);
            for (int slot = 0; slot < 4; slot++)
            {
                NetPlayerActors.Local = new[] { 1, 7, 19, 53 }[slot];
                var workspace = new TownServiceWorkspace(station.transform,service); workspaces.Add(workspace); workspace.SetVisibility(1);
                Check(workspace.RelocationRevision == 0, "initial placement does not advance relocation revision");
                // Geometry first also proves the old outward layout fails the regression check.
                if (slot > 0) CheckGeometry(workspaces);
                Check(Close(workspace.Root.position, slot == 0 ? station.transform.position : Expected(slot)), "complete roster assigns distinct ordinal including flat peers");
                Check(workspace.FurnitureRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0, "furniture clone contains no NPC");
                Check(workspace.FurnitureRoot.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "decorative furniture cannot intercept input");
                Check(workspace.Materials.All(m => !originals.Contains(m)), "each workspace owns its materials");
                Check(workspace.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - (slot == 0 ? 0 : 1)) < .0001f), "primary duplicate is hidden while extensions are visible");
                Check(workspace.FurnitureRoot.gameObject.activeSelf == (slot != 0), "primary hidden geometry costs no duplicate draw calls");
                foreach (var renderer in workspace.FurnitureRoot.GetComponentsInChildren<MeshRenderer>(true))
                { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); Check(block.isEmpty, "mesh materials are captured without unsupported property blocks"); }
            }
            for (int i = 0; i < originals.Length; i++) Check(originals[i].GetFloat("_TownVisibility") == originalVisibility[i], "shared template materials remain unchanged");
            for (int i = 0; i < workspaces.Count; i++) for (int j = i + 1; j < workspaces.Count; j++)
                Check(!workspaces[i].Materials.Any(m => workspaces[j].Materials.Contains(m)), "material fades cannot affect another visitor");
            NetPlayerActors.Local = 53; var moving = workspaces[3]; var previous = moving.Root.position;
            Roster(1, 19, 53); WorkspaceClock.Now = 1; moving.Tick(false);
            WorkspaceClock.Now = 1.15f; moving.Tick(false);
            Check(Close(moving.Root.position, previous) && moving.RelocationVisibility == 1f, "held or returning card defers relocation");
            WorkspaceClock.Now = 2; moving.Tick();
            Check(Close(moving.Root.position, previous) && moving.RelocationVisibility == 1f, "roster change begins from existing owner pose");
            Check(!moving.InputAvailable, "relocation blocks new grabs even in first fully opaque frame");
            Check(moving.RelocationRevision == 0, "fade-out does not advance relocation revision");
            WorkspaceClock.Now = 2.055f; moving.Tick();
            Check(Close(moving.Root.position, previous) && Math.Abs(moving.RelocationVisibility - .5f) < .001f, "owner dissolves without moving visible original cards");
            Check(moving.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - .5f) < .001f), "furniture publishes same intermediate relocation fade");
            WorkspaceClock.Now = 2.12f; moving.Tick();
            Check(moving.RelocationVisibility == 0f && Close(moving.Root.position, Expected(2)), "pose change has a fully invisible published frame");
            Check(moving.RelocationRevision == 1, "only invisible pose change advances relocation revision");
            WorkspaceClock.Now = 2.18f; moving.Tick(); Check(moving.RelocationVisibility > 0 && moving.RelocationVisibility < 1, "new pose fades in rather than popping");
            WorkspaceClock.Now = 2.23f; moving.Tick(); Check(moving.RelocationVisibility == 1 && Close(moving.Root.position, Expected(2)), "membership transition reaches full-opacity current ordinal");
            WorkspaceClock.Now = 4; moving.Tick(); Check(moving.RelocationRevision == 1, "ordinary fade and stable roster retain relocation revision"); Check(moving.RelocationVisibility == 1f && moving.InputAvailable, "unchanged roster cannot restart transition");
            using (var lateJoin = new TownServiceWorkspace(station.transform,service)) Check(Close(lateJoin.Root.position, moving.Root.position), "late join and survivor converge from native roster");
            // Ground sampling and canonical frame changes are actual production placement code.
            var room = new GameObject("sloped room"); room.transform.position = MapRoomDriver.Center; room.transform.localScale = Vector3.one * MapRoomDriver.Scale;
            var geometry = new GameObject("RoomGeo"); geometry.transform.SetParent(room.transform, false);
            var ground = new GameObject("Ground"); ground.transform.SetParent(geometry.transform, false);
            var mesh = new Mesh { vertices = new[] { new Vector3(-10, -.2f, -10), new Vector3(10, .2f, -10), new Vector3(10, .4f, 10), new Vector3(-10, 0f, 10) }, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            try
            {
                SkyAlternative.PlacedRoomRoot = room.transform; WorkspaceClock.Now = 5; moving.Tick(); WorkspaceClock.Now = 5.12f; moving.Tick(); WorkspaceClock.Now = 5.23f; moving.Tick();
                Vector3 local = room.transform.InverseTransformPoint(moving.Root.position);
                Check(Math.Abs(local.y - (.1f + .02f * local.x + .01f * local.z)) < .0001f, "workspace rests on original sloped floor at its own target");
                Check(Math.Abs(local.y) > .005f, "slope fixture differs measurably from room-root height");
                var supports=moving.FurnitureRoot.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f=>f.name.StartsWith("GroundSupport") && f.gameObject.activeInHierarchy).ToArray();
                Check(supports.Length>0,"workspace has actual authored ground supports");
                foreach(var foot in supports)
                {
                    var vertices=foot.sharedMesh.vertices;
                    Vector3 corner=vertices.Select(v=>foot.transform.TransformPoint(v)).OrderBy(v=>v.y).First();
                    float floor=TownServicePlacement.GroundHeight(room.transform,corner);
                    Check(corner.y<=floor+.001f,"ground supports reach their sampled terrain: workspace");
                }
                GroundingParity(room.transform);
                var parent=new GameObject("ReturnParent").transform;parent.SetParent(moving.Root,false);
                parent.localPosition=Vector3.up*TownServiceMerchantLayout.WorktopHeight;
                using(var counter=new TownServiceMerchantCounter(parent,0,true,0))
                {
                    foreach(var foot in counter.Root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("GroundSupport")))
                    {
                        Vector3 corner=foot.sharedMesh.vertices.Select(v=>foot.transform.TransformPoint(v)).OrderBy(v=>v.y).First();
                        Check(corner.y<=TownServicePlacement.GroundHeight(room.transform,corner)+.001f,"ground supports reach their sampled terrain: legacy return");
                    }
                }
                UnityEngine.Object.DestroyImmediate(parent.gameObject);
            }
            finally { SkyAlternative.PlacedRoomRoot = null; UnityEngine.Object.DestroyImmediate(room); UnityEngine.Object.DestroyImmediate(mesh); }
            Roster(1, 19, 53, 88); NetPlayerActors.Local = 88;
            using (var reconnect = new TownServiceWorkspace(station.transform,service)) Check(Close(reconnect.Root.position, Expected(3)), "new connection ID resolves next distinct seat");
            moving.SetVisibility(.4f); Check(moving.Materials.All(m => Math.Abs(m.GetFloat("_TownVisibility") - .4f) < .0001f), "owner service dissolve remains independent of relocation");
            moving.Dispose(); moving.Dispose(); Check(!moving.Root.gameObject.activeSelf && moving.Materials.Count == 0, "disposal hides furniture immediately and releases owned materials");
            Roster(1, 19, 53, 88, 104); NetPlayerActors.Local = 104; bool rejected = false;
            try { using var excess = new TownServiceWorkspace(station.transform,service); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("exceeds four users"); }
            Check(rejected, "unexpected fifth user is rejected instead of overlapping a valid seat");
            Roster(); NetPlayerActors.Local = 0;
            using (var solo = new TownServiceWorkspace(station.transform,service)) { solo.SetVisibility(1); Check(Close(solo.Root.position, station.transform.position), "offline keeps original service workspace"); }
            System.IO.File.WriteAllLines(System.IO.Path.ChangeExtension(typeof(InteractionProgram).Assembly.Location,"poses.csv"),PoseEvidence);
            return;
        }
        finally { foreach (var workspace in workspaces) workspace.Dispose(); UnityEngine.Object.DestroyImmediate(station); }
    }
}
