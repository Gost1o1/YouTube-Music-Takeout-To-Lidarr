const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const events={},el={innerHTML:''},calls=[];
const r1={key:'k1',kind:'artist',artist:'Dick Dale',song:'Miserlou',album:'Surfer\'s Choice',candidates:[{id:'11111111-1111-1111-1111-111111111111',label:'MusicBrainz artist candidate',evidence:'canonical; credited title matches 4'}]};
const r2={key:'k2',kind:'artist',artist:'Lazlo',song:'Sucette',album:'Lazlo',candidates:[
{id:'22222222-2222-2222-2222-222222222222',label:'MusicBrainz artist candidate',evidence:''},
{id:'33333333-3333-3333-3333-333333333333',label:'MusicBrainz artist candidate',evidence:''}]};
const r3={key:'k3',kind:'artist',artist:'Adam Church',song:'Blame It On The Backwoods',album:'Blame It On The Backwoods',youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',candidates:[]};
const state={reviewQueue:{items:[r1,r2,r3]},inventory:{done:true,review:3},verify:{totalArtists:951,artistNeedsReview:3},tracks:[]};
const pending=[],answers={
'k1:':{Name:'Dick Dale & His Del-Tones',ArtworkUrl:'',PreviewUrl:''},
['k1:'+r1.candidates[0].id]:{Name:'Dick Dale and His Del-Tones',ArtworkUrl:'',PreviewUrl:''},
'k2:':{Name:'Lazlo',ArtworkUrl:'',PreviewUrl:''},
['k2:'+r2.candidates[0].id]:{Name:'Lazlo',ArtworkUrl:'https://is1-ssl.mzstatic.com/test.jpg',PreviewUrl:'https://audio.itunes.apple.com/same.m4a'},
['k2:'+r2.candidates[1].id]:{Name:'LAZLO',ArtworkUrl:'https://is1-ssl.mzstatic.com/test.jpg',PreviewUrl:'https://audio.itunes.apple.com/same.m4a'},
'k3:':{Name:'Adam Church',ArtworkUrl:'',PreviewUrl:'',Availability:'YouTube metadata unavailable'}
};
const sandbox={state,el,console,document:{addEventListener:(e,fn)=>events[e]=fn,getElementById:()=>null},window:{open:()=>{}},navigator:{},setTimeout:(f)=>1,clearTimeout:()=>{},api:(route,data)=>{
 calls.push({route,data});
 if(route==='review/candidate/details')return new Promise(resolve=>pending.push(()=>resolve(answers[data.key+':'+data.candidateId]||{})));
 if(route==='review/manual-metadata/auto'){state.reviewQueue={items:[r1,r2],manualItems:[{key:'k3',artist:'Adam Church',song:'Blame It On The Backwoods',album:'',saveMode:'auto'}]};return Promise.resolve({saved:true});}
 if(route==='review/auto-classify/start'){
   state.reviewQueue={items:[r1,r2],manualItems:[{key:'k3',artist:'Adam Church',song:'Blame It On The Backwoods',album:'',saveMode:'auto'}]};
   return Promise.resolve({running:false,finished:true,total:1,completed:1,error:''});
 }
 if(route==='review/queue')return Promise.resolve(state.reviewQueue);
 return Promise.resolve({});
}};
vm.createContext(sandbox);vm.runInContext(source,sandbox);
const F=sandbox.window.Flow918;
const sleep=()=>new Promise(resolve=>setImmediate(resolve));
const choose=i=>events.click({target:{closest:(selector)=>selector.includes('data-option')?{dataset:{option:String(i)}}:null}});
async function run(){
 await F.open();
 await sleep();await sleep();await sleep();
 assert.match(el.innerHTML,/Loading candidate names/);
 choose(-1);assert.equal(F.choice.k1,undefined,'Cannot select none before async data arrives');
 while(pending.length){pending.shift()();await sleep()}
 await sleep();await sleep();
 assert.match(el.innerHTML,/Dick Dale and His Del-Tones/);
 assert.match(el.innerHTML,/Similar Names/); // after choosing a candidate, otherwise no evidence
}
async function test(){
 await F.open();await sleep();await sleep();await sleep();
 assert.match(el.innerHTML,/Loading candidate names/);
 choose(-1);assert.equal(F.choice.k1,undefined);
 for(let n=0;n<4;n++){while(pending.length){pending.shift()();await sleep()}await sleep()}
 assert.match(el.innerHTML,/Dick Dale and His Del-Tones/);
 choose(0);assert.match(el.innerHTML,/Similar Names/);
 // Separate MusicBrainz IDs may have identical catalog audio. Never collapse them.
 F.reviewIndex.artist=1;F.render();
 for(let n=0;n<3;n++){while(pending.length){pending.shift()();await sleep()}await sleep()}
 assert.match(el.innerHTML,/Separate MusicBrainz ID/);
 assert.equal((el.innerHTML.match(/class="fcandidate /g)||[]).length,2);
 assert.equal(state.reviewQueue.items.some(x=>x.key==='k3'),false,'No-candidate song must leave active review');
 assert.equal(state.reviewQueue.manualItems[0].saveMode,'auto');
 assert.equal(F.manualEntries.length,1);
 console.log('PASS: delayed candidate gating, alias evidence, distinct MBIDs despite shared preview, server-side automatic metadata filing');
}
test().catch(e=>{console.error(e);process.exitCode=1});
