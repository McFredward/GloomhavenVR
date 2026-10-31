"""Conservative offline roles from every original PCG MeshFilter use.

Roles certify a mesh family, not permission to disable a live renderer. Runtime
still checks native tile/wall ownership, mutable geometry, materials and fades.
"""
import re

# These native scripts only manage rendering quality/materials/shadows. Any
# unreviewed script on a mesh's actual ancestor chain makes the family protected.
RENDER_SCRIPTS = frozenset((
    'MaterialLoader', 'DetailsDisabler', 'ImportantObjectsShadowsDisabler',
    'PropObjectsShadowsDisabler', 'DetailLevelDisableProvider',
    'ActivateWallFadeInGame', 'AutomaticLOD', 'Simplifier',
    'EntranceUndergroundDisablerProvider',
))
PROTECTED_WORDS = re.compile(
    r'door|water|lava|toxic|sludge|hotcoals|puddle|actor|character|hero|monster|'
    r'obst|destruct|chest|treasure|prop_scen|doorlock|effect|(?:^|_)fx(?:_|$)', re.I)
DECORATION_WORDS = re.compile(
    r'grass|grassy|tree|bush|plants|vines|ivy|roots|leaves|fern|shrub|reed|flower|'
    r'foliage|geranium|moss|clutter|scatter|debris|skull|bone|paper|pages|parchment|'
    r'scroll|cup|vase|urn|barrel|bottle|candle|banner|carpet|curtain|cobweb|wallchains|'
    r'book|plate|flask|inkpot|potion|(?:^|_)detail(?:_|$)', re.I)
FLOOR_WORDS = re.compile(r'(?:^|_)floor(?:_|hex|base|basic|$)|underfloor|(?:^|_)slab(?:_|$)', re.I)
STRUCTURE_WORDS = re.compile(
    r'wall|pillar|rock|terrain|cliff|slope|stairs|arch|beam|ceiling|roof|bridge|'
    r'shelf|shelves|stoneblock|platform|balustrade|column', re.I)


def family_candidate(name):
    return bool(FLOOR_WORDS.search(name) or STRUCTURE_WORDS.search(name))


def architectural_ornament(name, uses):
    """Closed separate ornament families; never a complete wall/floor/furniture.

    The runtime still proves a retained collision core/native tile or wall and
    refuses grabbables. The catalog cannot authorize an invisible collider.
    """
    candidate = bool(re.fullmatch(r'WallTendril_\d+[a-z]', name)
                     or re.fullmatch(r'WallFeature_\w+_Frame', name)
                     or re.fullmatch(r'(?:CR_Dungeon_|TO_SB02_)?WallTop(?:_\w+)?', name)
                     or re.fullmatch(r'EN_CR_Wall_TrimBasic_\d+', name)
                     or re.fullmatch(r'CR_ST_Shelf_(?:Floor_)?Pot_Detail', name)
                     or re.fullmatch(r'CR_OS_Floor_Detail_\d+(?:_V\d+)?', name))
    if not candidate or not uses:
        return False
    for use in uses:
        if use.get('unresolved') or len(use['route']) < 2:
            return False
        if any(script not in RENDER_SCRIPTS for script in use['scripts']):
            return False
        if PROTECTED_WORDS.search('/'.join(use['route'])):
            return False
        if 'MeshRenderer' not in use['leafComponents']:
            return False
        if any(kind in ('Animator', 'Animation', 'Rigidbody', 'SkinnedMeshRenderer', 'ParticleSystem',
                        'ParticleSystemRenderer', 'TrailRenderer', 'LineRenderer', 'Light')
               for kind in use['components']):
            return False
    return True


def classify(name, uses):
    """Fail closed on a protected/unknown use, including reuse outside a floor.

    An enclosing floor prefab is insufficient: its separate water, grass or
    gameplay mesh children keep their original geometry. Conversely a shelf
    under a floor is structure, never the walkable floor core.
    """
    if not uses:
        return 'none', ['no-original-meshfilter-use']
    reasons = set()
    for use in uses:
        if use.get('unresolved'):
            reasons.add('unresolved-original-component')
        if any(script not in RENDER_SCRIPTS for script in use['scripts']):
            reasons.add('non-render-script')
        if any(kind in ('Animator', 'Animation', 'Rigidbody', 'SkinnedMeshRenderer', 'ParticleSystem',
                        'ParticleSystemRenderer', 'TrailRenderer', 'LineRenderer', 'Light')
               for kind in use['components']):
            reasons.add('animated-or-effect-ancestor')
        if PROTECTED_WORDS.search('/'.join(use['route'])):
            reasons.add('protected-original-ancestry')
        if 'MeshRenderer' not in use['leafComponents']:
            reasons.add('no-original-static-mesh-renderer')
    if PROTECTED_WORDS.search(name):
        reasons.add('protected-original-mesh')
    furniture_core = re.fullmatch(
        r'CR_ST_Shelf_(?:Alchemy|Books|Scrolls|Skulls|Pots|UrnsPots|Urns)_\d+(?:_Seg_\d+)?', name)
    if DECORATION_WORDS.search(name) and furniture_core is None:
        reasons.add('separate-detail-or-effect-mesh')
    # FloorShelf is architectural furnishing, not the tile surface itself.
    if FLOOR_WORDS.search(name) and not STRUCTURE_WORDS.search(name):
        role = 'floor'
    elif STRUCTURE_WORDS.search(name):
        role = 'structure'
    else:
        reasons.add('unclassified-original-geometry')
        role = 'none'
    if reasons:
        return 'none', sorted(reasons)
    return role, []
