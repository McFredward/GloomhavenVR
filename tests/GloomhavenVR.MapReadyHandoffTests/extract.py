"""Compile the actual parking coordinator and transfer kernel against bounded uGUI."""
from pathlib import Path
import sys

def method(source, signature):
    assert source.count(signature)==1,signature
    start=source.index(signature);opening=source.index('{',start);depth=1;end=opening+1
    # The selected production bodies contain braces in interpolations balanced in pairs.
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]

root,out=map(Path,sys.argv[1:]);source=(root/'MapTravelConfirm.cs').read_text()
methods=['internal static void Reconcile(UIWindow? questWindow)','private static void ReconcileCore(UIWindow? questWindow)',
 'internal static void Reset()','private static UIReadyToggle? QuestConfirmToggle()',
 'private static GameObject? ResolveContainer(AdventureMapUIManager? mgr, out bool readyToggle)',
 'private static bool ReadyToggleVisible()','private static bool Park(UIWindow questWindow, GameObject options, bool readyToggle)',
 'private static void Unpark(string why)']
text='using System;using UnityEngine;using UnityEngine.UI;using Object=UnityEngine.Object;using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI.MapRoom { internal static partial class MapTravelConfirm {\n'+'\n'.join(method(source,name) for name in methods)+'\n}}\n'
transfer=(root/'ModalFallback.CompositeTransfer.cs').read_text()
text+='namespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n'+method(transfer,'internal static bool ReleaseForComposite(UIWindow window)')+'\n'
conv=(root/'ModalFallback.8.Convert.cs').read_text();start=conv.index('Sticky = (');end=conv.index(',\n                Transient =',start)
text+='internal static bool StickyFor(UIWindow window) { bool isTransient=false; return '+conv[start+len('Sticky = '):end]+'; }\n'
text+='private static bool IsIntroductionWindow(UIWindow window)=>false;\n'
text+=method((root/'ModalFallback.10.CatchAll.cs').read_text(),'private static bool DropOwnFloat(UIWindow window)')+'\n'
main=(root/'ModalFallback.4.Tick.cs').read_text();start=main.index('bool stillOpen = alive');end=main.index(';',start)
text+='internal static bool Keep(WindowPanel wp) { bool alive=wp.Window!=null&&wp.Panel.IsAlive;bool refused=false,answeredMandatory=false,selectionGone=false;\n'+main[start:end+1]+'return stillOpen;}\n}}\n'
out.write_text(text)
