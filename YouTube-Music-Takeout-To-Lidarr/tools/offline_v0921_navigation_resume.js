// Integration-style Node VM replay with a simulated browser refresh (new JS realm).
const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const html=fs.readFileSync(path.join(__dirname,'../AppUI/index.html'),'utf8');
const inline=html.match(/<script>([\s\S]*?)<\/script>/)[1];
const flow=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const idle={running:false,done:false,failed:false},done={running:false,done:true,failed:false};
const track={Artist:'Blue Oyster Cult',Song:"Burnin' for You",YouTubeUrl:''};
const review={key:'artist:Blue Oyster Cult',kind:'artist',artist:'Blue Oyster Cult',song:"Burnin' for You",album:'Fire of Unknown Origin',candidates:[{id:'10000000-0000-0000-0000-000000000001',label:'Blue Öyster Cult',evidence:'canonical alias; credited title matches 10'}],disposition:'unreviewed',selected:''};
const stage=[{albumId:101,artist:'Blue Öyster Cult',album:'Fire of Unknown Origin',safeSongs:1,staged:true},{albumId:102,artist:'Marina',album:'The Family Jewels',safeSongs:2,staged:false}];
function makeRunner(store,opts={}){
 const element={innerHTML:''},events={},timers=[],calls=[];
 const stateObj={archive:opts.hasData?'C:/takeout.zip':'',analyzing:!!opts.analyzing,tracks:opts.hasData?[track]:[],lidarrConnected:!!opts.connected,lidarrUrl:'http://127.0.0.1:8686'};
 const pipeline=opts.pipeline||idle;
 const handlers={};
 const sb={console,Set,Map,Error,URL,JSON,Math,Date,Number,String,Promise,Array,Object,Boolean,
  crypto:{randomUUID:()=>"123e4567-e89b-42d3-a456-426614174000"},
  setTimeout(fn){timers.push(fn);return timers.length},clearTimeout(){},setInterval(fn){return 1},clearInterval(){},
  sessionStorage:{getItem(k){return store.get(k)||null},setItem(k,v){store.set(k,v)},removeItem(k){store.delete(k)}},
  document:{getElementById(id){return id==='app'?element:null},addEventListener(k,fn){handlers[k]=fn}},
  navigator:{clipboard:{writeText:async()=>{}}},
  window:{addEventListener(k,fn){events[k]=fn},open(){},Flow918:undefined},
  fetch:async function(url){calls.push(url);const endpoints={
   '/api/pipeline/status':pipeline,'/api/state':stateObj,'/api/lidarr/import/status':{total:0,restored:false},
   '/api/lidarr/inventory/status':{done:true,totalSongs:1,review:293,toAdd:2212,filesRead:0},
   '/api/review/queue':{items:[review,{...review,key:'song:001',kind:'song'}]},
   '/api/lidarr/inventory/staging-queue':{items:stage},
   '/api/lidarr/inventory/items':{items:[{type:'song',row:1,artist:'Blue Oyster Cult',title:"Burnin' for You",album:'Fire of Unknown Origin',category:'Manual Review / Songs'}]},
   '/api/review/auto-classify/start':{running:false,finished:true,total:0,completed:0,error:''},
   '/api/review/candidate/details':{Name:'Blue Öyster Cult',ArtworkUrl:'',PreviewUrl:''},
   '/api/lidarr/inventory/stage-albums':{ok:true,localOnly:true},
   '/api/github/status':{configured:false,authorized:false,starred:false}
  };if(!(url in endpoints))throw Error('UNEXPECTED REQUEST '+url);
  return {ok:true,json:async()=>endpoints[url]}
  }
 };
 vm.createContext(sb);vm.runInContext(inline,sb);vm.runInContext(flow,sb);
 return {sb,element,events,handlers,calls,timers,async restore(){await vm.runInContext('restoreNavigationOnLoad()',sb);await new Promise(setImmediate)},
  get step(){return vm.runInContext('state.step',sb)},get F(){return sb.window.Flow918},
  val(x){return vm.runInContext(x,sb)},
  click(action){return handlers.click({target:{closest(selector){return selector.includes('data-flow')?{dataset:{flow:action}}:null}}})}
 };
}
function assert(test,why){if(!test)throw Error(why)}
const withoutWrites=calls=>!calls.some(x=>['/api/scan','/api/analyze','/api/pipeline/start','/api/lidarr/connect'].includes(x));
(async()=>{
 const storage=new Map();let w=makeRunner(storage);await w.restore();w.val('changeStep(2)');
 assert(w.step===2,'step 2 navigation failed');
 w=makeRunner(storage);await w.restore();assert(w.step===2&&w.element.innerHTML.includes('Prepare Your YouTube Music Export'),'refresh did not restore Step 2');
 w.val('changeStep(3)');w=makeRunner(storage);await w.restore();assert(w.step===3,'refresh did not restore Step 3');
 assert(withoutWrites(w.calls),'early wizard refresh unexpectedly triggered write or scan');
 for(const step of [4,5]){
  const s=new Map();const init=makeRunner(s,{hasData:true});await init.restore();init.val('changeStep('+step+')');
  const restored=makeRunner(s,{hasData:true});await restored.restore();
  assert(restored.step===step,'refresh did not restore Step '+step);
  assert(withoutWrites(restored.calls),'Step '+step+' restarted scan or Lidarr');
 }
 {
  const saved=new Map();const first=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await first.restore();
  assert(first.F.active&&first.F.page==='manual','completed pipeline did not open review');
  first.F.reviewKind='song';first.F.reviewIndex.song=1;first.F.choice['song:001']=0;first.F.render();
  const restored=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await restored.restore();
  assert(restored.F.page==='manual'&&restored.F.reviewKind==='song'&&restored.F.reviewIndex.song===0,'manual review tab/index did not restore within current queue');
  assert(restored.F.choice['song:001']===0,'pending candidate choice not restored');
  assert(withoutWrites(restored.calls),'manual restore invoked a scan');
 }
 {
  const saved=new Map();const first=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await first.restore();
  await first.F.open('preview');first.F.selected=new Set([101,102]);first.F.tab='existing';first.F.page='preview';first.F.render();
  const second=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await second.restore();
  assert(second.F.page==='preview'&&second.F.tab==='existing','migration tab not restored');
  assert(second.F.selected.has(101)&&second.F.selected.has(102),'selected albums not restored');
  second.F.page='final';second.F.render();
  const third=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await third.restore();
  assert(third.F.page==='final'&&third.element.innerHTML.includes('Finalize Lidarr Import'),'final confirmation not restored');
 }
 {
  const saved=new Map();const first=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await first.restore();
  await first.F.open('preview');first.F.selected=new Set([101,102]);first.F.job=[...stage];first.F.completed=0;first.F.page='progress';first.F.render();
  const second=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await second.restore();
  assert(second.F.page==='progress'&&second.F.interrupted,'interrupted staging did not restore as paused');
  assert(second.F.completed===1,'did not reconcile staged prefix with server');
  assert(!second.calls.includes('/api/lidarr/inventory/stage-albums'),'refresh must not submit an import stage');
  await second.click('resume-plan');
  assert(second.calls.filter(x=>x==='/api/lidarr/inventory/stage-albums').length===1,'resume must send only the single remaining stage');
  assert(second.F.page==='complete','resume did not complete safely');
  const next=makeRunner(saved,{hasData:true,pipeline:done,connected:true});
  stage[1].staged=true;await next.restore();
  assert(next.F.page==='complete','completion screen did not restore');
 }
 {
  const saved=new Map();const started=makeRunner(saved);await started.restore();started.val('changeStep(2)');
  const active=makeRunner(saved,{hasData:true,pipeline:{running:true,done:false,failed:false,songsTotal:2505,songsChecked:1422,totalArtists:951,artistsMatched:400,progressPercent:42,elapsedSeconds:7200}});
  await active.restore();
  assert(active.step===6&&active.element.innerHTML.includes('Verifying Your Music Library'),'running server work did not take precedence');
  assert(withoutWrites(active.calls),'refresh re-ran processing');
 }
 {
  // A finished scan must not override a deliberate return to Step 2.
  const saved=new Map();const a=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await a.restore();
  a.F.active=false;a.val('changeStep(2)');
  const b=makeRunner(saved,{hasData:true,pipeline:done,connected:true});await b.restore();
  assert(b.step===2&&b.element.innerHTML.includes('Prepare Your YouTube Music Export'),'deliberately visited Step 2 was overridden');
 }
 console.log('PASS: steps 2/3/4/5, running verification, manual selection, migration preview, final, interrupted plan, completed screen, and no auto re-scan/re-import');
})().catch(e=>{console.error(e.stack||e);process.exitCode=1});
