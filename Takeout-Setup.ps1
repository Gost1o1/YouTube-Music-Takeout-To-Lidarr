#Requires -Version 5.1
# Google Takeout helper. Only guides the user and discovers local archives.
# No browser automation, Google account access, downloads, or deleting user files.
function Get-TakeoutDownloadsDirectory {
    $candidates = @(
        (Join-Path $env:USERPROFILE 'Downloads'),
        (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Container)) { return $candidate }
    }
    return (Join-Path $env:USERPROFILE 'Downloads')
}
function Get-TakeoutCandidates {
    $downloads = Get-TakeoutDownloadsDirectory
    if (-not (Test-Path -LiteralPath $downloads -PathType Container)) { return @() }
    $items = @(Get-ChildItem -LiteralPath $downloads -Force -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.PSIsContainer -and $_.Name -match '(?i)takeout|youtube') -or
            (-not $_.PSIsContainer -and $_.Extension -ieq '.zip' -and $_.Name -match '(?i)takeout|youtube|google')
        } | Sort-Object LastWriteTime -Descending | Select-Object -First 12)
    return $items
}
function Resolve-TakeoutInput {
    param([string]$ProvidedPath, [string]$OutputRoot, [switch]$NoPrompt)
    $statePath = Join-Path $OutputRoot 'last-takeout-path.json'
    if ($ProvidedPath) {
        $p = $ProvidedPath.Trim().Trim('"')
        if (-not (Test-Path -LiteralPath $p)) { throw "InputPath not found: $p" }
        return (Resolve-Path -LiteralPath $p).Path
    }
    if ($NoPrompt) { throw 'Supply -InputPath when using -NoPrompt.' }
    $previous = ''
    if (Test-Path -LiteralPath $statePath) {
        try {
            $obj = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            if ($obj.Path -and (Test-Path -LiteralPath ([string]$obj.Path))) { $previous = [string]$obj.Path }
        } catch { }
    }
    while ($true) {
        Write-Host "`n=== GOOGLE TAKEOUT SETUP ===" -ForegroundColor Cyan
        Write-Host '1. Open official Google Takeout export page (default)'
        Write-Host '2. Find Takeout ZIPs and folders in Downloads'
        Write-Host '3. Paste an existing ZIP or extracted folder path'
        if ($previous) { Write-Host '4. Resume using the last selected export' }
        Write-Host 'Q. Save your place and exit; run the same command later'
        $choice = (Read-Host 'Choose [Enter=1]').Trim()
        if (-not $choice) { $choice = '1' }
        $found = ''
        switch ($choice.ToUpperInvariant()) {
            '1' {
                Write-Host "`nTakeout instructions:" -ForegroundColor Cyan
                Write-Host '  a. Select Deselect all; then select YouTube and YouTube Music.'
                Write-Host '  b. Choose the available export settings and create an export.'
                Write-Host '  c. Google may email you when the archive is ready; use the Google download link.'
                Write-Host '  d. Download the ZIP in your browser. Return here and select option 2.'
                Write-Host '  Important: Google does not provide this tool an export-request API.'
                try { Start-Process 'https://takeout.google.com/settings/takeout' } catch { Write-Warning 'Could not open browser. Visit https://takeout.google.com/settings/takeout' }
                Write-Host 'Press Enter when the file has downloaded, or type Q to leave and return later.'
                $reply = Read-Host 'Continue'
                if ($reply.Trim() -ieq 'Q') { return '' }
            }
            '2' {
                $items = @(Get-TakeoutCandidates)
                Write-Host "`nDownloads directory: $(Get-TakeoutDownloadsDirectory)"
                if ($items.Count -eq 0) {
                    Write-Warning 'No likely Takeout ZIP or folder was found. You can paste its path with option 3.'
                    break
                }
                for ($i=0; $i -lt $items.Count; $i++) {
                    $type = if ($items[$i].PSIsContainer) { 'FOLDER' } else { 'ZIP' }
                    Write-Host ("{0}. [{1}] {2} ({3})" -f ($i+1), $type, $items[$i].Name, $items[$i].LastWriteTime.ToString('yyyy-MM-dd HH:mm'))
                }
                $pick = (Read-Host 'Enter number to select (Enter to return)').Trim()
                $number = 0
                if ([int]::TryParse($pick, [ref]$number) -and $number -ge 1 -and $number -le $items.Count) {
                    $found = $items[$number-1].FullName
                }
            }
            '3' {
                $candidate = (Read-Host 'Paste the full ZIP or folder path').Trim().Trim('"')
                if ($candidate -and (Test-Path -LiteralPath $candidate)) { $found = (Resolve-Path -LiteralPath $candidate).Path }
                else { Write-Warning 'File/folder not found. You can try again.' }
            }
            '4' {
                if ($previous) { $found = $previous }
                else { Write-Warning 'No saved export is available.' }
            }
            'Q' { return '' }
            default { Write-Warning 'Choose one of the displayed options.' }
        }
        if ($found) {
            if (-not ((Test-Path -LiteralPath $found -PathType Container) -or
                ((Test-Path -LiteralPath $found -PathType Leaf) -and $found -match '(?i)\.zip$'))) {
                Write-Warning 'Choose an extracted folder or a ZIP file.'
                continue
            }
            Write-Host "`nSelected: $found"
            $answer = (Read-Host 'Use this archive? [Y/N]').Trim()
            if ($answer -ine 'Y') { continue }
            # Only the selected path is remembered. No credentials or Takeout content saved here.
            @{ Path=$found; SavedAt=(Get-Date).ToString('o') } | ConvertTo-Json |
                Set-Content -LiteralPath $statePath -Encoding UTF8
            return $found
        }
    }
}
