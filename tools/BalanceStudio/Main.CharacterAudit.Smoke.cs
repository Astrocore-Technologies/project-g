using Godot;
namespace ProjectG.BalanceStudio;
public partial class Main
{
    private async Task RunCharacterAuditHostSmoke()
    {
        GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);
        async Task Frames(int n){for(var i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        await Frames(6);
        var button=FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Статы / EXP");
        if(button.GetGlobalRect().End.X>GetViewportRect().Size.X)throw new Exception("Audit entry is outside the workshop viewport.");
        var p=button.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion{Position=p,GlobalPosition=p});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p});await Frames(3);
        if(_characterAudit?.Visible!=true)throw new Exception("Workshop did not open the production audit form.");
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=true});await Frames(2);
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=false});await Frames(2);
        if(_characterAudit.Visible||_document.Dirty||_characterAudit.HasUnsavedChanges)throw new Exception("Opening/closing audit changed the project.");
        GD.Print("CHARACTER_AUDIT_HOST_OK: actual workshop button and editable form, Escape, source unchanged.");GetTree().Quit();
    }
}
