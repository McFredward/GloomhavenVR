import {translate} from './i18n.mjs';
import {choicesFromForm,progressView,macroStep,isActive,artworkUrl,sessionId} from './model.mjs';
import {LocalApi,PreviewApi} from './transport.mjs';

const $ = id => document.getElementById(id);
const preview = new URLSearchParams(location.search).get('preview') === '1';
const fragment = location.hash.slice(1), token = fragment.startsWith('token=') ? new URLSearchParams(fragment).get('token') : fragment;
const api = preview ? new PreviewApi() : new LocalApi(location.origin,token);
let language = (preview ? new URLSearchParams(location.search).get('lang') : null) ?? readStorage('quest-wizard-language') ?? (navigator.language.startsWith('de') ? 'de' : 'en');
if (!['de','en'].includes(language)) language='de';
let page=0,selected=null,discovery=null,state=null,busy=false,after=0,log=[],loadedLog=null,pollTimer=null,canConnect=preview||Boolean(token),artworkKey='',artworkGeneration=0;
const blobUrls=[];
const galleryTemplate=[...$('gallery').children].map(node=>node.cloneNode(true));
function readStorage(key) { try { return localStorage.getItem(key); } catch { return null; } }
function writeStorage(key,value) { try { localStorage.setItem(key,value); } catch { } }
function t(key,parameters) { return translate(language,key,parameters); }
function message(value,fallback='unknownAction') { return typeof value==='string' ? value : value?.[language] ?? value?.en ?? value?.de ?? t(fallback); }
function error(value) { $('error-banner').textContent=value?.message ? message(value.message) : t(value?.code ?? 'offline');$('error-banner').hidden=false; }
function clearError() { $('error-banner').hidden=true; }
function clearLoadedLog() {loadedLog=null;$('stage-log').textContent='';$('stage-log').hidden=true;$('log-truncated').hidden=true;}
function setLanguage(value) {
  language=value;writeStorage('quest-wizard-language',value);document.documentElement.lang=value;
  document.querySelectorAll('[data-i18n]').forEach(node => node.textContent=t(node.dataset.i18n));
  document.querySelectorAll('[data-i18n-aria]').forEach(node=>node.setAttribute('aria-label',t(node.dataset.i18nAria)));
  document.querySelectorAll('[data-language]').forEach(node => node.setAttribute('aria-pressed',String(node.dataset.language===value)));
  $('gallery-note').textContent=t($('gallery').querySelector('img')?'ownedArtwork':preview?'artPlaceholder':'artReady');
  $('gallery').querySelectorAll('img').forEach(node=>node.alt=t('ownedArtwork'));
  if(discovery) renderGames();updateView();
}
function form() { return {gameRoot:$('game-root').value,provider:$('provider').value,unityEditor:$('unity-editor').value,
  profileName:$('profile-name').value,profileId:$('profile-id').value,declareDlc:$('declare-dlc').checked,
  ownedDlc:[...document.querySelectorAll('input[name=dlc]:checked')].map(node => node.value),
  acceptUnityTerms:$('unity-terms').checked,install:$('install-choice').checked}; }
function selectGame(game) {
  if(selected?.id!==game.id)resetArtwork();
  selected=game;$('game-root').value=game.gameRoot;$('provider').value=game.provider;
  renderGames();updateView();loadArtwork(game.artwork);
}
function resetArtwork() {
  artworkGeneration++;artworkKey='';blobUrls.splice(0).forEach(url=>URL.revokeObjectURL(url));
  $('gallery').replaceChildren(...galleryTemplate.map(node=>node.cloneNode(true)));
  $('gallery').querySelectorAll('[data-i18n]').forEach(node=>node.textContent=t(node.dataset.i18n));
  $('gallery-note').textContent=t(preview?'artPlaceholder':'artReady');
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
  const resume=discovery?.recentSessions?.find(row => sessionId(row.session));
  if(resume&&!state) {
    const button=document.createElement('button');button.className='secondary';button.id='resume-session';button.textContent=t('resume');
    button.addEventListener('click',()=>openSession(resume.session));$('game-list').append(button);
  }
}
function updateView() {
  $('game-page').hidden=page!==0;$('setup-page').hidden=page!==1;$('progress-page').hidden=page!==2;
  const step=page===2 ? macroStep(state) : page;
  document.querySelectorAll('[data-step]').forEach(node => {const index=Number(node.dataset.step);node.classList.toggle('active',index===step);node.classList.toggle('done',index<step);if(index===step)node.setAttribute('aria-current','step');else node.removeAttribute('aria-current');});
  $('back').hidden=page!==1;$('cancel').hidden=page!==2||!isActive(state);
  $('cancel').disabled=busy||['cancelling','cancel_requested'].includes(state?.status);
  $('cancel').textContent=t(['cancelling','cancel_requested'].includes(state?.status)?'cancelling':'cancel');
  $('profile-summary').textContent=t($('profile-name').value?'profileManual':'profileCheck');
  $('selected-review').textContent=$('provider').value.toUpperCase()+' · '+$('game-root').value;
  $('primary').disabled=busy||!canConnect||(page===0&&!$('game-root').value.trim())||(page===2&&(isActive(state)||state?.status==='complete'));
  $('primary').firstElementChild.textContent=t(busy?'preparing':page===0?'continue':page===1?'build':isActive(state)?'working':state?.status==='complete'?'done':'retry');
  $('primary').lastElementChild.textContent=state?.status==='complete'?'✓':'→';
  if(page===2)renderProgress();
}
function renderProgress() {
  if(!state)return;
  const active=isActive(state),stopped=['failed','cancelled'].includes(state.status),blocked=state.status==='blocked',done=state.status==='complete';
  $('progress-title').textContent=t(done?'completeTitle':blocked?'blockedTitle':stopped?'stoppedTitle':state.status==='ready'?'readyTitle':'progressTitle');
  $('progress-copy').textContent=t(done?'completeCopy':blocked?'blockedCopy':stopped?'stoppedCopy':state.status==='ready'?'readyCopy':'progressCopy');
  const progress=progressView(state);
  $('phase-label').textContent=t('stage_'+progress.phase);
  $('progress-count').textContent=t(progress.percent===null?'phaseSteps':'phasePercent',progress);
  const track=$('progress-track');track.classList.toggle('indeterminate',progress.indeterminate);
  track.setAttribute('aria-valuemin','0');track.setAttribute('aria-valuemax','100');
  if(progress.percent!==null)track.setAttribute('aria-valuenow',String(progress.percent));else track.removeAttribute('aria-valuenow');
  track.setAttribute('aria-valuetext',$('progress-count').textContent);$('progress-fill').style.width=progress.width+'%';
  $('progress-detail').textContent=state.progress?.message ? message(state.progress.message) : active?t('firstBuild'):t(stopped?'stoppedCopy':'footerNote');
  $('stage-list').replaceChildren();
  for(const stage of state.stages??[]) {
    const li=document.createElement('li');li.className=stage.status;
    const marker=document.createElement('span');marker.className='stage-marker';marker.textContent=stage.status==='complete'?'✓':stage.status==='running'?'·':['blocked','failed'].includes(stage.status)?'!':'';
    const label=document.createElement('span');label.textContent=t('stage_'+stage.id);
    const status=document.createElement('span');status.className='stage-state';status.textContent=t(stage.status);li.append(marker,label,status);$('stage-list').append(li);
  }
  const action=state.needsActions?.[0];$('action-needed').hidden=!action&&!blocked&&!stopped;
  if(action||blocked||stopped){const blockedStage=state.stages?.find(stage=>stage.status==='blocked');const code=typeof action==='string'?action:action?.code??blockedStage?.details?.needsAction;
    $('action-needed-copy').textContent=action?.message?message(action.message):blockedStage?.details?.message?message(blockedStage.details.message):t('action_'+code)!=='action_'+code?t('action_'+code):t('unknownAction');}
  $('result-card').hidden=!done;
  const result=state.result??state.stages?.find(stage=>stage.id==='build')?.details??{};
  $('result-path').textContent=[result.apk,result.handoff,result.outputRoot].filter(value=>typeof value==='string').join('\n')||t('footerNote');
  $('session-detail').textContent=t('session',{session:state.session??''});$('event-log').textContent=log.slice(-50).join('\n');
  $('stage-log-controls').hidden=!preview&&discovery?.capabilities?.logs!==true;
  loadArtwork(state.artwork);
}
async function loadArtwork(artwork) {
  if(!Array.isArray(artwork)||!artwork.length)return;
  const selected=artwork.slice(0,3),key=selected.map(row=>row.id+':'+(row.sha256??'')).join('|');if(key===artworkKey)return;
  artworkKey=key;const generation=++artworkGeneration;
  const cards=[],newUrls=[];
  for(const row of selected) {
    const url=artworkUrl(row.url,location.origin);if(!url)continue;
    try {
      const response=await fetch(url,{headers:{'X-Quest-Token':token},cache:'no-store'});
      if(!response.ok||Number(response.headers.get('Content-Length'))>8*1024*1024||response.headers.get('Content-Type')!=='image/png')continue;
      const blob=await response.blob();if(blob.size>8*1024*1024)continue;
      const blobUrl=URL.createObjectURL(blob);newUrls.push(blobUrl);
      const card=document.createElement('div');card.className='art-card';const image=document.createElement('img');image.src=blobUrl;image.alt=t('ownedArtwork');
      const caption=document.createElement('span');caption.textContent=t('ownedArtwork');card.append(image,caption);cards.push(card);
    }catch{ /* Optional artwork never blocks a genuine build state. */ }
  }
  if(generation!==artworkGeneration){newUrls.forEach(url=>URL.revokeObjectURL(url));return;}
  if(cards.length){blobUrls.splice(0).forEach(url=>URL.revokeObjectURL(url));blobUrls.push(...newUrls);$('gallery').replaceChildren(...cards);$('gallery-note').textContent=t('ownedArtwork');}
  else{newUrls.forEach(url=>URL.revokeObjectURL(url));artworkKey='';}
}
function restoreChoices(choices) {
  if(!choices)return;
  $('game-root').value=choices.gameRoot??'';$('provider').value=choices.provider??'steam';$('unity-editor').value=choices.unityEditor??'';
  $('profile-name').value=choices.profile?.displayName??'';$('profile-id').value=choices.profile?.providerId??'';
  $('unity-terms').checked=choices.acceptUnityTerms===true;$('install-choice').checked=choices.install!==false;
  $('declare-dlc').checked=Array.isArray(choices.ownedDlc);$('dlc-declaration').hidden=!$('declare-dlc').checked;
  document.querySelectorAll('input[name=dlc]').forEach(node=>node.checked=choices.ownedDlc?.includes(node.value)??false);
}
async function openSession(session) {
  if(!sessionId(session))return;clearError();busy=true;updateView();
  try {const result=await api.status(session);resetArtwork();clearLoadedLog();state=result.state;restoreChoices(state.choices);page=2;after=0;log=[];schedulePoll();}
  catch(value){error(value);}finally{busy=false;updateView();}
}
function schedulePoll() {clearTimeout(pollTimer);if(state?.session)pollTimer=setTimeout(poll,1000);}
async function poll() {
  if(!state?.session)return;
  try {
    const result=await api.status(state.session);state=result.state;
    const events=await api.events(state.session,after);
    for(const event of events.events??[])if(Number(event.sequence)>after) {log.push([event.time,event.stage,event.code,event.message?message(event.message):''].filter(Boolean).join(' · '));after=Math.max(after,Number(event.sequence));}
    log=log.slice(-50);clearError();updateView();
  }catch(value){error(value);}
  if(isActive(state))schedulePoll();
}
async function primary() {
  clearError();
  if(page===0){try{choicesFromForm(form(),language);page=1;updateView();const heading=document.querySelector('#setup-page h2');heading.setAttribute('tabindex','-1');heading.focus();}catch(value){error({code:value.message});}return;}
  busy=true;updateView();
  try {
    if(page===1){const choices=choicesFromForm(form(),language);if(!choices.acceptUnityTerms)throw {code:'missingTerms'};
      const result=await api.plan(choices,state?.session);if(!sessionId(result.session)||!result.state)throw {code:'invalidReply'};clearLoadedLog();state=result.state;page=2;after=0;log=[];}
    await api.run(state.session);const result=await api.status(state.session);state=result.state;await poll();schedulePoll();
  }catch(value){error(value);}finally{busy=false;updateView();}
}
async function browse(kind) {
  busy=true;clearError();updateView();try{const result=await api.browse(kind);if(result.path){$(kind==='game'?'game-root':'unity-editor').value=result.path;if(kind==='game')selected=null;}}
  catch(value){error(value);}finally{busy=false;updateView();}
}
document.querySelectorAll('[data-language]').forEach(node=>node.addEventListener('click',()=>setLanguage(node.dataset.language)));
$('primary').addEventListener('click',primary);$('back').addEventListener('click',()=>{page=0;clearError();updateView();});
$('edit-choices').addEventListener('click',()=>{if(isActive(state))return;clearTimeout(pollTimer);restoreChoices(state?.choices);page=0;clearError();updateView();$('game-root').focus();});
$('save-log').addEventListener('click',()=>{const data=JSON.stringify({schema:1,kind:'quest-wizard-ui-log',session:state?.session,status:state?.status,progress:state?.progress,stages:state?.stages,events:log.slice(-50),loadedLog},null,2);const url=URL.createObjectURL(new Blob([data],{type:'application/json'}));const link=document.createElement('a');link.href=url;link.download='quest-wizard-'+(sessionId(state?.session)??'local')+'.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
$('load-log').addEventListener('click',async()=>{const button=$('load-log');button.disabled=true;try{const result=await api.log(state.session,$('log-stage').value);if(typeof result.text!=='string'||result.text.length>65536)throw {code:'invalidReply'};loadedLog={stage:result.stage,text:result.text,truncated:result.truncated===true};$('stage-log').textContent=result.text;$('stage-log').hidden=false;$('log-truncated').hidden=!result.truncated;}catch(value){error(value);}finally{button.disabled=false;}});
document.querySelector('.skip').addEventListener('click',event=>{event.preventDefault();$('workspace').focus();});
$('browse-game').addEventListener('click',()=>browse('game'));$('browse-unity').addEventListener('click',()=>browse('unity'));
$('cancel').addEventListener('click',async()=>{busy=true;updateView();try{await api.cancel(state.session);const result=await api.status(state.session);state=result.state;schedulePoll();}catch(value){error(value);}finally{busy=false;updateView();}});
$('game-root').addEventListener('input',()=>{selected=null;resetArtwork();updateView();});$('provider').addEventListener('change',()=>{selected=null;resetArtwork();updateView();});$('profile-name').addEventListener('input',updateView);
$('declare-dlc').addEventListener('change',()=>{$('dlc-declaration').hidden=!$('declare-dlc').checked;});
window.addEventListener('beforeunload',()=>{clearTimeout(pollTimer);blobUrls.forEach(url=>URL.revokeObjectURL(url));});
$('preview-notice').hidden=!preview;setLanguage(language);
try{discovery=await api.discover();renderGames();if(discovery.games?.length===1)selectGame(discovery.games[0]);
  const editor=discovery.unityEditors?.find(row=>row.version==='2021.3.5f1'&&row.androidSupport);if(editor)$('unity-editor').value=editor.path;
}catch(value){$('discovery-status').textContent=t('notFound');error(value);}updateView();
