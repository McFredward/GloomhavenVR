using System;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;

internal static class AudioClockChecks
{
    private static int count;
    private static void Check(bool okay, string message) { count++; if (!okay) throw new Exception(message); }
    internal static int Run()
    {
        count = 0;
        foreach (int rate in new[] { 30, 90 })
        {
            var clock = new TownServiceActivitySoundClock(); int coins = 0, spells = 0;
            for (byte service = 1; service <= 3; service += 2)
            {
                clock.Reset();
                int frames = (int)((service == 1 ? TownServiceActivityMotion.MerchantCycleSeconds : 48f) * 4f * rate);
                for (int frame = 0; frame < frames; frame++)
                {
                    var state = new TownActivityPose { WorkClock = (float)frame / rate, TransitionAge = .65f };
                    var shown = TownServiceActivityMotion.Visual(service, in state);
                    var sound = clock.Sample(service, 1, 3, state.WorkClock, 1f / rate, true, in shown);
                    if (sound == TownActivitySound.Coin) coins++;
                    if (sound == TownActivitySound.Spell) spells++;
                    if (frame == 0) Check(sound == TownActivitySound.None, "late join seeds activity audio silently");
                }
            }
            Check(coins == 24, "each visible coin deposit sounds once independent of frame rate: " + coins);
            Check(spells == 4, "one subtle sound per actual experiment independent of frame rate: " + spells);
        }
        var edge = new TownServiceActivitySoundClock();
        var held = new TownActivityVisual { CoinGrip = new UnityEngine.Vector3(1, 0, 0) };
        var released = new TownActivityVisual();
        edge.Sample(1, 1, 1, 1f, .01f, true, in held);
        Check(edge.Sample(1, 1, 1, 1.01f, .01f, true, in released) == TownActivitySound.Coin, "actual release triggers coin foley");
        Check(edge.Sample(1, 1, 1, 1.02f, .01f, true, in released) == TownActivitySound.None, "unchanged released pose cannot repeat audio");
        foreach (int boundary in new[] { 0, 1, 2, 3, 4, 5 })
        {
            edge.Reset(); edge.Sample(1, 1, 1, 2f, .01f, true, in held);
            int author = boundary == 0 ? 2 : 1; uint epoch = boundary == 1 ? 2u : 1u;
            float time = boundary == 2 ? 1f : boundary == 3 ? 8f : 2.01f;
            float elapsed = boundary == 4 ? 1f : .01f;
            Check(edge.Sample(1, author, epoch, time, elapsed, boundary != 5, in released) == TownActivitySound.None,
                "authority seek stall or hide cannot replay historical contact " + boundary);
            Check(edge.Sample(1, author, epoch, time + .01f, .01f, true, in released) == TownActivitySound.None,
                "resumed audio seeds without a deferred burst " + boundary);
        }
        return count;
    }
}
