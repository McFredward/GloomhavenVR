import pathlib,sys
root=pathlib.Path(sys.argv[1]);out=pathlib.Path(sys.argv[2])
cards=root/'src/GloomhavenVR/Cards'
source=(cards/'Driver/CardsDriver.4.Rebuild.cs').read_text()
artwork=(cards/'BurnArtwork.cs').read_text()
assert 'BurnTimelines.Add(__instance, playback);' in artwork and 'return !playback.Finished;' in artwork, 'Layout completion must observe the actual native burn iterator, including synchronous bails'

def method(text,name):
    start=text.index(name);start=text.rfind('\n',0,start)+1
    brace=text.index('{',start);depth=1;i=brace+1
    # These extracted methods have no brace characters in strings/comments.
    while depth:
        depth += (text[i]=='{')-(text[i]=='}');i+=1
    return text[start:i]
rebuild=source[source.index('    private void Rebuild(Transform anchor)'):]
assert rebuild.index('if (DeferLayoutForBurn()) return;') < rebuild.index('Board.CharacterFocus.ResolveHand'), 'Burn admission must precede actor resolution'
update=(cards/'Driver/CardsDriver.2.Update.cs').read_text()
assert update.index('RefreshBurnLayoutBarrier();') < update.index('DrainSwapExit();') < update.index('if (_boardChanged && !_burnLayoutPending)') < update.index('Rebuild(anchor);'), 'Burn discovery must precede board replacement'
assert '_faceRestoreDeadline += Time.unscaledDeltaTime;' in method(source,'private void DrainSwapExit()'), 'Paused exchange must retain original faces'
assert 'if (modalBlock || _burnLayoutPending)' in update, 'Burn wait must disarm retained card grabs'
field=(cards/'Driver/CardsDriver.5.Interactions.cs').read_text()
assert 'if (DeferLayoutForBurn()) return;' in method(field,'private void RelayoutField()'), 'Pick field must await burn'
fan=(cards/'CardFan.cs').read_text()
for signature in ['internal void Open(VRHand hand)', 'internal void Close()', 'private void Relayout(bool instant)']:
    assert 'if (CardsDriver.BurnLayoutPending) return;' in method(fan,signature), 'Fan mutation must await burn'
for text in ['if (_arriving.Count > 0 && !CardsDriver.BurnLayoutPending)', 'if (_closeElapsed >= 0f && !CardsDriver.BurnLayoutPending)', 'if (swapping && !CardsDriver.BurnLayoutPending)', 'if (_openElapsed >= 0f && !CardsDriver.BurnLayoutPending)']:
    assert text in fan, 'Fan animation must await burn'
active=(cards/'Piles/ActivePileViewer.cs').read_text()
assert 'if (CardsDriver.BurnLayoutPending) return;' in method(active,'private void Relayout(bool instant)'), 'Active grid must await burn'
result=(cards/'Driver/CardsDriver.8.BurnSequencing.cs').read_text()
result+='\ninternal sealed partial class CardsDriver\n{\n'
result+=method(source,'private bool IsFreshBurn(')+'\n'
result+=method(source,'private static RoundCardExit RoundCardExitOf(')+'\n'
start=source.index('    private static PileKind PileFateOf(');end=source.index('\n        };',start)+len('\n        };')
result+=source[start:end]+'\n'
# Execute the actual rebuild admission, replacing only the large downstream renderer with a spy.
result+='internal void RebuildProbe() {\n'+rebuild[rebuild.index('{')+1:rebuild.index('        NoteExhaustedRuleArmed();')]+' RenderRequestedLayout(); }\n}\n'
out.write_text(result.replace('using UnityEngine;','using UnityEngine;\nusing ScenarioRuleLibrary;'))
