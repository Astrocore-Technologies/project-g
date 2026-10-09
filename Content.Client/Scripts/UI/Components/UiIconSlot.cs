using Godot;
namespace ProjectG.UI;

/// <summary>Reusable icon, caption and quantity/key slot. Missing art never invents a content identity.</summary>
public partial class UiIconSlot : Button
{
    private TextureRect _art = null!;
    private Label _fallback = null!, _badge = null!;
    private bool _built;
    public override void _Ready() => Build();
    private void Build()
    {
        if (_built) return; _built = true;
        CustomMinimumSize = new(64,64); Theme = GameUi.CreateTheme(); FocusMode = FocusModeEnum.All;
        _art = new TextureRect { ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize, StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter=MouseFilterEnum.Ignore };
        _art.Modulate=UiAssets.Skin.IconTint; AddChild(_art); _art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _art.OffsetLeft=7; _art.OffsetRight=-7; _art.OffsetTop=7; _art.OffsetBottom=-12;
        _fallback=GameUi.Text("◇",26); _fallback.HorizontalAlignment=HorizontalAlignment.Center; _fallback.VerticalAlignment=VerticalAlignment.Center;
        _fallback.AddThemeColorOverride("font_color",UiAssets.Skin.IconTint); AddChild(_fallback); _fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _badge=GameUi.Text("",12); _badge.HorizontalAlignment=HorizontalAlignment.Right; _badge.VerticalAlignment=VerticalAlignment.Bottom;
        _badge.AddThemeColorOverride("font_color",UiAssets.Skin.IconTint); AddChild(_badge); _badge.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _badge.OffsetRight=-5; _badge.OffsetBottom=-3;
        AddThemeStyleboxOverride("normal",UiAssets.Skin.SlotNormal ?? GameUi.Box(new("52606a"),4));
        AddThemeStyleboxOverride("disabled",UiAssets.Skin.SlotNormal ?? GameUi.Box(new("52606a"),4));
        AddThemeStyleboxOverride("hover",UiAssets.Skin.SlotHover ?? UiAssets.Skin.SlotNormal ?? GameUi.Box(new("63717b"),4));
        AddThemeStyleboxOverride("pressed",UiAssets.Skin.SlotSelected ?? GameUi.Box(new("6b624a"),4));
    }
    public void Bind(string key, string accessibleName, string badge="", string? fallbackKey=null)
    {
        Build(); _art.Texture=UiAssets.Texture(key,fallbackKey); _art.Visible=_art.Texture is not null;
        _fallback.Visible=!_art.Visible; _badge.Text=badge; TooltipText=accessibleName; AccessibilityName=accessibleName;
    }
    public void SetBadge(string value) { Build(); _badge.Text=value; }
}
