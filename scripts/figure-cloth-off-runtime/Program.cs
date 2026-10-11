using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics="";
    private static readonly BindingFlags PrivateStatic=BindingFlags.Static|BindingFlags.NonPublic;
    private static void Check(bool value,string message) { Checks++;if(!value)throw new Exception(message); }
    private static int Cooks => PerfMonitor.Counts.TryGetValue("FigureGrab.ClothCooks",out int count)?count:0;
    private static IDictionary Tracking => (IDictionary)typeof(FigureCloth).GetField("_tracked",PrivateStatic)!.GetValue(null)!;
    private static IDictionary Claims => (IDictionary)typeof(FigureCloth).GetField("_disabledManaged",PrivateStatic)!.GetValue(null)!;
    private static GameObject Node(string name,Transform? parent=null)
    { var go=new GameObject(name);if(parent!=null)go.transform.SetParent(parent,false);return go; }
    private sealed class Figure
    {
        internal GameObject Root=null!;
        internal SkinnedMeshRenderer Renderer=null!,Reference=null!;
        internal Cloth Cloth=null!;
        internal ActorBehaviour Actor=null!;
        internal ClothSkinningCoefficient[] Original=null!;
        internal ClothSphereColliderPair[] Spheres=null!;
        internal CapsuleCollider[] Capsules=null!;
        internal Transform Bone=null!;
    }
    private static Figure Build(string name,int n=13)
    {
        var result=new Figure { Root=Node(name) };
        result.Actor=result.Root.AddComponent<ActorBehaviour>();result.Root.AddComponent<Animator>();
        result.Bone=Node("Native clothing bone",result.Root.transform).transform;
        var body=Node("Original cloak",result.Root.transform);
        var vertices=new Vector3[n*n];var normals=new Vector3[n*n];var weights=new BoneWeight[n*n];
        var triangles=new List<int>();
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            int i=z*n+x;vertices[i]=new Vector3(x/(float)(n-1)*.6f-.3f,0,-z/(float)(n-1)*.6f);
            normals[i]=Vector3.up;weights[i]=new BoneWeight { boneIndex0=0,weight0=1f };
            if(x<n-1&&z<n-1)triangles.AddRange(new[]{i,i+n,i+1,i+1,i+n,i+n+1});
        }
        var mesh=new Mesh { name="Original cloth topology" };
        mesh.vertices=vertices;mesh.normals=normals;mesh.triangles=triangles.ToArray();
        mesh.boneWeights=weights;mesh.bindposes=new[]{Matrix4x4.identity};mesh.RecalculateBounds();
        result.Renderer=body.AddComponent<SkinnedMeshRenderer>();
        result.Renderer.sharedMesh=mesh;result.Renderer.bones=new[]{result.Bone};
        result.Renderer.rootBone=result.Bone;result.Renderer.updateWhenOffscreen=true;
        result.Cloth=body.AddComponent<Cloth>();result.Cloth.useGravity=true;
        result.Cloth.stretchingStiffness=.7f;result.Cloth.damping=.1f;
        result.Original=result.Cloth.coefficients;
        for(int i=0;i<result.Original.Length;i++)
        { float part=(i/n)/(float)(n-1);result.Original[i].maxDistance=.25f*part;result.Original[i].collisionSphereDistance=.003f*part; }
        result.Cloth.coefficients=result.Original;
        var nativeSphere=Node("Native body collision",result.Root.transform).AddComponent<SphereCollider>();
        nativeSphere.radius=.005f;nativeSphere.transform.localPosition=new Vector3(0,0,4);
        result.Spheres=new[]{new ClothSphereColliderPair(nativeSphere)};result.Cloth.sphereColliders=result.Spheres;
        var nativeCapsule=Node("Native limb collision",result.Root.transform).AddComponent<CapsuleCollider>();
        nativeCapsule.transform.localPosition=new Vector3(0,0,4);result.Capsules=new[]{nativeCapsule};
        result.Cloth.capsuleColliders=result.Capsules;
        result.Reference=Node("Original nonsimulating skinned reference",result.Root.transform).AddComponent<SkinnedMeshRenderer>();
        result.Reference.sharedMesh=mesh;result.Reference.bones=result.Renderer.bones;result.Reference.rootBone=result.Bone;
        return result;
    }
    private static bool OriginalCoefficients(Figure figure)
    {
        ClothSkinningCoefficient[] now=figure.Cloth.coefficients;
        for(int i=0;i<now.Length;i++)if(now[i].maxDistance!=figure.Original[i].maxDistance
            ||now[i].collisionSphereDistance!=figure.Original[i].collisionSphereDistance)return false;
        return true;
    }
    private static object Tracked(Figure figure) => Tracking[figure.Root.GetInstanceID()]!;
    private static void Put(object tracked,string field,object value) => tracked.GetType().GetField(field)!.SetValue(tracked,value);
    private static void MidLiveCook(Figure figure)
    {
        FigureCloth.Note(figure.Root,2f);
        object tracked=Tracked(figure);
        var down=(List<bool>)tracked.GetType().GetField("LiveDown")!.GetValue(tracked)!;
        down[0]=true;Put(tracked,"LiveSpheres",figure.Cloth.sphereColliders);
        Put(tracked,"LiveCapsules",figure.Cloth.capsuleColliders);
        var changed=figure.Cloth.coefficients;for(int i=0;i<changed.Length;i++)changed[i].maxDistance+=.1f;
        figure.Cloth.coefficients=changed;figure.Cloth.stretchingStiffness=.3f;figure.Cloth.enabled=false;
    }
    private static void MidSettledCook(Figure figure)
    {
        FigureCloth.Note(figure.Root,2f);object tracked=Tracked(figure);
        Put(tracked,"Cooking",true);Put(tracked,"CookDown",true);Put(tracked,"CookCount",1);
        figure.Cloth.enabled=false;
    }
    private static void SetHand(Figure figure)
    {
        FigureGrabbable.Left=new FigureGrabbable { RootObject=figure.Root };FigureGrabbable.Right=null;
        VRHands.Right=new VRHand();
        VRHands.Right.Rig.PalmCenter=Node("Tracked free palm").transform;
        VRHands.Right.Rig.IndexTip=Node("Tracked free fingertip").transform;
        VRHands.Right.Rig.PalmCenter.position=figure.Root.transform.position+new Vector3(0,-.1f,-.2f);
        VRHands.Right.Rig.IndexTip.position=figure.Root.transform.position+new Vector3(0,-.1f,-.3f);
    }
    private static float MeshDifference(Figure figure)
    {
        var actual=new Mesh();var expected=new Mesh();
        figure.Renderer.BakeMesh(actual);figure.Reference.BakeMesh(expected);
        Vector3[] left=actual.vertices,right=expected.vertices;float worst=0;
        for(int i=0;i<left.Length;i++)worst=Mathf.Max(worst,(left[i]-right[i]).magnitude);
        UnityEngine.Object.DestroyImmediate(actual);UnityEngine.Object.DestroyImmediate(expected);return worst;
    }
    public static IEnumerator Run()
    {
        Checks=0;VRSession.IsRunning=true;
        var cold=Build("Cold disabled local or remote figure");cold.Cloth.enabled=false;
        PerfConfig.FigureClothSimulationEnabled=false;
        int reads=FigureGrabbable.Reads+VRHands.Reads;
        for(int i=0;i<40;i++)
        {
            FigureCloth.Note(cold.Root,1f+i*.04f);
            Check(Tracking.Count==0,"cold OFF skips hierarchy discovery and rescale records");
            FigureCloth.Tick();FigureClothHands.Tick();
        }
        Check(FigureGrabbable.Reads+VRHands.Reads==reads,"cold OFF skips local held and free-hand sampling");
        Check(Cooks==0&&OriginalCoefficients(cold),"cold OFF performs no coefficient uploads or cooks");
        Check(typeof(FigureClothHands).GetField("_probe",PrivateStatic)!.GetValue(null)==null,"cold OFF creates no hand collision probe");

        PerfConfig.FigureClothSimulationEnabled=true;
        var local=Build("Local held figure");SetHand(local);FigureClothHands.Tick();
        Check(local.Cloth.sphereColliders.Length==local.Spheres.Length+1,"ON appends real free-hand capsule beside native body pairs");
        MidLiveCook(local);var remote=Build("Remote held figure");MidSettledCook(remote);
        int before=Cooks;PerfConfig.FigureClothSimulationEnabled=false;
        // The same production Note entry is used by NetFigures.EaseSlot and by a local hold.
        FigureCloth.Note(remote.Root,2.1f);FigureCloth.Tick();
        Check(!local.Cloth.enabled&&!remote.Cloth.enabled,"OFF abandons both live and settled cook without re-enabling solver");
        Check(Cooks==before,"OFF cancels pending cook before any enable or counted cook");
        Check(OriginalCoefficients(local)&&OriginalCoefficients(remote),"OFF restores pristine per-vertex coefficients");
        Check(local.Cloth.stretchingStiffness==.7f,"OFF restores authored stretching stiffness");
        Check(local.Cloth.sphereColliders.Length==local.Spheres.Length
            &&local.Cloth.sphereColliders[0].first==local.Spheres[0].first,"OFF restores native sphere pairs without resurrecting hand probe stash");
        Check(local.Cloth.capsuleColliders.Length==1&&local.Cloth.capsuleColliders[0]==local.Capsules[0],"OFF restores captured native capsule array");
        Check(Tracking.Count==0,"OFF drops stale rest-scale and cooking bookkeeping");
        Check(FigureCloth.TakeDisabledSimulationOwnership(local.Cloth),"OFF passes original simulating claim to native figure budget");
        Check(!FigureCloth.TakeDisabledSimulationOwnership(local.Cloth),"disabled solver ownership is handed over exactly once");
        Check(FigureCloth.TakeDisabledSimulationOwnership(remote.Cloth),"remote down solver keeps its original simulating ownership");
        reads=FigureGrabbable.Reads+VRHands.Reads;
        for(int i=0;i<40;i++) {FigureCloth.Note(local.Root,1f+i*.04f);FigureCloth.Tick();FigureClothHands.Tick();}
        Check(reads==FigureGrabbable.Reads+VRHands.Reads&&Cooks==before,"steady OFF has no hand discovery or rescale CPU for local and remote figures");
        yield return null;
        int probes=0;foreach(var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))if(go.name=="GloomhavenVR.ClothHandProbe")probes++;
        Check(probes==0,"OFF destroys owned hand probe and its invisible colliders");
        Check(local.Root.GetComponent<Animator>().enabled,"cloth OFF retains actor animation and original skeleton");

        PerfConfig.FigureClothSimulationEnabled=true;local.Cloth.enabled=true;
        FigureClothHands.Tick();MidLiveCook(local);PerfConfig.FigureClothSimulationEnabled=false;FigureCloth.Clear();
        Check(!local.Cloth.enabled&&Cooks==before,"Clear during OFF never drains cook with an enable transition");
        PerfConfig.FigureClothSimulationEnabled=true;FigureCloth.Tick();
        Check(local.Cloth.enabled&&Claims.Count==0,"ON restores unclaimed originally enabled solver and clears ownership");
        Check(OriginalCoefficients(local),"ON does not capture an old pinned or rescaled array as pristine");
        MidLiveCook(local);PerfConfig.FigureClothSimulationEnabled=false;FigureCloth.Tick();
        local.Actor.ForceNativePosition(2);PerfConfig.FigureClothSimulationEnabled=true;FigureCloth.Tick();
        Check(!local.Cloth.enabled,"ON never cancels native two-frame forced-position reset");
        local.Actor.ForceNativePosition(0);local.Cloth.enabled=true; // Native reset owns this completion.
        MidLiveCook(local);int old=Cooks;FigureCloth.Clear();
        Check(local.Cloth.enabled&&Cooks==old+1,"ordinary ON teardown still drains its existing live cook");
        FigureClothHands.Clear();
        var destroyed=Build("Destroyed claim owner");MidLiveCook(destroyed);
        PerfConfig.FigureClothSimulationEnabled=false;FigureCloth.Tick();
        UnityEngine.Object.DestroyImmediate(destroyed.Root);FigureCloth.Clear();
        Check(Claims.Count==0,"scene teardown prunes destroyed cloth ownership rather than leaking old actors");
        UnityEngine.Object.DestroyImmediate(cold.Root);UnityEngine.Object.DestroyImmediate(local.Root);UnityEngine.Object.DestroyImmediate(remote.Root);

        PerfConfig.FigureClothSimulationEnabled=true;
        var delayed=Build("Budget-owned delayed ON solver");MidLiveCook(delayed);
        PerfConfig.FigureClothSimulationEnabled=false;FigureCloth.Tick();
        Check(FigureCloth.TakeDisabledSimulationOwnership(delayed.Cloth),"native budget adopts delayed ON ownership");
        PerfConfig.FigureClothSimulationEnabled=true;FigureCloth.Note(delayed.Root,2f);
        object pending=Tracked(delayed);
        Check(((ICollection)pending.GetType().GetField("Cloths")!.GetValue(pending)!).Count==0,
            "ON before native budget LateUpdate does not capture disabled solver as simulating");
        delayed.Cloth.enabled=true; // Boundary: the budget's LateUpdate restores its owned solver.
        yield return null;FigureCloth.Note(delayed.Root,2f);
        Check(((ICollection)pending.GetType().GetField("Cloths")!.GetValue(pending)!).Count==1,
            "ON late handover retries unchanged held factor and captures the original solver");
        for(int i=0;i<22;i++) {FigureCloth.Note(delayed.Root,2f);FigureCloth.Tick();yield return null;}
        Check(delayed.Cloth.coefficients[13].maxDistance==delayed.Original[13].maxDistance*2f,
            "ON held-scale handover restores authored coefficients at actual held scale");
        FigureCloth.Clear();UnityEngine.Object.DestroyImmediate(delayed.Root);

        // Actual dynamic cloth positive control and OFF movement/probe negative control.
        // A pure skinned clone shares the exact original mesh and bones but has no Cloth.
        PerfConfig.FigureClothSimulationEnabled=true;
        var physical=Build("Physical sheet");SetHand(physical);
        physical.Cloth.externalAcceleration=new Vector3(.3f,-2f,0);
        for(int i=0;i<150;i++)yield return null;
        float on=MeshDifference(physical);
        Check(on>.002f,"real Unity ON cloth physically deforms away from pure skinned reference");
        FigureCloth.Note(physical.Root,1.2f);FigureClothHands.Tick();
        Check(physical.Cloth.sphereColliders.Length==2,"physical ON control actually attaches original production hand probe");
        PerfConfig.FigureClothSimulationEnabled=false;FigureCloth.Tick();FigureClothHands.Tick();
        float off=0;before=Cooks;
        for(int i=0;i<50;i++)
        {
            physical.Root.transform.position=new Vector3(Mathf.Sin(i*.2f),Mathf.Cos(i*.17f),0);
            physical.Root.transform.rotation=Quaternion.Euler(i*3,i*5,i*2);
            physical.Root.transform.localScale=Vector3.one*(.8f+i*.04f);
            physical.Bone.localRotation=Quaternion.Euler(0,i*.4f,0); // skinning is still native and allowed
            VRHands.Right!.Rig.PalmCenter.position=physical.Root.transform.position;
            VRHands.Right.Rig.IndexTip.position=physical.Root.transform.position+Vector3.down*.08f;
            FigureCloth.Note(physical.Root,.8f+i*.04f);FigureCloth.Tick();FigureClothHands.Tick();
            yield return null;off=Mathf.Max(off,MeshDifference(physical));
        }
        Check(off<.00001f,"real Unity OFF mesh has zero secondary deformation through figure bone scale and hand motion");
        Check(Cooks==before&&Tracking.Count==0&&!physical.Cloth.enabled,"physical OFF movement has no rescale cooks or revived PhysX solver");
        physical.Root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        physical.Root.transform.localScale=Vector3.one;physical.Bone.localRotation=Quaternion.identity;
        PerfConfig.FigureClothSimulationEnabled=true;FigureCloth.Tick();FigureClothHands.Tick();
        for(int i=0;i<150;i++)yield return null;
        float restored=MeshDifference(physical);
        Check(physical.Cloth.enabled&&restored>.002f,"real Unity ON again restores original cloth movement after OFF");
        Metrics=$"ON deformation {on:F6} m; OFF worst {off:F8} m through 50 moving frames; restored ON {restored:F6} m";
        FigureCloth.Clear();FigureClothHands.Clear();UnityEngine.Object.DestroyImmediate(physical.Root);
    }
}
