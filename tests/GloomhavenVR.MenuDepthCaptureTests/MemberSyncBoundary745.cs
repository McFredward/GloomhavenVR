using GloomhavenVR.WorldUI;
using UnityEngine;

public static partial class MenuDepthProgram
{
    private static void MemberSyncBoundary745()
    {
        using (var scene = new Scene())
        {
            int before = PanelSupersample.MemberSyncs;
            bool prepared = false, synchronizedBeforeSource = true;
            scene.Source.OnBegin = () =>
            {
                prepared = true;
                synchronizedBeforeSource &= PanelSupersample.MemberSyncs > before
                    && ReferenceEquals(PanelSupersample.LastMemberSync, scene.Entry);
            };
            scene.Capture();
            Check(prepared && synchronizedBeforeSource,
                "membership sync boundary precedes actual native source preparation for the exact entry");
            int syncs = PanelSupersample.MemberSyncs, begins = scene.Source.Begins;
            PanelSupersample.CaptureAgain(scene.Head);
            Check(PanelSupersample.MemberSyncs == syncs + 1
                && ReferenceEquals(PanelSupersample.LastMemberSync, scene.Entry)
                && PanelSupersample.LastMemberSyncCacheFrame == Time.frameCount && scene.Source.Begins == begins,
                "cached head dispatch observes membership before its cache shortcut without another native draw");
            scene.Entry.DisplayImage.enabled = false; scene.Entry.MenuDepthFrame = -1;
            syncs = PanelSupersample.MemberSyncs;
            PanelSupersample.CaptureAgain(scene.Head);
            Check(PanelSupersample.MemberSyncs == syncs + 1
                && ReferenceEquals(PanelSupersample.LastMemberSync, scene.Entry) && scene.Entry.MenuDepthDisplay == null,
                "hidden head dispatch still observes membership before visibility retirement");
            PanelSupersample.Clear(); syncs = PanelSupersample.MemberSyncs;
            PanelSupersample.CaptureAgain(scene.Head);
            Check(PanelSupersample.MemberSyncs == syncs,
                "retired native entries never receive another membership sync dispatch");
        }
    }
}
