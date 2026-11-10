using System.Collections.Generic;
using UnityEngine;
namespace GloomhavenVR.WorldUI;
internal sealed partial class FlatScreen
{
    // Instance ownership/scrub state is an explicit fixture input. The current
    // source predicate and its actual capture records are included verbatim.
    private static FlatScreen? _coverageOwner;
    private bool _scrubActive;
    private RenderTexture? _scrubRt;
    private readonly List<Camera> _scrubbed=new();
    internal void BindPixelOwner760()=>_coverageOwner=this;
}
