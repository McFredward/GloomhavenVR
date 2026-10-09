using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Object=UnityEngine.Object;

// External boundary: known tracked controller samples. All board construction,
// placement, lifecycle, config-row construction, wire and remote furniture below
// are invoked from the private complete current-source production DLL.
public sealed class WristHarness : MonoBehaviour
{
    public static Action<int> Exit;
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    public static string Phase="Awaiting runtime Start";
    Assembly mod; string output; readonly List<string> checks=new List<string>();int controls;
    Transform rig,anchor;Camera head;object left,right,tray,factory;AssetBundle bundle;
    Type T(string name)=>mod.GetType("GloomhavenVR."+name,true);
    object Get(object obj,string name){for(Type t=obj as Type??obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,All);if(f!=null)return f.GetValue(obj is Type?null:obj);var p=t.GetProperty(name,All);if(p!=null)return p.GetValue(obj is Type?null:obj);}throw new Exception("Missing "+name);}
    void Set(object obj,string name,object value){for(Type t=obj as Type??obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,All);if(f!=null){f.SetValue(obj is Type?null:obj,value);return;}var p=t.GetProperty(name,All);if(p!=null){p.SetValue(obj is Type?null:obj,value);return;}}throw new Exception("Missing "+name);}
    object Call(object obj,string name,params object[] args){Type t=obj as Type??obj.GetType();foreach(var m in t.GetMethods(All).Where(x=>x.Name==name&&x.GetParameters().Length>=args.Length)){var p=m.GetParameters();if(p.Take(args.Length).Where((x,i)=>args[i]!=null&&!x.ParameterType.IsInstanceOfType(args[i])&&!x.ParameterType.IsByRef).Any()||p.Skip(args.Length).Any(x=>!x.IsOptional))continue;object[] values=new object[p.Length];Array.Copy(args,values,args.Length);for(int i=args.Length;i<p.Length;i++)values[i]=p[i].DefaultValue;try{return m.Invoke(obj is Type?null:obj,values);}catch(TargetInvocationException e){throw new Exception(t+"."+name,e.InnerException);}}throw new Exception("Missing method "+t+"."+name);}
    object New(string name,params object[] args)=>Activator.CreateInstance(T(name),BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,args,null);
    object EnumValue(string type,string name)=>Enum.Parse(T(type),name);
    void Check(bool yes,string label){if(!yes)throw new Exception(label);checks.Add(label);Phase=label;File.AppendAllText(Path.Combine(output,"live-checks.log"),checks.Count+" "+label+"\n");}
    void Near(Vector3 a,Vector3 b,string label)=>Check(Vector3.Distance(a,b)<.002f,label+" ("+Vector3.Distance(a,b)+")");
    void Rotation(Quaternion a,Quaternion b,string label)=>Check(Quaternion.Angle(a,b)<.04f,label+" ("+Quaternion.Angle(a,b)+")");
    void Value(string key,object value)=>Set(Get(T("Cards.CardsConfig"),key),"Value",value);
    Transform Root=>(Transform)Get(tray,"Root");
    IEnumerator Start()
    {
        var arguments=Environment.GetCommandLineArgs();output=arguments[Array.IndexOf(arguments,"-evidenceRoot")+1];mod=Assembly.Load("GloomhavenVR");IEnumerator sequence=Proof();
        while(true){bool more;object next=null;try{more=sequence.MoveNext();if(more)next=sequence.Current;}catch(Exception error){Finish(false,error.ToString());yield break;}if(!more)break;yield return next;}
        Finish(true,"");
    }
    IEnumerator Proof()
    {
        var logType=Assembly.Load("BepInEx").GetType("BepInEx.Logging.ManualLogSource",true);Call(T("Core.VRLog"),"Init",Activator.CreateInstance(logType,new object[]{"Wrist board proof"}));
        var paths=Assembly.Load("BepInEx").GetType("BepInEx.Paths",true);Call(paths,"SetExecutablePath",Path.Combine(output,"Gloomhaven.exe"));Directory.CreateDirectory((string)Get(paths,"ConfigPath"));
        object file=Call(T("Core.ModuleConfig"),"Create","wrist-proof-input");BindPlugin(file,"PrimaryHand",typeof(string),"Right");BindPlugin(file,"DevMode",typeof(bool),false);BindPlugin(file,"ScrollWithStickOnly",typeof(bool),true);
        Call(T("Cards.CardsConfig"),"Bind");Call(T("WorldUI.WorldUIConfig"),"Bind");Call(T("WorldUI.ButtonTuning"),"Bind");Call(T("Hands.HandsConfig"),"Bind");Call(T("Net.NetModule"),"BindConfig");
        bundle=AssetBundle.LoadFromFile(Path.Combine(output,"original-board-bundle/gloomhavenvr-wrist-proof"));Check(bundle!=null,"original three board prefabs imported and bundled for actual Unity editor");
        Set(T("WorldUI.NativeButtonSkin"),"_sampled",true);Set(T("WorldUI.NativeButtonSkin"),"_font",TMP_Settings.defaultFontAsset);
        rig=new GameObject("Tracked rig boundary").transform;rig.localScale=Vector3.one*3.2f;rig.position=new Vector3(8,1,6);rig.rotation=Quaternion.Euler(0,28,0);
        anchor=new GameObject("Original cards anchor").transform;anchor.SetParent(rig,false);
        head=new GameObject("Tracked headset boundary").AddComponent<Camera>();head.gameObject.tag="MainCamera";head.transform.SetParent(rig,false);head.transform.localPosition=new Vector3(0,1.6f,-.7f);
        Set(T("Rig.VRRigDriver"),"RigRoot",rig);Set(T("Rig.VRRigDriver"),"HeadCamera",head);Set(T("Rig.VRRigDriver"),"BaseWorldScale",3.2f);
        left=Hand("Left",new Vector3(-.25f,1.1f,-.3f));right=Hand("Right",new Vector3(.25f,1.1f,-.3f));Call(T("Hands.VRHands"),"Set",left,right);
        factory=New("Cards.VRCardFactory");Set(factory,"_bundle",bundle);tray=New("Cards.PlayTray");Call(tray,"EnsureBuilt",factory,anchor);Call(tray,"SetVisible",true);Call(tray,"PlaceAtHead");
        Check(Root.Find("TrayVisual")!=null,"complete original EnsureBuilt uses the real authored board prefab, not a mock or procedural replacement");
        Transform[] mounts=new[]{(Transform)Get(tray,"InitiativeMount"),(Transform)Get(tray,"ObjectivesMount"),(Transform)Get(tray,"ElementMount"),(Transform)Get(tray,"DecisionMount")};
        Check(mounts.All(x=>x!=null&&x.IsChildOf(Root)),"all original docks remain under the same board root");
        Vector3[] local=mounts.Select(x=>Root.InverseTransformPoint(x.position)).ToArray();
        Vector3 originalPosition=Root.position;Quaternion originalRotation=Root.rotation;float originalScale=Root.lossyScale.x;
        Value("WristBoardEnabled",true);Call(tray,"TickPlacement");
        Check((bool)Get(Get(T("Cards.CardsConfig"),"TrayFollow"),"Value"),"enabling wrist mode preserves stored follow preference");
        Check((bool)Get(tray,"WristControlsHidden"),"wrist mode has owner-authored hidden controls immediately");Hidden(true);
        yield return Settle();
        NativeState("first settled attachment");PoseProof(left,"non-main left attachment");
        for(int i=0;i<12;i++){
            Transform w=Wrist(left);w.localPosition+=new Vector3(.011f,.003f,0);w.localRotation=Quaternion.Euler(90+i*2,i*3,0);
            Call(tray,"TickPlacement");PoseProof(left,"tracked Update pose "+i);
            for(int j=0;j<mounts.Length;j++)Near(local[j],Root.InverseTransformPoint(mounts[j].position),"unchanged dock geometry "+i+"/"+j);
            yield return null;
        }
        // Full original late component reacts without calling the new helper from the fixture.
        Wrist(left).localPosition+=Vector3.right*.04f;yield return null;yield return null;PoseProof(left,"actual MonoBehaviour LateUpdate reads the latest wrist before drawing");
        var late=Root.GetComponent(T("Cards.PlayTray+BoardReFaceLateTick")) as Behaviour;Check(late!=null,"actual production late/before-render component is installed on the original board");
        late.enabled=false;Vector3 pausedRoot=Root.position;Wrist(left).localPosition+=Vector3.right*.06f;yield return null;yield return null;Control(Vector3.Distance(pausedRoot,Root.position)<.001f,"disabling original late/before-render component prevents automatic pose delivery");late.enabled=true;yield return null;yield return null;PoseProof(left,"reenabling original late component restores genuine automatic wrist motion");
        Transform handRoot=(Transform)Get(Get(left,"Rig"),"Root");handRoot.localScale=Vector3.one*.4f;
        Call(tray,"TickPlacement");Check(Mathf.Abs(Root.lossyScale.x-(float)Call(T("Cards.PlayTray"),"ComputeBoardScale",Get(T("Cards.CardsConfig"),"CurrentBoard"))*anchor.lossyScale.x*.5f)<.002f,"hand visual scale never multiplies the original board size (live "+Root.lossyScale.x+", baseline "+originalScale+")");
        object controllerLesson=New("Compat.ControllerVisual",left);Check(handRoot.GetComponentsInChildren<Renderer>(true).Any(),"actual authored glove renderers are available to the original tutorial visibility path");Call(controllerLesson,"HideHand");Check(handRoot.GetComponentsInChildren<Renderer>(true).All(x=>!x.enabled),"original controller-only lesson hides renderer art without removing tracked wrist socket");Wrist(left).localPosition+=Vector3.up*.03f;yield return null;yield return null;PoseProof(left,"controller-only tutorial keeps a valid wrist source while hand art is hidden");Call(controllerLesson,"ShowHand");
        // Actual hand-style teardown/rebuild replaces the wrist socket. The original board
        // is neither parented below the outgoing hand nor destroyed with its mesh.
        Vector3 savedHand=((Component)left).transform.localPosition;Transform oldWrist=Wrist(left);
        Call(T("Hands.VRHands"),"Set",null,right);Call(tray,"TickPlacement");Object.DestroyImmediate(((Component)left).gameObject);
        left=Hand("Left",savedHand,"Plate");Call(T("Hands.VRHands"),"Set",left,right);Call(tray,"TickPlacement");yield return Settle();PoseProof(left,"actual authored Plate wrist socket after complete tracked-hand replacement");Check(oldWrist==null&&Root!=null&&Root.parent==anchor,"hand-style teardown cannot destroy or strand the original board");
        Value("WristBoardHand",EnumValue("Cards.WristBoardHand","Right"));Call(tray,"TickPlacement");yield return Settle();PoseProof(right,"explicit right attachment");
        Value("WristBoardHand",EnumValue("Cards.WristBoardHand","NonMain"));Set(Get(T("Plugin"),"PrimaryHand"),"Value","Left");Call(tray,"TickPlacement");yield return Settle();PoseProof(right,"non-main follows main-controller swap");
        Set(Get(T("Plugin"),"PrimaryHand"),"Value","Right");Call(tray,"TickPlacement");yield return Settle();PoseProof(left,"main-controller return follows left wrist");
        Value("WristBoardOffsetMeters",new Vector3(.04f,-.13f,-.07f));Value("WristBoardAnglesDegrees",new Vector3(11,25,65));Call(tray,"TickPlacement");PoseProof(left,"advanced offset and all angles apply to the real root");
        Set(left,"IsTracked",false);Vector3 held=Root.localPosition;Quaternion heldRot=Root.localRotation;
        Wrist(left).localPosition+=Vector3.one*6f;rig.position+=Vector3.one*.7f;Call(tray,"TickPlacement");Near(held,Root.localPosition,"tracking loss holds last valid rig-local board rather than following zero/outlier pose");Rotation(heldRot,Root.localRotation,"tracking loss holds valid orientation through recenter");
        Wrist(left).localPosition-=Vector3.one*6f;Set(left,"IsTracked",true);Call(tray,"TickPlacement");PoseProof(left,"valid tracking resumes same wrist");
        rig.localScale=Vector3.one*7.5f;rig.rotation=Quaternion.Euler(0,65,0);Call(tray,"TickPlacement");PoseProof(left,"world scaling and snap/recenter rotations preserve real-metre wrist offsets");
        Check(Root.parent==anchor,"same rig parent survives every wrist pose, never visual parent");
        Render("original-wrist-board.png");MenuProof();yield return CuratedMenuProof();RemoteProof();
        // Inverted control ownership is detectable in actual presentation nodes.
        var follow=(Transform)Get(tray,"_followAnchor");follow.gameObject.SetActive(true);Control(follow.GetComponentsInChildren<Collider>(false).Any(),"a wrongly retained original follow collider is visible to the negative control");follow.gameObject.SetActive(false);
        float beforeScale=Root.lossyScale.x;Root.localScale*=.4f;Control(Mathf.Abs(Root.lossyScale.x-beforeScale)>.1f,"inheriting hand-art scale is rejected by actual root-size measurement");Root.localScale/=.4f;
        // Simulate the actual HandsRoot removal used by HandsDriver style rebuild: the
        // complete original board and both hand trees disappear, while the durable normal
        // seat must survive and the same tray rebuilds under the replacement anchor.
        Transform durableSeat=(Transform)Get(tray,"_wristReturnSeat");Call(T("Hands.VRHands"),"Set",null,null);Object.DestroyImmediate(anchor.gameObject);
        anchor=new GameObject("Replacement original hands/cards anchor").transform;anchor.SetParent(rig,false);
        left=Hand("Left",new Vector3(-.25f,1.1f,-.3f));right=Hand("Right",new Vector3(.25f,1.1f,-.3f));Call(T("Hands.VRHands"),"Set",left,right);Call(tray,"EnsureBuilt",factory,anchor);Call(tray,"SetVisible",true);yield return Settle();Call(tray,"TickPlacement");
        Check(durableSeat!=null&&ReferenceEquals(durableSeat,Get(tray,"_wristReturnSeat")),"actual hands-root destruction cannot destroy the durable remembered FOLLOW seat");PoseProof(left,"replacement hands anchor resumes same genuine wrist root pathway");Hidden(true);
        // Return to remembered FOLLOW under the CURRENT rig, not old raw world space.
        Transform returnSeat=(Transform)Get(tray,"_wristReturnSeat");float previousNormal=returnSeat.localScale.x;float configuredBefore=(float)Call(T("Cards.PlayTray"),"ComputeBoardScale",Get(T("Cards.CardsConfig"),"CurrentBoard"));
        Value("TrayScale",1.6f);Call(tray,"ReapplyOrientation");float configuredAfter=(float)Call(T("Cards.PlayTray"),"ComputeBoardScale",Get(T("Cards.CardsConfig"),"CurrentBoard"));
        Check(Mathf.Abs(returnSeat.localScale.x-previousNormal*configuredAfter/configuredBefore)<.00001f,"ordinary size edit while attached updates remembered size without recording wrist pose");
        Check(Mathf.Abs(Root.localScale.x-configuredAfter*.5f)<.00001f,"ordinary size edit applies uniformly to actual attached root");
        Vector3 returnPosition=returnSeat.position;Quaternion returnRot=returnSeat.rotation;Vector3 returnScale=Vector3.one*(returnSeat.lossyScale.x/anchor.lossyScale.x);
        Value("WristBoardEnabled",false);Call(tray,"TickPlacement");
        Transform exitSeat=(Transform)Get(tray,"_wristReturnSeat");Call(T("Hands.VRHands"),"Set",null,null);Object.DestroyImmediate(anchor.gameObject);
        anchor=new GameObject("Mid-exit replacement original hands anchor").transform;anchor.SetParent(rig,false);left=Hand("Left",new Vector3(-.25f,1.1f,-.3f));right=Hand("Right",new Vector3(.25f,1.1f,-.3f));Call(T("Hands.VRHands"),"Set",left,right);Call(tray,"EnsureBuilt",factory,anchor);Call(tray,"SetVisible",true);Check(exitSeat!=null,"actual hands-root destruction during return cannot destroy its target");
        yield return Settle();Call(tray,"TickPlacement");
        Check(!(bool)Get(tray,"WristControlsHidden"),"ordinary mode restores the complete board controls after the short return");Hidden(false);Near(returnPosition,Root.position,"ordinary mode restores saved seat carried through current rig frame");Rotation(returnRot,Root.rotation,"ordinary mode restores saved rotation");Near(returnScale,Root.localScale,"ordinary mode restores saved uniform size");
        // Original pinned mode is kept as the other legal remembered mode.
        Call(tray,"ToggleFollow");Check(!(bool)Get(Get(T("Cards.CardsConfig"),"TrayFollow"),"Value"),"original board button still changes ordinary follow/fixed mode");
        Vector3 fixedPosition=Root.position;Quaternion fixedRotation=Root.rotation;float fixedWorldSize=Root.lossyScale.x;
        Value("WristBoardEnabled",true);Call(tray,"TickPlacement");yield return Settle();
        rig.position+=new Vector3(3,1,-2);rig.rotation=Quaternion.Euler(0,133,0);rig.localScale*=.45f;Call(tray,"TickPlacement");
        Value("WristBoardEnabled",false);Call(tray,"TickPlacement");yield return Settle();Call(tray,"TickPlacement");
        Check(!((bool)Get(Get(T("Cards.CardsConfig"),"TrayFollow"),"Value"))&&Root.parent!=anchor,"wrist exit restores original fixed mode and world holder without rewriting its key");
        Near(fixedPosition,Root.position,"remembered fixed world seat is unaffected by rig locomotion and zoom while attached");Rotation(fixedRotation,Root.rotation,"remembered fixed rotation is unaffected by snap turn while attached");Check(Mathf.Abs(Root.lossyScale.x-fixedWorldSize)<.0001f,"remembered fixed world size is unaffected by rig zoom while attached");
        // Same original pin carry policy: only an explicit RigPoseVersion change carries
        // the remembered fixed seat, using the previous stable rig-relative cache.
        Value("WristBoardEnabled",true);Call(tray,"TickPlacement");yield return Settle();
        Transform fixedSeat=(Transform)Get(tray,"_wristReturnSeat");Vector3 fixedLocal=rig.InverseTransformPoint(fixedSeat.position);Quaternion fixedLocalRot=Quaternion.Inverse(rig.rotation)*fixedSeat.rotation;
        rig.position+=new Vector3(-2,.2f,1);rig.rotation=Quaternion.Euler(0,24,0);Set(T("Rig.VRRigDriver"),"RigPoseVersion",(int)Get(T("Rig.VRRigDriver"),"RigPoseVersion")+1);Call(tray,"TickPlacement");
        Near(rig.TransformPoint(fixedLocal),fixedSeat.position,"genuine original FollowPinAnchor carries remembered fixed seat through an intentional tracking-origin change");Rotation(rig.rotation*fixedLocalRot,fixedSeat.rotation,"original tracking-origin carry preserves remembered fixed orientation");
        // Rebuild full original board for each style while attached. Return frame stays
        // independent of the attached pose; never capture wrist position as ordinary seat.
        foreach(string style in new[]{"Steel","Bronze","Oak"}){
            Vector3 old=Root.position;Quaternion rot=Root.rotation;Vector3 scale=Root.localScale;
            Call(tray,"PrepareWristRebuild");Call(tray,"Destroy");Value("Board",EnumValue("Cards.ControlBoard",style));Call(tray,"EnsureBuilt",factory,anchor);Call(tray,"RestorePose",old,rot,scale);Call(tray,"SetVisible",true);yield return Settle();
            Check(Root.Find("TrayVisual")!=null,"real "+style+" board asset retained on wrist-style rebuild");PoseProof(left,style+" same tracked wrist after complete Destroy/EnsureBuilt/RestorePose");Hidden(true);
        }
        // Original board rebuild while a wrist EXIT is in flight must resume that exit,
        // never treat its captured intermediate wrist pose as the ordinary seat.
        Vector3 remembered=((Transform)Get(tray,"_wristReturnSeat")).position;Value("WristBoardEnabled",false);Call(tray,"TickPlacement");
        Vector3 intermediate=Root.position;Quaternion intermediateRot=Root.rotation;Vector3 intermediateScale=Root.localScale;
        Call(tray,"PrepareWristRebuild");Call(tray,"Destroy");Value("Board",EnumValue("Cards.ControlBoard","Steel"));Call(tray,"EnsureBuilt",factory,anchor);Call(tray,"RestorePose",intermediate,intermediateRot,intermediateScale);Call(tray,"SetVisible",true);
        Check((bool)Get(tray,"WristControlsHidden"),"style rebuild preserves hidden controls and exit intent while original return is in flight");Hidden(true);
        yield return Settle();Call(tray,"TickPlacement");Near(remembered,Root.position,"style rebuild during exit finishes at remembered ordinary seat rather than stranding at intermediate wrist pose");Hidden(false);
        // No former ordinary seat: user can enable mode before the first scene/hand exists.
        Call(tray,"Destroy");Value("WristBoardEnabled",true);tray=New("Cards.PlayTray");Call(tray,"EnsureBuilt",factory,anchor);Call(tray,"SetVisible",true);yield return Settle();
        Vector3 unplacedPose=Root.position;head.transform.localPosition=Vector3.zero;Value("WristBoardEnabled",false);Call(tray,"TickPlacement");yield return null;Call(tray,"TickPlacement");
        Check(Get(tray,"_wristReturnSeat")==null&&!(bool)Get(tray,"_placed"),"unplaced wrist exit with untracked head never captures wrist/default pose as ordinary seat");Near(unplacedPose,Root.position,"untracked head safely holds last valid root while exit waits for genuine native placement");
        head.transform.localPosition=new Vector3(0,1.6f,-.7f);Call(tray,"TickPlacement");yield return Settle();Call(tray,"TickPlacement");Check((bool)Get(tray,"IsVisible")&&!(bool)Get(tray,"WristControlsHidden"),"wrist selected before initial placement returns to genuine native head seat and remains playable");Hidden(false);
        Render("ordinary-board-restored.png");Value("WristBoardEnabled",true);Call(tray,"TickPlacement");Call(tray,"Destroy");Check(Get(tray,"_wristReturnSeat")==null&&Get(Get(tray,"_wristReturnAnchor"),"Holder")==null,"genuine module teardown releases remembered seat and its invisible pin holder");
    }
    // Let actual game frames advance through the original shared blend duration.
    // A realtime wait may elapse inside the first asset-import/render frame while
    // Unity unscaledTime has not advanced; that is not settled production animation.
    IEnumerator Settle(){float until=Time.unscaledTime+.25f;for(int frames=0;frames<3||Time.unscaledTime<until;frames++)yield return null;}
    void NativeState(string label){var late=Root.GetComponent(T("Cards.PlayTray+BoardReFaceLateTick")) as Behaviour;File.AppendAllText(Path.Combine(output,"pose-state.log"),label+" time="+Time.unscaledTime+" started="+Get(tray,"_wristBlendStarted")+" rootActive="+Root.gameObject.activeInHierarchy+" late="+late+" enabled="+(late!=null&&late.enabled)+" owner="+(late!=null&&ReferenceEquals(Get(late,"Owner"),tray))+" tracked="+Get(left,"IsTracked")+" root="+Root.position+" wrist="+Wrist(left).position+" main="+Get(T("Hands.VRHands"),"Primary")+" source="+Get(tray,"_wristSource")+"\n");}
    object Hand(string side,Vector3 position,string style="Glove"){GameObject go=new GameObject("Tracked "+side);go.transform.SetParent(anchor,false);go.transform.localPosition=position;var hand=go.AddComponent(T("Hands.VRHand"));((Behaviour)hand).enabled=false;Set(hand,"Side",EnumValue("Hands.HandSide",side));Transform visual=new GameObject("Actual hand-visual frame").transform;visual.SetParent(go.transform,false);object handRig=Call(T("Hands.HandVisuals"),"Build",visual,EnumValue("Hands.HandSide",side),EnumValue("Hands.HandStyle",style));Check(((Transform)Get(handRig,"Wrist")).name=="Socket_Wrist","actual "+side+" "+style+" authored wrist receives original scale-compensated socket");Set(hand,"Rig",handRig);Set(hand,"IsTracked",true);return hand;}
    Transform Wrist(object hand)=>(Transform)Get(Get(hand,"Rig"),"Wrist");
    void PoseProof(object hand,string label){Transform w=Wrist(hand);Vector3 offset=(Vector3)Get(Get(T("Cards.CardsConfig"),"WristBoardOffsetMeters"),"Value");Vector3 angles=(Vector3)Get(Get(T("Cards.CardsConfig"),"WristBoardAnglesDegrees"),"Value");Near(w.position+w.rotation*(offset*anchor.lossyScale.x),Root.position,label+" original root position");Rotation(w.rotation*Quaternion.Euler(angles),Root.rotation,label+" original root rotation");}
    void Hidden(bool yes){var follow=(Transform)Get(tray,"_followAnchor");var handle=(Component)Get(tray,"_handle");Check(follow.gameObject.activeSelf==!yes&&handle.gameObject.activeSelf==!yes,"full local cap/engraving/handle active state "+yes);Check(!yes||!follow.GetComponentsInChildren<Collider>(true).Any(x=>x.enabled&&x.gameObject.activeInHierarchy),"hidden local follow cap has no active laser/finger collider");Check(!yes||!handle.GetComponentsInChildren<Collider>(true).Any(x=>x.enabled&&x.gameObject.activeInHierarchy),"hidden local bar has no active grab/laser collider");}
    void BindPlugin(object file,string field,Type type,object value){var bind=file.GetType().GetMethods(All).Single(m=>m.Name=="Bind"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==4&&m.GetParameters()[0].ParameterType==typeof(string)&&m.GetParameters()[3].ParameterType==typeof(string));Set(T("Plugin"),field,bind.MakeGenericMethod(type).Invoke(file,new object[]{"Proof",field,value,"Explicit input boundary"}));}
    void Control(bool counterexample,string label){Check(counterexample,label);controls++;}
    void MenuProof()
    {
        Type catalog=T("WorldUI.ConfigCatalog");
        object offset=Call(catalog,"Describe","cards",Get(Get(T("Cards.CardsConfig"),"WristBoardOffsetMeters"),"Definition"),Get(T("Cards.CardsConfig"),"WristBoardOffsetMeters"));
        object angles=Call(catalog,"Describe","cards",Get(Get(T("Cards.CardsConfig"),"WristBoardAnglesDegrees"),"Definition"),Get(T("Cards.CardsConfig"),"WristBoardAnglesDegrees"));
        foreach(object item in new[]{offset,angles}){
            Check((int)Get(item,"Components")==3,"original catalog exposes all three wrist axes");string key=(string)Get(item,"Key");double step=(double)Call(T("WorldUI.ConfigSteps"),"Resolve","Cards",key,1d,false);Check(step>0&&step<=1,"original suffix resolution provides a usable precise wrist step "+key);Set(item,"BaseStep",step);Vector3 before=(Vector3)Get(Get(item,"Entry"),"BoxedValue");Call(catalog,"Step",item,2,1,1d);Vector3 after=(Vector3)Get(Get(item,"Entry"),"BoxedValue");Check(after.x==before.x&&after.y==before.y&&after.z>before.z,"original config step edits only the chosen Z wrist axis "+key);}
        // Real complete row builders, with a small external donor-row skeleton; no sizing,
        // choice/toggle callback or numeric-stepping algorithm is replaced.
        Type options=T("WorldUI.VROptionsTab");GameObject donor=new GameObject("Authored row seam",typeof(RectTransform),typeof(LayoutElement));((RectTransform)donor.transform).sizeDelta=new Vector2(800,44);
        GameObject title=new GameObject("Title",typeof(RectTransform),typeof(TextMeshProUGUI));title.transform.SetParent(donor.transform,false);title.GetComponent<TMP_Text>().font=TMP_Settings.defaultFontAsset;title.GetComponent<TMP_Text>().fontSize=24;
        GameObject option=new GameObject("Option",typeof(RectTransform));option.transform.SetParent(donor.transform,false);new GameObject("Toggle",typeof(RectTransform),typeof(Toggle)).transform.SetParent(option.transform,false);
        Set(options,"_toggleTemplate",donor);GameObject dropdown=new GameObject("Original dropdown control seam",typeof(RectTransform),typeof(TMP_Dropdown));Set(options,"_dropdownControl",dropdown);
        GameObject slider=new GameObject("Original slider control seam",typeof(RectTransform),typeof(Slider));GameObject amount=new GameObject("Amount",typeof(RectTransform),typeof(TextMeshProUGUI));amount.transform.SetParent(slider.transform,false);amount.GetComponent<TMP_Text>().font=TMP_Settings.defaultFontAsset;Set(options,"_sliderControl",slider);
        Transform parent=new GameObject("Actual settings output",typeof(RectTransform)).transform;
        foreach(string key in new[]{"WristBoardEnabled","WristBoardHand","WristBoardOffsetMeters","WristBoardAnglesDegrees"}){
            object entry=Get(T("Cards.CardsConfig"),key);object item=Call(catalog,"Describe","cards",Get(entry,"Definition"),entry);Set(item,"BaseStep",.001d);
            int components=(int)Get(item,"Components");for(int component=0;component<components;component++){int count=parent.childCount;Call(options,"BuildRow",parent,item,component,null,null);Check(parent.childCount==count+1,"actual production builder constructs wrist setting row "+key+"/"+component);}
        }
        var selector=parent.GetComponentsInChildren<TMP_Dropdown>(true).Single();Check(selector.options.Count==3&&selector.options[0].text!="NonMain","actual wrist selector presents translated labels instead of enum identifiers");
        var toggle=parent.GetChild(0).GetComponentsInChildren<Toggle>(true).Single();Check(toggle!=null,"actual settings-only wrist switch is a usable toggle");
        bool enabled=(bool)Get(Get(T("Cards.CardsConfig"),"WristBoardEnabled"),"Value");toggle.onValueChanged.Invoke(!enabled);Check((bool)Get(Get(T("Cards.CardsConfig"),"WristBoardEnabled"),"Value")==!enabled,"actual ordinary-menu toggle callback writes wrist setting");toggle.onValueChanged.Invoke(enabled);
        selector.onValueChanged.Invoke(1);Check(Get(Get(T("Cards.CardsConfig"),"WristBoardHand"),"Value").ToString()=="Left","actual advanced translated choice callback selects left wrist");Value("WristBoardHand",EnumValue("Cards.WristBoardHand","NonMain"));
        Object.DestroyImmediate(dropdown);Object.DestroyImmediate(parent.gameObject);
        // The original donor shapes remain available for the actual curated rebuild below.
    }
    object[] SliderBindings(Type options,string key)=>((IEnumerable)Get(options,"Sliders")).Cast<object>().Where(x=>(string)Get(Get(x,"Item2"),"Key")==key).OrderBy(x=>(int)Get(x,"Item3")).ToArray();
    GameObject[] LiveRows(Type options)=>((IEnumerable)Get(options,"Rows")).Cast<GameObject>().Where(x=>x!=null).ToArray();
    GameObject RowOf(Type options,Slider slider)=>LiveRows(options).Single(row=>slider.transform.IsChildOf(row.transform));
    Vector3 WristOffsets()=>(Vector3)Get(Get(T("Cards.CardsConfig"),"WristBoardOffsetMeters"),"Value");
    Toggle WristToggle(Type options)=>LiveRows(options).Single(row=>row.transform.Find("Title")?.GetComponent<TMP_Text>()?.text==(string)Call(T("Core.Loc"),"Mod","vr_o_wristboard")).GetComponentInChildren<Toggle>(true);
    IEnumerator CuratedMenuProof()
    {
        Type options=T("WorldUI.VROptionsTab");
        Array categories=(Array)Get(options,"Curated");int category=-1;
        for(int i=0;i<categories.Length;i++)if((string)Get(categories.GetValue(i),"LocKey")=="cat_boardcards")category=i;
        Check(category>=0,"actual everyday board category is selected from the production curated declaration");
        var parent=new GameObject("Actual everyday wrist settings output",typeof(RectTransform)).GetComponent<RectTransform>();
        Set(options,"ContentRoot",parent);Set(options,"_curated",category);Set(options,"_view",EnumValue("WorldUI.VROptionsTab+View","Curated"));
        Vector3 saved=WristOffsets();Value("WristBoardEnabled",false);Call(tray,"TickPlacement");yield return Settle();
        Call(options,"Rebuild");
        Check(SliderBindings(options,"WristBoardOffsetMeters").Length==0,"actual curated menu has no wrist position sliders while wrist attachment is off");
        Near(saved,WristOffsets(),"opening the ordinary menu preserves the saved wrist offset vector");
        WristToggle(options).isOn=true;
        Check((bool)Get(Get(T("Cards.CardsConfig"),"WristBoardEnabled"),"Value"),"the actual ordinary wrist toggle enables attachment through its original callback");
        object[] bindings=SliderBindings(options,"WristBoardOffsetMeters");Check(bindings.Length==3,"toggle callback immediately rebuilds all three position sliders without reopening the menu");
        GameObject[] rows=LiveRows(options);GameObject toggleRow=rows.Single(row=>WristToggle(options).transform.IsChildOf(row.transform));int toggleIndex=Array.IndexOf(rows,toggleRow);
        for(int axis=0;axis<3;axis++){
            Slider bar=(Slider)Get(bindings[axis],"Item1");
            Check((int)Get(bindings[axis],"Item3")==axis,"production slider registry retains its own wrist component "+axis);
            Check(ReferenceEquals(rows[toggleIndex+1+axis],RowOf(options,bar)),"wrist axis row is immediately below the enabled mode switch in draw order "+axis);
            Check(Mathf.Abs(bar.minValue+.5f)<.000001f&&Mathf.Abs(bar.maxValue-.5f)<.000001f,"wrist drag gesture spans half a metre in each direction "+axis);
            Check(Mathf.Abs(bar.value-WristOffsets()[axis])<.000001f,"first rendered slider handle reads the saved corresponding axis "+axis);
        }
        Call(tray,"TickPlacement");yield return Settle();PoseProof(left,"actual wrist-toggle activation finishes at the original tracked root");
        for(int axis=0;axis<3;axis++){
            Slider bar=(Slider)Get(bindings[axis],"Item1");Vector3 before=WristOffsets();float requested=new[]{.1234f,-.0876f,.2044f}[axis];bar.value=requested;
            Vector3 after=WristOffsets();float expected=new[]{.123f,-.088f,.204f}[axis];
            Check(Mathf.Abs(after[axis]-expected)<.000001f,"actual wrist slider snaps only the selected axis to a millimetre "+axis);
            for(int other=0;other<3;other++){
                if(other!=axis)Check(after[other]==before[other],"actual wrist slider preserves other persisted component "+axis+"/"+other);
                Slider sibling=(Slider)Get(bindings[other],"Item1");Check(Mathf.Abs(sibling.value-after[other])<.000001f,"live repaint keeps every other wrist handle on its own component "+axis+"/"+other);
            }
            GameObject row=RowOf(options,bar);string readout=(string)Call(T("WorldUI.ConfigCatalog"),"ValueText",Get(bindings[axis],"Item2"),axis);
            Check(row.GetComponentsInChildren<TMP_Text>(true).Any(x=>x.transform.name=="Amount"&&x.text==readout),"original harvested slider amount label displays its own live axis "+axis);
            row.GetComponentsInChildren<Button>(true).Single(x=>x.name=="ArrowRight").onClick.Invoke();
            Vector3 stepped=WristOffsets();Check(Mathf.Abs(stepped[axis]-after[axis]-.001f)<.000001f,"original fine-adjustment arrow steps the selected wrist axis by one millimetre "+axis);
            Check(Mathf.Abs(bar.value-stepped[axis])<.000001f,"arrow presses repaint the corresponding slider handle "+axis);
            for(int other=0;other<3;other++)if(other!=axis)Check(stepped[other]==after[other],"original wrist arrow preserves independent other axis "+axis+"/"+other);
            yield return null;yield return null;PoseProof(left,"actual original late tick applies the slider and arrow offset without a placement helper call "+axis);
        }
        // The shared hybrid kit still edits bounded scalar entries in their original type.
        object scalar=SliderBindings(options,"TrayScale").Single();Slider scalarBar=(Slider)Get(scalar,"Item1");float scalarSaved=(float)Get(Get(T("Cards.CardsConfig"),"TrayScale"),"Value");
        scalarBar.value=1.273f;Check(Mathf.Abs((float)Get(Get(T("Cards.CardsConfig"),"TrayScale"),"Value")-1.25f)<.000001f,"shared actual hybrid builder retains scalar range/step and float conversion");
        RowOf(options,scalarBar).GetComponentsInChildren<Button>(true).Single(x=>x.name=="ArrowRight").onClick.Invoke();Check(Mathf.Abs(scalarBar.value-1.3f)<.000001f,"shared scalar arrow still updates the ordinary scalar slider");Value("TrayScale",scalarSaved);
        Vector3 edited=WristOffsets();WristToggle(options).isOn=false;
        Check(SliderBindings(options,"WristBoardOffsetMeters").Length==0,"ordinary wrist-toggle callback immediately removes all three position sliders when disabled");
        Near(edited,WristOffsets(),"hiding the wrist controls never resets the edited vector");
        // A practical gesture range must not silently clamp an older saved calibration.
        Vector3 outside=new Vector3(.725f,-.63f,.012f);Value("WristBoardOffsetMeters",outside);WristToggle(options).isOn=true;
        Near(outside,WristOffsets(),"rebuilding enabled wrist sliders preserves saved values beyond their drag range");
        bindings=SliderBindings(options,"WristBoardOffsetMeters");Slider z=(Slider)Get(bindings[2],"Item1");z.value=.05f;
        Check(WristOffsets().x==outside.x&&WristOffsets().y==outside.y,"editing one in-range slider preserves other saved axes outside the drag range");
        Value("WristBoardOffsetMeters",edited);Call(options,"Rebuild");Call(tray,"TickPlacement");yield return Settle();PoseProof(left,"rebuilt normal menu leaves original board and owner world pose on the final saved calibration");
        Object.DestroyImmediate((GameObject)Get(options,"_toggleTemplate"));Object.DestroyImmediate((GameObject)Get(options,"_sliderControl"));Call(options,"ClearRows");Set(options,"ContentRoot",null);Object.DestroyImmediate(parent.gameObject);
    }
    void RemoteProof()
    {
        // Use the original remote furniture constructor and original TickWire on a dormant
        // uninitialized actorless peer. This is legal join state; gameplay and hand drivers
        // stay dormant. Owner config is intentionally changed independently of its rig state.
        object owner=FormatterServices.GetUninitializedObject(T("Net.RemoteAvatar"));object pose=New("Net.RemoteBoardPoseState");Set(owner,"_boardPose",pose);
        object tune=New("Net.RemoteBoardTuning",Get(T("Cards.CardsConfig"),"CurrentBoard"),null,0);Set(owner,"BoardTuning",tune);
        Transform board=new GameObject("Remote original board root").transform;
        object furniture=New("Net.RemoteBoardFurniture",board,tune,null,Vector3.zero,Vector3.zero,0f,0f);
        object state=New("Net.AvatarState");Set(state,"HasBoardPose",true);Set(state,"HasBoard",true);Set(state,"BoardScale",Root.lossyScale.x);Set(state,"WristBoard",true);
        object actualPose=New("Net.RigPose");Set(actualPose,"Position",Root.position);Set(actualPose,"Rotation",Root.rotation);Set(state,"BoardPose",actualPose);
        byte[] packet=new byte[189];int length=(int)Call(T("Net.AvatarSerializer"),"Write",state,packet);object[] readArgs={packet,length,null};bool read=(bool)T("Net.AvatarSerializer").GetMethod("TryRead",All).Invoke(null,readArgs);Check(read,"original allocation-free serializer decodes the actual owner wrist root packet");Call(pose,"AcceptRig",readArgs[2]);
        Near(Root.position,(Vector3)Get(Get(pose,"Pose"),"Position"),"original remote board state receives actual owner world position");Rotation(Root.rotation,(Quaternion)Get(Get(pose,"Pose"),"Rotation"),"original remote board state receives actual owner orientation");Check(Mathf.Abs((float)Get(pose,"Scale")-Root.lossyScale.x)<.0001f,"original fast board state carries owner uniform wrist scale instead of viewer tuning");
        // Invoke only the full original wrist visibility delivery within TickWire below; its
        // unrelated native gameplay decision helpers need a real running scenario. This method
        // is the production seam also used by TickWire, not fixture hiding logic.
        Call(furniture,"ApplyWristControlVisibility",owner);
        GameObject pin=(GameObject)Get(Get(furniture,"_pin"),"_go");
        Check(!pin.activeSelf,"owner fast wrist state hides full original remote cap");
        var engraving=Get(furniture,"_pinEngraving") as Component;Check(engraving==null||!engraving.gameObject.activeSelf,"owner fast wrist state hides original remote engraving");Check(!((Transform)Get(furniture,"_handleVisual")).gameObject.activeSelf,"owner fast wrist state hides original remote handle");
        Value("WristBoardEnabled",false);Call(furniture,"ApplyWristControlVisibility",owner);Check(!pin.activeSelf,"viewer wrist preference cannot unhide owner's attached controls");Value("WristBoardEnabled",true);
        Set(state,"WristBoard",false);Call(pose,"AcceptRig",state);Call(furniture,"ApplyWristControlVisibility",owner);Check(pin.activeSelf&&((Transform)Get(furniture,"_handleVisual")).gameObject.activeSelf,"owner next ordinary rig sample restores peer cap and handle despite viewer wrist preference");
        Call(furniture,"Destroy");Object.DestroyImmediate(board.gameObject);
    }
    void Render(string file){Camera camera=new GameObject("Board evidence camera").AddComponent<Camera>();camera.transform.position=Root.position+Root.rotation*new Vector3(0,0,-Root.lossyScale.x*1.3f);camera.transform.rotation=Root.rotation;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.white;var rt=new RenderTexture(900,650,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(900,650,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,650),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());RenderTexture.active=null;Object.DestroyImmediate(image);Object.DestroyImmediate(rt);Object.DestroyImmediate(camera.gameObject);}
    [Serializable] sealed class Result{public bool passed;public int assertions;public int causalControls;public string[] checks;public string error;}
    void Finish(bool passed,string error){File.WriteAllText(Path.Combine(output,"results.json"),JsonUtility.ToJson(new Result {passed=passed,assertions=checks.Count,causalControls=controls,checks=checks.ToArray(),error=error},true));Exit(passed?0:1);}
}
