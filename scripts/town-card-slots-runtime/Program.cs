using System;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static class InteractionProgram
{
    private static int _count;
    private static void Check(bool value,string message){_count++;if(!value)throw new Exception(message);}
    private static UIEnhanceCardPoint MakePoint(Transform parent,int index,bool occupied)
    {
        var root=new GameObject("NativePoint"+index,typeof(RectTransform));root.SetActive(false);root.transform.SetParent(parent,false);
        var point=root.AddComponent<UIEnhanceCardPoint>();
        root.AddComponent<NativePointController>();root.AddComponent<Button>();root.AddComponent<BoxCollider>();
        var mark=new GameObject("Original enhanced check",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));mark.transform.SetParent(root.transform,false);
        var image=mark.GetComponent<Image>();image.enabled=occupied;image.color=index%2==0?Color.red:Color.green;
        root.SetActive(true);return point;
    }
    public static int Run()
    {
        _count=0;SlotsClock.Now=0;
        var room=new GameObject("Native window source");var seat=new GameObject("Actual offering seat");
        seat.transform.SetPositionAndRotation(new Vector3(3,1,5),Quaternion.Euler(0,38,0));
        var slot=room.AddComponent<UIEnhanceCardSlot>();
        var handoff=new TownServiceEnhancementHandoff{Card=new object(),NativeSlot=slot,Seat=seat.transform};
        using var display=new TownServiceCardSlots();
        try
        {
            foreach(int count in new[]{0,1,2,9,24,3})
            {
                foreach(var point in slot.assignedPoints)UnityEngine.Object.DestroyImmediate(point.gameObject);
                slot.assignedPoints.Clear();
                for(int i=0;i<count;i++)slot.assignedPoints.Add(MakePoint(room.transform,i,i%2==0));
                int controllers=NativePointController.Enabled;SlotsClock.Now+=.2f;display.Tick(handoff);
                Check(display.Points.Count==count,"offered card exposes every native enhancement point without a fixed cap");
                Check(RemoteWidgetMirror.Live.Count==count,"changing point count retires all previous mirrors");
                Check(NativePointController.Enabled==controllers,"cloned native point controllers never execute");
                for(int i=0;i<count;i++)
                {
                    var point=display.Points[i];Check(point.Source==slot.assignedPoints[i].transform,"point order and source identity match actual offered card");
                    Transform content=point.Content!;Check(content!=null,"each original point has display content");
                    Check(content!.GetComponentsInChildren<Collider>(true).Length==0&&content.GetComponentsInChildren<Button>(true).Length==0,"enhancement slot display has no collider or native input");
                    var group=content.GetComponent<CanvasGroup>();Check(group!=null&&!group.interactable&&!group.blocksRaycasts,"native point clone cannot intercept laser or pointer");
                    var original=point.Source.GetComponentInChildren<Image>();var copied=point.CloneOf(original.transform)!.GetComponent<Image>();
                    Check(original.enabled==copied.enabled&&original.color==copied.color,"occupied and available slot appearance retains original native state");
                }
            }
            var old=display.Points[0];var replacement=MakePoint(room.transform,100,true);
            slot.assignedPoints[0]=replacement;SlotsClock.Now+=.001f;display.Tick(handoff);
            Check(display.Points[0].Source==replacement.transform&&old.Content==null,"same-count pooled point replacement cannot retain stale original content");
            var current=display.Points[0];var sourceImage=replacement.GetComponentInChildren<Image>();
            int created=RemoteWidgetMirror.Creates;int ticks=RemoteWidgetMirror.Live.Sum(m=>m.Ticks);
            sourceImage.enabled=false;sourceImage.color=Color.blue;SlotsClock.Now+=1f/90f;display.Tick(handoff);
            var clone=current.CloneOf(sourceImage.transform)!.GetComponent<Image>();
            Check(!clone.enabled&&clone.color==Color.blue,"enhancement removal reaches displayed original point on the next frame");
            Check(RemoteWidgetMirror.Live.Sum(m=>m.Ticks)>ticks,"intermediate native slot animation is sampled every frame");
            Check(RemoteWidgetMirror.Creates==created,"unchanged native point identity does not rebuild the wrapper");
            sourceImage.enabled=true;SlotsClock.Now+=1f/90f;display.Tick(handoff);
            Check(clone.enabled,"upgrade occupancy can return without rebuilding the handoff");
            var pulse=new GameObject("New original pulse",typeof(RectTransform),typeof(Image));pulse.transform.SetParent(replacement.transform,false);
            SlotsClock.Now+=.11f;display.Tick(handoff);
            Check(current.CloneOf(pulse.transform)!=null,"native point hierarchy refresh discovers new animation descendants");
            slot.assignedPoints[1]=null!;SlotsClock.Now+=.001f;display.Tick(handoff);
            Check(display.Points.Count==0&&RemoteWidgetMirror.Live.Count==0,"transient missing native point hides stale slot display atomically");
            slot.assignedPoints[1]=MakePoint(room.transform,101,false);SlotsClock.Now+=.001f;display.Tick(handoff);
            Check(display.Points.Count==3,"native point list recovers after a pooled rebuild");
            handoff.Card=null;SlotsClock.Now+=.001f;display.Tick(handoff);
            Check(display.Points.Count==0&&RemoteWidgetMirror.Live.Count==0,"physical card reclaim removes every slot mirror immediately");
            handoff.Card=new object();display.Tick(handoff);Check(display.Points.Count==3,"offering again restores current native points");
            handoff.NativeSlot=null;display.Tick(handoff);Check(display.Points.Count==0,"missing selected native card cannot expose stale slot data");
            handoff.NativeSlot=slot;display.Tick(handoff);display.Dispose();display.Dispose();
            Check(RemoteWidgetMirror.Live.Count==0&&RemoteWidgetMirror.Creates==RemoteWidgetMirror.Destroys,"repeated teardown releases all owned mirrors exactly once");
        }
        finally{display.Dispose();UnityEngine.Object.DestroyImmediate(room);UnityEngine.Object.DestroyImmediate(seat);}
        return _count;
    }
}
