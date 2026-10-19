#Requires -Version 5.1
[CmdletBinding()]
param([string]$InputPath='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$documents=[Environment]::GetFolderPath('MyDocuments')
if(-not $documents){$documents=Join-Path $env:USERPROFILE 'Documents'}
$root=Join-Path $documents 'MusicTakeoutToolkit'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$script:sessionKey='';$script:sessionUrl='http://127.0.0.1:8686';$script:currentReport='';$script:engineProcess=$null;$script:engineOutput='';$script:engineError='';$script:started=$false
function Info([string]$message){$script:status.Text=$message;$script:log.AppendText((Get-Date -Format 'HH:mm:ss')+'  '+$message+[Environment]::NewLine)}
function Alert([string]$message){[System.Windows.Forms.MessageBox]::Show($message,'Music Takeout to Lidarr','OK','Information')|Out-Null}
function Read-TrackFile {
 if(-not $script:currentReport){return}
 $csv=Join-Path $script:currentReport 'tracks.csv'
 if(-not (Test-Path -LiteralPath $csv)){return}
 $script:tracks=@(Import-Csv -LiteralPath $csv -Encoding UTF8)
 $script:grid.Rows.Clear()
 $script:grid.SuspendLayout()
 try {
  foreach($t in $script:tracks){
   $i=$script:grid.Rows.Add()
   $script:grid.Rows[$i].Cells[0].Value=$t.Artist
   $script:grid.Rows[$i].Cells[1].Value=$t.Song
   $script:grid.Rows[$i].Cells[2].Value=$t.MatchStatus
   $script:grid.Rows[$i].Cells[3].Value=$t.YouTubeUrl
  }
 } finally {$script:grid.ResumeLayout()}
 $script:countLabel.Text=('Tracks: '+$script:tracks.Count+'    Artists: '+@($script:tracks|Where-Object {$_.Artist}|Select-Object -ExpandProperty Artist -Unique).Count)
 Info ('Loaded '+$script:tracks.Count+' Takeout records. No downloads requested.')
 if($script:wizard){Update-LibrarySummary;Set-WizardStage 4;Info 'Takeout analysis complete. Review your music library and continue to Lidarr setup.'}
}
function New-Button($text,$x,$y,$w,$handler){
 $b=New-Object System.Windows.Forms.Button
 $b.Text=$text;$b.Location=New-Object System.Drawing.Point($x,$y);$b.Size=New-Object System.Drawing.Size($w,35)
 $b.Add_Click($handler);$script:form.Controls.Add($b);return $b
}
$script:form=New-Object System.Windows.Forms.Form
$script:form.Text='YouTube Music Takeout to Lidarr  |  v0.5.9'
$script:form.Size=New-Object System.Drawing.Size(1100,770)
$script:form.StartPosition='CenterScreen';$script:form.MinimumSize=New-Object System.Drawing.Size(850,650)
$script:form.Font=New-Object System.Drawing.Font('Segoe UI',10)
$header=New-Object System.Windows.Forms.Label;$header.Text='YouTube Music Takeout to Lidarr';$header.Font=New-Object System.Drawing.Font('Segoe UI',17,[System.Drawing.FontStyle]::Bold)
$header.Location=New-Object System.Drawing.Point(18,15);$header.AutoSize=$true;$script:form.Controls.Add($header)
$desc=New-Object System.Windows.Forms.Label;$desc.Text='Private desktop workspace. Import and Lidarr checks are read-only by default.';$desc.Location=New-Object System.Drawing.Point(20,54);$desc.AutoSize=$true;$script:form.Controls.Add($desc)
$script:sourceBox=New-Object System.Windows.Forms.TextBox;$script:sourceBox.Location=New-Object System.Drawing.Point(20,96);$script:sourceBox.Size=New-Object System.Drawing.Size(750,25);$script:sourceBox.Anchor='Top,Left,Right';$script:form.Controls.Add($script:sourceBox)
if($InputPath){$script:sourceBox.Text=$InputPath}
$browse=New-Button 'Choose Takeout ZIP' 785 91 165 {
 $picker=New-Object System.Windows.Forms.OpenFileDialog;$picker.Title='Select your Google Takeout ZIP';$picker.Filter='ZIP archives (*.zip)|*.zip|All files (*.*)|*.*'
 $picker.InitialDirectory=Join-Path $env:USERPROFILE 'Downloads'
 if($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$script:sourceBox.Text=$picker.FileName}
 $picker.Dispose()
}
$browse.Anchor='Top,Right'
$download=New-Button 'Open Google Takeout' 20 135 174 {Start-Process 'https://takeout.google.com/settings/takeout/custom/youtube'}
$discover=New-Button 'Find newest Takeout' 201 135 176 {
 $dl=Join-Path $env:USERPROFILE 'Downloads'
 $files=@(Get-ChildItem -LiteralPath $dl -Filter 'takeout*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
 if($files.Count){$script:sourceBox.Text=$files[0].FullName;Info ('Selected newest Takeout ZIP: '+$files[0].Name)}
 else{Alert 'No Takeout-named ZIP was found in Downloads. Click Choose Takeout ZIP.'}
}
$start=New-Button 'Analyze Takeout' 384 135 155 {
 if($script:engineProcess -and -not $script:engineProcess.HasExited){Alert 'Analysis is still running.';return}
 $path=$script:sourceBox.Text.Trim().Trim('"')
 if(-not(Test-Path -LiteralPath $path)){Alert 'Choose an existing ZIP or folder first.';return}
 $script:currentReport='';$script:started=$true;$script:analysisStartedAt=Get-Date
 $script:progress.Style='Marquee';$script:progress.MarqueeAnimationSpeed=28
 $script:grid.Rows.Clear();$script:countLabel.Text='Analyzing archive…'
 $script:engineOutput=Join-Path $root ('engine-'+[guid]::NewGuid().ToString('N')+'.out.log')
 $script:engineError=Join-Path $root ('engine-'+[guid]::NewGuid().ToString('N')+'.err.log')
 $engine=Join-Path $PSScriptRoot 'Takeout-Engine.ps1'
 $cmd='-NoProfile -ExecutionPolicy Bypass -File "'+$engine+'" -InputPath "'+$path+'" -OutputRoot "'+$root+'" -NoPrompt'
 try {$script:engineProcess=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $cmd -WindowStyle Hidden -RedirectStandardOutput $script:engineOutput -RedirectStandardError $script:engineError -PassThru -ErrorAction Stop;Info 'Scanning the full Takeout archive in the background…'}
 catch {$script:progress.Style='Blocks';if($script:wizard){Set-WizardStage 2};Alert $_.Exception.Message}
}
$script:progress=New-Object System.Windows.Forms.ProgressBar;$script:progress.Location=New-Object System.Drawing.Point(20,185);$script:progress.Size=New-Object System.Drawing.Size(1035,15);$script:progress.Anchor='Top,Left,Right';$script:form.Controls.Add($script:progress)
$script:countLabel=New-Object System.Windows.Forms.Label;$script:countLabel.Text='No Takeout archive analyzed yet';$script:countLabel.Location=New-Object System.Drawing.Point(20,212);$script:countLabel.AutoSize=$true;$script:form.Controls.Add($script:countLabel)
$lidarrLabel=New-Object System.Windows.Forms.Label;$lidarrLabel.Text='Lidarr URL:';$lidarrLabel.Location=New-Object System.Drawing.Point(20,249);$lidarrLabel.AutoSize=$true;$script:form.Controls.Add($lidarrLabel)
$script:urlBox=New-Object System.Windows.Forms.TextBox;$script:urlBox.Text='http://127.0.0.1:8686';$script:urlBox.Location=New-Object System.Drawing.Point(110,245);$script:urlBox.Size=New-Object System.Drawing.Size(250,25);$script:form.Controls.Add($script:urlBox)
$keyLabel=New-Object System.Windows.Forms.Label;$keyLabel.Text='API key:';$keyLabel.Location=New-Object System.Drawing.Point(380,249);$keyLabel.AutoSize=$true;$script:form.Controls.Add($keyLabel)
$script:keyBox=New-Object System.Windows.Forms.TextBox;$script:keyBox.UseSystemPasswordChar=$true;$script:keyBox.Location=New-Object System.Drawing.Point(445,245);$script:keyBox.Size=New-Object System.Drawing.Size(325,25);$script:keyBox.Anchor='Top,Left,Right';$script:form.Controls.Add($script:keyBox)
$connect=New-Button 'Connect to Lidarr' 785 241 165 {
 $script:sessionUrl=$script:urlBox.Text.Trim().TrimEnd('/');$script:sessionKey=$script:keyBox.Text.Trim()
 if(-not $script:sessionKey){Alert 'In Lidarr, select Settings > General > API Key, then paste the key here.';return}
 try{
  $headers=@{'X-Api-Key'=$script:sessionKey}
  $artists=@(Invoke-RestMethod -Uri ($script:sessionUrl+'/api/v1/artist') -Headers $headers -TimeoutSec 30 -ErrorAction Stop)
  $script:installedArtists=$artists
  Info ('Connected to Lidarr: '+$artists.Count+' installed artists. No changes made.')
  Alert ('Connected successfully. '+$artists.Count+' installed artists found.')
 }catch{$script:sessionKey='';Alert ('Lidarr connection failed: '+$_.Exception.Message)}
}
$connect.Anchor='Top,Right'
$hint=New-Object System.Windows.Forms.Label;$hint.Text='Find the API key in Lidarr: left sidebar > Settings > General > API Key. Key is kept in memory only.';$hint.Location=New-Object System.Drawing.Point(20,278);$hint.AutoSize=$true;$script:form.Controls.Add($hint)
$compare=New-Button 'Compare with Lidarr' 20 311 195 {
 if(-not $script:sessionKey){Alert 'Connect to Lidarr first.';return}
 if(-not $script:tracks -or $script:tracks.Count -eq 0){Alert 'Analyze a Takeout archive first.';return}
 $hash=@{}
 foreach($a in $script:installedArtists){if($a.artistName){$hash[[regex]::Replace($a.artistName.ToLowerInvariant(),'[^\p{L}\p{N}]','')]=$true};foreach($al in @($a.aliases)){if($al.name){$hash[[regex]::Replace($al.name.ToLowerInvariant(),'[^\p{L}\p{N}]','')]=$true}}}
 $present=0;$missing=0
 foreach($r in $script:grid.Rows){
  if($r.IsNewRow){continue}
  $name=[string]$r.Cells[0].Value
  $norm=[regex]::Replace($name.ToLowerInvariant(),'[^\p{L}\p{N}]','')
  $state=if(-not $norm){'ARTIST_UNKNOWN'}elseif($hash.ContainsKey($norm)){$present++;'NAME_PRESENT_UNVERIFIED'}else{$missing++;'REQUIRES_IDENTITY_REVIEW'}
  $r.Cells[2].Value=$state
 }
 Info ("Name-only comparison: $present existing rows, $missing requiring identity review. These are not verified recording matches.")
}
$export=New-Button 'Export unmatched links' 222 311 194 {
 if(-not $script:tracks){Alert 'Analyze a Takeout archive first.';return}
 $csv=Join-Path $script:currentReport 'unmatched-youtube-review.csv'
 $rows=@(foreach($r in $script:grid.Rows){if($r.IsNewRow){continue};if([string]$r.Cells[2].Value -notmatch '^NAME_PRESENT'){[pscustomobject]@{Artist=$r.Cells[0].Value;Song=$r.Cells[1].Value;Status=$r.Cells[2].Value;YouTubeUrl=$r.Cells[3].Value}}})
 $rows|Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
 Info ('Exported '+$rows.Count+' review rows to '+$csv)
 Alert ('Saved to: '+$csv)
}
$openReports=New-Button 'Open reports folder' 424 311 174 {if($script:currentReport){Start-Process explorer.exe -ArgumentList ('"'+$script:currentReport+'"')}else{Start-Process explorer.exe -ArgumentList ('"'+$root+'"')}}
$disclaimer=New-Object System.Windows.Forms.Label;$disclaimer.Text='Preview only. Artist additions and downloads are NOT enabled in this first desktop build.';$disclaimer.Location=New-Object System.Drawing.Point(20,356);$disclaimer.AutoSize=$true;$script:form.Controls.Add($disclaimer)
$script:grid=New-Object System.Windows.Forms.DataGridView
$script:grid.Location=New-Object System.Drawing.Point(20,386);$script:grid.Size=New-Object System.Drawing.Size(1035,245)
$script:grid.Anchor='Top,Bottom,Left,Right';$script:grid.AllowUserToAddRows=$false;$script:grid.AllowUserToDeleteRows=$false;$script:grid.ReadOnly=$true
$script:grid.AutoSizeColumnsMode='Fill';$script:grid.SelectionMode='FullRowSelect';$script:grid.RowHeadersVisible=$false
[void]$script:grid.Columns.Add('Artist','Artist');[void]$script:grid.Columns.Add('Song','Song');[void]$script:grid.Columns.Add('Status','Matching status');[void]$script:grid.Columns.Add('Video','YouTube Music URL')
$script:grid.Columns[0].FillWeight=26;$script:grid.Columns[1].FillWeight=30;$script:grid.Columns[2].FillWeight=24;$script:grid.Columns[3].FillWeight=20
$script:grid.Add_CellDoubleClick({param($sender,$e) if($e.RowIndex -ge 0 -and $e.ColumnIndex -eq 3){$url=[string]$sender.Rows[$e.RowIndex].Cells[3].Value;if($url -match '^https://music\.youtube\.com/watch\?v='){Start-Process $url}}})
$script:form.Controls.Add($script:grid)
$script:status=New-Object System.Windows.Forms.Label;$script:status.Text='Ready. Select your Google Takeout ZIP.';$script:status.Location=New-Object System.Drawing.Point(20,646);$script:status.AutoSize=$true;$script:status.Anchor='Bottom,Left';$script:form.Controls.Add($script:status)
$script:log=New-Object System.Windows.Forms.TextBox;$script:log.Multiline=$true;$script:log.ReadOnly=$true;$script:log.ScrollBars='Vertical';$script:log.Location=New-Object System.Drawing.Point(20,670);$script:log.Size=New-Object System.Drawing.Size(1035,45);$script:log.Anchor='Bottom,Left,Right';$script:form.Controls.Add($script:log)
$script:timer=New-Object System.Windows.Forms.Timer;$script:timer.Interval=1300
$script:timer.Add_Tick({
 if(-not $script:started -or -not $script:engineProcess){return}
 if(-not $script:engineProcess.HasExited){return}
 $script:started=$false;$script:progress.Style='Blocks';$script:progress.MarqueeAnimationSpeed=0;$script:progress.Value=100
 $logText='';$errText=''
 if(Test-Path $script:engineOutput){$logText=[IO.File]::ReadAllText($script:engineOutput)}
 if(Test-Path $script:engineError){$errText=[IO.File]::ReadAllText($script:engineError)}
 # Windows PowerShell may emit harmless warnings or non-terminating messages to stderr.
 # Only a nonzero process exit code is a fatal analysis failure; if a report exists,
 # prefer to show its results even when a warning was logged.
 $script:engineProcess.Refresh()
 $exitCode=$script:engineProcess.ExitCode
 if($exitCode -ne 0){
  Info ('Takeout analysis failed with exit code '+$exitCode+'. See diagnostic logs in Documents\MusicTakeoutToolkit.')
  if($script:wizard){Set-WizardStage 2}
  $diagnostic=($errText+' '+$logText).Trim()
  if(-not $diagnostic){$diagnostic='The Takeout process returned a nonzero exit code. Please check the generated logs.'}
  Alert $diagnostic.Substring(0,[math]::Min(900,$diagnostic.Length))
  return
 }
 # Match this particular analysis run, not an older run, if there are existing reports.
 $runs=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {
  (Test-Path (Join-Path $_.FullName 'tracks.csv')) -and
  $_.LastWriteTime -ge $script:analysisStartedAt.AddSeconds(-5)
 } | Sort-Object LastWriteTime -Descending)
 if(-not $runs.Count){
  Info 'Analysis exited successfully but no new tracks.csv was found.'
  if($script:wizard){Set-WizardStage 2}
  Alert 'Analysis finished, but no new tracks.csv could be located. Please inspect the engine logs.'
  return
 }
 $script:currentReport=$runs[0].FullName
 if($errText.Trim()){Info 'Takeout analysis completed with warnings. Diagnostic details are saved in the engine log.'}
 try{Read-TrackFile}catch{
  if($script:wizard){Set-WizardStage 2}
  Alert ('Could not load generated report: '+$_.Exception.Message)
 }
})
$script:timer.Start()
$script:form.Add_FormClosing({$script:timer.Stop();$script:sessionKey='';$script:keyBox.Clear();if($script:engineProcess -and -not $script:engineProcess.HasExited){$choice=[System.Windows.Forms.MessageBox]::Show('Analysis is still running. Leave it running and close the interface?','Analysis running','YesNo');if($choice -ne 'Yes'){$_.Cancel=$true;$script:timer.Start()}}})

# First-run wizard: one question per screen. This deliberately does not access Gmail data.
$script:wizard=New-Object System.Windows.Forms.Panel
$script:wizard.Dock='Fill';$script:wizard.BackColor=[System.Drawing.Color]::White
$script:form.Controls.Add($script:wizard);$script:wizard.BringToFront()
$script:wizardStage=0

$script:wizardProgress=New-Object System.Windows.Forms.ProgressBar
$script:wizardProgress.Style='Continuous'
$script:wizardProgress.Minimum=0;$script:wizardProgress.Maximum=100;$script:wizardProgress.Value=33
$script:wizardProgress.Size=New-Object System.Drawing.Size(260,8)
$script:wizardProgress.Location=New-Object System.Drawing.Point(0,46)
$script:wizard.Controls.Add($script:wizardProgress)

$script:wizardHeading=New-Object System.Windows.Forms.Label
$script:wizardHeading.Font=New-Object System.Drawing.Font('Segoe UI',21,[System.Drawing.FontStyle]::Bold)
$script:wizardHeading.Size=New-Object System.Drawing.Size(880,56)
$script:wizardHeading.TextAlign='MiddleCenter'
$script:wizard.Controls.Add($script:wizardHeading)

$script:wizardText=New-Object System.Windows.Forms.Label
$script:wizardText.Font=New-Object System.Drawing.Font('Segoe UI',11)
$script:wizardText.Size=New-Object System.Drawing.Size(820,380)
$script:wizardText.TextAlign='TopCenter'
$script:wizard.Controls.Add($script:wizardText)

$script:wizardMain=New-Object System.Windows.Forms.Button
$script:wizardMain.Size=New-Object System.Drawing.Size(360,46)
$script:wizard.Controls.Add($script:wizardMain)

$script:wizardNext=New-Object System.Windows.Forms.Button
$script:wizardNext.Size=New-Object System.Drawing.Size(360,46)
$script:wizard.Controls.Add($script:wizardNext)

$script:wizardSkip=New-Object System.Windows.Forms.Button
$script:wizardSkip.Size=New-Object System.Drawing.Size(360,46)
$script:wizard.Controls.Add($script:wizardSkip)

# Results and connection screens remain inside the wizard, never reveal the legacy dashboard.
$script:librarySummary=New-Object System.Windows.Forms.Label
$script:librarySummary.Font=New-Object System.Drawing.Font('Segoe UI',12)
$script:librarySummary.TextAlign='MiddleCenter'
$script:librarySummary.Size=New-Object System.Drawing.Size(820,80)
$script:wizard.Controls.Add($script:librarySummary)
$script:previewGrid=New-Object System.Windows.Forms.DataGridView
$script:previewGrid.ReadOnly=$true;$script:previewGrid.AllowUserToAddRows=$false
$script:previewGrid.AllowUserToDeleteRows=$false;$script:previewGrid.RowHeadersVisible=$false
$script:previewGrid.AutoSizeColumnsMode='Fill';$script:previewGrid.SelectionMode='FullRowSelect'
[void]$script:previewGrid.Columns.Add('Number','#')
[void]$script:previewGrid.Columns.Add('Artist','Artist')
[void]$script:previewGrid.Columns.Add('Song','Song')
[void]$script:previewGrid.Columns.Add('Link','YouTube Link')
$script:previewGrid.Columns[0].FillWeight=8;$script:previewGrid.Columns[1].FillWeight=30
$script:previewGrid.Columns[2].FillWeight=40;$script:previewGrid.Columns[3].FillWeight=22
$script:previewGrid.Add_CellDoubleClick({param($sender,$e)
 if($e.RowIndex -ge 0 -and $e.ColumnIndex -eq 3){$url=[string]$sender.Rows[$e.RowIndex].Tag
 if($url -match '^https://(music\.)?youtube\.com/'){Start-Process $url}}
})
$script:wizard.Controls.Add($script:previewGrid)
$script:wizardAddress=New-Object System.Windows.Forms.TextBox
$script:wizardAddress.Text='http://127.0.0.1:8686';$script:wizardAddress.Font=New-Object System.Drawing.Font('Segoe UI',11)
$script:wizard.Controls.Add($script:wizardAddress)
$script:wizardKey=New-Object System.Windows.Forms.TextBox
$script:wizardKey.UseSystemPasswordChar=$true;$script:wizardKey.Font=New-Object System.Drawing.Font('Segoe UI',11)
$script:wizard.Controls.Add($script:wizardKey)
$script:lidarrMessage=New-Object System.Windows.Forms.Label
$script:lidarrMessage.Size=New-Object System.Drawing.Size(820,65);$script:lidarrMessage.TextAlign='MiddleCenter'
$script:lidarrMessage.Font=New-Object System.Drawing.Font('Segoe UI',11)
$script:wizard.Controls.Add($script:lidarrMessage)
$script:wizardGetKey=New-Object System.Windows.Forms.Button
$script:wizardGetKey.Text='Get API Key';$script:wizardGetKey.Size=New-Object System.Drawing.Size(360,46)
$script:wizard.Controls.Add($script:wizardGetKey)
$script:wizardGetKey.Add_Click({
 try{Start-Process $script:wizardAddress.Text.Trim()}catch{Alert ('Unable to open Lidarr: '+$_.Exception.Message)}
})
$script:connectionResult=New-Object System.Windows.Forms.Label
$script:connectionResult.Size=New-Object System.Drawing.Size(820,80);$script:connectionResult.TextAlign='MiddleCenter'
$script:wizard.Controls.Add($script:connectionResult)
function Update-LibrarySummary {
 if(-not $script:tracks){return}
 $artists=@($script:tracks|Where-Object {$_.Artist}|ForEach-Object {$_.Artist.Trim()}|Sort-Object -Unique)
 $urls=@($script:tracks|Where-Object {$_.YouTubeUrl -match '^https://'})
 $script:librarySummary.Text=('{0:N0} songs    |    {1:N0} unique displayed artists    |    {2:N0} music links' -f $script:tracks.Count,$artists.Count,$urls.Count)
 $script:previewGrid.Rows.Clear()
 $limit=[math]::Min(100,$script:tracks.Count)
 for($i=0;$i -lt $limit;$i++){
  $t=$script:tracks[$i];$row=$script:previewGrid.Rows.Add(($i+1),$t.Artist,$t.Song,$(if($t.YouTubeUrl){'Open link'}else{'Unavailable'}))
  $script:previewGrid.Rows[$row].Tag=[string]$t.YouTubeUrl
 }
}
function Start-WizardAnalysis([string]$path){
 $script:sourceBox.Text=$path
 Set-WizardStage 3
 $start.PerformClick()
}
function Test-LidarrReachable([string]$url){
 try{
  $response=Invoke-WebRequest -Uri ($url.TrimEnd('/')+'/api/v1/system/status') -UseBasicParsing -TimeoutSec 4 -ErrorAction Stop
  return $true
 }catch{
  # 401/403 demonstrates that Lidarr is listening but needs authentication.
  if($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -in @(401,403)){return $true}
  return $false
 }
}
function Show-LidarrConnection {
 Set-WizardStage 5
 $script:lidarrMessage.Text='Checking the default Lidarr address...'
 $script:form.Refresh()
 $script:lidarrFound=Test-LidarrReachable $script:wizardAddress.Text
 if($script:lidarrFound){$script:lidarrMessage.Text='Lidarr found! Open Lidarr and go to Settings > General > API Key. Click Get API Key when you are ready.'}
 else{$script:lidarrMessage.Text='Lidarr was not found at the default address. Enter the address you use to open Lidarr, then click Test Address.'}
 $script:wizardMain.Text=$(if($script:lidarrFound){'Connect to Lidarr'}else{'Test Address'})
 $script:wizardGetKey.Visible=$script:lidarrFound
 $script:wizardKey.Visible=$script:lidarrFound
}

function Layout-Wizard {
 $panelWidth=[math]::Max(1,$script:wizard.ClientSize.Width)
 $centerX=[int](($panelWidth-880)/2)
 $textX=[int](($panelWidth-820)/2)
 $buttonX=[int](($panelWidth-360)/2)
 $progX=[int](($panelWidth-260)/2)
 $script:wizardProgress.Location=New-Object System.Drawing.Point($progX,46)
 $script:wizardHeading.Location=New-Object System.Drawing.Point($centerX,90)
 if($script:wizardStage -eq 0){
  $script:wizardMain.Location=New-Object System.Drawing.Point($buttonX,245)
  $script:wizardSkip.Location=New-Object System.Drawing.Point($buttonX,309)
 } elseif($script:wizardStage -eq 1){
  $script:wizardText.Size=New-Object System.Drawing.Size(820,380)
  $script:wizardText.Location=New-Object System.Drawing.Point($textX,164)
  $script:wizardMain.Location=New-Object System.Drawing.Point($buttonX,575)
  $script:wizardNext.Location=New-Object System.Drawing.Point($buttonX,635)
 } else {
  # Keep the explanatory label above the buttons; previously its 380px height covered them.
  $script:wizardText.Size=New-Object System.Drawing.Size(820,110)
  $script:wizardText.Location=New-Object System.Drawing.Point($textX,270)
  $script:wizardMain.Location=New-Object System.Drawing.Point($buttonX,415)
  $script:wizardNext.Location=New-Object System.Drawing.Point($buttonX,480)
 }
 $script:librarySummary.Visible=($script:wizardStage -eq 4)
 $script:previewGrid.Visible=($script:wizardStage -eq 4)
 $script:lidarrMessage.Visible=($script:wizardStage -eq 5)
 $script:wizardAddress.Visible=($script:wizardStage -eq 5)
 $script:connectionResult.Visible=($script:wizardStage -eq 6)
 if($script:wizardStage -eq 4){
  $script:librarySummary.Location=New-Object System.Drawing.Point($textX,165)
  $script:previewGrid.Location=New-Object System.Drawing.Point([int](($panelWidth-850)/2),255)
  $script:previewGrid.Size=New-Object System.Drawing.Size(850,270)
  $script:wizardMain.Location=New-Object System.Drawing.Point($buttonX,560)
  $script:wizardNext.Location=New-Object System.Drawing.Point($buttonX,620)
 }elseif($script:wizardStage -eq 5){
  $script:lidarrMessage.Location=New-Object System.Drawing.Point($textX,205)
  $script:wizardAddress.Size=New-Object System.Drawing.Size(360,30)
  $script:wizardAddress.Location=New-Object System.Drawing.Point($buttonX,300)
  $script:wizardGetKey.Location=New-Object System.Drawing.Point($buttonX,360)
  $script:wizardKey.Size=New-Object System.Drawing.Size(360,30)
  $script:wizardKey.Location=New-Object System.Drawing.Point($buttonX,425)
  $script:wizardMain.Location=New-Object System.Drawing.Point($buttonX,490)
  $script:wizardNext.Location=New-Object System.Drawing.Point($buttonX,555)
 }elseif($script:wizardStage -eq 6){
  $script:connectionResult.Location=New-Object System.Drawing.Point($textX,230)
 }
 # Labels must never cover clickable controls, including after window resize.
 $script:wizardMain.BringToFront()
 $script:wizardNext.BringToFront()
 $script:wizardSkip.BringToFront()
}
$script:wizard.Add_Resize({Layout-Wizard})

function Set-WizardStage([int]$stage){
 $script:wizardStage=$stage
 $script:wizardGetKey.Visible=$false;$script:wizardKey.Visible=$false
 $script:wizardMain.Visible=$true;$script:wizardNext.Visible=$true;$script:wizardSkip.Visible=$false
 switch($stage){
  0 {
   $script:wizardProgress.Value=33
   $script:wizardHeading.Text='Choose Your Data'
   $script:wizardText.Text='';$script:wizardText.Visible=$false
   $script:wizardMain.Text='Choose Google Account'
   $script:wizardNext.Visible=$false
   $script:wizardSkip.Visible=$true
   $script:wizardSkip.Text='Downloaded Data File'
  }
  1 {
   $script:wizardProgress.Value=66
   $script:wizardText.Visible=$true
   $script:wizardHeading.Text='Prepare Your YouTube Music Export'
   $script:wizardText.Text=@'
You're about to be directed to Google Takeout to download a copy of your YouTube Music library.

On Google's website, click Next step, then change the File size to 50 GB.

Don't worry! Your music library metadata is likely much smaller than 50 GB. Song titles, artist names, and links may only take up a few megabytes. Uploaded media can make the export larger.

Google may take several minutes to several days to prepare your export; smaller exports are often ready within minutes.

You'll receive an email confirming that your export was requested.

You'll receive a second email with a link when your export is ready to download.

While the prepared archive is available, you can download it again from Google Takeout without waiting for a new export.
'@
   $script:wizardMain.Text='Continue to Google Takeout'
   $script:wizardNext.Text='Back'
  }
  2 {
   $script:wizardProgress.Value=40
   $script:wizardText.Visible=$true
   $script:wizardHeading.Text='Find Your Downloaded Music Data'
   $script:wizardText.Text='Once Google has prepared your export, download the ZIP archive. Click Scan for Takeout File to search your Downloads folder. If a valid archive cannot be found, the Windows file picker will open so you can select it manually.'
   $script:wizardMain.Text='Scan for Takeout File'
   $script:wizardNext.Text='Back'
  }
  3 {
   $script:wizardProgress.Style='Marquee';$script:wizardProgress.MarqueeAnimationSpeed=22
   $script:wizardHeading.Text='Analyzing Your Music Library'
   $script:wizardText.Visible=$true
   $script:wizardText.Text='Your Takeout archive was found. Reading the music library now; this may take a little while.'
   $script:wizardMain.Visible=$false;$script:wizardNext.Visible=$false
  }
  4 {
   $script:wizardProgress.Style='Continuous';$script:wizardProgress.Value=65
   $script:wizardHeading.Text='Your Music Library Is Ready!'
   $script:wizardText.Visible=$false
   $script:wizardMain.Text='Continue to Lidarr Setup'
   $script:wizardNext.Text='Back to Scan'
  }
  5 {
   $script:wizardProgress.Value=78
   $script:wizardHeading.Text='Connect to Lidarr'
   $script:wizardText.Visible=$false
   $script:wizardMain.Text='Test Address'
   $script:wizardNext.Text='Back to Library'
  }
  6 {
   $script:wizardProgress.Value=88
   $script:wizardHeading.Text='Connected to Lidarr!'
   $script:wizardText.Visible=$false
   $script:connectionResult.Text='Lidarr authentication worked. Your Takeout library is ready for a read-only comparison. Advanced matching and background imports are still in development.'
   $script:wizardMain.Text='Open Read-Only Comparison Workspace'
   $script:wizardNext.Text='Back'
  }
 }
 if($stage -ne 3){$script:wizardProgress.Style='Continuous';$script:wizardProgress.MarqueeAnimationSpeed=0}
 Layout-Wizard
}
function Test-TakeoutZip([string]$path){
 if(-not (Test-Path -LiteralPath $path -PathType Leaf)){return $false}
 try{
  Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
  Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
  $archive=[System.IO.Compression.ZipFile]::OpenRead($path)
  try {
   foreach($entry in $archive.Entries){
    $name=($entry.FullName -replace '\\','/').ToLowerInvariant()
    if($name -match '(^|/)(music library songs|music library albums|music library artists)\.csv$' -or $name -match '(^|/)youtube and youtube music/.*\.csv$') {return $true}
   }
   return $false
  } finally {$archive.Dispose()}
 } catch {return $false}
}
function Select-TakeoutFromPicker {
 $picker=New-Object System.Windows.Forms.OpenFileDialog
 $picker.Title='Choose your Google Takeout ZIP'
 $picker.Filter='ZIP archives (*.zip)|*.zip'
 $dl=Join-Path $env:USERPROFILE 'Downloads'
 if(Test-Path $dl){$picker.InitialDirectory=$dl}
 try {
  if($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){return}
  if(-not (Test-TakeoutZip $picker.FileName)){Alert 'This ZIP does not appear to contain a recognized YouTube Music library CSV. Select a different Google Takeout ZIP.';return}
  Start-WizardAnalysis $picker.FileName
 } finally {$picker.Dispose()}
}
$script:wizardMain.Add_Click({
 switch($script:wizardStage){
  0 {Set-WizardStage 1}
  1 {Start-Process 'https://takeout.google.com/settings/takeout/custom/youtube';Set-WizardStage 2}
  2 {
   $dl=Join-Path $env:USERPROFILE 'Downloads'
   $files=@(Get-ChildItem -LiteralPath $dl -Filter 'takeout*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
   $found=$false
   foreach($candidate in $files){
    if(-not (Test-TakeoutZip $candidate.FullName)){continue}
    $found=$true
    $ans=[System.Windows.Forms.MessageBox]::Show(('Use this Takeout archive?'+[Environment]::NewLine+$candidate.FullName),'Takeout archive found',[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Question)
    if($ans -eq [System.Windows.Forms.DialogResult]::Yes){
     Start-WizardAnalysis $candidate.FullName
     return
    }
    break
   }
   if(-not $found){Info 'No recognizable Takeout music archive found in Downloads. Opening file picker.'}
   Select-TakeoutFromPicker
  }
  4 {Show-LidarrConnection}
  5 {
   if(-not $script:lidarrFound){Show-LidarrConnection;return}
   $url=$script:wizardAddress.Text.Trim().TrimEnd('/')
   $key=$script:wizardKey.Text.Trim()
   if(-not $key){Alert 'Click Get API Key, then paste your key before connecting.';return}
   try{
    $list=@(Invoke-RestMethod -Uri ($url+'/api/v1/artist') -Headers @{'X-Api-Key'=$key} -TimeoutSec 20 -ErrorAction Stop)
    $script:sessionUrl=$url;$script:sessionKey=$key;$script:installedArtists=$list
    $script:urlBox.Text=$url;$script:keyBox.Text=$key
    Set-WizardStage 6
    $script:connectionResult.Text=('Connected successfully. {0} Lidarr artist entries available for a read-only comparison. Advanced guided matching is still in development.' -f $list.Count)
   }catch{Alert ('Unable to authenticate with Lidarr: '+$_.Exception.Message)}
  }
  6 {$script:wizard.Hide();Info 'Read-only comparison workspace opened. Advanced guided matching is not yet implemented.'}
 }
})
$script:wizardNext.Add_Click({switch($script:wizardStage){2 {Set-WizardStage 1} 4 {Set-WizardStage 2} 5 {Set-WizardStage 4} 6 {Show-LidarrConnection} default {Set-WizardStage 0}}})
$script:wizardSkip.Add_Click({Select-TakeoutFromPicker})
Set-WizardStage 0
$script:form.Add_Shown({Layout-Wizard})
if($InputPath -and (Test-Path -LiteralPath $InputPath)){Start-WizardAnalysis $InputPath}

[void][System.Windows.Forms.Application]::Run($script:form)
