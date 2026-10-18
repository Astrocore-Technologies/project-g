using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
using System.Text.Json;
namespace ProjectG.Tests.UI;

/// <summary>Manual MCP review against an isolated development server; no runtime fixture data.</summary>
public partial class UiReworkLive : Node
{
    private WorldController? _world;
    public bool ReadyForReview { get; private set; }
    public override void _Ready()
    {
        try
        {
            var config=JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://../.artifacts/ui-redesign/live.json"));
            _world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); _world.AutoConnect=false; _world.UiSettingsPath="res://../.artifacts/ui-final-2026-10-18/review-settings.cfg";
            var network=_world.GetNode<NetworkClient>("NetworkClient"); network.DevelopmentIdentityProfile=config.RootElement.GetProperty("identity").GetString()!;
            _world.WorldReady+=()=> { ReadyForReview=true; GD.Print("UI_REWORK_REVIEW_READY"); };
            AddChild(_world); network.ConnectToServerAt(config.RootElement.GetProperty("port").GetInt32());
        }
        catch(Exception e) { GD.PushError(e.ToString()); }
    }
    public void ShowScreen(string route) => GameUi.Navigate(route);
    public void CloseScreen()
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=false });
    }
    public void SetReviewSize(int width,int height) { GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(width,height); }
}
