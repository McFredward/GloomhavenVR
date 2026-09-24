using System;
using System.Collections.Generic;
using System.Globalization;
using MapRuleLibrary.Adventure;

namespace GloomhavenVR.Net;

/// <summary>Public saved run provenance for map continuations. Sample only at native opening edges.</summary>
internal static class SharedMapRunIdentity
{
    internal static uint Key
    {
        get
        {
            var map = AdventureState.MapState;
            if (map == null) return 0;
            // JustCompletedLocationState is cleared as result processing progresses locally.
            // Saved quest scenario MatchSessionIDs survive that step and change on a new run.
            // Sort ordinally: native collection traversal order is not network identity.
            var runs = new List<string>();
            foreach (var quest in map.AllQuestStates)
            {
                string? match = quest?.ScenarioState?.MatchSessionID;
                if (!string.IsNullOrEmpty(match)) runs.Add(quest!.ID + "\0" + match);
            }
            runs.Sort(StringComparer.Ordinal);
            uint hash = Add(2166136261u, map.Seed.ToString(CultureInfo.InvariantCulture));
            foreach (string run in runs) hash = Add(hash, run);
            return hash == 0 ? 1u : hash;
        }
    }

    internal static uint Add(uint hash, string? value)
    {
        unchecked
        {
            if (value != null)
                for (int i = 0; i < value.Length; i++) hash = (hash ^ value[i]) * 16777619u;
            return (hash ^ 0xffffu) * 16777619u;
        }
    }
}
