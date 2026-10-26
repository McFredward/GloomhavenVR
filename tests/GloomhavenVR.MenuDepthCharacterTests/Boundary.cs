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
    public Transform Holder => character3DHolder;
    public bool Hidden { set => isHidden = value; get => isHidden; }
    public int DisplayCalls;
    public void Bind(Transform holder) => character3DHolder = holder;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Display(Component request, ECharacter character, string skin = "", string playAnimation = "")
    { DisplayCalls++; gameObject.SetActive(true); isHidden = false; }
}

namespace ScenarioRuleLibrary { public enum ECharacter { Brute } }

// This lane supplies explicit private-capture eyes; no direct XR camera draws.
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera { get; set; } } }

namespace GloomhavenVR.Core
{
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
