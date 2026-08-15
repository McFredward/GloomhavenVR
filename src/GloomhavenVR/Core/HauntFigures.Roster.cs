using System.Collections.Generic;
using System.Text;
using ClockStone;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT FIGURES — WHICH CREATURES EXIST ON THIS MACHINE, what they are called, what they sound
//  like, and the ONE-TIME CENSUS that makes the next round precise instead of speculative.
// =================================================================================================

internal static partial class HauntFigures
{
    /// <summary>
    /// Resolves <c>CClass.ENPCModel</c> values — which are compile-time constants and therefore
    /// safe to name in source — to the PREFAB NAME strings the asset system actually wants, which
    /// are not.
    ///
    /// <para><b>WHY THE ENUM NAME IS NOT THE PREFAB NAME.</b> The prefab name comes from the
    /// monster YAML's <c>Models</c> list (<c>CClass.Models</c>, CClass.cs:240) and those strings are
    /// DISPLAY names that may contain spaces: <c>MonstersYML.GetNPCModelEnumFromSpaceName</c>
    /// (MonstersYML.cs:808-813) exists precisely because it has to strip spaces before it can match
    /// an enum member. The game passes the raw, spaced string to the bundle system
    /// (<c>AssetBundleManager.cs:423</c> hands it <c>Models[ChosenModelIndex]</c> verbatim), and the
    /// addressable PATH is built from it verbatim too (:300). So the only way to know what a
    /// creature's prefab is actually called is to ask the running game — which is what this does,
    /// and which is why the census below exists at all.</para>
    ///
    /// <para><b>BASE GAME ONLY, AND THAT IS A CORRECTNESS REQUIREMENT RATHER THAN CAUTION.</b>
    /// <c>BundleConfigForPrefab</c> refuses a DLC model the player does not own
    /// (AssetBundleManager.cs:356-361, checking <c>PlatformLayer.DLC.UserInstalledDLC</c>), so DLC
    /// ownership is a PER-CLIENT fact. If a DLC creature were ever castable, two players in the same
    /// scenario — one who owns Jaws of the Lion and one who does not — would resolve different
    /// rosters, and the shared-clock hash would then pick a different creature for each of them, or
    /// pick one that only exists for one of them. The user's requirement is that every player sees
    /// the SAME thing in the same place at the same second, and a roster filtered to
    /// <c>BundleDLC == EDLCKey.None</c> is the only construction that keeps it. DLC is therefore not
    /// "handled"; it is excluded, and that exclusion is what makes the guarantee hold.</para>
    ///
    /// <para><b>THE PROBE IS NARROW ON PURPOSE.</b> <c>BundleConfigForPrefab</c> emits a Unity
    /// <c>Debug.LogErrorFormat</c> for every prefab name it does not recognise (:354). Walking all
    /// ~230 enum members through it would therefore write a wall of red into the game's own log on
    /// first activation. Only the models the four cast lists actually name are ever probed — seven
    /// of them — and each is probed at most once per session.</para>
    /// </summary>
    private static class Roster
    {
        /// <summary>Resolved enum → prefab name. Empty until <see cref="Ready"/> has run.</summary>
        private static readonly Dictionary<CClass.ENPCModel, string> Names = new(8);

        private static bool _resolved;
        private static bool _censused;

        internal static void Forget()
        {
            Names.Clear();
            _resolved = false;
            // The census is NOT reset: it is a once-per-process diagnostic, and repeating it on
            // every mixed-reality toggle would bury the log line it exists to produce.
        }

        /// <summary>
        /// Resolve the roster if it has not been resolved yet, and answer whether this room can be
        /// haunted by real figures at all. False means the caller must NOT publish the suppression
        /// mask — the shader apparitions have to keep the room.
        /// </summary>
        internal static bool Ready(SkyStyle style)
        {
            Resolve();
            for (int c = 0; c < Haunt.EventCount; c++)
                if (IsMineByDesign(style, c) && CanCast(EventFor(style, c).Cast))
                    return true;
            return false;
        }

        /// <summary>True when at least one creature of this cast resolved on this machine.</summary>
        internal static bool CanCast(CClass.ENPCModel[] cast)
        {
            Resolve();
            for (int i = 0; i < cast.Length; i++)
                if (Names.ContainsKey(cast[i]))
                    return true;
            return false;
        }

        // =========================================================================================
        //  WHICH CREATURE — A SHUFFLED ITERATION, NOT A DRAW.
        //
        //  USER REQUEST, verbatim: "Geb mir mehr Varianz bei den Figuren, jedes Mal wenn eine Figur
        //  angezeigt wird sollte das eine andere sein (durch einige ausgewählt random
        //  durchiterieren). Im MP sollten aber immer beide die gleiche sehen, nie eine andere."
        //
        //  WHAT IT USED TO BE AND WHY HE NOTICED. The creature was `hash(slot, channel 9)` scaled
        //  onto the cast, i.e. an INDEPENDENT DRAW from three: the chance that any apparition repeats
        //  the one before it is 1 in 3, and over ten apparitions a repeat is all but certain. That is
        //  what "jedes Mal eine andere" is a complaint about. An independent draw also clusters — it
        //  has no memory, so it cannot know it has just shown a Cultist twice.
        //
        //  WHAT IT IS NOW. The cast is PERMUTED per block of n APPARITIONS and the apparition's
        //  position inside its block indexes that permutation, so each creature appears exactly once
        //  per n consecutive apparitions — "durchiterieren" in the literal sense — and the ORDER of
        //  each pass is reshuffled. Across a block boundary the first element of the new permutation
        //  is compared against the last element of the previous one and swapped away from it, so the
        //  no-two-in-a-row property holds THROUGH the seam and not merely inside a block.
        //
        //  IT IS INDEXED BY THE APPARITION ORDINAL AND NOT BY THE SLOT, and that distinction is the
        //  difference between fixing this and only appearing to. Apparitions are not consecutive
        //  slots: the schedule's group partition puts each room's two figure cards on about one slot
        //  in three, so successive apparitions are typically THREE slots apart — and three is exactly
        //  the cast size, i.e. the same position of the next permutation block. Indexed by the slot,
        //  the shuffle measures out at a 28-35% repeat rate, which is no better than the independent
        //  draw it replaces (simulated over 4000 slots in both rooms). Indexed by the ordinal
        //  HauntFigures.ApparitionOrdinal computes — the count of slots this room's figure cards own,
        //  which is a pure function of the slot — it is EXACTLY 0%.
        //
        //  WHY IT CANNOT DIVERGE BETWEEN TWO CLIENTS, which is the standing ruling and not a
        //  nice-to-have. Three properties, and all three are structural:
        //    1. IT IS A PURE FUNCTION OF THE SLOT INDEX. The only inputs are the slot (an exact
        //       integer floor(sharedClock / 83) — SkyAlternative's shared environment epoch), the
        //       cast array (a compile-time constant) and Haunt.Hash, which is the same
        //       multiply/add/frac cascade the GPU and the bake already run bit-for-bit
        //       (Haunt.Schedule.cs's header explains why it may contain no sin()). No Random, no
        //       Time.time, no per-client seed. The ordinal is a pure function of the slot for the
        //       same reason — it counts CARDS, which the partition decides, and deliberately NOT
        //       which of them were live, because liveness reads the frequency dial and a dial is the
        //       one shared value that can differ between two peers for the frame it is being moved.
        //    2. THERE IS NO "LAST SHOWN" STATE ANYWHERE. The anti-repeat rule is expressed as a
        //       comparison against the PREVIOUS BLOCK'S PERMUTATION, which is recomputed from the
        //       block index — not remembered. That is the whole reason it is written this way: a
        //       remembered "last creature" would differ between a client that has been in the room
        //       for ten minutes and one that joined thirty seconds ago, and the two would then walk
        //       different sequences forever. Nothing here has a history to disagree about.
        //    3. THE ROSTER IS THE SAME ON BOTH MACHINES. DLC is excluded by construction (see the
        //       class doc), so the availability filter below cannot answer differently for two
        //       players in the same scenario.
        //  And it is still ZERO WIRE BYTES: nothing about the pick is sent, because nothing about it
        //  is state.
        //
        //  THE ONE RESIDUAL, STATED HONESTLY AND MEASURED. The ordinal counts every slot this room's
        //  figure cards OWN, and the frequency dial then makes only some of them live — so at a dial
        //  below 1 the player's own sequence skips entries of a sequence that is itself repeat-free.
        //  Simulated over 4000 slots with the permutation exactly as implemented below:
        //        dial 1.00 -> 0.0% repeats (cellar and forest)
        //        dial 0.60 -> 15% / 14%
        //        dial 0.35 -> 23% / 25%     (the independent draw it replaces: 28-35% at every dial)
        //  Closing that last gap means counting LIVE slots instead of owned ones, i.e. reading the
        //  frequency dial, i.e. giving up property 1 above. That trade is refused deliberately: a
        //  player on a low dial seeing the same creature twice in ten minutes is a disappointment,
        //  and two players seeing different creatures is a broken promise.
        // =========================================================================================

        /// <summary>Hash channel for the shuffle. 9 was the old creature draw and 10 is the walk
        /// direction; 11 is this feature's third and last, and nothing else in the mod reads it
        /// (2 is EnvSound's, 13 is the rat's, the rest are the schedule's own).</summary>
        private const float ShuffleChannel = 11f;

        /// <summary>The largest cast this can permute. Every cast in the feature is 3; the arrays are
        /// fixed so the pick allocates nothing, and a longer cast would be truncated rather than
        /// throwing — which is the correct failure for a decoration.</summary>
        private const int MaxCast = 8;

        private static readonly CClass.ENPCModel[] Canon = new CClass.ENPCModel[MaxCast];
        private static readonly int[] PermThis = new int[MaxCast];
        private static readonly int[] PermPrev = new int[MaxCast];

        /// <summary>
        /// Choose this slot's creature: element <c>slot mod n</c> of a permutation of the cast that
        /// is reshuffled every <c>n</c> slots. See the block above for the design and for the
        /// multiplayer argument.
        ///
        /// <para><b>THE CAST IS CANONICALISED BEFORE IT IS PERMUTED, and that is what makes the
        /// guarantee hold ACROSS EVENTS as well as within one.</b> The cellar's two events name the
        /// same three creatures in different orders (window: Cultist, Corpse, Bones; stair: Cultist,
        /// Bones, Corpse) and consecutive slots in the cellar are different CARDS by construction
        /// (Haunt.Resolve's group partition). Permuting the arrays as given would mean permuting two
        /// different index spaces, so position 0 of one and position 1 of the other could name the
        /// same creature. Sorting by the enum value first means any two events with the same cast SET
        /// walk the same sequence, and the no-two-in-a-row property survives the card changing under
        /// it. The forest's two casts are disjoint, so they cannot collide either way.</para>
        ///
        /// <para>A machine on which only one of the three resolves shows that one every time, which
        /// is correct behaviour and not a degradation — the walk below falls forward through the
        /// permutation until it finds something it can actually load.</para>
        /// </summary>
        /// <param name="sequence">The APPARITION ORDINAL, not the slot — see the block above for why
        /// the difference is the whole fix. An exact non-negative integer that advances by exactly 1
        /// between two consecutive apparitions of this room.</param>
        internal static string Pick(CClass.ENPCModel[] cast, float sequence, out CClass.ENPCModel picked)
        {
            Resolve();
            picked = CClass.ENPCModel.None;
            int n = Mathf.Min(cast.Length, MaxCast);
            if (n == 0)
                return string.Empty;

            // CANONICAL ORDER: ascending enum value. Insertion sort — n is 3.
            for (int i = 0; i < n; i++)
            {
                CClass.ENPCModel m = cast[i];
                int j = i - 1;
                while (j >= 0 && (int)Canon[j] > (int)m)
                {
                    Canon[j + 1] = Canon[j];
                    j--;
                }
                Canon[j + 1] = m;
            }

            float index = Mathf.Max(Mathf.Floor(sequence), 0f);
            float block = Mathf.Floor(index / n);
            int pos = Mathf.Clamp((int)(index - block * n), 0, n - 1);

            // A CAST OF ONE OR TWO IS NOT SHUFFLED. One has nothing to vary; two can only ALTERNATE,
            // because there is exactly one repeat-free sequence over two elements — and going through
            // the general path below
            // would be wrong rather than merely pointless: the seam correction swaps positions 0 and
            // 1, which at n = 2 also moves the LAST element, so correcting block b would change what
            // block b+1 has to be corrected against and the rule would have to recurse. At n >= 3 the
            // swap can never touch index n-1, which is exactly why it does not. (At n = 1 the same
            // guard also keeps the seam correction away from PermThis[1], which does not exist for
            // that cast and would otherwise be read from whatever the last call left there.)
            if (n <= 2)
            {
                for (int i = 0; i < n; i++)
                    PermThis[i] = i;
            }
            else
            {
                Permute(PermThis, n, block);

                // THE SEAM, AND IT IS APPLIED AT EVERY POSITION AND NOT ONLY AT THE FIRST. Only
                // position 0 can repeat the previous block's last element, but the CORRECTION
                // reorders the block — so a build that applied it only when pos == 0 would hand
                // position 1 a permutation the position-0 call had already swapped, and the two
                // would collide. (That is not hypothetical: it is what the first draft of this
                // method did, and a 120-slot simulation found the collisions immediately.) The
                // previous permutation is RECOMPUTED from its block index rather than remembered,
                // which is the property the whole multiplayer argument rests on. Block 0 has no
                // predecessor and needs no correction.
                if (block >= 1f)
                {
                    Permute(PermPrev, n, block - 1f);
                    if (PermThis[0] == PermPrev[n - 1])
                        (PermThis[0], PermThis[1]) = (PermThis[1], PermThis[0]);
                }
            }

            for (int i = 0; i < n; i++)
            {
                CClass.ENPCModel m = Canon[PermThis[(pos + i) % n]];
                if (!Names.TryGetValue(m, out string? name))
                    continue;
                picked = m;
                return name;
            }
            return string.Empty;
        }

        /// <summary>
        /// Fisher–Yates over <c>0..n-1</c>, with every swap partner taken from the shared schedule
        /// hash rather than from a generator. Deterministic in the only sense that matters here: the
        /// same block index gives the same permutation on every machine, for ever, with no state.
        ///
        /// <para>The hash is indexed by <c>block * MaxCast + i</c> so that two different blocks can
        /// never share a swap draw and two positions inside one block cannot either. Those indices
        /// stay exact integers in float32 for well over a century of slots at the 83 s beat.</para>
        ///
        /// <para><c>Mathf.Min</c> against <c>i</c> for the same reason <c>Haunt.Resolve</c> uses one
        /// on its card index: <c>frac()</c> is documented to be able to return exactly 0 and an edit
        /// that let it reach 1.0 would index one past the end.</para>
        /// </summary>
        private static void Permute(int[] into, int n, float block)
        {
            for (int i = 0; i < n; i++)
                into[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = Mathf.Min((int)(Haunt.Hash(block * MaxCast + i, ShuffleChannel) * (i + 1)), i);
                (into[i], into[j]) = (into[j], into[i]);
            }
        }

        internal static string Describe(CClass.ENPCModel[] cast)
        {
            var sb = new StringBuilder(64);
            for (int i = 0; i < cast.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(cast[i]);
                sb.Append(Names.ContainsKey(cast[i]) ? " (available)" : " (absent)");
            }
            return sb.ToString();
        }

        // ---- resolution --------------------------------------------------------------------------

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;

            AssetBundleManager? abm = AssetBundleManager.Instance;
            List<CMonsterClass>? classes = MonsterClassManager.Classes;
            if (abm == null || abm.BundleLoadConfigs == null || classes == null || classes.Count == 0)
            {
                // MonsterClassManager.Classes is empty until MonsterClassManager.Load() has run
                // (:46-49). Inside a scenario it has; on a menu it may not have, and this feature is
                // scenario-only anyway. Nothing resolved means nothing is suppressed.
                VRLog.Info("Core", "HAUNT FIGURES roster: the game's monster class library is not loaded yet "
                                   + $"(classes {(classes == null ? "null" : classes.Count.ToString())}, "
                                   + $"AssetBundleManager {(abm == null ? "null" : "present")}), so no enemy "
                                   + "model can be resolved and the shader-drawn apparitions keep the room.");
                return;
            }

            // The union of the four cast lists — the ONLY names that get probed. See the class doc.
            var wanted = new HashSet<CClass.ENPCModel>();
            foreach (CClass.ENPCModel m in CellarWindow.Cast) wanted.Add(m);
            foreach (CClass.ENPCModel m in CellarStair.Cast) wanted.Add(m);
            foreach (CClass.ENPCModel m in ForestWatcher.Cast) wanted.Add(m);
            foreach (CClass.ENPCModel m in ForestCross.Cast) wanted.Add(m);

            var sb = new StringBuilder(512);
            sb.Append("HAUNT FIGURES roster resolved from the running game — ");

            for (int i = 0; i < classes.Count; i++)
            {
                CMonsterClass cls = classes[i];
                List<string>? models = cls?.Models;
                if (models == null)
                    continue;

                for (int j = 0; j < models.Count; j++)
                {
                    string model = models[j];
                    if (string.IsNullOrEmpty(model))
                        continue;

                    CClass.ENPCModel e;
                    try
                    {
                        e = MonstersYML.GetNPCModelEnumFromSpaceName(model);
                    }
                    catch (System.Exception)
                    {
                        // SingleOrDefault throws when a YAML name matches two enum members. Not our
                        // problem to fix; skip the name.
                        continue;
                    }
                    if (e == CClass.ENPCModel.None || !wanted.Contains(e) || Names.ContainsKey(e))
                        continue;

                    BundleLoadSettings.BundleLoadConfig? cfg;
                    try
                    {
                        // PUBLIC and null-returning. The private GetBundleLoadConfig (:295) it wraps
                        // dereferences the result with NO null check, so calling that one — or
                        // anything that calls it, such as GetCharacterPrefabFromBundle — with an
                        // unknown or unowned name is an NRE rather than a null. This is the only
                        // safe door.
                        cfg = abm.BundleConfigForPrefab(CActor.EType.Enemy, model);
                    }
                    catch (System.Exception ex)
                    {
                        sb.Append($"[{e} threw {ex.GetType().Name}] ");
                        continue;
                    }
                    if (cfg == null)
                    {
                        sb.Append($"[{e}: no bundle config] ");
                        continue;
                    }
                    if (cfg.BundleDLC != DLCRegistry.EDLCKey.None)
                    {
                        sb.Append($"[{e}: DLC {cfg.BundleDLC}, excluded] ");
                        continue;
                    }

                    Names[e] = model;
                    sb.Append($"[{e} = '{model}' bundle '{cfg.AssetBundleName}'] ");
                }
            }

            sb.Append($"— {Names.Count} of {wanted.Count} wanted models available. DLC models are excluded "
                      + "BY DESIGN: ownership is per-client, and a roster that differed between two players "
                      + "would make the shared-clock pick disagree, which is exactly the guarantee this "
                      + "feature must not break.");
            VRLog.Info("Core", sb.ToString());

            Census(abm, classes);
        }

        // ---- audio ----------------------------------------------------------------------------------

        /// <summary>
        /// The creature's own sound bank category. These names are the ONE part of the audio system
        /// that is knowable offline: <c>AudioControllerUtils.AdjustEffectsVolume</c>
        /// (AudioControllerUtils.cs:130-205) names every category the game ships, one per line, and
        /// the seven below are the ones belonging to this feature's cast.
        /// </summary>
        private static string CategoryFor(CClass.ENPCModel m) => m switch
        {
            CClass.ENPCModel.LivingBones => "LivingBones_SFX",
            CClass.ENPCModel.LivingCorpse => "LivingCorpse_SFX",
            CClass.ENPCModel.LivingSpirit => "LivingSpirit_SFX",
            CClass.ENPCModel.Cultist => "Cultist_SFX",
            CClass.ENPCModel.HighCultist => "HighCultist_SFX",
            CClass.ENPCModel.BoneRanger => "BoneRanger_SFX",
            CClass.ENPCModel.Hound => "Hound_SFX",
            _ => string.Empty,
        };

        /// <summary>Item names that carry no threat. An apparition is allowed to breathe and shift;
        /// it is not allowed to shriek, and an attack or death cry from something that is not
        /// attacking or dying is both a lie and a startle.</summary>
        private static readonly string[] VoiceWanted = { "idle", "breath", "move", "walk", "step", "foot", "vocal" };

        private static readonly string[] VoiceBanned =
            { "attack", "death", "die", "hit", "damage", "crit", "scream", "roar", "shriek", "spawn", "summon" };

        /// <summary>
        /// Play ONE quiet, distance-attenuated sound from the creature's own bank at the figure's
        /// world position.
        ///
        /// <para><b>THE POSITIONAL OVERLOAD IS USED, and that is only correct because
        /// <see cref="EnvSound"/> owns the <c>AudioListener</c>.</b> The mod normally must not play
        /// 3D audio positionally — <see cref="GameAudio"/>'s class doc records the card-fan bug
        /// where a positional item attenuated to silence because the listener was on the game's 2D
        /// camera, metres away. But <c>EnvSound.TakeListener</c> (EnvSound.cs:1056-1090) moves the
        /// listener onto the VR head for exactly the two environments this feature runs in, and it
        /// does so precisely when its own switch is on. Gating on that switch therefore satisfies
        /// two independent requirements with one condition: the standing ruling that apparition
        /// sound rides the environment-sound toggle, and the technical precondition that makes a
        /// positional play audible at all.</para>
        ///
        /// <para>The item is chosen by ORDINAL SORT and not by a hash, so it needs no shared state
        /// to stay identical between clients. Deterministic by construction is cheaper than
        /// deterministic by agreement.</para>
        /// </summary>
        internal static void Voice(CClass.ENPCModel model, Vector3 world)
        {
            if (EnvSound.Enabled == null || !EnvSound.Enabled.Value)
                return;

            string category = CategoryFor(model);
            if (category.Length == 0)
                return;

            try
            {
                AudioCategory? cat = AudioController.GetCategory(category);
                AudioItem[]? items = cat?.AudioItems;
                if (items == null || items.Length == 0)
                    return;

                string best = string.Empty;
                for (int i = 0; i < items.Length; i++)
                {
                    string? n = items[i]?.Name;
                    if (string.IsNullOrEmpty(n) || !Looks(n!, VoiceWanted) || Looks(n!, VoiceBanned))
                        continue;
                    if (best.Length == 0 || string.CompareOrdinal(n, best) < 0)
                        best = n!;
                }
                if (best.Length == 0 || !AudioController.IsValidAudioID(best))
                    return;

                // 0.28 rather than 1.0. There is no read-back of EnvSound's own master multiply
                // (it is private and is applied to its own pooled voices), so this is a fixed,
                // deliberately low level chosen to sit under the ambience rather than over it —
                // and it is the single clearest thing for a hardware round to re-tune.
                AudioController.Play(best, world, null, 0.28f);
                VRLog.Info("Core", $"HAUNT FIGURES voice: '{best}' from category '{category}' ({model}) at "
                                   + $"{world:F2}, volume 0.28, positional. It is audible because EnvSound "
                                   + "holds the AudioListener on the VR head; with environment sounds off "
                                   + "this is not played at all.");
            }
            catch (System.Exception ex)
            {
                VRLog.Info("Core", $"HAUNT FIGURES voice skipped for {model} — the audio bank threw "
                                   + $"{ex.GetType().Name}: {ex.Message}. The apparition is silent this time "
                                   + "and nothing else is affected.");
            }
        }

        private static bool Looks(string name, string[] needles)
        {
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < needles.Length; i++)
                if (lower.Contains(needles[i]))
                    return true;
            return false;
        }

        // =========================================================================================
        //  THE CENSUS
        //
        //  Four things about this feature are UNKNOWABLE offline and every one of them is something
        //  a second round would otherwise have to guess at again: the real model-name strings, which
        //  npc bundles this machine actually ships, which of the animator states each creature has,
        //  and what the audio items inside a creature's category are called. The feature is built to
        //  work WITHOUT any of it — every lookup above probes at runtime and degrades — but one
        //  hardware round should hand back both the feature AND the data to make the next one
        //  precise instead of speculative.
        //
        //  IT IS BOUNDED AND IT RUNS ONCE PER PROCESS. Caps are applied to every list, because a log
        //  nobody can read is the same as no log.
        // =========================================================================================

        private const int CensusClassCap = 40;
        private const int CensusBundleCap = 40;

        private static void Census(AssetBundleManager abm, List<CMonsterClass> classes)
        {
            if (_censused)
                return;
            _censused = true;

            try
            {
                var sb = new StringBuilder(4096);
                sb.Append("HAUNT FIGURES CENSUS (once per process; everything here is unknowable without a "
                          + "running game, and it exists so the NEXT round can name creatures and sounds "
                          + "exactly instead of probing for them).\n");

                sb.Append($"  MONSTER CLASSES — {classes.Count} loaded, first {CensusClassCap}:\n");
                for (int i = 0; i < classes.Count && i < CensusClassCap; i++)
                {
                    CMonsterClass c = classes[i];
                    if (c == null)
                        continue;
                    string models = c.Models != null ? string.Join("|", c.Models) : "<null>";
                    string def = c.Models != null && c.Models.Count > 0 ? c.Models[0] : "<none>";
                    sb.Append($"    {c.ID}: default '{def}' models [{models}]\n");
                }

                BundleLoadSettings cfgs = abm.BundleLoadConfigs;
                sb.Append(Bundles("  NPC BUNDLES (base game)", cfgs.BundleConfigs));
                sb.Append(Bundles("  NPC BUNDLES (DLC — never cast, listed for completeness)", cfgs.DLCBundleConfigs));

                sb.Append("  AUDIO CATEGORIES for this feature's cast:\n");
                foreach (KeyValuePair<CClass.ENPCModel, string> kv in Names)
                {
                    string cat = CategoryFor(kv.Key);
                    if (cat.Length == 0)
                    {
                        sb.Append($"    {kv.Key}: no category mapped\n");
                        continue;
                    }
                    AudioCategory? c = AudioController.GetCategory(cat);
                    if (c?.AudioItems == null)
                    {
                        sb.Append($"    {kv.Key}: category '{cat}' not found in the running bank\n");
                        continue;
                    }
                    var names = new List<string>(c.AudioItems.Length);
                    foreach (AudioItem it in c.AudioItems)
                        if (it?.Name != null)
                            names.Add(it.Name);
                    sb.Append($"    {kv.Key} '{cat}': {string.Join(", ", names)}\n");
                }

                VRLog.Info("Core", sb.ToString());
            }
            catch (System.Exception ex)
            {
                VRLog.Info("Core", $"HAUNT FIGURES CENSUS could not be written ({ex.GetType().Name}: "
                                   + $"{ex.Message}). This is diagnostics only — the feature itself does not "
                                   + "read any of it.");
            }
        }

        private static string Bundles(string label, List<BundleLoadSettings.BundleLoadConfig>? list)
        {
            var sb = new StringBuilder(512);
            sb.Append(label).Append(list == null ? " — none\n" : $" — {list.Count}:\n");
            if (list == null)
                return sb.ToString();
            int shown = 0;
            for (int i = 0; i < list.Count && shown < CensusBundleCap; i++)
            {
                BundleLoadSettings.BundleLoadConfig b = list[i];
                if (b == null || b.BundleConfigType != BundleLoadSettings.BundleLoadConfig.EBundleConfigType.NPC)
                    continue;
                shown++;
                string models = b.AssociatedNPCModels != null
                    ? string.Join("|", b.AssociatedNPCModels)
                    : "<null>";
                sb.Append($"    {b.AssetBundleName} dlc={b.BundleDLC} always={b.AlwaysLoaded} models=[{models}]\n");
            }
            return sb.ToString();
        }

        // ---- the per-figure census -----------------------------------------------------------------

        /// <summary>Every animator state name the game's own choreographer knows
        /// (Choreographer.cs:262-302), plus the second idle
        /// (<c>LivingCorpseIdleSelect.cs:12</c>). Probed so the next round knows which creatures can
        /// do what — NOT played: only the idle family is ever played, and the reason is in
        /// HauntFigures.Clone.cs's class doc.</summary>
        private static readonly string[] AllStates =
        {
            "Idle-Run", "Idle-Run2", "Attack", "Damage", "PowerUp", "Hit", "Death", "PushPull", "Pull",
            "Push", "UseItem", "Summoned", "TeleportAway", "TeleportBack", "Loot", "SleepIdle",
            "SleepWakeUp", "SleepHit", "SleepDeath", "CheerAllyIdle", "CheerEnemyIdle",
        };

        private static readonly HashSet<string> FiguresCensused = new(8);

        /// <summary>One line per creature, the first time that creature is ever built.</summary>
        internal static void CensusFigure(string model, GameObject go, Animator? animator, string state,
                                          bool hasRunBlend, float scale)
        {
            if (!FiguresCensused.Add(model))
                return;

            try
            {
                var sb = new StringBuilder(1024);
                sb.Append($"HAUNT FIGURES CENSUS for '{model}' (once per creature per process): ");

                var comps = new Dictionary<string, int>(24);
                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null)
                        continue;
                    string n = c.GetType().Name;
                    comps[n] = comps.TryGetValue(n, out int k) ? k + 1 : 1;
                }
                sb.Append("components after stripping = ");
                foreach (KeyValuePair<string, int> kv in comps)
                    sb.Append($"{kv.Key}x{kv.Value} ");

                // ---- THE LIGHTS, NAMED. -------------------------------------------------------------
                //
                // WHY THIS BLOCK EXISTS. The component histogram above listed 'Lightx2' for 'Living
                // Spirit' through ModBuild 150 and nobody read it as a fault, because the same line
                // also lists Clothx1 and CapsuleColliderx1 — which Strip DOES destroy — so every entry
                // in it looked like a deferred-destroy artifact. Light was the one entry that was not:
                // it is a Behaviour, not a MonoBehaviour, and the allow-list sweep walked past it. A
                // count cannot distinguish those two cases; a name, an intensity and a range can. The
                // scene dump had the evidence all along ('LivingSpirit_Light (1)', intensity 20.00,
                // range 1.0) but nothing tied it to the figure.
                //
                // This runs AFTER Strip, so anything printed here is either a genuine survivor or a
                // component destroyed this frame whose Destroy has not landed — and the line says which
                // by reporting `enabled`, since Strip disables before it destroys.
                var lights = new List<UnityEngine.Light>(4);
                foreach (UnityEngine.Light l in go.GetComponentsInChildren<UnityEngine.Light>(true))
                    if (l != null)
                        lights.Add(l);
                sb.Append(lights.Count == 0
                              ? "| NO Light component survives on this figure, which is the required "
                                + "state: a haunt is lit BY THE ROOM and adds none of its own "
                              : $"| {lights.Count} Light component(s) STILL PRESENT — ");
                for (int i = 0; i < lights.Count && i < 8; i++)
                {
                    UnityEngine.Light l = lights[i];
                    sb.Append($"'{l.name}' {l.type} colour={l.color} intensity={l.intensity:F2} "
                              + $"range={l.range:F1} mask=0x{l.cullingMask:X8} enabled={l.enabled}"
                              + (l.enabled
                                     ? " <-- STILL LIVE: this one lights the creature's own face and the "
                                       + "ground around it, and no albedo or emissive lever can reach it. "
                                     : " (disabled by Strip; its Destroy lands at end of frame) "));
                }

                // ---- THE NON-SKINNED RENDERERS, with their disposition. ------------------------------
                //
                // The same census listed 'MeshRendererx3' beside the two lights and it is a fair
                // question whether those are body or effect: an enemy's body is SKINNED, so a plain
                // MeshRenderer on a creature is usually a prop or a VFX quad. They are NOT in the same
                // blind spot as Light — Collect() already has a policy (a distort/particle/fog/FX
                // shader is destroyed, anything else is kept and darkened with the body) — but through
                // ModBuild 150 the only way to know WHICH branch each took was to notice that the
                // material dump listed six renderers where the histogram implied nine. That is an
                // inference, so this prints the answer instead. `enabled=False` means Collect killed it.
                var meshes = new List<MeshRenderer>(4);
                foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr != null)
                        meshes.Add(mr);
                if (meshes.Count > 0)
                {
                    sb.Append($"| {meshes.Count} non-skinned MeshRenderer(s): ");
                    for (int i = 0; i < meshes.Count && i < 6; i++)
                    {
                        MeshRenderer mr = meshes[i];
                        Material? sm = mr.sharedMaterial;
                        Shader? sh = sm != null ? sm.shader : null;
                        sb.Append($"'{mr.name}' [{(sh != null ? sh.name : "<no shader>")}] "
                                  + $"{(mr.enabled ? "KEPT (darkened with the body)" : "DESTROYED as VFX")} ");
                    }
                }

                sb.Append($"| scale {scale:F3} | playing '{state}' | RunBlend parameter "
                          + $"{(hasRunBlend ? "PRESENT" : "ABSENT — the walk cycle cannot be driven and this "
                                                          + "creature will slide")}");

                if (animator != null)
                {
                    sb.Append(" | states present: ");
                    for (int i = 0; i < AllStates.Length; i++)
                        if (animator.HasState(0, Animator.StringToHash(AllStates[i])))
                            sb.Append(AllStates[i]).Append(' ');

                    RuntimeAnimatorController rac = animator.runtimeAnimatorController;
                    if (rac != null)
                    {
                        sb.Append("| clips: ");
                        AnimationClip[] clips = rac.animationClips;
                        for (int i = 0; i < clips.Length && i < 40; i++)
                            if (clips[i] != null)
                                sb.Append($"{clips[i].name}({clips[i].length:F2}s) ");
                    }
                }
                else
                {
                    sb.Append(" | NO ANIMATOR — the figure will stand in its bind pose");
                }

                VRLog.Info("Core", sb.ToString());
            }
            catch (System.Exception ex)
            {
                VRLog.Info("Core", $"HAUNT FIGURES CENSUS for '{model}' could not be written "
                                   + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }
}
