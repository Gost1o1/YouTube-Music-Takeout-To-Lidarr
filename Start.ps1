#Requires -Version 5.1
[CmdletBinding()]
param([string]$InputPath='', [string]$OutputRoot='', [string]$LidarrUrl='', [string]$LidarrApiKey='', [switch]$NoPrompt)
$ErrorActionPreference = 'Stop'
$toolName = 'YouTube Music Takeout to Lidarr'
function Get-Prop($row, [string[]]$aliases) {
    foreach ($alias in $aliases) {
        foreach ($p in $row.PSObject.Properties) {
            if ($p.Name.Trim() -ieq $alias -and $null -ne $p.Value) { return [string]$p.Value }
        }
    }
    return ''
}
function Normalize([string]$name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return '' }
    return [regex]::Replace($name.Trim().ToLowerInvariant(), '[^\p{L}\p{N}]', '')
}
function Html([string]$v) { return [System.Net.WebUtility]::HtmlEncode($v) }
function Pick-Folder($hint) {
    if ($hint -and (Test-Path -LiteralPath $hint)) {return (Resolve-Path -LiteralPath $hint).Path}
    if ($NoPrompt) {throw 'Supply -InputPath when using -NoPrompt.'}
    Write-Host '\nFind your Google Takeout ZIP (or extracted Takeout folder) under Downloads.'
    Write-Host 'Download a new export: https://takeout.google.com/settings/takeout'
    while ($true) {
        $v = Read-Host 'Paste the full ZIP or folder path (Enter to cancel)'
        $v = $v.Trim('"').Trim()
        if (-not $v) {throw 'Cancelled by user.'}
        if (Test-Path -LiteralPath $v) {return (Resolve-Path -LiteralPath $v).Path}
        Write-Warning 'That path does not exist. Try again.'
    }
}
$homeFolder = [Environment]::GetFolderPath('MyDocuments')
if (-not $homeFolder) { $homeFolder = Join-Path $env:USERPROFILE 'Documents' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $homeFolder 'MusicTakeoutToolkit' }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$runFolder = Join-Path $OutputRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Force -Path $runFolder | Out-Null
$tempFolder = Join-Path ([IO.Path]::GetTempPath()) ('MusicTakeoutToolkit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempFolder | Out-Null
try {
    $source = Pick-Folder $InputPath
    Write-Host "Source: $source"
    if ((Test-Path -LiteralPath $source -PathType Leaf) -and $source -match '\.zip$') {
        Write-Host 'Extracting ZIP to temporary workspace...'
        Expand-Archive -LiteralPath $source -DestinationPath $tempFolder -Force
        $scanRoot = $tempFolder
    } elseif (Test-Path -LiteralPath $source -PathType Container) {
        $scanRoot = $source
    } else { throw 'Input must be a ZIP file or extracted folder.' }

    $csvs = @(Get-ChildItem -LiteralPath $scanRoot -Filter '*.csv' -Recurse -File -ErrorAction SilentlyContinue)
    $songs = New-Object System.Collections.Generic.List[object]
    $seen = @{}; $scanned = 0; $unsupported = 0
    foreach ($file in $csvs) {
        $rows = @()
        try { $rows = @(Import-Csv -LiteralPath $file.FullName -ErrorAction Stop) }
        catch { $unsupported++; continue }
        if ($rows.Count -eq 0) { continue }
        $names = @($rows[0].PSObject.Properties.Name)
        $artistCols = @('Artist','Artists','Artist Name','Channel Title','Channel Name','Creator')
        $songCols = @('Song','Title','Track','Track Name','Video Title','Playlist Video Title','Name')
        $hasArtist = @($names | Where-Object { $artistCols -icontains $_ }).Count -gt 0
        $hasSong = @($names | Where-Object { $songCols -icontains $_ }).Count -gt 0
        if (-not $hasSong) { continue }
        $scanned++
        foreach ($r in $rows) {
            $title = Get-Prop $r $songCols
            $artist = Get-Prop $r $artistCols
            $videoId = Get-Prop $r @('Video ID','VideoId','YouTube Video ID','Youtube Video ID')
            $url = Get-Prop $r @('Video URL','Video Url','URL','Link','YouTube URL')
            if (-not $videoId -and $url -match '(?:v=|youtu\.be/|/shorts/)([A-Za-z0-9_-]{11})') { $videoId=$Matches[1] }
            if (-not $title -and -not $videoId) { continue }
            if (-not $title) { $title='(title missing)' }
            if ($videoId -notmatch '^[A-Za-z0-9_-]{11}$') { $videoId='' }
            $youtube = if ($videoId) { 'https://music.youtube.com/watch?v=' + $videoId } else { '' }
            $key = (Normalize $artist) + '|' + (Normalize $title) + '|' + $videoId
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key]=$true
            $songs.Add([pscustomobject]@{
                Artist=$artist; Song=$title; VideoId=$videoId; YouTubeUrl=$youtube
                SourceFile=$file.Name; MatchStatus='NOT_CHECKED'; DownloadRequested='NO'
            })
        }
    }

    # Optional read-only Lidarr name check. Explicit URL and key only; never search Docker or local credentials.
    $lidarrNames = @{}; $lidarrCount = -1; $lidarrState = 'Not connected'
    if ($LidarrUrl -and $LidarrApiKey) {
        try {
            $url = $LidarrUrl.TrimEnd('/')
            if ($url -notmatch '/api/v1$') { $url += '/api/v1' }
            $data = Invoke-RestMethod -Uri "$url/artist" -Headers @{'X-Api-Key'=$LidarrApiKey} -TimeoutSec 30
            $installed = @($data | Where-Object { $null -ne $_ -and $_.artistName })
            $lidarrCount = $installed.Count
            foreach ($a in $installed) {
                $names = @([string]$a.artistName)
                if ($a.aliases) { $names += @($a.aliases | ForEach-Object { $_.name }) }
                foreach ($n in $names) { $k=Normalize $n; if($k){$lidarrNames[$k]=$true} }
            }
            $lidarrState='Connected, name/alias comparison only'
        } catch { $lidarrState='Connection failed (no changes made)' }
    }
    $artistSet=@{}
    foreach ($s in $songs) {
        if ($s.Artist) { $artistSet[(Normalize $s.Artist)] = $s.Artist }
        $key = Normalize $s.Artist
        if (-not $key) { $s.MatchStatus = 'ARTIST_UNKNOWN' }
        elseif ($lidarrCount -lt 0) { $s.MatchStatus='NOT_CHECKED' }
        elseif ($lidarrNames.ContainsKey($key)) { $s.MatchStatus='NAME_IN_LIDARR_UNVERIFIED' }
        else { $s.MatchStatus='NOT_IN_LIDARR_BY_NAME' }
    }
    $artistRows = @(
        foreach($entry in $artistSet.GetEnumerator()) {
            [pscustomobject]@{Artist=$entry.Value; LidarrNameMatch= if($lidarrCount -lt 0){'NOT_CHECKED'} elseif($lidarrNames.ContainsKey($entry.Key)){'YES_UNVERIFIED'}else{'NO'} }
        }
    )
    $tracksPath = Join-Path $runFolder 'tracks.csv'
    $artistPath = Join-Path $runFolder 'artists.csv'
    $queuePath = Join-Path $runFolder 'optional-youtube-queue.csv'
    $reportPath = Join-Path $runFolder 'dashboard.html'
    $songs | Export-Csv -LiteralPath $tracksPath -NoTypeInformation -Encoding UTF8
    $artistRows | Sort-Object Artist | Export-Csv -LiteralPath $artistPath -NoTypeInformation -Encoding UTF8
    @($songs | Where-Object { $_.YouTubeUrl }) | Export-Csv -LiteralPath $queuePath -NoTypeInformation -Encoding UTF8
    $withVideo=@($songs | Where-Object { $_.VideoId }).Count
    $countKnown=@($songs | Where-Object { $_.MatchStatus -eq 'NAME_IN_LIDARR_UNVERIFIED' }).Count
    $countMissing=@($songs | Where-Object { $_.MatchStatus -eq 'NOT_IN_LIDARR_BY_NAME' }).Count
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Music Takeout Toolkit</title><style>body{font:16px system-ui,Segoe UI,sans-serif;max-width:1100px;margin:36px auto;padding:0 16px;color:#242936}a{color:#135ebc}.stats{display:flex;gap:12px;flex-wrap:wrap}.stat,.panel{border:1px solid #ddd;border-radius:12px;padding:16px;margin:12px 0}.stat b{display:block;font-size:28px}small{color:#616b78}table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #e4e6e8;padding:8px;text-align:left;word-break:break-word}input{padding:10px;max-width:100%;width:450px}.scroll{overflow:auto;max-height:520px}</style></head><body>')
    [void]$sb.AppendLine('<h1>YouTube Music Takeout to Lidarr</h1><p><small>Local audit. No downloads, no Lidarr changes, no file moves.</small></p>')
    [void]$sb.AppendLine('<div class="stats"><div class="stat"><b>'+$songs.Count+'</b>Track rows found</div><div class="stat"><b>'+$artistSet.Count+'</b>Artist credits</div><div class="stat"><b>'+$withVideo+'</b>Valid YouTube IDs</div><div class="stat"><b>'+$scanned+'</b>Recognized CSV files</div></div>')
    [void]$sb.AppendLine('<div class="panel"><h2>What you can do next</h2><p><a href="https://takeout.google.com/settings/takeout">Get a new Google Takeout export</a> · Review the local tracks.csv and artists.csv · Use optional-youtube-queue.csv to review original source videos · Connect Lidarr for a read-only name comparison · Download your own or authorized content separately.</p><p><strong>Nothing has been downloaded.</strong> Name matches are not proof of a specific recording. Your Takeout format may contain other music records not recognized by this CSV-only version.</p><p>Lidarr: '+(Html $lidarrState)+'; artist count: '+$(if($lidarrCount -ge 0){$lidarrCount}else{'not checked'})+'; matched track rows: '+$countKnown+'; missing-by-name track rows: '+$countMissing+'.</p></div>')
    [void]$sb.AppendLine('<div class="panel"><h2>Saved songs</h2><input id="find" type="search" placeholder="Search artist or song" oninput="filterRows()"><div class="scroll"><table id="tracks"><thead><tr><th>Artist</th><th>Song</th><th>Match status</th><th>Source video</th></tr></thead><tbody>')
    foreach($s in $songs) {
        $link = if ($s.YouTubeUrl) {'<a href="'+(Html $s.YouTubeUrl)+'" target="_blank" rel="noopener">Open</a>'} else {'—'}
        [void]$sb.AppendLine('<tr><td>'+(Html $s.Artist)+'</td><td>'+(Html $s.Song)+'</td><td>'+(Html $s.MatchStatus)+'</td><td>'+$link+'</td></tr>')
    }
    [void]$sb.AppendLine('</tbody></table></div></div><script>function filterRows(){var q=document.getElementById("find").value.toLowerCase();document.querySelectorAll("#tracks tbody tr").forEach(function(r){r.hidden=!r.textContent.toLowerCase().includes(q)})}</script></body></html>')
    [IO.File]::WriteAllText($reportPath,$sb.ToString(),(New-Object System.Text.UTF8Encoding($false)))
    Write-Host "`n===== MUSIC TAKEOUT SUMMARY =====" -ForegroundColor Cyan
    Write-Host "Songs identified: $($songs.Count)"
    Write-Host "Artist credits: $($artistSet.Count)"
    Write-Host "Source video IDs: $withVideo"
    Write-Host "Recognized CSV files: $scanned"
    Write-Host "Lidarr: $lidarrState"
    Write-Host "Reports saved in: $runFolder"
    Write-Host 'No searches, downloads or imports occurred during inventory.'
    if (-not $NoPrompt) {
        do {
            Write-Host "`nOPTIONS: [1] Open dashboard  [2] Open report folder  [3] Open Google Takeout  [4] Finish  [5] Review and optionally request Lidarr album searches"
            $choice = Read-Host 'Choose'
            switch ($choice) {
                '1' { Start-Process -FilePath $reportPath }
                '2' { Start-Process -FilePath $runFolder }
                '3' { Start-Process 'https://takeout.google.com/settings/takeout' }
                '4' { break }
                '5' { & (Join-Path $PSScriptRoot 'Lidarr-Review.ps1') -TracksCsv $tracksPath -ReportFolder $runFolder -LidarrUrl $LidarrUrl -LidarrApiKey $LidarrApiKey }
                default { Write-Host 'Choose 1 through 5.' }
            }
        } until ($choice -eq '4')
    }
} finally {
    # Only remove our private ZIP-extraction folder. Never delete the user's Takeout export or reports.
    if (Test-Path -LiteralPath $tempFolder) { Remove-Item -LiteralPath $tempFolder -Recurse -Force -ErrorAction SilentlyContinue }
}
