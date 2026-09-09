using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class CharacterDecisionParityVectors
{
    internal static void Run(Harness t, string root)
    {
        string Read(string path) => Regex.Replace(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR", path)),
            @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        string mirror = Read("WorldUI/Surfaces/CharacterDecisionMirror.cs");
        string state = Read("Net/CharacterDecisionPresentation.cs");
        string bars = Read("WorldUI/Surfaces/UseBarsSurface.cs");
        string preview = Read("WorldUI/Surfaces/DamageDecisionPreview.cs");
        string tooltip = Read("WorldUI/Surfaces/NativeUseBarTooltipHost.cs");
        string damageTip = Read("WorldUI/Surfaces/DamageTooltipSurface.cs");
        string sampler = Read("Net/NativeDecisionPromptSampler.cs");
        string prompt = Read("Net/Remote/RemoteOriginalDecisionPrompt.cs");
        t.Case("character decisions: owner authority, logical content and inert original widgets");
        t.True(CanonicalOwner(mirror), "foreign views resolve actual controller before a character snapshot");
        t.True(!CanonicalOwner(mirror.Replace("TryGetCharacterDecisionOwner", "GetAnyViewingPeer")),
            "negative control: arbitrary viewing peer fails authority guard");
        t.True(state.Contains("entry.ActorId != NetFigures.StableActorId(actor)"), "snapshot cannot cross characters");
        t.True(state.Contains("entry.Board.Visible = !entry.Suppressed && (!entry.Pending || entry.Visible)")
            && state.Contains("entry.Character.Visible = true"), "board visibility is separate from pending character content");
        t.True(mirror.Contains("TryGetLocal(actor, out picture)") && mirror.Contains("owner.PlayerId == boardOwner.PlayerId"),
            "peer boards can display locally owned characters without rebroadcast or duplicate owner rows");
        t.True(!Regex.IsMatch(mirror + state, @"\.(?:OnPointerDown|ToggleActiveBonus|ToggleShieldItem|SendGameAction|RegisterCanvas)\s*\("),
            "spectator construction has no gameplay or input registration path");
        t.True(mirror.Contains("mount.gameObject.activeInHierarchy") && mirror.Contains("_root.gameObject.SetActive(false)"),
            "detached mirror host hides when its board hides");
        t.True(preview.Contains("pending.PreDamageHealth") && preview.Contains("NetFigures.StableActorId(pending.ActorDamaged)"),
            "committed health comes from matching original damage data, not a first-poll guess");
        t.True(preview.Contains("Claimants.Contains(owner.PlayerId)") && preview.Contains("IsOwner(owner, actor)"),
            "peer damage preview requires actual character authority");
        t.True(OriginalTooltip(tooltip), "original native hover roots return from flat highlight holder");
        t.True(!OriginalTooltip(tooltip.Replace("manager.UnhighlightElement", "SkipRestore")),
            "negative control: losing tooltip restore fails");
        t.True(prompt.IndexOf("SetActive(false)", StringComparison.Ordinal) < prompt.IndexOf("Object.Instantiate", StringComparison.Ordinal)
            && prompt.IndexOf("RemoteWidgetMirror.Neutralize", StringComparison.Ordinal) < prompt.IndexOf("stage.SetActive(true)", StringComparison.Ordinal),
            "original prompt is instantiated under inactive parent and stripped before activation");
        t.True(prompt.Contains("SetOwnerFrame") && prompt.Contains("_placement.localPosition = Vector3.up * padding")
            && sampler.Contains("panel.FitContentPadding.y"), "prompt uses original fit and glyph-edge padding");
        t.True(!prompt.Contains("RemoteDecisionPrompt.Compose") && sampler.Contains("Text(line.warningTextAnimation)")
            && prompt.Contains("_clock.Advance"), "prompt uses actual native warning animation, not reconstructed text");
        t.True(!damageTip.Contains("|| DecisionDockSurface.RowFocusHidden)")
            && damageTip.Contains("UpdateFocusVisibility()"), "owner focus hides only the host, retaining logical native geometry");
        t.True(bars.Contains("_characterMirror.Tick()") && bars.Contains("ReadNativePresentation"),
            "production surface drives character mirror and exposes the same native intermediate values");
    }
    private static bool CanonicalOwner(string source) => source.Contains("TryGetCharacterDecisionOwner(actor, out RemoteAvatar? owner)")
        && source.Contains("CharacterDecisionPresentation.TryGet(owner, actor, out");
    private static bool OriginalTooltip(string source) => source.Contains("manager.highlightedElements.ContainsKey(root)")
        && source.Contains("manager.UnhighlightElement(root, unlockUI: false)");
}
