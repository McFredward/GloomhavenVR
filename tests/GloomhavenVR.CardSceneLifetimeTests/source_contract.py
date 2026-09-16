"""Bind iterator coverage to actual native-load registration and borrowed face release."""
from pathlib import Path
import re
import sys
root = Path(sys.argv[1])
checks = 0
def source(path):
    return re.sub(r'/\*[\s\S]*?\*/|//[^\n]*', '', (root/path).read_text())
def require(ok, message):
    global checks
    checks += 1
    assert ok, message
base = 'src/GloomhavenVR/Cards/'
patch = source(base+'Patches/CardLifecyclePatches.cs')
require('[HarmonyPatch(typeof(SceneController), "LoadSceneCoroutine")]' in patch, 'Native scene load method must be patched')
require('ref System.Collections.IEnumerator __result' in patch and '__result = new NativeCardSceneLifetime(__result,' in patch, 'Original iterator must be wrapped without eager execution')
require('() => __instance != null && !__instance.DataRestoring' in patch, 'DataRestoring no-op must be checked at execution time')
require('CardsDriver.ReleaseCardsBeforeSceneLoad, "Cards"' in patch, 'Load entry must release through the driver')
require('PatchAll(typeof(SceneController_LoadScene_CardLifetime))' in source(base+'CardsModule.cs'), 'Scene lifetime patch must be registered')
driver = source(base+'Driver/CardsDriver.2.Update.cs')
release = driver.split('internal static void ReleaseCardsBeforeSceneLoad()',1)[1].split('private void UpdateBody()',1)[0]
require('driver._factory.ReturnBorrowedFacesBeforeSceneLoad();' in release, 'Scene entry must return every borrowed factory face')
require('driver._dirty = true;' in release, 'Aborted native load must permit presentation rebuild')
body = driver.split('private void UpdateBody()',1)[1].split('CardActionQueue.Pump();',1)[0]
require('if (NativeSceneLoadInProgress) return;' in body and 'scene._loadingSceneType != SceneController.ESceneType.None' in driver, 'Native load state must prevent re-adoption')
require('.IsLoading' not in body, 'Generic loading screen must not block pending native damage decisions')
factory = source(base+'VRCardFactory.cs').split('internal void Clear()',1)[1].split('//',1)[0]
require(factory.index('card.DetachGameCard();') < factory.index('Object.Destroy(card.gameObject);'), 'Borrowed native face must detach before VR host destruction')
require('face.SetParent(_origParent, worldPositionStays: false);' in source(base+'Art/CardFace.cs'), 'Native face must return to its captured parent')

factory = source(base+'VRCardFactory.cs')
require('if (!existing.HasAdoptedFace && !CardsDriver.NativeSceneLoadInProgress)' in factory and 'existing.AttachGameCard(widget);' in factory, 'Abort rebuild must reattach existing wrapper')
card = source(base+'VRCard.cs').split('internal void ReturnBorrowedFaceForSceneLoad()',1)[1].split('internal void DetachGameCard()',1)[0]
require(card.index('AbilityCardUI? widget = GameCard;') < card.index('DetachGameCard();') < card.index('GameCard = widget;'), 'Scene release must preserve selected wrapper identity')

require(source(base+'VRCard.cs').count('if (CardsDriver.NativeSceneLoadInProgress) return;') == 2, 'VR card updates must not reapply native FX during scene loading')
require('if (CardsDriver.NativeSceneLoadInProgress) return false;' in source(base+'VRCard.cs').split('internal bool AttachGameCard(',1)[1].split('internal bool HasAdoptedFace',1)[0], 'Direct card attachment must not bypass native load admission')
require('_sceneFacesReturned = !_factory.ReattachBorrowedFacesAfterSceneLoad();' in body, 'Every retained face must recover before layout resumes')
require('if (ReferenceEquals(previousWidget, card)) GameCard = card;' in source(base+'VRCard.cs'), 'Failed re-adoption must preserve existing native identity for retry')
print(f'Card scene lifetime source bindings: {checks} assertions passed.')
