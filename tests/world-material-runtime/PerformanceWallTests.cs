using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using Object=UnityEngine.Object;

public static partial class WorldMaterialProgram
{
    // The policy membership delegate is an explicit boundary here. The separate wall
    // lane executes the complete real policy; this lane executes the entire World owner.
    private static void PerformanceWalls(GameObject host,GameObject room,MeshRenderer wall,Material first,Material second,Camera camera)
    {
        var floor=Source("Unhidden native floor",room.transform,wall.GetComponent<MeshFilter>().sharedMesh,first);
        floor.transform.localPosition=new Vector3(3,0,0);
        var hidden=new HashSet<Renderer>();int notices=0;
        WorldMaterialBudget.ConfigurePerformanceWallVisibility(hidden.Contains);
        WorldMaterialBudget.ConfigureSourceChanged(_=>notices++);
        try
        {
            WorldMaterialBudget.BeforeNativeRendererWrite(wall);wall.sharedMaterials=new[]{first,second};wall.forceRenderingOff=false;
            WorldMaterialBudget.MaterialReady(wall);WorldMaterialBudget.MaterialReady(floor);Tick(host,16);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(wall.sharedMaterials[0])&&WorldMaterialBudget.IsOwnedVariant(floor.sharedMaterial),"live wall and floor enter actual world material routes before quality compromise");
            hidden.Add(wall);wall.forceRenderingOff=true;Center(camera);
            Check(wall.sharedMaterials[0]==first&&wall.sharedMaterials[1]==second&&wall.forceRenderingOff,"actual hidden world transition restores native slots without clearing wall-owned flag");
            // Observe only this exact source; unrelated original fixture sources remain
            // active. A native writer may briefly clear its flag before the ready callback,
            // but ownership still must skip every current per-eye read in the World path.
            NativeWriteObserver.TrackedRenderer=wall;
            int material=NativeWriteObserver.TrackedMaterials,mesh=NativeWriteObserver.TrackedMeshes,wide=NativeWriteObserver.TrackedWideBlocks,index=NativeWriteObserver.TrackedSlotBlocks,notify=notices;
            wall.forceRenderingOff=false;
            for(int eye=0;eye<4;eye++)Center(camera);
            wall.forceRenderingOff=true;
            Check(NativeWriteObserver.TrackedMaterials==material&&NativeWriteObserver.TrackedMeshes==mesh&&NativeWriteObserver.TrackedWideBlocks==wide&&NativeWriteObserver.TrackedSlotBlocks==index,"settled performance-hidden world sources make zero per-eye mesh material and MPB reads");
            Check(notices==notify,"settled hidden source notifies geometry consumers once rather than every eye");
            first.SetColor("_Tint",Color.green);floor.gameObject.SetActive(true);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(floor.sharedMaterial)&&floor.sharedMaterial.GetColor("_Tint")==Color.green,"unhidden floor remains on live world validation while wall source is skipped");
            WorldMaterialBudget.BeforeNativeContentChange();var clone=Object.Instantiate(wall.gameObject);
            Check(clone.GetComponent<MeshRenderer>().sharedMaterials[0]==first&&!clone.GetComponent<MeshRenderer>().forceRenderingOff,"actual native clone inherits original slots and no wall force-off flag");Object.DestroyImmediate(clone);
            hidden.Clear();wall.forceRenderingOff=false;Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(wall.sharedMaterials[0])&&wall.sharedMaterial.GetColor("_Tint")==Color.green,"Regular rechecks current native mutation and restores simplified live wall route");
        }
        finally{NativeWriteObserver.TrackedRenderer=null;hidden.Clear();wall.forceRenderingOff=false;WorldMaterialBudget.ConfigurePerformanceWallVisibility(_=>false);WorldMaterialBudget.ConfigureSourceChanged(_=>{});Object.DestroyImmediate(floor.gameObject);first.SetColor("_Tint",Color.red);}
    }
}
