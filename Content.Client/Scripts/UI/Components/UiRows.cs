using Godot;
namespace ProjectG.UI;

/// <summary>Data-bound instances of editable list scenes. Never builds a screen layout.</summary>
public static class UiRows
{
    private static PackedScene? _row, _button, _note, _selection, _amount;
    public static void Clear(Node parent)
    { foreach(var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    public static Label Note(Node parent,string text)
    {
        var label=(_note??=GD.Load<PackedScene>("res://UI/Components/ListNote.tscn")).Instantiate<Label>();
        label.Text=text; parent.AddChild(label); return label;
    }
    public static Button Button(Node parent,string text,Action action,bool disabled=false)
    {
        var button=(_button??=GD.Load<PackedScene>("res://UI/Components/ActionButton.tscn")).Instantiate<Button>();
        button.Text=text; button.TooltipText=text; button.Disabled=disabled; button.Pressed+=action; parent.AddChild(button); return button;
    }
    public static Control Action(Node parent,string title,string details,string art="",string label="",Action? action=null,bool disabled=false)
    {
        var row=(_row??=GD.Load<PackedScene>("res://UI/Components/ActionRow.tscn")).Instantiate<Control>(); parent.AddChild(row);
        row.GetNode<Label>("%Title").Text=title; row.GetNode<Label>("%Details").Text=details;
        var icon=row.GetNode<TextureRect>("%Icon"); icon.Texture=UiAssets.Texture(art); icon.Visible=icon.Texture is not null;
        icon.Modulate=icon.Texture is AtlasTexture?Colors.White:new Color(.85f,.84f,.71f);
        var button=row.GetNode<Button>("%Primary"); button.Visible=action is not null; button.Text=label; button.Disabled=disabled;
        if(action is not null) button.Pressed+=action;
        return row;
    }
    public static void Secondary(Control row,string label,Action action,bool disabled=false)
    { var button=row.GetNode<Button>("%Secondary"); button.Visible=true; button.Text=label; button.Disabled=disabled; button.Pressed+=action; }
    public static CheckBox Selection(Node parent,string title,bool selected,bool disabled,Action<bool> changed)
    {
        var box=(_selection??=GD.Load<PackedScene>("res://UI/Components/SelectionRow.tscn")).Instantiate<CheckBox>();
        box.Text=title; box.TooltipText=title; box.ButtonPressed=selected; box.Disabled=disabled; parent.AddChild(box); box.Toggled+=changed.Invoke; return box;
    }
    public static SpinBox Amount(Node parent,string title,double maximum,double value,bool editable,Action<double> changed)
    {
        var row=(_amount??=GD.Load<PackedScene>("res://UI/Components/MaterialAmountRow.tscn")).Instantiate<Control>(); parent.AddChild(row);
        row.GetNode<Label>("%Title").Text=title; var amount=row.GetNode<SpinBox>("%Amount");
        amount.MaxValue=maximum; amount.Value=value; amount.Editable=editable; amount.ValueChanged+=changed.Invoke; return amount;
    }
}
