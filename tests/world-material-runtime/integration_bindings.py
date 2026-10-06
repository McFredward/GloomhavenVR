"""Read-only wiring evidence; separate from the executed native runtime fixture."""
from pathlib import Path


def verify(root: Path):
    files={name:root/path for name,path in {
        'core':'src/GloomhavenVR/Core/CoreModule.cs',
        'environment':'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.cs',
        'terrain':'src/GloomhavenVR/Core/Perf/ScenarioTerrainBudget.cs',
    }.items()}
    text={name:path.read_text() for name,path in files.items()}
    ambient_bound='WorldMaterialBudget.ConfigureAmbientWeight(' in text['core']
    if 'WorldMaterialBudget.Install(_hostGo);' not in text['core']:
        return [],{'coverage':'not-integrated-in-this-worker-tree','contracts':0,'controls':0}

    def check(source):
        core,env,terrain=(source[name] for name in ('core','environment','terrain'))
        assert 'WorldMaterialBudget.ConfigureAssetPreparation(ScenarioEnvironmentAssets.EnsureLoaded);' in core,'cold bundle loading binding'
        if ambient_bound:
            assert 'WorldMaterialBudget.ConfigureAmbientWeight(() => PerfConfig.WorldMaterialAmbientWeight);' in core,'current native ambient weight config binding'
        assert 'WorldMaterialBudget.ConfigureBeforeVariantDisposal(ScenarioEnvironmentBudget.BeforeWorldMaterialDisposal);' in core,'consumer disposal binding'
        assert 'WorldMaterialBudget.ConfigureCanonicalSource(ScenarioEnvironmentBudget.CanonicalMaterial);' in core,'native source canonical binding'
        assert 'WorldMaterialBudget.ConfigureSourceChanged(ScenarioEnvironmentBudget.WorldMaterialChanged);' in core,'source refusal consumer binding'
        assert core.index('WorldMaterialBudget.Install(_hostGo);')<core.index('ScenarioEnvironmentBudget.Install(_hostGo);'),'final boundary registration order'
        shutdown=core[core.index('public void Shutdown()'):]
        assert shutdown.index('ScenarioTerrainBudget.Shutdown();')<shutdown.index('WorldMaterialBudget.Shutdown();') and shutdown.index('ScenarioEnvironmentBudget.Shutdown();')<shutdown.index('WorldMaterialBudget.Shutdown();'),'native consumers precede disposal'
        assert 'ScenarioTerrainBudget.OwnsRenderSubstitute(renderer) || ScenarioEnvironmentBudget.OwnsRenderSubstitute(renderer)' in core,'exact native source mask ownership'
        assert 'WorldMaterialBudget.BeginMaterialReadPass, () => PerfConfig.WorldMaterialQualityMode > 0' in core,'scoped world factory binding'
        changed=env[env.index('internal static void WorldMaterialChanged'):env.index('internal static void BeforeWorldMaterialDisposal')]
        assert '_terrainBeforeWrite?.Invoke(renderer);' in changed and '_driver?.BeforeNativeRendererWrite(renderer);' in changed,'world reference change drops terrain and environment consumers'
        assert '_worldBeforeWrite' not in changed and '_worldReady' not in changed,'source change never re-enters world owner'
        disposal=env[env.index('internal static void BeforeWorldMaterialDisposal'):env.index('private static bool TerrainOwns')]
        assert '_terrainBeforeContent?.Invoke();' in disposal and '_driver?.WorldMaterialsDisposing();' in disposal,'all native consumer disposal branches'
        release=env[env.index('internal void WorldMaterialsDisposing()'):env.index('private void Report()')]
        assert 'ReleaseBatches();' in release and 'foreach (InstanceBatch batch in _instances) batch.Dispose();' in release and 'foreach (Batch batch in _batches) batch.Dispose();' in release and '_parts.Clear();' in release,'queued and live batch consumers released'
        assert 'using (_worldReadPass?.Invoke())' in terrain and 'supported &= !world || _worldOwns?.Invoke(next) == true;' in terrain,'terrain requires all audited world slots in synchronous read pass'
        assert 'try { _worldBeforeWrite?.Invoke(renderer); _terrainBeforeWrite?.Invoke(renderer);' in env,'native writer first restores world references'
        assert 'try { _worldBeforeContent?.Invoke(); _terrainBeforeContent?.Invoke(); }' in env,'native clone first restores world references'
        assert 'internal static void Placed(GameObject root) { _worldQueue?.Invoke(root);' in env and 'try { _worldReady?.Invoke(renderer); _terrainReady?.Invoke(renderer);' in env,'bounded native placement and material ready discovery'

    check(text)
    controls=[
        ('core','WorldMaterialBudget.ConfigureAssetPreparation(ScenarioEnvironmentAssets.EnsureLoaded);','/* missing asset loading */','cold bundle loading binding'),
        ('core','WorldMaterialBudget.ConfigureBeforeVariantDisposal(ScenarioEnvironmentBudget.BeforeWorldMaterialDisposal);','/* missing disposal */','consumer disposal binding'),
        ('core','ScenarioTerrainBudget.OwnsRenderSubstitute(renderer) || ScenarioEnvironmentBudget.OwnsRenderSubstitute(renderer)','true','exact native source mask ownership'),
        ('terrain','supported &= !world || _worldOwns?.Invoke(next) == true;','supported &= true;','terrain requires all audited world slots'),
        ('environment','_parts.Clear();','/* retained queued consumers */','queued and live batch consumers released'),
        ('environment','try { _worldBeforeWrite?.Invoke(renderer); _terrainBeforeWrite?.Invoke(renderer);','try { _terrainBeforeWrite?.Invoke(renderer);','native writer first restores world references'),
        ('environment','try { _worldBeforeContent?.Invoke(); _terrainBeforeContent?.Invoke(); }','try { _terrainBeforeContent?.Invoke(); }','native clone first restores world references'),
        ('environment','internal static void Placed(GameObject root) { _worldQueue?.Invoke(root);','internal static void Placed(GameObject root) {','bounded native placement and material ready discovery'),
    ]
    if ambient_bound:
        controls.append(('core','WorldMaterialBudget.ConfigureAmbientWeight(() => PerfConfig.WorldMaterialAmbientWeight);',
                         '/* missing ambient config */','current native ambient weight config binding'))
    for name,before,after,expected in controls:
        assert before in text[name],'integration source binding drift'
        mutated=dict(text);mutated[name]=mutated[name].replace(before,after)
        try:check(mutated)
        except AssertionError as error:
            assert expected in str(error),'integration negative control failed for another reason: '+str(error)
        else:raise AssertionError('integration negative control escaped: '+expected)
    return list(files.values()),{'coverage':'read-only-source-wiring','contracts':16+int(ambient_bound),'controls':len(controls),
        'ambientConfigBound':ambient_bound,
        'limits':'Existing bridge methods are source-bound here; actual material/renderer/camera lifecycle executes in the separate Unity cases.'}
