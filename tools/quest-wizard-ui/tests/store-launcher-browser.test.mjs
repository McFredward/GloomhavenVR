// Real browser uses production bilingual action data; OS startup is the seam.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';
const root=fileURLToPath(new URL('../',import.meta.url));
const repo=resolve(root,'../..'),session='1'.repeat(32),stages=['tools','source','unity','profile','inspect','build','install'];
test('launcher prerequisite remains visible, opens matching client and explicitly resumes retained run',
 {skip:!existsSync(chrome),timeout:45000},async()=>{
 const script="import sys,json;sys.path.insert(0,sys.argv[1]);from store_launcher import required;print(json.dumps({p:{'code':required(p,cause='Fixture SDK unavailable').code,'stage':'inspect','message':required(p).message,'parameters':required(p).parameters} for p in ('steam','epic','gog')}))";
 const actions=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B','-c',script,resolve(repo,'tools/quest-wizard')],{encoding:'utf8'}));
 let provider='steam',mode='blocked',client;const posts=[];
 const state=()=>({session,status:mode,choices:{provider,gameRoot:'C:\\Owned game',install:false},needsActions:mode==='blocked'?[actions[provider]]:[],stages:stages.map((id,i)=>({id,status:i<4?'complete':i===4?mode:'pending',progress:{stagePercent:i<4?100:0}}))});
 const server=createServer(async(request,response)=>{
  try{const url=new URL(request.url,'http://localhost');let value,body;
   if(request.method==='POST'){let text='';for await(const block of request)text+=block;body=JSON.parse(text);posts.push({path:url.pathname,body});}
   if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:mode}],games:[],unityEditors:[],capabilities:{storeLaunchers:true}};
   else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
   else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
   else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
   else if(url.pathname==='/api/launcher/open')value={schema:1,event:'launcher-opened',provider:body.provider,message:{en:'Client opened. Continue the saved run after sign-in.',de:'Launcher geöffnet. Nach der Anmeldung den gespeicherten Run fortsetzen.'}};
   else if(url.pathname==='/api/run'){mode='running';value={schema:1,event:'started'};}
   if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
   const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
   if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('static');
   response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
  }catch(error){response.writeHead(500);response.end(String(error));}
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{client=await browser();
  for(const selected of ['steam','epic','gog']){
   provider=selected;mode='blocked';
   await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/?provider='+selected+'#fixture'});
   await client.chooseHome('resume');
   await client.wait("!document.getElementById('failure-launcher-open').hidden&&!document.getElementById('failure-launcher-open').disabled");
   await client.evaluate("document.querySelector('[data-language=de]').click()");
   assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/diesen Run fortsetzen/);
   assert.match(await client.evaluate("document.getElementById('failure-launcher-open').textContent"),new RegExp({steam:'Steam',epic:'Epic',gog:'GOG'}[selected]));
   const before=posts.filter(row=>row.path==='/api/run').length;
   await client.evaluate("document.getElementById('failure-launcher-open').click()");
   await client.wait("!document.getElementById('failure-launcher-status').hidden&&document.getElementById('failure-launcher-status').textContent.includes('geöffnet')");
   assert.deepEqual(posts.at(-1),{path:'/api/launcher/open',body:{provider:selected}});
   assert.equal(posts.filter(row=>row.path==='/api/run').length,before,'opening client never starts build or grants ownership');
   assert.equal(await client.evaluate("document.getElementById('failure-launcher-status').getBoundingClientRect().height>0"),true);
   await client.picture('store-launcher-'+selected+'-de');
   await client.evaluate("document.querySelector('[data-language=en]').click()");
   assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/continue this saved run/);
   await client.evaluate("document.getElementById('failure-retry').click()");
   await client.wait("document.getElementById('failure-launcher-open').hidden");
   assert.deepEqual(posts.at(-1),{path:'/api/run',body:{session}});
  }
  assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
 }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
 });
