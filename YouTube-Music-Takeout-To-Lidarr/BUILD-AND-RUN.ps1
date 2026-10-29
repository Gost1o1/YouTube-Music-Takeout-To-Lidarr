# YouTube Music Takeout to Lidarr v0.9.1.15.6
# One-shot Windows PowerShell: build, self-test, publish, launch.
# Run from PowerShell with:  powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1
$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not on PATH. Install .NET 8 SDK and reopen PowerShell.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    $project = 'YouTubeMusicToLidarr.csproj'
    if (-not (Test-Path -LiteralPath $project)) {
        throw "Cannot find $project next to BUILD-AND-RUN.ps1. Extract the full ZIP first."
    }

    Write-Host "`n[1/4] Building for Windows x64..." -ForegroundColor Cyan
    & dotnet build $project -c Release -r win-x64
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE). Not starting tests or application." }

    Write-Host "`n[2/4] Running application self-tests..." -ForegroundColor Cyan
    & dotnet run --project $project -c Release -r win-x64 --no-build -- --self-test
    if ($LASTEXITCODE -ne 0) { throw "Self-tests failed (exit code $LASTEXITCODE). Not publishing or launching." }

    Write-Host "`n[3/4] Publishing portable self-contained EXE..." -ForegroundColor Cyan
    & dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE). Not launching." }

    $exe = Join-Path (Get-Location).Path 'publish\YouTubeMusicToLidarr.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Publish succeeded but EXE was not found: $exe" }

    Write-Host "`n[4/4] Starting YouTube Music Takeout to Lidarr..." -ForegroundColor Green
    Write-Host 'Close any running older version first to avoid a local port conflict.'
    & $exe
}
finally {
    Pop-Location
}
