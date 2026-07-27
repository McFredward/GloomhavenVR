using System;
using System.Reflection;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE UNDO PATH — the reason <see cref="StaticBatcher"/> is allowed to exist at all.
///
/// <para>THE PROBLEM. <see cref="StaticBatchingUtility"/> has a <c>Combine</c> and no
/// <c>Uncombine</c>. Restoring each object's original mesh is NOT enough on its own: combining
/// also writes two pieces of state INTO the renderer — which slice of the combined mesh it draws
/// (<c>SetStaticBatchInfo(firstSubMesh, subMeshCount)</c>) and which transform that mesh's vertices
/// were baked against (<c>staticBatchRootTransform</c>). Put the original mesh back without
/// clearing those, and the renderer draws submesh range [first, first+count) of a mesh that has one
/// submesh: at best nothing, at worst the wrong triangles. A half-undo is worse than no undo.</para>
///
/// <para>THE SOLUTION, AND WHY IT IS NOT A GUESS. Unity's static batching is implemented in C#, in
/// <c>UnityEngine.CoreModule</c> (<c>InternalStaticBatchingUtility.MakeBatch</c>), and it is
/// readable. Every write it performs is an INTERNAL member of a public type, which reflection
/// reaches:</para>
/// <list type="bullet">
/// <item><c>Renderer.SetStaticBatchInfo(int, int)</c> — internal instance method,</item>
/// <item><c>Renderer.staticBatchRootTransform</c> — internal property (native "StaticBatchRoot"),</item>
/// <item><c>MeshRenderer.enlightenVertexStream</c> — internal property, nulled by MakeBatch,</item>
/// <item><c>StaticBatchingHelper.IsMeshBatchable(Mesh)</c> — internal static, the exact test Unity
/// itself applies, so the PROBE can predict the outcome instead of estimating it,</item>
/// <item><c>Shader.disableBatching</c> — internal property, the other exact eligibility test.</item>
/// </list>
/// <para>So the undo is the literal inverse of the documented forward operation, member for member,
/// rather than an approximation of it.</para>
///
/// <para>THE CONTRACT THIS ENFORCES: <see cref="CanRevert"/> is resolved ONCE, at install, and the
/// batcher refuses to combine anything at all while it is false. There is no path on which this mod
/// mutates a game object it cannot hand back — which is the standing rule every game-object mutation
/// in this codebase lives under. On a Unity build that renamed these members the feature reports
/// itself unavailable and degrades to <see cref="BatchMode.Probe"/>; it never degrades to
/// "combine and hope".</para>
///
/// <para>COST. Six lookups at install, then open delegates (<see cref="Delegate.CreateDelegate"/>)
/// for the two hot writes, so applying and undoing a 1500-object pass costs ordinary calls rather
/// than 3000 <see cref="MethodBase.Invoke"/>s. The two read-only eligibility probes stay on
/// <c>Invoke</c> — they run once per distinct MESH and once per distinct SHADER per pass (roughly a
/// hundred calls), which is not worth a delegate.</para>
/// </summary>
internal static class StaticBatchInterop
{
    /// <summary>Set the renderer's (firstSubMesh, subMeshCount) slice — null while unresolved.</summary>
    private static Action<Renderer, int, int>? _setBatchInfo;

    /// <summary>Set the renderer's batch-root transform — null while unresolved.</summary>
    private static Action<Renderer, Transform?>? _setBatchRoot;

    /// <summary><c>StaticBatchingHelper.IsMeshBatchable</c>, or null when the type moved.</summary>
    private static MethodInfo? _isMeshBatchable;

    /// <summary><c>Shader.disableBatching</c> getter, or null when the property moved.</summary>
    private static MethodInfo? _shaderDisableBatching;

    /// <summary><c>MeshRenderer.enlightenVertexStream</c>, or null when the property moved.</summary>
    private static PropertyInfo? _enlightenStream;

    private static bool _resolved;

    /// <summary>
    /// True once every member needed to UNDO a combine has been found. The batcher gates
    /// <see cref="BatchMode.On"/> on this: no proven undo, no mutation.
    /// </summary>
    internal static bool CanRevert { get; private set; }

    /// <summary>One line naming exactly what was and was not found — for the log and the panel.</summary>
    internal static string Status { get; private set; } = "not resolved yet";

    /// <summary>
    /// Resolve everything, once. Safe to call repeatedly and from anywhere; never throws.
    /// </summary>
    internal static void Resolve()
    {
        if (_resolved)
            return;
        _resolved = true;

        // The two members the UNDO needs. Both are required — a batcher that can clear the slice
        // but not the root (or the other way round) leaves the renderer in a state neither Unity
        // nor this mod has a name for, so it is treated as "cannot revert" rather than "partly".
        _setBatchInfo = MakeAction3(typeof(Renderer), "SetStaticBatchInfo");
        _setBatchRoot = MakeSetter(typeof(Renderer), "staticBatchRootTransform");
        CanRevert = _setBatchInfo != null && _setBatchRoot != null;

        // The two eligibility probes and the Enlighten stream are OPTIONAL. Missing them costs
        // precision, never safety: an unpredicted mesh is simply one Unity declines to combine
        // (the batcher reconciles against reality afterwards, see StaticBatcher.Apply), and an
        // unrestorable Enlighten stream is a realtime-GI field this game does not use.
        _isMeshBatchable = FindStatic("UnityEngine.StaticBatchingHelper", "IsMeshBatchable");
        _shaderDisableBatching = typeof(Shader)
            .GetProperty("disableBatching", BindingFlags.Instance | BindingFlags.NonPublic)?.GetGetMethod(true);
        _enlightenStream = typeof(MeshRenderer)
            .GetProperty("enlightenVertexStream", BindingFlags.Instance | BindingFlags.NonPublic);

        Status = CanRevert
            ? $"undo path resolved (SetStaticBatchInfo + StaticBatchRoot); "
              + $"mesh test {(_isMeshBatchable != null ? "exact" : "estimated")}, "
              + $"shader test {(_shaderDisableBatching != null ? "exact" : "estimated")}"
            : $"UNDO PATH MISSING — SetStaticBatchInfo {(_setBatchInfo != null ? "ok" : "NOT FOUND")}, "
              + $"StaticBatchRoot {(_setBatchRoot != null ? "ok" : "NOT FOUND")}";

        if (CanRevert)
            VRLog.Info("Batch", $"Static-batching interop: {Status}.");
        else
            VRLog.Warn("Batch", $"Static-batching interop: {Status}. The pass will NOT combine "
                                + "anything — a combine this mod cannot hand back is not something "
                                + "it is allowed to do. Measurement (Mode = Probe) still works.");
    }

    // ==========================================================================================
    //  The two writes the undo is made of
    // ==========================================================================================

    /// <summary>
    /// Clear a renderer's static-batch state, exactly inverting what
    /// <c>InternalStaticBatchingUtility.MakeBatch</c> wrote: an EMPTY slice (subMeshCount 0 is what
    /// <c>isPartOfStaticBatch</c> reads as "not batched") and no batch root. The enabled off/on
    /// cycle is MakeBatch's own — it is how the renderer is made to re-read its geometry — so the
    /// undo performs it too rather than hoping the state change is noticed.
    /// </summary>
    internal static bool ClearBatchState(Renderer renderer)
    {
        if (renderer == null || _setBatchInfo == null || _setBatchRoot == null)
            return false;
        try
        {
            _setBatchInfo(renderer, 0, 0);
            _setBatchRoot(renderer, null);
            bool wasEnabled = renderer.enabled;
            renderer.enabled = false;
            renderer.enabled = wasEnabled;
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Batch", $"Clearing static-batch state threw {ex.GetType().Name} — that "
                                + "renderer stays combined until the scene changes.");
            return false;
        }
    }

    /// <summary>Read <c>MeshRenderer.enlightenVertexStream</c> (null when unavailable/unused).</summary>
    internal static Mesh? ReadEnlightenStream(Renderer renderer)
    {
        if (_enlightenStream == null || renderer is not MeshRenderer)
            return null;
        try
        {
            return _enlightenStream.GetValue(renderer, null) as Mesh;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Restore <c>MeshRenderer.enlightenVertexStream</c> (no-op when unavailable/null).</summary>
    internal static void WriteEnlightenStream(Renderer renderer, Mesh? stream)
    {
        if (_enlightenStream == null || stream == null || renderer is not MeshRenderer)
            return;
        try
        {
            _enlightenStream.SetValue(renderer, stream, null);
        }
        catch (Exception)
        {
            // A realtime-GI vertex stream this game does not use — worth restoring, never worth
            // failing an undo over.
        }
    }

    // ==========================================================================================
    //  The two eligibility probes — Unity's own tests, so the probe predicts rather than guesses
    // ==========================================================================================

    /// <summary>
    /// Unity's own <c>IsMeshBatchable</c>. Returns true when the test is unavailable: an
    /// optimistic answer costs a mesh that Unity then declines (harmless, and reconciled), while a
    /// pessimistic one would silently shrink the pass for a reason nobody could see.
    /// </summary>
    internal static bool IsMeshBatchable(Mesh mesh)
    {
        if (_isMeshBatchable == null || mesh == null)
            return mesh != null;
        try
        {
            return _isMeshBatchable.Invoke(null, new object[] { mesh }) is not bool ok || ok;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Unity's own shader test: <c>DisableBatchingType.False</c> (0) means the shader is happy to
    /// be batched; anything else disqualifies every renderer using it. Unavailable → false, same
    /// optimistic-and-reconciled reasoning as above.
    /// </summary>
    internal static bool ShaderDisablesBatching(Shader? shader)
    {
        if (_shaderDisableBatching == null || shader == null)
            return false;
        try
        {
            object? value = _shaderDisableBatching.Invoke(shader, null);
            return value != null && Convert.ToInt32(value) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ==========================================================================================
    //  Reflection plumbing
    // ==========================================================================================

    /// <summary>Open delegate over an internal instance method <c>void M(int, int)</c>.</summary>
    private static Action<Renderer, int, int>? MakeAction3(Type owner, string method)
    {
        try
        {
            MethodInfo? mi = owner.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(int), typeof(int) }, null);
            if (mi == null)
                return null;
            return (Action<Renderer, int, int>)Delegate.CreateDelegate(
                typeof(Action<Renderer, int, int>), mi);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Open delegate over an internal instance property setter taking a Transform.</summary>
    private static Action<Renderer, Transform?>? MakeSetter(Type owner, string property)
    {
        try
        {
            MethodInfo? setter = owner
                .GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetSetMethod(true);
            if (setter == null)
                return null;
            return (Action<Renderer, Transform?>)Delegate.CreateDelegate(
                typeof(Action<Renderer, Transform?>), setter);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A public-or-internal static method on a type named by its FULL name, looked up in the
    /// assembly that owns <see cref="Renderer"/> — i.e. UnityEngine.CoreModule, without hardcoding
    /// an assembly name that has moved between Unity versions before.
    /// </summary>
    private static MethodInfo? FindStatic(string typeFullName, string method)
    {
        try
        {
            Type? type = typeof(Renderer).Assembly.GetType(typeFullName, throwOnError: false);
            return type?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
