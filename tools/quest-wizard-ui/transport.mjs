import {sessionId,stageIds} from './model.mjs';

export class LocalApi {
  constructor(origin, token, fetcher = globalThis.fetch.bind(globalThis)) { this.origin = origin; this.token = token; this.fetcher = fetcher; }
  async request(path, body, signal) {
    if (!this.token) throw {code:'noToken'};
    let response;
    try { response = await this.fetcher(new URL(path,this.origin), {method:body === undefined ? 'GET' : 'POST',
      headers:{'X-Quest-Token':this.token,...(body === undefined ? {} : {'Content-Type':'application/json'})},
      cache:'no-store', credentials:'same-origin', ...(signal ? {signal} : {}), ...(body === undefined ? {} : {body:JSON.stringify(body)})}); }
    catch { throw {code:'offline'}; }
    let value;
    try { value = await response.json(); } catch { throw {code:'invalidReply'}; }
    if (!response.ok || value?.event === 'error') throw value;
    if (value?.schema !== 1) throw {code:'invalidReply'};
    return value;
  }
  async support(session) {
    if (!sessionId(session) || !this.token) throw {code:'invalidReply'};
    const response = await this.fetcher(new URL('/api/support',this.origin), {method:'POST',
      headers:{'X-Quest-Token':this.token,'Content-Type':'application/json'},
      cache:'no-store',credentials:'same-origin',body:JSON.stringify({session})});
    if (!response.ok) { let value; try { value=await response.json(); } catch { throw {code:'invalidReply'}; } throw value; }
    if (response.headers.get('Content-Type') !== 'application/zip') throw {code:'invalidReply'};
    return {blob:await response.blob(),name:response.headers.get('Content-Disposition')?.match(/filename="([A-Za-z0-9_.-]+)"/)?.[1]??'quest-build-support.zip'};
  }
  storage() { return this.request('/api/storage'); }
  packageStatus() { return this.request('/api/package/status'); }
  packageExport(source,destination) { return this.request('/api/package/export',{source,destination}); }
  packageCancel() { return this.request('/api/package/cancel',{}); }
  storageInventory() { return this.request('/api/storage/inventory',{}); }
  deviceStatus() { return this.request('/api/device/status'); }
  deviceConnect(host='',setupWireless=false,serial='') { return this.request('/api/device/connect',{host,setupWireless,serial}); }
  deviceCapture(host='',serial='') { return this.request('/api/device/capture',{host,serial}); }
  deviceCancel() { return this.request('/api/device/cancel',{}); }
  async deviceDownload(id) {
    if(typeof id!=='string'||!/^[a-f0-9]{32}$/.test(id)||!this.token)throw {code:'invalidReply'};
    const response=await this.fetcher(new URL('/api/device/capture?id='+encodeURIComponent(id),this.origin),{headers:{'X-Quest-Token':this.token},cache:'no-store',credentials:'same-origin'});
    if(!response.ok){let value;try{value=await response.json();}catch{throw {code:'invalidReply'};}throw value;}
    if(response.headers.get('Content-Type')!=='application/zip')throw {code:'invalidReply'};
    return {blob:await response.blob(),name:response.headers.get('Content-Disposition')?.match(/filename="([A-Za-z0-9_.-]+)"/)?.[1]??'quest-capture.zip'};
  }
  storagePlan(mode='duplicates') { if(!['duplicates','build-cache'].includes(mode))throw {code:'invalidReply'};return this.request('/api/storage/plan',{mode}); }
  storageClean(planId) { if(typeof planId!=='string'||!planId||planId.length>256)throw {code:'invalidReply'};return this.request('/api/storage/clean',{planId}); }
  discover() { return this.request('/api/discover'); }
  plan(choices, session) { if(session !== undefined && !sessionId(session))throw {code:'invalidReply'};return this.request('/api/plan',{choices,...(session?{session}:{})}); }
  run(session) { return this.request('/api/run',{session}); }
  skipInstall(session) { if(!sessionId(session))throw {code:'invalidReply'};return this.request('/api/install/skip',{session}); }
  installDataPolicy(session,nonce,policy='saves') { if(!sessionId(session)||typeof nonce!=='string'||!/^[a-f0-9]{32}$/.test(nonce)||policy!=='saves')throw {code:'invalidReply'};return this.request('/api/install/data-policy',{session,nonce,policy}); }
  installContentReinstall(session,nonce) { if(!sessionId(session)||typeof nonce!=='string'||!/^[a-f0-9]{32}$/.test(nonce))throw {code:'invalidReply'};return this.request('/api/install/content-reinstall',{session,nonce}); }
  cancel(session) { return this.request('/api/cancel',{session}); }
  log(session,stage,signal) { if(!sessionId(session)||!stageIds.includes(stage))throw {code:'invalidReply'};return this.request('/api/log?session='+encodeURIComponent(session)+'&stage='+stage,undefined,signal); }
  gallery() {return this.request('/api/gallery');}
  action(session,action,nonce) {if(!sessionId(session)||!['unity-open','unity-check'].includes(action)||typeof nonce!=='string'||!/^[a-f0-9]{32}$/.test(nonce))throw {code:'invalidReply'};return this.request('/api/action',{session,action,nonce});}
  qualify(gameRoot,mode='build',baseApk='') { return this.request('/api/qualify',{gameRoot,...(mode==='build'?{}:{mode}),...(mode!=='build'&&baseApk?{baseApk}:{})}); }
  browse(kind) { return this.request('/api/browse',{kind}); }
  status(session) { if (!sessionId(session)) throw {code:'invalidReply'}; return this.request('/api/status?session='+encodeURIComponent(session)); }
  events(session, after,signal) { if(!sessionId(session))throw {code:'invalidReply'};return this.request('/api/events?session='+encodeURIComponent(session)+'&after='+(Number.isInteger(after)&&after>=0?after:0),undefined,signal); }
}

// Deliberate, visibly labelled design preview; never falls through to real tools.
export class PreviewApi {
  async packageStatus(){return {schema:1,event:'package',state:{status:'idle'},defaults:{source:'',destination:'C:\\Users\\Preview\\.ghvrq\\exports\\GloomhavenVR-Quest.zip'}};}
  async packageExport(){throw {code:'preview'};}
  async packageCancel(){throw {code:'preview'};}
  async storage() {return {schema:1,event:'storage',workspaceRoot:'C:\\Users\\Preview\\.ghvrq',freeBytes:150*1073741824,state:this.storageState??{status:'idle'},inventory:{status:'complete',totalBytes:19*1073741824,categories:[{id:'apk-content',bytes:13*1073741824,files:2},{id:'unity-caches',bytes:6*1073741824,files:24000}]}};}
  async storageInventory(){return this.storage();}
  async deviceStatus(){return {schema:1,event:'device',state:{status:'idle',devices:[]}};}
  async deviceConnect(){throw {code:'preview'};}
  async deviceCapture(){throw {code:'preview'};}
  async deviceCancel(){throw {code:'preview'};}
  async deviceDownload(){throw {code:'preview'};}
  async storagePlan(mode='duplicates') {this.storageState={status:'preview',plan:{id:'preview-cleanup',mode,paths:[],bytes:0,files:0,exact:true}};return this.storage();}
  async storageClean() {this.storageState={status:'complete',result:{freedBytes:0,deletedFiles:0}};return this.storage();}
  async gallery() {return {schema:1,event:'gallery',artwork:[]};}
  async action() {throw {code:'preview'};}
  async skipInstall() {throw {code:'preview'};}
  async installDataPolicy() {throw {code:'preview'};}
  async installContentReinstall() {throw {code:'preview'};}
  constructor() { this.sequence=0; this.state=null; this.eventsList=[]; this.tick=0; }
  async discover() { return {schema:1,event:'discovery',capabilities:{browse:false,artwork:false,deviceTools:true,storageInventory:true},games:[
    {id:'preview-owned-game',provider:'steam',gameRoot:'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Gloomhaven',
      displayName:'Gloomhaven',profileAvailable:true,ownedDlc:['jotl','solo'],ownershipComplete:true}],unityEditors:[],unityHubs:[],recentSessions:[]}; }
  async plan(choices) { this.state={session:'preview-local-build',choices,status:'ready',needsActions:[],lastEvent:0,
    stages:['tools','source','unity','profile','inspect','build','install'].map(id => ({id,status:'pending',attempts:0})),
    progress:{completed:0,total:7,phase:'tools',percent:null}}; return {schema:1,event:'planned',session:this.state.session,state:this.state}; }
  async run() { this.state.status='running'; this.tick=0; return {schema:1,event:'started',session:this.state.session}; }
  async cancel() { this.state.status='cancelled';this.state.stages.forEach(row=>{if(row.status==='running')row.status='cancelled';});return {schema:1,event:'cancelled',session:this.state.session}; }
  async log(session,stage) { return {schema:1,event:'log',session,stage,text:'Design preview: no external tools were executed.',truncated:false}; }
  async status() {
    if(this.state?.status==='running') {
      const active=Math.min(5,Math.floor(this.tick/3)); this.tick++;
      this.state.stages.forEach((stage,index) => stage.status=index<active ? 'complete' : index===active ? 'running' : 'pending');
      this.state.progress={completed:active,total:7,phase:this.state.stages[active].id,percent:active===5 ? Math.min(89,20+(this.tick-15)*3) : null};
      if(this.tick===19) { this.state.status='blocked';this.state.stages[5].status='blocked';this.state.needsActions=[{code:'preview-only',message:{de:'Diese Vorschau führt keine Werkzeuge aus. Der echte Wizard arbeitet ausschließlich mit deiner lokalen Spielkopie.',en:'This preview does not run tools. The real wizard uses only your local game copy.'}}]; }
    }
    return {schema:1,event:'status',session:this.state?.session,state:this.state};
  }
  async events() { return {schema:1,event:'events',session:this.state?.session,events:this.eventsList,lastEvent:this.sequence}; }
  async browse() { return {schema:1,event:'browse',path:null}; }
}
