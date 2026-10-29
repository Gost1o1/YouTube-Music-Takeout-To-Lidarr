// Offline behavioral replay: server-owned classification, accurate progress,
// source-only filing, reversible saved metadata, and no Lidarr writes.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const script=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const exact={key:'artist:exact',kind:'artist',artist:'Eiden XII',song:'Light Em Up (Female Version)',album:'Light Em Up (Female Version)',youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',disposition:'unreviewed',candidates:[{id:'12345678-1234-4234-8234-123456789abc',label:'MusicBrainz artist candidate',evidence:''}]};
const multiple={key:'artist:multiple',kind:'artist',artist:'Ham Sandwich',song:'TRIPPIN?',album:'TRIPPIN?',youtubeUrl:'https://www.youtube.com/watch?v=bbbbbbbbbbb',disposition:'unreviewed',candidates:[{id:'22345678-1234-4234-8234-123456789abc',label:'candidate',evidence:''},{id:'32345678-1234-4234-8234-123456789abc',label:'candidate',evidence:''}]};
const saved={key:'artist:exact',kind:'artist',artist:exact.artist,song:exact.song,album:exact.album,youtubeUrl:exact.youtubeUrl,saveMode:'auto',reason:'sole-exact-artist-name-unverified'};
const server={items:[exact,multiple],manualItems:[],progress:{running:true,finished:false,completed:0,total:2,error:''},restored:false};
const el={innerHTML:''},events={},calls=[],clock=[];
const state={reviewQueue:{items:[...server.items],manualItems:[]},inventory:{done:true,review:289,filesRead:0,toAdd:2216},verify:{totalArtists:951,artistNeedsReview:23,total:2505,songNeedsReview:289},inventoryItems:[],tracks:[]};
const fields={};for(const id of ['fallback-artist','fallback-song','fallback-album','fallback-url'])fields[id]={value:''};
const media={
 'artist:multiple:':{Name:'Ham Sandwich'},
 ['artist:multiple:'+multiple.candidates[0].id]:{Name:'Ham Sandwich'},
 ['artist:multiple:'+multiple.candidates[1].id]:{Name:'Ham Sandwich'},
};
const sandbox={console,el,state,window:{open:()=>{}},navigator:{},document:{addEventListener:(n,fn)=>events[n]=fn,getElementById:id=>fields[id]||null},setTimeout:(fn)=>{clock.push(fn);return clock.length},clearTimeout:()=>{},api:async(route,data)=>{
 calls.push(route);
 if(route==='review/queue')return {items:[...server.items],manualItems:[...server.manualItems]};
 if(route==='review/auto-classify/start')return {...server.progress};
 if(route==='review/auto-classify/status')return {...server.progress};
 if(route==='review/manual-metadata/restore'){
  assert.equal(data.key,exact.key);
  server.manualItems=[];server.restored=true;
  server.items=[{...exact,manualOverride:true},...server.items];return {restored:true};
 }
 if(route==='review/candidate/details')return media[data.key+':'+data.candidateId]||{Name:'Eiden XII'};
 if(route==='lidarr/inventory/staging-queue')return {items:[{albumId:14,artist:'Test',album:'Test Album',safeSongs:1,staged:false}]};
 if(route==='review/manual-metadata'){
  assert.equal(data.key,exact.key); assert.equal(data.artist,'Eiden XII');assert.equal(data.song,'Light Em Up (Female Version)');
  server.items=server.items.filter(x=>x.key!==exact.key);server.manualItems=[{...saved,saveMode:'manual'}];return {saved:true};
 }
 return {};
}};
vm.createContext(sandbox);vm.runInContext(script,sandbox);
const F=sandbox.window.Flow918;
const flush=async()=>{for(let i=0;i<14;i++)await new Promise(resolve=>setImmediate(resolve))};
const click=async data=>{const node={dataset:data};const target={closest:(selector)=>selector.includes('data-edit-manual')&&data.editManual?node:selector.includes('data-flow')&&data.flow?node:selector.includes('data-tab')&&data.tab?node:null}; await events.click({target});};
(async()=>{
 await F.open();await flush();
 assert.equal(F.classifying,true);
 assert.match(el.innerHTML,/Preparing Manual Review/);
 assert.match(el.innerHTML,/0%/);
 assert(!el.innerHTML.includes('Candidate Matches'),'cannot decide while artist preflight is running');
 server.progress={running:true,finished:false,completed:1,total:2,error:''};
 assert(clock.length>0);clock.shift()();await flush();
 assert.match(el.innerHTML,/50%/);
 // Simulate backend saving the exact-name artist before giving user a choice.
 server.items=[multiple];server.manualItems=[saved];
 server.progress={running:false,finished:true,completed:2,total:2,error:''};
 assert(clock.length>0);clock.shift()();await flush();
 assert.equal(F.classifying,false);
 assert(!el.innerHTML.includes('Preparing Manual Review'));
 assert.match(el.innerHTML,/Ham Sandwich/);
 assert.equal(state.reviewQueue.items.some(x=>x.key===exact.key),false);
 await F.open('preview');await flush();
 await click({tab:'manual'});await flush();
 assert.match(el.innerHTML,/Saved for Manual Import/);
 assert.match(el.innerHTML,/Eiden XII/);
 assert.match(el.innerHTML,/Review \/ Edit/);
 await click({editManual:exact.key});await flush();
 assert.equal(server.restored,true);
 assert.equal(F.page,'fallback');
 assert.match(el.innerHTML,/Manual Metadata/);
 assert.match(el.innerHTML,/Eiden XII/);
 fields['fallback-artist'].value=exact.artist;
 fields['fallback-song'].value=exact.song;
 fields['fallback-album'].value=exact.album;
 fields['fallback-url'].value=exact.youtubeUrl;
 await click({flow:'save-manual'});await flush();
 assert.equal(F.page,'manual');
 assert(!server.items.some(x=>x.key===exact.key));
 assert.equal(server.manualItems[0].saveMode,'manual');
 assert(!calls.some(x=>x.startsWith('lidarr/inventory/stage-albums')));
 console.log('PASS: accurate 0–50–100% server preflight, auto-hidden exact name, saved-entry restore/edit, no Lidarr writes');
})().catch(err=>{console.error(err);process.exitCode=1});
