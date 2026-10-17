using Godot;
namespace ProjectG.UI;

/// <summary>Reusable icon, caption and quantity/key slot. Missing art never invents a content identity.</summary>
public partial class UiIconSlot : Button
{
    [Export] public bool Framed { get; set; }=true;
    private TextureRect _art = null!;
    private Label _fallback = null!, _badge = null!;
    private bool _built;
    public UiIconSlot()
    {
        FocusMode = FocusModeEnum.All;
    }
    public override void _Ready() => Build();
    private void Build()
    {
        if (_built) return; _built = true;
        if(CustomMinimumSize==Vector2.Zero) CustomMinimumSize=new(64,64);
        Theme = GameUi.CreateTheme();
        var content=GD.Load<PackedScene>("res://UI/Components/IconSlotContent.tscn").Instantiate<Control>(); AddChild(content);
        _art=content.GetNode<TextureRect>("Art"); _fallback=content.GetNode<Label>("Fallback"); _badge=content.GetNode<Label>("Badge");
        var theme=GameUi.CreateTheme();
        AddThemeStyleboxOverride("normal",Framed?theme.GetStylebox("panel","HudSlot"):new StyleBoxEmpty());
        AddThemeStyleboxOverride("disabled",Framed?theme.GetStylebox("panel","HudSlot"):new StyleBoxEmpty());
        AddThemeStyleboxOverride("hover",theme.GetStylebox("hover","Button"));
        AddThemeStyleboxOverride("pressed",theme.GetStylebox("pressed","Button"));
    }
    public void Bind(string key, string accessibleName, string badge="", string? fallbackKey=null)
    {
        Build(); _art.Texture=UiAssets.Texture(key,fallbackKey); _art.Visible=_art.Texture is not null;
        _art.Modulate=_art.Texture is AtlasTexture ? Colors.White : UiAssets.Skin.IconTint;
        _fallback.Visible=!_art.Visible; _badge.Text=badge; TooltipText=accessibleName; AccessibilityName=accessibleName;
    }
    public void SetBadge(string value) { Build(); _badge.Text=value; }
}
