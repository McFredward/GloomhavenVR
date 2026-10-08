using System.Reflection;
using System.Reflection.Emit;
using GloomhavenVR.Core;
using GloomhavenVR.Quest;
using UnityEngine;
using UnityEngine.UI;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    static Transform? Find(Transform parent, string name)
    {
        if (parent.gameObject.name == name) return parent;
        foreach (Transform child in parent.children)
        { Transform? found = Find(child, name); if (found is not null) return found; }
        return null;
    }
    static string Text(QuestLoadingView view, string name) => Find(view.transform, name)!.GetComponent<Text>().text;
    static Camera Head(string name) => new GameObject(name).AddComponent<Camera>();
    static QuestLoadingView View(QuestGameModLifecycle life) => (QuestLoadingView)typeof(QuestGameModLifecycle)
        .GetField("loadingView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(life)!;
    static void BepInExInitializerSeam()
    {
        // Only BepInEx's already-tested initializer is a seam. The actual
        // lifecycle's reflection signature, ordering and view ownership run.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("BepInEx"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("BepInEx").DefineType("BepInEx.QuestStandalone", TypeAttributes.Public);
        var method = type.DefineMethod("Initialize", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] { typeof(string) });
        method.GetILGenerator().Emit(OpCodes.Ret); type.CreateType();
        AppDomain.CurrentDomain.AssemblyResolve += (_, name) => new AssemblyName(name.Name).Name == "BepInEx" ? assembly : null;
    }
    static void Main()
    {
        Check(QuestLoadingView.Percent(5, 0) == -1, "unmeasured byte phase invented a percentage");
        Check(QuestLoadingView.Percent(-1, 100) == 0, "negative bytes escaped bounds");
        Check(QuestLoadingView.Percent(long.MaxValue - 1, long.MaxValue) == 99, "rounded partial count claimed byte completion");
        var head = Head("existing real head"); var view = QuestLoadingView.Create(head, 27);
        Check(view.Available && view.transform.parent == head.transform, "actual owner/view unavailable");
        view.UpdateOverall("Preparing game startup", 46);
        Check(Text(view, "Progress percentage") == "46 %", "single total differs from caller");
        Check(Math.Abs(((RectTransform)Find(view.transform, "Progress")!).anchorMax.x - .46f) < .00001f, "bar differs from total");
        view.UpdateOverall("Preparing game startup", 10);
        Check(Text(view, "Progress percentage") == "46 %", "single total rewound on file/phase change");
        view.UpdateOverall("Preparing game startup", 100);
        Check(Text(view, "Progress percentage") == "99 %", "unobserved handover claimed 100 percent");
        view.UpdateOverall("Preparing game startup", 100, true);
        Check(Text(view, "Progress percentage") == "100 %", "observed native handover lost completion");
        Check(Find(view.transform, "Preparation step") == null && Find(view.transform, "Current file progress") == null
            && Find(view.transform, "Current file name") == null, "developer file/stage rows leaked into startup");
        Check(!Find(view.transform, "Loading phase")!.GetComponent<Text>().supportRichText, "startup label became markup");
        var canvas = view.GetComponent<Canvas>(); var logo = Find(view.transform, "Original GloomhavenVR logo")!.GetComponent<RawImage>();
        var replacement = Head("observed replacement head"); int cameras = GameObject.All.Count(g => g.GetComponent<Camera>() != null);
        view.Retarget(replacement, 24);
        Check(ReferenceEquals(canvas, view.GetComponent<Canvas>()) && ReferenceEquals(logo.texture, Resources.Logo), "retarget recreated original artwork");
        Check(canvas.worldCamera == replacement && view.transform.parent == replacement.transform && view.gameObject.layer == 24
            && logo.gameObject.layer == 24 && Find(view.transform, "Progress")!.gameObject.layer == 24, "retarget owner/layer not adopted");
        Check(!head.transform.children.Contains(view.transform), "retarget retained previous camera parent");
        Check(cameras == GameObject.All.Count(g => g.GetComponent<Camera>() != null), "artwork added a second XR camera");
        Check(Text(view, "Progress percentage") == "100 %", "retarget reset progress");
        replacement.Alive = false; Check(!view.Available, "dead camera remained available"); view.Retire();
        string root = Path.Combine(Path.GetTempPath(), "quest-loading-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "StreamingAssets")); Application.persistentDataPath = root;
        try
        {
            Check(!QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "desktop policy changed");
            Application.platform = RuntimePlatform.Android;
            Check(!QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "unconfigured Android policy changed");
            var warm = new GameObject("warm real startup").AddComponent<QuestGameModLifecycle>();
            warm.PrepareStartupView(); warm.UpdateStartupView("checking-mod-content", 0);
            Check(!warm.StartupViewAvailable && View(warm) == null, "warm preparation created a canvas");
            warm.UpdateStartupView("initializing-native-addressables", 98);
            Check(View(warm) == null, "warm native phase created a canvas");
            warm.UpdateStartupView("original-bootstrap-loaded", 100); warm.EndDeliveryView();
            Check(View(warm) == null, "warm handover created artwork");
            var cold = new GameObject("cold real startup").AddComponent<QuestGameModLifecycle>();
            cold.PrepareStartupView(); cold.UpdateStartupView("copying-mod-content", 5); cold.BeginDeliveryView();
            var retained = View(cold); var retainedCanvas = retained.GetComponent<Canvas>();
            Check(retained.Available && Text(retained, "Progress percentage") == "5 %", "actual installation did not request artwork");
            var retainedOwner = retained.transform.parent.gameObject.GetComponent<Camera>();
            Check(retainedOwner.stereoTargetEye == StereoTargetEyeMask.Both, "cold artwork lacks initial stereo owner");
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = Head("GloomhavenVR.VRHeadCamera");
            BepInExInitializerSeam(); var activation = cold.Activate(root);
            for (int count = 0; activation.MoveNext(); ++count) if (count > 5) throw new InvalidOperationException("actual lifecycle did not observe fixture rig");
            Check(cold.Available, "actual plugin/session lifecycle unavailable: " + cold.Failure);
            Check(ReferenceEquals(View(cold), retained) && retained.Alive && retained.Available
                && ReferenceEquals(retained.GetComponent<Canvas>(), retainedCanvas), "plugin activation retired or recreated cold view");
            Check(retained.transform.parent == QuestStandalonePlatform.HeadCamera!.transform
                && retainedCanvas.worldCamera == QuestStandalonePlatform.HeadCamera, "real rig did not adopt same artwork");
            Check(retainedOwner.stereoTargetEye == StereoTargetEyeMask.None && retainedOwner.cullingMask == 0, "startup camera still double-rendered artwork");
            cold.UpdateStartupView("copying-content", 40); cold.BeginDeliveryView();
            Check(ReferenceEquals(View(cold), retained) && Text(retained, "Progress percentage") == "40 %", "game bank restarted loading view");
            cold.UpdateStartupView("extracting-content", 3);
            Check(Text(retained, "Progress percentage") == "40 %", "second bank reset single progress");
            Application.systemLanguage = SystemLanguage.German; cold.UpdateStartupView("initializing-native-addressables", 98);
            Check(Text(retained, "Loading phase") == QuestText.Get("loadingPreparing", true), "generic localized startup label lost");
            Check(Text(retained, "Progress percentage") == "98 %", "native startup progress changed");
            cold.UpdateStartupView("loading-original-bootstrap", 100);
            Check(Text(retained, "Progress percentage") == "99 %", "requested scene claimed actual handover");
            retained.Alive = false; retained.gameObject.SetActive(false); cold.UpdateStartupView("loading-original-bootstrap", 99);
            var recovered = View(cold);
            Check(!ReferenceEquals(recovered, retained) && recovered.Available && Text(recovered, "Progress percentage") == "99 %",
                "destroyed XR child did not recover on observed head");
            cold.UpdateStartupView("original-bootstrap-loaded", 100);
            Check(Text(recovered, "Progress percentage") == "100 %", "observed scene failed to complete total");
            cold.EndDeliveryView(); Check(!recovered.Available && View(cold) == null, "observed handover retained artwork");
            warm.UpdateStartupView("failed", 40);
            Check(warm.StartupViewAvailable && Text(View(warm), "Loading phase") == QuestText.Get("startupFailed", true), "quiet warm failure was hidden");
            warm.EndDeliveryView();
            Check(QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "synthetic Quest scene captured empty anchor");
            foreach (string scene in new[] { "Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu", "", "QuestOriginalStartupExtra" })
                Check(!QuestStandalonePlatform.SuppressStartupScreen(scene), "genuine original scene suppressed: " + scene);
            Application.platform = RuntimePlatform.WindowsPlayer;
            Check(!QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "configured bridge changed desktop visibility");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Quest loading view: {checks} production checks passed; actual lifecycle/artwork ownership; native layout/headset image unverified.");
    }
}
