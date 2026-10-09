using Godot;
namespace ProjectG.UI;

public partial class UiPortrait : Control
{
    private Texture2D? _portrait;
    private string _initial="?";
    public UiPortrait() { CustomMinimumSize=new(64,64); MouseFilter=MouseFilterEnum.Ignore; }
    public void Bind(string key,string name) { _portrait=UiAssets.Texture(key); _initial=string.IsNullOrWhiteSpace(name)?"?":System.Globalization.StringInfo.GetNextTextElement(name); TooltipText=name; QueueRedraw(); }
    public override void _Notification(int what) { if(what==NotificationResized) QueueRedraw(); }
    public override void _Draw()
    {
        var center=Size/2; var radius=Mathf.Min(Size.X,Size.Y)/2-3;
        DrawCircle(center,radius,UiAssets.Skin.Navy);
        if(_portrait is not null)
        {
            // A polygon UV mask keeps rectangular source portraits inside the round frame.
            var points=new Vector2[64]; var uv=new Vector2[64]; var aspect=_portrait.GetSize();
            var crop=new Vector2(Mathf.Min(1,aspect.Y/aspect.X),Mathf.Min(1,aspect.X/aspect.Y));
            for(var i=0;i<64;i++) { var unit=Vector2.FromAngle(i*Mathf.Tau/64); points[i]=center+unit*(radius-2); uv[i]=Vector2.One*.5f+unit*crop*.5f; }
            DrawPolygon(points,new Color[] { Colors.White },uv,_portrait);
        }
        else
        {
            var font=GetThemeDefaultFont(); var width=font.GetStringSize(_initial,fontSize:22).X;
            DrawString(font,new(center.X-width/2,center.Y+8),_initial,fontSize:22,modulate:UiAssets.Skin.LightText);
        }
        if(UiAssets.Skin.PortraitFrame is {} frame) DrawTextureRect(frame,new(Vector2.Zero,Size),false);
        else { DrawArc(center,radius,0,Mathf.Tau,64,UiAssets.Skin.Gold,2,true); DrawArc(center,radius-4,0,Mathf.Tau,64,new Color("84999f"),1,true); }
    }
}
