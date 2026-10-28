/* v0.9.1.15.3 song source-only fallback and reliable manual continuation. */
(() => {
 'use strict';
 const F={active:false,page:'manual',reviewKind:'artist',reviewIndex:{artist:0,song:0},choice:{},selected:new Set(),tab:'add',stageItems:[],job:[],completed:0,failed:false,gh:null,ghPoll:null,error:'',working:false,media:{},mediaLoading:new Set(),mediaPromises:new Map(),bundlePromises:new Map(),manualEntries:[],autoSaving:new Set(),autoAttempted:new Set(),autoFailed:new Set(),classifying:false,classifyStatus:null,classifyJob:null,editingEntry:null,youtubePlayer:null,audio:null,audioTimer:null};
 const root=()=>document.getElementById('flow918');
 const E=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const num=x=>Number(x||0).toLocaleString();
 const candidates=x=>(x?.candidates||[]).slice(0,12);
 const records=()=>((state.reviewQueue?.items||[]).filter(x=>x.kind===F.reviewKind));
 const current=()=>records()[Math.min(F.reviewIndex[F.reviewKind],records().length-1)];
 const key=x=>x?.key||'';
 const initials=s=>String(s||'?').split(/\s+/).slice(0,2).map(a=>a.charAt(0).toUpperCase()).join('')||'?';
 const art=s=>`<div class="fart" aria-hidden="true">${E(initials(s))}</div>`;
 const btn=(text,action,klass='',disabled=false)=>`<button type="button" class="fbtn ${klass}" data-flow="${action}" ${disabled?'disabled':''}>${text}</button>`;
 const warning=()=>F.error?`<div class="ferror" role="alert">${E(F.error)}</div>`:'';
 function rankEvidence(x,c){
   const words=(c?.evidence||'').toLowerCase();const result=[];
   const norm=s=>String(s||'').normalize('NFKD').replace(/[\u0300-\u036f]/g,'').toLowerCase().replace(/[^a-z0-9]/g,'');
   if(x?.kind==='artist'&&norm(x.artist)===norm(c?.label)) result.push('Similar Names');
   if(words.includes('alias')||words.includes('canonical'))result.push('Similar Names');
   const matching=words.match(/(?:credited title matches|matching titles?)\s*[:=]?\s*(\d+)/);
   if(matching&&Number(matching[1])>0)result.push(`${matching[1]} Similar Songs`);
   if(words.includes('album'))result.push('Album Evidence');
   if(words.includes('title')&&x?.kind==='song')result.push('Similar Song Title');
   if(words.includes('artist credit'))result.push('Matching Artist Credits');
   return [...new Set(result)];
 }
 function realStats(kind){
  const q=state.reviewQueue?.items||[];
  if(kind==='artist'){
   const total=state.verify?.totalArtists||new Set(state.tracks.map(t=>String(t.Artist||'').trim().toLowerCase()).filter(Boolean)).size||951;
   const review=state.verify?.artistNeedsReview??q.filter(x=>x.kind==='artist').length;
   return {total, matched:Math.max(0,total-review),unmatched:Math.min(total,review)};
  }
  const total=state.verify?.total||state.tracks.length||2505;
  const review=state.inventory?.done?state.inventory?.review:state.verify?.songNeedsReview;
  const reviewCount=typeof review==='number'?review:q.filter(x=>x.kind==='song').length;
  return {total,matched:Math.max(0,total-reviewCount),unmatched:Math.min(total,reviewCount)};
 }
 function progressCard(name,kind){const st=realStats(kind),pct=st.total?Math.round(st.matched/st.total*100):0;return `<div class="fstat"><h2 style="text-align:center">${name}</h2><div class="fbar"><i style="width:${pct}%"></i></div><div class="fnums"><div class="fnum"><b>${num(st.matched)} / ${num(st.total)}</b><span>Matched</span></div><div class="fnum"><b>${num(st.unmatched)}</b><span>Unmatched</span></div></div></div>`}
 function selectedChoice(x){const saved=candidates(x).findIndex(c=>c.id===x?.selected);return F.choice[key(x)]!==undefined?F.choice[key(x)]:x?.disposition==='needs-research'?-1:x?.disposition==='candidate-for-planning'&&saved>=0?saved:null}
 function mediaKey(x,id=''){return x?.key+':'+id}
 function reviewPicture(x,id,alt){
   const m=F.media[mediaKey(x,id)];
   const picture=m?.ArtworkUrl||m?.artworkUrl;
   return picture?`<img class="factual-art" src="${E(picture)}" alt="Album artwork for ${E(alt)}" loading="lazy"/>`:
     `<div class="fnoart" aria-label="Artwork unavailable">No artwork<br/>available</div>`;
 }
 function reviewAudio(x,id){
   const m=F.media[mediaKey(x,id)];
   // A genuine source-video preview is better than a different catalog recording.
   // Display a real YouTube player on explicit click, never hidden/background audio.
   if(!id && /^https:\/\/(?:www\.|music\.)?youtube\.com\/watch\?v=[A-Za-z0-9_-]{11}$/.test(x.youtubeUrl||'') &&
      !(m?.Availability||'').toLowerCase().includes('unavailable'))
     return `<button class="fplay playable" type="button" data-source-video="1">▶ Play original 10s</button><div class="fvideo-host"></div>`;
   const url=m?.PreviewUrl||m?.previewUrl;
   return url?`<button class="fplay playable" type="button" data-preview="${E(id||'source')}">${F.audio?.datasetId===mediaKey(x,id)?'■ Stop & reset':'▶ Play 10s'}</button>`:
      `<button class="fplay" type="button" disabled title="No verified preview for this candidate">10s preview unavailable</button>`;
 }
 function loadStatus(x){
   if(!x)return {done:0,total:0,pct:100};
   const ids=['',...candidates(x).map(c=>c.id)];
   const done=ids.filter(id=>Object.prototype.hasOwnProperty.call(F.media,mediaKey(x,id))).length;
   return {done,total:ids.length,pct:ids.length?Math.round(100*done/ids.length):100};
 }
 function loadingIndicator(x,prefix='Loading matching details'){
   const p=loadStatus(x);
   return `<div class="floading" aria-live="polite"><div class="floadhead"><strong>${E(prefix)}</strong><span>${p.pct}%</span></div><div class="floadbar" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${p.pct}" aria-label="Matching details loaded"><i style="width:${p.pct}%"></i></div><div class="muted">${p.done} of ${p.total} metadata checks completed</div></div>`;
 }
 function mediaLoaded(x){return !!x && ['',...candidates(x).map(c=>c.id)].every(id=>Object.prototype.hasOwnProperty.call(F.media,mediaKey(x,id)))}
 function fetchReviewMedia(x,id=''){
   const k=mediaKey(x,id);
   if(F.media[k])return Promise.resolve(F.media[k]);
   if(F.mediaPromises.has(k))return F.mediaPromises.get(k);
   F.mediaLoading.add(k);
   const request=api('review/candidate/details',{key:x.key,candidateId:id})
     .catch(e=>({Name:'',ArtworkUrl:'',PreviewUrl:'',Availability:'Metadata lookup failed: '+e.message}))
     .then(m=>{F.media[k]=m;return m})
     .finally(()=>{F.mediaLoading.delete(k);F.mediaPromises.delete(k);
        if(F.active&&F.page==='manual'&&current()?.key===x.key)F.render();});
   F.mediaPromises.set(k,request);
   return request;
 }
 function ensureBundle(x,onlyPrefetch=false){
   if(!x)return Promise.resolve();
   if(F.bundlePromises.has(x.key))return F.bundlePromises.get(x.key);
   const p=Promise.all(['',...candidates(x).map(c=>c.id)].map(id=>fetchReviewMedia(x,id)))
      .then(()=>{
        if(F.active&&F.page==='manual'&&
          (current()?.key===x.key || records().findIndex(y=>y.key===x.key)===F.reviewIndex[F.reviewKind]+1)) F.render();
        // Get one item ahead so a user never has to choose before candidates load.
        const group=records(),i=group.findIndex(y=>y.key===x.key);
        if(!onlyPrefetch&&i>=0&&i+1<group.length){const next=group[i+1];
          // Only one look-ahead, never recursively prefetch the entire queue.
          if(!F.bundlePromises.has(next.key))ensureBundle(next,true);
        }
      });
   F.bundlePromises.set(x.key,p);
   return p;
 }
 async function refreshManualQueue(){
    const latest=await api('review/queue');
    if(!Array.isArray(latest?.items))throw Error('Unable to reload the manual review queue.');
    state.reviewQueue=latest;
    F.manualEntries=Array.isArray(latest.manualItems)?latest.manualItems:[];
    for(const kind of ['artist','song'])
      F.reviewIndex[kind]=Math.min(F.reviewIndex[kind],Math.max(0,recordsFor(kind).length-1));
    // No cached choice may silently carry over to a different review row.
    const valid=new Set((latest.items||[]).map(x=>x.key));
    for(const k of Object.keys(F.choice))if(!valid.has(k))delete F.choice[k];
    F.error='';F.render();
 }
 function recordsFor(kind){return (state.reviewQueue?.items||[]).filter(x=>x.kind===kind)}
 // Pre-review classification runs before the artist/song queue is displayed.
 // It handles complete no-candidate songs and conservative artist-name cases.
 // The progress percentage is
 // server-reported completed checks; no synthetic percentages or repeat scans.
 async function classifyBeforeReview(){
   if(F.classifyJob)return F.classifyJob;
   F.classifyJob=(async()=>{
     try{
       let status=await api('review/auto-classify/start');
       if(!status||typeof status.running!=='boolean')return;
       F.classifying=!!status.running;F.classifyStatus=status;F.render();
       while(status.running){
         await new Promise(resolve=>setTimeout(resolve,850));
         status=await api('review/auto-classify/status');
         F.classifyStatus=status;F.render();
       }
       await refreshManualQueue();
       if(status.error)F.error=status.error;
     }catch(e){F.error='Automatic review preparation unavailable: '+e.message}
     finally{F.classifying=false;F.classifyJob=null;F.render()}
   })();
   return F.classifyJob;
 }
 function classificationPage(){
   const p=F.classifyStatus||{},n=Number(p.completed||0),total=Number(p.total||0);
   const pct=total?Math.round(n/total*100):0;
   return `<h1>Preparing Manual Review</h1><section class="fbox"><div class="floadhead"><strong>Checking artist and song metadata</strong><span>${pct}%</span></div>
     <div class="floadbar" role="progressbar" aria-label="Review preparation" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${pct}"><i style="width:${pct}%"></i></div>
     <div class="muted">${n} of ${total} artist checks completed</div></section>`;
 }
 // The server files fully known zero-candidate songs during preflight. A song
 // may also have raw IDs that prove unusable only after its display metadata
 // finishes loading; source-only filing then uses the idempotent server API.
 function failedCandidateLookup(x){return candidates(x).some(c=>{
   const m=F.media[mediaKey(x,c.id)]||{};
   return String(m.Availability||m.availability||'').toLowerCase().includes('metadata lookup failed');
 });}
 function automaticMetadataReason(x){
   if(!x||x.kind!=='song'||(x.disposition&&x.disposition!=='unreviewed')||!mediaLoaded(x)||
      failedCandidateLookup(x)||!knownName(x.artist)||!knownName(x.song)||
      !knownName(x.album)||!/^https:\/\/(?:www\.|music\.)?youtube\.com\/watch\?v=[A-Za-z0-9_-]{11}$/.test(x.youtubeUrl||'')||
      x.manualOverride||F.choice[x.key]!==undefined)return '';
   const groups=visibleCandidates(x);
   if(groups.length===0)return 'no-usable-candidate';
   return '';
 }
 function tryAutoSaveUnresolved(x){
   const reason=automaticMetadataReason(x);
   if(!reason||F.autoAttempted.has(x.key)||F.autoSaving.has(x.key)||F.autoFailed.has(x.key))return;
   F.autoAttempted.add(x.key);F.autoSaving.add(x.key);
   api('review/manual-metadata/auto',{key:x.key,reason})
     .then(()=>refreshManualQueue())
     .catch(err=>{F.autoFailed.add(x.key);F.error='Automatic metadata saving failed: '+err.message;F.render()})
     .finally(()=>{F.autoSaving.delete(x.key)});
 }
 function enrichCurrentReview(){const x=current();if(!x||F.page!=='manual')return;
   if(mediaLoaded(x)&&x.kind==='song')tryAutoSaveUnresolved(x);
   if(F.bundlePromises.has(x.key)&&mediaLoaded(x)){
     const group=records(),i=group.findIndex(y=>y.key===x.key);
     if(i>=0&&i+1<group.length){const next=group[i+1];
       if(!F.bundlePromises.has(next.key))ensureBundle(next,true);
     }
   }else ensureBundle(x);
 }
 function normalizedName(s){return String(s||'').normalize('NFKD').replace(/[\u0300-\u036f]/g,'').replace(/&/g,' and ').toLowerCase().replace(/[^a-z0-9]/g,'')}
 function knownName(s){return !!s && !/^(?:-|unknown|\[unknown\]|\[no artist\]|no artist|name unavailable|musicbrainz artist candidate|musicbrainz credited track candidate|lidarr artist candidate|prior artist mbid|isrc recording candidate|prior recording identity|lidarr candidate recording|musicbrainz artist|recording identity)$/i.test(String(s).trim())}
 function visibleCandidates(x){
   const result=[],byIdentity=new Map();
   for(const [i,c] of candidates(x).entries()){
     const m=F.media[mediaKey(x,c.id)]||{};
     const display=knownName(m.Name||m.name)?(m.Name||m.name):knownName(c.label)?c.label:'';
     const artwork=m.ArtworkUrl||m.artworkUrl||'',preview=m.PreviewUrl||m.previewUrl||'';
     if(!display&&!artwork&&!preview)continue;
     const norm=normalizedName(display),sourceNorm=normalizedName(x.kind==='song'?x.song:x.artist);
     const titleSupport=/credited title matches\s*[:=]?\s*[1-9]|matching artist credit|artist credit match|album and title/i.test(c.evidence||'');
     // A substring of an artist name is not an artist match (e.g. a tribute
     // act, collaboration, or parody). Album/song-credit evidence is separate.
     const nameSupported=!!(sourceNorm&&norm&&sourceNorm===norm);
     // A random MusicBrainz search hit is not useful without a name relationship,
     // a credited recording relationship, or independently correlated media.
     // Artwork or a preview from a *different* artist is not relevant identity evidence.
     if(x.kind==='artist'&&!nameSupported&&!titleSupport)continue;
     if(x.kind!=='artist'&&!nameSupported&&!titleSupport&&!artwork&&!preview)continue;
     // Different MusicBrainz IDs remain separately selectable even when a
     // catalog provider gives them the same name, artwork or audio sample.
     const evidenceKey=String(c.id||'').toLowerCase();
     const prev=byIdentity.get(evidenceKey);
     if(prev!==undefined){
       const old=result[prev],better=norm&&norm===sourceNorm&&normalizedName(old.display)!==norm;
       if(better){old.i=i;old.c=c;old.m=m;old.display=display}
       old.duplicateCount++;continue;
     }
     byIdentity.set(evidenceKey,result.length);
     result.push({i,c,m,display:display||'Unidentified catalog item',duplicateCount:1});
   }
   return result;
 }
 function stopReviewVideo(){
   if(F.youtubePlayer){try{F.youtubePlayer.remove()}catch(_){ }F.youtubePlayer=null}
   document.querySelectorAll?.('#flow918 [data-source-video]').forEach(b=>b.textContent='▶ Play original 10s');
 }
 function stopReviewAudio(){
   stopReviewVideo();
   if(F.audioTimer){clearInterval(F.audioTimer);F.audioTimer=null}
   if(F.audio){try{F.audio.pause();F.audio.currentTime=0}catch(_){}F.audio=null}
 }
 function toggleReviewAudio(sourceId){
   const x=current();if(!x)return;
   const id=sourceId==='source'?'':sourceId;
   const media=F.media[mediaKey(x,id)];
   const url=media?.PreviewUrl||media?.previewUrl;
   if(!url)return;
   if(F.audio&&F.audio.datasetId===mediaKey(x,id)){stopReviewAudio();F.render();return}
   stopReviewAudio();
   const audio=new Audio(url);audio.datasetId=mediaKey(x,id);F.audio=audio;
   audio.addEventListener('ended',()=>{stopReviewAudio();F.render()},{once:true});
   audio.play().catch(()=>{stopReviewAudio();F.error='The provider blocked this audio preview. The review candidate has not changed.';F.render()});
   F.audioTimer=setInterval(()=>{if(F.audio&&F.audio.currentTime>=10){stopReviewAudio();F.render()}},180);
   F.render();
 }
 function missingSourceFields(x){const missing=[];
   if(!knownName(x?.artist))missing.push('Artist');
   if(!knownName(x?.song))missing.push('Song');
   if(!knownName(x?.album))missing.push('Album');
   if(!/^https:\/\/(?:www\.|music\.)?youtube\.com\/watch\?v=[A-Za-z0-9_-]{11}$/.test(x?.youtubeUrl||''))missing.push('YouTube link');
   return missing;
 }
 function noCandidateActions(x,groups,automatic){
   if(automatic)return '<div class="fsourcewarning"><strong>Saving automatically:</strong> Original song metadata is being preserved for later manual import. No MusicBrainz recording is being verified.</div>';
   if(groups.length)return '';
   if(failedCandidateLookup(x))return '<div class="fsourcewarning">Candidate information could not finish loading. Retry before treating this as a missing match.</div>'+btn('Retry Candidate Lookup','retry-candidate-details')+btn('Enter Metadata Manually','manual-fallback');
   const missing=missingSourceFields(x);
   if(missing.length)return `<div class="fsourcewarning"><strong>Missing source metadata:</strong> ${E(missing.join(', '))}. Complete what you can, then save to continue reviewing songs.</div>`+btn('Complete Missing Metadata','manual-fallback');
   return '<div class="fsourcewarning">No usable matching candidate. Your original song information can be preserved for manual import.</div>'+btn('Save Metadata Manually','manual-fallback');
 }
 function manual(){
   const entries=records(),x=current(),pos=Math.min(F.reviewIndex[F.reviewKind],Math.max(0,entries.length-1));
   const ready=mediaLoaded(x),groups=ready?visibleCandidates(x):[];
   const autoReason=automaticMetadataReason(x);
   const automatic=!!(autoReason&&!F.autoFailed.has(x.key));
   const sel=selectedChoice(x);
   const forthcoming=entries[pos+1];
   const nextReady=!forthcoming||mediaLoaded(forthcoming);
   const activeChoice=groups.find(g=>g.i===sel);
   const evid=sel===null||sel===-1||!activeChoice?[]:rankEvidence(x,activeChoice.c);
   const source=F.media[mediaKey(x,'')]||{};
   const sourceArtist=knownName(source.Name||source.name)?(source.Name||source.name):knownName(x?.artist)?x.artist:'Artist unknown';
   const sourceSong=knownName(source.SourceTitle||source.sourceTitle)?(source.SourceTitle||source.sourceTitle):knownName(x?.song)?x.song:'Song title unavailable';
   const noSource=!knownName(x?.artist)&&!knownName(x?.song)&&!source.SourceTitle&&!source.sourceTitle&&
       !source.ArtworkUrl&&!source.artworkUrl&&!x?.youtubeUrl;
   const unavailable=source.Availability||source.availability||'';
   return `<h1>Manual Review</h1><div class="fstats">${progressCard('Artist Matching','artist')}${progressCard('Song Matching','song')}</div>
   <section class="fbox"><div class="ftabs"><button class="${F.reviewKind==='artist'?'active':''}" data-flow="artists">Artist Review</button><button class="${F.reviewKind==='song'?'active':''}" data-flow="songs">Song Review</button></div>
   <div class="fnav">${btn('← Previous','previous')}<strong>${entries.length?pos+1:0} of ${entries.length}</strong>${btn('Next →','next','primary',sel===null||F.working||!x||!ready||!nextReady||automatic||groups.length===0)}</div>
   ${ready&&!nextReady?loadingIndicator(forthcoming,'Preparing next review'):''}
   ${x?`<div class="fgrid"><section class="fpane"><h3>YouTube Music Source</h3>${noSource?'<div class="fsourcewarning">YouTube Music source unresolvable — no artist, song, accessible link, or artwork.</div>':`<div class="fprofile">${reviewPicture(x,'',sourceArtist)}<div><strong>${E(sourceArtist)}</strong><div>${E(sourceSong)}</div><div class="muted">${E(source.Album||source.album||x.album||'')}</div>${/^https:\/\/(www\.|music\.)?youtube\.com\/watch\?v=[\w-]{11}$/.test(x?.youtubeUrl||'')?`<a href="${E(x.youtubeUrl)}" target="_blank" rel="noopener noreferrer">Open original song ↗</a>`:''}</div></div>${reviewAudio(x,'')}`}${unavailable&&unavailable!=='YouTube link available'?`<div class="fsourcewarning">${E(unavailable)}</div>`:''}</section>
   <section class="fpane"><h3>Candidate Matches</h3>${!ready?loadingIndicator(x,'Loading candidate names, songs and previews'):`<div class="fcandidates">${automatic?'':groups.map(g=>{const selected=sel===g.i;const repeated=groups.filter(other=>normalizedName(other.display)===normalizedName(g.display)).length>1;return `<div class="fcandidate ${selected?'selected':''}" role="button" tabindex="0" data-option="${g.i}" aria-pressed="${selected}">${reviewPicture(x,g.c.id,g.display)}<strong>${E(g.display)}</strong>${repeated?`<small class="fduplicate">Separate MusicBrainz ID · ${E(g.c.id)}</small>`:''}${reviewAudio(x,g.c.id)}</div>`}).join('')}
   ${automatic||groups.length===0?'':`<div class="fnone ${sel===-1?'selected':''}" role="button" tabindex="0" data-option="-1" aria-pressed="${sel===-1}">${sel===-1?'✓ ':''}None of these</div>`}</div>${noCandidateActions(x,groups,automatic)}`}</section></div><section class="fpane" style="margin-top:12px"><h3>Match Evidence</h3><div class="fchips">${evid.map(e=>`<span class="fchip">${E(e)}</span>`).join('')}</div></section>`:'<div class="fcenter" style="padding:30px">No records currently need review in this category.</div>'}</section>
   <div class="fend">${btn('Continue to Migration Preview →','open-preview','primary')}</div>`;
 }
 function manualFallback(){const x=current(),saved=F.editingEntry?.key===x?.key?F.editingEntry:null;return `<h1>Manual Metadata</h1><section class="fbox"><h2>Song or Artist Not Found</h2><div class="fshort">Complete the missing source information and save to continue to the next review. Artist and Song are required; Album and YouTube link can be left empty if unavailable. This preserves metadata only; nothing is added to Lidarr or downloaded.</div>
 <div class="ffields"><label>Artist<input id="fallback-artist" value="${E(saved?.artist||F.media[mediaKey(x,'')]?.Name||(x?.artist&&x.artist!=='-'?x.artist:''))}" placeholder="Artist name" maxlength="220"/></label><label>Song<input id="fallback-song" value="${E(saved?.song||F.media[mediaKey(x,'')]?.SourceTitle||(x?.song&&x.song!=='-'?x.song:''))}" placeholder="Song title" maxlength="260"/></label><label>Album (optional)<input id="fallback-album" value="${E(saved?.album||(x?.album&&x.album!=='-'?x.album:''))}" maxlength="260" /></label><label>YouTube link (optional)<input id="fallback-url" value="${E(saved?.youtubeUrl||x?.youtubeUrl||'')}" maxlength="240"/></label></div>
 <div class="fend" style="gap:8px">${btn('← Back to Review','open-manual')}${btn('Save Manual Metadata','save-manual','primary',F.working)}</div></section>`}
 function categorized(name){
   if(name==='manual')return F.manualEntries;
   if(name==='add')return F.stageItems;
   const songs=(state.inventoryItems||[]).filter(x=>x.type==='song');
   if(name==='existing')return songs.filter(x=>String(x.category||'').startsWith('Already in Library')||
     (x.metadataStatus==='lidarr-track-metadata-present'&&!!x.trackId));
   if(name==='unmatched')return songs.filter(x=>String(x.category||'').startsWith('Manual Review'));
   return [];
 }
 function inventorySongRow(x){
   const candidate=x.artistReviewCandidateId||x.songReviewCandidateId;
   const review= candidate?'Selected review candidate saved; further recording/album verification required':x.reason||'';
   const metadata=x.metadataStatus==='lidarr-track-metadata-present';
   const stateLabel=metadata&&!String(x.category||'').startsWith('Already in Library')
     ?'Present in Lidarr metadata · audio not confirmed':String(x.category||'').startsWith('Already in Library')?'Lidarr file record present':'';
   return `<div class="frow passive">${art(x.artist||'A')}<div style="min-width:0;flex:1"><strong>${E(x.title||'Untitled')}</strong><small>${E(x.artist||'Unknown artist')}${x.album?' · '+E(x.album):''}</small>${stateLabel?`<small>${E(stateLabel)}</small>`:''}${review?`<small>${E(review)}</small>`:''}</div></div>`;
 }
 function albumRow(item,interactive){let id=Number(item.albumId),selected=F.selected.has(id);return `<label class="frow ${interactive&&selected?'selected':''} ${!interactive?'passive':''}">${interactive?`<input type="checkbox" data-album="${id}" ${selected?'checked':''} aria-label="Select ${E(item.album)}"/>`:''}${art(item.artist||'A')}<div><strong>${E(item.album||item.song||'Unresolved title')}</strong><small>${E(item.artist||'')} · ${num(item.safeSongs??1)} ${Number(item.safeSongs||1)===1?'song':'songs'}</small></div></label>`}
 function manualRow(x){return `<div class="frow passive">${art(x.artist||'A')}<div style="flex:1;min-width:0"><strong>${E(x.song||'Untitled')}</strong><small>${E(x.artist||'Unknown artist')}${x.album?' · '+E(x.album):''}</small><small>${x.saveMode==='auto'?(['sole-exact-artist-name-unverified','identical-artist-names-unverified'].includes(x.reason)?'Auto-saved · Matching artist name (not verified)':'Auto-saved · No usable candidates'):'Manually saved'} · Not in Lidarr</small></div><button class="fbtn" type="button" data-edit-manual="${E(x.key)}">Review / Edit</button></div>`;}
 function savedManualSummary(){const entries=F.manualEntries||[];if(!entries.length)return '';
   return `<section class="fbox"><h2>Saved for Manual Import (${entries.length})</h2><div class="muted">These songs were saved from YouTube Music metadata, not imported or downloaded. They still require a supported manual acquisition/import path.</div>${entries.slice(0,60).map(manualRow).join('')}${entries.length>60?`<div class="muted">${entries.length-60} additional entries are available in Migration Preview.</div>`:''}</section>`;
 }
 function preview(){const list=categorized(F.tab);return `<h1>Migration Preview</h1><div class="fsummary"><div class="fnum"><b>${num(state.inventory?.toAdd||0)}</b><span>Proposed Songs</span></div><div class="fnum"><b>${num(state.inventory?.review||0)}</b><span>Still Unmatched</span></div><div class="fnum"><b>${num(state.inventory?.filesRead||0)}</b><span>Downloaded Files Found</span></div></div>
  <section class="fbox"><h2>Music Library Overview</h2><div class="ftabs four">${['add','existing','unmatched','manual'].map((k,i)=>`<button data-tab="${k}" class="${F.tab===k?'active':''}">${['To Be Added','Already in Lidarr','Unmatched','Saved for Manual Import'][i]}</button>`).join('')}</div>
  ${F.tab==='add'?`<div class="fall"><label><input id="flow918-selectall" type="checkbox" ${F.selected.size===F.stageItems.length&&F.stageItems.length?'checked':''}/>Select All</label><span class="muted">${F.selected.size} of ${F.stageItems.length} selected</span></div>`:''}
  ${F.tab==='manual'?`<div class="fshort" style="margin-bottom:12px"><strong>${F.manualEntries.filter(x=>x.saveMode==='auto').length} automatically saved</strong> · ${F.manualEntries.filter(x=>x.saveMode!=='auto').length} manually saved. These entries are metadata only, not songs downloaded or imported to Lidarr.</div>`:''}
  ${F.tab==='existing'?'<div class="fshort">Takeout songs found in Lidarr catalog metadata or file records. Some may also need audio and appear under To Be Added.</div>':''}
  ${list.length?list.map(item=>F.tab==='manual'?manualRow(item):F.tab==='add'?albumRow(item,true):inventorySongRow(item)).join(''):'<div class="fcenter muted" style="padding:20px">No entries in this category.</div>'}
  <div class="fend">${btn('Next →','open-final','primary',!F.selected.size)}</div></section>
  <div class="fend">${btn('← Manual Review','open-manual')}</div>`;}
 function final(){const selected=F.stageItems.filter(x=>F.selected.has(x.albumId));const songCount=selected.reduce((n,x)=>n+(x.safeSongs||0),0);return `<h1>Finalize Lidarr Import</h1><section class="fbox"><h2>Albums to Import</h2><div class="fsummary two"><div class="fnum"><b>${selected.length}</b><span>Albums</span></div><div class="fnum"><b>${num(songCount)}</b><span>Songs</span></div></div>${selected.map(x=>albumRow(x,false)).join('')}<div class="fshort"><strong>This version prepares a local import plan only.</strong> Confirming below stages the selected albums in this application. It does not add or search for music in Lidarr, and does not download audio. A later, separately approved Lidarr-write version will be required for those actions.</div><div class="fend">${btn('← Back','open-preview')}${btn('Confirm Local Import Plan','confirm','primary',!selected.length||F.working)}</div></section>${savedManualSummary()}`}
 function progress(){const total=F.job.length,pct=total?Math.round(F.completed/total*100):0;const active=F.job[F.completed];return `<h1>Import Plan Progress</h1><section class="fbox"><div class="fnav"><h2>${F.failed?'An item needs attention':F.completed===total?'Complete':'Preparing your selected albums'}</h2><span class="fprogress">${pct}%</span></div><div class="fbar" role="progressbar" aria-label="Local import-plan progress" aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100"><i style="width:${pct}%"></i></div><div class="muted">${F.completed} of ${total} albums prepared</div>${active?`<section class="fpane" style="margin-top:13px"><h3>Currently Processing</h3><div class="fprofile">${art(active.artist)}<div><strong>${E(active.album)}</strong><div class="muted">${E(active.artist)}</div><div>${num(active.safeSongs)} source songs</div></div></div></section>`:''}</section><section class="fbox"><h2>Album Queue</h2>${F.job.map((a,i)=>`<div class="frow passive">${art(a.artist)}<div><strong>${E(a.album)}</strong><small>${num(a.safeSongs)} source songs</small></div><div style="margin-left:auto" class="muted">${i<F.completed?'✓ Prepared':i===F.completed&&!F.failed?'● Processing':i===F.completed?'Failed':'Pending'}</div></div>`).join('')}</section>${F.interrupted&&!F.failed?`<div class="fshort">This page was refreshed during local staging. Completed items have been checked against the server; no operation was repeated automatically.</div>${btn('Resume Preparation','resume-plan','primary')}`:''}${F.failed?`${warning()}${btn('Return to Migration Preview','open-preview')}`:''}`}
 function confetti(){const cols=['#2aba60','#ffbd48','#ef7aaa','#7a9bee','#b48be8','#ff795f'];return `<div class="fconfetti" aria-hidden="true">${Array.from({length:62},(_,i)=>`<i style="--x:${(i*41+5)%100}%;--color:${cols[i%cols.length]};--d:${3+(i%8)*.36}s;--delay:-${(i%15)*.23}s;--drift:${(i*17)%100-50}px"></i>`).join('')}</div>`}
 function githubMarkup(){
  if(!F.gh?.configured)return `<div class="fbox fcenter"><h2>Enjoyed using this tool?</h2><div class="muted">In-app GitHub starring requires the developer to configure a GitHub App client ID.</div></div>`;
  if(F.gh.starred)return `<div class="fdone">★ Thank you! GitHub confirmed your star.</div>`;
  return `<div class="fbox fcenter"><h2>Enjoyed using this tool?</h2><p>Give the project a star on GitHub to help others find it.</p>
   ${F.gh.authorized?btn('★ Star on GitHub','github-star','primary',F.working):F.gh.userCode?`<div class="fnum"><b style="letter-spacing:2px">${E(F.gh.userCode)}</b><span>GitHub authorization code</span></div><p>${btn('Open GitHub Sign-In','github-open')}${btn('Copy Code','github-copy')}</p><p class="muted">Sign in to GitHub, enter this code, and approve the app. The star itself is a separate click.</p>`:btn('Connect GitHub to Star','github-start','primary',F.working)}
   ${F.gh.error?`<div class="ferror">${E(F.gh.error)}</div>`:''}</div>`;
 }
 function completed(){const songCount=F.job.reduce((n,x)=>n+x.safeSongs,0);return `<div class="fbox fcelebrate fcenter">${confetti()}<div class="fcontent"><div class="fcheck">✓</div><h1>Congratulations!</h1><p class="fstatus">Your local import plan is complete.</p><div class="fbar"><i style="width:100%"></i></div><div class="fstatus">100% Complete</div><div class="fsummary two" style="max-width:370px;margin:15px auto"><div class="fnum"><b>${F.job.length}</b><span>Albums</span></div><div class="fnum"><b>${num(songCount)}</b><span>Songs</span></div></div><div class="fbuttons">${btn('Exit Application','exit')}${btn('Learn More About the Developer','about','primary')}</div></div></div>${savedManualSummary()}${githubMarkup()}`;}
 function about(){return `<h1>About the Developer</h1><section class="fbox fabout"><div class="fprofile">${art('Gost1o1')}<div><h2>Quinton Burgess</h2><strong>aka Gost1o1</strong></div></div><div class="fsep"></div><h2>Why I Created This Project</h2><p>I created YouTube Music Takeout to Lidarr to help you <strong>de-Google your life</strong> and easily import your YouTube Music collection into Lidarr, so you can manage your music without paying for another subscription service.</p><h2>Find Me Online</h2><div class="fbuttons"><a class="fbtn" href="https://www.Gost1o1.pro" target="_blank" rel="noopener noreferrer">Website ↗</a><a class="fbtn" href="https://m.twitch.tv/gost1o1/" target="_blank" rel="noopener noreferrer">Twitch ↗</a><a class="fbtn" href="https://www.youtube.com/@Gost1o1" target="_blank" rel="noopener noreferrer">YouTube ↗</a></div><h2>Questions or Bugs?</h2><p>If you encounter an error, email <a class="flink" href="mailto:gost1o1@gost1o1.pro?subject=YouTube%20Music%20to%20Lidarr%20Support">gost1o1@gost1o1.pro</a>. Please include screenshots, videos, error messages or anything else that helps show what happened. I'll work to investigate and fix it.</p><p><strong>Have a wonderful rest of your day, and thanks for using this tool!</strong></p><p class="muted">Developed with assistance from OpenAI ChatGPT (GPT-6). This credit identifies the assistant used in development; specific responses may use different product configurations.</p>${githubMarkup()}<div class="fbuttons">${btn('← Back','complete')}${btn('Exit Application','exit','primary')}</div></section>`;}
 function view(){switch(F.page){case 'manual':return manual();case 'fallback':return manualFallback();case 'preview':return preview();case 'final':return final();case 'progress':return progress();case 'complete':return completed();case 'about':return about();case 'exit':return `<section class="fbox fcenter"><div class="fcheck">✓</div><h1>Thanks for using this tool!</h1><p>The application is closing.</p></section>`;default:return ''}}
 F.render=function(){if(!F.active)return;el.innerHTML=`<div id="flow918"><div class="build-version">YouTube Music to Lidarr · v0.9.1.15.3</div>${F.page==='manual'&&F.classifying?classificationPage():view()}${warning()}</div>`;if(F.page==='manual'&&!F.classifying)enrichCurrentReview();if(typeof rememberNavigation==='function')rememberNavigation();};
 F.captureNavigation=()=>({
   page:F.page,reviewKind:F.reviewKind,reviewIndex:{...F.reviewIndex},
   tab:F.tab,selected:[...F.selected],choice:{...F.choice},
   job:F.job.map(x=>x.albumId),completed:F.completed,failed:F.failed
 });
 F.restoreNavigation=async function(saved){
   const allowed=['manual','fallback','preview','final','progress','complete','about'];
   const page=allowed.includes(saved?.page)?saved.page:'manual';
   state.reviewQueue=await api('review/queue');
   F.manualEntries=Array.isArray(state.reviewQueue.manualItems)?state.reviewQueue.manualItems:[];
   F.reviewKind=saved.reviewKind==='song'?'song':'artist';
   F.reviewIndex={artist:Math.max(0,Math.floor(Number(saved.reviewIndex?.artist)||0)),
                  song:Math.max(0,Math.floor(Number(saved.reviewIndex?.song)||0))};
   for(const kind of ['artist','song']){
     const total=(state.reviewQueue.items||[]).filter(x=>x.kind===kind).length;
     F.reviewIndex[kind]=Math.min(F.reviewIndex[kind],Math.max(0,total-1));
   }
   F.tab=['add','existing','unmatched','manual'].includes(saved.tab)?saved.tab:'add';
   // Saved selections are temporary UI choices, not verified identities.
   F.choice={};
   if(saved.choice&&typeof saved.choice==='object'){
     for(const [k,v] of Object.entries(saved.choice).slice(0,100))
       if(typeof k==='string'&&k.length<160&&Number.isInteger(v)&&v>=-1&&v<12)F.choice[k]=v;
   }
   F.active=true;F.error='';F.working=false;F.interrupted=false;
   if(page!=='manual'){
     if(!state.inventory?.done){F.page='manual';F.render();return}
     await fetchStages();
     const valid=new Set(F.stageItems.map(x=>Number(x.albumId)));
     const ids=Array.isArray(saved.selected)?saved.selected.slice(0,10000):[];
     F.selected=new Set(ids.map(Number).filter(id=>Number.isInteger(id)&&valid.has(id)));
     if((page==='final'||page==='progress')&&!F.selected.size){F.page='preview';F.render();return}
     const jobIds=Array.isArray(saved.job)?saved.job.map(Number):[];
     const idsSet=new Set(jobIds);
     F.job=F.stageItems.filter(x=>idsSet.has(Number(x.albumId)));
     // Server evidence wins: only staged items can be counted as completed.
     const stagedById=new Set(F.stageItems.filter(x=>x.staged).map(x=>Number(x.albumId)));
     F.completed=0;
     while(F.completed<F.job.length&&stagedById.has(Number(F.job[F.completed].albumId)))F.completed++;
     F.failed=!!saved.failed;
     if(['complete','about'].includes(page)){
       if(!F.job.length||F.completed!==F.job.length||F.failed){
         F.page='preview';F.render();return;
       }
     }
     if(page==='progress'){
       if(!F.job.length){F.page='preview';F.render();return}
       if(F.completed===F.job.length&&!F.failed){F.page='complete';F.render();openGithubStatus();return}
       F.interrupted=true;
     }
   }
   if(page==='manual')F.classifying=true;
   F.page=page;
   F.render();
   if(page==='manual')void classifyBeforeReview();
   if(['complete','about'].includes(page))openGithubStatus();
 };
 F.openPreview=()=>F.open('preview');
 F.open=async function(page='manual'){
  F.error='';F.working=false;
  try{
    const latest=await api('review/queue');
    if(Array.isArray(latest?.items))state.reviewQueue=latest;
    F.manualEntries=Array.isArray(state.reviewQueue?.manualItems)?state.reviewQueue.manualItems:[];
    F.active=true;F.page=page;
    if(page==='manual')F.classifying=true;
    if(page==='preview')await fetchStages();
    F.render();
    if(page==='manual')void classifyBeforeReview();
  }catch(e){F.error=e.message;F.active=true;F.page='manual';F.render()}
 };
 async function fetchStages(){
   if(!state.inventory?.done)throw Error('Please finish your Lidarr track and file inventory comparison first.');
   const [stages,inventory]=await Promise.all([api('lidarr/inventory/staging-queue'),api('lidarr/inventory/items')]);
   const rows=inventory.items||[];
   const songs=rows.filter(x=>x.type==='song');
   const expected=Number(state.inventory.totalSongs);
   if(!Array.isArray(rows)||(Number.isInteger(expected)&&expected>0&&songs.length!==expected))
     throw Error(`Incomplete Lidarr comparison: expected ${state.inventory.totalSongs} source songs, received ${songs.length}. Migration Preview was not opened.`);
   state.inventoryItems=rows;
   state.inventoryItemsLoaded=true;
   F.stageItems=stages.items||[];
   F.selected=new Set(F.stageItems.filter(x=>x.staged).map(x=>x.albumId));
 }
 function openGithubStatus(){api('github/status').then(x=>{F.gh={...x,...(F.gh?.userCode?{userCode:F.gh.userCode}:{} )};F.render()}).catch(e=>{F.error=e.message;F.render()})}
 async function preparePlan(){F.interrupted=false;F.job=F.stageItems.filter(x=>F.selected.has(x.albumId));F.completed=0;F.failed=false;F.page='progress';F.render();
  for(const album of F.job){
   try{await api('lidarr/inventory/stage-albums',{albumIds:[album.albumId],stage:true});F.completed++;F.render()}
   catch(e){F.failed=true;F.error=e.message;F.render();return;}
  }
  F.page='complete';F.render();openGithubStatus();
 }
 async function resumePlan(){
   if(!F.interrupted||F.working)return;
   F.working=true;F.error='';F.render();
   try{
     const status=await api('lidarr/inventory/staging-queue');
     const staged=new Set((status.items||[]).filter(x=>x.staged).map(x=>Number(x.albumId)));
     for(const album of F.job.slice(F.completed)){
       if(!staged.has(Number(album.albumId))){
         await api('lidarr/inventory/stage-albums',{albumIds:[album.albumId],stage:true});
         staged.add(Number(album.albumId));
       }
       F.completed++;
       F.render();
     }
     F.interrupted=false;F.page='complete';F.render();openGithubStatus();
   }catch(e){F.failed=true;F.error=e.message;F.render()}
   finally{F.working=false}
 }
 async function githubStart(){F.working=true;F.render();try{const r=await api('github/authorize/start');F.gh={...F.gh,userCode:r.userCode,verificationUri:r.verificationUri,authorized:false};F.working=false;F.render();schedulePoll(r.pollAfterSeconds||5)}catch(e){F.working=false;F.gh={...F.gh,error:e.message};F.render()}}
 function schedulePoll(seconds){clearTimeout(F.ghPoll);F.ghPoll=setTimeout(async()=>{
  if(!F.active||!['complete','about'].includes(F.page)||!F.gh?.userCode)return;
  try{const r=await api('github/authorize/poll');if(r.status==='authorized'){F.gh={...F.gh,authorized:true,userCode:null};F.render()}
  else if(r.status==='pending')schedulePoll(r.pollAfterSeconds||5);
  else{F.gh={...F.gh,userCode:null,error:r.error||'GitHub authorization expired or was denied.'};F.render()}
  }catch(e){F.gh={...F.gh,error:e.message};F.render()}
 },Math.max(3,seconds)*1000)}
 async function githubStar(){F.working=true;F.render();try{const r=await api('github/star');F.gh={...F.gh,starred:!!r.starred};F.render()}catch(e){F.gh={...F.gh,error:e.message};F.render()}finally{F.working=false}}
 async function saveAndNext(){stopReviewAudio();const x=current(),sel=selectedChoice(x);if(!x||sel===null||!mediaLoaded(x)||visibleCandidates(x).length===0)return;
   const nextRecord=records()[F.reviewIndex[F.reviewKind]+1];
   if(nextRecord&&!mediaLoaded(nextRecord))return;F.working=true;F.render();try{
  const c=candidates(x),none=sel===-1;
  await api('review/decision',{key:x.key,disposition:none?'needs-research':'candidate-for-planning',candidateId:none?'':c[sel].id,note:''});
  x.disposition=none?'needs-research':'candidate-for-planning';x.selected=none?'':c[sel].id;
  delete F.choice[x.key];F.reviewIndex[F.reviewKind]=Math.min(F.reviewIndex[F.reviewKind]+1,records().length-1);
  F.error='';
 }catch(e){F.error=e.message}finally{F.working=false;F.render()}}
 document.addEventListener('click',async(e)=>{
  if(!F.active)return;
  const sourcePlay=e.target.closest('#flow918 [data-source-video]');
  if(sourcePlay){
    e.stopPropagation();
    if(F.youtubePlayer){stopReviewVideo();return}
    const x=current(),m=F.media[mediaKey(x,'')]||{};
    if(!x||!/^https:\/\/(?:www\.|music\.)?youtube\.com\/watch\?v=[A-Za-z0-9_-]{11}$/.test(x.youtubeUrl||'')||
       String(m.Availability||'').toLowerCase().includes('unavailable'))return;
    stopReviewAudio();
    const videoId=new URL(x.youtubeUrl).searchParams.get('v');
    const frame=document.createElement('iframe');
    frame.width='100%';frame.height='188';frame.title='Original YouTube music video (first ten seconds)';
    frame.referrerPolicy='no-referrer';frame.allow='autoplay; encrypted-media; picture-in-picture';
    frame.allowFullscreen=false;
    frame.src='https://www.youtube-nocookie.com/embed/'+encodeURIComponent(videoId)+'?autoplay=1&start=0&end=10&rel=0';
    const box=sourcePlay.parentElement.querySelector('.fvideo-host');
    if(box){box.appendChild(frame);F.youtubePlayer=frame;sourcePlay.textContent='■ Stop & reset'}
    return;
  }
  const preview=e.target.closest('#flow918 [data-preview]');if(preview){e.stopPropagation();toggleReviewAudio(preview.dataset.preview);return}
  const card=e.target.closest('#flow918 [data-option]');if(card){const x=current();if(x&&mediaLoaded(x)){stopReviewAudio();F.choice[x.key]=Number(card.dataset.option);F.render()}return}
  const edit=e.target.closest('#flow918 [data-edit-manual]');
  if(edit){
    const saved=F.manualEntries.find(v=>v.key===edit.dataset.editManual);
    if(!saved)return;
    try{
      stopReviewAudio();
      await api('review/manual-metadata/restore',{key:saved.key});
      F.editingEntry={...saved};
      await refreshManualQueue();
      F.reviewKind=saved.kind==='song'?'song':'artist';
      F.reviewIndex[F.reviewKind]=Math.max(0,records().findIndex(v=>v.key===saved.key));
      F.page='fallback';F.render();
    }catch(err){F.error=err.message;F.render()}
    return;
  }
  const b=e.target.closest('#flow918 [data-flow],#flow918 [data-tab]');if(!b)return;
  if(b.dataset.tab){F.tab=b.dataset.tab;F.render();return}
  F.error='';const a=b.dataset.flow;
  switch(a){
   case 'artists':stopReviewAudio();F.reviewKind='artist';F.render();break;case 'songs':stopReviewAudio();F.reviewKind='song';F.render();break;
   case 'previous':stopReviewAudio();F.reviewIndex[F.reviewKind]=Math.max(0,F.reviewIndex[F.reviewKind]-1);F.render();break;
   case 'next':await saveAndNext();break;
   case 'open-preview':stopReviewAudio();try{await fetchStages();F.page='preview';F.render()}catch(err){F.error=err.message;F.render()}break;
   case 'retry-candidate-details':{
     const x=current();if(!x)break;
     for(const k of Object.keys(F.media))if(k.startsWith(x.key+':'))delete F.media[k];
     F.bundlePromises.delete(x.key);F.autoAttempted.delete(x.key);F.autoFailed.delete(x.key);
     ensureBundle(x);F.render();break;
   }
   case 'manual-fallback':stopReviewAudio();F.page='fallback';F.render();break;
   case 'save-manual':{
     const x=current();if(!x)break;
     const v=id=>document.getElementById(id)?.value||'';
     const payload={key:x.key,artist:v('fallback-artist'),song:v('fallback-song'),album:v('fallback-album'),youtubeUrl:v('fallback-url')};
     F.working=true;
     try{
       await api('review/manual-metadata',payload);
       for(const k of Object.keys(F.media))if(k.startsWith(x.key+':'))delete F.media[k];
       F.bundlePromises.delete(x.key);F.editingEntry=null;F.error='';F.page='manual';
       // The server now excludes handled items, so the next item shifts into
       // this same index. No disabled Next-button dead end after saving.
       await refreshManualQueue();
     }catch(err){F.error=err.message;F.render()}finally{F.working=false}break;
   }
   case 'open-manual':stopReviewAudio();F.page='manual';F.render();break;
   case 'open-final':if(F.selected.size){F.page='final';F.render()}break;
   case 'confirm':await preparePlan();break;
   case 'resume-plan':await resumePlan();break;
   case 'about':F.page='about';F.render();break;
   case 'complete':F.page='complete';F.render();break;
   case 'github-start':await githubStart();break;
   case 'github-open':window.open('https://github.com/login/device','_blank','noopener,noreferrer');break;
   case 'github-copy':if(navigator.clipboard&&F.gh?.userCode)await navigator.clipboard.writeText(F.gh.userCode);break;
   case 'github-star':await githubStar();break;
   case 'exit':F.page='exit';F.render();try{await api('app/exit')}catch(_){}break;
  }
 });
 document.addEventListener('change',e=>{
  if(!F.active||F.page!=='preview')return;
  if(e.target.id==='flow918-selectall'){
   if(e.target.checked)F.selected=new Set(F.stageItems.map(x=>x.albumId));else F.selected.clear();F.render();return;
  }
  if(e.target.matches('#flow918 [data-album]')){const n=Number(e.target.dataset.album);if(e.target.checked)F.selected.add(n);else F.selected.delete(n);F.render()}
 });
 document.addEventListener('keydown',e=>{if(!F.active)return;const node=e.target.closest('#flow918 [data-option]');if(node&&(e.key==='Enter'||e.key===' ')){e.preventDefault();node.click()}});
 window.Flow918=F;
})();
