using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardVisibilityVectors
{
    internal static void Run(Harness t, string repoRoot)
    {
        Provenance(t);
        t.Case("487 card visibility / one phase decision for every peer surface");
        string[] surfaces = { "hand", "held active", "held hand", "placed", "active", "discard", "lost", "item", "damage pick" };
        foreach (string surface in surfaces)
        {
            t.True(!CardPresentationPolicy.AllowsFace(true, true, true, false), surface + " is covered in selection");
            t.True(CardPresentationPolicy.AllowsFace(true, false, true, false), surface + " is open during action and damage choice");
        }
        for (int bits = 0; bits < 16; bits++)
        {
            bool online = (bits & 1) != 0, selection = (bits & 2) != 0;
            bool actor = (bits & 4) != 0, own = (bits & 8) != 0;
            bool expected = actor && (!online || own || !selection);
            t.Equal(expected, CardPresentationPolicy.AllowsFace(online, selection, actor, own), "offline/local ownership and missing actor remain distinct");
        }

        t.Case("487 membership / model hand precedes stale viewer widget type");
        t.True(CardPresentationPolicy.HandMember(false, true, false), "model hand survives a stale Round/Active/Lost widget type");
        t.True(!CardPresentationPolicy.HandMember(true, false, true), "a stale Hand widget cannot resurrect an exited card");
        t.True(CardPresentationPolicy.HandMember(true, false, false), "the existing unknown/supply fallback remains available");
        t.True(!CardPresentationPolicy.HandMember(false, false, false), "unknown non-hand widgets cannot enter the fan");
        t.True(CardPresentationPolicy.HandMember(true, true, false), "ordinary hand membership remains unchanged");
        t.True(!LegacyMembership(false, true), "negative control: the previous UI-first gate loses a model hand card");

        string gate = Read(repoRoot, "Net/RevealGate.cs");
        t.True(FaceMethodsIgnoreMembership(gate), "every CardFaces overload delegates to phase, never membership or population exceptions");
        string leaked = gate.Replace("bool secret = !ShowRoundCardFronts(actor);",
            "bool secret = !IsPublicPopulation(population) && !ShowRoundCardFronts(actor);");
        t.True(leaked != gate && !FaceMethodsIgnoreMembership(leaked), "negative control: a real population bypass is rejected");
        t.True(FaceMethodsIgnoreMembership("// IsPubliclyRevealedCard(actor, cardInstanceId)\n" + gate), "historical comments cannot satisfy or break the gate");

        string sampler = Read(repoRoot, "Net/Avatar/LocalRigSampler.cs");
        t.True(ActiveModelBeforeWidgetType(sampler), "sender names model-active cards before stale widget pile branches");
        string uiGated = sampler.Replace("int seatActive = -1;", "if (widget.CardType != CardPileType.Active) return; int seatActive = -1;");
        t.True(uiGated != sampler && !ActiveModelBeforeWidgetType(uiGated), "negative control: reintroducing the Active widget gate is rejected");
        t.True(sampler.Contains("!Cards.CardsGameApi.HandFanMember(widget, actor)"), "sender cannot misname a model hand card through a stale pile type");

        foreach (string file in new[] { "RemoteCardFx.cs", "RemoteBurnFx.cs" })
        {
            string source = Read(repoRoot, "Net/Remote/" + file);
            string call = file == "RemoteCardFx.cs" ? "RefreshFaceVisibility(f);" : "RefreshFaceVisibility(b);";
            t.True(source.Contains(call), file + " invokes the live face check from its per-frame driver");
            t.True(!source.Replace(call, "").Contains(call), file + " negative control: a method declaration cannot satisfy a removed live call");
            t.True(source.Contains("ShortRestBurn") && source.Contains("RevealGate.IsSecretSelectionPhase"), file + " preserves the covered short-rest burn through its flight");
        }
        string pile = Read(repoRoot, "Net/Remote/RemotePileFronts.cs");
        t.True(!pile.Contains("gate = Gate.BurnException;"), "covered item/pile fans cannot enter an exception branch that paints fronts");
    }

    private static void Provenance(Harness t)
    {
        t.Case("487 short-rest provenance / capture before context closes, consume once");
        t.True(CardFlightVisibility.KeepObservedCandidate(true, false, false, false), "a closing context retains a discard offer until model loss arrives");
        t.True(CardFlightVisibility.KeepObservedCandidate(true, false, true, true), "accepted model loss survives a following-round edge until its presentation claims it");
        t.True(!CardFlightVisibility.KeepObservedCandidate(true, true, false, false), "cancelled/redrawn candidate returned to hand cannot taint a later damage burn");
        t.True(!CardFlightVisibility.KeepObservedCandidate(true, true, true, false), "authoritative returned membership outranks a stale simultaneous lost entry");
        t.True(!CardFlightVisibility.KeepObservedCandidate(true, false, false, true), "unconsumed discard candidate expires with its originating round");
        t.True(!CardFlightVisibility.KeepObservedCandidate(false, false, true, false), "actor teardown cannot retain a candidate indefinitely");
        CardFlightVisibility.Reset();
        object first = new(), redraw = new(), ordinary = new();
        CardFlightVisibility.MarkShortRest(first);
        CardFlightVisibility.MarkShortRest(first);
        t.Equal((byte)1, CardFlightVisibility.ConsumeBurn(first), "repeated offer survives context closing without a phase read");
        t.Equal((byte)0, CardFlightVisibility.ConsumeBurn(first), "a later recovered-card damage burn is not mislabeled");
        CardFlightVisibility.MarkShortRest(first);
        CardFlightVisibility.Forget(first);
        CardFlightVisibility.MarkShortRest(redraw);
        t.Equal((byte)0, CardFlightVisibility.ConsumeBurn(first), "redrawn/cancelled old candidate is no longer a short-rest burn");
        t.Equal((byte)1, CardFlightVisibility.ConsumeBurn(redraw), "replacement candidate carries its own provenance");
        t.Equal((byte)0, CardFlightVisibility.ConsumeBurn(ordinary), "ordinary action damage burn remains open");
        t.Equal((byte)0, CardFlightVisibility.ConsumeBurn(null), "missing model never borrows another card's provenance");
        NetCardFx.Reset();
        NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Burnt, 1);
        NetCardFx.Report(CardFxAnchor.Slot1, CardFxAnchor.Burnt, 0);
        t.True(NetCardFx.TryDequeue(out byte a, out byte seqA, out byte flagsA), "first launch dequeues");
        t.True(NetCardFx.TryDequeue(out byte b, out byte seqB, out byte flagsB), "second launch dequeues");
        t.Equal((byte)CardFxAnchor.Slot0, (byte)NetCardFx.From(a), "covered provenance remains paired with first origin");
        t.Equal((byte)1, flagsA, "covered launch survives another queued launch");
        t.Equal((byte)CardFxAnchor.Slot1, (byte)NetCardFx.From(b), "ordinary provenance remains paired with second origin");
        t.Equal((byte)0, flagsB, "ordinary launch cannot inherit covered predecessor");
        t.Equal(unchecked((byte)(seqA + 1)), seqB, "the existing dense sequence is retained");
        t.True(!NetCardFx.TryDequeue(out _, out _, out byte empty) && empty == 0, "empty dequeue has no stale provenance");
        for (int i = 0; i < 10; i++) NetCardFx.Report(CardFxAnchor.Board, CardFxAnchor.Burnt, (byte)(i & 1));
        for (int i = 2; i < 10; i++)
        {
            t.True(NetCardFx.TryDequeue(out _, out _, out byte flags), "bounded queue retains newest event");
            t.Equal((byte)(i & 1), flags, "queue eviction cannot detach event provenance");
        }
        NetCardFx.Reset();
    }

    private static bool LegacyMembership(bool widgetHand, bool modelHand) => widgetHand && modelHand;

    private static bool ActiveModelBeforeWidgetType(string source)
    {
        int active = source.IndexOf("int seatActive = -1;", StringComparison.Ordinal);
        int widgetPile = source.IndexOf("CardPileType heldPile = widget.CardType;", StringComparison.Ordinal);
        return active >= 0 && widgetPile > active
            && !source.Substring(0, active).Contains("widget.CardType != CardPileType.Active");
    }

    private static bool FaceMethodsIgnoreMembership(string source)
    {
        int methods = 0;
        foreach (Match match in Regex.Matches(source, @"public\s+static\s+CardFaceSource\s+CardFaces\([^)]*\)\s*(=>|\{)"))
        {
            int start = match.Index + match.Length, end = start;
            if (match.Groups[1].Value == "=>") end = source.IndexOf(';', start);
            else
            {
                int depth = 1;
                for (; end < source.Length && depth > 0; end++)
                { if (source[end] == '{') depth++; else if (source[end] == '}') depth--; }
            }
            if (end < start) return false;
            string body = source.Substring(start, end - start);
            foreach (string forbidden in new[] { "IsPublicPopulation(", "IsPubliclyRevealedCard(", "IsDiscardedCard(", "CardIsPubliclyVisible(" })
                if (body.Contains(forbidden)) return false;
            methods++;
        }
        return methods == 5;
    }

    private static string Read(string root, string path) => Code(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR", path)));
    private static string Code(string source) => Regex.Replace(source,
        @"""(?:\\.|[^""\\])*""|//[^\r\n]*|/\*[\s\S]*?\*/", " ");
}
