using System;
using System.Threading;
using System.Threading.Tasks;

// Deliberate external boundaries: Unity camera identity/activity, XR notifications,
// current runtime/session and wall clock. No game, OpenVR, receiver or image output
// is simulated here. The lifecycle and asynchronous monitor under test are copied
// verbatim from the selected production checkout by run.py.
namespace UnityEngine
{
    internal enum RuntimePlatform { WindowsPlayer, LinuxPlayer, WindowsEditor }
    internal static class Application { internal static RuntimePlatform platform; }
    internal static class Time { internal static float unscaledTime; }
    internal sealed class Camera { internal bool isActiveAndEnabled = true; }
}
namespace UnityEngine.XR
{
    [Flags] internal enum InputDeviceCharacteristics { None = 0, HeadMounted = 1, Controller = 2 }
    internal enum XRNode { Head }
    internal readonly struct InputDevice : IEquatable<InputDevice>
    {
        internal readonly int Id;
        internal readonly bool isValid;
        internal readonly InputDeviceCharacteristics characteristics;
        internal InputDevice(int id, InputDeviceCharacteristics flags, bool valid = true)
        { Id = id; characteristics = flags; isValid = valid; }
        public bool Equals(InputDevice other) => Id == other.Id;
        public override bool Equals(object? other) => other is InputDevice device && Equals(device);
        public override int GetHashCode() => Id;
    }
    internal static class InputDevices
    {
        internal static event Action<InputDevice>? deviceConnected;
        internal static event Action<InputDevice>? deviceDisconnected;
        internal static event Action<InputDevice>? deviceConfigChanged;
        internal static InputDevice Head;
        internal static InputDevice GetDeviceAtXRNode(XRNode node) => Head;
        internal static void Connect(InputDevice device) => deviceConnected?.Invoke(device);
        internal static void Disconnect(InputDevice device) => deviceDisconnected?.Invoke(device);
        internal static void Configure(InputDevice device) => deviceConfigChanged?.Invoke(device);
        internal static int ConnectedObservers => deviceConnected?.GetInvocationList().Length ?? 0;
        internal static int DisconnectedObservers => deviceDisconnected?.GetInvocationList().Length ?? 0;
        internal static int ConfiguredObservers => deviceConfigChanged?.GetInvocationList().Length ?? 0;
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession
    { internal static bool IsRunning; internal static string RuntimeName = ""; }
    internal static class FrameNativePassthrough { internal static bool Required; }
    internal static class VRCameraPolicy { internal static UnityEngine.Camera? AllowedHead; }
}
namespace GloomhavenVR.Core.SteamLink
{
    // Only the process/diagnostic wrapper is replaced. Monitor, Snapshot, Reason,
    // Classify and Text retain their exact production implementation in Connection.cs.
    internal static partial class SteamLinkConnection
    {
        internal static Monitor State = new(_ => Task.FromResult(new Snapshot(Reason.NoSession)));
        internal static Snapshot Current => State.Current;
        internal static bool Available => Current.Available;
        internal static void Tick(bool eligible, float now) => State.Tick(eligible, now);
        internal static void Invalidate() => State.Invalidate();
        internal static void Reset() => State.Reset();
    }
}
