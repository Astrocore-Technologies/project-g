using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.Progression;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Real client/server UI smoke. Uses a separate identity and server passed on the command line.</summary>
public partial class PlayerHudSmoke : Node
{
    private int _previews;
    private ProgressionState _state;
    public override async void _Ready()
    {
        try
        {
            foreach (var index in new[] { 3,4,11,12,13,14 })
            {
                if (ProgressionPresentation.Format(index,1.02)!="+2%" ||
                    ProgressionPresentation.Format(index,.95)!="-5%" ||
                    ProgressionPresentation.Format(index,1)!="0%" ||
                    ProgressionPresentation.Format(index,1.000001)!="0%" ||
                    ProgressionPresentation.Format(index,1.0126)!="+1"+System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator+"3%")
                    throw new Exception("Incorrect percentage bonus display.");
            }
            if (ProgressionPresentation.Format(15,.05)!="5%" || ProgressionPresentation.Format(16,.3)!="30%" || ProgressionPresentation.Format(7,101)!="101")
                throw new Exception("Probability or absolute stat was formatted as a modifier.");
            foreach (var index in new[] { 0,1,2,5,6,7,8,9,10 })
                if (ProgressionPresentation.Format(index,12.34)!="12" || ProgressionPresentation.Format(index,12.76)!="13")
                    throw new Exception("Absolute stats must display whole numbers without changing their values.");
            if (ProgressionPresentation.Format(15,.0534)!="5"+System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator+"3%")
                throw new Exception("Critical chance must retain fractional percentages.");
            if (OS.GetCmdlineUserArgs().Contains("--format-only"))
            { GD.Print("STAT_PERCENTAGES_OK: positive, negative, zero, rounding, fractions, probability and absolute values."); GetTree().Quit(); return; }
            GetWindow().Mode=Window.ModeEnum.Windowed; await Frames(2); GetWindow().Size=new(1280,720); await Frames(3);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient");
            network.ProgressionReceived += state=>_state=state;
            network.StatPreviewReceived += _=>_previews++;
            network.ConnectToServer(); await Wait(()=>_state.Stats is not null);
            var character=world.GetChildren().OfType<ProgressionPresentation>().Single();
            var hud=world.GetChildren().OfType<PlayerHud>().Single();
            await ToSignal(GetTree().CreateTimer(.7),SceneTreeTimer.SignalName.Timeout);
            if (hud.FindChildren("*","ProgressBar",true,false).Count!=4) throw new Exception("Missing resource bars.");
            if (OS.GetCmdlineUserArgs().Contains("--solo"))
            {
                if (world.GetChildren().OfType<ProjectG.Echoes.EchoPresentation>().Any()) throw new Exception("Unacquired Echo was spawned.");
                var echoes=world.GetChildren().OfType<ProjectG.Echoes.EchoControls>().Single();
                if (((Control)echoes.FindChild("CompanionHud",true,false)).Visible) throw new Exception("Empty companion HUD is visible.");
                echoes.Toggle(); await Frames(3);
                if (!echoes.FindChildren("*","Label",true,false).Cast<Label>().Any(l=>l.Text.StartsWith("У вас пока нет Эхо.")))
                    throw new Exception("Missing unowned companion explanation.");
                echoes.Toggle(); await Frames(2);
                var guide=world.GetChildren().OfType<ProjectG.StarterZone.StarterZonePresentation>().Single();
                guide.ToggleGuide(); await Frames(2);
                if (guide.FindChildren("*","Label",true,false).Cast<Label>().Any(l=>l.Text.Contains("способность Эхо")))
                    throw new Exception("Unobtainable Echo tutorial is visible.");
                guide.ToggleGuide(); await Frames(2);
                GD.Print("SOLO_START_OK: no companion, hidden empty HUD, no Echo tutorial, empty collection remains accessible.");
            }
            await Capture("hud-1280");
            character.Toggle(); await Wait(()=>_previews>0); await Frames(3);
            var buttons=character.FindChildren("*","Button",true,false).Cast<Button>().ToArray();
            var plus=buttons.Where(b=>b.Text=="+").ToArray();
            var apply=buttons.Single(b=>b.Text=="Применить");
            var cancel=buttons.Single(b=>b.Text=="Отменить распределение");
            var original=_state; var count=_previews;
            await Click(plus[3]); await Click(plus[4]);
            await Wait(()=>_previews>count && !apply.Disabled);
            if (_state.StatPoints!=original.StatPoints || !_state.Stats.SequenceEqual(original.Stats)) throw new Exception("Draft mutated state.");
            await Frames(3); await Capture("character-preview-1280");
            count=_previews; await Click(cancel); await Wait(()=>_previews>count);
            if (!apply.Disabled || _state.StatPoints!=original.StatPoints) throw new Exception("Cancel failed.");
            count=_previews; await Click(plus[4]); await Wait(()=>_previews>count && !apply.Disabled);
            await Click(apply);
            await Wait(()=>_state.StatPoints==original.StatPoints-1);
            if (_state.Stats[4]!=original.Stats[4]+1 || _state.Stats[3]!=original.Stats[3]) throw new Exception("Batch applied wrong stats.");
            character.Toggle(); GetWindow().Size=new(1920,1080); await Frames(5); await Capture("hud-1920");
            world.QueueFree(); await Frames(2);
            GD.Print("PLAYER_HUD_OK: resources, draft preview, cancellation, atomic apply, resize, cleanup."); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Wait(Func<bool> condition)
    { var end=Time.GetTicksMsec()+20000; while(!condition() && Time.GetTicksMsec()<end) await Frames(1); if(!condition()) throw new TimeoutException("UI smoke deadline."); }
    private async Task Frames(int count) { for(var i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private async Task Click(Button button)
    {
        var position=button.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true,Position=position,GlobalPosition=position }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=false,Position=position,GlobalPosition=position }); await Frames(2);
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture")) return;
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"res://../.artifacts/{name}.png");
    }
}
