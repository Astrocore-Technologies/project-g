using Godot;
namespace ProjectG.UI;

[Tool]
public partial class UiCircleClip : Control
{
    public UiCircleClip() { ClipChildren=ClipChildrenMode.Only; MouseFilter=MouseFilterEnum.Ignore; }
    public override void _Notification(int what) { if(what==NotificationResized) QueueRedraw(); }
    public override void _Draw() => DrawCircle(Size/2,Mathf.Min(Size.X,Size.Y)/2,Colors.White);
}
