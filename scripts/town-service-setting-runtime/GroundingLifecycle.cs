using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

internal static class GroundingLifecycle
{
    private static int assertions;
    private static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception(why);}
    private static void Near(float a,float b,string why)=>Check(Math.Abs(a-b)<.00015f,why+$": {a} != {b}");
    private static Mesh SupportMesh()
    {
        var points=new System.Collections.Generic.List<Vector3>();
        foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f})foreach(float z in new[]{-.5f,.5f})points.Add(new Vector3(x,y,z));
        return new Mesh { vertices=points.ToArray() };
    }
    internal static void Run()
    {
        // The local terrain still disagrees with the native plane. A tilted station
        // keeps sole/footprint samples distinct against that horizontal common plane,
        // retaining the root-only and lowest-support causal controls after Build746.
        var floor=new Transform { mesh=new Mesh { vertices=new[]{
            new Vector3(-100,-2,-100),new Vector3(100,6,-100),new Vector3(0,-2,100)},triangles=new[]{0,1,2}}};
        SkyAlternative.PlacedRoomRoot=new Transform{floor=floor};
        MapRoomDriver.Active=true;MapRoomDriver.Floor=0;
        GuildmasterRoomGeometry.Available=true;
        foreach(float scale in new[]{1f,198f})
        foreach(float yaw in new[]{0f,90f,225f})
        {
            var root=new Transform{position=new Vector3(4,7,-2),localScale=Vector3.one*scale,rotation=Quaternion.Euler(11,yaw,19)};
            GuildmasterRoomGeometry.Floor=7f-.07f*scale;
            var actor=new Transform{localPosition=new Vector3(0,.015f,.65f)};root.Add("Actor",actor);
            var furniture=new Transform();root.Add("Counter",furniture);
            var support=new Transform{localPosition=new Vector3(0,.08f,0),localScale=new Vector3(1.42f,.16f,.66f)};support.mesh=SupportMesh();furniture.Add("GroundSupportPlinth",support);
            var helper=new TownServiceGrounding(root);
            // Scale the test terrain with the station, preserving real local slopes.
            floor.localScale=Vector3.one*scale;
            helper.Resolve(out float actorOffset,out float bottom);
            Vector3 left=root.TransformPoint(new Vector3(-.12f,0,.65f)),right=root.TransformPoint(new Vector3(.12f,0,.65f));
            float upProjection=MathF.Cos(11f*MathF.PI/180)*MathF.Cos(19f*MathF.PI/180);
            Func<Vector3,float> expected=p=>(GuildmasterRoomGeometry.Floor-p.y)*upProjection/scale;
            Near(actorOffset,Math.Min(expected(left),expected(right)),"stable soles use the lower canonical plane contact");
            float low=float.PositiveInfinity;
            foreach(float x in new[]{-.71f,.71f})foreach(float z in new[]{-.33f,.33f})low=Math.Min(low,expected(root.TransformPoint(new Vector3(x,0,z))));
            Near(bottom,low,"actual support corners determine lowest floor");
            for(int repeat=0;repeat<4;repeat++)
            {
                int writes=actor.LocalWrites+support.LocalWrites;
                helper.Apply(actorOffset,bottom);
                if(repeat>0)Check(actor.LocalWrites+support.LocalWrites==writes,"steady follower does not dirty transforms");
                Near(actor.localPosition.y,.015f+actorOffset,"canonical floor delta preserves authored sole correction");
                Near(actor.localPosition.z,.65f,"actor anchor remains fixed");
                Near(support.localPosition.y+support.localScale.y*.5f,.16f,"support upper edge remains fixed");
                Near(support.localPosition.y-support.localScale.y*.5f,bottom,"support lower edge reaches terrain");
                Near(support.localScale.x,1.42f,"support width unchanged");
                helper.Resolve(out float repeatedActor,out float repeatedBottom);
                Near(repeatedActor,actorOffset,"no repeated actor drift");Near(repeatedBottom,bottom,"cached footprint prevents feedback");
            }
            helper.Apply(0,0);Near(actor.localPosition.y,.015f,"zero restores authored actor");
            Near(support.localPosition.y,.08f,"zero restores support position");Near(support.localScale.y,.16f,"zero restores support height");
            helper.Apply(float.NaN,0);Near(actor.localPosition.y,.015f,"invalid remote correction ignored");
        }
        floor.localScale=Vector3.one;
        GuildmasterRoomGeometry.Floor=-.07f;
        // A workspace has only a Counter child; it uses exactly the same contact handling.
        var workspace=new Transform();var counter=new Transform();workspace.Add("Counter",counter);
        var plinth=new Transform{localPosition=new Vector3(0,.08f,0),localScale=new Vector3(1.42f,.16f,.66f)};plinth.mesh=SupportMesh();counter.Add("GroundSupportPlinth",plinth);
        var onlyFurniture=new TownServiceGrounding(workspace);onlyFurniture.Resolve(out float absentActor,out float floorBottom);
        Near(absentActor,0,"workspace has no actor correction");Near(floorBottom,-.07f,"workspace uses the common native floor");
        // All four workbench legs and the altar plinth use the same fixed-top operation.
        foreach(string name in new[]{"Shrine","Workbench"})
        {
            var root=new Transform();var furniture=new Transform();root.Add(name,furniture);
            var parts=new System.Collections.Generic.List<Transform>();
            if(name=="Shrine")Add("AltarPlinth",new Vector3(0,.065f,0),new Vector3(1.35f,.13f,.64f));
            else foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Add("WorkbenchLeg"+x+z,new Vector3(x*.63f,.44f,z*.24f),new Vector3(.11f,.88f,.11f));
            void Add(string partName,Vector3 pos,Vector3 size){var part=new Transform{localPosition=pos,localScale=size};part.mesh=SupportMesh();furniture.Add("GroundSupport"+partName,part);parts.Add(part);}
            var helper=new TownServiceGrounding(root);helper.Resolve(out _,out float bottom);helper.Apply(0,bottom);
            foreach(var part in parts){Near(part.localPosition.y-part.localScale.y*.5f,bottom,"every authored support contacts bottom plane");Near(part.localPosition.y+part.localScale.y*.5f,name=="Shrine"?.13f:.88f,"all support tops preserved");}
            SkyAlternative.PlacedRoomRoot=null;helper.Resolve(out float actor,out bottom);Near(actor,0,"MR does not invent an absent actor");Near(bottom,-.07f,"MR retains the common native furniture plane");
            GuildmasterRoomGeometry.Available=false;
            helper.Resolve(out actor,out bottom);Near(actor,0,"native fallback does not invent an absent actor");Near(bottom,0,"missing native furniture restores the map floor plane");helper.Apply(actor,bottom);
            foreach(var part in parts)Near(part.localPosition.y-part.localScale.y*.5f,0,"native floor fallback resets contact");
            GuildmasterRoomGeometry.Available=true;
            SkyAlternative.PlacedRoomRoot=new Transform{floor=floor};
        }
        Console.WriteLine($"Town grounding: {assertions} production assertions passed");
    }
}
