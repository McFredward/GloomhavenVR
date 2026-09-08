using System.Collections.Generic;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class FanReflowVectors
{
    internal static void Run(Harness t)
    {
        t.Case("fan reflow: card poses survive reordered plucks and returns");
        var into = new int[4];
        foreach (int[] before in Permutations(new[] { 0, 1, 2, 3 }))
        for (int held = 0; held < 4; held++)
        {
            var remaining = new List<int>();
            for (int i = 0; i < 4; i++) if (i != held) remaining.Add(i);
            foreach (int[] after in Permutations(remaining.ToArray()))
            {
                t.True(FanReflowMap.TryBuild(4, 3, held, before, 4, after, 3, into), "a reordered pluck has an unambiguous pose map");
                for (int i = 0; i < 3; i++) t.Equal(after[i], before[into[i]], "the carried pose still belongs to the same original model seat");
                t.True(FanReflowMap.TryBuild(3, 4, held, after, 3, before, 4, into), "a reordered return has an unambiguous pose map");
                for (int i = 0; i < 4; i++)
                    t.True(before[i] == held ? into[i] == -1 : into[i] >= 0 && after[into[i]] == before[i],
                        "only the returned card takes its pose from the fist; every resident keeps its own");
            }
        }
        t.True(FanReflowMap.TryBuild(4, 3, 1, null, 0, null, 0, into)
            && into[0] == 0 && into[1] == 2 && into[2] == 3, "legacy identity-order pluck keeps its original mapping");
        t.True(FanReflowMap.TryBuild(3, 4, 1, null, 0, null, 0, into)
            && into[0] == 0 && into[1] == -1 && into[2] == 1 && into[3] == 2, "legacy identity-order return keeps its original mapping");
        t.True(!FanReflowMap.TryBuild(4, 3, 1, null, 0, new[] { 0, 1, 2 }, 3, into), "a smaller arc still containing the held card is another transition");
        t.True(!FanReflowMap.TryBuild(4, 3, 1, new[] { 0, 0, 2, 3 }, 4, null, 0, into), "duplicate old seats cannot name poses");
        t.True(!FanReflowMap.TryBuild(4, 3, 1, null, 0, new[] { 0, 2, 4 }, 3, into), "out-of-range new seats are refused");
        t.True(!FanReflowMap.TryBuild(4, 2, 1, null, 0, null, 0, into), "two simultaneous removals need their own membership proof");
        t.True(!FanReflowMap.TryBuild(4, 3, -1, null, 0, null, 0, into), "unknown held seat cannot move another card");
        t.True(!FanReflowMap.TryBuild(4, 3, 1, null, 4, null, 0, into), "missing claimed old order is refused");
    }

    private static IEnumerable<int[]> Permutations(int[] values)
    {
        if (values.Length == 0) { yield return values; yield break; }
        for (int first = 0; first < values.Length; first++)
        {
            var tail = new List<int>(values); tail.RemoveAt(first);
            foreach (int[] rest in Permutations(tail.ToArray()))
            {
                var order = new int[values.Length]; order[0] = values[first];
                for (int i = 0; i < rest.Length; i++) order[i + 1] = rest[i];
                yield return order;
            }
        }
    }
}
