// Real browser requests stay pending; authoritative status must keep polling.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),session='pending-log-session';
const ids=['tools','source','unity','profile','inspect','build','install'];
test('never-ending event and live log requests cannot hide a later failure or hold retry busy',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    let mode='running',client,statusCalls=0;const held=[],posts=[];
    const state=()=>({session,status:mode,choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},artwork:[],
      needsActions:mode==='failed'?[{code:'build_tool_failed',stage:'build',message:{en:'Conversion failed.',de:'Konvertierung fehlgeschlagen.'},parameters:{failureStage:'recovery',cause:"ModuleNotFoundError: No module named 'recover'"}}]:[],
      stages:ids.map((id,index)=>({id,status:index<5?'complete':index===5?mode:'pending',progress:{stagePercent:index<5?100:index===5?43:0,phase:index===5?'recovery-asset-references':'complete',percent:null}}))});
    const server=createServer(async(request,response)=>{
      try {
        const url=new URL(request.url,'http://localhost');let value;
        if(request.method==='POST'){let text='';for await(const part of request)text+=part;posts.push({path:url.pathname,body:JSON.parse(text)});}
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,games:[],unityEditors:[],capabilities:{browse:false,logs:true,support:false}};
        else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(url.pathname==='/api/status'){statusCalls++;value={schema:1,event:'status',state:state()};}
        else if(['/api/events','/api/log'].includes(url.pathname)){
          // Deliberately provide neither headers nor body until the client
          // cancels. A 503 that returns eventually would miss the regression.
          const observation={path:url.pathname,response,closed:false};held.push(observation);
          response.once('close',()=>{observation.closed=true;});return;
        }else if(url.pathname==='/api/run'){mode='running';value={schema:1,event:'started'};}
        if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
        const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
        if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
        response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    try {
      client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      await client.evaluate("document.querySelector('[data-language=de]').click();document.querySelector('#diagnostic-details').open=true");
      for(let index=0;index<100&&(held.length<2||statusCalls<3);index++)await delay(50);
      assert.ok(statusCalls>=3,'status continues while logs have not returned');
      assert.deepEqual(held.map(row=>row.path).sort(),['/api/events','/api/log'],'one request per optional observer');
      assert.ok(held.every(row=>!row.closed),'the fixture requests really remain pending');
      mode='failed';
      await client.wait("document.getElementById('progress-title').textContent==='Build fehlgeschlagen'");
      assert.equal(await client.evaluate("document.getElementById('failure-cause').textContent"),"Ursache: ModuleNotFoundError: No module named 'recover'");
      assert.equal(await client.evaluate("document.getElementById('failure-retry').disabled"),false);
      assert.ok(held.every(row=>!row.closed),'terminal failure arrives without completing optional logs');
      await client.picture('pending-logs-later-failure-de');
      await client.evaluate("document.getElementById('failure-retry').click()");
      await client.wait("!document.getElementById('cancel').hidden&&!document.getElementById('cancel').disabled");
      assert.deepEqual(posts,[{path:'/api/run',body:{session}}]);
      for(let index=0;index<100&&held.filter(row=>row.closed).length<2;index++)await delay(20);
      assert.equal(held.filter(row=>row.closed).length,2,'retry cancels old observers without delaying the action');
      for(let index=0;index<100&&held.length<4;index++)await delay(20);
      assert.equal(held.length,4,'the new run owns one fresh observer per endpoint');
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      mode='failed';await client.wait("document.getElementById('progress-title').textContent==='Build fehlgeschlagen'");
      await client.evaluate("document.getElementById('new-build').click()");
      await client.wait("!document.getElementById('game-page').hidden");
      for(let index=0;index<100&&held.some(row=>!row.closed);index++)await delay(20);
      assert.ok(held.every(row=>row.closed),'changing session cancels every retained observer');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}}
  });
