using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

public static partial class EnvironmentProgram
{
    private static void RoomFloorMixedOwnership()
    {
        using var room = new Room();
        var legacyOne = room.Floor(.6f); var legacyTwo = room.Floor(.9f);
        var floorOne = room.Surface("AuditedRoomFloorOne", x: 1.2f);
        var floorTwo = room.Surface("AuditedRoomFloorTwo", x: 1.5f);
        Mesh floorMesh = room.Mesh();
        floorOne.GetComponent<MeshFilter>().sharedMesh = floorTwo.GetComponent<MeshFilter>().sharedMesh = floorMesh;
        ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(() => true, mesh => mesh == floorMesh,
            (Renderer renderer, out Mesh mesh) => { mesh = floorMesh; return true; });
        try
        {
            Configure(true, false, 100);
            ScenarioEnvironmentBudget.Placed(room.Generated); ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length == 2,
                "shared native materials and bounds keep room-floor ownership separate from legacy floor groups");
            bool drawn = false;
            room.ObserveRender = () => drawn = legacyOne.forceRenderingOff && legacyTwo.forceRenderingOff
                && floorOne.forceRenderingOff && floorTwo.forceRenderingOff;
            room.Render(); room.ObserveRender = null;
            Check(drawn && PerfMonitor.Counts["Environment.RoomFloorSources"] == 2
                && PerfMonitor.Counts["Environment.RoomFloorGroups"] == 1,
                "mixed floor camera submission counts only sources with the room-floor endpoint and marker contract");
        }
        finally
        {
            room.ObserveRender = null;
            ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(() => false, _ => false,
                (Renderer renderer, out Mesh mesh) => { mesh = null!; return false; });
        }
    }

    // Complete production chunk ownership/engine callbacks execute here. Catalog admission,
    // the terrain morph endpoint and the world material factory are explicit boundaries;
    // their source-proven mesh/animation contracts have independent runtime suites.
    private static void RoomFloorNativeFadeMaterial()
    {
        using var room=new Room();
        Mesh native=room.Mesh();
        var first=room.Surface("AuditedRoomFloorOne",x:1f);
        var second=room.Surface("AuditedRoomFloorTwo",x:2f);
        first.GetComponent<MeshFilter>().sharedMesh=second.GetComponent<MeshFilter>().sharedMesh=native;
        first.transform.localPosition+=Vector3.up;second.transform.localPosition+=Vector3.up;
        room.Original.SetFloat("_WallFade_On",1f);
        Shader shader=Shader.Find("GloomhavenVR/WorldSimpleMaterial");
        Check(shader!=null&&shader.isSupported,"production world floor shader imports on actual Unity graphics");
        var variant=new Material(shader);
        variant.SetFloat("_GHVRWorldMaterialMode",2f);variant.SetFloat("_GHVRWorldNativeRoute",4f);
        variant.SetFloat("_GHVRWorldAmbientWeight",0f);variant.SetFloat("_Cutoff",.5f);
        variant.SetColor("_Tint",new Color(.8f,.4f,.2f,0f));
        bool enabled=true,world=false;
        int toggle=Shader.GetGlobalInt("ToggleWallFade");
        Texture savedMap=Shader.GetGlobalTexture("_TilesOcclusionMap");
        var mask=new Texture2D(1,1);mask.SetPixel(0,0,new Color(1,0,0,0));mask.Apply();
        ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(()=>enabled,mesh=>mesh==native,
            (Renderer renderer,out Mesh mesh)=>{mesh=native;return true;});
        ScenarioEnvironmentBudget.ConfigureWorldMaterialIntegration(_=>{},_=>{},_=>{},()=>{},m=>m==variant?room.Original:m,
            m=>m==variant,()=>world);
        ScenarioEnvironmentBudget.ConfigureRoomFloorMaterials(_=>variant,()=>new FloorMaterialScope());
        try
        {
            ScenarioEnvironmentBudget.Placed(room.Generated);ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==0,"live native floor fade material cannot enter groups without its floor-aware world variant");
            world=true;ScenarioEnvironmentBudget.RoomFloorMeshReady(first);
            ScenarioEnvironmentBudget.RoomFloorMeshReady(second);ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==1&&room.Chunks()[0].sharedMaterial==variant,
                "broader floor grouping draws the owned world variant while leaving native floor material slots intact");
            Shader.SetGlobalInt("ToggleWallFade",1);Shader.SetGlobalTexture("_TilesOcclusionMap",mask);
            bool masked=false;room.ObserveRender=()=>masked=first.forceRenderingOff&&second.forceRenderingOff;
            Color32[] pixels=room.Render();room.ObserveRender=null;
            int colored=0;foreach(var pixel in pixels)if(pixel.r>30||pixel.g>30||pixel.b>30)colored++;
            Check(masked&&colored>20,"actual room-floor shader pixels ignore the native wall mask rather than disappear with walls");
            Check(first.sharedMaterial==room.Original&&second.sharedMaterial==room.Original,
                "room floor grouping never binds world variants onto original sources");
            // The independent world owner can legitimately bind its own source
            // slots later. Its exact notification boundary must rebuild floor
            // groups against those references without confusing them with natives.
            first.sharedMaterial=variant;ScenarioEnvironmentBudget.WorldMaterialChanged(first);
            second.sharedMaterial=variant;ScenarioEnvironmentBudget.WorldMaterialChanged(second);
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            room.ObserveRender=()=>masked=first.forceRenderingOff&&second.forceRenderingOff;
            room.Render();room.ObserveRender=null;
            Check(masked&&room.Chunks().Length==1&&room.Chunks()[0].sharedMaterial==variant,
                "world-owned source reference changes rebuild the floor group with canonical native admission and a current draw variant");
            room.Original.SetFloat("_AddVertexAnim",1f);
            room.ObserveRender=()=>masked=Array.Exists(room.Chunks(),r=>r.enabled)||first.forceRenderingOff||second.forceRenderingOff;
            room.Render();room.ObserveRender=null;
            Check(!masked,
                "in-place native floor material animation immediately revokes its private render group");
        }
        finally
        {
            Shader.SetGlobalInt("ToggleWallFade",toggle);Shader.SetGlobalTexture("_TilesOcclusionMap",savedMap);
            ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(()=>false,_=>false,
                (Renderer renderer,out Mesh mesh)=>{mesh=null!;return false;});
            ScenarioEnvironmentBudget.ConfigureWorldMaterialIntegration(_=>{},_=>{},_=>{},()=>{},m=>m,_=>false,()=>false);
            ScenarioEnvironmentBudget.ConfigureRoomFloorMaterials(m=>m,()=>new FloorMaterialScope());
            UnityEngine.Object.DestroyImmediate(mask);UnityEngine.Object.DestroyImmediate(variant);
        }
    }
    private sealed class FloorMaterialScope:IDisposable {public void Dispose(){}}

    private static void BroaderRoomFloorGroups()
    {
        using var room = new Room();
        var originals = new List<MeshRenderer>();
        Mesh native = room.Mesh(); native.name = "AuditedNativeFloorFixture";
        int[] dense = new int[24];
        for (int i=0;i<4;i++) Array.Copy(native.triangles,0,dense,i*6,6);
        native.triangles=dense;
        Mesh endpoint=room.Mesh(); endpoint.name="PrivateSettledFloorFixture";
        Mesh? selected=endpoint;
        bool enabled=true;
        for(int i=0;i<8;i++)
        {
            var renderer=room.Surface("NativeArchitectureFixture",x:.5f+i*.2f);
            renderer.GetComponent<MeshFilter>().sharedMesh=native;
            originals.Add(renderer);
        }
        ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(() => enabled,
            mesh => mesh==native, (Renderer renderer,out Mesh mesh) => {mesh=selected!;return selected!=null;});
        // Terrain owns potential standalone proxies. Floor groups must explicitly
        // take their completed endpoints without creating simultaneous substitutes.
        ScenarioEnvironmentBudget.ConfigureTerrainIntegration(_=>{},_=>{},_=>{},()=>{},_=>true);
        try
        {
            ScenarioEnvironmentBudget.Placed(room.Generated);
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==1, "broader catalog-proven floors group even without legacy floor names or a floor-plane proxy");
            MeshRenderer chunk=room.Chunks()[0];
            Check(chunk.transform.IsChildOf(room.Host.transform), "broader floor groups stay outside every native content cloning root");
            Check(chunk.GetComponent<MeshFilter>().sharedMesh.triangles.Length==8*endpoint.triangles.Length,
                "group geometry uses settled coarse endpoints and removes 75 percent of fixture floor triangle submissions");
            foreach(var source in originals)
                Check(source.GetComponent<MeshFilter>().sharedMesh==native && !source.isPartOfStaticBatch
                    && source.GetComponent<BoxCollider>()!=null && source.sharedMaterial==room.Original,
                    "native mesh pointer, Unity batching metadata, collider and original material slots survive grouping");
            bool leased=false;
            room.ObserveRender=()=>leased=originals.TrueForAll(r=>r.forceRenderingOff)&&chunk.enabled;
            room.Render();room.ObserveRender=null;
            Check(leased && originals.TrueForAll(r=>!r.forceRenderingOff) && !chunk.enabled,
                "one camera leases every source to its private floor group and returns native flags after rendering");
            Check(ScenarioEnvironmentBudget.HasPreparedRoomFloorGroup(originals[0]),
                "terrain ownership handshake identifies an actual prepared room-floor group");
            Check(PerfMonitor.Counts.TryGetValue("Environment.RoomFloorSources",out long sources)&&sources==8
                && PerfMonitor.Counts.TryGetValue("Environment.RoomFloorGroups",out long groups)&&groups==1,
                "completed camera counters expose actual surviving floor source reduction separately from preparation");
            Check(PerfMonitor.Counts.TryGetValue("Environment.RoomFloorOriginalTriangles",out long nativeTriangles)
                && nativeTriangles==64 && PerfMonitor.Counts.TryGetValue("Environment.RoomFloorSubmittedTriangles",out long submittedTriangles)
                && submittedTriangles==16,
                "completed floor-group triangle counters measure the submitted coarse geometry independently of per-floor proxies");
            var neverFade=new MaterialPropertyBlock();chunk.GetPropertyBlock(neverFade);
            Check(neverFade.GetFloat("_GHVRWorldNeverFade")==1f&&neverFade.GetFloat("_GHVRTerrainNeverFade")==1f,
                "private floor group carries floor-only never-fade markers without rewriting its shared material");

            // A head/view change cannot pin an obsolete morph endpoint or force a
            // native room visible. Test actual Renderer/Camera state, not only API counts.
            selected=null;
            room.ObserveRender=()=>leased=originals.TrueForAll(r=>r.forceRenderingOff);
            room.Render();room.ObserveRender=null;
            Check(!leased&&!chunk.enabled&&originals.TrueForAll(r=>!r.forceRenderingOff),
                "a live floor detail change revokes the obsolete group immediately before camera culling");
            selected=endpoint;
            originals[0].gameObject.SetActive(false);
            room.ObserveRender=()=>leased=chunk.enabled||originals[1].forceRenderingOff;
            room.Render();room.ObserveRender=null;
            Check(!leased,
                "hiding one native source cannot leave combined room geometry visible or hide its remaining originals");
            originals[0].gameObject.SetActive(true);
            var block=new MaterialPropertyBlock();block.SetColor("_Tint",Color.green);
            originals[0].SetPropertyBlock(block);
            room.ObserveRender=()=>leased=chunk.enabled||originals.Exists(r=>r.forceRenderingOff);
            room.Render();room.ObserveRender=null;
            Check(!leased,
                "native renderer property overrides restore per-object rendering instead of being discarded by grouping");
            originals[0].SetPropertyBlock(null);
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(originals[0]);
            Check(!ScenarioEnvironmentBudget.HasPreparedRoomFloorGroup(originals[1]),
                "native writer synchronously revokes every member of the affected floor group");
            ScenarioEnvironmentBudget.RoomFloorMeshReady(originals[0]);
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length==1,
                "a newly settled terrain endpoint event reconstructs groups without hierarchy-wide steady polling");
            ScenarioEnvironmentBudget.BeforeNativeContentChange();
            GameObject clone=UnityEngine.Object.Instantiate(room.Generated);
            try {Check(clone.GetComponentsInChildren<MeshRenderer>(true).Length==8
                && Array.TrueForAll(clone.GetComponentsInChildren<MeshRenderer>(true),r=>!r.forceRenderingOff),
                "native room cloning copies only original floor renderers with restored masks");}
            finally {UnityEngine.Object.DestroyImmediate(clone);}
            enabled=false;Tick();room.Render();
            Check(!ScenarioEnvironmentBudget.HasPreparedRoomFloorGroup(originals[0])
                && originals.TrueForAll(r=>!r.forceRenderingOff),
                "room floor grouping Off immediately restores individual native floor draws");
        }
        finally
        {
            room.ObserveRender=null;
            ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(()=>false,_=>false,
                (Renderer renderer,out Mesh mesh)=>{mesh=null!;return false;});
            ScenarioEnvironmentBudget.ConfigureTerrainIntegration(_=>{},_=>{},_=>{},()=>{},_=>false);
        }
    }

    private static void RoomFloorBoardMotion()
    {
        using var room = new Room();
        Mesh native = room.Mesh();
        var first = room.Surface("VerifiedMotionFloorOne", x: 1f);
        var second = room.Surface("VerifiedMotionFloorTwo", x: 2f);
        first.GetComponent<MeshFilter>().sharedMesh = native;
        second.GetComponent<MeshFilter>().sharedMesh = native;
        bool enabled = true;
        ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(() => enabled, mesh => mesh == native,
            (Renderer renderer, out Mesh mesh) => { mesh = native; return true; });
        try
        {
            ScenarioEnvironmentBudget.Placed(room.Generated);
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(room.Chunks().Length == 1, "board motion fixture prepares one private room-floor chunk");
            MeshRenderer chunk = room.Chunks()[0];
            Mesh combined = chunk.GetComponent<MeshFilter>().sharedMesh;
            Check(chunk.transform.IsChildOf(room.Host.transform) && !chunk.transform.IsChildOf(room.Tile.transform),
                "moving room-floor chunk remains outside native tile and scenario clone roots");
            void Lease(string step)
            {
                bool leased = false;
                room.Camera.transform.position = room.Tile.transform.TransformPoint(new Vector3(1.5f, 8f, 0f));
                room.Camera.transform.rotation = room.Tile.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
                room.Camera.orthographicSize = 3f * room.Tile.transform.lossyScale.x;
                long sourcesBefore = PerfMonitor.Counts.TryGetValue("Environment.RoomFloorSources", out long sourceCount) ? sourceCount : 0;
                long groupsBefore = PerfMonitor.Counts.TryGetValue("Environment.RoomFloorGroups", out long groupCount) ? groupCount : 0;
                room.ObserveRender = () => leased = first.forceRenderingOff && second.forceRenderingOff && chunk.enabled;
                Color32[] actual = room.Render(); room.ObserveRender = null;
                long sources = PerfMonitor.Counts["Environment.RoomFloorSources"] - sourcesBefore;
                long groups = PerfMonitor.Counts["Environment.RoomFloorGroups"] - groupsBefore;
                UnityEngine.Debug.Log("Room floor board motion " + step + ": leased=" + leased
                    + ", cameraSources=" + sources + ", cameraGroups=" + groups);
                Check(leased && sources == 2 && groups == 1,
                    "private room-floor chunk retains both camera leases after native board pose change: " + step);
                Check(chunk.GetComponent<MeshFilter>().sharedMesh == combined && room.Chunks().Length == 1,
                    "native board pose changes reuse the prepared private mesh without rebuilding geometry: " + step);
                enabled = false; Color32[] original = room.Render(); enabled = true;
                int visible = 0;
                for (int i = 0; i < actual.Length; i++)
                {
                    Check(actual[i].Equals(original[i]), "moved private floor pixels match current native board geometry: " + step);
                    if (actual[i].r != 0 || actual[i].g != 0 || actual[i].b != 0) visible++;
                }
                Check(visible > 10, "moved floor pose comparison contains actual visible pixels: " + step);
            }
            Lease("initial");
            room.Tile.transform.localPosition = new Vector3(.3f, .2f, -.4f); Lease("tile translation");
            room.Tile.transform.localRotation = Quaternion.Euler(11f, 23f, 7f); Lease("tile rotation");
            room.Tile.transform.localScale = Vector3.one * .72f; Lease("tile uniform scale");
            room.Root.transform.position = new Vector3(.6f, -.2f, .3f);
            room.Root.transform.rotation = Quaternion.Euler(8f, -31f, 12f);
            room.Root.transform.localScale = Vector3.one * 1.3f; Lease("scenario recenter and uniform scale");
            Vector3 sourcePosition = first.transform.localPosition;
            first.transform.localPosition += new Vector3(.15f, 0f, 0f);
            bool nativeDraw = false;
            room.ObserveRender = () => nativeDraw = !first.forceRenderingOff && !second.forceRenderingOff && !chunk.enabled;
            room.Render(); room.ObserveRender = null;
            Check(nativeDraw, "individual floor movement revokes the following chunk instead of moving stale combined geometry");
            first.transform.localPosition = sourcePosition; Lease("source restored");
            void NativeFallback(string reason)
            {
                nativeDraw = false;
                long sourcesBefore = PerfMonitor.Counts["Environment.RoomFloorSources"];
                room.ObserveRender = () => nativeDraw = !first.forceRenderingOff && !second.forceRenderingOff && !chunk.enabled;
                room.Render(); room.ObserveRender = null;
                Check(nativeDraw && PerfMonitor.Counts["Environment.RoomFloorSources"] == sourcesBefore,
                    "following floor chunk preserves native fallback for unsupported board or source state: " + reason);
            }
            room.Tile.transform.localScale = new Vector3(1.4f, 1f, 1f); NativeFallback("nonuniform tile scale");
            room.Tile.transform.localScale = new Vector3(-1f, 1f, 1f); NativeFallback("negative tile scale");
            room.Tile.transform.localScale = new Vector3(-1f, -1f, 1f); NativeFallback("two negative tile axes");
            room.Tile.transform.localScale = Vector3.one * .72f;
            room.Root.transform.localScale = new Vector3(1f, 1.3f, .8f); NativeFallback("sheared rotated ancestry");
            room.Root.transform.localScale = Vector3.one * 1.3f; Lease("supported board pose restored");
            room.Host.transform.position = new Vector3(.2f, -.1f, .4f);
            room.Host.transform.rotation = Quaternion.Euler(-7f, 18f, 4f);
            room.Host.transform.localScale = Vector3.one * .8f; Lease("private host moved independently");
            room.Host.transform.localScale = new Vector3(-1f, -1f, 1f); NativeFallback("negative private host ancestry");
            room.Host.transform.localScale = Vector3.one * .8f;
            var effect = new MaterialPropertyBlock(); effect.SetColor("_Tint", Color.green);
            first.SetPropertyBlock(effect); NativeFallback("native source property override"); first.SetPropertyBlock(null);
            second.sharedMaterial = room.Material(); NativeFallback("native source material replacement"); second.sharedMaterial = room.Original;
            second.enabled = false; NativeFallback("native source visibility"); second.enabled = true;
            Lease("native source states restored");
            ScenarioEnvironmentBudget.BeforeNativeContentChange();
            GameObject clone = UnityEngine.Object.Instantiate(room.Root);
            try
            {
                Check(clone.GetComponentsInChildren<MeshRenderer>(true).Length == 2
                    && Array.TrueForAll(clone.GetComponentsInChildren<MeshRenderer>(true), r => !r.forceRenderingOff),
                    "native scenario cloning after board motion contains only original unmasked floor renderers");
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }

            bool finalLease = false, finalPose = false;
            Camera.CameraCallback finalObserve = camera =>
            {
                if (camera != room.Camera) return;
                finalLease = first.forceRenderingOff && second.forceRenderingOff && chunk.enabled;
                finalPose = (chunk.transform.position - room.Tile.transform.position).sqrMagnitude < 1e-9f
                    && Quaternion.Angle(chunk.transform.rotation, room.Tile.transform.rotation) < .01f;
            };
            room.ObserveRender = () => room.Tile.transform.localPosition += new Vector3(.17f, .05f, -.08f);
            Camera.onPreRender += finalObserve;
            Color32[] latePixels;
            try { latePixels = room.Render(); }
            finally { Camera.onPreRender -= finalObserve; room.ObserveRender = null; }
            Check(finalLease && finalPose && chunk.GetComponent<MeshFilter>().sharedMesh == combined,
                "late native board movement updates the private floor pose before actual camera culling");
            enabled = false; Color32[] lateOriginal = room.Render(); enabled = true;
            for (int i = 0; i < latePixels.Length; i++)
                Check(latePixels[i].Equals(lateOriginal[i]), "late moved private floor pixels match current native board geometry");
            room.ObserveRender = () => room.Tile.transform.localScale = new Vector3(1.2f, 1f, 1f);
            bool lateNative = false;
            Camera.CameraCallback lateObserve = camera =>
            {
                if (camera == room.Camera) lateNative = !first.forceRenderingOff && !second.forceRenderingOff && !chunk.enabled;
            };
            long lateSourcesBefore = PerfMonitor.Counts["Environment.RoomFloorSources"];
            Camera.onPreRender += lateObserve;
            try { room.Render(); }
            finally { Camera.onPreRender -= lateObserve; room.ObserveRender = null; }
            Check(lateNative && PerfMonitor.Counts["Environment.RoomFloorSources"] == lateSourcesBefore,
                "late unsupported board pose restores original floor renderers before native culling");
            room.Tile.transform.localScale = Vector3.one * .72f; Lease("late board pose restored");
            room.ObserveRender = () =>
            {
                ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);
                first.transform.localPosition += new Vector3(.15f, 0f, 0f);
            };
            lateNative = false; Camera.onPreRender += lateObserve;
            try { room.Render(); }
            finally { Camera.onPreRender -= lateObserve; room.ObserveRender = null; }
            Check(lateNative && !ScenarioEnvironmentBudget.HasPreparedRoomFloorGroup(second),
                "late native source writer retires the following group before publishing independent geometry");
        }
        finally
        {
            room.ObserveRender = null;
            ScenarioEnvironmentBudget.ConfigureRoomFloorGrouping(() => false, _ => false,
                (Renderer renderer, out Mesh mesh) => { mesh = null!; return false; });
        }
    }

}
