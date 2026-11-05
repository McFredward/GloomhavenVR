using System;
using System.IO;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object=UnityEngine.Object;
public static class ProgressionTests {
 private static int count;
 private static void Check(bool b,string m){count++;if(!b)throw new Exception(m);}
 public static int Run(string evidence){
 count=0;
 foreach(float zoom in new[]{.25f,1f,1.6092f,4f})Check(ScenarioArrivalScale.Resolve(zoom,1.6092f,true)==zoom,"ordinary arrival keeps saved zoom");
 foreach(float zoom in new[]{.1f,11.5f,12f,float.NaN,float.PositiveInfinity})Check(ScenarioArrivalScale.Resolve(zoom,1.6092f,true)==1.6092f,"extreme arrival restores a readable table");
 var oldRig=new GameObject("Map rig");var newRig=new GameObject("Scenario rig");var grabGate=new WorldGrab();grabGate.Begin(oldRig.transform);ComfortSettings.Writes=0;grabGate.Handoff(newRig.transform);Check(grabGate.Idle&&ComfortSettings.Writes==0,"cross-scene handoff cannot persist old zoom");Object.DestroyImmediate(oldRig);Object.DestroyImmediate(newRig);
 Check(ScenarioArrivalScale.Resolve(12f,1.6092f,false)==12f,"live and rebuilt rigs retain deliberate zoom");
 var dwell=new StoryRecoveryDwell();Check(!dwell.Tick(true,true,false,10)&&!dwell.Tick(true,true,false,12.99f),"no premature story recovery");
 Check(dwell.Tick(true,true,false,13),"far story recovers after continuous dwell");dwell.Recovered();
 Check(!dwell.Tick(true,true,false,100),"recovered story stays at its new reading spot");
 Check(!dwell.Tick(true,true,true,101)&&dwell.Tick(true,true,true,104),"new external displacement arms a fresh dwell");
 Check(!dwell.Tick(false,true,true,105)&&!dwell.Tick(true,true,true,106),"manual grip resets recovery dwell");
 Check(!dwell.Tick(true,false,true,110)&&!dwell.Tick(true,true,true,111),"returning to playfield resets recovery dwell");
 Check(!RestOfferCue.Wanted(false,true,false),"normal or unresolved hand is never called forced-rest");
 Check(RestOfferCue.Wanted(true,true,false),"insufficient hand accents allowed rest offers");
 Check(!RestOfferCue.Wanted(true,false,false)&&!RestOfferCue.Wanted(true,true,true),"unavailable or already chosen rest never glows");
 var cameraGo=new GameObject("Head");var head=cameraGo.AddComponent<Camera>();head.enabled=false;
 head.transform.position=new Vector3(0,6,-8);head.transform.rotation=Quaternion.identity;
 CanvasConversion.WorldCamera=head;VRRigDriver.HeadCamera=head;
 var frame=new GameObject("Original progression story frame");
 StoryFlight(head,frame,evidence);
 var driverGo=new GameObject("Network presentation");var driver=driverGo.AddComponent<NetAvatarDriver>();NetAvatarDriver.Setup(driver);
 GloomhavenVR.WorldUI.Surfaces.UseBarsSurface.Pending=false;NetAvatarDriver.Peer(2,true,42,0);Check(NetAvatarDriver.PollAt(1)==0&&NetAvatarDriver.PollAt(50)==0,"ordinary optional use bar cannot blame a player");NetAvatarDriver.Peer(2,true,42);
 Check(NetAvatarDriver.PollAt(10)==0&&NetAvatarDriver.PollAt(29.99f)==0,"single pending decision waits before reminding");
 Check(NetAvatarDriver.PollAt(30)==2,"single pending peer gets reminder");NetAvatarDriver.Peer(3,true,43);
 Check(NetAvatarDriver.PollAt(31)==0&&NetAvatarDriver.PollAt(60)==0,"multiple pending players never accuse one player");
 NetAvatarDriver.Peer(2,false,0);NetAvatarDriver.Peer(3,false,0);
 Check(NetAvatarDriver.PollAt(61)==0,"answered decision clears reminder immediately");
 var toggleGo=new GameObject("Original ready toggle boundary");var toggle=toggleGo.AddComponent<UIReadyToggle>();Singleton<UIReadyToggle>.Instance=toggle;
 toggle.IsVisible=true;toggle.ReadyUpType=UIReadyToggle.EReadyUpType.Player;FFSNet.PlayerRegistry.Participants.Clear();FFSNet.PlayerRegistry.Participants.Add(1);FFSNet.PlayerRegistry.Participants.Add(2);toggle.PlayersReady.Add(1);
 Check(NetAvatarDriver.PollAt(70)==0&&NetAvatarDriver.PollAt(90)==2,"last native unready participant gets the same reminder");toggle.PlayersReady.Add(2);
 Check(NetAvatarDriver.PollAt(91)==0,"native readiness completion removes reminder");
 toggle.PlayersReady.Clear();Check(NetAvatarDriver.PollAt(100)==0&&NetAvatarDriver.PollAt(130)==0,"two unready participants do not identify a single blocker");
 // Render the actual new guidance geometry. Ports do not create or position this geometry.
 head.transform.position=new Vector3(0,0,-1);head.orthographic=true;head.orthographicSize=.20f;head.aspect=2f;head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;head.cullingMask=GloomhavenVR.Core.VRLayers.ModLayerMask;
 var cueRoot=new GameObject("Rest button geometry witness");cueRoot.transform.position=new Vector3(-.13f,0,0);
 var cue=new RestOfferCue();cue.Tick(cueRoot.transform,new Vector2(.1f,.1f),true);
 var arrowRoot=new GameObject("Steam identity witness");arrowRoot.transform.position=new Vector3(.13f,-.05f,0);
 var arrow=new DecisionWaitArrow();Check(arrow.Tick(arrowRoot.transform,Vector3.zero,true),"waiting arrow geometry built lazily");
 foreach(var r in arrowRoot.GetComponentsInChildren<Renderer>())Check(r.gameObject.layer==GloomhavenVR.Core.VRLayers.ModLayer,"arrow uses the actual dedicated mod layer");
 int visible=Capture(head,Path.Combine(evidence,"rest-and-wait-cues.png"));Check(visible>150,"actual guidance pixels visible");
 Check(cueRoot.GetComponentsInChildren<Collider>(true).Length==0&&arrowRoot.GetComponentsInChildren<Collider>(true).Length==0,"guidance never steals clicks or grabs");
 cue.Tick(cueRoot.transform,new Vector2(.1f,.1f),false);arrow.Tick(arrowRoot.transform,Vector3.zero,false);
 Check(Capture(head,Path.Combine(evidence,"cues-cleared.png"))==0,"completed or unavailable choices leave no residual cue pixels");
 cue.Destroy();arrow.Destroy();foreach(var go in new[]{frame,driverGo,toggleGo,cueRoot,arrowRoot,cameraGo})Object.DestroyImmediate(go);
 return count;
 }
 private static void StoryFlight(Camera head,GameObject frame,string evidence){
 var wp=ModalFallback.Add(frame.transform);frame.transform.position=new Vector3(500,0,0);
 SharedWindowMoves.Setup(ModalFallback.Kind);SharedWindowSpawnBridge.IsInitialAuthor=false;
 ModalFallback.PollAt(10);ModalFallback.PollAt(20);
 Check(wp.Grab!.Moves==0&&wp.StoryRecoveryFlight==null,"observer cannot recover another participant's story");
 SharedWindowSpawnBridge.IsInitialAuthor=true;ModalFallback.PollAt(21);ModalFallback.PollAt(23.9f);
 Check(wp.Grab.Moves==0,"coordinator honors three-second dwell");
 SharedWindowMoves.MayBegin=false;ModalFallback.PollAt(25);
 Check(wp.StoryRecoveryFlight==null&&wp.Grab.Moves==0,"held remote claim prevents recovery");
 SharedWindowMoves.MayBegin=true;ModalFallback.PollAt(26);
 Check(wp.StoryRecoveryFlight!=null&&wp.Grab.Moves==0&&SharedWindowMoves.Claims==1,
  "single author starts a flight without teleporting the native story");
 Check(!ModalFallback.FastAt(26),"no fast publication before the first actual picture sample");
 int reads=SpawnRing.Reads;ModalFallback.FrameAt(26.02f);
 Check(wp.Grab.Moves==1,"story flight samples before the four-Hz admission poll");
 Check(ModalFallback.FastAt(26.02f),"active story flight requests the existing fast send gate");
 Vector3 previous=frame.transform.position;
 for(int i=2;i<=9;i++){
  ModalFallback.FrameAt(26+i*.033f);
  Check(frame.transform.position.x<previous.x&&frame.transform.position.x>0,
   "story recovery has a visible finite intermediate pose");
  Check(wp.Window.IsOpen&&wp.StoryRecoveryFlight!=null,"animation never dismisses native story");
  SharedWindowMoves.Deliver(26+i*.033f);previous=frame.transform.position;
 }
 Check(SpawnRing.Reads==reads,"active story flight does not rewalk the native playfield");
 // A real rendered mesh and its Unity collider share the same animated frame. These
 // witness pixels exercise the coordinator's sampled geometry, not native story text.
 var ink=GameObject.CreatePrimitive(PrimitiveType.Quad);ink.name="Story flight picture/hit witness";
 ink.transform.SetParent(frame.transform,false);ink.transform.localScale=new Vector3(1,.4f,1);
 var material=new Material(Shader.Find("GloomhavenVR/Overlay"));material.color=new Color(1,.8f,.2f,1);
 ink.GetComponent<Renderer>().sharedMaterial=material;
 head.transform.position=frame.transform.position-Vector3.forward*2;head.transform.rotation=Quaternion.identity;
 head.orthographic=true;head.orthographicSize=.5f;head.aspect=2;head.clearFlags=CameraClearFlags.SolidColor;head.backgroundColor=Color.black;head.cullingMask=~0;
 Check(Capture(head,Path.Combine(evidence,"story-flight-middle.png"))>500,"story flight retains actual visible picture pixels");
 Physics.SyncTransforms();
 Check(ink.GetComponent<Collider>().bounds.Contains(frame.transform.position),"story flight hit geometry follows the visible sample");
 head.transform.position=new Vector3(-100,80,-100); // target must not follow a moving host
 ModalFallback.FrameAt(26.7f);
 Check(Vector3.Distance(frame.transform.position,new Vector3(0,6,1.5f))<.001f&&wp.Window.IsOpen,
  "recovery ends at its captured host target without changing native continuation");
 Check(ModalFallback.FastAt(26.7f)&&ModalFallback.FastAt(26.8f),"final story endpoint retains two rig intervals to publish");
 Check(!ModalFallback.FastAt(26.84f),"finite publication tail returns to idle cadence");
 int moves=wp.Grab.Moves;ModalFallback.PollAt(35);Check(wp.Grab.Moves==moves,"ordinary readable window remains fixed");
 Object.DestroyImmediate(ink);Object.DestroyImmediate(material);
 // The real consumer track renders between actual automatic claim samples, then
 // reaches the exact source pose; transport/anchor conversion remains its own suite.
 var track=new SharedWindowPoseTrack();var a=new RigPose{Position=Vector3.zero,Rotation=Quaternion.identity};
 var b=new RigPose{Position=Vector3.one,Rotation=Quaternion.Euler(0,35,0)};track.Seed(a,1,1);
 track.Sample(b,1,1.1f,out _);RigPose mid=track.Sample(b,1,1.15f,out float size);
 Check(mid.Position.x>0&&mid.Position.x<1&&size==1,"actual remote pose track interpolates a flight sample");
 Check(Vector3.Distance(track.Sample(b,1,1.3f,out _).Position,b.Position)<.0001f,
  "actual remote pose track reaches the exact offered flight endpoint");
 // Each interruption starts a fresh opening-scoped flight, then changes one live
 // authority/lifetime input after a visible sample. No path releases a user's grab.
 for(int scenario=0;scenario<9;scenario++){
  head.transform.position=new Vector3(0,6,-8);head.transform.rotation=Quaternion.identity;
  frame.transform.position=new Vector3(500,0,0);ModalFallback.Kind=SharedWindowKind.ScenarioStory;
  ModalFallback.Reflow=false;SharedWindowSpawnBridge.IsInitialAuthor=true;FFSNetwork.IsOnline=true;
  SharedWindowMoves.Setup(ModalFallback.Kind);wp=ModalFallback.Add(frame.transform);
  float now=50+scenario*10;ModalFallback.PollAt(now);ModalFallback.PollAt(now+3);ModalFallback.FrameAt(now+3.1f);
  Check(wp.StoryRecoveryFlight!=null,"interruption fixture starts with a live sampled flight");
  Vector3 stopped=frame.transform.position;int before=wp.Grab!.Moves;
  if(scenario==0)wp.Grab.IsGrabbed=true;
  if(scenario==1)SharedWindowMoves.RemoteGrab(now+3.11f);
  if(scenario==2)ModalFallback.Reflow=true;
  if(scenario==3)wp.Window.IsOpen=false;
  if(scenario==4)wp.Panel=new ConvertedPanel{HostGo=frame};
  if(scenario==5)SharedWindowMoves.Owner.Open(88,1);
  if(scenario==6)SharedWindowSpawnBridge.IsInitialAuthor=false;
  if(scenario==7)wp.Panel.RenderHidden=true;
  if(scenario==8)SharedWindowMoves.Owner.SetAutomaticAuthor(2,now+3.11f);
  string[] reasons={"local grip interrupts flight at its last visible sample","new remote grip interrupts the actual automatic claim","opening room-making wins over an old recovery flight","native close stops flight without reopening the window","replaced presentation cancels the previous flight","new native opening retires the previous automatic flight","placement author loss cancels flight","hidden native story cancels flight","automatic authority transfer cancels flight"};
  Check(!ModalFallback.FastAt(now+3.11f),reasons[scenario]+"; manual/native/authority interruption revokes fast publication");
  ModalFallback.FrameAt(now+3.12f);
  Check(wp.StoryRecoveryFlight==null&&wp.Grab.Moves==before&&frame.transform.position==stopped,reasons[scenario]);
  if(scenario==1)Check(SharedWindowMoves.Observer.LocalPhase==SharedWindowMovePhase.Held,
   "flight cancellation does not release the winning remote manual grip");
 }
 // Native flat/unmodded transport may have no ordered move register. Keep the
 // original single-author rescue available, but stop on a session/transport handover.
 for(int mode=0;mode<5;mode++){
  head.transform.position=new Vector3(0,6,-8);head.transform.rotation=Quaternion.identity;
  frame.transform.position=new Vector3(500,0,0);ModalFallback.Reflow=false;SharedWindowSpawnBridge.IsInitialAuthor=true;
  SharedWindowMoves.Setup(ModalFallback.Kind);FFSNetwork.IsOnline=mode!=1&&mode!=4;
  SharedWindowMoves.Active=mode==3;wp=ModalFallback.Add(frame.transform);
  float now=170+mode*10;ModalFallback.PollAt(now);ModalFallback.PollAt(now+3);ModalFallback.FrameAt(now+3.1f);
  Check(wp.StoryRecoveryFlight!=null,"original single-author recovery survives disabled transport or offline mode");
  int before=wp.Grab!.Moves;Vector3 stopped=frame.transform.position;
  if(mode==2)SharedWindowMoves.Active=true;
  if(mode==3)SharedWindowMoves.Active=false;
  if(mode==4)FFSNetwork.IsOnline=true;
  ModalFallback.FrameAt(now+3.7f);
  if(mode<2)Check(wp.StoryRecoveryFlight==null&&Vector3.Distance(frame.transform.position,new Vector3(0,6,1.5f))<.001f,
   "offline and unmodded presentation still complete the original rescue");
  else Check(wp.StoryRecoveryFlight==null&&wp.Grab.Moves==before&&frame.transform.position==stopped,
   mode==2?"transport activation cancels an unclaimed old flight":"session or transport handover yields the current visible pose");
 }
 FFSNetwork.IsOnline=true;SharedWindowMoves.Active=true;
 ModalFallback.Reflow=false;SharedWindowSpawnBridge.IsInitialAuthor=true;
 wp.Window.IsOpen=false;int priorReads=SpawnRing.Reads;ModalFallback.PollAt(160);ModalFallback.PollAt(170);
 Check(SpawnRing.Reads==priorReads,"ordinary play never scans a story footprint");
 }
 private static int Capture(Camera c,string path){var rt=new RenderTexture(800,400,24);c.targetTexture=rt;c.Render();RenderTexture.active=rt;
 var tex=new Texture2D(800,400,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,800,400),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());int n=0;
 foreach(var p in tex.GetPixels32())if(p.r>60&&p.g>40)n++;RenderTexture.active=null;c.targetTexture=null;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);return n;}
}
