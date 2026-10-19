#Requires -Version 5.1
[CmdletBinding()]
param([string]$InputPath='')
$ErrorActionPreference='Stop'
$hostScript=Join-Path $PSScriptRoot 'Desktop-Host.ps1'
if(-not (Test-Path -LiteralPath $hostScript)){throw 'Desktop-Host.ps1 is missing.'}
$argsList=@('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',('"'+$hostScript+'"'))
if($InputPath){$argsList+=@('-InputPath',('"'+$InputPath+'"'))}
$proc=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $argsList -WindowStyle Hidden -PassThru -ErrorAction Stop
Start-Sleep -Milliseconds 1500
$proc.Refresh()
if($proc.HasExited -and $proc.ExitCode -ne 0){throw 'Desktop app failed to start. See desktop-startup-error.log in AppData\\Local\\YouTubeMusicTakeoutToLidarr.'}
Write-Host 'Desktop app process started. You can close this PowerShell window.'
