// v0.9.1.15.5: review progress, artist-to-song Next, no auto-save flash,
// and provenance-aware Migration Preview without Lidarr writes.
'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const ui=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const serverCode=fs.readFileSync(path.join(__dirname,'../src/GuidedReviewV917.cs'),'utf8');
const program=fs.readFileSync(path.join(__dirname,'../src/Program.cs'),'utf8');
assert.match(serverCode,/ReviewPreviewPlacement/);
assert.match(program,/currentReviewSelections/);
assert.match(program,/savedSourceModes/);
const mbid='10000000-1111-4222-8333-100000000001';
const artist={key:'artist:example',kind:'artist',artist:'Example Artist',song:'Example',album:'Example Album',
 youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',disposition:'unreviewed',candidates:[{id:mbid,label:'Example Artist',evidence:''}]};
const selected={key:'song:selected',kind:'song',artist:'Example Artist',song:'Selected Song',album:'Example Album',
 youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',disposition:'candidate-for-planning',selected:mbid,
 candidates:[{id:mbid,label:'Selected Song',evidence:''}]};
const auto={key:'song:auto',kind:'song',artist:'Example Artist',song:'Flash Song',album:'Flash Album',
 youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',disposition:'unreviewed',candidates:[]};
const server={items:[artist,selected,auto],manualItems:[],rows:[
 {type:'song',row:1,artist:'Algorithm Artist',title:'Algorithm One',album:'Album A',category:'To Be Added / Songs',reviewOrigin:'Algorithm'},
 {type:'song',row:2,artist:'Algorithm Artist',title:'Algorithm Two',album:'Album B',category:'To Be Added / Songs',reviewOrigin:'Algorithm'},
 {type:'song',row:3,artist:'Example Artist',title:'Selected Song',album:'Example Album',category:'To Be Added / Songs (manual proposal)',reviewOrigin:'Manual Review',reviewPlanNote:'User chose a candidate; album verification pending.'},
 {type:'song',row:4,artist:'Example Artist',title:'Flash Song',album:'Flash Album',category:'To Be Added / Songs (source metadata)',reviewOrigin:'Auto Manual Review',reviewPlanNote:'Source metadata saved for manual import only.'},
 {type:'song',row:5,artist:'Unknown',title:'Truly Unmatched',album:'Unknown Album',category:'Manual Review / Songs',reviewOrigin:''}
]};
const calls=[],events={},frames=[];
let html='';const el={set innerHTML(s){html=s;frames.push(s)},get innerHTML(){return html}};
const state={tracks:[],reviewQueue:{items:server.items,manualItems:[]},
 verify:{totalArtists:1,artistNeedsReview:1,total:5,songNeedsReview:3},
 inventory:{done:true,totalSongs:5,toAdd:2,review:3,filesRead:0},inventoryItems:[]};
const sandbox={state,el,console,navigator:{},window:{open:()=>{}},
 document:{addEventListener:(type,fn)=>events[type]=fn,getElementById:()=>null},
 setTimeout:()=>1,clearTimeout:()=>{},
 api:async(route,data={})=>{
   calls.push(route);
   if(route==='review/queue')return {items:[...server.items],manualItems:[...server.manualItems]};
   if(route==='review/auto-classify/start'||route==='review/auto-classify/status')return {running:false,finished:true,completed:0,total:0,error:''};
   if(route==='review/candidate/details'){
     const item=server.items.find(x=>x.key===data.key)||auto;
     return {Name:data.candidateId?(item.kind==='artist'?item.artist:item.song):item.artist,
       SourceTitle:item.song,Album:item.album,Availability:'YouTube link available',CatalogIdentityConfirmed:!!data.candidateId};
   }
   if(route==='review/decision'){
     const item=server.items.find(x=>x.key===data.key);assert(item);
     item.disposition=data.disposition;item.selected=data.candidateId;
     return {saved:true};
   }
   if(route==='review/manual-metadata/auto'){
     assert.equal(data.key,auto.key);
     server.items=server.items.filter(x=>x.key!==auto.key);
     server.manualItems.push({key:auto.key,kind:'song',artist:auto.artist,song:auto.song,
       album:auto.album,saveMode:'auto',reason:data.reason});
     return {saved:true};
   }
   if(route==='lidarr/inventory/staging-queue')return {items:[{albumId:30,artist:'Algorithm Artist',album:'Album A',safeSongs:2,staged:false}]};
   if(route==='lidarr/inventory/items')return {items:[...server.rows]};
   throw Error('Unexpected API route '+route);
 }};
vm.createContext(sandbox);vm.runInContext(ui,sandbox);
const F=sandbox.window.Flow918;
const flush=async()=>{for(let i=0;i<25;i++)await new Promise(resolve=>setImmediate(resolve))};
async function clickFlow(action){await events.click({target:{closest:selector=>selector.includes('data-flow')?{dataset:{flow:action}}:null}})}
async function clickTab(tab){await events.click({target:{closest:selector=>selector.includes('data-tab')?{dataset:{tab}}:null}})}
(async()=>{
 await F.open('manual');await flush();
 assert.equal(F.reviewKind,'artist');
 assert.match(html,/0 \/ 1/,'artist begins unresolved');
 await events.click({target:{closest:selector=>selector.includes('data-option')?{dataset:{option:'0'}}:null}});
 await clickFlow('next');await flush();
 assert.equal(F.reviewKind,'song','Next on last artist goes to song matching');
 assert.match(html,/1 \/ 1/,'artist addressed counter updates after manual choice');
 assert.match(html,/3 \/ 5/,'song addressed count includes saved song candidate, without claiming independent verification');
 assert.match(html,/2 handled in review|1 handled in review/);
 assert.match(html,/Selected Song/);
 await clickFlow('next');await flush();
 assert(!server.items.some(x=>x.key===auto.key),'no-candidate source saved by the automatic song algorithm');
 assert.equal(server.manualItems[0].saveMode,'auto');
 assert(frames.every(s=>!(/YouTube Music Source[\s\S]*Flash Song/.test(s))),
   'auto-filed song must never appear as a user-facing source card');
 assert.match(html,/4 \/ 5/,'song progress updates after automatic source filing');
 assert.match(html,/1<\/b><span>Still needs review/,'only the unresolved song remains in the song counter');
 await F.open('preview');await flush();
 assert.match(html,/4<\/b><span>Proposed Songs/);
 assert.match(html,/1<\/b><span>Still Unmatched/);
 assert.match(html,/Selected Song/);
 assert.match(html,/Flash Song/);
 assert.match(html,/Auto Manual Review/);
 assert.match(html,/Manual Review/);
 assert.match(html,/Algorithm/);
 await clickTab('unmatched');
 assert.match(html,/Truly Unmatched/);
 assert.doesNotMatch(html,/Selected Song/);
 assert.doesNotMatch(html,/Flash Song/);
 await clickTab('manual');
 assert.match(html,/Auto Manual Review/,'source-only saves keep an editable manual-import record');
 assert(!calls.includes('lidarr/inventory/stage-albums'),'preview and review never write to Lidarr');
 console.log('PASS: live addressed stats, last-artist Next, hidden auto-saving song, manual/auto provenance, clean unmatched, no Lidarr writes');
})().catch(e=>{console.error(e);process.exitCode=1});
