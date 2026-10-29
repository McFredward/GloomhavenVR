using System.Collections.Generic;
using UnityEngine;
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

/// <summary>One explicitly approved native hint outside the unchanged window frame.</summary>
internal interface IMenuDepthWindowAnnotationSource : IMenuDepthSource
{
    RectTransform AnnotationRect { get; }
}

/// <summary>A source that renders into its own sampled texture and needs a per-consumer pixel demand.</summary>
internal interface IMenuDepthSampledSource : IMenuDepthSource { }

/// <summary>
/// Original printed surfaces carried by a raised native UI object. Colour and
/// text remain native; the compositor uses these exact authored silhouettes to
/// compare a foreground model fragment against the object's physical plane.
/// </summary>
internal interface IMenuDepthOcclusionSource : IMenuDepthSource
{
    IReadOnlyList<Graphic> DepthGraphics { get; }
}

/// <summary>
/// Conservative foreground footprint on the original Graphic's local z=0 plane.
/// Capture carriers may include this transparent overspill without changing the
/// native layout, input rectangle or synchronized world-window geometry.
/// </summary>
internal interface IMenuDepthBoundsSource : IMenuDepthSource
{
    bool TryGetForegroundBounds(MenuDepthEye eye, out Rect graphicLocalBounds);
}

/// <summary>Conservative original spatial ink bounds in the supplied eye's full head clip frame.</summary>
internal interface IMenuDepthHeadBoundsSource : IMenuDepthSource
{
    bool TryGetHeadBounds(MenuDepthEye eye, out Rect clipBounds);
}

/// <summary>Prime a stable native UI batch texture, then refresh its pixels for each actual head eye.</summary>
internal interface IMenuDepthDirectSource : IMenuDepthSampledSource
{
    void PrimeDirectCapture(MenuDepthEye samplingEye);
    void BeforeDirectCapture(MenuDepthEye eye);
    void CancelDirectCapture();
}
