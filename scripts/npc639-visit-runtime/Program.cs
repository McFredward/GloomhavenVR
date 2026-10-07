using System;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Info(string scope, string message) { }
        internal static void Note(string scope, string message) { }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static MeshRenderer? ParchmentRenderer = null;
        internal static float EyeHeight;
        internal static int Recentered;
        internal static void NoteEyeHeight(float height) => EyeHeight = height;
        internal static void NoteRecentered() => Recentered++;
    }
}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver
    {
        private GameObject? _rigRoot;
        private Camera? _camera;
        private MapRoomSeat.Seat _mapSeat;
        internal static int RigPoseVersion;
        internal VRRigDriver(GameObject root, Camera camera, MapRoomSeat.Seat seat)
        { _rigRoot = root; _camera = camera; _mapSeat = seat; }
        internal void RunRecenter() => RecenterMap();
    }
}
public static class InteractionProgram
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) <= .001f, message + ": " + actual + " vs " + expected);
        // These are actual floor-, seated-, and eye-origin tracked local poses.
        // The host638 measured2.21m pose is intentionally preserved, not called low.
        foreach (float trackedY in new[] { -1f, 0f, .12f, .75f, 1.2f, 1.48f, 1.65f, 2.21f })
        foreach (float scale in new[] { 1f, 198.12f, 500f })
        foreach (float zoom in new[] { .5f, 1f, 2f })
        foreach (float localYaw in new[] { -135f, 0f, 37f })
        {
            var root = new GameObject("Tracked map rig");
            var head = new GameObject("Tracked head", typeof(Camera));
            var hand = new GameObject("Tracked hand");
            head.transform.SetParent(root.transform, false);
            hand.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(.24f, trackedY, -.31f);
            head.transform.localRotation = Quaternion.Euler(0f, localYaw, 0f);
            hand.transform.localPosition = new Vector3(.42f, trackedY - .5f, -.18f);
            Vector3 originalHeadLocal = head.transform.localPosition;
            Vector3 originalHandLocal = hand.transform.localPosition;
            float currentScale = scale * zoom;
            root.transform.localScale = Vector3.one * currentScale;
            root.transform.position = new Vector3(15f, 100f, -30f);
            root.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
            const float topY = 24f;
            Vector3 floor = new Vector3(-184.45f, topY - MapRoomSeat.TableTopHeightMeters * scale, .18f);
            var seat = new MapRoomSeat.Seat(scale, floor, 90f, topY, 1.2f, false, Vector3.left);
            var rig = new VRRigDriver(root, head.GetComponent<Camera>(), seat);
            int version = VRRigDriver.RigPoseVersion;
            int recentered = MapRoomDriver.Recentered;
            rig.RunRecenter();
            float clearance = (head.transform.position.y - topY) / currentScale;
            Check(clearance >= .699f, "low origin obtains the minimum map overview");
            float expected = Mathf.Max(.70f, trackedY - .78f / zoom);
            Near(clearance, expected, "lift only to the minimum without raising the view farther");
            Near((head.transform.position.x - floor.x) / currentScale, 0f,
                "entry absorbs measured tracking XZ without moving the table");
            Near((head.transform.position.z - floor.z) / currentScale, 0f,
                "entry absorbs measured tracking XZ without moving the table");
            Near(Mathf.DeltaAngle(head.transform.eulerAngles.y, 90f), 0f,
                "entry still faces the table for each independently tracked multiplayer visitor");
            Check(head.transform.localPosition == originalHeadLocal && hand.transform.localPosition == originalHandLocal,
                "only the rig moves; original headset and hand tracking stay coherent");
            Near(MapRoomDriver.EyeHeight, expected + .78f,
                "existing map report records the resulting eye distance above the map floor");
            Check(VRRigDriver.RigPoseVersion == version + 1 && MapRoomDriver.Recentered == recentered + 1,
                "entry advances the existing one-shot map lifecycle exactly once");
            Vector3 headBefore = head.transform.position;
            Vector3 rootBefore = root.transform.position;
            rig.RunRecenter();
            Check(Vector3.Distance(head.transform.position, headBefore) <= .001f
                && Vector3.Distance(root.transform.position, rootBefore) <= .001f,
                "repeated deliberate map recenter never accumulates eye lift");
            head.transform.localPosition += Vector3.down * .12f;
            Near((head.transform.position.y - topY) / currentScale, expected - .12f,
                "ordinary head movement remains free after one-shot entry");
            root.transform.position += Vector3.down * (.18f * currentScale);
            Near((head.transform.position.y - topY) / currentScale, expected - .30f,
                "ordinary flight and world movement are not clamped to spawn height");
            Object.DestroyImmediate(root);
        }
        foreach (var bounds in new[] {
            new Bounds(new Vector3(0f,-.08f,.18f), new Vector3(237.744f,.12f,180f)),
            new Bounds(Vector3.one, new Vector3(120f,.1f,20f)),
            new Bounds(Vector3.zero, new Vector3(.5f,.01f,.5f)) })
        foreach (var side in new[] { Vector3.left, Vector3.back, Vector3.right })
        {
            Check(MapRoomSeat.Solve(bounds, side, out var seat), "world, city and guildmaster parchment seats remain measurable");
            Near(seat.TopY, bounds.max.y, "top remains original native parchment top");
            Near((seat.TopY - seat.FloorPosition.y) / seat.Scale, .78f,
                "native furniture and environment floor geometry are not raised by the eye correction");
        }
        return checks;
    }
}
