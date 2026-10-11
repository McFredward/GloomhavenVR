using System;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class BlessingChecks
{
    private static int _count;
    private static void Check(bool value, string text)
    { _count++; if (!value) throw new Exception(text); }

    internal static int Run()
    {
        _count = 0;
        var owner = new GameObject("Owner blessing");
        var observer = new GameObject("Observer blessing");
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        TownServiceDecor.MoneyBagTemplate = template.transform;
        try
        {
            using var a = new TownServiceTempleBowlMarker(owner.transform, true);
            using var b = new TownServiceTempleBowlMarker(observer.transform, true);
            FaceClock.Now = 0f; a.Tick(false); b.Tick(false);
            var aa = a.Root.GetComponentsInChildren<ParticleSystem>(true);
            var bb = b.Root.GetComponentsInChildren<ParticleSystem>(true);
            Check(aa.Length == 4 && bb.Length == 4, "both resident blessing phases exist before a shared event");
            a.SampleBlessing(7, 1, 0f, true);
            for (int n = 1; n <= 25; n++)
            { FaceClock.Now = n * .02f; a.SampleBlessing(7, 1, n * .02f, true); }
            // The observer first receives this event halfway through. Asset loading,
            // packet frequency and local frame rate cannot restart it at zero.
            FaceClock.Now = 20f; b.SampleBlessing(7, 1, .50f, true);
            for (int i = 0; i < aa.Length; i++)
            {
                Check(!aa[i].isPlaying && !bb[i].isPlaying,
                    "blessing simulation is owned by shared event age, not local automatic frames");
                var ap = new ParticleSystem.Particle[aa[i].main.maxParticles];
                var bp = new ParticleSystem.Particle[bb[i].main.maxParticles];
                int ac = aa[i].GetParticles(ap), bc = bb[i].GetParticles(bp);
                Check(ac == bc, "owner and late observer have the same seeded particle count");
                for (int n = 0; n < ac; n++)
                {
                    Check(Vector3.Distance(ap[n].position, bp[n].position) < .003f,
                        "owner and late observer share blessing particle positions");
                    Check(ap[n].randomSeed == bp[n].randomSeed,
                        "owner and late observer share blessing mote identity");
                }
            }
            Check(a.Root.transform.localScale == Vector3.one && b.Root.transform.localScale == Vector3.one,
                "shared blessing geometry cannot pulse from independent observer time");
            int before = aa[0].particleCount;
            a.SampleBlessing(7, 1, .50f, true);
            Check(aa[0].particleCount == before, "repeated blessing revision does not restart the particles");
            a.SampleBlessing(7, 1, TownServiceActivityMotion.TempleBlessingVisualSeconds, true);
            b.SampleBlessing(7, 1, TownServiceActivityMotion.TempleBlessingVisualSeconds, true);
            foreach (ParticleSystem system in aa) Check(system.particleCount == 0, "shared blessing has a bounded final clear");
            foreach (ParticleSystem system in bb) Check(system.particleCount == 0, "late observer clears the same completed blessing");
        }
        finally
        {
            TownServiceDecor.MoneyBagTemplate = null;
            UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(observer);
            UnityEngine.Object.DestroyImmediate(template);
        }
        return _count;
    }
}
