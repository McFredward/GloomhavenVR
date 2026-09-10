using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class FanInteractionVectors
{
    internal static void Run(Harness t)
    {
        t.Case("fan order: owned hand reordering survives phases and map/scenario card reconstruction");
        var memory = new FanOrderMemory();
        var scratch = new List<int>();
        var map = new List<int> { 10, 20, 30, 40 };
        List<int> order = memory.Apply("A", map, scratch, id => id);
        FanOrderMemory.Insert(order, 40, 20, int.MinValue);
        var action = new List<int> { 30, 10, 20, 40 };
        memory.Apply("A", action, scratch, id => id);
        Expect(t, action, 10, 40, 20, 30);
        // Different native card instances retain their class IDs when scenario cards are built.
        var scenario = new List<(int Instance, int Id)> { (101, 20), (102, 40), (103, 10), (104, 30) };
        memory.Apply("A", scenario, new List<(int Instance, int Id)>(), card => card.Id);
        for (int i = 0; i < action.Count; i++) t.Equal(action[i], scenario[i].Id, "map order becomes scenario order");
        var duringRest = new List<int> { 20, 30 };
        memory.Apply("A", duringRest, scratch, id => id);
        var afterRest = new List<int> { 10, 20, 30, 40 };
        memory.Apply("A", afterRest, scratch, id => id);
        Expect(t, afterRest, 10, 40, 20, 30);
        var other = new List<int> { 10, 20, 30, 40 };
        memory.Apply("B", other, scratch, id => id);
        Expect(t, other, 10, 20, 30, 40);
        // Deferred take-back captures A's list; a focus change to B must not redirect it.
        FanOrderMemory.Insert(order, 10, int.MinValue, 30);
        memory.Apply("A", afterRest, scratch, id => id);
        Expect(t, afterRest, 40, 20, 30, 10);
        memory.Apply("B", other, scratch, id => id);
        Expect(t, other, 10, 20, 30, 40);
        for (int owner = 0; owner < 2; owner++)
        for (int normal = 0; normal < 2; normal++)
        for (int same = 0; same < 2; same++)
            t.Equal(owner == 1 && normal == 1 && same == 1,
                FanOrderMemory.CanReorder(owner == 1, normal == 1, same == 1),
                "only the owned current normal hand permits reordering; pile choices and stale actors refuse");

        t.Case("fan poses: anonymous injections preserve covered plucks and two-hand reorders");
        var into = new int[4];
        int[] before = { 2, 0, 3, 1 };
        t.True(FanReflowMap.TryJoin(before, 4, new[] { 2, 3, 1 }, 3, 16, into), "covered pluck needs no face or held-card name");
        for (int i = 0; i < 3; i++) t.Equal(new[] { 0, 2, 3 }[i], into[i], "only the plucked resident disappears");
        t.True(FanReflowMap.TryJoin(before, 4, new[] { 3, 2 }, 2, 16, into), "two fists leave a valid positional injection");
        t.Equal(2, into[0], "reordered survivor keeps pose"); t.Equal(0, into[1], "second survivor keeps pose");
        t.True(FanReflowMap.TryJoin(new[] { 2, 3 }, 2, new[] { 3, 0, 2 }, 3, 16, into), "return may also commit a reorder");
        t.Equal(1, into[0], "return keeps first survivor"); t.Equal(-1, into[1], "returned card is a new resident"); t.Equal(0, into[2], "return keeps final survivor");
        t.True(!FanReflowMap.TryJoin(before, 4, new[] { 2, 2 }, 2, 16, into), "duplicate injection cannot move other cards");
        t.True(!FanReflowMap.TryJoin(null, 4, before, 4, 16, into), "missing previous order cannot invent geometry");
        t.True(!FanReflowMap.TryJoin(before, 4, new[] { 16 }, 1, 16, into), "seat outside protocol domain refused");
        t.True(!FanReflowMap.TryJoin(before, 4, before, 4, 16, new int[3]), "short output rejected");
        t.True(FanReflowMap.SameSource(1, 1, before, before), "unchanged canonical list permits a positional join");
        t.True(!FanReflowMap.SameSource(1, 2, before, before), "hand-to-discard transition refuses old addresses");
        t.True(!FanReflowMap.SameSource(1, 1, before, new[] { 0, 3, 1 }), "burning a model invalidates shifted indices");
        t.True(!FanReflowMap.SameSource(1, 1, before, new[] { 2, 0, 8, 1 }), "same-length model replacement invalidates indices");
        t.True(!FanReflowMap.SameSource(1, 1, before, new[] { 0, 2, 3, 1 }), "canonical sort changes invalidate indices");
        t.True(!FanReflowMap.SameSource(1, 1, new int[0], new int[0]), "missing model cannot certify a pose domain");
        t.True(!FanExchangeIdentity.Map(123).SameAs(FanExchangeIdentity.Scenario(123)), "map and scenario pose histories are separate");
        t.True(!FanExchangeIdentity.Scenario(1).SameAs(FanExchangeIdentity.Scenario(2)), "another actor cannot inherit anonymous poses");
        t.True(FanExchangeIdentity.Map(123).SameAs(FanExchangeIdentity.Map(123)), "unchanged actor retains pose history");
    }

    private static void Expect(Harness t, List<int> cards, params int[] expected)
    {
        t.Equal(expected.Length, cards.Count, "fan membership retained");
        for (int i = 0; i < expected.Length; i++) t.Equal(expected[i], cards[i], "stable character order");
    }
}
