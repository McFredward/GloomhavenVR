using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;
public static class VRRigDriver {public static Camera? HeadCamera;}
public static class ModalFallback
{
    public static int Closed;public static Action? OnClose;
    public static void CloseFloatedWindow(UIWindow window){Closed++;OnClose?.Invoke();window.IsOpen=false;}
}
namespace GloomhavenVR.WorldUI
{
    public sealed class TownServiceRitual
    {
        public sealed class Piece{public readonly CancelToken Token=new();}
        public sealed class CancelToken{public bool Cancelled;public void CancelInspection()=>Cancelled=true;}
        public readonly List<Piece> Pieces=new(){new Piece()};
    }
}
internal static class TempleExitProof
{
    internal static int Run()
    {
        int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
        for(int scenario=0;scenario<5;scenario++)
        {
            var root=new GameObject("Native temple exit",typeof(UIWindow));var window=root.GetComponent<UIWindow>();window.IsOpen=scenario!=3;
            root.transform.position=new Vector3(2f,.4f,1f);root.transform.localScale=Vector3.one*.7f;
            var head=new GameObject("Tracked head",typeof(Camera));VRRigDriver.HeadCamera=scenario==1?null:head.GetComponent<Camera>();
            head.transform.position=root.transform.TransformPoint(new Vector3(scenario==2?1f:2.1f,1.75f,0f));
            var visitor=new BoundTempleExit(window,root.transform){_visited=scenario!=4,_near=false};
            MapRoomHand.TempleInspection=true;ModalFallback.Closed=0;
            ModalFallback.OnClose=()=>Check(visitor._ritual.Pieces[0].Token.Cancelled&&!MapRoomHand.TempleInspection&&!visitor.Available,
                "purse cancellation and fan restoration happen before reentrant native temple exit");
            bool exited=visitor.ExitIfAway();
            if(scenario==0)
            {
                Check(exited&&ModalFallback.Closed==1&&!window.IsOpen,"physical departure closes native temple before visiting another resident");
                Check(!visitor.ExitIfAway()&&ModalFallback.Closed==1,"closed temple is not repeatedly exited");
            }
            else Check(!exited&&ModalFallback.Closed==0,"missing tracking, temporary input disablement or closed/unvisited temple never cause a false exit");
            Object.DestroyImmediate(root);Object.DestroyImmediate(head);
        }
        VRRigDriver.HeadCamera=null;ModalFallback.OnClose=null;return checks;
    }
}
