#Requires -Version 5.1
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$TracksCsv,
  [Parameter(Mandatory=$true)][string]$ReportFolder,
  [string]$LidarrUrl='', [string]$LidarrApiKey='',
  [switch]$PreviewOnly
)
$ErrorActionPreference='Stop'
function Norm([string]$s) { if(-not $s){return ''}; return [regex]::Replace($s.ToLowerInvariant().Trim(),'[^\p{L}\p{N}]','') }
$tracks=@(Import-Csv -LiteralPath $TracksCsv)
. (Join-Path $PSScriptRoot 'Lidarr-Connection.ps1')
$connection=Get-LidarrConnection -Url $LidarrUrl -ApiKey $LidarrApiKey
if(-not $connection){return}
$LidarrUrl=$connection.Url
$key=$connection.ApiKey
$base=$LidarrUrl.TrimEnd('/')
if($base -notmatch '/api/v1$'){$base+='/api/v1'}
$h=@{'X-Api-Key'=$key}
# Fail before any search actions if connection/authentication is invalid.
$artists=@(Invoke-RestMethod -Uri "$base/artist" -Headers $h -TimeoutSec 45 | Where-Object {$null -ne $_ -and $_.id})
$nameGroups=@{}
foreach($artist in $artists){
  $aliases=@([string]$artist.artistName)
  if($artist.aliases){$aliases+=@($artist.aliases | ForEach-Object {$_.name})}
  foreach($a in $aliases){$n=Norm $a; if(-not $n){continue}; if(-not $nameGroups.ContainsKey($n)){$nameGroups[$n]=@()}; if(@($nameGroups[$n] | Where-Object {$_.id -eq $artist.id}).Count -eq 0){$nameGroups[$n]+=$artist}}
}
$cache=@{}; $out=New-Object System.Collections.Generic.List[object]
foreach($t in $tracks){
  $name=Norm $t.Artist; $song=Norm $t.Song
  $candidates= if($name -and $nameGroups.ContainsKey($name)) {@($nameGroups[$name])}else{@()}
  $status='NO_UNIQUE_ARTIST_MATCH'; $matchedAlbum=''; $matchedArtist=''; $albumId=''; $matchCount=0
  if($candidates.Count -eq 1 -and $song){
    $a=$candidates[0]; $matchedArtist=$a.artistName
    if(-not $cache.ContainsKey($a.id)){
      try{
        $url="$base/track?artistId=$($a.id)"
        $cache[$a.id]=@(Invoke-RestMethod -Uri $url -Headers $h -TimeoutSec 75 | Where-Object {$null -ne $_ -and $_.title})
      }catch{$cache[$a.id]=@(); Write-Warning "Couldn't fetch tracks for $($a.artistName): $($_.Exception.Message)"}
    }
    $found=@($cache[$a.id] | Where-Object {(Norm $_.title) -eq $song -and $_.albumId} | Select-Object -ExpandProperty albumId -Unique)
    $matchCount=$found.Count
    if($found.Count -eq 1){$status='EXACT_TRACK_IN_LIDARR'; $albumId=[string]$found[0]}
    elseif($found.Count -gt 1){$status='MULTIPLE_ALBUMS_REVIEW'}
    else{$status='TRACK_NOT_IN_LIDARR_CATALOG'}
  }
  $out.Add([pscustomobject]@{Artist=$t.Artist;Song=$t.Song;LidarrArtist=$matchedArtist;Status=$status;AlbumId=$albumId;PossibleAlbumCount=$matchCount;YouTubeUrl=$t.YouTubeUrl;DownloadRequested='NO'})
}
New-Item -ItemType Directory -Path $ReportFolder -Force | Out-Null
$preview=Join-Path $ReportFolder 'lidarr-track-review.csv'
$out | Export-Csv -LiteralPath $preview -NoTypeInformation -Encoding UTF8
$needs=@($out | Where-Object {$_.Status -ne 'EXACT_TRACK_IN_LIDARR'})
$needs | Export-Csv -LiteralPath (Join-Path $ReportFolder 'unmatched-youtube-review.csv') -NoTypeInformation -Encoding UTF8
$eligible=@($out | Where-Object {$_.Status -eq 'EXACT_TRACK_IN_LIDARR'})
$albumIds=@($eligible | Select-Object -ExpandProperty AlbumId -Unique)
Write-Host "`n===== LIDARR TRACK REVIEW ====="
Write-Host "Input tracks: $($out.Count)"
Write-Host "Exact track matches: $($eligible.Count)"
Write-Host "Distinct albums eligible for review: $($albumIds.Count)"
Write-Host "Unmatched or ambiguous tracks: $($needs.Count)"
Write-Host "Reports: $ReportFolder"
if($PreviewOnly -or $albumIds.Count -eq 0){Write-Host 'Preview only; no searches requested.';return}
Write-Host "`nCAUTION: Lidarr searches albums, not individual songs. An album search can download more music than the selected tracks."
Write-Host 'Open lidarr-track-review.csv to inspect matching track/album IDs before approving.'
$answer=Read-Host 'Type REVIEWED to continue to an album search confirmation (anything else cancels)'
if($answer -cne 'REVIEWED'){Write-Host 'Cancelled; no searches requested.';return}
$sample = @($albumIds | Select-Object -First 15) -join ', '
Write-Host "Album IDs to request: $sample"
if($albumIds.Count -gt 15){Write-Host "... plus $($albumIds.Count-15) more"}
$confirm=Read-Host "Type SEARCH $($albumIds.Count) ALBUMS to request searches, or Enter to cancel"
if($confirm -cne "SEARCH $($albumIds.Count) ALBUMS"){Write-Host 'Cancelled; no searches requested.';return}
$payload=@{name='AlbumSearch';albumIds=@($albumIds | ForEach-Object {[int]$_})} | ConvertTo-Json -Depth 8
$bytes=[Text.Encoding]::UTF8.GetBytes($payload)
try{
  $command=Invoke-RestMethod -Method Post -Uri "$base/command" -Headers $h -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 60
  Write-Host "Lidarr accepted the album-search command (command ID: $($command.id)). Download results are not guaranteed."
  @([pscustomobject]@{Timestamp=(Get-Date).ToString('o');CommandId=$command.id;AlbumCount=$albumIds.Count;AlbumIds=($albumIds -join '|')}) | Export-Csv -LiteralPath (Join-Path $ReportFolder 'lidarr-search-requests.csv') -NoTypeInformation -Encoding UTF8
}catch{Write-Warning "Lidarr did not accept the request: $($_.Exception.Message)"}
