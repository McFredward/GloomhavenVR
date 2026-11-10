#pragma warning disable CS0649
using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;
public static class ProgressionTests {
 static int n;static string evidence="";static readonly List<Object> owned=new();
 static void Check(bool value,string why){n++;if(!value)throw new Exception(why);}
 static GameObject Go(string name,params Type[] components){var g=new GameObject(name,components);owned.Add(g);return g;}
 static RectTransform Rect(string name,Transform? parent=null){var r=(RectTransform)Go(name,typeof(RectTransform)).transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;return r;}
 static Image Ink(RectTransform r,Color colour){var i=r.gameObject.AddComponent<Image>();i.color=colour;return i;}
 static UIResultsButtonOption Option(string name,Transform parent,Action action){var r=Rect(name,parent);r.sizeDelta=new Vector2(300,70);var option=r.gameObject.AddComponent<UIResultsButtonOption>();option._button=r.gameObject.AddComponent<ExtendedButton>();option.Register(action);Ink(r,Color.cyan);return option;}
 public static int Run(string output){
  n=0;evidence=output;owned.Clear();FFSNetwork.IsOnline=true;FFSNetwork.IsClient=false;GloomhavenVR.Core.VRSession.IsRunning=true;
  var camera=Go("FinalHead",typeof(Camera)).GetComponent<Camera>();camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.75f;camera.aspect=2;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
  var canvas=Rect("Original native UI Canvas");canvas.sizeDelta=new Vector2(1920,1080);canvas.localScale=Vector3.one*.001f;
  var canvasComponent=canvas.gameObject.AddComponent<Canvas>();canvasComponent.renderMode=RenderMode.WorldSpace;canvasComponent.worldCamera=camera;canvas.gameObject.AddComponent<GraphicRaycaster>();
  var windowRect=Rect("New Scenario Results Panel",canvas);windowRect.sizeDelta=new Vector2(1920,1080);
  var window=windowRect.gameObject.AddComponent<UIWindow>();var manager=windowRect.gameObject.AddComponent<UINewAdventureResultsManager>();manager.myWindow=window;Singleton<UIResultsManager>.Instance=manager;
  var body=Rect("Native body",windowRect);body.sizeDelta=new Vector2(900,600);var original=Ink(body,Color.red);
  manager.resultsPanel=Go("Native Results");manager.resultsPanel.transform.SetParent(windowRect,false);
  manager.statsPanel=Go("Native Stats");manager.statsPanel.transform.SetParent(manager.resultsPanel.transform,false);
  manager.buttonsContainer=Go("Native Buttons");manager.buttonsContainer.transform.SetParent(manager.resultsPanel.transform,false);
  manager.header=Go("Native Header",typeof(HeaderHighlightText),typeof(GUIAnimator)).GetComponent<HeaderHighlightText>();manager.header.transform.SetParent(manager.resultsPanel.transform,false);manager.header.highlightAnimator=manager.header.GetComponent<GUIAnimator>();
  manager.showStatsAnimation=manager.resultsPanel.AddComponent<GUIAnimator>();
  int returns=0,retries=0;var returnOption=Option("Original return to map",manager.buttonsContainer.transform,()=>{returns++;manager.returnToMapOption.gameObject.SetActive(false);});var retryOption=Option("Original retry",manager.buttonsContainer.transform,()=>{retries++;manager.retryOption.gameObject.SetActive(false);});manager.returnToMapOption=returnOption;manager.retryOption=retryOption;
  var readyHome=Rect("Original native ready home",canvas);readyHome.sizeDelta=new Vector2(1920,1080);
  var readyRect=Rect("Multiplayer Ready Toggle",readyHome);readyRect.sizeDelta=new Vector2(307,65);readyRect.anchoredPosition3D=new Vector3(15,-420,0);readyRect.localRotation=Quaternion.Euler(0,0,3);readyRect.localScale=Vector3.one*1.1f;readyRect.gameObject.layer=5;
  var ready=readyRect.gameObject.AddComponent<UIReadyToggle>();ready.Window=readyRect.gameObject.AddComponent<UIWindow>();ready.Window.IsOpen=false;ready.Toggle=readyRect.gameObject.AddComponent<ExtendedToggle>();var readyInk=Ink(readyRect,Color.yellow);ready.Toggle.targetGraphic=readyInk;Singleton<UIReadyToggle>.Instance=ready;
  Singleton<UIScenarioMultiplayerController>.Instance=new UIScenarioMultiplayerController();
  var helpRect=Rect("Original HelpBox",canvas);helpRect.gameObject.AddComponent<CanvasGroup>().alpha=0;var help=helpRect.gameObject.AddComponent<HelpBox>();help.myWindow=helpRect.gameObject.AddComponent<UIWindow>();var helpInk=Ink(helpRect,Color.blue);Singleton<HelpBox>.Instance=help;
  FFSNet.PlayerRegistry.MyPlayer=new NetworkPlayer();var guest=new NetworkPlayer{IsParticipant=false};
  FFSNet.PlayerRegistry.AllPlayers.Clear();FFSNet.PlayerRegistry.AllPlayers.AddRange(new[]{FFSNet.PlayerRegistry.MyPlayer,guest});
  FFSNet.PlayerRegistry.Participants.Clear();FFSNet.PlayerRegistry.Participants.Add(FFSNet.PlayerRegistry.MyPlayer);int callbacks=0;
  // The original game's exact decision method executes. Network/bootstrap and
  // readiness acknowledgements are explicit ports, never generated by the mod.
  manager.BeginNativeReady(()=>callbacks++);Check(!ready.IsVisible,"native reveal owns when the original host Continue becomes available");ready.SetInteractable(true);int nativeReadyCalls=ready.ReadyCalls,shows=ready.Window.Shows;
  Check(ready.IsVisible&&ready.ReadyUpType==UIReadyToggle.EReadyUpType.Player&&nativeReadyCalls==0,"native host owns the original manual final ready control");
  var roots=new List<ScenarioResultsRoot>();ScenarioResultsNative.CollectRoots(roots);
  Check(ScenarioResultsNative.Window==window&&SharedWindows.KindOf(window)==SharedWindowKind.Results&&SharedWindows.WindowOf(SharedWindowKind.Results)==window,"Results7 resolves only the exact native singleton window");
  Check(SharedWindows.ParticipatesHere(SharedWindowKind.Results),"native results participate in shared scenario presentation");
  Check(roots.Count==6&&roots[5].Root==readyRect,"all original phase/ready roots are exposed without cloning gameplay");
  var homePosition=readyRect.anchoredPosition3D;var homeRotation=readyRect.localRotation;var homeScale=readyRect.localScale;
  var homeMin=readyRect.anchorMin;var homeMax=readyRect.anchorMax;var homePivot=readyRect.pivot;var homeSize=readyRect.sizeDelta;
  var fitted=Rect("Fitted original results host",canvas);fitted.sizeDelta=new Vector2(960,800);
  var conversion=new ConvertedPanel{Target=windowRect,HostRect=fitted,HostGo=fitted.gameObject,NativeHome=canvas,Position=windowRect.anchoredPosition3D,Scale=windowRect.localScale,Rotation=windowRect.localRotation,Min=windowRect.anchorMin,Max=windowRect.anchorMax,Pivot=windowRect.pivot,Size=windowRect.sizeDelta};
  CanvasConversion.ActivePanels.Add(conversion);windowRect.SetParent(fitted,false);
  // Paired764: the host controls both characters, guest owns none and auto-readies.
  // Execute the exact native OnReadiedPlayersChanged/ShouldBeVisible methods:
  // they hide the outstanding host waiter using Participants (1), although this
  // Player-mode decision must await AllPlayers (2).
  ready.Acknowledge(guest);Check(ready._allPlayersReady&&!ready.IsVisible,"native participant-only visibility bookkeeping reproduces the spectator deadlock");
  ScenarioResultsReadyPark.Tick();
  Check(ready.IsVisible&&!ready._allPlayersReady,"spectator acknowledgement cannot hide the outstanding host Continue control");
  Check(ready.PlayersReady.Count==1&&ready.ReadyCalls==nativeReadyCalls&&callbacks==0,"Continue visibility repair does not manufacture a vote or advance the native waiter");
  shows=ready.Window.Shows;int hides=ready.Window.Hides;
  Check(readyRect.parent!=windowRect&&ScenarioResultsReadyPark.Claims(ready.gameObject),"results seats exactly its own original ready control before fallback conversion");
  Check(readyRect.parent.parent==fitted&&conversion.ExtraRenderRoots.Contains(readyRect.parent),"original ready dock shares the fitted Results canvas/capture without becoming a native-owner child");
  Check(fitted.rect.Contains(fitted.InverseTransformPoint(readyRect.position)),"ready footer remains inside the actual fitted visible results frame");
  ScenarioResultsReadyPark.Tick();
  Check(ready.ReadyCalls==nativeReadyCalls&&callbacks==0&&ready.Window.Shows==shows&&ready.Window.Hides==hides,"parking never activates ready/Show/Hide or a native completion callback");
  ready.SetInteractable(false);ScenarioResultsReadyPark.Tick();Check(!ready.Toggle.interactable,"native reveal/readiness disablement remains intact");
  ready.SetInteractable(true);var eventGo=Go("OriginalEventSystem",typeof(EventSystem));Canvas.ForceUpdateCanvases();
  var proxyRect=Rect("Own public metadata input");var proxy=proxyRect.gameObject.AddComponent<ScenarioResultsContinuationProxy>();proxy.Operation=ScenarioResultsNative.ContinueReady;proxy.Refresh(77);int requests=0;byte requestedOperation=0;uint requestedOpening=0;
  ScenarioResultsNative.SubmitContinuation=(operation,key)=>{requestedOperation=operation;requestedOpening=key;requests++;return true;};
  ExecuteEvents.Execute(proxy.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
  Check(requestedOperation==ScenarioResultsNative.ContinueReady&&requestedOpening==77,"public Continue input carries only its exact operation/opening metadata");
  Check(requests==1&&ready.ReadyCalls==nativeReadyCalls,"observer input submits an explicit request without running any copied native callback");ScenarioResultsNative.SubmitContinuation=null;
  Picture(camera,"results-native-ready-stage.png",Color.red,readyRect);
  var screen=RectTransformUtility.WorldToScreenPoint(camera,readyRect.position);var data=new PointerEventData(EventSystem.current){position=screen,button=PointerEventData.InputButton.Left};
  var hits=new List<RaycastResult>();canvas.GetComponent<GraphicRaycaster>().Raycast(data,hits);
  File.WriteAllText(Path.Combine(evidence,"native-ready-ray.txt"),"point="+screen+" rectContains="+RectTransformUtility.RectangleContainsScreenPoint(readyRect,screen,camera)+" depth="+readyInk.depth+" culled="+readyInk.canvasRenderer.cull+" inheritedAlpha="+readyInk.canvasRenderer.GetInheritedAlpha()+" graphics="+GraphicRegistry.GetGraphicsForCanvas(canvasComponent).Count+" hits="+hits.Count+" world="+readyRect.position+" viewport="+camera.WorldToViewportPoint(readyRect.position));
  Check(hits.Exists(h=>h.gameObject==readyRect.gameObject),"parked original ready button remains in the real native GraphicRaycaster");
  InteractabilityManager.AllowToggle=false;Check(!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ContinueReady)&&ready.ReadyCalls==nativeReadyCalls,"native pointer interception remains authoritative for public continuation requests");InteractabilityManager.AllowToggle=true;
  Check(ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ContinueReady)&&ready.ReadyCalls==nativeReadyCalls+1&&callbacks==0,"original pointer callback votes without inventing all-player acknowledgement");
  Check(!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ContinueReady)&&ready.ReadyCalls==nativeReadyCalls+1,"a second public Continue cannot unready or replay the accepted native pointer");
  ready.AllReady!.Invoke();Check(callbacks==1,"only native all-ready continuation invokes the original host callback");
  // The actual native all-ready callback hides its control. Start a fresh native
  // decision before exercising independent pooled-owner handovers.
  ready.Reset();manager.BeginNativeReady(()=>callbacks++);ready.SetInteractable(true);ScenarioResultsReadyPark.Tick();
  // A subsequent quest/loadout owner must record the native home, not this footer.
  Check(ModalFallback.ReleaseForComposite(ready.Window),"external owner admits the pooled original ready control");
  Check(readyRect.parent==readyHome,"external owner receives the actual native ready home before recording its receipt");
  Check(readyRect.anchoredPosition3D==homePosition&&readyRect.localScale==homeScale&&readyRect.localRotation==homeRotation&&readyRect.anchorMin==homeMin&&readyRect.anchorMax==homeMax&&readyRect.pivot==homePivot&&readyRect.sizeDelta==homeSize&&readyRect.gameObject.layer==5,"results transfer restores original rect, scale, rotation and layers");
  ReadyToggleParkClaim.Claimed=true;ReadyToggleParkClaim.ClaimedObject=ready.gameObject;ScenarioResultsReadyPark.Tick();
  Check(readyRect.parent==readyHome,"an existing map/loadout owner retains the pooled ready control");ReadyToggleParkClaim.Claimed=false;
  ScenarioResultsReadyPark.Tick();Check(ScenarioResultsReadyPark.Claims(ready.gameObject),"native results re-adopts only after the other owner relinquishes its claim");
  ready.readyUpToggleState=EReadyUpToggleStates.Quests;ScenarioResultsReadyPark.Tick();Check(readyRect.parent==readyHome,"same-window pooled quest readiness immediately releases Results/NotSet ownership");Check(ScenarioResultsNative.ContinuationTarget(ScenarioResultsNative.ContinueReady)==ready.Toggle&&!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ContinueReady),"a late client template supplies geometry identity without granting another native waiter Results permission");ready.readyUpToggleState=EReadyUpToggleStates.NotSet;
  // Presentation repair may not override a real native visibility blocker or
  // the native reveal/readiness interactability permission.
  var blocker=new object();ready.Block(blocker);ready.PlayersReady.Clear();ready.Acknowledge(guest);ScenarioResultsReadyPark.Tick();
  Check(!ready.IsVisible&&!ready._allPlayersReady,"visibility-count repair retains an independent native visibility blocker");
  ready.Unblock(blocker);ready.SetInteractable(false);ready.PlayersReady.Clear();ready.Acknowledge(guest);ScenarioResultsReadyPark.Tick();
  Check(!ready.IsVisible&&!ready._allPlayersReady&&!ready.Toggle.interactable,"visibility-count repair cannot enable an unavailable native Continue decision");
  ready.SetInteractable(true);ScenarioResultsReadyPark.Tick();ready.Window.Hide();Check(readyRect.parent==readyHome,"native ready OnHidden restores home synchronously before reuse");ScenarioResultsReadyPark.Tick();Check(!ScenarioResultsReadyPark.Claims(ready.gameObject),"native ready closure retires the results footer immediately");
  ready.Window.IsOpen=true;ScenarioResultsReadyPark.Tick();window.Hide();Check(readyRect.parent==readyHome,"native results OnHidden restores its original control before the next flow");window.Show();
  ScenarioResultsReadyPark.Tick();windowRect.gameObject.SetActive(false);if(readyRect==null)throw new Exception("Native ready was destroyed during owner deactivation");
  Check(readyRect.gameObject.activeInHierarchy&&readyRect.parent.GetComponent<CanvasGroup>().alpha==0,"native owner deactivation safely silences its independent ready dock");
  ScenarioResultsReadyPark.Tick();Check(readyRect.parent==readyHome&&readyRect.gameObject.activeInHierarchy,"native owner deactivation cannot strand the pooled ready control under a hidden results owner");windowRect.gameObject.SetActive(true);
  // A native flat host keeps its original source geometry and callback ownership.
  GloomhavenVR.Core.VRSession.IsRunning=false;ScenarioResultsReadyPark.Tick();Check(readyRect.parent==readyHome,"native flat host source is never reparented into a VR-only footer");GloomhavenVR.Core.VRSession.IsRunning=true;
  ready.Reset();FFSNet.PlayerRegistry.MyPlayer.IsParticipant=false;guest.IsParticipant=true;FFSNet.PlayerRegistry.Participants.Clear();FFSNet.PlayerRegistry.Participants.Add(guest);int beforeOffer=ready.ReadyCalls;
  manager.BeginNativeReady(()=>callbacks++);ScenarioResultsReadyPark.Tick();Check(!ready.IsVisible,"a host without a character cannot see Continue before native reveal permission");
  ready.SetInteractable(true);ScenarioResultsReadyPark.Tick();Check(ready.IsVisible&&ready.ReadyCalls==beforeOffer&&callbacks==1,"host without a character receives its original manual Continue without an automatic vote");
  Check(ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ContinueReady)&&ready.ReadyCalls==beforeOffer+1&&callbacks==1,"a host without a character can explicitly use the original Player-mode pointer decision");
  FFSNet.PlayerRegistry.MyPlayer.IsParticipant=true;guest.IsParticipant=false;FFSNet.PlayerRegistry.Participants.Clear();FFSNet.PlayerRegistry.Participants.Add(FFSNet.PlayerRegistry.MyPlayer);
  returnOption.gameObject.SetActive(true);retryOption.gameObject.SetActive(true);InteractabilityManager.AllowButton=false;
  Check(!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ReturnToMap)&&returns==0,"original native button interception leaves a refused public choice retryable");InteractabilityManager.AllowButton=true;
  returnOption._button.interactable=false;Check(!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ReturnToMap)&&returns==0,"native disabled return permission cannot be bypassed by a remote request");returnOption._button.interactable=true;
  window.IsOpen=false;window.ClosingTail=true;Check(!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ReturnToMap)&&returns==0,"closing native Results cannot accept a late Continue request");window.IsOpen=true;window.ClosingTail=false;
  FFSNetwork.IsClient=true;Check(!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ReturnToMap)&&returns==0,"a visitor never executes host results callbacks on its local native controller");FFSNetwork.IsClient=false;
  Check(ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ReturnToMap)&&returns==1&&!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.ReturnToMap),"only the original return pointer callback executes once and retires its native option");
  Check(ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.RetryScenario)&&retries==1&&!ScenarioResultsNative.TryInvokeContinuation(ScenarioResultsNative.RetryScenario),"only the original retry pointer callback executes once and retires its native option");
  Check(!ScenarioResultsNative.TryInvokeContinuation(0)&&!ScenarioResultsNative.TryInvokeContinuation(4),"unrecognized operations never resolve arbitrary Results controls");
  ready.Reset();FFSNetwork.IsClient=true;int before=ready.ReadyCalls;manager.BeginNativeReady(()=>callbacks++);
  Check(ready.ReadyCalls==before+1&&!ready.IsVisible&&callbacks==1,"native client auto-ready remains exactly native, without host exit authority");
  ScenarioResultsReadyPark.Tick();Check(readyRect.parent==readyHome,"native hidden client ready control never becomes a second visible footer");
  // An inert receiver picture is visible while originals keep running. Original
  // draw output yields, not their controllers, native permission or callback state.
  var mirror=Rect("Inert common results picture",canvas);mirror.sizeDelta=new Vector2(900,600);var mirrorInk=Ink(mirror,Color.green);
  using(var lease=new ScenarioResultsVisualLease()){
   Check(lease.Tick(mirror),"inert visible results picture acquires a reversible draw-only lease");
   Check(original.enabled&&original.gameObject.activeSelf&&original.canvasRenderer.GetColor().a==0&&helpInk.canvasRenderer.GetColor().a==0,"received picture suppresses only original draw output and the exact native wait tip");
   Picture(camera,"results-common-body.png",Color.green);
   help.ShowTranslated("unrelated player instruction","different title");lease.Tick(mirror);
   Check(helpInk.canvasRenderer.GetColor().a>0,"an unrelated replacement HelpBox is restored immediately");
   original.canvasRenderer.SetColor(new Color(.5f,.2f,.1f,.35f));lease.Tick(mirror);mirror.gameObject.SetActive(false);Check(!lease.Tick(mirror),"a vanished common picture immediately releases native output");
   Check(Mathf.Abs(original.canvasRenderer.GetColor().a-.35f)<.001f,"native draw tween changes survive visual-lease retirement");
  }
  original.canvasRenderer.SetColor(Color.white);help.myWindow.IsOpen=false;helpRect.gameObject.SetActive(false);Picture(camera,"results-native-restored.png",Color.red);
  FFSNetwork.IsClient=false;ready.Window.Show();ScenarioResultsReadyPark.Tick();Check(ScenarioResultsReadyPark.Claims(ready.gameObject),"owner-destroy witness holds a real results-ready receipt");
  Object.DestroyImmediate(windowRect.gameObject);ScenarioResultsReadyPark.Tick();Check(readyRect!=null&&readyRect.parent==readyHome&&readyRect.gameObject.activeInHierarchy,"native results owner destruction preserves the original pooled ready control at home");
  ScenarioResultsReadyPark.Reset();foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();Singleton<UIResultsManager>.Instance=null!;Singleton<UIReadyToggle>.Instance=null!;Singleton<HelpBox>.Instance=null!;
  return n;
 }
 static void Picture(Camera camera,string name,Color expected,RectTransform? footer=null){Canvas.ForceUpdateCanvases();var rt=new RenderTexture(512,256,24);rt.Create();camera.targetTexture=rt;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(512,256,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,512,256),0,0);image.Apply();File.WriteAllBytes(Path.Combine(evidence,name),image.EncodeToPNG());var p=image.GetPixel(256,128);Check(Vector3.Distance(new Vector3(p.r,p.g,p.b),new Vector3(expected.r,expected.g,expected.b))<.1f,"final head pixels follow the common picture and restored original");if(footer!=null){var at=camera.WorldToViewportPoint(footer.position);var ink=image.GetPixel(Mathf.RoundToInt(at.x*511),Mathf.RoundToInt(at.y*255));Check(ink.r>.8f&&ink.g>.8f&&ink.b<.2f,"the repaired original Continue footer has visible final head pixels, not only a live GameObject");}RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
}
