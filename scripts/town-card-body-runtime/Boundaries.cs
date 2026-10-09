using System.Collections.Generic;
using System.Linq;
using UnityEngine;
// Production CardMesh/CardContour, materials and the ability helper compile in full.
// These read-only registry/cleanup ports retain the historical merchant assertions.
// Its final direct mesh assignment still models an external contour completion;
// the separate NPC660 ability proof executes the actual SetSilhouette engine.
namespace GloomhavenVR.Cards
{
    internal static partial class CardMesh
    {
        internal static HashSet<MeshFilter> Registered => new(_bodies.Select(body => body.Filter).Where(filter => filter != null));
        internal static void Clean()
        {
            _bodies.Clear();
            for (int i = 0; i < _edgeMaterials.Length; i++)
            {
                if (_edgeMaterials[i] != null) Object.DestroyImmediate(_edgeMaterials[i]);
                if (_backMaterials[i] != null) Object.DestroyImmediate(_backMaterials[i]);
                _edgeMaterials[i] = _backMaterials[i] = null;
            }
        }
    }
}
