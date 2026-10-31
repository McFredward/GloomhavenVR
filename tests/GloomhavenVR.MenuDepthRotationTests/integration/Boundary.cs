using System;
using System.Collections.Generic;
using UnityEngine;

// Explicit Android platform observation plus the target runtime's codegen
// refusal. Production still discovers/reads the real private manager fields.
internal static class AndroidAotInput
{
    internal static RuntimePlatform Platform => RuntimePlatform.Android;
    internal static System.Reflection.Emit.DynamicMethod RejectEmit(string name, Type result,
        Type[] arguments, Type owner, bool skipVisibility)
        => throw new NotSupportedException("Reflection.Emit is forbidden in the selected Android AOT character path");
}

// Original manager's native state shape only. No native gameplay controller is
// started; Characters discovers these real Camera/RT/RawImage identities itself.
public sealed class Character3DDisplayManager : MonoBehaviour
{
    private Transform character3DHolder = null!;
    private bool isHidden;
    private NativeCharacter character3D = null!;
    private sealed class NativeCharacter
    {
        private readonly List<NativeModel> models;
        internal NativeCharacter(Transform main) => models = new() { new NativeModel { CharacterManager = main } };
    }
    private sealed class NativeModel { public Component CharacterManager { get; set; } = null!; }
    public object Identity => character3D;
    public bool Hidden { get => isHidden; set => isHidden = value; }
    public void Supply(Transform holder, Transform main) { character3DHolder = holder; character3D = new(main); }
    public void Replace(Transform main) => character3D = new(main);
}
public sealed class UIAdventurePartyAssemblyCharacterDisplay : MonoBehaviour { }


namespace UnityEngine.UI
{
    // Source-audited native focus contract; no gameplay/bootstrap substitute.
    public sealed class UIWindow : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
    {
        public bool IsOpen;
        public int Downs;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data) => Downs++;
    }
}

namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class PerfConfig { internal static bool Quiet = true; }
    internal static class VRLog
    {
        internal static void Warn(string scope, string text) { }
        internal static void Info(string scope, string text) { }
        internal static void Note(string scope, string text) { }
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
    namespace Surfaces { internal static class InitiativePortraitClickSound { internal static void NoticeClick(GameObject target) { } } }
}
namespace GloomhavenVR.WorldUI.MenuDepth
{
    // Only the surrounding registry boundary: retain exactly what production
    // Characters registers. Enrollment, readiness and all rendering stay original.
    internal static class MenuDepthSources
    {
        internal static readonly List<IMenuDepthSource> Registered = new();
        internal static void Register(IMenuDepthSource source) => Registered.Add(source);
        internal static void Unregister(IMenuDepthSource source) => Registered.Remove(source);
    }
    // This focused lane reads the actual Source camera colour target. The final
    // UI surface shader and direct-head compositor run in the rendering suite.
    internal static class MenuDepthDisplay
    {
        internal static Shader? CharacterShader => null;
        internal static void RequireCharacterShader() { }
        // Compile-only port for the generic raster helper's unused Resolve
        // member. This scope observes original model Camera.Render; actual
        // final color/depth resolution executes in the desktop rendering suite.
        internal static void SetHeadSampleClip(Material material, MenuDepthEye eye, Vector3 point, Vector3 normal,
            Matrix4x4 worldToFrame, Rect frame, IReadOnlyList<IMenuDepthWindowAnnotationSource> annotations,
            Func<IMenuDepthWindowAnnotationSource, Matrix4x4>? nativeToPhysical = null)
            => throw new InvalidOperationException("Final head resolution is outside the rotation-only fixture boundary.");
        internal static bool TryGetEyePositions(Camera camera, out Vector3 left, out Vector3 right)
        { left = right = default; return false; }
    }
}

// Real Camera.Render invokes this original-style image-effect callback. Its
// measured view is an oracle, never a supplied view or duplicated yaw formula.
public sealed class RotationNativeImageEffect : MonoBehaviour
{
    public Transform Ground = null!;
    public Vector3 ObservedGround;
    public int Draws;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Draws++;
        ObservedGround = GetComponent<Camera>().worldToCameraMatrix.MultiplyPoint3x4(Ground.position);
        Graphics.Blit(source, destination);
    }
}

// Native creator ancestry, private serialized links and current page state are
// fixture inputs. Projection/layout and the final rendering execute production.
public sealed class UICharacterCreatorWindow : UnityEngine.MonoBehaviour
{
    private UnityEngine.Component? nameStep, personalQuestStep;
    public void BindCreationPages(UnityEngine.Component name, UnityEngine.Component quest)
    { nameStep = name; personalQuestStep = quest; }
}
public sealed class UICharacterCreatorPersonalQuestStep : UnityEngine.MonoBehaviour
{
    private UnityEngine.Component? helpText;
    public void BindHelp(UnityEngine.Component help) => helpText = help;
}
public sealed class UIKeyboard : UnityEngine.MonoBehaviour { }
