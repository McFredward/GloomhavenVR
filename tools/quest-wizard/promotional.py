"""Optional pinned publisher artwork; public images stay in a private local cache."""
import hashlib
import json
from pathlib import Path
import re
import struct
import threading
import urllib.request
from urllib.parse import urlsplit

from state import ordinary

MAX_IMAGE = 8 * 1048576
HOSTS = {'steamcdn-a.akamaihd.net', 'shared.akamai.steamstatic.com', 'shared.fastly.steamstatic.com'}


def trusted_url(url):
    parsed = urlsplit(url)
    return (parsed.scheme == 'https' and parsed.hostname in HOSTS and parsed.port in (None, 443)
            and not parsed.username and not parsed.password and not parsed.query and not parsed.fragment
            and re.fullmatch(r'/steamcommunity/public/images/clans/33333530/[a-f0-9]{40}\.(?:png|jpg)', parsed.path))


class Redirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        if not trusted_url(newurl): raise ValueError('Untrusted promotional image redirect.')
        return super().redirect_request(request, fp, code, msg, headers, newurl)


def image_type(raw):
    if raw[:8] == b'\x89PNG\r\n\x1a\n' and len(raw) >= 24 and raw[12:16] == b'IHDR':
        width, height = struct.unpack_from('>II', raw, 16)
        return 'image/png' if 128 <= width <= 4096 and 128 <= height <= 4096 else None
    if raw[:2] != b'\xff\xd8': return None
    offset = 2
    while offset + 4 <= len(raw):
        if raw[offset] != 255: return None
        marker = raw[offset + 1]; offset += 2
        if marker == 255: offset -= 1; continue
        if marker in (0xD8, 0xD9) or 0xD0 <= marker <= 0xD7: continue
        size = int.from_bytes(raw[offset:offset+2], 'big')
        if size < 2 or offset + size > len(raw): return None
        if marker in (0xC0, 0xC1, 0xC2) and size >= 8:
            height, width = struct.unpack_from('>HH', raw, offset + 3)
            return 'image/jpeg' if 128 <= width <= 4096 and 128 <= height <= 4096 else None
        if marker == 0xDA: return None
        offset += size
    return None


class Gallery:
    def __init__(self, ui_root, cache, *, opener=None):
        self.cache = ordinary(cache); self.opener = opener or urllib.request.build_opener(Redirect()).open
        self.rows = []; self.stop = threading.Event(); self.lock = threading.Lock(); self.thread = None
        pin = ordinary(Path(ui_root) / 'promo-artwork.json')
        self.pins = []
        if not pin.is_file() or pin.stat().st_size > 65536: return
        value = json.loads(pin.read_text(encoding='utf-8'))
        if value.get('schema') != 1 or not isinstance(value.get('images'), list): return
        for row in value['images'][:12]:
            if (isinstance(row, dict) and re.fullmatch('[a-z0-9-]{1,48}', str(row.get('id', '')))
                    and trusted_url(row.get('url', '')) and re.fullmatch('[a-f0-9]{64}', str(row.get('sha256', '')))
                    and type(row.get('size')) is int and 32 <= row['size'] <= MAX_IMAGE):
                self.pins.append(row)

    def read(self, row):
        path = ordinary(self.cache / (row['id'] + Path(urlsplit(row['url']).path).suffix))
        if not path.is_file() or path.stat().st_size != row['size']: return None
        raw = path.read_bytes()
        return raw if hashlib.sha256(raw).hexdigest() == row['sha256'] and image_type(raw) else None

    def fetch(self):
        self.cache.mkdir(parents=True, exist_ok=True)
        for row in self.pins:
            if self.stop.is_set(): break
            try:
                if self.read(row) is None:
                    request = urllib.request.Request(row['url'], headers={'Accept-Encoding': 'identity'})
                    with self.opener(request, timeout=8) as response:
                        raw = response.read(MAX_IMAGE + 1)
                    if len(raw) != row['size'] or hashlib.sha256(raw).hexdigest() != row['sha256'] or not image_type(raw): continue
                    path = ordinary(self.cache / (row['id'] + Path(urlsplit(row['url']).path).suffix))
                    temp = ordinary(path.with_suffix(path.suffix + '.partial')); temp.write_bytes(raw); temp.replace(path)
                with self.lock: self.rows.append(row)
            except (OSError, ValueError): continue  # Optional artwork cannot fail a conversion.

    def start(self):
        if self.thread is None:
            self.thread = threading.Thread(target=self.fetch, name='quest-promotional-artwork', daemon=True); self.thread.start()

    def visible(self):
        with self.lock:
            return [{'id': row['id'], 'url': '/api/promo-artwork?id=' + row['id'], 'sha256': row['sha256'],
                     'altCode': 'promoArtwork', 'source': row.get('source')} for row in self.rows]
