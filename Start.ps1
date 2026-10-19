#Requires -Version 5.1
[CmdletBinding()]
param([string]$InputPath='')
$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot 'WebApp-Host.ps1'
if(-not(Test-Path -LiteralPath $script)){throw 'WebApp-Host.ps1 is missing.'}
Write-Host 'Starting YouTube Music to Lidarr v0.6.3 (leave this window open)...'
if([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA){
  & (Join-Path $PSHOME 'powershell.exe') -NoProfile -STA -ExecutionPolicy Bypass -File $script -InputPath $InputPath
} else { & $script -InputPath $InputPath }
