using GloomhavenVR.Compat;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;
static class Program {
    static int count;
    static void Check(bool value,string message) { count++; if(!value) throw new Exception(message); }
    static GameObject? Model(VRHand hand)=>hand.transform.Children.FirstOrDefault(t=>t.name.StartsWith("GloomhavenVR.Controller_"))?.gameObject;
    static void Frames(int number) { for(int i=0;i<number;i++) { Time.unscaledTime+=Time.unscaledDeltaTime; ControlsTutorial.Tick(); RepresentedPair(); } }
    static void RepresentedPair() {
        foreach(var hand in new[]{VRHands.Left,VRHands.Right}) {
            var model=Model(hand);
            bool visibleModel=model!=null && model.transform.localScale.x>0
                && model.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled);
            Check(hand.Rig.Root.GetComponent<Renderer>().enabled || visibleModel,
                "Neither side may lose both its hand and controller during a transition or recovery");
        }
    }
    static void HandPair() {
        foreach(var hand in new[]{VRHands.Left,VRHands.Right}) {
            Check(Model(hand)==null && hand.Rig.Root.GetComponent<Renderer>().enabled,
                "Hand tasks must restore both ordinary hands without controller recovery overriding them");
            Check(!hand.Rig.Root.Children[0].GetComponent<Renderer>().enabled,
                "Hand restoration must preserve another owner's hidden renderers");
        }
    }
    static void VisiblePair() {
        foreach(var hand in new[]{VRHands.Left,VRHands.Right}) {
            var model=Model(hand);
            Check(model!=null && model.transform.localScale.x>0, "Controller steps must keep both models visible");
            Check(model!.GetComponentsInChildren<Renderer>(true).All(r=>r.enabled), "Hand hiding must not hide its controller sibling");
            Check(model.GetComponentsInChildren<Renderer>(true).All(r=>r.gameObject.name=="GloomhavenVR.KeyMarker" || r.gameObject.layer==31), "Every controller descendant must be excluded from scenery and camera culling");
        }
    }
    static void Key(VRHand hand,string? expected) {
        var model=Model(hand)!;
        foreach(var part in model.transform) {
            var renderer=part.GetComponent<Renderer>();
            if(renderer!=null) Check(renderer.Lit==(part.name==expected), "Only the configured hand and requested key may glow");
        }
        var markers=model.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.name=="GloomhavenVR.KeyMarker");
        foreach(var marker in markers) {
            Check(marker.gameObject.layer==31,"Anchor-only key markers must use the mod layer");
            Check(marker.gameObject.activeSelf==(expected=="squeeze"),"Previous anchor marker must clear when the task changes");
        }
    }
    static int Index(ControlAction action)=>Array.FindIndex(ControlsLesson.Steps,s=>s.Action==action);
    static void Single(ControlAction action,HandSide side,string key) {
        ControlsTutorial.Apply(Index(action));Frames(15);VisiblePair();
        Key(VRHands.Left,side==HandSide.Left?key:null);Key(VRHands.Right,side==HandSide.Right?key:null);
    }
    static void ModelLoss() {
        // Each public path must restore the hand even if no replacement asset is available.
        foreach(int recovery in new[]{0,1,2}) {
            var hand=new VRHand(HandSide.Right);
            var visual=new ControllerVisual(hand);
            visual.Highlight(ControllerKey.Primary);visual.Show();
            for(int i=0;i<15;i++) visual.Tick();
            Check(!hand.Rig.Root.GetComponent<Renderer>().enabled,"Model loss fixture must start with a hidden hand");
            var model=Model(hand);
            UnityEngine.Object.Destroy(model);
            Check(!ReferenceEquals(model,null) && model==null,"Destroyed model must have Unity null semantics");
            GloomhavenVR.WorldUI.WorldUIAssets.MissingRight=true;
            if(recovery==0) visual.Show();
            else if(recovery==1) visual.BeginHide();
            else visual.Tick();
            Check(hand.Rig.Root.GetComponent<Renderer>().enabled,
                "A lost controller must immediately restore the hand on every recovery path");
            Check(!hand.Rig.Root.Children[0].GetComponent<Renderer>().enabled,
                "Model loss must preserve another owner's hidden renderers");
            GloomhavenVR.WorldUI.WorldUIAssets.MissingRight=false;
            visual.Show();visual.Tick();
            Check(hand.Rig.Root.GetComponent<Renderer>().enabled && Model(hand)!.transform.localScale.x<1,
                "Replacement controller must animate in over a visible hand");
            for(int i=0;i<15;i++) visual.Tick();
            Key(hand,ControllerKey.Primary);
            visual.Hide();
        }
    }
    static void Main() {
        ControlsTutorial.Apply(0); Frames(15);HandPair();
        ControlsTutorial.Apply(Index(ControlAction.WorldDrag)); Frames(1);VisiblePair();
        Check(Model(VRHands.Left)!.transform.localScale.x<1, "Controller entry must remain animated");
        Check(VRHands.Left.Rig.Root.GetComponent<Renderer>().enabled,"Ordinary hand must remain until controller crossover");
        Frames(15);
        foreach(var primary in new[]{HandSide.Left,HandSide.Right}) {
            VRHands.Primary=primary==HandSide.Left?VRHands.Left:VRHands.Right;
            bool previousController=true;
            foreach(int index in Enumerable.Range(0,ControlsLesson.Steps.Length)) {
                var firstLeft=Model(VRHands.Left);var firstRight=Model(VRHands.Right);
                var step=ControlsLesson.Steps[index];
                bool want=step.Action is not (ControlAction.None or ControlAction.CardTake
                    or ControlAction.CardInHand or ControlAction.FingertipPick);
                Check(step.ShowsController==want,"Each original lesson task must keep its hand/controller presentation policy");
                ControlsTutorial.Apply(index);Frames(15);
                if(want) {
                    VisiblePair();
                    if(previousController)
                        Check(ReferenceEquals(firstLeft,Model(VRHands.Left))&&ReferenceEquals(firstRight,Model(VRHands.Right)),
                            "Consecutive controller tasks must retain both controller instances");
                    var hands=ControlsLesson.Availability(in step).Hands;
                    Key(VRHands.Left,(hands&ControlsLesson.LessonHands.Left)!=0?step.Key:null);
                    Key(VRHands.Right,(hands&ControlsLesson.LessonHands.Right)!=0?step.Key:null);
                } else { HandPair(); Frames(40);HandPair(); }
                previousController=want;
            }
            Single(ControlAction.Ping,primary,ControllerKey.Primary);
            Single(ControlAction.PanelReel,primary,ControllerKey.Thumbstick);
            Single(ControlAction.OpenMenu,primary==HandSide.Left?HandSide.Right:HandSide.Left,ControllerKey.Primary);
            foreach(var side in new[]{HandSide.Left,HandSide.Right}) {
                ComfortSettings.FlightHand.Value=side;ComfortSettings.TurnHand.Value=side;
                Single(ControlAction.Fly,side,ControllerKey.Thumbstick);
                Single(ControlAction.SnapTurn,side,ControllerKey.Thumbstick);
            }
        }
        ComfortSettings.FlightEnabled.Value=false;
        ControlsTutorial.Apply(Index(ControlAction.Fly));Frames(1);VisiblePair();Key(VRHands.Left,null);Key(VRHands.Right,null);
        ControlsTutorial.Teardown();ControlsTutorial.Teardown();
        foreach(var hand in new[]{VRHands.Left,VRHands.Right}) {
            Check(Model(hand)==null,"Lesson teardown must remove controller models");
            Check(hand.Rig.Root.GetComponent<Renderer>().enabled,"Lesson teardown must restore the ordinary hand");
            Check(!hand.Rig.Root.Children[0].GetComponent<Renderer>().enabled,"Teardown must not enable renderers hidden by another owner");
        }
        ControlsTutorial.Apply(0);Frames(15);HandPair();ControlsTutorial.Teardown();
        GloomhavenVR.WorldUI.WorldUIAssets.MissingRight=true;
        ControlsTutorial.Apply(Index(ControlAction.WorldDrag));Frames(2);
        Check(Model(VRHands.Left)!=null && Model(VRHands.Right)==null,"Missing one-sided asset must preserve the available controller");
        Check(VRHands.Right.Rig.Root.GetComponent<Renderer>().enabled,"Missing controller asset must retain its ordinary hand");
        GloomhavenVR.WorldUI.WorldUIAssets.MissingRight=false;Frames(40);VisiblePair();
        VRHands.Primary=VRHands.Right;Single(ControlAction.Ping,HandSide.Right,ControllerKey.Primary);
        var retired=VRHands.Right;VRHands.Right=new(HandSide.Right);Frames(40);VisiblePair();Key(VRHands.Left,null);Key(VRHands.Right,ControllerKey.Primary);
        Check(Model(retired)==null && retired.Rig.Root.GetComponent<Renderer>().enabled,"A replaced hand must release its old model and restore its renderers");
        ControlsTutorial.Teardown();
        // Preserve the original reversible 0.22s animation, including turning it around
        // on either side of the hand/controller crossover.
        ControlsTutorial.Apply(Index(ControlAction.WorldDrag));Frames(15);
        var model=Model(VRHands.Left);
        ControlsTutorial.Apply(Index(ControlAction.CardTake));Frames(1);
        Check(ReferenceEquals(model,Model(VRHands.Left)) && model!.transform.localScale.x>0
            && model.transform.localScale.x<1,"Returning to hands must animate instead of popping");
        ControlsTutorial.Apply(Index(ControlAction.WorldDrag));Frames(15);VisiblePair();
        Check(ReferenceEquals(model,Model(VRHands.Left)),"Reversing a swap must retain the live model");
        ControlsTutorial.Apply(Index(ControlAction.CardTake));Frames(6);
        Check(VRHands.Left.Rig.Root.GetComponent<Renderer>().enabled && Model(VRHands.Left)!=null,
            "The hand must return at the original animation crossover before model retirement");
        ControlsTutorial.Apply(Index(ControlAction.WorldDrag));Frames(15);VisiblePair();
        ControlsTutorial.Apply(Index(ControlAction.FingertipPick));Frames(15);HandPair();
        ControlsTutorial.Teardown();
        ModelLoss();
        Console.WriteLine($"Tutorial controllers: {count} runtime assertions passed.");
    }
}
