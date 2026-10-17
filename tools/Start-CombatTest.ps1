#requires -Version 7
[CmdletBinding()]
param(
    [ValidateRange(1,2)][int]$Clients = 1,
    [string]$GodotPath = 'D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe',
    [string]$ServerPath,
    [switch]$SkipBuild,
    [switch]$Smoke
)
$ErrorActionPreference = 'Stop'
$arenaRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$arenaId = [Guid]::NewGuid().ToString('N')
$arenaDirectory = Join-Path $arenaRoot ".artifacts/combat-test/$arenaId"
New-Item -ItemType Directory -Path $arenaDirectory -Force | Out-Null
if (!(Test-Path -LiteralPath $GodotPath -PathType Leaf)) { throw 'Set -GodotPath to the Godot .NET executable.' }
Push-Location $arenaRoot
$arenaProcesses = [Collections.Generic.List[object]]::new()
function Start-ArenaProcess([string]$Name, [string]$Executable, [string[]]$ProcessArguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $arenaRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['DOTNET_ENVIRONMENT'] = 'Development'
    foreach ($argument in $ProcessArguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $entry = @{ Name=$Name; Process=$process; Output=$process.StandardOutput.ReadToEndAsync(); Error=$process.StandardError.ReadToEndAsync() }
    $arenaProcesses.Add($entry)
    return $entry
}
try {
    if (!$SkipBuild) {
        $arenaBuild = Join-Path $arenaDirectory 'build'
        dotnet build Game.slnx -m:1 --artifacts-path $arenaBuild --ignore-failed-sources -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        if (!$ServerPath) { $ServerPath = Join-Path $arenaBuild 'bin/Content.Server/debug/Content.Server.dll' }
    }
    if (!$ServerPath) { $ServerPath = Join-Path $arenaRoot 'Content.Server/bin/Debug/net10.0/Content.Server.dll' }
    $socket = [Net.Sockets.UdpClient]::new(0)
    $port = $socket.Client.LocalEndPoint.Port
    $socket.Dispose()
    $server = Start-ArenaProcess 'server' 'dotnet' @(
        $ServerPath,
        '--Server:StartingRegion=linvel_beech', '--Server:TickRate=20', "--Server:Port=$port",
        '--CombatTest:Enabled=true', '--Persistence:Provider=Sqlite',
        "--Persistence:SqlitePath=$arenaDirectory/characters.db", '--Logging:LogLevel:Default=Information')
    Write-Host "Beech Valley / GDD 01. Fresh characters. UDP $port. Logs: $arenaDirectory"
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while (@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners() | Where-Object Port -eq $port).Count -eq 0) {
        if ($server.Process.HasExited) { throw ('Server startup failed: '+$server.Error.GetAwaiter().GetResult()) }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Server startup timed out; inspect server logs.' }
        Start-Sleep -Milliseconds 250
    }
    $native = $GodotPath -replace '_console\.exe$', '.exe'
    if (Test-Path -LiteralPath $native) { $GodotPath = $native }
    $gameClients = @()
    for ($i=1; $i -le $Clients; $i++) {
        $scene = if ($Smoke) { 'res://Tests/UI/CombatArenaSmoke.tscn' } else { 'res://Scenes/World.tscn' }
        $arguments = @('--path', (Join-Path $arenaRoot 'Content.Client'), '--resolution', '1280x720', $scene,
            '--', "--server-port=$port", "--identity=arena-$($arenaId.Substring(0,12))-$i")
        if ($Smoke) { $arguments = @('--headless') + $arguments }
        $gameClients += Start-ArenaProcess "client-$i" $GodotPath $arguments
    }
    $smokeDeadline = [DateTime]::UtcNow.AddSeconds(90)
    Write-Host 'I: equipment. K: skills/bar. F3: Echo roster. Close all game windows to stop this server.'
    while (@($gameClients | Where-Object { !$_.Process.HasExited }).Count -gt 0) {
        if ($server.Process.HasExited) { throw 'Server stopped unexpectedly.' }
        if ($Smoke -and [DateTime]::UtcNow -gt $smokeDeadline) { throw 'Arena smoke timed out.' }
        Start-Sleep -Milliseconds 250
    }
    foreach ($client in $gameClients) {
        if ($client.Process.ExitCode -ne 0) { throw 'Game client failed; inspect run logs.' }
        if ($Smoke -and !$client.Output.GetAwaiter().GetResult().Contains('COMBAT_ARENA_OK:')) { throw 'Arena smoke did not confirm setup.' }
    }
}
finally {
    foreach ($entry in $arenaProcesses) {
        if (!$entry.Process.HasExited) { $entry.Process.Kill($true) }
        $entry.Process.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $arenaDirectory ($entry.Name+'.log')), $entry.Output.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $arenaDirectory ($entry.Name+'.err.log')), $entry.Error.GetAwaiter().GetResult())
        $entry.Process.Dispose()
    }
    Pop-Location
    Write-Host "Logs: $arenaDirectory"
}
