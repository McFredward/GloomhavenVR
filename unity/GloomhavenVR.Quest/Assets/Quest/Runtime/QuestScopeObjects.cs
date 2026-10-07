#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Native, exact-type discovery for the explicitly excluded original UI owners.</summary>
    internal sealed class QuestScopeObjects
    {
        static readonly string[] NativeNames =
        {
            "GLOOM.MainMenu.UIMainOptionsMenu", "VoiceChat.VoceChatOptions",
            "UIBuyDLCSlot", "UIPromotionDLCSlot", "GLOOM.MainMenu.UILoadGameSlot",
            "UIDLCSelectorOption", "GLOOM.MainMenu.UIMainMenuSuboption"
        };
        readonly Type[] targets;
        readonly double[] deadlines;
        readonly int[] matchedCounts, nativeCounts;
        bool scheduled;
        double lastQueryTime;
        internal int LastNativeObjectCount { get; private set; }
        internal int MatchedOwnerCount { get; private set; }
        internal int LastQueryTypeCount { get; private set; }
        internal int LastQueryFamilyIndex { get; private set; }

        internal QuestScopeObjects() : this(ResolveTypes()) { }

        // The real Unity fixture supplies its own native MonoBehaviour types;
        // the standalone path always resolves the exact owned GH.Runtime ABI.
        internal QuestScopeObjects(Type[] targets)
        {
            this.targets = (Type[])targets.Clone();
            deadlines = new double[targets.Length];
            matchedCounts = new int[targets.Length];
            nativeCounts = new int[targets.Length];
            foreach (Type target in this.targets)
                if (target == null || !typeof(MonoBehaviour).IsAssignableFrom(target))
                    throw new InvalidOperationException("Original scope owner MonoBehaviour ABI is missing.");
        }

        static Type[] ResolveTypes()
        {
            var result = new Type[NativeNames.Length];
            for (int index = 0; index < result.Length; index++)
                result[index] = Type.GetType(NativeNames[index] + ", GH.Runtime", true);
            return result;
        }

        internal bool NeedsScan(double now)
        {
            if (!scheduled || now < lastQueryTime) return true;
            foreach (double deadline in deadlines) if (deadline <= now) return true;
            return false;
        }

        internal bool CollectDue(double now, List<MonoBehaviour> result)
        {
            // Bind already-present UI immediately once, preserving B621 startup.
            // Ordinary ticks query one named family; a stalled/backward clock may
            // require a bounded catch-up of at most these seven known families.
            if (!scheduled)
            {
                Collect(result);
                for (int index = 0; index < deadlines.Length; index++)
                    deadlines[index] = now + (index + 1d) / deadlines.Length;
                scheduled = true; lastQueryTime = now;
                return true;
            }
            if (now < lastQueryTime)
            {
                for (int index = 0; index < deadlines.Length; index++)
                    deadlines[index] = now + (double)index / deadlines.Length;
                lastQueryTime = now;
            }
            result.Clear();
            LastQueryTypeCount = 0;
            for (int pending = 0; pending < targets.Length; pending++)
            {
                int due = -1;
                for (int index = 0; index < deadlines.Length; index++)
                    if (deadlines[index] <= now && (due < 0 || deadlines[index] < deadlines[due])) due = index;
                if (due < 0) break;
                CollectType(due, result);
                // Keep each absolute one-second family deadline. A long stall
                // reads its current owners once, rather than replaying stale cycles.
                deadlines[due] += 1d;
                if (deadlines[due] <= now)
                    deadlines[due] += Math.Floor(now - deadlines[due]) + 1d;
            }
            if (LastQueryTypeCount > 0) lastQueryTime = now;
            return LastQueryTypeCount > 0;
        }

        internal void Collect(List<MonoBehaviour> result)
        {
            result.Clear();
            LastQueryTypeCount = 0;
            for (int index = 0; index < targets.Length; index++) CollectType(index, result);
        }

        void CollectType(int index, List<MonoBehaviour> result)
        {
            // B621 hardware prices its 14,338 managed component checks at 94ms.
            // Native exact-type queries transfer only rare matching candidates;
            // their whole-cycle desktop cost is retained in the test receipt.
            // Scheduling bounds ordinary frame cost without lowering any known
            // family's discovery cadence. Resources retains live DontSave owners
            // that Object.FindObjectsOfType(Type, true) would silently omit.
            Type target = targets[index];
            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(target);
            LastNativeObjectCount += found.Length - nativeCounts[index];
            nativeCounts[index] = found.Length;
            int matched = 0;
            foreach (UnityEngine.Object value in found)
            {
                var component = value as MonoBehaviour;
                // Keep B621's exact identity and loaded-scene boundary, including
                // inactive, DDOL, late component additions and pooled owners.
                if (component == null || component.GetType() != target
                    || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded) continue;
                result.Add(component); matched++;
            }
            MatchedOwnerCount += matched - matchedCounts[index];
            matchedCounts[index] = matched;
            LastQueryTypeCount++;
            LastQueryFamilyIndex = index;
        }
    }
}
#endif
