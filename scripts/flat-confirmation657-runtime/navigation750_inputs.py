"""Bind unchanged native callback/selection bodies for the navigation regression."""
import hashlib
import subprocess


def inputs(root, runtime, env, method, native_window, native_item):
    native = {}
    for name in ('ConfirmationBox', 'UIConfirmationBoxManager', 'NewPartyDisplayUI', 'UIPerksInventory', 'UIMenuOption', 'UIOptionsWindow',
                 'UIAdventureCharacterConfirmationBox', 'UIAdventurePartyAssemblyWindow', 'UICampaignAdventurePartyAssemblyWindow'):
        native[name] = subprocess.run([str(__import__('pathlib').Path.home()/'.dotnet/tools/ilspycmd'), '-t', name, str(runtime)],
            check=True, capture_output=True, text=True, env=env).stdout
    prefix = '#nullable disable\nusing System;using System.Collections.Generic;using System.Linq;using UnityEngine;using UnityEngine.UI;using UnityEngine.Events;using GLOOM;\n'
    generic = prefix + 'public partial class ConfirmationBox {\n'
    for signature in (
        'public void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, Color?',
        'private void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, UnityAction onActionCancelled, Color?',
        'public void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, UnityAction onActionCancelled, string',
        'public void ShowGenericCancelConfirmation(', 'public void ShowGenericSpendConfirmation(',
        'public void ResetConfirmationBox()', 'private void HideInternal()', 'public void Hide()',
        'public void OnHidden()', 'private void OnSubmit()', 'private void OnCancel()'):
        generic += method(native['ConfirmationBox'], signature) + '\n'
    generic += '}\npublic partial class UIConfirmationBoxManager {\n'
    for signature in ('public void ShowGenericConfirmation(', 'public void ShowGenericCancelConfirmation('):
        generic += method(native['UIConfirmationBoxManager'], signature) + '\n'
    generic += 'private void ShowGenericWarningConfirmation(){}private void ShowGenericSpendConfirmation(){}private void ShowCancelActiveAbility(){}\n}\n'
    selections = prefix + 'public partial class NewPartyDisplayUI {\n'
    for signature in ('private void SelectCurrentCharacter(', 'private void OnItemsSelected('):
        selections += method(native['NewPartyDisplayUI'], signature) + '\n'
    selections += '}\npublic partial class UIPerksInventory {\n'
    for signature in ('private void OnSelectedPerk(', 'public void Display('):
        selections += method(native['UIPerksInventory'], signature) + '\n'
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
        'NativeGeneric750.cs': generic, 'NativeSelections750.cs': selections,
        'ConfirmationOnOwner.Origins.cs': (world/'Composites/ConfirmationOnOwner.Origins.cs').read_text(),
        'ConfirmationOnOwner.Navigation.cs': (world/'Composites/ConfirmationOnOwner.Navigation.cs').read_text(),
        'ConfirmationOnOwner.Identity.cs': (world/'Composites/ConfirmationOnOwner.Identity.cs').read_text(),
    }
    provenance = {name: hashlib.sha256(value.encode()).hexdigest() for name, value in native.items()}
    provenance['limits'] = 'Unchanged native callback/selection methods; declared surrounding session, artwork, inventory population and native action ports. Whole production origins/navigation/identity hooks execute. Presentation seating/rendering remains covered by the complete levelup-native suite. Not native campaign bootstrap or HMD acceptance.'
    return sources, provenance
