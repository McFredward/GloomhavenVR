using System;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceDecor { internal static Transform? TempleBookRoot; }
}
internal static class BookInkProof
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        var frame=new GameObject("Original station");
        frame.transform.SetPositionAndRotation(new Vector3(3f,-1f,2f),Quaternion.Euler(0f,51f,0f));
        frame.transform.localScale=Vector3.one*.72f;
        var book=new GameObject("Native book",typeof(MeshFilter),typeof(MeshRenderer));
        book.transform.SetParent(frame.transform,false);
        var mesh=new Mesh();
        // Two deliberately different page slopes, with real mesh triangles and a spine.
        mesh.vertices=new[]{new Vector3(-.49f,.99f,-.29f),new Vector3(-.49f,.99f,.04f),
            new Vector3(-.33f,1.025f,-.29f),new Vector3(-.33f,1.025f,.04f),
            new Vector3(-.17f,.995f,-.29f),new Vector3(-.17f,.995f,.04f)};
        mesh.triangles=new[]{0,1,2,1,3,2,2,3,4,3,5,4};mesh.RecalculateBounds();
        book.GetComponent<MeshFilter>().sharedMesh=mesh;TownServiceDecor.TempleBookRoot=book.transform;
        var visitor=new GameObject("Independent visitor workspace");
        visitor.transform.SetPositionAndRotation(new Vector3(-2f,.3f,-4f),Quaternion.Euler(0f,-78f,0f));
        visitor.transform.localScale=Vector3.one*.72f;
        var ritual=new GameObject("Ritual");ritual.transform.SetParent(visitor.transform,false);
        ritual.transform.localPosition=TownServiceRitualLayout.Origin;
        foreach(string key in new[]{"temple.level","temple.gold","temple.progress","temple.description"})
        {
            var go=new GameObject(key,typeof(RectTransform));go.transform.SetParent(ritual.transform,false);
            var text=go.AddComponent<TextMeshProUGUI>();text.text="Original localized temple text with native numbers 40/100";
            var ink=new TownServiceBookInk(key,ritual.transform);ink.Apply(go.transform);
            Check(go.activeSelf,"real readable page supports native ink");
            Vector3 p=visitor.transform.InverseTransformPoint(go.transform.position);
            float y=p.x<-.33f?.99f+(p.x+.49f)*(.035f/.16f):1.025f-(p.x+.33f)*(.03f/.16f);
            Check(Mathf.Abs(p.y-y)<.001f,"ink is attached within one millimetre of the actual original page");
            Check(text.enableWordWrapping&&text.enableAutoSizing,"native description wraps on its own page");
            Check(text.color.r<.2f&&text.color.g<.1f,"book text is printed dark ink rather than white floating UI");
            Check(text.text.Contains("40/100"),"original native localized contents remain unchanged");
            Check(!text.raycastTarget&&go.GetComponentsInChildren<Collider>().Length==0,"book inscriptions create no invisible laser blockers");
            Object.DestroyImmediate(go);
        }
        Object.DestroyImmediate(frame);Object.DestroyImmediate(visitor);Object.DestroyImmediate(mesh);
        TownServiceDecor.TempleBookRoot=null;
        return checks;
    }
}

internal static class NativeBookProof
{
    internal static int Run()
    {
        int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-nativeBookObj");
        Check(at>=0,"actual original book mesh is required");
        var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
        foreach(string line in System.IO.File.ReadLines(args[at+1]))
        {
            string[] fields=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            if(fields.Length==0)continue;
            if(fields[0]=="v")
            {
                float N(int n)=>float.Parse(fields[n],System.Globalization.CultureInfo.InvariantCulture);
                vertices.Add(new Vector3(-N(1),N(2),N(3))); // UnityPy OBJ export mirrors X.
            }
            if(fields[0]=="f")for(int i=2;i+1<fields.Length;i++)
            {triangles.Add(int.Parse(fields[1].Split('/')[0])-1);triangles.Add(int.Parse(fields[i+1].Split('/')[0])-1);triangles.Add(int.Parse(fields[i].Split('/')[0])-1);}
        }
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();
        var frame=new GameObject("Actual original book frame");
        frame.transform.SetPositionAndRotation(new Vector3(2f,.2f,-3f),Quaternion.Euler(0,33,0));frame.transform.localScale=Vector3.one*.68f;
        var book=new GameObject("Original normalized book");book.transform.SetParent(frame.transform,false);
        var body=new GameObject("CR_ST_Shelf_Book_07",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));body.transform.SetParent(book.transform,false);
        body.transform.localRotation=Quaternion.Euler(-90f,0,0);body.GetComponent<MeshFilter>().sharedMesh=mesh;body.GetComponent<MeshCollider>().sharedMesh=mesh;
        var bounds=new Bounds(body.transform.localRotation*vertices[0],Vector3.zero);
        foreach(Vector3 vertex in vertices)bounds.Encapsulate(body.transform.localRotation*vertex);
        float factor=.30f/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        book.transform.localScale=Vector3.one*factor;
        book.transform.localPosition=new Vector3(-.33f,.957f,-.12f)-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*factor;
        TownServiceDecor.TempleBookRoot=book.transform;
        var visitor=new GameObject("Second visitor workspace");visitor.transform.SetPositionAndRotation(new Vector3(-7f,.1f,4f),Quaternion.Euler(0,-83,0));visitor.transform.localScale=Vector3.one*.68f;
        var ritual=new GameObject("Ritual");ritual.transform.SetParent(visitor.transform,false);ritual.transform.localPosition=TownServiceRitualLayout.Origin;
        bool previousBackface=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;Physics.SyncTransforms();
        foreach(string key in new[]{"temple.level","temple.gold","temple.progress","temple.description"})
        {
            var go=new GameObject(key,typeof(RectTransform));go.transform.SetParent(ritual.transform,false);var text=go.AddComponent<TextMeshProUGUI>();
            text.text="Original localized donation content";var ink=new TownServiceBookInk(key,ritual.transform);ink.Apply(go.transform);
            Check(go.activeSelf,"all ink blocks fit real shipped original book pages");
            // Exercise the actual TMP deformation callback with a glyph quad covering its block.
            TMP_TextInfo info=new TMP_TextInfo();info.characterCount=1;
            info.characterInfo=new[]{new TMP_CharacterInfo{isVisible=true,vertexIndex=0,materialReferenceIndex=0}};
            Vector2 size=text.rectTransform.rect.size*.45f;
            info.meshInfo=new[]{new TMP_MeshInfo{vertices=new[]{new Vector3(-size.x,-size.y,0),new Vector3(-size.x,size.y,0),new Vector3(size.x,size.y,0),new Vector3(size.x,-size.y,0)}}};
            typeof(TownServiceBookInk).GetMethod("ProjectVertices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(ink,new object[]{info});
            foreach(Vector3 vertex in info.meshInfo[0].vertices)
            {
                Vector3 local=visitor.transform.InverseTransformPoint(text.transform.TransformPoint(vertex));
                Vector3 original=frame.transform.TransformPoint(local);
                Check(body.GetComponent<MeshCollider>().Raycast(new Ray(original+frame.transform.up*.1f,-frame.transform.up),out RaycastHit hit,.2f),"ink glyph corners stay within actual original page geometry");
                Check(Vector3.Distance(original,hit.point)<.0015f,"glyph vertices follow curved original pages within 1.5 millimetres");
            }
            Vector3[] projected=(Vector3[])info.meshInfo[0].vertices.Clone();
            info.meshInfo[0].vertices=new[]{new Vector3(-size.x,-size.y,0),new Vector3(-size.x,size.y,0),new Vector3(size.x,size.y,0),new Vector3(size.x,-size.y,0)};
            Vector3 position=go.transform.position;Quaternion rotation=go.transform.rotation;Vector3 scale=go.transform.lossyScale;
            TownServiceBookInk.ApplyRemote(key+"|$",go.transform,frame.transform);
            Check(Vector3.Distance(go.transform.position,position)<1e-6f&&Quaternion.Angle(go.transform.rotation,rotation)<.001f&&Vector3.Distance(go.transform.lossyScale,scale)<1e-6f,
                "remote ink preserves authored second-visitor pose instead of using viewer's NPC position");
            var remote=(System.Runtime.CompilerServices.ConditionalWeakTable<Transform,TownServiceBookInk>)typeof(TownServiceBookInk).GetField("Remote",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            Check(remote.TryGetValue(go.transform,out TownServiceBookInk remoteInk),"remote native-part key installs the shared page projection");
            typeof(TownServiceBookInk).GetMethod("ProjectVertices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(remoteInk,new object[]{info});
            for(int i=0;i<4;i++)Check(Vector3.Distance(projected[i],info.meshInfo[0].vertices[i])<.025f,
                "owner and remote glyph deformation match across different workspace and map frames");
            Object.DestroyImmediate(go);
        }
        Physics.queriesHitBackfaces=previousBackface;Object.DestroyImmediate(frame);Object.DestroyImmediate(visitor);Object.DestroyImmediate(mesh);TownServiceDecor.TempleBookRoot=null;
        return checks;
    }
}

internal static class SharedBowlProof
{
    internal static int Run()
    {
        int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
        var priest=new GameObject("Actual shared priest");
        priest.transform.SetPositionAndRotation(new Vector3(2f,.4f,-3f),Quaternion.Euler(0,72,0));
        priest.transform.localScale=Vector3.one*.7f;
        var workspace1=new GameObject("Owner one workspace");var workspace2=new GameObject("Relocated owner two workspace");
        workspace1.transform.position=new Vector3(-8f,0,1f);workspace2.transform.position=new Vector3(7f,0,4f);
        Transform bowl1=TownServiceTempleBowl.Create(priest.transform),bowl2=TownServiceTempleBowl.Create(priest.transform);
        Vector3 actual=priest.transform.TransformPoint(TownServiceRitualLayout.Origin+TownServiceTempleBowl.Center);
        Check(Vector3.Distance(bowl1.TransformPoint(TownServiceTempleBowl.Center),actual)<.0001f&&Vector3.Distance(bowl2.TransformPoint(TownServiceTempleBowl.Center),actual)<.0001f,
            "both owners donate into the actual shared priest bowl, never a relocated workspace");
        foreach(Transform bowl in new[]{bowl1,bowl2})
        {
            Check(TownServiceTempleBowl.Contains(bowl,actual+priest.transform.up*.02f),"deliberate drop above actual bowl is accepted for both owners");
            Check(!TownServiceTempleBowl.Contains(bowl,workspace1.transform.TransformPoint(TownServiceRitualLayout.Origin+TownServiceTempleBowl.Center))
                &&!TownServiceTempleBowl.Contains(bowl,workspace2.transform.TransformPoint(TownServiceRitualLayout.Origin+TownServiceTempleBowl.Center)),"empty browsing workspace cannot receive a donation");
            Check(!TownServiceTempleBowl.Contains(bowl,bowl.TransformPoint(TownServiceTempleBowl.Center+new Vector3(.085f,0,.085f))),"outside circular bowl is not an offering");
            Check(!TownServiceTempleBowl.Contains(bowl,bowl.TransformPoint(TownServiceTempleBowl.Center+Vector3.down*.1f)),"dropping through the tabletop cannot donate");
        }
        Object.DestroyImmediate(priest);Object.DestroyImmediate(workspace1);Object.DestroyImmediate(workspace2);return checks;
    }
}
