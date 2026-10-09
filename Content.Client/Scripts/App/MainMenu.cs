using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;

namespace ProjectG.App;

/// <summary>Owns the client session and keeps login failures recoverable from the menu.</summary>
public partial class MainMenu : Node
{
    [Export] public PackedScene WorldScene { get; set; } = null!;
    [Export(PropertyHint.Range, "10,120,1")] public double LoginTimeoutSeconds { get; set; } = 30;
    private WorldController? _world;
    private NetworkClient? _network;
    private Control _screen = null!;
    private VBoxContainer _menu = null!;
    private VBoxContainer _settings = null!;
    private Button _enter = null!;
    private Button _cancel = null!;
    private Label _status = null!;
    private bool _connecting;
    private bool _returnPending;
    private double _remaining;

    public bool IsInWorld => _world is not null && !_screen.Visible;
    public bool IsConnecting => _connecting;
    public string StatusText => _status.Text;

    public override void _Ready()
    {
        BuildMenu();
        if (WorldScene is null || !double.IsFinite(LoginTimeoutSeconds) || LoginTimeoutSeconds <= 0)
        {
            _enter.Disabled = true;
            _status.Text = "Не настроена игровая сцена или время ожидания входа.";
            GD.PushError(_status.Text);
        }
    }

    private void BuildMenu()
    {
        // A cheap native gradient works on Mobile without external art or a live world connection.
        var canvas = new CanvasLayer { Layer = 200 };
        AddChild(canvas);
        _screen = new Control();
        canvas.AddChild(_screen);
        _screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var background = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient
                {
                    Colors = [new Color("101f2b"), new Color("30494a"), new Color("101820")],
                    Offsets = [0f, .65f, 1f]
                },
                FillFrom = Vector2.Zero, FillTo = Vector2.One,
                Width = 256, Height = 256
            },
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _screen.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer();
        _screen.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 32);
        var center = new CenterContainer();
        margin.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.04f, .08f, .11f, .92f),
            ContentMarginLeft = 28, ContentMarginRight = 28,
            ContentMarginTop = 28, ContentMarginBottom = 28,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12
        });
        center.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 18);
        panel.AddChild(content);
        var title = new Label { Text = "PROJECT G", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        content.AddChild(title);
        content.AddChild(new Label
        {
            Text = "Мир замечает то, что ты делаешь",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("b6cbbb")
        });
        _menu = new VBoxContainer();
        _menu.AddThemeConstantOverride("separation", 10);
        content.AddChild(_menu);
        _enter = AddButton(_menu, "Войти в мир", EnterWorld);
        AddButton(_menu, "Настройки", ShowSettings);
        AddButton(_menu, "Выход", QuitGame);
        _settings = new VBoxContainer { Visible = false };
        _settings.AddThemeConstantOverride("separation", 10);
        content.AddChild(_settings);
        var fullscreen = new CheckButton
        {
            Text = "Полный экран",
            ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen
        };
        fullscreen.Toggled += enabled => DisplayServer.WindowSetMode(enabled
            ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        _settings.AddChild(fullscreen);
        _settings.AddChild(new Label { Text = "Громкость" });
        var volume = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1,
            Value = AudioServer.GetBusVolumeLinear(0) * 100
        };
        volume.ValueChanged += value => AudioServer.SetBusVolumeLinear(0, (float)(value / 100));
        _settings.AddChild(volume);
        AddButton(_settings, "Назад", HideSettings);
        _status = new Label
        {
            Text = "Готов к входу",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300, 48),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.AddChild(_status);
        _cancel = AddButton(content, "Отмена", CancelLogin);
        _cancel.Visible = false;
        content.AddChild(new Label
        {
            Text = $"Development · {typeof(MainMenu).Assembly.GetName().Version}",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("849a9c")
        });
        _enter.GrabFocus();
    }

    private static Button AddButton(Node parent, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 48) };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    public void EnterWorld()
    {
        if (_world is not null || _connecting || _returnPending || WorldScene is null) return;
        _connecting = true;
        _remaining = LoginTimeoutSeconds;
        _enter.Disabled = true;
        _menu.Hide();
        _settings.Hide();
        _cancel.Show();
        _status.Text = "Подключение…";
        try
        {
            _world = WorldScene.Instantiate<WorldController>();
            _world.AutoConnect = false;
            // Bind before connect; the world's _Ready must subscribe before any packet is polled.
            _world.WorldReady += OnWorldReady;
            AddChild(_world);
            _network = _world.GetNode<NetworkClient>("NetworkClient");
            _network.HandshakeCompleted += OnHandshake;
            _network.ConnectionFailed += OnFailure;
            _network.Disconnected += OnDisconnected;
            _network.ConnectToServer();
        }
        catch (Exception error)
        {
            GD.PushError($"Could not start world scene: {error.Message}");
            OnFailure("Не удалось загрузить игровую сцену.");
        }
    }

    private void OnHandshake(ServerWelcome welcome) => _status.Text = "Загрузка мира…";
    private void OnWorldReady()
    {
        if (_returnPending) return;
        _connecting = false;
        _screen.Hide();
    }
    private void OnDisconnected() => OnFailure(_network?.LastConnectionError is { Length: > 0 } message
        ? message : "Соединение с сервером потеряно. Попробуй войти снова.");
    private void OnFailure(string message)
    {
        if (_returnPending) return;
        _returnPending = true;
        _connecting = false;
        _status.Text = message;
        _screen.Show();
        // Defer destruction: callbacks can run inside LiteNetLib.PollEvents on this same node tree.
        Callable.From(ReturnToMenu).CallDeferred();
    }

    private void ReturnToMenu()
    {
        ReleaseWorld();
        _returnPending = false;
        _cancel.Hide();
        _settings.Hide();
        _menu.Show();
        _enter.Disabled = false;
        _enter.GrabFocus();
    }

    private void ReleaseWorld(bool remove = true)
    {
        if (_network is not null)
        {
            _network.HandshakeCompleted -= OnHandshake;
            _network.ConnectionFailed -= OnFailure;
            _network.Disconnected -= OnDisconnected;
            _network = null;
        }
        if (_world is null) return;
        _world.WorldReady -= OnWorldReady;
        // Removing now invokes _ExitTree and stops networking before another login can start.
        if (remove)
        {
            if (_world.GetParent() == this) RemoveChild(_world);
            _world.QueueFree();
        }
        _world = null;
    }

    private void CancelLogin() => OnFailure("Вход отменён.");
    private void ShowSettings() { _menu.Hide(); _settings.Show(); }
    private void HideSettings() { _settings.Hide(); _menu.Show(); _enter.GrabFocus(); }
    private void QuitGame() => GetTree().Quit();

    public override void _Process(double delta)
    {
        if (!_connecting) return;
        _remaining -= delta;
        if (_remaining <= 0) OnFailure("Сервер не ответил вовремя. Проверь, что он запущен, и попробуй снова.");
    }

    public override void _Input(InputEvent @event)
    {
        // GUI blocks pointer events; consume gameplay keys while networking continues behind loading.
        if ((_connecting || _returnPending) && @event is InputEventKey)
            GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree() => ReleaseWorld(remove: false);
}
