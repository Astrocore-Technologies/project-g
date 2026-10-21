using Content.Shared.Network;
using Godot;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Standalone adapter for the working view. No world, transport, identity or persistence.</summary>
public partial class RespecPreview:Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size=new(1280,720);
            var shell=new UiWindow();AddChild(shell);shell.Build("Сброс характеристик",new(1080,650));
            var view=GD.Load<PackedScene>("res://UI/Screens/RespecView.tscn").Instantiate<RespecView>();shell.Body.AddChild(view);
            var before=new double[7];before[2]=3;
            var stats=new double[NetworkConstants.CharacterStatCount];stats[7]=103;stats[8]=100;stats[22]=100;
            var after=(double[])stats.Clone();after[7]=100;
            view.SetQuote(new(1,1,new(1),1,1,0,0,3,20,15,true,before,new double[7],stats,after));
            view.Render(true,false,false,"Предпросмотр рабочего окна без сервера.");shell.Open();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!view.GetNode<Button>("%Confirm").Disabled)throw new Exception("Preview skipped acknowledgement.");
            view.GetNode<CheckBox>("%Acknowledge").ButtonPressed=true;
            if(view.GetNode<Button>("%Confirm").Disabled)throw new Exception("Preview acknowledgement did not enable the action.");
            var folder=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            if(folder is not null){await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(folder+"/respec-preview-1280.png");}
            GD.Print("RESPEC_PREVIEW_OK: working scene loads independently, quote layout and acknowledgement.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
