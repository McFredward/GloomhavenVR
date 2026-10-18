#!/usr/bin/env bash
# Build a reproducible isolated public-OpenVR quality lease child; no Valve code.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
output="${1:-$root/src/GloomhavenVR/Assets/SteamLink/stream-quality.exe}"
compiler="${MINGW_CC:-x86_64-w64-mingw32-gcc}"
"$compiler" -std=c11 -Os -Wall -Wextra -Werror -municode \
  -Wl,--no-insert-timestamp,--nxcompat,--dynamicbase -s \
  "$root/tools/steamlink-stream-quality.c" -o "$output"
python3 -B - "$root" "$output" "$compiler" <<'PY'
import hashlib,json,pathlib,shutil,struct,subprocess,sys
root=pathlib.Path(sys.argv[1]);output=pathlib.Path(sys.argv[2]);compiler=pathlib.Path(shutil.which(sys.argv[3])).resolve()
b=output.read_bytes();offset=struct.unpack_from('<I',b,60)[0]
assert b[:2]==b'MZ' and b[offset:offset+4]==b'PE\0\0'
assert struct.unpack_from('<H',b,offset+4)[0]==0x8664 and struct.unpack_from('<H',b,offset+24)[0]==0x20b
record={'schema':'ghvr-steamlink-stream-quality-build-v1','source':'tools/steamlink-stream-quality.c','source_sha256':hashlib.sha256((root/'tools/steamlink-stream-quality.c').read_bytes()).hexdigest(),'script_sha256':hashlib.sha256((root/'scripts/build-steamlink-stream-quality.sh').read_bytes()).hexdigest(),'compiler':compiler.name,'compiler_sha256':hashlib.sha256(compiler.read_bytes()).hexdigest(),'compiler_version':subprocess.check_output([str(compiler),'--version'],text=True).splitlines()[0],'flags':['-std=c11','-Os','-Wall','-Wextra','-Werror','-municode','-Wl,--no-insert-timestamp,--nxcompat,--dynamicbase','-s'],'output_bytes':len(b),'output_sha256':hashlib.sha256(b).hexdigest(),'windows_or_proton_executed':False,'hardware_verified':False}
output.with_name(output.stem+'-provenance.json').write_text(json.dumps(record,indent=2)+'\n')
print(output,len(b),record['output_sha256'])
PY
