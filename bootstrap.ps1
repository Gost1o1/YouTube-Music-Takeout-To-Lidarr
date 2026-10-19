#Requires -Version 5.1
# Change the repository URL in the one-line install command, not in this script.
[CmdletBinding()]
param([string]$RepositoryZipUrl='', [string]$InputPath='')
$ErrorActionPreference='Stop'
if (-not $RepositoryZipUrl) { throw 'Pass -RepositoryZipUrl with a ZIP URL from your own GitHub repository.' }
if ($RepositoryZipUrl -notmatch '^https://(github\.com|codeload\.github\.com)/') { throw 'Only GitHub HTTPS ZIP URLs are accepted.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('MusicTakeout-Bootstrap-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
  $archive=Join-Path $temp 'app.zip'
  Invoke-WebRequest -Uri $RepositoryZipUrl -OutFile $archive -UseBasicParsing
  Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $temp 'app') -Force
  $start = @(Get-ChildItem -LiteralPath (Join-Path $temp 'app') -Recurse -Filter 'Start.ps1' -File | Select-Object -First 1)
  if ($start.Count -ne 1) {throw 'Could not locate Start.ps1 in the downloaded repository.'}
  & $start[0].FullName -InputPath $InputPath
} finally {
  if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
}
