using GloomhavenVR.Cards;

namespace GloomhavenVR.WireTests;

internal static class CardPresentationVectors
{
    internal static void Run(Harness t)
    {
        t.Case("card-presentation/native-pulse-parent-hide-and-return");
        // OnDisable leaves the highlight child's activeSelf true but cancels its
        // native hoverAnim. Old state/region/activeSelf-only gating fails this case.
        t.True(!CardPresentationPolicy.HighlightSettled(true, true, true, true, false),
            "a cancelled hover must restart even though its child remains activeSelf");
        for (int frame = 0; frame < 120; frame++)
            t.True(CardPresentationPolicy.HighlightSettled(true, true, true, true, true),
                "a live authored pulse survives repeated per-frame assertions");
        t.True(CardPresentationPolicy.HighlightSettled(true, true, true, false, false),
            "the selected steady look does not require a tween");
        t.True(CardPresentationPolicy.HighlightSettled(true, true, true, false, false),
            "the off look does not start an animation");
        t.True(!CardPresentationPolicy.HighlightSettled(false, true, true, true, true),
            "selected-to-hover changes still apply even if another tween is alive");
        t.True(!CardPresentationPolicy.HighlightSettled(true, false, true, true, true),
            "switching from the action region to its default chip moves the pulse");
        t.True(!CardPresentationPolicy.HighlightSettled(true, true, false, true, true),
            "native Hide is repaired independently of cached wanted state");

        t.Case("card-presentation/rest-ready-and-undo-while-peer-selects");
        t.True(CardPresentationPolicy.RestSelectionEditable(true, true, false, false),
            "rest offered before Ready");
        t.True(!CardPresentationPolicy.RestSelectionEditable(true, true, true, false),
            "Ready hides rests while global phase remains selection for another player");
        t.True(CardPresentationPolicy.RestSelectionEditable(true, true, false, false),
            "undo Ready restores rests without a global phase change");
        t.True(!CardPresentationPolicy.RestSelectionEditable(false, true, false, false),
            "action and initiative prompts cannot expose selection controls");
        t.True(!CardPresentationPolicy.RestSelectionEditable(true, false, false, false),
            "an exhausted actor does not regain rest controls");
        t.True(!CardPresentationPolicy.RestSelectionEditable(true, true, false, true),
            "the burn/redraw decision owns an in-progress rest");
    }
}
