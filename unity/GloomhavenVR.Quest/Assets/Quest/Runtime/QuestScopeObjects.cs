#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Quest
{
    /// <summary>Native, exact-type discovery for the explicitly excluded original UI owners.</summary>
    internal sealed class QuestScopeObjects
    {
        static readonly string[] NativeNames =
        {
#if !GHVR_QUEST_GAME
            // The legacy diagnostic target excludes these two features. The
            // full game retains both and compiles their Scan branches out;
            // querying their owners there did no work but still searched all
            // loaded native objects twice each second (B688 hardware).
            "GLOOM.MainMenu.UIMainOptionsMenu", "VoiceChat.VoceChatOptions",
#endif
            "UIBuyDLCSlot", "UIPromotionDLCSlot", "GLOOM.MainMenu.UILoadGameSlot",
            "UIDLCSelectorOption", "GLOOM.MainMenu.UIMainMenuSuboption"
        };
        readonly Type[] targets;
        readonly double[] deadlines;
        readonly int[] matchedCounts, nativeCounts;
        readonly Component anchor;
        readonly List<Scene> scenes = new List<Scene>();
        readonly List<GameObject> roots = new List<GameObject>();
        bool scheduled;
        double lastQueryTime;
        internal int LastNativeObjectCount { get; private set; }
        internal int MatchedOwnerCount { get; private set; }
        internal int LastQueryTypeCount { get; private set; }
        internal int LastQueryFamilyIndex { get; private set; }

        internal QuestScopeObjects(Component anchor) : this(ResolveTypes(), anchor) { }

        // The real Unity fixture supplies its own native MonoBehaviour types;
        // the standalone path always resolves the exact owned GH.Runtime ABI.
        internal QuestScopeObjects(Type[] targets, Component anchor)
        {
            this.anchor = anchor;
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
            // require a bounded catch-up of at most these known families.
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
            CollectScenes();
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
            roots.Clear(); scenes.Clear();
            return LastQueryTypeCount > 0;
        }

        internal void Collect(List<MonoBehaviour> result)
        {
            result.Clear();
            LastQueryTypeCount = 0;
            CollectScenes();
            for (int index = 0; index < targets.Length; index++) CollectType(index, result);
            roots.Clear(); scenes.Clear();
        }

        void CollectScenes()
        {
            scenes.Clear();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded) scenes.Add(scene);
            }
            // DDOL is omitted by SceneManager's ordinary list. This adapter's
            // persistent host supplies that exact scene, including inactive and
            // DontSave roots; never infer it from one already-matched UI owner.
            if (anchor == null) return;
            Scene persistent = anchor.gameObject.scene;
            foreach (Scene scene in scenes) if (scene.handle == persistent.handle) return;
            if (persistent.IsValid() && persistent.isLoaded) scenes.Add(persistent);
        }

        void CollectType(int index, List<MonoBehaviour> result)
        {
            // B689 still spends55ms (worst289ms) on one native Resources query:
            // exact type limits the returned array, not the global asset search.
            // Query that same type inside actual loaded scene hierarchies instead.
            // This keeps periodic late AddComponent discovery on inactive objects
            // without traversing unrelated managed components or imported assets.
            Type target = targets[index];
            int matched = 0, foundCount = 0;
            foreach (Scene scene in scenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                roots.Clear();
                if (roots.Capacity <= scene.rootCount) roots.Capacity = scene.rootCount + 1;
                scene.GetRootGameObjects(roots);
                foreach (GameObject root in roots)
                {
                    if (root == null) continue;
                    Component[] found = root.GetComponentsInChildren(target, true);
                    foundCount += found.Length;
                    foreach (Component value in found)
                    {
                        var component = value as MonoBehaviour;
                        // Keep B621's exact identity and loaded-scene boundary,
                        // including inactive, DDOL and pooled owners.
                        if (component == null || component.GetType() != target
                            || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded) continue;
                        result.Add(component); matched++;
                    }
                }
            }
            LastNativeObjectCount += foundCount - nativeCounts[index];
            nativeCounts[index] = foundCount;
            MatchedOwnerCount += matched - matchedCounts[index];
            matchedCounts[index] = matched;
            LastQueryTypeCount++;
            LastQueryFamilyIndex = index;
        }
    }
}
#endif
