#!/usr/bin/env python3
"""Replay the exact ESC family/sweep/closer; native owner Hide is an explicit port."""
import hashlib,json,os,shutil,subprocess,tempfile
from pathlib import Path
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
def body(s,k):
 a=s.index(k);p=s.index('{',a)+1;d=1
 while d:d+=(s[p]=='{')-(s[p]=='}');p+=1
 return s[a:p]
def expression(s,k):
 a=s.index(k);return s[a:s.index(';',a)+1]
def main():
 out=ROOT/'.planning/debug/esc-roster-close';out.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=out));status=1
 log=run/'completion.log';log.write_text('Evidence: '+str(run)+'\n');print('Evidence: '+str(run),flush=True)
 modal=ROOT/'src/GloomhavenVR/WorldUI/Modal';family=(modal/'MenuWindowFamily.cs').read_text();close=(modal/'ModalFallback.7.Close.cs').read_text()
 f=ROOT/'scripts/esc-roster-close-runtime';native=ROOT/'decompiled/GH.Runtime'
 assert 'selectPlayerPopup.Hide();' in body((native/'UIMultiplayerEscSubmenu.cs').read_text(),'private void OnHidden()')
 hide=body((native/'UIMultiplayerSelectPlayerScreen.cs').read_text(),'public void Hide()')
 assert hide.index('window.Hide();')<hide.index('currentSelection = null;')
 src='using System;using System.Collections.Generic;using GloomhavenVR.Core;using UnityEngine.UI;namespace GloomhavenVR.WorldUI;internal static partial class MenuWindowFamily{\n'
 for name in ['PlayerMenuIds','EscMenuSubWindowIds']:src+=body(family,'private static readonly HashSet<UIWindowID> '+name)+';\n'
 for name in ['IsGameOwnedMenu','IsEscMenuSubWindow','IsOptionsKeyDomain']:src+=expression(family,'internal static bool '+name)+'\n'
 src+='}\ninternal static partial class ModalFallback{\n'
 for sig in ['internal static void CloseFloatedWindow(','internal static void CloseStickyFloatsExceptEscMenu(','private static WindowPanel? FindPanel(']:src+=body(close,sig)+'\n'
 src+='}\n'
 (run/'source-pins.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [modal/'MenuWindowFamily.cs',modal/'ModalFallback.7.Close.cs',native/'UIMultiplayerEscSubmenu.cs',native/'UIMultiplayerSelectPlayerScreen.cs',Path(__file__),*f.glob('*.cs')]},indent=2))
 try:
  for name,text,expected in [('production',src,None),('old-family',src.replace('        UIWindowID.MutiplayerPlayerPicker,',''), 'controller close releases the already native-hidden chooser float')]:
   case=run/name;case.mkdir();(case/'Production.cs').write_text(text)
   for p in f.glob('*.cs'):shutil.copy2(p,case/p.name)
   (case/'Test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649</NoWarn></PropertyGroup></Project>')
   env=dict(os.environ,DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1',MSBUILDDISABLENODEREUSE='1')
   r=subprocess.run([str(Path.home()/'.dotnet/dotnet'),'run','--project',str(case/'Test.csproj'),'-c','Release','-p:UseSharedCompilation=false'],capture_output=True,text=True,env=env);output=r.stdout+r.stderr;(case/'result.log').write_text(output)
   if expected is None:assert r.returncode==0,output;print(output,end='')
   else:assert r.returncode!=0 and expected in output,output;print('PASS negative control old-family')
  status=0
 finally:
  (run/'result.json').write_text(json.dumps({'exit_code':status,'limits':__doc__},indent=2));receipt=cleanup_completed_run('esc-roster-close',status,log,ROOT,process_reaped=True);(run/'cleanup.json').write_text(json.dumps(receipt,indent=2))
if __name__=='__main__':main()
