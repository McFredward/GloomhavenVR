using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Core;
using UnityEngine;

internal sealed class QuestPingAutoClock : MonoBehaviour
{
    internal static void Begin()
    {
        new GameObject("original-ping-clock-probe").AddComponent<QuestPingAutoClock>().StartCoroutine(Observe());
    }
    private static IEnumerator Observe()
    {
        var baseline = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("NativePing"));
        var patched = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("NativePing"));
        baseline.transform.position = new Vector3(-1, 0, 0); patched.transform.position = new Vector3(1, 0, 0);
        var originalSystems = new List<ParticleSystem>(baseline.GetComponentsInChildren<ParticleSystem>(true));
        var candidateSystems = new List<ParticleSystem>(patched.GetComponentsInChildren<ParticleSystem>(true));
        GameObject originalShield=null,patchedShield=null;
        var shieldPrefab=Resources.Load<GameObject>("NativeShield");
        if (shieldPrefab!=null)
        {
            originalShield=UnityEngine.Object.Instantiate(shieldPrefab);patchedShield=UnityEngine.Object.Instantiate(shieldPrefab);
            originalShield.transform.position=new Vector3(-2,0,0);patchedShield.transform.position=new Vector3(2,0,0);
            originalSystems.AddRange(originalShield.GetComponentsInChildren<ParticleSystem>(true));
            candidateSystems.AddRange(patchedShield.GetComponentsInChildren<ParticleSystem>(true));
        }
        var compactBaseline=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("NativePing"));
        var compactPatched=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("NativePing"));
        compactBaseline.transform.position=new Vector3(-1,0,0);compactPatched.transform.position=new Vector3(1,0,0);
        SetCompact(compactBaseline);SetCompact(compactPatched);
        originalSystems.AddRange(compactBaseline.GetComponentsInChildren<ParticleSystem>(true));candidateSystems.AddRange(compactPatched.GetComponentsInChildren<ParticleSystem>(true));
        GameObject compactOriginalShield=null,compactPatchedShield=null;
        if (shieldPrefab!=null)
        {
            compactOriginalShield=UnityEngine.Object.Instantiate(shieldPrefab);compactPatchedShield=UnityEngine.Object.Instantiate(shieldPrefab);
            compactOriginalShield.transform.position=new Vector3(-2,0,0);compactPatchedShield.transform.position=new Vector3(2,0,0);
            SetCompact(compactOriginalShield);SetCompact(compactPatchedShield);
            originalSystems.AddRange(compactOriginalShield.GetComponentsInChildren<ParticleSystem>(true));candidateSystems.AddRange(compactPatchedShield.GetComponentsInChildren<ParticleSystem>(true));
        }
        var originals=originalSystems.ToArray();var candidates=candidateSystems.ToArray();
        foreach (var system in originals) Prepare(system);
        foreach (var system in candidates) Prepare(system);
        var owner = new GameObject("original-ping-auto-head"); Camera camera = owner.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 5, -5); camera.transform.LookAt(Vector3.zero);
        camera.orthographic = true; camera.orthographicSize = 3; camera.aspect = 1; camera.nearClipPlane = .05f; camera.farClipPlane = 20;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var target = new RenderTexture(128, 128, 24); target.Create(); camera.targetTexture = target; camera.enabled = true;
        VRCameraPolicy.AllowedHead = camera;
        QuestStandalonePlatform.Enabled = true; QuestPingStreams.Attach(patched);
        if (patchedShield!=null) QuestPingStreams.AttachShield(patchedShield);
        QuestPingStreams.Attach(compactPatched);if(compactPatchedShield!=null)QuestPingStreams.AttachShield(compactPatchedShield);
        float started = Time.realtimeSinceStartup;
        var rows = new List<string>(); int sample = 0; float next = .1f; bool sameThroughWindow = true;
        while (Time.realtimeSinceStartup - started < 1.2f)
        {
            yield return null;
            float elapsed = Time.realtimeSinceStartup - started;
            if (elapsed < next) continue;
            next += .2f; sample++;
            for (int i = 0; i < originals.Length; i++)
            {
                var a = originals[i]; var b = candidates[i];
                sameThroughWindow &= Math.Abs(a.time-b.time) <= .05f && a.particleCount == b.particleCount
                    && a.GetComponent<Renderer>().isVisible && b.GetComponent<Renderer>().isVisible;
                string row = "elapsed="+elapsed+" system="+i+" culling="+a.main.cullingMode+" nativeTime="+a.time+" patchedTime="+b.time
                    +" nativeParticles="+a.particleCount+" patchedParticles="+b.particleCount
                    +" nativeVisible="+a.GetComponent<Renderer>().isVisible+" patchedVisible="+b.GetComponent<Renderer>().isVisible;
                rows.Add(row); Debug.Log("AUTO_CLOCK "+row);
            }
        }
        Directory.CreateDirectory("ProbeOutput"); File.WriteAllLines("ProbeOutput/auto-clock.txt", rows);
        bool same = sameThroughWindow;
        for (int i = 0; i < originals.Length; i++)
            same &= Math.Abs(originals[i].time-candidates[i].time) <= .05f;
        File.WriteAllText("ProbeOutput/auto-clock.json", "{\"sameNativeClock\":"+same.ToString().ToLowerInvariant()+",\"systems\":"+originals.Length+",\"samples\":"+sample+",\"nativeClock\":"+originals[0].time.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"patchedClock\":"+candidates[0].time.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"hardwareVerified\":false}\n");
        Debug.Log("AUTO_CLOCK_RESULT same="+same+" samples="+sample);
        QuestPingStreams.ResetAll(); UnityEngine.Object.Destroy(baseline); UnityEngine.Object.Destroy(patched); UnityEngine.Object.Destroy(originalShield); UnityEngine.Object.Destroy(patchedShield); UnityEngine.Object.Destroy(compactBaseline);UnityEngine.Object.Destroy(compactPatched);UnityEngine.Object.Destroy(compactOriginalShield);UnityEngine.Object.Destroy(compactPatchedShield);UnityEngine.Object.Destroy(owner);
        target.Release(); UnityEngine.Object.Destroy(target);
        UnityEditor.EditorApplication.Exit(same ? 0 : 3);
    }
    private static void SetCompact(GameObject effect)
    {
        var compact=new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV};
        foreach(var renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if(renderer.sharedMaterial.shader.name!="KriptoFX/RFX4/Particle")renderer.SetActiveVertexStreams(compact);
    }
    private static void Prepare(ParticleSystem system)
    {
        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.useAutoRandomSeed = false; system.randomSeed = 21; system.Play(false);
    }
}
