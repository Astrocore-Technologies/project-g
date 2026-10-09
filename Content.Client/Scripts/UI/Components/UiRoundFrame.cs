using Godot;
namespace ProjectG.UI;

public partial class UiRoundFrame : Control
{
    private Control? _content;
    private TextureRect _frame=null!;
    public Control Content { get { Build(); return _content!; } }
    public override void _Ready() => Build();
    private void Build()
    {
        if(_content is not null) return;
        CustomMinimumSize=new(160,160); MouseFilter=MouseFilterEnum.Ignore;
        _content=new UiCircleClip { MouseFilter=MouseFilterEnum.Ignore }; AddChild(_content); _content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content.OffsetLeft=5; _content.OffsetTop=5; _content.OffsetRight=-5; _content.OffsetBottom=-5;
        _frame=new TextureRect { Texture=UiAssets.Skin.MinimapFrame,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=MouseFilterEnum.Ignore };
        AddChild(_frame); _frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    public override void _Notification(int what) { if(what==NotificationResized) QueueRedraw(); }
    public override void _Draw()
    { if(UiAssets.Skin.MinimapFrame is null) DrawArc(Size/2,Mathf.Min(Size.X,Size.Y)/2-2,0,Mathf.Tau,96,UiAssets.Skin.Gold,3,true); }
}

public partial class UiCircleClip : Control
{
    public UiCircleClip() { ClipChildren=ClipChildrenMode.Only; }
    public override void _Notification(int what) { if(what==NotificationResized) QueueRedraw(); }
    public override void _Draw() => DrawCircle(Size/2,Mathf.Min(Size.X,Size.Y)/2,Colors.White);
}
