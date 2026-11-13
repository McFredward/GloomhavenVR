using System;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class Program
{
    static int assertions;
    static void Check(bool value, string proof)
    { assertions++; if (!value) throw new InvalidOperationException(proof); }
    static Camera Cam(string tag = "Untagged", float depth = 0, RenderTexture? target = null) =>
        new Camera { tag = tag, depth = depth, targetTexture = target };
    static void Scene(params Camera[] cameras) => VRCameraPolicy.Cameras = cameras;

    static int Main()
    {
        try
        {
            FlatScreen.ResetClaims(); Camera.main = null; QuestStandalonePlatform.Enabled = true;
            var head = Cam(depth: 1000); VRRigDriver.HeadCamera = head;
            var screen = new FlatScreen();
            var sink = new RenderTexture { name = "GloomhavenVR.DesktopScrubSink" };
            var privateRt = new RenderTexture { name = "Character 3D assembly render texture" };
            var ui = Cam("UICamera", 1, sink);
            var preview = Cam(depth: 24, target: privateRt);
            Scene(head, preview, ui);
            Check(VRRigDriver.Resolve() == null, "unowned RT is not a native backbuffer anchor");
            screen.BeginScrub(sink);
            Check(FlatScreen.OwnsPresentationTarget(ui), "scrub target publication");
            Check(VRRigDriver.Resolve() == ui, "scrubbed original UICamera recovers menu anchor");
            Check(!FlatScreen.OwnsPresentationTarget(preview), "private preview RT excluded");
            var sameName = Cam("UICamera", 2, new RenderTexture { name = sink.name });
            Scene(preview, sameName);
            Check(VRRigDriver.Resolve() == null, "texture names cannot grant ownership");
            Scene(head, preview);
            Check(VRRigDriver.Resolve() == null, "owned head and native preview never anchor");
            Camera.main = head;
            Check(VRRigDriver.Resolve() == null, "Quest main camera cannot self-anchor");
            Camera.main = null;
            var world = Cam(depth: -2, target: sink);
            Scene(head, preview, ui, world);
            Check(VRRigDriver.Resolve() == world, "world anchor priority over UI");
            world.enabled = false;
            Check(VRRigDriver.Resolve() == ui, "disabled anchor recovers through original UI");
            Check(ui.enabled && ui.targetTexture == sink && ui.tag == "UICamera" && ui.depth == 1,
                "reference-only resolution never changes native camera");
            screen.EndScrub();
            Check(!FlatScreen.OwnsPresentationTarget(ui), "scrub target release");
            Check(VRRigDriver.Resolve() == null, "released foreign targets excluded");

            var baseRt = new RenderTexture { name = "GloomhavenVR.FlatScreenRT" };
            var glassRt = new RenderTexture { name = "GloomhavenVR.FlatScreenUIRT" };
            ui.targetTexture = baseRt;
            screen.Capture(ui, baseRt, true, false, glassRt);
            Check(FlatScreen.OwnsPresentationTarget(ui), "actual captured native target admitted");
            Scene(ui, preview, head);
            Check(VRRigDriver.Resolve() == ui, "captured UI remains usable after anchor destruction");
            ui.targetTexture = privateRt;
            Check(!FlatScreen.OwnsPresentationTarget(ui), "capture membership cannot grant a foreign target");
            Check(VRRigDriver.Resolve() == null, "game reassigned private target excluded");
            screen.Route(true); ui.targetTexture = glassRt;
            Check(VRRigDriver.Resolve() == ui, "split UI target remains reference-only anchor");
            screen.Route(false); ui.targetTexture = baseRt;
            Check(VRRigDriver.Resolve() == ui, "mono capture target remains reference-only anchor");

            QuestStandalonePlatform.Enabled = false;
            Check(VRRigDriver.Resolve() == null, "desktop captured UI eligibility unchanged");
            screen.BeginScrub(sink); world.enabled = true; world.targetTexture = sink;
            Scene(world, head, ui, preview);
            Check(VRRigDriver.Resolve() == null, "desktop owned RT eligibility unchanged");
            world.targetTexture = null;
            var high = Cam(depth: 10); Scene(head, ui, world, high);
            Check(VRRigDriver.Resolve() == high, "desktop original highest-depth world anchor");
            high.enabled = false;
            Check(VRRigDriver.Resolve() == world, "desktop disabled world camera excluded");
            Camera.main = ui;
            Check(VRRigDriver.Resolve() == ui, "desktop original Camera.main precedence unchanged");
            Camera.main = null; Scene(ui);
            Check(VRRigDriver.Resolve() == null, "desktop original UI-only fallback unchanged");

            QuestStandalonePlatform.Enabled = true; Scene(ui, head, preview);
            var main = Cam("MainCamera", target: privateRt); Camera.main = main;
            Check(VRRigDriver.Resolve() == main, "original tagged main camera precedence unchanged");
            Camera.main = null; Scene(world, ui, preview, head);
            for (int i = 0; i < 100; i++) VRRigDriver.Resolve();
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) VRRigDriver.Resolve();
            Check(GC.GetAllocatedBytesForCurrentThread() == start, "anchor scan has no steady managed allocation");
            Console.WriteLine("PASS Quest original menu camera ownership: " + assertions + " assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error.Message); return 1; }
    }
}
