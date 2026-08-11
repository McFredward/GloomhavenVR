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
        Unit(t, "FanRadius", 0.01d, ConfigSteps.UnitScope.Value);
        Unit(t, "TrayScale", 0.05d, ConfigSteps.UnitScope.Value);

        // ---- MILLIMETRES ARE NOT METRES, and the suffix test cannot tell them apart on its own --
        // "Millimeters" ENDS IN "Meters", so with the two entries in the wrong order a 40 mm dial
        // would take the metre step of 0.01 — a hundredth of a millimetre a press, the same shape
        // of defect as the axis-letter reports below and just as invisible from inside a headset.
        // Pinned here because the only thing keeping it right is the ORDER of two rows in a table.
        t.Case("configsteps/millimetres-before-metres");
        Unit(t, "PickRadiusMillimeters", 5d, ConfigSteps.UnitScope.Value);
        Unit(t, "BoardMinWidthMeters", 0.01d, ConfigSteps.UnitScope.Value);

        // ---- the variant tag (report #1: the asset rotation at 0.01°) ---------------------------
        // The tag is stripped for the unit lookup AND remembered: a per-board default is that
        // board's geometry, so it may coarsen the step but never sharpen it.
        t.Case("configsteps/variant-tag");
        Unit(t, "RestButtonDiameter_Steel", 0.01d, ConfigSteps.UnitScope.Variant);
        Unit(t, "SlotOverlaySpacing_Bronze", 0.01d, ConfigSteps.UnitScope.Variant);
        Unit(t, "DecisionGap_Oak", 0.01d, ConfigSteps.UnitScope.Variant);
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
        Unit(t, "GloveOffsetX", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveOffsetY", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveOffsetZ", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "OffsetX", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "ConfirmUndoInsetX", 0.01d, ConfigSteps.UnitScope.Component);
        // both decorations at once
        Unit(t, "OffsetX_Oak", 0.01d, ConfigSteps.UnitScope.Component);

        // ---- a direction spelled as a word, trailing or in the middle ---------------------------
        t.Case("configsteps/direction-word");
        Unit(t, "HeldOffsetSide", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "HeldOffsetUp", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "HeldOffsetForward", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveLateralOffset", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveVerticalOffset", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "GloveForwardOffset", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "SpawnSideMeters", 0.01d, ConfigSteps.UnitScope.Component);
        Unit(t, "CombatLogRight", 0.01d, ConfigSteps.UnitScope.Component);

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
        t.Case("configsteps/bar-size-family");
        Unit(t, "BarSizeScale", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "BarZoomMinScale", 0.05d, ConfigSteps.UnitScope.Value);
        Unit(t, "BarZoomMaxScale", 0.05d, ConfigSteps.UnitScope.Value);
        t.True(ConfigSteps.TryExplicit("WorldUI", "BarSizeScale", out double barStep)
               && ConfigSteps.TryExplicit("WorldUI", "BarZoomMinScale", out double barLoStep)
               && ConfigSteps.TryExplicit("WorldUI", "BarZoomMaxScale", out double barHiStep)
               && barStep == barLoStep && barStep == barHiStep,
               "the health-bar size and both of its bounds must carry the SAME written-down step — "
               + "a bound that steps more coarsely than the value it bounds cannot be set to it");

        // ---- an angle that is NOT one axis of an orientation ------------------------------------
        // "Degrees" says the number is an angle; it does not say it is a coordinate. A sweep and a
        // threshold are single quantities and their own magnitude is a perfectly good guide, so
        // they must NOT come back as components or they lose that guide.
        t.Case("configsteps/angle-is-not-a-pose");
        Unit(t, "FanArcSweepDegrees", 1d, ConfigSteps.UnitScope.Value);
        Unit(t, "RevealEnterDegrees", 1d, ConfigSteps.UnitScope.Value);
        Unit(t, "ModalRayConeDegrees", 1d, ConfigSteps.UnitScope.Value);
        // …but a named euler axis is.
        Unit(t, "WristHudPitch", 1d, ConfigSteps.UnitScope.Component);
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
        NoUnit(t, "MapUvComponent");
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
    /// <para>The type is not decoration. Only a NUMBER gets a stepper, and the config is full of
    /// keys that look like coordinates and are not: `[WorldUI] MapTexFlipX` / `MapTexFlipY` are
    /// bools that mirror the map texture. Sweeping them would demand a step for a toggle.</para>
    /// </summary>
    private static readonly Regex Annotated =
        new(@"(?:const|static\s+readonly)\s+(?<type>[A-Za-z0-9_.]+)\s+[A-Za-z0-9_]+\s*=.*?"
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
        // only correct response was to look at WHY the count fell and confirm each loss. Re-based
        // to 8 against the 10 that remain, so a silent loss of the sweep still fails loudly.
        t.True(axisKeys >= 8,
               $"the sweep found only {axisKeys} axis-suffixed keys; it used to find 10, so either "
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
