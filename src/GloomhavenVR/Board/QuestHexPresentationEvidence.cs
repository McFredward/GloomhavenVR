using System;
using System.Collections.Generic;
using System.Globalization;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>Bounded actual native selection inputs and head submissions. Never
/// changes native reachability, material state, geometry or renderer visibility.</summary>
internal static class QuestHexPresentationEvidence
{
    private static readonly Dictionary<int, string> States = new();
    private static readonly Dictionary<int, Renderer?> Sources = new();
    private static readonly List<int> Order = new(8);

    internal static string[] Snapshot
    {
        get
        {
            var result = new string[Order.Count];
            for (int i = 0; i < result.Length; i++)
            {
                Renderer? renderer = Sources[Order[i]];
                result[i] = States[Order[i]] + " now=" + (renderer != null
                    ? "active:" + renderer.gameObject.activeInHierarchy + ",enabled:" + renderer.enabled + ",forceOff:" + renderer.forceRenderingOff
                    : "retired");
            }
            return result;
        }
    }

    internal static void Observe(HexSelect_Control owner, Material? material)
    {
        if (!QuestStandalonePlatform.Enabled || !QuestStandalonePlatform.DebugLogging || owner == null) return;
        try
        {
            Renderer? renderer = owner.HexProjector;
            if (renderer != null)
            {
                var witness = renderer.GetComponent<QuestHexDrawWitness>();
                if (witness == null) witness = renderer.gameObject.AddComponent<QuestHexDrawWitness>();
                if (!witness.Bind(owner)) return;
            }
            Record(owner, material, null);
        }
        catch (Exception) { /* Debug evidence cannot interrupt the original selection callback. */ }
    }

    internal static void Record(HexSelect_Control owner, Material? material, Camera? camera)
    {
        if (!QuestStandalonePlatform.Enabled || !QuestStandalonePlatform.DebugLogging || owner == null) return;
        int key = (int)owner.Type * 16 + (int)owner.Mode;
        Renderer? renderer = owner.HexProjector;
        Shader? shader = material != null ? material.shader : null;
        Mesh? mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        string number(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
        string name(string value) => value.Length > 48 ? value.Substring(0, 48) : value;
        string scalar(string property) => material != null && material.HasProperty(property)
            ? number(material.GetFloat(property)) : "missing";
        Color tint = material != null && material.HasProperty("_HexColour") ? material.GetColor("_HexColour") : Color.clear;
        string texture(string property)
        {
            Texture? value = material != null && material.HasProperty(property) ? material.GetTexture(property) : null;
            return value != null ? name(value.name) + "/" + value.width + "x" + value.height : "none";
        }
        Camera? head = QuestStandalonePlatform.HeadCamera;
        Vector3 viewport = head != null && renderer != null ? head.WorldToViewportPoint(renderer.bounds.center) : Vector3.zero;
        string facts = "hex frame=" + Time.frameCount + " type=" + owner.Type + " mode=" + owner.Mode
            + " owner=" + owner.GetInstanceID() + " material=" + (material != null ? material.GetInstanceID() : 0) + " hover=" + owner.Hover
            + " active=" + (renderer != null && renderer.gameObject.activeInHierarchy) + " enabled=" + (renderer != null && renderer.enabled)
            + " forceOff=" + (renderer != null && renderer.forceRenderingOff)
            + " shader=" + (shader != null ? name(shader.name) : "none") + " supported=" + (shader != null && shader.isSupported)
            + " passes=" + (material != null ? material.passCount : 0) + " mesh=" + (mesh != null ? mesh.vertexCount : 0)
            + " upload=" + (mesh != null && mesh.name.StartsWith("GloomhavenVR.QuestLitPrimitive.") ? "owned" : "native")
            + " stride=" + (mesh != null ? mesh.GetVertexBufferStride(0) : 0)
            + " mask=" + texture("_HexMask") + " fill=" + scalar("_HexIntensity") + " border=" + scalar("_BorderLineIntensity")
            + " tint=" + number(tint.r) + "/" + number(tint.g) + "/" + number(tint.b) + "/" + number(tint.a)
            + " edges=" + scalar("_NW_On") + "/" + scalar("_NE_On") + "/" + scalar("_E_On")
                + "/" + scalar("_SE_On") + "/" + scalar("_SW_On") + "/" + scalar("_W_On")
            + " depth=" + scalar("_VRZTest") + " stencil=" + scalar("_VRStencilBit")
            + " layer=" + (renderer != null ? renderer.gameObject.layer : -1)
            + " admitted=" + (renderer != null && head != null && (head.cullingMask & (1 << renderer.gameObject.layer)) != 0)
            + " vp=" + number(viewport.x) + "/" + number(viewport.y) + "/" + number(viewport.z)
            + " draw=" + (camera != null ? "head:" + Time.frameCount : "pending");
        if (!States.ContainsKey(key)) Order.Add(key);
        States[key] = facts;
        Sources[key] = renderer;
        while (Order.Count > 8) { States.Remove(Order[0]); Sources.Remove(Order[0]); Order.RemoveAt(0); }
    }
}

internal sealed class QuestHexDrawWitness : MonoBehaviour
{
    private HexSelect_Control? _owner;
    private int _material, _state = -1, _edges;
    internal bool Bind(HexSelect_Control owner)
    {
        Material? material = owner.HexProjector != null ? owner.HexProjector.sharedMaterial : null;
        int id = material != null ? material.GetInstanceID() : 0;
        int state = (int)owner.Mode * 32 + (int)owner.Type * 2 + (owner.Hover ? 1 : 0);
        int edges = (owner.nw_On ? 1 : 0) | (owner.ne_On ? 2 : 0) | (owner.e_On ? 4 : 0)
            | (owner.se_On ? 8 : 0) | (owner.sw_On ? 16 : 0) | (owner.w_On ? 32 : 0);
        if (ReferenceEquals(_owner, owner) && _material == id && _state == state && _edges == edges) return false;
        _owner = owner; _material = id; _state = state; _edges = edges; enabled = true;
        return true;
    }
    private void OnWillRenderObject()
    {
        if (!enabled || !QuestStandalonePlatform.Enabled || !QuestStandalonePlatform.DebugLogging || _owner == null) return;
        Camera? camera = Camera.current;
        if (camera == null || !ReferenceEquals(camera, QuestStandalonePlatform.HeadCamera)) return;
        try
        {
            Material? material = _owner.HexProjector != null ? _owner.HexProjector.sharedMaterial : null;
            QuestHexPresentationEvidence.Record(_owner, material, camera);
            // One observed draw per native state edge, with no recurring
            // per-renderer callback after that admission has been witnessed.
            enabled = false;
        }
        catch (Exception) { /* Debug-only final binding facts never gate a native draw. */ }
    }
}
