using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Gameplay;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Native scene, real login and server-issued manual QA fixture.</summary>
public partial class CombatArenaSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            world.AutoConnect = false; AddChild(world);
            var network = world.GetNode<NetworkClient>("NetworkClient");
            PlayerSpawn? spawn = null; InventoryState inventory = default; ProgressionState progression = default;
            var actors = new HashSet<NetworkEntityId>();
            var bossSeen = false;
            network.PlayerSpawned += value => { if (value.PlayerId == network.LocalPlayerId) spawn = value; };
            network.InventoryReceived += value => inventory = value;
            network.ProgressionReceived += value => progression = value;
            network.CombatStateReceived += value => { if (value.Kind is CombatEntityKind.Monster or CombatEntityKind.Boss) actors.Add(value.EntityId); if(value.Kind==CombatEntityKind.Boss) bossSeen=true; };
            network.ConnectToServer();
            await Wait(() => network.RegionActive && spawn.HasValue && inventory.Items is { Count: 8 } &&
                progression.Skills?.Any(s => s.Id == 633) == true, "Beech spawn, eight items and full learned repertoire");
            if (network.ServerTickRate != 20 || System.Numerics.Vector2.Distance(spawn!.Value.Position, new(-425, 330)) > .5)
                throw new Exception("Unexpected combat test spawn or tick rate.");
            if (progression.Stats.Length != 7 || progression.Skills.Count(s => s.Slot > 0) != 8 ||
                !progression.Skills.Any(s => s.Id == 604 && s.IsPassive && s.Slot == 0))
                throw new Exception("Missing seven stats, eight-slot bar or slotless passive.");
            var player = world.GetChildren().OfType<PlayerController>().Single(p => p.EntityId == spawn.Value.EntityId);
            GetWindow().GrabFocus();
            var camera=GetViewport().GetCamera3D();
            Input.WarpMouse(camera.UnprojectPosition(player.GlobalPosition+new Vector3(3,-1,0)));
            await ToSignal(GetTree().CreateTimer(.15),SceneTreeTimer.SignalName.Timeout);
            Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=Key.Tab,Keycode=Key.Tab,Pressed=true});
            await Wait(()=>network.LatestDefense is {Blocking:true},"held Block activation");
            var guardDirection=network.LatestDefense!.Value.Direction;
            Input.WarpMouse(camera.UnprojectPosition(player.GlobalPosition+new Vector3(-3,-1,0)));
            var backwards=player.PredictedPosition-guardDirection*2;
            var guardColumn=new List<Content.Shared.Navigation.SurfaceLocation>();
            network.Navigation!.Surface!.SampleColumn(backwards.X,backwards.Y,guardColumn);
            if(guardColumn.Count==0 || !player.MoveTo(backwards,guardColumn[0].Position.Y))throw new Exception("No backwards Block test path.");
            var beforeBlockWalk=player.PredictedPosition;
            await ToSignal(GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);
            if(network.LatestDefense is not {Blocking:true} guard || System.Numerics.Vector2.Distance(guard.Direction,guardDirection)>.001f ||
                System.Numerics.Vector2.Dot(player.PredictedPosition-beforeBlockWalk,guardDirection)>=-.2f)
                throw new Exception("Held Block turned toward the moving cursor or prevented backwards walking.");
            Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=Key.Tab,Keycode=Key.Tab,Pressed=false});
            await Wait(()=>network.LatestDefense is {Blocking:false},"Block release");
            player.StopMovement();
            await ToSignal(GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
            GD.Print("BLOCK_DIRECTION_OK: cursor moved behind, guard retained activation direction during backwards walking.");
            network.SendEchoRoster(new(1, 0));
            await Wait(() => network.LatestEchoRoster is { Total: 3 }, "three owned Echoes");
            var roster = network.LatestEchoRoster!.Value;
            if (roster.Entries.Any(e => e.Availability != EchoAvailability.Ready)) throw new Exception("Echoes must await a real summon.");
            network.SendEchoSummon(new(1, roster.Entries[0].Handle));
            await Wait(() => world.GetChildren().OfType<ProjectG.Echoes.EchoControls>().Any(), "Echo UI");
            await ToSignal(GetTree().CreateTimer(11), SceneTreeTimer.SignalName.Timeout);
            network.SendEchoRoster(new(2, 0));
            await Wait(() => network.LatestEchoRoster is { Sequence: 2 } page && page.Entries.Any(e => e.Availability == EchoAvailability.Active), "ten-second Echo summon");
            var column = new List<Content.Shared.Navigation.SurfaceLocation>();
            network.Navigation!.Surface!.SampleColumn(-398, 307, column);
            if(column.Count==0) throw new Exception("Encounter approach has no walkable surface.");
            // Use the same target setter as RMB; the normal fixed-step client must own its sequence stream.
            if (!player.MoveTo(new(-398,307), column[0].Position.Y)) throw new Exception("No path from spawn to the encounter approach.");
            await Wait(() => actors.Count >= 5 && bossSeen, "walk from spawn to the ordinary enemy and boss");
            if (GameUi.SkillName(633).Contains("633")) throw new Exception("QA skills lack readable labels.");
            GD.Print($"COMBAT_ARENA_OK: Beech (-425,330), 20 TPS, {progression.Skills.Length} skills, 8 items, 3 Echoes, real summon, {actors.Count} nearby combat bodies.");
            world.QueueFree(); await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Wait(Func<bool> predicate, string stage)
    {
        var deadline = Time.GetTicksMsec() + 30000;
        while (!predicate())
        {
            if (Time.GetTicksMsec() > deadline) throw new TimeoutException(stage);
            await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
        }
    }
}
