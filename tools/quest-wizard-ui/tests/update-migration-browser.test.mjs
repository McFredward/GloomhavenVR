// Production Store/parser projections in the actual app and Chrome; no build tools.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';
import {buildOverviewView} from '../model.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),repo=resolve(root,'../..');
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};

test('Mod updates explain the retained full-build route, preserve measured progress and show only current retry work',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',
    ['-I','-B',fileURLToPath(new URL('./update_migration_fixture.py',import.meta.url)),repo],{encoding:'utf8',timeout:10000}));
  const phase=name=>rows[name].stages[5].progress.stagePercent;
  assert.equal(buildOverviewView(rows.compact).updateMigration,null);
  assert.deepEqual(buildOverviewView(rows.migration).updateMigration,{strategy:'cached-full-build',priorPercent:phase('compact')});
  assert.equal(phase('migration'),phase('compact'));
  assert.ok(phase('measured-3')>phase('measured-1')&&phase('measured-1')>phase('migration'));
  assert.equal(phase('retry'),phase('failed'));
  assert.ok(phase('retry-current')>=phase('retry'));
  const invalid=structuredClone(rows.migration);
  invalid.stages[5].progress.buildOverview.updateMigration.priorPercent=Infinity;
  assert.deepEqual(buildOverviewView(invalid).updateMigration,{strategy:'cached-full-build',priorPercent:null});
  let mode='compact',client;const requests=[];
  const server=createServer(async(request,response)=>{
    try{
      const path=new URL(request.url,'http://localhost').pathname;
      if(path.startsWith('/api/')){
        let raw='';for await(const chunk of request)raw+=chunk;
        const body=raw?JSON.parse(raw):null;requests.push({path,body});let value;
        if(path==='/api/discover')value={schema:1,event:'discovery',latestSession:rows.compact.session,recentSessions:[{session:rows.compact.session,status:rows.compact.status}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
        else if(path==='/api/status')value={schema:1,event:'status',state:rows[mode]};
        else if(path==='/api/events')value={schema:1,event:'events',events:[]};
        else if(path==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(path==='/api/run'){assert.deepEqual(body,{session:rows.failed.session});mode='retry';value={schema:1,event:'started',session:rows.failed.session};}
        else throw Error('Unexpected API operation '+path);
        response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));
      if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('Not static');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const select=async name=>{
    mode=name;await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==="+JSON.stringify(String(phase(name))));
  };
  try{
    client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#migration-fixture'});
    await client.chooseHome('resume');await client.wait("!document.getElementById('build-overview').hidden");
    assert.equal(await client.evaluate("document.getElementById('build-update-migration').hidden"),true);
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    mode='migration';await client.wait("!document.getElementById('build-update-migration').hidden");
    assert.match(await client.evaluate("document.getElementById('build-update-migration').textContent"),/aktualisierte Quest-Inhalte.*gespeicherte Ergebnisse werden wiederverwendet/s);
    assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Quest-Inhalte der APK aktualisieren/);
    assert.equal(await client.evaluate("document.getElementById('progress-detail').textContent.includes('INTERNAL_C:')"),false);
    assert.match(await client.evaluate("document.getElementById('build-overview-count').textContent"),/0 \/ 23.*23 ausstehend/);
    assert.equal(await client.evaluate("document.querySelectorAll('#build-groups [data-operation^=update-]').length"),0);
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),String(phase('compact')));
    await client.picture('mod-update-cached-build-migration-de');
    await client.evaluate("document.querySelector('[data-language=en]').click()");
    assert.equal(await client.evaluate("document.getElementById('build-update-migration').textContent"),'This APK needs updated Quest content. Compatible saved results are reused.');
    assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Update the APK.s Quest content/);
    for(const name of ['measured-1','measured-3']){
      await select(name);
      assert.match(await client.evaluate("document.getElementById('build-current').textContent"),/Check game files/);
      assert.equal(await client.evaluate("document.getElementById('build-update-migration').hidden"),false);
    }
    await select('reused');
    assert.match(await client.evaluate("document.getElementById('build-overview-count').textContent"),/6 \/ 23.*17 remaining/);
    assert.equal(await client.evaluate("document.querySelectorAll('#build-groups li.reused').length"),6);
    await select('failed');await client.wait("!document.getElementById('action-needed').hidden&&!document.getElementById('failure-retry').disabled");
    await client.evaluate("document.getElementById('failure-retry').click()");
    await client.wait("document.getElementById('action-needed').hidden&&document.getElementById('build-current').textContent.includes('Waiting')");
    assert.equal(await client.evaluate("document.querySelectorAll('#build-groups li.failed,#build-groups li.running,#build-groups li.checking').length"),0);
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),String(phase('failed')));
    assert.equal(await client.evaluate("document.getElementById('build-update-migration').hidden"),false);
    await select('retry-current');
    assert.match(await client.evaluate("document.getElementById('build-current').textContent"),/Convert game content/);
    assert.equal(await client.evaluate("document.querySelectorAll('#build-groups li.failed').length"),0);
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    for(const language of ['en','de']){
      await client.evaluate("document.querySelector('[data-language="+language+"]').click()");
      assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
      assert.equal(await client.evaluate("document.getElementById('build-update-migration').hidden"),false);
      await client.picture('mod-update-cached-build-retry-mobile-'+language);
    }
    assert.equal(requests.filter(row=>row.path==='/api/run').length,1);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}}
 });
