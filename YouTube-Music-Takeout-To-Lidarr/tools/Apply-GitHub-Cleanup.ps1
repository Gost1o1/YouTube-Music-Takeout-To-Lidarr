# One-time migration from the flat project layout into the nested repository
# layout used by Jellyfin-Franchise-Explorer. Does not commit or push changes.
# Run from a separately extracted source ZIP, never inside the GitHub clone.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryPath
)

$ErrorActionPreference = 'Stop'
$projectFolder = 'YouTube-Music-Takeout-To-Lidarr'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryPath).Path

if ($packageRoot.TrimEnd('\', '/') -eq $repositoryRoot.TrimEnd('\', '/')) {
    throw 'Extract the source ZIP into a separate folder, not into the GitHub clone.'
}
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
    throw 'The target must be a GitHub Desktop/git clone (.git not found).'
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is not available. Install Git for Windows or open a Git-enabled PowerShell shell.'
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "$projectFolder\src\Program.cs"))) {
    throw 'Source package is incomplete: the nested C# project was not found.'
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot '.github\workflows\build-windows.yml'))) {
    throw 'Source package is incomplete: the root GitHub build workflow was not found.'
}
if (-not ((Test-Path -LiteralPath (Join-Path $repositoryRoot 'YouTubeMusicToLidarr.csproj')) -or
          (Test-Path -LiteralPath (Join-Path $repositoryRoot "$projectFolder\YouTubeMusicToLidarr.csproj")))) {
    throw 'The clone does not appear to be the expected YouTube Music to Lidarr repository.'
}

$before = @(& git -C $repositoryRoot status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Could not check the GitHub clone status.' }
if ($before.Count -gt 0) {
    throw 'The GitHub clone has uncommitted changes. Commit or preserve them before organizing the repository.'
}

# All conflicts are checked BEFORE moving or copying any files. Unknown files
# are not renamed or deleted.
$oldRootItems = @('src', 'AppUI', 'docs', 'tools',
                  'YouTubeMusicToLidarr.csproj', 'BUILD-AND-RUN.ps1')
foreach ($name in $oldRootItems) {
    $old = Join-Path $repositoryRoot $name
    $new = Join-Path (Join-Path $repositoryRoot $projectFolder) $name
    if ((Test-Path -LiteralPath $old) -and (Test-Path -LiteralPath $new)) {
        throw "Both old and nested paths already exist for $name. No files have been changed."
    }
}

$nested = Join-Path $repositoryRoot $projectFolder
if (-not (Test-Path -LiteralPath $nested)) {
    New-Item -ItemType Directory -Path $nested | Out-Null
}
$moved = 0
foreach ($name in $oldRootItems) {
    $old = Join-Path $repositoryRoot $name
    if (-not (Test-Path -LiteralPath $old)) { continue }
    Move-Item -LiteralPath $old -Destination (Join-Path $nested $name)
    $moved++
}

# Copy the entire tested package: root README/license/workflows plus nested
# project source, tests and documentation. The .git directory is never copied.
$copied = 0
foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force)) {
    $relative = $file.FullName.Substring($packageRoot.Length).TrimStart('\', '/')
    if ($relative -eq '.git' -or $relative.StartsWith('.git\') -or $relative.StartsWith('.git/')) { continue }
    $destination = Join-Path $repositoryRoot $relative
    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    $copied++
}

Write-Host "Repository organization prepared: $moved old root items moved and $copied files copied." -ForegroundColor Green
Write-Host 'Review all changes in GitHub Desktop before committing and pushing.'
Write-Host 'Root .github/workflows/ remains in place. Nothing was pushed or released.'
