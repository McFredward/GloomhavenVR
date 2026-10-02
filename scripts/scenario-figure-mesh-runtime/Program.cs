using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GloomhavenVR.Core;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;

public static class InteractionProgram
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    private static int Triangles(Mesh mesh) { int n = 0; for (int i = 0; i < mesh.subMeshCount; i++) n += (int)mesh.GetIndexCount(i) / 3; return n; }
    private static Mesh Surface()
    {
        const int columns = 20;
        var mesh = new Mesh { name = "Generated geometry invariant fixture" };
        var vertices = new Vector3[(columns + 1) * (columns + 1)];
        var uv = new Vector2[vertices.Length]; var normals = new Vector3[vertices.Length];
        var weights = new BoneWeight[vertices.Length]; var colors = new Color[vertices.Length];
        var tangents = new Vector4[vertices.Length];
        for (int y = 0; y <= columns; y++) for (int x = 0; x <= columns; x++)
        {
            int i = y * (columns + 1) + x;
            vertices[i] = new Vector3(x / 20f - .5f, y / 20f - .5f, Mathf.Sin(x*.2f)*.015f);
            uv[i] = new Vector2(x / 20f, y / 20f); normals[i] = Vector3.forward;
            tangents[i] = new Vector4(1,0,0,-1); colors[i] = new Color(uv[i].x,uv[i].y,.5f,1);
            weights[i] = new BoneWeight { boneIndex0 = x < columns/2 ? 0 : 1, weight0 = 1 };
        }
        var triangles = new List<int>(); var second = new List<int>();
        for (int y = 0; y < columns; y++) for (int x = 0; x < columns; x++)
        { int a = y*(columns+1)+x; (x < columns/2 ? triangles : second).AddRange(new[]{a,a+columns+1,a+1,a+1,a+columns+1,a+columns+2}); }
        mesh.vertices = vertices; mesh.uv = uv; mesh.uv2 = uv; mesh.normals = normals; mesh.tangents = tangents; mesh.colors = colors;
        mesh.boneWeights = weights; mesh.bindposes = new[]{Matrix4x4.identity,Matrix4x4.identity};
        mesh.subMeshCount=2;mesh.SetTriangles(triangles,0);mesh.SetTriangles(second,1);mesh.RecalculateBounds();
        var delta = new Vector3[vertices.Length]; for (int i = 0; i < delta.Length; i++) delta[i] = new Vector3(0,0,uv[i].y*.1f);
        mesh.AddBlendShapeFrame("Native expression",100,delta,new Vector3[vertices.Length],new Vector3[vertices.Length]);
        return mesh;
    }
    private static Dictionary<ulong,int> Edges(Mesh mesh)
    {
        var result = new Dictionary<ulong,int>();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] t = mesh.GetTriangles(sub);
            for (int i = 0; i < t.Length; i += 3) for (int e = 0; e < 3; e++)
            { int a=t[i+e],b=t[i+(e+1)%3]; ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);result.TryGetValue(key,out int count);result[key]=count+1; }
        }
        return result;
    }
    private static int BoundaryEdges(Mesh mesh)
    { int count=0;foreach(var entry in Edges(mesh)) if(entry.Value==1)count++;return count; }
    private static void GeometryProof()
    {
        Mesh original=Surface();Mesh coarse=ScenarioFigureMeshTopology.Simplify(original,.2f,out int[] map);
        Check(Triangles(coarse)<Triangles(original)*.5f&&coarse.vertexCount<original.vertexCount,"actual topology must materially reduce geometry");
        Check(coarse.subMeshCount==original.subMeshCount&&coarse.bounds==original.bounds,"native material slots and conservative bounds remain");
        Check(BoundaryEdges(coarse)==BoundaryEdges(original),"edge collapse must preserve every original open boundary");
        bool manifold=true;foreach(var edge in Edges(coarse))if(edge.Value>2)manifold=false;
        Check(manifold,"edge collapse must never create non-manifold joins");
        Vector3[] before=original.vertices,after=coarse.vertices,normals=coarse.normals;
        BoneWeight[] skin=coarse.boneWeights;Vector2[] uv=coarse.uv;Color[] colors=coarse.colors;Vector4[] tangents=coarse.tangents;
        bool attributes=true;for(int i=0;i<map.Length;i++) attributes &= after[i]==before[map[i]]&&uv[i]==original.uv[map[i]]&&skin[i].Equals(original.boneWeights[map[i]])
            &&colors[i]==original.colors[map[i]]&&normals[i]==original.normals[map[i]]&&tangents[i]==original.tangents[map[i]];
        Check(attributes,"every survivor retains exact original UV bone normal tangent and color attributes");
        Check(coarse.bindposes[0]==original.bindposes[0],"exact bindposes remain native");
        var sourceDelta=new Vector3[original.vertexCount];var coarseDelta=new Vector3[coarse.vertexCount];
        original.GetBlendShapeFrameVertices(0,0,sourceDelta,null,null);coarse.GetBlendShapeFrameVertices(0,0,coarseDelta,null,null);
        bool shape=true;for(int i=0;i<map.Length;i++)shape &= coarseDelta[i]==sourceDelta[map[i]];
        Check(shape&&coarse.blendShapeCount==1&&coarse.GetBlendShapeName(0)=="Native expression","native blendshape frames follow surviving original vertices");
        Check(Triangles(original)==800&&original.vertexCount==441,"original shared source is never mutated");
        UnityEngine.Object.DestroyImmediate(coarse);UnityEngine.Object.DestroyImmediate(original);
        var shells=new Mesh { name="Separate terminal limb shells" };
        shells.vertices=new[]{new Vector3(0,0,0),new Vector3(.01f,0,0),new Vector3(0,.01f,0),new Vector3(0,0,.01f),
            new Vector3(1,0,0),new Vector3(1.01f,0,0),new Vector3(1,.01f,0),new Vector3(1,0,.01f)};
        shells.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3,4,6,5,4,5,7,4,7,6,5,6,7};shells.RecalculateBounds();
        Mesh terminal=ScenarioFigureMeshTopology.Simplify(shells,.1f,out _);
        Check(Triangles(terminal)==8&&terminal.vertexCount==8,"small closed limb shells may never collapse into duplicate zero-volume faces");
        UnityEngine.Object.DestroyImmediate(terminal);UnityEngine.Object.DestroyImmediate(shells);
    }
    private static SkinnedMeshRenderer Skin(GameObject root,Mesh mesh)
    {
        var renderer=root.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;
        var bones=new Transform[mesh.bindposes.Length];
        for(int i=0;i<bones.Length;i++)
        {
            var go=new GameObject("Native bone "+i);go.transform.SetParent(root.transform,false);
            Matrix4x4 inverse=mesh.bindposes[i].inverse;go.transform.localPosition=inverse.GetColumn(3);
            go.transform.localRotation=inverse.rotation;go.transform.localScale=inverse.lossyScale;bones[i]=go.transform;
        }
        renderer.bones=bones;renderer.rootBone=bones.Length>0?bones[0]:null;renderer.localBounds=mesh.bounds;
        renderer.sharedMaterial=new Material(Shader.Find("Unlit/Color")) { color=Color.green };
        return renderer;
    }
    private static bool[] Pixels(Camera camera, string pose)
    {
        var target=new RenderTexture(160,160,24);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(160,160,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,160,160),0,0);image.Apply();
        Color[] pixels=image.GetPixels();var result=new bool[pixels.Length];
        for(int i=0;i<pixels.Length;i++)result[i]=pixels[i].g>.5f&&pixels[i].r<.2f;
        var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"-interactionManifest");
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(args[arg+1])!,typeof(InteractionProgram).Assembly.GetName().Name+"-"+checks+"-"+pose+".png"),image.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);return result;
    }
    [DataContract] private sealed class SourceIndex { [DataMember] public SourceEntry[] meshes=Array.Empty<SourceEntry>(); }
    [DataContract] private sealed class SourceEntry { [DataMember] public string file=""; [DataMember] public string bundle=""; }
    private static void NativeProof()
    {
        var args=Environment.GetCommandLineArgs();string path=args[Array.IndexOf(args,"-figureNativeDir")+1];
        ScenarioFigureMeshBank.Prepare(0);int bodies=0;
        string nativeBundles=args[Array.IndexOf(args,"-figureNativeBundles")+1];
        var assetOwners=new List<AssetBundle>();
        var sourceBundles=new Dictionary<string,string>();
        using(var input=File.OpenRead(Path.Combine(path,"sources.json")))
            foreach(SourceEntry source in ((SourceIndex)new DataContractJsonSerializer(typeof(SourceIndex)).ReadObject(input)).meshes)
                sourceBundles.Add(source.file,source.bundle);
        var bankIndex=(Dictionary<string,string>)typeof(ScenarioFigureMeshBank).GetField("Index",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        Check(bankIndex.Count>1000,"immutable index must deserialize every generated derivative");
        try
        {
        foreach(string file in Directory.GetFiles(path,"*.mesh"))
        {
            Mesh original=NativeFigureMeshStream.Read(file);
            string bundleName=sourceBundles[Path.GetFileName(file)];
            bool sun=bundleName=="npc_sundemon_assets_all.bundle"&&original.name=="MO_NightDemon_Mesh"&&original.vertexCount==7652;
            if(!sun&&original.name!="MO_WindDemon_main"&&original.name!="HE_Berserker_main"&&!(original.name.Contains("Spitting_Drake")&&original.vertexCount==5079))
            { UnityEngine.Object.DestroyImmediate(original);continue; }
            Mesh sourceReference = original;
            var owner=AssetBundle.LoadFromFile(Path.Combine(nativeBundles,bundleName));
            Check(owner!=null,"original licensed native actor bundle must load unchanged in real Unity");
            owner=owner ?? throw new Exception("native bundle missing");assetOwners.Add(owner);original=null!;
            // Native bundles expose prefab containers, not standalone Mesh asset names.
            // Inspect original prefab assets without Instantiate/Awake/gameplay callbacks.
            var candidates=new HashSet<Mesh>(owner.LoadAllAssets<Mesh>());
            foreach(GameObject prefab in owner.LoadAllAssets<GameObject>())
                foreach(SkinnedMeshRenderer nativeSkin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(nativeSkin.sharedMesh!=null)candidates.Add(nativeSkin.sharedMesh);
            foreach(Mesh candidate in candidates)
                if(candidate.name==sourceReference.name&&candidate.vertexCount==sourceReference.vertexCount)
                { Check(original==null,"native mesh fixture identity must be unambiguous");original=candidate; }
            Check(original!=null&&original.bounds==sourceReference.bounds,"actual native mesh metadata matches immutable extractor stream");
            original=original ?? throw new Exception("native mesh missing");
            Check(!sourceReference.name.Contains("Spitting_Drake")||!original.isReadable,"actual native far Drake mesh is unreadable without readback");
            Mesh? reduced=ScenarioFigureMeshBank.Resolve(original,0);
            ScenarioFigureMeshBank.Prepare(original,0);reduced=ScenarioFigureMeshBank.Resolve(original,0);
            Check(reduced!=null&&reduced!=original,"verified native Wind Berserker and unreadable Drake bodies require real derivatives");
            Check(reduced!.vertexCount<original.vertexCount*.85f&&Triangles(reduced)<Triangles(original)*.8f,"native no-LOD and far-LOD reductions must be material");
            Check(reduced.bounds==original.bounds&&reduced.subMeshCount==original.subMeshCount&&reduced.bindposes.Length==original.bindposes.Length,
                  "actual native derivative retains material bounds and binding dimensions");
            // Original seam vertices may share positions, so identity also includes UV and
            // native skin weights. Validate every derivative survivor against complete source
            // channels, independently of the generator's recorded survivor map.
            var positions=new Dictionary<Vector3,List<int>>();
            Vector3[] sourcePositions=sourceReference.vertices,derivedPositions=reduced.vertices;
            Vector2[] sourceUV=sourceReference.uv,derivedUV=reduced.uv;BoneWeight[] sourceWeights=sourceReference.boneWeights,derivedWeights=reduced.boneWeights;
            Vector3[] sourceNormals=sourceReference.normals,derivedNormals=reduced.normals;Vector4[] sourceTangents=sourceReference.tangents,derivedTangents=reduced.tangents;
            for(int i=0;i<sourcePositions.Length;i++)
            { if(!positions.TryGetValue(sourcePositions[i],out List<int> vertices))positions.Add(sourcePositions[i],vertices=new());vertices.Add(i); }
            var remap=new int[derivedPositions.Length];bool exact=true;
            for(int i=0;i<derivedPositions.Length;i++)
            {
                bool found=false;
                if(positions.TryGetValue(derivedPositions[i],out List<int> indices))
                    foreach(int index in indices)
                        if(sourceUV[index]==derivedUV[i]&&sourceWeights[index].Equals(derivedWeights[i])&&sourceNormals[index]==derivedNormals[i]&&sourceTangents[index]==derivedTangents[i])
                        { found=true;remap[i]=index;break; }
                exact &= found;
            }
            Check(exact,"every actual native derivative vertex must retain original UV normal tangent and bone influences");
            bool poses=true;for(int i=0;i<original.bindposes.Length;i++)poses &= original.bindposes[i]==reduced.bindposes[i];
            Check(poses,"all actual native bindposes remain bit-identical");
            var root=new GameObject("Original native actor root");var renderer=Skin(root,original);var collision=root.AddComponent<BoxCollider>();
            var animator=root.AddComponent<Animator>();renderer.quality=SkinQuality.Bone4;
            var record=new ScenarioFigureMeshBank.Record { Renderer=renderer,Original=original };record.Apply(0);
            Check(record.UsesDerivative&&renderer.sharedMesh==reduced&&collision.enabled&&animator.enabled&&renderer.quality==SkinQuality.Bone4,"detail applies solely to native visual mesh slot");
            GameObject ghost=FigureVisualMirror.CloneVisual(root,new Vector3(4,0,0),Quaternion.identity,Vector3.one,out FigureVisualMirror mirror);
            var ghostSkin=ghost.GetComponent<SkinnedMeshRenderer>();
            Check(ghostSkin.sharedMesh==reduced&&ghost.GetComponent<Animator>()==null&&ghost.GetComponent<Collider>()==null,"already-local-held ghost starts with coarse visual without native gameplay");
            record.Apply(100);mirror.Sync();
            Check(renderer.sharedMesh==original&&ghostSkin.sharedMesh==original,"existing local home ghost follows exact original mesh restoration");
            record.Apply(0);mirror.Sync();
            Check(ghostSkin.sharedMesh==reduced,"existing remote home ghost follows re-applied native derivative");
            renderer.bones[Mathf.Min(3,renderer.bones.Length-1)].localRotation*=Quaternion.Euler(0,17,0);mirror.Sync();
            var fullPose=new Mesh();var coarsePose=new Mesh();
            renderer.sharedMesh=original;renderer.BakeMesh(fullPose);renderer.sharedMesh=reduced;renderer.BakeMesh(coarsePose);
            bool deformation=true;Vector3[] fullVertices=fullPose.vertices,coarseVertices=coarsePose.vertices;
            for(int i=0;i<remap.Length;i++)deformation &= (coarseVertices[i]-fullVertices[remap[i]]).sqrMagnitude<1e-8f;
            Check(deformation,"actual native coarse vertices must skin identically under a moving bone pose");
            UnityEngine.Object.DestroyImmediate(fullPose);UnityEngine.Object.DestroyImmediate(coarsePose);
            record.Restore();mirror.Sync();
            Check(!record.UsesDerivative&&renderer.sharedMesh==original&&ghostSkin.sharedMesh==original&&reduced!=null,"restoration never destroys a shared still-referenced bank mesh");
            Mesh foreign=Surface();renderer.sharedMesh=foreign;record.Apply(0);record.Restore();
            Check(!record.UsesDerivative&&renderer.sharedMesh==foreign,"foreign native shared-mesh changes are never overwritten");
            renderer.sharedMesh=original;
            Check(ScenarioFigureMeshBank.Resolve(original,100)==original,"100 percent is exact native original identity");
            // Real graphics proof on the native demon body; both derivatives preserve silhouette.
            if(!original.name.Contains("Spitting_Drake"))
            {
                // Keep the independently checked moving-bone proof above separate from
                // this graphics proof of the original and generated vertex/index buffers.
                // Synthetic bone hierarchy reconstruction is not the native Animator rig.
                renderer.enabled=false;ghostSkin.enabled=false;
                var visual=new GameObject("Native vertex buffer silhouette");var filter=visual.AddComponent<MeshFilter>();
                filter.sharedMesh=sourceReference;var visible=visual.AddComponent<MeshRenderer>();visible.sharedMaterial=renderer.sharedMaterial;
                var camera=new GameObject("Native mesh render proof").AddComponent<Camera>();
                camera.transform.position=sourceReference.bounds.center+new Vector3(0,0,-4);
                camera.orthographic=true;camera.orthographicSize=Mathf.Max(sourceReference.bounds.extents.x,sourceReference.bounds.extents.y)*1.15f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                bool[] full=Pixels(camera,"original-"+original.name);filter.sharedMesh=reduced;bool[] coarse=Pixels(camera,"coarse-"+original.name);
                int union=0,intersection=0;for(int i=0;i<full.Length;i++)
                { if(full[i]||coarse[i])union++;if(full[i]&&coarse[i])intersection++; }
                Check(union>100&&intersection>union*.9f,"original and coarse native body must really render a preserved silhouette: "+intersection+"/"+union);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(visual);
            }
            UnityEngine.Object.DestroyImmediate(ghost);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sourceReference);UnityEngine.Object.DestroyImmediate(foreign);bodies++;
        }
        Check(bodies>=4,"all four independently sourced Wind Sun Berserker and far Drake body cases must execute");
        }
        finally { foreach(AssetBundle owner in assetOwners)owner.Unload(true); }
    }
    public static int Run() { GeometryProof(); NativeProof();return checks; }
}
