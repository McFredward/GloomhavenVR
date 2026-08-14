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

        /// <summary>
        /// Choose this slot's creature. <paramref name="hash01"/> ROTATES the cast list rather than
        /// indexing it, so the author's preference order still decides quality (the first entry is
        /// the one the event was designed around) and the hash only decides variety. A machine on
        /// which only one of the three resolves shows that one every time, which is correct
        /// behaviour and not a degradation.
        /// </summary>
        internal static string Pick(CClass.ENPCModel[] cast, float hash01, out CClass.ENPCModel picked)
        {
            Resolve();
            picked = CClass.ENPCModel.None;
            if (cast.Length == 0)
                return string.Empty;

            int off = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(hash01) * cast.Length), 0, cast.Length - 1);
            for (int i = 0; i < cast.Length; i++)
            {
                CClass.ENPCModel m = cast[(off + i) % cast.Length];
                if (!Names.TryGetValue(m, out string? name))
                    continue;
                picked = m;
                return name;
            }
            return string.Empty;
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
