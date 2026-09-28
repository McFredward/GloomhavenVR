#!/usr/bin/env python3
"""Keep retired town-stand cloth static without touching figure garment physics."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOWN = ROOT / 'src/GloomhavenVR/WorldUI/TownServices'


def check(condition: bool, label: str) -> None:
    if not condition:
        raise AssertionError(label)


def retired_author_source(source: str) -> bool:
    forbidden = (
        'TownServiceCloth', 'TickClothAuthor', 'TickClothObserver',
        'HasWorkspaceCloth', 'WorkspaceClothFirst', 'WorkspaceClothSecond',
        'SetWorkspaceCloth(', 'AddComponent<Cloth>', 'GetComponent<Cloth>',
    )
    return not any(token in source for token in forbidden)


check(not (TOWN / 'TownServiceCloth.cs').exists(), 'town cloth simulation source remains')
author_source = '\n'.join(path.read_text() for path in TOWN.glob('*.cs'))
check(retired_author_source(author_source), 'town stand still constructs or publishes cloth behavior')
check('published.HasCloth = false;' in (TOWN / 'TownServicePopulation.cs').read_text(),
      'resident still authors the historical cloth tail')
for token in ('TownServiceCloth', 'TickClothAuthor', 'SetWorkspaceCloth(', 'AddComponent<Cloth>'):
    check(not retired_author_source(author_source + '\n' + token),
          f'negative control missed {token}')

bundle = ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Prefabs'
for name, count in (('TownMerchant.prefab', 0), ('TownPriestess.prefab', 2),
                    ('TownEnchantress.prefab', 1)):
    raw = (bundle / name).read_text()
    check(raw.count('m_Name: ClothRunner_') == count,
          f'{name} lost its visible authored fabric mesh')

print('TOWN_STAND_FABRIC_STATIC_PASS: 3 original drapes retained, 0 simulation paths, 4 negative controls')
