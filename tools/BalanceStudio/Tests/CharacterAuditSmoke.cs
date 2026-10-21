using Godot;
using ProjectG.Balance;
namespace ProjectG.BalanceStudio.Tests;

public partial class CharacterAuditSmoke:Node
{
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();var root=args.Single(a=>a.StartsWith("--repository="))[13..];var output=args.Single(a=>a.StartsWith("--output="))[9..];
            Directory.CreateDirectory(output);var content=Path.Combine(output,"content.json");var rewards=Path.Combine(output,"rewards.json");
            File.Copy(Path.Combine(root,"Content.Server/Data/prototype.json"),content);File.Copy(Path.Combine(root,"Content.Server/Data/experience-rewards.json"),rewards);
            var original=File.ReadAllText(content);var originalRewards=File.ReadAllText(rewards);var document=new BalanceDocument(content);
            var window=GD.Load<PackedScene>("res://CharacterAuditWindow.tscn").Instantiate<CharacterAuditWindow>();AddChild(window);window.Initialize(root,document,rewards);
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);window.Open();await Frames(8);await Capture(window,output,"audit-initial-1280");var pve=window.GetNode<SpinBox>("%Pve");pve.Value=21;
            if(!window.HasUnsavedChanges)throw new Exception("Unapplied fields bypassed the unsaved-change guard.");
            await Click(window.GetNode<Button>("%ApplyRewards"));if(window.RewardDraft.Value.PveExperience!=21||!window.HasUnsavedChanges)throw new Exception("Reward edit was not staged.");
            if(File.ReadAllText(rewards)!=originalRewards)throw new Exception("Editing changed the reward source.");
            await Click(window.GetNode<Button>("%Undo"));if(window.HasUnsavedChanges)throw new Exception("Undo failed.");
            await Click(window.GetNode<Button>("%Redo"));if(window.RewardDraft.Value.PveExperience!=21)throw new Exception("Redo failed.");
            await Click(window.GetNode<Button>("%SaveRewards"));if(window.HasUnsavedChanges||window.RewardDraft.Value.Revision!=3)throw new Exception("Validated reward save/revision failed.");
            await Capture(window,output,"audit-rewards-1280");
            await Click(window.GetNode<CheckBox>("%Experimental"));await Click(window.GetNode<Button>("%ApplyCurve"));
            if(document.CharacterExperienceTable()[0].NextExperience!=1677||File.ReadAllText(content)!=original)throw new Exception("Power draft leaked to source or used the wrong server curve.");
            document.Undo();if(document.Dirty)throw new Exception("Curve edit is not undoable.");
            pve.Value=22;await Click(window.GetNode<Button>("%ApplyRewards"));File.AppendAllText(rewards," ");var outside=File.ReadAllText(rewards);
            await Click(window.GetNode<Button>("%SaveRewards"));if(!window.GetNode<Label>("%Status").Text.Contains("изменён снаружи")||File.ReadAllText(rewards)!=outside)throw new Exception("Disk conflict overwrote an external edit.");
            GetWindow().Size=new(1920,1080);await Frames(5);await Capture(window,output,"audit-conflict-1920");
            var report=window.Export();if(!File.Exists(Path.Combine(report,"report.json")))throw new Exception("Export missing.");
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=true});await Frames(2);
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=false});await Frames(2);
            if(window.Visible||!window.HasUnsavedChanges)throw new Exception("Escape lost the draft or did not close the form.");
            window.Open();await Frames(2);if(pve.Value!=22)throw new Exception("Reopening discarded the unsaved reward.");
            GD.Print("CHARACTER_AUDIT_UI_OK: editable scene, real clicks, draft/undo/redo/save, revision, curve opt-in, outside conflict, export, 1280/1920. "+report);
            window.Hide();window.QueueFree();await Frames(2);GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task Frames(int count){for(var i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Click(BaseButton button)
    {
        if(!button.IsVisibleInTree()||button.Disabled)throw new Exception("Unavailable audit button: "+button.Name);
        var p=button.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion{Position=p,GlobalPosition=p});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p});await Frames(3);
    }
    private async Task Capture(Control window,string directory,string name)
    {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);window.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,name+".png"));}
}
