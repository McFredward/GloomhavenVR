namespace GloomhavenVR.Net;

/// <summary>Shared card presentation decisions, independent of widget lifecycle and wire layout.</summary>
internal static class CardPresentationPolicy
{
    internal static bool AllowsFace(bool online, bool selectionPhase, bool actorKnown, bool locallyControlled)
        => actorKnown && (!online || locallyControlled || !selectionPhase);

    internal static bool HandMember(bool widgetSaysHand, bool modelSaysHand, bool modelSaysExited)
        => modelSaysHand || (widgetSaysHand && !modelSaysExited);
}
