"""Stable original-asset import identity and pre-Editor Android settings.

Only source-proven Unity 2021 PlayerSettings fields are changed. The Editor
remains authoritative for gameplay, stereo policy and the remaining build setup.
"""
import re
from pathlib import Path

from storage import BuildError, digest, value_hash, write_json, _ordinary_owned

APIS = {'game': ('Vulkan', b'15000000'), 'startup': ('OpenGLES3', b'0b000000'), 'probe': ('OpenGLES3', b'0b000000')}
RECEIPT = 'QuestStartupEvidence/android-import-settings.json'


def contract(target):
    if target not in APIS: raise BuildError('Unknown Quest import target.')
    return {'schema': 1, 'target': 'Android', 'graphicsApi': APIS[target][0], 'colorSpace': 'Linear'}


def workspace_key(inputs, target):
    game = inputs['game']
    if not re.fullmatch(r'[0-9a-f]{64}', game['key']): raise BuildError('Original-asset workspace requires a valid game hash.')
    if game['unityVersion'] != '2021.3.5f1': raise BuildError('Original-asset workspace requires the audited Unity version.')
    return value_hash({'schema': 1, 'purpose': 'Quest owned original-asset workspace', 'gameKey': game['key'],
                       'unityVersion': game['unityVersion'], 'scope': target, 'import': contract(target)})


def patch_settings(raw, target):
    newline = b'\r\n' if b'\r\n' in raw else b'\n'
    color = re.compile(rb'(?m)^(  m_ActiveColorSpace: )[01](\r?)$')
    if len(color.findall(raw)) != 1: raise BuildError('Recovered Unity PlayerSettings has no unique audited color-space field.')
    changed = color.sub(rb'\g<1>1\2', raw)
    field = re.compile(rb'(?ms)^  m_BuildTargetGraphicsAPIs:(?: \[\])?\r?\n(?P<body>.*?)(?=^  [A-Za-z_][A-Za-z0-9_]*:)')
    matches = list(field.finditer(changed))
    if len(matches) != 1: raise BuildError('Recovered Unity PlayerSettings has no unique graphics-API collection.')
    match = matches[0]; body = match['body']
    entries = list(re.finditer(rb'(?m)^  - m_BuildTarget: ([^\r\n]+)\r?\n', body))
    android = [(row, entries[index + 1].start() if index + 1 < len(entries) else len(body))
               for index, row in enumerate(entries) if row[1] == b'AndroidPlayer']
    if len(android) > 1: raise BuildError('Recovered PlayerSettings contains duplicate Android graphics-API entries.')
    if android:
        start, end = android[0]; entry = body[start.start():end]
        for pattern, replacement in ((rb'(?m)^(    m_APIs: )[0-9a-fA-F]*(\r?)$', APIS[target][1]),
                                     (rb'(?m)^(    m_Automatic: )[01](\r?)$', b'0')):
            if len(re.findall(pattern, entry)) != 1: raise BuildError('Android graphics-API entry has an unsupported serialized shape.')
            entry = re.sub(pattern, lambda row: row[1] + replacement + row[2], entry)
        body = body[:start.start()] + entry + body[end:]
    else:
        body += b'  - m_BuildTarget: AndroidPlayer' + newline + b'    m_APIs: ' + APIS[target][1] + newline + b'    m_Automatic: 0' + newline
    replacement = b'  m_BuildTargetGraphicsAPIs:' + newline + body
    return changed[:match.start()] + replacement + changed[match.end():]


def stage(project, target):
    settings = _ordinary_owned(Path(project) / 'ProjectSettings/ProjectSettings.asset')
    if not settings.is_file():
        if target == 'game': raise BuildError('Full Campaign recovery lacks original PlayerSettings; no default project can replace it.')
        return None
    before = digest(settings); raw = settings.read_bytes(); changed = patch_settings(raw, target)
    if changed != raw: settings.write_bytes(changed)
    receipt = project / RECEIPT
    write_json(receipt, {**contract(target), 'source': 'QuestBuild.ConfigureAndroid audited serialized Unity2021 fields',
                        'assetPath': 'ProjectSettings/ProjectSettings.asset', 'beforeSha256': before,
                        'sha256': digest(settings), 'changed': changed != raw, 'unityImportTimingVerified': False})
    return receipt
