using System;
using System.Collections;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Final-head witnesses for a bounded native-mesh environment. The
/// graphics API port is the same declared shader witness as the containing scope.</summary>
public static class FiniteBubbleProbe
{
    public static int Assertions;
    private static void Check(bool value,string label)
    {Assertions++;if(!value)throw new InvalidOperationException(label);}
    private static Texture2D Capture(Camera head,string path)
    {
        var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);target.Create();
        var saved=head.targetTexture;var active=RenderTexture.active;
        var image=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
        try
        {
            head.targetTexture=target;head.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());return image;
        }
        finally{head.targetTexture=saved;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);}
    }
    private static int ClearPixels(Texture2D image)
    {
        int count=0;foreach(Color32 p in image.GetPixels32())if(p.r>252&&p.g<3&&p.b>252)count++;
        return count;
    }
    public static IEnumerator Run(Camera head,Renderer sky,string output)
    {
        Assertions=0;
        Vector3 savedPosition=head.transform.position;Quaternion savedRotation=head.transform.rotation;
        float savedFar=head.farClipPlane;
        Matrix4x4 placed=sky.transform.localToWorldMatrix;Bounds bounds=sky.bounds;
        Mesh native=sky.GetComponent<MeshFilter>().sharedMesh;
        float radius=sky.transform.TransformVector(Vector3.right*native.bounds.extents.x).magnitude;
        Check(Mathf.Abs(radius/VRRigDriver.BaseWorldScale-6.9991f)<.005f,
            "menu finite Default bubble uses the unzoomed canonical native radius");
        head.farClipPlane=Mathf.Max(savedFar,bounds.size.magnitude*4f);
        try
        {
            float[] distance={0f,.96f,1.04f,1.5f};
            for(int step=0;step<distance.Length;step++)
            {
                head.transform.SetPositionAndRotation(bounds.center+Vector3.right*radius*distance[step],Quaternion.LookRotation(Vector3.right));
                SkyBackdrop.Tick(false);
                Check(sky.transform.localToWorldMatrix.Equals(placed),"default bubble stays fixed while the player flies through its physical shell");
                Texture2D image=Capture(head,Path.Combine(output,"finite-bubble-flight-"+step+".png"));
                try
                {
                    int clear=ClearPixels(image);
                    File.AppendAllText(Path.Combine(output,"finite-bubble-pixels.txt"),"step="+step+";relativeRadius="+distance[step]+";clear="+clear+";worldCenter="+bounds.center+";worldExtents="+bounds.extents+"\n");
                    Check(step<2?clear==0:clear==256*256,step<2
                        ?"inside and approaching the original sky shell retain final head pixels"
                        :"crossing and leaving the fixed sky bubble reveals the clear exterior");
                }
                finally{Object.DestroyImmediate(image);}
                yield return null;
            }
            head.transform.LookAt(bounds.center);SkyBackdrop.Tick(false);
            var back=Capture(head,Path.Combine(output,"finite-bubble-exterior-lookback.png"));
            try
            {
                int clear=ClearPixels(back);
                Check(clear>1000&&clear<60000,"outside lookback shows a bounded original dome surrounded by the clear exterior");
            }
            finally{Object.DestroyImmediate(back);}
        }
        finally{head.transform.SetPositionAndRotation(savedPosition,savedRotation);head.farClipPlane=savedFar;}
    }

    internal static IEnumerator MapClip(Camera head,Transform rig,Renderer sky,string output)
    {
        Matrix4x4 placed=sky.transform.localToWorldMatrix;
        Mesh native=sky.GetComponent<MeshFilter>().sharedMesh;
        float radius=sky.transform.TransformVector(Vector3.right*native.bounds.extents.x).magnitude;
        float vertical=sky.transform.TransformVector(Vector3.up*native.bounds.extents.y).magnitude;
        Check(Mathf.Abs(radius/VRRigDriver.BaseWorldScale-6.9991f)<.005f
            &&Mathf.Abs(vertical/VRRigDriver.BaseWorldScale-2.9658f)<.005f,
            "menu and map use the canonical native Default physical semi-axes independently of their far planes");
        var clip=VRRigDriver.Instance!;
        Vector3 eye=head.transform.position;Quaternion facing=head.transform.rotation;
        var initial=Capture(head,Path.Combine(output,"finite-map-consumed-clip.png"));
        try{Check(ClearPixels(initial)==0,"actual map clip consumer retains the finite Default bubble in the final head raster");}
        finally{Object.DestroyImmediate(initial);}
        float initialFar=head.farClipPlane;
        Check(initialFar>1000f,"map clip policy consumes the live shell budget beyond its original five-metre far plane");
        try
        {
            for(int step=0;step<3;step++)
            {
                rig.position+=new Vector3(9,1,-7)*198.12f;
                rig.localScale=Vector3.one*(198.12f*(step==1?.3f:2f));
                head.transform.SetPositionAndRotation(sky.bounds.center+Vector3.right*radius*(1.5f+step),Quaternion.LookRotation(Vector3.left));
                SkyBackdrop.Tick(false);clip.TickClipProbe();
                Check(sky.transform.localToWorldMatrix.Equals(placed),"world drag zoom and distant lookback retain the exact occupied Default shell geometry");
                Check(head.farClipPlane>=NativeDefaultSky.MinFarWorldUnits()*.999f,
                    "actual clip policy consumes the moved-eye finite shell budget while retaining its precision guard");
                var image=Capture(head,Path.Combine(output,"finite-map-zoom-lookback-"+step+".png"));
                try
                {
                    int clear=ClearPixels(image);
                    Check(clear<65000,"world zoom and distant lookback retain original finite sky pixels through the actual clip consumer");
                }
                finally{Object.DestroyImmediate(image);}
                yield return null;
            }
        }
        finally{rig.localScale=Vector3.one*198.12f;head.transform.SetPositionAndRotation(eye,facing);clip.TickClipProbe();}
    }
}
