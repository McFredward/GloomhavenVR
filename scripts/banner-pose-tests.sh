#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$repo_root/tests/GloomhavenVR.BannerPoseTests/GloomhavenVR.BannerPoseTests.csproj"
source_file="$repo_root/src/GloomhavenVR/WorldUI/MapRoom/GuildmasterBannerBorrow.cs"
python3 - "$repo_root" <<'PY'
from pathlib import Path
import sys
source=(Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/MapRoom/GuildmasterDestinations.cs').read_text()
reconcile=source[source.index('    private static void ReconcileBanner('):source.index('    private static void ReleaseBanner(')]
release=source[source.index('    private static void ReleaseBanner('):source.index('    private static void ReleaseBanner(')+1700]
def bound(s):
    return all(part in s for part in ['BannerBorrow.Owns(banner)', 'BannerBorrow.IsUnder(floated.transform)',
        'ReleaseBanner("a different destination took over");', 'BannerBorrow.Borrow(banner, floated.transform);'])
assert bound(reconcile)
assert reconcile.index('ReleaseBanner("a different destination took over");') < reconcile.index('BannerBorrow.Borrow(banner, floated.transform);')
assert 'BannerBorrow.Release();' in release and 'Transform? banner = Banner();' not in release
conversion=(Path(sys.argv[1])/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.8.Convert.cs').read_text()
assert conversion.index('PrepareBannerForConversion(window);') < conversion.index('panel = CanvasConversion.Convert(')
assert 'new(GuildmasterBannerFrame.Native)' in source
assert 'BannerBorrow.ObserveNative(banner);' in source
assert 'BannerBorrow.Reset();' in source
assert not bound(reconcile.replace('BannerBorrow.IsUnder(floated.transform)','true'))
print('Banner pose: 7 integration bindings and 1 binding negative passed.')
PY
dotnet run --project "$project" --configuration Release
mutation_dir="$(mktemp -d)"
trap 'rm -rf "$mutation_dir"' EXIT
for mutation in native-handoff scale parent release-latch canonical local-replay native-frame; do
    python3 - "$source_file" "$mutation_dir/Mutated.cs" "$mutation" <<'PY'
from pathlib import Path
import sys
source=Path(sys.argv[1]).read_text()
changes={
 'native-handoff': ('if (banner == null) return;', 'if (banner == null || banner.parent != host) return;'),
 'scale': ('banner.localScale = local.lossyScale;', 'banner.localScale = Vector3.one;'),
 'parent': ('host != null && banner.parent == host', 'host != null'),
 'release-latch': ('_banner = null;', '// Mutation: ownership not cleared.'),
 'canonical': ('if (ReferenceEquals(_canonicalBanner, banner)) return;', '// Mutation: forget the original native observation.'),
 'local-replay': ('Matrix4x4 local = _nativeFrame(banner.parent).inverse * world;', 'Matrix4x4 local = _canonicalLocal;'),
 'native-frame': ('Matrix4x4 local = _nativeFrame(banner.parent).inverse * world;', 'Matrix4x4 local = (banner.parent != null ? banner.parent.localToWorldMatrix : Matrix4x4.identity).inverse * world;'),
}
old,new=changes[sys.argv[3]]
assert source.count(old)==1
Path(sys.argv[2]).write_text(source.replace(old,new))
PY
    if dotnet run --project "$project" --configuration Release --property:BannerSource="$mutation_dir/Mutated.cs" > "$mutation_dir/output" 2>&1; then
        echo "FAIL: banner pose mutation survived: $mutation" >&2; exit 1
    fi
    case "$mutation" in
        native-handoff) expected='temple return survives either window restore order position' ;;
        scale) expected='temple native coordinate frame stays stable scale' ;;
        parent) expected='release preserves parent and sibling chosen by native mode' ;;
        release-latch) expected='reset permits a new session baseline position' ;;
        canonical) expected='already-converted temple does not become canonical position' ;;
        local-replay) expected='temple return survives either window restore order position' ;;
        native-frame) expected='temple native coordinate frame stays stable position' ;;
    esac
    if ! rg -qF "Unhandled exception. System.Exception: $expected" "$mutation_dir/output"; then
        cat "$mutation_dir/output"; exit 1
    fi
    echo "Banner pose negative rejected: $mutation"
done
