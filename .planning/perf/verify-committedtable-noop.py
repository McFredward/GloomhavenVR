"""Prove the CommittedTable extraction is a purely syntactic no-op.

Take every wall-fade source file as it stands now, undo the rename mechanically
(_live.Segments -> _segments, cref="CommittedTable.Segments" -> cref="_segments"), and diff
the result against the SAME file at the parent commit. If the extraction is a rename and
nothing else, the only surviving differences are:
  * deletions of the field declarations (and their doc blocks) that moved into CommittedTable
  * the four `private` -> `internal` keywords on ArchRect / WaterRect / UnionEntry / UnionHit,
    which the C# accessibility rules force once those types are named by a field of a nested
    class (CS0052) and which change nothing at runtime: all four are still nested inside a
    private FadeDriver.
Anything else in that diff is a real behaviour change hiding inside a refactor, which is
exactly what this script exists to make impossible to miss.
"""
import os, re, subprocess, sys, difflib

CORE = 'src/GloomhavenVR/Core/'
MAP = {
    '_segments': 'Segments', '_splitAnchors': 'SplitAnchors', '_roomBounds': 'RoomBounds',
    '_roomFloorY': 'RoomFloorY', '_roomFloorAnchored': 'RoomFloorAnchored',
    '_roomLabels': 'RoomLabels', '_allSamples': 'AllSamples', '_sampleYMin': 'SampleYMin',
    '_sampleYMax': 'SampleYMax', '_roomsAnchored': 'RoomsAnchored',
    '_builtRoomCount': 'BuiltRoomCount', '_archRects': 'ArchRects',
    '_mountedUnion': 'MountedUnion', '_unionOwners': 'UnionOwners', '_waterRects': 'WaterRects',
    '_boardVolume': 'BoardVolume', '_boardVolumeValid': 'BoardVolumeValid',
    '_boardCrestWU': 'BoardCrestWU', '_boardFloorY': 'BoardFloorY',
    '_roomSampleStart': 'RoomSampleStart', '_roomSampleCount': 'RoomSampleCount',
    '_cornerPieces': 'CornerPieces', '_propUnitAnchors': 'PropUnitAnchors',
    '_propUnitRootMemo': 'PropUnitRootMemo', '_boardVolumeRooms': 'BoardVolumeRooms',
    '_prepBoardProbePos': 'PrepBoardProbePos', '_prepBoardProbeValid': 'PrepBoardProbeValid',
}
REV = {v: k for k, v in MAP.items()}

files = sorted(f for f in os.listdir(CORE)
               if f.startswith('WallSegmentFade') and f.endswith('.cs')
               and f != 'WallSegmentFade.CommittedTable.cs')

unexpected = 0
for fn in files:
    cur = open(CORE + fn).read()
    for prop, field in REV.items():
        cur = cur.replace('cref="CommittedTable.%s"' % prop, 'cref="%s"' % field)
        cur = cur.replace('_live.' + prop, field)
    try:
        base = subprocess.check_output(['git', 'show', 'cf24af90:' + CORE + fn],
                                       text=True)
    except subprocess.CalledProcessError:
        print('NEW FILE (no baseline): ' + fn)
        continue
    if base == cur:
        print('%-42s IDENTICAL after normalising the rename' % fn)
        continue
    diff = list(difflib.unified_diff(base.split('\n'), cur.split('\n'),
                                     fromfile='cf24af90/' + fn, tofile='normalised/' + fn,
                                     lineterm='', n=0))
    print('%-42s %d diff line(s):' % (fn, len(diff)))
    for line in diff:
        if line.startswith(('---', '+++', '@@')):
            continue
        kind = 'DEL' if line.startswith('-') else 'ADD'
        body = line[1:]
        ok = False
        if kind == 'DEL':
            # a moved declaration, or its doc block
            if body.strip().startswith('///'):
                ok = True
            elif re.match(r'\s*private\s.*\b(' + '|'.join(map(re.escape, MAP)) + r')\b', body):
                ok = True
            elif re.match(r'\s*private\s+(struct|sealed class|readonly struct)\s+'
                          r'(ArchRect|WaterRect|UnionEntry|UnionHit|CornerPiece)\b', body):
                ok = True
        else:
            if re.match(r'\s*internal\s+(struct|sealed class|readonly struct)\s+'
                        r'(ArchRect|WaterRect|UnionEntry|UnionHit|CornerPiece)\b', body):
                ok = True
        if not ok:
            unexpected += 1
            print('    UNEXPECTED %s: %s' % (kind, body))
        else:
            print('    expected   %s: %s' % (kind, body.strip()[:96]))

print()
print('UNEXPECTED DIFFERENCES:', unexpected)
sys.exit(1 if unexpected else 0)
