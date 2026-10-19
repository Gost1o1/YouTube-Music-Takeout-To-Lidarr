#Requires -Version 5.1
# The familiar one-command launcher calls this entrypoint.
# Start an independent STA process so this console can close immediately.
[CmdletBinding()]
param([string]$InputPath='')
$ErrorActionPreference='Stop'
$ui=Join-Path $PSScriptRoot 'Desktop.ps1'
if(-not (Test-Path -LiteralPath $ui)){throw 'Desktop.ps1 is missing from this release.'}
$argsList=@('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',('"'+$ui+'"'))
if($InputPath){$argsList+=@('-InputPath',('"'+$InputPath+'"'))}
Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $argsList -WindowStyle Hidden -ErrorAction Stop | Out-Null
Write-Host 'Desktop app launched. This PowerShell window can now close.'
