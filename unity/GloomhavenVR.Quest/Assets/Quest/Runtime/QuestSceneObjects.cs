#nullable disable
#if GHVR_QUEST_STARTUP
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Quest
{
    /// <summary>
    /// Reused, loaded-scene-only discovery for standalone adapters. B620 walked
    /// every imported MonoBehaviour prefab with Resources.FindObjectsOfTypeAll
    /// each second, then discarded asset objects. A recovered owned game has a
    /// much larger prefab population than its live menu. Walk the actual roots
    /// instead, including inactive and newly spawned native widgets. The normal
    /// discovery cadence and the original components/callbacks stay unchanged.
    /// </summary>
    public sealed class QuestSceneObjects
    {
        readonly Component anchor;
        readonly List<GameObject> roots = new List<GameObject>();
        readonly List<Scene> scenes = new List<Scene>();

        public QuestSceneObjects(Component anchor) { this.anchor = anchor; }

        public void Collect<T>(List<T> result, List<T> scratch) where T : Component
        {
            result.Clear();
            scenes.Clear();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded) scenes.Add(scene);
            }
            // DontDestroyOnLoad is a real loaded scene, but SceneManager's public
            // scene list excludes it. The adapter's persistent owner supplies its
            // handle; never recover that population through an all-assets scan.
            if (anchor != null)
            {
                Scene persistent = anchor.gameObject.scene;
                bool listed = false;
                for (int index = 0; index < scenes.Count; index++)
                    if (scenes[index].handle == persistent.handle) { listed = true; break; }
                if (!listed && persistent.IsValid() && persistent.isLoaded) scenes.Add(persistent);
            }
            for (int index = 0; index < scenes.Count; index++)
            {
                Scene scene = scenes[index];
                roots.Clear();
                // Unity otherwise builds a temporary root array when capacity is
                // smaller than rootCount. Grow only on a genuine new population.
                if (roots.Capacity <= scene.rootCount) roots.Capacity = scene.rootCount + 1;
                scene.GetRootGameObjects(roots);
                for (int root = 0; root < roots.Count; root++)
                {
                    GameObject item = roots[root];
                    if (item == null || !item.scene.IsValid() || !item.scene.isLoaded) continue;
                    scratch.Clear();
                    item.GetComponentsInChildren<T>(true, scratch);
                    result.AddRange(scratch);
                }
            }
            scratch.Clear();
            roots.Clear();
            scenes.Clear();
        }
    }
}
#endif
