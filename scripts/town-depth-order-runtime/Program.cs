using System;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Core
{
    internal enum VRLogLevel { Debug }
    internal static class VRLog
    {
        internal static bool Wants(VRLogLevel level) => false;
        internal static void Debug(string channel, string message) { }
    }
}

public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool ok, string message) { _checks++; if (!ok) throw new Exception(message); }
    public static int Run()
    {
        var purse = GameObject.CreatePrimitive(PrimitiveType.Cube);
        purse.transform.position = new Vector3(0f, 0f, 1f);
        Renderer body = purse.GetComponent<Renderer>();
        var textRoot = new GameObject("original native purse inscriptions", typeof(RectTransform), typeof(Canvas));
        textRoot.transform.SetParent(purse.transform, false);
        var canvas = textRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 0;
        TownServiceDepthOrder.Bind(purse.transform);
        CanvasConversion.Panels(2f); CanvasConversion.Tick(Vector3.zero);
        Check(body.sortingOrder > 100, "near purse ranks above the farther character window");
        Check(canvas.sortingOrder > 100, "near inscriptions rank above the farther character window");
        Check(canvas.sortingOrder == body.sortingOrder + 1, "native inscription stays above its own physical purse");
        // Binding.Apply rewrites source sortingOrder for every incoming sample. Restore
        // the observer's established band without waiting for head movement or a new rank.
        body.sortingOrder = 0; canvas.sortingOrder = 0;
        TownServiceDepthOrder.Refresh(purse.transform);
        Check(body.sortingOrder > 100 && canvas.sortingOrder == body.sortingOrder + 1,
            "incoming native sort values cannot undo depth at a stable camera rank");
        int entries = CanvasConversion.Entries;
        TownServiceDepthOrder.Bind(purse.transform);
        Check(CanvasConversion.Entries == entries && CanvasConversion.Groups == 1,
            "repeated presentation binding never duplicates the distance group");
        purse.transform.position = new Vector3(0f, 0f, 3f);
        for (int i = 0; i < 8; i++) CanvasConversion.Tick(Vector3.zero);
        Check(body.sortingOrder < 100 && canvas.sortingOrder < 100,
            "far purse and inscriptions both stay behind the nearer character window");
        // A detached mirrored canvas uses the same observer-space registration. Its inherited
        // original sort order is not allowed to override real camera-relative depth.
        var remote = new GameObject("mirrored service host", typeof(RectTransform), typeof(Canvas));
        remote.transform.position = new Vector3(.1f, .1f, .5f);
        var rect = (RectTransform)remote.transform; rect.sizeDelta = Vector2.one * .2f;
        var remoteCanvas = remote.GetComponent<Canvas>(); remoteCanvas.renderMode = RenderMode.WorldSpace;
        TownServiceDepthOrder.Bind(remote.transform); CanvasConversion.Tick(Vector3.zero);
        Check(remoteCanvas.sortingOrder > 100, "mirrored native UI joins the observer's actual distance ladder");
        UnityEngine.Object.DestroyImmediate(purse);
        UnityEngine.Object.DestroyImmediate(remote);
        CanvasConversion.Tick(Vector3.zero);
        Check(CanvasConversion.Groups == 0, "retired native presentation releases distance anchors");
        return _checks;
    }
}
