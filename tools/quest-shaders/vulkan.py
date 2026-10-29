"""Reflect and driver-compile unchanged actual Unity Android Vulkan stages.

This is a host development gate. It does not substitute host results for a
Quest image, and consumes real decoded bytes from the compiler receipt.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

from manifest import ValidationError, sha256, validate_receipt


class Module:
    def __init__(self, code):
        if len(code) < 20 or len(code) % 4:
            raise ValidationError('Actual Vulkan SPIR-V extent is invalid.')
        words = struct.unpack('<' + 'I' * (len(code) // 4), code)
        if words[0] != 0x07230203 or words[1] != 0x10000 or words[4] != 0:
            raise ValidationError('Actual Unity Vulkan bank requires SPIR-V1.0.')
        self.types, self.constants, self.variables, self.decorations, self.members = {}, {}, {}, {}, {}
        self.stage = None
        index = 5
        while index < len(words):
            length, op = words[index] >> 16, words[index] & 65535
            if length < 1 or length > len(words) - index:
                raise ValidationError('Actual Vulkan native instruction is truncated.')
            a = words[index + 1:index + length]
            minimum = {15: 3, 19: 1, 20: 1, 21: 3, 22: 2, 23: 3, 24: 3, 25: 8,
                       26: 1, 27: 2, 28: 3, 29: 2, 30: 1, 31: 2, 32: 3, 33: 2,
                       43: 3, 59: 3, 71: 2, 72: 3}.get(op, 0)
            if len(a) < minimum:
                raise ValidationError('Actual Vulkan native instruction operands are truncated.')
            if op == 15:
                if self.stage is not None or a[0] not in (0, 4):
                    raise ValidationError('Actual Vulkan graphics stage has no unique entry point.')
                self.stage = 1 if a[0] == 0 else 16
            elif 19 <= op <= 33: self.types[a[0]] = (op, a[1:])
            elif op == 43: self.constants[a[1]] = a[2:]
            elif op == 59: self.variables[a[1]] = (a[0], a[2])
            elif op == 71: self.decorations.setdefault(a[0], {})[a[1]] = a[2:]
            elif op == 72: self.members.setdefault((a[0], a[1]), {})[a[2]] = a[3:]
            index += length
        if self.stage is None: raise ValidationError('Actual Vulkan bank entry point is absent.')

    def descriptors(self):
        rows = []
        for identity, (pointer, storage) in self.variables.items():
            dec = self.decorations.get(identity, {})
            if 33 not in dec and 34 not in dec: continue
            if 33 not in dec or 34 not in dec: raise ValidationError('Incomplete actual Vulkan descriptor coordinates.')
            op, p = self.types[pointer]
            if op != 32 or p[0] != storage: raise ValidationError('Invalid actual Vulkan descriptor pointer.')
            target = p[1]; op, a = self.types[target]; count = 1
            if op == 28:
                n = self.constants.get(a[1])
                if n is None or len(n) != 1 or not 0 < n[0] <= 4096: raise ValidationError('Unknown actual Vulkan descriptor array extent.')
                count = n[0]; target = a[0]; op, a = self.types[target]
            if op == 26: kind = 0
            elif op == 25:
                if a[5] not in (1, 2): raise ValidationError('Unknown actual Vulkan image sampling use.')
                kind = (5 if a[5] == 2 else 4) if a[1] == 5 else (3 if a[5] == 2 else 2)
            elif op == 27: kind = 1
            elif op == 30:
                decorations = self.decorations.get(target, {})
                if 2 in decorations and storage == 2: kind = 6
                elif 3 in decorations or storage == 12: kind = 7
                else: raise ValidationError('Unknown actual Vulkan native descriptor block.')
            else: raise ValidationError('Unknown actual Vulkan descriptor resource type.')
            rows.append(dict(set=dec[34][0], binding=dec[33][0], kind=kind, count=count, stages=self.stage))
        return rows

    def interfaces(self, storage):
        result = []
        for identity, (pointer, actual_storage) in self.variables.items():
            if storage != actual_storage: continue
            _, p = self.types[pointer]; target = p[1]; op, a = self.types[target]
            if op == 30:
                for member, field in enumerate(a):
                    result.append((field, self.members.get((target, member), {})))
            else: result.append((target, self.decorations.get(identity, {})))
        return result

    def format(self, target):
        op, a = self.types[target]; width = 1
        if op == 23: width = a[1]; op, a = self.types[a[0]]
        if op not in (21, 22) or a[0] != 32 or width not in (1, 2, 3, 4):
            raise ValidationError('Actual Vulkan native location requires an audited 32-bit scalar/vector format.')
        # Exact Vulkan VkFormat enum, contiguous uint/sint/float triples.
        return (98, 101, 104, 107)[width - 1] + (2 if op == 22 else int(a[1])), width * 4

    def view_index(self):
        return any(d.get(11) == (4440,) for _, d in self.interfaces(1))


def request(vertex_path, fragment_path):
    vertex, fragment = Module(vertex_path.read_bytes()), Module(fragment_path.read_bytes())
    if vertex.stage != 1 or fragment.stage != 16: raise ValidationError('Actual Vulkan stage filenames have exchanged execution models.')
    descriptors = {}
    for row in vertex.descriptors() + fragment.descriptors():
        key = row['set'], row['binding']
        prior = descriptors.get(key)
        if prior:
            if (prior['kind'], prior['count']) != (row['kind'], row['count']): raise ValidationError('Actual Vulkan stages disagree on one native descriptor.')
            prior['stages'] |= row['stages']
        else: descriptors[key] = row
    attributes, offset = [], 0
    for target, dec in vertex.interfaces(1):
        if 30 in dec:
            format, size = vertex.format(target)
            attributes.append((dec[30][0], format, offset)); offset += size
    colors = {}
    for target, dec in fragment.interfaces(3):
        if 30 in dec:
            format, _ = fragment.format(target); colors[dec[30][0]] = format
    count = max(colors, default=-1) + 1
    if count > 8: raise ValidationError('Actual Vulkan driver proof exceeds witnessed native MRT limit.')
    lines = [f'{vertex_path} {fragment_path} {len(descriptors)} {len(attributes)} {count} {offset} {3 if vertex.view_index() else 0}']
    lines += [' '.join(map(str, (r['set'], r['binding'], r['kind'], r['count'], r['stages']))) for r in sorted(descriptors.values(), key=lambda r: (r['set'], r['binding']))]
    lines += [' '.join(map(str, attribute)) for attribute in attributes]
    lines += [str(colors.get(n, 109)) for n in range(count)]
    return '\n'.join(lines) + '\n'


def run(manifest_path, compiler_path, output, headers, icd):
    manifest_path, compiler_path, output = map(lambda p: Path(p).resolve(), (manifest_path, compiler_path, output))
    input = json.loads(manifest_path.read_text()); compiled = json.loads(compiler_path.read_text())
    if input.get('graphicsApi') != 'Vulkan': raise ValidationError('Actual Vulkan host requires its matching backend manifest.')
    validate_receipt(input, sha256(manifest_path), compiled)
    output.mkdir(parents=True, exist_ok=True)
    source = Path(__file__).with_name('vulkan_host.c'); binary = output / 'vulkan-graphics-host'
    subprocess.run(['cc', '-std=c11', '-O2', '-Wall', '-Wextra', '-I', str(Path(headers).resolve()), str(source), '-l:libvulkan.so.1', '-o', str(binary)], check=True)
    environment = {**os.environ, 'VK_ICD_FILENAMES': str(Path(icd).resolve())}
    rows = []; seen = {}; root = compiler_path.parent
    for row in compiled['programs']:
        vertex = root / row['vertexFile']; fragment = root / row['fragmentFile']; key = row['vertexSha256'], row['fragmentSha256']
        if sha256(vertex) != key[0] or sha256(fragment) != key[1]: raise ValidationError('Actual cooked Vulkan stage hash differs.')
        if key not in seen:
            for path in (vertex, fragment):
                result = subprocess.run(['spirv-val', '--target-env', 'vulkan1.0', str(path)], capture_output=True, text=True)
                if result.returncode: raise ValidationError('Actual native Vulkan validation failed: ' + result.stdout + result.stderr)
            case = output / (str(len(seen)) + '.txt'); case.write_text(request(vertex, fragment))
            actual = subprocess.run([str(binary), str(case)], env=environment, capture_output=True, text=True, timeout=120)
            if actual.returncode: raise ValidationError('Actual Vulkan native graphics pipeline failed: ' + actual.stderr + ' / ' + str(case))
            seen[key] = json.loads(actual.stdout)
        rows.append({k: row[k] for k in ('guid', 'subshader', 'pass', 'hardwareTier', 'stereo', 'keywords', 'vertexSha256', 'fragmentSha256')})
        if len(rows) % 100 == 0: print('Actual Vulkan driver aliases:', len(rows), 'distinct banks:', len(seen), flush=True)
    # Actual driver negative control: retain all instructions/resources but
    # change the fragment entry point name, making the requested main absent.
    vertex = root / compiled['programs'][0]['vertexFile']; fragment = root / compiled['programs'][0]['fragmentFile']; bad = bytearray(fragment.read_bytes())
    offset = 20
    while offset < len(bad):
        instruction = struct.unpack_from('<I', bad, offset)[0]; length, op = instruction >> 16, instruction & 65535
        if op == 15: bad[offset + 12:offset + 16] = b'bad\0'; break
        offset += length * 4
    broken = output / 'negative-fragment.spv'; broken.write_bytes(bad)
    case = output / 'negative.txt'; case.write_text(request(vertex, broken))
    rejected = subprocess.run([str(binary), str(case)], env=environment, capture_output=True, text=True, timeout=120).returncode != 0
    if not rejected: raise ValidationError('Actual Vulkan driver accepted the missing native fragment entry point.')
    proof = dict(schema=1, graphicsApi='Vulkan', sourceManifestSha256=sha256(manifest_path), compilerReceiptSha256=sha256(compiler_path),
                 actualNativePipelineAliasCount=len(rows), actualDistinctNativePipelineCount=len(seen), aliases=rows,
                 drivers=[json.loads(value) for value in sorted({json.dumps(value, sort_keys=True) for value in seen.values()})], missingNativeEntryRejected=True,
                 originalWindowsPixelParityVerified=False, headsetPictureVerified=False, hostSourceSha256=sha256(source))
    (output / 'actual-vulkan-pipelines.json').write_text(json.dumps(proof, indent=2) + '\n')
    return proof


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--manifest', required=True, type=Path); p.add_argument('--compiler-receipt', required=True, type=Path)
    p.add_argument('--output', required=True, type=Path); p.add_argument('--headers', required=True, type=Path); p.add_argument('--icd', required=True, type=Path)
    a = p.parse_args(); run(a.manifest, a.compiler_receipt, a.output, a.headers, a.icd)


if __name__ == '__main__': main()
