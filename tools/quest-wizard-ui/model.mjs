export const stageIds = ['tools','source','unity','profile','inspect','build','install'];
export function choicesFromForm(form, language) {
  const gameRoot = String(form.gameRoot ?? '').trim();
  if (!gameRoot) throw new Error('missingGame');
  const provider = ['steam','epic','gog'].includes(form.provider) ? form.provider : 'steam';
  const choices = {gameRoot, provider, install:Boolean(form.install), acceptUnityTerms:Boolean(form.acceptUnityTerms), language};
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
  const current = stages.find(row => row.status === 'running') ?? stages.find(row => ['blocked','failed','interrupted','cancelled'].includes(row.status)) ?? stages.find(row=>row.status==='pending');
  // A native phase can finish and restart inside one stage. Its percentage is
  // never a substitute for the durable, planned whole-stage percentage.
  const percent = state?.status === 'complete' ? 100 : stageProgress(current).percent;
  return {completed, total, phase:current?.id ?? state?.progress?.phase ?? '', percent,
    width:percent, indeterminate:false, current};
}
export function macroStep(state) {
  const phase = progressView(state).phase;
  if (state?.status === 'complete' || phase === 'install') return 3;
  if (phase === 'build') return 2;
  return 1;
}
export function isActive(state) { return ['running','cancelling','cancel_requested'].includes(state?.status); }
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
  const percent=stage?.status==='complete'?100:stage?.status==='pending'?0:
    typeof raw==='number'&&Number.isFinite(raw)&&raw>=0&&raw<=100?Math.min(99.9,raw):0;
  const phaseRaw=value.percent;
  const phasePercent=typeof phaseRaw==='number'&&Number.isFinite(phaseRaw)&&phaseRaw>=0&&phaseRaw<=100?phaseRaw:null;
  return {...value,percent,phasePercent,waiting:stage?.waiting??null};
}
export function activeWorkView(progress) {
  const work=progress?.activeWork;
  if(!work||!Number.isSafeInteger(work.done)||!Number.isSafeInteger(work.total)||work.total<1||work.done<0||work.done>work.total)return null;
  const operation=typeof work.operation==='string'&&/^[a-z0-9-]{1,64}$/.test(work.operation)?work.operation:null;
  return {done:work.done,total:work.total,operation};
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
