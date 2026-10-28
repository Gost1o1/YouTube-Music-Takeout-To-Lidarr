# One-time repository organization helper. Requires Git for Windows.
# Example:
# powershell -ExecutionPolicy Bypass -File .\tools\Apply-GitHub-Cleanup.ps1 -RepositoryPath 'C:\path\to\repo'
# Does not commit, push, delete unrelated files, or publish a GitHub Release.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryPath
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryPath).Path

if ($packageRoot.TrimEnd('\', '/') -eq $repositoryRoot.TrimEnd('\', '/')) {
    throw 'The extracted source package and the GitHub clone must be separate folders.'
}
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
    throw 'That folder is not a GitHub clone (.git not found). Clone the repository with GitHub Desktop first.'
}
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'YouTubeMusicToLidarr.csproj'))) {
    throw 'The selected clone does not contain YouTubeMusicToLidarr.csproj.'
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is not available in this PowerShell window. Install Git for Windows or open a Git-enabled shell.'
}

$before = @(& git -C $repositoryRoot status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Git status; no files were changed.' }
if ($before.Count -ne 0) {
    throw 'Your GitHub clone has uncommitted/untracked changes. Commit or preserve them before running this cleanup.'
}

$moves = New-Object 'System.Collections.Generic.List[object]'
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $packageRoot 'src') -Filter '*.cs' -File)) {
    $moves.Add(@{ Old = $file.Name; New = (Join-Path 'src' $file.Name) })
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $packageRoot 'docs/history') -File)) {
    if ($file.Name -ne 'README.md' -and $file.Name -ne 'README-legacy-v0.9.1.15.4.md') {
        $moves.Add(@{ Old = $file.Name; New = (Join-Path 'docs/history' $file.Name) })
    }
}
$moves.Add(@{ Old = 'TESTING.md'; New = 'docs/developer/TESTING.md' })
$moves.Add(@{ Old = 'IMPLEMENTATION-PLAN.md'; New = 'docs/developer/IMPLEMENTATION-PLAN.md' })

# Check every potential conflict before modifying anything.
foreach ($move in $moves) {
    $oldPath = Join-Path $repositoryRoot $move.Old
    $newPath = Join-Path $repositoryRoot $move.New
    if ((Test-Path -LiteralPath $oldPath) -and (Test-Path -LiteralPath $newPath)) {
        throw "Both old and new paths exist: $($move.Old) and $($move.New). No cleanup started."
    }
}

$movedCount = 0
foreach ($move in $moves) {
    $oldPath = Join-Path $repositoryRoot $move.Old
    if (-not (Test-Path -LiteralPath $oldPath)) { continue }
    $newPath = Join-Path $repositoryRoot $move.New
    $parent = Split-Path -Parent $newPath
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Move-Item -LiteralPath $oldPath -Destination $newPath
    $movedCount++
}

$copiedCount = 0
foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force)) {
    $relative = $file.FullName.Substring($packageRoot.Length).TrimStart('\', '/')
    $dest = Join-Path $repositoryRoot $relative
    $parent = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    $copiedCount++
}

Write-Host "`nRepository cleanup prepared." -ForegroundColor Green
Write-Host "Moved $movedCount existing repository files into src/docs."
Write-Host "Copied $copiedCount files from the new source package."
Write-Host "Any unknown/unrelated files were preserved. No commit, push, or release was made."
Write-Host "`nNext: Open GitHub Desktop, review Changes, commit, and Push origin."
