"""Check native FSB channel-extension and exact Vorbis packet boundaries."""
from pathlib import Path
import contextlib
import io
import json
import os
import struct
import sys
import tempfile
import types
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-builder'))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-recovery'))
import full_audio
from storage import BuildError


def bank(channels=4):
    packets = [b'first-packet', bytes(range(256)), b'last']
    payload = b''.join(struct.pack('<H', len(p)) + p for p in packets) + b'\0\0'
    payload += b'\0' * (-len(payload) % 32)
    sample = (400 << 34) | (9 << 1) | 1
    headers = struct.pack('<Q', sample) + struct.pack('<I', (1 << 25) | 2) + bytes([channels])
    headers += b'\0' * (-len(headers) % 4)
    data = b'FSB5' + struct.pack('<6I', 1, 1, len(headers), 0, len(payload), 15) + b'\0' * 32 + headers + payload
    return data, {'m_Channels': channels, 'm_Frequency': 48000, 'm_Length': 400/48000}, packets


def page(sizes, payload):
    return b'OggS\0' + b'\0' * 21 + bytes([len(sizes)]) + bytes(sizes) + payload


class BundledAudioTests(unittest.TestCase):
    def staged_audio(self, *, final_write_failure=False):
        import portable_decoder, pointer_recovery
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        root=Path(temporary.name); project=root/'project'; game=root/'game'; cache=root/'cache'
        (project/'QuestRecovery').mkdir(parents=True); game.mkdir(); cache.mkdir()
        (game/'audio.bundle').write_bytes(b'owned original audio container')
        raw,fields,packets=bank()
        header=b'\x01vorbis'+b'\0'*4+bytes([fields['m_Channels']])+struct.pack('<I',fields['m_Frequency'])
        corrected=page([len(header)],header)+page([7],b'comment')+page([5],b'setup')
        for packet in packets:
            corrected+=page([255]*(len(packet)//255)+[len(packet)%255],packet)
        rows=[]; objects=[]
        for index in range(2):
            relative=f'Assets/Audio{index}.ogg'; path=project/relative; path.parent.mkdir(exist_ok=True); path.write_bytes(b'old exported FSB')
            rows.append({'path':relative,'guid':f'{index+1:032x}','objects':[{'collection':'cab-audio','pathId':index+1,'fileId':8300000,'classId':83}]})
            tree={**fields,'m_CompressionFormat':1,'m_Resource':{'m_Source':'audio.resource','m_Offset':0,'m_Size':len(raw)}}
            objects.append(types.SimpleNamespace(assets_file=types.SimpleNamespace(name='cab-audio'),path_id=index+1,read_typetree=lambda tree=tree:tree))
        (project/'QuestRecovery/original-asset-identities.json').write_text(json.dumps({'identities':rows}))
        reader=types.SimpleNamespace(Position=0,read_bytes=lambda size:raw[:size])
        environment=types.SimpleNamespace(objects=objects,files={'bundle':types.SimpleNamespace(files={'audio.resource':reader})})
        def decode(command,args):
            self.assertEqual(Path(args[1]).read_bytes(),raw)
            Path(args[2]).write_bytes(corrected)
        stream=io.StringIO(); settings={full_audio.build_progress.ENV:'1'}
        with mock.patch.dict(os.environ,settings), contextlib.redirect_stdout(stream), \
             mock.patch.object(full_audio.build_progress.time,'monotonic',side_effect=range(10000)), \
             mock.patch.object(pointer_recovery,'load_native',return_value=environment), \
             mock.patch.object(portable_decoder,'build',return_value=['fixture-codec']), \
             mock.patch.object(portable_decoder,'execute',side_effect=decode):
            if final_write_failure:
                with mock.patch.object(full_audio,'write_json',side_effect=OSError('receipt write failed')), self.assertRaisesRegex(OSError,'receipt write failed'):
                    full_audio.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-audio':'audio.bundle'})
                report=None
            else:
                report=full_audio.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-audio':'audio.bundle'})
                for row in report['assets']:
                    self.assertEqual((project/row['assetPath']).read_bytes(),corrected)
                self.assertEqual(json.loads((project/'Assets/QuestOriginalCampaign/bundled-audio.json').read_text()),report)
        prefix=full_audio.build_progress.PREFIX
        progress=[json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        return report,[value for value in progress if value['phase']=='prepare-items:bundled-audio']

    def test_stage_progress_counts_accepted_original_audio_targets(self):
        report,progress=self.staged_audio()
        self.assertEqual(report['bundledAudioClipCount'],2)
        self.assertEqual([(row['status'],row['done'],row['total']) for row in progress],[('start',0,2),('progress',1,2),('complete',2,2)])
        self.assertEqual(progress[1]['detail'],'Assets/Audio0.ogg')

    def test_receipt_failure_never_publishes_audio_completion(self):
        _,progress=self.staged_audio(final_write_failure=True)
        self.assertEqual(progress[-1]['status'],'failed')
        self.assertFalse(any(row['status']=='complete' or row['done']==2 for row in progress))

    def test_quad_extension_and_original_packet_boundaries(self):
        raw, native, packets = bank()
        self.assertEqual(full_audio.fsb_vorbis(raw, native), (4, 48000, 400, packets))

    def test_source_disagreement_fails(self):
        raw, native, _ = bank()
        native['m_Channels'] = 1
        with self.assertRaises(BuildError): full_audio.fsb_vorbis(raw, native)

    def test_nonzero_packet_tail_fails(self):
        raw, native, _ = bank()
        with self.assertRaises(BuildError): full_audio.fsb_vorbis(raw[:-1]+b'x', native)

    def test_unknown_native_encoding_and_extension_fail(self):
        raw, native, _ = bank()
        changed = bytearray(raw); struct.pack_into('<I', changed, 24, 2)
        with self.assertRaises(BuildError): full_audio.fsb_vorbis(changed, native)
        changed = bytearray(raw); struct.pack_into('<I', changed, 68, (7 << 25) | 2)
        with self.assertRaises(BuildError): full_audio.fsb_vorbis(changed, native)

    def test_continued_ogg_packet_preserves_bytes(self):
        self.assertEqual(full_audio.ogg_packets(page([255], b'a'*255)+page([2,1],b'bcx')), [b'a'*255+b'bc',b'x'])

    def test_truncated_ogg_pages_fail(self):
        for value in (b'OggS', page([3],b'ab'), page([255],b'a'*255)):
            with self.assertRaises(BuildError): full_audio.ogg_packets(value)


if __name__ == '__main__': unittest.main()
