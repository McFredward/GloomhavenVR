#!/usr/bin/env python3
"""Run native merchant close/onboarding with portable explicit ports and causal controls.

When read-only game decompilation is present, bind its actual shop entry/exit,
FTUE start/finish, promise dispatch and map-listener bodies. Inventory/confirmation,
introduction presentation and Unity events remain explicit ports; purchase source
is separately checked for the absence of tutorial completion. CI retains native
boundary bodies; no Unity/game installation is required for the portable proof.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
TUTORIAL = 'src/GloomhavenVR/WorldUI/TownServices/TownServiceTutorialPatches.cs'
PROGRAM = 'tests/GloomhavenVR.TownAvailabilityTests/Program.cs'


def method_body(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 1
    cursor = opening + 1
    while depth:
        if text[cursor] == '{': depth += 1
        elif text[cursor] == '}': depth -= 1
        cursor += 1
    return text[opening:cursor]


def replace_one(text, before, after):
    if text.count(before) != 1:
        raise RuntimeError('Production mutation binding drift: ' + before)
    return text.replace(before, after, 1)


def replace_method(text, signature, body):
    start = text.index(signature)
    opening = text.index('{', start)
    original = method_body(text, signature)
    return text[:opening] + body + text[opening + len(original):]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-onboarding624')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--portable', action='store_true', help='Use the explicit CI native ports even when local game sources exist')
    args = parser.parse_args()
    source = args.source_root.resolve()
    files = {path: (source / path).read_text() for path in (
        TUTORIAL, PROGRAM, 'src/GloomhavenVR/WorldUI/TownServices/TownServiceAvailability.cs',
        'src/GloomhavenVR/WorldUI/MapRoom/TownWindowCloseScope.cs',
        'src/GloomhavenVR/WorldUI/TownServices/TownServicePopulation.cs',
        'tests/GloomhavenVR.TownAvailabilityTests/GloomhavenVR.TownAvailabilityTests.csproj')}
    native_bindings = {}
    native_contracts = {}
    native_root = source / 'decompiled/GH.Runtime'
    if not native_root.is_dir() and (ROOT / 'ressources').is_symlink():
        native_root = (ROOT / 'ressources').resolve().parent / 'decompiled/GH.Runtime'
    if native_root.is_dir() and not args.portable:
        for native_file, native_signature, port_signature in (
            ('UIShopItemWindow.cs', 'public void Exit()', 'private void NativeExit()'),
            ('UIShopItemWindow.cs', 'private void OnHidden()', 'private void OnHidden()'),
            ('UIShopItemWindow.cs', 'private void FinishEnterShop()', 'private void FinishEnterShop()'),
            ('MapFTUEManager.cs', 'public void CompleteStep(EMapFTUEStep step)', 'internal void CompleteStep(EMapFTUEStep step)'),
            ('MapFTUEManager.cs', 'public ICallbackPromise StartStep(IMapFTUEStep step)', 'private Assets.Script.Misc.ICallbackPromise NativeStartStep(IMapFTUEStep step)'),
            ('MapFTUEManager.cs', 'private void OnCompleteStep(EMapFTUEStep step)', 'private void OnCompleteStep(EMapFTUEStep step)'),
            ('UIMapFTUEStep.cs', 'public virtual ICallbackPromise StartStep()', 'private Assets.Script.Misc.ICallbackPromise NativeStartStep()'),
            ('UIMapFTUEStep.cs', 'public virtual void FinishStep()', 'private void NativeFinishStep()'),
            ('UIMapFTUEStep.cs', 'protected virtual void OnFinishedStep()', 'internal void CompleteHide()'),
            ('UIMapFTUEStepStartListener.cs', 'private void OnStepCompleted(EMapFTUEStep step)', 'private void OnStepCompleted(EMapFTUEStep step)'),
            ('UIMapFTUECompleteStepToggleListener.cs', 'private void OnValueChanged(bool isOn)', 'private void OnValueChanged(bool isOn)'),
            ('UIMapFTUECompleteStepListener.cs', 'public void CompleteStep()', 'private void CompleteStep()'),
            ('Assets.Script.Misc/CallbackPromise.cs', 'public void Done(Action callback, Action onCancelled = null)', 'internal void Done(Action? callback, Action? onCancelled = null)'),
            ('Assets.Script.Misc/CallbackPromise.cs', 'public void Cancelled(Action callback)', 'internal void Cancelled(Action callback)'),
            ('Assets.Script.Misc/CallbackPromise.cs', 'public ICallbackPromise Then(Action onDone, Action onCancel = null)', 'public ICallbackPromise Then(Action onDone, Action? onCancel = null)'),
            ('Assets.Script.Misc/CallbackPromise.cs', 'public void Resolve()', 'internal void Resolve()'),
            ('Assets.Script.Misc/CallbackPromise.cs', 'public void Cancel()', 'internal void Cancel()'),
            ('Assets.Script.Misc/CallbackPromise.cs', 'public static CallbackPromise Resolved()', 'internal static CallbackPromise Resolved()')):
            body = method_body((native_root / native_file).read_text(), native_signature)
            # These read-only native methods predate nullable reference annotations.
            # Suppress annotation warnings only inside that bound method body.
            # The explicit FTUE hide port uses CompleteHide for OnFinishedStep.
            bound_body = '{\n#nullable disable warnings\n' + body[1:-1].replace('OnFinishedStep()', 'CompleteHide()').replace('Show(OnFinishedStep)', 'Show(CompleteHide)') + '\n#nullable restore warnings\n}'
            files[PROGRAM] = replace_method(files[PROGRAM], port_signature, bound_body)
            native_bindings[native_file + ':' + native_signature] = hashlib.sha256(body.encode()).hexdigest()
        for signature in (
            'private void OnSelectedItemBuy(MerchantItemDecorator slot)',
            'private void BuyItem(MerchantItemDecorator itemUI, CMapCharacter targetCharacter = null, uint targetItemNetworkID = 0u, bool equip = true)'):
            body = method_body((native_root / 'UIShopItemInventory.cs').read_text(), signature)
            if 'MapFTUE' in body or 'CompleteStep(' in body or 'FinishStep(' in body:
                raise SystemExit('Native purchase tutorial contract changed: ' + signature)
            native_contracts['UIShopItemInventory.cs:' + signature] = hashlib.sha256(body.encode()).hexdigest()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    (run / 'sources.json').write_text(json.dumps({'root': str(source), 'native': native_bindings,
        'native_purchase_contracts': native_contracts,
        'sha256': {path: hashlib.sha256(text.encode()).hexdigest() for path, text in files.items()}}, indent=2) + '\n')
    cases = [('production', '', '', '')]
    if not args.no_negative_controls:
        cases += [
            ('missing-close-continuation', 'close.Manager.CompleteStep(EMapFTUEStep.BuyItem);', '{}',
             'real converted merchant close completes BuyItem after native cleanup even with a consumed WorldMap listener'),
            ('premature-continuation', '=> __state = TownServiceTutorialPatches.CaptureMerchantClose(__instance);',
             '{ __state = TownServiceTutorialPatches.CaptureMerchantClose(__instance); TownServiceTutorialPatches.CompleteMerchantClose(__state); }',
             'real converted merchant close completes BuyItem after native cleanup even with a consumed WorldMap listener'),
            ('temporary-hide-consumes-step', 'if (!TownWindowCloseScope.Active && !ReferenceEquals(_nativeExitShop, shop)) return null;',
             'if (!WorldUIConfig.ConversionActive) return null;',
             'temporary original shop presentation hide cannot consume a pending lesson'),
            ('wrong-native-step', '&& ReferenceEquals(CurrentStepField?.GetValue(manager), step)', '',
             'closing the shop cannot complete a different original step with the same enum'),
            ('old-save-continuation', '|| !ReferenceEquals(AdventureState.MapState, close.Map)', '',
             'native onExit replacing the loaded save invalidates the captured merchant continuation'),
            ('reentrant-finish', 'if (close == null || ReferenceEquals(_closingPromise, close.Promise)', 'if (close == null || close.Promise == null',
             'reentrant and delayed native hides request completion once per exact promise'),
            ('exit-scope-leak', 'scope.Dispose();', '{}',
             'failed native exit restores its scope and cannot turn a later temporary hide into progression'),
            ('sticky-converted-close-missing', 'if (window != null && !window.IsOpen) CompleteMerchantClose(close);', '{}',
             'real X of an already-native-hidden converted merchant completes its exact lesson once'),
            ('converted-close-before-hide', 'if (window != null && !window.IsOpen) CompleteMerchantClose(close);',
             'if (window != null) CompleteMerchantClose(close);',
             'a converted close that leaves the native shop open cannot consume its lesson'),
            ('immersive-pending-promise', '__result = CallbackPromise.Resolved();', '__result = new CallbackPromise();',
             'VisitMerchant to none to BuyItem continues once after a delayed native hide'),
            ('failed-finish-no-retry', 'if (close != null && ReferenceEquals(_closingPromise, close.Promise)) _closingPromise = null;', '{}',
             'failed completion dispatch releases the pending promise marker for a later real close'),
            ('capture-escape', 'try { return CaptureMerchantCloseCore(shop); }', 'CaptureMerchantCloseCore(shop); try { return CaptureMerchantCloseCore(shop); }',
             'a failing close capture cannot prevent the original native shop cleanup'),
            ('postfix-escape', 'try { CompleteMerchantCloseCore(close); }',
             'CompleteMerchantCloseCore(close); try { CompleteMerchantCloseCore(close); }',
             'a failing postfix eligibility probe cannot escape native shop dispatch'),
            ('default-finalizer-resets-scope', 'if (_entered) _nativeExitShop = _previous;', 'if (_entered || !_entered) _nativeExitShop = _previous;',
             'a skipped prefix default finalizer cannot reset an outer native shop exit scope'),
            ('option-resets-pending-hide', 'if (!Active(manager) || OriginalBuyStep() == null) return;',
             'if (!Active(manager) || OriginalBuyStep() == null) { _pendingManager = null; _pendingStep = EMapFTUEStep.None; _pendingPromise = null; return; }',
             'option off/on cannot finish the same delayed BuyItem promise more than once'),
        ]
        cases += [
            ('missing-native-entry', 'internal void Enter() => FinishEnterShop();', 'internal void Enter() { }',
             'native shop starts its original pending BuyItem promise', PROGRAM),
            ('confirmation-closes-shop', 'private void CloseConfirmation() { Open = false; _ = shop; }',
             'private void CloseConfirmation() { Open = false; shop.Exit(); }',
             'purchase confirmation cancel preserves the pending native exit lesson', PROGRAM),
            ('purchase-completes-tutorial', 'internal void Confirm() { Gold -= 10; Items++; CloseConfirmation(); }',
             'internal void Confirm() { Gold -= 10; Items++; CloseConfirmation(); Singleton<MapFTUEManager>.Instance!.CompleteStep(EMapFTUEStep.BuyItem); }',
             'native purchase boundary changes inventory only and never completes the exit lesson', PROGRAM),
            ('missing-next-native-map-step', 'Singleton<MapFTUEManager>.Instance.StartStep(stepToStart);', '{}',
             'actual native completion listener advances InteractWithMap after cleaned merchant exit', PROGRAM),
            ('inactive-native-entry', 'if (!isPlaying || HasCompletedStep(step.Step))',
             'if (HasCompletedStep(step.Step))',
             'native inactive manager refuses an uncompleted merchant step without creating its promise', PROGRAM),
        ]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    report = []
    for item in cases:
        name, before, after, expected = item[:4]
        mutation_path = item[4] if len(item) == 5 else TUTORIAL
        case = run / name
        for path, text in files.items():
            target = case / path
            target.parent.mkdir(parents=True, exist_ok=True)
            if path == mutation_path and before: text = replace_one(text, before, after)
            target.write_text(text)
        project = case / 'tests/GloomhavenVR.TownAvailabilityTests/GloomhavenVR.TownAvailabilityTests.csproj'
        completed = subprocess.run([dotnet, 'run', '--project', str(project), '-c', 'Release'],
            capture_output=True, text=True, env={**os.environ, 'DOTNET_NOLOGO': '1'})
        output = completed.stdout + completed.stderr
        (case / 'run.log').write_text(output)
        if 'error CS' in output or 'Build FAILED' in output:
            raise SystemExit('Compilation failure is not a passing negative control: ' + name + '\n' + output)
        valid = completed.returncode == 0 if not expected else completed.returncode != 0 and expected in output
        report.append({'case': name, 'pass': valid, 'expected': expected, 'exit': completed.returncode})
        if not valid:
            (run / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
            raise SystemExit('FAIL ' + name + '\n' + output + '\nEvidence: ' + str(run))
        print('PASS ' + name + ': ' + (expected or output.strip()), flush=True)
    (run / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'PASS {len(cases)} native onboarding production/control variants; {len(native_bindings)} source-bound native methods; {len(native_contracts)} checked native purchase boundaries; evidence: {run}')


if __name__ == '__main__':
    main()
