// UI regression: exact single artist name becomes local-only unverified metadata,
// while multiple MBIDs and artist/recording ambiguity stay in review.
'use strict';
const fs=require('node:fs'), path=require('node:path'), vm=require('node:vm'),assert=require('node:assert/strict');
const src=fs.readFileSync(path.join(__dirname,'../AppUI/flow918.js'),'utf8');
const ID=n=>String(n).padStart(8,'0')+'-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const hot={key:'artist:hot',kind:'artist',artist:'HOT SPOT',song:'MDMA (Hardstyle)',album:'HOT SPOT Hardstyle Remixes, Vol. 1',youtubeUrl:'https://www.youtube.com/watch?v=abcdefghijk',disposition:'unreviewed',candidates:[{id:ID(1),label:'MusicBrainz artist candidate',evidence:'Prior identity; artist MBID unverified'}]};
const multiple={key:'artist:twins',kind:'artist',artist:'Ham Sandwich',song:'TRIPPIN?',album:'TRIPPIN?',youtubeUrl:'https://www.youtube.com/watch?v=bbbbbbbbbbb',disposition:'unreviewed',candidates:[{id:ID(2),label:'MusicBrainz artist candidate',evidence:''},{id:ID(3),label:'MusicBrainz artist candidate',evidence:''}]};
const wrong={key:'artist:wrong',kind:'artist',artist:'Kanye West',song:'Gold Digger',album:'Late Registration',youtubeUrl:'https://www.youtube.com/watch?v=ccccccccccc',disposition:'unreviewed',candidates:[{id:ID(4),label:'MusicBrainz artist candidate',evidence:''},{id:ID(5),label:'MusicBrainz artist candidate',evidence:''}]};
const song={key:'song:1',kind:'song',artist:'HOT SPOT',song:'MDMA (Hardstyle)',album:'EP',youtubeUrl:'https://www.youtube.com/watch?v=ddddddddddd',disposition:'unreviewed',candidates:[{id:ID(6),label:'MusicBrainz credited track candidate',evidence:''}]};
const rows=[hot,multiple,wrong,song], pending=[],auto=[],ev={},el={innerHTML:''};
const state={reviewQueue:{items:[...rows],manualItems:[]},inventory:{done:true,review:288},verify:{totalArtists:951,artistNeedsReview:26,songNeedsReview:288,total:2505},tracks:[],inventoryItems:[]};
const media={
 ['artist:hot:']:{Name:'HOT SPOT',ArtworkUrl:''},['artist:hot:'+ID(1)]:{Name:'Hot Spot',ArtworkUrl:'',PreviewUrl:''},
 ['artist:twins:']:{Name:'Ham Sandwich'},['artist:twins:'+ID(2)]:{Name:'Ham Sandwich'},['artist:twins:'+ID(3)]:{Name:'Ham Sandwich'},
 ['artist:wrong:']:{Name:'Kanye West'},['artist:wrong:'+ID(4)]:{Name:'Kanye West Tribute Band',ArtworkUrl:'https://example.invalid/art'},['artist:wrong:'+ID(5)]:{Name:'Kanye West & Hatsune Miku',PreviewUrl:'https://example.invalid/audio'},
 ['song:1:']:{Name:'HOT SPOT'},['song:1:'+ID(6)]:{Name:'MDMA (Hardstyle)'}
};
const sandbox={state,el,console,navigator:{},window:{open:()=>{}},document:{addEventListener:(k,cb)=>ev[k]=cb,getElementById:()=>null},setTimeout:()=>1,clearTimeout:()=>{},api:(route,data)=>{
 if(route==='review/candidate/details')return new Promise(resolve=>pending.push(()=>resolve(media[data.key+':'+data.candidateId]||{})));
 if(route==='review/manual-metadata/auto'){
   auto.push(data);state.reviewQueue.items=state.reviewQueue.items.filter(x=>x.key!==data.key);
   state.reviewQueue.manualItems.push({key:data.key,kind:'artist',artist:hot.artist,song:hot.song,saveMode:'auto',reason:data.reason});
   return Promise.resolve({saved:true});
 }
 if(route==='review/queue')return Promise.resolve(state.reviewQueue);
 return Promise.resolve({});
 }};
vm.createContext(sandbox);vm.runInContext(src,sandbox);
const F=sandbox.window.Flow918;
const settle=async()=>{for(let r=0;r<10;r++){while(pending.length){pending.shift()();await new Promise(done=>setImmediate(done))}await new Promise(done=>setImmediate(done))}};
(async()=>{
 await F.open();
 assert.match(el.innerHTML,/Loading candidate names, songs and previews/);
 assert.match(el.innerHTML,/0%/);
 assert.match(el.innerHTML,/role="progressbar"[^>]+aria-valuenow="0"/);
 assert.equal(auto.length,0,'cannot auto-save while data remains pending');
 pending.shift()();await new Promise(done=>setImmediate(done));
 assert.match(el.innerHTML,/50%/,'current data loader advances when one of two requests is done');
 await settle();
 assert.deepEqual(auto.map(x=>x.reason),['sole-exact-artist-name-unverified']);
 assert(!state.reviewQueue.items.some(x=>x.key===hot.key));
 assert.match(el.innerHTML,/Ham Sandwich/,'a later multiple-candidate item remains');
 assert(!el.innerHTML.includes('Loading candidate names, songs and previews'),'loader disappears when fully loaded');
 assert.equal(state.reviewQueue.items.length,3);
 F.reviewIndex.artist=1;F.render();await settle();
 assert.equal(auto.filter(x=>x.reason==='sole-exact-artist-name-unverified').length,1,'irrelevant artist previews must not create false exact-name match');
 assert(auto.some(x=>x.key===wrong.key&&x.reason==='no-usable-candidate'),'irrelevant artist preview results should be treated as no usable artist candidate');
 assert(!el.innerHTML.includes('Kanye West Tribute Band'),'irrelevant name excluded even when artwork exists');
 assert(!el.innerHTML.includes('Hatsune Miku'),'irrelevant name excluded even when preview exists');
 // Without useful artist candidates, the existing metadata may instead be saved
 // automatically; this is still not artist verification.
 assert.equal(state.reviewQueue.manualItems.every(x=>x.saveMode==='auto'),true);
 F.reviewKind='song';F.reviewIndex.song=0;F.render();await settle();
 assert(!auto.some(x=>x.key===song.key&&x.reason==='sole-exact-artist-name-unverified'),'a song title must not auto-approve a MusicBrainz recording');
 assert.match(el.innerHTML,/Song Review/);
 console.log('PASS: HOT SPOT single-name automatic local save; 0→50→100% loading; multi-ID safety; no false artist verification; song version caution');
})().catch(err=>{console.error(err);process.exitCode=1});
