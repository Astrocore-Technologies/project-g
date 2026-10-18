using Godot;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

public partial class InputBindingsSmoke : Node
{
    public override async void _Ready()
    {
        var profile=$"user://m4-smoke-{Guid.NewGuid():N}/input.cfg";
        try
        {
            PcInput.Initialize(profile);
            Check(PcInput.Assign(PcInput.Skills[0],new(Key.F8),out _),"Assign skill");
            Check(!PcInput.Pressed(PcInput.Skills[0],new InputEventKey{PhysicalKeycode=Key.Q,Pressed=true}),"Old key still triggers skill");
            Check(PcInput.Pressed(PcInput.Skills[0],new InputEventKey{PhysicalKeycode=Key.F8,Pressed=true}),"New key does not trigger skill");
            Check(InputMap.EventIsAction(new InputEventKey{PhysicalKeycode=Key.F8,Pressed=true},"pc_skill_1",true),"Named action not installed");
            Check(!PcInput.Assign(PcInput.Skills[1],new(Key.F8),out var conflict)&&conflict.Contains("занята"),"Duplicate binding accepted");
            Check(!PcInput.Assign(PcInput.Move,new(Mouse:MouseButton.WheelUp),out _),"Held movement cannot use a wheel");
            Check(PcInput.Binding(PcInput.Parry).Matches(new InputEventKey{PhysicalKeycode=Key.Shift,ShiftPressed=true,Pressed=true},true),"Standalone modifier binding");
            Check(PcInput.Assign(PcInput.Skills[0],new(Mouse:MouseButton.Xbutton1,Ctrl:true),out _),"Assign mouse chord");
            Check(PcInput.Released(PcInput.Skills[0],new InputEventMouseButton{ButtonIndex=MouseButton.Xbutton1,Pressed=false}),"Release must work after modifier released");
            Check(PcInput.Assign(PcInput.Move,new(Mouse:MouseButton.Middle),out _),"Assign movement");
            PcInput.Initialize(profile);Check(PcInput.Binding(PcInput.Move).Mouse==MouseButton.Middle,"Profile did not persist");
            Check(PcInput.Reset(out _),"Reset failed");Check(PcInput.Binding(PcInput.Move).Mouse==MouseButton.Right,"Reset movement");
            var layer=new CanvasLayer();AddChild(layer);
            var root=new Control();layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var window=new UiWindow();root.AddChild(window);window.Build("Настройки",new(900,670));
            var settings=GD.Load<PackedScene>("res://UI/Screens/InGameSettingsView.tscn").Instantiate<ClientSettingsPanel>();
            settings.SettingsPath=profile.GetBaseDir()+"/display.cfg";settings.ShowHeading=false;window.Body.AddChild(settings);
            var tabs=settings.GetNode<TabContainer>("%Preferences");tabs.CurrentTab=1;
            window.Open();
            var bindings=tabs.GetNode<InputBindingsPanel>("Управление");
            Check(bindings.GetNode<VBoxContainer>("%BindingRows").GetChildCount()==PcInput.Actions.Length,"Missing action rows");
            bindings.BeginCapture(PcInput.Skills[0]);Check(PcInput.Capturing,"Capture not active");
            bindings._Input(new InputEventKey{PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=true});
            Check(!PcInput.Capturing&&window.Visible,"Escape must cancel capture before closing window");
            Check(PcInput.Blocked(bindings),"Settings must block gameplay");
            for(var frame=0;frame<4;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print($"BINDINGS_LAYOUT: viewport={GetViewport().GetVisibleRect()}, settings={settings.GetGlobalRect()}, tabs={tabs.GetGlobalRect()}, view={bindings.GetGlobalRect()}.");
            if(DisplayServer.GetName()!="headless")
            {
                await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var output=OS.GetEnvironment("PROJECT_G_BINDING_CAPTURE");
                if(output.Length==0)output=ProjectSettings.GlobalizePath("res://../.artifacts/movement-controls-m4/bindings.png");
                Check(GetViewport().GetTexture().GetImage().SavePng(output)==Error.Ok,"Cannot save actual settings frame");
            }
            GD.Print($"INPUT_BINDINGS_SMOKE_OK: {PcInput.Actions.Length} actions, key/mouse/modifiers, conflicts, held restrictions, persistence/reset, capture Escape, actual settings scene.");
            if(!OS.GetCmdlineUserArgs().Contains("--preview"))GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
