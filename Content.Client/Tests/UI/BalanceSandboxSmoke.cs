using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.Tests.UI;

/// <summary>Real client/UDP acceptance of the isolated editor fixture and changed skill parameters.</summary>
public partial class BalanceSandboxSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect = false; AddChild(world);
            var network = world.GetNode<NetworkClient>("NetworkClient");
            ProgressionState? progression = null; ProfessionState? profession = null; AbilityLoadout? loadout = null;
            var damage = 0d; var monsters = 0;
            network.ProgressionReceived += value => progression = value;
            network.ProfessionReceived += value => profession = value;
            network.AbilityLoadoutReceived += value => loadout = value;
            network.AbilityHitReceived += hit => damage += hit.Damage;
            network.CombatStateReceived += value => { if (value.Kind == CombatEntityKind.Monster) monsters++; };
            network.ConnectToServer();
            await Wait(() => progression is not null && profession is not null && loadout is not null && monsters > 0, "sandbox baseline");
            if (progression!.Value.Level != 15 || progression.Value.Stats[0] != 25 || profession!.Value.ActiveId != 2)
                throw new Exception("Sandbox build was not restored from the fixture.");
            var thrust = loadout!.Value.Abilities.Single(a => a.Id == 20);
            if (thrust.CooldownSeconds != 3 || !loadout.Value.Abilities.Any(a => a.Form == AbilityForm.Dash && a.Range == 20))
                throw new Exception("Changed balance or profession dash was not received.");
            var learnedThrust = progression.Value.Skills.Single(s => s.Id == 20);
            if (learnedThrust.Level != 2 || learnedThrust.NextPractice != 11 || learnedThrust.Practice != 0 || learnedThrust.Slot != 1)
                throw new Exception("Individual skill curve, test skill level or Q slot was not received.");
            var player = world.GetChildren().OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            if (System.Numerics.Vector2.Distance(player.PredictedPosition, new(15, -9)) > .3) throw new Exception("Unexpected arena spawn.");
            if (!player.MoveTo(new(15, -12))) throw new Exception("Arena route unavailable.");
            await Wait(() => System.Numerics.Vector2.Distance(player.PredictedPosition, new(15, -12)) < .3, "approach dummy");
            network.SendAbility(new(1, network.LatestServerTick, 20, new(0, -1)));
            await Wait(() => damage > 0, "authoritative edited thrust hit");
            if (damage < 70) throw new Exception("Edited thrust damage did not apply.");
            await Wait(() => progression.Value.Skills.Single(s => s.Id == 20).Practice == 2, "individual practice award");
            world.QueueFree(); await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"BALANCE_GAME_OK: level 15, skill level 2, threshold 11, practice award 2, STR 25, Swordsman, 20 m dash, cooldown 3, damage {damage:0.0}, live enemy."); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Wait(Func<bool> predicate, string stage)
    {
        var deadline = Time.GetTicksMsec() + 15000;
        while (!predicate() && Time.GetTicksMsec() < deadline) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!predicate()) throw new TimeoutException(stage);
    }
}
