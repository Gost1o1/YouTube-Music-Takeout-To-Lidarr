#Requires -Version 5.1
param([string]$InputPath='')
$ErrorActionPreference='Stop'
try {
 & (Join-Path $PSScriptRoot 'Desktop.ps1') -InputPath $InputPath
} catch {
 $message=($_ | Out-String)
 $folder=Join-Path $env:LOCALAPPDATA 'YouTubeMusicTakeoutToLidarr'
 New-Item -ItemType Directory -Path $folder -Force | Out-Null
 $log=Join-Path $folder 'desktop-startup-error.log'
 $message | Set-Content -LiteralPath $log -Encoding UTF8
 try {Add-Type -AssemblyName System.Windows.Forms;[System.Windows.Forms.MessageBox]::Show(('Desktop startup failed. Details: '+$log+'`n'+$message),'YouTube Music Takeout to Lidarr')|Out-Null} catch {}
 exit 1
}
