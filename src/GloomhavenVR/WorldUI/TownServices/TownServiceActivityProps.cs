using System;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Presentation-only work tools stay in their real grips throughout interruptions.</summary>
internal sealed class TownServiceActivityProps : IDisposable
{
    internal const float PenTipDistance = .06f;
    private readonly Transform _root;
    private readonly byte _service;
    private readonly Shader? _shader;
    private Transform? _leftGrip, _rightGrip, _pen, _coin;
    private Vector3 _coinOffset, _coinRest;
    private Mesh? _penMesh;
    private Material? _penMaterial;
    private float _visibility;
    private bool _gripsBound, _suspended;
    private static readonly int Visibility = Shader.PropertyToID("_TownVisibility");
    internal TownServiceActivityProps(Transform root, byte service, Shader? shader)
    { _root = root; _service = service; _shader = shader; }
    internal void BindCoin(Transform coin, Vector3 offset)
    { _coin = coin; _coinOffset = offset; _coinRest = coin.localPosition; }
    internal void Sample(in TownActivityPose pose) => Sample();
    internal void Sample()
    {
        if (_service != 1) return;
        _suspended = false;
        if (!_gripsBound)
        {
            _gripsBound = true; // Activity rig creates these synchronously; absent bones never trigger a per-frame hierarchy scan.
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "ActivityGripLeft") _leftGrip = child;
                else if (child.name == "ActivityGripRight") _rightGrip = child;
            }
        }
        if (_leftGrip != null && _coin != null)
        {
            Transform coin = _coin;
            // Keep the original coin mesh, materials and scale. Its grip remains visible
            // through attention transitions rather than hiding or respawning a prop.
            coin.localPosition = _coinOffset + _root.InverseTransformPoint(_leftGrip.position);
        }
        if (_rightGrip == null) return;
        if (_pen == null) CreatePen();
        if (_pen == null) return;
        if (!_pen.gameObject.activeSelf) _pen.gameObject.SetActive(true);
        _pen.position = _rightGrip.position;
        _pen.rotation = _rightGrip.rotation;
    }

    private void CreatePen()
    {
        Shader? shader = _shader;
        if (shader == null) return;
        // A small carved reed writing tool. The game provides the open ledger and coin;
        // its PCG prop catalog contains no standalone writing implement. Do not pass a
        // decorative knife or a complete feathered pole off as a pen.
        const int sides = 8;
        var vertices = new Vector3[sides * 2 + 2];
        var triangles = new int[sides * 12];
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            vertices[i] = new Vector3(Mathf.Cos(a) * .0022f, -.012f, Mathf.Sin(a) * .0022f);
            vertices[i + sides] = new Vector3(Mathf.Cos(a) * .0028f, .115f, Mathf.Sin(a) * .0028f);
            int j = (i + 1) % sides, t = i * 12;
            triangles[t] = i; triangles[t + 1] = j; triangles[t + 2] = i + sides;
            triangles[t + 3] = j; triangles[t + 4] = j + sides; triangles[t + 5] = i + sides;
            triangles[t + 6] = sides * 2; triangles[t + 7] = j; triangles[t + 8] = i;
            triangles[t + 9] = sides * 2 + 1; triangles[t + 10] = i + sides; triangles[t + 11] = j + sides;
        }
        vertices[sides * 2] = new Vector3(0f, -PenTipDistance, 0f);
        vertices[sides * 2 + 1] = new Vector3(0f, .115f, 0f);
        for (int i = 0; i < triangles.Length; i += 3)
        { int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap; }
        _penMesh = new Mesh { name = "Town.ReedPen", vertices = vertices, triangles = triangles };
        _penMesh.RecalculateNormals(); _penMesh.RecalculateBounds();
        var material = new Material(shader) { name = "Town.ReedPen", color = new Color(.42f, .27f, .11f) };
        material.SetFloat(Visibility, _visibility); _penMaterial = material;
        var obj = new GameObject("Town.ReedPen") { layer = VRLayers.ModLayer };
        _pen = obj.transform; _pen.SetParent(_root, false);
        obj.AddComponent<MeshFilter>().sharedMesh = _penMesh;
        MeshRenderer renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    internal void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        if (_pen != null) _pen.gameObject.SetActive(false);
        if (_coin != null) _coin.localPosition = _coinRest;
    }

    internal void SetVisibility(float value)
    {
        _visibility = value;
        if (_penMaterial != null) _penMaterial.SetFloat(Visibility, value);
    }
    public void Dispose()
    {
        if (_pen != null) UnityEngine.Object.Destroy(_pen.gameObject);
        if (_penMesh != null) UnityEngine.Object.Destroy(_penMesh);
        if (_penMaterial != null) UnityEngine.Object.Destroy(_penMaterial);
    }
}
