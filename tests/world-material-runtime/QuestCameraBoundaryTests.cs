using System;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(32700)]
public sealed class LateNativeCameraCallback : MonoBehaviour
{
    public Action? BeforeCull;
    private void OnPreCull() => BeforeCull?.Invoke();
}

public static partial class WorldMaterialProgram
{
    private static bool QuestCameraCallbacks => (bool)typeof(ScenarioCameraCullBoundary)
        .GetField("_questCallbacks", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static void QuestCameraBoundary(GameObject host, MeshRenderer source,
        Material first, Material second, Camera world)
    {
        if (!QuestCameraCallbacks) return;
        source.sharedMaterials = new[] { first, second }; source.SetPropertyBlock(null);
        first.SetFloat("_AddVertexAnim", 0f); PerfConfig.WorldMaterialQualityMode = 2; Tick(host, 16);
        int final = 0, unavailable = 0, native = 0;
        Camera? child = null;
        void Final(Camera camera) { if (camera == world || camera == child) final++; }
        void Unavailable(Camera camera) { if (camera == world || camera == child) unavailable++; }
        ScenarioCameraCullBoundary.Subscribe(Final);
        ScenarioCameraCullBoundary.SubscribeUnavailable(Unavailable);
        Camera.CameraCallback ordinary = camera => { if (camera == world) native++; };
        Camera.CameraCallback appended = camera => { if (camera == world) native += 10; };
        Camera.CameraCallback? changing = null, nested = null;
        LateNativeCameraCallback? late = null;
        try
        {
            Camera.onPreCull += ordinary;
            int before = final; Center(world);
            Check(final == before + 1 && native == 1 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "Quest owner runs after actual native public callbacks and adopts the requested private material");
            Camera.CameraCallback stable = Camera.onPreCull;
            before = final; Center(world); Center(world);
            Check(final == before + 2 && ReferenceEquals(stable, Camera.onPreCull),
                "stable Quest cameras retain the identical delegate list without per-render remove/combine allocation");
            Camera.onPreCull += appended; before = final; Center(world);
            Check(final == before + 1 && native == 14,
                "Quest owner re-arms behind a newly registered native listener before culling");
            Camera.onPreCull -= appended; before = final; Center(world);
            Check(final == before + 1 && native == 15,
                "Quest owner preserves actual listener removal and one final callback");

            changing = camera =>
            {
                if (camera != world) return;
                Camera.onPreCull -= changing; Camera.onPreCull += appended;
            };
            Camera.onPreCull += changing; before = final; int refused = unavailable;
            Center(world);
            Check(final == before && unavailable == refused + 1 && source.sharedMaterial == first,
                "mutation during actual public dispatch revokes private slots before native culling instead of claiming finality");
            Center(world);
            Check(final == before + 1 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "the next actual camera invocation admits the now stable native callback list");
            Camera.onPreCull -= appended;

            late = world.gameObject.AddComponent<LateNativeCameraCallback>();
            late.BeforeCull = () => { Camera.onPreCull += appended; late.BeforeCull = null; };
            before = final; refused = unavailable; Center(world);
            Check(final == before && unavailable == refused + 1 && source.sharedMaterial == first,
                "a later native component changing the armed public list retains original material pixels for that invocation");
            Center(world);
            Check(final == before + 1 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "stable subsequent native component invocation regains the qualified material stage");
            Camera.onPreCull -= appended; Object.DestroyImmediate(late); late = null;

            var childGo = new GameObject("Cold nested Quest camera"); child = childGo.AddComponent<Camera>();
            child.CopyFrom(world); child.enabled = false;
            child.targetTexture = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat); child.targetTexture.Create();
            before = final; refused = unavailable; Center(child);
            Check(final == before && unavailable == refused + 1 && source.sharedMaterial == first,
                "first unseen Quest camera renders current native originals while its own boundary attaches");
            Center(child);
            Check(final == before + 1 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "second unseen-camera invocation reaches its own qualified final boundary");
            // A fresh camera created inside an actual parent callback must not inherit
            // that parent's finality or leave its original slots masked by another eye.
            var coldGo = new GameObject("Nested unseen native camera"); var cold = coldGo.AddComponent<Camera>();
            cold.CopyFrom(child); cold.enabled = false; cold.targetTexture = child.targetTexture;
            bool observedOriginals = false;
            nested = camera =>
            {
                if (camera != world) return;
                Center(cold); observedOriginals = source.sharedMaterial == first;
            };
            Camera.onPreCull += nested; before = final; Center(world);
            Check(final == before + 1 && observedOriginals && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "nested cold camera restores originals and parent final validation reapplies its own current variant");
            Camera.onPreCull -= nested; Object.DestroyImmediate(coldGo);
            nested = camera => { if (camera == world) Center(child); };
            Camera.onPreCull += nested; before = final; Center(world);
            Check(final == before + 2 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "two nested qualified cameras keep independent armed snapshots and preserve the outer final continuation");
            Camera.onPreCull -= nested; nested = null;

            var owner = (Behaviour)world.GetComponent(typeof(ScenarioCameraCullBoundary)
                .GetNestedType("QuestFinalCull", BindingFlags.NonPublic)!);
            owner.enabled = false; before = final; refused = unavailable; Center(world);
            Check(final == before && unavailable == refused + 1 && source.sharedMaterial == first,
                "disabled camera boundary cannot leave optional source material ownership active");
            owner.enabled = true; Center(world);
            Check(final == before + 1 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "re-enabled camera owner resumes actual native final validation without duplicate callbacks");
            Object.DestroyImmediate(owner); before = final; refused = unavailable; Center(world);
            Check(final == before && unavailable == refused + 1 && source.sharedMaterial == first,
                "destroyed camera owner is recreated through one native-original invocation");
            Center(world);
            Check(final == before + 1 && WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "recreated camera owner resumes one qualified native continuation");
        }
        finally
        {
            Camera.onPreCull -= ordinary; Camera.onPreCull -= appended;
            if (changing != null) Camera.onPreCull -= changing;
            if (nested != null) Camera.onPreCull -= nested;
            if (late != null) Object.DestroyImmediate(late);
            ScenarioCameraCullBoundary.Unsubscribe(Final);
            ScenarioCameraCullBoundary.UnsubscribeUnavailable(Unavailable);
            if (child != null) { Object.DestroyImmediate(child.targetTexture); Object.DestroyImmediate(child.gameObject); }
        }
    }
}
