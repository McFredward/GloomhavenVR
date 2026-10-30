using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    // Explicit world-menu backing input: exact shipped Overlay shader source,
    // accepted6cm offset, native RGBA/opaque blend and fixed opposing windings.
    // Root's separate world-menu fixture executes its actual ownership/mesh API.
    private sealed class Backing742 : IDisposable
    {
        private readonly Transform _window;
        private readonly GameObject _front;
        private readonly GameObject? _rear;
        private readonly Mesh? _rearMesh;
        private readonly Material _material;

        internal Backing742(Transform window, bool originalSingleFace)
        {
            _window = window;
            _material = new Material(Shader.Find("GloomhavenVR/Overlay"));
            _material.SetFloat("_ZTest", 4); _material.SetFloat("_ZWrite", 1);
            _material.SetFloat("_Cull", originalSingleFace ? 0 : 2);
            _material.SetFloat("_SrcBlend", 1); _material.SetFloat("_DstBlend", 0);
            _material.SetFloat("_VertexColor", 0); _material.SetFloat("_UseAlphaMask", 0);
            _material.SetColor("_Color", new Color(.025f, .025f, .025f, 1));
            _material.renderQueue = 1002;
            _front = Plane("fixed front background", .06f);
            if (!originalSingleFace)
            {
                _rear = Plane("fixed rear background with unchanged UV", -.06f);
                _rearMesh = Object.Instantiate(_rear.GetComponent<MeshFilter>().sharedMesh);
                int[] triangles = _rearMesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                { int first = triangles[i]; triangles[i] = triangles[i + 1]; triangles[i + 1] = first; }
                _rearMesh.triangles = triangles;
                Vector3[] normals = _rearMesh.normals;
                for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
                _rearMesh.normals = normals;
                _rear.GetComponent<MeshFilter>().sharedMesh = _rearMesh;
            }
        }

        private GameObject Plane(string name, float offset)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = name; plane.layer = 27;
            plane.transform.SetParent(_window, false);
            plane.transform.localPosition = new Vector3(0, 0, offset);
            plane.transform.localScale = new Vector3(1.03f, 1.03f, 1f);
            plane.GetComponent<Renderer>().sharedMaterial = _material;
            return plane;
        }

        internal void RecordDepth(Bounds native, Matrix4x4 nativeToWorld, string output)
        {
            float front = float.MaxValue, back = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var point = new Vector3((i & 1) == 0 ? native.min.x : native.max.x,
                    (i & 2) == 0 ? native.min.y : native.max.y, (i & 4) == 0 ? native.min.z : native.max.z);
                float depth = Vector3.Dot(nativeToWorld.MultiplyPoint3x4(point) - _window.position, _window.forward);
                front = Mathf.Min(front, depth); back = Mathf.Max(back, depth);
            }
            File.WriteAllText(output, "front=" + front + ";back=" + back + ";backing gaps=+/-0.06 metres\n");
        }

        public void Dispose()
        {
            Object.DestroyImmediate(_front);
            if (_rear != null) Object.DestroyImmediate(_rear);
            if (_rearMesh != null) Object.DestroyImmediate(_rearMesh);
            Object.DestroyImmediate(_material);
        }
    }
}
