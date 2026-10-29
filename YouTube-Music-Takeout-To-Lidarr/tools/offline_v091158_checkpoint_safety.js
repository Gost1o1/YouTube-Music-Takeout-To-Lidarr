// v0.9.1.15.8 safety regression: authoritative reviewed stats, recovery and
// no automatic Lidarr write on restored/import-interrupted state.
'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/LidarrImport.cs'),'utf8');
const reviews=fs.readFileSync(path.join(__dirname,'../src/GuidedReviewV917.cs'),'utf8');
const program=fs.readFileSync(path.join(__dirname,'../src/Program.cs'),'utf8');
const index=fs.readFileSync(path.join(__dirname,'../AppUI/index.html'),'utf8');
const flow=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
assert.match(source,/sealed class ImportCheckpoint/);
assert.match(source,/File\.Move\(temporary, path, true\)/);
assert.match(source,/MarkImportWriteIntent\("Queue album search #" \+ albumId\)/);
assert.match(source,/LoadImportCheckpoint\(\)/);
assert.match(source,/No API keys, OAuth tokens/);
assert.match(source,/ReconnectImportCheckpointAsync/);
assert.match(program,/WatchBrowserTabsAsync/);
assert.match(program,/case "\/api\/app\/tab-close"/);
assert.match(program,/case "\/api\/lidarr\/import\/connect"/);
assert.match(index,/window\.addEventListener\('pagehide'/);
assert.match(reviews,/reviewProgress = progress/);
let html='';const calls=[];
const recovery={total:2,completed:1,catalogVerified:1,failed:0,running:false,done:false,
 restored:true,interrupted:true,endpointMatches:false,startedUtc:'2026-10-28T00:00:00Z',
 phase:'Previous Lidarr import was interrupted',inFlightAction:'Queue album search #50'};
const state={tracks:[],lidarr:'http://127.0.0.1:8686',reviewQueue:{items:[],manualItems:[],
 reviewProgress:{artist:{total:951,automatic:944,reviewed:1,addressed:945,pending:6},
 song:{total:2505,automatic:2220,reviewed:279,addressed:2499,pending:6}}}};
const sandbox={state,console,navigator:{},SESSION:'test',window:{scrollTo:()=>{}},
 el:{get innerHTML(){return html},set innerHTML(value){html=value}},
 document:{addEventListener:()=>{},getElementById:()=>null},
 setTimeout:()=>1,clearTimeout:()=>{},
 api:async route=>{calls.push(route);if(route==='lidarr/import/status')return recovery;
 if(route==='lidarr/import/background')return {active:0,tasks:[]};
 throw Error('Unexpected API '+route)}
};
vm.createContext(sandbox);vm.runInContext(flow,sandbox);
const F=sandbox.window.Flow918;
F.active=true;F.page='manual';F.render();
assert.match(html,/945 \/ 951/);
assert.match(html,/2,499 \/ 2,505/);
assert.match(html,/6<\/b><span>Still needs review/);
state.reviewQueue.reviewProgress.artist={total:951,automatic:944,reviewed:2,addressed:946,pending:5};
F.render();
assert.match(html,/946 \/ 951/);
assert.match(html,/5<\/b><span>Still needs review/);
F.openRecoveredImport(recovery);
assert.match(html,/Previous import checkpoint restored/);
assert.match(html,/Reconnect to the original Lidarr server/);
assert.doesNotMatch(html,/fconfetti/);
assert.equal(calls.filter(x=>x==='lidarr/import/start').length,0);
console.log('PASS: review counters from live queue, read-only checkpoint recovery, tab-close wiring and no automatic writes');
