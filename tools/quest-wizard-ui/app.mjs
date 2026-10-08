import {translate} from './i18n.mjs';
import {choicesFromForm,progressView,macroStep,isActive,stageStatus,stageProgress,artworkUrl,sessionId,savedSession,activeWorkView,timingView,durationText,failureView,activityView,buildOverviewView} from './model.mjs';
import {LocalApi,PreviewApi} from './transport.mjs';

const $ = id => document.getElementById(id);
const preview = new URLSearchParams(location.search).get('preview') === '1';
const fragment = location.hash.slice(1), token = fragment.startsWith('token=') ? new URLSearchParams(fragment).get('token') : fragment;
const api = preview ? new PreviewApi() : new LocalApi(location.origin,token);
let language = (preview ? new URLSearchParams(location.search).get('lang') : null) ?? readStorage('quest-wizard-language') ?? (navigator.language.startsWith('de') ? 'de' : 'en');
if (!['de','en'].includes(language)) language='de';
let page=0,selected=null,discovery=null,state=null,busy=false,after=0,log=[],loadedLog=null,pollTimer=null,canConnect=preview||Boolean(token),artworkKey='',artworkGeneration=0;
let qualification=null,resumeFailed=null,restoredSession=null,sessionGeneration=0;
let eventsUnavailable=false,stageLogUnavailable=false,lastFailureFocus='',eventsRequest=null,stageLogRequest=null;
let buildFocus=false;const buildGroupChoices=new Map();
let publisherArtwork=[],ownedArtwork=[],artworkSource='';
const reducedMotion=matchMedia('(prefers-reduced-motion:reduce)');
let slides=[],slideIndex=0,slideTimer=null,slidePaused=reducedMotion.matches,artPollTimer=null,actionBusy=false,logSelectionManual=false;
const blobUrls=[];
const galleryTemplate=[...$('gallery').children].map(node=>node.cloneNode(true));
function readStorage(key) { try { return localStorage.getItem(key); } catch { return null; } }
function writeStorage(key,value) { try { localStorage.setItem(key,value); } catch { } }
function t(key,parameters) { return translate(language,key,parameters); }
function percentText(value) { return new Intl.NumberFormat(language,{maximumFractionDigits:2}).format(value); }
function message(value,fallback='unknownAction') { return typeof value==='string' ? value : value?.[language] ?? value?.en ?? value?.de ?? t(fallback); }
function error(value) { $('error-banner').textContent=value?.message ? message(value.message) : t(value?.code ?? 'offline');$('error-banner').hidden=false; }
function clearError() { $('error-banner').hidden=true; }
function clearLoadedLog() {loadedLog=null;logSelectionManual=false;$('stage-log').textContent='';$('stage-log').hidden=true;$('log-truncated').hidden=true;}
function setLanguage(value) {
  language=value;writeStorage('quest-wizard-language',value);document.documentElement.lang=value;
  document.querySelectorAll('[data-i18n]').forEach(node => node.textContent=t(node.dataset.i18n));
  document.querySelectorAll('[data-i18n-aria]').forEach(node=>node.setAttribute('aria-label',t(node.dataset.i18nAria)));
  document.querySelectorAll('[data-i18n-title]').forEach(node=>node.title=t(node.dataset.i18nTitle));
  document.querySelectorAll('[data-language]').forEach(node => node.setAttribute('aria-pressed',String(node.dataset.language===value)));
  $('gallery').querySelectorAll('.art-card img').forEach((node,index)=>node.alt=t(slides[index]?.altCode??'ownedArtwork'));
  $('gallery-pause').textContent=t(slidePaused?'galleryPlay':'galleryPause');
  showSlide(slideIndex);
  if(discovery) renderGames();updateView();
}
function form() { return {gameRoot:$('game-root').value,provider:$('provider').value,unityEditor:$('unity-editor').value,
  mode:$('build-mode').value,baseApk:$('base-apk').value,signingRoot:$('signing-root').value,
  profileName:$('profile-name').value,profileId:$('profile-id').value,declareDlc:$('declare-dlc').checked,
  ownedDlc:[...document.querySelectorAll('input[name=dlc]:checked')].map(node => node.value),
  acceptUnityTerms:$('unity-terms').checked,install:$('install-choice').checked}; }
function selectGame(game) {
  if(selected?.id!==game.id)resetArtwork();
  selected=game;$('game-root').value=game.gameRoot;$('provider').value=game.provider;
  renderGames();updateView();loadArtwork(game.artwork);
}
function resetArtwork() {
  ownedArtwork=[];
  // Selecting a copy without recovered artwork must not erase the publisher
  // slideshow or invalidate an in-flight publisher request.
  if(artworkSource==='publisher'&&artworkKey)return;
  artworkGeneration++;artworkKey='';
  if(publisherArtwork.length){loadArtwork(publisherArtwork,{publisher:true});return;}
  blobUrls.splice(0).forEach(url=>URL.revokeObjectURL(url));
  $('gallery').replaceChildren(...galleryTemplate.map(node=>node.cloneNode(true)));
  $('gallery').querySelectorAll('[data-i18n]').forEach(node=>node.textContent=t(node.dataset.i18n));
  $('gallery-note').textContent=t(preview?'artPlaceholder':'artReady');
  slides=[];slideIndex=0;clearTimeout(slideTimer);$('gallery-controls').hidden=true;
}
function showSlide(index) {
  if(!slides.length)return;slideIndex=(index+slides.length)%slides.length;
  [...$('gallery').children].forEach((node,i)=>{node.hidden=i!==slideIndex;});
  $('gallery-count').textContent=(slideIndex+1)+' / '+slides.length;
  const row=slides[slideIndex],caption=row.caption?.[language]??row.caption?.en;
  $('gallery-note').textContent=[caption,t(row.altCode??'ownedArtwork')].filter(Boolean).join(' · ');
  clearTimeout(slideTimer);if(!slidePaused&&!document.hidden)slideTimer=setTimeout(()=>showSlide(slideIndex+1),6500);
}
function phaseLabel(phase='') {
  if(phase.startsWith('operation:')){
    const code=phase.slice(10),key='phase_operation_'+code,value=t(key);
    const tool=code.match(/^(download|extract|verify)-(git|dotnet8|dotnet10|apkJdk|apkBuildTools)$/);
    if(tool)return t(tool[1]==='verify'?'phase_outputVerify':'phase_'+tool[1])+' · '+({git:'Git',dotnet8:'.NET 8',dotnet10:'.NET 10',apkJdk:'Java 17',apkBuildTools:'Android Build Tools'}[tool[2]]);
    return value===key?t('phase_starting'):value;
  }
  const unityKey='phase_'+phase;
  if(t(unityKey)!==unityKey)return t(unityKey);
  const exact={'pending':'pending','starting':'starting','complete':'complete','receipt-verify':'receiptVerify','output-verify':'outputVerify','source-copy':'sourceCopy','unity-prerequisites':'unityPrerequisites','unity-hub-download':'unityDownload','unity-editor-install':'unityInstall','unity-window-open':'unityWindow','unity-editor-version':'unityVersion','unity-license-probe':'unityLicense'};
  const key=exact[phase]??(phase.startsWith('tool-download-')?'download':phase.startsWith('tool-extract-')?'extract':phase.startsWith('bee-actions:')?'native':phase.startsWith('stage:')?null:phase.includes('hash')?(phase.includes('game')?'gameHash':'sourceHash'):phase.includes('snapshot')?'snapshot':phase.includes('recover')||phase.includes('export')?'recovery':phase.includes('dependenc')||phase.includes('python')?'dependencies':null);
  if(phase.startsWith('stage:'))return t('phase_stage_'+phase.slice(6));
  if(phase.startsWith('stage-')&&phase.includes('verify:'))return t('phase_outputVerify')+' · '+t('phase_stage_'+phase.split(':').pop());
  if(phase.startsWith('tool:'))return t('phase_tool')+' · '+phase.slice(5);
  return key?t('phase_'+key):phase;
}
function counters(value) {
  if(!Number.isFinite(value.done)||!Number.isFinite(value.total)||value.total<=0)return '';
  const number=n=>new Intl.NumberFormat(language,{maximumFractionDigits:1}).format(n);
  if(value.unit==='bytes')return t('counterBytes',{done:number(value.done/1048576)+' MiB',total:number(value.total/1048576)+' MiB'});
  const key={files:'counterFiles',actions:'counterActions',checks:'counterChecks',batches:'counterBatches',steps:'counterSteps',checkpoints:'counterSteps',variants:'counterVariants',objects:'counterObjects'}[value.unit]??'counterUnits';
  return t(key,{done:number(value.done),total:number(value.total)});
}
function substepLabel(value) {
  const batch=value.recoveryBatchIndex,total=value.recoveryBatchTotal;
  const context=Number.isSafeInteger(batch)&&Number.isSafeInteger(total)&&batch>0&&batch<=total
    ?t('recoveryBatchContext',{index:batch,total}):'';
  const native=value.recoveryNativeIndex,nativeTotal=value.recoveryNativeTotal;
  const nativeContext=Number.isSafeInteger(native)&&Number.isSafeInteger(nativeTotal)&&native>0&&native<=nativeTotal
    ?t('recoveryNativeContext',{index:native,total:nativeTotal}):'';
  return t('substep',{phase:[phaseLabel(value.phase),context,nativeContext].filter(Boolean).join(' · ')});
}
function workSummary(value) {
  const work=activeWorkView(value),operation=work?.operation;
  if(!work)return t('activeWorkUnknown');
  const label=operation?phaseLabel('operation:'+operation):t('activeWorkCurrent');
  return t('activeWorkCompleted',{operation:label,done:new Intl.NumberFormat(language).format(work.done),total:new Intl.NumberFormat(language).format(work.total)});
}
function renderTiming(current) {
  const whole=timingView(state?.timing),stage=timingView(current?.timing),estimate=stage?.estimate;
  $('build-timing').hidden=!whole&&!stage;
  $('elapsed-label').textContent=t(whole?.elapsedBasis==='since-update'?'elapsedSinceUpdate':'elapsedBuild');
  $('elapsed-value').textContent=whole?durationText(whole.elapsedSeconds):t('timingUnavailable');
  $('stage-elapsed').hidden=!stage;
  $('stage-elapsed').textContent=stage?t(stage.elapsedBasis==='since-update'?'elapsedStageSinceUpdate':'elapsedStage',{duration:durationText(stage.elapsedSeconds)}):'';
  $('eta-label').textContent=t(estimate?.scope==='conversion-batches'?'etaBatches':'etaPhase');
  let text;
  if(state?.status==='complete')text=t('etaComplete');
  else if(estimate?.status==='estimated')text=t('etaRange',{lower:durationText(estimate.lowerSeconds),upper:durationText(estimate.upperSeconds)});
  else text=t(estimate?.status==='complete'?'etaComplete':estimate?.status==='paused'?'etaPaused':estimate?.status==='learning'?'etaLearning':'etaUnknown');
  $('eta-value').textContent=text;
  $('eta-value').title=t('etaScopeHint');
}
function overviewList(rows,prefix) {
  const list=document.createElement('ol');list.className='build-operation-list';
  for(const row of rows){
    const item=document.createElement('li');item.className=row.status;item.dataset.operation=row.id;
    const name=document.createElement('span');name.textContent=t(prefix+row.id);
    const status=document.createElement('small');status.textContent=t('overview_'+row.status)+(row.closed?'':row.percent>0?' · '+t('measuredPercent',{percent:percentText(row.percent)}):'');
    item.append(name,status);list.append(item);
  }
  return list;
}
function renderBuildOverview() {
  const overview=buildOverviewView(state);$('build-overview').hidden=!overview;
  const focus=Boolean(overview&&state.stages?.find(row=>row.id==='build')?.status!=='pending');
  if(focus!==buildFocus){$('setup-overview').open=!focus;buildFocus=focus;}
  if(!overview)return;
  const openPlans=new Set([...$('build-groups').querySelectorAll('details[data-plan][open]')].map(node=>node.dataset.plan));
  $('build-groups').replaceChildren();
  $('build-overview-count').textContent=t('buildOverviewCount',{done:overview.done,total:overview.total,remaining:overview.total-overview.done});
  $('build-current').textContent=t('buildCurrent',{operation:overview.active?phaseLabel('operation:'+overview.active):t('waiting')});
  const retained=overview.groups.some(group=>group.operations.some(row=>['retained','checking'].includes(row.status)));
  $('build-retained').hidden=!retained;
  for(const group of overview.groups){
    const active=group.operations.some(row=>row.id===overview.active),done=group.operations.filter(row=>row.closed).length;
    const card=document.createElement('details');card.className='build-group'+(active?' active':'');card.dataset.group=group.id;
    card.open=buildGroupChoices.has(group.id)?buildGroupChoices.get(group.id):active;
    const heading=document.createElement('summary');
    const name=document.createElement('strong');name.textContent=t('buildGroup_'+group.id);
    const status=document.createElement('span');status.textContent=[t(active?'overviewCurrent':done===group.operations.length?'overviewSaved':'overviewRemaining'),done+' / '+group.operations.length].join(' · ');heading.append(name,status);heading.addEventListener('click',()=>buildGroupChoices.set(group.id,!card.open));card.append(heading);
    card.append(overviewList(group.operations,'phase_operation_'));
    if(group.id==='recovery'){
      const data=overview.recovery;
      if(data.batches){const packages=document.createElement('p');packages.className='hint';packages.textContent=t('buildBatchSummary',data.batches);card.append(packages);}
      for(const [kind,rows,prefix] of [['sections',data.sections,'recoveryOverview_'],['staging',data.staging,'phase_staging-section:']]){
        if(!rows.length)continue;
        const nested=document.createElement('details');nested.className='build-subplan';nested.dataset.plan=kind;nested.open=openPlans.has(kind);
        const label=document.createElement('summary');label.textContent=t(kind==='sections'?'buildRecoverySummary':'buildStagingSummary',{done:rows.filter(row=>row.closed).length,total:rows.length});nested.append(label,overviewList(rows,prefix));card.append(nested);
        const current=rows.find(row=>['running','checking','failed'].includes(row.status));
        if(current){const line=document.createElement('p');line.className='hint';line.textContent=t('buildCurrent',{operation:t(prefix+current.id)});card.append(line);}
      }
      const outer=data.stagingCounter;
      if(outer&&Number.isFinite(outer.done)&&Number.isFinite(outer.total)){const line=document.createElement('p');line.className='hint';line.textContent=counters(outer);card.append(line);}
    }
    $('build-groups').append(card);
  }
}
function renderGames() {
  $('game-list').replaceChildren();
  const games=Array.isArray(discovery?.games) ? discovery.games : [];
  $('discovery-status').textContent=t(games.length===1?'foundOne':games.length?'foundMany':'notFound',{count:games.length});
  for(const game of games) {
    const button=document.createElement('button');button.type='button';button.className='game-option'+(selected?.id===game.id?' selected':'');
    button.setAttribute('aria-pressed',String(selected?.id===game.id));
    const icon=document.createElement('span');icon.className='game-icon';icon.textContent='◇';icon.setAttribute('aria-hidden','true');
    const info=document.createElement('span');info.className='game-information';
    const title=document.createElement('strong');title.textContent=game.displayName||'Gloomhaven';
    const path=document.createElement('small');path.textContent=game.provider.toUpperCase()+' · '+game.gameRoot;
    const badges=document.createElement('div');badges.className='game-badges';
    badges.textContent=game.ownershipComplete ? (game.ownedDlc?.length ? game.ownedDlc.map(value => ({jotl:'Jaws of the Lion',solo:'Solo Scenarios','jotl-skins':t('skins')})[value]??'').filter(Boolean).join(' · ') : t('baseGame')) : t('ownershipUnknown');
    const marker=document.createElement('span');marker.className='game-selection';marker.textContent=selected?.id===game.id?'✓':'';marker.setAttribute('aria-hidden','true');
    info.append(title,path,badges);button.append(icon,info,marker);button.addEventListener('click',()=>selectGame(game));$('game-list').append(button);
  }
  $('browse-game').hidden=discovery?.capabilities?.browse===false;$('browse-unity').hidden=discovery?.capabilities?.browse===false;
  const resume=savedSession(discovery);
  if(resume&&!state) {
    const button=document.createElement('button');button.className='secondary';button.id='resume-session';button.textContent=t(resumeFailed?'resumeRetry':'resume');button.disabled=busy;
    button.addEventListener('click',()=>openSession(resume));$('game-list').append(button);
  }
}
function updateView() {
  const previous=state?.modSource,current=discovery?.modSource;
  const sourceUpdate=state&&page===2&&!isActive(state)&&state.status!=='complete'&&
    !state.choices?.sourceRoot&&!state.choices?.sourceCommit&&!state.choices?.sourceRef&&
    previous?.sourceCommit&&current?.sourceCommit&&previous.sourceCommit!==current.sourceCommit;
  const source=sourceUpdate?current:previous??current;
  const version=typeof source?.modVersion==='string'?source.modVersion:'';
  const build=Number.isInteger(source?.modBuild)?'B'+source.modBuild:'';
  const commit=typeof source?.sourceCommit==='string'&&/^[a-f0-9]{40}$/i.test(source.sourceCommit)?source.sourceCommit.slice(0,10):'';
  $('mod-source').hidden=!source||![version,build,commit].some(Boolean);
  $('mod-source').textContent=[version?'GloomhavenVR '+version:'GloomhavenVR',build,commit,t(source?.kind==='bundled-release'?'modSourceRelease':'modSourceCheckout')].filter(Boolean).join(' · ');
  $('mod-source').title=t('modSourceHint');
  $('source-update').hidden=!sourceUpdate;
  $('source-update').textContent=sourceUpdate?t('sourceUpdate',{commit:previous.sourceCommit.slice(0,10)}):'';
  $('resume-notice').hidden=!resumeFailed&&!(page===2&&restoredSession===state?.session);
  $('resume-notice').textContent=t(resumeFailed?'resumeUnavailable':state?.status==='complete'?'resumeCompleted':isActive(state)?'resumeActive':'resumeRestored');
  const estimate=qualification?.spaceEstimate;
  $('space-estimate').hidden=!estimate;
  if(estimate){$('space-estimate').textContent=t(estimate.scanBounded?'spacePartial':qualification.spaceWarning?'spaceLow':'spaceEstimate',{required:Math.ceil(estimate.additionalEstimatedBytes/1073741824),free:Math.floor(qualification.freeBytes/1073741824)});$('space-estimate').classList.toggle('warning',qualification.spaceWarning===true);}
  $('game-page').hidden=page!==0;$('setup-page').hidden=page!==1;$('progress-page').hidden=page!==2;
  const step=page===2 ? macroStep(state) : page;
  document.querySelectorAll('[data-step]').forEach(node => {const index=Number(node.dataset.step);node.classList.toggle('active',index===step);node.classList.toggle('done',index<step);if(index===step)node.setAttribute('aria-current','step');else node.removeAttribute('aria-current');});
  $('back').hidden=page!==1;$('cancel').hidden=page!==2||!isActive(state);
  $('new-build').hidden=!(resumeFailed||page===2&&state&&!isActive(state));$('new-build').disabled=busy;$('new-build').title=t('newBuildHint');
  $('cancel').disabled=busy||['cancelling','cancel_requested'].includes(state?.status);
  $('cancel').textContent=t(['cancelling','cancel_requested'].includes(state?.status)?'cancelling':'cancel');
  $('profile-summary').textContent=t($('profile-name').value?'profileManual':'profileCheck');
  $('selected-review').textContent=$('provider').value.toUpperCase()+' · '+$('game-root').value;
  const mode=$('build-mode').value;
  $('base-apk-choice').hidden=mode==='build';
  $('update-signing-choice').hidden=mode==='build';
  document.querySelector('.license-note').hidden=mode==='update-profile';
  $('browse-apk').hidden=discovery?.capabilities?.browse===false;
  $('primary').disabled=busy||!canConnect||Boolean(resumeFailed)||(page===0&&!$('game-root').value.trim())||(page===2&&(isActive(state)||state?.status==='complete'));
  $('primary').firstElementChild.textContent=t(busy?'preparing':page===0?'continue':page===1?'build':isActive(state)?'working':state?.status==='complete'?'done':state?.status==='blocked'?'retry':'resumeContinue');
  $('primary').lastElementChild.textContent=state?.status==='complete'?'✓':'→';
  if(page===1&&mode!=='build'&&!busy)$('primary').firstElementChild.textContent=t('startUpdate');
  if(page===2)renderProgress();
}
function renderProgress() {
  if(!state)return;
  const active=isActive(state),stopped=['failed','cancelled','interrupted'].includes(state.status),blocked=state.status==='blocked',done=state.status==='complete';
  if(state.requestError)error(state.requestError);
  const failure=failureView(state);
  // A retained prerequisite record must never replace a confirmed terminal
  // failure with a misleading "waiting for user" presentation.
  const waiting=(active||blocked)?state.stages?.find(row=>row.waiting)?.waiting:null;
  $('action-needed-title').textContent=t(waiting||blocked?'actionNeeded':'failureTitle');
  $('progress-title').textContent=t(failure?'failureTitle':waiting?'actionNeeded':done?'completeTitle':blocked?'blockedTitle':stopped?'stoppedTitle':state.status==='ready'?'readyTitle':'progressTitle');
  $('progress-copy').textContent=t(failure?'failureCopy':done?'completeCopy':blocked?'blockedCopy':stopped?'stoppedCopy':state.status==='ready'?'readyCopy':'progressCopy');
  const progress=progressView(state);
  if(!logSelectionManual&&(progress.active??progress.current)?.id)$('log-stage').value=(progress.active??progress.current).id;
  $('phase-label').textContent=t(done?'done':t('stage_'+progress.phase)==='stage_'+progress.phase?'waiting':'stage_'+progress.phase);
  $('progress-count').textContent=t('phasePercent',{percent:percentText(progress.percent)});
  const track=$('progress-track');track.classList.toggle('indeterminate',progress.indeterminate);
  track.setAttribute('aria-valuemin','0');track.setAttribute('aria-valuemax','100');
  track.setAttribute('aria-valuenow',String(progress.percent));
  track.setAttribute('aria-valuetext',$('progress-count').textContent);$('progress-fill').style.width=progress.width+'%';
  const sub=stageProgress(progress.active??progress.current);
  const hasSubstep=!done&&sub.phase&&!['pending','starting','complete'].includes(sub.phase);
  $('progress-detail').textContent=[progress.retaining?t('retainedProgress'):'',!hasSubstep&&sub.phase?substepLabel(sub):'',counters(sub),sub.detail??'',waiting?t('waitingSince',{seconds:Math.max(0,Math.floor(Date.now()/1000-waiting.since))}):''].filter(Boolean).join(' · ');
  $('progress-completed').hidden=done;
  $('progress-completed').textContent=workSummary(sub);
  renderTiming(progress.current);
  const activity=activityView(progress.active??progress.current);
  $('activity-status').hidden=!active||Boolean(waiting);
  $('activity-status').classList.toggle('quiet',activity?.quiet===true);
  $('activity-status').textContent=t(activity?.quiet?'activityQuiet':activity?'activityRecent':'activityUnknown',{duration:durationText(activity?.seconds??0)});
  $('log-status').hidden=!eventsUnavailable&&!stageLogUnavailable;$('log-status').textContent=t('logUnavailable');
  renderBuildOverview();
  $('substep-progress').hidden=!hasSubstep;
  $('substep-label').textContent=substepLabel(sub);
  $('substep-count').textContent=sub.phasePercent===null?t('phaseUnknown'):t('phasePercent',{percent:percentText(sub.phasePercent)});
  const subtrack=$('substep-track');subtrack.classList.toggle('indeterminate',sub.phasePercent===null&&active&&!waiting);
  subtrack.setAttribute('aria-valuemin','0');subtrack.setAttribute('aria-valuemax','100');
  if(sub.phasePercent===null)subtrack.removeAttribute('aria-valuenow');else subtrack.setAttribute('aria-valuenow',String(sub.phasePercent));
  subtrack.setAttribute('aria-valuetext',$('substep-count').textContent);$('substep-fill').style.width=(sub.phasePercent??0)+'%';
  $('stage-list').replaceChildren();
  for(const stage of state.stages??[]) {
    const displayed=stageStatus(state,stage),li=document.createElement('li');li.className=displayed;
    const marker=document.createElement('span');marker.className='stage-marker';marker.textContent=displayed==='complete'?'✓':displayed==='running'?'·':['blocked','failed'].includes(displayed)?'!':'';
    const label=document.createElement('span');label.className='stage-content';const title=document.createElement('strong');title.textContent=t('stage_'+stage.id);label.append(title);
    const measured=stageProgress(stage),detail=document.createElement('small');detail.textContent=[measured.phase?substepLabel(measured):'',['pending','complete'].includes(stage.status)?'':counters(measured),measured.waiting?t('actionNeeded'):['pending','complete'].includes(stage.status)?'':measured.detail??''].filter(Boolean).join(' · ');label.append(detail);
    const bar=document.createElement('progress');bar.max=100;bar.setAttribute('aria-label',t('stage_'+stage.id)+' · '+t('stageTotal'));bar.value=measured.percent;label.append(bar);
    const status=document.createElement('span');status.className='stage-state';status.textContent=t('measuredPercent',{percent:percentText(measured.percent)})+' · '+t(measured.waiting?'blocked':displayed);li.append(marker,label,status);$('stage-list').append(li);
  }
  const action=active&&!waiting?null:state.needsActions?.[0];$('action-needed').hidden=!action&&!blocked&&!stopped;
  $('action-needed').classList.toggle('failure',Boolean(failure));
  $('action-needed').setAttribute('role',failure?'alert':'status');
  $('failure-cause').hidden=!failure?.cause;
  $('failure-cause').textContent=failure?.cause?t('failureCause',{cause:failure.cause}):'';
  $('failure-next').hidden=!failure;$('failure-actions').hidden=!failure;
  $('failure-retry').disabled=busy||!canConnect||Boolean(resumeFailed);
  $('failure-support').hidden=preview||discovery?.capabilities?.support!==true;
  $('failure-support').disabled=!sessionId(state.session);
  if(action||blocked||stopped){const blockedStage=state.stages?.find(stage=>stage.status==='blocked');const code=typeof action==='string'?action:action?.code??blockedStage?.details?.needsAction;
    const hint=action?.message?message(action.message):blockedStage?.details?.message?message(blockedStage.details.message):t('action_'+code)!=='action_'+code?t('action_'+code):t('unknownAction');
    const phase=action?.parameters?.failureStage;const stageName=phase?t('phase_stage_'+phase):action?.stage?t('stage_'+action.stage):progress.current?t('stage_'+progress.current.id):'';
    $('action-needed-copy').textContent=(stageName?stageName+': ':'')+hint;}
  const failureKey=failure?state.session+':'+failure.stage+':'+failure.cause:'';
  if(failureKey&&failureKey!==lastFailureFocus){$('action-needed').focus({preventScroll:true});$('action-needed').scrollIntoView({block:'nearest'});}
  lastFailureFocus=failureKey;
  $('unity-actions').hidden=!waiting;$('edit-choices').hidden=active;
  for(const id of ['unity-open','unity-check'])$(id).disabled=actionBusy;
  $('result-card').hidden=!done;
  const result=state.result??state.stages?.find(stage=>stage.id==='build')?.details??{};
  $('result-path').textContent=[result.apk,result.handoff,result.outputRoot].filter(value=>typeof value==='string').join('\n')||t('resultAvailable');
  $('session-detail').textContent=t('session',{session:state.session??''});$('event-log').textContent=log.slice(-50).join('\n');
  $('save-support').hidden=preview||discovery?.capabilities?.support!==true;$('save-support').disabled=!sessionId(state?.session);
  $('stage-log-controls').hidden=!preview&&discovery?.capabilities?.logs!==true;
  loadArtwork(state.artwork);
}
async function loadArtwork(artwork,{publisher=false}={}) {
  if(!Array.isArray(artwork)||!artwork.length)return;
  if(publisher){publisherArtwork=artwork;if(ownedArtwork.length)return;}
  else ownedArtwork=artwork;
  const selected=artwork.slice(0,32),key=selected.map(row=>row.id+':'+(row.sha256??'')+':'+row.url).join('|');if(key===artworkKey)return;
  artworkKey=key;const generation=++artworkGeneration;
  artworkSource=publisher?'publisher':'owned';
  const cards=[],newUrls=[],loadedRows=[];
  for(const row of selected) {
    const url=artworkUrl(row.url,location.origin);if(!url)continue;
    try {
      const response=await fetch(url,{headers:{'X-Quest-Token':token},cache:'no-store'});
      if(!response.ok||Number(response.headers.get('Content-Length'))>8*1024*1024||!['image/png','image/jpeg'].includes(response.headers.get('Content-Type')))continue;
      const blob=await response.blob();if(blob.size>8*1024*1024)continue;
      const blobUrl=URL.createObjectURL(blob);newUrls.push(blobUrl);
      const card=document.createElement('div');card.className='art-card';const image=document.createElement('img');image.alt=t(row.altCode??'ownedArtwork');
      image.addEventListener('load',()=>card.classList.toggle('landscape',image.naturalWidth/image.naturalHeight>=1.45),{once:true});
      image.src=blobUrl;card.append(image);cards.push(card);loadedRows.push(row);
    }catch{ /* Optional artwork never blocks a genuine build state. */ }
  }
  if(generation!==artworkGeneration){newUrls.forEach(url=>URL.revokeObjectURL(url));return;}
  if(cards.length){blobUrls.splice(0).forEach(url=>URL.revokeObjectURL(url));blobUrls.push(...newUrls);$('gallery').replaceChildren(...cards);slides=loadedRows;$('gallery-controls').hidden=cards.length<2;$('gallery-note').textContent=t(slides[0].altCode??'ownedArtwork');showSlide(0);}
  else{newUrls.forEach(url=>URL.revokeObjectURL(url));artworkKey='';if(!publisher){ownedArtwork=[];loadArtwork(publisherArtwork,{publisher:true});}}
}
function restoreChoices(choices) {
  if(!choices)return;
  $('build-mode').value=choices.mode??'build';$('base-apk').value=choices.baseApk??'';
  $('signing-root').value=choices.signingRoot??'';
  $('game-root').value=choices.gameRoot??'';$('provider').value=choices.provider??'steam';$('unity-editor').value=choices.unityEditor??'';
  $('profile-name').value=choices.profile?.displayName??'';$('profile-id').value=choices.profile?.providerId??'';
  $('unity-terms').checked=choices.acceptUnityTerms===true;$('install-choice').checked=choices.install!==false;
  $('declare-dlc').checked=Array.isArray(choices.ownedDlc);$('dlc-declaration').hidden=!$('declare-dlc').checked;
  document.querySelectorAll('input[name=dlc]').forEach(node=>node.checked=choices.ownedDlc?.includes(node.value)??false);
  selected=discovery?.games?.find(game=>game.gameRoot===choices.gameRoot&&game.provider===choices.provider)??null;
}
async function openSession(session) {
  if(!sessionId(session)||busy)return false;clearError();busy=true;clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try {const result=await api.status(session);if(!result.state||result.state.session!==session)throw {code:'invalidReply'};
    resetArtwork();clearLoadedLog();eventsUnavailable=false;stageLogUnavailable=false;state=result.state;resumeFailed=null;restoredSession=session;restoreChoices(state.choices);page=2;after=0;log=[];schedulePoll();return true;}
  catch(value){resumeFailed=session;error(value);return false;}finally{busy=false;renderGames();updateView();}
}
function schedulePoll() {clearTimeout(pollTimer);if(state?.session)pollTimer=setTimeout(poll,1000);}
async function poll() {
  clearTimeout(pollTimer);
  if(!state?.session)return;
  const session=state.session,generation=sessionGeneration;
  try {
    const result=await api.status(session);if(generation!==sessionGeneration||state?.session!==session)return;
    if(!result.state||result.state.session!==session)throw {code:'invalidReply'};
    // Status is authoritative. A failed/slow log endpoint used to discard a
    // successful status response and leave the screen claiming RUNNING.
    state=result.state;clearError();updateView();
    // Optional log connections may never finish. They own one cancellable
    // request each and cannot postpone the next authoritative status poll or
    // keep a primary action busy. No timeout is imposed on Unity/user actions.
    void refreshEvents();
    if($('live-log').checked&&document.querySelector('#diagnostic-details').open)void refreshLog();
  }catch(value){if(generation===sessionGeneration)error(value);}
  if(generation===sessionGeneration&&state?.session===session&&isActive(state))schedulePoll();
}
function stopOptionalReads() {
  eventsRequest?.controller.abort();stageLogRequest?.controller.abort();
  eventsRequest=null;stageLogRequest=null;$('load-log').disabled=false;
}
function ownsRead(request,current) {
  return request===current&&!request.controller.signal.aborted&&request.generation===sessionGeneration&&request.session===state?.session;
}
async function refreshEvents() {
  if(eventsRequest||!state?.session)return;
  const request={session:state.session,generation:sessionGeneration,controller:new AbortController()};eventsRequest=request;
  try {
    const events=await api.events(request.session,after,request.controller.signal);if(!ownsRead(request,eventsRequest))return;
    for(const event of events.events??[])if(Number(event.sequence)>after) {log.push([event.time,event.stage,event.code,event.message?message(event.message):event.parameters?.message?message(event.parameters.message):event.parameters?.cause??event.parameters?.detail??''].filter(Boolean).join(' · '));after=Math.max(after,Number(event.sequence));}
    log=log.slice(-50);eventsUnavailable=false;
  }catch{if(ownsRead(request,eventsRequest))eventsUnavailable=true;}
  finally{if(ownsRead(request,eventsRequest)){eventsRequest=null;updateView();}}
}
async function refreshLog() {
  if(stageLogRequest||!state?.session)return;
  const request={session:state.session,stage:$('log-stage').value,generation:sessionGeneration,controller:new AbortController()};stageLogRequest=request;
  $('load-log').disabled=true;
  try{const result=await api.log(request.session,request.stage,request.controller.signal);if(!ownsRead(request,stageLogRequest)||request.stage!==$('log-stage').value)return;if(typeof result.text!=='string'||result.text.length>65536)throw {code:'invalidReply'};loadedLog=result;$('stage-log').textContent=result.text;$('stage-log').hidden=false;$('log-truncated').hidden=!result.truncated;stageLogUnavailable=false;}
  catch{if(ownsRead(request,stageLogRequest))stageLogUnavailable=true;}
  finally{if(ownsRead(request,stageLogRequest)){stageLogRequest=null;$('load-log').disabled=false;updateView();}}
}
async function unityAction(action) {
  const waiting=state?.stages?.find(row=>row.waiting)?.waiting;if(!waiting||actionBusy)return;
  actionBusy=true;clearError();updateView();
  try{await api.action(state.session,action,waiting.nonce);await poll();}catch(value){error(value);}finally{actionBusy=false;updateView();schedulePoll();}
}
for(const id of ['unity-open','unity-check'])$(id).addEventListener('click',()=>unityAction(id));
$('gallery-previous').addEventListener('click',()=>showSlide(slideIndex-1));$('gallery-next').addEventListener('click',()=>showSlide(slideIndex+1));
$('gallery-pause').addEventListener('click',()=>{slidePaused=!slidePaused;$('gallery-pause').textContent=t(slidePaused?'galleryPlay':'galleryPause');showSlide(slideIndex);});
document.addEventListener('visibilitychange',()=>showSlide(slideIndex));
reducedMotion.addEventListener('change',event=>{if(event.matches){slidePaused=true;$('gallery-pause').textContent=t('galleryPlay');showSlide(slideIndex);}});
async function pollGallery(){try{const result=await api.gallery();await loadArtwork(result.artwork,{publisher:true});}catch{}if(!preview)artPollTimer=setTimeout(pollGallery,5000);}
async function primary() {
  if(busy||resumeFailed)return;
  clearError();
  if(page===0){busy=true;updateView();try{const selectedChoices=choicesFromForm(form(),language);
    if(discovery?.capabilities?.spaceEstimate===true){qualification=await api.qualify(selectedChoices.gameRoot,selectedChoices.mode);}
    page=1;updateView();const heading=document.querySelector('#setup-page h2');heading.setAttribute('tabindex','-1');heading.focus();}catch(value){error(typeof value?.message==='string'?{code:value.message}:value);}finally{busy=false;updateView();}return;}
  busy=true;clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try {
    if(page===1){const choices=choicesFromForm(form(),language);if(choices.mode!=='update-profile'&&!choices.acceptUnityTerms)throw {code:'missingTerms'};
      const result=await api.plan(choices,state?.session);if(!sessionId(result.session)||!result.state)throw {code:'invalidReply'};clearLoadedLog();state=result.state;page=2;after=0;log=[];}
    await api.run(state.session);const result=await api.status(state.session);state=result.state;await poll();schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
async function browse(kind) {
  busy=true;clearError();updateView();try{const result=await api.browse(kind);if(result.path){$(({game:'game-root',unity:'unity-editor',apk:'base-apk'})[kind]).value=result.path;if(kind==='game')selected=null;}}
  catch(value){error(value);}finally{busy=false;updateView();}
}
document.querySelectorAll('[data-language]').forEach(node=>node.addEventListener('click',()=>setLanguage(node.dataset.language)));
$('primary').addEventListener('click',primary);$('back').addEventListener('click',()=>{page=0;clearError();updateView();});
$('new-build').addEventListener('click',()=>{
  if(busy||isActive(state))return;
  // This is an explicit opt-out. Removing a UI selection never deletes the
  // backend session, completed receipts or owner recovery/download workspace.
  clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();state=null;resumeFailed=null;restoredSession=null;qualification=null;page=0;after=0;log=[];
  restoreChoices({install:true});resetArtwork();clearLoadedLog();clearError();renderGames();
  if(discovery?.games?.length===1)selectGame(discovery.games[0]);
  updateView();$('game-root').focus();
});
$('edit-choices').addEventListener('click',()=>{if(isActive(state))return;clearTimeout(pollTimer);restoreChoices(state?.choices);page=0;clearError();updateView();$('game-root').focus();});
async function saveSupport() {
  if(!sessionId(state?.session))return;
  $('save-support').disabled=true;$('failure-support').disabled=true;
  try {const result=await api.support(state.session);const url=URL.createObjectURL(result.blob);
    const link=document.createElement('a');link.href=url;link.download=result.name;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  }catch(value){error(value);}finally{$('save-support').disabled=false;$('failure-support').disabled=false;}
}
$('save-support').addEventListener('click',saveSupport);
$('failure-support').addEventListener('click',saveSupport);
$('failure-retry').addEventListener('click',primary);
$('log-stage').addEventListener('change',()=>{logSelectionManual=true;if($('live-log').checked)refreshLog();});
document.querySelector('#diagnostic-details').addEventListener('toggle',()=>{if(document.querySelector('#diagnostic-details').open&&$('live-log').checked)refreshLog();});
$('load-log').addEventListener('click',()=>void refreshLog());
document.querySelector('.skip').addEventListener('click',event=>{event.preventDefault();$('workspace').focus();});
$('browse-game').addEventListener('click',()=>browse('game'));$('browse-unity').addEventListener('click',()=>browse('unity'));
$('browse-apk').addEventListener('click',()=>browse('apk'));$('build-mode').addEventListener('change',updateView);$('base-apk').addEventListener('input',updateView);
$('cancel').addEventListener('click',async()=>{busy=true;updateView();try{await api.cancel(state.session);const result=await api.status(state.session);state=result.state;schedulePoll();}catch(value){error(value);}finally{busy=false;updateView();}});
$('game-root').addEventListener('input',()=>{selected=null;resetArtwork();updateView();});$('provider').addEventListener('change',()=>{selected=null;resetArtwork();updateView();});$('profile-name').addEventListener('input',updateView);
$('declare-dlc').addEventListener('change',()=>{$('dlc-declaration').hidden=!$('declare-dlc').checked;});
window.addEventListener('beforeunload',()=>{clearTimeout(pollTimer);clearTimeout(slideTimer);clearTimeout(artPollTimer);stopOptionalReads();blobUrls.forEach(url=>URL.revokeObjectURL(url));});
$('preview-notice').hidden=!preview;setLanguage(language);
if(canConnect)pollGallery();
busy=true;updateView();
try{discovery=await api.discover();renderGames();if(discovery.games?.length===1)selectGame(discovery.games[0]);
  const editor=discovery.unityEditors?.find(row=>row.version==='2021.3.5f1'&&row.androidSupport);if(editor)$('unity-editor').value=editor.path;
  const session=savedSession(discovery);busy=false;if(session)await openSession(session);
}catch(value){$('discovery-status').textContent=t('notFound');error(value);}finally{busy=false;renderGames();updateView();}
