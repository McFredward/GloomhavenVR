using System.Globalization;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>One passive head-camera submission observation per guide activation.</summary>
internal sealed class QuestCardGuideWitness : MonoBehaviour
{
    private Transform? _visual;
    internal bool Submitted { get; private set; }
    internal string Facts { get; private set; } = "pending";
    internal static void Attach(GameObject guide, Transform? visual)
    {
        if (!QuestStandalonePlatform.Enabled) return;
        guide.AddComponent<QuestCardGuideWitness>()._visual = visual;
    }
    private void OnEnable() { Submitted = false; Facts = "pending"; }
    private void OnWillRenderObject()
    {
        if (Submitted || !QuestStandalonePlatform.Enabled || !QuestStandalonePlatform.DebugLogging) return;
        Camera? camera = Camera.current;
        if (camera == null || camera != WorldUI.CanvasConversion.WorldCamera) return;
        // The earlier Update snapshot is before culling and legitimately reports
        // isVisible=false for BOTH guides. This callback proves submission, not
        // final eye visibility or a successful depth/blend test. No image reads.
        Submitted = true;
        try { ObserveCamera(camera); }
        catch (System.Exception error) { Facts = "unavailable=" + error.GetType().Name; }
    }
    private void ObserveCamera(Camera camera)
    {
        Renderer renderer = GetComponent<Renderer>();
        Bounds bounds = renderer.bounds;
        Vector3 center = camera.WorldToViewportPoint(bounds.center);
        Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 point = camera.WorldToViewportPoint(corner);
            low = Vector3.Min(low, point); high = Vector3.Max(high, point);
        }
        string f(float number) => number.ToString("F2", CultureInfo.InvariantCulture);
        string name = camera.name.Length <= 24 ? camera.name : camera.name.Substring(0, 24);
        MeshFilter? board = _visual != null ? _visual.GetComponentInChildren<MeshFilter>() : null;
        string mesh = board != null && board.sharedMesh != null ? board.sharedMesh.name : "none";
        if (mesh.Length > 20) mesh = mesh.Substring(0, 20);
        Material? material = board != null ? board.GetComponent<Renderer>()?.sharedMaterial : null;
        string shader = material != null && material.shader != null ? material.shader.name : "none";
        if (shader.Length > 24) shader = shader.Substring(0, 24);
        // Submission alone did not explain B690's missing left guide. Observe
        // its final per-renderer bindings, not just the material shared by the
        // pulse. An MPB can override that material without changing its values.
        Renderer? cue = GetComponent<Renderer>();
        Material? cueMaterial = cue != null ? cue.sharedMaterial : null;
        string tint(Color color) => f(color.r) + "/" + f(color.g) + "/" + f(color.b) + "/" + f(color.a);
        Texture? texture = cueMaterial != null ? cueMaterial.mainTexture : null;
        string textureName = texture != null ? texture.name : "default";
        if (textureName.Length > 24) textureName = textureName.Substring(0, 24);
        string propertyTint = "none";
        if (cue != null && cue.HasPropertyBlock())
        {
            var block = new MaterialPropertyBlock(); cue.GetPropertyBlock(block);
            int colorId = Shader.PropertyToID("_Color");
            propertyTint = block.HasColor(colorId) ? tint(block.GetColor(colorId)) : "other";
        }
        MeshCollider? collider = _visual != null ? _visual.GetComponentInChildren<MeshCollider>() : null;
        int blocked = 0;
        bool colliderReady = collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        if (colliderReady)
            for (int i = 0; i < 5; i++)
            {
                Vector3 sample = i == 0 ? Vector3.zero : new Vector3((i & 1) == 0 ? -.45f : .45f, (i & 2) == 0 ? -.45f : .45f, 0);
                Vector3 delta = transform.TransformPoint(sample) - camera.transform.position;
                float distance = delta.magnitude;
                if (distance > .0001f && collider!.Raycast(new Ray(camera.transform.position, delta / distance), out _, distance - .0001f)) blocked++;
            }
        Facts = "f" + Time.frameCount + ":" + name + ":" + (camera.stereoEnabled ? "xr" : "mono")
            + ",vp=" + f(center.x) + "/" + f(center.y) + "/" + f(center.z)
            + ",xy=" + f(low.x) + "/" + f(low.y) + "/" + f(high.x) + "/" + f(high.y)
            + ",near=" + f(camera.nearClipPlane)
            + ",board=" + CardsConfig.CurrentBoard + "/" + mesh + "/" + shader
            + ",colliderHits=" + (colliderReady ? blocked + "/5" : collider != null ? "inactive" : "none")
            + ",tint=" + (cueMaterial != null ? tint(cueMaterial.color) : "none")
            + ",blockTint=" + propertyTint + ",texture=" + textureName
            + ",supported=" + (cueMaterial != null && cueMaterial.shader != null && cueMaterial.shader.isSupported);
    }
}
