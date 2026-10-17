[CmdletBinding()]
param(
    [string]$GodotPath = 'D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe',
    [int]$TimeoutSeconds = 90,
    [switch]$PhasedBasics,
    [ValidateSet('Bow','Staff')][string]$RangedWeapon,
    [string]$SurfacePackagePath,
    [switch]$WeaponEquipment,
    [ValidateSet(20,30)][int]$TickRate = 20
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $workspace ".artifacts/combat-input/$runId"
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$probe = [System.Net.Sockets.UdpClient]::new(0)
$port = $probe.Client.LocalEndPoint.Port
$probe.Dispose()
function Start-HiddenProcess([string]$Executable, [string[]]$ProcessArguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $workspace
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $ProcessArguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['DOTNET_ENVIRONMENT'] = 'Development'
    $process = [System.Diagnostics.Process]::Start($start)
    return @{ Process=$process; Output=$process.StandardOutput.ReadToEndAsync(); Error=$process.StandardError.ReadToEndAsync() }
}
$server = $null
$client = $null
try {
    $testContent = Get-Content -LiteralPath (Join-Path $workspace 'Content.Server/Data/prototype.json') -Raw | ConvertFrom-Json -AsHashtable
    # -PhasedBasics enables extended client assertions against the shipped weapon definitions.
    $projectile = $testContent.abilities | Where-Object { $_.id -eq 'test_projectile' }
    $projectile.maxCharges = 3
    $projectile.cooldownSeconds = 5
    # Explicit fixture for buffer replacement: the running Cast accepts Esc/defense, but not another skill.
    $projectile.action.preparation.cancel = 'Voluntary, Block, Parry, Dodge'
    $support = $testContent.abilities | Where-Object { $_.id -eq 'test_ground_area' }
    $support.contactDisposition = 'Support'
    $support.power = 0
    $support.magicAttackScale = 0
    $support.effects = @(@{ operation='Heal'; trigger='Contact'; target='HitTarget'; power=@{ flat=10 } })
    $channel = $support | ConvertTo-Json -Depth 64 | ConvertFrom-Json -AsHashtable
    $channel.id = 'smoke_channel'
    $channel.networkId = 600
    $channel.manaCost = 0
    $channel.staminaCost = 0
    $channel.cooldownSeconds = 5
    $channel.action.form = 'Channel'
    $channel.action.preparation.seconds = 0
    $channel.action.active = @{ cancel='Voluntary'; movementMultiplier=0 }
    $channel.channel = @{ maximumSeconds=$null; intervalSeconds=0.1; intervalSpeed='None'; pulseAtStart=$false;
        manaPerSecond=2; staminaPerSecond=0; cooldownStart='Completion'; damageCalculation='Dynamic' }
    $channel.effects = @(@{ operation='Heal'; trigger='Contact'; target='HitTarget'; power=@{ flat=1 } })
    $testContent.abilities += $channel
    $exhaustedChannel = $channel | ConvertTo-Json -Depth 64 | ConvertFrom-Json -AsHashtable
    $exhaustedChannel.id = 'smoke_channel_exhaustion'
    $exhaustedChannel.networkId = 601
    $exhaustedChannel.channel.manaPerSecond = 100000
    $testContent.abilities += $exhaustedChannel
    $charged = $support | ConvertTo-Json -Depth 64 | ConvertFrom-Json -AsHashtable
    $charged.id = 'smoke_charged_hold'
    $charged.networkId = 602
    $charged.manaCost = 4
    $charged.cooldownSeconds = 5
    $charged.action.form = 'Charged'
    $charged.action.preparation = @{ seconds=0; canTurn=$true; cancel='Voluntary, Skill, Block, Parry, Dodge' }
    $charged.action.active = @{ seconds=0 }
    $charged.charge = @{ fullSeconds=0.5; speed='None'; atFull='Hold'; startPower=1; fullPower=3 }
    $testContent.abilities += $charged
    $automaticCharge = $charged | ConvertTo-Json -Depth 64 | ConvertFrom-Json -AsHashtable
    $automaticCharge.id = 'smoke_charged_auto'
    $automaticCharge.networkId = 603
    $automaticCharge.charge.atFull = 'Release'
    $testContent.abilities += $automaticCharge
    $held = $channel | ConvertTo-Json -Depth 64 | ConvertFrom-Json -AsHashtable
    $held.id = 'smoke_hold'
    $held.networkId = 604
    $held.action.form = 'Hold'
    $held.action.preparation.cancel = 'Voluntary, Skill, Block, Parry, Dodge'
    $held.action.active.canTurn = $true
    $testContent.abilities += $held
    $toggle = $channel | ConvertTo-Json -Depth 64 | ConvertFrom-Json -AsHashtable
    $toggle.id = 'smoke_toggle'
    $toggle.networkId = 605
    $toggle.action.form = 'Toggle'
    $toggle.action.preparation.cancel = 'Voluntary, Skill, Block, Parry, Dodge'
    $toggle.action.active.movementMultiplier = 1
    $toggle.action.active.canTurn = $false
    $toggle.toggle = @{ occupiesAction=$false; stopOn='None' }
    $testContent.abilities += $toggle
    $testContent.abilities += @{ id='smoke_passive'; networkId=610; kind='Passive'; power=0; manaCost=0; cooldownSeconds=0;
        action=@{ form='Passive'; category='Skill' }; range=0; radius=0; speed=0; magicAttackScale=0;
        passive=@{ modifiers=@{ magicDefense=1 } } }
    $adventurer = $testContent.creatures | Where-Object { $_.id -eq 'test_adventurer' }
    $adventurer.abilityIds += 'smoke_channel'
    $adventurer.abilityIds += 'smoke_channel_exhaustion'
    $adventurer.abilityIds += 'smoke_charged_hold'
    $adventurer.abilityIds += 'smoke_charged_auto'
    $adventurer.abilityIds += 'smoke_hold'
    $adventurer.abilityIds += 'smoke_toggle'
    $adventurer.abilityIds += 'smoke_passive'
    $adventurer.baseHealthRecovery = 0
    if ($RangedWeapon) {
        # Only the isolated fixture changes its starter item; production content is untouched.
        $rangedItem = $testContent.items | Where-Object { $_.id -eq 'test_sword_item' }
        $rangedItem.weaponId = if ($RangedWeapon -eq 'Bow') { 'training_bow' } else { 'training_staff' }
    }
    if ($WeaponEquipment) {
        if ($RangedWeapon) { throw 'Choose either -RangedWeapon or -WeaponEquipment.' }
        $adventurer.starterItemIds += @('training_daggers_item','training_twohand_item','training_shield')
    }
    $testContent.balance.healthRecovery.vitality = 0
    $contentPath = Join-Path $runDirectory 'combat-content.json'
    $testContent | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $contentPath -Encoding utf8NoBOM
    $serverArguments = @(
        (Join-Path $workspace 'Content.Server/bin/Debug/net10.0/Content.Server.dll'),
        "--Server:Port=$port", "--Server:TickRate=$TickRate", "--Balance:ContentPath=$contentPath", '--Server:StartingRegion=linvel_gardens', '--RegionExports:AutoExport=false',
        '--RegionExports:VerifySources=false', '--Persistence:Provider=Sqlite',
        "--Persistence:SqlitePath=$runDirectory/characters.db", '--Npc:Enabled=false', '--Boss:Actor:Enabled=false')
    if ($SurfacePackagePath) {
        $serverArguments += "--RegionExports:SurfacePackagePath=$((Resolve-Path -LiteralPath $SurfacePackagePath).Path)"
    }
    $server = Start-HiddenProcess 'dotnet' $serverArguments
    $serverDeadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    while (!$ready -and [DateTime]::UtcNow -lt $serverDeadline) {
        if ($server.Process.HasExited) { throw 'Isolated server failed during startup.' }
        $ready = @([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners() | Where-Object Port -eq $port).Count -gt 0
        if (!$ready) { Start-Sleep -Milliseconds 200 }
    }
    if (!$ready) { throw 'Isolated server did not open its UDP port.' }
    $smokeScene = if ($WeaponEquipment) { 'res://Tests/UI/WeaponEquipmentSmoke.tscn' } elseif ($RangedWeapon) { 'res://Tests/UI/RangedInputSmoke.tscn' } else { 'res://Tests/UI/CombatInputSmoke.tscn' }
    $combatClientArguments = @('--headless', '--path', (Join-Path $workspace 'Content.Client'),
        $smokeScene, '--', "--server-port=$port", ('--identity=gdd01-' + $runId.Substring(0,12)))
    if ($RangedWeapon) { $combatClientArguments += "--ranged-basic=$RangedWeapon" }
    if ($PhasedBasics) { $combatClientArguments += '--phased-basics' }
    $client = Start-HiddenProcess $GodotPath $combatClientArguments
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!$client.Process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if ($server.Process.HasExited) { throw 'Isolated server exited before the client.' }
        Start-Sleep -Milliseconds 200
    }
    if (!$client.Process.HasExited) { throw 'Combat input smoke timed out.' }
    $clientOutput = $client.Output.GetAwaiter().GetResult()
    if ($client.Process.ExitCode -ne 0 -or !$clientOutput.Contains('COMBAT_INPUT_OK:')) { throw 'Combat input smoke failed; inspect the run logs.' }
    if ($PhasedBasics -and !$RangedWeapon -and !$WeaponEquipment -and !$clientOutput.Contains('BASIC_PHASES_CONFIRMED:')) { throw 'Phased basic lifecycle was not observed.' }
    if ($PhasedBasics -and !$RangedWeapon -and !$WeaponEquipment -and !$clientOutput.Contains('BASIC_CONTROL_CONFIRMED:')) { throw 'Owned basic controls were not observed.' }
    if (!$RangedWeapon -and !$WeaponEquipment -and !$clientOutput.Contains('DODGE_CONFIRMED:')) { throw 'Approved Dodge input and profile were not observed.' }
    if ($RangedWeapon -and !$clientOutput.Contains('RANGED_BASIC_INPUT_OK:')) { throw 'Ranged basic input was not observed.' }
    if ($WeaponEquipment -and !$clientOutput.Contains('WEAPON_EQUIPMENT_OK:')) { throw 'Weapon equipment input was not observed.' }
    Write-Output 'COMBAT_INPUT_OK'
}
finally {
    foreach ($entry in @(@{ Name='client'; Run=$client }, @{ Name='server'; Run=$server })) {
        if ($null -eq $entry.Run) { continue }
        if (!$entry.Run.Process.HasExited) { $entry.Run.Process.Kill($true) }
        $entry.Run.Process.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $runDirectory ($entry.Name+'.log')), $entry.Run.Output.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $runDirectory ($entry.Name+'.err.log')), $entry.Run.Error.GetAwaiter().GetResult())
        $entry.Run.Process.Dispose()
    }
    Write-Output "Run logs: $runDirectory"
}
