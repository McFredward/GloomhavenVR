using System;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
public static class InteractionProgram
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        var parent = new GameObject("counter");
        GameObject? owner = null;
        try
        {
            owner = TownServiceCardBody.Create(parent.transform);
            var renderer = owner.GetComponent<MeshRenderer>();
            var source = CardMesh.CreateEdgeMaterial(CardBodyKind.Item);
            var material = renderer.sharedMaterials[0];
            Check(material != source, "body owns its fade material");
            TownServiceCardBody.SetVisibility(owner, 0f);
            Check(!renderer.enabled && material.color.a == 0f, "body disappears with zero-opacity original face");
            TownServiceCardBody.SetVisibility(owner, .4f);
            Check(renderer.enabled && Mathf.Abs(material.color.a - .4f) < .001f
                && material.GetFloat("_SrcBlend") == 5 && material.GetFloat("_DstBlend") == 10
                && material.GetFloat("_ZWrite") == 0 && material.IsKeywordEnabled("_ALPHABLEND_ON"),
                "body has genuine intermediate shader alpha blending");
            Check(source.color.a == 1f && source.GetFloat("_ZWrite") == 1,
                "physical-card fade never changes shared item fan materials");
            Check(!renderer.HasPropertyBlock(), "body fade uses wire-capturable owned material values");
            TownServiceCardBody.SetVisibility(owner, 1f);
            Check(material.color.a == source.color.a && material.renderQueue == source.renderQueue
                && material.GetFloat("_ZWrite") == source.GetFloat("_ZWrite")
                && material.IsKeywordEnabled("_ALPHATEST_ON") == source.IsKeywordEnabled("_ALPHATEST_ON"),
                "completed appearance restores original cutout and depth ordering");
            var silhouette = new Texture2D(2, 2); source.mainTexture = silhouette;
            TownServiceCardBody.SetVisibility(owner, 1f);
            Check(material.mainTexture == silhouette, "late original silhouette texture reaches an already-open body");
            Transform frozen = NativeTemplates.TestFreeze(owner.transform);
            Check(CardMesh.Registered.Contains(frozen.GetComponent<MeshFilter>()), "native frozen body joins silhouette updates");
            TownServiceMirror.PrepareInertGeometry = TownServiceCardBody.RebindClone;
            TownServiceMirror.RegisterTemplate(1, 1, frozen, address: "merchant.cardbody|");
            Check(CardMesh.Registered.Count == 3, "transport template joins silhouette updates");
            var frame = new TownServiceFrame { Service = 1, Template = 1, TemplateAddress = "merchant.cardbody|", Session = 1, Nodes = new[] { new TownServiceNode() } };
            object remote = typeof(TownServiceMirror).GetMethod("BuildRemote", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { frame, parent.transform })!;
            Check(CardMesh.Registered.Count == 4, "every inert observer body joins silhouette updates");
            var next = new Mesh();
            foreach (var filter in CardMesh.Registered) filter.sharedMesh = next;
            foreach (var filter in CardMesh.Registered) Check(filter.sharedMesh == next, "all original and cloned bodies receive the same later contour");
            ((IDisposable)remote).Dispose();
            TownServiceCardBody.Dispose(owner);
            Check(renderer.sharedMaterials[0] == material && source != null, "owned cleanup never destroys original item material");
            UnityEngine.Object.Destroy(silhouette);
            return assertions;
        }
        finally
        {
            if (owner != null) TownServiceCardBody.Dispose(owner);
            TownServiceMirror.Shutdown(); TownServiceMirror.PrepareInertGeometry = null;
            NativeTemplates.Clean(); CardMesh.Clean(); UnityEngine.Object.DestroyImmediate(parent);
        }
    }
}
