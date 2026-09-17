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
import time
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


class Refused(RuntimeError):
    pass


class GitHub:
    def __init__(self, repository, token):
        if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
            raise Refused('Invalid repository.')
        self.release_ids = {}
        self.repository = repository
        self.base = 'https://api.github.com/repos/' + repository
        self.headers = {'Authorization': 'Bearer ' + token,
                        'Accept': 'application/vnd.github+json',
                        'X-GitHub-Api-Version': '2022-11-28'}

    def request(self, path, method='GET', data=None):
        headers = dict(self.headers)
        if data is not None:
            headers['Content-Type'] = 'application/json'
        request = Request(self.base + path, headers=headers, method=method,
                          data=None if data is None else json.dumps(data).encode())
        try:
            with urlopen(request, timeout=60) as response:
                return json.load(response) if response.status != 204 else None
        except HTTPError as error:
            error.close()
            if error.code == 404 and method == 'GET':
                return None
            raise

    def release(self, tag):
        if tag in self.release_ids:
            release = self.request('/releases/' + str(self.release_ids[tag]))
            if release is None:
                raise OSError('Pinned release is not readable; refusing to create another draft.')
            if release.get('tag_name') != tag:
                raise Refused('Pinned release no longer names the requested tag.')
            return release
        # The by-tag endpoint resolves published releases only. Authenticated release
        # listings include drafts; relying on by-tag alone can create duplicate drafts.
        published = self.request('/releases/tags/' + tag)
        if published is not None:
            self.release_ids[tag] = published['id']
            return published
        matches = []
        for page in range(1, 21):
            batch = self.request(f'/releases?per_page=100&page={page}')
            if not isinstance(batch, list):
                raise OSError('Release listing is unavailable.')
            matches.extend(release for release in batch if release.get('tag_name') == tag)
            if len(matches) > 1:
                raise Refused('Multiple releases name this tag; inspect the drafts before recovery.')
            if len(batch) < 100:
                if matches:
                    self.release_ids[tag] = matches[0]['id']
                    return matches[0]
                return None
        raise Refused('Release listing exceeds the recovery search bound.')

    def create(self, tag, title, notes):
        # Like gh --verify-tag, verify the remote tag before POST; GitHub otherwise
        # invents a tag when creating a release. Never supply a replacement target.
        if self.request('/git/ref/tags/' + tag) is None:
            raise Refused('Existing remote tag is required before draft creation.')
        release = self.request('/releases', 'POST', {
            'tag_name': tag, 'name': title, 'body': notes,
            'draft': True, 'prerelease': False})
        if not release or not release.get('id') or release.get('tag_name') != tag:
            raise OSError('Draft creation response did not identify the requested release.')
        self.release_ids[tag] = release['id']
        return release

    def upload(self, tag, archive):
        release = self.release(tag)
        if release is None:
            raise Refused('Cannot upload without a known draft release.')
        require_draft(release)
        # Drafts cannot reliably be resolved by tag by the CLI. Upload directly to
        # the pinned release ID and stream bytes without buffering the full archive.
        url = ('https://uploads.github.com/repos/' + self.repository + '/releases/'
               + str(release['id']) + '/assets?' + urlencode({'name': archive.name}))
        headers = dict(self.headers, **{'Content-Type': 'application/zip',
                                       'Content-Length': str(archive.stat().st_size)})
        with archive.open('rb') as stream:
            request = Request(url, headers=headers, method='POST', data=stream)
            with urlopen(request, timeout=300) as response:
                return json.load(response)

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
        if not release.get('draft'):
            raise Refused('Published asset is incomplete; it will not be modified.')
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
    creation_started = False
    for attempt in range(attempts):
        try:
            check_source(api, tag, source)
            release = api.release(tag)
            if release is not None and not release.get('draft'):
                if publication_started and verified_asset(api, release, archive, digest):
                    return  # Publish succeeded remotely but its response was lost.
                require_draft(release)
            if release is None and not creation_started:
                creation_started = True  # A lost POST response must never create a second draft.
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
        except OSError as error:
            if isinstance(error, HTTPError):
                error.close()
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
