#!/usr/bin/env python3
"""Reuse successful, full dev CI for an identical Git tree; never trust a reused check.

Only GitHub run/job metadata is consulted; no Actions artifacts or executable logs are
loaded. A missing, stale, partial or superseded proof means full PR checks or no release.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from urllib.parse import urlencode
from urllib.request import Request, urlopen

WORKFLOW = '.github/workflows/ci.yml'
HELPER = 'scripts/ci-proof-reuse.py'
PROOF_STEP = 'Record full-check completion'
MAX_AGE_DAYS = 30
MAX_RUN_PAGES = 5
SHA = re.compile(r'^[0-9a-f]{40}$')


class NoProof(RuntimeError):
    pass


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args], text=True,
                                   stderr=subprocess.DEVNULL).strip()


def commit(root, ref):
    return git(root, 'rev-parse', '--verify', ref + '^{commit}')


def tree(root, ref):
    return git(root, 'rev-parse', '--verify', ref + '^{tree}')


def candidate(root, event, mode, repository, expected_sha, event_name=None, event_ref=None):
    """Bind checkout to the actual event, including both synthetic PR merge parents."""
    head = commit(root, 'HEAD')
    if head != expected_sha or not SHA.fullmatch(head):
        raise NoProof('Checkout does not match the event commit.')
    if mode == 'pr':
        pr = event.get('pull_request', {})
        if pr.get('head', {}).get('repo', {}).get('full_name') != repository:
            raise NoProof('Fork pull requests always run full checks.')
        if pr.get('base', {}).get('repo', {}).get('full_name') != repository:
            raise NoProof('Unexpected pull request base repository.')
        parents = git(root, 'show', '-s', '--format=%P', head).split()
        if parents != [pr.get('base', {}).get('sha'), pr.get('head', {}).get('sha')]:
            raise NoProof('PR checkout is not the current base/head merge; run full checks.')
    elif mode == 'release-resume':
        if event_name != 'workflow_dispatch' or event_ref != 'refs/heads/main' or event.get('ref') not in ('main', 'refs/heads/main'):
            raise NoProof('Release recovery requires a workflow dispatch on main.')
        tag = event.get('inputs', {}).get('resume_tag', '')
        if not re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+', tag):
            raise NoProof('Recovery requires an explicit stable release tag.')
        source = commit(root, 'refs/tags/' + tag)
        try:
            git(root, 'merge-base', '--is-ancestor', source, head)
            git(root, 'merge-base', '--is-ancestor', head, 'refs/remotes/origin/main')
        except subprocess.CalledProcessError as error:
            raise NoProof('Recovery source and workflow must belong to current main history.') from error
        try:
            project = ET.fromstring(git(root, 'show', source + ':src/GloomhavenVR/GloomhavenVR.csproj'))
        except ET.ParseError as error:
            raise NoProof('Tagged project version is not readable.') from error
        if project.findtext('.//Version') != tag[1:]:
            raise NoProof('Tagged source version does not match the requested release.')
        return source, tree(root, source)
    elif mode == 'release':
        if event.get('ref') != 'refs/heads/main' or event.get('after') != head:
            raise NoProof('Release proof is only valid for the exact pushed main commit.')
    else:
        raise NoProof('Unsupported proof request.')
    return head, tree(root, head)


class GitHub:
    def __init__(self, repository, token):
        if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
            raise NoProof('Invalid repository identity.')
        self.prefix = 'https://api.github.com/repos/' + repository
        self.token = token

    def get(self, path):
        request = Request(self.prefix + path, headers={
            'Accept': 'application/vnd.github+json',
            'Authorization': 'Bearer ' + self.token,
            'X-GitHub-Api-Version': '2022-11-28',
        })
        with urlopen(request, timeout=25) as response:
            return json.load(response)

    def runs(self, workflow_id):
        result = []
        for page in range(1, MAX_RUN_PAGES + 1):
            query = urlencode({'branch': 'dev', 'per_page': 100, 'page': page})
            batch = self.get(f'/actions/workflows/{workflow_id}/runs?{query}')['workflow_runs']
            result.extend(batch)
            if len(batch) < 100:
                break
        return result

    def jobs(self, run_id, attempt):
        result = []
        for page in range(1, 11):
            batch = self.get(f'/actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100&page={page}')['jobs']
            result.extend(batch)
            if len(batch) < 100:
                return result
        raise NoProof('Unexpectedly large job list; proof cannot be verified.')


def trusted_run(run, repository, workflow_id):
    return (run.get('repository', {}).get('full_name') == repository
            and run.get('head_repository', {}).get('full_name') == repository
            and run.get('workflow_id') == workflow_id
            and run.get('path') == WORKFLOW
            and run.get('event') in ('push', 'workflow_dispatch')
            and run.get('head_branch') == 'dev')


def verify_job(run, jobs, expected_tree):
    named = [job for job in jobs if job.get('name') == f'Full checks [{expected_tree}]']
    if len(named) != 1:
        raise NoProof('The latest matching run has no unique full-check job (reuse is not proof).')
    job = named[0]
    if (job.get('run_id') != run['id'] or job.get('run_attempt') != run['run_attempt']
            or job.get('head_sha') != run['head_sha'] or job.get('status') != 'completed'
            or job.get('conclusion') != 'success'):
        raise NoProof('The latest matching full-check job did not complete successfully.')
    markers = [step for step in job.get('steps', []) if step.get('name') == PROOF_STEP]
    if len(markers) != 1 or markers[0].get('status') != 'completed' or markers[0].get('conclusion') != 'success':
        raise NoProof('Full-check completion step is missing, skipped or unsuccessful.')


def find_proof(root, repository, wanted_tree, api, now=None):
    now = now or datetime.now(timezone.utc)
    workflow = api.get('/actions/workflows/ci.yml')
    if workflow.get('path') != WORKFLOW or workflow.get('state') != 'active':
        raise NoProof('The registered CI workflow is missing, inactive or has changed identity.')
    workflow_id = workflow['id']
    # A commit with equal files on an orphan branch is not dev validation. Fetching full
    # history in the workflow makes this comparison independent of GitHub branch labels.
    reachable = {}
    for line in git(root, 'log', '--format=%H %T', 'refs/remotes/origin/dev').splitlines():
        sha, tree_id = line.split()
        if tree_id == wanted_tree:
            reachable[sha] = tree_id
    # The executing workflow and proof verifier must be the version on trusted dev.
    # This also prevents a PR changing its own skip policy and reusing older evidence.
    for path in (WORKFLOW, HELPER):
        if git(root, 'rev-parse', f'HEAD:{path}') != git(root, 'rev-parse', f'origin/dev:{path}'):
            raise NoProof('Workflow or proof verifier differs from current trusted dev; run full checks.')
    candidates = [run for run in api.runs(workflow_id)
                  if trusted_run(run, repository, workflow_id) and run.get('head_sha') in reachable]
    if not candidates:
        raise NoProof('No full dev CI run exists for this exact source tree.')
    # Never search backwards past a newer failed/cancelled/in-progress run for green.
    latest = max(candidates, key=lambda run: (run['run_number'], run['id']))
    run = api.get(f"/actions/runs/{latest['id']}")  # latest attempt, not a cached list verdict
    if (not trusted_run(run, repository, workflow_id) or run.get('head_sha') not in reachable
            or run.get('id') != latest['id'] or run.get('status') != 'completed'
            or run.get('conclusion') != 'success' or int(run.get('run_attempt', 0)) < 1):
        raise NoProof('The latest matching CI run is not a completed successful trusted run.')
    updated = datetime.fromisoformat(run['updated_at'].replace('Z', '+00:00'))
    if updated > now + timedelta(minutes=5) or now - updated > timedelta(days=MAX_AGE_DAYS):
        raise NoProof('The latest matching CI evidence is stale or has an invalid timestamp.')
    jobs = api.jobs(run['id'], run['run_attempt'])
    verify_job(run, jobs, wanted_tree)
    # A rerun starting while jobs were read must invalidate the older result immediately.
    confirmed = api.get(f"/actions/runs/{run['id']}")
    if (confirmed.get('run_attempt'), confirmed.get('status'), confirmed.get('conclusion')) != (
            run['run_attempt'], 'completed', 'success'):
        raise NoProof('CI was rerun while its proof was being checked; retry after completion.')
    return f"https://github.com/{repository}/actions/runs/{run['id']}/attempts/{run['run_attempt']}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', required=True, choices=('pr', 'release', 'release-resume'))
    args = parser.parse_args()
    root = Path.cwd()
    reused, proof_url, reason = False, '', ''
    source_sha = commit(root, 'HEAD')
    wanted_tree = tree(root, 'HEAD')
    try:
        repository = os.environ['GITHUB_REPOSITORY']
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
        source_sha, wanted_tree = candidate(root, event, args.mode, repository, os.environ['GITHUB_SHA'],
                                           os.environ.get('GITHUB_EVENT_NAME'), os.environ.get('GITHUB_REF'))
        proof_url = find_proof(root, repository, wanted_tree, GitHub(repository, os.environ['GH_TOKEN']))
        reused = True
        reason = 'Successful full dev CI verified for the exact source tree.'
    except (NoProof, KeyError, ValueError, OSError, subprocess.CalledProcessError) as error:
        reason = str(error)
    output = os.environ.get('GITHUB_OUTPUT')
    if output:
        with open(output, 'a') as stream:
            stream.write(f'reuse={str(reused).lower()}\ntree={wanted_tree}\nproof_url={proof_url}\nsource_sha={source_sha}\n')
    print(reason)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a') as stream:
            stream.write(f'CI evidence: {"reused" if reused else "not reusable"}. Tree `{wanted_tree}`.\n\n')
            if reused:
                stream.write(f'[Full-check run]({proof_url}).\n')
            else:
                stream.write('Run full CI on dev for this exact tree, then retry the PR or release.\n')
    if args.mode in ('release', 'release-resume') and not reused:
        print('::error::Release blocked: run successful full CI on dev for this exact source tree, then rerun Release. Nothing was published.')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
