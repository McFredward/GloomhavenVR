from pathlib import Path
import sys
root, output = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/Cards/BurnArtwork.cs').read_text()
start=s.index('    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardEffects, NativeBurnEpisode<')
end=s.index('    private static void ClearRecoveredSpentBurnStart', start)
a=s[s.index('    private static readonly int GreyOutId'):start]+s[start:end]
start=s.index('    [HarmonyPatch(typeof(CardEffects), nameof(CardEffects.BurnCardTimeline))]')
end=s.index('    private static void RestoreNativeBurnChannels',start)
a+=s[start:end]
start=end
end=s.index('    /// <summary>\n    /// The <see cref="CardEffects"/>',start)
a+=s[start:end]
output.write_text('using HarmonyLib; using UnityEngine; using UnityEngine.UI; namespace GloomhavenVR.Cards; internal static partial class BurnArtwork {\n'+a.replace('private static','internal static').replace('private sealed','internal sealed')+'\n}')
assert 'CardFace.OwnerOf(full)' in a
assert a.index('if (!AllowEffect(__instance, active, effect))') < a.index('ClearRecoveredSpentBurnStart(__instance, full);')
assert 'BurnTimelines.Add(__instance, playback!);' in a
assert 'private static void Prefix(CardEffects __instance, bool burnAnim, ref bool playOnDisabled)' in a, 'Native inactive permission must bind the original coroutine argument before construction'
policy=(root/'src/GloomhavenVR/Cards/Art/BurnLookPolicy.cs').read_text()
assert 'BurnArtwork.ReconcileRecoveredAppearance(fx);' in policy, 'Local recovery binding missing'
assert policy.index('BurnArtwork.ReconcileRecoveredAppearance(fx);') < policy.index('running = fx.coroutine != null;')
sampler=(root/'src/GloomhavenVR/Net/CardAppearanceSampler.cs').read_text()
assert 'BurnArtwork.ReconcileRecoveredAppearance(full.cardEffects);' in sampler, 'Owner capture recovery binding missing'
assert sampler.index('BurnArtwork.ReconcileRecoveredAppearance(full.cardEffects);') < sampler.index('ObserveBurnProgress(card.GameCard, full)') < sampler.index('state.Nodes = capture.Bindings.Capture();')
plume=(root/'src/GloomhavenVR/Net/CardPlumeSampler.cs').read_text()
assert '!ReferenceEquals(smoke, effects._smokeEffect)' in plume, 'Recovered plume ownership binding missing'
assert plume.index('BurnArtwork.ReconcileRecoveredAppearance(effects);') < plume.index('!ReferenceEquals(smoke, effects._smokeEffect)') < plume.index('LocalRigSampler.NameCard(actor, card')
print('Burn replay: 7 source bindings passed.')
