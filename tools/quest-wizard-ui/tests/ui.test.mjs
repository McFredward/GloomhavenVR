import test from 'node:test';
import assert from 'node:assert/strict';
import {strings,translate} from '../i18n.mjs';
import {choicesFromForm,progressView,macroStep,isActive,stageStatus,stageProgress,artworkUrl,publisherSourceUrl,sessionId,savedSession,activeWorkView,timingView,durationText,failureView,activityView,buildOverviewView} from '../model.mjs';
import {LocalApi,PreviewApi} from '../transport.mjs';

test('German and English expose the same strings and parameter ABI',()=>{
  assert.deepEqual(Object.keys(strings.de).sort(),Object.keys(strings.en).sort());
  for(const key of Object.keys(strings.de)) {
    assert.deepEqual([...strings.de[key].matchAll(/\{([A-Za-z]+)\}/g)].map(row=>row[1]).sort(),[...strings.en[key].matchAll(/\{([A-Za-z]+)\}/g)].map(row=>row[1]).sort(),key);
    assert.ok(strings.de[key].length&&strings.en[key].length,key);
  }
  assert.equal(translate('en','foundOne'),'Found one local game copy.');
  assert.equal(translate('de','foundMany',{count:3}),'3 lokale Spielkopien gefunden.');
});

test('terminal failure exposes its concrete bounded cause; stale activity never invents progress',()=>{
  const action={stage:'build',message:{de:'Konvertierung fehlgeschlagen',en:'Conversion failed'},parameters:{failureStage:'recovery',cause:"Traceback:\n ModuleNotFoundError: No module named 'recover'"}};
  assert.deepEqual(failureView({status:'failed',needsActions:[action]}),{stage:'recovery',message:action.message,cause:"ModuleNotFoundError: No module named 'recover'"});
  for(const status of ['running','blocked','complete','cancelled'])assert.equal(failureView({status,needsActions:[action]}),null,'retained old errors are not active failures');
  assert.equal(failureView({status:'failed',stages:[{id:'build',status:'failed'}]}).stage,'build');
  assert.equal(stageProgress({status:'failed',waiting:{since:100}}).waiting,null,'a retained prerequisite does not conceal a failed stage');
  assert.equal(failureView({status:'failed',needsActions:[{parameters:{builderError:'x'.repeat(900)}}]}).cause.length,360);
  assert.deepEqual(activityView({progress:{updatedAt:100}},140),{seconds:40,quiet:false});
  assert.deepEqual(activityView({progress:{updatedAt:100}},280),{seconds:180,quiet:true});
  assert.deepEqual(activityView({progress:{updatedAt:100}},90),{seconds:0,quiet:false});
  for(const updatedAt of [null,undefined,NaN,Infinity,-1,0,'100'])assert.equal(activityView({progress:{updatedAt}},140),null);
});

test('native memory prerequisite exposes retry context without resurrecting an old failure',()=>{
  const action={code:'native_memory_unavailable',stage:'build',message:{de:'Verfügbarer RAM: 32.8 GiB; Commit: 24.5 GiB; benötigt: 44.4 GiB.',en:'Available RAM: 32.8 GiB; commit: 24.5 GiB; required: 44.4 GiB.'},
    parameters:{failureStage:'native-memory-check',cause:'Native compiler memory admission refused.'}};
  assert.deepEqual(failureView({status:'blocked',needsActions:[action]}),{stage:'native-memory-check',message:action.message,cause:action.parameters.cause,blocked:true});
  for(const status of ['running','complete','cancelled'])assert.equal(failureView({status,needsActions:[action]}),null);
  assert.equal(translate('de','phase_stage_native-memory-check'),'Speicherkapazität für native Kompilierung');
  assert.match(translate('de','nativeMemoryNext'),/erneut versuchen/);
  assert.doesNotMatch(translate('de','nativeMemoryNext'),/korrigierten Builder/);
});

test('overview uses retained completion and explicit active work, never wizard setup counts',()=>{
  const raw={schema:1,active:'recovery',groups:[{id:'inputs',operations:[{id:'game-inputs',status:'retained',closed:true,percent:100}]},
    {id:'recovery',operations:[{id:'recovery',status:'running',closed:false,percent:97}]}],recovery:{batches:{done:16,total:16},sections:[],staging:[]}};
  const view=buildOverviewView({stages:[{id:'build',progress:{buildOverview:raw}}],progress:{completed:5,total:7}});
  assert.equal(view.done,1);assert.equal(view.total,2);assert.equal(view.active,'recovery');
  assert.equal(view.groups[0].operations[0].status,'retained');
  assert.deepEqual(view.recovery.batches,{done:16,total:16});
  assert.equal(buildOverviewView({stages:[]}),null);
  assert.equal(buildOverviewView({stages:[{id:'build',progress:{buildOverview:{...raw,groups:[...raw.groups,raw.groups[0]]}}}]}),null,'duplicate planned operations cannot inflate completion');
});

test('graphics ordinal, closed checkpoint count, named pass plan and native compiler scope stay distinct',()=>{
  const work=activeWorkView({activeWork:{operation:'graphics',done:1,total:2,index:2,checkpoint:'campaign-shaders',passes:{done:4,total:14,active:'campaign-shaders-inventory',known:true}}});
  assert.equal(work.done,1);assert.equal(work.index,2);assert.equal(work.passes.total,14);assert.equal(work.passes.known,true);
  const checkpoints=[{id:'campaign-compute',closed:true,status:'retained',detail:'aggregate',percent:100,passes:[]},
    {id:'campaign-shaders',closed:false,status:'running',percent:25,passPlanKnown:true,
      passes:[{id:'campaign-shaders-backup',closed:true,status:'complete',percent:100},
        {id:'campaign-shaders-inventory',closed:false,status:'running',percent:5,counter:{done:1,total:300,unit:'shaders'}}],
      nestedCounter:{phase:'prepare-items:campaign-shaders-variants',done:10,total:10,unit:'variants'}}];
  const raw={schema:1,active:'graphics',groups:[{id:'project',operations:[{id:'graphics',closed:false,status:'running',percent:51,
    preparation:{checkpoints,active:'campaign-shaders'}}]}]};
  const view=buildOverviewView({stages:[{id:'build',progress:{buildOverview:raw}}]});
  const preparation=view.groups[0].operations[0].preparation;
  assert.equal(preparation.done,1);assert.equal(preparation.total,2);
  assert.equal(preparation.checkpoints[1].passDone,1);assert.equal(preparation.checkpoints[1].passTotal,2);
  assert.equal(preparation.checkpoints[1].nestedCounter.done,10);
  const compiler={phase:'unity-shader-compile',scope:'pass',done:4903,total:12288,unit:'variants',percent:100*4903/12288,status:'running'};
  const compilerView=buildOverviewView({stages:[{id:'build',progress:{buildOverview:{...raw,groups:[{id:'project',operations:[{id:'mod-banks',closed:false,status:'running',percent:75,compiler}]}]}}}]}).groups[0].operations[0].compiler;
  assert.equal(compilerView.scope,'pass');assert.equal(compilerView.counter.total,12288);
  for(const language of ['de','en'])for(const key of ['activeWorkCheckpoint','activeWorkPasses','buildPassObserved','phase_unity-shader-compile','shaderPassScope','shaderTaskScope'])assert.notEqual(translate(language,key),key);
});

test('all fourteen project-staging sections and their file counters have plain localized labels',()=>{
  const sections=['catalog','canonical','copy','runtime','guid','layout','native','catalog-final','index','tmp','bindings','audit','scenes','report'];
  const phases=['recovery-asset-reference-file','recovery-section:staging','staging-copy','staging-copy-file','staging-report-hash',
    'staging-managed-assemblies','staging-runtime-copy','staging-runtime-file','staging-report-files','staging-report-file',
    'staging-resume-verify','staging-resume-verify-files','prepare-resume-verify',
    'prepare-substage:compiler-contracts','prepare-substage:case-paths','prepare-substage:startup-compute','prepare-substage:script-orders','prepare-substage:final-settings',
    ...['startup-movies-backup','startup-movies-clips','startup-movies-scenes','startup-movies-assets','startup-movies-media',
      'native-sprites-identities','native-sprites-packed','native-sprites-targets','native-sprites-containers',
      'startup-audio-metadata','startup-audio-preflight','startup-audio-write','startup-audio-decode'].map(name=>'prepare-items:'+name),
    ...sections.map(section=>'staging-section:'+section)];
  for(const language of ['de','en'])for(const phase of phases){
    const key='phase_'+phase,label=translate(language,key);
    assert.notEqual(label,key,language+': '+phase);
    assert.ok(label.length>3&&label.length<70,language+': '+phase);
  }
  assert.equal(translate('de','phase_recovery-section:staging'),'Konvertierte Assets bereitstellen');
  assert.equal(translate('de','phase_staging-section:tmp'),'Schriften vorbereiten');
  assert.equal(translate('en','phase_staging-copy-file'),'Copy asset file');
});

test('whole project reuse does not expose fabricated pending children from an older saved overview',()=>{
  const sections=[{id:'staging',status:'complete',closed:true,percent:100}];
  const raw={schema:1,active:'textures',groups:[{id:'recovery',operations:[{id:'recovery',status:'complete',closed:true,percent:100}]},
    {id:'project',operations:[{id:'textures',status:'running',closed:false,percent:0}]}],
    recovery:{sections,staging:[{id:'catalog',status:'pending',closed:false,percent:0}]}};
  const overview=value=>buildOverviewView({stages:[{id:'build',progress:{buildOverview:value}}]});
  const view=overview(raw);
  assert.equal(view.done,1);assert.equal(view.active,'textures');
  assert.equal(view.recovery.stagingDetail,'aggregate');assert.deepEqual(view.recovery.staging,[]);
  assert.equal(view.recovery.sections[0].status,'complete');
  const live=overview({...raw,recovery:{...raw.recovery,staging:[{id:'catalog',status:'checking',closed:true,percent:100},
    {id:'copy',status:'pending',closed:false,percent:0}]}});
  assert.equal(live.recovery.stagingDetail,'observed');assert.equal(live.recovery.staging[0].status,'checking');
  const finished=overview({...raw,recovery:{...raw.recovery,staging:[{id:'catalog',status:'retained',closed:true,percent:100}]}});
  assert.equal(finished.recovery.stagingDetail,'observed');assert.equal(finished.recovery.staging[0].status,'retained');
  const unobserved=overview({...raw,recovery:{...raw.recovery,sections:[{id:'staging',status:'pending',closed:false,percent:0}]}});
  assert.equal(unobserved.recovery.stagingDetail,'unobserved');assert.deepEqual(unobserved.recovery.staging,[]);
  for(const language of ['de','en']){
    assert.notEqual(translate(language,'phase_network-preflight'),'phase_network-preflight');
    assert.ok(translate(language,'counterDependencies',{done:2,total:3}).includes('2 / 3'));
  }
});

test('ownership is never inferred from selected checkboxes unless explicitly declared',()=>{
  const base={gameRoot:' C:\\Owned Game ',provider:'gog',ownedDlc:['jotl'],install:true};
  assert.deepEqual(choicesFromForm(base,'de'),{gameRoot:'C:\\Owned Game',provider:'gog',mode:'build',install:true,acceptUnityTerms:false,language:'de'});
  assert.deepEqual(choicesFromForm({...base,declareDlc:true,ownedDlc:[]},'de').ownedDlc,[]);
  assert.throws(()=>choicesFromForm({...base,hostSystem:'Linux',provider:'steam'},'de'),/linuxDlcRequired/);
  assert.deepEqual(choicesFromForm({...base,hostSystem:'Linux',provider:'steam',declareDlc:true,ownedDlc:['solo']},'de').ownedDlc,['solo']);
  assert.deepEqual(choicesFromForm({...base,declareDlc:true,ownedDlc:['solo','solo','not-dlc','jotl']},'de').ownedDlc,['solo','jotl']);
  assert.throws(()=>choicesFromForm({gameRoot:' '},'de'),/missingGame/);
  assert.throws(()=>choicesFromForm({...base,profileName:'Name'},'de'),/missingProfile/);
  assert.deepEqual(choicesFromForm({...base,provider:'steam',profileName:'Name',profileId:'123'},'en').profile,{schema:1,provider:'steam',displayName:'Name',providerId:'123',steamId:'123'});
  assert.equal(choicesFromForm({...base,profileName:'Name',profileId:'gog-id'},'en').profile.steamId,undefined);
});

test('update workflows require the owned game and selected base APK; profile updates need no Unity consent',()=>{
  const base={gameRoot:' C:\\Owned Game ',provider:'gog',baseApk:' C:\\Quest\\Built.apk ',install:false};
  for(const mode of ['update-mod','update-profile']){
    const result=choicesFromForm({...base,mode},'de');
    assert.equal(result.mode,mode);assert.equal(result.baseApk,'C:\\Quest\\Built.apk');
    assert.equal(result.acceptUnityTerms,false);assert.equal(result.gameRoot,'C:\\Owned Game');
    assert.equal(choicesFromForm({...base,mode,signingRoot:' C:\\Owner Workspace '},'de').signingRoot,'C:\\Owner Workspace');
    assert.throws(()=>choicesFromForm({...base,mode,baseApk:' '},'de'),/missingBaseApk/);
    assert.throws(()=>choicesFromForm({...base,mode,gameRoot:' '},'de'),/missingGame/);
  }
  assert.equal(choicesFromForm(base,'de').mode,'build');
  assert.equal(choicesFromForm(base,'de').baseApk,undefined,'a new build cannot silently consume an update APK');
  assert.equal(choicesFromForm({...base,signingRoot:' C:\\Unused '},'de').signingRoot,undefined);
  for(const mode of ['unsupported','',true])assert.throws(()=>choicesFromForm({...base,mode},'de'),/invalidMode/);
});

test('prerequisite requalification keeps the retained build primary and exposes actual live work separately',()=>{
  const build={id:'build',status:'pending',progress:{stagePercent:49.5,phase:'pending',buildOverview:{active:null}}};
  for(const id of ['tools','source','unity','profile','inspect']){
    const active={id,status:'running',progress:{stagePercent:0,phase:'receipt-verify',percent:25}};
    const result=progressView({status:'running',stages:[active,build,{id:'install',status:'pending'}]});
    assert.equal(result.current,build);assert.equal(result.active,active);assert.equal(result.retaining,true);
    assert.equal(result.phase,'build');assert.equal(result.percent,49.5);assert.equal(result.width,49.5);
    assert.equal(macroStep({status:'running',stages:[active,build]}),2);
  }
  const source={id:'source',status:'blocked',waiting:{code:'source_required'},progress:{stagePercent:20}};
  const blocked=progressView({status:'blocked',stages:[source,build]});
  assert.equal(blocked.retaining,true);assert.equal(blocked.active,source);assert.equal(blocked.current,build);
  const fresh={...build,progress:{stagePercent:0}};
  assert.equal(progressView({status:'running',stages:[{...source,status:'running'},fresh]}).retaining,false,'changed game/workflow scope has no retained build');
  const executing={...build,status:'running',progress:{stagePercent:50,phase:'recovery'}};
  const current=progressView({status:'running',stages:[{...source,status:'complete'},executing]});
  assert.equal(current.current,executing);assert.equal(current.active,executing);assert.equal(current.retaining,false);
  const installation={id:'install',status:'running',progress:{stagePercent:15}};
  assert.equal(progressView({status:'running',stages:[{...build,status:'complete'},installation]}).current,installation);
});

test('whole-stage progress never substitutes a resettable phase percentage',()=>{
  const state={status:'running',stages:[{id:'tools',status:'complete'},{id:'build',status:'running'}]};
  assert.deepEqual(progressView(state),{completed:1,total:2,phase:'build',percent:0,width:0,indeterminate:false,current:state.stages[1],active:state.stages[1],retaining:false});
  for(const percent of [NaN,Infinity,-1,101,'50',null])assert.equal(progressView({...state,progress:{percent}}).percent,0);
  assert.equal(progressView({...state,progress:{percent:0}}).percent,0);
  assert.equal(progressView({...state,progress:{percent:72}}).width,0);
  assert.equal(macroStep(state),2);assert.equal(macroStep({...state,status:'complete'}),3);
  assert.equal(isActive({status:'cancel_requested'}),true);assert.equal(isActive({status:'cancelled'}),false);
  const interrupted={status:'interrupted',stages:[{id:'tools',status:'complete'},{id:'build',status:'interrupted'},{id:'install',status:'pending'}],progress:{phase:null,percent:null}};
  assert.equal(progressView(interrupted).phase,'build');assert.equal(macroStep(interrupted),2);assert.equal(isActive(interrupted),false);
  assert.equal(stageStatus({choices:{install:false}},{id:'install',status:'complete'}),'skipped');
  assert.equal(stageStatus({choices:{install:true}},{id:'install',status:'complete',details:{requested:false}}),'skipped');
  assert.equal(stageStatus({choices:{install:true}},{id:'install',status:'complete',details:{requested:true}}),'complete');
});

test('browser artwork URLs and session IDs remain local and bounded',()=>{
  const origin='http://127.0.0.1:1234';
  assert.equal(artworkUrl('/api/artwork?session=session-123&id=image',origin),origin+'/api/artwork?session=session-123&id=image');
  for(const value of ['https://other.invalid/api/artwork','file:///original.png','data:image/png,test','/Assets/Texture.png','//other.invalid/api/artwork'])assert.equal(artworkUrl(value,origin),null);
  assert.equal(publisherSourceUrl('https://store.steampowered.com/news/posts/?appids=780290'),'https://store.steampowered.com/news/posts/?appids=780290');
  for(const value of ['https://store.steampowered.com/account/','http://store.steampowered.com/news/','https://store.steampowered.com:123/news/','https://other.invalid/news/','javascript:alert(1)','https://user@store.steampowered.com/news/',undefined])assert.equal(publisherSourceUrl(value),null);
  assert.equal(sessionId('session-123'),'session-123');
  for(const value of ['../secret','short','x'.repeat(129),null])assert.equal(sessionId(value),null);
});

test('saved owner session takes precedence over recent history and needs no browser storage',()=>{
  assert.equal(savedSession({latestSession:'latest-session',recentSessions:[{session:'recent-session'}]}),'latest-session');
  assert.equal(savedSession({recentSessions:[{session:'../invalid'},{session:'recent-session'}]}),'recent-session');
  assert.equal(savedSession({latestSession:'../invalid',recentSessions:[{session:'recent-session'}]}),'recent-session');
  for(const discovery of [null,{}, {recentSessions:{}},{latestSession:'bad',recentSessions:[null]}])assert.equal(savedSession(discovery),null);
});

test('phase completion cannot finish a stage and the next phase leaves its achieved total intact',()=>{
  const first={id:'build',status:'running',progress:{stagePercent:71.5,percent:100,phase:'stage:recovery'}};
  assert.equal(stageProgress(first).percent,71.5);assert.equal(stageProgress(first).phasePercent,100);
  const next={...first,progress:{stagePercent:72,percent:5,phase:'stage:prepare'}};
  assert.equal(progressView({status:'running',stages:[next]}).percent,72);
  assert.equal(stageProgress(next).phasePercent,5);
  assert.equal(stageProgress({...next,progress:{stagePercent:100,percent:100}}).percent,99.9,'only verified completion reaches 100');
  assert.equal(stageProgress({...next,status:'complete'}).percent,100);
  assert.equal(stageProgress({...next,status:'interrupted'}).percent,72);
  const cancelled=progressView({status:'cancelled',stages:[{...next,status:'cancelled'},{id:'install',status:'pending'}]});
  assert.equal(cancelled.phase,'build');assert.equal(cancelled.percent,72);
  for(const stagePercent of [NaN,Infinity,-1,101,'50',null])assert.equal(stageProgress({...next,progress:{stagePercent,percent:80}}).percent,0);
});

test('measured phases remain separate from planned whole-stage progress and prerequisite actions',async()=>{
  const stage={id:'unity',status:'running',progress:{phase:'unity-prerequisites',done:3,total:4,percent:75,stagePercent:37.5,unit:'checks'},waiting:{nonce:'a'.repeat(32)}};
  assert.equal(progressView({status:'running',stages:[stage],progress:{percent:null}}).percent,37.5);
  assert.equal(stageProgress(stage).percent,37.5);assert.equal(stageProgress(stage).phasePercent,75);assert.equal(stageProgress(stage).waiting.nonce,'a'.repeat(32));
  assert.equal(stageProgress({...stage,progress:{phase:'unity-install',stagePercent:38,percent:null}}).percent,38);assert.equal(stageProgress({...stage,progress:{phase:'unity-install',percent:null}}).phasePercent,null);
  assert.equal(stageProgress({status:'complete'}).percent,100);assert.equal(stageProgress({status:'pending'}).percent,0);
  assert.equal(stageProgress({status:'pending',progress:{stagePercent:37.5,phase:'pending'}}).percent,37.5,'queued retry retains compatible observed whole-stage work');
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>{calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'action_requested'})};});
  await api.action('session-123','unity-open','a'.repeat(32));assert.deepEqual(JSON.parse(calls[0].options.body),{session:'session-123',action:'unity-open',nonce:'a'.repeat(32)});
  assert.throws(()=>api.action('session-123','cmd.exe','a'.repeat(32)));assert.throws(()=>api.action('session-123','unity-check','old-nonce'));
  assert.equal(artworkUrl('/api/promo-artwork?id=brute','http://127.0.0.1:1234'),'http://127.0.0.1:1234/api/promo-artwork?id=brute');
  assert.ok(!strings.de.progressEyebrow.includes('ABENTEUER'));assert.ok(!strings.en.progressEyebrow.includes('ADVENTURE'));
});

test('real transport sends token, structured choices and explicit optional session',async()=>{
  const calls=[];const api=new LocalApi('http://127.0.0.1:1234','private-token',async(url,options)=>{calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'planned'})};});
  await api.plan({gameRoot:'C:\\Owned'},'session-123');
  assert.deepEqual(JSON.parse(calls[0].options.body),{choices:{gameRoot:'C:\\Owned'},session:'session-123'});
  assert.equal(calls[0].options.headers['X-Quest-Token'],'private-token');assert.equal(calls[0].options.method,'POST');
  await api.events('session-123',NaN);assert.ok(calls[1].url.endsWith('after=0'));
  await api.log('session-123','build');assert.ok(calls[2].url.endsWith('stage=build'));
  assert.throws(()=>api.log('session-123','../../private'),error=>error.code==='invalidReply');
  assert.throws(()=>api.status('../bad'),error=>error.code==='invalidReply');
  assert.throws(()=>api.plan({},'../bad'),error=>error.code==='invalidReply');
  await assert.rejects(()=>new LocalApi('http://127.0.0.1:1234','',()=>{throw Error('should never fetch');}).discover(),error=>error.code==='noToken');
});

test('transport retains bilingual blocked errors and rejects malformed API responses',async()=>{
  const denied={schema:1,event:'error',code:'unity-required',message:{de:'Unity fehlt',en:'Unity missing'}};
  await assert.rejects(()=>new LocalApi('http://127.0.0.1:1234','token',async()=>({ok:false,json:async()=>denied})).discover(),error=>error===denied);
  await assert.rejects(()=>new LocalApi('http://127.0.0.1:1234','token',async()=>({ok:true,json:async()=>({schema:2})})).discover(),error=>error.code==='invalidReply');
});

test('only optional log observers accept cancellation; user and Unity actions have no imposed timeout',async()=>{
  const calls=[],controller=new AbortController();
  const api=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>{
    calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'fixture'})};
  });
  await api.events('session-123',0,controller.signal);await api.log('session-123','build',controller.signal);
  assert.ok(calls.slice(0,2).every(call=>call.options.signal===controller.signal));
  await api.status('session-123');await api.action('session-123','unity-check','a'.repeat(32));await api.browse('game');
  assert.ok(calls.slice(2).every(call=>!('signal' in call.options)));
  const pending=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>new Promise((resolve,reject)=>options.signal.addEventListener('abort',()=>reject(Error('cancelled observer')),{once:true})));
  const request=pending.events('session-123',0,controller.signal);controller.abort();
  await assert.rejects(()=>request,error=>error.code==='offline');
});

test('preview can never report a completed real build or invoke external tools',async()=>{
  const api=new PreviewApi();const discovery=await api.discover();assert.equal(discovery.capabilities.browse,false);
  await api.plan({gameRoot:'preview'});await api.run();
  for(let index=0;index<30;index++)await api.status();
  assert.equal(api.state.status,'blocked');assert.equal(api.state.needsActions[0].code,'preview-only');
  assert.equal(api.state.stages.find(row=>row.id==='build').status,'blocked');assert.notEqual(api.state.stages.find(row=>row.id==='install').status,'complete');
  await api.cancel();assert.equal(api.state.status,'cancelled');
  assert.ok(api.state.stages.every(row=>row.status!=='running'));
});


test('support transport downloads a token-protected ZIP and rejects foreign content',async()=>{
  let call;
  const blob=new Blob(['fixture']);
  const api=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>{
    call={url:String(url),options};return {ok:true,headers:new Map([['Content-Type','application/zip'],['Content-Disposition','attachment; filename="quest-build-support.zip"']]),blob:async()=>blob};
  });
  const result=await api.support('session-123');assert.equal(result.blob,blob);assert.equal(result.name,'quest-build-support.zip');
  assert.equal(call.options.headers['X-Quest-Token'],'token');assert.deepEqual(JSON.parse(call.options.body),{session:'session-123'});
  await assert.rejects(()=>api.support('../private'),error=>error.code==='invalidReply');
  const bad=new LocalApi('http://127.0.0.1:1234','token',async()=>({ok:true,headers:new Map([['Content-Type','text/html']])}));
  await assert.rejects(()=>bad.support('session-123'),error=>error.code==='invalidReply');
});

test('active subtask counts use the current operation instead of wizard stage counts',()=>{
  assert.deepEqual(activeWorkView({activeWork:{operation:'recovery',done:6,total:22,percent:31.7}}),{operation:'recovery',done:6,total:22});
  assert.equal(activeWorkView({completed:5,total:7}),null);
  for(const work of [{done:0,total:0},{done:23,total:22},{done:-1,total:22},{done:6.2,total:22},{done:6,total:'22'}])assert.equal(activeWorkView({activeWork:work}),null);
  assert.equal(activeWorkView({activeWork:{done:6,total:22,operation:'../../invalid'}}).operation,null);
});

test('durations and ETA retain measured scope, honest missing history and finite ranges',()=>{
  assert.equal(durationText(8100.9),'02:15:00');assert.equal(durationText(10),'00:00:10');assert.equal(durationText(0),'00:00:00');
  for(const value of [NaN,Infinity,-1,'10',null]){assert.equal(durationText(value),'');assert.equal(timingView({elapsedSeconds:value}),null);}
  const row={elapsedSeconds:3600,active:true,elapsedBasis:'since-update',estimate:{status:'estimated',scope:'conversion-batches',remainingSeconds:5400,lowerSeconds:4200,upperSeconds:7200,samples:3}};
  assert.deepEqual(timingView(row),{elapsedSeconds:3600,active:true,elapsedBasis:'since-update',estimate:{status:'estimated',scope:'conversion-batches',lowerSeconds:4200,upperSeconds:7200}});
  assert.equal(timingView({...row,estimate:{...row.estimate,upperSeconds:4000}}).estimate.status,'unknown');
  assert.equal(timingView({...row,estimate:{...row.estimate,remainingSeconds:NaN}}).estimate.status,'unknown');
  assert.equal(timingView({...row,estimate:{...row.estimate,scope:'whole-build'}}).estimate.status,'unknown','a phase estimate cannot be relabelled as whole-build time');
  for(const status of ['paused','learning','complete','unknown'])assert.equal(timingView({...row,estimate:{status,scope:'phase'}}).estimate.status,status);
  assert.equal(timingView({elapsedSeconds:12}).elapsedBasis,'recorded-active');
});

test('measured graphics and restoration tasks have localized readable labels', () => {
  const phases = ['campaign-compute-kernels','campaign-compute-reference-scan',
    'campaign-compute-reference-index','campaign-compute-publish',
    ...['bundles','identities','inventory','containers','extract','variants','materials','binary-materials',
      'material-containers','programs','sources','copy','publish','backup','contracts'].map(name=>'campaign-shaders-'+name),
    ...['native-cubemaps','native-texture2d','campaign-compute'].flatMap(name =>
      ['identities','pointer-owners','backup','contracts','manifests','reference-discovery',
        'reference-scan','reference-receipts','rollback-verify','rollback-restore'].map(action => name+'-'+action))];
  for (const language of ['de','en']) for (const phase of phases) {
    const key='phase_prepare-items:'+phase, label=translate(language,key);
    assert.notEqual(label,key,language+': '+phase);
    assert.ok(label.length > 3 && label.length < 70,language+': '+phase);
  }
});
