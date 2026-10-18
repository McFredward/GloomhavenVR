import {translate} from './i18n.mjs';
import {choicesFromForm,progressView,macroStep,isActive,stageStatus,stageProgress,artworkUrl,sessionId,activeWorkView,timingView,durationText,failureView,updateFailureView,installationView,activityView,buildOverviewView,nativeMemoryView,unityImportView,unityTaskView} from './model.mjs';
import {LocalApi,PreviewApi} from './transport.mjs';

const $ = id => document.getElementById(id);
// The navigation height changes with language and narrow-screen wrapping. Keep
// sticky artwork and focused headings clear without a guessed mobile offset.
const topbar=document.querySelector('.topbar');
const updateNavigationHeight=()=>document.documentElement.style.setProperty('--topbar-height',topbar.getBoundingClientRect().height+'px');
const navigationObserver=new ResizeObserver(updateNavigationHeight);navigationObserver.observe(topbar);updateNavigationHeight();

const preview = new URLSearchParams(location.search).get('preview') === '1';
const fragment = location.hash.slice(1), token = fragment.startsWith('token=') ? new URLSearchParams(fragment).get('token') : fragment;
const api = preview ? new PreviewApi() : new LocalApi(location.origin,token);
let language = (preview ? new URLSearchParams(location.search).get('lang') : null) ?? readStorage('quest-wizard-language') ?? (navigator.language.startsWith('de') ? 'de' : 'en');
if (!['de','en'].includes(language)) language='de';
let page=-1,selected=null,discovery=null,state=null,busy=false,after=0,log=[],loadedLog=null,pollTimer=null,canConnect=preview||Boolean(token),artworkKey='',artworkGeneration=0;
let completedDiscoveryKey='',completedDiscoveryRequest=null;
let storageInfo=null,storageBusy=false,storagePollTimer=null,storageCompletedShown=false,storageFailure=null,storageGeneration=0;
let storageOperationId=null,storageFailureOperationId=null;
let stoppedRuns=null,runsBusy=false,runsFailure=null,runsGeneration=0,pendingRunSelection=null;
let lastCleanupRefresh=null,supportBusy=false,supportResult=null,supportFailure=null;
let runsRefreshAfterInventory=false;
const removedRuns=new Set();
let deviceInfo=null,deviceBusy=false,devicePollTimer=null,deviceDownloading=false;
let pendingDeviceCleanup=null;
let qualification=null,resumeFailed=null,restoredSession=null,sessionGeneration=0;
let automaticBaseApk='',workflowMode=null,workflowPage=0;
let packageInfo=null,packageBusy=false,packagePollTimer=null,automaticPackageSource='',automaticPackageDestination='';
let eventsUnavailable=false,stageLogUnavailable=false,lastFailureFocus='',eventsRequest=null,stageLogRequest=null;
let pendingRestoreChoice=null;
let pendingUpdateRebuild=null;
let manualLaunchWaitSince=null;
let buildFocus=false;const buildGroupChoices=new Map();
let publisherArtwork=[],ownedArtwork=[],artworkSource='';
const reducedMotion=matchMedia('(prefers-reduced-motion:reduce)');
let slides=[],slideIndex=0,slideTimer=null,slidePaused=reducedMotion.matches,artPollTimer=null,actionBusy=false,logSelectionManual=false;
const blobUrls=[];
const galleryTemplate=[...$('gallery').children].map(node=>node.cloneNode(true));
function readStorage(key) { try { return localStorage.getItem(key); } catch { return null; } }
function writeStorage(key,value) { try { localStorage.setItem(key,value); } catch { } }
function t(key,parameters) { return translate(language,key,parameters); }
function percentText(value) {
  // Rounding an unfinished scope to100% would promise a completion that its
  // producer has not returned. Small real contributions remain visible.
  const number=value>=0&&value<100?Math.floor(value*1000)/1000:value;
  return new Intl.NumberFormat(language,{maximumFractionDigits:3}).format(number);
}
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
  renderRestoreChoiceDialog();
  renderDeviceCleanupDialog();
  renderUpdateRebuildDialog();
}
function form() { return {gameRoot:$('game-root').value,provider:$('provider').value,unityEditor:$('unity-editor').value,hostSystem:discovery?.host?.system,
  mode:$('build-mode').value,baseApk:$('base-apk').value,signingRoot:$('signing-root').value,signingMode:$('signing-mode').value,
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
function storageActive() {return storageBusy||['planning','deleting','cancelling'].includes(storageInfo?.state?.status);}
function stoppedRun(value){return typeof value?.session==='string'&&/^[a-f0-9]{32}$/.test(value.session)&&['ready','failed','blocked','cancelled','interrupted'].includes(value.status)&&!removedRuns.has(value.session);}
function normalRuns(){
  const saved=stoppedRuns?.runs??discovery?.recentSessions??[];
  const rows=state?.session&&!saved.some(row=>row?.session===state.session)?[state,...saved]:saved;
  const seen=new Set();
  return rows.filter(row=>sessionId(row?.session)&&!row.retired&&!removedRuns.has(row.session)&&!seen.has(row.session)&&seen.add(row.session))
    .map(row=>{
      if(state?.session!==row.session)return row;
      // Fresh shared metadata may describe a run completed/resumed by another
      // console. An older opened detail view must not revive its old actions.
      const savedAt=Number(row.updated??row.created),loadedAt=Number(state.updated??state.created);
      const newer=!Number.isFinite(savedAt)||Number.isFinite(loadedAt)&&loadedAt>=savedAt;
      return newer?{...row,status:state.status,mode:state.choices?.mode??row.mode}:row;
    })
    .sort((left,right)=>(Number(right.updated??right.created)||0)-(Number(left.updated??left.created)||0)||(right.session===discovery?.latestSession?1:0)-(left.session===discovery?.latestSession?1:0));
}
function retiredRuns(){
  const rows=storageInfo?.inventory?.retiredRuns??stoppedRuns?.retiredRuns??[];
  return rows.filter(row=>sessionId(row?.session));
}
function runTitle(row){
  const stamp=Number(row.created??row.updated),date=stamp>0?new Date(stamp*1000).toLocaleString(language,{dateStyle:'short',timeStyle:'short'}):t('runDateUnknown');
  const mode={build:'modeBuild','update-mod':'homeMod','update-profile':'homeProfile'}[row.mode]??'modeBuild';
  return [date,t(mode),row.session.slice(0,8)].join(' · ');
}
function runStatus(row){return t(row.retired?'runRetired':row.status==='ready'?'runReady':row.status==='cancel_requested'?'cancelling':row.status??'pending');}
function runMeasurement(row){
  const measured=storageInfo?.inventory?.runs?.find(item=>item.session===row.session)??row.storage??row;
  return Number.isFinite(measured.exclusiveBytes)?measured:null;
}
function retainedCleanupEligible(row){const measured=runMeasurement(row);return !measured||measured.reclaimableBytes>0||measured.complete===false||measured.exact===false;}
function renderRunList(target,rows,{home=false,locked=false,retired=false}={}){
  target.replaceChildren();
  for(const row of rows){
    const article=document.createElement('article');article.className='run-row';article.dataset.session=row.session;
    const title=document.createElement('h4');title.textContent=runTitle(row);article.append(title);
    const status=document.createElement('span');status.className='run-status '+(row.retired?'retired':row.status);status.textContent=runStatus(row);article.append(status);
    if(row.gameRoot){const path=document.createElement('p');path.className='run-game-root';path.textContent=row.gameRoot;article.append(path);}
    const measured=runMeasurement(row),usage=document.createElement('p');usage.className='run-usage';
    usage.textContent=measured?[t('runExclusiveSize',{size:storageSize(measured.exclusiveBytes)}),t('runSharedSize',{size:storageSize(measured.sharedBytes)}),t('runReclaimableSize',{size:storageSize(measured.reclaimableBytes)})].join(' · '):t('runStorageUnknown');article.append(usage);
    if(measured&&Number.isFinite(measured.intermediateBytes)){
      const detail=document.createElement('small');detail.className='run-storage-detail';detail.textContent=t('runIntermediateSize',{size:storageSize(measured.intermediateBytes)})+' · '+t('runDeliveredSize',{size:storageSize(measured.deliverableBytes)});article.append(detail);
    }
    if(measured&&(measured.complete===false||measured.exact===false)){const partial=document.createElement('p');partial.className='hint';partial.textContent=t('runStoragePartial');article.append(partial);}
    const actions=document.createElement('div');actions.className='run-actions';
    if(home&&!retired){
      const action=isActive(row)?'open':['ready','failed','blocked','cancelled','interrupted'].includes(row.status)?'resume':row.status==='complete'?'result':null;
      if(action){const button=document.createElement('button');button.className='secondary';button.dataset.runAction=action;button.textContent=t(action==='open'?'homeOpenActive':action==='result'?'runOpenResult':'resumeContinue');button.disabled=busy||storageActive()||deviceActive()||packageActive();button.addEventListener('click',()=>openSession(row.session));actions.append(button);}
    }
    if(discovery?.capabilities?.runRemoval===true&&(retired?retainedCleanupEligible(row):stoppedRun(row))){
      const button=document.createElement('button');button.className='text-button';button.dataset.runAction=retired?'cleanup':'delete';button.textContent=t(retired?'runCleanRetained':'runRemove');button.disabled=locked;button.addEventListener('click',()=>openStorage(row.session,retired));actions.append(button);
    }
    if(actions.childElementCount)article.append(actions);target.append(article);
  }
}
function reconcileRemovedRuns(result){
  const ids=result.state.status==='complete'&&Array.isArray(result.state.result?.sessionsRemoved)?result.state.result.sessionsRemoved.filter(id=>typeof id==='string'&&/^[a-f0-9]{32}$/.test(id)&&!removedRuns.has(id)):[];
  const identity=result.state.operationId??(result.state.result?JSON.stringify(result.state.result):null);
  const refresh=result.state.status==='complete'&&identity&&identity!==lastCleanupRefresh;
  if(refresh){
    lastCleanupRefresh=identity;
    // Mutation invalidates the measurement. Only a user-requested refresh may
    // start another large filesystem walk; status and run metadata stay cheap.
  }
  if(!ids.length){if(refresh)void refreshRuns(true);return;}
  ids.forEach(id=>removedRuns.add(id));
  // A successful deletion invalidates every retained browser read of this
  // record, including an already in-flight status or optional stage log.
  if(ids.includes(state?.session)){
    clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();state=null;resumeFailed=null;restoredSession=null;qualification=null;
    after=0;log=[];clearLoadedLog();eventsUnavailable=false;stageLogUnavailable=false;lastFailureFocus='';
    artworkGeneration++;resetArtwork();
    $('event-log').textContent='';$('session-detail').textContent='';$('log-stage').selectedIndex=0;
    workflowMode=null;workflowPage=0;
  }
  if(ids.includes(resumeFailed))resumeFailed=null;
  if(ids.includes(restoredSession))restoredSession=null;
  if(discovery){discovery={...discovery,recentSessions:discovery.recentSessions?.filter(row=>!removedRuns.has(row.session))};if(removedRuns.has(discovery.latestSession))discovery.latestSession=null;}
  if(stoppedRuns)stoppedRuns={...stoppedRuns,runs:stoppedRuns.runs.filter(row=>!removedRuns.has(row.session))};
  pendingRunSelection=null;runsGeneration++;
  void refreshAfterRemoval();
}
async function refreshAfterRemoval(){
  try{discovery=await api.discover();if(discovery?.event!=='discovery')throw {code:'invalidReply'};preselectUpdateApk();renderGames();updateView();}
  catch(value){error(value);}
  // Refresh only after this explicit mutation, never on storage status polls.
  await refreshRuns(true);
}
function acceptStorage(result) {
  if(result?.event!=='storage'||!result.state)throw {code:'invalidReply'};
  const operationId=typeof result.state.operationId==='string'&&/^[a-f0-9]{32}$/.test(result.state.operationId)?result.state.operationId:null;
  storageInfo=result;
  if(result.state.status==='failed'){
    storageFailure=result.state.error??{code:'unknownAction'};storageFailureOperationId=operationId;
  }else if(storageFailure&&(operationId?operationId!==storageFailureOperationId:!storageFailureOperationId&&['planning','deleting'].includes(result.state.status))){
    // A lost POST response may still have started and completed a real job.
    // Only its new operation identity supersedes a failure from an old request.
    storageFailure=null;storageFailureOperationId=null;
  }
  storageOperationId=operationId;
  reconcileRemovedRuns(result);
  if(runsRefreshAfterInventory&&result.inventory?.status==='complete'){
    runsRefreshAfterInventory=false;void refreshRuns(true);
  }
}
function failStorage(value) {
  // A rejected request or disconnected browser can leave the backend idle.
  // Its next inventory/status reply must not erase the cleanup explanation.
  // Keep the error separate so an actual job still locks conflicting actions.
  storageFailure=value?.message?{message:value.message}:{code:value?.code??'offline'};
  storageFailureOperationId=storageOperationId;
}
function beginStorage(status) {
  storageGeneration++;storageFailure=null;storageFailureOperationId=null;storageBusy=true;clearTimeout(storagePollTimer);
  storageInfo={...storageInfo,state:{status}};updateView();
}
function storageSize(bytes) {
  const amount=Math.max(0,Number(bytes)||0),scale=amount>=1073741824?1073741824:amount>=1048576?1048576:amount>=1024?1024:1;
  return new Intl.NumberFormat(language,{maximumFractionDigits:1}).format(amount/scale)+' '+({1:'B',1024:'KiB',1048576:'MiB',1073741824:'GiB'}[scale]);
}
function deviceActive(){return deviceBusy||['preparing','connecting','starting','capturing','scanning','cleaning','cancelling'].includes(deviceInfo?.state?.status);}
function packageActive(){return packageBusy||packageInfo?.state?.status==='exporting';}
function renderPackage(){
  const available=discovery?.capabilities?.portablePackages===true;
  $('package-card').hidden=!available||page!=='export';$('home-export').hidden=!available;$('result-export').hidden=!available;
  if(!available)return;
  const defaults=packageInfo?.defaults??{},current=packageInfo?.state??{},active=packageActive();
  const built=state?.stages?.find(row=>row.id==='build'&&row.status==='complete')?.details?.apk;
  const suggested=built||defaults.source||discovery?.defaultApk?.path||'';
  if(!$('package-source').value||$('package-source').value===automaticPackageSource){$('package-source').value=suggested;automaticPackageSource=suggested;}
  if(!$('package-destination').value||$('package-destination').value===automaticPackageDestination){
    let destination=defaults.destination??'';
    const pathKey=value=>{const normalized=String(value??'').replaceAll('\\','/');return discovery?.host?.system==='Windows'?normalized.toLowerCase():normalized;};
    const original=state?.choices?.baseApk;
    if(destination&&[ $('package-source').value,original ].some(path=>/\.zip$/i.test(path??'')&&pathKey(path)===pathKey(destination))){
      const suffix=state?.choices?.mode==='update-profile'?'-edited':state?.choices?.mode==='update-mod'?'-updated':'-copy';
      destination=destination.replace(/\.zip$/i,suffix+'.zip');
    }
    $('package-destination').value=destination;automaticPackageDestination=destination;
  }
  const locked=busy||!packageInfo||isActive(state)||deviceActive()||storageActive()||active||!canConnect;
  for(const id of ['package-source','package-destination','package-browse-source','package-browse-destination','package-export','result-export'])$(id).disabled=locked;
  for(const id of ['package-browse-source','package-browse-destination'])$(id).hidden=discovery?.capabilities?.browse===false;
  $('package-export').textContent=t(['failed','cancelled'].includes(current.status)?'packageRetry':'packageExport');
  $('package-cancel').hidden=!active;$('package-cancel').disabled=packageBusy;
  const progress=current.progress??{},status=current.status??'idle';
  const detail=typeof progress.detail==='string'?progress.detail:'';
  const counter=Number.isFinite(progress.completed)&&Number.isFinite(progress.total)&&progress.total>0?storageSize(progress.completed)+' / '+storageSize(progress.total):'';
  $('package-status').textContent=!packageInfo?t('packageLoading'):current.error?message(current.error.message):[t('package_status_'+status),Number.isFinite(progress.percent)?percentText(progress.percent)+' %':'',counter,detail].filter(Boolean).join(' · ');
  $('package-status').setAttribute('role',status==='failed'?'alert':'status');
  $('package-progress').hidden=!active&&status!=='complete';$('package-progress').value=Math.max(0,Math.min(100,Number(progress.percent)||0));
  const result=current.result??defaults.latestExport;
  $('package-result').hidden=!result?.path;$('package-result').textContent=result?.path??'';
}
function schedulePackagePoll(){clearTimeout(packagePollTimer);if(!document.hidden&&discovery?.capabilities?.portablePackages===true)packagePollTimer=setTimeout(refreshPackage,packageActive()?500:5000);}
async function refreshPackage(){
  try{const result=await api.packageStatus();if(result?.event!=='package'||!result.state)throw {code:'invalidReply'};packageInfo=result;updateView();}
  catch(value){error(value);}finally{schedulePackagePoll();}
}
function navigatePage(next,focusId){
  // Navigation does not reset retained work, stop an active job or authorize a
  // mutation. Utility controls enforce the shared active-work lock separately.
  if(typeof page==='number'&&page>=0)workflowPage=page;
  page=next;clearError();updateView();
  if(next===-1)void refreshCompletedDiscovery();
  const heading=$(focusId);heading?.setAttribute('tabindex','-1');heading?.focus();
}
function completedOutputKey(){
  const build=state?.stages?.find(row=>row.id==='build'&&row.status==='complete');
  if(!build)return '';
  return [state.session,build.key??'',build.details?.apkSha256??state.result?.apkSha256??'',state.result?.apk??''].join(':');
}
async function refreshCompletedDiscovery(){
  const key=completedOutputKey();
  if(!key||key===completedDiscoveryKey||completedDiscoveryRequest)return;
  // Successful publication changes the owned APK pointer. The initial browser
  // discovery cannot remain the authority for its old version afterwards.
  // Read only the existing bounded metadata API, once per observed output;
  // navigation and status polling never scan or validate APK payloads here.
  const request=api.discover();completedDiscoveryRequest=request;renderHome();
  try{
    const result=await request;
    if(result?.event!=='discovery')throw {code:'invalidReply'};
    // A completed run's base is its old input, not a manual override for the
    // next workflow. Preserve that durable choice in state, but suggest the
    // newly published result when the user starts another update.
    if(completedOutputKey()===key&&$('base-apk').value===state?.choices?.baseApk)
      automaticBaseApk=$('base-apk').value;
    discovery=result;completedDiscoveryKey=key;preselectUpdateApk();renderGames();
  }catch{
    // A failed advisory read does not turn a successful build into a failure.
    // Returning Home retries it without starting or changing any build job.
  }finally{completedDiscoveryRequest=null;updateView();}
}
function focusPackage(){if(discovery?.capabilities?.portablePackages===true)navigatePage('export','package-title');}

async function exportPackage(){
  if(busy||isActive(state)||storageActive()||deviceActive()||packageActive())return;
  const source=$('package-source').value.trim(),destination=$('package-destination').value.trim();
  if(!source||!destination){error({code:!source?'packageMissingSource':'packageMissingDestination'});return;}
  packageBusy=true;clearError();updateView();
  try{const result=await api.packageExport(source,destination);if(result?.event!=='package'||!result.state)throw {code:'invalidReply'};packageInfo=result;}
  catch(value){error(value);}finally{packageBusy=false;updateView();schedulePackagePoll();}
}
function homeActiveWork(){
  if(isActive(state)){
    const progress=progressView(state),current=progress.active??progress.current,sub=stageProgress(current);
    const measured=Number.isFinite(current?.progress?.stagePercent)||progress.percent>0;
    return {kind:'build',session:state.session,status:state.status,
      title:t({'update-mod':'homeMod','update-profile':'homeProfile'}[state.choices?.mode]??'homeBuild'),
      phase:t('homeWorkStage',{stage:progress.phase?t('stage_'+progress.phase):t('working')}),
      detail:[sub.phase?substepLabel(sub):'',typeof sub.detail==='string'?sub.detail:''].filter(Boolean).join(' · '),
      percent:measured?progress.percent:null};
  }
  // Discovery may already name a task from another open tab. Display it without
  // automatically reopening, resuming or replacing the selected workflow.
  const running=normalRuns().find(row=>isActive(row));
  if(running)return {kind:'build',session:running.session,status:running.status,
    title:t({'update-mod':'homeMod','update-profile':'homeProfile'}[running.mode]??'homeBuild'),detail:t('homeWorkLoading'),phase:t('working'),percent:null};
  if(deviceActive()){
    const current=deviceInfo?.state??{},progress=current.progress??{},key='deviceStatus_'+current.status;
    return {kind:'device',status:current.status,title:t('deviceTitle'),
      phase:current.message?message(current.message):t(key)===key?t('working'):t(key),
      detail:[typeof progress.label==='string'?progress.label:'',typeof current.activity?.detail==='string'?current.activity.detail:''].filter(Boolean).join(' · '),percent:progress.percent};
  }
  if(packageActive()){
    const current=packageInfo?.state??{},progress=current.progress??{};
    return {kind:'export',status:current.status,title:t('homeExport'),phase:t('package_status_exporting'),detail:typeof progress.detail==='string'?progress.detail:'',percent:progress.percent};
  }
  const inventory=storageInfo?.inventory??{},measuring=['scanning','cancelling'].includes(inventory.status);
  if(storageActive()||measuring){
    const current=storageActive()?storageInfo?.state??{}:inventory;
    const phaseKey='storage_phase_'+current.phase;
    const measured=current.phase==='cleanup-delete'&&Number.isFinite(current.done)&&Number.isFinite(current.total)&&current.total>0;
    return {kind:'storage',status:current.status,title:t(measuring&&!storageActive()?'homeWorkInventory':'homeWorkStorage'),
      phase:t(phaseKey)===phaseKey?t('working'):t(phaseKey),detail:storageCounters(current),percent:measured?100*current.done/current.total:null};
  }
  return null;
}
function renderHomeActivity(){
  const work=homeActiveWork();$('home-active').hidden=!work;
  if(!work)return;
  $('home-active-status').textContent=t(['cancelling','cancel_requested'].includes(work.status)?'homeWorkCancelling':'homeWorkRunning');
  $('home-active-title').textContent=work.title;$('home-active-detail').textContent=work.detail??'';$('home-active-detail').hidden=!work.detail;
  $('home-active-phase').textContent=work.phase??'';
  const measured=Number.isFinite(work.percent)&&work.percent>=0&&work.percent<=100;
  $('home-active-percent').textContent=measured?t('phasePercent',{percent:percentText(work.percent)}):t('homeWorkNotMeasured');
  $('home-active-percent').classList.toggle('unmeasured',!measured);
  if(measured)$('home-active-bar').value=work.percent;else $('home-active-bar').removeAttribute('value');
  $('home-active-bar').setAttribute('aria-label',t('homeWorkProgress'));
  $('home-active-open').disabled=busy;
}
function openHomeActivity(){
  const work=homeActiveWork();if(!work||busy)return;
  if(work.kind==='build'){
    if(state?.session===work.session){navigatePage(2,'progress-title');return;}
    void openSession(work.session);return;
  }
  navigatePage(work.kind,{device:'device-title',export:'package-title',storage:'storage-title'}[work.kind]);
}
function renderHome(){
  $('home-page').hidden=page!==-1;$('home').hidden=false;$('home').disabled=busy;
  if(page===-1)$('home').setAttribute('aria-current','page');else $('home').removeAttribute('aria-current');
  const recommendation=discovery?.updateRecommendation;
  const output=completedOutputKey(),stale=Boolean(output)&&output!==completedDiscoveryKey;
  const recommended=!stale&&!completedDiscoveryRequest&&recommendation?.available===true&&recommendation?.inputsReady===true;
  $('home-mod').classList.toggle('recommended',recommended);
  $('home-mod-badge').hidden=!recommended;$('home-mod-recommendation').hidden=!recommended;
  $('home-mod-recommendation').textContent=recommended?t('homeUpdateReady',{installed:recommendation.installedBuild,bundled:recommendation.bundledBuild}):'';
  $('home-update-status').hidden=!completedDiscoveryRequest&&(Boolean(discovery)||!busy);
  renderHomeActivity();
  document.querySelector('.steps').hidden=typeof page!=='number'||page<0;
  $('home-device').hidden=discovery?.capabilities?.deviceTools!==true;
  $('home-storage').hidden=!storageInfo;
  for(const id of ['home-export','home-device','home-storage'])$(id).disabled=busy||!canConnect;
  const locked=busy||storageActive()||deviceActive()||packageActive()||isActive(state)||normalRuns().some(isActive)||!canConnect;
  for(const id of ['home-build','home-mod','home-profile'])$(id).disabled=locked;
  const rows=normalRuns();$('home-runs').hidden=!rows.length&&!runsBusy&&!runsFailure;
  $('home-runs-refresh').disabled=busy||runsBusy||storageActive();
  $('home-runs-status').textContent=runsFailure?t('runsFailed',{message:runsFailure.message?message(runsFailure.message):t(runsFailure.code??'offline')}):runsBusy?t('runsLoading'):'';
  $('home-runs-status').setAttribute('role',runsFailure?'alert':'status');
  renderRunList($('home-runs-list'),rows,{home:true,locked});
}
function renderDevice(){
  $('device-card').hidden=discovery?.capabilities?.deviceTools!==true||page!=='device';
  if(discovery?.capabilities?.deviceTools!==true)return;
  const current=deviceInfo?.state??{},active=deviceActive(),locked=busy||isActive(state)||storageActive()||packageActive()||active||!canConnect;
  for(const id of ['device-connect','device-wireless','device-start','device-wait-debugger','device-capture','device-storage-preview','device-host','device-serial'])$(id).disabled=locked;
  $('device-cancel').hidden=!active;$('device-cancel').disabled=deviceBusy||current.status==='cancelling';
  const devices=Array.isArray(current.devices)?current.devices:[],key=devices.map(row=>[row.serial,row.state,row.model].join(':')).join('|');
  if($('device-serial').dataset.devices!==key){
    const previous=$('device-serial').value;$('device-serial').replaceChildren();
    const automatic=document.createElement('option');automatic.value='';automatic.textContent=t('deviceAutomatic');$('device-serial').append(automatic);
    for(const row of devices){if(typeof row.serial!=='string')continue;const option=document.createElement('option');option.value=row.serial;option.textContent=[row.model,row.serial,row.state].filter(Boolean).join(' · ');$('device-serial').append(option);}
    $('device-serial').value=previous;$('device-serial').dataset.devices=key;
  }else $('device-serial').firstElementChild.textContent=t('deviceAutomatic');
  $('device-selection').hidden=devices.length<2;
  const parameters=current.progress??{},number=value=>new Intl.NumberFormat(language).format(value);
  const counter=Number.isFinite(parameters.done)&&Number.isFinite(parameters.total)&&parameters.total>0?t('deviceCounter',{done:number(parameters.done),total:number(parameters.total)}):'';
  const percent=Number.isFinite(parameters.percent)?t('phasePercent',{percent:percentText(parameters.percent)}):'';
  const elapsed=Number.isFinite(current.elapsedSeconds)?t('deviceElapsed',{duration:durationText(current.elapsedSeconds)}):'';
  const fallback='deviceStatus_'+(current.status??'idle');
  const status=current.message?message(current.message):t(fallback)===fallback?t('deviceStatus_idle'):t(fallback);
  const selected=[current.model,current.serial,current.endpoint].filter(Boolean).join(' · ');
  $('device-status').textContent=[status,percent,counter,elapsed,typeof parameters.label==='string'?parameters.label:'',selected].filter(Boolean).join(' · ');
  $('device-status').setAttribute('role',current.status==='failed'?'alert':'status');
  $('device-progress').hidden=!active;
  if(Number.isFinite(parameters.percent))$('device-progress').value=Math.max(0,Math.min(100,parameters.percent));else $('device-progress').removeAttribute('value');
  $('device-progress').setAttribute('aria-label',t('deviceOverallProgress'));
  if(!$('device-activity')){
    const description=document.createElement('p');description.id='device-activity';description.className='hint';
    $('device-progress').insertAdjacentElement('afterend',description);
    const progress=document.createElement('progress');progress.id='device-subprogress';progress.max=100;
    description.insertAdjacentElement('afterend',progress);
  }
  const activity=current.activity??{},activityKey='deviceActivity_'+activity.label;
  const activityLabel=t(activityKey)===activityKey?activity.label??'':t(activityKey);
  const byteCount=Number.isFinite(activity.receivedBytes)&&(activity.receivedBytes>0||Number.isFinite(activity.totalBytes))?t(Number.isFinite(activity.totalBytes)?'deviceTransferKnown':'deviceTransferObserved',{
    done:number(Math.max(0,activity.receivedBytes)),total:number(Math.max(0,activity.totalBytes??0))}):'';
  const activityTime=Number.isFinite(activity.elapsedSeconds)?t('deviceTaskElapsed',{duration:durationText(activity.elapsedSeconds)}):'';
  $('device-activity').hidden=!active||!activityLabel;
  $('device-activity').textContent=[activityLabel,activity.detail,byteCount,activityTime].filter(Boolean).join(' · ');
  $('device-subprogress').hidden=!active||!Number.isFinite(activity.totalBytes)||activity.totalBytes<=0;
  $('device-subprogress').setAttribute('aria-label',t('deviceTaskProgress'));
  if(Number.isFinite(activity.percent))$('device-subprogress').value=Math.max(0,Math.min(100,activity.percent));else $('device-subprogress').removeAttribute('value');
  const capture=current.capture,complete=Boolean(capture?.id&&capture?.name);
  $('device-capture-result').hidden=!complete;$('device-capture-path').textContent=complete?(capture.path??capture.name):'';
  $('device-download').disabled=!complete||deviceDownloading;
  renderDeviceStorage(current,locked);
  if(current.status==='waiting')$('device-guide').open=true;
}
function scheduleDevicePoll(){clearTimeout(devicePollTimer);if(!document.hidden&&discovery?.capabilities?.deviceTools===true)devicePollTimer=setTimeout(refreshDevice,deviceActive()?600:5000);}
async function refreshDevice(){
  try{const result=await api.deviceStatus();if(result?.event!=='device'||!result.state)throw {code:'invalidReply'};deviceInfo=result;updateView();}
  catch(value){deviceInfo={schema:1,event:'device',state:{status:'failed',message:value?.message??t(value?.code??'offline')}};updateView();}
  finally{scheduleDevicePoll();}
}
async function deviceOperation(operation){
  if(deviceActive()||busy||isActive(state)||storageActive()||packageActive())return;
  deviceBusy=true;clearError();clearTimeout(devicePollTimer);updateView();
  try{
    const host=$('device-host').value.trim(),serial=$('device-serial').value;
    const result=operation==='capture'?await api.deviceCapture(host,serial):operation==='start'?await api.deviceStart(host,serial,$('device-wait-debugger').checked):operation==='storage-preview'?await api.deviceStoragePreview(host,serial):await api.deviceConnect(host,operation==='wireless',serial);
    if(result?.event!=='device'||!result.state)throw {code:'invalidReply'};deviceInfo=result;
  }catch(value){deviceInfo={schema:1,event:'device',state:{status:'failed',message:value?.message??t(value?.code??'offline')}};}
  finally{deviceBusy=false;updateView();scheduleDevicePoll();}
}
function deviceStorageRows(node,rows,emptyKey){
  node.replaceChildren();
  if(!Array.isArray(rows)||!rows.length){const li=document.createElement('li');li.textContent=t(emptyKey);node.append(li);return;}
  for(const row of rows){
    const li=document.createElement('li');
    const size=Number.isFinite(row.bytes)?storageSize(row.bytes):t('deviceStorageSizeUnknown');
    const count=Number.isFinite(row.files)?t('deviceStorageFiles',{files:new Intl.NumberFormat(language).format(row.files)}):'';
    const labels={'content-upload':'deviceStorageUpload','upload-marker':'deviceStorageMarker',
      'rename-helper':'deviceStorageHelper','restore-upload':'deviceStorageRestore',
      'debugger-scratch':'deviceStorageHelper','installed-content':'deviceStorageGame',
      'app-data':'deviceStorageAppData','pc-backup':'deviceStorageBackup','recovery-parking':'deviceStorageRecovery'};
    const supplied=message(row.label,'deviceStorageArtifact');
    const label=supplied===row.path?t(labels[row.kind]??'deviceStorageArtifact'):supplied;
    li.textContent=[label,size,count].filter(Boolean).join(' · ');
    // These are finite Builder artifact roots from the preview, never a save
    // member inventory. textContent also keeps device-controlled text inert.
    if(typeof row.path==='string'){const path=document.createElement('code');path.className='storage-root';path.textContent=row.path;li.append(path);}
    node.append(li);
  }
}
function renderDeviceStorage(current,locked){
  const plan=current.storagePlan,valid=current.status==='storage-preview'&&typeof plan?.planId==='string'&&/^[a-f0-9]{32}$/.test(plan.planId)&&typeof plan.complete==='boolean';
  $('device-storage-preview-result').hidden=!valid;
  const candidates=valid&&Array.isArray(plan.candidates)?plan.candidates:[];
  $('device-storage-clean').disabled=locked||!valid||!candidates.length;
  if(valid){
    $('device-storage-summary').textContent=t('deviceStorageSummary',{quest:plan.deviceSerial,size:storageSize(plan.bytes),files:new Intl.NumberFormat(language).format(plan.files??0)});
    deviceStorageRows($('device-storage-candidates'),candidates,'deviceStorageNoFiles');
    deviceStorageRows($('device-storage-protected'),plan.protected,'deviceStorageNoProtected');
    $('device-storage-unknown').hidden=!plan.unknown?.length;
    deviceStorageRows($('device-storage-unknown-list'),plan.unknown,'deviceStorageNoFiles');
  }
  const result=current.storageResult;
  $('device-storage-result').hidden=!result;
  if(result)$('device-storage-result').textContent=t('deviceStorageComplete',{size:storageSize(result.freedBytes),files:new Intl.NumberFormat(language).format(result.deletedFiles??0)})+(result.retained?.length?' '+t('deviceStorageRetained',{count:result.retained.length}):'');
  if(pendingDeviceCleanup&&(!valid||pendingDeviceCleanup.planId!==plan.planId||pendingDeviceCleanup.deviceSerial!==plan.deviceSerial))closeDeviceCleanupDialog();
}
function renderDeviceCleanupDialog(){
  if(!pendingDeviceCleanup)return;
  const plan=pendingDeviceCleanup;
  $('device-storage-dialog-copy').textContent=t('deviceStorageSummary',{quest:plan.deviceSerial,size:storageSize(plan.bytes),files:new Intl.NumberFormat(language).format(plan.files??0)});
  deviceStorageRows($('device-storage-dialog-list'),plan.candidates,'deviceStorageNoFiles');
}
function closeDeviceCleanupDialog(){pendingDeviceCleanup=null;$('device-storage-dialog').close();}
function openDeviceCleanupDialog(){
  const plan=deviceInfo?.state?.storagePlan;
  if($('device-storage-clean').disabled||!plan?.planId)return;
  pendingDeviceCleanup=structuredClone(plan);renderDeviceCleanupDialog();$('device-storage-dialog').showModal();
}
async function confirmDeviceCleanup(){
  const plan=pendingDeviceCleanup,current=deviceInfo?.state;
  if(!plan||current?.status!=='storage-preview'||plan.planId!==current.storagePlan?.planId||plan.deviceSerial!==current.storagePlan?.deviceSerial||deviceActive()||busy||isActive(state)||storageActive()||packageActive())return;
  closeDeviceCleanupDialog();deviceBusy=true;clearError();clearTimeout(devicePollTimer);updateView();
  try{const result=await api.deviceStorageClean(plan.planId);if(result?.event!=='device'||!result.state)throw {code:'invalidReply'};deviceInfo=result;}
  catch(value){deviceInfo={schema:1,event:'device',state:{status:'failed',message:value?.message??t(value?.code??'offline')}};}
  finally{deviceBusy=false;updateView();scheduleDevicePoll();}
}
async function downloadCapture(){
  const id=deviceInfo?.state?.capture?.id;if(!id||deviceDownloading)return;
  deviceDownloading=true;updateView();try{const result=await api.deviceDownload(id);downloadBlob(result);}catch(value){error(value);}finally{deviceDownloading=false;updateView();}
}
function downloadBlob(result){const url=URL.createObjectURL(result.blob),link=document.createElement('a');link.href=url;link.download=result.name;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
async function inventoryStorage(){
  if(['scanning','cancelling'].includes(storageInfo?.inventory?.status)||storageActive()||deviceActive()||discovery?.capabilities?.storageInventory!==true)return;
  $('storage-refresh').disabled=true;
  const generation=storageGeneration;
  runsRefreshAfterInventory=true;
  try{const result=await api.storageInventory();if(generation===storageGeneration)acceptStorage(result);}
  catch(value){runsRefreshAfterInventory=false;error(value);}finally{updateView();scheduleStoragePoll();}
}
async function refreshRuns(force=false){
  if(discovery?.capabilities?.runOverview!==true&&discovery?.capabilities?.runRemoval!==true||runsBusy&&!force)return;
  const generation=++runsGeneration;runsBusy=true;runsFailure=null;updateView();
  try{
    const result=await api.storageRuns();
    if(result?.event!=='runs'||!Array.isArray(result.runs))throw {code:'invalidReply'};
    if(generation!==runsGeneration)return;
    stoppedRuns={...result,runs:result.runs.filter(row=>sessionId(row?.session)&&!row.retired&&!removedRuns.has(row.session))};
    const selected=pendingRunSelection??$('storage-run').value;
    pendingRunSelection=stoppedRuns.runs.some(row=>row.session===selected)?selected:null;
  }catch(value){if(generation===runsGeneration)runsFailure=value;}
  finally{if(generation===runsGeneration){runsBusy=false;updateView();}}
}
function openStorage(session=null,retired=false){
  pendingRunSelection=session;
  if(session){$('storage-details').open=true;$('storage-run-keep').checked=!retired;$('storage-run-remove-data').checked=retired;}
  navigatePage('storage','storage-title');if(!stoppedRuns)void refreshRuns();
  if(session)$('storage-run-choice').scrollIntoView({block:'nearest'});
}
function renderRuns(locked){
  const available=discovery?.capabilities?.runRemoval===true;
  $('storage-runs').hidden=!available;if(!available)return;
  const rows=[...normalRuns().filter(stoppedRun),...retiredRuns().filter(retainedCleanupEligible).map(row=>({...row,retired:true}))],selection=pendingRunSelection??$('storage-run').value;
  const key=JSON.stringify([language,rows.map(row=>[row.session,row.status,row.gameRoot,row.updated,row.created,row.mode,row.retired])]);
  if($('storage-run').dataset.runs!==key){
    $('storage-run').replaceChildren();
    for(const row of rows){const option=document.createElement('option');option.value=row.session;option.dataset.retired=String(row.retired===true);option.textContent=runTitle(row)+' · '+runStatus(row);$('storage-run').append(option);}
    if(rows.some(row=>row.session===selection))$('storage-run').value=selection;
    $('storage-run').dataset.runs=key;
  }
  if(pendingRunSelection&&rows.some(row=>row.session===pendingRunSelection))pendingRunSelection=null;
  const retired=$('storage-run').selectedOptions[0]?.dataset.retired==='true';
  if(retired){$('storage-run-keep').checked=false;$('storage-run-remove-data').checked=true;}
  const disabled=locked||!rows.length||Boolean(runsFailure);
  $('storage-runs-refresh').disabled=locked||runsBusy;
  for(const id of ['storage-run','storage-run-choice','storage-run-plan'])$(id).disabled=disabled;
  $('storage-run-keep').disabled=retired;
  $('storage-run-consequence').hidden=!rows.length;$('storage-run-consequence').textContent=t(retired?'runRetiredConsequence':'runRemovalConsequence');
  $('storage-runs-status').textContent=runsFailure?t('runsFailed',{message:runsFailure.message?message(runsFailure.message):t(runsFailure.code??'offline')}):runsBusy?t('runsLoading'):rows.length?'':t('runsEmpty');
  $('storage-runs-status').setAttribute('role',runsFailure?'alert':'status');
}
function renderStorage() {
  $('storage-card').hidden=!storageInfo||page!=='storage';
  if(!storageInfo)return;
  $('storage-root').textContent=storageInfo.workspaceRoot??'';
  $('storage-free').textContent=Number.isFinite(storageInfo.freeBytes)?t('storageFree',{size:storageSize(storageInfo.freeBytes)}):'';
  const volume=storageInfo.volume??{};
  $('storage-volume').hidden=!Number.isFinite(volume.usedBytes)||!Number.isFinite(volume.totalBytes);
  $('storage-volume').textContent=t('storageVolume',{used:storageSize(volume.usedBytes),total:storageSize(volume.totalBytes)});
  const current=storageInfo.state??{},status=current.status,plan=current.plan;
  const inventory=storageInfo.inventory??{},measuring=['scanning','cancelling'].includes(inventory.status);
  $('storage-refresh').hidden=discovery?.capabilities?.storageInventory!==true;
  $('storage-refresh').disabled=measuring||busy||isActive(state)||storageActive()||deviceActive()||packageActive();
  $('storage-used').textContent=Number.isFinite(inventory.totalBytes)?t('storageUsed',{size:storageSize(inventory.totalBytes)}):t('storageUsageUnknown');
  $('storage-inventory-cancel').hidden=!measuring||discovery?.capabilities?.storageCancel!==true;
  $('storage-inventory-cancel').disabled=inventory.status==='cancelling'||!inventory.operationId;
  $('storage-inventory-status').textContent=measuring?[t(inventory.status==='cancelling'?'storageCancelling':'storageUsageScanning',{files:new Intl.NumberFormat(language).format(inventory.files??inventory.done??0)}),storageCounters(inventory)].filter(Boolean).join(' · '):inventory.status==='cancelled'?[t('storageInventoryCancelled'),storageCounters(inventory)].filter(Boolean).join(' · '):inventory.status==='failed'?[t('storageUsageFailed'),message(inventory.error?.message,''),storageCounters(inventory)].filter(Boolean).join(' · '):inventory.status==='stale'?t('storageUsageStale',{time:new Date(inventory.completedAt*1000).toLocaleString(language)}):inventory.completedAt?t('storageUsageMeasured',{time:new Date(inventory.completedAt*1000).toLocaleTimeString(language)}):'';
  $('storage-categories').hidden=!Array.isArray(inventory.categories);$('storage-categories').replaceChildren();
  for(const row of inventory.categories??[]){const pair=document.createElement('div'),label=document.createElement('dt'),size=document.createElement('dd'),key='storageCategory_'+row.id;label.textContent=t(key)===key?t('storageCategory_other'):t(key);size.textContent=storageSize(row.bytes);pair.append(label,size);$('storage-categories').append(pair);}
  for(const [field,key] of [['originalApkBytes','storageBackupOriginalApk'],['appDataBytes','storageBackupAppData']]){
    const bytes=inventory.signingBackups?.[field];if(!(bytes>0))continue;
    const pair=document.createElement('div'),label=document.createElement('dt'),size=document.createElement('dd');pair.className='storage-backup-detail';label.textContent=t(key);size.textContent=storageSize(bytes);pair.append(label,size);$('storage-categories').append(pair);
  }
  const locked=busy||isActive(state)||storageActive()||deviceActive()||packageActive();
  $('storage-attribution').hidden=!Array.isArray(inventory.runs)&&!Array.isArray(stoppedRuns?.runs);
  $('storage-ownership').replaceChildren();
  for(const [field,key] of [['exclusiveBytes','storageOwnershipExclusive'],['sharedBytes','storageOwnershipShared'],['unassignedBytes','storageOwnershipUnassigned']]){
    if(!Number.isFinite(inventory[field]))continue;
    const pair=document.createElement('div'),label=document.createElement('dt'),size=document.createElement('dd');label.textContent=t(key);size.textContent=storageSize(inventory[field]);pair.append(label,size);$('storage-ownership').append(pair);
  }
  renderRunList($('storage-runs-list'),normalRuns(),{locked});
  const retired=retiredRuns();$('storage-retired').hidden=!retired.length;renderRunList($('storage-retired-list'),retired,{retired:true,locked});
  renderRuns(locked);
  $('storage-plan-duplicates').disabled=locked;
  const completed=storageInfo.canDeleteBuildCache===true||inventory.canDeleteBuildCache===true||state?.status==='complete'||state?.stages?.some(row=>row.id==='build'&&row.status==='complete');
  $('storage-plan-cache').hidden=!completed;$('storage-plan-cache').disabled=locked;
  if(completed&&!storageCompletedShown){$('storage-details').open=true;storageCompletedShown=true;}
  $('storage-cache-consequence').hidden=plan?.mode!=='build-cache';
  const measured=current.phase==='cleanup-delete'&&Number.isFinite(current.done)&&Number.isFinite(current.total)&&current.total>0;
  const count=['planning','deleting','cancelling','cancelled','failed'].includes(status)?storageCounters(current):'';
  const files=plan?.files??0,parameters={files:new Intl.NumberFormat(language).format(files),size:storageSize(plan?.bytes)};
  const sessionPlan=plan?.mode==='session';
  const sessionEligible=sessionPlan&&plan.eligible===true;
  const removed=current.result?.sessionsRemoved;
  const copy=['planning','deleting'].includes(status)&&current.phase==='inventory-pausing'?t('storageInventoryPausing'):status==='cancelling'?t('storageCancelling'):status==='cancelled'?t('storageCancelled'):status==='planning'?t('storagePlanning'):status==='deleting'?t('storageDeleting'):status==='preview'?t(sessionPlan?(plan.retired?'runRetiredPreview':'runPreview'):files>0?'storagePreview':plan?.mode==='duplicates'&&discovery?.capabilities?.runRemoval===true?'storageNoFilesRetained':'storageNoFiles',{...parameters,session:(plan?.session??'').slice(0,8)}):status==='complete'?Array.isArray(removed)&&removed.length?t('runRemoved',{session:removed.map(id=>id.slice(0,8)).join(', '),size:storageSize(current.result?.freedBytes)}):t('storageComplete',{files:new Intl.NumberFormat(language).format(current.result?.deletedFiles??0),size:storageSize(current.result?.freedBytes)}):'';
  const failure=storageFailure??(status==='failed'?current.error:null);
  const recoverable=failure?.parameters?.retryable===true&&failure?.parameters?.recovery==='refresh-preview';
  const failed=failure?t(recoverable?'storageActionRequired':'storageFailed',{message:failure.message?message(failure.message):t(failure.code??'unknownAction')}):'';
  const buildStage=state?.stages?.find(row=>['running','cancelling','cancel_requested'].includes(row.status))?.id??state?.progress?.phase;
  const waiting=isActive(state)?t('storageWaitingBuild',{stage:buildStage?t('stage_'+buildStage):t('working')}):deviceActive()?t('storageWaitingDevice'):packageActive()?t('storageWaitingExport'):busy?t('storageWaitingSetup'):'';
  const skipped=status==='preview'&&plan?.skipped?.length?t('storageSkipped',{count:plan.skipped.length}):'';
  const phaseKey='storage_phase_'+current.phase,subphaseKey='storage_subphase_'+current.subphase;
  const phase=[t(phaseKey)!==phaseKey?t(phaseKey):'',t(subphaseKey)!==subphaseKey?t(subphaseKey):''].filter(Boolean).join(' · ');
  const partial=current.result??failure?.parameters?.result;
  const freed=partial?.deletedFiles>0?t('storagePartialRemoval',{files:new Intl.NumberFormat(language).format(partial.deletedFiles),size:storageSize(partial.freedBytes)}):'';
  $('storage-status').textContent=[failed,copy,phase,count,freed,skipped,waiting].filter(Boolean).join(' · ');$('storage-status').setAttribute('role',failure?'alert':'status');
  $('storage-progress').hidden=!['planning','deleting','cancelling'].includes(status);
  if(measured)$('storage-progress').value=Math.min(99.999,Math.max(0,current.done/current.total*100));else $('storage-progress').removeAttribute('value');
  $('storage-cancel').hidden=!['planning','deleting','cancelling'].includes(status)||discovery?.capabilities?.storageCancel!==true;
  $('storage-cancel').disabled=storageBusy||status==='cancelling'||!current.operationId;
  $('storage-retry').hidden=!['failed','cancelled'].includes(status)||!current.selection?.mode;
  $('storage-retry').disabled=locked;
  const previewing=status==='preview'&&!failure;$('storage-paths').hidden=!previewing;$('storage-paths').replaceChildren();
  if(previewing)for(const row of plan?.paths??[]){const li=document.createElement('li'),path=document.createElement('code'),detail=document.createElement('small');path.textContent=row.path;detail.textContent=t('storagePlanSize',{size:storageSize(row.bytes),files:new Intl.NumberFormat(language).format(row.files??0)});li.append(path,detail);$('storage-paths').append(li);}
  $('storage-skipped').hidden=!previewing||!plan?.skipped?.length;$('storage-skipped').replaceChildren();
  if(previewing)for(const row of plan?.skipped??[]){const li=document.createElement('li'),path=document.createElement('code'),detail=document.createElement('small');path.textContent=typeof row==='string'?row:row.path??'';const reason=typeof row==='object'?row.reason:'';detail.textContent=t('storageRetainedPath',{reason:reason==='shared'?t('storageRetainedShared'):reason==='shared_or_delivered'?t('storageRetainedSharedDelivered'):reason==='protected'?t('storageRetainedProtected'):t('storageRetainedUnknown')});li.append(path,detail);$('storage-skipped').append(li);}
  $('storage-confirmation').hidden=!previewing||sessionPlan&&!sessionEligible||files<=0&&!sessionEligible;$('storage-confirm-copy').textContent=t(sessionPlan?(plan.retired?'runRetiredConfirmCopy':'runConfirmCopy'):'storageConfirmCopy');
  $('storage-clean').textContent=t(sessionPlan&&!plan.retired?'runConfirm':'storageConfirm');
  $('storage-clean').disabled=locked||!plan?.id||Boolean(failure)||sessionPlan&&!sessionEligible;$('storage-dismiss').disabled=locked;
}
function storageCounters(current){
  const parts=[],files=current.files??current.done;
  if(Number.isFinite(files))parts.push(t('storageScanned',{done:new Intl.NumberFormat(language).format(files)}));
  if(Number.isFinite(current.folders))parts.push(t('storageFolders',{count:new Intl.NumberFormat(language).format(current.folders)}));
  if(Number.isFinite(current.processedEntries))parts.push(t('storageReadEntries',{count:new Intl.NumberFormat(language).format(current.processedEntries)}));
  if(Number.isFinite(current.metadataQueries))parts.push(t('storageMetadataQueries',{count:new Intl.NumberFormat(language).format(current.metadataQueries)}));
  if(Number.isFinite(current.bytes))parts.push(t('storageObservedBytes',{size:storageSize(current.bytes)}));
  if(current.unit==='entries'&&Number.isFinite(current.done)&&Number.isFinite(current.total))parts.push(t('storageEntries',{done:new Intl.NumberFormat(language).format(current.done),total:new Intl.NumberFormat(language).format(current.total)}));
  const elapsed=Number.isFinite(current.startedAt)?Math.max(0,(current.endedAt??Date.now()/1000)-current.startedAt):Number.isFinite(current.elapsedSeconds)?current.elapsedSeconds:null;
  if(elapsed!==null)parts.push(t('storageElapsed',{time:durationText(elapsed)}));
  const path=current.currentPath??current.detail;if(typeof path==='string'&&path)parts.push(t('storageInventoryFolder',{path}));
  return parts.join(' · ');
}
async function cancelStorage(target){
  const current=target==='inventory'?storageInfo?.inventory:storageInfo?.state;
  if(!current?.operationId||current.status==='cancelling')return;
  storageGeneration++;current.status='cancelling';updateView();
  try{acceptStorage(await api.storageCancel(target,current.operationId));}
  catch(value){error(value);}
  finally{updateView();scheduleStoragePoll();}
}
function scheduleStoragePoll() {clearTimeout(storagePollTimer);if(!document.hidden)storagePollTimer=setTimeout(refreshStorage,storageActive()||['scanning','cancelling'].includes(storageInfo?.inventory?.status)?750:15000);}
async function refreshStorage() {
  if(storageBusy)return;
  const generation=storageGeneration;
  try{const result=await api.storage();if(generation===storageGeneration){acceptStorage(result);updateView();}}
  catch(value){if(generation===storageGeneration&&storageInfo){failStorage(value);updateView();}}
  finally{if(!storageBusy)scheduleStoragePoll();}
}
async function planStorage(mode,session,removeData=false) {
  if(storageActive()||deviceActive()||packageActive()||busy||isActive(state))return;
  $('storage-details').open=true;beginStorage('planning');
  try{acceptStorage(await api.storagePlan(mode,session,removeData));}
  catch(value){failStorage(value);storageInfo={...storageInfo,state:{status:'failed'}};}
  finally{storageBusy=false;updateView();scheduleStoragePoll();}
}
async function cleanStorage() {
  const plan=storageInfo?.state?.status==='preview'?storageInfo.state.plan:null;
  if(storageActive()||deviceActive()||packageActive()||busy||isActive(state)||storageFailure||!plan?.id||plan.mode==='session'&&plan.eligible!==true||!(plan.files>0)&&plan.mode!=='session')return;
  beginStorage('deleting');
  try{acceptStorage(await api.storageClean(plan.id));qualification=null;}
  catch(value){failStorage(value);storageInfo={...storageInfo,state:{status:'failed'}};}
  finally{storageBusy=false;updateView();scheduleStoragePoll();}
}
function phaseLabel(phase='') {
  if(phase.startsWith('unity-progress:'))return t('phase_unity-progress');
  if(phase.startsWith('unity-shader-task:'))return t('phase_unity-shader-task');
  if(phase.startsWith('unity-work-stage:'))return t('unityWork_'+phase.slice(17));
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
  if(value.progressScope==='update-preparation')return value.scopeCounter?counters(value.scopeCounter):'';
  if(value.progressScope==='shader-phase')return value.scopeCounter?counters(value.scopeCounter):'';
  if(value.progressScope==='phase-continuation')return '';
  if(['next-phase','result-phase'].includes(value.progressScope))return value.scopeCounter?counters(value.scopeCounter):'';
  if(value.unit==='assets'&&Number.isSafeInteger(value.done)&&value.done>=0&&value.total==null)return t('counterImportsCompleted',{done:new Intl.NumberFormat(language).format(value.done)});
  if(value.unit==='tasks'&&Number.isSafeInteger(value.done)&&value.done>=0&&value.total==null)return t('counterTasksReported',{done:new Intl.NumberFormat(language).format(value.done)});
  if(value.unit==='bytes'&&Number.isSafeInteger(value.done)&&value.done>=0&&value.total==null)return t('counterTransferredBytes',{size:new Intl.NumberFormat(language,{maximumFractionDigits:1}).format(value.done/1048576)+' MiB'});
  if(!Number.isFinite(value.done)||!Number.isFinite(value.total)||value.total<=0)return '';
  const number=n=>new Intl.NumberFormat(language,{maximumFractionDigits:1}).format(n);
  if(value.unit==='bytes')return t('counterBytes',{done:number(value.done/1048576)+' MiB',total:number(value.total/1048576)+' MiB'});
  if(value.phase?.startsWith('bee-actions:'))return t('counterLocalGraphActions',{done:number(value.done),total:number(value.total)});
  if(value.phase==='unity-player-scenes')return t('counterSceneEntries',{done:number(value.done),total:number(value.total)});
  const key={attempts:'counterRepairAttempts',files:'counterFiles',bundles:'counterBundles',actions:'counterActions',checks:'counterChecks',dependencies:'counterDependencies',batches:'counterBatches',steps:'counterSteps',checkpoints:'counterSteps',variants:'counterVariants',objects:'counterObjects',assets:'counterImports',assemblies:'counterAssemblies',tasks:'counterSteps',invocations:'counterInvocations',scenes:'counterScenes',sprites:'counterSprites',shaders:'counterShaders','shader-names':'counterShaderNames'}[value.unit]??'counterUnits';
  return t(key,{done:number(value.done),total:number(value.total)});
}
function workDetail(value) {
  if(value?.phase==='update-rebuild-required')return t('buildUpdateMigration');
  if(value?.progressScope==='next-phase')return t('nextPhaseScope');
  if(value?.progressScope==='result-phase')return t('playerFinalizationScope');
  if(value?.progressScope==='phase-continuation')return t('wholePhaseContinuation');
  const raw=typeof value?.detail==='string'?value.detail:'';
  if(value?.phase==='install-recovery-repair'){
    const root=raw.match(/^\[repair-root:(private|device-protected|external)\]/)?.[1];
    return root?t('installationRepairRoot',{root:t('repairRoot_'+root)}):t('installationRepairDetail');
  }
  const deliveryKey={
    'Finishing the retained Android Player; earlier preparation and Unity work are not repeated.':'deliveryResumeDetail',
    'Retained completed Player proves this earlier operation.':'deliveryRetainedDetail',
    'Finishing and signing the existing Android Player.':'deliverySigningDetail',
  }[raw];
  if(deliveryKey)return t(deliveryKey);
  const single=raw.includes('[indivisible-task]');
  const text=raw.replace(/\[gradle-graph:[A-Za-z0-9-]{1,64}\]\s*/g,'').replace(/\[(?:bee-(?:graph|scope):[^\]]{1,160}|bee-native-plan|shader-(?:name-)?coverage|shader-pass:[0-9]{1,12}|shader-source:(?:unseen|observed|outside))\]\s*/g,'').replace(/\[indivisible-task\]\s*/g,'').trim();
  const scoped=value?.progressScope==='native-phase'?t('unityNativeGraphScope'):value?.progressScope==='scenes'?t('unitySceneReturnScope'):value?.progressScope==='shader-pass'?t('shaderPassReturnScope'):value?.progressScope==='shader-phase'?t('shaderWholeReturnScope'):value?.progressScope==='scope-return'?t('unityScopeReturn'):'';
  return [scoped,single?t('indivisibleTask')+' · '+text:text].filter(Boolean).join(' · ');
}
function substepLabel(value) {
  const batch=value.recoveryBatchIndex,total=value.recoveryBatchTotal;
  const context=Number.isSafeInteger(batch)&&Number.isSafeInteger(total)&&batch>0&&batch<=total
    ?t('recoveryBatchContext',{index:batch,total}):'';
  const native=value.recoveryNativeIndex,nativeTotal=value.recoveryNativeTotal;
  const nativeContext=Number.isSafeInteger(native)&&Number.isSafeInteger(nativeTotal)&&native>0&&native<=nativeTotal
    ?t('recoveryNativeContext',{index:native,total:nativeTotal}):'';
  return t(value.progressScope==='next-phase'?'nextSubstep':'substep',{phase:[phaseLabel(value.displayPhase??value.phase),context,nativeContext].filter(Boolean).join(' · ')});
}
function workSummary(value) {
  const work=activeWorkView(value),operation=work?.operation;
  const imported=unityImportView(value);
  if(work?.unity){
    const label=operation?phaseLabel('operation:'+operation):t('activeWorkCurrent');
    return [label,t('unityWorkSummary',{done:work.unity.done,total:work.unity.total}),
      work.unity.active?t('unityWorkCurrent',{phase:t('unityWork_'+work.unity.active)}):'',imported?importSummary(imported):''].filter(Boolean).join(' · ');
  }
  if(imported){
    const owner=typeof value.stageOperation==='string'?phaseLabel('operation:'+value.stageOperation):t('activeWorkCurrent');
    return [owner,imported.done===null?t('activeWorkUnknown'):counters({done:imported.done,total:imported.total,unit:'assets'})].join(' · ');
  }
  if(!work)return t('activeWorkUnknown');
  const label=operation?phaseLabel('operation:'+operation):t('activeWorkCurrent');
  const number=value=>new Intl.NumberFormat(language).format(value);
  const lines=[t('activeWorkCompleted',{operation:label,done:number(work.done),total:number(work.total),remaining:number(work.total-work.done)})];
  if(work.index)lines.push(t('activeWorkCheckpoint',{index:work.index,total:work.total,checkpoint:phaseLabel('prepare-substage:'+work.checkpoint)}));
  if(work.passes)lines.push(t(work.passes.known?'activeWorkPasses':'buildPassObserved',{done:work.passes.done,total:work.passes.total,remaining:work.passes.total-work.passes.done}));
  return lines.join(' · ');
}
function memorySummary(memory) {
  if(!memory)return '';
  const lines=[];
  if(memory.compilerProfile){const key='memoryProfile_'+memory.compilerProfile;lines.push(t(memory.phase.startsWith('asset-')?'memoryCodecProfile':'memoryProfile',{profile:t(key)===key?memory.compilerProfile:t(key)}));}
  if(memory.jobs)lines.push(t(memory.jobs===1?'memoryJob':'memoryJobs',{jobs:memory.jobs}));
  if(memory.attempt)lines.push(t('memoryAttempt',{attempt:memory.attempt}));
  if(memory.reason){const key='memoryReason_'+memory.reason;lines.push(t(key)===key?memory.reason:t(key));}
  for(const [name,bytes] of Object.entries(memory.resources??{})){
    const gib=bytes>=1073741824,size=new Intl.NumberFormat(language,{maximumFractionDigits:1}).format(bytes/(gib?1073741824:1048576))+(gib?' GiB':' MiB');
    lines.push(t('memory_'+name,{size}));
  }
  return lines.join(' · ');
}
function importSummary(imported) {
  if(!imported)return '';
  const counts=imported.done===null?'':imported.scope==='coverage'&&imported.total!==null
    ?t('counterAssetCoverage',{done:new Intl.NumberFormat(language).format(imported.done),total:new Intl.NumberFormat(language).format(imported.total)})
    :counters({done:imported.done,total:imported.total,unit:'assets'});
  return [imported.scope?t(imported.scope==='coverage'?'importScopeCoverage':'importScopeNative'):'',counts,imported.total===null?t('importTotalUnknown'):'',imported.elapsedSeconds===null?'':t(imported.status==='running'?'importElapsed':'importElapsedRecorded',{duration:durationText(imported.elapsedSeconds)})].filter(Boolean).join(' · ');
}
function renderTiming(current,resourceWait=false,connectionWait=false) {
  const whole=timingView(state?.timing),stage=timingView(current?.timing),estimate=stage?.estimate;
  $('build-timing').hidden=!whole&&!stage;
  $('elapsed-label').textContent=t(whole?.elapsedBasis==='since-update'?'elapsedSinceUpdate':'elapsedBuild');
  $('elapsed-value').textContent=whole?durationText(whole.elapsedSeconds):t('timingUnavailable');
  $('stage-elapsed').hidden=!stage;
  $('stage-elapsed').textContent=stage?t(stage.elapsedBasis==='since-update'?'elapsedStageSinceUpdate':'elapsedStage',{duration:durationText(stage.elapsedSeconds)}):'';
  $('eta-label').textContent=t(estimate?.scope==='conversion-batches'?'etaBatches':'etaPhase');
  let text;
  if(state?.status==='complete')text=t('etaComplete');
  else if(resourceWait)text=t('memoryWaitEta');
  else if(connectionWait)text=t('etaPaused');
  else if(estimate?.status==='estimated')text=t('etaRange',{lower:durationText(estimate.lowerSeconds),upper:durationText(estimate.upperSeconds)});
  else text=t(estimate?.status==='complete'?'etaComplete':estimate?.status==='paused'?'etaPaused':estimate?.status==='learning'?'etaLearning':'etaUnknown');
  $('eta-value').textContent=text;
  $('eta-value').title=t('etaScopeHint');
}
function overviewList(rows,prefix,children) {
  const list=document.createElement('ol');list.className='build-operation-list';
  for(const row of rows){
    const item=document.createElement('li');item.className=row.status;item.dataset.operation=row.id;
    const name=document.createElement('span');name.textContent=t(prefix+row.id)+(row.conditional?' · '+t('buildPassConditional'):'');
    const status=document.createElement('small');status.textContent=t('overview_'+row.status)+(row.closed||row.percent>0?' · '+t('measuredPercent',{percent:percentText(row.closed?100:row.percent)}):'')+(row.counter&&!row.closed?' · '+counters(row.counter):'');
    item.append(name,status);
    const child=children?.(row);
    if(child){item.classList.add('has-subplan');item.append(child);}
    list.append(item);
  }
  return list;
}
function preparationDetail(operation,openPlans) {
  const data=operation.preparation,compiler=operation.compiler,imported=operation.import,packed=operation.pack,api=operation.api,validation=operation.validation,task=operation.unityTask,unityWork=operation.unityWork,updateCode=operation.updateCode;
  if(!data&&!compiler&&!imported&&!packed&&!api&&!validation&&!task&&!unityWork&&!updateCode)return null;
  const container=document.createElement('div');container.className='preparation-detail';
  if(updateCode){
    const plan=document.createElement('details');plan.className='build-subplan';plan.dataset.plan='update-code';plan.open=openPlans.has(plan.dataset.plan)||operation.status==='running';
    const label=document.createElement('summary');label.textContent=t('updateCodeSummary',{done:updateCode.done,total:updateCode.total});
    const bar=document.createElement('progress');bar.max=100;bar.value=updateCode.percent;bar.dataset.updateCode='true';bar.setAttribute('aria-label',t('updateCodeSummary',{done:updateCode.done,total:updateCode.total}));
    plan.append(label,bar,overviewList(updateCode.passes,'updateCode_',row=>{
      if(row.id!=='update-code'||!updateCode.preparation)return null;
      const prepared=updateCode.preparation,nested=document.createElement('div');nested.dataset.updatePreparation='true';
      const summary=document.createElement('p');summary.className='hint';summary.textContent=t('updatePreparationSummary',{done:prepared.done,total:prepared.total});
      const progress=document.createElement('progress');progress.max=100;progress.value=prepared.percent;progress.setAttribute('aria-label',t('phase_update-code-preparation'));
      nested.append(summary,progress,overviewList(prepared.passes,'phase_'));return nested;
    }));container.append(plan);
  }
  if(unityWork){
    const plan=document.createElement('details');plan.className='build-subplan';plan.dataset.plan='unity-work-'+operation.id;
    plan.open=openPlans.has(plan.dataset.plan)||operation.status==='running';
    const label=document.createElement('summary');label.textContent=t('unityWorkSummary',{done:unityWork.done,total:unityWork.total});
    const bar=document.createElement('progress');bar.max=100;bar.value=unityWork.percent;
    bar.setAttribute('aria-label',phaseLabel('operation:'+operation.id)+' · '+t('unityWorkTotal'));
    const measured=document.createElement('p');measured.className='hint';measured.textContent=t('measuredPercent',{percent:percentText(unityWork.percent)})+' · '+t('unityWorkScope');
    plan.append(label,bar,measured,overviewList(unityWork.phases,'unityWork_',row=>{
      if(!row.parts?.length)return null;
      const children=document.createElement('div');children.append(overviewList(row.parts,'unityWork_',part=>{
        if(!part.activities?.length&&!part.localCounter&&part.id!=='shaders')return null;
        const scoped=document.createElement('div');
        if(part.id==='shaders'&&['player','update-code'].includes(operation.id)){const note=document.createElement('p');note.className='hint';note.textContent=t('unityShaderSourceScope');scoped.append(note);}
        if(part.id==='native'){const note=document.createElement('p');note.className='hint';note.textContent=t('unityNativeGraphScope');scoped.append(note);}
        if(part.activities?.length)scoped.append(overviewList(part.activities,'unityWork_'));
        if(part.localCounter){const local=document.createElement('p');local.className='hint';local.textContent=t('unityNativeLocalScope')+' · '+counters(part.localCounter)+' · '+workDetail(part.localCounter);scoped.append(local);}
        return scoped;
      }));return children;
    }));container.append(plan);
  }
  for(const [record,id,key] of [[packed,'content-pack','contentPackSummary'],[api,'package-api','packageApiSummary'],[unityWork?null:validation,'unity-validation','unityValidationSummary']])if(record){
    const plan=document.createElement('details');plan.className='build-subplan';plan.dataset.plan=id;
    plan.open=openPlans.has(plan.dataset.plan);
    const label=document.createElement('summary');label.textContent=t(key,{done:record.done,total:record.total});
    plan.append(label,overviewList(record.passes,'phase_'));container.append(plan);
  }
  if(task){
    const box=document.createElement('section');box.className='unity-task '+task.status;box.dataset.taskOwner=operation.id;
    const measured=document.createElement('p');measured.className='hint';measured.textContent=[phaseLabel(task.phase),counters(task),task.elapsedSeconds===null?'':t('unityTaskElapsed',{duration:durationText(task.elapsedSeconds)})].filter(Boolean).join(' · ');
    box.append(measured);
    if(task.detail){const detail=document.createElement('p');detail.className='hint';detail.textContent=workDetail(task);box.append(detail);}
    container.append(box);
  }
  if(imported){
    const box=document.createElement('section');box.className='unity-import '+imported.status;box.dataset.importOwner=operation.id;
    const measured=document.createElement('p');measured.className='hint';measured.textContent=importSummary(imported);box.append(measured);
    if(imported.detail){const action=document.createElement('p');action.className='hint';action.textContent=imported.detail;box.append(action);}
    container.append(box);
  }
  if(data){
    const plan=document.createElement('details');plan.className='build-subplan';plan.dataset.plan='prepare-'+operation.id;
    plan.open=openPlans.has(plan.dataset.plan);
    const label=document.createElement('summary');label.textContent=t('buildCheckpointSummary',{done:data.done,total:data.total,remaining:data.total-data.done});
    plan.append(label,overviewList(data.checkpoints,'phase_prepare-substage:',checkpoint=>{
      if(checkpoint.detail==='aggregate'){
        const aggregate=document.createElement('p');aggregate.className='hint';aggregate.textContent=t('buildPreparationAggregate');return aggregate;
      }
      if(!checkpoint.passes.length)return null;
      const part=document.createElement('details');part.className='build-subplan';part.dataset.plan='passes-'+checkpoint.id;
      part.open=openPlans.has(part.dataset.plan);
      const summary=document.createElement('summary');summary.textContent=t(checkpoint.passPlanKnown?'buildPassSummary':'buildPassObserved',{done:checkpoint.passDone,total:checkpoint.passTotal,remaining:checkpoint.passTotal-checkpoint.passDone});
      part.append(summary,overviewList(checkpoint.passes,'phase_prepare-items:'));
      if(checkpoint.nestedCounter){
        const nested=document.createElement('p');nested.className='hint';nested.textContent=phaseLabel(checkpoint.nestedCounter.phase)+' · '+counters(checkpoint.nestedCounter);part.append(nested);
      }
      return part;
    }));container.append(plan);
  }
  if(compiler){
    const box=document.createElement('section');box.className='unity-compiler '+compiler.status;box.dataset.compilerOwner=operation.id;
    const overall=compiler.aggregate;
    const title=document.createElement('strong');title.textContent=t('phase_unity-shader-overall');
    const scope=document.createElement('p');scope.className='hint';scope.textContent=t(overall?.scope==='sources'?'unityShaderSourceScope':overall?.scope==='banks'?'shaderBankScope':'shaderCallerScope');
    const percentage=overall?.percent??null;
    const status=document.createElement('p');status.className='hint';status.textContent=[t('overview_'+(overall?.status??compiler.status)),overall?.counter?counters(overall.counter):'',percentage===null?t('phaseUnknown'):t('phasePercent',{percent:percentText(percentage)})].filter(Boolean).join(' · ');
    const bar=document.createElement('progress');bar.max=100;bar.dataset.shaderOverall='true';bar.setAttribute('aria-label',t('phase_unity-shader-overall')+' · '+scope.textContent);
    if(percentage!==null)bar.value=percentage;
    const detail=document.createElement('p');detail.className='hint';detail.textContent=workDetail(compiler);
    box.append(title,scope,status,bar,detail);
    const local=compiler.currentPass??(overall?null:compiler);
    if(local&&overall?.status!=='complete'){
      const current=document.createElement('p');current.className='hint';current.textContent=[t(local.scope==='task'?'shaderTaskScope':'shaderPassScope'),local.counter?counters(local.counter):'',local.percent===null?t('phaseUnknown'):t('phasePercent',{percent:percentText(local.percent)}),t('overview_'+local.status)].filter(Boolean).join(' · ');
      const pass=document.createElement('progress');pass.max=100;pass.dataset.shaderPass='true';pass.setAttribute('aria-label',t('shaderPassScope'));
      if(local.percent!==null)pass.value=local.percent;
      box.append(current,pass);
    }
    container.append(box);
  }
  return container;
}
function renderBuildOverview() {
  const overview=buildOverviewView(state);$('build-overview').hidden=!overview;
  const focus=Boolean(overview&&state.stages?.find(row=>row.id==='build')?.status!=='pending');
  if(focus!==buildFocus){$('setup-overview').open=!focus;buildFocus=focus;}
  if(!overview)return;
  $('build-overview-title').textContent=t(['update-mod','update-profile'].includes(overview.workflow)?'updateOverviewTitle':'buildOverviewTitle');
  const openPlans=new Set([...$('build-groups').querySelectorAll('details[data-plan][open]')].map(node=>node.dataset.plan));
  $('build-groups').replaceChildren();
  $('build-overview-count').textContent=t('buildOverviewCount',{done:overview.done,total:overview.total,remaining:overview.total-overview.done});
  $('build-current').textContent=t('buildCurrent',{operation:overview.active?phaseLabel('operation:'+overview.active):t('waiting')});
  $('build-update-migration').hidden=!overview.updateMigration;
  const retained=overview.groups.some(group=>group.operations.some(row=>['retained','checking'].includes(row.status)));
  $('build-retained').hidden=!retained;
  for(const group of overview.groups){
    const active=group.operations.some(row=>row.id===overview.active),done=group.operations.filter(row=>row.closed).length;
    const card=document.createElement('details');card.className='build-group'+(active?' active':'');card.dataset.group=group.id;
    card.open=buildGroupChoices.has(group.id)?buildGroupChoices.get(group.id):active;
    const heading=document.createElement('summary');
    const name=document.createElement('strong');name.textContent=t('buildGroup_'+group.id);
    const status=document.createElement('span');status.textContent=[t(active?'overviewCurrent':done===group.operations.length?'overviewSaved':'overviewRemaining'),done+' / '+group.operations.length].join(' · ');heading.append(name,status);heading.addEventListener('click',()=>buildGroupChoices.set(group.id,!card.open));card.append(heading);
    card.append(overviewList(group.operations,'phase_operation_',operation=>preparationDetail(operation,openPlans)));
    if(group.id==='recovery'){
      const data=overview.recovery;
      if(data.batches){const packages=document.createElement('p');packages.className='hint';packages.textContent=t('buildBatchSummary',data.batches);card.append(packages);}
      const subplan=(kind,rows,prefix,children)=>{
        const nested=document.createElement('details');nested.className='build-subplan';nested.dataset.plan=kind;nested.open=openPlans.has(kind);
        const label=document.createElement('summary');label.textContent=t(kind==='sections'?'buildRecoverySummary':'buildStagingSummary',{done:rows.filter(row=>row.closed).length,total:rows.length});nested.append(label,overviewList(rows,prefix,children));
        const current=rows.find(row=>['running','checking','failed'].includes(row.status));
        if(current){const line=document.createElement('p');line.className='hint';line.textContent=t('buildCurrent',{operation:t(prefix+current.id)});nested.append(line);}
        return nested;
      };
      if(data.sections.length)card.append(subplan('sections',data.sections,'recoveryOverview_',row=>{
        if(row.id!=='staging')return null;
        let child;
        if(data.staging.length)child=subplan('staging',data.staging,'phase_staging-section:');
        else if(data.stagingDetail==='aggregate'){
          child=document.createElement('p');child.className='hint staging-aggregate';child.textContent=t('buildStagingAggregate');
        }
        const counter=data.stagingCounter;
        if(counter&&Number.isFinite(counter.done)&&Number.isFinite(counter.total)){
          child??=document.createElement('div');const line=document.createElement('p');line.className='hint';line.textContent=counters(counter);child.append(line);
        }
        return child;
      }));
    }
    $('build-groups').append(card);
  }
}
function renderGames() {
  const linux=discovery?.host?.system==='Linux';
  $('game-root').placeholder=linux?'…/steamapps/common/Gloomhaven':'C:\\…\\Gloomhaven';
  $('unity-editor').placeholder=linux?'…/Editor/Unity':'…\\Unity.exe';
  $('base-apk').placeholder=linux?'…/GloomhavenVR-Quest.apk':'C:\\…\\GloomhavenVR-Quest.apk';
  $('declare-dlc').nextElementSibling.textContent=t(linux?'declareDlcLinux':'declareDlc');
  document.querySelector('#dlc-declaration .hint').textContent=t(linux?'dlcDeclarationLinuxHint':'dlcDeclarationHint');
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
}
function updateView() {
  const previous=state?.modSource,current=discovery?.modSource;
  const sourceUpdate=state&&page===2&&!isActive(state)&&state.status!=='complete'&&
    !state.stages?.some(row=>row.id==='build'&&row.status==='complete')&&
    !state.choices?.sourceRoot&&!state.choices?.sourceCommit&&!state.choices?.sourceRef&&
    previous?.sourceCommit&&current?.sourceCommit&&previous.sourceCommit!==current.sourceCommit;
  const source=page===-1?current:sourceUpdate?current:previous??current;
  const version=typeof source?.modVersion==='string'?source.modVersion:'';
  const build=Number.isInteger(source?.modBuild)?'B'+source.modBuild:'';
  const commit=typeof source?.sourceCommit==='string'&&/^[a-f0-9]{40}$/i.test(source.sourceCommit)?source.sourceCommit.slice(0,10):'';
  $('mod-source').hidden=typeof page!=='number'||!source||![version,build,commit].some(Boolean);
  $('mod-source').textContent=[version?'GloomhavenVR '+version:'GloomhavenVR',build,commit,t(source?.kind==='bundled-release'?'modSourceRelease':'modSourceCheckout')].filter(Boolean).join(' · ');
  $('mod-source').title=t('modSourceHint');
  $('source-update').hidden=!sourceUpdate;
  $('source-update').textContent=sourceUpdate?t('sourceUpdate',{commit:previous.sourceCommit.slice(0,10)}):'';
  $('resume-notice').hidden=page!==2||!resumeFailed&&restoredSession!==state?.session;
  $('resume-notice').textContent=t(resumeFailed?'resumeUnavailable':state?.status==='complete'?'resumeCompleted':isActive(state)?'resumeActive':'resumeRestored');
  const estimate=qualification?.spaceEstimate;
  $('space-estimate').hidden=!estimate;
  if(estimate){$('space-estimate').textContent=t(estimate.scanBounded?'spacePartial':qualification.spaceWarning?'spaceLow':'spaceEstimate',{required:Math.ceil(estimate.additionalEstimatedBytes/1073741824),free:Math.floor(qualification.freeBytes/1073741824)});$('space-estimate').classList.toggle('warning',qualification.spaceWarning===true);}
  $('game-page').hidden=page!==0;$('setup-page').hidden=page!==1;$('progress-page').hidden=page!==2;
  renderHome();renderDevice();renderPackage();renderSupport();
  const step=page===2 ? macroStep(state) : page;
  document.querySelectorAll('[data-step]').forEach(node => {const index=Number(node.dataset.step);node.classList.toggle('active',index===step);node.classList.toggle('done',index<step);if(index===step)node.setAttribute('aria-current','step');else node.removeAttribute('aria-current');});
  $('back').hidden=page!==1;$('cancel').hidden=page!==2||!isActive(state);
  $('new-build').hidden=page!==2||!(resumeFailed||state&&!isActive(state));$('new-build').disabled=busy||storageActive();$('new-build').title=t('newBuildHint');
  $('cancel').disabled=busy||['cancelling','cancel_requested'].includes(state?.status);
  $('cancel').textContent=t(['cancelling','cancel_requested'].includes(state?.status)?'cancelling':'cancel');
  $('profile-summary').textContent=t($('profile-name').value?'profileManual':'profileCheck');
  $('selected-review').textContent=$('provider').value.toUpperCase()+' · '+$('game-root').value;
  const mode=$('build-mode').value;
  $('profile-name-hint').hidden=mode!=='update-profile';
  document.querySelector('label[for=profile-name]').textContent=t(mode==='update-profile'?'profileUpdateName':'profileName');
  document.querySelector('label[for=profile-id]').textContent=t(mode==='update-profile'?'profileUpdateId':'profileId');
  const unityLabel=document.querySelector('label[for=unity-editor]');unityLabel.hidden=mode==='update-profile';unityLabel.nextElementSibling.hidden=mode==='update-profile';
  $('base-apk-choice').hidden=mode==='build';
  const workspaceSource=discovery?.defaultApk?.path??'';
  const chosenSource=$('base-apk').value.trim();
  $('base-apk-default').hidden=mode==='build';
  $('base-apk-default').textContent=t(chosenSource&&chosenSource!==automaticBaseApk&&chosenSource!==workspaceSource?'baseApkOverride':workspaceSource?'baseApkWorkspace':'baseApkChoose',{path:chosenSource||workspaceSource});
  const actionTitle=mode==='update-mod'?'modOperationTitle':mode==='update-profile'?'profileOperationTitle':'gameTitle';
  document.querySelector('#game-page h2').textContent=t(actionTitle);
  document.querySelector('#setup-page h2').textContent=t(mode==='build'?'setupTitle':actionTitle);
  // The homepage owns operation selection; an opened workflow shows its own
  // task rather than advertising unrelated operations inside the form.
  $('build-mode').hidden=true;document.querySelector('label[for=build-mode]').hidden=true;
  $('update-signing-choice').hidden=mode==='build';
  document.querySelector('.license-note').hidden=mode==='update-profile';
  const updateContent=mode==='update-profile'?'setupProfile':mode==='update-mod'?'setupMod':'setupContent';
  document.querySelector('[data-i18n=setupContent]').textContent=t(updateContent);
  document.querySelector('[data-i18n=setupContentCopy]').textContent=t(updateContent+'Copy');
  document.querySelector('[data-i18n=firstBuild]').textContent=t(mode==='build'?'firstBuild':'updateBuild');
  $('browse-apk').hidden=discovery?.capabilities?.browse===false;
  $('primary').hidden=typeof page!=='number'||page<0;
  $('primary').disabled=typeof page!=='number'||page<0||busy||storageActive()||deviceActive()||packageActive()||!canConnect||Boolean(resumeFailed)||(page===0&&!$('game-root').value.trim())||(page===2&&(isActive(state)||state?.status==='complete'));
  $('primary').firstElementChild.textContent=t(busy?'preparing':page===0?'continue':page===1?'build':isActive(state)?'working':state?.status==='complete'?'done':state?.status==='blocked'?'retry':'resumeContinue');
  if(page===2&&!busy&&installationView(state)?.attention)$('primary').firstElementChild.textContent=t('installationRetry');
  $('primary').lastElementChild.textContent=state?.status==='complete'?'✓':'→';
  if(page===1&&mode!=='build'&&!busy)$('primary').firstElementChild.textContent=t('startUpdate');
  if(page===2)renderProgress();
  renderStorage();
}
function renderProgress() {
  if(!state)return;
  const active=isActive(state),stopped=['failed','cancelled','interrupted'].includes(state.status),blocked=state.status==='blocked',done=state.status==='complete';
  if(state.requestError)error(state.requestError);
  const failure=failureView(state);
  const updateFailure=updateFailureView(state);
  const installation=installationView(state),installIssue=installation?.attention===true,connectionWait=installation?.connectionWaiting===true,manualWait=installation?.manualLaunchWaiting===true;
  // Show connection setup as soon as installation begins, before a missing
  // device can become an error. Signing/corrupt-package failures retain their
  // distinct cause rather than sending the owner through unrelated USB steps.
  $('install-guide').hidden=!installation||state.stages.find(row=>row.id==='install')?.status==='complete'||installIssue&&!installation.connectionRequired;
  $('install-guide').querySelector('[data-i18n=questGuideStart]').textContent=t(connectionWait?'questGuideStartLiveWait':'questGuideStart');
  // A retained prerequisite record must never replace a confirmed terminal
  // failure with a misleading "waiting for user" presentation.
  const waiting=(active||blocked)?state.stages?.find(row=>row.waiting)?.waiting:null;
  $('action-needed-title').textContent=t(waiting||blocked?'actionNeeded':'failureTitle');
  $('progress-title').textContent=t(updateFailure?.newBuild?'updateRebuildPausedTitle':failure?.blocked?'blockedTitle':failure?'failureTitle':waiting?'actionNeeded':done?'completeTitle':blocked?'blockedTitle':stopped?'stoppedTitle':state.status==='ready'?'readyTitle':'progressTitle');
  $('progress-copy').textContent=t(updateFailure?.newBuild?'updateRebuildPausedCopy':failure?.workspace?'workspaceMetadataBlockedCopy':failure?.blocked?'nativeMemoryBlockedCopy':failure?'failureCopy':done?'completeCopy':blocked?'blockedCopy':stopped?'stoppedCopy':state.status==='ready'?'readyCopy':'progressCopy');
  if(installIssue){
    $('progress-title').textContent=t(installation.connectionRequired?'questConnectionTitle':installation.recoveryRequired?'installationRecoveryTitle':'installationFailedTitle');
    $('progress-copy').textContent=t(installation.connectionRequired?'questConnectionCopy':installation.recoveryRequired?'installationRecoveryCopy':'installationFailedCopy');
    $('action-needed-title').textContent=t(installation.connectionRequired?'questConnectionNeeded':installation.recoveryRequired?'installationRecoveryAction':'installationFailedAction');
    if(installation.cleanInstallRequired){
      $('progress-title').textContent=t('installationCleanTitle');
      $('progress-copy').textContent=t('installationCleanCopy');
      $('action-needed-title').textContent=t('installationCleanAction');
    }else if(installation.contentReinstallRequired){
      $('progress-title').textContent=t('installationContentTitle');
      $('progress-copy').textContent=t('installationContentCopy');
      $('action-needed-title').textContent=t('actionNeeded');
    }else if(installation.dataIssue){
      const saves=installation.dataIssue==='save-data-inaccessible';
      $('progress-title').textContent=t(saves?'installationSaveWaitTitle':'installationDataChoiceTitle');
      $('progress-copy').textContent=t(saves?'installationSaveWaitCopy':'installationDataChoiceCopy');
      $('action-needed-title').textContent=t(saves?'installationSaveWaitAction':'installationDataChoiceAction');
    }else if(installation.repairWait){
      const restoring=installation.repairWait.reason==='restore-app-access';
      const storage=installation.repairWait.reason==='restore-app-storage-unavailable';
      const launch=installation.repairWait.reason==='restore-app-launch-unavailable';
      $('progress-title').textContent=t(launch?'installationLaunchWaitTitle':storage?'installationStorageWaitTitle':restoring?'installationRestoreWaitTitle':'installationRepairWaitTitle');
      $('progress-copy').textContent=t(launch?'installationLaunchWaitCopy':storage?'installationStorageWaitCopy':restoring?'installationRestoreWaitCopy':'installationRepairWaitCopy');
      $('action-needed-title').textContent=t(launch?'installationLaunchWaitAction':storage?'installationStorageWaitAction':'installationRepairWaitAction');
    }
  }else if(installation&&active){
    $('progress-title').textContent=t(manualWait?'installationManualWaitingTitle':connectionWait?'questConnectionWaitingTitle':installation.recoveryActive?'installationRecoveryRunningTitle':'installationRunningTitle');
    $('progress-copy').textContent=t(manualWait?'installationManualWaitingCopy':connectionWait?'questConnectionWaitingCopy':installation.recoveryActive?'installationRecoveryRunningCopy':'installationRunningCopy');
    if(installation.cleanInstallActive&&!connectionWait&&!manualWait){
      $('progress-title').textContent=t('installationCleanRunningTitle');
      $('progress-copy').textContent=t('installationCleanRunningCopy');
    }
    if(connectionWait)$('action-needed-title').textContent=t('questConnectionNeeded');
    if(manualWait)$('action-needed-title').textContent=t('installationManualWaitingAction');
  }
  const progress=progressView(state);
  if(!logSelectionManual&&(progress.active??progress.current)?.id)$('log-stage').value=(progress.active??progress.current).id;
  $('phase-label').textContent=t(done?'done':t('stage_'+progress.phase)==='stage_'+progress.phase?'waiting':'stage_'+progress.phase);
  $('progress-count').textContent=t('phasePercent',{percent:percentText(progress.percent)});
  const track=$('progress-track');track.classList.toggle('indeterminate',progress.indeterminate);
  track.setAttribute('aria-valuemin','0');track.setAttribute('aria-valuemax','100');
  track.setAttribute('aria-valuenow',String(progress.percent));
  track.setAttribute('aria-valuetext',$('progress-count').textContent);$('progress-fill').style.width=progress.width+'%';
  const sub=stageProgress(progress.active??progress.current);
  const imported=unityImportView(sub);
  const memory=nativeMemoryView(progress.active??progress.current),resourceWait=active&&memory?.waiting===true;
  // Exhausted transfer methods are not a successfully repaired backup. Keep
  // their actual counts in the detail line, without a misleading100% repair bar.
  const hasSubstep=!done&&!manualWait&&!installation?.repairWait&&sub.phase&&!['pending','starting','complete'].includes(sub.phase);
  $('progress-detail').textContent=[progress.retaining?t('retainedProgress'):'',!hasSubstep&&sub.phase?substepLabel(sub):'',imported?importSummary(imported):counters(sub),memory?memorySummary(memory):imported?.detail??workDetail(sub),waiting?t('waitingSince',{seconds:Math.max(0,Math.floor(Date.now()/1000-waiting.since))}):''].filter(Boolean).join(' · ');
  if(manualWait){
    const owner=state.session+':'+state.stages.find(row=>row.id==='install')?.attempts;
    if(manualLaunchWaitSince?.owner!==owner)manualLaunchWaitSince={owner,since:Date.now()};
    $('progress-detail').textContent=t('installationManualWaitingElapsed',{seconds:Math.max(0,Math.floor((Date.now()-manualLaunchWaitSince.since)/1000))});
  }else manualLaunchWaitSince=null;
  $('progress-completed').hidden=done;
  $('progress-completed').textContent=workSummary(sub);
  renderTiming(progress.current,resourceWait,connectionWait);
  const activity=activityView(progress.active??progress.current);
  $('activity-status').hidden=!active||Boolean(waiting);
  $('activity-status').classList.toggle('quiet',!resourceWait&&!connectionWait&&activity?.quiet===true);
  $('activity-status').textContent=t(manualWait?'installationManualWaitingActivity':connectionWait?'questConnectionWaitingActivity':resourceWait?'memoryWaitActivity':activity?.measured?(activity.seconds===null?'activityOnly':'activityMeasured'):activity?.quiet?'activityQuiet':activity?'activityRecent':'activityUnknown',{duration:durationText(activity?.seconds??0),activity:durationText(activity?.activitySeconds??0)});
  $('log-status').hidden=!eventsUnavailable&&!stageLogUnavailable;$('log-status').textContent=t('logUnavailable');
  renderBuildOverview();
  $('substep-progress').hidden=!hasSubstep;
  $('substep-label').textContent=substepLabel(sub);
  $('substep-count').textContent=resourceWait?t('memoryWaiting'):sub.phasePercent===null?counters(sub)||t(connectionWait?'questConnectionWaitingActivity':'phaseUnknown'):t('phasePercent',{percent:percentText(sub.phasePercent)});
  const subtrack=$('substep-track');subtrack.classList.toggle('indeterminate',sub.phasePercent===null&&active&&!waiting&&!resourceWait&&!connectionWait);
  subtrack.setAttribute('aria-valuemin','0');subtrack.setAttribute('aria-valuemax','100');
  if(sub.phasePercent===null)subtrack.removeAttribute('aria-valuenow');else subtrack.setAttribute('aria-valuenow',String(sub.phasePercent));
  subtrack.setAttribute('aria-valuetext',$('substep-count').textContent);$('substep-fill').style.width=(sub.phasePercent??0)+'%';
  $('stage-list').replaceChildren();
  for(const stage of state.stages??[]) {
    const displayed=stageStatus(state,stage),li=document.createElement('li');li.className=displayed;
    const marker=document.createElement('span');marker.className='stage-marker';marker.textContent=displayed==='complete'?'✓':displayed==='running'?'·':['blocked','failed'].includes(displayed)?'!':'';
    const label=document.createElement('span');label.className='stage-content';const title=document.createElement('strong');title.textContent=t('stage_'+stage.id);label.append(title);
    const measured=stageProgress(stage),stageMemory=nativeMemoryView(stage),detail=document.createElement('small');detail.textContent=[measured.phase?substepLabel(measured):'',['pending','complete'].includes(stage.status)?'':counters(measured),measured.waiting?t('actionNeeded'):['pending','complete'].includes(stage.status)?'':stageMemory?memorySummary(stageMemory):workDetail(measured)].filter(Boolean).join(' · ');label.append(detail);
    const bar=document.createElement('progress');bar.max=100;bar.setAttribute('aria-label',t('stage_'+stage.id)+' · '+t('stageTotal'));bar.value=measured.percent;label.append(bar);
    const status=document.createElement('span');status.className='stage-state';status.textContent=t('measuredPercent',{percent:percentText(measured.percent)})+' · '+t(stage.id==='install'&&connectionWait?'waiting':measured.waiting?'blocked':displayed);li.append(marker,label,status);$('stage-list').append(li);
  }
  const action=active&&!waiting?null:state.needsActions?.[0];$('action-needed').hidden=!action&&!blocked&&!stopped&&!connectionWait&&!manualWait;
  $('action-needed').classList.toggle('failure',Boolean(failure)&&!installation?.connectionRequired);
  $('action-needed').classList.toggle('installation',installIssue||connectionWait||manualWait);
  const retainedWait=Boolean(installation?.dataIssue||installation?.repairWait||installation?.contentReinstallRequired);
  $('action-needed').setAttribute('role',installation?.connectionRequired||retainedWait?'status':failure||installIssue?'alert':'status');
  const cause=retainedWait||installation?.connectionRequired||connectionWait?null:installIssue?installation.cause:failure?.cause;
  $('failure-cause').hidden=!cause;
  $('failure-cause').textContent=cause?t('failureCause',{cause}):'';
  $('failure-location').hidden=!failure?.workspacePath;
  $('failure-location').textContent=failure?.workspacePath?t('workspaceMetadataPath',{path:failure.workspacePath}):'';
  $('failure-next').hidden=retainedWait||!failure&&!installIssue&&!connectionWait;$('failure-actions').hidden=!failure&&!installIssue;
  $('failure-next').textContent=t(connectionWait?'questConnectionWaitingNext':installIssue?installation.connectionRequired?'questConnectionNext':installation.cleanInstallRequired?'installationCleanNext':installation.recoveryRequired?'installationRecoveryNext':'installationNext':failure?.blocked?'nativeMemoryNext':'failureNext');
  if(retainedWait)$('failure-next').textContent='';
  if(failure?.workspace)$('failure-next').textContent=t('workspaceMetadataNext');
  else if(updateFailure)$('failure-next').textContent=message(updateFailure.nextSteps);
  if(installation?.backupPath&&!retainedWait)$('failure-next').textContent+=' '+t('installationRecoveryBackup',{path:installation.backupPath});
  $('failure-retry').textContent=t(installation?.dataIssue==='non-save-inaccessible'?'installationRetryAll':installIssue?'installationRetry':'failureRetry');
  const locked=busy||actionBusy||storageActive()||packageActive()||active||!canConnect||Boolean(resumeFailed);
  $('failure-retry').disabled=locked;
  $('failure-edit-inputs').hidden=!updateFailure?.editInputs;
  $('failure-edit-inputs').disabled=locked;
  $('failure-new-build').hidden=!updateFailure?.newBuild;
  $('failure-new-build').disabled=locked;
  $('install-skip').hidden=!installation?.canSkip;$('install-skip').disabled=locked;
  $('install-keep-saves').hidden=!installation?.canUseSaves;$('install-keep-saves').disabled=locked;
  $('install-content-reinstall').hidden=!installation?.canReinstallContent;$('install-content-reinstall').disabled=locked||deviceActive();
  $('install-manual-launch').hidden=!installation?.canManualLaunch;$('install-manual-launch').disabled=locked||deviceActive();
  $('install-reset-app-data').hidden=!installation?.canResetAppData;$('install-reset-app-data').disabled=locked||deviceActive();
  $('failure-support').hidden=preview||discovery?.capabilities?.support!==true;
  $('failure-support').disabled=supportBusy||!sessionId(state.session);
  if(manualWait)$('action-needed-copy').textContent=t('installationManualWaitingCopy');
  else if(connectionWait)$('action-needed-copy').textContent=t('questConnectionWaitingCopy');
  else if(action||blocked||stopped){const blockedStage=state.stages?.find(stage=>stage.status==='blocked');const code=typeof action==='string'?action:action?.code??blockedStage?.details?.needsAction;
    const hint=action?.message?message(action.message):blockedStage?.details?.message?message(blockedStage.details.message):t('action_'+code)!=='action_'+code?t('action_'+code):t('unknownAction');
    const phase=action?.parameters?.failureStage;const stageName=phase?t('phase_stage_'+phase):action?.stage?t('stage_'+action.stage):progress.current?t('stage_'+progress.current.id):'';
    $('action-needed-copy').textContent=installation?.contentReinstallRequired
      ?t('installationContentNext')+(Number.isFinite(installation.contentBytes)?' '+t('installationContentSize',{size:storageSize(installation.contentBytes)}):'')
      :installation?.dataIssue
      ?t(installation.dataIssue==='save-data-inaccessible'?'installationSaveWaitNext':'installationDataChoiceNext')
      :installation?.repairWait
      ?t('installationRepairReason_'+installation.repairWait.reason)+' '+t(installation.repairWait.reason==='restore-app-launch-unavailable'?'installationLaunchWaitNext':installation.repairWait.reason==='restore-app-storage-unavailable'?'installationStorageWaitNext':installation.repairWait.reason==='restore-app-access'?'installationRestoreWaitNext':'installationRepairWaitNext')
      :(stageName?stageName+': ':'')+hint;}
  if(installation?.canManualLaunch||installation?.canResetAppData){
    $('action-needed-copy').textContent=t(installation.canManualLaunch&&installation.canResetAppData?'installationRestoreChoicesNext':
      installation.canManualLaunch?'installationRestoreManualOnlyNext':'installationRestoreResetOnlyNext');
    if(installation.backupPath)$('action-needed-copy').textContent+=' '+t('installationRecoveryBackup',{path:installation.backupPath});
  }
  if(pendingRestoreChoice&&(state.session!==pendingRestoreChoice.session||installation?.restoreActionNonce!==pendingRestoreChoice.nonce))closeRestoreChoiceDialog();
  if(pendingUpdateRebuild&&(state.session!==pendingUpdateRebuild.session||!updateFailure?.newBuild))closeUpdateRebuildDialog();
  const failureKey=installIssue?state.session+':install:'+installation.cause:failure?state.session+':'+failure.stage+':'+failure.cause:'';
  if(failureKey&&failureKey!==lastFailureFocus){$('action-needed').focus({preventScroll:true});$('action-needed').scrollIntoView({block:'nearest'});}
  lastFailureFocus=failureKey;
  $('unity-actions').hidden=!waiting||installIssue;$('edit-choices').hidden=active||installIssue||Boolean(updateFailure)||Boolean(failure?.workspace);
  for(const id of ['unity-open','unity-check'])$(id).disabled=actionBusy;
  $('result-card').hidden=!done&&!installation;
  const result=installation?.paths??state.result??state.stages?.find(stage=>stage.id==='build')?.details??{};
  $('result-path').textContent=[result.apk,result.handoff,result.outputRoot].filter(value=>typeof value==='string').join('\n')||t('resultAvailable');
  $('session-detail').textContent=t('session',{session:state.session??''});$('event-log').textContent=log.slice(-50).join('\n');
  $('save-support').hidden=preview||discovery?.capabilities?.support!==true;$('save-support').disabled=supportBusy||!sessionId(state?.session);
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
  $('build-mode').value=choices.mode??'build';$('base-apk').value=choices.baseApk??'';automaticBaseApk='';$('signing-mode').value=choices.signingMode??'auto';
  $('signing-root').value=choices.signingRoot??'';
  $('game-root').value=choices.gameRoot??'';$('provider').value=choices.provider??'steam';$('unity-editor').value=choices.unityEditor??'';
  $('profile-name').value=choices.profile?.displayName??choices.profileName??'';$('profile-id').value=choices.profile?.providerId??'';
  $('unity-terms').checked=choices.acceptUnityTerms===true;$('install-choice').checked=choices.install!==false;
  $('declare-dlc').checked=Array.isArray(choices.ownedDlc);$('dlc-declaration').hidden=!$('declare-dlc').checked;
  document.querySelectorAll('input[name=dlc]').forEach(node=>node.checked=choices.ownedDlc?.includes(node.value)??false);
  selected=discovery?.games?.find(game=>game.gameRoot===choices.gameRoot&&game.provider===choices.provider)??null;
}
async function openSession(session) {
  if(!sessionId(session)||busy)return false;clearError();busy=true;clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try {const result=await api.status(session);if(!result.state||result.state.session!==session)throw {code:'invalidReply'};
    resetArtwork();clearLoadedLog();eventsUnavailable=false;stageLogUnavailable=false;state=result.state;resumeFailed=null;restoredSession=session;restoreChoices(state.choices);workflowMode=state.choices?.mode??'build';workflowPage=2;page=2;after=0;log=[];schedulePoll();return true;}
  catch(value){resumeFailed=session;error(value);return false;}finally{busy=false;renderGames();updateView();}
}
function schedulePoll() {
  clearTimeout(pollTimer);
  const current=state?.stages?.find(row=>row.status==='running'),phase=current?.progress?.phase??'';
  // Read real native updates more frequently; no timer changes their measured
  // percentages. Other phases keep the lower-frequency status cadence.
  const interval=phase.startsWith('unity-')||phase.startsWith('bee-actions:')?500:1000;
  if(state?.session)pollTimer=setTimeout(poll,interval);
}
async function poll() {
  clearTimeout(pollTimer);
  if(!state?.session)return;
  const session=state.session,generation=sessionGeneration;
  try {
    const result=await api.status(session);if(generation!==sessionGeneration||state?.session!==session)return;
    if(!result.state||result.state.session!==session)throw {code:'invalidReply'};
    // Status is authoritative. A failed/slow log endpoint used to discard a
    // successful status response and leave the screen claiming RUNNING.
    state=result.state;clearError();updateView();void refreshCompletedDiscovery();
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
  if(typeof page!=='number'||page<0||busy||actionBusy||resumeFailed||storageActive()||deviceActive()||packageActive()||isActive(state))return;
  clearError();
  if(page===0){busy=true;updateView();try{const selectedChoices=choicesFromForm(form(),language);
    if(discovery?.capabilities?.spaceEstimate===true){qualification=await api.qualify(selectedChoices.gameRoot,selectedChoices.mode,selectedChoices.baseApk);}
    page=1;updateView();const heading=document.querySelector('#setup-page h2');heading.setAttribute('tabindex','-1');heading.focus();}catch(value){error(typeof value?.message==='string'?{code:value.message}:value);}finally{busy=false;updateView();}return;}
  busy=true;clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try {
    if(page===1){const choices=choicesFromForm(form(),language);if(choices.mode!=='update-profile'&&!choices.acceptUnityTerms)throw {code:'missingTerms'};
      const result=await api.plan(choices,state?.session);if(!sessionId(result.session)||!result.state)throw {code:'invalidReply'};clearLoadedLog();state=result.state;page=2;after=0;log=[];void refreshRuns(true);}
    await api.run(state.session);const result=await api.status(state.session);state=result.state;await poll();schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
async function skipInstallation() {
  if(busy||actionBusy||storageActive()||isActive(state)||!canConnect||resumeFailed||!installationView(state)?.canSkip)return;
  busy=true;clearError();clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try{
    const result=await api.skipInstall(state.session);
    if(result?.event!=='status'||result.session!==state.session||result.state?.session!==state.session||result.state.status!=='complete')throw {code:'invalidReply'};
    state=result.state;schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
async function keepSavesAndInstall() {
  const installation=installationView(state);
  if(busy||actionBusy||storageActive()||isActive(state)||!canConnect||resumeFailed||!installation?.canUseSaves)return;
  const session=state.session,nonce=installation.actionNonce;
  busy=true;clearError();clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try{
    const result=await api.installDataPolicy(session,nonce);
    if(result?.event!=='status'||result.session!==session||result.state?.session!==session||
       result.state.installationRecoveryPolicy?.policy!=='saves')throw {code:'invalidReply'};
    state=result.state;
    await api.run(session);const current=await api.status(session);state=current.state;await poll();schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
async function allowContentReinstall(){
  const installation=installationView(state);
  if(busy||actionBusy||storageActive()||deviceActive()||isActive(state)||!canConnect||resumeFailed||!installation?.canReinstallContent)return;
  const session=state.session,nonce=installation.contentActionNonce;
  busy=true;clearError();clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try{
    const result=await api.installContentReinstall(session,nonce);
    if(result?.event!=='status'||result.session!==session||result.state?.session!==session)throw {code:'invalidReply'};
    state=result.state;
    await api.run(session);const current=await api.status(session);state=current.state;await poll();schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
function closeRestoreChoiceDialog(){
  pendingRestoreChoice=null;
  if($('install-restore-dialog').open)$('install-restore-dialog').close();
  $('install-reset-acknowledged').checked=false;
}
function renderRestoreChoiceDialog(){
  if(!pendingRestoreChoice)return;
  const reset=pendingRestoreChoice.choice==='reset-app-data';
  $('install-restore-dialog').dataset.choice=pendingRestoreChoice.choice;
  $('install-restore-dialog-title').textContent=t(reset?'installationResetConfirmTitle':'installationManualConfirmTitle');
  $('install-restore-dialog-copy').textContent=t(reset?'installationResetConfirmCopy':'installationManualConfirmCopy');
  $('install-manual-instructions').hidden=reset;
  $('install-reset-acknowledgment').hidden=!reset;
  $('install-restore-confirm').textContent=t(reset?'installationResetConfirm':'installationManualConfirm');
  $('install-restore-confirm').disabled=reset&&!$('install-reset-acknowledged').checked;
}
function showRestoreChoiceDialog(choice){
  const installation=installationView(state),available=choice==='manual-launch'?installation?.canManualLaunch:choice==='reset-app-data'?installation?.canResetAppData:false;
  if(!available||busy||actionBusy||storageActive()||deviceActive()||packageActive()||isActive(state)||!canConnect||resumeFailed)return;
  pendingRestoreChoice={session:state.session,nonce:installation.restoreActionNonce,choice};
  $('install-reset-acknowledged').checked=false;
  $('install-restore-dialog-error').hidden=true;
  renderRestoreChoiceDialog();$('install-restore-dialog').showModal();
  // Reset is never the default action or preselected acknowledgment. Returning
  // to the protected installation is the initial keyboard focus for that choice.
  $(choice==='reset-app-data'?'install-restore-cancel':'install-restore-confirm').focus();
}
async function confirmRestoreChoice(){
  const requested=pendingRestoreChoice,installation=installationView(state);
  const available=requested?.choice==='manual-launch'?installation?.canManualLaunch:requested?.choice==='reset-app-data'?installation?.canResetAppData:false;
  if(!available||state.session!==requested?.session||installation.restoreActionNonce!==requested.nonce||
     requested.choice==='reset-app-data'&&!$('install-reset-acknowledged').checked||
     busy||actionBusy||storageActive()||deviceActive()||packageActive()||isActive(state)||!canConnect||resumeFailed){closeRestoreChoiceDialog();return;}
  const {session,nonce,choice}=requested;
  closeRestoreChoiceDialog();busy=true;clearError();clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();updateView();
  try{
    const result=await api.installRestoreChoice(session,nonce,choice);
    if(result?.event!=='status'||result.session!==session||result.state?.session!==session)throw {code:'invalidReply'};
    state=result.state;
    await api.run(session);const current=await api.status(session);state=current.state;await poll();schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
async function browse(kind) {
  busy=true;clearError();updateView();try{const result=await api.browse(kind);if(result.path){$(({game:'game-root',unity:'unity-editor',apk:'base-apk'})[kind]).value=result.path;if(kind==='game')selected=null;if(kind==='apk')automaticBaseApk='';}}
  catch(value){error(value);}finally{busy=false;updateView();}
}
async function browsePackage(kind){
  if(packageActive()||busy)return;
  busy=true;clearError();updateView();try{const result=await api.browse(kind==='source'?'apk':'export');if(result.path){$('package-'+(kind==='source'?'source':'destination')).value=result.path;if(kind==='source')automaticPackageSource='';else automaticPackageDestination='';}}
  catch(value){error(value);}finally{busy=false;updateView();}
}
document.querySelectorAll('[data-language]').forEach(node=>node.addEventListener('click',()=>setLanguage(node.dataset.language)));
$('storage-plan-duplicates').addEventListener('click',()=>planStorage('duplicates'));
$('storage-plan-cache').addEventListener('click',()=>planStorage('build-cache'));
$('storage-refresh').addEventListener('click',inventoryStorage);
$('storage-inventory-cancel').addEventListener('click',()=>void cancelStorage('inventory'));
$('storage-cancel').addEventListener('click',()=>void cancelStorage('cleanup'));
$('storage-retry').addEventListener('click',()=>{const value=storageInfo?.state?.selection;if(value?.mode)void planStorage(value.mode,value.session??undefined,value.removeData===true);});
$('storage-runs-refresh').addEventListener('click',()=>void refreshRuns());
$('storage-run').addEventListener('change',()=>{$('storage-run-keep').checked=true;updateView();});
$('storage-run-plan').addEventListener('click',()=>{
  const session=$('storage-run').value;
  if(!normalRuns().some(row=>stoppedRun(row)&&row.session===session)&&!retiredRuns().some(row=>row.session===session))return;
  void planStorage('session',session,$('storage-run-remove-data').checked);
});
$('storage-clean').addEventListener('click',cleanStorage);
$('storage-dismiss').addEventListener('click',()=>{if(storageActive())return;storageGeneration++;storageFailure=null;storageFailureOperationId=null;storageInfo={...storageInfo,state:{status:'idle'}};updateView();});
$('primary').addEventListener('click',primary);$('back').addEventListener('click',()=>{page=0;clearError();updateView();});
function previousApkSelection(){
  // A typed/browsed or restored selection is the user's explicit override.
  // Otherwise use a just-completed result, then the owner's discovered APK.
  // Starting a workflow never resumes/builds that artifact by itself.
  const currentApk=$('base-apk').value,explicitApk=currentApk&&currentApk!==automaticBaseApk?currentApk:'';
  const completedBuild=state?.stages?.find(row=>row.id==='build'&&row.status==='complete');
  const path=explicitApk||(state?.status==='complete'?state.result?.apk:'')||completedBuild?.details?.apk||discovery?.defaultApk?.path||'';
  return {path,explicit:Boolean(explicitApk)};
}
function preselectUpdateApk(){
  if($('build-mode').value==='build')return;
  const selected=previousApkSelection();
  $('base-apk').value=selected.path;automaticBaseApk=selected.explicit?'':selected.path;
}
function beginBuild(mode='build',force=false,prefill=null){
  if(busy||isActive(state)||storageActive()||deviceActive()||packageActive())return;
  if(!force&&workflowMode===mode&&state?.status!=='complete'){navigatePage(workflowPage,workflowPage===0?'game-root':workflowPage===1?'setup-page':'progress-title');preselectUpdateApk();updateView();return;}
  const previousApk=previousApkSelection();
  // This is an explicit opt-out. Removing a UI selection never deletes the
  // backend session, completed receipts or owner recovery/download workspace.
  clearTimeout(pollTimer);sessionGeneration++;stopOptionalReads();state=null;resumeFailed=null;restoredSession=null;qualification=null;page=0;after=0;log=[];
  restoreChoices(prefill?{...prefill,mode,baseApk:'',signingRoot:'',signingMode:'auto'}:{install:true});resetArtwork();clearLoadedLog();clearError();renderGames();
  $('build-mode').value=mode;workflowMode=mode;workflowPage=0;
  if(mode!=='build'&&previousApk.path){$('base-apk').value=previousApk.path;automaticBaseApk=previousApk.explicit?'':previousApk.path;}
  if(mode==='update-profile')document.querySelector('#game-page .advanced').open=true;
  if(!prefill?.gameRoot&&discovery?.games?.length===1)selectGame(discovery.games[0]);
  updateView();$('game-root').focus();
}
$('new-build').addEventListener('click',()=>updateFailureView(state)?.newBuild?showUpdateRebuildDialog():beginBuild('build',true));
function closeUpdateRebuildDialog(){
  pendingUpdateRebuild=null;
  if($('update-rebuild-dialog').open)$('update-rebuild-dialog').close();
}
function renderUpdateRebuildDialog(){
  if(pendingUpdateRebuild)$('update-rebuild-reason').textContent=pendingUpdateRebuild.cause;
}
function showUpdateRebuildDialog(){
  if(!updateFailureView(state)?.newBuild||busy||actionBusy||storageActive()||deviceActive()||packageActive()||isActive(state))return;
  pendingUpdateRebuild={session:state.session,cause:failureView(state)?.cause??''};
  renderUpdateRebuildDialog();$('update-rebuild-dialog').showModal();$('update-rebuild-cancel').focus();
}
function confirmUpdateRebuild(){
  if(!pendingUpdateRebuild||pendingUpdateRebuild.session!==state?.session||!updateFailureView(state)?.newBuild||
     busy||actionBusy||storageActive()||deviceActive()||packageActive()||isActive(state)){closeUpdateRebuildDialog();return;}
  const retained=structuredClone(state.choices);
  closeUpdateRebuildDialog();beginBuild('build',true,retained);
}
$('failure-new-build').addEventListener('click',showUpdateRebuildDialog);
$('update-rebuild-confirm').addEventListener('click',confirmUpdateRebuild);
$('update-rebuild-cancel').addEventListener('click',closeUpdateRebuildDialog);
$('update-rebuild-dialog').addEventListener('cancel',closeUpdateRebuildDialog);
$('home-build').addEventListener('click',()=>beginBuild());
function beginModUpdate(){
  const firstSelection=workflowMode!=='update-mod'||state?.status==='complete',explicit=previousApkSelection().explicit;
  beginBuild('update-mod');
  const recommendation=discovery?.updateRecommendation;
  // Apply the qualified suggestion only when opening a fresh update workflow.
  // Returning from Home keeps an already edited workflow and manual overrides.
  if(!firstSelection||explicit||page!==0||workflowMode!=='update-mod'||recommendation?.available!==true||recommendation?.inputsReady!==true)return;
  if(typeof recommendation.apkPath==='string'&&recommendation.apkPath){$('base-apk').value=recommendation.apkPath;automaticBaseApk=recommendation.apkPath;}
  if(typeof recommendation.gameRoot==='string'&&recommendation.gameRoot){
    const game=discovery.games?.find(row=>row.gameRoot===recommendation.gameRoot);
    if(game)selectGame(game);
    else{$('game-root').value=recommendation.gameRoot;selected=null;renderGames();}
  }
  updateView();
}
$('home-mod').addEventListener('click',beginModUpdate);
$('home-profile').addEventListener('click',()=>beginBuild('update-profile'));
$('home-device').addEventListener('click',()=>navigatePage('device','device-title'));
$('home-storage').addEventListener('click',()=>openStorage());
$('home-runs-refresh').addEventListener('click',()=>refreshRuns(true));
$('home-export').addEventListener('click',focusPackage);$('result-export').addEventListener('click',focusPackage);
$('package-browse-source').addEventListener('click',()=>browsePackage('source'));$('package-browse-destination').addEventListener('click',()=>browsePackage('destination'));
$('package-source').addEventListener('input',()=>{automaticPackageSource='';updateView();});$('package-destination').addEventListener('input',()=>{automaticPackageDestination='';});
$('package-export').addEventListener('click',exportPackage);
$('package-cancel').addEventListener('click',async()=>{try{packageInfo=await api.packageCancel();}catch(value){error(value);}finally{updateView();schedulePackagePoll();}});
$('home').addEventListener('click',()=>navigatePage(-1,'home-title'));
$('home-active-open').addEventListener('click',openHomeActivity);
$('device-connect').addEventListener('click',()=>deviceOperation('connect'));
$('device-wireless').addEventListener('click',()=>deviceOperation('wireless'));
$('device-start').addEventListener('click',()=>deviceOperation('start'));
$('device-capture').addEventListener('click',()=>deviceOperation('capture'));
$('device-storage-preview').addEventListener('click',()=>deviceOperation('storage-preview'));
$('device-storage-clean').addEventListener('click',openDeviceCleanupDialog);
$('device-storage-confirm').addEventListener('click',confirmDeviceCleanup);
$('device-storage-dismiss').addEventListener('click',closeDeviceCleanupDialog);
$('device-storage-dialog').addEventListener('cancel',()=>{pendingDeviceCleanup=null;});
$('device-download').addEventListener('click',downloadCapture);
$('device-cancel').addEventListener('click',async()=>{try{const result=await api.deviceCancel();if(result?.event!=='device'||!result.state)throw {code:'invalidReply'};deviceInfo=result;}catch(value){error(value);}finally{updateView();scheduleDevicePoll();}});
function editRunChoices(){if(busy||actionBusy||isActive(state)||storageActive()||deviceActive()||packageActive())return;clearTimeout(pollTimer);restoreChoices(state?.choices);page=0;clearError();updateView();$('game-root').focus();}
$('edit-choices').addEventListener('click',editRunChoices);
$('failure-edit-inputs').addEventListener('click',editRunChoices);
function renderSupport(){
  const available=!preview&&(discovery?.capabilities?.workspaceSupport===true||discovery?.capabilities?.support===true);
  const workspace=discovery?.capabilities?.workspaceSupport===true;
  $('export-support').hidden=preview;
  $('export-support').disabled=supportBusy||!canConnect||!available||!workspace&&!sessionId(state?.session);
  $('export-support').textContent=t(supportBusy?'logsExporting':'exportLogs');
  for(const id of ['save-support','failure-support'])$(id).disabled=supportBusy||!sessionId(state?.session);
  $('support-status').hidden=!supportBusy&&!supportFailure&&!supportResult;
  $('support-status').setAttribute('role',supportFailure?'alert':'status');
  $('support-status').textContent=supportFailure?t('logsExportFailed',{message:supportFailure.message?message(supportFailure.message):t(supportFailure.code??'offline')}):supportBusy?t('logsExporting'):supportResult?t('logsExported',{name:supportResult}):'';
}
async function saveSupport(workspace=false) {
  if(supportBusy||!canConnect)return;
  const session=workspace&&discovery?.capabilities?.workspaceSupport===true?undefined:sessionId(state?.session)?state.session:undefined;
  if(session===undefined&&discovery?.capabilities?.workspaceSupport!==true)return;
  supportBusy=true;supportFailure=null;supportResult=null;renderSupport();
  try {const result=await api.support(session);downloadBlob(result);supportResult=result.name;}
  catch(value){supportFailure=value;}finally{supportBusy=false;renderSupport();}
}
$('export-support').addEventListener('click',()=>saveSupport(true));
$('save-support').addEventListener('click',()=>saveSupport());
$('failure-support').addEventListener('click',()=>saveSupport());
$('failure-retry').addEventListener('click',primary);
$('install-skip').addEventListener('click',skipInstallation);
$('install-keep-saves').addEventListener('click',keepSavesAndInstall);
$('install-content-reinstall').addEventListener('click',allowContentReinstall);
$('install-manual-launch').addEventListener('click',()=>showRestoreChoiceDialog('manual-launch'));
$('install-reset-app-data').addEventListener('click',()=>showRestoreChoiceDialog('reset-app-data'));
$('install-reset-acknowledged').addEventListener('change',renderRestoreChoiceDialog);
$('install-restore-confirm').addEventListener('click',confirmRestoreChoice);
$('install-restore-cancel').addEventListener('click',closeRestoreChoiceDialog);
$('install-restore-dialog').addEventListener('cancel',()=>{pendingRestoreChoice=null;$('install-reset-acknowledged').checked=false;});
$('log-stage').addEventListener('change',()=>{logSelectionManual=true;if($('live-log').checked)refreshLog();});
document.querySelector('#diagnostic-details').addEventListener('toggle',()=>{if(document.querySelector('#diagnostic-details').open&&$('live-log').checked)refreshLog();});
$('load-log').addEventListener('click',()=>void refreshLog());
document.querySelector('.skip').addEventListener('click',event=>{event.preventDefault();$('workspace').focus();});
$('browse-game').addEventListener('click',()=>browse('game'));$('browse-unity').addEventListener('click',()=>browse('unity'));
$('browse-apk').addEventListener('click',()=>browse('apk'));$('build-mode').addEventListener('change',()=>{preselectUpdateApk();updateView();});$('base-apk').addEventListener('input',()=>{automaticBaseApk='';updateView();});
$('cancel').addEventListener('click',async()=>{busy=true;updateView();try{await api.cancel(state.session);const result=await api.status(state.session);state=result.state;schedulePoll();}catch(value){error(value);}finally{busy=false;updateView();}});
$('game-root').addEventListener('input',()=>{selected=null;resetArtwork();updateView();});$('provider').addEventListener('change',()=>{selected=null;resetArtwork();updateView();});$('profile-name').addEventListener('input',updateView);
$('declare-dlc').addEventListener('change',()=>{$('dlc-declaration').hidden=!$('declare-dlc').checked;});
window.addEventListener('beforeunload',()=>{navigationObserver.disconnect();clearTimeout(packagePollTimer);clearTimeout(devicePollTimer);clearTimeout(storagePollTimer);clearTimeout(pollTimer);clearTimeout(slideTimer);clearTimeout(artPollTimer);stopOptionalReads();blobUrls.forEach(url=>URL.revokeObjectURL(url));});
$('preview-notice').hidden=!preview;setLanguage(language);
if(canConnect){pollGallery();void refreshStorage();}
busy=true;updateView();
try{discovery=await api.discover();renderGames();if(discovery.games?.length===1)selectGame(discovery.games[0]);
  const editor=discovery.unityEditors?.find(row=>row.version==='2021.3.5f1'&&row.androidSupport);if(editor)$('unity-editor').value=editor.path;
  if(discovery.capabilities?.deviceTools===true)void refreshDevice();
  if(discovery.capabilities?.portablePackages===true)void refreshPackage();
  if(discovery.capabilities?.runOverview===true||discovery.capabilities?.runRemoval===true)void refreshRuns();
  // No automatic cache enumeration at launch. A previous measurement is shown
  // with its date; a fresh scan is an explicit storage-management action.
}catch(value){$('discovery-status').textContent=t('notFound');error(value);}finally{busy=false;preselectUpdateApk();renderGames();updateView();}
