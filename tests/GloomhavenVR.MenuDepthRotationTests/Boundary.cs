using System.Collections.Generic;
using UnityEngine;

// Only the already-bound native manager lifetime and feature mode are inputs.
// No native character/gameplay bootstrap or camera/model mutation is supplied.
public sealed class Character3DDisplayManager : MonoBehaviour { }
public sealed class UIAdventurePartyAssemblyCharacterDisplay : MonoBehaviour { }
public sealed class UICharacterCreatorWindow : MonoBehaviour { }
public sealed class NativeWindowDragCanary : MonoBehaviour, UnityEngine.EventSystems.IDragHandler
{
    public int Drags;
    public void OnDrag(UnityEngine.EventSystems.PointerEventData data) => Drags++;
}

namespace UnityEngine.UI
{
    // Original native UIWindow's audited pointer contract: focus only, with its
    // own opt-out. Gameplay/transition machinery is outside this input fixture.
    public sealed class UIWindow : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.ISelectHandler
    {
        public int Downs;
        public int Selects;
        public bool DisabledClickToFocus;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data)
        { if (!DisabledClickToFocus) Downs++; }
        public void OnSelect(UnityEngine.EventSystems.BaseEventData data) => Selects++;
    }
}

namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class PerfConfig { internal static bool Quiet = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Warnings = new();
        internal static void Warn(string scope, string text) => Warnings.Add(text);
        internal static void Info(string scope, string text) { }
    }
}

namespace GloomhavenVR
{
    internal sealed class Setting { internal bool Value; internal Setting(bool value) => Value = value; }
    internal static class Plugin { internal static readonly Setting ScrollWithStickOnly = new(false); }
}

namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static readonly Setting CharacterPreviewDepth = new(true); }
    internal static class UiSoundEar { internal static void BeforeUiEvent() { } }
    internal static class ModalFallback { internal static bool ShouldSwallowMenuTabClick(GameObject target) => false; }
    internal static class VRKeyboard { internal static void NoticeClick(GameObject? target) { } }
    internal sealed class RotationMapInput { internal void EndMapPan() { } }
    namespace Surfaces
    {
        internal static class InitiativePortraitClickSound { internal static void NoticeClick(GameObject target) { } }
    }
}

namespace GloomhavenVR.Hands
{
    // Explicit XR sample/primary-hand ports. The production laser guard and its
    // actual Cancel -> UguiPointer.Cancel dispatch execute without alteration.
    internal sealed class RotationRayInput { internal bool Active = true; }
    internal sealed class RotationHandInput { internal readonly RotationRayInput Ray = new(); }
    internal static class VRHands { internal static RotationHandInput? Primary; }
}
