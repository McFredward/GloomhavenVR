// The real fan lifecycle and driver edge branch execute against these named
// hierarchy, animation-helper and audio-sink boundaries. No audio device, native
// clip playback or complete palm-pose calculation is simulated.
namespace GloomhavenVR.Core {
    internal static class PerfMonitor {
        internal readonly struct Measure : System.IDisposable { public void Dispose() { } }
        internal static Measure Scope(string name) => new();
    }
    internal static class VRLayers { internal static void Apply(UnityEngine.GameObject root) { } }
}

namespace GloomhavenVR.Cards {
    using UnityEngine;
    internal sealed class VRHand { internal readonly HandRig Rig=new(); }
    internal sealed class HandRig { internal readonly Transform PalmCenter=new GameObject().transform; }
    internal static class CardsConfig {
        internal sealed class Number { internal float Value=.2f; }
        internal static readonly Number FanOpenDuration=new(), FanCloseDuration=new();
    }
    internal static class CardArtPrewarm { internal static void NoteFanOpened(List<VRCard> cards) { } }
    internal sealed partial class CardFan {
        internal bool IsOpen { get; private set; }
        internal static CardFan? Current;
        internal VRHand? _hand;
        internal Transform? _root;
        internal GameObject? _overlay=new();
        internal bool _followInit;
        internal float _gazeBiasYaw, _gazeX, _layoutGazeX;
        internal float _closeElapsed=-1f, _openElapsed=-1f;
        internal int _lastFanParamSig, _insertGap=-1;
        internal readonly List<VRCard> _cards=new(){new VRCard()};
        internal int SwapFinishes, Relayouts, HoverClears;
        private void FinishSwap() => SwapFinishes++;
        private void Relayout(bool instant) => Relayouts++;
        private int FanParamSignature() => 1;
        private void ClearFingertipHover() => HoverClears++;
    }
    internal sealed partial class CardsDriver {
        private readonly CardFan _fan=new();
        private readonly VRHand _gateHand=new();
        internal int FanOpenSounds, FanCloseSounds;
        private void PlayFanEdgeSound(bool open) { if(open) FanOpenSounds++;else FanCloseSounds++; }
        internal bool FanIsOpen => _fan.IsOpen;
        internal bool FanRootVisible => _fan._root?.gameObject.activeSelf==true;
        internal bool FanCollapsing => _fan._closeElapsed==0f;
        internal void SetBurnWait(bool pending) => _burnLayoutPending=pending;
    }
}
