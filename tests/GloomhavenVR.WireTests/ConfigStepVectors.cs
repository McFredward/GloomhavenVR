// THE STEPPER GUARD — what one press of a debug-menu ◀ / ▶ is worth.
//
// WHY THIS IS PINNED HERE, in a project otherwise entirely about wire formats. It is the same
// shape of defect for the same reason: a value that is only ever observed from inside a headset,
// by feel, one press at a time. A step that is a hundred times too small does not fail, does not
// log and does not look wrong — it produces a dial the player reports as "hat keinen Einfluss"
// after pressing it twenty times. There is no safe place to discover that, so it is asserted here
// instead. Three separate rounds of it reached the user before this file existed:
//
//   * the per-board asset rotation, stepping 0.01° because `AssetPitchDegrees_Oak` ends in a
//     VARIANT TAG and the unit word was therefore invisible;
//   * `[WristHud] GloveOffsetX`, stepping 0.05 MILLIMETRES because it ends in an AXIS LETTER —
//     while its sibling `GloveOffsetY` stepped 1 mm, from the same table, for the same HUD;
//   * `[RoundButtons] OffsetX` at 1 mm beside `OffsetY` at 5 mm, on the Skip/Fixier group the
//     user had already reported once.
//
// `ConfigSteps` is free of Unity, BepInEx and the game model, so it links in whole and the real
// resolver is what runs below — not a copy of it.
//
// The sweep at the bottom is the part that matters most: it does not test a list of keys somebody
// remembered to add, it reads EVERY shipped default out of src/GloomhavenVR/Defaults/ and fails on
// the next axis-suffixed dial whoever adds it, whether or not they thought about stepping.

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.WireTests;

internal static class ConfigStepVectors
{
    internal static void Run(Harness t, string repoRoot)
    {
        Vectors(t);
        Sweep(t, repoRoot);
        ExplicitDrift(t, repoRoot);
        Rule(t, repoRoot);
    }

    // =============================================================================================
    //  1. Hand-written vectors — one per SHAPE a key can have
    // =============================================================================================

    private static void Vectors(Harness t)
    {
        // ---- the plain case the table was always able to read ----------------------------------
        t.Case("configsteps/plain-suffix");
        Unit(t, "WorldTiltDegrees", 1d, ConfigSteps.UnitScope.Component);
        Unit(t, "RecenterHoldSeconds", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "FanRadius", 0.001d, ConfigSteps.UnitScope.Value);
        Unit(t, "TrayScale", 0.05d, ConfigSteps.UnitScope.Value);
        // The dissolve floor (black-frame round 11, 2026-08-11): a 0..1 proportion whose shipped
        // default is a deliberately tiny 0.004 — exactly the shape whose magnitude fallback would
        // produce a step nobody can feel. "Fraction" is the unit word that keeps it steppable.

        // ---- MILLIMETRES ARE NOT METRES, and the suffix test cannot tell them apart on its own --
        // "Millimeters" ENDS IN "Meters", so with the two entries in the wrong order a 40 mm dial
        // would take the metre step of 0.01 — a hundredth of a millimetre a press, the same shape
        // of defect as the axis-letter reports below and just as invisible from inside a headset.
        // Pinned here because the only thing keeping it right is the ORDER of two rows in a table.
        t.Case("configsteps/millimetres-before-metres");
        Unit(t, "PickRadiusMillimeters", 5d, ConfigSteps.UnitScope.Value);
        Unit(t, "BoardMinWidthMeters", 0.001d, ConfigSteps.UnitScope.Value);

        // ---- the variant tag (report #1: the asset rotation at 0.01°) ---------------------------
        // The tag is stripped for the unit lookup AND remembered: a per-board default is that
        // board's geometry, so it may coarsen the step but never sharpen it.
        t.Case("configsteps/variant-tag");
        Unit(t, "PileSpacing_Steel", 0.001d, ConfigSteps.UnitScope.Variant);
        Unit(t, "SlotOverlaySpacing_Bronze", 0.001d, ConfigSteps.UnitScope.Variant);
        Unit(t, "DecisionGap_Oak", 0.001d, ConfigSteps.UnitScope.Variant);
        // The slot-overlay SIZE (2026-08-11). Its retired predecessor was called SlotCardFill, and
        // "Fill" is in no unit row — so that dial had been stepping off its own default's magnitude
        // all along, the exact defect this file exists to pin. Pinned as the reason the successor is
        // named …Scale: a factor's step is 0.05, not a fiftieth of whatever it happened to ship at.
        Unit(t, "SlotOverlayScale_Oak", 0.05d, ConfigSteps.UnitScope.Variant);
        // …unless the key also names a pose. A pitch is a pitch on every board.
        Unit(t, "AssetPitchDegrees_Oak", 1d, ConfigSteps.UnitScope.Component);
        Unit(t, "BoardTilt_Oak", 1d, ConfigSteps.UnitScope.Component);

        // ---- the axis letter (report #2 and #3) -------------------------------------------------
        t.Case("configsteps/axis-letter");
        Unit(t, "GloveOffsetX", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveOffsetY", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveOffsetZ", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "OffsetX", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "ConfirmUndoInsetX", 0.001d, ConfigSteps.UnitScope.Component);
        // both decorations at once
        Unit(t, "OffsetX_Oak", 0.001d, ConfigSteps.UnitScope.Component);

        // ---- a direction spelled as a word, trailing or in the middle ---------------------------
        t.Case("configsteps/direction-word");
        Unit(t, "HeldOffsetSide", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "HeldOffsetUp", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "HeldOffsetForward", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveLateralOffset", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveVerticalOffset", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveForwardOffset", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "SpawnSideMeters", 0.001d, ConfigSteps.UnitScope.Component);
        Unit(t, "CombatLogRight", 0.001d, ConfigSteps.UnitScope.Component);

        // ---- a clamp end is still the thing it clamps -------------------------------------------
        t.Case("configsteps/min-max-qualifier");
        Unit(t, "BoardPitchMin_Oak", 1d, ConfigSteps.UnitScope.Component);
        Unit(t, "BoardPitchMax_Oak", 1d, ConfigSteps.UnitScope.Component);
        Unit(t, "ScaleMin", 0.05d, ConfigSteps.UnitScope.Value);
        // The HELD-FIGURE STRETCH family (2026-08-11): the gesture's two clamps end in a Min/Max
        // qualifier BEHIND the unit word — the exact shape that has shipped dead dials before —
        // and its capture radius rides the millimetres row. Pinned on the day they were added.
        Unit(t, "StretchScaleMin", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "StretchScaleMax", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "StretchReachMillimeters", 5d, ConfigSteps.UnitScope.Value);

        // ---- THE HEALTH BARS' SIZE AND ITS TWO BOUNDS MOVE ALIKE --------------------------------
        // One size and the min/max the table zoom may carry it between. The bounds are worth
        // nothing if they cannot be tuned at the resolution of the value they bound, and the
        // Min/Max qualifier sits BEHIND the unit word in two of the three keys — the shape that
        // shipped the asset rotation at 0.01°. Asserted through the resolver, not by reading the
        // written-down table, so a future change to either answer has to face this.
        //
        // 2026-08-13: [WorldUI] BarZoomMinScale/BarZoomMaxScale are NO LONGER SHIPPED DIALS (user:
        // "Mindest und Maximalgröße der Lebensbalken haben keinen sehbaren einfluss … ziemlich
        // unintuitiv" — they clamped a follow factor that is 1.0 at the shipped zoom, so neither
        // bound was reachable where the player sits; the band is the constant pair
        // ActorBars.ZoomFollowMin/Max now). Their two NAMES stay here as pure RESOLVER VECTORS —
        // this half of the file tests the name→step rule, not the shipped key list, and the
        // qualifier-behind-the-unit-word shape is exactly the defect class the file exists for, so
        // it must keep answering correctly for the day such a key comes back. What could NOT stay
        // is a written-down step for a key with no Defaults line: the explicit-table sweep below
        // rejects that by name, which is why the family check now runs through the resolver.
        t.Case("configsteps/bar-size-family");
        Unit(t, "BarSizeScale", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "BarZoomMinScale", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "BarZoomMaxScale", 0.05d, ConfigSteps.UnitScope.Value);
        t.True(ConfigSteps.TryUnit("BarSizeScale", out double barStep, out _)
               && ConfigSteps.TryUnit("BarZoomMinScale", out double barLoStep, out _)
               && ConfigSteps.TryUnit("BarZoomMaxScale", out double barHiStep, out _)
               && barStep == barLoStep && barStep == barHiStep,
               "the health-bar size and both of its bounds must resolve to the SAME step — "
               + "a bound that steps more coarsely than the value it bounds cannot be set to it");

        // ---- an angle that is NOT one axis of an orientation ------------------------------------
        // "Degrees" says the number is an angle; it does not say it is a coordinate. A sweep and a
        // threshold are single quantities and their own magnitude is a perfectly good guide, so
        // they must NOT come back as components or they lose that guide.
        t.Case("configsteps/angle-is-not-a-pose");
        Unit(t, "FanArcSweepDegrees", 1d, ConfigSteps.UnitScope.Value);
        Unit(t, "RevealEnterDegrees", 1d, ConfigSteps.UnitScope.Value);
        // …but a named euler axis is.
        Unit(t, "GlovePalmPitch", 1d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveGripRollDegrees", 1d, ConfigSteps.UnitScope.Component);
        Unit(t, "HeldFaceYawDegrees", 1d, ConfigSteps.UnitScope.Component);

        // ---- THE FALSE POSITIVES. A wrong step is as bad as a tiny one. -------------------------
        // Every one of these CONTAINS a unit word or ends in a letter the resolver strips. None of
        // them is that unit, and the resolver must still say no — a decoration is only stripped
        // when a unit word actually stands behind it.
        t.Case("configsteps/no-false-positives");
        NoUnit(t, "ScaleFallback");                 // a policy, not a scale
        NoUnit(t, "InitiativeDepthMaxSpreadPx");    // pixels; "Depth" sits in the middle
        NoUnit(t, "HexHintSide");                   // "Side" with nothing behind it
        NoUnit(t, "StableDepthBias");               // the table refuses "Bias" on purpose
        NoUnit(t, "HeldFaceBias");
        NoUnit(t, "FanSideDepthCurve");             // a curve, not a depth
        NoUnit(t, "TouchRange");
        NoUnit(t, "CurlInputFullAt");
        NoUnit(t, "DecisionOffsetYRebased");        // a one-shot bool marker with an axis mid-key

        // ---- hump boundaries. "Up" is a direction; "Upright" is a word that starts with it. -----
        t.Case("configsteps/pose-word-hump-boundary");
        t.True(!ConfigSteps.TryUnit("UprightScale", out _, out ConfigSteps.UnitScope s1)
               || s1 != ConfigSteps.UnitScope.Component,
               "'Upright' must not read as the direction 'Up'");
        t.True(!ConfigSteps.TryUnit("DownforceScale", out _, out ConfigSteps.UnitScope s2)
               || s2 != ConfigSteps.UnitScope.Component,
               "'Downforce' must not read as the direction 'Down'");
        t.True(ConfigSteps.TryUnit("SpawnDownMeters", out _, out ConfigSteps.UnitScope s3)
               && s3 == ConfigSteps.UnitScope.Component,
               "'Down' followed by a new hump IS the direction");
    }

    private static void Unit(Harness t, string key, double step, ConfigSteps.UnitScope scope)
    {
        bool got = ConfigSteps.TryUnit(key, out double s, out ConfigSteps.UnitScope sc);
        t.True(got, $"'{key}' must resolve through a unit word, not the magnitude fallback");
        if (!got)
            return;
        t.Equal(step, s, $"'{key}' step");
        t.Equal(scope.ToString(), sc.ToString(), $"'{key}' scope");
    }

    private static void NoUnit(Harness t, string key)
    {
        t.True(!ConfigSteps.TryUnit(key, out _, out _),
               $"'{key}' must NOT be read as a unit — a wrong step is as bad as a tiny one");
    }

    // =============================================================================================
    //  2. The sweep — every shipped default, not a list somebody maintained
    // =============================================================================================

    /// <summary>
    /// `internal const float X = 1f;   // =&gt; [Section] Key` — the annotation that
    /// scripts/rebase-defaults.py already treats as machine-readable, plus the declared TYPE.
    ///
    /// <para>The type is not decoration. Only a NUMBER gets a stepper, and the config has keys
    /// that look like coordinates and are not: `[Cards] DecisionOffsetYRebased` is a bool
    /// one-shot marker with an axis letter mid-key. Sweeping it would demand a step for a
    /// toggle.</para>
    /// </summary>
    private static readonly Regex Annotated =
        new(@"(?:const|static\s+readonly)\s+(?<type>[A-Za-z0-9_.]+)\s+[A-Za-z0-9_]+\s*=\s*(?<val>.*?);?\s*"
            + @"//\s*=>\s*\[(?<sec>[^\]]+)\]\s*(?<key>[A-Za-z0-9_]+)", RegexOptions.Compiled);

    /// <summary>The types <c>ConfigCatalog</c> gives a stepper to (its own <c>IsNumeric</c>).</summary>
    private static readonly HashSet<string> Numeric = new(StringComparer.Ordinal)
    {
        "float", "double", "decimal",
        "int", "long", "short", "byte", "uint", "ulong", "ushort", "sbyte",
    };

    private static readonly string[] Axes = { "X", "Y", "Z" };
    private static readonly string[][] Triples =
    {
        new[] { "Pitch", "Yaw", "Roll" },
        new[] { "PitchDegrees", "YawDegrees", "RollDegrees" },
        new[] { "RotPitch", "RotYaw", "RotRoll" },
        new[] { "LateralOffset", "VerticalOffset", "ForwardOffset" },
        new[] { "OffsetSide", "OffsetUp", "OffsetForward" },
        new[] { "SideMeters", "DownMeters", "ForwardMeters" },
    };

    private static void Sweep(Harness t, string repoRoot)
    {
        List<(string Sec, string Key)> all = ReadDefaults(repoRoot);

        // (a) A KEY THAT ENDS IN AN AXIS LETTER IS A COORDINATE. It must reach the unit table —
        //     falling through to the magnitude fallback is exactly what shipped GloveOffsetX at
        //     0.05 mm, and nobody can see it from inside the headset.
        t.Case("configsteps/sweep-axis-suffixed-keys-resolve");
        int axisKeys = 0;
        foreach (var (sec, key) in all)
        {
            string stem = Stem(key);
            if (stem.Length < 2 || Array.IndexOf(Axes, stem.Substring(stem.Length - 1)) < 0)
                continue;
            axisKeys++;
            t.True(ConfigSteps.TryUnit(key, out _, out ConfigSteps.UnitScope sc)
                   && sc == ConfigSteps.UnitScope.Component,
                   $"[{sec}] {key} ends in an axis letter but does not resolve as a pose component "
                   + "— it will step off its own default's magnitude, which is the defect this "
                   + "guard exists for");
        }
        // The floor was 15 against 17 found when this guard was written. It dropped to 10 in the
        // very same build: the arm-HUD round renamed its six `{style}OffsetX/Y/Z` keys to end in
        // the unit word (`PalmSideOffset` and friends) and retired the six dead `[WorldUI]
        // WristHud*` twins — i.e. seven axis-suffixed keys legitimately stopped existing. That is
        // the guard doing its job, not a false alarm: it noticed the annotation set move on the
        // first run after the merge, which is exactly the failure it was built to catch, and the
        // only correct response was to look at WHY the count fell and confirm each loss. It fell
        // again to 7 in the 2026-08 dead-settings sweep, which DELETED the retired
        // `WristHudOffsetX/Y/Z` binds (and their Defaults annotations) outright — another
        // confirmed, legitimate loss. It fell to 4 on 2026-08-25, when the whole [RoundButtons]
        // section retired with the turn-flow cap group it sized and took OffsetX/OffsetY/OffsetZ
        // with it — a THIRD confirmed loss, and each of the three was checked the way this guard
        // asks: look at WHY the count fell, and confirm each key individually. Re-based to 3
        // against the 4 that remain, so a silent loss of the sweep still fails loudly.
        t.True(axisKeys >= 3,
               $"the sweep found only {axisKeys} axis-suffixed keys; it used to find 4, so either "
               + "the Defaults annotations moved or this guard has stopped reading them");

        // (b) TWO DIALS OF ONE VECTOR MUST STEP ALIKE. That invariant is what the user notices when
        //     it breaks, and it breaks silently: the coarse axis simply feels like the only one
        //     that works.
        t.Case("configsteps/sweep-siblings-agree");
        int groups = 0;
        foreach (var group in Families(all))
        {
            groups++;
            double first = 0d;
            ConfigSteps.UnitScope firstScope = ConfigSteps.UnitScope.Value;
            bool have = false;
            foreach (var (sec, key) in group.Members)
            {
                bool ok = ConfigSteps.TryUnit(key, out double s, out ConfigSteps.UnitScope sc);
                t.True(ok, $"[{sec}] {key} is one component of {group.Name} and must resolve");
                if (!ok)
                    continue;
                if (!have) { first = s; firstScope = sc; have = true; continue; }
                t.Equal(first, s, $"[{sec}] {key} must step exactly as its siblings in {group.Name}");
                t.Equal(firstScope.ToString(), sc.ToString(),
                        $"[{sec}] {key} must carry the same scope as its siblings in {group.Name}");
            }
        }
        t.True(groups >= 15,
               $"the sweep found only {groups} sibling groups; it used to find more, so either the "
               + "Defaults annotations moved or this guard has stopped reading them");
    }

    private sealed class Family
    {
        public string Name = string.Empty;
        public List<(string Sec, string Key)> Members = new();
    }

    /// <summary>Group the shipped keys into "one vector": same section, same stem, same variant
    /// tag, differing only in the axis letter or the euler word.</summary>
    private static IEnumerable<Family> Families(List<(string Sec, string Key)> all)
    {
        var byName = new Dictionary<string, Family>(StringComparer.Ordinal);

        void Add(string name, string sec, string key)
        {
            if (!byName.TryGetValue(name, out Family? f))
                byName[name] = f = new Family { Name = name };
            f.Members.Add((sec, key));
        }

        foreach (var (sec, key) in all)
        {
            string stem = Stem(key);
            string tag = key.Substring(stem.Length);

            string last = stem.Length > 1 ? stem.Substring(stem.Length - 1) : string.Empty;
            if (Array.IndexOf(Axes, last) >= 0)
                Add($"[{sec}] {stem.Substring(0, stem.Length - 1)}<axis>{tag}", sec, key);

            foreach (string[] triple in Triples)
                foreach (string member in triple)
                    if (stem.Length > member.Length && stem.EndsWith(member, StringComparison.Ordinal))
                        Add($"[{sec}] {stem.Substring(0, stem.Length - member.Length)}"
                            + $"<{triple[0]}>{tag}", sec, key);
        }

        foreach (Family f in byName.Values)
            if (f.Members.Count > 1)
                yield return f;
    }

    private static string Stem(string key)
    {
        int i = key.LastIndexOf('_');
        return i > 0 ? key.Substring(0, i) : key;
    }

    // =============================================================================================
    //  3. The written-down steps still name entries that exist
    // =============================================================================================

    /// <summary>
    /// `ConfigSteps.Explicit` is the one place a step is a JUDGEMENT rather than a derivation, and a
    /// key that is renamed or retired out from under it fails silently in the worst possible way:
    /// the row keeps working and quietly reverts to the derived step. That is precisely how the
    /// world tilt came to move in hundredths of a degree.
    /// </summary>
    private static void ExplicitDrift(Harness t, string repoRoot)
    {
        t.Case("configsteps/explicit-table-has-not-drifted");
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (sec, key) in ReadDefaults(repoRoot))
            all.Add(sec + "/" + key);

        int n = 0;
        foreach (string key in ConfigSteps.ExplicitKeys)
        {
            n++;
            t.True(all.Contains(key),
                   $"the written-down step for '{key}' names a config entry that no longer ships a "
                   + "default — either the key was renamed (and every player's tuned value with it) "
                   + "or the row is gone and the table entry is dead");
        }
        t.True(n >= 10, $"only {n} written-down steps found — the Explicit table has been gutted");
    }

    // =============================================================================================
    //  4. THE RULE — the property every dial in the mod has to satisfy
    // =============================================================================================

    /// <summary>
    /// A shipped default with its declared TYPE and its VALUE — everything the rule needs to know
    /// about an entry except its declared range, which lives at the bind site.
    /// </summary>
    private readonly struct Dial
    {
        internal Dial(string sec, string key, string type, double magnitude)
        {
            Sec = sec; Key = key; Type = type; Magnitude = magnitude;
        }

        internal readonly string Sec;
        internal readonly string Key;
        internal readonly string Type;

        /// <summary>|value|, or the largest |component| for a vector — never the euclidean length,
        /// which would make a diagonal offset read as larger than either of its axes.</summary>
        internal readonly double Magnitude;

        internal string Id => Sec + "/" + Key;
        internal bool Integral => Integrals.Contains(Type);
        internal bool IsVector => Type.StartsWith("Vector", StringComparison.Ordinal);
    }

    private static readonly HashSet<string> Integrals = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "uint", "ulong", "ushort", "sbyte",
    };

    /// <summary>The types that get a numeric row: a scalar, or a vector edited component-wise.</summary>
    private static readonly HashSet<string> Stepped = new(StringComparer.Ordinal)
    {
        "float", "double", "decimal",
        "int", "long", "short", "byte", "uint", "ulong", "ushort", "sbyte",
        "Vector2", "Vector3", "Vector4",
    };

    /// <summary>A numeric literal as C# spells it, suffix and all.</summary>
    private static readonly Regex Literal =
        new(@"^[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?[fdmFDM]?$", RegexOptions.Compiled);

    private static readonly Regex VectorLiteral =
        new(@"new\s+Vector[234]\s*\(([^)]*)\)", RegexOptions.Compiled);

    private static readonly Regex Range =
        new(@"AcceptableValueRange<[A-Za-z0-9_]+>\s*\(\s*([^,()]+?)\s*,\s*([^,()]+?)\s*\)",
            RegexOptions.Compiled);

    /// <summary>
    /// THE RULE, run over every dial the mod ships. <c>ConfigSteps</c> states it in words at the top
    /// of its own file; this is the same sentence as an assertion:
    ///
    /// <para><b>one press is at most a quarter of the dial's own scale and at least a
    /// two-hundred-and-fiftieth of it, where the scale is the declared range if there is one and
    /// otherwise the largest magnitude in the dial's FAMILY.</b></para>
    ///
    /// <para>WHY THIS AND NOT MORE HAND-WRITTEN VECTORS. Every one of the four reports in this
    /// family arrived as "dial X has no effect", from a step nobody had looked at, on a key nobody
    /// had thought about — the asset rotation, GloveOffsetX, [RoundButtons] OffsetX, and now
    /// [Cards] SlotOverlayOffset/Spacing. A list of keys somebody remembered cannot catch the fifth.
    /// This runs the REAL resolver (<see cref="ConfigSteps.Resolve"/> is free of Unity and BepInEx,
    /// so it links in whole) against the REAL shipped defaults, and it fails on the next dial
    /// whoever adds it, whether or not they thought about stepping.</para>
    ///
    /// <para>WHAT IT CANNOT SEE, said plainly. The declared range lives at the <c>Bind</c> call, not
    /// in <c>Defaults/</c>, so it is read here by a LITERAL-ONLY parse — a range whose ends are
    /// named constants (<c>MapTravelConfirm.OffsetLimitX</c>) is simply not found, and that dial is
    /// then checked against its family magnitude instead. That is the conservative direction:
    /// magnitudes in this config are never much wider than the ranges around them, and it was
    /// verified on the day this was written that every shipped dial passes either way. If a future
    /// entry fails here and its range is a constant, THAT is the thing to check first.</para>
    /// </summary>
    private static void Rule(Harness t, string repoRoot)
    {
        List<Dial> dials = ReadDials(repoRoot);
        Dictionary<string, (double Min, double Max)> ranges = ReadRanges(repoRoot, dials);
        Dictionary<string, double> scale = FamilyScale(dials, ranges);

        double ScaleOf(Dial d) => scale[ConfigSteps.FamilyOf(d.Sec, d.Key)];

        double StepOf(Dial d) => ConfigSteps.Resolve(d.Sec, d.Key, ScaleOf(d), d.Integral);

        // ---- (a) the two bounds --------------------------------------------------------------
        // Exempt: a WRITTEN-DOWN step (that table is where a human overrules the rule on purpose,
        // with the argument beside it — [WorldUI] ScreenParallaxScale is 590 presses across its
        // range and says why), and an INTEGRAL dial (1 is the grid an integer has; there is no
        // finer step to give an MSAA level of 4).
        t.Case("configsteps/rule-bounds");
        var written = new HashSet<string>(ConfigSteps.ExplicitKeys, StringComparer.Ordinal);
        int checked_ = 0;
        foreach (Dial d in dials)
        {
            if (written.Contains(d.Id) || d.Integral)
                continue;
            double s = ScaleOf(d);
            if (s <= 0d)
                continue;              // a family that ships all-zero says nothing about resolution
            checked_++;
            double step = StepOf(d);
            t.True(step <= s / ConfigSteps.CoarsestPresses + 1e-12,
                   $"[{d.Sec}] {d.Key} steps {step} against a scale of {s} — that is "
                   + $"{s / step:0.#} presses to cross the dial, and THE RULE's ceiling is "
                   + $"{ConfigSteps.CoarsestPresses}. A step this coarse cannot land on the value "
                   + "the player is aiming at; it is what [Cards] SlotOverlaySpacing_Steel did when "
                   + "one press was five times the whole value");
            t.True(step >= s / ConfigSteps.FinestPresses - 1e-12,
                   $"[{d.Sec}] {d.Key} steps {step} against a scale of {s} — that is "
                   + $"{s / step:0.} presses to cross the dial, past THE RULE's floor of "
                   + $"{ConfigSteps.FinestPresses}. A step too FINE is a defect too: nobody crosses "
                   + "a dial in a headset that way. Write the step down in ConfigSteps.Explicit if "
                   + "the resolution is genuinely worth the presses, and say why");
        }
        t.True(checked_ >= 300,
               $"THE RULE was checked against only {checked_} dials; it used to reach 330-odd, so "
               + "either the Defaults annotations moved or this guard has stopped reading them");

        // ---- (b) one vector, one step ----------------------------------------------------------
        // The sibling sweep above asserts that two axes of one pose agree on the UNIT. This asserts
        // they agree on the STEP THAT SHIPS, which is the thing the player feels and the thing that
        // broke: GloveOffsetX moved 0.05 mm beside a GloveOffsetY that moved 1 mm, from the same
        // unit row, because each was bounded by its OWN magnitude.
        t.Case("configsteps/rule-one-vector-one-step");
        var byFamily = new Dictionary<string, List<Dial>>(StringComparer.Ordinal);
        foreach (Dial d in dials)
        {
            string f = ConfigSteps.FamilyOf(d.Sec, d.Key);
            if (!byFamily.TryGetValue(f, out List<Dial>? members))
                byFamily[f] = members = new List<Dial>();
            members.Add(d);
        }
        int families = 0;
        foreach (KeyValuePair<string, List<Dial>> kv in byFamily)
        {
            if (kv.Value.Count < 2)
                continue;
            families++;
            double first = StepOf(kv.Value[0]);
            for (int i = 1; i < kv.Value.Count; i++)
            {
                Dial d = kv.Value[i];
                if (written.Contains(d.Id) || written.Contains(kv.Value[0].Id))
                    continue;         // a written-down step is a deliberate exception, by definition
                t.Equal(first, StepOf(d),
                        $"[{d.Sec}] {d.Key} must step exactly as the rest of {kv.Key} — two dials of "
                        + "one vector that move by different amounts read as one that works and one "
                        + "that does not");
            }
        }
        t.True(families >= 40,
               $"only {families} step families found; the family grouping has stopped seeing the "
               + "shipped keys");

        // ---- (c) THE USER'S OWN GRID -----------------------------------------------------------
        // Every non-zero component of every Vector dial in this mod is board-local geometry he has
        // hand-tuned, and on 2026-08 all 63 of them sat on a ONE MILLIMETRE grid while the arrows
        // moved 5 mm — 25 of the 63 were values his own arrows could not produce. That is the
        // report ("ich überspringe den optimalen Punkt immer") as an arithmetic fact, so it is
        // asserted as one. Epsilon seeds are excluded: this config writes 1e-10 / 2e-09 to mark an
        // explicit zero, and a marker is not tuning (ConfigSteps.ZeroMagnitude is that threshold,
        // and the resolver now agrees with it — see the note on the canary below).
        //
        // TWO COUNTS, BECAUSE THEY MEASURE DIFFERENT THINGS. `tuned` is what the assertion is
        // about. `parsed` is the canary — "is this guard still reading the Defaults at all?" — and
        // until 2026-08-26 the canary WAS the tuned count, floored at 55. That made a guard about
        // the PARSER a function of HOW MUCH THE USER HAS TUNED: when his cfg drop legitimately
        // zeroed five offset families the tuned count fell 63 -> 44 and the canary reported a
        // parser failure that had not happened. A count of non-zero values cannot answer "did the
        // parse work"; the number of components the parser PRODUCED can, whatever they are, so
        // that is what is floored now. Zeroing every vector in the mod would leave `parsed` at 180
        // and simply give the assertion nothing to say, which is the correct outcome.
        t.Case("configsteps/rule-vector-defaults-are-reachable");
        int parsed = 0, tuned = 0;
        foreach (Dial d in dials)
        {
            if (!d.IsVector)
                continue;
            double step = StepOf(d);
            foreach (double v in VectorComponents(repoRoot, d))
            {
                parsed++;
                if (Math.Abs(v) <= ConfigSteps.ZeroMagnitude)
                    continue;
                tuned++;
                double n = Math.Abs(v) / step;
                t.True(Math.Abs(n - Math.Round(n)) <= 1e-6d,
                       $"[{d.Sec}] {d.Key} ships a component of {v} that its own {step} arrows "
                       + "cannot produce — the player cannot return to the value the mod shipped, "
                       + "let alone stop on the one he wants");
            }
        }
        t.True(parsed >= 150,
               $"only {parsed} Vector components parsed out of the shipped defaults (180 on "
               + "2026-08-26, across 61 Vector2/Vector3 dials); the Vector literals have changed "
               + $"shape and this guard is no longer reading them — it saw {tuned} tuned ones");

        // ---- (d) THE DIALS IN THE REPORT -------------------------------------------------------
        // Pinned by name, against the real resolver, because a property test can be satisfied by a
        // rule that is right in general and wrong on the case that was reported. These four are the
        // ones the user was holding when he wrote in, and the numbers are what he will feel.
        t.Case("configsteps/rule-the-reported-dials");
        foreach (string board in new[] { "Oak", "Steel", "Bronze" })
        {
            Dial offset = Find(dials, "Cards", "SlotOverlayOffset_" + board);
            Dial spacing = Find(dials, "Cards", "SlotOverlaySpacing_" + board);
            t.Equal(0.001d, StepOf(offset),
                    $"[Cards] SlotOverlayOffset_{board} must step ONE MILLIMETRE — it shipped 5 mm "
                    + "on a family whose largest tuned component is 18 mm, and the values he holds "
                    + "(2, 3, 4, 18 mm) are not on a 5 mm grid at all");
            t.Equal(0.001d, StepOf(spacing),
                    $"[Cards] SlotOverlaySpacing_{board} must step ONE MILLIMETRE — it shipped 0.01 "
                    + "against a Steel value of 0.002, a press five times the whole dial");
        }
        // THE GloveOffsetX/Y REPORT, under the names it ships as today. Those two keys are gone —
        // the arm-HUD round renamed the six {style}OffsetX/Y/Z to {style}Palm{Side,Lift,Finger}
        // Offset — but the SHAPE is intact and still on the glove: GlovePalmSideOffset ships at
        // exactly 0 beside a GlovePalmFingerOffset of −0.05, one axis at its origin next to one
        // that is tuned. Deriving from each axis's own magnitude is what made X move 0.05 mm beside
        // a Y that moved 1 mm; the family bound is what stops it, and this asserts it still does.
        t.Equal(StepOf(Find(dials, "WristHud", "GlovePalmSideOffset")),
                StepOf(Find(dials, "WristHud", "GlovePalmFingerOffset")),
                "[WristHud] GlovePalmSideOffset and GlovePalmFingerOffset are two axes of one plate "
                + "and must move together — this is the report the UnitScope enum was written for, "
                + "and the family bound must not have quietly undone it");
        t.Equal(StepOf(Find(dials, "WristHud", "GlovePalmSideOffset")),
                StepOf(Find(dials, "WristHud", "GlovePalmLiftOffset")),
                "…and so must the third axis");
    }

    /// <summary>The one dial with this section and key, or a failure that says which one is missing
    /// — a silently absent key would turn a named assertion into a no-op.</summary>
    private static Dial Find(List<Dial> dials, string sec, string key)
    {
        foreach (Dial d in dials)
            if (string.Equals(d.Sec, sec, StringComparison.Ordinal)
                && string.Equals(d.Key, key, StringComparison.Ordinal))
                return d;
        throw new InvalidOperationException(
            $"the stepper guard names [{sec}] {key}, which no longer ships a Defaults line. Either "
            + "it was renamed (and every player's tuned value with it) or the assertion is dead.");
    }

    /// <summary>
    /// ONE SCALE PER <see cref="ConfigSteps.FamilyOf"/> FAMILY: the largest of its members' own
    /// scales, where a member's own scale is its declared range's width or — with no range — its
    /// shipped magnitude. The mirror of <c>ConfigCatalog.OwnScale</c>, and it must stay the mirror,
    /// or this guard checks a different question from the one the menu answers.
    ///
    /// <para>SINCE 2026-08-26 IT IS THE MIRROR BY CONSTRUCTION and no longer by hand: the
    /// per-entry arithmetic is <see cref="ConfigSteps.OwnScale"/>, which the catalog calls too.
    /// The two copies WERE faithful — that is why this guard caught the epsilon-zero collapse the
    /// day the user's cfg drop landed — but a mirror kept by hand is one that eventually is not,
    /// and the failure mode is silent: the guard goes green while the menu misbehaves. All this
    /// still owns is the loop, because only the two callers know what a "member" is (a ConfigItem
    /// there, a parsed Defaults line here).</para>
    /// </summary>
    private static Dictionary<string, double> FamilyScale(
        List<Dial> dials, Dictionary<string, (double Min, double Max)> ranges)
    {
        var scale = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Dial d in dials)
        {
            bool hasRange = ranges.TryGetValue(d.Id, out (double Min, double Max) r);
            double own = ConfigSteps.OwnScale(hasRange, r.Min, r.Max, d.Magnitude);
            string f = ConfigSteps.FamilyOf(d.Sec, d.Key);
            if (!scale.TryGetValue(f, out double best) || own > best)
                scale[f] = own;
        }
        return scale;
    }

    private static List<Dial> ReadDials(string repoRoot)
    {
        var dials = new List<Dial>();
        foreach ((string sec, string key, string type, string value) in AnnotatedDefaults(repoRoot))
        {
            if (!Stepped.Contains(type))
                continue;
            dials.Add(new Dial(sec, key, type, MagnitudeOf(type, value)));
        }
        if (dials.Count < 320)
            throw new InvalidOperationException(
                $"THE RULE guard read only {dials.Count} annotated stepped defaults (expected ~390). "
                + "The `// => [Section] Key` annotation has changed shape; the sweep would silently "
                + "pass on almost nothing.");
        return dials;
    }

    /// <summary>The raw right-hand sides of one vector default, for the reachability check.</summary>
    private static IEnumerable<double> VectorComponents(string repoRoot, Dial d)
    {
        foreach ((string sec, string key, string type, string value) in AnnotatedDefaults(repoRoot))
        {
            if (!string.Equals(sec, d.Sec, StringComparison.Ordinal)
                || !string.Equals(key, d.Key, StringComparison.Ordinal))
                continue;
            Match m = VectorLiteral.Match(value);
            if (!m.Success)
                yield break;
            foreach (string part in m.Groups[1].Value.Split(','))
                if (TryLiteral(part, out double v))
                    yield return v;
            yield break;
        }
    }

    private static double MagnitudeOf(string type, string value)
    {
        Match m = VectorLiteral.Match(value);
        if (m.Success)
        {
            double best = 0d;
            foreach (string part in m.Groups[1].Value.Split(','))
                if (TryLiteral(part, out double v) && Math.Abs(v) > best)
                    best = Math.Abs(v);
            return best;
        }
        return TryLiteral(value, out double s) ? Math.Abs(s) : 0d;
    }

    private static bool TryLiteral(string raw, out double value)
    {
        value = 0d;
        string s = raw.Trim().TrimEnd(';').Trim();
        if (!Literal.IsMatch(s))
            return false;
        return double.TryParse(s.TrimEnd('f', 'd', 'm', 'F', 'D', 'M'),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static List<(string Sec, string Key, string Type, string Value)>? _annotated;

    /// <summary>Every `// =&gt; [Section] Key` annotation in Defaults/, with its type and value.</summary>
    private static List<(string Sec, string Key, string Type, string Value)> AnnotatedDefaults(string repoRoot)
    {
        if (_annotated != null)
            return _annotated;

        var found = new List<(string, string, string, string)>();
        string dir = Path.Combine(repoRoot, "src", "GloomhavenVR", "Defaults");
        if (!Directory.Exists(dir))
            throw new InvalidOperationException(
                $"the stepper guard reads the shipped defaults from {dir}, which does not exist. "
                + "If Defaults/ moved, re-point this — otherwise the sweep silently tests nothing.");

        foreach (string file in Directory.GetFiles(dir, "*.cs"))
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                Match m = Annotated.Match(line);
                if (m.Success)
                    found.Add((m.Groups["sec"].Value, m.Groups["key"].Value,
                               m.Groups["type"].Value, m.Groups["val"].Value));
            }

        return _annotated = found;
    }

    /// <summary>
    /// Declared ranges, read off the <c>Bind</c> call sites — LITERAL ENDS ONLY.
    ///
    /// <para>A range whose ends are named constants is deliberately not resolved: doing so would
    /// mean evaluating arbitrary C# from a test, and the fallback (check the dial against its family
    /// magnitude instead) is the conservative direction. See <see cref="Rule"/> for the note.</para>
    ///
    /// <para>An interpolated key is a PATTERN, not a prefix: <c>$"{style}LateralOffset"</c> starts
    /// with its hole, so matching by prefix would claim one range for every key in the section. It
    /// is anchored and the holes become wildcards.</para>
    /// </summary>
    private static Dictionary<string, (double Min, double Max)> ReadRanges(string repoRoot, List<Dial> dials)
    {
        var found = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        string dir = Path.Combine(repoRoot, "src", "GloomhavenVR");
        if (!Directory.Exists(dir))
            return found;

        foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            int at = 0;
            while ((at = text.IndexOf(".Bind", at, StringComparison.Ordinal)) >= 0)
            {
                int open = at + ".Bind".Length;
                while (open < text.Length && char.IsWhiteSpace(text[open]))
                    open++;
                at = open;
                if (open >= text.Length || text[open] != '(')
                    continue;

                string args = Balanced(text, open);
                if (args.Length == 0)
                    continue;
                at = open + args.Length;

                Match r = Range.Match(args);
                if (!r.Success || !TryLiteral(r.Groups[1].Value, out double lo)
                    || !TryLiteral(r.Groups[2].Value, out double hi))
                    continue;

                Match head = Regex.Match(args, "^\\s*\"([^\"]+)\"\\s*,\\s*(\\$?)\"([^\"]*)\"");
                if (!head.Success)
                    continue;

                string sec = head.Groups[1].Value;
                string keyText = head.Groups[3].Value;
                if (head.Groups[2].Value.Length == 0)
                {
                    found[sec + "/" + keyText] = (lo, hi);
                    continue;
                }

                var pattern = new Regex("^" + Regex.Replace(Regex.Escape(keyText), @"\\\{[^}]*\\?\}",
                                                            "[A-Za-z0-9]+") + "$");
                foreach (Dial d in dials)
                    if (string.Equals(d.Sec, sec, StringComparison.Ordinal) && pattern.IsMatch(d.Key))
                        found[d.Id] = (lo, hi);
            }
        }
        return found;
    }

    /// <summary>The text inside the parenthesis at <paramref name="open"/>, brackets balanced.</summary>
    private static string Balanced(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return text.Substring(open + 1, i - open - 1);
        }
        return string.Empty;
    }

    private static List<(string Sec, string Key)> ReadDefaults(string repoRoot)
    {
        var outKeys = new List<(string, string)>();
        string dir = Path.Combine(repoRoot, "src", "GloomhavenVR", "Defaults");
        if (!Directory.Exists(dir))
            throw new InvalidOperationException(
                $"the stepper guard reads the shipped defaults from {dir}, which does not exist. "
                + "If Defaults/ moved, re-point this — otherwise the sweep silently tests nothing.");

        foreach (string file in Directory.GetFiles(dir, "*.cs"))
            foreach (string line in File.ReadAllLines(file))
            {
                // Skip the file header, which SPELLS the annotation out as an example.
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                Match m = Annotated.Match(line);
                if (m.Success && Numeric.Contains(m.Groups["type"].Value))
                    outKeys.Add((m.Groups["sec"].Value, m.Groups["key"].Value));
            }

        if (outKeys.Count < 250)
            throw new InvalidOperationException(
                $"the stepper guard read only {outKeys.Count} annotated numeric defaults (expected "
                + "~340). "
                + "The `// => [Section] Key` annotation has changed shape; the sweep below would "
                + "silently pass on almost nothing.");
        return outKeys;
    }
}
