using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Cards;
public static class InteractionProgram {
 public static int Checks;public static string Metrics="";
 private static void Check(bool ok,string message){Checks++;if(!ok)throw new Exception(message);}
 private static GameObject Node(string name,Transform? parent=null){var g=new GameObject(name,typeof(RectTransform));if(parent!=null)g.transform.SetParent(parent,false);return g;}
 private static void Refresh(HiddenWindowVeilState s,Transform r)=>typeof(CanvasConversion).GetMethod("RefreshVeilWindowInventory",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{s,r});
 public static IEnumerator Run(){
  var root=Node("root");var entry=Node("portrait",root.transform);var child=Node("avatar",entry.transform);child.transform.localPosition=new Vector3(1,2,30);
  var inactive=Node("hidden",entry.transform);inactive.SetActive(false);
  using(var inventory=new UiHierarchyInventory(root.transform)) {
   Check(inventory.Refresh()&&inventory.Nodes.Count==4,"initial inactive subtree inventoried");
   for(int i=0;i<1000;i++)Check(!inventory.Refresh(),"stable inventory must not rebuild");
   var newChild=Node("pooled",inactive.transform);Check(inventory.IsDirty,"inactive parent insertion invalidates immediately");
   inventory.Refresh();Check(inventory.Nodes.Count==5,"pooled inactive node captured");
   newChild.transform.SetParent(entry.transform,false);Check(inventory.IsDirty,"reparent invalidates immediately");inventory.Refresh();
   int revision=inventory.Revision;inactive.SetActive(true);Check(inventory.Revision!=revision,"activation wakes maintenance cadence");
   UnityEngine.Object.DestroyImmediate(newChild);Check(inventory.IsDirty,"removal invalidates immediately");inventory.Refresh();Check(inventory.Nodes.Count==4,"destroyed node removed");
   var nested=new UiHierarchyInventory(entry.transform);nested.Refresh();inventory.Dispose();Node("second",entry.transform);Check(nested.IsDirty,"disposing outer inventory retains inner subscriptions");nested.Dispose();
  }
  InitiativeTrack.Instance=new InitiativeTrack{initiativeTrackHolder=root.transform};
  var depth=new InitiativeTrackSurface();WorldUIConfig.InitiativeDepthMaxSpreadPx.Value=10;
  depth.Tick();Check(Mathf.Abs(child.transform.localPosition.z-10)<.001f,"authored subtree depth clamps to cap");
  for(int i=0;i<100;i++)depth.Tick();Check(Mathf.Abs(child.transform.localPosition.z-10)<.001f,"depth is idempotent");
  var added=Node("new accent",entry.transform);added.transform.localPosition=new Vector3(0,0,-30);depth.Tick();
  Check(Mathf.Abs(child.transform.localPosition.z-5)<.001f&&Mathf.Abs(added.transform.localPosition.z+5)<.001f,"new pooled accent gets exact full-spread clamp immediately");
  depth.Restore();Check(Mathf.Abs(child.transform.localPosition.z-30)<.001f&&Mathf.Abs(added.transform.localPosition.z+30)<.001f,"full original depths restore");
  var state=new HiddenWindowVeilState();var disabled=child.AddComponent<UIWindow>();disabled.enabled=false;Refresh(state,root.transform);
  Check(state.Windows.Contains(disabled),"disabled native windows retained beyond active registry");
  var late=added.AddComponent<UIWindow>();Refresh(state,root.transform);Check(state.Windows.Contains(late),"window added to existing transform captured through native registry");
  late.transform.SetParent(null);Refresh(state,root.transform);Check(!state.Windows.Contains(late),"reparented window leaves original panel inventory");state.Inventory!.Dispose();
  var host=Node("panel");var target=Node("target",host.transform);var imageNode=Node("image",target.transform);var image=imageNode.AddComponent<Image>();
  var texture=new Texture2D(16,16);var original=Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f));image.sprite=original;
  CanvasConversion.ActivePanels.Add(new ConvertedPanel{HostGo=host,Target=(RectTransform)target.transform});
  PanelMipBake.TickArrivals();yield return null;
  // Existing Image arrival stays same-frame even when the inventory is entirely stable.
  var arrived=Sprite.Create(texture,new Rect(0,0,8,8),new Vector2(.5f,.5f));image.enabled=false;image.sprite=arrived;
  PanelMipBake.TickArrivals();Check(image.sprite!=arrived&&CardFaceMipBake.IsBakedSprite(image.sprite),"hidden original sprite arrival swapped in same frame");yield return null;
  var pooledNode=Node("pooled image",target.transform);var pooled=pooledNode.AddComponent<Image>();pooled.sprite=original;pooled.enabled=false;
  PanelMipBake.TickArrivals();Check(pooled.sprite!=original&&CardFaceMipBake.IsBakedSprite(pooled.sprite),"pooled sprite arrival precedes next periodic recapture");yield return null;
  for(int i=0;i<40;i++){PanelMipBake.TickArrivals();yield return null;}
  WorldUIConfig.PanelMipBake.Value=false;PanelMipBake.TickArrivals();Check(image.sprite==arrived&&pooled.sprite==original,"config-off restores original sprites");
  Check(GloomhavenVR.Core.VRLog.Errors==0,"production arrival watch must not swallow fixture exceptions");
  Metrics="1000 stable polls/zero rebuilds; inactive pooling, reparenting, activation, disabled windows, exact depths and same-frame original sprite arrivals";
 }
}
