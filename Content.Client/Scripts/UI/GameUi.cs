using Godot;
namespace ProjectG.UI;

/// <summary>Shared presentation only. Appearance is configured in UI/Theme, game state stays with presenters.</summary>
public static class GameUi
{
    public static bool CharacterWindowOpen { get; set; }
    public static bool QuestWindowOpen { get; set; }
    private static readonly HashSet<Control> Modals = new();
    private static Theme? _dark, _paper;
    private static readonly Dictionary<string,Action> Routes = new();
    public static void RegisterRoute(string id, Action open) => Routes[id]=open;
    public static void ClearRoutes() => Routes.Clear();
    public static bool HasRoute(string id) => Routes.ContainsKey(id);
    public static void Navigate(string id)
    {
        if(!Routes.TryGetValue(id,out var open)) return;
        foreach(var modal in Modals.ToArray()) if(modal is UiWindow window) window.RequestClose();
        if(!GameplayModalOpen) open();
    }
    public static bool GameplayModalOpen => CharacterWindowOpen || QuestWindowOpen || Modals.Count != 0;
    public static Color Accent => UiAssets.Skin.Gold;
    public static event Action? ModalOpened;
    internal static void RegisterModal(Control control) { if(Modals.Add(control)) ModalOpened?.Invoke(); }
    internal static void UnregisterModal(Control control) => Modals.Remove(control);
    public static StyleBoxFlat Box(Color color, int padding=12) => new()
    {
        BgColor=color, BorderColor=UiAssets.Skin.Gold.Darkened(.3f),
        BorderWidthBottom=1, BorderWidthTop=1, BorderWidthLeft=1, BorderWidthRight=1,
        CornerRadiusTopLeft=6, CornerRadiusTopRight=6, CornerRadiusBottomLeft=6, CornerRadiusBottomRight=6,
        ContentMarginLeft=padding, ContentMarginRight=padding, ContentMarginTop=padding, ContentMarginBottom=padding
    };
    public static Theme CreateTheme() => _dark ??= BuildTheme(false);
    public static Theme CreatePaperTheme() => _paper ??= BuildTheme(true);
    private static Theme BuildTheme(bool paper)
    {
        var skin=UiAssets.Skin; var text=paper?skin.Ink:skin.LightText;
        var theme=new Theme { DefaultFontSize=skin.FontSize };
        if(skin.Font is not null) theme.DefaultFont=skin.Font;
        foreach(var type in new[]{"Label","RichTextLabel","CheckBox","CheckButton","TabBar","TabContainer"}) theme.SetColor("font_color",type,text);
        theme.SetColor("default_color","RichTextLabel",text);
        theme.SetStylebox("panel","PanelContainer",paper ? skin.PaperPanel ?? Box(skin.Paper,18) : skin.DarkPanel ?? Box(skin.Navy,12));
        foreach(var type in new[]{"Button","OptionButton","MenuButton"})
        {
            theme.SetStylebox("normal",type,skin.ButtonNormal ?? Box(new("34536b"),10));
            theme.SetStylebox("hover",type,skin.ButtonHover ?? Box(new("476e87"),10));
            theme.SetStylebox("pressed",type,skin.ButtonPressed ?? Box(new("203b50"),10));
            theme.SetStylebox("disabled",type,skin.ButtonDisabled ?? Box(new("58616a"),10));
            var focus=Box(Colors.Transparent,0); focus.BorderColor=skin.Gold; focus.SetBorderWidthAll(2); theme.SetStylebox("focus",type,skin.ButtonFocus ?? focus);
            foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_focus_color"}) theme.SetColor(state,type,skin.LightText);
            theme.SetColor("font_disabled_color",type,skin.DisabledText);
        }
        foreach(var type in new[]{"TabContainer","TabBar"})
        {
            theme.SetStylebox("panel",type,new StyleBoxEmpty());
            theme.SetStylebox("tab_selected",type,skin.TabSelected ?? Box(skin.Gold,10)); theme.SetStylebox("tab_unselected",type,skin.TabNormal ?? Box(skin.Navy,10));
            theme.SetStylebox("tab_disabled",type,skin.TabDisabled ?? Box(skin.Navy.Darkened(.2f),10));
            theme.SetColor("font_selected_color",type,skin.Ink); theme.SetColor("font_unselected_color",type,skin.LightText);
        }
        theme.SetStylebox("normal","LineEdit",skin.InputNormal ?? Box(new("f8ecd4"),8));
        theme.SetStylebox("focus","LineEdit",skin.InputFocus ?? Box(Colors.Transparent,0)); theme.SetColor("font_color","LineEdit",skin.Ink); theme.SetColor("font_placeholder_color","LineEdit",skin.MutedInk);
        theme.SetStylebox("panel","PopupMenu",Box(skin.Navy)); theme.SetColor("font_color","PopupMenu",skin.LightText);
        theme.SetStylebox("panel","TooltipPanel",skin.TooltipPanel ?? Box(new("263b50"),10)); theme.SetColor("font_color","TooltipLabel",skin.LightText);
        theme.SetStylebox("background","ProgressBar",Box(new("243949"),0)); theme.SetStylebox("fill","ProgressBar",Box(skin.Health,0));
        theme.SetConstant("separation","VBoxContainer",skin.Spacing); theme.SetConstant("separation","HBoxContainer",skin.Spacing);
        theme.SetConstant("h_separation","GridContainer",skin.Spacing); theme.SetConstant("v_separation","GridContainer",skin.Spacing);
        return theme;
    }
    public static Label Text(string text,int size=16)
    { var label=new Label { Text=text,MouseFilter=Control.MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size",size); if(size>=22 && UiAssets.Skin.HeadingFont is {} font) label.AddThemeFontOverride("font",font); return label; }
    public static Button Button(string text,Action action) { var button=new Button { Text=text }; button.Pressed+=action; return button; }
    public static string SkillName(ushort id) => id switch { 1=>"Снаряд",2=>"Область",3=>"Рывок",5=>"Болт открытия",6=>"Импульс",_=>"Навык" };
}
