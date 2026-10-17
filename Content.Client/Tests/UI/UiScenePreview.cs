using Godot;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Offline fixtures instantiate the shipped scenes. No world or network session is created.</summary>
public partial class UiScenePreview : Node
{
    [Export] public bool ShowHud { get; set; }
    [Export] public Texture2D? PreviewPortrait { get; set; }
    private const string SettingsPath = "res://../.artifacts/ui-scenes/preview-settings.cfg";
    private MainMenuView? _menu;

    public override async void _Ready()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath(SettingsPath))!);
            GetWindow().Mode = Window.ModeEnum.Windowed;
            GetWindow().Size = new(1280, 720);
            Control view;
            if (ShowHud)
            {
                view = GD.Load<PackedScene>("res://UI/Screens/PlayerHudView.tscn").Instantiate<Control>();
                AddChild(view);
                // Clearly isolated presentation fixtures, never used by runtime scenes.
                var resources = view.GetNode<Control>("PlayerResources");
                resources.GetNode<TextureRect>("%Art").Texture = PreviewPortrait;
                SetResource(resources, "Health", "HealthText", 82, "82 / 100");
                SetResource(resources, "Mana", "ManaText", 64, "64 / 100");
                SetResource(resources, "Stamina", "StaminaText", 75, "75 / 100");
                SetResource(view, "Experience", "ExperienceText", 30, "Опыт  30 / 100");
                string[] keys = ["Q", "W", "E", "R", "A", "S", "D", "F", "Space"];
                for (var i = 0; i < keys.Length; i++)
                {
                    var slot = view.GetNode<Control>($"Hotbar/Skill{i}");
                    slot.GetNode<Label>("Content/Key").Text = keys[i];
                    slot.GetNode<Label>("Content/SkillName").Text = "—";
                }
            }
            else
            {
                _menu = GD.Load<PackedScene>("res://UI/Screens/MainMenuView.tscn").Instantiate<MainMenuView>();
                _menu.GetNode<ClientSettingsPanel>("%Settings").SettingsPath = SettingsPath;
                AddChild(_menu);
                _menu.EnterRequested += PreviewConnection;
                _menu.CancelRequested += CancelConnection;
                view = _menu;
            }
            await Frames(4);
            if (!OS.GetCmdlineUserArgs().Contains("--smoke")) return;
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                GetWindow().Size = size;
                await Frames(5);
                var bounds = new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size);
                if (ShowHud)
                {
                    var resources = view.GetNode<Control>("PlayerResources");
                    var hotbar = view.GetNode<Control>("Hotbar");
                    Inside(resources, bounds);
                    Inside(hotbar, bounds);
                    if (resources.GetGlobalRect().Position.X > 32 || resources.GetGlobalRect().Position.Y > 32 ||
                        resources.GetGlobalRect().Intersects(hotbar.GetGlobalRect()))
                        throw new InvalidOperationException("Resources must be top-left and separate from the hotbar.");
                }
                else
                {
                    var enter = _menu!.GetNode<Button>("%EnterWorld");
                    Inside(enter, bounds);
                    Inside(_menu.GetNode<Button>("%Quit"), bounds);
                    if (!enter.HasFocus()) throw new InvalidOperationException("Missing initial keyboard focus.");
                }
                await Capture(ShowHud ? $"hud-{size.X}" : $"menu-{size.X}");
                if (_menu is null) continue;
                _menu.OpenSettings();
                await Frames(3);
                var settings = _menu.GetNode<ClientSettingsPanel>("%Settings");
                Inside(settings, bounds);
                if (!settings.GetNode<CheckButton>("%Fullscreen").HasFocus())
                    throw new InvalidOperationException("Settings did not receive keyboard focus.");
                settings.GetNode<HSlider>("%Volume").Value = 37;
                await Capture($"settings-{size.X}");
                _menu.CloseSettings();
                var config = new ConfigFile();
                if (config.Load(SettingsPath) != Error.Ok || config.GetValue("audio", "master_volume").AsDouble() != 37)
                    throw new InvalidOperationException("Local settings did not persist.");
                if (!_menu.GetNode<Button>("%OpenSettings").HasFocus())
                    throw new InvalidOperationException("Settings did not restore keyboard focus.");
                _menu.ShowConnecting();
                await Frames(2);
                if (!_menu.GetNode<Button>("%CancelConnection").HasFocus() || enterVisible(_menu))
                    throw new InvalidOperationException("Pending connection did not isolate its actions.");
                _menu.ShowMenu();
                await Frames(2);
            }
            GD.Print(ShowHud ? "HUD_SCENE_OK: resources top-left, hotbar, bounds at two resolutions." :
                "MENU_SCENE_OK: offline scene, bounds, settings persistence, focus and pending state at two resolutions.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static bool enterVisible(MainMenuView menu) => menu.GetNode<Button>("%EnterWorld").IsVisibleInTree();
    private static void SetResource(Control resources, string bar, string text, double value, string label)
    { resources.GetNode<ProgressBar>($"%{bar}").Value = value; resources.GetNode<Label>($"%{text}").Text = label; }
    private static void Inside(Control control, Rect2 bounds)
    {
        var rect = control.GetGlobalRect();
        if (!bounds.Encloses(rect)) throw new InvalidOperationException($"{control.Name} outside viewport: {rect} / {bounds}");
    }
    private async Task Frames(int count)
    { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture")) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng($"res://../.artifacts/ui-scenes/{name}.png");
        if (error != Error.Ok) throw new InvalidOperationException($"Capture failed: {error}");
    }
    private void PreviewConnection() { _menu!.ShowConnecting(); _menu.SetStatus("Предпросмотр подключения · сервер не используется"); }
    private void CancelConnection() { _menu!.ShowMenu(); _menu.SetStatus("Предпросмотр · вход отменён"); }
    public override void _ExitTree()
    {
        if (_menu is null) return;
        _menu.EnterRequested -= PreviewConnection;
        _menu.CancelRequested -= CancelConnection;
    }
}
