#!/usr/bin/env python3
"""Publish an immutable main release only after its draft asset is verified.

A failed upload never publishes an empty release. Retrying a draft reconciles the
remote asset instead of overwriting it; published releases are never modified.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen


class Refused(RuntimeError):
    pass


class GitHub:
    def __init__(self, repository, token):
        if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
            raise Refused('Invalid repository.')
        self.repository = repository
        self.base = 'https://api.github.com/repos/' + repository
        self.headers = {'Authorization': 'Bearer ' + token,
                        'Accept': 'application/vnd.github+json',
                        'X-GitHub-Api-Version': '2022-11-28'}

    def request(self, path, method='GET', data=None):
        request = Request(self.base + path, headers=self.headers, method=method,
                          data=None if data is None else json.dumps(data).encode())
        try:
            with urlopen(request, timeout=60) as response:
                return json.load(response) if response.status != 204 else None
        except HTTPError as error:
            if error.code == 404 and method == 'GET':
                return None
            raise

    def release(self, tag):
        return self.request('/releases/tags/' + tag)

    def create(self, tag, title, notes):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md') as body:
            body.write(notes)
            body.flush()
            subprocess.run(['gh', 'release', 'create', tag, '--repo', self.repository,
                            '--title', title, '--notes-file', body.name, '--verify-tag', '--draft'], check=True)

    def upload(self, tag, archive):
        subprocess.run(['gh', 'release', 'upload', tag, str(archive), '--repo', self.repository], check=True)

    def remove_starter(self, asset):
        self.request('/releases/assets/' + str(asset['id']), 'DELETE')

    def digest(self, asset):
        if (asset.get('digest') or '').startswith('sha256:'):
            return asset['digest'][7:]
        # Older GitHub installations may omit asset digests. Verify downloaded bytes.
        headers = dict(self.headers, Accept='application/octet-stream')
        request = Request(self.base + '/releases/assets/' + str(asset['id']), headers=headers)
        digest = hashlib.sha256()
        with urlopen(request, timeout=120) as response:
            while chunk := response.read(1024 * 1024):
                digest.update(chunk)
        return digest.hexdigest()

    def publish(self, release, title, notes):
        return self.request('/releases/' + str(release['id']), 'PATCH', {
            'name': title, 'body': notes, 'draft': False, 'prerelease': False,
            'make_latest': 'true'})


def check_source(api, tag, source):
    if not re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+', tag) or not re.fullmatch(r'[0-9a-f]{40}', source):
        raise Refused('Invalid tag or source commit.')
    tagged = api.request('/commits/' + tag)
    comparison = api.request('/compare/' + source + '...main')
    if not tagged or tagged.get('sha') != source or not comparison or comparison.get('merge_base_commit', {}).get('sha') != source:
        raise Refused('Immutable tag must still name the built source in main history.')


def require_draft(release):
    if release is not None and not release.get('draft'):
        raise Refused('This release is already published; it will not be modified.')


def verified_asset(api, release, archive, digest):
    assets = release.get('assets', [])
    if any(asset.get('name') != archive.name for asset in assets) or len(assets) > 1:
        raise Refused('Unexpected draft assets; review them before recovery.')
    if not assets:
        return False
    asset = assets[0]
    if asset.get('state') == 'starter':
        api.remove_starter(asset)  # GitHub documents this empty remnant after HTTP 502.
        return False
    if asset.get('state') != 'uploaded' or asset.get('size') != archive.stat().st_size or api.digest(asset) != digest:
        raise Refused('Existing draft asset differs from the fresh package; refusing overwrite.')
    return True


def publish(api, tag, source, archive, notes, sleep=time.sleep, attempts=5):
    check_source(api, tag, source)
    require_draft(api.release(tag))
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    title = 'GloomhavenVR ' + tag[1:]
    last_error = None
    publication_started = False
    for attempt in range(attempts):
        try:
            check_source(api, tag, source)
            release = api.release(tag)
            if release is not None and not release.get('draft'):
                if publication_started and verified_asset(api, release, archive, digest):
                    return  # Publish succeeded remotely but its response was lost.
                require_draft(release)
            if release is None:
                api.create(tag, title, notes)
                release = api.release(tag)
            if release is None:
                raise OSError('New draft is not yet readable.')
            require_draft(release)
            if not verified_asset(api, release, archive, digest):
                api.upload(tag, archive)
                release = api.release(tag)
                require_draft(release)
                if release is None or not verified_asset(api, release, archive, digest):
                    raise OSError('Upload is not yet complete.')
            check_source(api, tag, source)
            publication_started = True
            api.publish(release, title, notes)
            confirmed = api.release(tag)
            if confirmed is None or confirmed.get('draft') or not verified_asset(api, confirmed, archive, digest):
                raise OSError('Publication could not be confirmed.')
            return
        except Refused:
            raise
        except (OSError, subprocess.CalledProcessError) as error:
            last_error = error
            print(f'Release transport attempt {attempt + 1}/{attempts} failed: {error}', flush=True)
            if attempt + 1 < attempts:
                sleep(10 * (attempt + 1))
    raise Refused(f'Release transport failed after {attempts} attempts; inspect the retained draft: {last_error}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('check', 'publish'))
    parser.add_argument('--tag', required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--notes', type=Path)
    args = parser.parse_args()
    api = GitHub(os.environ['GITHUB_REPOSITORY'], os.environ['GH_TOKEN'])
    check_source(api, args.tag, args.source)
    if args.operation == 'check':
        require_draft(api.release(args.tag))
    else:
        if not args.archive or not args.notes:
            parser.error('publish requires --archive and --notes')
        publish(api, args.tag, args.source, args.archive, args.notes.read_text())


if __name__ == '__main__':
    main()
