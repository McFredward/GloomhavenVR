using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using Object=UnityEngine.Object;

public static partial class MenuDepthLaserProgram
{
    // Session/peer authority is a fixture input. The failure precedes that
    // authority: an unrelated destroyed interface source aborts every hand's
    // original uGUI tick before the following production bar query can run.
    public static IEnumerator RunRetiredPlaneStory(string output,Action<int> count)
    {
        _checks=0;
        foreach(string participant in new[]{"host","peer"})
        {
            using(var scene=new Scene())
            {
                scene.Root.name="shared scenario story "+participant;
                scene.Root.localScale=Vector3.one*(participant=="host"?.002f:.003f);
                var image=Picture("original story Continue target",scene.Root,new Vector2(800,180),Color.white);
                // An original native foreground member need not occupy the
                // canvas's z=0 plane; this enters the exact failing native scan.
                image.rectTransform.localPosition=new Vector3(0,0,-20);
                var callbacks=image.gameObject.AddComponent<LaserNativeCallbacks>();
                var rod=Rect("visible scenario grab bar",scene.Root,new Vector2(700,20));
                rod.localPosition=new Vector3(0,-160,-20);
                var collider=rod.gameObject.AddComponent<BoxCollider>();collider.isTrigger=true;
                collider.size=new Vector3(700,20,20);
                var handle=rod.gameObject.AddComponent<PanelGrabHandle>();handle.BarCollider=collider;
                VRInteractables.Grabbables.Add((handle,collider));
                yield return null;
                Canvas.ForceUpdateCanvases();Physics.SyncTransforms();

                // Retain the same managed-interface/native-destroyed split as
                // paired750 Player.log. Its graphic remains an inactive native
                // pool member, so the normal source-visible scan ignores it;
                // the native raised-member scan still inspects its readiness.
                var pooled=Picture("inactive retained native artwork",scene.Root,new Vector2(10,10),Color.white);
                pooled.gameObject.SetActive(false);
                var sourceObject=new GameObject("retired card plane wrapper");
                var plane=sourceObject.AddComponent<MenuDepthCardPlane>();
                plane.Configure(callbacks,image.rectTransform,new Graphic[]{pooled},scene.Root);
                IMenuDepthSource retained=plane;
                Object.DestroyImmediate(plane);
                MenuDepthSources.Register(retained);
                Check(plane==null&&!ReferenceEquals(retained,null),"Unity-destroyed source remains a managed interface reference");
                bool reachedBar=false,barHit=false;PanelGrabHandle? picked=null;Exception? failure=null;
                try
                {
                    scene.Aim(image.transform.position);scene.Tick();
                    reachedBar=true;
                    Vector3 origin=scene.Hand.Ray.Current.Origin;
                    barHit=RayGrabDriver.PickBarGeometry(new Ray(origin,(rod.position-origin).normalized),20f,
                        out picked,out _,out _);
                }
                catch(Exception error){failure=error;}
                Check(failure==null&&reachedBar,
                    "retired card plane cannot abort scenario story laser or following grab-bar phase");
                Check(scene.Driver.HasHit&&ReferenceEquals(scene.Driver.Hovered,image.gameObject),
                    participant+" scenario story remains a live original pointer target");
                Check(barHit&&ReferenceEquals(picked,handle),participant+" production bar geometry remains reachable after uGUI");
                scene.Hand.TriggerDown=scene.Hand.TriggerPressed=true;scene.Tick();
                scene.Hand.TriggerDown=scene.Hand.TriggerPressed=false;scene.Tick();
                Check(callbacks.Downs==1&&callbacks.Ups==1&&callbacks.Clicks==1,
                    participant+" original story pointer callback executes exactly once");
                Check(!retained!.IsReady,"destroyed source readiness returns false without a native Behaviour call");
                MenuDepthSources.Unregister(retained);VRInteractables.Grabbables.Clear();
                Object.DestroyImmediate(sourceObject);
            }
        }
        count(_checks);
    }
}
