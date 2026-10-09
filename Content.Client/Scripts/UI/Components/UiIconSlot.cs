using Godot;
namespace ProjectG.UI;

/// <summary>Reusable icon, caption and quantity/key slot. Missing art never invents a content identity.</summary>
public partial class UiIconSlot : Button
{
    private TextureRect _art = null!;
    private Label _fallback = null!, _badge = null!;
    private bool _built;
    public UiIconSlot()
    {
        CustomMinimumSize = new(64,64); FocusMode = FocusModeEnum.All;
    }
    public override void _Ready() => Build();
    private void Build()
    {
        if (_built) return; _built = true;
        Theme = GameUi.CreateTheme();
        // Preserve the caller's slot size and scale art insets with it, not the whole UI.
        var artScale = CustomMinimumSize / 64f;
        _art = new TextureRect { ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize, StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter=MouseFilterEnum.Ignore };
        _art.Modulate=UiAssets.Skin.IconTint; AddChild(_art); _art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _art.OffsetLeft=7*artScale.X; _art.OffsetRight=-7*artScale.X; _art.OffsetTop=7*artScale.Y; _art.OffsetBottom=-12*artScale.Y;
        _fallback=GameUi.Text("◇",Math.Max(1,Mathf.RoundToInt(26*Math.Min(artScale.X,artScale.Y)))); _fallback.HorizontalAlignment=HorizontalAlignment.Center; _fallback.VerticalAlignment=VerticalAlignment.Center;
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
