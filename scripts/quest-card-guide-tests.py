#!/usr/bin/env python3
"""Qualify the real bounded card-guide evidence producer, without changing placement."""
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run
source=ROOT/'src/GloomhavenVR/Cards/QuestCardGuideEvidence.cs'
original=source.read_text()
update=(ROOT/'src/GloomhavenVR/Cards/Driver/CardsDriver.2.Update.cs').read_text()
assert update.index('UpdateWantedSlots(_fakeActive ? null : hand);') < update.index('QuestCardGuideEvidence.Observe(_fakeActive ? null : hand, _tray,') < update.index('UpdatePickStatus(_fakeActive ? null : hand);')
assert 'QuestStandalonePlatform.CardGuideDiagnostics = Reports.ToArray();' in original
base=ROOT/'.planning/debug/quest-card-guide';base.mkdir(parents=True,exist_ok=True)
run=Path(tempfile.mkdtemp(prefix='run-',dir=base));log=run/'runner.log'
log.write_text('Quest card guide evidence: '+str(run)+'\n');print(log.read_text(),end='',flush=True)
variants=[('production','','',''),
('disabled-scope-lost','if (!QuestStandalonePlatform.Enabled || !QuestStandalonePlatform.DebugLogging) return;','if (!QuestStandalonePlatform.DebugLogging) return;','disabled evidence reads or formats native state'),
('change-dedup-lost','if (_havePrior && current.Equals(_prior)) return;','if (_havePrior && current.Equals(_prior) && fields < 0) return;','unchanged selection repeatedly formats renderer facts'),
('observation-cap-lost','Reports.Count - i > 8','Reports.Count - i > int.MaxValue','guide history exceeds eight observations'),
('utf8-entry-bound-lost','Encoding.UTF8.GetByteCount(facts) > 1024','Encoding.UTF8.GetByteCount(facts) > 4096','native Unicode text bypasses guide byte bounds')]
dotnet=shutil.which('dotnet') or '/home/claw/.dotnet/dotnet'
code=1;process=None
ctypes.CDLL(None,use_errno=True).prctl(36,1,0,0,0)
try:
 results=[]
 for name,before,after,expected in variants:
  case=run/name;case.mkdir()
  for file in ['Program.cs','Stubs.cs','QuestCardGuide.Tests.csproj']:shutil.copyfile(ROOT/'tests/QuestCardGuide.Tests'/file,case/file)
  content=original
  if before:assert content.count(before)==1;content=content.replace(before,after,1)
  evidence=case/'Production.cs';evidence.write_text(content)
  project=case/'QuestCardGuide.Tests.csproj'
  build=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','--disable-build-servers','-p:UseSharedCompilation=false','-p:EvidenceSource='+str(evidence)],capture_output=True,text=True)
  (case/'build.log').write_text(build.stdout+build.stderr)
  if build.returncode:raise RuntimeError('Compilation cannot pass a causal control: '+str(case/'build.log'))
  process=subprocess.Popen([dotnet,str(case/'bin/Release/net8.0/QuestCardGuide.Tests.dll')],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,start_new_session=True)
  output,_=process.communicate(timeout=60);(case/'output.log').write_text(output)
  passed=process.returncode==0 if not expected else process.returncode!=0 and expected in output
  if not passed:raise RuntimeError(name+' failed qualification: '+output)
  results.append({'name':name,'passed':True,'expectedDefect':expected,'output':output.strip()})
 (run/'results.json').write_text(json.dumps(results,indent=2)+'\n')
 (run/'source-hashes.json').write_text(json.dumps({'producer':hashlib.sha256(source.read_bytes()).hexdigest(),'actualCallSite':hashlib.sha256(update.encode()).hexdigest(),'fixture':hashlib.sha256((ROOT/'tests/QuestCardGuide.Tests/Program.cs').read_bytes()).hexdigest(),'hardwareVerified':False},indent=2)+'\n')
 print('PASS Quest card guide evidence: '+results[0]['output']+'; 4 causal controls',flush=True);code=0
finally:
 if process is not None and process.poll() is None:process.kill();process.wait()
 spec=importlib.util.spec_from_file_location('card_guide_cleanup',ROOT/'scripts/quest-artwork-tests.py');helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
 (run/'process-cleanup.json').write_text(json.dumps(helper.reap_fixture_group(process.pid if process else None),indent=2)+'\n')
 (run/'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-card-guide',code,log,ROOT,process_reaped=True),indent=2)+'\n')
