// Node-only UI wiring replay, no browser or network. Tests working state and routes.
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const handlers={};
const el={innerHTML:''};
const calls=[];
const record = {key:'review-artist-1',kind:'artist',artist:'Blue Oyster Cult',song:'',album:'',
  candidates:[{id:'10000000-0000-0000-0000-000000000001',label:'Blue Öyster Cult',evidence:'canonical alias; credited title matches 10'}],
  disposition:'unreviewed',selected:''};
const state={tracks:[],verify:{total:2505,totalArtists:951,artistNeedsReview:68,songNeedsReview:297},
 inventory:{done:true,toAdd:2208,review:297,filesRead:0},reviewQueue:{items:[record]},inventoryItems:[]};
const sandbox={state,el,console,document:{addEventListener:(k,cb)=>handlers[k]=cb},
  window:{open:()=>{}},navigator:{clipboard:{writeText:async()=>{}}},setTimeout:(fn)=>{sandbox.pending=fn;return 1},clearTimeout:()=>{},
  api:async (name,data)=>{
    calls.push({name,data});
    if(name==='review/queue')return {items:[record],manualItems:[]};
    if(name==='review/auto-classify/start')return {running:false,finished:true,total:0,completed:0,error:''};
    if(name==='review/candidate/details')return {Name:'Blue Öyster Cult',ArtworkUrl:'',PreviewUrl:''};
    if(name==='review/decision')return {localOnly:true};
    if(name==='lidarr/inventory/staging-queue')return {items:[{albumId:101,artist:'Blue Öyster Cult',album:'Fire of Unknown Origin',safeSongs:1,staged:false}]};
    if(name==='lidarr/inventory/stage-albums')return {localOnly:true,lidarrWrites:0};
    if(name==='github/status')return {configured:true,authorized:false,starred:false};
    if(name==='github/authorize/start')return {userCode:'CODE-1234',pollAfterSeconds:5,verificationUri:'https://github.com/login/device'};
    if(name==='github/star')return {starred:true};
    return {};
  }};
vm.createContext(sandbox);
vm.runInContext(script,sandbox);
const F=sandbox.window.Flow918;
function clickFlow(action){return handlers.click({target:{closest(selector){return selector.includes('data-flow')?{dataset:{flow:action}}:null}}});}
function clickOption(option){return handlers.click({target:{closest(selector){return selector.includes('data-option')?{dataset:{option:String(option)}}:null}}});}
(async()=>{
  await F.open();
  await new Promise(resolve=>setImmediate(resolve)); // Await candidate-name preload before interacting.
  if(!el.innerHTML.includes('Manual Review')||!el.innerHTML.includes('None of these'))throw Error('Manual UI absent');
  await clickOption(0);if(!el.innerHTML.includes('10 Similar Songs'))throw Error('Evidence chips missing');
  await clickFlow('next');if(!calls.some(c=>c.name==='review/decision'))throw Error('Next did not save review');
  await clickFlow('open-preview');
  if(!el.innerHTML.includes('Migration Preview'))throw Error('Preview UI absent');
  handlers.change({target:{id:'flow918-selectall',checked:true}});
  if(!el.innerHTML.includes('1 of 1 selected'))throw Error('Select All failed');
  await clickFlow('open-final');if(!el.innerHTML.includes('Finalize Lidarr Import'))throw Error('Final page absent');
  await clickFlow('confirm');
  if(!calls.some(c=>c.name==='lidarr/inventory/stage-albums'&&c.data.stage===true))throw Error('Local staging was not called');
  if(!el.innerHTML.includes('Album selection prepared'))throw Error('Local staging summary absent');
  if(el.innerHTML.includes('fconfetti'))throw Error('Local staging must not celebrate before Lidarr confirms catalog changes');
  await clickFlow('about');if(!el.innerHTML.includes('Quinton Burgess')||!el.innerHTML.includes('Gost1o1'))throw Error('About page absent');
  await clickFlow('github-start');if(!el.innerHTML.includes('CODE-1234'))throw Error('GitHub authorization UI absent');
  // No real star is sent until user explicitly presses Star.
  if(calls.some(c=>c.name==='github/star'))throw Error('GitHub star called without authorization and explicit click');
  console.log('PASS: manual selection, migration select-all, local staging without premature confetti, developer page, GitHub authorization');
})().catch(e=>{console.error(e);process.exitCode=1});
