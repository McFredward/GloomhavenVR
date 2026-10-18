using System;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EnvironmentProgram
{
    public static int RunQuestCameras()
    {
        count = 0;
        foreach (bool instances in new[] { false, true }) QuestCameraLeases(instances);
        LateNativeCommandBufferConsumer();
        NativeChunkLightmapWrites();
        LateNativeLightingFlags();
        return count;
    }

    private static void QuestCameraLeases(bool instances)
    {
        using var room = new Room(); var first = room.Floor(.5f); var second = room.Floor(2.5f);
        if (instances)
        {
            second.GetComponent<MeshFilter>().sharedMesh = first.GetComponent<MeshFilter>().sharedMesh;
            room.Original.enableInstancing = true;
        }
        int final = 0, refused = 0;
        void Finish(Camera camera) { if (camera == room.Camera) final++; }
        void Refuse(Camera camera) { if (camera == room.Camera) refused++; }
        ScenarioCameraCullBoundary.Subscribe(Finish);
        ScenarioCameraCullBoundary.SubscribeUnavailable(Refuse);
        Camera.CameraCallback appended = _ => { };
        GameObject? nestedGo = null;
        Camera.CameraCallback? renderNested = null;
        try
        {
            Color32[] original = room.Render();
            Check(final == 0 && refused == 1 && !first.forceRenderingOff && !second.forceRenderingOff,
                "cold Quest geometry camera renders original native sources before its owner attaches");
            room.Render(); Check(final == 1, "attached Quest geometry camera has one actual final continuation");
            Configure(!instances, false, 100); PerfConfig.EnvironmentDrawInstancingOn = instances;
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(instances ? Members("_instances") == 1 : room.Chunks().Length == 1,
                "requested shared Frame environment path prepares actual private geometry on Quest");
            bool leased = false;
            room.ObserveRender = () => leased = first.forceRenderingOff && second.forceRenderingOff;
            Color32[] shown = room.Render(); room.ObserveRender = null;
            Check(leased && SamePixels(original, shown) && !first.forceRenderingOff && !second.forceRenderingOff,
                "Quest final owner preserves actual original pixels and paired source restoration for chunks and instances");

            int before = final, fallback = refused;
            room.ObserveRender = () =>
            {
                leased = first.forceRenderingOff && second.forceRenderingOff;
                Camera.onPreCull += appended;
            };
            shown = room.Render(); room.ObserveRender = null;
            Check(leased && final == before && refused == fallback + 1 && SamePixels(original, shown)
                && !first.forceRenderingOff && !second.forceRenderingOff && room.Camera.commandBufferCount == 0,
                "changed public callback snapshot revokes actual geometry and native masks before culling without losing pixels");
            Camera.onPreCull -= appended;
            room.ObserveRender = () => leased = first.forceRenderingOff && second.forceRenderingOff;
            room.Render(); room.ObserveRender = null;
            Check(final == before + 1 && leased, "stable subsequent camera resumes the requested shared geometry path");

            nestedGo = new GameObject("Quest actual nested geometry camera"); var nested = nestedGo.AddComponent<Camera>();
            nested.CopyFrom(room.Camera); nested.enabled = false;
            var target = new RenderTexture(16, 16, 24); target.Create(); nested.targetTexture = target;
            bool inChild = false, childOriginals = false, parentStarted = false;
            renderNested = camera =>
            {
                if (camera == nested) { childOriginals = !first.forceRenderingOff && !second.forceRenderingOff; return; }
                if (camera != room.Camera || inChild) return;
                parentStarted = first.forceRenderingOff && second.forceRenderingOff;
                inChild = true; nested.Render(); inChild = false;
            };
            Camera.onPreCull += renderNested;
            try
            {
                shown = room.Render();
                Check(parentStarted && childOriginals && SamePixels(original, shown)
                    && !first.forceRenderingOff && !second.forceRenderingOff && nested.commandBufferCount == 0,
                    "unseen nested camera revokes outer optional geometry safely and both cameras retain native original pixels");
                Camera.onPreCull -= renderNested;
                bool childLease = false, outerLease = false;
                renderNested = camera =>
                {
                    if (camera == nested) { childLease = first.forceRenderingOff && second.forceRenderingOff; return; }
                    if (camera != room.Camera || inChild) return;
                    inChild = true; nested.Render(); inChild = false;
                    outerLease = first.forceRenderingOff && second.forceRenderingOff;
                };
                Camera.onPreCull += renderNested;
                shown = room.Render();
                Check(childLease && outerLease && SamePixels(original, shown)
                    && !first.forceRenderingOff && !second.forceRenderingOff
                    && room.Camera.commandBufferCount == 0 && nested.commandBufferCount == 0,
                    "two qualified nested Quest cameras retain independent geometry leases and restore the outer render correctly");
            }
            finally { nested.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
        }
        finally
        {
            room.ObserveRender = null; Camera.onPreCull -= appended;
            if (renderNested != null) Camera.onPreCull -= renderNested;
            if (nestedGo != null) Object.DestroyImmediate(nestedGo);
            ScenarioCameraCullBoundary.Unsubscribe(Finish);
            ScenarioCameraCullBoundary.UnsubscribeUnavailable(Refuse);
        }
    }
}
