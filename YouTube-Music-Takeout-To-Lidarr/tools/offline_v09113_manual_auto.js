// Offline simulation: zero-usable-candidate classification; manual correction
// removes item; category summary never implies MusicBrainz/Lidarr writes.
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const events={},el={innerHTML:''},calls=[],media={};
const withId={key:'artist:AXWELL',kind:'artist',artist:'Axwell / Ingrosso',song:'More Than You Know',album:'More Than You Know',youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',candidates:[{id:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',label:'MusicBrainz artist candidate',evidence:''}]};
const incomplete={key:'song:UNKNOWN',kind:'song',artist:'Mystery Artist',song:'-',album:'',youtubeUrl:'',candidates:[]};
const other={key:'artist:VALID',kind:'artist',artist:'Blue Oyster Cult',song:'Burnin for You',album:'Album',candidates:[{id:'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',label:'Blue Öyster Cult',evidence:'canonical; credited title matches 2'}]};
const state={reviewQueue:{items:[withId,incomplete,other],manualItems:[]},inventory:{done:true,review:2,toAdd:2200,filesRead:0},verify:{totalArtists:951,artistNeedsReview:2},inventoryItems:[],tracks:[]};
const form={
'fallback-artist':{value:'Mystery Artist'}, 'fallback-song':{value:'Recovered song title'},
'fallback-album':{value:'Recovered EP'}, 'fallback-url':{value:''}
};
const server={items:[withId,incomplete,other],manualItems:[{key:'song:ALREADY',kind:'song',artist:'Saved Sample',song:'Saved Song',album:'',saveMode:'auto'}]};
let promises=0;
const sandbox={state,el,console,setTimeout:()=>1,clearTimeout:()=>{},navigator:{},window:{open:()=>{}},
 document:{addEventListener:(n,fn)=>events[n]=fn,getElementById:id=>form[id]||null},
 api:async (route,payload)=>{
   calls.push([route,payload]);
   if(route==='review/queue')return {items:[...server.items],manualItems:[...server.manualItems]};
   if(route==='review/auto-classify/start'){
     // The server finishes a deterministic pre-review pass, not the browser
     // progressively hiding visible cards as candidate media arrives.
     server.items=server.items.filter(x=>x.key!==withId.key);
     server.manualItems.push({key:withId.key,kind:'artist',artist:withId.artist,song:withId.song,album:withId.album,saveMode:'auto'});
     return {running:false,finished:true,total:1,completed:1,error:''};
   }
   if(route==='review/candidate/details'){
     if(payload.candidateId===withId.candidates[0].id)return {Name:'',ArtworkUrl:'',PreviewUrl:''};
     if(payload.candidateId===other.candidates[0].id)return {Name:'Blue Öyster Cult',ArtworkUrl:'',PreviewUrl:''};
     return {Name:'',ArtworkUrl:'',PreviewUrl:''};
   }
   if(route==='review/manual-metadata/auto'){
     assert.equal(payload.key,withId.key);
     server.items=server.items.filter(x=>x.key!==withId.key);
     server.manualItems.push({key:withId.key,kind:'artist',artist:withId.artist,song:withId.song,album:withId.album,saveMode:'auto'});
     return {saved:true};
   }
   if(route==='review/manual-metadata'){
     assert.equal(payload.key,incomplete.key);
     assert.equal(payload.song,'Recovered song title');
     server.items=server.items.filter(x=>x.key!==incomplete.key);
     server.manualItems.push({key:incomplete.key,kind:'song',artist:payload.artist,song:payload.song,album:payload.album,saveMode:'manual'});
     return {saved:true};
   }
   if(route==='lidarr/inventory/staging-queue')return {items:[{albumId:9,artist:'Blue Öyster Cult',album:'Test Album',safeSongs:1,staged:false}]};
   return {};
 }};
vm.createContext(sandbox);vm.runInContext(source,sandbox);
const F=sandbox.window.Flow918;
const sleep=()=>new Promise(r=>setImmediate(r));
const clickFlow=action=>events.click({target:{closest:selector=>selector.includes('data-flow')?{dataset:{flow:action}}:null}});
const flush=async()=>{for(let i=0;i<12;i++)await sleep();};
(async()=>{
 await F.open();
 await flush();
 assert.equal(server.items.some(x=>x.key===withId.key),false,'server pre-review pass must save fully populated metadata');
 assert.equal(server.manualItems.find(x=>x.key===withId.key)?.saveMode,'auto');
 assert.equal(F.reviewIndex.artist,0,'after removing the current item, the next row is in its old position');
 // Artist review still contains one viable artist; move to song review.
 await clickFlow('songs');await flush();
 assert.match(el.innerHTML,/Complete Missing Metadata/,'partial metadata requires manual input');
 await clickFlow('manual-fallback');
 assert.match(el.innerHTML,/Manual Metadata/);
 await clickFlow('save-manual');await flush();
 assert.equal(server.items.some(x=>x.key===incomplete.key),false,'manual save removes item from review');
 assert.equal(F.page,'manual','save returns directly to review without disabled Next trap');
 assert.equal(F.manualEntries.length,3);
 await clickFlow('open-preview');await flush();
 assert.match(el.innerHTML,/Saved for Manual Import/);
 events.click({target:{closest:selector=>selector.includes('data-tab')?{dataset:{tab:'manual'}}:null}});
 assert.match(el.innerHTML,/2 automatically saved/);
 assert.match(el.innerHTML,/1 manually saved/);
 assert.match(el.innerHTML,/More Than You Know/);
 assert.match(el.innerHTML,/Recovered song title/);
 assert.match(el.innerHTML,/Auto Manual Review/);
 assert.match(el.innerHTML,/no verified recording or Lidarr write/i);
 assert.equal(calls.filter(c=>c[0].startsWith('lidarr/inventory/stage-albums')).length,0);
 console.log('PASS: auto queue removal, manual-save advance, persistent summary counts, no Lidarr writes');
})().catch(e=>{console.error(e);process.exitCode=1});
