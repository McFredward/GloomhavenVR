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
            // One Debug observation per native scene, never a recurring sweep.
            // A recovered blur placeholder is not proof that hidden UI draws.
            var graphics = new List<UnityEngine.UI.Graphic>();
            var scratch = new List<UnityEngine.UI.Graphic>();
            new QuestSceneObjects(GloomhavenVR.Core.QuestStandalonePlatform.HeadCamera).Collect(graphics, scratch);
            count = 0;
            foreach (var graphic in graphics)
            {
                if (graphic == null) continue;
                Material authored = graphic.material;
                if (authored == null || authored.shader == null || authored.shader.name != "Custom/SimpleGrabPassBlur") continue;
                CanvasRenderer ui = graphic.canvasRenderer;
                Material rendered = ui.materialCount > 0 ? ui.GetMaterial(0) : null;
                Debug.Log("[Quest startup] presentation blur scene=" + scene + " object=" + graphic.name
                    + " parent=" + (graphic.transform.parent != null ? graphic.transform.parent.name : "none")
                    + " active=" + graphic.gameObject.activeInHierarchy + " enabled=" + graphic.enabled
                    + " culled=" + ui.cull + " cullTransparentMesh=" + ui.cullTransparentMesh
                    + " vertexAlpha=" + graphic.color.a + " rendererAlpha=" + ui.GetAlpha()
                    + " inheritedAlpha=" + ui.GetInheritedAlpha() + " material=" + authored.name
                    + " renderedShader=" + (rendered != null && rendered.shader != null ? rendered.shader.name : "none"));
                if (++count >= 4) break;
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
