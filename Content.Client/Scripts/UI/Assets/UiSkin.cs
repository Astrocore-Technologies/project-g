using Godot;
namespace ProjectG.UI;

/// <summary>Artist-facing UI resources. Nine-patch margins belong to supplied StyleBoxTextures.</summary>
[GlobalClass]
public partial class UiSkin : Resource
{
    [Export] public Color Paper { get; set; } = new("efddba");
    [Export] public Color Ink { get; set; } = new("3a3026");
    [Export] public Color Navy { get; set; } = new("293d50ee");
    [Export] public Color LightText { get; set; } = new("f4e9d3");
    [Export] public Color Gold { get; set; } = new("d6ad63");
    [Export] public Color MutedInk { get; set; } = new("766754");
    [Export] public Color DisabledText { get; set; } = new("c3c1ba");
    [Export] public Color IconTint { get; set; } = Colors.White;
    [Export] public Color Health { get; set; } = new("69b35b");
    [Export] public Color Mana { get; set; } = new("4dadd1");
    [Export] public Color Stamina { get; set; } = new("d8bc72");
    [Export(PropertyHint.Range,"12,24,1")] public int FontSize { get; set; } = 16;
    [Export(PropertyHint.Range,"4,24,1")] public int Spacing { get; set; } = 8;
    [Export] public Font? Font { get; set; }
    [Export] public Font? HeadingFont { get; set; }
    [Export] public StyleBox? PaperPanel { get; set; }
    [Export] public StyleBox? DarkPanel { get; set; }
    [Export] public StyleBox? ButtonNormal { get; set; }
    [Export] public StyleBox? ButtonHover { get; set; }
    [Export] public StyleBox? ButtonPressed { get; set; }
    [Export] public StyleBox? ButtonDisabled { get; set; }
    [Export] public StyleBox? SlotNormal { get; set; }
    [Export] public StyleBox? SlotHover { get; set; }
    [Export] public StyleBox? SlotSelected { get; set; }
    [Export] public StyleBox? ButtonFocus { get; set; }
    [Export] public StyleBox? TabNormal { get; set; }
    [Export] public StyleBox? TabSelected { get; set; }
    [Export] public StyleBox? TabDisabled { get; set; }
    [Export] public StyleBox? InputNormal { get; set; }
    [Export] public StyleBox? InputFocus { get; set; }
    [Export] public StyleBox? TooltipPanel { get; set; }
    [Export] public StyleBox? GaugeBackground { get; set; }
    [Export] public StyleBox? HealthFill { get; set; }
    [Export] public StyleBox? ManaFill { get; set; }
    [Export] public StyleBox? StaminaFill { get; set; }
    [Export] public StyleBox? ExperienceFill { get; set; }
    [Export] public Texture2D? PortraitFrame { get; set; }
    [Export] public Texture2D? MinimapFrame { get; set; }
}
