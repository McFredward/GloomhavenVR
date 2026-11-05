using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Hides only original draw output while an inert common results picture is present.
/// Native controllers, active flags, interactability, votes and callbacks are never disabled.</summary>
internal sealed class ScenarioResultsVisualLease : IDisposable
{
    private readonly Dictionary<CanvasRenderer, Color> _colours = new();
    private readonly Dictionary<Renderer, bool> _meshes = new();
    private readonly List<CanvasRenderer> _graphics = new();
    private readonly List<Renderer> _renderers = new();
    private readonly HashSet<CanvasRenderer> _wantedGraphics = new();
    private readonly HashSet<Renderer> _wantedMeshes = new();
    private readonly List<CanvasRenderer> _retiredGraphics = new();
    private readonly List<Renderer> _retiredMeshes = new();
    private UIWindow? _window;

    /// <summary>Reassert immediately before the common picture renders/captures.
    /// A missing/hidden mirror relinquishes the lease in the same call.</summary>
    internal bool Tick(Transform? visibleMirror)
    {
        if (visibleMirror == null || !visibleMirror.gameObject.activeInHierarchy
            || !ScenarioResultsNative.TryRead(out var parts) || !parts.Window.IsOpen
            || ReferenceEquals(visibleMirror, parts.Window.transform)
            || parts.Window.transform.IsChildOf(visibleMirror))
        { Dispose(); return false; }
        if (_window != parts.Window) { Dispose(); _window = parts.Window; }
        _wantedGraphics.Clear(); _wantedMeshes.Clear();
        Hide(parts.Window.transform, visibleMirror);
        if (parts.Ready != null && !parts.Ready.transform.IsChildOf(parts.Window.transform))
            Hide(parts.Ready.transform, visibleMirror);
        // Native clients still own their original auto-ready/wait flow. Their
        // separate wait HelpBox must not cover the common host's results picture.
        HelpBox? help = ScenarioResultsNative.ClientWaitHelp;
        if (help != null) Hide(help.transform, visibleMirror);
        RestoreRetired();
        return true;
    }

    private void Hide(Transform root, Transform mirror)
    {
        root.GetComponentsInChildren(true, _graphics);
        foreach (CanvasRenderer renderer in _graphics)
        {
            if (renderer == null || renderer.transform.IsChildOf(mirror)) continue;
            _wantedGraphics.Add(renderer);
            Color current = renderer.GetColor();
            // Remember genuine native tween/graphic writes made between our
            // passes. A previous zero is our own draw suppression, not new state.
            if (!_colours.ContainsKey(renderer) || current.a != 0f) _colours[renderer] = current;
            renderer.SetAlpha(0f);
        }
        root.GetComponentsInChildren(true, _renderers);
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null || renderer.transform.IsChildOf(mirror)) continue;
            _wantedMeshes.Add(renderer);
            if (!_meshes.ContainsKey(renderer)) _meshes.Add(renderer, renderer.forceRenderingOff);
            renderer.forceRenderingOff = true;
        }
    }

    private void RestoreRetired()
    {
        _retiredGraphics.Clear(); _retiredMeshes.Clear();
        foreach (var pair in _colours)
            if (!_wantedGraphics.Contains(pair.Key!))
            {
                if (pair.Key != null && pair.Key.GetColor().a == 0f) pair.Key.SetColor(pair.Value);
                _retiredGraphics.Add(pair.Key!); // dictionary keys are never CLR null; Unity may have destroyed the native object
            }
        foreach (var pair in _meshes)
            if (!_wantedMeshes.Contains(pair.Key!))
            {
                if (pair.Key != null && pair.Key.forceRenderingOff) pair.Key.forceRenderingOff = pair.Value;
                _retiredMeshes.Add(pair.Key!); // retain a destroyed Unity key only long enough to remove its receipt
            }
        foreach (CanvasRenderer renderer in _retiredGraphics) _colours.Remove(renderer);
        foreach (Renderer renderer in _retiredMeshes) _meshes.Remove(renderer);
    }

    public void Dispose()
    {
        foreach (var pair in _colours)
            if (pair.Key != null && pair.Key.GetColor().a == 0f) pair.Key.SetColor(pair.Value);
        foreach (var pair in _meshes)
            if (pair.Key != null && pair.Key.forceRenderingOff) pair.Key.forceRenderingOff = pair.Value;
        _colours.Clear(); _meshes.Clear(); _graphics.Clear(); _renderers.Clear();
        _wantedGraphics.Clear(); _wantedMeshes.Clear(); _retiredGraphics.Clear(); _retiredMeshes.Clear(); _window = null;
    }
}
