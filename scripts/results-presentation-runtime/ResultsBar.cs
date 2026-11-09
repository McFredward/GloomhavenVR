using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit ports: shared-kind participation and committed local ink receipts,
// panel allocation/pose adoption, grip tracking, badge/X/visibility diagnostics.
// Exact production SyncBar, layout, settle, tween, mesh, visual, embedded texture
// loader and shipped overlay shader execute through actual rendered rod pixels.
namespace GloomhavenVR.WorldUI
{
    internal enum SharedWindowKind { Results, ScenarioStory }
    internal static partial class WorldUIConfig
    {
        internal sealed class FixtureDial { internal float Value = 150; }
        internal static readonly FixtureDial GrabBarTweenMs = new();
    }

    internal sealed partial class GrabbableModal : IDisposable
    {
        private sealed class FixtureHandle { internal bool IsGrabbed => false; }
        private readonly GrabBarVisual _bar;
        private readonly BoxCollider _grabZone;
        private readonly ConvertedPanel _panel;
        private readonly BarSizeSettle _heightSettle = new("height"), _widthSettle = new("width");
        private readonly GrabBarTween _barTween;
        private readonly FixtureHandle _handle = new();
        private readonly string _logName = "Results complete-picture bar proof";
        private readonly bool _inkValid = true, _inkFullFrame = false;
        private readonly bool _inkReportDue = false, _inkFallbackDue = false;
        private readonly Rect _inkRect;
        private readonly float _inkPlateBottom;
        private readonly bool _shared;
        private readonly SharedWindowKind _sharedKind;
        private readonly Transform _frame;
        private readonly float _localToWorld;
        private static readonly HashSet<Mesh> Meshes = new();
        private static readonly List<GrabbableModal> Live = new();

        internal GrabbableModal(ConvertedPanel panel, Rect ink, float plateBottom,
            bool shared = true, SharedWindowKind kind = SharedWindowKind.Results)
        {
            _panel = panel; _inkRect = ink; _inkPlateBottom = plateBottom;
            _shared = shared; _sharedKind = kind;
            _frame = new GameObject("GloomhavenVR.Results.BarFrame").transform;
            _frame.SetParent(panel.HostCanvas.transform, false);
            _localToWorld = panel.HostRect.lossyScale.x;
            _frame.localScale = Vector3.one / panel.HostCanvas.transform.lossyScale.x;
            _frame.SetPositionAndRotation(panel.HostRect.position, panel.HostRect.rotation);
            _bar = GrabBarVisual.Build(_frame, "GloomhavenVR.Results.Bar", GrabBarStyle.Generic,
                GrabBarMesh.DefaultRadius, overlay: true);
            _bar.AttachLaserTarget();
            _grabZone = _frame.gameObject.AddComponent<BoxCollider>();
            _barTween = new GrabBarTween(_bar, _grabZone, _logName, visible: true);
            Live.Add(this);
        }

        internal void Apply(float manualScale = 1)
        {
            _frame.SetPositionAndRotation(_panel.HostRect.position, _panel.HostRect.rotation);
            _frame.localScale = Vector3.one * manualScale / _panel.HostCanvas.transform.lossyScale.x;
            Rect rect = _panel.HostRect.rect;
            SyncBar(rect.height * _localToWorld * .5f, rect.width * _localToWorld,
                1, rect, _localToWorld);
            // Execute the real late tween consumer deterministically beyond one leg.
            typeof(GrabBarTween).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_barTween, new object[] { .3f });
            foreach (var mesh in _bar.Root.GetComponentsInChildren<MeshFilter>()) Meshes.Add(mesh.sharedMesh);
        }

        internal Bounds DrawnBounds
        {
            get { var bounds = _bar.Renderer.bounds; foreach (var renderer in _bar.Renderers) bounds.Encapsulate(renderer.bounds); return bounds; }
        }
        internal Vector3 TargetPosition => _barTween.TargetPosition;
        internal Vector3 ZoneCentre => _frame.TransformPoint(_grabZone.center);
        internal void SetVisible(bool visible) => _frame.gameObject.SetActive(visible);
        private void SyncBarVisibility() { }
        private void SyncCloseX(Rect rect) { }
        private void SyncBadge(Rect rect, float unit, float top) { }
        private void ReportBarPlacement(Rect rect, float unit, float width, float thickness,
            float intendedTopGapPx, float mmPerPx, GrabBarLayout.Span span) { }
        public void Dispose()
        {
            _barTween.Release();
            if (_bar.Material != null) Object.DestroyImmediate(_bar.Material);
            if (_frame != null) Object.DestroyImmediate(_frame.gameObject);
            Live.Remove(this);
        }
        internal static void Cleanup()
        {
            foreach (var bar in Live.ToArray()) bar.Dispose();
            foreach (var mesh in Meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            Meshes.Clear();
            var cache = (Dictionary<string, Texture2D?>)typeof(EmbeddedTexture)
                .GetField("_cache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (var texture in cache.Values) if (texture != null) Object.DestroyImmediate(texture);
            cache.Clear();
        }
    }
}

namespace GloomhavenVR.Cards
{
    internal enum ControlBoard { Oak, Steel, Bronze }
    internal static class PlayTray
    {
        internal static Shader? OverlayShader() => Resources.Load<Shader>("OriginalShaders/GrabBarOverlay");
        internal static Shader? BoardLitShader() => null;
    }
}
