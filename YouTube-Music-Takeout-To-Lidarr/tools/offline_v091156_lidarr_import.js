// v0.9.1.15.6: opt-in Lidarr writes, safe finalization with zero album stages,
// search consent, real API-progress states, and source-only exclusion.
'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const script=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const server=fs.readFileSync(path.join(__dirname,'../src/LidarrImport.cs'),'utf8');
const router=fs.readFileSync(path.join(__dirname,'../src/Program.cs'),'utf8');
assert.match(server,/await ImportWriteAsync\(HttpMethod\.Post, "\/api\/v1\/artist"/);
assert.match(server,/await ImportWriteAsync\(HttpMethod\.Put, "\/api\/v1\/album\/monitor"/);
assert.match(server,/new \{ name = "AlbumSearch", albumIds = new\[\] \{ albumId \} \}/);
assert.match(server,/searchForMissingAlbums"\] = false/);
assert.match(server,/VerifiedArtistsMissingFromLidarr/);
assert.match(server,/never called from matching, preview/);
assert.match(server,/consent\.ValueKind != JsonValueKind\.True/);
assert.match(router,/case "\/api\/lidarr\/import\/start"/);
assert.match(router,/case "\/api\/lidarr\/import\/status"/);
const events={},calls=[],state={tracks:[{Artist:'Example',Song:'Song'}],
  inventory:{done:true,totalSongs:1,filesRead:0,review:1},
  reviewQueue:{items:[],manualItems:[{key:'manual:1',kind:'song',artist:'Unverified',song:'Manual Song',album:'Unknown',saveMode:'auto'}]},
  inventoryItems:[]};
let html='';
const el={set innerHTML(value){html=value},get innerHTML(){return html}};
const mock={status:{running:true,done:false,total:1,completed:0,failed:0,phase:'Adding verified artist',events:[]}};
const sandbox={state,el,console,navigator:{},SESSION:'unit-session',
  window:{open:()=>{}},document:{addEventListener:(type,fn)=>events[type]=fn,getElementById:()=>null},
  clearTimeout:()=>{},setTimeout:()=>1,
  api:async(route,data)=>{
    calls.push({route,data});
    if(route==='review/queue')return state.reviewQueue;
    if(route==='lidarr/inventory/staging-queue')return {items:[{albumId:50,artist:'Verified',album:'Verified Album',safeSongs:1,staged:false}]};
    if(route==='lidarr/inventory/items')return {items:[{type:'song',row:1,artist:'Unverified',title:'Manual Song',album:'Unknown',category:'Manual Review / Songs'}]};
    if(route==='lidarr/import/options')return {roots:['C:\\Music'],quality:[{id:4,name:'Any'}],metadata:[{id:2,name:'Standard'}],verifiedArtistsToAdd:1,eligibleAlbums:0};
    if(route==='lidarr/import/start'){
      assert.equal(data.rootPath,'C:\\Music');
      assert.equal(data.qualityId,4);assert.equal(data.metadataId,2);
      assert.equal(data.includeArtists,true);
      assert.equal(data.mode,'search');
      assert.equal(data.confirmAlbumSearch,true);
      assert.equal(data.albumIds.length,1);
      assert.equal(data.albumIds[0],50);
      return {started:true};
    }
    if(route==='lidarr/import/status')return mock.status;
    if(route==='lidarr/import/background')return {active:1,tasks:[{name:'RefreshArtist',status:'started',message:'Updating artist info'}]};
    throw Error('Unexpected route: '+route);
}};
vm.createContext(sandbox);vm.runInContext(script,sandbox);
const F=sandbox.window.Flow918;
async function click(action){await events.click({target:{closest:q=>q.includes('[data-flow]')?{dataset:{flow:action}}:null}})}
async function flush(){for(let i=0;i<12;i++)await new Promise(done=>setImmediate(done))}
(async()=>{
 await F.open('preview');
 await flush();
 assert.match(html,/Continue to Finalize Import/);
 assert.equal(F.selected.size,0);
 await click('open-final');await flush();
 assert.equal(F.page,'final','final screen must be reachable without selectable albums');
 assert.match(html,/Add to Lidarr Only/);
 assert.match(html,/Add and Search/);
 assert.match(html,/Start Lidarr Import/);
 assert.match(html,/Select at least one eligible album/);
 assert.match(html,/1<\/b><span>Verified artists to add/);
 assert.doesNotMatch(html,/Confirm Local Import Plan/);
 assert(!calls.some(c=>c.route==='lidarr/import/start'),'no writes during preview');
 events.change({target:{name:'flow918-import-mode',value:'search'}});
 assert.equal(F.importMode,'catalog','search mode remains unavailable without a selected album');
 await click('open-preview');await flush();
 events.change({target:{id:'',checked:true,dataset:{album:'50'},matches:q=>q.includes('[data-album]')}});
 assert.equal(F.selected.size,1);
 await click('open-final');await flush();
 events.change({target:{name:'flow918-import-mode',value:'search'}});
 assert.equal(F.importMode,'search');
 assert.equal(F.searchConfirmed,false);
 assert.match(html,/data-flow="start-lidarr-import" disabled/);
 events.change({target:{id:'flow918-confirm-search',checked:true}});
 assert.equal(F.searchConfirmed,true);
 await click('start-lidarr-import');await flush();
 assert(calls.some(c=>c.route==='lidarr/import/start'),'explicit click sends the only write action');
 assert.equal(F.page,'import-progress');
 assert.match(html,/0 \/ 1/);
 mock.status={running:false,done:true,total:2,completed:2,failed:0,addedArtists:1,
   monitoredAlbums:0,alreadyMonitoredAlbums:0,searchesQueued:1,catalogVerified:1,
   catalogPending:1,catalogComplete:false,phase:'Waiting for album catalog confirmation',events:[]};
 await F.restoreNavigation({page:'import-progress',reviewKind:'song',reviewIndex:{song:0,artist:0},selected:[50]});
 await flush();
 assert.equal(F.page,'import-progress','requests queued must not show the final celebration');
 assert.match(html,/1 \/ 2<\/b><span>Confirmed in Lidarr/);
 assert.match(html,/Recheck Lidarr Catalog/);
 assert.doesNotMatch(html,/fconfetti/);
 mock.status={...mock.status,catalogVerified:2,catalogPending:0,catalogComplete:true,
   phase:'Selected catalog confirmed in Lidarr'};
 await F.restoreNavigation({page:'import-progress',reviewKind:'song',reviewIndex:{song:0,artist:0},selected:[50]});
 await flush();
 assert.equal(F.page,'import-result','refresh recovers completed server result');
 assert.match(html,/Artists added/);
 assert.match(html,/Selected catalog verified/);
 assert.doesNotMatch(html,/fconfetti/,'a saved source-only song prevents a false whole-library celebration');
 assert.match(html,/still need manual import/);
 assert.match(html,/Lidarr background activity/);
 assert.match(html,/Download Results CSV/);
 F.manualEntries=[];F.render();
 assert.match(html,/fconfetti/,'confetti appears when the entire reviewed source set has a supported catalog outcome');
 console.log('PASS: Lidarr consent, read-back progress, no premature celebration with manual proposals, background activity and refresh restoration');
})().catch(e=>{console.error(e);process.exitCode=1});
