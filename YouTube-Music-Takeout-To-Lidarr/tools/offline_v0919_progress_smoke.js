// Offline browser-simulation fixture: no real API calls, media requests or storage.
const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const html=fs.readFileSync(path.join(__dirname,'../AppUI/index.html'),'utf8');
const script=html.match(/<script>([\s\S]*?)<\/script>/)[1].replace("const SESSION='__LOCAL_SESSION_TOKEN__';", "const SESSION='OFFLINE';");
const element={innerHTML:''};
const pending=[];const apiCalls=[];
const sandbox={
 console,Set,Map,Error,URL,JSON,Math,Date,Number,String,
 setTimeout(fn){pending.push(fn);return pending.length},clearTimeout(){},
 document:{getElementById(id){return id==='app'?element:null}},
 window:{addEventListener(){},Flow918:{active:false,open:async function(page){sandbox.opened=page;this.active=true}},open(){}},
 fetch:async function(url,request){apiCalls.push(url);if(url==='/api/lidarr/connect')return {ok:true,json:async()=>({connected:true,automaticVerificationStarted:true,artistRecords:[]})};
 if(url==='/api/pipeline/status')return {ok:true,json:async()=>({running:false,done:true,failed:false,songsChecked:2505,songsTotal:2505,artistsMatched:882,totalArtists:951,progressPercent:100,elapsedSeconds:1000})};
 if(url==='/api/lidarr/inventory/status')return {ok:true,json:async()=>({done:true,toAdd:2212,review:293})};
 throw Error('Unexpected offline URL '+url);
 }
};
vm.createContext(sandbox);vm.runInContext(script,sandbox);
(async()=>{
 vm.runInContext("state.step=5;state.tracks=[{Artist:'Example',Song:'One'}];state.key='LOCAL-TEST-KEY';state.lidarr='http://127.0.0.1:8686';",sandbox);
 await vm.runInContext('connect()',sandbox);
 if(apiCalls[0]!=='/api/lidarr/connect')throw Error('Expected connect first');
 if(pending.length<1)throw Error('Connected screen did not schedule automatic verification');
 await pending.shift()();
 await new Promise(setImmediate); // allow async status polling and review opening to finish
 if(!apiCalls.includes('/api/pipeline/status'))throw Error('Unified pipeline did not start polling');
 if(sandbox.opened!=='manual')throw Error('Completed pipeline did not open manual review');
 if(!element.innerHTML.includes('Verifying Your Music Library'))throw Error('Progress page was not shown');
 if(element.innerHTML.includes('Current Stage')||element.innerHTML.includes('Already completed a previous scan?'))throw Error('Legacy UI still visible');
 if(!element.innerHTML.includes('Verification speed')||!element.innerHTML.includes('Time remaining'))throw Error('Required simple progress metrics missing');
 console.log('PASS: Lidarr connection auto-verification, single progress UI, successful automatic Manual Review transition');
})().catch(e=>{console.error(e);process.exitCode=1});
