#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Bounded startup evidence for native overlays versus the real VR camera.</summary>
    public sealed class QuestGamePresentationEvidence
    {
        readonly HashSet<string> sampledScenes = new HashSet<string>(StringComparer.Ordinal);
        public void Observe(string scene)
        {
            if (!GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging) return;
            if (string.IsNullOrEmpty(scene) || scene == "QuestOriginalStartup" || sampledScenes.Count >= 4 || !sampledScenes.Add(scene)) return;
            int count = 0;
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || !camera.isActiveAndEnabled || !camera.gameObject.scene.IsValid() || !camera.gameObject.scene.isLoaded) continue;
                var target = camera.targetTexture;
                Debug.Log("[Quest startup] presentation camera scene=" + scene + " name=" + camera.name
                    + " stereo=" + camera.stereoTargetEye + " mask=" + camera.cullingMask + " depth=" + camera.depth
                    + " target=" + (target != null ? target.name + " " + target.width + "x" + target.height : "backbuffer"));
                if (++count >= 6) break;
            }
            count = 0;
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || !canvas.isActiveAndEnabled || !canvas.gameObject.activeInHierarchy
                    || !canvas.gameObject.scene.IsValid() || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                Debug.Log("[Quest startup] presentation overlay scene=" + scene + " name=" + canvas.name
                    + " layer=" + canvas.gameObject.layer + " order=" + canvas.sortingOrder);
                if (++count >= 6) break;
            }
            count = 0;
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || !renderer.name.StartsWith("GloomhavenVR.LoadingIndicator.", StringComparison.Ordinal)) continue;
                var material = renderer.sharedMaterial;
                Debug.Log("[Quest startup] presentation loading surface scene=" + scene + " name=" + renderer.name
                    + " layer=" + renderer.gameObject.layer + " shader=" + (material != null && material.shader != null ? material.shader.name : "missing")
                    + " material=" + (material != null ? material.name : "missing"));
                if (++count >= 2) break;
            }
        }
    }
}
#endif
