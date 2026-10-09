using Godot;
namespace ProjectG.UI;

/// <summary>Shared readable prototype styling; no per-frame theme construction.</summary>
public static class GameUi
{
    public static bool CharacterWindowOpen { get; set; }
    public static readonly Color Accent = new("85d6b0");
    public static StyleBoxFlat Box(Color color, int padding = 12) => new()
    {
        BgColor = color, BorderColor = new("40545f"), BorderWidthBottom = 1, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        ContentMarginLeft = padding, ContentMarginRight = padding, ContentMarginTop = padding, ContentMarginBottom = padding
    };
    public static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFontSize = 16 };
        theme.SetColor("font_color", "Label", new("e4eced"));
        theme.SetStylebox("panel", "PanelContainer", Box(new Color(.055f,.08f,.10f,.96f)));
        theme.SetStylebox("normal", "Button", Box(new("23343e"), 8));
        theme.SetStylebox("hover", "Button", Box(new("345448"), 8));
        theme.SetStylebox("pressed", "Button", Box(new("446e59"), 8));
        theme.SetStylebox("disabled", "Button", Box(new("172128"), 8));
        theme.SetStylebox("panel", "TabContainer", new StyleBoxEmpty());
        theme.SetStylebox("tab_selected", "TabContainer", Box(new("29483c"),8));
        theme.SetStylebox("tab_unselected", "TabContainer", Box(new("172128"),8));
        theme.SetConstant("separation", "VBoxContainer", 8);
        theme.SetConstant("separation", "HBoxContainer", 8);
        return theme;
    }
    public static Label Text(string text, int size = 16) { var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size",size); return label; }
    public static Button Button(string text, Action action)
    { var button = new Button { Text = text }; button.Pressed += action; return button; }
    public static string SkillName(ushort id) => id switch { 1 => "Снаряд", 2 => "Область", 3 => "Рывок", 5 => "Болт открытия", 6 => "Импульс", _ => "Навык" };
}
