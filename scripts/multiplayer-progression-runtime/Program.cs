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
 Check(!RestOfferCue.Wanted(2,true,false)&&!RestOfferCue.Wanted(-1,true,false),"normal or unresolved hand is never called forced-rest");
 Check(RestOfferCue.Wanted(0,true,false)&&RestOfferCue.Wanted(1,true,false),"insufficient hand accents allowed rest offers");
 Check(!RestOfferCue.Wanted(0,false,false)&&!RestOfferCue.Wanted(0,true,true),"unavailable or already chosen rest never glows");
 var cameraGo=new GameObject("Head");var head=cameraGo.AddComponent<Camera>();head.enabled=false;
 head.transform.position=new Vector3(0,6,-8);head.transform.rotation=Quaternion.identity;
 CanvasConversion.WorldCamera=head;VRRigDriver.HeadCamera=head;
 var frame=new GameObject("Original progression story frame");frame.transform.position=new Vector3(500,0,0);
 var wp=ModalFallback.Add(frame.transform);SharedWindowMoves.Claims=0;SharedWindowMoves.MayBegin=true;
 SharedWindowSpawnBridge.IsInitialAuthor=false;ModalFallback.PollAt(10);ModalFallback.PollAt(20);
 Check(wp.Grab!.Moves==0,"observer cannot recover another participant's story");
 SharedWindowSpawnBridge.IsInitialAuthor=true;ModalFallback.PollAt(21);ModalFallback.PollAt(23.9f);
 Check(wp.Grab.Moves==0,"coordinator honors three-second dwell");
 SharedWindowMoves.MayBegin=false;ModalFallback.PollAt(25);
 Check(wp.Grab.Moves==0,"held remote claim prevents recovery");
 SharedWindowMoves.MayBegin=true;ModalFallback.PollAt(26);
 Check(wp.Grab.Moves==1&&SharedWindowMoves.Claims==1,"single author claims and restores lost story");
 Check(Vector3.Distance(frame.transform.position,new Vector3(0,6,1.5f))<.001f&&wp.Window.IsOpen,"recovery is in front of host and never closes or confirms native story");
 ModalFallback.PollAt(35);Check(wp.Grab.Moves==1,"ordinary readable window remains fixed");
 frame.transform.position=new Vector3(500,0,0);wp.Grab.IsGrabbed=true;ModalFallback.PollAt(40);ModalFallback.PollAt(50);
 Check(wp.Grab.Moves==1,"local manual grip always wins");wp.Grab.IsGrabbed=false;wp.Window.IsOpen=false;int priorReads=SpawnRing.Reads;ModalFallback.PollAt(60);ModalFallback.PollAt(70);Check(SpawnRing.Reads==priorReads,"ordinary play never scans a story footprint");
 Check(wp.Grab.Moves==1,"completed native story cannot be resurrected");
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
 private static int Capture(Camera c,string path){var rt=new RenderTexture(800,400,24);c.targetTexture=rt;c.Render();RenderTexture.active=rt;
 var tex=new Texture2D(800,400,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,800,400),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());int n=0;
 foreach(var p in tex.GetPixels32())if(p.r>60&&p.g>40)n++;RenderTexture.active=null;c.targetTexture=null;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);return n;}
}
