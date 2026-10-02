using System.Collections.Generic;
using UnityEngine;

public sealed class ActorBehaviour : MonoBehaviour
{
    public GameObject? m_RootGameObject, m_AnimatedGameObject, m_Hilight;
    public TestActor Actor = new TestActor();
}
public sealed class TestActor { public TestClass Class = new TestClass(); }
public sealed class TestClass { public string ID = "NativeSpittingDrake"; }
public sealed class NativeCallbackProbe : MonoBehaviour
{
    public static int Awakes, Events;
    private void Awake() => Awakes++;
    public void NativeEvent() => Events++;
}
public sealed class NativeStateProbe : StateMachineBehaviour
{
    public static int Enters;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo state, int layer) => Enters++;
}
namespace GloomhavenVR.Cards
{
    internal static class PlayTray { internal static Shader? Shader; internal static Shader? OverlayShader() => Shader; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal const string ModOwnedNamePrefix = "VR"; }
    internal static class VRLog { internal static void Note(string area, string text) { } internal static void Alert(string area, string text) { } }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera => null; } }
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldFigures { internal static readonly HashSet<ActorBehaviour> Actors = new(); internal static bool Owns(ActorBehaviour actor) => Actors.Contains(actor); }
    internal static class NetHeldFigures { internal static readonly HashSet<ActorBehaviour> Actors = new(); internal static bool Owns(ActorBehaviour actor) => Actors.Contains(actor); }
}
