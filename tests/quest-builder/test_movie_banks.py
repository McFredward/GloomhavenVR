"""Small source-bound media resume tests; synthetic files, no codec or game assets."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import movie_banks as banks
import storage


class Progress:
    def __init__(self): self.events = []
    def event(self, *args, **kwargs): self.events.append((args, kwargs))
    def operation(self, *args, **kwargs): pass


class MovieBankTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / 'source'; self.game = self.root / 'game'; self.output = self.root / 'output'
        # Keep the production canonical identities, but replace every native
        # frame/source with a one-frame synthetic subject. No original bytes,
        # depth payload, optional packages or actual FFmpeg are consumed here.
        recipe = json.loads((ROOT / banks.RECIPE).read_text())
        originals = {}
        for index, movie in enumerate(recipe['movies']):
            raw = ('synthetic native movie ' + str(index)).encode()
            movie.update(frames=1, source_sha256=hashlib.sha256(raw).hexdigest(), source_bytes=len(raw),
                         shots=[{'start': 0, 'end': 1, 'mode': 'screen'}])
            if movie['kind'] == 'external': name = movie['source_relative']
            else:
                name = movie['resource']; movie.update(resource_offset=0, resource_size=len(raw))
            path = self.game / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw)
            originals[name] = banks._record(path, name)
        storage.write_json(self.source / banks.RECIPE, recipe)
        for name in sorted(banks.SOURCE_HELPERS | {
                'tools/quest-movies/StereoVideoUI.shader', 'tools/quest-movies/QuestMovieEye.shader',
                'tools/quest-movies/MovieBankBuild.cs', banks.CP + 'GH_CP_Intro.mov', banks.CP + 'GH_CP_Intro.sbs-v2.mov'}):
            path = self.source / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(('synthetic selected source ' + name).encode())
        self.game_info = {'files': list(originals.values())}
        self.inputs = self.selected_inputs()
        self.progress = Progress()
        self.builder = types.SimpleNamespace(build_progress=self.progress,
            host_resources=types.SimpleNamespace(detect_host=lambda _: {'availableMemoryBytes': 64 * 1024**3, 'effectiveCpus': 16}),
            toolchain=lambda *_: self.fail('A warm movie/shader bank must not provision Unity'))
        self.args = types.SimpleNamespace(command='update-mod')
        self.shader = self.root / banks.BANK; self.shader.write_bytes(b'UnityFS\0synthetic qualified shader payload')
        self.calls = []
        with patch.object(banks, '_python', return_value=Path(sys.executable)), \
                patch.object(banks, '_shader_bank', return_value=self.shader), \
                patch.object(banks, '_child', side_effect=self.fake_child):
            self.first = banks.produce(self.builder, self.args, self.source, self.inputs, self.output, self.game)
        self.assertEqual(len(self.calls), 22)
        self.calls.clear(); self.progress.events.clear()
        self.addCleanup(banks._CANCEL_CHILDREN.clear)

    def selected_inputs(self):
        expected = banks.plan(self.source, self.game_info)
        return {'game': self.game_info, 'mod': {'files': expected['recipe']['sources']}}

    def fake_child(self, builder, python, source, job, folder, on_progress, output):
        self.calls.append(job)
        Path(job['target']).write_bytes(('synthetic encoded ' + job['source']).encode())
        on_progress(job['frames'])

    def warm(self, *, previous=None, output=None, base_apk=None):
        return banks.delivery(self.builder, self.source, self.inputs, output or self.output, self.game,
            args=self.args, previous=previous or self.first['metadata'], base_apk=base_apk)

    def without_conversion(self):
        self.addCleanup(patch.stopall)
        patch.object(banks, '_python', side_effect=AssertionError('Warm films cannot install dependencies')).start()
        patch.object(banks, '_child', side_effect=AssertionError('Warm films cannot render')).start()

    def test_warm_all23_uses_only_metadata_and_never_resets_aggregate_progress(self):
        self.without_conversion()
        with patch.object(banks, '_shader_bank', return_value=self.shader): result = self.warm()
        self.assertEqual(set(result), {banks.METADATA})
        self.assertEqual(len(json.loads(result[banks.METADATA])['files']), 23)
        counters = [(args[1], args[2]) for args, _ in self.progress.events if len(args) >= 3]
        self.assertTrue(counters)
        self.assertEqual(counters, sorted(counters))
        self.assertEqual(counters[-1][0], counters[-1][1])
        self.assertTrue(all(done < total for done, total in counters[:-1]))

    def test_producer_revision_keeps_all_independent_movie_keys(self):
        helper = self.source / 'tools/quest-builder/movie_banks.py'
        helper.write_bytes(helper.read_bytes() + b' orchestration-only revision')
        self.inputs = self.selected_inputs(); self.without_conversion()
        with patch.object(banks, '_shader_bank', return_value=self.shader): result = self.warm()
        metadata = json.loads(result[banks.METADATA])
        self.assertNotEqual(metadata['producerKey'], self.first['metadata']['producerKey'])
        self.assertEqual(metadata['movieKeys'], self.first['metadata']['movieKeys'])
        self.assertEqual(set(result), {banks.METADATA})

    def corrupt_one(self):
        key = self.first['metadata']['movieKeys']['CP_Intro/GH_CP_Intro.mov']
        target = self.output / 'cache/quest-movies/films' / key / 'movie.mov'
        target.write_bytes(b'interrupted or corrupt local result')
        return target

    def test_only_corrupt_film_is_reconstructed_and_other21_remain_retained(self):
        target = self.corrupt_one()
        with patch.object(banks, '_python', return_value=Path(sys.executable)), \
                patch.object(banks, '_shader_bank', return_value=self.shader), \
                patch.object(banks, '_child', side_effect=self.fake_child): result = self.warm()
        self.assertEqual(len(self.calls), 1)
        self.assertEqual(Path(self.calls[0]['target']).parent, target.parent)
        self.assertEqual(set(result), {banks.METADATA})  # Same original output restored.

    def test_allocation_failure_retries_only_incomplete_film_serially(self):
        self.corrupt_one(); attempts = []
        def fail_once(*args):
            attempts.append(args[3]['threads'])
            if len(attempts) == 1: raise banks.MovieMemoryError('synthetic witnessed allocation failure')
            return self.fake_child(*args)
        with patch.object(banks, '_python', return_value=Path(sys.executable)), \
                patch.object(banks, '_shader_bank', return_value=self.shader), \
                patch.object(banks, '_child', side_effect=fail_once): self.warm()
        self.assertEqual(attempts, [3, 1]); self.assertEqual(len(self.calls), 1)

    def test_apk_only_retains_films_and_hydrates_only_tiny_shader_without_unity(self):
        apk = self.root / 'base.apk'
        with zipfile.ZipFile(apk, 'w') as archive:
            for member, path in self.first['files']: archive.write(path, member)
        self.without_conversion(); cold = self.root / 'apk-only'
        result = self.warm(output=cold, base_apk=apk)
        self.assertEqual(set(result), {banks.METADATA})
        self.assertTrue(banks.shader_cached(cold, self.first['metadata']['shaderKey']))
        self.assertFalse((cold / 'cache/quest-movies/films').exists())

    def test_source_drift_during_conversion_cannot_publish_old_producer_key(self):
        self.corrupt_one()
        def drift(*args):
            self.fake_child(*args)
            path = self.source / 'tools/quest-builder/movie_render.py'; path.write_bytes(b'changed during conversion')
        with patch.object(banks, '_python', return_value=Path(sys.executable)), \
                patch.object(banks, '_shader_bank', return_value=self.shader), \
                patch.object(banks, '_child', side_effect=drift), \
                self.assertRaisesRegex(storage.BuildError, 'source changed during conversion'): self.warm()
        self.assertEqual(len(self.calls), 1)

    def test_cancelled_child_is_never_launched(self):
        banks._CANCEL_CHILDREN.set()
        with patch.object(banks.subprocess, 'Popen', side_effect=AssertionError('cancelled process launched')), \
                self.assertRaisesRegex(storage.BuildError, 'cancelled before launch'):
            banks._child(self.builder, Path(sys.executable), self.source, {}, self.root / 'cancelled', lambda _: None, self.output)

    def test_worker_policy_uses_cpu_available_memory_commit_and_explicit_limit(self):
        gib = 1024**3
        self.assertEqual(banks.worker_count({'availableMemoryBytes': 64*gib, 'effectiveCpus': 64}, 19), 16)
        self.assertEqual(banks.worker_count({'availableMemoryBytes': 2*gib, 'effectiveCpus': 64}, 19), 1)
        self.assertEqual(banks.worker_count({'availableMemoryBytes': 64*gib, 'commitHeadroomBytes': 2*gib, 'effectiveCpus': 64}, 19), 1)
        self.assertEqual(banks.worker_count({'availableMemoryBytes': 64*gib, 'effectiveCpus': 64}, 19, 3), 3)
        self.assertEqual(banks.worker_count({'effectiveCpus': 64}, 19), 1)

    def test_missing_owned_source_is_rejected_before_provisioning(self):
        with self.assertRaisesRegex(storage.BuildError, 'Owned original stereo movie source is missing'):
            banks.plan(self.source, {'files': []})
        self.assertEqual(banks.source_rows([{'path': 'src/GloomhavenVR/Assets/Movies/Heroes/BeastTyrant.sbs-v1.mov'}]), [])


if __name__ == '__main__': unittest.main()
