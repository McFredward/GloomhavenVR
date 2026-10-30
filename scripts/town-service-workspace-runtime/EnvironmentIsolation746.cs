using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

public static partial class EnvironmentIsolation746
{
    private static int _checks;
    private static void Check(bool pass,string reason) { _checks++;if(!pass)throw new Exception(reason); }
    private static bool Near(Vector3 a,Vector3 b) => Vector3.Distance(a,b)<.001f;
    private readonly struct Picture
    {
        internal readonly Vector3 Position, Scale;
        internal readonly Quaternion Rotation;
        internal readonly float Actor, Support;
        internal Picture(TownServiceStation station)
        { Position=station.Root.position;Rotation=station.Root.rotation;Scale=station.Root.lossyScale;Actor=station.ActorFloorOffset;Support=station.FurnitureBottom; }
        internal bool Same(TownServiceStation station) => Near(Position,station.Root.position)
            && Quaternion.Angle(Rotation,station.Root.rotation)<.001f && Near(Scale,station.Root.lossyScale)
            && Math.Abs(Actor-station.ActorFloorOffset)<.0001f && Math.Abs(Support-station.FurnitureBottom)<.0001f;
        internal void Replay(TownServiceStation observer)
        { observer.Root.SetPositionAndRotation(Position,Rotation);observer.Root.localScale=Scale;observer.SetGrounding(Actor,Support); }
    }
    public static int Run()
    {
        _checks=0;
        Vector3 center=MapRoomDriver.Center;float scale=MapRoomDriver.Scale;
        var parchment=new GameObject("common native parchment",typeof(MeshRenderer));
        parchment.transform.rotation=Quaternion.Euler(0,37,0);MapRoomDriver.ParchmentRenderer=parchment.GetComponent<MeshRenderer>();
        var rooms=new List<GameObject>();
        foreach(float y in new[]{-4f,2f,17f})
        {
            var room=new GameObject("local scenery");var geo=new GameObject("RoomGeo");geo.transform.SetParent(room.transform,false);
            var floor=new GameObject(rooms.Count%2==0?"Floor":"Ground");floor.transform.SetParent(geo.transform,false);
            room.transform.SetPositionAndRotation(new Vector3(8,y,3),Quaternion.Euler(0,19+rooms.Count*73,0));room.transform.localScale=Vector3.one*(1+rooms.Count);
            rooms.Add(room);
        }
        try
        {
            foreach(float metric in new[]{.5f,2f,198f})
            foreach(byte service in new byte[]{1,2,3})
            {
                MapRoomDriver.Scale=metric;MapRoomDriver.Floor=MapRoomDriver.Center.y-.78f*metric;
                string name=service==1?"townmerchant":service==2?"townpriestess":"townenchantress";
                var author=new TownServiceStation(UnityEngine.Object.Instantiate(TownServiceAssets.Prefab(name)!),service,MapRoomDriver.Center,metric);
                var observer=new TownServiceStation(UnityEngine.Object.Instantiate(TownServiceAssets.Prefab(name)!),service,MapRoomDriver.Center,metric);
                try
                {
                    SkyAlternative.PlacedRoomRoot=null;author.RefreshEnvironment(true);var published=new Picture(author);published.Replay(observer);
                    Check(Math.Abs(author.Root.position.y-MapRoomDriver.Floor.Value)<.001f,"resident starts on the common native map floor");
                    foreach(GameObject room in rooms)
                    {
                        SkyAlternative.PlacedRoomRoot=room.transform;
                        var joiner=new TownServiceStation(UnityEngine.Object.Instantiate(TownServiceAssets.Prefab(name)!),service,MapRoomDriver.Center,metric);
                        try { joiner.RefreshEnvironment(true);Check(published.Same(joiner),
                            "late joins with another local environment retain the same native resident geometry"); }
                        finally { joiner.Dispose(); }
                        if(service!=1)
                        {
                            GloomhavenVR.Net.NetPlayerActors.Roster.Clear();
                            GloomhavenVR.Net.NetPlayerActors.Roster.Add((1,"owner","owner"));
                            GloomhavenVR.Net.NetPlayerActors.Roster.Add((7,"visitor","visitor"));
                            GloomhavenVR.Net.NetPlayerActors.Local=7;
                            using(var visitor=new TownServiceWorkspace(author.Root,service))
                                Check(Math.Abs(visitor.Root.position.y-MapRoomDriver.Floor.Value)<.001f,
                                    "late visitor counters use the native map floor in every local environment");
                        }
                        author.RefreshEnvironment(true);
                        Check(published.Same(author),"changing the author's local environment never changes published resident geometry");
                        new Picture(author).Replay(observer);
                        observer.RefreshEnvironment(false);
                        Check(published.Same(observer),"changing either participant's environment preserves the same received actor and furniture geometry");
                        Check(observer._lighting.LocalEnvironment,"observer scenery refresh always selects its own local lighting");
                        var meshes=observer.Root.GetComponentsInChildren<Renderer>(true);
                        foreach(Renderer mesh in meshes) Check(!float.IsNaN(mesh.bounds.center.y)&&!float.IsInfinity(mesh.bounds.center.y),"original resident render geometry remains finite after environment changes");
                    }
                    SkyAlternative.PlacedRoomRoot=null;observer.RefreshEnvironment(true);
                    Check(published.Same(observer),"an authority handover retains the common geometry instead of adopting another local floor");
                    GuildmasterRoomGeometry.Floor=MapRoomDriver.Floor.Value-.12f*metric;
                    Check(TownServicePlacement.TryResolve(service,MapRoomDriver.Center,metric,out var nativeFloorPose,out _)
                        && Math.Abs(nativeFloorPose.y-GuildmasterRoomGeometry.Floor.Value)<.001f,"native Guildmaster furniture remains the common floor authority");
                    GuildmasterRoomGeometry.Floor=null;
                }
                finally { author.Dispose();observer.Dispose(); }
            }
        }
        finally
        {
            foreach(GameObject room in rooms)UnityEngine.Object.DestroyImmediate(room);
            UnityEngine.Object.DestroyImmediate(parchment);MapRoomDriver.ParchmentRenderer=null;
            MapRoomDriver.Center=center;MapRoomDriver.Scale=scale;MapRoomDriver.Floor=null;GuildmasterRoomGeometry.Floor=null;SkyAlternative.PlacedRoomRoot=null;
        }
        return _checks;
    }
}
