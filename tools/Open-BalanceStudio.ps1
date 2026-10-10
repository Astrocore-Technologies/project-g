param([string]$GodotPath = $env:GODOT_PATH)
# Keep this launcher ASCII-only: Windows PowerShell 5.1 may read BOM-less files as ANSI.
$ErrorActionPreference = 'Stop'
$balanceRepo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $balanceCommand = Get-Command godot -ErrorAction SilentlyContinue
    if ($balanceCommand) { $GodotPath = $balanceCommand.Source }
    else {
        $balanceDrive = [IO.Path]::GetPathRoot($balanceRepo)
        $balanceInstall = Get-ChildItem -LiteralPath $balanceDrive -Directory -Filter 'Godot*_mono*' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($balanceInstall) { $GodotPath = (Get-ChildItem -LiteralPath $balanceInstall.FullName -File -Filter '*_console.exe' | Select-Object -First 1).FullName }
    }
}
if ([string]::IsNullOrWhiteSpace($GodotPath) -or !(Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Godot .NET was not found. Set -GodotPath C:/path/Godot_console.exe or the GODOT_PATH environment variable.'
}
# The console wrapper can retain inherited handles after tools spawn child processes.
$balanceNativeGodot = $GodotPath -replace '_console\.exe$', '.exe'
if (Test-Path -LiteralPath $balanceNativeGodot -PathType Leaf) { $GodotPath = $balanceNativeGodot }
Push-Location $balanceRepo
try {
    dotnet build Game.slnx -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Fix the build errors before starting Balance Studio.' }
    & $GodotPath --path (Join-Path $balanceRepo 'tools/BalanceStudio') -- --repo=$balanceRepo
} finally { Pop-Location }
