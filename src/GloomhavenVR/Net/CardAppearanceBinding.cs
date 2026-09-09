namespace GloomhavenVR.Net;

/// <summary>One received address can never migrate to another model or resurrect after invalidation.</summary>
internal sealed class CardAppearanceBinding<T> where T : class
{
    private T? _model;
    internal CardAppearanceBinding(T? model) => _model = model;
    internal bool Matches(T requested, T? currentAddress)
    {
        if (!ReferenceEquals(_model, currentAddress)) _model = null;
        return _model != null && ReferenceEquals(_model, requested);
    }
}
