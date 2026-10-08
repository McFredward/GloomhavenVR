using System;
using GloomhavenVR.Core;
using UnityEngine;
using Object=UnityEngine.Object;

public static partial class WorldMaterialProgram
{
    private static void EmptyBlockParity(MeshRenderer source,Material first,Material second,Camera camera)
    {
        Color tint=first.GetColor("_Tint");
        var block=new MaterialPropertyBlock();
        for(int populated=0;populated<4;populated++)
        {
            source.SetPropertyBlock(null);source.SetPropertyBlock(null,0);source.SetPropertyBlock(null,1);
            source.sharedMaterials=new[]{first,second};first.SetColor("_Tint",Color.red);
            if((populated&1)!=0){block.Clear();block.SetColor("_Tint",Color.blue);source.SetPropertyBlock(block);}
            if((populated&2)!=0){block.Clear();block.SetColor("_Tint",Color.green);source.SetPropertyBlock(block,0);source.SetPropertyBlock(block,1);}
            Color shown=Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[0])&&WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[1]),
                "all empty/nonempty native block combinations retain safe independent slots: populated="+populated);
            Check((populated&2)!=0?shown.g>.7f:(populated&1)!=0?shown.b>.7f:shown.r>.7f,
                "actual pixels preserve indexed-over-renderer tint precedence across empty block transitions: populated="+populated);
            block.Clear();block.SetFloat("_AddVertexAnim",1f);source.SetPropertyBlock(block,1);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[0])&&source.sharedMaterials[1]==second,
                "fresh indexed-only animated scalar revokes its slot despite earlier empty read: populated="+populated);
            block.Clear();block.SetFloat("_AddVertexAnim",0f);source.SetPropertyBlock(block,1);Center(camera);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterials[1]),
                "present zero indexed scalar differs from an empty block and regains safe ownership: populated="+populated);
            block.SetFloat("_AddVertexAnim",1f);source.SetPropertyBlock(block);Center(camera);
            // An indexed block supplies the whole slot block: both levels still
            // matter to safety, even when a zero indexed value could mask a scalar.
            Check(source.sharedMaterials[0]==first&&source.sharedMaterials[1]==second,
                "fresh renderer-wide animated scalar revokes every slot with independently populated levels: populated="+populated);
        }
        source.SetPropertyBlock(null);source.SetPropertyBlock(null,0);source.SetPropertyBlock(null,1);
        Material standard=new(Shader.Find("Standard"));standard.SetFloat("_Mode",0f);standard.SetFloat("_SrcBlend",1f);
        standard.SetFloat("_DstBlend",0f);standard.SetFloat("_ZWrite",1f);standard.SetColor("_EmissionColor",Color.black);standard.shaderKeywords=Array.Empty<string>();
        source.sharedMaterials=new[]{standard};Center(camera);
        foreach(bool indexed in new[]{false,true})
        {
            block.Clear();block.SetFloat("_SrcBlend",0f);
            if(indexed)source.SetPropertyBlock(block,0);else source.SetPropertyBlock(block);
            Center(camera);Check(source.sharedMaterial==standard,
                "present Standard zero blend override retains native route at its actual block level: indexed="+indexed);
            block.SetFloat("_SrcBlend",1f);block.SetFloat("_DstBlend",0f);
            if(indexed)source.SetPropertyBlock(block,0);else source.SetPropertyBlock(block);
            Center(camera);Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "present Standard native blend endpoints regain safe route without treating zero as absent: indexed="+indexed);
            source.SetPropertyBlock(null);source.SetPropertyBlock(null,0);
        }
        source.sharedMaterials=new[]{first,second};first.SetColor("_Tint",tint);Center(camera);Object.DestroyImmediate(standard);
    }
    private static void CurrentOwnerParity(GameObject room,MeshRenderer source,Material first,Material second,Camera camera)
    {
        string name=source.gameObject.name;
        source.gameObject.name="Preview";Center(camera);
        Check(source.sharedMaterial==first,"same native Transform renamed to Preview immediately restores original material ownership");
        source.gameObject.name=name;Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"same native Transform renamed from Preview regains current eligibility");
        GameObject generated=room.transform.parent.gameObject;string generatedName=generated.name;
        generated.name="Native content without generation provenance";Center(camera);
        Check(source.sharedMaterial==first,"current Generated Content name removal does not reuse earlier positive ancestry");
        generated.name=generatedName;Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),"current Generated Content name restoration restores positive native ancestry");
        var replacement=Source("Replacement renderer identity",room.transform,source.GetComponent<MeshFilter>().sharedMesh,first);
        WorldMaterialBudget.MaterialReady(replacement);Center(camera);GameObject replacementObject=replacement.gameObject;Object.DestroyImmediate(replacement);
        replacement=replacementObject.AddComponent<MeshRenderer>();
        replacement.sharedMaterials=new[]{first,second};WorldMaterialBudget.MaterialReady(replacement);Center(camera);
        Check(WorldMaterialBudget.IsOwnedVariant(replacement.sharedMaterial)&&source.enabled,
            "replacement native Renderer owns its current component GameObject and Transform independently of dead candidates");
        Object.DestroyImmediate(replacementObject);Center(camera);
    }
}
