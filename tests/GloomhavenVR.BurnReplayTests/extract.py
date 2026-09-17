from pathlib import Path
import sys
root, output = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/Cards/BurnArtwork.cs').read_text()
start=s.index('    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardEffects, NativeBurnEpisode<')
end=s.index('    private static void ClearRecoveredSpentBurnStart', start)
a=s[start:end]
start=s.index('    [HarmonyPatch(typeof(CardEffects), nameof(CardEffects.BurnCardTimeline))]')
end=s.index('    private static void RestoreNativeBurnChannels',start)
a+=s[start:end]
output.write_text('using HarmonyLib; using UnityEngine; namespace GloomhavenVR.Cards; internal static partial class BurnArtwork {\n'+a.replace('private static','internal static').replace('private sealed','internal sealed')+'\n}')
assert 'CardFace.OwnerOf(full)' in a
assert a.index('if (!AllowEffect(__instance, active, effect)) return false;') < a.index('ClearRecoveredSpentBurnStart(__instance, full);')
assert 'BurnTimelines.Add(__instance, playback!);' in a
print('Burn replay: 3 source bindings passed.')
