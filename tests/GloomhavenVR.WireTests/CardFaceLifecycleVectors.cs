using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class CardFaceLifecycleVectors
{
    internal static void Run(Harness t, string root)
    {
        string Read(string path) => Regex.Replace(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR", path)),
            @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        string held = Read("Net/Remote/RemoteHeldCardFace.cs");
        string fan = Read("Net/Remote/RemoteHandFan.cs");
        string pile = Read("Net/Remote/RemotePileFronts.cs");
        string source = Read("Net/Remote/RemoteAbilityCardSource.cs");
        string active = Read("Net/Remote/RemoteActiveCards.cs");
        t.Case("490: held artwork belongs to its atomic source actor across focus changes");
        t.True(HeldActor(held), "both held slots resolve their own sampled actor");
        t.True(!HeldActor(held.Replace("RemoteBoardFocus.ActorById(_owner.HeldFaceActorId(_slot))",
            "RemoteBoardFocus.DisplayedActor(_owner, out _)")), "negative control: board focus cannot replace held ownership");
        t.True(held.Contains("_abilityCard = found.AbilityCard") && held.Contains("_abilityCard = widget != null ? widget.AbilityCard : null")
            && held.Contains("_activeCard ?? _mapCard ?? _abilityCard"), "hand and pile holds use the actual AbilityCardUI model");
        t.True(!held.Contains("_face.AbilityCard"), "action-only FullAbilityCard model cannot misbind never-played or recovered cards");
        t.True(fan.Contains("face.SetNativeAppearance(_owner.PlayerId, actor, widget.AbilityCard)")
            && !fan.Contains("SetNativeAppearance(_owner.PlayerId, actor, full.AbilityCard)"), "hand and pick fan use current widget model");
        t.True(pile.Contains("SetNativeAppearance(_owner.PlayerId, actor, widget != null ? widget.AbilityCard : null)")
            && !pile.Contains("full.AbilityCard"), "discard and burnt browsers use current widget model");
        t.True(BorrowCache(source), "held pooled fallback preserves the successful clone and advances async artwork upkeep");
        t.True(!BorrowCache(source.Replace("ReferenceEquals(borrowed.Card, card)", "true")),
            "negative control: another model cannot inherit a borrowed front");
        t.True(!BorrowCache(source.Replace("art.ShowsKey(borrowed.Key)", "true")),
            "negative control: a hidden or replaced clone cannot satisfy the borrow cache");
        t.True(!BorrowCache(source.Replace("art.MaintainMipBake();", "")),
            "negative control: cached artwork must continue processing async art");
        t.True(fan.Contains("_owner.HeldMapSeats(out first, out second, out length)")
            && fan.Contains("i == heldSeat || i == secondHeldSeat")
            && !fan.Contains("private int HeldMapLoadoutSeat()"), "two map holds project both source seats out of the public arc");
        t.True(active.Contains("_owner.BurnOwnsActiveCard(cardId)"), "active burn renderer excludes its duplicate stationary cell");
    }

    private static bool HeldActor(string source) => source.Contains("RemoteBoardFocus.ActorById(_owner.HeldFaceActorId(_slot))")
        && !source.Contains("actor = RemoteBoardFocus.DisplayedActor");
    private static bool BorrowCache(string source) => source.Contains("ReferenceEquals(borrowed.Card, card) && art.ShowsKey(borrowed.Key)")
        && source.Contains("art.MaintainMipBake();") && source.Contains("borrowed.Card = card;")
        && source.Contains("borrowed.Key = ui.fullAbilityCard.GetInstanceID();");
}
