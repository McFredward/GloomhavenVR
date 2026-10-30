"""Bind unchanged native callback/selection bodies for the navigation regression."""
import hashlib
import subprocess


def inputs(root, runtime, env, method, native_window, native_item, native_enhancement):
    native = {}
    for name in ('ConfirmationBox', 'UIConfirmationBoxManager', 'NewPartyDisplayUI', 'UIPerksInventory', 'UIMenuOption', 'UIOptionsWindow',
                 'UIAdventureCharacterConfirmationBox', 'UIAdventurePartyAssemblyWindow', 'UICampaignAdventurePartyAssemblyWindow'):
        native[name] = subprocess.run([str(__import__('pathlib').Path.home()/'.dotnet/tools/ilspycmd'), '-t', name, str(runtime)],
            check=True, capture_output=True, text=True, env=env).stdout
    prefix = '#nullable disable\nusing System;using System.Collections.Generic;using System.Linq;using UnityEngine;using UnityEngine.UI;using UnityEngine.Events;using GLOOM;using ScenarioRuleLibrary;\n'
    generic = prefix + 'public partial class ConfirmationBox {\n'
    for signature in (
        'public void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, Color?',
        'private void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, UnityAction onActionCancelled, Color?',
        'public void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, UnityAction onActionCancelled, string',
        'public void ShowGenericCancelConfirmation(', 'public void ShowGenericSpendConfirmation(',
        'public void ResetConfirmationBox()', 'private void HideInternal()', 'public void Hide()',
        'public void OnHidden()', 'private void OnShown()', 'private void OnSubmit()', 'private void OnCancel()'):
        generic += method(native['ConfirmationBox'], signature) + '\n'
    generic += '}\npublic partial class UIConfirmationBoxManager {\n'
    for signature in ('public void ShowGenericConfirmation(', 'public void ShowGenericCancelConfirmation('):
        generic += method(native['UIConfirmationBoxManager'], signature) + '\n'
    generic += 'private void ShowGenericWarningConfirmation(){}private void ShowGenericSpendConfirmation(){}private void ShowCancelActiveAbility(){}\n}\n'
    selections = prefix + 'public partial class NewPartyDisplayUI {\n'
    for signature in ('private void SelectCurrentCharacter(', 'private void OnItemsSelected(',
                      'private void OnCardsSelected(NewPartyCharacterUI data, bool isSelected, bool animate', 'private void OnPerksSelected('):
        selections += method(native['NewPartyDisplayUI'], signature) + '\n'
    selections += '}\npublic partial class UIPerksInventory {\n'
    for signature in ('private void OnSelectedPerk(', 'public void Display('):
        selections += method(native['UIPerksInventory'], signature) + '\n'
    selections += '}\n'
    inventory = subprocess.run([str(__import__('pathlib').Path.home()/'.dotnet/tools/ilspycmd'), '-t', 'UIPartyItemInventoryDisplay', str(runtime)],
        check=True, capture_output=True, text=True, env=env).stdout
    native['UIPartyItemInventoryDisplay'] = inventory
    # Both unbound-item callbacks are unchanged native source. Other equipment/
    # network branches are not executed; inventory services remain declared ports.
    start = inventory.index('\t\t\tselectedItemToCofirmSwitch = itemSlot;')
    end = inventory.index('\n\t\t}\n\t\telse if (flag)', start)
    selections += 'public partial class UIPartyItemInventoryDisplay {\n' + method(inventory, 'public void Display(')
    selections += '\nprivate void OnItemSelected(UIPartyItemSlot itemSlot){\n' + inventory[start:end] + '\n}\n}\n'
    native['GLOOM.MainMenu.UICreateGameDLCStep'] = subprocess.run([str(__import__('pathlib').Path.home()/'.dotnet/tools/ilspycmd'), '-t', 'GLOOM.MainMenu.UICreateGameDLCStep', str(runtime)],
        check=True, capture_output=True, text=True, env=env).stdout
    selections += 'namespace GLOOM.MainMenu {public partial class UICreateGameDLCStep {\n'
    selections += method(native['GLOOM.MainMenu.UICreateGameDLCStep'], 'private IEnumerator SkipAFrameBeforeMessage(') + '\n}}\n'
    selections = selections.replace('using System;using System.Collections.Generic;', 'using System;using System.Collections;using System.Collections.Generic;', 1)
    native['UITempleWindow'] = subprocess.run([str(__import__('pathlib').Path.home()/'.dotnet/tools/ilspycmd'), '-t', 'UITempleWindow', str(runtime)],
        check=True, capture_output=True, text=True, env=env).stdout
    selections += 'public partial class UITempleWindow {\n' + method(native['UITempleWindow'], 'private void OnSelectedCharacter(') + '\n}\n'
    for name, signatures in (
        ('UIMultiplayerConfirmationBox', ('public void ShowConfirmation(', 'private void ResetConfirmationBox()', 'private void OnConfirm()', 'private void OnSecondConfirm()', 'private void OnCancel()', 'public void Hide(')),
        ('UIMultiplayerSelectPlayerScreen', ('public void Show(', 'public void SetSelected(', 'private void OnSelectedRemovePlayer(')),
    ):
        native[name] = subprocess.run([str(__import__('pathlib').Path.home()/'.dotnet/tools/ilspycmd'), '-t', name, str(runtime)],
            check=True, capture_output=True, text=True, env=env).stdout
        selections += 'public partial class ' + name + '{\n'
        for signature in signatures: selections += method(native[name], signature) + '\n'
        selections += '}\n'
    for owner, signatures in (
        ('UIMenuOption', ('public virtual void Select()',)),
        ('UIOptionsWindow', ('public void Cancel()',)),
        ('UIAdventureCharacterConfirmationBox', ('public virtual void ShowConfirmationBox(', 'private void OnBackClick()', 'private void OnConfirmClick()', 'private void OnTransitionComplete(')),
        ('UIAdventurePartyAssemblyWindow', ('public CMapCharacter SelectedCharacter',)),
        ('UICampaignAdventurePartyAssemblyWindow', ('private void ConfirmDeleteCharacter()',)),
    ):
        selections += 'public partial class ' + owner + ' {\n'
        for signature in signatures: selections += method(native[owner], signature) + '\n'
        selections += '}\n'
    world = root / 'src/GloomhavenVR/WorldUI'
    sources = {
        'NativeWindowMethods.cs': native_window, 'NativeItemMethods.cs': native_item,
        'NativeEnhancement750.cs': native_enhancement,
        'NativeGeneric750.cs': generic, 'NativeSelections750.cs': selections,
        'ConfirmationOnOwner.Origins.cs': (world/'Composites/ConfirmationOnOwner.Origins.cs').read_text(),
        'ConfirmationOnOwner.Navigation.cs': (world/'Composites/ConfirmationOnOwner.Navigation.cs').read_text(),
        'ConfirmationOnOwner.Identity.cs': (world/'Composites/ConfirmationOnOwner.Identity.cs').read_text(),
        'ConfirmationOnOwner.NavigationState.cs': (world/'Composites/ConfirmationOnOwner.NavigationState.cs').read_text(),
        'ConfirmationOnOwner.Deferred.cs': (world/'Composites/ConfirmationOnOwner.Deferred.cs').read_text(),
        'ConfirmationBoxRescue.cs': (world/'Patches/ConfirmationBoxRescue.cs').read_text(),
    }
    reentry = world/'Composites/ConfirmationOnOwner.Reentrant.cs'
    if reentry.is_file(): sources[reentry.name] = reentry.read_text()
    scaffolds = (root/'scripts/flat-confirmation657-runtime/NativeScaffolds.cs').read_text()
    sources['NativeEnhancementBoundary750.cs'] = prefix.replace('#nullable disable', '#nullable enable') + method(scaffolds, 'public partial class UIEnhancementConfirmationBox')
    provenance = {name: hashlib.sha256(value.encode()).hexdigest() for name, value in native.items()}
    provenance['limits'] = 'Unchanged native callback/selection methods; declared surrounding session, artwork, inventory population and native action ports. Whole production origins/navigation/identity hooks execute. Presentation seating/rendering remains covered by the complete levelup-native suite. Not native campaign bootstrap or HMD acceptance.'
    return sources, provenance
