using Godot;
namespace ProjectG.UI;

/// <summary>Source-kit composition shared by live windows; routes never contain game state.</summary>
public static class UiComposition
{
    public static readonly (string Id,string Label,string Icon,string Key)[] Screens = [
        ("character","Персонаж","nav.character","K"), ("inventory","Инвентарь","nav.inventory","I"),
        ("echo","Эхо Прошлого","icon.star","F3"), ("guild","Гильдия","icon.guild","G"),
        ("journal","Журнал","nav.quests","L"), ("map","Карта мира","nav.map","M")];
    public static VBoxContainer Page(UiWindow window,string active)
    {
        var body=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill };
        window.Body.AddChild(body); return body;
    }
    public static VBoxContainer Card(Node parent, float width=0)
    {
        var panel=new PanelContainer { CustomMinimumSize=new(width,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; parent.AddChild(panel);
        var body=new VBoxContainer(); panel.AddChild(body); return body;
    }
    public static Label Paragraph(Node parent,string text,int size=16)
    {
        var label=GameUi.Text(text,size); label.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill; parent.AddChild(label); return label;
    }
    public static VBoxContainer Scroll(Node parent)
    {
        var scroll=new ScrollContainer { HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; parent.AddChild(scroll);
        var body=new VBoxContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill }; scroll.AddChild(body); return body;
    }
}
