"""Reconstruct original ShaderLab passes around strictly bound native DXBC math.

Writes a private overlay; the original/staged game remains read only. Compiler
coverage is independent of representative renderer fixtures: native fullscreen
and internal shaders do not need invented meshes/materials to enter the bank.
"""
from __future__ import annotations

import argparse
import collections
import functools
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil

from manifest import ValidationError, sha256
import integer_bits
import load_bounds


@functools.lru_cache(maxsize=1)
def recovery_module():
    path = Path(__file__).resolve().parents[1] / 'quest-builder/full_shaders.py'
    spec = importlib.util.spec_from_file_location('quest_native_shader_recovery', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def properties(form):
    lines = []
    for row in form['m_PropInfo']['m_Props']:
        name = row['m_Name']
        if not re.fullmatch(r'[A-Za-z_]\w*', name):
            raise ValidationError('Original ShaderLab property has an invalid identifier.')
        values = [format(row['m_DefValue_' + str(i) + '_'], '.9g') for i in range(4)]
        kind = row['m_Type']
        if kind in (0, 1):
            shape, value = ('Color' if kind == 0 else 'Vector'), '(' + ','.join(values) + ')'
        elif kind in (2, 3):
            shape = 'Float' if kind == 2 else 'Range(' + values[1] + ',' + values[2] + ')'
            value = values[0]
        elif kind == 4:
            try:
                shape = {1: 'any', 2: '2D', 3: '3D', 4: 'Cube', 5: '2DArray', 6: 'CubeArray'}[row['m_DefTexture']['m_TexDim']]
            except KeyError as error:
                raise ValidationError('Unsupported original ShaderLab texture dimension.') from error
            value = json.dumps(row['m_DefTexture']['m_DefaultName']) + ' {}'
        else:
            raise ValidationError('Unsupported original ShaderLab property kind: ' + str(kind))
        attributes = []
        for text in row['m_Attributes']:
            if '[' in text or ']' in text or '\n' in text:
                raise ValidationError('Original ShaderLab property attribute cannot be represented.')
            attributes.append('[' + text + ']')
        lines.append(' '.join(attributes + [name]) + ' (' + json.dumps(row['m_Description']) + ', ' + shape + ') = ' + value)
    return '\n'.join(lines)


def _banks(variants, stage):
    groups = collections.defaultdict(list)
    for row in variants:
        if row['stage'] == stage:
            groups[tuple(sorted(row['keywords']))].append(row)
    banks = {}
    for keywords, rows in groups.items():
        distinct = {(r['originalDxbcSha256'], r['originalInterfaceSha256']) for r in rows}
        for row in rows:
            identity = (row['originalDxbcSha256'], row['originalInterfaceSha256'])
            selection = keywords if len(distinct) == 1 else tuple(sorted([*keywords, 'UNITY_HARDWARE_TIER' + str(row['hardwareTier'] + 1)]))
            if selection in banks and banks[selection]['identity'] != identity:
                raise ValidationError('One original native hardware-tier bank has conflicting bytecode.')
            banks[selection] = {'identity': identity, 'row': row}
    return banks


def _selected(banks, keys):
    candidates = [(len(key), key, row) for key, row in banks.items() if set(key) <= set(keys)]
    if not candidates:
        raise ValidationError('Native pass has no original stage matching its keyword bank.')
    highest = max(row[0] for row in candidates)
    best = [row for row in candidates if row[0] == highest]
    if len({row[2]['identity'] for row in best}) != 1:
        raise ValidationError('Original stage keyword selection is ambiguous.')
    return best[0][2]['row']


@functools.lru_cache(maxsize=None)
def _program_features(interface_path, proof_path):
    interface = json.loads(Path(interface_path).read_text())
    proof = json.loads(Path(proof_path).read_text())
    observed = {value['originalBuffer']: set(value['usedScalarIndices']) for value in proof['usedOriginalBuffers']}
    native = recovery_module()
    names = {field['name'] for buffer in interface['buffers'] for field in buffer['fields']
             if set(native.field_components(field)) & observed.get(buffer['name'], set())}
    # These are Unity's explicit stereo array aliases. Their original native
    # field identities, rather than shader/pass display names, require an eye.
    eye = any(name.startswith('unity_Stereo') and name != 'unity_StereoEyeIndex' for name in names) or bool(names & {'_WorldSpaceCameraPos', 'unity_MatrixV', 'unity_MatrixInvV', 'unity_MatrixP',
                         'unity_MatrixInvP', 'unity_MatrixVP', 'unity_CameraProjection', 'unity_CameraInvProjection',
                         'unity_WorldToCamera', 'unity_CameraToWorld'})
    structures = any(buffer.get('structures') and observed.get(buffer['name']) for buffer in interface['buffers'])
    return eye, structures


def _fragment_eye(row, cache):
    return _program_features(str(cache / 'interfaces' / (row['originalInterfaceSha256'] + '.json')),
                             str(Path(row['boundHlslPath']).with_suffix('.json')))[0]


def _instance_layout(row, cache):
    return _program_features(str(cache / 'interfaces' / (row['originalInterfaceSha256'] + '.json')),
                             str(Path(row['boundHlslPath']).with_suffix('.json')))[1]


@functools.lru_cache(maxsize=None)
def _native_light_field(interface_path):
    interface = json.loads(Path(interface_path).read_text())
    fields = [field for buffer in interface['buffers'] for field in buffer['fields'] if field['name'] == '_LightColor0']
    for field in fields:
        if field['type'] != 0 or field['rows'] != 1 or field['columns'] != 4 or field['matrix'] or field['arraySize']:
            raise ValidationError('Original light color has an unsupported native field layout.')
    return bool(fields)


def _stage_block(banks, stage, include_paths):
    keys = sorted(set().union(*(set(k) for k in banks)))
    rows = ['#if defined(SHADER_STAGE_' + stage.upper() + ')']
    for index, (selection, bank) in enumerate(sorted(banks.items(), key=lambda row: (-len(row[0]), row[0]))):
        # Only keywords present in this original stage participate. A fragment
        # keyword absent from vertex metadata must not reject the vertex bank.
        condition = ['defined(' + key + ')' for key in selection]
        condition += ['!defined(' + key + ')' for key in keys if key not in selection and not key.startswith('UNITY_HARDWARE_TIER')]
        rows.append(('#if ' if index == 0 else '#elif ') + (' && '.join(condition) or '1'))
        if 'STEREO_INSTANCING_ON' in bank['row']['keywords']:
            rows.append('#define QUEST_NATIVE_STEREO_INSTANCE_ID 1')
        rows.append('#include ' + json.dumps(include_paths[bank['identity']]))
    rows += ['#else', '#error No exact original native keyword bank is available', '#endif', '#endif']
    return '\n'.join(rows)


def _exclusive_groups(banks, candidates):
    """Find source-witnessed exact-one keyword choices, never name heuristics."""
    states = sorted(set(frozenset(key) & candidates for key in banks), key=lambda row: (len(row), sorted(row)))
    if not states or any(not state for state in states):
        return []
    masks = {key: sum(1 << index for index, state in enumerate(states) if key in state) for key in sorted(candidates)}
    all_states = (1 << len(states)) - 1
    groups = set()
    def cover(selected, covered):
        if covered == all_states:
            if len(selected) > 1:
                groups.add(tuple(sorted(selected)))
            return
        uncovered = (all_states ^ covered) & -(all_states ^ covered)
        for key, mask in masks.items():
            if mask & uncovered and not mask & covered:
                cover((*selected, key), covered | mask)
    for key, mask in masks.items():
        if mask:
            cover((key,), mask)
    return sorted(groups, key=lambda group: (-len(group), group))


def _keyword_pragmas(vertex, fragment, keys, mandatory):
    engine = {'INSTANCING_ON', 'STEREO_INSTANCING_ON', 'STEREO_MULTIVIEW_ON'}
    remaining = set(keys) - mandatory - engine
    groups = _exclusive_groups(vertex, remaining) + _exclusive_groups(fragment, remaining)
    selected, claimed = [], set()
    for group in sorted(set(groups), key=lambda value: (-len(value), value)):
        if not claimed & set(group):
            selected.append(group)
            claimed.update(group)
    # The editor imports a default variant before explicit native-bank probes.
    # Each exact-one choice starts with an actual least-keyword native bank;
    # generating an all-disabled light/shadow bank would invent a program that
    # did not exist in the original player's serialized shader.
    defaults = sorted(set(vertex) | set(fragment), key=lambda state: (len(state), state))
    default = next((state for state in defaults if all(len(set(group) & set(state)) == 1 for group in selected)), None)
    if selected and default is None:
        raise ValidationError('Original exclusive keyword graph lacks a witnessed default bank.')
    rows = ['#pragma multi_compile ' + key for key in sorted(mandatory - engine)]
    for group in selected:
        first = next(key for key in group if key in default)
        rows.append('#pragma multi_compile ' + ' '.join([first, *(key for key in group if key != first)]))
    rows += ['#pragma shader_feature ' + key for key in sorted(remaining - claimed)]
    return rows


def shader_source(form, record, cache, includes, graphics_api="Vulkan"):
    native = recovery_module()
    lines = ['Shader ' + json.dumps(form['m_Name']) + ' {', 'Properties {', properties(form), '}']
    compiler_variants = []
    for si, subshader in enumerate(form['m_SubShaders']):
        lines += ['SubShader {', native.tags(subshader['m_Tags'])]
        if subshader.get('m_LOD'):
            lines.append('LOD ' + str(subshader['m_LOD']))
        for pi, original_pass in enumerate(subshader['m_Passes']):
            if original_pass['m_Type'] == 1:
                if not original_pass['m_UseName']:
                    raise ValidationError('Original UsePass lacks its native qualified address.')
                lines.append('UsePass ' + json.dumps(original_pass['m_UseName']))
                continue
            if original_pass['m_Type'] == 2:
                # SerializedPassType is source-proven: Pass=0, UsePass=1,
                # GrabPass=2. The original framebuffer-grab pass has no banks.
                lines.append('GrabPass { ' + (json.dumps(original_pass['m_TextureName']) if original_pass['m_TextureName'] else '') + ' }')
                continue
            if original_pass['m_Type'] != 0:
                raise ValidationError('Unsupported original native ShaderLab pass type.')
            variants = [row for row in record['variants'] if row['subshader'] == si and row['pass'] == pi]
            if {row['stage'] for row in variants} - {'vertex', 'fragment'}:
                raise ValidationError('Original geometry/tessellation stage requires a proven Quest translation.')
            vertex, fragment = _banks(variants, 'vertex'), _banks(variants, 'fragment')
            if not vertex or not fragment:
                if not variants and not any(original_pass[stage]['m_SubPrograms'] for stage in ('progVertex', 'progFragment', 'progGeometry', 'progHull', 'progDomain')):
                    # Native Windows retains serialized fallback pass shells
                    # whose programs were stripped. Preserve the original
                    # state/pass ordinal; there is no original math to replace.
                    lines += ['Pass {', native.render_state(original_pass['m_State']), '}']
                    continue
                raise ValidationError('Original programmed pass has no complete native stage bank: ' + record['guid'] + ' / ' + str(si) + '/' + str(pi))
            keys = sorted(set().union(*(set(row['keywords']) for row in variants)))
            light_modes = {str(value).upper() for key, value in original_pass['m_State']['m_Tags']['tags'] if key.upper() == 'LIGHTMODE'}
            if len(light_modes) > 1:
                raise ValidationError('Original native pass has ambiguous LightMode state.')
            pass_type = {'FORWARDBASE': 'ForwardBase', 'FORWARDADD': 'ForwardAdd', 'SHADOWCASTER': 'ShadowCaster',
                         'DEFERRED': 'Deferred', 'META': 'Meta', 'MOTIONVECTORS': 'MotionVectors',
                         'PREPASSBASE': 'LightPrePassBase', 'PREPASSFINAL': 'LightPrePassFinal',
                         'VERTEX': 'Vertex', 'VERTEXLM': 'VertexLM', 'VERTEXLMRGBM': 'VertexLMRGBM',
                         'ALWAYS': 'Normal', 'SRPDEFAULTUNLIT': 'ScriptableRenderPipelineDefaultUnlit'}
            mode = next(iter(light_modes), 'ALWAYS')
            if mode not in pass_type:
                raise ValidationError('Original native LightMode requires a ShaderVariantCollection mapping: ' + mode)
            mandatory = (set.intersection(*(set(k) for k in vertex)) | set.intersection(*(set(k) for k in fragment))) - {'UNITY_HARDWARE_TIER1', 'UNITY_HARDWARE_TIER2', 'UNITY_HARDWARE_TIER3'}
            lines += ['Pass {', native.render_state(original_pass['m_State']), 'HLSLPROGRAM',
                      '#pragma target 4.5', '#pragma vertex QuestOriginalVertex', '#pragma fragment QuestOriginalFragment']
            if any(_instance_layout(row, cache) for row in variants):
                # Unity's HLSLcc accepts native struct-array addressing only
                # when its immediate index definition remains IMUL/ISHL.
                # Reoptimizing the recovered original instruction math folds
                # that into a quotient and destroys its reflection pattern.
                # Keep the front-end addressing; the GLES/Vulkan driver owns
                # normal native optimization. No original instance is capped.
                lines.append('#pragma skip_optimizations gles3' + (' vulkan' if graphics_api == 'Vulkan' else ''))
            lines += _keyword_pragmas(vertex, fragment, keys, mandatory)
            lines += ['#pragma hardware_tier_variants ' + ('vulkan' if graphics_api == 'Vulkan' else 'gles3'), '#pragma multi_compile_instancing', '#pragma multi_compile __ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON',
                      '#define UNITY_LIGHT_PROBE_PROXY_VOLUME 1', '#include "UnityCG.cginc"']
            # The original instructions do not call high-level Unity lighting
            # helpers. LightingCommon's fixed4 _SpecColor collides with an exact
            # native material declaration, and its fixed4 light would narrow
            # the original float payload. Keep the original engine-bound name
            # and witnessed float4 field instead of importing unrelated types.
            light_fields = {_native_light_field(str(cache / 'interfaces' / (identity + '.json')))
                            for identity in {row['originalInterfaceSha256'] for row in variants}}
            if True in light_fields:
                lines.append('float4 _LightColor0;')
            lines += [_stage_block(vertex, 'vertex', includes), _stage_block(fragment, 'fragment', includes), 'ENDHLSL', '}']
            for tier, selection in sorted({(row['hardwareTier'], tuple(sorted(set(row['keywords']) | mandatory))) for row in variants}):
                selector = [*selection, 'UNITY_HARDWARE_TIER' + str(tier + 1)]
                v, f = _selected(vertex, selector), _selected(fragment, selector)
                compiler_variants.append({'subshader': si, 'pass': pi, 'passType': pass_type[mode], 'hardwareTier': tier, 'keywords': list(selection),
                    'coverageKind': 'original-native',
                    'stereo': 'multiview' if 'STEREO_MULTIVIEW_ON' in selection else 'instancing' if 'STEREO_INSTANCING_ON' in selection else 'mono',
                    'vertexOriginalDxbcSha256': v['originalDxbcSha256'], 'fragmentOriginalDxbcSha256': f['originalDxbcSha256'],
                    'fragmentOutput': f['fragmentOutput'], 'requiresFragmentEyeRouting': _fragment_eye(f, cache),
                    'viewInvariant': not _fragment_eye(v, cache) and not _fragment_eye(f, cache)})
            # Quest needs both views even when desktop has no native multiview
            # alias. The same exact desktop math receives Unity's view matrices.
            for tier, selection in sorted({(row['hardwareTier'], tuple(sorted(set(row['keywords']) | mandatory))) for row in variants if not set(row['keywords']) & {'STEREO_MULTIVIEW_ON', 'STEREO_INSTANCING_ON'}}):
                selector = [*selection, 'UNITY_HARDWARE_TIER' + str(tier + 1)]
                v, f = _selected(vertex, selector), _selected(fragment, selector)
                compiler_variants.append({'subshader': si, 'pass': pi, 'passType': pass_type[mode], 'hardwareTier': tier, 'keywords': sorted([*selection, 'STEREO_MULTIVIEW_ON']),
                    'coverageKind': 'quest-synthetic',
                    'stereo': 'multiview', 'vertexOriginalDxbcSha256': v['originalDxbcSha256'],
                    'fragmentOriginalDxbcSha256': f['originalDxbcSha256'], 'fragmentOutput': f['fragmentOutput'],
                    'requiresFragmentEyeRouting': _fragment_eye(f, cache),
                    'viewInvariant': not _fragment_eye(v, cache) and not _fragment_eye(f, cache)})
        lines.append('}')
    # Preserve the original intended fallback as metadata only; every required
    # native bank still passes the explicit compiler gate before shipping.
    if form.get('m_FallbackName'):
        lines.append('Fallback ' + json.dumps(form['m_FallbackName']))
    else:
        lines.append('Fallback Off')
    lines.append('}')
    unique = {}
    for row in compiler_variants:
        unique[(row['subshader'], row['pass'], row['hardwareTier'], tuple(row['keywords']))] = row
    return '\n'.join(lines) + '\n', [row for row in unique.values() if graphics_api != 'Vulkan' or row['coverageKind'] == 'original-native']


def restore_project(project, inventory_path, cache, output, preserved_sources=None, graphics_api="Vulkan"):
    if graphics_api not in ("Vulkan", "GLES3"):
        raise ValidationError("Original Campaign graphics backend is unsupported.")
    # Bound inputs can be regenerated between invocations in one builder
    # process; cache within one immutable reconstruction operation only.
    _program_features.cache_clear()
    _native_light_field.cache_clear()
    recovery_module.cache_clear()
    generator_paths = (Path(__file__), Path(integer_bits.__file__), Path(load_bounds.__file__),
                       Path(__file__).resolve().parents[1] / 'quest-builder/full_shaders.py')
    generator_hashes = {path.name: sha256(path) for path in generator_paths}
    project, cache, output = Path(project).resolve(), Path(cache).resolve(), Path(output).resolve()
    inventory = json.loads(Path(inventory_path).read_text())
    if inventory.get('blockedShaderCount') or inventory.get('errors'):
        raise ValidationError('Original shader instruction/interface recovery has unresolved failures.')
    if output == project or project in output.parents or output in project.parents:
        raise ValidationError('Shader reconstruction requires a disjoint private overlay.')
    preserved_sources = preserved_sources or {}
    output.mkdir(parents=True, exist_ok=True)
    native = recovery_module()
    includes, programs = {}, {}
    for shader in inventory['shaders']:
        if not shader.get('allOriginalInstructionsExtracted') or not shader.get('allOriginalInterfacesBound'):
            raise ValidationError('Original shader bank lacks complete instruction/binding proof.')
        for row in shader['variants']:
            key = (row['originalDxbcSha256'], row['originalInterfaceSha256'])
            if key in includes:
                continue
            bound = Path(row['boundHlslPath'])
            if sha256(bound) != row['boundHlslSha256']:
                raise ValidationError('Bound original shader bytes changed before reconstruction.')
            original = cache / 'translated' / (key[0] + '.dxbc')
            if sha256(original) != key[0]:
                raise ValidationError('Native stage signature bytes differ from their original DXBC identity.')
            _, chunks = native.dxbc_container(original.read_bytes())
            input_signature = native.signature(chunks[b'ISGN']) if b'ISGN' in chunks else []
            output_signature = native.signature(chunks[b'OSGN']) if b'OSGN' in chunks else []
            def prior_signature(values):
                return [{k: v for k, v in signature.items() if k != 'readWriteMask'} for signature in values]
            if prior_signature(input_signature) != prior_signature(row['originalInputSignature']) or prior_signature(output_signature) != prior_signature(row['originalOutputSignature']):
                raise ValidationError('Original stage semantics differ from the recovered native inventory.')
            portable, sampling_adapters = native.portable_sampling_interface(bound.read_text())
            wrapped = native.stereo_wrapper(portable, row['stage'], row.get('outputInterfaceAdapters', []),
                                            input_signature, output_signature, graphics_api=graphics_api)
            wrapped, load_proofs = load_bounds.restore(wrapped)
            wrapped, integer_proof = integer_bits.restore(wrapped)
            path = Path('Assets/QuestOriginalCampaign/ShaderPrograms') / (key[0] + '-' + key[1] + '.hlsl')
            target = output / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(wrapped)
            includes[key] = str(path)
            programs[str(path)] = {'assetPath': str(path), 'originalDxbcSha256': key[0], 'originalInterfaceSha256': key[1],
                                   'sourceSha256': sha256(target), 'boundHlslSha256': row['boundHlslSha256'],
                                   'outputInterfaceAdapters': row.get('outputInterfaceAdapters', []),
                                   'samplingInterfaceAdapters': sampling_adapters,
                                   'originalInputSignature': input_signature, 'originalOutputSignature': output_signature,
                                   'integerCarrierProof': integer_proof, 'textureLoadProofs': load_proofs}
    shaders = []
    for shader in inventory['shaders']:
        form = json.loads((cache / 'forms' / (shader['originalParsedFormSha256'] + '.json')).read_text())
        source, variants = shader_source(form, shader, cache, includes, graphics_api)
        retained = preserved_sources.get(shader['guid'])
        target = output / shader['assetPath']
        target.parent.mkdir(parents=True, exist_ok=True)
        if retained:
            original = project / shader['assetPath']
            if sha256(original) != retained['sourceSha256'] or not retained.get('originalProvenance'):
                raise ValidationError('Retained original shader source contract is incomplete or changed.')
            shutil.copy2(original, target)
        else:
            target.write_text(source)
        meta = project / (shader['assetPath'] + '.meta')
        if not meta.is_file() or re.search(r'(?m)^guid: ' + shader['guid'] + r'$', meta.read_text()) is None:
            raise ValidationError('Original shader GUID/meta changed before private reconstruction.')
        shutil.copy2(meta, target.with_name(target.name + '.meta'))
        shaders.append({**{key: shader[key] for key in ('guid', 'assetPath', 'originalName', 'originalSerializedFile', 'originalPathId')},
                        'sourceSha256': sha256(target), 'variants': variants,
                        'sourceRestoration': 'retained-source-contract' if retained else 'exact-original-dxbc',
                        'retainedSourceContract': retained})
    manifest = {'schema': 1, 'scope': 'campaign-compiler', 'graphicsApi': graphics_api,
                'compilerPlatform': 'Vulkan' if graphics_api == 'Vulkan' else 'GLES3x',
                'originalDepthConvention': 'D3D-reversed-Z' if graphics_api == 'Vulkan' else 'GLES-probe-only', 'requiredShaderCount': len(shaders),
                'requiredMaterialCount': len(inventory['materials']), 'shaders': shaders,
                'materials': inventory['materials'], 'programs': list(programs.values()), 'originalPixelParityVerified': False}
    if generator_hashes != {path.name: sha256(path) for path in generator_paths}:
        raise ValidationError('Shader generator changed during reconstruction; rebuild from stable tooling.')
    manifest['sourceGeneratorSha256'] = generator_hashes
    manifest['requiredOriginalNativeAliasCount'] = sum(row['coverageKind'] == 'original-native' for shader in shaders for row in shader['variants'])
    manifest['requiredSyntheticAliasCount'] = sum(row['coverageKind'] == 'quest-synthetic' for shader in shaders for row in shader['variants'])
    manifest['requiredHostRenderTargetCount'] = max([1, *[signature['semanticIndex'] + 1
        for shader in inventory['shaders'] for program in shader['variants']
        if program['stage'] == 'fragment' for signature in program['originalOutputSignature']
        if signature['semantic'].upper() == 'SV_TARGET']])
    receipt = output / 'QuestRecovery/campaign-shaders.json'
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(manifest, sort_keys=True, indent=2) + '\n')
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--inventory', type=Path, required=True)
    parser.add_argument('--cache', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--preserved-sources', type=Path)
    parser.add_argument('--graphics-api', choices=('Vulkan', 'GLES3'), default='Vulkan')
    args = parser.parse_args()
    retained = json.loads(args.preserved_sources.read_text()) if args.preserved_sources else None
    result = restore_project(args.project, args.inventory, args.cache, args.output, retained, args.graphics_api)
    print('Exact original shader sources: ' + str(len(result['shaders'])))


if __name__ == '__main__':
    main()
