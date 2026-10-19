#Requires -Version 5.1
[CmdletBinding()]
param([string]$InputPath='')
$ErrorActionPreference='Stop'
$hostScript=Join-Path $PSScriptRoot 'Desktop-Host.ps1'
if(-not(Test-Path -LiteralPath $hostScript)){throw 'Desktop-Host.ps1 is missing.'}
# Do not launch the GUI in a hidden child process: that prevented the window
# from appearing on some systems while incorrectly reporting success.
# Keep the host attached to the graphical application so startup failures
# remain observable and loggable.
if([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA){
 Write-Host 'Starting desktop interface in STA mode...'
 $arg=@('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',('"'+$hostScript+'"'))
 if($InputPath){$arg+=@('-InputPath',('"'+$InputPath+'"'))}
 & (Join-Path $PSHOME 'powershell.exe') @arg
 if($LASTEXITCODE -ne 0){throw ('Desktop interface exited with code '+$LASTEXITCODE)}
} else {
 & $hostScript -InputPath $InputPath
}
