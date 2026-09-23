using System.Collections.Generic;
using UnityEngine;
// Shader/materials, cloning and production capture code use real Unity. The established
// CardMesh consumer registry is an explicit boundary: its later silhouette event is simulated.
namespace GloomhavenVR.Cards
{
    internal enum CardBodyKind { Item }
    internal static class CardMesh
    {
        internal static readonly HashSet<MeshFilter> Registered = new();
        private static Material? _edge, _back;
        internal static void AttachBody(MeshFilter filter, CardBodyKind kind, float width, float height)
        { Registered.Add(filter); filter.sharedMesh = new Mesh(); }
        private static Material Material()
        {
            var result = new Material(Shader.Find("Standard")); result.color = Color.white;
            result.SetFloat("_Mode", 1); result.SetFloat("_ZWrite", 1); result.EnableKeyword("_ALPHATEST_ON"); result.renderQueue = 2450; return result;
        }
        internal static Material CreateEdgeMaterial(CardBodyKind kind) => _edge ??= Material();
        internal static Material CreateBackMaterial(CardBodyKind kind) => _back ??= Material();
        internal static void Clean()
        {
            Registered.Clear(); if (_edge != null) Object.DestroyImmediate(_edge); if (_back != null) Object.DestroyImmediate(_back);
            _edge = _back = null;
        }
    }
}
