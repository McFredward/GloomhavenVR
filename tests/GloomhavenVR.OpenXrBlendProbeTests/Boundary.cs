using System;
using System.Collections.Generic;

// Explicit feature-lifecycle/native-call boundary, not a headset or runtime support claim.
namespace UnityEngine.XR.OpenXR.NativeTypes
{
    public enum XrEnvironmentBlendMode { Opaque = 1, Additive = 2, AlphaBlend = 3 }
}
namespace UnityEngine.XR.OpenXR
{
    public static class OpenXRRuntime { public static string name = "fake native runtime"; }
}
namespace UnityEngine.XR.OpenXR.Features
{
    public abstract class OpenXRFeature
    {
        public static IntPtr ProcAddress;
        public static NativeTypes.XrEnvironmentBlendMode ActualMode = NativeTypes.XrEnvironmentBlendMode.Opaque;
        public static NativeTypes.XrEnvironmentBlendMode? RequestedMode;
        public static int GetCalls, SetCalls;
        public static bool ThrowGet, ThrowSet;
        protected static IntPtr xrGetInstanceProcAddr => ProcAddress;
        protected static NativeTypes.XrEnvironmentBlendMode GetEnvironmentBlendMode()
        {
            GetCalls++;
            if (ThrowGet) throw new InvalidOperationException("injected blend read failure");
            return ActualMode;
        }
        protected static void SetEnvironmentBlendMode(NativeTypes.XrEnvironmentBlendMode mode)
        {
            SetCalls++;
            if (ThrowSet) throw new InvalidOperationException("injected blend request failure");
            RequestedMode = mode; // Shipped Unity1.10 setter queues, not immediate acceptance.
        }
        public virtual bool OnInstanceCreate(ulong instance) => true;
        public virtual void OnSystemChange(ulong system) { }
        public virtual void OnInstanceDestroy(ulong instance) { }
        public virtual void OnInstanceLossPending(ulong instance) { }
        public virtual void OnSessionCreate(ulong session) { }
        public virtual void OnSessionBegin(ulong session) { }
        public virtual void OnSessionEnd(ulong session) { }
        public virtual void OnSessionExiting(ulong session) { }
        public virtual void OnSessionDestroy(ulong session) { }
        public virtual void OnSessionLossPending(ulong session) { }
        public virtual void OnEnvironmentBlendModeChange(NativeTypes.XrEnvironmentBlendMode mode) { }
        public bool Create(ulong instance) => OnInstanceCreate(instance);
        public void System(ulong system) => OnSystemChange(system);
        public void Destroy(ulong instance) => OnInstanceDestroy(instance);
        public void Loss(ulong instance) => OnInstanceLossPending(instance);
        public void Active(NativeTypes.XrEnvironmentBlendMode mode) => OnEnvironmentBlendModeChange(mode);
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        public static readonly List<string> Lines = new();
        public static void Info(string scope, string message) => Lines.Add(message);
        public static void Warn(string scope, string message) => Lines.Add(message);
    }
}
namespace GloomhavenVR
{
    internal static class FrameDefaults
    {
        private static bool _active;
        internal static int ActiveReads;
        internal static bool Active { get { ActiveReads++; return _active; } set => _active = value; }
    }
}
