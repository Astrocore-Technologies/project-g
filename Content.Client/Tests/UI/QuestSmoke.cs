using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.Quests;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Real dialog/journal controls against an isolated server and development identity.</summary>
public partial class QuestSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode = Window.ModeEnum.Windowed; GetWindow().Size = new(1280, 720);
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            world.AutoConnect = false; AddChild(world);
            var network = world.GetNode<NetworkClient>("NetworkClient");
            QuestReply? reply = null; network.QuestReplyReceived += value => reply = value;
            network.ConnectToServer(); await Wait(() => network.LatestQuestJournal is not null, "journal login"); await Delay(.5);
            var player = world.GetChildren().OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            var ui = world.GetChildren().OfType<QuestPresentation>().Single();
            await Walk(player, new(-15, 18));
            Press(Key.F2); await Wait(() => reply is { Choices: 1 }, "giver dialog");
            await Capture("quest-dialog");
            await Click(ui, "Взяться за доставку");
            await Wait(() => network.LatestQuestJournal is { Status: QuestStatus.Active, Carried: 3 }, "accept materials and quest");
            Press(Key.Escape); Press(Key.L); await Delay(.4);
            if (!GameUi.QuestWindowOpen) throw new Exception("Journal did not open.");
            await Capture("quest-journal");
            var position = player.PredictedPosition;
            var stamina = network.LatestDefense!.Value.Stamina;
            Press(Key.Shift);
            var point = GetViewport().GetCamera3D().UnprojectPosition(player.GlobalPosition + Vector3.Right * 3);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = point, GlobalPosition = point });
            await Delay(.3);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = point, GlobalPosition = point });
            if (System.Numerics.Vector2.Distance(position, player.PredictedPosition) > .15) throw new Exception("Journal leaked movement input.");
            if (network.LatestDefense!.Value.Stamina < stamina - .1) throw new Exception("Journal leaked parry input.");
            Press(Key.Escape); await Walk(player, new(-7, 7));
            reply = null;
            var recipient = network.QuestNpcs.Values.Single(n => n.Name == "Орен");
            point = GetViewport().GetCamera3D().UnprojectPosition(new(recipient.Position.X, 1, recipient.Position.Y));
            Input.WarpMouse(point); await Delay(.1);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point });
            await Wait(() => reply is { Choices: 2 }, "recipient NPC click");
            await Click(ui, "Передать материалы");
            await Wait(() => network.LatestQuestJournal is { Status: QuestStatus.Completed, Carried: 0 }, "delivery completion");
            Press(Key.Escape); Press(Key.L); await Delay(.4); await Capture("quest-complete");
            world.QueueFree(); await Delay(.5);
            if (GameUi.QuestWindowOpen) throw new Exception("Quest modal state was not cleared on teardown.");
            var restored = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); restored.AutoConnect = false; AddChild(restored);
            var next = restored.GetNode<NetworkClient>("NetworkClient"); next.ConnectToServer();
            await Wait(() => next.LatestQuestJournal is { Status: QuestStatus.Completed }, "durable quest reconnect");
            restored.QueueFree(); await Delay(.2);
            GD.Print("QUEST_SMOKE_OK: F2 dialog, accept button, L journal, modal input, deliver button, durable reconnect, cleanup.");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private async Task Walk(PlayerController player, System.Numerics.Vector2 destination)
    {
        if (!player.MoveTo(destination)) throw new Exception("Quest route is not navigable.");
        await Wait(() => System.Numerics.Vector2.Distance(player.PredictedPosition, destination) < .15, "quest approach"); await Delay(.4);
    }
    private static IEnumerable<Node> Descendants(Node node)
    { foreach (var child in node.GetChildren()) { yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private async Task Click(QuestPresentation ui, string text)
    {
        var button = Descendants(ui).OfType<Button>().Single(b => b.Text == text);
        await Wait(() => button.IsVisibleInTree() && !button.Disabled, "button: " + text);
        var point = button.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
        await Delay(.05);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point });
    }
    private static void Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture")) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/" + name + ".png");
    }
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> predicate, string stage)
    {
        var until = Time.GetTicksMsec() + 15000;
        while (!predicate() && Time.GetTicksMsec() < until) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!predicate()) throw new TimeoutException(stage);
    }
}
