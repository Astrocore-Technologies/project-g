using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;

namespace ProjectG.App;

/// <summary>Owns the client session and keeps login failures recoverable from the menu.</summary>
public partial class MainMenu : Node
{
    [Export] public PackedScene WorldScene { get; set; } = null!;
    [Export(PropertyHint.Range, "10,120,1")] public double LoginTimeoutSeconds { get; set; } = 30;
    private WorldController? _world;
    private NetworkClient? _network;
    private MainMenuView _screen = null!;
    private bool _connecting;
    private bool _returnPending;
    private double _remaining;

    public bool IsInWorld => _world is not null && !_screen.Visible;
    public bool IsConnecting => _connecting;
    public string StatusText => _screen.StatusText;

    public override void _Ready()
    {
        _screen = GetNode<MainMenuView>("MenuCanvas/MainMenuView");
        _screen.EnterRequested += EnterWorld;
        _screen.CancelRequested += CancelLogin;
        if (WorldScene is null || !double.IsFinite(LoginTimeoutSeconds) || LoginTimeoutSeconds <= 0)
        {
            _screen.DisableEntry();
            _screen.SetStatus("Не настроена игровая сцена или время ожидания входа.");
            GD.PushError(_screen.StatusText);
        }
    }

    public void EnterWorld()
    {
        if (_world is not null || _connecting || _returnPending || WorldScene is null) return;
        _connecting = true;
        _remaining = LoginTimeoutSeconds;
        _screen.ShowConnecting();
        _screen.SetStatus("Подключение…");
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

    private void OnHandshake(ServerWelcome welcome) => _screen.SetStatus("Загрузка мира…");
    private void OnWorldReady()
    {
        if (_returnPending) return;
        _connecting = false;
        _screen.Hide();
    }
    private void OnDisconnected() => OnFailure(_network?.LoggedOut==true?"Персонаж сохранён. Вы вышли из мира.":_network?.LastConnectionError is { Length: > 0 } message
        ? message : "Соединение с сервером потеряно. Попробуй войти снова.");
    private void OnFailure(string message)
    {
        if (_returnPending) return;
        _returnPending = true;
        _connecting = false;
        _screen.SetStatus(message);
        _screen.Show();
        // Defer destruction: callbacks can run inside LiteNetLib.PollEvents on this same node tree.
        Callable.From(ReturnToMenu).CallDeferred();
    }

    private void ReturnToMenu()
    {
        ReleaseWorld();
        _returnPending = false;
        _screen.ShowMenu();
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
        {
            if (_connecting && @event.IsActionPressed("ui_cancel")) CancelLogin();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        _screen.EnterRequested -= EnterWorld;
        _screen.CancelRequested -= CancelLogin;
        ReleaseWorld(remove: false);
    }
}
