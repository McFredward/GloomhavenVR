export const stageIds = ['tools','source','unity','profile','inspect','build','install'];
export function choicesFromForm(form, language) {
  const gameRoot = String(form.gameRoot ?? '').trim();
  if (!gameRoot) throw new Error('missingGame');
  const mode = form.mode ?? 'build';
  if (!['build','update-mod','update-profile'].includes(mode)) throw new Error('invalidMode');
  const baseApk = String(form.baseApk ?? '').trim();
  if (mode !== 'build' && !baseApk) throw new Error('missingBaseApk');
  const provider = ['steam','epic','gog'].includes(form.provider) ? form.provider : 'steam';
  if(form.hostSystem==='Linux'&&provider==='steam'&&mode!=='update-profile'&&!form.declareDlc)throw new Error('linuxDlcRequired');
  const choices = {gameRoot, provider, mode, install:Boolean(form.install), acceptUnityTerms:Boolean(form.acceptUnityTerms), language};
  if (mode !== 'build') choices.baseApk = baseApk;
  if (mode !== 'build' && String(form.signingRoot ?? '').trim()) choices.signingRoot = form.signingRoot.trim();
  if (String(form.unityEditor ?? '').trim()) choices.unityEditor = form.unityEditor.trim();
  const displayName = String(form.profileName ?? '').trim(), providerId = String(form.profileId ?? '').trim();
  if (displayName || providerId) {
    if (!displayName || !providerId) throw new Error('missingProfile');
    choices.profile = {schema:1, provider, displayName, providerId};
    if (provider === 'steam') choices.profile.steamId = providerId;
  }
  if (form.declareDlc) choices.ownedDlc = [...new Set((form.ownedDlc ?? []).filter(value => ['jotl','solo','jotl-skins'].includes(value)))];
  return choices;
}
export function progressView(state) {
  const stages = Array.isArray(state?.stages) ? state.stages : [];
  const completed = Number.isInteger(state?.progress?.completed) ? state.progress.completed : stages.filter(row => row.status === 'complete').length;
  const total = Number.isInteger(state?.progress?.total) && state.progress.total > 0 ? state.progress.total : stages.length;
  const active = stages.find(row => row.status === 'running') ?? stages.find(row => ['blocked','failed','interrupted','cancelled'].includes(row.status)) ?? stages.find(row=>row.status==='pending');
  const build = stages.find(row => row.id === 'build');
  // Setup receipts can change after a Builder update while closed game work
  // waits for qualification. Keep that work's total visible and name the
  // actual prerequisite separately; this does not certify an old receipt.
  const retaining = Boolean(build && build.status !== 'complete' && stageProgress(build).percent > 0 &&
    active && stageIds.indexOf(active.id) >= 0 && stageIds.indexOf(active.id) < stageIds.indexOf('build'));
  const current = retaining ? build : active;
  // A native phase can finish and restart inside one stage. Its percentage is
  // never a substitute for the durable, planned whole-stage percentage.
  const percent = state?.status === 'complete' ? 100 : stageProgress(current).percent;
  return {completed, total, phase:current?.id ?? state?.progress?.phase ?? '', percent,
    width:percent, indeterminate:false, current, active, retaining};
}
export function macroStep(state) {
  const phase = progressView(state).phase;
  if (state?.status === 'complete' || phase === 'install') return 3;
  if (phase === 'build') return 2;
  return 1;
}
export function isActive(state) { return ['running','cancelling','cancel_requested'].includes(state?.status); }
export function failureView(state) {
  const action=state?.needsActions?.find(value=>value&&typeof value==='object')??{};
  const memoryBlocked=state?.status==='blocked'&&action.code==='native_memory_unavailable';
  if(state?.status!=='failed'&&!memoryBlocked)return null;
  const parameters=action.parameters??{};
  const cause=typeof parameters.cause==='string'?parameters.cause:typeof parameters.builderError==='string'?parameters.builderError:'';
  // Show one concrete, bounded diagnostic rather than a traceback wall. Text
  // stays untrusted textContent; the complete original remains in the logs.
  const lines=cause.split(/\r?\n/).map(line=>line.trim()).filter(Boolean);
  return {stage:typeof parameters.failureStage==='string'?parameters.failureStage:action.stage??state.stages?.find(row=>row.status==='failed')?.id,
    message:action.message??null,cause:lines.at(-1)?.replace(/[\u0000-\u001f\u007f]/g,' ').slice(0,360)??'',
    ...(memoryBlocked?{blocked:true}:{})};
}
export function activityView(stage,now=Date.now()/1000) {
  const updated=stage?.progress?.updatedAt;
  if(typeof updated!=='number'||!Number.isFinite(updated)||updated<=0||typeof now!=='number'||!Number.isFinite(now))return null;
  const seconds=Math.max(0,Math.floor(now-updated));
  return {seconds,quiet:seconds>=60};
}
export function unityImportView(progress,now=Date.now()/1000) {
  const value=progress?.unityImport??progress;
  if(!value||!['unity-asset-import','unity-import-activity'].includes(value.phase))return null;
  const done=Number.isSafeInteger(value.done)&&value.done>=0?value.done:null;
  const total=Number.isSafeInteger(value.total)&&value.total>0&&(done===null||done<=value.total)?value.total:null;
  const text=typeof value.detail==='string'?value.detail.replace(/[\u0000-\u001f\u007f]/g,' ').slice(0,1024):'';
  const started=value.startedAt,status=['running','failed','complete','pending'].includes(value.status)?value.status:'running';
  const endpoint=status==='running'?now:value.updatedAt;
  const elapsedSeconds=typeof started==='number'&&Number.isFinite(started)&&started>0&&Number.isFinite(endpoint)?Math.max(0,endpoint-started):null;
  return {done,total,detail:text,elapsedSeconds,
    percent:done!==null&&total!==null?100*done/total:null,
    status};
}
export function nativeMemoryView(stage) {
  const value=stage?.progress??{},phases=['native-compiler-profile','native-memory-retry','native-memory-wait','asset-memory-retry','asset-memory-wait'];
  if(!phases.includes(value.phase))return null;
  const raw=value.nativeMemory??{},result={phase:value.phase,waiting:value.phase.endsWith('-wait')&&value.operationStatus!=='complete'};
  for(const [name,bound] of [['jobs',4096],['attempt',1000000]])if(Number.isSafeInteger(raw[name])&&raw[name]>0&&raw[name]<=bound)result[name]=raw[name];
  for(const [name,bound] of [['compilerProfile',64],['reason',80]])if(typeof raw[name]==='string'&&raw[name].length<=bound&&/^[a-z][a-z0-9-]*$/.test(raw[name]))result[name]=raw[name];
  const resources={};
  for(const name of ['availableMemoryBytes','commitHeadroomBytes','processWorkingSetBytes','processPrivateCommitBytes'])if(Number.isSafeInteger(raw.resources?.[name])&&raw.resources[name]>=0)resources[name]=raw.resources[name];
  if(Object.keys(resources).length)result.resources=resources;
  return result;
}
export function stageStatus(state,stage) {
  return stage.id==='install'&&stage.status==='complete'&&(stage.details?.requested===false||state?.choices?.install===false)?'skipped':stage.status;
}
export function artworkUrl(value, origin) {
  try { const url = new URL(value, origin); return url.origin === origin && ['/api/artwork','/api/promo-artwork'].includes(url.pathname) ? url.href : null; }
  catch { return null; }
}
export function publisherSourceUrl(value) {
  // Attribution links are an explicit public-publisher action, never a
  // browser-supplied image route or arbitrary external destination.
  try { const url=new URL(value);return url.origin==='https://store.steampowered.com'&&url.pathname.startsWith('/news/')&&!url.username&&!url.password?url.href:null; }
  catch { return null; }
}
export function stageProgress(stage) {
  const value=stage?.progress??{},raw=value.stagePercent;
  // A retry queues a previously failed stage before its prerequisite receipts
  // finish. Its compatible retained work is still real; pending is a live
  // status, not a reason to discard the backend's saved whole-stage percentage.
  const percent=stage?.status==='complete'?100:
    typeof raw==='number'&&Number.isFinite(raw)&&raw>=0&&raw<=100?Math.min(99.9,raw):0;
  const phaseRaw=value.percent;
  const phasePercent=typeof phaseRaw==='number'&&Number.isFinite(phaseRaw)&&phaseRaw>=0&&phaseRaw<=100?phaseRaw:null;
  return {...value,percent,phasePercent,waiting:['running','blocked'].includes(stage?.status)?stage?.waiting??null:null};
}
export function activeWorkView(progress) {
  const work=progress?.activeWork;
  if(!work||!Number.isSafeInteger(work.done)||!Number.isSafeInteger(work.total)||work.total<1||work.done<0||work.done>work.total)return null;
  const operation=typeof work.operation==='string'&&/^[a-z0-9-]{1,64}$/.test(work.operation)?work.operation:null;
  const value={done:work.done,total:work.total,operation};
  if(Number.isSafeInteger(work.index)&&work.index>0&&work.index<=work.total&&typeof work.checkpoint==='string'&&/^[a-z0-9-]{1,80}$/.test(work.checkpoint)){
    value.index=work.index;value.checkpoint=work.checkpoint;
  }
  const passes=work.passes;
  if(passes&&Number.isSafeInteger(passes.done)&&Number.isSafeInteger(passes.total)&&passes.total>0&&passes.done>=0&&passes.done<=passes.total)
    value.passes={done:passes.done,total:passes.total,active:typeof passes.active==='string'?passes.active:null,known:passes.known===true};
  return value;
}
export function buildOverviewView(state) {
  const raw=state?.stages?.find(row=>row.id==='build')?.progress?.buildOverview;
  if(raw?.schema!==1||!Array.isArray(raw.groups)||raw.groups.length>8)return null;
  const validId=id=>typeof id==='string'&&/^[a-z0-9-]{1,80}$/.test(id);
  const counter=value=>value&&Number.isSafeInteger(value.done)&&Number.isSafeInteger(value.total)&&value.done>=0&&value.total>=0&&value.done<=value.total
    ?{done:value.done,total:value.total,unit:typeof value.unit==='string'?value.unit:null,phase:typeof value.phase==='string'?value.phase:null}:null;
  const normalize=rows=>Array.isArray(rows)&&rows.length<=32?rows.filter(row=>validId(row?.id)).map(row=>({id:row.id,
    status:['pending','running','checking','failed','retained','reused','complete'].includes(row.status)?row.status:'pending',
    closed:row.closed===true,percent:typeof row.percent==='number'&&Number.isFinite(row.percent)&&row.percent>=0&&row.percent<=100?row.percent:0,
    ...(counter(row.counter)?{counter:counter(row.counter)}:{}),...(row.conditional===true?{conditional:true}:{})})):[];
  const preparation=value=>{
    if(!value||!Array.isArray(value.checkpoints)||value.checkpoints.length>16)return null;
    const checkpoints=normalize(value.checkpoints).map(row=>{
      const source=value.checkpoints.find(item=>item.id===row.id),passes=normalize(source.passes);
      return {...row,passes,passDone:passes.filter(item=>item.closed).length,passTotal:passes.length,passPlanKnown:source.passPlanKnown===true,
        detail:source.detail==='aggregate'?'aggregate':'observed',nestedCounter:counter(source.nestedCounter)};
    });
    if(!checkpoints.length||new Set(checkpoints.map(item=>item.id)).size!==checkpoints.length)return null;
    return {checkpoints,done:checkpoints.filter(item=>item.closed).length,total:checkpoints.length,
      active:checkpoints.some(item=>item.id===value.active)?value.active:null};
  };
  const compiler=value=>{
    if(!value||!['unity-shader-compile','unity-shader-task'].includes(value.phase))return null;
    return {phase:value.phase,scope:value.scope==='task'?'task':'pass',counter:counter(value),
      percent:typeof value.percent==='number'&&Number.isFinite(value.percent)&&value.percent>=0&&value.percent<=100?value.percent:null,
      detail:typeof value.detail==='string'?value.detail.slice(0,1024):'',status:['running','failed','complete','pending'].includes(value.status)?value.status:'pending'};
  };
  const observedPasses=(value,names)=>{
    if(!value||!Array.isArray(value.passes)||value.passes.length!==names.length)return null;
    const passes=normalize(value.passes);
    if(passes.length!==names.length||new Set(passes.map(row=>row.id)).size!==names.length||passes.some(row=>!names.includes(row.id)))return null;
    return {passes,done:passes.filter(row=>row.closed).length,total:names.length,
      active:passes.some(row=>row.id===value.active)?value.active:null};
  };
  const groups=raw.groups.filter(group=>validId(group?.id)).map(group=>({...group,operations:normalize(group.operations).map(row=>{
    const source=group.operations.find(item=>item.id===row.id);
    return {...row,preparation:preparation(source.preparation),compiler:compiler(source.compiler),import:unityImportView(source.import),
      pack:observedPasses(source.pack,['native-content-source-hash','native-content-native-hash','native-content-write','native-content-final-hash']),
      api:observedPasses(source.api,['package-api-bind','package-api-output','package-api-publish'])};
  })}));
  const operations=groups.flatMap(group=>group.operations);
  if(!operations.length||operations.length>32||new Set(operations.map(row=>row.id)).size!==operations.length)return null;
  const active=operations.some(row=>row.id===raw.active)?raw.active:null;
  const recovery=raw.recovery??{},batches=recovery.batches??{};
  const validBatches=Number.isSafeInteger(batches.done)&&Number.isSafeInteger(batches.total)&&batches.total>=0&&batches.done>=0&&batches.done<=batches.total;
  const sections=normalize(recovery.sections),staging=normalize(recovery.staging);
  const stagingParent=sections.find(row=>row.id==='staging');
  const stagingLive=staging.some(row=>['running','checking','failed'].includes(row.status));
  const stagingObserved=stagingLive||staging.some(row=>row.closed||row.percent>0);
  // Older snapshots fabricated pending children after the whole recovered
  // project was reused. Keep the parent proof without inventing child proofs.
  const aggregate=stagingParent?.closed&&!stagingLive&&
    (!staging.length||staging.some(row=>!row.closed)||recovery.stagingDetail==='aggregate');
  const stagingDetail=aggregate?'aggregate':stagingObserved?'observed':'unobserved';
  return {groups,active,done:operations.filter(row=>row.closed).length,total:operations.length,
    recovery:{sections,staging:stagingDetail==='observed'?staging:[],stagingDetail,
      batches:validBatches?{done:batches.done,total:batches.total}:null,stagingCounter:recovery.stagingCounter??null}};
}
export function durationText(value) {
  // Actual elapsed/estimated durations come from the backend. Formatting a
  // clock never advances a progress bar or reconstructs missing old runtime.
  if(typeof value!=='number'||!Number.isFinite(value)||value<0)return '';
  const seconds=Math.floor(value),hours=Math.floor(seconds/3600),minutes=Math.floor(seconds/60)%60;
  return [hours,minutes,seconds%60].map(number=>String(number).padStart(2,'0')).join(':');
}
export function timingView(value) {
  if(!value||typeof value.elapsedSeconds!=='number'||!Number.isFinite(value.elapsedSeconds)||value.elapsedSeconds<0)return null;
  const raw=value.estimate??{},status=['learning','estimated','unknown','paused','complete'].includes(raw.status)?raw.status:'unknown';
  const scope=raw.scope==='conversion-batches'?'conversion-batches':'phase';
  const valid=raw.scope==='phase'||raw.scope==='conversion-batches';
  const bounds=valid&&['remainingSeconds','lowerSeconds','upperSeconds'].every(key=>typeof raw[key]==='number'&&Number.isFinite(raw[key])&&raw[key]>=0)
    &&raw.lowerSeconds<=raw.remainingSeconds&&raw.remainingSeconds<=raw.upperSeconds;
  return {elapsedSeconds:value.elapsedSeconds,active:value.active===true,elapsedBasis:value.elapsedBasis==='since-update'?'since-update':'recorded-active',
    estimate:{status:status==='estimated'&&!bounds?'unknown':status,scope,lowerSeconds:bounds?raw.lowerSeconds:null,upperSeconds:bounds?raw.upperSeconds:null}};
}
export function sessionId(value) { return typeof value === 'string' && /^[a-zA-Z0-9_-]{8,128}$/.test(value) ? value : null; }
export function savedSession(discovery) {
  // The owner workspace, rather than this browser's changing loopback origin,
  // decides which retained build to reopen after a launcher update.
  return sessionId(discovery?.latestSession) ??
    (Array.isArray(discovery?.recentSessions) ? discovery.recentSessions.map(row=>sessionId(row?.session)).find(Boolean) : null) ?? null;
}
