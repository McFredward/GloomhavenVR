import pathlib,sys
root=pathlib.Path(sys.argv[1]);out=pathlib.Path(sys.argv[2]);p=root/'src/GloomhavenVR/Cards/Piles/ItemsPile.cs';s=p.read_text()
def method(name):
    start=s.index(name);start=s.rfind('\n',0,start)+1;br=s.index('{',start);depth=1;i=br+1
    while depth:depth+=(s[i]=='{')-(s[i]=='}');i+=1
    return s[start:i]
host=method('private bool TryHostRealCard(')
assert host.index('cardUI.item = item;') < host.index('ItemBurnPlayback.ObserveInitialState(cardUI);') < host.index('cardUI.Show(highlightElement: false);'), 'Historical consumed hosts must be classified before the first native state paint'
module=(root/'src/GloomhavenVR/Cards/CardsModule.cs').read_text()
assert 'PatchAll(typeof(ItemBurnPlayback.BurnCardTimeline_Track))' in module,'Native item burn tracker must be registered'
for name in ['private void Open(', 'internal void Close(', 'private void Populate(', 'private void Relayout(', 'internal void Tick(CardsHandUI? hand)']:
    assert 'UseAnimationPending' in method(name),'Item layout must retain the burning source'
for name in ['private void FinishUsedChip(', 'private void ConfirmDemandPick(']:
    m=method(name);assert 'BeginUsedChipPresentation(chip, consumed, spent, converge);' in m and '_chips.Remove(chip)' not in m,'Use and surrender must retain original chip registration'
result='using UnityEngine;\nnamespace GloomhavenVR.Cards;\ninternal sealed partial class ItemsPile {\n'
result+=method('private void BeginUsedChipPresentation(')+'\n'+method('private void CompleteUsedChipPresentation(')+'\n'
start=s.index('    internal int ClippedChipIndex');br=s.index('{',start);depth=1;i=br+1
while depth:depth+=(s[i]=='{')-(s[i]=='}');i+=1
result+=s[start:i]+'\ninternal sealed partial class ItemChip {\n'
result+=method('internal void PlayUseThenCollapse(')+'\n'+method('private void TickUseFx(')+'\n}}\n'
out.write_text(result)
