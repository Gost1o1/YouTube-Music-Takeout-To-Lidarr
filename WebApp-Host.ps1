#Requires -Version 5.1
[CmdletBinding()]
param([string]$InputPath='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$ErrorActionPreference='Continue'
$docs=[Environment]::GetFolderPath('MyDocuments')
$root=Join-Path $docs 'MusicTakeoutToolkit'
[IO.Directory]::CreateDirectory($root) | Out-Null
$script:serverLog=Join-Path $root 'startup-errors.log'
function Log-Server([string]$message){try{Add-Content -LiteralPath $script:serverLog -Value ('['+(Get-Date).ToString('s')+'] '+$message) -Encoding UTF8}catch{}}
$script:data=[ordered]@{archive=$InputPath;analyzing=$false;analysisError='';summary=$null;tracks=@();report='';lidarrUrl='http://127.0.0.1:8686';lidarrConnected=$false;lidarrArtists=@();comparison=$null;eventLog=@()}
$script:child=$null;$script:outLog='';$script:errLog='';$script:analysisStart=Get-Date;$script:runRoot=''
function Send-Json($ctx,$obj,[int]$code=200){
  $json=ConvertTo-Json -InputObject $obj -Depth 12 -Compress
  $bytes=[Text.Encoding]::UTF8.GetBytes($json)
  $ctx.Response.StatusCode=$code
  $ctx.Response.ContentType='application/json; charset=utf-8'
  $ctx.Response.Headers.Add('Cache-Control','no-store')
  $ctx.Response.OutputStream.Write($bytes,0,$bytes.Length)
  $ctx.Response.Close()
}
function Send-Error($ctx,$message,[int]$code=400){Send-Json $ctx @{error=[string]$message} $code}
function Read-Body($ctx){
  $reader=New-Object IO.StreamReader($ctx.Request.InputStream,[Text.Encoding]::UTF8)
  try {$raw=$reader.ReadToEnd();if($raw){return ConvertFrom-Json -InputObject $raw}}finally{$reader.Dispose()}
  return $null
}
function Latest-Takeout {
  $dl=Join-Path $env:USERPROFILE 'Downloads'
  return @(Get-ChildItem -LiteralPath $dl -Filter 'takeout*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
}
function Get-ValidatedArchive([string]$path){
  if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'The selected ZIP file does not exist.'}
  if(-not $path.EndsWith('.zip',[StringComparison]::OrdinalIgnoreCase)){throw 'Select a ZIP archive from Google Takeout.'}
  Add-Type -AssemblyName System.IO.Compression
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $z=[IO.Compression.ZipFile]::OpenRead($path)
  try {$entries=@($z.Entries | Where-Object {$_.FullName -match '(?i)(music library songs|music library albums|music library artists|youtube and youtube music)'})
    if(-not $entries.Count){throw 'This ZIP does not appear to include recognizable YouTube Music data. Select the right Takeout ZIP.'}
  }finally{$z.Dispose()}
  return $path
}
function Check-Process {
  if(-not $script:data.analyzing -or -not $script:child){return}
  $script:child.Refresh()
  if(-not $script:child.HasExited){return}
  $script:data.analyzing=$false
  $exit=$script:child.ExitCode
  $err=if(Test-Path -LiteralPath $script:errLog){[IO.File]::ReadAllText($script:errLog)}else{''}
  $out=if(Test-Path -LiteralPath $script:outLog){[IO.File]::ReadAllText($script:outLog)}else{''}
  # Use a dedicated output root per analysis. Avoid relying on directory timestamps.
  $reports=@(Get-ChildItem -LiteralPath $script:runRoot -Filter 'tracks.csv' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
  if(-not $reports.Count){
    $message='The parser did not produce tracks.csv (exit code '+$exit+').'
    $diagnostic=($err+' '+$out).Trim()
    if($diagnostic){$message+=' '+$diagnostic.Substring(0,[Math]::Min(1600,$diagnostic.Length))}
    $script:data.analysisError=$message
    Log-Server ('Analyze failed: '+$message)
    return
  }
  try {
    $script:data.report=$reports[0].DirectoryName
    $all=@(Import-Csv -LiteralPath $reports[0].FullName -Encoding UTF8)
    if(-not $all.Count){throw 'The report contains no music records.'}
    $script:data.tracks=$all
    $names=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $links=0
    foreach($t in $all){if($t.Artist){[void]$names.Add($t.Artist.Trim())};if($t.YouTubeUrl -match '^https?://'){$links++}}
    $script:data.summary=@{songs=$all.Count;artists=$names.Count;artistCreditsLabel='Unique displayed artist strings';links=$links;uploadedAudio='See full Takeout report';warning=if($err){'Analysis completed with nonfatal warnings.'}else{''}}
    if($exit -ne 0){Log-Server ('Parser returned exit '+$exit+' but valid tracks.csv was recovered. '+$err)}
    Log-Server ('Analysis succeeded: '+$all.Count+' songs; '+$script:data.report)
  } catch {
    $script:data.analysisError='Unable to load parsed tracks: '+$_.Exception.Message
    Log-Server $script:data.analysisError
  }
}
function Check-LidarrUrl([string]$url){
  if(-not $url -or $url -notmatch '^https?://'){throw 'Enter an http:// or https:// Lidarr address.'}
  $uri=[uri]$url
  if($uri.UserInfo){throw 'Do not put passwords or API keys in the address.'}
  if($uri.Host -eq '0.0.0.0'){throw 'Use an address reachable from this computer.'}
  $base=$url.TrimEnd('/')
  try {
    $req=[Net.HttpWebRequest]::Create($base+'/api/v1/system/status');$req.Method='GET';$req.Timeout=2500
    $resp=$req.GetResponse();$resp.Dispose()
    return @{found=$true;url=$base}
  } catch [Net.WebException] {
    $ex=$_.Exception
    if($ex.Response -and ([int]$ex.Response.StatusCode -eq 401 -or [int]$ex.Response.StatusCode -eq 403)){return @{found=$true;url=$base;requiresApiKey=$true}}
    # Lidarr commonly returns unauthorized when the service is reachable.
    return @{found=$false;url=$base;message=$ex.Message}
  }
}
function Handle($ctx){
  try {
    $path=$ctx.Request.Url.AbsolutePath
    $method=$ctx.Request.HttpMethod
    if($path -eq '/' -or $path -eq '/index.html'){
      $file=Join-Path $PSScriptRoot 'AppUI\index.html'
      $bytes=[IO.File]::ReadAllBytes($file)
      $ctx.Response.ContentType='text/html; charset=utf-8';$ctx.Response.Headers.Add('Cache-Control','no-store')
      $ctx.Response.OutputStream.Write($bytes,0,$bytes.Length);$ctx.Response.Close();return
    }
    if($path -eq '/api/state'){Check-Process;Send-Json $ctx @{archive=$script:data.archive;analyzing=$script:data.analyzing;analysisError=$script:data.analysisError;summary=$script:data.summary;tracks=@($script:data.tracks | Select-Object -First 2000 Artist,Song,YouTubeUrl,MatchStatus);report=$script:data.report;lidarrUrl=$script:data.lidarrUrl;lidarrConnected=$script:data.lidarrConnected;comparison=$script:data.comparison};return}
    if($method -ne 'POST'){Send-Error $ctx 'Unknown resource.' 404;return}
    $body=Read-Body $ctx
    if($path -eq '/api/scan'){
      $files=Latest-Takeout
      $found=''
      foreach($f in $files){try{$found=Get-ValidatedArchive $f.FullName;break}catch{}}
      if(-not $found){
        $dialog=New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title='Select your Google Takeout ZIP';$dialog.Filter='ZIP archives (*.zip)|*.zip';$dialog.InitialDirectory=Join-Path $env:USERPROFILE 'Downloads'
        if($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$found=Get-ValidatedArchive $dialog.FileName}
        $dialog.Dispose()
      }
      if($found){$script:data.archive=$found;Send-Json $ctx @{found=$true;archive=$found}}
      else{Send-Json $ctx @{found=$false;message='No matching Takeout archive was selected.'}}
      return
    }
    if($path -eq '/api/analyze'){
      if($script:data.analyzing){Send-Json $ctx @{started=$true};return}
      $archive=Get-ValidatedArchive $script:data.archive
      $script:data.summary=$null;$script:data.tracks=@();$script:data.analysisError='';$script:data.report=''
      $script:analysisStart=Get-Date
      $script:runRoot=Join-Path $root ('runs\'+[guid]::NewGuid().ToString('N'))
      [IO.Directory]::CreateDirectory($script:runRoot) | Out-Null
      $script:outLog=Join-Path $script:runRoot ('ui-engine-'+[guid]::NewGuid().ToString('N')+'.out.log')
      $script:errLog=Join-Path $script:runRoot ('ui-engine-'+[guid]::NewGuid().ToString('N')+'.err.log')
      $engine=Join-Path $PSScriptRoot 'Takeout-Engine.ps1'
      $cmd='-NoProfile -ExecutionPolicy Bypass -File "'+$engine+'" -InputPath "'+$archive+'" -OutputRoot "'+$script:runRoot+'" -NoPrompt'
      Log-Server ('Starting analysis: '+$archive+' Output: '+$script:runRoot)
      $script:child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $cmd -PassThru -WindowStyle Hidden -RedirectStandardOutput $script:outLog -RedirectStandardError $script:errLog
      $script:data.analyzing=$true
      Send-Json $ctx @{started=$true};return
    }
    if($path -eq '/api/lidarr/check'){
      $url=if($body.url){[string]$body.url}else{'http://127.0.0.1:8686'}
      Send-Json $ctx (Check-LidarrUrl $url);return
    }
    if($path -eq '/api/lidarr/connect'){
      $url=[string]$body.url;$key=[string]$body.key
      if(-not $key){throw 'Paste your Lidarr API key.'}
      $url=$url.TrimEnd('/')
      $headers=@{'X-Api-Key'=$key}
      $artists=@(Invoke-RestMethod -Uri ($url+'/api/v1/artist') -Headers $headers -TimeoutSec 15 -ErrorAction Stop)
      $script:data.lidarrUrl=$url;$script:data.lidarrConnected=$true;$script:data.lidarrArtists=$artists
      # Key intentionally used only for this request; the review interface is read-only.
      Send-Json $ctx @{connected=$true;artists=$artists.Count};return
    }
    if($path -eq '/api/lidarr/compare'){
      if(-not $script:data.lidarrConnected){throw 'Connect to Lidarr first.'}
      if(-not $script:data.tracks.Count){throw 'Analyze YouTube Music data first.'}
      $existing=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
      foreach($a in $script:data.lidarrArtists){if($a.artistName){[void]$existing.Add(([regex]::Replace($a.artistName.ToLowerInvariant(),'[^\p{L}\p{N}]','')))}}
      $artists=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
      $present=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
      $unknown=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
      foreach($t in $script:data.tracks){$a=[string]$t.Artist;if(-not $a){continue};[void]$artists.Add($a);$n=[regex]::Replace($a.ToLowerInvariant(),'[^\p{L}\p{N}]','');if($existing.Contains($n)){[void]$present.Add($a)}else{[void]$unknown.Add($a)}}
      $script:data.comparison=@{totalArtists=$artists.Count;namePresent=$present.Count;requiresVerification=$unknown.Count;songs=$script:data.tracks.Count;note='Name comparison only. MusicBrainz recording identities are not verified. No new additions are authorized.'}
      Send-Json $ctx $script:data.comparison;return
    }
    Send-Error $ctx 'Unknown action.' 404
  }catch{try{Send-Error $ctx $_.Exception.Message 400}catch{try{$ctx.Response.Close()}catch{}}}
}
# Random free localhost port, IPv4 only. No external binding, no external dependencies.
$socket=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$socket.Start();$port=($socket.LocalEndpoint).Port;$socket.Stop()
$listener=New-Object Net.HttpListener
$prefix='http://127.0.0.1:'+ $port +'/'
$listener.Prefixes.Add($prefix)
try{$listener.Start();Log-Server ('Listening on '+$prefix)}catch{Log-Server ('Listener could not start: '+$_.Exception.ToString());throw 'Unable to start the local interface: '+$_.Exception.Message}
$url=$prefix
$edgePaths=@("$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe","$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe")
$edge=$edgePaths|Where-Object{Test-Path -LiteralPath $_}|Select-Object -First 1
if($edge){Start-Process -FilePath $edge -ArgumentList @('--app='+$url,'--window-size=1280,900') | Out-Null}
else{Start-Process $url | Out-Null}
Write-Host ('YouTube Music to Lidarr v0.6.3 UI running at '+$url)
Write-Host 'Keep this PowerShell process open while using the app. Press Ctrl+C to stop.'
try {
  while($listener.IsListening){
    $pending=$listener.BeginGetContext($null,$null)
    while(-not $pending.AsyncWaitHandle.WaitOne(250)) {Check-Process}
    try{$context=$listener.EndGetContext($pending);Handle $context}catch{Log-Server ('Request processing failed: '+$_.Exception.ToString());Write-Warning ('Local service request failed: '+$_.Exception.Message)}
  }
}catch{Log-Server ('Server stopped unexpectedly: '+$_.Exception.ToString());throw}finally{Log-Server 'Listener closed';$listener.Stop();$listener.Close()}
