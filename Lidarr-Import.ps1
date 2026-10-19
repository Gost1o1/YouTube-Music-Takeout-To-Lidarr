#Requires -Version 5.1
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$ArtistsCsv,
 [Parameter(Mandatory=$true)][string]$ReportFolder,
 [string]$LidarrUrl='', [string]$LidarrApiKey='',
 [switch]$PreviewOnly
)
$ErrorActionPreference='Stop'
function Normalize-Artist([string]$s) { if ([string]::IsNullOrWhiteSpace($s)) {return ''}; return [regex]::Replace($s.Trim().ToLowerInvariant(),'[^\p{L}\p{N}]','') }
function Get-LidarrKey {
 if($LidarrApiKey){return $LidarrApiKey}
 $secure=Read-Host 'Lidarr API key (input hidden; never saved to reports)' -AsSecureString
 $ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
 try{return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)}
}
function Save-Progress([string]$phase,[int]$done,[int]$total,[int]$added,[int]$exists,[int]$review,[int]$errors){
 $pct=if($total -gt 0){[math]::Round(100.0*$done/$total,1)}else{100}
 $safe=[System.Net.WebUtility]::HtmlEncode($phase)
 $page=@"
<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="refresh" content="3"><title>Lidarr import progress</title><style>body{font:18px system-ui;max-width:720px;margin:60px auto;color:#202838;padding:0 16px}progress{width:100%;height:32px}small{color:#566}</style></head><body><h1>Lidarr artist import</h1><p>$safe</p><progress max="100" value="$pct"></progress><p><strong>$done / $total ($pct%)</strong></p><p>Added: $added &nbsp; Already present: $exists &nbsp; Needs review: $review &nbsp; Errors: $errors</p><small>This page refreshes while PowerShell runs the workflow. It cannot execute local scripts itself. Keep the PowerShell window open.</small></body></html>
"@
 [IO.File]::WriteAllText($script:progressFile,$page,(New-Object Text.UTF8Encoding($false)))
}
if(-not (Test-Path -LiteralPath $ArtistsCsv)){throw "Artist inventory missing: $ArtistsCsv"}
New-Item -ItemType Directory -Path $ReportFolder -Force | Out-Null
$inputRows=@(Import-Csv -LiteralPath $ArtistsCsv | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Artist) })
$unique = @($inputRows | Group-Object { Normalize-Artist $_.Artist } | ForEach-Object { $_.Group[0] } | Sort-Object Artist)
if(-not $unique.Count){Write-Host 'No artist credits available in this archive.';return}
if(-not $LidarrUrl){$LidarrUrl=Read-Host 'Lidarr URL (e.g. http://localhost:8686)'}
$key=Get-LidarrKey
if(-not $LidarrUrl -or -not $key){throw 'Lidarr URL and API key are required.'}
$base=$LidarrUrl.TrimEnd('/')
if($base -notmatch '/api/v1$'){$base+='/api/v1'}
$headers=@{'X-Api-Key'=$key}
# Validate credentials and snapshot the current library before any write.
$raw=Invoke-RestMethod -Uri "$base/artist" -Headers $headers -TimeoutSec 75 -ErrorAction Stop
$existing=@($raw | Where-Object {$null -ne $_ -and $_.id})
$idIndex=@{};$nameIndex=@{}
foreach($artist in $existing){
 if($artist.foreignArtistId){$idIndex[[string]$artist.foreignArtistId]=$artist.artistName}
 $names=@([string]$artist.artistName)
 if($artist.aliases){$names+=@($artist.aliases | ForEach-Object {$_.name})}
 foreach($name in $names){$normalized=Normalize-Artist $name;if(-not $normalized){continue};if(-not $nameIndex.ContainsKey($normalized)){$nameIndex[$normalized]=@{}};$nameIndex[$normalized][[string]$artist.id]=[string]$artist.artistName}
}
$rootResponse=Invoke-RestMethod -Uri "$base/rootfolder" -Headers $headers -TimeoutSec 45
$roots=@($rootResponse | Where-Object {$_.path})
$qResponse=Invoke-RestMethod -Uri "$base/qualityprofile" -Headers $headers -TimeoutSec 45
$qualities=@($qResponse | Where-Object {$_.id})
$mResponse=Invoke-RestMethod -Uri "$base/metadataprofile" -Headers $headers -TimeoutSec 45
$metas=@($mResponse | Where-Object {$_.id})
Write-Host "`n===== LIDARR IMPORT PREVIEW ====="
Write-Host "Takeout artist credits: $($unique.Count)"
Write-Host "Existing Lidarr artists: $($existing.Count)"
$preExisting=0
foreach($row in $unique){$n=Normalize-Artist $row.Artist;if($nameIndex.ContainsKey($n)){$preExisting++}}
Write-Host "Names/aliases already seen (unverified identity): $preExisting"
Write-Host "Names requiring lookup or review: $($unique.Count-$preExisting)"
Write-Host 'IMPORTANT: Artist-name matching alone is not proof of a specific recording.'
Write-Host 'Collaborations, duplicate artist names, and uncertain results will be held for review.'
Write-Host 'No album searches, downloads, or media moves are performed by this module.'
if($PreviewOnly){Write-Host 'PreviewOnly enabled. No changes made.';return}
if(-not $roots.Count -or -not $qualities.Count -or -not $metas.Count){throw 'Lidarr is missing a root folder or required profiles.'}
Write-Host "`nChoose the Lidarr defaults for NEW artist entries."
for($i=0;$i -lt $roots.Count;$i++){Write-Host "Root [$($i+1)]: $($roots[$i].path)"}
$rootChoice=Read-Host 'Root folder number (Enter cancels)'
if($rootChoice -notmatch '^\d+$' -or [int]$rootChoice -lt 1 -or [int]$rootChoice -gt $roots.Count){Write-Host 'Cancelled.';return}
$root=$roots[[int]$rootChoice-1].path
foreach($q in $qualities){Write-Host "Quality [$($q.id)]: $($q.name)"}
$qchoice=Read-Host 'Quality profile ID (Enter cancels)'
if($qchoice -notmatch '^\d+$' -or -not @($qualities | Where-Object { [string]$_.id -eq $qchoice }).Count){Write-Host 'Cancelled.';return}
foreach($m in $metas){Write-Host "Metadata [$($m.id)]: $($m.name)"}
$mchoice=Read-Host 'Metadata profile ID (Enter cancels)'
if($mchoice -notmatch '^\d+$' -or -not @($metas | Where-Object { [string]$_.id -eq $mchoice }).Count){Write-Host 'Cancelled.';return}
$confirmation=Read-Host "Type IMPORT $($unique.Count) ARTISTS to begin the verified-ID/unique-exact-name candidate pass"
if($confirmation -cne "IMPORT $($unique.Count) ARTISTS"){Write-Host 'Cancelled. No imports made.';return}
$resultsPath=Join-Path $ReportFolder 'lidarr-artist-import-results.csv'
$progressFile=Join-Path $ReportFolder 'lidarr-import-progress.html'
$results=@()
if(Test-Path -LiteralPath $resultsPath){$results=@(Import-Csv -LiteralPath $resultsPath)}
$complete=@{}
foreach($r in $results){if($r.Artist -and $r.Status -ne 'API_ERROR'){$complete[(Normalize-Artist $r.Artist)]=$true}}
$added=0;$present=0;$review=0;$errors=0;$done=0
Save-Progress 'Starting artist reconciliation' 0 $unique.Count 0 0 0 0
Start-Process -FilePath $progressFile
foreach($row in $unique){
 $name=[string]$row.Artist; $normalized=Normalize-Artist $name
 if($complete.ContainsKey($normalized)){$done++;continue}
 $status='NEEDS_REVIEW';$resolved='';$mbid='';$detail=''
 try {
  if($name -match '\s(&|,|\bfeat\.?\b|\bft\.?\b|\bwith\b|\bx\b|/)\s' -or $name -match '[\\/]'){ $status='COLLABORATION_REVIEW' }
  elseif($nameIndex.ContainsKey($normalized)){
   if($nameIndex[$normalized].Count -eq 1){$status='ALREADY_PRESENT_BY_NAME';$resolved=@($nameIndex[$normalized].Values)[0]}
   else{$status='AMBIGUOUS_INSTALLED_NAME'}
  } else {
   $term=[uri]::EscapeDataString($name)
   $look=Invoke-RestMethod -Uri "$base/artist/lookup?term=$term" -Headers $headers -TimeoutSec 75 -ErrorAction Stop
   $choices=@($look | Where-Object {$_.foreignArtistId -and (Normalize-Artist $_.artistName) -eq $normalized} | Group-Object foreignArtistId | ForEach-Object {$_.Group[0]})
   if($choices.Count -eq 0){$status='NO_EXACT_IDENTITY'}
   elseif($choices.Count -gt 1){$status='MULTIPLE_EXACT_IDENTITIES'}
   else {
    $candidate=$choices[0];$mbid=[string]$candidate.foreignArtistId;$resolved=[string]$candidate.artistName
    if($idIndex.ContainsKey($mbid)){$status='ALREADY_PRESENT_BY_ID'}
    else {
     # Single exact-name result is eligible only after the user's bulk approval; not a recording-level match.
     $candidate | Add-Member NoteProperty qualityProfileId ([int]$qchoice) -Force
     $candidate | Add-Member NoteProperty metadataProfileId ([int]$mchoice) -Force
     $candidate | Add-Member NoteProperty rootFolderPath $root -Force
     $candidate | Add-Member NoteProperty monitored $true -Force
     $candidate | Add-Member NoteProperty addOptions @{monitor='all';searchForMissingAlbums=$false} -Force
     $payload=$candidate | ConvertTo-Json -Depth 30
     $created=Invoke-RestMethod -Method Post -Uri "$base/artist" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -TimeoutSec 75 -ErrorAction Stop
     $status='IMPORTED_NAME_ONLY_USER_APPROVED';$idIndex[$mbid]=$resolved
     if($created.id){$nameIndex[$normalized]=@{([string]$created.id)=$resolved}}
    }
   }
  }
 }catch{$status='API_ERROR';$detail=$_.Exception.Message}
 if($status -eq 'IMPORTED_NAME_ONLY_USER_APPROVED'){$added++}
 elseif($status -match '^ALREADY_'){$present++}
 elseif($status -eq 'API_ERROR'){$errors++}
 else{$review++}
 $results=@($results | Where-Object {(Normalize-Artist $_.Artist) -ne $normalized})
 $results+= [pscustomobject]@{Artist=$name;Status=$status;LidarrArtist=$resolved;MusicBrainzId=$mbid;Details=$detail}
 $results | Export-Csv -LiteralPath $resultsPath -NoTypeInformation -Encoding UTF8
 $done++
 Save-Progress "Last processed: $name ($status)" $done $unique.Count $added $present $review $errors
 if($done % 25 -eq 0){Write-Host "Progress $done/$($unique.Count): added $added, already present $present, review $review, errors $errors"}
 Start-Sleep -Milliseconds 400
}
$left=@($results | Where-Object {$_.Status -notmatch '^(IMPORTED_|ALREADY_)'})
$left | Export-Csv -LiteralPath (Join-Path $ReportFolder 'lidarr-artist-import-exceptions.csv') -NoTypeInformation -Encoding UTF8
Save-Progress 'Finished. Review the saved reports for unresolved and ambiguous names.' $unique.Count $unique.Count $added $present $review $errors
Write-Host "`n===== IMPORT SUMMARY ====="
Write-Host "Added this run: $added; already present: $present; review: $review; errors: $errors"
Write-Host "Results: $resultsPath"
Write-Host "Exceptions: $(Join-Path $ReportFolder 'lidarr-artist-import-exceptions.csv')"
Write-Host 'No downloads, album searches, or file moves were requested.'
