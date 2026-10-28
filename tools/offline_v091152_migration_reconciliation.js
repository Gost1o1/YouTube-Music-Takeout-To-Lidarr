// Regression for the v0.9.1.15.1 Migration Preview defect: staging items
// loaded while the existing/unmatched source-song inventory was never fetched.
'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const program=fs.readFileSync(path.join(__dirname,'../Program.cs'),'utf8');
const id1='12345678-1111-4111-8111-123456789abc',id2='12345678-2222-4222-8222-123456789abc';
const artist={key:'artist:lazlo',kind:'artist',artist:'Lazlo',song:'Sucette',album:'Lazlo',
 youtubeUrl:'https://www.youtube.com/watch?v=csZa-YxvxSE',disposition:'candidate-for-planning',
 selected:id1,candidates:[{id:id1,label:'Lazlo',evidence:''},{id:id2,label:'Lazlo',evidence:''}]};
const server={rows:[
 {type:'song',row:1,artist:'Lazlo',title:'Sucette',album:'Lazlo',category:'Manual Review / Songs',
  reason:'Recording identity awaits verification',metadataStatus:'not-checked',artistReviewCandidateId:id1,songReviewCandidateId:''},
 {type:'song',row:2,artist:'Example',title:'Catalog Song',album:'Already Cataloged',trackId:'42',category:'To Be Added / Songs (missing file)',
  reason:'Missing file',metadataStatus:'lidarr-track-metadata-present',artistReviewCandidateId:'',songReviewCandidateId:''},
 {type:'song',row:3,artist:'Example',title:'Available Song',album:'Available Album',category:'Already in Library / Songs',
  reason:'File recorded',metadataStatus:'lidarr-track-metadata-present',artistReviewCandidateId:'',songReviewCandidateId:''}
]};
const calls=[],handlers={},el={innerHTML:''};
const state={tracks:[],reviewQueue:{items:[artist],manualItems:[]},inventory:{done:true,totalSongs:3,toAdd:1,review:1,filesRead:1},
 verify:{totalArtists:1,artistNeedsReview:1,total:3,songNeedsReview:1},inventoryItems:[]};
const sb={state,el,console,window:{open:()=>{}},navigator:{},document:{addEventListener:(name,fn)=>handlers[name]=fn,getElementById:()=>null},
 setTimeout:()=>1,clearTimeout:()=>{},api:async(route,args)=>{
  calls.push(route);
  if(route==='review/queue')return state.reviewQueue;
  if(route==='review/auto-classify/start'||route==='review/auto-classify/status')return {running:false,finished:true,total:0,completed:0,error:''};
  if(route==='review/candidate/details')return {Name:'Lazlo',PreviewUrl:args.candidateId?'https://example.invalid/shared-audio.m4a':''};
  if(route==='lidarr/inventory/staging-queue')return {items:[{albumId:81,artist:'Example',album:'Already Cataloged',safeSongs:1,staged:false}]};
  if(route==='lidarr/inventory/items')return {items:[...server.rows]};
  throw Error('Unexpected API call '+route);
 }};
vm.createContext(sb);vm.runInContext(source,sb);
const F=sb.window.Flow918;
const flush=async()=>{for(let i=0;i<12;i++)await new Promise(resolve=>setImmediate(resolve));};
async function tab(name){await handlers.click({target:{closest:selector=>selector.includes('data-tab')?{dataset:{tab:name}}:null}});}
(async()=>{
 await F.open('manual');await flush();
 assert.equal((el.innerHTML.match(/class="fcandidate /g)||[]).length,2,
  'distinct MusicBrainz identities must not collapse because they share audio');
 assert.match(el.innerHTML,/Separate MusicBrainz ID/);
 await F.open('preview');await flush();
 assert(calls.includes('lidarr/inventory/items'),'Migration Preview must fetch its inventory rows');
 assert.match(el.innerHTML,/Already Cataloged/,'To Be Added staging queue must remain usable');
 await tab('existing');
 assert.match(el.innerHTML,/Catalog Song/,'an existing Lidarr metadata record must display even without downloaded files');
 assert.match(el.innerHTML,/Available Song/,'a downloaded Lidarr record must also display');
 await tab('unmatched');
 assert.match(el.innerHTML,/Sucette/,'unmatched Lazlo recording must not disappear');
 assert.match(el.innerHTML,/Selected review candidate saved/,'the artist selection should be visible but not presented as recording verification');
 assert(!calls.includes('lidarr/inventory/stage-albums'),'viewing the preview must remain read-only');
 server.rows=server.rows.slice(0,2);
 await F.open('preview');await flush();
 assert.match(el.innerHTML,/Incomplete Lidarr comparison/,'missing source rows must block an incomplete preview');
 assert(program.includes('artistReviewCandidateId')&&program.includes('songReviewCandidateId'),
  'Lidarr inventory items must include saved choices from the review ledger');
 assert(program.includes('metadataStatus = d.MetadataStatus'),
  'Lidarr metadata status must be provided separately from audio-file presence');
 console.log('PASS: staging and inventory fetched; metadata/file overlap; 289-style unmatched rows; saved artist choice; unique MBIDs; fail closed on incomplete inventory');
})().catch(err=>{console.error(err);process.exitCode=1});
