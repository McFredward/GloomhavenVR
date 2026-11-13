using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>Own only the native decal's vertex upload, never its projection or eligibility.</summary>
internal sealed class QuestHexMesh : MonoBehaviour
{
    private static readonly HashSet<QuestHexMesh> Live = new();
    private MeshFilter? _filter;
    private Mesh? _original, _owned;

    internal static void Bind(Renderer? renderer)
    {
        if (!QuestStandalonePlatform.Enabled || renderer == null) return;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        QuestHexMesh? binding = renderer.GetComponent<QuestHexMesh>();
        if (binding != null && ReferenceEquals(filter.sharedMesh, binding._owned)) return;

        // B691 admits the native Cube into both head passes, with positive mask,
        // fill and hover/reach state, but neither highlight is visible. It still
        // uses Unity's original interleaved Cube upload, unlike the repaired menu,
        // pointer and piles. Re-upload the same bounded geometry/UVs/normals and
        // winding with an explicit white COLOR. The recovered two-pass shader,
        // native transforms, slab/occlusion/depth and gameplay remain untouched.
        // A Linux GPU witness qualifies the binding/parity, not an Adreno cure.
        Mesh source = filter.sharedMesh;
        Mesh? owned = QuestLitPrimitiveMesh.Rebuild(source, "native-hex-decal");
        if (owned == null) return;
        if (binding == null) binding = renderer.gameObject.AddComponent<QuestHexMesh>();
        else binding.Release();
        binding._filter = filter;
        binding._original = source;
        binding._owned = owned;
        filter.sharedMesh = owned;
        Live.Add(binding);
    }

    internal static void ResetAll()
    {
        // Uninstall/hot reload restores the original before its shader is restored.
        // Pooled/inactive objects remain in Live; no scene scan or frame watcher.
        var bindings = new List<QuestHexMesh>(Live);
        foreach (QuestHexMesh binding in bindings)
        {
            // Unity omits OnDestroy for a component that was never active. Its
            // managed record still owns a mesh even after the native component
            // compares null, so release the fields in that case as well.
            binding.Release();
            if (binding != null) Object.Destroy(binding);
        }
        Live.Clear();
    }

    private void Release()
    {
        // A later native/external mesh assignment owns itself. Never overwrite it.
        if (_filter != null && _owned != null && ReferenceEquals(_filter.sharedMesh, _owned))
            _filter.sharedMesh = _original;
        if (_owned != null) Object.Destroy(_owned);
        _owned = _original = null;
        _filter = null;
        Live.Remove(this);
    }

    private void OnDestroy() => Release();
}
