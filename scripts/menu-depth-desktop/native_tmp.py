"""Read-only original TMP font export and canonical shader fixture preparation."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True


def export_font(root, output):
    import UnityPy
    from UnityPy.classes import PPtr
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    spec = importlib.util.spec_from_file_location('native_profile', root / 'scripts/menu726-native-profile.py')
    profile = importlib.util.module_from_spec(spec); spec.loader.exec_module(profile)
    generator = TypeTreeGenerator('2021.3.5f1')
    generator.load_local_dll_folder(str(root / 'ressources/GH_Data/Managed'))
    scene = root / 'ressources/GH_Data/level9'
    environment = UnityPy.load(str(scene)); environment.typetree_generator = generator
    objects = {item.path_id: item for item in environment.objects}

    def pointer(value, owner):
        return PPtr(m_FileID=value['m_FileID'], m_PathID=value['m_PathID'], assetsfile=owner.assets_file)

    button = objects[18225]
    text = pointer(profile.authored_tree(button)['buttonText'], button).deref()
    text_data = profile.authored_tree(text)
    font = pointer(text_data['m_fontAsset'], text).deref()
    font_data = profile.authored_tree(font)
    material = pointer(text_data['m_sharedMaterial'], text).deref()
    material_data = profile.authored_tree(material)
    shader = pointer(material_data['m_Shader'], material).deref().read_typetree()['m_ParsedForm']['m_Name']
    assert shader == 'TextMeshPro/Distance Field', 'Original native font shader changed'
    atlas = pointer(font_data['m_AtlasTextures'][0], font).read()
    stream = atlas.m_StreamData
    stream_source = (root / 'ressources/GH_Data' / stream.path).resolve()
    assert stream.path == 'resources.assets.resS', 'Original atlas streaming source changed'
    with stream_source.open('rb') as source:
        source.seek(stream.offset); consumed = source.read(stream.size)
    assert len(consumed) == stream.size, 'Original atlas streaming span is incomplete'
    output.mkdir(parents=True)
    atlas.image.save(output / 'font-atlas.png')
    links = {'m_Script', 'm_GameObject', 'material', 'spriteSheet', 'm_SourceFontFile', 'm_AtlasTextures',
             'm_SourceFontFile_EditorRef', 'fallbackFontAssetTable', 'm_FallbackFontAssetTable'}
    (output / 'font.json').write_text(json.dumps({k: v for k, v in font_data.items() if k not in links}, indent=2) + '\n')
    saved = material_data['m_SavedProperties']
    (output / 'font-material.json').write_text(json.dumps({
        'original': profile.object_id(material), 'shader': shader,
        'keywords': material_data.get('m_ValidKeywords', []), 'renderQueue': material_data['m_CustomRenderQueue'],
        'floats': [{'name': key, 'value': value} for key, value in saved['m_Floats']],
        'colors': [{'name': key, 'value': value} for key, value in saved['m_Colors']]}, indent=2) + '\n')
    # The confirmation witness uses the game's actual button print as well as
    # its static SDF font. Its listener is an explicit presentation-test port;
    # no original gameplay controller is instantiated here.
    button_image = objects[17670]
    button_data = profile.authored_tree(button_image)
    original_sprite = pointer(button_data['m_Sprite'], button_image).read()
    original_sprite.image.save(output / 'button-sprite.png')
    (output / 'button-sprite.json').write_text(json.dumps({
        'original': profile.object_id(original_sprite.object_reader),
        'image': profile.object_id(button_image), 'imageType': button_data['m_Type'],
        'border': profile.vector(original_sprite.m_Border, 'xyzw'),
        'pivot': profile.vector(original_sprite.m_Pivot, 'xy'),
        'ppu': original_sprite.m_PixelsToUnits,
        'size': [original_sprite.m_Rect.width, original_sprite.m_Rect.height]}, indent=2) + '\n')
    (output / 'original-provenance.json').write_text(json.dumps({
        'font': profile.object_id(font), 'atlas': profile.object_id(atlas.object_reader),
        'atlas_stream': {'source': str(stream_source), 'offset': stream.offset, 'bytes': stream.size,
                         'sha256': hashlib.sha256(consumed).hexdigest()},
        'material': profile.object_id(material), 'nativeText': profile.object_id(text),
        'decoder': {'UnityPy': UnityPy.__version__, 'Unity': '2021.3.5f1'},
        'ports': 'Original static font/glyph/atlas/material data. Literal test text; no gameplay controllers.'}, indent=2) + '\n')
    decoder_files = {Path(sys.executable).resolve()}
    for module in list(sys.modules.values()):
        for attribute in ('__file__',):
            filename = getattr(module, attribute, None)
            if filename and Path(filename).is_file(): decoder_files.add(Path(filename).resolve())
    (output / 'decoder-pins.json').write_text(json.dumps({
        'python_version': sys.version,
        'files': [{'path': str(p), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                  for p in sorted(decoder_files)]}, indent=2) + '\n')
    (output / 'pins.json').write_text(json.dumps({p.name: hashlib.sha256(p.read_bytes()).hexdigest()
        for p in output.iterdir() if p.is_file()}, indent=2) + '\n')


def inputs(root):
    shaders = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
    return [Path(__file__), root / 'scripts/menu726-native-profile.py',
            root / 'ressources/GH_Data/level9', root / 'ressources/GH_Data/resources.assets',
            root / 'ressources/GH_Data/resources.assets.resS',
            *sorted((root / 'ressources/GH_Data/Managed').glob('*.dll')),
            *[shaders / name for name in ('ActorBarSDF.shader', 'source-provenance.json', 'TMPro.cginc', 'TMPro_Properties.cginc')]]


def prepare(root, run, project):
    output = run / 'native-tmp-inputs'
    command = [str(Path.home() / 'unitypy-venv/bin/python'), str(Path(__file__)), '--export', str(root), str(output)]
    result = subprocess.run(command, capture_output=True, text=True, timeout=180)
    (run / 'native-tmp-export.log').write_text(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError('Native font export failed: ' + str(run / 'native-tmp-export.log'))
    shaders = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
    source = (shaders / 'ActorBarSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarSDF"', 'Shader "TextMeshPro/Distance Field"', 1)
    source = source.replace('\n\t\t\t#include "ActorBarForeground.cginc"', '', 1)
    source = re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);', '', source)
    source = re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;', '', source)
    source = re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);', '', source)
    source = source.replace('\n\t\t\t\tComputeScreenPos(vPosition),', '')
    source = source.replace('Fallback "GloomhavenVR/ActorBarMobileSDF"', 'Fallback "TextMeshPro/Mobile/Distance Field"')
    record = next(item for item in json.loads((shaders / 'source-provenance.json').read_text())['sources'] if item['file'] == 'ActorBarSDF.shader')
    original = source
    for pragma in record['retained_feature_pragmas']:
        before = pragma.strip().replace('shader_feature', 'multi_compile')
        assert original.count(before) == 1, 'Native shader pragma binding drift'
        original = original.replace(before, pragma.strip())
    assert hashlib.sha256(original.encode()).hexdigest() == record['normalized_original_sha256'], 'Canonical original TMP shader differs'
    destination = project / 'Assets/Resources/OriginalShaders'; destination.mkdir(parents=True)
    (destination / 'NativeSDF.shader').write_text(source)
    for name in ('TMPro.cginc', 'TMPro_Properties.cginc'):
        shutil.copyfile(shaders / name, destination / name)
    (run / 'canonical-tmp-provenance.json').write_text(json.dumps({
        'normalized_original_sha256': record['normalized_original_sha256'],
        'runtime': 'Canonical original SDF source with retained keyword branches, original game TMP DLL/font/atlas/material. Windows D3D11 programs are not executed.'}, indent=2) + '\n')


if __name__ == '__main__':
    assert len(sys.argv) == 4 and sys.argv[1] == '--export'
    export_font(Path(sys.argv[2]), Path(sys.argv[3]))
