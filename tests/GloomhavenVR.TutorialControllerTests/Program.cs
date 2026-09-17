using GloomhavenVR.Compat;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;
static class Program {
    static int count;
    static void Check(bool value,string message) { count++; if(!value) throw new Exception(message); }
    static GameObject? Model(VRHand hand)=>hand.transform.Children.FirstOrDefault(t=>t.name.StartsWith("GloomhavenVR.Controller_"))?.gameObject;
    static void Frames(int number) { for(int i=0;i<number;i++) { Time.unscaledTime+=Time.unscaledDeltaTime; ControlsTutorial.Tick(); } }
    static void VisiblePair() {
        foreach(var hand in new[]{VRHands.Left,VRHands.Right}) {
            var model=Model(hand);
            Check(model!=null && model.transform.localScale.x>0, "Both controller models must remain visible throughout every lesson step");
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
    static void Main() {
        ControlsTutorial.Apply(0); Frames(1);VisiblePair();
        Check(Model(VRHands.Left)!.transform.localScale.x<1, "Controller entry must remain animated");
        Check(VRHands.Left.Rig.Root.GetComponent<Renderer>().enabled,"Ordinary hand must remain until controller crossover");
        Frames(15);
        foreach(var primary in new[]{HandSide.Left,HandSide.Right}) {
            VRHands.Primary=primary==HandSide.Left?VRHands.Left:VRHands.Right;
            var firstLeft=Model(VRHands.Left);var firstRight=Model(VRHands.Right);
            foreach(int index in Enumerable.Range(0,ControlsLesson.Steps.Length)) {
                ControlsTutorial.Apply(index);Frames(15);VisiblePair();
                Check(ReferenceEquals(firstLeft,Model(VRHands.Left))&&ReferenceEquals(firstRight,Model(VRHands.Right)),"Changing tasks must retain both controller instances");
                var step=ControlsLesson.Steps[index];var hands=ControlsLesson.Availability(in step).Hands;
                Key(VRHands.Left,(hands&ControlsLesson.LessonHands.Left)!=0?step.Key:null);
                Key(VRHands.Right,(hands&ControlsLesson.LessonHands.Right)!=0?step.Key:null);
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
        ControlsTutorial.Apply(0);Frames(15);VisiblePair();Key(VRHands.Left,null);Key(VRHands.Right,null);ControlsTutorial.Teardown();
        GloomhavenVR.WorldUI.WorldUIAssets.MissingRight=true;
        ControlsTutorial.Apply(0);Frames(2);
        Check(Model(VRHands.Left)!=null && Model(VRHands.Right)==null,"Missing one-sided asset must preserve the available controller");
        Check(VRHands.Right.Rig.Root.GetComponent<Renderer>().enabled,"Missing controller asset must retain its ordinary hand");
        GloomhavenVR.WorldUI.WorldUIAssets.MissingRight=false;Frames(40);VisiblePair();
        VRHands.Primary=VRHands.Right;Single(ControlAction.Ping,HandSide.Right,ControllerKey.Primary);
        var retired=VRHands.Right;VRHands.Right=new(HandSide.Right);Frames(40);VisiblePair();Key(VRHands.Left,null);Key(VRHands.Right,ControllerKey.Primary);
        Check(Model(retired)==null && retired.Rig.Root.GetComponent<Renderer>().enabled,"A replaced hand must release its old model and restore its renderers");
        ControlsTutorial.Teardown();
        Console.WriteLine($"Tutorial controllers: {count} runtime assertions passed.");
    }
}
