using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core
{
    internal static partial class PerfMonitor
    {
        internal static bool StepsActive = true;
        private static readonly Dictionary<string, Tally> Tallies = new();
        private static readonly List<Tally> CounterOrder = new();
        private static readonly System.Text.StringBuilder Sb = new();
        private static Tally Intern(string name)
        {
            if (!Tallies.TryGetValue(name, out Tally tally))
            { tally = new Tally(name); Tallies.Add(name, tally); CounterOrder.Add(tally); }
            return tally;
        }
        internal static void ResetCounters() { Counts.Clear(); Reports = 0; Tallies.Clear(); CounterOrder.Clear(); VRLog.Last = ""; }
        internal static void ReportCounters() { foreach (Tally tally in CounterOrder) tally.RollFrame(); LogCounters(1f); }
        internal static readonly Dictionary<string, long> Counts = new();
        internal static int Reports;
        internal static void Count(string name, long amount)
        {
            if (!StepsActive) return;
            Reports++;
            if (amount == 0) return;
            Intern(name).Add(amount);
            Counts.TryGetValue(name, out long before); Counts[name] = before + amount;
        }
        internal static long Value(string name) => Counts.TryGetValue(name, out long value) ? value : 0;
    }
    internal static class VRLog
    {
        internal static bool WantsDebug = true;
        internal static string Last = "";
        internal static void Info(string scope, string message) { Last = message; }
    }
    internal static class PerfConfig
    {
        internal sealed class Switch { internal bool Value = true; }
        internal static readonly Switch Attribution = new();
        private static bool _sharedReads;
        internal static int ModeReads;
        internal static bool SharedUiWindowReadsOn
        {
            get { if (GloomhavenVR.Net.Proof.Recording) ModeReads++; return _sharedReads; }
            set => _sharedReads = value;
        }
    }
}

namespace GloomhavenVR.Net
{
    internal static class Proof
    {
        internal static bool Recording;
        internal static int SourceReads;
        internal static readonly Dictionary<string, int> Reads = new();
        internal static readonly List<string> Trace = new();
        internal static string CurrentWrite = "";
        internal static Action<string>? OnCallback;
        internal static T Read<T>(string name, T value)
        {
            if (Recording)
            {
                SourceReads++;
                Reads.TryGetValue(name, out int count); Reads[name] = count + 1;
                Trace.Add("read:" + name);
            }
            return value;
        }
        internal static T Write<T>(string name, T value)
        {
            if (Recording)
            {
                CurrentWrite = name; Trace.Add("write:" + name);
            }
            return value;
        }
        internal static void Callback(string kind)
        {
            if (!Recording) return;
            string point = kind + ":" + CurrentWrite;
            Trace.Add("callback:" + point);
            OnCallback?.Invoke(point);
        }
        internal static void Reset()
        {
            Recording = false; SourceReads = 0; Reads.Clear(); Trace.Clear(); CurrentWrite = ""; OnCallback = null;
            GloomhavenVR.Core.PerfConfig.ModeReads = 0;
            GloomhavenVR.Core.PerfMonitor.ResetCounters();
        }
    }
}

// Native Unity invokes these synchronously during the actual destination writes.
public sealed class MirrorRectProbe : MonoBehaviour
{
    private void OnRectTransformDimensionsChange() => GloomhavenVR.Net.Proof.Callback("rect");
    private void OnEnable() => GloomhavenVR.Net.Proof.Callback("enable");
    private void OnDisable() => GloomhavenVR.Net.Proof.Callback("disable");
}

// A virtual source getter can run user code. It must keep both legacy observations.
public sealed class ChangingText : Text
{
    public int ReadNumber;
    public override string text
    {
        get { if (GloomhavenVR.Net.Proof.Recording) return "virtual-" + (++ReadNumber); return base.text; }
        set => base.text = value;
    }
}

public sealed class ChangingImage : Image
{
    public int ReadNumber;
    public override Color color
    {
        get => GloomhavenVR.Net.Proof.Recording ? new Color(.1f * ++ReadNumber, .4f, .6f, 1f) : base.color;
        set => base.color = value;
    }
}
