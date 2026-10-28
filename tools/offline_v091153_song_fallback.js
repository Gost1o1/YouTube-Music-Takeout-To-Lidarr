// No usable recording: automatically preserve complete Takeout source metadata.
// Incomplete source: prompt for correction, save, and advance to the next song.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const src=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const id='11111111-2222-4222-8222-123456789abc';
const complete={key:'song:max',kind:'song',row:1,artist:'MAX',song:'The Other Side',
 album:'The Greatest Showman: Reimagined (Deluxe)',youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',
 disposition:'unreviewed',candidates:[{id,label:'MusicBrainz credited track candidate',evidence:''}]};
const incomplete={key:'song:missing',kind:'song',row:2,artist:'Mystery Artist',song:'Unknown Tune',album:'',
 youtubeUrl:'',disposition:'unreviewed',candidates:[]};
const next={key:'song:next',kind:'song',row:3,artist:'Next Artist',song:'Valid Song',album:'Example Album',
 youtubeUrl:'https://www.youtube.com/watch?v=bbbbbbbbbbb',disposition:'unreviewed',
 candidates:[{id:'33333333-3333-4333-8333-333333333333',label:'Valid Song',evidence:''}]};
const server={items:[complete,incomplete,next],manualItems:[]};
const events={},el={innerHTML:''},calls=[],fields={
 'fallback-artist':{value:'Mystery Artist'},'fallback-song':{value:'Unknown Tune'},
 'fallback-album':{value:''},'fallback-url':{value:''}
};
let releaseAuto;
const state={tracks:[],reviewQueue:{items:server.items,manualItems:[]},
 inventory:{done:true,totalSongs:3,review:3,toAdd:0,filesRead:0},
 verify:{total:3,totalArtists:3,songNeedsReview:3,artistNeedsReview:0}};
const sandbox={state,el,console,window:{open:()=>{}},navigator:{},setTimeout:()=>1,clearTimeout:()=>{},
 document:{addEventListener:(k,fn)=>events[k]=fn,getElementById:id=>fields[id]||null},
 api:async(route,payload)=>{
  calls.push(route);
  if(route==='review/queue')return {items:[...server.items],manualItems:[...server.manualItems]};
  if(route==='review/auto-classify/start')return {running:false,finished:true,completed:0,total:0,error:''};
  if(route==='review/candidate/details'){
   if(payload.key===complete.key)return payload.candidateId?
      {Name:'Unrelated Recording',ArtworkUrl:'',PreviewUrl:''}:{Name:'MAX',SourceTitle:'The Other Side'};
   if(payload.key===incomplete.key)return {Name:'Mystery Artist',SourceTitle:'Unknown Tune'};
   return payload.candidateId?{Name:'Valid Song'}:{Name:'Next Artist',SourceTitle:'Valid Song'};
  }
  if(route==='review/manual-metadata/auto'){
   assert.equal(payload.key,complete.key);
   assert.equal(payload.reason,'no-usable-candidate');
   return new Promise(resolve=>{releaseAuto=()=>{
    server.items=server.items.filter(x=>x.key!==complete.key);
    server.manualItems.push({key:complete.key,kind:'song',artist:complete.artist,song:complete.song,
      album:complete.album,youtubeUrl:complete.youtubeUrl,saveMode:'auto',reason:'no-usable-candidate'});
    resolve({saved:true,newEntry:true});
   }});
  }
  if(route==='review/manual-metadata'){
   assert.equal(payload.key,incomplete.key);
   assert.equal(payload.artist,'Mystery Artist');
   assert.equal(payload.song,'Unknown Tune');
   assert.equal(payload.album,'Recovered Album');
   server.items=server.items.filter(x=>x.key!==incomplete.key);
   server.manualItems.push({key:incomplete.key,kind:'song',artist:payload.artist,song:payload.song,
      album:payload.album,saveMode:'manual'});
   return {saved:true};
  }
  throw Error('Unexpected call: '+route);
 }};
vm.createContext(sandbox);vm.runInContext(src,sandbox);
const F=sandbox.window.Flow918;
const flush=async()=>{for(let i=0;i<16;i++)await new Promise(resolve=>setImmediate(resolve));};
const click=async flow=>events.click({target:{closest:selector=>selector.includes('data-flow')?{dataset:{flow}}:null}});
(async()=>{
 await F.open('manual');await flush();F.reviewKind='song';F.render();await flush();
 assert.equal(typeof releaseAuto,'function','a complete source with no usable recording should auto-save');
 assert.match(el.innerHTML,/Saving automatically/);
 assert.match(el.innerHTML,/No MusicBrainz recording is being verified/);
 assert(!el.innerHTML.includes('MusicBrainz artist identity is not verified'));
 releaseAuto();await flush();
 assert(!server.items.some(x=>x.key===complete.key));
 assert.equal(server.manualItems[0].song,complete.song);
 assert.equal(F.reviewIndex.song,0,'next unresolved song should take the old position');
 assert.match(el.innerHTML,/Missing source metadata/);
 assert.match(el.innerHTML,/Album, YouTube link/);
 assert.match(el.innerHTML,/Complete Missing Metadata/);
 await click('manual-fallback');await flush();
 assert.equal(F.page,'fallback');
 assert.match(el.innerHTML,/value="Mystery Artist"/);
 assert.match(el.innerHTML,/value="Unknown Tune"/);
 fields['fallback-album'].value='Recovered Album';
 await click('save-manual');await flush();
 assert.equal(F.page,'manual');
 assert.match(el.innerHTML,/Valid Song/,'should move to the next Song Review card');
 assert.equal((el.innerHTML.match(/class="fcandidate /g)||[]).length,1,
  'song candidate titles must be compared with the source song title, not its artist name');
 assert.equal(server.manualItems.length,2);
 assert(!calls.includes('lidarr/inventory/stage-albums'),'fallback must remain source metadata only');
 console.log('PASS: complete no-usable-candidate song auto-filed; incomplete fields prompted; manual save advances to next song');
})().catch(err=>{console.error(err);process.exitCode=1});
