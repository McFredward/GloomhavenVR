using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The original transaction controls, freely suspended beneath the resident's palm.
/// Ownership is the exact native callback, never merely a window type. Native transitions and
/// callbacks remain authoritative; withdrawing a card cancels only its still-open decision.</summary>
internal static class TownServicePalmConfirmation
{
    internal sealed class Entry
    {
        internal readonly UIWindow Window;
        internal readonly Transform Seat;
        internal readonly byte Service;
        internal readonly List<TownServiceSurface> Surfaces = new();
        private readonly Func<object?> _identity;
        private readonly Action _cancel;
        private readonly object? _callback;
        private readonly float _createdAt;
        private readonly Component[] _parts;
        private TownServiceWindowMask? _mask;
        private bool _prepared;
        internal bool FailedMerchant;
        private Transform? _frame, _palm;
        private int _lastRotationFrame = -1;
        internal bool Current => Window != null && ReferenceEquals(_callback, _identity());
        internal object? Callback => _callback;
        internal bool Open => Current && Window.IsOpen;
        internal bool ControlsAlive
        {
            get
            {
                if (!_prepared) return true;
                if (Surfaces.Count != _parts.Length) return false;
                foreach (TownServiceSurface surface in Surfaces)
                    if (!surface.Panel.IsAlive) return false;
                return true;
            }
        }
        internal Entry(UIWindow window, Transform seat, byte service, Func<object?> identity, Action cancel, Component[] parts)
        { Window = window; Seat = seat; Service = service; _identity = identity; _callback = identity(); _cancel = cancel; _parts = parts; _createdAt = Time.unscaledTime; }
        private void Place()
        {
            if (_frame == null) return;
            Transform? station = Seat.parent;
            float scale = Mathf.Max(.0001f, Mathf.Abs(Seat.lossyScale.x));
            Vector3 position = _palm != null ? _palm.position : Seat.position - Vector3.up * (.17f * scale);
            // Controls extend .20 m beneath this frame and sweep in X/Z as the
            // visitor turns. Clearing only the .955 m worktop left the enchantress
            // decision intersecting her raised book at particular head yaws. Keep
            // that entire yaw sweep above the book, with no pitch or local-reader
            // placement. Native control geometry and the owner's eased pose are
            // subsequently copied unchanged by the multiplayer mirror.
            if (station != null)
            {
                Vector3 local = station.InverseTransformPoint(position);
                local.y = Mathf.Max(local.y, Service == 3 ? 1.26f : 1.17f);
                position = station.TransformPoint(local);
            }
            // The offering seat follows the visitor's head each render frame. Copying its
            // yaw straight onto the floating decision on both Tick and LateTick made the
            // text and buttons visibly snap, especially after network sampling. Keep the
            // position attached to the palm while easing only the facing angle. The
            // second placement in the same frame must not advance the filter twice.
            if (_lastRotationFrame != Time.frameCount)
            {
                _frame.rotation = _lastRotationFrame < 0 ? Seat.rotation
                    : Quaternion.Slerp(_frame.rotation, Seat.rotation,
                        1f - Mathf.Exp(-Time.unscaledDeltaTime / .10f));
                _lastRotationFrame = Time.frameCount;
            }
            _frame.position = position;
            _frame.localScale = Vector3.one;
        }
        internal bool Tick()
        {
            if (!Current || !Window.IsOpen && !Window.IsVisible) return false;
            if (Seat == null) return false;
            if (!_prepared)
            {
                // A prior classic conversion must restore its descendants before their new
                // owner records rollback state. Pending releases are retried, never destroyed.
                if (!ModalFallback.ReleaseForComposite(Window))
                {
                    // A conversion rollback is normally a short fade. If it never yields,
                    // the controls have no physical owner. Restore the complete original
                    // dialog rather than waiting forever under an invisible mask.
                    if (Service == 1 && Time.unscaledTime - _createdAt > 1f)
                        throw new InvalidOperationException("Merchant confirmation controls remained owned by a previous conversion");
                    return true;
                }
                _frame = new GameObject("GloomhavenVR.TownService.PalmDecision").transform;
                _frame.SetParent(Seat.parent, false);
                _palm = Seat.parent != null ? Seat.parent.Find("ActivityOfferingPalm") : null;
                Place();
                for (int i = 0; i < _parts.Length; i++)
                {
                    Component part = _parts[i];
                    if (part == null) continue;
                    Vector3 offset; float width, height;
                    if (i == 0) { offset = new Vector3(0f, -.025f, -.12f); width = .46f; height = .03f; }
                    else if (i == 1) { offset = new Vector3(0f, Service == 3 ? -.11f : -.095f, -.12f); width = .46f; height = Service == 3 ? .05f : .085f; }
                    else if (i <= 3) { offset = new Vector3(i == 2 ? -.125f : .125f, -.1775f, -.12f); width = .22f; height = .045f; }
                    else { offset = new Vector3(i == 4 ? -.14f : .075f, -.065f, -.12f); width = i == 4 ? .03f : .31f; height = .03f; }
                    Surfaces.Add(new TownServiceSurface((ushort)(60 + i), (RectTransform)part.transform,
                        offset, width, _frame, Quaternion.identity, height));
                }
                _mask = new TownServiceWindowMask((RectTransform)Window.transform);
                _prepared = true;
            }
            Place();
            foreach (TownServiceSurface surface in Surfaces) surface.Tick(Vector3.zero, Quaternion.identity, 1f);
            return true;
        }
        internal void LateTick() { Place(); foreach (TownServiceSurface surface in Surfaces) surface.Tick(Vector3.zero, Quaternion.identity, 1f); }
        internal void Cancel() { if (Open) _cancel(); }
        internal void MaskFailedDecision()
        {
            if (Window != null && Window.IsOpen)
                TownServiceConfirmationMask.Begin(Window, _identity);
        }
        internal void Dispose()
        {
            // The mask remains until descendants are home, so teardown cannot flash a native
            // screen-space button. Releasing a surface never skips the game's hide continuation.
            for (int i = Surfaces.Count - 1; i >= 0; i--) Surfaces[i].Dispose();
            Surfaces.Clear(); _mask?.Dispose(); _mask = null;
            if (_frame != null) UnityEngine.Object.Destroy(_frame.gameObject);
            _frame = null;
        }
    }
    private static readonly Dictionary<UIWindow, Entry> Entries = new();
    private static readonly List<UIWindow> Finished = new();
    internal static IEnumerable<Entry> Active => Entries.Values;
    internal static bool Owns(UIWindow window) => Entries.ContainsKey(window);
    internal static bool OwnsCurrent(UIWindow? window) => window != null && Entries.TryGetValue(window, out Entry? entry) && entry.Open;
    internal static void Begin(UIItemConfirmationBox box, Transform seat)
    {
        UIWindow window = box.GetComponent<UIWindow>();
        if (Retains(window, seat)) return;
        Begin(new Entry(window, seat, 1, () => box._onConfirmedCallback, box.OnCancel,
            new Component[] { box.titleText, box.informationText, box.confirmButton, box.cancelButton }));
    }
    internal static void Begin(UIEnhancementConfirmationBox box, Transform seat)
    {
        UIWindow window = box.GetComponent<UIWindow>();
        if (Retains(window, seat)) return;
        Begin(new Entry(window, seat, 3, () => box._onConfirmCallback, box.Hide,
            new Component[] { box.titleText, box.informationText, box.confirmButton, box.cancelButton, box.enhancementIcon, box.enhancementName }));
    }
    private static bool Retains(UIWindow window, Transform seat) => Entries.TryGetValue(window, out Entry? current)
        && current.Current && current.Seat == seat && current.ControlsAlive;
    private static void Begin(Entry entry)
    {
        if (!entry.Open) return;
        if (Entries.TryGetValue(entry.Window, out Entry? previous))
        {
            if (previous.Current && previous.Seat == entry.Seat && previous.ControlsAlive) return;
            previous.Dispose(); Entries.Remove(entry.Window);
        }
        Entries.Add(entry.Window, entry);
    }
    internal static void CancelOwned(UIWindow window)
    { if (Entries.TryGetValue(window, out Entry? entry)) entry.Cancel(); }
    internal static void Tick()
    {
        Finished.Clear();
        foreach (var pair in Entries)
        {
            try { if (!pair.Value.Tick()) Finished.Add(pair.Key); }
            catch (Exception ex)
            {
                // In immersive merchant mode, a failed physical presentation must return
                // the offered card and keep the flat native dialog concealed. A separate
                // mask owns its fade while the native cancel callback retires the prompt.
                // Other services retain their original usable-dialog fallback.
                if (pair.Value.Service == 1 && WorldUIConfig.ImmersiveTownServices.Value)
                {
                    pair.Value.FailedMerchant = true;
                    try { pair.Value.MaskFailedDecision(); }
                    catch (Exception maskError)
                    {
                        Core.VRLog.Warn("WorldUI", "TOWN PALM CONFIRMATION: emergency mask failed while cancelling merchant offer: " + maskError.Message);
                    }
                    Core.VRLog.Warn("WorldUI", "TOWN PALM CONFIRMATION: cancelling unpresentable merchant offer: " + ex.Message);
                }
                else
                    Core.VRLog.Warn("WorldUI", "TOWN PALM CONFIRMATION: restoring original decision controls: " + ex.Message);
                Finished.Add(pair.Key);
            }
        }
        foreach (UIWindow window in Finished)
        {
            if (!Entries.TryGetValue(window, out Entry? entry)) continue;
            Entries.Remove(window); entry.Dispose();
            if (entry.FailedMerchant)
            {
                TownServiceMerchantHandoff.AbortUnpresentableConfirmation();
                continue;
            }
            if (entry.Seat == null) entry.Cancel();
            if (window != null && window.IsOpen) ModalFallback.RestoreClassicTownService(window);
        }
    }
    internal static void LateTick()
    { foreach (Entry entry in Entries.Values) entry.LateTick(); }
    internal static void Clear()
    {
        var closing = new List<Entry>(Entries.Values);
        Entries.Clear(); Finished.Clear();
        foreach (Entry entry in closing) { entry.Cancel(); entry.Dispose(); }
    }
}
