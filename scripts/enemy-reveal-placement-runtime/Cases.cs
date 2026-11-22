using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Fixture
{
    internal static class FixtureTime
    {
        internal static float unscaledTime;
        internal static float unscaledDeltaTime;
    }

    internal static class Rig
    {
        internal static class VRRigDriver
        {
            internal static Transform RigRoot;
            internal static int RigPoseVersion;
        }
    }

    internal static class Net
    {
        internal static class PeerBoardFade
        {
            internal static readonly List<Transform> Visible = new();
            internal static void CollectShownBoardRoots(List<Transform> into) => into.AddRange(Visible);
        }
    }

    internal sealed partial class PlayTray
    {
        internal static PlayTray Current;
        internal Transform Root;
        internal bool IsVisible;
    }

    internal static class Defaults { internal const float EnemyRevealBoardClearance = 0.10f; }
    internal static class WorldUIConfig
    {
        internal sealed class Config { internal float Value = Defaults.EnemyRevealBoardClearance; }
        internal static readonly Config EnemyRevealBoardClearance = new();
    }

    internal static class VRLog
    {
        internal static bool WantsDebug => false;
        internal static void Debug(string module, string text) { }
        internal static void Info(string module, string text) { }
    }

    internal static class PanelLayout
    {
        internal static float WorldScale => Rig.VRRigDriver.RigRoot.lossyScale.x;
    }

    internal static class CanvasConversion { internal static Camera WorldCamera; }
    internal sealed class ConvertedPanel
    {
        internal RectTransform HostRect;
        internal Transform HostTransform => HostRect;
        internal bool FitMeasuredOnce;
    }

    internal sealed class InitiativeTrack
    {
        internal static InitiativeTrack Instance => null;
        internal Transform enemyCardsHolder => null;
    }

    internal sealed partial class EnemyRevealSurface
    {
        private ConvertedPanel _panel;
        private bool _fitPinned, _dropLogged;
        private float _placedMetersPerPx = -1f, _lastDiagTime;
        private static bool IsDescendantOf(Transform child, Transform parent) => child.IsChildOf(parent);
        private static Transform FirstActiveMonsterCard(InitiativeTrack track) => null;

        internal EnemyRevealSurface(RectTransform host)
        {
            _panel = new ConvertedPanel { HostRect = host, FitMeasuredOnce = true };
        }
        internal void Present() => Place();
        internal Vector3 Goal => _goal;
    }

    public static class Cases
    {
        private static int _checks;

        private static void Check(bool value, string failure)
        {
            _checks++;
            if (!value) throw new InvalidOperationException(failure);
        }

        private static Transform Board(Transform parent, Vector3 offset, float scale, Material paint)
        {
            Transform root = new GameObject("control board").transform;
            root.SetParent(parent, false);
            root.localPosition = offset;
            root.localRotation = Quaternion.Euler(-10f, -12f, 9f);
            root.localScale = Vector3.one * scale;
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.transform.SetParent(root, false);
            slab.transform.localScale = new Vector3(0.64f, 0.32f, 0.043f);
            slab.GetComponent<MeshRenderer>().sharedMaterial = paint;
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.transform.SetParent(root, false);
            handle.transform.localPosition = new Vector3(0f, -0.19f, 0.024f);
            handle.transform.localScale = new Vector3(0.35f, 0.028f, 0.028f);
            handle.GetComponent<MeshRenderer>().sharedMaterial = paint;
            return root;
        }

        private static int VisiblePixels(Camera camera, string evidence = null)
        {
            camera.Render();
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var pixels = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0f, 0f, pixels.width, pixels.height), 0, 0);
            pixels.Apply();
            int count = 0;
            foreach (Color32 pixel in pixels.GetPixels32())
                if (pixel.g > 180 && pixel.r < 50 && pixel.b < 50) count++;
            if (evidence != null) File.WriteAllBytes(evidence, pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);
            RenderTexture.active = active;
            return count;
        }

        private static void Wait(EnemyRevealSurface surface, float duration)
        {
            FixtureTime.unscaledDeltaTime = 0.05f;
            for (float elapsed = 0f; elapsed < duration; elapsed += FixtureTime.unscaledDeltaTime)
            {
                FixtureTime.unscaledTime += FixtureTime.unscaledDeltaTime;
                surface.Present();
            }
        }

        public static int Run(string evidence)
        {
            _checks = 0;
            FixtureTime.unscaledTime = 0f;
            FixtureTime.unscaledDeltaTime = 0f;
            Rig.VRRigDriver.RigPoseVersion = 0;
            var rig = new GameObject("tracking rig");
            Rig.VRRigDriver.RigRoot = rig.transform;
            var targets = new GameObject("physical scene");
            var rt = new RenderTexture(420, 300, 24);
            var boardMat = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
            var revealMat = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
            try
            {
                var camera = new GameObject("head", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(rig.transform, false);
                camera.transform.localPosition = Vector3.up * 1.6f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.fieldOfView = 70f;
                camera.nearClipPlane = .02f;
                camera.farClipPlane = 30f;
                camera.targetTexture = rt;
                CanvasConversion.WorldCamera = camera;

                Transform peer = Board(rig.transform, camera.transform.localPosition + new Vector3(.36f, -.12f, .68f), .7f, boardMat);
                Net.PeerBoardFade.Visible.Add(peer);
                Bounds nativeBounds = PlayTray.MeasureBoardLocalBounds(peer);
                PlayTray.MeasureBoardLocalExtents(peer, out float top, out float halfWidth);
                Check(nativeBounds.min.y < -.19f && nativeBounds.max.z > .025f, "measured silhouette includes the visible handle");
                Check(Mathf.Abs(top - .16f) < .0001f && Mathf.Abs(halfWidth - .32f) < .0001f,
                    "legacy top and half-width use the original board extents");

                RectTransform host = (RectTransform)new GameObject("converted reveal host", typeof(RectTransform)).transform;
                host.SetParent(targets.transform, false);
                var graphic = GameObject.CreatePrimitive(PrimitiveType.Quad);
                graphic.transform.SetParent(host, false);
                graphic.GetComponent<MeshRenderer>().sharedMaterial = revealMat;
                host.sizeDelta = new Vector2(320f, 220f);
                graphic.transform.localScale = new Vector3(320f, 220f, 1f);
                var surface = new EnemyRevealSurface(host);
                surface.Present();
                Vector3 natural = camera.transform.position + new Vector3(0f, -.15f, 1.3f);
                Check(Vector3.Distance(host.position, natural) < .001f, "unobstructed narrow fit stays on the preferred gaze");

                host.sizeDelta = new Vector2(1800f, 620f);
                graphic.transform.localScale = new Vector3(1800f, 620f, 1f);
                int before = VisiblePixels(camera);
                peer.gameObject.SetActive(false);
                int beforeFull = VisiblePixels(camera);
                peer.gameObject.SetActive(true);
                Check(before > 0 && beforeFull - before > 50, "the peer covers only the wider reveal footprint");
                Wait(surface, 1.5f);
                Check(Vector3.Distance(host.position, natural) > .03f, "wide fit repositions to clear the peer board");
                int visible = VisiblePixels(camera, evidence == null ? null : Path.Combine(evidence, "fitted-peers.png"));
                peer.gameObject.SetActive(false);
                int full = VisiblePixels(camera);
                peer.gameObject.SetActive(true);
                Check(visible > 1000 && full - visible <= 2, "the real rendered rectangle stays visible past the peer board");
                foreach (Vector2 sign in new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(1f, 1f) })
                {
                    Vector3 corner = host.TransformPoint(new Vector3(sign.x * host.rect.width * .5f, sign.y * host.rect.height * .5f, 0f));
                    Vector3 viewport = camera.WorldToViewportPoint(corner);
                    Check(viewport.x > .01f && viewport.x < .99f && viewport.y > .01f && viewport.y < .99f,
                        "all four fitted corners stay inside the camera viewport");
                }
                Check(Vector3.Distance(host.position, camera.transform.position) < 1.65f, "the panel stays within reading distance");
                Check(Mathf.Abs(host.forward.y) < .0001f && Vector3.Dot(host.up, Vector3.up) > .999f, "the paper faces the headset upright");

                Vector3 standing = host.position;
                peer.localPosition += Vector3.right * 1.3f;
                Wait(surface, .65f);
                Check(Vector3.Distance(standing, host.position) < .001f, "a moving board never steers a standing reveal");

                camera.transform.localRotation = Quaternion.Euler(-5f, 55f, 0f);
                Wait(surface, .4f);
                Check(Vector3.Distance(standing, host.position) < .001f, "follow waits out the physical head dwell");
                Wait(surface, .2f);
                Check(Vector3.Distance(standing, host.position) > .04f, "a sustained gaze turn eases to a new seat");
                Vector3 goal = surface.Goal;
                camera.transform.localRotation = Quaternion.Euler(-25f, 40f, 0f);
                Wait(surface, .3f);
                Check(Vector3.Distance(goal, surface.Goal) < .001f, "the running glide keeps its bounded goal");
                Wait(surface, 1.4f);

                Vector3 localBefore = rig.transform.InverseTransformPoint(host.position);
                rig.transform.SetPositionAndRotation(new Vector3(4f, 2f, -8f), Quaternion.Euler(8f, 29f, 7f));
                rig.transform.localScale = Vector3.one * 3f;
                surface.Present();
                Vector3 localAfter = rig.transform.InverseTransformPoint(host.position);
                Check(Vector3.Distance(localAfter, localBefore) < .001f, "a world grab or scale preserves the tracking-local seat");
                Check(Mathf.Abs(host.forward.y) < .0001f, "a tilted world grab leaves the paper upright");

                Matrix4x4 left = Matrix4x4.Perspective(75f, 1.3f, .02f, 20f);
                Matrix4x4 right = left;
                right.m02 = .32f;
                right.m12 = -.22f;
                Vector2 stereo = EnemyRevealPlacement.StereoViewHalfTangents(left, right);
                Check(Mathf.Abs(stereo.x - .68f / right.m00) < .0001f
                    && Mathf.Abs(stereo.y - .78f / right.m11) < .0001f, "stereo aperture uses the tighter asymmetric eye");

                Net.PeerBoardFade.Visible.Clear();
                camera.transform.localRotation = Quaternion.identity;
                Transform own = Board(rig.transform, camera.transform.localPosition + new Vector3(0f, -.14f, .68f), 1f, boardMat);
                PlayTray.Current = new PlayTray { Root = own, IsVisible = true };
                var ownSurface = new EnemyRevealSurface(host);
                ownSurface.Present();
                Vector3 ownRelative = rig.transform.InverseTransformPoint(host.position) - camera.transform.localPosition;
                Check(ownRelative.y > -.10f, "a centred own board triggers an upward sightline clearance");
                return _checks;
            }
            finally
            {
                Net.PeerBoardFade.Visible.Clear();
                PlayTray.Current = null;
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(targets);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(boardMat);
                Object.DestroyImmediate(revealMat);
            }
        }
    }
}
