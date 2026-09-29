using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A physical drop already supplies the confirmation gesture. Keep only its
/// exact native prompt visually suppressed while the original hide/transaction runs.</summary>
internal static class TownServiceConfirmationMask
{
    private sealed class Entry
    {
        internal TownServiceWindowMask Mask = null!;
        internal Func<object?> Identity = null!;
        internal object? Callback;
        internal float Deadline;
    }
    private static readonly Dictionary<UIWindow, Entry> Entries = new();
    private static readonly List<UIWindow> Finished = new();

    internal static void Begin(UIWindow window, Func<object?> identity)
    {
        Remove(window);
        Entries.Add(window, new Entry { Mask = new TownServiceWindowMask((RectTransform)window.transform),
            Identity = identity, Callback = identity(), Deadline = Time.unscaledTime + 5f });
    }

    internal static bool Owns(UIWindow window) => Entries.ContainsKey(window);

    internal static void Tick()
    {
        Finished.Clear();
        foreach (var pair in Entries)
        {
            UIWindow window = pair.Key; Entry entry = pair.Value;
            // UIWindow.onHidden fires at START of a fade, before the native confirmation
            // callback. Wait for actual zero visibility; never interrupt that continuation.
            bool done = window == null || !window.IsOpen && !window.IsVisible
                || !ReferenceEquals(entry.Callback, entry.Identity());
            if (!done && Time.unscaledTime >= entry.Deadline)
            {
                VRLog.Note("WorldUI", "TOWN CONFIRMATION: native prompt did not finish hiding; restoring its original controls.");
                done = true;
            }
            if (done) Finished.Add(window!);
        }
        foreach (UIWindow window in Finished) Remove(window);
    }

    private static void Remove(UIWindow window)
    {
        if (!Entries.TryGetValue(window, out Entry? entry)) return;
        Entries.Remove(window); entry.Mask.Dispose();
    }

    internal static void Clear()
    {
        foreach (Entry entry in Entries.Values) entry.Mask.Dispose();
        Entries.Clear(); Finished.Clear();
    }
}
