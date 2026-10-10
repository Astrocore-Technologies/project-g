using Godot;

namespace ProjectG.BalanceStudio;

internal static class StudioTheme
{
    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", new("e0e8ee"));
        theme.SetColor("font_color", "Button", new("d9e4ed"));
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, Box("233340", 6, 8));
            theme.SetStylebox("hover", type, Box("304856", 6, 8));
            theme.SetStylebox("pressed", type, Box("315f5d", 6, 8));
            theme.SetStylebox("disabled", type, Box("1a2630", 6, 8));
        }
        theme.SetStylebox("normal", "LineEdit", Box("101c25", 4, 7));
        theme.SetStylebox("panel", "Tree", Box("15212b", 6, 6));
        theme.SetStylebox("panel", "TabContainer", Box("15212b", 6, 10));
        theme.SetStylebox("tab_selected", "TabContainer", Box("29423f", 5, 9));
        theme.SetStylebox("tab_unselected", "TabContainer", Box("1b2b36", 5, 9));
        theme.SetConstant("v_separation", "Tree", 8);
        return theme;
    }
    public static StyleBoxFlat Panel() => Box("1b2b36", 8, 12);
    private static StyleBoxFlat Box(string color, int radius, int padding) => new()
    {
        BgColor = new(color), CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = padding, ContentMarginRight = padding, ContentMarginTop = padding, ContentMarginBottom = padding
    };
}
