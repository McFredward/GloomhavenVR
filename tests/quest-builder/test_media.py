"""Execute lossless container staging, corruption controls and immutable sources."""
import hashlib
import os
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import media
import storage


def box(kind, payload):
    return struct.pack(">I4s", len(payload) + 8, kind) + payload


def track(identity, handler, offset, *, width=4, reference=None, external=False, extra=b""):
    tkhd = box(b"tkhd", b"\0" * 12 + struct.pack(">I", identity) + b"original timing/geometry")
    hdlr = box(b"hdlr", b"\0" * 8 + handler + b"authored handler")
    table = box(b"stco" if width == 4 else b"co64", b"\0" * 4 + struct.pack(">I", 1) + struct.pack(">I" if width == 4 else ">Q", offset))
    description = box(b"stsd", b"codec/profile/extradata preserved")
    timeline = box(b"stts", b"authored sample timeline") + box(b"ctts", b"authored composition offsets")
    dref = box(b"dref", b"\0" * 4 + struct.pack(">I", 1) + box(b"url ", b"\0\0\0\0outside.mp4\0" if external else b"\0\0\0\1"))
    minf = box(b"minf", box(b"dinf", dref) + box(b"stbl", description + timeline + table + extra))
    mdia = box(b"mdia", box(b"mdhd", b"authored media duration") + hdlr + minf)
    tref = box(b"tref", box(reference[0], struct.pack(">I", reference[1]))) if reference else b""
    return box(b"trak", tkhd + box(b"edts", b"authored edit list") + tref + mdia)


def fixture(*, timecode=True, faststart=False, width=4, external=False, extra=b"", handler=b"tmcd", ref_kind=b"tmcd", bad_offset=False, extra_root=b"", duplicate=False):
    ftyp = box(b"ftyp", b"isom\0\0\0\0isomiso2avc1mp41")
    payload = b"VIDEO SAMPLE\0AUDIO SAMPLE\0TIMECODE SAMPLE"
    def movie(offset):
        video = track(1, b"vide", 0 if bad_offset else offset, width=width,
                      reference=(ref_kind, 3) if timecode else None, external=external, extra=extra)
        audio = track(1 if duplicate else 2, b"soun", offset + 13, width=width)
        code = track(3, handler, offset + 26, width=width) if timecode else b""
        return box(b"moov", box(b"mvhd", b"authored movie duration") + video + audio + code)
    offset = len(ftyp) + 8 + (len(movie(0)) if faststart else 0)
    moov, mdat = movie(offset), box(b"mdat", payload)
    return ftyp + (moov + mdat if faststart else mdat + moov) + extra_root


class MediaStagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "owned/intro.mp4"
        self.target = self.root / "generated/intro.mp4"
        self.source.parent.mkdir()
        self.relative = "StreamingAssets/QuestOriginalMovies/intro.mp4"

    def tearDown(self):
        self.temp.cleanup()

    def stage(self, payload=None):
        if payload is not None:
            self.source.write_bytes(payload)
        return media.stage_media(self.source, self.target, self.relative)

    def reject(self, payload, message):
        self.source.write_bytes(payload)
        with self.assertRaisesRegex(storage.BuildError, message):
            self.stage()
        self.assertFalse(self.target.exists())
        self.assertEqual(self.source.read_bytes(), payload)

    def test_lossless_timecode_removal_preserves_full_mdat_and_av_metadata(self):
        for faststart in (False, True):
            for width in (4, 8):
                with self.subTest(faststart=faststart, width=width):
                    original = fixture(faststart=faststart, width=width)
                    record = self.stage(original)
                    self.assertEqual(self.source.read_bytes(), original)
                    self.assertEqual(record["originalSha256"], hashlib.sha256(original).hexdigest())
                    self.assertEqual(record["sha256"], storage.digest(self.target))
                    self.assertEqual(record["size"], self.target.stat().st_size)
                    self.assertLess(record["size"], record["originalSize"])
                    self.assertEqual(record["delivery"], media.RECIPE)
                    self.assertEqual(record["mediaProof"]["removedTimecodeTrackIds"], [3])
                    self.assertEqual([item["handler"] for item in record["mediaProof"]["avMetadata"]], ["vide", "soun"])
                    old_top, old_moov = media._top(self.source)
                    new_top, new_moov = media._top(self.target)
                    old_mdat, new_mdat = media._one(old_top, b"mdat"), media._one(new_top, b"mdat")
                    output = self.target.read_bytes()
                    self.assertEqual(original[old_mdat.start:old_mdat.start + old_mdat.size], output[new_mdat.start:new_mdat.start + new_mdat.size])
                    self.assertLess(media._one(new_top, b"moov").start, new_mdat.start)
                    new_tracks = [a for a in media._children(new_moov, media._one(media._atoms(new_moov), b"moov")) if a.kind == b"trak"]
                    self.assertEqual([media._track(new_moov, a) for a in new_tracks], [(1, b"vide"), (2, b"soun")])
                    self.assertNotIn(b"tref", new_moov)

    def test_no_timecode_and_other_containers_remain_byte_identical(self):
        for payload in (fixture(timecode=False), b"other original container fixture", b"tiny"):
            with self.subTest(size=len(payload)):
                record = self.stage(payload)
                self.assertEqual(self.target.read_bytes(), payload)
                self.assertEqual(record["delivery"], "original")
                self.assertEqual(record["originalSha256"], record["sha256"])
                self.assertEqual(record["originalSize"], record["size"])
                self.assertEqual(record["mediaProof"], {})

    def test_verified_cache_reuse_and_same_size_timestamp_preserving_repair(self):
        record = self.stage(fixture())
        before = self.target.stat()
        self.assertEqual(self.stage(), record)
        self.assertEqual(self.target.stat().st_mtime_ns, before.st_mtime_ns)
        original = self.target.read_bytes()
        corrupted = bytearray(original); corrupted[-1] ^= 1
        self.target.write_bytes(corrupted)
        os.utime(self.target, ns=(before.st_atime_ns, before.st_mtime_ns))
        self.assertEqual(self.stage(), record)
        self.assertEqual(self.target.read_bytes(), original)

    def test_interrupted_temp_does_not_acquire_or_corrupt_delivery(self):
        self.target.parent.mkdir()
        abandoned = self.target.with_name(self.target.name + ".tmp-interrupted")
        abandoned.write_bytes(b"unfinished bytes")
        record = self.stage(fixture())
        self.assertEqual(record["sha256"], storage.digest(self.target))
        self.assertEqual(abandoned.read_bytes(), b"unfinished bytes")

    def test_unknown_offset_metadata_is_rejected_before_deployment(self):
        self.reject(fixture(extra=box(b"saio", b"offsets")), "unsupported AV metadata")
        self.reject(fixture(extra_root=box(b"moof", b"fragment")), "top-level container shape")

    def test_unknown_authored_track_and_reference_are_not_discarded(self):
        # Keep a tmcd track while introducing an authored third handler.
        payload = fixture().replace(b"soun", b"text", 1)
        self.reject(payload, "another authored track")
        self.reject(fixture(ref_kind=b"chap"), "non-timecode track references")

    def test_escaping_offsets_external_media_and_duplicate_ids_are_rejected(self):
        self.reject(fixture(bad_offset=True), "offsets escape")
        self.reject(fixture(external=True), "self-contained")
        self.reject(fixture(duplicate=True), "identities are invalid")

    def test_truncated_and_oversized_metadata_are_rejected(self):
        payload = fixture()
        self.reject(payload[:-3], "top-level atom")
        self.reject(payload[:32] + struct.pack(">I4s", media.MAX_METADATA + 1, b"moov") + b"\0" * media.MAX_METADATA, "exceeds its bound")
        with self.assertRaisesRegex(storage.BuildError, "truncated extended"):
            media._atoms(struct.pack(">I4s", 1, b"moov"))
        with self.assertRaisesRegex(storage.BuildError, "truncated atom"):
            media._atoms(b"broken")

    def test_source_changes_before_publication_leave_previous_output_untouched(self):
        self.stage(fixture(timecode=False))
        previous = self.target.read_bytes()
        self.source.write_bytes(fixture())
        actual_digest = media.digest
        def changing_digest(path):
            result = actual_digest(path)
            if path.name.startswith("intro.mp4.tmp-"):
                changed = bytearray(self.source.read_bytes()); changed[-1] ^= 1
                self.source.write_bytes(changed)
            return result
        with patch.object(media, "digest", changing_digest):
            with self.assertRaisesRegex(storage.BuildError, "changed during delivery"):
                self.stage()
        self.assertEqual(self.target.read_bytes(), previous)
        self.assertEqual(list(self.target.parent.glob("*.tmp-*")), [])

    def test_source_replacement_invalidates_verified_cache(self):
        self.stage(fixture())
        replacement = fixture(timecode=False)
        result = self.stage(replacement)
        self.assertEqual(result["delivery"], "original")
        self.assertEqual(self.target.read_bytes(), replacement)

    def test_source_and_destination_overlap_or_links_are_rejected(self):
        self.source.write_bytes(fixture())
        with self.assertRaisesRegex(storage.BuildError, "separate generated"):
            media.stage_media(self.source, self.source, self.relative)
        if sys.platform != "win32":
            self.target.parent.mkdir()
            self.target.symlink_to(self.source)
            with self.assertRaisesRegex(storage.BuildError, "symbolic links"):
                self.stage()
            self.target.unlink()
            link = self.root / "linked"; link.symlink_to(self.source.parent, target_is_directory=True)
            with self.assertRaisesRegex(storage.BuildError, "missing or linked"):
                media.stage_media(link / self.source.name, self.target, self.relative)


if __name__ == "__main__":
    unittest.main()
