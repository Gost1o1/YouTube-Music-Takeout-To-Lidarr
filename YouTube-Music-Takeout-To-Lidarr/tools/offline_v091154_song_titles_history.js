// Exact-title / trailing parenthetical source-only filing, and stable Previous navigation.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const script=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const uuid=n=>String(n).padStart(8,'0')+'-1234-4234-8234-123456789abc';
function song(key,artist,title,candidateTitle,confirmed=true,availability=''){
 return {key,kind:'song',row:Number(key.slice(1)),artist,song:title,album:'Source Album',
   youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',disposition:'unreviewed',
   candidates:[{id:uuid(Number(key.slice(1))),label:'MusicBrainz credited track candidate',evidence:''}],
   candidateTitle,confirmed,availability};
}
const a=song('s1','Manual Artist','Unresolved Song','An Unrelated Recording');
const victory=song('s2','Two Steps From Hell','Victory','Victory');
const miley=song('s3','Miley Cyrus',"We Can't Stop",'We Can’t Stop');
const league=song('s4','League of Legends','Burn It All Down','Burn It All Down');
const bonus=song('s5','Test Band','Track','Track (Bonus Track)');
const last=song('s6','Manual Artist','Another Unresolved Song','Different Recording');
const failed=song('s7','Network Failure','Retry Track','Retry Track',false,'Metadata lookup failed: MusicBrainz unavailable');
const server={items:[a,victory,miley,league,bonus,last,failed],manualItems:[]};
const calls=[],events={},el={innerHTML:''};
const state={reviewQueue:{items:server.items,manualItems:[]},tracks:[],
 inventory:{done:true,totalSongs:7,review:7},verify:{total:7,totalArtists:7,songNeedsReview:7,artistNeedsReview:0}};
const sandbox={state,el,console,window:{open:()=>{}},navigator:{},setTimeout:()=>1,clearTimeout:()=>{},
 document:{addEventListener:(name,fn)=>events[name]=fn,getElementById:()=>null},api:async(route,payload)=>{
  calls.push({route,payload});
  if(route==='review/queue')return {items:[...server.items],manualItems:[...server.manualItems]};
  if(route==='review/auto-classify/start')return {running:false,finished:true,total:0,completed:0,error:''};
  if(route==='review/candidate/details'){
   const x=server.items.find(v=>v.key===payload.key)||[a,victory,miley,league,bonus,last,failed].find(v=>v.key===payload.key);
   if(!payload.candidateId)return {Name:x.artist,SourceTitle:x.song,Availability:'YouTube link available'};
   return {Name:x.candidateTitle,ArtworkUrl:'https://is1-ssl.mzstatic.com/test.jpg',
     CatalogIdentityConfirmed:x.confirmed,Availability:x.availability};
  }
  if(route==='review/manual-metadata/auto'){
   assert(['s2','s3','s4','s5'].includes(payload.key),'only exact or parenthetical songs may auto-file');
   assert(['sole-exact-song-title-unverified','sole-parenthetical-song-title-unverified'].includes(payload.reason));
   const x=server.items.find(v=>v.key===payload.key);
   server.items=server.items.filter(v=>v.key!==payload.key);
   server.manualItems.push({key:x.key,kind:'song',artist:x.artist,song:x.song,
    album:x.album,youtubeUrl:x.youtubeUrl,saveMode:'auto',reason:payload.reason});
   return {saved:true};
  }
  if(route==='review/decision'){
   const x=server.items.find(v=>v.key===payload.key);
   // The production review queue moves handled entries after unreviewed ones.
   // Stable key navigation must survive that server-side reorder.
   server.items=[...server.items.filter(v=>v.key!==payload.key),x];
   return {saved:true};
  }
  throw Error('Unexpected route '+route);
 }};
vm.createContext(sandbox);vm.runInContext(script,sandbox);
const F=sandbox.window.Flow918;
const flush=async()=>{for(let i=0;i<32;i++)await new Promise(resolve=>setImmediate(resolve));};
const click=async action=>events.click({target:{closest:selector=>selector.includes('data-flow')?{dataset:{flow:action}}:null}});
(async()=>{
 await F.open('manual');await flush();F.reviewKind='song';F.render();await flush();
 assert.equal(F.reviewHistory.song[0],'s1');
 // Navigate directly to a known source to model successive review decisions.
 F.reviewIndex.song=1;F.render();await flush();
 assert.equal(server.manualItems.length,4,'Victory, Miley Cyrus, League of Legends and parenthetical title should auto-file');
 assert.deepEqual(server.manualItems.map(x=>x.key),['s2','s3','s4','s5']);
 assert.equal(server.manualItems[3].reason,'sole-parenthetical-song-title-unverified');
 assert.equal(F.reviewIndex.song,1,'the next unresolved source should take the prior position');
 assert.match(el.innerHTML,/Another Unresolved Song/);
 await click('previous');await flush();
 assert.match(el.innerHTML,/Unresolved Song/);
 assert.match(el.innerHTML,/Manual Artist/);
 assert.equal(F.reviewIndex.song,0,'Previous must return to the last surviving previously viewed song');
 F.choice.s1=0;F.render();await flush();await click('next');await flush();
 assert.match(el.innerHTML,/Another Unresolved Song/,'Next should follow original stable review order');
 assert.equal(server.items[server.items.length-1].key,'s1','server fixture really reordered the handled entry');
 await click('previous');await flush();
 assert.match(el.innerHTML,/Unresolved Song/,'Previous must survive a reordered reviewed entry');
 assert.equal(F.reviewIndex.song,0);
 // The lookup failure must never be treated as a safe title match.
 F.reviewIndex.song=2;F.render();await flush();
 assert.match(el.innerHTML,/Retry Candidate Lookup/);
 assert.equal(server.items.some(x=>x.key==='s7'),true);
 const decisions=calls.filter(c=>c.route==='review/decision');
 assert.equal(decisions.length,1);
 assert.equal(decisions[0].payload.key,'s1','only explicit manual review may commit a recording choice');
 assert(!calls.some(c=>c.route.startsWith('lidarr/')),'automatic filing must not write to Lidarr');
 console.log('PASS: four title-equivalent songs auto-filed as source metadata; previous returns to visited song; failed lookup retained');
})().catch(e=>{console.error(e);process.exitCode=1});
