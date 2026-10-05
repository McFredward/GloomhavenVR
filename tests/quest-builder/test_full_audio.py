"""Check native FSB channel-extension and exact Vorbis packet boundaries."""
from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-builder'))
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
