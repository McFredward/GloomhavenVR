using System.Collections.Generic;
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
    public void Bind(Transform holder) => character3DHolder = holder;
}

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
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Draws++;
        Graphics.Blit(source, destination);
    }
}
