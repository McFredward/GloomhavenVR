#!/usr/bin/env python3
"""Build652 bounded whole-World CPU comparisons, reusing the actual-source CPU lane.

Baseline651 and candidate use the same ~440 source topology and native Unity APIs.
The hidden workload explicitly changes visible geometry, which is the authorized
quality compromise. Regular/Auto-false retain identical input presentation.
"""
import argparse,hashlib,json,shutil,sys,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser(description=__doc__,add_help=False)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/frame652-proof/world-cpu')
    known,_=parser.parse_known_args();known.output_dir.mkdir(parents=True,exist_ok=True)
    transfer=Path(tempfile.mkdtemp(prefix='harness-',dir=known.output_dir.resolve()))
    (transfer/'scripts').mkdir();(transfer/'tests/world-material-cpu/Editor').mkdir(parents=True);(transfer/'tests/world-material-runtime').mkdir()
    script=ROOT/'scripts/check-world-material-cpu.py';runtime=ROOT/'scripts/check-world-material-runtime.py'
    shutil.copyfile(runtime,transfer/'scripts/check-world-material-runtime.py')
    for name in ['Boundaries.cs','Native.shader','Bridge.shader','World.csproj']:
        shutil.copyfile(ROOT/'tests/world-material-runtime'/name,transfer/'tests/world-material-runtime'/name)
    for name in ['Program.cs','Editor/WorldCpuRunner.cs']:
        shutil.copyfile(ROOT/'tests/world-material-cpu'/name,transfer/'tests/world-material-cpu'/name)
    program=transfer/'tests/world-material-cpu/Program.cs';text=program.read_text()
    anchor='        WorldMaterialBudget.Install(state.Host);'
    assert text.count(anchor)==1
    addition='''        // Optional native production API looked up once outside the timed region. Old651
        // lacks the wall-quality setting; its same sources therefore remain visible. The
        // candidate observes exact managed renderer identity and sets actual native flags.
        var wallApi=typeof(WorldMaterialBudget).GetMethod("ConfigurePerformanceWallVisibility",BindingFlags.Static|BindingFlags.NonPublic);
        if(wallApi!=null&&!workload.Contains("policy-null"))
        {
            var hidden=new HashSet<Renderer>();
            if(workload.Contains("hidden-half"))for(int i=64;i<284;i++){hidden.Add(state.Sources[i]);state.Sources[i].forceRenderingOff=true;}
            if(workload.Contains("hidden-all"))for(int i=64;i<440;i++){hidden.Add(state.Sources[i]);state.Sources[i].forceRenderingOff=true;}
            Func<Renderer,bool> predicate=workload.Contains("hidden")?hidden.Contains:r=>false;
            wallApi.Invoke(null,new object[]{predicate});
        }
'''
    text=text.replace(anchor,addition+anchor);program.write_text(text)
    # The original reproducible comparator remains unchanged. Its ROOT points to a
    # private fixture transfer beneath this Git worktree; production roots stay read-only.
    namespace={'__name__':'wall_cpu_delegate','__file__':str(script)}
    delegated=script.read_text().replace('fixture = ROOT / "tests/world-material-cpu"','fixture = Path('+repr(str(transfer/'tests/world-material-cpu'))+')').replace('runtime = ROOT / "tests/world-material-runtime"','runtime = Path('+repr(str(transfer/'tests/world-material-runtime'))+')')
    exec(compile(delegated,str(script),'exec'),namespace)
    namespace['ROOT']=ROOT
    namespace['WORKLOADS']=['representative-mpb-policy-null','representative-mpb-policy-false','representative-mpb-hidden-half','representative-mpb-hidden-all']
    provenance={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__).resolve(),script,runtime,ROOT/'tests/world-material-cpu/Program.cs',ROOT/'tests/world-material-cpu/Editor/WorldCpuRunner.cs']}
    (transfer/'transfer-proof.json').write_text(json.dumps({'sourceInputs':provenance,'transformation':'Add optional exact managed renderer membership configuration before Install, outside timed region; hidden-half220/hidden-all376 current source native force-off flags. Old651 remains visible because it lacks the explicit setting.','generatedProgramSha256':hashlib.sha256(program.read_bytes()).hexdigest(),'limits':['Existing CPU controllers/PerfMonitor/bridge shader boundaries retained.','This isolates actual World path; original wall collector, shader GPU and native game scene are not measured.','Hidden geometry input deliberately differs; savings are not a pure detail-preserving optimization.','No valid Mono allocation bytes or headset FPS inference.']},indent=2)+'\n')
    if '--baseline-ref' not in sys.argv:sys.argv+=['--baseline-ref','2f6819eee']
    if '--output-dir' not in sys.argv:sys.argv+=['--output-dir',str(known.output_dir)]
    namespace['main']()
    print('TRANSFER '+str(transfer))
if __name__=='__main__':main()
