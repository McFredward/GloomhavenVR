using System.Collections.Generic;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardResidentVectors
{
    internal static void Run(Harness t)
    {
        t.Case("resident cards preserve their own poses across reorder, insertion and removal");
        var map = new int[5];
        foreach (int[] previous in Permutations(new[] { 11, 22, 33, 44 }))
        foreach (int[] next in Permutations(new[] { 11, 22, 33, 44 }))
        {
            t.True(CardResidentMap.TryBuild(previous, next, map), "equal-count reorders are representable");
            for (int i = 0; i < next.Length; i++)
                t.Equal(next[i], previous[map[i]], "the pose follows the same resident card");
        }
        t.True(CardResidentMap.TryBuild(new[] { 11, 22, 33 }, new[] { 99, 11, 33 }, map)
            && map[0] == -1 && map[1] == 0 && map[2] == 2, "a newcomer cannot steal a later resident's pose");
        t.True(CardResidentMap.TryBuild(new[] { 11, 22, 33, 44 }, new[] { 11, 44 }, map)
            && map[0] == 0 && map[1] == 3, "multiple removals keep surviving positions");
        t.True(CardResidentMap.TryBuild(new[] { 11, 33 }, new[] { 11, 22, 33, 44 }, map)
            && map[0] == 0 && map[1] == -1 && map[2] == 1 && map[3] == -1, "insertions seed only new cards");
        t.True(!CardResidentMap.TryBuild(new[] { 11, 11 }, new[] { 11 }, map), "duplicate previous identities are refused");
        t.True(!CardResidentMap.TryBuild(new[] { 11 }, new[] { 11, 11 }, map), "duplicate incoming identities are refused");
        t.True(!CardResidentMap.TryBuild(new[] { 11 }, new[] { 11, 22 }, new int[1]), "short destination buffers are refused");
    }

    private static IEnumerable<int[]> Permutations(int[] source)
    {
        if (source.Length == 0) { yield return source; yield break; }
        for (int i = 0; i < source.Length; i++)
        {
            var tail = new List<int>(source);
            tail.RemoveAt(i);
            foreach (int[] rest in Permutations(tail.ToArray()))
            {
                var row = new int[source.Length];
                row[0] = source[i];
                for (int j = 0; j < rest.Length; j++) row[j + 1] = rest[j];
                yield return row;
            }
        }
    }
}
