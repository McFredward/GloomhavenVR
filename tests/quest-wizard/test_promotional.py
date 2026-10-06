"""Pinned artwork cache and MIME boundaries; no live network in these fixtures."""
import hashlib
import io
import json
import logging
import os
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-wizard'))
import promotional
ROOT = Path(__file__).resolve().parents[2]


class Images(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.ui = self.root / 'ui'; self.ui.mkdir()
        self.raw = b'\x89PNG\r\n\x1a\n' + struct.pack('>I', 13) + b'IHDR' + struct.pack('>II', 1024, 512) + b'fixture-picture'
        self.row = {'id': 'brute', 'url': 'https://steamcdn-a.akamaihd.net/steamcommunity/public/images/clans/33333530/' + 'a' * 40 + '.png',
                    'sha256': hashlib.sha256(self.raw).hexdigest(), 'size': len(self.raw)}
        self.pin()
    def pin(self): (self.ui / 'promo-artwork.json').write_text(json.dumps({'schema': 1, 'images': [self.row]}))
    def bundle(self):
        self.row['localPath'] = 'assets/promo/brute.png'
        self.row['dimensions'] = [1024, 512]
        path = self.ui / self.row['localPath']; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.raw); self.pin(); return path
    def test_pinned_bytes_cached_and_revalidated_without_second_download(self):
        calls = []
        def fetch(*args, **kwargs): calls.append(args[0].full_url); return io.BytesIO(self.raw)
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=fetch); gallery.fetch()
        self.assertEqual(gallery.visible()[0]['url'], '/api/promo-artwork?id=brute')
        self.assertEqual(gallery.read(self.row), self.raw)
        second = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: self.fail('cached bytes downloaded again')); second.fetch()
        self.assertEqual(len(second.visible()), 1); self.assertEqual(len(calls), 1)
        (self.root / 'cache/brute.png').write_bytes(self.raw[:-1] + b'x'); self.assertIsNone(gallery.read(self.row))
    def test_wrong_bytes_oversize_and_missing_images_do_not_fail_build(self):
        for raw in (self.raw[:-1], b'x' * (promotional.MAX_IMAGE + 1), b'not a raster'):
            gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: io.BytesIO(raw)); gallery.fetch()
            self.assertEqual(gallery.visible(), [])
        gallery = promotional.Gallery(self.root / 'missing-ui', self.root / 'cache'); self.assertEqual(gallery.visible(), [])
    def test_no_arbitrary_urls_redirects_or_cache_links(self):
        for url in ('http://steamcdn-a.akamaihd.net/x.png', 'https://127.0.0.1/private', 'file:///secret', self.row['url'] + '?token=private'):
            self.assertFalse(promotional.trusted_url(url))
        self.row['url'] = 'https://example.invalid/secret.png'; self.pin()
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: self.fail('untrusted URL used')); gallery.fetch()
        self.assertFalse(gallery.pins)
        with self.assertRaises(ValueError): promotional.Redirect().redirect_request(None, None, 302, '', {}, 'http://127.0.0.1/private')
    def test_png_jpeg_dimensions_and_signature_require_raster(self):
        self.assertEqual(promotional.image_type(self.raw), 'image/png')
        jpeg = b'\xff\xd8\xff\xc0' + struct.pack('>H B H H B', 8, 8, 1080, 1920, 3)
        self.assertEqual(promotional.image_type(jpeg), 'image/jpeg')
        self.assertIsNone(promotional.image_type(b'<!doctype html>'))
        self.assertIsNone(promotional.image_type(jpeg[:6]))

    def test_bundled_image_is_immediate_offline_and_background_does_not_duplicate(self):
        self.bundle()
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: self.fail('offline bundle used network'))
        self.assertEqual(len(gallery.visible()), 1)  # Before start/fetch, the first HTTP response can show artwork.
        self.assertEqual(gallery.read(gallery.pins[0]), self.raw)
        gallery.start(); gallery.thread.join(3)
        self.assertFalse(gallery.thread.is_alive())
        gallery.fetch()
        self.assertEqual(len(gallery.visible()), 1)
        self.assertFalse((self.root / 'cache').exists())
        self.assertEqual(gallery.diagnostics, [])

    def test_bundle_failure_uses_verified_cache_and_reports_each_failure_once(self):
        path = self.bundle(); path.write_bytes(self.raw[:-1] + b'x')
        calls = []
        def fetch(*args, **kwargs): calls.append(args[0].full_url); return io.BytesIO(self.raw)
        with self.assertLogs(promotional.LOG, logging.WARNING) as records:
            gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=fetch)
            self.assertEqual(gallery.visible(), [])
            gallery.fetch(); gallery.fetch()
        self.assertEqual(len(calls), 1)
        self.assertEqual(gallery.read(gallery.pins[0]), self.raw)
        self.assertEqual(gallery.diagnostics, [{'id': 'brute', 'code': 'bundle_invalid'}])
        self.assertEqual(len(records.output), 1)
        second = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: self.fail('warm verified cache used network'))
        self.assertEqual(len(second.visible()), 1)

    def test_declared_dimensions_are_enforced_for_bundle_cache_and_download(self):
        self.bundle(); self.row['dimensions'] = [2048, 512]; self.pin()
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: io.BytesIO(self.raw))
        gallery.fetch()
        self.assertEqual(gallery.visible(), [])
        self.assertIsNone(gallery.read(gallery.pins[0]))
        self.assertFalse((self.root / 'cache/brute.png').exists())

    def test_bundle_pin_cannot_escape_or_select_unlisted_source_files(self):
        self.bundle()
        for value in ('../../secret.png', '/secret.png', 'C:/secret.png', 'assets\\promo\\brute.png',
                      'assets/promo/../brute.png', 'assets/promo/other.png', ['assets/promo/brute.png']):
            with self.subTest(value=value):
                self.row['localPath'] = value; self.pin()
                gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: self.fail('invalid bundle path used network'))
                gallery.fetch()
                self.assertEqual(gallery.pins, [])
                self.assertEqual(gallery.visible(), [])
        self.row['localPath'] = 'assets/promo/brute.png'; self.pin()
        gallery = promotional.Gallery(self.ui, self.root / 'cache')
        unlisted = dict(gallery.pins[0], localPath='assets/promo/other.png')
        self.assertIsNone(gallery.read(unlisted))

    @unittest.skipIf(os.name == 'nt', 'Fixture symlink privilege is not available on ordinary Windows accounts.')
    def test_bundle_file_and_parent_symlinks_are_not_served(self):
        path = self.bundle(); outside = self.root / 'private-picture'; outside.write_bytes(self.raw)
        path.unlink(); path.symlink_to(outside)
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: io.BytesIO(b'invalid'))
        self.assertEqual(gallery.visible(), [])
        self.assertIsNone(gallery.read(gallery.pins[0]))
        self.assertIn({'id': 'brute', 'code': 'bundle_unavailable'}, gallery.diagnostics)
        path.unlink(); path.parent.rmdir(); path.parent.symlink_to(self.root)
        (self.root / 'brute.png').write_bytes(self.raw)
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=lambda *a, **k: io.BytesIO(b'invalid'))
        gallery.fetch()
        self.assertEqual(gallery.visible(), [])

    def test_optional_network_failure_is_bounded_and_keeps_verified_rows(self):
        def offline(*a, **k): raise OSError('network unavailable')
        gallery = promotional.Gallery(self.ui, self.root / 'cache', opener=offline)
        with self.assertLogs(promotional.LOG, logging.WARNING) as records:
            gallery.fetch(); gallery.fetch(); gallery.fetch()
        self.assertEqual(len(records.output), 1)
        self.assertEqual(gallery.diagnostics, [{'id': 'brute', 'code': 'download_unavailable'}])
        self.assertEqual(gallery.visible(), [])

    def test_malformed_pin_urls_and_manifests_do_not_crash_optional_gallery(self):
        for value in ([], None, 'https://steamcdn-a.akamaihd.net:broken/private.png'):
            self.assertFalse(promotional.trusted_url(value))
        for value in ('{broken', '[]', '{"schema":1,"images":null}'):
            (self.ui / 'promo-artwork.json').write_text(value)
            gallery = promotional.Gallery(self.ui, self.root / 'cache')
            self.assertEqual(gallery.visible(), [])
            self.assertEqual(gallery.diagnostics, [{'id': 'manifest', 'code': 'invalid_manifest'}])
        for field, value in (('id', 123), ('sha256', 123), ('url', []), ('size', True)):
            invalid = dict(self.row); invalid[field] = value
            (self.ui / 'promo-artwork.json').write_text(json.dumps({'schema': 1, 'images': [invalid]}))
            gallery = promotional.Gallery(self.ui, self.root / 'cache')
            self.assertEqual(gallery.pins, [])
            self.assertEqual(gallery.visible(), [])


class ActualPublisherBundle(unittest.TestCase):
    def test_all_shipped_publisher_pictures_available_offline_before_start(self):
        with tempfile.TemporaryDirectory() as temporary:
            def offline(*a, **k): self.fail('The actual publisher bundle contacted the network.')
            ui = ROOT / 'tools/quest-wizard-ui'
            cache = Path(temporary) / 'empty-cache'
            gallery = promotional.Gallery(ui, cache, opener=offline)
            self.assertEqual([row['id'] for row in gallery.visible()],
                             ['cragheart', 'spellweaver', 'brute', 'scoundrel', 'bandit-guard', 'bandit-archer',
                              'living-bones', 'living-corpse', 'living-spirit', 'cultist', 'sun-demon', 'night-demon-elite'])
            for pin, visible in zip(gallery.pins, gallery.visible()):
                raw = gallery.read(pin)
                self.assertEqual(len(raw), pin['size'])
                self.assertEqual(hashlib.sha256(raw).hexdigest(), visible['sha256'])
                self.assertEqual(promotional.image_info(raw)[1], tuple(pin['dimensions']))
                self.assertEqual(visible['url'], '/api/promo-artwork?id=' + pin['id'])
                self.assertEqual(visible['caption']['en'], visible['caption']['de'])
                self.assertTrue(visible['source'].startswith('https://store.steampowered.com/news/'))
            gallery.start(); gallery.thread.join(3)
            self.assertFalse(gallery.thread.is_alive())
            self.assertEqual(len(gallery.visible()), len(gallery.pins))
            self.assertEqual(gallery.diagnostics, [])
            self.assertFalse(cache.exists())


if __name__ == '__main__': unittest.main()
