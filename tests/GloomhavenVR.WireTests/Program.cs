using System;
using System.IO;

namespace GloomhavenVR.WireTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        string repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();

        var t = new Harness();
        try
        {
            // Pin the two shimmed constants against the real declarations FIRST — every clamp
            // vector below is only meaningful while they agree.
            Shims.VerifyAgainstSource(repoRoot);
            GoldenVectors.Run(t);
            StoryVectors.Run(t);
            // The 3D map room's two records (20 + 21). Same reason StoryVectors is on this list,
            // one step further: these carry a PAGE that turns for somebody else and a POSE in a
            // frame that is NOT record 19's, and both failures are silent. A frame byte read
            // per-record instead of per-entry would put a peer's window in a place nobody chose,
            // and a truncated entry that ended the walk would swallow the FINISHED bit that clears
            // an ActionProcessor halt — a stuck party, with nothing thrown and nothing logged.
            MapSyncVectors.Run(t);
            RelaunchVectors.Run(t);
            ConfigStepVectors.Run(t, repoRoot);
            ScrollTurnGateVectors.Run(t);
            LiftWedgeVectors.Run(t);
            EnvSoundScheduleVectors.Run(t);
            HauntFigureVectors.Run(t);
            HeldSizeVectors.Run(t);
            PeerBoardFadeVectors.Run(t, repoRoot);
            // The two round-card facts of the 2026-08-15 hardware session: record 14's
            // standard-action qualifier byte and record 18's slot order.
            BoardSlotVectors.Run(t);
            // Source lint, not a packet: a bundled shader looked up with a bare Shader.Find
            // resolves to null with the bundle open, and has silently cost two builds.
            BundledShaderVectors.Run(t, repoRoot);
            // Same reason, different unobservable: a card face that is a few millimetres too small
            // for its body reads as "looks a bit off" from inside a headset and as nothing at all
            // from outside one. Report 12 (2026-08-15).
            CardFaceRectVectors.Run(t, repoRoot);
            // Where the player STANDS in the 3D campaign map, and at what scale. The one defect
            // this arithmetic can ship is hardware test #8's giant map below the player — the
            // failure that kept [Rig] Experimental3DMap unimplemented — and it is decidable from
            // four numbers without a headset.
            MapRoomSeatVectors.Run(t);
            // Which shader property is the mirror, and which way it points. ModBuild 160 writes
            // onto shaders whose property names nobody here has ever read (they ship compiled in
            // the game's bundles), so the one thing holding that up is the invariant that no cap
            // can ever make a surface SHINIER than authored — which is only observable by eye,
            // from inside a headset, and has now cost three hardware rounds.
            WaterReflectionVectors.Run(t);
            WaterEdgeVectors.Run(t);
            // Round five: the film's whole material is replaced rather than retuned, and the two
            // things that would silently undo that are properties of TEXT — the replacement shader
            // acquiring an environment sample (the head-bound reflection is the symptom the user
            // cannot switch off) and the blend going additive (which re-creates the pale sheet out
            // of the mod's own shader). Both are linted against the .shader source.
            WaterOwnSurfaceVectors.Run(t, repoRoot);
            // Which WALL owns a statue that two walls claim. The report it answers has been
            // photographed twice (skelet.jpg: the skeleton's head dissolved with one wall while
            // its body stayed with another), and the way the fix fails is an owner that flips
            // every rescan — a prop that pops while neither wall changes state, visible only from
            // inside a headset.
            WallPropUnitVectors.Run(t);
            // WHETHER a prop may fade with a wall at all. Same photograph (skelet.jpg), fourth
            // report, and the one piece of arithmetic in the wall-fade family that fails in both
            // directions in silence: too tight and the skull stays missing, too loose and masonry
            // becomes permanently solid — which reads as wall see-through being switched off.
            WallStandingPropVectors.Run(t);
            WallSignatureCulpritVectors.Run(t);
            // WHETHER THE SLICED TABLE AND THE ATOMIC ONE ARE THE SAME TABLE. The gate that
            // decides whether PERF B may ship, on a subsystem whose behaviour the user has just
            // called perfect — and its whole value is that it can FAIL, so it is driven here on
            // a null input and on a known positive of each of its four snap cases first.
            WallCommitDiffVectors.Run(t);
            // WHERE THE CONTROLS AND THE BOARD MESH END UP once the user's own tuned dials meet
            // the re-authored assets. The failure this pins is not a corrupted peer, it is a
            // board standing on edge with its keycaps in mid-air on the OWNER's screen — and the
            // inert path matters just as much, because a clamp that fired on the bundle he
            // already has would move Oak's caps 8 mm and be blamed on anything but the clamp.
            BoardSeatVectors.Run(t);
            BoardCapSymbolVectors.Run(t);
            // WHETHER THE WORD ON THE KEY IS THE WHOLE WORD. The ModBuild 281 cap said
            // "AUSWAHL BEEN" for "Auswahl beenden" and every gate on this list agreed with it. The
            // captions are live game text with runtime insertions, so this asserts a PROPERTY —
            // nothing is ever dropped — rather than a list of strings that cannot be complete.
            CapLabelFitVectors.Run(t, repoRoot);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL  threw: " + e);
            return 2;
        }
        return t.Report();
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "GloomhavenVR.sln")))
            d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }
}
