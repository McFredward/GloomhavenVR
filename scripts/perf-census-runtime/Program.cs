using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class InteractionProgram
{
    private static int count;
    private static readonly List<GameObject> created = new();
    private static readonly List<UnityEngine.Object> graphics = new();
    private static FieldInfo Field(string name)=>typeof(PerfSceneProfile).GetField(name,BindingFlags.Static|BindingFlags.NonPublic)!;
    private static int Inventory<T>(string name)=>((List<T>)Field(name).GetValue(null)!).Count;
    private static int Visited()=>((HashSet<int>)Field("_visitedNodes").GetValue(null)!).Count;
    private static bool Pending()=>Field("_sample").GetValue(null)!=null;
    private static GameObject Obj(string name,Transform? parent=null){var go=new GameObject(name);if(parent!=null)go.transform.SetParent(parent);created.Add(go);return go;}
    private static void Check(bool yes,string message){count++;if(!yes)throw new Exception(message);}
    private static void Request(){var sb=new StringBuilder();PerfSceneProfile.AppendSceneLine(sb);}
    private static void Drain(Scene persistent)
    {
        int slices=0;
        while(Pending()&&slices++<50000)
        {
            int before=Visited();
            PerfSceneProfile.Tick(persistent);
            // Completed censuses clear refs after emission; every running slice must still obey
            // the true object/component work-unit cap, independent of the stopwatch/JIT cost.
            if(Pending())Check(Visited()-before<=8,"object-count budget limits a native traversal slice");
        }
        Check(!Pending()&&slices>100,"large scene census is spread across frames");
    }
    public static int Run()
    {
        count=0; created.Clear();
        try
        {
            SceneController.Instance.Current=SceneManager.GetActiveScene();
            Check(SceneController.Instance.Current.buildIndex==1,"fixture has native build-index1 scene");
            var root=Obj("NativeCensusFixtures");
            for(int i=0;i<1200;i++)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);created.Add(go);go.transform.SetParent(root.transform);
                var r=go.GetComponent<Renderer>();r.enabled=i%7!=0;r.forceRenderingOff=i%11==0;
            }
            var inactive=GameObject.CreatePrimitive(PrimitiveType.Cube);created.Add(inactive);inactive.transform.SetParent(root.transform);inactive.SetActive(false);
            var persistent=Obj("PersistentNativeCensusFixture");UnityEngine.Object.DontDestroyOnLoad(persistent);persistent.AddComponent<MeshRenderer>();
            var animator=Obj("NativeAnimator",root.transform);animator.AddComponent<Animator>();
            var hiddenChild=Obj("DisabledRendererChild",animator.transform);hiddenChild.AddComponent<MeshRenderer>();hiddenChild.SetActive(false);
            Obj("NativeAnimatorWithoutRenderer",root.transform).AddComponent<Animator>();
            Obj("Particle",root.transform).AddComponent<ParticleSystem>();
            Obj("Light",root.transform).AddComponent<Light>();
            Obj("Lod",root.transform).AddComponent<LODGroup>();
            var graphic=Obj("ModGraphic",root.transform);graphic.layer=VRLayers.ModLayer;graphic.AddComponent<Image>();
            var disabledGraphic=Obj("DisabledGraphic",root.transform);disabledGraphic.AddComponent<Image>().enabled=false;
            VRLog.Lines.Clear();
            PerfSceneProfile.Cancel();PerfSceneProfile.ConfigureBudget(4,8);
            PerfSceneProfile.Tick(persistent.scene);
            Request();Check(Pending(),"summary schedules loaded inventory");
            Drain(persistent.scene);
            string scene=VRLog.Lines.Find(l=>l.StartsWith("SCENE — renderer"))??"";
            string sim=VRLog.Lines.Find(l=>l.StartsWith("SIM —"))??"";
            string gfx=VRLog.Lines.Find(l=>l.StartsWith("GFX —"))??"";
            int native=UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            Check(scene.Contains("PersistentNativeCensusFixture"),"persistent scene renderer participates");
            Check(scene.Contains("totals: "+native+" active renderer(s)"),"active renderer population agrees with original Unity census");
            Check(scene.Contains("bounded slice(s)")&&scene.Contains("max atomic work unit"),"incremental CPU/slice/atomic provenance is explicit");
            Check(sim.Contains("1 enabled animator(s) own NO Renderer"),"inactive child renderer retains original animator population rule");
            Check(gfx.Contains("LOD groups: 1 active, 1 enabled"),"same sliced native LOD population reaches GFX");
            Check(Inventory<Renderer>("_inventoryRenderers")==0,"completed inventory releases retained renderer references");
            // Actual adaptive refresh dispatch closes timing windows without restarting
            // the real incremental inventory. Its enumerator must survive each boundary.
            PerfSceneProfile.Cancel();Field("_rationer").SetValue(null,new CensusRationer());
            PerfMonitor.SeedPacing();Request();
            object job=Field("_sample").GetValue(null)!;
            for(int i=0;i<240;i++)
            {
                if(i%24==0)
                {
                    float old=i%48==0?72:24,newRate=i%48==0?24:72;
                    PerfMonitor.AdaptiveRefresh(newRate);
                    Check(ReferenceEquals(job,Field("_sample").GetValue(null)),
                        "adaptive refresh preserves the same in-progress census");
                    Check(PerfMonitor.ClosedWindows==i/24+1&&PerfMonitor.ResetWindows==i/24+1
                        &&PerfMonitor.LastSummarizedHz==old,
                        "refresh closes the old pacing window before adopting the new rate");
                }
                PerfSceneProfile.Tick(persistent.scene);
            }
            var progress=new StringBuilder();PerfSceneProfile.AppendSceneLine(progress);
            Check(progress.ToString().Contains("progress 240 slices")&&progress.ToString().Contains("work units"),
                "bounded summary exposes advancing native inventory work");
            Drain(persistent.scene);
            Check(PerfFrameSplit.Roster!=null&&PerfFrameSplit.Roster.Length==native,
                "adaptive refresh census eventually publishes the native roster");
            Field("_rationer").SetValue(null,new CensusRationer());Request();
            for(int i=0;i<12;i++)PerfSceneProfile.Tick(persistent.scene);
            Check(Pending(),"graphics boundary starts from a live partially visited census");
            PerfMonitor.MarkChange("fixture real graphics setting");
            Check(!Pending(),
                "graphics changes still cancel incremental inventories");
            Check(PerfFrameSplit.Roster==null,
                "graphics changes drop the completed Zoom roster");
            // Resampling cooldown intentionally remains. Reset only the cadence seam, not the
            // production request/pump; every cancellation case must drop live Unity references.
            foreach(string reason in new[]{"debug","config","loading","scene"})
            {
                Field("_rationer").SetValue(null,new CensusRationer());
                Request(); for(int i=0;i<12;i++)PerfSceneProfile.Tick(persistent.scene);
                Check(Pending()&&Inventory<Renderer>("_inventoryRenderers")>0,"cancellation starts from real partially visited inventory");
                if(reason=="debug")VRLog.WantsDebug=false;
                if(reason=="config")PerfConfig.SceneProfileOn=false;
                if(reason=="loading")SceneController.Instance.ScenarioIsLoading=true;
                Scene extra=default;
                if(reason=="scene")extra=SceneManager.CreateScene("CensusCancellation-"+typeof(InteractionProgram).Assembly.GetName().Name);
                PerfSceneProfile.Tick(persistent.scene);
                Check(!Pending()&&Inventory<Renderer>("_inventoryRenderers")==0,
                    reason=="debug"?"Debug off cancels inventory references":reason+" transition cancels inventory references");
                VRLog.WantsDebug=true;PerfConfig.SceneProfileOn=true;SceneController.Instance.ScenarioIsLoading=false;
                if(extra.IsValid())SceneManager.UnloadSceneAsync(extra);
            }
            // Native texture getters/material slots are real; projected span is supplied directly
            // at this seam because a -nographics fixture has no headset projection/image.
            PerfTextureCensus.Begin(null);
            typeof(PerfTextureCensus).GetField("_armed",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,true);
            var textured=root.GetComponentInChildren<Renderer>();
            Shader shader=Shader.Find("Unlit/Texture");
            Check(shader!=null,"native texture fixture shader exists");
            for(int i=0;i<320;i++)
            {
                var texture=new Texture2D(16,16,TextureFormat.RGBA32,false);graphics.Add(texture);
                var material=new Material(shader);graphics.Add(material);material.mainTexture=texture;
                PerfTextureCensus.Offer(textured,material,128);
            }
            int textureSteps=0;
            foreach(object? step in PerfTextureCensus.PrepareLine())textureSteps++;
            Check(textureSteps>=320,"texture native population is sliced per surface");
            PerfTextureCensus.Log();
            Check(VRLog.Lines.Exists(l=>l.StartsWith("TEX —")&&l.Contains("POPULATION: 320 DISTINCT")),"original native texture population reaches TEX");
            var textures=(ICollection)typeof(PerfTextureCensus).GetField("Surfaces",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            Check(textures.Count==0,"finished TEX drops native texture/material references");
            PerfTextureCensus.Begin(null);
            typeof(PerfTextureCensus).GetField("_armed",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,true);
            PerfTextureCensus.Offer(textured,(Material)graphics[1],128);
            Check(textures.Count==1,"texture cancellation starts with native retained surface");
            PerfSceneProfile.Cancel();
            Check(textures.Count==0,"cancelled SCENE drops native texture/material references");
            // SceneCensus must stay useful when the expensive SceneProfile is off.
            PerfSceneProfile.Cancel();VRLog.Lines.Clear();PerfConfig.SceneProfileOn=false;
            PerfConfig.SceneCensus.Value=true;
            var frame=new StringBuilder();PerfFrameSplit.AppendSceneCensus(frame);
            Check(Pending(),"SceneProfile off still schedules lightweight census");
            Drain(persistent.scene);
            Check(PerfFrameSplit.Roster!=null&&PerfFrameSplit.Roster.Length==native,
                "SceneProfile off seeds the original Zoom roster");
            Check(Inventory<MonoBehaviour>("_inventoryBehaviours")==0
                &&!VRLog.Lines.Exists(l=>l.StartsWith("SIM —")||l.StartsWith("TEX —")),
                "lightweight roster omits full simulation/material/texture profile");
            frame.Length=0;PerfFrameSplit.AppendSceneCensus(frame);
            Check(frame.ToString().Contains("uGUI: 2 graphic(s), 1 enabled, 1 on the mod")
                && frame.ToString().Contains("scene census: "+native+" renderer(s)")
                &&frame.ToString().Contains("capture span"),"cached SPLIT census keeps explicit population/span grammar");
            // Publishing the cached summary also requests a fresh sliced capture. A Debug
            // disable must remove both that job and its previous complete original roster.
            VRLog.WantsDebug=false;PerfSceneProfile.Tick(persistent.scene);
            Check(PerfFrameSplit.Roster==null&&!Pending(),"Debug off drops completed Zoom roster and pending census");
            VRLog.WantsDebug=true;PerfConfig.SceneProfileOn=true;
            // Use the actual monitor summary/catch methods. Inject only the external game
            // context failure while a real native roster and refresh job are alive.
            PerfConfig.SceneProfileOn=false;
            frame.Length=0;PerfFrameSplit.AppendSceneCensus(frame);Drain(persistent.scene);
            frame.Length=0;PerfFrameSplit.AppendSceneCensus(frame);
            Check(Pending()&&PerfFrameSplit.Roster!=null,"profile fault starts with native pending and complete inventory");
            PerfConfig.SceneProfileOn=true;SceneController.Instance.ThrowRead=true;
            PerfMonitor.ProfileSummary();SceneController.Instance.ThrowRead=false;
            Check(!Pending()&&PerfFrameSplit.Roster==null,
                "profile summary fault cancels pending job and completed roster");
            int faultLog=VRLog.Lines.Count;PerfMonitor.SplitSummary();
            Check(!Pending()&&VRLog.Lines.Count>faultLog
                &&VRLog.Lines[VRLog.Lines.Count-1].Contains("scene census n/a (diagnostic fault"),
                "profile summary fault cannot schedule a new census");
            PerfMonitor.ResetFault();
            // Price actual distance native API and user callbacks without contact throttling.
            var handMethods=new NativeGrabMethods();
            var target=new Target();
            PerfMonitor.Reset();
            Check(handMethods.Eligible(target)&&target.GateCalls==1,"eligibility callback executes once");
            Check(handMethods.Allowed(target)&&target.HandCalls==1,"per-hand callback executes once");
            handMethods.Hover(target);handMethods.Hover(target);handMethods.Hover(null);
            Check(target.Events=="hover+;hover-;","hover transitions and duplicate no-op preserve callback order");
            Check(PerfMonitor.Calls("Hands.NearGrip.HoverCallbacks")==3&&PerfMonitor.Ms("Hands.NearGrip.HoverCallbacks")>=3,"hover callback stage exists and is priced");
            handMethods.Take(target);
            Check(target.Events=="hover+;hover-;grab;"&&target.Grabs==1,"pickup callback runs once after clearing hover");
            Check(PerfMonitor.Calls("Hands.NearGrip.Pickup")==1&&PerfMonitor.Ms("Hands.NearGrip.Pickup")>=1,"pickup callback is separate from hover stage");
            var collider=Obj("NativeReachCollider").AddComponent<BoxCollider>();collider.center=Vector3.zero;collider.size=Vector3.one;
            var entry=new VRInteractables.GrabbableEntry(target,collider);
            Check(Mathf.Abs(NativeGrabMethods.Distance(in entry,new Vector3(2,0,0))-1.5f)<.001f,"real native ClosestPoint distance is unchanged");
            Check(PerfMonitor.Calls("Hands.NearGrip.Distance")==1,"distance stage records native call");
            target.ThrowHover=true;
            try{handMethods.Hover(target);}catch(InvalidOperationException){}
            Check(PerfMonitor.Depth==0,"callback exceptions close all native scope nesting");
            PerfMonitor.StepsActive=false;
            double prior=PerfMonitor.Ms("Hands.NearGrip.Eligibility");handMethods.Eligible(target);
            Check(PerfMonitor.Ms("Hands.NearGrip.Eligibility")==prior&&target.GateCalls==2,"disabled attribution preserves callback without recording work");
            count += SpikePreparationFixture.Run();
            Console.WriteLine("Controlled loaded scene: "+native+" renderers; "+scene.Substring(scene.IndexOf("INSTRUMENT COST")));
            return count;
        }
        finally
        {
            PerfSceneProfile.Cancel();VRLog.WantsDebug=true;PerfConfig.SceneProfileOn=true;SceneController.Instance.ThrowRead=false;PerfMonitor.ResetFault();
            foreach(var go in created)if(go!=null)UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
            foreach(var asset in graphics)if(asset!=null)UnityEngine.Object.DestroyImmediate(asset);graphics.Clear();
        }
    }
    private sealed class Target:IGrabbable,IGrabbableHandFilter,IGrabHighlight
    {
        internal int GateCalls,HandCalls,Grabs;internal string Events="";internal bool ThrowHover;
        public bool CanGrab{get{GateCalls++;Thread.Sleep(2);return true;}}
        public bool GrabWithGrip=>false;
        public bool AllowsHand(VRHand h){HandCalls++;Thread.Sleep(2);return true;}
        public void OnGrabHighlight(VRHand h,bool active){if(ThrowHover)throw new InvalidOperationException();Thread.Sleep(2);Events+=active?"hover+;":"hover-;";}
        public void OnGrab(VRHand h){Thread.Sleep(2);Events+="grab;";Grabs++;}
        public void OnRelease(VRHand h,Vector3 v){}
    }
}
