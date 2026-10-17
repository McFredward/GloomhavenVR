#!/usr/bin/env python3
"""Build, derive and rasterize a tiny OWNED compiled shader-bank pair only."""
import argparse
import hashlib
import importlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
sys.dont_write_bytecode = True
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--unitypy',type=Path,default=Path('/home/claw/unitypy-venv/bin/python'))
    args=parser.parse_args()
    sys.path.insert(0,str(ROOT/'scripts'))
    cleanup=importlib.import_module('town_run_retention').cleanup_completed_run
    args.output.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()));(run/'.keep').touch()
    project=run/'unity';assets=project/'Assets';(assets/'Editor').mkdir(parents=True)
    shutil.copyfile(HERE/'Fixture.cs',assets/'Editor/Fixture.cs');shutil.copyfile(HERE/'Source.shader',assets/'Source.shader')
    (project/'Packages').mkdir();(project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.modules.assetbundle':'1.0.0','com.unity.modules.jsonserialize':'1.0.0'}},indent=2)+'\n')
    (project/'ProjectSettings').mkdir();(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    tool=ROOT/'tools/scene-coverage-shader-bank/derive.py'
    inputs={str(p.relative_to(ROOT)):sha(p) for p in [HERE/'Fixture.cs',HERE/'Source.shader',HERE/'controls.py',HERE/'run.py',tool]}
    (run/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
    receipts=[];exit_code=1
    def clean(label,code,log):
        receipt=cleanup(label,code,log,ROOT,process_reaped=True);removed=[]
        if receipt['run'] is None:
            for name in ['Library','Temp','Obj','Logs']:
                path=project/name
                if path.is_dir() and not path.is_symlink():shutil.rmtree(path);removed.append(str(path))
        receipts.append({'suite':label,'exit_code':code,'retention_api':receipt,'exact_owned_caches_removed':removed})
        (run/'cleanup.json').write_text(json.dumps(receipts,indent=2)+'\n')
    def execute(label,command,unity=False):
        log=run/(label+'.launcher.log');code=1
        environment=os.environ.copy()
        if unity:environment['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
        try:
            with log.open('w') as output:
                process=subprocess.Popen(command,stdout=output,stderr=subprocess.STDOUT,start_new_session=True,env=environment)
                try:code=process.wait(timeout=180)
                except BaseException:
                    if process.poll() is None:os.killpg(process.pid,signal.SIGKILL)
                    process.wait();raise
            if code:raise RuntimeError(label+' failed; see '+str(log))
        finally:clean('shader-bank-'+label,code,log)
    base=['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project)]
    try:
        original=run/'original/owned-original.bundle';derived=run/'owned-coverage.bundle'
        execute('build',base+['-executeMethod','CoverageBankFixture.Build','-bankOutput',str(original.parent),'-logFile',str(run/'build.unity.log')],True)
        original_pin=sha(original)
        execute('derive',[str(args.unitypy),str(tool),str(original),str(derived),'--source-sha256',original_pin,'--report',str(run/'derivation.json')])
        execute('controls',[str(args.unitypy),str(HERE/'controls.py'),str(original),str(derived),str(run/'controls.json')])
        execute('probe',base+['-executeMethod','CoverageBankFixture.Probe','-originalBank',str(original),'-derivativeBank',str(derived),'-bankResult',str(run/'pixels.json'),'-logFile',str(run/'probe.unity.log')],True)
        pixels=json.loads((run/'pixels.json').read_text());controls=json.loads((run/'controls.json').read_text())
        assert pixels['passed'] and pixels['original_and_derivative_simultaneously_loaded'] and pixels['backend']=='Vulkan'
        assert controls['passed'] and controls['controls']>=14
        assert sha(original)==original_pin and all(sha(ROOT/p)==digest for p,digest in inputs.items()),'Frozen source/original changed'
        exit_code=0
    finally:
        (run/'summary.json').write_text(json.dumps({'exit_code':exit_code,'scope':'OWNED_COMPILED_SHADER_BANK_ONLY','native_game_shader':False,'windows_d3d11':False,'xr':False,'runtime_adoption':False,'frozen_inputs':inputs},indent=2)+'\n')
        # Small owned bundle/metadata/source receipts are retained; project caches never are.
        clean('shader-bank-final',exit_code,run/'summary.json')
        print('evidence: '+str(run))
    return exit_code


if __name__=='__main__':
    raise SystemExit(main())
