#Requires -Version 5.1
# v0.7.1 fresh-run multi-pass verification. READ-ONLY. No Lidarr writes or downloads.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$TracksCsv,[Parameter(Mandatory)][string]$ArtistsJson,[Parameter(Mandatory)][string]$CacheRoot,[Parameter(Mandatory)][string]$StatusFile)
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
[IO.Directory]::CreateDirectory($CacheRoot)|Out-Null
$rows=@(Import-Csv -LiteralPath $TracksCsv -Encoding UTF8)
$lidarr=@(Get-Content -LiteralPath $ArtistsJson -Raw -Encoding UTF8|ConvertFrom-Json)
# Every run starts fresh. No stored match decisions or MusicBrainz responses are read.
$sourceHash=(Get-FileHash -LiteralPath $TracksCsv -Algorithm SHA256).Hash
$log=Join-Path $CacheRoot 'worker-errors.log'
$lookupFailures=0;$completed=0;$script:liveRequests=0;$script:lastCall=[datetime]::MinValue
function Normalize([string]$s){
 if(-not $s){return ''}
 $s=$s.Normalize([Text.NormalizationForm]::FormD).ToLowerInvariant()
 $s=[regex]::Replace($s,'\p{Mn}','')
 $s=[regex]::Replace($s,'(?i)\s*(?:\(|\[)(feat\.?|ft\.?|featuring)\s+.*?[\)\]]','')
 return [regex]::Replace($s,'[^\p{L}\p{N}]','')
}
function Fetch-MB([string]$type,[string]$q,[int]$limit=40){
 # Deliberately query MusicBrainz every time; no response cache.
 $uri='https://musicbrainz.org/ws/2/'+$type+'/?query='+[uri]::EscapeDataString($q)+'&fmt=json&limit='+$limit
 for($attempt=0;$attempt -lt 4;$attempt++){
  try{
   $delay=1250-([datetime]::UtcNow-$script:lastCall).TotalMilliseconds
   if($delay -gt 0){Start-Sleep -Milliseconds ([int]$delay)}
   $script:lastCall=[datetime]::UtcNow
   $script:liveRequests++
   $r=Invoke-RestMethod -Uri $uri -TimeoutSec 30 -Headers @{'User-Agent'='YouTubeMusicTakeoutToLidarr/0.7.0 (personal desktop metadata verifier)';'Accept'='application/json'} -ErrorAction Stop
   return $r
  }catch{
   if($attempt -eq 3){$script:lookupFailures++;Add-Content -LiteralPath $log -Value ((Get-Date).ToString('s')+' MusicBrainz '+$type+': '+$_.Exception.Message);return $null}
   Start-Sleep -Seconds ([math]::Min(20,[math]::Pow(2,$attempt+1)))
  }
 }
}
function Credits($rec){
 $out=@()
 foreach($entry in @($rec.'artist-credit')){if($entry.artist -and $entry.artist.id){$out+=@{id=[string]$entry.artist.id;name=[string]$entry.artist.name;creditedAs=[string]$entry.name}}}
 return @($out)
}
function Candidate-Recording($rec,[string]$artist,[string]$title,[string]$album){
 if((Normalize ([string]$rec.title)) -ne (Normalize $title)){return $null}
 $credits=@(Credits $rec);if(-not $credits.Count){return $null}
 $nameNorm=Normalize $artist
 $direct=@($credits|Where-Object {(Normalize $_.name) -eq $nameNorm -or (Normalize $_.creditedAs) -eq $nameNorm})
 $artistDisplay=@($credits|ForEach-Object {$_.name}) -join ' & '
 $collab=((Normalize $artistDisplay) -eq $nameNorm)
 $creditNames=@($credits|ForEach-Object {$_.name})
 $creditIds=@($credits|ForEach-Object {$_.id})
 $albumMatch=$false
 foreach($rel in @($rec.releases)){if((Normalize ([string]$rel.title)) -eq (Normalize $album) -and $album){$albumMatch=$true;break}}
 # Title is required; name or individual credit supports an artist hypothesis.
 if(-not $direct.Count -and -not $collab){return $null}
 $evidence=if($direct.Count){'recording_credit'}else{'collaboration_credit'}
 $weight=if($direct.Count){4}else{3}
 if($albumMatch){$weight+=2}
 return @{recordingId=[string]$rec.id;recordingTitle=[string]$rec.title;credits=$credits;creditIds=$creditIds;creditNames=$creditNames;albumMatch=$albumMatch;weight=$weight;evidence=$evidence;score=[int]$rec.score}
}
$existingIds=@{};$existingNames=@{}
foreach($a in $lidarr){if($a.foreignArtistId){$existingIds[[string]$a.foreignArtistId]=$true};$n=Normalize ([string]$a.artistName);if($n){$existingNames[$n]=$true}}
$uniqueArtists=@{};$group=@{};$songResults=@{};$artistResults=@{};$artistEvidence=@{}
for($i=0;$i -lt $rows.Count;$i++){
 $a=[string]$rows[$i].Artist;$key=Normalize $a
 if(-not $key){$key='missingartist'+$i}
 if(-not $uniqueArtists.ContainsKey($key)){$uniqueArtists[$key]=$a;$group[$key]=New-Object 'System.Collections.Generic.List[int]'}
 $group[$key].Add($i)
}
# No prior results or checkpoints are loaded, regardless of their existence.
function Save-Status([bool]$done=$false){
 $ar=@($artistResults.Values);$sr=@($songResults.Values)
 $aExisting=@($ar|Where-Object {$_.status -eq 'existing'}).Count
 $aMissing=@($ar|Where-Object {$_.status -eq 'verified_missing'}).Count
 $aReview=@($ar|Where-Object {$_.status -eq 'review'}).Count
 $sVerified=@($sr|Where-Object {$_.status -eq 'recording_verified'}).Count
 $sReview=@($sr|Where-Object {$_.status -eq 'review'}).Count
 $state=@{algorithmVersion='0.7.1';sourceFingerprint=$sourceHash;done=$done;processed=$script:completed;total=$rows.Count;artistsTotal=$uniqueArtists.Count;artistProcessed=$ar.Count;artistExisting=$aExisting;artistVerifiedMissing=$aMissing;artistReview=$aReview;songRecordingVerified=$sVerified;songReview=$sReview;lookupFailures=$script:lookupFailures;liveRequests=$script:liveRequests;cachedResponses=0;artistResults=$ar;songResults=$sr;updated=(Get-Date).ToString('s');note='Multi-pass title, album and credited-artist evidence. Confirmed missing artist IDs are candidates for later approval; no writes performed. Songs still require exact release and file verification.'}
 $json=ConvertTo-Json -InputObject $state -Depth 14 -Compress
 [IO.File]::WriteAllText($StatusFile,$json,[Text.Encoding]::UTF8)
}
try{
 Save-Status $false
 # PASS 1: exact recording + artist; PASS 2: title-only search for alternate credited names.
 for($i=0;$i -lt $rows.Count;$i++){
  $index=[string]($i+1)
  # Fresh run: every source record is checked.
  try {
  $artist=[string]$rows[$i].Artist;$song=[string]$rows[$i].Song;$album=[string]$rows[$i].Album
  if(-not $artist -or -not $song){$songResults[$index]=@{rowIndex=$i+1;artist=$artist;song=$song;status='review';reason='Source artist or title missing';candidates=@()};$completed++;continue}
  $safeSong=$song.Replace('"',' ').Trim();$safeArtist=$artist.Replace('"',' ').Trim()
  $query='recording:"'+$safeSong+'" AND artist:"'+$safeArtist+'"'
  $response=Fetch-MB 'recording' $query
  $checked=@();$fail=$false
  if($null -eq $response){$fail=$true}else{
   foreach($rec in @($response.recordings)){$c=Candidate-Recording $rec $artist $song $album;if($c){$checked+=,$c}}
  }
  if(-not $checked.Count -and -not $fail){
   $response2=Fetch-MB 'recording' ('recording:"'+$safeSong+'"') 75
   if($null -eq $response2){$fail=$true}else{
    foreach($rec in @($response2.recordings)){$c=Candidate-Recording $rec $artist $song $album;if($c){$checked+=,$c}}
   }
  }
  if($checked.Count){
   $best=@($checked|Sort-Object -Property @{Expression={$_.weight};Descending=$true},@{Expression={$_.score};Descending=$true})
   $uniqueRecordings=@($best|ForEach-Object {$_.recordingId}|Select-Object -Unique)
   $songResults[$index]=@{rowIndex=$i+1;artist=$artist;song=$song;album=$album;status=if($uniqueRecordings.Count -eq 1){'recording_verified'}else{'artist_evidence_only'};recordingId=if($uniqueRecordings.Count -eq 1){$uniqueRecordings[0]}else{''};reason=if($uniqueRecordings.Count -eq 1){'Exact title and corroborated credited artist'}else{'Artist credits supported; multiple recording versions require later release check'};candidates=@($best|Select-Object -First 8)}
  }else{
   $songResults[$index]=@{rowIndex=$i+1;artist=$artist;song=$song;album=$album;status=if($fail){'unchecked'}else{'review'};reason=if($fail){'Catalog lookup incomplete; retry without manual review'}else{'No sufficiently corroborated recording yet'};candidates=@()}
  }
  }catch{
   Add-Content -LiteralPath $log -Value ((Get-Date).ToString('s')+' Record '+($i+1)+': '+$_.Exception.Message)
   $songResults[[string]($i+1)]=@{rowIndex=$i+1;artist=[string]$rows[$i].Artist;song=[string]$rows[$i].Song;status='unchecked';reason='Internal check error, safe to retry';candidates=@()}
  }
  $completed++;if($completed%5 -eq 0){Save-Status}
 }
 # PASS 3: Aggregate evidence across every song by an artist before deciding its ID.
 foreach($key in @($uniqueArtists.Keys)){
  $votes=@{};$recordingVotes=@{};$uncertain=$false
  foreach($i in $group[$key]){
   $sr=$songResults[[string]($i+1)]
   if($sr.status -eq 'unchecked'){$uncertain=$true;continue}
   foreach($candidate in @($sr.candidates)){
    foreach($credit in @($candidate.credits)){
     if((Normalize ([string]$credit.name)) -ne $key -and (Normalize ([string]$credit.creditedAs)) -ne $key){continue}
     $id=[string]$credit.id;if(-not $id){continue}
     if(-not $votes.ContainsKey($id)){$votes[$id]=0;$recordingVotes[$id]=@{}}
     # A recording contributes once to each ID, preventing duplicate catalog versions from inflating support.
     $recordKey=[string]$candidate.recordingId
     if(-not $recordingVotes[$id].ContainsKey($recordKey)){$votes[$id]+=[int]$candidate.weight;$recordingVotes[$id][$recordKey]=$true}
    }
   }
  }
  $ranked=@($votes.GetEnumerator()|Sort-Object Value -Descending)
  $top=if($ranked.Count){$ranked[0]}else{$null}
  $runner=if($ranked.Count -gt 1){$ranked[1]}else{$null}
  $id=if($top){[string]$top.Key}else{''}
  $nEvidence=if($id){$recordingVotes[$id].Count}else{0}
  $gap=if($top){[int]$top.Value - $(if($runner){[int]$runner.Value}else{0})}else{0}
  # One unique credited recording with unambiguous ID can establish identity; conflicting IDs need stronger consensus.
  $verified=($top -and (($ranked.Count -eq 1 -and $nEvidence -ge 2) -or ($ranked.Count -eq 1 -and $nEvidence -eq 1 -and [int]$top.Value -ge 6) -or ($nEvidence -ge 2 -and $gap -ge 4 -and [int]$top.Value -ge 8)))
  $state=if($verified){if($existingIds.ContainsKey($id)){'existing'}else{'verified_missing'}}elseif($uncertain){'unchecked'}else{'review'}
  $artistResults[$key]=@{artist=$uniqueArtists[$key];status=$state;mbid=if($verified){$id}else{''};evidenceRecordingCount=$nEvidence;supportScore=if($top){[int]$top.Value}else{0};runnerUpScore=if($runner){[int]$runner.Value}else{0};reason=if($verified){'Corroborated credited recordings and no comparably supported competing identity'}elseif($uncertain){'One or more required lookups incomplete'}else{'No sufficiently corroborated artist ID or competing IDs remain'};candidates=@($ranked|Select-Object -First 5|ForEach-Object {@{id=$_.Key;score=$_.Value;recordings=$recordingVotes[$_.Key].Count}})}
 }
 # PASS 4: MusicBrainz artist-name/alias discovery for unresolved cases.
 # Alias matches help the human reviewer but never independently authorize an artist.
 foreach($key in @($uniqueArtists.Keys)) {
  $a=$artistResults[$key]
  if($a.status -notin @('review')){continue}
  $name=[string]$uniqueArtists[$key]
  if(-not $name){continue}
  $res=Fetch-MB 'artist' ('artist:"'+$name.Replace('"',' ')+'"') 25
  if($null -eq $res){$a.status='unchecked';$a.reason='Artist-alias check could not complete; retry later';continue}
  $matches=@()
  foreach($entry in @($res.artists)){
   $ok=((Normalize ([string]$entry.name)) -eq $key)
   if(-not $ok){foreach($alias in @($entry.aliases)){if((Normalize ([string]$alias.name)) -eq $key){$ok=$true;break}}}
   if($ok){$matches+=@{id=[string]$entry.id;name=[string]$entry.name;score=[int]$entry.score;disambiguation=[string]$entry.disambiguation}}
  }
  if($matches.Count){$a.aliasCandidates=@($matches|Select-Object -First 8);$a.reason='Name/alias candidates discovered; source recordings did not confirm a unique ID'}
 }
 # PASS 5: Reuse artist-level identity to distinguish unknown recording version from unknown artist.
 foreach($key in @($uniqueArtists.Keys)){
  $a=$artistResults[$key]
  if($a.status -notin @('existing','verified_missing')){continue}
  foreach($i in $group[$key]){
   $r=$songResults[[string]($i+1)]
   if($r.status -eq 'review' -and $r.reason -eq 'No sufficiently corroborated recording yet'){
    $r.status='artist_verified_recording_unresolved';$r.reason='Artist ID resolved using other source songs; this recording still requires exact-version verification'
   }
  }
 }
 Save-Status $true
}catch{
 Add-Content -LiteralPath $log -Value ((Get-Date).ToString('s')+' Fatal: '+$_.Exception.ToString())
 try{Save-Status $false}catch{}
 exit 1
}
