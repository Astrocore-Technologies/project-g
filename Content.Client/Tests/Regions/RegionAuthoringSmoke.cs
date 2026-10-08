using Godot;
using ProjectG.Regions;
using ProjectG.Regions.Authoring;

namespace ProjectG.Tests.Regions;

/// <summary>Run through Godot: tests actual PackedScene serialization and node lifetime.</summary>
public partial class RegionAuthoringSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var shellScene = GD.Load<PackedScene>("res://Scenes/App/GameRoot.tscn");
            var shell = shellScene.Instantiate<GameRoot>();
            AddChild(shell);
            Require(shell.ActiveRegion?.RegionId == "prototype", "Initial preview did not load.");
            var oldRegion = shell.ActiveRegion!;
            for (var i = 0; i < 20; i++)
            {
                var expected = i % 2 == 0 ? "outskirts" : "prototype";
                Require(shell.ShowRegion(i % 2 == 0 ? shell.OutskirtsScene : shell.PrototypeScene), "Switch failed.");
                Require(shell.ActiveRegion?.RegionId == expected, "Wrong region after switch.");
                Require(shell.RegionHost.GetChildCount() == 1, "Detached scene retained in host.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(!GodotObject.IsInstanceValid(oldRegion), "Old region was not freed.");

            var region = shell.ActiveRegion!;
            var marker = region.GetNode<EntryMarker>("AuthoringAnchors/Entry");
            var gate = region.GetNode<GateMarker>("AuthoringAnchors/Gate");
            var originalId = marker.AuthoredObjectId;
            marker.Position += Vector3.Left;
            marker.Name = "RenamedEntry";
            var packed = new PackedScene();
            Require(packed.Pack(region) == Error.Ok, "Packing edited scene failed.");
            var restored = packed.Instantiate<RegionRoot>();
            var restoredMarker = restored.GetNode<EntryMarker>("AuthoringAnchors/RenamedEntry");
            Require(restoredMarker.AuthoredObjectId == originalId && restoredMarker.Position == marker.Position,
                "Scene serialization lost edited placement or stable identity.");
            Require(RegionAuthoringValidation.Validate(restored).Count == 0, "Valid edited scene rejected.");
            restored.Free();

            gate.AuthoredObjectId = originalId;
            Require(RegionAuthoringValidation.Validate(region).Any(x => x.Contains("duplicate")), "Duplicate ID accepted.");
            Require(!shell.ShowRegion(Pack(region)), "Invalid scene replaced active preview.");
            Require(shell.ActiveRegion == region, "Invalid scene discarded current preview.");
            gate.AuthoredObjectId = "not-a-uuid";
            marker.Position = new Vector3(100, 0, 100);
            marker.Scale = new Vector3(2, 1, 1);
            var errors = RegionAuthoringValidation.Validate(region);
            Require(errors.Any(x => x.Contains("UUID")) && errors.Any(x => x.Contains("outside")) &&
                errors.Any(x => x.Contains("identity basis")), "Invalid marker constraints were not checked.");

            shell.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(shell) && !GodotObject.IsInstanceValid(region), "Preview subtree leaked.");
            GD.Print("REGION_AUTHORING_SMOKE_OK: scenes, switches, identity, validation, lifecycle.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static PackedScene Pack(Node node)
    {
        var packed = new PackedScene();
        Require(packed.Pack(node) == Error.Ok, "Packing failed.");
        return packed;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
