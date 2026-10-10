param([string]$GodotPath = $env:GODOT_PATH, [switch]$CheckOnly, [switch]$NoBuild)
# ASCII-only for Windows PowerShell 5.1. No working database or running server is touched.
$ErrorActionPreference = 'Stop'
$exportRepo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $exportCommand = Get-Command godot -ErrorAction SilentlyContinue
    if ($exportCommand) { $GodotPath = $exportCommand.Source }
    else {
        $exportInstall = Get-ChildItem -LiteralPath ([IO.Path]::GetPathRoot($exportRepo)) -Directory -Filter 'Godot*_mono*' | Select-Object -First 1
        if ($exportInstall) { $GodotPath = (Get-ChildItem -LiteralPath $exportInstall.FullName -File -Filter '*_console.exe' | Select-Object -First 1).FullName }
    }
}
if (-not $GodotPath -or -not (Test-Path -LiteralPath $GodotPath)) { throw 'Set -GodotPath to Godot .NET 4.7.1 console executable.' }
$exportArtifacts = Join-Path $exportRepo '.artifacts/region-export'
$exportBuild = Join-Path $exportArtifacts 'build'
$exportCandidate = Join-Path $exportArtifacts (([guid]::NewGuid().ToString('N')) + '.candidate.json')
$exportPublished = Join-Path $exportRepo 'Content.Server/Data/Regions/region-package.json'
$exportServer = Join-Path $exportBuild 'bin/Content.Server/debug/Content.Server.dll'
[IO.Directory]::CreateDirectory($exportArtifacts) | Out-Null
$exportLock = $null
Push-Location $exportRepo
try {
    $exportLock = [IO.File]::Open((Join-Path $exportArtifacts 'publish.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if (-not $NoBuild) {
        & dotnet build Content.Client/Project-G.csproj -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
        & dotnet build Content.Server/Content.Server.csproj --artifacts-path $exportBuild -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Isolated server build failed.' }
    }
    if (-not (Test-Path -LiteralPath $exportServer)) { throw 'Run without -NoBuild first.' }
    & $GodotPath --headless --path (Join-Path $exportRepo 'Content.Client') --log-file (Join-Path $exportArtifacts 'godot.log') res://Tools/Regions/ExportRegions.tscn -- "--output=$exportCandidate"
    if ($LASTEXITCODE -ne 0) { throw 'Scene export failed; published package unchanged.' }
    & dotnet $exportServer --validate-content "--RegionExports:PackagePath=$exportCandidate"
    if ($LASTEXITCODE -ne 0) { throw 'Server graph/content validation failed; published package unchanged.' }
    $exportBytes = [IO.File]::ReadAllBytes($exportCandidate)
    if ($CheckOnly) {
        if (-not (Test-Path -LiteralPath $exportPublished) -or
            [Convert]::ToBase64String($exportBytes) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($exportPublished))) {
            throw 'Published export is stale. Run tools/Export-Regions.ps1.'
        }
        Write-Host 'REGION_EXPORT_CURRENT'
    }
    else {
        # The public artifact is immutable by input revision, and exists before the server snapshot switches.
        # Built-in .NET works in both Windows PowerShell and pwsh, without optional module discovery.
        $exportHasher = [Security.Cryptography.SHA256]::Create()
        try { $exportHash = [BitConverter]::ToString($exportHasher.ComputeHash($exportBytes)).Replace('-', '') }
        finally { $exportHasher.Dispose() }
        $exportPublic = Join-Path $exportArtifacts 'client'
        [IO.Directory]::CreateDirectory($exportPublic) | Out-Null
        $exportArtifact = Join-Path $exportPublic ($exportHash + '.pck')
        if (-not (Test-Path -LiteralPath $exportArtifact)) { [IO.File]::Copy($exportCandidate + '.pck', $exportArtifact, $false) }
        # Replace one file on the same volume. Readers see either complete version, never a mixed set.
        $exportTemporary = $exportPublished + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
        $exportBackup = $exportTemporary + '.previous'
        try {
            $exportStream = [IO.File]::Open($exportTemporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $exportStream.Write($exportBytes, 0, $exportBytes.Length); $exportStream.Flush($true) } finally { $exportStream.Dispose() }
            if (Test-Path -LiteralPath $exportPublished) { [IO.File]::Replace($exportTemporary, $exportPublished, $exportBackup) }
            else { [IO.File]::Move($exportTemporary, $exportPublished) }
        }
        finally {
            if (Test-Path -LiteralPath $exportTemporary) { Remove-Item -LiteralPath $exportTemporary }
            if (Test-Path -LiteralPath $exportBackup) { Remove-Item -LiteralPath $exportBackup }
        }
        Write-Host 'REGION_EXPORT_PUBLISHED'
    }
}
finally {
    if (Test-Path -LiteralPath $exportCandidate) { Remove-Item -LiteralPath $exportCandidate }
    if (Test-Path -LiteralPath ($exportCandidate + '.pck')) { Remove-Item -LiteralPath ($exportCandidate + '.pck') }
    if ($exportLock) { $exportLock.Dispose() }
    Pop-Location
}
