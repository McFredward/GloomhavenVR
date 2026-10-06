"""Pinned publisher artwork, available offline before any game or tool discovery."""
import hashlib
import json
import logging
from pathlib import Path
import re
import struct
import threading
import urllib.request
from urllib.parse import urlsplit

from state import ordinary

MAX_IMAGE = 8 * 1048576
HOSTS = {'steamcdn-a.akamaihd.net', 'shared.akamai.steamstatic.com', 'shared.fastly.steamstatic.com'}
LOG = logging.getLogger(__name__)


def trusted_url(url):
    if not isinstance(url, str): return False
    try:
        parsed = urlsplit(url)
        port = parsed.port
    except ValueError: return False
    return (parsed.scheme == 'https' and parsed.hostname in HOSTS and port in (None, 443)
            and not parsed.username and not parsed.password and not parsed.query and not parsed.fragment
            and re.fullmatch(r'/steamcommunity/public/images/clans/33333530/[a-f0-9]{40}\.(?:png|jpg)', parsed.path))


class Redirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        if not trusted_url(newurl): raise ValueError('Untrusted promotional image redirect.')
        return super().redirect_request(request, fp, code, msg, headers, newurl)


def image_info(raw):
    if raw[:8] == b'\x89PNG\r\n\x1a\n' and len(raw) >= 24 and raw[12:16] == b'IHDR':
        width, height = struct.unpack_from('>II', raw, 16)
        return ('image/png', (width, height)) if 128 <= width <= 4096 and 128 <= height <= 4096 else None
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
            return ('image/jpeg', (width, height)) if 128 <= width <= 4096 and 128 <= height <= 4096 else None
        if marker == 0xDA: return None
        offset += size
    return None


def image_type(raw):
    info = image_info(raw)
    return info[0] if info else None


class Gallery:
    def __init__(self, ui_root, cache, *, opener=None):
        self.ui_root = ordinary(ui_root)
        self.cache = ordinary(cache); self.opener = opener or urllib.request.build_opener(Redirect()).open
        self.rows = []; self.stop = threading.Event(); self.lock = threading.Lock(); self.thread = None
        self.diagnostics = []; self.reported = set()
        pin = ordinary(self.ui_root / 'promo-artwork.json')
        self.pins = []
        if not pin.is_file() or pin.stat().st_size > 65536:
            self.report('manifest', 'missing_or_oversized_manifest'); return
        try: value = json.loads(pin.read_text(encoding='utf-8'))
        except (OSError, ValueError) as error:
            self.report('manifest', 'invalid_manifest', error); return
        if not isinstance(value, dict) or value.get('schema') != 1 or not isinstance(value.get('images'), list):
            self.report('manifest', 'invalid_manifest'); return
        seen = set()
        for row in value['images'][:12]:
            if (isinstance(row, dict) and isinstance(row.get('id'), str) and re.fullmatch('[a-z0-9-]{1,48}', row['id'])
                    and trusted_url(row.get('url', '')) and isinstance(row.get('sha256'), str)
                    and re.fullmatch('[a-f0-9]{64}', row['sha256'])
                    and type(row.get('size')) is int and 32 <= row['size'] <= MAX_IMAGE):
                if row['id'] in seen: continue
                seen.add(row['id'])
                # Only the declared public artwork directory is eligible. A pin
                # never becomes a generic filesystem/static-file capability.
                expected = 'assets/promo/' + row['id'] + Path(urlsplit(row['url']).path).suffix
                if 'localPath' in row and row['localPath'] != expected:
                    self.report(row['id'], 'invalid_bundle_path'); continue
                dimensions = row.get('dimensions')
                if dimensions is not None and (not isinstance(dimensions, list) or len(dimensions) != 2
                        or any(type(n) is not int or not 128 <= n <= 4096 for n in dimensions)):
                    self.report(row['id'], 'invalid_dimensions'); continue
                self.pins.append(row)
        # The former network-only worker left the UI empty until downloads
        # completed. The exact publisher bytes ship in the source release, so
        # gallery requests can display them immediately, including offline.
        for row in self.pins:
            if self.read(row) is not None: self.publish(row)

    def report(self, identity, code, error=None):
        with self.lock:
            key = (identity, code)
            if key in self.reported or len(self.reported) >= 48: return
            self.reported.add(key)
            self.diagnostics.append({'id': identity, 'code': code})
        # No owner paths, remote URL parameters or response body enter logs.
        LOG.warning('Quest promotional artwork %s: %s%s', identity, code,
                    ' (' + type(error).__name__ + ')' if error else '')

    def valid(self, raw, row):
        info = image_info(raw)
        return (len(raw) == row['size'] and hashlib.sha256(raw).hexdigest() == row['sha256']
                and info is not None and (row.get('dimensions') is None or tuple(row['dimensions']) == info[1]))

    def read(self, row):
        # Only rows accepted from this release's manifest may be served.
        if not any(row == pin for pin in self.pins): return None
        paths = []
        if 'localPath' in row: paths.append((self.ui_root / row['localPath'], 'bundle'))
        paths.append((self.cache / (row['id'] + Path(urlsplit(row['url']).path).suffix), 'cache'))
        for candidate, origin in paths:
            try:
                path = ordinary(candidate)
                if not path.is_file():
                    if origin == 'bundle': self.report(row['id'], 'bundle_missing')
                    continue
                if path.stat().st_size == row['size']:
                    with path.open('rb') as stream: raw = stream.read(MAX_IMAGE + 1)
                    if self.valid(raw, row): return raw
                self.report(row['id'], origin + '_invalid')
            except (OSError, ValueError, RuntimeError) as error:
                self.report(row['id'], origin + '_unavailable', error)
        return None

    def publish(self, row):
        with self.lock:
            if not any(visible['id'] == row['id'] for visible in self.rows): self.rows.append(row)

    def fetch(self):
        for row in self.pins:
            if self.stop.is_set(): break
            try:
                if self.read(row) is None:
                    request = urllib.request.Request(row['url'], headers={'Accept-Encoding': 'identity'})
                    with self.opener(request, timeout=8) as response:
                        raw = response.read(MAX_IMAGE + 1)
                    if not self.valid(raw, row):
                        self.report(row['id'], 'download_invalid'); continue
                    self.cache.mkdir(parents=True, exist_ok=True)
                    path = ordinary(self.cache / (row['id'] + Path(urlsplit(row['url']).path).suffix))
                    temp = ordinary(path.with_suffix(path.suffix + '.partial')); temp.write_bytes(raw); temp.replace(path)
                self.publish(row)
            except (OSError, ValueError, RuntimeError) as error:
                self.report(row['id'], 'download_unavailable', error)  # Optional art never fails conversion.

    def start(self):
        if self.thread is None:
            self.thread = threading.Thread(target=self.fetch, name='quest-promotional-artwork', daemon=True); self.thread.start()

    def visible(self):
        with self.lock:
            return [{'id': row['id'], 'url': '/api/promo-artwork?id=' + row['id'], 'sha256': row['sha256'],
                     'altCode': 'promoArtwork', 'source': row.get('source'), 'caption': row.get('caption')} for row in self.rows]
