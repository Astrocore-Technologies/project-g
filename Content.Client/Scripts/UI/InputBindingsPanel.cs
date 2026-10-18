using Godot;

namespace ProjectG.UI;

/// <summary>Repeating rows use one authored scene. Input profile remains client-only.</summary>
public partial class InputBindingsPanel : VBoxContainer
{
    private readonly Dictionary<string,Button> _buttons = new();
    private Label _feedback = null!;
    private string? _capture;
    private Key _modifier;
    public override void _Ready()
    {
        PcInput.Initialize();
        _feedback=GetNode<Label>("%BindingFeedback");
        var rows=GetNode<VBoxContainer>("%BindingRows");
        var rowScene=GD.Load<PackedScene>("res://UI/Components/InputBindingRow.tscn");
        foreach(var action in PcInput.Actions)
        {
            var row=rowScene.Instantiate<HBoxContainer>(); rows.AddChild(row);
            row.GetNode<Label>("Title").Text=action.Title;
            var button=row.GetNode<Button>("Binding");_buttons.Add(action.Id,button);
            button.Pressed+=()=>BeginCapture(action.Id);
            row.GetNode<Button>("Clear").Pressed+=()=>Apply(action.Id,new());
        }
        GetNode<Button>("%ResetBindings").Pressed+=Reset;
        PcInput.Changed+=Refresh; VisibilityChanged+=OnVisibility;
        Refresh();
    }
    public void BeginCapture(string action)
    {
        CancelCapture();_capture=action;_modifier=Key.None;PcInput.Capturing=true;
        _feedback.Text="Нажмите кнопку или сочетание. Escape — отменить.";
        _buttons[action].Text="Ожидание кнопки…";
    }
    public override void _Input(InputEvent input)
    {
        if(_capture is null || !IsVisibleInTree())return;
        GetViewport().SetInputAsHandled();
        if(input.IsActionPressed("ui_cancel")){CancelCapture();_feedback.Text="Назначение отменено";return;}
        if(input is InputEventKey key && key.PhysicalKeycode is Key.Ctrl or Key.Shift or Key.Alt)
        {
            if(key.Pressed){_modifier=key.PhysicalKeycode;return;}
            if(_modifier!=Key.None){Apply(_capture,new(_modifier));return;}
        }
        if(PcBinding.Capture(input) is {} binding)Apply(_capture,binding);
    }
    private void Apply(string action,PcBinding binding)
    {
        CancelCapture();
        _feedback.Text=PcInput.Assign(action,binding,out var reason)?"Управление сохранено":reason;
        Refresh();
    }
    private void Reset()
    { CancelCapture();_feedback.Text=PcInput.Reset(out var reason)?"Стандартное управление восстановлено":reason; }
    public void CancelCapture(){_capture=null;_modifier=Key.None;PcInput.Capturing=false;if(_buttons.Count>0)Refresh();}
    private void Refresh(){foreach(var pair in _buttons)pair.Value.Text=PcInput.Label(pair.Key);}
    private void OnVisibility(){if(!IsVisibleInTree())CancelCapture();}
    public override void _ExitTree(){CancelCapture();PcInput.Changed-=Refresh;VisibilityChanged-=OnVisibility;}
}
