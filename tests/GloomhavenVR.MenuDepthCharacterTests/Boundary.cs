using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ScenarioRuleLibrary;
using UnityEngine;

// Native scene/content inputs only: this provider represents the real manager's
// source camera, stable holder and hidden flag. Actual production discovers the
// camera/RT/RawImage identities; no binding result, projection or draw is supplied.
public sealed class Character3DDisplayManager : MonoBehaviour
{
    private Transform character3DHolder = null!;
    private bool isHidden;
    private NativeCharacter character3D = null!;
    private sealed class NativeCharacter
    {
        private readonly List<NativeModel> models;
        internal NativeCharacter(Transform[] roots) { models = new List<NativeModel>(); foreach (Transform root in roots) models.Add(new NativeModel { CharacterManager = root }); }
    }
    private sealed class NativeModel { public Component CharacterManager { get; set; } = null!; }
    public Transform Holder => character3DHolder;
    public object Original => character3D;
    public bool Hidden { set => isHidden = value; get => isHidden; }
    public int DisplayCalls;
    public void Bind(Transform holder) { character3DHolder = holder; character3D = new NativeCharacter(new[] { holder }); }
    public void ReplaceOriginal(params Transform[] roots) => character3D = new NativeCharacter(roots);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Display(Component request, ECharacter character, string skin = "", string playAnimation = "")
    { DisplayCalls++; gameObject.SetActive(true); isHidden = false; }
}

namespace ScenarioRuleLibrary { public enum ECharacter { Brute } }

// This lane supplies explicit private-capture eyes; no direct XR camera draws.
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera { get; set; } } }

// User preference is an input; production discovery and resource retirement run unchanged.
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig
    {
        internal sealed class Setting { internal bool Value = true; }
        internal static readonly Setting CharacterPreviewDepth = new();
    }
}

// This lane owns live character rendering. General decorative-owner discovery
// is exercised by the real Driver/performance lane; its unused observer compile
// ports deliberately do not supply any replacement discovery/capture behavior.
namespace GloomhavenVR.WorldUI.MenuDepth
{
    internal static class MenuDepthDriver
    {
        internal static IEnumerable<System.Reflection.MethodBase> NativeOwnerLifecycles()
            => System.Array.Empty<System.Reflection.MethodBase>();
        internal static void ObserveGraphic(UnityEngine.UI.Graphic graphic, bool moved) { }
        internal static void ForgetGraphic(UnityEngine.UI.Graphic graphic) { }
        internal static void ObserveOwner(MonoBehaviour owner) { }
    }
}
// Unused general-window observer type port. No UIWindow lifecycle is patched or
// exercised in this character-only lane; native owner coverage lives separately.
namespace UnityEngine.UI
{
    public sealed class UIWindow : MonoBehaviour { public bool IsOpen; private void OnEnable() { } }
}

namespace GloomhavenVR.Core
{
    // Only the performance clock is a port; actual source/camera rendering runs.
    internal static class PerfMonitor
    {
        internal readonly struct Measure : System.IDisposable { public void Dispose() { } }
        internal static Measure Scope(string name) => default;
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Note(string scope, string text) => Messages.Add(text);
        internal static void Warn(string scope, string text) => Messages.Add(text);
    }
}

public sealed class CharacterNativeImageEffect : MonoBehaviour
{
    public int Draws;
    public System.Action? BeforeBlit;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Draws++;
        BeforeBlit?.Invoke();
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
