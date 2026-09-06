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
            // SPATIAL VOICE CHAT (ModBuild 297). Same reason EnvSoundSchedule is above it, one
            // step worse: this one needs a headset AND a second person to observe at all, and the
            // rolloff's failure is silence rather than a wrong sound. The vectors assert the curve
            // in DECIBELS (the acceptance criterion is stated that way, so the test states it that
            // way), the indicator's hysteresis (its failure mode is a strobing icon), and the
            // loudspeaker glyph — which it also writes to .planning/voice/ so the pictures come
            // from the shipped rasteriser rather than from a lookalike.
            VoiceVectors.Run(t, repoRoot);
            HauntFigureVectors.Run(t);
            HeldSizeVectors.Run(t);
            HeldSizeVectors.RunBounds(t);
            // Record 37 — a map item in a peer's hand. Driven byte by byte because its slot count
            // is its LENGTH, because every one of its rejections is silent by design, and because a
            // prop moved out of the world by a bad packet never comes back: unlike a figure,
            // nothing in the game re-authors a prop's transform.
            HeldPropVectors.Run(t);
            // The short-rest sacrifice's seat (39). Same reasons as record 37 above, plus the one
            // that is peculiar to it: a 2-byte record is ALWAYS recess 1's, so a sacrifice lying in
            // recess 2 must force the long form or every peer draws its front in the wrong recess.
            SacrificeSeatVectors.Run(t);
            // Which HALF of which round card is already used (41, report item 8). Driven byte by
            // byte because the record's whole job is saying WHICH of four regions is spent: a bit
            // that lands on the wrong recess greys a card its owner can still play, and neither end
            // can see the swap. Half the byte is also unassigned, so the strip that keeps a future
            // field from dimming a half this build has no name for is asserted here too.
            SpentHalfVectors.Run(t);
            // WHICH PILE the sender's fan is drawn from (43, report item 7). Driven byte by byte
            // for the reason record 41 is: the HAND is the DEFAULT and is deliberately unsayable,
            // so "my fan is my hand" and "no record" must stay one state or a fan that stopped
            // being a pick keeps resolving against the discard list — every face in it then a
            // confidently wrong card. And an id this build cannot name must degrade to the HAND
            // rather than to a pile, which is the only degradation that cannot mislead.
            FanSourceVectors.Run(t);
            // The left hand is the mirror of the right, for a held mini and a held map item alike
            // — one shared definition, driven against the reflection it is supposed to be.
            HeldPoseMirrorVectors.Run(t, repoRoot);
            PeerBoardFadeVectors.Run(t, repoRoot);
            // The two round-card facts of the 2026-08-15 hardware session: record 14's
            // standard-action qualifier byte and record 18's slot order.
            BoardSlotVectors.Run(t);
            // Source lint, not a packet: a bundled shader looked up with a bare Shader.Find
            // resolves to null with the bundle open, and has silently cost two builds.
            BundledShaderVectors.Run(t, repoRoot);
            // Same shape again: a source lint for an invariant that a doc-block sentence failed to
            // hold. The element channel may publish no periodic term at any strength — the third
            // hardware round on that defect is what bought this file.
            ElementSteadyVectors.Run(t, repoRoot);
            // Same reason, different unobservable: a card face that is a few millimetres too small
            // for its body reads as "looks a bit off" from inside a headset and as nothing at all
            // from outside one. Report 12 (2026-08-15).
            CardFaceRectVectors.Run(t, repoRoot);
            // Where the player STANDS in the 3D campaign map, and at what scale. The one defect
            // this arithmetic can ship is hardware test #8's giant map below the player — the
            // failure that kept [Rig] Experimental3DMap unimplemented — and it is decidable from
            // four numbers without a headset.
            MapRoomSeatVectors.Run(t);
            // A SHARED WINDOW IS THE SAME SIZE FOR EVERY PLAYER (user ruling 2026-09-06,
            // "Gewährleiste das"). On this list for the reason every entry above it is: the failure
            // is invisible from one machine. The ModBuild 448 logs had the same story window at
            // 675 mm tall for the host and 502 mm for the co-player, and the only difference between
            // the two clients was the SHAPE OF THE MONITOR. Half of this runs the arithmetic against
            // every display shape and dial setting the config allows; the other half reads the
            // shipped source of the law and its four call sites, because a future edit that reaches
            // for a static changes no signature and would break no vector.
            SharedWindowSizeVectors.Run(t, repoRoot);
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
            // PERF S6: the SAME question that pass asks — "which segments hold this
            // renderer?" — answered the old way and the new way over the ModBuild 435
            // board, driven against each other. The equivalence half is the one that
            // matters: a faster answer to a different question would move a prop onto a
            // wall nobody chose, which is only visible from inside a headset and is the
            // exact defect skelet.jpg reported. The cost half prints a RATIO.
            WallPropUnitHolderVectors.Run(t);
            // WHETHER a prop may fade with a wall at all. Same photograph (skelet.jpg), fourth
            // report, and the one piece of arithmetic in the wall-fade family that fails in both
            // directions in silence: too tight and the skull stays missing, too loose and masonry
            // becomes permanently solid — which reads as wall see-through being switched off.
            WallStandingPropVectors.Run(t);
            // THE SAME SHAPE, ONE RULING LATER: whether a renderer is a FLOOR TILE, which by the
            // user's ruling of 2026-09-05 may never be faded by anything. It fails in both
            // directions in silence — too tight and the hexes the doors stand on keep vanishing
            // (fehlende_boden_tiles.jpg), too loose and a pillar's foot or a low wall goes
            // permanently solid (säulen.jpg, the report from the other side).
            WallFloorTileVectors.Run(t);
            WallSignatureCulpritVectors.Run(t);
            // ModBuild 439 — THE DECODER THAT NAMES THE RENDERER BEHIND A "RANDOM" HITCH. The
            // user accepts a stall when a door opens and does not accept the ones long after it
            // ("diese Hänger lange danach die einfach 'random' auftreten sind störend"), and in
            // his ModBuild 438 log every one of the late commits is the scene-signature term
            // with nothing shipped able to say which renderer moved it. This inverts the fold to
            // answer that, so its arithmetic is driven against the log's own hashes here.
            WallSigDeltaVectors.Run(t);
            // WHOSE GEOMETRY GETS THE HOVER GLOW — the miniature's body or the furniture hanging
            // off it. The report it answers ("der Boss-Drache hat immer noch KEIN Highlighting")
            // took from ModBuild 293 to ModBuild 342, and every build in between compiled clean,
            // engaged the highlight and returned true: the glow was drawn on a dart. The judgement
            // is five integers in and one grade out, so the part that does not need a headset is
            // settled here — against the SHIPPED methods, which are linked in, not copied.
            FigureHighlightGradeVectors.Run(t);
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
            // WHETHER A GAME STRING'S RICH-TEXT TAGS REACH THE KEY. The CONFIRM cap printed
            // '<SPRITE NAME="LOST"> VERBRENNEN …' for the game's card-burn wording; the seam
            // that strips it is pure string code and the screenshot string is pinned here.
            KeycapLabelVectors.Run(t);
            // SOURCE LINT for record 35: the mirrored item-usable frame must be cut to the
            // OWNER's numbers. Both frames use the shared SoftCueArt machinery, which is the part
            // that matters; the pixel geometry is declared twice because the owner's constants are
            // private to a nested class, and "share the machinery, not the look" is a failure this
            // project has already paid for. The drift is invisible from inside either headset.
            ItemUsableVectors.Run(t, repoRoot);
            // WHICH QUESTION A MANDATORY-DECISION TERM ANSWERS. The narrowest change on this list
            // and the one with the widest blast radius: a caller asked a UNION whose last term is
            // a derived net every map-room destination carries, and the flat game's own
            // single-window hide then closed seventeen merchants and temples in one two-player
            // session — while the build that shipped it asserted in its own message that the
            // merchant and the temple still stood open together. Nothing in this repository could
            // have noticed. The member pin makes the next widening fail here instead.
            MandatoryDecisionTermVectors.Run(t, repoRoot);
            // WHERE A MIRRORED CARD FLIGHT LANDS. Two endpoints share ONE byte's two nibbles, and
            // the receiver may be a different build from the sender — so the enum's width and
            // NetCardFx.Clamp's bound are a sender/receiver contract no single end can observe.
            // The active-matrix anchor (user item 8b) is the first value added since that codec
            // shipped; these vectors pin the nibble, the transposition, and the older-build
            // degradation the addition relies on.
            ActiveAnchorVectors.Run(t);
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
