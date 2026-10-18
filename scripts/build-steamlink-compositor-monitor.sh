#!/usr/bin/env bash
# Rebuild our read-only child from the public companion project; no Valve binaries.
set -euo pipefail
if [[ $# -lt 2 || $# -gt 3 ]]; then
    echo "Usage: $0 <steam-link-native-passthrough-checkout> <reviewed-openvr-header-directory> [output-exe]" >&2
    exit 2
fi
monitor_root="$(cd "$(dirname "$0")/.." && pwd)"
python3 -B - "$monitor_root" "$1" "$2" "${3:-$monitor_root/src/GloomhavenVR/Assets/SteamLink/compositor-state-probe.exe}" <<'PY'
import hashlib,json,pathlib,shutil,struct,subprocess,sys,tempfile

root=pathlib.Path(sys.argv[1]);source=pathlib.Path(sys.argv[2]).resolve()
sdk=pathlib.Path(sys.argv[3]).resolve();output=pathlib.Path(sys.argv[4]).absolute()
names=['pc/compositor-state-probe.cpp','pc/compositor-state-probe-core.h',
       'pc/compositor-state-probe-test.cpp','pc/compositor-state-probe-build.sh']
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
pins={name:digest(source/name) for name in names}
commit=subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()
if subprocess.check_output(['git','-C',str(source),'status','--porcelain','--',*names],text=True).strip():
    raise SystemExit('Commit the reviewed companion sources before packaging this resource.')
compiler=pathlib.Path(shutil.which('x86_64-w64-mingw32-g++')).resolve()
# The companion build checks the exact public-header hash, runs its portable
# getter/serialization fixture, and builds Win64. It never launches OpenVR.
with tempfile.TemporaryDirectory(prefix='ghvr-compositor-build-') as scratch:
    built=pathlib.Path(scratch)
    subprocess.run(['bash',str(source/names[-1]),str(sdk),str(built)],check=True)
    data=(built/'compositor-state-probe.exe').read_bytes()
    pe=struct.unpack_from('<I',data,60)[0]
    assert data[:2]==b'MZ' and data[pe:pe+4]==b'PE\0\0'
    assert struct.unpack_from('<H',data,pe+4)[0]==0x8664
    assert struct.unpack_from('<H',data,pe+24)[0]==0x20b
    assert pins=={name:digest(source/name) for name in names},'Sources changed during build'
    output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(data)
record={'schema':'ghvr-steamlink-compositor-monitor-build-v1',
        'source_repository':'https://github.com/McFredward/steam-link-native-passthrough',
        'source_commit':commit,'source_sha256':pins,
        'openvr_header_sha256':digest(sdk/'openvr_capi.h'),
        'script_sha256':digest(root/'scripts/build-steamlink-compositor-monitor.sh'),
        'compiler':compiler.name,'compiler_sha256':digest(compiler),
        'compiler_version':subprocess.check_output([str(compiler),'--version'],text=True).splitlines()[0],
        'output_bytes':len(data),'output_sha256':hashlib.sha256(data).hexdigest(),
        'windows_or_proton_executed':False,'hardware_verified':False,
        'behavior':'Debug-only read-only public OpenVR Background client; no settings or rendering changes'}
output.with_name('compositor-state-probe-provenance.json').write_text(json.dumps(record,indent=2)+'\n')
print(output,len(data),record['output_sha256'])
PY
