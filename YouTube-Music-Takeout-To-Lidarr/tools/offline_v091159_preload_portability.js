// v0.9.1.15.9: background preload completes every review bundle without
// exceeding the global external metadata concurrency cap.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'..');
const code=fs.readFileSync(path.join(root,'AppUI','flow918.js'),'utf8');
const candidates=i=>[0,1].map(n=>({id:`${String(i).padStart(8,'0')}-aaaa-bbbb-cccc-${String(n).padStart(12,'0')}`,label:'Exact source',evidence:'matching artist credits'}));
const rows=Array.from({length:12},(_,i)=>({key:'review-'+i,kind:i<6?'artist':'song',artist:'Artist '+i,song:'Song '+i,album:'Album '+i,
 youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',candidates:candidates(i)}));
const state={reviewQueue:{items:rows,manualItems:[]},inventory:{done:true,review:6},verify:{totalArtists:6,artistNeedsReview:6,total:6,songNeedsReview:6},tracks:[]};
const waiting=[];let active=0,maxActive=0,total=0;
const sandbox={state,el:{innerHTML:''},console,document:{addEventListener:()=>{},getElementById:()=>null},
 window:{open:()=>{}},navigator:{},setTimeout:()=>1,clearTimeout:()=>{},
 api:(route)=>{
  if(route==='review/queue')return Promise.resolve(state.reviewQueue);
  if(route==='review/auto-classify/start')return Promise.resolve({running:false,finished:true});
  if(route==='review/candidate/details'){
   active++;total++;maxActive=Math.max(active,maxActive);
   return new Promise(resolve=>waiting.push(()=>{active--;resolve({Name:'Exact source',ArtworkUrl:'',PreviewUrl:''})}));
  }
  return Promise.resolve({});
 }};
vm.createContext(sandbox);vm.runInContext(code,sandbox);
const tick=()=>new Promise(r=>setImmediate(r));
(async()=>{
 assert.equal(sandbox.window.Flow918.active,false);
 await sandbox.window.Flow918.prewarmDuringPipeline();
 await tick();
 assert.ok(total>0,'Candidate fetching should begin while the matching progress screen is still active');
 assert.equal(sandbox.window.Flow918.active,false,'Background preloading must not change the current UI page');
 await sandbox.window.Flow918.open('manual');
 for(let i=0;i<350;i++){
  await tick();
  if(waiting.length)waiting.shift()();
  if(total===rows.length*3&&active===0)break;
 }
 await tick();await tick();
 assert.equal(total,rows.length*3,'Each source and all candidates must be fetched once');
 assert.ok(maxActive<=3,`Metadata concurrency exceeded limit: ${maxActive}`);
 assert.equal(sandbox.window.Flow918.mediaLoading.size,0);
 for(const row of rows){
  assert.ok(sandbox.window.Flow918.media[row.key+':'],'Source details not cached: '+row.key);
  for(const c of row.candidates)assert.ok(sandbox.window.Flow918.media[row.key+':'+c.id]);
 }
 const cs=fs.readFileSync(path.join(root,'src','Program.cs'),'utf8');
 assert.match(cs,/Path\.Combine\(AppContext\.BaseDirectory, "RunData"\)/);
 assert.match(cs,/CleanupOwnedRuntimeFiles\(\)/);
 for(const c of ['GuidedReviewV917.cs','ManualFallbackV912.cs','LidarrImport.cs']){
  const src=fs.readFileSync(path.join(root,'src',c),'utf8');
  assert.ok(!src.includes('Environment.SpecialFolder.MyDocuments'),c+' must not write to Documents');
 }
 console.log('PASS: full review preload, at most 3 metadata requests active, all runtime files under EXE');
})().catch(e=>{console.error(e);process.exitCode=1});
