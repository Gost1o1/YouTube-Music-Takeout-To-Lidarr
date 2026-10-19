#Requires -Version 5.1
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$TracksCsv,
 [Parameter(Mandatory=$true)][string]$ReportFolder,
 [Parameter(Mandatory=$true)][string]$LidarrUrl,
 [Parameter(Mandatory=$true)][string]$LidarrApiKey
)
$ErrorActionPreference='Stop'
if(-not (Test-Path -LiteralPath $TracksCsv)){throw "Track inventory not found: $TracksCsv"}
Write-Host ''
Write-Host '===== OPTIONAL LIDARR MUSIC DOWNLOAD =====' -ForegroundColor Cyan
Write-Host 'This does not download audio directly from YouTube Music.'
Write-Host 'Lidarr uses your own indexers and download clients. It searches ALBUMS, not individual songs.'
Write-Host 'Artist imports alone do not prove that the intended song or recording exists in Lidarr.'
Write-Host 'This pass verifies exact artist + track catalog matches, then saves an unmatched YouTube review list.'
Write-Host 'It will not request searches unless you explicitly approve the matching albums.'
Write-Host 'Artists without track-level matches remain on hold; no forced name-only downloads.'
Write-Host ''
$answer=Read-Host 'Press Enter or Y to inspect all tracks and review eligible album searches (N cancels)'
if($answer.Trim().ToUpperInvariant() -notin @('','Y')){Write-Host 'Cancelled. No searches requested.';return}
# All operations and the two separate approval prompts are in Lidarr-Review.ps1.
# Use its existing catalog evidence gate, album-ID deduplication and unmatched YouTube queue.
& (Join-Path $PSScriptRoot 'Lidarr-Review.ps1') -TracksCsv $TracksCsv -ReportFolder $ReportFolder -LidarrUrl $LidarrUrl -LidarrApiKey $LidarrApiKey
Write-Host ''
Write-Host 'Review lidarr-track-review.csv, unmatched-youtube-review.csv, and any lidarr-search-requests.csv.'
Write-Host 'A successful Lidarr command submission does NOT mean that audio was downloaded.'
