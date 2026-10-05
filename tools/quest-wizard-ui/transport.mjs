import {sessionId,stageIds} from './model.mjs';

export class LocalApi {
  constructor(origin, token, fetcher = globalThis.fetch.bind(globalThis)) { this.origin = origin; this.token = token; this.fetcher = fetcher; }
  async request(path, body) {
    if (!this.token) throw {code:'noToken'};
    let response;
    try { response = await this.fetcher(new URL(path,this.origin), {method:body === undefined ? 'GET' : 'POST',
      headers:{'X-Quest-Token':this.token,...(body === undefined ? {} : {'Content-Type':'application/json'})},
      cache:'no-store', credentials:'same-origin', ...(body === undefined ? {} : {body:JSON.stringify(body)})}); }
    catch { throw {code:'offline'}; }
    let value;
    try { value = await response.json(); } catch { throw {code:'invalidReply'}; }
    if (!response.ok || value?.event === 'error') throw value;
    if (value?.schema !== 1) throw {code:'invalidReply'};
    return value;
  }
  discover() { return this.request('/api/discover'); }
  plan(choices, session) { if(session !== undefined && !sessionId(session))throw {code:'invalidReply'};return this.request('/api/plan',{choices,...(session?{session}:{})}); }
  run(session) { return this.request('/api/run',{session}); }
  cancel(session) { return this.request('/api/cancel',{session}); }
  log(session,stage) { if(!sessionId(session)||!stageIds.includes(stage))throw {code:'invalidReply'};return this.request('/api/log?session='+encodeURIComponent(session)+'&stage='+stage); }
  browse(kind) { return this.request('/api/browse',{kind}); }
  status(session) { if (!sessionId(session)) throw {code:'invalidReply'}; return this.request('/api/status?session='+encodeURIComponent(session)); }
  events(session, after) { if(!sessionId(session))throw {code:'invalidReply'};return this.request('/api/events?session='+encodeURIComponent(session)+'&after='+(Number.isInteger(after)&&after>=0?after:0)); }
}

// Deliberate, visibly labelled design preview; never falls through to real tools.
export class PreviewApi {
  constructor() { this.sequence=0; this.state=null; this.eventsList=[]; this.tick=0; }
  async discover() { return {schema:1,event:'discovery',capabilities:{browse:false,artwork:false},games:[
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
