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
}
function New-Button($text,$x,$y,$w,$handler){
 $b=New-Object System.Windows.Forms.Button
 $b.Text=$text;$b.Location=New-Object System.Drawing.Point($x,$y);$b.Size=New-Object System.Drawing.Size($w,35)
 $b.Add_Click($handler);$script:form.Controls.Add($b);return $b
}
$script:form=New-Object System.Windows.Forms.Form
$script:form.Text='YouTube Music Takeout to Lidarr  |  v0.5.3'
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
 $script:currentReport='';$script:started=$true
 $script:progress.Style='Marquee';$script:progress.MarqueeAnimationSpeed=28
 $script:grid.Rows.Clear();$script:countLabel.Text='Analyzing archive…'
 $script:engineOutput=Join-Path $root ('engine-'+[guid]::NewGuid().ToString('N')+'.out.log')
 $script:engineError=Join-Path $root ('engine-'+[guid]::NewGuid().ToString('N')+'.err.log')
 $engine=Join-Path $PSScriptRoot 'Takeout-Engine.ps1'
 $cmd='-NoProfile -ExecutionPolicy Bypass -File "'+$engine+'" -InputPath "'+$path+'" -OutputRoot "'+$root+'" -NoPrompt'
 try {$script:engineProcess=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $cmd -WindowStyle Hidden -RedirectStandardOutput $script:engineOutput -RedirectStandardError $script:engineError -PassThru -ErrorAction Stop;Info 'Scanning the full Takeout archive in the background…'}
 catch {$script:progress.Style='Blocks';Alert $_.Exception.Message}
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
 if($script:engineProcess.ExitCode -ne 0 -or $errText){Info 'Takeout analysis ended with an error. See log in the report folder.';Alert (($errText+' '+$logText).Trim().Substring(0,[math]::Min(900,($errText+' '+$logText).Trim().Length)));return}
 $runs=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {Test-Path (Join-Path $_.FullName 'tracks.csv')} | Sort-Object LastWriteTime -Descending)
 if(-not $runs.Count){Alert 'No tracks.csv was created. Check the output logs.';return}
 $script:currentReport=$runs[0].FullName
 try{Read-TrackFile}catch{Alert ('Could not load generated report: '+$_.Exception.Message)}
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
$script:wizardText.Size=New-Object System.Drawing.Size(820,96)
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

function Layout-Wizard {
 $panelWidth=$script:wizard.ClientSize.Width
 $centerX=[int](($panelWidth-880)/2)
 $textX=[int](($panelWidth-820)/2)
 $buttonX=[int](($panelWidth-360)/2)
 $progX=[int](($panelWidth-260)/2)
 $script:wizardProgress.Location=New-Object System.Drawing.Point($progX,46)
 $script:wizardHeading.Location=New-Object System.Drawing.Point($centerX,98)
 $script:wizardText.Location=New-Object System.Drawing.Point($textX,172)
 $script:wizardMain.Location=New-Object System.Drawing.Point($buttonX,306)
 if($script:wizardNext.Visible){$script:wizardNext.Location=New-Object System.Drawing.Point($buttonX,370)}
 if($script:wizardSkip.Visible){
  $y=if($script:wizardNext.Visible){434}else{370}
  $script:wizardSkip.Location=New-Object System.Drawing.Point($buttonX,$y)
 }
}
$script:wizard.Add_Resize({Layout-Wizard})

function Set-WizardStage([int]$stage){
 $script:wizardStage=$stage
 switch($stage){
  0 {
   $script:wizardProgress.Value=33
   $script:wizardHeading.Text='Where is your music stored?'
   $script:wizardText.Text='Choose the Google account that contains your YouTube Music library, or continue with a Takeout ZIP you already downloaded.'
   $script:wizardMain.Text='Choose Google Account'
   $script:wizardNext.Visible=$false
   $script:wizardSkip.Visible=$true
   $script:wizardSkip.Text='Use Already Downloaded YouTube Music Data'
  }
  1 {
   $script:wizardProgress.Value=66
   $script:wizardHeading.Text='Request your YouTube Music export'
   $script:wizardText.Text='Google Takeout will open with YouTube and YouTube Music preselected. On Google''s page keep these settings: send download link via email, export once, ZIP, and 50 GB. Then click Create export.'
   $script:wizardMain.Text='Open Google Takeout'
   $script:wizardNext.Visible=$true
   $script:wizardNext.Text='Export Requested - Continue'
   $script:wizardSkip.Visible=$false
  }
  2 {
   $script:wizardProgress.Value=100
   $script:wizardHeading.Text='Wait for Google to prepare your export'
   $script:wizardText.Text='When Google emails that your export is ready, download the ZIP. Then continue here so the app can locate the newest Takeout file or let you choose it manually.'
   $script:wizardMain.Text='Find the Ready Email in Gmail'
   $script:wizardNext.Visible=$true
   $script:wizardNext.Text='Find My Downloaded ZIP'
   $script:wizardSkip.Visible=$false
  }
 }
 Layout-Wizard
}
$script:wizardMain.Add_Click({
 switch($script:wizardStage){
  0 {Start-Process 'https://accounts.google.com/AccountChooser'}
  1 {Start-Process 'https://takeout.google.com/settings/takeout/custom/youtube'}
  2 {Start-Process 'https://mail.google.com/mail/u/0/#search/from%3A(noreply%40google.com)+subject%3A%22Your+Google+data+is+ready+to+download%22'}
 }
})
$script:wizardNext.Add_Click({
 if($script:wizardStage -lt 2){Set-WizardStage ($script:wizardStage+1);return}
 $dl=Join-Path $env:USERPROFILE 'Downloads'
 $files=@(Get-ChildItem -LiteralPath $dl -Filter 'takeout*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
 if($files.Count -eq 0){
  $picker=New-Object System.Windows.Forms.OpenFileDialog
  $picker.Title='Choose the Google Takeout ZIP';$picker.Filter='ZIP archives (*.zip)|*.zip|All files (*.*)|*.*';$picker.InitialDirectory=$dl
  if($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$script:sourceBox.Text=$picker.FileName;$script:wizard.Hide();Info 'Takeout archive selected. Click Analyze Takeout.'}
  $picker.Dispose();return
 }
 $candidate=$files[0]
 $ans=[System.Windows.Forms.MessageBox]::Show(('Use this Takeout archive?'+[Environment]::NewLine+$candidate.FullName),'Confirm Takeout ZIP',[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Question)
 if($ans -eq [System.Windows.Forms.DialogResult]::Yes){$script:sourceBox.Text=$candidate.FullName;$script:wizard.Hide();Info 'Takeout archive selected. Click Analyze Takeout.'}
 else {
  $picker=New-Object System.Windows.Forms.OpenFileDialog;$picker.Filter='ZIP archives (*.zip)|*.zip|All files (*.*)|*.*';$picker.InitialDirectory=$dl
  if($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$script:sourceBox.Text=$picker.FileName;$script:wizard.Hide();Info 'Takeout archive selected. Click Analyze Takeout.'}
  $picker.Dispose()
 }
})
$script:wizardSkip.Add_Click({
 $picker=New-Object System.Windows.Forms.OpenFileDialog
 $picker.Title='Choose an existing Google Takeout ZIP';$picker.Filter='ZIP archives (*.zip)|*.zip|All files (*.*)|*.*'
 $picker.InitialDirectory=Join-Path $env:USERPROFILE 'Downloads'
 if($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){$script:sourceBox.Text=$picker.FileName;$script:wizard.Hide();Info 'Takeout archive selected. Click Analyze Takeout.'}
 $picker.Dispose()
})
Set-WizardStage 0
if($InputPath -and (Test-Path -LiteralPath $InputPath)){$script:wizard.Hide()}

[void][System.Windows.Forms.Application]::Run($script:form)
