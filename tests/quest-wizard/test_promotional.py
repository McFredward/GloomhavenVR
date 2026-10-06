"""Pinned artwork cache and MIME boundaries; no live network in these fixtures."""
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-wizard'))
import promotional


class Images(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.ui = self.root / 'ui'; self.ui.mkdir()
        self.raw = b'\x89PNG\r\n\x1a\n' + struct.pack('>I', 13) + b'IHDR' + struct.pack('>II', 1024, 512) + b'fixture-picture'
        self.row = {'id': 'brute', 'url': 'https://steamcdn-a.akamaihd.net/steamcommunity/public/images/clans/33333530/' + 'a' * 40 + '.png',
                    'sha256': hashlib.sha256(self.raw).hexdigest(), 'size': len(self.raw)}
        self.pin()
    def pin(self): (self.ui / 'promo-artwork.json').write_text(json.dumps({'schema': 1, 'images': [self.row]}))
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


if __name__ == '__main__': unittest.main()
