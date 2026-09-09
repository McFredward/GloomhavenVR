namespace GloomhavenVR.Net;

/// <summary>Logical visibility is independent of the object kept active during its dissolve.</summary>
internal sealed class RemoteCapVisibility
{
    private bool _requested;

    internal RemoteCapVisibility(bool initiallyVisible) => _requested = initiallyVisible;

    internal bool Change(bool visible)
    {
        if (_requested == visible) return false;
        _requested = visible;
        return true;
    }
}
