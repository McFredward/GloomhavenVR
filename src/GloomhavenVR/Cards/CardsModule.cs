using GloomhavenVR.Core;

namespace GloomhavenVR.Cards;

/// <summary>
/// Demeo-style physical card hand: palm-up fan, grab/inspect, play tray with initiative
/// ordering, rest tokens, physical Ready button, top/bottom half selection.
/// Phase 3b (feat/cards). Key seams: CardsHandUI.SelectCard/UnselectCard, ProxySelectCardAction.
/// </summary>
internal sealed class CardsModule : IVRModule
{
    public string Name => "Cards";

    public void Init() => VRLog.Debug(Name, "stub initialized (Phase 3b implements the physical card hand).");
}
