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
        private readonly Component[] _parts;
        private TownServiceWindowMask? _mask;
        private bool _prepared;
        internal bool Current => Window != null && ReferenceEquals(_callback, _identity());
        internal bool Open => Current && Window.IsOpen;
        internal Entry(UIWindow window, Transform seat, byte service, Func<object?> identity, Action cancel, Component[] parts)
        { Window = window; Seat = seat; Service = service; _identity = identity; _callback = identity(); _cancel = cancel; _parts = parts; }
        internal bool Tick()
        {
            if (!Current || !Window.IsOpen && !Window.IsVisible) return false;
            if (Seat == null) return false;
            if (!_prepared)
            {
                // A prior classic conversion must restore its descendants before their new
                // owner records rollback state. Pending releases are retried, never destroyed.
                if (!ModalFallback.ReleaseForComposite(Window)) return true;
                for (int i = 0; i < _parts.Length; i++)
                {
                    Component part = _parts[i];
                    if (part == null) continue;
                    Vector3 offset; float width, height;
                    if (i == 0) { offset = new Vector3(0f, -.34f, -.80f); width = .46f; height = .045f; }
                    else if (i == 1) { offset = new Vector3(0f, -.405f, -.80f); width = .46f; height = .085f; }
                    else if (i <= 3) { offset = new Vector3(i == 2 ? -.125f : .125f, -.50f, -.80f); width = .22f; height = .065f; }
                    else { offset = new Vector3(i == 4 ? -.80f : .075f, -.285f, -.80f); width = i == 4 ? .04f : .31f; height = .04f; }
                    Surfaces.Add(new TownServiceSurface((ushort)(60 + i), (RectTransform)part.transform,
                        offset, width, Seat, Quaternion.identity, height));
                }
                _mask = new TownServiceWindowMask((RectTransform)Window.transform);
                _prepared = true;
            }
            foreach (TownServiceSurface surface in Surfaces) surface.Tick(Vector3.zero, Quaternion.identity, 1f);
            return true;
        }
        internal void Cancel() { if (Open) _cancel(); }
        internal void Dispose()
        {
            // The mask remains until descendants are home, so teardown cannot flash a native
            // screen-space button. Releasing a surface never skips the game's hide continuation.
            for (int i = Surfaces.Count - 1; i >= 0; i--) Surfaces[i].Dispose();
            Surfaces.Clear(); _mask?.Dispose(); _mask = null;
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
        && current.Current && current.Seat == seat;
    private static void Begin(Entry entry)
    {
        if (!entry.Open) return;
        if (Entries.TryGetValue(entry.Window, out Entry? previous))
        {
            if (previous.Current && previous.Seat == entry.Seat) return;
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
                // Fail open to the original usable dialog; never leave a payment hidden.
                Core.VRLog.Warn("WorldUI", "TOWN PALM CONFIRMATION: restoring original decision controls: " + ex.Message);
                Finished.Add(pair.Key);
            }
        }
        foreach (UIWindow window in Finished)
        {
            if (!Entries.TryGetValue(window, out Entry? entry)) continue;
            Entries.Remove(window); entry.Dispose();
            if (entry.Seat == null) entry.Cancel();
            if (window != null && window.IsOpen) ModalFallback.RestoreClassicTownService(window);
        }
    }
    internal static void LateTick()
    { foreach (Entry entry in Entries.Values) foreach (TownServiceSurface surface in entry.Surfaces) surface.Tick(Vector3.zero, Quaternion.identity, 1f); }
    internal static void Clear()
    {
        var closing = new List<Entry>(Entries.Values);
        Entries.Clear(); Finished.Clear();
        foreach (Entry entry in closing) { entry.Cancel(); entry.Dispose(); }
    }
}
