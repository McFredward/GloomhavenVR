"""Installed APK sources cross the real Wizard seams without PC data or ADB I/O.

ADB byte acquisition/signature/seed proofs belong to installed_source tests; these
fixtures prove selection, retained identity, resource reservation and CLI handoff.
"""
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import discovery
import provision
import qualification
import wizard
from state import Store, WizardError


class InstalledUpdateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name); self.store = Store(self.root / 'workspace')
        self.apk = self.root / 'installed.apk'; self.apk.write_bytes(b'APK fixture')
        self.engine = wizard.Engine(self.store)

    def choice(self, **extra):
        return wizard.choices({'mode': 'update-mod', 'baseApk': str(self.apk), 'installedGame': True, **extra})

    def test_only_explicit_boolean_updates_allow_missing_pc_copy(self):
        self.assertNotIn('gameRoot', self.choice())
        for value in ({'mode':'build','installedGame':True}, {'mode':'update-mod','installedGame':'yes'},
                      {'mode':'update-mod','baseApk':str(self.apk)}):
            with self.subTest(value=value), self.assertRaises(WizardError): wizard.choices(value)

    def test_source_change_discards_stale_pc_identity_and_dlc_overrides(self):
        result = self.choice(gameRoot='oldPC', steamRoot='Steam', steamId='7656119', steamLogo='old.png', ownedDlc=['jotl'])
        self.assertFalse({'gameRoot','steamRoot','steamId','steamLogo','ownedDlc'} & result.keys())
        self.assertEqual(result['baseApk'], str(self.apk.absolute()))

    def test_freeze_exact_imported_apk_and_device_without_discovery_side_effects(self):
        source = {'deviceSerial':'USB_TEST','versionName':'1.2.0.B717.123456789abc'}
        with patch.object(discovery, 'default_apk', side_effect=AssertionError('must retain selection')):
            state = self.engine.plan({**self.choice(), 'deviceSource':source})
        self.assertEqual(state['choices']['deviceSource'], source)
        with self.assertRaises(WizardError): self.choice(deviceSource={**source, 'remotePath':'/unexpected'})
        self.assertEqual(discovery.recent_sessions(self.store)[0][0]['session'], state['session'])

    def test_changed_apk_at_same_path_invalidates_seed_source_and_profile_receipts(self):
        saved=self.store.create(self.choice(mode='update-profile',profileName='Name'))
        before={stage:self.engine.key(saved,stage) for stage in ('source','profile')}
        self.apk.write_bytes(b'different APK identity in the same selected path')
        for stage in before:self.assertNotEqual(before[stage],self.engine.key(saved,stage))

    def test_cli_handoff_has_no_pc_profile_or_logo_arguments(self):
        state = self.store.create(self.choice())
        state['completed'] = {'tools':{'details':{'dotnet8':'dn8','dotnet10':'dn10'}},
            'source':{'details':{'sourceRoot':str(ROOT)}}, 'profile':{'details':{'retainedFromApk':True}},
            'unity':{'details':{'unityEditor':'Unity'}}}
        arguments = self.engine.arguments(state, 'build')
        self.assertIn('--installed-game', arguments); self.assertIn('--base-apk', arguments)
        for flag in ('--game-root','--profile-json','--steam-logo','--dlc-ownership-json'):
            self.assertNotIn(flag, arguments)
        state['choices']['installedGame'] = False
        with self.assertRaises(KeyError): self.engine.arguments(state, 'build')

    def test_profile_retention_never_downloads_or_opens_launcher(self):
        state = self.store.create(self.choice())
        state['completed'] = {'source':{'details':{'sourceRoot':str(ROOT)}}}
        with self.store.active(state), patch.object(provision, 'download', side_effect=AssertionError('no logo download')), \
             patch.object(self.engine, 'profile_launcher_required', side_effect=AssertionError('no launcher')):
            paths, details = self.engine.stage_profile(state, Mock())
        self.assertTrue(details['retainedFromApk']); self.assertNotIn('profilePath', details)
        self.assertEqual(json.loads(paths[0].read_text())['retainedFromApk'], str(self.apk))

    def test_name_only_edit_copies_bounded_identity_without_steam_lookup(self):
        state = self.store.create(self.choice(mode='update-profile', profileName='New name'))
        state['completed'] = {'source':{'details':{'sourceRoot':str(ROOT)}}}
        core = SimpleNamespace(PROFILE='profile', _json=Mock(return_value={'schema':1,'provider':'gog','providerId':'gog-original','displayName':'Original'}), profile_update=Mock())
        module = SimpleNamespace(apk_updates=core)
        # A tiny real ZIP still exercises the selected APK open boundary.
        import zipfile
        with zipfile.ZipFile(self.apk, 'w') as archive: archive.writestr('profile', '{}')
        with self.store.active(state), patch.object(discovery, 'builder', return_value=module), \
             patch.object(provision, 'download', side_effect=AssertionError('no logo download')):
            _, details = self.engine.stage_profile(state, Mock())
        value = json.loads(Path(details['profilePath']).read_text())
        self.assertEqual(value, {'schema':1,'provider':'gog','providerId':'gog-original','displayName':'New name'})
        core.profile_update.assert_called_once_with(self.apk, value)

    def test_source_provisioning_uses_qualified_seed_dlls_not_fake_pc_files(self):
        state = self.store.create(self.choice())
        state['completed'] = {'tools':{'details':{'dotnet8':'dn8'}}}
        data = self.root / 'seed/Data'; (data / 'Managed').mkdir(parents=True)
        updates = SimpleNamespace(preflight=Mock(return_value=({}, data, {})))
        helper = SimpleNamespace(_local_helper=lambda name: updates)
        with patch.object(discovery,'builder',return_value=helper), patch.object(provision,'source_checkout',return_value=([],{})) as checkout:
            self.engine.stage_source(state, Mock())
        selected = checkout.call_args.args[2]
        self.assertTrue(selected['installedGame']); self.assertEqual(selected['gameRoot'], str(data))
        self.assertNotIn('gameRoot', state['choices'])
        self.assertTrue(updates.preflight.call_args.kwargs['installed_game'])

    def test_capacity_reserves_compiler_and_apk_without_qualifying_pc(self):
        helper = SimpleNamespace(game_data=Mock(side_effect=AssertionError('no PC input')))
        result = qualification.qualify(self.store.root, system='Linux', machine='x86_64', free_bytes=100*qualification.GIB,
            game_root=None, mode='update-mod', installed_game=True, base_apk=self.apk, helper=helper)
        self.assertGreaterEqual(result['requiredFreeBytes'], 40*qualification.GIB)
        helper.game_data.assert_not_called()


    def test_missing_seed_is_a_pc_assisted_input_action_not_failed_rebuild(self):
        saved=self.store.create(self.choice())
        message="This older Quest APK has no portable compiler references. Run one PC-assisted update."
        updates=SimpleNamespace(preflight=Mock(side_effect=ValueError(message)))
        helper=SimpleNamespace(_local_helper=lambda name:updates)
        actions={'tools':Mock(side_effect=AssertionError('No tool provisioning before source decision'))}
        with patch.object(discovery,'builder',return_value=helper):
            result=wizard.Engine(self.store,actions=actions).run(saved['session'])
        self.assertEqual(result['status'],'blocked')
        action=result['needsActions'][0]
        self.assertEqual(action['code'],'installed_update_pc_required')
        self.assertTrue(action['parameters']['requiresPcUpdateSource'])
        self.assertEqual(result['choices']['baseApk'],str(self.apk));actions['tools'].assert_not_called()

    def test_retained_profile_has_no_steam_logo_network_requirement(self):
        import network_preflight
        saved=self.store.create(self.choice(mode='update-profile'))
        rows=network_preflight.inventory(self.store.root,ROOT,saved,pending=['profile'],host='linux-x64')
        self.assertEqual(rows,[])

    def test_reference_only_xr_preparation_never_qualifies_a_full_game(self):
        import runtime_dependencies
        saved=self.store.create(self.choice())
        data=self.root/'seed/Data';(data/'Managed').mkdir(parents=True)
        helper=SimpleNamespace(game_data=Mock(side_effect=AssertionError('This is not a full PC game')))
        supervisor=SimpleNamespace(store=self.store,session=saved['session'])
        with self.store.active(saved),patch.object(discovery,'builder',return_value=helper), \
             patch.object(runtime_dependencies,'derive',return_value='qualified-reference-cache') as derive:
            value=provision.derive_runtime_dependencies(ROOT,data,{},supervisor,self.root,lambda:None,reference_only=True)
        self.assertEqual(value,'qualified-reference-cache');self.assertEqual(derive.call_args.args[1],data/'Managed')
        helper.game_data.assert_not_called()


    def test_installed_update_run_resume_reuses_completed_stages_and_build_progress_without_pc_profile(self):
        real=discovery.builder(ROOT)
        helper=SimpleNamespace(apk_input_hashes=real.apk_input_hashes,
            _local_helper=lambda name:SimpleNamespace(preflight=lambda *args,**kwargs:({},self.root/'seed',{})))
        for mode in ('update-mod','update-profile'):
            with self.subTest(mode=mode):
                saved=self.store.create(self.choice(mode=mode,**({'profileName':'Changed name'} if mode=='update-profile' else {})))
                calls=[];seen=[]
                def perform(stage):
                    def action(state, supervisor):
                        calls.append(stage)
                        marker=self.store.session_dir(state['session'])/(stage+'.output');marker.write_text(stage)
                        details={}
                        if stage=='source': details={'sourceRoot':str(ROOT)}
                        if stage=='profile' and mode=='update-profile':
                            details={'profilePath':str(marker)}
                            marker.write_text(json.dumps({'schema':1,'provider':'gog','providerId':'original','displayName':'Changed name'}))
                        if stage=='inspect':details={'gameKey':'a'*64,'baseApkSha256':'b'*64,'inputKey':'c'*64}
                        if stage=='build':
                            row=state['stages'][5];seen.append(row.get('progressWorkKey'))
                            if calls.count('build')==1:
                                self.store.operation(state['session'],'build','update-owned-game',complete=True)
                                raise wizard.Cancelled()
                            self.assertIn('update-owned-game',row['progressPlan']['completed'])
                            self.assertGreater(row['progressPlan']['percent'],0)
                        return [marker],details
                    return action
                actions={stage:perform(stage) for stage in wizard.STAGES}
                with patch.object(discovery,'builder',return_value=helper):
                    first=wizard.Engine(self.store,actions=actions).run(saved['session'])
                    self.assertEqual(first['status'],'cancelled')
                    second=wizard.Engine(self.store,actions=actions).run(saved['session'])
                self.assertEqual(second['status'],'complete');self.assertTrue(seen[0]);self.assertEqual(seen[0],seen[1])
                for stage in wizard.STAGES[:5]:self.assertEqual(calls.count(stage),1)
                self.assertEqual(calls.count('build'),2);self.assertEqual(calls.count('install'),1)

    def test_game_derived_asset_changes_request_pc_before_preparing_unity(self):
        saved=self.store.create(self.choice());saved['completed']={'source':{'details':{'sourceRoot':str(ROOT)}}}
        core=SimpleNamespace(inspect_base=lambda apk:{})
        updates=SimpleNamespace(update_requirements=lambda *args,**kwargs:{'requiresUnity':True,'artChanged':True})
        helper=SimpleNamespace(apk_updates=core,_local_helper=lambda name:updates,
            source_inventory=lambda root:([],None,None),mod_build=lambda root:717)
        import unity_setup
        with self.store.active(saved),patch.object(discovery,'builder',return_value=helper), \
             patch.object(unity_setup,'prepare',side_effect=AssertionError('No compiler setup before the PC input choice')):
            with self.assertRaises(WizardError) as error:self.engine.stage_unity(saved,Mock())
        self.assertEqual(error.exception.code,'installed_update_pc_required')
        self.assertEqual(error.exception.parameters['pcSourceReason'],'derived-assets')


if __name__ == '__main__': unittest.main()
