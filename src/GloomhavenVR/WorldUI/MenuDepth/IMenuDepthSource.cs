using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// An existing native graphic with deliberate spatial content. Eye rendering must only
/// borrow presentation state; native callbacks, animation clocks and input remain original.
/// </summary>
internal interface IMenuDepthSource
{
    Graphic Graphic { get; }
    bool IsReady { get; }
    void BeforeCapture(MenuDepthEye eye);
    void AfterCapture();
}

/// <summary>A source that renders into its own sampled texture and needs a per-consumer pixel demand.</summary>
internal interface IMenuDepthSampledSource : IMenuDepthSource { }
