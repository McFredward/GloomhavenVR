# Exact compiler producer ASTs from published B735 (7975c7e8c95d70138a515599246a6682fe1f6aa9).
# Complete original update_driver.py: 110448 bytes,
# SHA256 a791248f986fad53840d70d5485a439fd33de69d912a2c8f87ae4d92d029444a.
# Parsed as immutable source by the production compiler recipe; never executed.
def _art_rows(files):
    producers = {'scripts/generate-environment-meshes.py', 'tools/quest-builder/environment_bank.py', 'tools/quest-builder/mod_assets.py'}
    return sorted((row for row in files if row['path'].startswith(('unity/GloomhavenVR.Assets/', 'tools/environment-mesh/')) and (not row['path'].endswith('.md')) or row['path'] in producers), key=lambda row: row['path'])

def _separate_movie(name):
    """Separate movie Content is delivered after compilation, never by IL2CPP.

    The current desktop project explicitly copies these files alongside its
    DLL. It does not embed them. Keep this exclusion limited to those media and
    provenance suffixes: a future C# file or embedded asset in this directory
    must still participate in the ordinary compiler identity.
    """
    return name.startswith('src/GloomhavenVR/Assets/Movies/') and name.endswith(('.mov', '.provenance.json'))

def _separate_menu_story(name):
    return name.startswith('src/GloomhavenVR/Assets/MenuDepth/Story/') and (name.endswith('.gmd') or name == 'src/GloomhavenVR/Assets/MenuDepth/Story/manifest.json')

def _code_rows(files, *, normalizer=None):
    """Conservatively retain every consumed runtime/compiler source identity.

    The source snapshot also contains browser, installer and recovery tools.
    Those do not become IL2CPP code. Native/assets and orchestration are checked
    independently before this comparison; an unsupported change never reaches
    the no-compiler path. Compiler-driver edits remain inputs even when they
    only improve reporting, rather than guessing whether new code is equivalent.
    """
    prefixes = ('src/', 'tools/QuestWeaver/', 'tools/quest-menu-depth/', 'libs/RefAsm/')
    producers = {'tools/quest-builder/apk_update.py', 'tools/quest-builder/update_driver.py', 'tools/quest-builder/quest_card_alpha.py', 'tools/quest-builder/menu_depth_resources.py', 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs'}

    def selected(name):
        if name == 'quest-builder-release.json':
            return False
        if _separate_movie(name) or _separate_menu_story(name):
            return False
        if name.startswith(prefixes) or name in producers:
            return True
        if name.startswith('unity/GloomhavenVR.Quest/Assets/Quest/Runtime/'):
            return True
        return '/' not in name and name.endswith(('.props', '.targets', '.config', '.json'))
    rows = [row for row in files if selected(row['path'])]
    if normalizer is not None:
        for name in ('MENU_DEPTH_DELIVERY_ROWS', 'INSTALLED_UPDATE_DELIVERY_ROWS', 'INSTALLED_UPDATE_DELIVERY_PREVIOUS_ROWS'):
            aliases = getattr(normalizer, name, {})
            rows = [aliases[row['path']][1] if row['path'] in aliases and row == aliases[row['path']][0] else row for row in rows]
        fixed = getattr(normalizer, 'UPDATE_DOWNLOAD_FIXED', None)
        previous = getattr(normalizer, 'UPDATE_DOWNLOAD_PREVIOUS', None)
        preceding = getattr(normalizer, 'UPDATE_DOWNLOAD_PARALLEL_PREVIOUS', None)
        retained = getattr(normalizer, 'UPDATE_RETAINED_APK_FIXED', None)
        tool_handoff = getattr(normalizer, 'UPDATE_TOOL_HANDOFF_FIXED', None)
        pixel_delivery = getattr(normalizer, 'UPDATE_PIXEL_DELIVERY_FIXED', None)
        if fixed is not None and previous is not None:
            rows = [previous if row in (fixed, preceding, retained, tool_handoff) else row for row in rows]
        pixel_previous = getattr(normalizer, 'UPDATE_PIXEL_DELIVERY_PREVIOUS', None)
        if pixel_delivery is not None and pixel_previous is not None:
            rows = [pixel_previous if row == pixel_delivery else row for row in rows]
        media_aliases = getattr(normalizer, 'REBASE_MEDIA_DELIVERY_ROWS', {})
        rows = [media_aliases[row['path']][1] if row['path'] in media_aliases and row == media_aliases[row['path']][0] else row for row in rows]
    return sorted(rows, key=lambda row: row['path'])

def _native_rows(files):
    """A code-only update cannot silently retain changed non-IL2CPP programs."""
    interop = {'QuestGameNetwork.cs', 'QuestContentHash.cs', 'QuestPassthroughFeature.cs', 'QuestGameProcedural.cs'}
    producers = {'tools/quest-builder/native_plugins.py', 'tools/quest-builder/campaign_native.py', 'scripts/build-quest-native.py', 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestNativePluginContract.cs'}
    prefixes = ('tools/quest-native/', 'tools/quest-network/', 'tools/quest-procedural-runtime/')

    def selected(name):
        if name in producers:
            return True
        if name.startswith('unity/GloomhavenVR.Quest/Assets/Quest/Runtime/') and Path(name).name in interop:
            return True
        return name.startswith(prefixes) and (not (name.endswith(('.md', '.meta')) or '/tests/' in name))
    return sorted((row for row in files if selected(row['path'])), key=lambda row: row['path'])

def _payload_rows(files):
    """Sources whose output is not replaced by the compact code/art writer.

    All Runtime C# files are compiled by stage_code_project; the static weaver
    and authored VR bank producer also run again when required. Original
    serialized scenes, shaders, recovered media and native programs do not.
    Their producer/template changes therefore need the ordinary preparation
    path, which retains independently compatible conversion checkpoints.
    Host-only progress, installer and diagnostics edits do not invalidate game
    payloads. A missing producer in an older manifest is a real mismatch too.
    """
    producers = {'builder.py', 'startup.py', 'campaign.py', 'audio.py', 'media.py', 'shaders.py', 'sprites.py', 'ui_assets.py', 'ui_blur.py', 'full_assets.py', 'full_audio.py', 'full_shaders.py', 'full_sprites.py', 'full_texture2d.py', 'full_textures.py', 'campaign_compute.py', 'campaign_shaders.py', 'campaign_native_shaders.py', 'stereo_eye_repair.py', 'script_order.py', 'installation_manifest.py', 'native_content_pack.py', 'staging_copy.py', 'preparation_repair.py', 'import_workspace.py'}

    def selected(name):
        if name.startswith('unity/GloomhavenVR.Quest/'):
            if name.startswith('unity/GloomhavenVR.Quest/Assets/Quest/Runtime/') and name.endswith('.cs'):
                return False
            if name == 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs':
                return False
            return not name.endswith('.md')
        if name.startswith('tools/quest-builder/') and Path(name).name in producers:
            return True
        if name == 'src/GloomhavenVR/Assets/GloomhavenVR_logo.png':
            return True
        return name.startswith(('tools/quest-recovery/', 'tools/quest-shaders/', 'tools/quest-compute/', 'libs/RuntimeDeps/', 'libs/Natives/', 'prebuilt/')) and (not name.endswith('.md')) and ('/tests/' not in name)
    return sorted((row for row in files if selected(row['path'])), key=lambda row: row['path'])

def _compact_player_command(builder, argv, launch_log, unity_log, env):
    """Keep compiler preparation failures separate from asset migration proof.

    Support153412 failed plugin import; Support170547 lost a conditional package
    script's namespace while reading the Editor catalog. Neither missing entry
    establishes a changed serialized asset layout. Only the qualified compiled
    player comparison may require migration. Preserve the old APK and compiler
    results rather than proposing a full build for a preparation defect.
    """
    unity_log = Path(unity_log)

    def stamp():
        if not unity_log.exists():
            return None
        value = unity_log.lstat()
        if not stat.S_ISREG(value.st_mode) or value.st_nlink != 1 or getattr(value, 'st_file_attributes', 0) & 1024:
            raise builder.BuildError('Compact compiler log is not a regular owned file.')
        return (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
    openxr_unloaded = re.compile('(?:^|\\n)(?:Error building Player: )?BuildFailedException: OpenXR Settings found in project but not yet loaded\\.  Please build again\\.\\r?(?:\\n|$)')
    for attempt in range(2):
        before = stamp()
        try:
            return builder.command(argv, launch_log, env=env)
        except builder.BuildError as exc:
            after = stamp()
            text = ''
            if after is not None and after != before:
                with unity_log.open('rb') as stream:
                    stream.seek(max(0, after[2] - 65536))
                    text = stream.read(65536).decode('utf-8', errors='replace')
                if stamp() != after:
                    text = ''
            if openxr_unloaded.search(text):
                if attempt == 0:
                    builder.build_progress.event('update-code-openxr-retry', operation='update-code', status='start', detail='OpenXR registered its settings during the first build callback. Retrying the same compiler project once; completed inputs and Library are retained.')
                    continue
                raise builder.BuildError('Compact update OpenXR preparation failed: the package settings are still unavailable after one retained compiler retry. Keep the existing APK and workspace for a targeted settings repair; no full game rebuild was started.') from exc
            missing = re.search('(?:^|\\n)(?:InvalidDataException: )?Retained original serialized script is missing: ([^\\r\\n]{1,512}); a full build is required\\.\\r?(?:\\n|$)', text)
            if missing:
                imports = re.search('[^\\r\\n]{0,240}(?:Unable to resolve reference|references itself\\.|Reference has errors)[^\\r\\n]{0,240}', text)
                if imports:
                    raise builder.BuildError('Compact update plugin import failed: ' + imports[0].strip() + '. The missing script is not evidence that game assets need conversion. Keep the existing APK and workspace; retry with the corrected Builder.') from exc
                raise builder.BuildError('Compact update repair required: the compiler could not resolve retained script ' + missing[1] + '. The Editor catalog is incomplete; this does not establish that game assets need conversion. Keep the existing APK and workspace for a targeted compiler repair.') from exc
            raise

def _native_producer_rows(files):
    host_parsers = {'tools/quest-builder/apk_update.py', 'tools/quest-builder/update_driver.py'}
    selected = {}
    for selector in (_code_rows, _payload_rows, _native_rows, _art_rows):
        for row in selector(files):
            if row['path'] not in host_parsers:
                selected[row['path']] = row
    return [selected[name] for name in sorted(selected)]

def _compile_code(builder, args, inputs, output, source, game, project, base, tools, root):
    core = _core(builder)
    builder.build_progress.operation('update-code', detail='Compiling the current mod and Quest-only original-game bindings.')
    package_abi = package_fingerprint(builder, source, inputs)
    compiled = root / 'code-player.apk'
    compiled_receipt = Path(str(compiled) + '.code-build.json')
    stages = builder.Stages(output)
    current_plan = _native_resume().current_plan(builder, __file__, inputs, root)
    closed = stages.valid('update-code', root.name)
    if closed:
        compiled, compiled_receipt = _native_resume().closed_paths(builder, output, root, closed)
        result = core.collect_code_update(base, compiled, compiled_receipt, package_abi=package_abi)
        builder.build_progress.operation('update-code', complete=True, detail='Completed native code and metadata reused; no compiler rerun.')
        return result
    retained = _native_resume().retained_player(builder, __file__, inputs, output, source, project, base, root, package_abi, producer_rows=_native_producer_rows, current=current_plan)
    if retained:
        compiled, compiled_receipt, result = retained
        stages.run('update-code', root.name, lambda: ([compiled, compiled_receipt], {'nativePairQualified': True}))
        builder.build_progress.operation('update-code', complete=True, detail='Retained completed native Player passed current compatibility checks; no weaving, SDK or native compiler rerun.')
        return result
    stages.run('update-code-production', root.name, lambda: _build_code_player(builder, args, inputs, output, source, game, project, base, tools, root, package_abi, compiled, compiled_receipt))
    result = core.collect_code_update(base, compiled, compiled_receipt, package_abi=package_abi)
    stages.run('update-code', root.name, lambda: ([compiled, compiled_receipt], {'nativePairQualified': True}))
    builder.build_progress.operation('update-code', complete=True, detail='Matched ARM64 code, metadata and original script types compiled.')
    return result

def _build_code_player(builder, args, inputs, output, source, game, project, base, tools, root, package_abi, compiled, compiled_receipt):
    core = _core(builder)
    core.stage_code_project(source, project, base, game / 'Managed', profile=inputs['profile'], package_abi=package_abi, version_code=inputs['mod']['modBuild'], version_name='0.1.0.B' + str(inputs['mod']['modBuild']) + '.' + root.name[:12], code_key=root.name, retain_compatible_library=True)
    builder.weave(args, inputs, output, source, game, project)
    ugui = project / 'Packages/com.unity.ugui/Runtime/UI/Core/Layout/LayoutRebuilder.cs'
    if ugui.is_file():
        proof = json.loads((project / 'QuestStartupEvidence/ugui-layout-gate.json').read_text())
        if proof.get('derivedSha256') != builder.digest(ugui) or proof.get('sourceSha256') != builder.UGUI_LAYOUT_SOURCE_SHA256:
            raise builder.BuildError('The retained private UGUI package changed; no native update was produced.')
    else:
        builder.restore_ugui_layout_gate(project, game, Path(tools['editor']))
    packages = json.loads((project / 'Packages/manifest.json').read_text())
    packages['dependencies'].update(builder.original_builtin_modules(game, Path(tools['editor'])))
    packages['dependencies'].update({'com.unity.addressables': '1.19.19'})
    builder.write_json(project / 'Packages/manifest.json', packages)
    env = dict(os.environ)
    env.update(core.code_build_env(project, compiled))
    env.update({'GHVR_QUEST_ANDROID_SDK': tools['androidSdk'], 'GHVR_QUEST_ANDROID_NDK': tools['androidNdk'], 'GHVR_QUEST_JDK': tools['jdk'], 'UNITY_IL2CPP_ANDROID_USE_LLD_LINKER': '1'})
    policy = builder.host_resources.phase_budget('il2cpp', output)
    if not policy['nativeLaunchAllowed']:
        raise builder.BuildError('Available RAM/commit cannot admit the native compiler. Completed update work is retained.')
    dotnet = builder.tool_path(args.dotnet, 'dotnet')
    launcher = builder.host_resources.prepare_bee_launcher(dotnet, output)
    env = builder.host_resources.unity_native_environment(tools['editor'], launcher, policy['jobs'], dotnet, env)
    _bind_code_packages(builder, args, output, source, game, project, tools, env, root.name)
    _compact_player_command(builder, builder.unity_launcher(tools['editor']) + ['-quit', '-projectPath', str(project), '-buildTarget', 'Android', '-executeMethod', 'GloomhavenVR.Quest.Editor.QuestCodeUpdateBuild.Build', '-logFile', str(output / 'logs/update-code-unity.log')], output / 'logs/update-code-launch.log', output / 'logs/update-code-unity.log', env)
    return ([compiled, compiled_receipt], {'nativePlayerProduced': True})

def _bind_code_packages(builder, args, output, source, game, project, tools, env, update_key):
    sdk = project / 'QuestStartupEvidence/PlayerSdk'

    def compile_sdk():
        builder.command(builder.unity_launcher(tools['editor']) + ['-quit', '-projectPath', str(project), '-buildTarget', 'Android', '-executeMethod', 'GloomhavenVR.Quest.Editor.QuestCodeUpdateBuild.CompileSdk', '-logFile', str(output / 'logs/update-sdk-unity.log')], output / 'logs/update-sdk-launch.log', env=env)
        return ([sdk / 'compilation.json', *(sdk / (name + '.dll') for name in builder.REPLACED_PACKAGES)], {'actualAndroidSdk': True})
    builder.Stages(output).run('update-sdk', update_key, compile_sdk)
    evidence = json.loads((sdk / 'compilation.json').read_text())
    if evidence != {'schema': 1, 'target': 'Android', 'backend': 'IL2CPP', 'compilation': 'Player', 'options': 'DevelopmentBuild|Assertions', 'unityVersion': '2021.3.5f1'}:
        raise builder.BuildError('The code update did not compile the actual Android package APIs.')
    plugins = sorted((path for path in (project / 'Assets/Plugins/QuestGame').glob('*.dll') if path.stem not in builder.REPLACED_PACKAGES))
    root = output / 'updates' / update_key / 'package-api'
    original, rewritten = (root / 'input', root / 'output')
    original.mkdir(parents=True, exist_ok=True)
    for path in plugins:
        shutil.copyfile(path, original / path.name)
    report = root / 'report.json'
    sdk_rows = builder.inventory(sdk, [name + '.dll' for name in builder.REPLACED_PACKAGES])
    selected = [builder.record_file(path, path.name) for path in plugins]
    key = builder.value_hash({'plugins': selected, 'sdk': sdk_rows})

    def bind():
        builder.command([str(builder.tool_path(args.dotnet, 'dotnet')), 'run', '--project', str(source / 'tools/QuestWeaver/QuestWeaver.csproj'), '--configuration', 'Release', '--', 'package-api', '--managed', str(original), '--sdk', str(sdk), '--sdk-target', 'Android', '--reference-managed', str(game / 'Managed'), '--output', str(rewritten), '--report', str(report)], output / 'logs/update-package-api.log', cwd=source)
        value = json.loads(report.read_text())
        if value.get('complete') is not True or value.get('issues') or value.get('inputAssemblies') != {row['path']: row['sha256'] for row in selected} or (value.get('sdkAssemblies') != {row['path']: row['sha256'] for row in sdk_rows}) or (value.get('outputAssemblies') != {row['path']: row['sha256'] for row in builder.inventory(rewritten)}):
            raise builder.BuildError('The code-update package API binding is incomplete or differs from its actual inputs.')
        return ([report, *sorted(rewritten.glob('*.dll'))], {'complete': True})
    builder.Stages(output).run('update-package-api', key, bind)
    for path in plugins:
        shutil.copyfile(rewritten / path.name, path)

def package_fingerprint(builder, source, inputs):
    manifest = json.loads((Path(source) / 'unity/GloomhavenVR.Quest/Packages/manifest.json').read_text())
    return builder.value_hash({'unityVersion': inputs['game']['unityVersion'], 'packages': manifest, 'playerUnityVersion': '2021.3.5f1', 'addressables': '1.19.19', 'tmpShaderSource': '3.0.6', 'uguiOriginal': builder.ORIGINAL_UGUI_SHA256, 'uguiSource': builder.UGUI_LAYOUT_SOURCE_SHA256, 'format': 1})
