using System;
using System.IO;
using System.Collections.Generic;
using GloomhavenVR.Net;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class CardFaceLifecycleVectors
{
    internal static void Run(Harness t, string root)
    {
        MapProjection(t);
        string Read(string path) => Regex.Replace(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR", path)),
            @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        string gate = Read("Net/RevealGate.cs");
        t.True(gate.Contains("bool secret = !ShowPeerCardFronts(actor);")
            && gate.Contains("actor != null, locallyControlled: false)"),
            "remote selection artwork cannot reopen because the viewer owns the character");
        t.True(gate.Contains("actor != null, LocallyControls(actor))"),
            "local own-card visibility and private naming keep their separate ownership rule");
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
        string map = Read("WorldUI/MapRoom/MapRoomHand.2.Fan.cs");
        t.True(map.Contains("MapCardSources.Add(card, new MapCardSource { Character = _character, Model = model })")
            && map.Contains("CharacterClassManager.Find(source.Character.CharacterID)"),
            "retired map cards retain original character and model independently of current loadout");
        t.True(map.Contains("IReadOnlyList<VRCard>? arc = CardFan.Current?.Cards")
            && map.Contains("ReferenceEquals(arc[i], card)"), "map held holes use actual native fan membership");
        t.True(held.Contains("MapRoomHand.ResolveMapPoolCard(_owner.HeldFaceMapKey(_slot)")
            && held.Contains("_owner.HeldFaceMapPoolSeat(_slot)"), "held map artwork resolves atomic pool address, not current focus loadout");
        t.True(fan.Contains("MapCardFaceProjection.TryBuild") && fan.Contains("_owner.HeldFaceMapArcSeat(1) < count"),
            "tested map projection and actual held-slot hiding both run in production");
    }

    private static void MapProjection(Harness t)
    {
        t.Case("490: exact map fan seats survive two plucks and held loadout edits");
        var arc = new List<int>();
        void Check(int n, int a, int atA, int b, int atB, int count, string expected, string reason)
        {
            t.True(MapCardFaceProjection.TryBuild(n, a, atA, b, atB, count, arc), reason + " resolves");
            t.Equal(expected, string.Join(",", arc), reason + " seats");
        }
        Check(4, -1, 255, -1, 255, 4, "0,1,2,3", "unheld loadout");
        Check(4, 1, 255, -1, 255, 3, "0,2,3", "one ordinary pluck");
        Check(4, 1, 255, 3, 255, 2, "0,2", "two ordinary plucks");
        Check(4, 1, 1, 3, 3, 4, "0,-1,2,-2", "republish retains two held holes");
        Check(4, 1, 255, 3, 2, 3, "0,2,-2", "one pluck with one retained hole");
        Check(3, -1, 3, -1, 255, 4, "0,1,2,-1", "held card deselected from loadout");
        Check(2, -1, 2, -1, 3, 4, "0,1,-1,-2", "two removed held cards retained");
        Check(0, -1, 0, -1, 255, 1, "-1", "empty loadout retains held card");
        for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
            {
                if (a == b) continue;
                t.True(MapCardFaceProjection.TryBuild(8, a, 255, b, 255, 6, arc), "all ordered pairs of two plucks");
                int at = 0;
                for (int i = 0; i < 8; i++)
                    if (i != a && i != b) t.Equal(i, arc[at++], "remaining cards retain source order");
            }
        t.True(!MapCardFaceProjection.TryBuild(4, 1, 255, 1, 255, 2, arc) && arc.Count == 0,
            "duplicate held source cannot shift other fronts");
        t.True(!MapCardFaceProjection.TryBuild(4, 1, 2, 3, 2, 4, arc) && arc.Count == 0,
            "two held cards cannot occupy one fan slot");
        t.True(!MapCardFaceProjection.TryBuild(4, 1, 4, -1, 255, 4, arc) && arc.Count == 0,
            "stale out-of-range native arc seat rejected");
        t.True(!MapCardFaceProjection.TryBuild(4, 1, 255, -1, 255, 2, arc) && arc.Count == 0,
            "unexplained model/count disagreement rejects the whole projection");
    }

    private static bool HeldActor(string source) => source.Contains("RemoteBoardFocus.ActorById(_owner.HeldFaceActorId(_slot))")
        && !source.Contains("actor = RemoteBoardFocus.DisplayedActor");
    private static bool BorrowCache(string source) => source.Contains("ReferenceEquals(borrowed.Card, card) && art.ShowsKey(borrowed.Key)")
        && source.Contains("art.MaintainMipBake();") && source.Contains("borrowed.Card = card;")
        && source.Contains("borrowed.Key = ui.fullAbilityCard.GetInstanceID();");
}
