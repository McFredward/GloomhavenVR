"""Independent native-clock SBS movie delivery from owned media and numeric-only depth.

The public recipe contains exact half-depth fields, never original movie color or
sound. It reuses the approved offline reprojection without running a depth model.
Each film and the tiny Android shader bank has its own immutable completion key.
"""
from __future__ import annotations
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import threading
import zipfile

from storage import BuildError, ImmutableFileHashes, _ordinary_owned, canonical, digest, record_file, value_hash, write_json

METADATA = 'assets/Quest/movies.json'
PREFIX = 'assets/QuestMovies/'
BANK = 'ghvr-quest-movies.bundle'
RECIPE = 'tools/quest-movies/recipe.json'
CP = 'src/GloomhavenVR/Assets/Movies/CP_Intro/'
PYPI_MIRRORS = ('https://pypi.org/simple', 'https://pypi.python.org/simple', 'https://mirrors.aliyun.com/pypi/simple')
SOURCE_HELPERS = {'tools/quest-builder/movie_banks.py', 'tools/quest-builder/movie_render.py',
                  'tools/quest-builder/requirements-movies.txt'}
_CHILDREN = set()
_CHILDREN_LOCK = threading.Lock()
_CANCEL_CHILDREN = threading.Event()


def source_rows(files):
    return [row for row in files if row.get('path', '').startswith('tools/quest-movies/')
        or row.get('path') in SOURCE_HELPERS
        or row.get('path') in {CP+'GH_CP_Intro.mov', CP+'GH_CP_Intro.sbs-v2.mov'}]


def _record(path, relative):
    path = _ordinary_owned(Path(path)); st = path.stat()
    return {'path': relative, 'size': st.st_size, 'sha256': digest(path)}


def _stamp(path):
    st = _ordinary_owned(path).stat()
    return [st.st_dev, st.st_ino, st.st_size, st.st_mtime_ns, st.st_ctime_ns]


def _recipe(source):
    path = _ordinary_owned(Path(source)/RECIPE)
    if path.stat().st_size > 2*1024*1024: raise BuildError('Stereo movie recipe exceeds its finite bound.')
    recipe = json.loads(path.read_text())
    if recipe.get('schema') != 1 or recipe.get('packing') != 'full-SBS-left-first' or len(recipe.get('movies',[])) != 19:
        raise BuildError('Stereo movie recipe lacks its exact19 original subjects.')
    keys = set()
    for movie in recipe['movies']:
        key = movie.get('key','')
        if key in keys or not key.startswith(('Ambient/','Heroes/','Embedded/')) or not key.endswith('.mov') or '..' in key or '\\' in key:
            raise BuildError('Stereo movie recipe has an unsafe or duplicate native identity.')
        keys.add(key)
        if movie.get('dimensions') != [1920,1080] or movie.get('fps') not in (30,60) or not 1<=movie.get('frames',0)<=3000:
            raise BuildError('Stereo movie native clock or dimensions changed.')
        cursor=0
        for shot in movie['shots']:
            if shot['start'] != cursor or not cursor < shot['end'] <= movie['frames'] or shot['mode'] not in ('depth','logo','screen'):
                raise BuildError('Stereo movie shots do not partition the original frames.')
            cursor=shot['end']
            if shot['mode']=='depth':
                for part in shot['depth']['chunks']:
                    if not part['path'].startswith('depth/') or '/' in part['path'][6:] or not part['path'].endswith('.zst'):
                        raise BuildError('Stereo depth chunk escaped its numeric recipe directory.')
        if cursor != movie['frames']: raise BuildError('Stereo movie recipe lost its native ending.')
    if any('gameplay_trailer' in key for key in keys): raise BuildError('The excluded gameplay trailer is not a stereo conversion subject.')
    return recipe


def plan(source, game_info):
    source = Path(source); recipe = _recipe(source)
    originals = {row['path']: row for row in game_info.get('files',[])}
    selected = []
    for movie in recipe['movies']:
        name = movie['source_relative'] if movie['kind']=='external' else movie['resource']
        row = originals.get(name)
        if row is None: raise BuildError('Owned original stereo movie source is missing: '+name)
        if movie['kind']=='external' and (row['sha256'] != movie['source_sha256'] or row['size'] != movie['source_bytes']):
            raise BuildError('Owned original movie differs from the qualified stereo recipe: '+name)
        if row not in selected: selected.append(row)
    compiler = sorted(SOURCE_HELPERS | {RECIPE, 'tools/quest-movies/StereoVideoUI.shader',
        'tools/quest-movies/QuestMovieEye.shader','tools/quest-movies/MovieBankBuild.cs',
        CP+'GH_CP_Intro.mov',CP+'GH_CP_Intro.sbs-v2.mov'})
    pin = {'schema':1,'target':'Android','unityVersion':'2021.3.5f1',
        'sources':[_record(source/name,name) for name in compiler]}
    return {'producerKey':value_hash({'recipe':pin,'sources':selected}), 'recipe':pin,
        'sources':selected,'shaderKey':_shader_key(source)}


def _retained(folder, key):
    try:
        receipt=json.loads((folder/'complete.json').read_text())
        if receipt['key'] != key:return None
        row=receipt['file'];path=folder/row['path']
        if _stamp(path)!=receipt['stamp']:
            if _record(path,row['path']) != row:return None
            receipt['stamp']=_stamp(path);write_json(folder/'complete.json',receipt)
        return row
    except (OSError,ValueError,KeyError,TypeError):return None


def _publish(folder, key, path):
    row=_record(path,path.name)
    write_json(folder/'complete.json',{'key':key,'file':row,'stamp':_stamp(path)})
    return row


def _python(builder, source, output, operation):
    requirements=source/'tools/quest-builder/requirements-movies.txt'
    scope=value_hash({'requirements':digest(requirements),'python':list(sys.version_info[:3]),
        'base':str(Path(getattr(sys,'_base_executable',sys.executable)).resolve()),'os':sys.platform})
    root=output/'tool-cache/movie-python'/scope
    marker=root/'owner.json';python=root/('Scripts/python.exe' if os.name=='nt' else 'bin/python')
    expected={'schema':1,'owner':'GloomhavenVR movie converter','key':scope}
    if root.exists():
        _ordinary_owned(root)
        if not marker.is_file() or json.loads(marker.read_text()).get('owner')!=expected['owner']:
            raise BuildError('Movie converter Python folder has no matching ownership receipt.')
        if python.is_file() and json.loads(marker.read_text())=={**expected,'complete':True}:return python
    else:
        root.mkdir(parents=True);write_json(marker,{**expected,'complete':False})
    builder.build_progress.event('stereo-movies-tools',detail='Prepare the isolated local movie converter; no depth model is installed.',operation=operation)
    log=output/'logs/movie-python.log'
    builder.command([sys.executable,'-m','venv',str(root)],log)
    env=dict(os.environ);env.update(PIP_CONFIG_FILE=os.devnull,PIP_EXTRA_INDEX_URL='',PIP_DISABLE_PIP_VERSION_CHECK='1')
    failures=[]
    for mirror in PYPI_MIRRORS:
        try:
            builder.command([str(python),'-m','pip','install','--index-url',mirror,'--require-hashes','--only-binary=:all:',
                '--no-deps','-r',str(requirements)],log,env=env)
            break
        except (BuildError,subprocess.CalledProcessError,OSError) as error:
            failures.append(type(error).__name__)
            builder.build_progress.event('stereo-movies-tools',detail='Retry pinned movie packages through another HTTPS package index.',operation=operation)
    else:raise BuildError('Pinned movie packages could not be downloaded from the available mirrors. Completed films are retained; reconnect and continue. See '+str(log))
    builder.command([str(python),'-I','-c','import numpy,cv2,numba,llvmlite,imageio_ffmpeg,zstandard'],log)
    write_json(marker,{**expected,'complete':True})
    return python


def _shader_key(source):
    names=['StereoVideoUI.shader','QuestMovieEye.shader','MovieBankBuild.cs']
    return value_hash({'target':'Android','unity':'2021.3.5f1','apis':['Vulkan','OpenGLES3'],
        'files':[_record(Path(source)/'tools/quest-movies'/name,name) for name in names]})


def shader_cached(output,shader_key):
    return _retained(Path(output)/'cache/quest-movies/shaders'/shader_key,shader_key) is not None


def _shader_bank(builder,args,source,output,*,previous=None,base_apk=None):
    names=['StereoVideoUI.shader','QuestMovieEye.shader','MovieBankBuild.cs']
    key=_shader_key(source)
    root=output/'cache/quest-movies/shaders'/key
    old=_retained(root,key)
    if old:return root/old['path']
    prior=next((row for row in (previous or {}).get('files',[]) if row.get('path')==BANK),None)
    if base_apk is not None and (previous or {}).get('shaderKey')==key and prior is not None:
        with zipfile.ZipFile(base_apk) as archive:
            item=archive.getinfo(PREFIX+BANK)
            if not 0<item.file_size<=8*1024*1024 or item.file_size!=prior['size']:
                raise BuildError('The retained APK movie shader bank has an invalid extent.')
            raw=archive.read(item)
        if hashlib.sha256(raw).hexdigest()!=prior['sha256'] or raw[:8]!=b'UnityFS\0':
            raise BuildError('The retained APK movie shader bank differs from its signed manifest.')
        root.mkdir(parents=True,exist_ok=True);target=root/BANK
        temporary=root/(BANK+'.retaining');temporary.write_bytes(raw);os.replace(temporary,target);_publish(root,key,target)
        return target
    project=root/'project';builder.project_access.wait_for_editor(project,builder.build_progress)
    (project/'Assets/Editor').mkdir(parents=True,exist_ok=True)
    (project/'ProjectSettings').mkdir(exist_ok=True)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    write_json(project/'Packages/manifest.json',{'dependencies':{'com.unity.modules.assetbundle':'1.0.0'}})
    for name in names:
        destination=project/('Assets/Editor' if name.endswith('.cs') else 'Assets')/name
        shutil.copy2(source/'tools/quest-movies'/name,destination)
    editor=Path(builder.toolchain(args,output)['editor'])
    log=output/'logs/stereo-movies-unity.log'
    builder.command(builder.unity_launcher(editor)+['-quit','-projectPath',str(project),'-buildTarget','Android',
        '-executeMethod','MovieBankBuild.Build','-logFile',str(log)],output/'logs/stereo-movies-launch.log',cwd=source)
    bank=root/'banks'/BANK
    if not bank.is_file() or bank.stat().st_size>8*1024*1024 or bank.read_bytes()[:8]!=b'UnityFS\0':
        raise BuildError('The independent Android movie shader bank was not produced.')
    target=root/BANK;os.replace(bank,target);_publish(root,key,target)
    # This tiny producer has no reusable original import. Its shader bank is the
    # retained result; removing its finished Library cannot invalidate movie data.
    for folder in ('Library','Temp','Logs','obj'):
        shutil.rmtree(project/folder,ignore_errors=True)
    return target


def _stop_child(process):
    """Let the converter reap its decoder/encoder before escalating its own tree."""
    if process.poll() is not None:return True
    try:
        if os.name=='nt':
            subprocess.run(['taskkill','/PID',str(process.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=10)
        else:
            import signal
            os.killpg(process.pid,signal.SIGINT)
        process.wait(timeout=10)
    except (OSError,subprocess.TimeoutExpired):
        try:process.kill();process.wait(timeout=5)
        except (OSError,subprocess.TimeoutExpired):return False
    return process.poll() is not None


def _child(builder,python,source,job,folder,on_progress,output):
    folder.mkdir(parents=True,exist_ok=True);job_path=folder/'job.json';write_json(job_path,job)
    env=dict(os.environ);env['NUMBA_CACHE_DIR']=str(output/'tool-cache/movie-numba')
    process=None
    with (folder/'render.log').open('w',encoding='utf-8') as log:
        try:
            if _CANCEL_CHILDREN.is_set():raise BuildError('Movie conversion was cancelled before launch; retained films are unchanged.')
            process=subprocess.Popen([str(python),'-I','-B',str(source/'tools/quest-builder/movie_render.py'),'--job',str(job_path)],
                stdout=subprocess.PIPE,stderr=log,text=True,env=env,start_new_session=os.name!='nt')
            with _CHILDREN_LOCK:_CHILDREN.add(process)
            if _CANCEL_CHILDREN.is_set():
                _stop_child(process);raise BuildError('Movie conversion was cancelled during launch; retained films are unchanged.')
            for line in process.stdout:
                log.write(line);log.flush()
                try:
                    event=json.loads(line)
                    if type(event.get('done')) is int:on_progress(event['done'])
                except (ValueError,TypeError):pass
            code=process.wait()
            if code:
                text=''
                for name in ('render.log','encode.log','decode.log'):
                    path=folder/name
                    if path.is_file():
                        with path.open('rb') as stream:
                            stream.seek(max(0,path.stat().st_size-16384));text+=stream.read().decode('utf-8','replace').lower()
                memory=any(token in text for token in ('memoryerror','out of memory','cannot allocate memory','bad_alloc','not enough memory')) or code in (-1073741801,3221225495)
                failure=MovieMemoryError if memory else BuildError
                raise failure('Owned stereo movie conversion failed; completed films are retained. See '+str(folder/'render.log'))
        finally:
            if process is not None:
                stopped=_stop_child(process)
                if process.stdout is not None:process.stdout.close()
                if stopped:
                    with _CHILDREN_LOCK:_CHILDREN.discard(process)
                else:log.write("Movie converter cleanup remains pending pid="+str(process.pid)+"\n");log.flush()


class MovieMemoryError(BuildError):
    pass


def worker_count(host,count,requested=None):
    """Use available physical and commit capacity; unknown capacity selects one job."""
    gib=1024**3;available=host.get('availableMemoryBytes')
    if type(available) is not int or available<=0:return 1
    commit=host.get('commitHeadroomBytes')
    if type(commit) is int and commit>0:available=min(available,commit)
    capacity=max(1,(available-min(gib,available//4))//gib)
    cpus=max(1,int(host.get('effectiveCpus') or 1)//4)
    return max(1,min(count,capacity,cpus,requested or count))


def produce(builder,args,source,inputs,output,game,*,previous=None,base_apk=None):
    _CANCEL_CHILDREN.clear()
    original_hashes=ImmutableFileHashes()
    source,output,game=map(Path,(source,output,game));expected=plan(source,inputs['game']);recipe=_recipe(source)
    # No large source snapshot: consume only bytes qualified by the selected
    # mod inventory. Unchanged movie caches do not reread numeric depth chunks.
    selected={row['path']:row for row in source_rows(inputs['mod']['files'])}
    for row in expected['recipe']['sources']:
        if selected.get(row['path'])!=row:raise BuildError('Stereo movie producer source changed after selection: '+row['path'])
    operation='update-art' if getattr(args,'command',None)=='update-mod' else 'stereo-movies'
    if operation=='stereo-movies':builder.build_progress.operation(operation,detail='Prepare or retain native stereo movies')
    render_hash=digest(source/'tools/quest-builder/movie_render.py')
    requirements_hash=digest(source/'tools/quest-builder/requirements-movies.txt')
    jobs=[]
    for movie in recipe['movies']:
        jobs.append({'key':movie['stereo_key'],'movie':movie,'kind':'render','frames':movie['frames'],
            'sourceHash':movie['source_sha256']})
        if movie['kind']=='embedded':
            jobs.append({'key':movie['key'],'movie':movie,'kind':'remux','frames':1,'sourceHash':movie['source_sha256']})
    for name in ('GH_CP_Intro.mov','GH_CP_Intro.sbs-v2.mov'):
        row=selected[CP+name];jobs.append({'key':'CP_Intro/'+name,'sourceFile':CP+name,'kind':'remux','frames':1,'sourceHash':row['sha256']})
    total=sum(j['frames'] for j in jobs)+1;progress=[0]*len(jobs);lock=threading.Lock();files=[];file_rows={}
    def report(index,done,detail):
        with lock:
            progress[index]=max(progress[index],min(done,jobs[index]['frames']))
            builder.build_progress.event('stereo-movies',sum(progress),total,'frames',detail,operation=operation)
    missing=[]
    old_rows={row['path']:row for row in (previous or {}).get('files',[])}
    movie_keys={}
    for i,job in enumerate(jobs):
        key=value_hash({'kind':job['kind'],'source':job['sourceHash'],'movie':job.get('movie'),
            'render':render_hash,'requirements':requirements_hash,'container':'android-mp4-no-tmcd-v1'})
        folder=output/'cache/quest-movies/films'/key;job.update(cacheKey=key,folder=folder);movie_keys[job['key']]=key
        prior=old_rows.get(job['key'])
        if base_apk is not None and (previous or {}).get('movieKeys',{}).get(job['key'])==key and prior is not None:
            with zipfile.ZipFile(base_apk) as archive:
                if archive.getinfo(PREFIX+job['key']).file_size!=prior['size']:
                    raise BuildError('Retained APK movie differs from its signed extent: '+job['key'])
            file_rows[job['key']]=prior;report(i,job['frames'],'Retained in APK '+job['key']);continue
        old=_retained(folder,key)
        if old:
            files.append((PREFIX+job['key'],folder/old['path']));file_rows[job['key']]={**old,'path':job['key']};report(i,job['frames'],'Retained '+job['key'])
        else:missing.append((i,job))
    if missing:
        python=_python(builder,source,output,operation)
        def convert(item,threads=3):
            i,job=item;folder=job['folder'];folder.mkdir(parents=True,exist_ok=True)
            target=folder/'movie.mov';part=folder/'movie.partial.mov';movie=job.get('movie')
            if 'sourceFile' in job:original=source/job['sourceFile']
            elif movie['kind']=='external':original=game/movie['source_relative']
            else:
                original=folder/'original.mov'
                resource=_ordinary_owned(game/movie['resource'])
                with resource.open('rb') as stream:
                    stream.seek(movie['resource_offset']);raw=stream.read(movie['resource_size'])
                if hashlib.sha256(raw).hexdigest()!=job['sourceHash']:raise BuildError('Owned embedded movie span changed.')
                original.write_bytes(raw)
            if original_hashes.digest(original)!=job['sourceHash']:raise BuildError('Owned movie changed before reconstruction: '+job['key'])
            try:
                _child(builder,python,source,{'kind':job['kind'],'source':str(original),'target':str(part),
                    'movie':movie,'recipeRoot':str(source/'tools/quest-movies'),'frames':job['frames'],'threads':threads},folder,
                    lambda done:report(i,done,job['key']),output)
                os.replace(part,target);row=_publish(folder,job['cacheKey'],target)
                with lock:file_rows[job['key']]={**row,'path':job['key']}
                report(i,job['frames'],'Completed '+job['key'])
                return PREFIX+job['key'],target
            finally:
                part.unlink(missing_ok=True)
                if movie is not None and movie['kind']=='embedded':original.unlink(missing_ok=True)
        # Independent films share CPU without retaining whole decoded movies.
        # Each worker holds at most128 half-depth frames and two1080p pictures.
        host=builder.host_resources.detect_host(output) if hasattr(builder,'host_resources') else {}
        workers=worker_count(host,len(missing),getattr(args,'jobs',None))
        write_json(output/'logs/movie-resource-policy.json',{'host':host,'workers':workers,'estimatedBytesPerFilm':1024**3,'unknownCapacityUsesOneWorker':True})
        memory_retries=[]
        executor=concurrent.futures.ThreadPoolExecutor(max_workers=workers)
        futures={}
        try:
            futures={executor.submit(convert,item):item for item in missing}
            for future in concurrent.futures.as_completed(futures):
                try:files.append(future.result())
                except MovieMemoryError:memory_retries.append(futures[future])
        except BaseException:
            _CANCEL_CHILDREN.set()
            for future in futures:future.cancel()
            with _CHILDREN_LOCK:children=list(_CHILDREN)
            for child in children:_stop_child(child)
            raise
        finally:executor.shutdown(wait=True,cancel_futures=True)
        # A witnessed allocation failure retries only its incomplete film, after
        # all other codec children have exited, with one encoder thread.
        for item in memory_retries:
            builder.build_progress.event('stereo-movies',sum(progress),total,'frames',
                'Retry only the incomplete movie with reduced memory pressure: '+item[1]['key'],operation=operation,status='repair')
            files.append(convert(item,threads=1))
    builder.build_progress.event('stereo-movies',total-1,total,'frames','Prepare or retain the two Android movie shaders',operation=operation)
    bank=_shader_bank(builder,args,source,output,previous=previous,base_apk=base_apk);files.append((PREFIX+BANK,bank))
    file_rows[BANK]=_record(bank,BANK)
    rows=[file_rows[name] for name in sorted(file_rows)]
    # The selected repository may be updated while a long conversion runs.
    # Unchanged witnesses make this cheap; never publish a mixed recipe/helper
    # revision under the key selected before conversion.
    if plan(source,inputs['game']) != expected:
        raise BuildError('Stereo movie producer source changed during conversion. Completed films remain reusable; select the current mod and continue.')
    manifest={'schema':1,'target':'Android','producerKey':expected['producerKey'],'shaderKey':expected['shaderKey'],
        'movieKeys':movie_keys,'files':rows}
    if len(rows)!=23:raise BuildError('Stereo movie delivery omitted a native film or shader.')
    builder.build_progress.event('stereo-movies',total,total,'frames','Stereo movie files and Android shader bank complete',operation=operation,status='complete')
    if operation=='stereo-movies':builder.build_progress.operation(operation,complete=True,detail='Stereo movies complete or retained')
    return {'metadata':manifest,'files':files}


def stage(project,payload):
    root=Path(project)/'Assets/StreamingAssets'
    for member,path in payload['files']:
        target=root/member[len('assets/'):];target.parent.mkdir(parents=True,exist_ok=True)
        if target.is_file() and _stamp(target)==_stamp(path):continue
        temporary=target.with_name(target.name+'.staging');temporary.unlink(missing_ok=True)
        try:
            try:os.link(path,temporary)
            except OSError:shutil.copy2(path,temporary)
            os.replace(temporary,target)
        finally:temporary.unlink(missing_ok=True)
    write_json(root/METADATA[len('assets/'):],payload['metadata'])


def delivery(builder,source,inputs,output,game,*,args=None,previous=None,base_apk=None):
    payload=produce(builder,args,source,inputs,output,game,previous=previous,base_apk=base_apk)
    old={row['path']:row for row in (previous or {}).get('files',[])}
    result={METADATA:canonical(payload['metadata'])}
    for member,path in payload['files']:
        row=next(row for row in payload['metadata']['files'] if member==PREFIX+row['path'])
        if old.get(row['path'])!=row:result[member]=path
    return result
