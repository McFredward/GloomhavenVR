using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class InitiativeSelectionOrderVectors
{
    internal static void Run(Harness t, string root)
    {
        t.Case("initiative-selection/hold-across-native-reorders");
        var order = new InitiativeSelectionOrder(6);
        int[] rows = { 10, 20 }, owner = { 20, 10 };
        float[] slots = { -120f, 80f }, output = { -999f, -999f };
        t.True(order.TryResolve(true, 1, true, rows, slots, 2, owner, 2, output), "capture settled original slots and owner's order");
        t.True(output[0] == 80f && output[1] == -120f, "actor20 takes owner's first native slot without consulting initiative values");
        foreach (float travel in new[] { 0f, 0.2f, 0.7f, 1f })
        {
            float[] tween = { -120f + 200f * travel, 80f - 200f * travel };
            t.True(order.TryResolve(true, 1, false, rows, tween, 2, owner, 2, output), "native isAnimating cannot suspend an existing selection latch");
            t.True(output[0] == 80f && output[1] == -120f, "partial and zero-travel source reorders cannot move the held portraits");
        }
        t.True(order.TryResolve(true, 1, true, new[] { 20, 10 }, slots, 2, new[] { 10, 20 }, 2, output)
            && output[0] == -120f && output[1] == 80f,
            "source sibling permutation and later owner order changes preserve actor-to-slot identity");
        t.True(order.TryResolve(true, 1, false, rows, slots, 2, Array.Empty<int>(), 0, output)
            && output[0] == 80f && output[1] == -120f, "temporary record27 omission cannot expose the viewer order");

        t.Case("initiative-selection/action-handoff-and-new-round");
        output[0] = 37f; output[1] = -42f;
        t.True(!order.TryResolve(false, 1, false, rows, slots, 2, owner, 2, output)
            && output[0] == 37f && output[1] == -42f,
            "action phase leaves native animation untouched even with a stale selection record");
        t.True(!order.TryResolve(true, 2, false, rows, slots, 2, owner, 2, output),
            "a new selection window never captures in-flight source geometry");
        t.True(order.TryResolve(true, 2, true, rows, new[] { -160f, 160f }, 2, rows, 2, output)
            && output[0] == -160f && output[1] == 160f, "new round adopts its own original settled layout");
        t.True(order.TryResolve(true, 3, true, rows, slots, 2, owner, 2, output)
            && output[0] == 80f && output[1] == -120f, "round identity resets a latch even if an intervening phase was not ticked");

        t.Case("initiative-selection/membership-and-atomic-validation");
        int[] larger = { 10, 30, 20 }; float[] threeSlots = { -200f, 0f, 200f }, threeOutput = { 91f, 92f, 93f };
        t.True(!order.TryResolve(true, 3, true, larger, threeSlots, 3, owner, 2, threeOutput)
            && threeOutput[0] == 91f && threeOutput[1] == 92f && threeOutput[2] == 93f,
            "a changed actor set cannot be half-permuted with an old owner snapshot");
        t.True(order.TryResolve(true, 3, true, larger, threeSlots, 3, new[] { 30, 20, 10 }, 3, threeOutput)
            && threeOutput[0] == 200f && threeOutput[1] == -200f && threeOutput[2] == 0f,
            "a complete new actor set latches its actual native slots");
        order.Reset();
        t.True(!order.TryResolve(true, 3, true, rows, slots, 2, new[] { 10, 10 }, 2, output), "duplicate owner identities refuse");
        t.True(!order.TryResolve(true, 3, true, new[] { 10, 10 }, slots, 2, rows, 2, output), "duplicate source identities refuse");
        t.True(!order.TryResolve(true, 3, true, rows, new[] { float.NaN, 20f }, 2, rows, 2, output), "nonfinite initial geometry refuses");
        t.True(!order.TryResolve(true, 3, true, rows, slots, 2, new[] { 0, 20 }, 2, output), "none identity cannot enter a selection plan");

        t.Case("initiative-selection/actual-render-writer-binding");
        string code = Code(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteInitiativeTrack.cs")));
        string tick = Method(code, "public void TickLive("), refresh = Method(code, "public void Refresh(");
        t.True(tick.IndexOf("_mirror.TickLive()", StringComparison.Ordinal) < tick.IndexOf("ApplyOwnerOverrides()", StringComparison.Ordinal)
            && tick.Contains("ApplyOwnerOverrides()") && refresh.Contains("ApplyOwnerOverrides()"),
            "both actual mirror synchronization entry points reapply owner order after source writes");
        string apply = Method(code, "private void ApplyOrderOverride(");
        t.True(apply.Contains("PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest")
            && apply.Contains("_selectionOrder.TryResolve(selecting, CardsGameApi.RoundNumber(), settled,")
            && apply.Contains("_orderResolvedX[r], p.y, p.z"),
            "rendered row x comes from the production phase/round latch while y and z remain native");
        t.True(!Regex.IsMatch(apply, @"if\s*\([^)]*isAnimating[^)]*\)\s*\{[^}]*return"),
            "a viewer animation flag cannot bypass the latched render write");
        t.True(apply.Contains("!node.SourceRow.gameObject.activeSelf")
            && apply.Contains("InitiativeTrackSurface.ReorderInProgress"),
            "pooled inactive actors are excluded and the actual adopted slide gates initial capture");
    }
    private static string Code(string source) => Regex.Replace(source,
        "@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|/\\*[\\s\\S]*?\\*/|//[^\\r\\n]*", " ");
    private static string Method(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return string.Empty;
        int start = source.IndexOf('{', at), depth = 0;
        for (int i = start; i < source.Length && start >= 0; i++)
        { if (source[i] == '{') depth++; else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1); }
        return string.Empty;
    }
}
